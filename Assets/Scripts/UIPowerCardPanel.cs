using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Card;

public class UIPowerCardPanel : UIPanelBase
{
    public static UIPowerCardPanel IN;//singleton

    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private TextMeshProUGUI confirmButtonText;

    [Space]
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button confirmButton;

    [Space]
    [SerializeField] private GameObject[] selectedHighlights;

    [Space]
    [SerializeField] private Card[] cards;
    [SerializeField] private CardConfig[] cardConfigs;

    private Action onConfirm;
    private Action onCancel;

    private int selectedPowerCardIndex = -1;

    private void Start()
    {
        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            var config = cardConfigs[i];

            var cardData = CardConfig.CreateCardData(config, 0);

            card.Configure(cardData);
        }
    }

    public void Show(Action inOnConfirm, Action inOnCancel = null, float inDelayToAutoHide = -1)
    {
        this.onConfirm = inOnConfirm;
        this.onCancel = inOnCancel;

        bool shouldShowBothButtons = inOnConfirm != null;

        this.cancelButton.gameObject.SetActive(shouldShowBothButtons);
        this.confirmButton.gameObject.SetActive(shouldShowBothButtons);

        ShowPowerCardSelected(-1);

        Show();

        if (inDelayToAutoHide > 0)
            Invoke(nameof(HandleConfirmButtonPress), inDelayToAutoHide);
    }

    public void HandleHealButtonPress()
    {
        if (UITutorialPanel.IsTutorialMode && UITutorialPanel.IN.TutorialStateIndex != 17) return;

        ShowPowerCardSelected(0);

        if (UITutorialPanel.IsTutorialMode)
        {
            UITutorialPanel.OnHealthPowerCardButtonPressed?.Invoke();
        }
    }

    public void HandleFreezeButtonPress()
    {
        if (UITutorialPanel.IsTutorialMode)
        {
            AudioManager.IN.PlayRejectSound(AudioManager.ERejectReason.TutorialBlockedCell);
            return;
        }

        ShowPowerCardSelected(1);
    }

    public void HandleLightningButtonPress()
    {
        if (UITutorialPanel.IsTutorialMode)
        {
            AudioManager.IN.PlayRejectSound(AudioManager.ERejectReason.TutorialBlockedCell);
            return;
        }

        ShowPowerCardSelected(2);
    }

    public void HandleCancelButtonPress()
    {
        if (UITutorialPanel.IsTutorialMode) return;

        Hide();

        this.onCancel?.Invoke();
        this.onCancel = null;
    }

    public void HandleConfirmButtonPress()
    {

        if (UITutorialPanel.IsTutorialMode && UITutorialPanel.IN.TutorialStateIndex != 18) return;

        Hide();

        GameManager.IN.SetUndoCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.ChoosePowerCard);

        this.onConfirm?.Invoke();
        this.onConfirm = null;
    }

    private void ShowPowerCardSelected(int inIndex)
    {
        //make clicking a second time deselect power card and return to random
        if (inIndex == this.selectedPowerCardIndex)
        {
            inIndex = -1;
        }

        this.selectedPowerCardIndex = inIndex;

        for (int i = 0; i < this.selectedHighlights.Length; i++)
        {
            var button = this.selectedHighlights[i];
            button.SetActive(inIndex == i);
        }

        switch (inIndex)
        {
            case -1:
                CardsManager.IN.PowerCardOverride = ECardType.None;
                //Click a card to choose a <color=#B250B1>Power Card</color>, or don't click any to randomly select between <color=#B250B1>Freeze</color>, <color=#B250B1>Heal</color> and <color=#B250B1>Lightning</color> Power Cards.
                this.messageText.text = LOC.IN.RandomDesc[LOC.LI];
                this.confirmButtonText.text = LOC.IN.Random[LOC.LI];//"Random";
                break;

            case 0:
                CardsManager.IN.PowerCardOverride = ECardType.Heal;
                //<color=#B250B1>Heal Card</color> restores a damaged or destroyed Element Zone's Health to full.
                this.messageText.text = LOC.IN.HealDesc[LOC.LI];
                this.confirmButtonText.text = LOC.IN.Heal[LOC.LI];//"Heal";
                break;

            case 1:
                CardsManager.IN.PowerCardOverride = ECardType.Freeze;
                //<color=#B250B1>Freeze Card</color> freezes an Element Zone.\nPoints remain, but no cards can be added to the Element Zone while it is frozen.

                this.messageText.text = LOC.IN.FreezeDesc[LOC.LI];
                this.confirmButtonText.text = LOC.IN.Freeze[LOC.LI];//"Freeze";
                break;

            case 2:
                CardsManager.IN.PowerCardOverride = ECardType.Lightning;
                //<color=#B250B1>Lightning Card</color> works just like a <color=#B250B1>Bomb Card</color>, but can go through <color=#B250B1>Shields</color>.
                this.messageText.text = LOC.IN.LightningDesc[LOC.LI];
                this.confirmButtonText.text = LOC.IN.Lightning[LOC.LI];//"Lightning";
                break;
        }
    }
}
