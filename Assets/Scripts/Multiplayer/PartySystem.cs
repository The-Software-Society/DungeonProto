using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace AldenFall.Multiplayer
{
    /// <summary>
    /// SERVER-authoritative party registry. Pure static server-side state, the world
    /// generator owns scene building, so there is no scene singleton to place; instead
    /// every player carries a <see cref="PartyMember"/> NetworkBehaviour whose Commands
    /// mutate this registry and whose TargetRpcs relay roster changes back to owners.
    ///
    /// All mutators no-op unless NetworkServer.active, so a pure client can never
    /// diverge from the server's view. Call <see cref="ServerReset"/> when the server
    /// stops so a host stop/start in one process doesn't leak stale parties.
    /// </summary>
    public static class PartySystem
    {
        public const int MaxPartySize = 4;
        private const double InviteLifetime = 30.0; // seconds before an unanswered invite dies

        private struct PendingInvite
        {
            public uint inviter;
            public double expires; // NetworkTime.time deadline
        }

        private static readonly Dictionary<uint, uint> memberToParty = new Dictionary<uint, uint>();
        private static readonly Dictionary<uint, List<uint>> parties = new Dictionary<uint, List<uint>>();
        private static readonly Dictionary<uint, PendingInvite> pendingInvites = new Dictionary<uint, PendingInvite>(); // key = invitee
        private static uint nextPartyId = 1;

        /// <summary>Clear everything (server stop, statics outlive the network session).</summary>
        public static void ServerReset()
        {
            memberToParty.Clear();
            parties.Clear();
            pendingInvites.Clear();
            nextPartyId = 1;
        }

        /// <summary>
        /// Record an invite from inviter → target. Fails when the target is already
        /// partied or the inviter's party is full. A newer invite overwrites an older one.
        /// </summary>
        public static bool ServerRegisterInvite(uint inviterNetId, uint targetNetId)
        {
            if (!NetworkServer.active) return false;
            if (inviterNetId == 0 || targetNetId == 0 || inviterNetId == targetNetId) return false;
            if (memberToParty.ContainsKey(targetNetId)) return false; // already in a party
            if (memberToParty.TryGetValue(inviterNetId, out uint partyId)
                && parties[partyId].Count >= MaxPartySize) return false;

            pendingInvites[targetNetId] = new PendingInvite
            {
                inviter = inviterNetId,
                expires = NetworkTime.time + InviteLifetime
            };
            return true;
        }

        /// <summary>
        /// Consume the target's pending invite and join them to the inviter's party
        /// (creating a fresh party of two when the inviter was solo).
        /// </summary>
        public static bool ServerAcceptInvite(uint targetNetId, uint inviterNetId)
        {
            if (!NetworkServer.active) return false;
            if (!pendingInvites.TryGetValue(targetNetId, out var invite)) return false;
            pendingInvites.Remove(targetNetId);
            if (invite.inviter != inviterNetId || NetworkTime.time > invite.expires) return false;
            if (memberToParty.ContainsKey(targetNetId)) return false;
            if (!NetworkServer.spawned.ContainsKey(inviterNetId)) return false; // inviter left

            if (!memberToParty.TryGetValue(inviterNetId, out uint partyId))
            {
                partyId = nextPartyId++;
                parties[partyId] = new List<uint> { inviterNetId };
                memberToParty[inviterNetId] = partyId;
            }

            var members = parties[partyId];
            if (members.Count >= MaxPartySize) return false;

            members.Add(targetNetId);
            memberToParty[targetNetId] = partyId;
            BroadcastRoster(partyId);
            return true;
        }

        /// <summary>
        /// Remove a player from their party (leave key, or disconnect via
        /// PartyMember.OnStopServer). A party of one disbands.
        /// </summary>
        public static void ServerRemove(uint netId)
        {
            if (!NetworkServer.active || netId == 0) return;

            pendingInvites.Remove(netId);
            // Drop any invites this player had outstanding toward others.
            var staleTargets = new List<uint>();
            foreach (var kvp in pendingInvites)
                if (kvp.Value.inviter == netId) staleTargets.Add(kvp.Key);
            foreach (uint target in staleTargets)
                pendingInvites.Remove(target);

            if (!memberToParty.TryGetValue(netId, out uint partyId)) return;

            memberToParty.Remove(netId);
            var members = parties[partyId];
            members.Remove(netId);
            SendRoster(netId, System.Array.Empty<string>(), System.Array.Empty<uint>()); // leaver is solo now

            if (members.Count <= 1)
            {
                // Last member standing: disband entirely.
                foreach (uint remaining in members)
                {
                    memberToParty.Remove(remaining);
                    SendRoster(remaining, System.Array.Empty<string>(), System.Array.Empty<uint>());
                }
                parties.Remove(partyId);
            }
            else
            {
                BroadcastRoster(partyId);
            }
        }

        public static bool SameParty(uint a, uint b)
        {
            return memberToParty.TryGetValue(a, out uint pa)
                && memberToParty.TryGetValue(b, out uint pb)
                && pa == pb;
        }

        /// <summary>
        /// Copy of the member netId list (including the player) for ownership snapshots
        /// (loot). Null when the player is solo.
        /// </summary>
        public static List<uint> SnapshotMemberIds(uint netId)
        {
            if (!NetworkServer.active) return null;
            if (!memberToParty.TryGetValue(netId, out uint partyId)) return null;
            return new List<uint>(parties[partyId]);
        }

        /// <summary>
        /// SERVER: run an action on every OTHER live party member's player object -
        /// the shared XP / gold / quest-credit fan-out. No-ops for solo players.
        /// </summary>
        public static void ForEachOtherMember(GameObject player, System.Action<GameObject> action)
        {
            if (!NetworkServer.active || player == null || action == null) return;
            var identity = player.GetComponent<NetworkIdentity>();
            if (identity == null) return;
            if (!memberToParty.TryGetValue(identity.netId, out uint partyId)) return;

            var members = parties[partyId];
            for (int i = 0; i < members.Count; i++)
            {
                uint memberId = members[i];
                if (memberId == identity.netId) continue;
                if (NetworkServer.spawned.TryGetValue(memberId, out var member) && member != null)
                    action(member.gameObject);
            }
        }

        /// <summary>SERVER: resolve a player's display name for invite/duel prompts.</summary>
        public static string DisplayNameOf(uint netId)
        {
            if (NetworkServer.spawned.TryGetValue(netId, out var identity) && identity != null)
            {
                var avatar = identity.GetComponent<NetworkPlayerAvatar>();
                if (avatar != null && !string.IsNullOrWhiteSpace(avatar.DisplayName))
                    return avatar.DisplayName;
            }
            return "Player";
        }

        // ---------------- roster relay ----------------

        private static void BroadcastRoster(uint partyId)
        {
            if (!parties.TryGetValue(partyId, out var members)) return;

            // Prune members whose player object is gone before telling anyone.
            members.RemoveAll(id => !NetworkServer.spawned.ContainsKey(id));

            var names = new string[members.Count];
            var ids = new uint[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                ids[i] = members[i];
                names[i] = DisplayNameOf(members[i]);
            }

            foreach (uint memberId in members)
                SendRoster(memberId, names, ids);
        }

        private static void SendRoster(uint netId, string[] names, uint[] ids)
        {
            if (!NetworkServer.spawned.TryGetValue(netId, out var identity) || identity == null) return;
            if (identity.connectionToClient == null) return; // disconnecting, nobody to tell
            identity.GetComponent<PartyMember>()?.TargetPartyChanged(names, ids);
        }
    }
}
