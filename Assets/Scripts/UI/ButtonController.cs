// ButtonController.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

public class ButtonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StateManager stateManager;
    [SerializeField] private ARLabelPlacer labelPlacer;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private MockTranslationService translationService;
    [SerializeField] private ARPlaneManager arPlaneManager;

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

        scanButton.onClick.AddListener(OnScanPressed);
        translateButton.onClick.AddListener(OnTranslatePressed);
        clearButton.onClick.AddListener(OnClearPressed);
        freezeButton.onClick.AddListener(OnFreezePressed);
        stateManager.OnStateChanged.AddListener(OnStateChanged);

        UpdateButtonStates();
        UpdateFreezeVisual();
        debugPanel?.UpdateTrackingState("Idle");
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

        // Mock: giả lập OCR + Translation
        var result = await translationService.TranslateAsync(
            "Sample lecture text on slide");

        debugPanel?.UpdateOCRText(result.OriginalText);
        debugPanel?.UpdateTranslatedText(result.TranslatedText);

        // Đặt label tại trung tâm màn hình
        Vector2 center = new Vector2(
            Screen.width / 2f, Screen.height / 2f);
        labelPlacer.PlaceFixedLabel(result.TranslatedText, center);

        stateManager.SetState(AppState.Anchored);
        debugPanel?.UpdateTrackingState(isFrozen ? "Frozen" : "Anchored");
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
