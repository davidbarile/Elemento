using UnityEngine;
using static CardData;

public class UIDeckFilterToggle : UISettingsTab
{
    public EFilterFlags FilterTags;

    private void Update()
    {
        //hack to fix random bug that causes the scale to go to zero when clicked
        this.transform.localScale = Vector3.one;
    }
}