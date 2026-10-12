using System.Collections.Generic;
using Elemento.Domain;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class GameRulesTests
    {
        [Test]
        public void CalculateZonePoints_SumsBeforeApplyingEachMultiplier()
        {
            var zone = CreateZone(EElementType.Air,
                CreateCard(points: 2),
                CreateCard(points: 3),
                CreateCard(multiplier: 2),
                CreateCard(multiplier: 3));

            Assert.That(GameRules.CalculateZonePoints(zone), Is.EqualTo(30));
        }

        [Test]
        public void CalculateZonePoints_WildValuesReplaceElementValues()
        {
            var card = CreateCard(points: 7, multiplier: 3);
            card.WildPoints = 2;
            card.WildMultiplier = 2;
            var zone = CreateZone(EElementType.Air, card);

            Assert.That(GameRules.CalculateZonePoints(zone), Is.EqualTo(4));
        }

        [Test]
        public void CalculateZonePoints_FaceUpOnlyExcludesHiddenPointsAndMultipliers()
        {
            var visiblePoints = CreateCard(points: 3);
            var hiddenPoints = CreateCard(points: 5);
            hiddenPoints.IsFaceUp = false;
            var visibleMultiplier = CreateCard(multiplier: 2);
            var hiddenMultiplier = CreateCard(multiplier: 3);
            hiddenMultiplier.IsFaceUp = false;
            var zone = CreateZone(EElementType.Air, visiblePoints, hiddenPoints, visibleMultiplier, hiddenMultiplier);

            Assert.That(GameRules.CalculateZonePoints(zone), Is.EqualTo(48));
            Assert.That(GameRules.CalculateZonePoints(zone, true), Is.EqualTo(6));
        }

        [TestCase(9, 10, false)]
        [TestCase(10, 10, true)]
        [TestCase(11, 10, true)]
        public void HasReachedActivationThreshold_IsInclusive(int points, int requiredPoints, bool expected)
        {
            Assert.That(GameRules.HasReachedActivationThreshold(points, requiredPoints), Is.EqualTo(expected));
        }

        [TestCase(49, false)]
        [TestCase(50, true)]
        public void HasPointsTargetWin_IsInclusive(int points, bool expected)
        {
            var player = new PlayerStateData();
            player.Zones.Add(CreateZone(EElementType.Air, CreateCard(points: points)));
            var rules = new RulesConfigData { TotalPointsRequiredToWin = 50 };

            Assert.That(GameRules.HasPointsTargetWin(player, rules), Is.EqualTo(expected));
        }

        [Test]
        public void AreAllZonesDestroyed_RequiresEveryZone()
        {
            var player = new PlayerStateData();
            player.Zones.Add(new ZoneStateData { IsDestroyed = true });
            player.Zones.Add(new ZoneStateData { IsDestroyed = false });

            Assert.That(GameRules.AreAllZonesDestroyed(player), Is.False);

            player.Zones[1].IsDestroyed = true;

            Assert.That(GameRules.AreAllZonesDestroyed(player), Is.True);
        }

        [Test]
        public void DeterministicRandom_SameSeedProducesSameSequenceAndCursor()
        {
            var first = new DeterministicRandom(12345);
            var second = new DeterministicRandom(12345);

            for (int i = 0; i < 20; ++i)
                Assert.That(first.NextUInt(), Is.EqualTo(second.NextUInt()));

            Assert.That(first.GetState().Cursor, Is.EqualTo(20));
            Assert.That(first.GetState().State, Is.EqualTo(second.GetState().State));
        }

        [Test]
        public void DeterministicRandom_StateResumesSequence()
        {
            var original = new DeterministicRandom(17);
            original.NextUInt();
            original.NextUInt();
            var snapshot = original.GetState();
            uint expectedNext = original.NextUInt();

            var resumed = new DeterministicRandom(snapshot);

            Assert.That(resumed.NextUInt(), Is.EqualTo(expectedNext));
            Assert.That(resumed.GetState().Cursor, Is.EqualTo(snapshot.Cursor + 1));
        }

        [Test]
        public void DeterministicShuffle_ProducesStableOrder()
        {
            List<int> values = new() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            DeterministicShuffle.Shuffle(values, new DeterministicRandom(12345));

            Assert.That(values, Is.EqualTo(new[] { 1, 8, 2, 4, 7, 6, 5, 9, 3, 0 }));
        }

        [Test]
        public void CardInstanceId_IsStableAcrossCreationAndClone()
        {
            string firstId = CardInstanceData.CreateId(42);
            string secondId = CardInstanceData.CreateId(42);
            var card = new CardInstanceData { Id = firstId, DefinitionId = 7 };

            var clone = card.Clone();

            Assert.That(firstId, Is.EqualTo("card-00000042"));
            Assert.That(secondId, Is.EqualTo(firstId));
            Assert.That(clone.Id, Is.EqualTo(card.Id));
            Assert.That(clone.DefinitionId, Is.EqualTo(card.DefinitionId));
        }

        [Test]
        public void MoveId_IsStableAcrossCreationAndClone()
        {
            string moveId = MoveData.CreateId(12);
            var move = new MoveData { Id = moveId, CardId = CardInstanceData.CreateId(7) };

            var clone = move.Clone();

            Assert.That(moveId, Is.EqualTo("move-00000012"));
            Assert.That(clone.Id, Is.EqualTo(move.Id));
            Assert.That(clone.CardId, Is.EqualTo(move.CardId));
        }

        [Test]
        public void GameStateClone_RoundTripsSerializableStateWithoutSharedCollections()
        {
            var state = new GameStateData
            {
                MatchId = "match-1",
                Revision = 12,
                TurnNumber = 4,
                CurrentPlayerId = 2,
                TurnPhase = ETurnPhase.OpponentPlaying,
                MovesRemaining = 1,
                NextCardInstanceId = 9,
                Random = new RandomStateData { State = 123u, Cursor = 6 },
                Rules = new RulesConfigData { TotalPointsRequiredToWin = 75 }
            };
            var player = new PlayerStateData { Id = 2, Name = "Player Two" };
            player.Hand.Add(new CardInstanceData { Id = CardInstanceData.CreateId(8), DefinitionId = 3 });
            var fireCard = CreateCard();
            fireCard.Points[2] = 4;
            player.Zones.Add(CreateZone(EElementType.Fire, fireCard));
            state.Players.Add(player);
            state.Deck.Add(new CardInstanceData { Id = CardInstanceData.CreateId(9), DefinitionId = 5 });
            state.Moves.Add(new MoveData
            {
                Id = "move-12",
                PlayerId = 2,
                Type = EMoveType.PlayCard,
                CardId = player.Hand[0].Id,
                TargetZoneId = player.Zones[0].Id,
                ExpectedRevision = 12
            });

            var clone = state.Clone();

            Assert.That(clone.MatchId, Is.EqualTo(state.MatchId));
            Assert.That(clone.Revision, Is.EqualTo(state.Revision));
            Assert.That(clone.Random.State, Is.EqualTo(state.Random.State));
            Assert.That(clone.Random.Cursor, Is.EqualTo(state.Random.Cursor));
            Assert.That(clone.Rules.TotalPointsRequiredToWin, Is.EqualTo(75));
            Assert.That(clone.Players[0].Hand[0].Id, Is.EqualTo(state.Players[0].Hand[0].Id));
            Assert.That(clone.Moves[0].CardId, Is.EqualTo(state.Moves[0].CardId));
            Assert.That(clone.Players, Is.Not.SameAs(state.Players));
            Assert.That(clone.Players[0].Zones[0].Cards, Is.Not.SameAs(state.Players[0].Zones[0].Cards));

            clone.Players[0].Zones[0].Cards[0].Points[2] = 99;

            Assert.That(state.Players[0].Zones[0].Cards[0].Points[2], Is.EqualTo(4));
        }

        [Test]
        public void ResolveBomb_ShieldAbsorbsBombAndStopsFurtherRemoval()
        {
            var lowerCard = CreateCard(points: 2);
            lowerCard.Id = "lower";
            var shield = CreateCard(shield: 2);
            shield.Id = "shield";
            var zone = CreateZone(EElementType.Air, lowerCard, shield);
            var bomb = CreateCard(bomb: 3);

            var result = GameRules.ResolveBomb(zone, bomb, new RulesConfigData());

            Assert.That(result.Cards.Count, Is.EqualTo(1));
            Assert.That(result.Cards[0].Id, Is.EqualTo("lower"));
            Assert.That(zone.Cards.Count, Is.EqualTo(2));
        }

        [Test]
        public void ResolveBomb_StrongerShieldPersists()
        {
            var shield = CreateCard(shield: 4);
            var zone = CreateZone(EElementType.Air, shield);
            var bomb = CreateCard(bomb: 3);

            var result = GameRules.ResolveBomb(zone, bomb, new RulesConfigData());

            Assert.That(result.Cards.Count, Is.EqualTo(1));
        }

        [Test]
        public void ResolveLightning_RemovesTopCardsWithoutShieldBlocking()
        {
            var bottom = CreateCard(points: 1);
            bottom.Id = "bottom";
            var shield = CreateCard(shield: 5);
            shield.Id = "shield";
            var top = CreateCard(points: 2);
            top.Id = "top";
            var zone = CreateZone(EElementType.Air, bottom, shield, top);
            var lightning = new CardInstanceData { WildLightning = 2 };

            var result = GameRules.ResolveLightning(zone, lightning, new RulesConfigData());

            Assert.That(result.Cards.Count, Is.EqualTo(1));
            Assert.That(result.Cards[0].Id, Is.EqualTo("bottom"));
        }

        [Test]
        public void FreezeAndMelt_RestoreActivationAtInclusiveThreshold()
        {
            var rules = new RulesConfigData
            {
                ElementZonePointsRequiredToActivate = 10,
                FrozenZoneHealth = 1,
                FrozenZonesMeltAmountPerTurn = 1
            };
            var zone = CreateZone(EElementType.Air, CreateCard(points: 10));
            zone.Points = 10;
            zone.IsActivated = true;

            var frozen = GameRules.ResolveFreeze(zone, rules);
            var melted = GameRules.ResolveMelt(frozen, rules);

            Assert.That(frozen.IsActivated, Is.False);
            Assert.That(frozen.FrozenHealth, Is.EqualTo(1));
            Assert.That(melted.FrozenHealth, Is.Zero);
            Assert.That(melted.IsActivated, Is.True);
        }

        [Test]
        public void ResolveHeal_RestoresConfiguredZoneHealth()
        {
            var zone = new ZoneStateData
            {
                Health = 2,
                MaxHealth = 10,
                IsDestroyed = false
            };
            var rules = new RulesConfigData { ElementZoneStartingHealth = 12 };

            var result = GameRules.ResolveHeal(zone, rules);

            Assert.That(result.Health, Is.EqualTo(12));
            Assert.That(result.MaxHealth, Is.EqualTo(12));
            Assert.That(zone.Health, Is.EqualTo(2));
        }

        [Test]
        public void PowerCardLegality_RequiresMatchingPowerCardType()
        {
            var rules = new RulesConfigData();
            var zone = CreateZone(EElementType.Air);
            zone.Health = 5;
            var heal = new CardInstanceData { IsPowerCard = true, WildZoneHeal = 1 };
            var freeze = new CardInstanceData { IsPowerCard = true, WildFreeze = 1 };
            var lightning = new CardInstanceData { IsPowerCard = true, WildLightning = 1 };

            Assert.That(GameRules.CanHealZone(zone, heal, rules, true), Is.True);
            Assert.That(GameRules.CanFreezeZone(zone, freeze, rules), Is.True);
            Assert.That(GameRules.CanLightningZone(zone, lightning, rules, false), Is.True);

            heal.IsPowerCard = false;
            freeze.IsPowerCard = false;
            lightning.IsPowerCard = false;

            Assert.That(GameRules.CanHealZone(zone, heal, rules, true), Is.False);
            Assert.That(GameRules.CanFreezeZone(zone, freeze, rules), Is.False);
            Assert.That(GameRules.CanLightningZone(zone, lightning, rules, false), Is.False);
        }

        private static ZoneStateData CreateZone(EElementType inElementType, params CardInstanceData[] inCards)
        {
            var zone = new ZoneStateData
            {
                Id = (int)inElementType,
                ElementType = inElementType,
                Health = 10,
                MaxHealth = 10
            };

            foreach (var card in inCards)
                zone.Cards.Add(card);

            zone.Points = GameRules.CalculateZonePoints(zone);
            zone.FaceUpPoints = GameRules.CalculateZonePoints(zone, true);

            return zone;
        }

        private static CardInstanceData CreateCard(int points = 0, int multiplier = 0, int bomb = 0, int shield = 0)
        {
            var card = new CardInstanceData { IsFaceUp = true };
            card.Points[0] = points;
            card.Multipliers[0] = multiplier;
            card.Bombs[0] = bomb;
            card.Shields[0] = shield;

            return card;
        }
    }
}
