using Elemento.Networking.Supabase;
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

    [TestCase(0, SettingsManager.EPlayMode.AgainstAI)]
    [TestCase(1, SettingsManager.EPlayMode.PassAndPlay)]
    [TestCase(3, SettingsManager.EPlayMode.Multiplayer)]
    public void SplashPlayModeButton_MapsConfiguredValue(int inValue, SettingsManager.EPlayMode inExpected)
    {
        Assert.That(GameManager.TryGetPlayModeForButton(inValue, out SettingsManager.EPlayMode playMode), Is.True);
        Assert.That(playMode, Is.EqualTo(inExpected));
    }

    [Test]
    public void SplashPlayModeButton_RejectsUnknownValue()
    {
        Assert.That(GameManager.TryGetPlayModeForButton(2, out _), Is.False);
    }

    [Test]
    public void RandomUsername_IsValidAndChangesBetweenProfiles()
    {
        string first = SupabaseProfileClient.CreateRandomUsername();
        string second = SupabaseProfileClient.CreateRandomUsername();

        StringAssert.IsMatch("\\A[A-Za-z0-9_]{3,20}\\z", first);
        Assert.That(second, Is.Not.EqualTo(first));
    }

    private static DeserializeSettings CreateDeserializeSettings()
    {
        return new DeserializeSettings
        {
            RequireAllFieldsArePopulated = false
        };
    }
}
