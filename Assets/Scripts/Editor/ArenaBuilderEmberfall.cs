#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.AI.Navigation;

namespace AldenFall.Editor
{
    /// <summary>
    /// Emberfall — Floor 1, V2 (radial world). A fresh, deliberately-designed world that
    /// REPLACES the old linear arena/field/forest layout. Origin = city centre; +Z north,
    /// +X east; ground y = 0.
    ///
    ///   * Emberfall City  — walled town at the centre (x,z in [-55,55]), 4 gates, Ember
    ///     Fountain plaza, Market Row (NE), The Forge (SE), Tavern + party board (NW),
    ///     Aldric + player spawn (SW). The SOUTH gate opens to the ember approach (danger).
    ///   * Forest ring     — wraps the city on every side except the south sector:
    ///       - Emberfall Lake (NW, swimmable) + feeder river + fish + fishing/cooking.
    ///       - Deep Dark Woods (N), Goblin Camp (W), Bandit Camp (E), Orc Warcamp (SW).
    ///   * Ember Approach   — the south corridor that chars + darkens toward the volcano;
    ///     EmberZones ramp the atmosphere, first LavaHazard pools appear.
    ///   * Emberfall Volcano (dungeon) — a caldera far south; multi-room crawl with lava
    ///     channels, elite guardians, the Floor Boss, and a sealed Floor-2 stairway.
    ///
    /// This is a partial of <see cref="ArenaBuilder"/> so it can reuse every private helper
    /// (Cube/Cylinder/Sphere/Mat/Child/ColliderBox/CreateTorch/PlaceMeshyProp/CreateLootChest/
    /// BuildQuestGiver/BuildRescueGiver/BuildOrcCampDressing/BuildBanditCampDressing/
    /// BuildCagedDaughter/SetLayerRecursive/BuildGrassBladeField) and shared state (rng, Mat cache).
    /// </summary>
    public static partial class ArenaBuilder
    {
        // ---- City ----
        private const float CityHalf = 55f;         // walls at ±55
        private const float CityGate = 10f;         // gate opening width

        // ---- Lake (NW) ----
        // v8: Jon called the old 88m lake "a puddle at best". Genuinely big and DEEP now,
        // sized against the NW quadrant (edges x[-196,-80], z[80,196]) with the whole
        // river network draining into it.
        // Environment/Waterworks milestone: the lake is the northern STARTING vista (player +
        // Aldric spawn on its south shore), city-sized and cradled by mountains with a forest
        // buffer. Progression runs south: lake -> dark woods -> city -> ember approach -> volcano.
        private static readonly Vector3 LakeCenter = new Vector3(0f, 0f, 300f);
        private const float LakeHalf = 95f;         // ~190m across, bigger than the city
        private const float LakeBedY = -5.0f;       // depth = WaterSurfaceY - LakeBedY = 4.65m (deep swim)

        // ---- Ember approach (south corridor) ----
        private const float ApproachHalfW = 62f;    // corridor half-width in x
        private const float ApproachNorthZ = -55f;  // starts at the south city gate
        private const float ApproachSouthZ = -315f;  // ends at the volcano entrance

        // ---- Volcano interior envelope: single source of truth in World.EmberfallVolcanoGeo,
        // shared with the runtime DungeonResetController safety envelope so they cannot desync. ----
        private const float VolcanoEntranceZ = AldenFall.World.EmberfallVolcanoGeo.EntranceZ;
        private const float BossZ = AldenFall.World.EmberfallVolcanoGeo.BossZ;

        // ==================== ENTRY ====================

        /// <summary>Single entry called by BuildArena — assembles the whole radial world.</summary>
        // Jon greenlit the real-Terrain rewrite (2026-07-12). ROLLED BACK after 3 QA
        // rounds: NavMesh/spawns/splats/carving all work, but URP renders the detail
        // grass as unlit black shards and the ground washes pale next to the richer
        // tile look — net visual regression per the rollback directive. The full
        // implementation (BuildEmberfallTerrain + helpers) stays for in-editor
        // iteration: the missing pieces are a proper URP detail-grass shader and
        // hand-tuned splat textures, not structure.
        private const bool UseUnityTerrain = true;   // v9: revived - real rolling terrain + splat + instanced grass

        private static void BuildEmberfallWorld(Transform root, int groundLayer)
        {
            if (UseUnityTerrain)
            {
                BuildEmberfallTerrain(root, groundLayer);  // real Terrain + splats + wind grass
                // The forest + ground ecosystem live in BuildEmberfallGround (legacy path); wire
                // them into the terrain path too or the world has no scattered trees/foliage/rocks.
                var eco = Child(root, "EmberEcosystem");
                ScatterForestTrees(eco, groundLayer);
                ScatterGroundDetail(eco, groundLayer);
            }
            else
                BuildEmberfallGround(root, groundLayer);   // legacy cube tiles (rollback path)
            BuildEmberfallCity(root, groundLayer);
            BuildEmberfallLake(root, groundLayer);
            BuildDeepDarkWoods(root, groundLayer);
            BuildWorldSecrets(root, groundLayer);
            BuildEmberForestCamps(root, groundLayer);
            BuildEmberOrcWarcamp(root, groundLayer);
            BuildEmberApproach(root, groundLayer);
            BuildEmberfallVolcano(root, groundLayer);
            BuildMountainRange(root, groundLayer);     // real mountain-mesh border (when assets exist)
            BuildAldricGlade(root, groundLayer);       // player spawn + hermit mentor (N of city)
            BuildEmberfallSystems(root, groundLayer);
            BuildGlobalSafety(root, groundLayer);      // mountain ring + underfill + catch floor
        }

        // ---- Aldric's forest glade (player spawn) — a lush clearing N of the city ----
        private static readonly Vector2 GladeCenter = new Vector2(0f, 200f);   // Aldric's lakeside camp / spawn (south shore of the lake)
        private const float GladeRadius = 20f;

        private static bool InGlade(float x, float z)
            => Vector2.Distance(new Vector2(x, z), GladeCenter) < GladeRadius + 3f;

        // ==================== REGION PREDICATES ====================

        private static bool InCityFootprint(float x, float z)
            => Mathf.Abs(x) <= CityHalf + 2f && Mathf.Abs(z) <= CityHalf + 2f;

        private static bool InApproachFootprint(float x, float z)
            => z <= ApproachNorthZ + 2f && z >= ApproachSouthZ && Mathf.Abs(x) <= ApproachHalfW;

        /// <summary>True if a Meshy prop fbx has been generated + imported. Lets new mesh-based
        /// features (mountain range, volcano) auto-activate the moment their asset lands, while
        /// falling back to the old geometry until then.</summary>
        private static bool HasMesh(string name)
            => AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{name}.fbx") != null;

        /// <summary>Load-or-create a 256px procedural texture asset (for skinning untextured
        /// preview meshes - mountains, volcano - with real surface detail).</summary>
        private static Texture2D GetGeneratedTexture(string name, System.Func<Color32[]> gen)
        {
            string p = $"{MaterialDir}/{name}.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            if (tex != null) return tex;
            tex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
            tex.SetPixels32(gen());
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, p);
            return tex;
        }

        // Distance from (x,z) to the R3 canal centreline (used to open cobbles + skip props
        // over the manmade canal where it threads the city).
        private static float DistToCityCanal(float x, float z)
        {
            var pts = RiverPaths[3];
            float d = float.MaxValue;
            var p = new Vector2(x, z);
            for (int i = 0; i < pts.Length - 1; i++)
                d = Mathf.Min(d, DistToSegment(p, new Vector2(pts[i].x, pts[i].z), new Vector2(pts[i + 1].x, pts[i + 1].z)));
            return d;
        }

        private static bool InLakeWater(float x, float z)
        {
            // Square basin footprint (matches BuildEmberfallLake). Leave a hole so the
            // basin bed shows through instead of a grass tile floating over the water.
            return Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) < LakeEdge(x, z) - 2f;
        }

        /// <summary>Organic lake shoreline radius at (x,z): base radius wobbled by two angular
        /// noise octaves so the lake is a natural irregular basin, not a perfect disc. Slightly
        /// negative-biased so the wobble never grows the lake into the mountain ring.</summary>
        private static float LakeEdge(float x, float z)
        {
            float dx = x - LakeCenter.x, dz = z - LakeCenter.z;
            float ang = Mathf.Atan2(dz, dx);
            float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
            float wob = (Mathf.PerlinNoise(ca * 1.6f + 4.2f, sa * 1.6f + 9.1f) - 0.55f) * 2f * 16f
                      + (Mathf.PerlinNoise(ca * 3.5f + 1.1f, sa * 3.5f + 6.3f) - 0.5f) * 2f * 7f;
            return LakeHalf + wob;
        }

        /// <summary>Feeder-river centre X at a given z (winds gently down into the lake).</summary>
        private static float FeederRiverX(float z) => LakeCenter.x + 5f * Mathf.Sin((z - 194f) / 22f);

        // ==================== RIVER NETWORK (v8 hydrology) ====================
        // Four waterfall SOURCES on the mountain ring feed seven river reaches
        // (two splits) that all drain into Emberfall Lake: a real water system
        // from mountain to lake, not a single fenced channel.
        //   R1  north feeder     (-150,250) -> lake north shore
        //   R2  northeast river  (85,248) -> through the dark woods -> north shore
        //   R2b   split of R2    -> east shore
        //   R3  west river       (-248,92) -> west shore
        //   R4  east river       (245,62) -> across the north forest
        //   R4a   split of R4    -> east shore (fords the main road at x~0)
        //   R4b   split of R4    -> southeast corner
        // v9 waterworks: 4 waterfall sources on the surrounding mountains, each feeding a river
        // that drains into the north lake (0,300,r95). R3 runs from the EAST mountains WEST
        // through the CITY (a canal) and up to the lake. Every water body is on this chain.
        //   R0 North falls  (0,485)   -> lake N shore
        //   R1 NE falls      (320,455) -> lake NE
        //   R2 NW falls      (-320,455)-> lake NW
        //   R3 East falls    (435,45)  -> through the CITY -> lake SW inlet
        private static readonly Vector3[][] RiverPaths =
        {
            new[] { new Vector3(0f,0f,485f), new Vector3(2f,0f,450f), new Vector3(-4f,0f,420f), new Vector3(0f,0f,398f) },
            new[] { new Vector3(320f,0f,455f), new Vector3(230f,0f,415f), new Vector3(140f,0f,378f), new Vector3(72f,0f,352f) },
            new[] { new Vector3(-320f,0f,455f), new Vector3(-230f,0f,415f), new Vector3(-140f,0f,378f), new Vector3(-72f,0f,352f) },
            new[] { new Vector3(435f,0f,45f), new Vector3(300f,0f,40f), new Vector3(170f,0f,28f), new Vector3(60f,0f,12f), new Vector3(-8f,0f,6f), new Vector3(-64f,0f,40f), new Vector3(-84f,0f,120f), new Vector3(-64f,0f,200f), new Vector3(-40f,0f,235f) },
        };
        private static readonly float[] RiverHalfWidths = { 8f, 7f, 7f, 8f };
        // Waterfall sources: index into RiverPaths whose first point sits on a mountain.
        private static readonly int[] RiverSourceFalls = { 0, 1, 2, 3 };

        /// <summary>Fords: shallow wading crossings where the city river meets the N-S road.</summary>
        private static bool IsRiverFord(Vector3 p) => Mathf.Abs(p.x) < 10f && Mathf.Abs(p.z - 6f) < 16f;

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>True when (x,z) lies inside any river channel footprint (+1m shoulder).</summary>
        private static bool InAnyRiver(float x, float z)
        {
            var pt = new Vector2(x, z);
            for (int r = 0; r < RiverPaths.Length; r++)
            {
                var path = RiverPaths[r];
                float reach = RiverHalfWidths[r] + 2.5f;
                for (int i = 0; i < path.Length - 1; i++)
                {
                    if (DistToSegment(pt, new Vector2(path[i].x, path[i].z),
                            new Vector2(path[i + 1].x, path[i + 1].z)) < reach)
                        return true;
                }
            }
            return false;
        }

        private static bool InFeederRiver(float x, float z) => InAnyRiver(x, z);

        /// <summary>
        /// How charred/sooty the ground is at (x,z), 0 (lush) → 1 (volcano rim). A WIDE
        /// gradient toward the south so lush forest blends into the ember approach with no
        /// stark line (img 17); tapered at the sides so the far E/W forest stays green.
        /// </summary>
        private static float SootAmount(float x, float z)
        {
            float byZ = Mathf.InverseLerp(30f, ApproachSouthZ, z);          // 0 at z>=30 → 1 at volcano
            float side = Mathf.Clamp01((Mathf.Abs(x) - (ApproachHalfW + 55f)) / 70f);
            return Mathf.Clamp01(byZ * (1f - 0.7f * side));
        }

        // ==================== GROUND (forest ring + approach) ====================

        // ==================== REAL TERRAIN (greenlit rewrite) ====================

        /// <summary>
        /// Real Unity Terrain replacing the cube-tile valley floor: procedurally
        /// authored splat layers (lush grass → lake sand → volcanic soot via the same
        /// SootAmount gradient the tiles used), carved lake/river bowls (the existing
        /// lake/river geometry renders inside them), a recessed city pad so the cobbles
        /// sit proud without z-fighting, subtle height noise for a natural floor, and
        /// GPU-instanced WAVING detail grass across the lush ring — the "real terrain +
        /// wind grass" upgrade Jon greenlit. TerrainCollider is on the Ground layer, so
        /// the NavMesh bake and GroundSpawn snapping work unchanged.
        /// </summary>
        private static void BuildEmberfallTerrain(Transform root, int groundLayer)
        {
            // Expanded world (Environment milestone): room for a city-sized north lake + forest
            // buffer + real mountains, keeping city@origin + volcano south.
            const float sizeX = 900f, sizeZ = 1040f, sizeY = 150f;   // tall Y range for real mountains
            Vector3 origin = new Vector3(-450f, -sizeY / 2f, -470f);   // x[-450,450], z[-470,570]; surface base y=0
            const int hmRes = 513, splatRes = 512, detailRes = 1024;

            var data = new TerrainData
            {
                heightmapResolution = hmRes,
                alphamapResolution = splatRes,
            };
            data.size = new Vector3(sizeX, sizeY, sizeZ);

            float baseH = 0.5f;   // heightmap value putting the surface at world y=0
            float MetersToH(float meters) => baseH + meters / sizeY;

            // ---- Heights: flat base + gentle low-frequency noise in the wild ring;
            // carved bowls for the lake + feeder river; recessed city pad. ----
            // When real mountain MESHES exist, the terrain only needs a low steep boundary berm
            // (the meshes are the visible range); otherwise fall back to the tall rising-terrain
            // mountains. Computed once (AssetDatabase lookups are not per-texel cheap).
            bool meshMountains = HasMesh("MountainRidge") || HasMesh("MountainPeak");
            float mtnReach = meshMountains ? 48f : 160f;
            float mtnHeight = meshMountains ? 34f : 130f;
            var heights = new float[hmRes, hmRes];
            for (int iz = 0; iz < hmRes; iz++)
            {
                for (int ix = 0; ix < hmRes; ix++)
                {
                    float wx = origin.x + ix / (float)(hmRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(hmRes - 1) * sizeZ;
                    float h = 0f;

                    bool flatZone = InCityFootprint(wx, wz) || InGlade(wx, wz)
                                 || InApproachFootprint(wx, wz)
                                 || (Mathf.Abs(wx) < 8f && wz > -322f && wz < 190f);   // city road (city -> glade)
                    if (!flatZone)
                        // Rolling valley floor lifted WELL above the waterline. The world-spanning
                        // water sheet sits at y-0.35; when the floor was ~0.05 (a 0.4m gap) the
                        // transparent sheet bled through the flat lake approach at grazing angles
                        // and read as marsh/puddles everywhere. A 0.7m floor (>1m gap) removes that
                        // and gives real hill/gradient depth (only the carved lake/rivers hold water).
                        h = Mathf.Max(0.7f,
                            1.1f
                          + (Mathf.PerlinNoise(wx * 0.008f + 12.3f, wz * 0.008f + 5.1f) - 0.5f) * 2.0f
                          + (Mathf.PerlinNoise(wx * 0.024f + 31.7f, wz * 0.024f + 17.3f) - 0.5f) * 0.9f);

                    // Glade (player spawn) + the lake/city road are DRY flatzones - lift them clear
                    // of the water sheet too, or it bleeds through the flat spawn (Aldric standing
                    // in a "flood"). North of the ember corridor only; the volcano approach stays low.
                    if (InGlade(wx, wz) || (Mathf.Abs(wx) < 8f && wz > -55f && wz < 195f))
                        h = 0.85f;

                    if (InCityFootprint(wx, wz))
                        h = -0.15f;   // cobbles (top y=0) sit proud of the pad

                    // MOUNTAINS ring the world with rising TERRAIN (not cube walls), leaving the
                    // south ember-approach corridor open to the volcano. Steep near the edge.
                    float edge = Mathf.Min(Mathf.Min(wx - origin.x, origin.x + sizeX - wx),
                                           Mathf.Min(wz - origin.z, origin.z + sizeZ - wz));
                    bool southCorridor = Mathf.Abs(wx) < ApproachHalfW + 24f && wz < -90f;
                    if (!InCityFootprint(wx, wz) && !southCorridor && edge < mtnReach)
                    {
                        float m = 1f - edge / mtnReach;             // 0 inner -> 1 at the edge
                        h = Mathf.Max(h, m * m * mtnHeight);        // tall rising terrain, or a low berm under the mesh range
                    }

                    // LAKE: a natural round basin carved into the terrain (deep centre -> shore),
                    // grass/sand meets the waterline. The terrain IS the lakebed (no cube banks).
                    float lakeR = Vector2.Distance(new Vector2(wx, wz), new Vector2(LakeCenter.x, LakeCenter.z));
                    float lakeEdge = LakeEdge(wx, wz);
                    if (lakeR < lakeEdge + 4f)
                    {
                        // STEEP bank: deep interior, quick rise to a rim above the waterline in the
                        // last ~12% - so the shallow "marsh" band where the bed shows through the
                        // water is narrow, not a wide green swamp (Jon: forest not swamp).
                        float d01 = Mathf.Clamp01(lakeR / (lakeEdge + 4f));
                        h = Mathf.Min(h, Mathf.Lerp(-4.8f, 0.7f, Mathf.SmoothStep(0f, 1f, Mathf.Pow(d01, 2.3f))));
                    }

                    // Carve every river channel as a swimmable gorge (source on a mountain ->
                    // down to the lake). Gradient banks; shallower where it threads the city.
                    var hpt = new Vector2(wx, wz);
                    for (int ri = 0; ri < RiverPaths.Length; ri++)
                    {
                        var rpath = RiverPaths[ri];
                        float rd = float.MaxValue;
                        for (int si = 0; si < rpath.Length - 1; si++)
                            rd = Mathf.Min(rd, DistToSegment(hpt, new Vector2(rpath[si].x, rpath[si].z),
                                                                   new Vector2(rpath[si + 1].x, rpath[si + 1].z)));
                        float hw = RiverHalfWidths[ri];
                        float bedDepth = InCityFootprint(wx, wz) ? -2.6f : -3.0f;   // deep swimmable canal in the city
                        if (rd < hw) h = Mathf.Min(h, bedDepth);
                        else if (rd < hw + 10f) h = Mathf.Min(h, Mathf.Lerp(bedDepth, h, (rd - hw) / 10f));
                    }

                    heights[iz, ix] = MetersToH(h);
                }
            }
            data.SetHeights(0, 0, heights);

            // ---- Splat layers (procedural textures — no external assets needed) ----
            var grassLayer = MakeTerrainLayer("TL_Grass", new Color(0.16f, 0.3f, 0.12f), 0.05f);
            var sandLayer = MakeTerrainLayer("TL_Sand", new Color(0.52f, 0.46f, 0.34f), 0.03f);
            var sootLayer = MakeTerrainLayer("TL_Soot", new Color(0.115f, 0.09f, 0.08f), 0.04f);
            var dirtLayer = MakeTerrainLayer("TL_Dirt", new Color(0.36f, 0.28f, 0.19f), 0.04f);
            var rockLayer = MakeTerrainLayer("TL_Rock", new Color(0.3f, 0.28f, 0.29f), 0.02f);
            data.terrainLayers = new[] { grassLayer, sandLayer, sootLayer, dirtLayer, rockLayer };

            var alpha = new float[splatRes, splatRes, 5];
            for (int iz = 0; iz < splatRes; iz++)
            {
                for (int ix = 0; ix < splatRes; ix++)
                {
                    float wx = origin.x + ix / (float)(splatRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(splatRes - 1) * sizeZ;

                    float soot = SootAmount(wx, wz);
                    float lakeR = Vector2.Distance(new Vector2(wx, wz), new Vector2(LakeCenter.x, LakeCenter.z));
                    float lakeEdgeS = LakeEdge(wx, wz);
                    // Sand hugs the shoreline RING only (bed under the deep water is covered by the
                    // tight lake plane, and a wide sand band on dry land read as water at grazing
                    // angles - the "flooded spawn"). Beyond the ring = grass (dry spawn glade).
                    bool shore = (lakeR > lakeEdgeS - 14f && lakeR < lakeEdgeS + 2f) || InAnyRiver(wx, wz);
                    bool road = (Mathf.Abs(wx) < 7f && wz > -66f && wz < 214f);   // N-S dirt road (city -> lakeside)
                    float edge = Mathf.Min(Mathf.Min(wx - origin.x, origin.x + sizeX - wx),
                                           Mathf.Min(wz - origin.z, origin.z + sizeZ - wz));
                    bool southCorridor = Mathf.Abs(wx) < ApproachHalfW + 24f && wz < -90f;

                    float g = 1f, s = 0f, so = 0f, d = 0f, rk = 0f;
                    if (!southCorridor && edge < 150f) { rk = Mathf.Clamp01((150f - edge) / 95f); g *= 1f - rk; }  // rock mountains
                    // Volcanic south gradient: green -> dry dirt -> cracked lava toward the volcano.
                    if (wz < 40f)
                    {
                        float southT = Mathf.Clamp01(Mathf.InverseLerp(40f, -300f, wz));
                        so = Mathf.Clamp01((southT - 0.58f) * 2.6f);                      // lava/ash at the deep end
                        float dryDirt = Mathf.Clamp01(1f - Mathf.Abs(southT - 0.45f) * 3.0f) * (1f - so);  // dirt peaks mid
                        d = Mathf.Max(d, dryDirt);
                        g *= Mathf.Clamp01(1f - southT * 1.9f);                            // green fades out going south
                        rk *= 1f - so;
                    }
                    // The ember CORRIDOR itself is charred end-to-end: dark soot from the
                    // south gate ramping to full at the volcano. Pale dirt here read as a
                    // watery sheet at grazing angles (bare-bright-ground = fake water).
                    if (Mathf.Abs(wx) < ApproachHalfW + 8f && wz < ApproachNorthZ)
                    {
                        float ct = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, wz);
                        so = Mathf.Max(so, Mathf.Lerp(0.6f, 1f, ct));
                        g = 0f; d *= 0.25f; rk = 0f;
                    }
                    // (No random dirt patches in the meadow - they read as stray mud puddles. Dirt
                    // is intentional only: roads + the volcanic approach.)
                    if (shore) { s = 0.8f; g *= 0.2f; rk *= 0.2f; }                                                 // sand/mud shores
                    if (road || InCityFootprint(wx, wz)) { d = Mathf.Max(d, 0.85f); g *= 0.15f; so *= 0.4f; }       // dirt roads + city

                    float sum = g + s + so + d + rk;
                    if (sum <= 0f) { g = 1f; sum = 1f; }
                    alpha[iz, ix, 0] = g / sum;
                    alpha[iz, ix, 1] = s / sum;
                    alpha[iz, ix, 2] = so / sum;
                    alpha[iz, ix, 3] = d / sum;
                    alpha[iz, ix, 4] = rk / sum;
                }
            }
            data.SetAlphamaps(0, 0, alpha);

            // ---- GPU-instanced detail grass across the lush ring. URP does NOT render
            // the classic GrassBillboard/wavingGrass path (QA round 1: zero grass drawn) —
            // instanced MESH details are the supported route in Unity 6 URP. ----
            data.SetDetailResolution(detailRes, 32);
            // Unity 6 defaults to COVERAGE scatter mode, where our 1-3 density values
            // (out of 255) render as effectively nothing — we author instance counts.
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            // ONE shared tuft prefab for both detail layers - MakeGrassTuftPrefab rebuilds the
            // same asset path each call, so calling it twice would invalidate the first
            // prototype's reference. The layers differ only by their healthy/dry tint colours.
            var tuftPrefab = MakeGrassTuftPrefab();
            var detail = new DetailPrototype
            {
                prototype = tuftPrefab,
                renderMode = DetailRenderMode.VertexLit,
                healthyColor = new Color(0.26f, 0.42f, 0.17f),   // deeper, less uniform bright green
                dryColor = new Color(0.46f, 0.44f, 0.22f),       // wider dry range for variation
                minWidth = 0.7f, maxWidth = 1.4f,
                minHeight = 0.4f, maxHeight = 0.85f,             // shorter so scattered foliage reads above it
                noiseSeed = 42,
                noiseSpread = 0.3f,
                useInstancing = true,
                usePrototypeMesh = true,
            };
            // A SECOND detail layer of charred stubble for the sooty south. The ember approach
            // is excluded from the green grass above, so its bare terrain washed pale (fake
            // water) at grazing angles - dark vertical cover breaks that exactly as the meadow
            // grass does elsewhere, and reads as scorched, ash-choked ground.
            // Distinct dark-ash prefab: baked charred vertex colours + a warm ash material tint
            // (mesh details ignore the prototype colours, so the look must live in the prefab).
            var ashPrefab = MakeGrassTuftPrefab("AshTuft",
                tuftDark: new Color(0.10f, 0.08f, 0.075f),
                tuftLight: new Color(0.28f, 0.20f, 0.15f),
                matTint: new Color(0.5f, 0.4f, 0.34f));
            var charDetail = new DetailPrototype
            {
                prototype = ashPrefab,
                renderMode = DetailRenderMode.VertexLit,
                healthyColor = new Color(0.11f, 0.085f, 0.075f),  // charred black-brown stubble
                dryColor = new Color(0.19f, 0.13f, 0.09f),        // ash-grey dry blades
                minWidth = 0.6f, maxWidth = 1.3f,
                minHeight = 0.3f, maxHeight = 0.7f,
                noiseSeed = 91,
                noiseSpread = 0.4f,
                useInstancing = true,
                usePrototypeMesh = true,
            };
            data.detailPrototypes = new[] { detail, charDetail };

            var density = new int[detailRes, detailRes];
            for (int iz = 0; iz < detailRes; iz++)
            {
                for (int ix = 0; ix < detailRes; ix++)
                {
                    float wx = origin.x + ix / (float)(detailRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(detailRes - 1) * sizeZ;

                    if (InCityFootprint(wx, wz) || InApproachFootprint(wx, wz)) continue;
                    if (InLakeWater(wx, wz) || InFeederRiver(wx, wz)) continue;
                    if (Vector2.Distance(new Vector2(wx, wz), new Vector2(LakeCenter.x, LakeCenter.z)) < LakeEdge(wx, wz) + 2f) continue;  // grass grows right up to the narrow sand shore ring
                    if (SootAmount(wx, wz) > 0.25f) continue;
                    if (Mathf.Abs(wx) < 8f && wz > -66f && wz < 214f) continue;  // full dirt-road clear (matches splat road)
                    // Keep the water sources (waterfall gorges) bare so they read as rock, not a
                    // green hill: clear grass around each river source + the lake outflow notch.
                    bool nearSource = false;
                    foreach (int rsi in RiverSourceFalls)
                    {
                        Vector3 sp = RiverPaths[rsi][0];
                        if ((wx - sp.x) * (wx - sp.x) + (wz - sp.z) * (wz - sp.z) < 26f * 26f) { nearSource = true; break; }
                    }
                    if (nearSource) continue;
                    // No grass on the rock mountains (rising terrain at the world edge).
                    float dEdge = Mathf.Min(Mathf.Min(wx - origin.x, origin.x + sizeX - wx),
                                            Mathf.Min(wz - origin.z, origin.z + sizeZ - wz));
                    if (dEdge < 82f) continue;

                    // Near-full grass coverage so bare ground never shows as fake water/mud.
                    float clump = Mathf.PerlinNoise(wx * 0.055f + 7.7f, wz * 0.055f + 3.1f);
                    if (clump < 0.05f) continue;
                    density[iz, ix] = clump > 0.45f ? 18 : 12;
                }
            }
            data.SetDetailLayer(0, 0, 0, density);

            // Charred stubble across the sooty south (approach + soot gradient), skipping water,
            // the central road, and the very deepest lava-choked ground at the volcano rim.
            var charDensity = new int[detailRes, detailRes];
            for (int iz = 0; iz < detailRes; iz++)
            {
                for (int ix = 0; ix < detailRes; ix++)
                {
                    float wx = origin.x + ix / (float)(detailRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(detailRes - 1) * sizeZ;

                    if (InCityFootprint(wx, wz)) continue;
                    if (InLakeWater(wx, wz) || InFeederRiver(wx, wz)) continue;
                    if (Mathf.Abs(wx) < 8f && wz < ApproachNorthZ + 4f) continue;   // keep the ember road clear
                    float dEdge = Mathf.Min(Mathf.Min(wx - origin.x, origin.x + sizeX - wx),
                                            Mathf.Min(wz - origin.z, origin.z + sizeZ - wz));
                    if (dEdge < 82f) continue;                                       // no cover on the rock mountains

                    float soot = SootAmount(wx, wz);
                    bool corridor = InApproachFootprint(wx, wz);
                    if (soot < 0.28f && !corridor) continue;                         // only the charred south
                    if (soot > 0.94f) continue;                                      // bare molten rock at the rim

                    float clump = Mathf.PerlinNoise(wx * 0.06f + 21.3f, wz * 0.06f + 9.4f);
                    if (clump < 0.08f) continue;
                    charDensity[iz, ix] = clump > 0.5f ? 14 : 9;
                }
            }
            data.SetDetailLayer(0, 0, 1, charDensity);

            AssetDatabase.CreateAsset(data, $"{MaterialDir}/EmberfallTerrain.asset");
            // Re-apply the splat on the now asset-backed TerrainData: assigning terrainLayers
            // re-initialises the alphamap to layer-0 (all grass), and that reset is what gets
            // serialised unless we write the weights again once the asset exists, then flush.
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var terrainObj = Terrain.CreateTerrainGameObject(data);
            terrainObj.name = "EmberfallTerrain";
            terrainObj.transform.SetParent(root);
            terrainObj.transform.position = origin;
            if (groundLayer >= 0) terrainObj.layer = groundLayer;
            terrainObj.isStatic = true;
            var terrain = terrainObj.GetComponent<Terrain>();
            terrain.detailObjectDistance = 220f;
            terrain.detailObjectDensity = 1f;
            // Low pixel error: a high value let the terrain LOD flatten/drop the mesh BELOW the
            // heightmap at distance, dipping under the world water sheet so it showed through the
            // flat fields as fake puddles/marsh (worse the farther out you looked). Keep the
            // rendered mesh faithful so dry land stays above the waterline everywhere.
            terrain.heightmapPixelError = 1f;
            // Default basemapDistance (~20m) swaps to the washed-out low-res basemap one
            // screen from the player — THE pale-mint wash in both QA rounds. Keep the
            // real splats out to the fog band.
            terrain.basemapDistance = 2000f;   // never swap to the low-res basemap (expanded world > 900m)
        }

        private static TerrainLayer MakeTerrainLayer(string name, Color baseColor, float noise)
        {
            // Rich procedural albedo (256px) per surface kind instead of a flat 64px noise wash
            // (the pale-ground problem that shelved the terrain). Tiles tightly so detail reads.
            const int s = 256;
            Color32[] px = name switch
            {
                "TL_Grass" => GroundTextureGenerator.Grass(s),
                "TL_Sand"  => GroundTextureGenerator.Sand(s),
                "TL_Soot"  => GroundTextureGenerator.Soot(s),
                "TL_Dirt"  => GroundTextureGenerator.Dirt(s),
                "TL_Rock"  => GroundTextureGenerator.Rock(s),
                _          => GroundTextureGenerator.Grass(s),
            };
            AssetDatabase.DeleteAsset($"{MaterialDir}/{name}_Tex.asset");
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, $"{MaterialDir}/{name}_Tex.asset");

            AssetDatabase.DeleteAsset($"{MaterialDir}/{name}.terrainlayer");
            var layer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(6f, 6f),
                specular = Color.black,
                metallic = 0f,
                smoothness = 0f,   // fully matte: any smoothness let flat low ground mirror the sky at grazing angles, reading as teal "water"/marsh
            };
            AssetDatabase.CreateAsset(layer, $"{MaterialDir}/{name}.terrainlayer");
            return layer;
        }

        /// <summary>
        /// A crossed-quad grass tuft PREFAB for instanced terrain details (URP path).
        /// Two intersecting vertical quads, vertex-colored dark→light so the terrain
        /// detail tinting reads as depth. Saved under MaterialDir like the layers.
        /// </summary>
        private static GameObject MakeGrassTuftPrefab(string id = "GrassTuft",
            Color? tuftDark = null, Color? tuftLight = null, Color? matTint = null)
        {
            // Baked vertex colours + material tint define the look. Terrain MESH details ignore
            // the DetailPrototype healthy/dry colours, so a charred variant needs its OWN prefab
            // with dark vertex colours (not just a dark tint on the shared green one).
            Color dark = tuftDark ?? new Color(0.16f, 0.3f, 0.12f);
            Color light = tuftLight ?? new Color(0.4f, 0.62f, 0.24f);
            string prefabPath = $"{MaterialDir}/{id}.prefab";
            // Always rebuild so the up-normal + alpha-grass-texture fixes take effect on regen.
            AssetDatabase.DeleteAsset(prefabPath);
            AssetDatabase.DeleteAsset($"{MaterialDir}/{id}Mesh.asset");

            var mesh = new Mesh { name = "GrassTuftMesh" };
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>();
            void Quad(Quaternion rot)
            {
                int b = verts.Count;
                verts.Add(rot * new Vector3(-0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.5f, 0f, 0f));
                verts.Add(rot * new Vector3(0.35f, 1f, 0f));
                verts.Add(rot * new Vector3(-0.35f, 1f, 0f));
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                cols.Add(dark); cols.Add(dark); cols.Add(light); cols.Add(light);
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2,
                                      b, b + 1, b + 2, b, b + 2, b + 3 });   // double-sided
            }
            Quad(Quaternion.identity);
            Quad(Quaternion.Euler(0f, 90f, 0f));
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            // UP normals (not RecalculateNormals, which gives the vertical faces sideways
            // normals that catch no overhead light and render as the black shards that shelved
            // the terrain). Lit as ground cover, the instanced tufts read as bright grass.
            var upN = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++) upN.Add(Vector3.up);
            mesh.SetNormals(upN);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{MaterialDir}/{id}Mesh.asset");

            // Alpha-cut grass-blade texture (double-sided) so each quad reads as a fuzzy tuft,
            // not a solid green card. Falls back to a solid material if the texture is missing.
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Ground/grass_tuft_alpha.png");
            Color tint = matTint ?? Color.white;
            Material mat;
            if (grassTex != null)
            {
                string mp = $"{MaterialDir}/{id}Detail.mat";
                AssetDatabase.DeleteAsset(mp);
                mat = new Material(LitShader());
                mat.SetTexture("_BaseMap", grassTex);
                mat.SetColor("_BaseColor", tint);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
                mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON"); mat.SetFloat("_Cutoff", 0.3f);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                AssetDatabase.CreateAsset(mat, mp);
            }
            else mat = Mat($"{id}Mat", new Color(0.3f, 0.5f, 0.2f) * tint, smoothness: 0.05f);

            var go = new GameObject(id);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static Texture2D MakeGrassBladeTexture()
        {
            // A few soft vertical blades with alpha falloff — enough for the terrain
            // grass billboard shader to read as waving tufts, not solid cards.
            const int w = 32, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var px = new Color[w * h];
            var r = new System.Random(1234);
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
            for (int blade = 0; blade < 7; blade++)
            {
                int bx = 2 + r.Next(w - 4);
                float lean = ((float)r.NextDouble() - 0.5f) * 6f;
                int height = h - 4 - r.Next(8);
                for (int y = 0; y < height; y++)
                {
                    int x = bx + Mathf.RoundToInt(lean * y / (float)h);
                    if (x < 0 || x >= w) continue;
                    float t = y / (float)height;
                    var c = Color.Lerp(new Color(0.25f, 0.42f, 0.15f), new Color(0.45f, 0.65f, 0.25f), t);
                    c.a = 1f;
                    px[y * w + x] = c;
                    if (y < height / 2 && x + 1 < w) px[y * w + x + 1] = c;   // thicker base
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, $"{MaterialDir}/TL_GrassBlade_Tex.asset");
            return tex;
        }

        private static void BuildEmberfallGround(Transform root, int groundLayer)
        {
            var container = Child(root, "Ground");

            var grass = new Material[4];
            for (int i = 0; i < 4; i++)
                grass[i] = Mat($"EmberGrass{i}",
                    new Color(0.20f + i * 0.015f, 0.34f + i * 0.02f, 0.17f + i * 0.012f), smoothness: 0.05f);
            var sand = Mat("LakeSand", new Color(0.52f, 0.46f, 0.34f), smoothness: 0.05f);

            const float step = 12f;
            const float ringOuter = 250f;

            // z reaches -320 so the ember-approach ground meets the volcano entrance
            // tunnel (which starts at z=-315) with no fall-through gap.
            for (float x = -252f; x < 252f; x += step)
            {
                for (float z = -320f; z < 252f; z += step)
                {
                    float cx = x + step / 2f, cz = z + step / 2f;
                    if (InCityFootprint(cx, cz)) continue;      // city lays its own cobbles
                    if (InLakeWater(cx, cz)) continue;          // lake carves its basin
                    if (InFeederRiver(cx, cz)) continue;        // river channel stays open

                    bool approach = InApproachFootprint(cx, cz);
                    float ring = Mathf.Sqrt(cx * cx + cz * cz);
                    if (!approach && ring > ringOuter) continue; // trim the ring to a disc

                    // Gradual lush → soot → ember gradient (no stark diagonal line, img 17):
                    // interpolate the tile colour across a wide band toward the volcano.
                    float soot = SootAmount(cx, cz);
                    Material mat;
                    if (soot > 0.06f)
                    {
                        Color lush = new Color(0.21f, 0.34f, 0.17f);
                        Color ash = new Color(0.11f, 0.085f, 0.075f);
                        int bucket = Mathf.RoundToInt(soot * 6f);
                        mat = Mat($"EmberBlend{bucket}",
                            Color.Lerp(lush, ash, bucket / 6f), smoothness: 0.03f);
                    }
                    else
                    {
                        bool nearLake = Mathf.Abs(cx - LakeCenter.x) < LakeHalf + 10f
                                     && Mathf.Abs(cz - LakeCenter.z) < LakeHalf + 10f;
                        mat = nearLake ? sand : grass[rng.Next(grass.Length)];
                    }

                    var tile = Cube($"Ground_{x}_{z}", new Vector3(cx, -0.25f, cz),
                        new Vector3(step, 0.5f, step), mat, container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            // Grass blades across the lush ring (skip city / approach / water / far corners).
            BuildGrassBladeField(container, "EmberGrassBlades",
                -250f, 250f, -60f, 250f, 3,
                new Color(0.30f, 0.52f, 0.22f),
                (x, z) =>
                {
                    if (InCityFootprint(x, z) || InApproachFootprint(x, z)) return false;
                    if (InLakeWater(x, z) || InFeederRiver(x, z)) return false;
                    if (Mathf.Sqrt(x * x + z * z) > 248f) return false;
                    return true;
                });

            // Scattered trees fill the lush ring (dense forest, not a lawn).
            ScatterForestTrees(container, groundLayer);
            // Ground ecosystem so open fields aren't barren: rocks, ferns/bushes, wildflowers,
            // lakeside reeds (real Meshy meshes; each populates once its fbx lands).
            ScatterGroundDetail(container, groundLayer);
        }

        /// <summary>Ground-level ecosystem detail: scattered boulders, forest undergrowth (ferns +
        /// bushes), meadow wildflowers, and reeds hugging the lake shore. All real Meshy meshes,
        /// snapped to terrain; a missing fbx just means fewer of that kind (no cube fallback).</summary>
        private static void ScatterGroundDetail(Transform container, int groundLayer)
        {
            bool Blocked(float x, float z)
            {
                if (InCityFootprint(x, z) || InApproachFootprint(x, z)) return true;
                if (InLakeWater(x, z) || InFeederRiver(x, z) || InAnyRiver(x, z)) return true;
                if (Mathf.Abs(x) < 10f && z > -66f && z < 214f) return true;              // road
                if (DistToCityCanal(x, z) < 10f) return true;
                float ring = Mathf.Sqrt(x * x + z * z);
                if (ring < 60f || ring > 246f) return true;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) < LakeEdge(x, z) + 3f) return true;
                return false;
            }

            // Scatter in natural CLUMPS (a patch of ferns, a rock outcrop, a bed of flowers) rather
            // than a uniform sprinkle - reads far more like a real ecosystem, and dense enough to
            // actually see. Each clump picks a valid centre then drops N props around it.
            void Clump(int clumps, int perClump, float spread, System.Func<float, float, bool> extra, System.Action<float, float> place)
            {
                for (int c = 0; c < clumps; c++)
                {
                    float cx = Mathf.Lerp(-244f, 244f, (float)rng.NextDouble());
                    float cz = Mathf.Lerp(-52f, 246f, (float)rng.NextDouble());
                    if (Blocked(cx, cz) || (extra != null && !extra(cx, cz))) continue;
                    int n = perClump / 2 + rng.Next(perClump);
                    for (int k = 0; k < n; k++)
                    {
                        float x = cx + (float)(rng.NextDouble() - 0.5) * spread;
                        float z = cz + (float)(rng.NextDouble() - 0.5) * spread;
                        if (Blocked(x, z) || (extra != null && !extra(x, z))) continue;
                        place(x, z);
                    }
                }
            }
            System.Func<float, float, bool> lush = (x, z) => SootAmount(x, z) < 0.4f;
            System.Func<float, float, bool> meadow = (x, z) => SootAmount(x, z) < 0.2f;

            // Rock outcrops (clusters of boulders) - ~110 outcrops.
            Clump(110, 4, 14f, null, (x, z) =>
            {
                string rock = new[] { "BoulderCluster", "MossBoulder", "MossyRock" }[rng.Next(3)];
                PlaceMeshyProp(container, rock, new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, 1.3f + (float)rng.NextDouble() * 3.4f, groundLayer, PropCollision.FitBox);
            });
            // Pebble/gravel patches on bare-ish ground (flat, low).
            Clump(90, 5, 10f, null, (x, z) =>
                PlaceMeshyProp(container, "PebblePatch", new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, 1.6f + (float)rng.NextDouble() * 1.8f, groundLayer, PropCollision.None));
            // Dense fern + bush undergrowth beds in the lush ring.
            Clump(230, 6, 12f, lush, (x, z) =>
            {
                string f = rng.NextDouble() < 0.6 ? "Fern" : "ForestBush";
                if (PlaceMeshyProp(container, f, new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f,
                        0.8f + (float)rng.NextDouble() * 0.8f, groundLayer, PropCollision.None) == null)
                    PlaceMeshyProp(container, "ForestBush", new Vector3(x, 0f, z), 0f, 0.9f, groundLayer, PropCollision.None);
            });
            // Wildflower beds in the sunlit meadows.
            Clump(180, 8, 11f, meadow, (x, z) =>
                PlaceMeshyProp(container, "Wildflowers", new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, 0.5f + (float)rng.NextDouble() * 0.5f, groundLayer, PropCollision.None));

            // Reeds ringing the whole organic lake shore + river mouths (dense water-edge foliage).
            for (int i = 0; i < 420; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float le = LakeEdge(LakeCenter.x + dir.x * LakeHalf, LakeCenter.z + dir.y * LakeHalf);
                float r = le - 1f + (float)rng.NextDouble() * 6f;
                float x = LakeCenter.x + dir.x * r, z = LakeCenter.z + dir.y * r;
                if (InCityFootprint(x, z)) continue;
                PlaceMeshyProp(container, "Reeds", new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, 1.0f + (float)rng.NextDouble() * 1.0f, groundLayer, PropCollision.None);
            }
        }

        private static void ScatterForestTrees(Transform container, int groundLayer)
        {
            var trunkMat = Mat("EmberTreeTrunk", new Color(0.30f, 0.21f, 0.13f), smoothness: 0.05f);
            var leafMat = Mat("EmberPineLeaf", new Color(0.13f, 0.29f, 0.15f), smoothness: 0.08f);
            var charTrunk = Mat("EmberCharTrunk", new Color(0.15f, 0.11f, 0.09f), smoothness: 0.03f);
            var charLeaf = Mat("EmberCharLeaf", new Color(0.17f, 0.13f, 0.10f), smoothness: 0.03f);
            string[] kit = { "PineTree", "OakTree", "BirchTree", "PineTree", "OakTree", "DeadTree" };   // real Meshy trees; cube pine only if a load misses

            // Camps + POIs the trees must NOT bury.
            var keepClear = new Vector2[]
            {
                new Vector2(-195f, 0f),   // goblin camp W
                new Vector2(195f, 0f),    // bandit camp E
                new Vector2(-110f, -120f),// orc warcamp SW
                new Vector2(LakeCenter.x, LakeCenter.z), // lake
                new Vector2(0f, 195f),    // deep dark woods handled separately
                GladeCenter,              // Aldric's glade (player spawn)
                new Vector2(0f, 68f),     // the open path from the glade to the north gate
            };

            int planted = 0;
            // Dense forest (Jon: "way more trees... look like a forest not a swamp"). ~3x the
            // old count, biased toward the lush ring + the lake->city dark woods.
            for (int attempt = 0; attempt < 6000 && planted < 780; attempt++)
            {
                float x = Mathf.Lerp(-246f, 246f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-52f, 246f, (float)rng.NextDouble());
                float ring = Mathf.Sqrt(x * x + z * z);
                if (ring < 62f || ring > 246f) continue;       // clear the city + trim corners
                if (InCityFootprint(x, z) || InApproachFootprint(x, z)) continue;
                if (InLakeWater(x, z) || InFeederRiver(x, z)) continue;
                if (Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) < LakeEdge(x, z) + 10f) continue; // lake shore stays open
                // Keep roads + rivers/canal CLEAR (Jon: trees were growing on the road - lost the
                // intentional placement). Trees line the road but never block it.
                if (Mathf.Abs(x) < 11f && z > -66f && z < 214f) continue;        // N-S dirt road
                if (InAnyRiver(x, z) || DistToCityCanal(x, z) < 12f) continue;   // river beds + city canal

                bool blocked = false;
                foreach (var c in keepClear)
                    if (Vector2.Distance(new Vector2(x, z), c) < 22f) { blocked = true; break; }
                if (blocked) continue;

                planted++;
                float h = 6f + (float)rng.NextDouble() * 5f;
                // Transitional forest: healthy trees give way to charred/dead trees as the
                // ground sooties toward the volcano — a gradient, not a seam.
                float soot = SootAmount(x, z);
                string species = soot > 0.35f && rng.NextDouble() < soot ? "DeadTree" : kit[planted % kit.Length];
                if (PlaceMeshyProp(container, species, new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk) != null)
                    continue;
                var tm = soot > 0.4f ? charTrunk : trunkMat;
                var lm = soot > 0.4f ? charLeaf : leafMat;
                PlaceCubePine(container, planted, x, z, h, tm, lm, groundLayer);
            }

            // Progressive soot patches across the blend band (flat dark decals, no collider)
            // so the lush→ember transition reads as scattered scorching, not a hard edge.
            var patchMat = Mat("SootPatch", new Color(0.12f, 0.10f, 0.09f), smoothness: 0.02f);
            for (int attempt = 0; attempt < 500; attempt++)
            {
                float x = Mathf.Lerp(-140f, 140f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-150f, 40f, (float)rng.NextDouble());
                float soot = SootAmount(x, z);
                if (soot < 0.12f || soot > 0.85f) continue;         // only the transition band
                if ((float)rng.NextDouble() > soot) continue;        // denser toward the volcano
                if (InCityFootprint(x, z) || InGlade(x, z)) continue;
                float s = 2.5f + (float)rng.NextDouble() * 4f;
                var patch = Cube($"Soot_{attempt}", new Vector3(x, 0.02f, z),
                    new Vector3(s, 0.04f, s * (0.7f + (float)rng.NextDouble() * 0.6f)), patchMat, container);
                patch.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                patch.isStatic = true;
                Object.DestroyImmediate(patch.GetComponent<BoxCollider>());
            }
        }

        /// <summary>Cheap cube-pine silhouette used when a tree fbx hasn't downloaded yet.</summary>
        private static void PlaceCubePine(Transform container, int id, float x, float z, float h,
            Material trunkMat, Material leafMat, int groundLayer)
        {
            var trunk = Cube($"Pine{id}_Trunk", new Vector3(x, h * 0.28f, z),
                new Vector3(0.8f, h * 0.56f, 0.8f), trunkMat, container);
            trunk.isStatic = true;
            if (groundLayer >= 0) trunk.layer = groundLayer;
            var low = Cube($"Pine{id}_Low", new Vector3(x, h * 0.62f, z),
                new Vector3(h * 0.5f, h * 0.38f, h * 0.5f), leafMat, container);
            low.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 45f, 0);
            low.isStatic = true;
            Object.DestroyImmediate(low.GetComponent<BoxCollider>());   // walk through canopy
            var high = Cube($"Pine{id}_High", new Vector3(x, h * 0.92f, z),
                new Vector3(h * 0.32f, h * 0.3f, h * 0.32f), leafMat, container);
            high.isStatic = true;
            Object.DestroyImmediate(high.GetComponent<BoxCollider>());  // walk through canopy
        }

        // ==================== CITY ====================

        private static void BuildEmberfallCity(Transform root, int groundLayer)
        {
            var container = Child(root, "EmberfallCity");

            // ---- Cobbled ground (stone TEXTURE so it isn't a flat grey Minecraft grid) ----
            var stoneTex = GetGeneratedTexture("CityStone_Tex", () => GroundTextureGenerator.Rock(256));
            Material StoneMat(string n, Color c, float tileScale)
            {
                var m = Mat(n, c, smoothness: 0.1f);
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", stoneTex); m.SetTextureScale("_BaseMap", new Vector2(tileScale, tileScale)); }
                if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", stoneTex); m.SetTextureScale("_MainTex", new Vector2(tileScale, tileScale)); }
                return m;
            }
            var cobble = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                float v = 1.15f + i * 0.1f;   // >1 so the dark rock texture reads at cobble brightness
                cobble[i] = StoneMat($"Cobble{i}", new Color(v, v * 0.97f, v * 0.92f), 2.4f);
            }
            var streetMat = StoneMat("CityStreet", new Color(1.0f, 0.94f, 0.86f), 1.5f);
            const float tile = 5f;
            for (float x = -CityHalf; x < CityHalf; x += tile)
                for (float z = -CityHalf; z < CityHalf; z += tile)
                {
                    float cx = x + tile / 2f, cz = z + tile / 2f;
                    // Leave the canal channel OPEN (no cobble lid over the water — the "river runs
                    // under town" bug); the stone canal banks are built separately.
                    if (DistToCityCanal(cx, cz) < 8f) continue;
                    // Streets: cross axes between the four gates.
                    bool street = Mathf.Abs(cx) < 5f || Mathf.Abs(cz) < 5f;
                    var mat = street ? streetMat : cobble[rng.Next(cobble.Length)];
                    var t = Cube($"City_{x}_{z}", new Vector3(cx, -0.25f, cz),
                        new Vector3(tile, 0.5f, tile), mat, container);
                    t.isStatic = true;
                    if (groundLayer >= 0) t.layer = groundLayer;
                }

            BuildCityWalls(container, groundLayer);
            BuildCityCanal(container, groundLayer);

            // ---- Ember Fountain (plaza centre) ----
            BuildEmberFountain(container, new Vector3(0f, 0f, 0f), groundLayer);

            // ---- Districts (Meshy props, auto-colored fallback) ----
            var buildings = new (string name, Vector3 pos, float yaw, float h)[]
            {
                // NW district: the Ember Rest tavern is BUILT (enterable), not placed.
                ("Cottage",        new Vector3(-38f, 0f, 12f),  90f,  7f),
                ("TownHouse",      new Vector3(-46f, 0f, 46f),  140f, 8f),
                // Residential lane (W of the plaza)
                ("TownHouse",      new Vector3(-22f, 0f, 22f),  120f, 8f),
                ("Cottage",        new Vector3(-16f, 0f, 36f),  100f, 7f),
                // Market Row (NE) — a real double row flanking the east street
                ("MarketStall",    new Vector3(30f, 0f, 34f),   215f, 3.4f),
                ("MarketStall",    new Vector3(38f, 0f, 26f),   250f, 3.4f),
                ("MarketStall",    new Vector3(22f, 0f, 26f),   190f, 3.4f),
                ("MarketStall",    new Vector3(16f, 0f, 38f),   170f, 3.4f),
                ("TownHouse",      new Vector3(36f, 0f, 40f),   225f, 8f),
                ("TownHouse",      new Vector3(46f, 0f, 46f),   225f, 8f),
                // The Forge (SE) — the gear-upgrader is the F7 panel, so this is flavour.
                ("BlacksmithShop", new Vector3(34f, 0f, -30f),  315f, 8f),
                ("Storehouse",     new Vector3(40f, 0f, -12f),  270f, 7.5f),
                ("TownHouse",      new Vector3(22f, 0f, -24f),  340f, 8f),
                ("Storehouse",     new Vector3(46f, 0f, -44f),  300f, 7.5f),
                // Aldric's home + residential (SW)
                ("Cottage",        new Vector3(-34f, 0f, -30f), 45f,  7f),
                ("TownHouse",      new Vector3(-40f, 0f, -12f), 90f,  8f),
                ("Cottage",        new Vector3(-22f, 0f, -22f), 30f,  7f),
                ("TownHouse",      new Vector3(-46f, 0f, -44f), 60f,  8f),
            };
            foreach (var b in buildings)
                PlaceCityBuilding(container, b.name, b.pos, b.yaw, b.h, groundLayer);

            // Market dressing + lanterns.
            PlaceMeshyProp(container, "TownWell", new Vector3(18f, 0f, 18f), 0f, 3f, groundLayer);
            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(28f, 0f, 30f), 0f), (new Vector3(34f, 0f, 22f), 30f),
                (new Vector3(-30f, 0f, 30f), 0f),
            })
            {
                if (PlaceMeshyProp(container, "MarketBarrel", pos, yaw, 1.2f, groundLayer) == null)
                    PlaceCrate(container, pos, 1.1f, yaw, groundLayer);
            }
            // The Forge anvil (flavour object by the blacksmith).
            var anvilMat = Mat("ForgeAnvil", new Color(0.18f, 0.18f, 0.2f), smoothness: 0.3f);
            var anvil = Cube("ForgeAnvil", new Vector3(30f, 0.5f, -26f), new Vector3(1.6f, 1f, 0.7f), anvilMat, container);
            anvil.isStatic = true;
            if (groundLayer >= 0) anvil.layer = groundLayer;
            CreateTorch(container, new Vector3(27f, 0f, -26f), 0.9f); // forge glow

            // Plaza + street torches for warm, lived-in light — lamps pace the
            // four gate streets so the walk from any gate feels tended.
            foreach (var p in new[]
            {
                new Vector3(9f, 0f, 9f), new Vector3(-9f, 0f, 9f),
                new Vector3(9f, 0f, -9f), new Vector3(-9f, 0f, -9f),
                new Vector3(0f, 0f, 30f), new Vector3(0f, 0f, -30f),
                new Vector3(30f, 0f, 0f), new Vector3(-30f, 0f, 0f),
                new Vector3(6.5f, 0f, 18f), new Vector3(-6.5f, 0f, -18f),
                new Vector3(18f, 0f, 6.5f), new Vector3(-18f, 0f, -6.5f),
                new Vector3(6.5f, 0f, 42f), new Vector3(-6.5f, 0f, -42f),
                new Vector3(42f, 0f, 6.5f), new Vector3(-42f, 0f, -6.5f),
            })
                CreateTorch(container, p, 1f);

            // Ambient (decorative) townsfolk — gentle, non-hostile figures for life.
            PlaceTownsfolk(container, new Vector3(24f, 0f, 26f), new Color(0.4f, 0.32f, 0.5f), groundLayer);   // market
            PlaceTownsfolk(container, new Vector3(-26f, 0f, 24f), new Color(0.3f, 0.4f, 0.55f), groundLayer);  // tavern
            PlaceTownsfolk(container, new Vector3(28f, 0f, -22f), new Color(0.5f, 0.35f, 0.28f), groundLayer);  // forge
            PlaceTownsfolk(container, new Vector3(20f, 0f, 33f), new Color(0.35f, 0.45f, 0.3f), groundLayer);   // market row
            PlaceTownsfolk(container, new Vector3(-18f, 0f, 28f), new Color(0.55f, 0.4f, 0.3f), groundLayer);   // residential W
            PlaceTownsfolk(container, new Vector3(-24f, 0f, -18f), new Color(0.32f, 0.34f, 0.5f), groundLayer); // residential SW
            PlaceTownsfolk(container, new Vector3(8f, 0f, 14f), new Color(0.45f, 0.3f, 0.35f), groundLayer);    // plaza edge

            // Plaza furniture: benches ringing the fountain + planters + a hand cart,
            // so the square reads lived-in instead of a bare cobble field.
            var benchMat = Mat("PlazaBench", new Color(0.36f, 0.26f, 0.16f), smoothness: 0.08f);
            var planterMat = Mat("PlazaPlanter", new Color(0.3f, 0.28f, 0.27f), smoothness: 0.1f);
            var shrubMat = Mat("PlazaShrub", new Color(0.28f, 0.44f, 0.22f), smoothness: 0.05f);
            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(11f, 0f, 0f), 90f), (new Vector3(-11f, 0f, 0f), 90f),
                (new Vector3(0f, 0f, 11f), 0f), (new Vector3(0f, 0f, -11f), 0f),
            })
            {
                var seat = Cube("PlazaBench", pos + Vector3.up * 0.45f, new Vector3(2.4f, 0.16f, 0.7f), benchMat, container);
                seat.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                seat.isStatic = true;
                if (groundLayer >= 0) seat.layer = groundLayer;
                foreach (float s in new[] { -0.9f, 0.9f })
                {
                    var leg = Cube("PlazaBenchLeg",
                        pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(s, 0.19f, 0f),
                        new Vector3(0.22f, 0.38f, 0.6f), benchMat, container);
                    leg.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    leg.isStatic = true;
                    Object.DestroyImmediate(leg.GetComponent<BoxCollider>());
                }
            }
            foreach (var pos in new[]
            {
                new Vector3(14f, 0f, 14f), new Vector3(-14f, 0f, 14f),
                new Vector3(14f, 0f, -14f), new Vector3(-14f, 0f, -14f),
            })
            {
                var box = Cube("PlazaPlanter", pos + Vector3.up * 0.35f, new Vector3(1.4f, 0.7f, 1.4f), planterMat, container);
                box.isStatic = true;
                if (groundLayer >= 0) box.layer = groundLayer;
                var shrub = Cube("PlazaShrub", pos + Vector3.up * 1.05f, new Vector3(1.05f, 0.8f, 1.05f), shrubMat, container);
                shrub.isStatic = true;
                Object.DestroyImmediate(shrub.GetComponent<BoxCollider>());
            }
            // Hand cart by Market Row.
            var cartMat = Mat("PlazaCart", new Color(0.4f, 0.29f, 0.17f), smoothness: 0.08f);
            var cartBed = Cube("CartBed", new Vector3(12f, 0.8f, 26f), new Vector3(2.6f, 0.3f, 1.5f), cartMat, container);
            cartBed.transform.rotation = Quaternion.Euler(0f, 25f, -7f);
            cartBed.isStatic = true;
            if (groundLayer >= 0) cartBed.layer = groundLayer;
            foreach (float s in new[] { -1f, 1f })
            {
                var wheel = Cylinder("CartWheel", new Vector3(12f, 0.55f, 26f) + new Vector3(s * 0.9f, 0f, 0.4f),
                    new Vector3(0.55f, 0.08f, 0.55f), cartMat, container);
                wheel.transform.rotation = Quaternion.Euler(0f, 25f, 90f);
                wheel.isStatic = true;
                Object.DestroyImmediate(wheel.GetComponent<CapsuleCollider>());
            }

            // ---- NPCs ----
            // Aldric is NOT in the city — he's the old hermit in the forest glade north of
            // town (see BuildAldricGlade), where the intro cutscene meets him. Only the
            // Elowen (q_rescue giver, npcId "elowen") is placed from npcs.json at (10,0,-6),
            // replacing the old hardcoded Worried Mother; the rescue cutscenes still frame there.
            BuildVendorNPC(container, groundLayer);
            BuildEmberRestTavern(container, groundLayer);
        }

        /// <summary>
        /// The Ember Rest: a real ENTERABLE tavern in the NW district. Constructed
        /// room (door gap, windows, bar, tables, fireplace) with an innkeeper vendor
        /// and patrons inside: proof the city is a place, not a diorama.
        /// </summary>
        private static void BuildEmberRestTavern(Transform parent, int groundLayer)
        {
            var container = Child(parent, "EmberRestTavern");
            Vector3 c = new Vector3(-32f, 0f, 32f);
            const float halfX = 9f, halfZ = 7f, wallH = 4.6f;

            var timberMat = Mat("TavernTimber", new Color(0.34f, 0.24f, 0.15f), smoothness: 0.08f);
            var plasterMat = Mat("TavernPlaster", new Color(0.72f, 0.66f, 0.55f), smoothness: 0.06f);
            var roofMat = Mat("TavernRoof", new Color(0.4f, 0.2f, 0.14f), smoothness: 0.05f);
            var floorMat = Mat("TavernFloor", new Color(0.3f, 0.22f, 0.13f), smoothness: 0.1f);
            var windowMat = Mat("TavernWindow", new Color(1f, 0.75f, 0.4f), smoothness: 0.4f,
                emission: new Color(1f, 0.62f, 0.25f) * 1.6f);

            void Solid(string n, Vector3 pos, Vector3 size, Material m)
            {
                var o = Cube(n, pos, size, m, container);
                o.isStatic = true;
                if (groundLayer >= 0) o.layer = groundLayer;
            }

            // Floor + walls. South wall splits around a 2.6m doorway facing the plaza.
            Solid("Tav_Floor", c + new Vector3(0f, 0.06f, 0f), new Vector3(halfX * 2f, 0.12f, halfZ * 2f), floorMat);
            Solid("Tav_WallN", c + new Vector3(0f, wallH / 2f, halfZ), new Vector3(halfX * 2f, wallH, 0.5f), plasterMat);
            Solid("Tav_WallW", c + new Vector3(-halfX, wallH / 2f, 0f), new Vector3(0.5f, wallH, halfZ * 2f), plasterMat);
            Solid("Tav_WallE", c + new Vector3(halfX, wallH / 2f, 0f), new Vector3(0.5f, wallH, halfZ * 2f), plasterMat);
            float doorHalf = 1.3f, segW = (halfX * 2f - doorHalf * 2f) / 2f;
            Solid("Tav_WallS_L", c + new Vector3(-(doorHalf + segW / 2f), wallH / 2f, -halfZ), new Vector3(segW, wallH, 0.5f), plasterMat);
            Solid("Tav_WallS_R", c + new Vector3(doorHalf + segW / 2f, wallH / 2f, -halfZ), new Vector3(segW, wallH, 0.5f), plasterMat);
            Solid("Tav_DoorLintel", c + new Vector3(0f, wallH - 0.5f, -halfZ), new Vector3(doorHalf * 2f + 0.6f, 1f, 0.5f), timberMat);

            // Roof: two pitched slabs + ridge beam.
            foreach (var (sx, yaw) in new[] { (-1f, 24f), (1f, -24f) })
            {
                var slab = Cube($"Tav_Roof{(sx < 0 ? "W" : "E")}",
                    c + new Vector3(sx * halfX * 0.5f, wallH + 1.3f, 0f), new Vector3(halfX * 1.25f, 0.35f, halfZ * 2f + 1.2f), roofMat, container);
                slab.transform.rotation = Quaternion.Euler(0f, 0f, yaw * sx >= 0 ? yaw : yaw);
                slab.transform.rotation = Quaternion.Euler(0f, 0f, sx * -24f);
                slab.isStatic = true;
                if (groundLayer >= 0) slab.layer = groundLayer;
            }
            Solid("Tav_Ridge", c + new Vector3(0f, wallH + 2.4f, 0f), new Vector3(0.6f, 0.4f, halfZ * 2f + 1.4f), timberMat);

            // Corner timbers + glowing windows on the plaza face.
            foreach (var (wx, wz) in new[] { (-halfX, -halfZ), (halfX, -halfZ), (-halfX, halfZ), (halfX, halfZ) })
                Solid("Tav_Corner", c + new Vector3(wx, wallH / 2f, wz), new Vector3(0.7f, wallH, 0.7f), timberMat);
            foreach (float wx in new[] { -5.4f, 5.4f })
            {
                var win = Cube("Tav_Window", c + new Vector3(wx, 2.2f, -halfZ - 0.05f), new Vector3(1.6f, 1.3f, 0.15f), windowMat, container);
                Object.DestroyImmediate(win.GetComponent<BoxCollider>());
            }

            // ---- Interior: bar, tables, fireplace ----
            Solid("Tav_Bar", c + new Vector3(0f, 0.62f, halfZ - 2f), new Vector3(8f, 1.1f, 1.1f), timberMat);
            foreach (var tp in new[] { new Vector3(-4.5f, 0f, -2f), new Vector3(4.5f, 0f, -2f), new Vector3(0f, 0f, 1.5f) })
            {
                Solid("Tav_Table", c + tp + Vector3.up * 0.5f, new Vector3(1.7f, 0.12f, 1.7f), timberMat);
                Solid("Tav_TableLeg", c + tp + Vector3.up * 0.25f, new Vector3(0.3f, 0.5f, 0.3f), timberMat);
                foreach (var bp in new[] { new Vector3(1.4f, 0f, 0f), new Vector3(-1.4f, 0f, 0f) })
                    Solid("Tav_Bench", c + tp + bp + Vector3.up * 0.28f, new Vector3(0.5f, 0.55f, 1.5f), timberMat);
            }

            // Fireplace on the east wall with a warm flicker.
            Solid("Tav_Hearth", c + new Vector3(halfX - 0.9f, 1f, 3.5f), new Vector3(1.4f, 2f, 2.4f), Mat("TavernHearth", new Color(0.32f, 0.3f, 0.3f), smoothness: 0.1f));
            var fireLight = new GameObject("Tav_FireLight");
            fireLight.transform.SetParent(container);
            fireLight.transform.position = c + new Vector3(halfX - 1.6f, 1.2f, 3.5f);
            var fl = fireLight.AddComponent<Light>();
            fl.type = LightType.Point; fl.color = new Color(1f, 0.6f, 0.3f); fl.range = 11f; fl.intensity = 2.2f;
            fl.shadows = LightShadows.None;
            fireLight.AddComponent<World.TorchFlicker>().Configure(2.4f, 0.7f, 6f);

            // Warm interior fill so the room reads cozy, not cave-dark.
            var fill = new GameObject("Tav_Fill");
            fill.transform.SetParent(container);
            fill.transform.position = c + Vector3.up * 3.4f;
            var fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point; fillLight.color = new Color(1f, 0.8f, 0.55f);
            fillLight.range = 13f; fillLight.intensity = 1.5f; fillLight.shadows = LightShadows.None;

            // Innkeeper (food vendor) behind the bar + two patrons.
            BuildOneVendor(container, groundLayer, "Innkeeper Hobb", "Hobb",
                c + new Vector3(0f, 0f, halfZ - 3.4f), 180f,
                new[] { "cooked_fish", "cooked_trout", "cooked_bass", "cooked_carp" });
            PlaceTownsfolk(container, c + new Vector3(-4.5f, 0f, -0.6f), new Color(0.4f, 0.32f, 0.5f), groundLayer);
            PlaceTownsfolk(container, c + new Vector3(4.5f, 0f, -3.2f), new Color(0.3f, 0.4f, 0.55f), groundLayer);

            // Hanging sign out front.
            Solid("Tav_SignArm", c + new Vector3(2.6f, 3.4f, -halfZ - 0.7f), new Vector3(1.6f, 0.14f, 0.14f), timberMat);
            var sign = Cube("Tav_Sign", c + new Vector3(3.2f, 2.7f, -halfZ - 0.7f), new Vector3(1.2f, 0.9f, 0.1f),
                Mat("TavernSign", new Color(0.55f, 0.3f, 0.15f), smoothness: 0.2f, emission: new Color(0.35f, 0.14f, 0.04f)), container);
            Object.DestroyImmediate(sign.GetComponent<BoxCollider>());
        }

        /// <summary>
        /// Marla the Trader — the Market Row vendor (E to trade). Opens the buy/sell
        /// VendorPanel on the interacting player. Non-networked like the other NPCs
        /// (the economy is client-local, same model as the F7 Forge).
        /// </summary>
        private static void BuildVendorNPC(Transform parent, int groundLayer)
        {
            // The market's three traders + the working forgemaster: a FUNCTIONAL city.
            BuildOneVendor(parent, groundLayer, "Marla the Trader", "Marla", new Vector3(26f, 0f, 31f), 200f,
                new[] { "cooked_fish", "cooked_trout", "fishing_pole", "leather_cap", "iron_helm", "apprentice_focus" });
            BuildOneVendor(parent, groundLayer, "Torvald the Armorer", "Torvald", new Vector3(36f, 0f, -26f), 320f,
                new[] { "rusty_sword", "knight_sword", "iron_helm", "iron_chestplate", "iron_legs", "bandit_cape" });
            BuildOneVendor(parent, groundLayer, "Sela the Provisioner", "Sela", new Vector3(14f, 0f, 36f), 170f,
                new[] { "cooked_fish", "cooked_trout", "cooked_bass", "raw_fish", "fishing_pole", "apprentice_wand", "arrow", "short_bow" });

            // Forgemaster at the anvil: E opens the +X combine panel in-world.
            var forgeNpc = new GameObject("ForgeNPC");
            int il = LayerMask.NameToLayer("Interactable");
            forgeNpc.transform.SetParent(parent);
            forgeNpc.transform.position = new Vector3(28.5f, 0f, -24.5f);
            forgeNpc.transform.rotation = Quaternion.Euler(0f, 300f, 0f);
            if (il >= 0) forgeNpc.layer = il;
            var forge = forgeNpc.AddComponent<World.ForgeNPC>();
            var forgeSO = new SerializedObject(forge);
            forgeSO.FindProperty("prompt").stringValue = "Forge with Brammon";
            forgeSO.FindProperty("radius").floatValue = 3.5f;
            forgeSO.ApplyModifiedProperties();
            var forgeSolid = forgeNpc.AddComponent<CapsuleCollider>();
            forgeSolid.center = new Vector3(0f, 0.95f, 0f); forgeSolid.height = 1.9f; forgeSolid.radius = 0.38f;
            var forgeBody = SceneSetup.InstantiateGeneratedVisual("HeroMale", forgeNpc.transform);
            if (forgeBody != null) { forgeBody.name = "Body"; forgeBody.transform.localPosition = Vector3.zero; }
        }

        private static void BuildOneVendor(Transform parent, int groundLayer, string displayName,
            string shortName, Vector3 pos, float yaw, string[] stock)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            var npcObj = new GameObject($"Vendor{shortName}");
            npcObj.transform.SetParent(parent);
            npcObj.transform.position = pos;
            npcObj.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (interactableLayer >= 0) npcObj.layer = interactableLayer;

            var solid = npcObj.AddComponent<CapsuleCollider>();
            solid.center = new Vector3(0f, 0.95f, 0f); solid.height = 1.9f; solid.radius = 0.38f;

            var vendor = npcObj.AddComponent<World.VendorNPC>();
            var vendorSO = new SerializedObject(vendor);
            vendorSO.FindProperty("displayName").stringValue = displayName;
            vendorSO.FindProperty("prompt").stringValue = $"Trade with {shortName}";
            vendorSO.FindProperty("radius").floatValue = 3.5f;
            var stockProp = vendorSO.FindProperty("stock");
            stockProp.arraySize = stock.Length;
            for (int i = 0; i < stock.Length; i++)
                stockProp.GetArrayElementAtIndex(i).stringValue = stock[i];
            vendorSO.ApplyModifiedProperties();

            var model = SceneSetup.InstantiateGeneratedVisual("HeroFemale", npcObj.transform);
            if (model != null)
            {
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
            }
            else
            {
                var robeMat = Mat("VendorRobe", new Color(0.55f, 0.38f, 0.2f), smoothness: 0.1f);
                var skinMat = Mat("VendorSkin", new Color(0.8f, 0.62f, 0.5f), smoothness: 0.1f);
                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(npcObj.transform);
                body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                body.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
                body.GetComponent<MeshRenderer>().sharedMaterial = robeMat;
                if (groundLayer >= 0) body.layer = groundLayer;

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(npcObj.transform);
                head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
                head.transform.localScale = Vector3.one * 0.55f;
                Object.DestroyImmediate(head.GetComponent<SphereCollider>());
                head.GetComponent<MeshRenderer>().sharedMaterial = skinMat;
                if (groundLayer >= 0) head.layer = groundLayer;
            }

            // Warm stall light so the vendor reads from across the plaza.
            var lightObj = new GameObject("VendorLight");
            lightObj.transform.SetParent(npcObj.transform);
            lightObj.transform.localPosition = Vector3.up * 2.6f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.95f, 0.8f, 0.5f);
            light.intensity = 1.6f;
            light.range = 5f;
            light.shadows = LightShadows.None;
        }

        /// <summary>The manmade stone canal: R3 threads the city, so line its banks with cut-stone
        /// coping and bridge the streets. This is the ONLY water in the world that should read as
        /// built (Jon: city canal is the one manmade-water exception). The channel itself is the
        /// carved terrain + WorldWater sheet + the reach's swim volume; this adds the built edges.</summary>
        private static void BuildCityCanal(Transform container, int groundLayer)
        {
            var stoneMat = Mat("CanalStone", new Color(0.44f, 0.42f, 0.39f), smoothness: 0.16f);
            var stoneDark = Mat("CanalStoneDark", new Color(0.31f, 0.30f, 0.28f), smoothness: 0.12f);
            var pts = RiverPaths[3];
            const float half = 7.2f;   // coping sits just outside the ~8m open channel

            for (int si = 0; si < pts.Length - 1; si++)
            {
                Vector3 a = pts[si], b = pts[si + 1];
                Vector3 seg = b - a; float len = seg.magnitude;
                if (len < 0.01f) continue;
                Vector3 dir = seg / len;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 rightN = new Vector3(dir.z, 0f, -dir.x);   // horizontal perpendicular
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 3.2f));
                for (int k = 0; k <= n; k++)
                {
                    Vector3 c = a + dir * (len * k / n);
                    // Only the reach that actually threads the city gets stone banks.
                    if (Mathf.Abs(c.x) > CityHalf + 3f || Mathf.Abs(c.z) > CityHalf + 3f) continue;
                    foreach (float sgn in new[] { 1f, -1f })
                    {
                        var wall = Cube($"Canal_{si}_{k}_{(sgn > 0 ? "R" : "L")}",
                            c + rightN * (sgn * (half + 0.6f)) + Vector3.up * (-0.5f),
                            new Vector3(1.3f, 2.4f, 3.4f), (k % 2 == 0) ? stoneMat : stoneDark, container);
                        wall.transform.rotation = rot; wall.isStatic = true;
                        if (groundLayer >= 0) wall.layer = groundLayer;
                    }
                }
            }

            // Stone footbridges where the two main streets cross the canal (canal is near z~7-9
            // through the plaza). Decks span the channel; keep them thin so they read as bridges.
            foreach (var at in new[] { new Vector3(0f, 0.15f, 7f), new Vector3(28f, 0.15f, 10f), new Vector3(-30f, 0.15f, 26f) })
            {
                var deck = Cube($"CanalBridge_{(int)at.x}", at, new Vector3(6.5f, 0.5f, half * 2f + 5f), stoneMat, container);
                deck.isStatic = true;
                if (groundLayer >= 0) deck.layer = groundLayer;
                // Low parapets on the deck sides.
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var rail = Cube($"CanalRail_{(int)at.x}_{(sgn > 0 ? "R" : "L")}",
                        at + new Vector3(sgn * 3.0f, 0.6f, 0f), new Vector3(0.5f, 1.0f, half * 2f + 5f), stoneDark, container);
                    rail.isStatic = true;
                    if (groundLayer >= 0) rail.layer = groundLayer;
                }
            }
        }

        private static void BuildCityWalls(Transform container, int groundLayer)
        {
            var wallStoneTex = GetGeneratedTexture("CityStone_Tex", () => GroundTextureGenerator.Rock(256));
            Material WallStone(string n, Color c, float tileScale)
            {
                var m = Mat(n, c, smoothness: 0.08f);
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", wallStoneTex); m.SetTextureScale("_BaseMap", new Vector2(tileScale, tileScale)); }
                if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", wallStoneTex); m.SetTextureScale("_MainTex", new Vector2(tileScale, tileScale)); }
                return m;
            }
            var wallMat = WallStone("CityWall", new Color(1.05f, 0.98f, 0.92f), 3.5f);
            var pillarMat = WallStone("CityGatePillar", new Color(1.2f, 1.12f, 1.05f), 2.5f);
            const float h = 7f, thick = 1.5f;
            float len = CityHalf * 2f + thick * 2f;
            float seg = (len - CityGate) / 2f;

            void Wall(string n, Vector3 pos, Vector3 size)
            {
                var w = Cube(n, pos, size, wallMat, container);
                w.isStatic = true;
                if (groundLayer >= 0) w.layer = groundLayer;
            }
            void Gate(string side, Vector3 center, bool alongX, string label)
            {
                // Split wall around a CityGate-wide opening at 'center'.
                if (alongX)
                {
                    Wall($"Wall_{side}_A", center + new Vector3(-(CityGate + seg) / 2f, h / 2f, 0f), new Vector3(seg, h, thick));
                    Wall($"Wall_{side}_B", center + new Vector3((CityGate + seg) / 2f, h / 2f, 0f), new Vector3(seg, h, thick));
                }
                else
                {
                    Wall($"Wall_{side}_A", center + new Vector3(0f, h / 2f, -(CityGate + seg) / 2f), new Vector3(thick, h, seg));
                    Wall($"Wall_{side}_B", center + new Vector3(0f, h / 2f, (CityGate + seg) / 2f), new Vector3(thick, h, seg));
                }
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 off = alongX ? new Vector3(s * (CityGate / 2f + 0.9f), 4.5f, 0f)
                                         : new Vector3(0f, 4.5f, s * (CityGate / 2f + 0.9f));
                    var p = Cube($"Gate_{side}_P{s}", center + off, new Vector3(alongX ? 1.8f : 2.4f, 9f, alongX ? 2.4f : 1.8f), pillarMat, container);
                    p.isStatic = true;
                    if (groundLayer >= 0) p.layer = groundLayer;
                }
                var lintel = Cube($"Gate_{side}_Lintel", center + Vector3.up * 8.4f,
                    new Vector3(alongX ? CityGate + 4f : 2.6f, 1.6f, alongX ? 2.6f : CityGate + 4f), pillarMat, container);
                lintel.isStatic = true;
                CreateTorch(container, center + (alongX ? new Vector3(-(CityGate / 2f + 0.9f), 0, 0) : new Vector3(0, 0, -(CityGate / 2f + 0.9f))), 1f);
                CreateTorch(container, center + (alongX ? new Vector3(CityGate / 2f + 0.9f, 0, 0) : new Vector3(0, 0, CityGate / 2f + 0.9f)), 1f);

                // Named "EMBERFALL" plate over the north gate; charred warning at the south gate.
                if (label != null)
                {
                    var signMat = label == "EMBERFALL"
                        ? Mat("CitySignN", new Color(0.55f, 0.42f, 0.22f), smoothness: 0.1f, emission: new Color(0.3f, 0.18f, 0.05f))
                        : Mat("CitySignS", new Color(0.2f, 0.11f, 0.08f), smoothness: 0.05f, emission: new Color(0.35f, 0.08f, 0.02f));
                    var plate = Cube($"Gate_{side}_Sign", center + Vector3.up * 6.6f,
                        new Vector3(alongX ? CityGate + 2f : 0.4f, 1.2f, alongX ? 0.4f : CityGate + 2f), signMat, container);
                    Object.DestroyImmediate(plate.GetComponent<BoxCollider>());
                }
            }

            Gate("N", new Vector3(0f, 0f, CityHalf + thick / 2f), true, "EMBERFALL");
            Gate("S", new Vector3(0f, 0f, -CityHalf - thick / 2f), true, "WARN");
            Gate("E", new Vector3(CityHalf + thick / 2f, 0f, 0f), false, null);
            Gate("W", new Vector3(-CityHalf - thick / 2f, 0f, 0f), false, null);

            // Corner towers.
            float t = CityHalf + thick / 2f;
            foreach (var c in new[] { new Vector3(t, 0, t), new Vector3(-t, 0, t), new Vector3(t, 0, -t), new Vector3(-t, 0, -t) })
            {
                var tower = Cube("CityTower", c + Vector3.up * 5.5f, new Vector3(5f, 11f, 5f), wallMat, container);
                tower.isStatic = true;
                if (groundLayer >= 0) tower.layer = groundLayer;
                CreateTorch(container, c - c.normalized * 3.2f, 0.9f);
            }

            // Charred arch telegraphing danger just OUTSIDE the south gate.
            var charMat = Mat("CharredArch", new Color(0.12f, 0.09f, 0.08f), smoothness: 0.03f,
                emission: new Color(0.25f, 0.06f, 0.02f));
            foreach (float s in new[] { -1f, 1f })
            {
                var post = Cube($"CharArch_P{s}", new Vector3(s * 6f, 3f, -CityHalf - 6f), new Vector3(1.2f, 6f, 1.2f), charMat, container);
                post.transform.rotation = Quaternion.Euler(0, 0, s * 8f);
                post.isStatic = true;
                if (groundLayer >= 0) post.layer = groundLayer;
            }
            var beam = Cube("CharArch_Beam", new Vector3(0f, 6f, -CityHalf - 6f), new Vector3(14f, 1f, 1.2f), charMat, container);
            beam.isStatic = true;
        }

        private static void PlaceCityBuilding(Transform container, string name, Vector3 pos, float yaw, float h, int groundLayer)
        {
            if (PlaceMeshyProp(container, name, pos, yaw, h, groundLayer) != null) return;
            // Fallback: a solid timber block so the district is never an empty lot.
            var mat = Mat($"House_{name}", new Color(0.48f, 0.38f, 0.26f), smoothness: 0.06f);
            var roofMat = Mat("HouseRoof", new Color(0.34f, 0.2f, 0.16f), smoothness: 0.05f);
            var body = Cube($"{name}_Body", pos + Vector3.up * (h * 0.32f), new Vector3(h * 0.8f, h * 0.64f, h * 0.7f), mat, container);
            body.transform.rotation = Quaternion.Euler(0, yaw, 0);
            body.isStatic = true;
            if (groundLayer >= 0) body.layer = groundLayer;
            var roof = Cube($"{name}_Roof", pos + Vector3.up * (h * 0.7f), new Vector3(h * 0.9f, h * 0.18f, h * 0.8f), roofMat, container);
            roof.transform.rotation = Quaternion.Euler(0, yaw, 0);
            roof.isStatic = true;
        }

        private static void PlaceCrate(Transform container, Vector3 pos, float size, float yaw, int groundLayer)
        {
            var c = Cube("Crate", pos + Vector3.up * size / 2f, Vector3.one * size,
                Mat("CityCrate", new Color(0.42f, 0.3f, 0.18f), smoothness: 0.08f), container);
            c.transform.rotation = Quaternion.Euler(0, yaw, 0);
            c.isStatic = true;
            if (groundLayer >= 0) c.layer = groundLayer;
        }

        /// <summary>
        /// A non-networked decorative townsperson. Uses the generated HeroMale/HeroFemale
        /// base bodies (alternating, idling via their animator) when available — the old
        /// giant robed capsules read as placeholder blobs in every QA shot. The robe color
        /// keeps deciding gender pick + survives as the capsule fallback tint.
        /// </summary>
        private static int townsfolkCounter;
        private static readonly string[] TownsfolkNames =
        {
            "Mera", "Old Casselt", "Jorun", "Willa", "Fenn", "Dara",
            "Halvard", "Petra", "Ostin", "Lysa", "Brame", "Tessi",
        };
        private static readonly string[][] TownsfolkBarks =
        {
            new[] { "Fine day, when the ash stays south.", "You look like you can handle a blade." },
            new[] { "The lake fish bite best at dusk.", "My knees ache when the mountain grumbles." },
            new[] { "Stay clear of the west woods, goblins about.", "Marla overcharges, but her fish is fresh." },
            new[] { "They say a hermit lives north of the walls.", "Heard the warcamp drums again last night." },
            new[] { "Brammon can make two blades into a better one.", "The rivers run colder every season." },
        };

        private static void PlaceTownsfolk(Transform container, Vector3 pos, Color robe, int groundLayer)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            var npc = new GameObject("Townsfolk");
            npc.transform.SetParent(container);
            npc.transform.position = pos;
            npc.transform.rotation = Quaternion.Euler(0, rng.Next(0, 360), 0);
            if (interactableLayer >= 0) npc.layer = interactableLayer;

            // SOLID: a capsule collider on the root so players cannot walk through
            // people, plus a talk interaction. Functional citizens, not scenery.
            var solid = npc.AddComponent<CapsuleCollider>();
            solid.center = new Vector3(0f, 0.95f, 0f);
            solid.height = 1.9f;
            solid.radius = 0.38f;

            var talk = npc.AddComponent<World.TownsfolkNPC>();
            var talkSO = new SerializedObject(talk);
            string npcName = TownsfolkNames[townsfolkCounter % TownsfolkNames.Length];
            talkSO.FindProperty("displayName").stringValue = npcName;
            talkSO.FindProperty("prompt").stringValue = $"Talk to {npcName}";
            talkSO.FindProperty("radius").floatValue = 3f;
            var barkSet = TownsfolkBarks[townsfolkCounter % TownsfolkBarks.Length];
            var barksProp = talkSO.FindProperty("barks");
            barksProp.arraySize = barkSet.Length;
            for (int i = 0; i < barkSet.Length; i++)
                barksProp.GetArrayElementAtIndex(i).stringValue = barkSet[i];
            talkSO.ApplyModifiedProperties();

            string bodyName = (townsfolkCounter++ % 2 == 0) ? "HeroMale" : "HeroFemale";
            var model = SceneSetup.InstantiateGeneratedVisual(bodyName, npc.transform);
            if (model != null)
            {
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                return;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(npc.transform);
            body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            body.transform.localScale = new Vector3(0.72f, 0.95f, 0.72f);
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = SceneSetup.GetOrCreateMaterialAsset(robe, $"Townsfolk_{robe.r:F2}");
            if (groundLayer >= 0) body.layer = groundLayer;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(npc.transform);
            head.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            head.transform.localScale = Vector3.one * 0.5f;
            Object.DestroyImmediate(head.GetComponent<SphereCollider>());
            head.GetComponent<MeshRenderer>().sharedMaterial = SceneSetup.GetOrCreateMaterialAsset(new Color(0.82f, 0.66f, 0.52f), "TownsfolkSkin");
            if (groundLayer >= 0) head.layer = groundLayer;
        }

        private static void BuildEmberFountain(Transform container, Vector3 center, int groundLayer)
        {
            var stoneMat = Mat("FountainStone", new Color(0.42f, 0.4f, 0.4f), smoothness: 0.2f);
            var emberMat = Mat("FountainEmber", new Color(1f, 0.45f, 0.12f), smoothness: 0.5f,
                emission: new Color(1f, 0.4f, 0.1f) * 3.2f);

            var basin = Cylinder("Fountain_Basin", center + Vector3.up * 0.5f, new Vector3(7f, 1f, 7f), stoneMat, container);
            basin.isStatic = true;
            if (groundLayer >= 0) basin.layer = groundLayer;
            var column = Cylinder("Fountain_Column", center + Vector3.up * 1.6f, new Vector3(1.4f, 1.6f, 1.4f), stoneMat, container);
            column.isStatic = true;
            if (groundLayer >= 0) column.layer = groundLayer;
            var bowl = Cylinder("Fountain_Ember", center + Vector3.up * 2.5f, new Vector3(2.6f, 0.3f, 2.6f), emberMat, container);
            Object.DestroyImmediate(bowl.GetComponent<Collider>());

            var lightObj = new GameObject("Fountain_Light");
            lightObj.transform.SetParent(container);
            lightObj.transform.position = center + Vector3.up * 3f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.2f);
            light.range = 20f;
            light.shadows = LightShadows.None;
            lightObj.AddComponent<World.TorchFlicker>().Configure(3.2f, 0.9f, 6f);
        }

        // ==================== LAKE (NW, swimmable) ====================

        private static void BuildEmberfallLake(Transform root, int groundLayer)
        {
            var container = Child(root, "EmberfallLake");
            int waterLayer = LayerMask.NameToLayer("Water");
            int interactableLayer = LayerMask.NameToLayer("Interactable");

            // Keep the basin off the NavMesh so mobs hold the shoreline.
            var mod = container.gameObject.AddComponent<NavMeshModifier>();
            mod.overrideArea = true;
            mod.area = 1;

            var mudMat = Mat("LakeMud", new Color(0.26f, 0.20f, 0.13f), smoothness: 0.05f);
            var basinMat = Mat("LakeBasin", new Color(0.16f, 0.19f, 0.20f), smoothness: 0.25f);
            // Deeper, more OPAQUE water (alpha 0.78) so the stepped carved bed + overlapping
            // river-segment quads stop showing through as tiled/banded stripes. Natural blue-green,
            // lower smoothness so it stops throwing harsh white sun-glints.
            var waterMat = Mat("LakeWater", new Color(0.12f, 0.32f, 0.40f, 0.80f),
                smoothness: 0.62f, transparent: true, emission: new Color(0.01f, 0.035f, 0.045f));

            Vector3 c = LakeCenter;
            float w = LakeHalf * 2f, depth = WaterSurfaceY - LakeBedY;   // depth ~2.7m (>1.8 swim)
            const float openHalf = 30f;                                  // deep swimmable core half-width

            // Deep bed under the open water (solid, Ground layer). Legacy flat-ground only;
            // in terrain mode the carved basin is the bed.
            if (!UseUnityTerrain)
            {
                var bed = Cube("Lake_Bed", c + Vector3.up * (LakeBedY - 0.35f),
                    new Vector3(openHalf * 2f + 4f, 0.7f, openHalf * 2f + 4f), basinMat, container);
                bed.isStatic = true;
                if (groundLayer >= 0) bed.layer = groundLayer;
            }

            // Sloped MUD banks all around (no man-made steps, img 21): each side shelves
            // gently from the surrounding ground (y=0) down to the deep bed. Banks span the
            // full width so they overlap at the corners (no gaps). Tilt sign puts the OUTER
            // edge at ground level and the INNER edge at the bed.
            float run = LakeHalf - openHalf;                  // horizontal shelf run (~14m)
            float slabLen = Mathf.Sqrt(run * run + depth * depth) + 1.5f;
            float tilt = Mathf.Atan2(depth, run) * Mathf.Rad2Deg;  // ~10-11°
            float mid = (openHalf + LakeHalf) / 2f, midY = (LakeBedY + WaterSurfaceY) / 2f;
            void MudBank(string n, Vector3 pos, Vector3 size, Vector3 euler)
            {
                var b = Cube(n, pos, size, mudMat, container);
                b.transform.rotation = Quaternion.Euler(euler);
                b.isStatic = true;
                if (groundLayer >= 0) b.layer = groundLayer;
            }
            // The cube bed + sloped mud banks are only for the legacy flat-ground path. In
            // terrain mode the heightmap carves the natural basin, so skip them.
            if (!UseUnityTerrain)
            {
                MudBank("Lake_BankE", c + new Vector3(mid, midY, 0f), new Vector3(slabLen, 0.6f, w + 2f), new Vector3(0f, 0f, tilt));
                MudBank("Lake_BankW", c + new Vector3(-mid, midY, 0f), new Vector3(slabLen, 0.6f, w + 2f), new Vector3(0f, 0f, -tilt));
                MudBank("Lake_BankN", c + new Vector3(0f, midY, mid), new Vector3(w + 2f, 0.6f, slabLen), new Vector3(-tilt, 0f, 0f));
                MudBank("Lake_BankS", c + new Vector3(0f, midY, -mid), new Vector3(w + 2f, 0.6f, slabLen), new Vector3(tilt, 0f, 0f));
            }

            // Water trigger volume (Water layer) — only the DEEP core, so you swim in the
            // middle and wade the mud shallows. Swim detection is depth-based.
            var vol = new GameObject("Lake_WaterVolume");
            vol.transform.SetParent(container);
            vol.transform.position = c + Vector3.up * ((WaterSurfaceY + LakeBedY) / 2f);
            var vbox = vol.AddComponent<BoxCollider>();
            vbox.isTrigger = true;
            vbox.size = new Vector3(openHalf * 2f, depth, openHalf * 2f);
            if (waterLayer >= 0) vol.layer = waterLayer;

            // Translucent surface spans the WHOLE lake (deep core + shallow mud shore) so it
            // reads as a full body of water. Collider stripped so players/props pass in.
            // LAKE surface: a plane LOCAL to the lake basin (sized to the organic shore). The old
            // world-spanning sheet bled over the whole valley at grazing angles (huge transparent
            // quads sort/occlude badly), reading as marsh/puddles everywhere - so water is now a
            // surface per body: this lake plane + each river reach's own ribbon (below).
            // Size the plane to the WATERLINE, not past it: a transparent quad overhanging DRY
            // raised terrain bleeds over it (the "flooded spawn"). The waterline sits at ~0.9x the
            // shore radius, so keep the plane just inside the shore so it only ever covers carved
            // (below-water) ground.
            float lakeSurfW = (LakeHalf - 7f) * 2f;
            var surf = Cube("Lake_Surface", c + Vector3.up * WaterSurfaceY, new Vector3(lakeSurfW, 0.06f, lakeSurfW), waterMat, container);
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());
            surf.isStatic = true;

            // The full river network: four waterfall sources on the mountain ring feeding
            // seven reaches (two splits) that all drain here. The lake still overflows
            // OUT at its NW corner off the map.
            BuildRiverNetwork(container, groundLayer, waterLayer, mudMat, waterMat);
            if (!UseUnityTerrain) BuildLakeOutflow(container, groundLayer, waterLayer, mudMat, waterMat);

            // Fish schools — visible ambient shoals in the deep lake + the feeder river.
            var lakeFish = new GameObject("Lake_FishSchool");
            lakeFish.transform.SetParent(container);
            lakeFish.AddComponent<World.FishSchool>().Configure(
                c + Vector3.up * (WaterSurfaceY - 0.8f), new Vector3(openHalf - 4f, 1.1f, openHalf - 4f), 16, WaterSurfaceY);

            var riverFish = new GameObject("River_FishSchool");
            riverFish.transform.SetParent(container);
            riverFish.AddComponent<World.FishSchool>().Configure(
                new Vector3(FeederRiverX(224f), WaterSurfaceY - 0.7f, 224f), new Vector3(6f, 0.8f, 18f), 6, WaterSurfaceY);

            // Fishing spot on the dry east bank + campfire a few steps back.
            BuildLakeFishingAndCooking(container, groundLayer, interactableLayer);
        }

        /// <summary>A vertical falling water sheet + foam spray + mist glow, poured at the
        /// mountain-ring edge. <paramref name="basePos"/> is the BASE; the sheet rises
        /// <paramref name="height"/> above it.</summary>
        private static void BuildWaterfall(Transform container, Vector3 basePos, float height, float width, float yaw, Material waterMat, int groundLayer)
        {
            // Bright white spray - NO blue emission (that rendered as glowing cyan card edges).
            var foamMat = Mat("WaterFoam", new Color(0.95f, 0.97f, 0.99f), smoothness: 0.1f,
                emission: new Color(0.14f, 0.15f, 0.16f));
            // Brighter, more saturated falling water so the sheet reads as bright cascading
            // water, not a dark teal card.
            var fallWaterMat = Mat("FallWater", new Color(0.42f, 0.66f, 0.78f), smoothness: 0.55f,
                transparent: true, emission: new Color(0.05f, 0.09f, 0.11f));
            var rockMat = Mat("FallRock", new Color(0.22f, 0.21f, 0.21f), smoothness: 0.04f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 intoMtn = rot * Vector3.forward;    // toward the source (up the mountain)
            Vector3 toValley = -intoMtn;                // downstream, where the plunge pool sits
            Vector3 right = rot * Vector3.right;

            // REAL rock: the fall spills through an outcrop of boulder MESHES (no cube cliff wall).
            // BoulderCluster/RockCliff when generated; MossBoulder is the always-present fallback;
            // a dark rock cube only if every mesh load misses.
            void Rock(string label, Vector3 pos, float size)
            {
                float ry = yaw + (float)rng.NextDouble() * 70f;
                var r = PlaceMeshyProp(container, "BoulderCluster", pos, ry, size, groundLayer, PropCollision.FitBox)
                     ?? PlaceMeshyProp(container, "RockCliff", pos, ry, size, groundLayer, PropCollision.FitBox)
                     ?? PlaceMeshyProp(container, "MossBoulder", pos, ry, size, groundLayer, PropCollision.FitBox);
                if (r == null)
                {
                    var c = Cube(label, pos, Vector3.one * size, rockMat, container);
                    c.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 25f, ry, (float)rng.NextDouble() * 25f);
                    c.isStatic = true;
                    if (groundLayer >= 0) c.layer = groundLayer;
                }
            }

            // A single CONTINUOUS cascade leaning against a rocky chute (the earlier per-tier
            // quads read as floating stacked cards with gaps). The mountains are meshes that may
            // not sit exactly at each source, so the fall carries its OWN boulder chute and never
            // floats. Water pours from a notch high up (set back into the slope) down one unbroken
            // sheet into a foam plunge pool where the river begins.
            const float leanDeg = 20f;                          // lean back from vertical
            float run = height * Mathf.Tan(leanDeg * Mathf.Deg2Rad);   // horizontal setback of the top
            float sheetLen = height / Mathf.Cos(leanDeg * Mathf.Deg2Rad);
            Quaternion sheetRot = Quaternion.AngleAxis(-leanDeg, right) * rot;
            Vector3 sheetCenter = basePos + Vector3.up * (WaterSurfaceY + height * 0.5f) + intoMtn * (run * 0.5f);

            // Rocky chute: boulders framing BOTH sides all the way up + a few midstream steps the
            // water breaks over, forming the cliff the fall pours down.
            int rows = Mathf.Max(5, Mathf.RoundToInt(height / 6f));
            for (int t = 0; t <= rows; t++)
            {
                float f = t / (float)rows;
                Vector3 spine = basePos + Vector3.up * (WaterSurfaceY + f * height) + intoMtn * (f * run);
                float side = width * (0.85f - f * 0.15f);
                float sz = (height / rows) * (1.5f + (float)rng.NextDouble() * 0.9f);
                Rock($"Chute_L{t}", spine - right * side + intoMtn * 1f, sz);
                Rock($"Chute_R{t}", spine + right * side + intoMtn * 1f, sz);
                if (t % 2 == 0)   // a midstream boulder the water splits around
                    Rock($"Chute_M{t}", spine + right * ((float)(rng.NextDouble() - 0.5) * width * 0.5f) + intoMtn * 1.5f, sz * 0.7f);
            }

            // The unbroken falling sheet.
            var sheet = Cube("Fall_Sheet", sheetCenter, new Vector3(width * 0.8f, sheetLen, 0.5f), fallWaterMat, container);
            sheet.transform.rotation = sheetRot;
            Object.DestroyImmediate(sheet.GetComponent<BoxCollider>());
            // A few bright vertical foam streaks over the sheet's face (slightly proud of it).
            for (int s = 0; s < 3; s++)
            {
                float fx = (s - 1) * width * 0.24f + (float)(rng.NextDouble() - 0.5) * 1.5f;
                var streak = Cube($"Fall_Streak{s}", sheetCenter + right * fx + toValley * 0.35f,
                    new Vector3(0.5f + (float)rng.NextDouble() * 0.5f, sheetLen * 0.96f, 0.2f), foamMat, container);
                streak.transform.rotation = sheetRot;
                Object.DestroyImmediate(streak.GetComponent<BoxCollider>());
            }
            // Bright foam cap at the top lip where it tips over, and churn at the base.
            var cap = Cube("Fall_Cap", basePos + Vector3.up * (WaterSurfaceY + height) + intoMtn * run,
                new Vector3(width * 0.85f, 0.8f, 2.2f), foamMat, container);
            cap.transform.rotation = rot;
            Object.DestroyImmediate(cap.GetComponent<BoxCollider>());

            // Plunge pool: churned foam where the fall lands + a couple of wet boulders it
            // breaks around, sitting right at the river's source water surface.
            var pool = Cube("Fall_Pool", basePos + toValley * 2.4f + Vector3.up * (WaterSurfaceY + 0.06f),
                new Vector3(width + 3f, 0.12f, width + 6f), foamMat, container);
            pool.transform.rotation = rot;
            Object.DestroyImmediate(pool.GetComponent<BoxCollider>());
            Rock("Plunge_Rk0", basePos + toValley * 2f + right * (width * 0.55f) + Vector3.up * 0.4f, 3f + (float)rng.NextDouble() * 2f);
            Rock("Plunge_Rk1", basePos + toValley * 2f - right * (width * 0.55f) + Vector3.up * 0.4f, 3f + (float)rng.NextDouble() * 2f);

            // Cool mist glow at the base (steady, no flicker - bloom sells the spray).
            var mistObj = new GameObject("Fall_Mist");
            mistObj.transform.SetParent(container);
            mistObj.transform.position = basePos + toValley * 2f + Vector3.up * 2f;
            var ml = mistObj.AddComponent<Light>();
            ml.type = LightType.Point;
            ml.color = new Color(0.7f, 0.85f, 0.95f);
            ml.range = width * 2.5f;
            ml.intensity = 1.4f;
            ml.shadows = LightShadows.None;
        }

        /// <summary>Short outflow channel from the lake's NW corner, spilling off-map as a
        /// waterfall at the mountain-ring notch.</summary>
        private static void BuildLakeOutflow(Transform container, int groundLayer, int waterLayer, Material bedMat, Material waterMat)
        {
            Vector3 c = LakeCenter;
            Vector3 dir = new Vector3(-0.707f, 0f, 0.707f);   // NW, toward the ring notch (~135°)
            Vector3 start = c + dir * (LakeHalf - 2f);
            Vector3 endEdge = c + dir * (LakeHalf + 22f);
            Vector3 mid = (start + endEdge) / 2f;
            float len = Vector3.Distance(start, endEdge) + 2f;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            var bed = Cube("Outflow_Bed", mid + Vector3.down * 1.2f, new Vector3(10f, 0.6f, len), bedMat, container);
            bed.transform.rotation = rot; bed.isStatic = true;
            if (groundLayer >= 0) bed.layer = groundLayer;

            var surf = Cube("Outflow_Surface", mid + Vector3.up * (WaterSurfaceY - 0.12f),
                new Vector3(9f, 0.06f, len), waterMat, container);
            surf.transform.rotation = rot;
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

            // The drop: a tall sheet falling from lake level down into the unseen lowlands.
            Vector3 fallBase = endEdge + dir * 3f + Vector3.down * 18f;
            BuildWaterfall(container, fallBase, 18f, 10f, yaw, waterMat, groundLayer);
        }

        /// <summary>Build every reach of the river network (v8): shared segment builder
        /// walking each polyline, with shallow FORD crossings where a reach meets the
        /// main road, a waterfall at each mountain source, and fish in the channels.</summary>
        private static void BuildRiverNetwork(Transform container, int groundLayer, int waterLayer, Material bedMat, Material waterMat)
        {
            for (int r = 0; r < RiverPaths.Length; r++)
                BuildRiverReach(container, r, groundLayer, waterLayer, bedMat, waterMat);

            foreach (int r in RiverSourceFalls)
            {
                Vector3 source = RiverPaths[r][0];
                Vector3 dirIn = (RiverPaths[r][1] - source).normalized;
                float yaw = Mathf.Atan2(dirIn.x, dirIn.z) * Mathf.Rad2Deg + 180f;
                // Tall sheet pouring down the rocky mountainside from a notch high up, into the
                // carved gorge (river bed ~-3). Base at the gorge, rising up the cliff.
                BuildWaterfall(container, source + Vector3.up * (-3f), 38f, RiverHalfWidths[r] * 2f + 2f, yaw, waterMat, groundLayer);
            }

            // Ambient fish in the two biggest reaches.
            foreach (var (idx, at) in new[] { (1, 2), (3, 3) })
            {
                var mid = Vector3.Lerp(RiverPaths[idx][at], RiverPaths[idx][at + 1], 0.5f);
                var fishObj = new GameObject($"River{idx}_FishSchool");
                fishObj.transform.SetParent(container);
                fishObj.transform.position = mid;
                fishObj.AddComponent<World.FishSchool>().Configure(
                    mid + Vector3.up * (WaterSurfaceY - 0.7f), new Vector3(RiverHalfWidths[idx] - 1f, 0.8f, 10f), 5, WaterSurfaceY);
            }
        }

        private static void BuildRiverReach(Transform container, int riverIndex, int groundLayer, int waterLayer, Material bedMat, Material waterMat)
        {
            var shoreMat = Mat("RiverShoreN", new Color(0.3f, 0.26f, 0.19f), smoothness: 0.05f);
            var path = RiverPaths[riverIndex];
            float halfW = RiverHalfWidths[riverIndex];
            const float seg = 7f;

            for (int leg = 0; leg < path.Length - 1; leg++)
            {
                Vector3 a = path[leg], b = path[leg + 1];
                float legLen = Vector3.Distance(a, b);
                int steps = Mathf.Max(1, Mathf.CeilToInt(legLen / seg));
                for (int s = 0; s < steps; s++)
                {
                    Vector3 pA = Vector3.Lerp(a, b, s / (float)steps);
                    Vector3 pB = Vector3.Lerp(a, b, (s + 1) / (float)steps);
                    BuildRiverSegment(container, riverIndex, pA, pB, halfW, shoreMat, groundLayer, waterLayer, bedMat, waterMat);
                }
                // ONE continuous water surface for the whole straight leg (no per-segment banding).
                if (UseUnityTerrain)
                {
                    Vector3 mid = (a + b) / 2f;
                    float yaw = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
                    var surf = Cube($"River{riverIndex}_Surf{leg}", new Vector3(mid.x, WaterSurfaceY, mid.z),
                        new Vector3(halfW * 2f + 1.5f, 0.06f, legLen + 3f), waterMat, container);
                    surf.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    surf.isStatic = true;
                    Object.DestroyImmediate(surf.GetComponent<BoxCollider>());
                }
            }
        }

        private static void BuildRiverSegment(Transform container, int riverIndex, Vector3 pA, Vector3 pB,
            float halfW, Material shoreMat, int groundLayer, int waterLayer, Material bedMat, Material waterMat)
        {
            Vector3 midP = (pA + pB) / 2f;
            // Fords stay wading depth (no swim, walk across); everywhere else is deep.
            bool ford = IsRiverFord(midP);
            float bedY = ford ? -0.9f : -2.4f;
            float colTop = WaterSurfaceY, colH = colTop - bedY, colCy = (colTop + bedY) / 2f;

            {
                float zA = pA.z, zB = pB.z;
                float xA = pA.x, xB = pB.x;
                Vector3 mid = new Vector3((xA + xB) / 2f, 0f, (zA + zB) / 2f);
                float dx = xB - xA, dz = zB - zA;
                float len = Mathf.Sqrt(dx * dx + dz * dz) + 1.6f;
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0, yaw, 0);
                float yawRad = yaw * Mathf.Deg2Rad;
                Vector3 rightDir = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

                // Legacy flat-ground only: cube bed. In terrain mode the carved channel is the bed.
                if (!UseUnityTerrain)
                {
                    var bed = Cube($"River{riverIndex}_Bed", mid + Vector3.up * (bedY - 0.35f), new Vector3(halfW * 2f + 1f, 0.7f, len), bedMat, container);
                    bed.transform.rotation = rot; bed.isStatic = true;
                    if (groundLayer >= 0) bed.layer = groundLayer;
                }

                var vol = new GameObject($"River{riverIndex}_WaterVolume");
                vol.transform.SetParent(container);
                vol.transform.position = mid + Vector3.up * colCy;
                vol.transform.rotation = rot;
                var vbox = vol.AddComponent<BoxCollider>();
                vbox.isTrigger = true;
                vbox.size = new Vector3(halfW * 2f, colH, len);
                if (waterLayer >= 0) vol.layer = waterLayer;

                // (River surface is built ONCE per straight leg in BuildRiverReach - a single quad,
                // so overlapping per-segment quads stop reading as a tiled/banded ribbon.)

                // Wade-in shore step cubes: legacy flat-ground only (the terrain carve gives
                // natural gradient banks now, so these fixed-Y brown blocks are retired).
                if (!ford && !UseUnityTerrain)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float top = -1.0f, h = top - (bedY - 0.4f), off = halfW + 1.25f;
                        var stp = Cube($"River{riverIndex}_Shore", mid + rightDir * (off * side) + Vector3.up * (top - h / 2f),
                            new Vector3(2.5f, h, len), shoreMat, container);
                        stp.transform.rotation = rot; stp.isStatic = true;
                        if (groundLayer >= 0) stp.layer = groundLayer;
                    }
                }
                else if (!UseUnityTerrain)
                {
                    // Stepping stones sell the crossing.
                    var stoneMat = Mat("FordStone", new Color(0.45f, 0.43f, 0.4f), smoothness: 0.1f);
                    for (int i = 0; i < 3; i++)
                    {
                        var stone = Cube($"River{riverIndex}_FordStone{i}",
                            mid + rightDir * ((i - 1) * 2.2f) + Vector3.up * (WaterSurfaceY + 0.12f),
                            new Vector3(1.5f, 0.5f, 1.7f), stoneMat, container);
                        stone.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 40f, 0f);
                        stone.isStatic = true;
                        if (groundLayer >= 0) stone.layer = groundLayer;
                    }
                }
            }
        }

        private static void BuildLakeFishingAndCooking(Transform container, int groundLayer, int interactableLayer)
        {
            // ---- Fishing spot on the dry east bank ----
            var spotObj = new GameObject("FishingSpot");
            spotObj.transform.SetParent(container);
            spotObj.transform.position = new Vector3(LakeCenter.x + LakeHalf + 3f, 0f, LakeCenter.z + 4f);
            if (interactableLayer >= 0) spotObj.layer = interactableLayer;

            var spot = spotObj.AddComponent<World.FishingSpot>();
            var spotSO = new SerializedObject(spot);
            spotSO.FindProperty("prompt").stringValue = "Fish";
            spotSO.FindProperty("radius").floatValue = 4f;
            spotSO.FindProperty("bobberAnchor").vector3Value =
                new Vector3(LakeCenter.x + LakeHalf - 8f, WaterSurfaceY + 0.05f, LakeCenter.z + 4f);
            spotSO.ApplyModifiedProperties();

            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "SignPost";
            post.transform.SetParent(spotObj.transform);
            post.transform.localPosition = new Vector3(0.6f, 0.55f, -0.8f);
            post.transform.localScale = new Vector3(0.09f, 0.55f, 0.09f);
            post.GetComponent<MeshRenderer>().sharedMaterial =
                SceneSetup.GetOrCreateMaterialAsset(new Color(0.4f, 0.28f, 0.16f), "FishSignPostMaterial");
            if (groundLayer >= 0) post.layer = groundLayer;

            // ---- Campfire a few steps inland ----
            var fireObj = new GameObject("Campfire");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = new Vector3(LakeCenter.x + LakeHalf + 9f, 0f, LakeCenter.z + 10f);
            if (interactableLayer >= 0) fireObj.layer = interactableLayer;

            var fire = fireObj.AddComponent<World.Campfire>();
            var fireSO = new SerializedObject(fire);
            fireSO.FindProperty("prompt").stringValue = "Cook";
            fireSO.FindProperty("radius").floatValue = 3.5f;
            fireSO.ApplyModifiedProperties();

            if (PlaceMeshyProp(fireObj.transform, "Campfire", fireObj.transform.position, rng.Next(0, 360), 0.9f, groundLayer) == null)
            {
                var stoneMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.35f, 0.33f, 0.31f), "CampfireStoneMaterial");
                for (int i = 0; i < 6; i++)
                {
                    float a = i * Mathf.PI * 2f / 6f;
                    var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    stone.name = $"Stone{i}";
                    stone.transform.SetParent(fireObj.transform);
                    stone.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.55f, 0.1f, Mathf.Sin(a) * 0.55f);
                    stone.transform.localScale = Vector3.one * 0.28f;
                    Object.DestroyImmediate(stone.GetComponent<SphereCollider>());
                    stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
                }
            }
            var lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(fireObj.transform);
            lightObj.transform.localPosition = Vector3.up * 0.8f;
            var fl = lightObj.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = new Color(1f, 0.55f, 0.25f);
            fl.intensity = 2.4f;
            fl.range = 7f;
            lightObj.AddComponent<World.TorchFlicker>();
        }

        // ==================== DEEP DARK WOODS (N) ====================

        private static void BuildDeepDarkWoods(Transform root, int groundLayer)
        {
            var container = Child(root, "DeepDarkWoods");
            Vector3 c = new Vector3(0f, 0f, 195f);

            var trunkMat = Mat("DarkTrunk", new Color(0.18f, 0.13f, 0.09f), smoothness: 0.04f);
            var leafMat = Mat("DarkLeaf", new Color(0.07f, 0.16f, 0.09f), smoothness: 0.05f);
            string[] kit = { "PineTree", "DeadTree", "BirchTree" };

            int planted = 0;
            for (int attempt = 0; attempt < 600 && planted < 150; attempt++)
            {
                float x = c.x + ((float)rng.NextDouble() * 2f - 1f) * 62f;
                float z = c.z + ((float)rng.NextDouble() * 2f - 1f) * 55f;
                if (Mathf.Sqrt(x * x + z * z) > 246f) continue;
                if (InLakeWater(x, z) || InFeederRiver(x, z)) continue;
                if (Mathf.Abs(x - LakeCenter.x) < LakeHalf + 8f && Mathf.Abs(z - LakeCenter.z) < LakeHalf + 8f) continue;
                planted++;
                float h = 7f + (float)rng.NextDouble() * 6f;
                if (PlaceMeshyProp(container, kit[planted % kit.Length], new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk) != null) continue;
                PlaceCubePine(container, 5000 + planted, x, z, h, trunkMat, leafMat, groundLayer);
            }

            // Dim, cold light + a couple of glowing-mushroom waypoints in the gloom.
            var shroomMat = Mat("DarkShroom", new Color(0.4f, 0.75f, 0.85f), smoothness: 0.3f,
                emission: new Color(0.2f, 0.55f, 0.7f) * 2f);
            for (int i = 0; i < 6; i++)
            {
                float x = c.x + ((float)rng.NextDouble() * 2f - 1f) * 45f;
                float z = c.z + ((float)rng.NextDouble() * 2f - 1f) * 40f;
                for (int m = 0; m < 3; m++)
                {
                    var cap = Sphere($"DarkShroom{i}_{m}", new Vector3(x + m * 0.5f - 0.5f, 0.22f + m * 0.06f, z),
                        Vector3.one * (0.35f + m * 0.12f), shroomMat, container);
                    Object.DestroyImmediate(cap.GetComponent<SphereCollider>());
                }
            }

            // Hidden treasure deep in the gloom (guarded by the new acolyte pair at
            // (-20,210) in monsters.json) — exploration should pay.
            CreateLootChest(container, new Vector3(-24f, 0f, 218f), 140f,
                new (string id, float chance)[] { ("raider_saber", 1f), ("bandit_cape", 0.8f), ("adept_focus", 0.5f) }, groundLayer);
        }

        /// <summary>Hidden world-secret chests: exploration rewards off the beaten paths.</summary>
        private static void BuildWorldSecrets(Transform root, int groundLayer)
        {
            var container = Child(root, "WorldSecrets");

            // Behind the lake's NW outflow, tucked against the mountain ring.
            CreateLootChest(container, new Vector3(-196f, 0f, 198f), 45f,
                new (string id, float chance)[] { ("iron_chestplate", 1f), ("cooked_carp", 1f), ("apprentice_focus", 0.6f) }, groundLayer);

            // A soot-buried cache just off the Ember Approach road (east shoulder).
            CreateLootChest(container, new Vector3(44f, 0f, -195f), -60f,
                new (string id, float chance)[] { ("iron_legs", 1f), ("emberfang", 0.35f) }, groundLayer);
        }

        // ==================== FOREST CAMPS (goblin W, bandit E dressing) ====================

        private static void BuildEmberForestCamps(Transform root, int groundLayer)
        {
            var container = Child(root, "ForestCamps");
            // Goblin Camp (W) — reuse the bandit-camp dressing (fire + tents) around (-195,0,0).
            BuildBanditCampDressing(container, new Vector3(-195f, 0f, 0f));
            BuildBanditCampDressing(container, new Vector3(-205f, 0f, -20f));
            // Bandit Camp (E) — around (195,0,0).
            BuildBanditCampDressing(container, new Vector3(195f, 0f, 0f));
            BuildBanditCampDressing(container, new Vector3(205f, 0f, -20f));
            // A scatter of orc dressing where the mid-forest orc camps sit.
            BuildOrcCampDressing(container, new Vector3(155f, 0f, 55f));
            BuildOrcCampDressing(container, new Vector3(-160f, 0f, 45f));
        }

        // ==================== ORC WARCAMP (SW, rescue quest) ====================

        private static void BuildEmberOrcWarcamp(Transform root, int groundLayer)
        {
            var container = Child(root, "OrcWarcamp");
            Vector3 center = new Vector3(-110f, 0f, -120f);

            BuildOrcCampDressing(container, center + new Vector3(5f, 0f, 4f));
            BuildOrcCampDressing(container, center + new Vector3(-7f, 0f, -6f));

            // War-fire.
            var fireObj = new GameObject("WarFire");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = center + Vector3.up * 0.9f;
            var fl = fireObj.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = new Color(1f, 0.5f, 0.18f);
            fl.range = 16f;
            fl.shadows = LightShadows.None;
            fireObj.AddComponent<World.TorchFlicker>().Configure(3.0f, 0.9f, 7f);

            // Spike ring.
            var spikeMat = Mat("WarSpikeE", new Color(0.28f, 0.2f, 0.13f), smoothness: 0.05f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 sp = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 12f;
                var spike = Cylinder($"WarSpike{i}", sp + Vector3.up * 1.1f, new Vector3(0.22f, 1.1f, 0.22f), spikeMat, container);
                spike.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 22f - 11f, (float)rng.NextDouble() * 360f, 0f);
                spike.isStatic = true;
                if (groundLayer >= 0) spike.layer = groundLayer;
            }

            // War banners: tall poles with ragged red cloth so the camp reads as
            // GRUKMAR'S from a distance (it was an anonymous dark patch in QA).
            var poleMat = Mat("WarBannerPole", new Color(0.2f, 0.15f, 0.1f), smoothness: 0.05f);
            var clothMat = Mat("WarBannerCloth", new Color(0.55f, 0.1f, 0.08f), smoothness: 0.05f);
            foreach (var (off, yaw) in new[]
            {
                (new Vector3(10f, 0f, 10f), 20f), (new Vector3(-11f, 0f, 8f), -35f),
                (new Vector3(0f, 0f, -13f), 180f),
            })
            {
                var pole = Cylinder("WarBannerPole", center + off + Vector3.up * 2.6f,
                    new Vector3(0.16f, 2.6f, 0.16f), poleMat, container);
                pole.isStatic = true;
                if (groundLayer >= 0) pole.layer = groundLayer;
                var cloth = Cube("WarBannerCloth", center + off + new Vector3(0f, 4.1f, 0f),
                    new Vector3(1.3f, 1.9f, 0.12f), clothMat, container);
                cloth.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                cloth.isStatic = true;
                Object.DestroyImmediate(cloth.GetComponent<BoxCollider>());
            }

            // Second fire by the cage so the rescue tableau is lit.
            var cageFire = new GameObject("CageFire");
            cageFire.transform.SetParent(container);
            cageFire.transform.position = center + new Vector3(-6f, 0.9f, -6f);
            var cfl = cageFire.AddComponent<Light>();
            cfl.type = LightType.Point;
            cfl.color = new Color(1f, 0.55f, 0.2f);
            cfl.range = 13f;
            cfl.shadows = LightShadows.None;
            cageFire.AddComponent<World.TorchFlicker>().Configure(2.4f, 0.7f, 6f);

            // The caged daughter — freed (RescueCaptive) when grukmar dies.
            BuildCagedDaughter(container, center + new Vector3(-3f, 0f, -3f), groundLayer);
        }

        // ==================== EMBER APPROACH (south corridor) ====================

        private static void BuildEmberApproach(Transform root, int groundLayer)
        {
            var container = Child(root, "EmberApproach");

            // Charred, progressively barer trees. Fewer + darker toward the volcano.
            var trunkMat = Mat("CharTrunk", new Color(0.14f, 0.10f, 0.08f), smoothness: 0.03f);
            var deadLeaf = Mat("CharLeaf", new Color(0.16f, 0.12f, 0.09f), smoothness: 0.03f);
            int planted = 0;
            for (int attempt = 0; attempt < 900 && planted < 140; attempt++)
            {
                float z = Mathf.Lerp(ApproachNorthZ - 4f, ApproachSouthZ + 6f, (float)rng.NextDouble());
                float x = ((float)rng.NextDouble() * 2f - 1f) * (ApproachHalfW - 6f);
                if (Mathf.Abs(x) < 6f) continue;   // keep the central road walkable
                float t = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, z);
                if ((float)rng.NextDouble() < t * 0.6f) continue; // thin out nearer the volcano
                planted++;
                float h = 5f + (float)rng.NextDouble() * 4f;
                if (PlaceMeshyProp(container, "DeadTree", new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk) != null)
                    continue;
                var trunk = Cube($"CharTree{planted}", new Vector3(x, h * 0.4f, z), new Vector3(0.55f, h * 0.8f, 0.55f), trunkMat, container);
                trunk.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 8f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 8f);
                trunk.isStatic = true;
                if (groundLayer >= 0) trunk.layer = groundLayer;
                if (t < 0.5f)
                {
                    var leaf = Cube($"CharTree{planted}_Leaf", new Vector3(x, h * 0.85f, z), new Vector3(h * 0.4f, h * 0.35f, h * 0.4f), deadLeaf, container);
                    leaf.isStatic = true;
                    Object.DestroyImmediate(leaf.GetComponent<BoxCollider>());  // canopy walk-through
                }
            }

            // ---- Scorched ground debris: dark ash rock + charred rubble scattered down the
            // corridor (denser toward the volcano) so the floor reads as broken volcanic earth
            // rather than a bare pale sheet, plus glowing ground cracks near the caldera. ----
            var ashRock = Mat("EmberAshRock", new Color(0.13f, 0.105f, 0.10f), smoothness: 0.03f);
            var ashRockDk = Mat("EmberAshRockDk", new Color(0.09f, 0.07f, 0.07f), smoothness: 0.03f);
            var emberCrack = Mat("EmberGroundCrack", new Color(1f, 0.45f, 0.12f), smoothness: 0.5f,
                emission: new Color(1f, 0.35f, 0.08f) * 3.5f);
            for (int i = 0; i < 220; i++)
            {
                float z = Mathf.Lerp(ApproachNorthZ - 6f, ApproachSouthZ + 4f, (float)rng.NextDouble());
                float x = ((float)rng.NextDouble() * 2f - 1f) * (ApproachHalfW - 3f);
                if (Mathf.Abs(x) < 6f) continue;   // keep the central road clear
                float t = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, z);   // 0 gate -> 1 volcano
                if ((float)rng.NextDouble() > 0.35f + t * 0.55f) continue;        // denser toward the volcano
                float s = 0.5f + (float)rng.NextDouble() * (1.4f + t * 1.6f);
                var r = Cube($"AshRock{i}", new Vector3(x, s * 0.28f, z),
                    new Vector3(s, s * (0.5f + (float)rng.NextDouble() * 0.5f), s * (0.7f + (float)rng.NextDouble() * 0.6f)),
                    rng.NextDouble() < 0.5 ? ashRock : ashRockDk, container);
                r.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 18f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 18f);
                r.isStatic = true;
                if (groundLayer >= 0) r.layer = groundLayer;
                // Glowing fissures crack open on the hot ground closer to the volcano.
                if (t > 0.45f && rng.NextDouble() < 0.28)
                {
                    var crack = Cube($"AshCrack{i}", new Vector3(x + (float)(rng.NextDouble() - 0.5) * 4f, 0.06f, z + (float)(rng.NextDouble() - 0.5) * 4f),
                        new Vector3(0.3f + (float)rng.NextDouble() * 0.5f, 0.05f, 2f + (float)rng.NextDouble() * 3f), emberCrack, container);
                    crack.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0);
                    Object.DestroyImmediate(crack.GetComponent<BoxCollider>());
                }
            }

            // ---- EmberZones: positional darkening gradient toward the volcano ----
            void Zone(float z, float intensity)
            {
                var go = new GameObject($"EmberZone_{Mathf.RoundToInt(intensity * 100f)}");
                go.transform.SetParent(container);
                go.transform.position = new Vector3(0f, 6f, z);
                var box = go.AddComponent<BoxCollider>();  // EmberZone forces isTrigger in Awake
                box.size = new Vector3(ApproachHalfW * 2f + 20f, 24f, 60f);
                go.AddComponent<World.EmberZone>().Configure(intensity);
            }
            Zone(-110f, 0.25f);
            Zone(-170f, 0.5f);
            Zone(-230f, 0.75f);
            Zone(-285f, 1.0f);

            // ---- First lava pools: from z ~ -200 onward, more toward the volcano ----
            BuildLavaPool(container, new Vector3(-24f, 0f, -205f), new Vector2(14f, 10f), groundLayer);
            BuildLavaPool(container, new Vector3(22f, 0f, -240f), new Vector2(16f, 12f), groundLayer);
            BuildLavaPool(container, new Vector3(-14f, 0f, -280f), new Vector2(20f, 16f), groundLayer);
            BuildLavaPool(container, new Vector3(20f, 0f, -300f), new Vector2(18f, 14f), groundLayer);

            // Ember braziers lighting the road at intervals.
            for (float z = -80f; z >= -300f; z -= 55f)
            {
                CreateTorch(container, new Vector3(-9f, 0f, z), 1f);
                CreateTorch(container, new Vector3(9f, 0f, z), 1f);
            }
        }

        /// <summary>
        /// A molten lava pool: a dark crust basin + raised rocky rim, a bright emissive
        /// molten surface with glowing cracks, and a trigger volume carrying a server-side
        /// LavaHazard DoT placed EXACTLY over the visible molten surface (and tall enough
        /// to catch a walking player). Soot ground carries NO hazard — only lava burns.
        /// </summary>
        private static void BuildLavaPool(Transform parent, Vector3 center, Vector2 sizeXZ, int groundLayer)
        {
            var container = Child(parent, "LavaPool");
            var moltenMat = Mat("LavaMolten", new Color(1f, 0.42f, 0.08f), smoothness: 0.55f,
                emission: new Color(1f, 0.38f, 0.06f) * 4.5f);
            var crackMat = Mat("LavaCrack", new Color(1f, 0.72f, 0.22f), smoothness: 0.6f,
                emission: new Color(1f, 0.66f, 0.18f) * 6.5f);
            var crustMat = Mat("LavaCrust", new Color(0.13f, 0.08f, 0.07f), smoothness: 0.04f,
                emission: new Color(0.30f, 0.06f, 0.015f));

            float hx = sizeXZ.x, hz = sizeXZ.y;

            // Dark crust basin: a sunken plate + a raised ring of rocky rim blocks so the
            // pool reads as molten rock in a basin, not a flat yellow decal.
            var plate = Cube("Lava_CrustPlate", center + Vector3.down * 0.2f,
                new Vector3(hx + 3f, 0.4f, hz + 3f), crustMat, container);
            plate.isStatic = true;
            if (groundLayer >= 0) plate.layer = groundLayer;

            const int rimN = 14;
            for (int i = 0; i < rimN; i++)
            {
                float a = i / (float)rimN * Mathf.PI * 2f;
                float rx = (hx / 2f + 0.9f) * Mathf.Cos(a);
                float rz = (hz / 2f + 0.9f) * Mathf.Sin(a);
                var rock = Cube($"Lava_Rim{i}",
                    center + new Vector3(rx, 0.25f + (float)rng.NextDouble() * 0.2f, rz),
                    new Vector3(1.6f + (float)rng.NextDouble(), 0.7f, 1.6f + (float)rng.NextDouble()), crustMat, container);
                rock.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 60f, 0);
                rock.isStatic = true;
                if (groundLayer >= 0) rock.layer = groundLayer;
            }

            // Glowing molten surface (collider stripped so the player can wade in and burn).
            var surf = Cube("Lava_Surface", center + Vector3.up * 0.05f, new Vector3(hx, 0.12f, hz), moltenMat, container);
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

            // Brighter molten cracks for an uneven, glowing look.
            for (int i = 0; i < 4; i++)
            {
                float cx = center.x + (float)(rng.NextDouble() - 0.5) * hx * 0.7f;
                float cz = center.z + (float)(rng.NextDouble() - 0.5) * hz * 0.7f;
                var crack = Cube($"Lava_Crack{i}", new Vector3(cx, 0.08f, cz),
                    new Vector3(hx * (0.14f + (float)rng.NextDouble() * 0.2f), 0.06f, 0.5f + (float)rng.NextDouble()),
                    crackMat, container);
                crack.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0);
                Object.DestroyImmediate(crack.GetComponent<BoxCollider>());
            }

            // Hazard trigger volume EXACTLY over the visible molten surface, rising 3.2m so a
            // standing/walking player is caught. Default layer collides with Player + Enemy.
            var hazardObj = new GameObject("LavaHazard");
            hazardObj.transform.SetParent(container);
            hazardObj.transform.position = center + Vector3.up * 1.4f;
            var box = hazardObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(hx, 3.2f, hz);
            hazardObj.AddComponent<World.LavaHazard>().Configure(14f, 0.5f);

            // Steady bright ember glow (no flicker component — avoids the flickering
            // orange-border artifact Jon saw; bloom on the emissive sells the molten heat).
            var lightObj = new GameObject("Lava_Light");
            lightObj.transform.SetParent(container);
            lightObj.transform.position = center + Vector3.up * 2f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.13f);
            light.range = Mathf.Max(hx, hz) * 1.5f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
        }

        // ==================== VOLCANO (dungeon) ====================

        /// <summary>Real mountain-mesh border enclosing the valley (ref: rocky ranges), replacing
        /// the old rising-terrain "landfill hills". Ridges/peaks ring the world edge facing inward,
        /// with forested foothills transitioning down to the valley. The south ember corridor is
        /// left open for the volcano. No-op until the mountain meshes are generated.</summary>
        private static void BuildMountainRange(Transform root, int groundLayer)
        {
            if (!HasMesh("MountainRidge") && !HasMesh("MountainPeak")) return;
            var container = Child(root, "MountainRange");
            const float xMin = -450f, xMax = 450f, zMin = -470f, zMax = 570f;
            var center = new Vector2(0f, 50f);
            string[] peaks = { "MountainRidge", "MountainPeak", "MountainRidge" };
            int pk = 0;

            // HEIGHT-BLEND material: every mountain reads as green lower slopes -> dirt/rock ->
            // snow-capped peak (Jon: not "separate mountains with just one colour"). Triplanar rock
            // texture for surface detail regardless of the preview-mesh UVs.
            var rockTex = GetGeneratedTexture("MountainRock_Tex", () => GroundTextureGenerator.Rock(256));
            Material mtnMat = null;
            var hbShader = Shader.Find("AldenFall/MountainHeightBlend");
            if (hbShader != null)
            {
                string mp = $"{MaterialDir}/MountainHeightBlend.mat";
                mtnMat = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (mtnMat == null) { mtnMat = new Material(hbShader); AssetDatabase.CreateAsset(mtnMat, mp); }
                else if (mtnMat.shader != hbShader) mtnMat.shader = hbShader;
                mtnMat.SetTexture("_RockTex", rockTex);
                EditorUtility.SetDirty(mtnMat);
            }
            void SkinMtn(GameObject go, int idx)
            {
                if (go == null || mtnMat == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mtnMat;
            }
            // Foothills are small STEEP cones: the height-blend shader reads their slope as
            // bare dirt/rock and paints them a brown "landfill" lump. They're meant to be
            // FORESTED transition hills, so skin them a dark forest green (the sculpted pine
            // bumps catch light and read as tree cover) with the rock texture for faint detail.
            var foothillMat = Mat("FoothillForest", new Color(0.22f, 0.34f, 0.17f), smoothness: 0.03f);
            void SkinFoothill(GameObject go)
            {
                if (go == null) return;
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = foothillMat;
            }

            void Place(float x, float z, bool corridor)
            {
                if (corridor) return;
                string peak = peaks[pk % peaks.Length];
                if (!HasMesh(peak)) peak = HasMesh("MountainRidge") ? "MountainRidge" : "MountainPeak";
                float yaw = Mathf.Atan2(center.x - x, center.y - z) * Mathf.Rad2Deg;   // face the valley
                float scale = 140f + (float)rng.NextDouble() * 80f;
                var mtn = PlaceMeshyProp(container, peak, new Vector3(x, -6f, z),
                    yaw + (float)(rng.NextDouble() - 0.5) * 26f, scale, groundLayer, PropCollision.FitBox);
                SkinMtn(mtn, pk + (scale > 190f ? 3 : 0));   // biggest peaks get the snow-touched variant
                pk++;
                if (HasMesh("ForestFoothill"))
                {
                    Vector2 toC = (center - new Vector2(x, z)).normalized;
                    Vector3 footPos = new Vector3(x + toC.x * 42f, 0f, z + toC.y * 42f);
                    // Don't bury a waterfall: foothills plant toward the valley and can land right
                    // on a river source, occluding the whole cascade. Skip when too close.
                    bool nearFall = false;
                    foreach (int rsi in RiverSourceFalls)
                    {
                        Vector3 sp = RiverPaths[rsi][0];
                        if ((footPos.x - sp.x) * (footPos.x - sp.x) + (footPos.z - sp.z) * (footPos.z - sp.z) < 60f * 60f)
                        { nearFall = true; break; }
                    }
                    if (nearFall) return;
                    var foot = PlaceMeshyProp(container, "ForestFoothill", footPos,
                        yaw + (float)(rng.NextDouble() - 0.5) * 40f, 34f + (float)rng.NextDouble() * 26f,
                        groundLayer, PropCollision.None);
                    SkinFoothill(foot);
                }
            }

            const float step = 80f;
            for (float x = xMin + 24f; x <= xMax - 24f; x += step)
            {
                Place(x, zMax - 22f, false);                                   // north edge
                Place(x, zMin + 22f, Mathf.Abs(x) < ApproachHalfW + 55f);      // south edge (open the volcano corridor)
            }
            for (float z = zMin + 24f + step * 0.5f; z <= zMax - 24f; z += step)
            {
                Place(xMax - 22f, z, false);                                   // east edge
                Place(xMin + 22f, z, false);                                   // west edge
            }
        }

        private static void BuildEmberfallVolcano(Transform root, int groundLayer)
        {
            var container = Child(root, "EmberfallVolcano");
            var rockMat = Mat("VolcanoRock", new Color(0.20f, 0.16f, 0.15f), smoothness: 0.04f);
            var rockDark = Mat("VolcanoRockDark", new Color(0.12f, 0.09f, 0.09f), smoothness: 0.04f);
            var floorMat = Mat("VolcanoFloor", new Color(0.15f, 0.12f, 0.12f), smoothness: 0.1f);
            var wallMat = Mat("VolcanoWall", new Color(0.10f, 0.08f, 0.09f), smoothness: 0.06f);
            var emberMat = Mat("VolcanoEmber", new Color(1f, 0.4f, 0.12f), smoothness: 0.4f,
                emission: new Color(1f, 0.35f, 0.1f) * 3f);

            // ---- Exterior caldera cone (dramatic massing behind/around the entrance) ----
            Vector3 cone = new Vector3(0f, 0f, -345f);
            void Rock(string n, Vector3 pos, Vector3 size, float yaw, Material m)
            {
                var r = Cube(n, pos, size, m, container);
                r.transform.rotation = Quaternion.Euler(0, yaw, 0);
                r.isStatic = true;
                if (groundLayer >= 0) r.layer = groundLayer;
            }
            // Real volcano MESH (dormant + erupting phases) when generated; otherwise the cube
            // cone below. Both phase meshes are placed under VolcanoVisual - dormant active,
            // erupting hidden - so the eruption world-state can swap them (montage toggles too).
            bool meshVolcano = HasMesh("VolcanoDormant");
            // The solid mesh cone sits SOUTH of the cube-cone origin so its north base lands at
            // the dungeon entrance (z-315) - otherwise the cone buries the mouth. Its bulk covers
            // the interior rooms (the dungeon is literally inside the volcano).
            Vector3 meshCenter = new Vector3(0f, 0f, -430f);
            Vector3 craterCenter = meshVolcano ? meshCenter : cone;
            float craterY = meshVolcano ? 78f : 78f;
            if (meshVolcano)
            {
                var vis = Child(container, "VolcanoVisual");
                var volcanoTex = GetGeneratedTexture("VolcanoRock_Tex", () => GroundTextureGenerator.Soot(256));
                Material VolMat(string n, Color c, Color? em)
                {
                    var m = Mat(n, c, smoothness: 0.06f, emission: em);
                    if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", volcanoTex); m.SetTextureScale("_BaseMap", new Vector2(9f, 9f)); }
                    if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", volcanoTex); m.SetTextureScale("_MainTex", new Vector2(9f, 9f)); }
                    return m;
                }
                // Dark basalt: keep the soot texture DARK and near-neutral (a warm/bright base
                // read as a brown dirt dome, "not a volcano"). Slight cool bias + low magnitude
                // = charcoal volcanic rock the sculpted gullies read against under the sun.
                var dormantMat = VolMat("VolcanoMeshRock", new Color(0.5f, 0.5f, 0.55f), null);
                var eruptMat = VolMat("VolcanoMeshLava", new Color(0.85f, 0.55f, 0.4f), new Color(0.7f, 0.19f, 0.04f)); // charred + hot glow
                void Skin(GameObject go, Material m) { if (go != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m; }
                var dormant = PlaceMeshyProp(vis, "VolcanoDormant", meshCenter, 180f, 230f, groundLayer, PropCollision.FitBox);
                if (dormant != null) { dormant.name = "VolcanoDormant"; Skin(dormant, dormantMat); }
                if (HasMesh("VolcanoErupting"))
                {
                    var erupt = PlaceMeshyProp(vis, "VolcanoErupting", meshCenter, 180f, 230f, groundLayer, PropCollision.None);
                    if (erupt != null) { erupt.name = "VolcanoErupting"; Skin(erupt, eruptMat); erupt.SetActive(false); }
                }
            }

            // ---- Cone: stacked tapering rings of INWARD-TILTED rock slabs, from a wide base
            // up to the crater lip (fallback when no volcano mesh exists yet). ----
            const int rings = 6;
            if (!meshVolcano)
            {
            for (int t = 0; t < rings; t++)
            {
                float f = t / (float)(rings - 1);                       // 0 base -> 1 crater lip
                float rad = Mathf.Lerp(62f, 16f, f);
                float yBase = Mathf.Lerp(0f, 68f, f * f * 0.8f + f * 0.2f);
                float slabH = Mathf.Lerp(30f, 15f, f);
                float tilt = Mathf.Lerp(20f, 42f, f);                   // steeper near the top
                int n = Mathf.RoundToInt(Mathf.Lerp(16f, 7f, f));
                for (int i = 0; i < n; i++)
                {
                    float a = (i + t * 0.5f) / n * Mathf.PI * 2f;       // stagger rings so seams don't stack
                    float sinA = Mathf.Sin(a);
                    bool northArc = sinA > 0.42f && Mathf.Abs(Mathf.Cos(a)) < 0.62f;
                    if (northArc && t >= 1) continue;                   // open mouth on the north face
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, sinA);
                    float yb = northArc ? yBase - 7f : yBase;
                    Vector3 p = cone + dir * rad + Vector3.up * (yb + slabH / 2f);
                    float w = Mathf.Lerp(30f, 12f, f) * (0.82f + (float)rng.NextDouble() * 0.34f);
                    float outYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    // Yaw the slab to face outward, then pitch its top toward the crater centre.
                    var q = Quaternion.Euler(0f, outYaw, 0f) * Quaternion.Euler(-tilt, 0f, 0f);
                    var r = Cube($"Cone{t}_{i}", p, new Vector3(w, slabH, w * 0.7f),
                        (t + i) % 2 == 0 ? rockMat : rockDark, container);
                    r.transform.rotation = q; r.isStatic = true;
                    if (groundLayer >= 0) r.layer = groundLayer;
                }
            }
            // A couple of taller jagged spires on the south rim for a broken-caldera silhouette.
            Rock("ConeSpireW", cone + new Vector3(-30f, 40f, -34f), new Vector3(16f, 46f, 16f), -18f, rockDark);
            Rock("ConeSpireE", cone + new Vector3(34f, 36f, -30f), new Vector3(14f, 40f, 14f), 22f, rockDark);
            }   // end !meshVolcano cube cone

            // ---- Crater: emissive glow ring + light + smoke/ember plume at the summit, visible
            // from the whole valley (the "ember-fall" is always on the horizon). ----
            var craterGlow = Cube("CraterGlow", craterCenter + new Vector3(0f, craterY, 0f), new Vector3(14f, 1.0f, 14f), emberMat, container);
            craterGlow.isStatic = true;
            Object.DestroyImmediate(craterGlow.GetComponent<BoxCollider>());
            var craterLightObj = new GameObject("CraterLight");
            craterLightObj.transform.SetParent(container);
            craterLightObj.transform.position = craterCenter + new Vector3(0f, craterY + 8f, 0f);
            var craterLight = craterLightObj.AddComponent<Light>();
            craterLight.type = LightType.Point;
            craterLight.color = new Color(1f, 0.4f, 0.1f);
            craterLight.range = 90f;
            craterLight.intensity = 5f;
            craterLight.shadows = LightShadows.None;
            var plumeObj = new GameObject("VolcanoPlume");
            plumeObj.transform.SetParent(container);
            plumeObj.transform.position = craterCenter + new Vector3(0f, craterY + 2f, 0f);
            plumeObj.AddComponent<World.VolcanoPlume>();

            // ---- Dungeon entrance: a dark recessed CAVE MOUTH at the volcano's north base, so it
            // reads clearly as the way in with the mesh cone rising behind it. The mouth frames the
            // EntryTunnel (built below) that leads to the boss chamber. ----
            var mouthRock = Mat("VolcanoMouthRock", new Color(0.13f, 0.10f, 0.09f), smoothness: 0.03f);
            var caveDark = Mat("VolcanoCaveDark", new Color(0.02f, 0.02f, 0.03f), smoothness: 0f);
            float ez = VolcanoEntranceZ;
            // A rocky PORCH juts NORTH out of the cone base (toward the approach) so the mouth is
            // always visible even with the huge cone behind it. Dark void recedes south to the
            // EntryTunnel; jagged jambs + a heavy lintel frame a tall opening; boulders flank it.
            var caveVoid = Cube("Mouth_Void", new Vector3(0f, 6f, ez - 8f), new Vector3(14f, 12f, 26f), caveDark, container);
            caveVoid.isStatic = true; Object.DestroyImmediate(caveVoid.GetComponent<BoxCollider>());
            Rock("Mouth_JambW", new Vector3(-10f, 8f, ez + 6f), new Vector3(8f, 17f, 12f), 14f, mouthRock);
            Rock("Mouth_JambE", new Vector3(10f, 8f, ez + 6f), new Vector3(8f, 17f, 12f), -14f, mouthRock);
            Rock("Mouth_Lintel", new Vector3(0f, 15f, ez + 6f), new Vector3(24f, 6f, 10f), 0f, mouthRock);
            Rock("Mouth_Brow", new Vector3(0f, 18.5f, ez + 8f), new Vector3(18f, 4f, 7f), 4f, mouthRock);
            // Flanking boulders + torches mark it from a distance.
            for (int b = 0; b < 4; b++)
            {
                float side = (b % 2 == 0 ? -1f : 1f) * (13f + b * 2f);
                if (PlaceMeshyProp(container, "BoulderCluster", new Vector3(side, 0f, ez + 9f - b), (float)rng.NextDouble() * 360f, 4f + (float)rng.NextDouble() * 3f, groundLayer, PropCollision.FitBox) == null)
                    Rock($"Mouth_Rock{b}", new Vector3(side, 3f, ez + 9f - b), Vector3.one * 6f, 0f, mouthRock);
            }
            CreateTorch(container, new Vector3(-7f, 0f, ez + 10f), 1.3f);
            CreateTorch(container, new Vector3(7f, 0f, ez + 10f), 1.3f);

            // ---- Interior rooms + corridors (all floors walkable, Ground layer) ----
            void Slab(string name, Vector3 pos, Vector3 size, Material mat, bool walkable)
            {
                var o = Cube(name, pos, size, mat, container);
                o.isStatic = true;
                if (groundLayer >= 0) o.layer = groundLayer;
                if (!walkable) { var m = o.AddComponent<NavMeshModifier>(); m.overrideArea = true; m.area = 1; }
            }
            void Room(string name, Vector3 c, Vector2 s)
            {
                Slab($"{name}_Floor", c + Vector3.down * 0.25f, new Vector3(s.x, 0.5f, s.y), floorMat, true);
                Slab($"{name}_Ceiling", c + Vector3.up * 8f, new Vector3(s.x + 2f, 0.8f, s.y + 2f), wallMat, false);

                // Side walls on the room's X edges: rooms are narrower than the outer
                // shell, and without these the player walks off the floor edge into the
                // void between room and shell (reads as "walking through walls").
                // Corridors connect along Z, so X-edge walls never need doorways.
                Slab($"{name}_WallW", c + new Vector3(-s.x / 2f - 0.4f, 4f, 0f), new Vector3(0.8f, 8.5f, s.y + 2f), wallMat, false);
                Slab($"{name}_WallE", c + new Vector3(s.x / 2f + 0.4f, 4f, 0f), new Vector3(0.8f, 8.5f, s.y + 2f), wallMat, false);

                // The ceiling blocks the sun, so every room carries its own warm ember
                // glow — without this the interior is a near-black void (QA shot 11).
                var lo = new GameObject($"{name}_RoomLight");
                lo.transform.SetParent(container);
                lo.transform.position = c + Vector3.up * 6.2f;
                var l = lo.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.55f, 0.28f);
                l.range = Mathf.Max(s.x, s.y) * 1.1f;
                l.intensity = 2.4f;
                l.shadows = LightShadows.None;
            }
            void Wall(string name, Vector3 c, Vector3 s) => Slab(name, c, s, wallMat, false);
            void Ember(string name, Vector3 pos)
            {
                var crystal = Cube($"{name}_C", pos + Vector3.up * 0.8f, new Vector3(0.7f, 1.6f, 0.7f), emberMat, container);
                crystal.isStatic = true;
                var lo = new GameObject($"{name}_L");
                lo.transform.SetParent(container);
                lo.transform.position = pos + Vector3.up * 1.7f;
                var l = lo.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.42f, 0.15f); l.range = 13f; l.shadows = LightShadows.None;
                lo.AddComponent<World.TorchFlicker>().Configure(2.2f, 0.5f, 5f);
            }

            // Entrance tunnel from the mouth into EntryHall.
            Room("EntryTunnel", new Vector3(0f, 0f, -326f), new Vector2(10f, 22f));
            Room("EntryHall", new Vector3(0f, 0f, -338f), new Vector2(34f, 28f));
            Room("CorrAB", new Vector3(0f, 0f, -354f), new Vector2(10f, 12f));
            Room("LavaHall", new Vector3(0f, 0f, -374f), new Vector2(70f, 36f));
            Room("CorrBC", new Vector3(0f, 0f, -396f), new Vector2(10f, 12f));
            Room("Antechamber", new Vector3(0f, 0f, -410f), new Vector2(64f, 20f));
            Room("CorrCD", new Vector3(0f, 0f, -424f), new Vector2(10f, 12f));
            Room("BossChamber", new Vector3(0f, 0f, BossZ), new Vector2(56f, 28f));

            // Outer + partition walls (doors left open at corridor joins).
            Wall("Vol_WestOuter", new Vector3(-36f, 4f, -382f), new Vector3(2f, 9f, 130f));
            Wall("Vol_EastOuter", new Vector3(36f, 4f, -382f), new Vector3(2f, 9f, 130f));
            Wall("Vol_SouthOuter", new Vector3(0f, 4f, -450f), new Vector3(70f, 9f, 2f));
            // Pinch the entry hall / lava hall so passage is via the corridor.
            Wall("Vol_EntrySealW", new Vector3(-22f, 4f, -352f), new Vector3(24f, 9f, 2f));
            Wall("Vol_EntrySealE", new Vector3(22f, 4f, -352f), new Vector3(24f, 9f, 2f));
            Wall("Vol_AnteSealW", new Vector3(-22f, 4f, -394f), new Vector3(24f, 9f, 2f));
            Wall("Vol_AnteSealE", new Vector3(22f, 4f, -394f), new Vector3(24f, 9f, 2f));
            Wall("Vol_BossPinchW", new Vector3(-20f, 4f, -422f), new Vector3(24f, 9f, 2f));
            Wall("Vol_BossPinchE", new Vector3(20f, 4f, -422f), new Vector3(24f, 9f, 2f));

            // Catch floor + entrance rails (keep the player from falling into the void).
            ColliderBox("Vol_CatchFloor", new Vector3(0f, -6f, -384f), new Vector3(120f, 1f, 140f), container, groundLayer);
            ColliderBox("Vol_EntryRailW", new Vector3(-10f, 3f, -316f), new Vector3(12f, 6f, 3f), container, groundLayer);
            ColliderBox("Vol_EntryRailE", new Vector3(10f, 3f, -316f), new Vector3(12f, 6f, 3f), container, groundLayer);

            // Ember pillars.
            foreach (var p in new[]
            {
                new Vector3(-10f, 0f, -374f), new Vector3(10f, 0f, -374f),
                new Vector3(-24f, 0f, -338f), new Vector3(24f, 0f, -338f),
                new Vector3(0f, 0f, -410f), new Vector3(-20f, 0f, BossZ), new Vector3(20f, 0f, BossZ),
            })
                PlaceMeshyProp(container, "DungeonPillar", p, 0f, 3.8f, groundLayer);

            foreach (var pos in new[]
            {
                new Vector3(-8f, 0f, -338f), new Vector3(8f, 0f, -338f),
                new Vector3(-30f, 0f, -366f), new Vector3(30f, 0f, -366f),
                new Vector3(0f, 0f, -410f), new Vector3(0f, 0f, -430f),
            })
                Ember($"VolEmber_{pos.x}_{pos.z}", pos);

            // ---- Lava channels (hazards) down the LavaHall + the boss chamber ----
            // Central channel kept short (z[-383,-361]) so the z-388 guardian camp stays dry.
            BuildLavaPool(container, new Vector3(0f, 0f, -372f), new Vector2(12f, 22f), groundLayer);
            BuildLavaPool(container, new Vector3(-24f, 0f, -368f), new Vector2(10f, 14f), groundLayer);
            BuildLavaPool(container, new Vector3(24f, 0f, -380f), new Vector2(10f, 14f), groundLayer);
            BuildLavaPool(container, new Vector3(-18f, 0f, BossZ), new Vector2(10f, 16f), groundLayer);
            BuildLavaPool(container, new Vector3(18f, 0f, BossZ), new Vector2(10f, 16f), groundLayer);

            // ---- Sealable boss door: solid until the antechamber sentinels are dead ----
            var sealMat = Mat("VolDoorSeal", new Color(0.17f, 0.11f, 0.09f), smoothness: 0.15f,
                emission: new Color(0.16f, 0.04f, 0.02f));
            // Full 10m corridor width + the pinch-wall gap (x[-8,8]) and floor-to-ceiling
            // (y0-8) so the seal can't be slipped around the flanks or double-jumped over.
            var slab = Cube("BossDoor_Seal", new Vector3(0f, 4f, -419f), new Vector3(16.5f, 8f, 0.6f), sealMat, container);
            slab.isStatic = true;
            if (groundLayer >= 0) slab.layer = groundLayer;
            var seNav = slab.AddComponent<NavMeshModifier>(); seNav.overrideArea = true; seNav.area = 1;
            slab.AddComponent<World.SealableDoor>().Configure(new Vector3(0f, 4f, -408f), new Vector3(60f, 14f, 26f));

            // ---- Sealed Floor-2 stairway beyond the boss (SAO seed) ----
            var stairMat = Mat("Floor2Stair", new Color(0.22f, 0.2f, 0.24f), smoothness: 0.15f);
            for (int i = 0; i < 5; i++)
            {
                var step = Cube($"Floor2_Step{i}", new Vector3(0f, 0.3f + i * 0.5f, -444f - i * 0.9f),
                    new Vector3(9f, 0.5f + i * 1f, 1.6f), stairMat, container);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;
            }
            var gateMat = Mat("Floor2Gate", new Color(0.14f, 0.13f, 0.16f), smoothness: 0.2f,
                emission: new Color(0.05f, 0.08f, 0.18f));
            var gate = Cube("Floor2_SealedGate", new Vector3(0f, 3.5f, -449f), new Vector3(10f, 7f, 0.8f), gateMat, container);
            gate.isStatic = true;
            if (groundLayer >= 0) gate.layer = groundLayer;
            // Sealed until the floor is cleared: opens on EmberfallWorldState -> Cleared
            // (Floor Boss death), revealing the stairway to Floor 2 (SAO hook).
            gate.AddComponent<World.WorldStateGate>().Configure(
                World.EmberfallWorldState.Phase.Cleared,
                "Ancient stonework grinds open, the stairway to Floor 2 stands revealed.");
            var signMat = Mat("Floor2Sign", new Color(0.3f, 0.28f, 0.34f), smoothness: 0.1f, emission: new Color(0.1f, 0.14f, 0.3f));
            var sign = Cube("Floor2_Sign", new Vector3(0f, 5f, -446.5f), new Vector3(6f, 1.2f, 0.3f), signMat, container);
            Object.DestroyImmediate(sign.GetComponent<BoxCollider>());

            // ---- Main-quest climax tripwire: first player through the entry tunnel
            // flips the world into the eruption (BeginEruption; server-side).
            var eruptionTrigger = new GameObject("EruptionTrigger");
            eruptionTrigger.transform.SetParent(container, false);
            eruptionTrigger.transform.position = new Vector3(0f, 4f, -326f);
            eruptionTrigger.AddComponent<World.EruptionTrigger>().Configure(
                new Vector3(0f, 4f, -326f), new Vector3(12f, 10f, 24f));

            // Guardian loot.
            CreateLootChest(container, new Vector3(-28f, 0f, -374f), 30f,
                new (string id, float chance)[] { ("knight_sword", 1f), ("adept_focus", 1f), ("iron_chestplate", 0.75f) }, groundLayer);

            // Hidden nook: a rock-screened cache in LavaHall's NE corner — reward for
            // hugging the wall past the lava channel instead of running the center.
            var nookRock = Cube("LavaHall_NookRock", new Vector3(27f, 1.6f, -359f), new Vector3(5f, 3.2f, 1.2f), rockDark, container);
            nookRock.transform.rotation = Quaternion.Euler(0f, 28f, 0f);
            nookRock.isStatic = true;
            if (groundLayer >= 0) nookRock.layer = groundLayer;
            CreateLootChest(container, new Vector3(30f, 0f, -362f), 210f,
                new (string id, float chance)[] { ("ember_focus", 0.5f), ("cooked_bass", 1f), ("knight_sword", 0.6f) }, groundLayer);
            CreateLootChest(container, new Vector3(0f, 0f, -430f), 0f,
                new (string id, float chance)[] { ("ember_focus", 1f), ("emberfang", 0.9f), ("knight_sword", 1f) }, groundLayer);

            // Sealed side cache off the LavaHall's west wall (clear of the central corridor):
            // a rich chest behind a seal that only drops when its two stone guardians fall
            // (guards are a stone_sentinel camp placed inside this AABB in monsters.json).
            var cacheSeal = Cube("LavaHall_CacheSeal", new Vector3(-24f, 4f, -389f), new Vector3(14f, 8f, 0.6f), sealMat, container);
            cacheSeal.isStatic = true;
            if (groundLayer >= 0) cacheSeal.layer = groundLayer;
            var cNav = cacheSeal.AddComponent<NavMeshModifier>(); cNav.overrideArea = true; cNav.area = 1;
            cacheSeal.AddComponent<World.SealableDoor>().Configure(new Vector3(-24f, 4f, -389f), new Vector3(16f, 12f, 12f));
            CreateLootChest(container, new Vector3(-24f, 0f, -391f), 0f,
                new (string id, float chance)[] { ("emberfang", 0.45f), ("iron_chestplate", 1f), ("adept_focus", 0.7f), ("cooked_carp", 1f) }, groundLayer);
        }

        // ==================== ALDRIC'S GLADE (player spawn) ====================

        /// <summary>
        /// A safe, lush forest glade just NORTH of the city (~0,0,90) where the weathered
        /// old hermit Aldric stands — the player's mentor and q_intro giver. The player
        /// spawns here (SceneSetup safeSpawnPosition + NetworkStart positions), the intro
        /// cutscene frames Aldric here, and the city is a short walk south through the gate.
        /// </summary>
        private static void BuildAldricGlade(Transform root, int groundLayer)
        {
            var container = Child(root, "AldricGlade");
            Vector3 center = new Vector3(GladeCenter.x, 0f, GladeCenter.y);   // (0,0,90)

            // Lush overgrowth carpeting the clearing. In terrain mode the instanced detail
            // grass already covers the glade, so skip the old blade field (it rendered as
            // scattered shards on top of the terrain grass).
            if (!UseUnityTerrain)
                BuildGrassBladeField(container, "GladeGrass",
                    center.x - GladeRadius, center.x + GladeRadius, center.z - GladeRadius, center.z + GladeRadius, 6,
                    new Color(0.34f, 0.56f, 0.26f),
                    (x, z) => Vector2.Distance(new Vector2(x, z), GladeCenter) < GladeRadius);

            // A soft ring of lush trees framing the glade (trunk-only colliders).
            var trunkMat = Mat("GladeTrunk", new Color(0.32f, 0.22f, 0.14f), smoothness: 0.05f);
            var leafMat = Mat("GladeLeaf", new Color(0.16f, 0.36f, 0.18f), smoothness: 0.08f);
            string[] kit = { "BirchTree", "PineTree", "BirchTree" };
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f + 0.3f;
                float rad = GladeRadius - 1f + (float)rng.NextDouble() * 3f;
                float x = center.x + Mathf.Cos(a) * rad;
                float z = center.z + Mathf.Sin(a) * rad;
                float h = 7f + (float)rng.NextDouble() * 4f;
                if (PlaceMeshyProp(container, kit[i % kit.Length], new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk) != null) continue;
                PlaceCubePine(container, 9000 + i, x, z, h, trunkMat, leafMat, groundLayer);
            }

            // A few mossy boulders for a lived-in hermit clearing.
            var rockMat = Mat("GladeRock", new Color(0.31f, 0.33f, 0.28f), smoothness: 0.08f);
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = 4f + (float)rng.NextDouble() * (GladeRadius - 9f);
                var rock = Sphere($"GladeRock{i}",
                    center + new Vector3(Mathf.Cos(a) * rad, 0.3f, Mathf.Sin(a) * rad),
                    new Vector3(1.2f + (float)rng.NextDouble(), 0.8f, 1.1f + (float)rng.NextDouble()), rockMat, container);
                rock.isStatic = true;
                if (groundLayer >= 0) rock.layer = groundLayer;
            }

            // The hermit's hearth — a warm campfire beside Aldric.
            var fireObj = new GameObject("GladeHearth");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = center + new Vector3(-3.5f, 0f, 2.5f);
            if (PlaceMeshyProp(fireObj.transform, "Campfire", fireObj.transform.position, rng.Next(0, 360), 0.9f, groundLayer) == null)
            {
                var stoneMat = Mat("GladeFireStone", new Color(0.35f, 0.33f, 0.31f), smoothness: 0.1f);
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * Mathf.PI / 3f;
                    var s = Sphere($"GladeStone{i}",
                        fireObj.transform.position + new Vector3(Mathf.Cos(ang) * 0.55f, 0.1f, Mathf.Sin(ang) * 0.55f),
                        Vector3.one * 0.28f, stoneMat, container);
                    Object.DestroyImmediate(s.GetComponent<SphereCollider>());
                }
            }
            var lo = new GameObject("GladeFireLight");
            lo.transform.SetParent(fireObj.transform);
            lo.transform.localPosition = Vector3.up * 0.8f;
            var fl = lo.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.color = new Color(1f, 0.6f, 0.3f);
            fl.intensity = 2.2f; fl.range = 8f; fl.shadows = LightShadows.None;
            lo.AddComponent<World.TorchFlicker>();

            // Aldric — the weathered robed hermit (q_intro giver), at the glade centre,
            // facing south toward the arriving player. BuildQuestGiver names him "Aldric".
            BuildQuestGiver(container, center, groundLayer);
        }

        // ==================== PHASE-1 SYSTEMS ====================

        private static void BuildEmberfallSystems(Transform root, int groundLayer)
        {
            // ---- EmberAtmosphere (singleton) — falling embers/soot, fog, sun tint ----
            var atmoObj = new GameObject("EmberAtmosphere");
            var atmo = atmoObj.AddComponent<World.EmberAtmosphere>();
            var sun = GameObject.Find("Sun (Dusk)");
            if (sun != null)
            {
                var sunLight = sun.GetComponent<Light>();
                if (sunLight != null)
                {
                    var so = new SerializedObject(atmo);
                    var prop = so.FindProperty("sunLight");
                    if (prop != null) { prop.objectReferenceValue = sunLight; so.ApplyModifiedProperties(); }
                }
            }

            // ---- EmberfallWorldState (networked scene object) ----
            // A scene GameObject with a NetworkIdentity: Mirror spawns scene network objects
            // automatically on server start (NetworkServer.SpawnObjects), so it needs NO entry
            // in NetworkManager.spawnPrefabs (that list is only for runtime-instantiated prefabs).
            // The sceneId is baked when the scene is saved.
            var stateObj = new GameObject("EmberfallWorldState");
            stateObj.AddComponent<Mirror.NetworkIdentity>();
            stateObj.AddComponent<World.EmberfallWorldState>();

            // ---- ServerProgressState (networked scene object) ----
            // Server-wide floor progress + first-clear prestige (Phase 11). A separate scene
            // NetworkIdentity so world atmosphere and server-progress concerns stay decoupled.
            var progressObj = new GameObject("ServerProgressState");
            progressObj.AddComponent<Mirror.NetworkIdentity>();
            progressObj.AddComponent<World.ServerProgressState>();

            // City progress board: a world-space sign near the tavern/plaza showing the
            // server-wide Floor-1 standings (reads ServerProgressState). Purely visual.
            var boardObj = new GameObject("CityProgressBoard");
            boardObj.transform.position = new Vector3(-18f, 2.3f, 18f);
            boardObj.transform.rotation = Quaternion.Euler(0f, 135f, 0f);
            boardObj.AddComponent<World.ProgressBoard>();
            // TODO (Phase-3 quest agent): wire the eruption climax —
            //   * entering the volcano / accepting the final quest -> EmberfallWorldState.Instance.BeginEruption()
            //   * cave_troll_boss (Floor Boss) death -> EmberfallWorldState.Instance.MarkDungeonCleared()
            //     (and unseal the Floor-2 gate at "EmberfallVolcano/Floor2_SealedGate").

            // ---- Safe zones: no unprovoked aggro over the pond (fishing/cooking) and the
            // city, per playtest feedback. Neutral dancers still retaliate if attacked. ----
            BuildSafeZone("SafeZone_Lake", new Vector3(LakeCenter.x, 0f, LakeCenter.z), LakeHalf + 12f);
            BuildSafeZone("SafeZone_City", Vector3.zero, CityHalf + 9f);

            // Q4 "First Blood, First Return": a fallen traveler on the road south of the glade.
            BuildDeadTravelerMarker(groundLayer);

            // Named NPC roster (Elowen, Rusk, Maela, Sella, Garron, Toma, Pip) from npcs.json.
            BuildDataNpcs(root, groundLayer);

            // Q10 gate-defense set-piece (orc waves; a scene NetworkBehaviour like the world-state).
            BuildGateDefense();

            // Q11 "Old Ash, New Fire": three seal stones out in the forest and deepwood.
            BuildSealStone("SealStone_West", new Vector3(-82f, 0f, 96f), groundLayer);
            BuildSealStone("SealStone_East", new Vector3(64f, 0f, 110f), groundLayer);
            BuildSealStone("SealStone_Deep", new Vector3(-30f, 0f, 184f), groundLayer);

            // Near-city eruption consequences: lava breaks through the streets only while Erupting.
            BuildEruptionSpawns(root, groundLayer);

            // Side quest "The Empty Grave": a grave near Sister Maela's shrine, never filled.
            BuildEmptyGrave(new Vector3(-36f, 0f, 13f), groundLayer);

            // Phase 7 gathering + crafting: ore rocks, oak trees, an anvil and a fletch bench.
            BuildGatherCraft(root, groundLayer);
        }

        private static void BuildGatherCraft(Transform root, int groundLayer)
        {
            int interactLayer = LayerMask.NameToLayer("Interactable");
            var container = new GameObject("GatherCraft");
            container.transform.SetParent(root, false);

            // ---- Ore rocks (Mining) ----
            var copperMat = Mat("OreCopper", new Color(0.55f, 0.35f, 0.22f), smoothness: 0.3f, emission: new Color(0.5f, 0.22f, 0.06f));
            var ironMat = Mat("OreIron", new Color(0.42f, 0.42f, 0.46f), smoothness: 0.25f, emission: new Color(0.12f, 0.12f, 0.14f));
            var rockMat = Mat("OreRock", new Color(0.3f, 0.29f, 0.3f), smoothness: 0.1f);
            Vector3[] copper = { new Vector3(-70f, 0f, 40f), new Vector3(76f, 0f, 52f), new Vector3(-58f, 0f, -42f) };
            foreach (var p in copper) BuildOreNode(container.transform, p, "copper_ore", "Copper", 1, rockMat, copperMat, interactLayer, groundLayer);
            Vector3[] iron = { new Vector3(-150f, 0f, 64f), new Vector3(150f, 0f, 72f) };
            foreach (var p in iron) BuildOreNode(container.transform, p, "iron_ore", "Iron", 10, rockMat, ironMat, interactLayer, groundLayer);

            // ---- Oak trees (Woodcutting) ----
            Vector3[] trees = { new Vector3(-90f, 0f, 120f), new Vector3(86f, 0f, 104f), new Vector3(42f, 0f, 150f) };
            foreach (var p in trees) BuildTreeNode(container.transform, p, interactLayer, groundLayer);

            // ---- Anvil (Smithing) near the forge, fletch bench (Fletching) at the market ----
            BuildStation(container.transform, new Vector3(30f, 0f, -10f), "anvil", "Smith at the anvil",
                new Color(0.16f, 0.16f, 0.18f), new Vector3(1.4f, 1.0f, 0.8f), interactLayer, groundLayer);
            BuildStation(container.transform, new Vector3(16f, 0f, -8f), "fletch_bench", "Fletch at the bench",
                new Color(0.4f, 0.28f, 0.16f), new Vector3(1.8f, 0.9f, 0.9f), interactLayer, groundLayer);
        }

        private static void BuildOreNode(Transform parent, Vector3 pos, string item, string label, int level,
            Material rockMat, Material oreMat, int interactLayer, int groundLayer)
        {
            var go = new GameObject($"OreNode_{label}");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            if (interactLayer >= 0) go.layer = interactLayer;

            var rock = Cube("Rock", pos + Vector3.up * 0.8f, new Vector3(2.4f, 1.7f, 2.2f), rockMat, go.transform);
            rock.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() * 8 - 4), (float)(rng.NextDouble() * 360), 0f);
            rock.isStatic = true;
            if (groundLayer >= 0) rock.layer = groundLayer;
            var vein = Cube("Vein", pos + Vector3.up * 1.1f, new Vector3(2.5f, 0.5f, 2.3f), oreMat, go.transform);
            Object.DestroyImmediate(vein.GetComponent<BoxCollider>());
            if (groundLayer >= 0) vein.layer = groundLayer;

            var node = go.AddComponent<World.ResourceNode>();
            node.Configure("Mining", item, "pickaxe", level, 18f, 3f, 8, 20f);
            node.Prompt = $"Mine {label.ToLower()} (Mining {level})";
            node.Radius = 3.2f;
        }

        private static void BuildTreeNode(Transform parent, Vector3 pos, int interactLayer, int groundLayer)
        {
            var go = new GameObject("TreeNode_Oak");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            if (interactLayer >= 0) go.layer = interactLayer;

            var trunkMat = Mat("OakTrunk", new Color(0.34f, 0.24f, 0.15f), smoothness: 0.05f);
            var leafMat = Mat("OakLeaf", new Color(0.26f, 0.45f, 0.2f), smoothness: 0.05f);
            var trunk = Cylinder("Trunk", pos + Vector3.up * 2.2f, new Vector3(0.7f, 2.2f, 0.7f), trunkMat, go.transform);
            trunk.isStatic = true;
            if (groundLayer >= 0) trunk.layer = groundLayer;
            var canopy = Sphere("Canopy", pos + Vector3.up * 5.0f, new Vector3(4.2f, 3.4f, 4.2f), leafMat, go.transform);
            Object.DestroyImmediate(canopy.GetComponent<SphereCollider>());
            if (groundLayer >= 0) canopy.layer = groundLayer;

            var node = go.AddComponent<World.ResourceNode>();
            node.Configure("Woodcutting", "oak_log", "axe", 1, 16f, 3f, 10, 22f);
            node.Prompt = "Chop oak (Woodcutting)";
            node.Radius = 3.4f;
        }

        private static void BuildStation(Transform parent, Vector3 pos, string station, string prompt,
            Color color, Vector3 size, int interactLayer, int groundLayer)
        {
            var go = new GameObject($"Station_{station}");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            if (interactLayer >= 0) go.layer = interactLayer;

            var block = Cube("Block", pos + Vector3.up * (size.y * 0.5f), size, Mat($"Station_{station}", color, smoothness: 0.2f), go.transform);
            block.isStatic = true;
            if (groundLayer >= 0) block.layer = groundLayer;

            var s = go.AddComponent<World.CraftingStation>();
            s.Station = station;
            s.Prompt = prompt;
            s.Radius = 3.2f;
        }

        private static void BuildEmptyGrave(Vector3 pos, int groundLayer)
        {
            var go = new GameObject("EmptyGrave");
            go.transform.position = pos;

            var mound = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mound.name = "Mound";
            mound.transform.SetParent(go.transform, false);
            mound.transform.localScale = new Vector3(1.4f, 0.35f, 2.2f);
            mound.transform.localPosition = new Vector3(0f, 0.17f, 0f);
            var mrend = mound.GetComponent<Renderer>();
            if (mrend != null) mrend.sharedMaterial = Mat("GraveMound", new Color(0.22f, 0.18f, 0.14f), smoothness: 0.05f);
            if (groundLayer >= 0) mound.layer = groundLayer;

            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = "Headstone";
            stone.transform.SetParent(go.transform, false);
            stone.transform.localScale = new Vector3(0.9f, 1.1f, 0.25f);
            stone.transform.localPosition = new Vector3(0f, 0.55f, -1.0f);
            var srend = stone.GetComponent<Renderer>();
            if (srend != null) srend.sharedMaterial = Mat("GraveStone", new Color(0.4f, 0.4f, 0.42f), smoothness: 0.1f);
            if (groundLayer >= 0) stone.layer = groundLayer;

            var marker = go.AddComponent<World.InspectableMarker>();
            marker.MarkerId = "empty_grave";
            marker.InspectText = "The stone bears a name you heard at the gate. The earth is undisturbed, the grave never filled.";
            marker.Prompt = "Inspect the empty grave";
            marker.Radius = 3f;
        }

        private static void BuildEruptionSpawns(Transform root, int groundLayer)
        {
            var go = new GameObject("EruptionSpawns");
            go.transform.SetParent(root, false);
            go.AddComponent<World.EruptionSpawns>();

            var content = Child(go.transform, "Content");
            BuildLavaPool(content, new Vector3(-42f, 0f, 22f), new Vector2(12f, 10f), groundLayer);
            BuildLavaPool(content, new Vector3(40f, 0f, -28f), new Vector2(14f, 10f), groundLayer);
            BuildLavaPool(content, new Vector3(4f, 0f, 50f), new Vector2(10f, 8f), groundLayer);
            content.gameObject.SetActive(false);   // EruptionSpawns switches it on during Erupting
        }

        private static void BuildGateDefense()
        {
            var go = new GameObject("GateDefense");
            go.transform.position = new Vector3(0f, 0f, 60f);   // just inside the north gate
            var dz = go.AddComponent<World.DefendZone>();   // plain MonoBehaviour, server-driven (like MobCamp)
            var so = new SerializedObject(dz);
            so.FindProperty("eventId").stringValue = "gate_defense";
            so.FindProperty("requiredQuestId").stringValue = "q_gate";
            so.FindProperty("mobId").stringValue = "orc_brute";
            so.FindProperty("completeCutscene").stringValue = "q_gate_garron";
            so.ApplyModifiedProperties();
        }

        private static void BuildSealStone(string name, Vector3 pos, int groundLayer)
        {
            var go = new GameObject(name);
            go.transform.position = pos;

            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = "Stone";
            stone.transform.SetParent(go.transform, false);
            stone.transform.localScale = new Vector3(1.0f, 2.2f, 0.7f);
            stone.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            stone.transform.localRotation = Quaternion.Euler(
                (float)(rng.NextDouble() * 8 - 4), (float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 6 - 3));
            var rend = stone.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = Mat("SealStone", new Color(0.34f, 0.33f, 0.36f), smoothness: 0.2f);
            if (groundLayer >= 0) stone.layer = groundLayer;

            var marker = go.AddComponent<World.InspectableMarker>();
            marker.MarkerId = "seal_stone";
            marker.InspectText = "Old runes, worn but warm to the touch. A seal, and behind it something that stirs.";
            marker.Prompt = "Inspect the seal stone";
            marker.Radius = 3.5f;
        }

        private static void BuildDataNpcs(Transform root, int groundLayer)
        {
            int interactableLayer = LayerMask.NameToLayer("Interactable");
            var container = new GameObject("DataNpcs");
            container.transform.SetParent(root, false);
            foreach (var def in World.NpcRoster.LoadAll())
                BuildOneDataNpc(container.transform, def, groundLayer, interactableLayer);
        }

        private static void BuildOneDataNpc(Transform parent, World.NpcDef def, int groundLayer, int interactableLayer)
        {
            var go = new GameObject(string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(def.x, 0f, def.z);
            go.transform.rotation = Quaternion.Euler(0f, def.yaw, 0f);
            if (interactableLayer >= 0) go.layer = interactableLayer;

            switch ((def.role ?? "townsfolk").ToLowerInvariant())
            {
                case "quest":
                {
                    var giver = go.AddComponent<World.QuestGiverNPC>();
                    var so = new SerializedObject(giver);
                    so.FindProperty("npcId").stringValue = string.IsNullOrEmpty(def.questNpcId) ? def.id : def.questNpcId;
                    so.FindProperty("questId").stringValue = "";
                    so.FindProperty("displayName").stringValue = def.displayName;
                    so.FindProperty("prompt").stringValue = $"Talk to {def.displayName}";
                    so.FindProperty("radius").floatValue = 3.5f;
                    so.ApplyModifiedProperties();
                    AddNpcLight(go.transform);
                    break;
                }
                case "vendor":
                {
                    var vendor = go.AddComponent<World.VendorNPC>();
                    var so = new SerializedObject(vendor);
                    so.FindProperty("displayName").stringValue = def.displayName;
                    so.ApplyModifiedProperties();
                    break;
                }
                default:
                {
                    var folk = go.AddComponent<World.TownsfolkNPC>();
                    var so = new SerializedObject(folk);
                    so.FindProperty("displayName").stringValue = def.displayName;
                    var barks = so.FindProperty("barks");
                    if (def.barks != null)
                    {
                        barks.arraySize = def.barks.Length;
                        for (int i = 0; i < def.barks.Length; i++)
                            barks.GetArrayElementAtIndex(i).stringValue = def.barks[i];
                    }
                    so.FindProperty("prompt").stringValue = $"Talk to {def.displayName}";
                    so.FindProperty("radius").floatValue = 2.6f;
                    so.ApplyModifiedProperties();
                    break;
                }
            }

            // Root collider so the player cannot walk through them.
            var col = go.AddComponent<CapsuleCollider>();
            col.height = 2f; col.radius = 0.4f; col.center = new Vector3(0f, 1f, 0f);

            BuildNpcVisual(go.transform, def, groundLayer);
        }

        private static void BuildNpcVisual(Transform npc, World.NpcDef def, int groundLayer)
        {
            var model = SceneSetup.InstantiateGeneratedVisual(def.model, npc);
            if (model != null)
            {
                model.name = "Body";
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                if (groundLayer >= 0) SetLayerRecursive(model, groundLayer);
                return;
            }

            Color robe = ColorUtility.TryParseHtmlString(string.IsNullOrEmpty(def.robeHex) ? "#4A4258" : def.robeHex, out var c)
                ? c : new Color(0.29f, 0.26f, 0.35f);
            var robeMat = SceneSetup.GetOrCreateMaterialAsset(robe, $"Npc_{def.id}_Robe");
            var skinMat = SceneSetup.GetOrCreateMaterialAsset(new Color(0.82f, 0.66f, 0.52f), "NpcSkinMaterial");

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(npc);
            body.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            body.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
            Object.DestroyImmediate(body.GetComponent<Collider>());   // root collider handles solidity
            body.GetComponent<MeshRenderer>().sharedMaterial = robeMat;
            if (groundLayer >= 0) body.layer = groundLayer;

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(npc);
            head.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            head.transform.localScale = Vector3.one * 0.55f;
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.GetComponent<MeshRenderer>().sharedMaterial = skinMat;
            if (groundLayer >= 0) head.layer = groundLayer;
        }

        private static void AddNpcLight(Transform npc)
        {
            var lightObj = new GameObject("QuestLight");
            lightObj.transform.SetParent(npc);
            lightObj.transform.localPosition = Vector3.up * 2.6f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.95f, 0.8f, 0.5f);
            light.intensity = 1.4f;
            light.range = 5f;
            light.shadows = LightShadows.None;
        }

        private static void BuildDeadTravelerMarker(int groundLayer)
        {
            var go = new GameObject("DeadTravelerMarker");
            go.transform.position = new Vector3(7f, 0f, 72f);   // road between the glade (z~84) and city gate

            // A body lying in the grass (capsule on its side) plus a small cairn stone.
            var bodyMat = Mat("DeadTravelerCloak", new Color(0.28f, 0.22f, 0.18f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            body.transform.localRotation = Quaternion.Euler(90f, 20f, 0f);
            body.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            var bodyRend = body.GetComponent<Renderer>();
            if (bodyRend != null) bodyRend.sharedMaterial = bodyMat;
            body.layer = groundLayer;

            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = "Cairn";
            stone.transform.SetParent(go.transform, false);
            stone.transform.localScale = new Vector3(0.6f, 0.5f, 0.4f);
            stone.transform.localPosition = new Vector3(0.9f, 0.25f, 0.2f);
            var stoneRend = stone.GetComponent<Renderer>();
            if (stoneRend != null) stoneRend.sharedMaterial = Mat("DeadTravelerStone", new Color(0.32f, 0.3f, 0.31f));
            stone.layer = groundLayer;

            var marker = go.AddComponent<World.InspectableMarker>();
            marker.MarkerId = "dead_traveler";
            marker.InspectText = "A traveler, cold and still. Yet the earth is scuffed, as if he rose and walked away.";
            marker.Prompt = "Examine the fallen traveler";
            marker.Radius = 3.5f;
        }

        private static void BuildSafeZone(string name, Vector3 center, float radius)
        {
            var go = new GameObject(name);
            go.transform.position = center;
            go.AddComponent<World.SafeZone>().Configure(radius);
        }

        // ==================== WORLD SAFETY (bounds) ====================
        // NOTE: the shared BuildGlobalSafety(root, groundLayer) lives in ArenaBuilder.cs and
        // has been resized to enclose x[-260,260], z[-450,270]. It is called from
        // BuildEmberfallWorld above.
    }
}
#endif
