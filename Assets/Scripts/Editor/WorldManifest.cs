using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>
    /// The authoritative, READABLE record of the whole world: the ground grid, every placed object
    /// (with its footprint + the ground it sits on), plus barriers (walls, mountain ring, map edges)
    /// and buildings (footprint + entrance + padding). Built during the bake so world generation is a
    /// data choice I can inspect and reason about numerically -- QA is then only a visual double-check.
    ///
    /// Pipeline: PLAN (record everything here) -> VALIDATE (numerical conflict scan) -> ship only when
    /// conflicts == 0. Dumped to WorldData/ every bake (manifest.md, ground_map.txt, per-category JSON,
    /// conflicts.json) so a future session can read the world without opening the editor.
    /// </summary>
    public class WorldManifest
    {
        public struct WallSeg { public Vector3 center, size; public float yaw; public string name; }
        public struct Building { public string name; public Vector3 pos, size; public float entranceYaw, padding; }
        public struct Conflict { public string kind, a, b; public float x, z; public string detail; }

        readonly PlacementMap _map;
        readonly PlacementCatalog _catalog;
        public readonly List<WallSeg> Walls = new();
        public readonly List<Building> Buildings = new();
        public float MountainRingRadius;
        public Vector3 PlayerSpawn;

        public WorldManifest(PlacementMap map, PlacementCatalog catalog) { _map = map; _catalog = catalog; }

        public void AddWall(string name, Vector3 center, Vector3 size, float yaw)
            => Walls.Add(new WallSeg { name = name, center = center, size = size, yaw = yaw });
        public void AddBuilding(string name, Vector3 pos, Vector3 size, float entranceYaw, float padding)
            => Buildings.Add(new Building { name = name, pos = pos, size = size, entranceYaw = entranceYaw, padding = padding });

        // ---- Ground rules: which surfaces a category must NEVER sit on -------------------------------
        // Public so the placement choke (PlaceMeshyProp) can PREVENT wrong-ground placements, not just
        // the validator detect them. One rule set, used for both prevention and the double-check.
        public static bool ForbiddenGround(string category, Ground g)
        {
            switch (category)
            {
                case "tree":
                case "deadtree":
                    return g == Ground.Water || g == Ground.DirtRoad || g == Ground.Cobble
                        || g == Ground.CityStone || g == Ground.WallBase || g == Ground.Mountain;
                case "rock":
                case "stump":
                    return g == Ground.Water || g == Ground.DirtRoad || g == Ground.Cobble
                        || g == Ground.CityStone || g == Ground.WallBase;
                case "plant":
                case "flower":
                    return g == Ground.Water || g == Ground.DirtRoad || g == Ground.Cobble
                        || g == Ground.CityStone || g == Ground.WallBase || g == Ground.Mountain;
                case "fence":
                    return g == Ground.Water || g == Ground.Mountain;
                case "building":
                    return g == Ground.Water || g == Ground.Mountain;
                default: // prop / mob / landmark -- only water is clearly wrong (props CAN sit on the mountain)
                    return g == Ground.Water;
            }
        }

        struct Solid { public Vector2 p; public float r; public string name, cat; }

        /// <summary>The numerical double-check. Scans the manifest for every conflict class and returns
        /// them (empty = clean). This is what gates the ship: only push when this comes back empty.</summary>
        public List<Conflict> Validate()
        {
            var conflicts = new List<Conflict>();
            var solids = new List<Solid>();

            foreach (var kv in _catalog.Objects)
            {
                string cat = PlacementCatalog.CategoryOf(kv.Key);
                foreach (var it in kv.Value)
                {
                    // out of bounds
                    if (!_map.InBounds(it.x, it.z))
                    {
                        conflicts.Add(new Conflict { kind = "out-of-bounds", a = kv.Key, x = it.x, z = it.z,
                            detail = "outside placement grid" });
                        continue;
                    }
                    // on wrong ground
                    var g = _map.GroundAt(it.x, it.z);
                    if (ForbiddenGround(cat, g))
                        conflicts.Add(new Conflict { kind = "wrong-ground", a = kv.Key, x = it.x, z = it.z,
                            detail = $"{cat} on {g}" });
                    // inside a wall footprint
                    foreach (var w in Walls)
                        if (Mathf.Abs(it.x - w.center.x) < w.size.x * 0.5f && Mathf.Abs(it.z - w.center.z) < w.size.z * 0.5f)
                        {
                            conflicts.Add(new Conflict { kind = "in-wall", a = kv.Key, b = w.name, x = it.x, z = it.z,
                                detail = "prop clipping a wall segment" });
                            break;
                        }
                    // floating / buried: only for props that sit ON THE TERRAIN. Furniture rides building
                    // floors and landmarks (peaks/foothills) rise above base terrain -- both legitimately
                    // above the heightmap, so they're exempt (prop/building categories).
                    bool onTerrain = cat != "building" && cat != "prop";
                    if (_map.HasHeights && onTerrain)
                    {
                        float gh = _map.HeightAt(it.x, it.z);
                        if (it.y > gh + 1.5f)
                            conflicts.Add(new Conflict { kind = "floating", a = kv.Key, x = it.x, z = it.z,
                                detail = $"base y={it.y:0.#} > ground {gh:0.#}" });
                        else if (it.y < gh - 1.5f)
                            conflicts.Add(new Conflict { kind = "buried", a = kv.Key, x = it.x, z = it.z,
                                detail = $"base y={it.y:0.#} < ground {gh:0.#}" });
                    }
                    if (it.footprint > 0.05f)
                        solids.Add(new Solid { p = new Vector2(it.x, it.z), r = it.footprint, name = kv.Key, cat = cat });
                }
            }

            // cross-type overlap between solids (spatial hash so it's ~O(n))
            const float cell = 8f;
            var grid = new Dictionary<long, List<int>>();
            long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
            for (int i = 0; i < solids.Count; i++)
            {
                int cx = Mathf.FloorToInt(solids[i].p.x / cell), cz = Mathf.FloorToInt(solids[i].p.y / cell);
                if (!grid.TryGetValue(Key(cx, cz), out var l)) { l = new List<int>(); grid[Key(cx, cz)] = l; }
                l.Add(i);
            }
            var seen = new HashSet<long>();
            for (int i = 0; i < solids.Count; i++)
            {
                var a = solids[i];
                int cx = Mathf.FloorToInt(a.p.x / cell), cz = Mathf.FloorToInt(a.p.y / cell);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (grid.TryGetValue(Key(cx + dx, cz + dz), out var l))
                            foreach (int j in l)
                            {
                                if (j <= i) continue;
                                var b = solids[j];
                                float rr = (a.r + b.r) * 0.7f;                 // 30% overlap tolerance
                                if ((a.p - b.p).sqrMagnitude < rr * rr)
                                {
                                    long pk = ((long)i << 32) ^ (uint)j;
                                    if (seen.Add(pk))
                                        conflicts.Add(new Conflict { kind = "overlap", a = a.name, b = b.name,
                                            x = a.p.x, z = a.p.y, detail = $"{a.cat} overlaps {b.cat}" });
                                }
                            }
            }
            return conflicts;
        }

        // ---- Dump -----------------------------------------------------------------------------------
        static readonly Dictionary<Ground, char> GroundChar = new()
        {
            { Ground.Grass, '.' }, { Ground.DirtField, ',' }, { Ground.DirtRoad, '=' }, { Ground.Cobble, 'o' },
            { Ground.CityStone, 'C' }, { Ground.Sand, 's' }, { Ground.Water, '~' }, { Ground.Lava, 'L' },
            { Ground.Mountain, '^' }, { Ground.WallBase, '#' }, { Ground.Glade, 'g' }, { Ground.Camp, 'x' },
            { Ground.Approach, 'a' },
        };

        public void Dump(string dir, List<Conflict> conflicts)
        {
            System.IO.Directory.CreateDirectory(dir);
            WriteGroundMap(System.IO.Path.Combine(dir, "ground_map.txt"));
            WriteCategoryJson(dir);
            WriteBuildings(System.IO.Path.Combine(dir, "buildings.json"));
            WriteConflicts(System.IO.Path.Combine(dir, "conflicts.json"), conflicts);
            WriteManifest(System.IO.Path.Combine(dir, "manifest.md"), conflicts);
        }

        void WriteGroundMap(string path)
        {
            // Downsample to <=130 cols so it reads as a screenful of text.
            int stride = Mathf.Max(1, _map.W / 130);
            var sb = new StringBuilder();
            sb.Append("# Ground map (top-down, +z UP, +x RIGHT). 1 char ~ ").Append((stride * PlacementMap.Cell).ToString("0.#")).Append("m\n");
            sb.Append("# . grass  , dirt  = road  o cobble  C city-stone  s sand  ~ water  L lava  ^ mountain  # wall  g glade  x camp  a approach\n");
            var g = _map.GroundGrid;
            for (int cz = _map.H - 1; cz >= 0; cz -= stride)
            {
                for (int cx = 0; cx < _map.W; cx += stride)
                    sb.Append(GroundChar.TryGetValue(g[cz * _map.W + cx], out var c) ? c : '?');
                sb.Append('\n');
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }

        void WriteCategoryJson(string dir)
        {
            // Group placement types by category, one file per bucket (trees/rocks/fences/plants/props).
            var buckets = new Dictionary<string, List<string>>();
            foreach (var kv in _catalog.Objects)
            {
                string cat = PlacementCatalog.CategoryOf(kv.Key);
                string file = cat == "tree" || cat == "deadtree" ? "trees"
                    : cat == "rock" || cat == "stump" ? "rocks"
                    : cat == "fence" ? "fences"
                    : cat == "plant" || cat == "flower" ? "plants"
                    : cat == "building" ? null : "props";
                if (file == null) continue;
                if (!buckets.TryGetValue(file, out var types)) { types = new List<string>(); buckets[file] = types; }
                types.Add(kv.Key);
            }
            foreach (var b in buckets)
            {
                var sb = new StringBuilder("{\n");
                for (int t = 0; t < b.Value.Count; t++)
                {
                    string type = b.Value[t];
                    var list = _catalog.Objects[type];
                    sb.Append("  \"").Append(type).Append("\": [\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        var it = list[i];
                        sb.Append("    {\"p\":[").Append(it.x.ToString("0.#")).Append(",").Append(it.y.ToString("0.#")).Append(",").Append(it.z.ToString("0.#"))
                          .Append("],\"r\":").Append(it.footprint.ToString("0.##"))
                          .Append(",\"ground\":\"").Append(it.ground).Append("\"}")
                          .Append(i < list.Count - 1 ? ",\n" : "\n");
                    }
                    sb.Append(t < b.Value.Count - 1 ? "  ],\n" : "  ]\n");
                }
                sb.Append("}\n");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, b.Key + ".json"), sb.ToString());
            }
        }

        void WriteBuildings(string path)
        {
            var sb = new StringBuilder("{\n  \"buildings\": [\n");
            for (int i = 0; i < Buildings.Count; i++)
            {
                var b = Buildings[i];
                sb.Append("    {\"name\":\"").Append(b.name).Append("\",\"pos\":[")
                  .Append(b.pos.x.ToString("0.#")).Append(",").Append(b.pos.y.ToString("0.#")).Append(",").Append(b.pos.z.ToString("0.#"))
                  .Append("],\"size\":[").Append(b.size.x.ToString("0.#")).Append(",").Append(b.size.y.ToString("0.#")).Append(",").Append(b.size.z.ToString("0.#"))
                  .Append("],\"entranceYaw\":").Append(b.entranceYaw.ToString("0")).Append(",\"padding\":").Append(b.padding.ToString("0.#")).Append("}")
                  .Append(i < Buildings.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            System.IO.File.WriteAllText(path, sb.ToString());
        }

        void WriteConflicts(string path, List<Conflict> conflicts)
        {
            var sb = new StringBuilder("{\n  \"count\": ").Append(conflicts.Count).Append(",\n  \"conflicts\": [\n");
            for (int i = 0; i < conflicts.Count; i++)
            {
                var c = conflicts[i];
                sb.Append("    {\"kind\":\"").Append(c.kind).Append("\",\"a\":\"").Append(c.a).Append("\"");
                if (!string.IsNullOrEmpty(c.b)) sb.Append(",\"b\":\"").Append(c.b).Append("\"");
                sb.Append(",\"at\":[").Append(c.x.ToString("0.#")).Append(",").Append(c.z.ToString("0.#")).Append("]")
                  .Append(",\"detail\":\"").Append(c.detail).Append("\"}")
                  .Append(i < conflicts.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            System.IO.File.WriteAllText(path, sb.ToString());
        }

        void WriteManifest(string path, List<Conflict> conflicts)
        {
            var sb = new StringBuilder();
            sb.Append("# Emberfall World Manifest\n\n");
            sb.Append("Authoritative data view of the baked world. QA is a double-check of THIS.\n\n");
            sb.Append("## Bounds\n");
            sb.Append($"- Grid origin: ({_map.OriginX:0}, {_map.OriginZ:0})  size: {_map.W}x{_map.H} cells @ {PlacementMap.Cell}m\n");
            sb.Append($"- World extent X: {_map.OriginX:0} .. {_map.OriginX + _map.W * PlacementMap.Cell:0}   Z: {_map.OriginZ:0} .. {_map.OriginZ + _map.H * PlacementMap.Cell:0}\n");
            sb.Append($"- Mountain ring (playable edge) radius: {MountainRingRadius:0}m\n");
            sb.Append($"- Player spawn: ({PlayerSpawn.x:0}, {PlayerSpawn.y:0.#}, {PlayerSpawn.z:0})\n\n");

            // ground breakdown
            sb.Append("## Ground surfaces (cell count + bounding box)\n");
            var gcount = new Dictionary<Ground, int>();
            var gmin = new Dictionary<Ground, Vector2>(); var gmax = new Dictionary<Ground, Vector2>();
            var grid = _map.GroundGrid;
            for (int cz = 0; cz < _map.H; cz++)
                for (int cx = 0; cx < _map.W; cx++)
                {
                    var g = grid[cz * _map.W + cx];
                    float wx = _map.CellWorldX(cx), wz = _map.CellWorldZ(cz);
                    if (!gcount.ContainsKey(g)) { gcount[g] = 0; gmin[g] = new Vector2(wx, wz); gmax[g] = new Vector2(wx, wz); }
                    gcount[g]++;
                    gmin[g] = Vector2.Min(gmin[g], new Vector2(wx, wz));
                    gmax[g] = Vector2.Max(gmax[g], new Vector2(wx, wz));
                }
            float cellArea = PlacementMap.Cell * PlacementMap.Cell;
            foreach (var kv in gcount)
                sb.Append($"- {kv.Key,-10} {kv.Value,6} cells (~{kv.Value * cellArea:0}m2)  bbox x[{gmin[kv.Key].x:0}..{gmax[kv.Key].x:0}] z[{gmin[kv.Key].y:0}..{gmax[kv.Key].y:0}]\n");
            sb.Append("\nSee ground_map.txt for the top-down ASCII layout.\n\n");

            // object counts by category + type
            sb.Append("## Objects\n");
            var catTotals = new Dictionary<string, int>();
            var typeCounts = new List<(string type, int n, string cat)>();
            foreach (var kv in _catalog.Objects)
            {
                string cat = PlacementCatalog.CategoryOf(kv.Key);
                catTotals[cat] = (catTotals.TryGetValue(cat, out var v) ? v : 0) + kv.Value.Count;
                typeCounts.Add((kv.Key, kv.Value.Count, cat));
            }
            foreach (var kv in catTotals) sb.Append($"- **{kv.Key}**: {kv.Value}\n");
            sb.Append("\n<details><summary>per type</summary>\n\n");
            typeCounts.Sort((a, b) => b.n.CompareTo(a.n));
            foreach (var t in typeCounts) sb.Append($"- {t.type} ({t.cat}): {t.n}\n");
            sb.Append("\n</details>\n\n");

            // buildings
            sb.Append("## Buildings (").Append(Buildings.Count).Append(")\n");
            foreach (var b in Buildings)
                sb.Append($"- {b.name}: pos ({b.pos.x:0},{b.pos.z:0}) size ({b.size.x:0}x{b.size.z:0}) entrance {b.entranceYaw:0}deg pad {b.padding:0.#}m\n");
            sb.Append("\n");

            // walls
            sb.Append("## Barriers\n");
            sb.Append($"- Wall segments: {Walls.Count}\n");
            sb.Append($"- Mountain ring beyond radius {MountainRingRadius:0}m (impassable edge)\n\n");

            // conflicts
            sb.Append("## Conflicts: ").Append(conflicts.Count).Append(conflicts.Count == 0 ? " — CLEAN, ship-ready\n\n" : " — MUST FIX before ship\n\n");
            var byKind = new Dictionary<string, int>();
            foreach (var c in conflicts) byKind[c.kind] = (byKind.TryGetValue(c.kind, out var v) ? v : 0) + 1;
            foreach (var kv in byKind) sb.Append($"- {kv.Key}: {kv.Value}\n");
            if (conflicts.Count > 0)
            {
                sb.Append("\n<details><summary>first 60</summary>\n\n");
                for (int i = 0; i < conflicts.Count && i < 60; i++)
                {
                    var c = conflicts[i];
                    sb.Append($"- [{c.kind}] {c.a}{(string.IsNullOrEmpty(c.b) ? "" : " ~ " + c.b)} @({c.x:0},{c.z:0}) {c.detail}\n");
                }
                sb.Append("\n</details>\n");
            }
            System.IO.File.WriteAllText(path, sb.ToString());
        }
    }
}
