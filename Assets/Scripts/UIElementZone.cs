using System;
using System.Collections;
using DG.Tweening;
using DomainElementType = Elemento.Domain.EElementType;
using DomainGameRules = Elemento.Domain.GameRules;
using DomainZoneStateData = Elemento.Domain.ZoneStateData;
using Sirenix.OdinInspector;
using UnityEngine;
using static SettingsManager;
using static UIPlayerBoard;

[RequireComponent(typeof(UICardCell))]
public class UIElementZone : MonoBehaviour
{
    public Action<bool> OnElementZonePointsChanged;

    [ReadOnly]
    public EPlayerType PlayerType;

    [ReadOnly]
    public int Points = 0;
    [ReadOnly]
    public int FaceUpPoints = 0;

    public bool IsActivated { get; private set; }
    public bool IsFrozen => this.FrozenHealth > 0;

    [Space]
    public UICardCell CardCell;

    [ReadOnly]
    [Space]
    public int Health = 0;
    [ReadOnly]
    [Space]
    public int FrozenHealth = 0;

    [Space]
    [SerializeField] private UIPoints pointsCount;
    [SerializeField] private GameObject pointsCountDisplay;

    [Space]
    [SerializeField] private UIPoints zoneHealth;
    [SerializeField] private GameObject zoneHealthDisplay;

    [Space]
    [SerializeField] private GameObject zoneDestroyedOverlay;

    [Space]
    [SerializeField] private GameObject[] activeDisplays;

    [Space]
    [SerializeField] private GameObject frozenOverlay;
    [SerializeField] private UIPoints zoneFrozenHealth;

    [Space]
    public UICardPlaceAnim CardPlaceAnimator;

    public bool IsDestroyed { get; private set; }

    public int MaxHealth { get; private set; }

    private void Start()
    {
        SetPointsCount(0);

        this.IsActivated = true;//set to opposite
        SetActiveState(false, false);

        this.FrozenHealth = -1;//set to opposite
        SetFrozenHealth(0, false);
    }

    public void SetPointsCount(int inCount)
    {
        this.pointsCountDisplay.SetActive(inCount > 0 && !this.IsDestroyed);
        this.pointsCount.TickToValue(inCount, SettingsManager.IN.ElementZonePointsRequiredToActivate);

        this.OnElementZonePointsChanged?.Invoke(true);
    }

    public void SubtractHealth(int inAmount)
    {
        SetZoneHealth(this.Health - inAmount);
    }

    public void SetMaxHealth(int inMaxHealth)
    {
        this.MaxHealth = inMaxHealth;
    }

    public void SetActiveState(bool inIsActive, bool inShouldPlaySound = true)
    {
        if (inIsActive == this.IsActivated) return;

        this.IsActivated = inIsActive;

        foreach (var item in this.activeDisplays)
        {
            item.SetActive(inIsActive);
        }

        if (inShouldPlaySound)
        {
            if (inIsActive)
                AudioManager.IN.PlayClip(AudioManager.IN.ActivateZoneClip, .5f);
            else
                AudioManager.IN.PlayClip(AudioManager.IN.DeactivateZoneClip, .5f);
        }
    }

    public void SubtractFrozenHealth(int inAmount)
    {
        var newFrozenHealth = Mathf.Max(0, this.FrozenHealth - inAmount);

        SetFrozenHealth(newFrozenHealth);
    }

    public void SetFrozenHealth(int inFrozenHealth, bool inShouldPlaySound = true)
    {
        if (inFrozenHealth == this.FrozenHealth) return;

        var prevFrozenHealth = this.FrozenHealth;

        this.FrozenHealth = inFrozenHealth;

        if (this.IsFrozen)
        {
            this.zoneFrozenHealth.SetText(this.FrozenHealth, SettingsManager.IN.FrozenZoneHealth);

            SetActiveState(false, false);
        }
        else
        {
            bool hasEnoughPointsToActivate = DomainGameRules.HasReachedActivationThreshold(this.Points, SettingsManager.IN.ElementZonePointsRequiredToActivate);
            SetActiveState(hasEnoughPointsToActivate, false);
        }

        //TODO: change to anim, add particles
        this.frozenOverlay.SetActive(this.IsFrozen);
        RefreshSiblingIndices();

        if (inShouldPlaySound)
        {
            if (prevFrozenHealth == 0 && this.FrozenHealth == SettingsManager.IN.FrozenZoneHealth)
                AudioManager.IN.PlayClip(AudioManager.IN.FreezeZoneClip, .5f);
            else if (prevFrozenHealth > 0 && this.FrozenHealth == 0)
                AudioManager.IN.PlayClip(AudioManager.IN.UnfreezeZoneClip, .5f);
        }
    }

    public void SetZoneHealth(int inHealth)
    {
        bool isElementZoneBombingEnabled = SettingsManager.IN.TutorialSafeElementZoneHealth > 0;

        if (isElementZoneBombingEnabled)
        {
            this.Health = Mathf.Max(0, inHealth);
            this.IsDestroyed = this.Health <= 0;

            if (this.IsDestroyed && this.IsActivated)
                SetActiveState(false, false);

            this.zoneHealthDisplay.SetActive(!this.IsDestroyed);
            this.zoneDestroyedOverlay.SetActive(this.IsDestroyed);

            this.zoneHealth.SetText(this.Health, SettingsManager.IN.TutorialSafeElementZoneHealth);

            if (this.IsDestroyed)
            {
                var board = this.GetComponentInParent<UIPlayerBoard>();
                GameManager.IN.CheckWinLoseState(board, true);
            }
        }
        else
        {
            this.IsDestroyed = false;
            this.zoneHealthDisplay.SetActive(false);
            this.zoneDestroyedOverlay.SetActive(this.IsDestroyed);
        }
    }

    public void CalculatePoints()
    {
        var state = CreateStateData();
        this.Points = DomainGameRules.CalculateZonePoints(state);
        this.FaceUpPoints = DomainGameRules.CalculateZonePoints(state, true);

        RefreshSiblingIndices();

        SetPointsCount(this.FaceUpPoints);

        GameManager.IN.CheckElementZoneActivateState(this);
    }

    public DomainZoneStateData CreateStateData()
    {
        var state = new DomainZoneStateData
        {
            Id = (int)this.CardCell.ElementType,
            ElementType = (DomainElementType)this.CardCell.ElementType,
            Points = this.Points,
            FaceUpPoints = this.FaceUpPoints,
            Health = this.Health,
            MaxHealth = this.MaxHealth,
            FrozenHealth = this.FrozenHealth,
            IsActivated = this.IsActivated,
            IsDestroyed = this.IsDestroyed
        };

        foreach (var card in this.CardCell.Cards)
            state.Cards.Add(card.CreateInstanceData());

        return state;
    }

    public void SwapPlayerState(UIElementZone inOther)
    {
        if (inOther == null)
            throw new ArgumentNullException(nameof(inOther));

        Swap(ref this.PlayerType, ref inOther.PlayerType);
        Swap(ref this.Points, ref inOther.Points);
        Swap(ref this.FaceUpPoints, ref inOther.FaceUpPoints);
        Swap(ref this.Health, ref inOther.Health);
        Swap(ref this.FrozenHealth, ref inOther.FrozenHealth);
        Swap(ref this.CardCell.Cards, ref inOther.CardCell.Cards);

        bool isActivated = this.IsActivated;
        this.IsActivated = inOther.IsActivated;
        inOther.IsActivated = isActivated;

        bool isDestroyed = this.IsDestroyed;
        this.IsDestroyed = inOther.IsDestroyed;
        inOther.IsDestroyed = isDestroyed;

        int maxHealth = this.MaxHealth;
        this.MaxHealth = inOther.MaxHealth;
        inOther.MaxHealth = maxHealth;

        RefreshPlayerStateView();
        inOther.RefreshPlayerStateView();
    }

    private void RefreshPlayerStateView()
    {
        this.CardCell.PlayerType = this.PlayerType;
        foreach (var card in this.CardCell.Cards)
            this.CardCell.SetParentCell(card, false);

        this.CardCell.RefreshVisibleCardCount();
        this.pointsCountDisplay.SetActive(this.FaceUpPoints > 0 && !this.IsDestroyed);
        this.pointsCount.SetText(this.FaceUpPoints, SettingsManager.IN.ElementZonePointsRequiredToActivate);

        foreach (var item in this.activeDisplays)
            item.SetActive(this.IsActivated);

        this.frozenOverlay.SetActive(this.IsFrozen);
        this.zoneFrozenHealth.SetText(this.FrozenHealth, SettingsManager.IN.FrozenZoneHealth);

        bool isElementZoneBombingEnabled = SettingsManager.IN.TutorialSafeElementZoneHealth > 0;
        this.zoneHealthDisplay.SetActive(isElementZoneBombingEnabled && !this.IsDestroyed);
        this.zoneDestroyedOverlay.SetActive(isElementZoneBombingEnabled && this.IsDestroyed);
        this.zoneHealth.SetText(this.Health, SettingsManager.IN.TutorialSafeElementZoneHealth);
        RefreshSiblingIndices();
    }

    private static void Swap<T>(ref T inFirst, ref T inSecond)
    {
        T value = inFirst;
        inFirst = inSecond;
        inSecond = value;
    }

    public void RefreshSiblingIndices()
    {
        this.CardPlaceAnimator.transform.SetAsLastSibling();
        this.frozenOverlay.transform.SetAsLastSibling();
    }

    public void HandleUseActivatePowerButtonClick()
    {
        if (!GameManager.IN.CanControlPlayer(this.PlayerType) || GameManager.IsDealingHand) return;

        if (UITutorialPanel.IsTutorialMode && UITutorialPanel.IN.ShouldBlockInput && UITutorialPanel.IN.AllowedActivateType != this.CardCell.ElementType) return;

        if (SettingsManager.IN.AllowSelectPowerCardType || UITutorialPanel.IsTutorialMode)
        {
            UIPowerCardPanel.IN.Show(() =>
            {
                UseActivatePower();
            });

            UITutorialPanel.OnUseActivatedZoneButtonPressed?.Invoke();
        }
        else
        {
            var localizedElementType = LOC.IN.GetElementNameFromEnum(this.CardCell.ElementType);
            //$"Are you sure you want to use Activation Power?\n\nIt will give you a powerful card, but remove all the cards and points from this zone.";
            var messageText = LOC.IN.Confirm_UsePower[LOC.LI];
            UIConfirmPanel.IN.Show(messageText, () =>
            {
                UseActivatePower();
            });
        }
    }

    public void UseActivatePower()
    {
        UITutorialPanel.OnActivatedZoneUse?.Invoke();
        StartCoroutine(UseActivationPowerCo());
    }

    private IEnumerator UseActivationPowerCo()
    {
        GameManager.IN.SetUndoCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.ActivateDeck);
        GameManager.IsDoingActivateSequence = true;

        foreach (var item in this.activeDisplays)
        {
            item.SetActive(false);
        }

        yield return null;

        var tweenDuration = GameManager.IN.ActivateDiscardTweenDuration;
        var destPosition = GameManager.IN.DiscardZoneTransform.position;
        var delayAtTweenEnd = .1f;

        GameManager.IN.DiscardZone.SetActive(true);

        Move.ShouldRecordMoves = false;

        while (this.CardCell.Cards.Count > 0)
        {
            var sourceTopCard = this.CardCell.TopCard;

            var pitch = 1 + (this.CardCell.Cards.Count * .1f);

            AudioManager.IN.PlayCardSwooshSound(0, pitch);

            sourceTopCard.Draggable.AnimateTo(destPosition, tweenDuration, delayAtTweenEnd);
            sourceTopCard.transform.DOScale(Vector3.one * .001f, tweenDuration).SetDelay(tweenDuration + .05f);

            yield return new WaitForSeconds(tweenDuration + delayAtTweenEnd + .05f);

            AudioManager.IN.PlayClip(AudioManager.IN.RemoveCardClip, .2f, pitch);

            yield return new WaitForSeconds(.1f);
        }

        Move.ShouldRecordMoves = true;

        CalculatePoints();

        GameManager.IN.DiscardZone.SetActive(SettingsManager.IN.DiscardMode == EDiscardMode.AllowDiscard);

        yield return new WaitForSeconds(.7f);

        GameManager.IN.StartCoroutine(GameManager.IN.DealPowerCardCo(this));

        GameManager.IsDoingActivateSequence = false;
    }
}
