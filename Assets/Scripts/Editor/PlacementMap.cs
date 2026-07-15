using UnityEngine;

namespace AldenFall.Editor
{
    public enum PlaceZone : byte { Open, Road, Water, City, Wall, Glade, Approach, Mountain, Camp }
    public enum PlaceBiome : byte { Lush, Meadow, DarkForest, Volcanic, Shore }

    /// <summary>
    /// Data-driven world occupancy map: ONE source of truth for what's at each (x,z) — its zone,
    /// its biome, and whether a large prop already occupies it. Placement becomes a QUERY (is this
    /// cell open ground of the right biome, and free?) instead of scattered ad-hoc guesses. This is
    /// what stops trees-in-roads / trees-in-walls / trees-in-trees / dead-trees-in-lush-forest.
    /// </summary>
    public class PlacementMap
    {
        public const float Cell = 2.0f;
        public readonly float OriginX, OriginZ;
        public readonly int W, H;
        readonly PlaceZone[] zone;
        readonly PlaceBiome[] biome;
        readonly float[] occ;   // radius of the prop occupying this cell (0 = free)

        public PlacementMap(float minX, float minZ, float maxX, float maxZ)
        {
            OriginX = minX; OriginZ = minZ;
            W = Mathf.CeilToInt((maxX - minX) / Cell);
            H = Mathf.CeilToInt((maxZ - minZ) / Cell);
            zone = new PlaceZone[W * H];
            biome = new PlaceBiome[W * H];
            occ = new float[W * H];
        }

        int Idx(int cx, int cz) => cz * W + cx;
        public int Cx(float x) => Mathf.Clamp(Mathf.FloorToInt((x - OriginX) / Cell), 0, W - 1);
        public int Cz(float z) => Mathf.Clamp(Mathf.FloorToInt((z - OriginZ) / Cell), 0, H - 1);
        public float CellWorldX(int cx) => OriginX + (cx + 0.5f) * Cell;
        public float CellWorldZ(int cz) => OriginZ + (cz + 0.5f) * Cell;
        public bool InBounds(float x, float z) => x >= OriginX && z >= OriginZ && x < OriginX + W * Cell && z < OriginZ + H * Cell;

        /// <summary>Classify every cell via the callback (called with each cell's world centre).</summary>
        public void Classify(System.Func<float, float, (PlaceZone, PlaceBiome)> f)
        {
            for (int cz = 0; cz < H; cz++)
                for (int cx = 0; cx < W; cx++)
                {
                    var (z, b) = f(CellWorldX(cx), CellWorldZ(cz));
                    int i = Idx(cx, cz); zone[i] = z; biome[i] = b;
                }
        }

        public PlaceZone ZoneAt(float x, float z) => zone[Idx(Cx(x), Cz(z))];
        public PlaceBiome BiomeAt(float x, float z) => biome[Idx(Cx(x), Cz(z))];

        /// <summary>Zone grid as bytes (row-major) for the runtime overlay to sample.</summary>
        public byte[] ZoneBytes()
        {
            var b = new byte[W * H];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)zone[i];
            return b;
        }

        /// <summary>Can a disc of `radius` at (x,z) be placed? Every covered cell must pass zoneOk
        /// and not already hold an overlapping prop.</summary>
        public bool CanPlace(float x, float z, float radius, System.Func<PlaceZone, bool> zoneOk)
        {
            if (!InBounds(x, z)) return false;
            int r = Mathf.CeilToInt(radius / Cell), ccx = Cx(x), ccz = Cz(z);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = ccx + dx, cz = ccz + dz;
                    if (cx < 0 || cz < 0 || cx >= W || cz >= H) return false;
                    int i = Idx(cx, cz);
                    float wx = CellWorldX(cx), wz = CellWorldZ(cz);
                    float dd = (wx - x) * (wx - x) + (wz - z) * (wz - z);
                    if (dd > radius * radius) continue;              // cell centre outside the disc
                    if (!zoneOk(zone[i])) return false;
                    if (occ[i] > 0f)
                    {
                        float rr = radius + occ[i];
                        if (dd < rr * rr) return false;              // discs overlap
                    }
                }
            return true;
        }

        public void Occupy(float x, float z, float radius)
        {
            int r = Mathf.CeilToInt(radius / Cell), ccx = Cx(x), ccz = Cz(z);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int cx = ccx + dx, cz = ccz + dz;
                    if (cx < 0 || cz < 0 || cx >= W || cz >= H) continue;
                    int i = Idx(cx, cz);
                    float wx = CellWorldX(cx), wz = CellWorldZ(cz);
                    if ((wx - x) * (wx - x) + (wz - z) * (wz - z) <= radius * radius)
                        occ[i] = Mathf.Max(occ[i], radius);
                }
        }

        /// <summary>Bake the zone grid into a point-filtered texture for the in-game inspector.</summary>
        public Texture2D BakeZoneTexture()
        {
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int i = 0; i < W * H; i++) px[i] = ZoneColor(zone[i]);
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Color32 ZoneColor(PlaceZone z) => z switch
        {
            PlaceZone.Road => new Color32(150, 95, 50, 255),
            PlaceZone.Water => new Color32(40, 110, 200, 255),
            PlaceZone.City => new Color32(150, 150, 150, 255),
            PlaceZone.Wall => new Color32(80, 80, 80, 255),
            PlaceZone.Glade => new Color32(140, 210, 140, 255),
            PlaceZone.Approach => new Color32(120, 55, 35, 255),
            PlaceZone.Mountain => new Color32(205, 195, 175, 255),
            PlaceZone.Camp => new Color32(210, 160, 55, 255),
            _ => new Color32(55, 130, 55, 255),   // Open
        };
    }
}
