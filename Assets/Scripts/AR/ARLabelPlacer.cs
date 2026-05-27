// ARLabelPlacer.cs — Hỗ trợ multi-label
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class ARLabelPlacer : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] private GameObject fixedLabelPrefab;    // World Space label
    [SerializeField] private GameObject subtitlePrefab;       // Screen Space subtitle

    [Header("References")]
    [SerializeField] private ARAnchorPlacer anchorPlacer;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private Transform subtitleContainer;     // Parent cho subtitle

    [Header("Label readability")]
    [SerializeField] private float minScreenSeparationPixels = 96f;
    [SerializeField] private float overlapStepPixels = 72f;
    [SerializeField] private int overlapSearchAttempts = 8;
    [SerializeField] private float minDistanceScale = 0.75f;
    [SerializeField] private float maxDistanceScale = 1.25f;
    [SerializeField] private int maxLabelCharacters = 140;
    [SerializeField] private int maxSubtitleCharacters = 220;

    // Quản lý nhiều label cùng lúc
    private List<GameObject> fixedLabels = new List<GameObject>();
    private readonly List<Vector2> placedScreenPoints = new List<Vector2>();
    private GameObject currentSubtitle;

    // Cache pose plane cuối cùng — dùng làm fallback khi raycast miss
    private Pose? cachedPlanePose = null;

    /// <summary>
    /// Gọi khi detect plane thành công — lưu pose để dùng khi camera di gần
    /// </summary>
    public void CachePlanePose(Pose pose)
    {
        cachedPlanePose = pose;
        Debug.Log($"[ARLabelPlacer] Cached plane pose at {pose.position}");
    }

    /// <summary>
    /// Đặt Fixed Label bám trên slide/bảng tại vị trí tap
    /// Có thể đặt nhiều label cùng lúc
    /// </summary>
    public void PlaceFixedLabel(string translatedText, Vector2 screenPos)
    {
        TryPlaceFixedLabel(translatedText, screenPos);
    }

    public bool TryPlaceFixedLabel(string translatedText, Vector2 screenPos)
    {
        if (fixedLabelPrefab == null)
        {
            Debug.LogWarning("[ARLabelPlacer] fixedLabelPrefab is null.");
            return false;
        }
        if (raycastController == null)
        {
            raycastController = FindAnyObjectByType<ARRaycastController>();
            if (raycastController == null)
            {
                Debug.LogWarning("[ARLabelPlacer] ARRaycastController not found.");
                return false;
            }
        }
        if (anchorPlacer == null)
        {
            anchorPlacer = FindAnyObjectByType<ARAnchorPlacer>();
            if (anchorPlacer == null)
            {
                Debug.LogWarning("[ARLabelPlacer] ARAnchorPlacer not found.");
                return false;
            }
        }

        Vector2 resolvedScreenPos = ResolveNonOverlappingScreenPoint(screenPos);
        bool hit = raycastController.TryRaycast(resolvedScreenPos, out Pose hitPose);
        Vector2 placedScreenPos = resolvedScreenPos;
        if (!hit && resolvedScreenPos != screenPos)
        {
            hit = raycastController.TryRaycast(screenPos, out hitPose);
            placedScreenPos = screenPos;
        }

        if (hit)
        {
            if (!CreateFixedLabel(translatedText, hitPose)) return false;
            placedScreenPoints.Add(placedScreenPos);
            return true;
        }

        return false;
    }

    public int PlacePipelineLabels(PipelineResponse response)
    {
        if (response == null || response.blocks == null) return 0;

        // Auto-find dependencies nếu chưa được gán trong Inspector
        if (raycastController == null)
            raycastController = FindAnyObjectByType<ARRaycastController>();
        if (anchorPlacer == null)
            anchorPlacer = FindAnyObjectByType<ARAnchorPlacer>();
        if (raycastController == null || anchorPlacer == null || fixedLabelPrefab == null)
        {
            Debug.LogWarning($"[ARLabelPlacer] PlacePipelineLabels: missing deps");
            return 0;
        }

        ClearFixedLabels();

        // Pre-cache center raycast — fallback khi per-block raycast miss
        bool hasCenterHit = raycastController.TryRaycastFromCenter(out Pose centerPose);
        if (hasCenterHit)
        {
            cachedPlanePose = centerPose; // Lưu để dùng khi raycast miss sau này
        }

        // Xác định fallback pose (center hit hoặc cached từ lúc detect plane)
        Pose? fallbackPose = hasCenterHit ? centerPose : cachedPlanePose;
        bool hasFallback = fallbackPose.HasValue;

        DocumentSurfaceMapper surfaceMapper = DocumentSurfaceMapper.TryCreate(response, raycastController, BBoxPointToScreenPoint);
        int placed = 0;
        int blockIndex = 0;

        foreach (PipelineBlock block in response.blocks)
        {
            if (block == null || block.bbox == null || block.bbox.Length < 4) continue;

            string text = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            Vector2 imagePoint = BBoxCenterToImagePoint(block.bbox);
            Vector2 screenPoint = ImagePointToScreenPoint(imagePoint, response.image_width, response.image_height);
            Vector2 resolvedScreenPoint = ResolveNonOverlappingScreenPoint(screenPoint);
            Vector2 resolvedImagePoint = ScreenPointToImagePoint(resolvedScreenPoint, response.image_width, response.image_height);
            bool labelPlaced = false;

            // Path A: SurfaceMapper (homography)
            if (surfaceMapper != null && surfaceMapper.TryMapImagePointToPose(resolvedImagePoint, out Pose surfacePose))
            {
                labelPlaced = CreateFixedLabel(text, surfacePose);
                if (labelPlaced) placedScreenPoints.Add(resolvedScreenPoint);
            }

            // Path B: Per-block raycast
            if (!labelPlaced && TryPlaceFixedLabel(text, resolvedScreenPoint))
            {
                labelPlaced = true;
            }

            // Path C+D: Fallback — dùng center raycast hoặc cached plane pose
            if (!labelPlaced && hasFallback)
            {
                Pose basePose = fallbackPose.Value;

                // Tính offset dựa trên vị trí bbox so với center screen
                float normalizedX = (screenPoint.x / Screen.width) - 0.5f;
                float normalizedY = (screenPoint.y / Screen.height) - 0.5f;
                float spreadFactor = 0.3f; // khoảng cách spread (meters)

                Vector3 right = basePose.rotation * Vector3.right;
                Vector3 forward = basePose.rotation * Vector3.forward;
                Vector3 offset = right * normalizedX * spreadFactor +
                                 forward * normalizedY * spreadFactor +
                                 forward * blockIndex * 0.03f;

                Pose offsetPose = new Pose(basePose.position + offset, basePose.rotation);
                labelPlaced = CreateFixedLabel(text, offsetPose);
                if (labelPlaced)
                {
                    placedScreenPoints.Add(resolvedScreenPoint);
                    Debug.Log($"[ARLabelPlacer] Block {blockIndex} placed via fallback pose");
                }
            }

            if (labelPlaced) placed++;
            blockIndex++;
        }

        return placed;
    }

    public int CountReadableBlocks(PipelineResponse response)
    {
        if (response == null || response.blocks == null) return 0;

        int count = 0;
        foreach (PipelineBlock block in response.blocks)
        {
            if (block == null) continue;
            string text = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;

            if (!string.IsNullOrWhiteSpace(text)) count++;
        }

        return count;
    }

    private Vector2 BBoxCenterToImagePoint(float[] bbox)
    {
        float centerX = (bbox[0] + bbox[2]) * 0.5f;
        float centerY = (bbox[1] + bbox[3]) * 0.5f;
        return new Vector2(centerX, centerY);
    }

    private Vector2 BBoxPointToScreenPoint(Vector2 imagePoint, PipelineResponse response)
    {
        if (response == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        return ImagePointToScreenPoint(imagePoint, response.image_width, response.image_height);
    }

    private Vector2 ImagePointToScreenPoint(Vector2 imagePoint, int imageWidth, int imageHeight)
    {
        float screenX = imageWidth > 0
            ? imagePoint.x / imageWidth * Screen.width
            : Screen.width * 0.5f;

        // Backend trả bbox theo gốc top-left; Unity screen point dùng gốc bottom-left.
        float screenY = imageHeight > 0
            ? Screen.height - (imagePoint.y / imageHeight * Screen.height)
            : Screen.height * 0.5f;

        return new Vector2(screenX, screenY);
    }

    private Vector2 ScreenPointToImagePoint(Vector2 screenPoint, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Vector2.zero;
        }

        float imageX = screenPoint.x / Mathf.Max(1f, Screen.width) * imageWidth;
        float imageY = (Screen.height - screenPoint.y) / Mathf.Max(1f, Screen.height) * imageHeight;
        return new Vector2(imageX, imageY);
    }

    /// <summary>
    /// Hiển thị/cập nhật Subtitle ở dưới màn hình
    /// Chỉ có 1 subtitle tại 1 thời điểm
    /// </summary>
    public void ShowSubtitle(string translatedText)
    {
        if (subtitlePrefab == null)
        {
            Debug.LogWarning("[ARLabelPlacer] subtitlePrefab is null, cannot show subtitle.");
            return;
        }

        if (currentSubtitle == null)
        {
            // Nếu subtitleContainer chưa được gán trong Inspector, tìm Canvas đầu tiên
            if (subtitleContainer == null)
            {
                Canvas canvas = FindAnyObjectByType<Canvas>();
                if (canvas != null) subtitleContainer = canvas.transform;
            }
            currentSubtitle = Instantiate(subtitlePrefab, subtitleContainer);
        }

        var textComp = currentSubtitle.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
        {
            textComp.text = Ellipsize(translatedText, maxSubtitleCharacters);
            ConfigureReadableText(textComp, 18f, 30f, 3);
        }

        ARLectureVisualPolish.StyleSubtitle(currentSubtitle);
    }

    /// <summary>
    /// Ẩn subtitle
    /// </summary>
    public void HideSubtitle()
    {
        if (currentSubtitle != null)
        {
            Destroy(currentSubtitle);
            currentSubtitle = null;
        }
    }

    /// <summary>
    /// Xóa tất cả Fixed Labels (giữ subtitle)
    /// </summary>
    public void ClearFixedLabels()
    {
        foreach (var label in fixedLabels)
        {
            if (label != null)
            {
                // Xóa cả anchor parent
                var anchor = label.GetComponentInParent<ARAnchor>();
                if (anchor != null)
                    Destroy(anchor.gameObject);
                else
                    Destroy(label);
            }
        }
        fixedLabels.Clear();
        placedScreenPoints.Clear();
    }

    /// <summary>
    /// Xóa tất cả (cả Fixed + Subtitle)
    /// </summary>
    public void ClearAll()
    {
        ClearFixedLabels();
        HideSubtitle();
    }

    private Vector2 ResolveNonOverlappingScreenPoint(Vector2 desiredPoint)
    {
        Vector2 clamped = ClampToSafeScreen(desiredPoint);
        if (IsFarEnoughFromPlacedLabels(clamped)) return clamped;

        Vector2[] directions =
        {
            Vector2.up,
            Vector2.down,
            Vector2.right,
            Vector2.left,
            new Vector2(1f, 1f).normalized,
            new Vector2(-1f, 1f).normalized,
            new Vector2(1f, -1f).normalized,
            new Vector2(-1f, -1f).normalized
        };

        int attempts = Mathf.Max(1, overlapSearchAttempts);
        for (int ring = 1; ring <= attempts; ring++)
        {
            float distance = overlapStepPixels * ring;
            foreach (Vector2 direction in directions)
            {
                Vector2 candidate = ClampToSafeScreen(desiredPoint + direction * distance);
                if (IsFarEnoughFromPlacedLabels(candidate)) return candidate;
            }
        }

        return clamped;
    }

    private bool CreateFixedLabel(string translatedText, Pose hitPose)
    {
        ARAnchor anchor = anchorPlacer.PlaceAnchor(hitPose);
        if (anchor == null) return false;

        GameObject label = Instantiate(fixedLabelPrefab, anchor.transform);
        label.transform.localPosition = Vector3.zero;
        ApplyDistanceScale(label, hitPose.position);

        var textComp = label.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
        {
            textComp.text = Ellipsize(translatedText, maxLabelCharacters);
            ConfigureReadableText(textComp, 16f, 32f, 4);
        }

        ARLectureVisualPolish.StyleLabel(label);
        FitLabelPanel(label, textComp);
        fixedLabels.Add(label);
        return true;
    }

    private bool IsFarEnoughFromPlacedLabels(Vector2 candidate)
    {
        float minDistanceSquared = minScreenSeparationPixels * minScreenSeparationPixels;
        foreach (Vector2 point in placedScreenPoints)
        {
            if ((candidate - point).sqrMagnitude < minDistanceSquared)
            {
                return false;
            }
        }

        return true;
    }

    private Vector2 ClampToSafeScreen(Vector2 point)
    {
        float margin = Mathf.Max(48f, minScreenSeparationPixels * 0.5f);
        return new Vector2(
            Mathf.Clamp(point.x, margin, Screen.width - margin),
            Mathf.Clamp(point.y, margin, Screen.height - margin)
        );
    }

    private void ApplyDistanceScale(GameObject label, Vector3 worldPosition)
    {
        Camera camera = Camera.main;
        if (camera == null || label == null) return;

        float distance = Vector3.Distance(camera.transform.position, worldPosition);
        float scale = Mathf.Clamp(distance * 0.55f, minDistanceScale, maxDistanceScale);
        label.transform.localScale = Vector3.one * scale;
    }

    private void FitLabelPanel(GameObject label, TextMeshProUGUI textComp)
    {
        if (label == null || textComp == null) return;

        int length = textComp.text == null ? 0 : textComp.text.Length;
        float width = Mathf.Clamp(260f + length * 2.4f, 280f, 520f);
        float height = length > 96 ? 150f : length > 52 ? 124f : 96f;

        RectTransform textRect = textComp.GetComponent<RectTransform>();
        if (textRect != null)
        {
            textRect.sizeDelta = new Vector2(Mathf.Max(textRect.sizeDelta.x, width - 44f), Mathf.Max(textRect.sizeDelta.y, height - 32f));
        }

        foreach (Image image in label.GetComponentsInChildren<Image>(true))
        {
            RectTransform imageRect = image.GetComponent<RectTransform>();
            if (imageRect != null && imageRect.name.ToLowerInvariant().Contains("background"))
            {
                imageRect.sizeDelta = new Vector2(width, height);
            }
        }
    }

    private static void ConfigureReadableText(TextMeshProUGUI text, float minSize, float maxSize, int maxLines)
    {
        if (text == null) return;

        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.enableAutoSizing = true;
        text.fontSizeMin = minSize;
        text.fontSizeMax = maxSize;
        text.maxVisibleLines = maxLines;
        text.alignment = TextAlignmentOptions.Center;
        text.margin = new Vector4(8f, 6f, 8f, 6f);
    }

    private static string Ellipsize(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        string trimmed = value.Trim();
        if (maxCharacters <= 3 || trimmed.Length <= maxCharacters) return trimmed;

        return trimmed.Substring(0, maxCharacters - 1).TrimEnd() + "…";
    }

    private sealed class DocumentSurfaceMapper
    {
        private readonly Vector2[] imageCorners;
        private readonly Vector3[] worldCorners;
        private readonly Quaternion rotation;

        private DocumentSurfaceMapper(Vector2[] imageCorners, Vector3[] worldCorners, Quaternion rotation)
        {
            this.imageCorners = imageCorners;
            this.worldCorners = worldCorners;
            this.rotation = rotation;
        }

        public static DocumentSurfaceMapper TryCreate(
            PipelineResponse response,
            ARRaycastController raycastController,
            System.Func<Vector2, PipelineResponse, Vector2> imageToScreenPoint)
        {
            if (response?.document_surface?.corners == null ||
                response.document_surface.corners.Length < 8 ||
                raycastController == null ||
                imageToScreenPoint == null)
            {
                return null;
            }

            Vector2[] imageCorners = ParseCorners(response.document_surface.corners);
            Vector3[] worldCorners = new Vector3[4];
            Quaternion rotation = Quaternion.identity;

            for (int i = 0; i < imageCorners.Length; i++)
            {
                Vector2 screenPoint = imageToScreenPoint(imageCorners[i], response);
                if (!raycastController.TryRaycast(screenPoint, out Pose hitPose))
                {
                    return null;
                }

                worldCorners[i] = hitPose.position;
                if (i == 0) rotation = hitPose.rotation;
            }

            return new DocumentSurfaceMapper(imageCorners, worldCorners, rotation);
        }

        public bool TryMapImagePointToPose(Vector2 imagePoint, out Pose pose)
        {
            pose = Pose.identity;
            if (!TryImagePointToSurfaceUv(imagePoint, out Vector2 uv))
            {
                return false;
            }

            Vector3 top = Vector3.Lerp(worldCorners[0], worldCorners[1], uv.x);
            Vector3 bottom = Vector3.Lerp(worldCorners[3], worldCorners[2], uv.x);
            Vector3 position = Vector3.Lerp(top, bottom, uv.y);
            pose = new Pose(position, rotation);
            return true;
        }

        private bool TryImagePointToSurfaceUv(Vector2 imagePoint, out Vector2 uv)
        {
            uv = Vector2.zero;
            double[,] matrix =
            {
                { imageCorners[0].x, imageCorners[0].y, 1.0, 0.0, 0.0, 0.0, -0.0 * imageCorners[0].x, -0.0 * imageCorners[0].y },
                { 0.0, 0.0, 0.0, imageCorners[0].x, imageCorners[0].y, 1.0, -0.0 * imageCorners[0].x, -0.0 * imageCorners[0].y },
                { imageCorners[1].x, imageCorners[1].y, 1.0, 0.0, 0.0, 0.0, -1.0 * imageCorners[1].x, -1.0 * imageCorners[1].y },
                { 0.0, 0.0, 0.0, imageCorners[1].x, imageCorners[1].y, 1.0, -0.0 * imageCorners[1].x, -0.0 * imageCorners[1].y },
                { imageCorners[2].x, imageCorners[2].y, 1.0, 0.0, 0.0, 0.0, -1.0 * imageCorners[2].x, -1.0 * imageCorners[2].y },
                { 0.0, 0.0, 0.0, imageCorners[2].x, imageCorners[2].y, 1.0, -1.0 * imageCorners[2].x, -1.0 * imageCorners[2].y },
                { imageCorners[3].x, imageCorners[3].y, 1.0, 0.0, 0.0, 0.0, -0.0 * imageCorners[3].x, -0.0 * imageCorners[3].y },
                { 0.0, 0.0, 0.0, imageCorners[3].x, imageCorners[3].y, 1.0, -1.0 * imageCorners[3].x, -1.0 * imageCorners[3].y },
            };
            double[] values = { 0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0 };

            if (!SolveLinearSystem(matrix, values, out double[] h))
            {
                return false;
            }

            double denominator = h[6] * imagePoint.x + h[7] * imagePoint.y + 1.0;
            if (System.Math.Abs(denominator) < 0.000001)
            {
                return false;
            }

            float u = (float)((h[0] * imagePoint.x + h[1] * imagePoint.y + h[2]) / denominator);
            float v = (float)((h[3] * imagePoint.x + h[4] * imagePoint.y + h[5]) / denominator);
            uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
            return true;
        }

        private static Vector2[] ParseCorners(float[] corners)
        {
            return new[]
            {
                new Vector2(corners[0], corners[1]),
                new Vector2(corners[2], corners[3]),
                new Vector2(corners[4], corners[5]),
                new Vector2(corners[6], corners[7]),
            };
        }

        private static bool SolveLinearSystem(double[,] matrix, double[] values, out double[] solution)
        {
            int size = values.Length;
            solution = new double[size];
            double[,] augmented = new double[size, size + 1];

            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    augmented[row, column] = matrix[row, column];
                }
                augmented[row, size] = values[row];
            }

            for (int pivot = 0; pivot < size; pivot++)
            {
                int bestRow = pivot;
                double bestValue = System.Math.Abs(augmented[pivot, pivot]);
                for (int row = pivot + 1; row < size; row++)
                {
                    double candidate = System.Math.Abs(augmented[row, pivot]);
                    if (candidate > bestValue)
                    {
                        bestValue = candidate;
                        bestRow = row;
                    }
                }

                if (bestValue < 0.0000001)
                {
                    return false;
                }

                if (bestRow != pivot)
                {
                    for (int column = pivot; column <= size; column++)
                    {
                        (augmented[pivot, column], augmented[bestRow, column]) =
                            (augmented[bestRow, column], augmented[pivot, column]);
                    }
                }

                double divisor = augmented[pivot, pivot];
                for (int column = pivot; column <= size; column++)
                {
                    augmented[pivot, column] /= divisor;
                }

                for (int row = 0; row < size; row++)
                {
                    if (row == pivot) continue;

                    double factor = augmented[row, pivot];
                    for (int column = pivot; column <= size; column++)
                    {
                        augmented[row, column] -= factor * augmented[pivot, column];
                    }
                }
            }

            for (int row = 0; row < size; row++)
            {
                solution[row] = augmented[row, size];
            }

            return true;
        }
    }
}
