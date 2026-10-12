using System;
using System.Collections.Generic;
using System.Globalization;

namespace Elemento.Domain
{
    public enum EElementType
    {
        None,
        Air,
        Earth,
        Fire,
        Water,
        Wild
    }

    public enum ETurnPhase
    {
        PlayerDraw,
        PlayerPlaying,
        OpponentDraw,
        OpponentPlaying,
        None
    }

    public enum EMoveType
    {
        None,
        DrawCard,
        PlayCard,
        DiscardCard,
        ActivateZone,
        EndTurn
    }

    public enum EWinCondition
    {
        None,
        PointsTarget,
        AllZonesDestroyed
    }

    public enum EDiscardMode
    {
        None,
        AllowDiscard,
        DiscardEndsTurn
    }

    [Serializable]
    public sealed class RulesConfigData
    {
        public int NumCardsToDeal = 5;
        public int MaxCardsInHand = 20;
        public int MovesPerTurn;
        public int ElementZoneStartingHealth = 10;
        public int FrozenZoneHealth = 5;
        public int LightningDamage = 5;
        public int ElementZonePointsRequiredToActivate = 10;
        public int TotalPointsRequiredToWin = 50;
        public int FrozenZonesMeltAmountPerTurn = 1;
        public EDiscardMode DiscardMode;

        public RulesConfigData Clone()
        {
            return new RulesConfigData
            {
                NumCardsToDeal = this.NumCardsToDeal,
                MaxCardsInHand = this.MaxCardsInHand,
                MovesPerTurn = this.MovesPerTurn,
                ElementZoneStartingHealth = this.ElementZoneStartingHealth,
                FrozenZoneHealth = this.FrozenZoneHealth,
                LightningDamage = this.LightningDamage,
                ElementZonePointsRequiredToActivate = this.ElementZonePointsRequiredToActivate,
                TotalPointsRequiredToWin = this.TotalPointsRequiredToWin,
                FrozenZonesMeltAmountPerTurn = this.FrozenZonesMeltAmountPerTurn,
                DiscardMode = this.DiscardMode
            };
        }
    }

    [Serializable]
    public sealed class RandomStateData
    {
        public uint State;
        public int Cursor;

        public RandomStateData Clone()
        {
            return new RandomStateData
            {
                State = this.State,
                Cursor = this.Cursor
            };
        }
    }

    [Serializable]
    public sealed class CardInstanceData
    {
        public string Id;
        public int DefinitionId;
        public string DefinitionName;
        public bool IsFaceUp;
        public bool IsPowerCard;
        public int WildPoints;
        public int WildMultiplier;
        public int WildBomb;
        public int WildShield;
        public int WildZoneHeal;
        public int WildFreeze;
        public int WildLightning;
        public int[] Points = new int[4];
        public int[] Multipliers = new int[4];
        public int[] Bombs = new int[4];
        public int[] Shields = new int[4];

        public static string CreateId(int inSequence)
        {
            return "card-" + inSequence.ToString("D8", CultureInfo.InvariantCulture);
        }

        public CardInstanceData Clone()
        {
            return new CardInstanceData
            {
                Id = this.Id,
                DefinitionId = this.DefinitionId,
                DefinitionName = this.DefinitionName,
                IsFaceUp = this.IsFaceUp,
                IsPowerCard = this.IsPowerCard,
                WildPoints = this.WildPoints,
                WildMultiplier = this.WildMultiplier,
                WildBomb = this.WildBomb,
                WildShield = this.WildShield,
                WildZoneHeal = this.WildZoneHeal,
                WildFreeze = this.WildFreeze,
                WildLightning = this.WildLightning,
                Points = CloneValues(this.Points),
                Multipliers = CloneValues(this.Multipliers),
                Bombs = CloneValues(this.Bombs),
                Shields = CloneValues(this.Shields)
            };
        }

        private static int[] CloneValues(int[] inValues)
        {
            if (inValues == null)
                return Array.Empty<int>();

            return (int[])inValues.Clone();
        }
    }

    [Serializable]
    public sealed class ZoneStateData
    {
        public int Id;
        public EElementType ElementType;
        public int Points;
        public int FaceUpPoints;
        public int Health;
        public int MaxHealth;
        public int FrozenHealth;
        public bool IsActivated;
        public bool IsDestroyed;
        public List<CardInstanceData> Cards = new();

        public bool IsFrozen
        {
            get { return this.FrozenHealth > 0; }
        }

        public ZoneStateData Clone()
        {
            var clone = new ZoneStateData
            {
                Id = this.Id,
                ElementType = this.ElementType,
                Points = this.Points,
                FaceUpPoints = this.FaceUpPoints,
                Health = this.Health,
                MaxHealth = this.MaxHealth,
                FrozenHealth = this.FrozenHealth,
                IsActivated = this.IsActivated,
                IsDestroyed = this.IsDestroyed
            };

            foreach (var card in this.Cards)
                clone.Cards.Add(card.Clone());

            return clone;
        }
    }

    [Serializable]
    public sealed class PlayerStateData
    {
        public int Id;
        public string Name;
        public List<CardInstanceData> Hand = new();
        public List<ZoneStateData> Zones = new();

        public PlayerStateData Clone()
        {
            var clone = new PlayerStateData
            {
                Id = this.Id,
                Name = this.Name
            };

            foreach (var card in this.Hand)
                clone.Hand.Add(card.Clone());

            foreach (var zone in this.Zones)
                clone.Zones.Add(zone.Clone());

            return clone;
        }
    }

    [Serializable]
    public sealed class MoveData
    {
        public string Id;
        public int PlayerId;
        public EMoveType Type;
        public string CardId;
        public int SourceZoneId;
        public int TargetPlayerId;
        public int TargetZoneId;
        public int ExpectedRevision;

        public static string CreateId(int inSequence)
        {
            return "move-" + inSequence.ToString("D8", CultureInfo.InvariantCulture);
        }

        public MoveData Clone()
        {
            return new MoveData
            {
                Id = this.Id,
                PlayerId = this.PlayerId,
                Type = this.Type,
                CardId = this.CardId,
                SourceZoneId = this.SourceZoneId,
                TargetPlayerId = this.TargetPlayerId,
                TargetZoneId = this.TargetZoneId,
                ExpectedRevision = this.ExpectedRevision
            };
        }
    }

    [Serializable]
    public sealed class GameStateData
    {
        public int SchemaVersion = 1;
        public int RulesVersion = 1;
        public string MatchId;
        public int Revision;
        public int TurnNumber;
        public int CurrentPlayerId;
        public ETurnPhase TurnPhase = ETurnPhase.None;
        public int MovesRemaining;
        public int WinnerPlayerId;
        public EWinCondition WinCondition;
        public int NextCardInstanceId;
        public RulesConfigData Rules = new();
        public RandomStateData Random = new();
        public List<PlayerStateData> Players = new();
        public List<CardInstanceData> Deck = new();
        public List<CardInstanceData> PowerDeck = new();
        public List<CardInstanceData> DiscardPile = new();
        public List<MoveData> Moves = new();

        public GameStateData Clone()
        {
            var clone = new GameStateData
            {
                SchemaVersion = this.SchemaVersion,
                RulesVersion = this.RulesVersion,
                MatchId = this.MatchId,
                Revision = this.Revision,
                TurnNumber = this.TurnNumber,
                CurrentPlayerId = this.CurrentPlayerId,
                TurnPhase = this.TurnPhase,
                MovesRemaining = this.MovesRemaining,
                WinnerPlayerId = this.WinnerPlayerId,
                WinCondition = this.WinCondition,
                NextCardInstanceId = this.NextCardInstanceId,
                Rules = this.Rules.Clone(),
                Random = this.Random.Clone()
            };

            foreach (var player in this.Players)
                clone.Players.Add(player.Clone());

            CloneCards(this.Deck, clone.Deck);
            CloneCards(this.PowerDeck, clone.PowerDeck);
            CloneCards(this.DiscardPile, clone.DiscardPile);

            foreach (var move in this.Moves)
                clone.Moves.Add(move.Clone());

            return clone;
        }

        private static void CloneCards(List<CardInstanceData> inSource, List<CardInstanceData> inTarget)
        {
            foreach (var card in inSource)
                inTarget.Add(card.Clone());
        }
    }
}
