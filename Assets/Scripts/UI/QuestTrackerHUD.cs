using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Items;
using AldenFall.Quests;

namespace AldenFall.UI
{
    /// <summary>
    /// A self-contained quest tracker + gold readout, built at runtime on its own
    /// canvas (like DebugCheatPanel) so it never touches the authored HUD. Lives
    /// on the player; reads the sibling QuestLog + Wallet and redraws on change.
    /// </summary>
    public class QuestTrackerHUD : MonoBehaviour
    {
        private QuestLog log;
        private Wallet wallet;
        private TMP_Text body;

        private void Start()
        {
            log = GetComponent<QuestLog>();
            wallet = GetComponent<Wallet>();
            if (log == null) { enabled = false; return; }

            Build();

            log.OnQuestChanged.AddListener(Refresh);
            if (wallet != null) wallet.OnChanged.AddListener(_ => Refresh());
            Refresh();
        }

        private void OnDestroy()
        {
            if (log != null) log.OnQuestChanged.RemoveListener(Refresh);
        }

        private void Build()
        {
            var canvasObj = new GameObject("QuestTrackerCanvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panel = new GameObject("Panel").AddComponent<RectTransform>();
            panel.SetParent(canvasObj.transform, false);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 1f);
            panel.pivot = new Vector2(1f, 1f);
            panel.anchoredPosition = new Vector2(-24f, -150f);
            panel.sizeDelta = new Vector2(360f, 260f);

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = UITheme.PanelBg;

            var textObj = new GameObject("Body").AddComponent<RectTransform>();
            textObj.SetParent(panel, false);
            textObj.anchorMin = Vector2.zero; textObj.anchorMax = Vector2.one;
            textObj.offsetMin = new Vector2(14f, 12f); textObj.offsetMax = new Vector2(-14f, -12f);

            body = textObj.gameObject.AddComponent<TextMeshProUGUI>();
            body.fontSize = 16;
            body.color = UITheme.TextMain;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.raycastTarget = false;
            body.enableWordWrapping = true;
        }

        private void Refresh() => Refresh(0);

        private void Refresh(int _)
        {
            if (body == null) return;

            var sb = new StringBuilder();
            int gold = wallet != null ? wallet.Gold : 0;
            sb.Append($"<color=#F2CC8C><b>Gold:</b> {gold}</color>\n");
            sb.Append("<size=13><color=#A69BB0>QUESTS</color></size>\n");

            bool any = false;
            foreach (var def in QuestDatabase.All)
            {
                var status = log.StatusOf(def.Id);
                if (status != QuestStatus.Active && status != QuestStatus.Complete) continue;
                any = true;

                string tag = status == QuestStatus.Complete ? " <color=#5ED68C>(ready)</color>" : "";
                sb.Append($"\n<b>{def.Name}</b>{tag}\n");
                for (int i = 0; i < def.Objectives.Count; i++)
                {
                    var obj = def.Objectives[i];
                    int have = log.ProgressOf(def.Id, i);
                    bool done = have >= obj.Count;
                    string mark = done ? "<color=#5ED68C>[x]</color>" : "[ ]";
                    string count = obj.Count > 1 ? $"  {Mathf.Min(have, obj.Count)}/{obj.Count}" : "";
                    sb.Append($"  {mark} <color=#C8C2CE>{obj.Description}{count}</color>\n");
                }
            }
            if (!any) sb.Append("\n<color=#8A8494>No active quests. Seek out Aldric.</color>");

            body.text = sb.ToString();
        }
    }
}
