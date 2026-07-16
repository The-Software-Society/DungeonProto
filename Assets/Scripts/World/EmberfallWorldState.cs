using System;
using UnityEngine;
using Mirror;

namespace AldenFall.World
{
    /// <summary>
    /// Per-CLIENT PRESENTATION hub for the eruption. The authoritative phase now lives PER-PLAYER on
    /// <see cref="PlayerWorldPhase"/> (the eruption follows each player's own questline — MMO phasing),
    /// and THIS scene singleton just applies the LOCAL player's phase to the local world look:
    /// atmosphere/sky, the lava-hazard content, the volcano plume, the floor gate, and the eruption/
    /// floor-cleared cutscenes. So a player still in town sees a calm world with no cutscene, while the
    /// party fighting the boss sees the mountain burn. Listeners (EruptionSpawns, VolcanoPlume,
    /// WorldStateGate, EmberAtmosphere) subscribe to <see cref="OnPhaseChanged"/> unchanged.
    ///
    /// The server-wide floor CLEAR (the SAO "shared floor" pillar) is a SEPARATE concern that stays
    /// global — see <see cref="ServerProgressState"/> (first-clear banner + Hope buff).
    ///
    /// Kept a NetworkBehaviour scene object (its baked NetworkIdentity) so nothing else that references
    /// Instance breaks, but it no longer holds a networked phase SyncVar.
    /// </summary>
    public class EmberfallWorldState : NetworkBehaviour
    {
        public enum Phase { Calm, Erupting, Cleared }

        /// <summary>Scene singleton (the per-client presentation hub).</summary>
        public static EmberfallWorldState Instance { get; private set; }

        [Header("Atmosphere intensity per phase (0..1)")]
        [SerializeField, Range(0f, 1f)] private float calmIntensity = 0f;
        [SerializeField, Range(0f, 1f)] private float eruptingIntensity = 0.85f;
        [SerializeField, Range(0f, 1f)] private float clearedIntensity = 0.15f;

        private Phase phase = Phase.Calm;   // the LOCAL player's phase, mirrored for presentation

        /// <summary>Fired on this machine whenever the local presentation phase changes.</summary>
        public event Action<Phase> OnPhaseChanged;

        public Phase CurrentPhase => phase;

        private bool hasApplied;
        private Phase lastApplied;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartServer()
        {
            // This scene singleton's Instance must exist on EVERY client (local presentation hub), so
            // it must never be culled by distance interest management.
            netIdentity.visibility = Visibility.ForceShown;
        }

        public override void OnStartClient()
        {
            // Seed the calm look; the local player's PlayerWorldPhase pushes its real phase in
            // once it spawns/syncs.
            SetPresentationPhase(phase);
        }

        /// <summary>
        /// Drive this client's world look to a phase. Called by the LOCAL player's
        /// <see cref="PlayerWorldPhase"/>. Purely cosmetic + local lava content; the authoritative
        /// phase (and who lava burns) is per-player on the server.
        /// </summary>
        public void SetPresentationPhase(Phase p)
        {
            phase = p;
            ApplyPhase(p);
        }

        /// <summary>
        /// DEV/testing (F9 panel, QA driver): force the LOCAL PLAYER's authoritative phase, which then
        /// drives presentation for real (and lava). Falls back to presentation-only if no local player.
        /// </summary>
        public void DebugForcePhase(Phase p)
        {
            var local = Multiplayer.LocalPlayerLocator.Find();
            var pwp = local != null ? local.GetComponent<PlayerWorldPhase>() : null;
            if (pwp != null) pwp.RequestPhase(p);
            else SetPresentationPhase(p);
        }

        private void ApplyPhase(Phase p)
        {
            if (hasApplied && lastApplied == p) return;
            bool isTransition = hasApplied;   // no cutscene on the very first (seed) application
            hasApplied = true;
            lastApplied = p;

            if (EmberAtmosphere.Instance != null)
                EmberAtmosphere.Instance.SetGlobalIntensity(IntensityFor(p));

            OnPhaseChanged?.Invoke(p);

            // Climax beats on the local player's director, only on a genuine transition (a client
            // arriving mid-eruption gets the atmosphere, not a replay). Headless has no local player.
            if (isTransition && p != Phase.Calm)
            {
                var localPlayer = Multiplayer.LocalPlayerLocator.Find();
                var director = localPlayer != null
                    ? localPlayer.GetComponent<Cinematics.CutsceneDirector>() : null;
                if (director != null && !director.IsPlaying)
                    director.Play(p == Phase.Erupting ? "eruption" : "floor_cleared");
            }
        }

        private float IntensityFor(Phase p)
        {
            switch (p)
            {
                case Phase.Erupting: return eruptingIntensity;
                case Phase.Cleared:  return clearedIntensity;
                default:             return calmIntensity;
            }
        }
    }
}
