using UnityEngine;

public class UIDragOverlay : MonoBehaviour
{
    public static UIDragOverlay IN;

    public RectTransform Overlay;
    public Camera Camera { get; private set; }

    public GameObject DummyObj { get; private set; }

    private void Awake()
    {
        IN = this;

        this.Camera = Camera.main;

        this.DummyObj = new GameObject();
        this.DummyObj.transform.SetParent(this.transform);
        this.DummyObj.name = "-*-";
        this.DummyObj.SetActive(false);

        SetVisible(false);
    }

    public void SetVisible(bool inIsVisible)
    {
        this.Overlay.gameObject.SetActive(inIsVisible);
    }

    public void SetDraggingItem(Transform inDragItem)
    {
        inDragItem.SetParent(this.transform);
        SetVisible(true);
    }
}