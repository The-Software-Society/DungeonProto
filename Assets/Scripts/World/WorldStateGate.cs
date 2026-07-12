using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.World
{
    /// <summary>
    /// A sealed barrier that opens when the world reaches a given
    /// <see cref="EmberfallWorldState.Phase"/> — the Floor-2 stairway gate opens
    /// on Cleared. Like <see cref="SealableDoor"/> it is deliberately
    /// NON-networked: the phase itself is the replicated state, so every machine
    /// observes the same transition and opens its local copy in lock-step.
    /// </summary>
    public class WorldStateGate : MonoBehaviour
    {
        [SerializeField] private EmberfallWorldState.Phase opensAt = EmberfallWorldState.Phase.Cleared;
        [SerializeField] private string openMessage = "Ancient stonework grinds open...";

        private bool opened;
        private bool subscribed;

        public void Configure(EmberfallWorldState.Phase phase, string message)
        {
            opensAt = phase;
            openMessage = message;
        }

        // The world-state singleton is a scene network object that may activate after
        // this gate, so keep trying to subscribe rather than assuming order in Start.
        private void Update()
        {
            if (opened) return;

            var world = EmberfallWorldState.Instance;
            if (world == null) return;

            if (!subscribed)
            {
                world.OnPhaseChanged += HandlePhase;
                subscribed = true;
            }

            // Catch up if the phase was already reached before we subscribed
            HandlePhase(world.CurrentPhase);
        }

        private void OnDestroy()
        {
            if (subscribed && EmberfallWorldState.Instance != null)
                EmberfallWorldState.Instance.OnPhaseChanged -= HandlePhase;
        }

        private void HandlePhase(EmberfallWorldState.Phase phase)
        {
            if (opened || phase != opensAt) return;
            opened = true;

            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
            foreach (var rend in GetComponentsInChildren<Renderer>())
                rend.enabled = false;

            CombatEvents.RaiseStatus(transform.position + Vector3.up * 3f,
                openMessage, UI.UITheme.TextGold);
        }
    }
}
