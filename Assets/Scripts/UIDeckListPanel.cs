using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIDeckListPanel : UIPanelBase
{
    public static UIDeckListPanel IN;

    [SerializeField] private VerticalLayoutGroup content;
    [SerializeField] private UIDeckItem deckItemPrefab;

    [Space]
    [SerializeField] private Slider initCardsSlider;
    [SerializeField] private TextMeshProUGUI initCardsSliderText;

    [Space]
    [SerializeField] private UIImportDeckPopup importDeckPopup;

    public override void Show()
    {
        base.Show();

        HandleSliderChanged(0);

        this.importDeckPopup.Hide();

        RefreshList();
    }

    public void SpawnItem(Deck inDeck)
    {
        var go = Instantiate(this.deckItemPrefab.gameObject, this.content.transform);

        var deckItem = go.GetComponent<UIDeckItem>();

        //bool isDefaultDeck = this.content.transform.childCount == 0;

        deckItem.Configure(inDeck);
    }

    public Deck CopyDeck(Deck inDeckToCopy)
    {
        var deck = Deck.Clone(inDeckToCopy, -1);

        int counter = 0;
        foreach (var kvp in PlayerData.Data.DecksDict)
        {
            if (kvp.Value.Name.Contains(deck.Name))
                ++counter;
        }

        if (counter > 0)
            deck.Name = $"{deck.Name} ({counter})";

        SpawnItem(deck);

        PlayerData.Data.DecksDict.Add(deck.ID, deck);

        PlayerData.Data.IsDirty = true;

        return deck;
    }

    public void HandleSliderChanged(Single inValue)
    {
        this.initCardsSliderText.text = string.Format(LOC.IN.InitCards[LOC.LI], inValue);//$"Init Cards: {inValue}";
    }

    public override void Hide()
    {
        DestroyAllChildren();

        base.Hide();
    }

    private void DestroyAllChildren()
    {
        var children = this.content.transform.GetComponentsInChildren<UIDeckItem>(true);
        foreach (var child in children)
        {
            Destroy(child.gameObject);
        }
    }

    public void RefreshList()
    {
        DestroyAllChildren();

        foreach (var kvp in PlayerData.Data.DecksDict)
        {
            SpawnItem(kvp.Value);
        }
    }

    public void HandleNewDeckButtonPress()
    {
        var deck = Deck.Clone(CardsManager.IN.DefaultDeck, (int)this.initCardsSlider.value);

        int counter = 0;

        var newDeckName = LOC.IN.NewDeck[LOC.LI];

        foreach (var kvp in PlayerData.Data.DecksDict)
        {
            if (kvp.Value.Name.Contains(newDeckName))
                ++counter;
        }

        if (counter > 0)
            deck.Name = $"{newDeckName} ({counter})";

        SpawnItem(deck);

        PlayerData.Data.DecksDict.Add(deck.ID, deck);

        PlayerData.Data.IsDirty = true;
    }

    public void HandleImportDeckButtonPress()
    {
        this.importDeckPopup.Show();
    }
}