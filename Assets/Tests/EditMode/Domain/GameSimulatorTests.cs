using System.Collections.Generic;
using Elemento.Domain;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class GameSimulatorTests
    {
        [Test]
        public void RulesConfigData_DefaultsMatchMainScene()
        {
            var rules = new RulesConfigData();

            Assert.That(rules.NumCardsToDeal, Is.EqualTo(5));
            Assert.That(rules.MaxCardsInHand, Is.EqualTo(7));
            Assert.That(rules.MovesPerTurn, Is.Zero);
            Assert.That(rules.ElementZoneStartingHealth, Is.EqualTo(5));
            Assert.That(rules.FrozenZoneHealth, Is.EqualTo(5));
            Assert.That(rules.LightningDamage, Is.EqualTo(5));
            Assert.That(rules.ElementZonePointsRequiredToActivate, Is.EqualTo(10));
            Assert.That(rules.TotalPointsRequiredToWin, Is.EqualTo(50));
            Assert.That(rules.FrozenZonesMeltAmountPerTurn, Is.EqualTo(1));
            Assert.That(rules.DiscardMode, Is.EqualTo(EDiscardMode.AllowDiscard));
            Assert.That(rules.AllowSelectPowerCardType, Is.True);
            Assert.That(rules.CardSpawnBatchSize, Is.EqualTo(10));
            Assert.That(rules.MaxCardsPerZone, Is.EqualTo(20));
        }

        [Test]
        public void CreateGame_UsesChoiceDeckAndDealsOnlyFirstPlayer()
        {
            var simulator = new GameSimulator();
            var state = simulator.CreateGame(17);

            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(5));
            Assert.That(state.Players[1].Hand.Count, Is.Zero);
            Assert.That(state.Deck.Count + state.ReserveDeck.Count + state.Players[0].Hand.Count, Is.EqualTo(555));
            Assert.That(state.PowerDeck.Count, Is.EqualTo(37));
            Assert.That(state.CurrentPlayerId, Is.Zero);
            Assert.That(state.TurnPhase, Is.EqualTo(ETurnPhase.PlayerPlaying));
        }

        [Test]
        public void ApplyMove_BombStopsAtShieldAndRecyclesRemovedCards()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var target = state.Players[1].Zones[0];
            target.Cards.Add(CreateCard("lower", points: 2));
            target.Cards.Add(CreateCard("shield", shield: 2));
            target.Points = 2;
            state.Players[0].Hand.Add(CreateCard("bomb", bomb: 3));
            int reserveBefore = state.ReserveDeck.Count;

            simulator.ApplyMove(state, FindPlay(simulator, state, "bomb", 1, 0));

            Assert.That(target.Cards.Count, Is.EqualTo(1));
            Assert.That(target.Cards[0].Id, Is.EqualTo("lower"));
            Assert.That(state.ReserveDeck.Count, Is.EqualTo(reserveBefore + 2));
        }

        [Test]
        public void ApplyMove_BombComboPlaysNormallyOnOwnZoneAndBombsOpponent()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var ownCombo = CreateCard("own-combo", points: 2, bomb: 1);
            var attackingCombo = CreateCard("attack-combo", points: 2, bomb: 1);
            state.Players[0].Hand.Add(ownCombo);
            state.Players[0].Hand.Add(attackingCombo);
            int reserveBefore = state.ReserveDeck.Count;

            simulator.ApplyMove(state, FindPlay(simulator, state, "own-combo", 0, 0));

            Assert.That(state.Players[0].Zones[0].Cards.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Zones[0].Points, Is.EqualTo(2));
            Assert.That(state.ReserveDeck.Count, Is.EqualTo(reserveBefore));

            simulator.ApplyMove(state, FindPlay(simulator, state, "attack-combo", 1, 0));

            Assert.That(state.Players[1].Zones[0].Health, Is.EqualTo(4));
            Assert.That(state.ReserveDeck.Count, Is.EqualTo(reserveBefore + 1));
        }

        [Test]
        public void GetLegalMoves_PureBombCannotBePlayedOnOwnUnfrozenZone()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            state.Players[0].Hand.Add(CreateCard("bomb", bomb: 1));

            foreach (var move in simulator.GetLegalMoves(state, 0))
            {
                Assert.That(move.Type != EMoveType.PlayCard || move.TargetPlayerId != 0,
                    Is.True, "Pure bomb unexpectedly targeted the owner's board.");
            }
        }

        [Test]
        public void ApplyMove_EmptyZoneDamageDestroysLastZoneAndWins()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            for (int i = 1; i < 4; ++i)
                state.Players[1].Zones[i].IsDestroyed = true;
            state.Players[1].Zones[0].Health = 1;
            state.Players[0].Hand.Add(CreateCard("bomb", bomb: 1));

            simulator.ApplyMove(state, FindPlay(simulator, state, "bomb", 1, 0));

            Assert.That(simulator.IsGameOver(state), Is.True);
            Assert.That(simulator.Winner(state), Is.Zero);
            Assert.That(simulator.WinType(state), Is.EqualTo(EWinCondition.AllZonesDestroyed));
        }

        [Test]
        public void ApplyMove_HealRestoresDestroyedOwnEmptyZone()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var zone = state.Players[0].Zones[0];
            zone.Health = 0;
            zone.IsDestroyed = true;
            var heal = DeckDefinitionData.CreatePowerCard(EPowerCardType.Heal);
            heal.Id = "heal";
            heal.WildZoneHeal = state.Rules.ElementZoneStartingHealth;
            state.Players[0].Hand.Add(heal);
            int powerDeckCount = state.PowerDeck.Count;

            simulator.ApplyMove(state, FindPlay(simulator, state, "heal", 0, 0));

            Assert.That(zone.Health, Is.EqualTo(5));
            Assert.That(zone.MaxHealth, Is.EqualTo(5));
            Assert.That(zone.IsDestroyed, Is.False);
            AssertPowerCardConsumedAndRecycled(state, powerDeckCount);
        }

        [Test]
        public void ApplyMove_FreezeThenNextTurnMeltsCurrentPlayersZone()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var freeze = DeckDefinitionData.CreatePowerCard(EPowerCardType.Freeze);
            freeze.Id = "freeze";
            freeze.WildFreeze = state.Rules.FrozenZoneHealth;
            state.Players[0].Hand.Add(freeze);
            int powerDeckCount = state.PowerDeck.Count;

            simulator.ApplyMove(state, FindPlay(simulator, state, "freeze", 1, 0));
            Assert.That(state.Players[1].Zones[0].FrozenHealth, Is.EqualTo(5));
            AssertPowerCardConsumedAndRecycled(state, powerDeckCount);

            simulator.EndTurn(state);

            Assert.That(state.Players[1].Zones[0].FrozenHealth, Is.EqualTo(4));
        }

        [Test]
        public void ApplyMove_LightningIgnoresShieldAndRemovesTopCards()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var target = state.Players[1].Zones[0];
            target.Cards.Add(CreateCard("bottom", points: 1));
            target.Cards.Add(CreateCard("shield", shield: 5));
            target.Cards.Add(CreateCard("top", points: 2));
            var lightning = DeckDefinitionData.CreatePowerCard(EPowerCardType.Lightning);
            lightning.Id = "lightning";
            lightning.WildLightning = 2;
            state.Players[0].Hand.Add(lightning);
            int powerDeckCount = state.PowerDeck.Count;

            simulator.ApplyMove(state, FindPlay(simulator, state, "lightning", 1, 0));

            Assert.That(target.Cards.Count, Is.EqualTo(1));
            Assert.That(target.Cards[0].Id, Is.EqualTo("bottom"));
            AssertPowerCardConsumedAndRecycled(state, powerDeckCount);
        }

        [Test]
        public void ApplyMove_ActivationClearsZoneAndDealsSelectedPowerCardWithoutSpendingMove()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);
            var zone = state.Players[0].Zones[0];
            zone.Cards.Add(CreateCard("points", points: 10));
            zone.Points = 10;
            zone.FaceUpPoints = 10;
            zone.IsActivated = true;
            int movesBefore = state.MovesRemaining;
            MoveData activation = null;
            foreach (var move in simulator.GetLegalMoves(state, 0))
            {
                if (move.Type == EMoveType.ActivateZone && move.SourceZoneId == 0 && move.PowerCardType == EPowerCardType.Heal)
                {
                    activation = move;
                    break;
                }
            }

            simulator.ApplyMove(state, activation);

            Assert.That(zone.Cards, Is.Empty);
            Assert.That(zone.Points, Is.Zero);
            Assert.That(zone.IsActivated, Is.False);
            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Hand[0].WildZoneHeal, Is.EqualTo(5));
            Assert.That(state.MovesRemaining, Is.EqualTo(movesBefore));
        }

        [Test]
        public void ApplyMove_MultiplierRecalculatesZoneAndPointsWin()
        {
            var simulator = new GameSimulator();
            var rules = new RulesConfigData { TotalPointsRequiredToWin = 4 };
            var state = CreateEmptyState(simulator, rules);
            var zone = state.Players[0].Zones[0];
            zone.Cards.Add(CreateCard("points", points: 2));
            zone.Points = 2;
            zone.FaceUpPoints = 2;
            state.Players[0].Hand.Add(CreateCard("multiplier", multiplier: 2));

            simulator.ApplyMove(state, FindPlay(simulator, state, "multiplier", 0, 0));

            Assert.That(zone.Points, Is.EqualTo(4));
            Assert.That(simulator.Winner(state), Is.Zero);
            Assert.That(simulator.WinType(state), Is.EqualTo(EWinCondition.PointsTarget));
        }

        [Test]
        public void ApplyMove_LimitedMovesAutoEndsUnlessDiscardMustEndTurn()
        {
            var simulator = new GameSimulator();
            var rules = new RulesConfigData { MovesPerTurn = 1, NumCardsToDeal = 0 };
            var state = CreateEmptyState(simulator, rules);
            state.Players[0].Hand.Add(CreateCard("points", points: 1));

            simulator.ApplyMove(state, FindPlay(simulator, state, "points", 0, 0));

            Assert.That(state.CurrentPlayerId, Is.EqualTo(1));
            Assert.That(state.MovesRemaining, Is.EqualTo(1));
        }

        [Test]
        public void ApplyMove_DiscardEndsTurnWithoutSpendingMove()
        {
            var simulator = new GameSimulator();
            var rules = new RulesConfigData
            {
                MovesPerTurn = 3,
                NumCardsToDeal = 0,
                DiscardMode = EDiscardMode.DiscardEndsTurn
            };
            var state = CreateEmptyState(simulator, rules);
            state.Players[0].Hand.Add(CreateCard("discard"));
            MoveData discard = null;
            foreach (var move in simulator.GetLegalMoves(state, 0))
            {
                if (move.Type == EMoveType.DiscardCard && move.CardId == "discard")
                {
                    discard = move;
                    break;
                }
            }

            simulator.ApplyMove(state, discard);

            Assert.That(state.CurrentPlayerId, Is.EqualTo(1));
            Assert.That(state.MovesRemaining, Is.EqualTo(3));
        }

        [Test]
        public void GetLegalMoves_DiscardModesControlDiscardAndEndTurnMoves()
        {
            var simulator = new GameSimulator();
            var noneState = CreateEmptyState(simulator, new RulesConfigData
            {
                NumCardsToDeal = 0,
                DiscardMode = EDiscardMode.None
            });
            noneState.Players[0].Hand.Add(CreateCard("none"));

            Assert.That(HasMoveType(simulator.GetLegalMoves(noneState, 0), EMoveType.DiscardCard), Is.False);
            Assert.That(HasMoveType(simulator.GetLegalMoves(noneState, 0), EMoveType.EndTurn), Is.True);

            var allowState = CreateEmptyState(simulator, new RulesConfigData
            {
                NumCardsToDeal = 0,
                DiscardMode = EDiscardMode.AllowDiscard
            });
            allowState.Players[0].Hand.Add(CreateCard("allow"));

            Assert.That(HasMoveType(simulator.GetLegalMoves(allowState, 0), EMoveType.DiscardCard), Is.True);
            Assert.That(HasMoveType(simulator.GetLegalMoves(allowState, 0), EMoveType.EndTurn), Is.True);

            var endState = CreateEmptyState(simulator, new RulesConfigData
            {
                NumCardsToDeal = 0,
                DiscardMode = EDiscardMode.DiscardEndsTurn
            });
            endState.Players[0].Hand.Add(CreateCard("end"));

            Assert.That(HasMoveType(simulator.GetLegalMoves(endState, 0), EMoveType.DiscardCard), Is.True);
            Assert.That(HasMoveType(simulator.GetLegalMoves(endState, 0), EMoveType.EndTurn), Is.False);
        }

        [Test]
        public void ApplyMove_RandomPowerCardDrawConsumesThenDiscardRecyclesPowerCard()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator, new RulesConfigData
            {
                NumCardsToDeal = 0,
                AllowSelectPowerCardType = false
            });
            var zone = state.Players[0].Zones[0];
            zone.Cards.Add(CreateCard("points", points: 10));
            zone.Points = 10;
            zone.FaceUpPoints = 10;
            zone.IsActivated = true;
            int powerCardsBefore = state.PowerDeck.Count;
            MoveData activation = null;
            foreach (var move in simulator.GetLegalMoves(state, 0))
            {
                if (move.Type == EMoveType.ActivateZone)
                {
                    activation = move;
                    break;
                }
            }

            simulator.ApplyMove(state, activation);

            Assert.That(state.PowerDeck.Count, Is.EqualTo(powerCardsBefore - 1));
            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(1));

            MoveData discard = null;
            foreach (var move in simulator.GetLegalMoves(state, 0))
            {
                if (move.Type == EMoveType.DiscardCard && move.CardId == state.Players[0].Hand[0].Id)
                {
                    discard = move;
                    break;
                }
            }

            simulator.ApplyMove(state, discard);

            Assert.That(state.PowerDeck.Count, Is.EqualTo(powerCardsBefore));
        }

        [Test]
        public void EndTurn_ExhaustedDeckDealsAvailableCardsAndRecycledDiscardRefillsIt()
        {
            var simulator = new GameSimulator();
            var rules = new RulesConfigData { NumCardsToDeal = 3, MaxCardsInHand = 7 };
            var state = CreateEmptyState(simulator, rules);
            state.Deck.Clear();
            state.ReserveDeck.Clear();
            var onlyCard = CreateCard("physical", points: 1);
            state.Deck.Add(onlyCard);

            simulator.EndTurn(state);

            Assert.That(state.Players[1].Hand.Count, Is.EqualTo(1));
            Assert.That(state.Deck, Is.Empty);

            state.Players[1].Hand.Clear();
            state.Players[1].Hand.Add(onlyCard);
            MoveData discard = null;
            foreach (var move in simulator.GetLegalMoves(state, 1))
            {
                if (move.Type == EMoveType.DiscardCard)
                {
                    discard = move;
                    break;
                }
            }
            simulator.ApplyMove(state, discard);
            simulator.EndTurn(state);

            Assert.That(state.Players[0].Hand.Count, Is.EqualTo(1));
            Assert.That(state.Players[0].Hand[0].DefinitionName, Is.EqualTo(onlyCard.DefinitionName));
        }

        [Test]
        public void EndTurn_TurnNumberAdvancesOnlyWhenReturningToFirstPlayer()
        {
            var simulator = new GameSimulator();
            var state = CreateEmptyState(simulator);

            Assert.That(state.TurnNumber, Is.EqualTo(1));

            simulator.EndTurn(state);

            Assert.That(state.CurrentPlayerId, Is.EqualTo(1));
            Assert.That(state.TurnNumber, Is.EqualTo(1));

            simulator.EndTurn(state);

            Assert.That(state.CurrentPlayerId, Is.Zero);
            Assert.That(state.TurnNumber, Is.EqualTo(2));
        }

        [Test]
        public void PlayerView_HidesOpponentHandAndDeckOrder()
        {
            var simulator = new GameSimulator();
            var state = simulator.CreateGame(9);
            simulator.EndTurn(state);

            var view = simulator.GetPlayerView(state, 0);

            Assert.That(view.Player.Hand.Count, Is.EqualTo(state.Players[0].Hand.Count));
            Assert.That(view.Opponents[0].HandCount, Is.EqualTo(state.Players[1].Hand.Count));
            Assert.That(view.Rules.TotalPointsRequiredToWin, Is.EqualTo(state.Rules.TotalPointsRequiredToWin));
            Assert.That(view.Rules, Is.Not.SameAs(state.Rules));
            Assert.That(typeof(OpponentViewData).GetField("Hand"), Is.Null);
            Assert.That(typeof(PlayerViewData).GetField("Deck"), Is.Null);
            Assert.That(typeof(PlayerViewData).GetField("Random"), Is.Null);
        }

        [Test]
        public void FullSeededGame_SameSeedAndPolicyProduceIdenticalWinnerAndState()
        {
            var simulator = new GameSimulator();
            var rules = new RulesConfigData
            {
                MovesPerTurn = 3,
                TotalPointsRequiredToWin = 12,
                ElementZonePointsRequiredToActivate = 100
            };
            var first = simulator.CreateGame(123456, rules);
            var second = simulator.CreateGame(123456, rules);

            for (int i = 0; i < 1000 && !simulator.IsGameOver(first); ++i)
            {
                var firstMove = ChooseDeterministicMove(simulator.GetLegalMoves(first, first.CurrentPlayerId));
                var secondMove = ChooseDeterministicMove(simulator.GetLegalMoves(second, second.CurrentPlayerId));
                AssertMovesEqual(firstMove, secondMove);
                simulator.ApplyMove(first, firstMove);
                simulator.ApplyMove(second, secondMove);
                AssertStatesEqual(first, second);
            }

            Assert.That(simulator.IsGameOver(first), Is.True);
            Assert.That(simulator.Winner(first), Is.EqualTo(simulator.Winner(second)));
            Assert.That(simulator.WinType(first), Is.EqualTo(simulator.WinType(second)));
        }

        private static GameStateData CreateEmptyState(GameSimulator inSimulator, RulesConfigData inRules = null)
        {
            var rules = inRules ?? new RulesConfigData { NumCardsToDeal = 0 };
            var state = inSimulator.CreateGame(71, rules);
            foreach (var player in state.Players)
            {
                player.Hand.Clear();
                foreach (var zone in player.Zones)
                {
                    zone.Cards.Clear();
                    zone.Points = 0;
                    zone.FaceUpPoints = 0;
                    zone.FrozenHealth = 0;
                    zone.IsActivated = false;
                    zone.IsDestroyed = false;
                    zone.Health = state.Rules.ElementZoneStartingHealth;
                    zone.MaxHealth = state.Rules.ElementZoneStartingHealth;
                }
            }
            return state;
        }

        private static CardInstanceData CreateCard(string inId, int points = 0, int multiplier = 0, int bomb = 0, int shield = 0)
        {
            return new CardInstanceData
            {
                Id = inId,
                DefinitionName = inId,
                IsFaceUp = true,
                Points = new[] { points, 0, 0, 0 },
                Multipliers = new[] { multiplier, 0, 0, 0 },
                Bombs = new[] { bomb, 0, 0, 0 },
                Shields = new[] { shield, 0, 0, 0 }
            };
        }

        private static MoveData FindPlay(GameSimulator inSimulator, GameStateData inState, string inCardId, int inTargetPlayerId, int inTargetZoneId)
        {
            foreach (var move in inSimulator.GetLegalMoves(inState, inState.CurrentPlayerId))
            {
                if (move.Type == EMoveType.PlayCard
                    && move.CardId == inCardId
                    && move.TargetPlayerId == inTargetPlayerId
                    && move.TargetZoneId == inTargetZoneId)
                {
                    return move;
                }
            }

            Assert.Fail("Expected legal play was not generated.");
            return null;
        }

        private static void AssertPowerCardConsumedAndRecycled(GameStateData inState, int inPowerDeckCount)
        {
            Assert.That(inState.Players[0].Hand, Is.Empty);
            Assert.That(inState.PowerDeck, Has.Count.EqualTo(inPowerDeckCount + 1));
            Assert.That(inState.PowerDeck[inState.PowerDeck.Count - 1].Id, Is.Null);
            Assert.That(inState.PowerDeck[inState.PowerDeck.Count - 1].IsPowerCard, Is.True);
            Assert.That(inState.Moves, Has.Count.EqualTo(1));
            Assert.That(inState.Revision, Is.EqualTo(1));
        }

        private static bool HasMoveType(List<MoveData> inMoves, EMoveType inMoveType)
        {
            foreach (var move in inMoves)
            {
                if (move.Type == inMoveType)
                    return true;
            }

            return false;
        }

        private static MoveData ChooseDeterministicMove(List<MoveData> inMoves)
        {
            foreach (var move in inMoves)
            {
                if (move.Type == EMoveType.PlayCard)
                    return move;
            }
            foreach (var move in inMoves)
            {
                if (move.Type == EMoveType.DiscardCard)
                    return move;
            }
            foreach (var move in inMoves)
            {
                if (move.Type == EMoveType.ActivateZone)
                    return move;
            }

            return inMoves[inMoves.Count - 1];
        }

        private static void AssertMovesEqual(MoveData inFirst, MoveData inSecond)
        {
            Assert.That(inSecond.Type, Is.EqualTo(inFirst.Type));
            Assert.That(inSecond.CardId, Is.EqualTo(inFirst.CardId));
            Assert.That(inSecond.SourceZoneId, Is.EqualTo(inFirst.SourceZoneId));
            Assert.That(inSecond.TargetPlayerId, Is.EqualTo(inFirst.TargetPlayerId));
            Assert.That(inSecond.TargetZoneId, Is.EqualTo(inFirst.TargetZoneId));
            Assert.That(inSecond.PowerCardType, Is.EqualTo(inFirst.PowerCardType));
        }

        private static void AssertStatesEqual(GameStateData inFirst, GameStateData inSecond)
        {
            Assert.That(inSecond.Revision, Is.EqualTo(inFirst.Revision));
            Assert.That(inSecond.TurnNumber, Is.EqualTo(inFirst.TurnNumber));
            Assert.That(inSecond.CurrentPlayerId, Is.EqualTo(inFirst.CurrentPlayerId));
            Assert.That(inSecond.Random.State, Is.EqualTo(inFirst.Random.State));
            Assert.That(inSecond.Random.Cursor, Is.EqualTo(inFirst.Random.Cursor));
            Assert.That(inSecond.Deck.Count, Is.EqualTo(inFirst.Deck.Count));
            Assert.That(inSecond.ReserveDeck.Count, Is.EqualTo(inFirst.ReserveDeck.Count));
            Assert.That(inSecond.PowerDeck.Count, Is.EqualTo(inFirst.PowerDeck.Count));
            for (int i = 0; i < inFirst.Players.Count; ++i)
            {
                Assert.That(inSecond.Players[i].Hand.Count, Is.EqualTo(inFirst.Players[i].Hand.Count));
                Assert.That(GameRules.CalculatePlayerPoints(inSecond.Players[i]), Is.EqualTo(GameRules.CalculatePlayerPoints(inFirst.Players[i])));
            }
        }
    }
}
