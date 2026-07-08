using UnityEngine;
using AldenFall.UI;

namespace AldenFall.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4.5f;   // grounded, matches walk anim cadence
        [SerializeField] private float runSpeed = 8.5f;
        [SerializeField] private float rotationSpeed = 14f;
        [SerializeField] private float gravity = -25f;
        [SerializeField] private float jumpHeight = 4f;

        [Header("Dodge Roll")]
        [SerializeField] private float dodgeDistance = 5.2f;
        [SerializeField] private float dodgeDuration = 0.32f;
        [SerializeField] private float dodgeCooldown = 1.25f;
        [SerializeField] private float doubleTapWindow = 0.3f;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundDistance = 0.3f;

        [Header("Swimming")]
        [SerializeField] private float swimSpeed = 6f;
        [SerializeField] private float swimUpSpeed = 4f;
        [SerializeField] private float sinkSpeed = 1.2f;
        [Tooltip("How deep (from surface to player feet) before swimming kicks in.")]
        [SerializeField] private float swimEnterDepth = 1.3f;
        [Tooltip("Depth below which swimming ends. Must be less than enter depth (hysteresis).")]
        [SerializeField] private float swimExitDepth = 1.0f;
        [Tooltip("Feet float this far below the surface while treading water.")]
        [SerializeField] private float floatDepth = 1.25f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private CharacterController controller;
        private Stats.StatBlock statBlock;
        private Vector3 velocity;
        private bool isGrounded;
        private Transform cameraTransform;

        // Dodge roll state
        private bool isDodging;
        private float dodgeTimer;
        private float dodgeElapsed;
        private float dodgeCooldownTimer;
        private Vector3 dodgeDirection;
        private Combat.Health health;
        private Combat.StatusSystem statuses;
        private Abilities.PlayerAbilities abilities;

        // Set each frame by auto-attack pursuit (PlayerMelee); manual input wins.
        private Vector3 externalMoveIntent;
        private bool externalMoveRun;

        // Double-tap detection
        private float lastWTime, lastATime, lastSTime, lastDTime;

        // Swimming state
        private bool isInWater;
        private float waterSurfaceY;
        private readonly Collider[] waterOverlaps = new Collider[4];
        private readonly Collider[] groundOverlaps = new Collider[8];

        // Knockback state
        private Vector3 knockbackVelocity;
        private bool isBeingKnockedBack;

        // Animation parameter hashes
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");
        private static readonly int DodgeHash = Animator.StringToHash("Dodge");
        private static readonly int IsSwimmingHash = Animator.StringToHash("IsSwimming");

        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsDodging => isDodging;
        public bool IsInWater => isInWater;
        public bool IsSwimming => isInWater;
        public Vector3 MoveDirection { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            health = GetComponent<Combat.Health>();
            statuses = GetComponent<Combat.StatusSystem>();
            abilities = GetComponent<Abilities.PlayerAbilities>();
            statBlock = GetComponent<Stats.StatBlock>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            cameraTransform = Camera.main?.transform;

            if (groundCheck == null)
            {
                var groundCheckObj = new GameObject("GroundCheck");
                groundCheck = groundCheckObj.transform;
                groundCheck.SetParent(transform);
                groundCheck.localPosition = new Vector3(0, 0.1f, 0);
            }

            if (health != null)
                health.OnKnockback.AddListener(OnKnockback);
        }

        private void OnKnockback(Vector3 direction, float force)
        {
            knockbackVelocity = direction * force;
            isBeingKnockedBack = true;
        }

        private void Update()
        {
            if (dodgeCooldownTimer > 0)
                dodgeCooldownTimer -= Time.deltaTime;

            HandleGroundCheck();
            UpdateWaterState();
            HandleKnockback();

            // Stunned: gravity and knockback still apply, but no player input.
            // Dashing: PlayerAbilities owns the controller for the moment.
            if ((statuses != null && statuses.IsStunned) ||
                (abilities != null && abilities.IsDashing) ||
                InputFocusGuard.IsTyping)
            {
                if (!isInWater)
                    ApplyGravity();
                UpdateAnimator();
                return;
            }

            if (isInWater)
            {
                HandleSwimming();
            }
            else
            {
                HandleDoubleTapDodge();

                if (isDodging)
                {
                    HandleDodge();
                }
                else
                {
                    HandleMovement();
                    HandleJump();
                }

                ApplyGravity();
            }

            UpdateAnimator();
        }

        private void HandleKnockback()
        {
            if (!isBeingKnockedBack) return;

            if (knockbackVelocity.sqrMagnitude > 0.5f)
            {
                controller.Move(knockbackVelocity * Time.deltaTime);
                knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 2.5f * Time.deltaTime);
            }
            else
            {
                isBeingKnockedBack = false;
                knockbackVelocity = Vector3.zero;
            }
        }

        private void HandleGroundCheck()
        {
            // Overlap + self-exclusion rather than CheckSphere: if the player ever
            // shares a layer with the ground mask, a bare CheckSphere hits our own
            // capsule and reports permanently-grounded (breaks gravity and swim).
            int count = Physics.OverlapSphereNonAlloc(groundCheck.position, groundDistance,
                groundOverlaps, GameLayers.GetGroundMask(), QueryTriggerInteraction.Ignore);

            isGrounded = false;
            for (int i = 0; i < count; i++)
            {
                var col = groundOverlaps[i];
                if (col.transform == transform || col.transform.IsChildOf(transform)) continue;
                isGrounded = true;
                break;
            }

            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f; // Small downward force to keep grounded
            }
        }

        // ========== WATER DETECTION ==========
        //
        // Water volumes are trigger BoxColliders on the Water layer. We measure how
        // deep the player's feet are below the highest overlapped water surface and
        // switch swim state with hysteresis (enter deep, exit shallow) so the state
        // can never flicker at the surface. Ankle-deep water stays normal walking.

        private void UpdateWaterState()
        {
            float depth = SampleWaterDepth(out float surfaceY);

            if (!isInWater)
            {
                if (depth >= swimEnterDepth)
                {
                    isInWater = true;
                    waterSurfaceY = surfaceY;

                    if (isDodging) EndDodge();

                    // Kill momentum on entry; splash-damp vertical velocity.
                    velocity.x = 0;
                    velocity.z = 0;
                    velocity.y *= 0.3f;
                }
            }
            else
            {
                if (depth <= swimExitDepth)
                {
                    ExitWater();
                }
                else
                {
                    waterSurfaceY = surfaceY;
                }
            }
        }

        /// <summary>
        /// Returns how far the player's feet are below the highest overlapping
        /// water surface (0 when not touching water), and that surface height.
        /// </summary>
        private float SampleWaterDepth(out float surfaceY)
        {
            surfaceY = 0f;
            LayerMask waterMask = GameLayers.GetWaterMask();
            if (waterMask == 0) return 0f;

            // Overlap the player's full capsule so partial submersion is measurable.
            Vector3 feet = transform.position + Vector3.up * controller.radius;
            Vector3 head = transform.position + Vector3.up * (controller.height - controller.radius);
            int count = Physics.OverlapCapsuleNonAlloc(feet, head, controller.radius,
                waterOverlaps, waterMask, QueryTriggerInteraction.Collide);

            if (count == 0) return 0f;

            surfaceY = float.MinValue;
            for (int i = 0; i < count; i++)
                surfaceY = Mathf.Max(surfaceY, waterOverlaps[i].bounds.max.y);

            return Mathf.Max(0f, surfaceY - transform.position.y);
        }

        private void ExitWater()
        {
            isInWater = false;
            waterSurfaceY = 0f;

            // No residual swim momentum on land — this was the source of the
            // permanent post-swim drift bug.
            velocity.x = 0;
            velocity.z = 0;
            if (velocity.y < 0) velocity.y = -2f;
        }

        // ========== SWIMMING ==========

        private void HandleSwimming()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            Vector3 inputDirection = new Vector3(horizontal, 0, vertical).normalized;
            IsMoving = inputDirection.magnitude > 0.1f;
            IsRunning = false;

            // Camera-relative horizontal swim — direct control, no lingering momentum.
            Vector3 moveDir = Vector3.zero;
            if (IsMoving && cameraTransform != null)
            {
                moveDir = GetCameraRelativeDirection(horizontal, vertical);
                MoveDirection = moveDir;

                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                        rotationSpeed * Time.deltaTime);
                }
            }
            else
            {
                MoveDirection = Vector3.zero;
            }

            // Vertical: buoyancy pulls the player toward the float line
            // (feet floatDepth below the surface). Space swims up / vaults out.
            float floatTargetY = waterSurfaceY - floatDepth;
            float verticalError = floatTargetY - transform.position.y;

            float verticalSpeed;
            if (Input.GetButton("Jump"))
            {
                float depth = waterSurfaceY - transform.position.y;
                if (depth <= floatDepth + 0.35f)
                {
                    // Near the surface: vault out with a real jump. The depth
                    // hysteresis in UpdateWaterState ends swim state naturally.
                    verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity) * 0.75f;
                }
                else
                {
                    verticalSpeed = swimUpSpeed;
                }
            }
            else
            {
                // Proportional buoyancy: settle at the float line without bouncing.
                verticalSpeed = Mathf.Clamp(verticalError * 2.5f, -sinkSpeed, swimUpSpeed * 0.8f);
            }

            Vector3 swimMotion = moveDir * swimSpeed + Vector3.up * verticalSpeed;
            controller.Move(swimMotion * Time.deltaTime);

            // Keep controller velocity in sync so exiting water behaves.
            velocity = new Vector3(0, verticalSpeed, 0);
        }

        // ========== DODGE COOLDOWN GETTERS (for UI) ==========

        public float GetDodgeCooldownPercent()
        {
            return Mathf.Clamp01(dodgeCooldownTimer / dodgeCooldown);
        }

        public float GetDodgeCooldownRemaining()
        {
            return Mathf.Max(0, dodgeCooldownTimer);
        }

        // ========== LAND MOVEMENT ==========

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            IsRunning = Input.GetKey(KeyCode.LeftShift);

            Vector3 inputDirection = new Vector3(horizontal, 0, vertical).normalized;
            bool manualInput = inputDirection.magnitude > 0.1f;

            if (manualInput && cameraTransform != null)
            {
                MoveDirection = GetCameraRelativeDirection(horizontal, vertical);
            }
            else if (externalMoveIntent.sqrMagnitude > 0.01f)
            {
                // Pursuit: melee attack-orders walk, queued abilities RUN in
                MoveDirection = externalMoveIntent.normalized;
                IsRunning = externalMoveRun;
            }
            else
            {
                MoveDirection = Vector3.zero;
            }

            IsMoving = MoveDirection.sqrMagnitude > 0.01f;
            externalMoveIntent = Vector3.zero; // consumed — must be re-set every frame
            externalMoveRun = false;

            if (IsMoving)
            {
                Quaternion targetRotation = Quaternion.LookRotation(MoveDirection);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                    rotationSpeed * Time.deltaTime);

                float speedMult = statBlock != null ? statBlock.Get(Stats.StatType.MoveSpeed) : 1f;
                float currentSpeed = (IsRunning ? runSpeed : walkSpeed) * speedMult;
                controller.Move(MoveDirection * currentSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Request movement toward a world direction this frame (auto-attack
        /// pursuit). Manual WASD always wins; the intent expires every frame.
        /// Pass run=true to sprint (queued ability closing the gap).
        /// </summary>
        public void SetExternalMoveIntent(Vector3 worldDirection, bool run = false)
        {
            worldDirection.y = 0f;
            externalMoveIntent = worldDirection;
            externalMoveRun = run;
        }

        /// <summary>True when the player is steering with WASD this frame.</summary>
        public bool HasManualMoveInput =>
            Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f ||
            Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;

        private void HandleJump()
        {
            if (Input.GetButtonDown("Jump") && isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

                if (animator != null)
                    animator.SetTrigger(JumpHash);
            }
        }

        private void HandleDoubleTapDodge()
        {
            if (isDodging || dodgeCooldownTimer > 0 || !isGrounded) return;

            float currentTime = Time.time;
            Vector3 dodgeDir = Vector3.zero;

            if (Input.GetKeyDown(KeyCode.W))
            {
                if (currentTime - lastWTime < doubleTapWindow)
                    dodgeDir = GetCameraRelativeDirection(0, 1);
                lastWTime = currentTime;
            }
            else if (Input.GetKeyDown(KeyCode.S))
            {
                if (currentTime - lastSTime < doubleTapWindow)
                    dodgeDir = GetCameraRelativeDirection(0, -1);
                lastSTime = currentTime;
            }
            else if (Input.GetKeyDown(KeyCode.A))
            {
                if (currentTime - lastATime < doubleTapWindow)
                    dodgeDir = GetCameraRelativeDirection(-1, 0);
                lastATime = currentTime;
            }
            else if (Input.GetKeyDown(KeyCode.D))
            {
                if (currentTime - lastDTime < doubleTapWindow)
                    dodgeDir = GetCameraRelativeDirection(1, 0);
                lastDTime = currentTime;
            }

            if (dodgeDir.magnitude > 0.1f)
            {
                StartDodge(dodgeDir);
            }
        }

        private Vector3 GetCameraRelativeDirection(float horizontal, float vertical)
        {
            if (cameraTransform == null) return new Vector3(horizontal, 0, vertical).normalized;

            Vector3 cameraForward = cameraTransform.forward;
            cameraForward.y = 0;
            cameraForward.Normalize();

            Vector3 cameraRight = cameraTransform.right;
            cameraRight.y = 0;
            cameraRight.Normalize();

            return (cameraForward * vertical + cameraRight * horizontal).normalized;
        }

        private void StartDodge(Vector3 direction)
        {
            isDodging = true;
            dodgeTimer = dodgeDuration;
            dodgeElapsed = 0f;
            dodgeDirection = direction;
            dodgeCooldownTimer = dodgeCooldown;

            if (health != null)
                health.SetInvulnerable(true);

            if (animator != null)
                animator.SetTrigger(DodgeHash);

            if (direction.magnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }

            Camera.main?.GetComponent<ThirdPersonCamera>()?.BeginDodgeAssist(direction);
        }

        private void HandleDodge()
        {
            dodgeTimer -= Time.deltaTime;
            dodgeElapsed += Time.deltaTime;

            float duration = Mathf.Max(0.01f, dodgeDuration);
            float previous = Mathf.Clamp01((dodgeElapsed - Time.deltaTime) / duration);
            float current = Mathf.Clamp01(dodgeElapsed / duration);
            float previousEase = Mathf.SmoothStep(0f, 1f, previous);
            float currentEase = Mathf.SmoothStep(0f, 1f, current);
            float frameDistance = (currentEase - previousEase) * dodgeDistance;
            controller.Move(dodgeDirection * frameDistance);

            if (dodgeTimer <= 0)
            {
                EndDodge();
            }
        }

        private void EndDodge()
        {
            isDodging = false;

            if (health != null)
                health.SetInvulnerable(false);
        }

        private void ApplyGravity()
        {
            velocity.y += gravity * Time.deltaTime;
            // Only the vertical component belongs to gravity. Horizontal motion is
            // applied directly by movement/dodge/knockback — never persisted here.
            controller.Move(new Vector3(0, velocity.y, 0) * Time.deltaTime);
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float speed = IsMoving ? (IsRunning ? 1f : 0.5f) : 0f;
            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            animator.SetBool(IsGroundedHash, isGrounded && !isInWater);
            animator.SetBool(IsRunningHash, IsRunning);
            animator.SetBool(IsSwimmingHash, isInWater);
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
            }

            if (isInWater)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireCube(
                    new Vector3(transform.position.x, waterSurfaceY, transform.position.z),
                    new Vector3(2f, 0.05f, 2f));
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(
                    new Vector3(transform.position.x, waterSurfaceY - floatDepth, transform.position.z),
                    new Vector3(1.5f, 0.05f, 1.5f));
            }
        }
    }
}
