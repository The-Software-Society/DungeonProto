using Mirror;
using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// The main-quest climax tripwire: a world-space AABB at the volcano mouth.
    /// The first time any living PLAYER crosses it, the server flips the world
    /// into the eruption (<see cref="EmberfallWorldState.BeginEruption"/>) -
    /// embers spread toward the city until the Floor Boss falls.
    ///
    /// Polls like <see cref="SealableDoor"/>/<see cref="LavaHazard"/> instead of
    /// relying on physics trigger callbacks, so it works no matter how avatars
    /// are moved (CharacterController locally, synced transforms on the server).
    /// Server-only: the phase itself replicates via the world-state SyncVar.
    /// </summary>
    public class EruptionTrigger : MonoBehaviour
    {
        [Header("Trigger volume (world-space AABB)")]
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(14f, 12f, 26f);

        [Header("Tuning")]
        [SerializeField] private float checkInterval = 0.5f;

        private bool fired;
        private float nextCheck;

        /// <summary>Called by the builder to place the tripwire (volcano entry tunnel).</summary>
        public void Configure(Vector3 worldCenter, Vector3 worldSize)
        {
            center = worldCenter;
            size = worldSize;
        }

        private void Update()
        {
            if (fired) return;
            if (!NetworkServer.active) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            var world = EmberfallWorldState.Instance;
            if (world == null) return;
            if (world.CurrentPhase != EmberfallWorldState.Phase.Calm)
            {
                fired = true; // already erupting/cleared, nothing left to trip
                return;
            }

            var bounds = new Bounds(center, size);
            foreach (var conn in NetworkServer.connections.Values)
            {
                var player = conn?.identity;
                if (player == null) continue;
                if (!bounds.Contains(player.transform.position)) continue;

                var health = player.GetComponent<Health>();
                if (health != null && !health.IsAlive) continue;

                fired = true;
                world.BeginEruption();
                return;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.35f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
