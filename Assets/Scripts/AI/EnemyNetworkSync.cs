using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace AldenFall.AI
{
    /// <summary>
    /// Authority gate for networked enemies. The SERVER owns AI, navigation and
    /// combat; clients only render the replicated transform (NetworkTransform) and
    /// animation (NetworkAnimator). This mirrors how NetworkPlayerAvatar disables
    /// gameplay components on remote player avatars.
    ///
    /// Least-invasive by design: EnemyAI/BossRageMode stay plain MonoBehaviours and
    /// simply do not run on clients, no need to convert their large FSMs to
    /// NetworkBehaviour since they never issue Commands/Rpcs of their own.
    /// </summary>
    [RequireComponent(typeof(NetworkIdentity))]
    public class EnemyNetworkSync : NetworkBehaviour
    {
        public override void OnStartClient()
        {
            // Host is also a client but IS the server, leave its full simulation running.
            if (isServer) return;

            // Pure client: stop everything the server authoritatively simulates.
            SetEnabled<EnemyAI>(false);
            SetEnabled<BossRageMode>(false);

            // NavMeshAgent must be server-only or it fights the incoming NetworkTransform.
            var agent = GetComponent<NavMeshAgent>();
            if (agent != null) agent.enabled = false;
        }

        private void SetEnabled<T>(bool value) where T : Behaviour
        {
            var component = GetComponent<T>();
            if (component != null) component.enabled = value;
        }

        [Tooltip("Radius around the boss within which players count as 'fighting it' and see the rage cinematic.")]
        [SerializeField] private float engageRadius = 55f;

        /// <summary>Server (BossRageMode) → only players ACTIVELY AT THE BOSS: play the rage
        /// cinematic locally. Scoped by proximity so someone in town never sees it, while everyone
        /// fighting the boss (their whole party is right there) sees it in sync. A solo fighter
        /// just sees it themselves.</summary>
        [Server]
        public void ServerPlayRageCinematic()
        {
            float rangeSqr = engageRadius * engageRadius;
            Vector3 bossPos = transform.position;
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn.identity == null) continue;
                if ((conn.identity.transform.position - bossPos).sqrMagnitude <= rangeSqr)
                    TargetPlayRageCinematic(conn);
            }
        }

        [TargetRpc]
        private void TargetPlayRageCinematic(NetworkConnectionToClient target)
        {
            // BossRageMode is disabled on pure clients, but a disabled MonoBehaviour's methods
            // still execute — call its local cinematic builder directly.
            GetComponent<BossRageMode>()?.PlayRageCinematicLocal();
        }
    }
}
