using System;

namespace Elemento.Domain.Net
{
    //Boundary between the client and the match authority. In-memory today, Supabase Edge Function later.
    //Live play listens to RevisionCommitted; async play just calls FetchSeatView when resuming.
    public interface IMatchTransport
    {
        event Action<string, int> RevisionCommitted;//matchId, revision

        MoveResultData Submit(MoveCommandData inCommand);
        MatchSeatViewData FetchSeatView(string inMatchId, int inSeat);
    }
}
