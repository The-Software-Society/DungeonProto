using UnityEngine;
using UnityEngine.Events;
using UnityEngine.AI;
using System.Collections;
using Mirror;

namespace AldenFall.Combat
{
    /// <summary>
    /// Server-authoritative health. The SERVER owns every mutation (damage, heal,
    /// regen, death); <see cref="currentHealth"/> is a SyncVar that replicates to
    /// clients, whose hook drives local health bars. Damage numbers, knockback and
    /// death are broadcast via ClientRpc so every player sees the same fight.
    ///
    /// maxHealth is NOT a SyncVar: it's derived deterministically from StatBlock in
    /// Awake on every client, and runtime changes (buffs, boss rage) are pushed with
    /// RpcSyncMaxHealth.
    /// </summary>
    public class Health : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SyncVar(hook = nameof(OnHealthSync))] private float currentHealth;

        [Header("Knockback")]
        [SerializeField] private float knockbackResistance = 0f; // 0 = full knockback, 1 = immune

        [Header("Regen")]
        [Tooltip("Always-on trickle, % of max health per second. Slow and steady — not a combat factor.")]
        [SerializeField] private float regenPercentPerSecond = 0.25f;
        [Tooltip("Faster recovery rate once no damage has been taken for restedDelay seconds.")]
        [SerializeField] private float restedRegenPercentPerSecond = 2f;
        [SerializeField] private float restedDelay = 5f;

        [Header("Events")]
        public UnityEvent<float, float> OnHealthChanged; // current, max
        public UnityEvent OnDeath;
        public UnityEvent<float> OnDamaged; // damage amount
        public UnityEvent<float> OnHealed;  // heal amount
        public UnityEvent<Vector3, float> OnKnockback; // direction, force

        private bool isInvulnerable;
        private Vector3 knockbackVelocity;
        private bool isBeingKnockedBack;
        private NavMeshAgent navAgent;
        private CharacterController charController;
        private float lastDamageTime = float.NegativeInfinity;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public float HealthPercent => maxHealth > 0 ? currentHealth / maxHealth : 0f;
        public bool IsDead => currentHealth <= 0;
        public bool IsAlive => currentHealth > 0;
        public bool IsInvulnerable => isInvulnerable;

        private void Awake()
        {
            // StatBlock is authoritative for max health when present. This runs on every
            // machine identically, so clients agree on maxHealth without syncing it.
            var stats = GetComponent<Stats.StatBlock>();
            if (stats != null)
                maxHealth = stats.Get(Stats.StatType.MaxHealth);

            currentHealth = maxHealth;
            navAgent = GetComponent<NavMeshAgent>();
            charController = GetComponent<CharacterController>();
        }

        private void Start()
        {
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Update()
        {
            // All health simulation is server-authoritative (host counts as server).
            if (!isServer) return;

            // Apply knockback movement for NavMeshAgent-based entities (enemies).
            if (isBeingKnockedBack && navAgent != null)
            {
                ProcessKnockback();
            }

            HandleRegen();
        }

        private void HandleRegen()
        {
            if (IsDead || currentHealth >= maxHealth) return;

            // Trickle always; proper recovery once out of the fray. The trickle
            // plus Defence-driven misses is what makes far-lower mobs unable to
            // ever finish off a tanky character.
            bool rested = Time.time - lastDamageTime >= restedDelay;
            float rate = (rested ? restedRegenPercentPerSecond : regenPercentPerSecond) / 100f;
            currentHealth = Mathf.Min(maxHealth, currentHealth + maxHealth * rate * Time.deltaTime);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void ProcessKnockback()
        {
            if (knockbackVelocity.sqrMagnitude < 0.5f) // Increased threshold
            {
                isBeingKnockedBack = false;
                if (navAgent != null && navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = false;
                }
                return;
            }

            // Apply knockback movement
            if (navAgent != null)
            {
                // Temporarily disable NavMeshAgent and move manually
                if (navAgent.enabled && navAgent.isOnNavMesh)
                {
                    navAgent.isStopped = true;
                }

                // Move the transform directly (server owns enemy position → NetworkTransform replicates)
                transform.position += knockbackVelocity * Time.deltaTime;

                // Decay the velocity SLOWLY (was 8f, now 2.5f for longer knockback)
                knockbackVelocity = Vector3.Lerp(knockbackVelocity, Vector3.zero, 2.5f * Time.deltaTime);
            }
        }

        public void ApplyKnockback(Vector3 direction, float force)
        {
            if (!isServer) return;
            if (IsDead || force <= 0) return;

            // Apply resistance
            float actualForce = force * (1f - knockbackResistance);
            if (actualForce <= 0) return;

            // Normalize and apply force
            direction = direction.normalized;
            direction.y = Mathf.Max(direction.y, 0.3f); // Slightly more upward for visible arc

            // Enemies: ProcessKnockback (server) moves the transform via knockbackVelocity.
            knockbackVelocity = direction * actualForce;
            isBeingKnockedBack = true;

            // Players: the owning client's PlayerController applies it (client-authoritative
            // movement). Remotes have PlayerController disabled, so only the owner reacts.
            RpcKnockback(direction, actualForce);
        }

        [ClientRpc]
        private void RpcKnockback(Vector3 direction, float force)
        {
            OnKnockback?.Invoke(direction, force);
        }

        public bool IsBeingKnockedBack => isBeingKnockedBack;
        public Vector3 KnockbackVelocity => knockbackVelocity;

        /// <summary>Raised with the PRE-mitigation damage (Defence XP hook).</summary>
        public UnityEvent<float> OnRawDamaged;

        /// <summary>Raised with whoever hurt us (retaliation hook for neutral mobs).</summary>
        public UnityEvent<GameObject> OnAttacked;

        /// <summary>
        /// One damage path for everything. Server-authoritative: runs only on the server,
        /// which mutates the SyncVar and broadcasts the number. canMiss is opt-in (melee
        /// autos and mob attacks): the defender's Defence can turn those into a 0.
        /// </summary>
        public void TakeDamage(float damage, GameObject attacker = null, bool canMiss = false)
        {
            if (!isServer) return;                                   // server owns all damage
            if (IsDead || damage <= 0 || isInvulnerable) return;

            // Friendly-fire guard: one player's attack can't hurt another player while
            // PvP is off. Single authoritative chokepoint — melee, spells and DoT ticks
            // all pass through here. Toggle CombatRules.PvpEnabled to re-enable PvP.
            if (attacker != null && !CombatRules.PvpEnabled
                && CombatRules.IsPlayer(attacker) && CombatRules.IsPlayer(gameObject))
                return;

            if (canMiss && attacker != null)
            {
                var attackerStats = attacker.GetComponent<Stats.StatBlock>();
                var defenderStats = GetComponent<Stats.StatBlock>();
                if (Random.value < Stats.CombatMath.MissChance(attackerStats, defenderStats))
                {
                    // A whiff still counts as aggression (neutral mobs retaliate)
                    OnAttacked?.Invoke(attacker);
                    RpcStatusText(transform.position + Vector3.up * 1.6f,
                        "Miss", new Color(0.65f, 0.7f, 0.82f));
                    return;
                }
            }

            lastDamageTime = Time.time;

            // Raw-damage hook drives Defence XP. For a networked player it must fire on
            // the OWNING CLIENT: that's where the player picks their combat style and
            // where the trained level is read + replicated back to the server (via
            // PlayerStatBridge) for authoritative mitigation. Enemies and the host's own
            // player (isLocalPlayer here on the server) train inline.
            if (CombatRules.IsPlayer(gameObject) && !isLocalPlayer)
                TargetTrainDefence(damage);
            else
                OnRawDamaged?.Invoke(damage);

            if (attacker != null)
                OnAttacked?.Invoke(attacker);

            // Mitigation is universal: any entity with a StatBlock gets its
            // Defence + Armor applied here — one damage path for everything.
            var stats = GetComponent<Stats.StatBlock>();
            if (stats != null)
                damage *= 1f - Stats.CombatMath.DamageReduction(stats);

            currentHealth = Mathf.Max(0, currentHealth - damage);   // SyncVar → clients update via hook
            OnDamaged?.Invoke(damage);                              // server-side gameplay listeners
            OnHealthChanged?.Invoke(currentHealth, maxHealth);      // server-side (e.g. BossRageMode)

            // Floating damage numbers (white = player-dealt, red = taken) on every client.
            bool fromPlayer = attacker != null && CombatRules.IsPlayer(attacker);
            RpcDamageNumber(transform.position + Vector3.up * 1.6f, damage, fromPlayer);

            if (IsDead)
            {
                Die();
            }
        }

        // ---- SyncVar hook + client-facing RPCs ----

        private void OnHealthSync(float oldValue, float newValue)
        {
            // Pure clients drive their UI from the synced value. On the host the server
            // code above already fired these events, so skip to avoid double-invoking.
            if (isServer) return;
            OnHealthChanged?.Invoke(newValue, maxHealth);
            if (newValue < oldValue) OnDamaged?.Invoke(oldValue - newValue);
            else if (newValue > oldValue) OnHealed?.Invoke(newValue - oldValue);
        }

        // Defence trains on the owning client so the level the player sees is the one
        // replicated to the server for mitigation. PlayerDefence listens to OnRawDamaged.
        [TargetRpc]
        private void TargetTrainDefence(float rawDamage)
        {
            OnRawDamaged?.Invoke(rawDamage);
        }

        [ClientRpc]
        private void RpcDamageNumber(Vector3 position, float amount, bool fromPlayer)
        {
            if (NearLocalPlayer(position))
                CombatEvents.RaiseDamage(position, amount, fromPlayer);
        }

        [ClientRpc]
        private void RpcStatusText(Vector3 position, string text, Color color)
        {
            if (NearLocalPlayer(position))
                CombatEvents.RaiseStatus(position, text, color);
        }

        // ClientRpc has no interest management, so a hit anywhere reaches every client. Only
        // show floating combat text for fights near the local player — otherwise you'd see
        // other zones' damage numbers on your screen.
        private static bool NearLocalPlayer(Vector3 worldPos)
        {
            var local = Multiplayer.LocalPlayerLocator.Find();
            if (local == null) return true; // no local player resolved yet — don't suppress
            return (local.transform.position - worldPos).sqrMagnitude <= 40f * 40f;
        }

        public void SetInvulnerable(bool invulnerable)
        {
            isInvulnerable = invulnerable;
        }

        public void Heal(float amount)
        {
            if (!isServer) return;
            if (IsDead || amount <= 0) return;

            float oldHealth = currentHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            float actualHeal = currentHealth - oldHealth;

            if (actualHeal > 0)
            {
                OnHealed?.Invoke(actualHeal);
                OnHealthChanged?.Invoke(currentHealth, maxHealth);
            }
        }

        public void SetMaxHealth(float newMax, bool healToFull = false)
        {
            if (!isServer) return;
            maxHealth = Mathf.Max(1, newMax);
            if (healToFull)
            {
                currentHealth = maxHealth;
            }
            else
            {
                currentHealth = Mathf.Min(currentHealth, maxHealth);
            }
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            RpcSyncMaxHealth(maxHealth);
        }

        [ClientRpc]
        private void RpcSyncMaxHealth(float newMax)
        {
            if (isServer) return;
            maxHealth = newMax;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public void ResetHealth()
        {
            if (!isServer) return;
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        private void Die()
        {
            // Server-side listeners: enemy respawn (EnemySpawner/MobCamp) + EnemyAI death anim.
            OnDeath?.Invoke();

            // Only players need client-side death handling (PlayerDeath screen). Broadcasting
            // to enemies would re-trigger their (client-disabled) EnemyAI death listener.
            if (CombatRules.IsPlayer(gameObject))
                RpcDeath();
        }

        [ClientRpc]
        private void RpcDeath()
        {
            if (isServer) return;   // host already fired OnDeath server-side
            OnDeath?.Invoke();
        }

        [ContextMenu("Test Damage (10)")]
        private void TestDamage()
        {
            TakeDamage(10);
        }

        [ContextMenu("Test Heal (10)")]
        private void TestHeal()
        {
            Heal(10);
        }
    }
}
