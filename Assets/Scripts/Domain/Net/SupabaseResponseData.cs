using System;

namespace Elemento.Domain.Net
{
    [Serializable]
    public sealed class SupabaseMatchResponseData
    {
        [NetJsonName("match")] public SupabaseMatchRecordData Match;
        [NetJsonName("seat")] public int Seat;
        [NetJsonName("view")] public MatchSeatViewData View;

        public static SupabaseMatchResponseData Parse(string inJson)
        {
            var result = NetJson.Deserialize<SupabaseMatchResponseData>(inJson);
            if (result == null || result.Match == null || string.IsNullOrEmpty(result.Match.Id))
                throw new FormatException("Missing match in Supabase response.");
            if (result.View != null && (result.View.View == null || result.View.MatchId != result.Match.Id))
                throw new FormatException("Invalid Supabase seat view.");
            return result;
        }
    }

    [Serializable]
    public sealed class SupabaseMatchRecordData
    {
        [NetJsonName("id")] public string Id;
        [NetJsonName("invite_code")] public string InviteCode;
        [NetJsonName("mode")] public string Mode;
        [NetJsonName("status")] public string Status;
        [NetJsonName("revision")] public int Revision;
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
