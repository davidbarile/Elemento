using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIMultiplier : UICardDecorationBase
{
    [SerializeField] private Image bgFrame;
    [SerializeField] private TextMeshProUGUI text;

    public void SetText(int inValue, bool inShouldAddX = true)
    {
        this.text.text = inShouldAddX ? $"x{inValue}" : inValue.ToString();
    }

    public void SetTextColor(Color inColor)
    {
        this.text.color = inColor;
    }

    public void SetBgColor(Color inColor)
    {
        this.bgFrame.color = inColor;
    }
}
