using TMPro;
using UnityEngine;

public class UIAlertPanel : UIPanelBase
{
    public static UIAlertPanel IN;

    public enum EAlertMode
    {
        Warning,
        Notification
    }

    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField] private TextMeshProUGUI notificationText;
    [Space]
    [SerializeField] private GameObject warningDisplay;
    [SerializeField] private GameObject notificationDisplay;

    public void Show(string inMessage, float inDuration = 2f, EAlertMode inAlertMode = EAlertMode.Warning)
    {
        base.Show();

        this.warningDisplay.SetActive(inAlertMode == EAlertMode.Warning);
        this.notificationDisplay.SetActive(inAlertMode == EAlertMode.Notification);

        if(inAlertMode == EAlertMode.Warning)
            this.warningText.text = inMessage;
        else
            this.notificationText.text = inMessage;

        if (inDuration > 0)
            Invoke(nameof(Hide), inDuration);
    }

    public override void Hide()
    {
        CancelInvoke();
        base.Hide();
    }
}