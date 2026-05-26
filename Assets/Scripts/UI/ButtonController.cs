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
    [SerializeField] private MockTranslationService translationService;
    [SerializeField] private ARPlaneManager arPlaneManager;

    [Header("OCR / Translate Pipeline")]
    [SerializeField] private bool useBackendPipeline = true;
    [SerializeField] private bool backendMockMode = false;
    [SerializeField] private bool fallbackToUnityMockOnBackendError = true;
    [SerializeField] private string targetLanguage = "vi";
    [SerializeField] private BackendPipelineMode backendPipelineMode = BackendPipelineMode.PipelineFrame;
    [SerializeField] private string ocrProvider = "tesseract";
    [SerializeField] private string translationProvider = "google";
    [SerializeField] private FrameCaptureService frameCaptureService;
    [SerializeField] private HttpPipelineClient httpPipelineClient;

    [Header("Buttons")]
    [SerializeField] private Button scanButton;
    [SerializeField] private Button translateButton;
    [SerializeField] private Button clearButton;
    [SerializeField] private Button freezeButton;

    [Header("Debug")]
    [SerializeField] private DebugPanelController debugPanel;

    private bool isFrozen = false;

    private void Start()
    {
        if (arPlaneManager == null)
        {
            arPlaneManager = FindAnyObjectByType<ARPlaneManager>();
        }
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

        scanButton.onClick.AddListener(OnScanPressed);
        translateButton.onClick.AddListener(OnTranslatePressed);
        clearButton.onClick.AddListener(OnClearPressed);
        freezeButton.onClick.AddListener(OnFreezePressed);
        stateManager.OnStateChanged.AddListener(OnStateChanged);

        UpdateButtonStates();
        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState("Idle");
        _ = CheckBackendHealthAsync();
    }

    private void OnDestroy()
    {
        if (scanButton != null) scanButton.onClick.RemoveListener(OnScanPressed);
        if (translateButton != null) translateButton.onClick.RemoveListener(OnTranslatePressed);
        if (clearButton != null) clearButton.onClick.RemoveListener(OnClearPressed);
        if (freezeButton != null) freezeButton.onClick.RemoveListener(OnFreezePressed);
        if (stateManager != null) stateManager.OnStateChanged.RemoveListener(OnStateChanged);
    }

    private void OnScanPressed()
    {
        isFrozen = false;
        SetPlaneTrackingEnabled(true);
        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState("Scanning");
        stateManager.SetState(AppState.Scanning);
        _ = CheckBackendHealthAsync();
    }

    private async void OnTranslatePressed()
    {
        if (stateManager.CurrentState != AppState.PlaneDetected &&
            stateManager.CurrentState != AppState.Anchored)
        {
            stateManager.SetState(AppState.Error);
            debugPanel?.UpdateTrackingState("No plane detected");
            return;
        }

        stateManager.SetState(AppState.Translating);
        UpdateButtonStates();

        try
        {
            PipelineResponse response = await RunPipelineAsync();
            debugPanel?.UpdatePipelineResponse(response);

            int readableBlocks = labelPlacer != null ? labelPlacer.CountReadableBlocks(response) : CountReadableBlocks(response);
            if (readableBlocks == 0)
            {
                string message = "Không đọc được chữ. Hãy đưa camera gần slide/bảng hơn, tăng ánh sáng hoặc thử chụp lại.";
                stateManager.SetState(AppState.Error);
                debugPanel?.UpdateTrackingState("OCR empty");
                debugPanel?.UpdateTranslatedText(message);
                labelPlacer?.ShowSubtitle(message);
                Debug.LogWarning("[ButtonController] OCR returned no readable text blocks.");
                return;
            }

            int placed = labelPlacer != null ? labelPlacer.PlacePipelineLabels(response) : 0;
            if (placed == 0)
            {
                string message = "Đã đọc được chữ nhưng chưa đặt được label AR. Hãy Scan lại mặt phẳng rồi bấm Translate.";
                stateManager.SetState(AppState.Error);
                debugPanel?.UpdateTrackingState("No AR hit");
                debugPanel?.UpdateTranslatedText(message);
                labelPlacer?.ShowSubtitle(message);
                Debug.LogWarning("[ButtonController] Pipeline returned readable blocks but no labels were placed. Scan plane again.");
                return;
            }

            if (response.blocks != null && response.blocks.Count > 0)
            {
                string subtitle = response.blocks[0].translated_text;
                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    labelPlacer.ShowSubtitle(subtitle);
                }
            }

            stateManager.SetState(AppState.Anchored);
            debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : $"Anchored ({placed})");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            stateManager.SetState(AppState.Error);
            debugPanel?.UpdateTrackingState("Pipeline error");
            debugPanel?.UpdateTranslatedText(ex.Message);
        }
    }

    private async System.Threading.Tasks.Task<PipelineResponse> RunPipelineAsync()
    {
        CapturedFrame frame = await CaptureFrameForPipelineAsync();

        if (useBackendPipeline && httpPipelineClient != null)
        {
            try
            {
                return await RunBackendPipelineAsync(frame);
            }
            catch (Exception)
            {
                if (!fallbackToUnityMockOnBackendError) throw;
                Debug.LogWarning("[ButtonController] Backend pipeline failed, falling back to Unity mock pipeline.");
            }
        }

        debugPanel?.UpdateTrackingState("Unity mock OCR/Translate");
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
            return await frameCaptureService.CaptureAsync();
        }

        return new CapturedFrame
        {
            frameId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff"),
            imageBase64 = "",
            width = Screen.width,
            height = Screen.height
        };
    }

    private void OnClearPressed()
    {
        labelPlacer.ClearAll();
        debugPanel?.ClearAll();
        debugPanel?.UpdateTrackingState("Idle");
        isFrozen = false;
        SetPlaneTrackingEnabled(true);
        UpdateFreezeVisual();
        stateManager.SetState(AppState.Idle);
    }

    private void OnFreezePressed()
    {
        isFrozen = !isFrozen;
        SetPlaneTrackingEnabled(!isFrozen);
        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : stateManager.CurrentState.ToString());
    }

    private void OnStateChanged(AppState newState)
    {
        if (newState == AppState.Idle || newState == AppState.Error)
        {
            isFrozen = false;
            SetPlaneTrackingEnabled(true);
            UpdateFreezeVisual();
        }

        debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : newState.ToString());
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        // Enable/disable buttons dựa trên state
        var state = stateManager.CurrentState;
        scanButton.interactable = state != AppState.Translating;
        translateButton.interactable =
            (state == AppState.PlaneDetected || state == AppState.Anchored);
        clearButton.interactable = (state == AppState.Anchored || state == AppState.Error);
        freezeButton.interactable =
            (state == AppState.Scanning || state == AppState.PlaneDetected || state == AppState.Anchored);
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
}
