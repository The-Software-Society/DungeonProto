using UnityEngine;

namespace AldenFall.World
{
    [ExecuteInEditMode]
    public class MockupTile : MonoBehaviour
    {
        [SerializeField] private TileType tileType = TileType.Grass;
        [SerializeField] private Vector2Int gridSize = new Vector2Int(1, 1);
        [SerializeField] private float tileSize = 4f;
        [SerializeField] private float height = 0.1f;

        private TileType lastTileType;
        private MeshRenderer meshRenderer;

        public TileType TileTypeValue => tileType;
        public Vector2Int GridSize => gridSize;

        private void OnValidate()
        {
            if (tileType != lastTileType)
            {
                UpdateMaterial();
                lastTileType = tileType;
            }
            UpdateScale();
        }

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            UpdateMaterial();
            UpdateScale();
        }

        private void UpdateScale()
        {
            transform.localScale = new Vector3(
                gridSize.x * tileSize,
                height,
                gridSize.y * tileSize
            );
        }

        private void UpdateMaterial()
        {
            if (meshRenderer == null)
                meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer == null) return;

            var manager = FindFirstObjectByType<TileMaterialManager>();
            if (manager != null)
            {
                var mat = manager.GetMaterial(tileType);
                if (mat != null)
                    meshRenderer.sharedMaterial = mat;
            }
        }

        public void SetTileType(TileType type)
        {
            tileType = type;
            UpdateMaterial();
        }

        public static Color GetDefaultColor(TileType type)
        {
            return type switch
            {
                TileType.Cobblestone => new Color(0.5f, 0.5f, 0.55f),      // Gray
                TileType.Grass => new Color(0.3f, 0.6f, 0.3f),             // Green
                TileType.Building => new Color(0.55f, 0.4f, 0.3f),         // Brown/Tan
                TileType.Wall => new Color(0.35f, 0.35f, 0.35f),           // Dark Gray
                TileType.Water => new Color(0.2f, 0.4f, 0.8f),             // Blue
                TileType.Market => new Color(0.9f, 0.6f, 0.2f),            // Warm Orange
                TileType.Path => new Color(0.6f, 0.55f, 0.4f),             // Light Brown
                TileType.WoodPlatform => new Color(0.5f, 0.35f, 0.2f),     // Wood Brown
                TileType.Tavern => new Color(0.6f, 0.35f, 0.2f),           // Warm Brown
                TileType.Temple => new Color(0.9f, 0.85f, 0.6f),           // Cream/Gold
                TileType.Guild => new Color(0.4f, 0.3f, 0.5f),             // Purple
                TileType.Bank => new Color(0.8f, 0.75f, 0.5f),             // Gold/Tan
                TileType.Smithy => new Color(0.3f, 0.3f, 0.35f),           // Dark Metal
                _ => Color.magenta
            };
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = GetDefaultColor(tileType);
            Gizmos.DrawWireCube(transform.position, transform.localScale);
        }
    }
}
