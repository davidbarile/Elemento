using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LocalizationItem : MonoBehaviour
{
    public int MaxCharactersBeforeScaleDown;

    public string[] localizedTexts = new string[0];

    private Text text;
    private TextMeshProUGUI textMeshProUGUI;

    private bool isTMPro;

    private void Start()
    {
        this.text = this.GetComponent<Text>();

        this.isTMPro = this.text == null;

        if(this.isTMPro)
            this.textMeshProUGUI = this.GetComponent<TextMeshProUGUI>();

        LocalizationManager.IN.RegisterLocalizationItem(this);
    }

    public string GetText()
    {
        if (this.isTMPro)
            return this.textMeshProUGUI.text;
        else
            return this.text.text;
    }

    public void SetText(string inString)
    {
        if (string.IsNullOrEmpty(inString))
            inString = "<!>";

        if (this.isTMPro)
            this.textMeshProUGUI.text = inString;
        else
            this.text.text = inString;

        float scale = 1; 

        if( this.MaxCharactersBeforeScaleDown > 0 && this.localizedTexts.Length > 0 )
        {
            float maxChars = Mathf.Max(this.MaxCharactersBeforeScaleDown, (float)this.localizedTexts[0].Length);
            scale = Mathf.Min(1, maxChars / (float)this.localizedTexts[(int)LocalizationManager.IN.SelectedLanguage].Length);
        }

        if (this.isTMPro)
            this.textMeshProUGUI.rectTransform.localScale = new Vector3( scale, scale, 1);
        else
            this.text.rectTransform.localScale = new Vector3(scale, scale, 1);
    }
}
