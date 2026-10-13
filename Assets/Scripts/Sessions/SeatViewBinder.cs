using System;

public sealed class SeatViewBinder
{
    private readonly int[] seatByBoardSlot;
    private readonly int[] boardSlotBySeat;

    public int PlayerCount => this.seatByBoardSlot.Length;
    public int ViewingSeat { get; private set; }

    public SeatViewBinder(int inPlayerCount, int inViewingSeat = 0)
    {
        if (inPlayerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(inPlayerCount));

        this.seatByBoardSlot = new int[inPlayerCount];
        this.boardSlotBySeat = new int[inPlayerCount];
        this.ViewingSeat = -1;
        SetViewingSeat(inViewingSeat);
    }

    public bool SetViewingSeat(int inViewingSeat)
    {
        ValidateSeat(inViewingSeat);

        if (this.ViewingSeat == inViewingSeat)
            return false;

        this.ViewingSeat = inViewingSeat;
        for (int boardSlot = 0; boardSlot < this.PlayerCount; boardSlot++)
        {
            int seat = (inViewingSeat + boardSlot) % this.PlayerCount;
            this.seatByBoardSlot[boardSlot] = seat;
            this.boardSlotBySeat[seat] = boardSlot;
        }

        return true;
    }

    public int GetBoardSlotForSeat(int inSeat)
    {
        ValidateSeat(inSeat);
        return this.boardSlotBySeat[inSeat];
    }

    public int GetSeatForBoardSlot(int inBoardSlot)
    {
        if (inBoardSlot < 0 || inBoardSlot >= this.PlayerCount)
            throw new ArgumentOutOfRangeException(nameof(inBoardSlot));

        return this.seatByBoardSlot[inBoardSlot];
    }

    public void Rebind<T>(T[] inSeats, T[] inBoardSlots, Action<T, T> inSwapState) where T : class
    {
        if (inSeats == null)
            throw new ArgumentNullException(nameof(inSeats));
        if (inBoardSlots == null)
            throw new ArgumentNullException(nameof(inBoardSlots));
        if (inSwapState == null)
            throw new ArgumentNullException(nameof(inSwapState));
        if (inSeats.Length != this.PlayerCount || inBoardSlots.Length != this.PlayerCount)
            throw new ArgumentException("Seat and board-slot counts must match the binder player count.");

        var seatByBoardSlot = new int[this.PlayerCount];
        for (int boardSlot = 0; boardSlot < this.PlayerCount; boardSlot++)
        {
            int seat = FindReferenceIndex(inSeats, inBoardSlots[boardSlot]);
            if (seat < 0)
                throw new InvalidOperationException("Every board slot must contain one player seat.");

            seatByBoardSlot[boardSlot] = seat;
        }

        for (int targetSlot = 0; targetSlot < this.PlayerCount; targetSlot++)
        {
            int targetSeat = GetSeatForBoardSlot(targetSlot);
            if (seatByBoardSlot[targetSlot] == targetSeat)
                continue;

            int sourceSlot = Array.IndexOf(seatByBoardSlot, targetSeat);
            inSwapState(inBoardSlots[targetSlot], inBoardSlots[sourceSlot]);

            int displacedSeat = seatByBoardSlot[targetSlot];
            seatByBoardSlot[targetSlot] = targetSeat;
            seatByBoardSlot[sourceSlot] = displacedSeat;
        }

        for (int seat = 0; seat < this.PlayerCount; seat++)
            inSeats[seat] = inBoardSlots[GetBoardSlotForSeat(seat)];
    }

    private void ValidateSeat(int inSeat)
    {
        if (inSeat < 0 || inSeat >= this.PlayerCount)
            throw new ArgumentOutOfRangeException(nameof(inSeat));
    }

    private static int FindReferenceIndex<T>(T[] inItems, T inItem) where T : class
    {
        for (int i = 0; i < inItems.Length; i++)
        {
            if (ReferenceEquals(inItems[i], inItem))
                return i;
        }

        return -1;
    }
}
