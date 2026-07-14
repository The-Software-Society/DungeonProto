#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

namespace AldenFall.Editor
{
    /// <summary>
    /// Headless visual QA: opens the demo scene and renders labelled camera
    /// angles to PNGs. Output dir comes from -screenshotDir on the command line
    /// (falls back to ./Screenshots).
    /// </summary>
    public static class ScreenshotTool
    {
        public static void CaptureDemo() => Run(new (string, Vector3, Vector3)[]
        {
            ("overview",  new Vector3(0, 55f, -70f),  new Vector3(0, 0, 0)),
            ("plaza",     new Vector3(8f, 4f, -14f),  new Vector3(0, 1.5f, 0)),
            ("goblin",    new Vector3(3f, 2.5f, 4f),  new Vector3(0, 1f, 8f)),
            ("player",    new Vector3(2.5f, 2.5f, -34f), new Vector3(0, 1f, -30f)),
            ("pond",      new Vector3(-12f, 5f, -10f), new Vector3(-24f, 0f, 0f)),
            ("props",     new Vector3(24f, 3f, -6f),  new Vector3(29f, 1f, 0f)),
        });

        /// <summary>Ground-focused close-ups: grass/dirt patches in several biomes + inside and
        /// outside the tavern. Low camera angled down so the ground texture fills the frame.</summary>
        public static void CaptureGround() => Run(new (string, Vector3, Vector3)[]
        {
            ("grass_glade",     new Vector3(0f, 2.4f, 84f),    new Vector3(0f, 0f, 92f)),
            ("grass_forest",    new Vector3(-60f, 2.4f, 34f),  new Vector3(-60f, 0f, 44f)),
            ("grass_northfield",new Vector3(40f, 2.4f, 120f),  new Vector3(40f, 0f, 132f)),
            ("dirt_lakeshore",  new Vector3(-100f, 2.4f, 118f),new Vector3(-108f, 0f, 128f)),
            ("dirt_approach",   new Vector3(0f, 2.4f, -150f),  new Vector3(0f, 0f, -162f)),
            ("city_lot",        new Vector3(-20f, 2.4f, 14f),  new Vector3(-20f, 0f, 24f)),
            ("tavern_outside",  new Vector3(-32f, 2.6f, 46f),  new Vector3(-32f, 1.2f, 32f)),
            ("tavern_inside",   new Vector3(-32f, 2.0f, 37f),  new Vector3(-32f, 1.0f, 28f)),
        });

        /// <summary>Lake + hydrology angles: the lake is centred at (-138,138), ~72m radius,
        /// cradled by mountains, fed by the north feeder river.</summary>
        public static void CaptureLake() => Run(new (string, Vector3, Vector3)[]
        {
            ("lake_birdseye",  new Vector3(0f, 150f, 120f),    new Vector3(0f, 0f, 300f)),      // whole lake + mtns
            ("lake_from_spawn",new Vector3(0f, 4f, 185f),      new Vector3(0f, 4f, 300f)),      // Aldric's shore looking across
            ("lake_shore_E",   new Vector3(90f, 6f, 300f),     new Vector3(0f, 3f, 300f)),      // east shore across water
            ("lake_river_in",  new Vector3(20f, 12f, 420f),    new Vector3(5f, 0f, 360f)),      // north river inflow
            ("lake_wide",      new Vector3(0f, 70f, 150f),     new Vector3(0f, 0f, 320f)),      // wide basin + mtns
        });

        /// <summary>A world tour: one labelled shot per feature, for the montage video.
        /// Ordered as a walk from spawn -> lake -> mountains -> rivers -> city -> volcano.</summary>
        public static void CaptureTour() => Run(new (string, Vector3, Vector3)[]
        {
            ("01_spawn_shore",   new Vector3(0f, 13f, 150f),   new Vector3(0f, 0f, 240f)),
            ("02_lake_vista",    new Vector3(-70f, 22f, 195f), new Vector3(30f, 0f, 320f)),
            ("03_mountain_range",new Vector3(0f, 30f, 330f),   new Vector3(0f, 60f, 520f)),
            ("04_north_waterfall",new Vector3(40f, 20f, 425f), new Vector3(0f, 6f, 486f)),
            ("05_connecting_river",new Vector3(190f, 24f, 345f),new Vector3(90f, 0f, 360f)),
            ("06_river_to_city", new Vector3(60f, 20f, 60f),   new Vector3(-70f, 0f, 90f)),
            ("07_city_river",    new Vector3(40f, 16f, 22f),   new Vector3(-60f, 0f, 55f)),
            ("08_city",          new Vector3(90f, 34f, -75f),  new Vector3(0f, 0f, 10f)),
            ("09_dirt_road",     new Vector3(24f, 16f, 88f),   new Vector3(0f, 0f, 172f)),
            ("10_forest",        new Vector3(-150f, 20f, 235f),new Vector3(-150f, 0f, 320f)),
            ("11_ember_approach",new Vector3(0f, 16f, -135f),  new Vector3(0f, 2f, -260f)),
            ("12_volcano",       new Vector3(0f, 26f, -110f),  new Vector3(0f, 42f, -345f)),
        });

        /// <summary>Tight close-ups of the exact problem spots for verify-driven fixing.</summary>
        public static void CaptureDiag() => Run(new (string, Vector3, Vector3)[]
        {
            ("diag_mob",        new Vector3(-195f, 3.5f, 22f),  new Vector3(-195f, 1.5f, -4f)),   // goblin camp: mobs up close
            ("diag_lakeshore",  new Vector3(64f, 3f, 188f),     new Vector3(24f, 0.5f, 214f)),    // lake edge: sand/reeds/rocks
            ("diag_forestfloor",new Vector3(-120f, 3f, 98f),    new Vector3(-118f, 0.5f, 128f)),  // forest ground: ferns/bushes/pebbles
            ("diag_path",       new Vector3(7f, 3f, 78f),       new Vector3(0f, 0.5f, 140f)),     // dirt road surface
            ("diag_canalwater", new Vector3(42f, 4f, 20f),      new Vector3(-20f, 0.5f, 10f)),    // canal water colour/edges
        });
        public static void CaptureDiagOld() => Run(new (string, Vector3, Vector3)[]
        {
            // Natural 3rd-person-ish angles (higher, ~30 deg down) - the old y2-4 grazing angles
            // made smooth bare ground (sand/dirt) mirror the sky and read as fake water.
            ("diag_spawn_lake",     new Vector3(0f, 12f, 168f),     new Vector3(0f, 0f, 250f)),
            ("diag_camp_goblin",    new Vector3(-195f, 14f, 42f),   new Vector3(-195f, 0f, -10f)),
            ("diag_forest",         new Vector3(-120f, 14f, 108f),  new Vector3(-120f, 0f, 175f)),
            ("diag_nwpatch",        new Vector3(-150f, 16f, 250f),  new Vector3(-135f, 0f, 305f)),
            ("diag_waterfall",      new Vector3(20f, 12f, 452f),    new Vector3(0f, 4f, 486f)),
        });

        /// <summary>High aerial of the whole expanded world to judge the macro layout.</summary>
        public static void CaptureMacro() => Run(new (string, Vector3, Vector3)[]
        {
            ("macro_N",  new Vector3(0f, 430f, -200f),  new Vector3(0f, 0f, 180f)),   // looking N over city->lake
            ("macro_top",new Vector3(0f, 620f, 40f),    new Vector3(0f, 0f, 45f)),    // near top-down
            ("macro_E",  new Vector3(-420f, 300f, 120f),new Vector3(60f, 0f, 160f)),  // from the west
        });

        /// <summary>This loop's targeted verify shots: waterfall straight-on, dark volcano,
        /// charred approach cover, and the camp (bush/foothill fixes).</summary>
        public static void CaptureLoop() => Run(new (string, Vector3, Vector3)[]
        {
            ("loop_lakevista",  new Vector3(-70f, 22f, 195f), new Vector3(30f, 0f, 320f)),   // FLOODING check (montage shot 2 angle)
            ("loop_lake_lo",    new Vector3(-40f, 6f, 205f),  new Vector3(20f, 1f, 300f)),   // low grazing lake edge: flood?
            ("loop_spawn",      new Vector3(0f, 13f, 150f),   new Vector3(0f, 0f, 240f)),    // spawn: waterfall sore-thumb check
            ("loop_fall_front", new Vector3(0f, 30f, 408f),   new Vector3(0f, 22f, 486f)),   // river-0 fall, pulled back + up
            ("loop_fall_side",  new Vector3(60f, 26f, 412f),  new Vector3(-4f, 20f, 486f)),  // fall 3/4 angle
            ("loop_culvert_w",  new Vector3(-92f, 12f, 34f),  new Vector3(-55f, 2f, 34f)),   // west canal culvert from outside
            ("loop_culvert_e",  new Vector3(92f, 12f, 12f),   new Vector3(55f, 2f, 12f)),    // east canal culvert from outside
            ("loop_volcano",    new Vector3(0f, 24f, -120f),  new Vector3(0f, 40f, -350f)),  // dormant volcano
            // --- this loop: road shine, trees-in-water, reeds, water realism ---
            ("dbg_approachroad",new Vector3(0f, 3.5f, -70f),  new Vector3(0f, 2f, -260f)),   // img19: ember-approach "road" wet/shiny?
            ("dbg_nsroad",      new Vector3(2f, 4f, 90f),     new Vector3(0f, 1.5f, 200f)),  // N-S dirt road toward lake
            ("dbg_nsroad_lo",   new Vector3(3f, 2.2f, 140f),  new Vector3(0f, 1.2f, 210f)),  // road grazing (shine)
            ("dbg_riverforest", new Vector3(150f, 5f, 360f),  new Vector3(120f, 1f, 380f)),  // img20: trees in river?
            ("dbg_reeds",       new Vector3(70f, 6f, 205f),   new Vector3(45f, 0.5f, 224f)), // img21: reed tufts + base disc
            ("dbg_mob_water",   new Vector3(90f, 4f, 300f),   new Vector3(40f, 1f, 300f)),   // mobs/props on water?
            ("dbg_meadow",      new Vector3(-120f, 5f, 150f), new Vector3(-90f, 1f, 190f)),  // open-field ecosystem density
            ("dbg_fall_rocks",  new Vector3(40f, 12f, 430f),  new Vector3(0f, 8f, 486f)),    // waterfall rock variety + foam
        });

        /// <summary>Probe what surface(s) sit at road/approach/reed points: raycast down + list
        /// every renderer near the point with its material shader + smoothness. Diagnoses the
        /// "shiny road" (is it a water mesh? a smooth material?).</summary>
        public static void ProbeSurfaces()
        {
            var pts = new (string, Vector3)[] {
                ("NS_road z150",   new Vector3(0f, 5f, 150f)),
                ("NS_road z190",   new Vector3(0f, 5f, 190f)),
                ("approach z-150", new Vector3(0f, 5f, -150f)),
                ("approach z-250", new Vector3(0f, 5f, -250f)),
                ("reeds z218",     new Vector3(45f, 5f, 218f)),
            };
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var tm = terrain.materialTemplate;
                Debug.Log($"PROBE TERRAIN mat={tm?.name} shader={tm?.shader?.name}");
                var td = terrain.terrainData;
                foreach (var l in td.terrainLayers)
                {
                    float alphaSample = 1f;
                    if (l.diffuseTexture != null && l.diffuseTexture.isReadable)
                        alphaSample = l.diffuseTexture.GetPixel(8, 8).a;
                    Debug.Log($"PROBE   layer={l.name} smooth={l.smoothness:F2} metallic={l.metallic:F2} specular={l.specular} normalScale={l.normalScale} diffAlpha@8,8={alphaSample:F2} maskMap={(l.maskMapTexture != null)}");
                }
            }
            foreach (var (label, p) in pts)
            {
                Debug.Log($"PROBE === {label} @ {p.x},{p.z} ===");
                foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    var b = r.bounds;
                    if (p.x < b.min.x - 4 || p.x > b.max.x + 4 || p.z < b.min.z - 4 || p.z > b.max.z + 4) continue;
                    if (b.max.y < -3f || b.min.y > 4f) continue;
                    var m = r.sharedMaterial;
                    float sm = m != null && m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : -1f;
                    Debug.Log($"PROBE   {r.gameObject.name} y=[{b.min.y:F2},{b.max.y:F2}] mat={m?.name} shader={m?.shader?.name} smooth={sm:F2}");
                }
            }
        }

        /// <summary>Log a prop's submesh/material structure (to see if OakTree has a separable
        /// trunk vs leaves).</summary>
        public static void ProbeProp()
        {
            foreach (var name in new[] { "OakTree", "PineTree", "BirchTree" })
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{name}.fbx");
                if (asset == null) { Debug.Log($"PROP {name}: null"); continue; }
                var inst = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset);
                foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
                {
                    var sm = mf.sharedMesh;
                    var r = mf.GetComponent<Renderer>();
                    Debug.Log($"PROP {name}: obj={mf.gameObject.name} subMeshes={sm?.subMeshCount} mats={r?.sharedMaterials?.Length} bounds={sm?.bounds.size}");
                    if (r != null) foreach (var m in r.sharedMaterials) Debug.Log($"PROP {name}   mat={m?.name}");
                }
                Object.DestroyImmediate(inst);
            }
        }

        public static void CheckMesh()
        {
            foreach (var p in new[] { "BoulderCluster", "MossBoulder", "MossyRock", "Fern", "Wildflowers", "Reeds", "PebblePatch", "ForestBush", "OakTree", "PineTree" })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Props/{p}.fbx");
                if (go == null) { Debug.Log($"MESHCHK {p}: LoadAsset=NULL (import failed)"); continue; }
                var mfs = go.GetComponentsInChildren<MeshFilter>();
                int verts = 0; foreach (var m in mfs) if (m.sharedMesh != null) verts += m.sharedMesh.vertexCount;
                Debug.Log($"MESHCHK {p}: meshFilters={mfs.Length} verts={verts}");
            }
        }

        public static void CheckTerrain()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            var terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null) { Debug.Log("CHECK: no Terrain in scene"); return; }
            var data = terrain.terrainData;
            Debug.Log($"CHECK layers={data.terrainLayers.Length} splatRes={data.alphamapResolution} " +
                      $"names=[{string.Join(",", System.Array.ConvertAll(data.terrainLayers, l => l == null ? "NULL" : l.name))}]");
            var mat = terrain.materialTemplate;
            Debug.Log($"CHECK terrain material={(mat == null ? "NULL(default)" : mat.name)} shader={(mat == null ? "?" : mat.shader.name)}");
            // world (0,-250) -> alphamap index. origin z=-470 sizeZ=1040, origin x=-450 sizeX=900, res=512
            int res = data.alphamapResolution;
            int ix = Mathf.RoundToInt((0f - (-450f)) / 900f * (res - 1));
            int iz = Mathf.RoundToInt((-250f - (-470f)) / 1040f * (res - 1));
            var a = data.GetAlphamaps(ix, iz, 1, 1);
            Debug.Log($"CHECK volcano alphamap grass={a[0, 0, 0]:F2} sand={a[0, 0, 1]:F2} soot={a[0, 0, 2]:F2} dirt={a[0, 0, 3]:F2} rock={a[0, 0, 4]:F2}");
        }

        public static void CheckPuddles()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            var terrain = Object.FindObjectOfType<Terrain>();
            if (terrain == null) { Debug.Log("PUDDLE: no terrain"); return; }
            var data = terrain.terrainData;
            Vector3 tp = terrain.transform.position;
            const float waterY = -0.35f;
            int res = data.heightmapResolution;
            var hm = data.GetHeights(0, 0, res, res);
            // Lake disc (0,300,r95). Count sub-water texels OUTSIDE it (rivers + stray puddles).
            int subWater = 0, outsideLake = 0;
            float minH = 999f;
            var samples = new System.Collections.Generic.List<string>();
            for (int iz = 0; iz < res; iz++)
                for (int ix = 0; ix < res; ix++)
                {
                    float wx = tp.x + ix / (float)(res - 1) * data.size.x;
                    float wz = tp.z + iz / (float)(res - 1) * data.size.z;
                    float wy = tp.y + hm[iz, ix] * data.size.y;
                    if (wy < minH) minH = wy;
                    if (wy < waterY)
                    {
                        subWater++;
                        float lakeR = Mathf.Sqrt(wx * wx + (wz - 300f) * (wz - 300f));
                        if (lakeR > 108f)
                        {
                            outsideLake++;
                            // Focus on the exact NW-field box the forest shot frames.
                            if (samples.Count < 40 && wz > 255f && wz < 340f && wx > -200f && wx < -100f)
                                samples.Add($"({wx:F0},{wz:F0},y{wy:F2})");
                        }
                    }
                }
            Debug.Log($"PUDDLE subWater={subWater} outsideLake={outsideLake} minWorldY={minH:F2} res={res}");
            Debug.Log($"PUDDLE outside-lake samples: {string.Join(" ", samples)}");
            // Fine terrain-height dump over the NW-field water the shot frames, via SampleHeight
            // (matches the rendered terrain), plus list any water renderers overlapping it.
            for (float wz = 165f; wz <= 215f; wz += 10f)
            {
                var row = new System.Text.StringBuilder($"GRID z{wz:F0}: ");
                for (float wx = -30f; wx <= 30f; wx += 10f)
                {
                    float th = terrain.SampleHeight(new Vector3(wx, 0f, wz)) + terrain.transform.position.y;
                    row.Append($"x{wx:F0}={th:F2}{(th < -0.35f ? "*W" : "")} ");
                }
                Debug.Log(row.ToString());
            }
            // Every renderer whose bounds overlap the spawn foreground (x-25..25, z170..200).
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var b = r.bounds;
                if (b.min.x < 25f && b.max.x > -25f && b.min.z < 200f && b.max.z > 170f && b.min.y < 0.5f && b.max.y > -1.5f
                    && (r.name.Contains("Water") || r.name.Contains("Lake") || r.name.Contains("Surface") || r.name.Contains("River") || r.name.Contains("Feeder") || r.name.Contains("Outflow")))
                    Debug.Log($"SPAWNREND {r.name} mat={r.sharedMaterial?.name} y[{b.min.y:F2},{b.max.y:F2}] x[{b.min.x:F0},{b.max.x:F0}] z[{b.min.z:F0},{b.max.z:F0}]");
            }
            var ls = GameObject.Find("Lake_Surface");
            if (ls != null) { var lb = ls.GetComponent<Renderer>().bounds; Debug.Log($"LAKESURF x[{lb.min.x:F0},{lb.max.x:F0}] z[{lb.min.z:F0},{lb.max.z:F0}] y[{lb.min.y:F2},{lb.max.y:F2}]"); }
            Debug.Log($"SCENE has {Object.FindObjectsOfType<Terrain>().Length} terrain(s); WorldWater present: {GameObject.Find("WorldWater") != null}; Lake_Surface: {GameObject.Find("Lake_Surface") != null}");
        }

        /// <summary>Drop mob VISUAL prefabs at the camps + wander spots so the montage shows a
        /// living world (real mobs are server-spawned at runtime and absent from the static scene).
        /// Scene is never saved, so these are montage-only.</summary>
        private static void PopulateMobVisuals()
        {
            var terrain = Object.FindObjectOfType<Terrain>();
            float GroundY(float x, float z) => terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
            void Drop(string prefab, float x, float z, int n, float spread)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Generated/{prefab}.prefab");
                if (asset == null) return;
                for (int i = 0; i < n; i++)
                {
                    // Deterministic scatter (no Random in batch QA): golden-angle ring.
                    float a = i * 2.399963f, rr = spread * (0.3f + 0.7f * (i / (float)Mathf.Max(1, n)));
                    float px = x + Mathf.Cos(a) * rr, pz = z + Mathf.Sin(a) * rr;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                    go.transform.position = new Vector3(px, GroundY(px, pz), pz);
                    go.transform.rotation = Quaternion.Euler(0f, a * 57.29578f, 0f);
                }
            }
            Drop("GoblinVisual", -195f, 0f, 5, 14f);      // goblin camp W
            Drop("BanditVisual", 195f, 0f, 5, 14f);       // bandit camp E
            Drop("OrcBruteVisual", -110f, -120f, 3, 12f); // orc warcamp SW
            Drop("GoblinVisual", -110f, -120f, 2, 16f);
            Drop("EmberAcolyteVisual", 0f, -210f, 4, 20f);// ember approach toward the volcano
            Drop("GoblinVisual", -150f, 120f, 3, 30f);    // wandering the west woods
            Drop("BanditVisual", 120f, 150f, 2, 24f);     // wandering NE
            Drop("GoblinVisual", 60f, -30f, 2, 18f);      // near the south gate
        }

        private static void Run((string name, Vector3 pos, Vector3 lookAt)[] shots)
        {
            string outDir = "Screenshots";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-screenshotDir") outDir = args[i + 1];
            Directory.CreateDirectory(outDir);

            EditorSceneManager.OpenScene("Assets/Scenes/EmberfallDemo.unity");
            PopulateMobVisuals();   // mobs spawn at runtime, so drop visuals for the montage (scene not saved)

            var camObj = new GameObject("QA_Camera");
            var cam = camObj.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1400f;

            var rt = new RenderTexture(1280, 720, 24);
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            foreach (var shot in shots)
            {
                camObj.transform.position = shot.pos;
                camObj.transform.LookAt(shot.lookAt);

                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                File.WriteAllBytes(Path.Combine(outDir, $"{shot.name}.png"), tex.EncodeToPNG());
                Debug.Log($"SCREENSHOT saved {shot.name}.png");
            }

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camObj);
        }
    }
}
#endif
