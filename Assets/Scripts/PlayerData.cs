using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class PlayerData
{
    public static PlayerData Data;

    [HideInInspector] public bool IsDirty;

    [Header("Settings")]
    public bool ShouldShowSplashScreen;
    public bool ShouldAutoDealHand;
    public bool AllowSelectPowerCardType;
    public bool AllowUndo;

    [Space]
    public int LanguageIndex;
    [Space]
    [Range(0, 4)] public int SkinTypeIndex;
    [Range(0, 4)] public int CardBackIndex;
    [Space]
    [Range(0, 1)] public float MusicVolume;
    [Range(0, 1)] public float EffectsVolume;
    [Space]
    [Range(1, 10)] public int NumCardsToDeal;
    [Range(5, 20)] public int MaxCardsInHand;
    [Range(0, 10)] public int MovesPerTurn;
    [Range(1, 20)] public int ElementZoneStartingHealth;
    [Range(0, 10)] public int FrozenZoneHealth;
    [Range(0, 10)] public int LightningDamage;
    [Range(10, 200)] public int ElementZonePointsRequiredToActivate;
    [Range(10, 300)] public int TotalPointsRequiredToWin;
    [Range(0, 2)] public int DiscardMode;
    [Range(0, 2)] public int PlayMode;
    [Range(0, 2)] public int AiDifficulty;

    [Header("Deck Info")]
    public string ActiveDeckId;
    public Deck ActiveDeck => this.DecksDict[this.ActiveDeckId];

    [ShowInInspector]
    [Header("(View Only)")]
    public Dictionary<string, Deck> DecksDict = new();
}

[Serializable]
public class DefaultDecks
{
    public static DefaultDecks Data;

    [HideInInspector] public bool IsDirty;

    [ShowInInspector]
    [Header("(View Only)")]
    public Dictionary<string, Deck> DecksDict = new();
}
