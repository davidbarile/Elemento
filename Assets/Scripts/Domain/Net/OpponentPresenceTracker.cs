using System;

namespace Elemento.Domain.Net
{
    public sealed class OpponentPresenceTracker
    {
        private readonly Func<double> clock;
        private double lastSeen;
        private double nextPrompt;
        private bool known;
        private bool prompted;

        public bool IsAway => this.known && this.clock() - this.lastSeen >= 20;

        public OpponentPresenceTracker(Func<double> inClock)
        {
            this.clock = inClock ?? throw new ArgumentNullException(nameof(inClock));
        }

        public void Observe(int inSecondsAgo)
        {
            if (inSecondsAgo < 0)
            {
                if (!this.known)
                {
                    this.lastSeen = this.clock();
                    this.nextPrompt = this.lastSeen + 120;
                    this.known = true;
                    this.prompted = false;
                }
                return;
            }
            double now = this.clock();
            double observedLastSeen = now - inSecondsAgo;
            if (!this.known || inSecondsAgo < 20)
            {
                this.nextPrompt = observedLastSeen + 120;
                this.prompted = false;
            }
            this.lastSeen = observedLastSeen;
            this.known = true;
        }

        public bool TryPrompt()
        {
            if (!this.IsAway || this.prompted || this.clock() < this.nextPrompt)
                return false;
            this.prompted = true;
            return true;
        }

        public void KeepWaiting()
        {
            this.nextPrompt = this.clock() + 120;
            this.prompted = false;
        }

        public void Reset()
        {
            this.known = false;
            this.prompted = false;
        }
    }
}
