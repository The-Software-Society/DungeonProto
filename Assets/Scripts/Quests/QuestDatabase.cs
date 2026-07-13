using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AldenFall.Quests
{
    /// <summary>
    /// Quest registry, loaded from StreamingAssets/quests.json (mirrors
    /// Data.MonsterDatabase). Edit the JSON to add or retune quests, no code
    /// change and no rebuild. Read identically by playmode, shipped clients,
    /// and the dedicated server.
    /// </summary>
    public static class QuestDatabase
    {
        public const string FileName = "quests.json";
        private static Dictionary<string, QuestDef> quests;

        public static string FilePath =>
            Path.Combine(Application.streamingAssetsPath, FileName);

        public static QuestDef Get(string id)
        {
            EnsureInitialized();
            if (id == null) return null;
            return quests.TryGetValue(id, out var q) ? q : null;
        }

        public static IEnumerable<QuestDef> All
        {
            get { EnsureInitialized(); return quests.Values; }
        }

        /// <summary>Drop the cache so the next access reloads quests.json (hot-reload).</summary>
        public static void Reload() { quests = null; EnsureInitialized(); }

        private static void EnsureInitialized()
        {
            if (quests != null) return;
            quests = new Dictionary<string, QuestDef>();

            try
            {
                if (!File.Exists(FilePath))
                {
                    Debug.LogError($"QuestDatabase: {FilePath} not found");
                    return;
                }
                var file = JsonUtility.FromJson<QuestFile>(File.ReadAllText(FilePath));
                if (file?.quests == null)
                {
                    Debug.LogError("QuestDatabase: quests.json parsed to nothing");
                    return;
                }
                foreach (var q in file.quests)
                {
                    if (q == null || string.IsNullOrEmpty(q.Id)) continue;
                    quests[q.Id] = q;
                }
                Debug.Log($"QuestDatabase: loaded {quests.Count} quests");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"QuestDatabase: failed to load {FileName}: {e.Message}");
            }
        }
    }
}
