using System.Collections;
using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Progression;
using AldenFall.World;

namespace AldenFall.Player
{
    /// <summary>
    /// Cooking at a campfire: every cycle converts one raw fish. Burn chance
    /// falls 5% per Cooking level, at level 11 fish stop burning entirely.
    /// Cancels the moment you walk off. RNG is injectable for tests.
    /// </summary>
    public class CookingController : MonoBehaviour
    {
        [Header("Loop")]
        [SerializeField] private float cookSeconds = 1.8f;
        [SerializeField] private float xpPerCook = 40f;
        [SerializeField] private float xpPerBurn = 10f;

        private Inventory inventory;
        private Skills skills;
        private Health health;
        private PlayerController controller;
        private Animator animator;
        private Coroutine loop;
        private float cookStartedAt;
        private System.Random rng = new System.Random();

        /// <summary>Fired when a cook SUCCEEDS (not burnt). Arg = cooked item id.
        /// Quest hook: the tutorial requires cooking your own fish, vendor food
        /// must not count.</summary>
        public event System.Action<string> OnCooked;

        private static readonly int AttackHash = Animator.StringToHash("Attack");

        public bool IsCooking => loop != null;

        /// <summary>Burn chance at a given Cooking level: 50% at 1 → 0% at 11+.</summary>
        public static float BurnChance(int cookingLevel) =>
            Mathf.Clamp01(0.5f - 0.05f * (cookingLevel - 1));

        /// <summary>Test hook: seed the roll for deterministic burn sequences.</summary>
        public void SetRandom(System.Random random) => rng = random;

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
            controller = GetComponent<PlayerController>();
        }

        private void Start()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
        }

        public bool StartCooking(Campfire fire)
        {
            if (IsCooking) return false;
            if (inventory == null || FirstRawFish() == null)
            {
                CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                    "Nothing raw to cook", UI.UITheme.TextDim);
                return false;
            }

            if (fire != null)
            {
                Vector3 look = fire.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(look.normalized);
            }

            cookStartedAt = Time.time;
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                "Cooking...", UI.UITheme.Gold);
            if (animator != null)
                animator.SetTrigger(AttackHash);
            loop = StartCoroutine(CookLoop());
            return true;
        }

        public void StopCooking()
        {
            if (loop != null)
            {
                StopCoroutine(loop);
                loop = null;
            }
        }

        private void Update()
        {
            if (!IsCooking) return;
            // Grace window: the E press that starts cooking often lands while a
            // movement key is still held for a frame or two; cancelling instantly
            // (and silently) made cooking look completely broken.
            if (Time.time - cookStartedAt < 0.35f) return;
            bool cancel =
                (controller != null && (controller.HasManualMoveInput || controller.IsSwimming)) ||
                (health != null && health.IsDead);
            if (cancel)
                StopCooking();
        }

        private static readonly string[] RawFish = { "raw_fish", "raw_trout", "raw_bass", "raw_carp" };

        private string FirstRawFish()
        {
            foreach (var id in RawFish)
                if (inventory != null && inventory.CountOf(id) > 0) return id;
            return null;
        }

        private IEnumerator CookLoop()
        {
            string raw;
            while ((raw = FirstRawFish()) != null)
            {
                yield return new WaitForSeconds(cookSeconds);

                if (inventory.CountOf(raw) <= 0) continue;   // eaten/moved mid-cook, re-pick
                var def = ItemDatabase.Get(raw);
                string cooked = def != null && !string.IsNullOrEmpty(def.CookedResult) ? def.CookedResult : "cooked_fish";
                inventory.Remove(raw);

                int level = skills != null ? skills.Level(SkillType.Cooking) : 1;
                bool burnt = rng.NextDouble() < BurnChance(level);

                // GiveOrDrop: with a full bag the result lands on the ground instead of vanishing.
                if (burnt)
                {
                    bool bagged = LootDrop.GiveOrDrop(inventory, "burnt_fish", transform);
                    skills?.AddXp(SkillType.Cooking, xpPerBurn);
                    CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                        bagged ? "Burnt!" : "Burnt! (bag full, dropped)", UI.UITheme.TextDim);
                }
                else
                {
                    bool bagged = LootDrop.GiveOrDrop(inventory, cooked, transform);
                    skills?.AddXp(SkillType.Cooking, xpPerCook);
                    OnCooked?.Invoke(cooked);
                    var cdef = ItemDatabase.Get(cooked);
                    string label = cdef != null ? cdef.Name + "!" : "Cooked!";
                    CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                        bagged ? label : label + " (bag full, dropped)", UI.UITheme.Gold);
                }
            }

            loop = null;
        }
    }
}
