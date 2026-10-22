using System;
using System.Collections.Generic;
using Elemento.Networking.Supabase;
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
    [SerializeField] private Toggle allowUndoToggle;
    [SerializeField] private TMP_Dropdown selectLanguageDropdown;
    [SerializeField] private TMP_Dropdown discardModeDropdown;
    [SerializeField] private TMP_Dropdown playModeDropdown;
    [SerializeField] private LocalizationItem playModeLockedNote;
    [SerializeField] private TMP_Dropdown aiDifficultyDropdown;
    [SerializeField] private TextMeshProUGUI dataPathText;
    [SerializeField] private GameObject[] categoryPanels;

    public bool IsDirty;
    public bool IsResettingData;

    private bool isInitialized;
    private bool isDebugAccessInitialized;
    private int debugTitleTapCount;
    private float lastDebugTitleTapTime;
    private GameObject aiGameDebugMenu;
    private TextMeshProUGUI aiGameRecordingButtonLabel;
    private TextMeshProUGUI settingsTitleText;

    private void Start()
    {
        if (!Initialize())
            return;

        RefreshPlayModeAvailability(GameManager.IN != null && GameManager.IN.IsGameInProgress);

        if (!Application.isEditor)
            this.filePath = $"{Application.persistentDataPath}/PlayerData.json";

        this.dataPathText.text = $"Data Path: {this.filePath}";
    }

    public override void Show()
    {
        if (!Initialize())
            return;

        base.Show();

        GameManager.IN.SetGamePaused(true);
        RefreshPlayModeAvailability(GameManager.IN.IsGameInProgress);

        LocalizationManager.OnLanguageChanged += RefreshDropdownsLanguage;
        RefreshDropdownsLanguage(LOC.LI);

        if (!this.isInitialized)
            SelectCategory(0);

        this.isInitialized = true;
    }

    private bool Initialize()
    {
        var missingReferences = new List<string>();

        if (this.SkinSelectPanel == null)
            missingReferences.Add(nameof(this.SkinSelectPanel));

        if (this.sliders == null || this.sliders.Length < 10)
        {
            missingReferences.Add($"{nameof(this.sliders)} (10 required)");
        }
        else
        {
            for (int i = 0; i < 10; i++)
            {
                if (this.sliders[i] == null)
                    missingReferences.Add($"{nameof(this.sliders)}[{i}]");
            }
        }

        if (this.showSplashScreenToggle == null)
            missingReferences.Add(nameof(this.showSplashScreenToggle));
        if (this.autoDealHandToggle == null)
            missingReferences.Add(nameof(this.autoDealHandToggle));
        if (this.allowSelectPowerCardTypeToggle == null)
            missingReferences.Add(nameof(this.allowSelectPowerCardTypeToggle));
        if (this.allowUndoToggle == null)
            missingReferences.Add(nameof(this.allowUndoToggle));
        if (this.selectLanguageDropdown == null)
            missingReferences.Add(nameof(this.selectLanguageDropdown));
        if (this.discardModeDropdown == null)
            missingReferences.Add(nameof(this.discardModeDropdown));
        if (this.playModeDropdown == null)
            missingReferences.Add(nameof(this.playModeDropdown));
        if (this.playModeLockedNote == null)
            missingReferences.Add(nameof(this.playModeLockedNote));
        if (this.aiDifficultyDropdown == null)
            missingReferences.Add(nameof(this.aiDifficultyDropdown));
        if (this.dataPathText == null)
            missingReferences.Add(nameof(this.dataPathText));

        if (this.categoryPanels == null || this.categoryPanels.Length == 0)
        {
            missingReferences.Add(nameof(this.categoryPanels));
        }
        else
        {
            for (int i = 0; i < this.categoryPanels.Length; i++)
            {
                if (this.categoryPanels[i] == null)
                    missingReferences.Add($"{nameof(this.categoryPanels)}[{i}]");
            }
        }

        var playModeLockedText = this.playModeLockedNote == null
            ? null
            : this.playModeLockedNote.GetComponent<TextMeshProUGUI>();
        if (this.playModeLockedNote != null && playModeLockedText == null)
            missingReferences.Add($"{nameof(this.playModeLockedNote)} TextMeshProUGUI");

        if (missingReferences.Count > 0)
        {
            Debug.LogError($"{nameof(UISettingsPanel)} is missing required references: {string.Join(", ", missingReferences)}.", this);
            return false;
        }

        this.playModeLockedNote.localizedTexts = LOC.IN.ReturnHomeToChangeMode;
        playModeLockedText.text = LOC.IN.ReturnHomeToChangeMode[LOC.LI];
        InitializeDebugAccess();
        return true;
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
        this.allowUndoToggle.SetIsOnWithoutNotify(SettingsManager.IN.AllowUndo);

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
        if (GameManager.IN != null && GameManager.IN.IsGameInProgress)
        {
            this.playModeDropdown.SetValueWithoutNotify((int)SettingsManager.IN.PlayMode);
            return;
        }

        SetPlayMode((EPlayMode)inValue);
        this.IsDirty = true;
    }

    public void SetPlayMode(EPlayMode inPlayMode)
    {
        SettingsManager.IN.PlayMode = inPlayMode;
        this.playModeDropdown.SetValueWithoutNotify((int)inPlayMode);
        this.playModeDropdown.RefreshShownValue();
        RefreshAiDifficultyVisibility();
    }

    public void RefreshPlayModeAvailability(bool inIsGameInProgress)
    {
        this.playModeDropdown.interactable = !inIsGameInProgress;
        this.playModeLockedNote.gameObject.SetActive(inIsGameInProgress);
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

    public void HandleAllowUndoToggleChanged(bool inIsOn)
    {
        SettingsManager.IN.SetAllowUndoPreference(inIsOn);
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
        this.debugTitleTapCount = 0;
        if (this.aiGameDebugMenu != null)
            this.aiGameDebugMenu.SetActive(false);

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

    public void HandleDebugTitlePress()
    {
        if (Time.unscaledTime - this.lastDebugTitleTapTime > 4f)
            this.debugTitleTapCount = 0;

        this.lastDebugTitleTapTime = Time.unscaledTime;
        this.debugTitleTapCount++;
        if (this.debugTitleTapCount < 10)
            return;

        this.debugTitleTapCount = 0;
        ShowAiGameDebugMenu();
    }

    private void InitializeDebugAccess()
    {
        if (this.isDebugAccessInitialized)
            return;

        foreach (var text in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (text.name == "Title Text")
            {
                this.settingsTitleText = text;
                break;
            }
        }

        if (this.settingsTitleText == null)
        {
            Debug.LogWarning("Settings debug access could not find Title Text.", this);
            return;
        }

        Button titleButton = this.settingsTitleText.GetComponent<Button>();
        if (titleButton == null)
            titleButton = this.settingsTitleText.gameObject.AddComponent<Button>();
        titleButton.transition = Selectable.Transition.None;
        titleButton.targetGraphic = this.settingsTitleText;
        titleButton.onClick.AddListener(HandleDebugTitlePress);
        this.isDebugAccessInitialized = true;
    }

    private void ShowAiGameDebugMenu()
    {
        if (this.aiGameDebugMenu == null)
            BuildAiGameDebugMenu();

        RefreshAiGameRecordingLabel();

        TextMeshProUGUI title = this.aiGameDebugMenu.transform.Find("Title")
            ?.GetComponent<TextMeshProUGUI>();
        if (title != null)
        {
            string userId = GameManager.IN?.AiGameTelemetry?.UserId;
            title.text = "AI Game Debug\n<font-weight=400><size=22>Supabase user: "
                + (string.IsNullOrEmpty(userId) ? "created on first upload" : userId) + "</size></font-weight>";
        }

        this.aiGameDebugMenu.SetActive(true);
        this.aiGameDebugMenu.transform.SetAsLastSibling();
    }

    private void BuildAiGameDebugMenu()
    {
        Transform parent = this.settingsTitleText.transform.parent;
        this.aiGameDebugMenu = new GameObject("AI Game Debug Menu",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
            typeof(VerticalLayoutGroup));
        var rect = (RectTransform)this.aiGameDebugMenu.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(.5f, .5f);
        rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(760, 740);

        var background = this.aiGameDebugMenu.GetComponent<Image>();
        background.color = new Color(.05f, .05f, .05f, .98f);

        var layout = this.aiGameDebugMenu.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(45, 45, 30, 30);
        layout.spacing = 14;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateDebugText("Title", 100, 34);
        this.aiGameRecordingButtonLabel = CreateDebugButton("AI Game Recording", ToggleAiGameRecording);
        CreateDebugButton("Clear This Installation's Server Games",
            () => ConfirmClearServerAiGames(false));
        CreateDebugButton("Clear All Server AI Games",
            () => ConfirmClearServerAiGames(true));
        CreateDebugButton("Clear Local Pending AI Games", ConfirmClearLocalAiGames);
        CreateDebugButton("Open Supabase Database", () => Application.OpenURL(SupabaseConfig.DashboardUrl));
        CreateDebugButton("Close", () => this.aiGameDebugMenu.SetActive(false));
    }

    private TextMeshProUGUI CreateDebugText(string inName, float inHeight, float inFontSize)
    {
        var textObject = new GameObject(inName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(this.aiGameDebugMenu.transform, false);
        var text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = this.settingsTitleText.font;
        text.fontSize = inFontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        textObject.GetComponent<LayoutElement>().preferredHeight = inHeight;
        return text;
    }

    private TextMeshProUGUI CreateDebugButton(string inLabel, Action inOnPress)
    {
        var buttonObject = new GameObject(inLabel, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonObject.transform.SetParent(this.aiGameDebugMenu.transform, false);
        var image = buttonObject.GetComponent<Image>();
        image.color = new Color(.22f, .22f, .22f, 1f);
        var button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => inOnPress());
        buttonObject.GetComponent<LayoutElement>().preferredHeight = 78;

        TextMeshProUGUI label = CreateDebugText(inLabel + " Label", 78, 27);
        label.transform.SetParent(buttonObject.transform, false);
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12, 4);
        labelRect.offsetMax = new Vector2(-12, -4);
        label.text = inLabel;
        UnityEngine.Object.Destroy(label.GetComponent<LayoutElement>());
        return label;
    }

    private void ToggleAiGameRecording()
    {
        if (GameManager.IN?.AiGameTelemetry == null)
        {
            ShowDebugStatus("AI game telemetry is unavailable.", true);
            return;
        }

        bool isEnabled = !GameManager.IN.AiGameTelemetry.IsRecordingEnabled;
        GameManager.IN.AiGameTelemetry.SetRecordingEnabled(isEnabled);
        RefreshAiGameRecordingLabel();
        ShowDebugStatus("AI game recording " + (isEnabled ? "enabled." : "disabled."), false);
    }

    private void RefreshAiGameRecordingLabel()
    {
        if (this.aiGameRecordingButtonLabel == null)
            return;

        var telemetry = GameManager.IN?.AiGameTelemetry;
        this.aiGameRecordingButtonLabel.text = telemetry == null
            ? "AI Game Recording: UNAVAILABLE"
            : "AI Game Recording: " + (telemetry.IsRecordingEnabled ? "ON" : "OFF");
    }

    private void ConfirmClearServerAiGames(bool inAllGames)
    {
        if (GameManager.IN?.AiGameTelemetry == null)
        {
            ShowDebugStatus("AI game telemetry is unavailable.", true);
            return;
        }

        string target = inAllGames ? "every saved AI game on the server"
            : "the server games owned by this installation";
        UIConfirmPanel.IN.transform.SetAsLastSibling();
        UIConfirmPanel.IN.Show("Clear AI Games?", "Permanently delete " + target + "?",
            async () =>
            {
                GameManager.IN.SetGamePaused(true);
                try
                {
                    int deleted = await GameManager.IN.AiGameTelemetry.ClearServerAsync(inAllGames);
                    ShowDebugStatus("Deleted " + deleted + " saved AI game(s).", false);
                }
                catch (Exception exception)
                {
                    ShowDebugStatus("Clear failed: " + exception.Message, true);
                }
            }, () => GameManager.IN.SetGamePaused(true), "Cancel", "Delete");
    }

    private void ConfirmClearLocalAiGames()
    {
        if (GameManager.IN?.AiGameTelemetry == null)
        {
            ShowDebugStatus("AI game telemetry is unavailable.", true);
            return;
        }

        UIConfirmPanel.IN.transform.SetAsLastSibling();
        UIConfirmPanel.IN.Show("Clear Local AI Games?",
            "Delete the active recovery journal and all uploads pending on this device?",
            () =>
            {
                GameManager.IN.SetGamePaused(true);
                GameManager.IN.AiGameTelemetry.ClearLocal();
                ShowDebugStatus("Local pending AI games cleared.", false);
            }, () => GameManager.IN.SetGamePaused(true), "Cancel", "Delete");
    }

    private static void ShowDebugStatus(string inMessage, bool inIsError)
    {
        UIAlertPanel.IN.transform.SetAsLastSibling();
        UIAlertPanel.IN.Show(inMessage, 4f, inIsError
            ? UIAlertPanel.EAlertMode.Warning : UIAlertPanel.EAlertMode.Notification);
    }
}
