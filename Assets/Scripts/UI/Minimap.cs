using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AldenFall.Quests;

namespace AldenFall.UI
{
    /// <summary>
    /// A whole-world minimap (top-right): a fixed top-down overview of Emberfall with static
    /// POI dots (city, glade, camps, volcano, seal stones, the fallen traveler), a live player
    /// marker, and gold quest markers for the current ReachLocation objective of active quests.
    /// Self-building runtime canvas (like FirstClearBanner); added to the local player via
    /// EnsureGameplayComponents. World (x,z) maps to the map with north = up.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        private const float MapSize = 190f;      // px, square
        private const float WorldHalf = 460f;    // world extent mapped to the map half-width
        private const float DotSize = 6f;

        // Static world points of interest (mirror the F9 teleport landmarks).
        private static readonly (string label, Vector2 xz, Color color)[] Pois =
        {
            ("City",    new Vector2(0, 0),      new Color(0.85f, 0.80f, 0.55f)),
            ("Glade",   new Vector2(0, 84),     new Color(0.55f, 0.80f, 0.55f)),
            ("Tavern",  new Vector2(-32, 32),   new Color(0.80f, 0.65f, 0.45f)),
            ("Lake",    new Vector2(-120, 138), new Color(0.45f, 0.70f, 0.95f)),
            ("Goblins", new Vector2(-190, 0),   new Color(0.55f, 0.75f, 0.40f)),
            ("Bandits", new Vector2(190, 0),    new Color(0.80f, 0.55f, 0.45f)),
            ("Orcs",    new Vector2(-105, -114),new Color(0.70f, 0.45f, 0.40f)),
            ("Volcano", new Vector2(0, -308),   new Color(0.95f, 0.45f, 0.25f)),
            ("Boss",    new Vector2(0, -430),   new Color(0.95f, 0.30f, 0.30f)),
            ("Seal W",  new Vector2(-82, 96),   new Color(0.60f, 0.55f, 0.80f)),
            ("Seal E",  new Vector2(64, 110),   new Color(0.60f, 0.55f, 0.80f)),
            ("Seal Dp", new Vector2(-30, 184),  new Color(0.60f, 0.55f, 0.80f)),
        };

        private QuestLog log;
        private RectTransform mapRect;
        private RectTransform playerDot;
        private readonly List<RectTransform> questDots = new List<RectTransform>();
        private float nextQuestRefresh;

        private void Start()
        {
            var identity = GetComponent<Mirror.NetworkIdentity>();
            if (identity != null && !identity.isLocalPlayer) { enabled = false; return; }
            log = GetComponent<QuestLog>();
            Build();
        }

        private void Build()
        {
            var canvasObj = new GameObject("MinimapCanvas");
            canvasObj.transform.SetParent(transform, false);
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 110;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            mapRect = MakeRect(canvasObj.transform, "Map", new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(MapSize, MapSize));
            var bg = mapRect.gameObject.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.08f, 0.72f);
            bg.raycastTarget = false;

            // Keyline
            var border = MakeRect(mapRect, "Border", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MapSize + 3f, MapSize + 3f));
            border.SetAsFirstSibling();
            var bimg = border.gameObject.AddComponent<Image>();
            bimg.color = new Color(0.75f, 0.62f, 0.35f, 0.55f);
            bimg.raycastTarget = false;

            var title = MakeRect(mapRect, "Title", new Vector2(0.5f, 1f), new Vector2(0f, 12f), new Vector2(MapSize, 16f));
            var tt = title.gameObject.AddComponent<TextMeshProUGUI>();
            tt.text = "EMBERFALL"; tt.fontSize = 11; tt.alignment = TextAlignmentOptions.Center;
            tt.color = new Color(0.85f, 0.80f, 0.60f); tt.characterSpacing = 4; tt.raycastTarget = false;

            foreach (var poi in Pois)
                MakeDot(MapRect(poi.xz), poi.color, DotSize, poi.label);

            playerDot = MakeDot(Vector2.zero, new Color(1f, 1f, 1f), DotSize + 2f, null);
            var pimg = playerDot.GetComponent<Image>();
            pimg.color = Color.white;
        }

        private void Update()
        {
            if (mapRect == null) return;
            if (playerDot != null)
                playerDot.anchoredPosition = MapRect(new Vector2(transform.position.x, transform.position.z));

            if (Time.time >= nextQuestRefresh)
            {
                nextQuestRefresh = Time.time + 1f;
                RefreshQuestMarkers();
            }
        }

        private void RefreshQuestMarkers()
        {
            foreach (var d in questDots) if (d != null) Destroy(d.gameObject);
            questDots.Clear();
            if (log == null) return;

            foreach (var def in QuestDatabase.All)
            {
                if (!log.IsActive(def.Id) && !log.IsComplete(def.Id)) continue;
                foreach (var o in def.Objectives)
                {
                    if (o.Kind != ObjectiveKind.ReachLocation) continue;
                    var dot = MakeDot(MapRect(new Vector2(o.Location.x, o.Location.z)),
                        new Color(1f, 0.82f, 0.4f), DotSize + 1f, null);
                    questDots.Add(dot);
                }
            }
        }

        // ---- world (x,z) -> map local anchored position (north = up) ----
        private static Vector2 MapRect(Vector2 xz)
        {
            float half = MapSize / 2f - 6f;
            float mx = Mathf.Clamp(xz.x / WorldHalf, -1f, 1f) * half;
            float my = Mathf.Clamp(xz.y / WorldHalf, -1f, 1f) * half;
            return new Vector2(mx, my);
        }

        private RectTransform MakeDot(Vector2 anchoredPos, Color color, float size, string label)
        {
            var dot = MakeRect(mapRect, "Dot", new Vector2(0.5f, 0.5f), anchoredPos, new Vector2(size, size));
            var img = dot.gameObject.AddComponent<Image>();
            img.color = color; img.raycastTarget = false;
            if (!string.IsNullOrEmpty(label))
            {
                var lt = MakeRect(dot, "Lbl", new Vector2(0.5f, 0f), new Vector2(0f, -7f), new Vector2(60f, 12f));
                var t = lt.gameObject.AddComponent<TextMeshProUGUI>();
                t.text = label; t.fontSize = 8; t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(color.r, color.g, color.b, 0.9f); t.raycastTarget = false;
            }
            return dot;
        }

        private static RectTransform MakeRect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
