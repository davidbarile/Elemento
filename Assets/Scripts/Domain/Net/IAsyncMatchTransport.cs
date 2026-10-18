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
        Task LeaveMatch(string inMatchId);
        Task<MatchStateUpdateData> FetchView(GetViewRequestData inRequest);
        Task<ListMyMatchesResultData> ListMyMatches(ListMyMatchesRequestData inRequest);
    }
}
