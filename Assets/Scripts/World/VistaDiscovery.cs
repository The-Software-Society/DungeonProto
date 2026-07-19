using System.Collections.Generic;
using UnityEngine;

namespace AldenFall.World
{
    /// <summary>Per-player, persisted discovery state for the three intentionally long Emberfall
    /// vistas. Ordinary traversal stays enclosed; reaching these authored summits is rewarded.</summary>
    public sealed class VistaProgress : MonoBehaviour
    {
        readonly HashSet<string> discovered = new();
        public IEnumerable<string> Discovered => discovered;
        public bool Discover(string id) => !string.IsNullOrEmpty(id) && discovered.Add(id);
        public void Restore(IEnumerable<string> ids)
        {
            discovered.Clear(); if (ids == null) return;
            foreach (var id in ids) if (!string.IsNullOrEmpty(id)) discovered.Add(id);
        }
    }

    [RequireComponent(typeof(BoxCollider))]
    public sealed class VistaDiscovery : MonoBehaviour
    {
        [SerializeField] string vistaId;
        [SerializeField] string displayName;
        [SerializeField] int goldReward = 100;

        public void Configure(string id, string label, int gold)
        {
            vistaId = id; displayName = label; goldReward = gold;
            var box = GetComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(10f, 7f, 10f);
        }

        void OnTriggerEnter(Collider other)
        {
            var progress = other.GetComponentInParent<VistaProgress>();
            if (progress == null || !progress.Discover(vistaId)) return;
            other.GetComponentInParent<Items.Wallet>()?.Add(goldReward);
            Debug.Log($"VISTA DISCOVERED: {displayName} (+{goldReward} gold)");
        }
    }
}
