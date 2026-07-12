using System.Collections;
using Mirror;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace AldenFall.AI
{
    /// <summary>
    /// Server-side safety net for enemy pathing.
    ///
    /// Enemies are simulated on the SERVER (see EnemyNetworkSync): their NavMeshAgents
    /// only move if a valid NavMesh exists on that peer. A built (Linux dedicated) server
    /// can easily ship WITHOUT a valid bake for the current world — the NavMeshSurface's
    /// baked NavMeshData may never have been rebuilt for the new geometry, or not included
    /// in the server build. When that happens <c>NavMeshAgent.SetDestination</c> silently
    /// no-ops, every enemy is frozen at its spawn on the server (and therefore on every
    /// client), and mobs that happen to spawn on top of a player still deal melee damage —
    /// which reads to the player as "a frozen enemy hitting me from range".
    ///
    /// This component lives on the arena root next to the build-time <see cref="NavMeshSurface"/>
    /// and RE-BAKES the NavMesh at runtime on the authoritative peer, so enemies always have
    /// a mesh to path on regardless of what the build shipped. Pure clients never bake — they
    /// only render the replicated transform and have their agents disabled.
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public class ServerNavMeshBaker : MonoBehaviour
    {
        [Tooltip("How long to wait for the NetworkManager to StartServer() (batchmode dedicated server) before deciding this peer's role.")]
        [SerializeField] private float serverWaitTimeout = 8f;

        private void Start() => StartCoroutine(RebakeWhenAuthoritative());

        private IEnumerator RebakeWhenAuthoritative()
        {
            // The dedicated server calls StartServer() from NetworkManager.Start() which may
            // land a frame or two after scene objects' Start(). Wait until this peer's role
            // is known (server active, or a client has connected) or the timeout elapses.
            float waited = 0f;
            while (waited < serverWaitTimeout && !NetworkServer.active && !NetworkClient.active)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            // Pure clients render server-replicated enemy transforms and have their agents
            // disabled by EnemyNetworkSync — they must NOT bake (wasteful, and a slightly
            // different local mesh would only fight the incoming NetworkTransform).
            if (NetworkClient.active && !NetworkServer.active)
                yield break;

            var surface = GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                Debug.LogWarning("ServerNavMeshBaker: no NavMeshSurface on the arena root — cannot rebake.");
                yield break;
            }

            surface.BuildNavMesh();
            Debug.Log($"ServerNavMeshBaker: runtime NavMesh (re)bake complete on the authoritative peer " +
                      $"(server={NetworkServer.active}). Enemies can now path.");
        }
    }
}
