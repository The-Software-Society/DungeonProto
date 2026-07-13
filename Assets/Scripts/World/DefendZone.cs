using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

namespace AldenFall.World
{
    /// <summary>
    /// A scripted wave-defense set-piece (the Q10 orc attack on Emberfall Gate).
    /// Plain MonoBehaviour, server-authoritative like MobCamp (NOT a scene network
    /// object): on the server it waits for a player to approach with the required
    /// quest active, spawns timed waves of a mob (prefab resolved from the spawn
    /// list), and when they are cleared fires a static event so the local QuestLog
    /// advances its DefendEvent objective and the completion cutscene plays.
    /// (Host mode reads the local player's quest state; pure-client triggering on a
    /// dedicated server is a Phase-13 multiplayer-validation item.)
    /// </summary>
    public class DefendZone : MonoBehaviour
    {
        [SerializeField] private string eventId = "gate_defense";
        [SerializeField] private string requiredQuestId = "q_gate";
        [SerializeField] private string mobId = "orc_brute";
        [SerializeField] private int waves = 3;
        [SerializeField] private int perWave = 2;
        [SerializeField] private float spawnRadius = 11f;
        [SerializeField] private float triggerRadius = 22f;
        [SerializeField] private string completeCutscene = "q_gate_garron";

        public string EventId { get => eventId; set => eventId = value; }
        public string RequiredQuestId { get => requiredQuestId; set => requiredQuestId = value; }
        public string MobId { get => mobId; set => mobId = value; }
        public string CompleteCutscene { get => completeCutscene; set => completeCutscene = value; }

        /// <summary>Fired when a defense event clears (arg = eventId). Local QuestLog listens.</summary>
        public static event System.Action<string> OnDefendComplete;

        private readonly List<GameObject> alive = new List<GameObject>();
        private bool started;

        private IEnumerator Start()
        {
            // Server owns the set-piece (host counts). Wait for the role like MobCamp.
            while (!NetworkServer.active && !NetworkClient.active)
                yield return null;
            if (!NetworkServer.active) yield break;

            while (!started)
            {
                if (PlayerReady()) { started = true; StartCoroutine(RunWaves()); }
                yield return new WaitForSeconds(0.5f);
            }
        }

        private bool PlayerReady()
        {
            foreach (var pc in FindObjectsByType<Player.PlayerController>(FindObjectsSortMode.None))
            {
                if ((pc.transform.position - transform.position).sqrMagnitude > triggerRadius * triggerRadius) continue;
                var log = pc.GetComponent<Quests.QuestLog>();
                if (log != null && log.IsActive(requiredQuestId)) return true;
            }
            return false;
        }

        private IEnumerator RunWaves()
        {
            var prefab = ResolveMobPrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"DefendZone {eventId}: mob '{mobId}' not in spawnPrefabs");
                Complete();   // never soft-lock the quest
                yield break;
            }

            for (int w = 0; w < waves; w++)
            {
                for (int i = 0; i < perWave; i++) SpawnOne(prefab, w * perWave + i);

                float t = 0f;
                while (AliveCount() > 0 && t < 50f) { t += 1f; yield return new WaitForSeconds(1f); }
            }
            Complete();
        }

        private void Complete()
        {
            OnDefendComplete?.Invoke(eventId);
            if (!string.IsNullOrEmpty(completeCutscene))
                Cinematics.CutsceneDirector.Instance?.Play(completeCutscene);
        }

        private void SpawnOne(GameObject prefab, int index)
        {
            float angle = (index / (float)Mathf.Max(1, perWave * waves)) * Mathf.PI * 2f;
            Vector3 candidate = transform.position +
                new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 8f, NavMesh.AllAreas))
                candidate = hit.position;

            var mob = Instantiate(prefab, candidate + Vector3.up * 0.1f,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            NetworkServer.Spawn(mob);
            alive.Add(mob);
        }

        private int AliveCount()
        {
            alive.RemoveAll(m => m == null);
            return alive.Count;
        }

        private GameObject ResolveMobPrefab()
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return null;
            foreach (var p in nm.spawnPrefabs)
                if (p != null && p.name == mobId) return p;
            return null;
        }
    }
}
