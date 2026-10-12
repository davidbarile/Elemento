using Elemento.Domain;

public sealed class LocalAiSession : IGameSession
{
    public EGameMode GameMode => EGameMode.AgainstAI;

    public bool IsHumanSeat(int inPlayerIndex)
    {
        return TurnRules.IsHumanSeat(this.GameMode, inPlayerIndex);
    }

    public bool ShouldRunAI(int inPlayerIndex)
    {
        return !IsHumanSeat(inPlayerIndex);
    }

    public bool IsHandVisible(int inHandPlayerIndex, int inViewerPlayerIndex, bool inIsHandoffPending)
    {
        return TurnRules.IsHandVisible(this.GameMode, inHandPlayerIndex, inViewerPlayerIndex, inIsHandoffPending);
    }

    public bool RequiresHandoff(int inPreviousPlayerIndex, int inNextPlayerIndex)
    {
        return TurnRules.RequiresHandoff(this.GameMode, inPreviousPlayerIndex, inNextPlayerIndex);
    }
}
