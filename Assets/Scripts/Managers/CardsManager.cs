using System;
using System.Collections.Generic;
using CardInstanceData = Elemento.Domain.CardInstanceData;
using Lean.Pool;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using static Card;
using static UICardFlip;

public class CardsManager : MonoBehaviour
{
    public static CardsManager IN;

    //[Header("Set in Inspector to force a type of Power Card")]

    [InlineButton("DealPowerCardFromEditor", "Deal Power Card")]
    public ECardType PowerCardOverride = ECardType.None;

    public int CardSpawnBatchSize = 10;
    public int MinDeckSize = 50;

    [Space]
    [ReadOnly]
    public List<Card> SpawnedCards = new();
    [Space]
    [ReadOnly]
    public List<CardData> AllCardDatas = new();
    public List<CardData> ActiveDeckCardDatas = new();
    public List<int> DefaultCardCounts = new();
    [Space]
    [ReadOnly]
    public List<CardData> ShuffledCardDatas = new();
    [Space]
    [ReadOnly]
    public List<CardData> TutorialCardDatas = new();

    [Space]
    [ReadOnly]
    public List<CardData> PowerCardDatas = new();
    [ReadOnly]
    public List<CardData> SinglePowerCardDatas = new();

    public Deck DefaultDeck { get; private set; }

    [Space]
    [SerializeField] private bool shouldIncludeTripleConfigs;

    [Space]
    [SerializeField] private CardConfig[] singlePointConfigs;
    [SerializeField] private CardConfig[] doublePointConfigs;
    [SerializeField] private CardConfig[] triplePointConfigs;
    [SerializeField] private CardConfig[] wildPointConfigs;

    [Space]
    [SerializeField] private CardConfig[] singleMultiplierConfigs;
    [SerializeField] private CardConfig[] doubleMultiplierConfigs;
    [SerializeField] private CardConfig[] tripleMultiplierConfigs;
    [SerializeField] private CardConfig[] wildMultiplierConfigs;

    [Space]
    [SerializeField] private CardConfig[] singleBombConfigs;
    [SerializeField] private CardConfig[] doubleBombConfigs;
    [SerializeField] private CardConfig[] wildBombConfigs;

    [Space]
    [SerializeField] private CardConfig[] singleShieldConfigs;
    [SerializeField] private CardConfig[] doubleShieldConfigs;
    [SerializeField] private CardConfig[] wildShieldConfigs;

    [Space]
    [SerializeField] private CardConfig[] singlePointComboConfigs;
    [SerializeField] private CardConfig[] singleMultComboConfigs;
    [SerializeField] private CardConfig[] singleShieldComboConfigs;
    [SerializeField] private CardConfig[] singleWildComboConfigs;

    [Space]
    [SerializeField] private CardConfig[] powerCardConfigs;


    [Space]
    [SerializeField] private CardConfig[] tutorialCardConfigs;

    [Space]
    [SerializeField] private UICardCell gameDeck;
    [SerializeField] private TextMeshProUGUI gameDeckText;

    [Space]
    public GameObject CardBackPrefab;

    private int cardDataIndex = 0;
    private int nextCardInstanceId;

    private void DealPowerCardFromEditor()
    {
        GameManager.IN.StartCoroutine(GameManager.IN.DealPowerCardCo());
    }

    public void ResetCardInstanceIds()
    {
        this.nextCardInstanceId = 0;
    }

    public void GenerateAllCardDatasFromScriptableObjects()
    {
        GenerateCardDatas(this.singlePointConfigs);
        GenerateCardDatas(this.wildPointConfigs);
        GenerateCardDatas(this.doublePointConfigs);

        if (this.shouldIncludeTripleConfigs)
            GenerateCardDatas(this.triplePointConfigs);

        GenerateCardDatas(this.singleMultiplierConfigs);
        GenerateCardDatas(this.wildMultiplierConfigs);
        GenerateCardDatas(this.doubleMultiplierConfigs);

        if (this.shouldIncludeTripleConfigs)
            GenerateCardDatas(this.tripleMultiplierConfigs);

        GenerateCardDatas(this.singleBombConfigs);
        GenerateCardDatas(this.wildBombConfigs);
        GenerateCardDatas(this.doubleBombConfigs);

        GenerateCardDatas(this.singleShieldConfigs);
        GenerateCardDatas(this.doubleShieldConfigs);
        GenerateCardDatas(this.wildShieldConfigs);

        GenerateCardDatas(this.singlePointComboConfigs);
        GenerateCardDatas(this.singleMultComboConfigs);
        GenerateCardDatas(this.singleShieldComboConfigs);
        GenerateCardDatas(this.singleWildComboConfigs);

        GenerateShuffledPowerCardDatas(this.powerCardConfigs);
        CreateSinglePowerCardDatas();

        GenerateTutorialCardDatas(this.tutorialCardConfigs);

        this.DefaultDeck = new Deck
        {
            Name = "New Deck",
            ID = "DefaultDeckID",
            IsDefault = true,
            CardCounts = new()
        };

        for (int i = 0; i < this.DefaultCardCounts.Count; i++)
        {
            this.DefaultDeck.CardCounts.Add(1);
        }

        void GenerateCardDatas(CardConfig[] inCardConfig)
        {
            for (int i = 0; i < inCardConfig.Length; i++)
            {
                var config = inCardConfig[i];
                var cardData = CardConfig.CreateCardData(config, this.cardDataIndex);

                this.DefaultCardCounts.Add(cardData.Count);

                var cardDataCountOne = CardConfig.CreateCardData(config, this.cardDataIndex);
                cardDataCountOne.Count = 1;
                this.AllCardDatas.Add(cardDataCountOne);

                ++this.cardDataIndex;
            }
        }

        void CreateSinglePowerCardDatas()
        {
            var healConfig = this.powerCardConfigs[0];
            var freezeConfig = this.powerCardConfigs[1];
            var lightningConfig = this.powerCardConfigs[2];

            var healData = CardConfig.CreateCardData(healConfig, 0);
            this.SinglePowerCardDatas.Add(healData);

            var freezeData = CardConfig.CreateCardData(freezeConfig, 1);
            this.SinglePowerCardDatas.Add(freezeData);

            var lightningData = CardConfig.CreateCardData(lightningConfig, 2);
            this.SinglePowerCardDatas.Add(lightningData);
        }
    }

    private void GenerateShuffledPowerCardDatas(CardConfig[] inCardConfig)
    {
        this.PowerCardDatas.Clear();

        for (int i = 0; i < inCardConfig.Length; i++)
        {
            var config = inCardConfig[i];

            for (int j = 0; j < config.Count; j++)
            {
                var cardData = CardConfig.CreateCardData(config, this.PowerCardDatas.Count);
                this.PowerCardDatas.Add(cardData);
            }
        }

        this.PowerCardDatas.RandomizeList();
    }

    void GenerateTutorialCardDatas(CardConfig[] inCardConfig)
    {
        this.TutorialCardDatas.Clear();

        for (int i = 0; i < inCardConfig.Length; i++)
        {
            var config = inCardConfig[i];

            var cardData = CardConfig.CreateCardData(config, this.TutorialCardDatas.Count);
            this.TutorialCardDatas.Add(cardData);
        }
    }

    public void AddBatchOfCardsToGameDeck()
    {
        if (UITutorialPanel.IsTutorialMode) return;

        this.ShuffledCardDatas.RandomizeList();//TODO: think about this

        int numToSpawn = this.CardSpawnBatchSize - this.gameDeck.Cards.Count;

        for (int i = 0; i < numToSpawn; i++)
        {
            if (this.ShuffledCardDatas.Count > 0)
            {
                var cardData = this.ShuffledCardDatas[0];
                CardsManager.SpawnCard(cardData, this.gameDeck.transform, true);

                this.ShuffledCardDatas.RemoveAt(0);
            }
            else
            {
                Debug.Log("<color=red>ShuffledCardDatas Count = ZERO</color>");
                break;
            }
        }

        this.gameDeck.RefreshCellsList();
    }

    public void GenerateShuffledGameDeck()
    {
        this.ShuffledCardDatas.Clear();

        foreach (var cardData in this.ActiveDeckCardDatas)
        {
            for (int i = 0; i < cardData.Count; i++)
            {
                this.ShuffledCardDatas.Add(cardData);
            }
        }

        this.ShuffledCardDatas.RandomizeList();

        this.gameDeckText.text = PlayerData.Data.ActiveDeck.Name;
    }

    public void AddTutorialCardsToGameDeck()
    {
        for (int i = this.TutorialCardDatas.Count - 1; i >= 0; i--)
        {
            var cardData = this.TutorialCardDatas[i];
            CardsManager.SpawnCard(cardData, this.gameDeck.transform, true);
        }

        this.gameDeck.RefreshCellsList();

        this.gameDeckText.text = string.Empty;
    }

    public Card SpawnPowerCardOfType(ECardType inCardType = ECardType.None, string inCardId = null)
    {
        Card spawnedCard = null;
        switch (inCardType)
        {
            case ECardType.None:
                if (this.PowerCardDatas.Count > 0)
                {
                    var cardData = this.PowerCardDatas[0];
                    spawnedCard = CardsManager.SpawnCard(cardData, this.gameDeck.transform, true, inCardId: inCardId);

                    this.PowerCardDatas.RemoveAt(0);

                    if (this.PowerCardDatas.Count < 2)
                        GenerateShuffledPowerCardDatas(this.powerCardConfigs);
                }
                else
                    Debug.Log("<color=red>PowerCardDatas Count = ZERO</color>");
                break;

            case ECardType.Heal:
                spawnedCard = CardsManager.SpawnCard(this.SinglePowerCardDatas[0], this.gameDeck.transform, true,
                    inCardId: inCardId);
                break;

            case ECardType.Freeze:
                spawnedCard = CardsManager.SpawnCard(this.SinglePowerCardDatas[1], this.gameDeck.transform, true,
                    inCardId: inCardId);
                break;

            case ECardType.Lightning:
                spawnedCard = CardsManager.SpawnCard(this.SinglePowerCardDatas[2], this.gameDeck.transform, true,
                    inCardId: inCardId);
                break;

            default:
                Debug.Log($"<color=red>Cannot spawn Power Card of type {inCardType}</color>");
                break;
        }

        this.gameDeck.RefreshCellsList();

        return spawnedCard;
    }

    public static Card SpawnCard(CardData inCardData, Transform inParent, bool inIsFaceDown, bool inIsDraggable = true, bool inShouldSnapToCenter = false, string inCardId = null)
    {
        string cardId = CardsManager.IN.AllocateCardInstanceId(inCardId);
        var prefab = Resources.Load($"Card Prefabs/{inCardData.PrefabName}");

        var go = Instantiate(prefab, inParent) as GameObject;
        go.name = $"{inCardData.Name}";

        var card = go.GetComponent<Card>();

        card.Configure(inCardData, cardId);

        card.transform.SetParent(inParent);
        card.transform.localScale = Vector3.one;
        card.transform.rotation = Quaternion.identity;

        if (inIsFaceDown)
            CardsManager.IN.SetCardFaceDown(card);
        else
            card.CardFlip.SetAnimState(ECardFlipState.Front);

        if (inShouldSnapToCenter)
            card.transform.localPosition = Vector3.zero;

        card.Draggable.IsDraggable = inIsDraggable;

        CardsManager.IN.SpawnedCards.Add(card);

        return card;
    }

    private string AllocateCardInstanceId(string inCardId)
    {
        foreach (var card in this.SpawnedCards)
        {
            if (card == null || card.IsDestroyed)
                continue;
            if (!string.IsNullOrEmpty(inCardId) && card.Id == inCardId)
                throw new InvalidOperationException("Card instance id already exists: " + inCardId);
            this.nextCardInstanceId = CardInstanceData.ReserveSequence(this.nextCardInstanceId, card.Id);
        }

        if (!string.IsNullOrEmpty(inCardId))
        {
            this.nextCardInstanceId = CardInstanceData.ReserveSequence(this.nextCardInstanceId, inCardId);
            return inCardId;
        }

        string cardId = CardInstanceData.CreateId(this.nextCardInstanceId);
        ++this.nextCardInstanceId;
        return cardId;
    }

    public void SetCardFaceDown(Card inCard)
    {
        inCard.CardFlip.SetAnimState(UICardFlip.ECardFlipState.Back);
        if (inCard.CardBack != null)
            return;

        var backGo = LeanPool.Spawn(this.CardBackPrefab, inCard.CardBackParent);
        backGo.name = "Card Back";
        inCard.CardBack = backGo;
        backGo.transform.localScale = Vector3.one;
        backGo.transform.rotation = Quaternion.identity;
    }

    public void SetOnlineDeckCount(int inCount)
    {
        this.gameDeckText.text = Mathf.Max(0, inCount).ToString();
    }

    //call when loading a new deck in editor
    public void RebuildActiveDeck()
    {
        this.ActiveDeckCardDatas.Clear();

        for (int i = 0; i < this.AllCardDatas.Count; i++)
        {
            var count = PlayerData.Data.ActiveDeck.CardCounts[i];

            if (count > 0)
            {
                var cardData = this.AllCardDatas[i];
                cardData.Count = count;
                this.ActiveDeckCardDatas.Add(cardData);
            }
        }
    }

    public void RestoreCardDataPools(IEnumerable<CardData> inShuffledCardDatas,
        IEnumerable<CardData> inPowerCardDatas)
    {
        this.ShuffledCardDatas.Clear();
        this.ShuffledCardDatas.AddRange(inShuffledCardDatas);
        this.PowerCardDatas.Clear();
        this.PowerCardDatas.AddRange(inPowerCardDatas);
    }

    public void DestroyAllCards()
    {
        DestroyAllCards(true);
    }

    public void DestroyAllCards(bool inShouldRecycleData)
    {
        List<Card> cards = new(this.SpawnedCards);
        this.SpawnedCards.Clear();

        foreach (var card in cards)
        {
            if (card != null)
                card.DestroySelf(inShouldRecycleData);
        }
    }
}
