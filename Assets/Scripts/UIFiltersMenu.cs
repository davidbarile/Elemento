using UnityEngine;
using UnityEngine.UI;
using static CardData;

public class UIFiltersMenu : UIPanelBase
{
    [SerializeField] private bool shouldValidate;
    [Space]
    [SerializeField] private UIDeckFilterToggle[] toggles;
    [SerializeField] private UIDeckFilterToggle[] elementToggles;
    [SerializeField] private UIDeckFilterToggle[] typeToggles;
    [SerializeField] private UIDeckFilterToggle[] quantityToggles;
    [Space]
    [SerializeField] private Button showZeroCountButton;
    [SerializeField] private Button hideZeroCountButton;
    [Space]
    [SerializeField] private CanvasGroup canvasGroup;

    private bool shouldShowZeros = true;
    private bool isDirty;
    private bool showHideZerosIsDirty;

    private void OnValidate()
    {
        if (!this.shouldValidate) return;

        this.shouldValidate = false;

        if (this.toggles.Length == 0)
            this.toggles = this.GetComponentsInChildren<UIDeckFilterToggle>(false);
    }

    public override void Show()
    {
        base.Show();

        HandleShowHideZerosButtonPress(true);
        //HandleShowAllButtonPress();
    }

    private void LateUpdate()
    {
        if (!this.isDirty) return;

        this.isDirty = false;

        var filterFlags = EFilterFlags.None;

        foreach (var toggle in this.toggles)
        {
            if (toggle.IsOn)
                filterFlags += (int)toggle.FilterTags;
        }

        //Debug.Log($"UIDeckEditor.IN.FilterFlags = {UIDeckEditor.IN.FilterFlags}   filterFlags = {filterFlags}");

        if (filterFlags != UIDeckEditor.IN.FilterFlags || this.showHideZerosIsDirty)
        {
            if (this.showHideZerosIsDirty)
                this.showHideZerosIsDirty = false;

            this.canvasGroup.interactable = false;
            UIDeckEditor.IN.ApplyShowHideFilters(filterFlags, this.shouldShowZeros);
            this.canvasGroup.interactable = true;
        }
    }

    private int GetNumActiveToggles(UIDeckFilterToggle[] inFilterCategory)
    {
        int numActive = 0;

        foreach (var toggle in inFilterCategory)
        {
            if (toggle.IsOn)
                ++numActive;
        }

        return numActive;
    }

    //private List<UIDeckFilterToggle> GetActiveToggles(UIDeckFilterToggle[] inFilterCategory)
    //{
    //    List<UIDeckFilterToggle> activeToggles = new();

    //    foreach (var toggle in inFilterCategory)
    //    {
    //        if (toggle.IsOn)
    //            activeToggles.Add(toggle);
    //    }

    //    return activeToggles;
    //}

    public void HandleToggleChanged(UIDeckFilterToggle inToggle)
    {
        if (inToggle.IsOn)
        {
            inToggle.SetState(true, false);
            this.isDirty = true;
        }
        else
        {
            int numElementTogglesActive = GetNumActiveToggles(this.elementToggles);
            int numTypeTogglesActive = GetNumActiveToggles(this.typeToggles);
            int numQuantityTogglesActive = GetNumActiveToggles(this.quantityToggles);

            bool atLeastOneShowingInCategory = numElementTogglesActive > 0 && numTypeTogglesActive > 0 && numQuantityTogglesActive > 0;

            if (atLeastOneShowingInCategory)
            {
                inToggle.SetState(false, false);
                this.isDirty = true;
            }
        }
    }

    public void HandleShowAllButtonPress()
    {
        foreach (var toggle in this.toggles)
        {
            toggle.SetState(true);
        }
    }

    public void HandleHideAllButtonPress()
    {
        foreach (var toggle in this.toggles)
        {
            toggle.SetState(false);
        }

        this.elementToggles[0].SetState(true);
        this.typeToggles[0].SetState(true);
        this.quantityToggles[0].SetState(true);
    }

    public void HandleShowHideZerosButtonPress(bool inShouldShow)
    {
        ShowHideZeros(inShouldShow);

        this.isDirty = true;
        this.showHideZerosIsDirty = true;
    }

    private void ShowHideZeros(bool inShouldShow)
    {
        this.shouldShowZeros = inShouldShow;

        this.showZeroCountButton.gameObject.SetActive(!inShouldShow);
        this.hideZeroCountButton.gameObject.SetActive(inShouldShow);
    }

    public void SetAllVisible()
    {
        ShowHideZeros(true);
        foreach (var toggle in this.toggles)
        {
            toggle.SetState(true, false);
        }

        UIDeckEditor.IN.FilterFlags = EFilterFlags.None;
    }
}