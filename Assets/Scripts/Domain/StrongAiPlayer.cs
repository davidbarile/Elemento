using System;
using System.Collections.Generic;

namespace Elemento.Domain
{
    public sealed class StrongAiOptions
    {
        public bool UseEffectBasedBombScoring = true;
        public EAiDifficulty Difficulty = EAiDifficulty.Hard;
        public float Temperature;
    }

    public sealed class StrongAiPlayer : LegacyAiPlayer
    {
        private const float zoneKillBonus = 5f;
        private const float multiplierStripBonus = 3f;
        private const float mediumTemperature = 1f;
        private const float easyTemperature = 2f;

        private readonly bool useEffectBasedBombScoring;
        private readonly float temperature;

        public StrongAiPlayer()
            : this(new StrongAiOptions())
        {
        }

        public StrongAiPlayer(StrongAiOptions inOptions)
            : base(inOptions == null ? EAiDifficulty.Hard : inOptions.Difficulty)
        {
            if (inOptions == null)
                throw new ArgumentNullException(nameof(inOptions));
            if (!Enum.IsDefined(typeof(EAiDifficulty), inOptions.Difficulty))
                throw new ArgumentOutOfRangeException(nameof(inOptions));
            if (inOptions.Temperature < 0f)
                throw new ArgumentOutOfRangeException(nameof(inOptions));

            this.useEffectBasedBombScoring = inOptions.UseEffectBasedBombScoring;
            this.temperature = ResolveTemperature(inOptions);
        }

        protected override bool TryGetPointPlayValue(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, BoardAnalysis inOwn, out float outValue)
        {
            if (!this.useEffectBasedBombScoring)
                return base.TryGetPointPlayValue(inView, inCard, inZone, inOwn, out outValue);

            var simulatedZone = inZone.Clone();
            var simulatedCard = inCard.Clone();
            simulatedCard.IsFaceUp = true;
            simulatedZone.Cards.Add(simulatedCard);
            int pointsGained = Math.Max(0, GameRules.CalculateZonePoints(simulatedZone) - inZone.Points);
            outValue = ScorePointDelta(pointsGained, inOwn.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);
            return true;
        }

        protected override bool TryScoreBomb(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, bool inIsOwnBoard, BoardAnalysis inOwn, BoardAnalysis inOpponent,
            int inNumMovesRemaining, out float outValue)
        {
            if (!this.useEffectBasedBombScoring || inIsOwnBoard)
            {
                return base.TryScoreBomb(inView, inCard, inZone, inIsOwnBoard, inOwn, inOpponent,
                    inNumMovesRemaining, out outValue);
            }

            var simulatedZone = GameRules.ResolveBomb(inZone.Clone(), inCard, inView.Rules);
            outValue = ScoreBombEffect(inZone, simulatedZone, inOpponent.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);
            return outValue > 0f;
        }

        protected override bool TryGetShieldPlayValue(PlayerViewData inView, CardInstanceData inCard,
            ZoneStateData inZone, BoardAnalysis inOwn, out float outValue)
        {
            if (!this.useEffectBasedBombScoring)
                return base.TryGetShieldPlayValue(inView, inCard, inZone, inOwn, out outValue);

            CardDefinitionData strongestBomb;
            int unseenCount;
            int threateningCount;
            GetUnseenBombThreat(inView, inZone, out strongestBomb, out unseenCount, out threateningCount);
            int opponentHandSize = inView.Opponents.Count == 0 ? 0 : inView.Opponents[0].HandCount;
            double threatProbability = ProbabilityOfAtLeastOne(unseenCount, threateningCount,
                opponentHandSize);
            if (strongestBomb == null || threatProbability <= 0.0)
            {
                outValue = 0f;
                return false;
            }

            var bombCard = strongestBomb.CreateCard();
            var bombedZone = GameRules.ResolveBomb(inZone, bombCard, inView.Rules);
            float exposedLoss = ScoreBombEffect(inZone, bombedZone, inOwn.TotalPoints,
                inView.Rules.TotalPointsRequiredToWin);

            var shieldedZone = inZone.Clone();
            var shieldCard = inCard.Clone();
            shieldCard.IsFaceUp = true;
            shieldedZone.Cards.Add(shieldCard);
            shieldedZone.Points = GameRules.CalculateZonePoints(shieldedZone);
            shieldedZone.FaceUpPoints = GameRules.CalculateZonePoints(shieldedZone, true);
            var shieldedBombedZone = GameRules.ResolveBomb(shieldedZone, bombCard, inView.Rules);
            float shieldedLoss = ScoreBombEffect(shieldedZone, shieldedBombedZone,
                inOwn.TotalPoints, inView.Rules.TotalPointsRequiredToWin);

            outValue = (float)(threatProbability * Math.Max(0f, exposedLoss - shieldedLoss));
            return true;
        }

        private static float ScorePointDelta(int inPointDelta, int inCurrentPoints, int inWinTarget)
        {
            if (inPointDelta <= 0 || inWinTarget <= 0)
                return Math.Max(0, inPointDelta);

            float proximity = Math.Min(1f, (float)inCurrentPoints / inWinTarget);
            return inPointDelta * (1f + proximity);
        }

        private static float ScoreBombEffect(ZoneStateData inBefore, ZoneStateData inAfter,
            int inCurrentPoints, int inWinTarget)
        {
            int pointsRemoved = Math.Max(0, inBefore.Points - inAfter.Points);
            int effectiveDamage = inBefore.Cards.Count - inAfter.Cards.Count
                + inBefore.Health - inAfter.Health
                + inBefore.FrozenHealth - inAfter.FrozenHealth;
            float value = Math.Max(effectiveDamage,
                ScorePointDelta(pointsRemoved, inCurrentPoints, inWinTarget));
            if (!inBefore.IsDestroyed && inAfter.IsDestroyed)
                value += zoneKillBonus;
            if (WasMultiplierStripped(inBefore, inAfter))
                value += multiplierStripBonus;
            return value;
        }

        private static void GetUnseenBombThreat(PlayerViewData inView, ZoneStateData inZone,
            out CardDefinitionData outStrongestBomb, out int outUnseenCount, out int outThreateningCount)
        {
            var visibleCounts = new Dictionary<int, int>();
            CountDefinitions(inView.Player.Hand, visibleCounts);
            foreach (var zone in inView.Player.Zones)
                CountDefinitions(zone.Cards, visibleCounts);
            foreach (var opponent in inView.Opponents)
            {
                foreach (var zone in opponent.Zones)
                    CountDefinitions(zone.Cards, visibleCounts);
            }

            int elementIndex = (int)inZone.ElementType - 1;
            int topShield = GetTopShield(inZone, elementIndex);
            int strongestValue = 0;
            outStrongestBomb = null;
            outUnseenCount = 0;
            outThreateningCount = 0;
            foreach (var definition in DeckDefinitionData.NormalCards)
            {
                int visible;
                visibleCounts.TryGetValue(definition.DefinitionId, out visible);
                int remaining = Math.Max(0, definition.ChoiceCount - visible);
                outUnseenCount += remaining;

                int bombValue = Math.Max(GetValue(definition.Bombs, elementIndex), definition.WildBomb);
                if (remaining <= 0 || bombValue <= 0 || bombValue < topShield)
                    continue;

                outThreateningCount += remaining;
                if (bombValue <= strongestValue)
                    continue;
                strongestValue = bombValue;
                outStrongestBomb = definition;
            }
        }

        private static double ProbabilityOfAtLeastOne(int inPopulation, int inSuccesses, int inDraws)
        {
            int draws = Math.Min(inDraws, inPopulation);
            if (inPopulation <= 0 || inSuccesses <= 0 || draws <= 0)
                return 0.0;
            if (inSuccesses >= inPopulation)
                return 1.0;

            double probabilityOfNone = 1.0;
            for (int i = 0; i < draws; ++i)
            {
                int safeRemaining = inPopulation - inSuccesses - i;
                if (safeRemaining <= 0)
                    return 1.0;
                probabilityOfNone *= (double)safeRemaining / (inPopulation - i);
            }
            return 1.0 - probabilityOfNone;
        }

        private static int GetTopShield(ZoneStateData inZone, int inElementIndex)
        {
            if (inZone.Cards.Count == 0)
                return 0;
            var topCard = inZone.Cards[inZone.Cards.Count - 1];
            return Math.Max(GetValue(topCard.Shields, inElementIndex), topCard.WildShield);
        }

        private static void CountDefinitions(List<CardInstanceData> inCards,
            Dictionary<int, int> inCounts)
        {
            foreach (var card in inCards)
            {
                int count;
                inCounts.TryGetValue(card.DefinitionId, out count);
                inCounts[card.DefinitionId] = count + 1;
            }
        }

        protected override int GetDifficultyIndex(int inCount, IRandom inRandom)
        {
            if (inCount <= 1 || this.temperature <= 0f)
                return 0;

            double totalWeight = 0.0;
            for (int i = 0; i < inCount; ++i)
                totalWeight += Math.Exp(-i / this.temperature);

            double selectedWeight = inRandom.NextDouble() * totalWeight;
            for (int i = 0; i < inCount; ++i)
            {
                selectedWeight -= Math.Exp(-i / this.temperature);
                if (selectedWeight <= 0.0)
                    return i;
            }

            return inCount - 1;
        }

        private static float ResolveTemperature(StrongAiOptions inOptions)
        {
            if (inOptions.Temperature > 0f)
                return inOptions.Temperature;
            if (inOptions.Difficulty == EAiDifficulty.Medium)
                return mediumTemperature;
            if (inOptions.Difficulty == EAiDifficulty.Easy)
                return easyTemperature;
            return 0f;
        }

        private static bool WasMultiplierStripped(ZoneStateData inBefore, ZoneStateData inAfter)
        {
            int elementIndex = (int)inBefore.ElementType - 1;
            foreach (var card in inBefore.Cards)
            {
                int multiplier = Math.Max(GetValue(card.Multipliers, elementIndex), card.WildMultiplier);
                if (multiplier <= 1 || ContainsCard(inAfter, card.Id))
                    continue;
                return true;
            }

            return false;
        }

        private static bool ContainsCard(ZoneStateData inZone, string inCardId)
        {
            foreach (var card in inZone.Cards)
            {
                if (card.Id == inCardId)
                    return true;
            }
            return false;
        }

        private static int GetValue(int[] inValues, int inIndex)
        {
            return inValues == null || inIndex < 0 || inIndex >= inValues.Length ? 0 : inValues[inIndex];
        }
    }
}
