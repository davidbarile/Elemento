using System;

namespace Elemento.Domain
{
    public static class GameRules
    {
        public static int CalculateZonePoints(ZoneStateData inZone, bool inFaceUpOnly = false)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));

            int elementIndex = GetElementIndex(inZone.ElementType);
            int points = 0;

            foreach (var card in inZone.Cards)
            {
                if (inFaceUpOnly && !card.IsFaceUp)
                    continue;

                int cardPoints = card.WildPoints;
                if (cardPoints == 0)
                    cardPoints = GetElementValue(card.Points, elementIndex);

                points += cardPoints;
            }

            foreach (var card in inZone.Cards)
            {
                if (inFaceUpOnly && !card.IsFaceUp)
                    continue;

                int multiplier = card.WildMultiplier;
                if (multiplier == 0)
                    multiplier = GetElementValue(card.Multipliers, elementIndex);

                if (multiplier > 1)
                    points *= multiplier;
            }

            return points;
        }

        public static int CalculatePlayerPoints(PlayerStateData inPlayer)
        {
            if (inPlayer == null)
                throw new ArgumentNullException(nameof(inPlayer));

            int points = 0;
            foreach (var zone in inPlayer.Zones)
                points += zone.Points;

            return points;
        }

        public static bool HasReachedActivationThreshold(int inPoints, int inRequiredPoints)
        {
            return inPoints >= inRequiredPoints;
        }

        public static bool HasPointsTargetWin(PlayerStateData inPlayer, RulesConfigData inRules)
        {
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            return CalculatePlayerPoints(inPlayer) >= inRules.TotalPointsRequiredToWin;
        }

        public static bool AreAllZonesDestroyed(PlayerStateData inPlayer)
        {
            if (inPlayer == null)
                throw new ArgumentNullException(nameof(inPlayer));

            foreach (var zone in inPlayer.Zones)
            {
                if (!zone.IsDestroyed)
                    return false;
            }

            return true;
        }

        public static bool CanPlayCardInZone(CardInstanceData inCard, EElementType inElementType)
        {
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));

            int elementIndex = GetElementIndex(inElementType);
            bool hasWild = inCard.WildPoints + inCard.WildMultiplier + inCard.WildShield > 0;

            return GetElementValue(inCard.Points, elementIndex)
                + GetElementValue(inCard.Multipliers, elementIndex)
                + GetElementValue(inCard.Shields, elementIndex) > 0 || hasWild;
        }

        public static bool CanHealZone(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules, bool inIsOwnZone)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            return inCard.IsPowerCard
                && inCard.WildZoneHeal > 0
                && inIsOwnZone
                && !inZone.IsFrozen
                && inZone.Cards.Count == 0
                && inZone.Health < inRules.ElementZoneStartingHealth;
        }

        public static bool CanFreezeZone(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            return inCard.IsPowerCard
                && inCard.WildFreeze > 0
                && (inZone.IsDestroyed || inZone.FrozenHealth < inRules.FrozenZoneHealth);
        }

        public static bool CanBombZone(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules, bool inIsOwnZone)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            int strength = GetBombStrength(inCard, inZone.ElementType);
            bool frozenStateMatchesTarget = inIsOwnZone && inZone.IsFrozen || !inIsOwnZone && !inZone.IsFrozen;
            bool zoneDamageEnabled = inRules.ElementZoneStartingHealth > 0;

            return strength > 0
                && !inZone.IsDestroyed
                && frozenStateMatchesTarget
                && (inZone.Cards.Count > 0 || zoneDamageEnabled);
        }

        public static bool CanLightningZone(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules, bool inIsOwnZone)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            bool frozenStateMatchesTarget = inIsOwnZone && inZone.IsFrozen || !inIsOwnZone && !inZone.IsFrozen;
            bool zoneDamageEnabled = inRules.ElementZoneStartingHealth > 0;

            return inCard.IsPowerCard
                && inCard.WildLightning > 0
                && !inZone.IsDestroyed
                && frozenStateMatchesTarget
                && (inZone.Cards.Count > 0 || zoneDamageEnabled);
        }

        public static ZoneStateData ResolveHeal(ZoneStateData inZone, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            var result = inZone.Clone();
            result.Health = inRules.ElementZoneStartingHealth;
            result.MaxHealth = inRules.ElementZoneStartingHealth;
            result.IsDestroyed = false;

            return result;
        }

        public static ZoneStateData ResolveFreeze(ZoneStateData inZone, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            var result = inZone.Clone();
            result.FrozenHealth = inRules.FrozenZoneHealth;
            result.IsActivated = false;

            return result;
        }

        public static ZoneStateData ResolveMelt(ZoneStateData inZone, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            var result = inZone.Clone();
            result.FrozenHealth = Math.Max(0, result.FrozenHealth - inRules.FrozenZonesMeltAmountPerTurn);
            result.IsActivated = !result.IsFrozen
                && HasReachedActivationThreshold(result.Points, inRules.ElementZonePointsRequiredToActivate);

            return result;
        }

        public static ZoneStateData ResolveBomb(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            var result = inZone.Clone();
            int elementIndex = GetElementIndex(result.ElementType);
            int bombPoints = Math.Max(GetElementValue(inCard.Bombs, elementIndex), inCard.WildBomb);

            if (result.IsFrozen)
            {
                result.FrozenHealth = Math.Max(0, result.FrozenHealth - bombPoints);
            }
            else if (result.Cards.Count > 0)
            {
                int cardsRemaining = Math.Min(bombPoints, result.Cards.Count);

                for (int i = result.Cards.Count - 1; i >= 0 && cardsRemaining > 0; --i)
                {
                    var targetCard = result.Cards[i];
                    if (HasAnyShield(targetCard))
                    {
                        int shieldPoints = Math.Max(GetElementValue(targetCard.Shields, elementIndex), targetCard.WildShield);
                        if (shieldPoints > bombPoints)
                            break;

                        result.Cards.RemoveAt(i);
                        cardsRemaining -= shieldPoints;
                        break;
                    }

                    result.Cards.RemoveAt(i);
                    --cardsRemaining;
                }
            }
            else
            {
                SetZoneHealth(result, result.Health - bombPoints, inRules);
            }

            RefreshZoneState(result, inRules);
            return result;
        }

        public static ZoneStateData ResolveLightning(ZoneStateData inZone, CardInstanceData inCard, RulesConfigData inRules)
        {
            if (inZone == null)
                throw new ArgumentNullException(nameof(inZone));
            if (inCard == null)
                throw new ArgumentNullException(nameof(inCard));
            if (inRules == null)
                throw new ArgumentNullException(nameof(inRules));

            var result = inZone.Clone();
            int lightningPoints = inCard.WildLightning;

            if (result.IsFrozen)
            {
                result.FrozenHealth = Math.Max(0, result.FrozenHealth - lightningPoints);
            }
            else if (result.Cards.Count > 0)
            {
                int cardsToRemove = Math.Min(lightningPoints, result.Cards.Count);
                for (int i = 0; i < cardsToRemove; ++i)
                    result.Cards.RemoveAt(result.Cards.Count - 1);
            }
            else
            {
                SetZoneHealth(result, result.Health - lightningPoints, inRules);
            }

            RefreshZoneState(result, inRules);
            return result;
        }

        private static void RefreshZoneState(ZoneStateData inZone, RulesConfigData inRules)
        {
            inZone.Points = CalculateZonePoints(inZone);
            inZone.FaceUpPoints = CalculateZonePoints(inZone, true);
            inZone.IsActivated = !inZone.IsFrozen
                && !inZone.IsDestroyed
                && HasReachedActivationThreshold(inZone.Points, inRules.ElementZonePointsRequiredToActivate);
        }

        private static void SetZoneHealth(ZoneStateData inZone, int inHealth, RulesConfigData inRules)
        {
            if (inRules.ElementZoneStartingHealth > 0)
            {
                inZone.Health = Math.Max(0, inHealth);
                inZone.IsDestroyed = inZone.Health <= 0;

                if (inZone.IsDestroyed)
                    inZone.IsActivated = false;
            }
            else
            {
                inZone.IsDestroyed = false;
            }
        }

        private static int GetBombStrength(CardInstanceData inCard, EElementType inElementType)
        {
            int elementIndex = GetElementIndex(inElementType);
            return Math.Max(GetElementValue(inCard.Bombs, elementIndex), inCard.WildBomb);
        }

        private static bool HasAnyShield(CardInstanceData inCard)
        {
            int shieldPoints = inCard.WildShield;
            foreach (int points in inCard.Shields)
                shieldPoints += points;

            return shieldPoints > 0;
        }

        private static int GetElementIndex(EElementType inElementType)
        {
            int elementIndex = (int)inElementType - 1;
            if (elementIndex < 0 || elementIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(inElementType));

            return elementIndex;
        }

        private static int GetElementValue(int[] inValues, int inElementIndex)
        {
            if (inValues == null || inValues.Length <= inElementIndex)
                throw new ArgumentException("Element values must contain Air, Earth, Fire, and Water entries.", nameof(inValues));

            return inValues[inElementIndex];
        }
    }
}
