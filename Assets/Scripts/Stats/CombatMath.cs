using UnityEngine;

namespace AldenFall.Stats
{
    /// <summary>
    /// The one place combat formulas live. Every damage event in the game —
    /// player melee, spells, mob attacks — routes through here, reading from
    /// each side's StatBlock.
    ///
    /// v6 skill identities:
    ///   Strength — melee damage (exponential per level)
    ///   Attack   — attack speed + melee-tree ability cooldown reduction
    ///   Magic    — spell damage (exponential) + magic cooldown reduction
    ///   Defence  — damage reduction + chance attackers miss entirely
    /// </summary>
    public static class CombatMath
    {
        public const float BaseMeleeDamage = 5f;
        public const float BaseSwingInterval = 1.9f;

        /// <summary>
        /// Exponential per-level damage growth. Tuned with mob HP on the same
        /// curve (monsters.json, hp = 40 * growth^(level-1), bosses ×3) so:
        ///   equal level    → abilities hit ~30% of HP (a real fight)
        ///   +15 levels     → biggest hits one-shot regular mobs
        ///   +28 levels     → one-shot a boss (3 hits at +15)
        /// e.g. 99 Magic obliterates a level-18 boss; one-hitting it starts
        /// around level 45-50.
        /// </summary>
        public const float LevelGrowth = 1.085f;

        /// <summary>Exponential scale for a skill level: growth^(level-1).</summary>
        public static float LevelScale(float level) =>
            Mathf.Pow(LevelGrowth, Mathf.Max(0f, level - 1f));

        /// <summary>Raw melee damage before the defender's mitigation. Strength-driven.</summary>
        public static float MeleeDamage(StatBlock attacker)
        {
            float weapon = attacker != null ? attacker.Get(StatType.WeaponPower) : 0f;
            float strength = attacker != null ? attacker.Get(StatType.Strength) : 1f;

            float raw = (BaseMeleeDamage + weapon) * LevelScale(strength);
            return raw * Random.Range(0.85f, 1.15f);
        }

        /// <summary>
        /// Spell damage scaled exponentially by Magic, gated by the focus item.
        /// No focus (SpellPower 0) = 60% output; SpellPower 10 (boss focus) = 100%.
        /// </summary>
        public static float SpellDamage(StatBlock caster, float baseDamage)
        {
            float magic = caster != null ? caster.Get(StatType.Magic) : 1f;
            float spellPower = caster != null ? caster.Get(StatType.SpellPower) : 0f;
            float focusGate = 0.6f + 0.04f * Mathf.Min(spellPower, 10f);
            return baseDamage * LevelScale(magic) * focusGate;
        }

        /// <summary>Incoming damage reduction 0..0.75 from Defence + Armor.</summary>
        public static float DamageReduction(StatBlock defender)
        {
            if (defender == null) return 0f;
            float defence = defender.Get(StatType.Defence);
            float armor = defender.Get(StatType.Armor);
            return Mathf.Min(0.75f, (defence - 1f) * 0.01f + armor * 0.03f);
        }

        /// <summary>
        /// Chance an auto-attack misses outright (a 0). Driven by the
        /// defender's Defence lead over the attacker's Attack: equal footing
        /// is a 10% miss, a level-5 goblin swinging at 20+ Defence whiffs
        /// ~75-95% of the time. Abilities and damage ticks never miss.
        /// </summary>
        public static float MissChance(StatBlock attacker, StatBlock defender)
        {
            float atk = attacker != null ? attacker.Get(StatType.Attack) : 1f;
            float def = defender != null ? defender.Get(StatType.Defence) : 1f;
            return Mathf.Clamp(0.10f + (def - atk) * 0.035f, 0.05f, 0.95f);
        }

        /// <summary>
        /// Seconds between melee swings. AttackSpeed is the gear multiplier;
        /// the Attack skill shaves the interval further (~1.8x rate at 99).
        /// </summary>
        public static float SwingInterval(StatBlock attacker)
        {
            float speed = attacker != null ? Mathf.Max(0.2f, attacker.Get(StatType.AttackSpeed)) : 1f;
            float attack = attacker != null ? attacker.Get(StatType.Attack) : 1f;
            return BaseSwingInterval / speed / (1f + (attack - 1f) * 0.008f);
        }

        /// <summary>
        /// Cooldown multiplier from a skill level: Attack drives melee-tree
        /// abilities, Magic drives spells. ~45% reduction at 99, floor 55%.
        /// </summary>
        public static float CooldownScale(float level) =>
            Mathf.Max(0.55f, 1f - (level - 1f) * 0.0046f);

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
