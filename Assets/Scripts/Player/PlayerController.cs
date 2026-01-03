using UnityEngine;

namespace DungeonProto.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float runSpeed = 8f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float jumpHeight = 1.5f;

        [Header("Ground Check")]
        [SerializeField] private Transform groundCheck;
        [SerializeField] private float groundDistance = 0.3f;
        [SerializeField] private LayerMask groundMask;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private CharacterController controller;
        private Vector3 velocity;
        private bool isGrounded;
        private Transform cameraTransform;

        // Animation parameter hashes
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsGroundedHash = Animator.StringToHash("IsGrounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int IsRunningHash = Animator.StringToHash("IsRunning");

        public bool IsMoving { get; private set; }
        public bool IsRunning { get; private set; }
        public Vector3 MoveDirection { get; private set; }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            cameraTransform = Camera.main?.transform;

            if (groundCheck == null)
            {
                // Create ground check point at feet
                var groundCheckObj = new GameObject("GroundCheck");
                groundCheck = groundCheckObj.transform;
                groundCheck.SetParent(transform);
                groundCheck.localPosition = new Vector3(0, 0.1f, 0);
            }
        }

        private void Update()
        {
            HandleGroundCheck();
            HandleMovement();
            HandleJump();
            ApplyGravity();
            UpdateAnimator();
        }

        private void HandleGroundCheck()
        {
            isGrounded = Physics.CheckSphere(groundCheck.position, groundDistance, groundMask);

            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f; // Small downward force to keep grounded
            }
        }

        private void HandleMovement()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");
            IsRunning = Input.GetKey(KeyCode.LeftShift);

            Vector3 inputDirection = new Vector3(horizontal, 0, vertical).normalized;
            IsMoving = inputDirection.magnitude > 0.1f;

            if (IsMoving && cameraTransform != null)
            {
                // Get camera forward and right vectors (flattened to horizontal plane)
                Vector3 cameraForward = cameraTransform.forward;
                cameraForward.y = 0;
                cameraForward.Normalize();

                Vector3 cameraRight = cameraTransform.right;
                cameraRight.y = 0;
                cameraRight.Normalize();

                // Calculate move direction relative to camera
                MoveDirection = (cameraForward * vertical + cameraRight * horizontal).normalized;

                // Rotate character to face movement direction
                if (MoveDirection.magnitude > 0.1f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(MoveDirection);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                }

                // Apply movement
                float currentSpeed = IsRunning ? runSpeed : walkSpeed;
                controller.Move(MoveDirection * currentSpeed * Time.deltaTime);
            }
            else
            {
                MoveDirection = Vector3.zero;
            }
        }

        private void HandleJump()
        {
            if (Input.GetButtonDown("Jump") && isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);

                if (animator != null)
                    animator.SetTrigger(JumpHash);
            }
        }

        private void ApplyGravity()
        {
            velocity.y += gravity * Time.deltaTime;
            controller.Move(velocity * Time.deltaTime);
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float speed = IsMoving ? (IsRunning ? 1f : 0.5f) : 0f;
            animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
            animator.SetBool(IsGroundedHash, isGrounded);
            animator.SetBool(IsRunningHash, IsRunning);
        }

        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = isGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundDistance);
            }
        }
    }
}
