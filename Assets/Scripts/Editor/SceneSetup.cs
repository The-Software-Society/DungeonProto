#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using TMPro;

namespace AldenFall.Editor
{
    public class SceneSetup : EditorWindow
    {
        [MenuItem("AldenFall/Build Complete Scene", priority = 1)]
        public static void BuildCompleteScene()
        {
            if (!EditorUtility.DisplayDialog("Build Complete Scene",
                "This will create a complete playable scene with:\n\n" +
                "- Millbrook Castle City (3-level medieval castle)\n" +
                "- NavMesh (auto-baked for AI)\n" +
                "- Player with all combat systems\n" +
                "- Test enemy with AI\n" +
                "- Camera and lighting\n" +
                "- Full UI (health, abilities, targeting)\n\n" +
                "Continue?",
                "Build", "Cancel"))
            {
                return;
            }

            float startTime = Time.realtimeSinceStartup;

            // Step 1: Build the city (this also clears any existing city)
            Debug.Log("Step 1/6: Building Millbrook City...");
            World.Editor.MillbrookBuilder.BuildMillbrook();

            // Step 2: Bake NavMesh
            Debug.Log("Step 2/6: Baking NavMesh...");
            World.Editor.MillbrookBuilder.BakeNavMesh();

            // Step 3: Create lighting
            Debug.Log("Step 3/6: Setting up lighting...");
            CreateLighting();

            // Step 4: Create player
            Debug.Log("Step 4/6: Creating player...");
            CreatePlayer();

            // Step 5: Create camera
            Debug.Log("Step 5/6: Setting up camera...");
            CreateCamera();

            // Step 6: Create enemy and UI
            Debug.Log("Step 6/6: Creating enemy and UI...");
            CreateTestEnemy();
            CreateUI();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            float totalTime = Time.realtimeSinceStartup - startTime;
            Debug.Log($"=== BUILD COMPLETE in {totalTime:F1}s ===");
            Debug.Log("Scene is ready to play! Press Play to test.");
            Debug.Log("Save your scene when done (Ctrl+S / Cmd+S).");
        }

        [MenuItem("AldenFall/Clear Scene", priority = 2)]
        public static void ClearScene()
        {
            if (!EditorUtility.DisplayDialog("Clear Scene",
                "This will remove the Millbrook city, player, enemies, and UI. Continue?",
                "Clear", "Cancel"))
            {
                return;
            }

            // Clear city
            World.Editor.MillbrookBuilder.ClearMillbrook();

            // Clear player
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) DestroyImmediate(player);

            // Clear enemies
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in enemies) if (e != null) DestroyImmediate(e);

            // Clear spawner
            var spawner = Object.FindFirstObjectByType<AI.EnemySpawner>();
            if (spawner != null) DestroyImmediate(spawner.gameObject);

            // Clear UI
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas != null && canvas.gameObject.name == "GameUI")
                DestroyImmediate(canvas.gameObject);

            // Clear test ground
            var ground = GameObject.Find("Ground");
            if (ground != null) DestroyImmediate(ground);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Scene cleared.");
        }

        [MenuItem("AldenFall/Fix Combat System", priority = 10)]
        public static void FixCombatSystem()
        {
            int fixedCount = 0;

            // Find Player in scene
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                // Ensure Player layer
                SafeSetLayer(player, "Player");

                // Fix TargetingSystem
                var targeting = player.GetComponent<Combat.TargetingSystem>();
                if (targeting != null)
                {
                    var targetingSO = new SerializedObject(targeting);
                    int enemyLayer = LayerMask.NameToLayer("Enemy");
                    if (enemyLayer >= 0)
                    {
                        // Include Default layer (1) as fallback
                        targetingSO.FindProperty("targetableLayers").intValue = (1 << enemyLayer) | 1;
                    }
                    else
                    {
                        // Enemy layer doesn't exist - use Default layer only
                        targetingSO.FindProperty("targetableLayers").intValue = 1;
                    }
                    targetingSO.ApplyModifiedProperties();
                    Debug.Log("Fixed TargetingSystem.targetableLayers → Enemy + Default layers");
                    fixedCount++;
                }

                // Fix PlayerAbilities - ensure fireball prefab
                var combat = player.GetComponent<Abilities.PlayerAbilities>();
                if (combat != null)
                {
                    var combatSO = new SerializedObject(combat);
                    var fireballProp = combatSO.FindProperty("fireballPrefab");

                    if (fireballProp.objectReferenceValue == null)
                    {
                        var fireballPrefab = GetOrCreateFireballPrefab();
                        fireballProp.objectReferenceValue = fireballPrefab;
                        combatSO.ApplyModifiedProperties();
                        Debug.Log("Assigned fireball prefab to PlayerCombat");
                        fixedCount++;
                    }

                    // Ensure firePoint exists
                    var firePointProp = combatSO.FindProperty("firePoint");
                    if (firePointProp.objectReferenceValue == null)
                    {
                        var existingFirePoint = player.transform.Find("FirePoint");
                        if (existingFirePoint == null)
                        {
                            var firePoint = new GameObject("FirePoint");
                            firePoint.transform.SetParent(player.transform);
                            firePoint.transform.localPosition = new Vector3(0, 1.5f, 1.5f); // Z=1.5 to spawn outside player capsule
                            existingFirePoint = firePoint.transform;
                        }
                        firePointProp.objectReferenceValue = existingFirePoint;
                        combatSO.ApplyModifiedProperties();
                        Debug.Log("Assigned FirePoint to PlayerCombat");
                        fixedCount++;
                    }
                }

                Debug.Log($"Player combat system configured");
            }
            else
            {
                Debug.LogWarning("No Player found in scene (needs 'Player' tag)");
            }

            // Find all Enemies in scene
            var enemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var enemy in enemies)
            {
                // Ensure Enemy layer
                SafeSetLayer(enemy, "Enemy");

                // Fix EnemyAI
                var enemyAI = enemy.GetComponent<AI.EnemyAI>();
                if (enemyAI != null)
                {
                    var aiSO = new SerializedObject(enemyAI);
                    int playerLayer = LayerMask.NameToLayer("Player");
                    if (playerLayer >= 0)
                    {
                        aiSO.FindProperty("playerLayer").intValue = (1 << playerLayer);
                        aiSO.ApplyModifiedProperties();
                    }
                }
                fixedCount++;
            }

            if (enemies.Length > 0)
            {
                Debug.Log($"Fixed {enemies.Length} enemy object(s) - layer and AI configured");
            }

            // Also find objects that might be enemies but aren't tagged
            var healthObjects = Object.FindObjectsByType<Combat.Health>(FindObjectsSortMode.None);
            int untaggedEnemies = 0;
            foreach (var health in healthObjects)
            {
                if (health.gameObject.CompareTag("Player")) continue;
                if (health.gameObject.CompareTag("Enemy")) continue;

                // This is likely an enemy without proper tag
                var enemyAI = health.GetComponent<AI.EnemyAI>();
                if (enemyAI != null)
                {
                    health.gameObject.tag = "Enemy";
                    SafeSetLayer(health.gameObject, "Enemy");
                    untaggedEnemies++;
                }
            }

            if (untaggedEnemies > 0)
            {
                Debug.Log($"Fixed {untaggedEnemies} untagged enemy object(s) - added 'Enemy' tag and layer");
                fixedCount += untaggedEnemies;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            if (fixedCount > 0)
            {
                Debug.Log($"Combat system fix complete! {fixedCount} items configured. Save your scene.");
            }
            else
            {
                Debug.Log("Combat system appears to be configured correctly.");
            }
        }

        private static Shader _workingShader;

        private static Shader GetWorkingShader()
        {
            if (_workingShader != null) return _workingShader;

            // Get shader from a primitive - this ALWAYS works
            GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _workingShader = tempCube.GetComponent<MeshRenderer>().sharedMaterial.shader;
            Object.DestroyImmediate(tempCube);
            return _workingShader;
        }

        private static Material CreateURPMaterial(Color color, string name = null)
        {
            Material mat = new Material(GetWorkingShader());

            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            mat.color = color;

            if (name != null)
                mat.name = name;

            return mat;
        }

        internal static Material GetOrCreateMaterialAsset(Color color, string name)
        {
            string path = $"Assets/Materials/{name}.mat";
            EnsureDirectoryExists("Assets/Materials");

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = CreateURPMaterial(color, name);
                AssetDatabase.CreateAsset(mat, path);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static void SafeSetLayer(GameObject obj, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0 && layer <= 31)
            {
                obj.layer = layer;
            }
            else
            {
                // Layer doesn't exist, use Default
                obj.layer = 0;
                Debug.LogWarning($"Layer '{layerName}' not found. Using Default layer. Add the layer in Edit > Project Settings > Tags and Layers.");
            }
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

        internal static GameObject CreatePlayerObject()
        {
            // Create player with capsule placeholder
            GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            player.tag = "Player";
            SafeSetLayer(player, "Player");

            // Remove default collider, add CharacterController
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());
            var cc = player.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.5f;
            cc.center = new Vector3(0, 1, 0);
            cc.stepOffset = 0.45f; // walkable pond steps / low rubble

            // Add components
            // NetworkIdentity is required because Health is a NetworkBehaviour (server-auth).
            player.AddComponent<Mirror.NetworkIdentity>();
            player.AddComponent<Player.PlayerController>();
            player.AddComponent<Combat.Health>();
            player.AddComponent<Combat.StatusSystem>();
            var targeting = player.AddComponent<Combat.TargetingSystem>();
            var combat = player.AddComponent<Abilities.PlayerAbilities>();
            player.AddComponent<Abilities.AbilityMastery>();
            player.AddComponent<Abilities.ActionBar>();
            player.AddComponent<Items.PlayerConsumables>();
            player.AddComponent<Player.PlayerDeath>();

            // Progression + items + stats (StatBlock = single source of truth)
            player.AddComponent<Stats.StatBlock>();
            player.AddComponent<Progression.Skills>();
            player.AddComponent<Stats.PlayerStatBridge>();
            player.AddComponent<Items.Inventory>();
            player.AddComponent<Items.Equipment>();
            player.AddComponent<Items.Wallet>();          // gold purse (economy)
            player.AddComponent<Quests.QuestLog>();        // per-player quest tracking
            player.AddComponent<World.PlayerWorldPhase>(); // per-player eruption phase (MMO phasing)
            player.AddComponent<Items.StarterLoadout>();
            player.AddComponent<Combat.PlayerMelee>();
            player.AddComponent<Combat.PlayerCombat>();   // server-auth damage relay (NetworkBehaviour → baked in prefab)
            player.AddComponent<Multiplayer.PartyMember>(); // party invite/roster relay (NetworkBehaviour → baked in prefab)
            player.AddComponent<Combat.DuelMember>();     // pairwise duel PvP relay (NetworkBehaviour → baked in prefab)
            player.AddComponent<Combat.PlayerDefence>();
            player.AddComponent<Combat.CombatStyleController>();
            player.AddComponent<UI.DebugCheatPanel>();   // F9 testing cheatsheet
            player.AddComponent<UI.QuestTrackerHUD>();    // quest tracker + gold readout
            player.AddComponent<UI.ForgePanel>();         // F7 gear-upgrade forge
            player.AddComponent<UI.VendorPanel>();        // market buy/sell (opened by VendorNPC)
            player.AddComponent<UI.PartyHUD>();           // minimal top-left party list + invite/duel prompts
            player.AddComponent<Player.CharacterAppearance>();      // v6 customization baseline
            player.AddComponent<UI.CharacterCustomizationUI>();     // F10 appearance panel
            player.AddComponent<Cinematics.CutsceneDirector>();     // v7.2 cutscene engine
            player.AddComponent<Cinematics.IntroCutsceneTrigger>(); // intro scene, once per save

            // Skilling: interact prompts, fishing, cooking
            player.AddComponent<Player.PlayerInteractor>();
            player.AddComponent<Player.FishingController>();
            player.AddComponent<Player.CookingController>();

            // Configure TargetingSystem - set targetableLayers to Enemy + Default fallback
            var targetingSO = new SerializedObject(targeting);
            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
            {
                // Include Default layer (1) as fallback in case Enemy layer doesn't exist on objects
                targetingSO.FindProperty("targetableLayers").intValue = (1 << enemyLayer) | 1;
            }
            else
            {
                // Enemy layer doesn't exist - use Default layer only
                targetingSO.FindProperty("targetableLayers").intValue = 1;
            }
            targetingSO.ApplyModifiedProperties();

            // Create fire point for projectiles
            var firePoint = new GameObject("FirePoint");
            firePoint.transform.SetParent(player.transform);
            firePoint.transform.localPosition = new Vector3(0, 1.5f, 1.5f); // Z=1.5 to spawn outside player capsule

            // Create and assign fireball prefab
            var fireballPrefab = GetOrCreateFireballPrefab();
            if (fireballPrefab != null)
            {
                var combatSO = new SerializedObject(combat);
                combatSO.FindProperty("fireballPrefab").objectReferenceValue = fireballPrefab;
                combatSO.FindProperty("firePoint").objectReferenceValue = firePoint.transform;
                combatSO.ApplyModifiedProperties();
            }

            // Visual child: v6 customization base bodies when generated (both
            // built in; CharacterAppearance activates the chosen one at
            // runtime), legacy armored Hero as fallback, capsule as last resort.
            var visual = new GameObject("Visual");
            visual.transform.SetParent(player.transform);
            visual.transform.localPosition = Vector3.zero;

            // Remove the root primitive's own mesh either way
            var renderer = player.GetComponent<MeshRenderer>();
            var filter = player.GetComponent<MeshFilter>();

            GameObject heroVisual = null;
            var maleBase = InstantiateGeneratedVisual("HeroMale", visual.transform);
            var femaleBase = InstantiateGeneratedVisual("HeroFemale", visual.transform);
            if (maleBase != null || femaleBase != null)
            {
                if (maleBase != null) maleBase.name = "Visual_Male";
                if (femaleBase != null)
                {
                    femaleBase.name = "Visual_Female";
                    femaleBase.SetActive(maleBase == null); // male is the build-time default
                }
                heroVisual = maleBase != null ? maleBase : femaleBase;
            }
            else
            {
                heroVisual = InstantiateGeneratedVisual("Hero", visual.transform);
            }

            if (heroVisual == null)
            {
                var capsuleVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsuleVisual.name = "CapsuleModel";
                capsuleVisual.transform.SetParent(visual.transform);
                capsuleVisual.transform.localPosition = new Vector3(0, 1, 0);
                Object.DestroyImmediate(capsuleVisual.GetComponent<CapsuleCollider>());

                var visualRenderer = capsuleVisual.GetComponent<MeshRenderer>();
                visualRenderer.sharedMaterial = GetOrCreateMaterialAsset(new Color(0.3f, 0.5f, 0.8f), "PlayerMaterial");
            }

            // Clean up original mesh components
            Object.DestroyImmediate(renderer);
            Object.DestroyImmediate(filter);

            return player;
        }

        internal static GameObject GetOrCreateFireballPrefab()
        {
            string prefabPath = "Assets/Prefabs/Fireball.prefab";
            EnsureDirectoryExists("Assets/Prefabs");

            // Check if prefab already exists — but force the no-knockback policy
            // onto old serialized copies (they carry the pre-v4 force of 8).
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existingPrefab != null)
            {
                var existingProjectile = existingPrefab.GetComponent<Combat.Projectile>();
                if (existingProjectile != null)
                {
                    var so = new SerializedObject(existingProjectile);
                    var kb = so.FindProperty("knockbackForce");
                    if (kb != null && kb.floatValue != 0f)
                    {
                        kb.floatValue = 0f;
                        so.ApplyModifiedProperties();
                        AssetDatabase.SaveAssets();
                    }
                }
                return existingPrefab;
            }

            // Create fireball
            var fireball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fireball.name = "Fireball";
            fireball.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            SafeSetLayer(fireball, "Projectile");

            // Remove collider, add trigger
            Object.DestroyImmediate(fireball.GetComponent<SphereCollider>());
            var collider = fireball.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = 0.5f;

            // Add Rigidbody (kinematic for manual movement)
            var rb = fireball.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Add Projectile component
            var projectile = fireball.AddComponent<Combat.Projectile>();

            // Configure Projectile (layers are resolved at runtime via GameLayers)
            var projectileSO = new SerializedObject(projectile);
            projectileSO.FindProperty("speed").floatValue = 18f;
            projectileSO.FindProperty("damage").floatValue = 25f;
            projectileSO.FindProperty("maxLifetime").floatValue = 5f;
            projectileSO.FindProperty("homingStrength").floatValue = 6f;
            projectileSO.ApplyModifiedProperties();

            // Orange/red material for fireball
            var renderer = fireball.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetOrCreateMaterialAsset(new Color(1f, 0.4f, 0.1f), "FireballMaterial");

            // Add trail renderer for visual effect
            var trail = fireball.AddComponent<TrailRenderer>();
            trail.startWidth = 0.3f;
            trail.endWidth = 0f;
            trail.time = 0.3f;
            trail.material = GetOrCreateMaterialAsset(new Color(1f, 0.6f, 0.2f), "FireballTrailMaterial");

            // Add light for glow effect
            var light = fireball.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.2f);
            light.intensity = 2f;
            light.range = 5f;

            // Save as prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(fireball, prefabPath);
            Object.DestroyImmediate(fireball);

            Debug.Log($"Fireball prefab created at {prefabPath}");
            return prefab;
        }

        private static void CreatePlayer()
        {
            // V2.5: safe spawn in Aldric's lush forest glade just NORTH of the city (0,0,90).
            // The intro meets Aldric here; the city is a short walk south through the north gate.
            Vector3 safeSpawnPosition = new Vector3(0, 1, 84);

            // Check if player already exists - move them instead of skipping
            var existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer != null)
            {
                existingPlayer.transform.position = safeSpawnPosition;
                Debug.Log($"Player moved to safe spawn position {safeSpawnPosition}");
                Selection.activeGameObject = existingPlayer;
                return;
            }

            var player = CreatePlayerObject();
            player.transform.position = safeSpawnPosition;

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

        internal static GameObject CreateEnemyObject()
        {
            // Create humanoid enemy
            GameObject enemy = new GameObject("Enemy_Goblin");
            enemy.tag = "Enemy";
            SafeSetLayer(enemy, "Enemy");

            // Add capsule collider for physics (invisible)
            var collider = enemy.AddComponent<CapsuleCollider>();
            collider.height = 2f;
            collider.radius = 0.5f;
            collider.center = new Vector3(0, 1, 0);

            // Colors
            Color bodyColor = new Color(0.8f, 0.2f, 0.2f);
            Color limbColor = new Color(0.6f, 0.15f, 0.15f);
            Color headColor = new Color(0.7f, 0.18f, 0.18f);

            // Create visual container
            var visual = new GameObject("Visual");
            visual.transform.SetParent(enemy.transform);
            visual.transform.localPosition = Vector3.zero;

            // Meshy-generated goblin model when available; blocky fallback otherwise
            var goblinVisual = InstantiateGeneratedVisual("Goblin", visual.transform);
            if (goblinVisual != null)
            {
                // Shorter model — tighten the physics capsule to match
                collider.height = 1.5f;
                collider.center = new Vector3(0, 0.75f, 0);
                AddEnemyRuntimeComponents(enemy);
                return enemy;
            }

            // Body (torso) - cube
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(visual.transform);
            body.transform.localPosition = new Vector3(0, 1.1f, 0);
            body.transform.localScale = new Vector3(0.6f, 0.7f, 0.35f);
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(bodyColor, "EnemyBodyMaterial");

            // Head - sphere
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(visual.transform);
            head.transform.localPosition = new Vector3(0, 1.7f, 0);
            head.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
            Object.DestroyImmediate(head.GetComponent<SphereCollider>());
            head.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(headColor, "EnemyHeadMaterial");

            // Left Arm
            var leftArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftArm.name = "LeftArm";
            leftArm.transform.SetParent(visual.transform);
            leftArm.transform.localPosition = new Vector3(-0.45f, 1.1f, 0);
            leftArm.transform.localScale = new Vector3(0.2f, 0.55f, 0.2f);
            Object.DestroyImmediate(leftArm.GetComponent<BoxCollider>());
            leftArm.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(limbColor, "EnemyLimbMaterial");

            // Right Arm
            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightArm.name = "RightArm";
            rightArm.transform.SetParent(visual.transform);
            rightArm.transform.localPosition = new Vector3(0.45f, 1.1f, 0);
            rightArm.transform.localScale = new Vector3(0.2f, 0.55f, 0.2f);
            Object.DestroyImmediate(rightArm.GetComponent<BoxCollider>());
            rightArm.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(limbColor, "EnemyLimbMaterial");

            // Left Leg
            var leftLeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftLeg.name = "LeftLeg";
            leftLeg.transform.SetParent(visual.transform);
            leftLeg.transform.localPosition = new Vector3(-0.15f, 0.35f, 0);
            leftLeg.transform.localScale = new Vector3(0.22f, 0.7f, 0.22f);
            Object.DestroyImmediate(leftLeg.GetComponent<BoxCollider>());
            leftLeg.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(limbColor, "EnemyLimbMaterial");

            // Right Leg
            var rightLeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightLeg.name = "RightLeg";
            rightLeg.transform.SetParent(visual.transform);
            rightLeg.transform.localPosition = new Vector3(0.15f, 0.35f, 0);
            rightLeg.transform.localScale = new Vector3(0.22f, 0.7f, 0.22f);
            Object.DestroyImmediate(rightLeg.GetComponent<BoxCollider>());
            rightLeg.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(limbColor, "EnemyLimbMaterial");

            // Eyes (small white spheres)
            var leftEye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leftEye.name = "LeftEye";
            leftEye.transform.SetParent(head.transform);
            leftEye.transform.localPosition = new Vector3(-0.25f, 0.1f, 0.4f);
            leftEye.transform.localScale = new Vector3(0.25f, 0.25f, 0.15f);
            Object.DestroyImmediate(leftEye.GetComponent<SphereCollider>());
            leftEye.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(Color.white, "EnemyEyeMaterial");

            var rightEye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rightEye.name = "RightEye";
            rightEye.transform.SetParent(head.transform);
            rightEye.transform.localPosition = new Vector3(0.25f, 0.1f, 0.4f);
            rightEye.transform.localScale = new Vector3(0.25f, 0.25f, 0.15f);
            Object.DestroyImmediate(rightEye.GetComponent<SphereCollider>());
            rightEye.GetComponent<MeshRenderer>().sharedMaterial = GetOrCreateMaterialAsset(Color.white, "EnemyEyeMaterial");

            AddEnemyRuntimeComponents(enemy);
            return enemy;
        }

        private static void AddEnemyRuntimeComponents(GameObject enemy)
        {
            // Add NavMeshAgent
            var agent = enemy.AddComponent<UnityEngine.AI.NavMeshAgent>();
            agent.height = 2f;
            agent.radius = 0.5f;
            agent.speed = 3.5f;

            // Add Health component (NetworkBehaviour → needs a NetworkIdentity on the object)
            enemy.AddComponent<Mirror.NetworkIdentity>();
            enemy.AddComponent<Combat.Health>();
            enemy.AddComponent<Combat.StatusSystem>();

            // Add EnemyAI and configure
            var enemyAI = enemy.AddComponent<AI.EnemyAI>();
            var aiSO = new SerializedObject(enemyAI);
            int playerLayerIndex = LayerMask.NameToLayer("Player");
            if (playerLayerIndex >= 0)
            {
                aiSO.FindProperty("playerLayer").intValue = (1 << playerLayerIndex);
                aiSO.FindProperty("detectionRange").floatValue = 25f;
                aiSO.FindProperty("attackRange").floatValue = 2.5f;
                aiSO.FindProperty("loseTargetRange").floatValue = 35f;
                aiSO.ApplyModifiedProperties();
            }

            // Add health bar
            enemy.AddComponent<UI.EnemyHealthBar>();
        }

        /// <summary>
        /// Instantiates Assets/Prefabs/Generated/<name>Visual.prefab (from the Meshy
        /// import pipeline) under the given parent. Returns null when not generated yet.
        /// </summary>
        internal static GameObject InstantiateGeneratedVisual(string name, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Generated/{name}Visual.prefab");
            if (prefab == null) return null;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static void CreateTestEnemy()
        {
            // Remove existing enemies first to prevent duplicates
            var existingEnemies = GameObject.FindGameObjectsWithTag("Enemy");
            foreach (var e in existingEnemies)
            {
                if (e != null)
                {
                    DestroyImmediate(e);
                }
            }

            // Also check for Health components without Enemy tag (incomplete enemies)
            var healthObjects = Object.FindObjectsByType<Combat.Health>(FindObjectsSortMode.None);
            foreach (var health in healthObjects)
            {
                if (health != null && !health.gameObject.CompareTag("Player"))
                {
                    var enemyAI = health.GetComponent<AI.EnemyAI>();
                    if (enemyAI != null)
                    {
                        DestroyImmediate(health.gameObject);
                    }
                }
            }

            Vector3 spawnPosition = new Vector3(0, 1, -35);

            var enemy = CreateEnemyObject();
            // Position at tile (15, 8) in castle city - cobblestone road, well south of fountain
            // Tile to world: (x - 15) * 5, y, (z - 15) * 5 = (0, y, -35)
            enemy.transform.position = spawnPosition;
            Undo.RegisterCreatedObjectUndo(enemy, "Create Enemy");

            // Create or update EnemySpawner for respawning
            var existingSpawner = Object.FindFirstObjectByType<AI.EnemySpawner>();
            if (existingSpawner == null)
            {
                var spawnerObj = new GameObject("EnemySpawner");
                var spawner = spawnerObj.AddComponent<AI.EnemySpawner>();

                // Configure spawner via SerializedObject
                var spawnerSO = new SerializedObject(spawner);
                spawnerSO.FindProperty("spawnPosition").vector3Value = spawnPosition;
                spawnerSO.FindProperty("respawnDelay").floatValue = 10f;
                spawnerSO.FindProperty("spawnOnStart").boolValue = false; // Already spawned one
                spawnerSO.ApplyModifiedProperties();

                Undo.RegisterCreatedObjectUndo(spawnerObj, "Create EnemySpawner");
                Debug.Log("EnemySpawner created for 10s respawn");
            }

            Debug.Log("Enemy spawned at (0, 1, -35). Run 'AldenFall > Bake NavMesh' to enable enemy AI movement.");
        }

        private static void CreateTestGround()
        {
            // Check if ground exists
            var existingGround = GameObject.Find("Ground");
            if (existingGround != null) return;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SafeSetLayer(ground, "Ground");
            ground.transform.localScale = new Vector3(10, 1, 10);
            ground.isStatic = true;

            // Green-ish material
            var renderer = ground.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateURPMaterial(new Color(0.3f, 0.5f, 0.3f));

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

        internal static void CreateUI()
        {
            // Remove existing UI if present
            var existingCanvas = Object.FindFirstObjectByType<Canvas>();
            if (existingCanvas != null && existingCanvas.gameObject.name == "GameUI")
            {
                Object.DestroyImmediate(existingCanvas.gameObject);
            }

            // Glassmorphism color palette
            Color glassBackground = new Color(0.08f, 0.04f, 0.12f, 0.75f);    // Dark purple-black glass
            Color glassBorder = new Color(0.4f, 0.3f, 0.5f, 0.6f);             // Purple border glow
            Color glassHighlight = new Color(1f, 1f, 1f, 0.1f);                // Top highlight
            Color healthGreen = new Color(0.2f, 0.85f, 0.4f, 1f);
            Color healthYellow = new Color(0.95f, 0.8f, 0.2f, 1f);
            Color healthRed = new Color(0.9f, 0.25f, 0.25f, 1f);

            // Create main canvas
            var canvasObj = new GameObject("GameUI");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObj.AddComponent<GraphicRaycaster>();

            // Add PlayerHUD component
            canvasObj.AddComponent<UI.PlayerHUD>();

            // ============ BOTTOM HUD CONTAINER ============
            // Container for the entire bottom HUD - centered
            var bottomHud = CreatePanel(canvasObj.transform, "BottomHUD",
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(750, 90));

            // ============ HEALTH BAR (Left side, no overlap) ============
            var healthPanel = CreateGlassPanel(bottomHud.transform, "HealthPanel",
                new Vector2(-260, 0), new Vector2(200, 45), glassBackground, glassBorder);

            // Health bar inner glow/highlight at top
            var healthHighlight = CreateImage(healthPanel.transform, "Highlight",
                new Vector2(0, 0.7f), new Vector2(1, 1), glassHighlight);
            var highlightRect = healthHighlight.GetComponent<RectTransform>();
            highlightRect.offsetMin = new Vector2(3, 0);
            highlightRect.offsetMax = new Vector2(-3, -3);

            // Health bar background track (darker, visible when health drains)
            var healthTrack = CreateImage(healthPanel.transform, "HealthTrack",
                new Vector2(0, 0), new Vector2(1, 1), new Color(0.1f, 0.08f, 0.08f, 0.95f));
            var trackRect = healthTrack.GetComponent<RectTransform>();
            trackRect.offsetMin = new Vector2(8, 10);
            trackRect.offsetMax = new Vector2(-8, -14);

            // Health bar fill - starts green, changes color based on health
            var playerHealthFill = CreateImage(healthPanel.transform, "HealthFill",
                new Vector2(0, 0), new Vector2(1, 1), new Color(0.2f, 0.85f, 0.2f, 1f)); // Bright green
            playerHealthFill.type = Image.Type.Filled;
            playerHealthFill.fillMethod = Image.FillMethod.Horizontal;
            playerHealthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            playerHealthFill.fillAmount = 1f;
            var fillRect = playerHealthFill.GetComponent<RectTransform>();
            fillRect.offsetMin = new Vector2(10, 12);
            fillRect.offsetMax = new Vector2(-10, -16);

            // Health text
            var playerHealthText = CreateText(healthPanel.transform, "HealthText",
                new Vector2(0, 0), new Vector2(1, 1), "100 / 100", 14, TextAlignmentOptions.Center);
            var healthTextRect = playerHealthText.GetComponent<RectTransform>();
            healthTextRect.offsetMin = new Vector2(8, 8);
            healthTextRect.offsetMax = new Vector2(-8, -12);
            playerHealthText.fontStyle = FontStyles.Bold;
            playerHealthText.outlineWidth = 0.2f;
            playerHealthText.outlineColor = new Color(0, 0, 0, 0.8f);

            // ============ ABILITY BAR (Center) ============
            var abilityBar = CreateGlassPanel(bottomHud.transform, "AbilityBar",
                new Vector2(0, 0), new Vector2(330, 70), glassBackground, glassBorder);

            // Ability bar highlight
            var abilityHighlight = CreateImage(abilityBar.transform, "Highlight",
                new Vector2(0, 0.75f), new Vector2(1, 1), glassHighlight);
            var abHighlightRect = abilityHighlight.GetComponent<RectTransform>();
            abHighlightRect.offsetMin = new Vector2(3, 0);
            abHighlightRect.offsetMax = new Vector2(-3, -3);

            // Create 5 ability slots (1-5)
            float slotSize = 52;
            float spacing = 8;
            float totalWidth = 5 * slotSize + 4 * spacing;
            float startX = -totalWidth / 2 + slotSize / 2;

            for (int i = 0; i < 5; i++)
            {
                CreateGlassmorphicAbilitySlot(abilityBar.transform, $"Ability{i + 1}",
                    new Vector2(startX + i * (slotSize + spacing), 0), slotSize, (i + 1).ToString(),
                    glassBackground, glassBorder);
            }

            // ============ STATUS PANEL (Right of abilities) ============
            var statusPanel = CreateGlassPanel(bottomHud.transform, "StatusPanel",
                new Vector2(220, 0), new Vector2(80, 70), glassBackground, glassBorder);
            statusPanel.SetActive(false); // Hidden until cooldown active

            // Dodge cooldown indicator
            var dodgeCooldownBg = new GameObject("DodgeCooldownBg");
            dodgeCooldownBg.transform.SetParent(statusPanel.transform, false);
            var dcbRect = dodgeCooldownBg.AddComponent<RectTransform>();
            dcbRect.anchorMin = new Vector2(0.5f, 0.5f);
            dcbRect.anchorMax = new Vector2(0.5f, 0.5f);
            dcbRect.anchoredPosition = new Vector2(0, 5);
            dcbRect.sizeDelta = new Vector2(50, 50);
            var dcbImg = dodgeCooldownBg.AddComponent<Image>();
            dcbImg.color = new Color(0.15f, 0.1f, 0.2f, 0.9f);

            // Dodge cooldown fill (radial)
            var dodgeCooldownFillObj = new GameObject("DodgeCooldownFill");
            dodgeCooldownFillObj.transform.SetParent(statusPanel.transform, false);
            var dcfRect = dodgeCooldownFillObj.AddComponent<RectTransform>();
            dcfRect.anchorMin = new Vector2(0.5f, 0.5f);
            dcfRect.anchorMax = new Vector2(0.5f, 0.5f);
            dcfRect.anchoredPosition = new Vector2(0, 5);
            dcfRect.sizeDelta = new Vector2(46, 46);
            var dodgeCooldownFill = dodgeCooldownFillObj.AddComponent<Image>();
            dodgeCooldownFill.color = new Color(0.6f, 0.4f, 0.8f, 0.8f); // Purple
            dodgeCooldownFill.type = Image.Type.Filled;
            dodgeCooldownFill.fillMethod = Image.FillMethod.Radial360;
            dodgeCooldownFill.fillClockwise = false;
            dodgeCooldownFill.fillOrigin = (int)Image.Origin360.Top;
            dodgeCooldownFill.fillAmount = 1f;

            // Dodge icon/label
            var dodgeLabel = CreateText(statusPanel.transform, "DodgeLabel",
                new Vector2(0, 0.6f), new Vector2(1, 0.9f), "DODGE", 10, TextAlignmentOptions.Center);
            dodgeLabel.fontStyle = FontStyles.Bold;

            // Cooldown text (countdown)
            var dodgeCooldownText = CreateText(statusPanel.transform, "DodgeCooldownText",
                new Vector2(0, 0.1f), new Vector2(1, 0.5f), "", 18, TextAlignmentOptions.Center);
            dodgeCooldownText.fontStyle = FontStyles.Bold;

            // ============ TARGET FRAME (Top center) ============
            var targetPanel = CreateGlassPanel(canvasObj.transform, "TargetPanel",
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(320, 70),
                glassBackground, glassBorder);
            targetPanel.SetActive(false);

            // Target highlight
            var targetHighlight = CreateImage(targetPanel.transform, "Highlight",
                new Vector2(0, 0.7f), new Vector2(1, 1), glassHighlight);
            var tHighlightRect = targetHighlight.GetComponent<RectTransform>();
            tHighlightRect.offsetMin = new Vector2(3, 0);
            tHighlightRect.offsetMax = new Vector2(-3, -3);

            // Target name
            var targetNameText = CreateText(targetPanel.transform, "TargetName",
                new Vector2(0, 0.55f), new Vector2(1, 1), "Enemy Name", 16, TextAlignmentOptions.Center);
            var tNameRect = targetNameText.GetComponent<RectTransform>();
            tNameRect.offsetMin = new Vector2(10, 0);
            tNameRect.offsetMax = new Vector2(-10, -8);
            targetNameText.fontStyle = FontStyles.Bold;

            // Target health track
            var targetHealthBg = CreateImage(targetPanel.transform, "TargetHealthBg",
                new Vector2(0, 0), new Vector2(1, 0.5f), new Color(0.15f, 0.1f, 0.2f, 0.9f));
            var thbRect = targetHealthBg.GetComponent<RectTransform>();
            thbRect.offsetMin = new Vector2(15, 8);
            thbRect.offsetMax = new Vector2(-15, -5);

            // Target health fill
            var targetHealthFill = CreateImage(targetPanel.transform, "TargetHealthFill",
                new Vector2(0, 0), new Vector2(1, 0.5f), healthRed);
            targetHealthFill.type = Image.Type.Filled;
            targetHealthFill.fillMethod = Image.FillMethod.Horizontal;
            var thfRect = targetHealthFill.GetComponent<RectTransform>();
            thfRect.offsetMin = new Vector2(17, 10);
            thfRect.offsetMax = new Vector2(-17, -7);

            // Target health text
            var targetHealthText = CreateText(targetPanel.transform, "TargetHealthText",
                new Vector2(0, 0), new Vector2(1, 0.5f), "100 / 100", 13, TextAlignmentOptions.Center);
            var thtRect = targetHealthText.GetComponent<RectTransform>();
            thtRect.offsetMin = new Vector2(15, 5);
            thtRect.offsetMax = new Vector2(-15, -5);

            // ============ WIRE UP REFERENCES ============
            var hud = canvasObj.GetComponent<UI.PlayerHUD>();
            var hudSO = new SerializedObject(hud);
            hudSO.FindProperty("playerHealthFill").objectReferenceValue = playerHealthFill;
            hudSO.FindProperty("playerHealthText").objectReferenceValue = playerHealthText.GetComponent<TMP_Text>();
            hudSO.FindProperty("targetPanel").objectReferenceValue = targetPanel;
            hudSO.FindProperty("targetHealthFill").objectReferenceValue = targetHealthFill;
            hudSO.FindProperty("targetNameText").objectReferenceValue = targetNameText.GetComponent<TMP_Text>();
            hudSO.FindProperty("targetHealthText").objectReferenceValue = targetHealthText.GetComponent<TMP_Text>();
            // Status display
            hudSO.FindProperty("statusPanel").objectReferenceValue = statusPanel;
            hudSO.FindProperty("dodgeCooldownFill").objectReferenceValue = dodgeCooldownFill;
            hudSO.FindProperty("dodgeCooldownText").objectReferenceValue = dodgeCooldownText.GetComponent<TMP_Text>();
            // Fireball cooldown (Ability1 slot's Cooldown child)
            var ability1Slot = abilityBar.transform.Find("Ability1");
            if (ability1Slot != null)
            {
                var fireballCooldown = ability1Slot.Find("Cooldown");
                if (fireballCooldown != null)
                {
                    hudSO.FindProperty("fireballCooldownFill").objectReferenceValue = fireballCooldown.GetComponent<Image>();
                }
            }
            // Nova cooldown (Ability2 slot's Cooldown child)
            var ability2Slot = abilityBar.transform.Find("Ability2");
            if (ability2Slot != null)
            {
                var novaCooldown = ability2Slot.Find("Cooldown");
                if (novaCooldown != null)
                {
                    hudSO.FindProperty("novaCooldownFill").objectReferenceValue = novaCooldown.GetComponent<Image>();
                }
            }
            hudSO.ApplyModifiedProperties();

            Undo.RegisterCreatedObjectUndo(canvasObj, "Create UI");
            Debug.Log("Glassmorphism UI created!");
        }

        private static GameObject CreateGlassPanel(Transform parent, string name, Vector2 anchoredPos, Vector2 size,
            Color bgColor, Color borderColor)
        {
            return CreateGlassPanel(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPos, size, bgColor, borderColor);
        }

        private static GameObject CreateGlassPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size, Color bgColor, Color borderColor)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            // Main glass background
            var bg = panel.AddComponent<Image>();
            bg.color = bgColor;

            // Outer glow/border effect
            var borderObj = new GameObject("Border");
            borderObj.transform.SetParent(panel.transform, false);
            var borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2, -2);
            borderRect.offsetMax = new Vector2(2, 2);
            var borderImg = borderObj.AddComponent<Image>();
            borderImg.color = borderColor;
            borderObj.transform.SetAsFirstSibling(); // Behind main panel

            // Inner shadow (darker at bottom)
            var shadowObj = new GameObject("InnerShadow");
            shadowObj.transform.SetParent(panel.transform, false);
            var shadowRect = shadowObj.AddComponent<RectTransform>();
            shadowRect.anchorMin = new Vector2(0, 0);
            shadowRect.anchorMax = new Vector2(1, 0.3f);
            shadowRect.offsetMin = Vector2.zero;
            shadowRect.offsetMax = Vector2.zero;
            var shadowImg = shadowObj.AddComponent<Image>();
            shadowImg.color = new Color(0, 0, 0, 0.25f);

            return panel;
        }

        private static void CreateGlassmorphicAbilitySlot(Transform parent, string name, Vector2 pos, float size,
            string keyLabel, Color bgColor, Color borderColor)
        {
            var slot = new GameObject(name);
            slot.transform.SetParent(parent, false);

            var rect = slot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(size, size);

            // Glass background
            var bg = slot.AddComponent<Image>();
            bg.color = new Color(bgColor.r * 1.2f, bgColor.g * 1.2f, bgColor.b * 1.2f, bgColor.a * 0.9f);

            // Border glow
            var borderObj = new GameObject("Border");
            borderObj.transform.SetParent(slot.transform, false);
            var borderRect = borderObj.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-1.5f, -1.5f);
            borderRect.offsetMax = new Vector2(1.5f, 1.5f);
            var borderImg = borderObj.AddComponent<Image>();
            borderImg.color = borderColor;
            borderObj.transform.SetAsFirstSibling();

            // Top highlight shine
            var shineObj = new GameObject("Shine");
            shineObj.transform.SetParent(slot.transform, false);
            var shineRect = shineObj.AddComponent<RectTransform>();
            shineRect.anchorMin = new Vector2(0.1f, 0.6f);
            shineRect.anchorMax = new Vector2(0.9f, 0.95f);
            shineRect.offsetMin = Vector2.zero;
            shineRect.offsetMax = Vector2.zero;
            var shineImg = shineObj.AddComponent<Image>();
            shineImg.color = new Color(1f, 1f, 1f, 0.12f);

            // Keybind number in top-left corner with background
            var keyBgObj = new GameObject("KeyBg");
            keyBgObj.transform.SetParent(slot.transform, false);
            var keyBgRect = keyBgObj.AddComponent<RectTransform>();
            keyBgRect.anchorMin = new Vector2(0, 1);
            keyBgRect.anchorMax = new Vector2(0, 1);
            keyBgRect.anchoredPosition = new Vector2(10, -10);
            keyBgRect.sizeDelta = new Vector2(20, 20);
            var keyBgImg = keyBgObj.AddComponent<Image>();
            keyBgImg.color = new Color(0.9f, 0.75f, 0.2f, 0.9f); // Gold background

            var keyText = CreateText(keyBgObj.transform, "KeyLabel",
                Vector2.zero, Vector2.one, keyLabel, 14, TextAlignmentOptions.Center);
            keyText.color = new Color(0.1f, 0.08f, 0.05f, 1f); // Dark text
            keyText.fontStyle = FontStyles.Bold;

            // Icon placeholder area (center)
            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(slot.transform, false);
            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.15f, 0.1f);
            iconRect.anchorMax = new Vector2(0.85f, 0.75f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImg = iconObj.AddComponent<Image>();
            iconImg.color = new Color(0.3f, 0.25f, 0.35f, 0.5f); // Placeholder

            // Cooldown overlay (radial fill)
            var cooldownObj = new GameObject("Cooldown");
            cooldownObj.transform.SetParent(slot.transform, false);
            var cooldownRect = cooldownObj.AddComponent<RectTransform>();
            cooldownRect.anchorMin = Vector2.zero;
            cooldownRect.anchorMax = Vector2.one;
            cooldownRect.offsetMin = new Vector2(2, 2);
            cooldownRect.offsetMax = new Vector2(-2, -2);
            var cooldownImg = cooldownObj.AddComponent<Image>();
            cooldownImg.color = new Color(0, 0, 0, 0.7f);
            cooldownImg.type = Image.Type.Filled;
            cooldownImg.fillMethod = Image.FillMethod.Radial360;
            cooldownImg.fillClockwise = false;
            cooldownImg.fillOrigin = (int)Image.Origin360.Top;
            cooldownImg.fillAmount = 0; // 0 = ready, 1 = on cooldown
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);

            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            return panel;
        }

        private static Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = obj.AddComponent<Image>();
            image.color = color;

            return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, string text, int fontSize, TextAlignmentOptions alignment)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            var rect = obj.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;

            return tmp;
        }

        private static GameObject CreateAbilitySlot(Transform parent, string name, Vector2 pos, float size, string keyLabel)
        {
            var slot = new GameObject(name);
            slot.transform.SetParent(parent, false);

            var rect = slot.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(size, size);

            // Background
            var bg = slot.AddComponent<Image>();
            bg.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);

            // Border
            var border = new GameObject("Border");
            border.transform.SetParent(slot.transform, false);
            var borderRect = border.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = Vector2.zero;
            borderRect.offsetMax = Vector2.zero;
            var borderImg = border.AddComponent<Image>();
            borderImg.color = new Color(0.4f, 0.4f, 0.4f, 1f);
            borderImg.type = Image.Type.Sliced;
            borderImg.fillCenter = false;

            // Key label
            var keyText = CreateText(slot.transform, "KeyLabel",
                new Vector2(0, 0), new Vector2(1, 0.3f), keyLabel, 12, TextAlignmentOptions.Center);
            keyText.color = new Color(0.8f, 0.8f, 0.2f);

            // Cooldown overlay
            var cooldown = CreateImage(slot.transform, "Cooldown",
                Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.7f));
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillClockwise = false;
            cooldown.fillAmount = 0;

            return slot;
        }
    }
}
#endif
