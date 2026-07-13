using System.Collections;
using UnityEngine;

namespace AldenFall.Items
{
    /// <summary>
    /// Grants and equips the starting gear on spawn. Path-aware: once the intro
    /// weapon choice is persisted (see UI.WeaponChoicePanel.PathKey), a magic
    /// player relogs into wand + focus, not the default rusty sword, inventory
    /// does not persist between sessions, so this IS the relog kit.
    /// </summary>
    [RequireComponent(typeof(Inventory), typeof(Equipment))]
    public class StarterLoadout : MonoBehaviour
    {
        // Mirrors UI.WeaponChoicePanel.PathKey / its starter item ids (Items
        // deliberately doesn't reference the UI namespace).
        private const string StarterPathKey = "starter_path_v1";
        private const string MagicWand = "apprentice_wand";
        private const string MagicFocus = "apprentice_focus";
        private const string Bow = "short_bow";
        private const string Arrows = "arrow";
        private const int ArrowCount = 60;

        [SerializeField] private string starterWeapon = "rusty_sword";
        [SerializeField] private string starterTool = "fishing_pole";

        private bool granted;

        private IEnumerator Start()
        {
            // Wait a frame so the visual hierarchy (hand bones) exists
            yield return null;
            EnsureGranted();
        }

        public void EnsureGranted()
        {
            if (granted) return;
            granted = true;

            var inventory = GetComponent<Inventory>();
            var equipment = GetComponent<Equipment>();
            if (inventory == null) return;

            // You begin with one terrible weapon set and a fishing pole, everything
            // better comes from the world.
            string path = PlayerPrefs.GetString(StarterPathKey, "");
            if (path == "magic")
            {
                GrantAndEquip(inventory, equipment, MagicWand);   // main hand
                GrantAndEquip(inventory, equipment, MagicFocus);  // off hand
            }
            else if (path == "archery")
            {
                GrantAndEquip(inventory, equipment, Bow);         // main hand
                if (inventory.CountOf(Arrows) < ArrowCount)       // top the quiver back up on relog
                    for (int i = inventory.CountOf(Arrows); i < ArrowCount; i++) inventory.Add(Arrows);
            }
            else
            {
                GrantAndEquip(inventory, equipment, starterWeapon);
            }
            if (!inventory.Contains(starterTool))
                inventory.Add(starterTool);
        }

        /// <summary>Grant once and equip; no duplicates if it's already in hand or bag.</summary>
        private static void GrantAndEquip(Inventory inventory, Equipment equipment, string itemId)
        {
            if (equipment != null && equipment.IsEquipped(itemId)) return;
            if (!inventory.Contains(itemId) && !inventory.Add(itemId)) return;
            equipment?.Equip(itemId);
        }
    }
}
