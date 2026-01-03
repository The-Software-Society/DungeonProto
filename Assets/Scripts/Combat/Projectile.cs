using UnityEngine;

namespace DungeonProto.Combat
{
    public class Projectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float speed = 15f;
        [SerializeField] private float homingStrength = 5f;
        [SerializeField] private float maxLifetime = 5f;

        [Header("Damage")]
        [SerializeField] private float damage = 20f;
        [SerializeField] private LayerMask hitLayers;

        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private bool destroyOnHit = true;

        private Transform target;
        private Vector3 direction;
        private GameObject owner;
        private bool isHoming;
        private float lifetime;

        public void Initialize(Vector3 fireDirection, GameObject projectileOwner, Transform homingTarget = null)
        {
            direction = fireDirection.normalized;
            owner = projectileOwner;
            target = homingTarget;
            isHoming = homingTarget != null;
            lifetime = 0f;

            // Face the direction of travel
            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }

        public void SetDamage(float newDamage)
        {
            damage = newDamage;
        }

        public void SetSpeed(float newSpeed)
        {
            speed = newSpeed;
        }

        private void Update()
        {
            lifetime += Time.deltaTime;
            if (lifetime >= maxLifetime)
            {
                Destroy(gameObject);
                return;
            }

            MoveProjectile();
        }

        private void MoveProjectile()
        {
            if (isHoming && target != null)
            {
                // Calculate direction to target
                Vector3 targetDirection = (target.position + Vector3.up - transform.position).normalized;

                // Smoothly rotate towards target
                direction = Vector3.Slerp(direction, targetDirection, homingStrength * Time.deltaTime);
                transform.rotation = Quaternion.LookRotation(direction);
            }

            // Move forward
            transform.position += direction * speed * Time.deltaTime;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Don't hit owner
            if (owner != null && other.gameObject == owner) return;
            if (other.transform.IsChildOf(owner?.transform)) return;

            // Check if we should hit this layer
            if (((1 << other.gameObject.layer) & hitLayers) == 0) return;

            // Apply damage if target has Health component
            var health = other.GetComponent<Health>();
            if (health != null)
            {
                health.TakeDamage(damage, owner);
            }

            // Spawn hit effect
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            }

            if (destroyOnHit)
            {
                Destroy(gameObject);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(transform.position, direction * 2f);

            if (target != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, target.position);
            }
        }
    }
}
