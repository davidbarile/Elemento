using System;

namespace Elemento.Domain.Net
{
    //Client-side state for one online match: only ever holds this seat's fair view.
    public sealed class OnlineMatchClient : IDisposable
    {
        public event Action<MatchSeatViewData> ViewChanged;

        private readonly IMatchTransport transport;
        private int clientMoveSequence;

        public string MatchId { get; }
        public int LocalSeat { get; }
        public MatchSeatViewData CurrentView { get; private set; }
        public int Revision => this.CurrentView == null ? -1 : this.CurrentView.View.Revision;
        public bool IsLocalTurn => this.CurrentView != null && this.CurrentView.View.CurrentPlayerId == this.LocalSeat;

        public OnlineMatchClient(IMatchTransport inTransport, string inMatchId, int inLocalSeat)
        {
            this.transport = inTransport ?? throw new ArgumentNullException(nameof(inTransport));
            this.MatchId = inMatchId;
            this.LocalSeat = inLocalSeat;
            this.transport.RevisionCommitted += HandleRevisionCommitted;
        }

        //async play: call on resume; live play: also triggered by RevisionCommitted
        public MatchSeatViewData Refresh()
        {
            this.CurrentView = this.transport.FetchSeatView(this.MatchId, this.LocalSeat);
            ViewChanged?.Invoke(this.CurrentView);
            return this.CurrentView;
        }

        public MoveResultData SubmitMove(MoveData inMove)
        {
            if (inMove == null)
                throw new ArgumentNullException(nameof(inMove));
            if (this.CurrentView == null)
                Refresh();

            var command = new MoveCommandData
            {
                MatchId = this.MatchId,
                Seat = this.LocalSeat,
                ClientMoveSequence = ++this.clientMoveSequence,
                ExpectedRevision = this.Revision,
                Move = inMove,
                ClientNonce = this.LocalSeat + "-" + this.clientMoveSequence
            };

            var result = this.transport.Submit(command);
            if (!result.Accepted)
                Refresh();//resync on stale/illegal

            return result;
        }

        private void HandleRevisionCommitted(string inMatchId, int inRevision)
        {
            if (inMatchId == this.MatchId && inRevision != this.Revision)
                Refresh();
        }

        public void Dispose()
        {
            this.transport.RevisionCommitted -= HandleRevisionCommitted;
        }
    }
}
