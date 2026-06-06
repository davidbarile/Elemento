using System;
using static Card;
using static UICardCell;

[Serializable]
public class Move
{
    public static bool ShouldRecordMoves = true;
    public static int MoveId = 0;

    public int ID = -1;
    public UICardCell SourceCell;
    public UICardCell TargetCell;
    public float Value;
    public bool IsDealHand;
    public bool IsDiscard;
    public UIElementZone ActivatePowerZone;
    public ECardType CardType;
    public int SourceCellIndex;
    public int TargetCellIndex;

    public static Move CreateMove(UICardCell inSourceCell, UICardCell inTargetCell, bool inIsDiscard = false, UIElementZone inActivatedZone = null, bool inIsDealHand = false, ECardType inCardType = ECardType.None)
    {
        int value = inIsDiscard ? -1 : 1;
        var move = new Move
        {
            SourceCell = inSourceCell,
            TargetCell = inTargetCell,
            Value = value,
            IsDiscard = inIsDiscard,
            ActivatePowerZone = inActivatedZone,
            CardType = inCardType,
            SourceCellIndex = -1,
            TargetCellIndex = -1,
            IsDealHand = inIsDealHand,
            ID = Move.MoveId
        };

        ++Move.MoveId;

        if (inSourceCell != null && inSourceCell.CellType == ECellType.Hand)
            move.SourceCellIndex = inSourceCell.transform.GetSiblingIndex();

        if (inTargetCell != null && inTargetCell.CellType == ECellType.Hand)
            move.TargetCellIndex = inTargetCell.transform.GetSiblingIndex();

        return move;
    }
}