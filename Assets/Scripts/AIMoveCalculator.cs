using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static Card;
using static SettingsManager;

[Serializable]
public class AIMoveCalculator
{
    private UIPlayerBoard playerBoard;

    public string MoveOutputInfo;

    public List<Move> PossibleMoves;
    public List<Move> BombMoves;
    public List<Move> DiscardMoves;
    public List<Move> ActivatePowerMoves;

    private int numActivatedPlayerZones;
    private int numActivatedOpponentZones;
    private UIElementZone highestPointPlayerZone;
    private UIElementZone lowestPointPlayerZone;
    private UIElementZone highestPointOpponentZone;
    private UIElementZone lowestPointOpponentZone;

    private UIElementZone highestHealthPlayerZone;
    private UIElementZone lowestHealthPlayerZone;
    private UIElementZone highestHealthOpponentZone;
    private UIElementZone lowestHealthOpponentZone;

    private int numFrozenPlayerZones;
    private int numFrozenOpponentZones;
    private int numDestroyedPlayerZones;
    private int numDestroyedOpponentZones;
    private UIElementZone highestFrozenPlayerZone;
    private UIElementZone lowestFrozenPlayerZone;

    private int numMovesRemaining;
    private bool isMustDiscardMode;
    private bool isLastMove;
    private bool isLookingForDiscard;

    private bool shouldSkipToEnd;

    private struct PossiblePowerCardMove
    {
        public float UseValue;
        public ECardType PowerCardType;
    }

    public Move CalculateBestMove(UIPlayerBoard inPlayerBoard)
    {
        this.shouldSkipToEnd = false;
        //Debug.Log($"AI.CalculateBestMove()      this.playerBoard = {this.playerBoard}    cells = { this.playerBoard.AllPlayableCells.Count}");
        #region INIT VARIABLES for doing calculations --------------------------
        this.playerBoard = inPlayerBoard;
        this.playerBoard.GenerateUsefulData();

        //cache these for use in other functions
        this.numMovesRemaining = Mathf.Min(this.playerBoard.CardsInHand.Count, GameManager.IN.MovesRemaining);
        this.isMustDiscardMode = SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn;
        this.isLastMove = this.numMovesRemaining <= 1;
        this.isLookingForDiscard = this.numMovesRemaining <= 0 && this.isMustDiscardMode;

        this.MoveOutputInfo = string.Empty;
        this.PossibleMoves.Clear();
        this.BombMoves.Clear();
        this.DiscardMoves.Clear();
        this.ActivatePowerMoves.Clear();
        #endregion

        //get if zones are Frozen or Destroyed
        GetZoneStatus();

        DecideWhetherToUseActivatedZone();

        var highValue = 99999;

        //TODO: loop thru all boards first and detect emergency states, bombs, shields, etc.

        //loop thru all players possible cards to move
        foreach (var playerCell in this.playerBoard.AllPlayableCells)
        {
            if (this.shouldSkipToEnd)
                break;

            var topCardData = playerCell.TopCard.Data;
            bool elementZoneHealthExists = SettingsManager.IN.TutorialSafeElementZoneHealth > 0;

            foreach (var board in GameManager.IN.PlayerBoards)
            {
                if (this.shouldSkipToEnd)
                    break;

                bool isOwnBoard = board.Equals(this.playerBoard);
                bool isScoreMove = isOwnBoard && !topCardData.IsBomb && topCardData.WildLightning <= 0;
                var percentToWin = (float)board.TotalPoints / SettingsManager.IN.ElementZonePointsRequiredToActivate;

                //loop thru all cards in Hand and top cards in Element Zones and create power moves, then point/multplier/shield moves, and finally bomb moves

                for (int i = 0; i < board.ElementZones.Length; i++)
                {
                    var zone = board.ElementZones[i];
                    var targetCell = zone.CardCell;

                    #region POWER CARDS -----------------------------------------
                    if (topCardData.IsPowerCard)
                    {
                        var powerCardMove = new Move
                        {
                            SourceCell = playerCell,
                            TargetCell = targetCell,
                        };

                        powerCardMove.Value = 100;

                        if (isOwnBoard)
                        {
                            if (topCardData.WildFreeze > 0)
                            {
                                powerCardMove.Value = 0; // TODO: for now.  change to if total points greater than X, etc.
                            }

                            if (topCardData.WildLightning > 0 && !zone.IsFrozen)
                            {
                                powerCardMove.Value = 0;
                            }

                            if (topCardData.WildZoneHeal > 0)
                            {
                                if (zone.Health >= zone.MaxHealth || zone.IsFrozen || zone.CardCell.Cards.Count > 0)
                                {
                                    powerCardMove.Value = 0;
                                }
                                else
                                {
                                    if (zone.IsDestroyed)
                                        powerCardMove.Value += 10;

                                    powerCardMove.Value += (zone.MaxHealth - zone.Health);
                                }
                            }
                            else
                            {
                                var zoneHealthLost = zone.MaxHealth - zone.Health;
                                powerCardMove.Value = zoneHealthLost * 10;
                            }
                        }
                        else
                        {
                            if (topCardData.WildFreeze > 0)
                            {
                                if (zone.FrozenHealth > 2 || zone.IsDestroyed)
                                {
                                    powerCardMove.Value = zone.IsDestroyed ? 0 : 1;
                                }
                                else
                                {
                                    if (zone.Equals(this.highestPointOpponentZone))
                                        powerCardMove.Value += 10;

                                    powerCardMove.Value += (board.MultsInElementZone[i] * 2);
                                }
                            }

                            if (topCardData.WildLightning > 0)
                            {
                                if (zone.IsDestroyed || zone.IsFrozen || zone.Health < 3)
                                {
                                    powerCardMove.Value = 0;
                                }
                                else
                                {
                                    //handle case below with bombs
                                }
                            }

                            if (topCardData.WildZoneHeal > 0)
                            {
                                powerCardMove.Value = 0;
                            }
                        }

                        if (powerCardMove.Value > 0)
                        {
                            this.PossibleMoves.Add(powerCardMove);
                        }
                        else
                        {
                            powerCardMove.IsDiscard = true;
                            this.DiscardMoves.Add(powerCardMove);
                            continue;
                        }
                    }

                    #endregion

                    #region Score Move -----------------------------------------

                    if (isScoreMove)
                    {
                        #region DISCARD unusable -------------------------------

                        bool shouldDiscard = isOwnBoard && (zone.IsDestroyed || topCardData.IsBomb);
                        bool isFrozenDiscard = zone.IsFrozen && !topCardData.IsBomb;
                        bool isOwnCell = playerCell.Equals(targetCell);
                        bool isCellFull = zone.CardCell.Cards.Count >= zone.CardCell.MaxCards;//TODO: consider option of moving topCard

                        if (shouldDiscard || isFrozenDiscard || isOwnCell || isCellFull)
                        {
                            var discardMove = new Move
                            {
                                SourceCell = playerCell,
                                TargetCell = targetCell,
                                Value = -1,
                                IsDiscard = true
                            };

                            this.DiscardMoves.Add(discardMove);
                            continue;
                        }

                        #endregion

                        #region CALCULATE Move Value Modifiers -----------------

                        //calculate priority of moves
                        float moveValueModifier = 1;

                        //prioritize zones with higher points
                        moveValueModifier += zone.Points * .05f;

                        //if Element Zone has multiplier, boost move value
                        moveValueModifier += board.MultsInElementZone[i] * .5f;

                        //DEFENSIVE: if Element Zone health low
                        bool isZoneBombable = elementZoneHealthExists && targetCell.Cards.Count < 3;
                        if (isZoneBombable) //maybe add case where Cards.Count < 3
                        {
                            float zoneHealthPercentLeft = (float)zone.Health / zone.MaxHealth;

                            var prevModifier = moveValueModifier;

                            if (zoneHealthPercentLeft < .5f)
                                moveValueModifier += (.5f - (targetCell.Cards.Count * .1f));
                            else if (zoneHealthPercentLeft < .1f)
                                moveValueModifier += (1f - (targetCell.Cards.Count * .1f));

                            //do not let calculation drop below original modifier
                            moveValueModifier = MathF.Max(moveValueModifier, prevModifier);
                        }

                        #endregion

                        var shieldsInHand = board.ShieldsInHand[i] + board.WildShieldsInHand;

                        #region POINTS -----------------------------------------

                        //combine element points and wild points
                        var maxPoints = Mathf.Max(topCardData.Points[i], topCardData.WildPoints);

                        if (maxPoints > 0)
                        {
                            bool moveWillWinGame = playerBoard.TotalPoints + topCardData.Points[i] >= SettingsManager.IN.TotalPointsRequiredToWin;

                            if (moveWillWinGame)
                            {
                                this.shouldSkipToEnd = true;
                                moveValueModifier = highValue;
                            }
                            else
                            {
                                //if Element Zone is close to winning, add to move value
                                bool hasValidShieldsInHand = shieldsInHand <= 0;
                                if (!this.isLastMove || !hasValidShieldsInHand)
                                {
                                    if (percentToWin < .5f)
                                        moveValueModifier += percentToWin;
                                    else if (percentToWin < .8f)
                                        moveValueModifier += (1.2f * percentToWin);
                                }
                            }

                            //prioritize points over shields in earlier moves
                            if (this.numMovesRemaining > shieldsInHand)
                            {
                                if (shieldsInHand > 0)
                                    moveValueModifier += this.numMovesRemaining;
                            }

                            //if multiple points cards of same element prioritize elements that have more points and mults in hand or in Element Zone
                            float priorityModifier = 0;
                            priorityModifier += board.MultsInElementZone[i] * .02f;
                            priorityModifier += board.MultsInHand[i] * .02f;
                            priorityModifier += zone.Points * .01f;
                            priorityModifier += board.PointsInHand[i] * .01f;

                            var pointsMove = new Move
                            {
                                SourceCell = playerCell,
                                TargetCell = targetCell,
                                CardType = ECardType.Points,
                                Value = ((maxPoints + (board.MultsInElementZone[i] * .3f)) * moveValueModifier) + priorityModifier
                            };
                            this.PossibleMoves.Add(pointsMove);//TODO: make other array of attack moves, defense moves, etc.
                        }

                        #endregion

                        #region MULTIPLIERS ------------------------------------

                        //combine element multipliers and wild multipliers
                        var maxMults = Mathf.Max(topCardData.Multipliers[i], topCardData.WildMultiplier);

                        if (maxMults > 0 && !this.shouldSkipToEnd)
                        {
                            moveValueModifier = 3;

                            bool moveWillWinGame = (playerBoard.TotalPoints - zone.Points) + (zone.Points * topCardData.Multipliers[i]) >= SettingsManager.IN.TotalPointsRequiredToWin;

                            if (moveWillWinGame)
                            {
                                this.shouldSkipToEnd = true;
                                moveValueModifier = highValue;
                            }
                            else
                            {
                                //if Element Zone is close to winning, add to move value
                                bool hasValidShieldsInHand = board.ShieldsInHand[i] + board.WildShieldsInHand <= 0;
                                if (this.numMovesRemaining > 1 || !hasValidShieldsInHand)
                                {
                                    if (percentToWin < .5f)
                                        moveValueModifier += (2 * percentToWin);
                                    else if (percentToWin < .8f)
                                        moveValueModifier += (3 * percentToWin);
                                }
                            }

                            //prioritize multipliers over shields in earlier moves
                            if (this.numMovesRemaining > shieldsInHand)
                            {
                                if (shieldsInHand > 0)
                                    moveValueModifier += (this.numMovesRemaining * 2f);
                            }

                            //if multiple multiplier cards of same element prioritize elements that have more points and mults in hand or in Element Zone
                            float priorityModifier = 0;
                            priorityModifier += board.MultsInElementZone[i] * .05f;
                            priorityModifier += board.MultsInHand[i] * .05f;
                            priorityModifier += zone.Points * .02f;
                            priorityModifier += board.PointsInHand[i] * .02f;

                            var multsMove = new Move
                            {
                                SourceCell = playerCell,
                                TargetCell = targetCell,
                                CardType = ECardType.Multiplier,
                                Value = ((maxMults + (zone.Points * .3f)) * moveValueModifier) + priorityModifier
                            };
                            this.PossibleMoves.Add(multsMove);//TODO: make other array of attack moves, defense moves, etc.
                        }

                        #endregion

                        #region SHIELDS ----------------------------------------

                        //combine element shield and wild shields
                        var maxShield = Mathf.Max(topCardData.Shields[i], topCardData.WildShield);

                        if (maxShield > 0 && !this.shouldSkipToEnd)
                        {
                            //calculate priority of moves
                            moveValueModifier = 1;

                            var numValidShieldsInHand = board.ShieldsInHand[i] + board.WildShieldsInHand;

                            if (targetCell.TopCard != null)
                            {
                                //check if topcard is shield and higher than current shield
                                var targetTopCardMaxShield = Mathf.Max(targetCell.TopCard.Data.Shields[i], targetCell.TopCard.Data.WildShield);

                                var pointsAboveShield = 0;

                                if (targetTopCardMaxShield <= maxShield)
                                {
                                    //loop thru cards until shield found, base value on the total score count
                                    var startingIndex = targetTopCardMaxShield > 0 ? targetCell.Cards.Count - 2 : targetCell.Cards.Count - 1;

                                    if (startingIndex > 0)
                                    {
                                        for (int j = startingIndex; j >= 0; j--)
                                        {
                                            var card = targetCell.Cards[j];

                                            var pts = card.Data.Points[i] + card.Data.WildPoints;
                                            var mults = card.Data.Multipliers[i] + card.Data.WildMultiplier;

                                            pointsAboveShield += pts;
                                            if (mults > 1)
                                                pointsAboveShield *= mults;

                                            if (card.Data.IsShield)
                                                break;
                                        }
                                    }

                                    moveValueModifier += (pointsAboveShield * 1f);
                                }

                                //if last two turns, raise value of zones with mults, and high points
                                if (this.numMovesRemaining <= shieldsInHand)
                                {
                                    //prioritize zones with higher points
                                    moveValueModifier += (zone.Points * .1f);//this was already done, but we're adding even more for shields

                                    //also increase value for number of mult cards in zone
                                    moveValueModifier += board.MultsInElementZone[i];//this was already done, but we're adding even more for shields

                                    // increase value by num turns left
                                    moveValueModifier += (5 - this.numMovesRemaining);
                                }
                            }
                            else
                            {
                                //prioritize zones with lower health
                                var zoneHealthLost = zone.MaxHealth - zone.Health;
                                moveValueModifier += zoneHealthLost;

                                //prioritize move if last 2 moves
                                if (this.numMovesRemaining <= 2)
                                {
                                    moveValueModifier += (3 - this.numMovesRemaining);
                                }

                                //it's the biggest shield card in hand
                                if (maxShield >= board.BiggestShieldsInHand[i])
                                {
                                    moveValueModifier += (3 - this.numMovesRemaining);
                                }
                            }

                            var shieldMove = new Move
                            {
                                SourceCell = playerCell,
                                TargetCell = targetCell,
                                CardType = ECardType.Shield,
                                Value = maxShield * moveValueModifier
                            };
                            this.PossibleMoves.Add(shieldMove);//TODO: make other array of attack moves, defense moves, etc.
                        }
                        #endregion
                    }
                    #endregion

                    if (!this.shouldSkipToEnd)
                    {
                        #region BOMBS ------------------------------------------

                        //combine element bomb and wild bomb
                        var bombPoints = Mathf.Max(topCardData.Bombs[i], topCardData.WildBomb);
                        var isLightning = topCardData.WildLightning > 0;

                        if (isLightning)
                            bombPoints = topCardData.WildLightning;

                        #region DISCARD unusable bombs -------------------------

                        var isShieldTooStrong = false;
                        var targetTopCardMaxShield = 0;

                        //if target cell shield greater or equal to bomb, reject
                        if (targetCell.TopCard != null)
                        {
                            targetTopCardMaxShield = Mathf.Max(targetCell.TopCard.Data.Shields[i], targetCell.TopCard.Data.WildShield);

                            if (bombPoints < targetTopCardMaxShield && !isLightning)
                                isShieldTooStrong = true;
                        }

                        bool isInvalid = (isOwnBoard && !zone.IsFrozen && (topCardData.IsBomb || isLightning || !topCardData.IsComboWithPointsComboOrShieldOfElement(zone.CardCell.ElementType))) ||
                            (!isOwnBoard && zone.IsFrozen && (topCardData.IsBombOrCombo || isLightning));

                        if (isInvalid || zone.IsDestroyed || isShieldTooStrong || bombPoints <= 0 || !topCardData.HasBombElement(zone.CardCell.ElementType))
                        {
                            var discardMove = new Move
                            {
                                SourceCell = playerCell,
                                TargetCell = targetCell,
                                CardType = ECardType.Bomb,
                                IsDiscard = true,
                                Value = -1
                            };

                            this.DiscardMoves.Add(discardMove);
                            continue;
                        }

                        #endregion

                        float bombValueModifier = 1;

                        if (!isOwnBoard)
                        {
                            //opponent board
                            if (elementZoneHealthExists || targetCell.Cards.Count > 0)
                            {
                                //more likely to bomb frozen zone if more zones are frozen or destroyed
                                bombValueModifier += (this.numFrozenPlayerZones * 2);
                                bombValueModifier += (this.numDestroyedPlayerZones * 2);

                                if (targetCell.TopCard == null)
                                {
                                    //no cards in zone, so set priority of bombing zone for health
                                    bool canDestroyZone = zone.Health <= bombPoints;

                                    if (canDestroyZone)
                                        bombValueModifier += 5;
                                    else if (zone.Equals(this.lowestHealthOpponentZone))
                                        bombValueModifier += 2;
                                }
                                else
                                {
                                    //cards in zone

                                    //slightly prioritize zones with higher points
                                    bombValueModifier += (zone.Points * .1f);

                                    //greatly prioritize zones with higher multipliers
                                    bombValueModifier += (board.MultsInElementZone[i] * 3);

                                    if (zone.Equals(this.highestPointPlayerZone))
                                        bombValueModifier += 3;

                                    if (isLightning)
                                        bombValueModifier += board.ShieldsInElementZone[i];

                                    //loop thru cards until first shield found, base value on the total score count
                                    var pointsAboveShield = 0;
                                    var multsAboveShield = 0;
                                    var counter = 0;
                                    for (int j = targetCell.Cards.Count - 1; j >= 0; j--)
                                    {
                                        var card = targetCell.Cards[j];

                                        var pts = card.Data.Points[i] + card.Data.WildPoints;
                                        var mults = card.Data.Multipliers[i] + card.Data.WildMultiplier;

                                        multsAboveShield += mults;

                                        pointsAboveShield += pts;
                                        if (mults > 1)
                                            pointsAboveShield *= mults;

                                        if (isLightning)
                                        {
                                            //drill down 5 cards deep regardless
                                            ++counter;
                                            if (counter >= targetCell.TopCard.Data.WildLightning)
                                                break;
                                        }
                                        else
                                        {
                                            //stop at first shield
                                            if (card.Data.IsShield)
                                                break;
                                        }
                                    }

                                    bombValueModifier += (pointsAboveShield * 1f);

                                    var multsBonus = isLightning ? .4f : .2f;
                                    bombValueModifier += (multsAboveShield * multsBonus);

                                    //make more likely to bomb in earlier turns
                                    if (this.numMovesRemaining > 2)
                                    {
                                        // increase value by num turns left
                                        bombValueModifier += 2;
                                    }
                                }
                            }
                        }
                        else
                        {
                            //own board
                            if (zone.IsFrozen)
                            {
                                //if bomb will break ice
                                if (zone.FrozenHealth <= bombPoints)
                                    bombValueModifier += 5;
                                else
                                {
                                    //higher value the closer to breaking the ice
                                    var pointsAboveZeroAfterBombing = (SettingsManager.IN.FrozenZoneHealth - zone.FrozenHealth) - bombPoints;
                                    bombValueModifier += (pointsAboveZeroAfterBombing * .3f);
                                }

                                //how many other zones destroyed or frozen
                                if (zone.Equals(this.lowestFrozenPlayerZone))
                                    bombValueModifier += 2;

                                //more likely to bomb frozen zone if more zones frozen or destroyed
                                bombValueModifier += (this.numFrozenPlayerZones * 2);
                                bombValueModifier += (this.numDestroyedPlayerZones * 2);
                            }
                        }

                        bombValueModifier *= .3f;//it was too OP, and I'm too lazy to fix all the cases
                        bombValueModifier = MathF.Max(1, bombValueModifier);

                        var bombMove = new Move
                        {
                            SourceCell = playerCell,
                            TargetCell = targetCell,
                            CardType = ECardType.Bomb,
                            Value = bombPoints * bombValueModifier
                        };

                        this.PossibleMoves.Add(bombMove);
                        this.BombMoves.Add(bombMove);
                        #endregion
                    }
                }
            }
        }

        bool hasElementBombMove = this.BombMoves.Exists(move =>
            move.SourceCell.TopCard.Data.WildBomb <= 0
            && move.SourceCell.TopCard.Data.IsBombOrCombo);
        if (hasElementBombMove)
        {
            foreach (var move in this.BombMoves)
            {
                move.Value -= move.SourceCell.TopCard.Data.WildBomb
                    * Elemento.Domain.LegacyAiPlayer.WildcardBombFlexibilityCost;
            }
        }

        #region Sort and Return best move

        if (this.isLookingForDiscard)
            this.PossibleMoves.Sort(SortByValueLowToHigh);
        else
            this.PossibleMoves.Sort(SortByValueHighToLow);

        if (this.PossibleMoves.Count > 0)
        {
            #region Debug Output
            if (Application.isEditor)
            {
                if (this.PossibleMoves[0].Value <= 0)
                {
                    int debugCount = Mathf.Min(this.PossibleMoves.Count, 5);
                    for (int i = 0; i < debugCount; i++)
                    {
                        var move = this.PossibleMoves[i];

                        Debug.Log($"---- Move {i}/{this.PossibleMoves.Count}    Value = {move.Value}    discard = {isLookingForDiscard}   source = {move.SourceCell.CellType}, {move.SourceCell.ElementType}    target = source = {move.TargetCell.CellType}, {move.TargetCell.ElementType}");
                    }
                }
                else
                {
                    string values = string.Empty;
                    for (int i = 0; i < this.PossibleMoves.Count; i++)
                    {
                        var move = this.PossibleMoves[i];

                        values += $"{move.Value},";

                        if (move.Value > 100)
                        {
                            Debug.Log($"BIGGIE Move#: {i}   move.Value = {move.Value}");//  move.type = {move.SourceCell.TopCard.Data.Points}");
                        }
                    }

                    Debug.Log($"---- PossibleMoves ({PossibleMoves.Count})   values = {values}     this.isLastMove = {this.isLastMove}");
                }
            }
            #endregion

            if (this.isLookingForDiscard)
            {
                this.DiscardMoves.Sort(SortByValueLowToHigh);

                var discardMove = this.PossibleMoves[0];//already sorted in low to high, use as fallback

                if (this.DiscardMoves.Count > 0)
                {
                    discardMove = this.DiscardMoves[0];

                    string values = string.Empty;
                    for (int i = 0; i < this.DiscardMoves.Count; i++)
                    {
                        var discMove = this.DiscardMoves[i];
                        values += $"{discMove.Value},";
                    }

                    Debug.Log($"---- DiscardMoves ({DiscardMoves.Count})   values = {values}     this.isLastMove = {this.isLastMove}");
                }

                discardMove.IsDiscard = true;

                return discardMove;
            }
            else
            {
                //normal move
                var selectedMove = GetMoveBasedOnAiDifficulty(this.PossibleMoves);
                return selectedMove;
            }
        }

        return null;

        #endregion

        #region Sort functions
        static int SortByValueHighToLow(Move inMoveA, Move inMoveB)
        {
            return inMoveB.Value.CompareTo(inMoveA.Value);
        }

        static int SortByValueLowToHigh(Move inMoveA, Move inMoveB)
        {
            return inMoveA.Value.CompareTo(inMoveB.Value);
        }
        #endregion

        #region Get AI Move Based On Difficulty
        Move GetMoveBasedOnAiDifficulty(List<Move> inPossibleMoves)
        {
            int selectedMoveIndex = 0;
            int firstIndex = 0;
            int lastIndex = 0;

            Move selectedMove = null;
            string values = string.Empty;

            if (SettingsManager.IN.AiDifficulty == SettingsManager.EAiDifficulty.Hard || inPossibleMoves.Count <= 1)
                selectedMove = inPossibleMoves[selectedMoveIndex];
            else
            {
                //easy
                firstIndex = Mathf.RoundToInt(inPossibleMoves.Count * .5f);
                lastIndex = inPossibleMoves.Count;

                if (SettingsManager.IN.AiDifficulty == SettingsManager.EAiDifficulty.Medium)
                {
                    firstIndex = Mathf.CeilToInt(inPossibleMoves.Count * .1f);
                    lastIndex = Mathf.FloorToInt(inPossibleMoves.Count * .5f);
                }

                selectedMoveIndex = UnityEngine.Random.Range(firstIndex, lastIndex);
                selectedMove = inPossibleMoves[selectedMoveIndex];
            }

            for (int i = 0; i < inPossibleMoves.Count; i++)
            {
                var move = inPossibleMoves[i];
                values += $"{move.Value},";
            }

            this.MoveOutputInfo = $"<color=black>Difficulty: {SettingsManager.IN.AiDifficulty})  # Possible Moves: {inPossibleMoves.Count} | First Index: {firstIndex}  Last Index: {lastIndex}    selectedMoveIndex = {selectedMoveIndex}    SelectedMove.Value: {selectedMove.Value}    Values: {values}</color>";

            return selectedMove;
        }
        #endregion

        #region Get Zone Status (Frozen, Destroyed)
        void GetZoneStatus()
        {
            this.numFrozenPlayerZones = 0;
            this.numFrozenOpponentZones = 0;
            this.numDestroyedPlayerZones = 0;
            this.numDestroyedOpponentZones = 0;

            this.numActivatedPlayerZones = 0;
            this.numActivatedOpponentZones = 0;
            this.highestPointPlayerZone = null;
            this.lowestPointPlayerZone = null;
            this.highestPointOpponentZone = null;
            this.lowestPointOpponentZone = null;

            this.highestFrozenPlayerZone = null;
            this.lowestFrozenPlayerZone = null;

            foreach (var board in GameManager.IN.PlayerBoards)
            {
                bool isOwnBoard = board.Equals(this.playerBoard);

                for (int i = 0; i < board.ElementZones.Length; i++)
                {
                    var zone = board.ElementZones[i];
                    var targetCell = zone.CardCell;

                    if (zone.IsFrozen)
                    {
                        if (isOwnBoard)
                        {
                            ++this.numFrozenPlayerZones;

                            if (this.highestFrozenPlayerZone == null || (zone.FrozenHealth > this.highestFrozenPlayerZone.FrozenHealth))
                            {
                                this.highestFrozenPlayerZone = zone;
                            }

                            if (this.numFrozenPlayerZones > 1 && (this.lowestFrozenPlayerZone == null || (zone.FrozenHealth < this.lowestFrozenPlayerZone.FrozenHealth)))
                            {
                                this.lowestFrozenPlayerZone = zone;
                            }
                        }
                        else
                        {
                            ++this.numFrozenOpponentZones;
                        }
                    }

                    if (zone.IsDestroyed)
                    {
                        if (isOwnBoard)
                            ++this.numDestroyedPlayerZones;
                        else
                            ++this.numDestroyedOpponentZones;
                    }
                    else
                    {
                        if (isOwnBoard)
                        {
                            if (this.highestHealthPlayerZone == null || (zone.Health > this.highestHealthPlayerZone.Health))
                            {
                                this.highestHealthPlayerZone = zone;
                            }

                            if ((this.lowestHealthPlayerZone == null || (this.lowestHealthPlayerZone != null && zone.Health < this.lowestHealthPlayerZone.Health)))
                            {
                                this.lowestHealthPlayerZone = zone;
                            }
                        }
                        else
                        {
                            if (this.highestHealthOpponentZone == null || (zone.Health > this.highestHealthOpponentZone.Health))
                            {
                                this.highestHealthOpponentZone = zone;
                            }

                            if ((this.lowestHealthOpponentZone == null || zone.Health < this.lowestHealthOpponentZone.Health))
                            {
                                this.lowestHealthOpponentZone = zone;
                            }
                        }
                    }

                    if (zone.IsActivated && !zone.IsFrozen)
                    {
                        if (isOwnBoard)
                        {
                            ++this.numActivatedPlayerZones;

                            if (this.highestPointPlayerZone == null || (zone.Points > this.highestPointPlayerZone.Points))
                            {
                                this.highestPointPlayerZone = zone;
                            }

                            if (this.numActivatedPlayerZones > 1 && (this.lowestPointPlayerZone == null || (zone.Points < this.lowestPointPlayerZone.Points)))
                            {
                                this.lowestPointPlayerZone = zone;
                            }
                        }
                        else
                        {
                            ++this.numActivatedOpponentZones;

                            if (this.highestPointOpponentZone == null || (zone.Points > this.highestPointOpponentZone.Points))
                            {
                                this.highestPointOpponentZone = zone;
                            }

                            if (this.numActivatedOpponentZones > 1 && (this.lowestPointOpponentZone == null || (zone.Points < this.lowestPointOpponentZone.Points)))
                            {
                                this.lowestPointOpponentZone = zone;
                            }
                        }
                    }
                }
            }
        }
        #endregion

        #region Decide Whether To Use Activated Zone
        void DecideWhetherToUseActivatedZone()
        {
            ECardType powerCardType = ECardType.None;

            foreach (var board in GameManager.IN.PlayerBoards)
            {
                bool isOwnBoard = board.Equals(this.playerBoard);

                if (isOwnBoard)
                {
                    for (int i = 0; i < board.ElementZones.Length; i++)
                    {
                        var zone = board.ElementZones[i];
                        var targetCell = zone.CardCell;

                        if (zone.IsActivated)
                        {
                            float useValue = CalculateUseActivateValue(board, i, out powerCardType);

                            var textColor = useValue > 0 ? "yellow" : "red";

                            Debug.Log($"<color={textColor}>DecideWhetherToUseActivatedZone()   useValue = {useValue}</color>");

                            var activatePowerMove = new Move
                            {
                                SourceCell = targetCell,
                                TargetCell = targetCell,
                                Value = useValue
                            };

                            if (!SettingsManager.IN.AllowSelectPowerCardType)
                                activatePowerMove.CardType = ECardType.None;

                            if (useValue >= 0)
                            {
                                activatePowerMove.ActivatePowerZone = zone;
                                activatePowerMove.CardType = powerCardType;
                                this.PossibleMoves.Add(activatePowerMove);
                                this.ActivatePowerMoves.Add(activatePowerMove);
                            }
                            else
                            {
                                activatePowerMove.IsDiscard = true;
                                this.DiscardMoves.Add(activatePowerMove);
                            }
                        }
                    }
                }

                float CalculateUseActivateValue(UIPlayerBoard inBoard, int inZoneIndex, out ECardType powerCardType)
                {
                    var possiblePowerCardMoves = new List<PossiblePowerCardMove>();

                    powerCardType = ECardType.None;
                    float useValue = 0;
                    var zone = board.ElementZones[inZoneIndex];
                    var targetCell = zone.CardCell;
                    var totalPointsAfterUseActivate = board.TotalPoints - zone.Points;
                    var pointsToWinAfterUseActivate = SettingsManager.IN.TotalPointsRequiredToWin - totalPointsAfterUseActivate;
                    var highestScoringMove = board.GetHighestScoringMoveValue();
                    //var percentToWin = (float)board.TotalPoints / SettingsManager.IN.TotalPointsRequiredToWin;
                    //var zonePointsUnderWinValue = SettingsManager.IN.TotalPointsRequiredToWin - (zone.Points + board.MultsInElementZone[inZoneIndex]);//TODO: some crazy calculation here

                    UIPlayerBoard opponentBoard = null;
                    foreach (var board in GameManager.IN.PlayerBoards)
                    {
                        if (!board.Equals(this.playerBoard))
                        {
                            opponentBoard = board;
                            break;
                        }
                    }

                    if (zone.IsFrozen || zone.IsDestroyed)
                    {
                        this.shouldSkipToEnd = true;
                        return -1;
                    }

                    var cardsOfElementInHandValue = (board.MultsInHand[inZoneIndex] * 3) + board.PointsInHand[inZoneIndex];
                    var cardsOfElementInHandModifier = (board.MultsInHand[inZoneIndex] * board.PointsInHand[inZoneIndex]);//could be 0

                    var highValue = 9999 + cardsOfElementInHandValue + cardsOfElementInHandModifier;

                    //EMERGENCY - about to lose because zones destroyed/frozen
                    if (this.numDestroyedPlayerZones + this.numFrozenPlayerZones >= 2)
                    {
                        if (this.numDestroyedPlayerZones >= 2)
                        {
                            if (board.HealCardsInHand <= 0)
                            {
                                if (this.numDestroyedPlayerZones == 3)
                                {
                                    this.shouldSkipToEnd = true;
                                    useValue = highValue;
                                    possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = ECardType.Heal, UseValue = useValue });
                                }
                                else if (this.numDestroyedPlayerZones == 2)
                                {
                                    //check points to win chances
                                    if (this.numMovesRemaining > 1)
                                    {
                                        if (highestScoringMove >= pointsToWinAfterUseActivate)
                                        {
                                            this.shouldSkipToEnd = true;
                                            powerCardType = ECardType.Heal;
                                            return highValue;//guaranteed win
                                        }

                                        if (pointsToWinAfterUseActivate - highestScoringMove < 5)
                                        {
                                            Debug.Log("A");

                                            if (zone.IsFrozen)
                                            {
                                                if (zone.Points > SettingsManager.IN.TotalPointsRequiredToWin * .5f)
                                                {
                                                    useValue = 50 + cardsOfElementInHandValue;
                                                    possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = ECardType.Lightning, UseValue = useValue });
                                                }
                                                else
                                                {
                                                    useValue = 50;
                                                    possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = ECardType.Heal, UseValue = useValue });
                                                }
                                            }
                                            else
                                            {
                                                useValue = 50;
                                                possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = ECardType.Heal, UseValue = useValue });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            //at least 2 zones frozen or destroyed, but not more than one is destroyed
                            var powerCardTypeToAdd = this.numDestroyedPlayerZones > 0 ? ECardType.Heal : ECardType.Lightning;

                            if (zone.Points < SettingsManager.IN.TotalPointsRequiredToWin * .9f)
                            {
                                useValue = 50 - board.MultsInElementZone[inZoneIndex];
                                Debug.Log($"Z    useValue = {useValue}");
                            }
                            else
                            {
                                useValue = 50 + cardsOfElementInHandValue;
                            }

                            possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = powerCardTypeToAdd, UseValue = useValue });
                        }
                    }

                    //EMERGENCY - about to lose because of opponent's high points
                    var pointsForOpponentToWin = SettingsManager.IN.TotalPointsRequiredToWin - opponentBoard.TotalFaceUpPoints;

                    if (!this.shouldSkipToEnd)
                    {
                        if (pointsForOpponentToWin < 10)
                        {
                            if (pointsForOpponentToWin < 5)
                            {
                                this.shouldSkipToEnd = true;
                                useValue = highValue;//high danger
                            }
                            else
                            {
                                //var highestScoringOpponentMove = opponentBoard.GetHighestScoringMoveValue();
                                var numPlayableZones = opponentBoard.GetNumPlayableZones();
                                if (numPlayableZones > 1)
                                    useValue = 50;//medium danger

                                Debug.Log("B");
                            }
                            possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = ECardType.Lightning, UseValue = useValue });
                        }

                        //more than one zone activated, so probably worth it
                        if (this.numActivatedPlayerZones > 1)
                        {
                            //figure out whether to select a Heal, Freeze or Lightning card
                            var powerCardTypeToAdd = ECardType.None;

                            if (SettingsManager.IN.AllowSelectPowerCardType)
                            {
                                var chanceForHeal = 50f;
                                var chanceForLightning = 50f;
                                var chanceForFreeze = 50f;

                                //modify chances based on various factors
                                chanceForHeal += numDestroyedPlayerZones * 10;

                                for (var j = 0; j < opponentBoard.ElementZones.Length; ++j)
                                {
                                    var opZone = opponentBoard.ElementZones[j];

                                    if (opZone.IsDestroyed)
                                    {
                                        chanceForHeal += 10;
                                    }
                                    else
                                    {
                                        if (opZone.IsFrozen)
                                        {
                                            chanceForFreeze -= 10;
                                        }
                                        else
                                        {
                                            if (opponentBoard.BiggestBombsInHand[inZoneIndex] > SettingsManager.IN.FrozenZoneHealth)
                                            {
                                                chanceForFreeze -= 10;
                                            }

                                            chanceForFreeze -= opponentBoard.BombsInHand[inZoneIndex] * 2;
                                        }

                                        if (opZone.Points + opZone.Health <= SettingsManager.IN.LightningDamage)
                                        {
                                            chanceForLightning += 15;
                                        }
                                        else
                                        {
                                            chanceForFreeze += 10;
                                            chanceForLightning -= opZone.Points + opZone.Health - SettingsManager.IN.LightningDamage;
                                        }
                                    }
                                }

                                //randomly select powerCardTypeToAdd
                                var randomRange = chanceForHeal + chanceForLightning + chanceForFreeze;
                                var rnd = UnityEngine.Random.Range(0, randomRange);

                                if (rnd < chanceForLightning)
                                    powerCardTypeToAdd = ECardType.Lightning;
                                else if (rnd < chanceForLightning + chanceForFreeze)
                                    powerCardTypeToAdd = ECardType.Freeze;
                                else
                                    powerCardTypeToAdd = ECardType.Heal;
                            }

                            //always choose to activate lowest point activateable zone
                            if (zone.Equals(this.lowestPointPlayerZone))
                            {
                                Debug.Log($"C   this.lowestPointPlayerZone.Points = {this.lowestPointPlayerZone.Points}");

                                var pointsAboveActivateMin = SettingsManager.IN.ElementZonePointsRequiredToActivate - this.lowestPointPlayerZone.Points;
                                if (pointsAboveActivateMin <= 2)
                                {
                                    this.shouldSkipToEnd = true;
                                    useValue = highValue;
                                }
                                else if (pointsAboveActivateMin <= 5)
                                {
                                    useValue += 99;
                                }
                                else if (pointsAboveActivateMin <= 10)
                                {
                                    useValue += 50;
                                }
                                else if (pointsAboveActivateMin <= 15)
                                {
                                    useValue += 5;
                                }

                                if (board.HealCardsInHand <= 0)
                                {
                                    useValue += (this.numDestroyedPlayerZones * 25);
                                }

                                if (board.LightningCardsInHand <= 0)
                                {
                                    useValue += (this.numFrozenPlayerZones * 20);
                                    useValue -= (board.WildBombsInHand * 3);
                                }
                            }

                            useValue += (zone.Points * .3f);
                            useValue += (board.MultsInElementZone[inZoneIndex]);

                            possiblePowerCardMoves.Add(new PossiblePowerCardMove { PowerCardType = powerCardTypeToAdd, UseValue = useValue });
                        }
                    }

                    //stop games from lasting so long
                    var turn = GameManager.IN.CurrentTurnNumber;

                    float amountToLessenUseValue = 0;

                    if (turn > 50)
                    {
                        useValue = 0;
                        powerCardType = ECardType.None;
                    }
                    else
                    {
                        if (turn > 45)
                            amountToLessenUseValue = turn * 2;
                        else if (turn > 40)
                            amountToLessenUseValue = turn * 1.5f;
                        else if (turn > 35)
                            amountToLessenUseValue = turn;
                        else if (turn > 30)
                            amountToLessenUseValue = turn * .5f;

                        useValue -= amountToLessenUseValue;

                        if (possiblePowerCardMoves.Count > 0)
                        {
                            for (var j = 0; j < possiblePowerCardMoves.Count; ++j)
                            {
                                var powerCardMove = possiblePowerCardMoves[j];
                                powerCardMove.UseValue -= amountToLessenUseValue;
                            }

                            possiblePowerCardMoves.Sort((move1, move2) => move2.UseValue.CompareTo(move1.UseValue));
                            powerCardType = possiblePowerCardMoves[0].PowerCardType;
                        }
                    }

                    if (useValue <= -1)
                    {
                        useValue = -1;
                        this.shouldSkipToEnd = false;
                    }

                    return useValue;
                }
            }
        }
        #endregion
    }
}
