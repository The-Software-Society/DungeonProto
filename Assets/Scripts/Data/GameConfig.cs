using System.IO;
using UnityEngine;

namespace AldenFall.Data
{
    /// <summary>
    /// Tuning knobs for combat math. Mirrors config/combat.json field-for-field.
    /// Defaults equal the shipped values so a missing/partial file degrades to
    /// today's balance rather than breaking. See ARCHITECTURE.md section 3.
    /// </summary>
    [System.Serializable]
    public class CombatConfig
    {
        public float baseMeleeDamage = 5f;
        public float baseRangedDamage = 5f;
        public float baseSwingInterval = 1.9f;
        public float levelGrowth = 1.085f;

        public float spellFocusGateBase = 0.60f;
        public float spellFocusGatePerPower = 0.04f;
        public float spellFocusPowerCap = 10f;

        public float mitigationPerDefence = 0.01f;
        public float mitigationPerArmor = 0.03f;
        public float mitigationCap = 0.75f;

        public float missBase = 0.10f;
        public float missPerDefenceLead = 0.035f;
        public float missFloor = 0.05f;
        public float missCeil = 0.95f;

        public float swingSpeedPerAttack = 0.008f;
        public float cooldownReductionPerLevel = 0.0046f;
        public float cooldownFloor = 0.55f;

        public float damageVariance = 0.15f;
    }

    /// <summary>XP curve and level caps. Mirrors config/progression.json.</summary>
    [System.Serializable]
    public class ProgressionConfig
    {
        public float xpCoefficient = 50f;
        public float xpExponent = 1.35f;
        public int practicalCap = 99;   // public wall shown to players
        public int hardCap = 500;       // secret ceiling, never surfaced in UI (reserved)
        // OSRS-style Hitpoints: every point of combat XP also trickles into Hitpoints at
        // this fraction (0.33 = the classic ~1/3). Trains from DEALING combat damage.
        public float hitpointsXpFraction = 0.33f;
    }

    /// <summary>Economy knobs. Mirrors config/economy.json.</summary>
    [System.Serializable]
    public class EconomyConfig
    {
        public float vendorBuyMultiplier = 4f;
        public int forgeCostCoef = 50;
        public int forgeCostExp = 2;
    }

    /// <summary>
    /// Tiered view/stream distances (the "world mapper" perf layer). Mirrors config/world.json.
    /// The server is the single source of truth for positions; each CLIENT only loads/renders what
    /// is near it. Living things (mobs, bosses, loot, other players) stream in at a natural view
    /// distance; the baked environment (buildings, mountains) stays far so vistas read; small scene
    /// accents cull at a medium distance. Bigger worlds tune these without a code change.
    /// </summary>
    [System.Serializable]
    public class WorldConfig
    {
        // Networked living things (mobs/bosses/loot/other players): Mirror interest-management range.
        // Beyond this the server does not spawn/sync them to a client, so they aren't rendered/tracked.
        public int entityViewDistance = 190;
        // Server idles a mob's AI (no pathing/target-scan) when no player is this close. Saves CPU on
        // the far side of the map; keep >= mob detection range so nothing pops into aggro.
        public float aiActivationDistance = 75f;
        // Camera far clip — the baked environment (buildings, mountains, terrain). Keep large: vistas.
        public float environmentDistance = 2200f;
        // Small scene accents (shrubs, small rocks, ferns) — camera layer cull (Detail layer).
        public float detailDistance = 440f;
    }

    /// <summary>
    /// The one place runtime tuning is loaded from. Every balance number lives in
    /// StreamingAssets/config/*.json, not in a C# const, so combat, damage, XP, and
    /// economy are edited without touching code. Read identically by the editor
    /// build, playmode, shipped clients, and the dedicated server (all reach the
    /// files through Application.streamingAssetsPath). Loaders fail loud and fall
    /// back to built-in defaults. Modeled on Data.MonsterDatabase.
    /// </summary>
    public static class GameConfig
    {
        public const string ConfigDir = "config";

        private static CombatConfig combat;
        private static ProgressionConfig progression;
        private static EconomyConfig economy;
        private static WorldConfig world;

        public static CombatConfig Combat => combat ??= Load<CombatConfig>("combat.json");
        public static ProgressionConfig Progression => progression ??= Load<ProgressionConfig>("progression.json");
        public static EconomyConfig Economy => economy ??= Load<EconomyConfig>("economy.json");
        public static WorldConfig World => world ??= Load<WorldConfig>("world.json");

        /// <summary>Preload at runtime start so first-frame combat does not pay the IO cost mid-fight.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Preload()
        {
            Reload();
            Debug.Log($"GameConfig: levelGrowth={Combat.levelGrowth}, xp={Progression.xpCoefficient}*l^{Progression.xpExponent} " +
                      $"cap {Progression.practicalCap}/{Progression.hardCap}, vendor x{Economy.vendorBuyMultiplier}");
        }

        /// <summary>Drop cached configs and reload from disk. Call for hot-reload while balancing.</summary>
        public static void Reload()
        {
            combat = Load<CombatConfig>("combat.json");
            progression = Load<ProgressionConfig>("progression.json");
            economy = Load<EconomyConfig>("economy.json");
            world = Load<WorldConfig>("world.json");
        }

        private static T Load<T>(string file) where T : new()
        {
            try
            {
                var path = Path.Combine(Application.streamingAssetsPath, ConfigDir, file);
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"GameConfig: {path} not found, using built-in defaults");
                    return new T();
                }
                var cfg = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (cfg == null)
                {
                    Debug.LogError($"GameConfig: {file} parsed to null, using defaults");
                    return new T();
                }
                return cfg;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"GameConfig: failed to load {file}: {e.Message}");
                return new T();
            }
        }
    }
}
