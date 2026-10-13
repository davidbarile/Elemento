using System;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using UnityEngine;

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseMatchData
    {
        public string MatchId;
        public string InviteCode;
        public string Mode;
        public int Seat;
        public MatchSeatViewData View;
    }

    public sealed class SupabaseMatchTransport : IAsyncMatchTransport, IDisposable
    {
        public event Action<string, int> RevisionCommitted;
        public event Action<Exception> RequestFailed;

        private readonly MonoBehaviour runner;
        private readonly SupabaseAuthClient auth;
        private SupabaseRealtimeClient realtime;
        private bool disposed;

        public SupabaseMatchTransport(MonoBehaviour inRunner, SupabaseAuthClient inAuth)
        {
            this.runner = inRunner != null ? inRunner : throw new ArgumentNullException(nameof(inRunner));
            this.auth = inAuth ?? throw new ArgumentNullException(nameof(inAuth));
        }

        public async Task<SupabaseMatchData> CreateMatch(string inMode)
        {
            if (inMode != "live" && inMode != "async")
                throw new ArgumentException("Match mode must be live or async.", nameof(inMode));
            var response = SupabaseMatchResponseData.Parse(await PostAsync("create-match", "{\"mode\":" + NetJson.Serialize(inMode) + "}"));
            return ToMatchData(response);
        }

        public async Task<SupabaseMatchData> JoinMatch(string inCode)
        {
            if (string.IsNullOrWhiteSpace(inCode))
                throw new ArgumentException("An invite code is required.", nameof(inCode));
            var response = SupabaseMatchResponseData.Parse(await PostAsync("join-match", "{\"inviteCode\":" + NetJson.Serialize(inCode.Trim().ToUpperInvariant()) + "}"));
            return ToMatchData(response);
        }

        public async Task<MoveResultData> SubmitMove(MoveCommandData inCommand)
        {
            if (inCommand == null)
                throw new ArgumentNullException(nameof(inCommand));
            return NetJson.Deserialize<MoveResultData>(await PostAsync("submit-move", NetJson.Serialize(inCommand)));
        }

        public async Task<MatchSeatViewData> FetchSeatView(string inMatchId)
        {
            var response = SupabaseMatchResponseData.Parse(await PostAsync("get-view", "{\"matchId\":" + NetJson.Serialize(inMatchId) + "}"));
            if (response.View == null || response.View.MatchId != inMatchId)
                throw new FormatException("Missing or mismatched seat view.");
            return response.View;
        }

        public void StartListening(string inMatchId, string inMode)
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            this.realtime?.Dispose();
            this.realtime = new SupabaseRealtimeClient(this.runner, this.auth, inMatchId, () => FetchSeatView(inMatchId));
            this.realtime.RevisionCommitted += HandleRevisionCommitted;
            this.realtime.RequestFailed += HandleRequestFailed;
            this.realtime.Start(inMode == "live");
        }

        private void HandleRevisionCommitted(string inMatchId, int inRevision)
        {
            if (!this.disposed)
                this.RevisionCommitted?.Invoke(inMatchId, inRevision);
        }

        private void HandleRequestFailed(Exception inException)
        {
            if (!this.disposed)
                this.RequestFailed?.Invoke(inException);
        }

        private async Task<string> PostAsync(string inFunction, string inBody)
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            var session = await this.auth.EnsureSessionAsync();
            return await this.auth.Http.SendAsync("/functions/v1/" + inFunction, inBody, session.AccessToken);
        }

        private static SupabaseMatchData ToMatchData(SupabaseMatchResponseData inResponse)
        {
            return new SupabaseMatchData
            {
                MatchId = inResponse.Match.Id,
                InviteCode = inResponse.Match.InviteCode,
                Mode = inResponse.Match.Mode,
                Seat = inResponse.Seat,
                View = inResponse.View
            };
        }

        public void Dispose()
        {
            this.disposed = true;
            this.realtime?.Dispose();
            this.realtime = null;
        }
    }
}
