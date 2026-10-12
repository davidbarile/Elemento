using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UISeatHandoffPanel : MonoBehaviour
{
    private TextMeshProUGUI messageText;
    private Button continueButton;
    private Action onContinue;

    public static UISeatHandoffPanel Create(Transform inParent)
    {
        var panelObject = new GameObject("Pass And Play Handoff Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        panelObject.transform.SetParent(inParent, false);

        var panel = panelObject.AddComponent<UISeatHandoffPanel>();
        panel.Initialize();
        return panel;
    }

    public void Initialize()
    {
        var rectTransform = this.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        var background = this.GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, .94f);

        this.continueButton = this.GetComponent<Button>();
        this.continueButton.targetGraphic = background;
        this.continueButton.onClick.AddListener(HandleContinueButtonPress);

        var labelObject = new GameObject("Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(this.transform, false);

        var labelRectTransform = labelObject.GetComponent<RectTransform>();
        labelRectTransform.anchorMin = new Vector2(.1f, .1f);
        labelRectTransform.anchorMax = new Vector2(.9f, .9f);
        labelRectTransform.offsetMin = Vector2.zero;
        labelRectTransform.offsetMax = Vector2.zero;

        this.messageText = labelObject.GetComponent<TextMeshProUGUI>();
        this.messageText.alignment = TextAlignmentOptions.Center;
        this.messageText.color = Color.white;
        this.messageText.fontSize = 48;
        this.messageText.raycastTarget = false;

        Hide();
    }

    public void Show(string inMessage, Action inOnContinue)
    {
        this.messageText.text = inMessage;
        this.onContinue = inOnContinue;
        this.transform.SetAsLastSibling();
        this.gameObject.SetActive(true);
    }

    public void Hide()
    {
        this.onContinue = null;
        this.gameObject.SetActive(false);
    }

    private void HandleContinueButtonPress()
    {
        var callback = this.onContinue;
        Hide();
        callback?.Invoke();
    }

    private void OnDestroy()
    {
        if (this.continueButton != null)
            this.continueButton.onClick.RemoveListener(HandleContinueButtonPress);
    }
}
