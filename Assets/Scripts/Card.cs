using System;
using CardInstanceData = Elemento.Domain.CardInstanceData;
using JoshH.UI;
using Lean.Pool;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using static CardData;
using static SkinManager;

public class Card : MonoBehaviour
{
    public CardData Data;
    public string Id { get; private set; }

    public static Action<int> OnLightningDamageChanged;
    public static Action<int> OnFreezeHealthChanged;
    public static Action<int> OnZoneHealthChanged;

    public enum ECardType
    {
        Points,
        Multiplier,
        Bomb,
        Shield,
        ComboBombPoints,
        ComboBombMultiplier,
        ComboBombShield,
        Heal,
        Freeze,
        Lightning,
        None
    }

    [Space]
    public ECardType CardType;

    public Transform Center => this.bgFrame.transform;

    [Space]
    [SerializeField] private Image bgFrame;
    [SerializeField] private UIGradient bgGradient;

    [Header("Combo Cards only")]
    [SerializeField] private Image bottomBgFrame;
    [SerializeField] private UIGradient topGradient;
    [SerializeField] private UIGradient bottomGradient;

    [Space]
    [SerializeField] private UIElementIcon[] elementIcons;
    [SerializeField] private UIPoints[] points;
    [SerializeField] private UIMultiplier[] multipliers;
    [Space]
    [SerializeField] private UICardDecorationBase[] bombDecorations;
    [SerializeField] private UIElementIcon[] wildElementIcons;

    [Space]
    [SerializeField] private Color[] pointsBgColors;
    [SerializeField] private Color[] multBgColors;
    [SerializeField] private Color[] bombBgColors;
    [SerializeField] private Color[] bombBgGradient1Colors;
    [SerializeField] private Color[] bombBgGradient2Colors;

    [Space]
    [SerializeField] private Color[] shieldBgColors;
    [SerializeField] private Color[] shieldBgGradient1Colors;
    [SerializeField] private Color[] shieldBgGradient2Colors;

    [Space]
    public UICardFlip CardFlip;
    public UIDraggable Draggable;

    [Space]
    [ReadOnly]
    public GameObject CardBack;
    public Transform CardBackParent;

    private bool isDestroyed;

    public void Configure(CardData inCardData, string inId = null)
    {
        this.Data = inCardData;
        this.Id = inId;

        if (this.Data.WildLightning > 0)
        {
            Card.OnLightningDamageChanged += UpdateLightningDamage;
            Card.OnLightningDamageChanged?.Invoke(SettingsManager.IN.LightningDamage);
        }

        if (this.Data.WildFreeze > 0)
        {
            Card.OnFreezeHealthChanged += UpdateFreezeHealth;
            Card.OnFreezeHealthChanged?.Invoke(SettingsManager.IN.FrozenZoneHealth);
        }

        if (this.Data.WildZoneHeal > 0)
        {
            Card.OnZoneHealthChanged += UpdateZoneHealth;
            Card.OnZoneHealthChanged?.Invoke(SettingsManager.IN.TutorialSafeElementZoneHealth);
        }

        if (this.Data.IsPowerCard) return;

        SkinManager.OnSkinChanged += SetShieldMultBgColors;
        SetShieldMultBgColors(new SkinData(), null);

        bool isCombo = this.bottomBgFrame != null;

        HideElementIcons();

        int index = 0;
        var wildPoints = inCardData.WildPoints;

        if (wildPoints > 0)
        {
            var pointsItem = this.points[0];

            pointsItem.ElementType = EElementType.Wild;

            pointsItem.SetText(wildPoints);
        }
        else
        {
            for (int i = 0; i < inCardData.Points.Length; i++)
            {
                var pointsData = inCardData.Points[i];

                bool shouldShow = pointsData > 0;

                if (shouldShow)
                {
                    int pointsIndex = isCombo ? 0 : index;

                    var pointsItem = this.points[pointsIndex];
                    pointsItem.ElementType = (EElementType)(i + 1);

                    pointsItem.SetText(pointsData);

                    int pointsElementIndex = isCombo ? 1 : index;
                    var elementIcon = this.elementIcons[pointsElementIndex];
                    elementIcon.SetIconVisible(i);
                    ++index;
                }
            }
        }

        int biggestMult = 0;
        var wildMult = inCardData.WildMultiplier;

        if (wildMult > 0)
        {
            biggestMult = wildMult;
            int multsIndex = isCombo ? 1 : 0;
            var multsItem = this.multipliers[multsIndex];

            multsItem.SetText(wildMult);

            var textColor = Color.white;
            if (wildMult > 1)
                textColor = this.multBgColors[wildMult - 2];
            multsItem.SetTextColor(textColor);
        }
        else
        {
            index = 0;
            for (int i = 0; i < inCardData.Multipliers.Length; i++)
            {
                var multValue = inCardData.Multipliers[i];

                bool shouldShow = multValue > 0;

                if (shouldShow)
                {
                    biggestMult = Mathf.Max(biggestMult, multValue);

                    int multsIndex = isCombo ? 1 : index;

                    var multsItem = this.multipliers[multsIndex];
                    multsItem.SetText(multValue);
                    var textColor = Color.white;
                    if (multValue > 1)
                        textColor = this.multBgColors[multValue - 2];
                    multsItem.SetTextColor(textColor);

                    var elementIcon = this.elementIcons[multsIndex];
                    elementIcon.SetIconVisible(i);
                    ++index;
                }
            }
        }

        int biggestBomb = 0;
        var wildBomb = inCardData.WildBomb;

        if (wildBomb > 0)
        {
            biggestBomb = wildBomb;
            var multsItem = this.multipliers[0];
            multsItem.SetText(-wildBomb, false);
            //var textColor = this.bombBgColors[wildBomb - 1];
            multsItem.SetTextColor(Color.white);
        }
        else
        {
            index = 0;
            for (int i = 0; i < inCardData.Bombs.Length; i++)
            {
                var bombValue = inCardData.Bombs[i];

                bool shouldShow = bombValue > 0;

                if (shouldShow)
                {
                    biggestBomb = Mathf.Max(biggestBomb, bombValue);

                    int bombsIndex = isCombo ? 0 : index;

                    var multsItem = this.multipliers[bombsIndex];
                    multsItem.SetText(-bombValue, false);
                    //var textColor = this.bombBgColors[bombValue - 1];
                    multsItem.SetTextColor(Color.white);

                    var elementIcon = this.elementIcons[bombsIndex];
                    elementIcon.SetIconVisible(i);
                    ++index;
                }
            }
        }

        int biggestShield = 0;
        var wildShield = inCardData.WildShield;

        if (wildShield > 0)
        {
            biggestShield = wildShield;
            int multsIndex = isCombo ? 1 : 0;
            var multsItem = this.multipliers[multsIndex];
            multsItem.SetText(wildShield, false);
            //var textColor = this.bombBgColors[shieldValue - 1];
            multsItem.SetTextColor(Color.white);
        }
        else
        {
            index = 0;
            for (int i = 0; i < inCardData.Shields.Length; i++)
            {
                var shieldValue = inCardData.Shields[i];
                bool shouldShow = shieldValue > 0;

                if (shouldShow)
                {
                    biggestShield = Mathf.Max(biggestShield, shieldValue);

                    int shieldsIndex = isCombo ? 1 : index;

                    var multsItem = this.multipliers[shieldsIndex];
                    multsItem.SetText(shieldValue, false);
                    //var textColor = this.bombBgColors[shieldValue - 1];
                    multsItem.SetTextColor(Color.white);

                    var elementIcon = this.elementIcons[shieldsIndex];
                    elementIcon.SetIconVisible(i);
                    ++index;
                }
            }
        }

        if (isCombo)
        {
            var bombBgColor = this.bombBgColors[biggestBomb - 1];

            this.topGradient.LinearColor1 = this.bombBgGradient1Colors[biggestBomb - 1];
            this.topGradient.LinearColor2 = this.bombBgGradient2Colors[biggestBomb - 1];

            var bottomBgColor = Color.white;

            var gradientColor1 = this.pointsBgColors[0];
            var gradientColor2 = this.pointsBgColors[1];

            if (biggestMult > 1)
            {
                bottomBgColor = this.multBgColors[biggestMult - 2];

                gradientColor1 = Color.Lerp(bottomBgColor, Color.white, .5f);
                gradientColor2 = Color.Lerp(bottomBgColor, Color.white, .95f);
            }
            else if (biggestShield > 0)
            {
                bottomBgColor = this.shieldBgColors[biggestShield - 1];

                gradientColor1 = this.shieldBgGradient1Colors[biggestShield - 1];
                gradientColor2 = this.shieldBgGradient2Colors[biggestShield - 1];
            }

            this.bgFrame.color = bombBgColor;
            this.bgGradient.LinearColor1 = bombBgColor;
            this.bgGradient.LinearColor2 = bottomBgColor;
            this.bottomBgFrame.color = bottomBgColor;

            this.bottomGradient.LinearColor1 = gradientColor1;
            this.bottomGradient.LinearColor2 = gradientColor2;
        }
        else
        {
            var bgColor = Color.white;
            var gradientColor1 = this.pointsBgColors[0];
            var gradientColor2 = this.pointsBgColors[1];

            if (biggestBomb > 0)
            {
                bgColor = this.bombBgColors[biggestBomb - 1];
                gradientColor1 = this.bombBgGradient1Colors[biggestBomb - 1];
                gradientColor2 = this.bombBgGradient2Colors[biggestBomb - 1];
            }
            else if (biggestShield > 0)
            {
                bgColor = this.shieldBgColors[biggestShield - 1];
                gradientColor1 = this.shieldBgGradient1Colors[biggestShield - 1];
                gradientColor2 = this.shieldBgGradient2Colors[biggestShield - 1];
            }
            else if (biggestMult > 1)
            {
                bgColor = this.multBgColors[biggestMult - 2];
                gradientColor1 = Color.Lerp(bgColor, Color.white, .5f);
                gradientColor2 = Color.Lerp(bgColor, Color.white, .95f);
            }

            this.bgFrame.color = bgColor;
            this.bgGradient.LinearColor1 = gradientColor1;
            this.bgGradient.LinearColor2 = gradientColor2;
        }

        void HideElementIcons()
        {
            foreach (var icons in this.elementIcons)
            {
                icons.HideIcons();
            }
        }
    }

    public CardInstanceData CreateInstanceData()
    {
        return new CardInstanceData
        {
            Id = this.Id,
            DefinitionId = this.Data.Index,
            DefinitionName = this.Data.Name,
            IsFaceUp = this.CardFlip.IsFaceUp,
            IsPowerCard = this.Data.IsPowerCard,
            WildPoints = this.Data.WildPoints,
            WildMultiplier = this.Data.WildMultiplier,
            WildBomb = this.Data.WildBomb,
            WildShield = this.Data.WildShield,
            WildZoneHeal = this.Data.WildZoneHeal,
            WildFreeze = this.Data.WildFreeze,
            WildLightning = this.Data.WildLightning,
            Points = (int[])this.Data.Points.Clone(),
            Multipliers = (int[])this.Data.Multipliers.Clone(),
            Bombs = (int[])this.Data.Bombs.Clone(),
            Shields = (int[])this.Data.Shields.Clone()
        };
    }

    public void SetShieldMultBgColors(SkinData inSkinData, Sprite inCardBackSprite)
    {
        if (this.Data == null) return;

        bool isCombo = this.bottomBgFrame != null;

        var index = 0;
        for (int i = 0; i < this.Data.Shields.Length; i++)
        {
            var shieldValue = this.Data.Shields[i];
            bool shouldShow = shieldValue > 0;

            if (shouldShow)
            {
                int shieldsIndex = isCombo ? 1 : index;

                var multsItem = this.multipliers[shieldsIndex];

                var shieldMultBgColor = SkinManager.IN.SelectedSkinData.ShieldMultBgColors[i];
                multsItem.SetBgColor(shieldMultBgColor);
                ++index;
            }
        }
    }

    public void ResetDimmedDecorations()
    {
        foreach (var points in this.points)
        {
            points.SetDimmedState(false);
            points.SetWildElementsDecorationsDimState(EElementType.None);
        }

        foreach (var mult in this.multipliers)
        {
            mult.SetDimmedState(false);
            mult.SetWildElementsDecorationsDimState(EElementType.None);
        }

        foreach (var elementIcon in this.elementIcons)
        {
            elementIcon.SetDimmedState(false);
            elementIcon.SetWildElementsDecorationsDimState(EElementType.None);
        }

        foreach (var wild in this.wildElementIcons)
        {
            wild.SetWildElementsDecorationsDimState(EElementType.None);
        }

        foreach (var bomb in this.bombDecorations)
        {
            bomb.SetDimmedState(false);
        }
    }

    public void SetDimmedDecorations(EElementType inElementType)
    {
        if (inElementType == EElementType.None || this.Data.IsBomb) return;

        foreach (var wild in this.wildElementIcons)
        {
            wild.SetWildElementsDecorationsDimState(inElementType);
        }

        bool isComboCard = this.bombDecorations.Length > 0;

        if (isComboCard)
        {
            if (this.elementIcons.Length > 0)
                this.elementIcons[0].SetDimmedState(true);

            if (this.multipliers.Length > 0)
                this.multipliers[0].SetDimmedState(true);

            foreach (var bomb in this.bombDecorations)
            {
                bomb.SetDimmedState(true);
            }
        }
        else
        {
            for (int i = 0; i < this.elementIcons.Length; i++)
            {
                var elementIcon = this.elementIcons[i];
                var shouldDim = !elementIcon.ElementType.Equals(inElementType);
                elementIcon.SetDimmedState(shouldDim);
                elementIcon.SetWildElementsDecorationsDimState(inElementType);

                if (i < this.points.Length)
                {
                    var points = this.points[i];
                    points.SetDimmedState(shouldDim);
                    points.SetWildElementsDecorationsDimState(inElementType);
                }

                if (i < this.multipliers.Length)
                {
                    var mult = this.multipliers[i];
                    mult.SetDimmedState(shouldDim);
                    mult.SetWildElementsDecorationsDimState(inElementType);
                }
            }
        }
    }

    private void UpdateLightningDamage(int inLightningDamage)
    {
        this.Data.WildLightning = inLightningDamage;
        this.multipliers[0].SetText(inLightningDamage, false);
    }

    private void UpdateFreezeHealth(int inFreezeHealth)
    {
        this.Data.WildFreeze = inFreezeHealth;
        this.multipliers[0].SetText(inFreezeHealth, false);
    }

    private void UpdateZoneHealth(int inZoneHealth)
    {
        this.Data.WildZoneHeal = inZoneHealth;
        this.multipliers[0].SetText(inZoneHealth, false);
    }

    private void OnDestroy()
    {
        if (this.isDestroyed) return;

        if (!SaveManager.IsApplicationQuiting)
            Debug.Log($"Card.OnDestroy = {this.name}");
    }

    public void DestroySelf()
    {
        if (this.CardBack != null)
            LeanPool.Despawn(this.CardBack);

        CardsManager.IN.SpawnedCards.Remove(this);

        if (this.Data.IsPowerCard)
            CardsManager.IN.PowerCardDatas.Add(this.Data);
        else
            CardsManager.IN.ShuffledCardDatas.Add(this.Data);

        Card.OnLightningDamageChanged -= UpdateLightningDamage;
        Card.OnFreezeHealthChanged -= UpdateFreezeHealth;
        Card.OnZoneHealthChanged -= UpdateZoneHealth;
        SkinManager.OnSkinChanged -= SetShieldMultBgColors;

        this.isDestroyed = true;

        Destroy(this.gameObject);
    }
}
