using UnityEngine;
using UnityEngine.Events;

namespace DungeonProto.Combat
{
    public class Health : MonoBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth;

        [Header("Events")]
        public UnityEvent<float, float> OnHealthChanged; // current, max
        public UnityEvent OnDeath;
        public UnityEvent<float> OnDamaged; // damage amount
        public UnityEvent<float> OnHealed;  // heal amount

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;
        public bool IsDead => currentHealth <= 0;
        public bool IsAlive => currentHealth > 0;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        private void Start()
        {
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public void TakeDamage(float damage, GameObject attacker = null)
        {
            if (IsDead || damage <= 0) return;

            currentHealth = Mathf.Max(0, currentHealth - damage);
            OnDamaged?.Invoke(damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            if (IsDead)
            {
                Die();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0) return;

            float oldHealth = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            float actualHeal = currentHealth - oldHealth;

            if (actualHeal > 0)
            {
                OnHealed?.Invoke(actualHeal);
                OnHealthChanged?.Invoke(currentHealth, maxHealth);
            }
        }

        public void SetMaxHealth(float newMax, bool healToFull = false)
        {
            maxHealth = Mathf.Max(1, newMax);
            if (healToFull)
            {
                currentHealth = maxHealth;
            }
            else
            {
                currentHealth = Mathf.Min(currentHealth, maxHealth);
            }
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public void ResetHealth()
        {
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Die()
        {
            OnDeath?.Invoke();
        }

        [ContextMenu("Test Damage (10)")]
        private void TestDamage()
        {
            TakeDamage(10);
        }

        [ContextMenu("Test Heal (10)")]
        private void TestHeal()
        {
            Heal(10);
        }
    }
}
