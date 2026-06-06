using System;
using UnityEngine;

[Serializable]
public class CardData
{
    [Flags]
    public enum EFilterFlags
    {
        None = 0,
        Air = 1,
        Earth = 2,
        Fire = 4,
        Water = 8,
        Wild = 16,
        Points = 32,
        Multiplier = 64,
        Bomb = 128,
        Shield = 256,
        Single = 512,
        Double = 1024,
        Triple = 2048,
        Combo = 4096,
        Heal = 8192,
        Freeze = 16384,
        Lightning = 32768,
        Other = 65536
    }

    public enum EElementType
    {
        None,
        Air,
        Earth,
        Fire,
        Water,
        Wild
    }

    public string Name;
    public int Index;
    [Space]
    public EFilterFlags FilterFlags;
    public string PrefabName;
    [Space]
    public int Count;

    [Space]
    public int WildPoints;
    public int WildMultiplier;
    public int WildBomb;
    public int WildShield;

    [Space]
    public int WildZoneHeal;
    public int WildFreeze;
    public int WildLightning;
    public bool IsPowerCard;

    public int[] Points = new int[4];
    public int[] Multipliers = new int[4];
    public int[] Bombs = new int[4];
    public int[] Shields = new int[4];

    public bool IsPoints
    {
        get { return this.Points[0] + this.Points[1] + this.Points[2] + this.Points[3] + this.WildPoints > 0; }
    }

    public bool IsMultiply
    {
        get { return this.Multipliers[0] + this.Multipliers[1] + this.Multipliers[2] + this.Multipliers[3] + this.WildMultiplier > 0; }
    }

    public bool IsBomb
    {
        get { return this.HasBomb && !this.IsPoints && !this.IsMultiply && !this.IsShield; }
    }

    public bool IsShield
    {
        get { return this.Shields[0] + this.Shields[1] + this.Shields[2] + this.Shields[3] + this.WildShield > 0; }
    }

    public bool IsCombo
    {
        get { return this.HasBomb && (this.IsPoints || this.IsMultiply || this.IsShield); }
    }

    public bool IsBombOrCombo
    {
        get { return this.HasBomb; }
    }

    private bool HasBomb
    {
        get { return this.Bombs[0] + this.Bombs[1] + this.Bombs[2] + this.Bombs[3] + this.WildBomb > 0; }
    }

    public bool HasAir
    {
        get { return this.Points[0] + this.Multipliers[0] + this.Bombs[0] + this.Shields[0] > 0 || this.HasWild; }
    }

    public bool HasEarth
    {
        get { return this.Points[1] + this.Multipliers[1] + this.Bombs[1] + this.Shields[1] > 0 || this.HasWild; }
    }

    public bool HasFire
    {
        get { return this.Points[2] + this.Multipliers[2] + this.Bombs[2] + this.Shields[2] > 0 || this.HasWild; }
    }

    public bool HasWater
    {
        get { return this.Points[3] + this.Multipliers[3] + this.Bombs[3] + this.Shields[3] > 0 || this.HasWild; }
    }

    public bool HasWild
    {
        get { return this.WildPoints + this.WildMultiplier + this.WildBomb + this.WildShield > 0; }
    }

    public bool HasElement(EElementType inElementType, bool inShouldIncludeBombs = true)
    {
        if(inShouldIncludeBombs)
        {
            switch (inElementType)
            {
                case EElementType.Air:
                    return HasAir;
                case EElementType.Earth:
                    return HasEarth;
                case EElementType.Fire:
                    return HasFire;
                case EElementType.Water:
                    return HasWater;
                default:
                    return false;
            }
        }
        else
        {
            var hasWild = this.WildPoints + this.WildMultiplier + this.WildShield > 0;
            int index = (int)(inElementType - 1);

            return this.Points[index] + this.Multipliers[index] + this.Shields[index] > 0 || hasWild;
        }
    }

    public bool HasBombElement(EElementType inElementType)
    {
        int index = (int)(inElementType - 1);

        return this.Bombs[index] + this.WildBomb > 0;
    }

    public bool IsComboWithPointsComboOrShieldOfElement(EElementType inElementType)
    {
        int index = (int)(inElementType - 1);
        return this.IsCombo && (this.Points[index] + this.WildPoints + this.Multipliers[index] + this.WildMultiplier + this.Shields[index] + this.WildShield > 0);
    }

    public static CardData Clone(CardData inOriginal)
    {
        var cardData = new CardData
        {
            Count = inOriginal.Count,
            FilterFlags = inOriginal.FilterFlags,

            Index = inOriginal.Index,
            Name = inOriginal.Name,
            PrefabName = inOriginal.PrefabName,

            WildPoints = inOriginal.WildPoints,
            WildMultiplier = inOriginal.WildMultiplier,
            WildBomb = inOriginal.WildBomb,
            WildShield = inOriginal.WildShield,

            Points = inOriginal.Points,
            Multipliers = inOriginal.Multipliers,
            Bombs = inOriginal.Bombs,
            Shields = inOriginal.Shields,

            WildZoneHeal = inOriginal.WildZoneHeal,
            WildFreeze = inOriginal.WildFreeze,
            WildLightning = inOriginal.WildLightning,
            IsPowerCard = inOriginal.IsPowerCard
        };

        return cardData;
    }
}