#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace AldenFall.Editor
{
    internal static class AuthoredLayout
    {
        internal static T Load<T>(string path) where T : class
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Missing authored layout", path);
            var value = JsonUtility.FromJson<T>(File.ReadAllText(path));
            if (value == null) throw new InvalidDataException($"Invalid authored layout: {path}");
            return value;
        }
    }

    [Serializable] public sealed class EmberfallCityDefinition
    {
        public int version;
        public string id;
        public District[] districts = Array.Empty<District>();
        public Street[] streets = Array.Empty<Street>();
        public Bridge[] bridges = Array.Empty<Bridge>();
        public Building[] buildings = Array.Empty<Building>();
        [Serializable] public sealed class District { public string id, role, visualProfile; public float[] center; public float[] size; }
        [Serializable] public sealed class Street { public string id, kind; public float width; public Point[] points = Array.Empty<Point>(); }
        [Serializable] public sealed class Point { public float x, z; }
        [Serializable] public sealed class Bridge { public string id, street; public float[] position; public float yaw, width, length; }
        [Serializable] public sealed class Building
        {
            public string id, asset, district, role;
            public float[] position;
            public float yaw, width, depth;
            public int stories;
        }
        public static EmberfallCityDefinition Load() => AuthoredLayout.Load<EmberfallCityDefinition>("Assets/World/Emberfall/city.json");
    }

    [Serializable] public sealed class EmberfallDungeonDefinition
    {
        public int version;
        public string id, visualProfile;
        public float[] origin;
        public Room[] rooms = Array.Empty<Room>();
        public Link[] links = Array.Empty<Link>();
        public Encounter[] encounters = Array.Empty<Encounter>();
        [Serializable] public sealed class Room
        {
            public string id, kind, purpose;
            public float[] center, size;
            public int streamGroup;
        }
        [Serializable] public sealed class Link
        {
            public string id, from, to, kind;
            public float width;
            public bool gated;
        }
        [Serializable] public sealed class Encounter { public string id, room, archetype, reward; }
        public static EmberfallDungeonDefinition Load() => AuthoredLayout.Load<EmberfallDungeonDefinition>("Assets/World/Emberfall/dungeon.json");
    }

    [Serializable] public sealed class EmberfallEncounterDefinition
    {
        public int version;
        public Camp[] camps = Array.Empty<Camp>();
        [Serializable] public sealed class Camp
        {
            public string id, mobId, region, purpose, kind = "camp";
            public float[] position;
            public int count = 1;
            public float radius = 8, respawn = 20;
        }
        public static EmberfallEncounterDefinition Load() => AuthoredLayout.Load<EmberfallEncounterDefinition>("Assets/World/Emberfall/encounters.json");
    }
}
#endif
