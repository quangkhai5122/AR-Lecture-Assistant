// DebugPanelController.cs
using System.Text;
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

    public void UpdatePipelineResponse(PipelineResponse response)
    {
        if (response == null)
        {
            UpdateOCRText("-");
            UpdateTranslatedText("-");
            return;
        }

        var ocrBuilder = new StringBuilder();
        var translatedBuilder = new StringBuilder();

        if (response.provider != null)
        {
            translatedBuilder.AppendLine($"Provider: OCR={response.provider.ocr}, Trans={response.provider.translation}");
        }
        translatedBuilder.AppendLine($"Frame: {response.frame_id}");
        translatedBuilder.AppendLine($"Blocks: {response.blocks?.Count ?? 0}");

        if (response.blocks != null)
        {
            foreach (PipelineBlock block in response.blocks)
            {
                ocrBuilder.AppendLine($"[{block.id}] {block.source_text}");
                translatedBuilder.AppendLine($"[{block.id}] {block.translated_text}");
            }
        }

        if (response.warnings != null && response.warnings.Length > 0)
        {
            translatedBuilder.AppendLine("Warnings:");
            foreach (string warning in response.warnings)
            {
                translatedBuilder.AppendLine($"- {warning}");
            }
        }

        UpdateOCRText(ocrBuilder.Length > 0 ? ocrBuilder.ToString() : "-");
        UpdateTranslatedText(translatedBuilder.ToString());
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
