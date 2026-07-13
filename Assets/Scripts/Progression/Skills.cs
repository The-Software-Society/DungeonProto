using UnityEngine;
using UnityEngine.Events;
using AldenFall.Data;

namespace AldenFall.Progression
{
    public enum SkillType
    {
        Attack = 0,
        Strength = 1,
        Defence = 2,
        Magic = 3,
        Fishing = 4,   // append-only enum, serialized indices
        Cooking = 5,
    }

    /// <summary>
    /// OSRS-inspired use-based skill progression. Melee autos train the
    /// SELECTED combat style (CombatStyleController), abilities train their
    /// tree, spells train Magic, fish/cook train themselves. Level effects
    /// live in CombatMath: Strength = melee damage (exponential), Attack =
    /// attack speed + melee cooldowns, Magic = spell damage + spell
    /// cooldowns, Defence = damage reduction + attacker miss chance.
    /// </summary>
    public class Skills : MonoBehaviour
    {
        public const int SkillCount = 6;

        /// <summary>The level clamp. Reads config/progression.json (practicalCap, 99 today).
        /// hardCap (500) is reserved for the secret-cap work in WORLD1_COMPLETION_PLAN F5.</summary>
        public static int MaxLevel => GameConfig.Progression.practicalCap;

        [Header("Events")]
        public UnityEvent<SkillType, float> OnXpGained;      // skill, xp amount
        public UnityEvent<SkillType, int> OnLevelUp;          // skill, new level

        private readonly int[] levels = { 1, 1, 1, 1, 1, 1 };
        private readonly float[] xp = new float[SkillCount];

        public int Level(SkillType skill) => levels[(int)skill];
        public float Xp(SkillType skill) => xp[(int)skill];

        /// <summary>Total XP required to reach the given level (from level 1).
        /// Curve knobs (xpCoefficient, xpExponent) live in config/progression.json.
        /// Default L2=50xp, L5=~450, L10=~1900, L20=~8300.</summary>
        public static float XpForLevel(int level)
        {
            if (level <= 1) return 0f;
            var p = GameConfig.Progression;
            float total = 0f;
            for (int l = 1; l < level; l++)
                total += Mathf.Floor(p.xpCoefficient * Mathf.Pow(l, p.xpExponent));
            return total;
        }

        /// <summary>Progress toward the next level, 0..1.</summary>
        public float Progress01(SkillType skill)
        {
            int level = Level(skill);
            if (level >= MaxLevel) return 1f;
            float current = XpForLevel(level);
            float next = XpForLevel(level + 1);
            return Mathf.InverseLerp(current, next, Xp(skill));
        }

        public float XpToNextLevel(SkillType skill)
        {
            int level = Level(skill);
            if (level >= MaxLevel) return 0f;
            return XpForLevel(level + 1) - Xp(skill);
        }

        public void AddXp(SkillType skill, float amount)
        {
            if (amount <= 0f) return;
            int index = (int)skill;
            xp[index] += amount;
            OnXpGained?.Invoke(skill, amount);

            while (levels[index] < MaxLevel && xp[index] >= XpForLevel(levels[index] + 1))
            {
                levels[index]++;
                OnLevelUp?.Invoke(skill, levels[index]);
            }
        }

        /// <summary>
        /// Testing/debug: force a level directly. XP snaps to the level floor
        /// and OnLevelUp fires so stat bridges and UI stay in sync.
        /// </summary>
        public void SetLevel(SkillType skill, int level)
        {
            int index = (int)skill;
            level = Mathf.Clamp(level, 1, MaxLevel);
            if (levels[index] == level) return;
            levels[index] = level;
            xp[index] = XpForLevel(level);
            OnLevelUp?.Invoke(skill, level);
        }

        // Combat formulas live in Stats.CombatMath (single source of truth) -
        // skill levels flow there through PlayerStatBridge → StatBlock.

        public static string DisplayName(SkillType skill) => skill switch
        {
            SkillType.Attack => "Attack",
            SkillType.Strength => "Strength",
            SkillType.Defence => "Defence",
            SkillType.Magic => "Magic",
            SkillType.Fishing => "Fishing",
            SkillType.Cooking => "Cooking",
            _ => skill.ToString(),
        };
    }
}
