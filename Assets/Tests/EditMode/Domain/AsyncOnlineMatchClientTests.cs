using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class AsyncOnlineMatchClientTests
    {
        [Test]
        public async Task AsyncClient_SubmitsCommandAndRefreshesWithoutRealtime()
        {
            var transport = new AsyncTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            int changes = 0;
            client.ViewChanged += _ => ++changes;
            await client.RefreshAsync();
            Assert.That(client.IsLocalTurn, Is.True);
            MoveResultData submitted = null;
            await client.SubmitMoveAsync(new MoveData(), inResult => submitted = inResult);
            Assert.That(submitted.Accepted, Is.True);
            Assert.That(transport.LastCommand.Seat, Is.EqualTo(1));
            Assert.That(transport.LastCommand.ExpectedRevision, Is.Zero);
            Assert.That(transport.LastCommand.ClientNonce, Is.Not.Empty);
            Assert.That(client.Revision, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(2));
        }

        [Test]
        public async Task Refresh_ReportsFailureThroughCallback()
        {
            var transport = new AsyncTransport { Fail = true };
            using var client = new OnlineMatchClient(transport, "match", 1);
            Exception failure = null;
            await client.RefreshAsync(null, inError => failure = inError);
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(client.CurrentView, Is.Null);
        }

        [Test]
        public async Task RealtimeEvent_RefreshesAndDisposeUnsubscribes()
        {
            var transport = new AsyncTransport();
            var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();
            transport.RaiseRevision(2);
            Assert.That(client.Revision, Is.EqualTo(2));
            client.Dispose();
            transport.RaiseRevision(3);
            Assert.That(client.Revision, Is.EqualTo(2));
        }

        [Test]
        public async Task LateRefresh_CannotRollBackNewerRevision()
        {
            var transport = new DelayedTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            Task first = client.RefreshAsync();
            Task second = client.RefreshAsync();
            transport.Requests[1].SetResult(CreateView(5));
            await second;
            transport.Requests[0].SetResult(CreateView(3));
            await first;
            Assert.That(client.Revision, Is.EqualTo(5));
        }

        private static MatchSeatViewData CreateView(int inRevision)
        {
            return new MatchSeatViewData { MatchId = "match", Seat = 1, View = new PlayerViewData { Revision = inRevision, CurrentPlayerId = 1 } };
        }

        private sealed class AsyncTransport : IAsyncMatchTransport
        {
            public event Action<string, int> RevisionCommitted;
            public MoveCommandData LastCommand;
            public bool Fail;
            private int revision;

            public Task<MoveResultData> SubmitMove(MoveCommandData inCommand)
            {
                this.LastCommand = inCommand;
                ++this.revision;
                return Task.FromResult(new MoveResultData { Accepted = true, NewRevision = this.revision });
            }

            public Task<MatchSeatViewData> FetchSeatView(string inMatchId)
            {
                return this.Fail ? Task.FromException<MatchSeatViewData>(new InvalidOperationException("offline")) : Task.FromResult(CreateView(this.revision));
            }

            public void RaiseRevision(int inRevision)
            {
                this.revision = inRevision;
                this.RevisionCommitted?.Invoke("match", inRevision);
            }
        }

        private sealed class DelayedTransport : IAsyncMatchTransport
        {
            public event Action<string, int> RevisionCommitted { add { } remove { } }
            public List<TaskCompletionSource<MatchSeatViewData>> Requests = new();

            public Task<MatchSeatViewData> FetchSeatView(string inMatchId)
            {
                var completion = new TaskCompletionSource<MatchSeatViewData>();
                this.Requests.Add(completion);
                return completion.Task;
            }

            public Task<MoveResultData> SubmitMove(MoveCommandData inCommand)
            {
                throw new NotSupportedException();
            }
        }
    }
}
