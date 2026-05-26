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
        if (raycastController == null || anchorPlacer == null || fixedLabelPrefab == null)
        {
            Debug.LogWarning("[ARLabelPlacer] Missing AR label dependencies.");
            return false;
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
            // Tạo anchor để text bám ổn định
            ARAnchor anchor = anchorPlacer.PlaceAnchor(hitPose);
            if (anchor == null) return false;

            // Instantiate label dưới anchor
            GameObject label = Instantiate(fixedLabelPrefab, anchor.transform);
            label.transform.localPosition = Vector3.zero;
            ApplyDistanceScale(label, hitPose.position);

            // Set text
            var textComp = label.GetComponentInChildren<TextMeshProUGUI>();
            if (textComp != null)
            {
                textComp.text = Ellipsize(translatedText, maxLabelCharacters);
                ConfigureReadableText(textComp, 16f, 32f, 4);
            }

            ARLectureVisualPolish.StyleLabel(label);
            FitLabelPanel(label, textComp);
            fixedLabels.Add(label);
            placedScreenPoints.Add(placedScreenPos);
            return true;
        }

        return false;
    }

    public int PlacePipelineLabels(PipelineResponse response)
    {
        if (response == null || response.blocks == null) return 0;

        ClearFixedLabels();
        int placed = 0;
        foreach (PipelineBlock block in response.blocks)
        {
            if (block == null || block.bbox == null || block.bbox.Length < 4) continue;

            string text = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            Vector2 screenPoint = BBoxCenterToScreenPoint(block.bbox, response.image_width, response.image_height);
            if (TryPlaceFixedLabel(text, screenPoint))
            {
                placed++;
            }
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

    private Vector2 BBoxCenterToScreenPoint(float[] bbox, int imageWidth, int imageHeight)
    {
        float centerX = (bbox[0] + bbox[2]) * 0.5f;
        float centerY = (bbox[1] + bbox[3]) * 0.5f;

        float screenX = imageWidth > 0
            ? centerX / imageWidth * Screen.width
            : Screen.width * 0.5f;

        // Backend trả bbox theo gốc top-left; Unity screen point dùng gốc bottom-left.
        float screenY = imageHeight > 0
            ? Screen.height - (centerY / imageHeight * Screen.height)
            : Screen.height * 0.5f;

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// Hiển thị/cập nhật Subtitle ở dưới màn hình
    /// Chỉ có 1 subtitle tại 1 thời điểm
    /// </summary>
    public void ShowSubtitle(string translatedText)
    {
        if (currentSubtitle == null)
        {
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
}
