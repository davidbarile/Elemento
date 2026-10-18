using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Elemento.Domain;
using Elemento.Domain.Net;

namespace Elemento.Simulation
{
    internal sealed class OnlineSimulationRunner
    {
        private readonly SimulationOptions options;
        private readonly GameSimulator simulator = new();
        private readonly HashSet<string> ruleVariants = new();
        private long turns;
        private long moves;
        private long undos;
        private long activations;
        private long powerPlays;
        private int finished;
        private int capped;
        private int mismatches;

        public OnlineSimulationRunner(SimulationOptions inOptions)
        {
            this.options = inOptions;
        }

        public async Task RunAsync()
        {
            int played = 0;
            var elapsed = Stopwatch.StartNew();
            try
            {
                if (!this.options.Transport)
                {
                    object gate = new();
                    Parallel.For(0, this.options.Games,
                        new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, gameIndex =>
                        {
                            var runner = new OnlineSimulationRunner(this.options);
                            try { runner.RunGameAsync(gameIndex).GetAwaiter().GetResult(); }
                            catch (Exception exception)
                            {
                                throw new InvalidOperationException($"game={gameIndex + 1} seed={unchecked(this.options.Seed + gameIndex * 7919)}: {exception.Message}", exception);
                            }
                            lock (gate)
                            {
                                this.turns += runner.turns;
                                this.moves += runner.moves;
                                this.undos += runner.undos;
                                this.activations += runner.activations;
                                this.powerPlays += runner.powerPlays;
                                this.finished += runner.finished;
                                this.capped += runner.capped;
                                this.ruleVariants.UnionWith(runner.ruleVariants);
                                ++played;
                                if (played % 1000 == 0)
                                    Console.WriteLine("progress games={0} turns={1} mismatches={2} elapsed-seconds={3:F2}",
                                        played, this.turns, this.mismatches, elapsed.Elapsed.TotalSeconds);
                            }
                        });
                }
                else
                {
                    for (; played < this.options.Games; ++played)
                        await RunGameAsync(played);
                }
            }
            catch (Exception exception)
            {
                ++this.mismatches;
                string context = this.options.Transport
                    ? $"game={played + 1} seed={unchecked(this.options.Seed + played * 7919)} " : string.Empty;
                throw new InvalidOperationException($"Online parity failure: {context}turns={this.turns}: {exception.Message}", exception);
            }
            finally
            {
                Console.WriteLine("online games={0} turns={1} mismatches={2} seed={3} transport={4}",
                    played, this.turns, this.mismatches, this.options.Seed, this.options.Transport);
                Console.WriteLine("turn-cap={0} per game (capped games are reported separately)", this.options.Cap);
                Console.WriteLine("finished={0} capped={1} moves={2} undo={3} activations={4} power-plays={5}",
                    this.finished, this.capped, this.moves, this.undos, this.activations, this.powerPlays);
                Console.WriteLine("rule-variants={0}/72 (select-power x discard-mode x moves-per-turn x undo)", this.ruleVariants.Count);
                Console.WriteLine("elapsed-seconds={0:F2}", elapsed.Elapsed.TotalSeconds);
            }
        }

        private async Task RunGameAsync(int inGameIndex)
        {
            int seed = unchecked(this.options.Seed + inGameIndex * 7919);
            var random = new Random(seed);
            var request = InitialMatchFactory.Create(CreateRules(inGameIndex), seed);
            // A compact shuffled reserve exercises recycling frequently while keeping thousands of JSON checks practical.
            if (request.InitialState.ReserveDeck.Count > 32)
                request.InitialState.ReserveDeck.RemoveRange(32, request.InitialState.ReserveDeck.Count - 32);
            request.InitialStateHash = GameStateHasher.Compute(request.InitialState);
            this.ruleVariants.Add($"{request.Rules.AllowSelectPowerCardType}:{request.Rules.DiscardMode}:{request.Rules.MovesPerTurn}:{request.Rules.AllowUndo}");
            var active = request.InitialState;
            OnlineMatchClient[] clients = null;
            if (this.options.Transport)
            {
                var host = new InMemoryMatchTransport("Device A");
                clients = new[] { new OnlineMatchClient(host), new OnlineMatchClient(host.CreatePeer("Device B")) };
                await clients[0].CreateAsync(request);
                await clients[1].JoinAsync(clients[0].MatchId);
                await clients[0].RefreshAsync();
                active = clients[0].CurrentState.State.Clone();
            }
            var remote = RoundTrip(active);
            int normalCardCount = Cards(active).Count(card => !card.IsPowerCard);
            int revision = 0;
            for (int turn = 0; turn < this.options.Cap && !this.simulator.IsGameOver(active); ++turn)
            {
                int seat = active.CurrentPlayerId;
                var recorder = new TurnRecorder(active, revision);
                int decisions = 0;
                while (!recorder.IsComplete)
                {
                    if (recorder.CanUndo && random.Next(8) == 0 && decisions < 128)
                    {
                        recorder.Undo();
                        ++this.undos;
                        ++decisions;
                        AssertIdentity(recorder.State, normalCardCount);
                        continue;
                    }
                    var legal = this.simulator.GetLegalMoves(recorder.State, seat);
                    if (legal.Count == 0)
                        throw new InvalidOperationException($"No legal move can complete the turn: seat={seat}, hand={recorder.State.Players[seat].Hand.Count}, deck={recorder.State.Deck.Count}, reserve={recorder.State.ReserveDeck.Count}, discard={recorder.State.Rules.DiscardMode}.");
                    // Bound pathological unlimited-move reorder loops, keeping all ordinary selections uniform.
                    var move = decisions++ < 128 ? legal[random.Next(legal.Count)] : FinishingMove(legal);
                    if (move.Type == EMoveType.ActivateZone)
                        ++this.activations;
                    if (move.Type == EMoveType.PlayCard && Cards(recorder.State).Any(card => card.Id == move.CardId && card.IsPowerCard))
                        ++this.powerPlays;
                    recorder.ApplyMove(move);
                    ++this.moves;
                    AssertIdentity(recorder.State, normalCardCount);
                }

                var package = recorder.BuildPackage();
                active = recorder.State;
                TurnPackageData received = RoundTrip(package);
                if (clients != null)
                {
                    var result = await clients[seat].SubmitTurnAsync(package, $"game-{inGameIndex}-turn-{revision}");
                    if (!result.Accepted || result.NewRevision != revision + 1)
                        throw new InvalidOperationException("Transport rejected a valid turn: " + result.Reason);
                    // Request explicitly from this turn's revision even if a notification already refreshed the client.
                    await clients[1 - seat].RefreshAsync(revision);
                    var update = clients[1 - seat].CurrentState;
                    received = update.Turns.Single(item => item.StartRevision == revision);
                    AssertEqual(active, update.State, "transport committed state");
                }
                var replay = new TurnReplayer().Replay(remote, received);
                if (!replay.IsMatch)
                    throw new InvalidOperationException("Replay mismatch: " + replay.Reason);
                remote = replay.State;
                AssertEqual(active, remote, "replayed state");
                AssertIdentity(remote, normalCardCount);
                Identity(active, out var expectedIds, out int expectedAnonymous);
                Identity(remote, out var actualIds, out int actualAnonymous);
                if (!CardIdentityValidator.TryValidate(expectedIds, expectedAnonymous, actualIds, actualAnonymous, out string error))
                    throw new InvalidOperationException(error);
                ++revision;
                ++this.turns;
            }
            if (this.simulator.IsGameOver(active))
                ++this.finished;
            else
                ++this.capped;
            if (clients != null)
                foreach (var client in clients)
                    client.Dispose();
        }

        private static RulesConfigData CreateRules(int inGameIndex)
        {
            return new RulesConfigData
            {
                AllowSelectPowerCardType = inGameIndex % 2 == 0,
                DiscardMode = (EDiscardMode)(inGameIndex / 2 % 3),
                MovesPerTurn = inGameIndex / 6 % 6,
                AllowUndo = inGameIndex / 36 % 2 == 0,
                ElementZoneStartingHealth = inGameIndex / 72 % 3 == 0 ? 0 : 5,
                ElementZonePointsRequiredToActivate = 3 + inGameIndex % 8,
                TotalPointsRequiredToWin = 8 + inGameIndex % 4,
                NumCardsToDeal = 3 + inGameIndex % 4,
                MaxCardsInHand = 7,
                CardSpawnBatchSize = 3 + inGameIndex % 8,
                FrozenZoneHealth = 1 + inGameIndex % 5,
                LightningDamage = 1 + inGameIndex % 5,
                FrozenZonesMeltAmountPerTurn = inGameIndex % 3,
                MaxCardsPerZone = 3 + inGameIndex % 8
            };
        }

        private static MoveData FinishingMove(List<MoveData> inMoves)
        {
            return inMoves.FirstOrDefault(move => move.Type == EMoveType.EndTurn)
                ?? inMoves.FirstOrDefault(move => move.Type == EMoveType.DiscardCard)
                ?? throw new InvalidOperationException("No legal way to end a long turn.");
        }

        private static void AssertEqual(GameStateData inExpected, GameStateData inActual, string inContext)
        {
            if (GameStateHasher.Compute(inExpected) != GameStateHasher.Compute(inActual)
                || GameStateHasher.CanonicalJson(inExpected) != GameStateHasher.CanonicalJson(inActual))
                throw new InvalidOperationException(inContext + " differs in hash or full canonical JSON.");
        }

        private static void AssertIdentity(GameStateData inState, int inNormalCardCount)
        {
            if (inState.Deck.Concat(inState.DiscardPile)
                .Concat(inState.Players.SelectMany(player => player.Hand.Concat(player.Zones.SelectMany(zone => zone.Cards))))
                .Any(card => string.IsNullOrEmpty(card.Id)))
                throw new InvalidOperationException("A live card is missing its instance id.");
            Identity(inState, out var ids, out int anonymous);
            if (!CardIdentityValidator.TryValidate(ids, anonymous, ids, anonymous, out string error))
                throw new InvalidOperationException(error);
            // Recycling retires an instance id into an anonymous template; normal cards must still be conserved.
            if (Cards(inState).Count(card => !card.IsPowerCard) != inNormalCardCount)
                throw new InvalidOperationException("A normal card was duplicated or lost.");
        }

        private static void Identity(GameStateData inState, out List<string> outIds, out int outAnonymous)
        {
            outIds = new();
            outAnonymous = 0;
            foreach (var card in Cards(inState))
            {
                if (string.IsNullOrEmpty(card.Id))
                    ++outAnonymous;
                else
                    outIds.Add(card.Id);
            }
        }

        private static IEnumerable<CardInstanceData> Cards(GameStateData inState)
        {
            return inState.Deck.Concat(inState.ReserveDeck).Concat(inState.PowerDeck).Concat(inState.DiscardPile)
                .Concat(inState.Players.SelectMany(player => player.Hand.Concat(player.Zones.SelectMany(zone => zone.Cards))));
        }

        private static T RoundTrip<T>(T inValue)
        {
            return NetJson.Deserialize<T>(NetJson.Serialize(inValue));
        }
    }
}
