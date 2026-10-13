using System;
using Elemento.Domain.Net;

public static class OnlineMatchContext
{
    public static OnlineMatchClient Client { get; private set; }
    public static int LocalSeat => OnlineMatchContext.Client == null ? -1 : OnlineMatchContext.Client.LocalSeat;
    public static bool IsActive => OnlineMatchContext.Client != null;

    public static void Set(OnlineMatchClient inClient)
    {
        if (inClient == null)
            throw new ArgumentNullException(nameof(inClient));
        if (inClient.LocalSeat < 0 || inClient.LocalSeat > 1)
            throw new ArgumentOutOfRangeException(nameof(inClient), "Online matches require seat 0 or 1.");

        OnlineMatchContext.Client = inClient;
    }

    public static void Clear()
    {
        // The lobby owns the client and transport lifetimes.
        OnlineMatchContext.Client = null;
    }
}
