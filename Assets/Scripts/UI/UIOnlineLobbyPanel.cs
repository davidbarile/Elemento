using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using Elemento.Networking;
using Elemento.Networking.Supabase;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UIAlertPanel;

public enum EForfeitOutcome
{
    Forfeited,
    AlreadyOver,
    Gone,
    Failed
}

public sealed class UIOnlineLobbyPanel : UIPanelBase
{
    public static UIOnlineLobbyPanel IN;

    private const string activeMatchIdKey = "Online.ActiveMatchId";
    private const string matchStateKeyPrefix = "Online.MatchState.";
    private const string pendingSubmissionKeyPrefix = "Online.PendingSubmission.";
    private const string replayHistoryKeyPrefix = "Online.ReplayHistory.";

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
    [SerializeField] private Button cancelGameButton;
    [SerializeField] private TextMeshProUGUI cancelGameText;
    [SerializeField] private Button shareButton;
    [SerializeField] private TextMeshProUGUI shareText;
    [SerializeField] private GameObject activeGamesRoot;
    [SerializeField] private TextMeshProUGUI activeGamesHeaderText;
    [SerializeField] private RectTransform activeGamesContent;
    [SerializeField] private UIActiveGameRow activeGameRowPrefab;

    private SupabaseAuthClient auth;
    private SupabaseProfileClient profiles;
    private SupabaseMatchTransport transport;
    private OnlineMatchClient client;
    private string inviteCode;
    private bool isBusy;
    private bool isProfileReady;
    private bool isResuming;
    private bool isResumeLoading;
    private bool isRecoveryPending;
    private int resumeGeneration;
    private MyMatchData pendingForfeitConfirmation;
    private bool pendingCancelGameConfirmation;
    private readonly List<UIActiveGameRow> activeGameRows = new();
    private readonly HashSet<string> forgottenMatchIds = new();
    private GameObject cancelGameButtonRoot;

    public bool IsRecoveryPending => this.isRecoveryPending;

    private void Awake()
    {
        this.inviteCodeInput.gameObject.AddComponent<UIInputPlaceholderToggle>().Initialize(this.inviteCodeInput);
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        this.cancelGameButton?.onClick.AddListener(HandleCancelGameButtonPress);
        this.shareButton?.onClick.AddListener(HandleShareButtonPress);

        // Tapping the invite code in the status line copies it again.
        this.statusText.raycastTarget = true;
        Button codeButton = this.statusText.gameObject.AddComponent<Button>();
        codeButton.transition = Selectable.Transition.None;
        codeButton.onClick.AddListener(HandleInviteCodePress);
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        this.cancelGameButton?.onClick.RemoveListener(HandleCancelGameButtonPress);
        this.shareButton?.onClick.RemoveListener(HandleShareButtonPress);
        ReleaseMatch();
    }

    private async void OnApplicationFocus(bool inHasFocus)
    {
        if (!inHasFocus || this.isBusy || this.client == null)
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
        string automaticResumeMatchId = PlayerPrefs.GetString(activeMatchIdKey, string.Empty);
        if (!string.IsNullOrEmpty(automaticResumeMatchId))
            Hide();
        this.inviteCode = null;
        this.statusText.text = string.Empty;
        this.inviteCodeInput.text = string.Empty;
        SetResumeLoading(false);
        RefreshText();
        this.isProfileReady = false;

        if (!string.IsNullOrEmpty(automaticResumeMatchId))
        {
            await ResumeMatchAsync(automaticResumeMatchId, true);
            return;
        }

        await LoadLobbyAsync();
    }

    private async Task LoadLobbyAsync()
    {
        base.Show();
        SetResumeLoading(false);
        SetBusy(true);
        try
        {
            this.auth ??= new SupabaseAuthClient(OnlineCoroutineHost.IN);
            this.profiles ??= new SupabaseProfileClient(this.auth);
            await this.profiles.EnsureProfileAsync();
            this.isProfileReady = true;
            this.statusText.text = string.Empty;
            await LoadActiveGames();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { SetBusy(false); }
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
            CopyInviteCode();
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
        this.transport = new SupabaseMatchTransport(OnlineCoroutineHost.IN, this.auth);
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
        RefreshCancelGameVisibility();
        if (inMatch.Status == "waiting")
        {
            if (!this.isResumeLoading)
                this.statusText.text = GetWaitingText();
            return;
        }
        HandleInitialView(inMatch);
    }

    private void HandleInitialView(MatchStateUpdateData inView)
    {
        RefreshCancelGameVisibility();
        if (inView?.State == null || inView.Status == "waiting" || this.client == null) return;
        if (this.pendingCancelGameConfirmation)
        {
            this.pendingCancelGameConfirmation = false;
            UIConfirmPanel.IN?.Hide();
        }
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

    public async void HandleBackButtonPress()
    {
        if (this.isResumeLoading)
        {
            ++this.resumeGeneration;
            ReleaseMatch();
            SetResumeLoading(false);
            SetBusy(false);
            await LoadLobbyAsync();
            return;
        }
        if (this.isBusy) return;
        ReleaseMatch();
        Hide();
        UISplashScreen.IN.Show();
    }

    public void ReleaseMatch()
    {
        ++this.resumeGeneration;
        this.isBusy = false;
        this.isResumeLoading = false;
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
        this.pendingCancelGameConfirmation = false;
        SettingsManager.IN?.ClearMatchRules();
        RefreshCancelGameVisibility();
    }

    public async Task<EForfeitOutcome> ForfeitMatchAsync(string inMatchId, int inLocalSeat = -1,
        bool inResumeOnFailure = true)
    {
        if (string.IsNullOrEmpty(inMatchId))
            return EForfeitOutcome.Failed;

        ++this.resumeGeneration;
        bool wasResumePending = inResumeOnFailure && this.isBusy && this.isResuming;
        if (wasResumePending && this.client != null)
            this.client.StateChanged -= HandleInitialView;
        EForfeitOutcome outcome = EForfeitOutcome.Failed;
        try
        {
            this.auth ??= new SupabaseAuthClient(OnlineCoroutineHost.IN);
            this.profiles ??= new SupabaseProfileClient(this.auth);
            await this.profiles.EnsureProfileAsync();

            LeaveMatchResultData result;
            if (this.client != null && this.client.MatchId == inMatchId)
            {
                if (inLocalSeat < 0)
                    inLocalSeat = this.client.LocalSeat;
                result = await this.client.LeaveMatchAsync();
            }
            else
            {
                using var leaveTransport = new SupabaseMatchTransport(OnlineCoroutineHost.IN, this.auth);
                MatchStateUpdateData checkpoint = LoadCheckpoint(inMatchId);
                int sinceRevision = Math.Max(checkpoint?.MatchRevision ?? 0, 0);
                MatchStateUpdateData beforeLeave = null;
                try
                {
                    beforeLeave = await leaveTransport.FetchView(new GetViewRequestData
                    {
                        MatchId = inMatchId,
                        SinceRevision = sinceRevision
                    });
                }
                catch (Exception exception) when (!(exception is SupabaseHttpException http
                    && (http.ResponseCode == 403 || http.ResponseCode == 404 || http.ResponseCode == 410)))
                {
                    Debug.LogWarning("Fetching online match before forfeit failed: " + exception.Message);
                }
                if (beforeLeave != null && inLocalSeat < 0)
                    inLocalSeat = beforeLeave.YourSeat;
                bool wasAlreadyOver = beforeLeave != null && (beforeLeave.Status == "finished"
                    || beforeLeave.Status == "abandoned");
                result = await leaveTransport.LeaveMatch(inMatchId);
                result.WasAlreadyOver |= wasAlreadyOver;
            }

            if (result?.Status == "abandoned")
            {
                outcome = result.WasAlreadyOver
                    || (inLocalSeat >= 0 && result.WinnerSeat == inLocalSeat)
                    ? EForfeitOutcome.AlreadyOver
                    : EForfeitOutcome.Forfeited;
            }
            else if (result?.Status == "finished")
            {
                outcome = EForfeitOutcome.AlreadyOver;
            }
        }
        catch (SupabaseHttpException exception) when (exception.ResponseCode == 403
            || exception.ResponseCode == 404 || exception.ResponseCode == 410)
        {
            outcome = EForfeitOutcome.Gone;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Forfeiting online match failed: " + exception.Message);
        }

        if (outcome != EForfeitOutcome.Failed)
            ForgetPersistedMatch(inMatchId);
        else if (wasResumePending)
        {
            ReleaseMatch();
            SetBusy(false);
            _ = ResumeMatchAsync(inMatchId, true);
        }
        return outcome;
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
        this.backButton.interactable = this.isResumeLoading || !inIsBusy;
        foreach (UIActiveGameRow row in this.activeGameRows)
            row.SetInteractable(!inIsBusy && this.isProfileReady && this.client == null);
        if (inIsBusy)
        {
            string[] status = this.isResumeLoading
                ? LocalizationManager.IN.OnlineResuming
                : LocalizationManager.IN.OnlineWorking;
            this.statusText.text = LocalizationManager.GetOnlineText(status);
        }
        RefreshCancelGameVisibility();
    }

    private void SetResumeLoading(bool inIsLoading)
    {
        this.isResumeLoading = inIsLoading;
        this.titleText.gameObject.SetActive(!inIsLoading);
        this.hintText.gameObject.SetActive(!inIsLoading);
        this.inviteCodeInput.gameObject.SetActive(!inIsLoading);
        this.createButton.gameObject.SetActive(!inIsLoading);
        this.joinButton.gameObject.SetActive(!inIsLoading);
        this.backButton.gameObject.SetActive(true);
        this.backButton.interactable = inIsLoading || !this.isBusy;
        RefreshActiveGamesVisibility();
        RefreshCancelGameVisibility();
        this.statusText.gameObject.SetActive(true);
        if (inIsLoading)
            this.statusText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineResuming);
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
        if (this.pendingCancelGameConfirmation && UIConfirmPanel.IN?.IsShowing == true)
            ShowCancelGameConfirmation();
        else if (this.pendingForfeitConfirmation != null && UIConfirmPanel.IN?.IsShowing == true)
            ShowForfeitConfirmation(this.pendingForfeitConfirmation);
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
        if (this.cancelGameText != null)
            this.cancelGameText.text = LocalizationManager.GetOnlineText(loc.OnlineCancelGame);
        if (this.shareText != null)
            this.shareText.text = LocalizationManager.GetOnlineText(loc.OnlineShareInvite);
        if (this.activeGamesHeaderText != null)
            this.activeGamesHeaderText.text = LocalizationManager.GetOnlineText(loc.OnlineActiveGames);
        if (this.isResumeLoading)
            this.statusText.text = LocalizationManager.GetOnlineText(loc.OnlineResuming);
        else if (!string.IsNullOrEmpty(this.inviteCode))
            this.statusText.text = GetWaitingText();
    }

    private bool IsWaitingForJoiner()
    {
        return this.client != null && this.client.CurrentState?.Status == "waiting";
    }

    private void RefreshCancelGameVisibility()
    {
        if (this.cancelGameButton == null)
            return;

        bool isVisible = !this.isResumeLoading && IsWaitingForJoiner();
        this.cancelGameButtonRoot ??= GetPanelChild(this.transform, this.cancelGameButton);
        if (isVisible)
            this.cancelGameButton.gameObject.SetActive(true);
        this.cancelGameButtonRoot.SetActive(isVisible);
        this.cancelGameButton.interactable = isVisible && !this.isBusy;
        if (this.shareButton != null)
        {
            bool canShare = isVisible && !string.IsNullOrEmpty(this.inviteCode);
            GetPanelChild(this.transform, this.shareButton).SetActive(canShare);
            this.shareButton.interactable = canShare && !this.isBusy;
        }
        if (isVisible)
        {
            this.createButton.gameObject.SetActive(false);
            this.inviteCodeInput.gameObject.SetActive(false);
            this.joinButton.gameObject.SetActive(false);
        }
        else if (!this.isResumeLoading)
        {
            this.createButton.gameObject.SetActive(true);
            this.inviteCodeInput.gameObject.SetActive(true);
            this.joinButton.gameObject.SetActive(true);
        }
    }

    private static GameObject GetPanelChild(Transform inPanel, Component inComponent)
    {
        Transform current = inComponent.transform;
        while (current.parent != null && current.parent != inPanel)
            current = current.parent;

        return current.parent == inPanel ? current.gameObject : inComponent.gameObject;
    }

    private void HandleCancelGameButtonPress()
    {
        ShowCancelGameConfirmation();
    }

    private void ShowCancelGameConfirmation()
    {
        if (!IsWaitingForJoiner() || this.isBusy || UIConfirmPanel.IN == null)
            return;

        this.pendingCancelGameConfirmation = true;
        LocalizationManager localization = LocalizationManager.IN;
        UIConfirmPanel.IN.transform.SetAsLastSibling();
        UIConfirmPanel.IN.Show(
            LocalizationManager.GetOnlineText(localization?.OnlineCancelGameTitle),
            LocalizationManager.GetOnlineText(localization?.OnlineCancelGameMessage),
            () =>
            {
                this.pendingCancelGameConfirmation = false;
                CancelWaitingGame();
            },
            () => this.pendingCancelGameConfirmation = false);
    }

    private async void CancelWaitingGame()
    {
        if (!IsWaitingForJoiner())
            return;

        string matchId = this.client.MatchId;
        int localSeat = this.client.LocalSeat;
        SetBusy(true);
        EForfeitOutcome outcome = await ForfeitMatchAsync(matchId, localSeat, false);
        if (outcome == EForfeitOutcome.Failed)
        {
            string error = LocalizationManager.GetOnlineText(LocalizationManager.IN?.OnlineError);
            this.statusText.text = error;
            if (UIAlertPanel.IN != null)
            {
                UIAlertPanel.IN.transform.SetAsLastSibling();
                UIAlertPanel.IN.Show(error, 3f, EAlertMode.Notification);
            }
            SetBusy(false);
            return;
        }

        ReleaseMatch();
        this.inviteCode = null;
        this.statusText.text = string.Empty;
        string[] notice = outcome == EForfeitOutcome.AlreadyOver
            ? LocalizationManager.IN?.OnlineMatchAlreadyEnded
            : outcome == EForfeitOutcome.Gone
                ? LocalizationManager.IN?.OnlineMatchGone
                : LocalizationManager.IN?.OnlineGameCancelled;
        if (UIAlertPanel.IN != null)
        {
            UIAlertPanel.IN.transform.SetAsLastSibling();
            UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(notice), 3f,
                EAlertMode.Notification);
        }
        await LoadLobbyAsync();
    }

    private void HandleInviteCodePress()
    {
        if (IsWaitingForJoiner())
            CopyInviteCode();
    }

    private void CopyInviteCode()
    {
        if (string.IsNullOrEmpty(this.inviteCode))
            return;
        GUIUtility.systemCopyBuffer = this.inviteCode;
        ShowNotice(LocalizationManager.IN.OnlineCodeCopied);
    }

    private void HandleShareButtonPress()
    {
        if (string.IsNullOrEmpty(this.inviteCode))
            return;
        var loc = LocalizationManager.IN;
        string message = string.Format(LocalizationManager.GetOnlineText(loc.OnlineInviteMessage), this.inviteCode);
#if UNITY_IOS && !UNITY_EDITOR
        ElementoShareText(message);
#elif UNITY_ANDROID && !UNITY_EDITOR
        using var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND");
        intent.Call<AndroidJavaObject>("setType", "text/plain");
        intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.SUBJECT",
            LocalizationManager.GetOnlineText(loc.OnlineInviteSubject));
        intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", message);
        using var intentClass = new AndroidJavaClass("android.content.Intent");
        using var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent,
            LocalizationManager.GetOnlineText(loc.OnlineShareInvite));
        using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
        activity.Call("startActivity", chooser);
#else
        // ponytail: no share sheet on desktop/editor, so the full invitation goes to the clipboard.
        GUIUtility.systemCopyBuffer = message;
        ShowNotice(loc.OnlineInviteCopied);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ElementoShareText(string inText);
#endif

    private static void ShowNotice(string[] inMessage)
    {
        if (UIAlertPanel.IN == null)
            return;
        UIAlertPanel.IN.transform.SetAsLastSibling();
        UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(inMessage), 2f, EAlertMode.Notification);
    }

    private string GetWaitingText()
    {
        if (string.IsNullOrEmpty(this.inviteCode))
            return LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting);
        return LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineInviteCodeLabel)
            + ": <color=#FFDD00>" + this.inviteCode + "</color>\n<size=80%>"
            + LocalizationManager.GetOnlineText(LocalizationManager.IN.OnlineWaiting) + "</size>";
    }

    private async Task LoadActiveGames()
    {
        ClearActiveGameRows();
        if (this.activeGamesContent == null || this.activeGameRowPrefab == null)
            return;

        using var lookup = new SupabaseMatchTransport(OnlineCoroutineHost.IN, this.auth);
        ListMyMatchesResultData result = await lookup.ListMyMatches(new ListMyMatchesRequestData());
        if (result?.Matches != null)
        {
            foreach (MyMatchData match in result.Matches)
            {
                UIActiveGameRow row = Instantiate(this.activeGameRowPrefab, this.activeGamesContent);
                row.Configure(match, HandleRejoin, ShowForfeitConfirmation);
                row.SetInteractable(!this.isBusy && this.isProfileReady && this.client == null);
                this.activeGameRows.Add(row);
            }
        }

        RefreshActiveGamesVisibility();
    }

    private void HandleRejoin(MyMatchData inMatch)
    {
        if (inMatch != null)
            ResumeMatch(inMatch.MatchId);
    }

    private void ShowForfeitConfirmation(MyMatchData inMatch)
    {
        if (inMatch == null || this.isBusy || UIConfirmPanel.IN == null)
            return;

        this.pendingForfeitConfirmation = inMatch;
        LocalizationManager localization = LocalizationManager.IN;
        UIConfirmPanel.IN.transform.SetAsLastSibling();
        UIConfirmPanel.IN.Show(
            LocalizationManager.GetOnlineText(localization?.OnlineForfeitTitle),
            LocalizationManager.GetOnlineText(localization?.OnlineForfeitMessage),
            () =>
            {
                this.pendingForfeitConfirmation = null;
                ForfeitActiveGame(inMatch);
            },
            () => this.pendingForfeitConfirmation = null);
    }

    private async void ForfeitActiveGame(MyMatchData inMatch)
    {
        if (inMatch == null || this.isBusy)
            return;

        SetBusy(true);
        EForfeitOutcome outcome = await ForfeitMatchAsync(inMatch.MatchId, inMatch.YourSeat);
        if (outcome == EForfeitOutcome.Failed)
        {
            string error = LocalizationManager.GetOnlineText(LocalizationManager.IN?.OnlineError);
            this.statusText.text = error;
            if (UIAlertPanel.IN != null)
            {
                UIAlertPanel.IN.transform.SetAsLastSibling();
                UIAlertPanel.IN.Show(error, 3f, EAlertMode.Notification);
            }
        }
        else
        {
            RemoveActiveGameRow(inMatch.MatchId);
            this.statusText.text = string.Empty;
            if (outcome == EForfeitOutcome.AlreadyOver || outcome == EForfeitOutcome.Gone)
            {
                string[] notice = outcome == EForfeitOutcome.AlreadyOver
                    ? LocalizationManager.IN?.OnlineMatchAlreadyEnded
                    : LocalizationManager.IN?.OnlineMatchGone;
                if (UIAlertPanel.IN != null)
                {
                    UIAlertPanel.IN.transform.SetAsLastSibling();
                    UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(notice), 3f,
                        EAlertMode.Notification);
                }
            }
        }
        SetBusy(false);
    }

    private void ClearActiveGameRows()
    {
        foreach (UIActiveGameRow row in this.activeGameRows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }
        this.activeGameRows.Clear();
        RefreshActiveGamesVisibility();
    }

    private void RemoveActiveGameRow(string inMatchId)
    {
        for (int index = this.activeGameRows.Count - 1; index >= 0; --index)
        {
            UIActiveGameRow row = this.activeGameRows[index];
            if (row == null || row.MatchId != inMatchId)
                continue;
            this.activeGameRows.RemoveAt(index);
            Destroy(row.gameObject);
        }
        RefreshActiveGamesVisibility();
    }

    private void RefreshActiveGamesVisibility()
    {
        if (this.activeGamesRoot != null)
            this.activeGamesRoot.SetActive(!this.isResumeLoading && this.activeGameRows.Count > 0);
    }

    private async void ResumeMatch(string inMatchId)
    {
        await ResumeMatchAsync(inMatchId, false);
    }

    private async Task ResumeMatchAsync(string inMatchId, bool inIsAutomatic)
    {
        if (this.isBusy || this.client != null || (!inIsAutomatic && !this.isProfileReady)) return;
        int resumeGeneration = ++this.resumeGeneration;
        SetBusy(true);
        try
        {
            this.isResuming = true;
            if (inIsAutomatic)
            {
                this.auth ??= new SupabaseAuthClient(OnlineCoroutineHost.IN);
                this.profiles ??= new SupabaseProfileClient(this.auth);
            }

            MatchStateUpdateData checkpoint = inIsAutomatic ? LoadCheckpoint(inMatchId) : null;
            bool restoreCheckpoint = checkpoint?.State != null && checkpoint.Status != "waiting";
            if (!restoreCheckpoint)
            {
                checkpoint = null;
                base.Show();
                SetResumeLoading(true);
            }

            CreateClient();
            if (restoreCheckpoint)
            {
                this.isRecoveryPending = true;
                this.client.RestoreState(checkpoint);
                StartMatch(checkpoint, false);
                Hide();
            }

            if (inIsAutomatic)
            {
                await this.profiles.EnsureProfileAsync();
                if (resumeGeneration != this.resumeGeneration)
                    return;
                this.isProfileReady = true;
            }

            SubmitTurnRequestData pending = LoadPendingSubmission(inMatchId);
            if (pending != null)
            {
                this.client.RestorePendingSubmission(pending);
                SubmitTurnResultData result = await this.client.RetryPendingSubmissionAsync();
                if (resumeGeneration != this.resumeGeneration)
                    return;
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

            MatchStateUpdateData match = await this.client.ResumeAsync(inMatchId);
            if (resumeGeneration != this.resumeGeneration)
                return;
            if (!restoreCheckpoint)
            {
                StartMatch(match, false);
                if (match.Status == "waiting")
                {
                    SetResumeLoading(false);
                    this.statusText.text = GetWaitingText();
                }
            }
            else
            {
                this.isRecoveryPending = false;
                GameManager.IN.HandleOnlineRecoveryCompleted();
                if (match.Status != "waiting")
                    Hide();
            }
            if (this.client == null || this.transport == null)
                return;
            this.transport.StartListening(this.client);
        }
        catch (Exception exception)
        {
            if (resumeGeneration != this.resumeGeneration)
                return;
            if (OnlineMatchContext.Client == this.client)
                GameManager.IN.AbortOnlineRecovery();
            else
                ReleaseMatch();
            int failureGeneration = this.resumeGeneration;
            if (inIsAutomatic && exception is SupabaseHttpException http
                && (http.ResponseCode == 403 || http.ResponseCode == 404 || http.ResponseCode == 410))
                ForgetPersistedMatch(inMatchId);
            if (inIsAutomatic)
            {
                await LoadLobbyAsync();
                if (failureGeneration != this.resumeGeneration)
                    return;
            }
            else
            {
                base.Show();
                SetResumeLoading(false);
                SetBusy(false);
            }
            ShowError(exception);
        }
        finally
        {
            if (resumeGeneration == this.resumeGeneration)
                SetBusy(false);
        }
    }

    private void HandlePersistentStateChanged(MatchStateUpdateData inState)
    {
        if (inState == null || string.IsNullOrEmpty(inState.MatchId))
            return;
        if (this.forgottenMatchIds.Contains(inState.MatchId))
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
        if (this.forgottenMatchIds.Contains(matchId))
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

    public static OnlineReplayHistoryData LoadOnlineReplayHistory(string inMatchId)
    {
        return LoadPersisted<OnlineReplayHistoryData>(replayHistoryKeyPrefix + inMatchId);
    }

    public static void SaveOnlineReplayHistory(string inMatchId, OnlineReplayHistoryData inHistory)
    {
        if (string.IsNullOrEmpty(inMatchId) || inHistory?.StartState == null || inHistory.Turn == null)
            return;
        if (IN?.forgottenMatchIds.Contains(inMatchId) == true)
            return;
        PlayerPrefs.SetString(replayHistoryKeyPrefix + inMatchId, NetJson.Serialize(inHistory));
        PlayerPrefs.Save();
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
        return !string.IsNullOrEmpty(GetPersistedActiveMatchId());
    }

    public static string GetPersistedActiveMatchId()
    {
        return PlayerPrefs.GetString(activeMatchIdKey, string.Empty);
    }

    public static void ForgetPersistedMatch(string inMatchId)
    {
        if (string.IsNullOrEmpty(inMatchId))
            return;
        IN?.forgottenMatchIds.Add(inMatchId);
        if (PlayerPrefs.GetString(activeMatchIdKey, string.Empty) == inMatchId)
            PlayerPrefs.DeleteKey(activeMatchIdKey);
        PlayerPrefs.DeleteKey(matchStateKeyPrefix + inMatchId);
        PlayerPrefs.DeleteKey(pendingSubmissionKeyPrefix + inMatchId);
        PlayerPrefs.DeleteKey(replayHistoryKeyPrefix + inMatchId);
        PlayerPrefs.Save();
    }
}
