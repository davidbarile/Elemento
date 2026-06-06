using UnityEngine;
using UnityEngine.UI;

//[RequireComponent(typeof(Toggle))]
public class UISettingsTab : MonoBehaviour
{
    [Space]
    [SerializeField] private Toggle toggle;
    [Space]
    [SerializeField] private GameObject onState;
    [SerializeField] private GameObject offState;

    public bool IsOn => this.toggle.isOn;

    public void SetState(bool inIsOn, bool inShouldNotify = true)
    {
        if (inShouldNotify)
            this.toggle.isOn = inIsOn;
        else
            this.toggle.SetIsOnWithoutNotify(inIsOn);

        this.onState.SetActive(inIsOn);
        this.offState.SetActive(!inIsOn);
    }
}