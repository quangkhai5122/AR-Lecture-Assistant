// ARLabelPlacer.cs — Hỗ trợ multi-label
using System;
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

    [Header("Selection Actions")]
    [SerializeField] private SpeechTranscriptController transcriptController;
    [SerializeField] private HttpPipelineClient httpPipelineClient;
    [SerializeField] private string notesFileName = "lecture_notes.md";
    [SerializeField] private string targetLanguage = "vi";
    [SerializeField] private string llmProvider = "gemini";
    [SerializeField] private bool geminiMockMode = false;

    [Header("Label readability")]
    [SerializeField] private float minScreenSeparationPixels = 96f;
    [SerializeField] private float overlapStepPixels = 72f;
    [SerializeField] private int overlapSearchAttempts = 8;
    [SerializeField] private float labelScreenPaddingPixels = 24f;
    [SerializeField] private float minDistanceScale = 0.75f;
    [SerializeField] private float maxDistanceScale = 1.25f;
    [SerializeField] private int maxLabelCharacters = 1200;
    [SerializeField] private int maxSubtitleCharacters = 220;
    [SerializeField] private bool useGoogleLensOverlayDefaults = true;
    [SerializeField] private bool groupNearbyTextBlocks = false;
    [SerializeField] private bool googleLensSingleOverlay = false;
    [SerializeField] private bool useScreenSpaceTranslationOverlay = true;
    [SerializeField] private bool mergeSameLineTextBlocks = true;
    [SerializeField] private float groupMaxVerticalGapRatio = 0.12f;
    [SerializeField] private Color lensOverlayBackgroundColor = new Color(0.96f, 0.98f, 1f, 0.88f);
    [SerializeField] private Color lensOverlayTextColor = new Color(0.10f, 0.11f, 0.12f, 0.98f);
    [SerializeField] private float lensOverlayWidthExpansion = 1.32f;
    [SerializeField] private float lensOverlayHeightExpansion = 1.28f;
    [SerializeField] private int lensOverlayMaxLines = 3;

    // Quản lý nhiều label cùng lúc
    private List<GameObject> fixedLabels = new List<GameObject>();
    private readonly List<GameObject> screenOverlayLabels = new List<GameObject>();
    private readonly List<Vector2> placedScreenPoints = new List<Vector2>();
    private readonly List<Rect> placedScreenRects = new List<Rect>();
    private GameObject currentSubtitle;
    private Canvas screenOverlayCanvas;
    private GameObject selectedActionMenu;
    private GameObject geminiAnswerPanel;
    private TextMeshProUGUI geminiAnswerText;
    private LectureNotesService fallbackNotesService;
    private bool translationsVisible = true;

    // Cache pose plane cuối cùng — dùng làm fallback khi raycast miss
    private Pose? cachedPlanePose = null;

    public bool AreTranslationsVisible => translationsVisible;

    /// <summary>
    /// Gọi khi detect plane thành công — lưu pose để dùng khi camera di gần
    /// </summary>
    public void CachePlanePose(Pose pose)
    {
        cachedPlanePose = pose;
        Debug.Log($"[ARLabelPlacer] Cached plane pose at {pose.position}");
    }

    private void ApplyGoogleLensOverlayDefaults()
    {
        if (!useGoogleLensOverlayDefaults)
        {
            return;
        }

        useScreenSpaceTranslationOverlay = true;
        mergeSameLineTextBlocks = true;
        groupNearbyTextBlocks = false;
        googleLensSingleOverlay = false;
        lensOverlayWidthExpansion = Mathf.Max(lensOverlayWidthExpansion, 1.32f);
        lensOverlayHeightExpansion = Mathf.Max(lensOverlayHeightExpansion, 1.28f);
        lensOverlayMaxLines = Mathf.Clamp(lensOverlayMaxLines, 1, 3);
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

        Vector2 resolvedScreenPos = ResolveNonOverlappingScreenPoint(screenPos, translatedText);
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
            RegisterPlacedLabel(placedScreenPos, translatedText);
            return true;
        }

        return false;
    }

    public int PlacePipelineLabels(PipelineResponse response)
    {
        if (response == null || response.blocks == null) return 0;

        ApplyGoogleLensOverlayDefaults();
        ClearFixedLabels();

        List<TranslationLabelGroup> labelGroups = BuildTranslationLabelGroups(response);
        if (useScreenSpaceTranslationOverlay)
        {
            int overlayPlaced = 0;
            foreach (TranslationLabelGroup group in labelGroups)
            {
                if (CreateScreenOverlayLabel(group))
                {
                    overlayPlaced++;
                }
            }

            return overlayPlaced;
        }

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

        for (int groupIndex = 0; groupIndex < labelGroups.Count; groupIndex++)
        {
            TranslationLabelGroup group = labelGroups[groupIndex];
            Vector2 imagePoint = group.ImagePoint;
            Vector2 screenPoint = group.ScreenPoint;
            string text = group.Text;
            Vector2 resolvedScreenPoint = ResolveNonOverlappingScreenPoint(screenPoint, text);
            Vector2 resolvedImagePoint = ScreenPointToImagePoint(resolvedScreenPoint, response.image_width, response.image_height);
            bool labelPlaced = false;

            // Path A: SurfaceMapper (homography)
            if (surfaceMapper != null && surfaceMapper.TryMapImagePointToPose(resolvedImagePoint, out Pose surfacePose))
            {
                labelPlaced = CreateFixedLabel(text, surfacePose, group.ScreenSize);
                if (labelPlaced) RegisterPlacedLabel(resolvedScreenPoint, text);
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
                                 forward * groupIndex * 0.04f;

                Pose offsetPose = new Pose(basePose.position + offset, basePose.rotation);
                labelPlaced = CreateFixedLabel(text, offsetPose, group.ScreenSize);
                if (labelPlaced)
                {
                    RegisterPlacedLabel(resolvedScreenPoint, text);
                    Debug.Log($"[ARLabelPlacer] Group {groupIndex} placed via fallback pose");
                }
            }

            if (labelPlaced) placed++;
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

    public bool HasPlacedTranslations()
    {
        if (currentSubtitle != null) return true;

        foreach (GameObject label in fixedLabels)
        {
            if (label != null) return true;
        }

        foreach (GameObject overlay in screenOverlayLabels)
        {
            if (overlay != null) return true;
        }

        return false;
    }

    public void SetTranslationsVisible(bool visible)
    {
        translationsVisible = visible;

        if (!translationsVisible)
        {
            HideTranslationActionMenu();
            HideGeminiAnswerPanel();
        }

        ApplyTranslationVisibility();
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

    private List<TranslationLabelGroup> BuildTranslationLabelGroups(PipelineResponse response)
    {
        var items = new List<TranslationLabelItem>();
        if (response?.blocks == null) return new List<TranslationLabelGroup>();

        foreach (PipelineBlock block in response.blocks)
        {
            if (block == null || block.bbox == null || block.bbox.Length < 4) continue;

            string text = string.IsNullOrWhiteSpace(block.translated_text)
                ? block.source_text
                : block.translated_text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            Rect bbox = BBoxToImageRect(block.bbox);
            if (bbox.width <= 0f || bbox.height <= 0f) continue;

            Vector2 imagePoint = bbox.center;
            items.Add(new TranslationLabelItem
            {
                Text = text.Trim(),
                BBox = bbox,
                ImagePoint = imagePoint,
                ScreenPoint = ImagePointToScreenPoint(imagePoint, response.image_width, response.image_height),
                ScreenSize = ImageRectToScreenSize(bbox, response.image_width, response.image_height)
            });
        }

        items.Sort((a, b) =>
        {
            int yCompare = a.BBox.yMin.CompareTo(b.BBox.yMin);
            return yCompare != 0 ? yCompare : a.BBox.xMin.CompareTo(b.BBox.xMin);
        });

        if (mergeSameLineTextBlocks)
        {
            List<TranslationLabelGroup> lineGroups = BuildSameLineLabelGroups(items, response);
            if (!groupNearbyTextBlocks || lineGroups.Count <= 1)
            {
                return lineGroups;
            }

            items = FlattenGroupsToItems(lineGroups);
        }

        if (!groupNearbyTextBlocks || items.Count <= 1)
        {
            var singleGroups = new List<TranslationLabelGroup>();
            foreach (TranslationLabelItem item in items)
            {
                singleGroups.Add(TranslationLabelGroup.FromItems(new List<TranslationLabelItem> { item }, response, this, false));
            }
            return singleGroups;
        }

        if (googleLensSingleOverlay)
        {
            return new List<TranslationLabelGroup>
            {
                TranslationLabelGroup.FromItems(items, response, this, true)
            };
        }

        var groups = new List<List<TranslationLabelItem>>();
        float maxGap = Mathf.Max(24f, response.image_height * groupMaxVerticalGapRatio);

        foreach (TranslationLabelItem item in items)
        {
            List<TranslationLabelItem> targetGroup = null;
            foreach (List<TranslationLabelItem> group in groups)
            {
                if (CanJoinGroup(item, group, maxGap))
                {
                    targetGroup = group;
                    break;
                }
            }

            if (targetGroup == null)
            {
                targetGroup = new List<TranslationLabelItem>();
                groups.Add(targetGroup);
            }

            targetGroup.Add(item);
        }

        var result = new List<TranslationLabelGroup>();
        foreach (List<TranslationLabelItem> group in groups)
        {
            result.Add(TranslationLabelGroup.FromItems(group, response, this, true));
        }

        return result;
    }

    private List<TranslationLabelGroup> BuildSameLineLabelGroups(List<TranslationLabelItem> items, PipelineResponse response)
    {
        var lineItems = new List<List<TranslationLabelItem>>();
        foreach (TranslationLabelItem item in items)
        {
            List<TranslationLabelItem> bestLine = null;
            float bestDistance = float.MaxValue;

            foreach (List<TranslationLabelItem> line in lineItems)
            {
                if (!CanJoinSameLine(item, line, response.image_width, out float distance)) continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLine = line;
                }
            }

            if (bestLine == null)
            {
                bestLine = new List<TranslationLabelItem>();
                lineItems.Add(bestLine);
            }

            bestLine.Add(item);
            bestLine.Sort((a, b) => a.BBox.xMin.CompareTo(b.BBox.xMin));
        }

        var result = new List<TranslationLabelGroup>();
        foreach (List<TranslationLabelItem> line in lineItems)
        {
            result.Add(TranslationLabelGroup.FromItems(line, response, this, false));
        }

        result.Sort((a, b) =>
        {
            int yCompare = a.BBox.yMin.CompareTo(b.BBox.yMin);
            return yCompare != 0 ? yCompare : a.BBox.xMin.CompareTo(b.BBox.xMin);
        });
        return result;
    }

    private List<TranslationLabelItem> FlattenGroupsToItems(List<TranslationLabelGroup> groups)
    {
        var items = new List<TranslationLabelItem>();
        foreach (TranslationLabelGroup group in groups)
        {
            items.Add(new TranslationLabelItem
            {
                Text = group.Text,
                BBox = group.BBox,
                ImagePoint = group.ImagePoint,
                ScreenPoint = group.ScreenPoint,
                ScreenSize = group.ScreenSize,
                LineCount = group.LineCount
            });
        }
        return items;
    }

    private static bool CanJoinSameLine(
        TranslationLabelItem item,
        List<TranslationLabelItem> line,
        int imageWidth,
        out float centerDistance
    )
    {
        centerDistance = float.MaxValue;
        if (line == null || line.Count == 0) return false;

        Rect union = line[0].BBox;
        for (int i = 1; i < line.Count; i++)
        {
            union = UnionRect(union, line[i].BBox);
        }

        float minBottom = Mathf.Max(union.yMin, item.BBox.yMin);
        float maxTop = Mathf.Min(union.yMax, item.BBox.yMax);
        float overlap = Mathf.Max(0f, maxTop - minBottom);
        float minHeight = Mathf.Max(1f, Mathf.Min(union.height, item.BBox.height));
        bool verticalOverlap = overlap / minHeight >= 0.45f;

        centerDistance = Mathf.Abs(union.center.y - item.BBox.center.y);
        bool centerAligned = centerDistance <= Mathf.Max(union.height, item.BBox.height) * 0.62f;

        float horizontalGap = 0f;
        if (item.BBox.xMin > union.xMax)
        {
            horizontalGap = item.BBox.xMin - union.xMax;
        }
        else if (union.xMin > item.BBox.xMax)
        {
            horizontalGap = union.xMin - item.BBox.xMax;
        }

        float maxHorizontalGap = Mathf.Max(imageWidth * 0.035f, Mathf.Max(union.height, item.BBox.height) * 3.2f);
        return (verticalOverlap || centerAligned) && horizontalGap <= maxHorizontalGap;
    }

    private static bool CanJoinGroup(TranslationLabelItem item, List<TranslationLabelItem> group, float maxGap)
    {
        if (group == null || group.Count == 0) return false;

        Rect union = group[0].BBox;
        for (int i = 1; i < group.Count; i++)
        {
            union = UnionRect(union, group[i].BBox);
        }

        float verticalGap = Mathf.Max(0f, item.BBox.yMin - union.yMax);
        float horizontalOverlap = Mathf.Min(union.xMax, item.BBox.xMax) - Mathf.Max(union.xMin, item.BBox.xMin);
        float minWidth = Mathf.Max(1f, Mathf.Min(union.width, item.BBox.width));
        bool overlapsHorizontally = horizontalOverlap / minWidth > 0.12f;
        bool centersClose = Mathf.Abs(item.BBox.center.x - union.center.x) < Mathf.Max(union.width, item.BBox.width) * 0.65f;

        return verticalGap <= maxGap && (overlapsHorizontally || centersClose);
    }

    private static Rect BBoxToImageRect(float[] bbox)
    {
        float xMin = Mathf.Min(bbox[0], bbox[2]);
        float yMin = Mathf.Min(bbox[1], bbox[3]);
        float xMax = Mathf.Max(bbox[0], bbox[2]);
        float yMax = Mathf.Max(bbox[1], bbox[3]);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Rect UnionRect(Rect a, Rect b)
    {
        return Rect.MinMaxRect(
            Mathf.Min(a.xMin, b.xMin),
            Mathf.Min(a.yMin, b.yMin),
            Mathf.Max(a.xMax, b.xMax),
            Mathf.Max(a.yMax, b.yMax)
        );
    }

    private static Vector2 ImageRectToScreenSize(Rect imageRect, int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return new Vector2(320f, 120f);
        }

        return new Vector2(
            Mathf.Max(72f, imageRect.width / imageWidth * Screen.width),
            Mathf.Max(36f, imageRect.height / imageHeight * Screen.height)
        );
    }

    private bool CreateScreenOverlayLabel(TranslationLabelGroup group)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Text)) return false;

        Canvas canvas = EnsureScreenOverlayCanvas();
        if (canvas == null) return false;

        GameObject panel = new GameObject("LensLineTranslationOverlay");
        panel.transform.SetParent(canvas.transform, false);
        screenOverlayLabels.Add(panel);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        Vector2 overlaySize = ResolveScreenOverlaySize(group);
        rect.sizeDelta = overlaySize;
        Vector2 overlayCenter = ResolveNonOverlappingOverlayCenter(group.ScreenPoint, overlaySize);
        rect.anchoredPosition = overlayCenter;
        placedScreenPoints.Add(overlayCenter);
        placedScreenRects.Add(ScreenRectForOverlay(overlayCenter, overlaySize));

        Image background = panel.AddComponent<Image>();
        background.color = lensOverlayBackgroundColor;
        background.raycastTarget = true;

        Button button = panel.AddComponent<Button>();
        button.targetGraphic = background;
        ColorBlock colors = button.colors;
        colors.normalColor = lensOverlayBackgroundColor;
        colors.highlightedColor = Color.Lerp(lensOverlayBackgroundColor, Color.white, 0.10f);
        colors.pressedColor = Color.Lerp(lensOverlayBackgroundColor, Color.black, 0.12f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = lensOverlayBackgroundColor;
        colors.colorMultiplier = 1f;
        button.colors = colors;

        string selectedText = group.Text.Trim();
        button.onClick.AddListener(() => ShowTranslationActionMenu(selectedText, overlayCenter));

        GameObject textObject = new GameObject("TranslatedText");
        textObject.transform.SetParent(panel.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = ResolveOverlayTextPadding(overlaySize);
        textRect.offsetMax = -ResolveOverlayTextPadding(overlaySize);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = Ellipsize(group.Text, maxLabelCharacters);
        text.color = lensOverlayTextColor;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        text.enableAutoSizing = true;
        text.fontSizeMax = ResolveScreenOverlayFontSize(group);
        text.fontSizeMin = Mathf.Min(6f, text.fontSizeMax);
        text.maxVisibleLines = 99;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = Vector4.zero;
        text.raycastTarget = false;

        ApplyTranslationVisibility(panel);
        return true;
    }

    private Canvas EnsureScreenOverlayCanvas()
    {
        if (screenOverlayCanvas != null) return screenOverlayCanvas;

        GameObject canvasObject = new GameObject("GoogleLensTranslationOverlayCanvas");
        screenOverlayCanvas = canvasObject.AddComponent<Canvas>();
        screenOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        screenOverlayCanvas.sortingOrder = 40;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1f;
        canvasObject.AddComponent<GraphicRaycaster>();

        return screenOverlayCanvas;
    }

    private void ShowTranslationActionMenu(string selectedText, Vector2 screenPoint)
    {
        if (!translationsVisible || string.IsNullOrWhiteSpace(selectedText)) return;

        HideTranslationActionMenu();
        Canvas canvas = EnsureScreenOverlayCanvas();
        if (canvas == null) return;

        selectedActionMenu = new GameObject("TranslationActionMenu");
        selectedActionMenu.transform.SetParent(canvas.transform, false);

        RectTransform rect = selectedActionMenu.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        Vector2 menuSize = new Vector2(Mathf.Min(380f, Screen.width * 0.82f), 88f);
        rect.sizeDelta = menuSize;
        float verticalOffset = screenPoint.y > Screen.height * 0.55f ? -70f : 70f;
        rect.anchoredPosition = ClampOverlayCenter(screenPoint + new Vector2(0f, verticalOffset), menuSize);

        Image background = selectedActionMenu.AddComponent<Image>();
        background.color = new Color(0.025f, 0.030f, 0.040f, 0.96f);

        Shadow shadow = selectedActionMenu.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.34f);
        shadow.effectDistance = new Vector2(0f, -3f);

        HorizontalLayoutGroup layout = selectedActionMenu.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button addButton = CreateOverlayActionButton(
            "AddSelectedTranslationToNotes",
            selectedActionMenu.transform,
            "Add notes",
            new Color(0.10f, 0.70f, 0.55f, 0.98f)
        );
        addButton.onClick.AddListener(() => AddSelectedTranslationToNotes(selectedText));

        Button askButton = CreateOverlayActionButton(
            "AskGeminiAboutTranslation",
            selectedActionMenu.transform,
            "Ask Gemini",
            new Color(0.38f, 0.34f, 0.95f, 0.98f)
        );
        askButton.onClick.AddListener(() => AskGeminiAboutSelectedText(selectedText));
    }

    private Button CreateOverlayActionButton(string name, Transform parent, string label, Color color)
    {
        GameObject buttonObject = new GameObject(name);
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(160f, 56f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = color;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.14f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.14f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.16f, 0.17f, 0.2f, 0.54f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.minHeight = 56f;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = labelObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.color = Color.white;
        text.fontStyle = FontStyles.Bold;
        text.fontSize = 20f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 12f;
        text.fontSizeMax = 20f;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        return button;
    }

    private void AddSelectedTranslationToNotes(string selectedText)
    {
        HideTranslationActionMenu();
        if (string.IsNullOrWhiteSpace(selectedText)) return;

        if (transcriptController == null)
        {
            transcriptController = FindAnyObjectByType<SpeechTranscriptController>();
        }

        if (transcriptController != null)
        {
            transcriptController.AddSelectedTranslationToNote(selectedText);
        }
        else
        {
            if (fallbackNotesService == null)
            {
                fallbackNotesService = new LectureNotesService(notesFileName);
            }

            fallbackNotesService.AppendSection("Slide translation", selectedText);
            Debug.Log("[ARLabelPlacer] Selected translation saved to " + fallbackNotesService.NotesPath);
        }

        ShowGeminiAnswerPanel("Saved to notes", selectedText);
    }

    private async void AskGeminiAboutSelectedText(string selectedText)
    {
        HideTranslationActionMenu();
        if (string.IsNullOrWhiteSpace(selectedText)) return;

        ShowGeminiAnswerPanel("Gemini", "Analyzing selected line...");

        try
        {
            HttpPipelineClient client = ResolveHttpPipelineClient();
            SpeechAskTextResponse response = await client.SendSpeechAskTextAsync(
                selectedText,
                targetLanguage,
                geminiMockMode,
                llmProvider
            );

            string answer = response != null ? response.answer_text : string.Empty;
            ShowGeminiAnswerPanel("Gemini", string.IsNullOrWhiteSpace(answer) ? "No answer returned." : answer);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ARLabelPlacer] Ask Gemini failed: " + ex.Message);
            ShowGeminiAnswerPanel("Gemini error", ex.Message);
        }
    }

    private HttpPipelineClient ResolveHttpPipelineClient()
    {
        if (httpPipelineClient == null)
        {
            httpPipelineClient = FindAnyObjectByType<HttpPipelineClient>();
        }

        if (httpPipelineClient == null)
        {
            httpPipelineClient = gameObject.AddComponent<HttpPipelineClient>();
        }

        return httpPipelineClient;
    }

    private void ShowGeminiAnswerPanel(string title, string body)
    {
        Canvas canvas = EnsureScreenOverlayCanvas();
        if (canvas == null) return;

        if (geminiAnswerPanel == null)
        {
            geminiAnswerPanel = new GameObject("TranslationGeminiAnswerPanel");
            geminiAnswerPanel.transform.SetParent(canvas.transform, false);

            RectTransform rect = geminiAnswerPanel.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.06f, 0.08f);
            rect.anchorMax = new Vector2(0.94f, 0.36f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image background = geminiAnswerPanel.AddComponent<Image>();
            background.color = new Color(0.025f, 0.030f, 0.040f, 0.94f);

            Shadow shadow = geminiAnswerPanel.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
            shadow.effectDistance = new Vector2(0f, -4f);

            geminiAnswerText = CreateAnswerPanelText(geminiAnswerPanel.transform);

            Button closeButton = CreateOverlayActionButton(
                "CloseTranslationGeminiAnswer",
                geminiAnswerPanel.transform,
                "X",
                new Color(0.16f, 0.18f, 0.22f, 0.96f)
            );
            RectTransform closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-12f, -12f);
            closeRect.sizeDelta = new Vector2(52f, 44f);
            Destroy(closeButton.GetComponent<LayoutElement>());
            closeButton.onClick.AddListener(HideGeminiAnswerPanel);
        }

        geminiAnswerPanel.SetActive(true);
        if (geminiAnswerText != null)
        {
            string cleanBody = StripMarkdown(body ?? string.Empty);
            geminiAnswerText.text = (string.IsNullOrWhiteSpace(title) ? "" : title.Trim() + "\n") +
                                    cleanBody.Trim();
        }
    }

    /// <summary>
    /// Loại bỏ markdown formatting từ text LLM trả về (**, *, #, ```, etc.)
    /// </summary>
    private static string StripMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Loại bỏ code blocks ``` ... ```
        text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", m =>
        {
            string inner = m.Value;
            if (inner.Length > 6) inner = inner.Substring(3, inner.Length - 6);
            // Bỏ dòng đầu nếu là tên language (python, csharp, etc.)
            int nl = inner.IndexOf('\n');
            if (nl >= 0 && nl < 20) inner = inner.Substring(nl + 1);
            return inner.Trim();
        });

        // Loại bỏ inline code `text`
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");

        // Loại bỏ bold **text** và __text__
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.+?)__", "$1");

        // Loại bỏ italic *text* và _text_ (cẩn thận không xóa bullet points)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "$1");

        // Loại bỏ headers # ## ### 
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);

        // Chuyển bullet points "* " thành "• "
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^\*\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^-\s+", "• ", System.Text.RegularExpressions.RegexOptions.Multiline);

        return text;
    }

    private TextMeshProUGUI CreateAnswerPanelText(Transform parent)
    {
        GameObject textObject = new GameObject("AnswerText");
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(18f, 14f);
        rect.offsetMax = new Vector2(-76f, -14f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.color = Color.white;
        text.fontSize = 22f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 13f;
        text.fontSizeMax = 22f;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        return text;
    }

    private Vector2 ClampOverlayCenter(Vector2 center, Vector2 size)
    {
        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;
        return new Vector2(
            Mathf.Clamp(center.x, halfWidth + 12f, Screen.width - halfWidth - 12f),
            Mathf.Clamp(center.y, halfHeight + 12f, Screen.height - halfHeight - 12f)
        );
    }

    private void HideTranslationActionMenu()
    {
        if (selectedActionMenu != null)
        {
            Destroy(selectedActionMenu);
            selectedActionMenu = null;
        }
    }

    private void HideGeminiAnswerPanel()
    {
        if (geminiAnswerPanel != null)
        {
            Destroy(geminiAnswerPanel);
            geminiAnswerPanel = null;
            geminiAnswerText = null;
        }
    }

    private Vector2 ResolveScreenOverlaySize(TranslationLabelGroup group)
    {
        Vector2 size = group.ScreenSize;
        int lineCount = ResolveOverlayLineCount(group);

        float width = Mathf.Clamp(size.x * lensOverlayWidthExpansion, 84f, Screen.width * 0.96f);
        float expectedTextHeight = ResolveScreenOverlayFontSize(group) * 1.22f * lineCount + 10f;
        float height = Mathf.Clamp(
            Mathf.Max(size.y * lensOverlayHeightExpansion, expectedTextHeight),
            24f,
            Mathf.Min(132f, Screen.height * 0.20f)
        );

        return new Vector2(width, height);
    }

    private float ResolveScreenOverlayFontSize(TranslationLabelGroup group)
    {
        int lineCount = ResolveOverlayLineCount(group);
        float lineHeight = group.ScreenSize.y / Mathf.Max(1, group.LineCount);
        float sizeByHeight = lineHeight * 0.82f;
        float sizeByWidth = group.ScreenSize.x / Mathf.Max(8f, group.Text.Length * 0.55f);
        float fittedSize = lineCount > 1 ? Mathf.Min(sizeByHeight * 0.92f, sizeByWidth * 1.15f) : Mathf.Min(sizeByHeight, sizeByWidth * 1.25f);
        return Mathf.Clamp(fittedSize, 9f, 24f);
    }

    private int ResolveOverlayLineCount(TranslationLabelGroup group)
    {
        if (group == null || string.IsNullOrWhiteSpace(group.Text)) return 1;

        int explicitLines = Mathf.Max(1, group.LineCount);
        float availableWidth = Mathf.Max(1f, group.ScreenSize.x * lensOverlayWidthExpansion);
        float fontSize = Mathf.Clamp(group.ScreenSize.y * 0.80f, 6f, 28f);
        float approximateTextWidth = group.Text.Length * fontSize * 0.48f;
        int wrappedLines = Mathf.CeilToInt(approximateTextWidth / availableWidth);
        return Mathf.Clamp(Mathf.Max(explicitLines, wrappedLines), 1, Mathf.Max(1, lensOverlayMaxLines));
    }

    private Vector2 ResolveOverlayTextPadding(Vector2 overlaySize)
    {
        return new Vector2(
            Mathf.Clamp(overlaySize.x * 0.025f, 2f, 8f),
            Mathf.Clamp(overlaySize.y * 0.08f, 1f, 5f)
        );
    }

    private Vector2 ClampOverlayCenterToScreen(Vector2 center, Vector2 size)
    {
        float halfWidth = size.x * 0.5f;
        float halfHeight = size.y * 0.5f;
        return new Vector2(
            Mathf.Clamp(center.x, halfWidth, Mathf.Max(halfWidth, Screen.width - halfWidth)),
            Mathf.Clamp(center.y, halfHeight, Mathf.Max(halfHeight, Screen.height - halfHeight))
        );
    }

    private Vector2 ResolveNonOverlappingOverlayCenter(Vector2 desiredCenter, Vector2 size)
    {
        Vector2 clamped = ClampOverlayCenterToScreen(desiredCenter, size);
        Rect initial = ScreenRectForOverlay(clamped, size);
        if (!OverlapsPlacedScreenRect(initial))
        {
            return clamped;
        }

        float step = Mathf.Max(10f, size.y * 0.42f);
        int attempts = Mathf.Max(2, overlapSearchAttempts);
        for (int ring = 1; ring <= attempts; ring++)
        {
            Vector2[] candidates =
            {
                clamped + Vector2.up * step * ring,
                clamped + Vector2.down * step * ring,
                clamped + Vector2.right * step * ring,
                clamped + Vector2.left * step * ring,
            };

            foreach (Vector2 candidate in candidates)
            {
                Vector2 resolved = ClampOverlayCenterToScreen(candidate, size);
                if (!OverlapsPlacedScreenRect(ScreenRectForOverlay(resolved, size)))
                {
                    return resolved;
                }
            }
        }

        return clamped;
    }

    private bool OverlapsPlacedScreenRect(Rect candidate)
    {
        foreach (Rect placed in placedScreenRects)
        {
            if (candidate.Overlaps(placed))
            {
                return true;
            }
        }

        return false;
    }

    private static Rect ScreenRectForOverlay(Vector2 center, Vector2 size)
    {
        return new Rect(
            center.x - size.x * 0.5f,
            center.y - size.y * 0.5f,
            size.x,
            size.y
        );
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

        var textComp = currentSubtitle.GetComponentInChildren<TextMeshProUGUI>(true);
        if (textComp != null)
        {
            textComp.text = Ellipsize(translatedText, maxSubtitleCharacters);
            ConfigureReadableText(textComp, 18f, 30f, 3);
        }

        ARLectureVisualPolish.StyleSubtitle(currentSubtitle);
        ApplyTranslationVisibility(currentSubtitle);
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
        HideTranslationActionMenu();
        HideGeminiAnswerPanel();

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

        foreach (var overlay in screenOverlayLabels)
        {
            if (overlay != null)
            {
                Destroy(overlay);
            }
        }

        fixedLabels.Clear();
        screenOverlayLabels.Clear();
        placedScreenPoints.Clear();
        placedScreenRects.Clear();
    }

    /// <summary>
    /// Xóa tất cả (cả Fixed + Subtitle)
    /// </summary>
    public void ClearAll()
    {
        ClearFixedLabels();
        HideSubtitle();
    }

    private Vector2 ResolveNonOverlappingScreenPoint(Vector2 desiredPoint, string labelText)
    {
        Vector2 clamped = ClampToSafeScreen(desiredPoint);
        if (IsFarEnoughFromPlacedLabels(clamped, labelText)) return clamped;

        Vector2[] directions =
        {
            Vector2.up,
            Vector2.up * 2f,
            Vector2.down,
            Vector2.down * 2f,
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
                if (IsFarEnoughFromPlacedLabels(candidate, labelText)) return candidate;
            }
        }

        return clamped;
    }

    private bool CreateFixedLabel(string translatedText, Pose hitPose)
    {
        return CreateFixedLabel(translatedText, hitPose, Vector2.zero);
    }

    private bool CreateFixedLabel(string translatedText, Pose hitPose, Vector2 targetScreenSize)
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
            ConfigureReadableText(textComp, 14f, 30f, 0);
        }

        ARLectureVisualPolish.StyleLabel(label);
        FitLabelPanel(label, textComp, targetScreenSize);
        fixedLabels.Add(label);
        ApplyTranslationVisibility(label);
        return true;
    }

    private void ApplyTranslationVisibility()
    {
        ApplyTranslationVisibility(currentSubtitle);

        foreach (GameObject label in fixedLabels)
        {
            ApplyTranslationVisibility(label);
        }

        foreach (GameObject overlay in screenOverlayLabels)
        {
            ApplyTranslationVisibility(overlay);
        }
    }

    private void ApplyTranslationVisibility(GameObject target)
    {
        if (target == null) return;
        if (target.activeSelf == translationsVisible) return;

        target.SetActive(translationsVisible);
    }

    private void RegisterPlacedLabel(Vector2 screenPoint, string labelText)
    {
        placedScreenPoints.Add(screenPoint);
        placedScreenRects.Add(EstimateLabelScreenRect(screenPoint, labelText));
    }

    private bool IsFarEnoughFromPlacedLabels(Vector2 candidate, string labelText)
    {
        float minDistanceSquared = minScreenSeparationPixels * minScreenSeparationPixels;
        foreach (Vector2 point in placedScreenPoints)
        {
            if ((candidate - point).sqrMagnitude < minDistanceSquared)
            {
                return false;
            }
        }

        Rect candidateRect = EstimateLabelScreenRect(candidate, labelText);
        foreach (Rect placedRect in placedScreenRects)
        {
            if (candidateRect.Overlaps(placedRect))
            {
                return false;
            }
        }

        return true;
    }

    private Vector2 ClampToSafeScreen(Vector2 point)
    {
        float margin = Mathf.Max(64f, minScreenSeparationPixels * 0.5f);
        return new Vector2(
            Mathf.Clamp(point.x, margin, Screen.width - margin),
            Mathf.Clamp(point.y, margin, Screen.height - margin)
        );
    }

    private Rect EstimateLabelScreenRect(Vector2 center, string labelText)
    {
        string normalizedText = string.IsNullOrWhiteSpace(labelText) ? "" : labelText.Trim();
        int length = normalizedText.Length == 0 ? 24 : Mathf.Min(normalizedText.Length, maxLabelCharacters);
        int lines = EstimateLineCount(normalizedText);
        float width = Mathf.Clamp(260f + length * 4.8f, 320f, Mathf.Min(760f, Screen.width * 0.92f));
        float height = Mathf.Clamp(96f + lines * 42f, 140f, Screen.height * 0.78f);
        width += labelScreenPaddingPixels * 2f;
        height += labelScreenPaddingPixels * 2f;

        return new Rect(
            center.x - width * 0.5f,
            center.y - height * 0.5f,
            width,
            height
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

    private void FitLabelPanel(GameObject label, TextMeshProUGUI textComp, Vector2 targetScreenSize)
    {
        if (label == null || textComp == null) return;

        int length = textComp.text == null ? 0 : textComp.text.Length;
        int lines = EstimateLineCount(textComp.text);
        bool hasTargetSize = targetScreenSize.x > 0f && targetScreenSize.y > 0f;
        float width = hasTargetSize
            ? Mathf.Clamp(targetScreenSize.x * 1.06f, 48f, 620f)
            : Mathf.Clamp(180f + length * 1.8f, 180f, 560f);
        float height = hasTargetSize
            ? Mathf.Clamp(targetScreenSize.y * 1.16f, 24f, 360f)
            : Mathf.Clamp(48f + lines * 26f, 56f, 320f);

        float fontMax = Mathf.Clamp((height - 6f) / Mathf.Max(1, lines) * 0.78f, 6f, 22f);
        textComp.fontSizeMax = fontMax;
        textComp.fontSizeMin = Mathf.Min(6f, fontMax);

        RectTransform textRect = textComp.GetComponent<RectTransform>();
        if (textRect != null)
        {
            textRect.sizeDelta = new Vector2(Mathf.Max(1f, width - 12f), Mathf.Max(1f, height - 8f));
        }

        foreach (Canvas canvas in label.GetComponentsInChildren<Canvas>(true))
        {
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            if (canvasRect != null)
            {
                canvasRect.sizeDelta = new Vector2(width, height);
            }
        }

        foreach (Image image in label.GetComponentsInChildren<Image>(true))
        {
            RectTransform imageRect = image.GetComponent<RectTransform>();
            if (imageRect != null && imageRect.name.ToLowerInvariant().Contains("background"))
            {
                imageRect.offsetMin = Vector2.zero;
                imageRect.offsetMax = Vector2.zero;
                imageRect.sizeDelta = Vector2.zero;
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
        text.maxVisibleLines = maxLines <= 0 ? 99 : maxLines;
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

    private static int EstimateLineCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;

        string[] explicitLines = text.Split('\n');
        int total = 0;
        foreach (string line in explicitLines)
        {
            int length = string.IsNullOrWhiteSpace(line) ? 1 : line.Trim().Length;
            total += Mathf.Max(1, Mathf.CeilToInt(length / 34f));
        }

        return Mathf.Max(1, total);
    }

    private sealed class TranslationLabelItem
    {
        public string Text;
        public Rect BBox;
        public Vector2 ImagePoint;
        public Vector2 ScreenPoint;
        public Vector2 ScreenSize;
        public int LineCount;
    }

    private sealed class TranslationLabelGroup
    {
        public string Text;
        public Rect BBox;
        public Vector2 ImagePoint;
        public Vector2 ScreenPoint;
        public Vector2 ScreenSize;
        public int LineCount;

        public static TranslationLabelGroup FromItems(
            List<TranslationLabelItem> items,
            PipelineResponse response,
            ARLabelPlacer placer,
            bool preserveLineBreaks
        )
        {
            Rect union = items[0].BBox;
            var lines = new List<string>();

            foreach (TranslationLabelItem item in items)
            {
                union = UnionRect(union, item.BBox);
                if (!string.IsNullOrWhiteSpace(item.Text))
                {
                    lines.Add(item.Text.Trim());
                }
            }

            Vector2 imagePoint = union.center;
            string text = preserveLineBreaks ? string.Join("\n", lines) : string.Join(" ", lines);
            return new TranslationLabelGroup
            {
                Text = text,
                BBox = union,
                ImagePoint = imagePoint,
                ScreenPoint = placer.ImagePointToScreenPoint(imagePoint, response.image_width, response.image_height),
                ScreenSize = ImageRectToScreenSize(union, response.image_width, response.image_height),
                LineCount = preserveLineBreaks ? Mathf.Max(1, lines.Count) : 1
            };
        }
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
