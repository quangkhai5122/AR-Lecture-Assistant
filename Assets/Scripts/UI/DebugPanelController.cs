// DebugPanelController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DebugPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI ocrTextField;
    [SerializeField] private TextMeshProUGUI translatedTextField;
    [SerializeField] private TextMeshProUGUI fpsText;
    [SerializeField] private TextMeshProUGUI trackingStateText;
    [SerializeField] private Button toggleButton;

    private bool isVisible = false;

    void Start()
    {
        toggleButton.onClick.AddListener(TogglePanel);
        panelRoot.SetActive(false);
    }

    void Update()
    {
        if (isVisible)
        {
            fpsText.text = $"FPS: {1f / Time.deltaTime:F0}";
        }
    }

    public void TogglePanel()
    {
        isVisible = !isVisible;
        panelRoot.SetActive(isVisible);
    }

    public void UpdateOCRText(string text)
    {
        ocrTextField.text = $"OCR Raw:\n{text}";
    }

    public void UpdateTranslatedText(string text)
    {
        translatedTextField.text = $"Translated:\n{text}";
    }

    public void UpdateTrackingState(string state)
    {
        trackingStateText.text = $"Tracking: {state}";
    }

    public void ClearAll()
    {
        ocrTextField.text = "OCR Raw:\n-";
        translatedTextField.text = "Translated:\n-";
    }
}