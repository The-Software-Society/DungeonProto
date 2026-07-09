using UnityEngine;

namespace AldenFall.World
{
    /// <summary>A campfire where raw fish become dinner (or charcoal).</summary>
    public class Campfire : Interactable
    {
        private void Reset() => Prompt = "Cook";

        public override void Interact(GameObject player)
        {
            player.GetComponent<Player.CookingController>()?.StartCooking(this);
        }
    }
}
