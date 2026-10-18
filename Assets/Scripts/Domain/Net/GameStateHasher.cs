using System;
using System.Security.Cryptography;
using System.Text;

namespace Elemento.Domain.Net
{
    public static class GameStateHasher
    {
        public static string CanonicalJson(GameStateData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));
            // NetJson orders public fields ordinally and preserves ordered collections.
            return NetJson.Serialize(inState);
        }

        public static string Compute(GameStateData inState)
        {
            using var hash = SHA256.Create();
            byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(CanonicalJson(inState)));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
