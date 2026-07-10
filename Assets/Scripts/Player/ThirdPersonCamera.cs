using UnityEngine;

namespace AldenFall.Player
{
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.5f, 0);

        [Header("Distance")]
        [SerializeField] private float defaultDistance = 8f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 15f;
        [SerializeField] private float scrollSensitivity = 2f;
        [SerializeField] private float distanceSmoothTime = 0.1f;

        [Header("Rotation")]
        [SerializeField] private float rotationSensitivity = 3f;
        [SerializeField] private float minVerticalAngle = -20f;
        [SerializeField] private float maxVerticalAngle = 60f;
        [SerializeField] private float rotationSmoothTime = 0.05f;
        [SerializeField] private float followSmoothTime = 0.045f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionLayers;
        [SerializeField] private float collisionBuffer = 0.3f;

        private float currentDistance;
        private float targetDistance;
        private float distanceVelocity;

        private float yaw;
        private float pitch;
        private float yawVelocity;
        private float pitchVelocity;
        private float targetYaw;
        private float targetPitch;
        private Vector3 followVelocity;

        // RMB is shared with attack-move taps: only lock the cursor and orbit
        // once the press has actually dragged, so a tap never flickers the cursor.
        private Vector2 rmbDownPos;
        private bool orbiting;
        private const float OrbitDragThresholdPixels = 8f;

        private void Start()
        {
            if (target == null)
            {
                var player = FindFirstObjectByType<PlayerController>();
                if (player != null)
                    target = player.transform;
            }

            currentDistance = defaultDistance;
            targetDistance = defaultDistance;

            // Initialize rotation based on current camera orientation
            Vector3 angles = transform.eulerAngles;
            yaw = targetYaw = angles.y;
            pitch = targetPitch = angles.x;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (target == null) return;

            HandleInput();
            UpdateCameraPosition();
        }

        private void HandleInput()
        {
            // Right-click-drag to rotate camera (BG3 style). A quick tap is an
            // attack-move order (TargetingSystem) and must not grab the cursor.
            if (Input.GetMouseButtonDown(1))
            {
                rmbDownPos = Input.mousePosition;
                orbiting = false;
            }

            if (Input.GetMouseButton(1))
            {
                if (!orbiting &&
                    ((Vector2)Input.mousePosition - rmbDownPos).magnitude > OrbitDragThresholdPixels)
                {
                    orbiting = true;
                }

                if (orbiting)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;

                    float mouseX = Input.GetAxis("Mouse X") * rotationSensitivity;
                    float mouseY = Input.GetAxis("Mouse Y") * rotationSensitivity;

                    targetYaw += mouseX;
                    targetPitch -= mouseY;
                    targetPitch = Mathf.Clamp(targetPitch, minVerticalAngle, maxVerticalAngle);
                }
            }
            else
            {
                orbiting = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            // Scroll to zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                targetDistance -= scroll * scrollSensitivity;
                targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            }
        }

        private void UpdateCameraPosition()
        {
            // Smooth rotation
            yaw = Mathf.SmoothDamp(yaw, targetYaw, ref yawVelocity, rotationSmoothTime);
            pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime);

            // Smooth distance
            currentDistance = Mathf.SmoothDamp(currentDistance, targetDistance, ref distanceVelocity, distanceSmoothTime);

            // Calculate rotation
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);

            // Calculate target position with offset
            Vector3 targetPosition = target.position + targetOffset;

            // Calculate desired camera position
            Vector3 desiredPosition = targetPosition - (rotation * Vector3.forward * currentDistance);

            // Check for collisions
            float actualDistance = CheckCollision(targetPosition, desiredPosition);

            // Apply final position
            Vector3 finalPosition = targetPosition - (rotation * Vector3.forward * actualDistance);
            transform.position = followSmoothTime > 0f
                ? Vector3.SmoothDamp(transform.position, finalPosition, ref followVelocity, followSmoothTime)
                : finalPosition;
            transform.rotation = rotation;
        }

        private float CheckCollision(Vector3 targetPos, Vector3 desiredPos)
        {
            Vector3 direction = desiredPos - targetPos;
            float distance = direction.magnitude;

            if (Physics.SphereCast(targetPos, collisionBuffer, direction.normalized, out RaycastHit hit, distance, collisionLayers))
            {
                return Mathf.Max(minDistance * 0.35f, hit.distance - collisionBuffer);
            }

            return currentDistance;
        }

        public void BeginDodgeAssist(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude < 0.01f) return;

            float dodgeYaw = Quaternion.LookRotation(worldDirection.normalized).eulerAngles.y;
            targetYaw = Mathf.LerpAngle(targetYaw, dodgeYaw, 0.45f);
            targetPitch = Mathf.Clamp(24f, minVerticalAngle, maxVerticalAngle);
            targetDistance = Mathf.Clamp(defaultDistance * 0.82f, minDistance, maxDistance);
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        public void SnapToAngle(float newYaw, float newPitch)
        {
            yaw = targetYaw = newYaw;
            pitch = targetPitch = Mathf.Clamp(newPitch, minVerticalAngle, maxVerticalAngle);
        }

        private void OnDrawGizmosSelected()
        {
            if (target == null) return;

            Gizmos.color = Color.yellow;
            Vector3 targetPos = target.position + targetOffset;
            Gizmos.DrawWireSphere(targetPos, 0.2f);
            Gizmos.DrawLine(targetPos, transform.position);
        }
    }
}
