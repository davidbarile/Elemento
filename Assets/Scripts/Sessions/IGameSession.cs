using Elemento.Domain;

public interface IGameSession
{
    EGameMode GameMode { get; }

    bool IsHumanSeat(int inPlayerIndex);
    bool ShouldRunAI(int inPlayerIndex);
    bool IsHandVisible(int inHandPlayerIndex, int inViewerPlayerIndex, bool inIsHandoffPending);
    bool RequiresHandoff(int inPreviousPlayerIndex, int inNextPlayerIndex);
}
