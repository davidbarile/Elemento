using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public sealed class DeterministicRandom : IRandom
    {
        private const uint ZeroSeedState = 0x6D2B79F5u;

        private RandomStateData state;

        public DeterministicRandom(int inSeed)
        {
            uint seed = unchecked((uint)inSeed);
            this.state = new RandomStateData
            {
                State = seed == 0 ? ZeroSeedState : seed,
                Cursor = 0
            };
        }

        public DeterministicRandom(RandomStateData inState)
        {
            if (inState == null)
                throw new ArgumentNullException(nameof(inState));

            this.state = inState.Clone();
            if (this.state.State == 0)
                this.state.State = ZeroSeedState;
        }

        public uint NextUInt()
        {
            uint value = this.state.State;
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;

            this.state.State = value;
            ++this.state.Cursor;

            return value;
        }

        public int NextInt(int inMaxExclusive)
        {
            if (inMaxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(inMaxExclusive));

            uint bound = (uint)inMaxExclusive;
            uint threshold = unchecked(0u - bound) % bound;

            while (true)
            {
                uint value = NextUInt();
                if (value >= threshold)
                    return (int)(value % bound);
            }
        }

        public double NextDouble()
        {
            return NextUInt() / 4294967296.0;
        }

        public RandomStateData GetState()
        {
            return this.state.Clone();
        }
    }

    public static class DeterministicShuffle
    {
        public static void Shuffle<T>(IList<T> inItems, DeterministicRandom inRandom)
        {
            if (inItems == null)
                throw new ArgumentNullException(nameof(inItems));
            if (inRandom == null)
                throw new ArgumentNullException(nameof(inRandom));

            for (int i = inItems.Count - 1; i > 0; --i)
            {
                int swapIndex = inRandom.NextInt(i + 1);
                T item = inItems[i];
                inItems[i] = inItems[swapIndex];
                inItems[swapIndex] = item;
            }
        }
    }
}
