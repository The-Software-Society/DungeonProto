using Mirror;
using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// A per-player eruption tripwire: a world-space AABB at the volcano mouth. Any living PLAYER
    /// inside it has their OWN world flipped into the eruption (their <see cref="PlayerWorldPhase"/>),
    /// as a safety net alongside the primary quest-driven trigger (accepting q_floor_boss). No global
    /// flip — a player in town keeps their calm world.
    ///
    /// Polls like <see cref="LavaHazard"/> instead of relying on physics trigger callbacks, so it
    /// works no matter how avatars are moved. Server-only.
    /// </summary>
    public class EruptionTrigger : MonoBehaviour
    {
        [Header("Trigger volume (world-space AABB)")]
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 size = new Vector3(14f, 12f, 26f);

        [Header("Tuning")]
        [SerializeField] private float checkInterval = 0.5f;

        private float nextCheck;

        /// <summary>Called by the builder to place the tripwire (volcano entry tunnel).</summary>
        public void Configure(Vector3 worldCenter, Vector3 worldSize)
        {
            center = worldCenter;
            size = worldSize;
        }

        private void Update()
        {
            if (!NetworkServer.active) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            // PER-PLAYER: entering the caldera erupts THIS player's own world (a safety net; the
            // primary trigger is accepting the q_floor_boss quest). No global flip — a player in
            // town keeps their calm world.
            var bounds = new Bounds(center, size);
            foreach (var conn in NetworkServer.connections.Values)
            {
                var player = conn?.identity;
                if (player == null) continue;
                if (!bounds.Contains(player.transform.position)) continue;

                var health = player.GetComponent<Health>();
                if (health != null && !health.IsAlive) continue;

                var pwp = player.GetComponent<PlayerWorldPhase>();
                if (pwp != null && pwp.Phase == EmberfallWorldState.Phase.Calm)
                    pwp.ServerSetPhase(EmberfallWorldState.Phase.Erupting);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.35f);
            Gizmos.DrawWireCube(center, size);
        }
    }
}
