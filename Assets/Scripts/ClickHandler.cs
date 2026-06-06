using UnityEngine;
using UnityEngine.EventSystems;
using static UnityEngine.EventSystems.EventTrigger;

/// <summary>
/// Use this to not conflict with ScrollViews and Inputfields and such
/// </summary>
public class ClickHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,  IBeginDragHandler, IEndDragHandler
{
    public TriggerEvent onPointerClick;
    public TriggerEvent onPointerEnter;
    public TriggerEvent onPointerExit;
    public TriggerEvent onPointerDown;
    public TriggerEvent onPointerUp;
    public TriggerEvent onDragStart;
    public TriggerEvent onDragEnd;

    public void OnPointerClick(PointerEventData eventData)
    {
        this.onPointerClick.Invoke(eventData);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        this.onPointerEnter.Invoke(eventData);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        this.onPointerExit.Invoke(eventData);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        this.onPointerDown.Invoke(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        this.onPointerUp.Invoke(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        this.onDragStart.Invoke(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        this.onDragStart.Invoke(eventData);
    }
}