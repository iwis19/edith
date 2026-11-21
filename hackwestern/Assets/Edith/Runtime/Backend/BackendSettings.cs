using System;
using UnityEngine;

namespace Edith.Backend
{
    /// Where the backend is and the device token it expects. The editor setup writes them from backend/.env or the
    /// environment into a git-ignored asset under Resources, so the build carries them and the repository does not.
    public sealed class BackendSettings : ScriptableObject
    {
        public const string ResourceName = "EdithBackend";
        const string ProfileKey = "edith.profile";

        [SerializeField] string url = "";
        [SerializeField] string deviceToken = "";

        static BackendSettings loaded;
        static bool searched;

        /// Without a trailing slash.
        public string Url => url.TrimEnd('/');
        public string DeviceToken => deviceToken;

        /// The settings in this build, or null after one log line saying what is missing.
        public static BackendSettings Load()
        {
            if (searched) return loaded;
            searched = true;
            var settings = Resources.Load<BackendSettings>(ResourceName);
            if (settings == null || string.IsNullOrWhiteSpace(settings.url))
            {
                Debug.LogWarning("[EDITH backend] no backend URL in this build: set EDITH_BACKEND_URL in backend/.env or the environment, then run EDITH/Configure Project");
                return null;
            }
            if (string.IsNullOrEmpty(settings.deviceToken))
                Debug.LogWarning("[EDITH backend] no device token in this build: a backend with DEVICE_TOKEN set will refuse it");
            if (settings.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning($"[EDITH backend] {settings.Url} is plain HTTP, which Android blocks unless the network security config allows it");
            loaded = settings;
            return loaded;
        }

        /// This headset's profile, made once and kept across launches.
        public static string ProfileId
        {
            get
            {
                var id = PlayerPrefs.GetString(ProfileKey, "");
                if (Guid.TryParse(id, out _)) return id;
                id = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(ProfileKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        /// Sets both values, for the editor setup.
        public void Set(string backendUrl, string token)
        {
            url = backendUrl ?? "";
            deviceToken = token ?? "";
        }
    }
}
