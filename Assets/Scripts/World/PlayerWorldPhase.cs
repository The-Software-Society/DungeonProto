using Mirror;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Per-player world phase (Calm / Erupting / Cleared): the eruption follows each player's OWN
    /// questline (MMO phasing), so a player still in town sees a calm world and gets no eruption
    /// cutscene, while the party fighting the boss sees the mountain burn. Server-owned SyncVar;
    /// on the OWNING client it drives that client's local scene presentation through
    /// <see cref="EmberfallWorldState"/> (sky, lava content, plume, floor gate, cutscenes).
    ///
    /// Partymates reach the same phase because quest progress fans out (each own phase, step-gated),
    /// NOT because of a shared world flag. The server reads this SyncVar to decide who lava burns.
    /// </summary>
    public class PlayerWorldPhase : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnPhaseSync))]
        private EmberfallWorldState.Phase phase = EmberfallWorldState.Phase.Calm;

        public EmberfallWorldState.Phase Phase => phase;

        public override void OnStartClient()
        {
            if (isLocalPlayer) ApplyLocalPresentation(phase);
        }

        public override void OnStartLocalPlayer()
        {
            ApplyLocalPresentation(phase);
        }

        private void OnPhaseSync(EmberfallWorldState.Phase _, EmberfallWorldState.Phase next)
        {
            if (isLocalPlayer) ApplyLocalPresentation(next);
        }

        // Only the LOCAL player's phase drives THIS client's world look (remote players' phases are
        // their own business).
        private static void ApplyLocalPresentation(EmberfallWorldState.Phase p)
        {
            EmberfallWorldState.Instance?.SetPresentationPhase(p);
        }

        /// <summary>Server: advance this player's phase (never regress within a floor; Calm resets).</summary>
        [Server]
        public void ServerSetPhase(EmberfallWorldState.Phase p)
        {
            if (p != EmberfallWorldState.Phase.Calm && (int)p <= (int)phase) return;
            phase = p;
        }

        /// <summary>Owning client → server: request a phase (quest hooks, the F9 dev panel).</summary>
        public void RequestPhase(EmberfallWorldState.Phase p)
        {
            if (isServer) ServerSetPhase(p);
            else if (isLocalPlayer) CmdSetPhase(p);
        }

        [Command]
        private void CmdSetPhase(EmberfallWorldState.Phase p) => ServerSetPhase(p);
    }
}
