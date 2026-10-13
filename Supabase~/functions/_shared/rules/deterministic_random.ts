import { cloneRandom, RandomStateData } from "./game_state_data.ts";

const ZERO_SEED_STATE = 0x6d2b79f5;

export class DeterministicRandom {
  private state: RandomStateData;

  constructor(inSeedOrState: number | RandomStateData) {
    if (typeof inSeedOrState === "number") {
      const seed = inSeedOrState >>> 0;
      this.state = Object.assign(new RandomStateData(), {
        State: seed === 0 ? ZERO_SEED_STATE : seed,
        Cursor: 0,
      });
    } else {
      if (inSeedOrState == null) throw new TypeError("inState");
      this.state = cloneRandom(inSeedOrState);
      if (this.state.State === 0) this.state.State = ZERO_SEED_STATE;
    }
  }

  NextUInt(): number {
    let value = this.state.State >>> 0;
    value = (value ^ (value << 13)) >>> 0;
    value = (value ^ (value >>> 17)) >>> 0;
    value = (value ^ (value << 5)) >>> 0;
    this.state.State = value;
    this.state.Cursor = (this.state.Cursor + 1) | 0;
    return value;
  }

  NextInt(inMaxExclusive: number): number {
    if (inMaxExclusive <= 0) throw new RangeError("inMaxExclusive");
    const bound = inMaxExclusive >>> 0;
    const threshold = ((0 - bound) >>> 0) % bound;
    while (true) {
      const value = this.NextUInt();
      if (value >= threshold) return value % bound;
    }
  }

  NextDouble(): number {
    return this.NextUInt() / 4294967296.0;
  }

  GetState(): RandomStateData {
    return cloneRandom(this.state);
  }
}

export function deterministicShuffle<T>(inItems: T[], inRandom: DeterministicRandom): void {
  if (inItems == null) throw new TypeError("inItems");
  if (inRandom == null) throw new TypeError("inRandom");
  for (let i = inItems.length - 1; i > 0; --i) {
    const swapIndex = inRandom.NextInt(i + 1);
    [inItems[i], inItems[swapIndex]] = [inItems[swapIndex], inItems[i]];
  }
}

export const DeterministicShuffle = { Shuffle: deterministicShuffle };
