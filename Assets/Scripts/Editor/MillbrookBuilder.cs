#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Unity.AI.Navigation;
using System.Collections.Generic;

namespace AldenFall.World.Editor
{
    /// <summary>
    /// Millbrook Castle City v3 - Dense Medieval Castle
    ///
    /// Based on Edinburgh-style castle city with prominent aqueducts.
    /// Grid: 30x30 tiles, 5 units per tile = 150x150 units
    ///
    /// Level 1 (Y=0): Ground, 100% coverage - courtyard, fountain, market
    /// Level 2 (Y=24): 75% coverage - solid floor platform with battlements
    /// Level 3 (Y=48): 45% coverage - upper district with keep
    /// </summary>
    public class MillbrookBuilder : EditorWindow
    {
        // ==================== CONSTANTS ====================

        // Grid (30% smaller than before)
        private const float TILE_SIZE = 5f;
        private const int GRID_SIZE = 30;
        private const float GROUND_HEIGHT = 0.3f;

        // Level heights (doubled for camera room)
        private const float LEVEL_1_Y = 0f;
        private const float LEVEL_2_Y = 24f;
        private const float LEVEL_3_Y = 48f;

        // Level coverage
        private const int LEVEL_2_RADIUS = 11;  // 22x22 tiles = 75%
        private const int LEVEL_3_RADIUS = 7;   // 14x14 tiles = 45%

        // Castle wall settings
        private const float WALL_HEIGHT = 20f;
        private const float WALL_THICKNESS = 3f;
        private const float BATTLEMENT_HEIGHT = 2f;
        private const float BATTLEMENT_WIDTH = 1.5f;
        private const float BATTLEMENT_SPACING = 3f;

        // Building settings
        private const float BUILDING_HEIGHT = 18f;
        private const float ARCHWAY_WIDTH = 4f;
        private const float ARCHWAY_HEIGHT = 5f;

        // Stairway settings
        private const float STAIR_WIDTH = 6f;
        private const float STAIR_STEP_HEIGHT = 0.5f;
        private const int STAIR_HOLE_SIZE = 4; // 4x4 tiles for stair openings

        // Aqueduct settings (prominent!)
        private const float AQUEDUCT_WIDTH = 3f;
        private const float AQUEDUCT_HEIGHT = 2f;
        private const float AQUEDUCT_SUPPORT_SPACING = 3f;

        // Water
        private const float WATER_DEPTH = 6f;

        // Static fields
        private static Dictionary<string, Material> materials;
        private static GameObject cityRoot;
        private static Shader workingShader;

        // Stairway positions for floor hole creation
        private static List<Vector2Int> stairwayPositions = new List<Vector2Int>();

        private static int CenterTile => GRID_SIZE / 2; // = 15
        private static float HalfGrid => (GRID_SIZE * TILE_SIZE) / 2f;

        // Color palette
        private static readonly Dictionary<string, Color> palette = new Dictionary<string, Color>
        {
            // Ground and floors
            { "Grass", new Color(0.28f, 0.52f, 0.28f) },
            { "Stone", new Color(0.55f, 0.55f, 0.58f) },
            { "Cobblestone", new Color(0.48f, 0.48f, 0.50f) },
            { "Level2Floor", new Color(0.58f, 0.52f, 0.45f) },
            { "Level3Floor", new Color(0.65f, 0.60f, 0.55f) },

            // Castle walls
            { "CastleWall", new Color(0.50f, 0.48f, 0.45f) },
            { "Battlement", new Color(0.52f, 0.50f, 0.48f) },

            // Water
            { "Water", new Color(0.15f, 0.45f, 0.75f, 0.7f) },
            { "WaterDeep", new Color(0.1f, 0.3f, 0.6f, 0.8f) },
            { "Waterfall", new Color(0.6f, 0.8f, 0.95f, 0.6f) },

            // Aqueduct
            { "Aqueduct", new Color(0.52f, 0.50f, 0.46f) },
            { "AqueductWater", new Color(0.2f, 0.5f, 0.8f, 0.8f) },

            // Buildings
            { "Building", new Color(0.55f, 0.50f, 0.45f) },
            { "Tavern", new Color(0.65f, 0.45f, 0.30f) },
            { "Guild", new Color(0.50f, 0.42f, 0.58f) },
            { "Smithy", new Color(0.38f, 0.38f, 0.42f) },
            { "Market", new Color(0.60f, 0.55f, 0.40f) },
            { "Keep", new Color(0.45f, 0.43f, 0.42f) },

            // Details
            { "Roof", new Color(0.55f, 0.32f, 0.25f) },
            { "Wood", new Color(0.48f, 0.35f, 0.25f) },
            { "Window", new Color(0.65f, 0.80f, 0.92f, 0.75f) },
            { "Tree", new Color(0.22f, 0.48f, 0.25f) },
            { "TreeTrunk", new Color(0.42f, 0.30f, 0.20f) },
            { "Bridge", new Color(0.50f, 0.45f, 0.40f) },
            { "Stairs", new Color(0.55f, 0.53f, 0.50f) },
            { "Metal", new Color(0.35f, 0.35f, 0.38f) },
            { "Lamp", new Color(1f, 0.88f, 0.65f) },
        };

        // ==================== MENU ITEMS ====================

        [MenuItem("AldenFall/Advanced/Build City Only", priority = 100)]
        public static void BuildMillbrookWithDialog()
        {
            if (!EditorUtility.DisplayDialog("Build Millbrook Castle City",
                "This will create a dense 3-level castle city with aqueducts, waterfall, and swimming areas.\n\n" +
                "Size: 150x150 units\nLevels: Y=0, Y=24, Y=48\n\nContinue?",
                "Build", "Cancel"))
            {
                return;
            }

            BuildMillbrook();
        }

        public static void BuildMillbrook()
        {
            float startTime = Time.realtimeSinceStartup;
            stairwayPositions.Clear();

            CreateMaterials();
            CreateCityRoot();

            // Define stairway positions first (needed for floor hole creation)
            DefineStairwayPositions();

            // Build levels
            BuildLevel1_Ground();
            BuildLevel2_MidCity();
            BuildLevel3_Upper();

            // Build connections
            BuildGrandStaircases();

            // Build castle features
            BuildCastleWalls();
            BuildAqueducts();
            BuildWaterfall();

            // Add details
            BuildTrees();
            BuildLamps();

            // NavMesh
            AddNavMesh();

            float buildTime = Time.realtimeSinceStartup - startTime;
            Selection.activeGameObject = cityRoot;
            Debug.Log($"Millbrook Castle City built in {buildTime:F2}s! 3 levels with aqueducts.");
        }

        [MenuItem("AldenFall/Advanced/Clear City Only", priority = 101)]
        public static void ClearMillbrook()
        {
            var existing = GameObject.Find("Millbrook_City");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
                Debug.Log("Millbrook city cleared.");
            }
        }

        // ==================== SETUP ====================

        private static Shader GetWorkingShader()
        {
            if (workingShader != null) return workingShader;
            GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            workingShader = tempCube.GetComponent<MeshRenderer>().sharedMaterial.shader;
            Object.DestroyImmediate(tempCube);
            return workingShader;
        }

        private static void CreateMaterials()
        {
            materials = new Dictionary<string, Material>();
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/City");

            foreach (var kvp in palette)
            {
                string path = $"Assets/Materials/City/{kvp.Key}.mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (mat == null)
                {
                    mat = new Material(GetWorkingShader());
                    mat.name = kvp.Key;

                    if (kvp.Value.a < 1f)
                    {
                        mat.SetFloat("_Surface", 1);
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        mat.SetInt("_ZWrite", 0);
                        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        mat.renderQueue = 3000;
                    }

                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", kvp.Value);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", kvp.Value);
                    mat.color = kvp.Value;

                    AssetDatabase.CreateAsset(mat, path);
                }
                materials[kvp.Key] = mat;
            }
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }
        }

        private static void CreateCityRoot()
        {
            ClearMillbrook();

            cityRoot = new GameObject("Millbrook_City");
            cityRoot.transform.position = Vector3.zero;

            string[] containers = {
                "Level1", "Level2", "Level3",
                "Walls", "Stairs", "Water", "Aqueducts",
                "Trees", "Lamps", "Details"
            };
            foreach (var name in containers)
            {
                new GameObject(name).transform.SetParent(cityRoot.transform);
            }

            Undo.RegisterCreatedObjectUndo(cityRoot, "Create Millbrook");
        }

        private static Transform GetContainer(string name) => cityRoot.transform.Find(name);

        private static Vector3 TileToWorld(int x, int z, float y = 0)
        {
            return new Vector3(
                (x - CenterTile) * TILE_SIZE,
                y,
                (z - CenterTile) * TILE_SIZE
            );
        }

        private static Vector3 TileToWorld(float x, float z, float y = 0)
        {
            return new Vector3(
                (x - CenterTile) * TILE_SIZE,
                y,
                (z - CenterTile) * TILE_SIZE
            );
        }

        // ==================== STAIRWAY POSITIONS ====================

        private static void DefineStairwayPositions()
        {
            // L1→L2 stairs at 4 corners (based on user's map: tiles 1-3 and 26-28 for x/z)
            stairwayPositions.Add(new Vector2Int(1, 8));   // SW
            stairwayPositions.Add(new Vector2Int(1, 20));  // NW
            stairwayPositions.Add(new Vector2Int(26, 8));  // SE
            stairwayPositions.Add(new Vector2Int(26, 20)); // NE

            // L2→L3 stairs (BOTH east and west for better access)
            stairwayPositions.Add(new Vector2Int(21, CenterTile)); // East L2→L3
            stairwayPositions.Add(new Vector2Int(9, CenterTile));  // West L2→L3
        }

        private static bool IsNearStairway(int x, int z, float level)
        {
            foreach (var pos in stairwayPositions)
            {
                int dx = Mathf.Abs(x - pos.x);
                int dz = Mathf.Abs(z - pos.y);

                // Check if this stairway is relevant for this level
                bool isL1L2Stair = pos.y <= 20 && pos.y >= 8 && (pos.x <= 3 || pos.x >= 25);
                bool isL2L3Stair = (pos.x == 21 || pos.x == 9) && pos.y == CenterTile;

                if (level == LEVEL_2_Y && isL1L2Stair && dx <= STAIR_HOLE_SIZE && dz <= STAIR_HOLE_SIZE)
                    return true;
                if (level == LEVEL_3_Y && isL2L3Stair && dx <= STAIR_HOLE_SIZE && dz <= STAIR_HOLE_SIZE)
                    return true;
            }
            return false;
        }

        // ==================== LEVEL 1 - GROUND ====================

        private static void BuildLevel1_Ground()
        {
            var container = GetContainer("Level1");
            var waterContainer = GetContainer("Water");
            int groundLayer = LayerMask.NameToLayer("Ground");

            // Build ground tiles (based on user map: 30x30, with walls at edges)
            for (int x = 1; x < GRID_SIZE - 1; x++)
            {
                for (int z = 1; z < GRID_SIZE - 1; z++)
                {
                    // Skip fountain area (tiles 7-17 for x, 15-19 for z based on map)
                    bool isFountain = x >= 7 && x <= 17 && z >= 15 && z <= 19;
                    if (isFountain) continue;

                    Vector3 pos = TileToWorld(x, z, GROUND_HEIGHT / 2f);
                    string mat = GetGroundMaterial(x, z);

                    var tile = CreateCube($"Ground_{x}_{z}", pos,
                        new Vector3(TILE_SIZE, GROUND_HEIGHT, TILE_SIZE), mat, container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            // === UNIFIED WATER POOL (Large, swimmable, includes fountain + waterfall area) ===
            // Single cohesive water volume covering the entire swimmable area
            Vector3 fountainCenter = TileToWorld(12, 17, 0);
            int waterLayerIndex = LayerMask.NameToLayer("Water");

            // Main water volume - extends from fountain to waterfall landing
            // Wider to include waterfall area (tiles 13-17 for z, 10-20 for x)
            var mainWaterPool = CreateCube("MainWaterPool", fountainCenter + Vector3.up * (-WATER_DEPTH / 2 + 0.5f),
                new Vector3(TILE_SIZE * 10, WATER_DEPTH, TILE_SIZE * 6), "WaterDeep", waterContainer);
            mainWaterPool.GetComponent<BoxCollider>().isTrigger = true;
            if (waterLayerIndex >= 0) mainWaterPool.layer = waterLayerIndex;
            // NOTE: Do NOT set tag - rely only on layer for detection (prevents tag errors)

            // Visual water surface plane (slightly above trigger volume to prevent z-fighting)
            var waterSurface = CreateCube("WaterSurface", fountainCenter + Vector3.up * 0.35f,
                new Vector3(TILE_SIZE * 10 - 0.5f, 0.1f, TILE_SIZE * 6 - 0.5f), "Water", waterContainer);
            // Remove collider from surface - it's visual only
            Object.DestroyImmediate(waterSurface.GetComponent<BoxCollider>());

            // Central fountain pillar
            CreateCube("Fountain_Pillar", fountainCenter + Vector3.up * 4f,
                new Vector3(2f, 8f, 2f), "Stone", container);

            // === BUILDINGS (based on user map) ===

            // Tavern (West, tiles 3-9 x, 23-26 z) - 7x4 with archway
            BuildCastleBuilding("Tavern", TileToWorld(6, 24.5f, 0), 7, 4, BUILDING_HEIGHT, "Tavern", container, true, 0);

            // Guild (East, tiles 20-26 x, 23-26 z) - 7x4 with archway
            BuildCastleBuilding("Guild", TileToWorld(23, 24.5f, 0), 7, 4, BUILDING_HEIGHT, "Guild", container, true, 180);

            // Smithy (SW, tiles 3-7 x, 5-7 z)
            BuildCastleBuilding("Smithy", TileToWorld(5, 6, 0), 5, 3, BUILDING_HEIGHT * 0.8f, "Smithy", container, true, 0);

            // Market (SE, tiles 20-24 x, 5-7 z)
            BuildCastleBuilding("Market", TileToWorld(22, 6, 0), 5, 3, BUILDING_HEIGHT * 0.8f, "Market", container, true, 180);

            // Bridge connecting Smithy and Market (tiles 8-19 x, row 6)
            BuildBridge("SmithyMarketBridge", TileToWorld(13.5f, 6, 0), 12, container);

            // Trees at corners (based on # symbols in map)
            var treeContainer = GetContainer("Trees");
            CreateTree("Tree_NW1", TileToWorld(2, 27, 0), 6f, treeContainer);
            CreateTree("Tree_NE1", TileToWorld(27, 27, 0), 6f, treeContainer);
            CreateTree("Tree_SW1", TileToWorld(2, 3, 0), 5f, treeContainer);
            CreateTree("Tree_SE1", TileToWorld(27, 3, 0), 5f, treeContainer);
            CreateTree("Tree_NW2", TileToWorld(2, 12, 0), 5f, treeContainer);
            CreateTree("Tree_SE2", TileToWorld(27, 12, 0), 5f, treeContainer);
        }

        private static string GetGroundMaterial(int x, int z)
        {
            // Roads near gates (center of each side)
            bool isNSRoad = Mathf.Abs(x - CenterTile) <= 2;
            bool isEWRoad = Mathf.Abs(z - CenterTile) <= 2;

            if (isNSRoad || isEWRoad) return "Cobblestone";

            // Near fountain
            if (Mathf.Abs(x - 12) <= 6 && Mathf.Abs(z - 17) <= 4) return "Stone";

            return "Grass";
        }

        // ==================== LEVEL 2 - MID CITY ====================

        private static void BuildLevel2_MidCity()
        {
            var container = GetContainer("Level2");
            int groundLayer = LayerMask.NameToLayer("Ground");

            // Level 2 covers tiles 4-25 (22x22 = 75%)
            int minTile = CenterTile - LEVEL_2_RADIUS; // 4
            int maxTile = CenterTile + LEVEL_2_RADIUS; // 26

            for (int x = minTile; x <= maxTile; x++)
            {
                for (int z = minTile; z <= maxTile; z++)
                {
                    // Skip stairway holes
                    if (IsNearStairway(x, z, LEVEL_2_Y)) continue;

                    // Skip waterfall hole (center-north area)
                    bool isWaterfallHole = Mathf.Abs(x - CenterTile) <= 2 && z >= CenterTile + 3 && z <= maxTile - 1;
                    if (isWaterfallHole) continue;

                    // Skip pool area - creates hole for sink-through feature to L1
                    bool isPoolHole = (x >= 8 && x <= 9 && z >= CenterTile && z <= CenterTile + 1);
                    if (isPoolHole) continue;

                    Vector3 pos = TileToWorld(x, z, LEVEL_2_Y);
                    var tile = CreateCube($"L2Floor_{x}_{z}", pos + Vector3.up * 0.15f,
                        new Vector3(TILE_SIZE, 0.3f, TILE_SIZE), "Level2Floor", container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            // Level 2 buildings
            // Inn (center area per map)
            BuildCastleBuilding("L2_Inn", TileToWorld(CenterTile, CenterTile + 3, LEVEL_2_Y), 5, 3,
                BUILDING_HEIGHT * 0.7f, "Building", container, false, 0);

            // Armory (south area per map)
            BuildCastleBuilding("L2_Armory", TileToWorld(CenterTile, CenterTile - 6, LEVEL_2_Y), 5, 3,
                BUILDING_HEIGHT * 0.7f, "Smithy", container, false, 180);

            // Bathhouse/Sink-through pool (west area: tiles 8-9, 15-16)
            // This pool has NO floor beneath - players sink and fall to L1 fountain
            var waterContainer = GetContainer("Water");
            int waterLayer = LayerMask.NameToLayer("Water");

            var bathPool = CreateCube("L2_SinkPool", TileToWorld(8.5f, CenterTile + 0.5f, LEVEL_2_Y + 0.5f),
                new Vector3(TILE_SIZE * 2, 2f, TILE_SIZE * 2), "Water", waterContainer);
            bathPool.GetComponent<BoxCollider>().isTrigger = true;
            if (waterLayer >= 0) bathPool.layer = waterLayer;
            // NOTE: Do NOT set tag - rely only on layer for detection

            // Pool edge railings (safety but jumpable)
            CreateCube("L2_PoolRail_N", TileToWorld(8.5f, CenterTile + 1.5f, LEVEL_2_Y + 1f),
                new Vector3(TILE_SIZE * 2, 1.5f, 0.3f), "Stone", container);
            CreateCube("L2_PoolRail_S", TileToWorld(8.5f, CenterTile - 0.5f, LEVEL_2_Y + 1f),
                new Vector3(TILE_SIZE * 2, 1.5f, 0.3f), "Stone", container);
            CreateCube("L2_PoolRail_W", TileToWorld(7.5f, CenterTile + 0.5f, LEVEL_2_Y + 1f),
                new Vector3(0.3f, 1.5f, TILE_SIZE * 2), "Stone", container);
            CreateCube("L2_PoolRail_E", TileToWorld(9.5f, CenterTile + 0.5f, LEVEL_2_Y + 1f),
                new Vector3(0.3f, 1.5f, TILE_SIZE * 2), "Stone", container);

            // L2 battlements around edge
            BuildBattlements(minTile, maxTile, LEVEL_2_Y, container);

            // Trees on L2
            var treeContainer = GetContainer("Trees");
            CreateTree("L2_Tree1", TileToWorld(6, CenterTile + 8, LEVEL_2_Y), 4f, treeContainer);
            CreateTree("L2_Tree2", TileToWorld(24, CenterTile - 6, LEVEL_2_Y), 4f, treeContainer);
            CreateTree("L2_Tree3", TileToWorld(6, CenterTile - 6, LEVEL_2_Y), 4f, treeContainer);
            CreateTree("L2_Tree4", TileToWorld(24, CenterTile + 8, LEVEL_2_Y), 4f, treeContainer);
        }

        private static void BuildBattlements(int minTile, int maxTile, float y, Transform container)
        {
            // North and south edges
            for (int x = minTile; x <= maxTile; x += 2)
            {
                // North
                CreateCube($"Battlement_N_{x}", TileToWorld(x, maxTile, y) + new Vector3(0, BATTLEMENT_HEIGHT / 2 + 0.3f, 0),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
                // South
                CreateCube($"Battlement_S_{x}", TileToWorld(x, minTile, y) + new Vector3(0, BATTLEMENT_HEIGHT / 2 + 0.3f, 0),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
            }

            // East and west edges
            for (int z = minTile; z <= maxTile; z += 2)
            {
                // East
                CreateCube($"Battlement_E_{z}", TileToWorld(maxTile, z, y) + new Vector3(0, BATTLEMENT_HEIGHT / 2 + 0.3f, 0),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
                // West
                CreateCube($"Battlement_W_{z}", TileToWorld(minTile, z, y) + new Vector3(0, BATTLEMENT_HEIGHT / 2 + 0.3f, 0),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
            }
        }

        // ==================== LEVEL 3 - UPPER DISTRICT ====================

        private static void BuildLevel3_Upper()
        {
            var container = GetContainer("Level3");
            int groundLayer = LayerMask.NameToLayer("Ground");

            // Level 3 covers tiles 8-21 (14x14 = 45%) - structured noble district
            int minTile = CenterTile - LEVEL_3_RADIUS; // 8
            int maxTile = CenterTile + LEVEL_3_RADIUS; // 22

            // Keep is smaller (7x3 tiles) centered at tiles 12-18 x, 14-17 z
            int keepMinX = CenterTile - 3;   // 12
            int keepMaxX = CenterTile + 3;   // 18
            int keepMinZ = CenterTile - 1;   // 14
            int keepMaxZ = CenterTile + 2;   // 17

            for (int x = minTile; x <= maxTile; x++)
            {
                for (int z = minTile; z <= maxTile; z++)
                {
                    // Skip stairway holes (BOTH east and west sides)
                    bool isEastStair = (x >= 20 && x <= 22 && z >= CenterTile - 2 && z <= CenterTile + 2);
                    bool isWestStair = (x >= 8 && x <= 10 && z >= CenterTile - 2 && z <= CenterTile + 2);
                    if (isEastStair || isWestStair) continue;

                    // Skip waterfall source (north edge center)
                    bool isWaterfallSource = Mathf.Abs(x - CenterTile) <= 1 && z >= maxTile - 2;
                    if (isWaterfallSource) continue;

                    // Skip Keep footprint
                    bool isKeepArea = (x >= keepMinX && x <= keepMaxX && z >= keepMinZ && z <= keepMaxZ);
                    if (isKeepArea) continue;

                    Vector3 pos = TileToWorld(x, z, LEVEL_3_Y);
                    var tile = CreateCube($"L3Floor_{x}_{z}", pos + Vector3.up * 0.15f,
                        new Vector3(TILE_SIZE, 0.3f, TILE_SIZE), "Level3Floor", container);
                    tile.isStatic = true;
                    if (groundLayer >= 0) tile.layer = groundLayer;
                }
            }

            // L3 edge battlements
            BuildBattlements(minTile, maxTile, LEVEL_3_Y, container);

            // === VIEWING PLATFORMS at corners (elevated 2 units with railings) ===
            BuildViewingPlatform("L3_ViewNE", TileToWorld(maxTile - 1, maxTile - 1, LEVEL_3_Y), container, groundLayer);
            BuildViewingPlatform("L3_ViewNW", TileToWorld(minTile + 1, maxTile - 1, LEVEL_3_Y), container, groundLayer);
            BuildViewingPlatform("L3_ViewSE", TileToWorld(maxTile - 1, minTile + 1, LEVEL_3_Y), container, groundLayer);
            BuildViewingPlatform("L3_ViewSW", TileToWorld(minTile + 1, minTile + 1, LEVEL_3_Y), container, groundLayer);

            // === KEEP - Vista Tower with exterior spiral staircase ===
            Vector3 keepPos = TileToWorld(CenterTile, CenterTile + 0.5f, LEVEL_3_Y);
            BuildKeepVistaTower(keepPos, container, groundLayer);

            // === SYMMETRICAL GARDENS ===
            var treeContainer = GetContainer("Trees");
            // Trees flanking the path to Keep (symmetrical)
            CreateTree("L3_Tree_PathL1", TileToWorld(minTile + 3, CenterTile, LEVEL_3_Y), 3f, treeContainer);
            CreateTree("L3_Tree_PathR1", TileToWorld(maxTile - 3, CenterTile, LEVEL_3_Y), 3f, treeContainer);
            CreateTree("L3_Tree_PathL2", TileToWorld(minTile + 3, CenterTile + 4, LEVEL_3_Y), 3f, treeContainer);
            CreateTree("L3_Tree_PathR2", TileToWorld(maxTile - 3, CenterTile + 4, LEVEL_3_Y), 3f, treeContainer);
            // Rear gardens behind Keep
            CreateTree("L3_Tree_Rear1", TileToWorld(CenterTile - 2, maxTile - 3, LEVEL_3_Y), 2.5f, treeContainer);
            CreateTree("L3_Tree_Rear2", TileToWorld(CenterTile + 2, maxTile - 3, LEVEL_3_Y), 2.5f, treeContainer);

            // === LAMPS along pathways ===
            var lampContainer = GetContainer("Lamps");
            CreateLamp("L3_Lamp_W", TileToWorld(minTile + 2, CenterTile, LEVEL_3_Y), lampContainer);
            CreateLamp("L3_Lamp_E", TileToWorld(maxTile - 2, CenterTile, LEVEL_3_Y), lampContainer);
            CreateLamp("L3_Lamp_Keep1", TileToWorld(CenterTile - 4, CenterTile, LEVEL_3_Y), lampContainer);
            CreateLamp("L3_Lamp_Keep2", TileToWorld(CenterTile + 4, CenterTile, LEVEL_3_Y), lampContainer);
        }

        private static void BuildViewingPlatform(string name, Vector3 pos, Transform container, int groundLayer)
        {
            // Elevated platform (2 units up) with railings
            float platformY = 2f;
            var platform = CreateCube(name + "_Floor", pos + Vector3.up * platformY,
                new Vector3(TILE_SIZE * 2, 0.3f, TILE_SIZE * 2), "Level3Floor", container);
            platform.isStatic = true;
            if (groundLayer >= 0) platform.layer = groundLayer;

            // Steps up to platform
            for (int i = 0; i < 4; i++)
            {
                var step = CreateCube($"{name}_Step{i}", pos + new Vector3(0, 0.5f * i, -TILE_SIZE * 0.5f - i * 0.4f),
                    new Vector3(TILE_SIZE, 0.5f, 1f), "Stairs", container);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;
            }

            // Railings
            CreateCube(name + "_RailN", pos + new Vector3(0, platformY + 1f, TILE_SIZE * 0.9f),
                new Vector3(TILE_SIZE * 2, 1.5f, 0.2f), "Stone", container);
            CreateCube(name + "_RailS", pos + new Vector3(0, platformY + 1f, -TILE_SIZE * 0.9f),
                new Vector3(TILE_SIZE * 2, 1.5f, 0.2f), "Stone", container);
            CreateCube(name + "_RailE", pos + new Vector3(TILE_SIZE * 0.9f, platformY + 1f, 0),
                new Vector3(0.2f, 1.5f, TILE_SIZE * 2), "Stone", container);
            CreateCube(name + "_RailW", pos + new Vector3(-TILE_SIZE * 0.9f, platformY + 1f, 0),
                new Vector3(0.2f, 1.5f, TILE_SIZE * 2), "Stone", container);
        }

        private static void BuildKeepVistaTower(Vector3 pos, Transform container, int groundLayer)
        {
            // Keep dimensions - smaller than before, hollow with open front
            float keepWidth = 7 * TILE_SIZE;   // 35 units
            float keepDepth = 3 * TILE_SIZE;   // 15 units
            float keepHeight = 20f;

            // === KEEP WALLS (hollow, open front) ===
            // Back wall
            CreateCube("Keep_Back", pos + new Vector3(0, keepHeight / 2, keepDepth / 2 - 0.5f),
                new Vector3(keepWidth, keepHeight, 1f), "Keep", container);

            // Side walls
            CreateCube("Keep_Left", pos + new Vector3(-keepWidth / 2 + 0.5f, keepHeight / 2, 0),
                new Vector3(1f, keepHeight, keepDepth), "Keep", container);
            CreateCube("Keep_Right", pos + new Vector3(keepWidth / 2 - 0.5f, keepHeight / 2, 0),
                new Vector3(1f, keepHeight, keepDepth), "Keep", container);

            // Front - just pillars, archway entrance in center
            CreateCube("Keep_FrontL", pos + new Vector3(-keepWidth / 2 + 3, keepHeight / 2, -keepDepth / 2 + 0.5f),
                new Vector3(5f, keepHeight, 1f), "Keep", container);
            CreateCube("Keep_FrontR", pos + new Vector3(keepWidth / 2 - 3, keepHeight / 2, -keepDepth / 2 + 0.5f),
                new Vector3(5f, keepHeight, 1f), "Keep", container);
            // Archway top
            CreateCube("Keep_FrontTop", pos + new Vector3(0, keepHeight - 3f, -keepDepth / 2 + 0.5f),
                new Vector3(keepWidth - 10f, 6f, 1f), "Keep", container);

            // Keep interior floor
            var keepFloor = CreateCube("Keep_IntFloor", pos + Vector3.up * 0.2f,
                new Vector3(keepWidth - 2f, 0.4f, keepDepth - 2f), "Stone", container);
            keepFloor.isStatic = true;
            if (groundLayer >= 0) keepFloor.layer = groundLayer;

            // === EXTERIOR SPIRAL STAIRCASE (Vista-style, wraps around Keep) ===
            float spiralRadius = keepWidth / 2 + 3f;  // Outside the Keep walls
            int numSteps = 50;  // Enough to reach observation deck
            float totalHeight = keepHeight + 4f;  // Above Keep walls

            for (int i = 0; i < numSteps; i++)
            {
                float y = 1f + i * (totalHeight / numSteps);
                float angle = i * 15f * Mathf.Deg2Rad;  // 24 steps per full rotation
                float x = Mathf.Cos(angle) * spiralRadius;
                float z = Mathf.Sin(angle) * spiralRadius;

                var step = CreateCube($"Keep_Spiral_{i}", pos + new Vector3(x, y, z),
                    new Vector3(3.5f, 0.5f, 2f), "Stairs", container);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;

                // Railing post every 6 steps
                if (i % 6 == 0)
                {
                    CreateCube($"Keep_SpiralRail_{i}", pos + new Vector3(x * 1.15f, y + 1.2f, z * 1.15f),
                        new Vector3(0.4f, 2.5f, 0.4f), "Metal", container);
                }
            }

            // === OBSERVATION DECK at top ===
            var obsDeck = CreateCube("Keep_ObsDeck", pos + Vector3.up * (keepHeight + 2f),
                new Vector3(keepWidth + 6f, 0.5f, keepDepth + 6f), "Level3Floor", container);
            obsDeck.isStatic = true;
            if (groundLayer >= 0) obsDeck.layer = groundLayer;

            // Observation deck battlements/railings (GW2 Vista style lookout)
            for (float bx = -keepWidth / 2 - 2; bx <= keepWidth / 2 + 2; bx += 4f)
            {
                CreateCube($"Keep_ObsBatt_N_{bx}", pos + new Vector3(bx, keepHeight + 3.5f, keepDepth / 2 + 2.5f),
                    new Vector3(1.5f, 2.5f, 0.5f), "Battlement", container);
                CreateCube($"Keep_ObsBatt_S_{bx}", pos + new Vector3(bx, keepHeight + 3.5f, -keepDepth / 2 - 2.5f),
                    new Vector3(1.5f, 2.5f, 0.5f), "Battlement", container);
            }
            for (float bz = -keepDepth / 2; bz <= keepDepth / 2; bz += 4f)
            {
                CreateCube($"Keep_ObsBatt_E_{bz}", pos + new Vector3(keepWidth / 2 + 2.5f, keepHeight + 3.5f, bz),
                    new Vector3(0.5f, 2.5f, 1.5f), "Battlement", container);
                CreateCube($"Keep_ObsBatt_W_{bz}", pos + new Vector3(-keepWidth / 2 - 2.5f, keepHeight + 3.5f, bz),
                    new Vector3(0.5f, 2.5f, 1.5f), "Battlement", container);
            }

            // Vista beacon/marker at top (like GW2 vista points)
            CreateCube("Keep_VistaMarker", pos + Vector3.up * (keepHeight + 5f),
                new Vector3(1f, 3f, 1f), "Lamp", container);
        }

        // ==================== CASTLE WALLS ====================

        private static void BuildCastleWalls()
        {
            var container = GetContainer("Walls");

            // Outer walls with gates (based on map: W around edge, G at centers)
            for (int i = 1; i < GRID_SIZE - 1; i++)
            {
                bool isGate = Mathf.Abs(i - CenterTile) <= 2;

                if (!isGate)
                {
                    // North wall
                    CreateCube($"Wall_N_{i}", TileToWorld(i, GRID_SIZE - 1, WALL_HEIGHT / 2),
                        new Vector3(TILE_SIZE, WALL_HEIGHT, WALL_THICKNESS), "CastleWall", container);
                    // South wall
                    CreateCube($"Wall_S_{i}", TileToWorld(i, 0, WALL_HEIGHT / 2),
                        new Vector3(TILE_SIZE, WALL_HEIGHT, WALL_THICKNESS), "CastleWall", container);
                    // East wall
                    CreateCube($"Wall_E_{i}", TileToWorld(GRID_SIZE - 1, i, WALL_HEIGHT / 2),
                        new Vector3(WALL_THICKNESS, WALL_HEIGHT, TILE_SIZE), "CastleWall", container);
                    // West wall
                    CreateCube($"Wall_W_{i}", TileToWorld(0, i, WALL_HEIGHT / 2),
                        new Vector3(WALL_THICKNESS, WALL_HEIGHT, TILE_SIZE), "CastleWall", container);
                }
            }

            // Wall top battlements
            for (int i = 2; i < GRID_SIZE - 2; i += 3)
            {
                CreateCube($"WallBatt_N_{i}", TileToWorld(i, GRID_SIZE - 1, WALL_HEIGHT + 1),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
                CreateCube($"WallBatt_S_{i}", TileToWorld(i, 0, WALL_HEIGHT + 1),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
                CreateCube($"WallBatt_E_{i}", TileToWorld(GRID_SIZE - 1, i, WALL_HEIGHT + 1),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
                CreateCube($"WallBatt_W_{i}", TileToWorld(0, i, WALL_HEIGHT + 1),
                    new Vector3(BATTLEMENT_WIDTH, BATTLEMENT_HEIGHT, BATTLEMENT_WIDTH), "Battlement", container);
            }

            // Corner towers
            float towerHeight = WALL_HEIGHT * 1.3f;
            CreateCube("Tower_NW", TileToWorld(0, GRID_SIZE - 1, towerHeight / 2),
                new Vector3(TILE_SIZE * 1.5f, towerHeight, TILE_SIZE * 1.5f), "CastleWall", container);
            CreateCube("Tower_NE", TileToWorld(GRID_SIZE - 1, GRID_SIZE - 1, towerHeight / 2),
                new Vector3(TILE_SIZE * 1.5f, towerHeight, TILE_SIZE * 1.5f), "CastleWall", container);
            CreateCube("Tower_SW", TileToWorld(0, 0, towerHeight / 2),
                new Vector3(TILE_SIZE * 1.5f, towerHeight, TILE_SIZE * 1.5f), "CastleWall", container);
            CreateCube("Tower_SE", TileToWorld(GRID_SIZE - 1, 0, towerHeight / 2),
                new Vector3(TILE_SIZE * 1.5f, towerHeight, TILE_SIZE * 1.5f), "CastleWall", container);

            // Gate archways
            CreateGateArchway("Gate_N", TileToWorld(CenterTile, GRID_SIZE - 1, 0), 0, container);
            CreateGateArchway("Gate_S", TileToWorld(CenterTile, 0, 0), 180, container);
            CreateGateArchway("Gate_E", TileToWorld(GRID_SIZE - 1, CenterTile, 0), -90, container);
            CreateGateArchway("Gate_W", TileToWorld(0, CenterTile, 0), 90, container);
        }

        private static void CreateGateArchway(string name, Vector3 pos, float rotation, Transform container)
        {
            // Gate pillars
            float pillarWidth = TILE_SIZE * 0.8f;
            float archHeight = WALL_HEIGHT * 0.8f;

            var parent = new GameObject(name);
            parent.transform.SetParent(container);
            parent.transform.position = pos;
            parent.transform.rotation = Quaternion.Euler(0, rotation, 0);

            // Left pillar
            CreateCube(name + "_PillarL", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(-TILE_SIZE * 1.5f, archHeight / 2, 0),
                new Vector3(pillarWidth, archHeight, pillarWidth), "CastleWall", container);
            // Right pillar
            CreateCube(name + "_PillarR", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(TILE_SIZE * 1.5f, archHeight / 2, 0),
                new Vector3(pillarWidth, archHeight, pillarWidth), "CastleWall", container);
            // Top arch
            CreateCube(name + "_Arch", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(0, archHeight + 2, 0),
                new Vector3(TILE_SIZE * 5, 4f, pillarWidth), "CastleWall", container);
        }

        // ==================== STAIRCASES ====================

        private static void BuildGrandStaircases()
        {
            var container = GetContainer("Stairs");

            // L1 → L2: Four grand staircases at corners (based on S tiles in map)
            // SW stairs (tiles 1-3, 8-10)
            BuildStaircase("Stairs_L1L2_SW", TileToWorld(1, 9, 0), LEVEL_1_Y, LEVEL_2_Y, 45, container);

            // SE stairs (tiles 26-28, 8-10)
            BuildStaircase("Stairs_L1L2_SE", TileToWorld(27, 9, 0), LEVEL_1_Y, LEVEL_2_Y, -45, container);

            // NW stairs (tiles 1-3, 20-22)
            BuildStaircase("Stairs_L1L2_NW", TileToWorld(1, 21, 0), LEVEL_1_Y, LEVEL_2_Y, 135, container);

            // NE stairs (tiles 26-28, 20-22)
            BuildStaircase("Stairs_L1L2_NE", TileToWorld(27, 21, 0), LEVEL_1_Y, LEVEL_2_Y, -135, container);

            // L2 → L3: Both east and west sides for better L3 access
            BuildStaircase("Stairs_L2L3_E", TileToWorld(21, CenterTile, LEVEL_2_Y), LEVEL_2_Y, LEVEL_3_Y, -90, container);
            BuildStaircase("Stairs_L2L3_W", TileToWorld(9, CenterTile, LEVEL_2_Y), LEVEL_2_Y, LEVEL_3_Y, 90, container);
        }

        private static void BuildStaircase(string name, Vector3 basePos, float startY, float endY, float rotation, Transform container)
        {
            float height = endY - startY;
            int numSteps = Mathf.CeilToInt(height / STAIR_STEP_HEIGHT);
            float totalLength = TILE_SIZE * 5;
            float stepDepth = totalLength / numSteps;

            int groundLayer = LayerMask.NameToLayer("Ground");

            for (int i = 0; i < numSteps; i++)
            {
                float y = startY + (i + 0.5f) * STAIR_STEP_HEIGHT;
                float forwardOffset = i * stepDepth;

                Vector3 stepLocalPos = new Vector3(0, y, forwardOffset);
                Vector3 rotatedPos = basePos + Quaternion.Euler(0, rotation, 0) * stepLocalPos;

                var step = CreateCube($"{name}_Step{i}", rotatedPos,
                    new Vector3(STAIR_WIDTH, STAIR_STEP_HEIGHT, stepDepth * 1.1f), "Stairs", container);
                step.transform.rotation = Quaternion.Euler(0, rotation, 0);
                step.isStatic = true;
                if (groundLayer >= 0) step.layer = groundLayer;
            }

            // Side railings
            for (int i = 0; i < numSteps; i += 5)
            {
                float y = startY + i * STAIR_STEP_HEIGHT + 1f;
                float forwardOffset = i * stepDepth;

                Vector3 leftOffset = Quaternion.Euler(0, rotation, 0) * new Vector3(-STAIR_WIDTH / 2 - 0.3f, 0, 0);
                Vector3 rightOffset = Quaternion.Euler(0, rotation, 0) * new Vector3(STAIR_WIDTH / 2 + 0.3f, 0, 0);
                Vector3 forwardVec = Quaternion.Euler(0, rotation, 0) * new Vector3(0, 0, forwardOffset);

                CreateCube($"{name}_RailL{i}", basePos + forwardVec + leftOffset + Vector3.up * y,
                    new Vector3(0.3f, 2f, 0.3f), "Stone", container);
                CreateCube($"{name}_RailR{i}", basePos + forwardVec + rightOffset + Vector3.up * y,
                    new Vector3(0.3f, 2f, 0.3f), "Stone", container);
            }
        }

        // ==================== AQUEDUCTS ====================

        private static void BuildAqueducts()
        {
            var container = GetContainer("Aqueducts");

            // LEVEL 2 aqueduct ring (per map: = symbols around edge at Y=24+2)
            float aqueductY = LEVEL_2_Y + 2f;
            int l2Min = CenterTile - LEVEL_2_RADIUS;
            int l2Max = CenterTile + LEVEL_2_RADIUS;

            // North edge
            for (int x = l2Min; x <= l2Max; x++)
            {
                if (Mathf.Abs(x - CenterTile) <= 2) continue; // Gap for walkway at G
                BuildAqueductSection($"Aqueduct_L2_N_{x}", TileToWorld(x, l2Max, aqueductY), true, container);
            }

            // South edge
            for (int x = l2Min; x <= l2Max; x++)
            {
                BuildAqueductSection($"Aqueduct_L2_S_{x}", TileToWorld(x, l2Min, aqueductY), true, container);
            }

            // East edge
            for (int z = l2Min; z <= l2Max; z++)
            {
                if (Mathf.Abs(z - CenterTile) <= 2) continue; // Gap for walkway
                BuildAqueductSection($"Aqueduct_L2_E_{z}", TileToWorld(l2Max, z, aqueductY), false, container);
            }

            // West edge
            for (int z = l2Min; z <= l2Max; z++)
            {
                if (Mathf.Abs(z - CenterTile) <= 2) continue;
                BuildAqueductSection($"Aqueduct_L2_W_{z}", TileToWorld(l2Min, z, aqueductY), false, container);
            }

            // LEVEL 3 aqueduct source (per map: = at top and bottom edges)
            float l3AqueductY = LEVEL_3_Y + 2f;
            int l3Min = CenterTile - LEVEL_3_RADIUS;
            int l3Max = CenterTile + LEVEL_3_RADIUS;

            for (int x = l3Min; x <= l3Max; x++)
            {
                // Skip waterfall opening
                if (Mathf.Abs(x - CenterTile) <= 2 && x != l3Min && x != l3Max) continue;

                BuildAqueductSection($"Aqueduct_L3_N_{x}", TileToWorld(x, l3Max, l3AqueductY), true, container);
                BuildAqueductSection($"Aqueduct_L3_S_{x}", TileToWorld(x, l3Min, l3AqueductY), true, container);
            }
        }

        private static void BuildAqueductSection(string name, Vector3 pos, bool eastWest, Transform container)
        {
            float length = TILE_SIZE;

            Vector3 channelSize = eastWest
                ? new Vector3(length, AQUEDUCT_HEIGHT, AQUEDUCT_WIDTH)
                : new Vector3(AQUEDUCT_WIDTH, AQUEDUCT_HEIGHT, length);

            // Stone channel
            CreateCube(name + "_Channel", pos, channelSize, "Aqueduct", container);

            // Water inside
            Vector3 waterSize = eastWest
                ? new Vector3(length - 0.5f, AQUEDUCT_HEIGHT * 0.3f, AQUEDUCT_WIDTH - 0.8f)
                : new Vector3(AQUEDUCT_WIDTH - 0.8f, AQUEDUCT_HEIGHT * 0.3f, length - 0.5f);
            CreateCube(name + "_Water", pos + Vector3.up * 0.5f, waterSize, "AqueductWater", container);

            // Support pillars (every few sections)
            if (name.Contains("_5") || name.Contains("_10") || name.Contains("_15") || name.Contains("_20"))
            {
                float supportHeight = pos.y - LEVEL_1_Y;
                CreateCube(name + "_Support", pos + Vector3.down * (supportHeight / 2),
                    new Vector3(1f, supportHeight, 1f), "Stone", container);
            }
        }

        // ==================== WATERFALL ====================

        private static void BuildWaterfall()
        {
            var waterContainer = GetContainer("Water");
            int waterLayerIndex = LayerMask.NameToLayer("Water");

            Vector3 center = TileToWorld(CenterTile, CenterTile, 0);

            // L3 → L2 waterfall (from source at north of L3)
            float waterfallX = center.x;
            float waterfallZ = center.z + TILE_SIZE * (LEVEL_3_RADIUS - 1);

            // Vertical cascade L3 to L2 - VISUAL ONLY (no colliders)
            for (float y = LEVEL_2_Y; y <= LEVEL_3_Y; y += 3f)
            {
                var cascade = CreateCube($"Waterfall_L3L2_{y}", new Vector3(waterfallX, y, waterfallZ),
                    new Vector3(TILE_SIZE * 2, 4f, 1.5f), "Waterfall", waterContainer);
                Object.DestroyImmediate(cascade.GetComponent<BoxCollider>()); // Visual only
            }

            // L2 landing pool (swimmable)
            var l2Pool = CreateCube("Waterfall_L2Pool", new Vector3(waterfallX, LEVEL_2_Y - 1.5f, waterfallZ),
                new Vector3(TILE_SIZE * 3, 3f, TILE_SIZE * 2), "WaterDeep", waterContainer);
            l2Pool.GetComponent<BoxCollider>().isTrigger = true;
            if (waterLayerIndex >= 0) l2Pool.layer = waterLayerIndex;
            // NOTE: Do NOT set tag - rely only on layer for detection

            // L2 → Ground waterfall (visual cascade into main fountain) - VISUAL ONLY
            float fountainZ = center.z + TILE_SIZE * 2;
            for (float y = 0; y <= LEVEL_2_Y; y += 3f)
            {
                var cascade = CreateCube($"Waterfall_L2G_{y}", new Vector3(waterfallX, y, fountainZ),
                    new Vector3(TILE_SIZE, 4f, 1.5f), "Waterfall", waterContainer);
                Object.DestroyImmediate(cascade.GetComponent<BoxCollider>()); // Visual only
            }

            // Mist effect at base - VISUAL ONLY
            var mist = CreateCube("Waterfall_Mist", center + new Vector3(0, 2f, TILE_SIZE * 2),
                new Vector3(TILE_SIZE * 3, 4f, TILE_SIZE * 2), "Waterfall", waterContainer);
            Object.DestroyImmediate(mist.GetComponent<BoxCollider>()); // Visual only
        }

        // ==================== BUILDING HELPERS ====================

        private static void BuildCastleBuilding(string name, Vector3 pos, int width, int depth, float height,
            string material, Transform container, bool hasRoofAccess, float rotation)
        {
            // Buildings now reach exactly to L2 and have OPEN TOPS (no roof)
            // Building tops connect seamlessly to L2 floor
            float buildingHeight = LEVEL_2_Y;  // Always reach L2
            float w = width * TILE_SIZE * 0.9f;
            float d = depth * TILE_SIZE * 0.9f;

            var parent = new GameObject(name);
            parent.transform.SetParent(container);
            parent.transform.position = pos;
            parent.transform.rotation = Quaternion.Euler(0, rotation, 0);

            // Main body - hollow with walls
            float wallThickness = 0.8f;

            // Front wall with archway (open entrance)
            // Front wall left
            CreateCube(name + "_FrontL", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(-w / 2 + wallThickness * 2, buildingHeight / 2, -d / 2 + wallThickness / 2),
                new Vector3(w / 3, buildingHeight, wallThickness), material, container);
            // Front wall right
            CreateCube(name + "_FrontR", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(w / 2 - wallThickness * 2, buildingHeight / 2, -d / 2 + wallThickness / 2),
                new Vector3(w / 3, buildingHeight, wallThickness), material, container);
            // Front wall top (above archway)
            CreateCube(name + "_FrontTop", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(0, buildingHeight - 2f, -d / 2 + wallThickness / 2),
                new Vector3(w / 3, 4f, wallThickness), material, container);

            // Back wall (solid)
            CreateCube(name + "_Back", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(0, buildingHeight / 2, d / 2 - wallThickness / 2),
                new Vector3(w, buildingHeight, wallThickness), material, container);

            // Left wall
            CreateCube(name + "_Left", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(-w / 2 + wallThickness / 2, buildingHeight / 2, 0),
                new Vector3(wallThickness, buildingHeight, d), material, container);

            // Right wall
            CreateCube(name + "_Right", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(w / 2 - wallThickness / 2, buildingHeight / 2, 0),
                new Vector3(wallThickness, buildingHeight, d), material, container);

            // Ground floor
            CreateCube(name + "_Floor", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(0, 0.2f, 0),
                new Vector3(w - 1f, 0.4f, d - 1f), "Stone", container);

            // TOP FLOOR at L2 height with HOLE for spiral staircase exit
            // Stair center is at: new Vector3(w / 3, 0, 0) with radius 2.5f
            int groundLayer = LayerMask.NameToLayer("Ground");
            float stairHoleRadius = 3.5f;  // Slightly larger than spiral radius for clearance
            float stairCenterX = w / 3;    // Match stair position

            // Left segment (west of stair hole)
            float leftWidth = stairCenterX - stairHoleRadius;
            if (leftWidth > 1f)
            {
                var topFloorL = CreateCube(name + "_TopFloorL",
                    pos + Quaternion.Euler(0, rotation, 0) * new Vector3(-w / 2 + leftWidth / 2 + 0.25f, buildingHeight, 0),
                    new Vector3(leftWidth, 0.3f, d - 0.5f), "Level2Floor", container);
                if (groundLayer >= 0) topFloorL.layer = groundLayer;
                topFloorL.isStatic = true;
            }

            // Right segment (east of stair hole)
            float rightStart = stairCenterX + stairHoleRadius;
            float rightWidth = w / 2 - rightStart;
            if (rightWidth > 1f)
            {
                var topFloorR = CreateCube(name + "_TopFloorR",
                    pos + Quaternion.Euler(0, rotation, 0) * new Vector3(rightStart + rightWidth / 2, buildingHeight, 0),
                    new Vector3(rightWidth, 0.3f, d - 0.5f), "Level2Floor", container);
                if (groundLayer >= 0) topFloorR.layer = groundLayer;
                topFloorR.isStatic = true;
            }

            // Back segment (behind stair hole - north side)
            var topFloorBack = CreateCube(name + "_TopFloorBack",
                pos + Quaternion.Euler(0, rotation, 0) * new Vector3(stairCenterX, buildingHeight, d / 4 + stairHoleRadius / 2),
                new Vector3(stairHoleRadius * 2, 0.3f, d / 2 - stairHoleRadius), "Level2Floor", container);
            if (groundLayer >= 0) topFloorBack.layer = groundLayer;
            topFloorBack.isStatic = true;

            // Front segment (in front of stair hole - south side)
            var topFloorFront = CreateCube(name + "_TopFloorFront",
                pos + Quaternion.Euler(0, rotation, 0) * new Vector3(stairCenterX, buildingHeight, -d / 4 - stairHoleRadius / 2),
                new Vector3(stairHoleRadius * 2, 0.3f, d / 2 - stairHoleRadius), "Level2Floor", container);
            if (groundLayer >= 0) topFloorFront.layer = groundLayer;
            topFloorFront.isStatic = true;

            // Low battlements around top edge (don't block walking, just visual)
            for (float bx = -w / 2 + 2; bx <= w / 2 - 2; bx += 4f)
            {
                // Front edge battlements
                CreateCube($"{name}_BattF_{bx}", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(bx, buildingHeight + 1f, -d / 2 + 0.3f),
                    new Vector3(1.2f, 2f, 0.5f), "Battlement", container);
                // Back edge battlements
                CreateCube($"{name}_BattB_{bx}", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(bx, buildingHeight + 1f, d / 2 - 0.3f),
                    new Vector3(1.2f, 2f, 0.5f), "Battlement", container);
            }

            // Windows
            CreateCube(name + "_WinL", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(-w / 4, buildingHeight * 0.4f, d / 2 + 0.1f),
                new Vector3(1.5f, 2.5f, 0.2f), "Window", container);
            CreateCube(name + "_WinR", pos + Quaternion.Euler(0, rotation, 0) * new Vector3(w / 4, buildingHeight * 0.4f, d / 2 + 0.1f),
                new Vector3(1.5f, 2.5f, 0.2f), "Window", container);

            // Internal spiral staircase - goes all the way to L2 height
            if (hasRoofAccess)
            {
                int numSteps = Mathf.CeilToInt(buildingHeight / 0.5f);  // Step every 0.5 units = ~48 steps
                float spiralRadius = 2.5f;
                Vector3 stairCenter = new Vector3(w / 3, 0, 0);  // Offset to one side

                for (int i = 0; i < numSteps; i++)
                {
                    float y = 1f + i * 0.5f;
                    float angle = i * 18f * Mathf.Deg2Rad;  // 20 steps per full rotation
                    Vector3 stepLocalPos = new Vector3(
                        Mathf.Cos(angle) * spiralRadius,
                        y,
                        Mathf.Sin(angle) * spiralRadius
                    );

                    var step = CreateCube($"{name}_Stair{i}",
                        pos + Quaternion.Euler(0, rotation, 0) * (stairCenter + stepLocalPos),
                        new Vector3(2.2f, 0.5f, 1.6f), "Stairs", container);
                    step.isStatic = true;
                    if (groundLayer >= 0) step.layer = groundLayer;
                }

                // Central pillar for spiral staircase
                CreateCube(name + "_StairPillar", pos + Quaternion.Euler(0, rotation, 0) * (stairCenter + new Vector3(0, buildingHeight / 2, 0)),
                    new Vector3(0.8f, buildingHeight, 0.8f), "Stone", container);
            }
        }

        private static void BuildBridge(string name, Vector3 pos, float length, Transform container)
        {
            int groundLayer = LayerMask.NameToLayer("Ground");

            // Bridge deck at Level 2 height - connects building rooftops
            float bridgeY = LEVEL_2_Y + 0.5f;
            var deck = CreateCube(name + "_Deck", pos + Vector3.up * bridgeY,
                new Vector3(length * TILE_SIZE, 0.5f, TILE_SIZE * 2f), "Bridge", container);
            deck.isStatic = true;
            if (groundLayer >= 0) deck.layer = groundLayer;

            // Tall supports from ground to bridge (reach all the way from ground to deck)
            float supportHeight = bridgeY;  // Full height from ground to deck
            for (float x = -length * TILE_SIZE / 2 + 8; x <= length * TILE_SIZE / 2 - 8; x += 10)
            {
                var support = CreateCube($"{name}_Support_{x}", pos + new Vector3(x, supportHeight / 2, 0),
                    new Vector3(2.5f, supportHeight, 2.5f), "Stone", container);
                support.isStatic = true;
            }

            // Railings at bridge level
            CreateCube(name + "_RailL", pos + new Vector3(0, bridgeY + 1f, -TILE_SIZE * 0.9f),
                new Vector3(length * TILE_SIZE, 1.5f, 0.4f), "Stone", container);
            CreateCube(name + "_RailR", pos + new Vector3(0, bridgeY + 1f, TILE_SIZE * 0.9f),
                new Vector3(length * TILE_SIZE, 1.5f, 0.4f), "Stone", container);

            Debug.Log($"Bridge '{name}' built at Y={bridgeY} with length {length * TILE_SIZE} units");
        }

        // ==================== DETAILS ====================

        private static void BuildTrees()
        {
            // Trees already added per level in their respective build methods
        }

        private static void CreateTree(string name, Vector3 pos, float height, Transform container)
        {
            CreateCube(name + "_Trunk", pos + Vector3.up * (height * 0.35f),
                new Vector3(0.8f, height * 0.7f, 0.8f), "TreeTrunk", container);
            CreateCube(name + "_Foliage1", pos + Vector3.up * (height * 0.65f),
                new Vector3(height * 0.7f, height * 0.45f, height * 0.7f), "Tree", container);
            CreateCube(name + "_Foliage2", pos + Vector3.up * (height * 0.85f),
                new Vector3(height * 0.5f, height * 0.35f, height * 0.5f), "Tree", container);
        }

        private static void BuildLamps()
        {
            var container = GetContainer("Lamps");

            // Ground level lamps along roads
            for (int i = -8; i <= 8; i += 4)
            {
                if (i == 0) continue;
                CreateLamp($"Lamp_NS_{i}", TileToWorld(CenterTile - 2, CenterTile + i, 0), container);
                CreateLamp($"Lamp_EW_{i}", TileToWorld(CenterTile + i, CenterTile - 2, 0), container);
            }

            // L2 lamps
            CreateLamp("Lamp_L2_1", TileToWorld(CenterTile - 6, CenterTile + 6, LEVEL_2_Y), container);
            CreateLamp("Lamp_L2_2", TileToWorld(CenterTile + 6, CenterTile + 6, LEVEL_2_Y), container);
            CreateLamp("Lamp_L2_3", TileToWorld(CenterTile - 6, CenterTile - 6, LEVEL_2_Y), container);
            CreateLamp("Lamp_L2_4", TileToWorld(CenterTile + 6, CenterTile - 6, LEVEL_2_Y), container);

            // L3 lamps
            CreateLamp("Lamp_L3_1", TileToWorld(CenterTile - 4, CenterTile, LEVEL_3_Y), container);
            CreateLamp("Lamp_L3_2", TileToWorld(CenterTile + 4, CenterTile, LEVEL_3_Y), container);
        }

        private static void CreateLamp(string name, Vector3 pos, Transform container)
        {
            CreateCube(name + "_Post", pos + Vector3.up * 3f,
                new Vector3(0.25f, 6f, 0.25f), "Metal", container);
            CreateCube(name + "_Arm", pos + new Vector3(0.6f, 5.8f, 0),
                new Vector3(1.2f, 0.15f, 0.15f), "Metal", container);
            CreateCube(name + "_Light", pos + new Vector3(1f, 5.2f, 0),
                new Vector3(0.6f, 1f, 0.6f), "Lamp", container);
        }

        // ==================== HELPER ====================

        private static GameObject CreateCube(string name, Vector3 position, Vector3 scale, string materialName, Transform parent)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;

            if (materials != null && materials.ContainsKey(materialName))
            {
                cube.GetComponent<MeshRenderer>().sharedMaterial = materials[materialName];
            }

            return cube;
        }

        private static void AddNavMesh()
        {
            var navSurface = cityRoot.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            Debug.Log("NavMeshSurface added. Use AldenFall > Bake NavMesh to bake.");
        }

        [MenuItem("AldenFall/Advanced/Bake NavMesh Only", priority = 102)]
        public static void BakeNavMesh()
        {
            var city = GameObject.Find("Millbrook_City");
            if (city == null)
            {
                Debug.LogError("Millbrook city not found. Build it first.");
                return;
            }

            var navSurface = city.GetComponent<NavMeshSurface>();
            if (navSurface != null)
            {
                navSurface.BuildNavMesh();
                Debug.Log("NavMesh baked successfully!");
            }
            else
            {
                Debug.LogError("No NavMeshSurface found on city root.");
            }
        }
    }
}
#endif
