using System;
using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.Stats
{
    public enum StatType
    {
        MaxHealth,
        Attack,       // accuracy / melee skill level
        Strength,     // melee damage scaling
        Defence,      // incoming damage reduction
        Magic,        // spell damage scaling
        Armor,        // flat equipment mitigation bonus
        WeaponPower,  // flat equipment damage bonus
        AttackSpeed,  // swings per second multiplier (1 = base)
        MoveSpeed,    // movement multiplier (1 = base)
        SpellPower,   // focus item bonus that gates spell damage (append-only enum!)
    }

    /// <summary>
    /// Single source of truth for an entity's combat stats. Attached to the
    /// player AND every mob. Final value = base + all modifiers.
    ///
    ///   base values  — what the entity IS (mob templates, player skill levels)
    ///   modifiers    — what the entity HAS (equipment, buffs), keyed by source
    ///                  so a source can be cleanly replaced or removed
    ///
    /// Everything that deals or takes damage reads through this + CombatMath.
    /// </summary>
    public class StatBlock : MonoBehaviour
    {
        [Serializable]
        public struct BaseStat
        {
            public StatType type;
            public float value;
        }

        [SerializeField] private List<BaseStat> baseStats = new List<BaseStat>();

        private Dictionary<StatType, float> baseLookup;
        private readonly Dictionary<string, List<(StatType type, float value)>> modifiers
            = new Dictionary<string, List<(StatType, float)>>();

        /// <summary>Raised whenever base stats or modifiers change.</summary>
        public event Action OnChanged;

        private static readonly Dictionary<StatType, float> Defaults = new Dictionary<StatType, float>
        {
            { StatType.MaxHealth, 100f },
            { StatType.Attack, 1f },
            { StatType.Strength, 1f },
            { StatType.Defence, 1f },
            { StatType.Magic, 1f },
            { StatType.Armor, 0f },
            { StatType.WeaponPower, 0f },
            { StatType.AttackSpeed, 1f },
            { StatType.MoveSpeed, 1f },
            { StatType.SpellPower, 0f },
        };

        private void EnsureLookup()
        {
            if (baseLookup != null) return;
            baseLookup = new Dictionary<StatType, float>();
            foreach (var stat in baseStats)
                baseLookup[stat.type] = stat.value;
        }

        public float GetBase(StatType type)
        {
            EnsureLookup();
            if (baseLookup.TryGetValue(type, out float value)) return value;
            return Defaults[type];
        }

        public void SetBase(StatType type, float value)
        {
            EnsureLookup();
            baseLookup[type] = value;

            // Keep the serialized list in sync (editor-built prefabs)
            int index = baseStats.FindIndex(s => s.type == type);
            var entry = new BaseStat { type = type, value = value };
            if (index >= 0) baseStats[index] = entry; else baseStats.Add(entry);

            OnChanged?.Invoke();
        }

        /// <summary>Final stat: base + all modifier contributions.</summary>
        public float Get(StatType type)
        {
            float total = GetBase(type);
            foreach (var mods in modifiers.Values)
                foreach (var (modType, value) in mods)
                    if (modType == type)
                        total += value;
            return total;
        }

        /// <summary>Sum of modifier contributions only (equipment bonuses display).</summary>
        public float GetModifierTotal(StatType type)
        {
            float total = 0f;
            foreach (var mods in modifiers.Values)
                foreach (var (modType, value) in mods)
                    if (modType == type)
                        total += value;
            return total;
        }

        /// <summary>Replace all modifiers from a source (e.g. an equipment slot).</summary>
        public void ApplyModifiers(string source, IEnumerable<(StatType type, float value)> mods)
        {
            modifiers[source] = new List<(StatType, float)>(mods);
            OnChanged?.Invoke();
        }

        public void ClearModifiers(string source)
        {
            if (modifiers.Remove(source))
                OnChanged?.Invoke();
        }
    }
}
