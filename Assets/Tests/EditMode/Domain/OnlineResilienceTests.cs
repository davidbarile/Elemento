using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class OnlineResilienceTests
    {
        [Test]
        public void Presence_UsesElapsedTimeAndRearmsAfterWaitingOrReturn()
        {
            double now = 1000;
            var presence = new OpponentPresenceTracker(() => now);
            Assert.That(presence.IsAway, Is.False);
            presence.Observe(0);
            now += 19;
            Assert.That(presence.IsAway, Is.False);
            now += 1;
            Assert.That(presence.IsAway, Is.True);
            Assert.That(presence.TryPrompt(), Is.False);
            now += 100;
            Assert.That(presence.TryPrompt(), Is.True);
            Assert.That(presence.TryPrompt(), Is.False);
            presence.KeepWaiting();
            presence.Observe(120);
            now += 119;
            Assert.That(presence.TryPrompt(), Is.False);
            now += 1;
            Assert.That(presence.TryPrompt(), Is.True);
            presence.Observe(0);
            Assert.That(presence.IsAway, Is.False);
            Assert.That(presence.TryPrompt(), Is.False);
            now += 120;
            Assert.That(presence.TryPrompt(), Is.True);
            presence.Reset();
            Assert.That(presence.IsAway, Is.False);
            Assert.That(presence.TryPrompt(), Is.False);
        }

        [Test]
        public void Presence_UnknownStartsFallbackTimerWithoutResettingIt()
        {
            double now = 50;
            var presence = new OpponentPresenceTracker(() => now);
            presence.Observe(-1);
            now += 19;
            presence.Observe(-1);
            Assert.That(presence.IsAway, Is.False);
            now += 1;
            presence.Observe(-1);
            Assert.That(presence.IsAway, Is.True);
            now += 100;
            Assert.That(presence.TryPrompt(), Is.True);
        }

        [Test]
        public void Presence_AlreadyAwayUsesServerAgeWithoutClientWallClock()
        {
            var presence = new OpponentPresenceTracker(() => 5000000);
            presence.Observe(121);
            Assert.That(presence.IsAway, Is.True);
            Assert.That(presence.TryPrompt(), Is.True);
        }

        [Test]
        public async Task Heartbeat_UpdatesPresenceWithoutStateChangeOrForfeit()
        {
            double now = 0;
            var host = new InMemoryMatchTransport(inClock: () => now);
            var peer = host.CreatePeer();
            using var client = new OnlineMatchClient(host);
            var created = await client.CreateAsync(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            await client.RefreshAsync();
            int stateChanges = 0;
            int presenceAge = -1;
            client.StateChanged += _ => ++stateChanges;
            client.PresenceChanged += inAge => presenceAge = inAge;
            now = 130;
            await client.HeartbeatAsync();
            Assert.That(presenceAge, Is.EqualTo(130));
            Assert.That(client.CurrentState.OpponentLastSeenSecondsAgo, Is.EqualTo(130));
            Assert.That(stateChanges, Is.Zero);
            await peer.Heartbeat(new HeartbeatRequestData { MatchId = created.MatchId });
            await client.RefreshAsync();
            Assert.That(presenceAge, Is.Zero);
            Assert.That(stateChanges, Is.Zero);
            Assert.That(client.CurrentState.Status, Is.EqualTo("active"));
            Assert.That(client.LastAppliedRevision, Is.Zero);
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.Heartbeat(new HeartbeatRequestData { MatchId = created.MatchId, ProtocolVersion = 1 }));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await host.CreatePeer().Heartbeat(new HeartbeatRequestData { MatchId = created.MatchId }));
        }

        [Test]
        public async Task Resume_ReplaysMultipleMissedTurnsOnceFromSavedCheckpoint()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var created = await host.CreateMatch(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            var checkpoint = await host.FetchView(new GetViewRequestData { MatchId = created.MatchId });
            var state = checkpoint.State.Clone();
            for (int revision = 0; revision < 4; ++revision)
            {
                var turn = new TurnRecorder(state.Clone(), revision).EndTurn();
                var transport = turn.Seat == 0 ? host : peer;
                var result = await transport.SubmitTurn(new SubmitTurnRequestData
                {
                    MatchId = created.MatchId, Seat = turn.Seat, ExpectedRevision = revision,
                    ClientNonce = "missed-" + revision, Turn = turn
                });
                Assert.That(result.Accepted, Is.True);
                var replay = new TurnReplayer().Replay(state, turn);
                Assert.That(replay.IsMatch, Is.True, replay.Reason);
                state = replay.State;
            }

            using var client = new OnlineMatchClient(host);
            client.RestoreState(NetJson.Deserialize<MatchStateUpdateData>(NetJson.Serialize(checkpoint)));
            List<int> received = new();
            List<int> missed = new();
            client.OpponentTurnReceived += inTurn => received.Add(inTurn.StartRevision);
            client.MissedTurnReceived += inTurn => missed.Add(inTurn.StartRevision);
            var resumed = await client.ResumeAsync(created.MatchId);
            Assert.That(resumed.Turns, Has.Count.EqualTo(4));
            Assert.That(received, Is.Empty);
            Assert.That(missed, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(client.LastAppliedRevision, Is.EqualTo(4));
            Assert.That(GameStateHasher.Compute(resumed.State), Is.EqualTo(GameStateHasher.Compute(state)));
            await client.RefreshAsync(0);
            await client.RefreshAsync();
            Assert.That(received, Is.Empty);
            Assert.That(missed, Has.Count.EqualTo(4));
        }

        [Test]
        public async Task FetchLatestOpponentTurn_ReturnsPreviousFinalStateWithoutMutatingClient()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var created = await host.CreateMatch(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            List<TurnPackageData> turns = await SubmitTurns(host, peer, created, 4);
            var local = turns[0].Seat == 0 ? host : peer;
            using var client = new OnlineMatchClient(local);
            await client.ResumeAsync(created.MatchId);
            string before = NetJson.Serialize(client.CurrentState);
            int revision = client.LastAppliedRevision;
            int events = 0;
            client.OpponentTurnReceived += _ => ++events;
            client.MissedTurnReceived += _ => ++events;
            client.StateChanged += _ => ++events;
            client.RequestFailed += _ => ++events;
            client.PendingSubmissionChanged += _ => ++events;
            client.PresenceChanged += _ => ++events;

            OnlineReplayHistoryData history = await client.FetchLatestOpponentTurnAsync();

            Assert.That(history.Turn.StartRevision, Is.EqualTo(3));
            Assert.That(GameStateHasher.Compute(history.StartState),
                Is.EqualTo(GameStateHasher.Compute(turns[2].FinalState)));
            Assert.That(NetJson.Serialize(client.CurrentState), Is.EqualTo(before));
            Assert.That(client.LastAppliedRevision, Is.EqualTo(revision));
            Assert.That(events, Is.Zero);
        }

        [Test]
        public async Task FetchLatestOpponentTurn_RetriesForMissingPrecedingPackage()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var created = await host.CreateMatch(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            List<TurnPackageData> turns = await SubmitTurns(host, peer, created, 3);
            var local = turns[0].Seat == 0 ? host : peer;
            using var client = new OnlineMatchClient(local);
            await client.ResumeAsync(created.MatchId);

            OnlineReplayHistoryData history = await client.FetchLatestOpponentTurnAsync();

            Assert.That(history.Turn.StartRevision, Is.EqualTo(1));
            Assert.That(GameStateHasher.Compute(history.StartState),
                Is.EqualTo(GameStateHasher.Compute(turns[0].FinalState)));
        }

        [Test]
        public async Task FetchLatestOpponentTurn_ReturnsNullWhenOpponentTurnWasFirstPackage()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var created = await host.CreateMatch(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            List<TurnPackageData> turns = await SubmitTurns(host, peer, created, 1);
            var local = turns[0].Seat == 0 ? peer : host;
            using var client = new OnlineMatchClient(local);
            await client.ResumeAsync(created.MatchId);

            Assert.That(await client.FetchLatestOpponentTurnAsync(), Is.Null);
        }

        private static async Task<List<TurnPackageData>> SubmitTurns(InMemoryMatchTransport inHost,
            InMemoryMatchTransport inPeer, MatchStateUpdateData inCreated, int inCount)
        {
            List<TurnPackageData> turns = new();
            var state = inCreated.State.Clone();
            for (int revision = 0; revision < inCount; ++revision)
            {
                var turn = new TurnRecorder(state.Clone(), revision).EndTurn();
                var transport = turn.Seat == 0 ? inHost : inPeer;
                var result = await transport.SubmitTurn(new SubmitTurnRequestData
                {
                    MatchId = inCreated.MatchId, Seat = turn.Seat, ExpectedRevision = revision,
                    ClientNonce = "replay-" + inCount + "-" + revision, Turn = turn
                });
                Assert.That(result.Accepted, Is.True);
                turns.Add(turn);
                state = turn.FinalState.Clone();
            }
            return turns;
        }

        [Test]
        public async Task DefinitiveRejection_ClearsPendingSoResyncedTurnCanBeSubmitted()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            using var client = new OnlineMatchClient(host);
            var created = await client.CreateAsync(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            await client.RefreshAsync();
            var staleTurn = new TurnRecorder(client.CurrentState.State.Clone(), 99).EndTurn();
            var rejected = await client.SubmitTurnAsync(staleTurn);
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.Reason, Is.EqualTo("stale_revision"));
            Assert.That(client.PendingSubmission, Is.Null);
            var turn = new TurnRecorder(client.CurrentState.State.Clone(), 0).EndTurn();
            Assert.That((await client.SubmitTurnAsync(turn)).Accepted, Is.True);
        }

        [Test]
        public async Task CrashAfterCommit_RestoresNonceAndCommitsExactlyOnceBeforeResync()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var transport = new LostResponseTransport(host);
            string saved = null;
            string matchId;
            using (var client = new OnlineMatchClient(transport))
            {
                var created = await client.CreateAsync(InitialMatchFactory.Create(inSeed: 2));
                matchId = created.MatchId;
                await peer.JoinMatch(new JoinMatchRequestData { MatchId = matchId });
                await client.RefreshAsync();
                client.PendingSubmissionChanged += inPending => saved = NetJson.Serialize(inPending);
                var turn = new TurnRecorder(client.CurrentState.State.Clone(), 0).EndTurn();
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await client.SubmitTurnAsync(turn, "persisted-nonce"));
                Assert.That(client.PendingSubmission.ClientNonce, Is.EqualTo("persisted-nonce"));
            }
            using var restarted = new OnlineMatchClient(host);
            restarted.RestorePendingSubmission(NetJson.Deserialize<SubmitTurnRequestData>(saved));
            var result = await restarted.RetryPendingSubmissionAsync();
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.NewRevision, Is.EqualTo(1));
            Assert.That(restarted.PendingSubmission, Is.Null);
            var resumed = await restarted.ResumeAsync(matchId);
            Assert.That(resumed.MatchRevision, Is.EqualTo(1));
            Assert.That(resumed.Turns, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task LostAcknowledgement_SubmittingAgainRetriesTheSameNonce()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            var transport = new LostResponseTransport(host);
            using var client = new OnlineMatchClient(transport);
            var created = await client.CreateAsync(InitialMatchFactory.Create(inSeed: 2));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = created.MatchId });
            await client.RefreshAsync();
            var turn = new TurnRecorder(client.CurrentState.State.Clone(), 0).EndTurn();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await client.SubmitTurnAsync(turn, "same-nonce"));

            Assert.That((await client.SubmitTurnAsync(turn)).Accepted, Is.True);
            Assert.That(transport.Nonces, Is.EqualTo(new[] { "same-nonce", "same-nonce" }));
        }

        private sealed class LostResponseTransport : IAsyncMatchTransport
        {
            private readonly IAsyncMatchTransport inner;
            private bool loseNextResponse = true;

            public List<string> Nonces { get; } = new();

            public LostResponseTransport(IAsyncMatchTransport inInner)
            {
                this.inner = inInner;
            }

            public event Action<string, int> RevisionCommitted
            {
                add { }
                remove { }
            }

            public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest) => this.inner.CreateMatch(inRequest);
            public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest) => this.inner.JoinMatch(inRequest);
            public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest) => this.inner.FetchView(inRequest);
            public Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest) => this.inner.Heartbeat(inRequest);
            public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest) => this.inner.ListMyMatches(inRequest);
            public Task LeaveMatch(string inMatchId) => this.inner.LeaveMatch(inMatchId);

            public async Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                this.Nonces.Add(inTurn.ClientNonce);
                SubmitTurnResultData result = await this.inner.SubmitTurn(inTurn);
                if (this.loseNextResponse)
                {
                    this.loseNextResponse = false;
                    throw new InvalidOperationException("Simulated crash after server commit, before receiving response.");
                }
                return result;
            }
        }
    }
}
