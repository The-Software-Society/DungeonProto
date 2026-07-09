using System.Collections;
using UnityEngine;

namespace AldenFall.Items
{
    /// <summary>Grants and equips the starting gear on spawn.</summary>
    [RequireComponent(typeof(Inventory), typeof(Equipment))]
    public class StarterLoadout : MonoBehaviour
    {
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

            // You begin with one terrible sword and a fishing pole — everything
            // better comes from the world.
            if (!inventory.Contains(starterWeapon) && inventory.Add(starterWeapon))
                equipment?.Equip(starterWeapon);
            if (!inventory.Contains(starterTool))
                inventory.Add(starterTool);
        }
    }
}
