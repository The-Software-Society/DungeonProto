using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>
    /// THE single source of truth for scattered world objects: a per-type list of placements
    /// ({pos, yaw, height}). World-gen fills it (checking the PlacementMap so nothing conflicts),
    /// writes it to JSON, and the renderer instantiates purely from it. Because it's flat per-object
    /// data, it can be analysed directly (Validate) to find every tree-in-road / overlap / biome
    /// mismatch, and inspected in-game via the coord overlay.
    /// </summary>
    public class PlacementCatalog
    {
        public struct Item
        {
            public float x, y, z, yaw, h;
            public float footprint;      // solid radius reserved on the occupancy grid (0 = soft/nestling)
            public PlaceZone zone; public PlaceBiome biome; public Ground ground;
            public string category;      // tree|deadtree|rock|fence|plant|flower|prop|building|mob
        }
        public readonly Dictionary<string, List<Item>> Objects = new();

        /// <summary>Category bucket for a placement type name — used to group the per-category dumps
        /// (trees.json, rocks.json, fences.json…) and to pick the on-wrong-ground rules for validation.</summary>
        public static string CategoryOf(string type)
        {
            string n = type.ToLowerInvariant();
            if (n.StartsWith("building_") || n.Contains("stall") || n.Contains("shell")) return "building";
            if (n.Contains("fence") || n.Contains("rail") || n.Contains("post")) return "fence";
            if (n.Contains("boulder") || n.Contains("rock") || n.Contains("stone") || n.Contains("namaqualand") || n.Contains("cliff")) return "rock";
            if (n.Contains("stump") || n.Contains("root")) return "stump";
            if (n.Contains("dead")) return "deadtree";
            if (n.Contains("tree") || n.Contains("island") || n.Contains("fir") || n.Contains("jacaranda") || n.Contains("pine")) return "tree";
            if (n.Contains("flower") || n.Contains("dandelion") || n.Contains("celandine") || n.Contains("gazania")) return "flower";
            if (n.Contains("shrub") || n.Contains("fern") || n.Contains("bush") || n.Contains("grass") || n.Contains("reed") || n.Contains("tussock")) return "plant";
            return "prop";
        }

        /// <summary>Legacy 2D add (y/footprint/ground filled from the map). Kept so existing callers
        /// compile; new callers should pass the full record via the richer overload.</summary>
        public void Add(string type, float x, float z, float yaw, float h, PlaceZone zone, PlaceBiome biome)
            => Add(type, x, 0f, z, yaw, h, 0f, zone, biome, PlacementMap.GroundFor(zone, biome));

        public void Add(string type, float x, float y, float z, float yaw, float h, float footprint,
            PlaceZone zone, PlaceBiome biome, Ground ground)
        {
            if (!Objects.TryGetValue(type, out var list)) { list = new List<Item>(); Objects[type] = list; }
            list.Add(new Item { x = x, y = y, z = z, yaw = yaw, h = h, footprint = footprint,
                zone = zone, biome = biome, ground = ground, category = CategoryOf(type) });
        }

        public int Count { get { int n = 0; foreach (var kv in Objects) n += kv.Value.Count; return n; } }

        /// <summary>Fold another catalog's records into this one (used to merge the record-only
        /// scatter-detail catalog into the main export so the JSON covers every placed object).</summary>
        public void Merge(PlacementCatalog other)
        {
            foreach (var kv in other.Objects)
            {
                if (!Objects.TryGetValue(kv.Key, out var list)) { list = new List<Item>(); Objects[kv.Key] = list; }
                list.AddRange(kv.Value);
            }
        }

        /// <summary>Serialise to the source-of-truth JSON (hand-rolled so it stays dependency-free
        /// and diff-friendly: one object type per block, one placement per line).</summary>
        public string ToJson(PlacementMap map)
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\n  \"meta\": { \"origin\": [")
              .Append(map.OriginX.ToString("0.##")).Append(", ").Append(map.OriginZ.ToString("0.##"))
              .Append("], \"cell\": ").Append(PlacementMap.Cell.ToString("0.##"))
              .Append(", \"gridW\": ").Append(map.W).Append(", \"gridH\": ").Append(map.H)
              .Append(", \"total\": ").Append(Count).Append(" },\n  \"objects\": {\n");
            bool firstType = true;
            foreach (var kv in Objects)
            {
                if (!firstType) sb.Append(",\n");
                firstType = false;
                sb.Append("    \"").Append(kv.Key).Append("\": [\n");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var it = kv.Value[i];
                    sb.Append("      {\"p\":[").Append(it.x.ToString("0.##")).Append(",").Append(it.y.ToString("0.##")).Append(",").Append(it.z.ToString("0.##"))
                      .Append("],\"yaw\":").Append(it.yaw.ToString("0.#"))
                      .Append(",\"h\":").Append(it.h.ToString("0.##"))
                      .Append(",\"r\":").Append(it.footprint.ToString("0.##"))
                      .Append(",\"ground\":\"").Append(it.ground).Append("\"")
                      .Append(",\"zone\":\"").Append(it.zone).Append("\",\"biome\":\"").Append(it.biome).Append("\"}");
                    sb.Append(i < kv.Value.Count - 1 ? ",\n" : "\n");
                }
                sb.Append("    ]");
            }
            sb.Append("\n  }\n}\n");
            return sb.ToString();
        }

        /// <summary>Analyse the catalog against the zone map + neighbours; return human-readable
        /// conflict lines (empty = clean). This is the "infer issues from the data" pass.</summary>
        public List<string> Validate(PlacementMap map, System.Func<string, float> footprint)
        {
            var issues = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (var kv in Objects)
            {
                int inRoad = 0, inWater = 0, inWall = 0, inCity = 0, overlaps = 0, biomeMismatch = 0;
                var list = kv.Value;
                float rad = footprint(kv.Key);
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    var z = map.ZoneAt(it.x, it.z);
                    if (z == PlaceZone.Road) inRoad++;
                    else if (z == PlaceZone.Water) inWater++;
                    else if (z == PlaceZone.Wall) inWall++;
                    else if (z == PlaceZone.City) inCity++;
                    // dead trees only belong in volcanic/soot ground
                    if (kv.Key == "DeadTree" && it.biome != PlaceBiome.Volcanic) biomeMismatch++;
                    // overlap vs later same-type items (cheap O(n^2) on a per-type basis)
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var o = list[j];
                        float dd = (o.x - it.x) * (o.x - it.x) + (o.z - it.z) * (o.z - it.z);
                        if (dd < (rad * 0.9f) * (rad * 0.9f)) { overlaps++; break; }
                    }
                }
                counts[kv.Key] = list.Count;
                if (inRoad + inWater + inWall + inCity + overlaps + biomeMismatch > 0)
                    issues.Add($"{kv.Key} ({list.Count}): road={inRoad} water={inWater} wall={inWall} city={inCity} overlap={overlaps} biomeMismatch={biomeMismatch}");
            }
            var sb = new StringBuilder("CATALOG counts: ");
            foreach (var c in counts) sb.Append($"{c.Key}={c.Value} ");
            issues.Insert(0, sb.ToString());
            return issues;
        }
    }
}
