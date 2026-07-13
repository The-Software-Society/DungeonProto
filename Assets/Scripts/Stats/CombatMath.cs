using UnityEngine;
using AldenFall.Data;

namespace AldenFall.Stats
{
    /// <summary>
    /// The one place combat formulas live. Every damage event in the game -
    /// player melee, spells, mob attacks, routes through here, reading from
    /// each side's StatBlock.
    ///
    /// v6 skill identities:
    ///   Strength, melee damage (exponential per level)
    ///   Attack  , attack speed + melee-tree ability cooldown reduction
    ///   Magic   , spell damage (exponential) + magic cooldown reduction
    ///   Defence , damage reduction + chance attackers miss entirely
    /// </summary>
    public static class CombatMath
    {
        // Tuning knobs now live in config/combat.json (loaded by Data.GameConfig).
        // These proxies keep the old CombatMath.X call sites and the "one place
        // formulas live" contract, while every NUMBER is a JSON edit.
        public static float BaseMeleeDamage => GameConfig.Combat.baseMeleeDamage;
        public static float BaseSwingInterval => GameConfig.Combat.baseSwingInterval;

        /// <summary>
        /// Exponential per-level damage growth (config/combat.json: levelGrowth).
        /// Tuned with mob HP on the same curve (monsters.json, hp = 40 *
        /// growth^(level-1), bosses ×3) so equal level fights are real, +15
        /// one-shots mobs, +28 one-shots a boss. Raising it steepens high levels;
        /// keep mob HP on the same curve.
        /// </summary>
        public static float LevelGrowth => GameConfig.Combat.levelGrowth;

        /// <summary>Exponential scale for a skill level: growth^(level-1).</summary>
        public static float LevelScale(float level) =>
            Mathf.Pow(LevelGrowth, Mathf.Max(0f, level - 1f));

        /// <summary>Raw melee damage before the defender's mitigation. Strength-driven.</summary>
        public static float MeleeDamage(StatBlock attacker)
        {
            float weapon = attacker != null ? attacker.Get(StatType.WeaponPower) : 0f;
            float strength = attacker != null ? attacker.Get(StatType.Strength) : 1f;

            float raw = (BaseMeleeDamage + weapon) * LevelScale(strength);
            float v = GameConfig.Combat.damageVariance;
            return raw * Random.Range(1f - v, 1f + v);
        }

        /// <summary>
        /// Spell damage scaled exponentially by Magic, gated by the focus item.
        /// No focus (SpellPower 0) = gate base; SpellPower at cap = 100% output.
        /// See config/combat.json: spellFocusGateBase / PerPower / PowerCap.
        /// </summary>
        public static float SpellDamage(StatBlock caster, float baseDamage)
        {
            var c = GameConfig.Combat;
            float magic = caster != null ? caster.Get(StatType.Magic) : 1f;
            float spellPower = caster != null ? caster.Get(StatType.SpellPower) : 0f;
            float focusGate = c.spellFocusGateBase + c.spellFocusGatePerPower * Mathf.Min(spellPower, c.spellFocusPowerCap);
            return baseDamage * LevelScale(magic) * focusGate;
        }

        /// <summary>Incoming damage reduction from Defence + Armor (config: mitigation*).</summary>
        public static float DamageReduction(StatBlock defender)
        {
            if (defender == null) return 0f;
            var c = GameConfig.Combat;
            float defence = defender.Get(StatType.Defence);
            float armor = defender.Get(StatType.Armor);
            return Mathf.Min(c.mitigationCap, (defence - 1f) * c.mitigationPerDefence + armor * c.mitigationPerArmor);
        }

        /// <summary>
        /// Chance an auto-attack misses outright (a 0). Driven by the defender's
        /// Defence lead over the attacker's Attack. Config: miss*. Abilities and
        /// damage ticks never miss.
        /// </summary>
        public static float MissChance(StatBlock attacker, StatBlock defender)
        {
            var c = GameConfig.Combat;
            float atk = attacker != null ? attacker.Get(StatType.Attack) : 1f;
            float def = defender != null ? defender.Get(StatType.Defence) : 1f;
            return Mathf.Clamp(c.missBase + (def - atk) * c.missPerDefenceLead, c.missFloor, c.missCeil);
        }

        /// <summary>
        /// Seconds between melee swings. AttackSpeed is the gear multiplier; the
        /// Attack skill shaves the interval further (config: swingSpeedPerAttack).
        /// </summary>
        public static float SwingInterval(StatBlock attacker)
        {
            float speed = attacker != null ? Mathf.Max(0.2f, attacker.Get(StatType.AttackSpeed)) : 1f;
            float attack = attacker != null ? attacker.Get(StatType.Attack) : 1f;
            return BaseSwingInterval / speed / (1f + (attack - 1f) * GameConfig.Combat.swingSpeedPerAttack);
        }

        /// <summary>
        /// Cooldown multiplier from a skill level (config: cooldownReductionPerLevel,
        /// cooldownFloor). Attack drives melee-tree abilities, Magic drives spells.
        /// </summary>
        public static float CooldownScale(float level) =>
            Mathf.Max(GameConfig.Combat.cooldownFloor, 1f - (level - 1f) * GameConfig.Combat.cooldownReductionPerLevel);

        /// <summary>OSRS-style combat level summary for display.</summary>
        public static int CombatLevel(StatBlock stats)
        {
            if (stats == null) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(
                (stats.Get(StatType.Attack) + stats.Get(StatType.Strength) +
                 stats.Get(StatType.Defence) + stats.Get(StatType.Magic)) / 4f));
        }
    }
}
