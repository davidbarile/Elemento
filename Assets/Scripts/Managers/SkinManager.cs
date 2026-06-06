using System;
using UnityEngine;

public class SkinManager : MonoBehaviour
{
    public static SkinManager IN;
    public static Action<SkinData, Sprite> OnSkinChanged;

    public enum ESkinType
    {
        Elements,
        SkinB,
        SkinC,
        SkinD
    }

    [Space]
    [SerializeField] private SkinData[] skinDatas;
    [SerializeField] private Sprite[] cardBacks;

    public SkinData SelectedSkinData => this.skinDatas[PlayerData.Data.SkinTypeIndex];
    public Sprite SelectedCardBackSprite => this.cardBacks[PlayerData.Data.CardBackIndex];

    [Serializable]
    public struct SkinData
    {
        [Header("Sync with LocManager.ElementKeys")]
        public string[] ElementNames;
        public Sprite[] ElementSprites;

        public Color[] PointsBgFrameColors;
        public Color[] PointsBgOutlineColors;
        public Color[] ShieldMultBgColors;
    }

    public void ChangeSkin(int inSkinIndex)
    {
        PlayerData.Data.SkinTypeIndex = inSkinIndex;
        OnSkinChanged?.Invoke(this.SelectedSkinData, this.SelectedCardBackSprite);
    }

    public void ChangeCardBackStyle(int inCardBackIndex)
    {
        PlayerData.Data.CardBackIndex = inCardBackIndex;
        OnSkinChanged?.Invoke(this.SelectedSkinData, this.SelectedCardBackSprite);
    }

    public SkinData GetSkinDataAtIndex(int inIndex)
    {
        return this.skinDatas[inIndex];
    }

    public Sprite GetCardBackAtIndex(int inIndex)
    {
        return this.cardBacks[inIndex];
    }
}