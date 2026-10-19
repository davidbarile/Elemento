using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager IN;

    public enum ERejectReason
    {
        None,
        InvalidDropLocation,
        HandIsFull,
        OutOfMoves,
        DestroyedZone,
        ElementZoneFull,
        GameDeck,
        CardOnOpponentBoard,
        CardInOpponentHand,
        DoesNotContainElement,
        BombDoesNotContainElement,
        BombOnOwnZone,
        BombOnEmpty,
        BombOnDestroyed,
        ZoneIsFrozen,
        HealNonEmptyZone,
        ZoneHasNoDamage,
        MoveToSameCell,
        TutorialBlockedCell
    }

    [SerializeField] private AudioSource[] musicSources;
    [SerializeField] private float audioFadeDuration = 1;

    [Header("Audio Clips")]
    public AudioClip ButtonClickClip;
    public AudioClip IncrementCounterClip;
    public AudioClip ExplosionClip;
    public AudioClip LightningClip;
    public AudioClip HealClip;
    public AudioClip PopClip;
    public AudioClip LetterAnimClip;
    public AudioClip LetterIdleAnimClip;
    public AudioClip ShowElementsAnimClip;
    public AudioClip ActivateZoneClip;
    public AudioClip DeactivateZoneClip;
    public AudioClip UseActivationPowerClip;
    public AudioClip RemoveCardClip;
    public AudioClip FreezeZoneClip;
    public AudioClip UnfreezeZoneClip;
    public AudioClip WinGameSound;
    public AudioClip LoseGameSound;
    public AudioClip MeltIceClip;
    public AudioClip TutorialBlockClip;

    [Space]
    public AudioClip[] DingClips;
    public AudioClip[] ZapClips;
    public AudioClip[] RewardClips;
    public AudioClip[] CardDropClips;
    [Space]
    public AudioClip[] ErrorSoundClips;
    [Space]
    public AudioClip[] CardSwooshClips;


    [Space(10)]
    [SerializeField] private float buttonVolume = 1;
    [SerializeField] private float buttonPitch = 1;
    [Space(10)]
    [SerializeField] private float keyPressVolume = 1;

    private List<AudioSource> audioSources = new List<AudioSource>();
    private int audioSourceIndex;
    private int recursionCounter;

    private Tween[] musicTweens = { null, null };
    private Tween[] ambientTweens = { null, null };

    private bool isDeckEditMode;

    public void Init()
    {
        for (int i = 0; i < this.musicSources.Length; i++)
        {
            this.musicSources[i].volume = 0;
            this.musicSources[i].ignoreListenerVolume = true;
        }

        ApplySavedEffectsVolume();
        SetAudioMode(true);
        
        //dynamically create audio sources
        for (int i = 0; i < 8; ++i)
        {
            AudioSource a = this.gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;

            this.audioSources.Add(a);
        }
    }

    public void SetAudioMode(bool inIsDeckEditMode)
    {
        if (inIsDeckEditMode == this.isDeckEditMode)
            return;

        this.isDeckEditMode = inIsDeckEditMode;

        KillAudioTweens();

        if (inIsDeckEditMode)
        {
            this.musicSources[0].Play();

            this.musicTweens[0] = this.musicSources[0].DOFade(PlayerData.Data.MusicVolume, this.audioFadeDuration);

            if(this.musicSources.Length > 1)
            {
                this.musicTweens[1] = this.musicSources[1].DOFade(0, this.audioFadeDuration);
                this.musicTweens[1].onComplete = () => { this.musicSources[1].Pause(); };
            }
        }
        else
        {
            if (this.musicSources.Length > 1)
                this.musicSources[1].Play();

            this.musicTweens[0] = this.musicSources[0].DOFade(0, this.audioFadeDuration);
            this.musicTweens[0].onComplete = () => { this.musicSources[0].Pause(); };

            if (this.musicSources.Length > 1)
                this.musicTweens[1] = this.musicSources[1].DOFade(PlayerData.Data.MusicVolume, this.audioFadeDuration);
        }
    }

    public void KillAudioTweens()
    {
        for (int i = 0; i < this.musicSources.Length; i++)
        {
            this.musicTweens[i]?.Kill();
            this.ambientTweens[i]?.Kill();
        }
    }

    public void StopMusicForQuit()
    {
        KillAudioTweens();

        for (int i = 0; i < this.musicSources.Length; i++)
        {
            this.musicSources[i].volume = 0;
        }

        AudioListener.volume = 0;
    }

    public AudioSource PlayClip(AudioClip inClip)
    {
        return PlayClip(inClip, 1, 1, 0);
    }

    public AudioSource PlayClip(AudioClip inClip, float inVolume)
    {
        return PlayClip(inClip, inVolume, 1, 0);
    }

    public AudioSource PlayClip(AudioClip inClip, float inVolume, float inPitch)
    {
        return PlayClip(inClip, inVolume, inPitch, 0);
    }

    public AudioSource PlayClip(AudioClip inClip, float inVolume, float inPitch, float inDelay)
    {
        if (inClip != null)
        {
            AudioSource audioSource = this.audioSources[this.audioSourceIndex];

            if (!audioSource.isPlaying)
            {
                audioSource.clip = inClip;
                audioSource.volume = inVolume;
                audioSource.pitch = inPitch;
                audioSource.PlayDelayed(inDelay);

                ++this.audioSourceIndex;
                this.audioSourceIndex %= this.audioSources.Count;

                this.recursionCounter = 0;

                return audioSource;
            }
            else
            {
                if (this.recursionCounter < this.audioSources.Count) //find next in list
                {
                    ++this.recursionCounter;

                    ++this.audioSourceIndex;
                    this.audioSourceIndex %= this.audioSources.Count;
                }
                else //entire list is exhausted, so add a new one and play it
                {
                    audioSource = gameObject.AddComponent<AudioSource>();

                    this.audioSources.Add(audioSource);

                    this.audioSourceIndex = this.audioSources.Count - 1;//select it


                }

                PlayClip(inClip, inVolume, inPitch, inDelay);

                return audioSource;
            }
        }
        else
        {
            Debug.Log("AudioManager.PlayClip()   inClip is null!");
            return null;
        }
    }

    public void PlayButtonSound()
    {
        float pitch = Random.Range(this.buttonPitch - .03f, this.buttonPitch + .03f);
        PlayClip(this.ButtonClickClip, this.buttonVolume, pitch);
    }

    public void PlayRejectSound(ERejectReason inRejectReason)
    {
        if (inRejectReason == ERejectReason.None) return;

        float pitch = Random.Range(1.07f, 1.1f);

        switch (inRejectReason)
        {
            case ERejectReason.HandIsFull:
            case ERejectReason.DestroyedZone:
            case ERejectReason.BombOnDestroyed:
            case ERejectReason.HealNonEmptyZone:
            case ERejectReason.ZoneHasNoDamage:
            case ERejectReason.ZoneIsFrozen:
                PlayClip(this.ErrorSoundClips[2], .5f, pitch);
                break;

            case ERejectReason.OutOfMoves:
                PlayClip(this.ErrorSoundClips[1], .5f, pitch);
                break;

            case ERejectReason.GameDeck:
            case ERejectReason.CardOnOpponentBoard:
            case ERejectReason.CardInOpponentHand:
            case ERejectReason.BombOnOwnZone:
            case ERejectReason.BombOnEmpty:
            case ERejectReason.MoveToSameCell:
            case ERejectReason.TutorialBlockedCell:
                PlayClip(this.ErrorSoundClips[3], .5f, pitch);
                break;

            case ERejectReason.ElementZoneFull:
            case ERejectReason.DoesNotContainElement:
            case ERejectReason.BombDoesNotContainElement:
                PlayClip(this.ErrorSoundClips[0], .5f, pitch);
                break;

            default:
                      PlayClip(this.ErrorSoundClips[0], .5f, pitch);
                break;
        }
    }

    public void PlayExplosionSound()
    {
        PlayExplosionSound(.5f);
    }

    public void PlayExplosionSound(float inVolume, float inBasePitch = 1)
    {
        float pitch = Random.Range(inBasePitch - .025f, inBasePitch + .025f);
        PlayClip(this.ExplosionClip, inVolume, pitch);
    }

    public void PlayCardSwooshSound(int inIndex, float inPitch = -1, float inVolume = .25f)
    {
        if (inPitch == -1)
            inPitch = Random.Range(.97f, 1.03f);

        PlayClip(this.CardSwooshClips[inIndex], inVolume, inPitch);
    }

    public void PlayCardDropSound(int inIndex, float inVolume = .5f, float inPitch = -1)
    {
        if (inPitch == -1)
            inPitch = Random.Range(.97f, 1.03f);

        PlayClip(this.CardDropClips[inIndex], inVolume, inPitch);
    }

    public void PlayPopSound(float inVolume, float inPitch = -1)
    {
        if(inPitch == -1)
            inPitch = Random.Range(.97f, 1.03f);

        PlayClip(this.PopClip, inVolume, inPitch);
    }

    public void SetMusicVolume(float inValue)
    {
        //PlayerData.Data.MusicVolume = inValue;
        for (int i = 0; i < this.musicSources.Length; i++)
        {
            this.musicSources[i].volume = inValue;
        }
    }

    public void SetEffectsVolume(float inValue)
    {
        //PlayerData.Data.EffectsVolume = inValue;
        AudioListener.volume = inValue;
    }

    public void ApplySavedEffectsVolume()
    {
        SetEffectsVolume(SettingsManager.IN != null
            ? SettingsManager.IN.EffectsVolume
            : PlayerData.Data?.EffectsVolume ?? 0f);
    }
}
