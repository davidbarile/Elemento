using System;

namespace Elemento.Domain
{
    public enum EGameMode
    {
        AgainstAI,
        PassAndPlay,
        Multiplayer
    }

    public enum ESeatType
    {
        Human,
        Ai
    }

    [Serializable]
    public sealed class TurnStateData
    {
        public int PreviousPlayerIndex = -1;
        public int CurrentPlayerIndex;
        public bool IsHandoffPending;
    }

    public static class TurnRules
    {
        public static int GetNextPlayerIndex(int inCurrentPlayerIndex, int inPlayerCount)
        {
            if (inPlayerCount < 1)
                throw new ArgumentOutOfRangeException(nameof(inPlayerCount));
            if (inCurrentPlayerIndex < 0 || inCurrentPlayerIndex >= inPlayerCount)
                throw new ArgumentOutOfRangeException(nameof(inCurrentPlayerIndex));

            return (inCurrentPlayerIndex + 1) % inPlayerCount;
        }

        public static ESeatType GetSeatType(EGameMode inGameMode, int inPlayerIndex)
        {
            if (inPlayerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(inPlayerIndex));

            if (inGameMode == EGameMode.PassAndPlay)
                return ESeatType.Human;

            return inPlayerIndex == 0 ? ESeatType.Human : ESeatType.Ai;
        }

        public static bool IsHumanSeat(EGameMode inGameMode, int inPlayerIndex)
        {
            return GetSeatType(inGameMode, inPlayerIndex) == ESeatType.Human;
        }

        public static bool IsHandVisible(EGameMode inGameMode, int inHandPlayerIndex, int inViewerPlayerIndex, bool inIsHandoffPending)
        {
            if (inHandPlayerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(inHandPlayerIndex));
            if (inViewerPlayerIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(inViewerPlayerIndex));

            if (inGameMode == EGameMode.PassAndPlay)
                return !inIsHandoffPending && inHandPlayerIndex == inViewerPlayerIndex;

            return inHandPlayerIndex == 0 && inViewerPlayerIndex == 0;
        }

        public static bool RequiresHandoff(EGameMode inGameMode, int inPreviousPlayerIndex, int inNextPlayerIndex)
        {
            return inGameMode == EGameMode.PassAndPlay
                && inPreviousPlayerIndex >= 0
                && inPreviousPlayerIndex != inNextPlayerIndex;
        }
    }
}
