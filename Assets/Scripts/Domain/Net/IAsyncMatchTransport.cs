using System;
using System.Threading.Tasks;

namespace Elemento.Domain.Net
{
    public interface IAsyncMatchTransport
    {
        event Action<string, int> RevisionCommitted;
        Task<MoveResultData> SubmitMove(MoveCommandData inCommand);
        Task<MatchSeatViewData> FetchSeatView(string inMatchId);
    }
}
