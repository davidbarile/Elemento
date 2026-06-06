using UnityEngine;

public class ScaleToFitRect : MonoBehaviour
{
    private enum EDimension
    {
        Width,
        Height
    }

    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private EDimension dimensionToScale;
    [SerializeField] private float scaleMultiplier = 1;
    [SerializeField] private bool shouldValidate;
    [SerializeField] private bool shouldUpdate;

    private void Awake()
    {
        if (this.rectTransform == null)
            this.rectTransform = this.GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        SetScale();
    }

    private void OnValidate()
    {
        if (this.shouldValidate)
            SetScale();
    }

    private void Update()
    {
        if (this.shouldUpdate)
            SetScale();
    }

    private void SetScale()
    {
        if (this.dimensionToScale == EDimension.Width)
        {
            this.transform.localScale = Vector3.one * (float)this.rectTransform.rect.width / this.scaleMultiplier;
            //Debug.Log($"width = {this.rectTransform.rect.width}");
        }
        else
        {
            this.transform.localScale = Vector3.one * (float)this.rectTransform.rect.height / this.scaleMultiplier;
        }
    }
}