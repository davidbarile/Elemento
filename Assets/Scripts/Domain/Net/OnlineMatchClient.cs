using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    public sealed class OnlineMatchClient : IDisposable
    {
        private readonly IAsyncMatchTransport transport;
        private int lastAppliedRevision = -1;
        private bool disposed;

        public event Action<TurnPackageData> OpponentTurnReceived;
        public event Action<MatchStateUpdateData> StateChanged;
        public event Action<Exception> RequestFailed;

        public MatchStateUpdateData CurrentState { get; private set; }
        public string MatchId => this.CurrentState?.MatchId;
        public int LocalSeat => this.CurrentState?.YourSeat ?? -1;
        public int LastAppliedRevision => this.lastAppliedRevision;
        public bool IsLocalTurn => this.CurrentState?.Status == "active"
            && this.CurrentState.CurrentSeat == this.LocalSeat;

        public OnlineMatchClient(IAsyncMatchTransport inTransport)
        {
            this.transport = inTransport ?? throw new ArgumentNullException(nameof(inTransport));
            this.transport.RevisionCommitted += HandleRevisionCommitted;
        }

        public async Task<MatchStateUpdateData> CreateAsync(CreateMatchRequestData inRequest)
        {
            CheckDisposed();
            return Apply(await this.transport.CreateMatch(inRequest), true);
        }

        public async Task<MatchStateUpdateData> JoinAsync(string inMatchId)
        {
            CheckDisposed();
            return Apply(await this.transport.JoinMatch(new JoinMatchRequestData { MatchId = inMatchId }), true);
        }

        public async Task<MatchStateUpdateData> RefreshAsync(int? inSinceRevision = null)
        {
            CheckDisposed();
            if (this.CurrentState == null)
                throw new InvalidOperationException("Create or join a match first.");
            return Apply(await this.transport.FetchView(new GetViewRequestData
            {
                MatchId = this.MatchId, SinceRevision = inSinceRevision ?? this.lastAppliedRevision
            }), false);
        }

        public async Task<SubmitTurnResultData> SubmitTurnAsync(TurnPackageData inTurn, string inClientNonce = null)
        {
            CheckDisposed();
            if (inTurn == null)
                throw new ArgumentNullException(nameof(inTurn));
            var result = await this.transport.SubmitTurn(new SubmitTurnRequestData
            {
                MatchId = this.MatchId, Seat = this.LocalSeat, ExpectedRevision = inTurn.StartRevision,
                ClientNonce = inClientNonce ?? this.MatchId + ":" + this.LocalSeat + ":" + inTurn.StartRevision,
                Turn = inTurn
            });
            await RefreshAsync();
            return result;
        }

        public Task<ListMyMatchesResultData> ListMyMatchesAsync()
        {
            CheckDisposed();
            return this.transport.ListMyMatches(new ListMyMatchesRequestData());
        }

        public Task LeaveMatchAsync()
        {
            CheckDisposed();
            return this.transport.LeaveMatch(this.MatchId);
        }

        private MatchStateUpdateData Apply(MatchStateUpdateData inUpdate, bool inInitial)
        {
            CheckDisposed();
            if (inUpdate?.State == null || string.IsNullOrEmpty(inUpdate.MatchId))
                throw new InvalidOperationException("Missing match state.");
            if (!inInitial && (inUpdate.MatchId != this.MatchId || inUpdate.YourSeat != this.LocalSeat))
                throw new InvalidOperationException("Unexpected match or seat.");
            if (!inInitial && inUpdate.MatchRevision < this.lastAppliedRevision)
                return this.CurrentState;
            if (!inInitial && inUpdate.MatchRevision == this.lastAppliedRevision)
            {
                if (IsStatusRegression(this.CurrentState.Status, inUpdate.Status)
                    || HaveSameSnapshot(this.CurrentState, inUpdate))
                {
                    return this.CurrentState;
                }
            }
            int previous = inInitial ? inUpdate.MatchRevision : this.lastAppliedRevision;
            this.CurrentState = inUpdate;
            this.lastAppliedRevision = inUpdate.MatchRevision;
            if (!inInitial)
            {
                foreach (var turn in inUpdate.Turns)
                {
                    if (turn.StartRevision >= previous && turn.Seat != this.LocalSeat)
                        this.OpponentTurnReceived?.Invoke(turn);
                }
            }
            this.StateChanged?.Invoke(inUpdate);
            return inUpdate;
        }

        private static bool IsStatusRegression(string inCurrent, string inIncoming)
        {
            int currentRank = StatusRank(inCurrent);
            int incomingRank = StatusRank(inIncoming);
            return incomingRank < currentRank
                || currentRank == 2 && !string.Equals(inCurrent, inIncoming, StringComparison.Ordinal);
        }

        private static int StatusRank(string inStatus)
        {
            if (inStatus == "active")
                return 1;
            if (inStatus == "finished" || inStatus == "abandoned")
                return 2;
            return 0;
        }

        private static bool HaveSameSnapshot(MatchStateUpdateData inFirst, MatchStateUpdateData inSecond)
        {
            if (inFirst.Status != inSecond.Status || inFirst.CurrentSeat != inSecond.CurrentSeat
                || inFirst.YourSeat != inSecond.YourSeat || inFirst.WinnerSeat != inSecond.WinnerSeat
                || inFirst.SeatNames.Count != inSecond.SeatNames.Count)
            {
                return false;
            }
            for (int i = 0; i < inFirst.SeatNames.Count; ++i)
                if (inFirst.SeatNames[i] != inSecond.SeatNames[i])
                    return false;
            return true;
        }

        private async void HandleRevisionCommitted(string inMatchId, int inRevision)
        {
            if (this.disposed || inMatchId != this.MatchId || inRevision <= this.lastAppliedRevision)
                return;
            try { await RefreshAsync(); }
            catch (Exception error)
            {
                if (!this.disposed)
                    this.RequestFailed?.Invoke(error);
            }
        }

        private void CheckDisposed()
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(OnlineMatchClient));
        }

        public void Dispose()
        {
            this.disposed = true;
            this.transport.RevisionCommitted -= HandleRevisionCommitted;
        }
    }
}
