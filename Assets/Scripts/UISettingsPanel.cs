using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SettingsManager;

public class UISettingsPanel : UIPanelBase
{
    public static UISettingsPanel IN;

    private string filePath = "Assets/PlayerData/PlayerData.json";

    public UISkinSelectPanel SkinSelectPanel;

    [Space]
    [SerializeField] private UISlider[] sliders;
    [SerializeField] private Toggle showSplashScreenToggle;
    [SerializeField] private Toggle autoDealHandToggle;
    [SerializeField] private Toggle allowSelectPowerCardTypeToggle;
    [SerializeField] private TMP_Dropdown selectLanguageDropdown;
    [SerializeField] private TMP_Dropdown discardModeDropdown;
    [SerializeField] private TMP_Dropdown playModeDropdown;
    [SerializeField] private TMP_Dropdown aiDifficultyDropdown;
    [SerializeField] private TextMeshProUGUI dataPathText;
    [SerializeField] private GameObject[] categoryPanels;

    public bool IsDirty;
    public bool IsResettingData;

    private bool isInitialized;

    private void Start()
    {
        if (!Application.isEditor)
            this.filePath = $"{Application.persistentDataPath}/PlayerData.json";

        this.dataPathText.text = $"Data Path: {this.filePath}";
    }

    public override void Show()
    {
        base.Show();

        GameManager.IN.SetGamePaused(true);

        LocalizationManager.OnLanguageChanged += RefreshDropdownsLanguage;
        RefreshDropdownsLanguage(LOC.LI);

        if (!this.isInitialized)
            SelectCategory(0);

        this.isInitialized = true;
    }

    private void SetSliderValuesToSettings()
    {
        this.sliders[0].SetValue(SettingsManager.IN.MusicVolume);
        this.sliders[1].SetValue(SettingsManager.IN.EffectsVolume);

        this.sliders[2].SetValue(SettingsManager.IN.NumCardsToDeal);
        this.sliders[3].SetValue(SettingsManager.IN.MaxCardsInHand);
        this.sliders[4].SetValue(SettingsManager.IN.MovesPerTurn);

        this.sliders[5].SetValue(SettingsManager.IN.ElementZoneStartingHealth);

        var activatePoints = Mathf.RoundToInt((float)SettingsManager.IN.ElementZonePointsRequiredToActivate / 5);
        this.sliders[6].SetValue(activatePoints);

        var totalWinPoints = Mathf.RoundToInt((float)SettingsManager.IN.TotalPointsRequiredToWin / 10);
        this.sliders[7].SetValue(totalWinPoints);

        this.sliders[8].SetValue(SettingsManager.IN.FrozenZoneHealth);

        this.sliders[9].SetValue(SettingsManager.IN.LightningDamage);

        this.showSplashScreenToggle.isOn = SettingsManager.IN.ShouldShowSplashScreen;
        this.autoDealHandToggle.isOn = SettingsManager.IN.ShouldAutoDealHand;
        this.allowSelectPowerCardTypeToggle.isOn = SettingsManager.IN.AllowSelectPowerCardType;

        this.selectLanguageDropdown.value = PlayerData.Data.LanguageIndex;
        this.selectLanguageDropdown.RefreshShownValue();

        this.discardModeDropdown.value = (int)SettingsManager.IN.DiscardMode;
        this.discardModeDropdown.RefreshShownValue();

        this.playModeDropdown.value = (int)SettingsManager.IN.PlayMode;
        this.playModeDropdown.RefreshShownValue();

        this.aiDifficultyDropdown.value = (int)SettingsManager.IN.AiDifficulty;
        this.aiDifficultyDropdown.RefreshShownValue();

        RefreshAiDifficultyVisibility();
    }

    public void HandleSliderChanged(int inSliderIndex)
    {
        var slider = this.sliders[inSliderIndex];

        switch (inSliderIndex)
        {
            case 0:
                SettingsManager.IN.MusicVolume = slider.Value;
                AudioManager.IN.SetMusicVolume(slider.Value);
                break;

            case 1:
                SettingsManager.IN.EffectsVolume = slider.Value;
                AudioManager.IN.SetEffectsVolume(slider.Value);
                break;

            case 2:
                SettingsManager.IN.NumCardsToDeal = (int)slider.Value;
                break;

            case 3:
                SettingsManager.IN.MaxCardsInHand = (int)slider.Value;
                break;

            case 4:
                SettingsManager.IN.MovesPerTurn = (int)slider.Value;
                break;

            case 5:
                SettingsManager.IN.ElementZoneStartingHealth = (int)slider.Value;
                break;

            case 6:
                SettingsManager.IN.ElementZonePointsRequiredToActivate = (int)slider.Value;
                break;

            case 7:
                SettingsManager.IN.TotalPointsRequiredToWin = (int)slider.Value;
                break;

            case 8:
                SettingsManager.IN.FrozenZoneHealth = (int)slider.Value;
                break;

            case 9:
                SettingsManager.IN.LightningDamage = (int)slider.Value;
                break;
        }

        this.IsDirty = true;
    }

    public void HandleSelectLanguageDropdownChange(Int32 inValue)
    {
        LocalizationManager.IN.SelectLanguage(inValue);
        this.IsDirty = true;
    }

    public void HandleDiscardModeDropdownChange(Int32 inValue)
    {
        SettingsManager.IN.DiscardMode = (EDiscardMode)inValue;
        this.IsDirty = true;
    }

    public void HandleAiDifficultyDropdownChange(Int32 inValue)
    {
        SettingsManager.IN.AiDifficulty = (EAiDifficulty)inValue;
        this.IsDirty = true;
    }

    private void RefreshAiDifficultyVisibility()
    {
        var aiDifficultyParent = this.aiDifficultyDropdown.transform.parent;
        var isAgainstAi = SettingsManager.IN.PlayMode == EPlayMode.AgainstAI;

        if (aiDifficultyParent != null)
            aiDifficultyParent.gameObject.SetActive(isAgainstAi);
        else
            this.aiDifficultyDropdown.gameObject.SetActive(isAgainstAi);
    }

    public void HandlePlayModeDropdownChange(Int32 inValue)
    {
        SettingsManager.IN.PlayMode = (EPlayMode)inValue;

        RefreshAiDifficultyVisibility();

        this.IsDirty = true;
    }

    public void HandleSplashScreenToggleChanged(bool inIsOn)
    {
        SettingsManager.IN.ShouldShowSplashScreen = inIsOn;
        this.IsDirty = true;
    }

    public void HandleAutoDealHandToggleChanged(bool inIsOn)
    {
        SettingsManager.IN.ShouldAutoDealHand = inIsOn;
        this.IsDirty = true;
    }

    public void HandleAllowSelectPowerCardTypeToggleChanged(bool inIsOn)
    {
        SettingsManager.IN.AllowSelectPowerCardType = inIsOn;
        this.IsDirty = true;
    }

    public void HandleRestoreDefaultsButtonPress()
    {
        string titleText = LOC.IN.RestoreDefaultsTitle[LOC.LI];//"Restore Defaults?";
        string messageText = LOC.IN.RestoreDefaultsMessage[LOC.LI];//$"Are you sure you want to restore all settings to default?";
        UIConfirmPanel.IN.Show(titleText, messageText, () =>
        {
            SettingsManager.IN.ResetToDefaults();
            LocalizationManager.IN.SelectLanguage(0);
            SetSliderValuesToSettings();
            this.SkinSelectPanel.Reset();
            this.IsDirty = true;
        });
    }

    public void HandleClearDataButtonPress()
    {
        string titleText = LOC.IN.ResetDataTitle[LOC.LI];//"Reset Data?";
        string messageText = LOC.IN.ResetDataMessage[LOC.LI];//$"Are you sure you want to Delete All Data?\nThis will quit the app!";
        UIConfirmPanel.IN.Show(titleText, messageText, () =>
        {
            this.IsResettingData = true;
            PlayerPrefs.DeleteAll();
            Application.Quit();
        });
    }

    public void HandleCategoryToggleChanged(UISettingsTab inToggle)
    {
        if (inToggle.IsOn)
        {
            inToggle.SetState(true, false);
        }
        else
        {
            inToggle.SetState(false, false);
        }
    }

    public void SelectCategory(int inIndex)
    {
        for (int i = 0; i < this.categoryPanels.Length; i++)
        {
            bool shouldShow = i == inIndex;
            var category = this.categoryPanels[i];
            category.SetActive(shouldShow);
        }
    }

    public override void Hide()
    {
        GameManager.IN.SetGamePaused(false);

        LocalizationManager.OnLanguageChanged -= RefreshDropdownsLanguage;

        if (this.SkinSelectPanel.IsDirty)
            this.SkinSelectPanel.HandleApplyButtonPress();

        base.Hide();

        if (this.IsDirty)
        {
            this.IsDirty = false;

            PlayerData.Data.IsDirty = true;

            SaveManager.IN.SaveGame();

            if (!UISplashScreen.IN.IsShowing)
                GameManager.IN.OnSettingsChanged();
        }
    }

    private void RefreshDropdownsLanguage(int inLanguageIndex)
    {
        //Discard Mode
        this.discardModeDropdown.ClearOptions();
        var discardOptions = new List<string>();
        foreach (var item in LOC.IN.DropdownOptions_DiscardMode)
        {
            discardOptions.Add(item.Options[inLanguageIndex]);
        }
        this.discardModeDropdown.AddOptions(discardOptions);
        this.discardModeDropdown.RefreshShownValue();

        //Play Mode
        this.playModeDropdown.ClearOptions();
        var playModeOptions = new List<string>();
        foreach (var item in LOC.IN.DropdownOptions_PlayMode)
        {
            playModeOptions.Add(item.Options[inLanguageIndex]);
        }
        this.playModeDropdown.AddOptions(playModeOptions);
        this.playModeDropdown.RefreshShownValue();

        //AI Difficulty
        this.aiDifficultyDropdown.ClearOptions();
        var aiDifficultyOptions = new List<string>();
        foreach (var item in LOC.IN.DropdownOptions_AiDifficulty)
        {
            aiDifficultyOptions.Add(item.Options[inLanguageIndex]);
        }
        this.aiDifficultyDropdown.AddOptions(aiDifficultyOptions);
        this.aiDifficultyDropdown.RefreshShownValue();

        this.SkinSelectPanel.RefreshDropdowns(inLanguageIndex);

        SetSliderValuesToSettings();
    }
}