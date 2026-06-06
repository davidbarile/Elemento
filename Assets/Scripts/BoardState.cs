using System;
using System.Collections.Generic;
using UnityEngine;
using static CardData;
using static UIPlayerBoard;

[Serializable]
public class BoardTurn
{
    public List<BoardState> BoardStates = new();
}

[Serializable]
public class BoardState
{
    public bool ShouldSet;
    [Space]
    public int TurnNumber;
    public int MoveNumber;
    public int TotalMovesTaken;
    [Space]
    public EPlayerType ActivePlayer;
    [Space]
    public bool IsDealHand;

    [Space]
    public float Value;
    [Space]
    public Move Move;
    [Space]
    public List<BoardPlayer> Players = new();

    //TODO: make this very complex, taking into account, frozen, destroyed zones, shielded cards, points, etc.
    public static int CompareBoards(BoardState inBoardA, BoardState inBoardB)
    {
        return inBoardA.Value.CompareTo(inBoardB.Value);
    }
}


[Serializable]
public class BoardPlayer
{
    public BoardHand Hand;
    public BoardZone[] ElementZones = new BoardZone[4];

    public int TotalPoints
    {
        get
        {
            int total = 0;
            foreach (var zone in this.ElementZones)
            {
                total += zone.Points;
            }
            return total;
        }
    }
}

[Serializable]
public class BoardZone
{
    public EElementType ElementType;
    //public EPlayerType PlayerType;
    [Space]
    public int Points = 0;
    public int FaceUpPoints = 0;

    [Space]
    public bool IsActivated;
    public bool IsFrozen => this.FrozenHealth > 0;
    public bool IsDestroyed;

    [Space]
    public int Health = 0;
    public int MaxHealth;
    [Space]
    public int FrozenHealth = 0;

    [Space]
    public List<CardData> Cards = new();

    public CardData TopCard
    {
        get
        {
            if (this.Cards.Count > 0)
                return this.Cards[this.Cards.Count - 1];
            else
                return null;
        }
    }
}

[Serializable]
public class BoardHand
{
    public List<CardData> Cards = new();
}