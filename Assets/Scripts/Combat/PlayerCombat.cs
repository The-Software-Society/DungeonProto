using Mirror;
using UnityEngine;

namespace AldenFall.Combat
{
    /// <summary>
    /// Client→server damage relay for the local player. Combat is server-authoritative
    /// (Health.TakeDamage/ApplyKnockback only run on the server), so player attacks route
    /// their hit through here: on the host it applies directly; on a client it sends a
    /// Command and the server applies it, then the result replicates back via the Health
    /// SyncVar + damage-number RPC.
    ///
    /// Damage is computed client-side and trusted by the server, fine for a co-op PvE
    /// test. If PvP/competitive ever matters, recompute damage server-side from the
    /// attacker's StatBlock instead.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        /// <summary>
        /// Relay a player hit to the server. <paramref name="xpSkill"/> (a
        /// <see cref="Progression.SkillType"/> as int, -1 = none) and <paramref name="xpRate"/>
        /// travel with the hit so the SERVER can mint combat XP at the moment the damage lands
        /// on the enemy's <see cref="Health"/>, the single, damage-integral XP source. Nothing
        /// on the swing/cast side grants XP anymore.
        /// </summary>
        public void DealDamage(Health target, float damage, bool canMiss = false,
            int xpSkill = -1, float xpRate = 0f)
        {
            if (target == null || damage <= 0f) return;
            if (isServer)
                target.TakeDamage(damage, gameObject, canMiss, xpSkill, xpRate);
            else if (isLocalPlayer)
                CmdDealDamage(target.netIdentity, damage, canMiss, xpSkill, xpRate);
        }

        [Command]
        private void CmdDealDamage(NetworkIdentity target, float damage, bool canMiss, int xpSkill, float xpRate)
        {
            if (target == null) return;
            var health = target.GetComponent<Health>();
            if (health != null)
                health.TakeDamage(damage, gameObject, canMiss, xpSkill, xpRate);
        }

        /// <summary>
        /// Server → this attacker's owning client: award combat XP for damage this player just
        /// dealt. Called only from <see cref="Health.TakeDamage"/> after real damage lands, so
        /// XP stays welded to the actual interaction. Skills train on the owning client (same as
        /// PlayerDefence/PlayerMelee), so a remote attacker gets a TargetRpc; the host's own
        /// player trains inline. Skill is passed as int to match the codebase's weaver-approved
        /// primitive-only Command/Rpc signatures.
        /// </summary>
        public void ServerGrantCombatXp(int xpSkill, float xp)
        {
            if (!isServer || xp <= 0f || xpSkill < 0) return;
            if (isLocalPlayer)
                ApplyCombatXp(xpSkill, xp);
            else
                TargetGrantCombatXp(xpSkill, xp);
        }

        [TargetRpc]
        private void TargetGrantCombatXp(int xpSkill, float xp)
        {
            ApplyCombatXp(xpSkill, xp);
        }

        /// <summary>Grant the chosen offensive skill AND an OSRS-style Hitpoints trickle: every
        /// point of combat XP also feeds Hitpoints (SkillType.Health) at hitpointsXpFraction,
        /// so Hitpoints levels slowly from ALL combat (melee/magic/archery), never from the
        /// Health grant itself.</summary>
        private void ApplyCombatXp(int xpSkill, float xp)
        {
            var skills = GetComponent<Progression.Skills>();
            if (skills == null) return;
            skills.AddXp((Progression.SkillType)xpSkill, xp);
            if ((Progression.SkillType)xpSkill != Progression.SkillType.Health)
            {
                float hp = xp * Data.GameConfig.Progression.hitpointsXpFraction;
                if (hp > 0f) skills.AddXp(Progression.SkillType.Health, hp);
            }
        }

        /// <summary>Server grants gold to this player. Host player (local) gets it directly;
        /// a pure-client player on the dedicated server gets it via TargetRpc so the client
        /// HUD and Wallet see it. Mirrors ServerGrantCombatXp.</summary>
        public void ServerGrantGold(int amount)
        {
            if (!isServer || amount <= 0) return;
            if (isLocalPlayer)
                GetComponent<Items.Wallet>()?.Add(amount);
            else
                TargetGrantGold(amount);
        }

        [TargetRpc]
        private void TargetGrantGold(int amount)
        {
            GetComponent<Items.Wallet>()?.Add(amount);
        }

        /// <summary>Server advances this player's KillMob quest credit. Quest state is
        /// client-local (the owning client's HUD reads it), so the host player advances
        /// inline while a pure-client player on the dedicated server gets a TargetRpc.
        /// Mirrors ServerGrantGold; without it a pure client's kill quests never progress.</summary>
        public void ServerAdvanceKillQuest(string mobId)
        {
            if (!isServer || string.IsNullOrEmpty(mobId)) return;
            if (isLocalPlayer)
                GetComponent<Quests.QuestLog>()?.Advance(Quests.ObjectiveKind.KillMob, mobId, 1, fromParty: true);
            else
                TargetAdvanceKillQuest(mobId);
        }

        [TargetRpc]
        private void TargetAdvanceKillQuest(string mobId)
        {
            GetComponent<Quests.QuestLog>()?.Advance(Quests.ObjectiveKind.KillMob, mobId, 1, fromParty: true);
        }

        // ---- party quest fan-out: when a LOCAL objective advances (collect/reach/talk/inspect/
        // fish/cook/ability/defend), share it with every OTHER party member so a shared quest
        // progresses together and fires the same objective/complete cutscenes at once. Kill
        // objectives are already fanned out by MobIdentity, so they never enter here. Members
        // receive the advance as fromParty so it never echoes back. ----

        public void ShareQuestObjective(int kind, string targetId, int amount)
        {
            if (isServer) ServerFanOutQuestObjective(kind, targetId, amount);
            else if (isLocalPlayer) CmdShareQuestObjective(kind, targetId, amount);
        }

        [Command]
        private void CmdShareQuestObjective(int kind, string targetId, int amount)
        {
            ServerFanOutQuestObjective(kind, targetId, amount);
        }

        private void ServerFanOutQuestObjective(int kind, string targetId, int amount)
        {
            if (!isServer) return;
            Multiplayer.PartySystem.ForEachOtherMember(gameObject, member =>
                member.GetComponent<PlayerCombat>()?.ServerReceiveQuestObjective(kind, targetId, amount));
        }

        public void ServerReceiveQuestObjective(int kind, string targetId, int amount)
        {
            if (!isServer) return;
            if (isLocalPlayer)
                GetComponent<Quests.QuestLog>()?.Advance((Quests.ObjectiveKind)kind, targetId, amount, fromParty: true);
            else
                TargetReceiveQuestObjective(kind, targetId, amount);
        }

        [TargetRpc]
        private void TargetReceiveQuestObjective(int kind, string targetId, int amount)
        {
            GetComponent<Quests.QuestLog>()?.Advance((Quests.ObjectiveKind)kind, targetId, amount, fromParty: true);
        }

        // Healing (food, etc.) is server-authoritative like damage, a client calling Health.Heal
        // directly no-ops (server guard), so route it through the server.
        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            if (isServer)
                GetComponent<Health>()?.Heal(amount);
            else if (isLocalPlayer)
                CmdHeal(amount);
        }

        [Command]
        private void CmdHeal(float amount)
        {
            GetComponent<Health>()?.Heal(amount);
        }

        // ---- status relay: a client-applied status (stun/bleed/slow/hex, or a self-buff)
        // must also land on the SERVER's copy of the target, or a pure client's effects are
        // cosmetic-only — the server resolves DoT ticks, incoming-damage mitigation, and the
        // stun-on-AI. We apply LOCALLY too (instant HUD/visual + client-side offensive buffs,
        // since player damage is client-computed), then relay; on the host the local apply IS
        // the authoritative one so no Command is sent. ----

        public struct NetStatusEffect
        {
            public string id;
            public string displayName;
            public float duration;
            public int flags;
            public float tickInterval;
            public float tickDamage;
            public float moveSpeedMultiplier;
            public int[] statTypes;
            public float[] statAmounts;
        }

        public void ApplyStatus(Health target, StatusEffect effect)
        {
            if (target == null || effect == null || string.IsNullOrEmpty(effect.Id)) return;
            target.GetComponent<StatusSystem>()?.Apply(effect);            // local: visuals + client-side offense
            if (!isServer && isLocalPlayer)
                CmdApplyStatus(target.netIdentity, ToNet(effect));         // authoritative on the server
        }

        [Command]
        private void CmdApplyStatus(NetworkIdentity target, NetStatusEffect net)
        {
            if (target == null) return;
            target.GetComponent<StatusSystem>()?.Apply(FromNet(net, gameObject));
        }

        private static NetStatusEffect ToNet(StatusEffect e)
        {
            int n = e.StatMods != null ? e.StatMods.Count : 0;
            var types = new int[n];
            var amts = new float[n];
            for (int i = 0; i < n; i++) { types[i] = (int)e.StatMods[i].stat; amts[i] = e.StatMods[i].amount; }
            return new NetStatusEffect
            {
                id = e.Id, displayName = e.DisplayName, duration = e.Duration, flags = (int)e.Flags,
                tickInterval = e.TickInterval, tickDamage = e.TickDamage,
                moveSpeedMultiplier = e.MoveSpeedMultiplier, statTypes = types, statAmounts = amts,
            };
        }

        private static StatusEffect FromNet(NetStatusEffect n, GameObject source)
        {
            System.Collections.Generic.List<(Stats.StatType, float)> mods = null;
            if (n.statTypes != null && n.statTypes.Length > 0)
            {
                mods = new System.Collections.Generic.List<(Stats.StatType, float)>(n.statTypes.Length);
                for (int i = 0; i < n.statTypes.Length; i++)
                    mods.Add(((Stats.StatType)n.statTypes[i], n.statAmounts[i]));
            }
            return new StatusEffect
            {
                Id = n.id, DisplayName = n.displayName, Duration = n.duration, Flags = (StatusFlags)n.flags,
                TickInterval = n.tickInterval, TickDamage = n.tickDamage,
                MoveSpeedMultiplier = n.moveSpeedMultiplier, StatMods = mods, Source = source,
            };
        }

        public void ApplyKnockbackTo(Health target, Vector3 direction, float force)
        {
            if (target == null || force <= 0f) return;
            if (isServer)
                target.ApplyKnockback(direction, force);
            else if (isLocalPlayer)
                CmdKnockback(target.netIdentity, direction, force);
        }

        [Command]
        private void CmdKnockback(NetworkIdentity target, Vector3 direction, float force)
        {
            if (target == null) return;
            var health = target.GetComponent<Health>();
            if (health != null)
                health.ApplyKnockback(direction, force);
        }

        // ---- loot drop relay: a client dropping an item asks the server to spawn the
        // networked world drop so every player sees it (LootDrop.Spawn routes here). ----

        public void RequestDrop(string itemId, Vector3 position, float pickupDelay)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            if (isServer)
                Items.LootDrop.ServerSpawn(itemId, position, pickupDelay);
            else if (isLocalPlayer)
                CmdRequestDrop(itemId, position, pickupDelay);
        }

        [Command]
        private void CmdRequestDrop(string itemId, Vector3 position, float pickupDelay)
        {
            Items.LootDrop.ServerSpawn(itemId, position, pickupDelay);
        }

        // ---- cosmetic cast replication: other players SEE your skill visuals + animation.
        // Damage is already server-authoritative (DealDamage above), so this is visuals only. ----

        public void BroadcastCast(string abilityId, Vector3 origin, Vector3 aimDir, NetworkIdentity target)
        {
            if (!isLocalPlayer || string.IsNullOrEmpty(abilityId)) return;
            CmdCast(abilityId, origin, aimDir, target);
        }

        [Command]
        private void CmdCast(string abilityId, Vector3 origin, Vector3 aimDir, NetworkIdentity target)
        {
            RpcCast(abilityId, origin, aimDir, target);
        }

        // includeOwner = false: the caster already played the visual locally; every OTHER client
        // (including the host as an observer) replays a damage-free copy on the caster's avatar.
        [ClientRpc(includeOwner = false)]
        private void RpcCast(string abilityId, Vector3 origin, Vector3 aimDir, NetworkIdentity target)
        {
            var abilities = GetComponent<Abilities.PlayerAbilities>();
            if (abilities != null)
                abilities.ReplayRemoteCast(abilityId, origin, aimDir, target != null ? target.transform : null);
        }
    }
}
