using System.Collections;
using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

[RequireComponent(typeof(Slider))]
public class UISlider : MonoBehaviour
{
    [ReadOnly]
    public float Value;

    [SerializeField] private bool shouldHideValueDisplay;
    [SerializeField] private float delayToHideValueDisplay;

    [Space()]
    [SerializeField] private GameObject valueDisplay;
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private TextMeshProUGUI minValueText;
    [SerializeField] private TextMeshProUGUI maxValueText;

    [Space]
    [SerializeField] private Replacement[] replacements;

    public Slider Slider => this.slider;
    private Slider slider;

    [Serializable]
    private struct Replacement
    {
        public int SliderValue;
        public int ConvertToValue;
        public string DisplayValue;
    }

    private void OnValidate()
    {
        Awake();
    }

    private void Awake()
    {
        this.slider = this.GetComponent<Slider>();

        if (this.slider.wholeNumbers)
        {
            this.minValueText.text = this.slider.minValue.ToString();
            this.maxValueText.text = this.slider.maxValue.ToString();
        }
        else
        {
            this.minValueText.text = this.slider.minValue.ToString("F2");
            this.maxValueText.text = this.slider.maxValue.ToString("F2");
        }

        if (this.replacements.Length > 0)
        {
            foreach (var replacement in this.replacements)
            {
                if (this.slider.minValue == replacement.SliderValue)
                {
                    if (!string.IsNullOrEmpty(replacement.DisplayValue))
                        this.minValueText.text = replacement.DisplayValue;
                    else if (this.slider.wholeNumbers)
                        this.minValueText.text = this.slider.minValue.ToString();
                    else
                        this.minValueText.text = this.slider.minValue.ToString("F2");
                }
                else if (this.slider.maxValue == replacement.SliderValue)
                {
                    if (!string.IsNullOrEmpty(replacement.DisplayValue))
                        this.maxValueText.text = replacement.DisplayValue;
                    else if (this.slider.wholeNumbers)
                        this.maxValueText.text = this.slider.maxValue.ToString();
                    else
                        this.maxValueText.text = this.slider.maxValue.ToString("F2");
                }
            }
        }

        if (this.valueDisplay != null)
            this.valueDisplay.SetActive(!this.shouldHideValueDisplay);
    }

    public void SetValue(float inValue)
    {
        this.Value = inValue;
        Refresh();
    }

    public void SetValue(int inValue)
    {
        this.Value = inValue;
        Refresh();
    }

    public void Refresh()
    {
        bool hasChanged = this.slider.value != this.Value;
        this.slider.value = this.Value;

        if (!hasChanged)
            HandleSliderValueChange(this.Value);
    }

    public void HandleSliderValueChange(float inValue)
    {
        if (this.shouldHideValueDisplay)
        {
            if (this.valueDisplay != null)
                this.valueDisplay.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(HideValueDisplay());
        }

        this.Value = inValue;

        foreach (var replacement in this.replacements)
        {
            if (this.slider.value == replacement.SliderValue)
            {
                this.Value = replacement.ConvertToValue;

                if (!string.IsNullOrEmpty(replacement.DisplayValue))
                {
                    if (this.valueText != null)
                        this.valueText.text = replacement.DisplayValue;
                    return;
                }

                break;
            }             
        }

        if (this.slider.wholeNumbers)
        {
            this.Value = Mathf.RoundToInt(this.Value);

            if (this.valueText != null)
                this.valueText.text = this.Value.ToString();
        }
        else
        {
            if (this.valueText != null)
                this.valueText.text = this.Value.ToString("F2");
        }
    }

    private IEnumerator HideValueDisplay()
    {
        yield return new WaitForSeconds(this.delayToHideValueDisplay);

        if (this.valueDisplay != null)
            this.valueDisplay.SetActive(false);
    }
}