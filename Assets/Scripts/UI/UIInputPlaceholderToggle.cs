using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class UIInputPlaceholderToggle : MonoBehaviour
{
    private TMP_InputField inputField;
    private Graphic placeholder;

    public void Initialize(TMP_InputField inInputField)
    {
        if (this.inputField != null && this.isActiveAndEnabled) Unsubscribe();

        this.inputField = inInputField;
        this.placeholder = this.inputField != null ? this.inputField.placeholder : null;

        if (this.isActiveAndEnabled) Subscribe();
        UpdatePlaceholder();
    }

    private void OnEnable()
    {
        Subscribe();
        UpdatePlaceholder();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (this.inputField == null) return;

        this.inputField.onSelect.AddListener(HandleSelect);
        this.inputField.onDeselect.AddListener(HandleDeselect);
        this.inputField.onEndEdit.AddListener(HandleEndEdit);
    }

    private void Unsubscribe()
    {
        if (this.inputField == null) return;

        this.inputField.onSelect.RemoveListener(HandleSelect);
        this.inputField.onDeselect.RemoveListener(HandleDeselect);
        this.inputField.onEndEdit.RemoveListener(HandleEndEdit);
    }

    private void HandleSelect(string inText)
    {
        SetPlaceholderVisible(false);
    }

    private void HandleDeselect(string inText)
    {
        SetPlaceholderVisible(string.IsNullOrEmpty(inText));
    }

    private void HandleEndEdit(string inText)
    {
        SetPlaceholderVisible(!this.inputField.isFocused && string.IsNullOrEmpty(inText));
    }

    private void UpdatePlaceholder()
    {
        if (this.inputField == null) return;

        SetPlaceholderVisible(!this.inputField.isFocused && string.IsNullOrEmpty(this.inputField.text));
    }

    private void SetPlaceholderVisible(bool inIsVisible)
    {
        if (this.placeholder != null) this.placeholder.enabled = inIsVisible;
    }
}
