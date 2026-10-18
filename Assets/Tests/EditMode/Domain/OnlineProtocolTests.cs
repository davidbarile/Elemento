using System;
using System.Collections.Generic;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class OnlineProtocolTests
    {
        [Test]
        public void MatchFactory_SameInputsProduceIdenticalState()
        {
            var decks = CreateDeckDefinitionsBySeat();
            var rules = CreateFastRules();
            var factory = new MatchFactory();

            var first = factory.Create(419, rules, decks);
            var second = factory.Create(419, rules, decks);

            Assert.That(NetJson.Serialize(second), Is.EqualTo(NetJson.Serialize(first)));
        }

        [TestCase(0, ETurnPhase.PlayerPlaying)]
        [TestCase(1, ETurnPhase.OpponentPlaying)]
        public void MatchFactory_StartingSeatReceivesOpeningHandAndStartsRound(int inStartingSeat,
            ETurnPhase inExpectedPhase)
        {
            var rules = CreateFastRules();
            var state = new MatchFactory().Create(420, rules, CreateDeckDefinitionsBySeat(), null,
                inStartingSeat);
            var simulator = new GameSimulator();

            Assert.That(state.StartingPlayerId, Is.EqualTo(inStartingSeat));
            Assert.That(state.CurrentPlayerId, Is.EqualTo(inStartingSeat));
            Assert.That(state.TurnNumber, Is.EqualTo(1));
            Assert.That(state.TurnPhase, Is.EqualTo(inExpectedPhase));
            Assert.That(state.Players[inStartingSeat].Hand.Count, Is.EqualTo(rules.NumCardsToDeal));
            Assert.That(state.Players[1 - inStartingSeat].Hand, Is.Empty);
            Assert.That(FairView.BuildSeatView(state, 0).View.StartingPlayerId, Is.EqualTo(inStartingSeat));

            simulator.EndTurn(state);
            Assert.That(state.TurnNumber, Is.EqualTo(1));
            simulator.EndTurn(state);
            Assert.That(state.TurnNumber, Is.EqualTo(2));
            Assert.That(state.CurrentPlayerId, Is.EqualTo(inStartingSeat));
        }

        [Test]
        public void FairView_ContainsOwnHandButNoOpponentOrDeckCardIds()
        {
            var state = new MatchFactory().Create(101, CreateFastRules(), CreateDeckDefinitionsBySeat());
            new GameSimulator().EndTurn(state);
            var seatView = FairView.BuildSeatView(state, 0);
            string json = NetJson.Serialize(seatView);

            foreach (var card in state.Players[0].Hand)
                Assert.That(json, Does.Contain(card.Id));
            foreach (var card in state.Players[1].Hand)
                Assert.That(json, Does.Not.Contain(card.Id));
            foreach (var card in state.Deck)
                Assert.That(json, Does.Not.Contain(card.Id));

            Assert.That(seatView.View.Player.Hand.Count, Is.EqualTo(state.Players[0].Hand.Count));
            Assert.That(seatView.View.Opponents[0].HandCount, Is.EqualTo(state.Players[1].Hand.Count));
            Assert.That(typeof(MatchSeatViewData).GetField("Deck"), Is.Null);
            Assert.That(typeof(PlayerViewData).GetField("Deck"), Is.Null);
        }

        [Test]
        public void FairView_NeverRevealsSeedOrFaceDownCards()
        {
            const int seed = 918273;
            var state = new MatchFactory().Create(seed, CreateFastRules(), CreateDeckDefinitionsBySeat(), "m-1");
            string viewJson = NetJson.Serialize(FairView.BuildSeatView(state, 0));
            string snapshotJson = NetJson.Serialize(FairView.BuildSnapshot(state));

            Assert.That(typeof(MatchSeatViewData).GetField("Seed"), Is.Null);
            Assert.That(typeof(MatchSnapshotData).GetField("Seed"), Is.Null);
            Assert.That(viewJson, Does.Not.Contain(seed.ToString()));
            Assert.That(snapshotJson, Does.Not.Contain(seed.ToString()));
            Assert.That(state.MatchId, Is.EqualTo("m-1"));

            var hidden = new CardInstanceData { Id = "card-9", DefinitionId = 7, DefinitionName = "Fire 3", IsFaceUp = false };
            var masked = FairView.MaskCard(hidden);
            Assert.That(masked.Id, Is.EqualTo("card-9"));
            Assert.That(masked.DefinitionId, Is.EqualTo(0));
            Assert.That(masked.DefinitionName, Is.Null);
            Assert.That(FairView.MaskCard(new CardInstanceData { Id = "c", DefinitionId = 3, IsFaceUp = true }).DefinitionId, Is.EqualTo(3));
        }

        private static RulesConfigData CreateFastRules()
        {
            return new RulesConfigData
            {
                NumCardsToDeal = 3,
                MovesPerTurn = 3,
                TotalPointsRequiredToWin = 10,
                ElementZonePointsRequiredToActivate = 100
            };
        }

        internal static List<IList<CardDefinitionData>> CreateDeckDefinitionsBySeat()
        {
            List<IList<CardDefinitionData>> decks = new()
            {
                new List<CardDefinitionData>(),
                new List<CardDefinitionData>()
            };
            int cardIndex = 0;
            foreach (var definition in DeckDefinitionData.NormalCards)
            {
                for (int copy = 0; copy < definition.ChoiceCount; ++copy)
                {
                    decks[cardIndex % decks.Count].Add(definition);
                    ++cardIndex;
                }
            }
            return decks;
        }

    }
}
