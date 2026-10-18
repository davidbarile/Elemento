using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using Elemento.Networking.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UIAlertPanel;

public sealed class UIOnlineLobbyPanel : UIPanelBase
{
    public static UIOnlineLobbyPanel IN;

    private const string activeMatchIdKey = "Online.ActiveMatchId";
    private const string matchStateKeyPrefix = "Online.MatchState.";
    private const string pendingSubmissionKeyPrefix = "Online.PendingSubmission.";

    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TextMeshProUGUI placeholderText;
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
    private bool isResuming;
    private bool isRecoveryPending;
    private GameObject resumeList;
    private readonly List<Button> resumeButtons = new();

    public bool IsRecoveryPending => this.isRecoveryPending;

    private void Awake()
    {
        this.inviteCodeInput.gameObject.AddComponent<UIInputPlaceholderToggle>().Initialize(this.inviteCodeInput);
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        ReleaseMatch();
    }

    private async void OnApplicationFocus(bool inHasFocus)
    {
        if (!inHasFocus || this.client == null)
            return;
        try
        {
            await this.client.HeartbeatAsync();
        }
        catch (Exception exception)
        {
            HandleTransportFailed(exception);
        }
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
        this.isProfileReady = false;
        SetBusy(true);
        string automaticResumeMatchId = null;
        try
        {
            this.auth ??= new SupabaseAuthClient(this);
            this.profiles ??= new SupabaseProfileClient(this.auth);
            await this.profiles.EnsureProfileAsync();
            this.isProfileReady = true;
            this.statusText.text = string.Empty;
            automaticResumeMatchId = await LoadResumableMatches();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }

        if (!string.IsNullOrEmpty(automaticResumeMatchId))
            ResumeMatch(automaticResumeMatchId);
    }

    public async void HandleCreateButtonPress()
    {
        if (this.isBusy || !this.isProfileReady || this.client != null) return;
        SetBusy(true);
        try
        {
            CreateClient();
            var rules = UnityDomainMapper.CreateRules(SettingsManager.IN, GameManager.IN.PlayerBoards,
                CardsManager.IN.CardSpawnBatchSize);
            var match = await this.client.CreateAsync(InitialMatchFactory.Create(rules));
            this.inviteCode = this.transport.LastInviteCode;
            this.statusText.text = GetWaitingText();
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
            CreateClient();
            var match = await this.client.JoinAsync(code);
            StartMatch(match);
        }
        catch (Exception exception) { ReleaseMatch(); ShowError(exception); }
        finally { SetBusy(false); }
    }

    private void CreateClient()
    {
        this.transport = new SupabaseMatchTransport(this, this.auth);
        this.transport.RequestFailed += HandleTransportFailed;
        this.client = new OnlineMatchClient(this.transport);
        this.client.StateChanged += HandlePersistentStateChanged;
        this.client.PendingSubmissionChanged += HandlePendingSubmissionChanged;
    }

    private void StartMatch(MatchStateUpdateData inMatch, bool inStartListening = true)
    {
        // TODO: Support optional per-player handicap settings separately from these shared match rules.
        SettingsManager.IN.ApplyMatchRules(inMatch.Rules ?? inMatch.State.Rules);
        this.client.StateChanged += HandleInitialView;
        this.client.RequestFailed += HandleTransportFailed;
        if (inStartListening)
            this.transport.StartListening(this.client);
        if (inMatch.Status == "waiting")
        {
            this.statusText.text = GetWaitingText();
            return;
        }
        HandleInitialView(inMatch);
    }

    private void HandleInitialView(MatchStateUpdateData inView)
    {
        if (inView?.State == null || inView.Status == "waiting" || this.client == null) return;
        this.client.StateChanged -= HandleInitialView;
        OnlineMatchContext.Set(this.client);
        if (!this.isRecoveryPending)
            Hide();
        GameManager.IN.RestartGame();
        bool isGoingFirst = inView.State.StartingPlayerId == inView.YourSeat;
        string[] message = isGoingFirst
            ? LocalizationManager.IN.OnlineYouGoFirst
            : LocalizationManager.IN.OnlineOpponentGoesFirst;

        if (!this.isResuming)
        {
            UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(message), 2f, EAlertMode.Notification);
        }  
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
            this.client.StateChanged -= HandleInitialView;
            this.client.StateChanged -= HandlePersistentStateChanged;
            this.client.RequestFailed -= HandleTransportFailed;
            this.client.PendingSubmissionChanged -= HandlePendingSubmissionChanged;
            if (OnlineMatchContext.Client == this.client) OnlineMatchContext.Clear();
            this.client.Dispose();
            this.client = null;
        }
        if (this.transport != null)
        {
            this.transport.RequestFailed -= HandleTransportFailed;
            this.transport.Dispose();
        }
        this.transport = null;
        this.isResuming = false;
        this.isRecoveryPending = false;
        SettingsManager.IN?.ClearMatchRules();
    }

    private void ShowError(Exception inException)
    {
        Debug.LogWarning("Online lobby failed: " + inException.Message);
        string error = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineError);
        if (inException is SupabaseHttpException http && http.ResponseCode == 426)
        {
            error = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineUpdateRequired);
            this.statusText.text = error;
            UIAlertPanel.IN.Show(error, 5f, EAlertMode.Notification);
            return;
        }
#if UNITY_EDITOR
        bool includeReason = true;
#else
        bool includeReason = Debug.isDebugBuild;
#endif
        if (includeReason)
        {
            string reason = inException.Message.Replace('\r', ' ').Replace('\n', ' ');
            if (reason.Length > 80)
                reason = reason.Substring(0, 77) + "...";
            error += " (" + reason + ")";
        }
        this.statusText.text = error;
    }

    private void HandleTransportFailed(Exception inException)
    {
        if (inException is SupabaseHttpException)
            ShowError(inException);
        else
            Debug.LogWarning("Online transport ignored a non-HTTP failure: " + inException.Message);
    }

    private void SetBusy(bool inIsBusy)
    {
        this.isBusy = inIsBusy;
        this.createButton.interactable = !inIsBusy && this.isProfileReady && this.client == null;
        this.joinButton.interactable = !inIsBusy && this.isProfileReady && this.client == null;
        this.inviteCodeInput.interactable = !inIsBusy && this.client == null;
        this.backButton.interactable = !inIsBusy;
        foreach (var button in this.resumeButtons)
            button.interactable = !inIsBusy && this.isProfileReady && this.client == null;
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
        this.placeholderText.text = LocalizationManager.GetOnlineText(loc.OnlineInviteCodePlaceholder);
        this.createText.text = LocalizationManager.GetOnlineText(loc.OnlineCreateMatch);
        this.joinText.text = LocalizationManager.GetOnlineText(loc.OnlineJoinMatch);
        this.backText.text = LocalizationManager.GetOnlineText(loc.OnlineBack);
        if (!string.IsNullOrEmpty(this.inviteCode))
            this.statusText.text = GetWaitingText();
    }

    private string GetWaitingText()
    {
        if (string.IsNullOrEmpty(this.inviteCode))
            return LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting);
        return LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineInviteCodeLabel)
            + ": <color=#FFDD00>" + this.inviteCode + "</color>\n<size=80%>"
            + LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting) + "</size>";
    }

    private async Task<string> LoadResumableMatches()
    {
        if (this.resumeList != null) Destroy(this.resumeList);
        this.resumeButtons.Clear();
        using var lookup = new SupabaseMatchTransport(this, this.auth);
        var result = await lookup.ListMyMatches(new ListMyMatchesRequestData());
        string savedMatchId = PlayerPrefs.GetString(activeMatchIdKey, string.Empty);
        string automaticResumeMatchId = null;
        if (result.Matches.Count == 0)
        {
            if (!string.IsNullOrEmpty(savedMatchId))
                ForgetPersistedMatch(savedMatchId);
            return null;
        }

        this.resumeList = new GameObject("Resume matches", typeof(RectTransform), typeof(RectMask2D), typeof(ScrollRect));
        var viewport = (RectTransform)this.resumeList.transform;
        viewport.SetParent(this.transform, false);
        viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
        viewport.sizeDelta = new Vector2(500f, 120f);
        viewport.anchoredPosition = new Vector2(0f, -440f);
        var content = new GameObject("Matches", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var contentRect = (RectTransform)content.transform;
        contentRect.SetParent(viewport, false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 10f;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = this.resumeList.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = contentRect;
        scroll.horizontal = false;
        foreach (var match in result.Matches)
        {
            if (match.MatchId == savedMatchId)
                automaticResumeMatchId = match.MatchId;
            // Reuse the lobby button's complete visual hierarchy and clear its serialized action.
            var row = Instantiate(this.createButton.transform.parent.gameObject, contentRect);
            ((RectTransform)row.transform).sizeDelta = new Vector2(500f, 75f);
            var button = row.GetComponentInChildren<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => ResumeMatch(match.MatchId));
            var label = row.GetComponentInChildren<TextMeshProUGUI>();
            label.richText = false;
            label.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineResumeMatch)
                + " — " + (string.IsNullOrEmpty(match.OpponentName)
                    ? LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting) : match.OpponentName);
            this.resumeButtons.Add(button);
        }
        if (!string.IsNullOrEmpty(savedMatchId) && automaticResumeMatchId == null)
            ForgetPersistedMatch(savedMatchId);
        return automaticResumeMatchId;
    }

    private async void ResumeMatch(string inMatchId)
    {
        if (this.isBusy || !this.isProfileReady || this.client != null) return;
        SetBusy(true);
        try
        {
            this.isResuming = true;
            CreateClient();
            MatchStateUpdateData checkpoint = LoadCheckpoint(inMatchId);
            if (checkpoint != null)
            {
                this.isRecoveryPending = true;
                this.client.RestoreState(checkpoint);
            }

            SubmitTurnRequestData pending = LoadPendingSubmission(inMatchId);
            if (pending != null)
            {
                this.client.RestorePendingSubmission(pending);
                SubmitTurnResultData result = await this.client.RetryPendingSubmissionAsync();
                if (checkpoint != null && result?.Accepted == true
                    && pending.Turn.FinalState != null)
                {
                    checkpoint.MatchRevision = result.NewRevision;
                    checkpoint.CurrentSeat = result.CurrentSeat;
                    checkpoint.Status = result.Status;
                    checkpoint.WinnerSeat = pending.Turn.WinnerSeat;
                    checkpoint.State = pending.Turn.FinalState;
                    checkpoint.Turns.Clear();
                    this.client.RestoreState(checkpoint);
                }
            }

            if (checkpoint != null)
                StartMatch(checkpoint, false);
            MatchStateUpdateData match = await this.client.ResumeAsync(inMatchId);
            if (checkpoint == null)
                StartMatch(match, false);
            else
            {
                this.isRecoveryPending = false;
                if (match.Status != "waiting")
                    Hide();
            }
            if (this.client == null || this.transport == null)
                return;
            this.transport.StartListening(this.client);
        }
        catch (Exception exception)
        {
            if (OnlineMatchContext.Client == this.client)
                GameManager.IN.AbortOnlineRecovery();
            else
                ReleaseMatch();
            ShowError(exception);
        }
        finally { SetBusy(false); }
    }

    private void HandlePersistentStateChanged(MatchStateUpdateData inState)
    {
        if (inState == null || string.IsNullOrEmpty(inState.MatchId))
            return;
        if (inState.Status != "waiting" && inState.Status != "active")
        {
            ForgetPersistedMatch(inState.MatchId);
            return;
        }

        PlayerPrefs.SetString(activeMatchIdKey, inState.MatchId);
        PlayerPrefs.SetString(matchStateKeyPrefix + inState.MatchId, NetJson.Serialize(inState));
        PlayerPrefs.Save();
    }

    private void HandlePendingSubmissionChanged(SubmitTurnRequestData inSubmission)
    {
        string matchId = inSubmission?.MatchId ?? this.client?.MatchId;
        if (string.IsNullOrEmpty(matchId))
            return;
        string key = pendingSubmissionKeyPrefix + matchId;
        if (inSubmission == null)
            PlayerPrefs.DeleteKey(key);
        else
            PlayerPrefs.SetString(key, NetJson.Serialize(inSubmission));
        PlayerPrefs.Save();
    }

    private static MatchStateUpdateData LoadCheckpoint(string inMatchId)
    {
        return LoadPersisted<MatchStateUpdateData>(matchStateKeyPrefix + inMatchId);
    }

    private static SubmitTurnRequestData LoadPendingSubmission(string inMatchId)
    {
        return LoadPersisted<SubmitTurnRequestData>(pendingSubmissionKeyPrefix + inMatchId);
    }

    private static T LoadPersisted<T>(string inKey) where T : class
    {
        string json = PlayerPrefs.GetString(inKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return NetJson.Deserialize<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Ignoring invalid saved online match data: " + exception.Message);
            PlayerPrefs.DeleteKey(inKey);
            PlayerPrefs.Save();
            return null;
        }
    }

    public static bool HasPersistedMatch()
    {
        return !string.IsNullOrEmpty(PlayerPrefs.GetString(activeMatchIdKey, string.Empty));
    }

    public static void ForgetPersistedMatch(string inMatchId)
    {
        if (string.IsNullOrEmpty(inMatchId))
            return;
        if (PlayerPrefs.GetString(activeMatchIdKey, string.Empty) == inMatchId)
            PlayerPrefs.DeleteKey(activeMatchIdKey);
        PlayerPrefs.DeleteKey(matchStateKeyPrefix + inMatchId);
        PlayerPrefs.DeleteKey(pendingSubmissionKeyPrefix + inMatchId);
        PlayerPrefs.Save();
    }
}
