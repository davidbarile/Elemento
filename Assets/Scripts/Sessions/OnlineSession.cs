using Elemento.Domain;
using Elemento.Domain.Net;

//Online match: the local seat is always the viewing seat (bottom board), no handoff, no AI.
public sealed class OnlineSession : IGameSession
{
    public EGameMode GameMode => EGameMode.Multiplayer;
    public int LocalSeat { get; }
    public OnlineMatchClient Client { get; }

    public OnlineSession(int inLocalSeat, OnlineMatchClient inClient = null)
    {
        this.LocalSeat = inLocalSeat;
        this.Client = inClient;
    }

    //only the local seat takes input on this device; the remote seat arrives via the transport
    public bool IsHumanSeat(int inPlayerIndex)
    {
        return inPlayerIndex == this.LocalSeat;
    }

    public bool IsLocalSeat(int inPlayerIndex)
    {
        return inPlayerIndex == this.LocalSeat;
    }

    public bool ShouldRunAI(int inPlayerIndex)
    {
        return false;
    }

    public int GetViewingSeat(int inActivePlayerIndex)
    {
        return this.LocalSeat;
    }

    public bool IsHandVisible(int inHandPlayerIndex, int inViewerPlayerIndex, bool inIsHandoffPending)
    {
        return inHandPlayerIndex == this.LocalSeat;
    }

    public bool RequiresHandoff(int inPreviousPlayerIndex, int inNextPlayerIndex)
    {
        return false;
    }
}
