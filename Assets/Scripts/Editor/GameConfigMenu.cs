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

            var skills = AldenFall.Progression.SkillRegistry.All;
            Debug.Log($"[Validate] skills loaded: {skills.Count}");
            foreach (var sk in skills)
                Debug.Log($"[Validate]   skill {sk.id}: '{sk.display}' cat={sk.category} glyph={sk.glyph} xpRate={sk.xpRate} color={(sk.Color)}");

            var itemList = new System.Collections.Generic.List<AldenFall.Items.ItemDef>(AldenFall.Items.ItemDatabase.All);
            Debug.Log($"[Validate] items loaded: {itemList.Count}");
            foreach (var it in itemList)
            {
                string bonuses = string.Join(",", System.Array.ConvertAll(it.Bonuses.ToArray(), b => $"{b.type}:{b.value}"));
                Debug.Log($"[Validate]   item {it.Id}: slot={it.Slot} kind={it.WeaponKind} rar={it.Rarity} sell={it.SellValue} heal={it.HealAmount} cook='{it.CookedResult}' [{bonuses}]");
            }

            var abils = new System.Collections.Generic.List<AldenFall.Abilities.AbilityDef>(AldenFall.Abilities.AbilityDatabase.All);
            Debug.Log($"[Validate] abilities loaded: {abils.Count}");
            foreach (var ab in abils)
                Debug.Log($"[Validate]   ability {ab.Id}: tree={ab.Tree} req={ab.RequiredWeapon} kind={ab.Kind} flags={ab.StatusFlags} " +
                          $"tint={(ab.ProjectileTint.HasValue ? "yes" : "no")} statmods={ab.StatusStatMods.Count} buffs={ab.BuffMods.Count}");

            var quests = new System.Collections.Generic.List<AldenFall.Quests.QuestDef>(AldenFall.Quests.QuestDatabase.All);
            Debug.Log($"[Validate] quests loaded: {quests.Count}");
            foreach (var q in quests)
                Debug.Log($"[Validate]   {q.Id}: '{q.Name}' obj={q.Objectives.Count} rewards={q.Rewards.Count} gold={q.GoldReward} " +
                          $"giver='{q.GiverNpcId}' prereq='{q.PrereqQuestId}' firstKind={(q.Objectives.Count > 0 ? q.Objectives[0].Kind.ToString() : "-")}");
        }
    }
}
#endif
