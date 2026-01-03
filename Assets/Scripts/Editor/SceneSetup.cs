#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace DungeonProto.Editor
{
    public class SceneSetup : EditorWindow
    {
        [MenuItem("DungeonProto/Setup New Scene")]
        public static void SetupNewScene()
        {
            if (!EditorUtility.DisplayDialog("Setup Scene",
                "This will set up the current scene with player, camera, lighting, and test enemy. Continue?",
                "Yes", "Cancel"))
            {
                return;
            }

            CreateLighting();
            CreatePlayer();
            CreateCamera();
            CreateTestEnemy();
            CreateTestGround();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Scene setup complete! Remember to save the scene.");
        }

        [MenuItem("DungeonProto/Create Player Prefab")]
        public static void CreatePlayerPrefab()
        {
            GameObject player = CreatePlayerObject();

            string path = "Assets/Prefabs/Player.prefab";
            EnsureDirectoryExists("Assets/Prefabs");

            PrefabUtility.SaveAsPrefabAsset(player, path);
            Debug.Log($"Player prefab created at {path}");
        }

        [MenuItem("DungeonProto/Create Enemy Prefab")]
        public static void CreateEnemyPrefab()
        {
            GameObject enemy = CreateEnemyObject();

            string path = "Assets/Prefabs/Enemy.prefab";
            EnsureDirectoryExists("Assets/Prefabs");

            PrefabUtility.SaveAsPrefabAsset(enemy, path);
            DestroyImmediate(enemy);
            Debug.Log($"Enemy prefab created at {path}");
        }

        private static void CreateLighting()
        {
            // Directional light
            var existingLight = Object.FindFirstObjectByType<Light>();
            if (existingLight == null)
            {
                var lightObj = new GameObject("Directional Light");
                var light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(1f, 0.96f, 0.84f);
                light.intensity = 1.2f;
                lightObj.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
        }

        private static GameObject CreatePlayerObject()
        {
            // Create player with capsule placeholder
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            player.layer = LayerMask.NameToLayer("Player");

            // Remove default collider, add CharacterController
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.5f;
            cc.center = new Vector3(0, 1, 0);

            // Add components
            player.AddComponent<Player.PlayerController>();
            player.AddComponent<Combat.Health>();
            player.AddComponent<Combat.TargetingSystem>();
            player.AddComponent<Combat.PlayerCombat>();
            player.AddComponent<Player.PlayerDeath>();

            // Visual child (for future model swap)
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform);
            visual.transform.localPosition = Vector3.zero;

            // Move renderer to visual
            var renderer = player.GetComponent<MeshRenderer>();
            var filter = player.GetComponent<MeshFilter>();

            var capsuleVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsuleVisual.name = "CapsuleModel";
            capsuleVisual.transform.SetParent(visual.transform);
            capsuleVisual.transform.localPosition = new Vector3(0, 1, 0);
            Object.DestroyImmediate(capsuleVisual.GetComponent<CapsuleCollider>());

            // Clean up original mesh components
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(filter);

            return player;
        }

        private static void CreatePlayer()
        {
            // Check if player already exists
            var existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer != null)
            {
                Debug.Log("Player already exists in scene");
                return;
            }

            var player = CreatePlayerObject();
            player.transform.position = new Vector3(0, 0, 0);

            Selection.activeGameObject = player;
            Undo.RegisterCreatedObjectUndo(player, "Create Player");
        }

        private static void CreateCamera()
        {
            // Find or create main camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                var camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                mainCam = camObj.AddComponent<Camera>();
                camObj.AddComponent<AudioListener>();
            }

            // Add third person camera script
            if (mainCam.GetComponent<Player.ThirdPersonCamera>() == null)
            {
                mainCam.gameObject.AddComponent<Player.ThirdPersonCamera>();
            }

            mainCam.transform.position = new Vector3(0, 5, -8);
            mainCam.transform.LookAt(Vector3.zero + Vector3.up);
        }

        private static GameObject CreateEnemyObject()
        {
            GameObject enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemy.name = "Enemy_Goblin";
            enemy.tag = "Enemy";
            enemy.layer = LayerMask.NameToLayer("Enemy");

            // Setup collider
            var collider = enemy.GetComponent<CapsuleCollider>();
            collider.height = 2f;
            collider.radius = 0.5f;
            collider.center = new Vector3(0, 1, 0);

            // Add NavMeshAgent
            var agent = enemy.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height = 2f;
            agent.radius = 0.5f;
            agent.speed = 3.5f;

            // Add components
            enemy.AddComponent<Combat.Health>();
            enemy.AddComponent<AI.EnemyAI>();

            // Red material to distinguish
            var renderer = enemy.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.8f, 0.2f, 0.2f);
            renderer.sharedMaterial = mat;

            return enemy;
        }

        private static void CreateTestEnemy()
        {
            var enemy = CreateEnemyObject();
            enemy.transform.position = new Vector3(5, 0, 5);
            Undo.RegisterCreatedObjectUndo(enemy, "Create Enemy");
        }

        private static void CreateTestGround()
        {
            // Check if ground exists
            var existingGround = GameObject.Find("Ground");
            if (existingGround != null) return;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.layer = LayerMask.NameToLayer("Ground");
            ground.transform.localScale = new Vector3(10, 1, 10);
            ground.isStatic = true;

            // Green-ish material
            var renderer = ground.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(0.3f, 0.5f, 0.3f);
            renderer.sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] folders = path.Split('/');
                string currentPath = folders[0];

                for (int i = 1; i < folders.Length; i++)
                {
                    string newPath = currentPath + "/" + folders[i];
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, folders[i]);
                    }
                    currentPath = newPath;
                }
            }
        }
    }
}
#endif
