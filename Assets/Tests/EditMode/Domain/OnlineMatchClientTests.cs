using System.Collections.Generic;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class OnlineMatchClientTests
    {
        [Test]
        public void TwoClients_StayInSyncThroughInMemoryTransport()
        {
            var transport = CreateTransport(out _);
            var seat0 = new OnlineMatchClient(transport, transport.MatchId, 0);
            var seat1 = new OnlineMatchClient(transport, transport.MatchId, 1);
            seat0.Refresh();
            seat1.Refresh();
            var simulator = new GameSimulator();

            for (int i = 0; i < 30; i++)
            {
                var active = seat0.IsLocalTurn ? seat0 : seat1;
                var waiting = active == seat0 ? seat1 : seat0;
                Assert.That(waiting.IsLocalTurn, Is.False);

                var move = active.CurrentView.View.LegalMoves[0];
                var result = active.SubmitMove(move.Clone());

                Assert.That(result.Accepted, Is.True, result.Reason);
                Assert.That(seat0.Revision, Is.EqualTo(transport.Revision));
                Assert.That(seat1.Revision, Is.EqualTo(transport.Revision));
                if (seat0.CurrentView.View.WinnerPlayerId >= 0)
                    break;
            }
        }

        [Test]
        public void StaleOrDuplicateSubmit_IsRejectedOrIdempotent()
        {
            var transport = CreateTransport(out _);
            var client = new OnlineMatchClient(transport, transport.MatchId, 0);
            var view = client.Refresh();
            var move = view.View.LegalMoves[0];

            var command = new MoveCommandData { MatchId = transport.MatchId, Seat = 0, ExpectedRevision = view.View.Revision, Move = move.Clone(), ClientNonce = "n1" };
            var first = transport.Submit(command);
            var retry = transport.Submit(command);
            Assert.That(first.Accepted, Is.True, first.Reason);
            Assert.That(retry, Is.SameAs(first));

            var stale = new MoveCommandData { MatchId = transport.MatchId, Seat = 0, ExpectedRevision = view.View.Revision, Move = move.Clone(), ClientNonce = "n2" };
            Assert.That(transport.Submit(stale).Accepted, Is.False);
        }

        [Test]
        public void AsyncResume_FetchesLatestViewWithoutLiveEvents()
        {
            var transport = CreateTransport(out _);
            var mover = new OnlineMatchClient(transport, transport.MatchId, 0);
            mover.Refresh();
            mover.SubmitMove(mover.CurrentView.View.LegalMoves[0].Clone());

            //a client created later (app reopened) sees the committed revision
            var resumed = new OnlineMatchClient(transport, transport.MatchId, 1);
            Assert.That(resumed.Refresh().View.Revision, Is.EqualTo(transport.Revision));
        }

        private static InMemoryMatchTransport CreateTransport(out GameStateData outState)
        {
            var decks = OnlineProtocolTests.CreateDeckDefinitionsBySeat();
            outState = new MatchFactory().Create(55, new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 3 }, decks, "test-match");
            return new InMemoryMatchTransport(outState);
        }
    }
}
