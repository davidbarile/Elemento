using UnityEngine;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager IN;

    public enum EDiscardMode
    {
        None,
        AllowDiscard,
        DiscardEndsTurn
    }

    public enum EPlayMode
    {
        AgainstAI,
        PassAndPlay,
        Multiplayer
    }

    public enum EAiDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    //default settings
    private float musicVolume = .5f;
    private float effectsVolume = .7f;

    private int numCardsToDeal = 5;
    private int maxCardsInHand = 20;
    private int movesPerTurn = 0;
    private int elementZoneStartingHealth = 10;
    private int frozenZoneHealth = 5;
    private int lightningDamage = 5;
    private int elementZonePointsRequiredToWin = 10;
    private int totalPointsRequiredToWin = 50;
    private EDiscardMode discardMode;
    private EPlayMode playMode;
    private EAiDifficulty aiDifficulty;

    //settings
    public bool ShouldShowSplashScreen;
    public bool ShouldAutoDealHand;

    public bool AllowSelectPowerCardType = true;

    [Range(0, 1)] public float MusicVolume = .5f;
    [Range(0, 1)] public float EffectsVolume = .7f;

    [Range(1, 10)] public int NumCardsToDeal = 5;
    [Range(5, 20)] public int MaxCardsInHand = 20;
    [Range(0, 10)] public int MovesPerTurn = 0;
    [Range(1, 20)] public int ElementZoneStartingHealth = 10;
    [Range(0, 10)] public int FrozenZoneHealth = 5;
    [Range(0, 10)] public int LightningDamage = 5;
    [Range(5, 50)] public int ElementZonePointsRequiredToActivate = 10;
    [Range(10, 200)] public int TotalPointsRequiredToWin = 50;
    [Range(0, 5)] public int FrozenZonesMeltAmountPerTurn = 1;

    public EDiscardMode DiscardMode;
    public EPlayMode PlayMode;
    public EAiDifficulty AiDifficulty;

    public int TutorialSafeElementZoneHealth => UITutorialPanel.IsTutorialMode ? 5 : this.ElementZoneStartingHealth;
    public int TutorialSafeMovesPerTurn => UITutorialPanel.IsTutorialMode ? 0 : this.MovesPerTurn;

    public void SetDefaults()
    {
        this.numCardsToDeal = this.NumCardsToDeal;
        this.maxCardsInHand = this.MaxCardsInHand;
        this.movesPerTurn = this.MovesPerTurn;
        this.elementZoneStartingHealth = this.ElementZoneStartingHealth;
        this.frozenZoneHealth = this.FrozenZoneHealth;
        this.lightningDamage = this.LightningDamage;
        this.elementZonePointsRequiredToWin = this.ElementZonePointsRequiredToActivate;
        this.totalPointsRequiredToWin = this.TotalPointsRequiredToWin;
        this.discardMode = this.DiscardMode;
        this.playMode = this.PlayMode;
        this.aiDifficulty = this.AiDifficulty;
    }

    public void ResetToDefaults()
    {
        PlayerData.Data.ShouldShowSplashScreen = true;

        this.MusicVolume = this.musicVolume;
        this.EffectsVolume = this.effectsVolume;

        this.NumCardsToDeal = this.numCardsToDeal;
        this.MaxCardsInHand = this.maxCardsInHand;
        this.MovesPerTurn = this.movesPerTurn;
        this.ElementZoneStartingHealth = this.elementZoneStartingHealth;
        this.FrozenZoneHealth = this.frozenZoneHealth;
        this.LightningDamage = this.lightningDamage;
        this.ElementZonePointsRequiredToActivate = this.elementZonePointsRequiredToWin;
        this.TotalPointsRequiredToWin = this.totalPointsRequiredToWin;
        this.DiscardMode = this.discardMode;
        this.PlayMode = this.playMode;
        this.AiDifficulty = this.aiDifficulty;
    }
}