using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AldenFall.Abilities;
using AldenFall.AI;
using AldenFall.Combat;
using AldenFall.Player;

namespace AldenFall.Tests
{
    /// <summary>
    /// Combat-feel regression tests for the v4 rework: enemy basic attacks must
    /// land damage WITHOUT shoving the player around (knockback is a boss-only
    /// special now — constant knockback made melee unplayable).
    /// </summary>
    public class CombatFeelTests
    {
        private const string SceneName = "EmberfallDemo";

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return new WaitForSeconds(0.5f);
        }

        private static PlayerController FindPlayer()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No PlayerController in scene");
            return player;
        }

        private static void Teleport(Component obj, Vector3 position)
        {
            var cc = obj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            obj.transform.position = position;
            if (cc != null) cc.enabled = true;
        }

        /// <summary>
        /// Always test against a goblin — FindFirstObjectByType can return the
        /// boss, whose jump/charge specials legitimately knock back and whose
        /// Defence shrugs off small test hits.
        /// </summary>
        private static EnemyAI FindGoblin()
        {
            foreach (var enemy in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                var identity = enemy.GetComponent<MobIdentity>();
                if (identity != null && identity.DisplayName.Contains("Goblin"))
                    return enemy;
            }
            Assert.Fail("no goblin in scene");
            return null;
        }

        [UnityTest]
        public IEnumerator EnemyBasicAttackDoesNotDisplacePlayer()
        {
            var player = FindPlayer();
            var playerHealth = player.GetComponent<Health>();
            Assert.IsNotNull(playerHealth, "player has no Health");

            var goblin = FindGoblin();
            Assert.IsNotNull(goblin, "no enemy in scene");

            // Park the player right in the goblin's face so it attacks immediately.
            Teleport(player, goblin.transform.position + goblin.transform.forward * 1.2f);
            yield return null;

            Vector3 anchor = player.transform.position;
            float startHp = playerHealth.CurrentHealth;
            bool tookHit = false;
            bool knockedBack = false;

            // Two full attack cooldowns' worth of standing in melee.
            float deadline = Time.time + 4.5f;
            while (Time.time < deadline)
            {
                if (playerHealth.CurrentHealth < startHp) tookHit = true;
                if (playerHealth.IsBeingKnockedBack) knockedBack = true;
                yield return null;
            }

            Assert.IsTrue(tookHit, "goblin never landed a basic attack — test setup is wrong");
            Assert.IsFalse(knockedBack, "basic attack applied knockback — should be boss-special only");

            Vector3 drift = player.transform.position - anchor;
            drift.y = 0f;
            Assert.Less(drift.magnitude, 0.5f,
                $"player displaced {drift.magnitude:F2}u by basic attacks (limit 0.5u)");
        }

        [UnityTest]
        public IEnumerator StunFreezesGoblin()
        {
            var player = FindPlayer();
            var goblin = FindGoblin();
            Assert.IsNotNull(goblin, "no enemy in scene");
            var statuses = goblin.GetComponent<StatusSystem>();
            Assert.IsNotNull(statuses, "enemy has no StatusSystem");

            // Aggro it so we know it WANTS to move, then stun it.
            Teleport(player, goblin.transform.position + Vector3.forward * 5f);
            float aggroDeadline = Time.time + 3f;
            while (Time.time < aggroDeadline && goblin.CurrentState != EnemyState.Chase &&
                   goblin.CurrentState != EnemyState.Attack)
                yield return null;

            statuses.Apply(new StatusEffect
            {
                Id = "stun",
                DisplayName = "Stunned",
                Duration = 1.5f,
                Flags = StatusFlags.Stunned,
            });
            yield return null;

            Assert.IsTrue(statuses.IsStunned, "stun did not register");

            Vector3 frozenAt = goblin.transform.position;
            yield return new WaitForSeconds(1.0f);
            float moved = (goblin.transform.position - frozenAt).magnitude;
            Assert.Less(moved, 0.15f, $"stunned goblin moved {moved:F2}u");

            // After expiry it resumes chasing the nearby player.
            yield return new WaitForSeconds(1.0f);
            Assert.IsFalse(statuses.IsStunned, "stun never expired");

            float resumeDeadline = Time.time + 3f;
            bool resumed = false;
            while (Time.time < resumeDeadline && !resumed)
            {
                resumed = (goblin.transform.position - frozenAt).magnitude > 0.3f ||
                          goblin.CurrentState == EnemyState.Attack;
                yield return null;
            }
            Assert.IsTrue(resumed, "goblin never resumed after stun expired");
        }

        [UnityTest]
        public IEnumerator FireballAbilityBurnsWithoutKnockback()
        {
            var player = FindPlayer();
            var abilities = player.GetComponent<PlayerAbilities>();
            var targeting = player.GetComponent<TargetingSystem>();
            Assert.IsNotNull(abilities, "player has no PlayerAbilities");

            var goblin = FindGoblin();
            var goblinHealth = goblin.GetComponent<Health>();
            var goblinStatuses = goblin.GetComponent<StatusSystem>();

            Teleport(player, goblin.transform.position - goblin.transform.forward * 8f);
            yield return null;
            targeting.SetTarget(goblin.transform);

            float startHp = goblinHealth.CurrentHealth;
            Assert.IsTrue(abilities.TryActivate("fireball"), "fireball did not fire");

            bool knockedBack = false;
            float deadline = Time.time + 3f;
            while (Time.time < deadline && goblinHealth.CurrentHealth >= startHp)
            {
                if (goblinHealth.IsBeingKnockedBack) knockedBack = true;
                yield return null;
            }

            Assert.Less(goblinHealth.CurrentHealth, startHp, "fireball never landed");
            Assert.IsFalse(knockedBack, "fireball knocked the goblin back — spells must not shove");
            Assert.IsTrue(goblinStatuses.Has("burn"), "fireball hit did not apply burn");
        }

        [UnityTest]
        public IEnumerator DashStunAbilityStunsEnemy()
        {
            var player = FindPlayer();
            var abilities = player.GetComponent<PlayerAbilities>();
            var targeting = player.GetComponent<TargetingSystem>();

            var goblin = FindGoblin();
            var goblinHealth = goblin.GetComponent<Health>();
            var goblinStatuses = goblin.GetComponent<StatusSystem>();

            Teleport(player, goblin.transform.position - goblin.transform.forward * 6f);
            yield return null;
            targeting.SetTarget(goblin.transform);

            float startHp = goblinHealth.CurrentHealth;
            Assert.IsTrue(abilities.TryActivate("blade_rush"),
                "blade_rush refused (weapon gate / range / cooldown?)");

            float deadline = Time.time + 2.5f;
            while (Time.time < deadline && !goblinStatuses.IsStunned)
                yield return null;

            Assert.IsTrue(goblinStatuses.IsStunned, "dash never stunned the goblin");
            Assert.Less(goblinHealth.CurrentHealth, startHp, "dash dealt no damage");
            Assert.Less(Vector3.Distance(player.transform.position, goblin.transform.position), 3.5f,
                "player did not dash to the target");
        }

        [UnityTest]
        public IEnumerator EngageOrderWalksToTargetAndAttacks()
        {
            var player = FindPlayer();
            var targeting = player.GetComponent<TargetingSystem>();
            var melee = player.GetComponent<PlayerMelee>();
            Assert.IsNotNull(melee, "player has no PlayerMelee");

            var goblin = FindGoblin();
            var goblinHealth = goblin.GetComponent<Health>();

            Teleport(player, goblin.transform.position - goblin.transform.forward * 8f);
            yield return null;

            // Simulate the right-click attack order (raw mouse input can't be
            // driven from a test; the tap handler routes to exactly this).
            targeting.SetTarget(goblin.transform);
            targeting.OnEngage.Invoke(goblin.transform);
            yield return null;

            Assert.IsTrue(melee.IsEngaged, "engage order did not register");

            float startHp = goblinHealth.CurrentHealth;
            float startDistance = Vector3.Distance(player.transform.position, goblin.transform.position);

            float deadline = Time.time + 8f;
            while (Time.time < deadline && goblinHealth.CurrentHealth >= startHp)
                yield return null;

            Assert.Less(goblinHealth.CurrentHealth, startHp,
                $"auto never connected (start distance {startDistance:F1}u)");
        }
    }
}
