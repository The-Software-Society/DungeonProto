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
    /// Damage is computed client-side and trusted by the server — fine for a co-op PvE
    /// test. If PvP/competitive ever matters, recompute damage server-side from the
    /// attacker's StatBlock instead.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        public void DealDamage(Health target, float damage, bool canMiss = false)
        {
            if (target == null || damage <= 0f) return;
            if (isServer)
                target.TakeDamage(damage, gameObject, canMiss);
            else if (isLocalPlayer)
                CmdDealDamage(target.netIdentity, damage, canMiss);
        }

        [Command]
        private void CmdDealDamage(NetworkIdentity target, float damage, bool canMiss)
        {
            if (target == null) return;
            var health = target.GetComponent<Health>();
            if (health != null)
                health.TakeDamage(damage, gameObject, canMiss);
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
    }
}
