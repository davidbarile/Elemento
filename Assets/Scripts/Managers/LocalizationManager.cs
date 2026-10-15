using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CardData;

//to make the written code shorter
public static class LOC
{
    public static LocalizationManager IN = LocalizationManager.IN;

    public static int LI
    {
        get
        {
            return PlayerData.Data.LanguageIndex;
        }
    }
}

[Serializable]
public class ArrayWrapper
{
    public string[] LocStrings;
}

public class LocalizationManager : MonoBehaviour
{
    public static LocalizationManager IN;//singleton

    public enum Language
    {
        English,
        Español,
        Francais,
        Italiano,
        Deutch,
        Portuguese,
        Chinese,
        Japanese,
        Korean,
        Count,
        None
    }

    public static Action<int> OnLanguageChanged;

    [Serializable]
    public struct DropDownLocItem
    {
        public string[] Options;
    }

    [Serializable]
    public struct TutorialLocItem
    {
        [TextArea] public string[] Text;
    }

    public Language SelectedLanguage = Language.English;
    [SerializeField] private bool shouldValidate;

    [Header("Language Selector")]
    [SerializeField] private TextMeshProUGUI languageButtonText;
    [SerializeField] private Image flagImage;
    [SerializeField] private Image splashScreenImage;

    [SerializeField] private Sprite[] flagSprites;
    [SerializeField] private Sprite[] splashScreenSprites;

    [SerializeField] private bool shouldFadeOutLanguageText;

    [HideInInspector] public List<string[]> AllTags = new();

    [ShowInInspector]
    private Dictionary<string, string> allLocTagsDict = new();
    [ShowInInspector]
    private Dictionary<string, string[]> elementLocTagsDict = new();
    private List<LocalizationItem> localizationItems = new List<LocalizationItem>();

    private void OnValidate()
    {
        if (!this.shouldValidate) return;

        this.shouldValidate = false;

        SelectLanguage((int)this.SelectedLanguage);
    }

    [Header("Splash Screen")]
    [Header("Auto-Localize ----------------------------")]
    [Space(50)]
    public string StartReflection;
    [Space(10)]
    [TextArea] public string[] AutoPlay;
    [TextArea] public string[] StopAuto;
    public string[] AutoDealHand;
    public string[] AutoStart;
    public string[] ChoosePowerCard;
    public string[] ChoosePowerCardType;
    [TextArea] public string[] ClearData;
    public string[] Continue;
    public string[] Copy;
    public string[] CreateDeck;
    [TextArea] public string[] DealHand;
    public string[] DeckName;
    [TextArea] public string[] DeckSize;
    public string[] Defeat;
    public string[] Discard;
    [TextArea] public string[] DiscardToEndTurn;
    public string[] Edit;
    [TextArea] public string[] EditDeck;
    public string[] Editing;
    public string[] Email;
    [TextArea] public string[] EndTurn;
    public string[] Export;
    public string[] GameDeck;
    public string[] Hide;
    public string[] HomeScreen;
    public string[] HideAll;
    public string[] ImportDeck;
    public string[] Load;
    public string[] LoadingCards;
    public string[] Moves;
    [TextArea] public string[] MovesLeft;
    public string[] NewDeck;
    public string[] NoCards;
    public string[] Opponent;
    public string[] OpponentA;
    public string[] PasteJSONDeckData;
    public string[] Play;
    public string[] PlayAgain;
    public string[] PlayGame;
    public string[] Replay;
    public string[] Undo = { "Undo", "Deshacer" };
    [TextArea] public string[] Rename;
    public string[] Save;
    public string[] SavedDecks;
    [TextArea] public string[] SetDefault;
    public string[] ShowAll;
    public string[] ShowSplashScreen;
    public string[] SpeedUp;
    public string[] Submit;
    public string[] Tie;
    public string[] Total;
    public string[] Turn;
    public string[] Tutorial;
    public string[] Use;
    public string[] Victory;
    public string[] View;

    [Header("Filters")]
    public string[] Filters;
    public string[] Points;
    public string[] Multiply;
    public string[] Bombs;
    public string[] Shields;
    public string[] Single;
    public string[] Double;
    public string[] Triple;
    public string[] Wild;
    public string[] Combo;
    public string[] Hide0s;
    public string[] Show0s;

    [Header("Settings")]
    public string[] Settings;
    public string[] General;
    public string[] GamePlay;
    public string[] Skins;
    public string[] EffectsVolume;
    public string[] MusicVolume;
    public string[] ChooseLanguage;
    public string[] CardsToDeal;
    public string[] MaxCardsInHand;
    public string[] MovesPerTurn;
    public string[] ElementZoneHealth;
    public string[] ElementZonePointsToActivate;
    public string[] TotalPointsToWin;
    public string[] FreezeZoneHealth;
    public string[] LightningDamage;
    public string[] DiscardMode;
    public string[] PlayMode;
    public string[] AiDifficulty;
    public string[] DataSaved;
    public string[] ChooseSkin;
    public string[] CardBackStyle;
    [TextArea] public string[] ApplySkin;
    [TextArea] public string[] ResetData;
    [TextArea] public string[] RestoreDefaults;

    [Header("Confirm Panel")]
    public string[] Yes;
    public string[] No;
    public string[] Confirm;
    public string[] Cancel;
    public string[] Dismiss;
    public string[] One;
    public string[] All;

    [Header("Misc")]
    public string[] Close;
    [Space(10)]
    public string EndReflection;

    [Header("Script Localized ----------------------------")]
    [Space(50)]
    public string[] Loading;
    public string[] MustBeXOrMore;

    [Header("Reject Reasons")]
    [TextArea] public string[] Reject_HandIsFull;// "Your Hand is Full!";
    [TextArea] public string[] Reject_OutOfMoves;//"You're out of moves!\nDiscard a card to continue."
    [TextArea] public string[] Reject_DestroyedZone;//"You cannot place a card\non a destroyed Element Zone"
    [TextArea] public string[] Reject_ElementZoneFull;//$"{this.ElementType} Zone is Full!"
    [TextArea] public string[] Reject_GameDeck;//"You cannot place a card\nonto the Game Deck!"
    [TextArea] public string[] Reject_CardOnOpponentBoard;//"You cannot place cards in another player's game board!"
    [TextArea] public string[] Reject_CardInOpponentHand;// "You cannot place cards in another player's hand!"
    public string[] Reject_InvalidDropLocation;//"Invalid drop location!"
    [TextArea] public string[] Reject_DoesNotContainElement;//"Card does not contain\nan Element from Element Zone!"
    [TextArea] public string[] Reject_TutorialBlocked;//"Please place card where indicated by tutorial!"
    [Space]
    [TextArea] public string[] Reject_BombDoesNotContainElement;//"Bomb Card does not contain\nan Element from Element Zone!"
    [TextArea] public string[] Reject_BombOnOwnZone;//"You cannot place a bomb card\non your own Element Zone!"
    [TextArea] public string[] Reject_BombOnEmpty;//"You cannot place a Bomb Card on an empty Element Zone!";
    [TextArea] public string[] Reject_BombOnDestroyed;//"You cannot place a Bomb Card on a destroyed Element Zone!";
    [TextArea] public string[] Reject_BombOnFrozenOpponent;//"You cannot play a Bomb Card\non another player's frozen Element Zone!"
    [Space]
    [TextArea] public string[] Reject_ZoneMustBeEmptyToHeal;//"Zone must be empty to Heal!"
    [TextArea] public string[] Reject_CannotHealFrozenZone;//"Cannot heal Frozen Zone!"
    [TextArea] public string[] Reject_ZoneHasNoDamage;//"Zone has no damage!"
    [TextArea] public string[] Reject_LightningOnDestroyed;//"You cannot place a Lightning Card on a destroyed Element Zone!";
    [TextArea] public string[] Reject_LightningOnOwnBoard;//"You cannot play a Lightning Card on Your Own Board!"
    [TextArea] public string[] Reject_LightningFrozenOpponent;//"You cannot play a Lightning Card\non another player's frozen Element Zone!"
    [Space]
    [TextArea] public string[] Reject_FrozenZone;//"You cannot play a card on a Frozen Element Zone!"
    [TextArea] public string[] Reject_AlreadyFrozenZone;//"This Element Zone is already frozen!"

    [Header("Misc")]
    public string[] GameSpeed;
    public string[] InitCards;////$"Init Cards: {inValue}";
    public string[] OpponentX;//$"Opponent {this.currentPlayerIndex}";
    public string[] OpponentsTurn;//"Opponent's Turn"
    public string[] PassToPlayerX;//$"Pass to Player {N}"
    public string[] TapToContinue;//"Tap to Continue"
    [Header("Online Play")]
    public string[] OnlineUsername = { "Choose a username", "Elige un nombre de usuario" };
    public string[] OnlineUsernameHint = { "3-20 letters, numbers or underscores", "3-20 letras, números o guiones bajos" };
    public string[] OnlineUsernamePlaceholder = { "Username", "Nombre de usuario" };
    public string[] OnlineCheckUsername = { "Check availability", "Comprobar disponibilidad" };
    public string[] OnlineUsernameAvailable = { "Username available", "Nombre disponible" };
    public string[] OnlineUsernameUnavailable = { "Username unavailable or invalid", "Nombre no disponible o no válido" };
    public string[] OnlineContinue = { "Continue", "Continuar" };
    public string[] OnlineLobby = { "Online Match", "Partida en línea" };
    public string[] OnlineCreateMatch = { "Create Match", "Crear partida" };
    public string[] OnlineJoinMatch = { "Join Match", "Unirse a partida" };
    public string[] OnlineInviteCode = { "Join with a code", "Unirse con un código" };
    public string[] OnlineInviteCodePlaceholder = { "Enter code", "Introduce el código" };
    public string[] OnlineInviteCodeLabel = { "Invite Code", "Código de invitación" };
    public string[] OnlineWaiting = { "Waiting for Opponent", "Esperando al rival" };
    public string[] OnlineWaitingForOpponent = { "Waiting for opponent...", "Esperando al rival..." };
    public string[] OnlineBack = { "Back", "Volver" };
    public string[] OnlineWorking = { "Connecting…", "Conectando…" };
    public string[] OnlineError = { "Connection failed. Please try again.", "Error de conexión. Inténtalo de nuevo." };
    public string[] OnlineInvalidInvite = { "Enter a six-character invite code", "Introduce un código de seis caracteres" };
    public string[] OnlineSendingTurn = { "Sending turn...", "Enviando turno..." };
    public string[] OnlineTurnRetry = { "Couldn't send turn. Press End Turn to retry.", "No se pudo enviar el turno. Pulsa Fin de turno para reintentar." };
    public string[] OnlineTurnRejected = { "Turn rejected, resyncing", "Turno rechazado, resincronizando" };
    public string[] OnlineSkipReplay = { "Skip", "Omitir" };
    public string[] OnlineDiscardUnsent = { "Discard your unsent moves?", "¿Descartar tus movimientos no enviados?" };
    public string[] OnlineMoveIllegal = { "That move is not legal in the current online turn.", "Ese movimiento no es válido en el turno en línea actual." };
    public string[] OnlineOpponentLeft = { "The other player has left the game", "El otro jugador ha abandonado la partida" };
    public string[] OnlineMatchSettings = { "The creator's gameplay settings apply to both players.", "Los ajustes de juego del creador se aplican a ambos jugadores." };
    public string[] OnlineYouGoFirst = { "You go first!", "¡Empiezas tú!" };
    public string[] OnlineOpponentGoesFirst = { "Opponent goes first", "Empieza el rival" };
    public string[] AllowUndo = { "Allow Undo", "Permitir deshacer" };
    public string[] UndoPastDraw = { "Can't undo past a draw.", "No puedes deshacer más allá de un robo." };
    public string[] UndoPastReveal = { "Can't undo past revealing a hidden card.", "No puedes deshacer más allá de revelar una carta oculta." };
    public string[] UndoPastActivateDeck = { "Can't undo past Activate Deck.", "No puedes deshacer más allá de Activar Mazo." };
    public string[] UndoPastPowerChoice = { "Can't undo past choosing a power card.", "No puedes deshacer más allá de elegir una carta de poder." };
    public string[] UndoTurnLocked = { "This turn can no longer be undone.", "Este turno ya no se puede deshacer." };

    public static string GetOnlineText(string[] inStrings)
    {
        if (inStrings == null || inStrings.Length == 0)
            return string.Empty;
        int index = LOC.LI;
        return index >= 0 && index < inStrings.Length && !string.IsNullOrEmpty(inStrings[index])
            ? inStrings[index] : inStrings[0];
    }
    public string[] YourTurn;//"Your Turn";
    public string[] YourTurnAuto;//"Your Turn <i>(auto)</i>"
    public string[] InfoAboutYourGame;//"Here is some info about your game:"
    public string[] You;//"You"
    [TextArea] public string[] ClickButtonToDrawCards;//"Click Button\n To Draw Cards";
    [TextArea] public string[] ClickButtonToEndTurn;//"Click Button\n To End Turn";

    [Header("Settings")]
    public string[] LeaveGame;//"Leave Game?"
    [TextArea] public string[] LeaveGameMessage;//$"Are you sure you want to leave the game?\n\nAll progress will  be lost."
    public string[] RestoreDefaultsTitle;//"Restore Defaults?";
    [TextArea] public string[] RestoreDefaultsMessage;//$"Are you sure you want to restore all settings to default?";
    public string[] ResetDataTitle;//"Reset Data?";
    [TextArea] public string[] ResetDataMessage;//$"Are you sure you want to Delete All Data?\nThis will quit the app!";
    public string[] ReturnHomeToChangeMode = { "Return Home to change mode", "Vuelve al inicio para cambiar de modo" };

    [Header("Dropdown Options")]
    public DropDownLocItem[] DropdownOptions_DiscardMode;
    public DropDownLocItem[] DropdownOptions_PlayMode;
    public DropDownLocItem[] DropdownOptions_AiDifficulty;
    public DropDownLocItem[] DropdownOptions_SkinType;
    public DropDownLocItem[] DropdownOptions_CardBackStyle;

    [Header("Confirm Panel")]
    [TextArea] public string[] Confirm_DeleteDeck;//$"Are you sure you want to delete\n<color=#FFFFFF><i>{this.deck.Name}</i></color>?";
    [TextArea] public string[] Confirm_SetDeckDefault;//$"Are you sure you want to set\n<color=#FFFFFF><i>{this.deck.Name}</i></color>\nto a Default Deck?";
    [TextArea] public string[] Confirm_SaveChangesBeforeEdit;// $"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before editing <color=#FFFFFF><i>{this.deck.Name}</i></color>?";
    [TextArea] public string[] Confirm_SaveCopyBeforeSet; //$"Would you like to save a copy of <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?";
    [TextArea] public string[] Confirm_SaveChangesBeforeSet;//$"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?";
    [TextArea] public string[] Confirm_SaveChangesBeforeSet2;//$"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting it as the Game Deck?";
    [TextArea] public string[] Confirm_SetWarnRestart;//$"Are you sure you to set <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?\n\nIt will restart the current game!";
    [TextArea] public string[] Confirm_UsePower;//$"Are you sure you want to use Activation Power?\n\nIt will give you a powerful card, but remove all the cards and points from this zone.";
    [Space]
    [TextArea] public string[] Warn_CannotOverwriteDefaultDeck;//"You cannot overwrite a Default Deck!";
    [TextArea] public string[] Prompt_SaveDeckCopy;//$"{reasonText}\n\nWould you like to save a copy of <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color>?";
    [TextArea] public string[] Prompt_WarnRestartGame;//$"<color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color>, is the current Game Deck\n\nOverwriting it will restart the game when you leave the Deck Editor!";
    [TextArea] public string[] Prompt_WarnRestartGame2; //$"Are you sure you want to set <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> as the Game Deck?\n\nIt will restart the current game!";
    [TextArea] public string[] Prompt_WarnMinDeckSize;//$"Must be {CardsManager.IN.MinDeckSize}\nor more!";

    [Space]
    [TextArea] public string[] PointsRatioToWin;//$"{inElementZone.Points}/{SettingsManager.IN.ElementZonePointsRequiredToWin} {inElementZone.CardCell.ElementType} Points!"
    [TextArea] public string[] YouActivatedElementZone;//$"You have earned enough {inElementZone.CardCell.ElementType} Points to win.\n\nIf your opponnent cannot lower your points or excede yours, you will win the beginning of next turn."
    [TextArea] public string[] OpponentActivatedElementZone;//$"Your opponent has earned enough {inElementZone.CardCell.ElementType} Points to win.\n\nIf you cannot lower their points or excede them, they will win the beginning of next turn.";

    public string[] TotalPointsTitle;//$"{inPlayerBoard.TotalPoints}/{SettingsManager.IN.TotalPointsRequiredToWin} Total Points!";
    public string[] AllZonesDestroyedTitle;//"All Opponent's Zones Destroyed!"
    [Space]
    [TextArea] public string[] TurnAndMoveCount;//$"Turn: {this.CurrentTurnNumber}   Move: {this.CurrentPlayerBoard.CurrentMoveNumber}\nTotal Moves: {this.CurrentPlayerBoard.TotalMovesTaken}"

    [Header("Power Cards Popup")]
    public string[] Random;
    public string[] Heal;
    public string[] Freeze;
    public string[] Lightning;

    [Space]
    [TextArea] public string[] RandomDesc;
    [TextArea] public string[] HealDesc;
    [TextArea] public string[] FreezeDesc;
    [TextArea] public string[] LightningDesc;

    [Header("Elements - Sync with SkinManager.SkinDatas")]
    public ElementLocItem[] AirLocKeys;
    public ElementLocItem[] EarthLocKeys;
    public ElementLocItem[] FireLocKeys;
    public ElementLocItem[] WaterLocKeys;
    public ElementLocItem[] WildLocKeys;

    [Header("Tutorial")]
    public string[] LeaveTutorial;//"Leave Tutorial?"
    [TextArea] public string[] LeaveTutorialMessage;//$"Are you sure you want to leave the tutorial?"
    public TutorialLocItem[] TutorialLocItems;

    public void CreateListsOfArrays()
    {
        if (this.AllTags.Count > 0)
            return;

        bool shouldAddToAllTagsList = false;

        FieldInfo[] fieldInfos = this.GetType().GetFields();
        //Debug.Log("fieldInfos.Length = " + fieldInfos.Length);

        foreach (FieldInfo fieldInfo in fieldInfos)
        {
            //Debug.Log("CHECK(" + fieldInfo.Name + ")");
            if (fieldInfo.Name == "EndReflection")
                shouldAddToAllTagsList = false;

            if (shouldAddToAllTagsList && fieldInfo.FieldType == typeof(String[]))
            {
                string[] varToAdd = (string[])fieldInfo.GetValue(this);
                this.AllTags.Add(varToAdd);
                //Debug.Log("<color=yellow>ADD(" + fieldInfo.Name + ")</color>");
            }

            if (fieldInfo.Name == "StartReflection")
                shouldAddToAllTagsList = true;
        }

        CreateElementLocKeysDict();

        void CreateElementLocKeysDict()
        {
            foreach (var airItem in this.AirLocKeys)
            {
                this.elementLocTagsDict.Add(airItem.Key, airItem.Values);
            }

            foreach (var earthItem in this.EarthLocKeys)
            {
                this.elementLocTagsDict.Add(earthItem.Key, earthItem.Values);
            }

            foreach (var fireItem in this.FireLocKeys)
            {
                this.elementLocTagsDict.Add(fireItem.Key, fireItem.Values);
            }

            foreach (var waterItem in this.WaterLocKeys)
            {
                this.elementLocTagsDict.Add(waterItem.Key, waterItem.Values);
            }

            foreach (var wildItem in this.WildLocKeys)
            {
                this.elementLocTagsDict.Add(wildItem.Key, wildItem.Values);
            }
        }
    }

    public string GetElementSkinName(string inKey)
    {
        //Debug.Log($"GetElementSkinName({inKey})");
        if (this.elementLocTagsDict.Count > 0 && this.elementLocTagsDict.ContainsKey(inKey))
        {
            return this.elementLocTagsDict[inKey][LOC.LI];
        }

        if (this.elementLocTagsDict.Count > 0)
            Debug.Log($"<color=red><FAIL> GetElementSkinName()   inKey = [{inKey}]</color>");

        return $"<FAIL>";
    }

    public string GetElementNameFromEnum(EElementType inElementType)
    {
        int elementIndex = (int)inElementType - 1;

        var locKey = SkinManager.IN.SelectedSkinData.ElementNames[elementIndex];

        var localizedElementName = GetElementSkinName(locKey);

        return localizedElementName;
    }

    public void HideLanguageText()
    {
        this.languageButtonText.CrossFadeAlpha(0, 0, false);
    }

    public void RegisterLocalizationItem(LocalizationItem item)
    {
        CreateListsOfArrays();

        this.localizationItems.Add(item);

        UpdateItemText(item);
    }

    private void UpdateItemText(LocalizationItem item)
    {
        string textString;

        if (item == null)
            return;

        if (item.localizedTexts.Length == 0)
        {
            foreach (string[] textArray in this.AllTags)
            {
                if (textArray.Length > 0)
                {
                    if (item.GetText() == textArray[0])
                    {
                        item.localizedTexts = textArray;
                        break;
                    }
                }
            }
        }

        if (item.localizedTexts.Length > 0)
            textString = item.localizedTexts[(int)this.SelectedLanguage];
        else
        {
            textString = "---";

            if (Application.isEditor)
            {
                string englishText = item.GetText();
                string key = englishText.Replace(" ", "");

                if (!this.allLocTagsDict.ContainsKey(key))
                {
                    this.allLocTagsDict.Add(key, englishText);
                }
            }
        }

        item.SetText(textString);
    }

    public void HandlePrevLanguageChangeButtonPress()
    {
        --PlayerData.Data.LanguageIndex;

        if (LOC.LI < 0)
            PlayerData.Data.LanguageIndex = this.flagSprites.Length - 1;

        SelectLanguage(LOC.LI);
    }

    public void HandleLanguageChangeButtonPress()
    {
        ++PlayerData.Data.LanguageIndex;
        PlayerData.Data.LanguageIndex %= this.flagSprites.Length;

        SelectLanguage(LOC.LI);
    }

    public void SelectLanguage(int inLanguageInt)
    {
        PlayerData.Data.LanguageIndex = inLanguageInt;

        this.SelectedLanguage = (Language)inLanguageInt;

        OnLanguageChanged?.Invoke(inLanguageInt);

        if (this.flagImage != null)
            this.flagImage.sprite = this.flagSprites[LOC.LI];

        if (this.splashScreenImage != null)
            this.splashScreenImage.sprite = this.splashScreenSprites[LOC.LI];

        if (this.shouldFadeOutLanguageText)
        {
            this.languageButtonText.CrossFadeAlpha(1, 0, false);
            this.languageButtonText.CrossFadeAlpha(0, 1.5f, false);
        }

        if (this.languageButtonText != null)
            this.languageButtonText.text = this.SelectedLanguage.ToString().ToUpper();

        foreach (LocalizationItem item in this.localizationItems)
            UpdateItemText(item);
    }

    public void PrintTagsList()
    {
        string output = string.Empty;

        foreach (KeyValuePair<string, string> pair in this.allLocTagsDict)
        {
            output += "Key: " + pair.Key + " = " + pair.Value + "\n";
        }

        Debug.Log(output);
    }

    [Serializable]
    public class ElementLocItem
    {
        public string Key;
        public string[] Values;
    }
}
