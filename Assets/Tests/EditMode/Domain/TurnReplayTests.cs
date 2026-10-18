using System.Linq;
using Elemento.Domain;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class TurnReplayTests
    {
        [Test]
        public void EndTurn_ReplaysDrawsWithoutUsingReceiverRandomness()
        {
            var initial = InitialMatchFactory.Create(inSeed: 17).InitialState;
            var recorder = new TurnRecorder(initial.Clone());
            var turn = recorder.EndTurn();
            initial.Random.State = 1;
            initial.Random.Cursor = 999;
            initial.ReserveDeck.Reverse();
            var result = new TurnReplayer().Replay(initial, RoundTrip(turn));
            Assert.That(result.IsMatch, Is.True, result.Reason);
            Assert.That(GameStateHasher.CanonicalJson(result.State), Is.EqualTo(GameStateHasher.CanonicalJson(turn.FinalState)));
            Assert.That(turn.Actions.Single().DrawnCards.Count, Is.GreaterThan(0));
        }

        [Test]
        public void Undo_RestoresCheckpointAndKeepsOrderedUndoRow()
        {
            var initial = InitialMatchFactory.Create(new RulesConfigData { AllowUndo = true }, 51).InitialState;
            var recorder = new TurnRecorder(initial.Clone());
            var move = new GameSimulator().GetLegalMoves(recorder.State, recorder.State.CurrentPlayerId)
                .First(inMove => inMove.Type == EMoveType.DiscardCard);
            recorder.ApplyMove(move);
            Assert.That(recorder.Undo(), Is.True);
            Assert.That(GameStateHasher.Compute(recorder.State), Is.EqualTo(GameStateHasher.Compute(initial)));
            var turn = recorder.EndTurn();
            Assert.That(turn.Actions[1].Kind, Is.EqualTo("Undo"));
            Assert.That(new TurnReplayer().Replay(initial, RoundTrip(turn)).IsMatch, Is.True);
            Assert.That(recorder.Undo(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Activation_ReplaysExactPowerCardIncludingRebuiltDeck(bool inSelect)
        {
            var initial = InitialMatchFactory.Create(new RulesConfigData
                { AllowSelectPowerCardType = inSelect, AllowUndo = true }, 81).InitialState;
            initial.Players[initial.CurrentPlayerId].Zones[0].IsActivated = true;
            initial.PowerDeck.Clear();
            var recorder = new TurnRecorder(initial.Clone());
            var activation = new GameSimulator().GetLegalMoves(recorder.State, recorder.State.CurrentPlayerId)
                .First(inMove => inMove.Type == EMoveType.ActivateZone);
            recorder.ApplyMove(activation);
            Assert.That(recorder.Actions[0].PowerCard, Is.Not.Null);
            Assert.That(recorder.Actions[0].PowerCard.Id, Is.Not.Null.And.Not.Empty);
            var turn = recorder.EndTurn();
            Assert.That(new TurnReplayer().Replay(initial, RoundTrip(turn)).IsMatch, Is.True);
        }

        [Test]
        public void Replay_DoesNotRefuseActionDueToMoveRevision()
        {
            var initial = InitialMatchFactory.Create(inSeed: 42).InitialState;
            var turn = new TurnRecorder(initial.Clone()).EndTurn();
            turn.Actions[0].Move.ExpectedRevision = -999;
            Assert.That(new TurnReplayer().Replay(initial, turn).IsMatch, Is.True);
        }

        [Test]
        public void Mismatch_ReturnsCommittedStateWithoutMutatingInput()
        {
            var initial = InitialMatchFactory.Create(inSeed: 42).InitialState;
            string before = GameStateHasher.Compute(initial);
            var turn = new TurnRecorder(initial.Clone()).EndTurn();
            turn.Actions[0].DrawnCards[0].WildPoints += 1;
            var result = new TurnReplayer().Replay(initial, turn);
            Assert.That(result.IsMatch, Is.False);
            Assert.That(result.Reason, Is.EqualTo("state_hash_mismatch"));
            Assert.That(GameStateHasher.Compute(result.State), Is.EqualTo(turn.FinalStateHash));
            Assert.That(GameStateHasher.Compute(initial), Is.EqualTo(before));
        }

        [Test]
        public void Replay_DetectsCorruptedOpaqueReservoirAndCardSequence()
        {
            var initial = InitialMatchFactory.Create(inSeed: 42).InitialState;
            var reservoirTurn = new TurnRecorder(initial.Clone()).EndTurn();
            reservoirTurn.FinalState.ReserveDeck.RemoveAt(0);
            reservoirTurn.FinalStateHash = GameStateHasher.Compute(reservoirTurn.FinalState);
            var reservoirResult = new TurnReplayer().Replay(initial, reservoirTurn);
            Assert.That(reservoirResult.IsMatch, Is.False);
            Assert.That(reservoirResult.Reason, Is.EqualTo("normal_reservoir_mismatch"));

            var sequenceTurn = new TurnRecorder(initial.Clone()).EndTurn();
            sequenceTurn.FinalState.NextCardInstanceId = 0;
            sequenceTurn.FinalStateHash = GameStateHasher.Compute(sequenceTurn.FinalState);
            var sequenceResult = new TurnReplayer().Replay(initial, sequenceTurn);
            Assert.That(sequenceResult.IsMatch, Is.False);
            Assert.That(sequenceResult.Reason, Is.EqualTo("next_card_id_regressed"));

            var identityTurn = new TurnRecorder(initial.Clone()).EndTurn();
            identityTurn.FinalState.Players.SelectMany(inPlayer => inPlayer.Hand).First().Id = null;
            identityTurn.FinalStateHash = GameStateHasher.Compute(identityTurn.FinalState);
            var identityResult = new TurnReplayer().Replay(initial, identityTurn);
            Assert.That(identityResult.IsMatch, Is.False);
            Assert.That(identityResult.Reason, Is.EqualTo("live_card_id_missing"));

            var knownDeckTurn = new TurnRecorder(initial.Clone()).EndTurn();
            var knownCard = knownDeckTurn.FinalState.Deck.First(inCard =>
                initial.Deck.Any(inInitialCard => inInitialCard.Id == inCard.Id));
            knownCard.Id = CardInstanceData.CreateId(99999998);
            knownDeckTurn.FinalState.NextCardInstanceId = 99999999;
            knownDeckTurn.FinalStateHash = GameStateHasher.Compute(knownDeckTurn.FinalState);
            var knownDeckResult = new TurnReplayer().Replay(initial, knownDeckTurn);
            Assert.That(knownDeckResult.IsMatch, Is.False);
            Assert.That(knownDeckResult.Reason, Is.EqualTo("known_deck_card_mismatch"));
        }

        [Test]
        public void InitialFactory_SeededStateAndHashRoundTrip()
        {
            var first = InitialMatchFactory.Create(inSeed: 99);
            var second = InitialMatchFactory.Create(inSeed: 99);
            Assert.That(first.InitialStateHash, Is.EqualTo(second.InitialStateHash));
            var restored = NetJson.Deserialize<CreateMatchRequestData>(NetJson.Serialize(first));
            Assert.That(GameStateHasher.Compute(restored.InitialState), Is.EqualTo(first.InitialStateHash));
            Assert.That(restored.InitialState.Revision, Is.Zero);
        }

        private static TurnPackageData RoundTrip(TurnPackageData inTurn)
        {
            return NetJson.Deserialize<TurnPackageData>(NetJson.Serialize(inTurn));
        }
    }
}
