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
        public string UserId => this.client.UserId;

        public AiGameTelemetryService()
        {
            this.archive = new AiGameArchive(Path.Combine(
                Application.persistentDataPath, "AiGameTelemetry"));
            this.client = new AiGameTelemetryClient(OnlineCoroutineHost.IN);
        }

        public void Initialize()
        {
            this.archive.RecoverInterruptedGame();
            Application.logMessageReceived += HandleLogMessage;
            _ = FlushPendingAsync();
        }

        public void RecordSnapshot(AiGameContextData inContext, BoardState inState,
            MoveData inMove, string inReason)
        {
            try
            {
                this.archive.RecordSnapshot(inContext, inState, inMove, inReason);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("AI game telemetry snapshot failed: " + exception.Message);
            }
        }

        public void Complete(string inStatus, bool? inPlayerWon = null,
            string inResultMessage = null)
        {
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
            if (this.isUploading || Application.internetReachability == NetworkReachability.NotReachable)
                return;

            this.isUploading = true;
            try
            {
                IReadOnlyList<string> pendingFiles = this.archive.GetPendingFiles();
                foreach (string pendingFile in pendingFiles)
                {
                    try
                    {
                        AiGameData game = this.archive.LoadPending(pendingFile);
                        await this.client.UploadAsync(game);
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
            if (this.isHandlingDiagnostic || inType == LogType.Log
                || inCondition.StartsWith("AI game telemetry", StringComparison.Ordinal))
                return;

            this.isHandlingDiagnostic = true;
            try
            {
                this.archive.RecordDiagnostic(inType.ToString(), Sanitize(inCondition),
                    Sanitize(inStackTrace));
            }
            catch (Exception)
            {
            }
            finally
            {
                this.isHandlingDiagnostic = false;
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
