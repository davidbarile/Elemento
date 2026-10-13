using NUnit.Framework;

public class OnlineSessionTests
{
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
