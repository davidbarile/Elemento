using System;
using Elemento.Domain.Net;
using Elemento.Networking.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIOnlineLobbyPanel : UIPanelBase
{
    public static UIOnlineLobbyPanel IN;

    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI createText;
    [SerializeField] private TextMeshProUGUI joinText;
    [SerializeField] private TextMeshProUGUI backText;
    [SerializeField] private Button createButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button backButton;

    private SupabaseAuthClient auth;
    private SupabaseProfileClient profiles;
    private SupabaseMatchTransport transport;
    private OnlineMatchClient client;
    private string inviteCode;
    private bool isBusy;
    private bool isProfileReady;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        ReleaseMatch();
    }

    public override async void Show()
    {
        if (this.isBusy) return;
        ReleaseMatch();
        base.Show();
        this.inviteCode = null;
        this.statusText.text = string.Empty;
        this.inviteCodeInput.text = string.Empty;
        RefreshText();
        SetBusy(true);
        try
        {
            this.auth ??= new SupabaseAuthClient(this);
            this.profiles ??= new SupabaseProfileClient(this.auth);
            await this.auth.EnsureSessionAsync();
            this.isProfileReady = !string.IsNullOrEmpty(await this.profiles.GetUsernameAsync());
            if (!this.isProfileReady)
            {
                base.Hide();
                UIUsernamePanel.IN.Configure(this.profiles, HandleProfileReady, HandleBackButtonPress);
            }
            this.statusText.text = string.Empty;
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void HandleProfileReady()
    {
        this.isProfileReady = true;
        base.Show();
        SetBusy(false);
    }

    public async void HandleCreateButtonPress()
    {
        if (this.isBusy || !this.isProfileReady || this.client != null) return;
        SetBusy(true);
        try
        {
            this.transport = new SupabaseMatchTransport(this, this.auth);
            var match = await this.transport.CreateMatch("live");
            this.inviteCode = match.InviteCode;
            this.statusText.text = string.Format(LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting), this.inviteCode);
            StartMatch(match);
        }
        catch (Exception exception) { ReleaseMatch(); ShowError(exception); }
        finally { SetBusy(false); }
    }

    public async void HandleJoinButtonPress()
    {
        if (this.isBusy || !this.isProfileReady || this.client != null) return;
        string code = this.inviteCodeInput.text.Trim().ToUpperInvariant();
        if (code.Length != 6)
        {
            this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineInvalidInvite);
            return;
        }
        SetBusy(true);
        try
        {
            this.transport = new SupabaseMatchTransport(this, this.auth);
            var match = await this.transport.JoinMatch(code);
            StartMatch(match);
        }
        catch (Exception exception) { ReleaseMatch(); ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void StartMatch(SupabaseMatchData inMatch)
    {
        this.client = new OnlineMatchClient(this.transport, inMatch.MatchId, inMatch.Seat);
        this.client.ViewChanged += HandleInitialView;
        this.transport.StartListening(inMatch.MatchId, "live");
        // A waiting creator has no seat view yet; polling retries once the opponent joins.
        _ = this.client.RefreshAsync(null, exception => Debug.LogWarning("Waiting for seat view: " + exception.Message));
    }

    private void HandleInitialView(MatchSeatViewData inView)
    {
        if (inView == null || this.client == null) return;
        this.client.ViewChanged -= HandleInitialView;
        OnlineMatchContext.Set(this.client);
        Hide();
        GameManager.IN.RestartGame();
    }

    public void HandleBackButtonPress()
    {
        if (this.isBusy) return;
        ReleaseMatch();
        Hide();
        UISplashScreen.IN.Show();
    }

    public void ReleaseMatch()
    {
        if (this.client != null)
        {
            this.client.ViewChanged -= HandleInitialView;
            if (OnlineMatchContext.Client == this.client) OnlineMatchContext.Clear();
            this.client.Dispose();
            this.client = null;
        }
        this.transport?.Dispose();
        this.transport = null;
    }

    private void ShowError(Exception inException)
    {
        Debug.LogWarning("Online lobby failed: " + inException.Message);
        this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineError);
    }

    private void SetBusy(bool inIsBusy)
    {
        this.isBusy = inIsBusy;
        this.createButton.interactable = !inIsBusy && this.isProfileReady && this.client == null;
        this.joinButton.interactable = !inIsBusy && this.isProfileReady && this.client == null;
        this.inviteCodeInput.interactable = !inIsBusy && this.client == null;
        this.backButton.interactable = !inIsBusy;
        if (inIsBusy) this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWorking);
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void RefreshText()
    {
        var loc = LocalizationManager.IN;
        if (loc == null) return;
        this.titleText.text = LocalizationManager.GetOnlineText(loc.OnlineLobby);
        this.hintText.text = LocalizationManager.GetOnlineText(loc.OnlineInviteCode);
        this.createText.text = LocalizationManager.GetOnlineText(loc.OnlineCreateMatch);
        this.joinText.text = LocalizationManager.GetOnlineText(loc.OnlineJoinMatch);
        this.backText.text = LocalizationManager.GetOnlineText(loc.OnlineBack);
        if (!string.IsNullOrEmpty(this.inviteCode))
            this.statusText.text = string.Format(LocalizationManager.GetOnlineText(loc.OnlineWaiting), this.inviteCode);
    }
}
