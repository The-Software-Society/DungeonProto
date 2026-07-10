using System.Collections;
using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.AI
{
    /// <summary>
    /// Two-phase boss fight. Above the trigger threshold the boss fights
    /// normally. The moment it drops below 50%, a transformation scene plays —
    /// it roars, GROWS, and breaks the ground around it (invulnerable and
    /// frozen while it happens) — then rage mode: much faster, attacking far
    /// more often, hitting harder, and it never stops hunting you.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(EnemyAI))]
    public class BossRageMode : MonoBehaviour
    {
        [Header("Trigger")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float triggerHealthPercent = 0.5f;

        [Header("Transformation Scene")]
        [SerializeField] private float transformSeconds = 2.4f;
        [SerializeField] private float growScale = 1.35f;

        [Header("Rage Buffs")]
        [SerializeField] private float speedMultiplier = 1.6f;
        [SerializeField] private float attackRateMultiplier = 0.55f;  // cooldowns × this
        [SerializeField] private float bonusWeaponPower = 8f;

        private Health health;
        private EnemyAI ai;
        private StatusSystem statuses;
        private Stats.StatBlock stats;
        private bool raged;

        private void Awake()
        {
            health = GetComponent<Health>();
            ai = GetComponent<EnemyAI>();
            statuses = GetComponent<StatusSystem>();
            stats = GetComponent<Stats.StatBlock>();
        }

        private void Start()
        {
            health.OnHealthChanged.AddListener(OnHealthChanged);
        }

        private void OnHealthChanged(float current, float max)
        {
            if (raged || current <= 0f || max <= 0f) return;
            if (current / max > triggerHealthPercent) return;

            raged = true;
            StartCoroutine(TransformRoutine());
        }

        private IEnumerator TransformRoutine()
        {
            // Freeze the boss for the scene — the self-stun keeps EnemyAI
            // parked, invulnerability makes the moment read as scripted.
            statuses?.Apply(new StatusEffect
            {
                Id = "rage_transform",
                DisplayName = "Transforming",
                Duration = transformSeconds,
                Flags = StatusFlags.Stunned,
                Source = gameObject,
            });
            health.SetInvulnerable(true);

            // A real cinematic moment: ALL combat pauses (player locked +
            // invulnerable, every mob frozen), the camera pushes in on the
            // boss, letterbox on — then the fight resumes at full rage.
            PlayRageCinematic();

            Vector3 startScale = transform.localScale;
            Vector3 endScale = startScale * growScale;

            float elapsed = 0f;
            float nextBurst = 0f;
            while (elapsed < transformSeconds)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / transformSeconds;

                // Grow with a building shudder
                float shake = Mathf.Sin(elapsed * 55f) * 0.02f * t;
                transform.localScale = Vector3.Lerp(startScale, endScale,
                    Mathf.SmoothStep(0f, 1f, t)) * (1f + shake);

                // Ground breaking up around it in waves
                if (elapsed >= nextBurst)
                {
                    nextBurst = elapsed + 0.55f;
                    GroundRingEffect.Create(transform.position, 3f + t * 5f, new Color(1f, 0.3f, 0.1f));
                    HitEffect.Create(transform.position + Vector3.up * 0.3f, new Color(1f, 0.4f, 0.15f));
                    SpawnRubble(6, 2.5f + t * 3f);
                }

                yield return null;
            }

            transform.localScale = endScale;
            health.SetInvulnerable(false);

            // Rage: faster, relentless, hits harder
            ai.ApplyRage(speedMultiplier, attackRateMultiplier);
            stats?.ApplyModifiers("boss_rage", new (Stats.StatType, float)[]
            {
                (Stats.StatType.WeaponPower, bonusWeaponPower),
            });
        }

        private void PlayRageCinematic()
        {
            var director = Cinematics.CutsceneDirector.Instance;
            if (director == null || director.IsPlaying)
            {
                // No director available — at least announce it
                CombatEvents.RaiseStatus(transform.position + Vector3.up * 3.5f,
                    "GRUKK IS ENRAGED", new Color(1f, 0.25f, 0.15f));
                return;
            }

            // Frame the boss from the player's side of the room
            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 toPlayer = player != null
                ? (player.transform.position - transform.position).normalized
                : -transform.forward;
            toPlayer.y = 0f;

            Vector3 head = transform.position + Vector3.up * 3.2f;
            Vector3 camStart = transform.position + toPlayer * 10f + Vector3.up * 2.2f;
            Vector3 camEnd = transform.position + toPlayer * 6f + Vector3.up * 3.4f;

            director.Play(new Cinematics.CutsceneData
            {
                id = "boss_rage",
                letterbox = true,
                steps = new[]
                {
                    new Cinematics.CutsceneStep { type = "camera", pos = Arr(camStart), lookAt = Arr(head), seconds = 0f },
                    new Cinematics.CutsceneStep { type = "subtitle", text = "GRUKK IS ENRAGED", seconds = transformSeconds, blocking = false },
                    new Cinematics.CutsceneStep { type = "camera", pos = Arr(camEnd), lookAt = Arr(head), seconds = transformSeconds + 0.6f },
                },
            });
        }

        private static float[] Arr(Vector3 v) => new[] { v.x, v.y, v.z };

        /// <summary>Chunks of broken ground tossed outward, cleaned up after a few seconds.</summary>
        private void SpawnRubble(int count, float radius)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                Vector3 pos = transform.position
                    + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (radius * Random.Range(0.6f, 1f));

                var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "RageRubble";
                chunk.transform.position = pos + Vector3.up * 0.1f;
                chunk.transform.rotation = Random.rotation;
                chunk.transform.localScale = Vector3.one * Random.Range(0.25f, 0.6f);
                Destroy(chunk.GetComponent<Collider>());

                var renderer = chunk.GetComponent<MeshRenderer>();
                renderer.material.color = new Color(0.32f, 0.28f, 0.25f);

                var rb = chunk.AddComponent<Rigidbody>();
                rb.mass = 2f;
                rb.linearVelocity = new Vector3(Mathf.Cos(angle) * 2f, Random.Range(4f, 8f), Mathf.Sin(angle) * 2f);
                rb.angularVelocity = Random.insideUnitSphere * 8f;

                Destroy(chunk, 4f);
            }
        }
    }
}
