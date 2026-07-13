using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// The Forgemaster: an in-world way to combine +X gear. Press E at his anvil
    /// and he opens the forge panel (the same system as F7, but discoverable and
    /// diegetic: the city forge is now FUNCTIONAL, not flavour).
    /// </summary>
    public class ForgeNPC : Interactable
    {
        [SerializeField] private string displayName = "Brammon the Forgemaster";

        protected override void OnEnable()
        {
            base.OnEnable();
            Prompt = $"Forge with {displayName}";
        }

        public override void Interact(GameObject player)
        {
            var panel = player.GetComponent<UI.ForgePanel>();
            if (panel == null) return;
            // DIRECT typed call, NOT SendMessage: a broadcast "Toggle" would also fire the
            // identically-named Toggle() on the F9 DebugCheatPanel sharing this player object,
            // which then renders on top of the forge (the "E opens dev panels" bug).
            panel.Open(transform);
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f,
                $"{displayName}: Two of a kind and some coin, and I will make them one better.",
                UI.UITheme.TextGold);
        }
    }
}
