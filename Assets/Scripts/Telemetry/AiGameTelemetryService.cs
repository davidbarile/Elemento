using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Networking;
using Elemento.Networking.Supabase;
using UnityEngine;

namespace Elemento.Telemetry
{
    public sealed class AiGameTelemetryService : IDisposable
    {
        private readonly AiGameArchive archive;
        private readonly AiGameTelemetryClient client;
        private bool isUploading;
        private bool isHandlingDiagnostic;

        public bool HasActiveGame => this.archive.HasActiveGame;
        public bool IsRecordingEnabled => PlayerData.Data?.RecordAiGames ?? true;
        public string UserId => this.client.UserId;

        public AiGameTelemetryService()
        {
            this.archive = new AiGameArchive(Path.Combine(
                Application.persistentDataPath, "AiGameTelemetry"));
            this.client = new AiGameTelemetryClient(OnlineCoroutineHost.IN);
        }

        public void Initialize()
        {
            Application.logMessageReceived += HandleLogMessage;
            if (this.IsRecordingEnabled)
            {
                this.archive.RecoverInterruptedGame();
                _ = FlushPendingAsync();
            }
        }

        public void RecordSnapshot(AiGameContextData inContext, BoardState inState,
            MoveData inMove, string inReason)
        {
            if (!this.IsRecordingEnabled)
                return;

            try
            {
                this.archive.RecordSnapshot(inContext, inState, inMove, inReason);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI game telemetry snapshot failed: " + exception.Message);
            }
        }

        public void Checkpoint()
        {
            if (!this.IsRecordingEnabled)
                return;

            _ = CheckpointAsync();
        }

        public void Complete(string inStatus, bool? inPlayerWon = null,
            string inResultMessage = null)
        {
            if (!this.IsRecordingEnabled)
                return;

            try
            {
                this.archive.Complete(inStatus, inPlayerWon, inResultMessage);
                _ = FlushPendingAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI game telemetry completion failed: " + exception.Message);
            }
        }

        public void ClearLocal()
        {
            this.archive.ClearLocal();
        }

        public void SetRecordingEnabled(bool inIsEnabled)
        {
            if (PlayerData.Data == null || PlayerData.Data.RecordAiGames == inIsEnabled)
                return;

            PlayerData.Data.RecordAiGames = inIsEnabled;
            PlayerData.Data.IsDirty = true;
            if (SaveManager.IN != null)
                SaveManager.IN.SaveGame();

            if (!inIsEnabled)
            {
                this.archive.Suspend();
                return;
            }

            this.archive.RecoverInterruptedGame();
            _ = FlushPendingAsync();
        }

        public Task<int> ClearServerAsync(bool inAllGames)
        {
            return this.client.ClearAsync(inAllGames);
        }

        public void Dispose()
        {
            Application.logMessageReceived -= HandleLogMessage;
        }

        private async Task FlushPendingAsync()
        {
            if (!this.IsRecordingEnabled || this.isUploading
                || Application.internetReachability == NetworkReachability.NotReachable)
                return;

            this.isUploading = true;
            try
            {
                IReadOnlyList<string> pendingFiles = this.archive.GetPendingFiles();
                foreach (string pendingFile in pendingFiles)
                {
                    if (!this.IsRecordingEnabled)
                        return;

                    try
                    {
                        await this.client.UploadFileAsync(pendingFile);
                        this.archive.RemovePending(pendingFile);
                    }
                    catch (SupabaseHttpException exception) when (exception.ResponseCode == 400
                        || exception.ResponseCode == 413 || exception.ResponseCode == 422)
                    {
                        Debug.LogWarning("AI game telemetry record was rejected and kept locally: "
                            + exception.Message);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI game telemetry upload deferred: " + exception.Message);
            }
            finally
            {
                this.isUploading = false;
            }
        }

        private void HandleLogMessage(string inCondition, string inStackTrace, LogType inType)
        {
            if (!this.IsRecordingEnabled || this.isHandlingDiagnostic || inType == LogType.Log
                || inCondition.StartsWith("AI game telemetry", StringComparison.Ordinal))
                return;

            this.isHandlingDiagnostic = true;
            try
            {
                this.archive.RecordDiagnostic(inType.ToString(), Sanitize(inCondition),
                    Sanitize(inStackTrace));
                if (inType == LogType.Error || inType == LogType.Assert
                    || inType == LogType.Exception)
                    Checkpoint();
            }
            catch (Exception)
            {
            }
            finally
            {
                this.isHandlingDiagnostic = false;
            }
        }

        private async Task CheckpointAsync()
        {
            if (!this.IsRecordingEnabled)
                return;

            try
            {
                await this.archive.CheckpointAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI game telemetry checkpoint failed: " + exception.Message);
            }
        }

        private static string Sanitize(string inValue)
        {
            if (string.IsNullOrEmpty(inValue))
                return inValue;

            string value = inValue.Replace(Application.dataPath, "<APPLICATION_DATA>")
                .Replace(Application.persistentDataPath, "<PERSISTENT_DATA>");
            string userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(userPath) ? value : value.Replace(userPath, "<USER_HOME>");
        }
    }
}
