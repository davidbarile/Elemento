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
    private bool IsOnlineInputBlocked => this.IsOnline && (this.isOnlineMovePending || !this.IsPlayersTurn || !IsHumanPlayingPhase());
    public bool IsCurrentSeatControlledByAI => !this.IsOnline && !this.IsPassAndPlay && ((this.gameSession != null && this.gameSession.ShouldRunAI(this.currentPlayerIndex)) || this.CurrentPlayerBoard.IsAutoPlay);

    public bool CanControlPlayer(EPlayerType inPlayerType)
    {
        if (this.IsOnline)
            return this.IsPlayersTurn && (int)inPlayerType - 1 == ((OnlineSession)this.gameSession).LocalSeat;

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
            StartOnlineSession();
            return;
        }

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

    public void CleanUpGameState()
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

            CardsManager.IN.DestroyAllCards();

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
        if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput || UITutorialPanel.IsTutorialMode) return;
        if (!IsHumanPlayingPhase()) return;//ignore stale/double end-turn requests

        if (this.IsOnline)
        {
            if (UnityDomainMapper.TryGetOnlineEndTurnMove(this.onlineClient.CurrentView.View, out DomainMoveData move))
                SubmitOnlineMove(move);
            else
                RejectOnlineMove("End turn is not legal in the current server view.");
            return;
        }

        SelectNextPlayer();
    }

    public void HandleSpeedUpOpponentTurnButtonPress()
    {
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

    private IEnumerator DealCardsCo(int inCount, UIHand inHand)
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

            if (this.Deck.Cards.Count <= 2)
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

        ResetMovesRemaining(true);

        SaveBoardState(null, null, false, null, true);

        GameManager.IsDealingHand = false;

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

        if (GameManager.IsDoingReplay)
        {
            cardType = BoardStateManager.IN.CurrentReplayBoardState.Move.CardType;
        }

        if (CardsManager.IN.PowerCardOverride != ECardType.None)
            cardType = CardsManager.IN.PowerCardOverride;

        var powerCard = CardsManager.IN.SpawnPowerCardOfType(cardType);

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
    }

    private void ConfigureSession()
    {
        if (this.onlineClient != null)
            this.onlineClient.ViewChanged -= HandleOnlineViewChanged;

        this.onlineClient = null;
        if (SettingsManager.IN.PlayMode != EPlayMode.Multiplayer && OnlineMatchContext.IsActive)
            UIOnlineLobbyPanel.IN.ReleaseMatch();

        this.gameSession = SessionFactory.Create(SettingsManager.IN.PlayMode);
        this.onlineClient = (this.gameSession as OnlineSession)?.Client;
        if (this.onlineClient != null)
            this.onlineClient.ViewChanged += HandleOnlineViewChanged;

        if (this.boardSlots == null || this.boardSlots.Length != this.PlayerBoards.Length)
        {
            this.boardSlots = (UIPlayerBoard[])this.PlayerBoards.Clone();
            this.seatViewBinder = new SeatViewBinder(this.PlayerBoards.Length);
        }

        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));
    }

    private void ReleaseOnlineSession()
    {
        if (this.onlineClient != null)
            this.onlineClient.ViewChanged -= HandleOnlineViewChanged;

        this.onlineClient = null;
        this.isOnlineMovePending = false;
        UIOnlineLobbyPanel.IN.ReleaseMatch();
        this.gameSession = null;
    }

    private void StartOnlineSession()
    {
        StopAllCoroutines();
        CancelInvoke();
        CleanUpGameState();
        UISeatHandoffPanel.IN.Hide();
        this.isHandoffPending = false;
        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(false);
        this.discardToEndTurnZone.SetActive(false);
        this.speedUpOpponentTurnButton.SetActive(false);
        SetReplayButtonVisibility(false);
        this.TurnPhase = ETurnPhase.None;
        this.isOnlineMovePending = false;

        if (this.onlineClient.CurrentView != null)
            HandleOnlineViewChanged(this.onlineClient.CurrentView);
        else
            _ = this.onlineClient.RefreshAsync(null, inError => Debug.LogWarning(inError.Message));
    }

    private void HandleOnlineViewChanged(MatchSeatViewData inSeatView)
    {
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
            RefreshPassAndPlayHandVisibility();
            RefreshTurnAndMoveCountDisplay();
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

        board.CalculateOnlineTotalPoints();
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
        var view = this.onlineClient?.CurrentView?.View;
        if (view == null)
        {
            this.beginTurnButton.SetActive(false);
            this.endTurnButton.SetActive(false);
            this.discardToEndTurnZone.SetActive(false);
            this.DiscardZone.SetActive(false);
            return;
        }

        bool canMove = this.IsPlayersTurn && IsHumanPlayingPhase() && !this.isOnlineMovePending;
        bool canEndTurn = canMove && UnityDomainMapper.TryGetOnlineEndTurnMove(view, out _);
        this.beginTurnButton.SetActive(false);
        this.endTurnButton.SetActive(canEndTurn);
        this.discardToEndTurnZone.SetActive(canMove && SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn);
        this.DiscardZone.SetActive(canMove && SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);
    }

    private void SubmitOnlineMove(DomainMoveData inMove)
    {
        if (!this.IsOnline || this.onlineClient == null || inMove == null || this.isOnlineMovePending)
            return;

        this.isOnlineMovePending = true;
        RefreshOnlineControls();
        _ = this.onlineClient.SubmitMoveAsync(inMove, HandleOnlineMoveCompleted, HandleOnlineMoveFailed);
    }

    private void HandleOnlineMoveCompleted(MoveResultData inResult)
    {
        this.isOnlineMovePending = false;
        if (!this.IsOnline || this.onlineClient == null)
            return;

        if (inResult == null || !inResult.Accepted)
        {
            string reason = inResult == null || string.IsNullOrEmpty(inResult.Reason)
                ? "The server rejected the move."
                : inResult.Reason;
            UIAlertPanel.IN.Show(reason);
            HandleOnlineViewChanged(this.onlineClient.CurrentView);
            return;
        }

        RefreshOnlineControls();
    }

    private void HandleOnlineMoveFailed(Exception inException)
    {
        this.isOnlineMovePending = false;
        if (!this.IsOnline || this.onlineClient == null)
            return;

        UIAlertPanel.IN.Show(inException?.Message ?? "The online move failed.");
        _ = this.onlineClient.RefreshAsync(HandleOnlineViewChanged,
            inError => Debug.LogWarning("Online resync failed: " + inError.Message));
        RefreshOnlineControls();
    }

    private void RejectOnlineMove(string inReason)
    {
        UIAlertPanel.IN.Show(inReason);
        if (this.onlineClient?.CurrentView != null)
            HandleOnlineViewChanged(this.onlineClient.CurrentView);
    }

    private void OnDestroy()
    {
        if (this.onlineClient != null)
            this.onlineClient.ViewChanged -= HandleOnlineViewChanged;
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
        if (this.IsOnline)
        {
            var view = this.onlineClient?.CurrentView?.View;
            if (view == null)
            {
                RejectOnlineMove("The current server view is unavailable.");
                return;
            }

            var unityMove = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inActivatedZone,
                inIsDealHand, inCardType, (int)this.CurrentPlayer, inCardId);
            if (UnityDomainMapper.TryCreateOnlineMove(unityMove, view, out DomainMoveData move))
                SubmitOnlineMove(move);
            else
                RejectOnlineMove("The move is not legal in the current server view.");
            return;
        }

        if (!Move.ShouldRecordMoves || BoardStateManager.IsDoingReplay || UITutorialPanel.IsTutorialMode) return;

        var boardState = new BoardState
        {
            TurnNumber = this.CurrentTurnNumber,
            MoveNumber = this.CurrentPlayerBoard.CurrentMoveNumber,
            TotalMovesTaken = this.CurrentPlayerBoard.TotalMovesTaken,
            ActivePlayer = this.CurrentPlayer,
            IsDealHand = inIsDealHand
            //Move = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inIsUseActivatedZone)
        };

        if (this.lastCreatedBoardState != null)
        {
            this.lastCreatedBoardState.Move = Move.CreateMove(inSourceCell, inTargetCell, inIsDiscard, inActivatedZone, inIsDealHand, inCardType, (int)this.CurrentPlayer, inCardId);
        }

        this.lastCreatedBoardState = boardState;

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
                    playerData.Hand.Cards.Add(topCard.Data);
                //else
                //    playerData.Hand.Cards.Add(null);
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
                    var card = zone.CardCell.Cards[l].Data;
                    //var clone = CardData.Clone(cardData);
                    dataZone.Cards.Add(card);
                }
            }

            boardState.Players.Add(playerData);
        }

        BoardStateManager.IN.AddBoardState(boardState);
    }
    public void SetGameToBoardState(BoardState inBoardState, bool inShouldPlay = false)
    {
        BoardStateManager.IsSettingState = true;
        BoardStateManager.IN.CurrentReplayBoardState = inBoardState;

        CleanUpGameState();

        SetHealthOnAllElementZones();

        this.CurrentTurnNumber = inBoardState.TurnNumber;
        this.currentPlayerIndex = (int)inBoardState.ActivePlayer - 1;
        ApplyViewingSeat(this.gameSession.GetViewingSeat(this.currentPlayerIndex));

        this.CurrentPlayerBoard.CurrentMoveNumber = inBoardState.MoveNumber;
        this.CurrentPlayerBoard.TotalMovesTaken = inBoardState.TotalMovesTaken;

        RefreshTurnAndMoveCountDisplay();

        this.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);

        CardsManager.IN.RebuildActiveDeck();
        CardsManager.IN.GenerateShuffledGameDeck();
        CardsManager.IN.AddBatchOfCardsToGameDeck();

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

                var handCard = CardsManager.SpawnCard(cardData, cell.CardHolder, false);
                cell.Cards.Clear();
                cell.AddCardToCell(handCard);
                cell.gameObject.SetActive(true);
            }

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

                        var zoneCard = CardsManager.SpawnCard(cardData, zone.CardCell.CardHolder, false);

                        zone.CardCell.AddCardToCell(zoneCard);
                        zoneCard.SetDimmedDecorations(zone.CardCell.ElementType);
                    }

                    zone.CalculatePoints();
                }
            }

            playerBoard.CalculateTotalPoints();
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
        if (UITutorialPanel.IsTutorialMode) return;

        UIDraggable.CancelActiveDrag();
        GameManager.IsApplyingRemoteUpdate = false;
        BoardStateManager.IsSettingState = false;

        string titleText = LOC.IN.LeaveGame[LOC.LI];// "Leave Game?";
        string messageText = LOC.IN.LeaveGameMessage[LOC.LI];//$"Are you sure you want to leave the game?\n\nAll progress will  be lost.";
        UIConfirmPanel.IN.Show(titleText, messageText, () =>
        {
            GameManager.IsDealingHand = false;
            GameManager.IsDoingActivateSequence = false;
            GameManager.IsDoingReplay = false;
            BoardStateManager.IsDoingReplay = false;
            BoardStateManager.IsSettingState = false;
            this.isHandoffPending = false;
            this.isOnlineMovePending = false;
            UIDraggable.ResetInputState();
            CleanUpGameState();

            if (this.IsOnline)
                ReleaseOnlineSession();

            UISplashScreen.IN.Show();
            UIGameBoard.IN.Hide();
        });
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

        if (BoardStateManager.IsDoingReplay) return;

        if (this.CurrentTurnNumber > 0 || BoardStateManager.IN.BoardTurns.Count > 0)
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
        if (this.CurrentTurnNumber <= BoardStateManager.IN.BoardTurns.Count)
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
            for (int i = turnIndex; i < statesList.BoardStates.Count; i++)
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
        if (turnIndex < BoardStateManager.IN.BoardTurns.Count)
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
