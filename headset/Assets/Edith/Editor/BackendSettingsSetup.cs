using System;
using System.Collections.Generic;
using System.IO;
using Edith.Backend;
using UnityEditor;
using UnityEngine;

namespace Edith.Editor
{
    /// Writes the backend URL and device token into the git-ignored BackendSettings asset the build carries. Each value
    /// comes from the environment, or else from backend/.env.
    public static class BackendSettingsSetup
    {
        public const string AssetPath = "Assets/Resources/" + BackendSettings.ResourceName + ".asset";
        const string UrlName = "EDITH_BACKEND_URL";
        const string TokenName = "DEVICE_TOKEN";

        public static void Write()
        {
            var file = ReadEnvFile(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "backend", ".env")));
            var url = Value(UrlName, file);
            var token = Value(TokenName, file);
            if (string.IsNullOrEmpty(url)) Debug.LogWarning($"[EDITH setup] no {UrlName} in the environment or backend/.env, so Ask will not reach a backend");
            if (string.IsNullOrEmpty(token)) Debug.LogWarning($"[EDITH setup] no {TokenName} in the environment or backend/.env");

            var settings = AssetDatabase.LoadAssetAtPath<BackendSettings>(AssetPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
                settings = ScriptableObject.CreateInstance<BackendSettings>();
                settings.Set(url, token);
                AssetDatabase.CreateAsset(settings, AssetPath);
            }
            else
            {
                settings.Set(url, token);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssetIfDirty(settings);
            Debug.Log($"[EDITH setup] backend settings written: url {(string.IsNullOrEmpty(url) ? "missing" : url)}, token {(string.IsNullOrEmpty(token) ? "missing" : "set")}");
        }

        static string Value(string name, Dictionary<string, string> file)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value)) file.TryGetValue(name, out value);
            return value?.Trim() ?? "";
        }

        /// KEY=VALUE lines; blank lines and # comments are skipped and one pair of surrounding quotes is dropped.
        static Dictionary<string, string> ReadEnvFile(string path)
        {
            var values = new Dictionary<string, string>();
            if (!File.Exists(path)) return values;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var equals = line.IndexOf('=');
                if (equals <= 0) continue;
                var value = line.Substring(equals + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[value.Length - 1] == value[0])
                    value = value.Substring(1, value.Length - 2);
                values[line.Substring(0, equals).Trim()] = value;
            }
            return values;
        }
    }
}
