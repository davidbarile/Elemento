using System.Collections.Generic;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class UndoCardConservationTests
    {
        [Test]
        public void MultipleUndos_AfterHandMoveBombAndPowerCard_PreserveEveryCardOnce()
        {
            var simulator = new GameSimulator();
            var state = simulator.CreateGame(72, new RulesConfigData
            {
                NumCardsToDeal = 0,
                TotalPointsRequiredToWin = 10000
            });
            var player = state.Players[0];
            var opponent = state.Players[1];
            player.Hand.Add(CreateCard("hand-move", points: 1));
            player.Hand.Add(CreateCard("bomb", bomb: 1));
            var heal = DeckDefinitionData.CreatePowerCard(EPowerCardType.Heal);
            heal.Id = "heal";
            player.Hand.Add(heal);
            opponent.Zones[0].Cards.Add(CreateCard("bomb-target", points: 1));
            player.Zones[1].Health = 0;
            player.Zones[1].IsDestroyed = true;

            var history = new TurnUndoHistory<GameStateData>();
            history.BeginTurn(ETurnUndoCheckpoint.Draw);
            List<CardIdentityState> expectedUndos = new();

            RecordAndApply(history, expectedUndos, simulator, ref state,
                FindPlay(simulator, state, "hand-move", 0, 0));
            RecordAndApply(history, expectedUndos, simulator, ref state,
                FindPlay(simulator, state, "bomb", 1, 0));
            RecordAndApply(history, expectedUndos, simulator, ref state,
                FindPlay(simulator, state, "heal", 0, 1));

            int totalCardCount = expectedUndos[0].Ids.Count + expectedUndos[0].AnonymousCount;
            for (int i = expectedUndos.Count - 1; i >= 0; --i)
            {
                Assert.That(history.TryPop(out GameStateData snapshot, out _), Is.True);
                state = snapshot.Clone();
                AssertIdentity(expectedUndos[i], state);
                CardIdentityState restored = CaptureIdentity(state);
                Assert.That(restored.Ids.Count + restored.AnonymousCount, Is.EqualTo(totalCardCount));
            }

            Assert.That(history.TryPop(out _, out _), Is.False);
        }

        [Test]
        public void Validator_RejectsDuplicateCardViewsEvenWhenTotalCountMatches()
        {
            Assert.That(CardIdentityValidator.TryValidate(new[] { "a", "b" }, 1,
                new[] { "a", "a" }, 1, out string error), Is.False);
            Assert.That(error, Does.Contain("duplicate card id 'a'"));
        }

        private static void RecordAndApply(TurnUndoHistory<GameStateData> inHistory,
            List<CardIdentityState> inExpectedUndos, GameSimulator inSimulator,
            ref GameStateData inState, MoveData inMove)
        {
            inHistory.Record(inState.Clone());
            inExpectedUndos.Add(CaptureIdentity(inState));
            inSimulator.ApplyMove(inState, inMove);
        }

        private static MoveData FindPlay(GameSimulator inSimulator, GameStateData inState,
            string inCardId, int inTargetPlayerId, int inTargetZoneId)
        {
            foreach (var move in inSimulator.GetLegalMoves(inState, inState.CurrentPlayerId))
            {
                if (move.Type == EMoveType.PlayCard && move.CardId == inCardId
                    && move.TargetPlayerId == inTargetPlayerId && move.TargetZoneId == inTargetZoneId)
                {
                    return move;
                }
            }

            Assert.Fail("Expected legal play for " + inCardId + ".");
            return null;
        }

        private static CardInstanceData CreateCard(string inId, int points = 0, int bomb = 0)
        {
            return new CardInstanceData
            {
                Id = inId,
                DefinitionName = inId,
                IsFaceUp = true,
                Points = new[] { points, 0, 0, 0 },
                Bombs = new[] { bomb, 0, 0, 0 }
            };
        }

        private static void AssertIdentity(CardIdentityState inExpected, GameStateData inState)
        {
            CardIdentityState actual = CaptureIdentity(inState);
            Assert.That(CardIdentityValidator.TryValidate(inExpected.Ids, inExpected.AnonymousCount,
                actual.Ids, actual.AnonymousCount, out string error), Is.True, error);
        }

        private static CardIdentityState CaptureIdentity(GameStateData inState)
        {
            var result = new CardIdentityState();
            AddCards(result, inState.Deck);
            AddCards(result, inState.ReserveDeck);
            AddCards(result, inState.PowerDeck);
            AddCards(result, inState.DiscardPile);
            foreach (var player in inState.Players)
            {
                AddCards(result, player.Hand);
                foreach (var zone in player.Zones)
                    AddCards(result, zone.Cards);
            }
            return result;
        }

        private static void AddCards(CardIdentityState inState, IEnumerable<CardInstanceData> inCards)
        {
            foreach (var card in inCards)
            {
                if (string.IsNullOrEmpty(card.Id))
                    ++inState.AnonymousCount;
                else
                    inState.Ids.Add(card.Id);
            }
        }

        private sealed class CardIdentityState
        {
            public readonly List<string> Ids = new();
            public int AnonymousCount;
        }
    }
}
