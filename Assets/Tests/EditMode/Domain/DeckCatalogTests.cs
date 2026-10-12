using System.Linq;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class DeckCatalogTests
    {
        [Test]
        public void Catalog_PreservesAllDefinitionsAndDeckConfigCounts()
        {
            var definitions = DeckDefinitionData.NormalCards;
            Assert.That(definitions.Count, Is.EqualTo(455));
            Assert.That(definitions.Select(card => card.DefinitionId), Is.EqualTo(Enumerable.Range(0, 455)));
            Assert.That(definitions.Sum(card => card.ConfigCount), Is.EqualTo(890));
            Assert.That(definitions.Sum(card => card.ChoiceCount), Is.EqualTo(555));
            Assert.That(definitions.Sum(card => card.ChanceCount), Is.EqualTo(890));
            Assert.That(definitions.Count(card => card.ChoiceCount > 0), Is.EqualTo(302));
            Assert.That(definitions.All(card => !card.IsPowerCard), Is.True);
        }

        [Test]
        public void ChoiceDeck_ExpandsEachExactCountInDefinitionOrder()
        {
            var cards = DeckDefinitionData.CreateChoiceDeck();
            Assert.That(cards.Count, Is.EqualTo(555));
            foreach (var definition in DeckDefinitionData.NormalCards)
                Assert.That(cards.Count(card => card.DefinitionId == definition.DefinitionId),
                    Is.EqualTo(definition.ChoiceCount), definition.Name);
            Assert.That(cards.Select(card => card.DefinitionId), Is.Ordered);
        }

        [Test]
        public void Factory_ProducesIndependentCardsAndValueArrays()
        {
            var original = DeckDefinitionData.NormalCards[0];
            var firstDeck = DeckDefinitionData.CreateChoiceDeck();
            var secondDeck = DeckDefinitionData.CreateChoiceDeck();
            firstDeck[0].Points[0] = 999;
            firstDeck[0].Multipliers[0] = 999;
            firstDeck[0].Bombs[0] = 999;
            firstDeck[0].Shields[0] = 999;
            Assert.That(firstDeck[1].Points, Is.EqualTo(original.Points));
            Assert.That(secondDeck[0].Points, Is.EqualTo(original.Points));
            Assert.That(secondDeck[0].Multipliers, Is.EqualTo(original.Multipliers));
            Assert.That(secondDeck[0].Bombs, Is.EqualTo(original.Bombs));
            Assert.That(secondDeck[0].Shields, Is.EqualTo(original.Shields));
            Assert.That(original.Points[0], Is.EqualTo(1));
            Assert.That(original.CreateCard("sample").Id, Is.EqualTo("sample"));
        }

        [Test]
        public void PowerDeck_UsesSerializedCountsValuesAndExpandedIndices()
        {
            var cards = DeckDefinitionData.CreatePowerDeck();
            Assert.That(DeckDefinitionData.PowerCards.Count, Is.EqualTo(3));
            Assert.That(cards.Count, Is.EqualTo(37));
            Assert.That(cards.Select(card => card.DefinitionId), Is.EqualTo(Enumerable.Range(0, 37)));
            Assert.That(cards.Count(card => card.WildZoneHeal == 10), Is.EqualTo(10));
            Assert.That(cards.Count(card => card.WildFreeze == 5), Is.EqualTo(12));
            Assert.That(cards.Count(card => card.WildLightning == 5), Is.EqualTo(15));
            Assert.That(cards.All(card => card.IsPowerCard), Is.True);
            Assert.That(DeckDefinitionData.CreatePowerCard(EPowerCardType.Heal).WildZoneHeal, Is.EqualTo(10));
            Assert.That(DeckDefinitionData.CreatePowerCard(EPowerCardType.Freeze).WildFreeze, Is.EqualTo(5));
            Assert.That(DeckDefinitionData.CreatePowerCard(EPowerCardType.Lightning).WildLightning, Is.EqualTo(5));
        }
    }
}
