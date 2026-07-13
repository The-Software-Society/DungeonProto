using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Swept-cast projectile. Hit detection is done with a SphereCast between the
    /// previous and current position every frame, so hits are deterministic and do
    /// not depend on trigger callbacks, rigidbody sync, or collider setup on targets.
    /// Homing follows the target for a limited time, then falls ballistically.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float speed = 18f;
        [SerializeField] private float homingStrength = 6f;
        [SerializeField] private float maxLifetime = 5f;

        [Header("Homing Behavior")]
        [SerializeField] private float maxHomingTime = 2.5f;
        [SerializeField] private float gravityAfterHoming = 15f;

        [Header("Damage")]
        [SerializeField] private float damage = 25f;
        [SerializeField] private float hitRadius = 0.45f;
        [SerializeField] private float knockbackForce = 0f;   // spells don't shove targets around

        [Header("Effects")]
        [SerializeField] private GameObject hitEffectPrefab;

        private Transform target;
        private Vector3 direction;
        private GameObject owner;
        private bool isHoming;
        private float lifetime;
        private float homingElapsed;
        private Vector3 ballisticVelocity;
        private bool exploded;
        private bool visualOnly;   // remote replay: fly + burst visually, but never touch Health
        private System.Action<Health> onHit;
        // Combat XP the caster earns when this bolt actually damages an enemy. Granted
        // server-side inside Health.TakeDamage (via the owner's PlayerCombat), so a bolt that
        // whiffs or hits nothing awards nothing. Defaults to the Magic tree for plain spells.
        private int xpSkill = (int)Progression.SkillType.Magic;
        private float xpRate = 1.2f;

        private void Awake()
        {
            // Safety net FIRST: guarantee despawn even if EnsureVisibleProjectile (or any later
            // step) throws, otherwise the glow orphans forever (esp. the prefab magic bolt).
            Destroy(gameObject, maxLifetime + 0.5f);
            EnsureVisibleProjectile();
        }

        public void Initialize(Vector3 fireDirection, GameObject projectileOwner, Transform homingTarget = null)
        {
            direction = fireDirection.normalized;
            owner = projectileOwner;
            target = homingTarget;
            isHoming = homingTarget != null;
            lifetime = 0f;
            homingElapsed = 0f;
            ballisticVelocity = direction * speed;

            if (direction != Vector3.zero)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        public void SetDamage(float newDamage) => damage = newDamage;
        /// <summary>Cosmetic replay of another player's projectile: flies + bursts, deals no damage.</summary>
        public void SetVisualOnly(bool value) => visualOnly = value;

        /// <summary>
        /// Custom on-hit hook (status effects). Runs client-side on a successful hit for
        /// effects like bleed/burn; XP is handled separately server-side via SetXpReward.
        /// </summary>
        public void SetOnHit(System.Action<Health> handler) => onHit = handler;

        /// <summary>
        /// Which combat skill this bolt trains, and its XP-per-damage rate. The grant happens
        /// server-side, scaled by the damage actually dealt to the enemy, never on cast.
        /// </summary>
        public void SetXpReward(Progression.SkillType skill, float rate)
        {
            xpSkill = (int)skill;
            xpRate = rate;
        }
        public void SetSpeed(float newSpeed)
        {
            speed = newSpeed;
            ballisticVelocity = direction * speed;
        }

        private void EnsureVisibleProjectile()
        {
            // The Fireball prefab (magic bolt) already carries its own mesh + trail + light, so
            // skip the runtime glow when the object is already visible, avoids fragile work
            // (double TrailRenderer, Shader.Find in stripped builds) that could leave an orphan.
            if (transform.Find("ProjectileGlow") != null || GetComponentInChildren<Renderer>() != null)
                return;

            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ProjectileGlow";
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Vector3.zero;
            glow.transform.localScale = Vector3.one * Mathf.Max(0.35f, hitRadius * 1.35f);
            Destroy(glow.GetComponent<Collider>());

            var renderer = glow.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Sprites/Default"));
                material.color = new Color(1f, 0.48f, 0.08f, 1f);
                renderer.material = material;
            }

            var light = glow.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.48f, 0.08f, 1f);
            light.range = 4f;
            light.intensity = 2.5f;

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 0.32f;
            trail.endWidth = 0.02f;
            trail.material = new Material(Shader.Find("Sprites/Default"));
            trail.startColor = new Color(1f, 0.55f, 0.12f, 0.9f);
            trail.endColor = new Color(1f, 0.18f, 0.03f, 0f);
        }

        private void Update()
        {
            if (exploded) return;

            lifetime += Time.deltaTime;
            if (lifetime >= maxLifetime)
            {
                Explode(transform.position, null);
                return;
            }

            Vector3 previousPosition = transform.position;
            Vector3 nextPosition = ComputeNextPosition(previousPosition);

            // Swept hit detection: everything solid or damageable along the travel path.
            if (SweepForHit(previousPosition, nextPosition, out RaycastHit hit))
            {
                transform.position = hit.point;
                Explode(hit.point, hit.collider);
                return;
            }

            transform.position = nextPosition;
        }

        private Vector3 ComputeNextPosition(Vector3 current)
        {
            if (isHoming && target != null && homingElapsed < maxHomingTime)
            {
                homingElapsed += Time.deltaTime;

                // Aim at the target's mid-body: use collider bounds when available.
                Vector3 aimPoint = target.position + Vector3.up;
                var targetCollider = target.GetComponentInChildren<Collider>();
                if (targetCollider != null)
                    aimPoint = targetCollider.bounds.center;

                Vector3 toTarget = (aimPoint - current).normalized;
                direction = Vector3.Slerp(direction, toTarget, homingStrength * Time.deltaTime).normalized;
                ballisticVelocity = direction * speed;

                transform.rotation = Quaternion.LookRotation(direction);
                return current + direction * speed * Time.deltaTime;
            }

            // Ballistic phase, gravity takes over.
            ballisticVelocity += Vector3.down * gravityAfterHoming * Time.deltaTime;
            if (ballisticVelocity.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(ballisticVelocity.normalized);
            return current + ballisticVelocity * Time.deltaTime;
        }

        private bool SweepForHit(Vector3 from, Vector3 to, out RaycastHit result)
        {
            result = default;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < Mathf.Epsilon) return false;

            LayerMask sweepMask = GameLayers.GetWorldMask() | GameLayers.GetEnemyMask() | GameLayers.GetPlayerMask();
            var hits = Physics.SphereCastAll(from, hitRadius, delta / distance, distance, sweepMask,
                QueryTriggerInteraction.Ignore);

            float best = float.MaxValue;
            bool found = false;
            foreach (var hit in hits)
            {
                if (IsOwner(hit.collider.transform)) continue;
                if (IsFriendlyPlayer(hit.collider.transform)) continue; // fly through allies while PvP is off
                if (hit.distance >= best) continue;
                best = hit.distance;
                result = hit;
                found = true;
            }

            // Duel: a remote avatar's ONLY collider is the detection TRIGGER capsule
            // (its CharacterController is disabled on non-owning machines), which the
            // trigger-ignoring sweep above can never hit. While the owner is dueling,
            // run a second trigger-including sweep on the player layer and accept
            // ONLY the duel opponent from it.
            var duel = owner != null ? owner.GetComponent<DuelMember>() : null;
            if (duel != null && duel.IsDueling)
            {
                var triggerHits = Physics.SphereCastAll(from, hitRadius, delta / distance, distance,
                    GameLayers.GetPlayerMask(), QueryTriggerInteraction.Collide);
                foreach (var hit in triggerHits)
                {
                    if (IsOwner(hit.collider.transform)) continue;
                    var avatar = hit.collider.GetComponentInParent<Multiplayer.NetworkPlayerAvatar>();
                    if (avatar == null || !duel.IsDuelingWith(avatar.netId)) continue;
                    if (hit.distance >= best) continue;
                    best = hit.distance;
                    result = hit;
                    found = true;
                }
            }

            return found;
        }

        private bool IsOwner(Transform t)
        {
            return owner != null && (t.gameObject == owner || t.IsChildOf(owner.transform));
        }

        /// <summary>
        /// True when this projectile came from a player and is passing another player
        /// while PvP is disabled, the shot flies through instead of exploding on allies.
        /// Not a hard PvP lock: gated on CombatRules.PvpEnabled for the future PvP system.
        /// </summary>
        private bool IsFriendlyPlayer(Transform t)
        {
            if (CombatRules.PvpEnabled) return false;
            if (owner == null || !CombatRules.IsPlayer(owner)) return false;
            var avatar = t.GetComponentInParent<Multiplayer.NetworkPlayerAvatar>();
            if (avatar == null) return false;

            // Duels: your ACTIVE duel opponent is a legitimate target, the projectile
            // must explode on them even while global PvP stays off (DuelMember's
            // opponent SyncVar replicates, so this client-side check stays honest).
            var duel = owner.GetComponent<DuelMember>();
            if (duel != null && duel.IsDuelingWith(avatar.netId)) return false;

            return true;
        }

        private void Explode(Vector3 position, Collider directHit)
        {
            if (exploded) return;
            exploded = true;

            try
            {
                // Damage the directly hit entity (Health may be on a parent of the collider).
                Health directHealth = directHit != null ? directHit.GetComponentInParent<Health>() : null;
                if (!visualOnly && directHealth != null && !IsOwner(directHealth.transform))
                {
                    // Projectiles are local visuals; route the hit through the caster's
                    // server relay so damage is server-authoritative. Non-player owners
                    // (server-side) apply directly.
                    var ownerCombat = owner != null ? owner.GetComponent<PlayerCombat>() : null;
                    if (ownerCombat != null)
                        // Server mints Magic (or the ability's) XP when this bolt bites an enemy.
                        ownerCombat.DealDamage(directHealth, damage, canMiss: false, xpSkill, xpRate);
                    else
                        directHealth.TakeDamage(damage, owner);   // enemy-cast bolt: no player XP

                    if (knockbackForce > 0f)
                    {
                        Vector3 knockDir = direction.sqrMagnitude > 0.01f
                            ? direction
                            : (directHealth.transform.position - position).normalized;
                        if (ownerCombat != null)
                            ownerCombat.ApplyKnockbackTo(directHealth, knockDir, knockbackForce);
                        else
                            directHealth.ApplyKnockback(knockDir, knockbackForce);
                    }

                    // On-hit effects (bleed/burn/etc.) still run client-side; XP was already
                    // routed server-side through DealDamage above.
                    onHit?.Invoke(directHealth);
                }

                HitEffect.CreateFireballImpact(position);

                if (hitEffectPrefab != null)
                    Instantiate(hitEffectPrefab, position, Quaternion.identity);
            }
            finally
            {
                // Always despawn, even if damage or effects threw.
                Destroy(gameObject);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, hitRadius);
            Gizmos.DrawRay(transform.position, direction * 2f);

            if (target != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, target.position);
            }
        }
    }
}
