using UnityEngine;
using UnityEngine.UI;
using static CardData;

public class UIElementIcon : UICardDecorationBase
{
    [SerializeField] private Image[] icons;

    public EElementType ElementType { get; private set; }

    public void HideIcons()
    {
        foreach (var icon in this.icons)
        {
            icon.gameObject.SetActive(false);
        }
    }

    public void SetIconVisible(int inIndex)
    {
        this.ElementType = (EElementType)(inIndex + 1);
        this.gameObject.SetActive(true);
        this.icons[inIndex].gameObject.SetActive(true);
    }
}