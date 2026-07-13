using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Quests;

namespace AldenFall.UI
{
    /// <summary>
    /// Quest UI, built at runtime on its own canvas (like DebugCheatPanel) so it
    /// never touches the authored HUD. Two surfaces:
    ///   • a MINIMAL always-on tracker (top-right): no panel, one small line per
    ///     active quest (max 2), "◆ name, current objective", so it never
    ///     covers gameplay;
    ///   • a full quest log toggled with <b>Q</b> that holds ALL the detail:
    ///     descriptions, every objective, direction hints, completed history.
    /// Lives on the player; reads the sibling QuestLog and redraws on change.
    /// Gold now lives in the inventory/character panel, not here.
    /// </summary>
    public class QuestTrackerHUD : MonoBehaviour
    {
        private QuestLog log;
        private TMP_Text body;          // compact always-on tracker
        private GameObject logPanel;    // full Q-toggled log
        private TMP_Text logBody;
        private string topQuestId;      // the first tracked quest, opened when the tracker is clicked

        private void Start()
        {
            // Only the local player drives quest UI + input (remote avatars must not).
            var identity = GetComponent<Mirror.NetworkIdentity>();
            if (identity != null && !identity.isLocalPlayer) { enabled = false; return; }

            log = GetComponent<QuestLog>();
            if (log == null) { enabled = false; return; }

            Build();
            BuildLogPanel();

            log.OnQuestChanged.AddListener(Refresh);
            Refresh();
        }

        private void OnDestroy()
        {
            if (log != null) log.OnQuestChanged.RemoveListener(Refresh);
        }

        private void Update()
        {
            if (InputFocusGuard.IsTyping) return;
            if (Input.GetKeyDown(KeyCode.Q))
                ToggleLog();
        }

        private void ToggleLog()
        {
            if (logPanel == null) return;
            bool show = !logPanel.activeSelf;
            logPanel.SetActive(show);
            if (show) RefreshLog();
        }

        /// <summary>Clicking the tracker opens the full quest DETAIL window for the top quest.</summary>
        private void OpenTopQuest()
        {
            if (string.IsNullOrEmpty(topQuestId)) return;
            GetComponent<QuestDetailPanel>()?.OpenFor(topQuestId);
        }

        // ==================== BUILD ====================

        private Canvas canvas;

        private void Build()
        {
            var canvasObj = new GameObject("QuestTrackerCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Minimal tracker: bare text, no panel, so it never covers gameplay.
            var textObj = new GameObject("Body").AddComponent<RectTransform>();
            textObj.SetParent(canvasObj.transform, false);
            textObj.anchorMin = textObj.anchorMax = new Vector2(1f, 1f);
            textObj.pivot = new Vector2(1f, 1f);
            textObj.anchoredPosition = new Vector2(-24f, -150f);
            textObj.sizeDelta = new Vector2(460f, 60f);

            body = textObj.gameObject.AddComponent<TextMeshProUGUI>();
            body.fontSize = 13;
            body.color = UITheme.TextMain;
            body.alignment = TextAlignmentOptions.TopRight;
            body.raycastTarget = true;   // clickable: opens the detail window for the top quest
            body.enableWordWrapping = false;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            var trackBtn = textObj.gameObject.AddComponent<Button>();
            trackBtn.transition = Selectable.Transition.None;
            trackBtn.onClick.AddListener(OpenTopQuest);
            body.overflowMode = TextOverflowModes.Truncate;   // a long line clips, never sprawls
            // Thin dark outline keeps the bare text readable over bright terrain.
            body.outlineWidth = 0.16f;
            body.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        }

        private void BuildLogPanel()
        {
            if (canvas == null) return;

            // Log panels need clicks blocked from the world underneath.
            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            var panel = PanelChrome.CreatePanel(canvas.transform, "QuestLogPanel",
                new Vector2(680f, 560f));
            logPanel = panel.gameObject;

            PanelChrome.Header(panel, "Quest Log", "Q closes");

            var body = PanelChrome.Item(panel, "LogBody", UITheme.Pad,
                PanelChrome.HeaderHeight + 8f, 680f - UITheme.Pad * 2f,
                560f - PanelChrome.HeaderHeight - 8f - UITheme.Pad);
            logBody = body.gameObject.AddComponent<TextMeshProUGUI>();
            logBody.fontSize = UITheme.BodySize;
            logBody.color = UITheme.TextMain;
            logBody.alignment = TextAlignmentOptions.TopLeft;
            logBody.raycastTarget = false;
            logBody.enableWordWrapping = true;

            logPanel.SetActive(false);
        }

        // ==================== COMPACT TRACKER ====================

        private void Refresh() => Refresh(0);

        private void Refresh(int _)
        {
            if (body != null)
            {
                // One compact line per active quest, max 2, everything else
                // (descriptions, hints, completed history) lives in the Q log.
                const int MaxTracked = 2;
                var sb = new StringBuilder();
                int shown = 0;
                topQuestId = null;

                foreach (var def in QuestDatabase.All)
                {
                    if (shown >= MaxTracked) break;
                    var status = log.StatusOf(def.Id);
                    if (status != QuestStatus.Active && status != QuestStatus.Complete) continue;
                    if (topQuestId == null) topQuestId = def.Id;

                    string tail;
                    if (status == QuestStatus.Complete)
                        tail = "<color=#5ED68C>ready to hand in</color>";
                    else
                    {
                        int cur = CurrentObjective(def);
                        var obj = cur >= 0 ? def.Objectives[cur] : null;
                        string count = obj != null && obj.Count > 1
                            ? $" {Mathf.Min(log.ProgressOf(def.Id, cur), obj.Count)}/{obj.Count}"
                            : "";
                        tail = $"<color=#C8C2CE>{obj?.Description}{count}</color>";
                    }

                    if (shown > 0) sb.Append('\n');
                    sb.Append($"<color=#F2CC8C>◆</color> <b>{def.Name}</b>, {tail}");
                    shown++;
                }

                if (shown == 0)
                    sb.Append("<color=#8A8494>No quest, find Aldric</color>");

                body.text = sb.ToString();
            }

            // Keep the open log fresh as objectives tick over.
            if (logPanel != null && logPanel.activeSelf) RefreshLog();
        }

        /// <summary>Index of the first unfinished objective, or -1 if all are done.</summary>
        private int CurrentObjective(QuestDef def)
        {
            for (int i = 0; i < def.Objectives.Count; i++)
                if (log.ProgressOf(def.Id, i) < def.Objectives[i].Count) return i;
            return -1;
        }

        // ==================== FULL LOG ====================

        private void RefreshLog()
        {
            if (logBody == null) return;

            var active = new StringBuilder();
            var done = new StringBuilder();
            int activeCount = 0, doneCount = 0;

            foreach (var def in QuestDatabase.All)
            {
                var status = log.StatusOf(def.Id);

                if (status == QuestStatus.Active || status == QuestStatus.Complete)
                {
                    activeCount++;
                    string tag = status == QuestStatus.Complete ? " <color=#5ED68C>(ready to hand in)</color>" : "";
                    active.Append($"<b>{def.Name}</b>{tag}\n");
                    if (!string.IsNullOrEmpty(def.Description))
                        active.Append($"<size=12><color=#9A93A4>{def.Description}</color></size>\n");

                    int cur = CurrentObjective(def);
                    for (int i = 0; i < def.Objectives.Count; i++)
                    {
                        var obj = def.Objectives[i];
                        int have = log.ProgressOf(def.Id, i);
                        bool objDone = have >= obj.Count;
                        string mark = objDone ? "<color=#5ED68C>[x]</color>" : "[ ]";
                        string count = obj.Count > 1 ? $"  {Mathf.Min(have, obj.Count)}/{obj.Count}" : "";
                        active.Append($"  {mark} <color=#C8C2CE>{obj.Description}{count}</color>\n");
                        // Direction hint on the current objective so the player knows where to go.
                        if (i == cur && !string.IsNullOrEmpty(obj.LocationHint))
                            active.Append($"      <color=#8FB8E8>→ {obj.LocationHint}</color>\n");
                    }
                    active.Append("\n");
                }
                else if (status == QuestStatus.TurnedIn)
                {
                    doneCount++;
                    done.Append($"<color=#7FA88C>[x] {def.Name}</color>\n");
                }
            }

            var sb = new StringBuilder();
            sb.Append("<size=12><cspace=3><color=#F2CC8C><b>ACTIVE</b></color></cspace></size>\n\n");
            sb.Append(activeCount > 0 ? active.ToString()
                                      : "<color=#8A8494>No active quests. Seek out Aldric in the clearing.</color>\n\n");
            sb.Append("<size=12><cspace=3><color=#A69BB0><b>COMPLETED</b></color></cspace></size>\n\n");
            sb.Append(doneCount > 0 ? done.ToString()
                                    : "<color=#8A8494>None yet.</color>");

            logBody.text = sb.ToString();
        }
    }
}
