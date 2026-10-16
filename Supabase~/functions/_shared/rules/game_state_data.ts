export const EElementType = {
  None: 0,
  Air: 1,
  Earth: 2,
  Fire: 3,
  Water: 4,
  Wild: 5,
} as const;
export type EElementType = typeof EElementType[keyof typeof EElementType];

export const ETurnPhase = {
  PlayerDraw: 0,
  PlayerPlaying: 1,
  OpponentDraw: 2,
  OpponentPlaying: 3,
  None: 4,
} as const;
export type ETurnPhase = typeof ETurnPhase[keyof typeof ETurnPhase];

export const EMoveType = {
  None: 0,
  DrawCard: 1,
  PlayCard: 2,
  DiscardCard: 3,
  ActivateZone: 4,
  EndTurn: 5,
} as const;
export type EMoveType = typeof EMoveType[keyof typeof EMoveType];

export const EWinCondition = { None: 0, PointsTarget: 1, AllZonesDestroyed: 2 } as const;
export type EWinCondition = typeof EWinCondition[keyof typeof EWinCondition];

export const EDiscardMode = { None: 0, AllowDiscard: 1, DiscardEndsTurn: 2 } as const;
export type EDiscardMode = typeof EDiscardMode[keyof typeof EDiscardMode];

export const EPowerCardType = { None: 0, Heal: 1, Freeze: 2, Lightning: 3 } as const;
export type EPowerCardType = typeof EPowerCardType[keyof typeof EPowerCardType];

export class RulesConfigData {
  NumCardsToDeal = 5;
  MaxCardsInHand = 7;
  MovesPerTurn = 0;
  ElementZoneStartingHealth = 5;
  FrozenZoneHealth = 5;
  LightningDamage = 5;
  ElementZonePointsRequiredToActivate = 10;
  TotalPointsRequiredToWin = 50;
  FrozenZonesMeltAmountPerTurn = 1;
  MaxCardsPerZone = 20;
  CardSpawnBatchSize = 10;
  AllowSelectPowerCardType = true;
  DiscardMode: EDiscardMode = EDiscardMode.AllowDiscard;

  Clone(): RulesConfigData {
    return cloneRules(this);
  }
}

export class RandomStateData {
  State = 0;
  Cursor = 0;

  Clone(): RandomStateData {
    return Object.assign(new RandomStateData(), { State: this.State >>> 0, Cursor: this.Cursor | 0 });
  }
}

export class CardInstanceData {
  Id: string | null = null;
  DefinitionId = 0;
  DefinitionName: string | null = null;
  IsFaceUp = false;
  IsPowerCard = false;
  WildPoints = 0;
  WildMultiplier = 0;
  WildBomb = 0;
  WildShield = 0;
  WildZoneHeal = 0;
  WildFreeze = 0;
  WildLightning = 0;
  Points: number[] = [0, 0, 0, 0];
  Multipliers: number[] = [0, 0, 0, 0];
  Bombs: number[] = [0, 0, 0, 0];
  Shields: number[] = [0, 0, 0, 0];

  static CreateId(inSequence: number): string {
    return `card-${formatDecimal8(inSequence)}`;
  }

  static ReserveSequence(inNextSequence: number, inCardId: string | null): number {
    const nextSequence = Math.max(0, inNextSequence | 0);
    const match = /^card-(\d+)$/.exec(inCardId ?? "");
    if (match == null) return nextSequence;
    const sequence = Number.parseInt(match[1], 10);
    return sequence >= nextSequence ? sequence + 1 : nextSequence;
  }

  Clone(): CardInstanceData {
    return cloneCard(this);
  }
}

export class ZoneStateData {
  Id = 0;
  ElementType: EElementType = EElementType.None;
  Points = 0;
  FaceUpPoints = 0;
  Health = 0;
  MaxHealth = 0;
  FrozenHealth = 0;
  IsActivated = false;
  IsDestroyed = false;
  Cards: CardInstanceData[] = [];

  get IsFrozen(): boolean {
    return this.FrozenHealth > 0;
  }

  Clone(): ZoneStateData {
    return cloneZone(this);
  }
}

export class PlayerStateData {
  Id = 0;
  Name: string | null = null;
  Hand: CardInstanceData[] = [];
  Zones: ZoneStateData[] = [];

  Clone(): PlayerStateData {
    return clonePlayer(this);
  }
}

export class MoveData {
  Id: string | null = null;
  PlayerId = 0;
  Type: EMoveType = EMoveType.None;
  CardId: string | null = null;
  SourceZoneId = 0;
  TargetPlayerId = 0;
  TargetZoneId = 0;
  PowerCardType: EPowerCardType = EPowerCardType.None;
  ExpectedRevision = 0;

  static CreateId(inSequence: number): string {
    return `move-${formatDecimal8(inSequence)}`;
  }

  Clone(): MoveData {
    return cloneMove(this);
  }
}

export class GameStateData {
  SchemaVersion = 1;
  RulesVersion = 1;
  MatchId: string | null = null;
  Seed = 0;
  Revision = 0;
  TurnNumber = 0;
  StartingPlayerId = 0;
  CurrentPlayerId = 0;
  TurnPhase: ETurnPhase = ETurnPhase.None;
  MovesRemaining = 0;
  WinnerPlayerId = 0;
  WinCondition: EWinCondition = EWinCondition.None;
  NextCardInstanceId = 0;
  Rules = new RulesConfigData();
  Random = new RandomStateData();
  Players: PlayerStateData[] = [];
  Deck: CardInstanceData[] = [];
  ReserveDeck: CardInstanceData[] = [];
  PowerDeck: CardInstanceData[] = [];
  DiscardPile: CardInstanceData[] = [];
  Moves: MoveData[] = [];

  GetNextCardInstanceSequence(): number {
    let nextSequence = Math.max(0, this.NextCardInstanceId | 0);
    const reserveCards = (cards: CardInstanceData[]): void => {
      for (const card of cards) nextSequence = CardInstanceData.ReserveSequence(nextSequence, card?.Id ?? null);
    };
    reserveCards(this.Deck);
    reserveCards(this.ReserveDeck);
    reserveCards(this.PowerDeck);
    reserveCards(this.DiscardPile);
    for (const player of this.Players) {
      reserveCards(player.Hand);
      for (const zone of player.Zones) reserveCards(zone.Cards);
    }
    return nextSequence;
  }

  TakeNextCardInstanceId(): string {
    this.NextCardInstanceId = this.GetNextCardInstanceSequence();
    const cardId = CardInstanceData.CreateId(this.NextCardInstanceId);
    this.NextCardInstanceId = (this.NextCardInstanceId + 1) | 0;
    return cardId;
  }

  Clone(): GameStateData {
    const clone = Object.assign(new GameStateData(), {
      SchemaVersion: this.SchemaVersion,
      RulesVersion: this.RulesVersion,
      MatchId: this.MatchId,
      Seed: this.Seed,
      Revision: this.Revision,
      TurnNumber: this.TurnNumber,
      StartingPlayerId: this.StartingPlayerId,
      CurrentPlayerId: this.CurrentPlayerId,
      TurnPhase: this.TurnPhase,
      MovesRemaining: this.MovesRemaining,
      WinnerPlayerId: this.WinnerPlayerId,
      WinCondition: this.WinCondition,
      NextCardInstanceId: this.NextCardInstanceId,
      Rules: cloneRules(this.Rules),
      Random: cloneRandom(this.Random),
    });
    clone.Players = this.Players.map(clonePlayer);
    clone.Deck = this.Deck.map(cloneCard);
    clone.ReserveDeck = this.ReserveDeck.map(cloneCard);
    clone.PowerDeck = this.PowerDeck.map(cloneCard);
    clone.DiscardPile = this.DiscardPile.map(cloneCard);
    clone.Moves = this.Moves.map(cloneMove);
    return clone;
  }
}

export class OpponentViewData {
  Id = 0;
  Name: string | null = null;
  HandCount = 0;
  Zones: ZoneStateData[] = [];
}

export class PlayerViewData {
  Revision = 0;
  TurnNumber = 0;
  StartingPlayerId = 0;
  CurrentPlayerId = 0;
  TurnPhase: ETurnPhase = ETurnPhase.PlayerDraw;
  MovesRemaining = 0;
  WinnerPlayerId = 0;
  WinType: EWinCondition = EWinCondition.None;
  DeckCount = 0;
  PowerDeckCount = 0;
  NextCardInstanceId = 0;
  Rules: RulesConfigData | null = null;
  Player: PlayerStateData | null = null;
  Opponents: OpponentViewData[] = [];
  LegalMoves: MoveData[] = [];
}

export function cloneRules(value: RulesConfigData): RulesConfigData {
  return Object.assign(new RulesConfigData(), value);
}

export function cloneRandom(value: RandomStateData): RandomStateData {
  return Object.assign(new RandomStateData(), { State: value.State >>> 0, Cursor: value.Cursor | 0 });
}

export function cloneCard(value: CardInstanceData): CardInstanceData {
  return Object.assign(new CardInstanceData(), value, {
    Points: value.Points == null ? [] : [...value.Points],
    Multipliers: value.Multipliers == null ? [] : [...value.Multipliers],
    Bombs: value.Bombs == null ? [] : [...value.Bombs],
    Shields: value.Shields == null ? [] : [...value.Shields],
  });
}

export function cloneZone(value: ZoneStateData): ZoneStateData {
  return Object.assign(new ZoneStateData(), value, { Cards: value.Cards.map(cloneCard) });
}

export function clonePlayer(value: PlayerStateData): PlayerStateData {
  return Object.assign(new PlayerStateData(), value, {
    Hand: value.Hand.map(cloneCard),
    Zones: value.Zones.map(cloneZone),
  });
}

export function cloneMove(value: MoveData): MoveData {
  return Object.assign(new MoveData(), value);
}

function formatDecimal8(value: number): string {
  const integer = value | 0;
  const digits = String(Math.abs(integer)).padStart(8, "0");
  return integer < 0 ? `-${digits}` : digits;
}
