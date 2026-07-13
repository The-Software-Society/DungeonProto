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
                GetComponent<Progression.Skills>()?.AddXp((Progression.SkillType)xpSkill, xp);
            else
                TargetGrantCombatXp(xpSkill, xp);
        }

        [TargetRpc]
        private void TargetGrantCombatXp(int xpSkill, float xp)
        {
            GetComponent<Progression.Skills>()?.AddXp((Progression.SkillType)xpSkill, xp);
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
