using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using static CardData;

public class UITotalPointsDisplay : MonoBehaviour
{
    [SerializeField] private UIPoints totalPoints;
    [SerializeField] private Image[] elementIcons;

    [SerializeField] private CanvasGroup tooltipCanvasGroup;
    [SerializeField] private TMP_Text tooltipText;

    private bool isTooltipVisible;

    private void Start()
    {
        this.tooltipCanvasGroup.gameObject.SetActive(false);
        this.tooltipCanvasGroup.alpha = 0f;
    }

    public void TickToTotalPoints(int inValue, bool inShouldHideIfZero = true)
    {
        if(inShouldHideIfZero)
            this.gameObject.SetActive(inValue > 0);

        this.totalPoints.TickToValue(inValue, SettingsManager.IN.TotalPointsRequiredToWin);
        this.tooltipText.text = $"<size=80>{inValue}/{SettingsManager.IN.TotalPointsRequiredToWin}</size>\nto win";
    }

    public void SetTotalPoints(int inValue)
    {
        this.totalPoints.SetText(inValue, SettingsManager.IN.TotalPointsRequiredToWin);
        this.tooltipText.text = $"<size=80>{inValue}/{SettingsManager.IN.TotalPointsRequiredToWin}</size>\nto win";
    }

    public void SetElementVisible(EElementType inElementType, int inPoints)
    {
        var elementIcon = this.elementIcons[(int)inElementType - 1];
        elementIcon.gameObject.SetActive(inPoints <= 0);
    }

    //called by the tooltip trigger component on the parent object
    public void ShowTooltip(bool inShouldAutoHide)
    {
        if (this.isTooltipVisible)
            return;
            
        this.isTooltipVisible = true;
        this.tooltipCanvasGroup.gameObject.SetActive(true);

        this.tooltipCanvasGroup.DOFade(1f, 0.1f).onComplete = () =>
        {
            if (inShouldAutoHide)
            {
                DOVirtual.DelayedCall(1f, () =>
                {
                    HideTooltip();
                });
            }
        };
    }
    
    //called by the tooltip trigger component on the parent object
    public void HideTooltip()
    {
        this.tooltipCanvasGroup.DOFade(0f, 0.3f).onComplete = () =>
        {
            this.tooltipCanvasGroup.gameObject.SetActive(false);
            this.isTooltipVisible = false;
        };
    }
}