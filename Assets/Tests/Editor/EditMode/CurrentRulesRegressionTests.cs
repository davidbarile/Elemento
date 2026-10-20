using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Elemento.Domain;
using Elemento.Domain.Net;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class CurrentRulesRegressionTests
{
    private readonly List<GameObject> createdObjects = new List<GameObject>();
    private SettingsManager settings;
    private GameManager gameManager;

    [SetUp]
    public void SetUp()
    {
        UITutorialPanel.IsTutorialMode = false;
        BoardStateManager.IsDoingReplay = false;
        BoardStateManager.IsSettingState = true;
        GameManager.IsApplyingRemoteUpdate = false;
        UIDraggable.ResetInputState();

        PlayerData.Data = new PlayerData { LanguageIndex = 0 };

        this.settings = CreateComponent<SettingsManager>("Settings");
        this.settings.DiscardMode = SettingsManager.EDiscardMode.None;
        SettingsManager.IN = this.settings;

        this.gameManager = CreateComponent<GameManager>("Game Manager");
        this.gameManager.MovesRemaining = 1;
        GameManager.IN = this.gameManager;

        var audioManager = CreateComponent<AudioManager>("Audio Manager");
        audioManager.ErrorSoundClips = new AudioClip[4];
        AudioManager.IN = audioManager;

        var localization = CreateComponent<LocalizationManager>("Localization");
        localization.TotalPointsTitle = new[] { "{0}/{1} Total Points" };
        localization.AllZonesDestroyedTitle = new[] { "All zones destroyed" };
        localization.You = new[] { "You" };
        localization.Opponent = new[] { "Opponent" };
        localization.Reject_CardOnOpponentBoard = new[] { string.Empty };
        localization.Reject_AlreadyFrozenZone = new[] { string.Empty };
        localization.Reject_LightningOnOwnBoard = new[] { string.Empty };
        LocalizationManager.IN = localization;
        LOC.IN = localization;
    }

    [TearDown]
    public void TearDown()
    {
        UIWinLosePanel.IN = null;
        AudioManager.IN = null;
        BoardStateManager.IN = null;
        CardsManager.IN = null;
        GameManager.IN = null;
        SettingsManager.IN = null;
        LocalizationManager.IN = null;
        LOC.IN = null;
        PlayerData.Data = null;
        UITutorialPanel.IsTutorialMode = false;
        BoardStateManager.IsDoingReplay = false;
        BoardStateManager.IsSettingState = false;
        GameManager.IsDealingHand = false;
        GameManager.IsDoingActivateSequence = false;
        GameManager.IsDoingReplay = false;
        GameManager.IsApplyingRemoteUpdate = false;
        UIDraggable.ResetInputState();

        for (var i = this.createdObjects.Count - 1; i >= 0; i--)
        {
            if (this.createdObjects[i] != null)
                UnityEngine.Object.DestroyImmediate(this.createdObjects[i]);
        }

        this.createdObjects.Clear();
    }

    [Test]
    public void DestroyAllCards_RemovesDestroyedEntriesWithoutLooping()
    {
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        cardsManager.SpawnedCards.Add(null);

        cardsManager.DestroyAllCards();

        Assert.That(cardsManager.SpawnedCards, Is.Empty);
    }

    [TestCase(SettingsManager.EPlayMode.AgainstAI)]
    [TestCase(SettingsManager.EPlayMode.PassAndPlay)]
    public void LocalPowerCardAllocation_SkipsExistingCardIds(SettingsManager.EPlayMode inPlayMode)
    {
        this.settings.PlayMode = inPlayMode;
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        var existingCard = CreateComponent<Card>("Existing Card");
        SetField(existingCard, "<Id>k__BackingField", "card-00000009");
        cardsManager.SpawnedCards.Add(existingCard);

        Assert.That(AllocateCardInstanceId(cardsManager, null), Is.EqualTo("card-00000010"));
    }

    [Test]
    public void OnlinePowerCardAllocation_ReservesAuthoritativeCardId()
    {
        this.settings.PlayMode = SettingsManager.EPlayMode.Multiplayer;
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        var existingCard = CreateComponent<Card>("Existing Card");
        SetField(existingCard, "<Id>k__BackingField", "card-00000009");
        cardsManager.SpawnedCards.Add(existingCard);

        Assert.That(AllocateCardInstanceId(cardsManager, "card-00000010"), Is.EqualTo("card-00000010"));
        Assert.That(AllocateCardInstanceId(cardsManager, null), Is.EqualTo("card-00000011"));
    }

    [Test]
    public void OnlineIceReplay_UsesOverrideWithoutLocalReplayBoardState()
    {
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        cardsManager.PowerCardOverride = Card.ECardType.Freeze;
        BoardStateManager.IN = null;
        GameManager.IsDoingReplay = true;

        var method = typeof(GameManager).GetMethod("GetPowerCardTypeForDeal",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        Assert.That(method.Invoke(null, null), Is.EqualTo(Card.ECardType.Freeze));
    }

    [TestCase(UICardCell.ECellType.Hand, true)]
    [TestCase(UICardCell.ECellType.ElementZone, false)]
    public void SetMovingCardFromHand_ReparentsOnlyHandCards(
        UICardCell.ECellType inCellType, bool inShouldReparent)
    {
        var overlay = CreateComponent<UIDragOverlay>("Drag Overlay");
        overlay.transform.localScale = new Vector3(2, 2, 2);

        var sourceCell = CreateComponent<UICardCell>("Source Cell");
        sourceCell.CellType = inCellType;
        sourceCell.transform.localScale = new Vector3(.5f, .5f, .5f);

        var card = CreateComponent<Card>("Card", sourceCell.transform);
        card.transform.position = new Vector3(10, 20, 30);
        Vector3 worldPosition = card.transform.position;
        Vector3 worldScale = card.transform.lossyScale;

        overlay.SetMovingCardFromHand(card, sourceCell);

        Transform expectedParent = inShouldReparent ? overlay.transform : sourceCell.transform;
        Assert.That(card.transform.parent, Is.EqualTo(expectedParent));
        Assert.That(card.transform.position, Is.EqualTo(worldPosition));
        Assert.That(card.transform.lossyScale, Is.EqualTo(worldScale));
    }

    [Test]
    public void GetCardLandingPosition_MatchesGridLayoutAfterParenting()
    {
        var cell = CreateUiComponent<UICardCell>("Cell", null);
        cell.gameObject.SetActive(true);
        cell.CellType = UICardCell.ECellType.ElementZone;
        cell.MaxVisibleCardsInDeck = 2;
        cell.transform.localScale = new Vector3(1.25f, 1.25f, 1.25f);

        var grid = CreateUiComponent<GridLayoutGroup>("Cards Grid", cell.transform);
        grid.gameObject.SetActive(true);
        grid.cellSize = new Vector2(450, 600);
        grid.padding = new RectOffset(40, 0, 40, 0);
        grid.spacing = new Vector2(0, -570);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 1;
        var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var gridRect = (RectTransform)grid.transform;
        gridRect.pivot = new Vector2(.5f, 1);
        gridRect.sizeDelta = new Vector2(530, 600);
        cell.CardHolder = gridRect;

        var firstCard = CreateUiComponent<Card>("First Card", gridRect);
        var secondCard = CreateUiComponent<Card>("Second Card", gridRect);
        firstCard.gameObject.SetActive(true);
        secondCard.gameObject.SetActive(true);
        ((RectTransform)firstCard.transform).sizeDelta = grid.cellSize;
        ((RectTransform)secondCard.transform).sizeDelta = grid.cellSize;
        cell.Cards.Add(firstCard);
        cell.Cards.Add(secondCard);

        var dragCanvas = CreateUiComponent<Image>("Drag Canvas", cell.transform.parent).rectTransform;
        dragCanvas.gameObject.SetActive(true);
        dragCanvas.localScale = new Vector3(2, 2, 2);
        var movingCard = CreateUiComponent<Card>("Moving Card", dragCanvas);
        movingCard.gameObject.SetActive(true);
        var movingCardRect = (RectTransform)movingCard.transform;
        movingCardRect.pivot = new Vector2(.5f, .5f);
        movingCardRect.sizeDelta = grid.cellSize;

        Vector3 landingPosition = cell.GetCardLandingPosition(movingCard);
        Vector3 landingScale = cell.GetCardLandingLocalScale(dragCanvas);

        Assert.That(cell.Cards, Has.Count.EqualTo(2));
        Assert.That(firstCard.gameObject.activeSelf, Is.True);
        movingCard.transform.localScale = landingScale;
        Assert.That(Vector3.Distance(movingCard.transform.lossyScale, gridRect.lossyScale),
            Is.LessThan(.01f));

        cell.Cards.Add(movingCard);
        cell.SetParentCell(movingCard);
        LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);

        Assert.That(Vector3.Distance(movingCard.transform.position, landingPosition),
            Is.LessThan(.01f));
    }

    [Test]
    public void RetireForDestruction_DetachesViewBeforeSameFrameHierarchyRefresh()
    {
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        var deckObject = CreateGameObject("Deck");
        var deck = deckObject.AddComponent<UICardCell>();
        deck.CardHolder = deckObject.transform;
        var card = CreateComponent<Card>("Card", deck.CardHolder);
        card.Data = new CardData();
        cardsManager.SpawnedCards.Add(card);
        deck.RefreshCellsList();

        var retire = typeof(Card).GetMethod("RetireForDestruction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(retire, Is.Not.Null);
        Assert.That(retire.Invoke(card, new object[] { false }), Is.EqualTo(true));
        deck.RefreshCellsList();

        Assert.That(deck.Cards, Is.Empty);
        Assert.That(card.IsDestroyed, Is.True);
        Assert.That(card.transform.parent, Is.Null);
        Assert.That(card.gameObject.activeSelf, Is.False);
        Assert.That(cardsManager.SpawnedCards, Is.Empty);
        Assert.That(cardsManager.ShuffledCardDatas, Is.Empty);
        Assert.That(cardsManager.PowerCardDatas, Is.Empty);
    }

    [Test]
    public void CardRestore_MultipleImmediateCyclesDoNotAccumulateViewsOrPools()
    {
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        var deckObject = CreateGameObject("Deck");
        var deck = deckObject.AddComponent<UICardCell>();
        deck.CardHolder = deckObject.transform;
        var shuffledCardData = new CardData { Name = "Shuffled" };
        var powerCardData = new CardData { Name = "Power", IsPowerCard = true };

        for (int i = 0; i < 3; ++i)
        {
            cardsManager.RestoreCardDataPools(new[] { shuffledCardData }, new[] { powerCardData });
            var card = CreateComponent<Card>("Restored Card " + i, deck.CardHolder);
            card.Data = shuffledCardData;
            SetField(card, "<Id>k__BackingField", "restored-card");
            cardsManager.SpawnedCards.Add(card);
            deck.RefreshCellsList();

            Assert.That(deck.Cards, Has.Count.EqualTo(1));
            Assert.That(cardsManager.SpawnedCards, Has.Count.EqualTo(1));
            Assert.That(cardsManager.ShuffledCardDatas, Has.Count.EqualTo(1));
            Assert.That(cardsManager.PowerCardDatas, Has.Count.EqualTo(1));

            cardsManager.DestroyAllCards(false);
            deck.RefreshCellsList();

            Assert.That(deck.Cards, Is.Empty);
            Assert.That(cardsManager.SpawnedCards, Is.Empty);
            Assert.That(cardsManager.ShuffledCardDatas, Has.Count.EqualTo(1));
            Assert.That(cardsManager.PowerCardDatas, Has.Count.EqualTo(1));
        }
    }

    [TestCase(Card.ECardType.Heal)]
    [TestCase(Card.ECardType.Freeze)]
    [TestCase(Card.ECardType.Lightning)]
    public void HandleDrop_RejectsPowerCardToHandWithoutRequiringElementZone(Card.ECardType powerCardType)
    {
        this.settings.FrozenZoneHealth = 3;

        var handObject = CreateGameObject("Hand");
        var hand = handObject.AddComponent<UICardCell>();
        hand.CellType = UICardCell.ECellType.Hand;
        hand.CardHolder = handObject.transform;
        hand.PlayerType = UIPlayerBoard.EPlayerType.PlayerA;

        var targetZone = CreateZone("Target Zone", CardData.EElementType.Air);
        targetZone.CardCell.PlayerType = powerCardType == Card.ECardType.Heal
            ? UIPlayerBoard.EPlayerType.PlayerB
            : UIPlayerBoard.EPlayerType.PlayerA;

        var card = CreateComponent<Card>(powerCardType + " Power Card", hand.transform);
        card.CardType = powerCardType;
        card.Data = new CardData { IsPowerCard = true };
        switch (powerCardType)
        {
            case Card.ECardType.Heal:
                card.Data.WildZoneHeal = 1;
                break;
            case Card.ECardType.Freeze:
                card.Data.WildFreeze = this.settings.FrozenZoneHealth;
                targetZone.FrozenHealth = this.settings.FrozenZoneHealth;
                break;
            case Card.ECardType.Lightning:
                card.Data.WildLightning = 1;
                break;
        }
        hand.Cards.Add(card);

        DG.Tweening.DOTween.Init();
        var previousUseSafeMode = DG.Tweening.DOTween.useSafeMode;
        var completedTweenCount = 0;
        try
        {
            targetZone.CardCell.HandleDrop(hand);
            DG.Tweening.DOTween.useSafeMode = false;
            Assert.DoesNotThrow(() => completedTweenCount = DG.Tweening.DOTween.Complete(card.transform, true));
        }
        finally
        {
            DG.Tweening.DOTween.useSafeMode = previousUseSafeMode;
        }

        Assert.That(completedTweenCount, Is.EqualTo(1));
        Assert.That(card.transform.parent, Is.SameAs(hand.CardHolder));
        Assert.That(hand.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void ResetInputState_ClearsDragLocksWithoutClearingRemoteUpdateLock()
    {
        GameManager.IsDealingHand = false;
        GameManager.IsDoingActivateSequence = false;
        GameManager.IsDoingReplay = false;
        GameManager.IsApplyingRemoteUpdate = true;
        UIDraggable.ShouldBlockInput = true;
        UIDraggable.IsDragPointerDown = true;

        UIDraggable.ResetInputState();

        Assert.That(UIDraggable.ShouldBlockInput, Is.False);
        Assert.That(UIDraggable.IsDragPointerDown, Is.False);
        Assert.That(GameManager.ShouldBlockInput, Is.True);

        GameManager.IsApplyingRemoteUpdate = false;
        Assert.That(GameManager.ShouldBlockInput, Is.False);
    }

    [Test]
    public void ReplayFailure_CatchesNestedExceptionAndReleasesEveryInputLock()
    {
        var cardsManager = CreateComponent<CardsManager>("Cards Manager");
        CardsManager.IN = cardsManager;
        cardsManager.PowerCardOverride = Card.ECardType.Freeze;
        GameManager.IsDealingHand = true;
        GameManager.IsDoingActivateSequence = true;
        GameManager.IsDoingReplay = true;
        GameManager.IsApplyingRemoteUpdate = true;
        BoardStateManager.IsDoingReplay = true;
        BoardStateManager.IsSettingState = true;
        Move.ShouldRecordMoves = false;
        UIDraggable.ShouldBlockInput = true;
        UIDraggable.IsDragPointerDown = true;
        Exception replayException = null;

        var runner = typeof(GameManager).GetMethod("RunReplayStepCo",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(runner, Is.Not.Null);
        var replay = (IEnumerator)runner.Invoke(null, new object[]
        {
            ThrowingReplayStep(),
            new Action<Exception>(inException => replayException = inException)
        });
        Drain(replay);
        InvokeReleaseReplayInputLocks();

        Assert.That(replayException, Is.TypeOf<InvalidOperationException>());
        Assert.That(GameManager.ShouldBlockInput, Is.False);
        Assert.That(UIDraggable.ShouldBlockInput, Is.False);
        Assert.That(UIDraggable.IsDragPointerDown, Is.False);
        Assert.That(BoardStateManager.IsDoingReplay, Is.False);
        Assert.That(BoardStateManager.IsSettingState, Is.False);
        Assert.That(Move.ShouldRecordMoves, Is.True);
        Assert.That(cardsManager.PowerCardOverride, Is.EqualTo(Card.ECardType.None));
    }

    [Test]
    public void ReplaySnapshotRestore_PreservesExplicitZoneHealthState()
    {
        var zone = CreateZone("Replay Zone", CardData.EElementType.Fire);

        zone.RestoreHealthState(7, 3, true);

        Assert.That(zone.MaxHealth, Is.EqualTo(7));
        Assert.That(zone.Health, Is.EqualTo(3));
        Assert.That(zone.IsDestroyed, Is.True);

        zone.RestoreHealthState(7, 0, false);

        Assert.That(zone.MaxHealth, Is.EqualTo(7));
        Assert.That(zone.Health, Is.Zero);
        Assert.That(zone.IsDestroyed, Is.False);
    }

    [Test]
    public void ReplayButtonPlan_AfterAiTurnStartsAndPlaysAiMovesInOrder()
    {
        var aiFirstMoveState = CreateReplayMoveState(UIPlayerBoard.EPlayerType.PlayerA,
            UIPlayerBoard.EPlayerType.PlayerB, 10, "ai-first");
        var aiSecondMoveState = CreateReplayMoveState(UIPlayerBoard.EPlayerType.PlayerA,
            UIPlayerBoard.EPlayerType.PlayerB, 11, "ai-second");
        var liveState = new BoardState { ActivePlayer = UIPlayerBoard.EPlayerType.PlayerA };
        List<BoardTurn> turns = new()
        {
            new BoardTurn
            {
                BoardStates = new()
                {
                    new BoardState { ActivePlayer = UIPlayerBoard.EPlayerType.PlayerB }
                }
            },
            new BoardTurn
            {
                BoardStates = new()
                {
                    aiFirstMoveState,
                    aiSecondMoveState,
                    liveState
                }
            }
        };

        var method = typeof(GameManager).GetMethod("TryCreateReplayPlan",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        object[] arguments = { turns, 2, 0, null };

        bool shouldStart = (bool)method.Invoke(null, arguments);
        object replayPlan = arguments[3];
        var opponentStates = GetField<List<BoardState>>(replayPlan, "OpponentStates");
        var currentPlayerStates = GetField<List<BoardState>>(replayPlan, "CurrentPlayerStates");

        Assert.That(shouldStart, Is.True);
        Assert.That(GetField<int>(replayPlan, "ReplaySeat"), Is.Zero);
        Assert.That(GetField<bool>(replayPlan, "ShouldReplayOpponent"), Is.True);
        Assert.That(opponentStates, Is.EqualTo(new[] { aiFirstMoveState, aiSecondMoveState }));
        Assert.That(opponentStates.ConvertAll(inState => inState.Move.Data.CardId),
            Is.EqualTo(new[] { "ai-first", "ai-second" }));
        Assert.That(currentPlayerStates, Is.Empty);
        Assert.That(turns[1].BoardStates[2], Is.SameAs(liveState));
    }

    [Test]
    public void ReplayButtonPlan_PassAndPlayUsesMovesOwnedByCurrentSeat()
    {
        var playerAMoveState = CreateReplayMoveState(UIPlayerBoard.EPlayerType.PlayerA,
            UIPlayerBoard.EPlayerType.PlayerA, 20, "player-a");
        var playerBMoveState = CreateReplayMoveState(UIPlayerBoard.EPlayerType.PlayerA,
            UIPlayerBoard.EPlayerType.PlayerB, 21, "player-b");
        List<BoardTurn> turns = new()
        {
            new BoardTurn
            {
                BoardStates = new() { playerAMoveState, playerBMoveState }
            }
        };
        var method = typeof(GameManager).GetMethod("TryCreateReplayPlan",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        object[] arguments = { turns, 1, 1, null };

        bool shouldStart = (bool)method.Invoke(null, arguments);
        object replayPlan = arguments[3];

        Assert.That(shouldStart, Is.True);
        Assert.That(GetField<int>(replayPlan, "ReplaySeat"), Is.EqualTo(1));
        Assert.That(GetField<bool>(replayPlan, "ShouldReplayOpponent"), Is.False);
        Assert.That(GetField<List<BoardState>>(replayPlan, "CurrentPlayerStates"),
            Is.EqualTo(new[] { playerBMoveState }));
    }

    [TestCase("Main.unity")]
    [TestCase("Main 1.unity")]
    public void ReplayButtonSceneBinding_TargetsExistingPublicParameterlessMethod(string inSceneName)
    {
        string scenePath = Path.Combine(Application.dataPath, "Scenes", inSceneName);
        string[] documents = Regex.Split(File.ReadAllText(scenePath), @"(?m)(?=^--- !u!)");
        string gameObjectDocument = Array.Find(documents,
            inDocument => Regex.IsMatch(inDocument, @"(?m)^  m_Name: Replay Button\r?$"));
        Assert.That(gameObjectDocument, Is.Not.Null);

        string gameObjectId = Regex.Match(gameObjectDocument,
            @"(?m)^--- !u!1 &(?<id>\d+)").Groups["id"].Value;
        string buttonDocument = Array.Find(documents, inDocument =>
            inDocument.Contains($"m_GameObject: {{fileID: {gameObjectId}}}")
            && inDocument.Contains("m_OnClick:"));
        Assert.That(buttonDocument, Is.Not.Null);

        Match callback = Regex.Match(buttonDocument,
            @"(?ms)m_Target: \{fileID: (?<target>\d+)\}.*?m_TargetAssemblyTypeName: (?<type>[^,\r\n]+),.*?m_MethodName: (?<method>[^\r\n]+).*?m_Mode: (?<mode>\d+)");
        Assert.That(callback.Success, Is.True);
        Assert.That(callback.Groups["mode"].Value, Is.EqualTo("1"));

        string targetId = callback.Groups["target"].Value;
        string targetDocument = Array.Find(documents, inDocument =>
            Regex.IsMatch(inDocument, $@"(?m)^--- !u!114 &{targetId}\r?$"));
        Assert.That(targetDocument, Is.Not.Null);
        string scriptGuid = Regex.Match(targetDocument,
            @"m_Script: \{fileID: 11500000, guid: (?<guid>[a-f0-9]+),").Groups["guid"].Value;
        string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
        Type targetType = script?.GetClass();
        Assert.That(targetType, Is.Not.Null);
        Assert.That(targetType.Name, Is.EqualTo(callback.Groups["type"].Value));

        MethodInfo callbackMethod = targetType.GetMethod(callback.Groups["method"].Value,
            BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        Assert.That(callbackMethod, Is.Not.Null);
        Assert.That(callbackMethod.ReturnType, Is.EqualTo(typeof(void)));
    }

    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    public void RefreshPlayModeAvailability_MatchesGameProgress(bool isGameInProgress,
        bool expectedInteractable, bool expectedNoteVisible)
    {
        var panel = CreateComponent<UISettingsPanel>("Settings Panel");
        var dropdown = CreateUiComponent<TMP_Dropdown>("Play Mode Dropdown", panel.transform);
        var note = CreateComponent<LocalizationItem>("Play Mode Locked Note", panel.transform);
        SetField(panel, "playModeDropdown", dropdown);
        SetField(panel, "playModeLockedNote", note);

        panel.RefreshPlayModeAvailability(isGameInProgress);

        Assert.That(dropdown.interactable, Is.EqualTo(expectedInteractable));
        Assert.That(note.gameObject.activeSelf, Is.EqualTo(expectedNoteVisible));
    }

    [Test]
    public void SetAllowUndoPreference_UpdatesSavedLocalRulesDuringMatch()
    {
        this.settings.AllowUndo = true;
        this.settings.ApplyMatchRules(new RulesConfigData
        {
            AllowUndo = false
        });

        this.settings.SetAllowUndoPreference(false);
        this.settings.ClearMatchRules();

        Assert.That(this.settings.AllowUndo, Is.False);
    }

    [Test]
    public void CalculatePoints_SumsPointsBeforeApplyingEachMultiplier()
    {
        this.settings.ElementZonePointsRequiredToActivate = 100;
        var zone = CreateZone("Air Zone", CardData.EElementType.Air);

        AddCard(zone, points: 2, multiplier: 0);
        AddCard(zone, points: 3, multiplier: 0);
        AddCard(zone, points: 0, multiplier: 2);
        AddCard(zone, points: 0, multiplier: 3);

        zone.CalculatePoints();

        Assert.That(zone.Points, Is.EqualTo(30));
        Assert.That(zone.FaceUpPoints, Is.EqualTo(30));
    }

    [TestCase(9, false)]
    [TestCase(10, true)]
    public void UnfreezingZone_UsesInclusiveActivationThreshold(int points, bool expectedActivation)
    {
        this.settings.ElementZonePointsRequiredToActivate = 10;
        var zone = CreateZone("Activation Zone", CardData.EElementType.Air);
        zone.Points = points;
        zone.FrozenHealth = -1;

        zone.SetFrozenHealth(0, false);

        Assert.That(zone.IsActivated, Is.EqualTo(expectedActivation));
    }

    [Test]
    public void SetFrozenHealth_AfterOnlineRebuildKeepsOverlayAbovePile()
    {
        this.settings.FrozenZoneHealth = 5;
        var zone = CreateZone("Rebuilt Zone", CardData.EElementType.Air);
        var frozenOverlay = GetField<GameObject>(zone, "frozenOverlay");
        CreateGameObject("Rebuilt Pile Card", zone.CardCell.CardHolder);

        zone.SetFrozenHealth(this.settings.FrozenZoneHealth, false);

        Assert.That(zone.FrozenHealth, Is.EqualTo(this.settings.FrozenZoneHealth));
        Assert.That(frozenOverlay.activeSelf, Is.True);
        Assert.That(frozenOverlay.transform.GetSiblingIndex(), Is.EqualTo(zone.CardCell.CardHolder.childCount - 1));
    }

    [Test]
    public void SwapPlayerState_PreservesFrozenHealthAndOverlay()
    {
        this.settings.FrozenZoneHealth = 5;
        var first = CreateZone("First Zone", CardData.EElementType.Air);
        var second = CreateZone("Second Zone", CardData.EElementType.Air);
        var firstOverlay = GetField<GameObject>(first, "frozenOverlay");
        var secondOverlay = GetField<GameObject>(second, "frozenOverlay");
        first.SetFrozenHealth(this.settings.FrozenZoneHealth, false);

        first.SwapPlayerState(second);

        Assert.That(first.FrozenHealth, Is.Zero);
        Assert.That(firstOverlay.activeSelf, Is.False);
        Assert.That(second.FrozenHealth, Is.EqualTo(this.settings.FrozenZoneHealth));
        Assert.That(secondOverlay.activeSelf, Is.True);
        Assert.That(secondOverlay.transform.GetSiblingIndex(), Is.EqualTo(second.CardCell.CardHolder.childCount - 1));
    }

    [TestCase(49, false)]
    [TestCase(50, true)]
    public void CheckWinLoseState_UsesInclusivePointsTarget(int totalPoints, bool expectedWin)
    {
        this.settings.TotalPointsRequiredToWin = 50;
        var player = CreateBoard("Player", new[] { 10, 10, 10, totalPoints - 30 });
        var opponent = CreateBoard("Opponent", new[] { 0, 0, 0, 0 });
        player.TotalPoints = totalPoints;
        this.gameManager.PlayerBoards = new[] { player, opponent };
        SetField(this.gameManager, "<IsGameInProgress>k__BackingField", true);
        var panel = CreateWinLosePanel(4);

        this.gameManager.CheckWinLoseState(player);

        Assert.That(panel.IsShowing, Is.EqualTo(expectedWin));
        Assert.That(this.gameManager.IsGameInProgress, Is.EqualTo(!expectedWin));
        if (expectedWin)
        {
            Assert.That(GetField<GameManager.EWinLoseState>(panel, "winLoseState"),
                Is.EqualTo(GameManager.EWinLoseState.Won));
        }
    }

    [Test]
    public void CheckWinLoseState_AllOpponentZonesDestroyedWins()
    {
        var player = CreateBoard("Player", new[] { 0, 0, 0, 0 });
        var opponent = CreateBoard("Opponent", new[] { 0, 0, 0, 0 }, true);
        this.gameManager.PlayerBoards = new[] { player, opponent };
        var panel = CreateWinLosePanel(4);

        this.gameManager.CheckWinLoseState(opponent, true);

        Assert.That(panel.IsShowing, Is.True);
        Assert.That(GetField<GameManager.EWinLoseState>(panel, "winLoseState"),
            Is.EqualTo(GameManager.EWinLoseState.Won));
    }

    [Test]
    public void OnlineWrapup_UsesLocalSeatForYouStats()
    {
        var opponent = CreateBoard("Opponent", new[] { 4, 0, 0, 0 });
        var player = CreateBoard("Player", new[] { 9, 0, 0, 0 });
        AddCard(opponent.ElementZones[0], 4, 0);
        AddCard(player.ElementZones[0], 9, 0);
        this.gameManager.PlayerBoards = new[] { opponent, player };
        SetField(this.gameManager, "gameSession", new OnlineSession(1));
        var panel = CreateWinLosePanel(4);

        InvokeShowWinLosePanel(false, "Defeat");

        var items = GetField<UIWrapupItem[]>(panel, "wrapupItems");
        Assert.That(items[0].PlayerText.text, Is.EqualTo("You"));
        Assert.That(items[0].PointsDisplays[0].Text.text, Is.EqualTo("9"));
        Assert.That(items[1].PlayerText.text, Is.EqualTo("Opponent"));
        Assert.That(items[1].PointsDisplays[0].Text.text, Is.EqualTo("4"));
    }

    private UIElementZone CreateZone(string name, CardData.EElementType elementType, Transform parent = null)
    {
        var zoneObject = CreateGameObject(name, parent);
        var cell = zoneObject.AddComponent<UICardCell>();
        var zone = zoneObject.AddComponent<UIElementZone>();

        cell.CellType = UICardCell.ECellType.ElementZone;
        cell.ElementType = elementType;
        cell.CardHolder = zoneObject.transform;
        cell.ElementZone = zone;
        zone.CardCell = cell;
        zone.CardPlaceAnimator = CreateComponent<UICardPlaceAnim>(name + " Place Animator", zoneObject.transform);

        SetField(zone, "pointsCount", CreatePoints(name + " Points", zoneObject.transform));
        SetField(zone, "zoneFrozenHealth", CreatePoints(name + " Frozen Health", zoneObject.transform));
        SetField(zone, "zoneHealth", CreatePoints(name + " Health", zoneObject.transform));
        SetField(zone, "pointsCountDisplay", CreateGameObject(name + " Points Display", zoneObject.transform));
        SetField(zone, "zoneHealthDisplay", CreateGameObject(name + " Health Display", zoneObject.transform));
        SetField(zone, "zoneDestroyedOverlay", CreateGameObject(name + " Destroyed Overlay", zoneObject.transform));
        SetField(zone, "activeDisplays", Array.Empty<GameObject>());
        SetField(zone, "frozenOverlay", CreateGameObject(name + " Frozen Overlay", zoneObject.transform));

        return zone;
    }

    private void AddCard(UIElementZone zone, int points, int multiplier)
    {
        var card = CreateComponent<Card>("Card", zone.transform);
        var flip = card.gameObject.AddComponent<UICardFlip>();
        SetField(flip, "animState", UICardFlip.ECardFlipState.Front);

        var elementIndex = (int)zone.CardCell.ElementType - 1;
        var data = new CardData();
        data.Points[elementIndex] = points;
        data.Multipliers[elementIndex] = multiplier;

        card.Data = data;
        card.CardFlip = flip;
        zone.CardCell.Cards.Add(card);
    }

    private UIPlayerBoard CreateBoard(string name, int[] zonePoints, bool zonesDestroyed = false)
    {
        var board = AddPlayerBoardIgnoringEditorCallbacks(CreateGameObject(name));
        var zones = new UIElementZone[zonePoints.Length];

        for (var i = 0; i < zonePoints.Length; i++)
        {
            zones[i] = CreateZone(name + " Zone " + i, (CardData.EElementType)(i + 1), board.transform);
            zones[i].Points = zonePoints[i];
            zones[i].FaceUpPoints = zonePoints[i];
            SetField(zones[i], "<IsDestroyed>k__BackingField", zonesDestroyed);
        }

        board.ElementZones = zones;
        SetField(board, "totalPointsDisplay", CreateTotalPointsDisplay(name + " Total", board.transform, zonePoints.Length));
        return board;
    }

    // In the editor, AddComponent<UIPlayerBoard> synchronously invokes Reset() and
    // OnValidate(). Both dereference serialized refs (Hand, ElementZones,
    // AiOutputBubble) that cannot be assigned until the component exists, so they
    // throw NullReferenceException. The component is still added and the tests set
    // up every field they need afterwards. Ignore failing log messages only for this
    // single call so any other unexpected error still fails the test.
    private static UIPlayerBoard AddPlayerBoardIgnoringEditorCallbacks(GameObject gameObject)
    {
        var previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            return gameObject.AddComponent<UIPlayerBoard>();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previous;
        }
    }

    private UIWinLosePanel CreateWinLosePanel(int zoneCount)
    {
        var panel = CreateComponent<UIWinLosePanel>("Win Lose Panel");
        SetField(panel, "winTitle", CreateGameObject("Win Title", panel.transform));
        SetField(panel, "loseTitle", CreateGameObject("Lose Title", panel.transform));
        SetField(panel, "tieTitle", CreateGameObject("Tie Title", panel.transform));
        SetField(panel, "messageText", CreateText("Message", panel.transform));
        SetField(panel, "cancelButton", CreateUiComponent<Button>("Cancel", panel.transform));
        SetField(panel, "confirmButton", CreateUiComponent<Button>("Confirm", panel.transform));
        SetField(panel, "dismissButton", CreateUiComponent<Button>("Dismiss", panel.transform));
        SetField(panel, "showAnim", panel.gameObject.AddComponent<Animation>());

        var items = new UIWrapupItem[2];
        for (var i = 0; i < items.Length; i++)
            items[i] = CreateWrapupItem("Wrapup " + i, panel.transform, zoneCount);

        SetField(panel, "wrapupItems", items);
        UIWinLosePanel.IN = panel;
        return panel;
    }

    private UIWrapupItem CreateWrapupItem(string name, Transform parent, int zoneCount)
    {
        var item = CreateComponent<UIWrapupItem>(name, parent);
        item.PlayerText = CreateText(name + " Player", item.transform);
        item.TotalPointsDisplay = CreateTotalPointsDisplay(name + " Total", item.transform, zoneCount);
        item.PointsDisplays = new UIPoints[zoneCount];
        var canvasGroups = new CanvasGroup[zoneCount];
        var destroyedDisplays = new GameObject[zoneCount];

        for (var i = 0; i < zoneCount; i++)
        {
            item.PointsDisplays[i] = CreatePoints(name + " Points " + i, item.transform);
            canvasGroups[i] = CreateComponent<CanvasGroup>(name + " Canvas Group " + i, item.transform);
            destroyedDisplays[i] = CreateGameObject(name + " Destroyed " + i, item.transform);
        }

        SetField(item, "pointsCanvasGroups", canvasGroups);
        SetField(item, "zoneDestroyedDisplays", destroyedDisplays);
        return item;
    }

    private UITotalPointsDisplay CreateTotalPointsDisplay(string name, Transform parent, int zoneCount)
    {
        var display = CreateComponent<UITotalPointsDisplay>(name, parent);
        SetField(display, "totalPoints", CreatePoints(name + " Value", display.transform));
        SetField(display, "tooltipText", CreateText(name + " Tooltip", display.transform));

        var icons = new Image[zoneCount];
        for (var i = 0; i < zoneCount; i++)
            icons[i] = CreateUiComponent<Image>(name + " Icon " + i, display.transform);

        SetField(display, "elementIcons", icons);
        return display;
    }

    private UIPoints CreatePoints(string name, Transform parent)
    {
        var points = CreateComponent<UIPoints>(name, parent);
        SetField(points, "text", CreateText(name + " Text", points.transform));
        SetField(points, "fills", Array.Empty<Image>());
        return points;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent)
    {
        return CreateUiComponent<TextMeshProUGUI>(name, parent);
    }

    private T CreateUiComponent<T>(string name, Transform parent) where T : Component
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.SetActive(false);
        gameObject.transform.SetParent(parent, false);
        this.createdObjects.Add(gameObject);
        return gameObject.AddComponent<T>();
    }

    private T CreateComponent<T>(string name, Transform parent = null) where T : Component
    {
        var gameObject = CreateGameObject(name, parent);
        return gameObject.AddComponent<T>();
    }

    private GameObject CreateGameObject(string name, Transform parent = null)
    {
        var gameObject = new GameObject(name);
        gameObject.SetActive(false);
        if (parent != null)
            gameObject.transform.SetParent(parent, false);

        this.createdObjects.Add(gameObject);
        return gameObject;
    }

    private static string AllocateCardInstanceId(CardsManager inCardsManager, string inCardId)
    {
        var method = typeof(CardsManager).GetMethod("AllocateCardInstanceId",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (string)method.Invoke(inCardsManager, new object[] { inCardId });
    }

    private static IEnumerator ThrowingReplayStep()
    {
        yield return null;
        throw new InvalidOperationException("Ice replay failed.");
    }

    private static void Drain(IEnumerator inRoutine)
    {
        while (inRoutine.MoveNext())
        {
            if (inRoutine.Current is IEnumerator nestedRoutine)
                Drain(nestedRoutine);
        }
    }

    private static void InvokeReleaseReplayInputLocks()
    {
        var method = typeof(GameManager).GetMethod("ReleaseReplayInputLocks",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(null, null);
    }

    private void InvokeShowWinLosePanel(bool inPlayerWon, string inMessage)
    {
        var method = typeof(GameManager).GetMethod("ShowWinLosePanel",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(this.gameManager, new object[] { inPlayerWon, inMessage });
    }

    private static BoardState CreateReplayMoveState(UIPlayerBoard.EPlayerType inSnapshotPlayer,
        UIPlayerBoard.EPlayerType inMovePlayer, int inMoveId, string inCardId)
    {
        return new BoardState
        {
            ActivePlayer = inSnapshotPlayer,
            Move = new Move
            {
                ID = inMoveId,
                Data = new MoveData
                {
                    PlayerId = (int)inMovePlayer,
                    CardId = inCardId
                }
            }
        };
    }

    private static void SetField(object target, string fieldName, object value)
    {
        var field = FindField(target.GetType(), fieldName);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = FindField(target.GetType(), fieldName);
        return (T)field.GetValue(target);
    }

    private static FieldInfo FindField(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;

            type = type.BaseType;
        }

        throw new MissingFieldException(fieldName);
    }
}
