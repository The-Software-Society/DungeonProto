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
        private Items.Inventory inventory;   // arrow ammo for the bow
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
            inventory = GetComponent<Items.Inventory>();
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
            // A bow makes the player a ranged attacker (arrows at range, no melee).
            // Otherwise any magic implement (off-hand focus OR main-hand wand) enables magic autos.
            bool hasBow = equipment != null && equipment.HasBow;
            bool hasFocus = equipment != null && equipment.HasMagicImplement;

            bool onCooldown = Time.time - lastSwingTime < Stats.CombatMath.SwingInterval(stats);

            if (hasBow)
            {
                if (distance <= magicAutoRange)
                {
                    if (!onCooldown) CastArrow(target);
                }
                else if (engaged)
                    playerController?.SetExternalMoveIntent(target.position - transform.position);
            }
            else if (hasFocus)
            {
                // A caster (wand/focus) ALWAYS casts - never a melee swing. Previously, standing in
                // melee range made a caster auto-SWING, which trained the melee style skill (Attack/
                // Strength/Defence) instead of Magic (Jon: "did magic but leveled attack").
                if (distance <= magicAutoRange)
                {
                    if (!onCooldown) CastBolt(target);
                }
                else if (engaged)
                    playerController?.SetExternalMoveIntent(target.position - transform.position);
            }
            else if (distance <= meleeRange)
            {
                if (!onCooldown)
                    StartSwing(target);
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

            // Let other players see the swing (visual only; damage is server-authoritative).
            combat?.BroadcastAuto(0, transform.position, target.position - transform.position, target.position);

            StartCoroutine(SwingRoutine(target));
        }

        /// <summary>Replay another player's auto-attack on THIS observer: kind 0=melee swing,
        /// 1=magic bolt, 2=arrow. Damage-free — the server already resolved the hit. Runs on the
        /// remote avatar's (disabled) PlayerMelee via PlayerCombat's Rpc.</summary>
        public void ReplayRemoteAuto(byte kind, Vector3 origin, Vector3 aimDir, Vector3 targetPos)
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (kind == 0)
            {
                if (animator != null) animator.SetTrigger(AttackHash);
                return;
            }
            if (animator != null) animator.SetTrigger(SpecialAttackHash);
            if (aimDir.sqrMagnitude < 0.0001f) return;

            Projectile p;
            if (kind == 2)
            {
                // Arrow: a real procedural arrow, not a shrunken fireball.
                p = Projectile.SpawnArrow(origin, aimDir.normalized, 42f);
            }
            else
            {
                var prefab = abilities != null ? abilities.FireballPrefab : null;
                if (prefab == null) return;
                var proj = Object.Instantiate(prefab, origin, Quaternion.LookRotation(aimDir.normalized));
                proj.transform.localScale *= 0.6f;
                p = proj.GetComponent<Projectile>();
            }
            if (p != null)
            {
                p.SetVisualOnly(true);
                p.SetDamage(0f);
                p.Initialize(aimDir.normalized, gameObject, null);
            }
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

            // XP is granted server-side the instant this damage actually lands (Health.TakeDamage),
            // scaled by the damage dealt and routed to the SELECTED combat style, you level what
            // you choose. Passing it through the hit means a whiff (Defence miss) or an
            // undamageable target awards nothing: no phantom XP, ever. Damage itself is recomputed
            // server-side from the caster's synced stats (anti-cheat), not sent from the client.
            var trained = combatStyle != null ? combatStyle.TrainedSkill : SkillType.Attack;
            combat?.DealAttack(targetHealth, (byte)PlayerCombat.DamageKind.Melee, null, 1f, 0f,
                canMiss: true, (int)trained, 2.4f);
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
                // No callback: the projectile's own Magic XP grant applies. Defensive stance trades
                // offence for guard (half). Damage is recomputed server-side from the descriptor.
                float mult = combatStyle != null ? combatStyle.OffenseMultiplier : 1f;
                projectile.SetServerAttack((byte)PlayerCombat.DamageKind.Spell, null, mult, boltBaseDamage);
                projectile.Initialize(aimDirection, gameObject, target);
            }
        }

        // ==================== ARCHERY AUTO ====================

        private void CastArrow(Transform target)
        {
            // Consume one arrow; without ammo the bow cannot loose.
            if (inventory == null || !inventory.Remove("arrow")) return;

            lastSwingTime = Time.time;
            statuses?.Remove("invisible");

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

            var projectile = Projectile.SpawnArrow(spawnPos, aimDirection, 42f);
            if (projectile != null)
            {
                // Defensive stance trades offence for guard (half). Damage recomputed server-side.
                float mult = combatStyle != null ? combatStyle.OffenseMultiplier : 1f;
                projectile.SetServerAttack((byte)PlayerCombat.DamageKind.Ranged, null, mult, 0f);
                projectile.SetXpReward(Progression.SkillType.Ranged, 2.0f);   // arrows train Ranged
                projectile.Initialize(aimDirection, gameObject, target);
            }
        }
    }
}
