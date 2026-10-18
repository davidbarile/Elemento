using System;
using System.Security.Cryptography;

namespace Elemento.Domain.Net
{
    public static class InitialMatchFactory
    {
        public static CreateMatchRequestData Create(RulesConfigData inRules = null, int? inSeed = null)
        {
            int seed;
            if (inSeed.HasValue)
                seed = inSeed.Value;
            else
            {
                byte[] bytes = new byte[4];
                using var random = RandomNumberGenerator.Create();
                random.GetBytes(bytes);
                seed = BitConverter.ToInt32(bytes, 0);
            }
            var coin = new DeterministicRandom(seed);
            var state = new GameSimulator().CreateGame(seed, inRules, coin.NextInt(2));
            return new CreateMatchRequestData
            {
                ProtocolVersion = ProtocolVersions.Current,
                Rules = state.Rules.Clone(),
                InitialState = state,
                InitialStateHash = GameStateHasher.Compute(state)
            };
        }
    }
}
