using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// The market vendor: press E to open the buy/sell <see cref="UI.VendorPanel"/>
    /// on the interacting player. Purely local-economy (Wallet + Inventory are
    /// client-side), so no networking needed, same model as the F7 Forge.
    /// </summary>
    public class VendorNPC : Interactable
    {
        [SerializeField] private string displayName = "Marla the Trader";
        [SerializeField] private string[] stock =
        {
            "cooked_fish", "cooked_trout", "fishing_pole",
            "leather_cap", "iron_helm", "apprentice_focus",
        };

        public string DisplayName { get => displayName; set => displayName = value; }
        public string[] Stock { get => stock; set => stock = value; }

        protected override void OnEnable()
        {
            base.OnEnable();
            Prompt = $"Trade with {displayName}";
        }

        public override void Interact(GameObject player)
        {
            var panel = player.GetComponent<UI.VendorPanel>();
            if (panel == null) return;
            panel.Open(transform, stock);
            Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f,
                $"{displayName}: Have a look, finest goods this side of the volcano.", UI.UITheme.TextGold);
        }
    }
}
