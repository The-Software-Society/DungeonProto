using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Per-player duel endpoint (player prefab, next to PartyMember). Duels are
    /// PAIRWISE PvP: the server records a mutual opponent pair and
    /// <see cref="Health.TakeDamage"/> lets exactly that pair hurt each other while the
    /// global <see cref="CombatRules.PvpEnabled"/> stays off. A duel can never kill:
    /// the losing blow is clamped at 1 HP server-side, the duel ends, and both
    /// fighters are restored to full a few seconds later.
    ///
    /// Keys (local player): O — challenge the nearest player within 8m;
    /// Y / N — accept / decline (party invites get Y/N priority).
    ///
    /// The opponent is a SyncVar so the CLIENT also knows its own pair — projectiles
    /// are client-side visuals that normally fly through allies, and they need to know
    /// the duel opponent is a legitimate target.
    /// </summary>
    public class DuelMember : NetworkBehaviour
    {
        public const float RequestRange = 8f;
        private const double RequestLifetime = 30.0; // unanswered challenges die
        private const float RestoreDelay = 3f;       // both back to full HP after the duel

        [SyncVar(hook = nameof(OnOpponentSync))] private uint opponentNetId; // 0 = not dueling

        // SERVER-only: pending challenges keyed by the challenged player's netId.
        private static readonly Dictionary<uint, PendingRequest> pendingRequests = new Dictionary<uint, PendingRequest>();

        private struct PendingRequest
        {
            public uint challenger;
            public double expires;
        }

        // ---- client-side pending state (owner only) ----
        public uint PendingChallengerNetId { get; private set; }
        public string PendingChallengerName { get; private set; }
        public bool HasPendingRequest => PendingChallengerNetId != 0;

        public bool IsDueling => opponentNetId != 0;
        public bool IsDuelingWith(uint otherNetId) => otherNetId != 0 && opponentNetId == otherNetId;

        /// <summary>Duel or pending-request state changed (HUD redraw).</summary>
        public event System.Action Changed;
        /// <summary>Transient HUD toast ("challenge sent", "You won the duel!", ...).</summary>
        public event System.Action<string> Notice;

        private Multiplayer.PartyMember partyMember; // Y/N priority check

        /// <summary>Clear server-side statics on server stop (host stop/start in-process).</summary>
        public static void ServerReset() => pendingRequests.Clear();

        /// <summary>
        /// True when these two players are BOTH locked into a duel with each other —
        /// the exact pair Health.TakeDamage lets through the friendly-fire guard.
        /// </summary>
        public static bool IsActiveDuelPair(GameObject a, GameObject b)
        {
            if (a == null || b == null) return false;
            var da = a.GetComponent<DuelMember>();
            var db = b.GetComponent<DuelMember>();
            return da != null && db != null
                && da.opponentNetId != 0
                && da.opponentNetId == db.netId
                && db.opponentNetId == da.netId;
        }

        private void Awake()
        {
            partyMember = GetComponent<Multiplayer.PartyMember>();
        }

        private void Update()
        {
            if (!isLocalPlayer || UI.InputFocusGuard.IsTyping) return;

            if (Input.GetKeyDown(KeyCode.O) && !IsDueling)
            {
                var target = FindNearestOtherPlayer();
                if (target != null)
                {
                    CmdRequestDuel(target.netId);
                    Notice?.Invoke("Duel challenge sent.");
                }
                else
                {
                    Notice?.Invoke("No player within 8m to challenge.");
                }
            }

            // A pending PARTY invite owns Y/N first — never double-consume the keys.
            if (partyMember == null) partyMember = GetComponent<Multiplayer.PartyMember>();
            if (partyMember != null && partyMember.HasPendingInvite) return;

            if (HasPendingRequest)
            {
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    CmdAcceptDuel(PendingChallengerNetId);
                    ClearRequest();
                }
                else if (Input.GetKeyDown(KeyCode.N))
                {
                    Notice?.Invoke("Duel declined.");
                    ClearRequest();
                }
            }
        }

        private NetworkIdentity FindNearestOtherPlayer()
        {
            // Reuse PartyMember's keypress-only spawned-dictionary walk when present.
            if (partyMember != null)
                return partyMember.FindNearestOtherPlayer(RequestRange);

            NetworkIdentity best = null;
            float bestSqr = RequestRange * RequestRange;
            foreach (var kvp in NetworkClient.spawned)
            {
                var identity = kvp.Value;
                if (identity == null || identity.netId == netId) continue;
                if (identity.GetComponent<Multiplayer.NetworkPlayerAvatar>() == null) continue;
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
        private void CmdRequestDuel(uint targetNetId)
        {
            if (targetNetId == netId || IsDueling) return;
            if (!NetworkServer.spawned.TryGetValue(targetNetId, out var targetIdentity) || targetIdentity == null) return;
            var target = targetIdentity.GetComponent<DuelMember>();
            if (target == null || target.IsDueling) return;

            float maxRange = RequestRange + 6f;
            if ((targetIdentity.transform.position - transform.position).sqrMagnitude > maxRange * maxRange) return;

            pendingRequests[targetNetId] = new PendingRequest
            {
                challenger = netId,
                expires = NetworkTime.time + RequestLifetime
            };
            target.TargetDuelRequest(Multiplayer.PartySystem.DisplayNameOf(netId), netId);
        }

        [Command]
        private void CmdAcceptDuel(uint challengerNetId)
        {
            if (IsDueling) return;
            if (!pendingRequests.TryGetValue(netId, out var request)) return;
            pendingRequests.Remove(netId);
            if (request.challenger != challengerNetId || NetworkTime.time > request.expires) return;

            if (!NetworkServer.spawned.TryGetValue(challengerNetId, out var challengerIdentity) || challengerIdentity == null) return;
            var challenger = challengerIdentity.GetComponent<DuelMember>();
            if (challenger == null || challenger.IsDueling) return;

            // Lock the pair. Both SyncVars replicate, so both clients know their opponent.
            opponentNetId = challengerNetId;
            challenger.opponentNetId = netId;
            TargetDuelStarted(Multiplayer.PartySystem.DisplayNameOf(challengerNetId));
            challenger.TargetDuelStarted(Multiplayer.PartySystem.DisplayNameOf(netId));
        }

        /// <summary>
        /// SERVER (from Health.TakeDamage): the losing blow just got clamped at 1 HP.
        /// Clear the pair, announce, and restore both to full after a beat.
        /// (Explicit NetworkServer.active guard — the weaver's [Server] injection on
        /// static methods can't be validated by the offline compile.)
        /// </summary>
        public static void ServerEndDuel(GameObject winner, GameObject loser)
        {
            if (!NetworkServer.active) return;
            if (winner == null || loser == null) return;
            var winnerDuel = winner.GetComponent<DuelMember>();
            var loserDuel = loser.GetComponent<DuelMember>();
            if (winnerDuel == null || loserDuel == null) return;

            winnerDuel.opponentNetId = 0;
            loserDuel.opponentNetId = 0;

            winnerDuel.TargetDuelEnded(true, Multiplayer.PartySystem.DisplayNameOf(loserDuel.netId));
            loserDuel.TargetDuelEnded(false, Multiplayer.PartySystem.DisplayNameOf(winnerDuel.netId));

            // Runs on the winner's component; if the winner disconnects inside the delay
            // the coroutine dies with them and the loser just regens — acceptable MVP edge.
            winnerDuel.StartCoroutine(winnerDuel.RestoreBothAfterDelay(
                winner.GetComponent<Health>(), loser.GetComponent<Health>()));
        }

        // Server-only by construction (started from ServerEndDuel). No [Server] attribute:
        // a weaver guard on an IEnumerator would return null instead of the enumerator.
        private IEnumerator RestoreBothAfterDelay(Health winnerHealth, Health loserHealth)
        {
            yield return new WaitForSeconds(RestoreDelay);
            if (!NetworkServer.active) yield break;
            if (winnerHealth != null) winnerHealth.ResetHealth();
            if (loserHealth != null) loserHealth.ResetHealth();
        }

        public override void OnStopServer()
        {
            pendingRequests.Remove(netId);

            // Disconnecting mid-duel releases the opponent (no winner).
            if (opponentNetId != 0
                && NetworkServer.spawned.TryGetValue(opponentNetId, out var other) && other != null)
            {
                var otherDuel = other.GetComponent<DuelMember>();
                if (otherDuel != null && otherDuel.opponentNetId == netId)
                {
                    otherDuel.opponentNetId = 0;
                    otherDuel.TargetDuelCancelled();
                }
            }
            opponentNetId = 0;
        }

        // ---------------- target rpcs (run on owning client) ----------------

        [TargetRpc]
        public void TargetDuelRequest(string challengerName, uint challengerNetId)
        {
            PendingChallengerName = challengerName;
            PendingChallengerNetId = challengerNetId;
            Changed?.Invoke();
        }

        [TargetRpc]
        private void TargetDuelStarted(string opponentName)
        {
            Notice?.Invoke($"Duel started against {opponentName} — first to 1 HP loses!");
            Changed?.Invoke();
        }

        [TargetRpc]
        private void TargetDuelEnded(bool won, string opponentName)
        {
            Notice?.Invoke(won
                ? $"You WON the duel against {opponentName}!"
                : $"You lost the duel against {opponentName}.");
            Changed?.Invoke();
        }

        [TargetRpc]
        private void TargetDuelCancelled()
        {
            Notice?.Invoke("Duel ended — your opponent left.");
            Changed?.Invoke();
        }

        private void OnOpponentSync(uint _, uint __)
        {
            if (isLocalPlayer) Changed?.Invoke();
        }

        private void ClearRequest()
        {
            PendingChallengerNetId = 0;
            PendingChallengerName = null;
            Changed?.Invoke();
        }
    }
}
