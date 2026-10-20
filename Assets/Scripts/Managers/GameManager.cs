using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using Elemento.Domain.Net;
using Elemento.Networking.Supabase;
using DomainCardInstanceData = Elemento.Domain.CardInstanceData;
using DomainGameStateData = Elemento.Domain.GameStateData;
using DomainGameRules = Elemento.Domain.GameRules;
using DomainMoveData = Elemento.Domain.MoveData;
using DomainOpponentViewData = Elemento.Domain.OpponentViewData;
using DomainPlayerStateData = Elemento.Domain.PlayerStateData;
using DomainRulesConfigData = Elemento.Domain.RulesConfigData;
using DomainTurnRules = Elemento.Domain.TurnRules;
using DomainZoneStateData = Elemento.Domain.ZoneStateData;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Card;
using static SettingsManager;
using static UIAlertPanel;
using static UIPlayerBoard;

public class GameManager : MonoBehaviour
{
    public static GameManager IN;

    public static bool IsDealingHand;
    public static bool IsDoingActivateSequence;
    public static bool IsDoingReplay;
    public static bool IsApplyingRemoteUpdate;
    public static bool ShouldBlockInput => IsDealingHand || IsDoingActivateSequence || IsDoingReplay || IsApplyingRemoteUpdate
        || (IN != null && (IN.isHandoffPending || IN.isMoveBudgetEndPending || IN.IsOnlineInputBlocked));

    private readonly string ANIM_STATE = "state";

    public enum ETurnPhase
    {
        PlayerDraw,
        PlayerPlaying,
        OpponentDraw,
        OpponentPlaying,
        None
    }

    public enum EAnimState
    {
        Showing,
        Hidden,
        Show,
        Hide
    }

    public enum EWinLoseState
    {
        None,
        Won,
        Lost,
        Tie
    }

    public ETurnPhase TurnPhase;

    [Range(0, 1000)][SerializeField] private int randomSeed;

    [ReadOnly]
    public int MovesRemaining;
    [ReadOnly]
    public int CurrentTurnNumber;

    [Space]
    [Range(0, 1)][SerializeField] private float delayBetweenDeal = 1;
    [Range(0, 1)][SerializeField] private float dealTweenDuration = .5f;
    [Range(0, 1)] public float OpponentMoveTweenDuration = .5f;
    [Range(0, 1)] public float OpponentMovePauseDuration = .4f;
    [Range(0, 1)] public float ActivateDiscardTweenDuration = .2f;
    [Range(0, 1)] public float HandInsertTweenDuration = .2f;
    [Range(0, 1)] public float CardSnapBackTweenSpeed = .2f;
    [SerializeField] private Vector3 dealDestOffset;
    [Space]
    [SerializeField] private Slider gameSpeedSlider;
    [SerializeField] private TextMeshProUGUI gameSpeedText;
    [SerializeField] private TextMeshProUGUI playerTurnText;
    [SerializeField] private TextMeshProUGUI turnAndMoveCountText;

    [Space]
    public UICardCell Deck;
    public UIPlayerBoard PlayerBoard => PlayerBoards[0];
    public UIPlayerBoard[] PlayerBoards;

    [Space]
    [SerializeField] private GameObject beginTurnButton;
    [SerializeField] private GameObject endTurnButton;
    [SerializeField] private GameObject speedUpOpponentTurnButton;
    [SerializeField] private GameObject replayButton;
    [SerializeField] private GameObject undoButtonParent;
    [SerializeField] private Button undoButton;
    public GameObject DiscardZone;
    public Transform DiscardZoneTransform;
    [SerializeField] private GameObject discardToEndTurnZone;
    public Transform DiscardToEndTurnZoneTransform;

    [SerializeField] private GameObject discardPileHighlight;

    [Space]
    [SerializeField] private GameObject movesRemainingDisplay;
    [SerializeField] private UIPoints movesRemainingPoints;

    [SerializeField] private Animator opponentBoardAnim;

    [SerializeField] private Transform mainCanvas;

    [Space]
    public GameObject BombParticlePrefab;
    public GameObject LightningParticlePrefab;
    public GameObject HealParticlePrefab;

    private int currentPlayerIndex = 0;
    private int previousPlayerIndex = -1;
    public UIPlayerBoard CurrentPlayerBoard => this.PlayerBoards[this.currentPlayerIndex];
    public UIPlayerBoard PlayerInputBoard => this.IsOnline ? this.PlayerBoards[((OnlineSession)this.gameSession).LocalSeat] : this.IsPassAndPlay ? this.CurrentPlayerBoard : this.PlayerBoard;
    public EPlayerType CurrentPlayer => (EPlayerType)this.currentPlayerIndex + 1;
    public bool IsPlayersTurn => this.IsOnline
        ? this.onlineClient?.IsLocalTurn == true
        : this.gameSession == null ? this.currentPlayerIndex == 0 : (this.gameSession.IsHumanSeat(this.currentPlayerIndex) && !this.isHandoffPending);
    public bool IsPassAndPlay => this.gameSession != null && this.gameSession.GameMode == Elemento.Domain.EGameMode.PassAndPlay;
    public bool IsOnline => this.gameSession is OnlineSession;
    public bool IsGameInProgress { get; private set; }
    private bool IsOnlineInputBlocked => this.IsOnline && (this.isOnlineMovePending || this.isOnlineLeavePending
        || this.isOnlineReplayActive || this.onlineTurnRecorder == null || this.onlineTurnRecorder.IsComplete
        || UIOnlineLobbyPanel.IN?.IsRecoveryPending == true || !this.IsPlayersTurn
        || !IsHumanPlayingPhase());
    public bool IsCurrentSeatControlledByAI => !this.IsOnline && !this.IsPassAndPlay && ((this.gameSession != null && this.gameSession.ShouldRunAI(this.currentPlayerIndex)) || this.CurrentPlayerBoard.IsAutoPlay);

    public bool CanControlPlayer(EPlayerType inPlayerType)
    {
        if (this.IsOnline)
            return !this.IsOnlineInputBlocked && this.IsPlayersTurn
                && (int)inPlayerType - 1 == ((OnlineSession)this.gameSession).LocalSeat;

        return this.IsPlayersTurn && (!this.IsPassAndPlay || this.CurrentPlayer == inPlayerType);
    }

    public float GameSpeed => this.gameSpeed;
    private float gameSpeed = 1;
    private bool isGameManagerInitialized;
    private bool isGameInitialized;
    private bool isDoingCleanup;
    private bool isHandoffPending;
    private int cachedRandomSeed = -1;
    private IGameSession gameSession;
    private SeatViewBinder seatViewBinder;
    private UIPlayerBoard[] boardSlots;
    private OnlineMatchClient onlineClient;
    private bool isOnlineMovePending;
    private bool isOnlineLeavePending;
    private bool isOnlineReplayActive;
    private bool isSubmittingOnlineWin;
    private bool isMoveBudgetEndPending;
    private bool isOnlineActivationRecorded;
    private bool skipOnlineReplay;
    private bool isOnlineHeartbeatPending;
    private bool isOnlineResumePending;
    private bool isApplicationPaused;
    private bool isOpponentAwayPromptShowing;
    private Coroutine onlineReplayCoroutine;
    private MatchStateUpdateData pendingOnlineUpdate;
    private TurnRecorder onlineTurnRecorder;
    private DomainGameStateData onlineRenderedState;
    private DomainGameStateData onlineTurnStartState;
    private DomainGameStateData onlineReplayStartState;
    private readonly List<TurnPackageData> onlineReplayTurns = new();
    private readonly Queue<TurnPackageData> pendingOnlineTurns = new();
    private readonly Elemento.Domain.TurnUndoHistory<BoardState> undoHistory = new();
    private OpponentPresenceTracker opponentPresenceTracker;

    private BoardState lastCreatedBoardState;

    private void Awake()
    {
        DOTween.Init();

        var singletonManager = GetComponent<SingletonManager>();
        singletonManager.Init();

        Init();
    }

    public void Init()
    {
        if (this.isGameManagerInitialized) return;

        this.isGameManagerInitialized = true;

        SettingsManager.IN.SetDefaults();

        CardsManager.IN.GenerateAllCardDatasFromScriptableObjects();

        SaveManager.IN.LoadGame();

        ConfigureSession();

        UISplashScreen.IN.SetVisible(PlayerData.Data.ShouldShowSplashScreen);
        UIGameBoard.IN.SetVisible(!PlayerData.Data.ShouldShowSplashScreen);

        AudioManager.IN.Init();
    }

    private void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;

        LOC.IN.SelectLanguage(LOC.LI);
        RefreshUndoControls();

#if UNITY_WEBGL
        Screen.fullScreen = true;
#endif
        if (UIOnlineLobbyPanel.HasPersistedMatch())
        {
            SettingsManager.IN.PlayMode = EPlayMode.Multiplayer;
            UISplashScreen.IN.Hide();
            UIGameBoard.IN.Show();
            RestartGame();
            return;
        }
        if (!SettingsManager.IN.ShouldShowSplashScreen)
            RestartGame();
    }

    public void RestartGame()
    {
        ConfigureSession();
        if (SettingsManager.IN.PlayMode == EPlayMode.Multiplayer && !OnlineMatchContext.IsActive)
        {
            StopAllCoroutines();
            CancelInvoke();
            UIOnlineLobbyPanel.IN.Show();
            return;
        }

        if (this.IsOnline)
        {
            SetGameInProgress(true);
            StartOnlineSession();
            return;
        }

        SetGameInProgress(true);

        UISeatHandoffPanel.IN.Hide();
        this.isHandoffPending = false;

        if (this.cachedRandomSeed == -1)
            this.cachedRandomSeed = this.randomSeed;

        if (this.cachedRandomSeed == 0 || !Application.isEditor)
        {
            this.randomSeed = UnityEngine.Random.Range(0, 1000);
        }

        UnityEngine.Random.InitState(this.randomSeed);

        this.lastCreatedBoardState = null;
        this.undoHistory.Reset();
        BoardStateManager.IN.ClearBoardStates();
        BoardStateManager.IN.CurrentReplayBoardState = null;
        Move.ShouldRecordMoves = true;
        Move.MoveId = 0;
        GameManager.IsDoingReplay = false;

        this.currentPlayerIndex = 0;
        this.previousPlayerIndex = -1;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
        this.CurrentTurnNumber = 0;

        ResetMovesRemaining();

        RefreshTurnAndMoveCountDisplay();
        SetReplayButtonVisibility(false);

        if (UITutorialPanel.IsTutorialMode)
            SetBoardZoomAnimState(EAnimState.Showing);
        else
            SetBoardZoomAnimState(EAnimState.Hidden);

        this.PlayerBoard.Hand.HidePlayerHandText();
        this.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);

        SetHealthOnAllElementZones();

        if (this.isGameInitialized)
            CleanUpGameState();

        CardsManager.IN.ResetCardInstanceIds();

        StartCoroutine(RestartGameCo());
    }

    private IEnumerator RestartGameCo()
    {
        yield return new WaitForSeconds(.1f);

        if (!this.isGameInitialized)
        {
            HandleGameSpeedSliderChange(1);
            yield return new WaitForSeconds(.7f);
        }

        yield return null;//to let animation set scale of deck before spawning card into it

        this.isGameInitialized = true;

        if (UITutorialPanel.IsTutorialMode)
        {
            CardsManager.IN.AddTutorialCardsToGameDeck();
        }
        else
        {
            CardsManager.IN.RebuildActiveDeck();
            CardsManager.IN.GenerateShuffledGameDeck();
            CardsManager.IN.AddBatchOfCardsToGameDeck();

            SetTurnPhase(ETurnPhase.PlayerDraw);
        }
    }

    private void SetGameInProgress(bool inIsGameInProgress)
    {
        this.IsGameInProgress = inIsGameInProgress;

        if (!inIsGameInProgress)
            UIWaitingForOpponentPanel.IN?.Hide();

        if (UISettingsPanel.IN != null)
            UISettingsPanel.IN.RefreshPlayModeAvailability(inIsGameInProgress);
    }

    public void CleanUpGameState()
    {
        CleanUpGameState(true);
    }

    private void CleanUpGameState(bool inShouldRecycleCards)
    {
        this.isDoingCleanup = true;
        try
        {
            GameManager.IsDealingHand = false;
            GameManager.IsDoingActivateSequence = false;

            if (!BoardStateManager.IsDoingReplay && !this.isOnlineReplayActive)
                StopAllCoroutines();

            CardsManager.IN.DestroyAllCards(inShouldRecycleCards);

            foreach (var board in this.PlayerBoards)
            {
                board.Reset();
            }

            foreach (var board in this.PlayerBoards)
            {
                foreach (var zone in board.ElementZones)
                {
                    zone.CardCell.Cards.Clear();
                    zone.SetActiveState(false, false);
                    zone.SetFrozenHealth(0, false);
                }

                foreach (var zone in board.ElementZones)
                {
                    zone.CalculatePoints(false);
                    zone.SetActiveState(false, false);
                }

                foreach (var cell in board.Hand.CardCells)
                {
                    cell.Cards.Clear();
                    cell.gameObject.SetActive(false);
                }
            }

            this.Deck.RefreshCellsList();
        }
        finally
        {
            this.isDoingCleanup = false;
        }
    }

    public void SetHealthOnAllElementZones()
    {
        foreach (var board in this.PlayerBoards)
        {
            foreach (var zone in board.ElementZones)
            {
                zone.SetZoneHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);
                zone.SetMaxHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);
            }
        }
    }

    public void HandleDealHandButtonPress()
    {
        if (GameManager.ShouldBlockInput) return;
        DealCards();
    }

    private void DealCards()
    {
        int numCardsInHand = this.CurrentPlayerBoard.Hand.NumCardsInHand;

        if (numCardsInHand >= SettingsManager.IN.MaxCardsInHand)
        {
            this.endTurnButton.SetActive(SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn);

            UIAlertPanel.IN.Show(LOC.IN.Reject_HandIsFull[LOC.LI]);//"Your Hand is Full!"
            return;
        }

        if (!this.IsCurrentSeatControlledByAI)
            MeltFrozenZones();

        RefreshTurnAndMoveCountDisplay();

        if (this.IsCurrentSeatControlledByAI)
            StartCoroutine(OpponentsTurnCo(true));
        else
            StartCoroutine(DealCardsCo(SettingsManager.IN.NumCardsToDeal, this.CurrentPlayerBoard.Hand));
    }

    //called by End Turn Button
    public void EndTurn()
    {
        bool isOnlineRetry = this.IsOnline && this.onlineTurnRecorder?.IsComplete == true
            && !this.isOnlineMovePending;
        if (!isOnlineRetry && (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput)) return;
        if (UITutorialPanel.IsTutorialMode) return;
        if (!IsHumanPlayingPhase()) return;//ignore stale/double end-turn requests

        LockUndoTurn();

        if (this.IsOnline)
        {
            if (this.onlineTurnRecorder == null)
                return;
            try
            {
                TurnPackageData turn = this.onlineTurnRecorder.EndTurn();
                this.onlineRenderedState = turn.FinalState.Clone();
                SubmitOnlineTurn(turn);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Online end turn was not accepted locally: " + exception.Message);
                UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineMoveIllegal));
                RefreshOnlineControls();
            }
            return;
        }

        SelectNextPlayer();
    }

    public void HandleSpeedUpOpponentTurnButtonPress()
    {
        if (this.isOnlineReplayActive)
        {
            this.skipOnlineReplay = true;
            Time.timeScale = 100;
            return;
        }
        if (UITutorialPanel.IsTutorialMode || UIDraggable.ShouldBlockInput) return;

        //TODO: this is breaking when in Opponent Draw mode
        Time.timeScale = 100;
    }

    private IEnumerator OpponentsTurnCo(bool inIsPlayerAutoPlay = false, bool inShouldDrawCards = true)
    {
        if (inShouldDrawCards)
        {
            MeltFrozenZones();

            if (SettingsManager.IN.TutorialSafeMovesPerTurn > 0)
                SetMovesRemaining(this.MovesRemaining);

            RefreshTurnAndMoveCountDisplay();

            if (!inIsPlayerAutoPlay)
                SetBoardZoomAnimState(EAnimState.Show);

            yield return new WaitForSeconds(.25f);

            StartCoroutine(DealCardsCo(SettingsManager.IN.NumCardsToDeal, this.CurrentPlayerBoard.Hand));

            while (GameManager.IsDealingHand)
            {
                yield return new WaitForSeconds(.1f);
            }

            yield return new WaitForSeconds(1f);

            if (inIsPlayerAutoPlay)
                SetTurnPhase(ETurnPhase.PlayerPlaying);
            else
                SetTurnPhase(ETurnPhase.OpponentPlaying);
        }

        this.CurrentPlayerBoard.StartTurn();

        while (this.CurrentPlayerBoard.IsTakingTurn || UIConfirmPanel.IN.IsShowing)
        {
            yield return new WaitForSeconds(.1f);
        }

        if (!inIsPlayerAutoPlay)
            SetBoardZoomAnimState(EAnimState.Hide);

        //yield return new WaitForSeconds(.25f);

        SelectNextPlayer();
    }

    private IEnumerator DealCardsCo(int inCount, UIHand inHand, bool inShouldAdvanceTurn = true)
    {
        GameManager.IsDealingHand = true;

        this.Deck.RefreshCellsList();

        yield return new WaitForSeconds(.5f);

        inCount = Mathf.Min(inCount, SettingsManager.IN.MaxCardsInHand - inHand.NumCardsInHand);

        if (inCount <= 0)
            inCount = 0;

        for (int i = 0; i < inCount; i++)
        {
            int numCardsInHand = inHand.NumCardsInHand;

            //if (numCardsInHand >= SettingsManager.IN.MaxCardsInHand)
            //{
            //    bool isOwnHand = this.CurrentPlayerBoard.PlayerType == EPlayerType.PlayerA;
            //    var rejectReason = isOwnHand ? "Your Hand is Full!" : "Player's Hand is Full!";

            //    UIAlertPanel.IN.Show(rejectReason);

            //    break;
            //}

            if (inShouldAdvanceTurn && this.Deck.Cards.Count <= 2)
                CardsManager.IN.AddBatchOfCardsToGameDeck();

            if (this.Deck.TopCard != null)
            {
                var playerHandPos = GetCardAnimDestination(inHand, numCardsInHand);

                this.Deck.TopCard.Draggable.AnimateTo(playerHandPos, this.dealTweenDuration);

                var pitch = 1 + (i * .1f);
                AudioManager.IN.PlayCardSwooshSound(0, pitch);

                yield return new WaitForSeconds(this.delayBetweenDeal);
            }
            else
                Debug.Log($"<color=red>Deck.TopCard is null!   {i}/{inCount}</color>");
        }

        if (inShouldAdvanceTurn)
        {
            ResetMovesRemaining(true);
            SaveBoardState(null, null, false, null, true);
        }

        GameManager.IsDealingHand = false;

        if (inShouldAdvanceTurn)
            IncrementTurnPhase();
    }

    private Vector3 GetCardAnimDestination(UIHand inHand, int inNumCardsInHand)
    {
        var playerBoard = inHand.GetComponentInParent<UIPlayerBoard>();
        var boardScale = playerBoard.transform.parent.localScale.x;

        var handRT = inHand.transform.GetComponent<RectTransform>();
        var halfHandWidth = handRT.rect.width * .5f * boardScale + 30;
        var rawDealDest = this.dealDestOffset.x * inNumCardsInHand;
        var isOutsideOfHand = rawDealDest > halfHandWidth;

        var offset = isOutsideOfHand ? Vector3.zero : this.dealDestOffset * inNumCardsInHand * this.mainCanvas.localScale.x * boardScale;
        var playerHandPos = inHand.transform.position + offset;

        return playerHandPos;
    }

    public IEnumerator DealPowerCardCo(UIElementZone inElementZone = null)
    {
        GameManager.IsDealingHand = true;
        try
        {
            ECardType cardType = GetPowerCardTypeForDeal();
            DomainCardInstanceData onlinePowerCard = null;
            if (this.IsOnline && !this.isOnlineReplayActive)
            {
                if (cardType == ECardType.None
                    && this.onlineTurnRecorder?.State.Rules.AllowSelectPowerCardType == true)
                {
                    cardType = GetRandomPowerCardType();
                }
                var activation = Move.CreateMove(this.Deck, this.Deck, false, inElementZone, false,
                    cardType, (int)this.CurrentPlayer);
                if (!TryRecordOnlineMove(activation, out TurnActionData action, out string reason))
                {
                    Debug.LogWarning("Online activation was not recorded: " + reason);
                    RestoreOnlineActionCheckpoint();
                    yield break;
                }

                onlinePowerCard = action.PowerCard;
                this.isOnlineActivationRecorded = true;
            }

            var powerCard = onlinePowerCard == null
                ? CardsManager.IN.SpawnPowerCardOfType(cardType)
                : SpawnOnlineCard(onlinePowerCard, this.Deck.transform, true);

            if (powerCard == null)
                yield break;

            var powerCardType = ECardType.Heal;
            if (powerCard.Data.WildFreeze > 0)
                powerCardType = ECardType.Freeze;
            else if (powerCard.Data.WildLightning > 0)
                powerCardType = ECardType.Lightning;

            this.Deck.RefreshCellsList();

            yield return new WaitForSeconds(.5f);

            int numCardsInHand = this.CurrentPlayerBoard.Hand.NumCardsInHand;

            if (powerCard != null)
            {
                //TODO: add trail renderer
                var playerHandPos = GetCardAnimDestination(this.CurrentPlayerBoard.Hand, numCardsInHand);
                powerCard.Draggable.AnimateTo(playerHandPos, this.dealTweenDuration);

                AudioManager.IN.PlayCardSwooshSound(0, 1);

                yield return new WaitForSeconds(this.dealTweenDuration + .3f);

                var parentCell = powerCard.transform.GetComponentInParent<UICardCell>();

                if (inElementZone != null)
                    SaveBoardState(this.Deck, parentCell, false, inElementZone, false,
                        powerCardType, powerCard.Id);

                AudioManager.IN.PlayClip(AudioManager.IN.UseActivationPowerClip, .5f);

                //TODO: play particle
            }
            else
                Debug.Log($"<color=red>PowerCard is null!</color>");

            UITutorialPanel.OnPowerCardDealt?.Invoke(powerCardType);
        }
        finally
        {
            this.isOnlineActivationRecorded = false;
            GameManager.IsDealingHand = false;
        }
    }

    private static ECardType GetPowerCardTypeForDeal()
    {
        ECardType cardType = ECardType.None;
        if (GameManager.IsDoingReplay && BoardStateManager.IN?.CurrentReplayBoardState?.Move != null)
            cardType = BoardStateManager.IN.CurrentReplayBoardState.Move.CardType;
        if (CardsManager.IN.PowerCardOverride != ECardType.None)
            cardType = CardsManager.IN.PowerCardOverride;
        return cardType;
    }

    private static ECardType GetRandomPowerCardType()
    {
        return UnityEngine.Random.Range(0, 3) switch
        {
            0 => ECardType.Heal,
            1 => ECardType.Freeze,
            _ => ECardType.Lightning
        };
    }

    public IEnumerator DealTutorialCardsCo()
    {
        this.currentPlayerIndex = 0;

        GameManager.IsDealingHand = true;

        //Debug.Log($"<color=yellow>DealTutorialCardsCo</color>  CardsManager.IN.TutorialCardDatas.Count = {CardsManager.IN.TutorialCardDatas.Count}");

        this.Deck.RefreshCellsList();

        yield return new WaitForSeconds(.1f);

        for (int i = 0; i < 5; i++)
        {
            int numCardsInHand = this.PlayerBoard.Hand.NumCardsInHand;

            if (this.Deck.TopCard != null)
            {
                var playerHandPos = GetCardAnimDestination(this.CurrentPlayerBoard.Hand, numCardsInHand);

                this.Deck.TopCard.Draggable.AnimateTo(playerHandPos, this.dealTweenDuration);

                var pitch = 1 + (i * .1f);
                AudioManager.IN.PlayCardSwooshSound(0, pitch);

                yield return new WaitForSeconds(this.delayBetweenDeal);
            }
        }

        ResetMovesRemaining(true);

        GameManager.IsDealingHand = false;
    }

    public IEnumerator TutorialOpponentsTurnCo()
    {
        this.currentPlayerIndex = 1;

        this.CurrentPlayerBoard.CurrentMoveNumber = 0;

        ++this.CurrentTurnNumber;

        RefreshTurnAndMoveCountDisplay();

        yield return new WaitForSeconds(.25f);

        GameManager.IsDealingHand = true;

        this.Deck.RefreshCellsList();

        yield return new WaitForSeconds(.1f);

        for (int i = 0; i < 5; i++)
        {
            int numCardsInHand = this.CurrentPlayerBoard.Hand.NumCardsInHand;

            if (this.Deck.TopCard != null)
            {
                var playerHandPos = GetCardAnimDestination(this.CurrentPlayerBoard.Hand, numCardsInHand);
                this.Deck.TopCard.Draggable.AnimateTo(playerHandPos, this.dealTweenDuration);

                var pitch = 1 + (i * .1f);
                AudioManager.IN.PlayCardSwooshSound(0, pitch);

                yield return new WaitForSeconds(this.delayBetweenDeal);
            }
        }

        ResetMovesRemaining(true);

        GameManager.IsDealingHand = false;

        MeltFrozenZones();

        this.CurrentPlayerBoard.StartTurn();

        while (this.CurrentPlayerBoard.IsTakingTurn || UIConfirmPanel.IN.IsShowing)
        {
            yield return new WaitForSeconds(.1f);
        }

        StartCoroutine(DealTutorialCardsCo());

        while (this.PlayerBoard.IsTakingTurn)
        {
            yield return new WaitForSeconds(.1f);
        }

        UITutorialPanel.IN.IncrementTutorialState();
    }

    private void IncrementTurnPhase()
    {
        if (this.TurnPhase < ETurnPhase.OpponentPlaying)
            ++this.TurnPhase;
        else
            this.TurnPhase = ETurnPhase.PlayerDraw;

        SetTurnPhase(this.TurnPhase);
    }

    private bool IsHumanPlayingPhase()
    {
        if (this.IsCurrentSeatControlledByAI) return false;

        return this.TurnPhase == ETurnPhase.PlayerPlaying
            || ((this.IsPassAndPlay || this.IsOnline) && this.TurnPhase == ETurnPhase.OpponentPlaying);
    }

    private void SelectNextPlayer()
    {
        CancelInvoke(nameof(EndTurn));
        this.previousPlayerIndex = this.currentPlayerIndex;
        this.currentPlayerIndex = DomainTurnRules.GetNextPlayerIndex(this.currentPlayerIndex, this.PlayerBoards.Length);

        var nextTurnPhase = this.currentPlayerIndex == 0 ? ETurnPhase.PlayerDraw : ETurnPhase.OpponentDraw;

        SetTurnPhase(nextTurnPhase);
    }

    public void SetReplayButtonVisibility(bool inIsVisible)
    {
        this.replayButton.SetActive(inIsVisible);
    }

    private void SetTurnPhase(ETurnPhase inTurnPhase)
    {
        this.TurnPhase = inTurnPhase;
        GameManager.IsDealingHand = false;

        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(false);
        this.discardToEndTurnZone.SetActive(false);
        SetReplayButtonVisibility(false);

        SetDiscardHighlightsVisible(false);

        this.speedUpOpponentTurnButton.SetActive(false);

        switch (inTurnPhase)
        {
            case ETurnPhase.PlayerDraw:

                BeginUndoTurn(Elemento.Domain.ETurnUndoCheckpoint.Draw);

                SetGamePaused(false);
                ResetMovesRemaining();
                IncrementTurn();

                if (!TryShowPassAndPlayHandoff())
                    BeginHumanDrawPhase();

                break;

            case ETurnPhase.PlayerPlaying:
                if (this.IsCurrentSeatControlledByAI)
                {
                    StopAllCoroutines();
                    StartCoroutine(OpponentsTurnCo(true, false));
                }

                if (this.CurrentTurnNumber > 1)
                    SetReplayButtonVisibility(true);

                SetMovesRemaining(SettingsManager.IN.TutorialSafeMovesPerTurn);
                this.endTurnButton.SetActive(SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn);
                this.discardToEndTurnZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);
                break;

            case ETurnPhase.OpponentDraw:

                if (this.IsPassAndPlay)
                    BeginUndoTurn(Elemento.Domain.ETurnUndoCheckpoint.Draw);
                else
                    LockUndoTurn();

                ResetMovesRemaining();
                this.PlayerBoard.Hand.RefreshPlayerHandText();
                this.CurrentPlayerBoard.CurrentMoveNumber = 0;

                if (this.gameSession.ShouldRunAI(this.currentPlayerIndex))
                {
                    this.speedUpOpponentTurnButton.SetActive(true);
                    StartCoroutine(OpponentsTurnCo());
                }
                else if (!TryShowPassAndPlayHandoff())
                {
                    BeginHumanDrawPhase();
                }
                break;

            case ETurnPhase.OpponentPlaying:

                if (this.IsPassAndPlay)
                {
                    SetMovesRemaining(SettingsManager.IN.TutorialSafeMovesPerTurn);
                    this.endTurnButton.SetActive(SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn);
                    this.discardToEndTurnZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);
                    this.CurrentPlayerBoard.Hand.RefreshPlayerHandText();
                }
                else
                {
                    this.speedUpOpponentTurnButton.SetActive(true);
                }
                break;
        }

        RefreshUndoControls();
    }

    private void ConfigureSession()
    {
        UIWaitingForOpponentPanel.IN?.Hide();

        if (this.onlineClient != null)
        {
            this.onlineClient.StateChanged -= HandleOnlineStateChanged;
            this.onlineClient.OpponentTurnReceived -= HandleOpponentTurnReceived;
            this.onlineClient.MissedTurnReceived -= HandleMissedTurnReceived;
            this.onlineClient.RequestFailed -= HandleOnlineRequestFailed;
            this.onlineClient.PresenceChanged -= HandleOnlinePresenceChanged;
        }

        this.onlineClient = null;
        if (SettingsManager.IN.PlayMode != EPlayMode.Multiplayer && OnlineMatchContext.IsActive)
            UIOnlineLobbyPanel.IN.ReleaseMatch();

        this.gameSession = SessionFactory.Create(SettingsManager.IN.PlayMode);
        this.onlineClient = (this.gameSession as OnlineSession)?.Client;
        if (this.onlineClient != null)
        {
            this.onlineClient.StateChanged += HandleOnlineStateChanged;
            this.onlineClient.OpponentTurnReceived += HandleOpponentTurnReceived;
            this.onlineClient.MissedTurnReceived += HandleMissedTurnReceived;
            this.onlineClient.RequestFailed += HandleOnlineRequestFailed;
            this.onlineClient.PresenceChanged += HandleOnlinePresenceChanged;
            this.opponentPresenceTracker = new OpponentPresenceTracker(
                () => Time.realtimeSinceStartup);
            this.opponentPresenceTracker.Observe(
                this.onlineClient.CurrentState?.OpponentLastSeenSecondsAgo ?? -1);
        }

        if (this.boardSlots == null || this.boardSlots.Length != this.PlayerBoards.Length)
        {
            this.boardSlots = (UIPlayerBoard[])this.PlayerBoards.Clone();
            this.seatViewBinder = new SeatViewBinder(this.PlayerBoards.Length);
        }

        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
    }

    private void ReleaseOnlineSession()
    {
        CancelOnlineReplay();
        if (this.onlineClient != null)
        {
            this.onlineClient.StateChanged -= HandleOnlineStateChanged;
            this.onlineClient.OpponentTurnReceived -= HandleOpponentTurnReceived;
            this.onlineClient.MissedTurnReceived -= HandleMissedTurnReceived;
            this.onlineClient.RequestFailed -= HandleOnlineRequestFailed;
            this.onlineClient.PresenceChanged -= HandleOnlinePresenceChanged;
        }

        this.onlineClient = null;
        this.onlineTurnRecorder = null;
        this.onlineRenderedState = null;
        this.onlineTurnStartState = null;
        this.pendingOnlineUpdate = null;
        this.pendingOnlineTurns.Clear();
        this.isOnlineMovePending = false;
        this.isOnlineLeavePending = false;
        this.isOnlineReplayActive = false;
        this.isMoveBudgetEndPending = false;
        this.isOnlineHeartbeatPending = false;
        this.isOnlineResumePending = false;
        this.opponentPresenceTracker?.Reset();
        this.opponentPresenceTracker = null;
        if (this.isOpponentAwayPromptShowing && UIConfirmPanel.IN != null)
            UIConfirmPanel.IN.Hide();
        this.isOpponentAwayPromptShowing = false;
        ClearOnlineReplayHistory();
        UIOnlineLobbyPanel.IN.ReleaseMatch();
        this.gameSession = null;
        UIWaitingForOpponentPanel.IN?.Hide();
    }

    public void AbortOnlineRecovery()
    {
        if (!this.IsOnline)
            return;
        SetGameInProgress(false);
        ReleaseOnlineSession();
    }

    public void HandleOnlineRecoveryCompleted()
    {
        if (!this.IsOnline)
            return;
        RefreshTurnAndMoveCountDisplay();
        RefreshOnlineControls();
        _ = RestoreOnlineReplayHistoryAsync();
    }

    private void StartOnlineSession()
    {
        StopAllCoroutines();
        CancelInvoke();
        CleanUpGameState();
        UISeatHandoffPanel.IN.Hide();
        UIWaitingForOpponentPanel.IN?.Hide();
        this.isHandoffPending = false;
        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(false);
        this.discardToEndTurnZone.SetActive(false);
        this.speedUpOpponentTurnButton.SetActive(false);
        SetReplayButtonVisibility(false);
        this.TurnPhase = ETurnPhase.None;
        this.isOnlineMovePending = false;
        this.isOnlineLeavePending = false;
        this.isOnlineReplayActive = false;
        this.isMoveBudgetEndPending = false;
        this.skipOnlineReplay = false;
        this.pendingOnlineUpdate = null;
        this.pendingOnlineTurns.Clear();
        ClearOnlineReplayHistory();
        this.opponentPresenceTracker?.Observe(
            this.onlineClient?.CurrentState?.OpponentLastSeenSecondsAgo ?? -1);

        if (this.onlineClient?.CurrentState == null)
            return;

        if (this.onlineClient.CurrentState.Status == "abandoned")
        {
            HandleOnlineOpponentLeft();
            return;
        }

        ApplyOnlineState(this.onlineClient.CurrentState);
        _ = RestoreOnlineReplayHistoryAsync();
    }

    private void HandleOnlineStateChanged(MatchStateUpdateData inUpdate)
    {
        if (!this.IsOnline || this.onlineClient == null || inUpdate?.State == null
            || inUpdate.MatchId != this.onlineClient.MatchId || this.isOnlineLeavePending)
            return;

        HandleOnlinePresenceChanged(inUpdate.OpponentLastSeenSecondsAgo);

        if (inUpdate.Rules != null)
            SettingsManager.IN.ApplyMatchRules(inUpdate.Rules);

        if (inUpdate.Status == "abandoned")
        {
            HandleOnlineOpponentLeft();
            return;
        }

        if (this.isOnlineReplayActive || this.onlineReplayCoroutine != null
            || this.pendingOnlineTurns.Count > 0)
        {
            this.pendingOnlineUpdate = inUpdate;
            return;
        }

        bool hasUnfinishedLocalTurn = this.onlineTurnRecorder != null
            && !this.onlineTurnRecorder.IsComplete
            && this.onlineTurnRecorder.State.CurrentPlayerId == this.onlineClient.LocalSeat;
        if (hasUnfinishedLocalTurn && inUpdate.Status == "active")
            return;

        ApplyOnlineState(inUpdate);
    }

    private void HandleOpponentTurnReceived(TurnPackageData inTurn)
    {
        if (!this.IsOnline || this.onlineClient == null || inTurn == null
            || inTurn.Seat == this.onlineClient.LocalSeat || this.isOnlineLeavePending)
            return;

        QueueOnlineTurn(inTurn);
    }

    private void HandleMissedTurnReceived(TurnPackageData inTurn)
    {
        if (!this.IsOnline || this.onlineClient == null || inTurn == null
            || this.isOnlineLeavePending)
            return;

        QueueOnlineTurn(inTurn);
    }

    private void QueueOnlineTurn(TurnPackageData inTurn)
    {

        this.pendingOnlineTurns.Enqueue(inTurn);
        if (this.onlineReplayCoroutine == null)
        {
            this.onlineReplayStartState = this.onlineRenderedState?.Clone();
            this.onlineReplayTurns.Clear();
            if (inTurn.Seat != this.onlineClient.LocalSeat)
                PersistOnlineReplayHistory(this.onlineReplayStartState, inTurn);
            this.onlineReplayCoroutine = StartCoroutine(ReplayPendingOnlineTurnsCo());
        }
    }

    private void HandleOnlineRequestFailed(Exception inException)
    {
        if (inException == null)
            return;
        Debug.LogWarning("Online request failed: " + inException.Message);
        if (inException.Message.Contains("426") || inException.Message.Contains("update_required"))
            UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineUpdateRequired));
    }

    private void HandleOnlinePresenceChanged(int inSecondsAgo)
    {
        this.opponentPresenceTracker?.Observe(inSecondsAgo);
        bool isAway = this.opponentPresenceTracker?.IsAway ?? false;
        UIWaitingForOpponentPanel.IN?.SetOpponentAway(isAway);
        if (!isAway && this.isOpponentAwayPromptShowing)
        {
            this.isOpponentAwayPromptShowing = false;
            UIConfirmPanel.IN?.Hide();
        }
    }

    private void ApplyOnlineState(MatchStateUpdateData inUpdate)
    {
        if (inUpdate?.State == null)
            return;
        if (inUpdate.Rules != null)
            SettingsManager.IN.ApplyMatchRules(inUpdate.Rules);
        RenderOnlineState(inUpdate.State, true);
    }

    private void RenderOnlineState(DomainGameStateData inState, bool inInitializeLocalTurn)
    {
        if (!this.IsOnline || this.onlineClient == null || inState == null
            || inState.CurrentPlayerId < 0 || inState.CurrentPlayerId >= this.PlayerBoards.Length)
            return;

        bool wasApplyingUpdate = GameManager.IsApplyingRemoteUpdate;
        GameManager.IsApplyingRemoteUpdate = true;
        try
        {
            UIDraggable.CancelActiveDrag(false);
            this.currentPlayerIndex = inState.CurrentPlayerId;
            this.CurrentTurnNumber = inState.TurnNumber;
            this.MovesRemaining = inState.MovesRemaining;
            this.TurnPhase = (ETurnPhase)inState.TurnPhase;
            ApplyViewingSeat(this.onlineClient.LocalSeat);
            RenderOnlineView(FairView.BuildSeatView(inState, this.onlineClient.LocalSeat).View);
            this.onlineRenderedState = inState.Clone();

            if (inInitializeLocalTurn && this.onlineClient.CurrentState?.Status == "active"
                && inState.CurrentPlayerId == this.onlineClient.LocalSeat)
            {
                this.onlineTurnStartState = inState.Clone();
                this.onlineTurnRecorder = new TurnRecorder(inState.Clone(),
                    this.onlineClient.LastAppliedRevision);
                BeginUndoTurn(Elemento.Domain.ETurnUndoCheckpoint.Draw);
                this.lastCreatedBoardState = CreateBoardState(false);
            }
            else if (inInitializeLocalTurn)
            {
                this.onlineTurnRecorder = null;
                this.onlineTurnStartState = null;
                LockUndoTurn();
            }

            RefreshPassAndPlayHandVisibility();
            RefreshTurnAndMoveCountDisplay();
            RefreshOnlineControls();
            if (inInitializeLocalTurn)
                TryShowOnlineGameOver();
        }
        finally
        {
            UIDraggable.ResetInputState();
            GameManager.IsApplyingRemoteUpdate = wasApplyingUpdate;
        }
    }

    private IEnumerator ReplayPendingOnlineTurnsCo()
    {
        OnlineReplayHistoryData latestOpponentHistory = null;
        this.isOnlineReplayActive = true;
        this.skipOnlineReplay = false;
        UIWaitingForOpponentPanel.IN?.Hide();
        GameManager.IsDoingReplay = true;
        GameManager.IsApplyingRemoteUpdate = true;
        Move.ShouldRecordMoves = false;
        SetReplayButtonVisibility(false);
        SetOnlineReplayButton(true);

        try
        {
            while (this.pendingOnlineTurns.Count > 0)
            {
                TurnPackageData turn = this.pendingOnlineTurns.Dequeue();
                DomainGameStateData startState = this.onlineRenderedState?.Clone();
                if (startState == null)
                {
                    LogOnlineReplayMismatch(turn, "missing_start_state", null);
                    RenderOnlineState(turn.FinalState, false);
                    continue;
                }

                var replay = new TurnReplayer().Replay(startState, turn);
                if (!replay.IsMatch)
                    LogOnlineReplayMismatch(turn, replay.Reason, replay.ActualHash);

                this.onlineReplayTurns.Add(turn);
                if (turn.Seat != this.onlineClient.LocalSeat)
                {
                    latestOpponentHistory = new OnlineReplayHistoryData
                    {
                        StartState = startState.Clone(), Turn = turn
                    };
                }
                yield return AnimateOnlineTurnCo(turn, startState);
                DomainGameStateData resolvedState = replay.IsMatch ? replay.State : turn.FinalState;
                RenderOnlineState(resolvedState, false);
            }
        }
        finally
        {
            this.isOnlineReplayActive = false;
            this.skipOnlineReplay = false;
            this.onlineReplayCoroutine = null;
            Time.timeScale = this.gameSpeed;
            ReleaseReplayInputLocks();
            SetOnlineReplayButton(false);
        }

        if (latestOpponentHistory != null)
            PersistOnlineReplayHistory(latestOpponentHistory.StartState, latestOpponentHistory.Turn);

        MatchStateUpdateData update = this.pendingOnlineUpdate ?? this.onlineClient?.CurrentState;
        this.pendingOnlineUpdate = null;
        if (update?.State != null)
            ApplyOnlineState(update);
        else
            RefreshOnlineControls();
    }

    private IEnumerator AnimateOnlineTurnCo(TurnPackageData inTurn, DomainGameStateData inStartState)
    {
        if (inTurn?.Actions == null)
            yield break;

        Stack<BoardState> checkpoints = new();
        foreach (var action in inTurn.Actions)
        {
            if (this.skipOnlineReplay)
                yield break;

            if (action == null)
                continue;

            if (action.Kind == "Undo")
            {
                if (checkpoints.Count == 0)
                {
                    Debug.LogWarning("Online replay Undo had no checkpoint.");
                    yield break;
                }
                SetGameToBoardState(checkpoints.Pop());
                yield return new WaitForSeconds(.1f);
                continue;
            }

            DomainMoveData move = action.Move;
            if (action.Kind == "EndTurn" && move == null)
            {
                move = new DomainMoveData
                {
                    Type = Elemento.Domain.EMoveType.EndTurn,
                    PlayerId = inTurn.Seat
                };
            }

            if (move == null)
                continue;

            checkpoints.Push(CreateBoardState(false));
            if (move.Type == Elemento.Domain.EMoveType.ActivateZone)
            {
                yield return AnimateOnlineActivationCo(move);
                ApplyOnlineActivationPresentation(move, action);
            }
            else if (move.Type != Elemento.Domain.EMoveType.EndTurn)
            {
                yield return AnimateOnlineCardMoveCo(move, inTurn, inStartState);
                ApplyOnlineCardMovePresentation(move);
            }

            AddOnlineReplayDraws(action, inTurn.FinalState);

            if (move.Type == Elemento.Domain.EMoveType.EndTurn)
            {
                this.currentPlayerIndex = inTurn.NextSeat;
                this.CurrentTurnNumber = inTurn.FinalState?.TurnNumber
                    ?? this.CurrentTurnNumber;
                RefreshTurnAndMoveCountDisplay();
                yield return new WaitForSeconds(.2f);
                continue;
            }

            if (this.OpponentMovePauseDuration > 0)
                yield return new WaitForSeconds(this.OpponentMovePauseDuration);
        }
    }

    private void ApplyOnlineActivationPresentation(DomainMoveData inMove,
        TurnActionData inAction)
    {
        UIElementZone zone = FindOnlineZone(inMove.PlayerId, inMove.SourceZoneId);
        if (zone != null)
        {
            foreach (var card in new List<Card>(zone.CardCell.Cards))
                card?.DestroySelf();
            zone.CardCell.Cards.Clear();
            zone.SetActiveState(false, false);
        }

        if (inAction.PowerCard != null)
            AddOnlineReplayHandCard(inMove.PlayerId, inAction.PowerCard);
    }

    private void ApplyOnlineCardMovePresentation(DomainMoveData inMove)
    {
        UICardCell source = FindOnlineCardCell(inMove.PlayerId, inMove.CardId);
        Card card = FindOnlineCardInCell(source, inMove.CardId);
        if (card == null)
            return;

        source.Cards.Remove(card);
        source.RefreshVisibleCardCount();
        if (inMove.Type == Elemento.Domain.EMoveType.DiscardCard)
        {
            card.DestroySelf();
            return;
        }

        UICardCell target = FindOnlineCardMoveTarget(inMove);

        if (target == null)
        {
            source.Cards.Add(card);
            source.SetParentCell(card);
            Debug.LogWarning("Online replay target could not be presented: "
                + UnityDomainMapper.CreateMoveKey(inMove));
            return;
        }

        bool isPowerCard = card.CardType == ECardType.Heal
            || card.CardType == ECardType.Freeze || card.CardType == ECardType.Lightning;
        bool isBombEffect = card.Data.IsBombOrCombo
            && (inMove.PlayerId != inMove.TargetPlayerId
                || target.ElementZone?.IsFrozen == true);
        if (isPowerCard || isBombEffect)
            card.DestroySelf();
        else
        {
            target.Cards.Add(card);
            target.SetParentCell(card);
            if (target.CellType == UICardCell.ECellType.Hand)
                target.gameObject.SetActive(true);
        }
        this.PlayerBoards[inMove.PlayerId].Hand.RefreshPlayerHandText();
        if (inMove.TargetPlayerId >= 0 && inMove.TargetPlayerId < this.PlayerBoards.Length)
            this.PlayerBoards[inMove.TargetPlayerId].Hand.RefreshPlayerHandText();
    }

    private UICardCell FindOnlineCardMoveTarget(DomainMoveData inMove, Card inMovingCard = null)
    {
        if (inMove.TargetZoneId >= 0)
            return FindOnlineZone(inMove.TargetPlayerId, inMove.TargetZoneId)?.CardCell;

        if (inMove.TargetPlayerId >= 0 && inMove.TargetPlayerId < this.PlayerBoards.Length)
        {
            return this.PlayerBoards[inMove.TargetPlayerId].Hand.CardCells.Find(
                inCell => inCell != null && (inCell.TopCard == null
                    || ReferenceEquals(inCell.TopCard, inMovingCard)));
        }

        return null;
    }

    private void AddOnlineReplayDraws(TurnActionData inAction,
        DomainGameStateData inFinalState)
    {
        foreach (var card in inAction.DrawnCards)
        {
            int owner = FindOnlineHandOwner(inFinalState, card.Id);
            if (owner >= 0)
                AddOnlineReplayHandCard(owner, card);
        }
    }

    private void AddOnlineReplayHandCard(int inSeat, DomainCardInstanceData inCard)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length || inCard == null)
            return;

        UIHand hand = this.PlayerBoards[inSeat].Hand;
        int cellIndex = hand.CardCells.FindIndex(inCell => inCell != null
            && inCell.TopCard == null);
        if (cellIndex < 0)
        {
            Debug.LogWarning("Online replay has no empty hand cell for card " + inCard.Id);
            return;
        }

        DomainCardInstanceData visibleCard = inCard;
        bool isLocalCard = inSeat == this.onlineClient.LocalSeat;
        if (!isLocalCard)
        {
            visibleCard = new DomainCardInstanceData
            {
                Id = "hidden-" + inSeat + "-replay-" + inCard.Id,
                IsFaceUp = false
            };
        }
        AddOnlineHandCard(this.PlayerBoards[inSeat], cellIndex, visibleCard, isLocalCard);
        hand.RefreshPlayerHandText();
    }

    private static int FindOnlineHandOwner(DomainGameStateData inState, string inCardId)
    {
        if (inState == null || string.IsNullOrEmpty(inCardId))
            return -1;
        foreach (var player in inState.Players)
            if (player.Hand.Exists(inCard => inCard.Id == inCardId))
                return player.Id;
        return -1;
    }

    private IEnumerator AnimateOnlineActivationCo(DomainMoveData inMove)
    {
        UIElementZone zone = FindOnlineZone(inMove.PlayerId, inMove.SourceZoneId);
        if (zone == null)
            yield break;

        foreach (var card in zone.CardCell.Cards)
        {
            if (card != null)
                card.transform.DOMove(this.DiscardZoneTransform.position,
                    this.OpponentMoveTweenDuration).SetEase(Ease.InOutSine);
        }
        yield return new WaitForSeconds(this.OpponentMoveTweenDuration);
    }

    private IEnumerator AnimateOnlineCardMoveCo(DomainMoveData inMove, TurnPackageData inTurn,
        DomainGameStateData inStartState)
    {
        UICardCell source = FindOnlineCardCell(inMove.PlayerId, inMove.CardId);
        Card card = FindOnlineCardInCell(source, inMove.CardId);
        if (card == null && inMove.SourceZoneId < 0)
        {
            source = MaterializeOnlineReplayPlaceholder(inMove, inTurn, inStartState);
            card = FindOnlineCardInCell(source, inMove.CardId);
        }
        if (card == null)
        {
            Debug.LogWarning("Online replay card could not be animated: "
                + UnityDomainMapper.CreateMoveKey(inMove));
            yield break;
        }

        UICardCell target = inMove.Type == Elemento.Domain.EMoveType.PlayCard
            ? FindOnlineCardMoveTarget(inMove, card)
            : null;
        UIDragOverlay.IN.SetMovingCardFromHand(card, source);
        Vector3 destination = this.DiscardZoneTransform.position;
        if (target != null)
        {
            if (target.CellType == UICardCell.ECellType.Hand)
                target.gameObject.SetActive(true);

            destination = target.GetCardLandingPosition(card);
            Vector3 destinationScale = target.GetCardLandingLocalScale(card.transform.parent);
            if ((card.transform.localScale - destinationScale).sqrMagnitude > 0.0001f)
                card.transform.DOScale(destinationScale, this.OpponentMoveTweenDuration)
                    .SetEase(Ease.InOutSine);
        }

        Tween moveTween = card.transform.DOMove(destination, this.OpponentMoveTweenDuration)
            .SetEase(Ease.InOutSine);
        yield return moveTween.WaitForCompletion(true);
    }

    private static Card FindOnlineCardInCell(UICardCell inCell, string inCardId)
    {
        return inCell?.Cards.Find(inCard => inCard != null && inCard.Id == inCardId);
    }

    private UICardCell MaterializeOnlineReplayPlaceholder(DomainMoveData inMove,
        TurnPackageData inTurn, DomainGameStateData inStartState)
    {
        if (inMove.PlayerId < 0 || inMove.PlayerId >= this.PlayerBoards.Length)
            return null;

        DomainCardInstanceData cardData = FindOnlineCard(inStartState, inMove.CardId)
            ?? FindOnlineCard(inTurn?.FinalState, inMove.CardId);
        if (cardData == null)
        {
            foreach (var action in inTurn?.Actions ?? new List<TurnActionData>())
            {
                if (action?.PowerCard?.Id == inMove.CardId)
                    cardData = action.PowerCard;
                if (cardData == null)
                    cardData = action?.DrawnCards?.Find(inCard => inCard.Id == inMove.CardId);
                if (cardData != null)
                    break;
            }
        }
        if (cardData == null)
            return null;

        UIHand hand = this.PlayerBoards[inMove.PlayerId].Hand;
        UICardCell cell = hand.CardCells.Find(inCell => inCell?.TopCard != null
            && inCell.TopCard.Id != null
            && inCell.TopCard.Id.StartsWith("hidden-" + inMove.PlayerId + "-"));
        if (cell?.TopCard != null)
        {
            cell.TopCard.DestroySelf();
            cell.Cards.Clear();
        }
        else
            cell = hand.CardCells.Find(inCell => inCell != null && inCell.TopCard == null);
        if (cell == null)
            return null;

        AddOnlineHandCard(this.PlayerBoards[inMove.PlayerId], hand.CardCells.IndexOf(cell),
            cardData, false);
        return cell;
    }

    private static DomainCardInstanceData FindOnlineCard(DomainGameStateData inState, string inCardId)
    {
        if (inState == null || string.IsNullOrEmpty(inCardId))
            return null;
        foreach (var player in inState.Players)
        {
            DomainCardInstanceData card = player.Hand.Find(inItem => inItem.Id == inCardId);
            if (card != null)
                return card;
            foreach (var zone in player.Zones)
            {
                card = zone.Cards.Find(inItem => inItem.Id == inCardId);
                if (card != null)
                    return card;
            }
        }
        return inState.DiscardPile.Find(inItem => inItem.Id == inCardId);
    }

    private UICardCell FindOnlineCardCell(int inSeat, string inCardId)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length || string.IsNullOrEmpty(inCardId))
            return null;
        UIPlayerBoard board = this.PlayerBoards[inSeat];
        UICardCell cell = board.Hand.CardCells.Find(inItem => inItem?.TopCard?.Id == inCardId);
        if (cell != null)
            return cell;
        foreach (var zone in board.ElementZones)
        {
            if (zone.CardCell.Cards.Exists(inCard => inCard?.Id == inCardId))
                return zone.CardCell;
        }
        return null;
    }

    private UIElementZone FindOnlineZone(int inSeat, int inZone)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length || inZone < 0
            || inZone >= this.PlayerBoards[inSeat].ElementZones.Length)
            return null;
        return this.PlayerBoards[inSeat].ElementZones[inZone];
    }

    private static void LogOnlineReplayMismatch(TurnPackageData inTurn, string inReason,
        string inActualHash)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.LogError("ONLINE REPLAY HASH MISMATCH match=" + inTurn?.MatchId
            + " turn=" + inTurn?.TurnNumber + " reason=" + inReason
            + " actual=" + inActualHash + " expected=" + inTurn?.FinalStateHash
            + ". Rebuilding from the committed final state.");
#endif
    }

    private void ClearOnlineReplayHistory()
    {
        this.onlineReplayStartState = null;
        this.onlineReplayTurns.Clear();
    }

    private void PersistOnlineReplayHistory(DomainGameStateData inStartState, TurnPackageData inTurn)
    {
        if (inStartState == null || inTurn == null || this.onlineClient == null
            || inTurn.Seat == this.onlineClient.LocalSeat
            || (this.onlineClient.CurrentState?.Status != "waiting"
                && this.onlineClient.CurrentState?.Status != "active"))
            return;
        UIOnlineLobbyPanel.SaveOnlineReplayHistory(this.onlineClient.MatchId, new OnlineReplayHistoryData
        {
            StartState = inStartState.Clone(), Turn = inTurn
        });
    }

    private async Task RestoreOnlineReplayHistoryAsync()
    {
        if (!CanRestoreOnlineReplayHistory()
            || UIOnlineLobbyPanel.IN?.IsRecoveryPending == true)
            return;

        OnlineMatchClient client = this.onlineClient;
        string matchId = client.MatchId;
        try
        {
            OnlineReplayHistoryData history = UIOnlineLobbyPanel.LoadOnlineReplayHistory(matchId);
            if (!IsCurrentOnlineReplayHistory(history, client.CurrentState, client.LocalSeat))
                history = await client.FetchLatestOpponentTurnAsync();
            if (this.onlineClient != client || client.MatchId != matchId
                || !CanRestoreOnlineReplayHistory()
                || !IsCurrentOnlineReplayHistory(history, client.CurrentState, client.LocalSeat))
                return;

            this.onlineReplayStartState = history.StartState.Clone();
            this.onlineReplayTurns.Add(history.Turn);
            RefreshOnlineControls();
        }
        catch (Exception exception)
        {
            if (this.onlineClient == client && client.MatchId == matchId)
                HandleOnlineRequestFailed(exception);
        }
    }

    private bool CanRestoreOnlineReplayHistory()
    {
        return this.IsOnline && this.onlineClient?.CurrentState != null
            && this.onlineReplayTurns.Count == 0 && this.onlineReplayCoroutine == null
            && !this.isOnlineReplayActive;
    }

    private static bool IsCurrentOnlineReplayHistory(OnlineReplayHistoryData inHistory,
        MatchStateUpdateData inCurrentState, int inLocalSeat)
    {
        TurnPackageData turn = inHistory?.Turn;
        if (inHistory?.StartState == null || turn?.FinalState == null || inCurrentState == null
            || (!string.IsNullOrEmpty(turn.MatchId) && turn.MatchId != inCurrentState.MatchId)
            || turn.Seat == inLocalSeat)
            return false;

        int revisionAge = inCurrentState.MatchRevision - turn.StartRevision;
        if (revisionAge < 1 || revisionAge > 2)
            return false;

        TurnPackageData latestOpponentTurn = null;
        foreach (var currentTurn in inCurrentState.Turns)
        {
            if (currentTurn.Seat != inLocalSeat
                && (latestOpponentTurn == null
                    || currentTurn.StartRevision > latestOpponentTurn.StartRevision))
                latestOpponentTurn = currentTurn;
        }
        return latestOpponentTurn == null
            || (latestOpponentTurn.StartRevision == turn.StartRevision
                && latestOpponentTurn.FinalStateHash == turn.FinalStateHash);
    }

    private void SaveOnlineLocalReplay(TurnPackageData inTurn)
    {
        if (this.onlineTurnStartState == null || inTurn == null)
            return;
        ClearOnlineReplayHistory();
        this.onlineReplayStartState = this.onlineTurnStartState.Clone();
        this.onlineReplayTurns.Add(inTurn);
    }

    private bool HasOnlineReplayMoves()
    {
        foreach (var turn in this.onlineReplayTurns)
        {
            foreach (var action in turn.Actions)
            {
                if (action?.Move != null
                    && action.Move.Type != Elemento.Domain.EMoveType.EndTurn)
                    return true;
            }
        }
        return false;
    }

    private IEnumerator ReplayOnlineHistoryCo(DomainGameStateData inStartState,
        IReadOnlyList<TurnPackageData> inTurns)
    {
        DomainGameStateData restoreState = this.onlineTurnRecorder?.State.Clone()
            ?? this.onlineRenderedState?.Clone();
        if (inStartState == null || restoreState == null)
            yield break;

        this.isOnlineReplayActive = true;
        this.skipOnlineReplay = false;
        GameManager.IsDoingReplay = true;
        GameManager.IsApplyingRemoteUpdate = true;
        Move.ShouldRecordMoves = false;
        SetOnlineReplayButton(true);
        SetReplayButtonVisibility(false);
        try
        {
            DomainGameStateData state = inStartState.Clone();
            RenderOnlineState(state, false);
            foreach (var turn in inTurns)
            {
                var replay = new TurnReplayer().Replay(state, turn);
                if (!replay.IsMatch)
                    LogOnlineReplayMismatch(turn, replay.Reason, replay.ActualHash);
                yield return AnimateOnlineTurnCo(turn, state);
                state = replay.IsMatch ? replay.State : turn.FinalState.Clone();
                RenderOnlineState(state, false);
                if (this.skipOnlineReplay)
                    break;
            }
        }
        finally
        {
            RenderOnlineState(restoreState, false);
            this.isOnlineReplayActive = false;
            this.skipOnlineReplay = false;
            this.onlineReplayCoroutine = null;
            Time.timeScale = this.gameSpeed;
            ReleaseReplayInputLocks();
            SetOnlineReplayButton(false);
            RefreshOnlineControls();
        }
    }

    private void CancelOnlineReplay()
    {
        if (this.onlineReplayCoroutine != null)
            StopCoroutine(this.onlineReplayCoroutine);
        this.onlineReplayCoroutine = null;
        this.isOnlineReplayActive = false;
        this.skipOnlineReplay = false;
        this.pendingOnlineTurns.Clear();
        Time.timeScale = this.gameSpeed;
        ReleaseReplayInputLocks();
        if (this.speedUpOpponentTurnButton != null)
            SetOnlineReplayButton(false);
    }

    private static IEnumerator RunReplayStepCo(IEnumerator inStep, Action<Exception> inFailed)
    {
        if (inStep == null)
            yield break;
        try
        {
            while (true)
            {
                bool hasNext = false;
                object current = null;
                Exception stepException = null;
                try
                {
                    hasNext = inStep.MoveNext();
                    if (hasNext)
                        current = inStep.Current;
                }
                catch (Exception exception)
                {
                    stepException = exception;
                }

                if (stepException != null)
                {
                    inFailed?.Invoke(stepException);
                    yield break;
                }
                if (!hasNext)
                    yield break;
                if (current is IEnumerator nestedStep)
                {
                    bool nestedFailed = false;
                    yield return RunReplayStepCo(nestedStep, inException =>
                    {
                        nestedFailed = true;
                        inFailed?.Invoke(inException);
                    });
                    if (nestedFailed)
                        yield break;
                }
                else
                    yield return current;
            }
        }
        finally
        {
            (inStep as IDisposable)?.Dispose();
        }
    }

    private static void ReleaseReplayInputLocks()
    {
        Move.ShouldRecordMoves = true;
        GameManager.IsDealingHand = false;
        GameManager.IsDoingActivateSequence = false;
        GameManager.IsDoingReplay = false;
        GameManager.IsApplyingRemoteUpdate = false;
        BoardStateManager.IsDoingReplay = false;
        BoardStateManager.IsSettingState = false;
        if (BoardStateManager.IN != null)
            BoardStateManager.IN.CurrentReplayBoardState = null;
        if (CardsManager.IN != null)
            CardsManager.IN.PowerCardOverride = ECardType.None;
        try
        {
            UIDraggable.ResetInputState();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private void SetOnlineReplayButton(bool inIsVisible)
    {
        this.speedUpOpponentTurnButton.SetActive(inIsVisible);
        var label = this.speedUpOpponentTurnButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = inIsVisible
                ? LocalizationManager.GetOnlineText(LOC.IN.OnlineSkipReplay)
                : LOC.IN.SpeedUp[LOC.LI];
        }
    }

    private void RenderOnlineView(Elemento.Domain.PlayerViewData inView)
    {
        CleanUpGameState();
        this.isDoingCleanup = true;
        try
        {
            RenderOnlineSeat(inView.Player.Id, inView.Player, null);
            foreach (var opponent in inView.Opponents)
            {
                if (opponent?.Zones != null)
                    RenderOnlineSeat(opponent.Id, null, opponent);
            }
        }
        finally
        {
            this.isDoingCleanup = false;
        }

        CardsManager.IN.SetOnlineDeckCount(inView.DeckCount);
        this.Deck.RefreshCellsList();
    }

    private void RenderOnlineSeat(int inSeat, DomainPlayerStateData inPlayer,
        DomainOpponentViewData inOpponent)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length)
            return;

        var board = this.PlayerBoards[inSeat];
        board.Hand.RefreshCardCells();
        if (inPlayer != null)
        {
            for (int i = 0; i < inPlayer.Hand.Count && i < board.Hand.CardCells.Count; i++)
                AddOnlineHandCard(board, i, inPlayer.Hand[i],
                    inSeat == this.onlineClient.LocalSeat);
            RenderOnlineZones(board, inPlayer.Zones, inSeat == this.onlineClient.LocalSeat);
        }
        else if (inOpponent != null)
        {
            for (int i = 0; i < inOpponent.HandCount && i < board.Hand.CardCells.Count; i++)
            {
                AddOnlineHandCard(board, i, new DomainCardInstanceData
                {
                    Id = "hidden-" + inSeat + "-" + i,
                    IsFaceUp = false
                }, false);
            }
            RenderOnlineZones(board, inOpponent.Zones, false);
        }

        board.CalculateOnlineTotalPointsInstantly();
        board.Hand.RefreshPlayerHandText();
    }

    private void AddOnlineHandCard(UIPlayerBoard inBoard, int inCellIndex,
        DomainCardInstanceData inCard, bool inIsDraggable)
    {
        var cell = inBoard.Hand.CardCells[inCellIndex];
        var card = SpawnOnlineCard(inCard, cell.CardHolder, inIsDraggable);
        if (card == null)
            return;

        cell.Cards.Clear();
        cell.AddCardToCell(card);
        cell.gameObject.SetActive(true);
    }

    private void RenderOnlineZones(UIPlayerBoard inBoard, List<DomainZoneStateData> inZones,
        bool inIsDraggable)
    {
        foreach (var zoneData in inZones)
        {
            if (zoneData.Id < 0 || zoneData.Id >= inBoard.ElementZones.Length)
                continue;

            var zone = inBoard.ElementZones[zoneData.Id];
            foreach (var cardData in zoneData.Cards)
            {
                var card = SpawnOnlineCard(cardData, zone.CardCell.CardHolder, inIsDraggable);
                if (card == null)
                    continue;
                zone.CardCell.AddCardToCell(card);
                card.SetDimmedDecorations(zone.CardCell.ElementType);
            }

            zone.Points = zoneData.Points;
            zone.FaceUpPoints = zoneData.FaceUpPoints;
            zone.SetMaxHealth(zoneData.MaxHealth);
            zone.SetZoneHealth(zoneData.Health);
            zone.SetFrozenHealth(zoneData.FrozenHealth, false);
            zone.SetActiveState(zoneData.IsActivated && !zoneData.IsDestroyed, false);
            zone.SetPointsCount(zoneData.Points);
        }
    }

    private Card SpawnOnlineCard(DomainCardInstanceData inCard, Transform inParent,
        bool inIsDraggable)
    {
        if (inCard == null)
            return null;

        CardData cardData = FindOnlineCardData(inCard);
        if (cardData == null)
        {
            Debug.LogWarning("Online state contains unknown card definition "
                + inCard.DefinitionId + ".");
            return null;
        }

        return CardsManager.SpawnCard(cardData, inParent, !inCard.IsFaceUp,
            inIsDraggable, false, inCard.Id);
    }

    private CardData FindOnlineCardData(DomainCardInstanceData inCard)
    {
        var definitions = inCard.IsPowerCard
            ? CardsManager.IN.SinglePowerCardDatas : CardsManager.IN.AllCardDatas;
        CardData definition = null;
        foreach (var cardData in definitions)
        {
            if (cardData.Index == inCard.DefinitionId)
            {
                definition = cardData;
                break;
            }
        }

        if (definition == null && !inCard.IsFaceUp && CardsManager.IN.AllCardDatas.Count > 0)
            definition = CardsManager.IN.AllCardDatas[0];
        if (definition == null)
            return null;

        var result = CardData.Clone(definition);
        if (!inCard.IsFaceUp)
            return result;

        result.Name = string.IsNullOrEmpty(inCard.DefinitionName)
            ? result.Name : inCard.DefinitionName;
        result.Index = inCard.DefinitionId;
        result.IsPowerCard = inCard.IsPowerCard;
        result.WildPoints = inCard.WildPoints;
        result.WildMultiplier = inCard.WildMultiplier;
        result.WildBomb = inCard.WildBomb;
        result.WildShield = inCard.WildShield;
        result.WildZoneHeal = inCard.WildZoneHeal;
        result.WildFreeze = inCard.WildFreeze;
        result.WildLightning = inCard.WildLightning;
        result.Points = inCard.Points;
        result.Multipliers = inCard.Multipliers;
        result.Bombs = inCard.Bombs;
        result.Shields = inCard.Shields;
        return result;
    }

    private void RefreshOnlineControls()
    {
        RefreshWaitingForOpponentPanel();
        bool isRecoveryPending = UIOnlineLobbyPanel.IN?.IsRecoveryPending == true;

        DomainGameStateData state = this.onlineTurnRecorder?.State
            ?? this.onlineClient?.CurrentState?.State;
        if (state == null)
        {
            this.beginTurnButton.SetActive(false);
            this.endTurnButton.SetActive(false);
            this.discardToEndTurnZone.SetActive(false);
            this.DiscardZone.SetActive(false);
            this.speedUpOpponentTurnButton.SetActive(false);
            SetReplayButtonVisibility(false);
            return;
        }

        bool canMove = !isRecoveryPending && this.IsPlayersTurn && IsHumanPlayingPhase()
            && !this.isOnlineMovePending && this.onlineTurnRecorder != null
            && !this.onlineTurnRecorder.IsComplete && !this.isMoveBudgetEndPending;
        bool canEndTurn = !isRecoveryPending && this.IsPlayersTurn && IsHumanPlayingPhase()
            && !this.isOnlineMovePending && this.onlineTurnRecorder != null
            && (this.onlineTurnRecorder.IsComplete
                || SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn);
        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(canEndTurn);
        this.discardToEndTurnZone.SetActive(canMove
            && SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);
        this.DiscardZone.SetActive(canMove
            && SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);
        SetReplayButtonVisibility(!isRecoveryPending && this.IsPlayersTurn && !this.isOnlineMovePending
            && !this.isOnlineReplayActive && HasOnlineReplayMoves());
        SetOnlineReplayButton(this.isOnlineReplayActive);
        if (isRecoveryPending)
            this.playerTurnText.text =
                LocalizationManager.GetOnlineText(LOC.IN.OnlineResuming);
        else if (this.isOnlineMovePending)
            this.playerTurnText.text =
                LocalizationManager.GetOnlineText(LOC.IN.OnlineSendingTurn);
        RefreshUndoControls();
    }

    private void RefreshWaitingForOpponentPanel()
    {
        UIWaitingForOpponentPanel panel = UIWaitingForOpponentPanel.IN;
        if (panel == null)
            return;

        bool shouldShow = this.IsOnline && this.IsGameInProgress
            && this.onlineClient?.CurrentState?.State != null
            && this.onlineClient.CurrentState.Status == "active"
            && !this.IsPlayersTurn && !this.isOnlineReplayActive;

        panel.SetOpponentAway(this.opponentPresenceTracker?.IsAway ?? false);

        if (shouldShow && !panel.IsShowing)
            panel.Show();
        else if (!shouldShow && panel.IsShowing)
            panel.Hide();
    }

    private bool TryRecordOnlineMove(Move inMove, out TurnActionData outAction,
        out string outReason)
    {
        outAction = null;
        outReason = null;
        if (!this.IsOnline || this.onlineClient == null
            || this.onlineTurnRecorder == null || inMove == null)
        {
            outReason = "The current online turn is unavailable.";
            return false;
        }

        try
        {
            var view = FairView.BuildSeatView(this.onlineTurnRecorder.State,
                this.onlineClient.LocalSeat).View;
            if (!UnityDomainMapper.TryCreateOnlineMove(inMove, view,
                out DomainMoveData domainMove))
            {
                outReason = "The Unity move could not be mapped.";
                return false;
            }

            domainMove.ExpectedRevision = this.onlineTurnRecorder.State.Revision;
            int actionIndex = this.onlineTurnRecorder.Actions.Count;
            this.onlineTurnRecorder.ApplyMove(domainMove);
            outAction = this.onlineTurnRecorder.Actions[actionIndex];
            this.onlineRenderedState = this.onlineTurnRecorder.State.Clone();
            CardsManager.IN.SetOnlineDeckCount(this.onlineTurnRecorder.State.Deck.Count);
            return true;
        }
        catch (Exception exception)
        {
            outReason = exception.Message;
            return false;
        }
    }

    private void RestoreOnlineActionCheckpoint()
    {
        if (this.lastCreatedBoardState == null)
            return;
        bool wasApplyingUpdate = GameManager.IsApplyingRemoteUpdate;
        GameManager.IsApplyingRemoteUpdate = true;
        try
        {
            SetGameToBoardState(this.lastCreatedBoardState);
        }
        finally
        {
            GameManager.IsApplyingRemoteUpdate = wasApplyingUpdate;
        }
    }

    private async void SubmitOnlineTurn(TurnPackageData inTurn)
    {
        if (!this.IsOnline || this.onlineClient == null || inTurn == null
            || this.isOnlineMovePending)
            return;

        inTurn.MatchId = this.onlineClient.MatchId;
        SaveOnlineLocalReplay(inTurn);
        TurnRecorder submittedRecorder = this.onlineTurnRecorder;
        this.isOnlineMovePending = true;
        RefreshOnlineControls();
        try
        {
            SubmitTurnResultData result = await this.onlineClient.SubmitTurnAsync(inTurn);
            if (result == null || !result.Accepted)
            {
                string reason = result?.Reason ?? "missing_result";
                Debug.LogWarning(SupabaseHttpClient.BuildFailureWarning(
                    "Online turn rejected", result?.HttpStatusCode ?? 0,
                    reason, result?.ResponseBody));
                if (reason == "stale_revision")
                    ApplyOnlineState(this.onlineClient.CurrentState);
                else if (this.onlineClient.CurrentState?.Status != "active")
                    ApplyOnlineState(this.onlineClient.CurrentState);
                return;
            }

            if (ReferenceEquals(this.onlineTurnRecorder, submittedRecorder))
            {
                this.onlineTurnRecorder = null;
                this.onlineTurnStartState = null;
            }
            bool replayOwnsUpdate = this.isOnlineReplayActive
                || this.onlineReplayCoroutine != null || this.pendingOnlineTurns.Count > 0;
            if (!replayOwnsUpdate && this.onlineTurnRecorder == null
                && this.onlineClient.CurrentState?.State != null)
                ApplyOnlineState(this.onlineClient.CurrentState);
            TryShowOnlineGameOver();
        }
        catch (Exception exception)
        {
            var httpException = exception as SupabaseHttpException;
            Debug.LogWarning(SupabaseHttpClient.BuildFailureWarning(
                "Online turn submission failed", httpException?.ResponseCode ?? 0,
                httpException?.Reason ?? exception.Message, httpException?.ResponseBody));
            if (exception.Message.Contains("426")
                || exception.Message.Contains("update_required"))
                UIAlertPanel.IN.Show(
                    LocalizationManager.GetOnlineText(LOC.IN.OnlineUpdateRequired));
            else
                UIAlertPanel.IN.Show(
                    LocalizationManager.GetOnlineText(LOC.IN.OnlineTurnRetry));
        }
        finally
        {
            this.isOnlineMovePending = false;
            RefreshTurnAndMoveCountDisplay();
            RefreshOnlineControls();
        }
    }

    private void OnDestroy()
    {
        if (this.onlineClient == null)
            return;
        this.onlineClient.StateChanged -= HandleOnlineStateChanged;
        this.onlineClient.OpponentTurnReceived -= HandleOpponentTurnReceived;
        this.onlineClient.MissedTurnReceived -= HandleMissedTurnReceived;
        this.onlineClient.RequestFailed -= HandleOnlineRequestFailed;
        this.onlineClient.PresenceChanged -= HandleOnlinePresenceChanged;
    }

    private void OnApplicationPause(bool inIsPaused)
    {
        this.isApplicationPaused = inIsPaused;
        if (!inIsPaused)
            ResumeOnlineAfterInterruption();
    }

    private void OnApplicationFocus(bool inHasFocus)
    {
        this.isApplicationPaused = !inHasFocus;
        if (inHasFocus)
            ResumeOnlineAfterInterruption();
    }

    private async void ResumeOnlineAfterInterruption()
    {
        if (this.isApplicationPaused || !this.IsOnline || this.onlineClient == null
            || this.isOnlineResumePending)
            return;

        SendOnlineHeartbeat();
        if (this.isOnlineMovePending)
            return;

        this.isOnlineResumePending = true;
        try
        {
            if (this.onlineClient.PendingSubmission != null)
                await this.onlineClient.RetryPendingSubmissionAsync();
            await this.onlineClient.RefreshAsync();
        }
        catch (Exception exception)
        {
            HandleOnlineRequestFailed(exception);
        }
        finally
        {
            this.isOnlineResumePending = false;
        }
    }

    private async void SendOnlineHeartbeat()
    {
        if (this.isApplicationPaused || !this.IsOnline || this.onlineClient == null
            || this.isOnlineHeartbeatPending)
            return;
        this.isOnlineHeartbeatPending = true;
        try
        {
            await this.onlineClient.HeartbeatAsync();
        }
        catch (Exception exception)
        {
            HandleOnlineRequestFailed(exception);
        }
        finally
        {
            this.isOnlineHeartbeatPending = false;
        }
    }

    private void ApplyViewingSeat(int inViewingSeat)
    {
        if (this.seatViewBinder == null)
            return;

        //always rebind: Rebind is idempotent and repairs any drift between the binder and the boards
        this.seatViewBinder.SetViewingSeat(inViewingSeat);
        this.seatViewBinder.Rebind(this.PlayerBoards, this.boardSlots,
            (inFirst, inSecond) => inFirst.SwapPlayerState(inSecond));

        for (int i = 0; i < this.PlayerBoards.Length; i++)
        {
            var expectedType = (EPlayerType)(i + 1);
            if (this.PlayerBoards[i].PlayerType == expectedType)
                continue;

            Debug.LogError($"ApplyViewingSeat: seat {i} board has PlayerType {this.PlayerBoards[i].PlayerType}, expected {expectedType}. Repairing.");
            this.PlayerBoards[i].SetPlayerType(expectedType);
        }
    }

    private void BeginHumanDrawPhase()
    {
        RefreshPassAndPlayHandVisibility();
        RefreshTurnAndMoveCountDisplay();
        this.CurrentPlayerBoard.Hand.RefreshPlayerHandText();

        if (SettingsManager.IN.ShouldAutoDealHand)
            DealCards();
        else
            this.beginTurnButton.SetActive(true);
    }

    private bool TryShowPassAndPlayHandoff()
    {
        if (!this.IsPassAndPlay || !this.gameSession.RequiresHandoff(this.previousPlayerIndex, this.currentPlayerIndex))
            return false;

        this.isHandoffPending = true;
        UISeatHandoffPanel.IN.Show(this.currentPlayerIndex + 1, ContinuePassAndPlayTurn);
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
        RefreshPassAndPlayHandVisibility();
        return true;
    }

    private void ContinuePassAndPlayTurn()
    {
        this.isHandoffPending = false;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
        RefreshPassAndPlayHandVisibility();
        BeginHumanDrawPhase();
    }

    private void RefreshPassAndPlayHandVisibility()
    {
        if (!this.IsPassAndPlay && !this.IsOnline) return;

        for (int i = 0; i < this.PlayerBoards.Length; i++)
        {
            bool isVisible = this.gameSession.IsHandVisible(i, this.currentPlayerIndex, this.isHandoffPending);
            this.PlayerBoards[i].Hand.SetHandVisible(isVisible);
        }
    }

    private void IncrementTurn()
    {
        ++this.CurrentTurnNumber;

        this.PlayerBoard.CurrentMoveNumber = 0;

        if (this.CurrentTurnNumber > 1)
            SetReplayButtonVisibility(true);

        this.PlayerBoard.Hand.RefreshPlayerHandText();
    }

    private void MeltFrozenZones()
    {
        if (SettingsManager.IN.FrozenZonesMeltAmountPerTurn <= 0) return;

        bool shouldPlaySound = false;

        foreach (var zone in this.CurrentPlayerBoard.ElementZones)
        {
            if (zone.IsFrozen)
            {
                zone.SubtractFrozenHealth(SettingsManager.IN.FrozenZonesMeltAmountPerTurn);
                shouldPlaySound = true;
            }
        }

        if (shouldPlaySound)
            AudioManager.IN.PlayClip(AudioManager.IN.MeltIceClip, 1f, 5f);
    }

    #region Win/Lose handling
    public void CheckElementZoneActivateState(UIElementZone inElementZone)
    {
        if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn && this.CurrentPlayerBoard.Hand.NumCardsInHand == 0)
            return;
        else if (SettingsManager.IN.TutorialSafeMovesPerTurn <= 0 && this.MovesRemaining < 1)
            return;

        var pointsRequiredToActivate = UITutorialPanel.IsTutorialMode ? 10 : SettingsManager.IN.ElementZonePointsRequiredToActivate;

        bool hasEnoughPointsToActivate = DomainGameRules.HasReachedActivationThreshold(inElementZone.Points, pointsRequiredToActivate);

        bool isAlreadyActivated = inElementZone.IsActivated;

        inElementZone.SetActiveState(hasEnoughPointsToActivate);

        if (inElementZone.IsActivated && !isAlreadyActivated)
        {
            var localizedElementType = LOC.IN.GetElementNameFromEnum(inElementZone.CardCell.ElementType);

            if (inElementZone.PlayerType == EPlayerType.PlayerA)
            {
                var messageText = string.Format(LOC.IN.YouActivatedElementZone[LOC.LI], localizedElementType, inElementZone.Points, SettingsManager.IN.ElementZonePointsRequiredToActivate);
                UIAlertPanel.IN.Show(messageText, 3f, EAlertMode.Notification);
            }
            else
            {
                //var opponentBoard = this.PlayerBoards[1];

                var messageText = string.Format(LOC.IN.OpponentActivatedElementZone[LOC.LI], localizedElementType, inElementZone.Points, SettingsManager.IN.ElementZonePointsRequiredToActivate);
                UIAlertPanel.IN.Show(messageText, 3f, EAlertMode.Notification);
            }
        }
    }

    public void CheckWinLoseState(UIPlayerBoard inPlayerBoard, bool inIsFromDestroyedZone = false)
    {
        if (this.isDoingCleanup) return;
        if (UIWinLosePanel.IN.IsShowing) return;

        var playerWon = false;

        var message = string.Format(LOC.IN.TotalPointsTitle[LOC.LI], inPlayerBoard.TotalPoints, SettingsManager.IN.TotalPointsRequiredToWin);
        //var message = LOC.IN.InfoAboutYourGame[LOC.LI];// "Here is some info about your game:";

        if (inIsFromDestroyedZone)
        {
            if (!DomainGameRules.AreAllZonesDestroyed(inPlayerBoard.CreateStateData())) return;

            message = LOC.IN.AllZonesDestroyedTitle[LOC.LI];//All Opponent's Zones Destroyed!
            playerWon = !inPlayerBoard.Equals(this.PlayerBoard);
        }
        else
        {
            var rules = new DomainRulesConfigData
            {
                TotalPointsRequiredToWin = SettingsManager.IN.TotalPointsRequiredToWin
            };

            if (!DomainGameRules.HasPointsTargetWin(inPlayerBoard.CreateStateData(), rules)) return;

            if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn && this.CurrentPlayerBoard.Hand.NumCardsInHand == 0)
                return;
            else if (SettingsManager.IN.TutorialSafeMovesPerTurn <= 0 && this.MovesRemaining < 1)
                return;

            playerWon = inPlayerBoard.Equals(this.PlayerBoard);
        }

        //Debug.Log($"CheckWinLoseState({inPlayerBoard.Name}, inIsFromDestroyedZone)   inPlayerBoard.TotalPoints = {inPlayerBoard.TotalPoints}");

        // Online the server decides the result: send the winning turn, then both clients show the
        // panel from the confirmed view (TryShowOnlineGameOver). Replays/renders only re-show state.
        if (this.IsOnline)
        {
            if (this.IsPlayersTurn && !GameManager.IsApplyingRemoteUpdate && !this.isOnlineReplayActive
                && !this.isSubmittingOnlineWin)
                StartCoroutine(SubmitOnlineWinningTurnCo());
            return;
        }

        ShowWinLosePanel(playerWon, message);
    }

    private IEnumerator SubmitOnlineWinningTurnCo()
    {
        this.isSubmittingOnlineWin = true;
        try
        {
            // The winning move is buffered after the zone/points check fires, so let the drop finish.
            yield return null;
            while (UIDraggable.ShouldBlockInput || GameManager.IsDealingHand
                || GameManager.IsDoingActivateSequence)
                yield return null;

            EndTurn();
        }
        finally
        {
            this.isSubmittingOnlineWin = false;
        }
    }

    private void TryShowOnlineGameOver()
    {
        DomainGameStateData state = this.onlineTurnRecorder?.State
            ?? this.onlineClient?.CurrentState?.State;
        // A running replay calls this again when it finishes.
        if (!this.IsOnline || state == null || state.WinnerPlayerId < 0 || UIWinLosePanel.IN.IsShowing
            || this.isOnlineReplayActive)
            return;

        bool playerWon = state.WinnerPlayerId == this.onlineClient.LocalSeat;
        string message;
        if (state.WinCondition == Elemento.Domain.EWinCondition.AllZonesDestroyed)
            message = LOC.IN.AllZonesDestroyedTitle[LOC.LI];
        else
        {
            var winnerBoard = this.PlayerBoards[state.WinnerPlayerId];
            message = string.Format(LOC.IN.TotalPointsTitle[LOC.LI], winnerBoard.TotalPoints,
                SettingsManager.IN.TotalPointsRequiredToWin);
        }

        ShowWinLosePanel(playerWon, message);
    }

    private void ShowWinLosePanel(bool playerWon, string message)
    {
        SetGameInProgress(false);
        BoardStateManager.IsDoingReplay = false;

        StopAllCoroutines();
        foreach (var board in this.PlayerBoards)
        {
            board.StopMovesCoroutine();
        }

        var wrapupItems = new List<WrapupItemData>();
        var playerPoints = new List<int>();

        UIPlayerBoard playerBoard = this.IsOnline ? this.PlayerInputBoard : this.PlayerBoard;
        playerBoard.CalculateTotalPoints(false);

        foreach (var zone in playerBoard.ElementZones)
        {
            int points = zone.IsDestroyed ? -1 : zone.Points;
            playerPoints.Add(points);
        }

        var playerData = new WrapupItemData
        {
            PlayerName = LOC.IN.You[LOC.LI],//"You"
            TotalPoints = playerBoard.TotalPoints,
            ElementPoints = playerPoints
        };

        wrapupItems.Add(playerData);

        UIPlayerBoard opponentBoard = Array.Find(this.PlayerBoards,
            inBoard => !ReferenceEquals(inBoard, playerBoard));

        opponentBoard.CalculateTotalPoints(false);

        var opponentPoints = new List<int>();
        foreach (var zone in opponentBoard.ElementZones)
        {
            int points = zone.IsDestroyed ? -1 : zone.Points;
            opponentPoints.Add(points);
        }

        var opponentData = new WrapupItemData
        {
            PlayerName = LOC.IN.Opponent[LOC.LI], //"Opponent" //TODO: put name
            TotalPoints = opponentBoard.TotalPoints,
            ElementPoints = opponentPoints
        };

        wrapupItems.Add(opponentData);

        var winLoseState = playerWon ? EWinLoseState.Won : EWinLoseState.Lost;

        UIWinLosePanel.IN.Show(winLoseState, message, wrapupItems, () =>
        {
            BoardStateManager.IsDoingReplay = false;
            if (this.IsOnline)
                ReleaseOnlineSession();
            RestartGame();
        },
        () =>
        {
            BoardStateManager.IsDoingReplay = false;
            CleanUpGameState();

            if (this.IsOnline)
                ReleaseOnlineSession();

            UISplashScreen.IN.Show();
            UIGameBoard.IN.Hide();
        });
    }
    #endregion

    public void ResetMovesRemaining(bool inShouldShowDisplay = false)
    {
        var isLimitedMoves = SettingsManager.IN.TutorialSafeMovesPerTurn > 0;
        this.MovesRemaining = isLimitedMoves ? SettingsManager.IN.TutorialSafeMovesPerTurn : 99;
        this.movesRemainingDisplay.SetActive(inShouldShowDisplay && isLimitedMoves);
        //this.movesRemainingPoints.SetText(this.MovesRemaining);
        SetMovesRemaining(this.MovesRemaining);
    }

    public void DecrementMovesRemaining()
    {
        //if (this.TurnPhase != ETurnPhase.PlayerPlaying) return;

        IncrementCurrentMove(this.CurrentPlayerBoard);

        if (SettingsManager.IN.TutorialSafeMovesPerTurn > 0)
            SetMovesRemaining(this.MovesRemaining - 1);
    }

    #region Save/Load Board State
    public void SaveBoardState(UICardCell inSourceCell, UICardCell inTargetCell, bool inIsDiscard = false, UIElementZone inActivatedZone = null, bool inIsDealHand = false, ECardType inCardType = ECardType.None, string inCardId = null)
    {
        Move recordedMove;
        if (this.IsOnline)
        {
            if (!Move.ShouldRecordMoves || this.isOnlineReplayActive || inIsDealHand)
                return;
            recordedMove = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inActivatedZone,
                inIsDealHand, inCardType, (int)this.CurrentPlayer, inCardId);

            bool wasActivationRecorded = inActivatedZone != null && this.isOnlineActivationRecorded;
            this.isOnlineActivationRecorded = false;
            if (!wasActivationRecorded
                && !TryRecordOnlineMove(recordedMove, out _, out string reason))
            {
                Debug.LogWarning("Online action was not recorded: " + reason);
                RestoreOnlineActionCheckpoint();
                UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineMoveIllegal));
                return;
            }
            RefreshOnlineControls();
        }
        else
        {
            if (!Move.ShouldRecordMoves || BoardStateManager.IsDoingReplay || UITutorialPanel.IsTutorialMode) return;

            recordedMove = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inActivatedZone,
                inIsDealHand, inCardType, (int)this.CurrentPlayer, inCardId);
        }

        var boardState = CreateBoardState(inIsDealHand);
        if (this.lastCreatedBoardState != null)
            this.lastCreatedBoardState.Move = recordedMove;

        if (inIsDealHand)
        {
            this.undoHistory.SetCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.Draw);
        }
        else if (inActivatedZone != null)
        {
            this.undoHistory.SetCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.ActivateDeck);
        }
        else if (this.lastCreatedBoardState != null && IsUndoRecordable())
        {
            this.undoHistory.Record(this.lastCreatedBoardState);
        }

        this.lastCreatedBoardState = boardState;

        if (!this.IsOnline)
            BoardStateManager.IN.AddBoardState(boardState);

        RefreshUndoControls();
    }

    private BoardState CreateBoardState(bool inIsDealHand)
    {
        var boardState = new BoardState
        {
            TurnNumber = this.CurrentTurnNumber,
            MoveNumber = this.CurrentPlayerBoard.CurrentMoveNumber,
            TotalMovesTaken = this.CurrentPlayerBoard.TotalMovesTaken,
            MovesRemaining = this.MovesRemaining,
            ActivePlayer = this.CurrentPlayer,
            IsDealHand = inIsDealHand,
            Deck = CaptureDeckState(),
            ShuffledDeck = new(CardsManager.IN.ShuffledCardDatas),
            PowerDeck = new(CardsManager.IN.PowerCardDatas)
        };

        for (int i = 0; i < this.PlayerBoards.Length; i++)
        {
            var board = this.PlayerBoards[i];
            var playerData = new BoardPlayer
            {
                Hand = new BoardHand(),
                CurrentMoveNumber = board.CurrentMoveNumber,
                TotalMovesTaken = board.TotalMovesTaken
            };

            //copy cards in hand
            for (int j = 0; j < board.Hand.CardCells.Count; j++)
            {
                var topCard = board.Hand.CardCells[j].TopCard;
                if (topCard != null)
                {
                    playerData.Hand.Cards.Add(topCard.Data);
                    playerData.Hand.CardIds.Add(topCard.Id);
                    playerData.Hand.CardFaceUpStates.Add(topCard.CardFlip.IsFaceUp);
                }
            }

            //copy cards in ElementZones
            for (int k = 0; k < board.ElementZones.Length; k++)
            {
                playerData.ElementZones[k] = new BoardZone();

                var dataZone = playerData.ElementZones[k];
                var zone = board.ElementZones[k];

                dataZone.ElementType = zone.CardCell.ElementType;
                dataZone.Points = zone.Points;
                dataZone.IsActivated = zone.IsActivated;
                dataZone.IsDestroyed = zone.IsDestroyed;
                dataZone.Health = zone.Health;
                dataZone.MaxHealth = zone.MaxHealth;
                dataZone.FrozenHealth = zone.FrozenHealth;

                for (int l = 0; l < zone.CardCell.Cards.Count; l++)
                {
                    var card = zone.CardCell.Cards[l];
                    dataZone.Cards.Add(card.Data);
                    dataZone.CardIds.Add(card.Id);
                    dataZone.CardFaceUpStates.Add(card.CardFlip.IsFaceUp);
                }
            }

            boardState.Players.Add(playerData);
        }

        TryRepairBoardStateIds(boardState, "capture");
        return boardState;
    }

    private static bool TryRepairBoardStateIds(BoardState inBoardState, string inContext)
    {
        if (inBoardState == null || inBoardState.Deck == null || inBoardState.Players == null)
        {
            Debug.LogError($"Cannot apply {inContext} board snapshot because required state is missing.");
            return false;
        }

        HashSet<string> retainedIds = new();
        int duplicateCount = 0;
        string firstDuplicate = null;

        // Zones are authoritative once a play completes, so retain them before stale hand entries.
        for (int i = 0; i < inBoardState.Players.Count; i++)
        {
            var player = inBoardState.Players[i];
            if (player == null || player.Hand == null || player.ElementZones == null)
            {
                Debug.LogError($"Cannot apply {inContext} board snapshot because player {i} state is missing.");
                return false;
            }

            for (int k = 0; k < player.ElementZones.Length; k++)
            {
                var zone = player.ElementZones[k];
                if (zone == null)
                {
                    Debug.LogError($"Cannot apply {inContext} board snapshot because P{i} zone {k} state is missing.");
                    return false;
                }

                RepairCards(zone.Cards, zone.CardIds, zone.CardFaceUpStates, $"P{i} zone {k}");
            }
        }

        for (int i = 0; i < inBoardState.Players.Count; i++)
        {
            var player = inBoardState.Players[i];
            RepairCards(player.Hand.Cards, player.Hand.CardIds,
                player.Hand.CardFaceUpStates, $"P{i} hand");
        }

        RepairCards(inBoardState.Deck.Cards, inBoardState.Deck.CardIds,
            inBoardState.Deck.CardFaceUpStates, "Deck");

        if (duplicateCount > 0)
        {
            Debug.LogWarning($"Repaired {inContext} board snapshot by removing {duplicateCount} duplicate card placement(s); zones take precedence. First duplicate was {firstDuplicate}.");
        }

        return true;

        void RepairCards(List<CardData> inCards, List<string> inIds,
            List<bool> inFaceUpStates, string inLocation)
        {
            if (inCards == null || inIds == null || inFaceUpStates == null)
                return;

            while (inIds.Count > inCards.Count)
                inIds.RemoveAt(inIds.Count - 1);
            while (inFaceUpStates.Count > inCards.Count)
                inFaceUpStates.RemoveAt(inFaceUpStates.Count - 1);

            for (int i = 0; i < inCards.Count;)
            {
                string id = i < inIds.Count ? inIds[i] : null;
                if (string.IsNullOrEmpty(id) || retainedIds.Add(id))
                {
                    ++i;
                    continue;
                }

                ++duplicateCount;
                if (firstDuplicate == null)
                    firstDuplicate = $"'{id}' in {inLocation}";
                inCards.RemoveAt(i);
                if (i < inIds.Count)
                    inIds.RemoveAt(i);
                if (i < inFaceUpStates.Count)
                    inFaceUpStates.RemoveAt(i);
            }
        }
    }

    public void SetGameToBoardState(BoardState inBoardState, bool inShouldPlay = false)
    {
        BoardStateManager.IsSettingState = true;
        try
        {
            ApplyBoardState(inBoardState, inShouldPlay);
        }
        finally
        {
            BoardStateManager.IsSettingState = false;
        }
    }

    private void ApplyBoardState(BoardState inBoardState, bool inShouldPlay)
    {
        if (!TryRepairBoardStateIds(inBoardState, "restore"))
            return;

        CleanUpGameState(false);

        BoardStateManager.IN.CurrentReplayBoardState = inBoardState;

        this.CurrentTurnNumber = inBoardState.TurnNumber;
        this.currentPlayerIndex = (int)inBoardState.ActivePlayer - 1;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));

        this.CurrentPlayerBoard.CurrentMoveNumber = inBoardState.MoveNumber;
        this.CurrentPlayerBoard.TotalMovesTaken = inBoardState.TotalMovesTaken;
        this.MovesRemaining = inBoardState.MovesRemaining;
        SetMovesRemaining(this.MovesRemaining);

        RefreshTurnAndMoveCountDisplay();

        this.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);

        CardsManager.IN.RestoreCardDataPools(inBoardState.ShuffledDeck, inBoardState.PowerDeck);

        if (this.IsOnline)
            CardsManager.IN.SetOnlineDeckCount(this.onlineTurnRecorder?.State.Deck.Count
                ?? this.onlineRenderedState?.Deck.Count ?? 0);
        else
            RestoreDeckState(inBoardState.Deck);

        for (int i = 0; i < inBoardState.Players.Count; i++)
        {
            var player = inBoardState.Players[i];
            var playerBoard = this.PlayerBoards[i];

            playerBoard.CurrentMoveNumber = player.CurrentMoveNumber;
            playerBoard.TotalMovesTaken = player.TotalMovesTaken;

            playerBoard.Hand.RefreshCardCells();

            //spawn cards into hands of each player
            for (int j = 0; j < player.Hand.Cards.Count; j++)
            {
                var cardData = player.Hand.Cards[j];
                var cell = playerBoard.Hand.CardCells[j];
                string cardId = j < player.Hand.CardIds.Count ? player.Hand.CardIds[j] : null;
                bool isFaceUp = j >= player.Hand.CardFaceUpStates.Count || player.Hand.CardFaceUpStates[j];
                bool isDraggable = !this.IsOnline || i == ((OnlineSession)this.gameSession).LocalSeat;

                var handCard = CardsManager.SpawnCard(cardData, cell.CardHolder, !isFaceUp,
                    isDraggable, false, cardId);
                cell.Cards.Clear();
                cell.AddCardToCell(handCard);
                handCard.CardFlip.SetAnimState(isFaceUp ? UICardFlip.ECardFlipState.Front : UICardFlip.ECardFlipState.Back);
                cell.gameObject.SetActive(true);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(playerBoard.Hand.HandGrid);

            //spawn cards into each element zone of each player
            for (int j = 0; j < player.ElementZones.Length; j++)
            {
                var zoneData = player.ElementZones[j];
                var zone = playerBoard.ElementZones[j];

                zone.RestoreHealthState(zoneData.MaxHealth, zoneData.Health,
                    zoneData.IsDestroyed);

                for (int k = 0; k < zoneData.Cards.Count; k++)
                {
                    var cardData = zoneData.Cards[k];
                    string cardId = k < zoneData.CardIds.Count ? zoneData.CardIds[k] : null;
                    bool isFaceUp = k >= zoneData.CardFaceUpStates.Count || zoneData.CardFaceUpStates[k];
                    bool isDraggable = !this.IsOnline || i == ((OnlineSession)this.gameSession).LocalSeat;

                    var zoneCard = CardsManager.SpawnCard(cardData, zone.CardCell.CardHolder,
                        !isFaceUp, isDraggable, false, cardId);

                    zone.CardCell.AddCardToCell(zoneCard);
                    zoneCard.CardFlip.SetAnimState(isFaceUp ? UICardFlip.ECardFlipState.Front : UICardFlip.ECardFlipState.Back);
                    zoneCard.SetDimmedDecorations(zone.CardCell.ElementType);
                }

                zone.CalculatePoints(false);
                zone.SetFrozenHealth(zoneData.FrozenHealth, false);
                zone.SetActiveState(!zoneData.IsDestroyed && !zoneData.IsFrozen
                    && zoneData.IsActivated, false);
            }

            playerBoard.CalculateTotalPointsInstantly(false);
        }

        this.PlayerBoard.Hand.RefreshPlayerHandText();

        if (inShouldPlay && inBoardState.Move != null && !inBoardState.Move.IsDealHand)
            StartCoroutine(DoGameStateMoveCo(inBoardState));
        else
            BoardStateManager.IN.CurrentReplayBoardState = null;
    }

    private BoardHand CaptureDeckState()
    {
        var deckState = new BoardHand();
        foreach (var card in this.Deck.Cards)
        {
            deckState.Cards.Add(card.Data);
            deckState.CardIds.Add(card.Id);
            deckState.CardFaceUpStates.Add(card.CardFlip.IsFaceUp);
        }
        return deckState;
    }

    private void RestoreDeckState(BoardHand inDeckState)
    {
        for (int i = 0; i < inDeckState.Cards.Count; i++)
        {
            string cardId = i < inDeckState.CardIds.Count ? inDeckState.CardIds[i] : null;
            bool isFaceUp = i >= inDeckState.CardFaceUpStates.Count || inDeckState.CardFaceUpStates[i];
            CardsManager.SpawnCard(inDeckState.Cards[i], this.Deck.CardHolder, !isFaceUp,
                false, false, cardId);
        }
        this.Deck.RefreshCellsList();
    }

    public void HandleUndoButtonPress()
    {
        if (!SettingsManager.IN.AllowUndo || GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput
            || !this.IsPlayersTurn || this.IsCurrentSeatControlledByAI || !IsHumanPlayingPhase())
            return;

        if (!this.undoHistory.TryPop(out BoardState boardState,
            out Elemento.Domain.ETurnUndoCheckpoint blockedBy))
        {
            ShowUndoBlockedHint(blockedBy);
            RefreshUndoControls();
            return;
        }

        if (this.IsOnline && (this.onlineTurnRecorder == null || !this.onlineTurnRecorder.Undo()))
        {
            this.undoHistory.Record(boardState);
            ShowUndoBlockedHint(Elemento.Domain.ETurnUndoCheckpoint.None);
            RefreshUndoControls();
            return;
        }

        if (this.IsOnline)
            this.onlineRenderedState = this.onlineTurnRecorder.State.Clone();

        UndoMove(boardState);
    }

    public void SetUndoCheckpoint(Elemento.Domain.ETurnUndoCheckpoint inCheckpoint)
    {
        if (this.isDoingCleanup || GameManager.IsApplyingRemoteUpdate
            || GameManager.IsDoingReplay || BoardStateManager.IsDoingReplay)
            return;

        bool didBlockMoves = SettingsManager.IN.AllowUndo && this.undoHistory.CanUndo;
        this.undoHistory.SetCheckpoint(inCheckpoint);
        if (inCheckpoint == Elemento.Domain.ETurnUndoCheckpoint.RevealHiddenCard
            && this.isGameInitialized && this.IsPlayersTurn && IsHumanPlayingPhase())
        {
            this.lastCreatedBoardState = CreateBoardState(false);
            if (!this.IsOnline)
                BoardStateManager.IN.AddBoardState(this.lastCreatedBoardState);
        }
        if (didBlockMoves)
            ShowUndoBlockedHint(inCheckpoint);
        RefreshUndoControls();
    }

    private void UndoMove(BoardState inBoardState)
    {
        if (inBoardState.Move == null)
        {
            RefreshUndoControls();
            return;
        }

        SetGameToBoardState(inBoardState);
        this.lastCreatedBoardState = inBoardState;
        inBoardState.Move = null;
        if (!this.IsOnline)
            BoardStateManager.IN.RemoveLastBoardState();

        TryHighlightDiscardPile();
        if (!this.undoHistory.CanUndo)
            ShowUndoBlockedHint(this.undoHistory.Checkpoint);
        if (this.IsOnline)
            RefreshOnlineControls();
        ValidateUndoCardState(inBoardState);
        RefreshUndoControls();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateUndoCardState(BoardState inBoardState)
    {
        List<string> expectedIds = new();
        AddCardIds(expectedIds, inBoardState.Deck.CardIds);
        foreach (var player in inBoardState.Players)
        {
            AddCardIds(expectedIds, player.Hand.CardIds);
            foreach (var zone in player.ElementZones)
                AddCardIds(expectedIds, zone.CardIds);
        }

        int expectedAnonymousCount = inBoardState.ShuffledDeck.Count + inBoardState.PowerDeck.Count;
        List<string> logicalIds = new();
        AddCardIds(logicalIds, this.Deck.Cards);
        foreach (var playerBoard in this.PlayerBoards)
        {
            foreach (var cell in playerBoard.Hand.CardCells)
                AddCardIds(logicalIds, cell.Cards);
            foreach (var zone in playerBoard.ElementZones)
                AddCardIds(logicalIds, zone.CardCell.Cards);
        }
        AddCardIds(logicalIds, GetLiveCards(this.DiscardZoneTransform));
        AddCardIds(logicalIds, GetLiveCards(this.DiscardToEndTurnZoneTransform));

        int actualAnonymousCount = CardsManager.IN.ShuffledCardDatas.Count + CardsManager.IN.PowerCardDatas.Count;
        if (!Elemento.Domain.CardIdentityValidator.TryValidate(expectedIds, expectedAnonymousCount,
            logicalIds, actualAnonymousCount, out string logicalError))
        {
            Debug.LogError("Undo card ownership invariant failed: " + logicalError);
        }

        List<string> registryIds = new();
        AddCardIds(registryIds, CardsManager.IN.SpawnedCards);
        if (!Elemento.Domain.CardIdentityValidator.TryValidate(expectedIds, 0,
            registryIds, 0, out string registryError))
        {
            Debug.LogError("Undo CardsManager invariant failed: " + registryError);
        }

        List<string> viewIds = new();
        foreach (var card in Resources.FindObjectsOfTypeAll<Card>())
        {
            if (card == null || card.IsDestroyed || !card.gameObject.scene.IsValid()
                || string.IsNullOrEmpty(card.Id))
                continue;
            viewIds.Add(card.Id);
        }

        if (!Elemento.Domain.CardIdentityValidator.TryValidate(expectedIds, 0,
            viewIds, 0, out string viewError))
        {
            Debug.LogError("Undo card GameObject invariant failed: " + viewError);
        }
    }

    private static void AddCardIds(List<string> inIds, IEnumerable<string> inCardIds)
    {
        if (inCardIds == null) return;
        foreach (string cardId in inCardIds)
            inIds.Add(cardId);
    }

    private static void AddCardIds(List<string> inIds, IEnumerable<Card> inCards)
    {
        if (inCards == null) return;
        foreach (var card in inCards)
        {
            if (card != null && !card.IsDestroyed)
                inIds.Add(card.Id);
        }
    }

    private static Card[] GetLiveCards(Transform inParent)
    {
        return inParent == null ? Array.Empty<Card>() : inParent.GetComponentsInChildren<Card>(true);
    }

    private bool IsUndoRecordable()
    {
        return !this.undoHistory.IsLocked && this.IsPlayersTurn
            && !this.IsCurrentSeatControlledByAI && IsHumanPlayingPhase();
    }

    private void BeginUndoTurn(Elemento.Domain.ETurnUndoCheckpoint inCheckpoint)
    {
        this.undoHistory.BeginTurn(inCheckpoint);
        this.lastCreatedBoardState = null;
        RefreshUndoControls();
    }

    private void LockUndoTurn()
    {
        this.undoHistory.LockTurn();
        RefreshUndoControls();
    }

    private void RefreshUndoControls()
    {
        if (this.undoButtonParent == null)
            return;

        bool isAllowed = SettingsManager.IN != null && SettingsManager.IN.AllowUndo;
        this.undoButtonParent.SetActive(isAllowed);
        if (this.undoButton != null)
            this.undoButton.interactable = isAllowed && this.undoHistory.CanUndo
                && !GameManager.ShouldBlockInput && !UIDraggable.ShouldBlockInput
                && this.IsPlayersTurn && !this.IsCurrentSeatControlledByAI && IsHumanPlayingPhase();
    }

    private static void ShowUndoBlockedHint(Elemento.Domain.ETurnUndoCheckpoint inCheckpoint)
    {
        string[] message = null;
        switch (inCheckpoint)
        {
            case Elemento.Domain.ETurnUndoCheckpoint.Draw:
                message = LOC.IN.UndoPastDraw;
                break;
            case Elemento.Domain.ETurnUndoCheckpoint.RevealHiddenCard:
                message = LOC.IN.UndoPastReveal;
                break;
            case Elemento.Domain.ETurnUndoCheckpoint.ActivateDeck:
                message = LOC.IN.UndoPastActivateDeck;
                break;
            case Elemento.Domain.ETurnUndoCheckpoint.ChoosePowerCard:
                message = LOC.IN.UndoPastPowerChoice;
                break;
            case Elemento.Domain.ETurnUndoCheckpoint.EndTurn:
                message = LOC.IN.UndoTurnLocked;
                break;
        }
        if (message != null)
            UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(message));
    }

    private IEnumerator DoGameStateMoveCo(BoardState inBoardState)
    {
        Move.ShouldRecordMoves = false;
        GameManager.IsDoingReplay = true;
        IEnumerator replayStep = DoGameStateMoveStepsCo(inBoardState);
        try
        {
            while (replayStep.MoveNext())
                yield return replayStep.Current;
        }
        finally
        {
            (replayStep as IDisposable)?.Dispose();
            Move.ShouldRecordMoves = true;
            GameManager.IsDoingReplay = false;
            if (BoardStateManager.IN != null)
                BoardStateManager.IN.CurrentReplayBoardState = null;
        }
    }

    private IEnumerator DoGameStateMoveStepsCo(BoardState inBoardState)
    {
        //Debug.Log($"<color=yellow>GameManager.IsDoingReplay = { GameManager.IsDoingReplay}</color>");

        var move = inBoardState.Move;
        var playerIndex = (int)GetReplayMovePlayerType(inBoardState) - 1;
        var playerBoard = this.PlayerBoards[playerIndex];
        var isPlayer = playerIndex == 0;

        if (move.IsDiscard)
        {
            this.DiscardZone.SetActive(true);
            yield return new WaitForSeconds(.35f);
        }
        else if (move.ActivatePowerZone != null)
        {
            move.ActivatePowerZone.SetActiveState(true, false);

            yield return new WaitForSeconds(.5f);

            yield return move.ActivatePowerZone.UseActivationPowerCo();
        }
        else
        {
            this.IncrementCurrentMove(playerBoard);
        }

        if (!move.ActivatePowerZone)
        {
            var tweenDuration = this.OpponentMoveTweenDuration;

            var discardPile = this.DiscardZoneTransform;

            if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
                discardPile = isPlayer ? this.DiscardToEndTurnZoneTransform : this.DiscardZoneTransform;

            UICardCell sourceCell = FindReplayCardCell(playerBoard, move.Data?.CardId);
            if (sourceCell != null)
            {
                move.SourceCell = sourceCell;
                move.SourceCell.SetParentCell(move.SourceCell.TopCard, false);
                yield return null;
            }
            else if (move.SourceCellIndex > -1)
            {
                var child = playerBoard.Hand.HandGrid.GetChild(move.SourceCellIndex);
                move.SourceCell = child.GetComponent<UICardCell>();
                move.SourceCell.SetParentCell(move.SourceCell.TopCard, false);
                yield return null;
            }

            if (move.TargetCellIndex > -1)
            {
                var child = playerBoard.Hand.HandGrid.GetChild(move.TargetCellIndex);
                move.TargetCell = child.GetComponent<UICardCell>();
                move.TargetCell.gameObject.SetActive(true);
                yield return null;
            }

            yield return null;

            //Debug.Log($">>>>> ID = {move.ID}   move.TargetCell = {move.TargetCell}  move.SourceCell = {move.SourceCell}   move.discard = {move.IsDiscard}   move.power = {move.ActivatePowerZone}");

            var delayAtTweenEnd = move.IsDiscard ? .35f : 0;
            var sourceCard = move.SourceCell.TopCard;

            if (move.IsDiscard)
                sourceCard.Draggable.AnimateTo(discardPile.position, tweenDuration, delayAtTweenEnd);
            else
                sourceCard.Draggable.AnimateTo(move.TargetCell, tweenDuration, delayAtTweenEnd);
            sourceCard.transform.DORotate(Vector3.zero, tweenDuration);

            if (move.IsDiscard)
            {
                //move.SourceCell.TopCard.transform.DOScale(Vector3.one * .4f, tweenDuration);
                sourceCard.transform.DOScale(Vector3.one * .001f, tweenDuration).SetDelay(tweenDuration + .1f);
            }

            yield return new WaitForSeconds(tweenDuration + delayAtTweenEnd + .05f);

            while (UIConfirmPanel.IN.IsShowing)
            {
                yield return new WaitForSeconds(.1f);
            }

            if (move.IsDiscard)
            {
                yield return new WaitForSeconds(1f);
                this.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);
                yield break;
            }

            if (move.CardType == ECardType.Bomb)
            {
                yield return new WaitForSeconds(1f);

                //TODO: if UI is going to display bomb cards, have a while loop until dismissed
            }
        }

        yield return new WaitForSeconds(.5f);
    }

    private static UICardCell FindReplayCardCell(UIPlayerBoard inPlayerBoard, string inCardId)
    {
        if (inPlayerBoard == null || string.IsNullOrEmpty(inCardId))
            return null;

        UICardCell handCell = inPlayerBoard.Hand.CardCells.Find(
            inCell => inCell?.TopCard?.Id == inCardId);
        if (handCell != null)
            return handCell;

        foreach (var zone in inPlayerBoard.ElementZones)
            if (zone.CardCell.TopCard?.Id == inCardId)
                return zone.CardCell;

        return null;
    }
    #endregion

    public void IncrementCurrentMove(UIPlayerBoard inPlayerBoard)
    {
        ++inPlayerBoard.CurrentMoveNumber;
        ++inPlayerBoard.TotalMovesTaken;

        if (!GameManager.IsDoingReplay)
            SetReplayButtonVisibility(this.IsPlayersTurn);

        RefreshTurnAndMoveCountDisplay();
    }

    private void RefreshTurnAndMoveCountDisplay()
    {
        var playerName = string.Format(LOC.IN.OpponentX[LOC.LI], this.currentPlayerIndex);// $"Opponent {this.currentPlayerIndex}";

        if (this.IsOnline && !this.IsPlayersTurn)
            playerName = LOC.IN.OpponentsTurn[LOC.LI];

        if (this.IsPlayersTurn)
            playerName = SettingsManager.IN.ShouldAutoDealHand ? LOC.IN.YourTurn[LOC.LI] : LOC.IN.YourTurnAuto[LOC.LI];// "Your Turn" : "Your Turn <i>(auto)</i>";

        this.playerTurnText.text = playerName;

        //$"Turn: {this.CurrentTurnNumber}   Move: {this.CurrentPlayerBoard.CurrentMoveNumber}\nTotal Moves: {this.CurrentPlayerBoard.TotalMovesTaken}"
        this.turnAndMoveCountText.text = string.Format(LOC.IN.TurnAndMoveCount[LOC.LI], this.CurrentTurnNumber, this.CurrentPlayerBoard.CurrentMoveNumber, this.CurrentPlayerBoard.TotalMovesTaken);
    }

    private void SetMovesRemaining(int inNumMoves)
    {
        if (SettingsManager.IN.TutorialSafeMovesPerTurn <= 0) return;

        this.MovesRemaining = inNumMoves;

        if (this.MovesRemaining > 0)
            this.isMoveBudgetEndPending = false;

        this.movesRemainingPoints.SetText(this.MovesRemaining);

        //Debug.Log($"SetMovesRemaining({this.MovesRemaining})");

        if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
        {
            if (this.IsPlayersTurn && this.MovesRemaining < 1 && !this.IsCurrentSeatControlledByAI)
                SetDiscardHighlightsVisible(true);
        }
        else if (this.MovesRemaining < 1 && this.IsPlayersTurn)
        {
            if (!this.isMoveBudgetEndPending && !GameManager.IsApplyingRemoteUpdate
                && !this.isDoingCleanup && !this.IsCurrentSeatControlledByAI)
            {
                this.isMoveBudgetEndPending = true;
                if (this.IsOnline)
                    RefreshOnlineControls();
                StartCoroutine(EndTurnAfterMoveBudgetCo());
            }
        }
    }

    private IEnumerator EndTurnAfterMoveBudgetCo()
    {
        yield return null;
        while (GameManager.IsDealingHand || GameManager.IsDoingActivateSequence
            || UIDraggable.ShouldBlockInput)
            yield return null;

        if (!this.isMoveBudgetEndPending)
            yield break;
        this.isMoveBudgetEndPending = false;
        EndTurn();
    }

    public void TryHighlightDiscardPile()
    {
        if (!IsHumanPlayingPhase() || this.IsCurrentSeatControlledByAI) return;

        bool isLastCard = this.CurrentPlayerBoard.Hand.NumCardsInHand <= 1;

        //Debug.Log($"TryHighlightDiscardPile({isLastCard})");
        if (this.IsPlayersTurn)
        {
            if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
                SetDiscardHighlightsVisible(this.MovesRemaining < 1 || isLastCard);
            else
                SetDiscardHighlightsVisible(isLastCard);
        }
    }

    private void SetDiscardHighlightsVisible(bool inIsVisible)
    {
        this.discardPileHighlight.SetActive(inIsVisible);
    }

    public void OnSettingsChanged()
    {
        //TODO: if settings changes require restart, do it here

        ConfigureSession();

        this.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);

        foreach (var board in this.PlayerBoards)
        {
            foreach (var zone in board.ElementZones)
            {
                int lostHealth = zone.MaxHealth - zone.Health;
                zone.SetZoneHealth(SettingsManager.IN.TutorialSafeElementZoneHealth - lostHealth);
                zone.SetMaxHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);

                //TODO: check if all boards die
            }
        }

        Card.OnLightningDamageChanged?.Invoke(SettingsManager.IN.LightningDamage);
        Card.OnFreezeHealthChanged?.Invoke(SettingsManager.IN.FrozenZoneHealth);
        Card.OnZoneHealthChanged?.Invoke(SettingsManager.IN.TutorialSafeElementZoneHealth);

        ResetMovesRemaining(true);

        SetTurnPhase(this.TurnPhase);
        RefreshUndoControls();
    }

    public void HandleGameSpeedSliderChange(Single inValue)
    {
        var formatValue = inValue.ToString("F2");

        if (Mathf.Abs(inValue % 1) < .1f)
        {
            inValue = Mathf.Round(inValue);
            formatValue = inValue.ToString("N0");//$"Game Speed: {inValue:N0}";
        }

        this.gameSpeedText.text = string.Format(LOC.IN.GameSpeed[LOC.LI], formatValue);

        this.gameSpeed = inValue;

        SetGamePaused(false);
    }

    private void SetBoardZoomAnimState(EAnimState inAnimState)
    {
        var isMobile = false;

#if UNITY_ANDROID || UNITY_IOS
        isMobile = true;
#endif

        float aspectRatio = (float)Screen.width / Screen.height;

        if (aspectRatio < 1.5f)
            isMobile = false;

        if (isMobile)
            this.opponentBoardAnim.SetInteger(ANIM_STATE, (int)inAnimState);
        else
            this.opponentBoardAnim.SetInteger(ANIM_STATE, (int)EAnimState.Showing);
    }

    public void HandleEditCardsButtonPress()
    {
        if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput || UITutorialPanel.IsTutorialMode) return;

        UIGameBoard.IN.Hide();
        UIDeckEditor.IN.Show(() =>
        {
            SetGamePaused(false);
            UIGameBoard.IN.Show();
        });
    }

    public void HandleSettingsButtonPress()
    {
        if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput || UITutorialPanel.IsTutorialMode) return;

        UISettingsPanel.IN.Show();
    }

    public void HandleHomeButtonPress()
    {
        ShowLeaveGameConfirmation();
    }

    public void ShowLeaveGameConfirmation()
    {
        if (UITutorialPanel.IsTutorialMode || this.isOnlineLeavePending) return;

        UIDraggable.CancelActiveDrag();

        string titleText = LOC.IN.LeaveGame[LOC.LI];// "Leave Game?";
        string messageText = this.IsOnline && this.onlineTurnRecorder != null
            && this.onlineTurnRecorder.Actions.Count > 0
            ? LocalizationManager.GetOnlineText(LOC.IN.OnlineDiscardUnsent)
            : LOC.IN.LeaveGameMessage[LOC.LI];//$"Are you sure you want to leave the game?\n\nAll progress will  be lost.";
        UIConfirmPanel.IN.transform.SetAsLastSibling();
        UIConfirmPanel.IN.Show(titleText, messageText, async () =>
        {
            string persistedMatchId = UIOnlineLobbyPanel.GetPersistedActiveMatchId();
            string matchId = this.onlineClient?.MatchId ?? persistedMatchId;
            bool shouldForfeitOnline = this.IsOnline
                || UIOnlineLobbyPanel.IN?.IsRecoveryPending == true
                || !string.IsNullOrEmpty(persistedMatchId);
            if (shouldForfeitOnline && !string.IsNullOrEmpty(matchId))
            {
                this.isOnlineLeavePending = true;
                UIAlertPanel.IN.Hide();
                UIAlertPanel.IN.Show(
                    LocalizationManager.GetOnlineText(LOC.IN.OnlineWorking), 0f,
                    UIAlertPanel.EAlertMode.Notification);
                EForfeitOutcome outcome = UIOnlineLobbyPanel.IN == null
                    ? EForfeitOutcome.Failed
                    : await UIOnlineLobbyPanel.IN.ForfeitMatchAsync(
                        matchId, this.onlineClient?.LocalSeat ?? -1);
                UIAlertPanel.IN.Hide();
                if (outcome == EForfeitOutcome.Failed)
                {
                    this.isOnlineLeavePending = false;
                    if (this.IsOnline && this.onlineClient?.CurrentState != null)
                        HandleOnlineStateChanged(this.onlineClient.CurrentState);
                    UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineError));
                    return;
                }

                UIOnlineLobbyPanel.IN?.ReleaseMatch();
                UIOnlineLobbyPanel.IN?.Hide();

                if (outcome == EForfeitOutcome.AlreadyOver
                    || outcome == EForfeitOutcome.Gone)
                {
                    string[] resultMessage = outcome == EForfeitOutcome.AlreadyOver
                        ? LOC.IN.OnlineMatchAlreadyEnded
                        : LOC.IN.OnlineMatchGone;
                    UIConfirmPanel.IN.ShowSingleButton(
                        LocalizationManager.GetOnlineText(LOC.IN.OnlineLobby),
                        LocalizationManager.GetOnlineText(resultMessage),
                        LOC.IN.HomeScreen[LOC.LI],
                        ReturnHomeFromGame);
                    return;
                }
            }

            ReturnHomeFromGame();
        });
    }

    private void HandleOnlineOpponentLeft()
    {
        if (!this.IsOnline || this.onlineClient == null || this.isOnlineLeavePending)
            return;

        SetGameInProgress(false);
        UIOnlineLobbyPanel.ForgetPersistedMatch(this.onlineClient.MatchId);
        ReleaseOnlineSession();
        UIConfirmPanel.IN.ShowSingleButton(
            LocalizationManager.GetOnlineText(LOC.IN.OnlineLobby),
            LocalizationManager.GetOnlineText(LOC.IN.OnlineOpponentLeft),
            LOC.IN.HomeScreen[LOC.LI],
            ReturnHomeFromGame);
    }

    private void ReturnHomeFromGame()
    {
        SetGameInProgress(false);
        CancelOnlineReplay();
        GameManager.IsApplyingRemoteUpdate = false;
        GameManager.IsDealingHand = false;
        GameManager.IsDoingActivateSequence = false;
        GameManager.IsDoingReplay = false;
        BoardStateManager.IsDoingReplay = false;
        BoardStateManager.IsSettingState = false;
        this.isHandoffPending = false;
        this.isOnlineMovePending = false;
        this.isOnlineLeavePending = false;
        this.isOnlineReplayActive = false;
        this.onlineTurnRecorder = null;
        this.onlineTurnStartState = null;
        UIDraggable.ResetInputState();
        CleanUpGameState();

        if (this.IsOnline)
            ReleaseOnlineSession();

        UISplashScreen.IN.Show();
        UIGameBoard.IN.Hide();
    }

    public void HandlePlayGameButtonPress()
    {
        UISplashScreen.IN.Hide();
        UIGameBoard.IN.Show();

        RestartGame();
    }

    public void HandlePlayModePlayButtonPress(int inPlayMode)
    {
        if (!TryGetPlayModeForButton(inPlayMode, out EPlayMode playMode))
        {
            Debug.LogWarning($"Unknown splash screen play mode: {inPlayMode}.");
            return;
        }

        UISettingsPanel.IN.SetPlayMode(playMode);
        PlayerData.Data.IsDirty = true;
        SaveManager.IN.SaveGame();

        UISplashScreen.IN.Hide();
        UIGameBoard.IN.Show();

        RestartGame();
    }

    public static bool TryGetPlayModeForButton(int inPlayMode, out EPlayMode outPlayMode)
    {
        switch (inPlayMode)
        {
            case 0:
                outPlayMode = EPlayMode.AgainstAI;
                return true;
            case 1:
                outPlayMode = EPlayMode.PassAndPlay;
                return true;
            case 3:
                outPlayMode = EPlayMode.Multiplayer;
                return true;
            default:
                outPlayMode = default;
                return false;
        }
    }

    public void HandleTutorialButtonPress()
    {
        UISplashScreen.IN.Hide();
        UIGameBoard.IN.Show();

        UITutorialPanel.IN.Show();

        SetGamePaused(false);

        RestartGame();
    }

    public void HandleStartAutoPlayButtonPress()
    {
        if (GameManager.IsDoingReplay) return;

        if (this.TurnPhase == ETurnPhase.PlayerDraw)
        {
            this.endTurnButton.SetActive(false);
        }
        else if (this.TurnPhase == ETurnPhase.PlayerPlaying)
        {
            this.endTurnButton.SetActive(false);

            StartCoroutine(OpponentsTurnCo(true, false));
        }
    }

    public void HandleStopAutoPlayButtonPress()
    {
        if (GameManager.IsDoingReplay) return;

        if (this.TurnPhase == ETurnPhase.PlayerDraw)
        {
            //TODO: stop coroutine, etc.
        }
        else if (this.TurnPhase == ETurnPhase.PlayerPlaying)
        {
            //this.endTurnButton.gameObject.SetActive(SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn);
            //this.discardToEndTurnZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);

            //TODO: stop coroutine, etc.
        }
    }

    public void HandleReplayButtonPress()
    {
        //Debug.Log($"HandleReplayButtonPress(this.CurrentTurnNumber = {this.CurrentTurnNumber})   BoardStateManager.IN.BoardTurns.Count = {BoardStateManager.IN.BoardTurns.Count}   BoardStateManager.IsDoingReplay = {BoardStateManager.IsDoingReplay}");

        if (BoardStateManager.IsDoingReplay || this.isOnlineReplayActive) return;

        if (this.IsOnline)
        {
            if (this.onlineReplayStartState != null && this.onlineReplayTurns.Count > 0)
                this.onlineReplayCoroutine = StartCoroutine(ReplayOnlineHistoryCo(
                    this.onlineReplayStartState.Clone(), new List<TurnPackageData>(this.onlineReplayTurns)));
            return;
        }

        if (BoardStateManager.IN == null
            || !TryCreateReplayPlan(BoardStateManager.IN.BoardTurns,
                this.CurrentTurnNumber, this.currentPlayerIndex, out ReplayPlan replayPlan))
            return;

        StopAllCoroutines();
        StartCoroutine(PlaybackCo(replayPlan));
    }

    private IEnumerator PlaybackCo(ReplayPlan inReplayPlan)
    {
        BoardState restoreState = CreateBoardState(false);
        ETurnPhase restoreTurnPhase = this.TurnPhase;
        bool wasReplayVisible = this.replayButton.activeSelf;
        bool wasDiscardVisible = this.DiscardZone.activeSelf;
        bool wasDiscardToEndTurnVisible = this.discardToEndTurnZone.activeSelf;
        Exception replayException = null;
        Exception restoreException = null;
        BoardStateManager.IsDoingReplay = true;
        try
        {
            yield return RunReplayStepCo(PlaybackStepsCo(inReplayPlan),
                inException => replayException = inException);
        }
        finally
        {
            try
            {
                BoardStateManager.IsDoingReplay = true;
                SetGameToBoardState(restoreState);
                this.TurnPhase = restoreTurnPhase;
                this.replayButton.SetActive(wasReplayVisible);
                this.DiscardZone.SetActive(wasDiscardVisible);
                this.discardToEndTurnZone.SetActive(wasDiscardToEndTurnVisible);
            }
            catch (Exception exception)
            {
                restoreException = exception;
            }
            finally
            {
                ReleaseReplayInputLocks();
            }
        }

        if (replayException != null)
            Debug.LogException(replayException);
        if (restoreException != null)
            Debug.LogException(restoreException);
    }

    private IEnumerator PlaybackStepsCo(ReplayPlan inReplayPlan)
    {
        ResetMovesRemaining();

        SetDiscardHighlightsVisible(false);

        if (inReplayPlan.ShouldReplayOpponent)
        {
            this.discardToEndTurnZone.SetActive(false);

            SetBoardZoomAnimState(EAnimState.Show);

            yield return new WaitForSeconds(1);

            ResetMovesRemaining(true);

            //replay opponent moves from last turn
            for (int i = 0; i < inReplayPlan.OpponentStates.Count; i++)
            {
                var state = inReplayPlan.OpponentStates[i];

                SetGameToBoardState(state);
                SetReplayMovePlayer(state);
                BoardStateManager.IN.CurrentReplayBoardState = state;
                yield return DoGameStateMoveCo(state);
            }

            SetBoardZoomAnimState(EAnimState.Hide);

            ResetMovesRemaining();

            yield return new WaitForSeconds(1f);
        }

        ResetMovesRemaining(true);

        this.currentPlayerIndex = inReplayPlan.ReplaySeat;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));

        //now do player moves this turn
        for (int i = 0; i < inReplayPlan.CurrentPlayerStates.Count; i++)
        {
            var state = inReplayPlan.CurrentPlayerStates[i];

            SetGameToBoardState(state);
            SetReplayMovePlayer(state);
            BoardStateManager.IN.CurrentReplayBoardState = state;
            yield return DoGameStateMoveCo(state);
        }
    }

    private static bool TryCreateReplayPlan(List<BoardTurn> inBoardTurns,
        int inCurrentTurnNumber, int inReplaySeat, out ReplayPlan outReplayPlan)
    {
        outReplayPlan = null;
        if (inBoardTurns == null)
            return false;

        int currentTurnIndex = inCurrentTurnNumber - 1;
        int opponentTurnIndex = currentTurnIndex - (inReplaySeat == 0 ? 1 : 0);
        EPlayerType replayPlayerType = (EPlayerType)(inReplaySeat + 1);
        List<BoardState> currentPlayerStates = GetReplayMoveStates(inBoardTurns,
            currentTurnIndex, replayPlayerType, false);
        List<BoardState> opponentStates = GetReplayMoveStates(inBoardTurns,
            opponentTurnIndex, replayPlayerType, true);
        if (opponentTurnIndex != currentTurnIndex)
            opponentStates.AddRange(GetReplayMoveStates(inBoardTurns,
                currentTurnIndex, replayPlayerType, true));
        bool shouldReplayOpponent = currentPlayerStates.Count == 0;
        if (shouldReplayOpponent && opponentStates.Count == 0)
            return false;

        outReplayPlan = new ReplayPlan
        {
            ReplaySeat = inReplaySeat,
            ShouldReplayOpponent = shouldReplayOpponent,
            OpponentStates = opponentStates,
            CurrentPlayerStates = currentPlayerStates
        };
        return true;
    }

    private static List<BoardState> GetReplayMoveStates(List<BoardTurn> inBoardTurns,
        int inTurnIndex, EPlayerType inReplayPlayerType, bool inOpponentTurn)
    {
        if (inTurnIndex < 0 || inTurnIndex >= inBoardTurns.Count
            || inBoardTurns[inTurnIndex]?.BoardStates == null)
            return new();

        return inBoardTurns[inTurnIndex].BoardStates.FindAll(inState =>
        {
            if (inState?.Move == null || inState.Move.ID < 0 || inState.Move.IsDealHand)
                return false;

            EPlayerType movePlayerType = GetReplayMovePlayerType(inState);
            return inOpponentTurn
                ? movePlayerType != inReplayPlayerType
                : movePlayerType == inReplayPlayerType;
        });
    }

    private static EPlayerType GetReplayMovePlayerType(BoardState inState)
    {
        int playerId = inState.Move?.Data?.PlayerId ?? 0;
        return playerId >= (int)EPlayerType.PlayerA && playerId <= (int)EPlayerType.PlayerD
            ? (EPlayerType)playerId
            : inState.ActivePlayer;
    }

    private void SetReplayMovePlayer(BoardState inState)
    {
        this.currentPlayerIndex = (int)GetReplayMovePlayerType(inState) - 1;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
    }

    private sealed class ReplayPlan
    {
        public int ReplaySeat;
        public bool ShouldReplayOpponent;
        public List<BoardState> OpponentStates = new();
        public List<BoardState> CurrentPlayerStates = new();
    }

    private void Update()
    {
        RefreshUndoControls();
        UpdateOpponentPresence();

        if (Input.GetKeyDown(KeyCode.F))
            ToggleFulScreen();
    }

    private void UpdateOpponentPresence()
    {
        if (!this.IsOnline || this.onlineClient?.CurrentState?.Status != "active"
            || this.opponentPresenceTracker == null)
            return;

        UIWaitingForOpponentPanel.IN?.SetOpponentAway(this.opponentPresenceTracker.IsAway);
        if (this.isOpponentAwayPromptShowing || UIConfirmPanel.IN == null
            || UIConfirmPanel.IN.IsShowing || !this.opponentPresenceTracker.TryPrompt())
            return;

        this.isOpponentAwayPromptShowing = true;
        UIConfirmPanel.IN.Show(
            LocalizationManager.GetOnlineText(LOC.IN.OnlineLobby),
            LocalizationManager.GetOnlineText(LOC.IN.OnlineOpponentAway),
            () =>
            {
                this.isOpponentAwayPromptShowing = false;
                ReturnHomeFromGame();
            },
            () =>
            {
                this.isOpponentAwayPromptShowing = false;
                this.opponentPresenceTracker?.KeepWaiting();
            },
            LocalizationManager.GetOnlineText(LOC.IN.OnlineKeepWaiting),
            LocalizationManager.GetOnlineText(LOC.IN.OnlineLeaveAndContinueLater));
    }

    public void ToggleFulScreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    public void SetGamePaused(bool inIsPaused)
    {
        if (inIsPaused)
            Time.timeScale = 0;
        else
            Time.timeScale = UISplashScreen.IN.IsShowing ? 1 : this.gameSpeed;
    }
}
