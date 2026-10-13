using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public static class ProtocolVersions
    {
        public const int Current = 1;
    }

    [Serializable]
    public sealed class MoveCommandData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public int Seat;
        public int ClientMoveSequence;
        public int ExpectedRevision;
        public MoveData Move;
        public string ClientNonce;
    }

    [Serializable]
    public sealed class MoveResultData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public bool Accepted;
        public string Reason;
        public int NewRevision;
        public MoveLogEntryData LogEntry;
    }

    [Serializable]
    public sealed class SeatPublicStateData
    {
        public int Seat;
        public string Name;
        public List<ZoneStateData> Zones = new();
    }

    [Serializable]
    public sealed class MatchSnapshotData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public RulesConfigData Rules;
        public int Revision;
        public int TurnNumber;
        public int CurrentSeat;
        public ETurnPhase TurnPhase;
        public int MovesRemaining;
        public List<SeatPublicStateData> Seats = new();
        public List<int> HandCounts = new();
        public int DeckCount;
        public int ReserveDeckCount;
        public int PowerDeckCount;
        public int DiscardPileCount;
        public int WinnerSeat;
        public EWinCondition WinCondition;
    }

    [Serializable]
    public sealed class MatchSeatViewData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public int Seat;
        public PlayerViewData View;
    }

    [Serializable]
    public sealed class MoveLogEntryData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public int Seat;
        public int ClientMoveSequence;
        public string ClientNonce;
        public int RevisionBefore;
        public int RevisionAfter;
        public MoveData Move;
    }
}
