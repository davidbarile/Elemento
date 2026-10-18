using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    // Each endpoint represents an authenticated device; peers share the opaque storage.
    public sealed class InMemoryMatchTransport : IAsyncMatchTransport
    {
        private readonly Dictionary<string, StoredMatch> matches;
        private readonly List<InMemoryMatchTransport> endpoints;
        private readonly string name;
        private readonly Func<double> clock;

        public event Action<string, int> RevisionCommitted;

        public InMemoryMatchTransport(string inName = "Player", Func<double> inClock = null)
        {
            this.name = inName;
            this.clock = inClock ?? (() => DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond);
            this.matches = new();
            this.endpoints = new() { this };
        }

        private InMemoryMatchTransport(InMemoryMatchTransport inServer, string inName)
        {
            this.name = inName;
            this.clock = inServer.clock;
            this.matches = inServer.matches;
            this.endpoints = inServer.endpoints;
            this.endpoints.Add(this);
        }

        public InMemoryMatchTransport CreatePeer(string inName = "Opponent")
        {
            return new InMemoryMatchTransport(this, inName);
        }

        public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
        {
            var request = Copy(inRequest);
            CheckVersion(request.ProtocolVersion);
            if (request.InitialState == null || request.Rules == null)
                throw new ArgumentException("Initial state and rules are required.");
            lock (this.matches)
            {
                string id = request.InitialState.MatchId;
                if (string.IsNullOrEmpty(id))
                    id = Guid.NewGuid().ToString("N");
                var match = new StoredMatch
                {
                    State = request.InitialState,
                    Rules = request.Rules,
                    CurrentSeat = request.InitialState.CurrentPlayerId
                };
                match.State.MatchId = id;
                match.Seats.Add(this);
                this.matches.Add(id, match);
                return Task.FromResult(BuildView(id, match, -1));
            }
        }

        public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
        {
            var request = Copy(inRequest);
            CheckVersion(request.ProtocolVersion);
            lock (this.matches)
            {
                var match = this.matches[request.MatchId];
                if (!match.Seats.Contains(this))
                {
                    if (match.Seats.Count != 1 || match.Status != "waiting")
                        throw new InvalidOperationException("Match is unavailable.");
                    match.Seats.Add(this);
                    match.Status = "active";
                    match.UpdatedAt = DateTime.UtcNow.ToString("O");
                }
                return Task.FromResult(BuildView(request.MatchId, match, -1));
            }
        }

        public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
        {
            var request = Copy(inRequest);
            CheckVersion(request.ProtocolVersion);
            lock (this.matches)
                return Task.FromResult(BuildView(request.MatchId, this.matches[request.MatchId], request.SinceRevision));
        }

        public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
        {
            var request = Copy(inTurn);
            CheckVersion(request.ProtocolVersion);
            string envelopeFailure = GetTurnEnvelopeFailure(request);
            if (envelopeFailure != null)
                throw new ArgumentException("invalid turn package: " + envelopeFailure, nameof(inTurn));
            SubmitTurnResultData result;
            lock (this.matches)
            {
                var match = this.matches[request.MatchId];
                int seat = match.Seats.IndexOf(this);
                if (seat < 0 || request.Seat != seat)
                    throw new UnauthorizedAccessException("Seat does not belong to this device.");
                match.LastSeen[this] = this.clock();
                if (string.IsNullOrEmpty(request.ClientNonce))
                    throw new ArgumentException("ClientNonce is required.");
                if (match.Results.TryGetValue(request.ClientNonce, out var previous))
                {
                    if (previous.Seat != seat)
                        throw new UnauthorizedAccessException("Nonce belongs to another seat.");
                    return Task.FromResult(Copy(previous.Result));
                }
                string reason = match.Status != "active" ? "match_not_active"
                    : request.ExpectedRevision != match.Revision ? "stale_revision"
                    : seat != match.CurrentSeat ? "not_your_turn" : null;
                result = new SubmitTurnResultData
                {
                    Accepted = reason == null, Reason = reason,
                    NewRevision = match.Revision, CurrentSeat = match.CurrentSeat, Status = match.Status
                };
                if (reason != null)
                    return Task.FromResult(Copy(result));
                if (request.Turn?.FinalState == null)
                    throw new ArgumentException("A turn with final state is required.");
                // No simulator, legality, hash, or random-outcome validation occurs here.
                match.Turns.Add(request.Turn);
                match.State = request.Turn.FinalState;
                match.CurrentSeat = request.Turn.NextSeat;
                match.Status = request.Turn.Status;
                match.WinnerSeat = request.Turn.WinnerSeat;
                match.UpdatedAt = DateTime.UtcNow.ToString("O");
                ++match.Revision;
                result.NewRevision = match.Revision;
                result.CurrentSeat = match.CurrentSeat;
                result.Status = match.Status;
                match.Results.Add(request.ClientNonce, (seat, Copy(result)));
            }
            foreach (var endpoint in this.endpoints.ToArray())
                endpoint.RevisionCommitted?.Invoke(request.MatchId, result.NewRevision);
            return Task.FromResult(Copy(result));
        }

        public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
        {
            CheckVersion(Copy(inRequest).ProtocolVersion);
            var result = new ListMyMatchesResultData();
            lock (this.matches)
            {
                foreach (var pair in this.matches)
                {
                    var match = pair.Value;
                    int seat = match.Seats.IndexOf(this);
                    if (seat < 0 || (match.Status != "waiting" && match.Status != "active"))
                        continue;
                    result.Matches.Add(new MyMatchData
                    {
                        MatchId = pair.Key, Status = match.Status, MatchRevision = match.Revision,
                        InviteCode = match.InviteCode,
                        CurrentSeat = match.CurrentSeat, YourSeat = seat, UpdatedAt = match.UpdatedAt,
                        OpponentName = match.Seats.Count == 2 ? match.Seats[1 - seat].name : null
                    });
                }
            }
            return Task.FromResult(Copy(result));
        }

        public Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest)
        {
            CheckVersion(inRequest.ProtocolVersion);
            lock (this.matches)
            {
                var match = this.matches[inRequest.MatchId];
                return Task.FromResult(new HeartbeatResultData
                {
                    OpponentLastSeenSecondsAgo = StampPresence(match)
                });
            }
        }

        private int StampPresence(StoredMatch inMatch)
        {
            int seat = inMatch.Seats.IndexOf(this);
            if (seat < 0)
                throw new UnauthorizedAccessException("Device is not seated.");
            double now = this.clock();
            inMatch.LastSeen[this] = now;
            if (inMatch.Seats.Count < 2 || !inMatch.LastSeen.TryGetValue(inMatch.Seats[1 - seat], out double seen))
                return -1;
            return (int)Math.Max(0, Math.Floor(now - seen));
        }

        public Task<LeaveMatchResultData> LeaveMatch(string inMatchId)
        {
            string id = Copy(inMatchId);
            LeaveMatchResultData result;
            bool wasAbandoned = false;
            lock (this.matches)
            {
                var match = this.matches[id];
                int seat = match.Seats.IndexOf(this);
                if (seat < 0)
                    throw new UnauthorizedAccessException("Device is not seated.");
                bool wasAlreadyOver = match.Status == "finished" || match.Status == "abandoned";
                bool abandonsWaitingMatch = match.Status == "waiting" && seat == 0;
                if (match.Status == "active" || abandonsWaitingMatch)
                {
                    match.Status = "abandoned";
                    if (!abandonsWaitingMatch)
                        match.WinnerSeat = 1 - seat;
                    ++match.Revision;
                    match.UpdatedAt = DateTime.UtcNow.ToString("O");
                    wasAbandoned = true;
                }
                result = new LeaveMatchResultData
                {
                    Status = match.Status,
                    WinnerSeat = match.WinnerSeat,
                    MatchRevision = match.Revision,
                    WasAlreadyOver = wasAlreadyOver
                };
            }
            if (wasAbandoned)
                foreach (var endpoint in this.endpoints.ToArray())
                    endpoint.RevisionCommitted?.Invoke(id, result.MatchRevision);
            return Task.FromResult(Copy(result));
        }

        private MatchStateUpdateData BuildView(string inId, StoredMatch inMatch, int inSinceRevision)
        {
            int seat = inMatch.Seats.IndexOf(this);
            if (seat < 0)
                throw new UnauthorizedAccessException("Device is not seated.");
            var view = new MatchStateUpdateData
            {
                MatchId = inId, Status = inMatch.Status, MatchRevision = inMatch.Revision,
                CurrentSeat = inMatch.CurrentSeat, YourSeat = seat, WinnerSeat = inMatch.WinnerSeat,
                Rules = inMatch.Rules, State = inMatch.State,
                OpponentLastSeenSecondsAgo = StampPresence(inMatch)
            };
            foreach (var endpoint in inMatch.Seats)
                view.SeatNames.Add(endpoint.name);
            for (int i = Math.Max(0, inSinceRevision); i < inMatch.Turns.Count; ++i)
                view.Turns.Add(inMatch.Turns[i]);
            return Copy(view);
        }

        private static T Copy<T>(T inValue)
        {
            if (inValue == null)
                throw new ArgumentNullException(nameof(inValue));
            return NetJson.Deserialize<T>(NetJson.Serialize(inValue));
        }

        private static void CheckVersion(int inVersion)
        {
            if (inVersion != ProtocolVersions.Current)
                throw new InvalidOperationException("update_required: Please update Elemento to play online.");
        }

        private static string GetTurnEnvelopeFailure(SubmitTurnRequestData inRequest)
        {
            TurnPackageData turn = inRequest?.Turn;
            if (turn == null)
                return "Turn must be an object";
            if (string.IsNullOrEmpty(turn.MatchId))
                turn.MatchId = inRequest.MatchId;
            if (turn.MatchId != inRequest.MatchId)
                return "Turn.MatchId must match MatchId";
            if (turn.Seat != inRequest.Seat)
                return "Turn.Seat must match Seat";
            if (turn.StartRevision != inRequest.ExpectedRevision)
                return "Turn.StartRevision must match ExpectedRevision";
            if (turn.Actions == null)
                return "Turn.Actions must be an array";
            if (turn.FinalState == null)
                return "Turn.FinalState must be an object";
            if (turn.FinalStateHash == null)
                return "Turn.FinalStateHash must be a string";
            if (turn.Status != "active" && turn.Status != "finished")
                return "Turn.Status must be active or finished";
            if (turn.NextSeat != 0 && turn.NextSeat != 1)
                return "Turn.NextSeat must be 0 or 1";
            if (turn.WinnerSeat != -1 && turn.WinnerSeat != 0 && turn.WinnerSeat != 1)
                return "Turn.WinnerSeat must be -1, 0, or 1";
            return null;
        }

        private sealed class StoredMatch
        {
            public string InviteCode = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            public GameStateData State;
            public RulesConfigData Rules;
            public int Revision;
            public int CurrentSeat;
            public int WinnerSeat = -1;
            public string Status = "waiting";
            public string UpdatedAt = DateTime.UtcNow.ToString("O");
            public List<InMemoryMatchTransport> Seats = new();
            public List<TurnPackageData> Turns = new();
            public Dictionary<string, (int Seat, SubmitTurnResultData Result)> Results = new();
            public Dictionary<InMemoryMatchTransport, double> LastSeen = new();
        }
    }
}
