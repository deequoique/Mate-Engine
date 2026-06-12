using UnityEngine;

namespace PetRemote
{
    public static class PetRemoteBootstrap
    {
        static RemoteManager instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureInstance()
        {
            if (instance != null) return;
            instance = Object.FindFirstObjectByType<RemoteManager>(FindObjectsInactive.Include);
            if (instance != null) return;

            var root = new GameObject("PetRemote");
            Object.DontDestroyOnLoad(root);
            instance = root.AddComponent<RemoteManager>();
        }

        public static void NotifyAvatarLoaded(GameObject avatarRoot)
        {
            if (instance == null) instance = Object.FindFirstObjectByType<RemoteManager>(FindObjectsInactive.Include);
            if (instance != null) instance.HandleAvatarLoaded(avatarRoot);
        }
    }
}
