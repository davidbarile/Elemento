using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Domain.Net;
using NUnit.Framework;

public class OnlineSessionTests
{
    [SetUp]
    public void SetUp()
    {
        OnlineMatchContext.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        OnlineMatchContext.Clear();
    }

    [Test]
    public void MultiplayerWithoutActiveMatch_FallsBackToLocalAi()
    {
        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.Multiplayer), Is.TypeOf<LocalAiSession>());
        Assert.That(OnlineMatchContext.LocalSeat, Is.EqualTo(-1));
    }

    [TestCase(0)]
    [TestCase(1)]
    public async Task MultiplayerWithActiveMatch_UsesClientAndLocalSeat(int inLocalSeat)
    {
        using var client = await CreateClient(inLocalSeat);
        OnlineMatchContext.Set(client);

        var session = SessionFactory.Create(SettingsManager.EPlayMode.Multiplayer) as OnlineSession;

        Assert.That(session, Is.Not.Null);
        Assert.That(session.Client, Is.SameAs(client));
        Assert.That(session.LocalSeat, Is.EqualTo(inLocalSeat));
        Assert.That(session.GetViewingSeat(1 - inLocalSeat), Is.EqualTo(inLocalSeat));
        Assert.That(session.ShouldRunAI(1 - inLocalSeat), Is.False);
    }

    [Test]
    public async Task ActiveMatch_DoesNotChangeLocalModeSelection()
    {
        using var client = await CreateClient(1);
        OnlineMatchContext.Set(client);

        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.AgainstAI), Is.TypeOf<LocalAiSession>());
        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.PassAndPlay), Is.TypeOf<PassAndPlaySession>());
    }

    [Test]
    public async Task ClearingContext_RestoresMultiplayerFallback()
    {
        using var client = await CreateClient(1);
        OnlineMatchContext.Set(client);
        OnlineMatchContext.Clear();

        Assert.That(OnlineMatchContext.IsActive, Is.False);
        Assert.That(OnlineMatchContext.Client, Is.Null);
        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.Multiplayer), Is.TypeOf<LocalAiSession>());
    }

    [Test]
    public void ContextRejectsMissingClient()
    {
        Assert.Throws<ArgumentNullException>(() => OnlineMatchContext.Set(null));
    }

    private static async Task<OnlineMatchClient> CreateClient(int inLocalSeat)
    {
        var client = new OnlineMatchClient(new MemoryTransport(inLocalSeat));
        await client.JoinAsync("session-selection");
        return client;
    }

    [Test]
    public async Task RefreshSameRevision_DoesNotInterruptLocalTurn()
    {
        var transport = new MemoryTransport(0);
        using var client = new OnlineMatchClient(transport);
        await client.JoinAsync("session-selection");
        int changes = 0;
        client.StateChanged += _ => changes++;

        await client.RefreshAsync();

        Assert.That(changes, Is.Zero);
        Assert.That(client.IsLocalTurn, Is.True);
        Assert.That(transport.LastFetch.SinceRevision, Is.EqualTo(client.LastAppliedRevision));
        Assert.That(transport.FetchCount, Is.EqualTo(1));
    }

    [Test]
    public async Task SubmitTurn_SendsCompletePackageAndRefreshesFromAppliedRevision()
    {
        var transport = new MemoryTransport(0);
        using var client = new OnlineMatchClient(transport);
        await client.JoinAsync("session-selection");
        var recorder = new TurnRecorder(client.CurrentState.State.Clone(), client.LastAppliedRevision);
        var package = recorder.EndTurn();

        var result = await client.SubmitTurnAsync(package, "turn-0");

        Assert.That(result.Accepted, Is.True);
        Assert.That(transport.LastSubmission.Turn, Is.SameAs(package));
        Assert.That(transport.LastSubmission.ClientNonce, Is.EqualTo("turn-0"));
        Assert.That(transport.LastSubmission.ExpectedRevision, Is.Zero);
        Assert.That(transport.LastSubmission.ProtocolVersion, Is.EqualTo(2));
        Assert.That(transport.LastFetch.SinceRevision, Is.Zero);
        Assert.That(client.LastAppliedRevision, Is.EqualTo(1));
        Assert.That(client.IsLocalTurn, Is.False);
        Assert.That(package.Actions[package.Actions.Count - 1].Kind, Is.EqualTo("EndTurn"));
        Assert.That(GameStateHasher.Compute(client.CurrentState.State), Is.EqualTo(package.FinalStateHash));
    }

    [Test]
    public async Task OpponentPackage_ReplaysEndTurnAndIsDeliveredOnlyOnce()
    {
        var transport = new MemoryTransport(1);
        using var client = new OnlineMatchClient(transport);
        await client.JoinAsync("session-selection");
        var initial = client.CurrentState.State.Clone();
        var package = new TurnRecorder(initial.Clone(), 0).EndTurn();
        List<TurnPackageData> received = new();
        client.OpponentTurnReceived += received.Add;
        transport.Commit(package);

        await client.RefreshAsync();
        await client.RefreshAsync();

        Assert.That(received, Has.Count.EqualTo(1));
        var replay = new TurnReplayer().Replay(initial, received[0]);
        Assert.That(replay.IsMatch, Is.True, replay.Reason);
        Assert.That(replay.State.CurrentPlayerId, Is.EqualTo(1));
        Assert.That(client.IsLocalTurn, Is.True);
        Assert.That(new OnlineSession(1, client).GetViewingSeat(0), Is.EqualTo(1));
    }

    [Test]
    public async Task ResumeStoredMatch_UsesLatestStateWithoutReplayingHistoricalTurns()
    {
        var transport = new MemoryTransport(1);
        var package = new TurnRecorder(transport.Snapshot.State.Clone(), 0).EndTurn();
        transport.Commit(package);
        using var client = new OnlineMatchClient(transport);
        int replayCount = 0;
        client.OpponentTurnReceived += _ => replayCount++;

        var matches = await client.ListMyMatchesAsync();
        await client.JoinAsync(matches.Matches[0].MatchId);
        OnlineMatchContext.Set(client);

        Assert.That(replayCount, Is.Zero);
        Assert.That(client.LastAppliedRevision, Is.EqualTo(1));
        Assert.That(GameStateHasher.Compute(client.CurrentState.State), Is.EqualTo(package.FinalStateHash));
        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.Multiplayer).GetViewingSeat(0), Is.EqualTo(1));
    }

    private sealed class MemoryTransport : IAsyncMatchTransport
    {
        public MatchStateUpdateData Snapshot;
        public GetViewRequestData LastFetch;
        public SubmitTurnRequestData LastSubmission;
        public int FetchCount;

        public event Action<string, int> RevisionCommitted { add { } remove { } }

        public MemoryTransport(int inLocalSeat)
        {
            var state = new GameSimulator().CreateGame(17, new RulesConfigData(), 0);
            state.MatchId = "session-selection";
            this.Snapshot = new MatchStateUpdateData
            {
                MatchId = state.MatchId, Status = "active", YourSeat = inLocalSeat,
                CurrentSeat = 0, State = state, Rules = state.Rules.Clone()
            };
        }

        public void Commit(TurnPackageData inPackage)
        {
            this.Snapshot.MatchRevision++;
            this.Snapshot.State = inPackage.FinalState.Clone();
            this.Snapshot.CurrentSeat = inPackage.NextSeat;
            this.Snapshot.Status = inPackage.Status;
            this.Snapshot.WinnerSeat = inPackage.WinnerSeat;
            this.Snapshot.Turns.Add(inPackage);
        }

        private MatchStateUpdateData Copy()
        {
            return NetJson.Deserialize<MatchStateUpdateData>(NetJson.Serialize(this.Snapshot));
        }

        public Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest)
        {
            throw new NotSupportedException();
        }

        public Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest)
        {
            Assert.That(inRequest.MatchId, Is.EqualTo(this.Snapshot.MatchId));
            return Task.FromResult(Copy());
        }

        public Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest)
        {
            this.LastFetch = inRequest;
            this.FetchCount++;
            var result = Copy();
            result.Turns.RemoveAll(inTurn => inTurn.StartRevision < inRequest.SinceRevision);
            return Task.FromResult(result);
        }

        public Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inRequest)
        {
            this.LastSubmission = inRequest;
            Commit(inRequest.Turn);
            return Task.FromResult(new SubmitTurnResultData
            {
                Accepted = true, NewRevision = this.Snapshot.MatchRevision,
                CurrentSeat = this.Snapshot.CurrentSeat, Status = this.Snapshot.Status
            });
        }

        public Task LeaveMatch(string inMatchId)
        {
            return Task.CompletedTask;
        }

        public Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest)
        {
            return Task.FromResult(new ListMyMatchesResultData
            {
                Matches = new() { new MyMatchData
                {
                    MatchId = this.Snapshot.MatchId, Status = this.Snapshot.Status,
                    MatchRevision = this.Snapshot.MatchRevision, YourSeat = this.Snapshot.YourSeat,
                    CurrentSeat = this.Snapshot.CurrentSeat
                } }
            });
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void LocalSeat_AlwaysBottomAndOnlyLocalHandVisible(int inLocalSeat)
    {
        var session = new OnlineSession(inLocalSeat);
        var binder = new SeatViewBinder(2, session.GetViewingSeat(0));

        for (int active = 0; active < 2; active++)
        {
            binder.SetViewingSeat(session.GetViewingSeat(active));

            Assert.That(binder.GetBoardSlotForSeat(inLocalSeat), Is.EqualTo(0));
            Assert.That(session.RequiresHandoff(1 - active, active), Is.False);
            Assert.That(session.ShouldRunAI(active), Is.False);
            Assert.That(session.IsHumanSeat(active), Is.EqualTo(active == inLocalSeat));
            Assert.That(session.IsHandVisible(inLocalSeat, active, false), Is.True);
            Assert.That(session.IsHandVisible(1 - inLocalSeat, active, false), Is.False);
        }
    }
}
