using System;
using System.Collections.Generic;
using System.IO;
using Elemento.Domain;
using Elemento.Domain.Net;

namespace Elemento.Telemetry
{
    public sealed class AiGameArchive
    {
        private const int MaxDiagnostics = 50;
        private const int MaxDiagnosticLength = 16000;
        private const int MaxRecoveredJsonLength = 262144;

        private readonly string directoryPath;
        private readonly string pendingDirectoryPath;
        private readonly string activeFilePath;
        private AiGameData currentGame;

        public bool HasActiveGame => this.currentGame != null;

        public AiGameArchive(string inDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(inDirectoryPath))
                throw new ArgumentException("An AI game archive directory is required.", nameof(inDirectoryPath));

            this.directoryPath = inDirectoryPath;
            this.pendingDirectoryPath = Path.Combine(inDirectoryPath, "Pending");
            this.activeFilePath = Path.Combine(inDirectoryPath, "Active.json");
            EnsureDirectories();
        }

        public void RecoverInterruptedGame()
        {
            string sourcePath = File.Exists(this.activeFilePath)
                ? this.activeFilePath
                : File.Exists(this.activeFilePath + ".bak") ? this.activeFilePath + ".bak"
                : File.Exists(this.activeFilePath + ".tmp") ? this.activeFilePath + ".tmp" : null;
            if (sourcePath == null)
                return;

            AiGameData game;
            try
            {
                game = Load(sourcePath);
            }
            catch (Exception exception)
            {
                string recoveredJson = File.ReadAllText(sourcePath);
                game = new AiGameData
                {
                    ClientGameId = Guid.NewGuid().ToString("N"),
                    StartedAtUtc = UtcNow(),
                    Status = "corrupt",
                    RecoveryError = Truncate(exception.Message, MaxDiagnosticLength),
                    RecoveredJson = Truncate(recoveredJson, MaxRecoveredJsonLength)
                };
            }

            if (game.Status != "corrupt" && File.Exists(GetPendingPath(game.ClientGameId)))
            {
                DeleteActiveFiles();
                return;
            }

            game.Status = game.Status == "corrupt" ? "corrupt" : "interrupted";
            game.EndedAtUtc = UtcNow();
            WritePending(game);
            DeleteActiveFiles();
        }

        public void RecordSnapshot(AiGameContextData inContext, BoardState inState,
            MoveData inMove, string inReason)
        {
            if (inContext == null)
                throw new ArgumentNullException(nameof(inContext));
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
            if (inState.Move != null)
                throw new InvalidOperationException("AI telemetry snapshots must not retain Unity move references.");

            StartIfNeeded(inContext);
            // shortcut: full snapshots favor diagnosis over payload size; switch to deltas if uploads approach function limits.
            this.currentGame.Snapshots.Add(new AiGameSnapshotData
            {
                CapturedAtUtc = UtcNow(),
                Reason = inReason,
                Move = inMove?.Clone(),
                State = CloneState(inState)
            });
            WriteActive();
        }

        public void RecordDiagnostic(string inType, string inMessage, string inStackTrace)
        {
            if (this.currentGame == null)
                return;

            if (this.currentGame.Diagnostics.Count >= MaxDiagnostics)
                this.currentGame.Diagnostics.RemoveAt(0);
            this.currentGame.Diagnostics.Add(new AiGameDiagnosticData
            {
                CapturedAtUtc = UtcNow(),
                Type = inType,
                Message = Truncate(inMessage, MaxDiagnosticLength),
                StackTrace = Truncate(inStackTrace, MaxDiagnosticLength)
            });
            WriteActive();
        }

        public void Complete(string inStatus, bool? inPlayerWon = null, string inResultMessage = null)
        {
            if (this.currentGame == null)
                return;

            this.currentGame.Status = inStatus;
            this.currentGame.PlayerWon = inPlayerWon.HasValue ? inPlayerWon.Value ? 1 : 0 : -1;
            this.currentGame.ResultMessage = inResultMessage;
            this.currentGame.EndedAtUtc = UtcNow();
            WritePending(this.currentGame);
            this.currentGame = null;
            DeleteActiveFiles();
        }

        public IReadOnlyList<string> GetPendingFiles()
        {
            EnsureDirectories();
            string[] files = Directory.GetFiles(this.pendingDirectoryPath, "*.json");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        public AiGameData LoadPending(string inPath)
        {
            return Load(ResolvePendingPath(inPath));
        }

        public void RemovePending(string inPath)
        {
            string fullPath = ResolvePendingPath(inPath);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
            if (File.Exists(fullPath + ".bak"))
                File.Delete(fullPath + ".bak");
        }

        public void ClearLocal()
        {
            this.currentGame = null;
            if (Directory.Exists(this.directoryPath))
                Directory.Delete(this.directoryPath, true);
            EnsureDirectories();
        }

        private void StartIfNeeded(AiGameContextData inContext)
        {
            if (this.currentGame != null)
                return;

            this.currentGame = new AiGameData
            {
                ClientGameId = Guid.NewGuid().ToString("N"),
                StartedAtUtc = UtcNow(),
                AppVersion = inContext.AppVersion,
                BuildGuid = inContext.BuildGuid,
                UnityVersion = inContext.UnityVersion,
                Platform = inContext.Platform,
                AiImplementation = inContext.AiImplementation,
                AiDifficulty = inContext.AiDifficulty,
                RandomSeed = inContext.RandomSeed,
                Rules = inContext.Rules?.Clone()
            };
        }

        private void WriteActive()
        {
            WriteAtomic(this.activeFilePath, NetJson.Serialize(this.currentGame));
        }

        private void WritePending(AiGameData inGame)
        {
            EnsureDirectories();
            string id = string.IsNullOrEmpty(inGame.ClientGameId)
                ? Guid.NewGuid().ToString("N") : inGame.ClientGameId;
            inGame.ClientGameId = id;
            WriteAtomic(GetPendingPath(id), NetJson.Serialize(inGame));
        }

        private string GetPendingPath(string inClientGameId)
        {
            return Path.Combine(this.pendingDirectoryPath, inClientGameId + ".json");
        }

        private string ResolvePendingPath(string inPath)
        {
            string pendingRoot = Path.GetFullPath(this.pendingDirectoryPath)
                + Path.DirectorySeparatorChar;
            string fullPath = string.IsNullOrEmpty(inPath) ? string.Empty : Path.GetFullPath(inPath);
            if (!fullPath.StartsWith(pendingRoot, StringComparison.Ordinal))
                throw new ArgumentException("The pending AI game path is outside the archive.", nameof(inPath));
            return fullPath;
        }

        private static AiGameData Load(string inPath)
        {
            AiGameData game = NetJson.Deserialize<AiGameData>(File.ReadAllText(inPath));
            if (game == null || !Guid.TryParseExact(game.ClientGameId, "N", out _))
                throw new FormatException("The saved AI game is incomplete.");
            return game;
        }

        private static BoardState CloneState(BoardState inState)
        {
            return NetJson.Deserialize<BoardState>(NetJson.Serialize(inState));
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(this.directoryPath);
            Directory.CreateDirectory(this.pendingDirectoryPath);
        }

        private void DeleteActiveFiles()
        {
            if (File.Exists(this.activeFilePath))
                File.Delete(this.activeFilePath);
            if (File.Exists(this.activeFilePath + ".bak"))
                File.Delete(this.activeFilePath + ".bak");
            if (File.Exists(this.activeFilePath + ".tmp"))
                File.Delete(this.activeFilePath + ".tmp");
        }

        private static void WriteAtomic(string inPath, string inJson)
        {
            string temporaryPath = inPath + ".tmp";
            string backupPath = inPath + ".bak";
            File.WriteAllText(temporaryPath, inJson);
            if (!File.Exists(inPath))
            {
                File.Move(temporaryPath, inPath);
                return;
            }

            try
            {
                File.Replace(temporaryPath, inPath, backupPath);
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            catch (Exception exception) when (exception is IOException || exception is NotSupportedException)
            {
                File.Copy(temporaryPath, inPath, true);
                File.Delete(temporaryPath);
            }
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("O");
        }

        private static string Truncate(string inValue, int inMaxLength)
        {
            if (string.IsNullOrEmpty(inValue) || inValue.Length <= inMaxLength)
                return inValue;
            return inValue.Substring(0, inMaxLength);
        }
    }
}
