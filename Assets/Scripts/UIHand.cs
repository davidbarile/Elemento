using System;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using static CardData;
using static GameManager;
using static SettingsManager;
using static UICardCell;
using static UICardFlip;
using static UIPlayerBoard;

public class UIHand : MonoBehaviour
{
    [ReadOnly]
    public List<UICardCell> CardCells;
    private List<UICardCell> orderedCardCells;

    [Space]
    [SerializeField] private TextMeshProUGUI promptText;
    [FormerlySerializedAs("horizontalGroupRectTrans")]
    public RectTransform HandGrid;

    public int NumCardsInHand
    {
        get
        {
            int numCards = 0;
            foreach (var cell in this.CardCells)
            {
                if (cell.TopCard != null)
                    ++numCards;
            }

            return numCards;
        }
    }

    private void Awake()
    {
        RefreshCardCells();

        this.orderedCardCells = this.transform.GetComponentsInChildren<UICardCell>(true).ToList();

        foreach (var cell in this.CardCells)
        {
            cell.OnCardRemoved += HandleCardRemoved;
        }
    }

    private void OnDestroy()
    {
        foreach (var cell in this.CardCells)
        {
            cell.OnCardRemoved -= HandleCardRemoved;
        }
    }

    public void RefreshCardCells()
    {
        this.CardCells = this.transform.GetComponentsInChildren<UICardCell>(true).ToList();
    }

    public void SwapPlayerState(UIHand inOther)
    {
        if (inOther == null)
            throw new ArgumentNullException(nameof(inOther));
        if (this.CardCells.Count != inOther.CardCells.Count)
            throw new InvalidOperationException("Cannot swap hands with different card-cell counts.");

        for (int i = 0; i < this.CardCells.Count; i++)
        {
            var thisCell = this.CardCells[i];
            var otherCell = inOther.CardCells[i];
            var thisCards = thisCell.Cards;
            bool isThisCellActive = thisCell.gameObject.activeSelf;

            thisCell.Cards = otherCell.Cards;
            otherCell.Cards = thisCards;
            thisCell.gameObject.SetActive(otherCell.gameObject.activeSelf);
            otherCell.gameObject.SetActive(isThisCellActive);

            RefreshCardParents(thisCell);
            RefreshCardParents(otherCell);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(this.HandGrid);
        LayoutRebuilder.ForceRebuildLayoutImmediate(inOther.HandGrid);
    }

    private static void RefreshCardParents(UICardCell inCell)
    {
        foreach (var card in inCell.Cards)
            inCell.SetParentCell(card, false);

        inCell.RefreshVisibleCardCount();
    }

    public void ResetCardCellSiblingIndices()
    {
        for (int i = 0; i < this.orderedCardCells.Count; i++)
        {
            this.orderedCardCells[i].transform.SetSiblingIndex(i);
        }

        RefreshCardCells();
    }

    public void HandleCardRemoved(UICardCell inCardCell)
    {
        var layoutElement = inCardCell.GetComponent<LayoutElement>();

        if (layoutElement != null)
        {
            UIDraggable.ShouldBlockInput = true;

            var originalWidth = layoutElement.minWidth;

            var endSize = new Vector2(0, layoutElement.minHeight);
            layoutElement.DOMinSize(endSize, GameManager.IN.HandInsertTweenDuration).OnComplete(() =>
            {
                layoutElement.minWidth = originalWidth;
                layoutElement.gameObject.SetActive(false);
                layoutElement.transform.SetAsLastSibling();

                LayoutRebuilder.ForceRebuildLayoutImmediate(this.HandGrid);

                RefreshCardCells();

                //if (inCardCell.CellType == ECellType.PlayerHand)
                //{
                //    GameManager.IN.RefreshPlayerHandText();
                //}

                UIDraggable.ShouldBlockInput = false;
            });
        }
    }

    public bool HandleDrop(UICardCell inSourceCell)
    {
        if (inSourceCell.CellType != ECellType.Hand)
        {
            //do not allow draw cards if no turns left
            bool isTurnLimit = SettingsManager.IN.TutorialSafeMovesPerTurn > 0;
            if (isTurnLimit && GameManager.IN.MovesRemaining <= 0)
            {
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_OutOfMoves[LOC.LI]);//"You're out of moves!\nDiscard a card to continue."
                return false;
            }

            int numCells = Mathf.Min(this.CardCells.Count, SettingsManager.IN.MaxCardsInHand);

            for (int i = 0; i < numCells; i++)
            {
                var cell = this.CardCells[i];

                if (!cell.gameObject.activeSelf)
                {
                    if (cell.Cards.Count > 0)
                    {
                        //Debug.Log($"<color=red>UIHand.HandleDrop()   i = {i}   cell = {cell.name} is invisible but card count is {cell.Cards.Count}   cell.Cards[0] = {cell.Cards[0]}  </color>", cell.gameObject);
                        cell.Cards.Clear();
                    }

                    cell.gameObject.SetActive(true);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(this.HandGrid);

                    cell.TryAddToCell(inSourceCell, true);
                    return true;
                }
            }
        }

        inSourceCell.ReturnCardToCell(string.Empty);
        return false;
    }

    public UICardCell GetFirstOpenCell()
    {
        int numCells = Mathf.Min(this.CardCells.Count, SettingsManager.IN.MaxCardsInHand);

        for (int i = 0; i < numCells; i++)
        {
            var cell = this.CardCells[i];

            if (!cell.gameObject.activeSelf)
            {
                if (cell.Cards.Count > 0)
                {
                    //Debug.Log($"<color=red>UIHand.GetFirstOpenCell()   i = {i}   cell = {cell.name} is invisible but card count is {cell.Cards.Count}   cell.Cards[0] = {cell.Cards[0]}  </color>", cell.gameObject);
                    cell.Cards.Clear();
                }

                return cell;
            }
        }

        return null;
    }

    public void InsertCell(UICardCell inSourceCell, UICardCell inTargetCell)
    {
        var insertedCell = GetFirstOpenCell();

        var rejectReason = LOC.IN.Reject_HandIsFull[LOC.LI];//"Your Hand is Full!";

        bool isOwnHand = inSourceCell.PlayerType == inTargetCell.PlayerType || inSourceCell.PlayerType == EPlayerType.None;

        if (isOwnHand)
            rejectReason = LOC.IN.Reject_CardInOpponentHand[LOC.LI];// "You cannot place cards in another player's hand!";

        if (insertedCell != null && isOwnHand)
        {
            //do not allow draw cards if no turns left
            bool isTurnLimit = SettingsManager.IN.TutorialSafeMovesPerTurn > 0;
            if (isTurnLimit && GameManager.IN.MovesRemaining <= 0)
            {
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_OutOfMoves[LOC.LI]);//"You're out of moves!\nDiscard a card to continue."
                return;
            }

            int targetIndex = inTargetCell.transform.GetSiblingIndex();

            insertedCell.transform.SetSiblingIndex(targetIndex + 1);
            insertedCell.gameObject.SetActive(true);

            var sourceTopCard = inSourceCell.TopCard;
            var targetTopCard = inTargetCell.TopCard;

            insertedCell.Cards.Add(sourceTopCard);
            insertedCell.SetParentCell(sourceTopCard);

            inSourceCell.Cards.Remove(sourceTopCard);
            inSourceCell.OnCardRemoved?.Invoke(inSourceCell);

            RefreshCardCells();

            var layoutElement = insertedCell.GetComponent<LayoutElement>();

            if (layoutElement != null)
            {
                UIDraggable.ShouldBlockInput = true;

                var destWidth = layoutElement.minWidth;

                layoutElement.minWidth = 0;

                //AudioManager.IN.PlayCardDropSound(0);

                var endSize = new Vector2(destWidth, layoutElement.minHeight);
                layoutElement.DOMinSize(endSize, GameManager.IN.HandInsertTweenDuration).OnComplete(() =>
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(this.HandGrid);

                    insertedCell.SetParentCell(sourceTopCard);

                    //if dragging from an element zone
                    if (inSourceCell.ElementType != EElementType.None)
                    {
                        inSourceCell.ElementZone.CalculatePoints();
                    }

                    if (inSourceCell.CellType == ECellType.Deck)
                    {
                        sourceTopCard.CardFlip.SetAnimState(ECardFlipState.FlipToFront);
                        GameManager.IN.SetUndoCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.RevealHiddenCard);
                    }

                    if (inTargetCell.CellType == ECellType.Hand && inSourceCell.CellType != ECellType.Deck)
                    {
                        AudioManager.IN.PlayCardDropSound(9, .1f);
                    }

                    GameManager.IN.PlayerInputBoard.Hand.RefreshPlayerHandText();

                    UIDraggable.ShouldBlockInput = false;
                });
            }

            if (inSourceCell.CellType != ECellType.Deck)
            {
                GameManager.IN.DecrementMovesRemaining();
                GameManager.IN.TryHighlightDiscardPile();
                GameManager.IN.SaveBoardState(inSourceCell, inTargetCell, inCardId: sourceTopCard.Id);
            }
        }
        else
        {
            inSourceCell.ReturnCardToCell(rejectReason);
        }
    }

    public static UIHand GetUIHandAtPosition(Vector3 inPosition)
    {
        var results = UIDraggable.GameObjectAtPosition(inPosition);

        foreach (var result in results)
        {
            var hand = result.gameObject.GetComponent<UIHand>();

            if (hand != null)
                return hand;
        }

        return null;
    }

    public List<Card> GetCardsInHand()
    {
        List<Card> cardsInHand = new();

        foreach (var cell in this.CardCells)
        {
            if (cell.TopCard != null)
            {
                cardsInHand.Add(cell.TopCard);
            }
        }

        return cardsInHand;
    }

    public void SetHandVisible(bool inIsVisible)
    {
        var flipState = inIsVisible ? ECardFlipState.Front : ECardFlipState.Back;

        foreach (var cell in this.CardCells)
        {
            foreach (var card in cell.Cards)
                card.CardFlip.SetAnimState(flipState);
        }
    }

    public void RefreshPlayerHandText()
    {
        if (this.promptText == null) return;

        bool usesOpponentPhases = GameManager.IN.IsPassAndPlay || GameManager.IN.IsOnline;
        bool isDrawPhase = GameManager.IN.TurnPhase == ETurnPhase.PlayerDraw || (usesOpponentPhases && GameManager.IN.TurnPhase == ETurnPhase.OpponentDraw);
        bool isPlayingPhase = GameManager.IN.TurnPhase == ETurnPhase.PlayerPlaying || (usesOpponentPhases && GameManager.IN.TurnPhase == ETurnPhase.OpponentPlaying);
        bool isCurrentHand = this == GameManager.IN.PlayerInputBoard.Hand;

        bool shouldShow = this.NumCardsInHand == 0 && isCurrentHand && GameManager.IN.IsPlayersTurn && (isDrawPhase || isPlayingPhase)
            && SettingsManager.IN.DiscardMode != EDiscardMode.DiscardEndsTurn && !GameManager.IN.IsCurrentSeatControlledByAI
            && !(isDrawPhase && SettingsManager.IN.ShouldAutoDealHand);

        this.promptText.gameObject.SetActive(shouldShow);

        if (shouldShow)
        {
            if (isDrawPhase)
                this.promptText.text = LOC.IN.ClickButtonToDrawCards[LOC.LI];// "Click Button\n To Draw Cards";
            else if (isPlayingPhase)
                this.promptText.text = LOC.IN.ClickButtonToEndTurn[LOC.LI];// "Click Button\n To End Turn";
        }
    }

    public void HidePlayerHandText()
    {
        if (this.promptText == null) return;

        this.promptText.text = string.Empty;
        this.promptText.gameObject.SetActive(false);
    }
}
