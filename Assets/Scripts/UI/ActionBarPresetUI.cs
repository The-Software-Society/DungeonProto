using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Abilities;
using AldenFall.Multiplayer;

namespace AldenFall.UI
{
    /// <summary>
    /// The preset switcher sitting beside the action bar: up/down arrows cycle
    /// through the bars (up to five), a label shows "BAR n/m", and an [X] chip
    /// shows and rebinds which weapon family auto-swaps to the current bar. Reads
    /// and drives the player's ActionBar; stays in sync via OnChanged.
    /// </summary>
    public class ActionBarPresetUI : MonoBehaviour
    {
        public TMP_Text barLabel;      // "BAR 1/3"
        public TMP_Text triggerLabel;  // "AUTO: MELEE" / "AUTO: —"
        public Button upButton;
        public Button downButton;
        public Button triggerButton;   // the [X] rebind chip

        private ActionBar actionBar;

        private void Start()
        {
            if (upButton != null) upButton.onClick.AddListener(OnUp);
            if (downButton != null) downButton.onClick.AddListener(OnDown);
            if (triggerButton != null) triggerButton.onClick.AddListener(OnCycleTrigger);
            TryBind();
            Refresh();
        }

        private void OnDestroy()
        {
            if (actionBar != null) actionBar.OnChanged -= Refresh;
        }

        private void Update()
        {
            if (actionBar == null) TryBind();
        }

        private void TryBind()
        {
            var player = LocalPlayerLocator.Find();
            var found = player != null ? player.GetComponent<ActionBar>() : null;
            if (found == null || found == actionBar) return;

            if (actionBar != null) actionBar.OnChanged -= Refresh;
            actionBar = found;
            actionBar.OnChanged += Refresh;
            Refresh();
        }

        private void OnUp() { actionBar?.PrevPreset(); }
        private void OnDown() { actionBar?.NextPreset(); }
        private void OnCycleTrigger() { actionBar?.CycleActiveTrigger(); }

        private static string TriggerName(WeaponCategory c) => c switch
        {
            WeaponCategory.Melee => "MELEE",
            WeaponCategory.Magic => "MAGIC",
            WeaponCategory.Archery => "ARCHERY",
            _ => "—",   // em dash: no auto-swap
        };

        private void Refresh()
        {
            if (actionBar == null) return;
            if (barLabel != null)
                barLabel.text = $"BAR {actionBar.ActivePreset + 1}/{actionBar.PresetCount}";
            if (triggerLabel != null)
                triggerLabel.text = $"AUTO: {TriggerName(actionBar.TriggerOf(actionBar.ActivePreset))}";
        }
    }
}
