using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// A functional townsperson: solid (you cannot walk through them) and
    /// talkable (press E for a rotating bark line). The city should feel like
    /// people live in it, not like scenery you clip through.
    /// </summary>
    public class TownsfolkNPC : Interactable
    {
        [SerializeField] private string displayName = "Townsperson";
        [SerializeField]
        private string[] barks =
        {
            "Fine day, when the ash stays south.",
            "The lake fish bite best at dusk.",
            "Stay clear of the west woods, goblins about.",
        };

        private int nextBark;

        public string DisplayName { get => displayName; set => displayName = value; }
        public string[] Barks { get => barks; set => barks = value; }

        protected override void OnEnable()
        {
            base.OnEnable();
            Prompt = $"Talk to {displayName}";
        }

        public override void Interact(GameObject player)
        {
            if (barks == null || barks.Length == 0) return;
            string line = barks[nextBark % barks.Length];
            nextBark++;
            CombatEvents.RaiseStatus(transform.position + Vector3.up * 2.2f,
                $"{displayName}: {line}", UI.UITheme.TextGold);
        }
    }
}
