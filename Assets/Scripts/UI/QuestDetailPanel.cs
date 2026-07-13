using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;
using AldenFall.Quests;

namespace AldenFall.UI
{
    /// <summary>
    /// The quest DETAIL window: the "working window where we accept the quest, it tells us
    /// what we need to do and what we get" (v9 feedback). Opened when you talk to a quest
    /// giver (with an ACCEPT / TURN IN action) and when you click a tracker entry (read-only).
    /// Shows the story, the objectives with live progress, and the full reward preview
    /// (gold + items + reward-choice). Lives on the player (added via
    /// EnsureGameplayComponents); sizes itself to content so it never overflows. ESC or the
    /// Close button dismisses it; accepting/handing in routes through the normal QuestLog.
    /// </summary>
    public class QuestDetailPanel : MonoBehaviour
    {
        private const float PanelWidth = 620f;
        private const float Pad = 22f;
        private const float ContentWidth = PanelWidth - Pad * 2f;

        private QuestLog log;
        private GameObject panelRoot;
        private RectTransform panel;
        private RectTransform content;
        private string currentQuestId;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            log = GetComponent<QuestLog>();
        }

        /// <summary>Open the window for a quest. Shows an ACCEPT button if it can be accepted,
        /// a TURN IN button if its objectives are done, otherwise read-only progress.</summary>
        public void OpenFor(string questId)
        {
            if (log == null) log = GetComponent<QuestLog>();
            var def = QuestDatabase.Get(questId);
            if (def == null || log == null) return;
            currentQuestId = questId;
            if (panelRoot == null) BuildCanvas();
            panelRoot.SetActive(true);
            Rebuild(def);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        private void BuildCanvas()
        {
            var canvasObj = new GameObject("QuestDetailCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 270;   // above HUD + vendor/forge (260), below the first-clear banner
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasObj.AddComponent<GraphicRaycaster>();
            panelRoot = canvasObj;

            panel = PanelChrome.CreatePanel(canvasObj.transform, "QuestPanel", new Vector2(PanelWidth, 460f));
            content = PanelChrome.Item(panel, "Content", 0f, 0f, PanelWidth, 460f);
        }

        private void Rebuild(QuestDef def)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            var status = log.StatusOf(def.Id);
            bool acceptable = log.CanAccept(def.Id);
            bool turnInable = log.IsComplete(def.Id);

            string caption = turnInable ? "Ready to hand in"
                : status == QuestStatus.Active ? "In progress"
                : status == QuestStatus.TurnedIn ? "Completed"
                : acceptable ? "New quest" : "Locked";

            PanelChrome.Header(content, def.Name, caption);

            float y = PanelChrome.HeaderHeight + 6f;

            // Story
            y = AddWrapped(def.Description, y, UITheme.TextMain, UITheme.BodySize);
            y += 6f;

            // Objectives
            PanelChrome.Section(content, "ObjLabel", "Objectives", Pad, y, ContentWidth);
            y += 24f;
            for (int i = 0; i < def.Objectives.Count; i++)
            {
                var o = def.Objectives[i];
                int prog = Mathf.Min(log.ProgressOf(def.Id, i), Mathf.Max(1, o.Count));
                bool done = status != QuestStatus.NotStarted && prog >= o.Count;
                string count = o.Count > 1 ? $"  <color={UITheme.DimHex}>{prog}/{o.Count}</color>" : "";
                string mark = done ? "<color=#5ED68C>[x]</color>" : "<color=#8A8494>[ ]</color>";
                y = AddWrapped($"{mark} {o.Description}{count}", y, UITheme.TextMain, UITheme.BodySize);
                if (!string.IsNullOrEmpty(o.LocationHint))
                    y = AddWrapped($"<size=85%><i><color={UITheme.DimHex}>{o.LocationHint}</color></i></size>", y, UITheme.TextDim, UITheme.BodySize);
            }
            y += 6f;

            // Rewards
            PanelChrome.Section(content, "RewLabel", "Rewards", Pad, y, ContentWidth);
            y += 24f;
            var rew = new StringBuilder();
            if (def.GoldReward > 0) rew.Append($"<color={UITheme.GoldHex}>{def.GoldReward} gold</color>");
            foreach (var r in def.Rewards)
            {
                if (rew.Length > 0) rew.Append("   ");
                var idef = ItemDatabase.Get(r.itemId);
                rew.Append($"{(idef != null ? idef.Name : r.itemId)}{(r.count > 1 ? $" x{r.count}" : "")}");
            }
            if (rew.Length == 0 && def.RewardChoices.Count == 0) rew.Append("<color=#8A8494>-</color>");
            y = AddWrapped(rew.ToString(), y, UITheme.TextMain, UITheme.BodySize);
            if (def.RewardChoices.Count > 0)
            {
                y = AddWrapped($"<color={UITheme.DimHex}>Choose one on completion:</color>", y, UITheme.TextDim, UITheme.BodySize);
                foreach (var c in def.RewardChoices)
                    y = AddWrapped($"  - <b>{c.label}</b> <size=85%><color={UITheme.DimHex}>{c.description}</color></size>", y, UITheme.TextMain, UITheme.BodySize);
            }
            y += 10f;

            // Action buttons
            float btnY = y;
            if (acceptable)
                PanelChrome.MakeButton(content, "Accept", "ACCEPT", Pad, btnY, 150f, () => { AcceptCurrent(def); }, primary: true);
            else if (turnInable)
                PanelChrome.MakeButton(content, "TurnIn", "TURN IN", Pad, btnY, 150f, () => { TurnInCurrent(def); }, primary: true);
            PanelChrome.MakeButton(content, "Close", "CLOSE", PanelWidth - Pad - 130f, btnY, 130f, Close);
            y += UITheme.ButtonHeight + Pad;

            // Size the panel to its content so nothing spills over the edge.
            panel.sizeDelta = new Vector2(PanelWidth, Mathf.Max(y, 200f));
        }

        private void AcceptCurrent(QuestDef def)
        {
            if (log.Accept(def.Id))
            {
                if (!string.IsNullOrEmpty(def.GiverNpcId))
                    log.Advance(ObjectiveKind.TalkToNpc, def.GiverNpcId);
                Rebuild(def);   // refresh into in-progress view
            }
        }

        private void TurnInCurrent(QuestDef def)
        {
            if (log.TurnIn(def.Id)) Close();   // RewardChoicePanel (if any) takes over
        }

        /// <summary>Add a wrapping text block at running y; returns the new y below it.</summary>
        private float AddWrapped(string text, float y, Color color, float size)
        {
            if (string.IsNullOrEmpty(text)) return y;
            var rect = PanelChrome.Item(content, "L" + y.ToString("F0"), Pad, y, ContentWidth, 20f);
            var t = rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            t.ForceMeshUpdate();
            float h = Mathf.Max(20f, t.preferredHeight);
            rect.sizeDelta = new Vector2(ContentWidth, h);
            return y + h + 3f;
        }
    }
}
