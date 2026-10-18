using System;
using Elemento.Domain.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIActiveGameRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI codeText;
    [SerializeField] private TextMeshProUGUI opponentText;
    [SerializeField] private Button rejoinButton;
    [SerializeField] private TextMeshProUGUI rejoinText;
    [SerializeField] private Button forfeitButton;
    [SerializeField] private TextMeshProUGUI forfeitText;

    private MyMatchData match;
    private Action<MyMatchData> onRejoin;
    private Action<MyMatchData> onForfeit;

    public string MatchId => this.match?.MatchId;

    private void Awake()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        RefreshListeners();
    }

    private void OnDestroy()
    {
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        RemoveListeners();
    }

    public void Configure(MyMatchData inMatch, Action<MyMatchData> inOnRejoin,
        Action<MyMatchData> inOnForfeit)
    {
        this.match = inMatch;
        this.onRejoin = inOnRejoin;
        this.onForfeit = inOnForfeit;
        LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
        LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        RefreshListeners();
        RefreshText();
    }

    public void SetInteractable(bool inIsInteractable)
    {
        if (this.rejoinButton != null)
            this.rejoinButton.interactable = inIsInteractable;
        if (this.forfeitButton != null)
            this.forfeitButton.interactable = inIsInteractable;
    }

    private void HandleLanguageChanged(int inLanguageIndex)
    {
        RefreshText();
    }

    private void HandleRejoinButtonPress()
    {
        this.onRejoin?.Invoke(this.match);
    }

    private void HandleForfeitButtonPress()
    {
        this.onForfeit?.Invoke(this.match);
    }

    private void RefreshListeners()
    {
        RemoveListeners();
        this.rejoinButton?.onClick.AddListener(HandleRejoinButtonPress);
        this.forfeitButton?.onClick.AddListener(HandleForfeitButtonPress);
    }

    private void RemoveListeners()
    {
        this.rejoinButton?.onClick.RemoveListener(HandleRejoinButtonPress);
        this.forfeitButton?.onClick.RemoveListener(HandleForfeitButtonPress);
    }

    private void RefreshText()
    {
        LocalizationManager localization = LocalizationManager.IN;
        if (localization == null || this.match == null)
            return;

        if (this.codeText != null)
        {
            this.codeText.richText = false;
            this.codeText.text = string.Format(
                LocalizationManager.GetOnlineText(localization.OnlineMatchCodeFormat),
                this.match.InviteCode ?? string.Empty);
        }

        if (this.opponentText != null)
        {
            this.opponentText.richText = false;
            this.opponentText.text = string.IsNullOrEmpty(this.match.OpponentName)
                ? LocalizationManager.GetOnlineText(localization.OnlineWaiting)
                : string.Format(LocalizationManager.GetOnlineText(localization.OnlineOpponentFormat),
                    this.match.OpponentName);
        }

        if (this.rejoinText != null)
            this.rejoinText.text = LocalizationManager.GetOnlineText(localization.OnlineRejoin);
        if (this.forfeitText != null)
            this.forfeitText.text = LocalizationManager.GetOnlineText(localization.OnlineForfeit);
    }
}
