using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using AldenFall.AI;
using AldenFall.Combat;
using AldenFall.Player;

namespace AldenFall.Tests
{
    /// <summary>
    /// End-to-end smoke tests against the real EmberfallDemo scene.
    /// These run the actual gameplay loop in playmode: real physics, real NavMesh,
    /// real components — verifying the exact bugs that were reworked (projectile
    /// hits, water enter/exit, post-swim drift, enemy aggro).
    /// </summary>
    public class EmberfallSmokeTests
    {
        private const string SceneName = "EmberfallDemo";

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;              // scene activation
            yield return new WaitForSeconds(0.5f); // Start() + NavMesh warps
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
        /// FindFirstObjectByType returns an ARBITRARY enemy — after the JSON
        /// roster it can be the boss, whose Defence shrugs off small test hits.
        /// Kill-math tests must target an actual goblin.
        /// </summary>
        private static EnemyAI FindGoblin()
        {
            foreach (var enemy in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                var identity = enemy.GetComponent<AI.MobIdentity>();
                if (identity != null && identity.DisplayName.Contains("Goblin"))
                    return enemy;
            }
            Assert.Fail("no goblin in scene");
            return null;
        }

        // ---------- Scene integrity ----------

        [UnityTest]
        public IEnumerator SceneHasCoreActors()
        {
            yield return null;

            Assert.IsNotNull(Object.FindFirstObjectByType<PlayerController>(), "player missing");
            Assert.IsNotNull(Camera.main, "main camera missing");
            Assert.IsNotNull(Object.FindFirstObjectByType<EnemySpawner>(), "spawner missing");

            var enemies = Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(enemies.Length, 3, "expected at least 3 goblins");

            foreach (var enemy in enemies)
                Assert.IsNotNull(enemy.GetComponent<Health>(), $"{enemy.name} has no Health");
        }

        // ---------- Combat: the swept-cast projectile actually lands ----------

        [UnityTest]
        public IEnumerator FireballDamagesGoblin()
        {
            var player = FindPlayer();

            // Use the goblin nearest the open plaza so props/terrace never occlude
            // the shot — this tests projectile mechanics, not level geometry.
            EnemyAI enemy = null;
            float best = float.MaxValue;
            foreach (var candidate in Object.FindObjectsByType<EnemyAI>(FindObjectsSortMode.None))
            {
                float d = Vector3.Distance(candidate.transform.position, new Vector3(0, 0, 8f));
                if (d < best) { best = d; enemy = candidate; }
            }
            Assert.IsNotNull(enemy, "no goblin found");

            var enemyHealth = enemy.GetComponent<Health>();
            float healthBefore = enemyHealth.CurrentHealth;

            // Fire from open ground between the goblin and the arena center.
            Vector3 toCenter = (Vector3.zero - enemy.transform.position).normalized;
            Vector3 origin = enemy.transform.position + toCenter * 6f + Vector3.up * 1.2f;
            Vector3 direction = (enemy.transform.position + Vector3.up - origin).normalized;

            var fireballObj = new GameObject("TestFireball");
            fireballObj.transform.position = origin;
            var projectile = fireballObj.AddComponent<Projectile>();
            projectile.SetDamage(25f);
            projectile.SetSpeed(18f);
            projectile.Initialize(direction, player.gameObject, enemy.transform);

            float deadline = Time.time + 3f;
            while (Time.time < deadline && enemyHealth.CurrentHealth >= healthBefore)
                yield return null;

            Assert.Less(enemyHealth.CurrentHealth, healthBefore,
                "fireball never damaged the goblin (swept-cast hit failed)");
        }

        [UnityTest]
        public IEnumerator FireballKillsAndGoblinDies()
        {
            var player = FindPlayer();
            var enemy = FindGoblin();
            var enemyHealth = enemy.GetComponent<Health>();

            enemyHealth.TakeDamage(enemyHealth.CurrentHealth - 1f, player.gameObject);
            Assert.IsTrue(enemyHealth.IsAlive, "goblin should survive at 1hp");

            enemyHealth.TakeDamage(10f, player.gameObject);
            Assert.IsTrue(enemyHealth.IsDead, "goblin should be dead");

            yield return null;
            Assert.AreEqual(EnemyState.Dead, enemy.CurrentState, "AI did not enter Dead state");
        }

        // ---------- Water: enter, float, exit, and no post-swim drift ----------

        [UnityTest]
        public IEnumerator SwimEnterFloatAndExitWithoutDrift()
        {
            var player = FindPlayer();

            // Pond center in Emberfall (see ArenaBuilder.PondRect): (-24, z 0)
            Teleport(player, new Vector3(-24f, 0.6f, 0f));

            // Motion trace: sample every 0.1s to see the fall/settle profile
            var velocityField = typeof(PlayerController).GetField("velocity",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var trace = new System.Text.StringBuilder();
            for (int i = 0; i < 15; i++)
            {
                yield return new WaitForSeconds(0.1f);
                Vector3 vel = (Vector3)velocityField.GetValue(player);
                trace.AppendLine($"t={i * 0.1f:F1} y={player.transform.position.y:F2} " +
                    $"velY={vel.y:F2} inWater={player.IsInWater}");
            }
            Debug.Log("MOTION TRACE:\n" + trace);

            if (!player.IsInWater)
            {
                // Diagnostics: where is the player, and is the volume detectable?
                var volume = GameObject.Find("WaterVolume");
                string volInfo = volume == null
                    ? "WaterVolume NOT FOUND"
                    : $"WaterVolume layer={volume.layer} " +
                      $"trigger={volume.GetComponent<BoxCollider>()?.isTrigger} " +
                      $"bounds={volume.GetComponent<BoxCollider>()?.bounds}";
                int waterLayer = LayerMask.NameToLayer("Water");
                var manual = Physics.OverlapBox(new Vector3(-24f, -1.4f, 0f),
                    new Vector3(1f, 1f, 1f), Quaternion.identity,
                    waterLayer >= 0 ? (1 << waterLayer) : ~0,
                    QueryTriggerInteraction.Collide);
                // What is the player standing on?
                var below = Physics.RaycastAll(
                    player.transform.position + Vector3.up * 0.5f, Vector3.down, 6f,
                    ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(below, (a, b) => a.distance.CompareTo(b.distance));
                string standing = string.Join("; ", System.Array.ConvertAll(below,
                    h => $"{h.collider.name}@y={h.point.y:F2}(layer {h.collider.gameObject.layer})"));

                Assert.Fail(
                    $"player did not enter swim state. playerPos={player.transform.position} " +
                    $"| {volInfo} | manual overlap hits at pond center={manual.Length} " +
                    $"| waterLayerIndex={waterLayer} | UNDER PLAYER: {standing}");
            }

            // Must not sink to the floor: buoyancy holds a float line
            Assert.Greater(player.transform.position.y, -2.4f,
                "player sank to the pond floor while idle (buoyancy failed)");

            // Teleport onto dry land and confirm clean exit
            Teleport(player, new Vector3(0f, 0.6f, -30f));
            yield return new WaitForSeconds(0.6f);

            Assert.IsFalse(player.IsInWater, "player still swimming after leaving water");

            // No residual swim momentum: position must be stable with zero input
            Vector3 before = player.transform.position;
            yield return new WaitForSeconds(1f);
            Vector3 drift = player.transform.position - before;
            drift.y = 0; // ignore gravity settling
            Assert.Less(drift.magnitude, 0.05f,
                $"player drifted {drift.magnitude:F3}u after swimming (residual velocity bug)");
        }

        // ---------- AI: goblin aggros a nearby player ----------

        [UnityTest]
        public IEnumerator GoblinAggrosNearbyPlayer()
        {
            var player = FindPlayer();
            var enemy = Object.FindFirstObjectByType<EnemyAI>();

            Teleport(player, enemy.transform.position + new Vector3(4f, 0.1f, 0f));

            float deadline = Time.time + 3f;
            while (Time.time < deadline
                   && enemy.CurrentState != EnemyState.Chase
                   && enemy.CurrentState != EnemyState.Attack
                   && enemy.CurrentState != EnemyState.JumpAttack
                   && enemy.CurrentState != EnemyState.ChargeAttack)
                yield return null;

            Assert.That(enemy.CurrentState,
                Is.EqualTo(EnemyState.Chase)
                  .Or.EqualTo(EnemyState.Attack)
                  .Or.EqualTo(EnemyState.JumpAttack)
                  .Or.EqualTo(EnemyState.ChargeAttack),
                $"goblin never aggroed (state stayed {enemy.CurrentState})");
        }
    }
}
