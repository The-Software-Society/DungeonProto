#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace DungeonProto.World.Editor
{
    public class CityLayoutEditor : EditorWindow
    {
        private TileType selectedTileType = TileType.Cobblestone;
        private Vector2Int gridSize = new Vector2Int(1, 1);
        private float tileSize = 4f;
        private float tileHeight = 0.1f;

        private Vector2 scrollPosition;
        private GameObject cityRoot;
        private Material[] tileMaterials;
        private bool showGrid = true;

        [MenuItem("DungeonProto/City Layout Editor")]
        public static void ShowWindow()
        {
            GetWindow<CityLayoutEditor>("City Layout Editor");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            CreateDefaultMaterials();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void CreateDefaultMaterials()
        {
            tileMaterials = new Material[System.Enum.GetValues(typeof(TileType)).Length];

            int index = 0;
            foreach (TileType type in System.Enum.GetValues(typeof(TileType)))
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = MockupTile.GetDefaultColor(type);
                mat.name = $"Tile_{type}";
                tileMaterials[index++] = mat;
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("City Layout Editor", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // City Root
            EditorGUILayout.LabelField("Scene Setup", EditorStyles.boldLabel);
            cityRoot = (GameObject)EditorGUILayout.ObjectField("City Root", cityRoot, typeof(GameObject), true);

            if (cityRoot == null)
            {
                if (GUILayout.Button("Create City Root"))
                {
                    CreateCityRoot();
                }
            }

            EditorGUILayout.Space();

            // Tile Settings
            EditorGUILayout.LabelField("Tile Settings", EditorStyles.boldLabel);
            selectedTileType = (TileType)EditorGUILayout.EnumPopup("Tile Type", selectedTileType);
            gridSize = EditorGUILayout.Vector2IntField("Grid Size", gridSize);
            tileSize = EditorGUILayout.FloatField("Tile Size", tileSize);
            tileHeight = EditorGUILayout.FloatField("Tile Height", tileHeight);

            EditorGUILayout.Space();

            // Preview
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            Color previewColor = MockupTile.GetDefaultColor(selectedTileType);
            EditorGUILayout.ColorField("Color", previewColor);
            showGrid = EditorGUILayout.Toggle("Show Grid in Scene", showGrid);

            EditorGUILayout.Space();

            // Quick place buttons
            EditorGUILayout.LabelField("Quick Place", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Place at Origin"))
            {
                PlaceTile(Vector3.zero);
            }

            if (GUILayout.Button("Place at Selection"))
            {
                if (Selection.activeTransform != null)
                {
                    PlaceTile(Selection.activeTransform.position);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Tile type quick buttons with colors
            EditorGUILayout.LabelField("Tile Types", EditorStyles.boldLabel);

            int buttonsPerRow = 4;
            int buttonIndex = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (TileType type in System.Enum.GetValues(typeof(TileType)))
            {
                GUI.backgroundColor = MockupTile.GetDefaultColor(type);
                if (GUILayout.Button(type.ToString(), GUILayout.Width(80)))
                {
                    selectedTileType = type;
                }
                buttonIndex++;

                if (buttonIndex % buttonsPerRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Millbrook preset layouts
            EditorGUILayout.LabelField("Millbrook Presets", EditorStyles.boldLabel);

            if (GUILayout.Button("Create Market Square (8x8)"))
            {
                CreateMarketSquare();
            }

            if (GUILayout.Button("Create City Walls"))
            {
                CreateCityWalls();
            }

            if (GUILayout.Button("Create Fountain (2x2)"))
            {
                CreateFountain();
            }

            EditorGUILayout.EndScrollView();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showGrid || cityRoot == null) return;

            Handles.color = new Color(1, 1, 1, 0.3f);

            // Draw grid
            for (int x = -20; x <= 20; x++)
            {
                Handles.DrawLine(
                    new Vector3(x * tileSize, 0, -20 * tileSize),
                    new Vector3(x * tileSize, 0, 20 * tileSize)
                );
            }

            for (int z = -20; z <= 20; z++)
            {
                Handles.DrawLine(
                    new Vector3(-20 * tileSize, 0, z * tileSize),
                    new Vector3(20 * tileSize, 0, z * tileSize)
                );
            }
        }

        private void CreateCityRoot()
        {
            cityRoot = new GameObject("Millbrook_City");

            // Create child containers
            new GameObject("Ground").transform.SetParent(cityRoot.transform);
            new GameObject("Buildings").transform.SetParent(cityRoot.transform);
            new GameObject("Walls").transform.SetParent(cityRoot.transform);
            new GameObject("Props").transform.SetParent(cityRoot.transform);
            new GameObject("NPCs").transform.SetParent(cityRoot.transform);

            // Add TileMaterialManager
            var manager = cityRoot.AddComponent<TileMaterialManager>();

            Selection.activeGameObject = cityRoot;
            Undo.RegisterCreatedObjectUndo(cityRoot, "Create City Root");
        }

        private void PlaceTile(Vector3 position)
        {
            if (cityRoot == null)
            {
                CreateCityRoot();
            }

            // Snap to grid
            position.x = Mathf.Round(position.x / tileSize) * tileSize;
            position.z = Mathf.Round(position.z / tileSize) * tileSize;
            position.y = 0;

            // Create tile
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = $"Tile_{selectedTileType}_{position.x}_{position.z}";

            // Position and scale
            tile.transform.position = position;
            tile.transform.localScale = new Vector3(
                gridSize.x * tileSize,
                tileHeight,
                gridSize.y * tileSize
            );

            // Parent to appropriate container
            Transform parent = GetParentForTileType(selectedTileType);
            tile.transform.SetParent(parent);

            // Apply material
            var renderer = tile.GetComponent<MeshRenderer>();
            int materialIndex = (int)selectedTileType;
            if (materialIndex < tileMaterials.Length && tileMaterials[materialIndex] != null)
            {
                renderer.sharedMaterial = tileMaterials[materialIndex];
            }

            // Add MockupTile component
            var mockupTile = tile.AddComponent<MockupTile>();

            Selection.activeGameObject = tile;
            Undo.RegisterCreatedObjectUndo(tile, "Place Tile");
        }

        private Transform GetParentForTileType(TileType type)
        {
            if (cityRoot == null) return null;

            string containerName = type switch
            {
                TileType.Cobblestone or TileType.Grass or TileType.Path => "Ground",
                TileType.Building => "Buildings",
                TileType.Wall => "Walls",
                _ => "Props"
            };

            Transform container = cityRoot.transform.Find(containerName);
            return container ?? cityRoot.transform;
        }

        private void CreateMarketSquare()
        {
            if (cityRoot == null) CreateCityRoot();

            Vector3 center = Vector3.zero;

            // 8x8 cobblestone area
            for (int x = -4; x < 4; x++)
            {
                for (int z = -4; z < 4; z++)
                {
                    Vector3 pos = center + new Vector3(x * tileSize + tileSize / 2, 0, z * tileSize + tileSize / 2);
                    selectedTileType = TileType.Cobblestone;
                    gridSize = Vector2Int.one;
                    PlaceTile(pos);
                }
            }

            // Market stalls around edges
            selectedTileType = TileType.Market;
            for (int i = -3; i <= 3; i += 2)
            {
                PlaceTile(center + new Vector3(i * tileSize, 0, 3.5f * tileSize));
                PlaceTile(center + new Vector3(i * tileSize, 0, -3.5f * tileSize));
            }
        }

        private void CreateCityWalls()
        {
            if (cityRoot == null) CreateCityRoot();

            selectedTileType = TileType.Wall;
            tileHeight = 3f;
            gridSize = new Vector2Int(1, 1);

            int wallLength = 15;
            float wallOffset = wallLength * tileSize / 2;

            // North and South walls
            for (int x = -wallLength / 2; x <= wallLength / 2; x++)
            {
                PlaceTile(new Vector3(x * tileSize, 0, wallOffset));
                PlaceTile(new Vector3(x * tileSize, 0, -wallOffset));
            }

            // East and West walls
            for (int z = -wallLength / 2; z <= wallLength / 2; z++)
            {
                PlaceTile(new Vector3(wallOffset, 0, z * tileSize));
                PlaceTile(new Vector3(-wallOffset, 0, z * tileSize));
            }

            tileHeight = 0.1f; // Reset
        }

        private void CreateFountain()
        {
            if (cityRoot == null) CreateCityRoot();

            selectedTileType = TileType.Water;
            gridSize = new Vector2Int(2, 2);
            PlaceTile(Vector3.zero);
            gridSize = Vector2Int.one;
        }
    }
}
#endif
