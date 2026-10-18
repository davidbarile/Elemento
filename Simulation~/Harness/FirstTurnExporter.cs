using System;
using System.IO;
using Elemento.Domain.Net;

namespace Elemento.Simulation
{
    internal static class FirstTurnExporter
    {
        private const string MatchIdPlaceholder = "MATCH_ID";
        private const int Seed = 2;

        public static void Run(string[] inArgs)
        {
            if (inArgs.Length != 3 || inArgs[1] != "--out"
                || string.IsNullOrWhiteSpace(inArgs[2]))
            {
                throw new ArgumentException(
                    "export-first-turn requires --out <directory>.");
            }

            string outputDirectory = Path.GetFullPath(inArgs[2]);
            Directory.CreateDirectory(outputDirectory);

            CreateMatchRequestData createRequest = InitialMatchFactory.Create(null, Seed);
            if (createRequest.InitialState.CurrentPlayerId != 0)
                throw new InvalidOperationException("Fixture seed must start with creator seat 0.");
            createRequest.InitialState.MatchId = MatchIdPlaceholder;
            createRequest.InitialStateHash = GameStateHasher.Compute(createRequest.InitialState);

            var recorder = new TurnRecorder(createRequest.InitialState.Clone(), 0);
            TurnPackageData turn = recorder.EndTurn();
            var submitRequest = new SubmitTurnRequestData
            {
                MatchId = MatchIdPlaceholder,
                Seat = 0,
                ExpectedRevision = 0,
                ClientNonce = MatchIdPlaceholder + ":0:0",
                Turn = turn
            };

            string createPath = Path.Combine(outputDirectory, "create-match.json");
            string submitPath = Path.Combine(outputDirectory, "submit-turn.json");
            File.WriteAllText(createPath, NetJson.Serialize(createRequest) + Environment.NewLine);
            File.WriteAllText(submitPath, NetJson.Serialize(submitRequest) + Environment.NewLine);
            Console.WriteLine(createPath);
            Console.WriteLine(submitPath);
        }
    }
}
