using UnityEngine;

namespace AldenFall.World
{
    /// <summary>A patch of shoreline where the pond can be fished.</summary>
    public class FishingSpot : Interactable
    {
        [SerializeField] private Vector3 bobberAnchor;

        /// <summary>World position the bobber floats at (a couple units into the water).</summary>
        public Vector3 BobberAnchor
        {
            get => bobberAnchor;
            set => bobberAnchor = value;
        }

        private void Reset() => Prompt = "Fish";

        public override void Interact(GameObject player)
        {
            player.GetComponent<Player.FishingController>()?.StartFishing(this);
        }
    }
}
