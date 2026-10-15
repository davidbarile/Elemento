using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    //Client-side state for one online match: only ever holds this seat's fair view.
    public sealed class OnlineMatchClient : IDisposable
    {
        public event Action<MatchSeatViewData> ViewChanged;
        public event Action<OnlineViewUpdateData> ViewUpdateReceived;
        public event Action<Exception> RequestFailed;

        private readonly IMatchTransport transport;
        private readonly IAsyncMatchTransport asyncTransport;
        private bool disposed;
        private int clientMoveSequence;
        private int lastAppliedRevision = -1;

        public string MatchId { get; }
        public int LocalSeat { get; }
        public MatchSeatViewData CurrentView { get; private set; }
        public int Revision => this.CurrentView == null ? -1 : this.CurrentView.View.Revision;
        public bool IsLocalTurn => this.CurrentView != null && this.CurrentView.View.CurrentPlayerId == this.LocalSeat;
        public bool UsesAsyncTransport => this.asyncTransport != null;
        public int LastAppliedRevision => this.lastAppliedRevision;
        public OnlineTurnBuffer TurnBuffer { get; } = new();

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
                throw new InvalidOperationException("Single-move submission is unavailable for an asynchronous transport.");
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
                    _ = RefreshAsync(this.lastAppliedRevision < 0 ? null : this.lastAppliedRevision,
                        null, inError => this.RequestFailed?.Invoke(inError));
                else
                    Refresh();
            }
        }

        public Task RefreshAsync(Action<MatchSeatViewData> inCompleted = null, Action<Exception> inFailed = null)
        {
            return RefreshAsync(null, inCompleted, inFailed);
        }

        public async Task RefreshAsync(int? inSinceRevision, Action<MatchSeatViewData> inCompleted = null,
            Action<Exception> inFailed = null)
        {
            try
            {
                if (this.asyncTransport == null)
                {
                    inCompleted?.Invoke(Refresh());
                    return;
                }

                var update = await this.asyncTransport.FetchView(this.MatchId, inSinceRevision);
                if (this.disposed)
                    return;
                var view = update?.View;
                if (view == null || view.View == null || view.MatchId != this.MatchId || view.Seat != this.LocalSeat)
                    throw new InvalidOperationException("The server returned an invalid seat view.");
                if (view.View.Revision >= this.Revision)
                {
                    this.CurrentView = view;
                    this.ViewChanged?.Invoke(view);
                    this.ViewUpdateReceived?.Invoke(update);
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

        public bool TryBufferMove(MoveData inMove, out string outReason)
        {
            if (inMove == null)
                throw new ArgumentNullException(nameof(inMove));
            if (this.CurrentView?.View == null)
            {
                outReason = "The confirmed seat view is unavailable.";
                return false;
            }

            if (!this.TurnBuffer.HasMoves && this.TurnBuffer.ExpectedRevision != this.Revision)
                this.TurnBuffer.Begin(this.CurrentView);
            return this.TurnBuffer.TryAppend(inMove, out outReason);
        }

        public bool TryUndoBufferedMove(out MoveData outMove, out string outReason)
        {
            return this.TurnBuffer.TryPop(out outMove, out outReason);
        }

        public async Task SubmitTurnAsync(MoveData inEndTurnMove,
            Action<SubmitTurnResultData> inCompleted = null, Action<Exception> inFailed = null)
        {
            try
            {
                if (this.disposed)
                    throw new ObjectDisposedException(nameof(OnlineMatchClient));
                if (this.asyncTransport == null)
                    throw new InvalidOperationException("Batched turns require an asynchronous transport.");
                if (!this.TurnBuffer.HasMoves && this.TurnBuffer.ExpectedRevision != this.Revision)
                    this.TurnBuffer.Begin(this.CurrentView);
                if (!this.TurnBuffer.IsComplete
                    && !this.TurnBuffer.TryAppend(inEndTurnMove, out string reason))
                    throw new InvalidOperationException(reason);

                var result = await this.asyncTransport.SubmitTurn(
                    this.TurnBuffer.CreateRequest(this.MatchId, this.LocalSeat));
                if (this.disposed)
                    return;

                if (result?.View?.View == null || result.View.MatchId != this.MatchId
                    || result.View.Seat != this.LocalSeat)
                    throw new InvalidOperationException("The server returned an invalid submit-turn seat view.");

                this.TurnBuffer.Clear();
                if (result.View.View.Revision >= this.Revision)
                {
                    var update = new OnlineViewUpdateData { View = result.View };
                    this.CurrentView = result.View;
                    this.ViewChanged?.Invoke(result.View);
                    this.ViewUpdateReceived?.Invoke(update);
                }
                inCompleted?.Invoke(result);
            }
            catch (Exception error)
            {
                if (inFailed == null)
                    throw;
                inFailed(error);
            }
        }

        public void DiscardBufferedTurn()
        {
            this.TurnBuffer.Clear();
        }

        public void MarkAppliedRevision(int inRevision)
        {
            if (inRevision > this.lastAppliedRevision)
                this.lastAppliedRevision = inRevision;
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
