using Sirenix.OdinInspector;
using UnityEngine;
using static CardData;

[RequireComponent(typeof(Animator))]
public class UICardFlip : MonoBehaviour
{
    private readonly string ANIM_STATE = "state";

    [SerializeField] private Animator animator;

    [SerializeField] private ECardFlipState animState;
    [SerializeField] private float animSpeed = 1;

    public enum ECardFlipState
    {
        Back,
        Front,
        FlipToFront,
        FlipToBack
    }

    public ECardFlipState CardFlipState => this.animState;

    [ShowInInspector]
    public bool IsFaceUp
    {
        get { return this.animState == ECardFlipState.Front || this.animState == ECardFlipState.FlipToFront; }
    }

    private void OnEnable()
    {
        SetAnimState(this.animState);
    }

    public void SetAnimState(ECardFlipState inState)
    {
        this.animator.speed = this.animSpeed;
        this.animState = inState;
        this.animator.SetInteger(ANIM_STATE, (int)inState);
    }

    public void ToggleState()
    {
        if (this.animState == ECardFlipState.Front)
            SetAnimState(ECardFlipState.FlipToBack);
        else if (this.animState == ECardFlipState.Back)
            SetAnimState(ECardFlipState.FlipToFront);
    }

    //called by timeline animation
    public void SetAnimStateToBack()
    {
        SetAnimState(ECardFlipState.Back);

        RefreshElementZonePoints();
    }

    //called by timeline animation
    public void SetAnimStateToFront()
    {
        SetAnimState(ECardFlipState.Front);

        RefreshElementZonePoints();
    }

    private void RefreshElementZonePoints()
    {
        var cellParent = this.GetComponentInParent<UICardCell>();
        if (cellParent != null)
        {
            if (cellParent.ElementType != EElementType.None)
            {
                cellParent.ElementZone.CalculatePoints();
            }
        }
    }
}
