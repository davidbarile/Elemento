using UnityEngine;

public class SplashLetterAnimSounds : MonoBehaviour
{
    public void PlayLetterIdleClip0()
    {
        PlayLetterIdleClip(1f);
    }

    public void PlayLetterIdleClip1()
    {
        PlayLetterIdleClip(1.1f);
    }

    public void PlayLetterIdleClip2()
    {
        PlayLetterIdleClip(1.2f);
    }

    public void PlayLetterIdleClip3()
    {
        PlayLetterIdleClip(1.3f);
    }

    public void PlayLetterIdleClip4()
    {
        PlayLetterIdleClip(1.4f);
    }

    public void PlayLetterIdleClip5()
    {
        PlayLetterIdleClip(1.5f);
    }
    public void PlayLetterIdleClip6()
    {
        PlayLetterIdleClip(1.6f);
    }

    public void PlayLetterIdleClip7()
    {
        PlayLetterIdleClip(1.7f);
    }

    private void PlayLetterIdleClip(float inPitch)
    {
        AudioManager.IN.PlayClip(AudioManager.IN.LetterIdleAnimClip, .1f, inPitch);
    }
}