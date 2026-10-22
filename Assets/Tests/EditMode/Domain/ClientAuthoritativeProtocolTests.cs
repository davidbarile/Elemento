using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class ClientAuthoritativeProtocolTests
    {
        [Test]
        public void ProtocolV3_UsesContractFieldsAndRoundTripsRandomOutcomes()
        {
            Assert.That(ProtocolVersions.Current, Is.EqualTo(3));
            AssertFields<TurnActionData>("Index", "Move", "DrawnCards", "PowerCard", "Kind");
            AssertFields<TurnPackageData>("MatchId", "Seat", "TurnNumber", "StartRevision", "Actions",
                "FinalState", "FinalStateHash", "Status", "WinnerSeat", "WinCondition", "NextSeat");
            AssertFields<SubmitTurnRequestData>("ProtocolVersion", "MatchId", "Seat", "ExpectedRevision", "ClientNonce", "Turn");
            AssertFields<SubmitTurnResultData>("Accepted", "Reason", "NewRevision", "CurrentSeat", "Status");
            AssertFields<LeaveMatchResultData>("Status", "WinnerSeat", "MatchRevision", "WasAlreadyOver");
            AssertFields<CreateMatchRequestData>("ProtocolVersion", "Rules", "InitialState", "InitialStateHash");
            AssertFields<MatchStateUpdateData>("MatchId", "Status", "MatchRevision", "CurrentSeat", "YourSeat",
                "WinnerSeat", "OpponentLastSeenSecondsAgo", "Rules", "State", "Turns", "SeatNames");
            AssertFields<MyMatchData>("MatchId", "InviteCode", "Status", "MatchRevision", "CurrentSeat", "YourSeat", "OpponentName", "UpdatedAt");
            var request = Request("match", 0, 0);
            request.Turn.Actions.Add(new TurnActionData
            {
                Kind = "Move", Index = 0, Move = new MoveData { Type = EMoveType.ActivateZone },
                DrawnCards = new() { new CardInstanceData { Id = "draw", Points = new[] { 1, 2, 3, 4 } } },
                PowerCard = new CardInstanceData { Id = "power", IsPowerCard = true, WildFreeze = 5 }
            });
            string json = NetJson.Serialize(request);
            var restored = NetJson.Deserialize<SubmitTurnRequestData>(json);
            Assert.That(NetJson.Serialize(restored), Is.EqualTo(json));
            Assert.That(restored.Turn.Actions[0].PowerCard.WildFreeze, Is.EqualTo(5));
            Assert.That(restored.Turn.Actions[0].DrawnCards[0].Points, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            RoundTrip(new CreateMatchRequestData { InitialState = request.Turn.FinalState, InitialStateHash = "hash" });
            RoundTrip(new SubmitTurnResultData { Accepted = true, NewRevision = 4, Status = "active" });
            RoundTrip(new MatchStateUpdateData { Turns = new() { request.Turn }, SeatNames = new() { "A", "B" } });
            RoundTrip(new ListMyMatchesResultData { Matches = new() { new MyMatchData { InviteCode = "52MMXK", OpponentName = "B" } } });
            RoundTrip(new ListMyMatchesRequestData());
            RoundTrip(new GetViewRequestData { MatchId = "match", SinceRevision = 3 });
            RoundTrip(new JoinMatchRequestData { MatchId = "match" });
        }

        [Test]
        public async Task FirstTurnAfterCreateJoin_SerializesCompleteServerEnvelope()
        {
            var server = new InMemoryMatchTransport("Creator");
            var recording = new RecordingTransport(server);
            using var creator = new OnlineMatchClient(recording);
            using var opponent = new OnlineMatchClient(server.CreatePeer("Opponent"));
            MatchStateUpdateData created = await creator.CreateAsync(InitialMatchFactory.Create(null, 2));
            await opponent.JoinAsync(created.MatchId);
            await creator.RefreshAsync();

            var recorder = new TurnRecorder(creator.CurrentState.State.Clone(),
                creator.LastAppliedRevision);
            SubmitTurnResultData result = await creator.SubmitTurnAsync(recorder.EndTurn());
            string json = NetJson.Serialize(recording.LastSubmission);
            SubmitTurnRequestData request = NetJson.Deserialize<SubmitTurnRequestData>(json);

            Assert.That(result.Accepted, Is.True);
            Assert.That(request.Turn, Is.Not.Null);
            Assert.That(request.Turn.MatchId, Is.EqualTo(request.MatchId));
            Assert.That(request.Turn.Seat, Is.EqualTo(request.Seat));
            Assert.That(request.Turn.StartRevision, Is.EqualTo(request.ExpectedRevision));
            Assert.That(request.Turn.TurnNumber, Is.EqualTo(1));
            Assert.That(request.Turn.Actions, Is.Not.Null);
            Assert.That(request.Turn.FinalState, Is.Not.Null);
            Assert.That(request.Turn.FinalStateHash, Is.Not.Null);
            Assert.That(request.Turn.Status, Is.EqualTo("active").Or.EqualTo("finished"));
            Assert.That(request.Turn.NextSeat, Is.InRange(0, 1));
            Assert.That(request.Turn.WinnerSeat, Is.InRange(-1, 1));
            Assert.That(json, Does.Contain("\"WinnerSeat\":-1"));
        }

        [Test]
        public async Task Client_NormalizesServerAssignedMatchIdBeforeFirstSubmit()
        {
            var transport = new ServerAssignedMatchTransport(
                InitialMatchFactory.Create(null, 2), "server-match-id");
            using var client = new OnlineMatchClient(transport);
            await client.CreateAsync(transport.CreateRequest);
            Assert.That(client.CurrentState.State.MatchId, Is.EqualTo("server-match-id"));

            var recorder = new TurnRecorder(client.CurrentState.State.Clone(),
                client.LastAppliedRevision);
            SubmitTurnResultData result = await client.SubmitTurnAsync(recorder.EndTurn());

            Assert.That(result.Accepted, Is.True);
            Assert.That(transport.LastSubmission.MatchId, Is.EqualTo("server-match-id"));
            Assert.That(transport.LastSubmission.Turn.MatchId, Is.EqualTo("server-match-id"));
            Assert.That(transport.LastSubmission.Turn.FinalState.MatchId,
                Is.EqualTo("server-match-id"));
            Assert.That(transport.LastSubmission.Turn.FinalStateHash,
                Is.EqualTo(GameStateHasher.Compute(transport.LastSubmission.Turn.FinalState)));
        }

        [Test]
        public async Task Transport_CommitsOpaqueTurnOnceAndNeverAliasesCallerState()
        {
            var host = new InMemoryMatchTransport("A");
            var peer = host.CreatePeer("B");
            var initial = Setup("opaque");
            await host.CreateMatch(initial);
            initial.InitialState.Seed = 999;
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = "opaque" });
            var request = Request("opaque", 0, 0);
            request.Turn.Actions.Add(new TurnActionData { Kind = "Move", Move = new MoveData { CardId = "not-a-legal-card" } });
            request.Turn.FinalStateHash = "opaque-unverified-hash";
            var accepted = await host.SubmitTurn(request);
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.NewRevision, Is.EqualTo(1));
            request.Turn.FinalState.Seed = 888;
            accepted.NewRevision = 99;
            var retry = await host.SubmitTurn(request);
            Assert.That(retry.NewRevision, Is.EqualTo(1));
            var view = await peer.FetchView(new GetViewRequestData { MatchId = "opaque", SinceRevision = 0 });
            Assert.That(view.State.Seed, Is.EqualTo(42));
            Assert.That(view.Turns, Has.Count.EqualTo(1));
            view.State.Seed = 777;
            view.Turns.Clear();
            var again = await peer.FetchView(new GetViewRequestData { MatchId = "opaque", SinceRevision = 1 });
            Assert.That(again.State.Seed, Is.EqualTo(42));
            Assert.That(again.Turns, Is.Empty);
        }

        [Test]
        public async Task Transport_ChecksSeatsRevisionsStatusAndNonceOwnership()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            await host.CreateMatch(Setup("checks"));
            Assert.That((await host.SubmitTurn(Request("checks", 0, 0))).Reason, Is.EqualTo("match_not_active"));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = "checks" });
            Assert.That((await peer.SubmitTurn(Request("checks", 1, 0))).Reason, Is.EqualTo("not_your_turn"));
            Assert.That((await host.SubmitTurn(Request("checks", 0, 5))).Reason, Is.EqualTo("stale_revision"));
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await peer.SubmitTurn(Request("checks", 0, 0)));
            Assert.That((await host.SubmitTurn(Request("checks", 0, 0))).Accepted, Is.True);
            var otherNonce = Request("checks", 1, 1);
            otherNonce.ClientNonce = "0:0";
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await peer.SubmitTurn(otherNonce));
            await host.LeaveMatch("checks");
            Assert.That((await peer.SubmitTurn(Request("checks", 1, 1))).Reason, Is.EqualTo("match_not_active"));
            Assert.That((await host.SubmitTurn(Request("checks", 0, 0))).Accepted, Is.True);
        }

        [Test]
        public async Task LeaveMatch_ActiveMatchAbandonsWithOpponentAsWinner()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            await host.CreateMatch(Setup("leave-active"));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = "leave-active" });

            LeaveMatchResultData result = await host.LeaveMatch("leave-active");

            Assert.That(result.Status, Is.EqualTo("abandoned"));
            Assert.That(result.WinnerSeat, Is.EqualTo(1));
            Assert.That(result.MatchRevision, Is.EqualTo(1));
            Assert.That(result.WasAlreadyOver, Is.False);
        }

        [Test]
        public async Task LeaveMatch_AbandonedMatchReturnsUnchangedAlreadyOverResult()
        {
            var host = new InMemoryMatchTransport();
            var peer = host.CreatePeer();
            await host.CreateMatch(Setup("leave-again"));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = "leave-again" });
            LeaveMatchResultData first = await host.LeaveMatch("leave-again");

            LeaveMatchResultData second = await host.LeaveMatch("leave-again");

            Assert.That(second.Status, Is.EqualTo(first.Status));
            Assert.That(second.WinnerSeat, Is.EqualTo(first.WinnerSeat));
            Assert.That(second.MatchRevision, Is.EqualTo(first.MatchRevision));
            Assert.That(second.WasAlreadyOver, Is.True);
        }

        [Test]
        public async Task LeaveMatch_WaitingCreatorAbandonsMatch()
        {
            var host = new InMemoryMatchTransport();
            await host.CreateMatch(Setup("leave-waiting"));

            LeaveMatchResultData result = await host.LeaveMatch("leave-waiting");

            Assert.That(result.Status, Is.EqualTo("abandoned"));
            Assert.That(result.WinnerSeat, Is.EqualTo(-1));
            Assert.That(result.MatchRevision, Is.EqualTo(1));
            Assert.That(result.WasAlreadyOver, Is.False);
        }

        [Test]
        public void Transport_RejectsMismatchedTurnEnvelope()
        {
            var transport = new InMemoryMatchTransport();
            SubmitTurnRequestData request = Request("envelope", 0, 0);
            request.Turn.MatchId = "different-match";
            Assert.That(async () => await transport.SubmitTurn(request),
                Throws.ArgumentException.With.Message.Contains("Turn.MatchId must match MatchId"));
        }

        [Test]
        public async Task Transport_ListAndResumeIncludeOnlyOwnedUnfinishedMatches()
        {
            var host = new InMemoryMatchTransport("A");
            var peer = host.CreatePeer("B");
            var stranger = host.CreatePeer("C");
            await host.CreateMatch(Setup("list"));
            var waitingMatch = (await host.ListMyMatches(new ListMyMatchesRequestData())).Matches[0];
            Assert.That(waitingMatch.Status, Is.EqualTo("waiting"));
            Assert.That(waitingMatch.InviteCode, Has.Length.EqualTo(6));
            await peer.JoinMatch(new JoinMatchRequestData { MatchId = "list" });
            var resumed = await peer.JoinMatch(new JoinMatchRequestData { MatchId = "list" });
            Assert.That(resumed.YourSeat, Is.EqualTo(1));
            var matches = await peer.ListMyMatches(new ListMyMatchesRequestData());
            Assert.That(matches.Matches[0].OpponentName, Is.EqualTo("A"));
            Assert.That(matches.Matches[0].InviteCode, Is.EqualTo(waitingMatch.InviteCode));
            Assert.That((await stranger.ListMyMatches(new ListMyMatchesRequestData())).Matches, Is.Empty);
            Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await stranger.FetchView(new GetViewRequestData { MatchId = "list" }));
            var turn = Request("list", 0, 0);
            turn.Turn.Status = "finished";
            await host.SubmitTurn(turn);
            Assert.That((await host.ListMyMatches(new ListMyMatchesRequestData())).Matches, Is.Empty);
        }

        [Test]
        public void Transport_RejectsOldProtocol()
        {
            var transport = new InMemoryMatchTransport();
            var request = Setup("version");
            request.ProtocolVersion = 1;
            Assert.ThrowsAsync<InvalidOperationException>(async () => await transport.CreateMatch(request));
        }

        [Test]
        public async Task Client_AppliesOpponentTurnsExactlyOnceAcrossBroadcastAndPoll()
        {
            var host = new InMemoryMatchTransport();
            using var a = new OnlineMatchClient(host);
            using var b = new OnlineMatchClient(host.CreatePeer());
            await a.CreateAsync(Setup("client"));
            await b.JoinAsync("client");
            await a.RefreshAsync();
            int incoming = 0;
            int own = 0;
            b.OpponentTurnReceived += _ => ++incoming;
            a.OpponentTurnReceived += _ => ++own;
            var result = await a.SubmitTurnAsync(Request("client", 0, 0).Turn, "retry");
            Assert.That(result.Accepted, Is.True);
            await b.RefreshAsync(0);
            await b.RefreshAsync();
            Assert.That(incoming, Is.EqualTo(1));
            Assert.That(own, Is.Zero);
            Assert.That(a.LastAppliedRevision, Is.EqualTo(1));
            Assert.That(b.LastAppliedRevision, Is.EqualTo(1));
            Assert.That(b.IsLocalTurn, Is.True);
            Assert.That((await a.ListMyMatchesAsync()).Matches, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Client_DoesNotRegressOrNotifyForLateEqualRevisionUpdates()
        {
            var transport = new ReorderedTransport();
            using var client = new OnlineMatchClient(transport);
            int changes = 0;
            client.StateChanged += _ => ++changes;
            await client.CreateAsync(new CreateMatchRequestData());

            Task<MatchStateUpdateData> older = client.RefreshAsync(0);
            Task<MatchStateUpdateData> newer = client.RefreshAsync(0);
            transport.Requests[1].SetResult(Update("active", "A", "B"));
            await newer;
            transport.Requests[0].SetResult(Update("waiting", "A"));
            await older;

            Task<MatchStateUpdateData> unchanged = client.RefreshAsync(0);
            transport.Requests[2].SetResult(Update("active", "A", "B"));
            await unchanged;
            Assert.That(client.CurrentState.Status, Is.EqualTo("active"));
            Assert.That(changes, Is.EqualTo(2));
        }

        private static CreateMatchRequestData Setup(string inId)
        {
            return new CreateMatchRequestData
            {
                Rules = new RulesConfigData(), InitialState = new GameStateData { MatchId = inId, Seed = 42 },
                InitialStateHash = "opaque"
            };
        }

        private static SubmitTurnRequestData Request(string inId, int inSeat, int inRevision)
        {
            return new SubmitTurnRequestData
            {
                MatchId = inId, Seat = inSeat, ExpectedRevision = inRevision, ClientNonce = inSeat + ":" + inRevision,
                Turn = new TurnPackageData
                {
                    MatchId = inId, Seat = inSeat, StartRevision = inRevision, NextSeat = 1 - inSeat,
                    Status = "active", FinalState = new GameStateData { MatchId = inId, Seed = 42 },
                    FinalStateHash = "opaque-unverified-hash"
                }
            };
        }

        private static void AssertFields<T>(params string[] inNames)
        {
            Assert.That(typeof(T).GetFields().Select(inField => inField.Name), Is.EquivalentTo(inNames));
        }

        private static void RoundTrip<T>(T inValue)
        {
            string json = NetJson.Serialize(inValue);
            Assert.That(NetJson.Serialize(NetJson.Deserialize<T>(json)), Is.EqualTo(json));
        }

        private static MatchStateUpdateData Update(string inStatus, params string[] inNames)
        {
            return new MatchStateUpdateData
            {
                MatchId = "race", Status = inStatus, MatchRevision = 0, YourSeat = 0,
                State = new GameStateData { MatchId = "race" }, SeatNames = new(inNames)
            };
        }

        private sealed class ReorderedTransport : IAsyncMatchTransport
        {
            public Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest)
            {
                return Task.FromResult(new HeartbeatResultData());
            }
            public event Action<string, int> RevisionCommitted
            {
                add { }
                remove { }
            }
            public List<TaskCompletionSource<MatchStateUpdateData>> Requests = new();

            public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
            {
                return Task.FromResult(Update("waiting", "A"));
            }

            public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
            {
                throw new NotSupportedException();
            }

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                throw new NotSupportedException();
            }

            public Task<LeaveMatchResultData> LeaveMatch(string inMatchId)
            {
                throw new NotSupportedException();
            }

            public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
            {
                var request = new TaskCompletionSource<MatchStateUpdateData>();
                this.Requests.Add(request);
                return request.Task;
            }

            public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class RecordingTransport : IAsyncMatchTransport
        {
            public Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest)
            {
                return this.inner.Heartbeat(inRequest);
            }
            private readonly IAsyncMatchTransport inner;

            public event Action<string, int> RevisionCommitted
            {
                add { this.inner.RevisionCommitted += value; }
                remove { this.inner.RevisionCommitted -= value; }
            }

            public SubmitTurnRequestData LastSubmission { get; private set; }

            public RecordingTransport(IAsyncMatchTransport inInner)
            {
                this.inner = inInner;
            }

            public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
            {
                return this.inner.CreateMatch(inRequest);
            }

            public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
            {
                return this.inner.JoinMatch(inRequest);
            }

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                this.LastSubmission = NetJson.Deserialize<SubmitTurnRequestData>(
                    NetJson.Serialize(inTurn));
                return this.inner.SubmitTurn(inTurn);
            }

            public Task<LeaveMatchResultData> LeaveMatch(string inMatchId)
            {
                return this.inner.LeaveMatch(inMatchId);
            }

            public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
            {
                return this.inner.FetchView(inRequest);
            }

            public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
            {
                return this.inner.ListMyMatches(inRequest);
            }
        }

        private sealed class ServerAssignedMatchTransport : IAsyncMatchTransport
        {
            public Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest)
            {
                return Task.FromResult(new HeartbeatResultData());
            }
            private readonly string matchId;
            private MatchStateUpdateData update;

            public event Action<string, int> RevisionCommitted
            {
                add { }
                remove { }
            }

            public CreateMatchRequestData CreateRequest { get; }
            public SubmitTurnRequestData LastSubmission { get; private set; }

            public ServerAssignedMatchTransport(CreateMatchRequestData inCreateRequest,
                string inMatchId)
            {
                this.CreateRequest = inCreateRequest;
                this.matchId = inMatchId;
                this.update = new MatchStateUpdateData
                {
                    MatchId = inMatchId,
                    Status = "active",
                    MatchRevision = 0,
                    CurrentSeat = 0,
                    YourSeat = 0,
                    WinnerSeat = -1,
                    Rules = inCreateRequest.Rules,
                    State = NetJson.Deserialize<GameStateData>(
                        NetJson.Serialize(inCreateRequest.InitialState)),
                    SeatNames = new() { "Creator", "Opponent" }
                };
            }

            public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
            {
                return Task.FromResult(CopyUpdate());
            }

            public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
            {
                throw new NotSupportedException();
            }

            public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn)
            {
                this.LastSubmission = NetJson.Deserialize<SubmitTurnRequestData>(
                    NetJson.Serialize(inTurn));
                this.update = new MatchStateUpdateData
                {
                    MatchId = this.matchId,
                    Status = inTurn.Turn.Status,
                    MatchRevision = 1,
                    CurrentSeat = inTurn.Turn.NextSeat,
                    YourSeat = 0,
                    WinnerSeat = inTurn.Turn.WinnerSeat,
                    Rules = inTurn.Turn.FinalState.Rules,
                    State = inTurn.Turn.FinalState,
                    Turns = new() { inTurn.Turn },
                    SeatNames = new() { "Creator", "Opponent" }
                };
                return Task.FromResult(new SubmitTurnResultData
                {
                    Accepted = true,
                    NewRevision = 1,
                    CurrentSeat = inTurn.Turn.NextSeat,
                    Status = inTurn.Turn.Status
                });
            }

            public Task<LeaveMatchResultData> LeaveMatch(string inMatchId)
            {
                throw new NotSupportedException();
            }

            public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
            {
                return Task.FromResult(CopyUpdate());
            }

            public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
            {
                throw new NotSupportedException();
            }

            private MatchStateUpdateData CopyUpdate()
            {
                return NetJson.Deserialize<MatchStateUpdateData>(
                    NetJson.Serialize(this.update));
            }
        }
    }
}
