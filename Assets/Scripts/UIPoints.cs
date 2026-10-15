using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CardData;
using static SkinManager;

public class UIPoints : UICardDecorationBase
{
    public EElementType ElementType;

    [SerializeField] private bool shouldPlayTickSound = false;

    [SerializeField] private Image bgFrame;
    [SerializeField] private Image bgOutline;
    [SerializeField] private Image[] fills;
    [SerializeField] private bool shouldReverseFill = true;
    public TextMeshProUGUI Text => this.text;
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private GameObject godRays;

    [Header("Unused --------")]
    [SerializeField] private Color[] defaultBgColors;

    private int pointsValue = -1;
    private int maxValue = -1;
    private int destValue = -1;

    private void Start()
    {
        SkinManager.OnSkinChanged += SetBackgroundColor;
        SetBackgroundColor(new SkinData(), null);
    }

    public void SetText(int inValue)
    {
        SetText(inValue, -1);
    }

    public void SetText(int inValue, int inMaxValue)
    {
        this.text.text = inValue.ToString();

        this.pointsValue = inValue;

        if (inMaxValue > 0)
        {
            this.maxValue = inMaxValue;
            var percent = (float)inValue / this.maxValue;
            SetFillValue(percent);
        }
    }

    public void SetTextInstantly(int inValue, int inMaxValue)
    {
        StopAllCoroutines();
        this.destValue = -1;
        SetText(inValue, inMaxValue);
    }

    public void ResetToZero()
    {
        this.pointsValue = 0;
        this.text.text = this.pointsValue.ToString();
        SetFillValue(0);
    }

    public void SetBackgroundColor(SkinData inSkinData, Sprite inCardBackSprite)
    {
        if (this.ElementType == EElementType.None) return;

        var index = (int)this.ElementType - 1;

        this.bgFrame.color = SkinManager.IN.SelectedSkinData.PointsBgFrameColors[index];
        this.bgOutline.color = SkinManager.IN.SelectedSkinData.PointsBgOutlineColors[index];
    }

    private void SetFillValue(float inPercent)
    {
        float value = this.shouldReverseFill ? 1 - inPercent : inPercent;

        foreach (var fill in this.fills)
        {
            fill.fillAmount = value;
        }

        if (this.godRays != null)
            this.godRays.SetActive(inPercent >= 1);
    }

    private void OnDestroy()
    {
        SkinManager.OnSkinChanged -= SetBackgroundColor;
    }

    public void TickToValue(int inDestValue, int inMaxValue)
    {
        this.maxValue = inMaxValue;

        if (!this.gameObject.activeInHierarchy || BoardStateManager.IsSettingState)
        {
            SetTextInstantly(inDestValue, inMaxValue);
            return;
        }

        if (inDestValue == this.pointsValue) return;

        StopAllCoroutines();
        StartCoroutine(TickToValuCo(inDestValue));
    }

    private IEnumerator TickToValuCo(int inDestValue)
    {
        float delta = inDestValue - this.pointsValue;

        //if interrupted, set value to correct start value
        if(this.destValue > -1)
            SetText(this.destValue, this.maxValue);

        this.destValue = inDestValue;

        float changingScore = this.pointsValue;
        this.pointsValue = inDestValue;

        var maxScoreTickDuration = 1;
        float scoreTickDuration = Mathf.Min(maxScoreTickDuration, inDestValue / 500);
        scoreTickDuration = Mathf.Max(scoreTickDuration, .25f);//min time 1/4 second

        float endTime = Time.time + scoreTickDuration;
        float tickSpeed = .03f;//lower is faster

        float ratio = scoreTickDuration / tickSpeed;
        float incrementAmount = delta / ratio;

        int counter = 0;
        int lastPlayedSoundAmount = -1;

        while (Time.time < endTime)
        {
            if (delta > 0)
                ++counter;
            else
                --counter;

            changingScore += incrementAmount;
            int roundedAmount = delta > 0 ? Mathf.FloorToInt(changingScore) : Mathf.CeilToInt(changingScore);

            if (this.shouldPlayTickSound && counter % 2 == 0)
            {
                if(lastPlayedSoundAmount != roundedAmount)
                {
                    lastPlayedSoundAmount = roundedAmount;
                    float pitch = 3f + ((changingScore / this.maxValue) * 2f);

                    AudioManager.IN.PlayClip(AudioManager.IN.IncrementCounterClip, .1f, pitch);
                }
            }

            SetText(roundedAmount, this.maxValue);
            yield return new WaitForSeconds(tickSpeed);
        }

        SetText(this.destValue, this.maxValue);

        this.destValue = -1;

        yield break;
    }
}
