using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AldenFall.Combat;
using AldenFall.Multiplayer;

namespace AldenFall.UI
{
    /// <summary>
    /// The three-button combat-style selector (Attack/Strength/Defence) beside
    /// the action bar. Highlights the active style and stays in sync with the
    /// V-key cycle on CombatStyleController.
    /// </summary>
    public class CombatStyleUI : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            public CombatStyle style;
            public Image border;
            public Image icon;
        }

        public List<Entry> entries = new List<Entry>();

        private CombatStyleController controller;

        private void Start()
        {
            TryBindController();
            Refresh();
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.OnChanged -= OnStyleChanged;
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
                controller.OnChanged -= OnStyleChanged;
            controller = found;
            controller.OnChanged += OnStyleChanged;
            Refresh();
        }

        /// <summary>Wired to buttons by HUDBuilder (method groups serialize).</summary>
        public void SelectAttack() => Select(CombatStyle.Attack);
        public void SelectStrength() => Select(CombatStyle.Strength);
        public void SelectDefence() => Select(CombatStyle.Defence);

        private void Select(CombatStyle style)
        {
            controller?.Set(style);
            Refresh();
        }

        private void Refresh()
        {
            var active = controller != null ? controller.Style : CombatStyle.Attack;
            foreach (var entry in entries)
            {
                bool on = entry.style == active;
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
