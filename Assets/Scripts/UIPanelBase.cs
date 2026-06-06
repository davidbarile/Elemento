using UnityEngine;

public class UIPanelBase : MonoBehaviour
{
    public bool IsShowing { get; private set; }

    private Canvas Canvas
    {
        get
        {
            if (this.canvas == null)
                this.canvas = this.GetComponent<Canvas>();

            return this.canvas;
        }
    }

    [Header("Serialized Automatically if null")]
    [SerializeField] private Canvas canvas;

    public virtual void Show()
    {
        SetVisible(true);
    }

    public virtual void Hide()
    {
        SetVisible(false);
    }

    [Tooltip("This avoids calling functionality of overrides")]
    public void SetVisible(bool inIsVisible)
    {
        this.IsShowing = inIsVisible;

        if(inIsVisible)
            this.gameObject.SetActive(true);

        if (this.Canvas != null)
            this.Canvas.enabled = inIsVisible;
        else
            this.gameObject.SetActive(inIsVisible);
    }
}