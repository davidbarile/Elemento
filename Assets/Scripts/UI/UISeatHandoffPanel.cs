using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UISeatHandoffPanel : MonoBehaviour
{
    private const string defaultFontName = "Cabold Comic demo SDF";
    private const float titleFontSize = 90f;
    private const float subtitleFontSize = 75f;
    private static readonly Color subtitleColor = new Color32(0x91, 0x91, 0x91, 0xFF);

    private TextMeshProUGUI titleText;
    private TextMeshProUGUI subtitleText;
    private Button continueButton;
    private Action onContinue;
    private int playerNumber = 1;

    public static UISeatHandoffPanel Create(Transform inParent, TMP_FontAsset inFont = null)
    {
        var panelObject = new GameObject("Pass And Play Handoff Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        panelObject.transform.SetParent(inParent, false);

        var panel = panelObject.AddComponent<UISeatHandoffPanel>();
        panel.Initialize(inFont != null ? inFont : FindDefaultFont());
        return panel;
    }

    public void Initialize(TMP_FontAsset inFont)
    {
        var rectTransform = this.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var background = this.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, .94f);

        this.continueButton = this.GetComponent<Button>();
        this.continueButton.targetGraphic = background;
        this.continueButton.onClick.AddListener(HandleContinueButtonPress);

        this.titleText = CreateLabel("Title", inFont, titleFontSize, Color.white,
            new Vector2(.05f, .5f), new Vector2(.95f, .75f));
        this.subtitleText = CreateLabel("Subtitle", inFont, subtitleFontSize, subtitleColor,
            new Vector2(.05f, .3f), new Vector2(.95f, .5f));
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        RefreshText();

        Hide();
    }

    public void Show(int inPlayerNumber, Action inOnContinue)
    {
        this.playerNumber = inPlayerNumber;
        RefreshText();
        this.onContinue = inOnContinue;
        this.transform.SetAsLastSibling();
        this.gameObject.SetActive(true);
    }

    public void Hide()
    {
        this.onContinue = null;
        this.gameObject.SetActive(false);
    }

    private void HandleContinueButtonPress()
    {
        var callback = this.onContinue;
        Hide();
        callback?.Invoke();
    }

    private TextMeshProUGUI CreateLabel(string inName, TMP_FontAsset inFont, float inFontSize, Color inColor,
        Vector2 inAnchorMin, Vector2 inAnchorMax)
    {
        var labelObject = new GameObject(inName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(this.transform, false);

        var labelRectTransform = labelObject.GetComponent<RectTransform>();
        labelRectTransform.anchorMin = inAnchorMin;
        labelRectTransform.anchorMax = inAnchorMax;
        labelRectTransform.offsetMin = Vector2.zero;
        labelRectTransform.offsetMax = Vector2.zero;

        var label = labelObject.GetComponent<TextMeshProUGUI>();
        if (inFont != null)
            label.font = inFont;
        label.alignment = TextAlignmentOptions.Center;
        label.color = inColor;
        label.fontSize = inFontSize;
        label.raycastTarget = false;
        return label;
    }

    private static TMP_FontAsset FindDefaultFont()
    {
        foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font != null && font.name == defaultFontName)
                return font;
        }

        Debug.LogWarning($"UISeatHandoffPanel: font '{defaultFontName}' not found; using TMP default font.");
        return null;
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void RefreshText()
    {
        string passFormat = GetLocalized(LocalizationManager.IN != null ? LocalizationManager.IN.PassToPlayerX : null, "Pass to Player {0}");
        this.titleText.text = string.Format(passFormat, this.playerNumber);
        this.subtitleText.text = GetLocalized(LocalizationManager.IN != null ? LocalizationManager.IN.TapToContinue : null, "Tap to Continue");
    }

    private static string GetLocalized(string[] inStrings, string inFallback)
    {
        if (inStrings == null || inStrings.Length == 0)
            return inFallback;

        int languageIndex = LOC.LI;
        if (languageIndex < 0 || languageIndex >= inStrings.Length || string.IsNullOrEmpty(inStrings[languageIndex]))
            return inStrings[0];

        return inStrings[languageIndex];
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        if (this.continueButton != null)
            this.continueButton.onClick.RemoveListener(HandleContinueButtonPress);
    }
}
