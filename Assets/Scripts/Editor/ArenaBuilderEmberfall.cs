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
        private static float CityHalf = 55f;         // populated from authoritative region data
        private static float CityGate = 10f;

        // ---- Lake (NW) ----
        // v8: Jon called the old 88m lake "a puddle at best". Genuinely big and DEEP now,
        // sized against the NW quadrant (edges x[-196,-80], z[80,196]) with the whole
        // river network draining into it.
        // Environment/Waterworks milestone: the lake is the northern STARTING vista (player +
        // Aldric spawn on its south shore), city-sized and cradled by mountains with a forest
        // buffer. Progression runs south: lake -> dark woods -> city -> ember approach -> volcano.
        private static Vector3 LakeCenter = new Vector3(0f, 0f, 300f);
        private static float LakeHalf = 95f;
        private const float LakeBedY = -5.0f;       // depth = WaterSurfaceY - LakeBedY = 4.65m (deep swim)

        // ---- Ember approach (south corridor) ----
        private static float ApproachHalfW = 62f;
        private static float ApproachNorthZ = -55f;
        private static float ApproachSouthZ = -315f;
        private static float VolcanicVisualNorthZ = -90f;

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
            _worldDef = EmberfallWorldDefinition.Load();
            ApplyAuthoredWorldScale();
            if (UseUnityTerrain)
            {
                BuildEmberfallTerrain(root, groundLayer);  // real Terrain + splats + wind grass
                // The forest + ground ecosystem live in BuildEmberfallGround (legacy path); wire
                // them into the terrain path too or the world has no scattered trees/foliage/rocks.
                BuildPlacementMap();                        // zone/occupancy map = single source of truth
                var eco = Child(root, "EmberEcosystem");
                ScatterForestTrees(eco, groundLayer);       // these RECORD into the catalog (no conflicts)
                ScatterGroundDetail(eco, groundLayer);
                ScatterRoadDetail(eco, groundLayer);
                RenderCatalog(eco, groundLayer);            // instantiate everything from the catalog
            }
            else
                BuildEmberfallGround(root, groundLayer);   // legacy cube tiles (rollback path)
            BuildEmberfallCity(root, groundLayer);
            BuildEmberfallLake(root, groundLayer);
            BuildDeepDarkWoods(root, groundLayer);
            BuildWorldSecrets(root, groundLayer);
            BuildEmberForestCamps(root, groundLayer);
            BuildEmberOrcWarcamp(root, groundLayer);
            BuildRoadsideFences(root, groundLayer);
            BuildEmberApproach(root, groundLayer);
            BuildEmberfallVolcano(root, groundLayer);
            BuildMountainRange(root, groundLayer);     // real mountain-mesh border (when assets exist)
            BuildAuthoredVistas(root, groundLayer);
            BuildAldricGlade(root, groundLayer);       // player spawn + hermit mentor (N of city)
            BuildEmberfallSystems(root, groundLayer);
            BuildGlobalSafety(root, groundLayer);      // mountain ring + underfill + catch floor
            // Export AFTER everything is placed (city buildings/furniture, camps, approach) so the
            // JSON mapper captures ALL objects incl. the enterable buildings + interiors, not just
            // the trees. (Was exported mid-build, before the city existed.)
            if (_place != null) ExportCatalog(root);
        }

        // ---- Aldric's forest glade (player spawn) — a lush clearing N of the city ----
        private static Vector2 GladeCenter = new Vector2(0f, 200f);
        private static float GladeRadius = 20f;

        private static EmberfallWorldDefinition.RegionDef Region(string id)
        {
            foreach (var r in _worldDef.regions) if (r.id == id) return r;
            throw new System.InvalidOperationException($"World definition is missing required region '{id}'");
        }

        private static void ApplyAuthoredWorldScale()
        {
            var city = Region("emberfall_city");
            CityHalf = Mathf.Min(city.radiusX, city.radiusZ);
            CityGate = 18f;
            var lake = Region("great_lake");
            LakeCenter = new Vector3(lake.center[0], 0f, lake.center[1]);
            LakeHalf = Mathf.Min(lake.radiusX, lake.radiusZ);
            var glade = Region("aldric_glade");
            GladeCenter = new Vector2(glade.center[0], glade.center[1]);
            GladeRadius = Mathf.Max(glade.radiusX, glade.radiusZ);
            var approach = Region("ember_approach");
            ApproachHalfW = approach.radiusX;
            ApproachNorthZ = -CityHalf;
            ApproachSouthZ = VolcanoEntranceZ + 30f;
            VolcanicVisualNorthZ = approach.center[1] + approach.radiusZ;
            _encounterDef = EmberfallEncounterDefinition.Load();
            RiverPaths = _worldDef.BuildRiverPaths();
            RiverHalfWidths = new float[_worldDef.rivers.Length];
            for (int i = 0; i < RiverHalfWidths.Length; i++) RiverHalfWidths[i] = _worldDef.rivers[i].halfWidth;
        }

        private static bool InAuthoredRegion(EmberfallWorldDefinition.RegionDef r, float x, float z)
        {
            if (r?.center == null || r.center.Length < 2) return false;
            float dx = (x - r.center[0]) / Mathf.Max(1f, r.radiusX);
            float dz = (z - r.center[1]) / Mathf.Max(1f, r.radiusZ);
            return dx * dx + dz * dz <= 1f;
        }

        private static float DistanceToAuthoredRoutes(float x, float z, out float halfWidth)
        {
            float best = float.MaxValue; halfWidth = 0f; var p = new Vector2(x, z);
            foreach (var route in _worldDef.routes)
                for (int i = 0; i < route.points.Length - 1; i++)
                {
                    float d = DistToSegment(p, new Vector2(route.points[i].x, route.points[i].z),
                        new Vector2(route.points[i + 1].x, route.points[i + 1].z));
                    if (d < best) { best = d; halfWidth = route.width * 0.5f; }
                }
            return best;
        }

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
        private static Vector3[][] RiverPaths =
        {
            new[] { new Vector3(0f,0f,485f), new Vector3(2f,0f,450f), new Vector3(-4f,0f,420f), new Vector3(0f,0f,398f) },
            new[] { new Vector3(320f,0f,455f), new Vector3(230f,0f,415f), new Vector3(140f,0f,378f), new Vector3(72f,0f,352f) },
            new[] { new Vector3(-320f,0f,455f), new Vector3(-230f,0f,415f), new Vector3(-140f,0f,378f), new Vector3(-72f,0f,352f) },
            new[] { new Vector3(435f,0f,45f), new Vector3(300f,0f,40f), new Vector3(170f,0f,28f), new Vector3(60f,0f,12f), new Vector3(-8f,0f,6f), new Vector3(-64f,0f,40f), new Vector3(-84f,0f,120f), new Vector3(-64f,0f,200f), new Vector3(-40f,0f,235f) },
        };
        private static float[] RiverHalfWidths = { 8f, 7f, 7f, 8f };
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
                // Clear grass/props well past the water edge (water spans ~halfWidth+0.75): a wider
                // bare bank stops grass tufts sitting AT the waterline and reading as marsh/flood.
                float reach = RiverHalfWidths[r] + 5.5f;
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

        // ==================== DATA-DRIVEN PLACEMENT (single source of truth) ====================
        private static PlacementMap _place;
        private static PlacementCatalog _catalog;
        private static PlacementCatalog _detailCatalog;   // scattered rocks/fences/shrubs (record-only, merged at export)
        private static WorldManifest _manifest;           // authoritative world record: ground grid + objects + barriers + buildings
        private static EmberfallWorldDefinition _worldDef; // authoritative authored INPUT (Assets/World/Emberfall)
        private static EmberfallEncounterDefinition _encounterDef;
        private const float MountainRingR = 244f;         // playable edge; beyond = impassable mountain ring

        /// <summary>Record a directly-placed scatter prop into the detail catalog so it lands in the
        /// source-of-truth JSON + validation, WITHOUT re-instantiating it (it's already placed).</summary>
        /// <summary>Ground elevation at (x,z) from the sampled terrain (0 before heights are sampled).</summary>
        private static float GY(float x, float z) => (_place != null && _place.HasHeights) ? _place.HeightAt(x, z) : 0f;

        /// <summary>True when (x,z) is clear of the main road's canopy buffer -- trees keep 20m off the
        /// 12m-half road so their wide canopies don't overhang the path (applies to EVERY tree scatter).</summary>
        private static bool OffRoad(float x, float z) => !(Mathf.Abs(x) < 20f && z > -72f && z < 220f);

        /// <summary>Record a direct (non-TryScatter) placement into the detail catalog. `footprint` is the
        /// solid reservation radius (0 = soft foliage that may nestle and is skipped by overlap checks).
        /// y defaults to the terrain sample; pass the real placed y (realY) so the validator can catch
        /// floating/buried numerically against the terrain height.</summary>
        private static void RecordDetail(string type, float x, float z, float yaw, float h, float footprint = 0f, float realY = float.NaN)
        {
            if (_detailCatalog == null || _place == null || !_place.InBounds(x, z)) return;
            float y = float.IsNaN(realY) ? GY(x, z) : realY;
            _detailCatalog.Add(type, x, y, z, yaw, h, footprint,
                _place.ZoneAt(x, z), _place.BiomeAt(x, z), _place.GroundAt(x, z));
        }

        // Camp / POI centres the scatter must keep clear (marked Camp zone in the map).
        private static readonly Vector2[] CampCenters =
        {
            new Vector2(-195f, 0f), new Vector2(195f, 0f), new Vector2(-110f, -120f),
            new Vector2(155f, 55f), new Vector2(-160f, 45f),
        };

        /// <summary>Build the world occupancy/zone map — the classification every scatterer queries.
        /// One place decides "what is at (x,z)"; nothing guesses independently after this.</summary>
        private static void BuildPlacementMap()
        {
            _worldDef ??= EmberfallWorldDefinition.Load();
            var wb = _worldDef.bounds;
            _place = new PlacementMap(wb.minX, wb.minZ, wb.maxX, wb.maxZ);
            _catalog = new PlacementCatalog();
            _detailCatalog = new PlacementCatalog();
            ResetSolidHash();   // fresh placement gate for this bake
            _suppressAutoRecord = false;
            _place.Classify((x, z) =>
            {
                float edge = Mathf.Min(Mathf.Min(x - wb.minX, wb.maxX - x),
                                       Mathf.Min(z - wb.minZ, wb.maxZ - z));
                // ---- ZONE (priority order) ----
                PlaceZone zone;
                bool nearCity = Mathf.Abs(x) < CityHalf + 8f && Mathf.Abs(z) < CityHalf + 8f;
                if (InLakeWater(x, z) || InAnyRiver(x, z) || DistToCityCanal(x, z) < 9f) zone = PlaceZone.Water;
                else if (InCityFootprint(x, z)) zone = PlaceZone.City;
                else if (nearCity) zone = PlaceZone.Wall;                                  // 8m ring around the walls
                else if (InGlade(x, z)) zone = PlaceZone.Glade;
                else if (InApproachFootprint(x, z)) zone = PlaceZone.Approach;
                else if (DistanceToAuthoredRoutes(x, z, out float routeHalf) < routeHalf) zone = PlaceZone.Road;
                else if (edge < 42f) zone = PlaceZone.Mountain;
                else
                {
                    zone = PlaceZone.Open;
                    foreach (var c in _encounterDef.camps)
                    {
                        float dx = x - c.position[0], dz = z - c.position[2], reach = Mathf.Max(12f, c.radius + 6f);
                        if (dx * dx + dz * dz < reach * reach) { zone = PlaceZone.Camp; break; }
                    }
                }
                // ---- BIOME ----
                float soot = SootAmount(x, z);
                float lakeDist = Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) - LakeEdge(x, z);
                PlaceBiome biome;
                if (soot > 0.4f) biome = PlaceBiome.Volcanic;
                else if (lakeDist < 16f) biome = PlaceBiome.Shore;
                else
                {
                    biome = PlaceBiome.Meadow;
                    foreach (var region in _worldDef.regions)
                    {
                        if (!InAuthoredRegion(region, x, z)) continue;
                        if (region.id == "north_woods") biome = PlaceBiome.DarkForest;
                        else if (region.kind == "forest" || region.id == "aldric_glade") biome = PlaceBiome.Lush;
                        break;
                    }
                }
                return (zone, biome);
            });

            // Sample terrain elevation into the grid (terrain is built before this) so every recorded
            // object carries a real ground Y and the validator can check floating/buried numerically.
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float y0 = terrain.transform.position.y;
                _place.SampleHeights((x, z) => terrain.SampleHeight(new Vector3(x, 0f, z)) + y0);
            }

            _manifest = new WorldManifest(_place, _catalog)
            {
                MountainRingRadius = Mathf.Min((_worldDef.bounds.maxX - _worldDef.bounds.minX) * 0.5f,
                    (_worldDef.bounds.maxZ - _worldDef.bounds.minZ) * 0.5f),
                PlayerSpawn = new Vector3(0f, GY(0f, GladeCenter.y - 22f), GladeCenter.y - 22f),
            };

            // Let shared spawn code (CreateCamp) keep mob camps out of the lake/rivers.
            WaterTest = (x, z) => InLakeWater(x, z) || InAnyRiver(x, z);
        }

        /// <summary>Record a placement into the catalog IF the map says it fits (right zones, no
        /// overlap). Returns true when accepted. Nothing is instantiated here — the render pass
        /// reads the catalog. This is the choke point that guarantees no tree-in-road/water/etc.</summary>
        private static bool TryScatter(string type, float x, float z, float yaw, float h, float radius,
            System.Func<PlaceZone, bool> zoneOk)
        {
            if (_place == null || !_place.CanPlace(x, z, radius, zoneOk)) return false;
            if (radius > 0.05f && SolidOverlaps(x, z, radius)) return false;
            _catalog.Add(type, x, GY(x, z), z, yaw, h, radius, _place.ZoneAt(x, z), _place.BiomeAt(x, z), _place.GroundAt(x, z));
            if (radius > 0.05f) { _place.Occupy(x, z, radius); SolidAdd(x, z, radius); }
            return true;
        }

        /// <summary>Zone+overlap guard for DIRECT (non-catalog) scatters that run after the catalog
        /// export - keeps them out of road/water/city and off already-placed catalog props (Occupies
        /// on success). Returns true (allow) when no map exists (non-terrain build path). Full catalog
        /// migration of these scatters is a follow-up; this at least enforces the zone rules.</summary>
        private static bool MapClear(float x, float z, float radius)
        {
            if (_place == null) return true;
            if (!_place.CanPlace(x, z, radius, zn => zn == PlaceZone.Open)) return false;
            _place.Occupy(x, z, radius);
            return true;
        }

        /// <summary>Reserve a SOLID prop's footprint on the shared occupancy grid so it can't land on a
        /// tree / rock / fence that already reserved that spot (occupancy only -- the caller already chose
        /// the zone). Returns null (skip) if the spot is taken. This is the "solve numerically" guard --
        /// route rocks/decorative-trees through it; small foliage stays direct (undergrowth may nestle).</summary>
        private static GameObject PlaceSolid(Transform container, string name, float x, float z, float yaw,
            float h, float radius, int groundLayer, PropCollision collision)
        {
            // Solids never belong in water / on the city stone / in a wall / on the bare mountain rock;
            // otherwise the caller already chose the zone, so this is an occupancy (no-overlap) check.
            if (_place != null && !_place.CanPlace(x, z, radius,
                    zn => zn != PlaceZone.Water && zn != PlaceZone.City && zn != PlaceZone.Wall && zn != PlaceZone.Mountain))
                return null;   // footprint taken or structurally-wrong ground
            // PlaceMeshyProp auto-records the object into the manifest; on success we also reserve the
            // footprint so LATER solids can't overlap it (the prevention half of the data guarantee).
            var g = PlaceMeshyProp(container, name, new Vector3(x, 0f, z), yaw, h, groundLayer, collision);
            if (g != null && _place != null) _place.Occupy(x, z, radius);
            return g;
        }

        /// <summary>Warm-dark tint on a placed rock so roadside/dirt stones sit IN the earth instead
        /// of popping as bright white granite (distinct "_Earth" material, shared + cached).</summary>
        private static void EarthTint(GameObject go)
        {
            if (go == null || IsKitProp(go)) return;   // never flatten a real Poly Haven material
            var tinted = MeshyCharacterImporter.BuildTexturedMaterial(go.name + "_Earth",
                $"Assets/Models/Props/{go.name}_baseColor", new Color(0.52f, 0.46f, 0.39f));
            if (tinted != null) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = tinted;
        }

        /// <summary>A curated Poly Haven kit prop (tree/plant/rock) -- it already carries real textured
        /// leaf/bark materials, so the flat-colour Tint/EarthTint helpers must leave it alone.</summary>
        private static bool IsKitProp(GameObject go)
        {
            var r = go != null ? go.GetComponentInChildren<Renderer>() : null;
            var n = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
            return n.Contains("_leaf") || n.Contains("_bark");
        }

        /// <summary>Instantiate everything in the catalog. Collision + foliage tint are derived from
        /// the type + position (deterministic) so the JSON stays pure placement data.</summary>
        private static void RenderCatalog(Transform container, int groundLayer)
        {
            RenderTerrainTrees();
            var chunks = new Dictionary<long, Transform>();
            float chunkSize = _worldDef.performance.chunkSize;
            Transform ChunkFor(float x, float z)
            {
                int cx = Mathf.FloorToInt((x - _worldDef.bounds.minX) / chunkSize);
                int cz = Mathf.FloorToInt((z - _worldDef.bounds.minZ) / chunkSize);
                long key = ((long)cx << 32) ^ (uint)cz;
                if (chunks.TryGetValue(key, out var found)) return found;
                var go = new GameObject($"VisualChunk_{cx}_{cz}"); go.transform.SetParent(container);
                var marker = go.AddComponent<World.WorldChunkMarker>();
                marker.center = new Vector2(_worldDef.bounds.minX + (cx + 0.5f) * chunkSize,
                    _worldDef.bounds.minZ + (cz + 0.5f) * chunkSize);
                marker.halfDiagonal = chunkSize * 0.708f;
                chunks[key] = go.transform; return go.transform;
            }
            _suppressAutoRecord = true;   // catalog objects are ALREADY recorded (TryScatter); don't double
            try
            {
            foreach (var kv in _catalog.Objects)
            {
                string category = PlacementCatalog.CategoryOf(kv.Key);
                if (category == "tree" || category == "deadtree") continue; // native Terrain instances
                var collision = CatalogCollision(kv.Key);
                foreach (var it in kv.Value)
                {
                    var go = PlaceMeshyProp(ChunkFor(it.x, it.z), kv.Key, new Vector3(it.x, 0f, it.z), it.yaw, it.h, groundLayer, collision);
                    var tint = CatalogTint(kv.Key, it.x, it.z, it.biome);
                    if (go != null && tint.HasValue)
                    {
                        var m = Mat($"Fol_{Mathf.RoundToInt(tint.Value.r*12)}_{Mathf.RoundToInt(tint.Value.g*12)}_{Mathf.RoundToInt(tint.Value.b*12)}", tint.Value, smoothness: 0.03f);
                        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
                    }
                    // NOTE: do NOT add it.y here. The prop snaps ITSELF to the terrain (PlaceKitTree),
                    // and it.y now stores the terrain height (manifest data) — re-adding it double-counts
                    // and floats the prop by the local terrain height (the dark-forest +1.7m float bug).
                }
            }
            }
            finally { _suppressAutoRecord = false; }
            var streamer = container.GetComponent<World.WorldChunkStreamer>() ?? container.gameObject.AddComponent<World.WorldChunkStreamer>();
            streamer.activeRadius = _worldDef.performance.normalSightline;
        }

        /// <summary>Real photoscan trees as Terrain tree instances. This replaces thousands of
        /// instantiated LOD hierarchies with Unity's native batched distance culling while keeping
        /// the exact catalog placements as source of truth.</summary>
        private static void RenderTerrainTrees()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null || _catalog == null) return;
            var prototypes = new List<TreePrototype>();
            var instances = new List<TreeInstance>();
            var naturalHeights = new List<float>();

            foreach (var kv in _catalog.Objects)
            {
                string category = PlacementCatalog.CategoryOf(kv.Key);
                if (category != "tree" && category != "deadtree") continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TreeKitImporter.PrefabDir}/{kv.Key}.prefab")
                    ?? AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{kv.Key}.fbx");
                if (prefab == null) continue;
                int prototype = prototypes.Count;
                prototypes.Add(new TreePrototype { prefab = prefab, bendFactor = category == "tree" ? 0.12f : 0f });

                float naturalHeight = 1f;
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                if (temp != null)
                {
                    bool have = false; Bounds bounds = default;
                    foreach (var renderer in temp.GetComponentsInChildren<Renderer>())
                    { if (!have) { bounds = renderer.bounds; have = true; } else bounds.Encapsulate(renderer.bounds); }
                    if (have) naturalHeight = Mathf.Max(0.1f, bounds.size.y);
                    Object.DestroyImmediate(temp);
                }
                naturalHeights.Add(naturalHeight);

                foreach (var item in kv.Value)
                {
                    float scale = Mathf.Clamp(item.h / naturalHeight, 0.35f, 2.2f);
                    float nx = (item.x - terrain.transform.position.x) / terrain.terrainData.size.x;
                    float nz = (item.z - terrain.transform.position.z) / terrain.terrainData.size.z;
                    float ny = (GY(item.x, item.z) - terrain.transform.position.y) / terrain.terrainData.size.y;
                    instances.Add(new TreeInstance
                    {
                        position = new Vector3(nx, ny, nz), prototypeIndex = prototype,
                        widthScale = scale, heightScale = scale,
                        rotation = item.yaw * Mathf.Deg2Rad,
                        color = Color.white, lightmapColor = Color.white
                    });
                }
            }
            terrain.terrainData.treePrototypes = prototypes.ToArray();
            terrain.terrainData.SetTreeInstances(instances.ToArray(), true);
            // Photoscan LODGroup tree prototypes have NO terrain billboard asset -- beyond
            // treeBillboardDistance they don't billboard, they VANISH (the "empty forest" bug).
            // Draw real meshes the whole way; the prefabs' own LOD levels absorb the cost.
            terrain.treeDistance = 320f;
            terrain.treeBillboardDistance = terrain.treeDistance;
            terrain.treeCrossFadeLength = 12f;
            terrain.treeMaximumFullLODCount = 180;
            EditorUtility.SetDirty(terrain.terrainData);
        }

        private static PropCollision CatalogCollision(string type)
        {
            string n = type.ToLowerInvariant();
            if (n.Contains("tree")) return PropCollision.Trunk;
            if (n.Contains("boulder") || n.Contains("rock") || n.Contains("cliff") || n.Contains("moss")) return PropCollision.FitBox;
            return PropCollision.None;
        }

        // Deterministic foliage tint (position hash) so beds vary without storing per-item colours.
        private static Color? CatalogTint(string type, float x, float z, PlaceBiome biome)
        {
            string n = type.ToLowerInvariant();
            float Hash() { float s = Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f; return s - Mathf.Floor(s); }
            if (n.Contains("wildflower") || n.Contains("flower"))
            {
                Color[] pal = { new Color(0.62f,0.40f,0.66f), new Color(0.80f,0.72f,0.28f), new Color(0.86f,0.86f,0.80f), new Color(0.72f,0.34f,0.40f) };
                return pal[Mathf.FloorToInt(Hash() * pal.Length) % pal.Length];
            }
            if (n.Contains("reed")) return Color.Lerp(new Color(0.30f,0.38f,0.14f), new Color(0.40f,0.46f,0.20f), Hash());
            if (n == "fern" && biome == PlaceBiome.DarkForest) return Color.Lerp(new Color(0.09f,0.18f,0.10f), new Color(0.14f,0.24f,0.12f), Hash());
            if (n.Contains("fern") || n.Contains("bush")) return Color.Lerp(new Color(0.13f,0.26f,0.12f), new Color(0.20f,0.36f,0.15f), Hash());
            return null;
        }

        private static float CatalogFootprint(string type)
        {
            string n = type.ToLowerInvariant();
            if (n.Contains("tree")) return 3.0f;
            if (n.Contains("boulder") || n.Contains("rock") || n.Contains("cliff")) return 2.0f;
            return 0.6f;
        }

        // Mob-spawn data mirrors the object catalog: monsters.json already holds each mob's camp
        // coordinates + roam radius + count, so we run it through the SAME zone map to map mob
        // locations/density and flag conflicts (camp or roam overlapping water/road/city/mountain).
        [System.Serializable] private class MJRoot { public MJMonster[] monsters; }
        [System.Serializable] private class MJMonster { public string id; public MJSpawn[] spawns; }
        [System.Serializable] private class MJSpawn { public string kind; public float x, y, z; public int count; public float radius; }

        /// <summary>Parse monsters.json spawns, validate each camp+roam against the placement map,
        /// write mob_placements.json (single source of truth for mob locations), and log conflicts +
        /// density so mobs-in-water / camps-on-the-road / overlaps surface the same way tree
        /// conflicts do.</summary>
        private static void ExportAndValidateMobs(PlacementMap map)
        {
            var encounterDef = EmberfallEncounterDefinition.Load();
            var grouped = new Dictionary<string, List<MJSpawn>>();
            foreach (var c in encounterDef.camps)
            {
                if (!grouped.TryGetValue(c.mobId, out var list)) { list = new List<MJSpawn>(); grouped[c.mobId] = list; }
                list.Add(new MJSpawn { kind = c.kind, x = c.position[0], y = c.position[1], z = c.position[2], count = c.count, radius = c.radius });
            }
            var monsters = new List<MJMonster>();
            foreach (var pair in grouped) monsters.Add(new MJMonster { id = pair.Key, spawns = pair.Value.ToArray() });
            var root = new MJRoot { monsters = monsters.ToArray() };

            var sb = new System.Text.StringBuilder(1 << 16);
            sb.Append("{\n  \"mobs\": {\n");
            int totalCamps = 0, totalMobs = 0, conflicts = 0;
            var issues = new System.Collections.Generic.List<string>();
            var centres = new System.Collections.Generic.List<(string id, float x, float z, float r)>();
            bool firstType = true;
            foreach (var m in root.monsters)
            {
                if (m.spawns == null || m.spawns.Length == 0) continue;
                if (!firstType) sb.Append(",\n");
                firstType = false;
                sb.Append("    \"").Append(m.id).Append("\": [\n");
                for (int i = 0; i < m.spawns.Length; i++)
                {
                    var sp = m.spawns[i];
                    totalCamps++; totalMobs += Mathf.Max(1, sp.count);
                    centres.Add((m.id, sp.x, sp.z, sp.radius));
                    var zc = map.InBounds(sp.x, sp.z) ? map.ZoneAt(sp.x, sp.z) : PlaceZone.Open;
                    int water = 0, road = 0, city = 0, mtn = 0;   // roam-ring zone tally (8 samples)
                    for (int a = 0; a < 8; a++)
                    {
                        float ang = a / 8f * Mathf.PI * 2f;
                        float rx = sp.x + Mathf.Cos(ang) * sp.radius, rz = sp.z + Mathf.Sin(ang) * sp.radius;
                        if (!map.InBounds(rx, rz)) continue;
                        switch (map.ZoneAt(rx, rz))
                        {
                            case PlaceZone.Water: water++; break;
                            case PlaceZone.Road: road++; break;
                            case PlaceZone.City: city++; break;
                            case PlaceZone.Mountain: mtn++; break;
                        }
                    }
                    bool centreBad = zc == PlaceZone.Water || zc == PlaceZone.City || zc == PlaceZone.Mountain;
                    if (centreBad || water > 0)
                    {
                        conflicts++;
                        issues.Add($"{m.id} camp#{i} @({sp.x:0},{sp.z:0}) r={sp.radius:0}: centre={zc} roam[water={water} road={road} city={city} mtn={mtn}]");
                    }
                    sb.Append("      {\"p\":[").Append(sp.x.ToString("0.#")).Append(",").Append(sp.y.ToString("0.#")).Append(",").Append(sp.z.ToString("0.#"))
                      .Append("],\"count\":").Append(sp.count).Append(",\"roam\":").Append(sp.radius.ToString("0.#"))
                      .Append(",\"zone\":\"").Append(zc).Append("\"}").Append(i < m.spawns.Length - 1 ? ",\n" : "\n");
                }
                sb.Append("    ]");
            }
            // Camp-vs-camp overcrowding: centres whose roam circles heavily overlap a DIFFERENT type.
            for (int i = 0; i < centres.Count; i++)
                for (int j = i + 1; j < centres.Count; j++)
                {
                    if (centres[i].id == centres[j].id) continue;
                    float dx = centres[i].x - centres[j].x, dz = centres[i].z - centres[j].z;
                    float dd = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dd < (centres[i].r + centres[j].r) * 0.6f)
                    { conflicts++; issues.Add($"overlap {centres[i].id} x {centres[j].id} centres {dd:0}m apart (roam {centres[i].r:0}+{centres[j].r:0})"); }
                }
            sb.Append("\n  },\n  \"meta\": { \"camps\": ").Append(totalCamps).Append(", \"mobs\": ").Append(totalMobs)
              .Append(", \"conflicts\": ").Append(conflicts).Append(" }\n}\n");
            System.IO.File.WriteAllText("Assets/StreamingAssets/mob_placements.json", sb.ToString());
            Debug.Log($"MOBS wrote {totalCamps} camps ({totalMobs} mobs) -> mob_placements.json; conflicts={conflicts}");
            foreach (var l in issues) Debug.Log($"MOBS CONFLICT {l}");
            _mobCentres = centres;   // fed to the in-game F3 overlay so mobs are mappable live too
        }

        private static System.Collections.Generic.List<(string id, float x, float z, float r)> _mobCentres;

        /// <summary>Scene-TRUTH audit: iterate the actually-placed curated kit props (each is a LODGroup
        /// named after its asset) and flag real conflicts -- trunks in water, floating/buried, oversize for
        /// their type, or untextured/fallback materials. Runs every headless build and prints a CATALOG
        /// summary + the first offenders, so a regression like "giant ferns in the lake" is caught at bake
        /// time instead of on Jon's screen. Fix the SOURCE (scatter call / classifier), not the symptom.</summary>
        private static void AuditScene(Transform root)
        {
            var terrain = Terrain.activeTerrain;
            float terrY0 = terrain != null ? terrain.transform.position.y : 0f;
            int total = 0, inWater = 0, floating = 0, buried = 0, untextured = 0, oversize = 0;
            var byType = new System.Collections.Generic.Dictionary<string, int>();
            var offenders = new System.Collections.Generic.List<string>();
            // Every SOLID prop (tree/rock/stump) with a footprint radius, for cross-type overlap detection.
            var solids = new System.Collections.Generic.List<(Vector2 p, float r, string name)>();

            foreach (var lg in root.GetComponentsInChildren<LODGroup>(true))
            {
                var go = lg.gameObject;
                var kit = TreeKitImporter.Classify(go.name);
                if (kit == TreeKitImporter.KitType.Unknown) continue;   // only curated kit props
                total++;
                // Record solids (not small foliage -- undergrowth is meant to nestle around trees/rocks).
                if (kit == TreeKitImporter.KitType.Tree || kit == TreeKitImporter.KitType.DeadTree
                    || kit == TreeKitImporter.KitType.Rock || kit == TreeKitImporter.KitType.Stump)
                {
                    float fr = kit == TreeKitImporter.KitType.Tree ? 3.2f : kit == TreeKitImporter.KitType.DeadTree ? 2.2f
                             : kit == TreeKitImporter.KitType.Rock ? 1.3f : 0.8f;
                    solids.Add((new Vector2(go.transform.position.x, go.transform.position.z), fr, go.name));
                }
                string tk = kit.ToString();
                byType[tk] = byType.TryGetValue(tk, out var c) ? c + 1 : 1;

                var rends = go.GetComponentsInChildren<MeshRenderer>(true);
                if (rends.Length == 0) continue;
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                Vector3 p = go.transform.position;

                if (InLakeWater(p.x, p.z) || InAnyRiver(p.x, p.z))
                { inWater++; if (offenders.Count < 25) offenders.Add($"IN-WATER {go.name} @({p.x:F0},{p.z:F0})"); }

                if (terrain != null)
                {
                    float gy = terrain.SampleHeight(p) + terrY0;
                    if (b.min.y > gy + 1.6f) { floating++; if (offenders.Count < 25) offenders.Add($"FLOATING {go.name} @({p.x:F0},{p.z:F0}) +{b.min.y - gy:F1}m"); }
                    else if (b.min.y < gy - 1.2f) { buried++; if (offenders.Count < 25) offenders.Add($"BURIED {go.name} @({p.x:F0},{p.z:F0}) {b.min.y - gy:F1}m"); }
                }

                var (hMin, hMax) = TreeKitImporter.HeightRange(kit);
                if (b.size.y > hMax * 1.35f || b.size.y < hMin * 0.5f)
                { oversize++; if (offenders.Count < 25) offenders.Add($"OVERSIZE {go.name} {b.size.y:F1}m (range {hMin}-{hMax})"); }

                var mat = rends[0].sharedMaterial;
                if (mat == null || (mat.mainTexture == null && !mat.name.ToLower().Contains("bark")))
                { untextured++; if (offenders.Count < 25) offenders.Add($"UNTEXTURED {go.name} mat={(mat != null ? mat.name : "null")}"); }
            }

            // CROSS-TYPE OVERLAP (the "solve numerically" pass): two SOLID props whose footprints intersect
            // -- rock-in-tree, tree-in-tree, rock-in-fence. Bucketed into a grid so it's ~O(n), not O(n^2).
            int overlaps = 0;
            const float cell = 8f;
            var grid = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<int>>();
            for (int i = 0; i < solids.Count; i++)
            {
                var c = ((int)Mathf.Floor(solids[i].p.x / cell), (int)Mathf.Floor(solids[i].p.y / cell));
                if (!grid.TryGetValue(c, out var l)) { l = new System.Collections.Generic.List<int>(); grid[c] = l; }
                l.Add(i);
            }
            for (int i = 0; i < solids.Count; i++)
            {
                var a = solids[i];
                int cx = (int)Mathf.Floor(a.p.x / cell), cy = (int)Mathf.Floor(a.p.y / cell);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (grid.TryGetValue((cx + dx, cy + dy), out var l))
                            foreach (int j in l)
                            {
                                if (j <= i) continue;
                                var b = solids[j];
                                float mind = (a.r + b.r) * 0.7f;
                                if ((a.p - b.p).sqrMagnitude < mind * mind)
                                {
                                    overlaps++;
                                    if (offenders.Count < 25) offenders.Add($"OVERLAP {a.name} ~ {b.name} @({a.p.x:F0},{a.p.y:F0})");
                                }
                            }
            }

            // PLACEHOLDER guard: a Unity primitive (Cube/Cylinder/Sphere...) used as SCENERY (a tree,
            // bush, foliage) is the exact "cube-tree / lollipop" regression -- flag it so it can never
            // ship again. Primitives are fine as STRUCTURE (building shells), so only foliage names count.
            int placeholders = 0;
            string[] prims = { "Cube", "Cylinder", "Sphere", "Capsule", "Quad", "Plane" };
            string[] sceneryWords = { "tree", "leaf", "canopy", "trunk", "pine", "bush", "foliage", "shrub", "fern", "reed" };
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || System.Array.IndexOf(prims, mf.sharedMesh.name) < 0) continue;
                string gn = mf.gameObject.name.ToLower();
                foreach (var w in sceneryWords)
                    if (gn.Contains(w)) { placeholders++; if (offenders.Count < 25) offenders.Add($"PLACEHOLDER-PRIMITIVE {mf.gameObject.name} ({mf.sharedMesh.name})"); break; }
            }

            var sb = new System.Text.StringBuilder();
            foreach (var kv in byType) { if (sb.Length > 0) sb.Append(", "); sb.Append($"{kv.Key}:{kv.Value}"); }
            Debug.Log($"AUDIT kit props: {total} ({sb})");
            Debug.Log($"AUDIT conflicts: inWater={inWater} floating={floating} buried={buried} oversize={oversize} untextured={untextured} placeholders={placeholders} solidOverlaps={overlaps}");
            foreach (var o in offenders) Debug.Log($"AUDIT {o}");
            if (inWater + floating + oversize + untextured + placeholders + overlaps == 0) Debug.Log("AUDIT CLEAN — no kit-prop conflicts");
        }

        /// <summary>COMPREHENSIVE scene-truth audit of EVERY mesh object in the world (not just kit props).
        /// Groups objects by name-pattern and flags, per group: primitive-as-scenery, floating, buried,
        /// underwater, and fat colliders (invisible walls). Writes an uncapped report to WorldData/
        /// world_audit.txt so problems are FOUND from the data, not from screenshots. Fix the source.</summary>
        /// <summary>Remove every missing-script component from the scene before save. A MonoBehaviour
        /// whose class can't be resolved (e.g. class defined in a mismatched file name) bakes as a
        /// "missing script"; the editor tolerates it but the STANDALONE build writes its orphaned data
        /// into level0 and the typetree-less player misreads it -- "level0 corrupted" +
        /// "[Position out of bounds!]" instant crash. Purge + log loudly so it can never ship.</summary>
        private static void PurgeMissingScripts()
        {
            int purged = 0, gos = 0;
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                int n = UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (n > 0)
                {
                    purged += UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                    gos++;
                }
            }
            if (purged > 0)
                Debug.LogWarning($"MISSING SCRIPTS purged: {purged} components on {gos} objects -- find the class-in-wrong-file bug that created them!");
            else
                Debug.Log("MISSING SCRIPTS: 0");
        }

        private static void DumpWorldAudit(Transform root)
        {
            var terrain = Terrain.activeTerrain;
            float y0 = terrain != null ? terrain.transform.position.y : 0f;
            var prims = new System.Collections.Generic.HashSet<string> { "Cube", "Cylinder", "Sphere", "Capsule", "Quad", "Plane" };
            // Structural things that are SUPPOSED to be off the terrain surface (buildings, walls, water,
            // mountains, dungeon, bridges) -- excluded from floating/buried so the report shows real junk.
            string[] structural = { "wall", "floor", "roof", "gable", "building", "shell", "bldg", "tav_", "city",
                "dungeon", "volcano", "mountain", "peak", "foothill", "berm", "bridge", "canal", "rail", "plaza",
                "tile", "road", "cobble", "step", "stair", "fountain", "gate", "pillar", "beam", "timber", "window",
                "door", "chimney", "banner", "sign", "lamp", "torch", "furn", "bed", "table", "chair", "shelf",
                "barrel", "crate", "anvil", "cauldron", "rug", "waterfall", "foam", "water", "lava", "crater",
                "plume", "spike", "cart", "well", "fish", "star", "moon", "sky", "cloud", "impostor", "billboard",
                // structural continued: river/canal surfaces, dungeon+volcano rooms, decals, corners, windows
                "river", "surf", "culvert", "ceiling", "seal", "mouth", "arch", "jamb", "lintel", "brow", "block",
                "vol_", "volember", "corner", "ridge", "lin#", "win", "w#", "rut", "mud", "ash", "vein", "soot",
                "corr", "hall", "chamber", "ante", "entry", "boss", "outer", "pinch", "void", "lake_", "fall_",
                "char", "orc", "camp", "cage", "tent", "bone", "totem", "spear", "firestone", "lip", "crate",
                "mountainridge", "chest" };

            var byPrefix = new System.Collections.Generic.Dictionary<string, int[]>();   // [count, prim, float, bury, water, fatCol]
            var sample = new System.Collections.Generic.Dictionary<string, string>();
            var meshName = new System.Collections.Generic.Dictionary<string, string>();

            float BaseY(GameObject go)
            {
                bool any = false; float m = float.MaxValue;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null) continue;
                    var mtx = mf.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 cr = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        float wy = mtx.MultiplyPoint3x4(cr).y; if (wy < m) m = wy; any = true;
                    }
                }
                return any ? m : go.transform.position.y;
            }

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = mf.gameObject; var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                if (go.GetComponentInParent<LODGroup>() != null) continue;   // kit props: AuditScene owns them
                Vector3 p = go.transform.position;
                string prefix = System.Text.RegularExpressions.Regex.Replace(go.name, @"\d+", "#").Replace("(Clone)", "");
                bool isPrim = prims.Contains(mesh.name);
                string lname = prefix.ToLowerInvariant();   // digit-runs are '#', so "w#_#"/"lin#" match
                bool isStruct = false; foreach (var s in structural) if (lname.Contains(s)) { isStruct = true; break; }

                if (!byPrefix.TryGetValue(prefix, out var st)) { st = new int[6]; byPrefix[prefix] = st; sample[prefix] = $"({p.x:0},{p.z:0})"; meshName[prefix] = mesh.name; }
                st[0]++;
                if (isPrim && !isStruct) st[1]++;
                // The structural exemption shields water/foam VFX QUADS (legit surfaces), but a
                // SPHERE under a water/foam/waterfall name is never legit -- that was the "3 white
                // balls" foam-blob regression. Flag it even though the name is structural.
                else if (mesh.name == "Sphere" && (lname.Contains("foam") || lname.Contains("water") || lname.Contains("fall_"))) st[1]++;

                if (!isStruct && terrain != null)
                {
                    float baseY = BaseY(go), gy = terrain.SampleHeight(p) + y0;
                    if (baseY > gy + 1.5f) st[2]++;
                    else if (baseY < gy - 1.2f) st[3]++;
                }
                if (!isStruct && _place != null && _place.InBounds(p.x, p.z) && (InLakeWater(p.x, p.z) || InAnyRiver(p.x, p.z))) st[4]++;

                // Fat collider: a BoxCollider whose XZ footprint is much larger than the mesh (invisible
                // wall). Skip structural (mountains/crates/buildings legitimately have big box colliders).
                var box = isStruct ? null : go.GetComponent<BoxCollider>();
                if (box != null)
                {
                    Vector3 cs = Vector3.Scale(box.size, go.transform.lossyScale);
                    Vector3 ms = Vector3.Scale(mesh.bounds.size, go.transform.lossyScale);
                    if (ms.x > 0.01f && ms.z > 0.01f && (cs.x > ms.x * 1.7f || cs.z > ms.z * 1.7f)) st[5]++;
                }
            }

            var lines = new System.Collections.Generic.List<(int severity, string line)>();
            int tPrim = 0, tFloat = 0, tBury = 0, tWater = 0, tFat = 0;
            foreach (var kv in byPrefix)
            {
                var s = kv.Value; int issues = s[1] + s[2] + s[3] + s[4] + s[5];
                tPrim += s[1]; tFloat += s[2]; tBury += s[3]; tWater += s[4]; tFat += s[5];
                if (issues == 0) continue;
                var tags = new System.Collections.Generic.List<string>();
                if (s[1] > 0) tags.Add($"PRIM:{s[1]}"); if (s[2] > 0) tags.Add($"float:{s[2]}"); if (s[3] > 0) tags.Add($"bury:{s[3]}");
                if (s[4] > 0) tags.Add($"water:{s[4]}"); if (s[5] > 0) tags.Add($"fatCol:{s[5]}");
                lines.Add((issues, $"{kv.Key,-26} x{s[0],-4} [{meshName[kv.Key]}]  {string.Join(" ", tags)}  e.g.{sample[kv.Key]}"));
            }
            lines.Sort((a, b) => b.severity.CompareTo(a.severity));

            var sb = new System.Text.StringBuilder();
            sb.Append("# World object audit (scene-truth, every mesh). Groups by name-pattern.\n");
            sb.Append($"# PRIM=primitive-as-scenery  float/bury=off terrain  water=in lake/river  fatCol=collider>>mesh (invisible wall)\n");
            sb.Append($"TOTALS: primScenery={tPrim} floating={tFloat} buried={tBury} underwater={tWater} fatColliders={tFat}\n\n");
            foreach (var l in lines) sb.Append(l.line).Append('\n');
            System.IO.Directory.CreateDirectory("WorldData");
            System.IO.File.WriteAllText("WorldData/world_audit.txt", sb.ToString());
            Debug.Log($"WORLD AUDIT: primScenery={tPrim} floating={tFloat} buried={tBury} underwater={tWater} fatColliders={tFat} (WorldData/world_audit.txt)");
        }

        /// <summary>Write the catalog to the source-of-truth JSON, log the validation report, and
        /// spawn the in-game F3 coord/zone overlay that reads the same map.</summary>
        private static void ExportCatalog(Transform root)
        {
            try
            {
                string dir = "Assets/StreamingAssets";
                System.IO.Directory.CreateDirectory(dir);
                string path = $"{dir}/world_placements.json";
                if (_detailCatalog != null) _catalog.Merge(_detailCatalog);   // scattered rocks/fences/shrubs too
                System.IO.File.WriteAllText(path, _catalog.ToJson(_place));
                Debug.Log($"CATALOG wrote {_catalog.Count} placements -> {path}");
                foreach (var s in _catalog.Validate(_place, CatalogFootprint)) Debug.Log($"CATALOG {s}");
                ExportAndValidateMobs(_place);   // mobs run through the SAME map: locations + conflicts

                // ---- Authoritative World Manifest: numerical conflict scan + readable data dump ----
                // This is the data-first gate: I read WorldData/ + this count to KNOW the world is clean
                // before QA. Ship only when WORLD CONFLICTS == 0.
                if (_manifest != null)
                {
                    var conflicts = _manifest.Validate();
                    _manifest.Dump("WorldData", conflicts);
                    Debug.Log($"WORLD CONFLICTS: {conflicts.Count} (see WorldData/manifest.md + conflicts.json)");
                    var byKind = new System.Collections.Generic.Dictionary<string, int>();
                    foreach (var c in conflicts) byKind[c.kind] = (byKind.TryGetValue(c.kind, out var v) ? v : 0) + 1;
                    foreach (var kv in byKind) Debug.Log($"WORLD CONFLICT {kv.Key}: {kv.Value}");
                }

                PurgeMissingScripts();           // missing-script components corrupt the standalone build
                AuditScene(root);                // scene-TRUTH audit of the actually-placed kit props
                DumpWorldAudit(root);            // COMPREHENSIVE audit of EVERY mesh (finds junk from data)
            }
            catch (System.Exception e) { Debug.Log($"CATALOG export failed: {e.Message}"); }

            var ov = new GameObject("WorldCoordOverlay");
            ov.transform.SetParent(root);
            var comp = ov.AddComponent<AldenFall.World.WorldCoordOverlay>();
            comp.originX = _place.OriginX; comp.originZ = _place.OriginZ;
            comp.cellSize = PlacementMap.Cell; comp.gridW = _place.W; comp.gridH = _place.H;
            comp.zones = _place.ZoneBytes();
            if (_mobCentres != null && _mobCentres.Count > 0)
            {
                int n = _mobCentres.Count;
                comp.mobIds = new string[n]; comp.mobX = new float[n]; comp.mobZ = new float[n]; comp.mobRoam = new float[n];
                for (int i = 0; i < n; i++)
                { comp.mobIds[i] = _mobCentres[i].id; comp.mobX[i] = _mobCentres[i].x; comp.mobZ[i] = _mobCentres[i].z; comp.mobRoam[i] = _mobCentres[i].r; }
            }
            EditorUtility.SetDirty(comp);
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
            _worldDef ??= EmberfallWorldDefinition.Load();
            // Expanded world (Environment milestone): room for a city-sized north lake + forest
            // buffer + real mountains, keeping city@origin + volcano south.
            var worldBounds = _worldDef.bounds;
            float sizeX = worldBounds.maxX - worldBounds.minX;
            float sizeZ = worldBounds.maxZ - worldBounds.minZ;
            const float sizeY = 220f;
            Vector3 origin = new Vector3(worldBounds.minX, -sizeY / 2f, worldBounds.minZ);
            // Preserve roughly the old metres-per-sample density across the expanded footprint.
            // Detail is still distance-culled to 140m; total map area does not become active detail.
            const int hmRes = 1025, splatRes = 1024, detailRes = 2048;

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
                                 || DistanceToAuthoredRoutes(wx, wz, out float terrainRouteHalf) < terrainRouteHalf;
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

                    // Authored traversal-room occluders. These are world-design inputs, not random
                    // noise: they restrict ordinary sightlines to the 140m budget and stage reveals
                    // around roads, the glade, camps, lake and volcano approach.
                    if (!flatZone)
                        h = Mathf.Max(h, 0.7f + _worldDef.LandformHeight(wx, wz));

                    // Glade (player spawn) + the lake/city road are DRY flatzones - lift them clear
                    // of the water sheet too, or it bleeds through the flat spawn (Aldric standing
                    // in a "flood"). North of the ember corridor only; the volcano approach stays low.
                    if (InGlade(wx, wz) || DistanceToAuthoredRoutes(wx, wz, out float dryRouteHalf) < dryRouteHalf)
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
            // Warmer OLIVE grass (was a cold flat green that clashed with the warm Poly Haven foliage
            // and read as a manicured lawn); warmer earthy dirt + rock to match the photoscan rocks.
            // Muted olive to MATCH the grass blades above (was a brighter green -> "green ground under
            // dead grass"). Reads as shaded earth/undergrowth beneath the blades, same hue family.
            var grassLayer = MakeTerrainLayer("TL_Grass", new Color(0.22f, 0.27f, 0.16f), 0.04f);   // DE-NEON: G pulled toward R, lower value (was a glowing green ground)
            var sandLayer = MakeTerrainLayer("TL_Sand", new Color(0.42f, 0.36f, 0.26f), 0.03f);   // muddier: bright sand washed pale at distance
            var sootLayer = MakeTerrainLayer("TL_Soot", new Color(0.115f, 0.09f, 0.08f), 0.04f);
            var dirtLayer = MakeTerrainLayer("TL_Dirt", new Color(0.38f, 0.29f, 0.18f), 0.04f);
            var rockLayer = MakeTerrainLayer("TL_Rock", new Color(0.34f, 0.30f, 0.27f), 0.02f);   // warmer grey-brown, matches namaqualand
            var cobbleLayer = MakeTerrainLayer("TL_Cobble", new Color(0.42f, 0.41f, 0.39f), 0.03f);
            data.terrainLayers = new[] { grassLayer, sandLayer, sootLayer, dirtLayer, rockLayer, cobbleLayer };

            var alpha = new float[splatRes, splatRes, 6];
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
                    bool road = DistanceToAuthoredRoutes(wx, wz, out float splatRouteHalf) < splatRouteHalf;
                    float edge = Mathf.Min(Mathf.Min(wx - origin.x, origin.x + sizeX - wx),
                                           Mathf.Min(wz - origin.z, origin.z + sizeZ - wz));
                    bool southCorridor = Mathf.Abs(wx) < ApproachHalfW + 24f && wz < -90f;

                    float g = 1f, s = 0f, so = 0f, d = 0f, rk = 0f;
                    if (!southCorridor && edge < 150f) { rk = Mathf.Clamp01((150f - edge) / 95f); g *= 1f - rk; }  // rock mountains
                    // Volcanic south gradient: green -> dry dirt -> cracked lava toward the volcano.
                    if (wz < -300f)
                    {
                        // A long authored ecological gradient: living southern forest first,
                        // then dry/ailing forest, deadwood/ash foothills, and only finally lava.
                        float southT = Mathf.Clamp01(Mathf.InverseLerp(-300f, ApproachSouthZ, wz));
                        so = Mathf.Clamp01((southT - 0.58f) * 2.6f);                      // lava/ash at the deep end
                        float dryDirt = Mathf.Clamp01(1f - Mathf.Abs(southT - 0.45f) * 3.0f) * (1f - so);  // dirt peaks mid
                        d = Mathf.Max(d, dryDirt);
                        g *= Mathf.Clamp01(1f - southT * 1.9f);                            // green fades out going south
                        rk *= 1f - so;
                    }
                    // The ember CORRIDOR itself is charred end-to-end: dark soot from the
                    // south gate ramping to full at the volcano. Pale dirt here read as a
                    // watery sheet at grazing angles (bare-bright-ground = fake water).
                    if (Mathf.Abs(wx) < ApproachHalfW + 8f && wz < VolcanicVisualNorthZ)
                    {
                        float ct = Mathf.InverseLerp(VolcanicVisualNorthZ, ApproachSouthZ, wz);
                        so = Mathf.Max(so, Mathf.Lerp(0.6f, 1f, ct));
                        g = 0f; d *= 0.25f; rk = 0f;
                    }
                    // (No random dirt patches in the meadow - they read as stray mud puddles. Dirt
                    // is intentional only: roads + the volcanic approach.)
                    if (shore) { s = 0.8f; g *= 0.2f; rk *= 0.2f; }                                                 // sand/mud shores
                    if (road || InCityFootprint(wx, wz)) { d = Mathf.Max(d, 0.85f); g *= 0.15f; so *= 0.4f; }       // dirt roads + city

                    // COBBLESTONE transition: on the road just OUTSIDE each city gate, cobbles emerge
                    // from the dirt (full at the wall, fading to dirt over ~16m) so the manmade street
                    // eases into the natural trail instead of a hard dirt/cobble seam.
                    float cb = 0f;
                    if (road && !InCityFootprint(wx, wz))
                    {
                        float gd = -1f;
                        if (wz >= CityHalf && wz < CityHalf + 16f) gd = wz - CityHalf;          // north gate -> lake
                        else if (wz <= -CityHalf && wz > -CityHalf - 16f) gd = -CityHalf - wz;  // south gate -> approach
                        if (gd >= 0f) { cb = Mathf.Clamp01(1f - gd / 16f); d *= 1f - cb; g *= 1f - cb; }
                    }

                    float sum = g + s + so + d + rk + cb;
                    if (sum <= 0f) { g = 1f; sum = 1f; }
                    alpha[iz, ix, 0] = g / sum;
                    alpha[iz, ix, 1] = s / sum;
                    alpha[iz, ix, 2] = so / sum;
                    alpha[iz, ix, 3] = d / sum;
                    alpha[iz, ix, 4] = rk / sum;
                    alpha[iz, ix, 5] = cb / sum;
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
                // MUTED OLIVE/SAGE, not kelly-green: the bright saturated grass clashed hard with the
                // desaturated photoscan trees/rocks. R pulled up toward G (desaturate) + warmed toward khaki.
                healthyColor = new Color(0.34f, 0.38f, 0.19f),   // muted olive
                dryColor = new Color(0.52f, 0.46f, 0.26f),       // warm khaki dry blades
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
            // A THIRD detail layer for the DIRT ROADS: short, sparse dry-earth stubble + gravel
            // clumps so the bare road bed carries 3D speckle depth under the SAME GPU-instanced
            // detail engine as the grass. NOTE: an earlier "DirtTuft" detail-mesh carpet on the road
            // read as blotchy angular quads - the REAL dirt look (ref: New World) comes from the
            // richly-mottled Dirt() albedo (baked damp/dry variation) + occasional real rocks + mud
            // decals, NOT a carpet of flecks. So the road carries no detail-mesh layer.
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
                    if (DistanceToAuthoredRoutes(wx, wz, out float grassRouteHalf) < grassRouteHalf) continue;
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
                    if (DistanceToAuthoredRoutes(wx, wz, out float ashRouteHalf) < ashRouteHalf) continue;
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
            terrain.detailObjectDistance = 140f;   // perf: grass is the densest thing; 140m keeps the near ground lush while cutting far-grass instance count hard (250m was a big cost)
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
            string scanDir = "Assets/Textures/Ground/PolyHaven";
            string scanDiffuse = name == "TL_Grass" ? $"{scanDir}/forest_ground_04_diff_2k.jpg"
                : name == "TL_Dirt" ? $"{scanDir}/brown_mud_leaves_01_diff_2k.jpg" : null;
            string scanNormal = name == "TL_Grass" ? $"{scanDir}/forest_ground_04_nor_gl_2k.jpg"
                : name == "TL_Dirt" ? $"{scanDir}/brown_mud_leaves_01_nor_gl_2k.jpg" : null;
            var scannedAlbedo = scanDiffuse != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(scanDiffuse) : null;
            if (scannedAlbedo != null)
            {
                AssetDatabase.DeleteAsset($"{MaterialDir}/{name}.terrainlayer");
                var scanned = new TerrainLayer
                {
                    diffuseTexture = scannedAlbedo,
                    normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(scanNormal),
                    tileSize = new Vector2(name == "TL_Dirt" ? 7.5f : 6f, name == "TL_Dirt" ? 7.5f : 6f),
                    normalScale = 1f,
                    specular = Color.black,
                    metallic = 0f,
                    smoothness = name == "TL_Grass" ? 0.08f : 0.03f,
                };
                AssetDatabase.CreateAsset(scanned, $"{MaterialDir}/{name}.terrainlayer");
                return scanned;
            }
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
                "TL_Cobble" => GroundTextureGenerator.Cobble(s),
                _          => GroundTextureGenerator.Grass(s),
            };
            // CRITICAL: terrain layers use SmoothnessSource=AlbedoAlpha, so the diffuse ALPHA (not
            // the layer's m_Smoothness=0) drives gloss. Color32 albedo lands opaque (a=255) =>
            // smoothness ~1 => the bare dirt/road/approach terrain rendered as a WET MIRROR (Jon:
            // "roads too shiny, like the water bed"). Grass hid it under tuft billboards; roads
            // didn't. Zero the alpha so the ground is genuinely matte.
            for (int i = 0; i < px.Length; i++) px[i].a = 0;
            AssetDatabase.DeleteAsset($"{MaterialDir}/{name}_Tex.asset");
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            tex.SetPixels32(px);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, $"{MaterialDir}/{name}_Tex.asset");

            AssetDatabase.DeleteAsset($"{MaterialDir}/{name}.terrainlayer");
            // Bigger tile for dirt/cobble roads so the texture doesn't visibly REPEAT as squares
            // underfoot (Jon: "repeat squares"); grass/sand/etc stay finer for close detail.
            float tile = (name == "TL_Dirt" || name == "TL_Cobble") ? 11f : 6f;
            var layer = new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize = new Vector2(tile, tile),
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
            Color? tuftDark = null, Color? tuftLight = null, Color? matTint = null, bool useGrassTex = true)
        {
            // Baked vertex colours + material tint define the look. Terrain MESH details ignore
            // the DetailPrototype healthy/dry colours, so a charred variant needs its OWN prefab
            // with dark vertex colours (not just a dark tint on the shared green one).
            // MUTED OLIVE/SAGE. The blade TEXTURE is now near-neutral grey (grass_tuft_desat.png), so
            // these vertex colours actually define the hue (the old grass_tuft_alpha.png was itself a
            // saturated green (55,96,33) that NO tint could desaturate -- multiply only darkens). R kept
            // close to G (desaturated) + a touch warm = a natural dry-olive that sits with the photoscans.
            // Muted NATURAL olive-green (alive, not neon, not dead-khaki). G moderately above R (green
            // reads) but desaturated so it sits with the photoscans. Coherent with the terrain grass splat
            // below (both muted olive) so it isn't "green ground with dead grass on top".
            Color dark = tuftDark ?? new Color(0.20f, 0.27f, 0.14f);
            Color light = tuftLight ?? new Color(0.33f, 0.45f, 0.24f);   // green + alive but LOWER value/sat so it doesn't glow neon; matches the splat below
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
            var grassTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Ground/grass_tuft_desat.png");
            Color tint = matTint ?? new Color(0.42f, 0.62f, 0.28f);
            Material mat;
            if (useGrassTex && grassTex != null)
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
            else mat = Mat($"{id}Mat", matTint ?? new Color(0.3f, 0.5f, 0.2f), smoothness: 0.05f);

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

        /// <summary>One flat MeshCollider covering exactly the ground tiles that exist (walkable top
        /// at y=0). Replaces ~1800 per-tile BoxColliders. The lake/city/river carve stays a hole (those
        /// tiles were never added), so no fall-through and no walk-on-water. Built before the NavMesh
        /// bake, so pathing collects it like the old tiles.</summary>
        private static void BuildMergedGroundCollider(Transform parent, System.Collections.Generic.List<Vector2> centers, float step, int groundLayer)
        {
            if (centers.Count == 0) return;
            var verts = new System.Collections.Generic.List<Vector3>(centers.Count * 4);
            var tris = new System.Collections.Generic.List<int>(centers.Count * 6);
            float h = step * 0.5f;
            foreach (var c in centers)
            {
                int b = verts.Count;
                verts.Add(new Vector3(c.x - h, 0f, c.y - h));
                verts.Add(new Vector3(c.x + h, 0f, c.y - h));
                verts.Add(new Vector3(c.x + h, 0f, c.y + h));
                verts.Add(new Vector3(c.x - h, 0f, c.y + h));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var mesh = new Mesh { name = "GroundCollisionMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("GroundCollider");
            go.transform.SetParent(parent);
            go.isStatic = true;
            if (groundLayer >= 0) go.layer = groundLayer;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
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
            // Collect each ground tile's footprint; collision becomes ONE merged flat MeshCollider
            // instead of ~1800 per-tile BoxColliders (golden rule: reuse, don't multiply).
            var tileCenters = new System.Collections.Generic.List<Vector2>();

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
                    var tcol = tile.GetComponent<Collider>();
                    if (tcol != null) Object.DestroyImmediate(tcol);   // one merged collider instead
                    tileCenters.Add(new Vector2(cx, cz));
                }
            }

            BuildMergedGroundCollider(container, tileCenters, step, groundLayer);

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
            // Expanded-world path: region ecology is authored by biome and recorded into the same
            // chunked catalog as trees. This keeps tens of thousands of small props out of the
            // hierarchy outside the active 140 m envelope. The legacy compact-ring scatter below
            // remains only as rollback documentation.
            if (_worldDef != null && _worldDef.version >= 2)
            {
                bool Clear(float x, float z)
                {
                    if (InCityFootprint(x, z) || InApproachFootprint(x, z) || InGlade(x, z)) return false;
                    if (InLakeWater(x, z) || InAnyRiver(x, z) || DistToCityCanal(x, z) < 12f) return false;
                    return DistanceToAuthoredRoutes(x, z, out float half) >= half + 3f;
                }

                foreach (var region in _worldDef.regions)
                {
                    if (region.center == null || region.center.Length < 2) continue;
                    bool forest = region.kind == "forest";
                    bool transition = region.id == "ashen_foothills";
                    if (!forest && !transition) continue;

                    float area = Mathf.PI * region.radiusX * region.radiusZ;
                    int target = Mathf.Clamp(Mathf.RoundToInt(area / (forest ? 230f : 420f)), 120, 1900);
                    for (int attempt = 0, placed = 0; attempt < target * 12 && placed < target; attempt++)
                    {
                        float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                        float dist = Mathf.Sqrt((float)rng.NextDouble());
                        float x = region.center[0] + Mathf.Cos(ang) * dist * region.radiusX;
                        float z = region.center[1] + Mathf.Sin(ang) * dist * region.radiusZ;
                        if (!Clear(x, z)) continue;

                        double roll = rng.NextDouble();
                        string asset;
                        float height, footprint;
                        if (roll < (transition ? 0.24 : 0.10))
                        {
                            asset = rng.NextDouble() < 0.55 ? "namaqualand_boulder_02" : "boulder_01";
                            height = 0.8f + (float)rng.NextDouble() * 2.5f;
                            footprint = Mathf.Max(0.7f, height * 0.42f);
                        }
                        else if (!transition && roll > 0.84)
                        {
                            asset = "shrub_02";
                            height = 0.7f + (float)rng.NextDouble() * 0.8f;
                            footprint = 0f;
                        }
                        else
                        {
                            asset = transition && z < -670f ? "shrub_03" : "fern_02";
                            height = 0.45f + (float)rng.NextDouble() * (region.id == "south_woods" ? 1.0f : 0.65f);
                            footprint = 0f;
                        }
                        if (TryScatter(asset, x, z, (float)rng.NextDouble() * 360f, height, footprint,
                                zone => zone == PlaceZone.Open || zone == PlaceZone.Camp)) placed++;
                    }
                }

                // Shore ecology is its own narrow habitat band, never a generic forest sprinkle.
                for (int i = 0; i < 900; i++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float edge = LakeEdge(LakeCenter.x + dir.x * LakeHalf, LakeCenter.z + dir.y * LakeHalf);
                    float r = edge + 1.5f + (float)rng.NextDouble() * 11f;
                    float x = LakeCenter.x + dir.x * r, z = LakeCenter.z + dir.y * r;
                    if (!Clear(x, z)) continue;
                    TryScatter(rng.NextDouble() < 0.78 ? "fern_02" : "shrub_02", x, z,
                        (float)rng.NextDouble() * 360f, 0.45f + (float)rng.NextDouble() * 0.55f, 0f,
                        zone => zone == PlaceZone.Open);
                }
                return;
            }

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
            // BIOME classifiers so scatter is APPROPRIATE, not random (Jon: water-plants by the
            // lake, dense dark-forest undergrowth, meadow flowers + tall grass, burnt scrub south).
            System.Func<float, float, bool> lush = (x, z) => SootAmount(x, z) < 0.4f;
            System.Func<float, float, bool> meadow = (x, z) => SootAmount(x, z) < 0.15f
                && Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) > LakeEdge(x, z) + 20f;
            // Dark forest: the lake->city corridor (Jon: "dark forest, this is where it should be dense").
            System.Func<float, float, bool> darkForest = (x, z) => Mathf.Abs(x) < 95f && z > 55f && z < 250f
                && Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) > LakeEdge(x, z) + 8f;

            // Slight per-instance tint jitter so beds don't read as one flat clone.
            void Tint(GameObject go, Color baseCol, float amt)
            {
                if (go == null || IsKitProp(go)) return;   // keep the real Poly Haven texture/alpha
                float j = (float)(rng.NextDouble() - 0.5) * amt;
                var c = new Color(Mathf.Clamp01(baseCol.r + j * 0.5f), Mathf.Clamp01(baseCol.g + j), Mathf.Clamp01(baseCol.b + j * 0.4f));
                var m = Mat($"Fol_{Mathf.RoundToInt(c.r * 12)}_{Mathf.RoundToInt(c.g * 12)}_{Mathf.RoundToInt(c.b * 12)}", c, smoothness: 0.03f);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = m;
            }

            // Rock outcrops - VARIED meshes (4 kinds) + full tilt so they're not clones. Earth-toned
            // so field boulders sit in the grass instead of popping as bright white granite.
            Clump(120, 4, 14f, null, (x, z) =>
            {
                string rock = new[] { "namaqualand_boulder_02", "boulder_01", "boulder_01", "namaqualand_boulder_02" }[rng.Next(4)];
                float rh = 1.2f + (float)rng.NextDouble() * 3.6f;
                float rr = Mathf.Clamp(rh * 0.45f, 0.8f, 2.4f);   // bigger boulder reserves more ground
                var g = PlaceSolid(container, rock, x, z, (float)rng.NextDouble() * 360f, rh, rr, groundLayer, PropCollision.FitBox);
                if (g != null)
                {
                    g.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 24f, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 24f) * g.transform.rotation;
                    EarthTint(g);
                }
            });
            // (Removed the white PebblePatch "gravel clumps" - they read as ugly white hat-patches
            // on the dirt; damp variation is now baked into the dirt texture + road mud decals.)
            // General lush undergrowth (ferns + bushes), tinted for variation. Skip the open
            // meadows so they read as flower fields, not a wall of ferns.
            System.Func<float, float, bool> lushNotMeadow = (x, z) => lush(x, z) && !meadow(x, z);
            Clump(95, 5, 15f, lushNotMeadow, (x, z) =>
            {
                // Jon: shrubs read as filler/infestation -- keep undergrowth fern-dominant + sparser,
                // shrubs only occasionally. (Intentional shrubs live near the water, below.)
                string f = rng.NextDouble() < 0.88 ? "fern_02" : "shrub_01";
                var g = PlaceMeshyProp(container, f, new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f,
                    0.5f + (float)rng.NextDouble() * 0.6f, groundLayer, PropCollision.None);
                Tint(g, PropFallbackColor(f), 0.12f);
            });
            // DARK FOREST: dark ferns + the occasional bush + mossy boulders in the lake->city woods.
            Clump(90, 7, 11f, darkForest, (x, z) =>
            {
                double roll = rng.NextDouble();
                string f = roll < 0.72 ? "fern_02" : roll < 0.86 ? "shrub_01" : "boulder_01";
                var g = PlaceMeshyProp(container, f, new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f,
                    0.5f + (float)rng.NextDouble() * 0.7f, groundLayer, f == "boulder_01" ? PropCollision.FitBox : PropCollision.None);
                if (f != "boulder_01") Tint(g, new Color(0.10f, 0.20f, 0.10f), 0.08f);   // deep, shadowed green
            });
            // MEADOW: colourful wildflower beds + tall-grass tussocks in the open sunlit fields
            // (dense enough that open fields read as living meadows, not bare grass).
            Clump(340, 10, 13f, meadow, (x, z) =>
            {
                if (rng.NextDouble() < 0.6)
                {
                    var g = PlaceMeshyProp(container, "flower_gazania", new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, 0.4f + (float)rng.NextDouble() * 0.5f, groundLayer, PropCollision.None);
                    // Wildflowers span the palette: lilac / gold / white / rose, not one green.
                    Color[] palette = { new Color(0.62f, 0.40f, 0.66f), new Color(0.80f, 0.72f, 0.28f), new Color(0.86f, 0.86f, 0.80f), new Color(0.72f, 0.34f, 0.40f) };
                    Tint(g, palette[rng.Next(palette.Length)], 0.14f);
                }
                else   // a taller grass tussock (a fern reads as a grassy tuft when squashed wide)
                {
                    var g = PlaceMeshyProp(container, "fern_02", new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f,
                        0.45f + (float)rng.NextDouble() * 0.4f, groundLayer, PropCollision.None);
                    Tint(g, new Color(0.24f, 0.40f, 0.16f), 0.10f);
                }
            });

            // Reeds ringing the organic lake shore + river mouths - SUNK slightly so the solid base
            // disc buries in the bed (Jon: "circle above ground"), tinted for variation, shore-biased.
            for (int i = 0; i < 460; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                float le = LakeEdge(LakeCenter.x + dir.x * LakeHalf, LakeCenter.z + dir.y * LakeHalf);
                float r = le + 0.5f + (float)rng.NextDouble() * 7f;   // just OUTSIDE the waterline, out onto the shore
                float x = LakeCenter.x + dir.x * r, z = LakeCenter.z + dir.y * r;
                if (InCityFootprint(x, z) || InLakeWater(x, z)) continue;   // never in the water
                var g = PlaceMeshyProp(container, "fern_02", new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, 0.35f + (float)rng.NextDouble() * 0.4f, groundLayer, PropCollision.None);
                if (g != null) { g.transform.position += Vector3.down * 0.12f; Tint(g, new Color(0.34f, 0.42f, 0.16f), 0.12f); }
            }
        }

        /// <summary>Dress the dirt roads with 3D DEPTH (Jon: roads are "flat minecraft png") - gravel
        /// pebbles across the bed, worn wheel ruts, and rocks/boulders lining the shoulders, along
        /// the continuous ember-approach -> city -> lakeside road.</summary>
        private static void ScatterRoadDetail(Transform container, int groundLayer)
        {
            if (_worldDef != null && _worldDef.version >= 2)
            {
                foreach (var route in _worldDef.routes)
                {
                    if (route.points == null) continue;
                    for (int p = 0; p < route.points.Length - 1; p++)
                    {
                        Vector2 a = new Vector2(route.points[p].x, route.points[p].z);
                        Vector2 b = new Vector2(route.points[p + 1].x, route.points[p + 1].z);
                        float length = Vector2.Distance(a, b);
                        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 5.5f));
                        Vector2 tangent = (b - a).normalized;
                        Vector2 normal = new Vector2(-tangent.y, tangent.x);
                        float yaw = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg;
                        for (int i = 0; i <= steps; i++)
                        {
                            Vector2 c = Vector2.Lerp(a, b, i / (float)steps);
                            foreach (float side in new[] { -1f, 1f })
                            {
                                if (rng.NextDouble() > 0.36) continue;
                                Vector2 q = c + normal * side * (route.width * 0.5f + 1.5f + (float)rng.NextDouble() * 3f);
                                if (InCityFootprint(q.x, q.y) || InLakeWater(q.x, q.y) || InAnyRiver(q.x, q.y)) continue;
                                string asset = rng.NextDouble() < 0.66 ? "namaqualand_boulder_02" : "shrub_03";
                                float h = asset == "shrub_03" ? 0.55f + (float)rng.NextDouble() * 0.7f : 0.6f + (float)rng.NextDouble() * 1.4f;
                                TryScatter(asset, q.x, q.y, yaw + (float)(rng.NextDouble() - 0.5) * 90f,
                                    h, asset == "shrub_03" ? 0.25f : h * 0.35f,
                                    zone => zone == PlaceZone.Open);
                            }
                        }
                    }
                }
                return;
            }

            var rutMat = Mat("RoadRut", new Color(0.22f, 0.16f, 0.11f));
            var mudMat = Mat("RoadMud", new Color(0.20f, 0.145f, 0.095f), smoothness: 0.14f);   // damp wet earth (subtle)
            bool haveShrub = HasMesh("shrub_03");
            for (float z = -312f; z <= 212f; z += 2.2f)
            {
                if (InCityFootprint(0f, z)) continue;   // city lays cobbles
                // Faint parallel wheel ruts (thin darker strips worn into the road).
                if (rng.NextDouble() < 0.5)
                    foreach (float s in new[] { -1f, 1f })
                    {
                        var rut = Cube($"Rut{z:F0}_{s}", new Vector3(s * 2.4f, 0.015f, z),
                            new Vector3(1.1f, 0.03f, 2.3f), rutMat, container);
                        Object.DestroyImmediate(rut.GetComponent<BoxCollider>());
                    }
                // DAMP / MUD patches worn into the road bed (REPLACES the white pebble "hat patch"
                // clumps): flat, dark, irregular, ground-hugging so they read as wet earth/erosion.
                if (rng.NextDouble() < 0.18)
                {
                    float mw = 2f + (float)rng.NextDouble() * 2.5f;
                    var mud = Cube($"Mud{z:F0}", new Vector3((float)(rng.NextDouble() * 2 - 1) * 5.5f, 0.02f, z + (float)(rng.NextDouble() - 0.5) * 2f),
                        new Vector3(mw, 0.03f, mw * (0.55f + (float)rng.NextDouble() * 0.7f)), mudMat, container);
                    mud.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
                    Object.DestroyImmediate(mud.GetComponent<BoxCollider>());
                }
                // Roadside dry shrub (real mesh when present) tucked at the road edge.
                if (haveShrub && rng.NextDouble() < 0.16)
                    PlaceMeshyProp(container, "shrub_03",
                        new Vector3((rng.NextDouble() < 0.5 ? -1f : 1f) * (7f + (float)rng.NextDouble() * 3f), 0f, z),
                        (float)rng.NextDouble() * 360f, 0.6f + (float)rng.NextDouble() * 0.6f, groundLayer, PropCollision.None);
                // Occasional EARTH-TONED stone embedded in the road (warm-dark, not white gravel).
                if (rng.NextDouble() < 0.10)
                    EarthTint(PlaceMeshyProp(container, rng.NextDouble() < 0.5 ? "boulder_01" : "namaqualand_boulder_02",
                        new Vector3((float)(rng.NextDouble() * 2 - 1) * 5f, 0f, z), (float)rng.NextDouble() * 360f,
                        0.45f + (float)rng.NextDouble() * 0.6f, groundLayer, PropCollision.FitBox));
                // Earth-toned rocks lining the shoulders.
                foreach (float s in new[] { -1f, 1f })
                    if (rng.NextDouble() < 0.30)
                        EarthTint(PlaceMeshyProp(container, new[] { "boulder_01", "namaqualand_boulder_02", "boulder_01" }[rng.Next(3)],
                            new Vector3(s * (7.5f + (float)rng.NextDouble() * 4f), 0f, z), (float)rng.NextDouble() * 360f,
                            1f + (float)rng.NextDouble() * 1.8f, groundLayer, PropCollision.FitBox));
            }
        }

        /// <summary>Wooden rail fences lining the road shoulders near the city + penning the camps,
        /// so the settled areas read as kept and lived-in (ref: village road fencing). Sections run
        /// end-to-end; yaw orients the wide face ALONG the run.</summary>
        private static void BuildRoadsideFences(Transform root, int groundLayer)
        {
            if (!HasMesh("Fence")) return;
            var container = Child(root, "RoadsideFences");
            const float sec = 3.6f;   // fence section length
            // Run fence sections between two points, wide face along the run.
            void FenceRun(Vector3 a, Vector3 b)
            {
                Vector3 d = b - a; float len = d.magnitude;
                if (len < sec) return;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 90f;   // wide face along the run
                int n = Mathf.RoundToInt(len / sec);
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = a + d * ((i + 0.5f) / n);
                    if (InCityFootprint(p.x, p.z) || InLakeWater(p.x, p.z) || InAnyRiver(p.x, p.z)) continue;
                    PlaceMeshyProp(container, "Fence", new Vector3(p.x, 0f, p.z), yaw, sec, groundLayer, PropCollision.FitBox);
                }
            }
            if (_worldDef != null && _worldDef.version >= 2)
            {
                foreach (var route in _worldDef.routes)
                {
                    // Fencing is a sign of settlement, so it only follows the first ~100 m beyond
                    // each city gate; wilderness routes remain visually organic.
                    if (route.points == null || route.points.Length < 2) continue;
                    int segmentLimit = Mathf.Min(2, route.points.Length - 1);
                    for (int i = 0; i < segmentLimit; i++)
                    {
                        Vector2 a2 = new Vector2(route.points[i].x, route.points[i].z);
                        Vector2 b2 = new Vector2(route.points[i + 1].x, route.points[i + 1].z);
                        Vector2 n = new Vector2(-(b2 - a2).normalized.y, (b2 - a2).normalized.x);
                        float shoulder = route.width * 0.5f + 2.5f;
                        FenceRun(new Vector3(a2.x + n.x * shoulder, 0f, a2.y + n.y * shoulder),
                            new Vector3(b2.x + n.x * shoulder, 0f, b2.y + n.y * shoulder));
                        FenceRun(new Vector3(a2.x - n.x * shoulder, 0f, a2.y - n.y * shoulder),
                            new Vector3(b2.x - n.x * shoulder, 0f, b2.y - n.y * shoulder));
                    }
                }
                return;
            }
            // Both shoulders of the N-S road, from the north gate up toward the lakeside.
            FenceRun(new Vector3(-9.5f, 0f, CityHalf + 5f), new Vector3(-9.5f, 0f, 172f));
            FenceRun(new Vector3(9.5f, 0f, CityHalf + 5f), new Vector3(9.5f, 0f, 172f));
        }

        private static void ScatterForestTrees(Transform container, int groundLayer)
        {
            // DATA-DRIVEN: trees are recorded into the catalog only where the map says OPEN ground
            // with no overlap. Species is chosen by BIOME - alive trees in lush/meadow/dark-forest,
            // DeadTree ONLY in the volcanic soot band (Jon: no dead trees in the lush forest). The
            // trunk-footprint radius keeps trees from growing into each other.
            // Curated Poly Haven canopy trees (real photoscans, alpha foliage, decimated LOD prefabs).
            // Fewer + BIGGER + wider-spaced than the old dense clone field -- tall trees with real depth,
            // like the New World / BDO reference forests (see poly-haven-forest-kit memory).
            string[] aliveKit = { "island_tree_01", "island_tree_02", "island_tree_03", "island_tree_01", "tree_small_02" };
            string[] deadKit = { "dead_tree_trunk", "dead_tree_trunk_02" };
            bool NearRoute(float x, float z, float extra)
            {
                var p = new Vector2(x, z);
                foreach (var route in _worldDef.routes)
                    for (int i = 0; i < route.points.Length - 1; i++)
                        if (DistToSegment(p, new Vector2(route.points[i].x, route.points[i].z),
                            new Vector2(route.points[i + 1].x, route.points[i + 1].z)) < route.width * 0.5f + extra)
                            return true;
                return false;
            }
            int planted = 0, alive = 0;
            foreach (var region in _worldDef.regions)
            {
                bool forest = region.kind == "forest" || region.id == "ashen_foothills";
                if (!forest || region.center == null || region.center.Length < 2) continue;
                int target = Mathf.Clamp(Mathf.RoundToInt(Mathf.PI * region.radiusX * region.radiusZ / 250f), 220, 2600);
                int accepted = 0;
                for (int attempt = 0; attempt < target * 28 && accepted < target; attempt++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float d = Mathf.Sqrt((float)rng.NextDouble());
                    float x = region.center[0] + Mathf.Cos(a) * d * region.radiusX;
                    float z = region.center[1] + Mathf.Sin(a) * d * region.radiusZ;
                    float lakeDist = Vector2.Distance(new Vector2(x, z), new Vector2(LakeCenter.x, LakeCenter.z)) - LakeEdge(x, z);
                    if (lakeDist < 10f || InLakeWater(x, z) || InAnyRiver(x, z) || NearRoute(x, z, 14f)) continue;
                    float radius = 4.5f + (float)rng.NextDouble() * 3.5f;
                    float ashT = region.id == "ashen_foothills" ? Mathf.InverseLerp(-520f, VolcanicVisualNorthZ, z) : 0f;
                    bool dead = region.id == "ashen_foothills" && rng.NextDouble() < Mathf.Lerp(0.15f, 0.82f, ashT);
                    string species = dead ? deadKit[planted % deadKit.Length] : aliveKit[alive % aliveKit.Length];
                    float h = region.id == "south_woods" ? 12f + (float)rng.NextDouble() * 7f
                        : 10f + (float)rng.NextDouble() * 7f;
                    if (!TryScatter(species, x, z, (float)rng.NextDouble() * 360f, h, radius,
                            zn => zn == PlaceZone.Open || zn == PlaceZone.Camp)) continue;
                    accepted++; planted++; if (!dead) alive++;
                }
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
                // Not on the roads/approach: dark flat decals read as ugly slabs on bare dirt (they
                // only blend into grass). The ember corridor has its own ash treatment.
                if (InApproachFootprint(x, z) || (Mathf.Abs(x) < 11f && z > -66f && z < 214f)) continue;
                float s = 2.5f + (float)rng.NextDouble() * 4f;
                var patch = Cube($"Soot_{attempt}", new Vector3(x, 0.02f, z),
                    new Vector3(s, 0.04f, s * (0.7f + (float)rng.NextDouble() * 0.6f)), patchMat, container);
                patch.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                patch.isStatic = true;
                Object.DestroyImmediate(patch.GetComponent<BoxCollider>());
            }
        }

        /// <summary>Cheap cube-pine silhouette used when a tree fbx hasn't downloaded yet.</summary>
        // No more cube-canopy placeholders: place a REAL curated kit tree (see CLAUDE.md HARD RULE).
        // Kept the signature so the dark-woods / glade callers don't change; trunkMat/leafMat unused.
        private static void PlaceCubePine(Transform container, int id, float x, float z, float h,
            Material trunkMat, Material leafMat, int groundLayer)
        {
            // Vary the species by id so groves aren't clones; clamp handled inside PlaceKitTree.
            string[] kit = { "island_tree_02", "island_tree_01", "island_tree_03", "tree_small_02" };
            PlaceMeshyProp(container, kit[id % kit.Length], new Vector3(x, 0f, z),
                (float)rng.NextDouble() * 360f, Mathf.Max(7f, h), groundLayer, PropCollision.Trunk);
        }

        // ==================== CITY ====================

        private static void BuildEmberfallCity(Transform root, int groundLayer)
        {
            var container = Child(root, "EmberfallCity");
            var cityDef = EmberfallCityDefinition.Load();

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
            var cityVerts = new List<Vector3>();
            var cityUvs = new List<Vector2>();
            var cityTris = new List<int>[5];
            for (int i = 0; i < cityTris.Length; i++) cityTris[i] = new List<int>();
            bool NearAuthoredStreet(float x, float z)
            {
                var p = new Vector2(x, z);
                foreach (var street in cityDef.streets)
                    for (int i = 0; i < street.points.Length - 1; i++)
                        if (DistToSegment(p, new Vector2(street.points[i].x, street.points[i].z),
                            new Vector2(street.points[i + 1].x, street.points[i + 1].z)) < street.width * 0.5f)
                            return true;
                return false;
            }
            for (float x = -CityHalf; x < CityHalf; x += tile)
                for (float z = -CityHalf; z < CityHalf; z += tile)
                {
                    float cx = x + tile / 2f, cz = z + tile / 2f;
                    // Leave the canal channel OPEN (no cobble lid over the water — the "river runs
                    // under town" bug); the stone canal banks are built separately.
                    if (DistToCityCanal(cx, cz) < 8f) continue;
                    bool street = NearAuthoredStreet(cx, cz);
                    int sub = street ? 4 : rng.Next(cobble.Length);
                    int vi = cityVerts.Count;
                    cityVerts.Add(new Vector3(x, 0.055f, z));
                    cityVerts.Add(new Vector3(x, 0.055f, z + tile));
                    cityVerts.Add(new Vector3(x + tile, 0.055f, z));
                    cityVerts.Add(new Vector3(x + tile, 0.055f, z + tile));
                    cityUvs.Add(new Vector2(x / tile, z / tile));
                    cityUvs.Add(new Vector2(x / tile, z / tile + 1));
                    cityUvs.Add(new Vector2(x / tile + 1, z / tile));
                    cityUvs.Add(new Vector2(x / tile + 1, z / tile + 1));
                    cityTris[sub].AddRange(new[] { vi, vi + 1, vi + 2, vi + 2, vi + 1, vi + 3 });
                }
            var cityMesh = new Mesh { name = "AuthoredCityGround", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, subMeshCount = 5 };
            cityMesh.SetVertices(cityVerts); cityMesh.SetUVs(0, cityUvs);
            for (int i = 0; i < cityTris.Length; i++) cityMesh.SetTriangles(cityTris[i], i);
            cityMesh.RecalculateNormals(); cityMesh.RecalculateBounds();
            var cityGround = new GameObject("AuthoredCityGround"); cityGround.transform.SetParent(container);
            cityGround.AddComponent<MeshFilter>().sharedMesh = cityMesh;
            cityGround.AddComponent<MeshRenderer>().sharedMaterials = new[] { cobble[0], cobble[1], cobble[2], cobble[3], streetMat };
            cityGround.AddComponent<MeshCollider>().sharedMesh = cityMesh;
            cityGround.isStatic = true; if (groundLayer >= 0) cityGround.layer = groundLayer;

            BuildCityWalls(container, groundLayer);
            BuildCityCanal(container, groundLayer);

            // ---- Ember Fountain (plaza centre) ----
            BuildEmberFountain(container, new Vector3(0f, 0f, 0f), groundLayer);

            // ---- Market Row (NE): open-air stall facades (not enterable) ----
            foreach (var (pos, yaw) in new[]
            {
                (new Vector3(30f, 0f, 34f), 215f), (new Vector3(22f, 0f, 30f), 190f),
                (new Vector3(16f, 0f, 40f), 170f),
            })
                PlaceCityBuilding(container, "MarketStall", pos, yaw, 3.4f, groundLayer);

            // Exact district/building intent comes from city.json. Generated interiors remain a
            // compiler concern, but their identity, parcel, role, dimensions and entrance heading
            // are now inspectable and stable rather than buried in this method.
            foreach (var b in cityDef.buildings)
            {
                float yaw = Mathf.Repeat(b.yaw, 360f);
                int doorSide = yaw < 45f || yaw >= 315f ? 0 : yaw < 135f ? 2 : yaw < 225f ? 1 : 3;
                var pos = new Vector3(b.position[0], 0f, b.position[1]);
                if (b.role == "inn")
                    BuildTownBuilding(container, b.id, pos, b.width, b.depth, b.stories, doorSide, b.role, groundLayer,
                        "Hobb", new[] { "cooked_fish", "cooked_trout", "cooked_bass", "cooked_carp" });
                else
                    BuildTownBuilding(container, b.id, pos, b.width, b.depth, b.stories, doorSide, b.role, groundLayer);
            }

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

            // STREET LAMPS everywhere (Jon): two variations (a glowing globe lamp + an iron
            // hanging-lantern post), each a REAL point light, paced along both gate streets + a
            // ring around the plaza + the perimeter walk. All recorded in the JSON mapper.
            int lampN = 0;
            void Lamp(Vector3 p)
            {
                if (DistToCityCanal(p.x, p.z) < 6f) return;   // not in the canal
                if (Mathf.Abs(p.x) < 6f && Mathf.Abs(p.z) < 6f) return;   // not on the fountain
                BuildStreetLamp(container, p, lampN++ % 2, groundLayer);
            }
            for (float d = -46f; d <= 46f; d += 11f)
            {
                Lamp(new Vector3(3.2f, 0f, d)); Lamp(new Vector3(-3.2f, 0f, d));   // N-S street both sides
                Lamp(new Vector3(d, 0f, 3.2f)); Lamp(new Vector3(d, 0f, -3.2f));   // E-W street both sides
            }
            foreach (var p in new[]   // plaza ring + inner corners
            {
                new Vector3(10f, 0f, 10f), new Vector3(-10f, 0f, 10f), new Vector3(10f, 0f, -10f), new Vector3(-10f, 0f, -10f),
                new Vector3(24f, 0f, 24f), new Vector3(-24f, 0f, 24f), new Vector3(24f, 0f, -24f), new Vector3(-24f, 0f, -24f),
            })
                Lamp(p);

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
                // Real potted shrub sitting on the planter (was a cube). Elevated y skips the terrain snap.
                PlaceMeshyProp(container, "shrub_01", new Vector3(pos.x, 0.7f, pos.z),
                    (float)rng.NextDouble() * 360f, 0.9f, groundLayer, PropCollision.None);
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
            // (The Ember Rest inn is now the 3-story enterable BuildTownBuilding above; the old
            // single-room BuildEmberRestTavern is retired.)
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
        /// A generalized ENTERABLE town building: a block shell (solid ground floor + upper floors
        /// with a stairwell hole, walls with a door + emissive windows, pitched roof) you can walk
        /// into, ramp-stairs between stories, a furnished interior (real Meshy furniture when it
        /// exists, cube fallbacks otherwise) themed by type, NPCs with intention inside, and a
        /// catalog record. doorSide: 0=-Z 1=+Z 2=-X 3=+X (the wall facing the street).
        /// </summary>
        private static void BuildTownBuilding(Transform parent, string key, Vector3 c, float halfX, float halfZ,
            int stories, int doorSide, string type, int groundLayer, string npcName = null, string[] npcStock = null)
        {
            var container = Child(parent, key);
            float canalD = DistToCityCanal(c.x, c.z);
            if (canalD < Mathf.Max(halfX, halfZ) + 4f) Debug.Log($"BUILDING {key} @({c.x:F0},{c.z:F0}) too close to canal (dist {canalD:F0}, needs {Mathf.Max(halfX, halfZ) + 4f:F0})");
            const float wallH = 3.4f, thick = 0.4f;
            var timber = Mat("BldgTimber", new Color(0.34f, 0.24f, 0.15f), smoothness: 0.08f);
            // Real MATERIAL on the blocks (Jon: "brick/stone slabs on top of blocks"): brick or
            // stone walls, tile or thatch roofs, plank floors - procedural textures on the cubes.
            Material TexMat(string n, System.Func<Color32[]> gen, float tile)
            {
                var m = Mat(n, Color.white, smoothness: 0.04f);
                var tex = GetGeneratedTexture($"{n}_Tex", gen);
                if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(tile, tile)); }
                if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", new Vector2(tile, tile)); }
                return m;
            }
            int variant = (int)(Mathf.Abs(c.x) + Mathf.Abs(c.z) * 1.7f);
            bool stoneWall = variant % 3 == 0;
            bool thatchRoof = (variant / 3) % 2 == 0;
            var wallMat = stoneWall ? TexMat("BldgStone", () => GroundTextureGenerator.StoneWall(256), 3f)
                                    : TexMat("BldgBrick", () => GroundTextureGenerator.Brick(256), 3f);
            var roofMat = thatchRoof ? TexMat("BldgThatch", () => GroundTextureGenerator.Thatch(256), 2.5f)
                                     : TexMat("BldgTile", () => GroundTextureGenerator.RoofTile(256), 2.5f);
            var floorMat = TexMat("BldgPlank", () => GroundTextureGenerator.Plank(256), 3f);
            // Daytime GLASS window (dark blue-grey, barely-emissive) - the bright emissive panes
            // read as floating light-cards against the dark walls (Jon). Glass, not a lamp.
            var windowMat = Mat("BldgWindow", new Color(0.22f, 0.27f, 0.33f), smoothness: 0.55f, emission: new Color(0.5f, 0.42f, 0.28f) * 0.08f);

            GameObject Solid(string n, Vector3 local, Vector3 size, Material m, bool collide = true)
            {
                var o = Cube(n, c + local, size, m, container);
                o.isStatic = true;
                var bc = o.GetComponent<BoxCollider>();
                if (!collide) { if (bc) Object.DestroyImmediate(bc); }
                else if (groundLayer >= 0) o.layer = groundLayer;
                return o;
            }

            float top = wallH * stories;
            bool multi = stories > 1;
            float swX0 = halfX - 3.0f, swDepth = 4.5f;   // stairwell in the +X,-Z corner

            // ---- Floors (ground solid; upper floors omit the stairwell hole) ----
            Solid("Floor0", new Vector3(0f, 0.06f, 0f), new Vector3(halfX * 2f, 0.12f, halfZ * 2f), floorMat);
            for (int s = 1; s < stories; s++)
            {
                float y = s * wallH;
                float leftW = swX0 - (-halfX);
                Solid($"Floor{s}A", new Vector3((-halfX + swX0) / 2f, y, 0f), new Vector3(leftW, 0.12f, halfZ * 2f), floorMat);
                float bDepth = halfZ - (-halfZ + swDepth);
                Solid($"Floor{s}B", new Vector3((swX0 + halfX) / 2f, y, ((-halfZ + swDepth) + halfZ) / 2f), new Vector3(halfX - swX0, 0.12f, bDepth), floorMat);
            }

            // ---- Walls per story (door gap on the ground floor's doorSide) + windows ----
            for (int s = 0; s < stories; s++)
            {
                float y0 = s * wallH, yc = y0 + wallH / 2f;
                for (int side = 0; side < 4; side++)
                {
                    bool isDoor = (s == 0 && side == doorSide);
                    bool spanX = side < 2;   // sides 0/1 (-Z/+Z) span X; 2/3 (-X/+X) span Z
                    float span = spanX ? halfX : halfZ;
                    Vector3 wc = side == 0 ? new Vector3(0, yc, -halfZ) : side == 1 ? new Vector3(0, yc, halfZ)
                              : side == 2 ? new Vector3(-halfX, yc, 0) : new Vector3(halfX, yc, 0);
                    Vector3 wsize = spanX ? new Vector3(halfX * 2f, wallH, thick) : new Vector3(thick, wallH, halfZ * 2f);
                    if (isDoor)
                    {
                        float dH = 1.3f, segW = (span * 2f - dH * 2f) / 2f;
                        float woff = thick / 2f + 0.05f;
                        if (spanX)
                        {
                            Solid($"W{s}_{side}L", new Vector3(-(dH + segW / 2f), yc, wc.z), new Vector3(segW, wallH, thick), wallMat);
                            Solid($"W{s}_{side}R", new Vector3(dH + segW / 2f, yc, wc.z), new Vector3(segW, wallH, thick), wallMat);
                            Solid($"Lin{s}_{side}", new Vector3(0, y0 + wallH - 0.5f, wc.z), new Vector3(dH * 2f + 0.6f, 1f, thick), timber);
                            foreach (float wx in new[] { -(dH + segW / 2f), dH + segW / 2f })   // windows flanking the door
                                Solid($"WinD{s}_{side}_{wx:F0}", new Vector3(wx, y0 + 2.1f, wc.z + Mathf.Sign(wc.z) * woff), new Vector3(1.2f, 1.1f, 0.1f), windowMat, collide: false);
                        }
                        else
                        {
                            Solid($"W{s}_{side}L", new Vector3(wc.x, yc, -(dH + segW / 2f)), new Vector3(thick, wallH, segW), wallMat);
                            Solid($"W{s}_{side}R", new Vector3(wc.x, yc, dH + segW / 2f), new Vector3(thick, wallH, segW), wallMat);
                            Solid($"Lin{s}_{side}", new Vector3(wc.x, y0 + wallH - 0.5f, 0), new Vector3(thick, 1f, dH * 2f + 0.6f), timber);
                            foreach (float wz in new[] { -(dH + segW / 2f), dH + segW / 2f })
                                Solid($"WinD{s}_{side}_{wz:F0}", new Vector3(wc.x + Mathf.Sign(wc.x) * woff, y0 + 2.1f, wz), new Vector3(0.1f, 1.1f, 1.2f), windowMat, collide: false);
                        }
                    }
                    else
                    {
                        Solid($"W{s}_{side}", wc, wsize, wallMat);
                        // glowing window(s) on the EXTERIOR face so the facade reads as inhabited.
                        float woff2 = thick / 2f + 0.06f;
                        Vector3 wp = wc + (spanX ? new Vector3(0, 0, Mathf.Sign(wc.z == 0 ? 1 : wc.z) * woff2)
                                                 : new Vector3(Mathf.Sign(wc.x == 0 ? 1 : wc.x) * woff2, 0, 0));
                        wp.y = y0 + 2.1f;
                        Vector3 winSize = spanX ? new Vector3(1.6f, 1.3f, 0.1f) : new Vector3(0.1f, 1.3f, 1.6f);
                        // wp is already a LOCAL coord (built from wc around 0); Solid adds c. Passing
                        // wp-c placed every non-door window at its local coord near the plaza -> the
                        // "gray tiles floating in the air over the city" (Jon). Pass wp local.
                        Solid($"Win{s}_{side}", wp, winSize, windowMat, collide: false);
                    }
                }
            }
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                Solid("Corner", new Vector3(sx * halfX, top / 2f, sz * halfZ), new Vector3(0.6f, top, 0.6f), timber);

            // ---- Ramp stairs through each stairwell hole ----
            if (multi)
                for (int s = 0; s < stories - 1; s++)
                {
                    float y0 = s * wallH;
                    var ramp = Solid($"Stair{s}", new Vector3(swX0 + (halfX - swX0) / 2f, y0 + wallH / 2f + 0.1f, -halfZ + swDepth / 2f),
                        new Vector3(halfX - swX0 - 0.3f, 0.3f, Mathf.Sqrt(wallH * wallH + swDepth * swDepth)), timber);
                    ramp.transform.rotation = Quaternion.Euler(Mathf.Atan2(wallH, swDepth) * Mathf.Rad2Deg, 0f, 0f);
                }

            // ---- Pitched roof ----
            foreach (var sx in new[] { -1f, 1f })
            {
                var slab = Cube($"Roof{(sx < 0 ? "W" : "E")}", c + new Vector3(sx * halfX * 0.5f, top + 1.3f, 0f),
                    new Vector3(halfX * 1.32f, 0.35f, halfZ * 2f + 1.2f), roofMat, container);
                slab.transform.rotation = Quaternion.Euler(0f, 0f, sx * -26f);
                slab.isStatic = true; if (groundLayer >= 0) slab.layer = groundLayer;
            }
            Solid("Ridge", new Vector3(0f, top + 2.3f, 0f), new Vector3(0.6f, 0.4f, halfZ * 2f + 1.4f), timber);

            // ---- Gable-end walls: fill the OPEN triangle between the flat wall top and the pitched
            // roof (Jon's "triangle on the sides of houses"). Stepped narrowing slabs = a solid
            // triangular gable at each Z end. ----
            float gableH = halfX * 0.49f;
            const int gSteps = 12;   // more, thinner steps => reads as a smooth triangle, not a blocky pyramid
            foreach (float zEnd in new[] { -halfZ, halfZ })
                for (int i = 0; i < gSteps; i++)
                {
                    float f = i / (float)gSteps;
                    float w = halfX * 2f * (1f - f) + 0.3f;
                    Solid($"Gable{(zEnd < 0 ? "S" : "N")}_{i}", new Vector3(0f, top + (i + 0.5f) * (gableH / gSteps), zEnd), new Vector3(w, gableH / gSteps + 0.06f, thick), wallMat);
                }

            // ---- Interior fill light per story (so rooms read cozy, not cave-dark) ----
            for (int s = 0; s < stories; s++)
            {
                var fl = new GameObject($"Fill{s}"); fl.transform.SetParent(container);
                fl.transform.position = c + new Vector3(0f, s * wallH + wallH * 0.75f, 0f);
                var L = fl.AddComponent<Light>();
                L.type = LightType.Point; L.color = new Color(1f, 0.87f, 0.64f);
                L.range = Mathf.Max(halfX, halfZ) * 2.6f; L.intensity = 1.75f; L.shadows = LightShadows.None;
            }

            // ---- Furnish + NPCs per story ----
            for (int s = 0; s < stories; s++)
                FurnishStory(container, type, s, c, halfX, halfZ, s * wallH, doorSide, groundLayer,
                    s == 0 ? npcName : null, s == 0 ? npcStock : null);

            // ---- Catalog + manifest: the building as a mapped structure (footprint + entrance) ----
            RecordDetail($"building_{type}", c.x, c.z, doorSide * 90f, top);
            _manifest?.AddBuilding($"building_{type}", new Vector3(c.x, 0f, c.z),
                new Vector3(halfX * 2f, top, halfZ * 2f), doorSide * 90f, 2f);
        }

        /// <summary>Furnish one story of a town building with themed Meshy furniture (cube fallback
        /// until the mesh exists) + intentful NPCs on the ground floor.</summary>
        private static void FurnishStory(Transform container, string type, int story, Vector3 c,
            float halfX, float halfZ, float floorY, int doorSide, int groundLayer, string npcName, string[] npcStock)
        {
            float fy = floorY + 0.13f;
            void Place(string mesh, float lx, float lz, float yaw, float h, Vector3 fbSize, Color fbCol, bool collide = false)
            {
                Vector3 wp = c + new Vector3(lx, fy, lz);
                // Rugs must lie FLAT; the mesh imports standing up like a tapestry, so use the flat
                // cube (a low patterned slab on the floor) instead.
                if (mesh != "Rug" && HasMesh(mesh))
                    PlaceMeshyProp(container, mesh, wp, yaw, h, groundLayer, collide ? PropCollision.FitBox : PropCollision.None);
                else
                {
                    var o = Cube($"{mesh}_fb", wp + Vector3.up * (fbSize.y * 0.5f), fbSize, Mat($"Furn_{mesh}", fbCol, 0.08f), container);
                    o.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    o.isStatic = true;
                    var bc = o.GetComponent<BoxCollider>();
                    if (!collide) { if (bc) Object.DestroyImmediate(bc); } else if (groundLayer >= 0) o.layer = groundLayer;
                }
                // (PlaceMeshyProp auto-records the mesh furniture into the manifest; cube fallbacks are
                // interior dressing that doesn't need cataloguing for conflict purposes.)
            }
            Color wood = new Color(0.35f, 0.24f, 0.14f), cloth = new Color(0.5f, 0.3f, 0.28f);
            bool ground = story == 0;
            // Keep the door approach (doorSide wall) clear; anchor furniture toward the far/back.
            float back = doorSide == 1 ? -(halfZ - 2f) : (halfZ - 2f);   // opposite the door in Z (most doors face -Z)

            if (type == "inn")
            {
                if (ground)
                {
                    Place("Fireplace", halfX - 1.4f, back, doorSide == 1 ? 0f : 180f, 2.6f, new Vector3(1.4f, 2f, 1.2f), new Color(0.32f, 0.3f, 0.3f), true);
                    Place("Table", 0f, 0f, 0f, 1.5f, new Vector3(1.6f, 0.9f, 1.6f), wood);
                    Place("Chair", 1.3f, 0f, 90f, 1.1f, new Vector3(0.5f, 1f, 0.5f), wood);
                    Place("Chair", -1.3f, 0f, 270f, 1.1f, new Vector3(0.5f, 1f, 0.5f), wood);
                    Place("Table", -halfX + 2.4f, -1.5f, 0f, 1.5f, new Vector3(1.6f, 0.9f, 1.6f), wood);
                    Place("Bench", -halfX + 2.4f, -3f, 0f, 1.6f, new Vector3(1.7f, 0.6f, 0.5f), wood);
                    Place("Rug", 0f, 1.5f, 0f, 3f, new Vector3(2.6f, 0.05f, 1.8f), new Color(0.5f, 0.14f, 0.12f));
                    if (npcName != null)
                        BuildOneVendor(container, groundLayer, $"Innkeeper {npcName}", npcName, c + new Vector3(0f, floorY, back + 1.2f), doorSide == 1 ? 0f : 180f,
                            npcStock ?? new[] { "cooked_fish", "cooked_trout", "cooked_bass" });
                    PlaceTownsfolk(container, c + new Vector3(1.3f, floorY, 0.9f), new Color(0.4f, 0.32f, 0.5f), groundLayer);
                }
                else
                {
                    Place("Bed", -halfX + 2f, back, 90f, 2.2f, new Vector3(1.3f, 0.7f, 2.2f), cloth, true);
                    Place("Bed", halfX - 4f, back, 90f, 2.2f, new Vector3(1.3f, 0.7f, 2.2f), cloth, true);
                    Place("Table", 0f, 0f, 0f, 1.2f, new Vector3(1.2f, 0.9f, 1.2f), wood);
                    Place("Rug", 0f, 0f, 0f, 2.5f, new Vector3(2.2f, 0.05f, 1.6f), new Color(0.4f, 0.2f, 0.14f));
                }
            }
            else if (type == "smithy")
            {
                if (ground)
                {
                    Place("Anvil", halfX - 2f, back, doorSide == 1 ? 0f : 180f, 1.2f, new Vector3(0.9f, 1f, 0.5f), new Color(0.2f, 0.2f, 0.22f), true);
                    Place("Fireplace", -halfX + 1.6f, back, 0f, 2.4f, new Vector3(1.4f, 2f, 1.2f), new Color(0.3f, 0.2f, 0.18f), true);
                    Place("WoodCrate", -halfX + 2f, 0f, 20f, 1.1f, new Vector3(1f, 1f, 1f), wood, true);
                    Place("MarketBarrel", halfX - 2f, 0f, 0f, 1.2f, new Vector3(1f, 1.2f, 1f), wood, true);
                    PlaceTownsfolk(container, c + new Vector3(halfX - 3.4f, floorY, back), new Color(0.32f, 0.28f, 0.26f), groundLayer);
                }
                else Place("Bed", -halfX + 2f, back, 90f, 2.2f, new Vector3(1.3f, 0.7f, 2.2f), cloth, true);
            }
            else if (type == "shop")
            {
                if (ground)
                {
                    Place("Table", 0f, back * 0.5f, doorSide < 2 ? 0f : 90f, 2.4f, new Vector3(2.6f, 1f, 0.9f), wood, true);   // counter
                    Place("Bookshelf", -halfX + 1.2f, back, 90f, 2.6f, new Vector3(0.6f, 2.4f, 1.8f), wood, true);
                    Place("MarketBarrel", halfX - 1.8f, back, 0f, 1.2f, new Vector3(1f, 1.2f, 1f), wood, true);
                    Place("WoodCrate", halfX - 1.8f, back - 1.6f, 30f, 1f, new Vector3(1f, 1f, 1f), wood, true);
                    if (npcName != null)
                        BuildOneVendor(container, groundLayer, npcName, npcName.Split(' ')[0], c + new Vector3(0f, floorY, back * 0.5f - 1.2f), doorSide == 1 ? 180f : 0f,
                            npcStock ?? new[] { "cooked_fish", "leather_cap", "apprentice_focus" });
                    else   // an ambient shopkeeper behind the counter (functional traders are the market stalls)
                        PlaceTownsfolk(container, c + new Vector3(0f, floorY, back * 0.5f - 1.4f), new Color(0.4f, 0.36f, 0.3f), groundLayer);
                }
                else Place("Bed", -halfX + 2f, back, 90f, 2.2f, new Vector3(1.3f, 0.7f, 2.2f), cloth, true);
            }
            else   // "house" / residence
            {
                if (ground)
                {
                    Place("Fireplace", halfX - 1.4f, back, doorSide == 1 ? 0f : 180f, 2.4f, new Vector3(1.4f, 2f, 1.2f), new Color(0.32f, 0.3f, 0.3f), true);
                    Place("Table", 0f, 0f, 0f, 1.4f, new Vector3(1.5f, 0.9f, 1.5f), wood);
                    Place("Chair", 1.2f, 0f, 90f, 1.1f, new Vector3(0.5f, 1f, 0.5f), wood);
                    Place("Chair", -1.2f, 0f, 270f, 1.1f, new Vector3(0.5f, 1f, 0.5f), wood);
                    Place("Cauldron", halfX - 2.6f, back, 0f, 1.1f, new Vector3(0.8f, 0.9f, 0.8f), new Color(0.18f, 0.18f, 0.2f));
                    Place("Rug", 0f, 0f, 0f, 2.5f, new Vector3(2f, 0.05f, 1.6f), new Color(0.42f, 0.22f, 0.16f));
                    PlaceTownsfolk(container, c + new Vector3(-1.2f, floorY, -1.4f), new Color(0.36f, 0.34f, 0.44f), groundLayer);
                }
                else
                {
                    Place("Bed", -halfX + 2f, back, 90f, 2.2f, new Vector3(1.3f, 0.7f, 2.2f), cloth, true);
                    Place("Bookshelf", halfX - 1.2f, back, 270f, 2.4f, new Vector3(0.6f, 2.2f, 1.6f), wood, true);
                    Place("Rug", 0f, 0f, 0f, 2f, new Vector3(1.8f, 0.05f, 1.4f), new Color(0.4f, 0.2f, 0.14f));
                }
            }
        }

        /// <summary>A street lamp that ACTUALLY emits light. variant 0 = a frosted glowing globe on
        /// an iron post; variant 1 = an iron post with a hanging warm lantern on an arm. Both add a
        /// real point light + are recorded in the placement catalog.</summary>
        private static void BuildStreetLamp(Transform container, Vector3 basePos, int variant, int groundLayer)
        {
            // Data-first: never plant a lamp in the canal water (the coarse route can drift over it).
            if (_place != null && _place.InBounds(basePos.x, basePos.z) && _place.GroundAt(basePos.x, basePos.z) == Ground.Water) return;
            var iron = Mat("LampIron", new Color(0.13f, 0.12f, 0.13f), smoothness: 0.25f);
            const float h = 3.6f;
            var post = Cube("Lamp_Post", basePos + Vector3.up * (h / 2f), new Vector3(0.18f, h, 0.18f), iron, container);
            post.isStatic = true; if (groundLayer >= 0) post.layer = groundLayer;
            Color glow;
            if (variant == 0)
            {
                glow = new Color(1f, 0.93f, 0.72f);
                var globeMat = Mat("LampGlobe", new Color(1f, 0.97f, 0.82f), smoothness: 0.4f, emission: new Color(1f, 0.93f, 0.72f) * 2.4f);
                var globe = Sphere("Lamp_Globe", basePos + Vector3.up * (h + 0.28f), Vector3.one * 0.55f, globeMat, container);
                Object.DestroyImmediate(globe.GetComponent<Collider>());
            }
            else
            {
                glow = new Color(1f, 0.66f, 0.3f);
                var lantMat = Mat("LampLantern", new Color(1f, 0.8f, 0.5f), smoothness: 0.3f, emission: new Color(1f, 0.62f, 0.28f) * 2.2f);
                var arm = Cube("Lamp_Arm", basePos + Vector3.up * (h - 0.15f) + new Vector3(0.45f, 0f, 0f), new Vector3(0.95f, 0.12f, 0.12f), iron, container);
                Object.DestroyImmediate(arm.GetComponent<BoxCollider>());
                var lant = Cube("Lamp_Lantern", basePos + Vector3.up * (h - 0.45f) + new Vector3(0.9f, 0f, 0f), new Vector3(0.42f, 0.62f, 0.42f), lantMat, container);
                Object.DestroyImmediate(lant.GetComponent<BoxCollider>());
            }
            var lo = new GameObject("Lamp_Light"); lo.transform.SetParent(container);
            lo.transform.position = basePos + Vector3.up * (h + 0.2f);
            var L = lo.AddComponent<Light>();
            L.type = LightType.Point; L.color = glow; L.range = 15f; L.intensity = 2.6f; L.shadows = LightShadows.None;
            RecordDetail(variant == 0 ? "lamp_globe" : "lamp_lantern", basePos.x, basePos.z, 0f, h);
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
            var cityDef = EmberfallCityDefinition.Load();
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
            foreach (var authored in cityDef.bridges)
            {
                var at = new Vector3(authored.position[0], 0.15f, authored.position[1]);
                var rotation = Quaternion.Euler(0f, authored.yaw, 0f);
                var deck = Cube(authored.id, at, new Vector3(authored.width, 0.5f, authored.length), stoneMat, container);
                deck.transform.rotation = rotation;
                deck.isStatic = true;
                if (groundLayer >= 0) deck.layer = groundLayer;
                // Low parapets on the deck sides.
                foreach (float sgn in new[] { 1f, -1f })
                {
                    var rail = Cube($"{authored.id}_Rail_{(sgn > 0 ? "R" : "L")}",
                        at + rotation * new Vector3(sgn * (authored.width * 0.46f), 0.6f, 0f),
                        new Vector3(0.5f, 1.0f, authored.length), stoneDark, container);
                    rail.transform.rotation = rotation;
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
            const float canalHalf = 9f;   // half-width of the culvert opening (canal is ~16m wide)

            void Wall(string n, Vector3 pos, Vector3 size)
            {
                var w = Cube(n, pos, size, wallMat, container);
                w.isStatic = true;
                if (groundLayer >= 0) w.layer = groundLayer;
                _manifest?.AddWall(n, pos, size, 0f);   // register barrier so nothing scatters inside it
            }

            // Where the manmade canal (R3) crosses a given wall X, or NaN if it doesn't.
            float CanalCrossZ(float wallX)
            {
                var pts = RiverPaths[3];
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    float x0 = pts[i].x, x1 = pts[i + 1].x;
                    if ((x0 - wallX) * (x1 - wallX) <= 0f && Mathf.Abs(x1 - x0) > 0.001f)
                        return Mathf.Lerp(pts[i].z, pts[i + 1].z, (wallX - x0) / (x1 - x0));
                }
                return float.NaN;
            }

            // Gate decoration: flanking pillars, lintel, torches, and (N/S) a name/warn plate.
            void GateDecor(string side, Vector3 center, bool alongX, string label)
            {
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

            // Canal water-gate: the wall opening the manmade canal flows through. A low arched
            // lintel + jamb piers over the water read it as a built culvert (manmade INSIDE ->
            // natural OUTSIDE), not a random hole. Water level ~WaterSurfaceY; arch clears it.
            void CanalCulvert(string side, Vector3 center, bool alongX)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 off = alongX ? new Vector3(s * (canalHalf + 0.8f), 2.5f, 0f)
                                         : new Vector3(0f, 2.5f, s * (canalHalf + 0.8f));
                    var pier = Cube($"Culvert_{side}_P{s}", center + off, new Vector3(alongX ? 1.6f : thick + 1.4f, 5f, alongX ? thick + 1.4f : 1.6f), pillarMat, container);
                    pier.isStatic = true; if (groundLayer >= 0) pier.layer = groundLayer;
                }
                // Arch lintel bridging the gap (top of the opening) + the wall above it.
                var arch = Cube($"Culvert_{side}_Arch", center + Vector3.up * 4.8f,
                    new Vector3(alongX ? canalHalf * 2f + 2f : thick + 0.4f, 1.4f, alongX ? thick + 0.4f : canalHalf * 2f + 2f), pillarMat, container);
                arch.isStatic = true; if (groundLayer >= 0) arch.layer = groundLayer;
                var above = Cube($"Culvert_{side}_Top", center + Vector3.up * 6.3f,
                    new Vector3(alongX ? canalHalf * 2f + 2f : thick, h - 5.6f, alongX ? thick : canalHalf * 2f + 2f), wallMat, container);
                above.isStatic = true; if (groundLayer >= 0) above.layer = groundLayer;
                CreateTorch(container, center + (alongX ? new Vector3(-(canalHalf + 0.8f), 0, 0) : new Vector3(0, 0, -(canalHalf + 0.8f))), 0.9f);
                CreateTorch(container, center + (alongX ? new Vector3(canalHalf + 0.8f, 0, 0) : new Vector3(0, 0, canalHalf + 0.8f)), 0.9f);
            }

            // Build one walled side with openings (gate at centre + any canal culvert), leaving
            // clean gaps so the wall segments never span an opening.
            void Side(string side, Vector3 center, bool alongX, string label)
            {
                float halfSpan = CityHalf + thick;   // wall runs center +/- halfSpan along its axis
                var openings = new System.Collections.Generic.List<(float pos, float halfW, bool gate)>
                    { (0f, CityGate / 2f, true) };
                // Canal culvert where R3 crosses THIS wall (E/W walls only in practice).
                if (!alongX)
                {
                    float cz = CanalCrossZ(center.x);
                    if (!float.IsNaN(cz)) openings.Add((cz, canalHalf, false));
                }
                openings.Sort((a, b) => a.pos.CompareTo(b.pos));
                int idx = 0;
                float cursor = -halfSpan;
                void Seg(float a, float b)
                {
                    if (b - a < 0.4f) return;
                    float mid = (a + b) / 2f, segLen = b - a;
                    Vector3 pos = center + (alongX ? new Vector3(mid, h / 2f, 0f) : new Vector3(0f, h / 2f, mid));
                    Vector3 size = alongX ? new Vector3(segLen, h, thick) : new Vector3(thick, h, segLen);
                    Wall($"Wall_{side}_{idx++}", pos, size);
                }
                foreach (var op in openings) { Seg(cursor, op.pos - op.halfW); cursor = op.pos + op.halfW; }
                Seg(cursor, halfSpan);
                foreach (var op in openings)
                {
                    Vector3 oc = center + (alongX ? new Vector3(op.pos, 0f, 0f) : new Vector3(0f, 0f, op.pos));
                    if (op.gate) GateDecor(side, oc, alongX, label); else CanalCulvert(side, oc, alongX);
                }
            }

            Side("N", new Vector3(0f, 0f, CityHalf + thick / 2f), true, "EMBERFALL");
            Side("S", new Vector3(0f, 0f, -CityHalf - thick / 2f), true, "WARN");
            Side("E", new Vector3(CityHalf + thick / 2f, 0f, 0f), false, null);
            Side("W", new Vector3(-CityHalf - thick / 2f, 0f, 0f), false, null);

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
            // More opaque + lower smoothness: the high-smoothness surface blew out to a flat
            // white sky-glint at grazing angles, reading as a broad flood sheet. A deeper, more
            // opaque blue-green with a matte finish reads as contained water.
            // REAL water via the AldenFall/Water shader: depth-based clarity (clear shallows show
            // the bed, deepening to blue), animated ripples + moving sun sparkle, fresnel, and a
            // foam line at the shore. Reads as one fluid body, not a flat blue lid. Falls back to a
            // flat opaque teal only if the shader is missing.
            var waterMat = BuildWaterMaterial();

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

            // Water trigger volume (Water layer) covering the WHOLE lake basin (was a tiny 60m core
            // -> you could run underwater far out on the bed without ever swimming). Swim detection
            // is DEPTH-based (enter at 1.3m feet-below-surface), so a full-basin box still means you
            // WADE the shallows and only SWIM where the water is genuinely deep - and dry corners of
            // the box never trigger swim (depth there is ~0). Bottom sits below the bed so the
            // deepest lakebed is inside the volume.
            float volTop = WaterSurfaceY, volBot = LakeBedY - 4f;
            var vol = new GameObject("Lake_WaterVolume");
            vol.transform.SetParent(container);
            vol.transform.position = c + Vector3.up * ((volTop + volBot) / 2f);
            var vbox = vol.AddComponent<BoxCollider>();
            vbox.isTrigger = true;
            vbox.size = new Vector3(LakeHalf * 2f, volTop - volBot, LakeHalf * 2f);
            if (waterLayer >= 0) vol.layer = waterLayer;

            // Carve the DEEP lake out of the NavMesh (area=Not Walkable) so enemies can't path onto
            // the submerged bed and stand underwater (Jon: "mobs underwater"). Sized to the deep
            // basin only, so the shallow shore RING where NPCs/fishers stand stays walkable. Both
            // the editor bake and the dedicated-server runtime bake read this volume.
            var carve = new GameObject("Lake_NavCarve");
            carve.transform.SetParent(container);
            carve.transform.position = c + Vector3.up * ((0.5f + (LakeBedY - 1f)) / 2f);
            var nmv = carve.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
            nmv.size = new Vector3(120f, 0.5f - (LakeBedY - 1f), 120f);
            nmv.center = Vector3.zero;
            nmv.area = 1;   // 1 = Not Walkable

            // LAKE surface: an ORGANIC DISC mesh that follows the shoreline exactly, NOT a square
            // plane. The square plane's corners jutted ~30m past the round basin onto dry rim,
            // reading as a broad flood sheet (Jon: "obviously shows flooding"). LakeEdge is purely
            // angular, so each rim vertex sits at the true waterline (~0.93*(edge+4)) inset a few
            // metres, leaving a clean sand ring between water and grass.
            BuildLakeSurfaceMesh(container, waterMat);

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

        /// <summary>The shared water material — AldenFall/Water shader (depth-clear + ripples +
        /// foam) when present, else a flat teal fallback.</summary>
        private static Material BuildWaterMaterial()
        {
            var shader = Shader.Find("AldenFall/Water");
            if (shader == null)
                return Mat("LakeWater", new Color(0.08f, 0.21f, 0.27f), smoothness: 0.1f);
            string mp = $"{MaterialDir}/EmberWater.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, mp); }
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_NormalTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Water/WaterNormal.png"));
            m.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Water/FoamMask.png"));
            m.SetColor("_ShallowColor", new Color(0.33f, 0.58f, 0.56f, 1f));
            m.SetColor("_DeepColor", new Color(0.09f, 0.27f, 0.32f, 1f));
            m.SetVector("_Absorb", new Vector4(0.42f, 0.18f, 0.13f, 0f));
            m.SetColor("_FresnelTint", new Color(0.56f, 0.66f, 0.72f, 1f));
            m.SetColor("_FoamColor", new Color(0.94f, 0.97f, 0.98f, 1f));
            m.SetFloat("_RefractStrength", 0.35f);
            m.SetFloat("_DetailStrength", 0.85f);
            m.SetFloat("_DetailScale", 5.5f);
            m.SetFloat("_FoamScale", 7.0f);
            m.SetFloat("_RippleSpeed", 0.55f);
            m.SetFloat("_WaveHeight", 0.06f);
            m.SetFloat("_Sparkle", 1.3f);
            m.SetVector("_FlowDir", new Vector4(0.7f, 0.7f, 0.045f, 0f));
            m.SetVector("_FoamZones", new Vector4(0.18f, 0.7f, 1.5f, 0f));
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>The lake water surface as an ORGANIC DISC mesh whose rim follows the true
        /// waterline (radius per angle from LakeEdge), inset a touch so it never overhangs the
        /// dry shore. Replaces the square plane whose corners jutted onto land as a flood sheet.
        /// Double-sided so it renders from above and while swimming under it.</summary>
        private static void BuildLakeSurfaceMesh(Transform container, Material waterMat)
        {
            // Radial GRID (rings x segments), not a center fan: the v2 water shader displaces
            // vertices for real swell, and a fan has no interior vertices to move. Rings are
            // sqrt-spaced so density concentrates toward the rim where the shoreline lives.
            const int seg = 96;
            const int rings = 10;
            var mesh = new Mesh { name = "LakeSurfaceMesh" };
            var verts = new Vector3[1 + seg * rings];
            var uvs = new Vector2[verts.Length];
            verts[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float ang = i / (float)seg * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                // Terrain waterline solves to lakeR ~= 0.867*(edge+4). The rim OVERHANGS the
                // waterline +0.5m so the shader's foam band dissolves the edge into the sand
                // without reaching over beach ridges and flooding the dips behind them.
                float edge = LakeEdge(LakeCenter.x + dir.x * 50f, LakeCenter.z + dir.y * 50f);
                float rWater = Mathf.Max(4f, 0.867f * (edge + 4f) + 0.5f);
                for (int j = 0; j < rings; j++)
                {
                    float rt = Mathf.Sqrt((j + 1) / (float)rings);
                    int vi = 1 + i * rings + j;
                    verts[vi] = new Vector3(dir.x * rWater * rt, 0f, dir.y * rWater * rt);
                    uvs[vi] = new Vector2(0.5f + dir.x * rt * 0.5f, 0.5f + dir.y * rt * 0.5f);
                }
            }
            var triList = new System.Collections.Generic.List<int>(seg * rings * 12);
            void Quad(int a, int b, int c, int d)
            {
                triList.AddRange(new[] { a, b, c, a, c, d });       // up face
                triList.AddRange(new[] { a, c, b, a, d, c });       // down face
            }
            for (int i = 0; i < seg; i++)
            {
                int i2 = (i + 1) % seg;
                int v0 = 1 + i * rings, w0 = 1 + i2 * rings;
                triList.AddRange(new[] { 0, v0, w0, 0, w0, v0 });   // center fan (both faces)
                for (int j = 0; j < rings - 1; j++)
                    Quad(v0 + j, v0 + j + 1, w0 + j + 1, w0 + j);
            }
            mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = triList.ToArray();
            var up = new Vector3[verts.Length];
            for (int i = 0; i < up.Length; i++) up[i] = Vector3.up;
            mesh.normals = up;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{MaterialDir}/LakeSurfaceMesh.asset");

            var go = new GameObject("Lake_Surface");
            go.transform.SetParent(container);
            go.transform.position = LakeCenter + Vector3.up * WaterSurfaceY;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = waterMat;
            go.isStatic = true;
        }

        /// <summary>A vertical falling water sheet + foam spray + mist glow, poured at the
        /// mountain-ring edge. <paramref name="basePos"/> is the BASE; the sheet rises
        /// <paramref name="height"/> above it.</summary>
        private static void BuildWaterfall(Transform container, Vector3 basePos, float height, float width, float yaw, Material waterMat, int groundLayer)
        {
            var foamMat = Mat("WaterFoam", new Color(0.95f, 0.97f, 0.99f), smoothness: 0.1f,
                emission: new Color(0.10f, 0.11f, 0.12f));
            // Muted natural blue-green (the bright cyan sheet read as a glowing card / "sore
            // thumb" at distance). Matte-ish so it doesn't sun-glint white.
            var fallWaterMat = Mat("FallWater", new Color(0.30f, 0.48f, 0.55f, 0.92f), smoothness: 0.38f,
                transparent: true, emission: new Color(0.03f, 0.06f, 0.07f));
            var rockMat = Mat("FallRock", new Color(0.26f, 0.25f, 0.24f), smoothness: 0.04f);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 intoMtn = rot * Vector3.forward;    // toward the source (up the mountain)
            Vector3 toValley = -intoMtn;                // downstream, where the plunge pool sits
            Vector3 right = rot * Vector3.right;

            // Animated froth on a FLAT quad (AldenFall/FoamPatch: scrolling procedural churn with a
            // soft elliptical edge). Replaces the old white SPHERE blobs, which read as "3 balls of
            // white" at distance and violated the no-primitive-scenery rule.
            var foamPatchShader = Shader.Find("AldenFall/FoamPatch");
            Material FoamMatFor(float stretch, float density)
            {
                if (foamPatchShader == null) return foamMat;
                string fp = $"{MaterialDir}/FoamPatch_{stretch:F1}_{density:F2}.mat";
                var fm = AssetDatabase.LoadAssetAtPath<Material>(fp);
                if (fm == null) { fm = new Material(foamPatchShader); AssetDatabase.CreateAsset(fm, fp); }
                else if (fm.shader != foamPatchShader) fm.shader = foamPatchShader;
                fm.SetFloat("_Stretch", stretch);
                fm.SetFloat("_Density", density);
                fm.SetFloat("_Churn", 1.1f);
                fm.renderQueue = 3001;   // atop the water surface
                EditorUtility.SetDirty(fm);
                return fm;
            }
            GameObject FoamQuad(string n, Vector3 pos, float sizeX, float sizeZ, float yawDeg, Material fm)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = n; q.transform.SetParent(container); q.transform.position = pos;
                q.transform.rotation = Quaternion.Euler(90f, yawDeg, 0f);   // flat on the water
                q.transform.localScale = new Vector3(sizeX, sizeZ, 1f);
                q.GetComponent<Renderer>().sharedMaterial = fm;
                Object.DestroyImmediate(q.GetComponent<Collider>());
                q.isStatic = true; if (groundLayer >= 0) q.layer = groundLayer;
                return q;
            }
            // A tuft of ferns/bush at the wet rocks - foliage volume around the fall.
            void Foliage(string kind, Vector3 pos, float h)
                => PlaceMeshyProp(container, kind, pos, (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.None);

            // REAL rock with VARIETY (Jon: "not just a massive stack of the same"): rotate through
            // four different rock meshes, random full-3D tilt, and varied scale so the cliff reads
            // as a natural jumble of boulders, not one mesh cloned. A dark cube only if all miss.
            string[] fallRocks = { "namaqualand_boulder_02", "boulder_01", "boulder_01", "namaqualand_boulder_02" };
            int rockPick = 0;
            void Rock(string label, Vector3 pos, float size)
            {
                float ry = (float)rng.NextDouble() * 360f;
                // interleave the meshes deterministically + a random offset so neighbours differ
                string mesh = fallRocks[(rockPick++ + rng.Next(2)) % fallRocks.Length];
                float sz = size * (0.85f + (float)rng.NextDouble() * 0.4f);
                // Cliff-face rocks intentionally sit above the sampled terrain and therefore are
                // visual structure, not terrain-grounded catalog solids.
                bool priorSuppress = _suppressAutoRecord;
                _suppressAutoRecord = true;
                var r = PlaceMeshyProp(container, mesh, pos, ry, sz, groundLayer, PropCollision.FitBox)
                     ?? PlaceMeshyProp(container, "namaqualand_boulder_02", pos, ry, sz, groundLayer, PropCollision.FitBox)
                     ?? PlaceMeshyProp(container, "boulder_01", pos, ry, sz, groundLayer, PropCollision.FitBox);
                _suppressAutoRecord = priorSuppress;
                if (r != null)
                {
                    // WET, DARKER cliff rock (distinct "_Wet" material so forest boulders stay bright)
                    // so the fall reads as wet stone, not a pale boulder pile.
                    var wet = MeshyCharacterImporter.BuildTexturedMaterial(mesh + "_Wet",
                        $"Assets/Models/Props/{mesh}_baseColor", new Color(0.42f, 0.47f, 0.52f));
                    if (wet != null) foreach (var rend in r.GetComponentsInChildren<Renderer>()) rend.sharedMaterial = wet;
                    // gentle tilt only - big tilts read as a chaotic pile, not a cliff
                    r.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 12f, ry, (float)(rng.NextDouble() - 0.5) * 12f) * r.transform.rotation;
                }
                // No primitive fallback: an absent scan leaves a cleaner silhouette than a cube.
            }

            // A real FALLING-WATER sheet (AldenFall/Waterfall shader: vertical foam veins + base
            // churn, reads as moving water even in a still) is THE hero. A WIDE, gently-leaning
            // sheet pours down the mountain face; rocks only FRAME it (a lip at the top + boulders
            // banking each side + base) - never a tall wall in front that hides the water.
            float vw = Mathf.Max(width, 15f);                    // visual width - avoid a narrow tower
            const float leanDeg = 8f;                            // lean back from vertical (gentle)
            float run = height * Mathf.Tan(leanDeg * Mathf.Deg2Rad);   // horizontal setback of the top
            float sheetLen = height / Mathf.Cos(leanDeg * Mathf.Deg2Rad);
            Quaternion sheetRot = Quaternion.AngleAxis(-leanDeg, right) * rot;
            Vector3 sheetCenter = basePos + Vector3.up * (WaterSurfaceY + height * 0.5f) + intoMtn * (run * 0.5f);

            // Rocky LIP at the top where the water tips over (a short row spanning the width), and
            // BANK boulders climbing each side to frame the fall - all set BEHIND/BESIDE the water
            // plane and OUTSIDE the sheet width, so nothing occludes the cascade from the front.
            int lipN = Mathf.Max(5, Mathf.RoundToInt(vw / 2.4f));
            for (int i = 0; i <= lipN; i++)
            {
                float fx = (i / (float)lipN - 0.5f) * vw * 1.05f;
                // Lower + set back so the rocks sit AS the ledge (embedded, overlapping) instead of
                // floating as spaced blocks above the water lip.
                Vector3 lip = basePos + Vector3.up * (WaterSurfaceY + height - 1.6f) + intoMtn * (run + 2.2f) + right * fx;
                Rock($"Lip{i}", lip, 3.2f + (float)rng.NextDouble() * 1.6f);
            }
            int banks = Mathf.Max(4, Mathf.RoundToInt(height / 4.5f));
            for (int i = 0; i <= banks; i++)
            {
                float fy = i / (float)banks;
                Vector3 bc = basePos + Vector3.up * (WaterSurfaceY + fy * height) + intoMtn * (fy * run);
                float bs = 2.6f + (float)rng.NextDouble() * 1.8f;
                Rock($"BankL{i}", bc - right * (vw * 0.62f) + intoMtn * (0.6f + (float)rng.NextDouble()), bs);
                Rock($"BankR{i}", bc + right * (vw * 0.62f) + intoMtn * (0.6f + (float)rng.NextDouble()), bs);
            }
            width = vw;   // downstream foam/pool sizing uses the visual width

            // THE water sheet: a vertical quad with the flowing-water shader. Falls back to the
            // matte blue material if the shader failed to compile.
            Material fallSheetMat = null;
            var fallShader = Shader.Find("AldenFall/Waterfall");
            if (fallShader != null)
            {
                string fp = $"{MaterialDir}/FallSheet.mat";
                fallSheetMat = AssetDatabase.LoadAssetAtPath<Material>(fp);
                if (fallSheetMat == null) { fallSheetMat = new Material(fallShader); AssetDatabase.CreateAsset(fallSheetMat, fp); }
                else if (fallSheetMat.shader != fallShader) fallSheetMat.shader = fallShader;
            }
            var sheet = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet.name = "Fall_Sheet";
            sheet.transform.SetParent(container);
            sheet.transform.position = sheetCenter + toValley * 0.4f;   // just in front of the backing rock
            sheet.transform.rotation = sheetRot;
            sheet.transform.localScale = new Vector3(width * 1.02f, sheetLen, 1f);
            Object.DestroyImmediate(sheet.GetComponent<Collider>());
            sheet.GetComponent<Renderer>().sharedMaterial = fallSheetMat ?? fallWaterMat;
            if (groundLayer >= 0) sheet.layer = groundLayer;
            // A second, narrower sheet a touch further forward adds depth/volume to the cascade.
            var sheet2 = GameObject.CreatePrimitive(PrimitiveType.Quad);
            sheet2.name = "Fall_Sheet2";
            sheet2.transform.SetParent(container);
            sheet2.transform.position = sheetCenter + toValley * 0.9f + Vector3.up * (height * 0.02f);
            sheet2.transform.rotation = sheetRot;
            sheet2.transform.localScale = new Vector3(width * 0.7f, sheetLen * 0.98f, 1f);
            Object.DestroyImmediate(sheet2.GetComponent<Collider>());
            sheet2.GetComponent<Renderer>().sharedMaterial = fallSheetMat ?? fallWaterMat;
            if (groundLayer >= 0) sheet2.layer = groundLayer;

            // Foam line where the water tips over the lip.
            FoamQuad("Lip_Foam", basePos + Vector3.up * (WaterSurfaceY + height - 0.25f) + intoMtn * (run * 0.8f),
                width * 0.9f, 1.1f, yaw, FoamMatFor(3.5f, 0.7f));

            // CRASHING FOAM at the plunge: two staggered churning patches hugging the impact,
            // sitting just above the water surface.
            Vector3 plunge = basePos + toValley * 1.3f + Vector3.up * (WaterSurfaceY + 0.08f);
            var churn = FoamMatFor(1f, 0.62f);
            FoamQuad("Crash_Foam0", plunge, width * 1.25f, width * 0.85f, yaw, churn);
            FoamQuad("Crash_Foam1", plunge + toValley * (width * 0.28f) + Vector3.up * 0.04f,
                width * 0.9f, width * 0.6f, yaw + 24f, churn);

            // Foam streaks fanning downstream across the plunge pool.
            var streak = FoamMatFor(0.22f, 0.5f);
            for (int i = 0; i < 5; i++)
            {
                float a = (i / 5f - 0.5f) * 1.2f;
                FoamQuad($"Fall_Trail{i}", plunge + toValley * (2.2f + i * 0.5f) + right * (a * width) + Vector3.up * 0.02f,
                    0.6f + (float)rng.NextDouble() * 0.5f, 3.2f + (float)rng.NextDouble() * 2.5f, yaw, streak);
            }
            // Just TWO wet boulders flanking the plunge base (not a ring closing off the front).
            Rock("Plunge_RkL", basePos + toValley * 1.6f - right * (width * 0.7f) + Vector3.up * 0.3f, 2.2f + (float)rng.NextDouble() * 2f);
            Rock("Plunge_RkR", basePos + toValley * 1.6f + right * (width * 0.7f) + Vector3.up * 0.3f, 2.2f + (float)rng.NextDouble() * 2f);

            // FOLIAGE volume: ferns + bushes tucked among the wet rocks, moss boulders, and a pine
            // flanking each bank so the fall sits in living terrain, not bare geometry.
            bool priorFoliageSuppress = _suppressAutoRecord;
            _suppressAutoRecord = true; // gorge dressing follows the waterfall structure, not surface-grid height
            for (int i = 0; i < 8; i++)
            {
                float sgn = i % 2 == 0 ? 1f : -1f;
                Vector3 fp = basePos + toValley * ((float)rng.NextDouble() * 3f) + right * (sgn * (width * 0.7f + (float)rng.NextDouble() * 4f)) + Vector3.up * 0.2f;
                Foliage(rng.NextDouble() < 0.6 ? "fern_02" : "shrub_01", fp, 0.8f + (float)rng.NextDouble() * 0.7f);
            }
            Rock("Fall_MossL", basePos + toValley * 3f + right * (width * 0.9f), 2.5f + (float)rng.NextDouble());
            Rock("Fall_MossR", basePos + toValley * 3f - right * (width * 0.9f), 2.5f + (float)rng.NextDouble());
            Foliage("island_tree_02", basePos + toValley * 4f + right * (width * 1.2f), 6f + (float)rng.NextDouble() * 3f);
            Foliage("island_tree_02", basePos + toValley * 4f - right * (width * 1.2f), 6f + (float)rng.NextDouble() * 3f);
            _suppressAutoRecord = priorFoliageSuppress;

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
                BuildWaterfall(container, source + Vector3.up * (-3f), 22f, RiverHalfWidths[r] * 3.0f, yaw, waterMat, groundLayer);
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

            // Real campfire mesh if present; otherwise just the fire light (no Sphere-primitive stone
            // ring -- it read as a cluster of grey balls). A charred log ring can come with a real asset.
            PlaceMeshyProp(fireObj.transform, "Campfire", fireObj.transform.position, rng.Next(0, 360), 0.9f, groundLayer);
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
            if (_worldDef != null && _worldDef.version >= 2)
            {
                // Canopy and understorey are already compiled from north_woods. This POI is the
                // authored exploration payoff, tucked behind the western glade cradle.
                CreateLootChest(container, new Vector3(-126f, 0f, 486f), 140f,
                    new (string id, float chance)[] { ("raider_saber", 1f), ("bandit_cape", 0.8f), ("adept_focus", 0.5f) }, groundLayer);
                return;
            }
            Vector3 c = new Vector3(0f, 0f, 195f);

            var trunkMat = Mat("DarkTrunk", new Color(0.18f, 0.13f, 0.09f), smoothness: 0.04f);
            var leafMat = Mat("DarkLeaf", new Color(0.07f, 0.16f, 0.09f), smoothness: 0.05f);
            // Dense DARK EVERGREENS (no DeadTree - dead trees belong in the volcanic band, not this
            // lush lakeside grove; they were reading as "dead trees in the green forest").
            string[] kit = { "island_tree_02", "island_tree_01", "island_tree_03", "island_tree_02", "tree_small_02" };

            int planted = 0;
            for (int attempt = 0; attempt < 900 && planted < 150; attempt++)
            {
                float x = c.x + ((float)rng.NextDouble() * 2f - 1f) * 62f;
                float z = c.z + ((float)rng.NextDouble() * 2f - 1f) * 55f;
                if (Mathf.Sqrt(x * x + z * z) > 246f) continue;
                if (InLakeWater(x, z) || InFeederRiver(x, z)) continue;
                if (Mathf.Abs(x - LakeCenter.x) < LakeHalf + 8f && Mathf.Abs(z - LakeCenter.z) < LakeHalf + 8f) continue;
                float h = 7f + (float)rng.NextDouble() * 6f;
                // Respect the placement map: no trees in the road/water/city, none on catalog trees.
                if (!MapClear(x, z, 2.7f) || !OffRoad(x, z)) continue;
                planted++;
                // Real tree only. If the gate rejects it (wrong ground / overlap / taken) it is SKIPPED,
                // never replaced with a cube (HARD RULE). Fewer trees > fake cube trees.
                PlaceMeshyProp(container, kit[planted % kit.Length], new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk);
            }

            // (Removed the DarkShroom "glowing mushroom" waypoints: they were cyan-emissive SPHERE
            // primitives at a fixed y that floated on the slope and read as random blue balls. The dark
            // woods keeps its gloom from the dim ambient + real trees; no floating orbs.)

            // Hidden treasure deep in the gloom (guarded by the new acolyte pair at
            // (-20,210) in monsters.json) — exploration should pay.
            CreateLootChest(container, new Vector3(-24f, 0f, 218f), 140f,
                new (string id, float chance)[] { ("raider_saber", 1f), ("bandit_cape", 0.8f), ("adept_focus", 0.5f) }, groundLayer);
        }

        /// <summary>Hidden world-secret chests: exploration rewards off the beaten paths.</summary>
        private static void BuildWorldSecrets(Transform root, int groundLayer)
        {
            var container = Child(root, "WorldSecrets");

            if (_worldDef != null && _worldDef.version >= 2)
            {
                CreateLootChest(container, new Vector3(-205f, 0f, 795f), 45f,
                    new (string id, float chance)[] { ("iron_chestplate", 1f), ("cooked_carp", 1f), ("apprentice_focus", 0.6f) }, groundLayer);
                CreateLootChest(container, new Vector3(74f, 0f, -718f), -60f,
                    new (string id, float chance)[] { ("iron_legs", 1f), ("emberfang", 0.35f) }, groundLayer);
                return;
            }

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
            if (_encounterDef != null && _worldDef != null && _worldDef.version >= 2)
            {
                foreach (var camp in _encounterDef.camps)
                {
                    if (camp.kind == "spawner" || camp.position == null || camp.position.Length < 3) continue;
                    if (camp.region == "volcano_dungeon" || camp.region == "ember_approach" || camp.region == "volcano_entrance") continue;
                    Vector3 p = new Vector3(camp.position[0], 0f, camp.position[2]);
                    if (camp.mobId.Contains("goblin") || camp.mobId.Contains("bandit")) BuildBanditCampDressing(container, p);
                    else if (camp.mobId.Contains("orc")) BuildOrcCampDressing(container, p);
                    else BuildBanditCampDressing(container, p);
                }
                return;
            }
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
            if (_encounterDef != null && _worldDef != null && _worldDef.version >= 2)
            {
                foreach (var camp in _encounterDef.camps)
                    if (camp.id == "orc_warcamp" && camp.position != null && camp.position.Length >= 3)
                    { center = new Vector3(camp.position[0], 0f, camp.position[2]); break; }
            }

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
                // Real charred tree only; if the gate rejects it, SKIP -- no cube-trunk/cube-leaf fake
                // (that was the "CharTree cube" the audit flagged, a HARD-RULE violation).
                PlaceMeshyProp(container, "dead_tree_trunk", new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk);
            }

            // ---- Scorched ground debris: REAL textured volcanic rock (glowing lava-rock chunks +
            // basalt boulders) scattered down the corridor, denser toward the volcano, so the floor
            // reads as broken volcanic earth built from proper meshes rather than flat cube slop.
            // Glowing ground cracks stay near the caldera. ----
            var emberCrack = Mat("EmberGroundCrack", new Color(1f, 0.45f, 0.12f), smoothness: 0.5f,
                emission: new Color(1f, 0.35f, 0.08f) * 3.5f);
            // ONLY dark volcanic lava-rock here - the pale/mossy granite boulders read as
            // out-of-place white rocks in the charred ember corridor (wrong biome).
            for (int i = 0; i < 220; i++)
            {
                float z = Mathf.Lerp(ApproachNorthZ - 6f, ApproachSouthZ + 4f, (float)rng.NextDouble());
                float x = ((float)rng.NextDouble() * 2f - 1f) * (ApproachHalfW - 3f);
                if (Mathf.Abs(x) < 6f) continue;   // keep the central road clear
                float t = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, z);   // 0 gate -> 1 volcano
                if ((float)rng.NextDouble() > 0.30f + t * 0.50f) continue;        // denser toward the volcano
                float s = 1.0f + (float)rng.NextDouble() * (1.6f + t * 2.0f);
                var r = PlaceMeshyProp(container, "LavaRockGround", new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, s, groundLayer, PropCollision.FitBox);
                if (r != null)   // gentle 3D tilt so rocks sit naturally, not a chaotic pile
                    r.transform.rotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 14f, (float)rng.NextDouble() * 360f, (float)(rng.NextDouble() - 0.5) * 14f) * r.transform.rotation;
                // Glowing fissures crack open on the hot ground closer to the volcano.
                if (t > 0.45f && rng.NextDouble() < 0.28)
                {
                    var crack = Cube($"AshCrack{i}", new Vector3(x + (float)(rng.NextDouble() - 0.5) * 4f, 0.06f, z + (float)(rng.NextDouble() - 0.5) * 4f),
                        new Vector3(0.3f + (float)rng.NextDouble() * 0.5f, 0.05f, 2f + (float)rng.NextDouble() * 3f), emberCrack, container);
                    crack.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 180f, 0);
                    Object.DestroyImmediate(crack.GetComponent<BoxCollider>());
                }
            }

            // ---- Volcanic scrub: charred dead shrubs (real DeadTree meshes scaled low) + wind-worn
            // stones so the approach has a biome-appropriate ecosystem (Jon: "volcano = tumbleweed,
            // burnt bush") built from real assets, no primitive shard/ball clusters. Thins to bare
            // ash near the caldera. ----
            for (int i = 0; i < 90; i++)
            {
                float z = Mathf.Lerp(ApproachNorthZ - 4f, ApproachSouthZ + 30f, (float)rng.NextDouble());
                float x = ((float)rng.NextDouble() * 2f - 1f) * (ApproachHalfW - 4f);
                if (Mathf.Abs(x) < 7f) continue;                        // clear the road
                float t = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, z);
                if ((float)rng.NextDouble() < t * 0.7f) continue;       // thin out toward the caldera
                if (rng.NextDouble() < 0.6)
                    // Low charred scrub: a small DeadTree reads as a burnt bush (real textured mesh).
                    PlaceMeshyProp(container, "dead_tree_trunk", new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, 1.4f + (float)rng.NextDouble() * 1.3f, groundLayer, PropCollision.None);
                else
                    // A squat wind-worn volcanic stone among the scrub (dark, biome-appropriate).
                    PlaceMeshyProp(container, "LavaRockGround",
                        new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f,
                        0.8f + (float)rng.NextDouble() * 0.7f, groundLayer, PropCollision.FitBox);
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
                var pos = center + new Vector3(rx, 0f, rz);
                float rs = 1.8f + (float)rng.NextDouble() * 1.3f;
                // Real textured lava-rock / basalt boulder rim (glowing seams sell the molten heat).
                var rock = PlaceMeshyProp(container, rng.NextDouble() < 0.55 ? "LavaRockGround" : "namaqualand_boulder_02",
                    pos, (float)rng.NextDouble() * 360f, rs, groundLayer, PropCollision.FitBox);
                if (rock == null)
                {
                    var c = Cube($"Lava_Rim{i}", center + new Vector3(rx, 0.25f + (float)rng.NextDouble() * 0.2f, rz),
                        new Vector3(1.6f + (float)rng.NextDouble(), 0.7f, 1.6f + (float)rng.NextDouble()), crustMat, container);
                    c.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 60f, 0);
                    c.isStatic = true;
                    if (groundLayer >= 0) c.layer = groundLayer;
                }
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
            _worldDef ??= EmberfallWorldDefinition.Load();
            float xMin = _worldDef.bounds.minX, xMax = _worldDef.bounds.maxX;
            float zMin = _worldDef.bounds.minZ, zMax = _worldDef.bounds.maxZ;
            var center = new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f);
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
                // Snow only on the very tips (peaks reach y~130-210): was Y=105 -> whole upper half white.
                // More green/rock below + warmer rock tone to match the Poly Haven asset palette.
                mtnMat.SetFloat("_RockTop", 185f);
                mtnMat.SetFloat("_GreenTop", 42f);
                mtnMat.SetFloat("_Band", 24f);
                mtnMat.SetColor("_RockColor", new Color(0.40f, 0.35f, 0.30f, 1f));
                mtnMat.SetColor("_GreenColor", new Color(0.21f, 0.29f, 0.15f, 1f));
                mtnMat.SetColor("_DirtColor", new Color(0.34f, 0.27f, 0.19f, 1f));
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

        /// <summary>Compile the three exceptional long-view rewards. Their locations and reveal
        /// direction are authored input; terrain owns final elevation. Scanned boulders form a
        /// readable cairn without introducing primitive placeholder visuals.</summary>
        private static void BuildAuthoredVistas(Transform root, int groundLayer)
        {
            _worldDef ??= EmberfallWorldDefinition.Load();
            if (_worldDef.vistas == null || _worldDef.vistas.Length == 0) return;

            var container = Child(root, "AuthoredVistas");
            var terrain = Terrain.activeTerrain;
            float terrainY = terrain != null ? terrain.transform.position.y : 0f;
            int index = 0;
            foreach (var vista in _worldDef.vistas)
            {
                if (vista.position == null || vista.position.Length < 3) continue;
                float x = vista.position[0], z = vista.position[2];
                float y = terrain != null
                    ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrainY
                    : vista.position[1];

                var summit = new GameObject(vista.id);
                summit.transform.SetParent(container);
                summit.transform.position = new Vector3(x, y + 1.25f, z);
                summit.transform.rotation = Quaternion.Euler(0f, vista.yaw, 0f);
                var discovery = summit.AddComponent<World.VistaDiscovery>();
                discovery.Configure(vista.id, vista.displayName, 100 + index * 50);

                // A low, asymmetrical cairn remains visible against foliage and communicates an
                // intentional destination. Every visible component is a real scanned asset.
                var offsets = new[]
                {
                    new Vector3(-1.4f, 0f, 0.5f), new Vector3(1.2f, 0f, 0.1f),
                    new Vector3(0.1f, 0f, -1.15f), new Vector3(0.15f, 0f, 0.15f)
                };
                for (int i = 0; i < offsets.Length; i++)
                {
                    var rock = PlaceMeshyProp(container,
                        i == offsets.Length - 1 ? "boulder_01" : "namaqualand_boulder_02",
                        new Vector3(x + offsets[i].x, y + offsets[i].y, z + offsets[i].z),
                        vista.yaw + i * 73f, i == offsets.Length - 1 ? 2.5f : 1.6f + i * 0.22f,
                        groundLayer, PropCollision.FitBox);
                    if (rock != null) rock.name = $"{vista.id}_Cairn_{i}";
                }
                index++;
            }
        }

        private static void BuildAuthoredVolcanoDungeon(Transform container, int groundLayer,
            Material floorMat, Material wallMat, Material rockMat, Material emberMat)
        {
            bool priorSuppress = _suppressAutoRecord;
            _suppressAutoRecord = true; // underground structure is validated by dungeon.json, not surface height
            var def = EmberfallDungeonDefinition.Load();
            var origin = new Vector3(def.origin[0], def.origin[1], def.origin[2]);
            var centres = new Dictionary<string, Vector3>();
            var sizes = new Dictionary<string, Vector3>();

            GameObject Slab(string name, Vector3 p, Vector3 s, Material m)
            {
                var go = Cube(name, p, s, m, container); go.isStatic = true;
                if (groundLayer >= 0) go.layer = groundLayer;
                return go;
            }

            foreach (var room in def.rooms)
            {
                Vector3 c = origin + new Vector3(room.center[0], room.center[1], room.center[2]);
                Vector3 s = new Vector3(room.size[0], room.size[1], room.size[2]);
                centres[room.id] = c; sizes[room.id] = s;
                var group = new GameObject($"SG{room.streamGroup}_{room.id}"); group.transform.SetParent(container);
                Slab($"{room.id}_Floor", c + Vector3.down * 0.3f, new Vector3(s.x, 0.6f, s.z), floorMat);
                Slab($"{room.id}_Ceiling", c + Vector3.up * s.y, new Vector3(s.x + 2f, 0.8f, s.z + 2f), wallMat);
                Slab($"{room.id}_WallW", c + new Vector3(-s.x * 0.5f, s.y * 0.5f, 0), new Vector3(1.2f, s.y, s.z), wallMat);
                Slab($"{room.id}_WallE", c + new Vector3(s.x * 0.5f, s.y * 0.5f, 0), new Vector3(1.2f, s.y, s.z), wallMat);
                // Real scanned rocks and pillars break the structural shell and carry scale/depth.
                for (int i = 0; i < Mathf.Clamp(Mathf.RoundToInt((s.x + s.z) / 30f), 2, 8); i++)
                {
                    float side = i % 2 == 0 ? -1f : 1f;
                    float z = c.z - s.z * 0.35f + (i / Mathf.Max(1f, Mathf.Clamp(Mathf.RoundToInt((s.x + s.z) / 30f), 2, 8) - 1f)) * s.z * 0.7f;
                    PlaceMeshyProp(container, "namaqualand_boulder_02",
                        new Vector3(c.x + side * (s.x * 0.43f), c.y, z), i * 47f, 3.5f + (i % 3), groundLayer, PropCollision.FitBox);
                }
                var lightGo = new GameObject($"{room.id}_Indirect"); lightGo.transform.SetParent(container);
                lightGo.transform.position = c + Vector3.up * Mathf.Min(7f, s.y * 0.62f);
                var light = lightGo.AddComponent<Light>(); light.type = LightType.Point;
                light.color = room.kind == "secret" ? new Color(0.30f, 0.42f, 0.55f) : new Color(1f, 0.42f, 0.16f);
                light.range = Mathf.Min(32f, Mathf.Max(s.x, s.z) * 0.55f); light.intensity = 2.2f; light.shadows = LightShadows.None;
            }

            foreach (var link in def.links)
            {
                if (!centres.TryGetValue(link.from, out var a) || !centres.TryGetValue(link.to, out var b)) continue;
                Vector3 delta = b - a; float len = new Vector2(delta.x, delta.z).magnitude;
                if (len < 1f) continue;
                Vector3 mid = (a + b) * 0.5f;
                float yaw = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float slopeLen = delta.magnitude;
                float pitch = -Mathf.Asin(Mathf.Clamp(delta.y / Mathf.Max(0.01f, slopeLen), -1f, 1f)) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
                var floor = Slab(link.id + "_Floor", mid - Vector3.up * 0.2f,
                    new Vector3(link.width, 0.5f, slopeLen), floorMat); floor.transform.rotation = rot;
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                foreach (float side in new[] { -1f, 1f })
                {
                    var wall = Slab(link.id + (side < 0 ? "_WallL" : "_WallR"),
                        mid + right * side * (link.width * 0.52f) + Vector3.up * 4f,
                        new Vector3(1f, 8f, slopeLen), wallMat); wall.transform.rotation = rot;
                }
                if (link.gated)
                {
                    var gate = Slab(link.id + "_Gate", mid + Vector3.up * 3.8f,
                        new Vector3(link.width, 7.6f, 0.7f), emberMat);
                    gate.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    gate.AddComponent<World.SealableDoor>().Configure(mid + Vector3.up * 4f,
                        new Vector3(link.width + 5f, 12f, 12f));
                }
            }

            foreach (var encounter in def.encounters)
            {
                if (!centres.TryGetValue(encounter.room, out var c)) continue;
                if (encounter.reward.Contains("cache") || encounter.reward.Contains("checkpoint"))
                    CreateLootChest(container, c + new Vector3(0f, 0f, 5f), 0f,
                        new (string id, float chance)[] { ("ember_focus", 0.45f), ("knight_sword", 0.35f), ("cooked_bass", 1f) }, groundLayer);
                if (encounter.room == "magma_works" || encounter.room == "caldera_heart")
                    BuildLavaPool(container, c, new Vector2(sizes[encounter.room].x * 0.28f, sizes[encounter.room].z * 0.55f), groundLayer);
            }

            var trigger = new GameObject("EruptionTrigger"); trigger.transform.SetParent(container);
            trigger.transform.position = centres["descent"];
            trigger.AddComponent<World.EruptionTrigger>().Configure(centres["descent"], new Vector3(18f, 14f, 24f));
            CreateLootChest(container, centres["boss_chamber"] + new Vector3(0f, 0f, -18f), 0f,
                new (string id, float chance)[] { ("ember_focus", 1f), ("emberfang", 0.9f), ("knight_sword", 1f) }, groundLayer);
            _suppressAutoRecord = priorSuppress;
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
                // Prefer the REAL PBR texture baked by Meshy refine (dark basalt cone / painted lava
                // flows). Only fall back to the flat soot-skinned material when that texture is
                // missing, so the cone is never a bald white silhouette.
                bool dormantTex = System.IO.File.Exists("Assets/Models/Props/VolcanoDormant_baseColor.jpg");
                bool eruptTex = System.IO.File.Exists("Assets/Models/Props/VolcanoErupting_baseColor.jpg");
                var dormant = PlaceMeshyProp(vis, "VolcanoDormant", meshCenter, 180f, 230f, groundLayer, PropCollision.FitBox);
                if (dormant != null) { dormant.name = "VolcanoDormant"; if (!dormantTex) Skin(dormant, dormantMat); }
                if (HasMesh("VolcanoErupting"))
                {
                    var erupt = PlaceMeshyProp(vis, "VolcanoErupting", meshCenter, 180f, 230f, groundLayer, PropCollision.None);
                    if (erupt != null)
                    {
                        erupt.name = "VolcanoErupting";
                        if (!eruptTex) Skin(erupt, eruptMat);
                        erupt.SetActive(false);
                    }
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
                if (PlaceMeshyProp(container, "namaqualand_boulder_02", new Vector3(side, 0f, ez + 9f - b), (float)rng.NextDouble() * 360f, 4f + (float)rng.NextDouble() * 3f, groundLayer, PropCollision.FitBox) == null)
                    Rock($"Mouth_Rock{b}", new Vector3(side, 3f, ez + 9f - b), Vector3.one * 6f, 0f, mouthRock);
            }
            CreateTorch(container, new Vector3(-7f, 0f, ez + 10f), 1.3f);
            CreateTorch(container, new Vector3(7f, 0f, ez + 10f), 1.3f);

            // The authoritative graph replaces the legacy absolute-coordinate strip below. The
            // return is intentional: no disconnected legacy rooms survive at the old volcano.
            BuildAuthoredVolcanoDungeon(container, groundLayer, floorMat, wallMat, rockMat, emberMat);
            return;

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
            string[] kit = { "island_tree_03", "island_tree_02", "island_tree_01" };
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f + 0.3f;
                float rad = GladeRadius - 1f + (float)rng.NextDouble() * 3f;
                float x = center.x + Mathf.Cos(a) * rad;
                float z = center.z + Mathf.Sin(a) * rad;
                if (!OffRoad(x, z)) continue;   // don't ring trees across the road that enters the glade
                float h = 7f + (float)rng.NextDouble() * 4f;
                // Real tree only; gate-rejected -> skipped, never a cube (HARD RULE).
                PlaceMeshyProp(container, kit[i % kit.Length], new Vector3(x, 0f, z),
                    (float)rng.NextDouble() * 360f, h, groundLayer, PropCollision.Trunk);
            }

            // A few real boulders for a lived-in hermit clearing (was sphere primitives at a fixed y that
            // floated on the sloped glade -- now real kit rocks through the gate: snapped + slope-conformed).
            for (int i = 0; i < 5; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rad = 4f + (float)rng.NextDouble() * (GladeRadius - 9f);
                PlaceMeshyProp(container, i % 2 == 0 ? "boulder_01" : "namaqualand_boulder_02",
                    center + new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad),
                    (float)rng.NextDouble() * 360f, 0.7f + (float)rng.NextDouble() * 0.8f, groundLayer, PropCollision.FitBox);
            }

            // The hermit's hearth — a warm campfire beside Aldric.
            var fireObj = new GameObject("GladeHearth");
            fireObj.transform.SetParent(container);
            fireObj.transform.position = center + new Vector3(-3.5f, 0f, 2.5f);
            // Real campfire mesh if present; no Sphere-primitive stone ring fallback (read as grey balls).
            PlaceMeshyProp(fireObj.transform, "Campfire", fireObj.transform.position, rng.Next(0, 360), 0.9f, groundLayer);
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

            // ---- DayNightCycle (singleton) — orbiting sun + moon, dawn/dusk, stars,
            // moonlight; owns the time-based lighting and blends the eruption tint on top.
            // NetworkBehaviour scene object: the SERVER owns the clock (SyncVar anchor) and
            // Mirror spawns it automatically on server start (sceneId baked with the scene). ----
            var dayNightObj = new GameObject("DayNightCycle");
            dayNightObj.AddComponent<Mirror.NetworkIdentity>();
            var dayNight = dayNightObj.AddComponent<World.DayNightCycle>();
            var sunForCycle = RenderSettings.sun != null ? RenderSettings.sun.gameObject : GameObject.Find("Sun");
            if (sunForCycle != null)
            {
                var sunLight = sunForCycle.GetComponent<Light>();
                if (sunLight != null)
                {
                    var so = new SerializedObject(dayNight);
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

            // (Removed the CityProgressBoard sign: it exposed "FLOOR 1 / the Cinderbound Guardian"
            // in the open world, which spoils the floor/boss meta the player must discover through
            // story. ServerProgressState above still tracks clears headlessly.)
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
            pos.y = GY(pos.x, pos.z);   // seat the ore node on the terrain (was fixed y=0 -> float/bury)
            var go = new GameObject($"OreNode_{label}");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            if (interactLayer >= 0) go.layer = interactLayer;

            var rock = PlaceMeshyProp(go.transform, "namaqualand_boulder_02", pos,
                (float)(rng.NextDouble() * 360), 2.4f, groundLayer, PropCollision.FitBox)
                ?? PlaceMeshyProp(go.transform, "boulder_01", pos,
                    (float)(rng.NextDouble() * 360), 2.4f, groundLayer, PropCollision.FitBox);
            if (rock != null)
                foreach (var renderer in rock.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = oreMat;

            var node = go.AddComponent<World.ResourceNode>();
            node.Configure("Mining", item, "pickaxe", level, 18f, 3f, 8, 20f);
            node.Prompt = $"Mine {label.ToLower()} (Mining {level})";
            node.Radius = 3.2f;
        }

        private static void BuildTreeNode(Transform parent, Vector3 pos, int interactLayer, int groundLayer)
        {
            pos.y = GY(pos.x, pos.z);   // seat the gather node on the terrain (was fixed y=0)
            var go = new GameObject("TreeNode_Oak");
            go.transform.SetParent(parent);
            go.transform.position = pos;
            if (interactLayer >= 0) go.layer = interactLayer;

            // Real kit tree as the choppable visual (no more cylinder+sphere lollipop). The trunk
            // collider comes from the kit prefab; the ResourceNode drives the chop interaction.
            var visual = PlaceMeshyProp(go.transform, "island_tree_01", pos,
                (float)(rng.NextDouble() * 360.0), 9f, groundLayer, PropCollision.Trunk);
            // Missing scan = no visual fallback; primitive trunks violate the environment bar.

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
            // Keep the marker on dry, terrain-snapped ground -- it was a fixed y=0 cube that could stand
            // in a river (the "random pillar in water"). Nudge out of water, then seat on the terrain.
            if (_place != null && (InLakeWater(pos.x, pos.z) || InAnyRiver(pos.x, pos.z)))
                for (int r = 4; r <= 30; r += 3)
                {
                    bool found = false;
                    for (int a = 0; a < 12; a++)
                    {
                        float ang = a * Mathf.PI / 6f;
                        float nx = pos.x + Mathf.Cos(ang) * r, nz = pos.z + Mathf.Sin(ang) * r;
                        if (_place.InBounds(nx, nz) && !InLakeWater(nx, nz) && !InAnyRiver(nx, nz)) { pos.x = nx; pos.z = nz; found = true; break; }
                    }
                    if (found) break;
                }
            pos.y = GY(pos.x, pos.z);

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
