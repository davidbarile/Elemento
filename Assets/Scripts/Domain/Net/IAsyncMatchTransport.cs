using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    public interface IAsyncMatchTransport
    {
        event Action<string, int> RevisionCommitted;
        Task<SubmitTurnResultData> SubmitTurn(SubmitTurnRequestData inTurn);
        Task LeaveMatch(string inMatchId);
        Task<OnlineViewUpdateData> FetchView(string inMatchId, int? inSinceRevision = null);
    }
}
