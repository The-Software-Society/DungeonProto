using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Mirror;

namespace AldenFall.AI
{
    using Combat;

    public enum EnemyState
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        JumpAttack,
        ChargeAttack,
        Dead
    }

    [RequireComponent(typeof(Health))]
    public class EnemyAI : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] private float detectionRange = 25f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float loseTargetRange = 35f;
        [SerializeField] private LayerMask playerLayer;
        [SerializeField] private float detectionInterval = 0.2f;

        [Header("Combat - Basic Attack")]
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private float attackDuration = 0.5f;
        [SerializeField] private float basicKnockback = 0f;   // basic attacks never knock back by default

        [Header("Combat - Jump Attack")]
        [SerializeField] private bool jumpAttackEnabled = true;
        [SerializeField] private float jumpAttackMinRange = 4f;
        [SerializeField] private float jumpAttackMaxRange = 8f;
        [SerializeField] private float jumpAttackDamage = 15f;
        [SerializeField] private float jumpAttackCooldown = 5f;
        [SerializeField] private float jumpAttackDuration = 0.8f;
        [SerializeField] private float jumpAttackArcHeight = 3f;
        [SerializeField] private float jumpAttackKnockback = 0f;  // boss-only special

        [Header("Combat - Charge Attack")]
        [SerializeField] private bool chargeAttackEnabled = true;
        [SerializeField] private float chargeAttackMinRange = 5f;
        [SerializeField] private float chargeAttackMaxRange = 10f;
        [SerializeField] private float chargeAttackDamage = 25f;
        [SerializeField] private float chargeAttackCooldown = 8f;
        [SerializeField] private float chargeWindupTime = 0.6f;
        [SerializeField] private float chargeSpeed = 18f;
        [SerializeField] private float chargeMaxDuration = 1.5f;
        [SerializeField] private float chargeStunDuration = 0.5f;
        [SerializeField] private float chargeKnockback = 0f;      // boss-only special
        [Range(0f, 1f)]
        [SerializeField] private float chargeChance = 0.3f;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 3.5f;
        [SerializeField] private float runSpeed = 6f;
        [SerializeField] private float rotationSpeed = 5f;

        [Header("Navigation Jumping")]
        [SerializeField] private float linkTraversalSpeed = 5f;

        [Header("Patrol")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolWaitTime = 2f;
        [Tooltip("Off for stationary mobs (dancing goblins): no auto patrol points, stays Idle until aggroed.")]
        [SerializeField] private bool autoGeneratePatrol = true;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        private NetworkAnimator netAnimator;

        // State
        private EnemyState currentState = EnemyState.Idle;
        private NavMeshAgent agent;
        private Health health;
        private Combat.StatusSystem statuses;
        private Transform playerTarget;
        private int currentPatrolIndex;
        private float patrolWaitTimer;

        // Basic attack
        private float lastAttackTime;
        private bool isAttacking;
        private bool stunLocked; // set while a stun froze this AI, so only stun expiry resets state

        // Jump attack
        private float lastJumpAttackTime;
        private bool isJumpAttacking;
        private Vector3 jumpAttackStartPos;
        private Vector3 jumpAttackTargetPos;
        private float jumpAttackTimer;
        private bool jumpAttackHit;

        // Charge attack
        private float lastChargeAttackTime;
        private bool isWindingUp;
        private bool isCharging;
        private float chargeTimer;
        private Vector3 chargeDirection;
        private bool chargeHitSomething;

        // Detection optimization
        private float detectionTimer;

        // Navigation jump
        private bool isTraversingLink;

        // Auto-generated patrol points
        private GameObject patrolPointContainer;
        private Vector3 spawnPosition;

        // Animation hashes
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DeathHash = Animator.StringToHash("Death");
        private static readonly int IsDeadHash = Animator.StringToHash("IsDead");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int IsJumpingHash = Animator.StringToHash("IsJumping");
        private static readonly int JumpAttackHash = Animator.StringToHash("JumpAttack");
        private static readonly int ChargeWindupHash = Animator.StringToHash("ChargeWindup");
        private static readonly int ChargingHash = Animator.StringToHash("Charging");
        private static readonly int ChargeHitHash = Animator.StringToHash("ChargeHit");

        public EnemyState CurrentState => currentState;
        public bool IsAlive => health != null && health.IsAlive;

        /// <summary>Movement slow from statuses (Hamstring etc.) — 1 = unslowed.</summary>
        private float SpeedFactor => statuses != null ? statuses.MoveSpeedFactor : 1f;

        /// <summary>
        /// Boss rage (BossRageMode): permanently faster movement, much shorter
        /// attack cooldowns, aggressive specials, and it never loses you.
        /// </summary>
        public void ApplyRage(float speedMultiplier, float cooldownMultiplier)
        {
            walkSpeed *= speedMultiplier;
            runSpeed *= speedMultiplier;
            attackCooldown *= cooldownMultiplier;
            jumpAttackCooldown *= cooldownMultiplier;
            chargeAttackCooldown *= cooldownMultiplier;
            chargeChance = Mathf.Min(0.6f, chargeChance * 1.5f);
            detectionRange = Mathf.Max(detectionRange, 35f);
            loseTargetRange = Mathf.Max(loseTargetRange, 70f);
            if (agent != null && agent.isOnNavMesh)
                agent.speed = runSpeed;
        }

        /// <summary>
        /// Taunt hook (Challenging Shout): lock onto the target immediately,
        /// regardless of detection range or line of sight.
        /// </summary>
        public void ForceAggro(Transform target)
        {
            if (!IsAlive || target == null) return;
            playerTarget = target;
            if (statuses == null || !statuses.IsStunned)
                currentState = EnemyState.Chase;
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            statuses = GetComponent<Combat.StatusSystem>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            netAnimator = GetComponent<NetworkAnimator>();

            if (agent != null)
            {
                agent.speed = walkSpeed;
                agent.autoTraverseOffMeshLink = false; // We handle manually for animation
            }
        }

        private void Start()
        {
            // Store spawn position for patrol generation
            spawnPosition = transform.position;

            // Ensure health component is found (retry if Awake failed)
            if (health == null)
                health = GetComponent<Health>();

            if (health != null)
                health.OnDeath.AddListener(OnDeath);
            else
                Debug.LogError($"EnemyAI: No Health component found on {gameObject.name}!");

            // Auto-generate patrol points if none exist
            if (autoGeneratePatrol && (patrolPoints == null || patrolPoints.Length == 0))
            {
                GeneratePatrolPoints();
            }

            // Start patrolling if we have patrol points
            if (patrolPoints != null && patrolPoints.Length > 0)
            {
                currentState = EnemyState.Patrol;
            }

            // Auto-configure playerLayer if not set - Player layer ONLY (not Default)
            if (playerLayer == 0)
            {
                int playerLayerIndex = LayerMask.NameToLayer("Player");
                if (playerLayerIndex >= 0)
                    playerLayer = (1 << playerLayerIndex);  // Player layer only, NOT Default
                else
                {
                    playerLayer = 1;  // Fallback to Default if Player layer doesn't exist
                    Debug.LogWarning("EnemyAI: 'Player' layer not found! Using Default layer as fallback.");
                }
            }

            // Ensure collider is enabled (in case this is a respawned enemy)
            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.enabled = true;

            if (statuses != null)
            {
                statuses.OnApplied += OnStatusApplied;
                statuses.OnRemoved += OnStatusRemoved;
            }

            // Initialize NavMeshAgent on NavMesh after a short delay
            StartCoroutine(InitializeNavMeshAgent());
        }

        private void OnDestroy()
        {
            if (statuses != null)
            {
                statuses.OnApplied -= OnStatusApplied;
                statuses.OnRemoved -= OnStatusRemoved;
            }
        }

        private void OnStatusApplied(Combat.StatusEffect effect)
        {
            if ((effect.Flags & Combat.StatusFlags.Stunned) == 0 || !IsAlive) return;
            stunLocked = true;
            if (isJumpAttacking) return; // let the leap land; the Update gate freezes it after

            // Kill any queued attack callbacks so a hit can't land while frozen
            CancelInvoke(nameof(DealDamage));
            CancelInvoke(nameof(EndAttack));
            isAttacking = false;

            if (isWindingUp || isCharging)
                EndChargeAttack();

            if (agent != null && agent.isOnNavMesh)
                agent.isStopped = true;

            // Stagger react — every character's controller wires its Hit clip to ChargeHit
            if (animator != null)
                PlayTrigger(ChargeHitHash);
        }

        private void OnStatusRemoved(string id)
        {
            if (!stunLocked || statuses == null || statuses.IsStunned || !IsAlive) return;
            stunLocked = false;

            if (agent != null && agent.isOnNavMesh)
                agent.isStopped = false;
            currentState = playerTarget != null ? EnemyState.Chase : EnemyState.Idle;
        }

        private IEnumerator InitializeNavMeshAgent()
        {
            yield return null; // Wait one frame for all components to initialize

            if (agent != null && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Debug.Log($"EnemyAI: Initialized on NavMesh at {hit.position}");
                }
            }

            // Ensure agent is enabled and not stopped
            if (agent != null)
            {
                agent.enabled = true;
                agent.isStopped = false;
            }
        }

        private void GeneratePatrolPoints()
        {
            // Create container for patrol points
            patrolPointContainer = new GameObject("PatrolPoints");
            patrolPointContainer.transform.SetParent(transform.parent);

            // Generate 4 patrol points in a square pattern around spawn position
            float patrolRadius = 8f;
            Vector3[] offsets = new Vector3[]
            {
                new Vector3(patrolRadius, 0, 0),
                new Vector3(0, 0, patrolRadius),
                new Vector3(-patrolRadius, 0, 0),
                new Vector3(0, 0, -patrolRadius)
            };

            var validPoints = new System.Collections.Generic.List<Transform>();

            foreach (var offset in offsets)
            {
                Vector3 targetPos = spawnPosition + offset;

                // Validate point is on NavMesh
                if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    var point = new GameObject($"PatrolPoint_{validPoints.Count}");
                    point.transform.SetParent(patrolPointContainer.transform);
                    point.transform.position = hit.position;
                    validPoints.Add(point.transform);
                }
            }

            // Assign the patrol points
            if (validPoints.Count > 0)
            {
                patrolPoints = validPoints.ToArray();
                Debug.Log($"EnemyAI: Generated {patrolPoints.Length} patrol points around spawn position");
            }
            else
            {
                Debug.LogWarning("EnemyAI: Could not generate any valid patrol points on NavMesh");
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
            // Cutscene playing: the whole battlefield freezes in place
            if (Cinematics.CutsceneDirector.CombatPaused)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh)
                    agent.isStopped = true;
                return;
            }

            // Ensure agent is enabled
            if (agent != null && !agent.enabled)
            {
                agent.enabled = true;
            }

            // Try to place on NavMesh if not already
            if (agent != null && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                    Debug.Log($"EnemyAI: Warped to NavMesh at {hit.position}");
                    // Don't return - continue with state machine after warping
                }
                else
                {
                    // No NavMesh found within 10 units - likely a setup issue
                    Debug.LogWarning($"EnemyAI: Cannot find NavMesh within 10 units of {transform.position}. Run 'AldenFall > Build Complete Scene' to bake NavMesh.");
                    return;
                }
            }

            // Handle OffMeshLink traversal (navigation jumps)
            if (agent != null && agent.isOnOffMeshLink && !isTraversingLink)
            {
                StartCoroutine(TraverseOffMeshLink());
                return;
            }

            // Skip normal state updates during link traversal
            if (isTraversingLink) return;

            // Stunned: frozen in place. A mid-air jump attack is allowed to land
            // first (interrupting it would leave the agent floating off-mesh).
            if (statuses != null && statuses.IsStunned && !isJumpAttacking)
            {
                if (agent != null && agent.isOnNavMesh)
                    agent.isStopped = true;
                return;
            }

            // Check for player (with interval optimization)
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
                case EnemyState.JumpAttack:
                    HandleJumpAttack();
                    break;
                case EnemyState.ChargeAttack:
                    HandleChargeAttack();
                    break;
            }
        }

        private void CheckForPlayer()
        {
            if (playerTarget == null)
            {
                // Only run expensive detection at intervals when no target
                detectionTimer += Time.deltaTime;
                if (detectionTimer < detectionInterval)
                    return;
                detectionTimer = 0f;

                // Look for player
                Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange, playerLayer);
                foreach (var col in colliders)
                {
                    // Skip self - prevents enemy from targeting itself
                    if (col.gameObject == gameObject) continue;

                    var playerHealth = col.GetComponent<Health>();
                    if (playerHealth != null && playerHealth.IsAlive)
                    {
                        // Veiled players are undetectable
                        var candidateStatuses = col.GetComponent<Combat.StatusSystem>();
                        if (candidateStatuses != null && candidateStatuses.IsInvisible) continue;

                        playerTarget = col.transform;
                        currentState = EnemyState.Chase;
                        Debug.Log($"EnemyAI: Detected player at {playerTarget.position}, switching to Chase");
                        break;
                    }
                }
            }
            else
            {
                // Target validation is cheap - keep every frame
                var targetHealth = playerTarget.GetComponent<Health>();
                if (targetHealth == null || targetHealth.IsDead)
                {
                    LoseTarget();
                    return;
                }

                // Veil: the target vanished from our senses — drop aggro
                var targetStatuses = playerTarget.GetComponent<Combat.StatusSystem>();
                if (targetStatuses != null && targetStatuses.IsInvisible)
                {
                    LoseTarget();
                    return;
                }

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
            detectionTimer = 0f; // Reset to check immediately
            currentState = patrolPoints != null && patrolPoints.Length > 0 ? EnemyState.Patrol : EnemyState.Idle;
        }

        private void HandleIdle()
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.speed = walkSpeed;
            }
        }

        private void HandlePatrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0) return;
            if (agent == null || !agent.isOnNavMesh) return;

            agent.speed = walkSpeed * SpeedFactor; // Walk during patrol

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

            // Check for charge attack opportunity (special attack)
            if (chargeAttackEnabled
                && distance >= chargeAttackMinRange && distance <= chargeAttackMaxRange
                && Time.time - lastChargeAttackTime >= chargeAttackCooldown
                && CanSeePlayer()
                && Random.value < chargeChance)
            {
                StartChargeAttack();
                return;
            }

            // Check for jump attack opportunity
            if (jumpAttackEnabled
                && distance >= jumpAttackMinRange && distance <= jumpAttackMaxRange
                && Time.time - lastJumpAttackTime >= jumpAttackCooldown
                && CanSeePlayer())
            {
                StartJumpAttack();
                return;
            }

            // Close enough for basic attack
            if (distance <= attackRange)
            {
                currentState = EnemyState.Attack;
                if (agent != null && agent.isOnNavMesh)
                    agent.isStopped = true;
            }
            else
            {
                // Chase - use run speed (statuses like Hamstring slow it)
                if (agent != null && agent.isOnNavMesh)
                {
                    agent.speed = runSpeed * SpeedFactor;
                    agent.isStopped = false;
                    agent.SetDestination(playerTarget.position);
                }
            }

            FaceTarget(playerTarget.position);
        }

        private bool CanSeePlayer()
        {
            if (playerTarget == null) return false;

            Vector3 eyePos = transform.position + Vector3.up * 1.5f;
            Vector3 targetPos = playerTarget.position + Vector3.up * 1f;
            Vector3 direction = targetPos - eyePos;

            if (Physics.Raycast(eyePos, direction.normalized, out RaycastHit hit, direction.magnitude))
            {
                return hit.transform == playerTarget || hit.transform.IsChildOf(playerTarget);
            }
            return true; // No obstruction
        }

        #region Basic Attack

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
                // The player fled out of melee range: drop the queued hit so a swing that
                // started while they were adjacent can't land after they've run away.
                // DealDamage also re-checks distance, but cancelling here stops the swing
                // outright rather than relying solely on the range gate at impact time.
                CancelInvoke(nameof(DealDamage));
                isAttacking = false;
                currentState = EnemyState.Chase;
                return;
            }

            FaceTarget(playerTarget.position);

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
                PlayTrigger(AttackHash);

            Invoke(nameof(DealDamage), attackDuration * 0.5f);
            Invoke(nameof(EndAttack), attackDuration);
        }

        /// <summary>Melee damage via the shared formula when a StatBlock exists.</summary>
        private float RollDamage(float fallback)
        {
            var stats = GetComponent<Stats.StatBlock>();
            return stats != null ? Stats.CombatMath.MeleeDamage(stats) : fallback;
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
                    targetHealth.TakeDamage(RollDamage(attackDamage), gameObject, canMiss: true);

                    Vector3 hitDir = (playerTarget.position - transform.position).normalized;
                    if (basicKnockback > 0f)
                        targetHealth.ApplyKnockback(hitDir, basicKnockback);

                    // Create melee hit effect at impact point
                    Vector3 impactPos = transform.position + hitDir * attackRange * 0.5f + Vector3.up;
                    HitEffect.CreateMeleeImpact(impactPos);
                }
            }
        }

        private void EndAttack()
        {
            isAttacking = false;
        }

        #endregion

        #region Jump Attack

        private void StartJumpAttack()
        {
            currentState = EnemyState.JumpAttack;
            isJumpAttacking = true;
            jumpAttackHit = false;
            lastJumpAttackTime = Time.time;
            jumpAttackTimer = 0f;

            jumpAttackStartPos = transform.position;
            jumpAttackTargetPos = playerTarget.position;

            // Disable NavMeshAgent during jump
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.updatePosition = false;
            }

            if (animator != null)
                PlayTrigger(JumpAttackHash);
        }

        private void HandleJumpAttack()
        {
            jumpAttackTimer += Time.deltaTime;
            float t = jumpAttackTimer / jumpAttackDuration;

            if (t >= 1f)
            {
                EndJumpAttack();
                return;
            }

            // Parabolic arc movement
            Vector3 currentPos = Vector3.Lerp(jumpAttackStartPos, jumpAttackTargetPos, t);
            currentPos.y += jumpAttackArcHeight * 4f * t * (1f - t);
            transform.position = currentPos;

            FaceTarget(jumpAttackTargetPos);

            // Check for hit during descent (t > 0.5)
            if (t > 0.5f && !jumpAttackHit)
            {
                CheckJumpAttackHit();
            }
        }

        private void CheckJumpAttackHit()
        {
            if (playerTarget == null) return;

            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (distance <= attackRange * 1.5f)
            {
                var targetHealth = playerTarget.GetComponent<Health>();
                if (targetHealth != null && !targetHealth.IsDead)
                {
                    targetHealth.TakeDamage(RollDamage(jumpAttackDamage) * 1.4f, gameObject, canMiss: true);
                    jumpAttackHit = true;

                    if (jumpAttackKnockback > 0f)
                    {
                        // Downward slam knockback (boss-only special)
                        Vector3 knockbackDir = (playerTarget.position - transform.position).normalized;
                        knockbackDir.y = -0.3f;
                        knockbackDir.Normalize();
                        targetHealth.ApplyKnockback(knockbackDir, jumpAttackKnockback);
                    }

                    // Create impact effect
                    HitEffect.CreateMeleeImpact(transform.position + Vector3.up * 0.5f);
                }
            }
        }

        private void EndJumpAttack()
        {
            isJumpAttacking = false;

            // Re-enable NavMeshAgent
            if (agent != null)
            {
                agent.updatePosition = true;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
                agent.isStopped = false;
            }

            currentState = EnemyState.Chase;
        }

        #endregion

        #region Charge Attack

        private void StartChargeAttack()
        {
            currentState = EnemyState.ChargeAttack;
            isWindingUp = true;
            isCharging = false;
            chargeHitSomething = false;
            lastChargeAttackTime = Time.time;
            chargeTimer = 0f;

            // Face target and lock direction
            FaceTarget(playerTarget.position);
            chargeDirection = (playerTarget.position - transform.position).normalized;
            chargeDirection.y = 0;

            // Disable NavMeshAgent
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.updatePosition = false;
            }

            if (animator != null)
                PlayTrigger(ChargeWindupHash);
        }

        private void HandleChargeAttack()
        {
            chargeTimer += Time.deltaTime;

            if (isWindingUp)
            {
                // Windup phase - enemy telegraphs attack
                if (chargeTimer >= chargeWindupTime)
                {
                    isWindingUp = false;
                    isCharging = true;
                    chargeTimer = 0f;

                    if (animator != null)
                        animator.SetBool(ChargingHash, true);
                }
                return;
            }

            if (isCharging)
            {
                // Check for timeout or hit
                if (chargeTimer >= chargeMaxDuration || chargeHitSomething)
                {
                    EndChargeAttack();
                    return;
                }

                // Move forward
                Vector3 movement = chargeDirection * chargeSpeed * Time.deltaTime;

                // Check for obstacles ahead (wall collision)
                if (Physics.Raycast(transform.position + Vector3.up, chargeDirection, out RaycastHit wallHit, 1f))
                {
                    if (!wallHit.collider.CompareTag("Player"))
                    {
                        // Hit a wall - stun
                        chargeHitSomething = true;
                        StartCoroutine(ChargeStun());
                        return;
                    }
                }

                // Move
                transform.position += movement;

                // Check for player hit
                CheckChargeHit();
            }
        }

        private void CheckChargeHit()
        {
            if (playerTarget == null) return;

            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (distance <= attackRange * 1.5f)
            {
                var targetHealth = playerTarget.GetComponent<Health>();
                if (targetHealth != null && !targetHealth.IsDead)
                {
                    targetHealth.TakeDamage(RollDamage(chargeAttackDamage) * 2f, gameObject, canMiss: true);
                    chargeHitSomething = true;

                    if (chargeKnockback > 0f)
                        targetHealth.ApplyKnockback(chargeDirection, chargeKnockback);

                    // Create impact effect
                    HitEffect.CreateMeleeImpact(playerTarget.position + Vector3.up);
                }
            }
        }

        private IEnumerator ChargeStun()
        {
            if (animator != null)
            {
                animator.SetBool(ChargingHash, false);
                PlayTrigger(ChargeHitHash);
            }

            yield return new WaitForSeconds(chargeStunDuration);

            EndChargeAttack();
        }

        private void EndChargeAttack()
        {
            isCharging = false;
            isWindingUp = false;

            if (animator != null)
                animator.SetBool(ChargingHash, false);

            // Re-enable NavMeshAgent
            if (agent != null)
            {
                agent.updatePosition = true;
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
                agent.isStopped = false;
            }

            currentState = EnemyState.Chase;
        }

        #endregion

        #region Navigation Jump (OffMeshLink)

        private IEnumerator TraverseOffMeshLink()
        {
            isTraversingLink = true;

            OffMeshLinkData linkData = agent.currentOffMeshLinkData;
            Vector3 startPos = transform.position;
            Vector3 endPos = linkData.endPos + Vector3.up * agent.baseOffset;

            // Trigger jump animation
            if (animator != null)
            {
                animator.SetBool(IsJumpingHash, true);
                PlayTrigger(JumpHash);
            }

            // Calculate arc height based on horizontal distance
            float distance = Vector3.Distance(startPos, endPos);
            float arcHeight = Mathf.Max(1f, distance * 0.3f);

            float duration = distance / linkTraversalSpeed;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;

                // Parabolic arc
                Vector3 currentPos = Vector3.Lerp(startPos, endPos, t);
                currentPos.y += arcHeight * 4f * t * (1f - t);

                transform.position = currentPos;

                // Face movement direction
                Vector3 lookDir = (endPos - startPos).normalized;
                lookDir.y = 0;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Complete the link traversal
            agent.CompleteOffMeshLink();

            if (animator != null)
            {
                animator.SetBool(IsJumpingHash, false);
            }

            isTraversingLink = false;
        }

        #endregion

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
                speed = agent.velocity.magnitude / runSpeed; // Normalize against run speed
            }

            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
        }

        // Animation triggers don't auto-replicate through NetworkAnimator (only float/int/bool
        // params do). Route them through NetworkAnimator so remote clients see attack/death/jump.
        // EnemyAI runs server-only, so this fires server → all clients (and locally on the host).
        private void PlayTrigger(int hash)
        {
            if (netAnimator != null) netAnimator.SetTrigger(hash);
            else if (animator != null) animator.SetTrigger(hash);
        }

        private void OnDeath()
        {
            currentState = EnemyState.Dead;

            // Cancel any pending invokes
            CancelInvoke();

            // Stop any coroutines
            StopAllCoroutines();

            if (agent != null)
            {
                if (agent.isOnNavMesh)
                    agent.isStopped = true;
                agent.enabled = false;
            }

            if (animator != null)
            {
                animator.SetBool(IsDeadHash, true);
                PlayTrigger(DeathHash);
            }

            // Disable collider after short delay
            Invoke(nameof(DisableCollider), 0.5f);

            // Respawning is owned by whoever spawned us (EnemySpawner/MobCamp
            // subscribe to Health.OnDeath themselves).

            // Networked despawn after the death animation. OnDeath runs server-only
            // (EnemyAI is disabled on clients by EnemyNetworkSync), so this despawns
            // the enemy for everyone.
            Invoke(nameof(NetworkDespawn), 2f);
        }

        private void NetworkDespawn()
        {
            if (NetworkServer.active)
                NetworkServer.Destroy(gameObject);
            else
                Destroy(gameObject);
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

            // Jump attack range
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, jumpAttackMinRange);
            Gizmos.DrawWireSphere(transform.position, jumpAttackMaxRange);

            // Charge attack range
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireSphere(transform.position, chargeAttackMinRange);
            Gizmos.DrawWireSphere(transform.position, chargeAttackMaxRange);

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
