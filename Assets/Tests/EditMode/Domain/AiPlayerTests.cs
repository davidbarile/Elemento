using System.Collections.Generic;
using Elemento.Domain;
using NUnit.Framework;

namespace Elemento.Tests.Domain
{
    public sealed class AiPlayerTests
    {
        [Test]
        public void HardAi_FixedViewAlwaysChoosesHighestScoredMove()
        {
            var view = CreateRankedPointView();
            var ai = new LegacyAiPlayer(EAiDifficulty.Hard);

            var first = ai.ChooseMove(view, new DeterministicRandom(17));
            var second = ai.ChooseMove(view, new DeterministicRandom(999));

            Assert.That(first.CardId, Is.EqualTo("point-10"));
            Assert.That(second.CardId, Is.EqualTo("point-10"));
        }

        [Test]
        public void DifficultyMistakeRates_MatchLegacyRankBuckets()
        {
            const int decisions = 2000;
            var view = CreateRankedPointView();
            var hard = new LegacyAiPlayer(EAiDifficulty.Hard);
            var medium = new LegacyAiPlayer(EAiDifficulty.Medium);
            var easy = new LegacyAiPlayer(EAiDifficulty.Easy);
            var hardRandom = new DeterministicRandom(123);
            var mediumRandom = new DeterministicRandom(123);
            var easyRandom = new DeterministicRandom(123);
            int hardMistakes = 0;
            int mediumMistakes = 0;
            int easyMistakes = 0;

            for (int i = 0; i < decisions; ++i)
            {
                int hardValue = GetPointValue(view, hard.ChooseMove(view, hardRandom));
                int mediumValue = GetPointValue(view, medium.ChooseMove(view, mediumRandom));
                int easyValue = GetPointValue(view, easy.ChooseMove(view, easyRandom));

                if (hardValue != 10)
                    ++hardMistakes;
                if (mediumValue != 10)
                    ++mediumMistakes;
                if (easyValue != 10)
                    ++easyMistakes;

                Assert.That(mediumValue, Is.InRange(6, 9));
                Assert.That(easyValue, Is.InRange(1, 5));
            }

            Assert.That(hardMistakes, Is.Zero);
            Assert.That(mediumMistakes, Is.EqualTo(decisions));
            Assert.That(easyMistakes, Is.EqualTo(decisions));
        }

        [Test]
        public void DifficultyRandomness_SameSeedProducesSameChoices()
        {
            var view = CreateRankedPointView();
            var ai = new LegacyAiPlayer(EAiDifficulty.Medium);
            var firstRandom = new DeterministicRandom(77);
            var secondRandom = new DeterministicRandom(77);

            for (int i = 0; i < 100; ++i)
            {
                Assert.That(ai.ChooseMove(view, firstRandom).CardId,
                    Is.EqualTo(ai.ChooseMove(view, secondRandom).CardId));
            }
        }

        [Test]
        public void StrongAi_BombThatDestroysZoneBeatsOnePointPlay()
        {
            var view = CreateBombDecisionView();
            var ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.CardId, Is.EqualTo("bomb"));
            Assert.That(move.TargetPlayerId, Is.EqualTo(1));
        }

        [Test]
        public void StrongAi_HiddenCardOrderDoesNotChangeDeterministicChoice()
        {
            var simulator = new GameSimulator();
            var original = simulator.CreateGame(12345);
            var reordered = original.Clone();
            reordered.Players[1].Hand.Reverse();
            reordered.Deck.Reverse();
            var ai = new StrongAiPlayer();

            var originalMove = ai.ChooseMove(simulator.GetPlayerView(original, 0),
                new DeterministicRandom(77));
            var reorderedMove = ai.ChooseMove(simulator.GetPlayerView(reordered, 0),
                new DeterministicRandom(77));

            Assert.That(originalMove, Is.Not.Null);
            Assert.That(reorderedMove, Is.Not.Null);
            Assert.That(reorderedMove.Id, Is.EqualTo(originalMove.Id));
        }

        private static PlayerViewData CreateRankedPointView()
        {
            var view = new PlayerViewData
            {
                CurrentPlayerId = 0,
                MovesRemaining = 99,
                Rules = new RulesConfigData
                {
                    TotalPointsRequiredToWin = 1000,
                    ElementZonePointsRequiredToActivate = 10
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            view.Player.Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            });

            for (int value = 1; value <= 10; ++value)
            {
                string cardId = "point-" + value;
                view.Player.Hand.Add(new CardInstanceData
                {
                    Id = cardId,
                    DefinitionName = cardId,
                    IsFaceUp = true,
                    Points = new[] { value, 0, 0, 0 },
                    Multipliers = new int[4],
                    Bombs = new int[4],
                    Shields = new int[4]
                });
                view.LegalMoves.Add(new MoveData
                {
                    Id = "move-" + value,
                    PlayerId = 0,
                    Type = EMoveType.PlayCard,
                    CardId = cardId,
                    SourceZoneId = -1,
                    TargetPlayerId = 0,
                    TargetZoneId = 0
                });
            }

            return view;
        }

        private static PlayerViewData CreateBombDecisionView()
        {
            var view = new PlayerViewData
            {
                CurrentPlayerId = 0,
                MovesRemaining = 2,
                Rules = new RulesConfigData
                {
                    ElementZoneStartingHealth = 1,
                    ElementZonePointsRequiredToActivate = 10,
                    TotalPointsRequiredToWin = 1000
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            view.Player.Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 1,
                MaxHealth = 1
            });
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent" });
            view.Opponents[0].Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 1,
                MaxHealth = 1
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "point-1",
                DefinitionName = "Point 1",
                Points = new[] { 1, 0, 0, 0 },
                Multipliers = new int[4],
                Bombs = new int[4],
                Shields = new int[4]
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "bomb",
                DefinitionName = "Bomb",
                WildBomb = 1,
                Points = new int[4],
                Multipliers = new int[4],
                Bombs = new int[4],
                Shields = new int[4]
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "point-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "point-1",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "bomb-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "bomb",
                SourceZoneId = -1,
                TargetPlayerId = 1,
                TargetZoneId = 0
            });
            return view;
        }

        private static int GetPointValue(PlayerViewData inView, MoveData inMove)
        {
            foreach (var card in inView.Player.Hand)
            {
                if (card.Id == inMove.CardId)
                    return card.Points[0];
            }

            Assert.Fail("The selected card was not in the fixed hand.");
            return 0;
        }
    }
}
