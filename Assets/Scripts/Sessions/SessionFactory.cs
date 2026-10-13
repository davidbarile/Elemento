public static class SessionFactory
{
    public static IGameSession Create(SettingsManager.EPlayMode inPlayMode)
    {
        if (inPlayMode == SettingsManager.EPlayMode.PassAndPlay)
            return new PassAndPlaySession();

        if (inPlayMode == SettingsManager.EPlayMode.Multiplayer && OnlineMatchContext.IsActive)
            return new OnlineSession(OnlineMatchContext.LocalSeat, OnlineMatchContext.Client);

        return new LocalAiSession();
    }
}
