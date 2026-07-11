using System.Collections;
using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Progression;
using AldenFall.World;

namespace AldenFall.Player
{
    /// <summary>
    /// OSRS-style AFK fishing: interact once and the character keeps casting —
    /// a catch tick every few seconds grants a raw fish + Fishing XP until you
    /// move, get hit, dodge, die, or the bag fills. Requires a fishing pole.
    /// </summary>
    public class FishingController : MonoBehaviour
    {
        [Header("Loop (serialized so tests can force determinism)")]
        [SerializeField] private float tickSeconds = 2.5f;
        [SerializeField, Range(0f, 1f)] private float catchChance = 0.75f;
        [SerializeField] private float xpPerFish = 30f;

        [Header("Requirements")]
        [SerializeField] private string requiredTool = "fishing_pole";
        [SerializeField] private string caughtItem = "raw_fish";

        private PlayerController controller;
        private Inventory inventory;
        private Skills skills;
        private Health health;
        private Animator animator;
        private FishingSpot spot;
        private Coroutine loop;
        private GameObject bobber;
        private float tickStartedAt;

        private static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");

        public bool IsFishing => loop != null;
        public float Tick01 => IsFishing
            ? Mathf.Clamp01((Time.time - tickStartedAt) / Mathf.Max(0.05f, tickSeconds))
            : 0f;

        // Test hooks
        public void ConfigureLoop(float seconds, float chance)
        {
            tickSeconds = seconds;
            catchChance = chance;
        }

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            inventory = GetComponent<Inventory>();
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
        }

        private void Start()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (health != null)
                health.OnDamaged.AddListener(OnDamaged);
        }

        private void OnDamaged(float amount) => StopFishing();

        public bool StartFishing(FishingSpot fishingSpot)
        {
            if (IsFishing) return false;
            if (inventory == null || !inventory.Contains(requiredTool))
            {
                CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                    "You need a fishing pole", UI.UITheme.TextDim);
                return false;
            }

            spot = fishingSpot;

            // Face the water
            if (spot != null)
            {
                Vector3 look = spot.BobberAnchor - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(look.normalized);
            }

            loop = StartCoroutine(FishLoop());
            return true;
        }

        public void StopFishing()
        {
            if (loop != null)
            {
                StopCoroutine(loop);
                loop = null;
            }
            if (bobber != null)
            {
                Destroy(bobber);
                bobber = null;
            }
        }

        private void Update()
        {
            if (!IsFishing) return;

            // Any of these breaks the trance
            bool cancel =
                (controller != null && (controller.HasManualMoveInput || controller.IsSwimming)) ||
                (health != null && health.IsDead) ||
                Input.GetKeyDown(KeyCode.Space);
            if (cancel)
                StopFishing();
        }

        private IEnumerator FishLoop()
        {
            SpawnBobber();

            while (true)
            {
                tickStartedAt = Time.time;

                // Cast gesture each tick (dedicated fishing anim can replace this)
                if (animator != null)
                    animator.SetTrigger(SpecialAttackHash);

                yield return new WaitForSeconds(tickSeconds);

                if (inventory.IsFull && inventory.CountOf(caughtItem) == 0)
                {
                    CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                        "Your bag is full", UI.UITheme.TextDim);
                    break;
                }

                if (Random.value <= catchChance)
                {
                    string caught = PickCatch();
                    if (!inventory.Add(caught))
                    {
                        CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                            "Your bag is full", UI.UITheme.TextDim);
                        break;
                    }
                    skills?.AddXp(SkillType.Fishing, xpPerFish);
                    SplashBobber();
                    OnFishCaught?.Invoke(caught);
                }
            }

            loop = null;
            if (bobber != null)
            {
                Destroy(bobber);
                bobber = null;
            }
        }

        /// <summary>Fired when a fish is caught (tutorial/quest hook). Arg = caught item id.</summary>
        public event System.Action<string> OnFishCaught;

        /// <summary>Level-gated catch: higher Fishing level unlocks + weights rarer fish.</summary>
        private string PickCatch()
        {
            int lvl = skills != null ? skills.Level(SkillType.Fishing) : 1;
            float r = Random.value;
            if (lvl >= 20 && r < 0.12f) return "raw_carp";
            if (lvl >= 10 && r < 0.30f) return "raw_bass";
            if (lvl >= 5 && r < 0.55f) return "raw_trout";
            return "raw_fish";
        }

        // ---------- bobber (primitive until a real model earns its place) ----------

        private void SpawnBobber()
        {
            if (spot == null) return;
            bobber = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bobber.name = "Bobber";
            Destroy(bobber.GetComponent<Collider>());
            bobber.transform.position = spot.BobberAnchor;
            bobber.transform.localScale = Vector3.one * 0.18f;
            var renderer = bobber.GetComponent<MeshRenderer>();
            renderer.material.color = new Color(0.9f, 0.25f, 0.2f);
            StartCoroutine(BobberBob());
        }

        private IEnumerator BobberBob()
        {
            while (bobber != null)
            {
                float bob = Mathf.Sin(Time.time * 2.2f) * 0.06f;
                bobber.transform.position = spot.BobberAnchor + Vector3.up * bob;
                yield return null;
            }
        }

        private void SplashBobber()
        {
            if (bobber != null)
                StartCoroutine(SplashPunch());
        }

        private IEnumerator SplashPunch()
        {
            float elapsed = 0f;
            const float duration = 0.3f;
            Vector3 baseScale = Vector3.one * 0.18f;
            while (elapsed < duration && bobber != null)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                bobber.transform.localScale = baseScale * (1f + 0.9f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            if (bobber != null)
                bobber.transform.localScale = baseScale;
        }
    }
}
