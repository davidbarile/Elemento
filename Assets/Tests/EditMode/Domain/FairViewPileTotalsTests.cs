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

        [Test]
        public void SeatViewsAndSnapshot_PreserveFrozenHealth()
        {
            var state = new MatchFactory().Create(78, new RulesConfigData(),
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "frozen-view");
            state.Players[1].Zones[0].FrozenHealth = 4;

            var opponentView = FairView.BuildSeatView(state, 0);
            var ownView = FairView.BuildSeatView(state, 1);
            var snapshot = FairView.BuildSnapshot(state);
            var roundTrip = NetJson.Deserialize<MatchSeatViewData>(NetJson.Serialize(opponentView));

            Assert.That(opponentView.View.Opponents[0].Zones[0].FrozenHealth, Is.EqualTo(4));
            Assert.That(ownView.View.Player.Zones[0].FrozenHealth, Is.EqualTo(4));
            Assert.That(snapshot.Seats[1].Zones[0].FrozenHealth, Is.EqualTo(4));
            Assert.That(roundTrip.View.Opponents[0].Zones[0].FrozenHealth, Is.EqualTo(4));
        }
    }
}
