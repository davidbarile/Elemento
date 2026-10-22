using System;
using System.IO;
using System.Text;
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

        public async Task UploadFileAsync(string inPath)
        {
            if (string.IsNullOrEmpty(inPath))
                throw new ArgumentException("An AI game telemetry file is required.", nameof(inPath));

            var session = await this.auth.EnsureSessionAsync();
            byte[] body = await Task.Run(() => CreateUploadBody(inPath));
            await this.auth.Http.SendBytesAsync("/functions/v1/record-ai-game", body,
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

        private static byte[] CreateUploadBody(string inPath)
        {
            byte[] prefix = Encoding.UTF8.GetBytes("{\"Game\":");
            byte[] game = File.ReadAllBytes(inPath);
            var body = new byte[prefix.Length + game.Length + 1];
            Buffer.BlockCopy(prefix, 0, body, 0, prefix.Length);
            Buffer.BlockCopy(game, 0, body, prefix.Length, game.Length);
            body[body.Length - 1] = (byte)'}';
            return body;
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
