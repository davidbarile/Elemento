using UnityEngine;
using UnityEngine.Events;

public class UIHighlightableObject : MonoBehaviour
{
    [SerializeField] private UnityEvent onShow;
    [SerializeField] private UnityEvent onHide;

    public bool IsEnabled = true;

    public void Show()
    {
        if(this.IsEnabled)
            this.onShow?.Invoke();
    }

    public void Hide()
    {
        if (this.IsEnabled)
            this.onHide?.Invoke();
    }
}