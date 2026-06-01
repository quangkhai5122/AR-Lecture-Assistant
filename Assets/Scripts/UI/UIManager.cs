// UIManager.cs
// Đặt tại: Assets/Scripts/UI/UIManager.cs
// Mục đích: Điều phối tổng thể giữa AR và UI, là "bộ não" của app

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class UIManager : MonoBehaviour
{
    [Header("=== AR Components ===")]
    [SerializeField] private ARPlaneManager arPlaneManager;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private ARAnchorPlacer anchorPlacer;
    [SerializeField] private ARLabelPlacer labelPlacer;
    [SerializeField] private ARSurfaceLockController surfaceLockController;

    [Header("=== UI Components ===")]
    [SerializeField] private StateManager stateManager;
    [SerializeField] private ButtonController buttonController;
    [SerializeField] private DebugPanelController debugPanel;
    [SerializeField] private SpeechTranscriptController speechTranscriptController;
    [SerializeField] private bool showTranscriptControl = true;

    [Header("=== Services ===")]
    [SerializeField] private MockOCRService ocrService;
    [SerializeField] private MockTranslationService translationService;

    [Header("=== UI Elements ===")]
    [SerializeField] private GameObject crosshair;  // Dấu + ở giữa màn hình


    private void Start()
    {
        InitializeApp();
    }

    private void InitializeApp()
    {
        ARLectureVisualPolish polish = GetComponent<ARLectureVisualPolish>();
        if (polish == null)
        {
            polish = gameObject.AddComponent<ARLectureVisualPolish>();
        }
        polish.Apply();
        EnsureSurfaceLockController();
        EnsureSpeechTranscriptController();

        // Bắt đầu ở trạng thái Idle
        stateManager.SetState(AppState.Idle);

        // Tắt plane detection ban đầu (chờ nhấn Scan)
        ClearSurfaceLock();

        // Lắng nghe khi plane được detect
        if (arPlaneManager != null)
        {
            arPlaneManager.planesChanged += OnPlanesChanged;
        }

        // Lắng nghe thay đổi state
        stateManager.OnStateChanged.AddListener(OnStateChanged);

        Debug.Log("[UIManager] App initialized successfully");
    }

    private void EnsureSpeechTranscriptController()
    {
        if (speechTranscriptController == null)
        {
            speechTranscriptController = FindAnyObjectByType<SpeechTranscriptController>();
        }

        if (speechTranscriptController == null)
        {
            speechTranscriptController = gameObject.AddComponent<SpeechTranscriptController>();
        }

        if (showTranscriptControl)
        {
            speechTranscriptController.EnsureTranscriptUiVisible();
        }
        else
        {
            speechTranscriptController.HideTranscriptUi();
        }
    }

    private void EnsureSurfaceLockController()
    {
        if (surfaceLockController == null)
        {
            surfaceLockController = FindAnyObjectByType<ARSurfaceLockController>();
        }

        if (surfaceLockController == null)
        {
            surfaceLockController = gameObject.AddComponent<ARSurfaceLockController>();
        }

        surfaceLockController.SurfaceLocked -= OnSurfaceLocked;
        surfaceLockController.SurfaceLocked += OnSurfaceLocked;
        surfaceLockController.TrackingStateChanged -= OnSurfaceTrackingStateChanged;
        surfaceLockController.TrackingStateChanged += OnSurfaceTrackingStateChanged;
    }

    private void OnDestroy()
    {
        if (arPlaneManager != null)
        {
            arPlaneManager.planesChanged -= OnPlanesChanged;
        }

        if (surfaceLockController != null)
        {
            surfaceLockController.SurfaceLocked -= OnSurfaceLocked;
            surfaceLockController.TrackingStateChanged -= OnSurfaceTrackingStateChanged;
        }
    }

    private void OnSurfaceLocked(Pose pose)
    {
        labelPlacer?.CachePlanePose(pose);
        if (stateManager != null && stateManager.CurrentState == AppState.Scanning)
        {
            stateManager.SetState(AppState.PlaneDetected);
        }
    }

    private void OnSurfaceTrackingStateChanged(ARSurfaceLockState trackingState)
    {
        debugPanel?.UpdateTrackingState(trackingState.ToString());

        if (stateManager == null) return;

        switch (trackingState)
        {
            case ARSurfaceLockState.SearchingPlane:
                stateManager.SetStatusMessage("Đang tìm mặt bảng/slide...");
                break;
            case ARSurfaceLockState.PlaneFound:
                stateManager.SetStatusMessage("Đã thấy mặt bảng/slide", true);
                break;
            case ARSurfaceLockState.SurfaceLocked:
                if (stateManager.CurrentState == AppState.Scanning)
                {
                    stateManager.SetState(AppState.PlaneDetected);
                }
                else
                {
                    stateManager.SetStatusMessage("Đã ghim mặt bảng/slide", true);
                }
                break;
            case ARSurfaceLockState.TrackingLimited:
                stateManager.SetStatusMessage("Di chuyển camera chậm lại để bắt mặt bảng");
                break;
            case ARSurfaceLockState.Lost:
                if (surfaceLockController != null && !surfaceLockController.HasLockedSurface)
                {
                    return;
                }

                if (stateManager.CurrentState != AppState.Idle &&
                    stateManager.CurrentState != AppState.Error)
                {
                    stateManager.SetError("Mất bám mặt bảng. Bấm Thử lại để quét lại.");
                }
                break;
        }
    }

    /// <summary>
    /// Bật/tắt plane detection
    /// </summary>
    public void SetPlaneDetection(bool enabled)
    {
        if (enabled)
        {
            BeginSurfaceSearch();
            return;
        }

        if (surfaceLockController != null)
        {
            ClearSurfaceLock();
            return;
        }

        if (arPlaneManager != null)
        {
            arPlaneManager.enabled = enabled;

            // Ẩn/hiện các plane đã detect
            foreach (var plane in arPlaneManager.trackables)
            {
                plane.gameObject.SetActive(enabled);
            }
        }
    }

    /// <summary>
    /// Routes scan/reset operations through the surface lock controller.
    /// </summary>
    private void BeginSurfaceSearch()
    {
        EnsureSurfaceLockController();

        if (surfaceLockController != null)
        {
            surfaceLockController.BeginSearch();
            return;
        }

        SetPlaneDetectionFallback(true);
    }

    private void ClearSurfaceLock()
    {
        if (surfaceLockController != null)
        {
            surfaceLockController.ClearLock();
            return;
        }

        SetPlaneDetectionFallback(false);
    }

    private void SetPlaneDetectionFallback(bool enabled)
    {
        if (arPlaneManager == null) return;

        arPlaneManager.enabled = enabled;
        foreach (var plane in arPlaneManager.trackables)
        {
            plane.gameObject.SetActive(enabled);
        }
    }

    /// <summary>
    /// Callback khi AR Plane Manager detect/update/remove planes
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Khi có plane mới được detect
        if (args.added.Count > 0)
        {
            if (stateManager.CurrentState == AppState.Scanning)
            {
                stateManager.SetState(AppState.PlaneDetected);
                Debug.Log($"[UIManager] Plane detected! Count: {args.added.Count}");

                // Cache plane pose cho ARLabelPlacer — dùng khi camera di gần và raycast miss
                surfaceLockController?.ObservePlaneFound(args.added[0]);
                if (labelPlacer != null && raycastController != null)
                {
                    if (raycastController.TryRaycastFromCenter(out UnityEngine.Pose hitPose))
                    {
                        labelPlacer.CachePlanePose(hitPose);
                    }
                    else
                    {
                        // Dùng pose của plane đầu tiên
                        var plane = args.added[0];
                        labelPlacer.CachePlanePose(new UnityEngine.Pose(
                            plane.transform.position,
                            plane.transform.rotation));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Xử lý khi state thay đổi - cập nhật UI tương ứng
    /// </summary>
    private void OnStateChanged(AppState newState)
    {
        switch (newState)
        {
            case AppState.Idle:
                ClearSurfaceLock();
                if (crosshair != null) crosshair.SetActive(false);
                break;

            case AppState.Scanning:
                BeginSurfaceSearch();
                if (crosshair != null) crosshair.SetActive(true);
                break;

            case AppState.PlaneDetected:
                if (crosshair != null) crosshair.SetActive(true);
                break;

            case AppState.Translating:
                // Đang xử lý, có thể hiện loading indicator
                break;

            case AppState.Anchored:
                // Label đã được đặt thành công
                break;

            case AppState.Error:
                if (crosshair != null)
                {
                    crosshair.SetActive(
                        surfaceLockController != null &&
                        surfaceLockController.CurrentState == ARSurfaceLockState.SurfaceLocked
                    );
                }
                break;
        }

        Debug.Log($"[UIManager] State changed to: {newState}");
    }

    /// <summary>
    /// Được gọi khi nhấn nút Scan
    /// </summary>
    public void OnScanRequested()
    {
        stateManager.SetState(AppState.Scanning);
        BeginSurfaceSearch();
    }

    /// <summary>
    /// Được gọi khi nhấn nút Translate
    /// Quy trình: OCR → Translate → Place Label
    /// </summary>
    public async void OnTranslateRequested()
    {
        if (stateManager.CurrentState != AppState.PlaneDetected &&
            stateManager.CurrentState != AppState.Anchored)
        {
            Debug.LogWarning("[UIManager] Cannot translate: no plane detected");
            return;
        }

        stateManager.SetState(AppState.Translating);

        try
        {
            // Bước 1: OCR - đọc text từ slide
            string ocrText = await ocrService.RecognizeTextFromSlideAsync();
            debugPanel?.UpdateOCRText(ocrText);

            // Bước 2: Translate - dịch text
            TranslationResult result = await translationService.TranslateAsync(ocrText);
            debugPanel?.UpdateTranslatedText(result.TranslatedText);

            // Bước 3: Đặt Fixed Label lên plane
            Vector2 screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
            labelPlacer.PlaceFixedLabel(result.TranslatedText, screenCenter);

            // Bước 4: Hiện subtitle
            string speechText = await ocrService.RecognizeSpeechAsync();
            TranslationResult speechResult = await translationService.TranslateAsync(speechText);
            labelPlacer.ShowSubtitle(speechResult.TranslatedText);

            stateManager.SetState(AppState.Anchored);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[UIManager] Translation error: {e.Message}");
            stateManager.SetState(AppState.Error);
        }
    }

    /// <summary>
    /// Được gọi khi nhấn nút Clear
    /// </summary>
    public void OnClearRequested()
    {
        labelPlacer.ClearAll();
        ocrService.Reset();
        debugPanel?.ClearAll();
        ClearSurfaceLock();
        stateManager.SetState(AppState.Idle);
    }

    /// <summary>
    /// Được gọi khi nhấn nút Freeze
    /// </summary>
    public void OnFreezeRequested(bool freeze)
    {
        if (freeze)
        {
            if (surfaceLockController != null)
            {
                surfaceLockController.PausePlaneDetection();
            }
            else
            {
                SetPlaneDetectionFallback(false);
            }
        }
        else
        {
            if (surfaceLockController != null)
            {
                surfaceLockController.ResumePlaneDetection();
            }
            else
            {
                BeginSurfaceSearch();
            }
        }

        Debug.Log($"[UIManager] Freeze: {freeze}");
    }
}
