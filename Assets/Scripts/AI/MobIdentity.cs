using UnityEngine;
using AldenFall.Combat;
using AldenFall.Items;

namespace AldenFall.AI
{
    /// <summary>
    /// Names a mob for the target frame and rolls its loot table on death.
    /// Kept separate from EnemyAI so any Health-bearing thing can drop loot.
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

        [Header("Loot Table (each entry rolls independently)")]
        [SerializeField] private LootEntry[] lootTable = new LootEntry[0];

        public string DisplayName => displayName;
        public int CombatLevel => combatLevel;

        private void Start()
        {
            GetComponent<Health>().OnDeath.AddListener(RollLoot);
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
