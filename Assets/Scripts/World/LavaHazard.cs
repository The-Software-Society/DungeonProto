using System.Collections.Generic;
using UnityEngine;
using Mirror;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// Damage-over-time volume for lava pools. Attach to a GameObject that carries a
    /// TRIGGER collider sized to the pool (the world-gen adds a BoxCollider with
    /// isTrigger = true). Any entity with a <see cref="Health"/> standing inside takes
    /// <see cref="damagePerSecond"/>, applied in discrete ticks of <see cref="tickInterval"/>.
    ///
    /// Deliberately a plain MonoBehaviour that guards on Mirror's <c>NetworkServer.active</c>
    /// rather than a NetworkBehaviour: the pool is a scenery object placed at runtime by the
    /// world-gen and never needs its own NetworkIdentity. Damage flows through Health's single
    /// server-authoritative <see cref="Health.TakeDamage(float, GameObject, bool)"/> path, so
    /// only the server (host included) ever mutates health; clients see the change replicated
    /// via Health's SyncVar. Works on BOTH players and enemies.
    ///
    /// World-gen usage: create the GO, add a trigger BoxCollider sized to the lava pool, then
    ///   var lava = go.AddComponent&lt;LavaHazard&gt;();
    ///   lava.Configure(12f, 0.5f);
    /// This component only applies damage — visuals (emissive lava mesh, particles) are built
    /// separately by the world-gen.
    /// </summary>
    public class LavaHazard : MonoBehaviour
    {
        [Header("Damage")]
        [Tooltip("Damage dealt per second to anything standing in the lava.")]
        [SerializeField] private float damagePerSecond = 12f;
        [Tooltip("Seconds between damage ticks. Each tick applies damagePerSecond * tickInterval.")]
        [SerializeField] private float tickInterval = 0.5f;

        // Ref-counted so an entity with several child colliders isn't dropped when just one
        // of them leaves the volume. Keyed on Health so players and enemies share one path.
        private readonly Dictionary<Health, int> occupants = new Dictionary<Health, int>();
        private readonly List<Health> pruneScratch = new List<Health>();
        private float tickTimer;

        /// <summary>World-gen entry point. Mirrors the Configure convention used by
        /// TorchFlicker / FishingSpot so the editor builder can tune each pool.</summary>
        public void Configure(float damagePerSecond, float tickInterval)
        {
            this.damagePerSecond = damagePerSecond;
            this.tickInterval = Mathf.Max(0.05f, tickInterval);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active) return; // server owns all damage

            // Colliders frequently sit on a child of the entity root, so search upward.
            var health = other.GetComponentInParent<Health>();
            if (health == null) return;

            occupants.TryGetValue(health, out int refs);
            occupants[health] = refs + 1;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!NetworkServer.active) return;

            var health = other.GetComponentInParent<Health>();
            if (health == null) return;

            if (occupants.TryGetValue(health, out int refs))
            {
                if (refs <= 1) occupants.Remove(health);
                else occupants[health] = refs - 1;
            }
        }

        private void Update()
        {
            // Host counts as server; pure clients never simulate damage.
            if (!NetworkServer.active) return;
            if (occupants.Count == 0) return;

            tickTimer += Time.deltaTime;
            if (tickTimer < tickInterval) return;
            tickTimer -= tickInterval;

            float tickDamage = damagePerSecond * tickInterval;

            // Iterate over a snapshot so we can safely prune destroyed entries (a mob may
            // die and be despawned while still inside the pool → its Health becomes a
            // Unity fake-null and must be forgotten).
            pruneScratch.Clear();
            foreach (var kvp in occupants)
            {
                Health health = kvp.Key;
                if (health == null) { pruneScratch.Add(health); continue; }

                // TakeDamage no-ops on the already-dead, so lingering corpses cost nothing.
                // No attacker → environmental damage, and it can't miss.
                health.TakeDamage(tickDamage);

                if (health.IsDead) pruneScratch.Add(health);
            }

            for (int i = 0; i < pruneScratch.Count; i++)
                occupants.Remove(pruneScratch[i]);
        }

        private void OnDisable()
        {
            occupants.Clear();
            tickTimer = 0f;
        }
    }
}
