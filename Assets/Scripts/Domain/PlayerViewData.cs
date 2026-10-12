using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    [Serializable]
    public sealed class OpponentViewData
    {
        public int Id;
        public string Name;
        public int HandCount;
        public List<ZoneStateData> Zones = new();
    }

    [Serializable]
    public sealed class PlayerViewData
    {
        public int Revision;
        public int TurnNumber;
        public int CurrentPlayerId;
        public ETurnPhase TurnPhase;
        public int MovesRemaining;
        public int WinnerPlayerId;
        public EWinCondition WinType;
        public int DeckCount;
        public int PowerDeckCount;
        public RulesConfigData Rules;
        public PlayerStateData Player;
        public List<OpponentViewData> Opponents = new();
        public List<MoveData> LegalMoves = new();
    }
}
