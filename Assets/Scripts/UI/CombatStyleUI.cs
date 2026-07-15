using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Combat;
using AldenFall.Multiplayer;

namespace AldenFall.UI
{
    /// <summary>
    /// The combat-stance selector beside the action bar. It is WEAPON-CATEGORY
    /// aware, matching CombatStyleController:
    ///   Melee (sword)   -> three buttons: Attack / Strength / Defence
    ///   Magic / Archery -> two buttons:   Pure / Defensive (third hidden)
    /// Highlights the active stance and stays in sync with the V-key cycle. The
    /// button at each index dispatches by the CURRENT category, so the same three
    /// baked buttons drive both stance families.
    /// </summary>
    public class CombatStyleUI : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public CombatStyle style;   // melee slot identity (index 0/1/2)
            public GameObject root;     // the whole button, toggled per category
            public Image border;
            public Image icon;
            public TMP_Text label;      // "ATK"/"STR"/"DEF" or "PURE"/"DEF"
        }

        public List<Entry> entries = new List<Entry>();

        private CombatStyleController controller;

        private static readonly string[] MeleeLabels = { "ATK", "STR", "DEF" };
        private static readonly string[] RangedLabels = { "PURE", "DEF" };

        private void Start()
        {
            TryBindController();
            Refresh();
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.OnChanged -= OnStyleChanged;
                controller.OnStanceChanged -= Refresh;
            }
        }

        private void OnStyleChanged(CombatStyle _) => Refresh();

        private void Update()
        {
            if (controller == null)
                TryBindController();
        }

        private void TryBindController()
        {
            var player = LocalPlayerLocator.Find();
            var found = player != null ? player.GetComponent<CombatStyleController>() : null;
            if (found == null || found == controller) return;

            if (controller != null)
            {
                controller.OnChanged -= OnStyleChanged;
                controller.OnStanceChanged -= Refresh;
            }
            controller = found;
            controller.OnChanged += OnStyleChanged;
            controller.OnStanceChanged += Refresh;
            Refresh();
        }

        // Wired to buttons by HUDBuilder (method groups serialize). Each dispatches
        // by the CURRENT weapon category so the same button serves both families.
        public void SelectSlot0() => SelectIndex(0);
        public void SelectSlot1() => SelectIndex(1);
        public void SelectSlot2() => SelectIndex(2);

        // Back-compat aliases (older baked scenes may reference these).
        public void SelectAttack() => SelectIndex(0);
        public void SelectStrength() => SelectIndex(1);
        public void SelectDefence() => SelectIndex(2);

        private void SelectIndex(int index)
        {
            if (controller == null) return;
            if (controller.IsRangedCategory)
            {
                if (index < 2) controller.SetStance((RangedStance)index);
            }
            else
            {
                controller.Set((CombatStyle)index);
            }
            Refresh();
        }

        private void Refresh()
        {
            bool ranged = controller != null && controller.IsRangedCategory;
            int activeIndex = controller == null ? 0
                : ranged ? (int)controller.Stance : (int)controller.Style;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                // Third button only exists for the melee family.
                bool visible = !ranged || i < 2;
                if (entry.root != null) entry.root.SetActive(visible);
                if (!visible) continue;

                if (entry.label != null)
                    entry.label.text = ranged ? RangedLabels[i] : MeleeLabels[i];

                bool on = i == activeIndex;
                if (entry.border != null)
                {
                    var c = on ? UITheme.Gold : new Color(1f, 1f, 1f, 0.12f);
                    if (on) c.a = 0.9f;
                    entry.border.color = c;
                }
                if (entry.icon != null)
                {
                    var c = entry.icon.color;
                    c.a = on ? 1f : 0.38f;
                    entry.icon.color = c;
                }
            }
        }
    }
}
