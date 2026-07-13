using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// One named NPC placed from data: a quest giver, a vendor, or a talkable
    /// townsperson. role drives which component the builder attaches. Loaded from
    /// StreamingAssets/npcs.json so the roster grows without editing the builder.
    /// </summary>
    [System.Serializable]
    public class NpcDef
    {
        public string id;
        public string displayName;
        public string role = "townsfolk";   // quest | vendor | townsfolk
        public float x, z, yaw;
        public string model = "HeroMale";    // generated-visual folder; capsule fallback if missing
        public string robeHex = "#4A4258";   // fallback robe tint
        public string questNpcId = "";        // for role=quest: the npcId the quest data refers to
        public string[] barks = new string[0];
    }

    [System.Serializable]
    public class NpcRosterFile
    {
        public NpcDef[] npcs = new NpcDef[0];
    }

    /// <summary>Named-NPC roster, loaded from StreamingAssets/npcs.json (mirrors MonsterDatabase).</summary>
    public static class NpcRoster
    {
        public const string FileName = "npcs.json";

        public static string FilePath =>
            Path.Combine(Application.streamingAssetsPath, FileName);

        public static List<NpcDef> LoadAll()
        {
            var list = new List<NpcDef>();
            try
            {
                if (!File.Exists(FilePath))
                {
                    Debug.LogWarning($"NpcRoster: {FilePath} not found");
                    return list;
                }
                var file = JsonUtility.FromJson<NpcRosterFile>(File.ReadAllText(FilePath));
                if (file?.npcs != null)
                    foreach (var n in file.npcs)
                        if (n != null && !string.IsNullOrEmpty(n.id)) list.Add(n);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"NpcRoster: failed to load {FileName}: {e.Message}");
            }
            return list;
        }
    }
}
