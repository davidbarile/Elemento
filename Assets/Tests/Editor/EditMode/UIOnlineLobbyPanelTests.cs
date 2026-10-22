using System;
using System.Reflection;
using Elemento.Domain.Net;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class UIOnlineLobbyPanelTests
{
    private GameObject canvasObject;
    private GameObject lobbyObject;
    private GameObject localizationObject;
    private OnlineMatchClient client;

    [SetUp]
    public void SetUp()
    {
        PlayerData.Data = new PlayerData { LanguageIndex = 0 };
        this.localizationObject = new GameObject("Localization");
        LocalizationManager localization = this.localizationObject.AddComponent<LocalizationManager>();
        LocalizationManager.IN = localization;
        LOC.IN = localization;
    }

    [TearDown]
    public void TearDown()
    {
        if (this.lobbyObject != null)
            UnityEngine.Object.DestroyImmediate(this.lobbyObject);
        if (this.canvasObject != null)
            UnityEngine.Object.DestroyImmediate(this.canvasObject);
        if (this.localizationObject != null)
            UnityEngine.Object.DestroyImmediate(this.localizationObject);
        this.client?.Dispose();
        UIOnlineLobbyPanel.IN = null;
        LocalizationManager.IN = null;
        LOC.IN = null;
        PlayerData.Data = null;
        OnlineMatchContext.Clear();
    }

    [Test]
    public void CancelGameLocalization_HasEnglishAndSpanishEntries()
    {
        GameObject localizationObject = new("Localization");
        try
        {
            LocalizationManager localization = localizationObject.AddComponent<LocalizationManager>();

            Assert.That(localization.OnlineCancelGame,
                Is.EqualTo(new[] { "Cancel game", "Cancelar partida" }));
            Assert.That(localization.OnlineCancelGameTitle,
                Is.EqualTo(new[] { "Cancel this game?", "¿Cancelar esta partida?" }));
            Assert.That(localization.OnlineCancelGameMessage, Is.EqualTo(new[]
            {
                "The invite code will stop working and you can create or join a new game.",
                "El código de invitación dejará de funcionar y podrás crear o unirte a otra partida."
            }));
            Assert.That(localization.OnlineGameCancelled,
                Is.EqualTo(new[] { "Game cancelled", "Partida cancelada" }));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(localizationObject);
        }
    }

    [Test]
    public void CancelGamePrefabFields_ArePrivateSerializedFields()
    {
        AssertSerializedField("cancelGameButton", typeof(Button));
        AssertSerializedField("cancelGameText", typeof(TextMeshProUGUI));
        AssertSerializedField("cancelGameButtonRoot", typeof(GameObject));
        AssertSerializedField("shareButtonRoot", typeof(GameObject));
        AssertSerializedField("shareButton", typeof(Button));
        AssertSerializedField("shareText", typeof(TextMeshProUGUI));
        Assert.That(typeof(OnlineUiPrefabBuilder).GetMethod("ConfigureLobbyPrefab",
            BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
    }

    [Test]
    public void RestoredWaitingMatch_ShowsCancelGameButtonOnScreen()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Online Lobby Panel.prefab");
        Assert.That(prefab, Is.Not.Null);

        this.canvasObject = new GameObject("Test Canvas", typeof(RectTransform), typeof(Canvas));
        Canvas canvas = this.canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform canvasRect = this.canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1080f, 1920f);

        this.lobbyObject = PrefabUtility.InstantiatePrefab(prefab, canvasRect) as GameObject;
        Assert.That(this.lobbyObject, Is.Not.Null);
        RectTransform lobbyRect = this.lobbyObject.GetComponent<RectTransform>();
        lobbyRect.anchorMin = Vector2.zero;
        lobbyRect.anchorMax = Vector2.one;
        lobbyRect.sizeDelta = Vector2.zero;
        lobbyRect.anchoredPosition = Vector2.zero;
        this.lobbyObject.SetActive(true);

        UIOnlineLobbyPanel panel = this.lobbyObject.GetComponent<UIOnlineLobbyPanel>();
        Assert.That(panel, Is.Not.Null);
        var transport = new InMemoryMatchTransport("Host");
        this.client = new OnlineMatchClient(transport);
        this.client.CreateAsync(InitialMatchFactory.Create(inSeed: 17)).GetAwaiter().GetResult();
        Assert.That(this.client.CurrentState.Status, Is.EqualTo("waiting"));
        SetField(panel, "client", this.client);

        Invoke(panel, "SetResumeLoading", true);
        Invoke(panel, "SetResumeLoading", false);
        Invoke(panel, "SetBusy", false);
        Canvas.ForceUpdateCanvases();

        Button cancelGameButton = GetField<Button>(panel, "cancelGameButton");
        Button createButton = GetField<Button>(panel, "createButton");
        Button joinButton = GetField<Button>(panel, "joinButton");
        Assert.That(cancelGameButton.gameObject.activeInHierarchy, Is.True);
        RectTransform cancelRect = (RectTransform)cancelGameButton.transform;
        Assert.That(cancelRect.rect.width, Is.GreaterThan(0f));
        Assert.That(cancelRect.rect.height, Is.GreaterThan(0f));

        Vector3[] canvasCorners = new Vector3[4];
        Vector3[] cancelCorners = new Vector3[4];
        canvasRect.GetWorldCorners(canvasCorners);
        cancelRect.GetWorldCorners(cancelCorners);
        for (int i = 0; i < cancelCorners.Length; ++i)
        {
            Assert.That(cancelCorners[i].x, Is.InRange(canvasCorners[0].x, canvasCorners[2].x));
            Assert.That(cancelCorners[i].y, Is.InRange(canvasCorners[0].y, canvasCorners[2].y));
        }

        Assert.That(createButton.gameObject.activeInHierarchy, Is.False);
        Assert.That(joinButton.gameObject.activeInHierarchy, Is.False);

        Button shareButton = GetField<Button>(panel, "shareButton");
        Assert.That(shareButton.gameObject.activeInHierarchy, Is.False);
        SetField(panel, "inviteCode", "ABC123");
        Invoke(panel, "SetBusy", false);
        Canvas.ForceUpdateCanvases();
        Assert.That(shareButton.gameObject.activeInHierarchy, Is.True);
        Assert.That(shareButton.interactable, Is.True);
        Assert.That(cancelGameButton.gameObject.activeInHierarchy, Is.True);
    }

    private static void AssertSerializedField(string inName, Type inType)
    {
        FieldInfo field = typeof(UIOnlineLobbyPanel).GetField(inName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        Assert.That(field.FieldType, Is.EqualTo(inType));
        Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
    }

    private static T GetField<T>(object inTarget, string inFieldName)
    {
        FieldInfo field = inTarget.GetType().GetField(inFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(inTarget);
    }

    private static void SetField(object inTarget, string inFieldName, object inValue)
    {
        FieldInfo field = inTarget.GetType().GetField(inFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(inTarget, inValue);
    }

    private static void Invoke(object inTarget, string inMethodName, bool inValue)
    {
        MethodInfo method = inTarget.GetType().GetMethod(inMethodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(inTarget, new object[] { inValue });
    }
}
