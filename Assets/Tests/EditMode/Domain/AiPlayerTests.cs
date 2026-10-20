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
        public void StrongAi_ThreeDamageBombBeatsTwoPointPlay()
        {
            var view = CreateBombDecisionView();
            view.Player.Hand[0].Points[0] = 2;
            view.Opponents[0].Zones[0].Health = 5;
            view.Opponents[0].Zones[0].MaxHealth = 5;
            view.Player.Hand[1].WildBomb = 3;
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

        [Test]
        public void StrongAi_EmergencyShieldProtectsExposedMultipliedZone()
        {
            var view = CreateEmergencyShieldView();
            var withoutEmergencyShield = new StrongAiPlayer(new StrongAiOptions
            {
                UseEffectBasedBombScoring = false
            });
            var ai = new StrongAiPlayer();

            var ordinaryMove = withoutEmergencyShield.ChooseMove(view, new DeterministicRandom(17));
            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(ordinaryMove.CardId, Is.EqualTo("point-15"));
            Assert.That(move.CardId, Is.EqualTo("shield"));
            Assert.That(move.TargetPlayerId, Is.EqualTo(0));
            Assert.That(move.TargetZoneId, Is.EqualTo(0));
        }

        [Test]
        public void StrongAi_WholeTurnPlanChoosesSynergyOverBestImmediateMove()
        {
            var view = CreateWholeTurnPlanningView();
            LegacyAiPlayer b2 = new StrongAiPlayer();
            IAiPlayer ai = new StrongAiPlayer();

            var immediateMove = b2.ChooseMove(view, new DeterministicRandom(17));
            var plannedMove = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(immediateMove.CardId, Is.EqualTo("earth-points"));
            Assert.That(plannedMove.CardId, Is.EqualTo("air-points"));
            Assert.That(plannedMove.TargetZoneId, Is.EqualTo(0));
        }

        [Test]
        public void StrongAi_HardDifficultyMatchesDefault()
        {
            var view = CreateWholeTurnPlanningView();
            var defaultAi = new StrongAiPlayer();
            var hardAi = new StrongAiPlayer(new StrongAiOptions
            {
                Difficulty = EAiDifficulty.Hard
            });

            for (int seed = 1; seed <= 20; ++seed)
            {
                var defaultMove = defaultAi.ChooseMove(view, new DeterministicRandom(seed));
                var hardMove = hardAi.ChooseMove(view, new DeterministicRandom(seed));

                Assert.That(hardMove.Id, Is.EqualTo(defaultMove.Id));
            }
        }

        [Test]
        public void StrongAi_DifficultyRandomnessSameSeedProducesSameChoices()
        {
            var view = CreateWholeTurnPlanningView();
            var difficulties = new[] { EAiDifficulty.Medium, EAiDifficulty.Easy };
            foreach (var difficulty in difficulties)
            {
                var ai = new StrongAiPlayer(new StrongAiOptions { Difficulty = difficulty });
                var firstRandom = new DeterministicRandom(77);
                var secondRandom = new DeterministicRandom(77);

                for (int i = 0; i < 100; ++i)
                {
                    Assert.That(ai.ChooseMove(view, firstRandom).Id,
                        Is.EqualTo(ai.ChooseMove(view, secondRandom).Id));
                }
            }
        }

        [Test]
        public void StrongAi_UnlimitedPlanPlacesMultiplierBelowPointsAndShield()
        {
            var view = CreateWholeTurnPlanningView();
            view.Rules.MovesPerTurn = 0;
            view.MovesRemaining = 0;
            view.Player.Hand.RemoveAt(2);
            view.LegalMoves.RemoveAt(2);
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "shield",
                IsFaceUp = true,
                Shields = new[] { 1, 0, 0, 0 }
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "shield-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "shield",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            IAiPlayer ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.CardId, Is.EqualTo("multiplier"));

            view.Player.Hand.RemoveAt(0);
            view.LegalMoves.RemoveAt(0);
            view.Player.Zones[0].Cards.Add(new CardInstanceData
            {
                Id = "multiplier",
                IsFaceUp = true,
                Multipliers = new[] { 2, 0, 0, 0 }
            });
            view.Player.Zones[0].Points = 6;
            view.Player.Zones[0].FaceUpPoints = 6;

            move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.CardId, Is.EqualTo("air-points"));
        }

        [Test]
        public void StrongAi_RebuildsExposedMultiplierBelowPointsAndShields()
        {
            var state = CreateMultiplierOrderingState(true, 0, true);

            PlayStrongAiTurn(state, out var moves);

            var cards = state.Players[0].Zones[0].Cards;
            int multiplierIndex = FindCardIndex(cards, "water-multiplier-x3");
            int points3Index = FindCardIndex(cards, "water-points-3");
            int points5Index = FindCardIndex(cards, "water-points-5");
            int points4Index = FindCardIndex(cards, "water-points-4");
            int shield1Index = FindCardIndex(cards, "water-shield-1");
            int shield3Index = FindCardIndex(cards, "water-shield-3");

            Assert.That(moves[0].CardId, Is.EqualTo("water-multiplier-x3"));
            Assert.That(multiplierIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(points3Index, Is.GreaterThan(multiplierIndex));
            Assert.That(points5Index, Is.GreaterThan(multiplierIndex));
            Assert.That(points4Index, Is.GreaterThan(multiplierIndex));
            int highestPointIndex = System.Math.Max(points3Index,
                System.Math.Max(points5Index, points4Index));
            Assert.That(shield1Index, Is.GreaterThan(highestPointIndex));
            Assert.That(shield3Index, Is.GreaterThan(shield1Index),
                "the stronger outer shield survives the more common weak bombs");
        }

        [Test]
        public void StrongAi_BuriesMultiplierFromHandWhenPileCanBeRebuilt()
        {
            var state = CreateMultiplierOrderingState(false, 0, true);

            PlayStrongAiTurn(state, out _);

            var cards = state.Players[0].Zones[0].Cards;
            int multiplierIndex = FindCardIndex(cards, "water-multiplier-x3");
            Assert.That(multiplierIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(FindCardIndex(cards, "water-points-3"), Is.GreaterThan(multiplierIndex));
            Assert.That(FindCardIndex(cards, "water-points-5"), Is.GreaterThan(multiplierIndex));
        }

        [Test]
        public void StrongAi_DoesNotStartMultiplierRebuildWithOnlyTwoMoves()
        {
            var state = CreateMultiplierOrderingState(true, 2, true);

            PlayStrongAiTurn(state, out var moves);

            Assert.That(moves[0].CardId, Is.Not.EqualTo("water-multiplier-x3"));
            Assert.That(moves[0].SourceZoneId, Is.EqualTo(-1));
        }

        [Test]
        public void StrongAi_DoesNotStripMultiplierWithoutAttackThreat()
        {
            var state = CreateMultiplierOrderingState(true, 0, false);

            PlayStrongAiTurn(state, out var moves);

            foreach (var move in moves)
                Assert.That(move.SourceZoneId, Is.EqualTo(-1));
        }

        [Test]
        public void StrongAi_LocksDualCardPlayedFromHandForRestOfTurn()
        {
            var view = CreateDualCardDecisionView();
            var ai = new StrongAiPlayer();
            var handPlay = view.LegalMoves[0];

            ai.RecordExecutedMove(view, handPlay);
            ai.RecordExecutedMove(view, new MoveData
            {
                Id = "change-opponent-pile",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "other-bomb",
                SourceZoneId = -1,
                TargetPlayerId = 1,
                TargetZoneId = 0
            });
            var dualCard = view.Player.Hand[0];
            view.Player.Hand.Clear();
            view.Player.Zones[0].Cards.Add(dualCard);
            view.Player.Zones[0].Points = GameRules.CalculateZonePoints(view.Player.Zones[0]);
            view.Player.Zones[0].FaceUpPoints = view.Player.Zones[0].Points;
            ++view.Revision;
            view.LegalMoves.Clear();
            var bombMove = new MoveData
            {
                Id = "move-played-dual-as-bomb",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = dualCard.Id,
                SourceZoneId = 0,
                TargetPlayerId = 1,
                TargetZoneId = 0
            };
            view.LegalMoves.Add(bombMove);
            view.LegalMoves.Add(CreateEndTurnMove("end-after-dual"));

            Assert.That(ai.IsMoveAllowedThisTurn(view, bombMove), Is.False);
            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.Type, Is.EqualTo(EMoveType.EndTurn));

            ++view.TurnNumber;
            ++view.Revision;
            Assert.That(ai.IsMoveAllowedThisTurn(view, bombMove), Is.True);
        }

        [Test]
        public void StrongAi_DualCardPrefersMultiplierOverWeakEmptyZoneBomb()
        {
            var view = CreateDualCardDecisionView();
            var ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.CardId, Is.EqualTo("dual-multiplier-bomb"));
            Assert.That(move.TargetPlayerId, Is.EqualTo(0));
            Assert.That(move.TargetZoneId, Is.EqualTo(0));
        }

        [Test]
        public void StrongAi_AllowsStripAndRestackWhenSeatingMultiplierUnderPoints()
        {
            var view = CreateMultiplierRebuildView();
            var ai = new StrongAiPlayer();
            var strip = new MoveData
            {
                Id = "strip-fire-points",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "fire-points",
                SourceZoneId = 0,
                TargetPlayerId = 0,
                TargetZoneId = 1
            };
            var place = new MoveData
            {
                Id = "place-multiplier",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "fire-multiplier",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            };
            var restack = new MoveData
            {
                Id = "restack-fire-points",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "fire-points",
                SourceZoneId = 1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            };

            Assert.That(ai.IsMoveAllowedThisTurn(view, strip), Is.True);
            ai.RecordExecutedMove(view, strip);
            MoveTopCard(view.Player.Zones[0], view.Player.Zones[1]);

            Assert.That(ai.IsMoveAllowedThisTurn(view, place), Is.True);
            ai.RecordExecutedMove(view, place);
            var multiplier = view.Player.Hand[0];
            view.Player.Hand.Clear();
            view.Player.Zones[0].Cards.Add(multiplier);
            RefreshZonePoints(view.Player.Zones[0]);
            ++view.Revision;

            Assert.That(ai.IsMoveAllowedThisTurn(view, restack), Is.True,
                "pile beneath changed when multiplier was placed");
            ai.RecordExecutedMove(view, restack);
            MoveTopCard(view.Player.Zones[1], view.Player.Zones[0]);
            Assert.That(view.Player.Zones[0].Cards[0].Id, Is.EqualTo("fire-multiplier"));
            Assert.That(view.Player.Zones[0].Cards[1].Id, Is.EqualTo("fire-points"));
        }

        [Test]
        public void StrongAi_CoversValuablePileWithShieldInsteadOfLeavingItExposed()
        {
            var view = CreateShieldRebuildView();
            var ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.CardId, Is.EqualTo("protective-shield"));
            Assert.That(move.SourceZoneId, Is.EqualTo(-1));
            Assert.That(move.TargetZoneId, Is.EqualTo(0));

            var shieldedZone = view.Player.Zones[0].Clone();
            shieldedZone.Cards.Add(view.Player.Hand[0].Clone());
            RefreshZonePoints(shieldedZone);
            var bombedZone = GameRules.ResolveBomb(shieldedZone,
                new CardInstanceData { WildBomb = 2 }, view.Rules);

            Assert.That(bombedZone.Points, Is.EqualTo(shieldedZone.Points));
        }

        [Test]
        public void StrongAi_AllowsShieldLiftWhenPileChangedBeneath()
        {
            var view = new PlayerViewData
            {
                Revision = 1,
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                Rules = new RulesConfigData { MovesPerTurn = 0, TotalPointsRequiredToWin = 1000 },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var zoneA = new ZoneStateData { Id = 0, ElementType = EElementType.Air, Health = 5, MaxHealth = 5,
                Points = 5, FaceUpPoints = 5 };
            var zoneB = new ZoneStateData { Id = 1, ElementType = EElementType.Earth, Health = 5, MaxHealth = 5,
                Points = 5, FaceUpPoints = 5 };
            var shield = new CardInstanceData { Id = "shield", IsFaceUp = true, WildShield = 2 };
            zoneA.Cards.Add(new CardInstanceData { Id = "a-points", IsFaceUp = true, WildPoints = 5 });
            zoneA.Cards.Add(shield);
            zoneB.Cards.Add(new CardInstanceData { Id = "b-points", IsFaceUp = true, WildPoints = 5 });
            view.Player.Zones.Add(zoneA);
            view.Player.Zones.Add(zoneB);
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent" });
            var ai = new StrongAiPlayer();
            var lift = new MoveData { Id = "lift", PlayerId = 0, Type = EMoveType.PlayCard,
                CardId = shield.Id, SourceZoneId = 0, TargetPlayerId = 0, TargetZoneId = 1 };
            var playHigh = new MoveData { Id = "play-high", PlayerId = 0, Type = EMoveType.PlayCard,
                CardId = "high", SourceZoneId = -1, TargetPlayerId = 0, TargetZoneId = 0 };
            var shieldBack = new MoveData { Id = "shield-back", PlayerId = 0, Type = EMoveType.PlayCard,
                CardId = shield.Id, SourceZoneId = 1, TargetPlayerId = 0, TargetZoneId = 0 };

            Assert.That(ai.IsMoveAllowedThisTurn(view, lift), Is.True);
            ai.RecordExecutedMove(view, lift);
            Assert.That(ai.IsMoveAllowedThisTurn(view, shieldBack), Is.False, "back-and-forth with nothing changed");

            view.Revision = 2;
            ai.RecordExecutedMove(view, playHigh);
            view.Revision = 3;
            Assert.That(ai.IsMoveAllowedThisTurn(view, shieldBack), Is.True, "pile beneath changed, shield may return");
        }

        [Test]
        public void StrongAi_BlocksReMoveWhenDestinationUnchanged()
        {
            var view = new PlayerViewData
            {
                Revision = 1,
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                Rules = new RulesConfigData { MovesPerTurn = 0, TotalPointsRequiredToWin = 1000 },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var zoneA = new ZoneStateData { Id = 0, ElementType = EElementType.Air, Health = 5, MaxHealth = 5 };
            var zoneB = new ZoneStateData { Id = 1, ElementType = EElementType.Earth, Health = 5, MaxHealth = 5,
                Points = 5, FaceUpPoints = 5 };
            var card = new CardInstanceData { Id = "moving-card", IsFaceUp = true, WildMultiplier = 2 };
            zoneA.Cards.Add(card);
            zoneB.Cards.Add(new CardInstanceData { Id = "b-points", IsFaceUp = true, WildPoints = 5 });
            view.Player.Zones.Add(zoneA);
            view.Player.Zones.Add(zoneB);
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent" });
            view.LegalMoves.Add(new MoveData { Id = "move-out", PlayerId = 0, Type = EMoveType.PlayCard,
                CardId = card.Id, SourceZoneId = 0, TargetPlayerId = 0, TargetZoneId = 1 });
            view.LegalMoves.Add(new MoveData { Id = "end-1", PlayerId = 0, Type = EMoveType.EndTurn,
                SourceZoneId = -1, TargetPlayerId = -1, TargetZoneId = -1 });
            var ai = new StrongAiPlayer();
            var first = ai.ChooseMove(view, new DeterministicRandom(17));
            Assume.That(first.CardId, Is.EqualTo(card.Id));

            zoneA.Cards.Clear();
            zoneB.Cards.Add(card);
            view.Revision = 2;
            view.LegalMoves.Clear();
            view.LegalMoves.Add(new MoveData { Id = "move-again", PlayerId = 0, Type = EMoveType.PlayCard,
                CardId = card.Id, SourceZoneId = 1, TargetPlayerId = 1, TargetZoneId = 0 });
            view.LegalMoves.Add(new MoveData { Id = "end-2", PlayerId = 0, Type = EMoveType.EndTurn,
                SourceZoneId = -1, TargetPlayerId = -1, TargetZoneId = -1 });

            var second = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(second.Type, Is.EqualTo(EMoveType.EndTurn));
        }

        [Test]
        public void StrongAi_DoesNotMoveCardBackToPreviousPileInSameTurn()
        {
            var view = new PlayerViewData
            {
                Revision = 1,
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                Rules = new RulesConfigData
                {
                    MovesPerTurn = 0,
                    TotalPointsRequiredToWin = 1000
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var sourceZone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            };
            var multiplier = new CardInstanceData
            {
                Id = "moving-multiplier",
                IsFaceUp = true,
                WildMultiplier = 2
            };
            sourceZone.Cards.Add(multiplier);
            var targetZone = new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Earth,
                Points = 5,
                FaceUpPoints = 5,
                Health = 5,
                MaxHealth = 5
            };
            targetZone.Cards.Add(new CardInstanceData
            {
                Id = "target-points",
                IsFaceUp = true,
                WildPoints = 5
            });
            view.Player.Zones.Add(sourceZone);
            view.Player.Zones.Add(targetZone);
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent" });
            view.LegalMoves.Add(new MoveData
            {
                Id = "move-out",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = multiplier.Id,
                SourceZoneId = sourceZone.Id,
                TargetPlayerId = 0,
                TargetZoneId = targetZone.Id
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "end-turn-1",
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            });
            IAiPlayer ai = new StrongAiPlayer();

            var firstMove = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(firstMove.CardId, Is.EqualTo(multiplier.Id));
            Assert.That(firstMove.SourceZoneId, Is.EqualTo(sourceZone.Id));
            Assert.That(firstMove.TargetZoneId, Is.EqualTo(targetZone.Id));

            sourceZone.Cards.Clear();
            sourceZone.Cards.Add(new CardInstanceData
            {
                Id = "source-points",
                IsFaceUp = true,
                WildPoints = 10
            });
            sourceZone.Points = 10;
            sourceZone.FaceUpPoints = 10;
            targetZone.Cards.Add(multiplier);
            targetZone.Points = 10;
            targetZone.FaceUpPoints = 10;
            view.Revision = 2;
            view.LegalMoves.Clear();
            view.LegalMoves.Add(new MoveData
            {
                Id = "move-back",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = multiplier.Id,
                SourceZoneId = targetZone.Id,
                TargetPlayerId = 0,
                TargetZoneId = sourceZone.Id
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "end-turn-2",
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            });

            var repeatedMove = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(repeatedMove.Type, Is.EqualTo(EMoveType.EndTurn));

            ++view.TurnNumber;
            ++view.Revision;
            var nextTurnMove = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(nextTurnMove.CardId, Is.EqualTo(multiplier.Id));
        }

        [Test]
        public void StrongAi_LateTurnEndsInsteadOfReorderingExistingPile()
        {
            var view = new PlayerViewData
            {
                TurnNumber = 100,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 99,
                Rules = new RulesConfigData { TotalPointsRequiredToWin = 1000 },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var sourceZone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            };
            sourceZone.Cards.Add(new CardInstanceData
            {
                Id = "multiplier",
                IsFaceUp = true,
                WildMultiplier = 2
            });
            var targetZone = new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Earth,
                Health = 5,
                MaxHealth = 5,
                Points = 5,
                FaceUpPoints = 5
            };
            targetZone.Cards.Add(new CardInstanceData
            {
                Id = "points",
                IsFaceUp = true,
                WildPoints = 5
            });
            view.Player.Zones.Add(sourceZone);
            view.Player.Zones.Add(targetZone);
            view.LegalMoves.Add(new MoveData
            {
                Id = "reorder",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "multiplier",
                SourceZoneId = 0,
                TargetPlayerId = 0,
                TargetZoneId = 1
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "end-turn",
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            });
            IAiPlayer ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.Type, Is.EqualTo(EMoveType.EndTurn));
        }

        [Test]
        public void StrongAi_LateTurnDiscardsLockedHandInsteadOfEndingTurn()
        {
            var view = new PlayerViewData
            {
                TurnNumber = 100,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 99,
                Rules = new RulesConfigData(),
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            view.Player.Hand.Add(new CardInstanceData { Id = "locked-card", IsFaceUp = true });
            view.LegalMoves.Add(new MoveData
            {
                Id = "discard",
                PlayerId = 0,
                Type = EMoveType.DiscardCard,
                CardId = "locked-card",
                SourceZoneId = -1
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "end-turn",
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            });
            IAiPlayer ai = new StrongAiPlayer();

            var move = ai.ChooseMove(view, new DeterministicRandom(17));

            Assert.That(move.Type, Is.EqualTo(EMoveType.DiscardCard));
            Assert.That(move.CardId, Is.EqualTo("locked-card"));
        }

        private static PlayerViewData CreateDualCardDecisionView()
        {
            var view = new PlayerViewData
            {
                Revision = 1,
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 3,
                Rules = new RulesConfigData
                {
                    MovesPerTurn = 3,
                    ElementZoneStartingHealth = 5,
                    TotalPointsRequiredToWin = 1000
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var fireZone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Fire,
                Health = 5,
                MaxHealth = 5,
                Points = 5,
                FaceUpPoints = 5
            };
            fireZone.Cards.Add(new CardInstanceData
            {
                Id = "existing-fire-points",
                IsFaceUp = true,
                WildPoints = 5
            });
            view.Player.Zones.Add(fireZone);
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "dual-multiplier-bomb",
                IsFaceUp = true,
                WildMultiplier = 2,
                WildBomb = 2
            });
            var opponent = new OpponentViewData { Id = 1, Name = "Opponent" };
            opponent.Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            });
            view.Opponents.Add(opponent);
            view.LegalMoves.Add(new MoveData
            {
                Id = "play-dual-as-multiplier",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "dual-multiplier-bomb",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "play-dual-as-bomb",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "dual-multiplier-bomb",
                SourceZoneId = -1,
                TargetPlayerId = 1,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(CreateEndTurnMove("end-dual-decision"));
            return view;
        }

        private static PlayerViewData CreateMultiplierRebuildView()
        {
            var view = CreateRebuildView();
            view.Player.Zones[0].Cards.Add(new CardInstanceData
            {
                Id = "fire-points",
                IsFaceUp = true,
                WildPoints = 4
            });
            RefreshZonePoints(view.Player.Zones[0]);
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "fire-multiplier",
                IsFaceUp = true,
                WildMultiplier = 2
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "strip-fire-points",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "fire-points",
                SourceZoneId = 0,
                TargetPlayerId = 0,
                TargetZoneId = 1
            });
            view.LegalMoves.Add(CreateEndTurnMove("end-multiplier-rebuild"));
            return view;
        }

        private static GameStateData CreateMultiplierOrderingState(bool inMultiplierOnPile,
            int inMovesPerTurn, bool inHasThreat)
        {
            var state = new GameStateData
            {
                MatchId = "strong-ai-multiplier-ordering",
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = inMovesPerTurn > 0 ? inMovesPerTurn : 99,
                WinnerPlayerId = -1,
                Rules = new RulesConfigData
                {
                    NumCardsToDeal = 0,
                    MaxCardsInHand = 7,
                    MovesPerTurn = inMovesPerTurn,
                    ElementZonePointsRequiredToActivate = 100,
                    TotalPointsRequiredToWin = 1000,
                    MaxCardsPerZone = 20,
                    DiscardMode = EDiscardMode.None
                },
                Random = new RandomStateData { State = 1 }
            };
            var ai = new PlayerStateData { Id = 0, Name = "AI" };
            var waterZone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Water,
                Health = 5,
                MaxHealth = 5
            };
            waterZone.Cards.Add(CreateWaterPointCard("water-points-3", 3));
            waterZone.Cards.Add(CreateWaterPointCard("water-points-5", 5));
            if (inMultiplierOnPile)
                waterZone.Cards.Add(CreateWaterMultiplierCard());
            RefreshZonePoints(waterZone);
            ai.Zones.Add(waterZone);
            ai.Zones.Add(new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            });
            if (!inMultiplierOnPile)
                ai.Hand.Add(CreateWaterMultiplierCard());
            ai.Hand.Add(CreateWaterPointCard("water-points-4", 4));
            ai.Hand.Add(CreateWaterShieldCard("water-shield-1", 1));
            ai.Hand.Add(CreateWaterShieldCard("water-shield-3", 3));
            state.Players.Add(ai);

            var opponent = new PlayerStateData { Id = 1, Name = "Opponent" };
            opponent.Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Water,
                Health = 5,
                MaxHealth = 5
            });
            if (inHasThreat)
            {
                for (int i = 0; i < 7; ++i)
                    opponent.Hand.Add(new CardInstanceData { Id = "opponent-card-" + i });
            }
            state.Players.Add(opponent);

            int powerDeckCount = 37;
            for (int i = 0; i < powerDeckCount; ++i)
            {
                state.PowerDeck.Add(new CardInstanceData
                {
                    Id = "power-card-" + i,
                    IsPowerCard = true
                });
            }
            return state;
        }

        private static CardInstanceData CreateWaterPointCard(string inId, int inPoints)
        {
            return new CardInstanceData
            {
                Id = inId,
                IsFaceUp = true,
                Points = new[] { 0, 0, 0, inPoints }
            };
        }

        private static CardInstanceData CreateWaterMultiplierCard()
        {
            return new CardInstanceData
            {
                Id = "water-multiplier-x3",
                IsFaceUp = true,
                Multipliers = new[] { 0, 0, 0, 3 }
            };
        }

        private static CardInstanceData CreateWaterShieldCard(string inId, int inShield)
        {
            return new CardInstanceData
            {
                Id = inId,
                IsFaceUp = true,
                Shields = new[] { 0, 0, 0, inShield }
            };
        }

        private static void PlayStrongAiTurn(GameStateData inState, out List<MoveData> outMoves)
        {
            var simulator = new GameSimulator();
            var ai = new StrongAiPlayer();
            var random = new DeterministicRandom(17);
            outMoves = new List<MoveData>();
            while (inState.CurrentPlayerId == 0 && inState.WinnerPlayerId < 0)
            {
                var move = ai.ChooseMove(simulator.GetPlayerView(inState, 0), random);
                Assert.That(move, Is.Not.Null);
                outMoves.Add(move.Clone());
                simulator.ApplyMove(inState, move);
                Assert.That(outMoves.Count, Is.LessThanOrEqualTo(20),
                    "Strong AI must finish the deterministic turn");
            }
        }

        private static int FindCardIndex(List<CardInstanceData> inCards, string inCardId)
        {
            for (int i = 0; i < inCards.Count; ++i)
            {
                if (inCards[i].Id == inCardId)
                    return i;
            }
            return -1;
        }

        private static PlayerViewData CreateShieldRebuildView()
        {
            var view = CreateRebuildView();
            view.Player.Zones[0].Cards.Add(new CardInstanceData
            {
                Id = "valuable-fire-points",
                IsFaceUp = true,
                WildPoints = 10
            });
            view.Player.Zones[0].Cards.Add(new CardInstanceData
            {
                Id = "cover-card",
                IsFaceUp = true,
                WildPoints = 1
            });
            RefreshZonePoints(view.Player.Zones[0]);
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "protective-shield",
                IsFaceUp = true,
                WildShield = 2
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "strip-cover",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "cover-card",
                SourceZoneId = 0,
                TargetPlayerId = 0,
                TargetZoneId = 1
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "place-shield-directly",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "protective-shield",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(CreateEndTurnMove("end-shield-rebuild"));
            return view;
        }

        private static PlayerViewData CreateRebuildView()
        {
            var view = new PlayerViewData
            {
                Revision = 1,
                TurnNumber = 1,
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 0,
                Rules = new RulesConfigData
                {
                    MovesPerTurn = 0,
                    TotalPointsRequiredToWin = 1000,
                    ElementZonePointsRequiredToActivate = 100
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            view.Player.Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Fire,
                Health = 5,
                MaxHealth = 5
            });
            var stagingZone = new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5
            };
            stagingZone.Cards.Add(new CardInstanceData
            {
                Id = "staging-card",
                IsFaceUp = true,
                WildPoints = 1
            });
            RefreshZonePoints(stagingZone);
            view.Player.Zones.Add(stagingZone);
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent" });
            view.Opponents[0].Zones.Add(new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Water,
                Health = 5,
                MaxHealth = 5
            });
            return view;
        }

        private static MoveData CreateEndTurnMove(string inId)
        {
            return new MoveData
            {
                Id = inId,
                PlayerId = 0,
                Type = EMoveType.EndTurn,
                SourceZoneId = -1,
                TargetPlayerId = -1,
                TargetZoneId = -1
            };
        }

        private static void MoveTopCard(ZoneStateData inSource, ZoneStateData inTarget)
        {
            int topIndex = inSource.Cards.Count - 1;
            var card = inSource.Cards[topIndex];
            inSource.Cards.RemoveAt(topIndex);
            inTarget.Cards.Add(card);
            RefreshZonePoints(inSource);
            RefreshZonePoints(inTarget);
        }

        private static void RefreshZonePoints(ZoneStateData inZone)
        {
            inZone.Points = GameRules.CalculateZonePoints(inZone);
            inZone.FaceUpPoints = GameRules.CalculateZonePoints(inZone, true);
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

        private static PlayerViewData CreateEmergencyShieldView()
        {
            var view = new PlayerViewData
            {
                CurrentPlayerId = 0,
                MovesRemaining = 2,
                Rules = new RulesConfigData
                {
                    ElementZoneStartingHealth = 5,
                    ElementZonePointsRequiredToActivate = 10,
                    TotalPointsRequiredToWin = 100
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var zone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5,
                Points = 20,
                FaceUpPoints = 20
            };
            zone.Cards.Add(new CardInstanceData
            {
                Id = "zone-points",
                DefinitionId = 4,
                IsFaceUp = true,
                Points = new[] { 10, 0, 0, 0 }
            });
            zone.Cards.Add(new CardInstanceData
            {
                Id = "zone-multiplier",
                DefinitionId = 20,
                IsFaceUp = true,
                Multipliers = new[] { 2, 0, 0, 0 }
            });
            view.Player.Zones.Add(zone);
            view.Player.Zones.Add(new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Earth,
                Health = 5,
                MaxHealth = 5
            });
            view.Opponents.Add(new OpponentViewData { Id = 1, Name = "Opponent", HandCount = 10 });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "shield",
                DefinitionId = 215,
                DefinitionName = "Shield",
                WildShield = 1
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "point-15",
                DefinitionId = 5,
                DefinitionName = "Point 15",
                Points = new[] { 0, 15, 0, 0 }
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "shield-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "shield",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "point-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "point-15",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 1
            });
            return view;
        }

        private static PlayerViewData CreateWholeTurnPlanningView()
        {
            var view = new PlayerViewData
            {
                CurrentPlayerId = 0,
                TurnPhase = ETurnPhase.PlayerPlaying,
                MovesRemaining = 2,
                Rules = new RulesConfigData
                {
                    MovesPerTurn = 2,
                    TotalPointsRequiredToWin = 1000,
                    ElementZonePointsRequiredToActivate = 100
                },
                Player = new PlayerStateData { Id = 0, Name = "AI" }
            };
            var airZone = new ZoneStateData
            {
                Id = 0,
                ElementType = EElementType.Air,
                Health = 5,
                MaxHealth = 5,
                Points = 3,
                FaceUpPoints = 3
            };
            airZone.Cards.Add(new CardInstanceData
            {
                Id = "existing-air-points",
                IsFaceUp = true,
                Points = new[] { 3, 0, 0, 0 }
            });
            view.Player.Zones.Add(airZone);
            view.Player.Zones.Add(new ZoneStateData
            {
                Id = 1,
                ElementType = EElementType.Earth,
                Health = 5,
                MaxHealth = 5
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "multiplier",
                IsFaceUp = true,
                Multipliers = new[] { 2, 0, 0, 0 }
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "air-points",
                IsFaceUp = true,
                Points = new[] { 8, 0, 0, 0 }
            });
            view.Player.Hand.Add(new CardInstanceData
            {
                Id = "earth-points",
                IsFaceUp = true,
                Points = new[] { 0, 10, 0, 0 }
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "multiplier-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "multiplier",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "air-points-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "air-points",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 0
            });
            view.LegalMoves.Add(new MoveData
            {
                Id = "earth-points-move",
                PlayerId = 0,
                Type = EMoveType.PlayCard,
                CardId = "earth-points",
                SourceZoneId = -1,
                TargetPlayerId = 0,
                TargetZoneId = 1
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
