using UnityEngine;
using AldenFall.Progression;

namespace AldenFall.Stats
{
    /// <summary>
    /// Keeps the player's StatBlock base values in sync with skill levels.
    /// Equipment writes its own modifiers separately (per-slot sources), so
    /// StatBlock.Get always answers skills + gear in one call.
    /// </summary>
    [RequireComponent(typeof(StatBlock), typeof(Skills))]
    public class PlayerStatBridge : MonoBehaviour
    {
        private StatBlock stats;
        private Skills skills;

        private void Awake()
        {
            stats = GetComponent<StatBlock>();
            skills = GetComponent<Skills>();
        }

        private void Start()
        {
            skills.OnLevelUp.AddListener((_, _2) => Sync());
            Sync();
        }

        private void Sync()
        {
            stats.SetBase(StatType.Attack, skills.Level(SkillType.Attack));
            stats.SetBase(StatType.Strength, skills.Level(SkillType.Strength));
            stats.SetBase(StatType.Defence, skills.Level(SkillType.Defence));
            stats.SetBase(StatType.Magic, skills.Level(SkillType.Magic));
        }
    }
}
