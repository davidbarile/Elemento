using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public sealed class MatchAuthority
    {
        public const string AcceptedReason = "Accepted";
        public const string ProtocolVersionReason = "Unsupported protocol version.";
        public const string MatchReason = "Command match id does not match the state.";
        public const string SeatReason = "Command seat is invalid.";
        public const string TurnReason = "It is not this seat's turn.";
        public const string RevisionReason = "Command revision is stale.";
        public const string MoveReason = "Move is not legal in the current state.";

        private readonly GameSimulator simulator;
        private readonly MatchFactory matchFactory;
        private readonly List<MoveLogEntryData> moveLog = new();

        public IReadOnlyList<MoveLogEntryData> MoveLog
        {
            get { return this.moveLog; }
        }

        public MatchAuthority()
        {
            this.simulator = new GameSimulator();
            this.matchFactory = new MatchFactory();
        }

        public MoveResultData ValidateAndApply(GameStateData inState, MoveCommandData inCommand)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
            if (inCommand == null)
                return Reject(inState.Revision, MoveReason);
            if (inCommand.ProtocolVersion != ProtocolVersions.Current)
                return Reject(inState.Revision, ProtocolVersionReason);
            if (!string.Equals(inCommand.MatchId, inState.MatchId, StringComparison.Ordinal))
                return Reject(inState.Revision, MatchReason);
            if (!HasSeat(inState, inCommand.Seat) || inCommand.Move == null || inCommand.Move.PlayerId != inCommand.Seat)
                return Reject(inState.Revision, SeatReason);
            if (inState.CurrentPlayerId != inCommand.Seat)
                return Reject(inState.Revision, TurnReason);
            if (inCommand.ExpectedRevision != inState.Revision)
                return Reject(inState.Revision, RevisionReason);

            MoveData legalMove = null;
            foreach (var candidate in this.simulator.GetLegalMoves(inState, inCommand.Seat))
            {
                if (MovesMatch(candidate, inCommand.Move))
                {
                    legalMove = candidate;
                    break;
                }
            }

            if (legalMove == null)
                return Reject(inState.Revision, MoveReason);

            int revisionBefore = inState.Revision;
            this.simulator.ApplyMove(inState, legalMove);
            var entry = new MoveLogEntryData
            {
                MatchId = inState.MatchId,
                Seat = inCommand.Seat,
                ClientMoveSequence = inCommand.ClientMoveSequence,
                ClientNonce = inCommand.ClientNonce,
                RevisionBefore = revisionBefore,
                RevisionAfter = inState.Revision,
                Move = legalMove.Clone()
            };
            this.moveLog.Add(entry);

            return new MoveResultData
            {
                Accepted = true,
                Reason = AcceptedReason,
                NewRevision = inState.Revision,
                LogEntry = entry
            };
        }

        public GameStateData Replay(int inSeed, RulesConfigData inRules,
            IList<IList<CardDefinitionData>> inDeckDefinitionsBySeat, IList<MoveLogEntryData> inMoveLog,
            int inStartingSeat = 0)
        {
            if (inMoveLog == null)
                throw new ArgumentNullException(nameof(inMoveLog));

            var state = this.matchFactory.Create(inSeed, inRules, inDeckDefinitionsBySeat, null,
                inStartingSeat);
            var replayAuthority = new MatchAuthority();
            MoveLogEntryData previousEntry = null;
            foreach (var entry in inMoveLog)
            {
                if (entry == null || entry.ProtocolVersion != ProtocolVersions.Current)
                    throw new InvalidOperationException("The move log contains an unsupported entry.");
                if (entry.RevisionBefore != state.Revision)
                    throw new InvalidOperationException("The move log revision sequence is invalid.");

                if (IsTerminalEndMarker(state, entry, previousEntry))
                {
                    var marker = entry.Move.Clone();
                    marker.ExpectedRevision = state.Revision;
                    state.Moves.Add(marker);
                    ++state.Revision;
                    if (state.Revision != entry.RevisionAfter)
                        throw new InvalidOperationException("The move log cannot be replayed from the supplied setup.");
                    previousEntry = entry;
                    continue;
                }

                var result = replayAuthority.ValidateAndApply(state, new MoveCommandData
                {
                    MatchId = entry.MatchId,
                    Seat = entry.Seat,
                    ClientMoveSequence = entry.ClientMoveSequence,
                    ClientNonce = entry.ClientNonce,
                    ExpectedRevision = entry.RevisionBefore,
                    Move = entry.Move == null ? null : entry.Move.Clone()
                });

                if (!result.Accepted || result.NewRevision != entry.RevisionAfter)
                    throw new InvalidOperationException("The move log cannot be replayed from the supplied setup.");
                previousEntry = entry;
            }

            return state;
        }

        private static bool IsTerminalEndMarker(GameStateData inState, MoveLogEntryData inEntry,
            MoveLogEntryData inPreviousEntry)
        {
            return inEntry.Move != null && inEntry.Move.Type == EMoveType.EndTurn
                && inEntry.Seat == inEntry.Move.PlayerId
                && inPreviousEntry?.Move != null
                && inPreviousEntry.Move.Type != EMoveType.EndTurn
                && inPreviousEntry.Seat == inEntry.Seat
                && inPreviousEntry.ClientNonce == inEntry.ClientNonce
                && inPreviousEntry.RevisionAfter == inEntry.RevisionBefore
                && (inState.CurrentPlayerId != inEntry.Seat
                    || inState.WinCondition != EWinCondition.None);
        }

        private static MoveResultData Reject(int inRevision, string inReason)
        {
            return new MoveResultData
            {
                Accepted = false,
                Reason = inReason,
                NewRevision = inRevision
            };
        }

        private static bool HasSeat(GameStateData inState, int inSeat)
        {
            foreach (var player in inState.Players)
            {
                if (player.Id == inSeat)
                    return true;
            }

            return false;
        }

        private static bool MovesMatch(MoveData inFirst, MoveData inSecond)
        {
            return inFirst.Type == inSecond.Type
                && string.Equals(inFirst.CardId, inSecond.CardId, StringComparison.Ordinal)
                && inFirst.SourceZoneId == inSecond.SourceZoneId
                && inFirst.TargetPlayerId == inSecond.TargetPlayerId
                && inFirst.TargetZoneId == inSecond.TargetZoneId
                && inFirst.PowerCardType == inSecond.PowerCardType;
        }
    }
}
