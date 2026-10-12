using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
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

        PlayerData.Data = new PlayerData { LanguageIndex = 0 };

        this.settings = CreateComponent<SettingsManager>("Settings");
        this.settings.DiscardMode = SettingsManager.EDiscardMode.None;
        SettingsManager.IN = this.settings;

        this.gameManager = CreateComponent<GameManager>("Game Manager");
        this.gameManager.MovesRemaining = 1;
        GameManager.IN = this.gameManager;

        var localization = CreateComponent<LocalizationManager>("Localization");
        localization.TotalPointsTitle = new[] { "{0}/{1} Total Points" };
        localization.AllZonesDestroyedTitle = new[] { "All zones destroyed" };
        localization.You = new[] { "You" };
        localization.Opponent = new[] { "Opponent" };
        LocalizationManager.IN = localization;
        LOC.IN = localization;
    }

    [TearDown]
    public void TearDown()
    {
        UIWinLosePanel.IN = null;
        GameManager.IN = null;
        SettingsManager.IN = null;
        LocalizationManager.IN = null;
        LOC.IN = null;
        PlayerData.Data = null;
        UITutorialPanel.IsTutorialMode = false;
        BoardStateManager.IsDoingReplay = false;
        BoardStateManager.IsSettingState = false;

        for (var i = this.createdObjects.Count - 1; i >= 0; i--)
        {
            if (this.createdObjects[i] != null)
                UnityEngine.Object.DestroyImmediate(this.createdObjects[i]);
        }

        this.createdObjects.Clear();
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

    [TestCase(49, false)]
    [TestCase(50, true)]
    public void CheckWinLoseState_UsesInclusivePointsTarget(int totalPoints, bool expectedWin)
    {
        this.settings.TotalPointsRequiredToWin = 50;
        var player = CreateBoard("Player", new[] { 10, 10, 10, totalPoints - 30 });
        var opponent = CreateBoard("Opponent", new[] { 0, 0, 0, 0 });
        player.TotalPoints = totalPoints;
        this.gameManager.PlayerBoards = new[] { player, opponent };
        var panel = CreateWinLosePanel(4);

        this.gameManager.CheckWinLoseState(player);

        Assert.That(panel.IsShowing, Is.EqualTo(expectedWin));
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
        SetField(zone, "pointsCountDisplay", CreateGameObject(name + " Points Display", zoneObject.transform));
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
        var board = CreateComponent<UIPlayerBoard>(name);
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
