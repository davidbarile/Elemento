using System;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Domain.Net;
using UnityEngine;

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseMatchData
    {
        public string MatchId;
        public string InviteCode;
        public string Mode;
        public string Status;
        public RulesConfigData Rules;
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

        public async Task<SupabaseMatchData> CreateMatch(string inMode, RulesConfigData inRules)
        {
            if (inMode != "live" && inMode != "async")
                throw new ArgumentException("Match mode must be live or async.", nameof(inMode));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));
            string body = "{\"mode\":" + NetJson.Serialize(inMode)
                + ",\"rules\":" + NetJson.Serialize(inRules) + "}";
            var response = SupabaseMatchResponseData.Parse(await PostAsync("create-match", body));
            return ToMatchData(response);
        }

        public async Task<SupabaseMatchData> JoinMatch(string inCode)
        {
            if (string.IsNullOrWhiteSpace(inCode))
                throw new ArgumentException("An invite code is required.", nameof(inCode));
            var response = SupabaseMatchResponseData.Parse(await PostAsync("join-match", "{\"inviteCode\":" + NetJson.Serialize(inCode.Trim().ToUpperInvariant()) + "}"));
            return ToMatchData(response);
        }

        public async Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
        {
            if (inTurn == null)
                throw new ArgumentNullException(nameof(inTurn));
            return SubmitTurnResponseData.Parse(await PostAsync("submit-turn", NetJson.Serialize(inTurn)));
        }

        public async Task LeaveMatch(string inMatchId)
        {
            if (string.IsNullOrEmpty(inMatchId))
                throw new ArgumentException("A match id is required.", nameof(inMatchId));
            await PostAsync("leave-match", "{\"matchId\":" + NetJson.Serialize(inMatchId) + "}");
        }

        public async Task<OnlineViewUpdateData> FetchView(string inMatchId, int? inSinceRevision = null)
        {
            SupabaseMatchResponseData response = null;
            for (int attempt = 1; attempt <= 3; ++attempt)
            {
                try
                {
                    string body = "{\"matchId\":" + NetJson.Serialize(inMatchId)
                        + (inSinceRevision.HasValue ? ",\"sinceRevision\":" + inSinceRevision.Value : string.Empty) + "}";
                    response = SupabaseMatchResponseData.Parse(await PostAsync("get-view", body));
                    break;
                }
                catch (SupabaseHttpException) when (attempt < 3)
                {
                    await Task.Delay(500 * attempt);
                }
            }

            if (response.View == null)
                return null;
            if (response.View.MatchId != inMatchId)
                throw new FormatException("Mismatched seat view.");
            return response.ToViewUpdate();
        }

        public void StartListening(string inMatchId, string inMode)
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(SupabaseMatchTransport));
            this.realtime?.Dispose();
            this.realtime = new SupabaseRealtimeClient(this.runner, this.auth, inMatchId,
                () => FetchView(inMatchId));
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
                Status = inResponse.Match.Status,
                Rules = inResponse.Match.Rules ?? inResponse.View?.View?.Rules,
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
