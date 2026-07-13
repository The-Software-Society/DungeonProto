using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Mirror;

namespace AldenFall.World
{
    /// <summary>
    /// A scripted wave-defense set-piece (the Q10 orc attack on Emberfall Gate).
    /// Arms when the LOCAL player is near with the required quest active, then the
    /// server spawns timed waves of a mob. When the waves are cleared (or time
    /// out), every client is told: the local QuestLog advances its DefendEvent
    /// objective and the completion cutscene (Garron's death and return) plays.
    /// Server-authoritative spawning (mirrors MobCamp); the mob prefab is resolved
    /// at runtime from the NetworkManager spawn list so it needs no build-time wiring.
    /// </summary>
    public class DefendZone : NetworkBehaviour
    {
        [SerializeField] private string eventId = "gate_defense";
        [SerializeField] private string requiredQuestId = "q_gate";
        [SerializeField] private string mobId = "orc_brute";
        [SerializeField] private int waves = 3;
        [SerializeField] private int perWave = 2;
        [SerializeField] private float spawnRadius = 11f;
        [SerializeField] private float triggerRadius = 20f;
        [SerializeField] private string completeCutscene = "q_gate_garron";

        public string EventId { get => eventId; set => eventId = value; }
        public string RequiredQuestId { get => requiredQuestId; set => requiredQuestId = value; }
        public string MobId { get => mobId; set => mobId = value; }
        public string CompleteCutscene { get => completeCutscene; set => completeCutscene = value; }

        /// <summary>Fired on every client when a defense event clears (arg = eventId).</summary>
        public static event System.Action<string> OnDefendComplete;

        private bool serverStarted, armSent;
        private readonly List<GameObject> alive = new List<GameObject>();

        // ---- client: arm the event when the local player is near with the quest active ----
        private void Update()
        {
            if (armSent || !NetworkClient.active) return;
            var p = NetworkClient.localPlayer;
            if (p == null) return;
            var log = p.GetComponent<Quests.QuestLog>();
            if (log == null || !log.IsActive(requiredQuestId)) return;
            if ((p.transform.position - transform.position).sqrMagnitude > triggerRadius * triggerRadius) return;
            armSent = true;
            CmdBegin();
        }

        [Command(requiresAuthority = false)]
        private void CmdBegin()
        {
            if (serverStarted) return;
            serverStarted = true;
            StartCoroutine(RunWaves());
        }

        private IEnumerator RunWaves()
        {
            var prefab = ResolveMobPrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"DefendZone {eventId}: mob prefab '{mobId}' not found in spawnPrefabs");
                RpcCleared();   // do not soft-lock the quest if the prefab is missing
                yield break;
            }

            for (int w = 0; w < waves; w++)
            {
                for (int i = 0; i < perWave; i++) SpawnOne(prefab, w * perWave + i);

                float t = 0f;
                while (AliveCount() > 0 && t < 50f) { t += 1f; yield return new WaitForSeconds(1f); }
            }
            RpcCleared();
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

        [ClientRpc]
        private void RpcCleared()
        {
            OnDefendComplete?.Invoke(eventId);
            if (!string.IsNullOrEmpty(completeCutscene))
                Cinematics.CutsceneDirector.Instance?.Play(completeCutscene);
        }
    }
}
