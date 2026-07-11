using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.AI
{
    public class DungeonResetController : MonoBehaviour
    {
        [SerializeField] private float resetDelay = 3.4f;
        [SerializeField] private string[] dungeonNamePrefixes =
        {
            // V2: only the Emberfall Volcano mobs are dungeon mobs. field_bandit/orc_brute are
            // now forest-ring mobs (they respawn on their own) so they are NOT reset here.
            "ember_acolyte",
            "stone_sentinel",
            "cave_troll_boss",
        };

        private void Start()
        {
            EnsureDungeonEnvelope();

            var player = GameObject.FindGameObjectWithTag("Player");
            var health = player != null ? player.GetComponent<Health>() : null;
            if (health != null)
                health.OnDeath.AddListener(() => Invoke(nameof(ResetDungeon), resetDelay));
        }

        private void EnsureDungeonEnvelope()
        {
            if (GameObject.Find("RuntimeDungeonEnvelope") != null)
                return;

            var root = new GameObject("RuntimeDungeonEnvelope");
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0) groundLayer = 0;

            var wallMat = RuntimeDungeonMaterial("RuntimeDungeonWall", new Color(0.10f, 0.09f, 0.11f));
            var floorMat = RuntimeDungeonMaterial("RuntimeDungeonFloor", new Color(0.14f, 0.13f, 0.15f));

            // V2: the dungeon is the Emberfall Volcano to the SOUTH — interior x[-56,56],
            // z[-450,-318] with the entrance mouth (12m gap) on the north face. This safety
            // envelope mirrors the built volcano walls so a death-reset can never drop the
            // player into the void; the north wall is split around the entrance.
            Slab(root.transform, "DungeonEnvelope_Floor", new Vector3(0f, -0.55f, -384f), new Vector3(112f, 1f, 132f), floorMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_Ceiling", new Vector3(0f, 8.5f, -384f), new Vector3(112f, 1f, 132f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_West", new Vector3(-56f, 4f, -384f), new Vector3(2f, 9f, 132f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_East", new Vector3(56f, 4f, -384f), new Vector3(2f, 9f, 132f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_South", new Vector3(0f, 4f, -450f), new Vector3(112f, 9f, 2f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_NorthL", new Vector3(-31f, 4f, -318f), new Vector3(50f, 9f, 2f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_NorthR", new Vector3(31f, 4f, -318f), new Vector3(50f, 9f, 2f), wallMat, groundLayer);
        }

        private static void Slab(Transform parent, string name, Vector3 position, Vector3 scale, Material material, int layer)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.layer = layer;

            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = material;
        }

        private static Material RuntimeDungeonMaterial(string name, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (material.shader == null)
                material = new Material(Shader.Find("Standard"));
            material.name = name;
            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            return material;
        }

        private void ResetDungeon()
        {
            foreach (var spawner in FindObjectsByType<EnemySpawner>(FindObjectsSortMode.None))
                if (IsDungeonObject(spawner.name)) spawner.ForceReset();

            foreach (var camp in FindObjectsByType<MobCamp>(FindObjectsSortMode.None))
                if (IsDungeonObject(camp.name)) camp.ForceReset();
        }

        private bool IsDungeonObject(string objectName)
        {
            foreach (var prefix in dungeonNamePrefixes)
                if (objectName.StartsWith(prefix)) return true;
            return false;
        }
    }
}
