using Leguar.TotalJSON;
using NUnit.Framework;

public class SettingsPersistenceTests
{
    [Test]
    public void PlayerData_RoundTripPreservesPlayModeAndAllowUndo()
    {
        PlayerData saved = new()
        {
            PlayMode = (int)SettingsManager.EPlayMode.Multiplayer,
            AllowUndo = true
        };

        PlayerData loaded = JSON.Serialize(saved).Deserialize<PlayerData>(CreateDeserializeSettings());

        Assert.That((SettingsManager.EPlayMode)loaded.PlayMode,
            Is.EqualTo(SettingsManager.EPlayMode.Multiplayer));
        Assert.That(loaded.AllowUndo, Is.True);
    }

    [Test]
    public void PlayerData_OldSaveWithoutNewSettingsUsesSafeDefaults()
    {
        PlayerData loaded = JSON.ParseString("{}").Deserialize<PlayerData>(CreateDeserializeSettings());

        Assert.That((SettingsManager.EPlayMode)loaded.PlayMode,
            Is.EqualTo(SettingsManager.EPlayMode.AgainstAI));
        Assert.That(loaded.AllowUndo, Is.False);
    }

    private static DeserializeSettings CreateDeserializeSettings()
    {
        return new DeserializeSettings
        {
            RequireAllFieldsArePopulated = false
        };
    }
}
