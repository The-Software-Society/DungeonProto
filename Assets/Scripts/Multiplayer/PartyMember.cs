using Mirror;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    /// <summary>
    /// Per-player party endpoint (lives on the player prefab next to PlayerCombat).
    /// The registry itself is the static server-side <see cref="PartySystem"/>; this
    /// component is the Command/TargetRpc bridge plus the local player's keys:
    ///   P        — invite the nearest other player within 8m
    ///   Shift+P  — leave your party
    ///   Y / N    — accept / decline a pending invite
    /// PartyHUD (same GameObject) renders the roster + prompts off the events here.
    /// </summary>
    public class PartyMember : NetworkBehaviour
    {
        public const float InviteRange = 8f;

        // ---- client-side state (owner only; fed by TargetRpcs) ----
        public string[] MemberNames { get; private set; } = System.Array.Empty<string>();
        public uint[] MemberNetIds { get; private set; } = System.Array.Empty<uint>();
        public bool InParty => MemberNetIds.Length > 0; // roster includes self when partied
        public uint PendingInviterNetId { get; private set; }
        public string PendingInviterName { get; private set; }
        public bool HasPendingInvite => PendingInviterNetId != 0;

        /// <summary>Roster or pending-invite state changed (HUD redraw).</summary>
        public event System.Action Changed;
        /// <summary>Transient HUD toast ("invite sent", "invite declined", ...).</summary>
        public event System.Action<string> Notice;

        /// <summary>Client-side: is that netId in my current party roster?</summary>
        public bool IsPartiedWith(uint otherNetId)
        {
            for (int i = 0; i < MemberNetIds.Length; i++)
                if (MemberNetIds[i] == otherNetId) return true;
            return false;
        }

        private void Update()
        {
            if (!isLocalPlayer || UI.InputFocusGuard.IsTyping) return;

            if (Input.GetKeyDown(KeyCode.P))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift)
                {
                    if (InParty)
                    {
                        CmdLeave();
                        Notice?.Invoke("You left the party.");
                    }
                }
                else
                {
                    var target = FindNearestOtherPlayer(InviteRange);
                    if (target != null)
                    {
                        CmdInvite(target.netId);
                        Notice?.Invoke("Party invite sent.");
                    }
                    else
                    {
                        Notice?.Invoke("No player within 8m to invite.");
                    }
                }
            }

            if (HasPendingInvite)
            {
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    CmdAccept(PendingInviterNetId);
                    ClearInvite();
                }
                else if (Input.GetKeyDown(KeyCode.N))
                {
                    Notice?.Invoke("Invite declined.");
                    ClearInvite();
                }
            }
        }

        /// <summary>
        /// Nearest other player avatar within range. Runs only on a keypress —
        /// NetworkClient.spawned is a dictionary walk, not a scene search.
        /// </summary>
        public NetworkIdentity FindNearestOtherPlayer(float range)
        {
            NetworkIdentity best = null;
            float bestSqr = range * range;
            foreach (var kvp in NetworkClient.spawned)
            {
                var identity = kvp.Value;
                if (identity == null || identity.netId == netId) continue;
                if (identity.GetComponent<NetworkPlayerAvatar>() == null) continue;
                float sqr = (identity.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = identity;
                }
            }
            return best;
        }

        // ---------------- commands (run on server) ----------------

        [Command]
        private void CmdInvite(uint targetNetId)
        {
            if (targetNetId == netId) return;
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out var targetIdentity) || targetIdentity == null) return;
            var target = targetIdentity.GetComponent<PartyMember>();
            if (target == null) return;

            // Generous server-side range re-check (client saw them in range; allow lag drift).
            float maxRange = InviteRange + 6f;
            if ((targetIdentity.transform.position - transform.position).sqrMagnitude > maxRange * maxRange) return;

            if (PartySystem.SameParty(netId, targetNetId)) return;
            if (!PartySystem.ServerRegisterInvite(netId, targetNetId)) return;

            target.TargetInviteReceived(PartySystem.DisplayNameOf(netId), netId);
        }

        [Command]
        private void CmdAccept(uint inviterNetId)
        {
            PartySystem.ServerAcceptInvite(netId, inviterNetId);
        }

        [Command]
        private void CmdLeave()
        {
            PartySystem.ServerRemove(netId);
        }

        public override void OnStopServer()
        {
            // Disconnect = implicit leave; PartySystem notifies the remaining members.
            PartySystem.ServerRemove(netId);
        }

        // ---------------- target rpcs (run on owning client) ----------------

        [TargetRpc]
        public void TargetInviteReceived(string inviterName, uint inviterNetId)
        {
            PendingInviterName = inviterName;
            PendingInviterNetId = inviterNetId;
            Changed?.Invoke();
        }

        [TargetRpc]
        public void TargetPartyChanged(string[] memberNames, uint[] memberNetIds)
        {
            MemberNames = memberNames ?? System.Array.Empty<string>();
            MemberNetIds = memberNetIds ?? System.Array.Empty<uint>();
            Changed?.Invoke();
        }

        private void ClearInvite()
        {
            PendingInviterNetId = 0;
            PendingInviterName = null;
            Changed?.Invoke();
        }
    }
}
