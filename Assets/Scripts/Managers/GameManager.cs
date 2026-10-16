using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Elemento.Domain.Net;
using DomainCardInstanceData = Elemento.Domain.CardInstanceData;
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
        || (IN != null && (IN.isHandoffPending || IN.IsOnlineInputBlocked));

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
        ? this.onlineClient?.CurrentView?.View != null && this.onlineClient.CurrentView.View.CurrentPlayerId == ((OnlineSession)this.gameSession).LocalSeat
        : this.gameSession == null ? this.currentPlayerIndex == 0 : (this.gameSession.IsHumanSeat(this.currentPlayerIndex) && !this.isHandoffPending);
    public bool IsPassAndPlay => this.gameSession != null && this.gameSession.GameMode == Elemento.Domain.EGameMode.PassAndPlay;
    public bool IsOnline => this.gameSession is OnlineSession;
    public bool IsGameInProgress { get; private set; }
    private bool IsOnlineInputBlocked => this.IsOnline && (this.isOnlineMovePending || this.isOnlineLeavePending || this.isOnlineReplayActive
        || this.onlineClient?.TurnBuffer.IsComplete == true
        || !this.IsPlayersTurn || !IsHumanPlayingPhase());
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
    private bool hasOnlineRenderedView;
    private bool skipOnlineReplay;
    private int onlineAppliedRevision = -1;
    private Coroutine onlineReplayCoroutine;
    private OnlineViewUpdateData pendingOnlineUpdate;
    private MatchSeatViewData onlineRenderedView;
    private MatchSeatViewData onlineReplayStartView;
    private readonly List<MoveLogEntryData> onlineReplayMoves = new();
    private int onlineReplayEndRevision = -1;
    private readonly Elemento.Domain.TurnUndoHistory<BoardState> undoHistory = new();

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

            if (!BoardStateManager.IsDoingReplay)
                StopAllCoroutines();

            foreach (var board in this.PlayerBoards)
            {
                board.Reset();
            }

            CardsManager.IN.DestroyAllCards(inShouldRecycleCards);

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
                    zone.CalculatePoints();
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

        RefreshTurnAndMoveCountDisplay();

        if (this.IsCurrentSeatControlledByAI)
            StartCoroutine(OpponentsTurnCo(true));
        else
            StartCoroutine(DealCardsCo(SettingsManager.IN.NumCardsToDeal, this.CurrentPlayerBoard.Hand));
    }

    //called by End Turn Button
    public void EndTurn()
    {
        bool isOnlineRetry = this.IsOnline && this.onlineClient?.TurnBuffer.IsComplete == true
            && !this.isOnlineMovePending;
        if (!isOnlineRetry && (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput)) return;
        if (UITutorialPanel.IsTutorialMode) return;
        if (!IsHumanPlayingPhase()) return;//ignore stale/double end-turn requests

        LockUndoTurn();

        if (this.IsOnline)
        {
            DomainMoveData move = isOnlineRetry
                ? this.onlineClient.TurnBuffer.Moves[this.onlineClient.TurnBuffer.Moves.Count - 1].Clone()
                : null;
            if (move != null
                || UnityDomainMapper.TryGetOnlineEndTurnMove(this.onlineClient.CurrentView.View, out move)
                || this.onlineClient.TurnBuffer.HasMoves && TryCreateOnlineEndMarker(out move))
                SubmitOnlineTurn(move);
            else
                RejectOnlineMove(LocalizationManager.GetOnlineText(LOC.IN.OnlineMoveIllegal));
            return;
        }

        SelectNextPlayer();
    }

    private bool TryCreateOnlineEndMarker(out DomainMoveData outMove)
    {
        outMove = null;
        if (!this.IsOnline || this.onlineClient?.CurrentView?.View?.Player == null)
            return false;
        outMove = new DomainMoveData
        {
            PlayerId = this.onlineClient.LocalSeat,
            Type = Elemento.Domain.EMoveType.EndTurn,
            SourceZoneId = -1,
            TargetPlayerId = -1,
            TargetZoneId = -1
        };
        return true;
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

        MeltFrozenZones();

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

        var cardType = ECardType.None;

        // Online replays also set IsDoingReplay but feed the type through PowerCardOverride; their
        // CurrentReplayBoardState is stale (an undo leaves it with Move == null).
        if (GameManager.IsDoingReplay && !this.IsOnline)
        {
            cardType = BoardStateManager.IN.CurrentReplayBoardState.Move.CardType;
        }

        if (CardsManager.IN.PowerCardOverride != ECardType.None)
            cardType = CardsManager.IN.PowerCardOverride;

        // Replays must not touch the turn buffer: Begin() throws off-turn, which kills this coroutine
        // with IsDealingHand still set and hangs PlayOnlineMoveCo. The replayed card's id is reconciled
        // by MaterializeOnlineReplayCard and the post-replay rebuild.
        string powerCardId = null;
        if (this.IsOnline)
            powerCardId = GameManager.IsDoingReplay ? "replay-" + Guid.NewGuid().ToString("N")
                : this.onlineClient?.GetNextBufferedCardId();
        var powerCard = CardsManager.IN.SpawnPowerCardOfType(cardType, powerCardId);

        if (powerCard == null) yield break;

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
                SaveBoardState(this.Deck, parentCell, false, inElementZone, false, powerCardType, powerCard.Id);

            AudioManager.IN.PlayClip(AudioManager.IN.UseActivationPowerClip, .5f);

            //TODO: play particle
        }
        else
            Debug.Log($"<color=red>PowerCard is null!</color>");

        UITutorialPanel.OnPowerCardDealt?.Invoke(powerCardType);

        GameManager.IsDealingHand = false;
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

                MeltFrozenZones();

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
                    MeltFrozenZones();
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
            this.onlineClient.ViewUpdateReceived -= HandleOnlineViewUpdate;

        this.onlineClient = null;
        if (SettingsManager.IN.PlayMode != EPlayMode.Multiplayer && OnlineMatchContext.IsActive)
            UIOnlineLobbyPanel.IN.ReleaseMatch();

        this.gameSession = SessionFactory.Create(SettingsManager.IN.PlayMode);
        this.onlineClient = (this.gameSession as OnlineSession)?.Client;
        if (this.onlineClient != null)
            this.onlineClient.ViewUpdateReceived += HandleOnlineViewUpdate;

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
            this.onlineClient.ViewUpdateReceived -= HandleOnlineViewUpdate;

        this.onlineClient = null;
        this.isOnlineMovePending = false;
        this.isOnlineLeavePending = false;
        this.isOnlineReplayActive = false;
        this.hasOnlineRenderedView = false;
        this.pendingOnlineUpdate = null;
        this.onlineAppliedRevision = -1;
        ClearOnlineReplayHistory();
        this.onlineRenderedView = null;
        UIOnlineLobbyPanel.IN.ReleaseMatch();
        this.gameSession = null;
        UIWaitingForOpponentPanel.IN?.Hide();
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
        this.hasOnlineRenderedView = false;
        this.skipOnlineReplay = false;
        this.pendingOnlineUpdate = null;
        ClearOnlineReplayHistory();
        this.onlineRenderedView = null;
        this.onlineAppliedRevision = LoadLastSeenOnlineRevision();
        MatchSeatViewData lastSeenView = LoadLastSeenOnlineView();

        if (this.onlineClient.Status == "abandoned")
        {
            HandleOnlineOpponentLeft();
            return;
        }

        if (lastSeenView?.View != null && lastSeenView.MatchId == this.onlineClient.MatchId
            && lastSeenView.Seat == this.onlineClient.LocalSeat
            && lastSeenView.View.Revision == this.onlineAppliedRevision
            && this.onlineClient.CurrentView?.View != null
            && this.onlineAppliedRevision < this.onlineClient.Revision)
        {
            HandleOnlineViewChanged(lastSeenView);
            GameManager.IsApplyingRemoteUpdate = true;
            _ = this.onlineClient.RefreshAsync(this.onlineAppliedRevision, null, inError =>
            {
                GameManager.IsApplyingRemoteUpdate = false;
                HandleOnlineViewChanged(this.onlineClient.CurrentView);
                Debug.LogWarning(inError.Message);
            });
        }
        else if (this.onlineClient.CurrentView != null
            && (this.onlineAppliedRevision < 0 || this.onlineAppliedRevision >= this.onlineClient.Revision))
        {
            HandleOnlineViewChanged(this.onlineClient.CurrentView);
        }
        else
        {
            int? sinceRevision = this.onlineAppliedRevision < 0 ? null : this.onlineAppliedRevision;
            _ = this.onlineClient.RefreshAsync(sinceRevision, null,
                inError => Debug.LogWarning(inError.Message));
        }
    }

    private void HandleOnlineViewUpdate(OnlineViewUpdateData inUpdate)
    {
        if (inUpdate?.Rules != null)
            SettingsManager.IN.ApplyMatchRules(inUpdate.Rules);
        if (!this.IsOnline || this.onlineClient == null || inUpdate?.View?.View == null)
            return;
        if (inUpdate.Status == "abandoned")
        {
            HandleOnlineOpponentLeft();
            return;
        }
        if (this.isOnlineReplayActive)
        {
            this.pendingOnlineUpdate = inUpdate;
            this.skipOnlineReplay = true;
            return;
        }
        if (this.onlineClient.TurnBuffer.HasMoves)
        {
            if (inUpdate.View.View.Revision <= this.onlineClient.TurnBuffer.ExpectedRevision)
                return;

            bool isCommittedRetry = HasOnlineTurnNonce(inUpdate.Moves,
                this.onlineClient.TurnBuffer.ClientNonce);
            this.onlineClient.DiscardBufferedTurn();
            this.isOnlineMovePending = false;
            if (!isCommittedRetry)
            {
                ClearOnlineReplayHistory();
                UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineTurnRejected));
            }
            HandleOnlineViewChanged(inUpdate.View);
            return;
        }

        if (this.hasOnlineRenderedView && this.onlineAppliedRevision >= 0
            && inUpdate.Moves != null && inUpdate.Moves.Count > 0
            && OnlineReconciliation.HasContinuousMoveLog(this.onlineAppliedRevision, inUpdate.Moves,
                inUpdate.View.View.Revision))
        {
            SetOnlineReplayHistory(this.onlineRenderedView, inUpdate.Moves,
                inUpdate.View.View.Revision);
            this.onlineReplayCoroutine = StartCoroutine(ReplayOnlineMovesCo(inUpdate));
            return;
        }

        HandleOnlineViewChanged(inUpdate.View);
    }

    private void ClearOnlineReplayHistory()
    {
        this.onlineReplayStartView = null;
        this.onlineReplayMoves.Clear();
        this.onlineReplayEndRevision = -1;
    }

    private void SetOnlineReplayHistory(MatchSeatViewData inStartView,
        IReadOnlyList<MoveLogEntryData> inMoves, int inEndRevision)
    {
        ClearOnlineReplayHistory();
        if (inStartView?.View == null || inMoves == null || inMoves.Count == 0)
            return;

        this.onlineReplayStartView = inStartView;
        foreach (var entry in inMoves)
        {
            if (entry != null)
                this.onlineReplayMoves.Add(entry);
        }
        this.onlineReplayEndRevision = inEndRevision;
    }

    private void SaveOnlineLocalReplay(DomainMoveData inEndTurnMove)
    {
        if (this.onlineClient?.CurrentView?.View == null)
            return;

        List<DomainMoveData> moves = new();
        foreach (var move in this.onlineClient.TurnBuffer.Moves)
            moves.Add(move);
        if (!this.onlineClient.TurnBuffer.IsComplete)
            moves.Add(inEndTurnMove);

        List<MoveLogEntryData> entries = OnlineReconciliation.BuildLocalMoveLog(
            this.onlineClient.MatchId, this.onlineClient.LocalSeat,
            this.onlineClient.TurnBuffer.ClientNonce,
            this.onlineClient.TurnBuffer.ExpectedRevision >= 0
                ? this.onlineClient.TurnBuffer.ExpectedRevision
                : this.onlineClient.CurrentView.View.Revision,
            moves);
        foreach (var entry in entries)
        {
            DomainCardInstanceData replayCard = FindOnlineReplayCard(
                this.onlineClient.CurrentView.View, entry.Move.CardId);
            entry.ReplayCard = replayCard?.Clone();
        }

        int endRevision = entries.Count > 0
            ? entries[entries.Count - 1].RevisionAfter
            : this.onlineClient.CurrentView.View.Revision;
        SetOnlineReplayHistory(this.onlineClient.CurrentView, entries, endRevision);
    }

    private List<MoveLogEntryData> GetOnlineReplayMoves(out MatchSeatViewData outStartView)
    {
        List<MoveLogEntryData> moves = new();
        outStartView = this.onlineClient?.CurrentView;
        if (outStartView?.View == null)
            return moves;

        int confirmedRevision = outStartView.View.Revision;
        if (this.onlineReplayStartView?.View != null
            && this.onlineReplayEndRevision == confirmedRevision)
        {
            outStartView = this.onlineReplayStartView;
            moves.AddRange(this.onlineReplayMoves);
        }

        if (this.onlineClient.TurnBuffer.HasMoves)
        {
            List<MoveLogEntryData> localMoves = OnlineReconciliation.BuildLocalMoveLog(
                this.onlineClient.MatchId, this.onlineClient.LocalSeat,
                this.onlineClient.TurnBuffer.ClientNonce,
                this.onlineClient.TurnBuffer.ExpectedRevision,
                this.onlineClient.TurnBuffer.Moves);
            foreach (var entry in localMoves)
            {
                DomainCardInstanceData replayCard = FindOnlineReplayCard(
                    this.onlineClient.CurrentView.View, entry.Move.CardId);
                entry.ReplayCard = replayCard?.Clone();
            }
            moves.AddRange(localMoves);
        }

        return moves;
    }

    private bool HasOnlineReplayMoves()
    {
        List<MoveLogEntryData> moves = GetOnlineReplayMoves(out _);
        foreach (var entry in moves)
        {
            if (entry?.Move != null && entry.Move.Type != Elemento.Domain.EMoveType.EndTurn
                && entry.Move.Type != Elemento.Domain.EMoveType.DrawCard)
                return true;
        }
        return false;
    }

    private static bool HasOnlineTurnNonce(List<MoveLogEntryData> inMoves, string inClientNonce)
    {
        if (inMoves == null || string.IsNullOrEmpty(inClientNonce))
            return false;
        foreach (var entry in inMoves)
        {
            if (entry != null && entry.ClientNonce == inClientNonce)
                return true;
        }
        return false;
    }

    private void HandleOnlineViewChanged(MatchSeatViewData inSeatView)
    {
        HandleOnlineViewChanged(inSeatView, false);
    }

    private void HandleOnlineViewChanged(MatchSeatViewData inSeatView, bool inIsReplayPreview)
    {
        if (!inIsReplayPreview && this.onlineClient?.Status == "abandoned")
        {
            HandleOnlineOpponentLeft();
            return;
        }
        if (!this.IsOnline || this.onlineClient == null || inSeatView?.View == null || inSeatView.MatchId != this.onlineClient.MatchId
            || inSeatView.Seat != this.onlineClient.LocalSeat || inSeatView.View.Player == null
            || inSeatView.View.Player.Hand == null || inSeatView.View.Player.Zones == null
            || inSeatView.View.Opponents == null)
            return;

        if (inSeatView.View.CurrentPlayerId < 0 || inSeatView.View.CurrentPlayerId >= this.PlayerBoards.Length)
            return;

        GameManager.IsApplyingRemoteUpdate = true;
        try
        {
            UIDraggable.CancelActiveDrag(false);
            this.currentPlayerIndex = inSeatView.View.CurrentPlayerId;
            this.CurrentTurnNumber = inSeatView.View.TurnNumber;
            this.MovesRemaining = inSeatView.View.MovesRemaining;
            this.TurnPhase = (ETurnPhase)inSeatView.View.TurnPhase;
            ApplyViewingSeat(this.onlineClient.LocalSeat);
            RenderOnlineView(inSeatView.View);
            if (!inIsReplayPreview && this.IsPlayersTurn && IsHumanPlayingPhase())
            {
                BeginUndoTurn(Elemento.Domain.ETurnUndoCheckpoint.Draw);
                this.lastCreatedBoardState = CreateBoardState(false);
            }
            else if (!inIsReplayPreview)
                LockUndoTurn();
            RefreshPassAndPlayHandVisibility();
            RefreshTurnAndMoveCountDisplay();
            if (!inIsReplayPreview)
            {
                this.onlineAppliedRevision = inSeatView.View.Revision;
                this.hasOnlineRenderedView = true;
                this.onlineRenderedView = inSeatView;
                this.onlineClient.MarkAppliedRevision(this.onlineAppliedRevision);
                SaveLastSeenOnlineRevision(this.onlineAppliedRevision);
                SaveLastSeenOnlineView(inSeatView);
            }
            RefreshOnlineControls();
        }
        finally
        {
            GameManager.IsDealingHand = false;
            GameManager.IsDoingActivateSequence = false;
            GameManager.IsDoingReplay = false;
            BoardStateManager.IsDoingReplay = false;
            BoardStateManager.IsSettingState = false;
            UIDraggable.ResetInputState();
            GameManager.IsApplyingRemoteUpdate = false;
        }
    }

    private IEnumerator ReplayOnlineMovesCo(OnlineViewUpdateData inUpdate)
    {
        List<string> visibleHandCardIds = new();
        int localSeat = this.onlineClient.LocalSeat;
        if (localSeat >= 0 && localSeat < this.PlayerBoards.Length)
        {
            foreach (var cell in this.PlayerBoards[localSeat].Hand.CardCells)
            {
                if (cell?.TopCard != null)
                    visibleHandCardIds.Add(cell.TopCard.Id);
            }
        }

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
            yield return PlayOnlineMoveLogCo(inUpdate.Moves, inUpdate.View.View);

            if (OnlineReconciliation.RequiresRebuild(this.onlineAppliedRevision, inUpdate, null))
            {
                var newHandCards = OnlineReconciliation.FindNewHandCards(visibleHandCardIds,
                    inUpdate.View.View);
                BoardStateManager.IsDoingReplay = true;
                try
                {
                    HandleOnlineViewChanged(inUpdate.View);
                }
                finally
                {
                    BoardStateManager.IsDoingReplay = false;
                }

                int cardsToDeal = StageOnlineHandCardsForDeal(localSeat, newHandCards);
                if (cardsToDeal > 0)
                {
                    GameManager.IsApplyingRemoteUpdate = true;
                    yield return DealCardsCo(cardsToDeal, this.PlayerBoards[localSeat].Hand, false);
                }
            }
        }
        finally
        {
            Move.ShouldRecordMoves = true;
            GameManager.IsDoingReplay = false;
            GameManager.IsApplyingRemoteUpdate = false;
            this.isOnlineReplayActive = false;
            this.skipOnlineReplay = false;
            this.onlineReplayCoroutine = null;
            Time.timeScale = this.gameSpeed;
            SetOnlineReplayButton(false);
            RefreshOnlineControls();
            UIDraggable.ResetInputState();
        }

        if (this.pendingOnlineUpdate != null)
        {
            var pending = this.pendingOnlineUpdate;
            this.pendingOnlineUpdate = null;
            HandleOnlineViewUpdate(pending);
        }
    }

    private IEnumerator PlayOnlineMoveLogCo(IReadOnlyList<MoveLogEntryData> inMoves,
        Elemento.Domain.PlayerViewData inFinalView)
    {
        if (inMoves == null)
            yield break;

        foreach (var entry in inMoves)
        {
            if (this.skipOnlineReplay)
                yield break;
            if (entry?.Move == null || entry.Move.Type == Elemento.Domain.EMoveType.EndTurn)
                continue;
            if (entry.Move.Type == Elemento.Domain.EMoveType.DrawCard)
            {
                yield return new WaitForSeconds(.2f);
                continue;
            }

            MaterializeOnlineReplayCard(entry.Move, entry.ReplayCard, inFinalView);
            if (!UnityDomainMapper.TryCreateOnlineReplayMove(entry.Move, this.PlayerBoards, out Move move))
                continue;
            UIPlayerBoard board = entry.Seat >= 0 && entry.Seat < this.PlayerBoards.Length
                ? this.PlayerBoards[entry.Seat] : null;
            if (board != null)
                yield return board.PlayOnlineMoveCo(move);
        }
    }

    private IEnumerator ReplayOnlineHistoryCo(MatchSeatViewData inStartView,
        IReadOnlyList<MoveLogEntryData> inMoves)
    {
        BoardState restoreState = CreateBoardState(false);
        ETurnPhase restoreTurnPhase = this.TurnPhase;
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
            BoardStateManager.IsDoingReplay = true;
            HandleOnlineViewChanged(inStartView, true);
            BoardStateManager.IsDoingReplay = true;
            GameManager.IsDoingReplay = true;
            GameManager.IsApplyingRemoteUpdate = true;
            yield return PlayOnlineMoveLogCo(inMoves, this.onlineClient.CurrentView.View);
        }
        finally
        {
            BoardStateManager.IsDoingReplay = true;
            SetGameToBoardState(restoreState);
            this.TurnPhase = restoreTurnPhase;
            this.lastCreatedBoardState = restoreState;
            BoardStateManager.IsDoingReplay = false;
            Move.ShouldRecordMoves = true;
            GameManager.IsDoingReplay = false;
            GameManager.IsApplyingRemoteUpdate = false;
            this.isOnlineReplayActive = false;
            this.skipOnlineReplay = false;
            this.onlineReplayCoroutine = null;
            Time.timeScale = this.gameSpeed;
            SetOnlineReplayButton(false);
            RefreshPassAndPlayHandVisibility();
            RefreshTurnAndMoveCountDisplay();
            RefreshOnlineControls();
            UIDraggable.ResetInputState();
        }

        if (this.pendingOnlineUpdate != null)
        {
            var pending = this.pendingOnlineUpdate;
            this.pendingOnlineUpdate = null;
            HandleOnlineViewUpdate(pending);
        }
    }

    private int StageOnlineHandCardsForDeal(int inSeat, IReadOnlyList<DomainCardInstanceData> inCards)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length || inCards == null || inCards.Count == 0)
            return 0;

        UIHand hand = this.PlayerBoards[inSeat].Hand;
        int stagedCardCount = 0;
        for (int i = inCards.Count - 1; i >= 0; --i)
        {
            foreach (var cell in hand.CardCells)
            {
                Card card = cell?.TopCard;
                if (card == null || card.Id != inCards[i].Id)
                    continue;

                cell.Cards.Remove(card);
                cell.gameObject.SetActive(false);
                this.Deck.Cards.Add(card);
                this.Deck.SetParentCell(card);
                CardsManager.IN.SetCardFaceDown(card);
                ++stagedCardCount;
                break;
            }
        }

        this.Deck.RefreshCellsList();
        hand.RefreshPlayerHandText();
        return stagedCardCount;
    }

    private void CancelOnlineReplay()
    {
        if (this.onlineReplayCoroutine != null)
            StopCoroutine(this.onlineReplayCoroutine);
        this.onlineReplayCoroutine = null;
        this.isOnlineReplayActive = false;
        this.skipOnlineReplay = false;
        Move.ShouldRecordMoves = true;
        GameManager.IsDoingReplay = false;
        BoardStateManager.IsDoingReplay = false;
        GameManager.IsApplyingRemoteUpdate = false;
        Time.timeScale = this.gameSpeed;
        if (this.speedUpOpponentTurnButton != null)
            SetOnlineReplayButton(false);
        UIDraggable.ResetInputState();
    }

    private void MaterializeOnlineReplayCard(DomainMoveData inMove, DomainCardInstanceData inReplayCard,
        Elemento.Domain.PlayerViewData inFinalView)
    {
        if (inMove == null || inMove.SourceZoneId >= 0 || string.IsNullOrEmpty(inMove.CardId)
            || inMove.PlayerId < 0 || inMove.PlayerId >= this.PlayerBoards.Length)
            return;

        UIPlayerBoard board = this.PlayerBoards[inMove.PlayerId];
        foreach (var cell in board.Hand.CardCells)
        {
            if (cell?.TopCard != null && cell.TopCard.Id == inMove.CardId)
                return;
        }

        DomainCardInstanceData cardData = inReplayCard?.Clone()
            ?? FindOnlineReplayCard(inFinalView, inMove.CardId);
        if (cardData == null)
            return;

        foreach (var cell in board.Hand.CardCells)
        {
            if (cell?.TopCard == null)
                continue;
            cell.TopCard.DestroySelf();
            cell.Cards.Clear();
            AddOnlineHandCard(board, board.Hand.CardCells.IndexOf(cell), cardData, false);
            return;
        }
    }

    private static DomainCardInstanceData FindOnlineReplayCard(Elemento.Domain.PlayerViewData inView,
        string inCardId)
    {
        if (inView?.Player != null)
        {
            if (inView.Player.Hand != null)
            {
                foreach (var handCard in inView.Player.Hand)
                {
                    if (handCard.Id == inCardId)
                        return handCard;
                }
            }
            var zoneCard = FindOnlineReplayCard(inView.Player.Zones, inCardId);
            if (zoneCard != null)
                return zoneCard;
        }
        if (inView?.Opponents != null)
        {
            foreach (var opponent in inView.Opponents)
            {
                var card = FindOnlineReplayCard(opponent.Zones, inCardId);
                if (card != null)
                    return card;
            }
        }
        return null;
    }

    private static DomainCardInstanceData FindOnlineReplayCard(List<DomainZoneStateData> inZones,
        string inCardId)
    {
        if (inZones == null)
            return null;
        foreach (var zone in inZones)
        {
            foreach (var card in zone.Cards)
            {
                if (card.Id == inCardId)
                    return card;
            }
        }
        return null;
    }

    private void SetOnlineReplayButton(bool inIsVisible)
    {
        this.speedUpOpponentTurnButton.SetActive(inIsVisible);
        if (!inIsVisible)
        {
            var speedLabel = this.speedUpOpponentTurnButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (speedLabel != null)
                speedLabel.text = LOC.IN.SpeedUp[LOC.LI];
            return;
        }

        var label = this.speedUpOpponentTurnButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = LocalizationManager.GetOnlineText(LOC.IN.OnlineSkipReplay);
    }

    private int LoadLastSeenOnlineRevision()
    {
        string key = GetOnlineRevisionKey();
        return string.IsNullOrEmpty(key) ? -1 : PlayerPrefs.GetInt(key, -1);
    }

    private void SaveLastSeenOnlineRevision(int inRevision)
    {
        string key = GetOnlineRevisionKey();
        if (string.IsNullOrEmpty(key))
            return;
        PlayerPrefs.SetInt(key, inRevision);
        PlayerPrefs.Save();
    }

    private MatchSeatViewData LoadLastSeenOnlineView()
    {
        string key = GetOnlineViewKey();
        if (string.IsNullOrEmpty(key) || !PlayerPrefs.HasKey(key))
            return null;
        try
        {
            return NetJson.Deserialize<MatchSeatViewData>(PlayerPrefs.GetString(key));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Stored online replay view was invalid: " + exception.Message);
            return null;
        }
    }

    private void SaveLastSeenOnlineView(MatchSeatViewData inView)
    {
        string key = GetOnlineViewKey();
        if (string.IsNullOrEmpty(key) || inView == null)
            return;
        PlayerPrefs.SetString(key, NetJson.Serialize(inView));
        PlayerPrefs.Save();
    }

    private string GetOnlineRevisionKey()
    {
        return this.onlineClient == null || string.IsNullOrEmpty(this.onlineClient.MatchId)
            ? null : "Elemento.Online.LastSeenRevision." + this.onlineClient.MatchId;
    }

    private string GetOnlineViewKey()
    {
        return this.onlineClient == null || string.IsNullOrEmpty(this.onlineClient.MatchId)
            ? null : "Elemento.Online.LastSeenView." + this.onlineClient.MatchId;
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

    private void RenderOnlineSeat(int inSeat, DomainPlayerStateData inPlayer, DomainOpponentViewData inOpponent)
    {
        if (inSeat < 0 || inSeat >= this.PlayerBoards.Length)
            return;

        var board = this.PlayerBoards[inSeat];
        board.Hand.RefreshCardCells();
        if (inPlayer != null)
        {
            for (int i = 0; i < inPlayer.Hand.Count && i < board.Hand.CardCells.Count; i++)
                AddOnlineHandCard(board, i, inPlayer.Hand[i], inSeat == this.onlineClient.LocalSeat);
            RenderOnlineZones(board, inPlayer.Zones, inSeat == this.onlineClient.LocalSeat);
        }
        else if (inOpponent != null)
        {
            for (int i = 0; i < inOpponent.HandCount && i < board.Hand.CardCells.Count; i++)
            {
                AddOnlineHandCard(board, i, new DomainCardInstanceData
                {
                    Id = $"hidden-{inSeat}-{i}",
                    IsFaceUp = false
                }, false);
            }
            RenderOnlineZones(board, inOpponent.Zones, false);
        }

        board.CalculateOnlineTotalPointsInstantly();
        board.Hand.RefreshPlayerHandText();
    }

    private void AddOnlineHandCard(UIPlayerBoard inBoard, int inCellIndex, DomainCardInstanceData inCard, bool inIsDraggable)
    {
        var cell = inBoard.Hand.CardCells[inCellIndex];
        var card = SpawnOnlineCard(inCard, cell.CardHolder, inIsDraggable);
        if (card == null)
            return;

        cell.Cards.Clear();
        cell.AddCardToCell(card);
        cell.gameObject.SetActive(true);
    }

    private void RenderOnlineZones(UIPlayerBoard inBoard, List<DomainZoneStateData> inZones, bool inIsDraggable)
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

    private Card SpawnOnlineCard(DomainCardInstanceData inCard, Transform inParent, bool inIsDraggable)
    {
        if (inCard == null)
            return null;

        CardData cardData = FindOnlineCardData(inCard);
        if (cardData == null)
        {
            Debug.LogWarning($"Online view contains unknown card definition {inCard.DefinitionId}.");
            return null;
        }

        return CardsManager.SpawnCard(cardData, inParent, !inCard.IsFaceUp, inIsDraggable, false, inCard.Id);
    }

    private CardData FindOnlineCardData(DomainCardInstanceData inCard)
    {
        var definitions = inCard.IsPowerCard ? CardsManager.IN.SinglePowerCardDatas : CardsManager.IN.AllCardDatas;
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

        result.Name = string.IsNullOrEmpty(inCard.DefinitionName) ? result.Name : inCard.DefinitionName;
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

        var view = this.onlineClient?.CurrentView?.View;
        if (view == null)
        {
            this.beginTurnButton.SetActive(false);
            this.endTurnButton.SetActive(false);
            this.discardToEndTurnZone.SetActive(false);
            this.DiscardZone.SetActive(false);
            this.speedUpOpponentTurnButton.SetActive(false);
            SetReplayButtonVisibility(false);
            return;
        }

        bool hasCompleteRetry = this.onlineClient.TurnBuffer.IsComplete;
        bool canMove = this.IsPlayersTurn && IsHumanPlayingPhase() && !this.isOnlineMovePending
            && !hasCompleteRetry;
        bool canEndTurn = !this.isOnlineMovePending && this.IsPlayersTurn && IsHumanPlayingPhase()
            && (hasCompleteRetry || UnityDomainMapper.TryGetOnlineEndTurnMove(view, out _));
        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(canEndTurn);
        this.discardToEndTurnZone.SetActive(canMove && SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);
        this.DiscardZone.SetActive(canMove && SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);
        SetReplayButtonVisibility(this.IsPlayersTurn && !this.isOnlineMovePending
            && !this.isOnlineReplayActive && HasOnlineReplayMoves());
        SetOnlineReplayButton(this.isOnlineReplayActive);
        if (this.isOnlineMovePending)
            this.playerTurnText.text = LocalizationManager.GetOnlineText(LOC.IN.OnlineSendingTurn);
        RefreshUndoControls();
    }

    private void RefreshWaitingForOpponentPanel()
    {
        UIWaitingForOpponentPanel panel = UIWaitingForOpponentPanel.IN;
        if (panel == null)
            return;

        bool shouldShow = this.IsOnline && this.IsGameInProgress
            && this.onlineClient?.CurrentView?.View != null
            && !this.IsPlayersTurn && !this.isOnlineReplayActive;

        if (shouldShow && !panel.IsShowing)
            panel.Show();
        else if (!shouldShow && panel.IsShowing)
            panel.Hide();
    }

    private void SubmitOnlineTurn(DomainMoveData inMove)
    {
        if (!this.IsOnline || this.onlineClient == null || inMove == null || this.isOnlineMovePending)
            return;

        SaveOnlineLocalReplay(inMove);
        this.isOnlineMovePending = true;
        RefreshOnlineControls();
        _ = this.onlineClient.SubmitTurnAsync(inMove, HandleOnlineTurnCompleted, HandleOnlineTurnFailed);
    }

    private void HandleOnlineTurnCompleted(SubmitTurnResultData inResult)
    {
        this.isOnlineMovePending = false;
        if (!this.IsOnline || this.onlineClient == null)
            return;

        RefreshTurnAndMoveCountDisplay();

        if (inResult == null || !inResult.Accepted)
        {
            ClearOnlineReplayHistory();
            UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineTurnRejected));
            if (this.onlineClient.CurrentView != null)
                HandleOnlineViewChanged(this.onlineClient.CurrentView);
            else
                _ = this.onlineClient.RefreshAsync(null, HandleOnlineViewChanged,
                    inError => Debug.LogWarning("Online resync failed: " + inError.Message));
            return;
        }

        RefreshOnlineControls();
    }

    private void HandleOnlineTurnFailed(Exception inException)
    {
        this.isOnlineMovePending = false;
        if (!this.IsOnline || this.onlineClient == null)
            return;

        Debug.LogWarning("Online turn submission failed: " + inException?.Message);
        UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineTurnRetry));
        RefreshTurnAndMoveCountDisplay();
        RefreshOnlineControls();
    }

    private void RejectOnlineMove(string inReason)
    {
        UIAlertPanel.IN.Show(inReason);
        this.onlineClient?.DiscardBufferedTurn();
        if (this.onlineClient?.CurrentView != null)
            HandleOnlineViewChanged(this.onlineClient.CurrentView);
    }

    private void OnDestroy()
    {
        if (this.onlineClient != null)
            this.onlineClient.ViewUpdateReceived -= HandleOnlineViewUpdate;
    }

    private void OnApplicationFocus(bool inHasFocus)
    {
        if (!inHasFocus || !this.IsOnline || this.onlineClient == null || this.isOnlineMovePending)
            return;
        int? sinceRevision = this.onlineAppliedRevision < 0 ? null : this.onlineAppliedRevision;
        _ = this.onlineClient.RefreshAsync(sinceRevision, null,
            inError => Debug.LogWarning("Online resume refresh failed: " + inError.Message));
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

        SetGameInProgress(false);
        BoardStateManager.IsDoingReplay = false;

        StopAllCoroutines();
        foreach (var board in this.PlayerBoards)
        {
            board.StopMovesCoroutine();
        }

        var wrapupItems = new List<WrapupItemData>();
        var playerPoints = new List<int>();

        this.PlayerBoard.CalculateTotalPoints(false);

        foreach (var zone in this.PlayerBoard.ElementZones)
        {
            int points = zone.IsDestroyed ? -1 : zone.Points;
            playerPoints.Add(points);
        }

        var playerData = new WrapupItemData
        {
            PlayerName = LOC.IN.You[LOC.LI],//"You"
            TotalPoints = this.PlayerBoard.TotalPoints,
            ElementPoints = playerPoints
        };

        wrapupItems.Add(playerData);

        var opponentBoard = this.PlayerBoards[1];

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
            var view = this.onlineClient?.CurrentView?.View;
            if (view == null)
            {
                RejectOnlineMove("The current server view is unavailable.");
                return;
            }

            recordedMove = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inActivatedZone,
                inIsDealHand, inCardType, (int)this.CurrentPlayer, inCardId);
            if (UnityDomainMapper.TryCreateOnlineMove(recordedMove, view, out DomainMoveData move)
                && this.onlineClient.TryBufferMove(move, out _))
                RefreshOnlineControls();
            else
            {
                RejectOnlineMove(LocalizationManager.GetOnlineText(LOC.IN.OnlineMoveIllegal));
                return;
            }
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
                Hand = new BoardHand()
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

        ValidateBoardStateIds(boardState);
        return boardState;
    }

    // Catches a duplicate at capture time; the stack trace names the caller that recorded it.
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private static void ValidateBoardStateIds(BoardState inBoardState)
    {
        Dictionary<string, string> locations = new();
        CheckIds(inBoardState.Deck.CardIds, "Deck");
        for (int i = 0; i < inBoardState.Players.Count; i++)
        {
            var player = inBoardState.Players[i];
            CheckIds(player.Hand.CardIds, $"P{i} hand");
            for (int k = 0; k < player.ElementZones.Length; k++)
                CheckIds(player.ElementZones[k].CardIds, $"P{i} zone {k}");
        }

        void CheckIds(List<string> inIds, string inLocation)
        {
            foreach (string id in inIds)
            {
                if (locations.TryGetValue(id, out string firstLocation))
                    Debug.LogError($"Board snapshot has card id '{id}' in {firstLocation} and {inLocation}.");
                else
                    locations[id] = inLocation;
            }
        }
    }

    public void SetGameToBoardState(BoardState inBoardState, bool inShouldPlay = false)
    {
        BoardStateManager.IsSettingState = true;
        BoardStateManager.IN.CurrentReplayBoardState = inBoardState;

        CleanUpGameState(false);

        SetHealthOnAllElementZones();

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
            CardsManager.IN.SetOnlineDeckCount(this.onlineClient.CurrentView.View.DeckCount);
        else
            RestoreDeckState(inBoardState.Deck);

        for (int i = 0; i < inBoardState.Players.Count; i++)
        {
            var player = inBoardState.Players[i];
            var playerBoard = this.PlayerBoards[i];

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

                zone.SetMaxHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);
                zone.SetZoneHealth(zoneData.Health);

                zone.SetFrozenHealth(zoneData.FrozenHealth, false);

                if (!zone.IsDestroyed)
                {
                    zone.SetActiveState(zoneData.IsActivated, false);

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

                    zone.CalculatePoints();
                }
            }

            playerBoard.CalculateTotalPointsInstantly();
        }

        this.PlayerBoard.Hand.RefreshPlayerHandText();

        BoardStateManager.IsSettingState = false;

        //if (inBoardState.ActivePlayer == this.CurrentPlayer)
        //    SetTurnPhase(ETurnPhase.PlayerPlaying);
        //else
        //    SetTurnPhase(ETurnPhase.OpponentPlaying);

        //if (inBoardState.ActivePlayer == this.CurrentPlayer)
        //    SetTurnPhase(ETurnPhase.PlayerPlaying);
        //else
        //    this.TurnPhase = ETurnPhase.OpponentPlaying;

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

        if (this.IsOnline && !this.onlineClient.TryUndoBufferedMove(out _, out string reason))
        {
            this.undoHistory.Record(boardState);
            RejectOnlineMove(reason);
            RefreshUndoControls();
            return;
        }

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

        //Debug.Log($"<color=yellow>GameManager.IsDoingReplay = { GameManager.IsDoingReplay}</color>");

        var move = inBoardState.Move;
        var playerIndex = (int)inBoardState.ActivePlayer - 1;
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

            move.ActivatePowerZone.UseActivatePower();

            while (GameManager.IsDoingActivateSequence || GameManager.IsDealingHand)
            {
                yield return new WaitForSeconds(.3f);
            }
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

            if (move.SourceCellIndex > -1)
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

            var animTarget = move.TargetCell.AnimTarget != null ? move.TargetCell.AnimTarget : move.TargetCell.CardHolder;//need when CardHolder pivot changed from .5 to 1
            var targetObj = move.TargetCell.TopCard != null ? move.TargetCell.TopCard.Center : animTarget;
            var destPosition = move.IsDiscard ? discardPile.position : targetObj.position;
            var delayAtTweenEnd = move.IsDiscard ? .35f : 0;

            move.SourceCell.TopCard.Draggable.AnimateTo(destPosition, tweenDuration, delayAtTweenEnd);
            move.SourceCell.TopCard.transform.DORotate(Vector3.zero, tweenDuration);

            if (move.IsDiscard)
            {
                //move.SourceCell.TopCard.transform.DOScale(Vector3.one * .4f, tweenDuration);
                move.SourceCell.TopCard.transform.DOScale(Vector3.one * .001f, tweenDuration).SetDelay(tweenDuration + .1f);
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
                GameManager.IsDoingReplay = false;
                yield break;
            }

            if (move.CardType == ECardType.Bomb)
            {
                yield return new WaitForSeconds(1f);

                //TODO: if UI is going to display bomb cards, have a while loop until dismissed
            }
        }

        yield return new WaitForSeconds(.5f);

        Move.ShouldRecordMoves = true;
        GameManager.IsDoingReplay = false;
        BoardStateManager.IN.CurrentReplayBoardState = null;
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

        this.movesRemainingPoints.SetText(this.MovesRemaining);

        //Debug.Log($"SetMovesRemaining({this.MovesRemaining})");

        if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
        {
            if (this.IsPlayersTurn && this.MovesRemaining < 1 && !this.IsCurrentSeatControlledByAI)
                SetDiscardHighlightsVisible(true);
        }
        else if (this.MovesRemaining < 1 && this.IsPlayersTurn)
            Invoke(nameof(EndTurn), .3f);
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
        if (UITutorialPanel.IsTutorialMode || this.isOnlineLeavePending) return;

        UIDraggable.CancelActiveDrag();

        string titleText = LOC.IN.LeaveGame[LOC.LI];// "Leave Game?";
        string messageText = this.IsOnline && this.onlineClient?.TurnBuffer.HasMoves == true
            ? LocalizationManager.GetOnlineText(LOC.IN.OnlineDiscardUnsent)
            : LOC.IN.LeaveGameMessage[LOC.LI];//$"Are you sure you want to leave the game?\n\nAll progress will  be lost.";
        UIConfirmPanel.IN.Show(titleText, messageText, async () =>
        {
            if (this.IsOnline && this.onlineClient != null)
            {
                this.isOnlineLeavePending = true;
                try
                {
                    await this.onlineClient.LeaveMatchAsync();
                }
                catch (Exception exception)
                {
                    if (this.onlineClient?.Status != "abandoned")
                    {
                        this.isOnlineLeavePending = false;
                        Debug.LogWarning("Leaving online match failed: " + exception.Message);
                        UIAlertPanel.IN.Show(LocalizationManager.GetOnlineText(LOC.IN.OnlineError));
                        return;
                    }
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
        this.onlineClient?.DiscardBufferedTurn();
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
            List<MoveLogEntryData> moves = GetOnlineReplayMoves(out MatchSeatViewData startView);
            if (startView?.View != null && moves.Count > 0)
                this.onlineReplayCoroutine = StartCoroutine(ReplayOnlineHistoryCo(startView, moves));
            return;
        }

        if (BoardStateManager.IN.BoardTurns.Count > 0
            && BoardStateManager.IN.BoardHistory.BoardStates.Count > 0)
        {
            StopAllCoroutines();

            StartCoroutine(PlaybackCo());
        }
    }

    private IEnumerator PlaybackCo()
    {
        BoardStateManager.IsDoingReplay = true;

        var replaySeat = this.currentPlayerIndex;
        var replayPlayerType = (EPlayerType)(replaySeat + 1);

        var turnIndex = this.CurrentTurnNumber - 2;

        var numCurrentTurnPlayerMoves = 0;

        ResetMovesRemaining();

        SetDiscardHighlightsVisible(false);

        //check if player has moved this turn. If so, skip opponent's moves
        if (this.CurrentTurnNumber > 0
            && this.CurrentTurnNumber <= BoardStateManager.IN.BoardTurns.Count)
        {
            var nextTurnStatesList = BoardStateManager.IN.BoardTurns[this.CurrentTurnNumber - 1];
            numCurrentTurnPlayerMoves = nextTurnStatesList.BoardStates.Count;
        }

        BoardTurn statesList = null;

        if (turnIndex >= 0 && turnIndex < BoardStateManager.IN.BoardTurns.Count && numCurrentTurnPlayerMoves <= 1)
        {
            this.discardToEndTurnZone.SetActive(false);

            SetBoardZoomAnimState(EAnimState.Show);

            yield return new WaitForSeconds(1);

            ResetMovesRemaining(true);

            MeltFrozenZones();

            //replay opponent moves from last turn
            statesList = BoardStateManager.IN.BoardTurns[turnIndex];
            for (int i = 0; i < statesList.BoardStates.Count; i++)
            {
                var state = statesList.BoardStates[i];
                var isOpponent = state.ActivePlayer != replayPlayerType;

                if (isOpponent)
                {
                    //Debug.Log($"{i}/{statesList.BoardStates.Count}  OPPONENT");

                    SetGameToBoardState(state, true);

                    while (GameManager.IsDoingReplay)
                    {
                        yield return new WaitForSeconds(.1f);
                    }

                    if (state.Move != null && state.Move.IsDealHand)
                        yield return new WaitForSeconds(.2f);
                }
            }

            SetBoardZoomAnimState(EAnimState.Hide);

            ResetMovesRemaining();

            yield return new WaitForSeconds(1f);
        }

        ++turnIndex;

        ResetMovesRemaining(true);

        this.currentPlayerIndex = replaySeat;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));

        MeltFrozenZones();

        //now do player moves this turn
        if (turnIndex >= 0 && turnIndex < BoardStateManager.IN.BoardTurns.Count)
        {
            statesList = BoardStateManager.IN.BoardTurns[turnIndex];
            for (int i = 0; i < statesList.BoardStates.Count; i++)
            {
                var state = statesList.BoardStates[i];

                if (state.Move != null && state.Move.ID > -1)
                {
                    //Debug.Log($"{i}/{statesList.BoardStates.Count}  PLAYER");

                    SetGameToBoardState(state, true);

                    while (GameManager.IsDoingReplay)
                    {
                        yield return new WaitForSeconds(.1f);
                    }
                }
            }
        }
        else
            Debug.Log($"<color=red>PlaybackCo() turnIndex = {turnIndex} is greater than BoardStateManager.IN.BoardTurns.Count = {BoardStateManager.IN.BoardTurns.Count}</color>");

        //return board state to most recent board state
        var lastStateIndex = BoardStateManager.IN.BoardHistory.BoardStates.Count - 1;
        var lastState = BoardStateManager.IN.BoardHistory.BoardStates[lastStateIndex];
        SetGameToBoardState(lastState);

        ResetMovesRemaining(true);
        this.discardToEndTurnZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);

        BoardStateManager.IsDoingReplay = false;
    }

    private void Update()
    {
        RefreshUndoControls();

        if (Input.GetKeyDown(KeyCode.F))
            ToggleFulScreen();
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
