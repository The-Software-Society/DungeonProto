using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// A craft station (anvil for Smithing, bench for Fletching). Press E to have
    /// the player's CraftingController work through the recipes for this station id.
    /// </summary>
    public class CraftingStation : Interactable
    {
        [SerializeField] private string station = "anvil";

        public string Station { get => station; set => station = value; }

        public override void Interact(GameObject player)
        {
            if (player == null) return;
            player.GetComponent<Player.CraftingController>()?.StartCraft(station);
        }
    }
}
