using UnityEngine;

namespace AldenFall.World
{
    public enum TileType
    {
        Cobblestone,    // City streets - gray
        Grass,          // Open areas - green
        Building,       // Residential buildings - brown/tan
        Wall,           // City walls - dark gray
        Water,          // Rivers, fountains - blue
        Market,         // Market stalls - warm orange
        Path,           // Dirt paths - light brown
        WoodPlatform,   // Docks, platforms - wood brown
        Tavern,         // Tavern/Inn - warm brown
        Temple,         // Religious buildings - cream/gold
        Guild,          // Guild halls - purple/blue
        Bank,           // Banks - gold/tan
        Smithy          // Smithy/forge - dark metal
    }

    [System.Serializable]
    public class TileMaterialMapping
    {
        public TileType tileType;
        public Material material;
        public Color gizmoColor = Color.white;
    }
}
