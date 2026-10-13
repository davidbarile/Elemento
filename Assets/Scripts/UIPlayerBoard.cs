using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using DomainGameRules = Elemento.Domain.GameRules;
using DomainPlayerStateData = Elemento.Domain.PlayerStateData;
using DomainStrongAiOptions = Elemento.Domain.StrongAiOptions;
using DomainStrongAiPlayer = Elemento.Domain.StrongAiPlayer;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using static SettingsManager;
using static UICardCell;

public class UIPlayerBoard : MonoBehaviour
{
    public enum EPlayerType
    {
        None,
        PlayerA,
        PlayerB,
        PlayerC,
        PlayerD
    }

    public string Name;
    public EPlayerType PlayerType;
    public bool IsAutoPlay;

    [ReadOnly]
    public int CurrentMoveNumber;
    public int TotalMovesTaken;

    [Space]
    public UIHand Hand;
    public UIElementZone[] ElementZones;
    [SerializeField] private UITotalPointsDisplay totalPointsDisplay;

    [Header("TRUE for Opponents, FALSE for Player")]
    [SerializeField] private bool shouldDisplayFaceUpPoints;

    public bool IsTakingTurn { get; private set; }

    [SerializeField] private AIMoveCalculator AIMoveCalculator;

    [Space]
    [SerializeField] private GameObject autoPlayButton;
    [SerializeField] private GameObject stopAutoPlayButton;
    public CanvasGroup Brain;
    public GameObject AiOutputBubble;
    [SerializeField] private TextMeshProUGUI aiOutputText;

    private bool cachedIsAutoPlay;
    private DomainStrongAiPlayer strongAiPlayer;
    private SettingsManager.EAiDifficulty? strongAiDifficulty;

    #region Data for doing calculations
    [Header("Data for doing calculations")]
    public int TotalPoints;
    public int TotalFaceUpPoints;

    //TODO: change cards to cardCells
    public List<Card> CardsInHand = new();
    public List<UICardCell> AllPlayableCells = new();

    public List<Card> PointsCards = new();
    public List<Card> MultCards = new();
    public List<Card> BombCards = new();
    public List<Card> ShieldCards = new();

    public Card[] TopCards = new Card[4];
    public Card AirTopCard => this.TopCards[0];
    public Card EarthTopCard => this.TopCards[1];
    public Card FireTopCard => this.TopCards[2];
    public Card WaterTopCard => this.TopCards[3];

    public int[] PointsInHand = new int[4];
    public int[] MultsInHand = new int[4];
    public int[] BombsInHand = new int[4];
    public int[] ShieldsInHand = new int[4];

    public int[] BiggestPointsInHand = new int[4];
    public int[] BiggestMultsInHand = new int[4];
    public int[] BiggestBombsInHand = new int[4];
    public int[] BiggestShieldsInHand = new int[4];

    public int[] MultsInElementZone = new int[4];
    public int[] ShieldsInElementZone = new int[4];

    public int WildPointsInHand;
    public int WildMultsInHand;
    public int WildBombsInHand;
    public int WildShieldsInHand;

    public int PowerCardsInHand;
    public int HealCardsInHand;
    public int FreezeCardsInHand;
    public int LightningCardsInHand;

    private void OnValidate()
    {
        foreach (var zone in this.ElementZones)
        {
            zone.PlayerType = this.PlayerType;
            zone.CardCell.PlayerType = this.PlayerType;
            zone.CardCell.CellType = ECellType.ElementZone;
        }

        this.Hand.RefreshCardCells();

        foreach (var cell in this.Hand.CardCells)
        {
            cell.CellType = ECellType.Hand;
            cell.PlayerType = this.PlayerType;
        }
    }

    public int GetNumPossibleMoves()
    {
        int numPossibleMoves = 0;
        foreach (var cell in this.Hand.CardCells)
        {
            if (cell.gameObject.activeSelf && cell.TopCard != null)
            {
                ++numPossibleMoves;
            }
        }

        foreach (var zone in this.ElementZones)
        {
            if (!zone.IsFrozen && !zone.IsDestroyed && zone.CardCell.TopCard != null)
            {
                ++numPossibleMoves;
            }
        }

        return numPossibleMoves;
    }

    public void GenerateUsefulData()
    {
        //Debug.Log("GenerateUsefulData()");
        this.AllPlayableCells.Clear();
        this.CardsInHand.Clear();

        this.PointsCards.Clear();
        this.MultCards.Clear();
        this.BombCards.Clear();
        this.ShieldCards.Clear();

        this.PointsInHand = new int[4];
        this.MultsInHand = new int[4];
        this.BombsInHand = new int[4];
        this.ShieldsInHand = new int[4];

        this.BiggestPointsInHand = new int[4];
        this.BiggestMultsInHand = new int[4];
        this.BiggestBombsInHand = new int[4];
        this.BiggestShieldsInHand = new int[4];

        this.MultsInElementZone = new int[4];
        this.ShieldsInElementZone = new int[4];
        //TODO add biggest of both of these

        this.WildPointsInHand = 0;
        this.WildMultsInHand = 0;
        this.WildBombsInHand = 0;
        this.WildShieldsInHand = 0;

        this.PowerCardsInHand = 0;
        this.HealCardsInHand = 0;
        this.FreezeCardsInHand = 0;
        this.LightningCardsInHand = 0;

        //TODO: add check if bases have health
        foreach (var cell in this.Hand.CardCells)
        {
            if (cell.gameObject.activeSelf && cell.TopCard != null)
            {
                var card = cell.TopCard;
                var data = card.Data;

                this.AllPlayableCells.Add(cell);

                this.CardsInHand.Add(card);

                if (data.IsPoints) this.PointsCards.Add(card);
                if (data.IsMultiply) this.MultCards.Add(card);
                if (data.IsBomb) this.BombCards.Add(card);
                if (data.IsShield) this.ShieldCards.Add(card);

                for (int i = 0; i < this.ElementZones.Length; i++)
                {
                    this.PointsInHand[i] += data.Points[i];
                    this.MultsInHand[i] += data.Multipliers[i];
                    this.BombsInHand[i] += data.Bombs[i];
                    this.ShieldsInHand[i] += data.Shields[i];

                    this.BiggestPointsInHand[i] = Mathf.Max(this.BiggestPointsInHand[i], data.Points[i]);
                    this.BiggestMultsInHand[i] = Mathf.Max(this.BiggestMultsInHand[i], data.Multipliers[i]);
                    this.BiggestBombsInHand[i] = Mathf.Max(this.BiggestBombsInHand[i], data.Bombs[i]);
                    this.BiggestShieldsInHand[i] = Mathf.Max(this.BiggestShieldsInHand[i], data.Shields[i]);

                    this.BiggestPointsInHand[i] = Mathf.Max(this.BiggestPointsInHand[i], data.WildPoints);
                    this.BiggestMultsInHand[i] = Mathf.Max(this.BiggestMultsInHand[i], data.WildMultiplier);
                    this.BiggestBombsInHand[i] = Mathf.Max(this.BiggestBombsInHand[i], data.WildBomb);
                    this.BiggestShieldsInHand[i] = Mathf.Max(this.BiggestShieldsInHand[i], data.WildShield);

                    var elementCell = this.ElementZones[i].CardCell;
                    this.TopCards[i] = elementCell.TopCard;

                    //TODO: add to ElementZones array for use in special cases
                    //if(elementCell.TopCard != null)
                    //this.AllPlayableCells.Add(elementCell);

                    bool hasAddedWildMult = false;
                    bool hasAddedWildShield = false;

                    foreach (var elementCard in this.ElementZones[i].CardCell.Cards)
                    {
                        //Debug.Log($"{i} cards count = {ElementZones[i].CardCell.Cards.Count}   elementCard = {elementCard.name}  MultsInElementZone[{i}] = { MultsInElementZone[i]}   elementCard.Data.Multipliers[{i}] = {elementCard.Data.Multipliers[i]}   elementCard.Data.WildMultiplier = {elementCard.Data.WildMultiplier}");
                        this.MultsInElementZone[i] += (elementCard.Data.Multipliers[i] + elementCard.Data.WildMultiplier);
                        this.ShieldsInElementZone[i] += (elementCard.Data.Shields[i] + elementCard.Data.WildShield);
                    }
                }

                this.WildPointsInHand += data.WildPoints;
                this.WildMultsInHand += data.WildMultiplier;
                this.WildBombsInHand += data.WildBomb;
                this.WildShieldsInHand += data.WildShield;

                //power cards
                if (data.WildZoneHeal > 0)
                {
                    ++this.HealCardsInHand;
                    ++this.PowerCardsInHand;
                }
                else if (data.WildFreeze > 0)
                {
                    ++this.FreezeCardsInHand;
                    ++this.PowerCardsInHand;
                }
                else if (data.WildLightning > 0)
                {
                    ++this.LightningCardsInHand;
                    ++this.PowerCardsInHand;
                }
            }
        }

        //add cards in Element Zones
        //foreach (var zone in this.ElementZones)
        //{
        //    if (!zone.IsFrozen && !zone.IsDestroyed && zone.CardCell.TopCard != null)
        //    {
        //        var cell = zone.CardCell;
        //        this.AllPlayableCells.Add(cell);//TODO: add back but add checks for not duplicating moves, etc.
        //    }
        //}
    }

    public int GetHighestScoringMoveValue()
    {
        int highestValue = 0;

        for (int i = 0; i < this.ElementZones.Length; i++)
        {
            var zone = this.ElementZones[i];

            if (!zone.IsDestroyed && !zone.IsFrozen)
            {
                var biggestMultCard = this.BiggestMultsInHand[i];

                if (biggestMultCard > 0)
                    highestValue = Mathf.Max(highestValue, biggestMultCard * zone.Points);

                var biggestPointsCard = this.BiggestPointsInHand[i];

                highestValue = Mathf.Max(highestValue, biggestPointsCard * zone.Points);
            }
        }

        return highestValue;
    }

    public int GetNumPlayableZones()
    {
        int numPlayableZones = 0;

        for (int i = 0; i < this.ElementZones.Length; i++)
        {
            var zone = this.ElementZones[i];

            if (!zone.IsDestroyed && !zone.IsFrozen)
                ++numPlayableZones;
        }

        return numPlayableZones;
    }

    #endregion

    private void Start()
    {
        this.cachedIsAutoPlay = this.IsAutoPlay;

        foreach (var zone in this.ElementZones)
        {
            zone.OnElementZonePointsChanged += CalculateTotalPoints;
        }

        if (this.Brain != null)
            this.Brain.gameObject.SetActive(false);

        CalculateTotalPoints();

        ClearAiOutputText();
        this.AiOutputBubble.SetActive(false);

        Reset();
    }

    public void CalculateTotalPoints(bool inShouldCheckWinLoseState = true)
    {
        this.TotalPoints = DomainGameRules.CalculatePlayerPoints(CreateStateData());
        this.TotalFaceUpPoints = 0;

        foreach (var zone in this.ElementZones)
        {
            this.TotalFaceUpPoints += zone.FaceUpPoints;

            int points = this.shouldDisplayFaceUpPoints ? zone.FaceUpPoints : zone.Points;
            this.totalPointsDisplay.SetElementVisible(zone.CardCell.ElementType, points);
        }

        this.totalPointsDisplay.TickToTotalPoints(this.TotalPoints);

        if (inShouldCheckWinLoseState)
            GameManager.IN.CheckWinLoseState(this);
    }

    public DomainPlayerStateData CreateStateData()
    {
        var state = new DomainPlayerStateData
        {
            Id = (int)this.PlayerType,
            Name = this.Name
        };

        foreach (var zone in this.ElementZones)
            state.Zones.Add(zone.CreateStateData());

        return state;
    }

    public void SwapPlayerState(UIPlayerBoard inOther)
    {
        if (inOther == null)
            throw new ArgumentNullException(nameof(inOther));
        if (this.ElementZones.Length != inOther.ElementZones.Length)
            throw new InvalidOperationException("Cannot swap player boards with different element-zone counts.");

        Swap(ref this.Name, ref inOther.Name);
        Swap(ref this.PlayerType, ref inOther.PlayerType);
        Swap(ref this.CurrentMoveNumber, ref inOther.CurrentMoveNumber);
        Swap(ref this.TotalMovesTaken, ref inOther.TotalMovesTaken);

        this.Hand.SwapPlayerState(inOther.Hand);
        for (int i = 0; i < this.ElementZones.Length; i++)
            this.ElementZones[i].SwapPlayerState(inOther.ElementZones[i]);

        ConfigurePlayerType();
        inOther.ConfigurePlayerType();
        RefreshPlayerStateData();
        inOther.RefreshPlayerStateData();
        CalculateTotalPoints(false);
        inOther.CalculateTotalPoints(false);
    }

    private void RefreshPlayerStateData()
    {
        GenerateUsefulData();
        for (int i = 0; i < this.ElementZones.Length; i++)
            this.TopCards[i] = this.ElementZones[i].CardCell.TopCard;
    }

    public void SetPlayerType(EPlayerType inPlayerType)
    {
        this.PlayerType = inPlayerType;
        ConfigurePlayerType();
    }

    private void ConfigurePlayerType()
    {
        foreach (var zone in this.ElementZones)
        {
            zone.PlayerType = this.PlayerType;
            zone.CardCell.PlayerType = this.PlayerType;
        }

        foreach (var cell in this.Hand.CardCells)
            cell.PlayerType = this.PlayerType;
    }

    private static void Swap<T>(ref T inFirst, ref T inSecond)
    {
        T value = inFirst;
        inFirst = inSecond;
        inSecond = value;
    }

    public void Reset()
    {
        StopMovesCoroutine();

        this.Hand.ResetCardCellSiblingIndices();

        this.CurrentMoveNumber = 0;
        this.TotalMovesTaken = 0;

        ClearAiOutputText();
        this.AiOutputBubble.SetActive(false);

        SetAutoPlay(this.cachedIsAutoPlay);
    }

    public void StartTurn()
    {
        StartCoroutine(DoMovesCo());
    }

    public void StopMovesCoroutine()
    {
        StopAllCoroutines();
        this.IsTakingTurn = false;
    }

    private IEnumerator DoMovesCo()
    {
        this.IsTakingTurn = true;

        bool isPlayer = this.PlayerType == EPlayerType.PlayerA;

        //if (this.IsAutoPlay && this.Brain != null)
        //{
        //    this.Brain.gameObject.SetActive(true);
        //    this.Brain.DOFade(1, .5f);
        //}

        if (this.Brain != null)
            this.Brain.gameObject.SetActive(true);

        var numPossibleMoves = GetNumPossibleMoves();
        var movesOffset = SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn ? 1 : 0;
        var plannedMoveCount = Mathf.Min(numPossibleMoves, GameManager.IN.MovesRemaining + movesOffset);
        var rejectedMoveKeys = new HashSet<string>();
        int completedMoveCount = 0;
        int decisionCount = 0;
        bool useLegacyFallback = false;

        while (!UnityDomainMapper.ShouldStopAiTurn(completedMoveCount, plannedMoveCount,
            decisionCount, GameManager.IN.MovesRemaining + movesOffset, IsAiTurnActive()))
        {
            if (!SettingsManager.IN.UseLegacyAi && !useLegacyFallback)
                yield return null;

            if (UnityDomainMapper.ShouldStopAiTurn(completedMoveCount, plannedMoveCount,
                decisionCount, GameManager.IN.MovesRemaining + movesOffset, IsAiTurnActive()))
                break;

            ++decisionCount;
            Move move;
            bool shouldEndTurn;
            bool usedLegacyFallback;
            string moveInfo;
            if (useLegacyFallback)
            {
                usedLegacyFallback = true;
                move = GetFallbackAiMove(out _, out moveInfo);
                shouldEndTurn = move == null;
            }
            else
            {
                move = GetAiMove(rejectedMoveKeys, out shouldEndTurn, out usedLegacyFallback,
                    out _, out moveInfo);
            }
            useLegacyFallback = false;

            if (shouldEndTurn)
                break;

            if (move != null && (move.Value > 0 || move.IsDiscard))
            {
                string moveKey = UnityDomainMapper.CreateMoveKey(move);
                if (usedLegacyFallback && rejectedMoveKeys.Contains(moveKey))
                {
                    Debug.LogWarning("Legacy AI selected the same move rejected by the Unity drop path; ending turn.");
                    break;
                }

                string output = $"<color=blue>Turn {GameManager.IN.CurrentTurnNumber}: Move {completedMoveCount + 1}/{plannedMoveCount}</color>\n<color=red>{move.CardType}   Value: {move.Value}</color>\n[{move.SourceCell.CellType}, {move.SourceCell.ElementType} --> {move.TargetCell.CellType}, {move.TargetCell.ElementType}]";
                AddToAiOutputText(output);
                AddToAiOutputText(moveInfo, 2);

                Debug.Log(output);
                var sourceCard = move.SourceCell.TopCard;

                if (move.IsDiscard && !isPlayer)
                {
                    GameManager.IN.DiscardZone.SetActive(true);
                    yield return new WaitForSeconds(.3f);
                }
                else if (move.ActivatePowerZone)
                {
                    CardsManager.IN.PowerCardOverride = move.CardType;

                    //Debug.Log($"ActivatePowerZone   move.SourceCell = {move.SourceCell}     move.SourceCell.ElementZone = {move.SourceCell.ElementZone}  move.CardType {move.CardType}");
                    move.SourceCell.ElementZone.UseActivatePower();

                    while (GameManager.IsDoingActivateSequence || GameManager.IsDealingHand)
                    {
                        yield return new WaitForSeconds(.3f);
                    }
                }
                else
                    GameManager.IN.IncrementCurrentMove(this);

                if (!move.ActivatePowerZone)
                {
                    var tweenDuration = GameManager.IN.OpponentMoveTweenDuration;

                    var discardPile = GameManager.IN.DiscardZoneTransform;

                    if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
                        discardPile = isPlayer ? GameManager.IN.DiscardToEndTurnZoneTransform : GameManager.IN.DiscardZoneTransform;

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

                    if (ReferenceEquals(move.SourceCell.TopCard, sourceCard))
                    {
                        if (!string.IsNullOrEmpty(moveKey))
                            rejectedMoveKeys.Add(moveKey);

                        Debug.LogWarning("AI move was rejected by the Unity drop path.");
                        if (!usedLegacyFallback && !SettingsManager.IN.UseLegacyAi)
                        {
                            useLegacyFallback = true;
                            continue;
                        }

                        break;
                    }

                    if (move.IsDiscard && !isPlayer)
                    {
                        ++completedMoveCount;
                        yield return new WaitForSeconds(.6f);
                        GameManager.IN.ResetMovesRemaining();
                        GameManager.IN.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);
                        break;
                    }

                    if (move.CardType == Card.ECardType.Bomb)
                    {
                        yield return new WaitForSeconds(.5f);

                        //TODO: if UI is going to display bomb cards, have a while loop until dismissed
                    }
                }

                ++completedMoveCount;
                yield return new WaitForSeconds(.5f);
            }
            else
            {
                AddToAiOutputText($"Turn {GameManager.IN.CurrentTurnNumber}: No moves found");
                break;
            }
        }

        AddToAiOutputText("--------------", 2);

        //if (this.IsAutoPlay && this.Brain != null)
        //this.Brain.DOFade(0, .5f).OnComplete(() => { this.Brain.gameObject.SetActive(false); });


        if (this.Brain != null)
            this.Brain.gameObject.SetActive(false);

        this.IsTakingTurn = false;
    }

    private Move GetAiMove(HashSet<string> inRejectedMoveKeys, out bool outShouldEndTurn,
        out bool outUsedLegacyFallback, out int outPossibleMoveCount, out string outMoveInfo)
    {
        outShouldEndTurn = false;
        outUsedLegacyFallback = false;
        outPossibleMoveCount = 0;
        outMoveInfo = string.Empty;

        if (SettingsManager.IN.UseLegacyAi)
        {
            outUsedLegacyFallback = true;
            var legacyMove = GetFallbackAiMove(out outPossibleMoveCount, out outMoveInfo);
            outShouldEndTurn = legacyMove == null;
            return legacyMove;
        }

        try
        {
            GenerateUsefulData();

            int cardSpawnBatchSize = CardsManager.IN == null ? 0 : CardsManager.IN.CardSpawnBatchSize;
            int powerDeckCount = CardsManager.IN == null ? 0 : CardsManager.IN.PowerCardDatas.Count;
            var rules = UnityDomainMapper.CreateRules(SettingsManager.IN, GameManager.IN.PlayerBoards,
                cardSpawnBatchSize);
            var view = UnityDomainMapper.CreatePlayerView(this, GameManager.IN.PlayerBoards,
                GameManager.IN.Deck.Cards.Count, powerDeckCount, GameManager.IN.CurrentTurnNumber,
                GameManager.IN.TurnPhase, GameManager.IN.MovesRemaining, rules);
            UnityDomainMapper.FilterMappableMoves(view, inRejectedMoveKeys);
            outPossibleMoveCount = view.LegalMoves.Count;
            if (outPossibleMoveCount <= 0)
            {
                outShouldEndTurn = true;
                return null;
            }

            var options = new DomainStrongAiOptions
            {
                Difficulty = UnityDomainMapper.MapDifficulty(SettingsManager.IN.AiDifficulty)
            };
            if (this.strongAiPlayer == null
                || this.strongAiDifficulty != SettingsManager.IN.AiDifficulty)
            {
                this.strongAiPlayer = new DomainStrongAiPlayer(options);
                this.strongAiDifficulty = SettingsManager.IN.AiDifficulty;
            }
            var domainMove = this.strongAiPlayer.ChooseMove(view, new UnityDomainRandom());

            if (!UnityDomainMapper.TryCreateMove(domainMove, view, this, GameManager.IN.PlayerBoards,
                    out var move, out outShouldEndTurn, out string error))
            {
                inRejectedMoveKeys?.Add(UnityDomainMapper.CreateMoveKey(domainMove));
                throw new InvalidOperationException(error);
            }
            if (!outShouldEndTurn && !CanExecuteAiMove(move, out error))
            {
                inRejectedMoveKeys?.Add(UnityDomainMapper.CreateMoveKey(domainMove));
                throw new InvalidOperationException(error);
            }

            outMoveInfo = $"<color=black>Strong AI difficulty: {SettingsManager.IN.AiDifficulty} | Legal moves: {outPossibleMoveCount}</color>";
            return move;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Strong AI decision failed; using legacy AI for this decision. {exception.Message}");
            outUsedLegacyFallback = true;
            var fallbackMove = GetFallbackAiMove(out outPossibleMoveCount, out outMoveInfo);
            outShouldEndTurn = fallbackMove == null;
            return fallbackMove;
        }
    }

    private Move GetLegacyAiMove(out int outPossibleMoveCount, out string outMoveInfo)
    {
        var move = this.AIMoveCalculator.CalculateBestMove(GameManager.IN.CurrentPlayerBoard);
        outPossibleMoveCount = this.AIMoveCalculator.PossibleMoves.Count;
        outMoveInfo = this.AIMoveCalculator.MoveOutputInfo;
        return move;
    }

    private Move GetFallbackAiMove(out int outPossibleMoveCount, out string outMoveInfo)
    {
        try
        {
            return GetLegacyAiMove(out outPossibleMoveCount, out outMoveInfo);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Legacy AI fallback also failed; ending this AI turn. {exception.Message}");
            outPossibleMoveCount = 0;
            outMoveInfo = string.Empty;
            return null;
        }
    }

    private static bool CanExecuteAiMove(Move inMove, out string outError)
    {
        if (inMove == null || inMove.SourceCell == null || inMove.TargetCell == null)
        {
            outError = "Strong AI move is missing a live source or target cell.";
            return false;
        }
        if (inMove.ActivatePowerZone != null)
        {
            bool canActivate = inMove.SourceCell.ElementZone == inMove.ActivatePowerZone;
            outError = canActivate ? string.Empty : "Strong AI activation is no longer executable.";
            return canActivate;
        }
        if (inMove.SourceCell.TopCard == null || inMove.SourceCell.TopCard.Draggable == null)
        {
            outError = "Strong AI source card is no longer executable.";
            return false;
        }
        if (inMove.TargetCell.AnimTarget == null && inMove.TargetCell.CardHolder == null)
        {
            outError = "Strong AI target cell has no animation target.";
            return false;
        }
        if (inMove.IsDiscard)
        {
            bool usesEndTurnDiscard = SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn
                && inMove.SourceCell.PlayerType == EPlayerType.PlayerA;
            var discardTarget = usesEndTurnDiscard
                ? GameManager.IN.DiscardToEndTurnZoneTransform
                : GameManager.IN.DiscardZoneTransform;
            if (discardTarget == null)
            {
                outError = "Strong AI discard target is unavailable.";
                return false;
            }
        }

        outError = string.Empty;
        return true;
    }

    private bool IsAiTurnActive()
    {
        if (GameManager.IN == null || GameManager.IN.CurrentPlayerBoard != this)
            return false;

        return GameManager.IN.TurnPhase == GameManager.ETurnPhase.PlayerPlaying
            || GameManager.IN.TurnPhase == GameManager.ETurnPhase.OpponentPlaying;
    }

    //TODO: optimize with stringbuilder
    public void AddToAiOutputText(string inValue, int numLineBreaks = 1)
    {
        this.aiOutputText.text += inValue;

        for (int i = 0; i < numLineBreaks; i++)
        {
            this.aiOutputText.text += "\n";
        }
    }

    public void SetAiOutputText(string inValue)
    {
        this.aiOutputText.text = inValue;
    }

    public void ClearAiOutputText()
    {
        this.aiOutputText.text = string.Empty;
    }

    private void OnDestroy()
    {
        foreach (var zone in this.ElementZones)
        {
            zone.OnElementZonePointsChanged -= CalculateTotalPoints;
        }
    }

    //called from buttons
    public void SetAutoPlay(bool inIsOn)
    {
        this.IsAutoPlay = inIsOn;

        if (this.autoPlayButton == null) return;

        this.autoPlayButton.SetActive(!inIsOn);
        this.stopAutoPlayButton.SetActive(inIsOn);

        if (!inIsOn)
            this.AiOutputBubble.SetActive(false);
    }

    public void HandleBrainButtonPress()
    {
        this.AiOutputBubble.SetActive(!this.AiOutputBubble.activeSelf);
    }
}
