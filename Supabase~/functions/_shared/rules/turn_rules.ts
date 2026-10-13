export const EGameMode = { AgainstAI: 0, PassAndPlay: 1, Multiplayer: 2 } as const;
export type EGameMode = typeof EGameMode[keyof typeof EGameMode];
export const ESeatType = { Human: 0, Ai: 1 } as const;
export type ESeatType = typeof ESeatType[keyof typeof ESeatType];

export class TurnStateData {
  PreviousPlayerIndex = -1;
  CurrentPlayerIndex = 0;
  IsHandoffPending = false;
}

export class TurnRules {
  static GetNextPlayerIndex(current: number, count: number): number {
    if (count < 1 || current < 0 || current >= count) throw new RangeError("player index/count");
    return (current + 1) % count;
  }
  static GetSeatType(mode: EGameMode, index: number): ESeatType {
    if (index < 0) throw new RangeError("inPlayerIndex");
    if (mode === EGameMode.PassAndPlay) return ESeatType.Human;
    return index === 0 ? ESeatType.Human : ESeatType.Ai;
  }
  static IsHumanSeat(mode: EGameMode, index: number): boolean {
    return this.GetSeatType(mode, index) === ESeatType.Human;
  }
  static IsHandVisible(mode: EGameMode, hand: number, viewer: number, pending: boolean): boolean {
    if (hand < 0 || viewer < 0) throw new RangeError("player index");
    if (mode === EGameMode.PassAndPlay) return !pending && hand === viewer;
    return hand === 0 && viewer === 0;
  }
  static RequiresHandoff(mode: EGameMode, previous: number, next: number): boolean {
    return mode === EGameMode.PassAndPlay && previous >= 0 && previous !== next;
  }
}
