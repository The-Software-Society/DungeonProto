using UnityEngine;
using UnityEngine.Events;

namespace AldenFall.Items
{
    /// <summary>
    /// The player's gold purse. A plain (non-networked) MonoBehaviour: in the
    /// host-authoritative demo the killer's player object lives on the same
    /// machine as its owning client, so a server-side grant (MobIdentity) lands
    /// on the right Wallet and the local HUD sees it. A dedicated-server split
    /// (no local client for that player) would need a SyncVar / TargetRpc bridge
    /// — the same limitation noted on MobIdentity's kill credit.
    /// </summary>
    public class Wallet : MonoBehaviour
    {
        /// <summary>Concrete subclass so the UnityEvent is real when added at runtime
        /// (generic UnityEvent&lt;T&gt; is abstract and can't self-instantiate).</summary>
        [System.Serializable] public class GoldChanged : UnityEvent<int> { }

        [SerializeField] private int gold;
        public GoldChanged OnChanged = new GoldChanged();

        public int Gold => gold;

        public void Add(int amount)
        {
            if (amount <= 0) return;
            gold += amount;
            OnChanged?.Invoke(gold);
        }

        /// <summary>Spends gold if affordable; returns false (and spends nothing) otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0 || gold < amount) return false;
            gold -= amount;
            OnChanged?.Invoke(gold);
            return true;
        }
    }
}
