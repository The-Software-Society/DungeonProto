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

            // Only the server simulates enemies, so only the server ever bakes. A plain
            // "client-active" check is not enough: a client that sits on the connect screen
            // past the timeout has NetworkClient.active == false and would bake anyway
            // (a wasteful multi-second hitch right at scene load).
            if (!NetworkServer.active)
                yield break;

            var surface = GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                Debug.LogWarning("ServerNavMeshBaker: no NavMeshSurface on the arena root — cannot rebake.");
                yield break;
            }

            surface.BuildNavMesh();

            var triangulation = NavMesh.CalculateTriangulation();
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
            {
                Debug.LogError("ServerNavMeshBaker: runtime bake produced an EMPTY NavMesh — enemies cannot path. " +
                               "Check that world geometry is on the Ground layer and present in the server build.");
                yield break;
            }

            // Enemies spawned before the bake finished failed their initial Warp and sit
            // frozen off-mesh; nudge every agent back onto the fresh mesh.
            int rewarped = 0;
            foreach (var agent in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
            {
                if (agent.isOnNavMesh) continue;
                if (NavMesh.SamplePosition(agent.transform.position, out NavMeshHit hit, 6f, NavMesh.AllAreas) &&
                    agent.Warp(hit.position))
                    rewarped++;
            }

            Debug.Log($"ServerNavMeshBaker: runtime NavMesh (re)bake complete on the server — " +
                      $"{triangulation.vertices.Length} verts, {rewarped} agent(s) re-warped. Enemies can now path.");
        }
    }
}
