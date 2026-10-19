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
        private const float mediumTemperature = 2.5f;
        private const float easyTemperature = 20f;
        private const float reorderPenalty = .35f; // Reorders must overcome this score margin.
        private const float orderingPruneWeight = .1f;
        private const float orderingPlanWeight = .1f;
        private const float b2MoveBonus = 20f;
        private const float rebuildStripBonus = 24f;
        private const float winningPlanBonus = 10000f;
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
            public Dictionary<string, HashSet<int>> LeftZonesByCard;
            public HashSet<string> LockedCardIds;
            public Dictionary<string, int> ZoneVersions;
            public Dictionary<string, Dictionary<string, int>> CardMoveSnapshots;
            public float TacticalValue;
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
            public float TacticalValue;
            public float Value;
            public float OrderingValue;
        }

        private readonly bool useEffectBasedBombScoring;
        private readonly float temperature;
        private readonly int maxDepth;

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
            this.maxDepth = inOptions.Difficulty == EAiDifficulty.Easy ? 1 : maxPlanDepth;
        }

        private int trackedTurnNumber = int.MinValue;
        private int trackedCurrentPlayerId = int.MinValue;
        private int trackedRevision = int.MinValue;
        private int turnReorders;
        private readonly HashSet<string> playedFromHandCardIds = new();
        private readonly Dictionary<string, HashSet<int>> leftZonesByCard = new();
        private readonly Dictionary<string, int> zoneVersions = new();
        private readonly Dictionary<string, Dictionary<string, int>> cardMoveSnapshots = new();
        private PlayerViewData currentView;

        public bool IsMoveAllowedThisTurn(PlayerViewData inView, MoveData inMove)
        {
            PrepareTurnState(inView);
            return !IsBlockedReorder(inMove);
        }

        public void RecordExecutedMove(PlayerViewData inView, MoveData inMove)
        {
            PrepareTurnState(inView);
            RecordMoveVersions(inMove);
            RecordPlayedFromHand(inMove);
            if (IsTurnReorder(inMove))
            {
                ++this.turnReorders;
                AddReorderHistory(this.leftZonesByCard, inMove);
            }
        }

        private void PrepareTurnState(PlayerViewData inView)
        {
            if (inView.TurnNumber != this.trackedTurnNumber
                || inView.CurrentPlayerId != this.trackedCurrentPlayerId
                || inView.Revision < this.trackedRevision)
            {
                this.turnReorders = 0;
                this.playedFromHandCardIds.Clear();
                this.leftZonesByCard.Clear();
                this.zoneVersions.Clear();
                this.cardMoveSnapshots.Clear();
            }
            this.currentView = inView;
            this.trackedTurnNumber = inView.TurnNumber;
            this.trackedCurrentPlayerId = inView.CurrentPlayerId;
            this.trackedRevision = inView.Revision;
        }

        public new MoveData ChooseMove(PlayerViewData inView, IRandom inRandom)
        {
            PrepareTurnState(inView);

            var original = inView.LegalMoves;
            var allowed = new List<MoveData>(original.Count);
            foreach (var candidate in original)
            {
                if (!IsBlockedReorder(candidate))
                    allowed.Add(candidate);
            }
            MoveData move;
            inView.LegalMoves = allowed;
            try
            {
                move = ChooseMoveCore(inView, inRandom);
            }
            finally
            {
                inView.LegalMoves = original;
            }
            if (IsBlockedReorder(move))
            {
                move = null;
                foreach (var candidate in allowed)
                {
                    if (candidate.Type != EMoveType.EndTurn)
                        continue;
                    move = candidate;
                    break;
                }
            }
            RecordMoveVersions(move);
            RecordPlayedFromHand(move);
            if (IsTurnReorder(move))
            {
                ++this.turnReorders;
                if (!this.leftZonesByCard.TryGetValue(move.CardId, out var zones))
                    this.leftZonesByCard[move.CardId] = zones = new HashSet<int>();
                zones.Add(move.SourceZoneId);
            }
            return move;
        }

        private static string ZoneKey(int inPlayerId, int inZoneId)
        {
            return inPlayerId + ":" + inZoneId;
        }

        private int GetZoneVersion(string inKey)
        {
            return this.zoneVersions.TryGetValue(inKey, out var version) ? version : 0;
        }

        // A card may move again this turn only if its destination pile changed since its last move.
        private bool IsUnchangedReMove(MoveData inMove)
        {
            if (inMove == null || inMove.SourceZoneId < 0 || string.IsNullOrEmpty(inMove.CardId)
                || inMove.TargetZoneId < 0)
                return false;
            if (!this.cardMoveSnapshots.TryGetValue(inMove.CardId, out var snapshot))
                return false;
            string targetKey = ZoneKey(inMove.TargetPlayerId, inMove.TargetZoneId);
            int versionAtLastMove = snapshot.TryGetValue(targetKey, out var version) ? version : 0;
            return GetZoneVersion(targetKey) == versionAtLastMove;
        }

        private void RecordMoveVersions(MoveData inMove)
        {
            if (inMove == null)
                return;
            if (inMove.SourceZoneId >= 0)
            {
                string sourceKey = ZoneKey(inMove.PlayerId, inMove.SourceZoneId);
                this.zoneVersions[sourceKey] = GetZoneVersion(sourceKey) + 1;
            }
            if (inMove.TargetZoneId >= 0 && inMove.TargetPlayerId >= 0)
            {
                string targetKey = ZoneKey(inMove.TargetPlayerId, inMove.TargetZoneId);
                this.zoneVersions[targetKey] = GetZoneVersion(targetKey) + 1;
            }
            if (!string.IsNullOrEmpty(inMove.CardId))
                this.cardMoveSnapshots[inMove.CardId] = new Dictionary<string, int>(this.zoneVersions);
        }

        private void RecordPlayedFromHand(MoveData inMove)
        {
            if (IsFromHandPlay(inMove))
                this.playedFromHandCardIds.Add(inMove.CardId);
        }

        private bool IsEmptyOwnZone(int inZoneId)
        {
            if (this.currentView == null)
                return false;
            foreach (var zone in this.currentView.Player.Zones)
            {
                if (zone.Id == inZoneId)
                    return zone.Cards.Count == 0;
            }
            return false;
        }

        private static bool IsTurnReorder(MoveData inMove)
        {
            return inMove != null && inMove.Type == EMoveType.PlayCard && inMove.SourceZoneId >= 0
                && inMove.TargetPlayerId == inMove.PlayerId;
        }

        private static bool IsFromHandPlay(MoveData inMove)
        {
            return inMove != null && inMove.Type == EMoveType.PlayCard && inMove.SourceZoneId < 0
                && !string.IsNullOrEmpty(inMove.CardId);
        }

        private bool IsBlockedReorder(MoveData inMove)
        {
            if (inMove != null && inMove.SourceZoneId >= 0
                && this.playedFromHandCardIds.Contains(inMove.CardId))
                return true;
            if (IsUnchangedReMove(inMove))
                return true;
            if (!IsTurnReorder(inMove))
                return false;
            if (IsEmptyOwnZone(inMove.TargetZoneId))
                return true;
            return this.turnReorders >= maxReorderMoves;
        }

        private static bool IsBlockedReorder(int inReorderMoves,
            Dictionary<string, HashSet<int>> inLeftZonesByCard, HashSet<string> inLockedCardIds,
            Dictionary<string, int> inZoneVersions,
            Dictionary<string, Dictionary<string, int>> inCardMoveSnapshots, MoveData inMove)
        {
            if (inMove.SourceZoneId >= 0 && inLockedCardIds.Contains(inMove.CardId))
                return true;
            if (IsUnchangedReMove(inZoneVersions, inCardMoveSnapshots, inMove))
                return true;
            if (!IsTurnReorder(inMove))
                return false;
            if (inReorderMoves >= maxReorderMoves)
                return true;
            if (!inLeftZonesByCard.TryGetValue(inMove.CardId, out var leftZones))
                return false;
            if (leftZones.Contains(inMove.TargetZoneId))
                return true;
            return leftZones.Count > 0;
        }

        private static bool IsUnchangedReMove(Dictionary<string, int> inZoneVersions,
            Dictionary<string, Dictionary<string, int>> inCardMoveSnapshots, MoveData inMove)
        {
            if (inMove.SourceZoneId < 0 || string.IsNullOrEmpty(inMove.CardId)
                || inMove.TargetZoneId < 0
                || !inCardMoveSnapshots.TryGetValue(inMove.CardId, out var snapshot))
                return false;

            string targetKey = ZoneKey(inMove.TargetPlayerId, inMove.TargetZoneId);
            int versionAtLastMove = snapshot.TryGetValue(targetKey, out var snapshotVersion)
                ? snapshotVersion
                : 0;
            int currentVersion = inZoneVersions.TryGetValue(targetKey, out var zoneVersion)
                ? zoneVersion
                : 0;
            return currentVersion == versionAtLastMove;
        }

        private MoveData ChooseMoveCore(PlayerViewData inView, IRandom inRandom)
        {
            var b2Move = base.ChooseMove(inView, inRandom);
            if (b2Move == null || !this.useEffectBasedBombScoring)
                return b2Move;

            var plannedMove = ChoosePlannedMove(inView, inRandom, b2Move);
            if (plannedMove != null)
                return plannedMove;
            if (inView.TurnNumber >= antiStallTurn && b2Move.Type == EMoveType.EndTurn)
            {
                var discard = FindHandDiscard(inView);
                if (discard != null)
                    return discard;
            }
            return b2Move;
        }

        private MoveData ChoosePlannedMove(PlayerViewData inView, IRandom inRandom,
            MoveData inB2Move)
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
                ReorderMoves = this.turnReorders,
                LeftZonesByCard = CloneReorderHistory(this.leftZonesByCard),
                LockedCardIds = new HashSet<string>(this.playedFromHandCardIds),
                ZoneVersions = new Dictionary<string, int>(this.zoneVersions),
                CardMoveSnapshots = CloneMoveSnapshots(this.cardMoveSnapshots),
                OrderingValue = EvaluatePileOrdering(inView, state)
            };
            root.Value = EvaluateEndOfTurn(inView, state, this.turnReorders)
                + root.OrderingValue * orderingPlanWeight;
            var frontier = new List<PlanNode> { root };
            var planChoices = new List<PlanNode>();
            PlanNode best = null;

            for (int depth = 0; depth < this.maxDepth && frontier.Count > 0; ++depth)
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
                        var leftZonesByCard = node.LeftZonesByCard;
                        if (candidate.IsReorder)
                        {
                            leftZonesByCard = CloneReorderHistory(leftZonesByCard);
                            AddReorderHistory(leftZonesByCard, candidate.Move);
                        }
                        var lockedCardIds = node.LockedCardIds;
                        if (IsFromHandPlay(candidate.Move))
                        {
                            lockedCardIds = new HashSet<string>(lockedCardIds)
                            {
                                candidate.Move.CardId
                            };
                        }
                        var zoneVersions = new Dictionary<string, int>(node.ZoneVersions);
                        var cardMoveSnapshots = CloneMoveSnapshots(node.CardMoveSnapshots);
                        RecordMoveVersions(zoneVersions, cardMoveSnapshots, candidate.Move);
                        var child = new PlanNode
                        {
                            State = candidate.State,
                            FirstMove = firstMove,
                            MovesRemaining = node.MovesRemaining
                                - (candidate.Move.Type == EMoveType.PlayCard ? 1 : 0),
                            ReorderMoves = reorderMoves,
                            LeftZonesByCard = leftZonesByCard,
                            LockedCardIds = lockedCardIds,
                            ZoneVersions = zoneVersions,
                            CardMoveSnapshots = cardMoveSnapshots,
                            TacticalValue = candidate.TacticalValue,
                            Value = candidate.Value,
                            OrderingValue = candidate.OrderingValue,
                            FirstMoveIsB2 = node.FirstMove == null
                                ? MovesMatch(firstMove, inB2Move)
                                : node.FirstMoveIsB2
                        };
                        next.Add(child);
                        AddPlanChoice(planChoices, child);
                        if (best == null || ComparePlanNodes(child, best) < 0)
                            best = child;
                    }
                }

                next.Sort(ComparePlanNodes);
                if (next.Count > beamWidth)
                    next.RemoveRange(beamWidth, next.Count - beamWidth);
                frontier = next;
            }

            if (best == null || this.temperature <= 0f)
                return best == null ? null : best.FirstMove.Clone();
            return ChooseSoftmaxPlan(planChoices, inRandom).FirstMove.Clone();
        }

        private static void AddPlanChoice(List<PlanNode> inChoices, PlanNode inCandidate)
        {
            for (int i = 0; i < inChoices.Count; ++i)
            {
                if (!MovesMatch(inChoices[i].FirstMove, inCandidate.FirstMove))
                    continue;
                if (ComparePlanNodes(inCandidate, inChoices[i]) < 0)
                    inChoices[i] = inCandidate;
                return;
            }

            inChoices.Add(inCandidate);
        }

        private PlanNode ChooseSoftmaxPlan(List<PlanNode> inChoices, IRandom inRandom)
        {
            inChoices.Sort(ComparePlanNodes);
            double bestValue = inChoices[0].Value;
            double totalWeight = 0.0;
            for (int i = 0; i < inChoices.Count; ++i)
                totalWeight += Math.Exp((inChoices[i].Value - bestValue) / this.temperature);

            double selectedWeight = inRandom.NextDouble() * totalWeight;
            for (int i = 0; i < inChoices.Count; ++i)
            {
                selectedWeight -= Math.Exp((inChoices[i].Value - bestValue) / this.temperature);
                if (selectedWeight <= 0.0)
                    return inChoices[i];
            }

            return inChoices[inChoices.Count - 1];
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

                bool isReorder = IsTurnReorder(move);
                if (IsBlockedReorder(inNode.ReorderMoves, inNode.LeftZonesByCard,
                        inNode.LockedCardIds, inNode.ZoneVersions, inNode.CardMoveSnapshots, move))
                    continue;
                if (isReorder && IsEmptyOwnZone(planningView, move.TargetZoneId))
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
                candidate.TacticalValue = inNode.TacticalValue
                    + GetStrategicMoveValue(planningView, candidate.Move);
                candidate.Value = EvaluateEndOfTurn(inSourceView, candidate.State, reorderMoves);
                candidate.OrderingValue = EvaluatePileOrdering(inSourceView, candidate.State);
                candidate.Value += candidate.OrderingValue * orderingPlanWeight
                    + candidate.TacticalValue;
                candidate.PruneValue += candidate.Value - inNode.Value;
                candidate.PruneValue += Math.Max(0f,
                    candidate.OrderingValue - inNode.OrderingValue) * orderingPruneWeight;
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

        private static Dictionary<string, HashSet<int>> CloneReorderHistory(
            Dictionary<string, HashSet<int>> inHistory)
        {
            var clone = new Dictionary<string, HashSet<int>>();
            foreach (var pair in inHistory)
                clone[pair.Key] = new HashSet<int>(pair.Value);
            return clone;
        }

        private static Dictionary<string, Dictionary<string, int>> CloneMoveSnapshots(
            Dictionary<string, Dictionary<string, int>> inSnapshots)
        {
            var clone = new Dictionary<string, Dictionary<string, int>>();
            foreach (var pair in inSnapshots)
                clone[pair.Key] = new Dictionary<string, int>(pair.Value);
            return clone;
        }

        private static void RecordMoveVersions(Dictionary<string, int> inZoneVersions,
            Dictionary<string, Dictionary<string, int>> inCardMoveSnapshots, MoveData inMove)
        {
            if (inMove.SourceZoneId >= 0)
            {
                string sourceKey = ZoneKey(inMove.PlayerId, inMove.SourceZoneId);
                int sourceVersion = inZoneVersions.TryGetValue(sourceKey, out var version) ? version : 0;
                inZoneVersions[sourceKey] = sourceVersion + 1;
            }
            if (inMove.TargetZoneId >= 0 && inMove.TargetPlayerId >= 0)
            {
                string targetKey = ZoneKey(inMove.TargetPlayerId, inMove.TargetZoneId);
                int targetVersion = inZoneVersions.TryGetValue(targetKey, out var version) ? version : 0;
                inZoneVersions[targetKey] = targetVersion + 1;
            }
            if (!string.IsNullOrEmpty(inMove.CardId))
            {
                inCardMoveSnapshots[inMove.CardId] = new Dictionary<string, int>(inZoneVersions);
            }
        }

        private static void AddReorderHistory(Dictionary<string, HashSet<int>> inHistory,
            MoveData inMove)
        {
            if (!inHistory.TryGetValue(inMove.CardId, out var leftZones))
            {
                leftZones = new HashSet<int>();
                inHistory[inMove.CardId] = leftZones;
            }
            leftZones.Add(inMove.SourceZoneId);
        }

        private static bool IsEmptyOwnZone(PlayerViewData inView, int inZoneId)
        {
            var zone = FindZone(inView.Player.Zones, inZoneId);
            return zone != null && zone.Cards.Count == 0;
        }

        private static float EvaluatePileOrdering(PlayerViewData inSourceView, GameStateData inState)
        {
            if (inSourceView.Rules.MovesPerTurn > 0
                && inSourceView.Rules.MovesPerTurn < maxPlanDepth)
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
                    if (multiplier > 1)
                        value += multiplier * GetPointsAbove(zone, i, elementIndex);
                    if (shield > 0)
                    {
                        float protectedValue = GetProtectedCardValue(zone, i, elementIndex);
                        value += shield * protectedValue;
                    }
                }
            }
            return value;
        }

        private static int GetPointsAbove(ZoneStateData inZone, int inCardIndex,
            int inElementIndex)
        {
            int points = 0;
            for (int i = inCardIndex + 1; i < inZone.Cards.Count; ++i)
            {
                var card = inZone.Cards[i];
                points += Math.Max(GetValue(card.Points, inElementIndex), card.WildPoints);
            }
            return points;
        }

        private static float GetProtectedCardValue(ZoneStateData inZone, int inShieldIndex,
            int inElementIndex)
        {
            float value = 0f;
            for (int i = 0; i < inShieldIndex; ++i)
            {
                var card = inZone.Cards[i];
                int points = Math.Max(GetValue(card.Points, inElementIndex), card.WildPoints);
                int multiplier = Math.Max(GetValue(card.Multipliers, inElementIndex),
                    card.WildMultiplier);
                value += points + multiplier * 10f;
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
                value -= GetDualPurposeBombPenalty(inView, card, targetZone, result);
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

            value += GetRebuildStripBonus(inView, inMove);
            if (inMove.SourceZoneId >= 0)
                value -= reorderPenalty;
            return value;
        }

        private static float GetRebuildStripBonus(PlayerViewData inView, MoveData inMove)
        {
            if (inMove.Type != EMoveType.PlayCard || inMove.SourceZoneId < 0
                || inMove.TargetPlayerId != inView.Player.Id)
                return 0f;

            var sourceZone = FindZone(inView.Player.Zones, inMove.SourceZoneId);
            if (sourceZone == null || sourceZone.Cards.Count == 0)
                return 0f;

            var coverCard = sourceZone.Cards[sourceZone.Cards.Count - 1];
            if (coverCard.Id != inMove.CardId)
                return 0f;

            int elementIndex = (int)sourceZone.ElementType - 1;
            int coverPoints = Math.Max(GetValue(coverCard.Points, elementIndex),
                coverCard.WildPoints);
            bool canRebuildWithMultiplier = coverCard.IsFaceUp && coverPoints > 0;
            bool canRebuildWithShield = false;
            for (int i = 0; i < sourceZone.Cards.Count - 1; ++i)
            {
                var buriedCard = sourceZone.Cards[i];
                int buriedPoints = Math.Max(GetValue(buriedCard.Points, elementIndex),
                    buriedCard.WildPoints);
                int buriedMultiplier = Math.Max(GetValue(buriedCard.Multipliers, elementIndex),
                    buriedCard.WildMultiplier);
                if (coverPoints > 0
                    && (buriedPoints > coverPoints || buriedMultiplier > 1))
                {
                    canRebuildWithShield = true;
                    break;
                }
            }

            foreach (var handCard in inView.Player.Hand)
            {
                if (!GameRules.CanPlayCardInZone(handCard, sourceZone.ElementType))
                    continue;

                int multiplier = Math.Max(GetValue(handCard.Multipliers, elementIndex),
                    handCard.WildMultiplier);
                int shield = Math.Max(GetValue(handCard.Shields, elementIndex),
                    handCard.WildShield);
                if ((canRebuildWithMultiplier && multiplier > 1)
                    || (canRebuildWithShield && shield > 0))
                    return rebuildStripBonus;
            }
            return 0f;
        }

        private static float GetStrategicMoveValue(PlayerViewData inView, MoveData inMove)
        {
            if (inMove.Type != EMoveType.PlayCard || inMove.TargetPlayerId == inView.Player.Id)
                return 0f;

            var card = FindSourceCard(inView.Player, inMove);
            var targetZone = FindTargetZone(inView, inMove.TargetPlayerId, inMove.TargetZoneId);
            if (card == null || targetZone == null)
                return 0f;

            int elementIndex = (int)targetZone.ElementType - 1;
            int bomb = Math.Max(GetValue(card.Bombs, elementIndex), card.WildBomb);
            if (bomb <= 0)
                return 0f;

            var result = GameRules.ResolveBomb(targetZone, card, inView.Rules);
            if (!IsHighValueBomb(targetZone, result))
                return 0f;
            return ScoreStrategicBombEffect(targetZone, result);
        }

        private static float ScoreStrategicBombEffect(ZoneStateData inBefore,
            ZoneStateData inAfter)
        {
            float value = Math.Max(0, inBefore.Health - inAfter.Health)
                + Math.Max(0, inBefore.FrozenHealth - inAfter.FrozenHealth);
            if (!inBefore.IsDestroyed && inAfter.IsDestroyed)
                value += zoneKillBonus;
            if (inBefore.IsFrozen && !inAfter.IsFrozen)
                value += zoneKillBonus;
            if (WasMultiplierStripped(inBefore, inAfter))
                value += multiplierStripBonus;
            return value;
        }

        private static float GetDualPurposeBombPenalty(PlayerViewData inView,
            CardInstanceData inCard, ZoneStateData inTargetZone, ZoneStateData inBombedZone)
        {
            if (IsHighValueBomb(inTargetZone, inBombedZone))
                return 0f;

            float bestMultiplierValue = 0f;
            foreach (var zone in inView.Player.Zones)
            {
                if (zone.IsDestroyed || zone.IsFrozen || zone.Points <= 0
                    || !GameRules.CanPlayCardInZone(inCard, zone.ElementType))
                    continue;

                int elementIndex = (int)zone.ElementType - 1;
                int multiplier = Math.Max(GetValue(inCard.Multipliers, elementIndex),
                    inCard.WildMultiplier);
                if (multiplier <= 1)
                    continue;

                var result = zone.Clone();
                var playedCard = inCard.Clone();
                playedCard.IsFaceUp = true;
                result.Cards.Add(playedCard);
                result.Points = GameRules.CalculateZonePoints(result);
                bestMultiplierValue = Math.Max(bestMultiplierValue,
                    Math.Max(0, result.Points - zone.Points) + multiplier);
            }
            return bestMultiplierValue;
        }

        private static bool IsHighValueBomb(ZoneStateData inBefore, ZoneStateData inAfter)
        {
            return (!inBefore.IsDestroyed && inAfter.IsDestroyed)
                || WasMultiplierStripped(inBefore, inAfter)
                || inBefore.IsFrozen && !inAfter.IsFrozen && inBefore.Points > 0;
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
                StartingPlayerId = inView.StartingPlayerId,
                CurrentPlayerId = inView.Player.Id,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = maxPlanDepth,
                WinnerPlayerId = -1,
                Rules = rules,
                Random = new RandomStateData { State = 1 },
                NextCardInstanceId = inView.NextCardInstanceId
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
                StartingPlayerId = inState.StartingPlayerId,
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

        private static MoveData FindHandDiscard(PlayerViewData inView)
        {
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type == EMoveType.DiscardCard && move.SourceZoneId < 0)
                    return move.Clone();
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
            outValue -= GetDualPurposeBombPenalty(inView, inCard, inZone, simulatedZone);
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
            if (inBefore.IsFrozen && !inAfter.IsFrozen)
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
