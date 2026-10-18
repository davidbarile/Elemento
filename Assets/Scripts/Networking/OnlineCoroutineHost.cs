using UnityEngine;

namespace Elemento.Networking
{
    public sealed class OnlineCoroutineHost : MonoBehaviour
    {
        private static OnlineCoroutineHost instance;

        public static OnlineCoroutineHost IN
        {
            get
            {
                if (instance != null)
                    return instance;

                var hostObject = new GameObject("Online Coroutine Host");
                hostObject.hideFlags = HideFlags.HideInHierarchy;
                instance = hostObject.AddComponent<OnlineCoroutineHost>();
                if (Application.isPlaying)
                    DontDestroyOnLoad(hostObject);
                return instance;
            }
        }
    }
}
