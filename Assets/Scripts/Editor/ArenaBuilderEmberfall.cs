#if UNITY_EDITOR
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
        private static readonly Vector3 LakeCenter = new Vector3(-150f, 0f, 150f);
        private const float LakeHalf = 44f;         // half-width of the open water (~88m across)
        private const float LakeBedY = -2.6f;       // depth = WaterSurfaceY - LakeBedY = 2.7m (>1.3 swim)

        // ---- Ember approach (south corridor) ----
        private const float ApproachHalfW = 62f;    // corridor half-width in x
        private const float ApproachNorthZ = -55f;  // starts at the south city gate
        private const float ApproachSouthZ = -315f;  // ends at the volcano entrance

        // ---- Volcano interior envelope (kept in sync with DungeonResetController) ----
        private const float VolcanoEntranceZ = -315f;
        private const float BossZ = -436f;

        // ==================== ENTRY ====================

        /// <summary>Single entry called by BuildArena — assembles the whole radial world.</summary>
        private static void BuildEmberfallWorld(Transform root, int groundLayer)
        {
            BuildEmberfallGround(root, groundLayer);   // forest ring + charred approach ground
            BuildEmberfallCity(root, groundLayer);
            BuildEmberfallLake(root, groundLayer);
            BuildDeepDarkWoods(root, groundLayer);
            BuildEmberForestCamps(root, groundLayer);
            BuildEmberOrcWarcamp(root, groundLayer);
            BuildEmberApproach(root, groundLayer);
            BuildEmberfallVolcano(root, groundLayer);
            BuildEmberfallSystems(root, groundLayer);
            BuildGlobalSafety(root, groundLayer);      // resized to the new world bounds
        }

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

        private static bool InFeederRiver(float x, float z)
            => z <= 252f && z >= LakeCenter.z + LakeHalf - 6f
               && Mathf.Abs(x - FeederRiverX(z)) < 11f;

        // ==================== GROUND (forest ring + approach) ====================

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

                    Material mat;
                    if (approach)
                    {
                        // Sooty gradient — the ground darkens as it nears the volcano.
                        float t = Mathf.InverseLerp(ApproachNorthZ, ApproachSouthZ, cz);
                        mat = Mat($"Soot{Mathf.RoundToInt(t * 5f)}",
                            Color.Lerp(new Color(0.26f, 0.24f, 0.18f), new Color(0.12f, 0.09f, 0.08f), t),
                            smoothness: 0.03f);
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
            string[] kit = { "OakTree", "BirchTree", "PineTree", "DeadTree" };

            // Camps + POIs the trees must NOT bury.
            var keepClear = new Vector2[]
            {
                new Vector2(-195f, 0f),   // goblin camp W
                new Vector2(195f, 0f),    // bandit camp E
                new Vector2(-110f, -120f),// orc warcamp SW
                new Vector2(LakeCenter.x, LakeCenter.z), // lake
                new Vector2(0f, 195f),    // deep dark woods handled separately
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
                if (PlaceMeshyProp(container, kit[planted % kit.Length], new Vector3(x, 0f, z),
                        (float)rng.NextDouble() * 360f, h, groundLayer) != null)
                    continue;
                PlaceCubePine(container, planted, x, z, h, trunkMat, leafMat, groundLayer);
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
            var high = Cube($"Pine{id}_High", new Vector3(x, h * 0.92f, z),
                new Vector3(h * 0.32f, h * 0.3f, h * 0.32f), leafMat, container);
            high.isStatic = true;
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
                // Tavern + party/quest board (NW)
                ("TownHall",       new Vector3(-34f, 0f, 34f),  135f, 9f),
                ("Cottage",        new Vector3(-38f, 0f, 12f),  90f,  7f),
                // Market Row (NE)
                ("MarketStall",    new Vector3(30f, 0f, 34f),   215f, 3.4f),
                ("MarketStall",    new Vector3(38f, 0f, 26f),   250f, 3.4f),
                ("TownHouse",      new Vector3(36f, 0f, 40f),   225f, 8f),
                // The Forge (SE) — the gear-upgrader is the F7 panel, so this is flavour.
                ("BlacksmithShop", new Vector3(34f, 0f, -30f),  315f, 8f),
                ("Storehouse",     new Vector3(40f, 0f, -12f),  270f, 7.5f),
                // Aldric's home (SW, by the plaza)
                ("Cottage",        new Vector3(-34f, 0f, -30f), 45f,  7f),
                ("TownHouse",      new Vector3(-40f, 0f, -12f), 90f,  8f),
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

            // Plaza + street torches for warm, lived-in light.
            foreach (var p in new[]
            {
                new Vector3(9f, 0f, 9f), new Vector3(-9f, 0f, 9f),
                new Vector3(9f, 0f, -9f), new Vector3(-9f, 0f, -9f),
                new Vector3(0f, 0f, 30f), new Vector3(0f, 0f, -30f),
                new Vector3(30f, 0f, 0f), new Vector3(-30f, 0f, 0f),
            })
                CreateTorch(container, p, 1f);

            // Ambient (decorative) townsfolk — gentle, non-hostile figures for life.
            PlaceTownsfolk(container, new Vector3(24f, 0f, 26f), new Color(0.4f, 0.32f, 0.5f), groundLayer);   // market
            PlaceTownsfolk(container, new Vector3(-26f, 0f, 24f), new Color(0.3f, 0.4f, 0.55f), groundLayer);  // tavern
            PlaceTownsfolk(container, new Vector3(28f, 0f, -22f), new Color(0.5f, 0.35f, 0.28f), groundLayer);  // forge

            // ---- NPCs ----
            // Aldric (q_intro giver) — SW quad by the plaza, near the player spawn.
            BuildQuestGiver(container, new Vector3(5f, 0f, -9f), groundLayer);
            // Worried Mother (q_rescue giver, npcId "townsperson") — kept at (10,0,-6)
            // so the existing q_rescue_start/complete cutscenes still frame correctly.
            BuildRescueGiver(container, groundLayer);
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

        /// <summary>A simple, non-networked decorative townsperson (robed capsule + head).</summary>
        private static void PlaceTownsfolk(Transform container, Vector3 pos, Color robe, int groundLayer)
        {
            var npc = new GameObject("Townsfolk");
            npc.transform.SetParent(container);
            npc.transform.position = pos;
            npc.transform.rotation = Quaternion.Euler(0, rng.Next(0, 360), 0);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(npc.transform);
            body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            body.transform.localScale = new Vector3(0.72f, 0.95f, 0.72f);
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

            var basinMat = Mat("LakeBasin", new Color(0.18f, 0.22f, 0.24f), smoothness: 0.3f);
            var waterMat = Mat("LakeWater", new Color(0.15f, 0.34f, 0.44f, 0.62f),
                smoothness: 0.95f, transparent: true, emission: new Color(0.03f, 0.08f, 0.1f));

            Vector3 c = LakeCenter;
            float w = LakeHalf * 2f, depth = WaterSurfaceY - LakeBedY;

            // Bed (solid, Ground layer — a swimmer who touches bottom stands on it).
            var bed = Cube("Lake_Bed", c + Vector3.up * (LakeBedY - 0.35f), new Vector3(w + 2f, 0.7f, w + 2f), basinMat, container);
            bed.isStatic = true;
            if (groundLayer >= 0) bed.layer = groundLayer;

            // Basin side walls on N/W/NW; the SE side gets wade-in steps toward the city.
            void SideWall(string n, Vector3 pos, Vector3 size)
            {
                var wall = Cube(n, pos, size, basinMat, container);
                wall.isStatic = true;
                if (groundLayer >= 0) wall.layer = groundLayer;
            }
            float wy = LakeBedY / 2f;
            SideWall("Lake_N", c + new Vector3(0, wy, LakeHalf + 0.25f), new Vector3(w + 1f, depth, 0.5f));
            SideWall("Lake_W", c + new Vector3(-LakeHalf - 0.25f, wy, 0), new Vector3(0.5f, depth, w + 1f));
            SideWall("Lake_S", c + new Vector3(0, wy, -LakeHalf - 0.25f), new Vector3(w + 1f, depth, 0.5f));

            // Wade-in steps on the EAST bank (toward the city) — grade the bank down.
            int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float top = LakeBedY + (i + 1) * (depth / steps);
                float h = top - (LakeBedY - 0.4f);
                var stp = Cube($"Lake_Step{i}",
                    c + new Vector3(LakeHalf - (steps - i) * 1.4f + 0.7f, top - h / 2f, 0f),
                    new Vector3(1.4f, h, w * 0.7f), basinMat, container);
                stp.isStatic = true;
                if (groundLayer >= 0) stp.layer = groundLayer;
            }

            // Water trigger volume (Water layer) — swim detection is depth-based.
            var vol = new GameObject("Lake_WaterVolume");
            vol.transform.SetParent(container);
            vol.transform.position = c + Vector3.up * ((WaterSurfaceY + LakeBedY) / 2f);
            var vbox = vol.AddComponent<BoxCollider>();
            vbox.isTrigger = true;
            vbox.size = new Vector3(w, depth, w);
            if (waterLayer >= 0) vol.layer = waterLayer;

            // Translucent surface (collider stripped so players/props pass in).
            var surf = Cube("Lake_Surface", c + Vector3.up * WaterSurfaceY, new Vector3(w - 0.4f, 0.06f, w - 0.4f), waterMat, container);
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

            // Feeder river flowing in from the north.
            BuildFeederRiver(container, groundLayer, waterLayer, basinMat, waterMat);

            // Fish schools — visible ambient shoals in the lake + the river.
            var lakeFish = new GameObject("Lake_FishSchool");
            lakeFish.transform.SetParent(container);
            lakeFish.AddComponent<World.FishSchool>().Configure(
                c + Vector3.up * (WaterSurfaceY - 0.8f), new Vector3(LakeHalf - 6f, 1.1f, LakeHalf - 6f), 14, WaterSurfaceY);

            var riverFish = new GameObject("River_FishSchool");
            riverFish.transform.SetParent(container);
            riverFish.AddComponent<World.FishSchool>().Configure(
                new Vector3(FeederRiverX(224f), WaterSurfaceY - 0.7f, 224f), new Vector3(6f, 0.8f, 18f), 6, WaterSurfaceY);

            // Fishing spot on the dry east bank + campfire a few steps back.
            BuildLakeFishingAndCooking(container, groundLayer, interactableLayer);
        }

        private static void BuildFeederRiver(Transform container, int groundLayer, int waterLayer, Material bedMat, Material waterMat)
        {
            var shoreMat = Mat("RiverShoreN", new Color(0.3f, 0.26f, 0.19f), smoothness: 0.05f);
            const float halfW = 7f, bedY = -2.2f, seg = 6f;
            float colTop = WaterSurfaceY, colH = colTop - bedY, colCy = (colTop + bedY) / 2f;
            float sourceZ = 250f, mouthZ = LakeCenter.z + LakeHalf - 4f;

            for (float z = sourceZ; z > mouthZ - 0.1f; z -= seg)
            {
                float zA = z, zB = Mathf.Max(z - seg, mouthZ);
                float xA = FeederRiverX(zA), xB = FeederRiverX(zB);
                Vector3 mid = new Vector3((xA + xB) / 2f, 0f, (zA + zB) / 2f);
                float dx = xB - xA, dz = zB - zA;
                float len = Mathf.Sqrt(dx * dx + dz * dz) + 1.6f;
                float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                Quaternion rot = Quaternion.Euler(0, yaw, 0);
                float yawRad = yaw * Mathf.Deg2Rad;
                Vector3 rightDir = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

                var bed = Cube("Feeder_Bed", mid + Vector3.up * (bedY - 0.35f), new Vector3(halfW * 2f + 1f, 0.7f, len), bedMat, container);
                bed.transform.rotation = rot; bed.isStatic = true;
                if (groundLayer >= 0) bed.layer = groundLayer;

                var vol = new GameObject("Feeder_WaterVolume");
                vol.transform.SetParent(container);
                vol.transform.position = mid + Vector3.up * colCy;
                vol.transform.rotation = rot;
                var vbox = vol.AddComponent<BoxCollider>();
                vbox.isTrigger = true;
                vbox.size = new Vector3(halfW * 2f, colH, len);
                if (waterLayer >= 0) vol.layer = waterLayer;

                var surf = Cube("Feeder_Surface", mid + Vector3.up * WaterSurfaceY, new Vector3(halfW * 2f - 0.4f, 0.06f, len), waterMat, container);
                surf.transform.rotation = rot;
                Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

                // Wade-in shore steps each bank.
                for (int side = -1; side <= 1; side += 2)
                {
                    float top = -1.0f, h = top - (bedY - 0.4f), off = halfW + 1.25f;
                    var stp = Cube("Feeder_Shore", mid + rightDir * (off * side) + Vector3.up * (top - h / 2f),
                        new Vector3(2.5f, h, len), shoreMat, container);
                    stp.transform.rotation = rot; stp.isStatic = true;
                    if (groundLayer >= 0) stp.layer = groundLayer;
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
            string[] kit = { "PineTree", "DeadTree", "OakTree" };

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
                        (float)rng.NextDouble() * 360f, h, groundLayer) != null) continue;
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
                if (PlaceMeshyProp(container, "DeadTree", new Vector3(x, 0f, z), (float)rng.NextDouble() * 360f, h, groundLayer) != null)
                    continue;
                var trunk = Cube($"CharTree{planted}", new Vector3(x, h * 0.4f, z), new Vector3(0.55f, h * 0.8f, 0.55f), trunkMat, container);
                trunk.transform.rotation = Quaternion.Euler((float)rng.NextDouble() * 8f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 8f);
                trunk.isStatic = true;
                if (groundLayer >= 0) trunk.layer = groundLayer;
                if (t < 0.5f)
                {
                    var leaf = Cube($"CharTree{planted}_Leaf", new Vector3(x, h * 0.85f, z), new Vector3(h * 0.4f, h * 0.35f, h * 0.4f), deadLeaf, container);
                    leaf.isStatic = true;
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

        /// <summary>Emissive lava surface + a trigger volume with a server-side LavaHazard DoT.</summary>
        private static void BuildLavaPool(Transform parent, Vector3 center, Vector2 sizeXZ, int groundLayer)
        {
            var container = Child(parent, "LavaPool");
            var lavaMat = Mat("Lava", new Color(1f, 0.35f, 0.06f), smoothness: 0.3f,
                emission: new Color(1f, 0.32f, 0.05f) * 3.5f);
            var crustMat = Mat("LavaCrust", new Color(0.16f, 0.09f, 0.07f), smoothness: 0.05f,
                emission: new Color(0.4f, 0.1f, 0.02f));

            // Sunken crust rim (solid) so the pool reads as a basin, not a decal.
            var rim = Cube("Lava_Crust", center + Vector3.down * 0.15f, new Vector3(sizeXZ.x + 2f, 0.3f, sizeXZ.y + 2f), crustMat, container);
            rim.isStatic = true;
            if (groundLayer >= 0) rim.layer = groundLayer;

            // Glowing surface — collider stripped so the player can wade in and burn.
            var surf = Cube("Lava_Surface", center + Vector3.up * 0.06f, new Vector3(sizeXZ.x, 0.12f, sizeXZ.y), lavaMat, container);
            Object.DestroyImmediate(surf.GetComponent<BoxCollider>());

            // Trigger volume (default layer) rising ~2m so a standing entity overlaps it.
            var hazardObj = new GameObject("LavaHazard");
            hazardObj.transform.SetParent(container);
            hazardObj.transform.position = center + Vector3.up * 1f;
            var box = hazardObj.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(sizeXZ.x, 2.2f, sizeXZ.y);
            hazardObj.AddComponent<World.LavaHazard>().Configure(14f, 0.5f);

            // Ember glow.
            var lightObj = new GameObject("Lava_Light");
            lightObj.transform.SetParent(container);
            lightObj.transform.position = center + Vector3.up * 1.5f;
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.4f, 0.12f);
            light.range = Mathf.Max(sizeXZ.x, sizeXZ.y) * 1.2f;
            light.intensity = 2.2f;
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
            var slab = Cube("BossDoor_Seal", new Vector3(0f, 2.5f, -419f), new Vector3(6.6f, 5f, 0.6f), sealMat, container);
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
            var signMat = Mat("Floor2Sign", new Color(0.3f, 0.28f, 0.34f), smoothness: 0.1f, emission: new Color(0.1f, 0.14f, 0.3f));
            var sign = Cube("Floor2_Sign", new Vector3(0f, 5f, -446.5f), new Vector3(6f, 1.2f, 0.3f), signMat, container);
            Object.DestroyImmediate(sign.GetComponent<BoxCollider>());
            // NOTE: "Sealed until Emberfall is cleared." The Phase-3 quest agent unlocks this
            // gate on EmberfallWorldState -> Cleared. For now it is a permanent wall (SAO hook).

            // Guardian loot.
            CreateLootChest(container, new Vector3(-28f, 0f, -374f), 30f,
                new (string id, float chance)[] { ("knight_sword", 1f), ("adept_focus", 1f), ("iron_chestplate", 0.75f) }, groundLayer);
            CreateLootChest(container, new Vector3(0f, 0f, -430f), 0f,
                new (string id, float chance)[] { ("ember_focus", 1f), ("emberfang", 0.9f), ("knight_sword", 1f) }, groundLayer);
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
        }

        // ==================== WORLD SAFETY (bounds) ====================
        // NOTE: the shared BuildGlobalSafety(root, groundLayer) lives in ArenaBuilder.cs and
        // has been resized to enclose x[-260,260], z[-450,270]. It is called from
        // BuildEmberfallWorld above.
    }
}
#endif
