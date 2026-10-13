using System;
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
    public void MultiplayerWithActiveMatch_UsesClientAndLocalSeat(int inLocalSeat)
    {
        using var client = CreateClient(inLocalSeat);
        OnlineMatchContext.Set(client);

        var session = SessionFactory.Create(SettingsManager.EPlayMode.Multiplayer) as OnlineSession;

        Assert.That(session, Is.Not.Null);
        Assert.That(session.Client, Is.SameAs(client));
        Assert.That(session.LocalSeat, Is.EqualTo(inLocalSeat));
        Assert.That(session.GetViewingSeat(1 - inLocalSeat), Is.EqualTo(inLocalSeat));
        Assert.That(session.ShouldRunAI(1 - inLocalSeat), Is.False);
    }

    [Test]
    public void ActiveMatch_DoesNotChangeLocalModeSelection()
    {
        using var client = CreateClient(1);
        OnlineMatchContext.Set(client);

        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.AgainstAI), Is.TypeOf<LocalAiSession>());
        Assert.That(SessionFactory.Create(SettingsManager.EPlayMode.PassAndPlay), Is.TypeOf<PassAndPlaySession>());
    }

    [Test]
    public void ClearingContext_RestoresMultiplayerFallback()
    {
        using var client = CreateClient(1);
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

    private static OnlineMatchClient CreateClient(int inLocalSeat)
    {
        var transport = new InMemoryMatchTransport(new GameStateData { MatchId = "session-selection" });
        return new OnlineMatchClient(transport, transport.MatchId, inLocalSeat);
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
