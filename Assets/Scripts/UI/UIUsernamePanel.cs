using System;
using Elemento.Networking.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIUsernamePanel : UIPanelBase
{
    public static UIUsernamePanel IN;

    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TextMeshProUGUI placeholderText;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI checkText;
    [SerializeField] private TextMeshProUGUI continueText;
    [SerializeField] private TextMeshProUGUI backText;
    [SerializeField] private Button checkButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button backButton;

    private SupabaseProfileClient profiles;
    private Action onContinue;
    private Action onBack;
    private bool isBusy;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
    }

    public void Configure(SupabaseProfileClient inProfiles, Action inOnContinue, Action inOnBack)
    {
        this.profiles = inProfiles;
        this.onContinue = inOnContinue;
        this.onBack = inOnBack;
        this.usernameInput.text = string.Empty;
        this.statusText.text = string.Empty;
        RefreshText();
        Show();
    }

    public async void HandleCheckButtonPress()
    {
        if (this.isBusy || this.profiles == null) return;
        SetBusy(true);
        try
        {
            bool available = await this.profiles.IsUsernameAvailableAsync(this.usernameInput.text.Trim());
            this.statusText.text = LocalizationManager.GetOnlineText(available
                ? LocalizationManager.IN.OnlineUsernameAvailable : LocalizationManager.IN.OnlineUsernameUnavailable);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Username check failed: " + exception.Message);
            this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineError);
        }
        finally { SetBusy(false); }
    }

    public async void HandleContinueButtonPress()
    {
        if (this.isBusy || this.profiles == null) return;
        SetBusy(true);
        try
        {
            string username = this.usernameInput.text.Trim();
            if (!await this.profiles.IsUsernameAvailableAsync(username))
            {
                this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineUsernameUnavailable);
                return;
            }
            await this.profiles.CreateProfileAsync(username);
            Hide();
            this.onContinue?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Profile creation failed: " + exception.Message);
            this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineError);
        }
        finally { SetBusy(false); }
    }

    public void HandleBackButtonPress()
    {
        if (this.isBusy) return;
        Hide();
        this.onBack?.Invoke();
    }

    private void SetBusy(bool inIsBusy)
    {
        this.isBusy = inIsBusy;
        this.usernameInput.interactable = !inIsBusy;
        this.checkButton.interactable = !inIsBusy;
        this.continueButton.interactable = !inIsBusy;
        this.backButton.interactable = !inIsBusy;
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void RefreshText()
    {
        var loc = LocalizationManager.IN;
        if (loc == null) return;
        this.titleText.text = LocalizationManager.GetOnlineText(loc.OnlineUsername);
        this.hintText.text = LocalizationManager.GetOnlineText(loc.OnlineUsernameHint);
        this.placeholderText.text = LocalizationManager.GetOnlineText(loc.OnlineUsernamePlaceholder);
        this.checkText.text = LocalizationManager.GetOnlineText(loc.OnlineCheckUsername);
        this.continueText.text = LocalizationManager.GetOnlineText(loc.OnlineContinue);
        this.backText.text = LocalizationManager.GetOnlineText(loc.OnlineBack);
    }
}
