using System.Collections.Generic;
using UnityEngine;
using AldenFall.Combat;
using AldenFall.Progression;
using AldenFall.Stats;

namespace AldenFall.Abilities
{
    /// <summary>
    /// What kind of weapon an ability needs. This is the scaling hook for
    /// future branches (dual-wield, 2h, staves): new WeaponClass values +
    /// new defs — the tree UI and action bar are data-driven off this.
    /// </summary>
    public enum WeaponClass
    {
        Any,
        Sword,
        Focus,
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

    /// <summary>Static definition of one active ability (code-as-data, like ItemDef).</summary>
    public class AbilityDef
    {
        public string Id;
        public string Name;
        public string Description;
        public string IconSprite;          // UISpriteGenerator sprite name
        public SkillType Tree;             // which combat skill tree it lives under
        public WeaponClass RequiredWeapon;
        public AbilityKind Kind;

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
        public StatusFlags StatusFlags;            // e.g. Stunned for Bash
        public float StatusMoveSpeedMultiplier;    // 0 = none; 0.5 = half speed (Hamstring)
        // On-target debuff stat mods (e.g. Hex shredding Defence/Armor to amp incoming damage).
        // Routed through StatusSystem → the target's StatBlock, cleared when the status expires.
        public List<(StatType type, float value)> StatusStatMods = new List<(StatType, float)>();

        // Projectile visual/behaviour overrides. Tint null = the default orange fireball look.
        public Color? ProjectileTint;
        public float ProjectileSpeed;      // 0 = use PlayerAbilities.fireballSpeed

        public float ImpactDelay;          // GroundAoe: seconds from cast to the AoE landing

        public List<(StatType type, float value)> BuffMods = new List<(StatType, float)>();
        public float BuffDuration;
    }

    /// <summary>
    /// Code-defined ability registry. Demo content is deliberately minimal —
    /// one ability per weapon class per tree — but everything downstream
    /// (tree UI, action bar, mastery) iterates this, so growth is data-only.
    /// </summary>
    public static class AbilityDatabase
    {
        private static Dictionary<string, AbilityDef> abilities;

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

            Register(new AbilityDef
            {
                Id = "fireball",
                Name = "Fireball",
                Description = "Hurl a homing bolt of flame that sets the target burning.",
                IconSprite = "icon_fireball",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Any,
                Kind = AbilityKind.Projectile,
                Cooldown = 2.5f,
                BaseDamage = 14f,
                Range = 30f,
                AppliesStatusId = "burn",
                StatusDuration = 2f,
                StatusTickInterval = 0.5f,
                StatusTickDamage = 1.5f,
            });

            Register(new AbilityDef
            {
                Id = "ember_nova",
                Name = "Ember Nova",
                Description = "Detonate a ring of fire around you, blasting enemies away.",
                IconSprite = "icon_nova",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Focus,
                Kind = AbilityKind.Nova,
                Cooldown = 8f,
                BaseDamage = 18f,
                Radius = 5f,
                Knockback = 10f,   // the one player knockback left
            });

            Register(new AbilityDef
            {
                Id = "blade_rush",
                Name = "Blade Rush",
                Description = "Dash to your target and slam them with the flat of your blade, stunning them.",
                IconSprite = "icon_dash",
                Tree = SkillType.Attack,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.DashStun,
                Cooldown = 7f,
                MeleeMultiplier = 1.6f,
                Range = 9f,
                DashSpeed = 22f,
                StunDuration = 1.2f,
            });

            Register(new AbilityDef
            {
                Id = "savage_cleave",
                Name = "Savage Cleave",
                Description = "A brutal overhead cleave that hits far harder than any swing.",
                IconSprite = "icon_cleave",
                Tree = SkillType.Strength,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.SingleHit,
                Cooldown = 7f,
                MeleeMultiplier = 2.2f,
                Range = 3f,
            });

            Register(new AbilityDef
            {
                Id = "bulwark",
                Name = "Bulwark",
                Description = "Brace behind your guard: +8 Armor and +10 Defence for 8 seconds.",
                IconSprite = "icon_bulwark",
                Tree = SkillType.Defence,
                RequiredWeapon = WeaponClass.Any,
                Kind = AbilityKind.SelfBuff,
                Cooldown = 15f,
                BuffMods = { (StatType.Armor, 8f), (StatType.Defence, 10f) },
                BuffDuration = 8f,
            });

            // ---- v5: three abilities per combat tree ----

            Register(new AbilityDef
            {
                Id = "blade_flurry",
                Name = "Blade Flurry",
                Description = "A blur of quick stabs that leaves the target bleeding.",
                IconSprite = "icon_flurry",
                Tree = SkillType.Attack,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.MultiHit,
                Cooldown = 6f,
                MeleeMultiplier = 0.55f,
                Range = 3f,
                HitCount = 4,
                HitInterval = 0.16f,
                AppliesStatusId = "bleed",
                StatusDuration = 5f,
                StatusTickInterval = 1f,
                StatusTickDamage = 2.5f,
            });

            Register(new AbilityDef
            {
                Id = "hamstring",
                Name = "Hamstring",
                Description = "Slash the legs: solid damage and the target limps at half speed.",
                IconSprite = "icon_slow",
                Tree = SkillType.Attack,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.SingleHit,
                Cooldown = 6f,
                MeleeMultiplier = 1.2f,
                Range = 3f,
                AppliesStatusId = "slow",
                StatusDuration = 4f,
                StatusMoveSpeedMultiplier = 0.5f,
            });

            Register(new AbilityDef
            {
                Id = "whirlwind",
                Name = "Whirlwind",
                Description = "Spin with blade out, striking and shoving everything around you.",
                IconSprite = "icon_whirl",
                Tree = SkillType.Strength,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.Nova,
                Cooldown = 8f,
                SpinCaster = true,
                MeleeMultiplier = 1.4f,
                Radius = 4.5f,
                Knockback = 6f,
            });

            Register(new AbilityDef
            {
                Id = "seismic_slam",
                Name = "Seismic Slam",
                Description = "Slam the ground: everything nearby is damaged and dragged toward you.",
                IconSprite = "icon_slam",
                Tree = SkillType.Strength,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.Nova,
                Cooldown = 10f,
                MeleeMultiplier = 1.1f,
                Radius = 6.5f,
                Knockback = -14f,   // negative = pull toward the caster
            });

            Register(new AbilityDef
            {
                Id = "challenging_shout",
                Name = "Challenging Shout",
                Description = "Bellow a challenge: every enemy nearby turns on you.",
                IconSprite = "icon_taunt",
                Tree = SkillType.Defence,
                RequiredWeapon = WeaponClass.Any,
                Kind = AbilityKind.Taunt,
                Cooldown = 12f,
                Radius = 12f,
            });

            Register(new AbilityDef
            {
                Id = "bash",
                Name = "Bash",
                Description = "Crack the target with your pommel, stunning them briefly.",
                IconSprite = "icon_bash",
                Tree = SkillType.Defence,
                RequiredWeapon = WeaponClass.Sword,
                Kind = AbilityKind.SingleHit,
                Cooldown = 10f,
                MeleeMultiplier = 0.8f,
                Range = 3f,
                AppliesStatusId = "stun",
                StatusDuration = 1.5f,
                StatusFlags = StatusFlags.Stunned,
            });

            Register(new AbilityDef
            {
                Id = "veil",
                Name = "Veil of Shadows",
                Description = "Fade from sight for 6 seconds — enemies forget you. Attacking breaks the veil.",
                IconSprite = "icon_veil",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Focus,
                Kind = AbilityKind.SelfBuff,
                Cooldown = 25f,
                AppliesStatusId = "invisible",   // the aggro-drop status EnemyAI checks
                BuffDuration = 6f,
            });

            // ---- v7: magic depth — a full magic-only kit ----
            // A single-target filler nuke, a ranged ground AoE, a curse/DoT debuff,
            // a blink mobility, and a magic ward — so a pure caster has real options.

            // Fast, low-cooldown bolt: less punch than Fireball but spammable and
            // it CHILLS (a movement slow, no burn) — the caster's kiting tool.
            Register(new AbilityDef
            {
                Id = "frost_bolt",
                Name = "Frost Bolt",
                Description = "A darting shard of ice — quick to cast and it chills the target, slowing them.",
                IconSprite = "icon_bolt",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Any,
                Kind = AbilityKind.Projectile,
                Cooldown = 1.5f,
                BaseDamage = 11f,
                Range = 30f,
                ProjectileTint = new Color(0.45f, 0.75f, 1f, 1f),
                ProjectileSpeed = 30f,   // noticeably faster than the fireball
                AppliesStatusId = "chill",
                StatusDuration = 2.5f,
                StatusMoveSpeedMultiplier = 0.6f,
            });

            // Ranged targeted AoE: lands where the target stands after a short
            // fall, distinct from the self-centred Ember Nova.
            Register(new AbilityDef
            {
                Id = "meteor_strike",
                Name = "Meteor Strike",
                Description = "Call down a blazing meteor onto your target, scorching everything nearby.",
                IconSprite = "icon_flame",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Focus,
                Kind = AbilityKind.GroundAoe,
                Cooldown = 12f,
                BaseDamage = 28f,
                Range = 28f,
                Radius = 5f,
                ImpactDelay = 0.7f,
            });

            // Curse: light direct hit, a lingering arcane DoT, and — the point —
            // it SHREDS the target's Defence/Armor, amplifying all incoming damage.
            Register(new AbilityDef
            {
                Id = "hex",
                Name = "Hex of Frailty",
                Description = "Curse the target: it withers over time and its defences crumble, taking more damage.",
                IconSprite = "icon_veil",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Focus,
                Kind = AbilityKind.Projectile,
                Cooldown = 10f,
                BaseDamage = 6f,
                Range = 30f,
                ProjectileTint = new Color(0.7f, 0.35f, 0.95f, 1f),
                ProjectileSpeed = 20f,
                AppliesStatusId = "hex",
                StatusDuration = 6f,
                StatusTickInterval = 1f,
                StatusTickDamage = 3f,
                StatusStatMods = { (StatType.Defence, -6f), (StatType.Armor, -4f) },
            });

            // Mobility: instant short teleport in the facing direction. No damage —
            // the caster's escape / gap-closer so magic isn't rooted in place.
            Register(new AbilityDef
            {
                Id = "blink",
                Name = "Arcane Blink",
                Description = "Wink out and reappear a short distance ahead — a caster's escape.",
                IconSprite = "icon_dash",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Any,
                Kind = AbilityKind.Blink,
                Cooldown = 9f,
                Range = 8f,
            });

            // Defensive ward: the caster's answer to Bulwark — a timed Armor/Defence
            // buff so a mage can turtle a burst instead of only kiting.
            Register(new AbilityDef
            {
                Id = "arcane_ward",
                Name = "Arcane Ward",
                Description = "Wreathe yourself in a shield of force: +6 Armor and +8 Defence for 8 seconds.",
                IconSprite = "icon_shield",
                Tree = SkillType.Magic,
                RequiredWeapon = WeaponClass.Focus,
                Kind = AbilityKind.SelfBuff,
                Cooldown = 18f,
                BuffMods = { (StatType.Armor, 6f), (StatType.Defence, 8f) },
                BuffDuration = 8f,
            });
        }

        private static void Register(AbilityDef def) => abilities[def.Id] = def;
    }
}
