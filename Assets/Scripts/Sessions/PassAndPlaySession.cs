using Elemento.Domain;

public sealed class PassAndPlaySession : IGameSession
{
    public EGameMode GameMode => EGameMode.PassAndPlay;

    public bool IsHumanSeat(int inPlayerIndex)
    {
        return TurnRules.IsHumanSeat(this.GameMode, inPlayerIndex);
    }

    public bool ShouldRunAI(int inPlayerIndex)
    {
        return false;
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
