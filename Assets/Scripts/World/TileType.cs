using UnityEngine;

namespace DungeonProto.World
{
    public enum TileType
    {
        Cobblestone,    // City streets - gray
        Grass,          // Open areas - green
        Building,       // Building footprints - brown/tan
        Wall,           // City walls - dark gray
        Water,          // Rivers, fountains - blue
        Market,         // Market stalls - warm orange
        Path,           // Dirt paths - light brown
        WoodPlatform    // Docks, platforms - wood brown
    }

    [System.Serializable]
    public class TileMaterialMapping
    {
        public TileType tileType;
        public Material material;
        public Color gizmoColor = Color.white;
    }
}
