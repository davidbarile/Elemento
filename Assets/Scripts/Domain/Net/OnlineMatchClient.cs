using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    //Client-side state for one online match: only ever holds this seat's fair view.
    public sealed class OnlineMatchClient : IDisposable
    {
        public event Action<MatchSeatViewData> ViewChanged;
        public event Action<Exception> RequestFailed;

        private readonly IMatchTransport transport;
        private readonly IAsyncMatchTransport asyncTransport;
        private bool disposed;
        private int clientMoveSequence;

        public string MatchId { get; }
        public int LocalSeat { get; }
        public MatchSeatViewData CurrentView { get; private set; }
        public int Revision => this.CurrentView == null ? -1 : this.CurrentView.View.Revision;
        public bool IsLocalTurn => this.CurrentView != null && this.CurrentView.View.CurrentPlayerId == this.LocalSeat;
        public bool UsesAsyncTransport => this.asyncTransport != null;

        public OnlineMatchClient(IAsyncMatchTransport inTransport, string inMatchId, int inLocalSeat)
        {
            this.asyncTransport = inTransport ?? throw new ArgumentNullException(nameof(inTransport));
            this.MatchId = inMatchId;
            this.LocalSeat = inLocalSeat;
            this.asyncTransport.RevisionCommitted += HandleRevisionCommitted;
        }

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
            if (this.transport == null)
                throw new InvalidOperationException("Use RefreshAsync for an asynchronous transport.");
            this.CurrentView = this.transport.FetchSeatView(this.MatchId, this.LocalSeat);
            ViewChanged?.Invoke(this.CurrentView);
            return this.CurrentView;
        }

        public MoveResultData SubmitMove(MoveData inMove)
        {
            if (this.transport == null)
                throw new InvalidOperationException("Use SubmitMoveAsync for an asynchronous transport.");
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
            if (inMatchId == this.MatchId && inRevision > this.Revision)
            {
                if (this.asyncTransport != null)
                    _ = RefreshAsync(null, inError => this.RequestFailed?.Invoke(inError));
                else
                    Refresh();
            }
        }

        public async Task RefreshAsync(Action<MatchSeatViewData> inCompleted = null, Action<Exception> inFailed = null)
        {
            try
            {
                var view = this.asyncTransport == null ? Refresh() : await this.asyncTransport.FetchSeatView(this.MatchId);
                if (this.disposed)
                    return;
                if (view == null || view.View == null || view.MatchId != this.MatchId || view.Seat != this.LocalSeat)
                    throw new InvalidOperationException("The server returned an invalid seat view.");
                if (this.asyncTransport != null && view.View.Revision >= this.Revision)
                {
                    this.CurrentView = view;
                    this.ViewChanged?.Invoke(view);
                }
                inCompleted?.Invoke(this.CurrentView);
            }
            catch (Exception error)
            {
                if (inFailed == null)
                    throw;
                inFailed(error);
            }
        }

        public async Task SubmitMoveAsync(MoveData inMove, Action<MoveResultData> inCompleted = null, Action<Exception> inFailed = null)
        {
            try
            {
                if (inMove == null)
                    throw new ArgumentNullException(nameof(inMove));
                if (this.disposed)
                    throw new ObjectDisposedException(nameof(OnlineMatchClient));
                if (this.asyncTransport == null)
                {
                    inCompleted?.Invoke(SubmitMove(inMove));
                    return;
                }
                if (this.CurrentView == null)
                    await RefreshAsync();
                var command = new MoveCommandData
                {
                    MatchId = this.MatchId,
                    Seat = this.LocalSeat,
                    ClientMoveSequence = ++this.clientMoveSequence,
                    ExpectedRevision = this.Revision,
                    Move = inMove,
                    ClientNonce = Guid.NewGuid().ToString("N")
                };
                var result = await this.asyncTransport.SubmitMove(command);
                await RefreshAsync();
                if (!this.disposed)
                    inCompleted?.Invoke(result);
            }
            catch (Exception error)
            {
                if (inFailed == null)
                    throw;
                inFailed(error);
            }
        }

        public void Dispose()
        {
            this.disposed = true;
            if (this.transport != null)
                this.transport.RevisionCommitted -= HandleRevisionCommitted;
            if (this.asyncTransport != null)
                this.asyncTransport.RevisionCommitted -= HandleRevisionCommitted;
        }
    }
}
