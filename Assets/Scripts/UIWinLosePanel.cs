using System;
using System.Collections.Generic;
using UnityEngine;
using static CardData;
using static GameManager;

public class UIWinLosePanel : UIConfirmPanel
{
    public static new UIWinLosePanel IN;

    [SerializeField] private GameObject winTitle;
    [SerializeField] private GameObject loseTitle;
    [SerializeField] private GameObject tieTitle;
    [Space]
    [SerializeField] private UIWrapupItem[] wrapupItems;
    [Space]
    [SerializeField] private Animation showAnim;

    private EWinLoseState winLoseState;

    public void Show(EWinLoseState inWinLoseState, string inMessage, List<WrapupItemData> inWrapupDatas, Action inOnConfirm, Action inOnCancel)
    {
        this.onConfirm = inOnConfirm;
        this.onCancel = inOnCancel;

        this.winLoseState = inWinLoseState;

        bool shouldShowBothButtons = inOnConfirm != null;

        this.cancelButton.gameObject.SetActive(shouldShowBothButtons);
        this.confirmButton.gameObject.SetActive(shouldShowBothButtons);
        if (this.dismissButton != null)
           this.dismissButton.gameObject.SetActive(!shouldShowBothButtons);

        this.winTitle.SetActive(inWinLoseState == EWinLoseState.Won);
        this.loseTitle.SetActive(inWinLoseState == EWinLoseState.Lost);
        this.tieTitle.SetActive(inWinLoseState == EWinLoseState.Tie);

        base.Show();

        this.messageText.text = inMessage;

        for (int i = 0; i < inWrapupDatas.Count; i++)
        {
            var data = inWrapupDatas[i];
            var item = this.wrapupItems[i];

            item.PlayerText.text = data.PlayerName;
            item.TotalPointsDisplay.SetTotalPoints(data.TotalPoints);

            for (int j = 0; j < data.ElementPoints.Count; j++)
            {
                int numPoints = data.ElementPoints[j];
                var pointsDisplay = item.PointsDisplays[j];

                bool isZoneDestroyed = numPoints == -1;

                item.SetDestroyedState(j, isZoneDestroyed);

                item.TotalPointsDisplay.SetElementVisible((EElementType) j + 1, numPoints);

                pointsDisplay.SetText(numPoints, SettingsManager.IN.ElementZonePointsRequiredToActivate);
            }
        }

        this.showAnim.Play();
    }

    public void HandleReplayButtonPress()
    {
        Hide();

        GameManager.IN.HandleReplayButtonPress();
    }

    public void PlayWinLoseSound()
    {
        if(this.winLoseState == EWinLoseState.Won)
        {
            AudioManager.IN.PlayClip(AudioManager.IN.WinGameSound, .7f);
        }
        else if(this.winLoseState == EWinLoseState.Lost)
        {
            AudioManager.IN.PlayClip(AudioManager.IN.LoseGameSound, .7f);
        }
    }
}