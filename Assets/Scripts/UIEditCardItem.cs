using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIEditCardItem : MonoBehaviour
{
    public Action<int> OnDeckCountChange;
    public Transform CardHolder;
    public Card Card { get; set; }

    [Space]
    [SerializeField] private GameObject dimOverlay;
    [SerializeField] private GameObject countDisplay;
    [SerializeField] private TextMeshProUGUI countText;

    [SerializeField] private Button down5Button;
    [SerializeField] private Button down1Button;
    [SerializeField] private Button up1Button;
    [SerializeField] private Button up5Button;

    private int maxCount = 20;
    public int Count { get; private set; }

    public void HandleChangeCountPress(int inValue)
    {
        SetCardCount(this.Count + inValue, true);
    }

    public void SetCardCount(int inValue, bool inShouldCallChangeEvent = false)
    {
        var oldCardCount = this.Count;

        this.Count = Mathf.Clamp(inValue, 0, this.maxCount);

        this.down5Button.interactable = this.Count > 0;
        this.down1Button.interactable = this.Count > 0;
        this.up1Button.interactable = this.Count < this.maxCount;
        this.up5Button.interactable = this.Count < this.maxCount;

        this.countDisplay.SetActive(this.Count > 0);
        this.dimOverlay.SetActive(this.Count == 0);

        this.countText.text = this.Count.ToString();

        if(inShouldCallChangeEvent)
        {
            var delta = this.Count - oldCardCount;
            this.OnDeckCountChange?.Invoke(delta);
        }
    }
}