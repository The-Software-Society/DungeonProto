using System.Collections.Generic;
using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Progression;
using AldenFall.Stats;

namespace AldenFall.Abilities
{
    /// <summary>
    /// What kind of weapon an ability needs. This is the scaling hook for
    /// future branches (dual-wield, 2h, staves): new WeaponClass values +
    /// new defs, the tree UI and action bar are data-driven off this.
    /// </summary>
    public enum WeaponClass
    {
        Any,
        Sword,
        Focus,
        Bow,     // append-only enum!
    }

    public enum AbilityKind
    {
        Projectile,  // homing missile at the target (fireball)
        Nova,        // point-blank AoE around the caster (melee when MeleeMultiplier > 0)
        DashStun,    // dash to the target, hit + stun
        SingleHit,   // heavy strike on the current target
        SelfBuff,    // timed StatBlock buff on the caster
        MultiHit,    // rapid burst of strikes on the current target (flurry)
        Taunt,       // force-aggro every enemy in a radius onto the caster
        GroundAoe,   // ranged targeted AoE that lands at the target's location (Meteor)
        Blink,       // instant short-range teleport in the facing direction (mobility)
    }

    /// <summary>Static definition of one active ability, loaded from abilities.json.
    /// Enums, flags, and the nullable projectile tint serialize as editable strings
    /// (get/set properties over string backing fields); readers use the typed accessors.</summary>
    [System.Serializable]
    public class AbilityDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string IconSprite;          // UISpriteGenerator sprite name

        public string tree = "Attack";             // SkillType name: which combat skill tree it lives under
        public string requiredWeapon = "Any";      // WeaponClass name: Any/Sword/Focus
        public string kind = "Projectile";         // AbilityKind name
        public SkillType Tree { get => Parse(tree, SkillType.Attack); set => tree = value.ToString(); }
        public WeaponClass RequiredWeapon { get => Parse(requiredWeapon, WeaponClass.Any); set => requiredWeapon = value.ToString(); }
        public AbilityKind Kind { get => Parse(kind, AbilityKind.Projectile); set => kind = value.ToString(); }

        public float Cooldown;
        public float BaseDamage;           // spell base for Projectile/Nova
        public float MeleeMultiplier;      // × CombatMath.MeleeDamage for melee kinds
        public float Range;                // max target distance (dash reach / strike reach)
        public float Radius;               // Nova AoE
        public float Knockback;            // 0 = none

        public float StunDuration;         // DashStun
        public float DashSpeed;

        public int HitCount;               // MultiHit strikes
        public float HitInterval;          // seconds between MultiHit strikes
        public bool SpinCaster;            // Nova visual: spin the body (Whirlwind)

        public string AppliesStatusId;     // e.g. "burn" on projectile hit
        public float StatusDuration;
        public float StatusTickInterval;
        public float StatusTickDamage;
        public string statusFlags = "None";        // StatusFlags name(s), e.g. "Stunned" (comma-separated for combos)
        public StatusFlags StatusFlags { get => Parse(statusFlags, StatusFlags.None); set => statusFlags = value.ToString(); }
        public float StatusMoveSpeedMultiplier;    // 0 = none; 0.5 = half speed (Hamstring)
        // On-target debuff stat mods (e.g. Hex shredding Defence/Armor to amp incoming damage).
        // Routed through StatusSystem → the target's StatBlock, cleared when the status expires.
        public List<StatBonus> StatusStatMods = new List<StatBonus>();

        // Projectile visual/behaviour overrides. Empty tint = the default orange fireball look.
        public string projectileTint = "";         // hex like "#E4622A", empty = default look
        public Color? ProjectileTint
        {
            get => string.IsNullOrEmpty(projectileTint)
                ? (Color?)null
                : (ColorUtility.TryParseHtmlString(projectileTint, out var c) ? c : (Color?)null);
            set => projectileTint = value.HasValue ? "#" + ColorUtility.ToHtmlStringRGBA(value.Value) : "";
        }
        public float ProjectileSpeed;      // 0 = use PlayerAbilities.fireballSpeed

        public float ImpactDelay;          // GroundAoe: seconds from cast to the AoE landing

        public List<StatBonus> BuffMods = new List<StatBonus>();
        public float BuffDuration;

        private static T Parse<T>(string s, T fallback) where T : struct =>
            System.Enum.TryParse<T>(s, out var v) ? v : fallback;
    }

    /// <summary>File wrapper for JsonUtility (mirrors Data.MonsterFile).</summary>
    [System.Serializable]
    public class AbilityFile
    {
        public AbilityDef[] abilities = new AbilityDef[0];
    }

    /// <summary>
    /// Ability registry, loaded from StreamingAssets/abilities.json. Everything
    /// downstream (tree UI, action bar, mastery) iterates this, so growth is
    /// data-only, no code change.
    /// </summary>
    public static class AbilityDatabase
    {
        public const string FileName = "abilities.json";
        private static Dictionary<string, AbilityDef> abilities;

        public static string FilePath =>
            System.IO.Path.Combine(Application.streamingAssetsPath, FileName);

        public static AbilityDef Get(string id)
        {
            EnsureInitialized();
            return id != null && abilities.TryGetValue(id, out var def) ? def : null;
        }

        public static IEnumerable<AbilityDef> All
        {
            get { EnsureInitialized(); return abilities.Values; }
        }

        private static void EnsureInitialized()
        {
            if (abilities != null) return;
            abilities = new Dictionary<string, AbilityDef>();

            try
            {
                if (!System.IO.File.Exists(FilePath))
                {
                    Debug.LogError($"AbilityDatabase: {FilePath} not found");
                    return;
                }
                var file = JsonUtility.FromJson<AbilityFile>(System.IO.File.ReadAllText(FilePath));
                if (file?.abilities == null)
                {
                    Debug.LogError("AbilityDatabase: abilities.json parsed to nothing");
                    return;
                }
                foreach (var d in file.abilities)
                {
                    if (d == null || string.IsNullOrEmpty(d.Id)) continue;
                    abilities[d.Id] = d;
                }
                Debug.Log($"AbilityDatabase: loaded {abilities.Count} abilities");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"AbilityDatabase: failed to load {FileName}: {e.Message}");
            }
        }

        /// <summary>Drop the cache so the next access reloads abilities.json (hot-reload).</summary>
        public static void Reload() { abilities = null; EnsureInitialized(); }
    }
}
