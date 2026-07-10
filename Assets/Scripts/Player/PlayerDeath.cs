using UnityEngine;

namespace AldenFall.Player
{
    using Combat;

    [RequireComponent(typeof(Health))]
    public class PlayerDeath : MonoBehaviour
    {
        [Header("Death Settings")]
        [SerializeField] private float respawnDelay = 3f;
        [SerializeField] private Transform respawnPoint;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        [Header("Components to Disable on Death")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private Abilities.PlayerAbilities playerAbilities;
        [SerializeField] private TargetingSystem targetingSystem;

        private Health health;
        private CharacterController characterController;
        private bool isDead;

        // Animation hashes
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int RespawnHash = Animator.StringToHash("Respawn");

        private void Awake()
        {
            health = GetComponent<Health>();
            characterController = GetComponent<CharacterController>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (playerController == null)
                playerController = GetComponent<PlayerController>();

            if (playerAbilities == null)
                playerAbilities = GetComponent<Abilities.PlayerAbilities>();

            if (targetingSystem == null)
                targetingSystem = GetComponent<TargetingSystem>();
        }

        private void Start()
        {
            health.OnDeath.AddListener(OnDeath);

            // Create default respawn point if not set
            if (respawnPoint == null)
            {
                var respawnObj = new GameObject("PlayerRespawnPoint");
                respawnPoint = respawnObj.transform;
                respawnPoint.position = transform.position;
            }
        }

        private void OnDeath()
        {
            if (isDead) return;
            isDead = true;

            // Disable player controls
            if (playerController != null)
                playerController.enabled = false;

            if (playerAbilities != null)
                playerAbilities.enabled = false;

            if (targetingSystem != null)
            {
                targetingSystem.ClearTarget();
                targetingSystem.enabled = false;
            }

            // Play death animation
            if (animator != null)
            {
                animator.SetBool(IsDeadHash, true);
                animator.SetTrigger(DeathHash);
            }

            // Start respawn timer
            Invoke(nameof(Respawn), respawnDelay);
        }

        private void Respawn()
        {
            isDead = false;

            // Reset health
            health.ResetHealth();

            // Teleport to respawn point
            if (characterController != null)
            {
                characterController.enabled = false;
                transform.position = respawnPoint.position;
                transform.rotation = respawnPoint.rotation;
                characterController.enabled = true;
            }
            else
            {
                transform.position = respawnPoint.position;
                transform.rotation = respawnPoint.rotation;
            }

            // Re-enable controls
            if (playerController != null)
                playerController.enabled = true;

            if (playerAbilities != null)
                playerAbilities.enabled = true;

            if (targetingSystem != null)
                targetingSystem.enabled = true;

            // Play respawn animation
            if (animator != null)
            {
                animator.SetBool(IsDeadHash, false);
                animator.SetTrigger(RespawnHash);
            }
        }

        public void SetRespawnPoint(Transform newRespawnPoint)
        {
            respawnPoint = newRespawnPoint;
        }

        private void OnDrawGizmosSelected()
        {
            if (respawnPoint != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(respawnPoint.position, 0.5f);
                Gizmos.DrawLine(transform.position, respawnPoint.position);
            }
        }
    }
}
