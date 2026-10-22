using System;
using System.Collections.Generic;
using Elemento.Domain;
using Elemento.Domain.Net;

namespace Elemento.Networking.Supabase
{
    [Serializable]
    public sealed class GameStateWireData
    {
        [NetJsonName("v")] public int SchemaVersion;
        [NetJsonName("w")] public int RulesVersion;
        public string MatchId;
        [NetJsonName("s")] public int Seed;
        [NetJsonName("r")] public int Revision;
        [NetJsonName("t")] public int TurnNumber;
        [NetJsonName("b")] public int StartingPlayerId;
        public int CurrentPlayerId;
        [NetJsonName("p")] public ETurnPhase TurnPhase;
        [NetJsonName("m")] public int MovesRemaining;
        [NetJsonName("i")] public int WinnerPlayerId;
        [NetJsonName("o")] public EWinCondition WinCondition;
        [NetJsonName("n")] public int NextCardInstanceId;
        [NetJsonName("u")] public RulesConfigData Rules;
        [NetJsonName("a")] public RandomStateData Random;
        [NetJsonName("f")] public List<CardDefinitionWireData> Definitions = new();
        [NetJsonName("y")] public List<PlayerWireData> Players = new();
        [NetJsonName("d")] public List<CardWireData> Deck = new();
        [NetJsonName("e")] public List<CardWireData> ReserveDeck = new();
        [NetJsonName("q")] public List<CardWireData> PowerDeck = new();
        [NetJsonName("c")] public List<CardWireData> DiscardPile = new();
        [NetJsonName("x")] public List<MoveWireData> Moves = new();
    }

    [Serializable]
    public sealed class CardWireData
    {
        [NetJsonName("i")] public string Id;
        [NetJsonName("d")] public int DefinitionCode;
        [NetJsonName("f")] public bool IsFaceUp;
    }

    [Serializable]
    public sealed class CardDefinitionWireData
    {
        [NetJsonName("i")] public int DefinitionId;
        [NetJsonName("n")] public string DefinitionName;
        [NetJsonName("x")] public bool IsPowerCard;
        [NetJsonName("w")] public int[] WildValues;
        [NetJsonName("v")] public int[] ElementValues;
    }

    [Serializable]
    public sealed class ZoneWireData
    {
        [NetJsonName("i")] public int Id;
        [NetJsonName("e")] public EElementType ElementType;
        [NetJsonName("p")] public int Points;
        [NetJsonName("u")] public int FaceUpPoints;
        [NetJsonName("h")] public int Health;
        [NetJsonName("m")] public int MaxHealth;
        [NetJsonName("f")] public int FrozenHealth;
        [NetJsonName("a")] public bool IsActivated;
        [NetJsonName("x")] public bool IsDestroyed;
        [NetJsonName("c")] public List<CardWireData> Cards = new();
    }

    [Serializable]
    public sealed class PlayerWireData
    {
        [NetJsonName("i")] public int Id;
        [NetJsonName("n")] public string Name;
        [NetJsonName("h")] public List<CardWireData> Hand = new();
        [NetJsonName("z")] public List<ZoneWireData> Zones = new();
    }

    [Serializable]
    public sealed class MoveWireData
    {
        [NetJsonName("i")] public string Id;
        [NetJsonName("p")] public int PlayerId;
        [NetJsonName("t")] public EMoveType Type;
        [NetJsonName("c")] public string CardId;
        [NetJsonName("s")] public int SourceZoneId;
        [NetJsonName("a")] public int TargetPlayerId;
        [NetJsonName("z")] public int TargetZoneId;
        [NetJsonName("w")] public EPowerCardType PowerCardType;
        [NetJsonName("r")] public int ExpectedRevision;
    }

    public static class GameStateWireCodec
    {
        public static GameStateWireData Encode(GameStateData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));

            var definitions = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new GameStateWireData
            {
                SchemaVersion = inState.SchemaVersion,
                RulesVersion = inState.RulesVersion,
                MatchId = inState.MatchId,
                Seed = inState.Seed,
                Revision = inState.Revision,
                TurnNumber = inState.TurnNumber,
                StartingPlayerId = inState.StartingPlayerId,
                CurrentPlayerId = inState.CurrentPlayerId,
                TurnPhase = inState.TurnPhase,
                MovesRemaining = inState.MovesRemaining,
                WinnerPlayerId = inState.WinnerPlayerId,
                WinCondition = inState.WinCondition,
                NextCardInstanceId = inState.NextCardInstanceId,
                Rules = inState.Rules?.Clone(),
                Random = inState.Random?.Clone()
            };
            foreach (var player in inState.Players)
            {
                var compactPlayer = new PlayerWireData { Id = player.Id, Name = player.Name };
                EncodeCards(player.Hand, compactPlayer.Hand, result.Definitions, definitions);
                foreach (var zone in player.Zones)
                {
                    var compactZone = new ZoneWireData
                    {
                        Id = zone.Id,
                        ElementType = zone.ElementType,
                        Points = zone.Points,
                        FaceUpPoints = zone.FaceUpPoints,
                        Health = zone.Health,
                        MaxHealth = zone.MaxHealth,
                        FrozenHealth = zone.FrozenHealth,
                        IsActivated = zone.IsActivated,
                        IsDestroyed = zone.IsDestroyed
                    };
                    EncodeCards(zone.Cards, compactZone.Cards, result.Definitions, definitions);
                    compactPlayer.Zones.Add(compactZone);
                }
                result.Players.Add(compactPlayer);
            }
            EncodeCards(inState.Deck, result.Deck, result.Definitions, definitions);
            EncodeCards(inState.ReserveDeck, result.ReserveDeck, result.Definitions, definitions);
            EncodeCards(inState.PowerDeck, result.PowerDeck, result.Definitions, definitions);
            EncodeCards(inState.DiscardPile, result.DiscardPile, result.Definitions, definitions);
            foreach (var move in inState.Moves)
            {
                result.Moves.Add(new MoveWireData
                {
                    Id = move.Id,
                    PlayerId = move.PlayerId,
                    Type = move.Type,
                    CardId = move.CardId,
                    SourceZoneId = move.SourceZoneId,
                    TargetPlayerId = move.TargetPlayerId,
                    TargetZoneId = move.TargetZoneId,
                    PowerCardType = move.PowerCardType,
                    ExpectedRevision = move.ExpectedRevision
                });
            }
            return result;
        }

        public static GameStateData Decode(GameStateWireData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
            if (inState.Definitions == null)
                throw new FormatException("A compact game state is missing card definitions.");

            var result = new GameStateData
            {
                SchemaVersion = inState.SchemaVersion,
                RulesVersion = inState.RulesVersion,
                MatchId = inState.MatchId,
                Seed = inState.Seed,
                Revision = inState.Revision,
                TurnNumber = inState.TurnNumber,
                StartingPlayerId = inState.StartingPlayerId,
                CurrentPlayerId = inState.CurrentPlayerId,
                TurnPhase = inState.TurnPhase,
                MovesRemaining = inState.MovesRemaining,
                WinnerPlayerId = inState.WinnerPlayerId,
                WinCondition = inState.WinCondition,
                NextCardInstanceId = inState.NextCardInstanceId,
                Rules = inState.Rules?.Clone() ?? new RulesConfigData(),
                Random = inState.Random?.Clone() ?? new RandomStateData()
            };
            foreach (var player in inState.Players)
            {
                var fullPlayer = new PlayerStateData { Id = player.Id, Name = player.Name };
                DecodeCards(player.Hand, fullPlayer.Hand, inState.Definitions);
                foreach (var zone in player.Zones)
                {
                    var fullZone = new ZoneStateData
                    {
                        Id = zone.Id,
                        ElementType = zone.ElementType,
                        Points = zone.Points,
                        FaceUpPoints = zone.FaceUpPoints,
                        Health = zone.Health,
                        MaxHealth = zone.MaxHealth,
                        FrozenHealth = zone.FrozenHealth,
                        IsActivated = zone.IsActivated,
                        IsDestroyed = zone.IsDestroyed
                    };
                    DecodeCards(zone.Cards, fullZone.Cards, inState.Definitions);
                    fullPlayer.Zones.Add(fullZone);
                }
                result.Players.Add(fullPlayer);
            }
            DecodeCards(inState.Deck, result.Deck, inState.Definitions);
            DecodeCards(inState.ReserveDeck, result.ReserveDeck, inState.Definitions);
            DecodeCards(inState.PowerDeck, result.PowerDeck, inState.Definitions);
            DecodeCards(inState.DiscardPile, result.DiscardPile, inState.Definitions);
            foreach (var move in inState.Moves)
            {
                result.Moves.Add(new MoveData
                {
                    Id = move.Id,
                    PlayerId = move.PlayerId,
                    Type = move.Type,
                    CardId = move.CardId,
                    SourceZoneId = move.SourceZoneId,
                    TargetPlayerId = move.TargetPlayerId,
                    TargetZoneId = move.TargetZoneId,
                    PowerCardType = move.PowerCardType,
                    ExpectedRevision = move.ExpectedRevision
                });
            }
            return result;
        }

        private static void EncodeCards(List<CardInstanceData> inCards,
            List<CardWireData> inTarget, List<CardDefinitionWireData> inDefinitions,
            Dictionary<string, int> inDefinitionCodes)
        {
            foreach (var card in inCards)
            {
                var definition = new CardDefinitionWireData
                {
                    DefinitionId = card.DefinitionId,
                    DefinitionName = card.DefinitionName,
                    IsPowerCard = card.IsPowerCard,
                    WildValues = new[]
                    {
                        card.WildPoints, card.WildMultiplier, card.WildBomb, card.WildShield,
                        card.WildZoneHeal, card.WildFreeze, card.WildLightning
                    },
                    ElementValues = CombineValues(card.Points, card.Multipliers,
                        card.Bombs, card.Shields)
                };
                string signature = NetJson.Serialize(definition);
                if (!inDefinitionCodes.TryGetValue(signature, out int code))
                {
                    code = inDefinitions.Count;
                    inDefinitionCodes.Add(signature, code);
                    inDefinitions.Add(definition);
                }
                inTarget.Add(new CardWireData
                {
                    Id = card.Id,
                    DefinitionCode = code,
                    IsFaceUp = card.IsFaceUp
                });
            }
        }

        private static void DecodeCards(List<CardWireData> inCards,
            List<CardInstanceData> inTarget, List<CardDefinitionWireData> inDefinitions)
        {
            if (inCards == null)
                throw new FormatException("A compact game state is missing a card collection.");
            foreach (var card in inCards)
            {
                if (card.DefinitionCode < 0 || card.DefinitionCode >= inDefinitions.Count)
                    throw new FormatException("A compact game state contains an invalid card definition code.");
                var definition = inDefinitions[card.DefinitionCode];
                if (definition.WildValues == null || definition.WildValues.Length != 7
                    || definition.ElementValues == null || definition.ElementValues.Length != 16)
                    throw new FormatException("A compact game state contains an invalid card definition.");
                var decoded = new CardInstanceData
                {
                    Id = card.Id,
                    DefinitionId = definition.DefinitionId,
                    DefinitionName = definition.DefinitionName,
                    IsFaceUp = card.IsFaceUp,
                    IsPowerCard = definition.IsPowerCard,
                    WildPoints = definition.WildValues[0],
                    WildMultiplier = definition.WildValues[1],
                    WildBomb = definition.WildValues[2],
                    WildShield = definition.WildValues[3],
                    WildZoneHeal = definition.WildValues[4],
                    WildFreeze = definition.WildValues[5],
                    WildLightning = definition.WildValues[6],
                    Points = SliceValues(definition.ElementValues, 0),
                    Multipliers = SliceValues(definition.ElementValues, 4),
                    Bombs = SliceValues(definition.ElementValues, 8),
                    Shields = SliceValues(definition.ElementValues, 12)
                };
                inTarget.Add(decoded);
            }
        }

        private static int[] CombineValues(params int[][] inValues)
        {
            var result = new int[16];
            int targetIndex = 0;
            foreach (var values in inValues)
            {
                for (int i = 0; i < 4; ++i)
                    result[targetIndex++] = values != null && i < values.Length ? values[i] : 0;
            }
            return result;
        }

        private static int[] SliceValues(int[] inValues, int inStart)
        {
            var result = new int[4];
            Array.Copy(inValues, inStart, result, 0, result.Length);
            return result;
        }
    }
}
