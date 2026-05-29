using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ARLectureVisualPolish : MonoBehaviour
{
    [SerializeField] private bool showAdvancedControls = false;

    private static readonly Color DockColor = new Color(0.025f, 0.030f, 0.040f, 0.70f);
    private static readonly Color PanelColor = new Color(0.045f, 0.055f, 0.072f, 0.84f);
    private static readonly Color PrimaryColor = new Color(0.13f, 0.70f, 0.92f, 0.96f);
    private static readonly Color TranslateColor = new Color(0.38f, 0.34f, 0.95f, 0.96f);
    private static readonly Color ClearColor = new Color(0.08f, 0.10f, 0.14f, 0.92f);
    private static readonly Color SuccessColor = new Color(0.12f, 0.78f, 0.56f, 0.90f);
    private static readonly Color TextColor = new Color(0.94f, 0.96f, 1f, 1f);
    private static readonly Color LensOverlayColor = new Color(0.92f, 0.92f, 0.88f, 0.78f);
    private static readonly Color LensTextColor = new Color(0.14f, 0.15f, 0.17f, 0.96f);

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
        ApplyNavigationMode();
        StyleAllTexts();
        StyleTopBar();
        StyleActionBar();
        StyleButtons();
        StyleStatusPanels();
        StyleDebugPanels();
        StyleCrosshair();
    }

    public static void StyleLabel(GameObject labelRoot)
    {
        if (labelRoot == null) return;

        HideDecorativeLabelParts(labelRoot);

        foreach (Image image in labelRoot.GetComponentsInChildren<Image>(true))
        {
            if (!image.gameObject.activeInHierarchy) continue;

            image.color = LensOverlayColor;
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.16f), new Vector2(0f, -1f));
        }

        foreach (TextMeshProUGUI text in labelRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (!text.gameObject.activeInHierarchy) continue;

            text.color = LensTextColor;
            text.fontSize = Mathf.Clamp(text.fontSize, 14f, 28f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 13f;
            text.fontSizeMax = 28f;
            text.maxVisibleLines = 99;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.fontStyle = FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Left;
        }
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
            text.fontSize = Mathf.Clamp(text.fontSize, 18f, 30f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 18f;
            text.fontSizeMax = 30f;
            text.maxVisibleLines = 3;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.fontStyle = FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
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

    private static void HideDecorativeLabelParts(GameObject labelRoot)
    {
        foreach (Transform child in labelRoot.GetComponentsInChildren<Transform>(true))
        {
            string lowerName = child.name.ToLowerInvariant();
            if (lowerName.Contains("languagebadge") ||
                lowerName.Contains("accentbar") ||
                lowerName.Contains("en→vi") ||
                lowerName.Contains("en->vi"))
            {
                child.gameObject.SetActive(false);
            }
        }

        foreach (TextMeshProUGUI text in labelRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            string value = text.text.Trim().ToLowerInvariant();
            if (value == "en→vi" || value == "en->vi")
            {
                text.gameObject.SetActive(false);
            }
        }
    }

    private void StyleButtons()
    {
        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (buttons.Length == 0) return;

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
                bool isPrimaryAction = IsMainActionButton(button.name);
                float minWidth = isPrimaryAction ? 188f : 150f;
                float minHeight = isPrimaryAction ? 68f : 56f;
                rect.sizeDelta = new Vector2(Mathf.Max(rect.sizeDelta.x, minWidth), Mathf.Max(rect.sizeDelta.y, minHeight));
            }

            LayoutElement layoutElement = button.GetComponent<LayoutElement>();
            if (layoutElement == null) layoutElement = button.gameObject.AddComponent<LayoutElement>();
            if (IsMainActionButton(button.name))
            {
                layoutElement.minHeight = 68f;
                layoutElement.preferredHeight = 72f;
                layoutElement.flexibleWidth = 1f;
            }

            foreach (TextMeshProUGUI text in button.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = ResolveButtonLabel(button.name, text.text);
                text.color = Color.white;
                text.fontSize = Mathf.Max(text.fontSize, 24f);
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18f;
                text.fontSizeMax = 26f;
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
        foreach (Image image in FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            string lowerName = image.name.ToLowerInvariant();
            if (!lowerName.Contains("topbar")) continue;

            image.color = new Color(0.025f, 0.030f, 0.040f, 0.62f);
            AddShadow(image.gameObject, new Color(0f, 0f, 0f, 0.24f), new Vector2(0f, -3f));
        }

        foreach (TextMeshProUGUI text in FindObjectsByType<TextMeshProUGUI>(FindObjectsSortMode.None))
        {
            string lowerName = text.name.ToLowerInvariant();
            if (!lowerName.Contains("title")) continue;

            text.text = string.IsNullOrWhiteSpace(text.text) ? "AR Translator" : text.text.Trim();
            text.color = Color.white;
            text.fontSize = Mathf.Max(text.fontSize, 32f);
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Left;
        }
    }

    private void StyleActionBar()
    {
        GameObject actionBar = GameObject.Find("ActionBar");
        if (actionBar == null) return;

        RectTransform actionRect = actionBar.GetComponent<RectTransform>();
        if (actionRect != null)
        {
            actionRect.anchorMin = new Vector2(0f, 0f);
            actionRect.anchorMax = new Vector2(1f, 0f);
            actionRect.pivot = new Vector2(0.5f, 0f);
            actionRect.anchoredPosition = new Vector2(0f, 28f);
            actionRect.sizeDelta = new Vector2(-52f, showAdvancedControls ? 214f : 120f);
        }

        Image dockImage = actionBar.GetComponent<Image>();
        if (dockImage == null) dockImage = actionBar.AddComponent<Image>();
        dockImage.color = DockColor;
        dockImage.raycastTarget = false;
        AddShadow(actionBar, new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, -4f));
        AddOutline(actionBar, new Color(1f, 1f, 1f, 0.06f));

        VerticalLayoutGroup vertical = actionBar.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
        {
            vertical.padding = new RectOffset(18, 18, 18, 18);
            vertical.spacing = showAdvancedControls ? 12f : 0f;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
        }

        GameObject topRow = GameObject.Find("TopRow");
        if (topRow != null)
        {
            RectTransform topRect = topRow.GetComponent<RectTransform>();
            if (topRect != null) topRect.sizeDelta = new Vector2(0f, 78f);

            HorizontalLayoutGroup topLayout = topRow.GetComponent<HorizontalLayoutGroup>();
            if (topLayout != null)
            {
                topLayout.padding = new RectOffset(0, 0, 0, 0);
                topLayout.spacing = 14f;
                topLayout.childControlWidth = true;
                topLayout.childControlHeight = true;
                topLayout.childForceExpandWidth = true;
                topLayout.childForceExpandHeight = true;
            }
        }

        GameObject bottomRow = GameObject.Find("BottomRow");
        if (bottomRow != null)
        {
            bottomRow.SetActive(showAdvancedControls);
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

    private void ApplyNavigationMode()
    {
        SetObjectActive("FreezeButton", showAdvancedControls);
        SetObjectActive("ToggleDebugButton", showAdvancedControls);
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
        if (lowerName.Contains("clear")) return ClearColor;
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

    private static bool IsMainActionButton(string objectName)
    {
        string lowerName = objectName.ToLowerInvariant();
        return lowerName.Contains("scan") ||
               lowerName.Contains("translate") ||
               lowerName.Contains("clear");
    }

    private static void SetObjectActive(string objectName, bool active)
    {
        GameObject obj = GameObject.Find(objectName);
        if (obj != null) obj.SetActive(active);
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
