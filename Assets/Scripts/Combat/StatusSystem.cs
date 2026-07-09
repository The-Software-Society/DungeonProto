using System;
using System.Collections.Generic;
using UnityEngine;
using AldenFall.Stats;

namespace AldenFall.Combat
{
    [Flags]
    public enum StatusFlags
    {
        None = 0,
        Stunned = 1 << 0,      // no movement, no attacks, AI frozen
        Silenced = 1 << 1,     // no abilities (autos still allowed)
        CombatLocked = 1 << 2, // no combat actions at all (eating)
    }

    /// <summary>
    /// One active effect: stun, burn, buff, debuff, lockout. Plain data —
    /// created by whatever applies it (abilities, food, enemy attacks).
    /// </summary>
    public class StatusEffect
    {
        public string Id;                              // "stun", "burn", "eating", "bulwark"
        public string DisplayName;
        public float Duration;
        public StatusFlags Flags;
        public List<(StatType stat, float amount)> StatMods; // routed through StatBlock modifiers
        public float TickInterval;                     // 0 = no tick
        public float TickDamage;                       // burn/poison damage per tick
        public float MoveSpeedMultiplier;              // 0 = no effect; 0.5 = half speed (slows)
        public GameObject Source;                      // attacker credited for tick damage
    }

    /// <summary>
    /// Per-entity status effects for player AND mobs. Stat changes are applied
    /// as StatBlock modifiers keyed "status:{id}", so buffs/debuffs flow through
    /// the same single source of truth as gear — CombatMath never needs to know.
    /// Damage ticks route through Health.TakeDamage (the one damage path).
    /// </summary>
    public class StatusSystem : MonoBehaviour
    {
        private class Active
        {
            public StatusEffect Effect;
            public float ExpiresAt;
            public float NextTickAt;
        }

        private readonly Dictionary<string, Active> active = new Dictionary<string, Active>();
        private static readonly List<string> scratchKeys = new List<string>();

        private StatBlock stats;
        private Health health;

        public event Action<StatusEffect> OnApplied;
        public event Action<string> OnRemoved;

        public bool IsStunned => HasFlag(StatusFlags.Stunned);
        public bool IsSilenced => HasFlag(StatusFlags.Silenced);
        public bool IsCombatLocked => HasFlag(StatusFlags.CombatLocked);

        /// <summary>"invisible" is the aggro-drop status (Veil) — EnemyAI checks it.</summary>
        public bool IsInvisible => Has("invisible");

        /// <summary>Combined movement slow: the strongest active slow wins (1 = unslowed).</summary>
        public float MoveSpeedFactor
        {
            get
            {
                float factor = 1f;
                foreach (var a in active.Values)
                    if (a.Effect.MoveSpeedMultiplier > 0f && a.Effect.MoveSpeedMultiplier < factor)
                        factor = a.Effect.MoveSpeedMultiplier;
                return factor;
            }
        }

        private void Awake()
        {
            stats = GetComponent<StatBlock>();
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            if (health != null)
                health.OnDeath.AddListener(ClearAll);
        }

        private void OnDisable()
        {
            if (health != null)
                health.OnDeath.RemoveListener(ClearAll);
        }

        public void Apply(StatusEffect effect)
        {
            if (effect == null || string.IsNullOrEmpty(effect.Id)) return;
            if (health != null && health.IsDead) return;

            active[effect.Id] = new Active
            {
                Effect = effect,
                ExpiresAt = Time.time + effect.Duration,
                NextTickAt = effect.TickInterval > 0f ? Time.time + effect.TickInterval : float.PositiveInfinity,
            };

            if (stats != null && effect.StatMods != null && effect.StatMods.Count > 0)
                stats.ApplyModifiers(SourceKey(effect.Id), effect.StatMods);

            OnApplied?.Invoke(effect);
        }

        public void Remove(string id)
        {
            if (!active.Remove(id)) return;
            stats?.ClearModifiers(SourceKey(id));
            OnRemoved?.Invoke(id);
        }

        public bool Has(string id) => active.ContainsKey(id);

        public float Remaining(string id) =>
            active.TryGetValue(id, out var a) ? Mathf.Max(0f, a.ExpiresAt - Time.time) : 0f;

        public void ClearAll()
        {
            scratchKeys.Clear();
            scratchKeys.AddRange(active.Keys);
            foreach (var id in scratchKeys)
                Remove(id);
        }

        private void Update()
        {
            if (active.Count == 0) return;

            scratchKeys.Clear();
            scratchKeys.AddRange(active.Keys);

            foreach (var id in scratchKeys)
            {
                if (!active.TryGetValue(id, out var a)) continue;

                if (Time.time >= a.NextTickAt && a.Effect.TickDamage > 0f && health != null && !health.IsDead)
                {
                    health.TakeDamage(a.Effect.TickDamage, a.Effect.Source);
                    a.NextTickAt += a.Effect.TickInterval;
                }

                if (Time.time >= a.ExpiresAt)
                    Remove(id);
            }
        }

        private bool HasFlag(StatusFlags flag)
        {
            foreach (var a in active.Values)
                if ((a.Effect.Flags & flag) != 0)
                    return true;
            return false;
        }

        private static string SourceKey(string id) => $"status:{id}";
    }
}
