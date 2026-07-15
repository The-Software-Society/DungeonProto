using UnityEngine;
using TMPro;

namespace AldenFall.UI
{
    /// <summary>
    /// Panel toggle input, isolated in its own component so no other HUD code
    /// can break it. C toggles the equipment (character) panel, I/B the inventory
    /// panel, K the skills panel, T the combat skill trees, Escape closes all.
    /// </summary>
    public class PanelHotkeys : MonoBehaviour
    {
        public SlidePanel characterPanel;   // equipment / paper doll
        public SlidePanel inventoryPanel;   // bag
        public SlidePanel skillsPanel;
        public SlidePanel treesPanel;

        private void Start()
        {
            if (treesPanel != null)
                treesPanel.shownPos = new Vector2(0, 365);

            var panelButtons = transform.Find("PanelButtons") as RectTransform;
            if (panelButtons != null)
                panelButtons.anchoredPosition = new Vector2(-160, 52);

            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "Header" && text.text == "COMBAT TREES")
                {
                    text.text = "COMBAT TREES (T)";
                    break;
                }
            }
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping)
                return;

            if (Input.GetKeyDown(KeyCode.C))
                ToggleCharacter();

            if (Input.GetKeyDown(KeyCode.I) || Input.GetKeyDown(KeyCode.B))
                ToggleInventory();

            if (Input.GetKeyDown(KeyCode.K))
                ToggleSkills();

            if (Input.GetKeyDown(KeyCode.T))
                ToggleTrees();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                characterPanel?.Set(false);
                inventoryPanel?.Set(false);
                skillsPanel?.Set(false);
                treesPanel?.Set(false);
            }
        }

        public void ToggleCharacter() => characterPanel?.Toggle();
        public void ToggleInventory() => inventoryPanel?.Toggle();
        public void ToggleSkills() => skillsPanel?.Toggle();
        public void ToggleTrees() => treesPanel?.Toggle();
    }
}
