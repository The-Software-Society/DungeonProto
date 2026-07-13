using UnityEngine;
using AldenFall.Progression;

namespace AldenFall.Combat
{
    /// <summary>
    /// The Health skill: the body toughens under fire, so taking damage grants
    /// Health XP (scaled by the hit). Levels feed max HP through the StatBlock
    /// (PlayerStatBridge). Hooked to Health.OnDamaged; client-local like the other
    /// skills (host reads the local player; pure-client grant is a Phase-13 item).
    /// </summary>
    public class HealthSkill : MonoBehaviour
    {
        [SerializeField] private float xpPerDamage = 0.8f;

        private Skills skills;
        private Health health;

        private void Awake()
        {
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
        }

        private void OnEnable()
        {
            if (health != null && health.OnDamaged != null)
                health.OnDamaged.AddListener(OnDamaged);
        }

        private void OnDisable()
        {
            if (health != null && health.OnDamaged != null)
                health.OnDamaged.RemoveListener(OnDamaged);
        }

        private void OnDamaged(float amount)
        {
            if (amount > 0.5f)
                skills?.AddXp(SkillType.Health, amount * xpPerDamage);
        }
    }
}
