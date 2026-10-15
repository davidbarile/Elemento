import { DeckDefinitionData } from "./deck_definition_data.ts";
import { DeterministicRandom, deterministicShuffle } from "./deterministic_random.ts";
import { GameRules } from "./game_rules.ts";
import {
  CardInstanceData,
  EDiscardMode,
  EElementType,
  EMoveType,
  EPowerCardType,
  ETurnPhase,
  EWinCondition,
  GameStateData,
  MoveData,
  OpponentViewData,
  PlayerStateData,
  PlayerViewData,
  RulesConfigData,
  ZoneStateData,
  cloneCard,
  cloneMove,
  clonePlayer,
  cloneRules,
  cloneZone,
} from "./game_state_data.ts";
import { TurnRules } from "./turn_rules.ts";

const UNLIMITED_MOVES = 99;
const NUM_PLAYERS = 2;

export class GameSimulator {
  CreateGame(inSeed: number, inRules: RulesConfigData | null = null, inDeck: CardInstanceData[] | null = DeckDefinitionData.CreateChoiceDeck(), inStartingPlayerId = 0): GameStateData {
    const deck = inDeck;
    if (deck == null) throw new TypeError("inDeck");
    if (!Number.isInteger(inStartingPlayerId) || inStartingPlayerId < 0 || inStartingPlayerId >= NUM_PLAYERS) throw new RangeError("inStartingPlayerId");
    const rules = inRules == null ? new RulesConfigData() : cloneRules(inRules);
    this.ValidateRules(rules);
    const state = Object.assign(new GameStateData(), {
      MatchId: `match-${inSeed}`,
      Seed: inSeed | 0,
      StartingPlayerId: inStartingPlayerId,
      CurrentPlayerId: inStartingPlayerId,
      WinnerPlayerId: -1,
      Rules: rules,
      Random: new DeterministicRandom(inSeed).GetState(),
    });
    state.Players.push(this.CreatePlayer(0, "Player One", rules), this.CreatePlayer(1, "Player Two", rules));
    for (const source of deck) {
      if (source == null) throw new Error("The deck cannot contain null cards.");
      const card = cloneCard(source);
      card.Id = null;
      card.IsFaceUp = false;
      state.ReserveDeck.push(card);
    }
    this.Shuffle(state, state.ReserveDeck);
    this.AddBatchOfCardsToGameDeck(state);
    state.PowerDeck.push(...DeckDefinitionData.CreatePowerDeck());
    this.Shuffle(state, state.PowerDeck);
    this.BeginTurn(state, inStartingPlayerId);
    return state;
  }

  GetLegalMoves(inState: GameStateData, inPlayer: number | PlayerStateData): MoveData[] {
    this.ValidateState(inState);
    const playerId = typeof inPlayer === "number" ? inPlayer : inPlayer?.Id;
    if (playerId == null) throw new TypeError("inPlayer");
    const moves: MoveData[] = [];
    if (this.IsGameOver(inState) || inState.CurrentPlayerId !== playerId || !this.IsPlayingPhase(inState.TurnPhase)) return moves;
    const player = this.GetPlayer(inState, playerId);
    const canPlay = inState.Rules.MovesPerTurn <= 0 || inState.MovesRemaining > 0;
    const sequence = { value: inState.Moves.length + 1 };
    this.AddActivationMoves(inState, player, moves, sequence);
    for (const card of player.Hand) this.AddCardMoves(inState, player, card, -1, canPlay, moves, sequence);
    for (const zone of player.Zones) {
      if (zone.Cards.length === 0) continue;
      this.AddCardMoves(inState, player, zone.Cards[zone.Cards.length - 1], zone.Id, canPlay, moves, sequence);
    }
    if (inState.Rules.DiscardMode !== EDiscardMode.DiscardEndsTurn) {
      moves.push(this.CreateMove(inState, sequence.value, playerId, EMoveType.EndTurn));
    }
    return moves;
  }

  ApplyMove(inState: GameStateData, inMove: MoveData): void {
    this.ValidateState(inState);
    if (inMove == null) throw new TypeError("inMove");
    if (inMove.ExpectedRevision !== inState.Revision) throw new Error("The move was generated for a different game revision.");
    const legalMove = this.GetLegalMoves(inState, inMove.PlayerId).find((candidate) => this.MovesMatch(candidate, inMove));
    if (legalMove == null) throw new Error("The move is not legal in the current game state.");
    if (legalMove.Type === EMoveType.EndTurn) {
      this.RecordMove(inState, legalMove);
      this.EndTurnInternal(inState);
      inState.Revision = (inState.Revision + 1) | 0;
      return;
    }
    const player = this.GetPlayer(inState, legalMove.PlayerId);
    if (legalMove.Type === EMoveType.ActivateZone) {
      this.ApplyActivation(inState, player, legalMove);
      this.RecordMove(inState, legalMove);
      inState.Revision = (inState.Revision + 1) | 0;
      return;
    }
    const source = this.RemoveSourceCard(player, legalMove.CardId, legalMove.SourceZoneId);
    if (legalMove.Type === EMoveType.DiscardCard) {
      this.RecycleCard(inState, source.card);
      if (source.zone != null) this.RefreshZone(source.zone, inState.Rules, player);
      this.RecordMove(inState, legalMove);
      inState.Revision = (inState.Revision + 1) | 0;
      if (inState.Rules.DiscardMode === EDiscardMode.DiscardEndsTurn && !this.IsGameOver(inState)) this.EndTurnInternal(inState);
      return;
    }
    if (legalMove.TargetZoneId < 0) {
      source.card.IsFaceUp = true;
      player.Hand.push(source.card);
      this.RefreshZone(source.zone, inState.Rules, player);
    } else {
      const targetPlayer = this.GetPlayer(inState, legalMove.TargetPlayerId);
      const targetZone = this.GetZone(targetPlayer, legalMove.TargetZoneId);
      this.ApplyCardToZone(inState, player, source.zone, source.card, targetPlayer, targetZone);
    }
    this.DecrementMoves(inState);
    this.RecordMove(inState, legalMove);
    inState.Revision = (inState.Revision + 1) | 0;
    this.CheckPointsWin(inState, player);
    if (!this.IsGameOver(inState) && inState.Rules.MovesPerTurn > 0 && inState.MovesRemaining <= 0 && inState.Rules.DiscardMode !== EDiscardMode.DiscardEndsTurn) {
      this.EndTurnInternal(inState);
    }
  }

  EndTurn(inState: GameStateData): void {
    this.ValidateState(inState);
    if (this.IsGameOver(inState)) throw new Error("The game is already over.");
    if (inState.Rules.DiscardMode === EDiscardMode.DiscardEndsTurn) throw new Error("This discard mode requires a discard to end the turn.");
    const move = this.CreateMove(inState, inState.Moves.length + 1, inState.CurrentPlayerId, EMoveType.EndTurn);
    this.RecordMove(inState, move);
    this.EndTurnInternal(inState);
    inState.Revision = (inState.Revision + 1) | 0;
  }

  IsGameOver(inState: GameStateData): boolean {
    this.ValidateState(inState);
    return inState.WinCondition !== EWinCondition.None;
  }
  Winner(inState: GameStateData): number { this.ValidateState(inState); return inState.WinnerPlayerId; }
  WinType(inState: GameStateData): EWinCondition { this.ValidateState(inState); return inState.WinCondition; }

  GetPlayerView(inState: GameStateData, inPlayerId: number): PlayerViewData {
    this.ValidateState(inState);
    const player = this.GetPlayer(inState, inPlayerId);
    const view = Object.assign(new PlayerViewData(), {
      Revision: inState.Revision,
      TurnNumber: inState.TurnNumber,
      StartingPlayerId: inState.StartingPlayerId,
      CurrentPlayerId: inState.CurrentPlayerId,
      TurnPhase: inState.TurnPhase,
      MovesRemaining: inState.MovesRemaining,
      WinnerPlayerId: inState.WinnerPlayerId,
      WinType: inState.WinCondition,
      DeckCount: inState.Deck.length + inState.ReserveDeck.length,
      PowerDeckCount: inState.PowerDeck.length,
      NextCardInstanceId: inState.NextCardInstanceId,
      Rules: cloneRules(inState.Rules),
      Player: clonePlayer(player),
    });
    for (const opponent of inState.Players) {
      if (opponent.Id === inPlayerId) continue;
      const opponentView = Object.assign(new OpponentViewData(), { Id: opponent.Id, Name: opponent.Name, HandCount: opponent.Hand.length });
      opponentView.Zones = opponent.Zones.map(cloneZone);
      view.Opponents.push(opponentView);
    }
    view.LegalMoves = this.GetLegalMoves(inState, inPlayerId).map(cloneMove);
    return view;
  }

  private CreatePlayer(id: number, name: string, rules: RulesConfigData): PlayerStateData {
    const player = Object.assign(new PlayerStateData(), { Id: id, Name: name });
    for (let i = 0; i < 4; ++i) player.Zones.push(Object.assign(new ZoneStateData(), {
      Id: i, ElementType: (i + 1) as EElementType, Health: rules.ElementZoneStartingHealth, MaxHealth: rules.ElementZoneStartingHealth,
    }));
    return player;
  }

  private ValidateRules(rules: RulesConfigData): void {
    if (rules.NumCardsToDeal < 0 || rules.MaxCardsInHand < 1 || rules.MovesPerTurn < 0 || rules.ElementZoneStartingHealth < 0 ||
      rules.FrozenZoneHealth < 0 || rules.LightningDamage < 0 || rules.MaxCardsPerZone < 1 || rules.CardSpawnBatchSize < 1) throw new RangeError("Invalid rules.");
  }
  private ValidateState(state: GameStateData): void { if (state == null) throw new TypeError("inState"); }
  private IsPlayingPhase(phase: ETurnPhase): boolean { return phase === ETurnPhase.PlayerPlaying || phase === ETurnPhase.OpponentPlaying; }
  private GetPlayer(state: GameStateData, id: number): PlayerStateData {
    const player = state.Players.find((value) => value.Id === id);
    if (player == null) throw new RangeError("inPlayerId");
    return player;
  }
  private GetZone(player: PlayerStateData, id: number): ZoneStateData {
    const zone = player.Zones.find((value) => value.Id === id);
    if (zone == null) throw new RangeError("inZoneId");
    return zone;
  }

  private BeginTurn(state: GameStateData, playerId: number): void {
    state.CurrentPlayerId = playerId;
    if (playerId === state.StartingPlayerId) state.TurnNumber = (state.TurnNumber + 1) | 0;
    state.TurnPhase = playerId === 0 ? ETurnPhase.PlayerDraw : ETurnPhase.OpponentDraw;
    state.MovesRemaining = state.Rules.MovesPerTurn > 0 ? state.Rules.MovesPerTurn : UNLIMITED_MOVES;
    const player = this.GetPlayer(state, playerId);
    this.DrawCards(state, player, state.Rules.NumCardsToDeal);
    state.TurnPhase = playerId === 0 ? ETurnPhase.PlayerPlaying : ETurnPhase.OpponentPlaying;
    this.MeltFrozenZones(player, state.Rules);
    state.MovesRemaining = state.Rules.MovesPerTurn > 0 ? state.Rules.MovesPerTurn : UNLIMITED_MOVES;
  }

  private EndTurnInternal(state: GameStateData): void {
    const current = state.Players.findIndex((player) => player.Id === state.CurrentPlayerId);
    const next = TurnRules.GetNextPlayerIndex(current, state.Players.length);
    this.BeginTurn(state, state.Players[next].Id);
  }

  private DrawCards(state: GameStateData, player: PlayerStateData, count: number): void {
    const toDraw = Math.min(count, state.Rules.MaxCardsInHand - player.Hand.length);
    for (let i = 0; i < toDraw; ++i) {
      if (state.Deck.length <= 2) this.AddBatchOfCardsToGameDeck(state);
      if (state.Deck.length === 0) break;
      const card = state.Deck.pop()!;
      card.IsFaceUp = true;
      player.Hand.push(card);
      this.AddBatchOfCardsToGameDeck(state);
    }
  }

  private AddBatchOfCardsToGameDeck(state: GameStateData): void {
    this.Shuffle(state, state.ReserveDeck);
    const count = Math.min(state.Rules.CardSpawnBatchSize - state.Deck.length, state.ReserveDeck.length);
    for (let i = 0; i < count; ++i) {
      const template = state.ReserveDeck.shift()!;
      const card = cloneCard(template);
      const sequence = state.NextCardInstanceId;
      state.NextCardInstanceId = (state.NextCardInstanceId + 1) | 0;
      card.Id = CardInstanceData.CreateId(sequence);
      card.IsFaceUp = false;
      state.Deck.push(card);
    }
  }

  private Shuffle(state: GameStateData, cards: CardInstanceData[]): void {
    const random = new DeterministicRandom(state.Random);
    deterministicShuffle(cards, random);
    state.Random = random.GetState();
  }

  private MeltFrozenZones(player: PlayerStateData, rules: RulesConfigData): void {
    if (rules.FrozenZonesMeltAmountPerTurn <= 0) return;
    for (const zone of player.Zones) if (zone.FrozenHealth > 0) this.CopyZone(GameRules.ResolveMelt(zone, rules), zone);
  }

  private AddActivationMoves(state: GameStateData, player: PlayerStateData, moves: MoveData[], sequence: { value: number }): void {
    if (state.Rules.DiscardMode === EDiscardMode.DiscardEndsTurn && player.Hand.length === 0) return;
    for (const zone of player.Zones) {
      if (!zone.IsActivated) continue;
      if (state.Rules.AllowSelectPowerCardType) {
        this.AddActivationMove(state, player.Id, zone.Id, EPowerCardType.Heal, moves, sequence);
        this.AddActivationMove(state, player.Id, zone.Id, EPowerCardType.Freeze, moves, sequence);
        this.AddActivationMove(state, player.Id, zone.Id, EPowerCardType.Lightning, moves, sequence);
      } else this.AddActivationMove(state, player.Id, zone.Id, EPowerCardType.None, moves, sequence);
    }
  }

  private AddActivationMove(state: GameStateData, playerId: number, zoneId: number, type: EPowerCardType, moves: MoveData[], sequence: { value: number }): void {
    const move = this.CreateMove(state, sequence.value++, playerId, EMoveType.ActivateZone);
    Object.assign(move, { SourceZoneId: zoneId, TargetPlayerId: playerId, TargetZoneId: zoneId, PowerCardType: type });
    moves.push(move);
  }

  private AddCardMoves(state: GameStateData, player: PlayerStateData, card: CardInstanceData, sourceZoneId: number, canPlay: boolean, moves: MoveData[], sequence: { value: number }): void {
    if (state.Rules.DiscardMode !== EDiscardMode.None) {
      const discard = this.CreateMove(state, sequence.value++, player.Id, EMoveType.DiscardCard);
      discard.CardId = card.Id;
      discard.SourceZoneId = sourceZoneId;
      moves.push(discard);
    }
    if (!canPlay) return;
    if (sourceZoneId >= 0 && player.Hand.length < state.Rules.MaxCardsInHand) {
      const returnToHand = this.CreateMove(state, sequence.value++, player.Id, EMoveType.PlayCard);
      Object.assign(returnToHand, { CardId: card.Id, SourceZoneId: sourceZoneId, TargetPlayerId: player.Id, TargetZoneId: -1 });
      moves.push(returnToHand);
    }
    for (const targetPlayer of state.Players) for (const targetZone of targetPlayer.Zones) {
      if (targetPlayer.Id === player.Id && targetZone.Id === sourceZoneId) continue;
      if (!this.CanPlayCardToZone(state, player, card, targetPlayer, targetZone)) continue;
      const play = this.CreateMove(state, sequence.value++, player.Id, EMoveType.PlayCard);
      Object.assign(play, { CardId: card.Id, SourceZoneId: sourceZoneId, TargetPlayerId: targetPlayer.Id, TargetZoneId: targetZone.Id });
      moves.push(play);
    }
  }

  private CanPlayCardToZone(state: GameStateData, player: PlayerStateData, card: CardInstanceData, targetPlayer: PlayerStateData, zone: ZoneStateData): boolean {
    const own = player.Id === targetPlayer.Id;
    if (card.IsPowerCard) {
      if (card.WildZoneHeal > 0) return GameRules.CanHealZone(zone, card, state.Rules, own);
      if (card.WildFreeze > 0) return GameRules.CanFreezeZone(zone, card, state.Rules);
      if (card.WildLightning > 0) return GameRules.CanLightningZone(zone, card, state.Rules, own);
      return false;
    }
    if (this.HasBomb(card) && (!own || zone.FrozenHealth > 0)) return GameRules.CanBombZone(zone, card, state.Rules, own);
    if (this.IsPureBomb(card)) return false;
    return own && !zone.IsDestroyed && zone.FrozenHealth <= 0 && zone.Cards.length < state.Rules.MaxCardsPerZone && GameRules.CanPlayCardInZone(card, zone.ElementType);
  }

  private HasBomb(card: CardInstanceData): boolean { return card.WildBomb > 0 || card.Bombs.some((value) => value > 0); }
  private IsPureBomb(card: CardInstanceData): boolean {
    return this.HasBomb(card) && !this.HasAnyValue(card.Points, card.WildPoints) && !this.HasAnyValue(card.Multipliers, card.WildMultiplier) && !this.HasAnyValue(card.Shields, card.WildShield);
  }
  private HasAnyValue(values: number[], wild: number): boolean { return wild > 0 || values.some((value) => value > 0); }

  private CreateMove(state: GameStateData, sequence: number, playerId: number, type: EMoveType): MoveData {
    return Object.assign(new MoveData(), {
      Id: MoveData.CreateId(sequence), PlayerId: playerId, Type: type, SourceZoneId: -1, TargetPlayerId: -1, TargetZoneId: -1, ExpectedRevision: state.Revision,
    });
  }
  private MovesMatch(first: MoveData, second: MoveData): boolean {
    return first.PlayerId === second.PlayerId && first.Type === second.Type && first.CardId === second.CardId && first.SourceZoneId === second.SourceZoneId && first.TargetPlayerId === second.TargetPlayerId && first.TargetZoneId === second.TargetZoneId && first.PowerCardType === second.PowerCardType;
  }

  private ApplyActivation(state: GameStateData, player: PlayerStateData, move: MoveData): void {
    const zone = this.GetZone(player, move.SourceZoneId);
    while (zone.Cards.length > 0) this.RecycleCard(state, zone.Cards.pop()!);
    this.RefreshZone(zone, state.Rules, player);
    const powerCard = move.PowerCardType === EPowerCardType.None ? this.DrawRandomPowerCard(state) : this.SpawnPowerCard(state, DeckDefinitionData.CreatePowerCard(move.PowerCardType));
    if (powerCard != null) player.Hand.push(powerCard);
  }

  private DrawRandomPowerCard(state: GameStateData): CardInstanceData | null {
    if (state.PowerDeck.length === 0) this.RebuildPowerDeck(state);
    if (state.PowerDeck.length === 0) return null;
    const card = this.SpawnPowerCard(state, state.PowerDeck.shift()!);
    if (state.PowerDeck.length < 2) this.RebuildPowerDeck(state);
    return card;
  }
  private RebuildPowerDeck(state: GameStateData): void {
    state.PowerDeck.length = 0;
    state.PowerDeck.push(...DeckDefinitionData.CreatePowerDeck());
    this.Shuffle(state, state.PowerDeck);
  }
  private SpawnPowerCard(state: GameStateData, template: CardInstanceData | null): CardInstanceData | null {
    if (template == null) return null;
    const card = cloneCard(template);
    const sequence = state.NextCardInstanceId;
    state.NextCardInstanceId = (state.NextCardInstanceId + 1) | 0;
    card.Id = CardInstanceData.CreateId(sequence);
    card.IsFaceUp = true;
    if (card.WildZoneHeal > 0) card.WildZoneHeal = state.Rules.ElementZoneStartingHealth;
    if (card.WildFreeze > 0) card.WildFreeze = state.Rules.FrozenZoneHealth;
    if (card.WildLightning > 0) card.WildLightning = state.Rules.LightningDamage;
    return card;
  }

  private RemoveSourceCard(player: PlayerStateData, cardId: string | null, sourceZoneId: number): { card: CardInstanceData; zone: ZoneStateData | null } {
    if (sourceZoneId < 0) return { card: this.RemoveCard(player.Hand, cardId), zone: null };
    const zone = this.GetZone(player, sourceZoneId);
    if (zone.Cards.length === 0 || zone.Cards[zone.Cards.length - 1].Id !== cardId) throw new Error("Only the top zone card can be moved.");
    return { card: zone.Cards.pop()!, zone };
  }
  private RemoveCard(cards: CardInstanceData[], cardId: string | null): CardInstanceData {
    const index = cards.findIndex((card) => card.Id === cardId);
    if (index < 0) throw new Error("The source card no longer exists.");
    return cards.splice(index, 1)[0];
  }

  private ApplyCardToZone(state: GameStateData, player: PlayerStateData, sourceZone: ZoneStateData | null, card: CardInstanceData, targetPlayer: PlayerStateData, targetZone: ZoneStateData): void {
    const original = cloneZone(targetZone);
    const own = player.Id === targetPlayer.Id;
    if (card.IsPowerCard) {
      const result = card.WildZoneHeal > 0 ? GameRules.ResolveHeal(targetZone, state.Rules) : card.WildFreeze > 0 ? GameRules.ResolveFreeze(targetZone, state.Rules) : GameRules.ResolveLightning(targetZone, card, state.Rules);
      this.RecycleMissingCards(state, original, result);
      this.PreserveBlockedActivation(state, player, original, result);
      this.CopyZone(result, targetZone);
      this.RecycleCard(state, card);
    } else if (this.HasBomb(card) && (!own || targetZone.FrozenHealth > 0)) {
      const result = GameRules.ResolveBomb(targetZone, card, state.Rules);
      this.RecycleMissingCards(state, original, result);
      this.PreserveBlockedActivation(state, player, original, result);
      this.CopyZone(result, targetZone);
      this.RecycleCard(state, card);
    } else {
      card.IsFaceUp = true;
      targetZone.Cards.push(card);
      this.RefreshZone(targetZone, state.Rules, targetPlayer);
    }
    if (sourceZone != null) this.RefreshZone(sourceZone, state.Rules, player);
    if (targetZone.IsDestroyed) this.CheckDestroyedZonesWin(state, targetPlayer);
  }

  private PreserveBlockedActivation(state: GameStateData, player: PlayerStateData, before: ZoneStateData, after: ZoneStateData): void {
    if (state.Rules.DiscardMode !== EDiscardMode.DiscardEndsTurn || player.Hand.length > 0 || after.IsDestroyed) return;
    if (before.FrozenHealth > 0) after.IsActivated = after.FrozenHealth <= 0 && GameRules.HasReachedActivationThreshold(after.Points, state.Rules.ElementZonePointsRequiredToActivate);
    else after.IsActivated = before.IsActivated;
  }
  private RecycleMissingCards(state: GameStateData, before: ZoneStateData, after: ZoneStateData): void {
    for (const card of before.Cards) if (!after.Cards.some((remaining) => remaining.Id === card.Id)) this.RecycleCard(state, card);
  }
  private RecycleCard(state: GameStateData, card: CardInstanceData): void {
    const template = cloneCard(card);
    template.Id = null;
    template.IsFaceUp = false;
    (template.IsPowerCard ? state.PowerDeck : state.ReserveDeck).push(template);
  }
  private RefreshZone(zone: ZoneStateData | null, rules: RulesConfigData, owner: PlayerStateData): void {
    if (zone == null) return;
    zone.Points = GameRules.CalculateZonePoints(zone);
    zone.FaceUpPoints = GameRules.CalculateZonePoints(zone, true);
    if (rules.DiscardMode === EDiscardMode.DiscardEndsTurn && owner.Hand.length === 0) return;
    zone.IsActivated = zone.FrozenHealth <= 0 && !zone.IsDestroyed && GameRules.HasReachedActivationThreshold(zone.Points, rules.ElementZonePointsRequiredToActivate);
  }
  private CopyZone(source: ZoneStateData, target: ZoneStateData): void {
    Object.assign(target, {
      Id: source.Id, ElementType: source.ElementType, Points: source.Points, FaceUpPoints: source.FaceUpPoints, Health: source.Health, MaxHealth: source.MaxHealth,
      FrozenHealth: source.FrozenHealth, IsActivated: source.IsActivated, IsDestroyed: source.IsDestroyed, Cards: source.Cards.map(cloneCard),
    });
  }
  private DecrementMoves(state: GameStateData): void { if (state.Rules.MovesPerTurn > 0) state.MovesRemaining = Math.max(0, state.MovesRemaining - 1); }
  private CheckDestroyedZonesWin(state: GameStateData, destroyed: PlayerStateData): void {
    if (!GameRules.AreAllZonesDestroyed(destroyed)) return;
    const winner = state.Players.find((player) => player.Id !== destroyed.Id);
    if (winner != null) { state.WinnerPlayerId = winner.Id; state.WinCondition = EWinCondition.AllZonesDestroyed; }
  }
  private CheckPointsWin(state: GameStateData, player: PlayerStateData): void {
    if (state.WinCondition !== EWinCondition.None || (state.Rules.DiscardMode === EDiscardMode.DiscardEndsTurn && player.Hand.length === 0) || !GameRules.HasPointsTargetWin(player, state.Rules)) return;
    state.WinnerPlayerId = player.Id;
    state.WinCondition = EWinCondition.PointsTarget;
  }
  private RecordMove(state: GameStateData, move: MoveData): void {
    const record = cloneMove(move);
    record.Id = MoveData.CreateId(state.Moves.length + 1);
    record.ExpectedRevision = state.Revision;
    state.Moves.push(record);
  }
}
