using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Domain.Net;
using Elemento.Telemetry;
using NUnit.Framework;

public sealed class AiGameArchiveTests
{
    private string directoryPath;

    [SetUp]
    public void SetUp()
    {
        this.directoryPath = Path.Combine(Path.GetTempPath(),
            "ElementoAiGameArchiveTests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.directoryPath))
            Directory.Delete(this.directoryPath, true);
    }

    [Test]
    public void Complete_PreservesReplayableSnapshotInPendingQueue()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(3), new MoveData
        {
            Id = "move-1",
            PlayerId = 1,
            Type = EMoveType.PlayCard,
            CardId = "card-00000001"
        }, "move");

        archive.Complete("completed", true, "points");

        IReadOnlyList<string> pending = archive.GetPendingFiles();
        Assert.That(pending.Count, Is.EqualTo(1));
        AiGameData game = archive.LoadPending(pending[0]);
        Assert.That(game.Status, Is.EqualTo("completed"));
        Assert.That(game.PlayerWon, Is.EqualTo(1));
        Assert.That(game.Snapshots.Count, Is.EqualTo(1));
        Assert.That(game.SchemaVersion, Is.EqualTo(2));
        Assert.That(game.Snapshots[0].CompactState.TurnNumber, Is.EqualTo(3));
        Assert.That(game.Snapshots[0].Move.Id, Is.EqualTo("move-1"));
        Assert.That(archive.HasActiveGame, Is.False);
    }

    [Test]
    public void CompactSnapshots_StoreCardDefinitionOnceAndPreserveOrderedPositions()
    {
        var card = new CardData
        {
            Index = 17,
            Name = "Water 3",
            PrefabName = "Water3",
            Points = new[] { 0, 0, 0, 3 },
            Multipliers = new int[4],
            Bombs = new int[4],
            Shields = new int[4]
        };
        BoardState state = CreateState(1);
        state.Deck.Cards.Add(card);
        state.Deck.CardIds.Add("card-00000001");
        state.Deck.CardFaceUpStates.Add(false);
        for (int i = 0; i < 100; ++i)
            state.ShuffledDeck.Add(card);
        var archive = new AiGameArchive(this.directoryPath);

        archive.RecordSnapshot(CreateContext(), state, null, "deal");
        archive.RecordSnapshot(CreateContext(), state, null, "move");
        archive.Complete("completed");

        AiGameData game = archive.LoadPending(archive.GetPendingFiles()[0]);
        Assert.That(game.CardDefinitions, Has.Count.EqualTo(1));
        Assert.That(game.Snapshots[0].CompactState.Deck[0].Id,
            Is.EqualTo("card-00000001"));
        Assert.That(game.Snapshots[0].CompactState.ShuffledDeck,
            Has.All.EqualTo(0));
        Assert.That(game.Snapshots[1].CompactState.ShuffledDeck,
            Has.All.EqualTo(0));
    }

    [Test]
    public void UploadBody_WrapsPendingJsonWithoutReserializingIt()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(1), null, "deal");
        archive.Complete("completed");
        string path = archive.GetPendingFiles()[0];
        byte[] gameJson = File.ReadAllBytes(path);
        MethodInfo method = typeof(AiGameTelemetryClient).GetMethod("CreateUploadBody",
            BindingFlags.NonPublic | BindingFlags.Static);

        byte[] body = (byte[])method.Invoke(null, new object[] { path });

        byte[] prefix = Encoding.UTF8.GetBytes("{\"Game\":");
        Assert.That(body, Has.Length.EqualTo(prefix.Length + gameJson.Length + 1));
        for (int i = 0; i < prefix.Length; ++i)
            Assert.That(body[i], Is.EqualTo(prefix[i]));
        for (int i = 0; i < gameJson.Length; ++i)
            Assert.That(body[prefix.Length + i], Is.EqualTo(gameJson[i]));
        Assert.That(body[body.Length - 1], Is.EqualTo((byte)'}'));
    }

    [Test]
    public void RecoverInterruptedGame_QueuesLastDurableState()
    {
        var firstRun = new AiGameArchive(this.directoryPath);
        firstRun.RecordSnapshot(CreateContext(), CreateState(7), null, "paused");

        var nextRun = new AiGameArchive(this.directoryPath);
        nextRun.RecoverInterruptedGame();

        IReadOnlyList<string> pending = nextRun.GetPendingFiles();
        Assert.That(pending.Count, Is.EqualTo(1));
        AiGameData game = nextRun.LoadPending(pending[0]);
        Assert.That(game.Status, Is.EqualTo("interrupted"));
        Assert.That(game.Snapshots[0].CompactState.TurnNumber, Is.EqualTo(7));
    }

    [Test]
    public async Task RecordSnapshot_AfterInitialRecoveryPoint_WaitsForCheckpoint()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(1), null, "deal");
        string activePath = Path.Combine(this.directoryPath, "Active.json");
        string initialJson = File.ReadAllText(activePath);

        archive.RecordSnapshot(CreateContext(), CreateState(2), null, "move");

        Assert.That(File.ReadAllText(activePath), Is.EqualTo(initialJson));
        await archive.CheckpointAsync();
        AiGameData checkpoint = NetJson.Deserialize<AiGameData>(File.ReadAllText(activePath));
        Assert.That(checkpoint.Snapshots.Count, Is.EqualTo(2));
        Assert.That(checkpoint.Snapshots[1].CompactState.TurnNumber, Is.EqualTo(2));
    }

    [Test]
    public async Task Complete_InvalidatesQueuedActiveCheckpoint()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(1), null, "deal");
        archive.RecordSnapshot(CreateContext(), CreateState(2), null, "move");

        Task checkpoint = archive.CheckpointAsync();
        archive.Complete("completed");
        await checkpoint;

        Assert.That(File.Exists(Path.Combine(this.directoryPath, "Active.json")), Is.False);
        Assert.That(archive.GetPendingFiles().Count, Is.EqualTo(1));
        Assert.That(archive.LoadPending(archive.GetPendingFiles()[0]).Snapshots.Count,
            Is.EqualTo(2));
    }

    [Test]
    public void ClearLocal_RemovesActiveAndPendingGames()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(1), null, "move");
        archive.Complete("quit");
        archive.RecordSnapshot(CreateContext(), CreateState(2), null, "move");

        archive.ClearLocal();

        Assert.That(archive.HasActiveGame, Is.False);
        Assert.That(archive.GetPendingFiles(), Is.Empty);
    }

    [Test]
    public void Suspend_PreservesRecoveryJournalWithoutKeepingActiveGame()
    {
        var archive = new AiGameArchive(this.directoryPath);
        archive.RecordSnapshot(CreateContext(), CreateState(6), null, "move");

        archive.Suspend();

        Assert.That(archive.HasActiveGame, Is.False);
        Assert.That(File.Exists(Path.Combine(this.directoryPath, "Active.json")), Is.True);

        var nextRun = new AiGameArchive(this.directoryPath);
        nextRun.RecoverInterruptedGame();
        IReadOnlyList<string> pending = nextRun.GetPendingFiles();
        Assert.That(pending.Count, Is.EqualTo(1));
        Assert.That(nextRun.LoadPending(pending[0]).Status, Is.EqualTo("interrupted"));
    }

    [Test]
    public void LoadPending_RejectsSiblingDirectory()
    {
        var archive = new AiGameArchive(this.directoryPath);
        string siblingPath = Path.Combine(this.directoryPath + "-other", "game.json");

        Assert.Throws<ArgumentException>(() => archive.LoadPending(siblingPath));
    }

    [Test]
    public void RecoverInterruptedGame_PreservesCompletedPendingCopy()
    {
        var firstRun = new AiGameArchive(this.directoryPath);
        firstRun.RecordSnapshot(CreateContext(), CreateState(4), null, "move");
        string activePath = Path.Combine(this.directoryPath, "Active.json");
        AiGameData completed = NetJson.Deserialize<AiGameData>(File.ReadAllText(activePath));
        completed.Status = "completed";
        completed.EndedAtUtc = DateTime.UtcNow.ToString("O");
        string pendingPath = Path.Combine(this.directoryPath, "Pending",
            completed.ClientGameId + ".json");
        File.WriteAllText(pendingPath, NetJson.Serialize(completed));

        var nextRun = new AiGameArchive(this.directoryPath);
        nextRun.RecoverInterruptedGame();

        AiGameData recovered = nextRun.LoadPending(pendingPath);
        Assert.That(recovered.Status, Is.EqualTo("completed"));
        Assert.That(File.Exists(activePath), Is.False);
    }

    [Test]
    public void RecoverInterruptedGame_UsesFirstWriteTemporaryFile()
    {
        var firstRun = new AiGameArchive(this.directoryPath);
        firstRun.RecordSnapshot(CreateContext(), CreateState(5), null, "move");
        string activePath = Path.Combine(this.directoryPath, "Active.json");
        File.Move(activePath, activePath + ".tmp");

        var nextRun = new AiGameArchive(this.directoryPath);
        nextRun.RecoverInterruptedGame();

        IReadOnlyList<string> pending = nextRun.GetPendingFiles();
        Assert.That(pending.Count, Is.EqualTo(1));
        Assert.That(nextRun.LoadPending(pending[0]).Status, Is.EqualTo("interrupted"));
    }

    private static AiGameContextData CreateContext()
    {
        return new AiGameContextData
        {
            AppVersion = "test",
            BuildGuid = "build",
            UnityVersion = "unity",
            Platform = "editor",
            AiImplementation = "strong",
            AiDifficulty = 2,
            RandomSeed = 17,
            Rules = new RulesConfigData()
        };
    }

    private static BoardState CreateState(int inTurnNumber)
    {
        return new BoardState
        {
            TurnNumber = inTurnNumber,
            Deck = new BoardHand(),
            Players = new List<BoardPlayer>()
        };
    }
}
