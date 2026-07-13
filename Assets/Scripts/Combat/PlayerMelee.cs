using UnityEngine;
using AldenFall.Items;
using AldenFall.Progression;

namespace AldenFall.Combat
{
    /// <summary>
    /// LoL-style basic attacks. Right-click an enemy (attack order) and the
    /// character handles the rest with zero camera/click/target fiddling:
    ///   - walks into range on its own (pursuit via PlayerController intent)
    ///   - in melee range: swings on the StatBlock interval (sword auto)
    ///   - focus equipped + target at range: casts a weak Magic Bolt instead
    /// Autos are deliberately low damage, abilities carry the fight.
    /// </summary>
    public class PlayerMelee : MonoBehaviour
    {
        [Header("Melee")]
        [SerializeField] private float meleeRange = 2.6f;
        [SerializeField] private float hitDelay = 0.32f;   // impact lands mid-swing
        [SerializeField] private float lungeDistance = 0.55f;

        [Header("Magic Auto (needs a focus)")]
        [SerializeField] private float magicAutoRange = 14f;
        [SerializeField] private float boltBaseDamage = 8f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private TargetingSystem targeting;
        private CombatStyleController combatStyle;
        private Stats.StatBlock stats;
        private Health health;
        private StatusSystem statuses;
        private Equipment equipment;
        private CharacterController controller;
        private Player.PlayerController playerController;
        private Abilities.PlayerAbilities abilities;
        private PlayerCombat combat;   // server-auth damage relay
        private float lastSwingTime = float.NegativeInfinity;
        private bool engaged; // attack order active: pursue until cancelled

        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");

        public float MeleeRange => meleeRange;
        public bool IsEngaged => engaged;
        public float CooldownPercent =>
            Mathf.Clamp01(1f - (Time.time - lastSwingTime) / Stats.CombatMath.SwingInterval(stats));

        private void Awake()
        {
            targeting = GetComponent<TargetingSystem>();
            combatStyle = GetComponent<CombatStyleController>();
            stats = GetComponent<Stats.StatBlock>();
            health = GetComponent<Health>();
            statuses = GetComponent<StatusSystem>();
            equipment = GetComponent<Equipment>();
            controller = GetComponent<CharacterController>();
            playerController = GetComponent<Player.PlayerController>();
            abilities = GetComponent<Abilities.PlayerAbilities>();
            combat = GetComponent<PlayerCombat>();
        }

        private void Start()
        {
            // Animator lives on the visual child, which exists by now
            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (targeting != null)
            {
                targeting.OnEngage.AddListener(OnEngageOrder);
                targeting.OnTargetLost.AddListener(OnTargetLost);
            }

            if (health != null)
                health.OnAttacked.AddListener(OnAttacked);
        }

        /// <summary>Re-resolve the Animator onto the active body after a gender/body swap.</summary>
        public void RefreshAnimator()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void OnDestroy()
        {
            if (health != null)
                health.OnAttacked.RemoveListener(OnAttacked);
            if (targeting != null)
            {
                targeting.OnEngage.RemoveListener(OnEngageOrder);
                targeting.OnTargetLost.RemoveListener(OnTargetLost);
            }
        }

        private void OnEngageOrder(Transform target) => engaged = true;
        private void OnTargetLost() => engaged = false;

        private void OnAttacked(GameObject attacker)
        {
            if (attacker == null || targeting == null) return;
            var attackerHealth = attacker.GetComponentInParent<Health>();
            if (attackerHealth == null || attackerHealth.IsDead || !attackerHealth.CompareTag("Enemy")) return;

            if (!targeting.HasTarget)
                targeting.SetTarget(attackerHealth.transform);
            engaged = true;
        }

        private void Update()
        {
            if (health != null && health.IsDead) return;
            if (statuses != null && (statuses.IsStunned || statuses.IsCombatLocked)) return;
            if (targeting == null || !targeting.HasTarget)
            {
                engaged = false;
                return;
            }

            // Steering with WASD cancels the attack order (LoL move command)
            if (engaged && playerController != null && playerController.HasManualMoveInput)
                engaged = false;
            if (playerController != null && playerController.IsSwimming)
                engaged = false;

            var target = targeting.CurrentTarget;
            float distance = Vector3.Distance(transform.position, target.position);
            // Any magic implement (off-hand focus OR main-hand wand) enables magic autos at range.
            bool hasFocus = equipment != null && equipment.HasMagicImplement;
            float autoRange = hasFocus ? magicAutoRange : meleeRange;

            bool onCooldown = Time.time - lastSwingTime < Stats.CombatMath.SwingInterval(stats);

            if (distance <= meleeRange)
            {
                if (!onCooldown)
                    StartSwing(target);
            }
            else if (hasFocus && distance <= magicAutoRange)
            {
                if (!onCooldown)
                    CastBolt(target);
            }
            else if (engaged)
            {
                // Walk into range on our own, no camera or clicking required
                playerController?.SetExternalMoveIntent(target.position - transform.position);
            }
        }

        // ==================== SWORD AUTO ====================

        private void StartSwing(Transform target)
        {
            lastSwingTime = Time.time;
            statuses?.Remove("invisible"); // attacking breaks Veil

            // Face the target
            Vector3 look = target.position - transform.position;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized);

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.SetTrigger(AttackHash);

            StartCoroutine(SwingRoutine(target));
        }

        private System.Collections.IEnumerator SwingRoutine(Transform target)
        {
            // Small lunge into the swing, sells the weight of the attack
            Vector3 lungeDir = (target.position - transform.position);
            lungeDir.y = 0;
            lungeDir.Normalize();
            float lungeTime = 0.12f;
            float elapsed = 0f;
            while (elapsed < lungeTime && controller != null)
            {
                elapsed += Time.deltaTime;
                controller.Move(lungeDir * (lungeDistance / lungeTime) * Time.deltaTime);
                yield return null;
            }

            yield return new WaitForSeconds(Mathf.Max(0f, hitDelay - lungeTime));

            if (target == null) yield break;
            float distance = Vector3.Distance(transform.position, target.position);
            if (distance > meleeRange * 1.4f) yield break; // they stepped away

            var targetHealth = target.GetComponentInParent<Health>();
            if (targetHealth == null || targetHealth.IsDead) yield break;

            float damage = Stats.CombatMath.MeleeDamage(stats);

            // XP is granted server-side the instant this damage actually lands (Health.TakeDamage),
            // scaled by the damage dealt and routed to the SELECTED combat style, you level what
            // you choose. Passing it through the hit means a whiff (Defence miss) or an
            // undamageable target awards nothing: no phantom XP, ever.
            var trained = combatStyle != null ? combatStyle.TrainedSkill : SkillType.Attack;
            combat?.DealDamage(targetHealth, damage, canMiss: true, xpSkill: (int)trained, xpRate: 2.4f);
            HitEffect.CreateMeleeImpact(target.position + Vector3.up);
        }

        // ==================== MAGIC AUTO ====================

        private void CastBolt(Transform target)
        {
            var prefab = abilities != null ? abilities.FireballPrefab : null;
            if (prefab == null) return;

            lastSwingTime = Time.time;
            statuses?.Remove("invisible"); // attacking breaks Veil

            Vector3 look = target.position - transform.position;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized);

            if (animator != null)
                animator.SetTrigger(SpecialAttackHash);

            Vector3 spawnPos = abilities != null && abilities.FirePoint != null
                ? abilities.FirePoint.position
                : transform.position + Vector3.up * 1.4f;

            Vector3 aimPoint = target.position + Vector3.up;
            var targetCollider = target.GetComponentInChildren<Collider>();
            if (targetCollider != null) aimPoint = targetCollider.bounds.center;
            Vector3 aimDirection = (aimPoint - spawnPos).normalized;

            var bolt = Object.Instantiate(prefab, spawnPos, Quaternion.LookRotation(aimDirection));
            bolt.transform.localScale *= 0.6f; // visibly a lesser missile than Fireball
            var projectile = bolt.GetComponent<Projectile>();
            if (projectile != null)
            {
                // No callback: the projectile's own Magic XP grant applies
                projectile.SetDamage(Stats.CombatMath.SpellDamage(stats, boltBaseDamage));
                projectile.Initialize(aimDirection, gameObject, target);
            }
        }
    }
}
