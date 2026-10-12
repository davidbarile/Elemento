public static class SessionFactory
{
    public static IGameSession Create(SettingsManager.EPlayMode inPlayMode)
    {
        if (inPlayMode == SettingsManager.EPlayMode.PassAndPlay)
            return new PassAndPlaySession();

        // Multiplayer intentionally falls back to local AI until remote sessions exist.
        return new LocalAiSession();
    }
}
