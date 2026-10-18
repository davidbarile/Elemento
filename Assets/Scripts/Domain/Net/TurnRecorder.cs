using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public sealed class TurnRecorder
    {
        private readonly GameSimulator simulator;
        private readonly int seat;
        private readonly int turnNumber;
        private readonly int startRevision;
        private readonly List<TurnActionData> actions = new();
        private readonly Stack<GameStateData> checkpoints = new();

        public GameStateData State { get; private set; }
        public IReadOnlyList<TurnActionData> Actions => this.actions;
        public bool IsComplete => this.State.CurrentPlayerId != this.seat || this.State.WinCondition != EWinCondition.None;
        public bool CanUndo => this.State.Rules.AllowUndo && !this.IsComplete && this.checkpoints.Count > 0;

        public TurnRecorder(GameStateData inState, int inStartRevision = 0, GameSimulator inSimulator = null)
        {
            this.State = inState ?? throw new ArgumentNullException(nameof(inState));
            this.simulator = inSimulator ?? new GameSimulator();
            this.seat = inState.CurrentPlayerId;
            this.turnNumber = inState.TurnNumber;
            this.startRevision = inStartRevision;
        }

        public void ApplyMove(MoveData inMove)
        {
            if (this.IsComplete)
                throw new InvalidOperationException("The recorded turn has already ended.");
            var before = this.State.Clone();
            this.simulator.ApplyMove(this.State, inMove);
            this.checkpoints.Push(before);
            var action = new TurnActionData
            {
                Index = this.actions.Count,
                Kind = inMove.Type == EMoveType.EndTurn ? "EndTurn" : "Move",
                Move = this.State.Moves[this.State.Moves.Count - 1].Clone()
            };
            HashSet<string> previousCards = new(StringComparer.Ordinal);
            foreach (var player in before.Players)
            {
                foreach (var card in player.Hand)
                    previousCards.Add(card.Id);
                foreach (var zone in player.Zones)
                    foreach (var card in zone.Cards)
                        previousCards.Add(card.Id);
            }
            foreach (var player in this.State.Players)
            {
                foreach (var card in player.Hand)
                {
                    if (previousCards.Contains(card.Id))
                        continue;
                    if (inMove.Type == EMoveType.ActivateZone && player.Id == this.seat && card.IsPowerCard)
                        action.PowerCard = card.Clone();
                    else
                        action.DrawnCards.Add(card.Clone());
                }
            }
            this.actions.Add(action);
        }

        public bool Undo()
        {
            if (!this.CanUndo)
                return false;
            this.State = this.checkpoints.Pop();
            this.actions.Add(new TurnActionData { Index = this.actions.Count, Kind = "Undo" });
            return true;
        }

        public TurnPackageData EndTurn()
        {
            if (!this.IsComplete)
            {
                var move = this.simulator.GetLegalMoves(this.State, this.seat)
                    .Find(inMove => inMove.Type == EMoveType.EndTurn);
                if (move == null)
                    throw new InvalidOperationException("This turn must end through an accepted domain action.");
                ApplyMove(move);
            }
            return BuildPackage();
        }

        public TurnPackageData BuildPackage()
        {
            if (!this.IsComplete)
                throw new InvalidOperationException("End the turn before building its package.");
            var package = new TurnPackageData
            {
                MatchId = this.State.MatchId,
                Seat = this.seat,
                TurnNumber = this.turnNumber,
                StartRevision = this.startRevision,
                FinalState = this.State.Clone(),
                FinalStateHash = GameStateHasher.Compute(this.State),
                Status = this.State.WinCondition == EWinCondition.None ? "active" : "finished",
                WinnerSeat = this.State.WinnerPlayerId,
                WinCondition = this.State.WinCondition,
                NextSeat = this.State.CurrentPlayerId
            };
            // Detach the wire package from both the recorder and callers' move objects.
            package.Actions = NetJson.Deserialize<List<TurnActionData>>(NetJson.Serialize(this.actions));
            return package;
        }
    }
}
