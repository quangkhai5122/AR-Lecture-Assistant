// ButtonController.cs
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public enum BackendPipelineMode
{
    PipelineFrame,
    PipelineAlias,
    SplitOcrTranslate
}

public class ButtonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StateManager stateManager;
    [SerializeField] private ARLabelPlacer labelPlacer;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private ARSurfaceLockController surfaceLockController;
    [SerializeField] private MockTranslationService translationService;
    [SerializeField] private ARPlaneManager arPlaneManager;

    [Header("OCR / Translate Pipeline")]
    [SerializeField] private bool useBackendPipeline = true;
    [SerializeField] private bool backendMockMode = true;
    [SerializeField] private string targetLanguage = "vi";
    [SerializeField] private BackendPipelineMode backendPipelineMode = BackendPipelineMode.PipelineFrame;
    [SerializeField] private string ocrProvider = "";
    [SerializeField] private string translationProvider = "mock";
    [SerializeField] private FrameCaptureService frameCaptureService;
    [SerializeField] private HttpPipelineClient httpPipelineClient;

    [Header("Buttons")]
    [SerializeField] private Button scanButton;
    [SerializeField] private Button translateButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button hideTranslationsButton;
    [SerializeField] private Button freezeButton;

    [Header("UI Mode")]
    [SerializeField] private bool useCompactDemoControls = false;
    [SerializeField] private bool showTranslationVisibilityButton = true;

    [Header("Debug")]
    [SerializeField] private bool showAdvancedControls = false;
    [SerializeField] private DebugPanelController debugPanel;

    private bool isFrozen = false;

    private void Awake()
    {
        ApplyControlVisibility();
    }

    private void Start()
    {
        ResolveButtonReferences();

        if (arPlaneManager == null)
        {
            arPlaneManager = FindAnyObjectByType<ARPlaneManager>();
        }
        ResolveSurfaceLockController();
        if (frameCaptureService == null)
        {
            frameCaptureService = GetComponent<FrameCaptureService>();
            if (frameCaptureService == null) frameCaptureService = gameObject.AddComponent<FrameCaptureService>();
        }
        if (httpPipelineClient == null)
        {
            httpPipelineClient = GetComponent<HttpPipelineClient>();
            if (httpPipelineClient == null) httpPipelineClient = gameObject.AddComponent<HttpPipelineClient>();
        }

        if (showTranslationVisibilityButton)
        {
            EnsureHideTranslationsButton();
        }
        else
        {
            SetButtonObjectActive(hideTranslationsButton, false);
            SetNamedObjectActive("HideTranslationsButton", false);
        }

        if (scanButton != null) scanButton.onClick.AddListener(OnScanPressed);
        if (translateButton != null) translateButton.onClick.AddListener(OnTranslatePressed);
        if (clearButton != null) clearButton.onClick.AddListener(OnClearPressed);
        if (hideTranslationsButton != null)
        {
            hideTranslationsButton.onClick.AddListener(OnHideTranslationsPressed);
        }
        if (freezeButton != null)
        {
            freezeButton.onClick.AddListener(OnFreezePressed);
        }
        if (stateManager != null) stateManager.OnStateChanged.AddListener(OnStateChanged);

        ApplyControlVisibility();
        UpdateButtonStates();
        UpdatePrimaryActionLabel();
        UpdateTranslateActionLabel();
        SetButtonLabel(clearButton, "Xóa");
        UpdateFreezeVisual();
        UpdateHideTranslationsVisual();
        debugPanel?.UpdateTrackingState("Idle");
        _ = CheckBackendHealthAsync();
    }

    private void OnDestroy()
    {
        if (scanButton != null) scanButton.onClick.RemoveListener(OnScanPressed);
        if (translateButton != null) translateButton.onClick.RemoveListener(OnTranslatePressed);
        if (clearButton != null) clearButton.onClick.RemoveListener(OnClearPressed);
        if (hideTranslationsButton != null) hideTranslationsButton.onClick.RemoveListener(OnHideTranslationsPressed);
        if (freezeButton != null) freezeButton.onClick.RemoveListener(OnFreezePressed);
        if (stateManager != null) stateManager.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnScanPressed()
    {
        if (useCompactDemoControls && stateManager != null)
        {
            if (stateManager.CurrentState == AppState.PlaneDetected ||
                stateManager.CurrentState == AppState.Anchored)
            {
                OnTranslatePressed();
                return;
            }

            if (stateManager.CurrentState == AppState.Error &&
                CanRetryTranslationOnLockedSurface())
            {
                stateManager.SetState(AppState.PlaneDetected);
                OnTranslatePressed();
                return;
            }
        }

        StartScanning();
    }

    private void StartScanning()
    {
        isFrozen = false;
        ARSurfaceLockController lockController = ResolveSurfaceLockController();
        if (lockController != null)
        {
            lockController.BeginSearch();
        }
        else
        {
            SetPlaneTrackingEnabled(true);
        }
        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState("Scanning");
        stateManager?.SetState(AppState.Scanning);
        _ = CheckBackendHealthAsync();
    }

    private async void OnTranslatePressed()
    {
        AppState currentState = stateManager != null ? stateManager.CurrentState : AppState.Idle;
        bool retryOnLockedSurface = currentState == AppState.Error && CanRetryTranslationOnLockedSurface();
        if (currentState != AppState.PlaneDetected &&
            currentState != AppState.Anchored &&
            !retryOnLockedSurface)
        {
            stateManager.SetError("Chưa thấy bảng/slide. Hãy bấm Quét trước.");
            debugPanel?.UpdateTrackingState("No plane detected");
            return;
        }

        stateManager.SetState(AppState.Translating);
        UpdateButtonStates();

        string step = "init";
        try
        {
            step = "1-capture";
            PipelineResponse response = await RunPipelineAsync();

            step = "2-count";
            int readableBlocks = labelPlacer != null ? labelPlacer.CountReadableBlocks(response) : CountReadableBlocks(response);
            if (readableBlocks == 0)
            {
                stateManager.SetError("Chưa đọc được chữ. Giữ camera ổn định và thử lại.");
                return;
            }

            step = "3-place";
            int placed = labelPlacer != null ? labelPlacer.PlacePipelineLabels(response) : 0;
            if (placed == 0)
            {
                stateManager.SetError("Chưa ghim được bản dịch. Hãy quét lại mặt bảng/slide.");
                return;
            }

            step = "4-subtitle";
            if (response.blocks != null && response.blocks.Count > 0)
            {
                string subtitle = response.blocks[0].translated_text;
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    labelPlacer?.ShowSubtitle(subtitle);
                }
            }

            step = "5-done";
            stateManager.SetState(AppState.Anchored);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            string fullError = $"[{step}] {ex.GetType().Name}\n\n{ex.Message}\n\n{ex.StackTrace}";
            stateManager.SetError("Không dịch được. Hãy kiểm tra backend và bấm Thử lại.");
            debugPanel?.UpdateTrackingState($"Translate failed at {step}");
            if (showAdvancedControls)
            {
                ShowErrorOverlay(fullError);
            }
        }
    }

    private GameObject errorOverlay;

    private void ShowErrorOverlay(string errorText)
    {
        if (errorOverlay != null) Destroy(errorOverlay);

        // Tạo Canvas riêng
        errorOverlay = new GameObject("ErrorOverlay");
        var canvas = errorOverlay.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;
        errorOverlay.AddComponent<UnityEngine.UI.CanvasScaler>();
        errorOverlay.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Nền đen mờ
        var bgObj = new GameObject("BG");
        bgObj.transform.SetParent(errorOverlay.transform, false);
        var bgImage = bgObj.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.92f);
        var bgRect = bgObj.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        // Text lỗi
        var textObj = new GameObject("ErrorText");
        textObj.transform.SetParent(bgObj.transform, false);
        var text = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        text.text = errorText;
        text.fontSize = 24;
        text.color = Color.white;
        text.alignment = TMPro.TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TMPro.TextOverflowModes.Overflow;
        var textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.05f, 0.1f);
        textRect.anchorMax = new Vector2(0.95f, 0.85f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        // Nút đóng
        var btnObj = new GameObject("CloseBtn");
        btnObj.transform.SetParent(bgObj.transform, false);
        var btnImage = btnObj.AddComponent<UnityEngine.UI.Image>();
        btnImage.color = Color.white;
        var btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.3f, 0.02f);
        btnRect.anchorMax = new Vector2(0.7f, 0.08f);
        btnRect.offsetMin = Vector2.zero;
        btnRect.offsetMax = Vector2.zero;
        var btn = btnObj.AddComponent<UnityEngine.UI.Button>();
        btn.onClick.AddListener(() => { Destroy(errorOverlay); errorOverlay = null; });

        var btnTextObj = new GameObject("BtnText");
        btnTextObj.transform.SetParent(btnObj.transform, false);
        var btnText = btnTextObj.AddComponent<TMPro.TextMeshProUGUI>();
        btnText.text = "ĐÓNG";
        btnText.fontSize = 28;
        btnText.color = Color.black;
        btnText.alignment = TMPro.TextAlignmentOptions.Center;
        var btnTextRect = btnTextObj.GetComponent<RectTransform>();
        btnTextRect.anchorMin = Vector2.zero;
        btnTextRect.anchorMax = Vector2.one;
        btnTextRect.offsetMin = Vector2.zero;
        btnTextRect.offsetMax = Vector2.zero;
    }

    private async System.Threading.Tasks.Task<PipelineResponse> RunPipelineAsync()
    {
        CapturedFrame frame = await CaptureFrameForPipelineAsync();

        if (useBackendPipeline && httpPipelineClient != null)
        {
            // Không fallback sang mock — luôn hiện lỗi thật để debug
            return await RunBackendPipelineAsync(frame);
        }

        // Chỉ dùng mock khi useBackendPipeline = false
        var mockClient = new MockPipelineClient();
        return await mockClient.SendFrameAsync(
            frame.frameId,
            frame.imageBase64,
            frame.width,
            frame.height,
            targetLanguage,
            mock: true
        );
    }

    private async System.Threading.Tasks.Task<PipelineResponse> RunBackendPipelineAsync(CapturedFrame frame)
    {
        switch (backendPipelineMode)
        {
            case BackendPipelineMode.PipelineAlias:
                debugPanel?.UpdateTrackingState("Backend /pipeline");
                return await httpPipelineClient.SendPipelineAliasAsync(
                    frame.frameId,
                    frame.imageBase64,
                    frame.width,
                    frame.height,
                    targetLanguage,
                    backendMockMode,
                    ocrProvider,
                    translationProvider
                );

            case BackendPipelineMode.SplitOcrTranslate:
                debugPanel?.UpdateTrackingState("Backend /ocr");
                OCRResponse ocrResponse = await httpPipelineClient.SendOcrAsync(
                    frame.imageBase64,
                    frame.width,
                    frame.height,
                    backendMockMode,
                    ocrProvider
                );

                debugPanel?.UpdateOCRResponse(ocrResponse);
                debugPanel?.UpdateTrackingState("Backend /translate");
                TranslateResponse translateResponse = await httpPipelineClient.SendTranslateAsync(
                    BuildTranslateItems(ocrResponse),
                    targetLanguage,
                    backendMockMode,
                    translationProvider
                );
                debugPanel?.UpdateTranslateResponse(translateResponse);
                return httpPipelineClient.ComposePipelineResponse(frame.frameId, ocrResponse, translateResponse);

            case BackendPipelineMode.PipelineFrame:
            default:
                debugPanel?.UpdateTrackingState("Backend /pipeline/frame");
                return await httpPipelineClient.SendFrameAsync(
                    frame.frameId,
                    frame.imageBase64,
                    frame.width,
                    frame.height,
                    targetLanguage,
                    backendMockMode,
                    ocrProvider,
                    translationProvider
                );
        }
    }

    private List<TranslateTextItem> BuildTranslateItems(OCRResponse ocrResponse)
    {
        var texts = new List<TranslateTextItem>();
        if (ocrResponse?.blocks == null) return texts;

        foreach (OCRBlock block in ocrResponse.blocks)
        {
            if (block == null || string.IsNullOrWhiteSpace(block.text)) continue;
            texts.Add(new TranslateTextItem
            {
                id = block.id,
                text = block.text
            });
        }

        return texts;
    }

    private int CountReadableBlocks(PipelineResponse response)
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

    private async System.Threading.Tasks.Task CheckBackendHealthAsync()
    {
        if (!useBackendPipeline || httpPipelineClient == null) return;

        try
        {
            BackendHealthResponse health = await httpPipelineClient.CheckHealthAsync();
            debugPanel?.UpdateBackendHealth(health);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ButtonController] Backend health check failed: {ex.Message}");
            debugPanel?.UpdateTrackingState("Backend offline");
        }
    }

    private async System.Threading.Tasks.Task<CapturedFrame> CaptureFrameForPipelineAsync()
    {
        if (frameCaptureService != null && useBackendPipeline)
        {
            debugPanel?.UpdateTrackingState("Capturing frame");
            CapturedFrame frame = await frameCaptureService.CaptureAsync();
            string source = string.IsNullOrWhiteSpace(frame.captureSource)
                ? frameCaptureService.LastCaptureSource
                : frame.captureSource;
            if (!string.IsNullOrWhiteSpace(frameCaptureService.LastCaptureWarning))
            {
                debugPanel?.UpdateTrackingState(frameCaptureService.LastCaptureWarning);
                stateManager?.SetStatusMessage("Không lấy được ảnh AR, đang dùng ảnh dự phòng.");
            }
            else if (!string.IsNullOrWhiteSpace(source))
            {
                debugPanel?.UpdateTrackingState("Captured: " + source);
            }

            return frame;
        }

        return new CapturedFrame
        {
            frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
            imageBase64 = "",
            width = Screen.width,
            height = Screen.height,
            captureSource = "mock_empty_frame"
        };
    }

    private void OnClearPressed()
    {
        labelPlacer?.ClearAll();
        labelPlacer?.SetTranslationsVisible(true);
        debugPanel?.ClearAll();
        debugPanel?.UpdateTrackingState("Idle");
        isFrozen = false;
        ARSurfaceLockController lockController = ResolveSurfaceLockController();
        if (lockController != null)
        {
            lockController.ClearLock();
        }
        else
        {
            SetPlaneTrackingEnabled(false);
        }
        UpdateFreezeVisual();
        UpdateHideTranslationsVisual();
        stateManager?.SetState(AppState.Idle);
    }

    private void OnHideTranslationsPressed()
    {
        if (labelPlacer == null) return;

        labelPlacer.SetTranslationsVisible(!labelPlacer.AreTranslationsVisible);
        UpdateHideTranslationsVisual();
        UpdateButtonStates();
    }

    private void OnFreezePressed()
    {
        isFrozen = !isFrozen;
        ARSurfaceLockController lockController = ResolveSurfaceLockController();
        if (lockController != null)
        {
            if (isFrozen)
            {
                lockController.PausePlaneDetection();
            }
            else
            {
                lockController.ResumePlaneDetection();
            }
        }
        else
        {
            SetPlaneTrackingEnabled(!isFrozen);
        }

        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : stateManager.CurrentState.ToString());
    }

    private void OnStateChanged(AppState newState)
    {
        if (newState == AppState.Idle)
        {
            isFrozen = false;
            ARSurfaceLockController lockController = ResolveSurfaceLockController();
            if (lockController != null)
            {
                lockController.ClearLock();
            }
            else
            {
                SetPlaneTrackingEnabled(false);
            }
            UpdateFreezeVisual();
        }
        else if (newState == AppState.Error)
        {
            isFrozen = false;
            UpdateFreezeVisual();
        }

        debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : newState.ToString());
        UpdateHideTranslationsVisual();
        UpdateButtonStates();
        UpdatePrimaryActionLabel();
        UpdateTranslateActionLabel();
    }

    private void UpdateButtonStates()
    {
        // Enable/disable buttons dựa trên state
        if (stateManager == null) return;

        var state = stateManager.CurrentState;
        if (scanButton != null)
        {
            scanButton.interactable = state != AppState.Translating;
        }

        if (translateButton != null)
        {
            translateButton.interactable =
                !useCompactDemoControls &&
                state != AppState.Translating &&
                (state == AppState.PlaneDetected ||
                 state == AppState.Anchored ||
                 (state == AppState.Error && CanRetryTranslationOnLockedSurface()));
        }

        if (clearButton != null)
        {
            clearButton.interactable = state != AppState.Idle && state != AppState.Translating;
        }

        if (hideTranslationsButton != null)
        {
            bool hasTranslations = showTranslationVisibilityButton && labelPlacer != null && labelPlacer.HasPlacedTranslations();
            hideTranslationsButton.interactable = hasTranslations && state != AppState.Translating;
        }
        if (freezeButton != null)
        {
            freezeButton.interactable =
                showAdvancedControls &&
                (state == AppState.Scanning || state == AppState.PlaneDetected || state == AppState.Anchored);
        }
    }

    private void UpdatePrimaryActionLabel()
    {
        if (scanButton == null || stateManager == null) return;

        string label;
        if (!useCompactDemoControls)
        {
            switch (stateManager.CurrentState)
            {
                case AppState.Scanning:
                    label = "Đang quét";
                    break;
                case AppState.Translating:
                    label = "Đợi";
                    break;
                case AppState.PlaneDetected:
                case AppState.Anchored:
                case AppState.Error:
                    label = "Quét lại";
                    break;
                case AppState.Idle:
                default:
                    label = "Quét";
                    break;
            }

            SetButtonLabel(scanButton, label);
            return;
        }

        switch (stateManager.CurrentState)
        {
            case AppState.Scanning:
                label = "Đang quét";
                break;
            case AppState.PlaneDetected:
                label = "Dịch";
                break;
            case AppState.Translating:
                label = "Đang dịch";
                break;
            case AppState.Anchored:
                label = "Dịch lại";
                break;
            case AppState.Error:
                label = "Thử lại";
                break;
            case AppState.Idle:
            default:
                label = "Quét";
                break;
        }

        SetButtonLabel(scanButton, label);
    }

    private void UpdateTranslateActionLabel()
    {
        if (translateButton == null || stateManager == null) return;

        string label;
        switch (stateManager.CurrentState)
        {
            case AppState.Translating:
                label = "Đang dịch";
                break;
            case AppState.Anchored:
                label = "Dịch lại";
                break;
            case AppState.Error:
                label = CanRetryTranslationOnLockedSurface() ? "Thử lại" : "Dịch";
                break;
            case AppState.PlaneDetected:
            case AppState.Scanning:
            case AppState.Idle:
            default:
                label = "Dịch";
                break;
        }

        SetButtonLabel(translateButton, label);
    }

    private void ApplyControlVisibility()
    {
        bool showTranslateButton = !useCompactDemoControls;
        SetButtonObjectActive(translateButton, showTranslateButton);
        SetButtonObjectActive(hideTranslationsButton, showTranslationVisibilityButton);
        SetButtonObjectActive(freezeButton, showAdvancedControls);

        SetNamedObjectActive("TranslateButton", showTranslateButton);
        SetNamedObjectActive("HideTranslationsButton", showTranslationVisibilityButton);
        SetNamedObjectActive("FreezeButton", showAdvancedControls);
        SetNamedObjectActive("ToggleDebugButton", showAdvancedControls);
    }

    private void ResolveButtonReferences()
    {
        if (scanButton == null) scanButton = FindButtonByName("ScanButton");
        if (translateButton == null) translateButton = FindButtonByName("TranslateButton");
        if (clearButton == null) clearButton = FindButtonByName("ClearButton");
        if (hideTranslationsButton == null) hideTranslationsButton = FindButtonByName("HideTranslationsButton");
        if (freezeButton == null) freezeButton = FindButtonByName("FreezeButton");
    }

    private static Button FindButtonByName(string objectName)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (button.name == objectName) return button;
        }

        return null;
    }

    private void SetPlaneTrackingEnabled(bool enabled)
    {
        if (arPlaneManager == null) return;

        arPlaneManager.enabled = enabled;
        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            plane.gameObject.SetActive(enabled);
        }
    }

    private ARSurfaceLockController ResolveSurfaceLockController()
    {
        if (surfaceLockController == null)
        {
            surfaceLockController = FindAnyObjectByType<ARSurfaceLockController>();
        }

        if (surfaceLockController == null)
        {
            surfaceLockController = gameObject.AddComponent<ARSurfaceLockController>();
        }

        return surfaceLockController;
    }

    private bool CanRetryTranslationOnLockedSurface()
    {
        ARSurfaceLockController lockController = ResolveSurfaceLockController();
        return lockController != null &&
               lockController.HasLockedSurface &&
               lockController.CurrentState == ARSurfaceLockState.SurfaceLocked;
    }

    private void UpdateFreezeVisual()
    {
        if (freezeButton == null) return;

        Image image = freezeButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = isFrozen
                ? new Color(0f, 0.82f, 1f, 0.96f)
                : new Color(0.12f, 0.45f, 0.64f, 0.94f);
        }

        foreach (TextMeshProUGUI text in freezeButton.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = isFrozen ? "Frozen" : "Freeze";
        }

        foreach (Text text in freezeButton.GetComponentsInChildren<Text>(true))
        {
            text.text = isFrozen ? "Frozen" : "Freeze";
        }
    }

    private void UpdateHideTranslationsVisual()
    {
        if (hideTranslationsButton == null) return;

        bool translationsVisible = labelPlacer == null || labelPlacer.AreTranslationsVisible;
        string label = translationsVisible ? "Hide VN" : "Show VN";
        Color baseColor = translationsVisible
            ? new Color(0.18f, 0.24f, 0.31f, 0.96f)
            : new Color(0.10f, 0.68f, 0.52f, 0.96f);

        Image image = hideTranslationsButton.GetComponent<Image>();
        if (image != null)
        {
            image.color = baseColor;
        }

        ColorBlock colors = hideTranslationsButton.colors;
        colors.normalColor = baseColor;
        colors.highlightedColor = Color.Lerp(baseColor, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(baseColor, Color.black, 0.16f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.18f, 0.20f, 0.24f, 0.52f);
        colors.colorMultiplier = 1f;
        hideTranslationsButton.colors = colors;

        SetButtonLabel(hideTranslationsButton, label);
    }

    private void EnsureHideTranslationsButton()
    {
        if (hideTranslationsButton == null)
        {
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button.name != "HideTranslationsButton") continue;

                hideTranslationsButton = button;
                hideTranslationsButton.gameObject.SetActive(true);
                break;
            }
        }

        if (hideTranslationsButton != null)
        {
            ConfigureHideTranslationsButtonLayout(hideTranslationsButton.GetComponent<RectTransform>(), false);
            return;
        }

        Button template = clearButton != null ? clearButton : translateButton;
        Transform parent = ResolveHideTranslationsParent(out bool attachToTopBar);
        if (template == null || parent == null) return;

        GameObject buttonObject = Instantiate(template.gameObject, parent, false);
        buttonObject.name = "HideTranslationsButton";
        buttonObject.SetActive(true);
        hideTranslationsButton = buttonObject.GetComponent<Button>();

        ConfigureHideTranslationsButtonLayout(buttonObject.GetComponent<RectTransform>(), attachToTopBar);
        SetButtonLabel(hideTranslationsButton, "Hide VN");

        if (attachToTopBar)
        {
            ReserveTopBarSpaceForHideTranslationsButton();
        }
    }

    private Transform ResolveHideTranslationsParent(out bool attachToTopBar)
    {
        // Luôn gắn vào root Canvas (cùng parent với Transcript button)
        attachToTopBar = false;
        Canvas canvas = FindAnyObjectByType<Canvas>();
        return canvas != null ? canvas.transform : null;
    }

    private void ConfigureHideTranslationsButtonLayout(RectTransform rect, bool attachToTopBar)
    {
        if (rect == null) return;

        // Luôn đặt ở top-right, ngay dưới nút Transcript (y=-70, h=50 → dưới là y=-126)
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-16f, -126f);
        rect.sizeDelta = new Vector2(160f, 50f);
    }

    private void ReserveTopBarSpaceForHideTranslationsButton()
    {
        GameObject titleObject = GameObject.Find("AppTitle");
        if (titleObject == null) return;

        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        if (titleRect == null) return;

        titleRect.offsetMax = new Vector2(Mathf.Min(titleRect.offsetMax.x, -190f), titleRect.offsetMax.y);
    }

    private static void SetButtonObjectActive(Button button, bool active)
    {
        if (button != null) button.gameObject.SetActive(active);
    }

    private static void SetNamedObjectActive(string objectName, bool active)
    {
        GameObject obj = GameObject.Find(objectName);
        if (obj != null) obj.SetActive(active);
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null) return;

        foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = label;
        }

        foreach (Text text in button.GetComponentsInChildren<Text>(true))
        {
            text.text = label;
        }
    }
}
