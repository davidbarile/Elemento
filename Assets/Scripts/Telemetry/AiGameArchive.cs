using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly object checkpointLock = new();
        private Task checkpointTask = Task.CompletedTask;
        private int checkpointGeneration;
        private AiGameData currentGame;
        private readonly Dictionary<CardData, int> cardDefinitionCodes = new();

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

            bool isFirstSnapshot = this.currentGame == null;
            StartIfNeeded(inContext);
            // shortcut: full snapshots favor diagnosis over payload size; switch to deltas if uploads approach function limits.
            this.currentGame.Snapshots.Add(new AiGameSnapshotData
            {
                CapturedAtUtc = UtcNow(),
                Reason = inReason,
                Move = inMove?.Clone(),
                CompactState = CreateCompactState(inState)
            });
            if (isFirstSnapshot)
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
        }

        public Task CheckpointAsync()
        {
            lock (this.checkpointLock)
            {
                if (this.currentGame == null)
                    return Task.CompletedTask;

                int generation = this.checkpointGeneration;
                AiGameData checkpoint = CopyForCheckpoint(this.currentGame);
                this.checkpointTask = this.checkpointTask.ContinueWith(_ =>
                    WriteActive(checkpoint, generation), CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
                return this.checkpointTask;
            }
        }

        public void Complete(string inStatus, bool? inPlayerWon = null, string inResultMessage = null)
        {
            AiGameData completedGame;
            lock (this.checkpointLock)
            {
                if (this.currentGame == null)
                    return;
                ++this.checkpointGeneration;
                this.checkpointTask = Task.CompletedTask;
                completedGame = this.currentGame;
                this.currentGame = null;
                this.cardDefinitionCodes.Clear();
            }

            completedGame.Status = inStatus;
            completedGame.PlayerWon = inPlayerWon.HasValue ? inPlayerWon.Value ? 1 : 0 : -1;
            completedGame.ResultMessage = inResultMessage;
            completedGame.EndedAtUtc = UtcNow();
            WritePending(completedGame);
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
            lock (this.checkpointLock)
            {
                ++this.checkpointGeneration;
                this.checkpointTask = Task.CompletedTask;
                this.currentGame = null;
                this.cardDefinitionCodes.Clear();
            }
            if (Directory.Exists(this.directoryPath))
                Directory.Delete(this.directoryPath, true);
            EnsureDirectories();
        }

        public void Suspend()
        {
            lock (this.checkpointLock)
            {
                ++this.checkpointGeneration;
                this.checkpointTask = Task.CompletedTask;
                this.currentGame = null;
                this.cardDefinitionCodes.Clear();
            }
        }

        private void StartIfNeeded(AiGameContextData inContext)
        {
            if (this.currentGame != null)
                return;

            this.cardDefinitionCodes.Clear();
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

        private void WriteActive(AiGameData inGame, int inGeneration)
        {
            string json = NetJson.Serialize(inGame);
            lock (this.checkpointLock)
            {
                if (inGeneration != this.checkpointGeneration || this.currentGame == null)
                    return;
                WriteAtomic(this.activeFilePath, json);
            }
        }

        private static AiGameData CopyForCheckpoint(AiGameData inGame)
        {
            return new AiGameData
            {
                SchemaVersion = inGame.SchemaVersion,
                ClientGameId = inGame.ClientGameId,
                Status = inGame.Status,
                StartedAtUtc = inGame.StartedAtUtc,
                EndedAtUtc = inGame.EndedAtUtc,
                AppVersion = inGame.AppVersion,
                BuildGuid = inGame.BuildGuid,
                UnityVersion = inGame.UnityVersion,
                Platform = inGame.Platform,
                AiImplementation = inGame.AiImplementation,
                AiDifficulty = inGame.AiDifficulty,
                RandomSeed = inGame.RandomSeed,
                PlayerWon = inGame.PlayerWon,
                ResultMessage = inGame.ResultMessage,
                RecoveryError = inGame.RecoveryError,
                RecoveredJson = inGame.RecoveredJson,
                Rules = inGame.Rules?.Clone(),
                CardDefinitions = new List<AiGameCardDefinitionData>(inGame.CardDefinitions),
                Snapshots = new List<AiGameSnapshotData>(inGame.Snapshots),
                Diagnostics = new List<AiGameDiagnosticData>(inGame.Diagnostics)
            };
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

        private AiGameStateData CreateCompactState(BoardState inState)
        {
            var result = new AiGameStateData
            {
                TurnNumber = inState.TurnNumber,
                MoveNumber = inState.MoveNumber,
                TotalMovesTaken = inState.TotalMovesTaken,
                MovesRemaining = inState.MovesRemaining,
                ActivePlayer = (int)inState.ActivePlayer,
                IsDealHand = inState.IsDealHand
            };
            AddCardReferences(result.Deck, inState.Deck);
            AddDefinitionCodes(result.ShuffledDeck, inState.ShuffledDeck);
            AddDefinitionCodes(result.PowerDeck, inState.PowerDeck);
            foreach (var player in inState.Players)
            {
                var compactPlayer = new AiGamePlayerData
                {
                    CurrentMoveNumber = player.CurrentMoveNumber,
                    TotalMovesTaken = player.TotalMovesTaken
                };
                AddCardReferences(compactPlayer.Hand, player.Hand);
                foreach (var zone in player.ElementZones)
                {
                    if (zone == null)
                        continue;
                    var compactZone = new AiGameZoneData
                    {
                        ElementType = (int)zone.ElementType,
                        Points = zone.Points,
                        FaceUpPoints = zone.FaceUpPoints,
                        IsActivated = zone.IsActivated,
                        Health = zone.Health,
                        MaxHealth = zone.MaxHealth,
                        FrozenHealth = zone.FrozenHealth,
                        IsDestroyed = zone.IsDestroyed
                    };
                    AddCardReferences(compactZone.Cards, zone.Cards, zone.CardIds,
                        zone.CardFaceUpStates);
                    compactPlayer.Zones.Add(compactZone);
                }
                result.Players.Add(compactPlayer);
            }
            return result;
        }

        private void AddCardReferences(List<AiGameCardReferenceData> inTarget, BoardHand inSource)
        {
            if (inSource == null)
                return;
            AddCardReferences(inTarget, inSource.Cards, inSource.CardIds,
                inSource.CardFaceUpStates);
        }

        private void AddCardReferences(List<AiGameCardReferenceData> inTarget,
            List<CardData> inCards, List<string> inIds, List<bool> inFaceUpStates)
        {
            for (int i = 0; i < inCards.Count; ++i)
            {
                inTarget.Add(new AiGameCardReferenceData
                {
                    Id = i < inIds.Count ? inIds[i] : null,
                    DefinitionCode = GetDefinitionCode(inCards[i]),
                    IsFaceUp = i >= inFaceUpStates.Count || inFaceUpStates[i]
                });
            }
        }

        private void AddDefinitionCodes(List<int> inTarget, List<CardData> inCards)
        {
            foreach (var card in inCards)
                inTarget.Add(GetDefinitionCode(card));
        }

        private int GetDefinitionCode(CardData inCard)
        {
            if (inCard == null)
                return -1;
            if (this.cardDefinitionCodes.TryGetValue(inCard, out int code))
                return code;

            code = this.currentGame.CardDefinitions.Count;
            this.cardDefinitionCodes.Add(inCard, code);
            this.currentGame.CardDefinitions.Add(new AiGameCardDefinitionData
            {
                OriginalIndex = inCard.Index,
                Name = inCard.Name,
                PrefabName = inCard.PrefabName,
                FilterFlags = (int)inCard.FilterFlags,
                Count = inCard.Count,
                WildValues = new[]
                {
                    inCard.WildPoints, inCard.WildMultiplier, inCard.WildBomb,
                    inCard.WildShield, inCard.WildZoneHeal, inCard.WildFreeze,
                    inCard.WildLightning
                },
                ElementValues = CombineValues(inCard.Points, inCard.Multipliers,
                    inCard.Bombs, inCard.Shields),
                IsPowerCard = inCard.IsPowerCard
            });
            return code;
        }

        private static int[] CombineValues(params int[][] inValues)
        {
            var result = new int[16];
            int targetIndex = 0;
            foreach (var values in inValues)
            {
                for (int i = 0; i < 4; ++i)
                    result[targetIndex++] = values != null && i < values.Length ? values[i] : 0;
            }
            return result;
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
