using System;

namespace Elemento.Domain.Net
{
    public static class SubmitTurnResponseData
    {
        public static SubmitTurnResultData Parse(string inJson)
        {
            var result = NetJson.Deserialize<SubmitTurnResultData>(inJson);
            if (result == null)
                throw new FormatException("Missing submit-turn response.");
            if (result.NewRevision < 0)
                throw new FormatException("Invalid submit-turn response counters.");
            return result;
        }
    }

    [Serializable]
    public sealed class SupabaseSessionData
    {
        [NetJsonName("access_token")] public string AccessToken;
        [NetJsonName("refresh_token")] public string RefreshToken;
        [NetJsonName("expires_at")] public long ExpiresAt;
        [NetJsonName("expires_in")] public int ExpiresIn;
        [NetJsonName("user")] public SupabaseUserData User;

        public string UserId => this.User?.Id;

        public static SupabaseSessionData Parse(string inJson)
        {
            var result = NetJson.Deserialize<SupabaseSessionData>(inJson);
            if (result == null || string.IsNullOrEmpty(result.AccessToken)
                || string.IsNullOrEmpty(result.RefreshToken) || string.IsNullOrEmpty(result.UserId))
                throw new FormatException("Incomplete Supabase auth session.");
            return result;
        }
    }

    [Serializable]
    public sealed class SupabaseUserData
    {
        [NetJsonName("id")] public string Id;
    }
}
