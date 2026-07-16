using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// In-game coordinate + zone inspector (toggle F3). World-gen bakes the PlacementMap's zone
    /// grid onto this so you can stand anywhere and read the exact X/Y/Z plus what the placement
    /// system thinks is there (Road / Water / Wall / Open / biome-etc). This is the single source
    /// of truth the scatterers query — so if the overlay says "Road", nothing should be scattered
    /// there. Data-driven, not guessed.
    /// </summary>
    public class WorldCoordOverlay : MonoBehaviour
    {
        public float originX, originZ, cellSize = 2f;
        public int gridW, gridH;
        public byte[] zones;   // PlaceZone per cell, row-major (cz*gridW + cx)

        // Mob camp map (same single-source-of-truth as monsters.json) so mobs are readable live too.
        public string[] mobIds;
        public float[] mobX, mobZ, mobRoam;

        static readonly string[] ZoneNames =
            { "Open", "Road", "Water", "City", "Wall", "Glade", "Approach", "Mountain", "Camp" };

        bool show = true;
        Transform probe;
        GUIStyle style;

        // Smoothed frame time -> live FPS readout (exponential moving average, unscaled).
        float smoothDt = 1f / 60f;
        float worstDt;          // worst frame in the current ~1s window (a "1% low" feel)
        float worstWindow;

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F3)) show = !show;
            float dt = Time.unscaledDeltaTime;
            smoothDt += (dt - smoothDt) * 0.08f;
            if (dt > worstDt) worstDt = dt;
            worstWindow += dt;
            if (worstWindow > 1f) { worstWindow = 0f; worstDt = dt; }   // reset the worst each second
        }

        Vector3 ProbePos()
        {
            if (probe == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) probe = tagged.transform;
                else if (Camera.main != null) probe = Camera.main.transform;
            }
            return probe != null ? probe.position : Vector3.zero;
        }

        string ZoneAt(float x, float z)
        {
            if (zones == null || zones.Length != gridW * gridH || gridW == 0) return "?";
            int cx = Mathf.Clamp(Mathf.FloorToInt((x - originX) / cellSize), 0, gridW - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z - originZ) / cellSize), 0, gridH - 1);
            byte v = zones[cz * gridW + cx];
            return v < ZoneNames.Length ? ZoneNames[v] : "?";
        }

        void OnGUI()
        {
            if (!show) return;
            if (style == null)
                style = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8), richText = true };
            Vector3 p = ProbePos();
            float fps = 1f / Mathf.Max(0.0001f, smoothDt);
            float ms = smoothDt * 1000f;
            float low = 1f / Mathf.Max(0.0001f, worstDt);   // worst frame this second
            string fpsColor = fps >= 58f ? "#8CE68C" : fps >= 30f ? "#F2CC8C" : "#E68C8C";
            GUI.Box(new Rect(10, 10, 300, 140),
                $"<color={fpsColor}>FPS {fps:F0}</color>   {ms:F1} ms   (low {low:F0})\n" +
                $"X {p.x:F1}   Y {p.y:F1}   Z {p.z:F1}\nZone: {ZoneAt(p.x, p.z)}\nMob: {NearestMob(p.x, p.z)}\n[F3] hide", style);
        }

        // Nearest mob camp to a point (edge distance = centre distance - roam radius, so 0 means
        // you're inside its roam), for live mob mapping alongside the zone read.
        string NearestMob(float x, float z)
        {
            if (mobIds == null || mobIds.Length == 0) return "-";
            int best = -1; float bestEdge = float.MaxValue;
            for (int i = 0; i < mobIds.Length; i++)
            {
                float dx = mobX[i] - x, dz = mobZ[i] - z;
                float edge = Mathf.Sqrt(dx * dx + dz * dz) - mobRoam[i];
                if (edge < bestEdge) { bestEdge = edge; best = i; }
            }
            if (best < 0) return "-";
            return bestEdge <= 0f
                ? $"{mobIds[best]} (INSIDE roam {mobRoam[best]:F0})"
                : $"{mobIds[best]} {bestEdge:F0}m (roam {mobRoam[best]:F0})";
        }
    }
}
