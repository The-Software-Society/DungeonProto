using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

namespace AldenFall.AI
{
    /// <summary>
    /// Maintains a population of one mob type around a camp point: spawns
    /// <see cref="population"/> mobs on start and respawns each one a delay
    /// after it dies. Positions sample the NavMesh inside the camp radius.
    /// </summary>
    public class MobCamp : MonoBehaviour
    {
        [Header("Camp")]
        [SerializeField] private GameObject mobPrefab;
        [SerializeField] private int population = 3;
        [SerializeField] private float radius = 9f;
        [SerializeField] private float respawnDelay = 15f;
        [SerializeField] private bool respawnAfterDeath = true;

        private readonly List<GameObject> alive = new List<GameObject>();

        private IEnumerator Start()
        {
            // Server-authoritative: only the server populates the camp (host counts).
            // WAIT for the role instead of testing once, Start() runs at scene load,
            // which can precede StartServer()/StartHost() (host menu, batchmode boot
            // ordering), and a one-shot check leaves the world permanently empty.
            while (!NetworkServer.active && !NetworkClient.active)
                yield return null;
            if (!NetworkServer.active) yield break;   // pure client: the server owns spawning

            for (int i = 0; i < population; i++)
                SpawnOne();
        }

        private void SpawnOne()
        {
            if (mobPrefab == null) return;

            Vector3 pos = transform.position;
            int index = alive.Count;
            float angle = population <= 1 ? 0f : (index / (float)population) * Mathf.PI * 2f;
            float ring = population <= 1 ? 0f : radius * (0.35f + 0.15f * (index % 3));
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
            Vector3 candidate = pos + new Vector3(offset.x, 0, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                candidate = hit.position;

            var mob = Instantiate(mobPrefab, candidate + Vector3.up * 0.1f,
                Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            NetworkServer.Spawn(mob);   // replicate to all clients (prefab must be in spawnPrefabs)
            alive.Add(mob);

            var health = mob.GetComponent<Combat.Health>();
            if (health != null && respawnAfterDeath)
                health.OnDeath.AddListener(() => StartCoroutine(RespawnAfter(mob)));
        }

        private IEnumerator RespawnAfter(GameObject dead)
        {
            alive.Remove(dead);
            yield return new WaitForSeconds(respawnDelay);
            SpawnOne();
        }

        public void ForceReset()
        {
            if (!NetworkServer.active) return;
            StopAllCoroutines();
            foreach (var mob in alive)
                if (mob != null) NetworkServer.Destroy(mob);
            alive.Clear();

            for (int i = 0; i < population; i++)
                SpawnOne();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
