using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Linq;

namespace AldenFall.Combat
{
    public class TargetingSystem : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float maxTargetDistance = 500f;
        [SerializeField] private LayerMask targetableLayers;
        [SerializeField] private string enemyTag = "Enemy";

        [Header("Tab Targeting")]
        [SerializeField] private KeyCode tabTargetKey = KeyCode.Tab;
        [Tooltip("TAB only considers enemies within this range — without a cap it grabs the nearest monster anywhere on the map, through walls.")]
        [SerializeField] private float tabAcquireRange = 30f;

        [Header("Click Forgiveness")]
        [Tooltip("If a click misses every enemy collider, the nearest enemy within this many screen pixels of the cursor is selected instead.")]
        [SerializeField] private float clickBufferPixels = 28f;

        [Header("Visual")]
        [SerializeField] private GameObject targetIndicatorPrefab;

        [Header("Attack-Move (right-click tap)")]
        [SerializeField] private float tapMaxSeconds = 0.3f;
        [SerializeField] private float tapMaxDragPixels = 8f;

        [Header("Events")]
        public UnityEvent<Transform> OnTargetChanged;
        public UnityEvent OnTargetLost;

        /// <summary>Right-click tap on an enemy: LoL-style attack order (PlayerMelee pursues).</summary>
        public UnityEvent<Transform> OnEngage;

        private Transform currentTarget;
        private List<Transform> nearbyTargets = new List<Transform>();
        private int currentTargetIndex = -1;
        private GameObject targetIndicatorInstance;
        private LineRenderer targetRing;
        private Camera mainCamera;
        private float rmbDownTime;
        private Vector2 rmbDownPos;

        public Transform CurrentTarget => currentTarget;
        public bool HasTarget => currentTarget != null;

        private void Start()
        {
            mainCamera = Camera.main;

            // Auto-configure targetableLayers if not set (safety fallback)
            if (targetableLayers == 0)
            {
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                if (enemyLayer >= 0)
                    targetableLayers = (1 << enemyLayer) | 1;  // Enemy + Default
                else
                    targetableLayers = 1;  // Default layer only
                Debug.Log($"TargetingSystem: Auto-configured targetableLayers = {targetableLayers}");
            }

            if (targetIndicatorPrefab != null)
            {
                targetIndicatorInstance = Instantiate(targetIndicatorPrefab);
                targetIndicatorInstance.SetActive(false);
            }
            else
            {
                targetIndicatorInstance = new GameObject("TargetRing");
                targetRing = targetIndicatorInstance.AddComponent<LineRenderer>();
                targetRing.loop = true;
                targetRing.useWorldSpace = false;
                targetRing.positionCount = 64;
                targetRing.startWidth = 0.08f;
                targetRing.endWidth = 0.08f;
                targetRing.material = new Material(Shader.Find("Sprites/Default"));
                targetRing.startColor = new Color(1f, 0.72f, 0.24f, 0.95f);
                targetRing.endColor = new Color(1f, 0.72f, 0.24f, 0.95f);
                targetIndicatorInstance.SetActive(false);
            }
        }

        private void Update()
        {
            HandleClickTargeting();
            HandleAttackMoveTap();
            HandleTabTargeting();
            ValidateCurrentTarget();
            UpdateTargetIndicator();
        }

        private void HandleClickTargeting()
        {
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftShift))
            {
                var enemy = RaycastEnemyUnderCursor();
                if (enemy != null)
                {
                    SetTarget(enemy);
                    return;
                }

                // Clicked on nothing targetable - clear target
                ClearTarget();
            }
        }

        /// <summary>
        /// RMB tap (short press, no drag) on an enemy = attack order: lock the
        /// target and raise OnEngage so PlayerMelee walks in and autos with no
        /// further input. RMB *hold* stays camera orbit (ThirdPersonCamera).
        /// </summary>
        private void HandleAttackMoveTap()
        {
            if (Input.GetMouseButtonDown(1))
            {
                rmbDownTime = Time.unscaledTime;
                rmbDownPos = Input.mousePosition;
            }

            if (Input.GetMouseButtonUp(1))
            {
                bool quick = Time.unscaledTime - rmbDownTime <= tapMaxSeconds;
                bool still = ((Vector2)Input.mousePosition - rmbDownPos).magnitude <= tapMaxDragPixels;
                if (!quick || !still) return;

                var enemy = RaycastEnemyUnderCursor();
                if (enemy != null)
                {
                    SetTarget(enemy);
                    OnEngage?.Invoke(enemy);
                }
            }
        }

        private Transform RaycastEnemyUnderCursor()
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null) return null;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

            // Raycast everything solid; resolve to the entity root via Health.
            // GetComponentInParent means child colliders (real character models)
            // resolve to the correct entity.
            if (Physics.Raycast(ray, out RaycastHit hit, maxTargetDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                var targetHealth = hit.collider.GetComponentInParent<Health>();
                if (targetHealth != null
                    && targetHealth.transform != transform
                    && !targetHealth.IsDead
                    && targetHealth.CompareTag(enemyTag))
                {
                    return targetHealth.transform;
                }
            }

            // Near-miss forgiveness: moving enemies are hard to hit with a thin
            // ray, so fall back to the closest visible enemy within a small
            // screen-space radius of the cursor.
            return FindEnemyNearCursor();
        }

        private Transform FindEnemyNearCursor()
        {
            Vector2 mouse = Input.mousePosition;
            Transform best = null;
            float bestPixels = clickBufferPixels;

            Collider[] colliders = Physics.OverlapSphere(transform.position, maxTargetDistance,
                targetableLayers, QueryTriggerInteraction.Ignore);

            foreach (var collider in colliders)
            {
                var targetHealth = collider.GetComponentInParent<Health>();
                if (targetHealth == null || targetHealth.transform == transform) continue;
                if (targetHealth.IsDead || !targetHealth.CompareTag(enemyTag)) continue;

                Vector3 center = collider.bounds.center;
                Vector3 screen = mainCamera.WorldToScreenPoint(center);
                if (screen.z <= 0f) continue; // Behind the camera

                float pixels = Vector2.Distance(mouse, new Vector2(screen.x, screen.y));
                if (pixels >= bestPixels) continue;

                // Don't grab enemies hidden behind walls.
                Vector3 camPos = mainCamera.transform.position;
                if (Physics.Linecast(camPos, center, out RaycastHit block, ~0, QueryTriggerInteraction.Ignore)
                    && block.collider.GetComponentInParent<Health>() != targetHealth)
                {
                    continue;
                }

                bestPixels = pixels;
                best = targetHealth.transform;
            }

            return best;
        }

        private void HandleTabTargeting()
        {
            if (Input.GetKeyDown(tabTargetKey))
            {
                RefreshNearbyTargets();

                if (nearbyTargets.Count == 0)
                {
                    ClearTarget();
                    return;
                }

                // Always snap to the closest enemy first. Only once we already
                // hold a target does TAB cycle outward in distance order, so
                // the first press is always the nearest monster.
                if (currentTarget != null && currentTargetIndex >= 0)
                {
                    currentTargetIndex = (currentTargetIndex + 1) % nearbyTargets.Count;
                }
                else
                {
                    currentTargetIndex = 0;
                }
                SetTarget(nearbyTargets[currentTargetIndex]);
            }
        }

        private void RefreshNearbyTargets()
        {
            nearbyTargets.Clear();

            Collider[] colliders = Physics.OverlapSphere(transform.position, tabAcquireRange,
                targetableLayers, QueryTriggerInteraction.Ignore);

            foreach (var collider in colliders)
            {
                // Resolve to entity root; child colliders map to their parent Health.
                var targetHealth = collider.GetComponentInParent<Health>();
                if (targetHealth == null || targetHealth.transform == transform) continue;
                if (targetHealth.IsDead || !targetHealth.CompareTag(enemyTag)) continue;
                if (nearbyTargets.Contains(targetHealth.transform)) continue;

                // No facing cone: TAB should find the closest monster no matter
                // which way the player is looking — that's what makes it reliable.
                nearbyTargets.Add(targetHealth.transform);
            }

            // Sort by distance
            nearbyTargets = nearbyTargets
                .OrderBy(t => Vector3.Distance(transform.position, t.position))
                .ToList();

            // Reset index if current target is no longer in list
            if (currentTarget != null && nearbyTargets.Contains(currentTarget))
            {
                currentTargetIndex = nearbyTargets.IndexOf(currentTarget);
            }
            else
            {
                currentTargetIndex = -1;
            }
        }

        private void ValidateCurrentTarget()
        {
            if (currentTarget == null) return;

            // Check if target is dead
            var health = currentTarget.GetComponent<Health>();
            if (health != null && health.IsDead)
            {
                ClearTarget();
                return;
            }

            // Check if target is too far
            float distance = Vector3.Distance(transform.position, currentTarget.position);
            if (distance > maxTargetDistance * 1.5f) // Give some buffer
            {
                ClearTarget();
            }
        }

        private void UpdateTargetIndicator()
        {
            if (targetIndicatorInstance == null) return;

            if (currentTarget != null)
            {
                targetIndicatorInstance.SetActive(true);
                float radius = TargetRadius(currentTarget);
                targetIndicatorInstance.transform.position = currentTarget.position + Vector3.up * 0.08f;
                targetIndicatorInstance.transform.rotation = Quaternion.identity;
                targetIndicatorInstance.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
                if (targetRing != null)
                    SetRingRadius(radius);
            }
            else
            {
                targetIndicatorInstance.SetActive(false);
            }
        }

        private static float TargetRadius(Transform target)
        {
            var collider = target.GetComponentInChildren<Collider>();
            if (collider == null) return 1.1f;
            var extents = collider.bounds.extents;
            return Mathf.Clamp(Mathf.Max(extents.x, extents.z) + 0.25f, 0.8f, 2.5f);
        }

        private void SetRingRadius(float radius)
        {
            if (targetRing == null) return;
            for (int i = 0; i < targetRing.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / targetRing.positionCount;
                targetRing.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
        }

        public void SetTarget(Transform target)
        {
            if (target == currentTarget) return;

            currentTarget = target;
            OnTargetChanged?.Invoke(currentTarget);

            // Update index in nearby targets list
            if (nearbyTargets.Contains(target))
            {
                currentTargetIndex = nearbyTargets.IndexOf(target);
            }
        }

        public void ClearTarget()
        {
            if (currentTarget == null) return;

            currentTarget = null;
            currentTargetIndex = -1;
            OnTargetLost?.Invoke();
            OnTargetChanged?.Invoke(null);
        }

        public Transform GetNearestTarget()
        {
            RefreshNearbyTargets();
            return nearbyTargets.Count > 0 ? nearbyTargets[0] : null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, maxTargetDistance);

            if (currentTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position + Vector3.up, currentTarget.position + Vector3.up);
            }
        }
    }
}
