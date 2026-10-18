using Elemento.Networking;
using NUnit.Framework;
using UnityEngine;

public sealed class OnlineCoroutineHostTests
{
    private GameObject hostObject;

    [TearDown]
    public void TearDown()
    {
        if (this.hostObject != null)
            Object.DestroyImmediate(this.hostObject);
        this.hostObject = null;
    }

    [Test]
    public void IN_ReturnsSameActiveEnabledInstance()
    {
        OnlineCoroutineHost first = OnlineCoroutineHost.IN;
        this.hostObject = first.gameObject;
        OnlineCoroutineHost second = OnlineCoroutineHost.IN;

        Assert.That(second, Is.SameAs(first));
        Assert.That(first.gameObject.activeInHierarchy, Is.True);
        Assert.That(first.enabled, Is.True);
        Assert.That(first.gameObject.hideFlags, Is.EqualTo(HideFlags.HideInHierarchy));
    }
}
