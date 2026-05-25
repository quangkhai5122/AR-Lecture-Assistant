using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ARLectureVisualPolish : MonoBehaviour
{
    private static readonly Color DockColor = new Color(0.035f, 0.043f, 0.055f, 0.74f);
    private static readonly Color PanelColor = new Color(0.045f, 0.055f, 0.072f, 0.82f);
    private static readonly Color PrimaryColor = new Color(0.36f, 0.48f, 0.95f, 0.96f);
    private static readonly Color TranslateColor = new Color(0.52f, 0.28f, 0.74f, 0.96f);
    private static readonly Color SuccessColor = new Color(0.15f, 0.72f, 0.45f, 0.9f);
    private static readonly Color TextColor = new Color(0.94f, 0.96f, 1f, 1f);

    private bool applied;

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (applied) return;
        applied = true;

        ConfigureCanvases();
        StyleAllTexts();
        StyleTopBar();
        StyleButtons();
        StyleStatusPanels();
        StyleDebugPanels();
        StyleCrosshair();
    }

    public static void StyleLabel(GameObject labelRoot)
    {
        if (labelRoot == null) return;

        foreach (Image image in labelRoot.GetComponentsInChildren<Image>(true))
        {
            image.color = new Color(0.04f, 0.05f, 0.07f, 0.78f);
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.34f), new Vector2(0f, -3f));
        }

        foreach (TextMeshProUGUI text in labelRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.color = TextColor;
            text.fontSize = Mathf.Max(text.fontSize, 30f);
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
        }

        EnsureAccentBar(labelRoot.transform);
    }

    public static void StyleSubtitle(GameObject subtitleRoot)
    {
        if (subtitleRoot == null) return;

        foreach (Image image in subtitleRoot.GetComponentsInChildren<Image>(true))
        {
            image.color = new Color(0.02f, 0.03f, 0.04f, 0.84f);
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.42f), new Vector2(0f, -4f));
        }

        foreach (TextMeshProUGUI text in subtitleRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.color = TextColor;
            text.fontSize = Mathf.Max(text.fontSize, 28f);
            text.fontStyle = FontStyles.Normal;
        }
    }

    private void ConfigureCanvases()
    {
        foreach (CanvasScaler scaler in FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None))
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.55f;
        }
    }

    private void StyleAllTexts()
    {
        foreach (TextMeshProUGUI text in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            text.color = TextColor;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (text.fontSize < 18f)
            {
                text.fontSize = 18f;
            }
        }
    }

    private void StyleButtons()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (buttons.Length == 0) return;

        Transform commonParent = FindCommonButtonParent(buttons);
        if (commonParent != null)
        {
            EnsureBottomDock(commonParent);
        }

        foreach (Button button in buttons)
        {
            Image image = button.GetComponent<Image>();
            if (image == null) image = button.gameObject.AddComponent<Image>();

            Color baseColor = ResolveButtonColor(button.name);
            image.color = baseColor;

            ColorBlock colors = button.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = LerpToWhite(baseColor, 0.12f);
            colors.pressedColor = LerpToBlack(baseColor, 0.18f);
            colors.selectedColor = LerpToWhite(baseColor, 0.08f);
            colors.disabledColor = new Color(0.18f, 0.2f, 0.24f, 0.52f);
            colors.colorMultiplier = 1f;
            button.colors = colors;

            AddShadow(button.gameObject, new Color(0f, 0f, 0f, 0.24f), new Vector2(0f, -2f));
            AddOutline(button.gameObject, new Color(1f, 1f, 1f, 0.08f));

            RectTransform rect = button.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, 150f), Mathf.Max(rect.sizeDelta.y, 56f));
            }

            foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = ResolveButtonLabel(button.name, text.text);
                text.color = Color.white;
                text.fontSize = Mathf.Max(text.fontSize, 22f);
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
            }

            foreach (Text legacyText in button.GetComponentsInChildren<Text>(true))
            {
                legacyText.text = ResolveButtonLabel(button.name, legacyText.text);
                legacyText.color = Color.white;
                legacyText.fontSize = Mathf.Max(legacyText.fontSize, 22);
                legacyText.alignment = TextAnchor.MiddleCenter;
            }
        }
    }

    private void StyleTopBar()
    {
        foreach (TextMeshProUGUI text in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            string lowerName = text.name.ToLowerInvariant();
            if (!lowerName.Contains("title")) continue;

            text.text = string.IsNullOrWhiteSpace(text.text) ? "AR Translator" : text.text.Trim();
            text.color = Color.white;
            text.fontSize = Mathf.Max(text.fontSize, 30f);
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Left;
        }
    }

    private void StyleStatusPanels()
    {
        foreach (Image image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string lowerName = image.name.ToLowerInvariant();
            if (!lowerName.Contains("status")) continue;

            image.color = SuccessColor;
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, -3f));
        }
    }

    private void StyleDebugPanels()
    {
        foreach (Image image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string lowerName = image.name.ToLowerInvariant();
            if (!lowerName.Contains("debug") && !lowerName.Contains("panel")) continue;
            if (lowerName.Contains("button")) continue;

            image.color = PanelColor;
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.3f), new Vector2(0f, -3f));
        }
    }

    private void StyleCrosshair()
    {
        GameObject crosshair = GameObject.Find("Crosshair");
        if (crosshair == null) crosshair = GameObject.Find("crosshair");
        if (crosshair == null) return;

        foreach (TextMeshProUGUI text in crosshair.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.text = "+";
            text.color = new Color(1f, 1f, 1f, 0.92f);
            text.fontSize = Mathf.Max(text.fontSize, 42f);
            text.fontStyle = FontStyles.Normal;
        }

        foreach (Image image in crosshair.GetComponentsInChildren<Image>(true))
        {
            image.color = new Color(1f, 1f, 1f, 0.86f);
        }
    }

    private static void EnsureBottomDock(Transform parent)
    {
        if (parent.Find("PolishedBottomDock") != null) return;

        GameObject dockObject = new GameObject("PolishedBottomDock");
        dockObject.transform.SetParent(parent, false);
        dockObject.transform.SetAsFirstSibling();

        RectTransform rect = dockObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 210f);

        Image image = dockObject.AddComponent<Image>();
        image.color = DockColor;
        AddShadow(dockObject, new Color(0f, 0f, 0f, 0.34f), new Vector2(0f, 4f));
    }

    private static Transform FindCommonButtonParent(Button[] buttons)
    {
        foreach (Button button in buttons)
        {
            if (button == null || button.transform.parent == null) continue;
            if (button.name.ToLowerInvariant().Contains("debug")) continue;
            return button.transform.parent;
        }

        return null;
    }

    private static void EnsureAccentBar(Transform parent)
    {
        if (parent == null || parent.Find("PolishedAccentBar") != null) return;

        GameObject accentObject = new GameObject("PolishedAccentBar");
        accentObject.transform.SetParent(parent, false);
        accentObject.transform.SetAsFirstSibling();

        RectTransform rect = accentObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(12f, 0f);

        Image image = accentObject.AddComponent<Image>();
        image.color = new Color(0f, 0.82f, 1f, 0.95f);
    }

    private static Color ResolveButtonColor(string objectName)
    {
        string lowerName = objectName.ToLowerInvariant();
        if (lowerName.Contains("scan")) return PrimaryColor;
        if (lowerName.Contains("translate")) return TranslateColor;
        if (lowerName.Contains("clear")) return new Color(0.12f, 0.14f, 0.17f, 0.94f);
        if (lowerName.Contains("freeze")) return new Color(0.12f, 0.45f, 0.64f, 0.94f);
        if (lowerName.Contains("debug")) return new Color(0.92f, 0.93f, 0.96f, 0.94f);
        return new Color(0.12f, 0.14f, 0.18f, 0.94f);
    }

    private static string ResolveButtonLabel(string objectName, string currentText)
    {
        string lowerName = objectName.ToLowerInvariant();
        if (lowerName.Contains("scan")) return "Scan";
        if (lowerName.Contains("translate")) return "Translate";
        if (lowerName.Contains("clear")) return "Clear";
        if (lowerName.Contains("freeze")) return "Freeze";
        if (lowerName.Contains("debug")) return "Debug";
        return string.IsNullOrWhiteSpace(currentText) ? "Action" : currentText.Trim();
    }

    private static Color LerpToWhite(Color color, float amount)
    {
        return Color.Lerp(color, Color.white, amount);
    }

    private static Color LerpToBlack(Color color, float amount)
    {
        return Color.Lerp(color, Color.black, amount);
    }

    private static void AddShadow(GameObject target, Color color, Vector2 distance)
    {
        Shadow shadow = target.GetComponent<Shadow>();
        if (shadow == null) shadow = target.AddComponent<Shadow>();
        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
    }

    private static void AddOutline(GameObject target, Color color)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(1f, -1f);
        outline.useGraphicAlpha = true;
    }
}
