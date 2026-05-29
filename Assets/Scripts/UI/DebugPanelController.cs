// DebugPanelController.cs
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DebugPanelController : MonoBehaviour
{
    [SerializeField] private bool showDebugToggle = false;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI ocrTextField;
    [SerializeField] private TextMeshProUGUI translatedTextField;
    [SerializeField] private TextMeshProUGUI fpsText;
    [SerializeField] private TextMeshProUGUI trackingStateText;
    [SerializeField] private Button toggleButton;

    private bool isVisible = false;

    void Start()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (toggleButton == null)
        {
            GameObject toggleObject = GameObject.Find("ToggleDebugButton");
            if (toggleObject != null) toggleButton = toggleObject.GetComponent<Button>();
        }

        if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
        if (toggleButton != null) toggleButton.gameObject.SetActive(showDebugToggle);
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    void Update()
    {
        if (isVisible && fpsText != null)
        {
            fpsText.text = $"FPS: {1f / Time.deltaTime:F0}";
        }
    }

    public void TogglePanel()
    {
        isVisible = !isVisible;
        if (panelRoot != null) panelRoot.SetActive(isVisible);
    }

    public void UpdateOCRText(string text)
    {
        if (ocrTextField != null) ocrTextField.text = $"OCR Raw:\n{text}";
    }

    public void UpdateTranslatedText(string text)
    {
        if (translatedTextField != null) translatedTextField.text = $"Translated:\n{text}";
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

    public void UpdateBackendHealth(BackendHealthResponse health)
    {
        if (health == null)
        {
            UpdateTrackingState("Backend unknown");
            return;
        }

        string ocrProvider = health.provider != null ? health.provider.ocr : "-";
        string translationProvider = health.provider != null ? health.provider.translation : "-";
        UpdateTrackingState($"Backend {health.status} | OCR={ocrProvider} | Trans={translationProvider}");
    }

    public void UpdateOCRResponse(OCRResponse response)
    {
        if (response == null)
        {
            UpdateOCRText("-");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Provider: {response.provider?.ocr ?? "-"}");
        builder.AppendLine($"Blocks: {response.blocks?.Count ?? 0}");

        if (response.blocks != null)
        {
            foreach (OCRBlock block in response.blocks)
            {
                builder.AppendLine($"[{block.id}] {block.text}");
            }
        }

        if (response.warnings != null && response.warnings.Length > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (string warning in response.warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        UpdateOCRText(builder.ToString());
    }

    public void UpdateTranslateResponse(TranslateResponse response)
    {
        if (response == null)
        {
            UpdateTranslatedText("-");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Provider: {response.provider?.translation ?? "-"}");
        builder.AppendLine($"Blocks: {response.translations?.Count ?? 0}");

        if (response.translations != null)
        {
            foreach (TranslationBlock block in response.translations)
            {
                builder.AppendLine($"[{block.id}] {block.translated_text}");
            }
        }

        if (response.warnings != null && response.warnings.Length > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (string warning in response.warnings)
            {
                builder.AppendLine($"- {warning}");
            }
        }

        UpdateTranslatedText(builder.ToString());
    }

    public void UpdateTrackingState(string state)
    {
        if (trackingStateText != null) trackingStateText.text = $"Tracking: {state}";
    }

    public void ClearAll()
    {
        if (ocrTextField != null) ocrTextField.text = "OCR Raw:\n-";
        if (translatedTextField != null) translatedTextField.text = "Translated:\n-";
    }
}
