using System;
using UnityEngine;
using Mirror;

namespace AldenFall.World
{
    /// <summary>
    /// The networked world-state for Floor 1's eruption climax. A single scene object
    /// (server-authoritative) holds one SyncVar — the current <see cref="Phase"/> — and
    /// replicates it to every client. The hook runs on all clients and drives the
    /// (cosmetic, client-side) <see cref="EmberAtmosphere"/> global intensity, so the
    /// whole world flips into apocalypse and back in lock-step:
    ///
    ///   Calm     → intensity 0.00  (lush, the default living world)
    ///   Erupting → intensity 0.85  (embers + soot rain, hellish sky, city under threat)
    ///   Cleared  → intensity 0.15  (settling embers after the Floor Boss falls, calm returning)
    ///
    /// The server API is deliberately tiny — <see cref="BeginEruption"/> and
    /// <see cref="MarkDungeonCleared"/> — and is called by the main-quest hooks the quest /
    /// world-gen agents wire (climax cutscene → BeginEruption; Floor Boss death →
    /// MarkDungeonCleared). Other systems (near-city lava, spawns, quest UI) subscribe to
    /// <see cref="OnPhaseChanged"/> rather than reaching in here.
    ///
    /// This is a scene NetworkBehaviour: its GO must carry a NetworkIdentity and be spawned
    /// with the scene (world-gen's responsibility), the same way Mirror handles other scene
    /// network objects in the project.
    /// </summary>
    public class EmberfallWorldState : NetworkBehaviour
    {
        public enum Phase { Calm, Erupting, Cleared }

        /// <summary>Scene singleton. There is one world-state per floor.</summary>
        public static EmberfallWorldState Instance { get; private set; }

        [Header("Atmosphere intensity per phase (0..1)")]
        [SerializeField, Range(0f, 1f)] private float calmIntensity = 0f;
        [SerializeField, Range(0f, 1f)] private float eruptingIntensity = 0.85f;
        [SerializeField, Range(0f, 1f)] private float clearedIntensity = 0.15f;

        [SyncVar(hook = nameof(OnPhaseSync))] private Phase phase = Phase.Calm;

        /// <summary>Fired on every machine (server + clients) whenever the phase changes.
        /// Listeners: near-city lava hazards, eruption spawns, quest UI, etc.</summary>
        public event Action<Phase> OnPhaseChanged;

        /// <summary>The current world phase.</summary>
        public Phase CurrentPhase => phase;

        // Dedupe guard so the atmosphere/event only reacts to a genuine change, no matter
        // whether it arrives via the SyncVar hook, OnStartClient, or a server setter.
        private bool hasApplied;
        private Phase lastApplied;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        [Header("Main-quest hooks")]
        [Tooltip("MobId whose death settles the eruption (the Floor Boss).")]
        [SerializeField] private string floorBossMobId = "cave_troll_boss";

        public override void OnStartClient()
        {
            // A late-joining pure client reflects the current phase immediately. On the host
            // the server setters already applied it, so skip to avoid a redundant pass.
            if (!isServer) ApplyPhase(phase);
        }

        public override void OnStartServer()
        {
            // Seed the initial phase so later setters register as genuine TRANSITIONS
            // (the climax cutscene only plays on a transition, never on initial state).
            ApplyPhase(phase);
            AI.MobIdentity.OnAnyMobKilled += HandleMobKilled;
        }

        public override void OnStopServer()
        {
            AI.MobIdentity.OnAnyMobKilled -= HandleMobKilled;
        }

        // The Floor Boss falling is THE floor objective: settle the eruption. If the
        // eruption somehow never triggered (entrance trigger skipped), advance through
        // Erupting so the world still converges on Cleared.
        [Server]
        private void HandleMobKilled(string mobId, GameObject mob)
        {
            if (mobId != floorBossMobId) return;
            if (phase == Phase.Calm) BeginEruption();
            MarkDungeonCleared();
        }

        // ---------------------------------------------------------------- server API

        /// <summary>Server: flip the world into the eruption (Calm → Erupting). No-op otherwise.
        /// Wired to the main-quest climax cutscene.</summary>
        [Server]
        public void BeginEruption()
        {
            if (phase != Phase.Calm) return;
            phase = Phase.Erupting;
            ApplyPhase(phase);   // host + server-side listeners (pure clients get it via the hook)
        }

        /// <summary>Server: the Floor Boss is dead — settle the world (Erupting → Cleared).
        /// No-op otherwise. Wired to the boss death.</summary>
        [Server]
        public void MarkDungeonCleared()
        {
            if (phase != Phase.Erupting) return;
            phase = Phase.Cleared;
            ApplyPhase(phase);
        }

        // --------------------------------------------------------------- replication

        // Runs on clients when the SyncVar arrives. On the host the [Server] setter above
        // already applied the change, so bail here to avoid double-firing (mirrors Health.cs).
        private void OnPhaseSync(Phase oldPhase, Phase newPhase)
        {
            if (isServer) return;
            ApplyPhase(newPhase);
        }

        // ------------------------------------------------------------------ apply

        private void ApplyPhase(Phase p)
        {
            if (hasApplied && lastApplied == p) return;
            bool isTransition = hasApplied; // false only for the initial state application
            hasApplied = true;
            lastApplied = p;

            if (EmberAtmosphere.Instance != null)
                EmberAtmosphere.Instance.SetGlobalIntensity(IntensityFor(p));

            OnPhaseChanged?.Invoke(p);

            // Climax beats: text cutscene on the LOCAL player's director. Only on a live
            // transition — a client logging in mid-eruption gets the atmosphere, not a
            // replay of the moment. Headless servers have no local player and skip this.
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
