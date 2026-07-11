using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.Items
{
    /// <summary>
    /// Eating food: heals, consumes one from the stack, and applies an
    /// "eating" combat lockout — while chewing you cannot attack or cast
    /// (PlayerAbilities / PlayerMelee both respect IsCombatLocked), but you
    /// can still move and dodge.
    /// </summary>
    public class PlayerConsumables : MonoBehaviour
    {
        private const string EatingStatusId = "eating";

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private Inventory inventory;
        private Health health;
        private PlayerCombat combat;
        private StatusSystem statuses;
        private bool eatTriggerChecked;
        private bool hasEatTrigger;

        private static readonly int EatHash = Animator.StringToHash("Eat");

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            health = GetComponent<Health>();
            combat = GetComponent<PlayerCombat>();
            statuses = GetComponent<StatusSystem>();
        }

        public bool IsEating => statuses != null && statuses.Has(EatingStatusId);
        public float Lockout01
        {
            get
            {
                if (statuses == null) return 0f;
                float remaining = statuses.Remaining(EatingStatusId);
                return remaining <= 0f ? 0f : Mathf.Clamp01(remaining / lastLockout);
            }
        }
        public float LockoutRemaining => statuses != null ? statuses.Remaining(EatingStatusId) : 0f;

        private float lastLockout = 1f;

        public bool TryEat(string itemId)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null || !def.IsConsumable) return false;
            if (health == null || health.IsDead) return false;
            if (IsEating) return false;
            if (inventory == null || inventory.CountOf(itemId) <= 0) return false;

            inventory.Remove(itemId);
            if (combat != null) combat.Heal(def.HealAmount);   // server-authoritative heal
            else health.Heal(def.HealAmount);
            lastLockout = Mathf.Max(0.1f, def.EatLockout);

            statuses?.Apply(new StatusEffect
            {
                Id = EatingStatusId,
                DisplayName = "Eating",
                Duration = def.EatLockout,
                Flags = StatusFlags.CombatLocked,
                Source = gameObject,
            });

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f,
                $"+{def.HealAmount:0}", UI.UITheme.HpGreen);
            PlayEatAnimation();
            OnAte?.Invoke(itemId);   // quest/tutorial hook (EatFood objectives)
            return true;
        }

        /// <summary>Raised when food is successfully eaten. Arg = item id.</summary>
        public event System.Action<string> OnAte;

        private void PlayEatAnimation()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            if (!eatTriggerChecked)
            {
                eatTriggerChecked = true;
                foreach (var p in animator.parameters)
                    if (p.type == AnimatorControllerParameterType.Trigger && p.nameHash == EatHash)
                        hasEatTrigger = true;
            }
            if (hasEatTrigger)
                animator.SetTrigger(EatHash);
        }
    }
}
