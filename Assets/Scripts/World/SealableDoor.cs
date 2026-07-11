using UnityEngine;
using AldenFall.AI;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// A solid dungeon door that starts CLOSED (a Ground-layer collider that
    /// blocks the passage — and, being present at NavMesh bake time, the navmesh
    /// too) and OPENS once every enemy inside its guarded room is dead. While
    /// sealed the opaque slab also blocks line-of-sight into the next room, so a
    /// ranged player can't snipe / mage-cheese the room from the doorway.
    ///
    /// Deliberately NON-networked (a plain MonoBehaviour) to stay simple and
    /// compile-safe. Enemies are server-spawned but REPLICATED to every client,
    /// so each machine independently counts the room's live mobs and opens the
    /// door when it observes zero. Enemy death is server-authoritative, so all
    /// machines converge on "cleared" within a poll of each other — no SyncVar
    /// or scene NetworkIdentity required. Limitation: the open moment is decided
    /// per-client from replicated state rather than pushed by the server, so a
    /// client that hasn't yet received a mob's death can lag the door by up to
    /// one poll interval. Acceptable for the demo.
    /// </summary>
    public class SealableDoor : MonoBehaviour
    {
        [Header("Guarded room (world-space AABB)")]
        [SerializeField] private Vector3 guardCenter;
        [SerializeField] private Vector3 guardSize = new Vector3(20f, 12f, 20f);

        [Header("Tuning")]
        [SerializeField] private float checkInterval = 0.5f;

        private bool opened;
        private bool hasSeenEnemies;   // don't open before the room has ever been populated
        private float nextCheck;

        /// <summary>Called by the builder to point the door at the room it gates.</summary>
        public void Configure(Vector3 center, Vector3 size)
        {
            guardCenter = center;
            guardSize = size;
        }

        private void Update()
        {
            if (opened) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            int alive = CountLiveEnemiesInRoom();
            if (alive > 0) { hasSeenEnemies = true; return; }

            // Zero live enemies. Only unseal once the room was actually populated,
            // otherwise the build-time / pre-spawn frame would pop the door open.
            if (hasSeenEnemies) Open();
        }

        private int CountLiveEnemiesInRoom()
        {
            var bounds = new Bounds(guardCenter, guardSize);
            int count = 0;
            var mobs = Object.FindObjectsByType<MobIdentity>(FindObjectsSortMode.None);
            foreach (var mob in mobs)
            {
                if (mob == null) continue;
                if (!bounds.Contains(mob.transform.position)) continue;
                var health = mob.GetComponent<Health>();
                if (health == null || health.IsAlive) count++;
            }
            return count;
        }

        private void Open()
        {
            opened = true;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
            foreach (var rend in GetComponentsInChildren<Renderer>())
                rend.enabled = false;

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 3f,
                "The way opens.", UI.UITheme.TextGold);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.9f, 0.3f, 0.2f, 0.35f);
            Gizmos.DrawWireCube(guardCenter, guardSize);
        }
    }
}
