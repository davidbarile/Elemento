using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CardData;
using static SkinManager;

public class UISkinSwapItem : MonoBehaviour
{
    [SerializeField] private EElementType elementType;
    [Space]
    [SerializeField] private Image image;
    [SerializeField] private TextMeshProUGUI text;
    [Space]
    [SerializeField] private bool isCardBack;

    private string locKey;

    private void Start()
    {
        SkinManager.OnSkinChanged += Refresh;

        if (this.text != null)
            LocalizationManager.OnLanguageChanged += RefreshText;

        Refresh(SkinManager.IN.SelectedSkinData, SkinManager.IN.SelectedCardBackSprite);

        Invoke(nameof(DelayedRefresh), .05f);
    }

    //hack to fix bug
    private void DelayedRefresh()
    {
        Refresh(SkinManager.IN.SelectedSkinData, SkinManager.IN.SelectedCardBackSprite);
    }

    private void OnDestroy()
    {
        SkinManager.OnSkinChanged -= Refresh;
        LocalizationManager.OnLanguageChanged -= RefreshText;
    }

    public void Refresh(SkinData inSkinData, Sprite inCardBackSprite)
    {
        if(this.isCardBack)
        {
            if (this.image != null)
                this.image.sprite = inCardBackSprite;
        }
        else if (this.elementType != EElementType.None)
        {
            int index = (int)this.elementType - 1;
            if (this.image != null)
                this.image.sprite = inSkinData.ElementSprites[index];

            if (this.text != null)
            {
                this.locKey = inSkinData.ElementNames[index];
                RefreshText(index);
            }
        }
    }

    private void RefreshText(int inLanguageIndex)
    {
        this.text.text = LOC.IN.GetElementSkinName(this.locKey);
    }
}