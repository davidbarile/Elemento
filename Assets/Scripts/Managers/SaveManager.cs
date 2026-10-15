using System.Collections.Generic;
using System.IO;
using System.Linq;
using Leguar.TotalJSON;
using UnityEngine;
using static SettingsManager;

public class SaveManager : MonoBehaviour
{
    public static SaveManager IN;
    private PlayerData playerData;
    private DefaultDecks defaultDecks;

    public static bool IsApplicationQuiting;

    private string filePath = "Assets/PlayerData/PlayerData.json";
    private string defaultDecksFilePath = "Assets/PlayerData/DefaultDecks.json";

    [Space, SerializeField] private bool clearPlayerPrefs;


    [Space, Header("DeckConfig to create"), SerializeField] private DeckConfig defaultDeckConfig;
    [Tooltip("This will create a DeckConfig asset from the default deck in the DefaultDecks.json file.  This is useful for quickly creating a DeckConfig asset that matches the default deck without having to manually set all the card counts.")]
    [SerializeField] private bool createDeckConfigFromJSONDefaultDeck;

    [Space, Header("Default DeckConfigs"), SerializeField] private DeckConfig[] defaultDeckConfigs;

    private bool playerPrefsExist;
    private bool defaultDeckJsonExist;

    private void OnValidate()
    {
        if (!this.createDeckConfigFromJSONDefaultDeck) return;

        if (!Application.isPlaying || this.defaultDeckConfig == null || this.defaultDecks == null || this.defaultDecks.DecksDict.Count == 0)
        {
            Debug.LogError($"OnValidate()   <color=red>Cannot create DeckConfig from JSON default deck.\nMake sure the game is running\ndefaultDeckConfig is assigned\ndefaultDecks is assigned\n defaultDecks.DecksDict has at least 1 deck.</color>");
            return;
        }
       
        this.createDeckConfigFromJSONDefaultDeck = false;

        var defaultName = "Default Deck";
        var activeDeckId = "DefaultDeckID";

        this.defaultDeckConfig.DeckName = defaultName;
        this.defaultDeckConfig.DeckID = activeDeckId;
        this.defaultDeckConfig.IsDefaultDeck = true;
        this.defaultDeckConfig.CardCounts = new List<int>(this.defaultDecks.DecksDict[activeDeckId].CardCounts);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this.defaultDeckConfig);
        Debug.Log($"Created DeckConfig from JSON default deck: {this.defaultDeckConfig.DeckName}");
#endif
    }

    private void Awake()
    {
        if (this.clearPlayerPrefs)
        {
            Debug.Log($"<color=red>clearPlayerPrefs = TRUE  ---->  PlayerPrefs.DeleteAll()</color>");
            PlayerPrefs.DeleteAll();
        }
    }

    public void LoadGame()
    {
        if (!Application.isEditor)
        {
            this.filePath = $"{Application.persistentDataPath}/PlayerData.json";
            this.defaultDecksFilePath = $"{Application.persistentDataPath}/DefaultDecks.json";
        }

        this.playerPrefsExist = PlayerPrefsX.GetBool("playerPrefsExist");

        if (this.playerPrefsExist)
            Debug.Log($"LoadGame()   <color=#00FF00>playerPrefsExist = {playerPrefsExist}</color>");
        else
            Debug.Log($"LoadGame()   <color=red>playerPrefsExist = {playerPrefsExist}</color>");


        LoadDefaultDecks();

        if (this.playerPrefsExist)
        {
            bool didMigrateSettings = LoadPlayerData();
            InitSettingsFromData();
            this.playerData.IsDirty = didMigrateSettings;
        }
        else
        {
            this.playerData = new();

            SetPlayerDataFromSettings();

            //this.defaultDeckJsonExist = false;//TODO: remove!!!!  
            // Only set this to populate ALL cards from CardsManager if you want to reset all decks to default.  
            // Otherwise, if the DefaultDecks.json exists, it will copy the default deck from that json instead of from CardsManager.

            if(this.defaultDeckConfigs.Length > 0)
            {
                for (int i = 0; i < this.defaultDeckConfigs.Length; i++)
                {
                    var config = this.defaultDeckConfigs[i];

                    Deck defaultDeck = new()
                    {
                        ID = config.DeckID,
                        Name = config.DeckName,
                        IsDefault = config.IsDefaultDeck,
                        CardCounts = new List<int>(config.CardCounts)
                    };

                    this.playerData.DecksDict.Add(config.DeckID, defaultDeck);

                    if (i == 0)
                        this.playerData.ActiveDeckId = config.DeckID;
                }

                this.playerData.IsDirty = true;
            }
            else if (this.defaultDeckJsonExist)
            {
                //copy decks dicts into playerdata
                var defaultName = "Default Deck";
                this.playerData.ActiveDeckId = "DefaultDeckID";

                Deck defaultDeck = new()
                {
                    ID = this.playerData.ActiveDeckId,
                    Name = defaultName,
                    CardCounts = new List<int>(this.defaultDecks.DecksDict[this.playerData.ActiveDeckId].CardCounts)
                };

                this.playerData.DecksDict.Add(this.playerData.ActiveDeckId, defaultDeck);
                this.playerData.IsDirty = true;

                print($"defaultDeckJsonExist = {this.defaultDeckJsonExist}");
            }
            else
            {
                var defaultName = "Default Deck";
                this.playerData.ActiveDeckId = "DefaultDeckID";

                Deck defaultDeck = new()
                {
                    ID = this.playerData.ActiveDeckId,
                    Name = defaultName,
                    IsDefault = true,
                    CardCounts = new List<int>(CardsManager.IN.DefaultCardCounts)
                };

                this.playerData.DecksDict.Add(this.playerData.ActiveDeckId, defaultDeck);
                this.playerData.IsDirty = true;

                this.defaultDecks = new();
                this.defaultDecks.DecksDict.Add(this.playerData.ActiveDeckId, defaultDeck);

                this.defaultDecks.IsDirty = true;
            }
        }

        PlayerData.Data = this.playerData;
        DefaultDecks.Data = this.defaultDecks;

        void InitSettingsFromData()
        {
            SettingsManager.IN.ShouldShowSplashScreen = this.playerData.ShouldShowSplashScreen;
            SettingsManager.IN.ShouldAutoDealHand = this.playerData.ShouldAutoDealHand;
            SettingsManager.IN.AllowUndo = this.playerData.AllowUndo;

            SettingsManager.IN.MusicVolume = this.playerData.MusicVolume;
            SettingsManager.IN.EffectsVolume = this.playerData.EffectsVolume;

            SettingsManager.IN.NumCardsToDeal = this.playerData.NumCardsToDeal;
            SettingsManager.IN.MaxCardsInHand = this.playerData.MaxCardsInHand;
            SettingsManager.IN.MovesPerTurn = this.playerData.MovesPerTurn;
            SettingsManager.IN.ElementZoneStartingHealth = this.playerData.ElementZoneStartingHealth;
            SettingsManager.IN.FrozenZoneHealth = this.playerData.FrozenZoneHealth;
            SettingsManager.IN.LightningDamage = this.playerData.LightningDamage;
            SettingsManager.IN.ElementZonePointsRequiredToActivate = this.playerData.ElementZonePointsRequiredToActivate;
            SettingsManager.IN.TotalPointsRequiredToWin = this.playerData.TotalPointsRequiredToWin;
            SettingsManager.IN.DiscardMode = (EDiscardMode)this.playerData.DiscardMode;
            SettingsManager.IN.PlayMode = (EPlayMode)this.playerData.PlayMode;
            SettingsManager.IN.AiDifficulty = (EAiDifficulty)this.playerData.AiDifficulty;
        }
    }

    private void SetPlayerDataFromSettings()
    {
        var localRules = SettingsManager.IN.GetLocalGameplayRules();
        this.playerData.ShouldShowSplashScreen = SettingsManager.IN.ShouldShowSplashScreen;
        this.playerData.ShouldAutoDealHand = SettingsManager.IN.ShouldAutoDealHand;
        this.playerData.AllowSelectPowerCardType = localRules.AllowSelectPowerCardType;
        this.playerData.AllowUndo = localRules.AllowUndo;

        this.playerData.MusicVolume = SettingsManager.IN.MusicVolume;
        this.playerData.EffectsVolume = SettingsManager.IN.EffectsVolume;

        this.playerData.NumCardsToDeal = localRules.NumCardsToDeal;
        this.playerData.MaxCardsInHand = localRules.MaxCardsInHand;
        this.playerData.MovesPerTurn = localRules.MovesPerTurn;
        this.playerData.ElementZoneStartingHealth = localRules.ElementZoneStartingHealth;
        this.playerData.FrozenZoneHealth = localRules.FrozenZoneHealth;
        this.playerData.LightningDamage = localRules.LightningDamage;
        this.playerData.ElementZonePointsRequiredToActivate = localRules.ElementZonePointsRequiredToActivate;
        this.playerData.TotalPointsRequiredToWin = localRules.TotalPointsRequiredToWin;
        this.playerData.DiscardMode = (int)localRules.DiscardMode;
        this.playerData.PlayMode = (int)SettingsManager.IN.PlayMode;
        this.playerData.AiDifficulty = (int)SettingsManager.IN.AiDifficulty;
    }

    private bool LoadPlayerData()
    {
        var reader = new StreamReader(this.filePath);
        var data = reader.ReadToEnd();
        reader.Close();

        DeserializeSettings deserializeSettings = new DeserializeSettings()
        {
            RequireAllFieldsArePopulated = false
        };

        JSON jsonObject = JSON.ParseString(data);
        this.playerData = jsonObject.Deserialize<PlayerData>(deserializeSettings);

        bool didMigrateSettings = false;
        if (!jsonObject.ContainsKey(nameof(PlayerData.PlayMode)))
        {
            this.playerData.PlayMode = (int)SettingsManager.IN.PlayMode;
            didMigrateSettings = true;
        }
        if (!jsonObject.ContainsKey(nameof(PlayerData.AllowUndo)))
        {
            this.playerData.AllowUndo = false;
            didMigrateSettings = true;
        }

        //Debug.Log($"Loaded: {data}");
        return didMigrateSettings;
    }

    public void SaveGame()
    {
        //Debug.Log($"SaveGame()");

        this.playerPrefsExist = PlayerPrefsX.SetBool("playerPrefsExist", true);

        if (PlayerData.Data.IsDirty)
        {
            SetPlayerDataFromSettings();
            SavePlayerData();
        }

        if (DefaultDecks.Data.IsDirty)
            SaveDefaultDecks();
    }

    private void SavePlayerData()
    {
        var playerDataJSON = JSON.Serialize(this.playerData);

        string jsonAsString = string.Empty;

        if (Application.isEditor)
            jsonAsString = playerDataJSON.CreatePrettyString();
        else
            jsonAsString = playerDataJSON.CreateString();

        var writer = new StreamWriter(this.filePath);
        writer.WriteLine(jsonAsString);
        writer.Close();

        this.playerData.IsDirty = false;
    }

    private void LoadDefaultDecks()
    {
        try
        {
            StreamReader reader = new StreamReader(this.defaultDecksFilePath);
            var data = reader.ReadToEnd();
            reader.Close();

            DeserializeSettings deserializeSettings = new DeserializeSettings()
            {
                RequireAllFieldsArePopulated = false
            };

            JSON jsonObject = JSON.ParseString(data);
            this.defaultDecks = jsonObject.Deserialize<DefaultDecks>(deserializeSettings);

            this.defaultDeckJsonExist = true;
        }
        catch
        {
            this.defaultDeckJsonExist = false;
        }
    }

    public void SaveDefaultDecks()
    {
        JSON playerDataJSON = JSON.Serialize(this.defaultDecks);

        string jsonAsString = string.Empty;

        if (Application.isEditor)
            jsonAsString = playerDataJSON.CreatePrettyString();
        else
            jsonAsString = playerDataJSON.CreateString();

        StreamWriter writer = new StreamWriter(this.defaultDecksFilePath);
        writer.WriteLine(jsonAsString);
        writer.Close();

        this.defaultDecks.IsDirty = false;
    }

    private void OnApplicationQuit()
    {
        SaveManager.IsApplicationQuiting = true;

        if (!UISettingsPanel.IN.IsResettingData)
            SaveGame();
    }
}
