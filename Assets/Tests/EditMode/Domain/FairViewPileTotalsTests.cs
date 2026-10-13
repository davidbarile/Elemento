using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class FairViewPileTotalsTests
    {
        //Design decision (David): pile point totals, including face-down cards, are public to both players.
        [Test]
        public void OpponentView_IncludesFullPileTotalsButMasksFaceDownCards()
        {
            var state = new MatchFactory().Create(77, new RulesConfigData(), OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "totals");
            var zone = state.Players[1].Zones[0];
            zone.Cards.Add(new CardInstanceData { Id = "hidden-1", DefinitionId = 5, DefinitionName = "Hidden", IsFaceUp = false });
            zone.Points = 42;
            zone.FaceUpPoints = 10;

            var opponentZone = FairView.BuildSeatView(state, 0).View.Opponents[0].Zones[0];
            var snapshotZone = FairView.BuildSnapshot(state).Seats[1].Zones[0];

            Assert.That(opponentZone.Points, Is.EqualTo(42));
            Assert.That(opponentZone.FaceUpPoints, Is.EqualTo(10));
            Assert.That(snapshotZone.Points, Is.EqualTo(42));
            var masked = opponentZone.Cards[opponentZone.Cards.Count - 1];
            Assert.That(masked.Id, Is.EqualTo("hidden-1"));
            Assert.That(masked.DefinitionName, Is.Null);
        }
    }
}
