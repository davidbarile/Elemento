using System;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using Elemento.Networking.Supabase;

namespace Elemento.Telemetry
{
    public sealed class AiGameTelemetryClient
    {
        private readonly SupabaseAuthClient auth;

        public string UserId => this.auth.Session?.UserId;

        public AiGameTelemetryClient(UnityEngine.MonoBehaviour inRunner)
        {
            this.auth = new SupabaseAuthClient(inRunner);
        }

        public async Task UploadAsync(AiGameData inGame)
        {
            if (inGame == null)
                throw new ArgumentNullException(nameof(inGame));

            var session = await this.auth.EnsureSessionAsync();
            await this.auth.Http.SendAsync("/functions/v1/record-ai-game",
                NetJson.Serialize(new RecordAiGameRequestData { Game = inGame }),
                session.AccessToken);
        }

        public async Task<int> ClearAsync(bool inAllGames)
        {
            var session = await this.auth.EnsureSessionAsync();
            string json = await this.auth.Http.SendAsync("/functions/v1/clear-ai-games",
                NetJson.Serialize(new ClearAiGamesRequestData
                {
                    Scope = inAllGames ? "all" : "owner"
                }), session.AccessToken);
            return NetJson.Deserialize<ClearAiGamesResponseData>(json)?.Deleted ?? 0;
        }

        [Serializable]
        private sealed class RecordAiGameRequestData
        {
            public AiGameData Game;
        }

        [Serializable]
        private sealed class ClearAiGamesRequestData
        {
            public string Scope;
        }

        [Serializable]
        private sealed class ClearAiGamesResponseData
        {
            [NetJsonName("deleted")] public int Deleted;
        }
    }
}
