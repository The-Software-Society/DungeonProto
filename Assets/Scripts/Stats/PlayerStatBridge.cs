using UnityEngine;
using Mirror;
using AldenFall.Progression;
using AldenFall.Combat;

namespace AldenFall.Stats
{
    /// <summary>
    /// Keeps the player's StatBlock base values in sync with skill levels.
    /// Equipment writes its own modifiers separately (per-slot sources), so
    /// StatBlock.Get always answers skills + gear in one call.
    ///
    /// Networking: skills train on the OWNING CLIENT (PlayerMelee/PlayerAbilities/
    /// PlayerDefence all run there), but combat is server-authoritative, Health.
    /// TakeDamage reads the SERVER's StatBlock to apply Defence mitigation. So the
    /// owning client replicates its combat levels to the server (co-op PvE trust
    /// model, same as PlayerCombat's damage relay). Without this the server sees a
    /// level-1 Defence and incoming damage is never reduced.
    /// </summary>
    [RequireComponent(typeof(StatBlock), typeof(Skills))]
    public class PlayerStatBridge : NetworkBehaviour
    {
        private StatBlock stats;
        private Skills skills;
        private Health health;
        private Items.Equipment equipment;
        private CombatStyleController combatStyle;

        // Hitpoints skill -> max HP: 100 at level 1, +10 per level (level 99 -> 1080).
        private static int MaxHealthForLevel(int level) => 100 + Mathf.Max(0, level - 1) * 10;

        private void Awake()
        {
            stats = GetComponent<StatBlock>();
            skills = GetComponent<Skills>();
            health = GetComponent<Health>();
            equipment = GetComponent<Items.Equipment>();
            combatStyle = GetComponent<CombatStyleController>();
        }

        private void Start()
        {
            skills.OnLevelUp.AddListener(OnLevelUp);

            // A pure client's equipment/stance modifiers live only on its local StatBlock, so the
            // server (which now RECOMPUTES damage + mitigation) needs them pushed. Host = server,
            // so its local mods already ARE the server's; only a pure owning client pushes.
            if (equipment != null) equipment.OnEquipmentChanged.AddListener(PushGearMods);
            if (combatStyle != null) combatStyle.OnStanceChanged += PushGearMods;

            // The server's copy of a REMOTE player's StatBlock is driven only by the
            // owning client's pushed levels (CmdSyncCombatLevels), never by this
            // machine's own Skills, which never train for a remote avatar. Everywhere
            // else (owning client, host, offline editor player) the local Skills are
            // authoritative, so mirror them into the local StatBlock.
            if (!IsServerRemoteProxy)
                Sync();
        }

        private void OnDestroy()
        {
            if (skills != null)
                skills.OnLevelUp.RemoveListener(OnLevelUp);
            if (equipment != null)
                equipment.OnEquipmentChanged.RemoveListener(PushGearMods);
            if (combatStyle != null)
                combatStyle.OnStanceChanged -= PushGearMods;
        }

        /// <summary>
        /// Owning pure client → server: replicate the equipment + stance modifier TOTALS so the
        /// server's StatBlock (skills base + these) matches the client's, letting the server
        /// recompute damage AND incoming mitigation authoritatively. Applied as one "netmods"
        /// source. No-op on the host (its local StatBlock already is the server's).
        /// </summary>
        private void PushGearMods()
        {
            if (!isLocalPlayer || isServer || stats == null) return;
            CmdSyncGearMods(
                stats.GetModifierTotal(StatType.WeaponPower),
                stats.GetModifierTotal(StatType.SpellPower),
                stats.GetModifierTotal(StatType.Armor),
                stats.GetModifierTotal(StatType.AttackSpeed),
                stats.GetModifierTotal(StatType.Defence),
                stats.GetModifierTotal(StatType.Strength),
                stats.GetModifierTotal(StatType.Magic),
                stats.GetModifierTotal(StatType.Ranged),
                stats.GetModifierTotal(StatType.MaxHealth),
                stats.GetModifierTotal(StatType.MoveSpeed));
        }

        [Command]
        private void CmdSyncGearMods(float weaponPower, float spellPower, float armor, float attackSpeed,
            float defence, float strength, float magic, float ranged, float maxHealth, float moveSpeed)
        {
            stats.ApplyModifiers("netmods", new (StatType, float)[]
            {
                (StatType.WeaponPower, weaponPower),
                (StatType.SpellPower, spellPower),
                (StatType.Armor, armor),
                (StatType.AttackSpeed, attackSpeed),
                (StatType.Defence, defence),
                (StatType.Strength, strength),
                (StatType.Magic, magic),
                (StatType.Ranged, ranged),
                (StatType.MaxHealth, maxHealth),
                (StatType.MoveSpeed, moveSpeed),
            });
            if (health != null) health.SetMaxHealth(stats.Get(StatType.MaxHealth));
        }

        /// <summary>
        /// True on the server for a player owned by some OTHER connection. That
        /// instance's local Skills are inert, so its StatBlock must come from the
        /// owner's replicated levels rather than from Sync().
        /// </summary>
        private bool IsServerRemoteProxy => isServer && !isLocalPlayer;

        private void OnLevelUp(SkillType skill, int newLevel)
        {
            if (!IsServerRemoteProxy)
                Sync();

            // Owning client: push combat levels to the server so server-authoritative
            // mitigation reads the real Defence (and miss chance the real Attack).
            if (isLocalPlayer)
                PushCombatLevels();
        }

        public override void OnStartLocalPlayer()
        {
            // Push current levels once on spawn, covers loaded/saved progress and
            // debug-set levels that changed before this object was network-ready.
            PushCombatLevels();
            PushGearMods();   // initial equipment/stance mods (starter loadout equips before this)
        }

        private void PushCombatLevels()
        {
            CmdSyncCombatLevels(
                skills.Level(SkillType.Attack),
                skills.Level(SkillType.Strength),
                skills.Level(SkillType.Defence),
                skills.Level(SkillType.Magic),
                skills.Level(SkillType.Ranged),
                skills.Level(SkillType.Health));
        }

        [Command]
        private void CmdSyncCombatLevels(int attack, int strength, int defence, int magic, int ranged, int health_)
        {
            // Server: mirror the owning client's leveled skills into this machine's
            // StatBlock, the single source of truth Health.TakeDamage reads.
            stats.SetBase(StatType.Attack, attack);
            stats.SetBase(StatType.Strength, strength);
            stats.SetBase(StatType.Defence, defence);
            stats.SetBase(StatType.Magic, magic);
            stats.SetBase(StatType.Ranged, ranged);
            stats.SetBase(StatType.MaxHealth, MaxHealthForLevel(health_));
            // Push the new cap (base + equipment MaxHealth bonuses) into Health authoritatively.
            if (health != null) health.SetMaxHealth(stats.Get(StatType.MaxHealth));
        }

        private void Sync()
        {
            stats.SetBase(StatType.Attack, skills.Level(SkillType.Attack));
            stats.SetBase(StatType.Strength, skills.Level(SkillType.Strength));
            stats.SetBase(StatType.Defence, skills.Level(SkillType.Defence));
            stats.SetBase(StatType.Magic, skills.Level(SkillType.Magic));
            stats.SetBase(StatType.Ranged, skills.Level(SkillType.Ranged));
            stats.SetBase(StatType.MaxHealth, MaxHealthForLevel(skills.Level(SkillType.Health)));
            // On the host/offline (server), apply immediately; pure clients get it via the Command RPC.
            if (health != null && isServer) health.SetMaxHealth(stats.Get(StatType.MaxHealth));
        }
    }
}
