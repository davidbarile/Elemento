using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Leguar.TotalJSON;
using TMPro;
using Sirenix.OdinInspector;
using static UIDeckEditor;

public class UIImportDeckPopup : UIPanelBase
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshProUGUI outputText;

    [ReadOnly]
    [SerializeField] private ExportItem exportItem;

    public override void Show()
    {
        this.inputField.text = string.Empty;
        ClearOutputText();
        this.exportItem = new ExportItem();

        base.Show();
    }

    public void HandleSubmitButtonPress()
    {
        ClearOutputText();

        if (this.inputField.text.Length <= 0) return;

        try
        {
            var deserializeSettings = new DeserializeSettings()
            {
                RequireAllFieldsArePopulated = false
            };

            JSON jsonObject = JSON.ParseString(this.inputField.text);
            this.exportItem = jsonObject.Deserialize<ExportItem>(deserializeSettings);

            var deck = Deck.Clone(CardsManager.IN.DefaultDeck, 0);

            deck.Name = this.exportItem.Name;

            int counter = 0;
            foreach (var kvp in PlayerData.Data.DecksDict)
            {
                if (kvp.Value.Name.Contains(deck.Name))
                    ++counter;
            }

            if (counter > 0)
                deck.Name = $"{deck.Name} ({counter})";

            deck.CardCounts = this.exportItem.Counts;

            UIDeckListPanel.IN.SpawnItem(deck);

            PlayerData.Data.DecksDict.Add(deck.ID, deck);

            PlayerData.Data.IsDirty = true;

            Hide();
        }
        catch
        {
            this.outputText.text = "Invalid JSON Format!";
            Invoke(nameof(ClearOutputText), 3);
        }
    }

    private void ClearOutputText()
    {
        this.outputText.text = string.Empty;
    }
}