using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ButtonSound : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    private enum ButtonAudioClip
    {
        Click,
        KeyPress,
        Custom
    }

    [SerializeField] private ButtonAudioClip btnAudioClip;
    [Space(10)]
    [SerializeField] private AudioClip customAudioClip;
    [Range(0, 1f)] [SerializeField] private float customClipVolume = 1;
    [Range(0, 3f)] [SerializeField] private float customClipPitch = 1;

    [Space(10)]
    [SerializeField] private bool shouldDoSquish = true;

    private Button button;

    private Toggle toggle;

    private Vector3 initScale;

    private void Start()
    {
        this.button = this.GetComponent<Button>();

        if (this.button != null)
        {

            this.initScale = this.button.transform.localScale;

            if (this.customAudioClip != null)
            {
                this.button.onClick.AddListener(() =>
                {
                    if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
                    AudioManager.IN.PlayClip(this.customAudioClip, this.customClipVolume, this.customClipPitch);
                });
            }
            else if (this.btnAudioClip == ButtonAudioClip.Click)
            {
                this.button.onClick.AddListener(() =>
                {
                    if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
                    AudioManager.IN.PlayButtonSound();
                });
            }
        }

        this.toggle = this.GetComponent<Toggle>();

        if (this.toggle != null)
        {
            if (this.customAudioClip != null)
            {
                this.toggle.onValueChanged.AddListener((bool inIsOn) =>
                {
                    if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
                    AudioManager.IN.PlayClip(this.customAudioClip, this.customClipVolume, this.customClipPitch);
                });
            }
            else if (this.btnAudioClip == ButtonAudioClip.Click)
            {
                this.toggle.onValueChanged.AddListener((bool inIsOn) =>
                {
                    if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
                    AudioManager.IN.PlayButtonSound();
                });
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if(this.button != null)
        {
            if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
            if (this.shouldDoSquish && this.button.isActiveAndEnabled)
                this.button.transform.localScale = this.initScale * .95f;
        }

        if (this.toggle != null)
        {
            if (GameManager.ShouldBlockInput || UIDraggable.ShouldBlockInput ) return;
            if (this.shouldDoSquish && this.toggle.isActiveAndEnabled)
                this.toggle.transform.localScale = this.initScale * .95f;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (this.button != null)
        {
            if (this.shouldDoSquish && this.button.isActiveAndEnabled)
                this.button.transform.localScale = this.initScale;
        }

        if (this.toggle != null)
        {
            if (this.shouldDoSquish && this.toggle.isActiveAndEnabled)
                this.toggle.transform.localScale = this.initScale;
        }
    }
}
