using TMPro;
using UnityEngine;
using static UIDeckEditor;

public class UIDeckExportedPopup : UIPanelBase
{
    [SerializeField] private TextMeshProUGUI deckNameText;
    [SerializeField] private TextMeshProUGUI cardsCountText;
    [SerializeField] private TextMeshProUGUI viewButtonText;
    [SerializeField] private TextMeshProUGUI jsonDisplayText;
    [SerializeField] private GameObject jsonDisplay;

    [SerializeField] private string emailAddress;
    private string emailSubject;
    private string emailBody;

    public void Show(ExportItem inExportItem, string inJsonString)
    {
        this.deckNameText.text = inExportItem.Name;
        this.cardsCountText.text = inExportItem.NumCards.ToString();
        this.jsonDisplayText.text = inJsonString;

        this.emailSubject = $"Exported Deck: {inExportItem.Name} ({inExportItem.NumCards} cards)";
        this.emailBody = $"JSON:\n\n{inJsonString}";

#if UNITY_IOS
        this.emailSubject = WWW.EscapeURL(this.emailSubject);
        this.emailBody = WWW.EscapeURL(this.emailBody);
#endif

        SetViewJsonVisible(false);

        base.Show();
    }

    private void SetViewJsonVisible(bool inIsVisible)
    {
        this.viewButtonText.text = inIsVisible ? LOC.IN.Hide[LOC.LI] : LOC.IN.View[LOC.LI];// "Hide" : "View";
        this.jsonDisplay.SetActive(inIsVisible);
    }

    public void HandleEmailButtonPress()
    {
        Application.OpenURL($"mailto:{this.emailAddress}?subject={this.emailSubject}&body={this.emailBody}");
    }

    public void HandleViewButtonPress()
    {
        SetViewJsonVisible(!this.jsonDisplay.activeSelf);
    }
}