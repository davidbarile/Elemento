using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public sealed class OnlineTurnBuffer
    {
        private readonly List<MoveData> moves = new();
        private readonly List<MoveData> initialLegalMoves = new();
        private readonly HashSet<string> consumedCardIds = new(StringComparer.Ordinal);
        private readonly GameSimulator simulator = new();
        private GameStateData initialMirror;
        private GameStateData mirror;
        private int turnSeat = -1;

        public IReadOnlyList<MoveData> Moves => this.moves;
        public int ExpectedRevision { get; private set; } = -1;
        public string ClientNonce { get; private set; }
        public bool HasMoves => this.moves.Count > 0;
        public bool IsComplete => this.moves.Count > 0 && this.moves[this.moves.Count - 1].Type == EMoveType.EndTurn;
        public string NextCardId => this.mirror == null
            ? null : CardInstanceData.CreateId(this.mirror.GetNextCardInstanceSequence());

        public void Begin(MatchSeatViewData inSeatView, string inClientNonce = null)
        {
            if (inSeatView?.View?.Player == null)
                throw new ArgumentException("A complete seat view is required.", nameof(inSeatView));
            if (inSeatView.View.CurrentPlayerId != inSeatView.Seat)
                throw new InvalidOperationException("A turn buffer can only begin for the viewing seat's turn.");

            Clear();
            this.ExpectedRevision = inSeatView.View.Revision;
            this.turnSeat = inSeatView.Seat;
            this.ClientNonce = string.IsNullOrEmpty(inClientNonce) ? Guid.NewGuid().ToString("N") : inClientNonce;
            foreach (var move in inSeatView.View.LegalMoves)
                this.initialLegalMoves.Add(move.Clone());
            this.initialMirror = CreateMirror(inSeatView);
            this.mirror = this.initialMirror.Clone();
        }

        public bool TryAppend(MoveData inMove, out string outReason)
        {
            outReason = string.Empty;
            if (inMove == null)
                return Fail("Move is required.", out outReason);
            if (this.ExpectedRevision < 0 || string.IsNullOrEmpty(this.ClientNonce))
                return Fail("The turn buffer has not started.", out outReason);
            if (inMove.PlayerId != this.turnSeat)
                return Fail("Move does not belong to the buffered turn seat.", out outReason);
            if (this.IsComplete)
                return Fail("The turn is already complete.", out outReason);

            var moveToMatch = NormalizeActivation(inMove);
            MoveData acceptedMove = null;
            if (this.mirror != null)
            {
                try
                {
                    acceptedMove = FindMatching(this.simulator.GetLegalMoves(this.mirror, inMove.PlayerId), moveToMatch);
                    if (acceptedMove == null)
                    {
                        if (!IsTerminalEndMarker(this.mirror, moveToMatch))
                            return Fail("Move is not legal in the local turn state.", out outReason);
                        acceptedMove = moveToMatch.Clone();
                        acceptedMove.ExpectedRevision = this.mirror.Revision;
                        this.mirror.Moves.Add(acceptedMove.Clone());
                        ++this.mirror.Revision;
                    }
                    else
                        this.simulator.ApplyMove(this.mirror, acceptedMove);
                }
                catch (InvalidOperationException)
                {
                    // shortcut: masked decks prevent exact simulation after hidden-random effects; keep validating against the turn-start fair view.
                    acceptedMove = FindMatching(this.initialLegalMoves, moveToMatch);
                    this.mirror = null;
                }
            }
            else
            {
                acceptedMove = FindMatching(this.initialLegalMoves, moveToMatch);
            }

            if (acceptedMove == null)
                return Fail("Move is not legal in the confirmed seat view.", out outReason);
            if (UsesCard(acceptedMove) && !string.IsNullOrEmpty(acceptedMove.CardId)
                && this.consumedCardIds.Contains(acceptedMove.CardId))
                return Fail("The card was already used during this turn.", out outReason);

            var bufferedMove = acceptedMove.Clone();
            bufferedMove.ExpectedRevision = this.ExpectedRevision + this.moves.Count;
            this.moves.Add(bufferedMove);
            if (UsesCard(bufferedMove) && !string.IsNullOrEmpty(bufferedMove.CardId))
                this.consumedCardIds.Add(bufferedMove.CardId);
            return true;
        }

        public SubmitTurnRequestData CreateRequest(string inMatchId, int inSeat)
        {
            if (!this.IsComplete)
                throw new InvalidOperationException("The buffered turn must end with EndTurn before submission.");

            var request = new SubmitTurnRequestData
            {
                MatchId = inMatchId,
                Seat = inSeat,
                ExpectedRevision = this.ExpectedRevision,
                ClientNonce = this.ClientNonce
            };
            foreach (var move in this.moves)
                request.Moves.Add(move.Clone());
            return request;
        }

        public bool TryPop(out MoveData outMove, out string outReason)
        {
            outMove = null;
            outReason = string.Empty;
            if (this.IsComplete)
                return Fail("The turn is already complete.", out outReason);
            if (this.moves.Count == 0)
                return Fail("The turn buffer has no moves to undo.", out outReason);

            int index = this.moves.Count - 1;
            outMove = this.moves[index].Clone();
            this.moves.RemoveAt(index);
            RebuildTracking();
            return true;
        }

        public void Clear()
        {
            this.moves.Clear();
            this.initialLegalMoves.Clear();
            this.consumedCardIds.Clear();
            this.initialMirror = null;
            this.mirror = null;
            this.ExpectedRevision = -1;
            this.ClientNonce = null;
            this.turnSeat = -1;
        }

        private void RebuildTracking()
        {
            this.consumedCardIds.Clear();
            this.mirror = this.initialMirror?.Clone();
            foreach (var move in this.moves)
            {
                if (UsesCard(move) && !string.IsNullOrEmpty(move.CardId))
                    this.consumedCardIds.Add(move.CardId);
                if (this.mirror == null)
                    continue;

                try
                {
                    var acceptedMove = FindMatching(this.simulator.GetLegalMoves(this.mirror, move.PlayerId), move);
                    if (acceptedMove == null)
                    {
                        this.mirror = null;
                        continue;
                    }
                    this.simulator.ApplyMove(this.mirror, acceptedMove);
                }
                catch (InvalidOperationException)
                {
                    this.mirror = null;
                }
            }
        }

        private static GameStateData CreateMirror(MatchSeatViewData inSeatView)
        {
            var view = inSeatView.View;
            var state = new GameStateData
            {
                MatchId = inSeatView.MatchId,
                Revision = view.Revision,
                TurnNumber = view.TurnNumber,
                StartingPlayerId = view.StartingPlayerId,
                CurrentPlayerId = view.CurrentPlayerId,
                TurnPhase = view.TurnPhase,
                MovesRemaining = view.MovesRemaining,
                WinnerPlayerId = view.WinnerPlayerId,
                WinCondition = view.WinType,
                Rules = view.Rules?.Clone() ?? new RulesConfigData(),
                Random = new RandomStateData { State = 1 },
                NextCardInstanceId = view.NextCardInstanceId
            };
            state.Players.Add(view.Player.Clone());
            foreach (var opponent in view.Opponents)
            {
                var player = new PlayerStateData { Id = opponent.Id, Name = opponent.Name };
                foreach (var zone in opponent.Zones)
                    player.Zones.Add(zone.Clone());
                state.Players.Add(player);
            }
            return state;
        }

        private static MoveData NormalizeActivation(MoveData inMove)
        {
            if (inMove.Type != EMoveType.ActivateZone || string.IsNullOrEmpty(inMove.CardId))
                return inMove;

            var move = inMove.Clone();
            move.CardId = null;
            return move;
        }

        private static MoveData FindMatching(IEnumerable<MoveData> inMoves, MoveData inMove)
        {
            foreach (var candidate in inMoves)
            {
                if (MovesMatch(candidate, inMove))
                    return candidate;
            }
            return null;
        }

        private static bool MovesMatch(MoveData inFirst, MoveData inSecond)
        {
            return inFirst.PlayerId == inSecond.PlayerId
                && inFirst.Type == inSecond.Type
                && string.Equals(inFirst.CardId, inSecond.CardId, StringComparison.Ordinal)
                && inFirst.SourceZoneId == inSecond.SourceZoneId
                && inFirst.TargetPlayerId == inSecond.TargetPlayerId
                && inFirst.TargetZoneId == inSecond.TargetZoneId
                && inFirst.PowerCardType == inSecond.PowerCardType;
        }

        private static bool UsesCard(MoveData inMove)
        {
            return inMove.Type == EMoveType.PlayCard || inMove.Type == EMoveType.DiscardCard;
        }

        private bool IsTerminalEndMarker(GameStateData inState, MoveData inMove)
        {
            return this.moves.Count > 0 && inMove.Type == EMoveType.EndTurn
                && inMove.PlayerId == this.turnSeat
                && (inState.CurrentPlayerId != inMove.PlayerId
                    || inState.WinCondition != EWinCondition.None);
        }

        private static bool Fail(string inReason, out string outReason)
        {
            outReason = inReason;
            return false;
        }
    }

    public static class OnlineReconciliation
    {
        public static List<MoveLogEntryData> BuildLocalMoveLog(string inMatchId, int inSeat,
            string inClientNonce, int inStartingRevision, IReadOnlyList<MoveData> inMoves)
        {
            List<MoveLogEntryData> entries = new();
            if (inMoves == null)
                return entries;

            int revision = inStartingRevision;
            for (int i = 0; i < inMoves.Count; ++i)
            {
                if (inMoves[i] == null)
                    continue;

                var move = inMoves[i].Clone();
                move.ExpectedRevision = revision;
                entries.Add(new MoveLogEntryData
                {
                    MatchId = inMatchId,
                    Seat = inSeat,
                    ClientMoveSequence = i + 1,
                    ClientNonce = inClientNonce,
                    RevisionBefore = revision,
                    RevisionAfter = revision + 1,
                    Move = move
                });
                ++revision;
            }
            return entries;
        }

        public static List<CardInstanceData> FindNewHandCards(IEnumerable<string> inExistingCardIds,
            PlayerViewData inFinalView)
        {
            var existingCardIds = new HashSet<string>(inExistingCardIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            List<CardInstanceData> newCards = new();
            if (inFinalView?.Player?.Hand == null)
                return newCards;

            foreach (var card in inFinalView.Player.Hand)
            {
                if (card != null && !string.IsNullOrEmpty(card.Id) && !existingCardIds.Contains(card.Id))
                    newCards.Add(card);
            }
            return newCards;
        }

        public static bool RequiresRebuild(int inLastAppliedRevision, OnlineViewUpdateData inUpdate,
            MatchSeatViewData inProjectedView)
        {
            if (inUpdate?.View?.View == null)
                return true;
            if (!HasContinuousMoveLog(inLastAppliedRevision, inUpdate.Moves, inUpdate.View.View.Revision))
                return true;
            if (inProjectedView?.View == null)
                return true;
            return !string.Equals(NetJson.Serialize(inProjectedView), NetJson.Serialize(inUpdate.View),
                StringComparison.Ordinal);
        }

        public static bool HasContinuousMoveLog(int inLastAppliedRevision,
            IReadOnlyList<MoveLogEntryData> inMoves, int inFinalRevision)
        {
            int revision = inLastAppliedRevision;
            if (inMoves != null)
            {
                foreach (var entry in inMoves)
                {
                    if (entry == null || entry.RevisionBefore != revision || entry.RevisionAfter != revision + 1)
                        return false;
                    revision = entry.RevisionAfter;
                }
            }
            return revision == inFinalRevision;
        }
    }
}
