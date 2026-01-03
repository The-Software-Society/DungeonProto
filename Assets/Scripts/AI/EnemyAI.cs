using UnityEngine;
using UnityEngine.AI;

namespace DungeonProto.AI
{
    using Combat;

    public enum EnemyState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Dead
    }

    [RequireComponent(typeof(Health))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float loseTargetRange = 15f;
        [SerializeField] private LayerMask playerLayer;

        [Header("Combat")]
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private float attackDuration = 0.5f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.5f;
        [SerializeField] private float rotationSpeed = 5f;

        [Header("Patrol")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolWaitTime = 2f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private EnemyState currentState = EnemyState.Idle;
        private NavMeshAgent agent;
        private Health health;
        private Transform playerTarget;
        private int currentPatrolIndex;
        private float lastAttackTime;
        private float patrolWaitTimer;
        private bool isAttacking;

        // Animation hashes
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");

        public EnemyState CurrentState => currentState;
        public bool IsAlive => health != null && health.IsAlive;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();

            if (agent != null)
            {
                agent.speed = moveSpeed;
            }
        }

        private void Start()
        {
            health.OnDeath.AddListener(OnDeath);

            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                currentState = EnemyState.Patrol;
            }
        }

        private void Update()
        {
            if (!IsAlive) return;

            UpdateStateMachine();
            UpdateAnimator();
        }

        private void UpdateStateMachine()
        {
            // Check for player
            CheckForPlayer();

            switch (currentState)
            {
                case EnemyState.Idle:
                    HandleIdle();
                    break;
                case EnemyState.Patrol:
                    HandlePatrol();
                    break;
                case EnemyState.Chase:
                    HandleChase();
                    break;
                case EnemyState.Attack:
                    HandleAttack();
                    break;
            }
        }

        private void CheckForPlayer()
        {
            if (playerTarget == null)
            {
                // Look for player
                Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange, playerLayer);
                foreach (var col in colliders)
                {
                    var playerHealth = col.GetComponent<Health>();
                    if (playerHealth != null && playerHealth.IsAlive)
                    {
                        playerTarget = col.transform;
                        currentState = EnemyState.Chase;
                        break;
                    }
                }
            }
            else
            {
                // Check if player is still valid target
                var targetHealth = playerTarget.GetComponent<Health>();
                if (targetHealth == null || targetHealth.IsDead)
                {
                    LoseTarget();
                    return;
                }

                // Check if player is too far
                float distance = Vector3.Distance(transform.position, playerTarget.position);
                if (distance > loseTargetRange)
                {
                    LoseTarget();
                }
            }
        }

        private void LoseTarget()
        {
            playerTarget = null;
            currentState = patrolPoints != null && patrolPoints.Length > 0 ? EnemyState.Patrol : EnemyState.Idle;
        }

        private void HandleIdle()
        {
            if (agent != null)
                agent.isStopped = true;
        }

        private void HandlePatrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0) return;

            if (agent == null || !agent.isOnNavMesh) return;

            // Check if we've reached the patrol point
            if (!agent.pathPending && agent.remainingDistance < 0.5f)
            {
                patrolWaitTimer += Time.deltaTime;
                agent.isStopped = true;

                if (patrolWaitTimer >= patrolWaitTime)
                {
                    patrolWaitTimer = 0f;
                    currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
                    agent.isStopped = false;
                    agent.SetDestination(patrolPoints[currentPatrolIndex].position);
                }
            }
            else
            {
                agent.isStopped = false;
                if (agent.destination != patrolPoints[currentPatrolIndex].position)
                {
                    agent.SetDestination(patrolPoints[currentPatrolIndex].position);
                }
            }
        }

        private void HandleChase()
        {
            if (playerTarget == null) return;

            float distance = Vector3.Distance(transform.position, playerTarget.position);

            if (distance <= attackRange)
            {
                currentState = EnemyState.Attack;
                if (agent != null)
                    agent.isStopped = true;
            }
            else
            {
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.isStopped = false;
                    agent.SetDestination(playerTarget.position);
                }
            }

            // Face the player
            FaceTarget(playerTarget.position);
        }

        private void HandleAttack()
        {
            if (playerTarget == null)
            {
                currentState = EnemyState.Idle;
                return;
            }

            float distance = Vector3.Distance(transform.position, playerTarget.position);

            if (distance > attackRange * 1.2f)
            {
                currentState = EnemyState.Chase;
                return;
            }

            // Face the player
            FaceTarget(playerTarget.position);

            // Attack if cooldown is ready
            if (!isAttacking && Time.time - lastAttackTime >= attackCooldown)
            {
                StartAttack();
            }
        }

        private void StartAttack()
        {
            isAttacking = true;
            lastAttackTime = Time.time;

            if (animator != null)
                animator.SetTrigger(AttackHash);

            // Deal damage after a short delay (animation timing)
            Invoke(nameof(DealDamage), attackDuration * 0.5f);
            Invoke(nameof(EndAttack), attackDuration);
        }

        private void DealDamage()
        {
            if (playerTarget == null) return;

            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (distance <= attackRange * 1.2f)
            {
                var targetHealth = playerTarget.GetComponent<Health>();
                if (targetHealth != null)
                {
                    targetHealth.TakeDamage(attackDamage, gameObject);
                }
            }
        }

        private void EndAttack()
        {
            isAttacking = false;
        }

        private void FaceTarget(Vector3 targetPos)
        {
            Vector3 direction = (targetPos - transform.position).normalized;
            direction.y = 0;

            if (direction != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float speed = 0f;
            if (agent != null && !agent.isStopped)
            {
                speed = agent.velocity.magnitude / moveSpeed;
            }

            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
        }

        private void OnDeath()
        {
            currentState = EnemyState.Dead;

            if (agent != null)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }

            if (animator != null)
            {
                animator.SetBool(IsDeadHash, true);
                animator.SetTrigger(DeathHash);
            }

            // Disable collider after short delay
            Invoke(nameof(DisableCollider), 0.5f);

            // Destroy after longer delay
            Destroy(gameObject, 5f);
        }

        private void DisableCollider()
        {
            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;
        }

        private void OnDrawGizmosSelected()
        {
            // Detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);

            // Attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Lose target range
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, loseTargetRange);

            // Patrol path
            if (patrolPoints != null && patrolPoints.Length > 1)
            {
                Gizmos.color = Color.blue;
                for (int i = 0; i < patrolPoints.Length; i++)
                {
                    if (patrolPoints[i] == null) continue;

                    int nextIndex = (i + 1) % patrolPoints.Length;
                    if (patrolPoints[nextIndex] != null)
                    {
                        Gizmos.DrawLine(patrolPoints[i].position, patrolPoints[nextIndex].position);
                    }
                    Gizmos.DrawWireSphere(patrolPoints[i].position, 0.3f);
                }
            }
        }
    }
}
