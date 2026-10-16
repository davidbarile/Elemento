using System;
using System.Collections.Generic;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class OnlineProtocolTests
    {
        [Test]
        public void ProtocolDtos_JsonRoundTripPreservesEveryEnvelope()
        {
            var decks = CreateDeckDefinitionsBySeat();
            var state = new MatchFactory().Create(27, CreateFastRules(), decks);
            var legalMove = new GameSimulator().GetLegalMoves(state, state.CurrentPlayerId)[0];
            var command = CreateCommand(state, legalMove, 4);
            var authority = new MatchAuthority();
            var result = authority.ValidateAndApply(state, command);

            AssertRoundTrip(command);
            AssertRoundTrip(result);
            AssertRoundTrip(FairView.BuildSnapshot(state));
            AssertRoundTrip(FairView.BuildSeatView(state, 0));
            AssertRoundTrip(result.LogEntry);
        }

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
        public void MatchAuthority_ReplaysAiMoveLogToIdenticalFinalState()
        {
            const int seed = 7331;
            const int startingSeat = 1;
            var decks = CreateDeckDefinitionsBySeat();
            var rules = CreateFastRules();
            var state = new MatchFactory().Create(seed, rules, decks, null, startingSeat);
            var authority = new MatchAuthority();
            IAiPlayer[] players =
            {
                new LegacyAiPlayer(EAiDifficulty.Hard),
                new LegacyAiPlayer(EAiDifficulty.Hard)
            };
            var random = new DeterministicRandom(unchecked(seed ^ (int)0x9E3779B9u));

            for (int sequence = 0; sequence < 2000 && state.WinCondition == EWinCondition.None; ++sequence)
            {
                int seat = state.CurrentPlayerId;
                var move = players[seat].ChooseMove(new GameSimulator().GetPlayerView(state, seat), random);
                Assert.That(move, Is.Not.Null, "AI returned no move before the game ended.");

                var result = authority.ValidateAndApply(state, CreateCommand(state, move, sequence));
                Assert.That(result.Accepted, Is.True, result.Reason);
            }

            Assert.That(state.WinCondition, Is.Not.EqualTo(EWinCondition.None), "AI self-play exceeded the move cap.");
            var replayed = new MatchAuthority().Replay(seed, rules, decks,
                new List<MoveLogEntryData>(authority.MoveLog), startingSeat);

            Assert.That(NetJson.Serialize(FairView.BuildSnapshot(replayed)),
                Is.EqualTo(NetJson.Serialize(FairView.BuildSnapshot(state))));
            Assert.That(NetJson.Serialize(replayed), Is.EqualTo(NetJson.Serialize(state)));
        }

        [Test]
        public void MatchAuthority_ReplayPreservesFrozenZoneAfterEndTurn()
        {
            const int seed = 7332;
            var decks = CreateDeckDefinitionsBySeat();
            var rules = new RulesConfigData
            {
                AllowSelectPowerCardType = true,
                ElementZonePointsRequiredToActivate = 1,
                MovesPerTurn = 0,
                NumCardsToDeal = 3,
                TotalPointsRequiredToWin = 10000
            };
            var state = new MatchFactory().Create(seed, rules, decks);
            var authority = new MatchAuthority();
            var simulator = new GameSimulator();

            var play = simulator.GetLegalMoves(state, 0).Find(inMove =>
                inMove.Type == EMoveType.PlayCard && inMove.TargetPlayerId == 0);
            Assert.That(play, Is.Not.Null);
            Assert.That(authority.ValidateAndApply(state, CreateCommand(state, play, 0)).Accepted, Is.True);

            var activate = simulator.GetLegalMoves(state, 0).Find(inMove =>
                inMove.Type == EMoveType.ActivateZone && inMove.PowerCardType == EPowerCardType.Freeze);
            Assert.That(activate, Is.Not.Null);
            Assert.That(authority.ValidateAndApply(state, CreateCommand(state, activate, 1)).Accepted, Is.True);

            var freezeCard = state.Players[0].Hand.Find(inCard => inCard.WildFreeze > 0);
            Assert.That(freezeCard, Is.Not.Null);
            var freeze = simulator.GetLegalMoves(state, 0).Find(inMove =>
                inMove.Type == EMoveType.PlayCard && inMove.CardId == freezeCard.Id
                && inMove.TargetPlayerId == 1);
            Assert.That(freeze, Is.Not.Null);
            Assert.That(authority.ValidateAndApply(state, CreateCommand(state, freeze, 2)).Accepted, Is.True);
            int frozenZoneId = freeze.TargetZoneId;

            var endTurn = simulator.GetLegalMoves(state, 0).Find(inMove => inMove.Type == EMoveType.EndTurn);
            Assert.That(endTurn, Is.Not.Null);
            Assert.That(authority.ValidateAndApply(state, CreateCommand(state, endTurn, 3)).Accepted, Is.True);

            var replayed = new MatchAuthority().Replay(seed, rules, decks,
                new List<MoveLogEntryData>(authority.MoveLog));
            int expectedFrozenHealth = rules.FrozenZoneHealth - rules.FrozenZonesMeltAmountPerTurn;

            Assert.That(state.Players[1].Zones[frozenZoneId].FrozenHealth, Is.EqualTo(expectedFrozenHealth));
            Assert.That(replayed.Players[1].Zones[frozenZoneId].FrozenHealth, Is.EqualTo(expectedFrozenHealth));
            Assert.That(NetJson.Serialize(replayed), Is.EqualTo(NetJson.Serialize(state)));
        }

        [Test]
        public void MatchAuthority_RejectsWrongSeatStaleRevisionAndIllegalMove()
        {
            var state = new MatchFactory().Create(81, CreateFastRules(), CreateDeckDefinitionsBySeat());
            var simulator = new GameSimulator();
            var legalMove = simulator.GetLegalMoves(state, 0)[0];
            var authority = new MatchAuthority();

            var wrongSeat = CreateCommand(state, legalMove, 0);
            wrongSeat.Seat = 1;
            wrongSeat.Move.PlayerId = 1;
            var wrongSeatResult = authority.ValidateAndApply(state, wrongSeat);

            var stale = CreateCommand(state, legalMove, 1);
            stale.ExpectedRevision = state.Revision - 1;
            var staleResult = authority.ValidateAndApply(state, stale);

            var illegal = CreateCommand(state, new MoveData
            {
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "missing-card",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            }, 2);
            var illegalResult = authority.ValidateAndApply(state, illegal);

            Assert.That(wrongSeatResult.Accepted, Is.False);
            Assert.That(wrongSeatResult.Reason, Is.EqualTo(MatchAuthority.TurnReason));
            Assert.That(staleResult.Accepted, Is.False);
            Assert.That(staleResult.Reason, Is.EqualTo(MatchAuthority.RevisionReason));
            Assert.That(illegalResult.Accepted, Is.False);
            Assert.That(illegalResult.Reason, Is.EqualTo(MatchAuthority.MoveReason));
            Assert.That(state.Revision, Is.Zero);
            Assert.That(authority.MoveLog, Is.Empty);
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

        private static MoveCommandData CreateCommand(GameStateData inState, MoveData inMove, int inSequence)
        {
            return new MoveCommandData
            {
                MatchId = inState.MatchId,
                Seat = inState.CurrentPlayerId,
                ClientMoveSequence = inSequence,
                ExpectedRevision = inState.Revision,
                Move = inMove.Clone(),
                ClientNonce = "nonce-" + inSequence
            };
        }

        private static void AssertRoundTrip<T>(T inValue)
        {
            string json = NetJson.Serialize(inValue);
            T roundTrip = NetJson.Deserialize<T>(json);

            Assert.That(NetJson.Serialize(roundTrip), Is.EqualTo(json));
        }
    }
}
