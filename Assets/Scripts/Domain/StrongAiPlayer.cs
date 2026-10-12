using System;

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

        protected override bool TryGetPointPlayValue(CardInstanceData inCard, ZoneStateData inZone,
            out float outValue)
        {
            if (!this.useEffectBasedBombScoring)
                return base.TryGetPointPlayValue(inCard, inZone, out outValue);

            var simulatedZone = inZone.Clone();
            var simulatedCard = inCard.Clone();
            simulatedCard.IsFaceUp = true;
            simulatedZone.Cards.Add(simulatedCard);
            outValue = Math.Max(0, GameRules.CalculateZonePoints(simulatedZone) - inZone.Points);
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
            outValue = Math.Max(0, inZone.Points - simulatedZone.Points);
            if (!inZone.IsDestroyed && simulatedZone.IsDestroyed)
                outValue += zoneKillBonus;
            if (WasMultiplierStripped(inZone, simulatedZone))
                outValue += multiplierStripBonus;
            return outValue > 0f;
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
