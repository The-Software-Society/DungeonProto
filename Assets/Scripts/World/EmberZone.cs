using UnityEngine;
using AldenFall.Multiplayer;

namespace AldenFall.World
{
    /// <summary>
    /// A positional ember-intensity volume. World-gen places a chain of these along the
    /// Ember Approach with rising intensities (e.g. 0.2 → 0.45 → 0.7 → 1.0) so the walk
    /// toward the volcano visibly darkens. While the LOCAL player is inside, the zone
    /// registers itself with <see cref="EmberAtmosphere"/>, which blends the strongest
    /// active zone with the global (eruption) intensity, so overlapping zones and the
    /// world-state never fight; the loudest one simply wins.
    ///
    /// Purely cosmetic and client-local: the trigger only reacts to THIS machine's local
    /// player, so each client darkens its own view based on where its own camera is. Put
    /// this on a GO with a trigger collider (the collider is forced <c>isTrigger</c> on
    /// Awake so a hand-placed box can't accidentally block movement).
    ///
    /// World-gen usage:
    ///   var go = new GameObject("EmberZone");
    ///   var box = go.AddComponent&lt;BoxCollider&gt;(); box.size = ...; // trigger
    ///   go.AddComponent&lt;EmberZone&gt;().Configure(0.7f);
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class EmberZone : MonoBehaviour
    {
        [Tooltip("Ember intensity this zone drives while the local player is inside (0..1).")]
        [SerializeField, Range(0f, 1f)] private float zoneIntensity = 0.5f;

        private bool localPlayerInside;

        /// <summary>The intensity this zone contributes while occupied. Read by EmberAtmosphere.</summary>
        public float ZoneIntensity => zoneIntensity;

        /// <summary>World-gen entry point. Mirrors the Configure convention of the other
        /// World components.</summary>
        public void Configure(float zoneIntensity)
        {
            this.zoneIntensity = Mathf.Clamp01(zoneIntensity);
        }

        private void Awake()
        {
            // Guarantee the collider is a trigger, this volume must never block movement.
            foreach (var col in GetComponents<Collider>())
                col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (localPlayerInside) return;
            if (!IsLocalPlayer(other)) return;

            localPlayerInside = true;
            if (EmberAtmosphere.Instance != null)
                EmberAtmosphere.Instance.RegisterZone(this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (!localPlayerInside) return;
            if (!IsLocalPlayer(other)) return;

            localPlayerInside = false;
            if (EmberAtmosphere.Instance != null)
                EmberAtmosphere.Instance.UnregisterZone(this);
        }

        private void OnDisable()
        {
            // Never leave the atmosphere stuck bright if the zone is torn down mid-occupancy.
            if (localPlayerInside && EmberAtmosphere.Instance != null)
                EmberAtmosphere.Instance.UnregisterZone(this);
            localPlayerInside = false;
        }

        /// <summary>
        /// Is this collider the LOCAL player? Resolves the local player the same way the
        /// rest of the project does (<see cref="LocalPlayerLocator"/>, Mirror's local
        /// player, then the "Player" tag) and matches the entering collider against it,
        /// walking up from a child collider / rigidbody to the player root.
        /// </summary>
        private static bool IsLocalPlayer(Collider other)
        {
            var local = LocalPlayerLocator.Find();
            if (local == null) return false;

            Transform t = other.attachedRigidbody != null
                ? other.attachedRigidbody.transform
                : other.transform;

            return t == local.transform
                || t.IsChildOf(local.transform)
                || local.transform.IsChildOf(t);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.15f, 0.25f);
            foreach (var col in GetComponents<Collider>())
            {
                if (col is BoxCollider box)
                {
                    Gizmos.matrix = transform.localToWorldMatrix;
                    Gizmos.DrawWireCube(box.center, box.size);
                }
            }
        }
    }
}
