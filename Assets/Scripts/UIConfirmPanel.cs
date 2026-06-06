using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIConfirmPanel : UIPanelBase
{
    public static UIConfirmPanel IN;//singleton

    [SerializeField] protected TextMeshProUGUI titleText;
    [SerializeField] protected TextMeshProUGUI messageText;
    [SerializeField] protected TextMeshProUGUI cancelButtonText;
    [SerializeField] protected TextMeshProUGUI confirmButtonText;

    [Space]
    [SerializeField] protected Button cancelButton;
    [SerializeField] protected Button confirmButton;
    [SerializeField] protected Button dismissButton;

    protected Action onConfirm;
    protected Action onCancel;

    public void Show(string inMessage, Action inOnConfirm, Action inOnCancel = null)
    {
        string confirm = LOC.IN.Confirm[LOC.LI];
        Show(confirm, inMessage, inOnConfirm, inOnCancel);
    }

    public void Show(string inTitle, string inMessage, Action inOnConfirm = null, Action inOnCancel = null, float inDelayToAutoHide = -1)
    {
        string confirm = LOC.IN.Cancel[LOC.LI];
        string cancel = LOC.IN.Confirm[LOC.LI];
        Show(inTitle, inMessage, inOnConfirm, inOnCancel, confirm, cancel, inDelayToAutoHide);
    }

    public void Show(string inTitle, string inMessage, Action inOnConfirm, Action inOnCancel, string inCancelButtonString, string inConfirmButtonString, float inDelayToAutoHide = -1)
    {
        this.onConfirm = inOnConfirm;
        this.onCancel = inOnCancel;

        bool shouldShowBothButtons = inOnConfirm != null;

        this.cancelButton.gameObject.SetActive(shouldShowBothButtons);
        this.confirmButton.gameObject.SetActive(shouldShowBothButtons);
        if (this.dismissButton != null)
            this.dismissButton.gameObject.SetActive(!shouldShowBothButtons);

        this.titleText.text = inTitle;
        this.messageText.text = inMessage;

        this.cancelButtonText.text = inCancelButtonString;
        this.confirmButtonText.text = inConfirmButtonString;

        Show();

        if (inDelayToAutoHide > 0)
            Invoke(nameof(HandleConfirmButtonPress), inDelayToAutoHide);
        else
            GameManager.IN.SetGamePaused(true);
    }

    public void HandleCancelButtonPress()
    {
        Hide();

        this.onCancel?.Invoke();
        this.onCancel = null;
    }

    public void HandleConfirmButtonPress()
    {
        Hide();

        this.onConfirm?.Invoke();
        this.onConfirm = null;
    }

    public override void Hide()
    {
        if(!UISplashScreen.IN.IsShowing)
            GameManager.IN.SetGamePaused(false);

        base.Hide();
    }
}