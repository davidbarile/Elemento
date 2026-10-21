using System;
using System.Collections.Generic;
using Elemento.Domain;

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
        public BoardState State;
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
        public int SchemaVersion = 1;
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
        public List<AiGameSnapshotData> Snapshots = new();
        public List<AiGameDiagnosticData> Diagnostics = new();
    }
}
