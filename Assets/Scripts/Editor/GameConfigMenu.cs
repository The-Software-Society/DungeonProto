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
    }
}
#endif
