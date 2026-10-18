using System;
using System.Collections.Generic;
using DG.Tweening;
using Lean.Pool;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;
using static AudioManager;
using static CardData;
using static ScreenShakeController;
using static SettingsManager;
using static UICardFlip;
using static UICardPlaceAnim;
using static UIPlayerBoard;

public class UICardCell : MonoBehaviour
{
    public enum ECellType
    {
        Deck,
        Discard,
        Hand,
        ElementZone
    }

    public ECellType CellType;

    [ReadOnly]
    public EPlayerType PlayerType;

    [ShowIf("@CellType == ECellType.ElementZone")]
    public EElementType ElementType;

    public int MaxCards = 1;
    public int MaxVisibleCardsInDeck;

    public Action<UICardCell> OnCardRemoved;

    [Space]
    [ReadOnly]
    public List<Card> Cards = new();
    [Space]
    public Transform CardHolder;
    public Transform AnimTarget;

    [ShowIf("@CellType == ECellType.ElementZone")]
    public UIElementZone ElementZone;

    public Card TopCard
    {
        get
        {
            if (this.Cards.Count > 0)
            {
                int lastIndex = this.Cards.Count - 1;
                return this.Cards[lastIndex];
            }

            return null;
        }
    }

    private void OnValidate()
    {
        if (this.CardHolder == null)
            this.CardHolder = this.transform;

        if (this.ElementZone == null)
            this.ElementZone = this.GetComponent<UIElementZone>();
    }

    public void RefreshCellsList()
    {
        this.Cards.Clear();

        var childCards = this.CardHolder.GetComponentsInChildren<Card>(true);

        foreach (var card in childCards)
        {
            this.Cards.Add(card);
        }

        RefreshVisibleCardCount();
    }

    //public void SetCardsDraggable(bool inIsDraggable)
    //{
    //    foreach (var card in this.Cards)
    //    {
    //        card.Draggable.IsDraggable = inIsDraggable;
    //    }
    //}

    public void RefreshVisibleCardCount()
    {
        for (int i = 0; i < this.Cards.Count; i++)
        {
            var card = this.Cards[i];

            bool shouldShow = this.MaxVisibleCardsInDeck == 0 || i >= this.Cards.Count - this.MaxVisibleCardsInDeck;
            card.gameObject.SetActive(shouldShow);
        }
    }

    public void ReturnCardToCell(string inSource = null, ERejectReason inRejectReason = ERejectReason.None)
    {
        //if(inSource != null) Debug.Log(inSource);
        if (!string.IsNullOrEmpty(inSource))
            UIAlertPanel.IN.Show(inSource);

        if (inRejectReason != ERejectReason.None)
            AudioManager.IN.PlayRejectSound(inRejectReason);

        var target = this.AnimTarget != null ? this.AnimTarget : this.CardHolder;
        UIDragOverlay.IN.SetMovingCardFromHand(this.TopCard, this);

        this.TopCard.transform.DOMove(target.position, GameManager.IN.CardSnapBackTweenSpeed).OnComplete(() =>
        {
            SetParentCell(this.TopCard);
            this.TopCard.SetDimmedDecorations(this.ElementType);

            if (this.ElementZone != null)
                this.ElementZone.RefreshSiblingIndices();
        });
    }

    public void HandleDrop(UICardCell inSourceCell)
    {
        string cardId = inSourceCell.TopCard != null ? inSourceCell.TopCard.Id : null;

        if (this.CellType == ECellType.Discard)
        {
            if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
            {
                GameManager.IN.TryHighlightDiscardPile();

                DeleteCard(inSourceCell);

                AudioManager.IN.PlayCardDropSound(6, .5f, 1);
            }
            else
            {
                DeleteCard(inSourceCell);
                AudioManager.IN.PlayCardDropSound(7, .5f, .8f);
            }

            if (inSourceCell.ElementZone != null)
                inSourceCell.ElementZone.CalculatePoints();

            GameManager.IN.SaveBoardState(inSourceCell, this, true, inCardId: cardId);

            if (SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn
                && GameManager.IN.IsPlayersTurn && !GameManager.IN.IsCurrentSeatControlledByAI)
                GameManager.IN.EndTurn();

            return;
        }
        else
        {
            bool isTurnLimit = SettingsManager.IN.TutorialSafeMovesPerTurn > 0;
            if (isTurnLimit && GameManager.IN.MovesRemaining <= 0 && SettingsManager.IN.DiscardMode == EDiscardMode.DiscardEndsTurn)
            {
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_OutOfMoves[LOC.LI], ERejectReason.OutOfMoves);//"You're out of moves!\nDiscard a card to continue."
                return;
            }
        }

        var isElementZone = this.CellType == ECellType.ElementZone;
        var isOwnElementZone = this.PlayerType == inSourceCell.PlayerType;
        var elementIndex = (int)this.ElementType - 1;

        var canFreeze = isElementZone && inSourceCell.TopCard.Data.WildFreeze > 0 && this.ElementZone.FrozenHealth < SettingsManager.IN.FrozenZoneHealth;
        var isValidBomb = inSourceCell.TopCard.Data.WildBomb > 0 || (elementIndex >= 0 && inSourceCell.TopCard.Data.Bombs[elementIndex] > 0);
        var isBlockedByTutorial = false;

        if (UITutorialPanel.IsTutorialMode)
        {
            if (UITutorialPanel.IN.ValidTargetCells.Count > 0 && !UITutorialPanel.IN.ValidTargetCells.Contains(this))
                isBlockedByTutorial = true;
        }

        if (isElementZone && (isBlockedByTutorial || (!this.ElementZone.IsDestroyed && this.ElementZone.IsFrozen && !isValidBomb && !canFreeze && inSourceCell.TopCard.Data.WildLightning <= 0)))
        {
            //TODO: more cases
            if (this.ElementZone.IsDestroyed)
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_DestroyedZone[LOC.LI], ERejectReason.DestroyedZone);//"This Element Zone is already frozen!"
            else if (inSourceCell.TopCard.Data.WildFreeze > 0 && this.ElementZone.FrozenHealth >= SettingsManager.IN.FrozenZoneHealth)
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_AlreadyFrozenZone[LOC.LI], ERejectReason.DestroyedZone);//"This Element Zone is already frozen!"
            else if (isBlockedByTutorial)
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_TutorialBlocked[LOC.LI], ERejectReason.TutorialBlockedCell);//"This Element Zone is already frozen!"
            else
                inSourceCell.ReturnCardToCell(LOC.IN.Reject_FrozenZone[LOC.LI], ERejectReason.ZoneIsFrozen);//"You cannot play a card on a Frozen Element Zone!"
        }
        else if (isElementZone && inSourceCell.TopCard.Data.IsPowerCard)
        {
            if (inSourceCell.TopCard.Data.WildZoneHeal > 0)
            {
                TryHealElementZone(inSourceCell);
            }
            else if (inSourceCell.TopCard.Data.WildFreeze > 0)
            {
                FreezeElementZone(inSourceCell);
            }
            else if (inSourceCell.TopCard.Data.WildLightning > 0)
            {
                TryLightningElementZone(inSourceCell);
            }
        }
        else if (isElementZone && this.ElementZone.IsDestroyed)
        {
            inSourceCell.ReturnCardToCell(LOC.IN.Reject_DestroyedZone[LOC.LI], ERejectReason.DestroyedZone);//"You cannot place a card\non a destroyed Element Zone"
        }
        else if (isElementZone && inSourceCell.TopCard.Data.IsBombOrCombo && (!isOwnElementZone || this.ElementZone.IsFrozen))
        {
            BombCell(inSourceCell);
        }
        else if (this.CellType == ECellType.Hand && inSourceCell.CellType == ECellType.Hand)
        {
            SwapCells(inSourceCell);
        }
        else if (this.CellType == ECellType.Hand && (inSourceCell.CellType == ECellType.Deck || inSourceCell.CellType == ECellType.ElementZone))
        {
            var hand = this.GetComponentInParent<UIHand>();
            hand.InsertCell(inSourceCell, this);

            //if (inSourceCell.CellType == ECellType.Deck)
            inSourceCell.RefreshVisibleCardCount();

            RefreshVisibleCardCount();
        }
        else
        {
            TryAddToCell(inSourceCell);
        }

        void TryHealElementZone(UICardCell inSourceCell)
        {
            var rejectReasonMsg = string.Empty;
            var rejectReason = ERejectReason.None;
            var dropCellPermitted = true;

            if (!isOwnElementZone)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_CardOnOpponentBoard[LOC.LI];//"You cannot place cards in another player's game board!"
                rejectReason = ERejectReason.CardOnOpponentBoard;
            }
            else if (this.ElementZone.IsFrozen)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_CannotHealFrozenZone[LOC.LI];//"Cannot heal Frozen Zone!"
                rejectReason = ERejectReason.ZoneIsFrozen;
            }
            else if (this.Cards.Count > 0)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_ZoneMustBeEmptyToHeal[LOC.LI];//"Zone must be empty to Heal!";
                rejectReason = ERejectReason.HealNonEmptyZone;
            }
            else if (this.ElementZone.Health >= SettingsManager.IN.TutorialSafeElementZoneHealth)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_ZoneHasNoDamage[LOC.LI];//"Zone has no damage!";
                rejectReason = ERejectReason.HealNonEmptyZone;
            }
            else if (UITutorialPanel.IsTutorialMode)
            {
                if (UITutorialPanel.IN.ValidTargetCells.Count > 0 && !UITutorialPanel.IN.ValidTargetCells.Contains(this))
                {
                    dropCellPermitted = false;
                    rejectReasonMsg = LOC.IN.Reject_TutorialBlocked[LOC.LI];//"Please place card where indicated by tutorial!"
                    rejectReason = ERejectReason.TutorialBlockedCell;
                }
            }

            if (dropCellPermitted)
            {
                this.ElementZone.SetZoneHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);
                this.ElementZone.SetMaxHealth(SettingsManager.IN.TutorialSafeElementZoneHealth);

                DeleteCard(inSourceCell);

                GameManager.IN.SaveBoardState(inSourceCell, this, inCardId: cardId);

                inSourceCell.RefreshVisibleCardCount();
                RefreshVisibleCardCount();

                AudioManager.IN.PlayClip(AudioManager.IN.HealClip, 1);

                SpawnHealParticle();

                UITutorialPanel.OnCardPlaced?.Invoke(inSourceCell);

                GameManager.IN.DecrementMovesRemaining();
            }
            else
                inSourceCell.ReturnCardToCell(rejectReasonMsg, rejectReason);

            void SpawnHealParticle()
            {
                var healParticle = LeanPool.Spawn(GameManager.IN.HealParticlePrefab);

                healParticle.transform.SetParent(UIDragOverlay.IN.transform);
                healParticle.transform.position = this.AnimTarget.transform.position;
                healParticle.transform.localScale = Vector3.one;
                healParticle.transform.rotation = Quaternion.identity;

                var despawn = healParticle.GetComponent<DespawnParticle>();
                despawn.SetDespawn(5f);
            }
        }

        void FreezeElementZone(UICardCell inSourceCell)
        {
            this.ElementZone.SetFrozenHealth(SettingsManager.IN.FrozenZoneHealth);

            UITutorialPanel.OnCardPlaced?.Invoke(inSourceCell);

            DeleteCard(inSourceCell);

            GameManager.IN.DecrementMovesRemaining();

            GameManager.IN.SaveBoardState(inSourceCell, this, inCardId: cardId);
        }

        void TryLightningElementZone(UICardCell inSourceCell)
        {
            var isOwnElementZone = this.PlayerType == inSourceCell.PlayerType;
            var sourceTopCard = inSourceCell.TopCard;
            var rejectReasonMsg = string.Empty;
            var rejectReason = ERejectReason.None;
            var lightningPoints = sourceTopCard.Data.WildLightning;
            var numCardsToZap = Mathf.Min(lightningPoints, this.Cards.Count);
            var cardsToDestroy = new List<Card>();
            var frozenOk = (isOwnElementZone && this.ElementZone.IsFrozen) || (!isOwnElementZone && !this.ElementZone.IsFrozen);
            var isBlockedByTutorial = false;

            bool isElementZoneBombingEnabled = SettingsManager.IN.TutorialSafeElementZoneHealth > 0;

            if (isOwnElementZone && !this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_LightningOnOwnBoard[LOC.LI];//"You cannot place a Lightning Card on your own board!";
                rejectReason = ERejectReason.CardOnOpponentBoard;
            }

            if (!isOwnElementZone && this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_LightningFrozenOpponent[LOC.LI];//"You cannot play a Lightning Card\non another player's frozen Element Zone!"
                rejectReason = ERejectReason.BombDoesNotContainElement;
            }

            if (this.ElementZone.IsDestroyed && !this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_LightningOnDestroyed[LOC.LI];//"You cannot place a Lightning Card on a destroyed Element Zone!";
                rejectReason = ERejectReason.BombOnDestroyed;
            }

            if (UITutorialPanel.IsTutorialMode)
            {
                if (UITutorialPanel.IN.ValidTargetCells.Count > 0 && !UITutorialPanel.IN.ValidTargetCells.Contains(this))
                {
                    isBlockedByTutorial = true;
                    rejectReasonMsg = LOC.IN.Reject_TutorialBlocked[LOC.LI];//"Please place card where indicated by tutorial!"
                    rejectReason = ERejectReason.TutorialBlockedCell;
                }
            }

            if (!isBlockedByTutorial && !this.ElementZone.IsDestroyed && frozenOk && (this.Cards.Count > 0 || isElementZoneBombingEnabled))
            {
                SpawnLightningParticle();
                ScreenShakeController.IN.DoScreenShake(EScreenShakeMode.Medium);
                AudioManager.IN.PlayClip(AudioManager.IN.LightningClip, .6f);

                DeleteCard(inSourceCell);
                inSourceCell.RefreshVisibleCardCount();

                UITutorialPanel.OnCardPlaced?.Invoke(inSourceCell);

                if (this.ElementZone.IsFrozen)
                {
                    this.ElementZone.SubtractFrozenHealth(lightningPoints);
                }
                else if (this.Cards.Count > 0)
                {
                    //loop backwards thru cards and check for bombs
                    for (int i = this.Cards.Count - 1; i >= 0 && numCardsToZap > 0; --i)
                    {
                        var targetCard = this.Cards[i];

                        //normal card, so remove it and move on to next one if more bomb points remaining
                        cardsToDestroy.Add(targetCard);
                        --numCardsToZap;
                    }
                }
                else
                {
                    this.ElementZone.SubtractHealth(lightningPoints);
                }
            }
            else
            {
                inSourceCell.ReturnCardToCell(rejectReasonMsg, rejectReason);
                return;
            }

            //TODO: put this in UI to display to user, then delete it later
            for (int i = 0; i < cardsToDestroy.Count; i++)
            {
                var cardToDestroy = cardsToDestroy[i];
                this.Cards.Remove(cardToDestroy);

                cardToDestroy.DestroySelf();
            }

            RefreshVisibleCardCount();
            this.ElementZone.CalculatePoints();

            if (rejectReason != ERejectReason.None)
            {
                inSourceCell.ReturnCardToCell(rejectReasonMsg, rejectReason);
            }
            else
                GameManager.IN.DecrementMovesRemaining();

            GameManager.IN.PlayerInputBoard.Hand.RefreshPlayerHandText();

            GameManager.IN.TryHighlightDiscardPile();
            GameManager.IN.SaveBoardState(inSourceCell, this, inCardId: cardId);

            void SpawnLightningParticle()
            {
                var lightningParticle = LeanPool.Spawn(GameManager.IN.LightningParticlePrefab);

                lightningParticle.transform.SetParent(UIDragOverlay.IN.transform);
                lightningParticle.transform.position = this.AnimTarget.transform.position;
                lightningParticle.transform.localScale = Vector3.one;
                lightningParticle.transform.rotation = Quaternion.identity;

                var despawn = lightningParticle.GetComponent<DespawnParticle>();
                despawn.SetDespawn(5f);

                var anim = lightningParticle.GetComponent<Animation>();
                anim.Play();
            }
        }

        void SwapCells(UICardCell inSourceCell)
        {
            var sourceTopCard = inSourceCell.TopCard;
            var targetTopCard = this.TopCard;

            inSourceCell.Cards.Remove(sourceTopCard);
            this.Cards.Remove(targetTopCard);

            inSourceCell.Cards.Add(targetTopCard);
            this.Cards.Add(sourceTopCard);

            inSourceCell.SetParentCell(targetTopCard);//error
            SetParentCell(sourceTopCard);

            AudioManager.IN.PlayCardDropSound(4, .2f);
        }

        void BombCell(UICardCell inSourceCell)
        {
            //Debug.Log($"UICardCell.BombCell()  inSourceCell = {inSourceCell}  target = {this}");

            var sourceTopCard = inSourceCell.TopCard;
            var hasBombElement = sourceTopCard.Data.HasBombElement(this.ElementType);
            var isOwnElementZone = this.PlayerType == inSourceCell.PlayerType;
            var bombPoints = Mathf.Max(sourceTopCard.Data.Bombs[elementIndex], sourceTopCard.Data.WildBomb);
            var numCardsToBomb = Mathf.Min(bombPoints, this.Cards.Count);
            var cardsToDestroy = new List<Card>();
            var isElementZoneBombingEnabled = SettingsManager.IN.TutorialSafeElementZoneHealth > 0;
            var rejectReasonMsg = LOC.IN.Reject_BombDoesNotContainElement[LOC.LI];// "Bomb Card does not contain an Element from Element Zone!";
            var rejectReason = ERejectReason.BombDoesNotContainElement;
            var frozenOk = (isOwnElementZone && this.ElementZone.IsFrozen) || (!isOwnElementZone && !this.ElementZone.IsFrozen);
            var isBlockedByTutorial = false;

            //TODO: add frozen oppenent zone reject reason

            if (isOwnElementZone && !this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_BombOnOwnZone[LOC.LI];//"You cannot place a Bomb Card on your own Element Zone!";
                rejectReason = ERejectReason.BombDoesNotContainElement;
            }

            if (!isOwnElementZone && this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_BombOnFrozenOpponent[LOC.LI];//"You cannot play a Bomb Card\non another player's frozen Element Zone!"
                rejectReason = ERejectReason.BombDoesNotContainElement;
            }

            if (!isElementZoneBombingEnabled)
            {
                rejectReasonMsg = LOC.IN.Reject_BombOnEmpty[LOC.LI];//"You cannot place a Bomb Card on an empty Element Zone!";
                rejectReason = ERejectReason.BombOnEmpty;
            }

            if (this.ElementZone.IsDestroyed && !this.ElementZone.IsFrozen)
            {
                rejectReasonMsg = LOC.IN.Reject_BombOnDestroyed[LOC.LI];//"You cannot place a Bomb Card on a destroyed Element Zone!";
                rejectReason = ERejectReason.BombOnDestroyed;
            }

            if (UITutorialPanel.IsTutorialMode)
            {
                if (UITutorialPanel.IN.ValidTargetCells.Count > 0 && !UITutorialPanel.IN.ValidTargetCells.Contains(this))
                {
                    isBlockedByTutorial = true;
                    rejectReasonMsg = LOC.IN.Reject_TutorialBlocked[LOC.LI];//"Please place card where indicated by tutorial!"
                    rejectReason = ERejectReason.TutorialBlockedCell;
                }
            }

            if (!isBlockedByTutorial && hasBombElement && !this.ElementZone.IsDestroyed && frozenOk && (this.Cards.Count > 0 || isElementZoneBombingEnabled))
            {
                SpawnExplosionParticle();
                ScreenShakeController.IN.DoScreenShake((EScreenShakeMode)bombPoints - 1);
                float volume = .4f + (.05f * bombPoints);
                float basePitch = 1.1f - (bombPoints * .05f);
                AudioManager.IN.PlayExplosionSound(volume, basePitch);

                DeleteCard(inSourceCell);

                inSourceCell.RefreshVisibleCardCount();

                if (this.ElementZone.IsFrozen)
                {
                    this.ElementZone.SubtractFrozenHealth(bombPoints);
                }
                else if (this.Cards.Count > 0)
                {
                    //loop backwards thru cards and check for bombs
                    for (int i = this.Cards.Count - 1; i >= 0 && numCardsToBomb > 0; --i)
                    {
                        var targetCard = this.Cards[i];

                        if (targetCard.Data.IsShield)
                        {
                            int shieldPoints = Mathf.Max(targetCard.Data.Shields[elementIndex], targetCard.Data.WildShield);

                            //Question: is it intuitive that the bomb must have MORE than the shield, or equal values should remove the shield?
                            if (shieldPoints > bombPoints)
                            {
                                //shield completely blocks bomb and persists
                                break;
                            }
                            else
                            {
                                //remove shield card and do not delete any cards below
                                cardsToDestroy.Add(this.Cards[i]);
                                numCardsToBomb -= shieldPoints;
                                break;
                            }
                        }
                        else
                        {
                            //normal card, so remove it and move on to next one if more bomb points remaining
                            cardsToDestroy.Add(this.Cards[i]);
                            --numCardsToBomb;
                        }
                    }
                }
                else
                    this.ElementZone.SubtractHealth(bombPoints);

                UITutorialPanel.OnCardPlaced?.Invoke(inSourceCell);
            }
            else
            {
                inSourceCell.ReturnCardToCell(rejectReasonMsg, rejectReason);
                return;
            }

            //TODO: put this in UI to display to user, then delete it later
            for (int i = 0; i < cardsToDestroy.Count; i++)
            {
                var cardToDestroy = cardsToDestroy[i];
                this.Cards.Remove(cardToDestroy);

                cardToDestroy.DestroySelf();
            }

            RefreshVisibleCardCount();

            this.ElementZone.CalculatePoints();

            GameManager.IN.PlayerInputBoard.Hand.RefreshPlayerHandText();

            GameManager.IN.DecrementMovesRemaining();
            GameManager.IN.TryHighlightDiscardPile();
            GameManager.IN.SaveBoardState(inSourceCell, this, inCardId: cardId);

            void SpawnExplosionParticle()
            {
                var bombParticle = LeanPool.Spawn(GameManager.IN.BombParticlePrefab);

                bombParticle.transform.SetParent(UIDragOverlay.IN.transform);
                bombParticle.transform.position = this.AnimTarget.transform.position;
                bombParticle.transform.localScale = Vector3.one;
                bombParticle.transform.rotation = Quaternion.identity;

                var despawn = bombParticle.GetComponent<DespawnParticle>();
                despawn.SetDespawn(3f);
            }
        }
    }

    public void TryAddToCell(UICardCell inSourceCell, bool inIsCalledByHand = false)
    {
        //Debug.Log($"UICardCell.TryAddToCell()  inSourceCell = {inSourceCell}  target = {this}");

        var rejectReasonMsg = LOC.IN.Reject_HandIsFull[LOC.LI];//LOC.IN.HandIsFull[LOC.LI];// "Your Hand is Full!";
        var rejectReason = ERejectReason.HandIsFull;

        var dropCellPermitted = this.Cards.Count < this.MaxCards || inIsCalledByHand;

        var isOwnHand = this.PlayerType == inSourceCell.PlayerType;
        var isFromDeck = inSourceCell.CellType == ECellType.Deck;
        var hasElement = false;

        if (dropCellPermitted && this.CellType == ECellType.Deck && !inSourceCell.TopCard.Draggable.IsAnimateDragging)
        {
            dropCellPermitted = false;
            rejectReasonMsg = LOC.IN.Reject_GameDeck[LOC.LI];//"You cannot place a card\nonto the Game Deck!"
            rejectReason = ERejectReason.GameDeck;
        }

        if (dropCellPermitted && isOwnHand && inSourceCell.TopCard.Data.IsBomb)
        {
            dropCellPermitted = false;
            rejectReasonMsg = LOC.IN.Reject_BombOnOwnZone[LOC.LI];//"You cannot place a bomb card\nin your own Element Zone!"
            rejectReason = ERejectReason.BombOnOwnZone;
        }

        if (dropCellPermitted && !isOwnHand && !isFromDeck)
        {
            dropCellPermitted = false;
            rejectReasonMsg = LOC.IN.Reject_CardOnOpponentBoard[LOC.LI];//"You cannot place cards in another player's game board!"
            rejectReason = ERejectReason.CardOnOpponentBoard;
        }

        if (dropCellPermitted && this.CellType == ECellType.ElementZone)
        {
            var locElement = LOC.IN.GetElementNameFromEnum(this.ElementType);
            rejectReasonMsg = string.Format(LOC.IN.Reject_ElementZoneFull[LOC.LI], locElement);//$"{this.ElementType} Zone is Full!"
            rejectReason = ERejectReason.ElementZoneFull;
        }

        if (dropCellPermitted && this.CellType == ECellType.Hand)
        {
            var hand = this.GetComponentInParent<UIHand>();
            var openCell = hand.GetFirstOpenCell();

            if (openCell == null)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_InvalidDropLocation[LOC.LI];// "Invalid drop location!"
                rejectReason = ERejectReason.InvalidDropLocation;
            }
        }

        //only allow element cards containing corresponding element onto element zones
        if (dropCellPermitted && this.ElementType != EElementType.None)
        {
            hasElement = inSourceCell.TopCard.Data.HasElement(this.ElementType, false);

            if (!hasElement)
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_DoesNotContainElement[LOC.LI];//"Card does not contain\nan Element from Element Zone!"
                rejectReason = ERejectReason.DoesNotContainElement;
            }
        }

        if (inSourceCell == this)
        {
            dropCellPermitted = false;
            rejectReasonMsg = string.Empty;
            rejectReason = ERejectReason.MoveToSameCell;
        }

        if (UITutorialPanel.IsTutorialMode && inSourceCell.CellType != ECellType.Deck)
        {
            if (UITutorialPanel.IN.ValidTargetCells.Count > 0 && !UITutorialPanel.IN.ValidTargetCells.Contains(this))
            {
                dropCellPermitted = false;
                rejectReasonMsg = LOC.IN.Reject_TutorialBlocked[LOC.LI];//"Please place card where indicated by tutorial!"
                rejectReason = ERejectReason.TutorialBlockedCell;
            }
        }

        if (dropCellPermitted)
        {
            var sourceTopCard = inSourceCell.TopCard;

            this.Cards.Add(sourceTopCard);
            SetParentCell(sourceTopCard);

            inSourceCell.Cards.Remove(sourceTopCard);
            inSourceCell.RefreshVisibleCardCount();

            RefreshVisibleCardCount();

            UITutorialPanel.OnCardPlaced?.Invoke(inSourceCell);

            if (inSourceCell.CellType == ECellType.Deck)
            {
                sourceTopCard.CardFlip.SetAnimState(ECardFlipState.FlipToFront);
                GameManager.IN.SetUndoCheckpoint(Elemento.Domain.ETurnUndoCheckpoint.RevealHiddenCard);
            }

            if (!inIsCalledByHand)
                inSourceCell.OnCardRemoved?.Invoke(inSourceCell);

            if (hasElement)
            {
                this.ElementZone.CalculatePoints();
                sourceTopCard.SetDimmedDecorations(this.ElementType);

                //play card anim effect
                int zoneIndex = (int)this.ElementType - 1;

                if (sourceTopCard.Data.Points[zoneIndex] + sourceTopCard.Data.WildPoints > 0)
                    this.ElementZone.CardPlaceAnimator.PlayPlaceCardAnim(ECardPlaceType.PlacePointsCard);
                else if (sourceTopCard.Data.Shields[zoneIndex] + sourceTopCard.Data.WildShield > 0)
                    this.ElementZone.CardPlaceAnimator.PlayPlaceCardAnim(ECardPlaceType.PlaceShieldCard);
                else if (sourceTopCard.Data.Multipliers[zoneIndex] + sourceTopCard.Data.WildMultiplier == 2)
                    this.ElementZone.CardPlaceAnimator.PlayPlaceCardAnim(ECardPlaceType.PlaceMult_x2);
                else if (sourceTopCard.Data.Multipliers[zoneIndex] + sourceTopCard.Data.WildMultiplier == 3)
                    this.ElementZone.CardPlaceAnimator.PlayPlaceCardAnim(ECardPlaceType.PlaceMult_x3);
            }

            //if dragging from an element zone
            if (inSourceCell.ElementType != EElementType.None)
            {
                inSourceCell.ElementZone.CalculatePoints();
            }

            if (inSourceCell.CellType == ECellType.Deck && !GameManager.IsApplyingRemoteUpdate)
            {
                CardsManager.IN.AddBatchOfCardsToGameDeck();
            }

            if (this.CellType == ECellType.Hand || inSourceCell.CellType == ECellType.Hand)
            {
                GameManager.IN.PlayerInputBoard.Hand.RefreshPlayerHandText();
            }

            if (inSourceCell.CellType != ECellType.Deck)
            {
                GameManager.IN.DecrementMovesRemaining();
                GameManager.IN.TryHighlightDiscardPile();

                GameManager.IN.SaveBoardState(inSourceCell, this, inCardId: sourceTopCard != null ? sourceTopCard.Id : null);
            }

            if (this.CellType == ECellType.Hand && inSourceCell.CellType != ECellType.Deck)
            {
                AudioManager.IN.PlayCardDropSound(9, .1f);
            }
            else if (inSourceCell == this)
            {
                AudioManager.IN.PlayCardDropSound(5, .1f, 1.2f);
            }
            else if (hasElement)
            {
                var elementIndex = (int)this.ElementType - 1;

                if (sourceTopCard.Data.IsPoints)
                {
                    //var pitch = .8f + this.ElementZone.Points * .02f;
                    var pitch = .9f + this.Cards.Count * .03f;
                    pitch = Mathf.Min(pitch, 1.7f);
                    AudioManager.IN.PlayCardDropSound(1, .3f, pitch);
                }
                else if (sourceTopCard.Data.IsMultiply)
                {
                    var highestMult = Mathf.Max(sourceTopCard.Data.Multipliers[elementIndex], sourceTopCard.Data.WildMultiplier);
                    var pitch = highestMult == 2 ? .75f : 1;
                    AudioManager.IN.PlayCardDropSound(2, .5f, pitch);
                }
                else if (sourceTopCard.Data.IsShield)
                {
                    AudioManager.IN.PlayCardDropSound(3);
                }
            }
            else
            {
                //Debug.Log("Drop case for sound unhandled");
            }
        }
        else
        {
            inSourceCell.ReturnCardToCell(rejectReasonMsg, rejectReason);
        }
    }

    public void AddCardToCell(Card inCard)
    {
        this.Cards.Add(inCard);
        SetParentCell(inCard);
        //RefreshVisibleCardCount();
        inCard.CardFlip.SetAnimState(ECardFlipState.Front);
    }

    public static void DeleteCard(UICardCell inSourceCell)
    {
        var sourceTopCard = inSourceCell.TopCard;
        inSourceCell.Cards.Remove(sourceTopCard);
        sourceTopCard.DestroySelf();

        inSourceCell.OnCardRemoved?.Invoke(inSourceCell);
    }

    public void SetParentCell(Card inCard, bool shouldRefreshGrid = true)
    {
        if (shouldRefreshGrid)
            RefreshVisibleCardCount();

        inCard.transform.SetParent(this.CardHolder);
        inCard.transform.localPosition = Vector3.zero;
        inCard.transform.localScale = Vector3.one;
        inCard.transform.localRotation = Quaternion.identity;

        // FrozenOverlay shares CardHolder with the cards, so every card parented here must re-sort it on top.
        if (this.ElementZone != null)
            this.ElementZone.RefreshSiblingIndices();

        if (this.CellType == ECellType.Hand)
        {
            var grid = this.GetComponentInParent<HorizontalLayoutGroup>();

            if (grid != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(grid.GetComponent<RectTransform>());
        }
    }

    public static UICardCell GetCellAtPosition(Vector3 inPosition)
    {
        var results = UIDraggable.GameObjectAtPosition(inPosition);

        foreach (var result in results)
        {
            var cell = result.gameObject.GetComponent<UICardCell>();

            if (cell != null)
                return cell;
        }

        return null;
    }
}
