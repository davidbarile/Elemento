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

        private readonly MonoBehaviour runner;
        private readonly SupabaseAuthClient auth;
        private readonly string matchId;
        private readonly Func<Task<MatchSeatViewData>> fetchView;
        private Coroutine polling;
        private bool disposed;
        private int revision = -1;
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
                NotifyRevision(view.View.Revision);
            }
            catch (Exception)
            {
                // A waiting match has no seat view until a second player joins.
                // Transient network failures are retried on the next polling tick.
            }
        }

        private void NotifyRevision(int inRevision)
        {
            if (this.disposed || inRevision <= this.revision)
                return;
            this.revision = inRevision;
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
                        if (envelope.Topic == "realtime:match:" + this.matchId && envelope.Event == "broadcast" && envelope.Payload?.Payload != null)
                            NotifyRevision(envelope.Payload.Payload.Revision);
                    }
                }
            }
            catch (Exception)
            {
                // Polling remains active when WebSockets or private channel auth fail.
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
            catch (Exception)
            {
                // Polling remains active after socket loss or disposal.
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
            [NetJsonName("payload")] public RealtimeRevisionData Payload;
        }

        [Serializable]
        public sealed class RealtimeRevisionData
        {
            [NetJsonName("revision")] public int Revision;
        }
#endif

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
