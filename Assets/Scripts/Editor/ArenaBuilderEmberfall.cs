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
        private static readonly Vector3 LakeCenter = new Vector3(-138f, 0f, 138f);
        private const float LakeHalf = 58f;         // half-width of the open water (~116m across)
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
        private const bool UseUnityTerrain = false;

        private static void BuildEmberfallWorld(Transform root, int groundLayer)
        {
            if (UseUnityTerrain)
                BuildEmberfallTerrain(root, groundLayer);  // real Terrain + splats + wind grass
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
            BuildAldricGlade(root, groundLayer);       // player spawn + hermit mentor (N of city)
            BuildEmberfallSystems(root, groundLayer);
            BuildGlobalSafety(root, groundLayer);      // mountain ring + underfill + catch floor
        }

        // ---- Aldric's forest glade (player spawn) — a lush clearing N of the city ----
        private static readonly Vector2 GladeCenter = new Vector2(0f, 90f);
        private const float GladeRadius = 20f;

        private static bool InGlade(float x, float z)
            => Vector2.Distance(new Vector2(x, z), GladeCenter) < GladeRadius + 3f;

        // ==================== REGION PREDICATES ====================

        private static bool InCityFootprint(float x, float z)
            => Mathf.Abs(x) <= CityHalf + 2f && Mathf.Abs(z) <= CityHalf + 2f;

        private static bool InApproachFootprint(float x, float z)
            => z <= ApproachNorthZ + 2f && z >= ApproachSouthZ && Mathf.Abs(x) <= ApproachHalfW;

        private static bool InLakeWater(float x, float z)
        {
            // Square basin footprint (matches BuildEmberfallLake). Leave a hole so the
            // basin bed shows through instead of a grass tile floating over the water.
            return Mathf.Abs(x - LakeCenter.x) < LakeHalf - 1f
                && Mathf.Abs(z - LakeCenter.z) < LakeHalf - 1f;
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
        private static readonly Vector3[][] RiverPaths =
        {
            new[] { new Vector3(-150f,0f,250f), new Vector3(-146f,0f,220f), new Vector3(-140f,0f,204f), new Vector3(-138f,0f,192f) },
            new[] { new Vector3(85f,0f,248f), new Vector3(40f,0f,238f), new Vector3(-8f,0f,228f), new Vector3(-40f,0f,205f), new Vector3(-70f,0f,196f), new Vector3(-78f,0f,188f) },
            new[] { new Vector3(-40f,0f,205f), new Vector3(-56f,0f,180f), new Vector3(-72f,0f,162f), new Vector3(-78f,0f,155f) },
            new[] { new Vector3(-248f,0f,92f), new Vector3(-224f,0f,102f), new Vector3(-206f,0f,118f), new Vector3(-196f,0f,130f) },
            new[] { new Vector3(245f,0f,62f), new Vector3(198f,0f,76f), new Vector3(150f,0f,92f), new Vector3(104f,0f,106f), new Vector3(56f,0f,116f), new Vector3(18f,0f,124f) },
            new[] { new Vector3(18f,0f,124f), new Vector3(0f,0f,142f), new Vector3(-30f,0f,156f), new Vector3(-60f,0f,162f), new Vector3(-78f,0f,158f) },
            new[] { new Vector3(18f,0f,124f), new Vector3(-16f,0f,112f), new Vector3(-52f,0f,98f), new Vector3(-84f,0f,88f) },
        };
        private static readonly float[] RiverHalfWidths = { 7f, 6f, 4.5f, 6f, 6f, 5f, 4.5f };
        // Waterfall sources: index into RiverPaths whose first point sits at the ring.
        private static readonly int[] RiverSourceFalls = { 0, 1, 3, 4 };

        /// <summary>Fords: shallow wading crossings where a river meets the main road.</summary>
        private static bool IsRiverFord(Vector3 p) => Mathf.Abs(p.x) < 10f && Mathf.Abs(p.z - 140f) < 14f;

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
            const float sizeX = 520f, sizeZ = 592f, sizeY = 40f;
            Vector3 origin = new Vector3(-260f, -sizeY / 2f, -322f);   // surface base sits at y=0
            const int hmRes = 257, splatRes = 256, detailRes = 512;

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
                                 || (Mathf.Abs(wx) < 8f && wz > -322f);   // main N-S road
                    if (!flatZone)
                        h = (Mathf.PerlinNoise(wx * 0.012f + 31.7f, wz * 0.012f + 17.3f) - 0.5f) * 0.5f;

                    if (InCityFootprint(wx, wz))
                        h = -0.15f;   // cobbles (top y=0) sit proud of the pad

                    // Lake bowl: sit just under the built mud banks/bed with a WIDE soft
                    // blend — the deep sharp carve exposed the bank slabs as floating
                    // shelves in QA round 1.
                    float lakeDx = Mathf.Max(Mathf.Abs(wx - LakeCenter.x), Mathf.Abs(wz - LakeCenter.z));
                    if (lakeDx < LakeHalf - 6f)
                        h = -2.9f;
                    else if (lakeDx < LakeHalf + 12f)
                        h = Mathf.Lerp(-2.9f, h, (lakeDx - (LakeHalf - 6f)) / 18f);

                    // Feeder river channel down from the north mountains.
                    if (wz > LakeCenter.z + LakeHalf - 4f && wz < 270f)
                    {
                        float riverDx = Mathf.Abs(wx - FeederRiverX(wz));
                        if (riverDx < 9f)
                            h = -2.6f;
                        else if (riverDx < 15f)
                            h = Mathf.Lerp(-2.6f, h, (riverDx - 9f) / 6f);
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
            data.terrainLayers = new[] { grassLayer, sandLayer, sootLayer, dirtLayer };

            var alpha = new float[splatRes, splatRes, 4];
            for (int iz = 0; iz < splatRes; iz++)
            {
                for (int ix = 0; ix < splatRes; ix++)
                {
                    float wx = origin.x + ix / (float)(splatRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(splatRes - 1) * sizeZ;

                    float soot = SootAmount(wx, wz);
                    bool nearLake = Mathf.Abs(wx - LakeCenter.x) < LakeHalf + 12f
                                 && Mathf.Abs(wz - LakeCenter.z) < LakeHalf + 12f;
                    bool road = Mathf.Abs(wx) < 6f && wz > -322f;

                    float g = 1f, s = 0f, so = 0f, d = 0f;
                    if (soot > 0.05f) { so = Mathf.Clamp01(soot); g = 1f - so; }
                    if (nearLake) { s = 0.85f; g *= 0.15f; }
                    if (road || InCityFootprint(wx, wz)) { d = 0.8f; g *= 0.2f; so *= 0.5f; }

                    float sum = g + s + so + d;
                    alpha[iz, ix, 0] = g / sum;
                    alpha[iz, ix, 1] = s / sum;
                    alpha[iz, ix, 2] = so / sum;
                    alpha[iz, ix, 3] = d / sum;
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
            var detail = new DetailPrototype
            {
                prototype = MakeGrassTuftPrefab(),
                renderMode = DetailRenderMode.VertexLit,
                healthyColor = new Color(0.32f, 0.55f, 0.22f),
                dryColor = new Color(0.42f, 0.44f, 0.16f),
                minWidth = 0.7f, maxWidth = 1.3f,
                minHeight = 0.6f, maxHeight = 1.1f,
                noiseSeed = 42,
                useInstancing = true,
                usePrototypeMesh = true,
            };
            data.detailPrototypes = new[] { detail };

            var density = new int[detailRes, detailRes];
            for (int iz = 0; iz < detailRes; iz++)
            {
                for (int ix = 0; ix < detailRes; ix++)
                {
                    float wx = origin.x + ix / (float)(detailRes - 1) * sizeX;
                    float wz = origin.z + iz / (float)(detailRes - 1) * sizeZ;

                    if (InCityFootprint(wx, wz) || InApproachFootprint(wx, wz)) continue;
                    if (InLakeWater(wx, wz) || InFeederRiver(wx, wz)) continue;
                    if (Mathf.Abs(wx - LakeCenter.x) < LakeHalf + 10f
                     && Mathf.Abs(wz - LakeCenter.z) < LakeHalf + 10f) continue;
                    if (SootAmount(wx, wz) > 0.25f) continue;
                    if (Mathf.Abs(wx) < 7f && wz > -60f) continue;      // keep the road clear
                    float ring = Mathf.Sqrt(wx * wx + wz * wz);
                    if (ring > 250f) continue;

                    // Clumpy coverage reads more natural than a uniform lawn.
                    float clump = Mathf.PerlinNoise(wx * 0.05f + 7.7f, wz * 0.05f + 3.1f);
                    if (clump < 0.35f) continue;
                    density[iz, ix] = clump > 0.6f ? 3 : 1;
                }
            }
            data.SetDetailLayer(0, 0, 0, density);

            AssetDatabase.CreateAsset(data, $"{MaterialDir}/EmberfallTerrain.asset");

            var terrainObj = Terrain.CreateTerrainGameObject(data);
            terrainObj.name = "EmberfallTerrain";
            terrainObj.transform.SetParent(root);
            terrainObj.transform.position = origin;
            if (groundLayer >= 0) terrainObj.layer = groundLayer;
            terrainObj.isStatic = true;
            var terrain = terrainObj.GetComponent<Terrain>();
            terrain.detailObjectDistance = 120f;
            terrain.detailObjectDensity = 1f;
            terrain.heightmapPixelError = 6f;
            // Default basemapDistance (~20m) swaps to the washed-out low-res basemap one
            // screen from the player — THE pale-mint wash in both QA rounds. Keep the
            // real splats out to the fog band.
            terrain.basemapDistance = 400f;
        }

        private static TerrainLayer MakeTerrainLayer(string name, Color baseColor, float noise)
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, true);
            var px = new Color[64 * 64];
            var r = new System.Random(name.GetHashCode());
            for (int i = 0; i < px.Length; i++)
            {
                float n = ((float)r.NextDouble() * 2f - 1f) * noise * 3f;
                px[i] = new Color(
                    Mathf.Clamp01(baseColor.r + n),
                    Mathf.Clamp01(baseColor.g + n),
                    Mathf.Clamp01(baseColor.b + n), 1f);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, $"{MaterialDir}/{name}_Tex.asset");

            var layer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(14f, 14f),
                specular = Color.black,
                smoothness = 0.03f,
            };
            AssetDatabase.CreateAsset(layer, $"{MaterialDir}/{name}.terrainlayer");
            return layer;
        }

        /// <summary>
        /// A crossed-quad grass tuft PREFAB for instanced terrain details (URP path).
        /// Two intersecting vertical quads, vertex-colored dark→light so the terrain
        /// detail tinting reads as depth. Saved under MaterialDir like the layers.
        /// </summary>
        private static GameObject MakeGrassTuftPrefab()
        {
            string prefabPath = $"{MaterialDir}/GrassTuft.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null) return existing;

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
                var dark = new Color(0.16f, 0.3f, 0.12f);
                var light = new Color(0.4f, 0.62f, 0.24f);
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
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{MaterialDir}/GrassTuftMesh.asset");

            var mat = Mat("GrassTuft", new Color(0.3f, 0.5f, 0.2f), smoothness: 0.05f);

            var go = new GameObject("GrassTuft");
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
        }

        private static void ScatterForestTrees(Transform container, int groundLayer)
        {
            var trunkMat = Mat("EmberTreeTrunk", new Color(0.30f, 0.21f, 0.13f), smoothness: 0.05f);
            var leafMat = Mat("EmberPineLeaf", new Color(0.13f, 0.29f, 0.15f), smoothness: 0.08f);
            var charTrunk = Mat("EmberCharTrunk", new Color(0.15f, 0.11f, 0.09f), smoothness: 0.03f);
            var charLeaf = Mat("EmberCharLeaf", new Color(0.17f, 0.13f, 0.10f), smoothness: 0.03f);
            string[] kit = { "PineTree", "BirchTree", "PineTree", "DeadTree" };   // OakTree fbx pending — every miss became a cube pine

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
            for (int attempt = 0; attempt < 1400 && planted < 320; attempt++)
            {
                float x = Mathf.Lerp(-246f, 246f, (float)rng.NextDouble());
                float z = Mathf.Lerp(-52f, 246f, (float)rng.NextDouble());
                float ring = Mathf.Sqrt(x * x + z * z);
                if (ring < 62f || ring > 246f) continue;       // clear the city + trim corners
                if (InCityFootprint(x, z) || InApproachFootprint(x, z)) continue;
                if (InLakeWater(x, z) || InFeederRiver(x, z)) continue;
                if (Mathf.Abs(x - LakeCenter.x) < LakeHalf + 8f
                 && Mathf.Abs(z - LakeCenter.z) < LakeHalf + 8f) continue; // lake shore stays open

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

            // ---- Cobbled ground ----
            var cobble = new Material[4];
            for (int i = 0; i < 4; i++)
            {
                float v = 0.38f + i * 0.03f;
                cobble[i] = Mat($"Cobble{i}", new Color(v, v * 0.97f, v * 0.94f), smoothness: 0.12f);
            }
            var streetMat = Mat("CityStreet", new Color(0.34f, 0.31f, 0.29f), smoothness: 0.1f);
            const float tile = 5f;
            for (float x = -CityHalf; x < CityHalf; x += tile)
                for (float z = -CityHalf; z < CityHalf; z += tile)
                {
                    float cx = x + tile / 2f, cz = z + tile / 2f;
                    // Streets: cross axes between the four gates.
                    bool street = Mathf.Abs(cx) < 5f || Mathf.Abs(cz) < 5f;
                    var mat = street ? streetMat : cobble[rng.Next(cobble.Length)];
                    var t = Cube($"City_{x}_{z}", new Vector3(cx, -0.25f, cz),
                        new Vector3(tile, 0.5f, tile), mat, container);
                    t.isStatic = true;
                    if (groundLayer >= 0) t.layer = groundLayer;
                }

            BuildCityWalls(container, groundLayer);

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
            // Worried Mother (q_rescue giver, npcId "townsperson") lives in the city — kept at (10,0,-6)
            // so the existing q_rescue_start/complete cutscenes still frame correctly.
            BuildRescueGiver(container, groundLayer);
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
                new[] { "cooked_fish", "cooked_trout", "cooked_bass", "raw_fish", "fishing_pole", "apprentice_wand" });

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

        private static void BuildCityWalls(Transform container, int groundLayer)
        {
            var wallMat = Mat("CityWall", new Color(0.34f, 0.31f, 0.32f), smoothness: 0.1f);
            var pillarMat = Mat("CityGatePillar", new Color(0.42f, 0.38f, 0.38f), smoothness: 0.15f);
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
            // Readable but TRANSLUCENT water — 0.82 alpha + strong emission rendered as
            // an opaque cyan slab in QA; this keeps the glint while letting the bed show.
            var waterMat = Mat("LakeWater", new Color(0.13f, 0.38f, 0.5f, 0.62f),
                smoothness: 0.95f, transparent: true, emission: new Color(0.02f, 0.07f, 0.09f));

            Vector3 c = LakeCenter;
            float w = LakeHalf * 2f, depth = WaterSurfaceY - LakeBedY;   // depth ~2.7m (>1.8 swim)
            const float openHalf = 30f;                                  // deep swimmable core half-width

            // Deep bed under the open water (solid, Ground layer — touch bottom and you stand).
            var bed = Cube("Lake_Bed", c + Vector3.up * (LakeBedY - 0.35f),
                new Vector3(openHalf * 2f + 4f, 0.7f, openHalf * 2f + 4f), basinMat, container);
            bed.isStatic = true;
            if (groundLayer >= 0) bed.layer = groundLayer;

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
            MudBank("Lake_BankE", c + new Vector3(mid, midY, 0f), new Vector3(slabLen, 0.6f, w + 2f), new Vector3(0f, 0f, tilt));
            MudBank("Lake_BankW", c + new Vector3(-mid, midY, 0f), new Vector3(slabLen, 0.6f, w + 2f), new Vector3(0f, 0f, -tilt));
            MudBank("Lake_BankN", c + new Vector3(0f, midY, mid), new Vector3(w + 2f, 0.6f, slabLen), new Vector3(-tilt, 0f, 0f));
            MudBank("Lake_BankS", c + new Vector3(0f, midY, -mid), new Vector3(w + 2f, 0.6f, slabLen), new Vector3(tilt, 0f, 0f));

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
            var surf = Cube("Lake_Surface", c + Vector3.up * WaterSurfaceY, new Vector3(w - 0.4f, 0.06f, w - 0.4f), waterMat, container);
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

            // The full river network: four waterfall sources on the mountain ring feeding
            // seven reaches (two splits) that all drain here. The lake still overflows
            // OUT at its NW corner off the map.
            BuildRiverNetwork(container, groundLayer, waterLayer, mudMat, waterMat);
            BuildLakeOutflow(container, groundLayer, waterLayer, mudMat, waterMat);

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
        private static void BuildWaterfall(Transform container, Vector3 basePos, float height, float width, float yaw, Material waterMat)
        {
            var foamMat = Mat("WaterFoam", new Color(0.86f, 0.9f, 0.94f), smoothness: 0.15f,
                emission: new Color(0.22f, 0.26f, 0.30f));
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

            var sheet = Cube("Fall_Sheet", basePos + Vector3.up * (height / 2f),
                new Vector3(width, height, 0.6f), waterMat, container);
            sheet.transform.rotation = rot;
            Object.DestroyImmediate(sheet.GetComponent<BoxCollider>());

            for (int i = 0; i < 8; i++)
            {
                float fx = (float)(rng.NextDouble() - 0.5) * width;
                float fz = (float)(rng.NextDouble() - 0.5) * 3f;
                var foam = Sphere($"Fall_Foam{i}",
                    basePos + rot * new Vector3(fx, 0.3f + (float)rng.NextDouble() * 0.6f, fz),
                    Vector3.one * (0.8f + (float)rng.NextDouble() * 1.2f), foamMat, container);
                Object.DestroyImmediate(foam.GetComponent<SphereCollider>());
            }

            var lightObj = new GameObject("Fall_Light");
            lightObj.transform.SetParent(container);
            lightObj.transform.position = basePos + Vector3.up * 2f;
            var l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.6f, 0.75f, 0.85f);
            l.range = 12f; l.intensity = 1.2f; l.shadows = LightShadows.None;
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
            BuildWaterfall(container, fallBase, 18f, 10f, yaw, waterMat);
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
                BuildWaterfall(container, source + Vector3.up * 0.2f, 14f, RiverHalfWidths[r] * 2f, yaw, waterMat);
            }

            // Ambient fish in the two biggest reaches.
            foreach (var (idx, at) in new[] { (1, 2), (4, 2) })
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

                var bed = Cube($"River{riverIndex}_Bed", mid + Vector3.up * (bedY - 0.35f), new Vector3(halfW * 2f + 1f, 0.7f, len), bedMat, container);
                bed.transform.rotation = rot; bed.isStatic = true;
                if (groundLayer >= 0) bed.layer = groundLayer;

                var vol = new GameObject($"River{riverIndex}_WaterVolume");
                vol.transform.SetParent(container);
                vol.transform.position = mid + Vector3.up * colCy;
                vol.transform.rotation = rot;
                var vbox = vol.AddComponent<BoxCollider>();
                vbox.isTrigger = true;
                vbox.size = new Vector3(halfW * 2f, colH, len);
                if (waterLayer >= 0) vol.layer = waterLayer;

                var surf = Cube($"River{riverIndex}_Surface", mid + Vector3.up * WaterSurfaceY, new Vector3(halfW * 2f - 0.4f, 0.06f, len), waterMat, container);
                surf.transform.rotation = rot;
                Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

                // Wade-in shore steps each bank (skipped at fords, the road runs through).
                if (!ford)
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
                else
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
            // Ring of ascending rock forming the cone (leave the north face open for the mouth).
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                // Skip the north-facing arc so the entrance mouth stays open.
                if (Mathf.Sin(a) > 0.55f && Mathf.Abs(Mathf.Cos(a)) < 0.5f) continue;
                float rad = 58f;
                Vector3 p = cone + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                float hgt = 26f + (float)rng.NextDouble() * 18f;
                Rock($"Cone{i}", new Vector3(p.x, hgt / 2f, p.z), new Vector3(34f, hgt, 34f), (float)rng.NextDouble() * 40f, i % 2 == 0 ? rockMat : rockDark);
            }
            // Inner caldera lip peaks (taller, further out) for the horizon silhouette.
            Rock("ConePeakW", cone + new Vector3(-46f, 24f, -30f), new Vector3(50f, 60f, 50f), -18f, rockDark);
            Rock("ConePeakE", cone + new Vector3(48f, 22f, -26f), new Vector3(46f, 52f, 46f), 22f, rockDark);
            Rock("ConePeakS", cone + new Vector3(0f, 30f, -46f), new Vector3(56f, 72f, 56f), 10f, rockMat);

            // ---- Upper cone: two shrinking ring tiers stacked over the base so the
            // volcano reads as a MOUNTAIN from the city, not another wall of boxes. ----
            for (int tier = 0; tier < 2; tier++)
            {
                float tierRad = 40f - tier * 16f;
                float tierBase = 34f + tier * 26f;
                int n = 10 - tier * 3;
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    if (tier == 0 && Mathf.Sin(a) > 0.75f) continue;   // keep the north face lower
                    Vector3 p = cone + new Vector3(Mathf.Cos(a) * tierRad, 0f, Mathf.Sin(a) * tierRad);
                    float hgt = 20f + (float)rng.NextDouble() * 10f;
                    Rock($"ConeT{tier}_{i}", new Vector3(p.x, tierBase + hgt / 2f, p.z),
                        new Vector3(26f - tier * 8f, hgt, 26f - tier * 8f),
                        (float)rng.NextDouble() * 40f, (i + tier) % 2 == 0 ? rockMat : rockDark);
                }
            }

            // ---- Crater: emissive glow ring + light + smoke/ember plume, visible from
            // the whole valley (the "ember-fall" is always on the horizon). ----
            float craterY = 78f;
            var craterGlow = Cube("CraterGlow", cone + new Vector3(0f, craterY, 0f), new Vector3(20f, 1.2f, 20f), emberMat, container);
            craterGlow.isStatic = true;
            Object.DestroyImmediate(craterGlow.GetComponent<BoxCollider>());
            var craterLightObj = new GameObject("CraterLight");
            craterLightObj.transform.SetParent(container);
            craterLightObj.transform.position = cone + new Vector3(0f, craterY + 8f, 0f);
            var craterLight = craterLightObj.AddComponent<Light>();
            craterLight.type = LightType.Point;
            craterLight.color = new Color(1f, 0.4f, 0.1f);
            craterLight.range = 90f;
            craterLight.intensity = 5f;
            craterLight.shadows = LightShadows.None;
            var plumeObj = new GameObject("VolcanoPlume");
            plumeObj.transform.SetParent(container);
            plumeObj.transform.position = cone + new Vector3(0f, craterY + 2f, 0f);
            plumeObj.AddComponent<World.VolcanoPlume>();

            // ---- Entrance mouth at the cone base ----
            Rock("MouthLintel", new Vector3(0f, 9f, VolcanoEntranceZ - 3f), new Vector3(16f, 4f, 4f), 0f, rockDark);
            CreateTorch(container, new Vector3(-7f, 0f, VolcanoEntranceZ), 1.1f);
            CreateTorch(container, new Vector3(7f, 0f, VolcanoEntranceZ), 1.1f);

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

            // Lush overgrowth carpeting the clearing.
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
