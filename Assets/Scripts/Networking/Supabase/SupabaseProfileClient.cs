using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Elemento.Domain.Net;

namespace Elemento.Networking.Supabase
{
    public sealed class SupabaseProfileClient
    {
        private readonly SupabaseAuthClient auth;

        public SupabaseProfileClient(SupabaseAuthClient inAuth)
        {
            this.auth = inAuth ?? throw new ArgumentNullException(nameof(inAuth));
        }

        public async Task<bool> IsUsernameAvailableAsync(string inUsername)
        {
            if (!IsValidUsername(inUsername))
                return false;
            var session = await this.auth.EnsureSessionAsync();
            string body = "{\"p_username\":" + NetJson.Serialize(inUsername) + "}";
            return NetJson.Deserialize<bool>(await this.auth.Http.SendAsync("/rest/v1/rpc/is_username_available", body, session.AccessToken));
        }

        public async Task CreateProfileAsync(string inUsername)
        {
            if (!IsValidUsername(inUsername))
                throw new ArgumentException("Username must contain 3–20 letters, digits, or underscores.", nameof(inUsername));
            var session = await this.auth.EnsureSessionAsync();
            string body = "{\"id\":" + NetJson.Serialize(session.UserId) + ",\"username\":" + NetJson.Serialize(inUsername) + "}";
            // An insert must fail on duplicate usernames/ids; never use merge-duplicates.
            await this.auth.Http.SendAsync("/rest/v1/profiles", body, session.AccessToken);
        }

        public async Task<string> GetUsernameAsync()
        {
            var session = await this.auth.EnsureSessionAsync();
            string json = await this.auth.Http.SendAsync("/rest/v1/profiles?select=username&id=eq." + Uri.EscapeDataString(session.UserId), null, session.AccessToken, "GET");
            var profiles = NetJson.Deserialize<ProfileData[]>(json);
            return profiles.Length == 0 ? null : profiles[0].Username;
        }

        private static bool IsValidUsername(string inUsername)
        {
            return inUsername != null && Regex.IsMatch(inUsername, "\\A[A-Za-z0-9_]{3,20}\\z");
        }

        [Serializable]
        public sealed class ProfileData
        {
            [NetJsonName("username")] public string Username;
        }
    }
}
