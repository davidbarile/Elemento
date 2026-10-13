using System;
using TMPro;
using UnityEngine;

public sealed class UISeatHandoffPanel : UIPanelBase
{
    public static UISeatHandoffPanel IN;//singleton

    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;

    private Action onContinue;
    private int playerNumber = 1;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
    }

    public void Show(int inPlayerNumber, Action inOnContinue)
    {
        this.playerNumber = inPlayerNumber;
        this.onContinue = inOnContinue;

        RefreshText();

        Show();
    }

    public override void Hide()
    {
        this.onContinue = null;

        base.Hide();
    }

    public void HandleContinueButtonPress()
    {
        var callback = this.onContinue;

        Hide();

        callback?.Invoke();
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
}
