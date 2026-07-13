using System.IO;
using UnityEngine;

namespace AldenFall.Persistence
{
    /// <summary>
    /// Server-side save/load of per-player profiles as JSON files, keyed by account
    /// id. On the Render dedicated server the save dir is the persistent disk mount
    /// (/data, see render.yaml); locally it falls back to persistentDataPath. Writes
    /// should be debounced by callers. Fail-loud, never throw into gameplay.
    /// </summary>
    public static class PlayerPersistence
    {
        public static string SaveRoot
        {
            get
            {
                string envDir = System.Environment.GetEnvironmentVariable("ALDENFALL_SAVE_DIR");
                if (!string.IsNullOrEmpty(envDir)) return envDir;
                if (Directory.Exists("/data")) return "/data/saves";   // Render persistent disk
                return Path.Combine(Application.persistentDataPath, "saves");
            }
        }

        private static string PathFor(string accountId) =>
            Path.Combine(SaveRoot, Sanitize(accountId) + ".json");

        public static void Save(PlayerSaveData data)
        {
            if (data == null || string.IsNullOrEmpty(data.accountId)) return;
            try
            {
                Directory.CreateDirectory(SaveRoot);
                File.WriteAllText(PathFor(data.accountId), JsonUtility.ToJson(data));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"PlayerPersistence: save failed for '{data.accountId}': {e.Message}");
            }
        }

        public static PlayerSaveData Load(string accountId)
        {
            if (string.IsNullOrEmpty(accountId)) return null;
            try
            {
                var path = PathFor(accountId);
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<PlayerSaveData>(File.ReadAllText(path));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"PlayerPersistence: load failed for '{accountId}': {e.Message}");
                return null;
            }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "anon" : s;
        }
    }

    /// <summary>A stable per-install account id (client-generated GUID in PlayerPrefs,
    /// sent to the server at login). Good enough to key persistence for the beta.</summary>
    public static class PlayerIdentity
    {
        private const string Key = "account_id_v1";

        public static string AccountId
        {
            get
            {
                string id = PlayerPrefs.GetString(Key, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = System.Guid.NewGuid().ToString("N").Substring(0, 16);
                    PlayerPrefs.SetString(Key, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }
    }
}
