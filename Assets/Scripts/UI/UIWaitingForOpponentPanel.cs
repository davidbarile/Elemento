using TMPro;
using UnityEngine;

public sealed class UIWaitingForOpponentPanel : UIPanelBase
{
    public static UIWaitingForOpponentPanel IN;//singleton

    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private RectTransform spinnerTransform;
    [SerializeField] private float spinnerSpeed = -180f;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
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

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void RefreshText()
    {
        this.messageText.text = LocalizationManager.GetOnlineText(LocalizationManager.IN != null
            ? LocalizationManager.IN.OnlineWaitingForOpponent : null);
    }
}
