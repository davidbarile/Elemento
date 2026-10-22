using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Domain.Net;
using UnityEngine;

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseMatchTransport : IAsyncMatchTransport, IDisposable
    {
        public event Action<string, int> RevisionCommitted;
        public event Action<Exception> RequestFailed;
        public string LastInviteCode { get; private set; }

        private readonly MonoBehaviour runner;
        private readonly SupabaseAuthClient auth;
        private SupabaseRealtimeClient realtime;
        private OnlineMatchClient listeningClient;
        private MatchStateUpdateData polledUpdate;
        private bool disposed;

        public SupabaseMatchTransport(MonoBehaviour inRunner, SupabaseAuthClient inAuth)
        {
            this.runner = inRunner != null ? inRunner : throw new ArgumentNullException(nameof(inRunner));
            this.auth = inAuth ?? throw new ArgumentNullException(nameof(inAuth));
        }

        public async Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
        {
            if (inRequest == null) throw new ArgumentNullException(nameof(inRequest));
            string body = await Task.Run(() => NetJson.Serialize(new CreateMatchWireRequestData
            {
                ProtocolVersion = inRequest.ProtocolVersion,
                Rules = inRequest.Rules,
                InitialState = GameStateWireCodec.Encode(inRequest.InitialState),
                InitialStateHash = inRequest.InitialStateHash
            }));
            string json = await PostAsync("create-match", body);
            this.LastInviteCode = NetJson.Deserialize<InviteCodeData>(json).InviteCode;
            return await DecodeUpdateAsync(json);
        }

        public async Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
        {
            if (string.IsNullOrWhiteSpace(inRequest?.MatchId))
                throw new ArgumentException("A match id or invite code is required.", nameof(inRequest));
            // The domain join entry point also opens a saved match. Only invite codes claim a seat.
            if (Guid.TryParse(inRequest.MatchId, out _))
                return await FetchView(new GetViewRequestData { MatchId = inRequest.MatchId });
            string body = NetJson.Serialize(new JoinInviteData
            {
                ProtocolVersion = inRequest.ProtocolVersion,
                InviteCode = inRequest.MatchId.Trim().ToUpperInvariant()
            });
            return await DecodeUpdateAsync(await PostAsync("join-match", body));
        }

        public async Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
        {
            if (inTurn == null) throw new ArgumentNullException(nameof(inTurn));
            string body = await Task.Run(() => NetJson.Serialize(CreateWireRequest(inTurn)));
            SupabaseHttpResponseData response = await PostResponseAsync(
                "submit-turn", body);
            var result = NetJson.Deserialize<SubmitTurnResultData>(response.Body);
            result.HttpStatusCode = response.StatusCode;
            result.ResponseBody = response.Body;
            if (!result.Accepted)
            {
                Debug.LogWarning(SupabaseHttpClient.BuildFailureWarning(
                    "Online turn rejected", response.StatusCode, result.Reason, response.Body));
            }
            return result;
        }

        public async Task<LeaveMatchResultData> LeaveMatch(string inMatchId)
        {
            if (string.IsNullOrEmpty(inMatchId)) throw new ArgumentException("A match id is required.", nameof(inMatchId));
            string json = await PostAsync("leave-match", NetJson.Serialize(new GetViewRequestData { MatchId = inMatchId }));
            LeaveMatchResponseData response = NetJson.Deserialize<LeaveMatchResponseData>(json);
            if (response?.Match == null || string.IsNullOrEmpty(response.Match.Status))
                throw new FormatException("Missing leave-match result.");
            return new LeaveMatchResultData
            {
                Status = response.Match.Status,
                WinnerSeat = response.Match.WinnerSeat ?? -1,
                MatchRevision = response.Match.Revision
            };
        }

        public async Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest)
        {
            if (string.IsNullOrEmpty(inRequest?.MatchId))
                throw new ArgumentException("A match id is required.", nameof(inRequest));
            return NetJson.Deserialize<HeartbeatResultData>(
                await PostAsync("heartbeat", NetJson.Serialize(inRequest)));
        }

        public async Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
        {
            return NetJson.Deserialize<ListMyMatchesResultData>(await PostAsync("list-my-matches", NetJson.Serialize(inRequest)));
        }

        public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
        {
            if (this.polledUpdate != null && this.polledUpdate.MatchId == inRequest.MatchId
                && this.polledUpdate.MatchRevision >= inRequest.SinceRevision)
            {
                var update = this.polledUpdate;
                this.polledUpdate = null;
                return Task.FromResult(update);
            }
            this.polledUpdate = null;
            return FetchRemoteView(inRequest);
        }

        private async Task<MatchStateUpdateData> FetchRemoteView(GetViewRequestData inRequest)
        {
            for (int attempt = 1; ; ++attempt)
            {
                try
                {
                    string json = await PostAsync("get-view", NetJson.Serialize(inRequest));
                    var update = await DecodeUpdateAsync(json);
                    if (update?.State == null || update.MatchId != inRequest.MatchId)
                        throw new FormatException("Missing or mismatched match state.");
                    return update;
                }
                catch (SupabaseHttpException exception) when (attempt < 3 && (exception.ResponseCode == 0 || exception.ResponseCode >= 500))
                {
                    await Task.Delay(500 * attempt);
                }
            }
        }

        public void StartListening(OnlineMatchClient inClient)
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            this.realtime?.Dispose();
            this.listeningClient = inClient;
            this.realtime = new SupabaseRealtimeClient(this.runner, this.auth, inClient.MatchId,
                () => FetchRemoteView(new GetViewRequestData { MatchId = inClient.MatchId, SinceRevision = inClient.LastAppliedRevision }),
                () => inClient.HeartbeatAsync());
            this.realtime.StateReceived += HandlePolledState;
            this.realtime.RevisionCommitted += HandleRevisionCommitted;
            this.realtime.RequestFailed += HandleRequestFailed;
            this.realtime.Start(true);
        }

        private async void HandlePolledState(MatchStateUpdateData inUpdate)
        {
            if (this.disposed || inUpdate.MatchRevision < this.listeningClient.LastAppliedRevision) return;
            this.polledUpdate = inUpdate;
            // Joining changes waiting -> active without advancing the turn revision.
            // Apply this response even at the same revision; the client suppresses unchanged state.
            try { await this.listeningClient.RefreshAsync(); }
            catch (Exception exception) { HandleRequestFailed(exception); }
        }

        private void HandleRevisionCommitted(string inMatchId, int inRevision)
        {
            if (!this.disposed) this.RevisionCommitted?.Invoke(inMatchId, inRevision);
        }

        private void HandleRequestFailed(Exception inException)
        {
            if (!this.disposed) this.RequestFailed?.Invoke(inException);
        }

        private async Task<string> PostAsync(string inFunction, string inBody)
        {
            return (await PostResponseAsync(inFunction, inBody)).Body;
        }

        private async Task<SupabaseHttpResponseData> PostResponseAsync(string inFunction, string inBody)
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            var session = await this.auth.EnsureSessionAsync();
            return await this.auth.Http.SendWithStatusAsync(
                "/functions/v1/" + inFunction, inBody, session.AccessToken);
        }

        private static SubmitTurnWireRequestData CreateWireRequest(SubmitTurnRequestData inRequest)
        {
            return new SubmitTurnWireRequestData
            {
                ProtocolVersion = inRequest.ProtocolVersion,
                MatchId = inRequest.MatchId,
                Seat = inRequest.Seat,
                ExpectedRevision = inRequest.ExpectedRevision,
                ClientNonce = inRequest.ClientNonce,
                Turn = CreateWireTurn(inRequest.Turn)
            };
        }

        private static TurnPackageWireData CreateWireTurn(TurnPackageData inTurn)
        {
            return new TurnPackageWireData
            {
                MatchId = inTurn.MatchId,
                Seat = inTurn.Seat,
                TurnNumber = inTurn.TurnNumber,
                StartRevision = inTurn.StartRevision,
                Actions = inTurn.Actions,
                FinalState = GameStateWireCodec.Encode(inTurn.FinalState),
                FinalStateHash = inTurn.FinalStateHash,
                Status = inTurn.Status,
                WinnerSeat = inTurn.WinnerSeat,
                WinCondition = inTurn.WinCondition,
                NextSeat = inTurn.NextSeat
            };
        }

        private static Task<MatchStateUpdateData> DecodeUpdateAsync(string inJson)
        {
            return Task.Run(() =>
            {
                var wire = NetJson.Deserialize<MatchStateUpdateWireData>(inJson);
                if (wire == null)
                    return null;
                var result = new MatchStateUpdateData
                {
                    MatchId = wire.MatchId,
                    Status = wire.Status,
                    MatchRevision = wire.MatchRevision,
                    CurrentSeat = wire.CurrentSeat,
                    YourSeat = wire.YourSeat,
                    WinnerSeat = wire.WinnerSeat,
                    OpponentLastSeenSecondsAgo = wire.OpponentLastSeenSecondsAgo,
                    Rules = wire.Rules,
                    State = wire.State == null ? null : GameStateWireCodec.Decode(wire.State),
                    SeatNames = wire.SeatNames
                };
                foreach (var turn in wire.Turns)
                {
                    result.Turns.Add(new TurnPackageData
                    {
                        MatchId = turn.MatchId,
                        Seat = turn.Seat,
                        TurnNumber = turn.TurnNumber,
                        StartRevision = turn.StartRevision,
                        Actions = turn.Actions,
                        FinalState = GameStateWireCodec.Decode(turn.FinalState),
                        FinalStateHash = turn.FinalStateHash,
                        Status = turn.Status,
                        WinnerSeat = turn.WinnerSeat,
                        WinCondition = turn.WinCondition,
                        NextSeat = turn.NextSeat
                    });
                }
                return result;
            });
        }

        [Serializable]
        private sealed class InviteCodeData { public string InviteCode; }

        [Serializable]
        private sealed class JoinInviteData
        {
            public int ProtocolVersion;
            public string InviteCode;
        }

        [Serializable]
        private sealed class CreateMatchWireRequestData
        {
            public int ProtocolVersion;
            public RulesConfigData Rules;
            public GameStateWireData InitialState;
            public string InitialStateHash;
        }

        [Serializable]
        private sealed class SubmitTurnWireRequestData
        {
            public int ProtocolVersion;
            public string MatchId;
            public int Seat;
            public int ExpectedRevision;
            public string ClientNonce;
            public TurnPackageWireData Turn;
        }

        [Serializable]
        private sealed class TurnPackageWireData
        {
            public string MatchId;
            public int Seat;
            public int TurnNumber;
            public int StartRevision;
            public List<TurnActionData> Actions = new();
            public GameStateWireData FinalState;
            public string FinalStateHash;
            public string Status;
            public int WinnerSeat = -1;
            public EWinCondition WinCondition;
            public int NextSeat;
        }

        [Serializable]
        private sealed class MatchStateUpdateWireData
        {
            public string MatchId;
            public string Status;
            public int MatchRevision;
            public int CurrentSeat;
            public int YourSeat;
            public int WinnerSeat = -1;
            public int OpponentLastSeenSecondsAgo = -1;
            public RulesConfigData Rules;
            public GameStateWireData State;
            public List<TurnPackageWireData> Turns = new();
            public List<string> SeatNames = new();
        }

        [Serializable]
        private sealed class LeaveMatchResponseData
        {
            [NetJsonName("match")] public LeaveMatchData Match;
        }

        [Serializable]
        private sealed class LeaveMatchData
        {
            [NetJsonName("status")] public string Status;
            [NetJsonName("winner_seat")] public int? WinnerSeat;
            [NetJsonName("revision")] public int Revision;
        }

        public void Dispose()
        {
            this.disposed = true;
            this.realtime?.Dispose();
            this.realtime = null;
            this.polledUpdate = null;
            this.listeningClient = null;
        }
    }
}
