using System;
using System.Collections.Generic;
using Elemento.Domain;

namespace Elemento.Simulation
{
    internal static class BadPatternDetector
    {
        private const int strongBombDamageThreshold = 3;
        private const int maxExamplesPerAgent = 6;

        public static void Record(PlayerViewData inView, MoveData inMove, AgentStats inStats,
            int inGameNumber, int inGameSeed, int inPlayerTurn)
        {
            CardInstanceData selectedCard = FindSourceCard(inView, inMove);
            if (selectedCard == null)
                return;

            string detail;
            if (IsBombMove(inView, inMove, selectedCard)
                && HasBombOverShieldOpportunity(inView, out detail))
            {
                ++inStats.BombOverShield;
                AddExample(inStats, "BombOverShield", inView, inMove, selectedCard, detail,
                    inGameNumber, inGameSeed, inPlayerTurn);
            }

            if (IsWeakPointMove(inView, inMove, selectedCard)
                && HasStrongBombOpportunity(inView, out detail))
            {
                ++inStats.WeakPointOverBomb;
                AddExample(inStats, "WeakPointOverBomb", inView, inMove, selectedCard, detail,
                    inGameNumber, inGameSeed, inPlayerTurn);
            }
        }

        // BombOverShield: the selected move resolves a bomb; at decision time an own zone had
        // points > 0 and a multiplier card > 1, its top card did not shield that element, at
        // least one matching bomb copy remained outside all visible cards in the known Choice
        // deck, and a currently legal shield play to that zone existed.
        private static bool HasBombOverShieldOpportunity(PlayerViewData inView, out string outDetail)
        {
            foreach (var zone in inView.Player.Zones)
            {
                if (zone.Points <= 0 || !HasMultiplier(zone) || IsTopShielded(zone))
                    continue;

                int unseenBombs = CountUnseenBombs(inView, zone.ElementType);
                if (unseenBombs <= 0 || !HasLegalShieldMove(inView, zone))
                    continue;

                outDetail = string.Format("own {0} zone points={1}, unseen matching bombs={2}, legal shield available",
                    zone.ElementType, zone.Points, unseenBombs);
                return true;
            }

            outDetail = null;
            return false;
        }

        // WeakPointOverBomb: the selected move is a normal own-zone point play whose effective
        // point value is 1 or 2, while a legal bomb against an opponent either has actual
        // effective damage >= 3 (cards removed + health lost + frozen health lost), destroys
        // the zone, or removes at least one multiplier card with effective multiplier > 1.
        private static bool HasStrongBombOpportunity(PlayerViewData inView, out string outDetail)
        {
            foreach (var move in inView.LegalMoves)
            {
                var card = FindSourceCard(inView, move);
                var target = FindTargetZone(inView, move);
                if (card == null || target == null || move.Type != EMoveType.PlayCard
                    || move.TargetPlayerId == inView.Player.Id || !HasBomb(card))
                    continue;

                var after = GameRules.ResolveBomb(target, card, inView.Rules);
                int effectiveDamage = target.Cards.Count - after.Cards.Count
                    + target.Health - after.Health
                    + target.FrozenHealth - after.FrozenHealth;
                bool strippedMultiplier = RemovedMultiplierCard(target, after);
                if (effectiveDamage < strongBombDamageThreshold && !after.IsDestroyed && !strippedMultiplier)
                    continue;

                outDetail = string.Format("legal bomb {0} -> opponent {1}, effective damage={2}, stripped multiplier={3}, destroyed={4}",
                    card.DefinitionName, target.ElementType, effectiveDamage, strippedMultiplier, after.IsDestroyed);
                return true;
            }

            outDetail = null;
            return false;
        }

        private static bool IsBombMove(PlayerViewData inView, MoveData inMove, CardInstanceData inCard)
        {
            var target = FindTargetZone(inView, inMove);
            return inMove.Type == EMoveType.PlayCard && target != null && HasBomb(inCard)
                && (inMove.TargetPlayerId != inView.Player.Id || target.IsFrozen);
        }

        private static bool IsWeakPointMove(PlayerViewData inView, MoveData inMove, CardInstanceData inCard)
        {
            var target = FindTargetZone(inView, inMove);
            if (inMove.Type != EMoveType.PlayCard || target == null
                || inMove.TargetPlayerId != inView.Player.Id || target.IsFrozen)
                return false;

            int elementIndex = GetElementIndex(target.ElementType);
            int value = Math.Max(GetValue(inCard.Points, elementIndex), inCard.WildPoints);
            return value > 0 && value <= 2;
        }

        private static bool HasLegalShieldMove(PlayerViewData inView, ZoneStateData inZone)
        {
            foreach (var move in inView.LegalMoves)
            {
                if (move.Type != EMoveType.PlayCard || move.TargetPlayerId != inView.Player.Id
                    || move.TargetZoneId != inZone.Id)
                    continue;

                var card = FindSourceCard(inView, move);
                if (card == null)
                    continue;
                int elementIndex = GetElementIndex(inZone.ElementType);
                if (Math.Max(GetValue(card.Shields, elementIndex), card.WildShield) > 0)
                    return true;
            }
            return false;
        }

        private static bool HasMultiplier(ZoneStateData inZone)
        {
            int elementIndex = GetElementIndex(inZone.ElementType);
            foreach (var card in inZone.Cards)
            {
                if (Math.Max(GetValue(card.Multipliers, elementIndex), card.WildMultiplier) > 1)
                    return true;
            }
            return false;
        }

        private static bool IsTopShielded(ZoneStateData inZone)
        {
            if (inZone.Cards.Count == 0)
                return false;
            var topCard = inZone.Cards[inZone.Cards.Count - 1];
            int elementIndex = GetElementIndex(inZone.ElementType);
            return Math.Max(GetValue(topCard.Shields, elementIndex), topCard.WildShield) > 0;
        }

        private static bool RemovedMultiplierCard(ZoneStateData inBefore, ZoneStateData inAfter)
        {
            int elementIndex = GetElementIndex(inBefore.ElementType);
            foreach (var beforeCard in inBefore.Cards)
            {
                if (Math.Max(GetValue(beforeCard.Multipliers, elementIndex), beforeCard.WildMultiplier) <= 1)
                    continue;
                bool remains = false;
                foreach (var afterCard in inAfter.Cards)
                {
                    if (afterCard.Id == beforeCard.Id)
                    {
                        remains = true;
                        break;
                    }
                }
                if (!remains)
                    return true;
            }
            return false;
        }

        private static int CountUnseenBombs(PlayerViewData inView, EElementType inElementType)
        {
            int elementIndex = GetElementIndex(inElementType);
            var visibleCounts = new Dictionary<int, int>();
            CountDefinitions(inView.Player.Hand, visibleCounts);
            foreach (var zone in inView.Player.Zones)
                CountDefinitions(zone.Cards, visibleCounts);
            foreach (var opponent in inView.Opponents)
            {
                foreach (var zone in opponent.Zones)
                    CountDefinitions(zone.Cards, visibleCounts);
            }

            int count = 0;
            foreach (var definition in DeckDefinitionData.NormalCards)
            {
                if (Math.Max(GetValue(definition.Bombs, elementIndex), definition.WildBomb) <= 0)
                    continue;
                int visible;
                visibleCounts.TryGetValue(definition.DefinitionId, out visible);
                count += Math.Max(0, definition.ChoiceCount - visible);
            }
            return count;
        }

        private static void CountDefinitions(List<CardInstanceData> inCards, Dictionary<int, int> inCounts)
        {
            foreach (var card in inCards)
            {
                int count;
                inCounts.TryGetValue(card.DefinitionId, out count);
                inCounts[card.DefinitionId] = count + 1;
            }
        }

        private static CardInstanceData FindSourceCard(PlayerViewData inView, MoveData inMove)
        {
            if (inMove == null || inMove.CardId == null)
                return null;
            if (inMove.SourceZoneId < 0)
            {
                foreach (var card in inView.Player.Hand)
                {
                    if (card.Id == inMove.CardId)
                        return card;
                }
                return null;
            }

            foreach (var zone in inView.Player.Zones)
            {
                if (zone.Id == inMove.SourceZoneId && zone.Cards.Count > 0)
                    return zone.Cards[zone.Cards.Count - 1];
            }
            return null;
        }

        private static ZoneStateData FindTargetZone(PlayerViewData inView, MoveData inMove)
        {
            if (inMove == null || inMove.TargetZoneId < 0)
                return null;
            if (inMove.TargetPlayerId == inView.Player.Id)
            {
                foreach (var zone in inView.Player.Zones)
                {
                    if (zone.Id == inMove.TargetZoneId)
                        return zone;
                }
            }
            else
            {
                foreach (var opponent in inView.Opponents)
                {
                    if (opponent.Id != inMove.TargetPlayerId)
                        continue;
                    foreach (var zone in opponent.Zones)
                    {
                        if (zone.Id == inMove.TargetZoneId)
                            return zone;
                    }
                }
            }
            return null;
        }

        private static bool HasBomb(CardInstanceData inCard)
        {
            if (inCard.WildBomb > 0)
                return true;
            foreach (int bomb in inCard.Bombs)
            {
                if (bomb > 0)
                    return true;
            }
            return false;
        }

        private static void AddExample(AgentStats inStats, string inPattern, PlayerViewData inView,
            MoveData inMove, CardInstanceData inCard, string inDetail, int inGameNumber,
            int inGameSeed, int inPlayerTurn)
        {
            if (inStats.Examples.Count >= maxExamplesPerAgent)
                return;
            var target = FindTargetZone(inView, inMove);
            string targetText = target == null ? "none" : inMove.TargetPlayerId + "/" + target.ElementType;
            inStats.Examples.Add(string.Format("{0}: game={1} seed={2} player-turn={3}, chose {4} -> {5}; {6}",
                inPattern, inGameNumber, inGameSeed, inPlayerTurn,
                inCard.DefinitionName, targetText, inDetail));
        }

        private static int GetElementIndex(EElementType inElementType)
        {
            return (int)inElementType - 1;
        }

        private static int GetValue(int[] inValues, int inIndex)
        {
            return inValues == null || inIndex < 0 || inIndex >= inValues.Length ? 0 : inValues[inIndex];
        }
    }
}
