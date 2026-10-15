using System.Collections.Generic;
using System.Linq;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class OnlineTurnBufferTests
    {
        [Test]
        public void Buffer_PreservesMoveOrderAndNonceAcrossRetries()
        {
            var state = new MatchFactory().Create(55,
                new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 3 },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var view = FairView.BuildSeatView(state, 0);
            var play = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.PlayCard).Clone();
            var endTurn = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.EndTurn).Clone();
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view, "stable-turn-nonce");
            Assert.That(buffer.TryAppend(play, out string playReason), Is.True, playReason);
            play.CardId = "mutated-after-append";
            Assert.That(buffer.TryAppend(endTurn, out string endReason), Is.True, endReason);

            var first = buffer.CreateRequest("match", 0);
            var retry = buffer.CreateRequest("match", 0);
            Assert.That(first.ClientNonce, Is.EqualTo("stable-turn-nonce"));
            Assert.That(retry.ClientNonce, Is.EqualTo(first.ClientNonce));
            Assert.That(NetJson.Serialize(retry), Is.EqualTo(NetJson.Serialize(first)));
            Assert.That(first.Moves.Select(inMove => inMove.Type),
                Is.EqualTo(new[] { EMoveType.PlayCard, EMoveType.EndTurn }));
            Assert.That(first.Moves[0].CardId, Is.Not.EqualTo("mutated-after-append"));
        }

        [Test]
        public void Buffer_NewTurnGetsFreshNonce()
        {
            var state = new MatchFactory().Create(56,
                new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 3 },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var view = FairView.BuildSeatView(state, 0);
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view);
            string first = buffer.ClientNonce;
            buffer.Clear();
            buffer.Begin(view);

            Assert.That(buffer.ClientNonce, Is.Not.Empty.And.Not.EqualTo(first));
        }

        [Test]
        public void Buffer_PopRemovesMostRecentMoveAndAllowsItAgain()
        {
            var state = new MatchFactory().Create(62,
                new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 3 },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var simulator = new GameSimulator();
            var view = FairView.BuildSeatView(state, 0);
            var first = simulator.GetLegalMoves(state, 0).First(inMove => inMove.Type == EMoveType.PlayCard).Clone();
            simulator.ApplyMove(state, first);
            var second = simulator.GetLegalMoves(state, 0)
                .First(inMove => inMove.Type == EMoveType.PlayCard && inMove.CardId != first.CardId).Clone();
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view);
            Assert.That(buffer.TryAppend(first, out string firstReason), Is.True, firstReason);
            Assert.That(buffer.TryAppend(second, out string secondReason), Is.True, secondReason);

            Assert.That(buffer.TryPop(out MoveData popped, out string popReason), Is.True, popReason);
            Assert.That(popped.CardId, Is.EqualTo(second.CardId));
            Assert.That(buffer.Moves.Select(inMove => inMove.CardId), Is.EqualTo(new[] { first.CardId }));
            Assert.That(buffer.TryAppend(second, out string appendAgainReason), Is.True, appendAgainReason);
            Assert.That(buffer.TryPop(out _, out string secondPopReason), Is.True, secondPopReason);

            var endTurn = simulator.GetLegalMoves(state, 0).First(inMove => inMove.Type == EMoveType.EndTurn);
            Assert.That(buffer.TryAppend(endTurn, out string endReason), Is.True, endReason);
            var request = buffer.CreateRequest("match", 0);
            Assert.That(request.Moves.Select(inMove => inMove.CardId),
                Is.EqualTo(new[] { first.CardId, endTurn.CardId }));
        }

        [Test]
        public void Buffer_AcceptsTerminalMarkerAfterMoveBudgetAutoEndsTurn()
        {
            var state = new MatchFactory().Create(57,
                new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 1 },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var view = FairView.BuildSeatView(state, 0);
            var play = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.PlayCard).Clone();
            var endTurn = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.EndTurn).Clone();
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view);
            Assert.That(buffer.TryAppend(play, out string playReason), Is.True, playReason);
            Assert.That(buffer.TryAppend(endTurn, out string endReason), Is.True, endReason);
            Assert.That(buffer.IsComplete, Is.True);
        }

        [Test]
        public void Buffer_RejectsTerminalMarkerForDifferentSeat()
        {
            var state = new MatchFactory().Create(61,
                new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 1 },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var view = FairView.BuildSeatView(state, 0);
            var play = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.PlayCard).Clone();
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view);
            Assert.That(buffer.TryAppend(play, out string playReason), Is.True, playReason);
            Assert.That(buffer.TryAppend(CreateTerminalMarker(1), out _), Is.False);
        }

        [Test]
        public void Buffer_AcceptsTerminalMarkerAfterDiscardEndsTurn()
        {
            var state = new MatchFactory().Create(58,
                new RulesConfigData
                {
                    NumCardsToDeal = 3,
                    MovesPerTurn = 3,
                    DiscardMode = EDiscardMode.DiscardEndsTurn
                },
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var view = FairView.BuildSeatView(state, 0);
            var discard = view.View.LegalMoves.First(inMove => inMove.Type == EMoveType.DiscardCard).Clone();
            var endTurn = CreateTerminalMarker(0);
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(view);
            Assert.That(buffer.TryAppend(discard, out string discardReason), Is.True, discardReason);
            Assert.That(buffer.TryAppend(endTurn, out string endReason), Is.True, endReason);
            Assert.That(buffer.IsComplete, Is.True);
        }

        [Test]
        public void Buffer_AcceptsTerminalMarkerAfterWinningMove()
        {
            var rules = new RulesConfigData
            {
                NumCardsToDeal = 3,
                MovesPerTurn = 3,
                TotalPointsRequiredToWin = 1
            };
            var state = new MatchFactory().Create(59, rules,
                OnlineProtocolTests.CreateDeckDefinitionsBySeat(), "match");
            var simulator = new GameSimulator();
            MoveData winningMove = null;
            foreach (var candidate in simulator.GetLegalMoves(state, 0).Where(inMove => inMove.Type == EMoveType.PlayCard))
            {
                var projected = state.Clone();
                simulator.ApplyMove(projected, candidate);
                if (projected.WinCondition != EWinCondition.None)
                {
                    winningMove = candidate.Clone();
                    break;
                }
            }
            Assert.That(winningMove, Is.Not.Null, "Fixture must expose a points-winning move.");
            var buffer = new OnlineTurnBuffer();

            buffer.Begin(FairView.BuildSeatView(state, 0));
            Assert.That(buffer.TryAppend(winningMove, out string playReason), Is.True, playReason);
            Assert.That(buffer.TryAppend(CreateTerminalMarker(0), out string endReason), Is.True, endReason);
            Assert.That(buffer.IsComplete, Is.True);
        }

        private static MoveData CreateTerminalMarker(int inPlayerId)
        {
            return new MoveData
            {
                PlayerId = inPlayerId,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            };
        }
    }

    public class OnlineReconciliationTests
    {
        [Test]
        public void BuildLocalMoveLog_CreatesReplayableSequentialEntriesWithoutAliasingMoves()
        {
            var first = new MoveData { PlayerId = 1, Type = EMoveType.PlayCard, CardId = "card" };
            var second = new MoveData { PlayerId = 1, Type = EMoveType.EndTurn };

            var entries = OnlineReconciliation.BuildLocalMoveLog("match", 1, "nonce", 7,
                new[] { first, second });
            first.CardId = "mutated";

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].RevisionBefore, Is.EqualTo(7));
            Assert.That(entries[0].RevisionAfter, Is.EqualTo(8));
            Assert.That(entries[0].Move.ExpectedRevision, Is.EqualTo(7));
            Assert.That(entries[0].Move.CardId, Is.EqualTo("card"));
            Assert.That(entries[1].RevisionBefore, Is.EqualTo(8));
            Assert.That(entries[1].RevisionAfter, Is.EqualTo(9));
            Assert.That(entries[1].Move.ExpectedRevision, Is.EqualTo(8));
        }

        [Test]
        public void FindNewHandCards_ReturnsOnlyAddedCardsInAuthoritativeOrder()
        {
            var view = CreateView(3);
            view.View.Player.Hand.Add(new CardInstanceData { Id = "existing" });
            view.View.Player.Hand.Add(new CardInstanceData { Id = "new-1" });
            view.View.Player.Hand.Add(new CardInstanceData { Id = "new-2" });

            var cards = OnlineReconciliation.FindNewHandCards(new[] { "existing" }, view.View);

            Assert.That(cards, Has.Count.EqualTo(2));
            Assert.That(cards[0].Id, Is.EqualTo("new-1"));
            Assert.That(cards[1].Id, Is.EqualTo("new-2"));
        }

        [Test]
        public void MatchingViewAndContinuousLogDoNotRequireRebuild()
        {
            var view = CreateView(3);
            var update = new OnlineViewUpdateData { View = view };
            update.Moves.Add(CreateLogEntry(2, 3));

            Assert.That(OnlineReconciliation.RequiresRebuild(2, update, view), Is.False);
        }

        [Test]
        public void MissingProjectionOrDiscontinuousLogRequiresRebuild()
        {
            var view = CreateView(4);
            var update = new OnlineViewUpdateData { View = view };
            update.Moves.Add(CreateLogEntry(2, 4));

            Assert.That(OnlineReconciliation.RequiresRebuild(2, update, view), Is.True);
            update.Moves.Clear();
            Assert.That(OnlineReconciliation.RequiresRebuild(4, update, null), Is.True);
        }

        private static MatchSeatViewData CreateView(int inRevision)
        {
            return new MatchSeatViewData
            {
                MatchId = "match",
                Seat = 1,
                View = new PlayerViewData
                {
                    Revision = inRevision,
                    CurrentPlayerId = 1,
                    Rules = new RulesConfigData(),
                    Player = new PlayerStateData { Id = 1 }
                }
            };
        }

        private static MoveLogEntryData CreateLogEntry(int inBefore, int inAfter)
        {
            return new MoveLogEntryData
            {
                MatchId = "match",
                Seat = 0,
                RevisionBefore = inBefore,
                RevisionAfter = inAfter,
                Move = new MoveData { PlayerId = 0, Type = EMoveType.EndTurn }
            };
        }
    }

    public class OnlineTerminalMarkerReplayTests
    {
        [Test]
        public void Replay_AcceptsLoggedTerminalMarkerAfterAutomaticTurnAdvance()
        {
            const int seed = 60;
            var rules = new RulesConfigData { NumCardsToDeal = 3, MovesPerTurn = 1 };
            var decks = OnlineProtocolTests.CreateDeckDefinitionsBySeat();
            var state = new MatchFactory().Create(seed, rules, decks);
            var authority = new MatchAuthority();
            var play = new GameSimulator().GetLegalMoves(state, 0)
                .First(inMove => inMove.Type == EMoveType.PlayCard);
            var result = authority.ValidateAndApply(state, new MoveCommandData
            {
                MatchId = state.MatchId,
                Seat = 0,
                ClientMoveSequence = 1,
                ExpectedRevision = state.Revision,
                Move = play,
                ClientNonce = "terminal-marker"
            });
            Assert.That(result.Accepted, Is.True, result.Reason);

            var markerMove = new MoveData
            {
                Id = MoveData.CreateId(state.Moves.Count + 1),
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1,
                ExpectedRevision = state.Revision
            };
            var marker = new MoveLogEntryData
            {
                MatchId = state.MatchId,
                Seat = 0,
                ClientMoveSequence = 2,
                ClientNonce = "terminal-marker",
                RevisionBefore = state.Revision,
                RevisionAfter = state.Revision + 1,
                Move = markerMove
            };
            var log = new List<MoveLogEntryData>(authority.MoveLog) { marker };
            state.Moves.Add(markerMove.Clone());
            ++state.Revision;

            var replayed = new MatchAuthority().Replay(seed, rules, decks, log);

            Assert.That(NetJson.Serialize(replayed), Is.EqualTo(NetJson.Serialize(state)));
        }
    }
}
