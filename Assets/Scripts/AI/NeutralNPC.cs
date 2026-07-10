using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.AI
{
    /// <summary>Which routine a NeutralNPC performs while idle.</summary>
    public enum DanceStyle
    {
        Modern = 0,   // bouncy hip-hop groove: bob, slide, lean, occasional spin
        Robot = 1,    // stiff quantized snaps and holds
        Twerk = 2,    // deep crouch, lean forward, rapid low bounce
    }

    /// <summary>
    /// A neutral mob that dances instead of hunting — but it's no pushover:
    /// hurt it and it retaliates (ForceAggro on its EnemyAI, which must be
    /// configured with detectionRange 0 so it never aggros on its own). While
    /// the EnemyAI is fighting, the dance pauses so walk/attack animations and
    /// movement work normally; once things calm down, the groove resumes.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class NeutralNPC : MonoBehaviour
    {
        [Header("Dance")]
        [SerializeField] private DanceStyle danceStyle = DanceStyle.Modern;
        [SerializeField] private float bobHeight = 0.14f;
        [SerializeField] private float bobSpeed = 7f;
        [SerializeField] private float swayAngle = 26f;
        [Range(0f, 0.05f)]
        [SerializeField] private float spinChancePerFrame = 0.004f;

        private Health health;
        private EnemyAI enemyAI;
        private Transform visual;
        private Quaternion baseRotation;
        private float seed;
        private float spinUntil;
        private float spinSpeed;
        private bool dancePoseDirty;

        // Real mocap mode: the imported Dance* animator states (Meshy library
        // clips) — the transform puppetry below is only the fallback for
        // builds where the clips haven't been generated/imported yet.
        private Animator animator;
        private int danceStateHash;
        private static readonly int LocomotionHash = Animator.StringToHash("Locomotion");
        private bool hasDanceClip;
        private bool inDanceState;

        /// <summary>
        /// Candidate animator states per style, best first. Mocap library
        /// clips ALWAYS beat hand-authored ones — authored euler curves fight
        /// unknown rig axis conventions and read as broken (learned the hard
        /// way on the pond troupe).
        /// </summary>
        private string[] DanceStateCandidates => danceStyle switch
        {
            // "DanceRobot"/"DanceTwerk" are reserved for video-mocap clips —
            // drop GoblinDancer@DanceRobot.fbx into the Animations folder and
            // they take over automatically. Until then: least-wrong library
            // picks (Breakdance_1990 = nonstop spin, Shake_It_Off = arm pumps
            // — both voted off the island by Jon).
            DanceStyle.Robot => new[] { "DanceRobot", "DanceGangnam", "DanceBreak" },
            DanceStyle.Twerk => new[] { "DanceTwerk", "DanceFunny", "DanceShake" },
            _ => new[] { "DanceHipHop", "DanceBounce" },
        };

        private void Start()
        {
            health = GetComponent<Health>();
            enemyAI = GetComponent<EnemyAI>();
            visual = transform.Find("Visual");
            if (visual == null && transform.childCount > 0)
                visual = transform.GetChild(0);
            baseRotation = visual != null ? visual.localRotation : Quaternion.identity;
            seed = Random.value * 100f;

            animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                foreach (var stateName in DanceStateCandidates)
                {
                    int hash = Animator.StringToHash(stateName);
                    if (!animator.HasState(0, hash)) continue;
                    danceStateHash = hash;
                    hasDanceClip = true;
                    break;
                }
            }
            if (hasDanceClip)
                animator.speed = Random.Range(0.92f, 1.08f); // troupe desync

            // Peaceful until provoked — then it's personal
            health.OnAttacked.AddListener(OnHurt);
        }

        private void OnHurt(GameObject attacker)
        {
            if (enemyAI == null || attacker == null || attacker == gameObject) return;
            enemyAI.ForceAggro(attacker.transform);
        }

        private bool IsCalm =>
            enemyAI == null
            || enemyAI.CurrentState == EnemyState.Idle
            || enemyAI.CurrentState == EnemyState.Patrol;

        private void Update()
        {
            if (visual == null) return;
            if (health != null && health.IsDead) return;

            if (!IsCalm)
            {
                // Fighting: hand the body back to EnemyAI/animator cleanly
                if (inDanceState)
                {
                    animator.CrossFadeInFixedTime(LocomotionHash, 0.15f);
                    inDanceState = false;
                }
                if (dancePoseDirty)
                {
                    visual.localPosition = Vector3.zero;
                    visual.localRotation = baseRotation;
                    dancePoseDirty = false;
                }
                return;
            }

            // Real mocap: cross-fade into the looping dance state and let the
            // skeleton do the work. No transform tricks — they read as stiff.
            if (hasDanceClip)
            {
                if (!inDanceState)
                {
                    animator.CrossFadeInFixedTime(danceStateHash, 0.3f);
                    inDanceState = true;
                }
                return;
            }

            // Fallback puppetry (dance clips not generated/imported yet)
            dancePoseDirty = true;
            switch (danceStyle)
            {
                case DanceStyle.Robot: DanceRobot(); break;
                case DanceStyle.Twerk: DanceTwerk(); break;
                default: DanceModern(); break;
            }
        }

        // ==================== THE ROUTINES ====================

        /// <summary>Bouncy hip-hop groove: hops, side slides, body lean, spins.</summary>
        private void DanceModern()
        {
            float t = Time.time * bobSpeed + seed;

            // Double-bounce hops + a lateral slide on the half beat
            float slide = Mathf.Sin(t * 0.5f) * 0.14f;
            visual.localPosition = new Vector3(slide, Mathf.Abs(Mathf.Sin(t)) * bobHeight, 0f);

            if (Time.time < spinUntil)
            {
                visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);
            }
            else
            {
                // Sway + a cocky lean into the slide direction
                visual.localRotation = baseRotation
                    * Quaternion.Euler(0f, Mathf.Sin(t * 0.5f) * swayAngle, slide * -60f);

                if (Random.value < spinChancePerFrame)
                {
                    spinUntil = Time.time + 0.8f;
                    spinSpeed = (Random.value < 0.5f ? -1f : 1f) * 450f;
                }
            }
        }

        /// <summary>Stiff quantized snaps with robotic holds — no smooth motion at all.</summary>
        private void DanceRobot()
        {
            // Step time: hold a pose, SNAP to the next — square waves, no lerp
            float step = Mathf.Floor((Time.time + seed) / 0.55f);

            // Deterministic pseudo-random pose per step
            float yaw = ((step * 73f) % 8f - 4f) * 22f;          // -88..+88 in 22° steps
            float tilt = ((step * 31f) % 3f - 1f) * 14f;          // -14/0/+14 head-tilt
            float pop = (step % 2f) * 0.07f;                      // alternate-step body pop

            visual.localPosition = Vector3.up * pop;
            visual.localRotation = baseRotation * Quaternion.Euler(tilt, yaw, tilt * 0.6f);
        }

        /// <summary>Deep crouch, heavy forward lean, rapid low bounce. You know the one.</summary>
        private void DanceTwerk()
        {
            float t = Time.time * 13f + seed; // roughly double-time tempo

            // Crouched and leaned WAY forward, bouncing from the hips
            float bounce = Mathf.Abs(Mathf.Sin(t)) * 0.09f;
            visual.localPosition = new Vector3(0f, -0.16f + bounce, -0.06f);
            visual.localRotation = baseRotation
                * Quaternion.Euler(38f + Mathf.Sin(t) * 7f, Mathf.Sin(t * 0.23f) * 14f, 0f);
        }
    }
}
