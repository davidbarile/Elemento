using System;
using System.Threading.Tasks;
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
            string json = await PostAsync("create-match", NetJson.Serialize(inRequest));
            this.LastInviteCode = NetJson.Deserialize<InviteCodeData>(json).InviteCode;
            return NetJson.Deserialize<MatchStateUpdateData>(json);
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
            return NetJson.Deserialize<MatchStateUpdateData>(await PostAsync("join-match", body));
        }

        public async Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
        {
            if (inTurn == null) throw new ArgumentNullException(nameof(inTurn));
            var result = NetJson.Deserialize<SubmitTurnResultData>(await PostAsync("submit-turn", NetJson.Serialize(inTurn)));
            if (!result.Accepted)
                Debug.LogWarning("Online turn rejected: " + result.Reason);
            return result;
        }

        public async Task LeaveMatch(string inMatchId)
        {
            if (string.IsNullOrEmpty(inMatchId)) throw new ArgumentException("A match id is required.", nameof(inMatchId));
            await PostAsync("leave-match", NetJson.Serialize(new GetViewRequestData { MatchId = inMatchId }));
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
                    var update = NetJson.Deserialize<MatchStateUpdateData>(await PostAsync("get-view", NetJson.Serialize(inRequest)));
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
                () => FetchRemoteView(new GetViewRequestData { MatchId = inClient.MatchId, SinceRevision = inClient.LastAppliedRevision }));
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
            if (this.disposed) throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            var session = await this.auth.EnsureSessionAsync();
            return await this.auth.Http.SendAsync("/functions/v1/" + inFunction, inBody, session.AccessToken);
        }

        [Serializable]
        private sealed class InviteCodeData { public string InviteCode; }

        [Serializable]
        private sealed class JoinInviteData
        {
            public int ProtocolVersion;
            public string InviteCode;
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
