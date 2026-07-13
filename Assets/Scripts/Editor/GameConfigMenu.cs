#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using AldenFall.Data;

namespace AldenFall.Editor
{
    /// <summary>
    /// Balancing helper: reload StreamingAssets/config/*.json without restarting.
    /// Works in playmode too, so combat/xp/economy edits take effect on the next
    /// formula call. See ARCHITECTURE.md for the config layer.
    /// </summary>
    public static class GameConfigMenu
    {
        [MenuItem("AldenFall/Reload Game Config", priority = 30)]
        public static void ReloadConfig()
        {
            GameConfig.Reload();
            var c = GameConfig.Combat;
            var p = GameConfig.Progression;
            var e = GameConfig.Economy;
            Debug.Log($"Game config reloaded: levelGrowth={c.levelGrowth}, xp={p.xpCoefficient}*l^{p.xpExponent} " +
                      $"cap {p.practicalCap}/{p.hardCap}, vendor x{e.vendorBuyMultiplier}, forge {e.forgeCostCoef}*(p+1)^{e.forgeCostExp}");
        }

        /// <summary>
        /// Loads every data source through its real runtime path and logs a
        /// summary, so JSON migrations can be verified headless
        /// (-executeMethod AldenFall.Editor.GameConfigMenu.ValidateData).
        /// </summary>
        [MenuItem("AldenFall/Validate Data", priority = 31)]
        public static void ValidateData()
        {
            GameConfig.Reload();
            Debug.Log($"[Validate] config OK: levelGrowth={GameConfig.Combat.levelGrowth}, " +
                      $"xpCoef={GameConfig.Progression.xpCoefficient}, vendorX={GameConfig.Economy.vendorBuyMultiplier}");

            var mobs = AldenFall.Data.MonsterDatabase.LoadAll();
            Debug.Log($"[Validate] monsters loaded: {mobs.Count}");

            var quests = new System.Collections.Generic.List<AldenFall.Quests.QuestDef>(AldenFall.Quests.QuestDatabase.All);
            Debug.Log($"[Validate] quests loaded: {quests.Count}");
            foreach (var q in quests)
                Debug.Log($"[Validate]   {q.Id}: '{q.Name}' obj={q.Objectives.Count} rewards={q.Rewards.Count} gold={q.GoldReward} " +
                          $"giver='{q.GiverNpcId}' prereq='{q.PrereqQuestId}' firstKind={(q.Objectives.Count > 0 ? q.Objectives[0].Kind.ToString() : "-")}");
        }
    }
}
#endif
