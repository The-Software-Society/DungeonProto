using UnityEngine;

namespace DungeonProto.Combat
{
    public class PlayerCombat : MonoBehaviour
    {
        [Header("Fireball (Key 1)")]
        [SerializeField] private GameObject fireballPrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireballDamage = 25f;
        [SerializeField] private float fireballSpeed = 12f;
        [SerializeField] private float fireballCooldown = 1f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private TargetingSystem targetingSystem;
        private float lastFireballTime;

        // Animation hashes
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");

        private void Awake()
        {
            targetingSystem = GetComponent<TargetingSystem>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (firePoint == null)
            {
                // Create default fire point
                var firePointObj = new GameObject("FirePoint");
                firePoint = firePointObj.transform;
                firePoint.SetParent(transform);
                firePoint.localPosition = new Vector3(0, 1.2f, 0.5f);
            }
        }

        private void Update()
        {
            HandleFireballInput();
        }

        private void HandleFireballInput()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                TryFireball();
            }
        }

        private void TryFireball()
        {
            if (Time.time - lastFireballTime < fireballCooldown) return;
            if (fireballPrefab == null) return;

            lastFireballTime = Time.time;

            // Trigger animation
            if (animator != null)
            {
                animator.SetTrigger(SpecialAttackHash);
            }

            // Spawn fireball
            SpawnFireball();
        }

        private void SpawnFireball()
        {
            Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position + Vector3.up;
            GameObject fireball = Instantiate(fireballPrefab, spawnPos, Quaternion.identity);

            var projectile = fireball.GetComponent<Projectile>();
            if (projectile != null)
            {
                // Get direction - towards target if we have one, otherwise forward
                Vector3 direction = transform.forward;
                Transform target = null;

                if (targetingSystem != null && targetingSystem.HasTarget)
                {
                    target = targetingSystem.CurrentTarget;
                    direction = (target.position + Vector3.up - spawnPos).normalized;
                }

                projectile.Initialize(direction, gameObject, target);
                projectile.SetDamage(fireballDamage);
                projectile.SetSpeed(fireballSpeed);
            }
        }

        // Called by animation event if needed
        public void OnAttackHit()
        {
            // For melee attacks later
        }

        private void OnDrawGizmosSelected()
        {
            if (firePoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(firePoint.position, 0.1f);
                Gizmos.DrawRay(firePoint.position, transform.forward * 2f);
            }
        }
    }
}
