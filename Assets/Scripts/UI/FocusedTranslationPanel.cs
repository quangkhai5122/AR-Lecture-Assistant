using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class FocusedTranslationPanel : MonoBehaviour
{
    [SerializeField] private bool showByDefault = false;
    [SerializeField] private int maxCharacters = 1200;

    private GameObject panel;
    private TextMeshProUGUI bodyText;

    private void Awake()
    {
        EnsurePanel();
        if (!showByDefault)
        {
            Hide();
        }
    }

    public void Show(string text)
    {
        EnsurePanel();
        if (bodyText != null)
        {
            bodyText.text = Ellipsize(text, maxCharacters);
        }

        panel.SetActive(true);
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private void EnsurePanel()
    {
        if (panel != null) return;

        Canvas canvas = ResolveCanvas();
        if (canvas == null) return;

        panel = new GameObject("FocusedTranslationPanel");
        panel.transform.SetParent(canvas.transform, false);

        RectTransform rect = panel.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.07f, 0.08f);
        rect.anchorMax = new Vector2(0.93f, 0.30f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.02f, 0.92f);

        Shadow shadow = panel.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.40f);
        shadow.effectDistance = new Vector2(0f, -4f);

        bodyText = CreateBodyText(panel.transform);
        Button closeButton = CreateCloseButton(panel.transform);
        closeButton.onClick.AddListener(Hide);
    }

    private Canvas ResolveCanvas()
    {
        foreach (Canvas existingCanvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (existingCanvas != null &&
                existingCanvas.renderMode != RenderMode.WorldSpace &&
                existingCanvas.gameObject.activeInHierarchy)
            {
                return existingCanvas;
            }
        }

        GameObject canvasObject = new GameObject("FocusedTranslationCanvas");
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private TextMeshProUGUI CreateBodyText(Transform parent)
    {
        GameObject textObject = new GameObject("FocusedText");
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(18f, 14f);
        rect.offsetMax = new Vector2(-70f, -14f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.color = Color.white;
        text.fontSize = 24f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 15f;
        text.fontSizeMax = 26f;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateCloseButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("CloseFocusedTranslation");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-12f, -12f);
        rect.sizeDelta = new Vector2(48f, 42f);

        Image image = buttonObject.AddComponent<Image>();
        image.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.AddComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI label = labelObject.AddComponent<TextMeshProUGUI>();
        label.text = "X";
        label.color = Color.black;
        label.fontStyle = FontStyles.Bold;
        label.fontSize = 22f;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return button;
    }

    private static string Ellipsize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string trimmed = value.Trim();
        if (maxLength <= 3 || trimmed.Length <= maxLength) return trimmed;
        return trimmed.Substring(0, maxLength - 1).TrimEnd() + "...";
    }
}
