using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using static Card;
using static CardData;

public class UITutorialPanel : UIPanelBase
{
    public static UITutorialPanel IN;
    public static bool IsTutorialMode;
    private readonly string ANIM_STATE = "tutorialState";

    public static Action<UICardCell> OnCardPlaced;
    public static Action OnUseActivatedZoneButtonPressed;
    public static Action OnHealthPowerCardButtonPressed;
    public static Action OnActivatedZoneUse;
    public static Action<ECardType> OnPowerCardDealt;

    public int TutorialStateIndex;

    public List<UICardCell> InteractableCardCells => this.interactableCardCells[this.TutorialStateIndex].Cells;
    public List<UICardCell> ValidTargetCells => this.interactableCardCells[this.TutorialStateIndex].TargetCells;
    public bool ShouldBlockInput => this.interactableCardCells[this.TutorialStateIndex].ShouldBlockInput;
    public EElementType AllowedActivateType => this.interactableCardCells[this.TutorialStateIndex].AllowedActivateType;

    [SerializeField] private Animator tutorialAnim;

    [SerializeField] private TextMeshProUGUI tutorialBubbleText;
    [SerializeField] private GameObject tutorialBubble;

    [SerializeField] private CanvasGroup iris;
    [SerializeField] private CanvasGroup dimScreen;

    [Space]
    [SerializeField] private bool shouldValidate;

    [Space]
    [SerializeField] private CardCellList[] interactableCardCells = new CardCellList[20];

    [Serializable]
    public struct CardCellList
    {
        public int Index;
        public bool ShouldIncrementTutorial;
        public bool ShouldBlockInput;
        public List<UICardCell> Cells;
        public List<UICardCell> TargetCells;
        public EElementType AllowedActivateType;
    }

    private void OnValidate()
    {
        if (!this.shouldValidate) return;

        this.shouldValidate = false;

        for (int i = 0; i < this.interactableCardCells.Length; i++)
        {
            this.interactableCardCells[i].Index = i;
        }
    }

    public override void Show()
    {
        UITutorialPanel.IsTutorialMode = true;
        this.interactableCardCells[22].ShouldIncrementTutorial = false;//undo hack
        this.TutorialStateIndex = -1;
        base.Show();

        UITutorialPanel.OnCardPlaced += CardPlaced;
        UITutorialPanel.OnUseActivatedZoneButtonPressed += HandleUseActivatedZonePress;
        UITutorialPanel.OnHealthPowerCardButtonPressed += DoIncrementState;
        UITutorialPanel.OnActivatedZoneUse += DoIncrementState;
        UITutorialPanel.OnPowerCardDealt += PowerCardDealt;

        CardsManager.IN.PowerCardOverride = Card.ECardType.Heal;

        IncrementTutorialState();
    }

    public override void Hide()
    {
        UITutorialPanel.IsTutorialMode = false;

        UITutorialPanel.OnCardPlaced -= CardPlaced;
        UITutorialPanel.OnUseActivatedZoneButtonPressed -= HandleUseActivatedZonePress;
        UITutorialPanel.OnHealthPowerCardButtonPressed -= DoIncrementState;
        UITutorialPanel.OnActivatedZoneUse -= DoIncrementState;
        UITutorialPanel.OnPowerCardDealt -= PowerCardDealt;

        CardsManager.IN.PowerCardOverride = Card.ECardType.None;

        base.Hide();
    }

    private void CardPlaced(UICardCell inSourceCell)
    {
        //Debug.Log($"CardPlaced({inSourceCell.name})   this.TutorialStateIndex = {this.TutorialStateIndex}");

        if (!UITutorialPanel.IsTutorialMode || inSourceCell.CellType == UICardCell.ECellType.Deck || !GameManager.IN.IsPlayersTurn) return;

        var tutState = this.interactableCardCells[this.TutorialStateIndex];

        if (tutState.ShouldIncrementTutorial)
            IncrementTutorialState();
    }

    private void HandleUseActivatedZonePress()
    {
        //Debug.Log("HandleUseActivatedZonePress()");

        if (!UITutorialPanel.IsTutorialMode || !GameManager.IN.IsPlayersTurn) return;

        IncrementTutorialState();
    }

    private void DoIncrementState()
    {
        //Debug.Log("DoIncrementState()");

        if (!UITutorialPanel.IsTutorialMode || !GameManager.IN.IsPlayersTurn) return;

        IncrementTutorialState();
    }

    private void PowerCardDealt(ECardType inCardType)
    {
        //Debug.Log($"<color=white>PowerCardDealt</color> TutorialStateIndex = {UITutorialPanel.IN.TutorialStateIndex}");

        if (!UITutorialPanel.IsTutorialMode || !GameManager.IN.IsPlayersTurn) return;

        var tutState = this.interactableCardCells[this.TutorialStateIndex];

        if (tutState.ShouldIncrementTutorial)
            IncrementTutorialState();
    }

    public void IncrementTutorialState()
    {
        ++this.TutorialStateIndex;

        //Debug.Log($"<color=yellow>IncrementTutorialState</color> TutorialStateIndex = {UITutorialPanel.IN.TutorialStateIndex}");

        if (this.TutorialStateIndex < LOC.IN.TutorialLocItems.Length)
        {
            SetBubbleText();

            this.tutorialAnim.SetInteger(ANIM_STATE, this.TutorialStateIndex);
        }
        else
            ReturnToHomeScreen();
    }

    public void HandleNextTurnButtonPress()
    {
        IncrementTutorialState();
        GameManager.IN.StartCoroutine(GameManager.IN.TutorialOpponentsTurnCo());
    }

    public void HandleCloseButtonPress()
    {
        string titleText = LOC.IN.LeaveTutorial[LOC.LI];// "Leave Tutorial?";
        string messageText = LOC.IN.LeaveTutorialMessage[LOC.LI];//$"Are you sure you want to leave the tutorial?";

        UIConfirmPanel.IN.Show(titleText, messageText, () =>
        {
            ReturnToHomeScreen();
        });
    }

    private void ReturnToHomeScreen()
    {
        BoardStateManager.IsDoingReplay = false;
        GameManager.IN.CleanUpGameState();

        UISplashScreen.IN.Show();
        UIGameBoard.IN.Hide();
        Hide();
    }

    public void SetBubbleText()
    {
        var locItem = LOC.IN.TutorialLocItems[this.TutorialStateIndex];

        if (locItem.Text.Length > 0)
            this.tutorialBubbleText.text = locItem.Text[LOC.LI];
    }

    #region Play from Animation Timeline
    public void DealPlayerHand()
    {
        GameManager.IN.StartCoroutine(GameManager.IN.DealTutorialCardsCo());
    }

    public void DealLightningPowerCard()
    {
        //Debug.Log("DealLightningPowerCard()");
        CardsManager.IN.PowerCardOverride = ECardType.Lightning;
        GameManager.IN.StartCoroutine(GameManager.IN.DealPowerCardCo());
    }

    public void DealFreezePowerCard()
    {
        //Debug.Log("DealFreezePowerCard()");
        CardsManager.IN.PowerCardOverride = ECardType.Freeze;
        GameManager.IN.StartCoroutine(GameManager.IN.DealPowerCardCo());
    }

    public void SetIncrementTutorialTrue()
    {
        //Debug.Log($"SetIncrementTutorialTrue()  this.TutorialStateIndex = {this.TutorialStateIndex}");
        this.interactableCardCells[this.TutorialStateIndex].ShouldIncrementTutorial = true;//hack
    }
    #endregion
}