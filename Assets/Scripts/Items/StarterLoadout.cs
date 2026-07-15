using System.Collections;
using UnityEngine;

namespace AldenFall.Items
{
    /// <summary>
    /// Grants the starting gear on spawn. Aldric hands you ALL THREE weapon
    /// families (blade, wand + focus, bow) plus a full quiver, so you can explore
    /// whatever playstyle you like, the action bar auto-swaps to match whatever you
    /// draw. Inventory does not persist between sessions for pure clients, so this
    /// IS the relog kit; the blade is equipped by default (melee bar active).
    /// </summary>
    [RequireComponent(typeof(Inventory), typeof(Equipment))]
    public class StarterLoadout : MonoBehaviour
    {
        private const string MagicWand = "apprentice_wand";
        private const string MagicFocus = "apprentice_focus";
        private const string Bow = "short_bow";
        private const string Arrows = "arrow";
        private const int ArrowCount = 100;

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

            // All three families in the bag; the blade goes to the main hand as the
            // default, everything better comes from the world.
            GrantAndEquip(inventory, equipment, starterWeapon);   // blade -> main hand (equipped)
            GrantOnly(inventory, equipment, MagicWand);            // in the bag, draw to go magic
            GrantOnly(inventory, equipment, MagicFocus);
            GrantOnly(inventory, equipment, Bow);                 // in the bag, draw to go archery
            if (inventory.CountOf(Arrows) < ArrowCount)            // top the quiver back up on relog
                for (int i = inventory.CountOf(Arrows); i < ArrowCount; i++) inventory.Add(Arrows);

            if (!inventory.Contains(starterTool))
                inventory.Add(starterTool);
        }

        /// <summary>Put an item in the bag once, without equipping it (skip if already held/worn).</summary>
        private static void GrantOnly(Inventory inventory, Equipment equipment, string itemId)
        {
            if (equipment != null && equipment.IsEquipped(itemId)) return;
            if (!inventory.Contains(itemId)) inventory.Add(itemId);
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
