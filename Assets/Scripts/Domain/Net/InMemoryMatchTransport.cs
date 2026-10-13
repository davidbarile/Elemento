using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    //Loopback "server": owns the authoritative state and never hands it to clients.
    public sealed class InMemoryMatchTransport : IMatchTransport
    {
        public event Action<string, int> RevisionCommitted;

        private readonly GameStateData state;
        private readonly MatchAuthority authority = new();
        private readonly Dictionary<string, MoveResultData> resultsByNonce = new();

        public string MatchId => this.state.MatchId;
        public int Revision => this.state.Revision;

        public InMemoryMatchTransport(GameStateData inAuthoritativeState)
        {
            this.state = inAuthoritativeState ?? throw new ArgumentNullException(nameof(inAuthoritativeState));
        }

        public MoveResultData Submit(MoveCommandData inCommand)
        {
            if (inCommand == null)
                throw new ArgumentNullException(nameof(inCommand));

            string nonceKey = inCommand.Seat + ":" + inCommand.ClientNonce;
            if (!string.IsNullOrEmpty(inCommand.ClientNonce) && this.resultsByNonce.TryGetValue(nonceKey, out var previous))
                return previous;//idempotent retry

            var result = this.authority.ValidateAndApply(this.state, inCommand);

            if (!string.IsNullOrEmpty(inCommand.ClientNonce))
                this.resultsByNonce[nonceKey] = result;

            if (result.Accepted)
                RevisionCommitted?.Invoke(this.state.MatchId, result.NewRevision);

            return result;
        }

        public MatchSeatViewData FetchSeatView(string inMatchId, int inSeat)
        {
            if (inMatchId != this.state.MatchId)
                throw new ArgumentException("Unknown match.", nameof(inMatchId));

            return FairView.BuildSeatView(this.state, inSeat);
        }
    }
}
