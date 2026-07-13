using UnityEngine;
using AldenFall.Quests;

namespace AldenFall.World
{
    /// <summary>
    /// A world marker the player presses E on to advance an InspectMarker quest
    /// objective (seal stones, dead-traveler markers, the empty grave). markerId
    /// is matched against the objective's TargetId; an objective with an empty
    /// TargetId accepts any marker.
    /// </summary>
    public class InspectableMarker : Interactable
    {
        [SerializeField] private string markerId = "";
        [SerializeField] private string inspectText = "You examine it closely...";

        public string MarkerId { get => markerId; set => markerId = value; }
        public string InspectText { get => inspectText; set => inspectText = value; }

        public override void Interact(GameObject player)
        {
            var log = player != null ? player.GetComponent<QuestLog>() : null;
            log?.Advance(ObjectiveKind.InspectMarker, markerId);
            if (!string.IsNullOrEmpty(inspectText))
                Combat.CombatEvents.RaiseStatus(transform.position + Vector3.up * 2f, inspectText, UI.UITheme.TextGold);
        }
    }
}
