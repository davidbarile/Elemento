using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIWrapupItem : MonoBehaviour
{
    public TextMeshProUGUI PlayerText;
    public UIPoints[] PointsDisplays;
    public UITotalPointsDisplay TotalPointsDisplay;
    [SerializeField] private CanvasGroup[] pointsCanvasGroups;
    [SerializeField] private GameObject[] zoneDestroyedDisplays;

    public void SetDestroyedState(int inIndex, bool inIsDestroyed)
    {
        this.zoneDestroyedDisplays[inIndex].SetActive(inIsDestroyed);
        this.pointsCanvasGroups[inIndex].alpha = inIsDestroyed ? .25f : 1f;

        if(inIsDestroyed)
            this.PointsDisplays[inIndex].Text.text = "0";
    }
}

public class WrapupItemData
{
    public string PlayerName;
    public int TotalPoints;
    public List<int> ElementPoints = new();
}