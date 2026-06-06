using System.Collections;
using UnityEngine;

public class UICardPlaceAnim : MonoBehaviour
{
    [SerializeField] private Animator animator;

    [SerializeField] private RectTransform animParent;

    private string triggerName;

    public enum ECardPlaceType
    {
        Clear,
        PlacePointsCard,
        PlaceShieldCard,
        PlaceMult_x2,
        PlaceMult_x3
    }

    public void PlayPlaceCardAnim(ECardPlaceType inCardPlaceType)
    {
        this.triggerName = inCardPlaceType.ToString();
        StartCoroutine(PlayAnimCo());
    }

    private IEnumerator PlayAnimCo()
    {
        this.animator.ResetTrigger(this.triggerName);
        yield return null;

        this.animator.SetTrigger(this.triggerName);
        this.animator.transform.SetParent(UIDragOverlay.IN.transform);

        yield return new WaitForSeconds(1f);

        this.animator.transform.SetParent(this.animParent);
        this.animator.transform.localPosition = Vector3.zero;
    }
}