using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Decorative shoal of fish that patrol and bob just under the water surface within a
    /// box volume — pure ambiance for the lake and its feeder rivers. There is NO networking,
    /// NO collision with the player and NO Health here: every client runs its own independent
    /// copy of the motion, which is fine because nothing about it is gameplay-authoritative.
    ///
    /// World-gen usage: create an empty GO at the water's centre, then
    ///   var school = go.AddComponent&lt;FishSchool&gt;();
    ///   school.Configure(center, extents, count: 10, surfaceY: waterLevel);
    /// If dropped in-scene without Configure it falls back to the serialized defaults so it
    /// still runs standalone.
    /// </summary>
    public class FishSchool : MonoBehaviour
    {
        [Header("Volume (world-space)")]
        [SerializeField] private Vector3 center;
        [SerializeField] private Vector3 extents = new Vector3(8f, 2f, 8f);
        [Tooltip("World Y of the water surface. Fish stay a little below this.")]
        [SerializeField] private float surfaceY = 0f;

        [Header("Shoal")]
        [Tooltip("Number of fish spawned. Keep it modest — this is decoration.")]
        [SerializeField] private int count = 10;

        [Header("Motion tuning")]
        [SerializeField] private float minSpeed = 0.8f;
        [SerializeField] private float maxSpeed = 1.8f;
        [Tooltip("How sharply a fish turns toward a new heading (per second).")]
        [SerializeField] private float turnSpeed = 1.4f;
        [Tooltip("Vertical bob amplitude around the fish's cruising depth.")]
        [SerializeField] private float bobAmplitude = 0.15f;
        [Tooltip("Clearance kept below the surface and above the bed.")]
        [SerializeField] private float depthMargin = 0.3f;

        private readonly List<Fish> fish = new List<Fish>();
        private Material sharedFishMaterial;
        private bool spawned;

        /// <summary>One swimmer: its transform plus the little bit of state that drives
        /// smooth, looping, per-fish motion (heading, speed, bob phase, cruise depth).</summary>
        private class Fish
        {
            public Transform transform;
            public Vector3 heading;       // current unit swim direction
            public Vector3 wanderTarget;  // heading it is steering toward
            public float speed;
            public float bobPhase;
            public float bobSpeed;
            public float cruiseY;         // baseline depth this fish patrols at
        }

        /// <summary>World-gen entry point. Positions the volume and (re)builds the shoal.
        /// Mirrors the Configure convention used by the other World components.</summary>
        public void Configure(Vector3 center, Vector3 extents, int count, float surfaceY)
        {
            this.center = center;
            this.extents = new Vector3(Mathf.Abs(extents.x), Mathf.Abs(extents.y), Mathf.Abs(extents.z));
            this.count = Mathf.Max(0, count);
            this.surfaceY = surfaceY;

            if (spawned) Rebuild();
            else Spawn();
        }

        private void Start()
        {
            if (!spawned) Spawn();
        }

        private void Rebuild()
        {
            foreach (var f in fish)
                if (f.transform != null) Destroy(f.transform.gameObject);
            fish.Clear();
            Spawn();
        }

        private void Spawn()
        {
            spawned = true;

            // One shared, unlit-friendly material for the whole shoal (cheap: a single
            // draw material, tinted a muted fishy silver-blue).
            if (sharedFishMaterial == null)
            {
                sharedFishMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "FishScaleMat"
                };
                Color body = new Color(0.55f, 0.62f, 0.68f);
                sharedFishMaterial.color = body;
                if (sharedFishMaterial.HasProperty("_BaseColor"))
                    sharedFishMaterial.SetColor("_BaseColor", body);
            }

            for (int i = 0; i < count; i++)
                fish.Add(CreateFish(i));
        }

        private Fish CreateFish(int index)
        {
            // Elongated body: a stretched cube reads as a fish at ambiance distance and is
            // the cheapest possible primitive. Collider stripped — this never touches gameplay.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Fish_" + index;
            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);
            body.transform.SetParent(transform, worldPositionStays: true);
            body.transform.localScale = new Vector3(0.14f, 0.14f, 0.5f); // long along +Z (forward)

            var rend = body.GetComponent<MeshRenderer>();
            rend.sharedMaterial = sharedFishMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var f = new Fish
            {
                transform = body.transform,
                heading = RandomHorizontalDir(),
                wanderTarget = RandomHorizontalDir(),
                speed = Random.Range(minSpeed, maxSpeed),
                bobPhase = Random.value * Mathf.PI * 2f,
                bobSpeed = Random.Range(1.2f, 2.2f),
                cruiseY = Random.Range(BedY() + depthMargin, TopY() - depthMargin)
            };

            f.transform.position = RandomPointInVolume(f.cruiseY);
            f.transform.rotation = Quaternion.LookRotation(f.heading);
            return f;
        }

        private void Update()
        {
            if (fish.Count == 0) return;

            float dt = Time.deltaTime;
            Vector3 min = center - extents;
            Vector3 max = center + extents;
            float bedY = BedY() + depthMargin;
            float topY = TopY() - depthMargin;

            for (int i = 0; i < fish.Count; i++)
            {
                Fish f = fish[i];
                if (f.transform == null) continue;
                Vector3 pos = f.transform.position;

                // Steer back toward the volume centre when close to a wall, otherwise keep
                // meandering toward the current wander target. Blend gives smooth arcs.
                bool nearEdge =
                    pos.x < min.x + 1f || pos.x > max.x - 1f ||
                    pos.z < min.z + 1f || pos.z > max.z - 1f;

                if (nearEdge)
                {
                    Vector3 toCenter = new Vector3(center.x - pos.x, 0f, center.z - pos.z);
                    if (toCenter.sqrMagnitude > 0.0001f)
                        f.wanderTarget = toCenter.normalized;
                }
                else if (Random.value < 0.01f)
                {
                    // Occasional gentle course change so paths never feel robotic.
                    f.wanderTarget = RandomHorizontalDir();
                }

                // Ease the heading toward the target; keep it horizontal (bob handles Y).
                f.heading = Vector3.Slerp(f.heading, f.wanderTarget, turnSpeed * dt);
                f.heading.y = 0f;
                if (f.heading.sqrMagnitude < 0.0001f) f.heading = RandomHorizontalDir();
                f.heading.Normalize();

                // Advance along the heading, then layer a sine bob for the cruise depth.
                pos += f.heading * (f.speed * dt);
                f.bobPhase += f.bobSpeed * dt;
                float y = f.cruiseY + Mathf.Sin(f.bobPhase) * bobAmplitude;
                pos.y = Mathf.Clamp(y, bedY, topY);

                // Hard clamp horizontal position so a fast turn can never escape the box.
                pos.x = Mathf.Clamp(pos.x, min.x, max.x);
                pos.z = Mathf.Clamp(pos.z, min.z, max.z);
                f.transform.position = pos;

                // Face the way it swims (with a touch of the bob's vertical for liveliness).
                Vector3 look = f.heading;
                look.y = Mathf.Cos(f.bobPhase) * bobAmplitude * f.bobSpeed;
                if (look.sqrMagnitude > 0.0001f)
                    f.transform.rotation = Quaternion.Slerp(
                        f.transform.rotation, Quaternion.LookRotation(look), turnSpeed * dt);
            }
        }

        // --- helpers ---

        private float TopY() => surfaceY;                    // just under the water surface
        private float BedY() => center.y - extents.y;        // bottom of the volume

        private static Vector3 RandomHorizontalDir()
        {
            Vector2 d = Random.insideUnitCircle.normalized;
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            return new Vector3(d.x, 0f, d.y);
        }

        private Vector3 RandomPointInVolume(float y)
        {
            return new Vector3(
                Random.Range(center.x - extents.x, center.x + extents.x),
                y,
                Random.Range(center.z - extents.z, center.z + extents.z));
        }

        private void OnDestroy()
        {
            if (sharedFishMaterial != null) Destroy(sharedFishMaterial);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.6f, 0.9f, 0.25f);
            Gizmos.DrawWireCube(center, extents * 2f);
        }
    }
}
