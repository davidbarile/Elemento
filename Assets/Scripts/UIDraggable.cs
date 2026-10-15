using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public enum EDragMode
{
    Drag,
    ClickDrag,
    Both
}

public class UIDraggable : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IDragHandler
{
    public static bool ShouldBlockInput;
    public static bool IsDragPointerDown;
    public static bool ShouldSnapToCenter;

    private static UIDraggable activeDraggedItem;

    public static EDragMode DRAG_MODE = EDragMode.Drag;

    public bool IsDragging { get; private set; }
    public bool IsDraggable { get; set; }

    protected bool pointerClickFlag;

    protected Vector2 clickPosition;
    private Vector2 mouseOffset;
    private float buffer = 10;
    protected float clickStartTime = -1;

    private float doubleClickTolerance = .3f;
    private float doubleClickStartTime = -1;

    private bool wasDraggedOutsideOfBuffer;
    private bool isClickDraggingFlag;

    private UICardCell originalParentCell;

    private List<UIHighlightableObject> highlightedObjects = new List<UIHighlightableObject>();

    public bool IsAnimateDragging { get; private set; }

    public virtual void OnPointerClick(PointerEventData eventData)
    {
        if (GameManager.ShouldBlockInput) return;
        if (!GameManager.IN.IsPlayersTurn) return;

        float elapsedTimeSinceLastClick = Time.time - this.doubleClickStartTime;

        StopAllCoroutines();

        if (eventData.button == PointerEventData.InputButton.Left)
        {
            if (this.doubleClickStartTime > -1 && elapsedTimeSinceLastClick < this.doubleClickTolerance)
            {
                this.doubleClickStartTime = -1;
                OnDoubleClick();
            }
            else
            {
                this.doubleClickStartTime = Time.time;
                StartCoroutine(ResetDoubleClickTimer());
            }
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelDraggingItem();

            Debug.Log($"Right Click");
        }
    }

    private IEnumerator ResetDoubleClickTimer()
    {
        yield return new WaitForSeconds(this.doubleClickTolerance);
        this.doubleClickStartTime = -1;
    }

    public virtual void OnDoubleClick()
    {
        if (GameManager.ShouldBlockInput) return;
        if (!GameManager.IN.IsPlayersTurn) return;

        var dragOverlay = this.GetComponentInParent<UIDragOverlay>();
        if (dragOverlay != null)
        {
            this.GetComponent<Card>().DestroySelf();
        }

        //Debug.Log($"OnDoubleClick");

        //var cardFlip = this.GetComponent<UICardFlip>();

        //if (cardFlip != null)
        //    cardFlip.ToggleState();
    }

    public virtual void OnPointerDown(PointerEventData eventData)
    {
        if (GameManager.ShouldBlockInput) return;
        if (!GameManager.IN.IsPlayersTurn) return;

        UIDraggable.IsDragPointerDown = true;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelDraggingItem();
            return;
        }

        this.clickStartTime = Time.time;
        this.clickPosition = GetMousePosition();

        if (ShouldSnapToCenter)
        {
            this.mouseOffset = Vector2.zero;
        }
        else
        {
            UIDragOverlay.IN.DummyObj.transform.position = this.transform.position;
            this.mouseOffset = (Vector2)UIDragOverlay.IN.DummyObj.transform.localPosition - this.clickPosition;
        }

        if (DRAG_MODE == EDragMode.Drag || DRAG_MODE == EDragMode.Both)
        {
            if (!this.IsDragging)
            {
                this.pointerClickFlag = true;
                this.wasDraggedOutsideOfBuffer = false;
            }

            this.isClickDraggingFlag = this.IsDragging;
        }
    }

    public virtual void OnPointerUp(PointerEventData eventData)
    {
        if (GameManager.ShouldBlockInput) return;
        if (!GameManager.IN.IsPlayersTurn) return;

        UIDraggable.IsDragPointerDown = false;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            CancelDraggingItem();
            return;
        }

        if (DRAG_MODE == EDragMode.Drag || DRAG_MODE == EDragMode.Both)
        {
            this.pointerClickFlag = false;

            if (this.IsDragging && (this.wasDraggedOutsideOfBuffer || this.isClickDraggingFlag))
            {
                this.wasDraggedOutsideOfBuffer = false;
                this.isClickDraggingFlag = false;
                StopDraggingItem(eventData);
            }
        }
        else if (DRAG_MODE == EDragMode.ClickDrag)
        {
            if (!this.IsDragging)
            {
                float delta = Vector2.Distance(this.clickPosition, GetMousePosition());

                if (delta < this.buffer)
                {
                    this.pointerClickFlag = false;
                    BeginDraggingItem();
                }
            }
            else
            {
                StopDraggingItem(eventData);
            }
        }
    }

    protected bool CheckStartDrag()
    {
        if (this.pointerClickFlag)
        {
            var dragItemPos = GetMousePosition();

            float delta = Vector2.Distance(this.clickPosition, dragItemPos);
            float elapsedTime = Time.time - this.clickStartTime;

            bool isDraggedOutOfBuffer = delta > this.buffer;
            bool isClickedAndNotDragged = delta < this.buffer && elapsedTime < .1f && DRAG_MODE != EDragMode.Drag;

            if (isDraggedOutOfBuffer || isClickedAndNotDragged)
            {
                this.pointerClickFlag = false;
                BeginDraggingItem();
            }
        }

        return !this.pointerClickFlag;
    }

    protected Vector2 GetMousePosition()
    {
        var mousePos = UIDragOverlay.IN.Camera.WorldToScreenPoint(Input.mousePosition);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(UIDragOverlay.IN.Overlay, mousePos, UIDragOverlay.IN.Camera, out var dragItemPos);

        return dragItemPos;
    }

    protected virtual void LateUpdate()
    {
        if (!CheckStartDrag() || this.IsAnimateDragging) return;

        if (this.IsDragging)
        {
            var dragItemPos = GetMousePosition();

            float delta = Vector2.Distance(this.clickPosition, dragItemPos);

            if (delta > this.buffer)
                this.wasDraggedOutsideOfBuffer = true;

            HighlightItemsUnderCursor();

            this.transform.localPosition = dragItemPos + this.mouseOffset;
        }
    }

    private void HighlightItemsUnderCursor()
    {
        var results = GameObjectUnderPointer();

        //string debugString = string.Empty;

        List<UIHighlightableObject> currentItemsUnderCursor = new List<UIHighlightableObject>();

        foreach (var result in results)
        {
            //debugString += $"{result.gameObject.name}, ";

            var highlight = result.gameObject.GetComponent<UIHighlightableObject>();

            if (highlight != null)
            {
                if (!this.highlightedObjects.Contains(highlight))
                {
                    this.highlightedObjects.Add(highlight);
                    highlight.Show();
                }

                currentItemsUnderCursor.Add(highlight);
            }
        }

        //Debug.Log($"under cursor = {debugString}");

        List<UIHighlightableObject> listCopy = new List<UIHighlightableObject>(this.highlightedObjects);

        //when item is not longer under cursor, hide and remove from list
        foreach (var item in listCopy)
        {
            if (!currentItemsUnderCursor.Contains(item))
            {
                item.Hide();
                this.highlightedObjects.Remove(item);
            }
        }
    }

    private void HideItemHighlights()
    {
        foreach (var item in this.highlightedObjects)
        {
            if (item != null)
                item.Hide();
        }

        this.highlightedObjects.Clear();
    }

    protected virtual void BeginDraggingItem()
    {
        //Debug.Log($"BeginDraggingItem");

        if ((GameManager.IsApplyingRemoteUpdate && !this.IsAnimateDragging)
            || UIDraggable.ShouldBlockInput || (!this.IsDraggable && !this.IsAnimateDragging) || this.IsDragging) return;

        var cell = this.transform.GetComponentInParent<UICardCell>();
        if (cell == null || (cell.PlayerType != GameManager.IN.CurrentPlayer && !this.IsAnimateDragging)) return;

        if (UITutorialPanel.IsTutorialMode && cell.CellType != UICardCell.ECellType.Deck && GameManager.IN.IsPlayersTurn)
        {
            bool allowInteract = UITutorialPanel.IN.InteractableCardCells.Contains(cell) && !UITutorialPanel.IN.ShouldBlockInput;

            //Debug.Log($"BeginDraggingItem()  TutorialIndex = {UITutorialPanel.IN.TutorialStateIndex}  allowInteract = {allowInteract}   count = {UITutorialPanel.IN.InteractableCardCells.Count}");

            if (!allowInteract)
            {
                float pitch = Random.Range(.5f, .6f);
                AudioManager.IN.PlayClip(AudioManager.IN.TutorialBlockClip, .2f, pitch);
                return;
            }
        }

        UIDraggable.ShouldBlockInput = true;
        this.IsDragging = true;
        UIDraggable.activeDraggedItem = this;

        cell.TopCard.ResetDimmedDecorations();

        this.originalParentCell = cell;

        UIDragOverlay.IN.SetDraggingItem(this.transform);
    }

    public void SetOriginalParentCell()
    {
        var cell = this.transform.GetComponentInParent<UICardCell>();
        this.originalParentCell = cell;
    }

    protected virtual void StopDraggingItem(PointerEventData eventData)
    {
        UIDraggable.ResetInputState();

        var cell = UICardCell.GetCellAtPosition(this.transform.position);

        if (cell != null)
        {
            cell.HandleDrop(this.originalParentCell);
        }
        else
        {
            var hand = UIHand.GetUIHandAtPosition(this.transform.position);

            if (hand != null)
            {
                hand.HandleDrop(this.originalParentCell);
            }
            else
            {
                this.originalParentCell.ReturnCardToCell(LOC.IN.Reject_InvalidDropLocation[LOC.LI], AudioManager.ERejectReason.InvalidDropLocation);
            }
        }
    }

    public virtual void CancelDraggingItem()
    {
        //Debug.Log($"CancelDraggingItem");

        try
        {
            if (this.IsDragging && this.originalParentCell != null)
                this.originalParentCell.ReturnCardToCell();
        }
        finally
        {
            if (UIDraggable.activeDraggedItem == this)
                UIDraggable.ResetInputState();
            else
            {
                this.IsDragging = false;
                HideItemHighlights();
                UIDraggable.IsDragPointerDown = false;
            }
        }
    }

    public static void CancelActiveDrag(bool inShouldReturnToCell = true)
    {
        var draggedItem = UIDraggable.activeDraggedItem;
        try
        {
            if (draggedItem == null)
                return;

            draggedItem.StopAllCoroutines();
            draggedItem.transform.DOKill();
            draggedItem.IsAnimateDragging = false;

            if (inShouldReturnToCell)
                draggedItem.CancelDraggingItem();
        }
        finally
        {
            UIDraggable.ResetInputState();
        }
    }

    public static void ResetInputState()
    {
        var draggedItem = UIDraggable.activeDraggedItem;
        UIDraggable.activeDraggedItem = null;
        UIDraggable.ShouldBlockInput = false;
        UIDraggable.IsDragPointerDown = false;

        if (draggedItem != null)
        {
            draggedItem.IsDragging = false;
            draggedItem.pointerClickFlag = false;
            draggedItem.isClickDraggingFlag = false;
            draggedItem.wasDraggedOutsideOfBuffer = false;
            draggedItem.HideItemHighlights();
        }

        if (UIDragOverlay.IN != null)
            UIDragOverlay.IN.SetVisible(false);
    }

    private void OnDestroy()
    {
        if (UIDraggable.activeDraggedItem == this)
            UIDraggable.ResetInputState();
    }

    public static List<RaycastResult> GameObjectUnderPointer()
    {
        return GameObjectAtPosition(Input.mousePosition);
    }

    public static List<RaycastResult> GameObjectAtPosition(Vector3 inPosition)
    {
        var pointerData = new PointerEventData(EventSystem.current)
        {
            pointerId = -1
        };

        pointerData.position = inPosition;

        var raycastResults = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, raycastResults);

        return raycastResults;
    }

    //this is to fix a bug that happens with ScrollRect interfering with OnPointerUp()
    public void OnDrag(PointerEventData eventData) { }

    public void AnimateTo(Vector3 inDestPosition, float inTweenDuration = .5f, float inDelayAtEnd = 0)
    {
        this.IsAnimateDragging = true;

        BeginDraggingItem();

        UIDraggable.ShouldBlockInput = true;

        this.transform.DOMove(inDestPosition, inTweenDuration, false).SetEase(Ease.InOutSine).OnComplete(() =>
        {
            this.transform.position = inDestPosition;

            if (inDelayAtEnd == 0)
            {
                StopDraggingItem(null);
                this.IsAnimateDragging = false;
            }
            else
            {
                this.gameObject.SetActive(true);//not sure if necessary, but prevents an error
                StartCoroutine(DelayedAnimCompleteCo(inDelayAtEnd));
            }
        });
    }

    private IEnumerator DelayedAnimCompleteCo(float inDelay)
    {
        yield return new WaitForSeconds(inDelay);

        StopDraggingItem(null);
        this.IsAnimateDragging = false;
    }
}
