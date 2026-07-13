using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AldenFall.Progression
{
    /// <summary>Per-skill metadata (display name, UI glyph/color, category, XP rate),
    /// loaded from skills.json. id matches a SkillType enum name.</summary>
    [System.Serializable]
    public class SkillDef
    {
        public string id;
        public string category = "combat";  // combat | gather | craft | support
        public string display = "";
        public string glyph = "icon_sword";
        public string color = "#FFFFFF";
        public float xpRate = 1f;

        public Color Color => ColorUtility.TryParseHtmlString(color, out var c) ? c : UnityEngine.Color.white;
        public SkillType Type => System.Enum.TryParse<SkillType>(id, out var k) ? k : SkillType.Attack;
    }

    [System.Serializable]
    public class SkillFile
    {
        public SkillDef[] skills = new SkillDef[0];
    }

    /// <summary>
    /// Skill metadata registry, loaded from StreamingAssets/skills.json (mirrors
    /// Data.MonsterDatabase). The source of truth for skill names, glyphs, colors,
    /// categories, and XP rates, so the UI and progression are data-driven and a
    /// new skill is a SkillType enum value + a skills.json entry. Missing entries
    /// degrade to a sane default rather than crashing.
    /// </summary>
    public static class SkillRegistry
    {
        public const string FileName = "skills.json";
        private static Dictionary<SkillType, SkillDef> byType;
        private static List<SkillDef> ordered;

        public static string FilePath =>
            Path.Combine(Application.streamingAssetsPath, FileName);

        public static SkillDef Get(SkillType t)
        {
            EnsureInitialized();
            return byType.TryGetValue(t, out var d) ? d : Fallback(t);
        }

        public static IReadOnlyList<SkillDef> All
        {
            get { EnsureInitialized(); return ordered; }
        }

        public static void Reload() { byType = null; EnsureInitialized(); }

        private static SkillDef Fallback(SkillType t) =>
            new SkillDef { id = t.ToString(), display = t.ToString(), glyph = "icon_sword", color = "#FFFFFF", xpRate = 1f };

        private static void EnsureInitialized()
        {
            if (byType != null) return;
            byType = new Dictionary<SkillType, SkillDef>();
            ordered = new List<SkillDef>();

            try
            {
                if (!File.Exists(FilePath))
                {
                    Debug.LogError($"SkillRegistry: {FilePath} not found");
                    return;
                }
                var file = JsonUtility.FromJson<SkillFile>(File.ReadAllText(FilePath));
                if (file?.skills == null)
                {
                    Debug.LogError("SkillRegistry: skills.json parsed to nothing");
                    return;
                }
                foreach (var d in file.skills)
                {
                    if (d == null || string.IsNullOrEmpty(d.id)) continue;
                    if (!System.Enum.TryParse<SkillType>(d.id, out var t))
                    {
                        Debug.LogWarning($"SkillRegistry: '{d.id}' is not a SkillType, skipping");
                        continue;
                    }
                    byType[t] = d;
                    ordered.Add(d);
                }
                Debug.Log($"SkillRegistry: loaded {ordered.Count} skills");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"SkillRegistry: failed to load {FileName}: {e.Message}");
            }
        }
    }
}
