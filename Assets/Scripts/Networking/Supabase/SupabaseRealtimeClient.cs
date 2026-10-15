using System;
using System.Collections;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using UnityEngine;
#if !UNITY_WEBGL
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
#endif

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseRealtimeClient : IDisposable
    {
        public event Action<string, int> RevisionCommitted;
        public event Action<Exception> RequestFailed;

        private readonly MonoBehaviour runner;
        private readonly SupabaseAuthClient auth;
        private readonly string matchId;
        private readonly Func<Task<MatchSeatViewData>> fetchView;
        private Coroutine polling;
        private bool disposed;
        private string lastFailure;
#if !UNITY_WEBGL
        private readonly CancellationTokenSource cancellation = new();
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private ClientWebSocket socket;
        private int messageReference;
#endif

        public SupabaseRealtimeClient(MonoBehaviour inRunner, SupabaseAuthClient inAuth, string inMatchId, Func<Task<MatchSeatViewData>> inFetchView)
        {
            this.runner = inRunner;
            this.auth = inAuth;
            this.matchId = inMatchId;
            this.fetchView = inFetchView;
        }

        public void Start(bool inUseRealtime)
        {
            if (this.disposed)
                throw new ObjectDisposedException(nameof(SupabaseRealtimeClient));
            if (this.polling != null)
                return;
            // Poll in both modes: also recovers missed broadcasts and reconnect gaps.
            this.polling = this.runner.StartCoroutine(Poll());
#if !UNITY_WEBGL
            if (inUseRealtime)
                _ = ConnectAsync();
#endif
        }

        private IEnumerator Poll()
        {
            while (!this.disposed)
            {
                Task request = PollOnceAsync();
                while (!request.IsCompleted && !this.disposed)
                    yield return null;
                yield return new WaitForSecondsRealtime(3f);
            }
        }

        private async Task PollOnceAsync()
        {
            try
            {
                var view = await this.fetchView();
                this.lastFailure = null;
                if (view != null)
                    NotifyRevision(view.View.Revision);
            }
            catch (SupabaseHttpException exception) { ReportFailure(exception); }
            catch (Exception exception) { LogRealtimeFailure("polling", exception); }
        }

        private void NotifyRevision(int inRevision)
        {
            if (this.disposed)
                return;
            this.lastFailure = null;
            this.RevisionCommitted?.Invoke(this.matchId, inRevision);
        }

#if !UNITY_WEBGL
        private async Task ConnectAsync()
        {
            try
            {
                var session = await this.auth.EnsureSessionAsync();
                if (this.disposed)
                    return;
                this.socket = new ClientWebSocket();
                string url = SupabaseConfig.Url.Replace("https://", "wss://") + "/realtime/v1/websocket?apikey=" + Uri.EscapeDataString(SupabaseConfig.AnonKey) + "&vsn=1.0.0";
                await this.socket.ConnectAsync(new Uri(url), this.cancellation.Token);
                string payload = "{\"config\":{\"private\":true,\"broadcast\":{\"ack\":false,\"self\":false},\"presence\":{\"key\":\"\"}},\"access_token\":" + NetJson.Serialize(session.AccessToken) + "}";
                await SendAsync("realtime:match:" + this.matchId, "phx_join", payload);
                _ = HeartbeatAsync();
                var buffer = new byte[8192];
                while (!this.disposed && this.socket.State == WebSocketState.Open)
                {
                    using (var message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await this.socket.ReceiveAsync(new ArraySegment<byte>(buffer), this.cancellation.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                                return;
                            message.Write(buffer, 0, result.Count);
                            if (message.Length > 65536)
                                throw new InvalidOperationException("Realtime message is too large.");
                        } while (!result.EndOfMessage);
                        if (result.MessageType != WebSocketMessageType.Text)
                            continue;
                        var envelope = NetJson.Deserialize<RealtimeMessageData>(Encoding.UTF8.GetString(message.ToArray()));
                        if (envelope.Event == "phx_reply" && envelope.Payload?.Status == "error")
                        {
                            LogRealtimeFailure("subscription", new InvalidOperationException("Supabase Realtime rejected the private channel request."));
                            return;
                        }
                        if (envelope.Topic == "realtime:match:" + this.matchId && envelope.Event == "broadcast" && envelope.Payload?.Payload != null)
                            NotifyRevision(envelope.Payload.Payload.Revision);
                    }
                }
            }
            catch (Exception exception)
            {
                // Polling remains active when WebSockets or private channel auth fail.
                LogRealtimeFailure("connection", exception);
            }
        }

        private async Task HeartbeatAsync()
        {
            try
            {
                while (!this.disposed && this.socket.State == WebSocketState.Open)
                {
                    await Task.Delay(25000, this.cancellation.Token);
                    await SendAsync("phoenix", "heartbeat", "{}");
                    var session = await this.auth.EnsureSessionAsync();
                    await SendAsync("realtime:match:" + this.matchId, "access_token", "{\"access_token\":" + NetJson.Serialize(session.AccessToken) + "}");
                }
            }
            catch (Exception exception)
            {
                // Polling remains active after socket loss or disposal.
                if (!this.disposed)
                    LogRealtimeFailure("heartbeat", exception);
            }
        }

        private async Task SendAsync(string inTopic, string inEvent, string inPayload)
        {
            await this.sendLock.WaitAsync(this.cancellation.Token);
            try
            {
                string json = "{\"topic\":" + NetJson.Serialize(inTopic) + ",\"event\":" + NetJson.Serialize(inEvent) + ",\"payload\":" + inPayload + ",\"ref\":" + NetJson.Serialize((++this.messageReference).ToString()) + "}";
                await this.socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, this.cancellation.Token);
            }
            finally { this.sendLock.Release(); }
        }

        [Serializable]
        public sealed class RealtimeMessageData
        {
            [NetJsonName("topic")] public string Topic;
            [NetJsonName("event")] public string Event;
            [NetJsonName("payload")] public RealtimeBroadcastData Payload;
        }

        [Serializable]
        public sealed class RealtimeBroadcastData
        {
            [NetJsonName("status")] public string Status;
            [NetJsonName("payload")] public RealtimeRevisionData Payload;
        }

        [Serializable]
        public sealed class RealtimeRevisionData
        {
            [NetJsonName("revision")] public int Revision;
        }
#endif

        private void ReportFailure(Exception inException)
        {
            if (this.disposed)
                return;

            string failure = inException.GetType().Name + ":" + inException.Message;
            if (failure == this.lastFailure)
                return;

            this.lastFailure = failure;
            Debug.LogWarning("Supabase polling failed after retries: " + inException.Message);
            this.RequestFailed?.Invoke(inException);
        }

        private void LogRealtimeFailure(string inStage, Exception inException)
        {
            if (!this.disposed)
                Debug.LogWarning("Supabase realtime " + inStage + " failed; polling remains active: " + inException.Message);
        }

        public void Dispose()
        {
            if (this.disposed)
                return;
            this.disposed = true;
            if (this.polling != null && this.runner != null)
                this.runner.StopCoroutine(this.polling);
            this.polling = null;
#if !UNITY_WEBGL
            this.cancellation.Cancel();
            this.socket?.Abort();
            this.socket?.Dispose();
#endif
        }
    }
}
