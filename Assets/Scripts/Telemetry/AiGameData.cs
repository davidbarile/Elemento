using System;
using System.Collections.Generic;
using Elemento.Domain;
using Elemento.Domain.Net;

namespace Elemento.Telemetry
{
    [Serializable]
    public sealed class AiGameContextData
    {
        public string AppVersion;
        public string BuildGuid;
        public string UnityVersion;
        public string Platform;
        public string AiImplementation;
        public int AiDifficulty;
        public int RandomSeed;
        public RulesConfigData Rules;
    }

    [Serializable]
    public sealed class AiGameSnapshotData
    {
        public string CapturedAtUtc;
        public string Reason;
        public MoveData Move;
        [NetJsonName("s")]
        public AiGameStateData CompactState;
        // Schema 1 only. Kept so interrupted recordings from older builds remain recoverable.
        public BoardState State;
    }

    [Serializable]
    public sealed class AiGameCardDefinitionData
    {
        [NetJsonName("i")]
        public int OriginalIndex;
        [NetJsonName("n")]
        public string Name;
        [NetJsonName("p")]
        public string PrefabName;
        [NetJsonName("f")]
        public int FilterFlags;
        [NetJsonName("c")]
        public int Count;
        [NetJsonName("w")]
        public int[] WildValues;
        [NetJsonName("v")]
        public int[] ElementValues;
        [NetJsonName("x")]
        public bool IsPowerCard;
    }

    [Serializable]
    public sealed class AiGameCardReferenceData
    {
        [NetJsonName("i")]
        public string Id;
        [NetJsonName("d")]
        public int DefinitionCode;
        [NetJsonName("f")]
        public bool IsFaceUp;
    }

    [Serializable]
    public sealed class AiGameZoneData
    {
        [NetJsonName("e")]
        public int ElementType;
        [NetJsonName("p")]
        public int Points;
        [NetJsonName("u")]
        public int FaceUpPoints;
        [NetJsonName("a")]
        public bool IsActivated;
        [NetJsonName("h")]
        public int Health;
        [NetJsonName("m")]
        public int MaxHealth;
        [NetJsonName("f")]
        public int FrozenHealth;
        [NetJsonName("x")]
        public bool IsDestroyed;
        [NetJsonName("c")]
        public List<AiGameCardReferenceData> Cards = new();
    }

    [Serializable]
    public sealed class AiGamePlayerData
    {
        [NetJsonName("m")]
        public int CurrentMoveNumber;
        [NetJsonName("t")]
        public int TotalMovesTaken;
        [NetJsonName("h")]
        public List<AiGameCardReferenceData> Hand = new();
        [NetJsonName("z")]
        public List<AiGameZoneData> Zones = new();
    }

    [Serializable]
    public sealed class AiGameStateData
    {
        [NetJsonName("t")]
        public int TurnNumber;
        [NetJsonName("m")]
        public int MoveNumber;
        [NetJsonName("n")]
        public int TotalMovesTaken;
        [NetJsonName("r")]
        public int MovesRemaining;
        [NetJsonName("a")]
        public int ActivePlayer;
        [NetJsonName("x")]
        public bool IsDealHand;
        [NetJsonName("d")]
        public List<AiGameCardReferenceData> Deck = new();
        [NetJsonName("s")]
        public List<int> ShuffledDeck = new();
        [NetJsonName("q")]
        public List<int> PowerDeck = new();
        [NetJsonName("p")]
        public List<AiGamePlayerData> Players = new();
    }

    [Serializable]
    public sealed class AiGameDiagnosticData
    {
        public string CapturedAtUtc;
        public string Type;
        public string Message;
        public string StackTrace;
    }

    [Serializable]
    public sealed class AiGameData
    {
        public int SchemaVersion = 2;
        public string ClientGameId;
        public string Status = "active";
        public string StartedAtUtc;
        public string EndedAtUtc;
        public string AppVersion;
        public string BuildGuid;
        public string UnityVersion;
        public string Platform;
        public string AiImplementation;
        public int AiDifficulty;
        public int RandomSeed;
        public int PlayerWon = -1;
        public string ResultMessage;
        public string RecoveryError;
        public string RecoveredJson;
        public RulesConfigData Rules;
        [NetJsonName("d")]
        public List<AiGameCardDefinitionData> CardDefinitions = new();
        public List<AiGameSnapshotData> Snapshots = new();
        public List<AiGameDiagnosticData> Diagnostics = new();
    }
}
