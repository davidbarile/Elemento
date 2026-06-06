using UnityEngine;
using UnityEngine.UI;

public class ResetScrollRectOnEnable : MonoBehaviour
{
    [Header("0 = bottom/left   1 = top/right")]
    [Range(0, 1)]
    public float Position;

    private ScrollRect scrollRect;

    public ScrollRect ScrollRect
    {
        get
        {
            if (this.scrollRect == null)
                this.scrollRect = this.GetComponent<ScrollRect>();

            return this.scrollRect;
        }
    }

    private void OnEnable()
    {
        ResetPosition();
    }

    public void ResetPosition()
    {
        ScrollToPosition(this.Position);
    }

    public void ScrollToPosition(float inPosition)
    {
        this.ScrollRect.horizontalNormalizedPosition = inPosition;
        this.ScrollRect.verticalNormalizedPosition = inPosition;
    }
}