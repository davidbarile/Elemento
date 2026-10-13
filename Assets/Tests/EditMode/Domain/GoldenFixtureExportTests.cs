using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Elemento.Domain.Net;
using NUnit.Framework;

namespace Elemento.Domain.Tests
{
    public class GoldenFixtureExportTests
    {
        private const int MoveCap = 400;
        // Default rules produce large full-state snapshots; two moves keep the six-file corpus under 5 MB.
        private const int DefaultRulesMoveCap = 2;

        private static readonly int[] seeds =
        {
            1077,
            1603,
            7331,
            918273,
            101,
            -12345
        };

        [Test]
        public void ExportSeededAiSelfPlayGames_WhenGoldenDirectoryIsConfigured()
        {
            string goldenDirectory = Environment.GetEnvironmentVariable("ELEMENTO_GOLDEN_DIR");
            if (string.IsNullOrWhiteSpace(goldenDirectory))
            {
                Assert.Pass("Set ELEMENTO_GOLDEN_DIR to export golden fixtures.");
                return;
            }

            Directory.CreateDirectory(goldenDirectory);
            for (int gameIndex = 0; gameIndex < seeds.Length; ++gameIndex)
            {
                int seed = seeds[gameIndex];
                int moveCap = gameIndex < 2 ? DefaultRulesMoveCap : MoveCap;
                string json = CreateFixtureJson(seed, CreateRules(gameIndex), moveCap);
                string path = Path.Combine(goldenDirectory, "game-" + gameIndex + "-seed-" + seed + ".json");
                File.WriteAllText(path, json);
            }

            Assert.That(Directory.GetFiles(goldenDirectory, "game-*-seed-*.json").Length,
                Is.GreaterThanOrEqualTo(seeds.Length));
        }

        [Test]
        public void SeededAiSelfPlayFixture_IsDeterministic()
        {
            const int seed = 7331;
            var rules = CreateRules(2);

            string first = CreateFixtureJson(seed, rules);
            string second = CreateFixtureJson(seed, rules);

            Assert.That(second, Is.EqualTo(first));
        }

        private static string CreateFixtureJson(int inSeed, RulesConfigData inRules, int inMoveCap = MoveCap)
        {
            var decks = OnlineProtocolTests.CreateDeckDefinitionsBySeat();
            string matchId = "golden-" + inSeed;
            var state = new MatchFactory().Create(inSeed, inRules, decks, matchId);
            var initialState = state.Clone();
            var authority = new MatchAuthority();
            var simulator = new GameSimulator();
            IAiPlayer[] players =
            {
                new LegacyAiPlayer(EAiDifficulty.Hard),
                new LegacyAiPlayer(EAiDifficulty.Hard)
            };
            var random = new DeterministicRandom(unchecked(inSeed ^ (int)0x9E3779B9u));
            List<GameStateData> stateAfterEachMove = new();

            for (int sequence = 0; sequence < inMoveCap && state.WinCondition == EWinCondition.None; ++sequence)
            {
                int seat = state.CurrentPlayerId;
                var move = players[seat].ChooseMove(simulator.GetPlayerView(state, seat), random);
                Assert.That(move, Is.Not.Null, "AI returned no move before the game ended for seed " + inSeed + ".");

                var result = authority.ValidateAndApply(state, CreateCommand(state, move, sequence));
                Assert.That(result.Accepted, Is.True, result.Reason);
                stateAfterEachMove.Add(state.Clone());
            }

            List<MatchSeatViewData> seatViews = new()
            {
                FairView.BuildSeatView(state, 0),
                FairView.BuildSeatView(state, 1)
            };

            StringBuilder json = new();
            json.Append("{\"seed\":").Append(NetJson.Serialize(inSeed));
            json.Append(",\"matchId\":").Append(NetJson.Serialize(matchId));
            json.Append(",\"rules\":").Append(NetJson.Serialize(inRules));
            json.Append(",\"decks\":").Append(NetJson.Serialize(decks));
            json.Append(",\"initialState\":").Append(NetJson.Serialize(initialState));
            json.Append(",\"moveLog\":").Append(NetJson.Serialize(new List<MoveLogEntryData>(authority.MoveLog)));
            json.Append(",\"stateAfterEachMove\":").Append(NetJson.Serialize(stateAfterEachMove));
            json.Append(",\"finalState\":").Append(NetJson.Serialize(state));
            json.Append(",\"seatViews\":").Append(NetJson.Serialize(seatViews));
            json.Append('}');
            return json.ToString();
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
                ClientNonce = "golden-" + inSequence
            };
        }

        private static RulesConfigData CreateRules(int inGameIndex)
        {
            if (inGameIndex < 2)
                return new RulesConfigData();

            return new RulesConfigData
            {
                NumCardsToDeal = 3 + inGameIndex % 2,
                MovesPerTurn = 1 + inGameIndex % 3,
                TotalPointsRequiredToWin = 1,
                ElementZonePointsRequiredToActivate = inGameIndex % 2 == 0 ? 100 : 8
            };
        }
    }
}
