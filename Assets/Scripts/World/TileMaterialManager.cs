using UnityEngine;
using System.Collections.Generic;

namespace DungeonProto.World
{
    public class TileMaterialManager : MonoBehaviour
    {
        [SerializeField] private List<TileMaterialMapping> materialMappings = new List<TileMaterialMapping>();

        private Dictionary<TileType, Material> materialLookup;

        private void Awake()
        {
            BuildLookup();
        }

        private void BuildLookup()
        {
            materialLookup = new Dictionary<TileType, Material>();
            foreach (var mapping in materialMappings)
            {
                if (!materialLookup.ContainsKey(mapping.tileType))
                {
                    materialLookup.Add(mapping.tileType, mapping.material);
                }
            }
        }

        public Material GetMaterial(TileType type)
        {
            if (materialLookup == null)
                BuildLookup();

            return materialLookup.TryGetValue(type, out var mat) ? mat : null;
        }

        [ContextMenu("Generate Default Materials")]
        public void GenerateDefaultMaterials()
        {
            materialMappings.Clear();

            foreach (TileType type in System.Enum.GetValues(typeof(TileType)))
            {
                var mapping = new TileMaterialMapping
                {
                    tileType = type,
                    gizmoColor = MockupTile.GetDefaultColor(type)
                };
                materialMappings.Add(mapping);
            }
        }
    }
}
