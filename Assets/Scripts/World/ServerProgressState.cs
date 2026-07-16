using System;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

namespace AldenFall.World
{
    /// <summary>
    /// Server-wide Floor-1 progress and the first-clear prestige moment: the signature
    /// "the whole server shares one floor" pillar (SAO-style). One scene network object
    /// (server-authoritative) holds SyncVar counters replicated to every client:
    ///
    ///   bossClears     how many times the Floor Boss has fallen server-wide
    ///   floorCleared   has ANYONE cleared Floor 1 yet
    ///   firstClearName who landed the historic first clear ("" until then)
    ///
    /// On the FIRST clear it announces server-wide (RpcAnnounceFirstClear -> a banner on
    /// every client) and grants the "Hope of Emberfall" buff to all online players.
    /// Later clears just bump the counter. Modeled on <see cref="EmberfallWorldState"/>
    /// (same scene NetworkIdentity + boss-death hook via MobIdentity.OnAnyMobKilled), kept
    /// a SEPARATE object so atmosphere and progress concerns stay decoupled.
    /// </summary>
    public class ServerProgressState : NetworkBehaviour
    {
        /// <summary>Scene singleton (one server-progress tracker per floor).</summary>
        public static ServerProgressState Instance { get; private set; }

        // ---- Hope of Emberfall buff tuning. Kept as named consts for now; a future pass
        // can lift these into config/progression.json alongside the other combat knobs. ----
        public const float HopeBuffSeconds = 300f;   // 5 minutes of shared celebration
        public const float HopeWeaponPower = 4f;
        public const float HopeSpellPower = 4f;
        public const string HopeBuffId = "hope_of_emberfall";

        [SerializeField] private string floorBossMobId = "cave_troll_boss";

        [SyncVar(hook = nameof(OnClearsSync))] private int bossClears;
        [SyncVar(hook = nameof(OnFlagSync))] private bool floorCleared;
        [SyncVar(hook = nameof(OnNameSync))] private string firstClearName = "";

        /// <summary>Fired on every machine whenever the counters change (banners/boards refresh).</summary>
        public event Action OnProgressChanged;

        public int BossClears => bossClears;
        public bool FloorCleared => floorCleared;
        public string FirstClearName => firstClearName;

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
            // Server-wide floor state must reach every client regardless of distance (opt out of
            // distance interest management, which would hide this scene singleton from far players).
            netIdentity.visibility = Visibility.ForceShown;
            AI.MobIdentity.OnAnyMobKilled += HandleMobKilled;
        }

        public override void OnStopServer()
        {
            AI.MobIdentity.OnAnyMobKilled -= HandleMobKilled;
        }

        public override void OnStartClient()
        {
            // Late joiner reflects the current standings immediately.
            if (!isServer) OnProgressChanged?.Invoke();
        }

        [Server]
        private void HandleMobKilled(string mobId, GameObject mob)
        {
            if (mobId != floorBossMobId) return;
            // The last attacker was credited the kill by MobIdentity; use it for the name.
            RegisterBossClear(FindKiller(mob));
        }

        // MobIdentity credits the last attacker but does not expose it here; fall back to the
        // nearest local player. Good enough to name the historic clear in the host demo.
        private static GameObject FindKiller(GameObject mob)
        {
            return Multiplayer.LocalPlayerLocator.Find();
        }

        /// <summary>Server: record a Floor-Boss kill. The FIRST is the historic server-wide
        /// clear (announcement + Hope buff for everyone); later kills only bump the count.</summary>
        [Server]
        public void RegisterBossClear(GameObject killer)
        {
            bool wasFirst = !floorCleared;
            bossClears++;

            // Personal clear credit: the killer and their party each record Floor 1 cleared
            // (persisted per player, gates later floors).
            MarkPersonalClear(killer);
            Multiplayer.PartySystem.ForEachOtherMember(killer, MarkPersonalClear);

            if (wasFirst)
            {
                floorCleared = true;
                firstClearName = ResolveName(killer);
                RpcAnnounceFirstClear(firstClearName);
                GrantHopeOfEmberfallToAll();
            }

            OnProgressChanged?.Invoke();   // host-side boards
        }

        private static void MarkPersonalClear(GameObject player)
        {
            if (player != null)
                player.GetComponent<Progression.PlayerProgress>()?.MarkFloor1Cleared();
        }

        private static string ResolveName(GameObject killer)
        {
            var avatar = killer != null ? killer.GetComponent<Multiplayer.NetworkPlayerAvatar>() : null;
            if (avatar != null && !string.IsNullOrWhiteSpace(avatar.DisplayName)) return avatar.DisplayName;
            return "A hero of Emberfall";
        }

        [ClientRpc]
        private void RpcAnnounceFirstClear(string clearerName)
        {
            UI.FirstClearBanner.Show(clearerName);
            OnProgressChanged?.Invoke();
        }

        // ---- Hope of Emberfall: a server-wide buff on the first clear. Buffs live on each
        // player's OWNING machine (StatusSystem is client-local), so we TargetRpc every
        // connection and let each client apply it to its own local player. In host mode the
        // host's connection resolves locally, so this covers the host too. ----

        [Server]
        private void GrantHopeOfEmberfallToAll()
        {
            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null) continue;
                // Host's own player: apply inline (a TargetRpc to the local connection is
                // deferred past this frame). Pure clients: TargetRpc onto their machine.
                if (conn == NetworkServer.localConnection)
                    ApplyHopeBuffLocal();
                else
                    TargetGrantHope(conn);
            }
        }

        [TargetRpc]
        private void TargetGrantHope(NetworkConnectionToClient target)
        {
            ApplyHopeBuffLocal();
        }

        /// <summary>Apply the Hope buff to THIS machine's local player. Public + static so the
        /// QA harness can exercise it directly in a single host.</summary>
        public static void ApplyHopeBuffLocal()
        {
            var player = Multiplayer.LocalPlayerLocator.Find();
            var status = player != null ? player.GetComponent<Combat.StatusSystem>() : null;
            if (status == null) return;

            status.Apply(new Combat.StatusEffect
            {
                Id = HopeBuffId,
                DisplayName = "Hope of Emberfall",
                Duration = HopeBuffSeconds,
                StatMods = new List<(Stats.StatType stat, float amount)>
                {
                    (Stats.StatType.WeaponPower, HopeWeaponPower),
                    (Stats.StatType.SpellPower, HopeSpellPower),
                },
            });
        }

        private void OnClearsSync(int _, int __) => OnProgressChanged?.Invoke();
        private void OnFlagSync(bool _, bool __) => OnProgressChanged?.Invoke();
        private void OnNameSync(string _, string __) => OnProgressChanged?.Invoke();
    }
}
