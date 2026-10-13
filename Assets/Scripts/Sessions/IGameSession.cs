using Elemento.Domain;

public interface IGameSession
{
    EGameMode GameMode { get; }

    bool IsHumanSeat(int inPlayerIndex);
    bool ShouldRunAI(int inPlayerIndex);
    int GetViewingSeat(int inActivePlayerIndex);
    bool IsHandVisible(int inHandPlayerIndex, int inViewerPlayerIndex, bool inIsHandoffPending);
    bool RequiresHandoff(int inPreviousPlayerIndex, int inNextPlayerIndex);
}
