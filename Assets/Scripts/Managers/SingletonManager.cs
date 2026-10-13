using UnityEngine;

public class SingletonManager : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private CardsManager cardsManager;
    [SerializeField] private BoardStateManager boardStateManager;
    [SerializeField] private SkinManager skinManager;
    [SerializeField] private SaveManager saveManager;
    [SerializeField] private SettingsManager settingsManager;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private LocalizationManager localizationManager;
    [Space]
    [SerializeField] private ScreenShakeController screenShakeController;
    [SerializeField] private UIGameBoard gameBoard;
    [SerializeField] private UISplashScreen splashScreen;
    [SerializeField] private UIDeckEditor deckEditor;
    [SerializeField] private UIDeckListPanel deckListPanel;
    [SerializeField] private UISettingsPanel settingsPanel;
    [SerializeField] private UITutorialPanel tutorialPanel;
    [SerializeField] private UIConfirmPanel confirmPanel;
    [SerializeField] private UIPowerCardPanel powerCardPanel;
    [SerializeField] private UIWinLosePanel winLosePanel;
    [SerializeField] private UIAlertPanel alertPanel;
    [SerializeField] private UISeatHandoffPanel seatHandoffPanel;
    [SerializeField] private UIUsernamePanel usernamePanel;
    [SerializeField] private UIOnlineLobbyPanel onlineLobbyPanel;

    public void Init()
    {
        GameManager.IN = this.gameManager;
        CardsManager.IN = this.cardsManager;
        BoardStateManager.IN = this.boardStateManager;
        SkinManager.IN = this.skinManager;
        SaveManager.IN = this.saveManager;
        SettingsManager.IN = this.settingsManager;
        AudioManager.IN = this.audioManager;
        LocalizationManager.IN = this.localizationManager;

        ScreenShakeController.IN = this.screenShakeController;
        UIGameBoard.IN = this.gameBoard;
        UISplashScreen.IN = this.splashScreen;
        UIDeckEditor.IN = this.deckEditor;
        UIDeckListPanel.IN = this.deckListPanel;
        UISettingsPanel.IN = this.settingsPanel;
        UITutorialPanel.IN = this.tutorialPanel;
        UIConfirmPanel.IN = this.confirmPanel;
        UIPowerCardPanel.IN = this.powerCardPanel;
        UIWinLosePanel.IN = this.winLosePanel;
        UIAlertPanel.IN = this.alertPanel;
        UISeatHandoffPanel.IN = this.seatHandoffPanel;
        UIUsernamePanel.IN = this.usernamePanel;
        UIOnlineLobbyPanel.IN = this.onlineLobbyPanel;

        UIDeckEditor.IN.SetVisible(false);
        UIDeckListPanel.IN.SetVisible(false);
        UISettingsPanel.IN.SetVisible(false);
        UIConfirmPanel.IN.SetVisible(false);
        UIPowerCardPanel.IN.SetVisible(false);
        UIWinLosePanel.IN.SetVisible(false);
        UIAlertPanel.IN.SetVisible(false);
        UITutorialPanel.IN.SetVisible(false);
        UISeatHandoffPanel.IN.SetVisible(false);
        UIUsernamePanel.IN?.SetVisible(false);
        UIOnlineLobbyPanel.IN?.SetVisible(false);
    }
}
