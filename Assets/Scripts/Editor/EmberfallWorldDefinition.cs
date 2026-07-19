#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace AldenFall.Editor
{
    /// <summary>Authoritative INPUT for Emberfall's large-scale composition. Exact generated
    /// placements remain compiler output; regions, traversal rooms, occluding landforms, vistas
    /// and budgets live in readable data and are consumed before terrain or scatter is built.</summary>
    [Serializable]
    public sealed class EmberfallWorldDefinition
    {
        public int version = 1;
        public int seed = 18442;
        public BoundsDef bounds = new();
        public PerformanceDef performance = new();
        public RegionDef[] regions = Array.Empty<RegionDef>();
        public RiverDef[] rivers = Array.Empty<RiverDef>();
        public LandformDef[] landforms = Array.Empty<LandformDef>();
        public VistaDef[] vistas = Array.Empty<VistaDef>();
        public RouteDef[] routes = Array.Empty<RouteDef>();

        [Serializable] public sealed class BoundsDef
        {
            public float minX = -450, minZ = -470, maxX = 450, maxZ = 570;
        }
        [Serializable] public sealed class PerformanceDef
        {
            public float chunkSize = 64;
            public float normalSightline = 140;
            public float heroRadius = 25;
            public float midRadius = 60;
            public float farRadius = 140;
            public int targetFps = 100;
        }
        [Serializable] public sealed class RegionDef
        {
            public string id, kind, biome, purpose;
            public float[] center = new float[2];
            public float radiusX, radiusZ;
            public float targetTraversalMinutes;
            public string[] connectsTo = Array.Empty<string>();
        }
        [Serializable] public sealed class LandformDef
        {
            public string id;
            public string kind = "hill";
            public float[] center = new float[2];
            public float radiusX = 60, radiusZ = 60, height = 18, yaw;
            public string purpose;
        }
        [Serializable] public sealed class VistaDef
        {
            public string id, displayName, rewardId;
            public float[] position = new float[3];
            public float yaw;
            public string reveal;
        }
        [Serializable] public sealed class RouteDef
        {
            public string id, from, to;
            public float width = 10;
            public PointDef[] points = Array.Empty<PointDef>();
        }
        [Serializable] public sealed class RiverDef
        {
            public string id, source, destination, purpose;
            public float halfWidth = 8;
            public PointDef[] points = Array.Empty<PointDef>();
        }
        [Serializable] public sealed class PointDef { public float x, y, z; }

        public Vector3[][] BuildRiverPaths()
        {
            var result = new Vector3[rivers.Length][];
            for (int i = 0; i < rivers.Length; i++)
            {
                result[i] = new Vector3[rivers[i].points.Length];
                for (int p = 0; p < rivers[i].points.Length; p++)
                {
                    var v = rivers[i].points[p];
                    result[i][p] = new Vector3(v.x, v.y, v.z);
                }
            }
            return result;
        }

        public static EmberfallWorldDefinition Load()
        {
            const string path = "Assets/World/Emberfall/world.json";
            if (!File.Exists(path)) throw new FileNotFoundException("Missing authoritative Emberfall world definition", path);
            var result = JsonUtility.FromJson<EmberfallWorldDefinition>(File.ReadAllText(path));
            if (result == null || result.version < 1) throw new InvalidDataException($"Invalid world definition: {path}");
            return result;
        }

        /// <summary>Elliptical authored terrain mass. Smooth compact support means adjacent rooms
        /// blend naturally but the compiler can still reason about the intended occluder.</summary>
        public float LandformHeight(float x, float z)
        {
            float result = 0f;
            foreach (var f in landforms)
            {
                if (f.center == null || f.center.Length < 2) continue;
                float a = -f.yaw * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float dx = x - f.center[0], dz = z - f.center[1];
                float lx = dx * ca - dz * sa, lz = dx * sa + dz * ca;
                float q = Mathf.Sqrt(lx * lx / Mathf.Max(1f, f.radiusX * f.radiusX)
                                   + lz * lz / Mathf.Max(1f, f.radiusZ * f.radiusZ));
                if (q >= 1f) continue;
                float t = 1f - Mathf.SmoothStep(0f, 1f, q);
                result = Mathf.Max(result, f.height * t * t);
            }
            return result;
        }
    }
}
#endif
