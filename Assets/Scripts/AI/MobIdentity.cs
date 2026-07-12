using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;

namespace AldenFall.AI
{
    /// <summary>
    /// Names a mob for the target frame, rolls its loot table on death, and pays
    /// out gold + quest kill-credit to whoever killed it. Kept separate from
    /// EnemyAI so any Health-bearing thing can drop loot.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class MobIdentity : MonoBehaviour
    {
        [System.Serializable]
        public struct LootEntry
        {
            public string itemId;
            [Range(0f, 1f)] public float chance;
        }

        [Header("Identity")]
        [SerializeField] private string displayName = "Monster";
        [SerializeField] private int combatLevel = 2;

        [Tooltip("Stable id for KillMob quest objectives (e.g. \"grukmar\"). Optional.")]
        public string MobId;

        [Header("Gold (scaled by combat level)")]
        [SerializeField] private Vector2Int goldRange = new Vector2Int(1, 5);

        [Header("Loot Table (each entry rolls independently)")]
        [SerializeField] private LootEntry[] lootTable = new LootEntry[0];

        public string DisplayName => displayName;
        public int CombatLevel => combatLevel;

        /// <summary>
        /// SERVER-side: fired when any mob with a MobId dies (Health death is
        /// server-authoritative, so this never fires on pure clients). World-state
        /// hooks subscribe here — e.g. the Floor Boss death settling the eruption —
        /// without every mob needing a bespoke watcher component.
        /// </summary>
        public static event System.Action<string, GameObject> OnAnyMobKilled;

        // Last thing to hurt us — tracked server-side so death can credit the killer.
        private GameObject lastAttacker;

        private void Start()
        {
            var health = GetComponent<Health>();
            health.OnDeath.AddListener(OnDeath);
            health.OnAttacked.AddListener(a => lastAttacker = a);
        }

        private void OnDeath()
        {
            RollLoot();
            GrantKillRewards();
            if (!string.IsNullOrEmpty(MobId))
                OnAnyMobKilled?.Invoke(MobId, gameObject);
        }

        /// <summary>
        /// Gold + KillMob quest credit go to the last attacker (the killer). Death
        /// runs on the SERVER, and in the host-authoritative demo the killer's
        /// player object lives on the same machine as its owning client, so the
        /// Wallet/QuestLog updated here are the very ones the local HUD reads.
        /// A dedicated-server split (no local client for that player) would need a
        /// SyncVar / TargetRpc bridge to push these to the owning client — an
        /// accepted limitation for now (party-shared credit also comes later).
        /// </summary>
        private void GrantKillRewards()
        {
            if (lastAttacker == null) return;

            int gold = Random.Range(goldRange.x, goldRange.y + 1) * Mathf.Max(1, combatLevel);
            lastAttacker.GetComponent<Wallet>()?.Add(gold);

            if (!string.IsNullOrEmpty(MobId))
                lastAttacker.GetComponent<Quests.QuestLog>()?.Advance(Quests.ObjectiveKind.KillMob, MobId);
        }

        private void RollLoot()
        {
            foreach (var entry in lootTable)
            {
                if (string.IsNullOrEmpty(entry.itemId)) continue;
                if (Random.value > entry.chance) continue;

                // Equipment rolls a +1..+10 upgrade (the id carries it)
                string dropId = entry.itemId;
                var def = ItemDatabase.Get(dropId);
                if (def != null && def.Slot != EquipSlot.None)
                    dropId = ItemPlus.Compose(dropId, ItemPlus.RollPlus());

                // Scatter multiple drops slightly so they don't stack
                Vector3 offset = Random.insideUnitSphere * 0.6f;
                offset.y = 0;
                LootDrop.Spawn(dropId, transform.position + offset);
            }
        }
    }
}
