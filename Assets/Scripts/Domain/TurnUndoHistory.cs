using System.Collections.Generic;

namespace Elemento.Domain
{
    public enum ETurnUndoCheckpoint
    {
        None,
        Draw,
        RevealHiddenCard,
        ActivateDeck,
        ChoosePowerCard,
        EndTurn
    }

    public sealed class TurnUndoHistory<TState>
    {
        private readonly List<TState> states = new();

        public int Count => this.states.Count;
        public bool CanUndo => !this.IsLocked && this.states.Count > 0;
        public bool IsLocked { get; private set; }
        public ETurnUndoCheckpoint Checkpoint { get; private set; }

        public void BeginTurn(ETurnUndoCheckpoint inCheckpoint)
        {
            this.states.Clear();
            this.IsLocked = false;
            this.Checkpoint = inCheckpoint;
        }

        public void Record(TState inState)
        {
            if (this.IsLocked)
                return;

            this.states.Add(inState);
        }

        public void SetCheckpoint(ETurnUndoCheckpoint inCheckpoint)
        {
            this.states.Clear();
            this.Checkpoint = inCheckpoint;
        }

        public void LockTurn()
        {
            this.states.Clear();
            this.IsLocked = true;
            this.Checkpoint = ETurnUndoCheckpoint.EndTurn;
        }

        public void Reset()
        {
            this.states.Clear();
            this.IsLocked = false;
            this.Checkpoint = ETurnUndoCheckpoint.None;
        }

        public bool TryPop(out TState outState, out ETurnUndoCheckpoint outBlockedBy)
        {
            if (!this.CanUndo)
            {
                outState = default;
                outBlockedBy = this.IsLocked ? ETurnUndoCheckpoint.EndTurn : this.Checkpoint;
                return false;
            }

            int index = this.states.Count - 1;
            outState = this.states[index];
            this.states.RemoveAt(index);
            outBlockedBy = ETurnUndoCheckpoint.None;
            return true;
        }
    }
}
