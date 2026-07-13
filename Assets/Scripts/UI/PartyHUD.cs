using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AldenFall.UI
{
    /// <summary>
    /// Minimal party corner list (top-left, its own runtime canvas like
    /// QuestTrackerHUD): one small name line + a thin HP bar per member, no panel
    /// background, so it never covers gameplay. Also hosts the party-invite / duel
    /// prompts ("&lt;name&gt; invites you to a party, Y accept / N decline") and short
    /// toasts raised by PartyMember / DuelMember on the same GameObject.
    /// </summary>
    public class PartyHUD : MonoBehaviour
    {
        private const float RowHeight = 30f;
        private const float BarWidth = 130f;
        private const float ToastSeconds = 4f;
        private const float BarRefreshInterval = 0.25f;

        private class MemberRow
        {
            public GameObject root;
            public Image fill;
            public uint netId;
            public Combat.Health health; // resolved lazily from NetworkClient.spawned
        }

        private Multiplayer.PartyMember party;
        private Combat.DuelMember duel;

        private Canvas canvas;
        private RectTransform listRoot;
        private TMP_Text promptText;
        private readonly List<MemberRow> rows = new List<MemberRow>();

        private float nextBarRefresh;
        private string toast;
        private float toastUntil;

        private void Start()
        {
            // Only the local player draws party UI (remote avatars must not).
            var identity = GetComponent<NetworkIdentity>();
            if (identity != null && !identity.isLocalPlayer) { enabled = false; return; }

            party = GetComponent<Multiplayer.PartyMember>();
            duel = GetComponent<Combat.DuelMember>();
            if (party == null && duel == null) { enabled = false; return; }

            Build();

            if (party != null)
            {
                party.Changed += OnPartyChanged;
                party.Notice += ShowToast;
            }
            if (duel != null)
            {
                duel.Changed += RefreshPrompt;
                duel.Notice += ShowToast;
            }

            RebuildRoster();
            RefreshPrompt();
        }

        private void OnDestroy()
        {
            if (party != null)
            {
                party.Changed -= OnPartyChanged;
                party.Notice -= ShowToast;
            }
            if (duel != null)
            {
                duel.Changed -= RefreshPrompt;
                duel.Notice -= ShowToast;
            }
            if (canvas != null)
                Destroy(canvas.gameObject);
        }

        private void Update()
        {
            if (toast != null && Time.time > toastUntil)
            {
                toast = null;
                RefreshPrompt();
            }

            if (Time.time < nextBarRefresh) return;
            nextBarRefresh = Time.time + BarRefreshInterval;
            RefreshBars();
        }

        private void OnPartyChanged()
        {
            RebuildRoster();
            RefreshPrompt();
        }

        private void ShowToast(string message)
        {
            toast = message;
            toastUntil = Time.time + ToastSeconds;
            RefreshPrompt();
        }

        // ==================== BUILD ====================

        private void Build()
        {
            var canvasObj = new GameObject("PartyHUDCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // Member rows anchor: bare top-left column, no background.
            listRoot = new GameObject("PartyList").AddComponent<RectTransform>();
            listRoot.SetParent(canvasObj.transform, false);
            listRoot.anchorMin = listRoot.anchorMax = new Vector2(0f, 1f);
            listRoot.pivot = new Vector2(0f, 1f);
            listRoot.anchoredPosition = new Vector2(22f, -22f);
            listRoot.sizeDelta = new Vector2(240f, RowHeight * Multiplayer.PartySystem.MaxPartySize);

            // Prompt / toast line sits under the (max-size) list.
            var promptRect = new GameObject("Prompt").AddComponent<RectTransform>();
            promptRect.SetParent(canvasObj.transform, false);
            promptRect.anchorMin = promptRect.anchorMax = new Vector2(0f, 1f);
            promptRect.pivot = new Vector2(0f, 1f);
            promptRect.anchoredPosition = new Vector2(22f, -22f - RowHeight * Multiplayer.PartySystem.MaxPartySize - 8f);
            promptRect.sizeDelta = new Vector2(520f, 60f);

            promptText = promptRect.gameObject.AddComponent<TextMeshProUGUI>();
            promptText.fontSize = 13;
            promptText.color = UITheme.TextGold;
            promptText.alignment = TextAlignmentOptions.TopLeft;
            promptText.raycastTarget = false;
            promptText.enableWordWrapping = true;
            promptText.outlineWidth = 0.16f;
            promptText.outlineColor = new Color(0f, 0f, 0f, 0.85f);
        }

        // ==================== ROSTER ====================

        private void RebuildRoster()
        {
            foreach (var row in rows)
                if (row.root != null) Destroy(row.root);
            rows.Clear();

            if (party == null || !party.InParty) return;

            for (int i = 0; i < party.MemberNetIds.Length && i < Multiplayer.PartySystem.MaxPartySize; i++)
                rows.Add(BuildRow(i, party.MemberNames[i], party.MemberNetIds[i]));

            RefreshBars();
        }

        private MemberRow BuildRow(int index, string memberName, uint memberNetId)
        {
            var root = new GameObject($"Member_{memberName}").AddComponent<RectTransform>();
            root.SetParent(listRoot, false);
            root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(0f, -index * RowHeight);
            root.sizeDelta = new Vector2(240f, RowHeight);

            bool isSelf = duel != null ? memberNetId == duel.netId
                        : party != null && memberNetId == party.netId;

            var nameRect = new GameObject("Name").AddComponent<RectTransform>();
            nameRect.SetParent(root, false);
            nameRect.anchorMin = new Vector2(0f, 1f); nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = Vector2.zero;
            nameRect.sizeDelta = new Vector2(0f, 18f);
            var nameText = nameRect.gameObject.AddComponent<TextMeshProUGUI>();
            nameText.text = isSelf ? $"{memberName} (you)" : memberName;
            nameText.fontSize = 13;
            nameText.color = isSelf ? UITheme.TextDim : UITheme.TextMain;
            nameText.alignment = TextAlignmentOptions.TopLeft;
            nameText.raycastTarget = false;
            nameText.enableWordWrapping = false;
            nameText.overflowMode = TextOverflowModes.Truncate;
            nameText.outlineWidth = 0.16f;
            nameText.outlineColor = new Color(0f, 0f, 0f, 0.85f);

            // Thin HP bar: dark track + left-pivoted fill scaled by health percent.
            var trackRect = new GameObject("BarTrack").AddComponent<RectTransform>();
            trackRect.SetParent(root, false);
            trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 1f);
            trackRect.pivot = new Vector2(0f, 1f);
            trackRect.anchoredPosition = new Vector2(1f, -18f);
            trackRect.sizeDelta = new Vector2(BarWidth, 4f);
            var track = trackRect.gameObject.AddComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.55f);
            track.raycastTarget = false;

            var fillRect = new GameObject("BarFill").AddComponent<RectTransform>();
            fillRect.SetParent(trackRect, false);
            fillRect.anchorMin = new Vector2(0f, 0f); fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = new Vector2(BarWidth, 0f);
            var fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = UITheme.HpGreen;
            fill.raycastTarget = false;

            return new MemberRow { root = root.gameObject, fill = fill, netId = memberNetId };
        }

        private void RefreshBars()
        {
            foreach (var row in rows)
            {
                if (row.fill == null) continue;

                // Resolve (and cache) the member's Health from the spawned lookup -
                // Health's SyncVar replicates globally, so the percent is always live.
                if (row.health == null
                    && NetworkClient.spawned.TryGetValue(row.netId, out var identity) && identity != null)
                    row.health = identity.GetComponent<Combat.Health>();

                float percent = row.health != null ? Mathf.Clamp01(row.health.HealthPercent) : 0f;
                row.fill.rectTransform.localScale = new Vector3(percent, 1f, 1f);
                row.fill.color = UITheme.HealthColor(percent);
            }
        }

        // ==================== PROMPTS ====================

        private void RefreshPrompt()
        {
            if (promptText == null) return;

            var lines = new List<string>(3);

            if (party != null && party.HasPendingInvite)
                lines.Add($"<b>{party.PendingInviterName}</b> invites you to a party, <color=#5ED68C>Y</color> accept / <color=#E06060>N</color> decline");
            if (duel != null && duel.HasPendingRequest)
                lines.Add($"<b>{duel.PendingChallengerName}</b> challenges you to a duel, <color=#5ED68C>Y</color> accept / <color=#E06060>N</color> decline");
            if (duel != null && duel.IsDueling)
                lines.Add("<color=#E06060>DUEL</color>, first to 1 HP loses");
            if (toast != null)
                lines.Add($"<color=#C8C2CE>{toast}</color>");

            promptText.text = string.Join("\n", lines);
        }
    }
}
