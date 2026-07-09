using UnityEngine;
using AldenFall.Items;

namespace AldenFall.World
{
    public class LootChest : Interactable
    {
        [System.Serializable]
        public struct Reward
        {
            public string itemId;
            [Range(0f, 1f)] public float chance;
        }

        [SerializeField] private Reward[] rewards = new Reward[0];
        [SerializeField] private bool opened;

        public override void Interact(GameObject player)
        {
            if (opened || player == null) return;

            opened = true;
            Prompt = "Opened";

            var inventory = player.GetComponent<Inventory>();
            foreach (var reward in rewards)
            {
                if (string.IsNullOrEmpty(reward.itemId) || Random.value > reward.chance)
                    continue;

                string id = reward.itemId;
                var def = ItemDatabase.Get(id);
                if (def != null && def.Slot != EquipSlot.None)
                    id = ItemPlus.Compose(id, Mathf.Max(4, ItemPlus.RollPlus()));

                LootDrop.GiveOrDrop(inventory, id, player.transform);
            }

            transform.localRotation *= Quaternion.Euler(-18f, 0f, 0f);
        }
    }
}
