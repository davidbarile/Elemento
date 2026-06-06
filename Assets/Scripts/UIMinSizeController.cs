using UnityEngine;
using UnityEngine.UI;

public class UIMinSizeController : MonoBehaviour
{
    [SerializeField] private float minWidth = 0;
    [SerializeField] private float minHeight = 0;

    private RectTransform rectTransform;
    private void Start()
    {
        this.rectTransform = this.GetComponent<RectTransform>();
    }

    private void Update()
    {
        this.rectTransform.ForceUpdateRectTransforms();
        LayoutRebuilder.ForceRebuildLayoutImmediate(this.rectTransform);
    }

    private void LateUpdate()
    {
        if (this.minWidth > 0 && this.rectTransform.rect.width < this.minWidth)
        {
            this.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, this.minWidth);
        }

        if (this.minHeight > 0 && this.rectTransform.rect.height < this.minHeight)
        {
            this.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, this.minHeight);
        }
    }
}