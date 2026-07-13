using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using Mirror;

namespace AldenFall.AI
{
    /// <summary>
    /// Single-point spawner: keeps one enemy alive at a position, respawning it
    /// a delay after death. Used for the courtyard goblins; field camps use
    /// <see cref="MobCamp"/>.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0, 1, -35);
        [SerializeField] private float respawnDelay = 10f;
        [SerializeField] private bool respawnAfterDeath = true;

        [Header("Debug")]
        [SerializeField] private bool spawnOnStart = true;

        private GameObject currentEnemy;
        private bool isRespawning;

        private System.Collections.IEnumerator Start()
        {
            // Enemies are server-authoritative: only the server spawns and simulates them.
            // WAIT for the role instead of testing once, Start() runs at scene load, which
            // can precede StartServer()/StartHost() (host menu, batchmode boot ordering),
            // and a one-shot check leaves the world permanently empty.
            while (!NetworkServer.active && !NetworkClient.active)
                yield return null;
            if (!NetworkServer.active) yield break;   // pure client: the server owns spawning

            if (!NavMesh.SamplePosition(spawnPosition, out _, 5f, NavMesh.AllAreas))
            {
                Debug.LogWarning("EnemySpawner: No NavMesh at spawn position, bake the scene first.");
            }

            if (spawnOnStart && currentEnemy == null)
            {
                SpawnEnemy();
            }
        }

        public void ScheduleRespawn(float delay = -1)
        {
            if (isRespawning) return;
            StartCoroutine(RespawnAfterDelay(delay > 0 ? delay : respawnDelay));
        }

        private IEnumerator RespawnAfterDelay(float delay)
        {
            isRespawning = true;
            yield return new WaitForSeconds(delay);
            SpawnEnemy();
            isRespawning = false;
        }

        public void SpawnEnemy()
        {
            if (enemyPrefab == null)
            {
                Debug.LogWarning("EnemySpawner: no enemy prefab assigned.");
                return;
            }

            currentEnemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
            NetworkServer.Spawn(currentEnemy);   // replicate to all clients (prefab must be in spawnPrefabs)
            var health = currentEnemy.GetComponent<Combat.Health>();
            if (health != null)
            {
                health.OnDeath.AddListener(() =>
                {
                    currentEnemy = null;
                    if (respawnAfterDeath)
                        ScheduleRespawn(respawnDelay);
                });
            }
        }

        public void ForceReset()
        {
            if (!NetworkServer.active) return;
            if (currentEnemy != null)
                NetworkServer.Destroy(currentEnemy);
            currentEnemy = null;
            isRespawning = false;
            StopAllCoroutines();
            SpawnEnemy();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(spawnPosition, 1f);
        }
    }
}
