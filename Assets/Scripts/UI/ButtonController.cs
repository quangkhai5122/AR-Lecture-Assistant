// ButtonController.cs
using UnityEngine;
using UnityEngine.UI;

public class ButtonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StateManager stateManager;
    [SerializeField] private ARLabelPlacer labelPlacer;
    [SerializeField] private ARRaycastController raycastController;
    [SerializeField] private MockTranslationService translationService;

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
        scanButton.onClick.AddListener(OnScanPressed);
        translateButton.onClick.AddListener(OnTranslatePressed);
        clearButton.onClick.AddListener(OnClearPressed);
        freezeButton.onClick.AddListener(OnFreezePressed);

        UpdateButtonStates();
    }

    private void OnScanPressed()
    {
        stateManager.SetState(AppState.Scanning);
        // Bật plane detection
        // AR Plane Manager sẽ tự động detect
        // Khi detect thành công → chuyển sang PlaneDetected
    }

    private async void OnTranslatePressed()
    {
        stateManager.SetState(AppState.Translating);

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
    }

    private void OnClearPressed()
    {
        labelPlacer.ClearAll();
        debugPanel?.ClearAll();
        stateManager.SetState(AppState.Idle);
    }

    private void OnFreezePressed()
    {
        isFrozen = !isFrozen;
        // Freeze: tạm dừng tracking/update để giữ nguyên vị trí
        // Có thể disable AR Plane Manager tạm thời
    }

    private void UpdateButtonStates()
    {
        // Enable/disable buttons dựa trên state
        var state = stateManager.CurrentState;
        translateButton.interactable =
            (state == AppState.PlaneDetected || state == AppState.Anchored);
        clearButton.interactable = (state == AppState.Anchored);
    }
}