using System;
using System.Collections.Generic;
using System.Reflection;
using Elemento.Domain;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using DomainAiDifficulty = Elemento.Domain.EAiDifficulty;
using DomainMoveType = Elemento.Domain.EMoveType;

public class UnityDomainMapperTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        Move.MoveId = 0;

        for (int i = this.createdObjects.Count - 1; i >= 0; --i)
        {
            if (this.createdObjects[i] != null)
                UnityEngine.Object.DestroyImmediate(this.createdObjects[i]);
        }

        this.createdObjects.Clear();
    }

    [TestCase(SettingsManager.EAiDifficulty.Easy, DomainAiDifficulty.Easy)]
    [TestCase(SettingsManager.EAiDifficulty.Medium, DomainAiDifficulty.Medium)]
    [TestCase(SettingsManager.EAiDifficulty.Hard, DomainAiDifficulty.Hard)]
    public void MapDifficulty_MapsEveryUnityDifficulty(SettingsManager.EAiDifficulty difficulty,
        DomainAiDifficulty expected)
    {
        Assert.That(UnityDomainMapper.MapDifficulty(difficulty), Is.EqualTo(expected));
    }

    [Test]
    public void CreatePlayerView_MapsOnlyOwnHandAndMapsReturnedMove()
    {
        var aiBoard = CreateBoard("AI", UIPlayerBoard.EPlayerType.PlayerB);
        var opponentBoard = CreateBoard("Opponent", UIPlayerBoard.EPlayerType.PlayerA);
        AddCard(aiBoard.Hand.CardCells[0], "ai-hand", 7, inPoints: 2);
        AddCard(aiBoard.Hand.CardCells[1], "ai-power", 8, inHeal: 5, inIsPowerCard: true);
        AddCard(aiBoard.ElementZones[0].CardCell, "ai-pile-bottom", 9, inPoints: 1, inShield: 3);
        AddCard(aiBoard.ElementZones[0].CardCell, "ai-pile-top", 9, inMultiplier: 2);
        AddCard(opponentBoard.Hand.CardCells[0], "opponent-secret", 10, inPoints: 3);
        AddCard(opponentBoard.ElementZones[0].CardCell, "opponent-visible", 11, inPoints: 4);
        aiBoard.ElementZones[0].Points = 6;
        aiBoard.ElementZones[0].FaceUpPoints = 4;
        aiBoard.ElementZones[1].FrozenHealth = 3;

        var rules = new RulesConfigData
        {
            MaxCardsInHand = 7,
            MovesPerTurn = 1,
            MaxCardsPerZone = 20,
            DiscardMode = Elemento.Domain.EDiscardMode.AllowDiscard
        };
        var boards = new[] { opponentBoard, aiBoard };
        var view = UnityDomainMapper.CreatePlayerView(aiBoard, boards, 23, 5, 12,
            GameManager.ETurnPhase.OpponentPlaying, 1, rules);

        Assert.That(view.Player.Id, Is.EqualTo((int)UIPlayerBoard.EPlayerType.PlayerB));
        Assert.That(view.Player.Hand.Count, Is.EqualTo(2));
        Assert.That(view.Player.Hand[0].Id, Is.EqualTo("ai-hand"));
        Assert.That(view.Player.Hand[1].Id, Is.EqualTo("ai-power"));
        Assert.That(view.Player.Hand[1].IsPowerCard, Is.True);
        Assert.That(view.Player.Hand[1].WildZoneHeal, Is.EqualTo(5));
        Assert.That(view.Player.Zones[0].Cards[0].Id, Is.EqualTo("ai-pile-bottom"));
        Assert.That(view.Player.Zones[0].Cards[0].Shields[0], Is.EqualTo(3));
        Assert.That(view.Player.Zones[0].Cards[1].Id, Is.EqualTo("ai-pile-top"));
        Assert.That(view.Player.Zones[0].Cards[1].Multipliers[0], Is.EqualTo(2));
        Assert.That(view.Player.Zones[0].Points, Is.EqualTo(6));
        Assert.That(view.Player.Zones[0].FaceUpPoints, Is.EqualTo(4));
        Assert.That(view.Player.Zones[0].Health, Is.EqualTo(10));
        Assert.That(view.Player.Zones[0].MaxHealth, Is.EqualTo(10));
        Assert.That(view.Player.Zones[1].FrozenHealth, Is.EqualTo(3));
        Assert.That(view.Opponents[0].HandCount, Is.EqualTo(1));
        Assert.That(view.Opponents[0].Zones[0].Cards[0].Id, Is.EqualTo("opponent-visible"));
        Assert.That(view.DeckCount, Is.EqualTo(23));
        Assert.That(view.PowerDeckCount, Is.EqualTo(5));

        MoveData playMove = null;
        foreach (var legalMove in view.LegalMoves)
        {
            Assert.That(legalMove.CardId, Is.Not.EqualTo("opponent-secret"));
            if (legalMove.Type == DomainMoveType.PlayCard && legalMove.CardId == "ai-hand"
                && legalMove.TargetPlayerId == (int)aiBoard.PlayerType
                && legalMove.TargetZoneId == (int)CardData.EElementType.Air)
            {
                playMove = legalMove;
            }
        }

        Assert.That(playMove, Is.Not.Null);
        var illegalMove = playMove.Clone();
        illegalMove.CardId = "opponent-secret";
        Assert.That(UnityDomainMapper.IsLegalMove(illegalMove, view), Is.False);
        Assert.That(UnityDomainMapper.TryCreateMove(playMove, view, aiBoard, boards,
            out var unityMove, out bool shouldEndTurn, out string error), Is.True, error);
        Assert.That(shouldEndTurn, Is.False);
        Assert.That(unityMove.SourceCell.TopCard.Id, Is.EqualTo("ai-hand"));
        Assert.That(unityMove.TargetCell, Is.SameAs(aiBoard.ElementZones[0].CardCell));
    }

    private UIPlayerBoard CreateBoard(string inName, UIPlayerBoard.EPlayerType inPlayerType)
    {
        var boardObject = CreateGameObject(inName);
        var board = AddPlayerBoardIgnoringEditorCallbacks(boardObject);
        board.Name = inName;
        board.PlayerType = inPlayerType;

        var hand = CreateComponent<UIHand>(inName + " Hand", board.transform);
        hand.CardCells = new List<UICardCell>();
        for (int i = 0; i < 2; ++i)
        {
            var cell = CreateCell(inName + " Hand " + i, UICardCell.ECellType.Hand,
                CardData.EElementType.None, hand.transform);
            cell.PlayerType = inPlayerType;
            hand.CardCells.Add(cell);
        }

        board.Hand = hand;
        board.ElementZones = new UIElementZone[4];
        for (int i = 0; i < board.ElementZones.Length; ++i)
        {
            var elementType = (CardData.EElementType)(i + 1);
            var cell = CreateCell(inName + " Zone " + elementType, UICardCell.ECellType.ElementZone,
                elementType, board.transform);
            cell.PlayerType = inPlayerType;
            cell.MaxCards = 20;

            var zone = cell.gameObject.AddComponent<UIElementZone>();
            zone.PlayerType = inPlayerType;
            zone.CardCell = cell;
            zone.Health = 10;
            zone.SetMaxHealth(10);
            cell.ElementZone = zone;
            board.ElementZones[i] = zone;
        }

        return board;
    }

    private UICardCell CreateCell(string inName, UICardCell.ECellType inCellType,
        CardData.EElementType inElementType, Transform inParent)
    {
        var cellObject = CreateGameObject(inName, inParent);
        var cell = cellObject.AddComponent<UICardCell>();
        cell.CellType = inCellType;
        cell.ElementType = inElementType;
        cell.CardHolder = cellObject.transform;
        return cell;
    }

    private void AddCard(UICardCell inCell, string inId, int inDefinitionId,
        int inPoints = 0, int inMultiplier = 0, int inShield = 0, int inHeal = 0,
        bool inIsPowerCard = false)
    {
        var card = CreateComponent<Card>(inId, inCell.transform);
        var flip = card.gameObject.AddComponent<UICardFlip>();
        SetField(flip, "animState", UICardFlip.ECardFlipState.Front);

        var data = new CardData
        {
            Index = inDefinitionId,
            Name = inId
        };
        if (inCell.ElementType != CardData.EElementType.None)
        {
            int elementIndex = (int)inCell.ElementType - 1;
            data.Points[elementIndex] = inPoints;
            data.Multipliers[elementIndex] = inMultiplier;
            data.Shields[elementIndex] = inShield;
        }
        else
        {
            data.WildPoints = inPoints;
            data.WildMultiplier = inMultiplier;
            data.WildShield = inShield;
        }
        data.WildZoneHeal = inHeal;
        data.IsPowerCard = inIsPowerCard;

        card.Data = data;
        card.CardFlip = flip;
        SetField(card, "<Id>k__BackingField", inId);
        inCell.Cards.Add(card);
    }

    private static UIPlayerBoard AddPlayerBoardIgnoringEditorCallbacks(GameObject inGameObject)
    {
        bool previous = LogAssert.ignoreFailingMessages;
        LogAssert.ignoreFailingMessages = true;
        try
        {
            return inGameObject.AddComponent<UIPlayerBoard>();
        }
        finally
        {
            LogAssert.ignoreFailingMessages = previous;
        }
    }

    private T CreateComponent<T>(string inName, Transform inParent = null) where T : Component
    {
        return CreateGameObject(inName, inParent).AddComponent<T>();
    }

    private GameObject CreateGameObject(string inName, Transform inParent = null)
    {
        var gameObject = new GameObject(inName);
        gameObject.SetActive(false);
        if (inParent != null)
            gameObject.transform.SetParent(inParent, false);

        this.createdObjects.Add(gameObject);
        return gameObject;
    }

    private static void SetField(object inTarget, string inFieldName, object inValue)
    {
        var field = FindField(inTarget.GetType(), inFieldName);
        field.SetValue(inTarget, inValue);
    }

    private static FieldInfo FindField(Type inType, string inFieldName)
    {
        while (inType != null)
        {
            var field = inType.GetField(inFieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;

            inType = inType.BaseType;
        }

        throw new MissingFieldException(inFieldName);
    }
}
