using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Progression;

namespace AldenFall.UI
{
    /// <summary>
    /// Combat skill trees: one tab per combat skill (Attack/Strength/Defence/
    /// Magic), abilities grouped by weapon class underneath. Tabs and tiles
    /// are built by HUDBuilder from AbilityDatabase, adding a weapon branch
    /// later is data, not UI code. Drag tiles onto the action bar.
    /// </summary>
    public class SkillTreePanelUI : MonoBehaviour
    {
        [System.Serializable]
        public class Tab
        {
            public SkillType tree;
            public Button button;
            public Image buttonBg;
            public TMP_Text label;
            public Image underline;      // accent bar under the active tab (optional)
            public GameObject content;   // the tree page this tab shows
        }

        public List<Tab> tabs = new List<Tab>();

        private SkillType active = SkillType.Attack;

        private void Start() => Select(active);

        /// <summary>Wired to tab buttons by HUDBuilder (method group per tree).</summary>
        public void SelectAttack() => Select(SkillType.Attack);
        public void SelectStrength() => Select(SkillType.Strength);
        public void SelectDefence() => Select(SkillType.Defence);
        public void SelectMagic() => Select(SkillType.Magic);
        public void SelectRanged() => Select(SkillType.Ranged);

        public void Select(SkillType tree)
        {
            active = tree;
            foreach (var tab in tabs)
            {
                bool on = tab.tree == tree;
                if (tab.content != null) tab.content.SetActive(on);
                if (tab.buttonBg != null)
                    tab.buttonBg.color = on ? UITheme.PanelBgLight : new Color(0, 0, 0, 0.35f);
                if (tab.label != null)
                    tab.label.color = on ? UITheme.TextGold : UITheme.TextDim;
                if (tab.underline != null)
                    tab.underline.enabled = on;
            }
        }
    }
}
