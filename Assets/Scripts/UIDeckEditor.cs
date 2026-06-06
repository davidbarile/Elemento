using System;
using System.Collections;
using System.Collections.Generic;
using Leguar.TotalJSON;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CardData;

public class UIDeckEditor : UIPanelBase
{
    public static UIDeckEditor IN;

    public EFilterFlags FilterFlags;
    [ReadOnly]
    [Space]
    [SerializeField] private List<UIEditCardItem> editCardItems = new();
    [ReadOnly]
    [Space]
    [SerializeField] private ExportItem exportItem;
    [Space]
    [SerializeField] private RectTransform deckEditGrid;
    public TextMeshProUGUI EditingDeckNameText => this.editingDeckNameText;

    [SerializeField] private TextMeshProUGUI editingDeckNameText;
    [SerializeField] private TextMeshProUGUI activeGameDeckNameText;
    [SerializeField] private GameObject activeGameDeckNameDisplay;
    [SerializeField] private Color[] deckNameTextColors;
    [SerializeField] private TextMeshProUGUI deckCountText;
    [SerializeField] private TextMeshProUGUI deckCountTooSmallText;
    [SerializeField] private ResetScrollRectOnEnable resetScrollRect;
    [Space]
    [SerializeField] private Button saveButton;
    [SerializeField] private TextMeshProUGUI saveButtonText;
    [SerializeField] private Color[] saveButtonTextColors;
    [Space]
    [SerializeField] private Button useButton;
    [SerializeField] private TextMeshProUGUI useButtonText;
    [SerializeField] private Color[] useButtonTextColors;
    [Space]
    [SerializeField] private GameObject loadingScreen;
    [Space]
    [SerializeField] private UIDeckExportedPopup deckExportedPopup;
    [Space]
    [SerializeField] private UIFiltersMenu filtersMenu;
    [Space]
    [SerializeField] private UIEditCardItem editCardItemPrefab;

    private Action onHide;
    private int deckCount;
    public Deck EditingDeck { get; private set; }
    public bool IsEditingDeckDirty { get; private set; }
    public bool IsEditingDeckDefaultDeck { get; private set; }
    private bool isEditingDeckActiveDeck;
    private bool isEditingDeckTooSmall;

    private string originalDeckId;
    private bool isActiveDeckDirty;

    public void Show(Action inOnHide)
    {
        this.onHide = inOnHide;

        this.loadingScreen.SetActive(true);
        this.deckExportedPopup.Hide();
        this.filtersMenu.Hide();

        base.Show();

        StartCoroutine(DelayedShowCo());
    }

    private IEnumerator DelayedShowCo()
    {
        yield return null;

        GameManager.IN.SetGamePaused(true);

        this.EditingDeck = PlayerData.Data.ActiveDeck;
        SetEditingDeckDirty(false);

        PopulateCardsGrid();
        DisplayDeck(this.EditingDeck);

        this.originalDeckId = PlayerData.Data.ActiveDeckId;

        this.filtersMenu.SetAllVisible();

        this.loadingScreen.SetActive(false);
    }

    public void PopulateCardsGrid()
    {
        var allCardDatas = CardsManager.IN.AllCardDatas;

        for (int i = 0; i < allCardDatas.Count; i++)
        {
            var cardData = allCardDatas[i];

            var go = Instantiate(this.editCardItemPrefab, this.deckEditGrid);
            var editItem = go.GetComponent<UIEditCardItem>();

            var card = CardsManager.SpawnCard(cardData, editItem.CardHolder, false, false, true);

            editItem.Card = card;

            var count = PlayerData.Data.ActiveDeck.CardCounts[i];

            editItem.SetCardCount(count);

            this.editCardItems.Add(editItem);

            editItem.OnDeckCountChange += ChangeDeckCount;
        }
    }

    public void DisplayDeck(Deck inDeck)
    {
        this.EditingDeck = inDeck;

        this.IsEditingDeckDefaultDeck = this.EditingDeck.IsDefault;
        this.isEditingDeckActiveDeck = this.EditingDeck.ID.Equals(PlayerData.Data.ActiveDeckId);

        this.saveButtonText.text = this.IsEditingDeckDefaultDeck ? LOC.IN.Copy[LOC.LI] : LOC.IN.Save[LOC.LI];//"Copy" : "Save";

        SetGameAndEditingDeckNames();

        SetEditingDeckDirty(false);

        this.deckCount = 0;
        for (int i = 0; i < this.editCardItems.Count; i++)
        {
            var editItem = this.editCardItems[i];
            int count = this.EditingDeck.CardCounts[i];
            editItem.SetCardCount(count);
            this.deckCount += count;
        }

        SetDeckCountText(this.deckCount);

        this.filtersMenu.SetAllVisible();
    }

    private void SetGameAndEditingDeckNames()
    {
        this.activeGameDeckNameDisplay.SetActive(!this.isEditingDeckActiveDeck);

        if(this.isEditingDeckActiveDeck)
        {
            this.editingDeckNameText.text = this.EditingDeck.Name;
            this.editingDeckNameText.color = this.deckNameTextColors[1];
        }
        else
        {
            this.editingDeckNameText.text = this.EditingDeck.Name;
            this.activeGameDeckNameText.text = PlayerData.Data.ActiveDeck.Name;

            this.editingDeckNameText.color = this.deckNameTextColors[0];
            this.activeGameDeckNameText.color = this.deckNameTextColors[1];
        }
    }

    public void TrySaveDeck()
    {
        if (!this.IsEditingDeckDirty) return;

        if (this.IsEditingDeckDefaultDeck)
        {
            var reasonText = LOC.IN.Warn_CannotOverwriteDefaultDeck[LOC.LI];//"You cannot overwrite the Default Deck!";
            //$"{reasonText}\n\nWould you like to save a copy of <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color>?";
            var messageText = string.Format(LOC.IN.Prompt_SaveDeckCopy[LOC.LI], reasonText, UIDeckEditor.IN.EditingDeck.Name);
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                CopyEditingDeck();
            });
        }
        else if(this.isEditingDeckActiveDeck)
        {
            //$"<color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color>, is the current Game Deck\n\nOverwriting it will restart the game when you leave the Deck Editor!";
            var messageText = string.Format(LOC.IN.Prompt_WarnRestartGame[LOC.LI], UIDeckEditor.IN.EditingDeck.Name);
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                SaveDeck(this.EditingDeck);
            });
        }
        else
            SaveDeck(this.EditingDeck);
    }

    public void CopyEditingDeck(bool inShouldRefreshUI = true)
    {
        var copy = UIDeckListPanel.IN.CopyDeck(UIDeckEditor.IN.EditingDeck);

        SaveDeck(copy);

        if(inShouldRefreshUI)
            UIDeckEditor.IN.DisplayDeck(copy);
    }

    public void SaveDeck(Deck inDeck)
    {
        for (int i = 0; i < this.editCardItems.Count; i++)
        {
            var editItem = this.editCardItems[i];
            inDeck.CardCounts[i] = editItem.Count;
        }

        SetEditingDeckDirty(false);

        PlayerData.Data.IsDirty = true;
    }

    public void ChangeDeckCount(int inDelta)
    {
        this.deckCount += inDelta;

        SetDeckCountText(this.deckCount);

        SetEditingDeckDirty(true);
    }

    private void SetEditingDeckDirty(bool inIsDirty)
    {
        this.IsEditingDeckDirty = inIsDirty;
        SetSaveButtonActive(this.IsEditingDeckDirty);

        if (this.isEditingDeckActiveDeck && inIsDirty)
            this.isActiveDeckDirty = true;

        RefreshUseButton();
    }

    private void SetSaveButtonActive(bool inIsDirty)
    {
        bool isEnabled = inIsDirty || this.IsEditingDeckDefaultDeck;
        this.saveButton.interactable = isEnabled;
        this.saveButtonText.color = isEnabled ? this.saveButtonTextColors[0] : this.saveButtonTextColors[1];
    }

    private void SetDeckCountText(int inCount)
    {
        this.isEditingDeckTooSmall = this.deckCount < CardsManager.IN.MinDeckSize;

        this.deckCountText.text = inCount.ToString();

        this.deckCountTooSmallText.gameObject.SetActive(this.isEditingDeckTooSmall);
        this.deckCountTooSmallText.text = string.Format(LOC.IN.Prompt_WarnMinDeckSize[LOC.LI], CardsManager.IN.MinDeckSize);//$"Must be {CardsManager.IN.MinDeckSize}\nor more!";
    }

    private void RefreshUseButton()
    {
        bool isUnEditedActiveDeck = this.isEditingDeckActiveDeck && !this.isActiveDeckDirty;

        bool shouldShow = (!this.isEditingDeckActiveDeck || !isUnEditedActiveDeck) && !this.isEditingDeckTooSmall;

        this.useButton.interactable = shouldShow;
        this.useButtonText.color = shouldShow ? this.useButtonTextColors[0] : this.useButtonTextColors[1];
    }

    public void ApplyShowHideFilters(EFilterFlags inFilterFlags, bool inShouldShowZeroCount)
    {
        this.FilterFlags = inFilterFlags;

        foreach (var item in this.editCardItems)
        {
            if (item.Count == 0 && !inShouldShowZeroCount)
            {
                item.gameObject.SetActive(false);
            }
            else
            {
                bool shouldShowAll = inFilterFlags == EFilterFlags.None;
                bool containsFlag = (item.Card.Data.FilterFlags & inFilterFlags) == item.Card.Data.FilterFlags;

                item.gameObject.SetActive(containsFlag || shouldShowAll);
            }
        }

        this.resetScrollRect.ResetPosition();
    }

    public void HandleShowFiltersMenuButtonPress()
    {
        this.filtersMenu.Show();
    }

    public override void Hide()
    {
        foreach (var editItem in this.editCardItems)
        {
            editItem.OnDeckCountChange -= ChangeDeckCount;

            editItem.Card.DestroySelf();
            Destroy(editItem.gameObject);
        }

        this.editCardItems.Clear();

        if (PlayerData.Data.IsDirty)
        {
            SaveManager.IN.SaveGame();

            bool isOriginalActiveDeck = this.originalDeckId.Equals(PlayerData.Data.ActiveDeckId);

            if (!UISplashScreen.IN.IsShowing && (!isOriginalActiveDeck || (isOriginalActiveDeck && this.isActiveDeckDirty)))
            {
                GameManager.IN.RestartGame();
            }
        }
        else if (DefaultDecks.Data.IsDirty)
            SaveManager.IN.SaveDefaultDecks();

        base.Hide();

        this.onHide?.Invoke();
    }

    public void HandleExportButtonPress()
    {
        this.exportItem = new ExportItem
        {
            Name = this.EditingDeck.Name
        };

        for (int i = 0; i < this.editCardItems.Count; i++)
        {
            var editItem = this.editCardItems[i];
            this.exportItem.Counts.Add(editItem.Count);
            this.exportItem.NumCards += editItem.Count;
        }

        JSON exportItemJSON = JSON.Serialize(this.exportItem);

        string jsonAsString = exportItemJSON.CreateString();
        //string jsonAsString = exportItemJSON.CreatePrettyString();

        TextEditor te = new TextEditor
        {
            text = jsonAsString
        };

        te.SelectAll();
        te.Copy();

        //Debug.Log($"EXPORTED: {jsonAsString}");

        this.deckExportedPopup.Show(this.exportItem, jsonAsString);
    }

    public void HandleSaveButtonPress()
    {
        if (this.IsEditingDeckDefaultDeck)
        {
            CopyEditingDeck();
        }
        else
            TrySaveDeck();
    }

    public void HandleUseButtonPress()
    {
        if (this.IsEditingDeckDirty)
        {
            string messageText = string.Empty;
            if (this.IsEditingDeckDefaultDeck)
            {
                var reasonText = LOC.IN.Warn_CannotOverwriteDefaultDeck[LOC.LI];// "You cannot overwrite the Default Deck!";
                //$"{reasonText}\n\nWould you like to save a copy of <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> and then restart the game?";
                messageText = string.Format(LOC.IN.Prompt_SaveDeckCopy[LOC.LI], reasonText, UIDeckEditor.IN.EditingDeck.Name);
                UIConfirmPanel.IN.Show(messageText, () =>
                {
                    CopyEditingDeck();
                    SetActiveAndRestartGame();
                });
            }
            else if (this.isEditingDeckActiveDeck)
            {
                //$"<color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color>, is the current Game Deck\n\nOverwriting it will restart the game!";
                messageText = string.Format(LOC.IN.Prompt_WarnRestartGame[LOC.LI], UIDeckEditor.IN.EditingDeck.Name);
                UIConfirmPanel.IN.Show(messageText, () =>
                {
                    SaveDeck(this.EditingDeck);
                    SetActiveAndRestartGame();
                });
            }
            else
            {
                //$"Would you like to save changes made to <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> before setting it as the Game Deck?";
                messageText = string.Format(LOC.IN.Confirm_SaveChangesBeforeSet2[LOC.LI], UIDeckEditor.IN.EditingDeck.Name);
                UIConfirmPanel.IN.Show(messageText, () =>
                {
                    SaveDeck(this.EditingDeck);
                    Hide();
                });
            }
        }
        else if(this.isEditingDeckActiveDeck && this.isActiveDeckDirty)
        {
            SetActiveAndRestartGame();
        }
        else
        {
            //$"Are you sure you want to set <color=#FFFFFF><i>{UIDeckEditor.IN.EditingDeck.Name}</i></color> as the Game Deck?\n\nIt will restart the current game!";
            var messageText = string.Format(LOC.IN.Prompt_WarnRestartGame2[LOC.LI], UIDeckEditor.IN.EditingDeck.Name);
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                SetActiveAndRestartGame();
            });
        }

        void SetActiveAndRestartGame()
        {
            PlayerData.Data.ActiveDeckId = this.EditingDeck.ID;
            PlayerData.Data.IsDirty = true;
            Hide();
        }
    }

    [Serializable]
    public class ExportItem
    {
        public string Name;
        public int NumCards;
        public List<int> Counts = new();
    }
}