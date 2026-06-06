using UnityEngine;

public class UISplashScreen : UIPanelBase
{
    public static UISplashScreen IN;

    [SerializeField] private GameManager gameManager;

    private void Awake()
    {
        this.gameManager.Init();
    }

    public override void Show()
    {
        base.Show();
        GameManager.IN.SetGamePaused(false);
    }

    public override void Hide()
    {
        base.Hide();

        this.gameObject.SetActive(false);
    }

    public void HandleEditCardsButtonPress()
    {
        GameManager.IN.SetGamePaused(true);
        UIDeckEditor.IN.Show(()=> { Show(); });
    }

    #region Animation Sounds
    public void PlayExplosionSound()
    {
        AudioManager.IN.PlayExplosionSound();
    }

    public void PlayShowElementsClip()
    {
        AudioManager.IN.PlayClip(AudioManager.IN.ShowElementsAnimClip, .12f, .75f);
    }

    public void PlayCardWhooshSound()
    {
        AudioManager.IN.PlayCardSwooshSound(0, .7f);
    }

    public void PlayPopInSound()
    {
        AudioManager.IN.PlayPopSound(.5f);
    }

    public void PlayPopInSoundLow()
    {
        AudioManager.IN.PlayPopSound(.5f, .75f);
    }

    public void PlayPopInSoundMed()
    {
        AudioManager.IN.PlayPopSound(.3f, .9f);
    }

    public void PlayPopInSoundHi()
    {
        AudioManager.IN.PlayPopSound(.3f, 1.05f);
    }

    public void PlayLetterClip0()
    {
        PlayLetterClip(.5f);
    }

    public void PlayLetterClip1()
    {
        PlayLetterClip(.6f);
    }

    public void PlayLetterClip2()
    {
        PlayLetterClip(.7f);
    }

    public void PlayLetterClip3()
    {
        PlayLetterClip(.8f);
    }

    public void PlayLetterClip4()
    {
        PlayLetterClip(.9f);
    }

    public void PlayLetterClip5()
    {
        PlayLetterClip(1f);
    }
    public void PlayLetterClip6()
    {
        PlayLetterClip(1.1f);
    }

    public void PlayLetterClip7()
    {
        PlayLetterClip(1.2f);
    }

    private void PlayLetterClip(float inPitch)
    {
        AudioManager.IN.PlayClip(AudioManager.IN.LetterAnimClip, .2f, inPitch);
    }
    #endregion
}