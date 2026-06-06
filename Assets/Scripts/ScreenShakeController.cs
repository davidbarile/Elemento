using UnityEngine;

public class ScreenShakeController : MonoBehaviour
{
    public static ScreenShakeController IN;
    public enum EScreenShakeMode
    {
        Small,
        Medium,
        Large,
        Huge,
        Count
    }

    [SerializeField] private Animator screenShakeAnimator;

    public void DoScreenShake(int inIndex)
    {
        DoScreenShake((EScreenShakeMode)inIndex);
    }

    public void DoScreenShake(EScreenShakeMode inCameraShakeMode)
    {
        this.screenShakeAnimator.SetTrigger(inCameraShakeMode.ToString());
    }
}