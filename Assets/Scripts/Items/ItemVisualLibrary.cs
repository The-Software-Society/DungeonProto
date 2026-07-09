using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.Items
{
    /// <summary>
    /// Scene-resident lookup from item id to model prefab + icon sprite.
    /// Populated at build time by the editor tooling (runtime code cannot use
    /// AssetDatabase); consumed by Equipment (hand visuals), LootDrop (world
    /// pickups) and the inventory UI (icons).
    /// </summary>
    public class ItemVisualLibrary : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public string itemId;
            public GameObject modelPrefab;
            public Sprite icon;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        private static ItemVisualLibrary instance;
        private Dictionary<string, Entry> lookup;

        public static ItemVisualLibrary Instance
        {
            get
            {
                if (instance == null)
                    instance = FindFirstObjectByType<ItemVisualLibrary>();
                return instance;
            }
        }

        private void Awake()
        {
            instance = this;
        }

        private Entry Find(string itemId)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Entry>();
                foreach (var e in entries)
                    lookup[e.itemId] = e;
            }
            lookup.TryGetValue(itemId, out var entry);
            return entry;
        }

        public GameObject GetModel(string itemId) => Find(itemId)?.modelPrefab;
        public Sprite GetIcon(string itemId) => Find(itemId)?.icon;

        /// <summary>Used by editor build tooling to populate the library.</summary>
        public void EditorSetEntries(List<Entry> newEntries) => entries = newEntries;
    }
}
