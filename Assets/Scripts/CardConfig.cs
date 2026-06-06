using Sirenix.OdinInspector;
using UnityEngine;
using static CardData;

[CreateAssetMenu(fileName = "Card", menuName = "ScriptableObjects/Card Config", order = 1)]
public class CardConfig : ScriptableObject
{
    public int Count;
    public EFilterFlags FilterFlags;

    public Card Prefab;

    [Space]
    public int AirPoints;
    public int EarthPoints;
    public int FirePoints;
    public int WaterPoints;

    [Space]
    public int AirMultiplier;
    public int EarthMultiplier;
    public int FireMultiplier;
    public int WaterMultiplier;

    [Space]
    public int AirBomb;
    public int EarthBomb;
    public int FireBomb;
    public int WaterBomb;

    [Space]
    public int AirShield;
    public int EarthShield;
    public int FireShield;
    public int WaterShield;

    [Space]
    public int WildPoints;
    public int WildMultiplier;
    public int WildBomb;
    public int WildShield;

    [Space]
    public int WildZoneHeal;
    public int WildFreeze;
    public int WildLightning;
    [Header("Set In Inspector")]
    public bool IsPowerCard;

    [ReadOnly]
    public int[] Points = new int[4];
    [ReadOnly]
    public int[] Multipliers = new int[4];
    [ReadOnly]
    public int[] Bombs = new int[4];
    [ReadOnly]
    public int[] Shields = new int[4];

    public bool shouldValidate;

    //public bool shouldClear;

    public string FileName;

    private void RefreshArrays()
    {
        this.Points[0] = this.AirPoints;
        this.Points[1] = this.EarthPoints;
        this.Points[2] = this.FirePoints;
        this.Points[3] = this.WaterPoints;

        this.Multipliers[0] = this.AirMultiplier;
        this.Multipliers[1] = this.EarthMultiplier;
        this.Multipliers[2] = this.FireMultiplier;
        this.Multipliers[3] = this.WaterMultiplier;

        this.Bombs[0] = this.AirBomb;
        this.Bombs[1] = this.EarthBomb;
        this.Bombs[2] = this.FireBomb;
        this.Bombs[3] = this.WaterBomb;

        this.Shields[0] = this.AirShield;
        this.Shields[1] = this.EarthShield;
        this.Shields[2] = this.FireShield;
        this.Shields[3] = this.WaterShield;
    }

    private void OnValidate()
    {
        //RefreshArrays();

        if (this.shouldValidate)
        {
            this.shouldValidate = false;

            RefreshArrays();

            if (HasAir) this.FilterFlags += (int)EFilterFlags.Air;
            if (HasEarth) this.FilterFlags += (int)EFilterFlags.Earth;
            if (HasFire) this.FilterFlags += (int)EFilterFlags.Fire;
            if (HasWater) this.FilterFlags += (int)EFilterFlags.Water;

            //this.FilterFlags += (int)EFilterFlags.Combo;

            this.FilterFlags += (int)EFilterFlags.Bomb;

            if (WildPoints > 0)
                this.FilterFlags += (int)EFilterFlags.Points;

            if (WildMultiplier > 0)
                this.FilterFlags += (int)EFilterFlags.Multiplier;

            if (WildShield > 0)
                this.FilterFlags += (int)EFilterFlags.Shield;

            if (WildPoints > 0 || WildMultiplier > 0 || WildShield > 0)
                this.FilterFlags += (int)EFilterFlags.Wild;
        }

        //if (this.shouldClear)
        //{
        //    this.shouldClear = false;

        //    if (HasAir) this.FilterFlags = EFilterFlags.None;
        //    if (HasEarth) this.FilterFlags = EFilterFlags.None;
        //    if (HasFire) this.FilterFlags = EFilterFlags.None;
        //    if (HasWater) this.FilterFlags = EFilterFlags.None;
        //}
    }

    public string GetFileName()
    {
        this.FileName = $"Combo - Bomb ";

        if (Bombs[0] > 0)
            this.FileName += $"Air {Bombs[0]}";
        if (Bombs[1] > 0)
            this.FileName += $"Earth {Bombs[1]}";
        if (Bombs[2] > 0)
            this.FileName += $"Fire {Bombs[2]}";
        if (Bombs[3] > 0)
            this.FileName += $"Water {Bombs[3]}";
        if (WildBomb > 0)
            this.FileName += $"Wild {WildBomb}";

        this.FileName += $" =";

        if (Points[0] > 0)
            this.FileName += $" Points Air {Points[0]}";
        if (Points[1] > 0)
            this.FileName += $" Points Earth {Points[1]}";
        if (Points[2] > 0)
            this.FileName += $" Points Fire {Points[2]}";
        if (Points[3] > 0)
            this.FileName += $" Points Water {Points[3]}";
        if (WildPoints > 0)
            this.FileName += $" Points Wild {WildPoints}";

        if (Multipliers[0] > 0)
            this.FileName += $" Mult Air {Multipliers[0]}";
        if (Multipliers[1] > 0)
            this.FileName += $" Mult Earth {Multipliers[1]}";
        if (Multipliers[2] > 0)
            this.FileName += $" Mult Fire {Multipliers[2]}";
        if (Multipliers[3] > 0)
            this.FileName += $" Mult Water {Multipliers[3]}";
        if (WildMultiplier > 0)
            this.FileName += $" Mult Wild {WildMultiplier}";

        if (Shields[0] > 0)
            this.FileName += $" Shield Air {Shields[0]}";
        if (Shields[1] > 0)
            this.FileName += $" Shield Earth {Shields[1]}";
        if (Shields[2] > 0)
            this.FileName += $" Shield Fire {Shields[2]}";
        if (Shields[3] > 0)
            this.FileName += $" Shield Water {Shields[3]}";
        if (WildShield > 0)
            this.FileName += $" Shield Wild {WildShield}";

        return this.FileName;
    }

    public bool HasAir
    {
        get { return this.Points[0] + this.Multipliers[0] + this.Bombs[0] + this.Shields[0] > 0; }
    }

    public bool HasEarth
    {
        get { return this.Points[1] + this.Multipliers[1] + this.Bombs[1] + this.Shields[1] > 0; }
    }

    public bool HasFire
    {
        get { return this.Points[2] + this.Multipliers[2] + this.Bombs[2] + this.Shields[2] > 0; }
    }

    public bool HasWater
    {
        get { return this.Points[3] + this.Multipliers[3] + this.Bombs[3] + this.Shields[3] > 0; }
    }

    public static CardData CreateCardData(CardConfig inConfig, int inIndex)
    {
        var cardData = new CardData
        {
            Count = inConfig.Count,
            FilterFlags = inConfig.FilterFlags,

            Index = inIndex,
            Name = inConfig.name,
            PrefabName = inConfig.Prefab.name,

            WildPoints = inConfig.WildPoints,
            WildMultiplier = inConfig.WildMultiplier,
            WildBomb = inConfig.WildBomb,
            WildShield = inConfig.WildShield,

            Points = inConfig.Points,
            Multipliers = inConfig.Multipliers,
            Bombs = inConfig.Bombs,
            Shields = inConfig.Shields,

            WildZoneHeal = inConfig.WildZoneHeal,
            WildFreeze = inConfig.WildFreeze,
            WildLightning = inConfig.WildLightning,
            IsPowerCard = inConfig.IsPowerCard
        };

        return cardData;
    }
}