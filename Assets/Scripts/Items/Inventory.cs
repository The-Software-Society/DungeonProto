using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace AldenFall.Items
{
    /// <summary>
    /// Slot-based inventory. Each entry is one bag cell; stackable items
    /// (ItemDef.MaxStack > 1) accumulate a Count instead of eating more cells.
    /// </summary>
    public class Inventory : MonoBehaviour
    {
        public const int Capacity = 20;

        [System.Serializable]
        public class Entry
        {
            public string ItemId;
            public int Count = 1;
        }

        [Header("Events")]
        public UnityEvent OnChanged;
        public UnityEvent<string> OnItemAdded;   // item id (for pickup toasts)

        private readonly List<Entry> slots = new List<Entry>();

        public IReadOnlyList<Entry> Slots => slots;
        public bool IsFull => slots.Count >= Capacity;

        public bool Add(string itemId)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null) return false;

            if (def.MaxStack > 1)
            {
                var stack = slots.Find(e => e.ItemId == itemId && e.Count < def.MaxStack);
                if (stack != null)
                {
                    stack.Count++;
                    OnItemAdded?.Invoke(itemId);
                    OnChanged?.Invoke();
                    return true;
                }
            }

            if (IsFull) return false;
            slots.Add(new Entry { ItemId = itemId, Count = 1 });
            OnItemAdded?.Invoke(itemId);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Removes ONE item from the cell at index (whole cell at count 1).</summary>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= slots.Count) return false;
            if (slots[index].Count > 1)
                slots[index].Count--;
            else
                slots.RemoveAt(index);
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Removes ONE of the given item.</summary>
        public bool Remove(string itemId)
        {
            int index = slots.FindIndex(e => e.ItemId == itemId);
            return index >= 0 && RemoveAt(index);
        }

        public bool Contains(string itemId) => slots.Exists(e => e.ItemId == itemId);

        public int CountOf(string itemId)
        {
            int total = 0;
            foreach (var e in slots)
                if (e.ItemId == itemId) total += e.Count;
            return total;
        }
    }
}
