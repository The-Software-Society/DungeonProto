using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;
using AldenFall.Progression;
using AldenFall.Stats;

namespace AldenFall.Abilities
{
    /// <summary>
    /// The player's active-ability kit (replaces the old PlayerCombat).
    /// Abilities come from AbilityDatabase, cooldowns and weapon gates live
    /// here, damage flows through CombatMath + the target's Health, statuses
    /// through StatusSystem, and every use feeds AbilityMastery — the
    /// Conquer-style "skills grow with use" loop.
    /// </summary>
    public class PlayerAbilities : MonoBehaviour
    {
        [Header("Projectiles")]
        [SerializeField] private GameObject fireballPrefab;
        [SerializeField] private Transform firePoint;
        [SerializeField] private float fireballSpeed = 18f;

        [Header("Animation")]
        [SerializeField] private Animator animator;

        private TargetingSystem targeting;
        private Health health;
        private StatusSystem statuses;
        private StatBlock stats;
        private Skills skills;
        private AbilityMastery mastery;
        private Equipment equipment;
        private CharacterController controller;
        private Player.PlayerController playerController;

        private readonly Dictionary<string, float> lastUsed = new Dictionary<string, float>();
        private bool isDashing;

        // Pursue-then-cast: an out-of-range targeted ability walks the player
        // in and fires automatically. Any manual input cancels it.
        private string pendingAbilityId;
        private Transform pendingTarget;

        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int SpecialAttackHash = Animator.StringToHash("SpecialAttack");

        /// <summary>PlayerController pauses manual movement during a dash.</summary>
        public bool IsDashing => isDashing;

        // PlayerMelee's magic auto reuses the same projectile setup
        public GameObject FireballPrefab => fireballPrefab;
        public Transform FirePoint => firePoint;

        private void Awake()
        {
            targeting = GetComponent<TargetingSystem>();
            health = GetComponent<Health>();
            statuses = GetComponent<StatusSystem>();
            stats = GetComponent<StatBlock>();
            skills = GetComponent<Skills>();
            mastery = GetComponent<AbilityMastery>();
            equipment = GetComponent<Equipment>();
            controller = GetComponent<CharacterController>();
            playerController = GetComponent<Player.PlayerController>();

            if (animator == null)
                animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (firePoint == null)
            {
                var firePointObj = new GameObject("FirePoint");
                firePoint = firePointObj.transform;
                firePoint.SetParent(transform);
                firePoint.localPosition = new Vector3(0, 1.4f, 0.9f);
            }
        }

        // Number-key input lives in ActionBar — the only number-key reader.

        private void Update()
        {
            HandlePendingCast();
        }

        // ==================== PURSUE-THEN-CAST ====================

        private void HandlePendingCast()
        {
            if (pendingAbilityId == null) return;

            var def = AbilityDatabase.Get(pendingAbilityId);
            bool cancelled =
                def == null
                || pendingTarget == null
                || (health != null && health.IsDead)
                || (targeting != null && targeting.CurrentTarget != pendingTarget)  // clicked away / cleared
                || (playerController != null && (playerController.HasManualMoveInput || playerController.IsSwimming));

            if (cancelled)
            {
                ClearPendingCast();
                return;
            }

            float distance = Vector3.Distance(transform.position, pendingTarget.position);
            if (distance <= def.Range * 0.95f)
            {
                // In range — fire for real (TryActivate re-validates everything)
                string id = pendingAbilityId;
                ClearPendingCast();
                TryActivate(id);
            }
            else
            {
                // RUN in — same pursuit channel as auto-attack, sprint pace
                playerController?.SetExternalMoveIntent(pendingTarget.position - transform.position, run: true);
            }
        }

        private void ClearPendingCast()
        {
            pendingAbilityId = null;
            pendingTarget = null;
        }

        // ==================== PUBLIC API ====================

        public bool CanUse(string abilityId)
        {
            var def = AbilityDatabase.Get(abilityId);
            if (def == null) return false;
            if (GetCooldownRemaining(abilityId) > 0f) return false;
            if (health != null && health.IsDead) return false;
            if (statuses != null && (statuses.IsStunned || statuses.IsSilenced || statuses.IsCombatLocked)) return false;
            if (playerController != null && playerController.IsSwimming) return false;
            if (isDashing) return false;
            if (!HasRequiredWeapon(def.RequiredWeapon)) return false;
            return true;
        }

        public bool HasRequiredWeapon(WeaponClass required)
        {
            if (required == WeaponClass.Any || equipment == null) return required == WeaponClass.Any;
            return required switch
            {
                WeaponClass.Sword => equipment.GetEquipped(EquipSlot.Weapon) != null,
                WeaponClass.Focus => equipment.GetEquipped(EquipSlot.Focus) != null,
                _ => false,
            };
        }

        public bool TryActivate(string abilityId)
        {
            if (!CanUse(abilityId)) return false;
            var def = AbilityDatabase.Get(abilityId);

            Transform target = targeting != null && targeting.HasTarget ? targeting.CurrentTarget : null;

            // Target-required kinds: out of range doesn't fail — it queues a
            // pursue-then-cast (walk in, fire on arrival). No cooldown burned.
            switch (def.Kind)
            {
                case AbilityKind.DashStun:
                case AbilityKind.SingleHit:
                case AbilityKind.MultiHit:
                    if (target == null) return false;
                    if (Vector3.Distance(transform.position, target.position) > def.Range)
                    {
                        pendingAbilityId = abilityId;
                        pendingTarget = target;
                        return true;
                    }
                    break;
            }

            ClearPendingCast(); // a direct cast supersedes any queued one
            lastUsed[abilityId] = Time.time;

            // Any offensive cast breaks Veil
            if (def.Kind != AbilityKind.SelfBuff)
                statuses?.Remove("invisible");

            switch (def.Kind)
            {
                case AbilityKind.Projectile: CastProjectile(def, target); break;
                case AbilityKind.Nova: CastNova(def); break;
                case AbilityKind.DashStun: StartCoroutine(DashStunRoutine(def, target)); break;
                case AbilityKind.SingleHit: StartCoroutine(SingleHitRoutine(def, target)); break;
                case AbilityKind.SelfBuff: CastSelfBuff(def); break;
                case AbilityKind.MultiHit: StartCoroutine(MultiHitRoutine(def, target)); break;
                case AbilityKind.Taunt: CastTaunt(def); break;
            }

            mastery?.AddUse(abilityId);
            return true;
        }

        /// <summary>
        /// Cooldown after skill-level reduction: Attack level shortens melee
        /// trees (Attack/Strength/Defence abilities), Magic shortens spells.
        /// </summary>
        public float EffectiveCooldown(AbilityDef def)
        {
            if (def == null) return 0f;
            float level = 1f;
            if (skills != null)
                level = def.Tree == SkillType.Magic
                    ? skills.Level(SkillType.Magic)
                    : skills.Level(SkillType.Attack);
            return def.Cooldown * CombatMath.CooldownScale(level);
        }

        public float GetCooldownRemaining(string abilityId)
        {
            var def = AbilityDatabase.Get(abilityId);
            if (def == null) return 0f;
            float last = lastUsed.TryGetValue(abilityId, out var t) ? t : float.NegativeInfinity;
            return Mathf.Max(0f, EffectiveCooldown(def) - (Time.time - last));
        }

        public float GetCooldown01(string abilityId)
        {
            var def = AbilityDatabase.Get(abilityId);
            if (def == null || def.Cooldown <= 0f) return 0f;
            return Mathf.Clamp01(GetCooldownRemaining(abilityId) / EffectiveCooldown(def));
        }

        // ==================== KINDS ====================

        private void CastProjectile(AbilityDef def, Transform target)
        {
            if (target != null)
                FaceInstantly(target.position);
            if (animator != null)
                animator.SetTrigger(SpecialAttackHash);

            Vector3 spawnPos = firePoint != null
                ? firePoint.position
                : transform.position + Vector3.up * 1.4f;

            Vector3 aimDirection = transform.forward;
            if (target != null)
            {
                Vector3 aimPoint = target.position + Vector3.up;
                var targetCollider = target.GetComponentInChildren<Collider>();
                if (targetCollider != null) aimPoint = targetCollider.bounds.center;
                aimDirection = (aimPoint - spawnPos).normalized;
            }

            float damage = CombatMath.SpellDamage(stats, def.BaseDamage)
                         * (mastery != null ? mastery.DamageMultiplier(def.Id) : 1f);

            var fireball = fireballPrefab != null
                ? Instantiate(fireballPrefab, spawnPos, Quaternion.LookRotation(aimDirection))
                : new GameObject("Fireball");
            fireball.transform.SetPositionAndRotation(spawnPos, Quaternion.LookRotation(aimDirection));

            var projectile = fireball.GetComponent<Projectile>();
            if (projectile == null)
                projectile = fireball.AddComponent<Projectile>();

            if (projectile != null)
            {
                projectile.SetDamage(damage);
                projectile.SetSpeed(fireballSpeed);
                projectile.SetOnHit(hit =>
                {
                    skills?.AddXp(SkillType.Magic, damage * 1.2f);
                    ApplyDefStatus(def, hit);
                });
                projectile.Initialize(aimDirection, gameObject, target);
            }
        }

        private void CastNova(AbilityDef def)
        {
            // Visual: Whirlwind spins the body, Seismic Slam plays the heavy
            // special swing, spell novas the standard cast swing.
            if (def.SpinCaster)
                StartCoroutine(SpinVisualRoutine());
            else if (animator != null)
                animator.SetTrigger(def.Knockback < 0f ? SpecialAttackHash : AttackHash);

            Vector3 center = transform.position + Vector3.up;
            var colliders = Physics.OverlapSphere(center, def.Radius,
                GameLayers.GetEnemyMask(), QueryTriggerInteraction.Ignore);

            // Radius telegraph so AoE range always reads on screen
            GroundRingEffect.Create(transform.position, def.Radius,
                def.MeleeMultiplier > 0f ? new Color(1f, 0.8f, 0.35f) : new Color(0.45f, 0.7f, 1f));
            if (def.Id == "whirlwind")
                CreateWhirlwindVisual(def.Radius);
            else if (def.Id == "seismic_slam")
                CreateSeismicVisual(def.Radius);

            // Melee novas (Whirlwind/Seismic Slam) scale off weapon damage;
            // spell novas (Ember Nova) off spell power.
            bool meleeNova = def.MeleeMultiplier > 0f;
            float multiplier = mastery != null ? mastery.DamageMultiplier(def.Id) : 1f;
            var damaged = new HashSet<Health>();
            foreach (var col in colliders)
            {
                var enemyHealth = col.GetComponentInParent<Health>();
                if (enemyHealth == null || enemyHealth.gameObject == gameObject) continue;
                if (!damaged.Add(enemyHealth)) continue;

                float damage = (meleeNova
                    ? CombatMath.MeleeDamage(stats) * def.MeleeMultiplier
                    : CombatMath.SpellDamage(stats, def.BaseDamage)) * multiplier;
                enemyHealth.TakeDamage(damage, gameObject);
                if (def.Knockback > 0f)
                {
                    Vector3 knockDir = (enemyHealth.transform.position - transform.position).normalized;
                    enemyHealth.ApplyKnockback(knockDir, def.Knockback);
                }
                else if (def.Knockback < 0f)
                {
                    // Negative knockback = yank toward the caster (Seismic
                    // Slam). A dedicated pull, not knockback physics — the
                    // upward pop and decay made the old version imperceptible.
                    StartCoroutine(PullEnemyRoutine(enemyHealth));
                }
                ApplyDefStatus(def, enemyHealth);
                HitEffect.CreateNovaImpact(enemyHealth.transform.position + Vector3.up);
                skills?.AddXp(TreeSkill(def), damage * 1.2f);
            }

            // Central burst so the cast is always visible even on a whiff.
            HitEffect.CreateNovaImpact(center);
        }

        private IEnumerator MultiHitRoutine(AbilityDef def, Transform target)
        {
            int hits = Mathf.Max(1, def.HitCount);
            for (int i = 0; i < hits; i++)
            {
                if (target == null) yield break;
                if (Vector3.Distance(transform.position, target.position) > def.Range * 1.5f) yield break;

                var targetHealth = target.GetComponentInParent<Health>();
                if (targetHealth == null || targetHealth.IsDead) yield break;

                FaceInstantly(target.position);
                if (animator != null)
                    animator.SetTrigger(AttackHash);

                float damage = CombatMath.MeleeDamage(stats) * def.MeleeMultiplier
                             * (mastery != null ? mastery.DamageMultiplier(def.Id) : 1f);
                targetHealth.TakeDamage(damage, gameObject);
                HitEffect.CreateMeleeImpact(target.position + Vector3.up);
                skills?.AddXp(TreeSkill(def), damage * 1.2f);

                // The final stab opens the wound (bleed)
                if (i == hits - 1)
                    ApplyDefStatus(def, targetHealth);

                yield return new WaitForSeconds(def.HitInterval);
            }
        }

        private IEnumerator PullEnemyRoutine(Health enemyHealth, float duration = 0.3f)
        {
            if (enemyHealth == null) yield break;
            var tr = enemyHealth.transform;
            var agent = enemyHealth.GetComponent<UnityEngine.AI.NavMeshAgent>();
            bool hadAgent = agent != null && agent.enabled;
            if (hadAgent)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                agent.updatePosition = false;
            }

            Vector3 start = tr.position;
            Vector3 toCaster = transform.position - start;
            toCaster.y = 0f;
            // Land them just outside melee arm's reach, never on top of us
            Vector3 goal = start + toCaster - toCaster.normalized * 2f;
            goal.y = start.y;

            float elapsed = 0f;
            while (elapsed < duration && enemyHealth != null && !enemyHealth.IsDead)
            {
                elapsed += Time.deltaTime;
                tr.position = Vector3.Lerp(start, goal, Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }

            if (hadAgent && agent != null)
            {
                agent.updatePosition = true;
                if (UnityEngine.AI.NavMesh.SamplePosition(tr.position, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas))
                    agent.Warp(hit.position);
                if (agent.isOnNavMesh) agent.isStopped = false;
            }
        }

        private IEnumerator SpinVisualRoutine(float duration = 0.45f, float degrees = 720f)
        {
            var visual = animator != null ? animator.transform : null;
            if (visual == null || visual == transform) yield break;

            Quaternion original = visual.localRotation;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                visual.localRotation = original * Quaternion.Euler(0f, degrees * (elapsed / duration), 0f);
                yield return null;
            }
            visual.localRotation = original;
        }

        private void CastTaunt(AbilityDef def)
        {
            if (animator != null)
                animator.SetTrigger(AttackHash);

            GroundRingEffect.Create(transform.position, def.Radius, UI.UITheme.Gold);

            var colliders = Physics.OverlapSphere(transform.position + Vector3.up, def.Radius,
                GameLayers.GetEnemyMask(), QueryTriggerInteraction.Ignore);

            var taunted = new HashSet<AI.EnemyAI>();
            foreach (var col in colliders)
            {
                var enemy = col.GetComponentInParent<AI.EnemyAI>();
                if (enemy == null || !enemy.IsAlive || !taunted.Add(enemy)) continue;
                enemy.ForceAggro(transform);
            }

            // Louder shout, more XP — but always something for using it
            skills?.AddXp(TreeSkill(def), 15f + taunted.Count * 10f);
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f, def.Name, UI.UITheme.Gold);
            HitEffect.CreateNovaImpact(transform.position + Vector3.up);
        }

        private IEnumerator DashStunRoutine(AbilityDef def, Transform target)
        {
            isDashing = true;

            // Speed trail sells the dash even before real VFX exist
            var trail = CreateDashTrail();

            // Distance-budgeted dash: a wall-clock deadline could expire after a
            // single hitchy frame (shader compile, VFX spawn) and silently whiff
            // the whole ability. The budget is generous — the target can back up
            // a bit and the dash still connects.
            float budget = def.Range + 4f;
            while (budget > 0f && target != null)
            {
                Vector3 to = target.position - transform.position;
                to.y = 0f;
                if (to.magnitude <= 1.6f) break;

                transform.rotation = Quaternion.LookRotation(to.normalized);
                // Clamp the step so a hitchy frame can't teleport past the target
                float step = Mathf.Min(def.DashSpeed * Time.deltaTime, to.magnitude - 1.2f, budget);
                if (controller != null && controller.enabled)
                    controller.Move(to.normalized * step);
                else
                    transform.position += to.normalized * step;
                budget -= step;
                yield return null;
            }

            isDashing = false;
            if (trail != null)
            {
                trail.emitting = false;
                Destroy(trail.gameObject, trail.time + 0.1f);
            }
            if (target == null) yield break;

            Vector3 gap = target.position - transform.position;
            gap.y = 0f;
            if (gap.magnitude > 2.4f) yield break; // they got away mid-dash

            FaceInstantly(target.position);
            if (animator != null)
                animator.SetTrigger(AttackHash);

            var targetHealth = target.GetComponentInParent<Health>();
            if (targetHealth == null || targetHealth.IsDead) yield break;

            float damage = CombatMath.MeleeDamage(stats) * def.MeleeMultiplier
                         * (mastery != null ? mastery.DamageMultiplier(def.Id) : 1f);
            targetHealth.TakeDamage(damage, gameObject);
            HitEffect.CreateMeleeImpact(target.position + Vector3.up);

            targetHealth.GetComponent<StatusSystem>()?.Apply(new StatusEffect
            {
                Id = "stun",
                DisplayName = "Stunned",
                Duration = def.StunDuration,
                Flags = StatusFlags.Stunned,
                Source = gameObject,
            });

            skills?.AddXp(TreeSkill(def), damage * 1.2f);
        }

        private IEnumerator SingleHitRoutine(AbilityDef def, Transform target)
        {
            FaceInstantly(target.position);
            if (animator != null)
                animator.SetTrigger(AttackHash);

            yield return new WaitForSeconds(0.28f); // impact lands mid-swing

            if (target == null) yield break;
            if (Vector3.Distance(transform.position, target.position) > def.Range * 1.4f) yield break;

            var targetHealth = target.GetComponentInParent<Health>();
            if (targetHealth == null || targetHealth.IsDead) yield break;

            float damage = CombatMath.MeleeDamage(stats) * def.MeleeMultiplier
                         * (mastery != null ? mastery.DamageMultiplier(def.Id) : 1f);
            targetHealth.TakeDamage(damage, gameObject);
            HitEffect.CreateMeleeImpact(target.position + Vector3.up);
            ApplyDefStatus(def, targetHealth);
            skills?.AddXp(TreeSkill(def), damage * 1.2f);
        }

        private void CastSelfBuff(AbilityDef def)
        {
            // AppliesStatusId lets a buff register under a well-known status id
            // ("invisible" for Veil) that other systems check by name.
            statuses?.Apply(new StatusEffect
            {
                Id = string.IsNullOrEmpty(def.AppliesStatusId) ? def.Id : def.AppliesStatusId,
                DisplayName = def.Name,
                Duration = def.BuffDuration,
                StatMods = def.BuffMods,
                Source = gameObject,
            });
            skills?.AddXp(TreeSkill(def), 20f);
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f, def.Name, UI.UITheme.Gold);
            GroundRingEffect.Create(transform.position, 2.8f, UI.UITheme.Gold);
            HitEffect.CreateNovaImpact(transform.position + Vector3.up);
        }

        /// <summary>Applies the def's on-hit status (bleed/slow/stun/burn) to a target, if any.</summary>
        private void ApplyDefStatus(AbilityDef def, Component target)
        {
            if (string.IsNullOrEmpty(def.AppliesStatusId) || target == null) return;
            target.GetComponentInParent<StatusSystem>()?.Apply(new StatusEffect
            {
                Id = def.AppliesStatusId,
                DisplayName = def.AppliesStatusId,
                Duration = def.StatusDuration,
                TickInterval = def.StatusTickInterval,
                TickDamage = def.StatusTickDamage,
                Flags = def.StatusFlags,
                MoveSpeedMultiplier = def.StatusMoveSpeedMultiplier,
                Source = gameObject,
            });
        }

        private static SkillType TreeSkill(AbilityDef def) => def.Tree;

        private TrailRenderer CreateDashTrail()
        {
            var obj = new GameObject("DashTrail");
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = Vector3.up * 1.1f;
            var trail = obj.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.startWidth = 0.55f;
            trail.endWidth = 0.05f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(1f, 0.85f, 0.5f, 0.85f);
            trail.endColor = new Color(1f, 0.6f, 0.2f, 0f);
            return trail;
        }

        private void CreateWhirlwindVisual(float radius)
        {
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * 2f / 10f;
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(angle), 0.6f, Mathf.Sin(angle)) * radius * 0.75f;
                HitEffect.Create(pos, new Color(1f, 0.78f, 0.22f, 1f));
            }
        }

        private void CreateSeismicVisual(float radius)
        {
            GroundRingEffect.Create(transform.position, radius * 0.55f, new Color(1f, 0.45f, 0.18f, 1f));
            GroundRingEffect.Create(transform.position, radius, new Color(1f, 0.72f, 0.28f, 1f));
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2f / 12f;
                Vector3 pos = transform.position + new Vector3(Mathf.Cos(angle), 0.1f, Mathf.Sin(angle)) * radius * 0.7f;
                HitEffect.Create(pos, new Color(0.95f, 0.62f, 0.28f, 1f));
            }
        }

        private void FaceInstantly(Vector3 worldPos)
        {
            Vector3 look = worldPos - transform.position;
            look.y = 0;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized);
        }

        private void OnDrawGizmosSelected()
        {
            if (firePoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(firePoint.position, 0.1f);
            }
        }
    }
}
