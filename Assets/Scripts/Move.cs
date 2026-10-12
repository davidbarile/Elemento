using System;
using MoveData = Elemento.Domain.MoveData;
using EMoveType = Elemento.Domain.EMoveType;
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
    public MoveData Data;

    public static Move CreateMove(UICardCell inSourceCell, UICardCell inTargetCell, bool inIsDiscard = false, UIElementZone inActivatedZone = null, bool inIsDealHand = false, ECardType inCardType = ECardType.None, int inPlayerId = 0, string inCardId = null)
    {
        int moveId = Move.MoveId;
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
            ID = moveId,
            Data = CreateMoveData(moveId, inSourceCell, inTargetCell, inIsDiscard, inActivatedZone, inIsDealHand, inPlayerId, inCardId)
        };

        ++Move.MoveId;

        if (inSourceCell != null && inSourceCell.CellType == ECellType.Hand)
            move.SourceCellIndex = inSourceCell.transform.GetSiblingIndex();

        if (inTargetCell != null && inTargetCell.CellType == ECellType.Hand)
            move.TargetCellIndex = inTargetCell.transform.GetSiblingIndex();

        return move;
    }

    private static MoveData CreateMoveData(int inMoveId, UICardCell inSourceCell, UICardCell inTargetCell, bool inIsDiscard, UIElementZone inActivatedZone, bool inIsDealHand, int inPlayerId, string inCardId)
    {
        var playerType = inSourceCell != null && inSourceCell.PlayerType != UIPlayerBoard.EPlayerType.None
            ? inSourceCell.PlayerType
            : inTargetCell != null ? inTargetCell.PlayerType : UIPlayerBoard.EPlayerType.None;
        int playerId = inPlayerId > 0 ? inPlayerId : (int)playerType;
        var card = inIsDiscard ? null : inTargetCell?.TopCard;

        return new MoveData
        {
            Id = MoveData.CreateId(inMoveId),
            PlayerId = playerId,
            Type = GetMoveType(inIsDiscard, inActivatedZone, inIsDealHand),
            CardId = !string.IsNullOrEmpty(inCardId) ? inCardId : card != null ? card.Id : null,
            SourceZoneId = GetZoneId(inSourceCell),
            TargetPlayerId = inTargetCell != null && inTargetCell.PlayerType != UIPlayerBoard.EPlayerType.None ? (int)inTargetCell.PlayerType : playerId,
            TargetZoneId = inActivatedZone != null ? (int)inActivatedZone.CardCell.ElementType : GetZoneId(inTargetCell),
            ExpectedRevision = -1
        };
    }

    private static EMoveType GetMoveType(bool inIsDiscard, UIElementZone inActivatedZone, bool inIsDealHand)
    {
        if (inIsDealHand)
            return EMoveType.DrawCard;
        if (inActivatedZone != null)
            return EMoveType.ActivateZone;
        if (inIsDiscard)
            return EMoveType.DiscardCard;

        return EMoveType.PlayCard;
    }

    private static int GetZoneId(UICardCell inCell)
    {
        if (inCell == null || inCell.CellType != ECellType.ElementZone)
            return 0;

        return (int)inCell.ElementType;
    }
}
