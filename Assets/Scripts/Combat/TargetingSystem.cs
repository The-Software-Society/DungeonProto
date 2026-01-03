using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using System.Linq;

namespace DungeonProto.Combat
{
    public class TargetingSystem : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float maxTargetDistance = 30f;
        [SerializeField] private LayerMask targetableLayers;
        [SerializeField] private string enemyTag = "Enemy";

        [Header("Tab Targeting")]
        [SerializeField] private KeyCode tabTargetKey = KeyCode.Tab;
        [SerializeField] private float tabTargetAngle = 90f; // Cone in front of player

        [Header("Visual")]
        [SerializeField] private GameObject targetIndicatorPrefab;

        [Header("Events")]
        public UnityEvent<Transform> OnTargetChanged;
        public UnityEvent OnTargetLost;

        private Transform currentTarget;
        private List<Transform> nearbyTargets = new List<Transform>();
        private int currentTargetIndex = -1;
        private GameObject targetIndicatorInstance;
        private Camera mainCamera;

        public Transform CurrentTarget => currentTarget;
        public bool HasTarget => currentTarget != null;

        private void Start()
        {
            mainCamera = Camera.main;

            if (targetIndicatorPrefab != null)
            {
                targetIndicatorInstance = Instantiate(targetIndicatorPrefab);
                targetIndicatorInstance.SetActive(false);
            }
        }

        private void Update()
        {
            HandleClickTargeting();
            HandleTabTargeting();
            ValidateCurrentTarget();
            UpdateTargetIndicator();
        }

        private void HandleClickTargeting()
        {
            if (Input.GetMouseButtonDown(0) && !Input.GetKey(KeyCode.LeftShift))
            {
                Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, maxTargetDistance, targetableLayers))
                {
                    // Check if we hit something with the enemy tag or a Health component
                    Transform potentialTarget = hit.transform;

                    if (potentialTarget.CompareTag(enemyTag) || potentialTarget.GetComponent<Health>() != null)
                    {
                        SetTarget(potentialTarget);
                    }
                }
                else
                {
                    // Clicked on nothing - clear target
                    ClearTarget();
                }
            }
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

                // Cycle to next target
                currentTargetIndex = (currentTargetIndex + 1) % nearbyTargets.Count;
                SetTarget(nearbyTargets[currentTargetIndex]);
            }
        }

        private void RefreshNearbyTargets()
        {
            nearbyTargets.Clear();

            Collider[] colliders = Physics.OverlapSphere(transform.position, maxTargetDistance, targetableLayers);

            foreach (var collider in colliders)
            {
                if (collider.transform == transform) continue;

                // Check tag or Health component
                if (collider.CompareTag(enemyTag) || collider.GetComponent<Health>() != null)
                {
                    // Optional: Filter by angle (targets in front of player)
                    Vector3 directionToTarget = (collider.transform.position - transform.position).normalized;
                    float angle = Vector3.Angle(transform.forward, directionToTarget);

                    if (angle <= tabTargetAngle)
                    {
                        nearbyTargets.Add(collider.transform);
                    }
                }
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
                targetIndicatorInstance.transform.position = currentTarget.position + Vector3.up * 0.1f;
                targetIndicatorInstance.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
            }
            else
            {
                targetIndicatorInstance.SetActive(false);
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
