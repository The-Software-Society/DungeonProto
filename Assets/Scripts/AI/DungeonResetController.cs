using UnityEngine;
using AldenFall.Combat;

namespace AldenFall.AI
{
    public class DungeonResetController : MonoBehaviour
    {
        [SerializeField] private float resetDelay = 3.4f;
        [SerializeField] private string[] dungeonNamePrefixes =
        {
            "ember_acolyte",
            "stone_sentinel",
            "cave_troll_boss",
            "field_bandit",   // dungeon-entrance guards — reset on re-entry (no longer infinite-respawn)
            "orc_brute"
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

            Slab(root.transform, "DungeonEnvelope_Floor", new Vector3(0f, -0.55f, 262f), new Vector3(104f, 1f, 170f), floorMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_Ceiling", new Vector3(0f, 8.5f, 262f), new Vector3(104f, 1f, 170f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_West", new Vector3(-52f, 4f, 262f), new Vector3(2f, 9f, 170f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_East", new Vector3(52f, 4f, 262f), new Vector3(2f, 9f, 170f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_North", new Vector3(0f, 4f, 347f), new Vector3(104f, 9f, 2f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_SouthL", new Vector3(-31f, 4f, 177f), new Vector3(42f, 9f, 2f), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_SouthR", new Vector3(31f, 4f, 177f), new Vector3(42f, 9f, 2f), wallMat, groundLayer);
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
