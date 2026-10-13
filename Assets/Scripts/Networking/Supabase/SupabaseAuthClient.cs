using System;
using System.Threading.Tasks;
using Elemento.Domain.Net;
using UnityEngine;

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseAuthClient
    {
        private const string SessionKey = "Elemento.Supabase.Session";
        private readonly SupabaseHttpClient http;
        private Task<SupabaseSessionData> pendingSession;
        private SupabaseSessionData session;

        public SupabaseSessionData Session => this.session;
        internal SupabaseHttpClient Http => this.http;

        public SupabaseAuthClient(MonoBehaviour inRunner)
        {
            this.http = new SupabaseHttpClient(inRunner);
            if (PlayerPrefs.HasKey(SessionKey))
            {
                try { this.session = SupabaseSessionData.Parse(PlayerPrefs.GetString(SessionKey)); }
                catch (Exception) { PlayerPrefs.DeleteKey(SessionKey); }
            }
        }

        public async Task<SupabaseSessionData> EnsureSessionAsync()
        {
            if (this.session != null && this.session.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60)
                return this.session;
            if (this.pendingSession != null)
                return await this.pendingSession;
            this.pendingSession = AuthenticateAsync();
            try { return await this.pendingSession; }
            finally { this.pendingSession = null; }
        }

        public async Task<SupabaseSessionData> RefreshSessionAsync()
        {
            if (this.session == null)
                return await EnsureSessionAsync();
            this.session.ExpiresAt = 0;
            return await EnsureSessionAsync();
        }

        private async Task<SupabaseSessionData> AuthenticateAsync()
        {
            string path = this.session == null ? "/auth/v1/signup" : "/auth/v1/token?grant_type=refresh_token";
            string body = this.session == null ? "{}" : "{\"refresh_token\":" + NetJson.Serialize(this.session.RefreshToken) + "}";
            var result = SupabaseSessionData.Parse(await this.http.SendAsync(path, body, null));
            if (result.ExpiresAt <= 0)
                result.ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + result.ExpiresIn;
            this.session = result;
            PlayerPrefs.SetString(SessionKey, NetJson.Serialize(result));
            PlayerPrefs.Save();
            return result;
        }
    }
}
