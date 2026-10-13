using NUnit.Framework;

public class SeatViewBinderTests
{
    [Test]
    public void ViewingSeat_MapsToBottomBoardSlot()
    {
        var binder = new SeatViewBinder(2);

        binder.SetViewingSeat(1);

        Assert.That(binder.GetBoardSlotForSeat(1), Is.EqualTo(0));
        Assert.That(binder.GetBoardSlotForSeat(0), Is.EqualTo(1));
        Assert.That(binder.GetSeatForBoardSlot(0), Is.EqualTo(1));
        Assert.That(binder.GetSeatForBoardSlot(1), Is.EqualTo(0));
    }

    [Test]
    public void ViewingSeat_RoundTripPreservesSeatState()
    {
        var binder = new SeatViewBinder(2);
        var bottomBoard = new BoardSlotState("Player A state");
        var topBoard = new BoardSlotState("Player B state");
        var seats = new[] { bottomBoard, topBoard };
        var boardSlots = new[] { bottomBoard, topBoard };

        binder.SetViewingSeat(1);
        binder.Rebind(seats, boardSlots, SwapState);

        Assert.That(boardSlots[0].Value, Is.EqualTo("Player B state"));
        Assert.That(boardSlots[1].Value, Is.EqualTo("Player A state"));
        Assert.That(seats[0].Value, Is.EqualTo("Player A state"));
        Assert.That(seats[1].Value, Is.EqualTo("Player B state"));

        binder.SetViewingSeat(0);
        binder.Rebind(seats, boardSlots, SwapState);

        Assert.That(boardSlots[0].Value, Is.EqualTo("Player A state"));
        Assert.That(boardSlots[1].Value, Is.EqualTo("Player B state"));
        Assert.That(seats[0], Is.SameAs(bottomBoard));
        Assert.That(seats[1], Is.SameAs(topBoard));
    }

    [Test]
    public void LocalAiSession_AlwaysKeepsHumanSeatAtBottom()
    {
        var session = new LocalAiSession();
        var binder = new SeatViewBinder(2, session.GetViewingSeat(0));

        bool didChange = binder.SetViewingSeat(session.GetViewingSeat(1));

        Assert.That(didChange, Is.False);
        Assert.That(binder.ViewingSeat, Is.EqualTo(0));
        Assert.That(binder.GetBoardSlotForSeat(0), Is.EqualTo(0));
        Assert.That(binder.GetBoardSlotForSeat(1), Is.EqualTo(1));
    }

    [Test]
    public void Rebind_RepairsDriftWhenViewingSeatUnchanged()
    {
        var binder = new SeatViewBinder(2);
        var bottomBoard = new BoardSlotState("Player A state");
        var topBoard = new BoardSlotState("Player B state");
        var boardSlots = new[] { bottomBoard, topBoard };
        //seat 0 drifted onto the top slot while the binder still believes seat 0 is viewed
        var seats = new[] { topBoard, bottomBoard };

        bool didChange = binder.SetViewingSeat(0);
        binder.Rebind(seats, boardSlots, SwapState);

        Assert.That(didChange, Is.False);
        Assert.That(seats[0], Is.SameAs(boardSlots[0]));
        Assert.That(seats[1], Is.SameAs(boardSlots[1]));
    }

    [Test]
    public void ManyConsecutiveTurns_ActiveSeatAlwaysBottom()
    {
        var session = new PassAndPlaySession();
        var binder = new SeatViewBinder(2);
        var bottomBoard = new BoardSlotState("A");
        var topBoard = new BoardSlotState("B");
        var boardSlots = new[] { bottomBoard, topBoard };
        var seats = new[] { bottomBoard, topBoard };
        int current = 0;

        for (int turn = 0; turn < 40; turn++)
        {
            current = (current + 1) % 2;
            binder.SetViewingSeat(session.GetViewingSeat(current));
            binder.Rebind(seats, boardSlots, SwapState);
            //a redundant re-apply (e.g. on Continue) must be a no-op
            binder.SetViewingSeat(session.GetViewingSeat(current));
            binder.Rebind(seats, boardSlots, SwapState);

            Assert.That(seats[current], Is.SameAs(boardSlots[0]), $"turn {turn}");
            Assert.That(seats[current].Value, Is.EqualTo(current == 0 ? "A" : "B"), $"turn {turn}");
        }
    }

    private static void SwapState(BoardSlotState inFirst, BoardSlotState inSecond)
    {
        string value = inFirst.Value;
        inFirst.Value = inSecond.Value;
        inSecond.Value = value;
    }

    private sealed class BoardSlotState
    {
        public string Value;

        public BoardSlotState(string inValue)
        {
            this.Value = inValue;
        }
    }
}
