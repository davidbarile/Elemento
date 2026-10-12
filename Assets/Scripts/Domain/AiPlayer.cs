using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public enum EAiDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    public interface IRandom
    {
        int NextInt(int inMaxExclusive);
        double NextDouble();
    }

    public interface IAiPlayer
    {
        MoveData ChooseMove(PlayerViewData inView, IRandom inRandom);
    }

    public sealed class RandomLegalAi : IAiPlayer
    {
        public MoveData ChooseMove(PlayerViewData inView, IRandom inRandom)
        {
            ValidateInputs(inView, inRandom);
            if (inView.LegalMoves.Count == 0)
                return null;

            return inView.LegalMoves[inRandom.NextInt(inView.LegalMoves.Count)].Clone();
        }

        private static void ValidateInputs(PlayerViewData inView, IRandom inRandom)
        {
            if (inView == null)
                throw new ArgumentNullException(nameof(inView));
            if (inRandom == null)
                throw new ArgumentNullException(nameof(inRandom));
        }
    }

    // Faithful engine-free port of AIMoveCalculator. It intentionally considers hand cards,
    // not movable zone-top cards, because UIPlayerBoard.GenerateUsefulData did the same.
    public class LegacyAiPlayer : IAiPlayer
    {
        private const float winningMoveValue = 99999f;

        private sealed class ScoredMove
        {
            public MoveData Move;
            public float Value;
            public int Order;
        }

        protected sealed class BoardAnalysis
        {
            public int TotalPoints;
            public int TotalFaceUpPoints;
            public int[] PointsInHand = new int[4];
            public int[] MultsInHand = new int[4];
            public int[] BombsInHand = new int[4];
            public int[] ShieldsInHand = new int[4];
            public int[] BiggestPointsInHand = new int[4];
            public int[] BiggestMultsInHand = new int[4];
            public int[] BiggestBombsInHand = new int[4];
            public int[] BiggestShieldsInHand = new int[4];
            public int[] MultsInZone = new int[4];
            public int[] ShieldsInZone = new int[4];
            public int WildBombsInHand;
            public int WildShieldsInHand;
            public int HealCardsInHand;
            public int LightningCardsInHand;
            public int FrozenZones;
            public int DestroyedZones;
            public int ActivatedZones;
            public int LowestFrozenZoneId = -1;
            public int LowestHealthZoneId = -1;
            public int HighestActivatedZoneId = -1;
            public int LowestActivatedZoneId = -1;
        }

        private struct PowerChoice
        {
            public EPowerCardType Type;
            public float Value;
        }

        public EAiDifficulty Difficulty { get; }

        public LegacyAiPlayer(EAiDifficulty inDifficulty)
        {
            this.Difficulty = inDifficulty;
        }

        public MoveData ChooseMove(PlayerViewData inView, IRandom inRandom)
        {
            ValidateInputs(inView, inRandom);
            if (inView.CurrentPlayerId != inView.Player.Id || inView.LegalMoves.Count == 0)
                return null;

            var own = AnalyzePlayer(inView.Player);
            var opponent = AnalyzeOpponent(inView.Opponents.Count > 0 ? inView.Opponents[0] : null);
            int numMovesRemaining = Math.Min(inView.Player.Hand.Count, inView.MovesRemaining);
            bool isLastMove = numMovesRemaining <= 1;
            bool isLookingForDiscard = numMovesRemaining <= 0
                && inView.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn;
            var candidates = new List<ScoredMove>();
            int order = 0;
            bool stop = false;

            AddActivationCandidates(inView, inRandom, own, opponent, numMovesRemaining, candidates, ref order, ref stop);

            if (!stop)
            {
                foreach (var legalMove in inView.LegalMoves)
                {
                    if (legalMove.Type != EMoveType.PlayCard || legalMove.SourceZoneId >= 0)
                        continue;

                    var card = FindCard(inView.Player.Hand, legalMove.CardId);
                    if (card == null || legalMove.TargetZoneId < 0)
                        continue;

                    bool isOwnBoard = legalMove.TargetPlayerId == inView.Player.Id;
                    var targetZone = FindTargetZone(inView, legalMove.TargetPlayerId, legalMove.TargetZoneId);
                    if (targetZone == null)
                        continue;

                    if (card.IsPowerCard)
                    {
                        float value = ScorePowerCard(card, targetZone, isOwnBoard, opponent);
                        if (value > 0f)
                            AddCandidate(candidates, legalMove, value, ref order);
                        continue;
                    }

                    bool resolvesAsBomb = HasBomb(card) && (!isOwnBoard || targetZone.IsFrozen);
                    if (resolvesAsBomb)
                    {
                        float bombValue;
                        if (TryScoreBomb(inView, card, targetZone, isOwnBoard, own, opponent, numMovesRemaining, out bombValue))
                            AddCandidate(candidates, legalMove, bombValue, ref order);
                        continue;
                    }

                    if (!isOwnBoard)
                        continue;

                    AddScoreCandidates(inView, card, targetZone, own, numMovesRemaining, isLastMove,
                        legalMove, candidates, ref order, ref stop);
                    if (stop)
                        break;
                }
            }

            if (isLookingForDiscard)
                return ChooseDiscard(inView, candidates);

            candidates.Sort(CompareHighToLow);
            if (candidates.Count == 0 || candidates[0].Value <= 0f)
                return FindEndTurn(inView);

            int selectedIndex = GetDifficultyIndex(candidates.Count, inRandom);
            return candidates[selectedIndex].Move.Clone();
        }

        private static void AddActivationCandidates(PlayerViewData inView, IRandom inRandom,
            BoardAnalysis inOwn, BoardAnalysis inOpponent, int inNumMovesRemaining,
            List<ScoredMove> inCandidates, ref int inOrder, ref bool inStop)
        {
            foreach (var zone in inView.Player.Zones)
            {
                if (!zone.IsActivated)
                    continue;

                EPowerCardType selectedType;
                float value = ScoreActivation(inView, inRandom, zone, inOwn, inOpponent,
                    inNumMovesRemaining, out selectedType, out inStop);
                if (value < 0f)
                    continue;

                var move = FindActivationMove(inView, zone.Id, selectedType);
                if (move != null)
                    AddCandidate(inCandidates, move, value, ref inOrder);
                if (inStop)
                    return;
            }
        }

        private void AddScoreCandidates(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, BoardAnalysis inOwn, int inNumMovesRemaining, bool inIsLastMove,
            MoveData inMove, List<ScoredMove> inCandidates, ref int inOrder, ref bool inStop)
        {
            int elementIndex = GetElementIndex(inZone.ElementType);
            float modifier = 1f + inZone.Points * .05f + inOwn.MultsInZone[elementIndex] * .5f;

            if (inView.Rules.ElementZoneStartingHealth > 0 && inZone.Cards.Count < 3)
            {
                float previousModifier = modifier;
                float healthPercent = inZone.MaxHealth <= 0 ? 0f : (float)inZone.Health / inZone.MaxHealth;
                if (healthPercent < .5f)
                    modifier += .5f - inZone.Cards.Count * .1f;
                else if (healthPercent < .1f)
                    modifier += 1f - inZone.Cards.Count * .1f;
                modifier = Math.Max(modifier, previousModifier);
            }

            int shieldsInHand = inOwn.ShieldsInHand[elementIndex] + inOwn.WildShieldsInHand;
            int maxPoints = Math.Max(GetValue(inCard.Points, elementIndex), inCard.WildPoints);
            if (maxPoints > 0)
            {
                bool moveWillWin = inOwn.TotalPoints + GetValue(inCard.Points, elementIndex)
                    >= inView.Rules.TotalPointsRequiredToWin;
                float pointPlayValue = 0f;
                bool hasPointPlayValue = !moveWillWin
                    && TryGetPointPlayValue(inCard, inZone, out pointPlayValue);
                if (hasPointPlayValue)
                {
                    AddCandidate(inCandidates, inMove, pointPlayValue, ref inOrder);
                }
                else
                {
                    float pointModifier = modifier;
                    if (moveWillWin)
                    {
                        pointModifier = winningMoveValue;
                        inStop = true;
                    }
                    else
                    {
                        float percentToWin = inView.Rules.ElementZonePointsRequiredToActivate == 0
                            ? 0f
                            : (float)inOwn.TotalPoints / inView.Rules.ElementZonePointsRequiredToActivate;
                        bool hasValidShieldsInHand = shieldsInHand <= 0;
                        if (!inIsLastMove || !hasValidShieldsInHand)
                        {
                            if (percentToWin < .5f)
                                pointModifier += percentToWin;
                            else if (percentToWin < .8f)
                                pointModifier += 1.2f * percentToWin;
                        }
                    }

                    if (inNumMovesRemaining > shieldsInHand && shieldsInHand > 0)
                        pointModifier += inNumMovesRemaining;

                    float priority = inOwn.MultsInZone[elementIndex] * .02f
                        + inOwn.MultsInHand[elementIndex] * .02f
                        + inZone.Points * .01f
                        + inOwn.PointsInHand[elementIndex] * .01f;
                    AddCandidate(inCandidates, inMove,
                        (maxPoints + inOwn.MultsInZone[elementIndex] * .3f) * pointModifier + priority,
                        ref inOrder);
                }
            }

            if (inStop)
                return;

            int maxMultiplier = Math.Max(GetValue(inCard.Multipliers, elementIndex), inCard.WildMultiplier);
            if (maxMultiplier > 0)
            {
                float multiplierModifier = 3f;
                bool moveWillWin = inOwn.TotalPoints - inZone.Points
                    + inZone.Points * GetValue(inCard.Multipliers, elementIndex)
                    >= inView.Rules.TotalPointsRequiredToWin;
                if (moveWillWin)
                {
                    multiplierModifier = winningMoveValue;
                    inStop = true;
                }
                else
                {
                    float percentToWin = inView.Rules.ElementZonePointsRequiredToActivate == 0
                        ? 0f
                        : (float)inOwn.TotalPoints / inView.Rules.ElementZonePointsRequiredToActivate;
                    bool hasValidShieldsInHand = shieldsInHand <= 0;
                    if (inNumMovesRemaining > 1 || !hasValidShieldsInHand)
                    {
                        if (percentToWin < .5f)
                            multiplierModifier += 2f * percentToWin;
                        else if (percentToWin < .8f)
                            multiplierModifier += 3f * percentToWin;
                    }
                }

                if (inNumMovesRemaining > shieldsInHand && shieldsInHand > 0)
                    multiplierModifier += inNumMovesRemaining * 2f;

                float priority = inOwn.MultsInZone[elementIndex] * .05f
                    + inOwn.MultsInHand[elementIndex] * .05f
                    + inZone.Points * .02f
                    + inOwn.PointsInHand[elementIndex] * .02f;
                AddCandidate(inCandidates, inMove,
                    (maxMultiplier + inZone.Points * .3f) * multiplierModifier + priority,
                    ref inOrder);
            }

            if (inStop)
                return;

            int maxShield = Math.Max(GetValue(inCard.Shields, elementIndex), inCard.WildShield);
            if (maxShield <= 0)
                return;

            float shieldModifier = 1f;
            if (inZone.Cards.Count > 0)
            {
                var topCard = inZone.Cards[inZone.Cards.Count - 1];
                int topShield = Math.Max(GetValue(topCard.Shields, elementIndex), topCard.WildShield);
                int pointsAboveShield = 0;
                if (topShield <= maxShield)
                {
                    int startIndex = topShield > 0 ? inZone.Cards.Count - 2 : inZone.Cards.Count - 1;
                    if (startIndex > 0)
                    {
                        for (int i = startIndex; i >= 0; --i)
                        {
                            var zoneCard = inZone.Cards[i];
                            int points = GetValue(zoneCard.Points, elementIndex) + zoneCard.WildPoints;
                            int multiplier = GetValue(zoneCard.Multipliers, elementIndex) + zoneCard.WildMultiplier;
                            pointsAboveShield += points;
                            if (multiplier > 1)
                                pointsAboveShield *= multiplier;
                            if (HasShield(zoneCard))
                                break;
                        }
                    }
                    shieldModifier += pointsAboveShield;
                }

                if (inNumMovesRemaining <= shieldsInHand)
                {
                    shieldModifier += inZone.Points * .1f;
                    shieldModifier += inOwn.MultsInZone[elementIndex];
                    shieldModifier += 5 - inNumMovesRemaining;
                }
            }
            else
            {
                shieldModifier += inZone.MaxHealth - inZone.Health;
                if (inNumMovesRemaining <= 2)
                    shieldModifier += 3 - inNumMovesRemaining;
                if (maxShield >= inOwn.BiggestShieldsInHand[elementIndex])
                    shieldModifier += 3 - inNumMovesRemaining;
            }

            AddCandidate(inCandidates, inMove, maxShield * shieldModifier, ref inOrder);
        }

        private static float ScorePowerCard(CardInstanceData inCard, ZoneStateData inZone,
            bool inIsOwnBoard, BoardAnalysis inOpponent)
        {
            float value = 100f;
            if (inIsOwnBoard)
            {
                if (inCard.WildZoneHeal > 0)
                {
                    if (inZone.Health >= inZone.MaxHealth || inZone.IsFrozen || inZone.Cards.Count > 0)
                        return 0f;
                    if (inZone.IsDestroyed)
                        value += 10f;
                    value += inZone.MaxHealth - inZone.Health;
                    return value;
                }

                return (inZone.MaxHealth - inZone.Health) * 10f;
            }

            if (inCard.WildFreeze > 0)
            {
                if (inZone.FrozenHealth > 2 || inZone.IsDestroyed)
                    return inZone.IsDestroyed ? 0f : 1f;
                if (inZone.Id == inOpponent.HighestActivatedZoneId)
                    value += 10f;
                value += inOpponent.MultsInZone[GetElementIndex(inZone.ElementType)] * 2f;
            }

            if (inCard.WildLightning > 0 && (inZone.IsDestroyed || inZone.IsFrozen || inZone.Health < 3))
                value = 0f;
            if (inCard.WildZoneHeal > 0)
                value = 0f;

            return value;
        }

        protected virtual bool TryGetPointPlayValue(CardInstanceData inCard, ZoneStateData inZone,
            out float outValue)
        {
            outValue = 0f;
            return false;
        }

        protected virtual bool TryScoreBomb(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, bool inIsOwnBoard, BoardAnalysis inOwn, BoardAnalysis inOpponent,
            int inNumMovesRemaining, out float outValue)
        {
            int elementIndex = GetElementIndex(inZone.ElementType);
            int bombPoints = Math.Max(GetValue(inCard.Bombs, elementIndex), inCard.WildBomb);
            if (bombPoints <= 0)
            {
                outValue = 0f;
                return false;
            }

            if (inZone.Cards.Count > 0)
            {
                var topCard = inZone.Cards[inZone.Cards.Count - 1];
                int topShield = Math.Max(GetValue(topCard.Shields, elementIndex), topCard.WildShield);
                if (bombPoints < topShield)
                {
                    outValue = 0f;
                    return false;
                }
            }

            float modifier = 1f;
            if (!inIsOwnBoard)
            {
                if (inView.Rules.ElementZoneStartingHealth > 0 || inZone.Cards.Count > 0)
                {
                    modifier += inOwn.FrozenZones * 2f;
                    modifier += inOwn.DestroyedZones * 2f;
                    if (inZone.Cards.Count == 0)
                    {
                        if (inZone.Health <= bombPoints)
                            modifier += 5f;
                        else if (inZone.Id == inOpponent.LowestHealthZoneId)
                            modifier += 2f;
                    }
                    else
                    {
                        modifier += inZone.Points * .1f;
                        modifier += inOpponent.MultsInZone[elementIndex] * 3f;

                        int pointsAboveShield = 0;
                        int multsAboveShield = 0;
                        for (int i = inZone.Cards.Count - 1; i >= 0; --i)
                        {
                            var zoneCard = inZone.Cards[i];
                            int points = GetValue(zoneCard.Points, elementIndex) + zoneCard.WildPoints;
                            int multiplier = GetValue(zoneCard.Multipliers, elementIndex) + zoneCard.WildMultiplier;
                            multsAboveShield += multiplier;
                            pointsAboveShield += points;
                            if (multiplier > 1)
                                pointsAboveShield *= multiplier;
                            if (HasShield(zoneCard))
                                break;
                        }

                        modifier += pointsAboveShield;
                        modifier += multsAboveShield * .2f;
                        if (inNumMovesRemaining > 2)
                            modifier += 2f;
                    }
                }
            }
            else if (inZone.IsFrozen)
            {
                if (inZone.FrozenHealth <= bombPoints)
                    modifier += 5f;
                else
                    modifier += ((inView.Rules.FrozenZoneHealth - inZone.FrozenHealth) - bombPoints) * .3f;
                if (inZone.Id == inOwn.LowestFrozenZoneId)
                    modifier += 2f;
                modifier += inOwn.FrozenZones * 2f;
                modifier += inOwn.DestroyedZones * 2f;
            }

            modifier *= .3f;
            modifier = Math.Max(1f, modifier);
            outValue = bombPoints * modifier;
            return true;
        }

        private static float ScoreActivation(PlayerViewData inView, IRandom inRandom,
            ZoneStateData inZone, BoardAnalysis inOwn, BoardAnalysis inOpponent,
            int inNumMovesRemaining, out EPowerCardType outType, out bool outStop)
        {
            outType = EPowerCardType.None;
            outStop = false;
            if (inZone.IsFrozen || inZone.IsDestroyed)
            {
                outStop = true;
                return -1f;
            }

            int elementIndex = GetElementIndex(inZone.ElementType);
            int cardsOfElementValue = inOwn.MultsInHand[elementIndex] * 3 + inOwn.PointsInHand[elementIndex];
            int cardsOfElementModifier = inOwn.MultsInHand[elementIndex] * inOwn.PointsInHand[elementIndex];
            float highValue = 9999f + cardsOfElementValue + cardsOfElementModifier;
            float useValue = 0f;
            var choices = new List<PowerChoice>();
            int totalAfterActivation = inOwn.TotalPoints - inZone.Points;
            int pointsNeeded = inView.Rules.TotalPointsRequiredToWin - totalAfterActivation;
            int highestScoringMove = GetHighestScoringMove(inView.Player, inOwn);

            if (inOwn.DestroyedZones + inOwn.FrozenZones >= 2)
            {
                if (inOwn.DestroyedZones >= 2 && inOwn.HealCardsInHand <= 0)
                {
                    if (inOwn.DestroyedZones == 3)
                    {
                        outStop = true;
                        useValue = highValue;
                        choices.Add(new PowerChoice { Type = EPowerCardType.Heal, Value = useValue });
                    }
                    else if (inOwn.DestroyedZones == 2 && inNumMovesRemaining > 1)
                    {
                        if (highestScoringMove >= pointsNeeded)
                        {
                            outStop = true;
                            outType = EPowerCardType.Heal;
                            return highValue;
                        }
                        if (pointsNeeded - highestScoringMove < 5)
                        {
                            useValue = 50f;
                            choices.Add(new PowerChoice { Type = EPowerCardType.Heal, Value = useValue });
                        }
                    }
                }
                else if (inOwn.DestroyedZones < 2)
                {
                    var type = inOwn.DestroyedZones > 0 ? EPowerCardType.Heal : EPowerCardType.Lightning;
                    useValue = inZone.Points < inView.Rules.TotalPointsRequiredToWin * .9f
                        ? 50f - inOwn.MultsInZone[elementIndex]
                        : 50f + cardsOfElementValue;
                    choices.Add(new PowerChoice { Type = type, Value = useValue });
                }
            }

            int pointsForOpponentToWin = inView.Rules.TotalPointsRequiredToWin - inOpponent.TotalFaceUpPoints;
            if (!outStop)
            {
                if (pointsForOpponentToWin < 10)
                {
                    if (pointsForOpponentToWin < 5)
                    {
                        outStop = true;
                        useValue = highValue;
                    }
                    else if (GetPlayableZoneCount(inView.Opponents.Count > 0 ? inView.Opponents[0].Zones : null) > 1)
                    {
                        useValue = 50f;
                    }
                    choices.Add(new PowerChoice { Type = EPowerCardType.Lightning, Value = useValue });
                }

                if (inOwn.ActivatedZones > 1)
                {
                    EPowerCardType type = EPowerCardType.None;
                    if (inView.Rules.AllowSelectPowerCardType)
                    {
                        float heal = 50f + inOwn.DestroyedZones * 10f;
                        float lightning = 50f;
                        float freeze = 50f;
                        foreach (var zone in inView.Opponents[0].Zones)
                        {
                            if (zone.IsDestroyed)
                            {
                                heal += 10f;
                                continue;
                            }

                            if (zone.IsFrozen)
                                freeze -= 10f;
                            else
                            {
                                int unseenBombCount;
                                int unseenBombMax;
                                GetUnseenBombPotential(inView, inZone.ElementType, out unseenBombCount, out unseenBombMax);
                                if (unseenBombMax > inView.Rules.FrozenZoneHealth)
                                    freeze -= 10f;
                                freeze -= unseenBombCount * 2f;
                            }

                            if (zone.Points + zone.Health <= inView.Rules.LightningDamage)
                                lightning += 15f;
                            else
                            {
                                freeze += 10f;
                                lightning -= zone.Points + zone.Health - inView.Rules.LightningDamage;
                            }
                        }

                        float range = heal + lightning + freeze;
                        double randomValue = range <= 0f ? 0.0 : inRandom.NextDouble() * range;
                        if (randomValue < lightning)
                            type = EPowerCardType.Lightning;
                        else if (randomValue < lightning + freeze)
                            type = EPowerCardType.Freeze;
                        else
                            type = EPowerCardType.Heal;
                    }

                    if (inZone.Id == inOwn.LowestActivatedZoneId)
                    {
                        int pointsAboveMinimum = inView.Rules.ElementZonePointsRequiredToActivate - inZone.Points;
                        if (pointsAboveMinimum <= 2)
                        {
                            outStop = true;
                            useValue = highValue;
                        }
                        else if (pointsAboveMinimum <= 5)
                            useValue += 99f;
                        else if (pointsAboveMinimum <= 10)
                            useValue += 50f;
                        else if (pointsAboveMinimum <= 15)
                            useValue += 5f;

                        if (inOwn.HealCardsInHand <= 0)
                            useValue += inOwn.DestroyedZones * 25f;
                        if (inOwn.LightningCardsInHand <= 0)
                        {
                            useValue += inOwn.FrozenZones * 20f;
                            useValue -= inOwn.WildBombsInHand * 3f;
                        }
                    }

                    useValue += inZone.Points * .3f;
                    useValue += inOwn.MultsInZone[elementIndex];
                    choices.Add(new PowerChoice { Type = type, Value = useValue });
                }
            }

            if (inView.TurnNumber > 50)
            {
                useValue = 0f;
                outType = EPowerCardType.None;
            }
            else
            {
                float penalty = 0f;
                if (inView.TurnNumber > 45)
                    penalty = inView.TurnNumber * 2f;
                else if (inView.TurnNumber > 40)
                    penalty = inView.TurnNumber * 1.5f;
                else if (inView.TurnNumber > 35)
                    penalty = inView.TurnNumber;
                else if (inView.TurnNumber > 30)
                    penalty = inView.TurnNumber * .5f;
                useValue -= penalty;

                if (choices.Count > 0)
                {
                    choices.Sort((inFirst, inSecond) => inSecond.Value.CompareTo(inFirst.Value));
                    outType = choices[0].Type;
                }
            }

            if (useValue <= -1f)
            {
                outStop = false;
                return -1f;
            }

            return useValue;
        }

        private static BoardAnalysis AnalyzePlayer(PlayerStateData inPlayer)
        {
            var analysis = AnalyzeZones(inPlayer.Zones);
            foreach (var card in inPlayer.Hand)
            {
                for (int i = 0; i < 4; ++i)
                {
                    analysis.PointsInHand[i] += GetValue(card.Points, i);
                    analysis.MultsInHand[i] += GetValue(card.Multipliers, i);
                    analysis.BombsInHand[i] += GetValue(card.Bombs, i);
                    analysis.ShieldsInHand[i] += GetValue(card.Shields, i);
                    analysis.BiggestPointsInHand[i] = Math.Max(analysis.BiggestPointsInHand[i], Math.Max(GetValue(card.Points, i), card.WildPoints));
                    analysis.BiggestMultsInHand[i] = Math.Max(analysis.BiggestMultsInHand[i], Math.Max(GetValue(card.Multipliers, i), card.WildMultiplier));
                    analysis.BiggestBombsInHand[i] = Math.Max(analysis.BiggestBombsInHand[i], Math.Max(GetValue(card.Bombs, i), card.WildBomb));
                    analysis.BiggestShieldsInHand[i] = Math.Max(analysis.BiggestShieldsInHand[i], Math.Max(GetValue(card.Shields, i), card.WildShield));
                }

                analysis.WildBombsInHand += card.WildBomb;
                analysis.WildShieldsInHand += card.WildShield;
                if (card.WildZoneHeal > 0)
                    ++analysis.HealCardsInHand;
                else if (card.WildLightning > 0)
                    ++analysis.LightningCardsInHand;
            }
            return analysis;
        }

        private static BoardAnalysis AnalyzeOpponent(OpponentViewData inOpponent)
        {
            return inOpponent == null ? new BoardAnalysis() : AnalyzeZones(inOpponent.Zones);
        }

        private static BoardAnalysis AnalyzeZones(List<ZoneStateData> inZones)
        {
            var analysis = new BoardAnalysis();
            int activatedSeen = 0;
            int frozenSeen = 0;
            foreach (var zone in inZones)
            {
                analysis.TotalPoints += zone.Points;
                analysis.TotalFaceUpPoints += zone.FaceUpPoints;
                int elementIndex = GetElementIndex(zone.ElementType);
                foreach (var card in zone.Cards)
                {
                    analysis.MultsInZone[elementIndex] += GetValue(card.Multipliers, elementIndex) + card.WildMultiplier;
                    analysis.ShieldsInZone[elementIndex] += GetValue(card.Shields, elementIndex) + card.WildShield;
                }

                if (zone.IsFrozen)
                {
                    ++analysis.FrozenZones;
                    ++frozenSeen;
                    if (frozenSeen > 1 && (analysis.LowestFrozenZoneId < 0
                        || zone.FrozenHealth < FindZone(inZones, analysis.LowestFrozenZoneId).FrozenHealth))
                    {
                        analysis.LowestFrozenZoneId = zone.Id;
                    }
                }
                if (zone.IsDestroyed)
                    ++analysis.DestroyedZones;
                else if (analysis.LowestHealthZoneId < 0
                    || zone.Health < FindZone(inZones, analysis.LowestHealthZoneId).Health)
                    analysis.LowestHealthZoneId = zone.Id;

                if (zone.IsActivated && !zone.IsFrozen)
                {
                    ++analysis.ActivatedZones;
                    ++activatedSeen;
                    if (analysis.HighestActivatedZoneId < 0
                        || zone.Points > FindZone(inZones, analysis.HighestActivatedZoneId).Points)
                        analysis.HighestActivatedZoneId = zone.Id;
                    if (activatedSeen > 1 && (analysis.LowestActivatedZoneId < 0
                        || zone.Points < FindZone(inZones, analysis.LowestActivatedZoneId).Points))
                        analysis.LowestActivatedZoneId = zone.Id;
                }
            }
            return analysis;
        }

        protected virtual int GetDifficultyIndex(int inCount, IRandom inRandom)
        {
            if (this.Difficulty == EAiDifficulty.Hard || inCount <= 1)
                return 0;

            int firstIndex = (int)Math.Round(inCount * .5, MidpointRounding.ToEven);
            int lastIndex = inCount;
            if (this.Difficulty == EAiDifficulty.Medium)
            {
                firstIndex = (int)Math.Ceiling(inCount * .1);
                lastIndex = (int)Math.Floor(inCount * .5);
            }

            if (lastIndex <= firstIndex)
                return Math.Min(firstIndex, inCount - 1);
            return firstIndex + inRandom.NextInt(lastIndex - firstIndex);
        }

        private static MoveData ChooseDiscard(PlayerViewData inView, List<ScoredMove> inCandidates)
        {
            inCandidates.Sort(CompareLowToHigh);
            string preferredCardId = inCandidates.Count > 0 ? inCandidates[0].Move.CardId : null;
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type == EMoveType.DiscardCard && move.SourceZoneId < 0
                    && (preferredCardId == null || move.CardId == preferredCardId))
                    return move.Clone();
            }
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type == EMoveType.DiscardCard && move.SourceZoneId < 0)
                    return move.Clone();
            }
            return null;
        }

        private static MoveData FindEndTurn(PlayerViewData inView)
        {
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type == EMoveType.EndTurn)
                    return move.Clone();
            }
            return null;
        }

        private static MoveData FindActivationMove(PlayerViewData inView, int inZoneId, EPowerCardType inType)
        {
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type == EMoveType.ActivateZone && move.SourceZoneId == inZoneId
                    && move.PowerCardType == inType)
                    return move;
            }
            return null;
        }

        private static ZoneStateData FindTargetZone(PlayerViewData inView, int inPlayerId, int inZoneId)
        {
            if (inPlayerId == inView.Player.Id)
                return FindZone(inView.Player.Zones, inZoneId);
            foreach (var opponent in inView.Opponents)
            {
                if (opponent.Id == inPlayerId)
                    return FindZone(opponent.Zones, inZoneId);
            }
            return null;
        }

        private static ZoneStateData FindZone(List<ZoneStateData> inZones, int inZoneId)
        {
            foreach (var zone in inZones)
            {
                if (zone.Id == inZoneId)
                    return zone;
            }
            return null;
        }

        private static CardInstanceData FindCard(List<CardInstanceData> inCards, string inCardId)
        {
            foreach (var card in inCards)
            {
                if (card.Id == inCardId)
                    return card;
            }
            return null;
        }

        private static int GetHighestScoringMove(PlayerStateData inPlayer, BoardAnalysis inAnalysis)
        {
            int highest = 0;
            foreach (var zone in inPlayer.Zones)
            {
                if (zone.IsDestroyed || zone.IsFrozen)
                    continue;
                int index = GetElementIndex(zone.ElementType);
                if (inAnalysis.BiggestMultsInHand[index] > 0)
                    highest = Math.Max(highest, inAnalysis.BiggestMultsInHand[index] * zone.Points);
                highest = Math.Max(highest, inAnalysis.BiggestPointsInHand[index] * zone.Points);
            }
            return highest;
        }

        private static int GetPlayableZoneCount(List<ZoneStateData> inZones)
        {
            if (inZones == null)
                return 0;
            int count = 0;
            foreach (var zone in inZones)
            {
                if (!zone.IsDestroyed && !zone.IsFrozen)
                    ++count;
            }
            return count;
        }

        private static void GetUnseenBombPotential(PlayerViewData inView, EElementType inElementType,
            out int outCount, out int outMaximum)
        {
            int elementIndex = GetElementIndex(inElementType);
            var visibleCounts = new Dictionary<int, int>();
            CountVisibleDefinitions(inView.Player.Hand, visibleCounts);
            foreach (var zone in inView.Player.Zones)
                CountVisibleDefinitions(zone.Cards, visibleCounts);
            foreach (var opponent in inView.Opponents)
            {
                foreach (var zone in opponent.Zones)
                    CountVisibleDefinitions(zone.Cards, visibleCounts);
            }

            outCount = 0;
            outMaximum = 0;
            foreach (var definition in DeckDefinitionData.NormalCards)
            {
                int strength = Math.Max(GetValue(definition.Bombs, elementIndex), definition.WildBomb);
                if (strength <= 0 || definition.ChoiceCount <= 0)
                    continue;
                int visible;
                visibleCounts.TryGetValue(definition.DefinitionId, out visible);
                int remaining = Math.Max(0, definition.ChoiceCount - visible);
                outCount += remaining;
                if (remaining > 0)
                    outMaximum = Math.Max(outMaximum, strength);
            }
        }

        private static void CountVisibleDefinitions(List<CardInstanceData> inCards, Dictionary<int, int> inCounts)
        {
            foreach (var card in inCards)
            {
                int count;
                inCounts.TryGetValue(card.DefinitionId, out count);
                inCounts[card.DefinitionId] = count + 1;
            }
        }

        private static void AddCandidate(List<ScoredMove> inCandidates, MoveData inMove,
            float inValue, ref int inOrder)
        {
            inCandidates.Add(new ScoredMove { Move = inMove, Value = inValue, Order = inOrder++ });
        }

        private static int CompareHighToLow(ScoredMove inFirst, ScoredMove inSecond)
        {
            int valueComparison = inSecond.Value.CompareTo(inFirst.Value);
            return valueComparison != 0 ? valueComparison : inFirst.Order.CompareTo(inSecond.Order);
        }

        private static int CompareLowToHigh(ScoredMove inFirst, ScoredMove inSecond)
        {
            int valueComparison = inFirst.Value.CompareTo(inSecond.Value);
            return valueComparison != 0 ? valueComparison : inFirst.Order.CompareTo(inSecond.Order);
        }

        private static bool HasBomb(CardInstanceData inCard)
        {
            if (inCard.WildBomb > 0)
                return true;
            for (int i = 0; i < inCard.Bombs.Length; ++i)
            {
                if (inCard.Bombs[i] > 0)
                    return true;
            }
            return false;
        }

        private static bool HasShield(CardInstanceData inCard)
        {
            if (inCard.WildShield > 0)
                return true;
            for (int i = 0; i < inCard.Shields.Length; ++i)
            {
                if (inCard.Shields[i] > 0)
                    return true;
            }
            return false;
        }

        private static int GetElementIndex(EElementType inElementType)
        {
            int index = (int)inElementType - 1;
            if (index < 0 || index > 3)
                throw new ArgumentOutOfRangeException(nameof(inElementType));
            return index;
        }

        private static int GetValue(int[] inValues, int inIndex)
        {
            return inValues == null || inIndex < 0 || inIndex >= inValues.Length ? 0 : inValues[inIndex];
        }

        private static void ValidateInputs(PlayerViewData inView, IRandom inRandom)
        {
            if (inView == null)
                throw new ArgumentNullException(nameof(inView));
            if (inRandom == null)
                throw new ArgumentNullException(nameof(inRandom));
            if (inView.Player == null)
                throw new ArgumentException("The player view has no player state.", nameof(inView));
            if (inView.Rules == null)
                throw new ArgumentException("The player view has no rules.", nameof(inView));
        }
    }
}
