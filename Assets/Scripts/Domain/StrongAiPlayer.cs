using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public sealed class StrongAiOptions
    {
        public bool UseEffectBasedBombScoring = true;
        public EAiDifficulty Difficulty = EAiDifficulty.Hard;
        public float Temperature;
    }

    public sealed class StrongAiPlayer : LegacyAiPlayer, IAiPlayer
    {
        private const float zoneKillBonus = 5f;
        private const float multiplierStripBonus = 3f;
        private const float mediumTemperature = 1f;
        private const float easyTemperature = 2f;
        private const float reorderPenalty = .35f;
        private const float b2MoveBonus = 20f;
        private const float winningPlanBonus = 10000f;
        private const float antiStallProgressPerTurn = .05f;
        private const int maxPlanDepth = 3;
        private const int beamWidth = 6;
        private const int candidateWidth = 7;
        private const int maxReorderMoves = 3;
        private const int antiStallTurn = 100;

        private sealed class PlanNode
        {
            public GameStateData State;
            public MoveData FirstMove;
            public int MovesRemaining;
            public int ReorderMoves;
            public float Value;
            public float OrderingValue;
            public bool FirstMoveIsB2;
        }

        private sealed class PlanCandidate
        {
            public MoveData Move;
            public GameStateData State;
            public bool IsReorder;
            public float PruneValue;
            public float Value;
            public float OrderingValue;
        }

        private readonly bool useEffectBasedBombScoring;
        private readonly float temperature;

        public StrongAiPlayer()
            : this(new StrongAiOptions())
        {
        }

        public StrongAiPlayer(StrongAiOptions inOptions)
            : base(inOptions == null ? EAiDifficulty.Hard : inOptions.Difficulty)
        {
            if (inOptions == null)
                throw new ArgumentNullException(nameof(inOptions));
            if (!Enum.IsDefined(typeof(EAiDifficulty), inOptions.Difficulty))
                throw new ArgumentOutOfRangeException(nameof(inOptions));
            if (inOptions.Temperature < 0f)
                throw new ArgumentOutOfRangeException(nameof(inOptions));

            this.useEffectBasedBombScoring = inOptions.UseEffectBasedBombScoring;
            this.temperature = ResolveTemperature(inOptions);
        }

        public new MoveData ChooseMove(PlayerViewData inView, IRandom inRandom)
        {
            var b2Move = base.ChooseMove(inView, inRandom);
            if (b2Move == null || !this.useEffectBasedBombScoring || this.temperature > 0f)
                return b2Move;

            var plannedMove = ChoosePlannedMove(inView, b2Move);
            return plannedMove ?? b2Move;
        }

        private MoveData ChoosePlannedMove(PlayerViewData inView, MoveData inB2Move)
        {
            var simulator = new GameSimulator();
            var state = CreatePlanningState(inView);
            var b2StateMove = FindMatchingMove(simulator.GetLegalMoves(state, inView.Player.Id), inB2Move);
            if (b2StateMove != null)
            {
                var b2State = state.Clone();
                simulator.ApplyMove(b2State, b2StateMove);
                if (b2State.WinnerPlayerId == inView.Player.Id)
                    return inB2Move.Clone();
            }

            int movesRemaining = inView.Rules.MovesPerTurn > 0
                ? Math.Max(0, inView.MovesRemaining)
                : maxPlanDepth;
            var root = new PlanNode
            {
                State = state,
                MovesRemaining = movesRemaining,
                Value = EvaluateEndOfTurn(inView, state, 0),
                OrderingValue = EvaluatePileOrdering(inView, state)
            };
            var frontier = new List<PlanNode> { root };
            PlanNode best = null;

            for (int depth = 0; depth < maxPlanDepth && frontier.Count > 0; ++depth)
            {
                var next = new List<PlanNode>();
                foreach (var node in frontier)
                {
                    var candidates = GetPlanCandidates(inView, simulator, node);
                    int count = Math.Min(beamWidth, candidates.Count);
                    for (int i = 0; i < count; ++i)
                    {
                        var candidate = candidates[i];
                        var firstMove = node.FirstMove;
                        if (firstMove == null)
                            firstMove = FindMatchingMove(inView.LegalMoves, candidate.Move);
                        if (firstMove == null)
                            continue;

                        int reorderMoves = node.ReorderMoves + (candidate.IsReorder ? 1 : 0);
                        var child = new PlanNode
                        {
                            State = candidate.State,
                            FirstMove = firstMove,
                            MovesRemaining = node.MovesRemaining
                                - (candidate.Move.Type == EMoveType.PlayCard ? 1 : 0),
                            ReorderMoves = reorderMoves,
                            Value = candidate.Value,
                            OrderingValue = candidate.OrderingValue,
                            FirstMoveIsB2 = node.FirstMove == null
                                ? MovesMatch(firstMove, inB2Move)
                                : node.FirstMoveIsB2
                        };
                        next.Add(child);
                        if (best == null || ComparePlanNodes(child, best) < 0)
                            best = child;
                    }
                }

                next.Sort(ComparePlanNodes);
                if (next.Count > beamWidth)
                    next.RemoveRange(beamWidth, next.Count - beamWidth);
                frontier = next;
            }

            return best == null ? null : best.FirstMove.Clone();
        }

        private static int ComparePlanNodes(PlanNode inLeft, PlanNode inRight)
        {
            int valueComparison = inRight.Value.CompareTo(inLeft.Value);
            if (valueComparison != 0)
                return valueComparison;
            int orderingComparison = inRight.OrderingValue.CompareTo(inLeft.OrderingValue);
            if (orderingComparison != 0)
                return orderingComparison;
            return inRight.FirstMoveIsB2.CompareTo(inLeft.FirstMoveIsB2);
        }

        private List<PlanCandidate> GetPlanCandidates(PlayerViewData inSourceView,
            GameSimulator inSimulator, PlanNode inNode)
        {
            var planningView = CreatePlanningView(inSourceView, inNode.State, inNode.MovesRemaining);
            var legalMoves = inSimulator.GetLegalMoves(inNode.State, inSourceView.Player.Id);
            var prunedMoves = new List<PlanCandidate>();
            foreach (var move in legalMoves)
            {
                if (move.Type == EMoveType.PlayCard && inNode.MovesRemaining <= 0)
                    continue;
                if (move.Type != EMoveType.PlayCard && move.Type != EMoveType.ActivateZone)
                    continue;
                if (move.Type == EMoveType.ActivateZone && move.PowerCardType == EPowerCardType.None)
                    continue;

                bool isReorder = move.Type == EMoveType.PlayCard && move.SourceZoneId >= 0;
                if (isReorder && inNode.ReorderMoves >= maxReorderMoves)
                    continue;
                if (isReorder && inSourceView.TurnNumber >= antiStallTurn)
                    continue;
                if (inNode.FirstMove == null && FindMatchingMove(inSourceView.LegalMoves, move) == null)
                    continue;

                prunedMoves.Add(new PlanCandidate
                {
                    Move = move,
                    IsReorder = isReorder,
                    PruneValue = GetB2PruneValue(planningView, move)
                });
            }

            planningView.LegalMoves.Clear();
            foreach (var candidate in prunedMoves)
                planningView.LegalMoves.Add(candidate.Move.Clone());
            var b2Move = base.ChooseMove(planningView, new DeterministicRandom(1));
            foreach (var candidate in prunedMoves)
            {
                if (b2Move != null && MovesMatch(candidate.Move, b2Move))
                    candidate.PruneValue += b2MoveBonus;
            }
            prunedMoves.Sort((inLeft, inRight) => inRight.PruneValue.CompareTo(inLeft.PruneValue));

            var candidates = new List<PlanCandidate>();
            int count = Math.Min(candidateWidth, prunedMoves.Count);
            for (int i = 0; i < count; ++i)
            {
                var candidate = prunedMoves[i];
                candidate.State = inNode.State.Clone();
                inSimulator.ApplyMove(candidate.State, candidate.Move);
                int reorderMoves = inNode.ReorderMoves + (candidate.IsReorder ? 1 : 0);
                candidate.Value = EvaluateEndOfTurn(inSourceView, candidate.State, reorderMoves);
                candidate.OrderingValue = EvaluatePileOrdering(inSourceView, candidate.State);
                candidate.PruneValue += candidate.Value - inNode.Value;
                candidates.Add(candidate);
            }
            candidates.Sort((inLeft, inRight) =>
            {
                int pruneComparison = inRight.PruneValue.CompareTo(inLeft.PruneValue);
                return pruneComparison != 0
                    ? pruneComparison
                    : inRight.OrderingValue.CompareTo(inLeft.OrderingValue);
            });
            return candidates;
        }

        private static float EvaluatePileOrdering(PlayerViewData inSourceView, GameStateData inState)
        {
            if (inSourceView.Rules.MovesPerTurn > 0)
                return 0f;

            float value = 0f;
            foreach (var zone in FindPlayer(inState, inSourceView.Player.Id).Zones)
            {
                int elementIndex = (int)zone.ElementType - 1;
                for (int i = 0; i < zone.Cards.Count; ++i)
                {
                    var card = zone.Cards[i];
                    int cardsAbove = zone.Cards.Count - i - 1;
                    int points = Math.Max(GetValue(card.Points, elementIndex), card.WildPoints);
                    int multiplier = Math.Max(GetValue(card.Multipliers, elementIndex),
                        card.WildMultiplier);
                    int shield = Math.Max(GetValue(card.Shields, elementIndex), card.WildShield);
                    value += cardsAbove * (points + multiplier * 10f);
                    value += i * shield;
                }
            }
            return value;
        }

        private static float GetB2PruneValue(PlayerViewData inView, MoveData inMove)
        {
            if (inMove.Type == EMoveType.ActivateZone)
                return 1f;

            var card = FindSourceCard(inView.Player, inMove);
            var targetZone = FindTargetZone(inView, inMove.TargetPlayerId, inMove.TargetZoneId);
            if (card == null || targetZone == null)
                return inMove.SourceZoneId >= 0 ? -reorderPenalty : 0f;

            float value;
            bool isOwnBoard = inMove.TargetPlayerId == inView.Player.Id;
            if (card.IsPowerCard)
            {
                ZoneStateData result;
                if (card.WildZoneHeal > 0)
                    result = GameRules.ResolveHeal(targetZone, inView.Rules);
                else if (card.WildFreeze > 0)
                    result = GameRules.ResolveFreeze(targetZone, inView.Rules);
                else
                    result = GameRules.ResolveLightning(targetZone, card, inView.Rules);
                value = Math.Abs(targetZone.Points - result.Points)
                    + Math.Abs(targetZone.Health - result.Health)
                    + Math.Abs(targetZone.FrozenHealth - result.FrozenHealth);
            }
            else if (!isOwnBoard)
            {
                var result = GameRules.ResolveBomb(targetZone, card, inView.Rules);
                value = ScoreBombEffect(targetZone, result, GetTotalPoints(inView.Opponents[0].Zones),
                    inView.Rules.TotalPointsRequiredToWin);
            }
            else
            {
                var result = targetZone.Clone();
                var playedCard = card.Clone();
                playedCard.IsFaceUp = true;
                result.Cards.Add(playedCard);
                result.Points = GameRules.CalculateZonePoints(result);
                value = Math.Max(0, result.Points - targetZone.Points);
                int elementIndex = (int)targetZone.ElementType - 1;
                value += Math.Max(GetValue(card.Multipliers, elementIndex), card.WildMultiplier);
                value += .5f * Math.Max(GetValue(card.Shields, elementIndex), card.WildShield);
            }

            if (inMove.SourceZoneId >= 0)
                value -= reorderPenalty;
            return value;
        }

        private static CardInstanceData FindSourceCard(PlayerStateData inPlayer, MoveData inMove)
        {
            if (inMove.SourceZoneId < 0)
            {
                foreach (var card in inPlayer.Hand)
                {
                    if (card.Id == inMove.CardId)
                        return card;
                }
                return null;
            }

            foreach (var zone in inPlayer.Zones)
            {
                if (zone.Id == inMove.SourceZoneId && zone.Cards.Count > 0)
                    return zone.Cards[zone.Cards.Count - 1];
            }
            return null;
        }

        private static ZoneStateData FindTargetZone(PlayerViewData inView, int inPlayerId, int inZoneId)
        {
            if (inZoneId < 0)
                return null;
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

        private static GameStateData CreatePlanningState(PlayerViewData inView)
        {
            var rules = inView.Rules.Clone();
            rules.MovesPerTurn = 0;
            var state = new GameStateData
            {
                MatchId = "strong-ai-plan",
                Revision = inView.Revision,
                TurnNumber = inView.TurnNumber,
                CurrentPlayerId = inView.Player.Id,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = maxPlanDepth,
                WinnerPlayerId = -1,
                Rules = rules,
                Random = new RandomStateData { State = 1 },
                NextCardInstanceId = 1000000
            };

            state.Players.Add(inView.Player.Clone());
            foreach (var opponent in inView.Opponents)
            {
                var player = new PlayerStateData
                {
                    Id = opponent.Id,
                    Name = opponent.Name
                };
                foreach (var zone in opponent.Zones)
                    player.Zones.Add(zone.Clone());
                state.Players.Add(player);
            }

            // Opponent hands and both decks intentionally remain empty: plans use only PlayerViewData.
            return state;
        }

        private static PlayerViewData CreatePlanningView(PlayerViewData inSource,
            GameStateData inState, int inMovesRemaining)
        {
            var view = new PlayerViewData
            {
                Revision = inState.Revision,
                TurnNumber = inState.TurnNumber,
                CurrentPlayerId = inSource.Player.Id,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = inMovesRemaining,
                WinnerPlayerId = -1,
                WinType = EWinCondition.None,
                DeckCount = inSource.DeckCount,
                PowerDeckCount = inSource.PowerDeckCount,
                Rules = inSource.Rules.Clone(),
                Player = FindPlayer(inState, inSource.Player.Id).Clone()
            };

            foreach (var player in inState.Players)
            {
                if (player.Id == inSource.Player.Id)
                    continue;

                var opponent = new OpponentViewData
                {
                    Id = player.Id,
                    Name = player.Name,
                    HandCount = FindOpponentHandCount(inSource, player.Id)
                };
                foreach (var zone in player.Zones)
                    opponent.Zones.Add(zone.Clone());
                view.Opponents.Add(opponent);
            }
            return view;
        }

        private static float EvaluateEndOfTurn(PlayerViewData inSourceView,
            GameStateData inState, int inReorderMoves)
        {
            var own = FindPlayer(inState, inSourceView.Player.Id);
            int ownPoints = GetTotalPoints(own.Zones);
            int opponentPoints = 0;
            foreach (var player in inState.Players)
            {
                if (player.Id != own.Id)
                    opponentPoints += GetTotalPoints(player.Zones);
            }

            int winTarget = inSourceView.Rules.TotalPointsRequiredToWin;
            float proximity = winTarget <= 0 ? 0f : Math.Min(1f, (float)ownPoints / winTarget);
            float value = ownPoints * (1f + proximity) - opponentPoints;
            if (winTarget > 0 && ownPoints >= winTarget)
                value += winningPlanBonus;
            if (inSourceView.TurnNumber >= antiStallTurn)
                value += ownPoints * (inSourceView.TurnNumber - antiStallTurn + 1)
                    * antiStallProgressPerTurn;

            var planningView = CreatePlanningView(inSourceView, inState, 0);
            value -= GetExpectedExposure(planningView, ownPoints);
            return value - inReorderMoves * reorderPenalty;
        }

        private static float GetExpectedExposure(PlayerViewData inView, int inOwnPoints)
        {
            int opponentHandSize = 0;
            foreach (var opponent in inView.Opponents)
                opponentHandSize += opponent.HandCount;

            float exposure = 0f;
            foreach (var zone in inView.Player.Zones)
            {
                CardDefinitionData strongestBomb;
                int unseenCount;
                int threateningCount;
                GetUnseenBombThreat(inView, zone, out strongestBomb, out unseenCount, out threateningCount);
                if (strongestBomb == null)
                    continue;

                double probability = ProbabilityOfAtLeastOne(unseenCount, threateningCount,
                    opponentHandSize);
                var bombedZone = GameRules.ResolveBomb(zone, strongestBomb.CreateCard(), inView.Rules);
                exposure += (float)probability * ScoreBombEffect(zone, bombedZone, inOwnPoints,
                    inView.Rules.TotalPointsRequiredToWin);
            }
            return exposure;
        }

        private static int GetTotalPoints(List<ZoneStateData> inZones)
        {
            int points = 0;
            foreach (var zone in inZones)
                points += zone.Points;
            return points;
        }

        private static PlayerStateData FindPlayer(GameStateData inState, int inPlayerId)
        {
            foreach (var player in inState.Players)
            {
                if (player.Id == inPlayerId)
                    return player;
            }
            throw new InvalidOperationException("The planning player no longer exists.");
        }

        private static int FindOpponentHandCount(PlayerViewData inView, int inPlayerId)
        {
            foreach (var opponent in inView.Opponents)
            {
                if (opponent.Id == inPlayerId)
                    return opponent.HandCount;
            }
            return 0;
        }

        private static MoveData FindMatchingMove(List<MoveData> inMoves, MoveData inMove)
        {
            foreach (var move in inMoves)
            {
                if (MovesMatch(move, inMove))
                    return move;
            }
            return null;
        }

        private static bool MovesMatch(MoveData inFirst, MoveData inSecond)
        {
            return inFirst.PlayerId == inSecond.PlayerId
                && inFirst.Type == inSecond.Type
                && inFirst.CardId == inSecond.CardId
                && inFirst.SourceZoneId == inSecond.SourceZoneId
                && inFirst.TargetPlayerId == inSecond.TargetPlayerId
                && inFirst.TargetZoneId == inSecond.TargetZoneId
                && inFirst.PowerCardType == inSecond.PowerCardType;
        }

        protected override bool TryGetPointPlayValue(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, BoardAnalysis inOwn, out float outValue)
        {
            if (!this.useEffectBasedBombScoring)
                return base.TryGetPointPlayValue(inView, inCard, inZone, inOwn, out outValue);

            var simulatedZone = inZone.Clone();
            var simulatedCard = inCard.Clone();
            simulatedCard.IsFaceUp = true;
            simulatedZone.Cards.Add(simulatedCard);
            int pointsGained = Math.Max(0, GameRules.CalculateZonePoints(simulatedZone) - inZone.Points);
            outValue = ScorePointDelta(pointsGained, inOwn.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);
            return true;
        }

        protected override bool TryScoreBomb(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, bool inIsOwnBoard, BoardAnalysis inOwn, BoardAnalysis inOpponent,
            int inNumMovesRemaining, out float outValue)
        {
            if (!this.useEffectBasedBombScoring || inIsOwnBoard)
            {
                return base.TryScoreBomb(inView, inCard, inZone, inIsOwnBoard, inOwn, inOpponent,
                    inNumMovesRemaining, out outValue);
            }

            var simulatedZone = GameRules.ResolveBomb(inZone.Clone(), inCard, inView.Rules);
            outValue = ScoreBombEffect(inZone, simulatedZone, inOpponent.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);
            return outValue > 0f;
        }

        protected override bool TryGetShieldPlayValue(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, BoardAnalysis inOwn, out float outValue)
        {
            if (!this.useEffectBasedBombScoring)
                return base.TryGetShieldPlayValue(inView, inCard, inZone, inOwn, out outValue);

            CardDefinitionData strongestBomb;
            int unseenCount;
            int threateningCount;
            GetUnseenBombThreat(inView, inZone, out strongestBomb, out unseenCount, out threateningCount);
            int opponentHandSize = inView.Opponents.Count == 0 ? 0 : inView.Opponents[0].HandCount;
            double threatProbability = ProbabilityOfAtLeastOne(unseenCount, threateningCount,
                opponentHandSize);
            if (strongestBomb == null || threatProbability <= 0.0)
            {
                outValue = 0f;
                return false;
            }

            var bombCard = strongestBomb.CreateCard();
            var bombedZone = GameRules.ResolveBomb(inZone, bombCard, inView.Rules);
            float exposedLoss = ScoreBombEffect(inZone, bombedZone, inOwn.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);

            var shieldedZone = inZone.Clone();
            var shieldCard = inCard.Clone();
            shieldCard.IsFaceUp = true;
            shieldedZone.Cards.Add(shieldCard);
            shieldedZone.Points = GameRules.CalculateZonePoints(shieldedZone);
            shieldedZone.FaceUpPoints = GameRules.CalculateZonePoints(shieldedZone, true);
            var shieldedBombedZone = GameRules.ResolveBomb(shieldedZone, bombCard, inView.Rules);
            float shieldedLoss = ScoreBombEffect(shieldedZone, shieldedBombedZone,
                inOwn.TotalPoints, inView.Rules.TotalPointsRequiredToWin);

            outValue = (float)(threatProbability * Math.Max(0f, exposedLoss - shieldedLoss));
            return true;
        }

        private static float ScorePointDelta(int inPointDelta, int inCurrentPoints, int inWinTarget)
        {
            if (inPointDelta <= 0 || inWinTarget <= 0)
                return Math.Max(0, inPointDelta);

            float proximity = Math.Min(1f, (float)inCurrentPoints / inWinTarget);
            return inPointDelta * (1f + proximity);
        }

        private static float ScoreBombEffect(ZoneStateData inBefore, ZoneStateData inAfter,
            int inCurrentPoints, int inWinTarget)
        {
            int pointsRemoved = Math.Max(0, inBefore.Points - inAfter.Points);
            int effectiveDamage = inBefore.Cards.Count - inAfter.Cards.Count
                + inBefore.Health - inAfter.Health
                + inBefore.FrozenHealth - inAfter.FrozenHealth;
            float value = Math.Max(effectiveDamage,
                ScorePointDelta(pointsRemoved, inCurrentPoints, inWinTarget));
            if (!inBefore.IsDestroyed && inAfter.IsDestroyed)
                value += zoneKillBonus;
            if (WasMultiplierStripped(inBefore, inAfter))
                value += multiplierStripBonus;
            return value;
        }

        private static void GetUnseenBombThreat(PlayerViewData inView, ZoneStateData inZone,
            out CardDefinitionData outStrongestBomb, out int outUnseenCount, out int outThreateningCount)
        {
            var visibleCounts = new Dictionary<int, int>();
            CountDefinitions(inView.Player.Hand, visibleCounts);
            foreach (var zone in inView.Player.Zones)
                CountDefinitions(zone.Cards, visibleCounts);
            foreach (var opponent in inView.Opponents)
            {
                foreach (var zone in opponent.Zones)
                    CountDefinitions(zone.Cards, visibleCounts);
            }

            int elementIndex = (int)inZone.ElementType - 1;
            int topShield = GetTopShield(inZone, elementIndex);
            int strongestValue = 0;
            outStrongestBomb = null;
            outUnseenCount = 0;
            outThreateningCount = 0;
            foreach (var definition in DeckDefinitionData.NormalCards)
            {
                int visible;
                visibleCounts.TryGetValue(definition.DefinitionId, out visible);
                int remaining = Math.Max(0, definition.ChoiceCount - visible);
                outUnseenCount += remaining;

                int bombValue = Math.Max(GetValue(definition.Bombs, elementIndex), definition.WildBomb);
                if (remaining <= 0 || bombValue <= 0 || bombValue < topShield)
                    continue;

                outThreateningCount += remaining;
                if (bombValue <= strongestValue)
                    continue;
                strongestValue = bombValue;
                outStrongestBomb = definition;
            }
        }

        private static double ProbabilityOfAtLeastOne(int inPopulation, int inSuccesses, int inDraws)
        {
            int draws = Math.Min(inDraws, inPopulation);
            if (inPopulation <= 0 || inSuccesses <= 0 || draws <= 0)
                return 0.0;
            if (inSuccesses >= inPopulation)
                return 1.0;

            double probabilityOfNone = 1.0;
            for (int i = 0; i < draws; ++i)
            {
                int safeRemaining = inPopulation - inSuccesses - i;
                if (safeRemaining <= 0)
                    return 1.0;
                probabilityOfNone *= (double)safeRemaining / (inPopulation - i);
            }
            return 1.0 - probabilityOfNone;
        }

        private static int GetTopShield(ZoneStateData inZone, int inElementIndex)
        {
            if (inZone.Cards.Count == 0)
                return 0;
            var topCard = inZone.Cards[inZone.Cards.Count - 1];
            return Math.Max(GetValue(topCard.Shields, inElementIndex), topCard.WildShield);
        }

        private static void CountDefinitions(List<CardInstanceData> inCards,
            Dictionary<int, int> inCounts)
        {
            foreach (var card in inCards)
            {
                int count;
                inCounts.TryGetValue(card.DefinitionId, out count);
                inCounts[card.DefinitionId] = count + 1;
            }
        }

        protected override int GetDifficultyIndex(int inCount, IRandom inRandom)
        {
            if (inCount <= 1 || this.temperature <= 0f)
                return 0;

            double totalWeight = 0.0;
            for (int i = 0; i < inCount; ++i)
                totalWeight += Math.Exp(-i / this.temperature);

            double selectedWeight = inRandom.NextDouble() * totalWeight;
            for (int i = 0; i < inCount; ++i)
            {
                selectedWeight -= Math.Exp(-i / this.temperature);
                if (selectedWeight <= 0.0)
                    return i;
            }

            return inCount - 1;
        }

        private static float ResolveTemperature(StrongAiOptions inOptions)
        {
            if (inOptions.Temperature > 0f)
                return inOptions.Temperature;
            if (inOptions.Difficulty == EAiDifficulty.Medium)
                return mediumTemperature;
            if (inOptions.Difficulty == EAiDifficulty.Easy)
                return easyTemperature;
            return 0f;
        }

        private static bool WasMultiplierStripped(ZoneStateData inBefore, ZoneStateData inAfter)
        {
            int elementIndex = (int)inBefore.ElementType - 1;
            foreach (var card in inBefore.Cards)
            {
                int multiplier = Math.Max(GetValue(card.Multipliers, elementIndex), card.WildMultiplier);
                if (multiplier <= 1 || ContainsCard(inAfter, card.Id))
                    continue;
                return true;
            }

            return false;
        }

        private static bool ContainsCard(ZoneStateData inZone, string inCardId)
        {
            foreach (var card in inZone.Cards)
            {
                if (card.Id == inCardId)
                    return true;
            }
            return false;
        }

        private static int GetValue(int[] inValues, int inIndex)
        {
            return inValues == null || inIndex < 0 || inIndex >= inValues.Length ? 0 : inValues[inIndex];
        }
    }
}
