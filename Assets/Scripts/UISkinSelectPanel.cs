using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UISkinSelectPanel : MonoBehaviour
{
    public GameObject ApplySkinButton;

    [SerializeField] private TMP_Dropdown skinSelectDropdown;
    [SerializeField] private TMP_Dropdown cardBackSelectDropdown;

    [Space]
    [SerializeField] private UISkinSwapItem[] sampleSkinSwapItems;

    private int skinIndex;
    private int cardBackIndex;

    public bool IsDirty;

    public void SetSliderValuesToSettings()
    {
        this.skinIndex = PlayerData.Data.SkinTypeIndex;
        this.cardBackIndex = PlayerData.Data.CardBackIndex;

        this.skinSelectDropdown.value = PlayerData.Data.SkinTypeIndex;
        this.skinSelectDropdown.RefreshShownValue();

        this.cardBackSelectDropdown.value = PlayerData.Data.CardBackIndex;
        this.cardBackSelectDropdown.RefreshShownValue();
    }

    public void HandleSkinSelectDropdownChange(Int32 inValue)
    {
        this.IsDirty = true;

        this.skinIndex = inValue;

        RefreshSkinSamples();
    }

    public void HandleCardBackStyleDropdownChange(Int32 inValue)
    {
        this.IsDirty = true;

        this.cardBackIndex = inValue;

        RefreshSkinSamples();
    }

    private void RefreshSkinSamples()
    {
        var skinData = SkinManager.IN.GetSkinDataAtIndex(this.skinIndex);
        var cardBackSprite = SkinManager.IN.GetCardBackAtIndex(this.cardBackIndex);

        foreach (var item in this.sampleSkinSwapItems)
        {
            item.Refresh(skinData, cardBackSprite);
        }
    }

    public void Reset()
    {
        this.IsDirty = false;

        this.skinIndex = 0;
        this.cardBackIndex = 0;

        HandleApplyButtonPress();
    }

    public void HandleApplyButtonPress()
    {
        this.IsDirty = false;

        SkinManager.IN.ChangeSkin(this.skinIndex);
        SkinManager.IN.ChangeCardBackStyle(this.cardBackIndex);

        PlayerData.Data.SkinTypeIndex = this.skinIndex;
        PlayerData.Data.CardBackIndex = this.cardBackIndex;

        UISettingsPanel.IN.IsDirty = true;
    }

    public void HandleDisabledApplyButtonPress()
    {
        string titleText = "Pay Up, Bro!";
        string messageText = $"If you want to use this group of skins in your game, you gotta pay the piper!";
        UIConfirmPanel.IN.Show(titleText, messageText);
    }

    public void RefreshDropdowns(int inLanguageIndex)
    {
        //Skins
        this.skinSelectDropdown.ClearOptions();
        var skinOptions = new List<string>();
        foreach (var item in LOC.IN.DropdownOptions_SkinType)
        {
            skinOptions.Add(item.Options[inLanguageIndex]);
        }
        this.skinSelectDropdown.AddOptions(skinOptions);
        this.skinSelectDropdown.RefreshShownValue();

        //Card Backs
        this.cardBackSelectDropdown.ClearOptions();
        var cardBackOptions = new List<string>();
        foreach (var item in LOC.IN.DropdownOptions_CardBackStyle)
        {
            cardBackOptions.Add(item.Options[inLanguageIndex]);
        }
        this.cardBackSelectDropdown.AddOptions(cardBackOptions);
        this.cardBackSelectDropdown.RefreshShownValue();

        SetSliderValuesToSettings();
    }
}