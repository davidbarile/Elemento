using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIWaitingForOpponentPanel : UIPanelBase
{
    public static UIWaitingForOpponentPanel IN;//singleton

    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private RectTransform spinnerTransform;
    [SerializeField] private float spinnerSpeed = -180f;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TextMeshProUGUI leaveText;

    private bool isOpponentAway;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        this.leaveButton?.onClick.AddListener(HandleLeaveButtonPress);
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        this.leaveButton?.onClick.RemoveListener(HandleLeaveButtonPress);
    }

    private void Update()
    {
        if (!this.IsShowing)
            return;

        this.spinnerTransform.Rotate(0f, 0f, this.spinnerSpeed * Time.unscaledDeltaTime);
    }

    public override void Show()
    {
        RefreshText();
        base.Show();
    }

    public void SetOpponentAway(bool inIsOpponentAway)
    {
        if (this.isOpponentAway == inIsOpponentAway)
            return;

        this.isOpponentAway = inIsOpponentAway;
        RefreshText();
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void HandleLeaveButtonPress()
    {
        GameManager.IN?.ShowLeaveGameConfirmation();
    }

    private void RefreshText()
    {
        LocalizationManager localization = LocalizationManager.IN;
        this.messageText.text = LocalizationManager.GetOnlineText(localization == null
            ? null : this.isOpponentAway
                ? localization.OnlineOpponentAway
                : localization.OnlineWaitingForOpponent);
        if (this.leaveText != null)
        {
            this.leaveText.text = LocalizationManager.GetOnlineText(
                localization == null ? null : localization.OnlineLeave);
        }
    }
}
