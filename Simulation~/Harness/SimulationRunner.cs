using System;
using System.Collections.Generic;
using System.Diagnostics;
using Elemento.Domain;

namespace Elemento.Simulation
{
    internal sealed class SimulationRunner
    {
        private readonly SimulationOptions options;
        private readonly AgentStats playerOneStats;
        private readonly AgentStats playerTwoStats;
        private readonly List<int> gameLengths = new();

        private int firstPlayerWins;
        private int pointsWins;
        private int zonesDestroyedWins;
        private int draws;
        private int timeouts;

        public SimulationRunner(SimulationOptions inOptions)
        {
            this.options = inOptions;
            this.playerOneStats = new AgentStats("P1", inOptions.PlayerOne);
            this.playerTwoStats = new AgentStats("P2", inOptions.PlayerTwo);
        }

        public void Run()
        {
            for (int gameIndex = 0; gameIndex < this.options.Games; ++gameIndex)
                RunGame(gameIndex);

            PrintReport();
        }

        private void RunGame(int inGameIndex)
        {
            int gameSeed = unchecked(this.options.Seed + inGameIndex * 7919);
            bool playerOneStarts = inGameIndex % 2 == 0;
            var seatZeroStats = playerOneStarts ? this.playerOneStats : this.playerTwoStats;
            var seatOneStats = playerOneStarts ? this.playerTwoStats : this.playerOneStats;
            var statsBySeat = new[] { seatZeroStats, seatOneStats };
            var aiBySeat = new[] { CreateAi(seatZeroStats.Kind), CreateAi(seatOneStats.Kind) };
            var simulator = new GameSimulator();
            var state = simulator.CreateGame(gameSeed);
            var decisionRandom = new DeterministicRandom(unchecked(gameSeed ^ (int)0x9E3779B9u));
            int playerTurns = 0;

            ++this.playerOneStats.Games;
            ++this.playerTwoStats.Games;

            while (!simulator.IsGameOver(state) && playerTurns < this.options.Cap)
            {
                int actingSeat = state.CurrentPlayerId;
                int decisionBudget = GetDecisionBudget(simulator.GetPlayerView(state, actingSeat));

                for (int decision = 0; decision < decisionBudget
                    && !simulator.IsGameOver(state)
                    && state.CurrentPlayerId == actingSeat; ++decision)
                {
                    var view = simulator.GetPlayerView(state, actingSeat);
                    long startedAt = Stopwatch.GetTimestamp();
                    var move = aiBySeat[actingSeat].ChooseMove(view, decisionRandom);
                    statsBySeat[actingSeat].DecisionTicks += Stopwatch.GetTimestamp() - startedAt;
                    ++statsBySeat[actingSeat].DecisionCalls;
                    if (move == null)
                        break;

                    ++statsBySeat[actingSeat].Decisions;
                    BadPatternDetector.Record(view, move, statsBySeat[actingSeat],
                        inGameIndex + 1, gameSeed, playerTurns + 1);
                    simulator.ApplyMove(state, move);
                }

                if (!simulator.IsGameOver(state) && state.CurrentPlayerId == actingSeat)
                    ForceTurnAdvance(simulator, state);

                ++playerTurns;
            }

            this.gameLengths.Add(playerTurns);
            if (!simulator.IsGameOver(state))
            {
                ++this.timeouts;
                return;
            }

            int winnerSeat = simulator.Winner(state);
            if (winnerSeat < 0)
            {
                ++this.draws;
                return;
            }

            ++statsBySeat[winnerSeat].Wins;
            if (winnerSeat == 0)
                ++this.firstPlayerWins;

            if (simulator.WinType(state) == EWinCondition.PointsTarget)
                ++this.pointsWins;
            else if (simulator.WinType(state) == EWinCondition.AllZonesDestroyed)
                ++this.zonesDestroyedWins;
        }

        private static int GetDecisionBudget(PlayerViewData inView)
        {
            int possibleMoves = inView.Player.Hand.Count;
            foreach (var zone in inView.Player.Zones)
            {
                if (!zone.IsFrozen && !zone.IsDestroyed && zone.Cards.Count > 0)
                    ++possibleMoves;
            }

            int offset = inView.Rules.DiscardMode == EDiscardMode.DiscardEndsTurn ? 1 : 0;
            return Math.Max(0, Math.Min(possibleMoves, inView.MovesRemaining + offset));
        }

        private static void ForceTurnAdvance(GameSimulator inSimulator, GameStateData inState)
        {
            var legalMoves = inSimulator.GetLegalMoves(inState, inState.CurrentPlayerId);
            foreach (var move in legalMoves)
            {
                if (move.Type == EMoveType.EndTurn)
                {
                    inSimulator.ApplyMove(inState, move);
                    return;
                }
            }

            foreach (var move in legalMoves)
            {
                if (move.Type == EMoveType.DiscardCard)
                {
                    inSimulator.ApplyMove(inState, move);
                    return;
                }
            }

            throw new InvalidOperationException("The game has no legal way to advance the turn.");
        }

        private static IAiPlayer CreateAi(string inKind)
        {
            if (inKind == "random")
                return new RandomLegalAi();
            if (inKind == "easy")
                return new LegacyAiPlayer(EAiDifficulty.Easy);
            if (inKind == "medium")
                return new LegacyAiPlayer(EAiDifficulty.Medium);
            if (inKind == "strong" || inKind == "strong-hard")
                return new StrongAiPlayer();
            if (inKind == "strong-medium")
            {
                return new StrongAiPlayer(new StrongAiOptions
                {
                    Difficulty = EAiDifficulty.Medium
                });
            }
            if (inKind == "strong-easy")
            {
                return new StrongAiPlayer(new StrongAiOptions
                {
                    Difficulty = EAiDifficulty.Easy
                });
            }
            return new LegacyAiPlayer(EAiDifficulty.Hard);
        }

        private void PrintReport()
        {
            Console.WriteLine("Elemento AI simulation");
            Console.WriteLine("games={0} seed={1} p1={2} p2={3} cap={4} player-turns",
                this.options.Games, this.options.Seed, this.options.PlayerOne,
                this.options.PlayerTwo, this.options.Cap);
            Console.WriteLine("Seat assignment alternates every game; seat 0 is the first player.");
            Console.WriteLine();
            PrintAgent(this.playerOneStats);
            PrintAgent(this.playerTwoStats);
            Console.WriteLine("First-player wins: {0}/{1} = {2}", this.firstPlayerWins,
                this.options.Games, FormatRate(this.firstPlayerWins, this.options.Games));
            Console.WriteLine("Game length: avg {0:F2}, median {1:F1} player-turns",
                Average(this.gameLengths), Median(this.gameLengths));
            Console.WriteLine("Win types: points={0}, zones-destroyed={1}, draw={2}, timeout-cap={3}",
                this.pointsWins, this.zonesDestroyedWins, this.draws, this.timeouts);
            Console.WriteLine();
            Console.WriteLine("Bad patterns per 1000 decisions:");
            PrintPatterns(this.playerOneStats);
            PrintPatterns(this.playerTwoStats);
            PrintExamples(this.playerOneStats);
            PrintExamples(this.playerTwoStats);
        }

        private static void PrintAgent(AgentStats inStats)
        {
            Console.WriteLine("{0} ({1}) wins: {2}/{3} = {4}", inStats.Label, inStats.Kind,
                inStats.Wins, inStats.Games, FormatRate(inStats.Wins, inStats.Games));
            Console.WriteLine("{0} ({1}) AI time: {2:F4} ms/decision ({3} calls)", inStats.Label,
                inStats.Kind, MillisecondsPerDecision(inStats), inStats.DecisionCalls);
        }

        private static void PrintPatterns(AgentStats inStats)
        {
            Console.WriteLine("{0} ({1}): decisions={2}, BombOverShield={3} ({4:F3}), WeakPointOverBomb={5} ({6:F3})",
                inStats.Label, inStats.Kind, inStats.Decisions, inStats.BombOverShield,
                PerThousand(inStats.BombOverShield, inStats.Decisions), inStats.WeakPointOverBomb,
                PerThousand(inStats.WeakPointOverBomb, inStats.Decisions));
        }

        private static void PrintExamples(AgentStats inStats)
        {
            if (inStats.Examples.Count == 0)
                return;
            Console.WriteLine("{0} ({1}) examples:", inStats.Label, inStats.Kind);
            foreach (string example in inStats.Examples)
                Console.WriteLine("  " + example);
        }

        private static string FormatRate(int inSuccesses, int inTotal)
        {
            if (inTotal == 0)
                return "n/a";

            const double z = 1.96;
            double rate = (double)inSuccesses / inTotal;
            double denominator = 1.0 + z * z / inTotal;
            double center = (rate + z * z / (2.0 * inTotal)) / denominator;
            double margin = z * Math.Sqrt(rate * (1.0 - rate) / inTotal
                + z * z / (4.0 * inTotal * inTotal)) / denominator;
            return string.Format("{0:P2} (95% CI {1:P2}..{2:P2})", rate,
                Math.Max(0.0, center - margin), Math.Min(1.0, center + margin));
        }

        private static double PerThousand(int inCount, int inDecisions)
        {
            return inDecisions == 0 ? 0.0 : inCount * 1000.0 / inDecisions;
        }

        private static double MillisecondsPerDecision(AgentStats inStats)
        {
            return inStats.DecisionCalls == 0
                ? 0.0
                : inStats.DecisionTicks * 1000.0 / Stopwatch.Frequency / inStats.DecisionCalls;
        }

        private static double Average(List<int> inValues)
        {
            long total = 0;
            foreach (int value in inValues)
                total += value;
            return inValues.Count == 0 ? 0.0 : (double)total / inValues.Count;
        }

        private static double Median(List<int> inValues)
        {
            if (inValues.Count == 0)
                return 0.0;
            var sorted = new List<int>(inValues);
            sorted.Sort();
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }
    }

    internal sealed class AgentStats
    {
        public string Label;
        public string Kind;
        public int Games;
        public int Wins;
        public int Decisions;
        public int DecisionCalls;
        public long DecisionTicks;
        public int BombOverShield;
        public int WeakPointOverBomb;
        public List<string> Examples = new();

        public AgentStats(string inLabel, string inKind)
        {
            this.Label = inLabel;
            this.Kind = inKind;
        }
    }
}
