using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// A no-aggro volume. While a hostile mob OR a would-be target is inside one,
    /// EnemyAI will not acquire a target. Placed over the pond (fishing/cooking) and
    /// the city so those areas stay peaceful, per playtest feedback. Tested as a
    /// world-space sphere on the XZ plane, so it works server-side (where aggro
    /// runs) without relying on physics trigger events. Neutral dancers are
    /// unaffected (they use detectionRange 0 and only retaliate when hit).
    ///
    /// World-gen usage: new GameObject("SafeZone").AddComponent&lt;SafeZone&gt;().Configure(58f);
    /// </summary>
    public class SafeZone : MonoBehaviour
    {
        private static readonly List<SafeZone> active = new List<SafeZone>();

        [Tooltip("Radius (XZ) of the peaceful area.")]
        [SerializeField] private float radius = 20f;

        public float Radius { get => radius; set => radius = value; }
        public void Configure(float r) => radius = Mathf.Max(0f, r);

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        public bool Contains(Vector3 p)
        {
            float dx = p.x - transform.position.x;
            float dz = p.z - transform.position.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>True if the point is inside any active safe zone (XZ).</summary>
        public static bool Contains(Vector3 p, out SafeZone zone)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i] != null && active[i].Contains(p)) { zone = active[i]; return true; }
            }
            zone = null;
            return false;
        }

        public static bool IsInAnySafeZone(Vector3 p) => Contains(p, out _);
    }
}
