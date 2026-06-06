using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIDeckItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI deckNameText;
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private GameObject inputFieldPopup;
    [SerializeField] private GameObject[] inputButtons;
    [SerializeField] private TextMeshProUGUI cardCountText;
    [Space]
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button renameButton;
    [SerializeField] private Button setDefaultButton;
    [SerializeField] private Button editButton;
    [SerializeField] private Button useButton;
    [SerializeField] private TextMeshProUGUI editButtonText;
    [SerializeField] private TextMeshProUGUI useButtonText;
    [Space]
    [SerializeField] private Image background;
    [SerializeField] private Color[] backgroundColors;
    [SerializeField] private Color[] textColors;
    [SerializeField] private Color editButtonActiveColor;
    [SerializeField] private Color useButtonActiveColor;
    [SerializeField] private Color buttonInactiveColor;

    private Deck deck;
    private bool isActiveDeck;
    private bool isEditingDeck;
    private bool isDefaultDeck;

    public void Configure(Deck inDeck)
    {
        this.deck = inDeck;

        HideInputField();

        #if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        this.inputField.onEndEdit.AddListener(delegate
        {
            OnInputSubmit(this.inputField.text);
        });

        foreach (var button in this.inputButtons)
        {
            button.SetActive(false);
        }   
        #endif

        this.isActiveDeck = inDeck.ID.Equals(PlayerData.Data.ActiveDeckId);
        this.isEditingDeck = inDeck.ID.Equals(UIDeckEditor.IN.EditingDeck.ID);
        this.isDefaultDeck = inDeck.IsDefault;

        this.background.color = this.isActiveDeck ? this.backgroundColors[0] : this.backgroundColors[1];

        if(Application.isEditor)
            this.setDefaultButton.gameObject.SetActive(!this.isDefaultDeck);
        else
            this.setDefaultButton.gameObject.SetActive(false);

        this.deleteButton.gameObject.SetActive(Application.isEditor || !this.isDefaultDeck && !this.isActiveDeck && !isDefaultDeck);
        this.renameButton.gameObject.SetActive(Application.isEditor || !this.isDefaultDeck && !this.isActiveDeck && !this.isDefaultDeck);

        bool shouldEnableEditButton = !this.isEditingDeck;
        this.editButton.interactable = shouldEnableEditButton;
        this.editButtonText.text = this.isDefaultDeck ? LOC.IN.View[LOC.LI] : LOC.IN.Edit[LOC.LI];//"View" : "Edit";
        this.editButtonText.color = shouldEnableEditButton ? this.editButtonActiveColor : this.buttonInactiveColor;

        bool shouldEnableUseButton = !this.isActiveDeck;
        this.useButton.interactable = shouldEnableUseButton;
        this.useButtonText.color = shouldEnableUseButton ? this.useButtonActiveColor : this.buttonInactiveColor;

        SetDeckNameText(inDeck.Name);

        this.cardCountText.text = inDeck.GetTotalCardsCount().ToString();
    }

    private void SetDeckNameText(string inName)
    {
        this.deckNameText.text = this.isEditingDeck ? $"* {inName}" : inName;

        if (this.isActiveDeck)
            this.deckNameText.color = this.textColors[0];
        else if(this.isDefaultDeck)
            this.deckNameText.color = this.textColors[1];
        else
            this.deckNameText.color = this.textColors[2];
    }

    public void HandleDeleteButtonPress()
    {
        //$"Are you sure you want to delete\n<color=#FFFFFF><i>{this.deck.Name}</i></color>?";
        var messageText = string.Format(LOC.IN.Confirm_DeleteDeck[LOC.LI], this.deck.Name);
        UIConfirmPanel.IN.Show(messageText, () =>
        {
            PlayerData.Data.DecksDict.Remove(this.deck.ID);
            PlayerData.Data.IsDirty = true;

            if (this.isEditingDeck)
            {
                UIDeckEditor.IN.DisplayDeck(CardsManager.IN.DefaultDeck);
                UIDeckListPanel.IN.RefreshList();
            }
            else
                Destroy(this.gameObject);
        });
    }

    public void HandleSetDefaultButtonPress()
    {
        //$"Are you sure you want to set\n<color=#FFFFFF><i>{this.deck.Name}</i></color>\nto a Default Deck?";
        var messageText = string.Format(LOC.IN.Confirm_SetDeckDefault[LOC.LI], this.deck.Name);
        UIConfirmPanel.IN.Show(messageText, () =>
        {
            this.deck.IsDefault = true;

            var deck = Deck.Clone(this.deck, -1);
            deck.IsDefault = true;//do this manually here, not inside Deck.Clone()

            int counter = 0;
            foreach (var kvp in DefaultDecks.Data.DecksDict)
            {
                if (kvp.Value.Name.Contains(deck.Name))
                    ++counter;
            }

            if (counter > 0)
                deck.Name = $"{deck.Name} ({counter})";

            DefaultDecks.Data.DecksDict.Add(deck.ID, deck);

            DefaultDecks.Data.IsDirty = true;

            Configure(this.deck);

            //refresh buttons on deck editor
            if (this.isEditingDeck)
                UIDeckEditor.IN.DisplayDeck(this.deck);
        });
    }

    public void HandleRenameButtonPress()
    {
        this.inputFieldPopup.SetActive(true);
        this.inputField.Select();
        this.inputField.text = this.deck.Name;
    }

    public void OnInputSubmit(string inValue)
    {
        var enteredText = this.inputField.text;

        if (enteredText.Length > 0 && !enteredText.Equals(this.deck.Name))
        {
            int counter = 0;
            foreach (var kvp in PlayerData.Data.DecksDict)
            {
                if (kvp.Value.Name.Contains(enteredText))
                    ++counter;
            }

            if (counter > 0)
                enteredText = $"{enteredText} ({counter})";

            this.deck.Name = enteredText;
            SetDeckNameText(enteredText);

            if (this.isEditingDeck)
                UIDeckEditor.IN.EditingDeckNameText.text = enteredText;

            HideInputField();
            PlayerData.Data.IsDirty = true;
        }
        else
            HideInputField();
    }

    public void HideInputField()
    {
        this.inputFieldPopup.SetActive(false);
    }

    public void HandleCopyButtonPress()
    {
        UIDeckListPanel.IN.CopyDeck(this.deck);
    }

    public void HandleEditButtonPress()
    {
        if (UIDeckEditor.IN.IsEditingDeckDirty)
        {
            // $"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before editing <color=#FFFFFF><i>{this.deck.Name}</i></color>?";
            var messageText = string.Format(LOC.IN.Confirm_SaveChangesBeforeEdit[LOC.LI], UIDeckEditor.IN.EditingDeck.Name, this.deck.Name);
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                UIDeckEditor.IN.TrySaveDeck();
                UIDeckEditor.IN.DisplayDeck(this.deck);
                UIDeckListPanel.IN.Hide();
            });
        }
        else
        {
            UIDeckEditor.IN.DisplayDeck(this.deck);
            UIDeckListPanel.IN.Hide();
        }
    }

    public void HandleUseButtonPress()
    {
        if (UIDeckEditor.IN.IsEditingDeckDirty)
        {
            if(UIDeckEditor.IN.IsEditingDeckDefaultDeck)
            {
                //$"Would you like to save a copy of <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?";
                var messageText = string.Format(LOC.IN.Confirm_SaveCopyBeforeSet[LOC.LI], UIDeckEditor.IN.EditingDeck.Name, this.deck.Name);
                UIConfirmPanel.IN.Show(messageText, () =>
                {
                    UIDeckEditor.IN.CopyEditingDeck(false);
                    SetActiveDeckAndCloseMenus(this.deck);
                });
            }
            else
            {
                //$"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?";
                var messageText = string.Format(LOC.IN.Confirm_SaveChangesBeforeSet[LOC.LI], this.deck.Name);
                UIConfirmPanel.IN.Show(messageText, () =>
                {
                    UIDeckEditor.IN.SaveDeck(UIDeckEditor.IN.EditingDeck);
                    SetActiveDeckAndCloseMenus(this.deck);
                });
            }
        }
        else
        {
            //$"Are you sure you to set <color=#FFFFFF><i>{this.deck.Name}</i></color> as the Game Deck?\n\nIt will restart the current game!";
            var messageText = string.Format(LOC.IN.Confirm_SetWarnRestart[LOC.LI], this.deck.Name);
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                SetActiveDeckAndCloseMenus(this.deck);
            });
        }

        static void SetActiveDeckAndCloseMenus(Deck inDeck)
        {
            PlayerData.Data.ActiveDeckId = inDeck.ID;
            PlayerData.Data.IsDirty = true;
            UIDeckListPanel.IN.Hide();
            UIDeckEditor.IN.Hide();
        }
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID || UNITY_IOS
        this.inputField.onEndEdit.RemoveAllListeners();
#endif
    }
}