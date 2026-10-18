using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIOnlineLobbyPanelTests
{
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
        Assert.That(typeof(OnlineUiPrefabBuilder).GetMethod("ConfigureLobbyPrefab",
            BindingFlags.Static | BindingFlags.NonPublic), Is.Not.Null);
    }

    private static void AssertSerializedField(string inName, Type inType)
    {
        FieldInfo field = typeof(UIOnlineLobbyPanel).GetField(inName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        Assert.That(field.FieldType, Is.EqualTo(inType));
        Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
    }
}
