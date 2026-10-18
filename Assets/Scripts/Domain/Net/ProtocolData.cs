using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    public static class ProtocolVersions
    {
        public const int Current = 2;
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
        public CardInstanceData ReplayCard;
        public string ClientNonce;
    }

    [Serializable]
    public sealed class SubmitTurnRequestData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public int Seat;
        public int ExpectedRevision;
        public string ClientNonce;
        public TurnPackageData Turn;
    }

    [Serializable]
    public sealed class SubmitTurnResultData
    {
        public bool Accepted;
        public string Reason;
        public int NewRevision;
        public int CurrentSeat;
        public string Status;
    }

    [Serializable]
    public sealed class TurnActionData
    {
        public int Index;
        public MoveData Move;
        public List<CardInstanceData> DrawnCards = new();
        public CardInstanceData PowerCard;
        public string Kind;
    }

    [Serializable]
    public sealed class TurnPackageData
    {
        public string MatchId;
        public int Seat;
        public int TurnNumber;
        public int StartRevision;
        public List<TurnActionData> Actions = new();
        public GameStateData FinalState;
        public string FinalStateHash;
        public string Status;
        public int WinnerSeat = -1;
        public EWinCondition WinCondition;
        public int NextSeat;
    }

    [Serializable]
    public sealed class CreateMatchRequestData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public RulesConfigData Rules;
        public GameStateData InitialState;
        public string InitialStateHash;
    }

    [Serializable]
    public sealed class MatchStateUpdateData
    {
        public string MatchId;
        public string Status;
        public int MatchRevision;
        public int CurrentSeat;
        public int YourSeat;
        public int WinnerSeat = -1;
        public RulesConfigData Rules;
        public GameStateData State;
        public List<TurnPackageData> Turns = new();
        public List<string> SeatNames = new();
    }

    [Serializable]
    public sealed class GetViewRequestData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
        public int SinceRevision = -1;
    }

    [Serializable]
    public sealed class JoinMatchRequestData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
        public string MatchId;
    }

    [Serializable]
    public sealed class ListMyMatchesRequestData
    {
        public int ProtocolVersion = ProtocolVersions.Current;
    }

    [Serializable]
    public sealed class ListMyMatchesResultData
    {
        public List<MyMatchData> Matches = new();
    }

    [Serializable]
    public sealed class MyMatchData
    {
        public string MatchId;
        public string Status;
        public int MatchRevision;
        public int CurrentSeat;
        public int YourSeat;
        public string OpponentName;
        public string UpdatedAt;
    }

    [Serializable]
    public sealed class OnlineViewUpdateData
    {
        public string Status;
        public int MatchRevision;
        public RulesConfigData Rules;
        public MatchSeatViewData View;
        public List<MoveLogEntryData> Moves = new();
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
        public CardInstanceData ReplayCard;
    }
}
