import {
  CardInstanceData,
  EElementType,
  PlayerStateData,
  RulesConfigData,
  ZoneStateData,
  cloneZone,
} from "./game_state_data.ts";

const addI32 = (left: number, right: number): number => (left + right) | 0;
const subI32 = (left: number, right: number): number => (left - right) | 0;

export class GameRules {
  static CalculateZonePoints(inZone: ZoneStateData, inFaceUpOnly = false): number {
    if (inZone == null) throw new TypeError("inZone");
    const elementIndex = this.GetElementIndex(inZone.ElementType);
    let points = 0;
    for (const card of inZone.Cards) {
      if (inFaceUpOnly && !card.IsFaceUp) continue;
      let cardPoints = card.WildPoints;
      if (cardPoints === 0) cardPoints = this.GetElementValue(card.Points, elementIndex);
      points = addI32(points, cardPoints);
    }
    for (const card of inZone.Cards) {
      if (inFaceUpOnly && !card.IsFaceUp) continue;
      let multiplier = card.WildMultiplier;
      if (multiplier === 0) multiplier = this.GetElementValue(card.Multipliers, elementIndex);
      if (multiplier > 1) points = Math.imul(points, multiplier);
    }
    return points;
  }

  static CalculatePlayerPoints(inPlayer: PlayerStateData): number {
    if (inPlayer == null) throw new TypeError("inPlayer");
    let points = 0;
    for (const zone of inPlayer.Zones) points = addI32(points, zone.Points);
    return points;
  }

  static HasReachedActivationThreshold(inPoints: number, inRequiredPoints: number): boolean {
    return inPoints >= inRequiredPoints;
  }

  static HasPointsTargetWin(inPlayer: PlayerStateData, inRules: RulesConfigData): boolean {
    if (inRules == null) throw new TypeError("inRules");
    return this.CalculatePlayerPoints(inPlayer) >= inRules.TotalPointsRequiredToWin;
  }

  static AreAllZonesDestroyed(inPlayer: PlayerStateData): boolean {
    if (inPlayer == null) throw new TypeError("inPlayer");
    return inPlayer.Zones.every((zone) => zone.IsDestroyed);
  }

  static CanPlayCardInZone(inCard: CardInstanceData, inElementType: EElementType): boolean {
    if (inCard == null) throw new TypeError("inCard");
    const index = this.GetElementIndex(inElementType);
    const hasWild = addI32(addI32(inCard.WildPoints, inCard.WildMultiplier), inCard.WildShield) > 0;
    return addI32(addI32(this.GetElementValue(inCard.Points, index), this.GetElementValue(inCard.Multipliers, index)), this.GetElementValue(inCard.Shields, index)) > 0 || hasWild;
  }

  static CanHealZone(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData, isOwn: boolean): boolean {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    return card.IsPowerCard && card.WildZoneHeal > 0 && isOwn && zone.FrozenHealth <= 0 && zone.Cards.length === 0 && zone.Health < rules.ElementZoneStartingHealth;
  }

  static CanFreezeZone(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData): boolean {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    return card.IsPowerCard && card.WildFreeze > 0 && (zone.IsDestroyed || zone.FrozenHealth < rules.FrozenZoneHealth);
  }

  static CanBombZone(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData, isOwn: boolean): boolean {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    const strength = this.GetBombStrength(card, zone.ElementType);
    const stateMatches = (isOwn && zone.FrozenHealth > 0) || (!isOwn && zone.FrozenHealth <= 0);
    return strength > 0 && !zone.IsDestroyed && stateMatches && (zone.Cards.length > 0 || rules.ElementZoneStartingHealth > 0);
  }

  static CanLightningZone(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData, isOwn: boolean): boolean {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    const stateMatches = (isOwn && zone.FrozenHealth > 0) || (!isOwn && zone.FrozenHealth <= 0);
    return card.IsPowerCard && card.WildLightning > 0 && !zone.IsDestroyed && stateMatches && (zone.Cards.length > 0 || rules.ElementZoneStartingHealth > 0);
  }

  static ResolveHeal(zone: ZoneStateData, rules: RulesConfigData): ZoneStateData {
    if (zone == null || rules == null) throw new TypeError("zone and rules are required");
    const result = cloneZone(zone);
    result.Health = rules.ElementZoneStartingHealth;
    result.MaxHealth = rules.ElementZoneStartingHealth;
    result.IsDestroyed = false;
    return result;
  }

  static ResolveFreeze(zone: ZoneStateData, rules: RulesConfigData): ZoneStateData {
    if (zone == null || rules == null) throw new TypeError("zone and rules are required");
    const result = cloneZone(zone);
    result.FrozenHealth = rules.FrozenZoneHealth;
    result.IsActivated = false;
    return result;
  }

  static ResolveMelt(zone: ZoneStateData, rules: RulesConfigData): ZoneStateData {
    if (zone == null || rules == null) throw new TypeError("zone and rules are required");
    const result = cloneZone(zone);
    result.FrozenHealth = Math.max(0, subI32(result.FrozenHealth, rules.FrozenZonesMeltAmountPerTurn));
    result.IsActivated = result.FrozenHealth <= 0 && this.HasReachedActivationThreshold(result.Points, rules.ElementZonePointsRequiredToActivate);
    return result;
  }

  static ResolveBomb(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData): ZoneStateData {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    const result = cloneZone(zone);
    const elementIndex = this.GetElementIndex(result.ElementType);
    const bombPoints = Math.max(this.GetElementValue(card.Bombs, elementIndex), card.WildBomb);
    if (result.FrozenHealth > 0) {
      result.FrozenHealth = Math.max(0, subI32(result.FrozenHealth, bombPoints));
    } else if (result.Cards.length > 0) {
      let remaining = Math.min(bombPoints, result.Cards.length);
      for (let i = result.Cards.length - 1; i >= 0 && remaining > 0; --i) {
        const target = result.Cards[i];
        if (this.HasAnyShield(target)) {
          const shield = Math.max(this.GetElementValue(target.Shields, elementIndex), target.WildShield);
          if (shield > bombPoints) break;
          result.Cards.splice(i, 1);
          remaining -= shield;
          break;
        }
        result.Cards.splice(i, 1);
        --remaining;
      }
    } else {
      this.SetZoneHealth(result, subI32(result.Health, bombPoints), rules);
    }
    this.RefreshZoneState(result, rules);
    return result;
  }

  static ResolveLightning(zone: ZoneStateData, card: CardInstanceData, rules: RulesConfigData): ZoneStateData {
    if (zone == null || card == null || rules == null) throw new TypeError("zone, card, and rules are required");
    const result = cloneZone(zone);
    const points = card.WildLightning;
    if (result.FrozenHealth > 0) result.FrozenHealth = Math.max(0, subI32(result.FrozenHealth, points));
    else if (result.Cards.length > 0) result.Cards.splice(Math.max(0, result.Cards.length - Math.min(points, result.Cards.length)));
    else this.SetZoneHealth(result, subI32(result.Health, points), rules);
    this.RefreshZoneState(result, rules);
    return result;
  }

  private static RefreshZoneState(zone: ZoneStateData, rules: RulesConfigData): void {
    zone.Points = this.CalculateZonePoints(zone);
    zone.FaceUpPoints = this.CalculateZonePoints(zone, true);
    zone.IsActivated = zone.FrozenHealth <= 0 && !zone.IsDestroyed && this.HasReachedActivationThreshold(zone.Points, rules.ElementZonePointsRequiredToActivate);
  }

  private static SetZoneHealth(zone: ZoneStateData, health: number, rules: RulesConfigData): void {
    if (rules.ElementZoneStartingHealth > 0) {
      zone.Health = Math.max(0, health);
      zone.IsDestroyed = zone.Health <= 0;
      if (zone.IsDestroyed) zone.IsActivated = false;
    } else zone.IsDestroyed = false;
  }

  private static GetBombStrength(card: CardInstanceData, elementType: EElementType): number {
    const index = this.GetElementIndex(elementType);
    return Math.max(this.GetElementValue(card.Bombs, index), card.WildBomb);
  }

  private static HasAnyShield(card: CardInstanceData): boolean {
    let points = card.WildShield;
    for (const value of card.Shields) points = addI32(points, value);
    return points > 0;
  }

  private static GetElementIndex(elementType: EElementType): number {
    const index = elementType - 1;
    if (index < 0 || index > 3) throw new RangeError("inElementType");
    return index;
  }

  private static GetElementValue(values: number[], index: number): number {
    if (values == null || values.length <= index) throw new Error("Element values must contain Air, Earth, Fire, and Water entries.");
    return values[index];
  }
}
