using System;
using System.Collections.Generic;

namespace Elemento.Domain.Net
{
    [Serializable]
    public sealed class SupabaseMatchResponseData
    {
        [NetJsonName("match")] public SupabaseMatchRecordData Match;
        [NetJsonName("seat")] public int Seat;
        [NetJsonName("view")] public MatchSeatViewData View;
        [NetJsonName("moves")] public List<SupabaseMoveRowData> Moves = new();

        public static SupabaseMatchResponseData Parse(string inJson)
        {
            var result = NetJson.Deserialize<SupabaseMatchResponseData>(inJson);
            if (result == null || result.Match == null || string.IsNullOrEmpty(result.Match.Id))
                throw new FormatException("Missing match in Supabase response.");
            if (result.View == null && result.Match.Status != "waiting")
                throw new FormatException("Missing Supabase seat view for a non-waiting match.");
            if (result.View != null && (result.View.View == null || result.View.MatchId != result.Match.Id))
                throw new FormatException("Invalid Supabase seat view.");
            return result;
        }

        public OnlineViewUpdateData ToViewUpdate()
        {
            var update = new OnlineViewUpdateData
            {
                Status = this.Match.Status,
                MatchRevision = this.Match.Revision,
                Rules = this.Match.Rules ?? this.View?.View?.Rules,
                View = this.View
            };
            if (this.Moves == null)
                return update;

            foreach (var row in this.Moves)
            {
                if (row?.Command?.Move == null)
                    throw new FormatException("Invalid move log row in Supabase response.");
                update.Moves.Add(new MoveLogEntryData
                {
                    MatchId = row.Command.MatchId,
                    Seat = row.Seat,
                    ClientMoveSequence = row.Command.ClientMoveSequence,
                    ClientNonce = row.Command.ClientNonce,
                    RevisionBefore = row.Command.ExpectedRevision,
                    RevisionAfter = row.Revision,
                    Move = row.Command.Move.Clone(),
                    ReplayCard = row.Command.ReplayCard?.Clone()
                });
            }

            return update;
        }
    }

    [Serializable]
    public sealed class SupabaseMoveRowData
    {
        [NetJsonName("revision")] public int Revision;
        [NetJsonName("seat")] public int Seat;
        [NetJsonName("command")] public MoveCommandData Command;
    }

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
    public sealed class SupabaseMatchRecordData
    {
        [NetJsonName("id")] public string Id;
        [NetJsonName("invite_code")] public string InviteCode;
        [NetJsonName("mode")] public string Mode;
        [NetJsonName("status")] public string Status;
        [NetJsonName("rules")] public RulesConfigData Rules;
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
