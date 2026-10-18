using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    public interface IAsyncMatchTransport
    {
        event Action<string, int> RevisionCommitted;
        Task<MatchStateUpdateData> CreateMatch(CreateMatchRequestData inRequest);
        Task<MatchStateUpdateData> JoinMatch(JoinMatchRequestData inRequest);
        Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn);
        Task<LeaveMatchResultData> LeaveMatch(string inMatchId);
        Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest);
        Task<HeartbeatResultData> Heartbeat(HeartbeatRequestData inRequest);
        Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest);
    }
}
