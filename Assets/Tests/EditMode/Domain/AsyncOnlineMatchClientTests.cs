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
        public async Task AsyncClient_SubmitsBufferedTurnWithoutRealtime()
        {
            var transport = new AsyncTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            int changes = 0;
            client.ViewChanged += _ => ++changes;
            await client.RefreshAsync();
            Assert.That(client.IsLocalTurn, Is.True);
            SubmitTurnResultData submitted = null;
            await client.SubmitTurnAsync(CreateEndTurn(0), inResult => submitted = inResult);
            Assert.That(submitted.Accepted, Is.True);
            Assert.That(transport.LastTurn.Seat, Is.EqualTo(1));
            Assert.That(transport.LastTurn.ExpectedRevision, Is.Zero);
            Assert.That(transport.LastTurn.ClientNonce, Is.Not.Empty);
            Assert.That(transport.LastTurn.Moves[^1].Type, Is.EqualTo(EMoveType.EndTurn));
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
        public async Task SubmitTurn_NetworkFailureRetainsBufferAndNonceForRetry()
        {
            var transport = new AsyncTransport { FailSubmit = true };
            using var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();
            Exception failure = null;

            await client.SubmitTurnAsync(CreateEndTurn(0), null, inError => failure = inError);
            string nonce = client.TurnBuffer.ClientNonce;
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(client.TurnBuffer.IsComplete, Is.True);

            transport.FailSubmit = false;
            await client.SubmitTurnAsync(CreateEndTurn(0));
            Assert.That(transport.LastTurn.ClientNonce, Is.EqualTo(nonce));
            Assert.That(client.TurnBuffer.HasMoves, Is.False);
        }

        [Test]
        public async Task SubmitTurn_RejectionClearsBufferAndUsesAuthoritativeView()
        {
            var transport = new AsyncTransport { RejectSubmit = true };
            using var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();

            await client.SubmitTurnAsync(CreateEndTurn(0));

            Assert.That(client.TurnBuffer.HasMoves, Is.False);
            Assert.That(client.CurrentView.View.Revision, Is.Zero);
        }

        [Test]
        public async Task Refresh_ForwardsAppliedRevisionCursor()
        {
            var transport = new AsyncTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);

            await client.RefreshAsync(7);

            Assert.That(transport.LastSinceRevision, Is.EqualTo(7));
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
        public async Task RepeatedRevision_RetriesAfterTransientRefreshFailure()
        {
            var transport = new AsyncTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();

            transport.Fail = true;
            transport.RaiseRevision(2);
            Assert.That(client.Revision, Is.Zero);

            transport.Fail = false;
            transport.RaiseRevision(2);
            Assert.That(client.Revision, Is.EqualTo(2));
        }

        [Test]
        public async Task LateRefresh_CannotRollBackNewerRevision()
        {
            var transport = new DelayedTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            Task first = client.RefreshAsync();
            Task second = client.RefreshAsync();
            transport.Requests[1].SetResult(new OnlineViewUpdateData { View = CreateView(5) });
            await second;
            transport.Requests[0].SetResult(new OnlineViewUpdateData { View = CreateView(3) });
            await first;
            Assert.That(client.Revision, Is.EqualTo(5));
        }

        [Test]
        public async Task LateSubmitResponse_CannotRollBackNewerRealtimeView()
        {
            var transport = new SubmitRaceTransport();
            using var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();

            Task submit = client.SubmitTurnAsync(CreateEndTurn(0));
            transport.View = CreateView(2);
            await client.RefreshAsync();
            transport.SubmitCompletion.SetResult(new SubmitTurnResultData
            {
                Accepted = true,
                NewRevision = 1,
                MoveCount = 1,
                View = CreateView(1)
            });
            await submit;

            Assert.That(client.Revision, Is.EqualTo(2));
            Assert.That(client.TurnBuffer.HasMoves, Is.False);
        }

        [Test]
        public async Task SubmitTurn_DisposedBeforeResponseReportsFailure()
        {
            var transport = new SubmitRaceTransport();
            var client = new OnlineMatchClient(transport, "match", 1);
            await client.RefreshAsync();
            Exception failure = null;

            Task submit = client.SubmitTurnAsync(CreateEndTurn(0), null, inError => failure = inError);
            client.Dispose();
            transport.SubmitCompletion.SetResult(new SubmitTurnResultData
            {
                Accepted = true,
                NewRevision = 1,
                MoveCount = 1,
                View = CreateView(1)
            });
            await submit;

            Assert.That(failure, Is.TypeOf<ObjectDisposedException>());
        }

        private static MatchSeatViewData CreateView(int inRevision)
        {
            var view = new PlayerViewData
            {
                Revision = inRevision,
                TurnNumber = 1,
                CurrentPlayerId = 1,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 3,
                Rules = new RulesConfigData { MovesPerTurn = 3 },
                Player = new PlayerStateData { Id = 1 }
            };
            view.LegalMoves.Add(CreateEndTurn(inRevision));
            return new MatchSeatViewData { MatchId = "match", Seat = 1, View = view };
        }

        private static MoveData CreateEndTurn(int inRevision)
        {
            return new MoveData
            {
                PlayerId = 1,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1,
                ExpectedRevision = inRevision
            };
        }

        private sealed class AsyncTransport : IAsyncMatchTransport
        {
            public event Action<string, int> RevisionCommitted;
            public SubmitTurnRequestData LastTurn;
            public bool Fail;
            public bool FailSubmit;
            public bool RejectSubmit;
            public int? LastSinceRevision;
            private int revision;

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                this.LastTurn = inTurn;
                if (this.FailSubmit)
                    return Task.FromException<SubmitTurnResultData>(new InvalidOperationException("offline"));
                if (this.RejectSubmit)
                {
                    return Task.FromResult(new SubmitTurnResultData
                    {
                        Accepted = false,
                        Reason = "illegal",
                        NewRevision = this.revision,
                        View = CreateView(this.revision)
                    });
                }
                ++this.revision;
                return Task.FromResult(new SubmitTurnResultData
                {
                    Accepted = true,
                    NewRevision = this.revision,
                    MoveCount = inTurn.Moves.Count,
                    View = CreateView(this.revision)
                });
            }

            public Task<OnlineViewUpdateData> FetchView(string inMatchId, int? inSinceRevision = null)
            {
                this.LastSinceRevision = inSinceRevision;
                return this.Fail
                    ? Task.FromException<OnlineViewUpdateData>(new InvalidOperationException("offline"))
                    : Task.FromResult(new OnlineViewUpdateData { View = CreateView(this.revision) });
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
            public List<TaskCompletionSource<OnlineViewUpdateData>> Requests = new();

            public Task<OnlineViewUpdateData> FetchView(string inMatchId, int? inSinceRevision = null)
            {
                var completion = new TaskCompletionSource<OnlineViewUpdateData>();
                this.Requests.Add(completion);
                return completion.Task;
            }

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class SubmitRaceTransport : IAsyncMatchTransport
        {
            public event Action<string, int> RevisionCommitted { add { } remove { } }
            public MatchSeatViewData View = CreateView(0);
            public TaskCompletionSource<SubmitTurnResultData> SubmitCompletion = new();

            public Task<OnlineViewUpdateData> FetchView(string inMatchId, int? inSinceRevision = null)
            {
                return Task.FromResult(new OnlineViewUpdateData { View = this.View });
            }

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                return this.SubmitCompletion.Task;
            }
        }
    }
}
