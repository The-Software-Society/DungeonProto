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
    /// through StatusSystem, and every use feeds AbilityMastery, the
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
        private PlayerCombat combat;   // routes damage to the server (server-authoritative combat)
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
            combat = GetComponent<PlayerCombat>();
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

        // Number-key input lives in ActionBar, the only number-key reader.

        /// <summary>Re-resolve the Animator onto the active body. After a gender/body swap on a
        /// remote avatar the Awake-cached ref points at the now-inactive body, so casts (and
        /// movement) would drive a hidden Animator.</summary>
        public void RefreshAnimator()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

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
                // In range, fire for real (TryActivate re-validates everything)
                string id = pendingAbilityId;
                ClearPendingCast();
                TryActivate(id);
            }
            else
            {
                // RUN in, same pursuit channel as auto-attack, sprint pace
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

        /// <summary>
        /// Reads BOTH hands. Sword abilities need a Sword-class MAIN hand (a wand
        /// won't do); Focus abilities accept a focus OFF hand OR a wand main hand,
        /// so a sword-and-focus fighter and a wand-only caster both keep their kit.
        /// </summary>
        public bool HasRequiredWeapon(WeaponClass required)
        {
            if (required == WeaponClass.Any) return true;
            if (equipment == null) return false;
            return required switch
            {
                WeaponClass.Sword => equipment.HasSwordMainHand,
                WeaponClass.Focus => equipment.HasMagicImplement,
                _ => false,
            };
        }

        public bool TryActivate(string abilityId)
        {
            if (!CanUse(abilityId)) return false;
            var def = AbilityDatabase.Get(abilityId);

            Transform target = targeting != null && targeting.HasTarget ? targeting.CurrentTarget : null;

            // Target-required kinds: out of range doesn't fail, it queues a
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
                case AbilityKind.GroundAoe: StartCoroutine(GroundAoeRoutine(def, target)); break;
                case AbilityKind.Blink: CastBlink(def); break;
            }

            // Replicate this cast's visual + animation to other clients (cosmetic only -
            // damage is already server-authoritative, so no double-damage).
            if (combat != null)
            {
                Vector3 castOrigin = firePoint != null ? firePoint.position : transform.position + Vector3.up * 1.4f;
                Vector3 castAim = target != null ? ((target.position + Vector3.up) - castOrigin).normalized : transform.forward;
                var targetId = target != null ? target.GetComponentInParent<Mirror.NetworkIdentity>() : null;
                combat.BroadcastCast(abilityId, castOrigin, castAim, targetId);
            }

            mastery?.AddUse(abilityId);
            OnAbilityUsed?.Invoke(abilityId);   // quest/tutorial hook (UseAbility objectives)
            return true;
        }

        /// <summary>Raised on a successful ability activation. Arg = ability id.</summary>
        public event System.Action<string> OnAbilityUsed;

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

            SpawnProjectile(def, target, spawnPos, aimDirection, visualOnly: false);
        }

        /// <summary>
        /// Spawns the fireball. Used by the local cast (deals damage) and by remote replay
        /// (visualOnly = damage-free, the server already applied it). The SpecialAttack cast
        /// animation plays either way, so remote players see the caster point and the fireball fly.
        /// </summary>
        private void SpawnProjectile(AbilityDef def, Transform target, Vector3 spawnPos, Vector3 aimDirection, bool visualOnly)
        {
            if (animator != null)
                animator.SetTrigger(SpecialAttackHash);

            var bolt = new GameObject(def.Id == "fireball" ? "RuntimeFireball" : "RuntimeBolt");
            bolt.transform.SetPositionAndRotation(spawnPos, Quaternion.LookRotation(aimDirection));

            // A tint builds a coloured glow BEFORE the Projectile awakes, so its
            // EnsureVisibleProjectile sees an existing renderer and skips the default
            // orange look, Frost Bolt reads icy-blue, Hex reads sickly-purple.
            if (def.ProjectileTint.HasValue)
                BuildBoltVisual(bolt, def.ProjectileTint.Value);

            var projectile = bolt.AddComponent<Projectile>();
            projectile.SetSpeed(def.ProjectileSpeed > 0f ? def.ProjectileSpeed : fireballSpeed);

            if (visualOnly)
            {
                projectile.SetVisualOnly(true);
                projectile.SetDamage(0f);
            }
            else
            {
                float damage = CombatMath.SpellDamage(stats, def.BaseDamage)
                             * (mastery != null ? mastery.DamageMultiplier(def.Id) : 1f);
                projectile.SetDamage(damage);
                // XP is minted server-side when the bolt lands on an enemy (scaled by real
                // damage), routed to this ability's tree. The client hook only applies status.
                projectile.SetXpReward(TreeSkill(def), 1.2f);
                projectile.SetOnHit(hit => ApplyDefStatus(def, hit));
            }

            projectile.Initialize(aimDirection, gameObject, target);
        }

        /// <summary>
        /// Cosmetic replay of another player's cast on THIS observer. Damage is already
        /// server-authoritative, so this only spawns visuals + plays the cast animation.
        /// Runs on the remote avatar's (disabled) PlayerAbilities via PlayerCombat's Rpc, so it
        /// must avoid coroutines, coroutine-heavy effects fall back to the swing animation.
        /// </summary>
        public void ReplayRemoteCast(string abilityId, Vector3 origin, Vector3 aimDir, Transform target)
        {
            var def = AbilityDatabase.Get(abilityId);
            if (def == null) return;

            switch (def.Kind)
            {
                case AbilityKind.Projectile:
                    SpawnProjectile(def, target, origin,
                        aimDir.sqrMagnitude > 0.001f ? aimDir : transform.forward, visualOnly: true);
                    break;
                case AbilityKind.Nova:
                    ReplayNovaVisual(def);
                    break;
                case AbilityKind.GroundAoe:
                    // Damage-free meteor: same landing point maths as the local cast.
                    if (animator != null)
                        animator.SetTrigger(SpecialAttackHash);
                    FallingOrbEffect.Create(ResolveGroundPoint(def, target, origin, aimDir),
                        def.Radius, def.ImpactDelay, MeteorColor);
                    break;
                case AbilityKind.Blink:
                    // Position is replicated by NetworkTransform; just poof at the arrival point.
                    if (animator != null)
                        animator.SetTrigger(SpecialAttackHash);
                    HitEffect.Create(origin + Vector3.up, BlinkColor);
                    break;
                default:
                    // Dash / melee / buff: the swing animation carries the read; dashes also
                    // replicate their movement via NetworkTransform.
                    if (animator != null)
                        animator.SetTrigger(AttackHash);
                    break;
            }
        }

        private void ReplayNovaVisual(AbilityDef def)
        {
            if (animator != null)
                animator.SetTrigger(def.Knockback < 0f ? SpecialAttackHash : AttackHash);
            GroundRingEffect.Create(transform.position, def.Radius,
                def.MeleeMultiplier > 0f ? new Color(1f, 0.8f, 0.35f) : new Color(0.45f, 0.7f, 1f));
            HitEffect.CreateNovaImpact(transform.position + Vector3.up);
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
                // Server grants this ability's XP per enemy actually damaged, scaled by the hit.
                combat?.DealDamage(enemyHealth, damage, canMiss: false, (int)TreeSkill(def), 1.2f);
                if (def.Knockback > 0f)
                {
                    Vector3 knockDir = (enemyHealth.transform.position - transform.position).normalized;
                    combat?.ApplyKnockbackTo(enemyHealth, knockDir, def.Knockback);
                }
                else if (def.Knockback < 0f)
                {
                    // Negative knockback = yank toward the caster (Seismic
                    // Slam). A dedicated pull, not knockback physics, the
                    // upward pop and decay made the old version imperceptible.
                    StartCoroutine(PullEnemyRoutine(enemyHealth));
                }
                ApplyDefStatus(def, enemyHealth);
                HitEffect.CreateNovaImpact(enemyHealth.transform.position + Vector3.up);
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
                combat?.DealDamage(targetHealth, damage, canMiss: false, (int)TreeSkill(def), 1.2f);
                HitEffect.CreateMeleeImpact(target.position + Vector3.up);

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

            // Louder shout, more XP, but always something for using it
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
            // the whole ability. The budget is generous, the target can back up
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
            combat?.DealDamage(targetHealth, damage, canMiss: false, (int)TreeSkill(def), 1.2f);
            HitEffect.CreateMeleeImpact(target.position + Vector3.up);

            targetHealth.GetComponent<StatusSystem>()?.Apply(new StatusEffect
            {
                Id = "stun",
                DisplayName = "Stunned",
                Duration = def.StunDuration,
                Flags = StatusFlags.Stunned,
                Source = gameObject,
            });
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
            combat?.DealDamage(targetHealth, damage, canMiss: false, (int)TreeSkill(def), 1.2f);
            HitEffect.CreateMeleeImpact(target.position + Vector3.up);
            ApplyDefStatus(def, targetHealth);
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
                StatMods = AldenFall.Items.StatBonus.ToTuples(def.BuffMods),
                Source = gameObject,
            });
            skills?.AddXp(TreeSkill(def), 20f);
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f, def.Name, UI.UITheme.Gold);
            GroundRingEffect.Create(transform.position, 2.8f, UI.UITheme.Gold);
            HitEffect.CreateNovaImpact(transform.position + Vector3.up);
        }

        private static readonly Color MeteorColor = new Color(1f, 0.5f, 0.15f, 1f);
        private static readonly Color BlinkColor = new Color(0.65f, 0.4f, 1f, 1f);

        /// <summary>
        /// Ranged targeted AoE (Meteor Strike): a meteor falls onto the target's
        /// location and, after ImpactDelay, blasts everything in Radius. Damage is
        /// server-authoritative via PlayerCombat; the orb itself is pure visual.
        /// </summary>
        private IEnumerator GroundAoeRoutine(AbilityDef def, Transform target)
        {
            if (animator != null)
                animator.SetTrigger(SpecialAttackHash);
            if (target != null)
                FaceInstantly(target.position);

            Vector3 point = ResolveGroundPoint(def, target, transform.position, transform.forward);
            FallingOrbEffect.Create(point, def.Radius, def.ImpactDelay, MeteorColor);

            yield return new WaitForSeconds(def.ImpactDelay);

            var colliders = Physics.OverlapSphere(point + Vector3.up * 0.5f, def.Radius,
                GameLayers.GetEnemyMask(), QueryTriggerInteraction.Ignore);

            float multiplier = mastery != null ? mastery.DamageMultiplier(def.Id) : 1f;
            var damaged = new HashSet<Health>();
            foreach (var col in colliders)
            {
                var enemyHealth = col.GetComponentInParent<Health>();
                if (enemyHealth == null || enemyHealth.gameObject == gameObject) continue;
                if (!damaged.Add(enemyHealth)) continue;

                float damage = CombatMath.SpellDamage(stats, def.BaseDamage) * multiplier;
                // Server grants this ability's XP per enemy actually damaged, scaled by the hit.
                combat?.DealDamage(enemyHealth, damage, canMiss: false, (int)TreeSkill(def), 1.2f);
                ApplyDefStatus(def, enemyHealth);
                HitEffect.CreateNovaImpact(enemyHealth.transform.position + Vector3.up);
            }
        }

        /// <summary>
        /// Where a GroundAoe lands: the target's feet if there is one, else a point
        /// out in front, snapped down onto the world so the meteor hits the floor.
        /// </summary>
        private Vector3 ResolveGroundPoint(AbilityDef def, Transform target, Vector3 fallbackOrigin, Vector3 fallbackDir)
        {
            Vector3 p = target != null
                ? target.position
                : fallbackOrigin + fallbackDir.normalized * (def.Range * 0.6f);

            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var hit, 14f,
                    GameLayers.GetWorldMask(), QueryTriggerInteraction.Ignore))
                p = hit.point;
            return p;
        }

        /// <summary>
        /// Mobility (Arcane Blink): instantly teleport the caster forward up to Range,
        /// stopping short of any wall. NetworkTransform replicates the new position to
        /// other clients; the arrival poof is replayed cosmetically via ReplayRemoteCast.
        /// </summary>
        private void CastBlink(AbilityDef def)
        {
            Vector3 dir = transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            dir.Normalize();

            float distance = def.Range;
            Vector3 chest = transform.position + Vector3.up;
            if (Physics.Raycast(chest, dir, out var wall, distance,
                    GameLayers.GetWorldMask(), QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0f, wall.distance - 1f);

            Vector3 origin = transform.position;
            Vector3 destination = origin + dir * distance;

            // Toggle the controller so the teleport isn't swallowed by CharacterController.
            if (controller != null && controller.enabled)
            {
                controller.enabled = false;
                transform.position = destination;
                controller.enabled = true;
            }
            else
            {
                transform.position = destination;
            }

            if (animator != null)
                animator.SetTrigger(SpecialAttackHash);
            HitEffect.Create(origin + Vector3.up, BlinkColor);
            HitEffect.Create(destination + Vector3.up, BlinkColor);
            skills?.AddXp(SkillType.Magic, 10f);
        }

        /// <summary>
        /// Builds a coloured glow + trail on a runtime bolt, mirroring the fireball's
        /// runtime look but tinted. Named "ProjectileGlow" so Projectile.EnsureVisible
        /// treats the bolt as already-visible and skips the default orange build.
        /// </summary>
        private static void BuildBoltVisual(GameObject bolt, Color core)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ProjectileGlow";
            glow.transform.SetParent(bolt.transform, false);
            glow.transform.localPosition = Vector3.zero;
            glow.transform.localScale = Vector3.one * 0.5f;
            Destroy(glow.GetComponent<Collider>());

            var renderer = glow.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = core;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", core * 2f);
                renderer.material = mat;
            }

            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = core;
            light.range = 4f;
            light.intensity = 2.5f;

            var trail = bolt.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 0.32f;
            trail.endWidth = 0.02f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            var tail = core; tail.a = 0f;
            trail.startColor = core;
            trail.endColor = tail;
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
                // Debuff stat mods (Hex: negative Defence/Armor = damage amp) routed
                // through the target's StatBlock, cleared when the status expires.
                StatMods = def.StatusStatMods != null && def.StatusStatMods.Count > 0 ? AldenFall.Items.StatBonus.ToTuples(def.StatusStatMods) : null,
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
