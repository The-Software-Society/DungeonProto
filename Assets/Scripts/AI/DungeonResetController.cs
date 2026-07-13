using UnityEngine;
using AldenFall.Combat;
using AldenFall.World;

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

            // The dungeon is the Emberfall Volcano to the SOUTH, with the entrance mouth
            // (a 12m gap) on the north face. This safety envelope is derived entirely from
            // EmberfallVolcanoGeo (the single source of truth shared with the builder) so a
            // death-reset can never drop the player into the void; the north wall is split
            // around the entrance. Resize the dungeon by editing EmberfallVolcanoGeo only.
            float w = EmberfallVolcanoGeo.Width, d = EmberfallVolcanoGeo.DepthZ;
            float cz = EmberfallVolcanoGeo.CenterZ, hx = EmberfallVolcanoGeo.HalfWidth;
            float wy = EmberfallVolcanoGeo.WallCenterY, wh = EmberfallVolcanoGeo.WallHeight, wt = EmberfallVolcanoGeo.WallThickness;
            Slab(root.transform, "DungeonEnvelope_Floor", new Vector3(0f, EmberfallVolcanoGeo.FloorY, cz), new Vector3(w, 1f, d), floorMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_Ceiling", new Vector3(0f, EmberfallVolcanoGeo.CeilY, cz), new Vector3(w, 1f, d), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_West", new Vector3(-hx, wy, cz), new Vector3(wt, wh, d), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_East", new Vector3(hx, wy, cz), new Vector3(wt, wh, d), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_South", new Vector3(0f, wy, EmberfallVolcanoGeo.BackZ), new Vector3(w, wh, wt), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_NorthL", new Vector3(-EmberfallVolcanoGeo.NorthSegCenterX, wy, EmberfallVolcanoGeo.NorthZ), new Vector3(EmberfallVolcanoGeo.NorthSegWidth, wh, wt), wallMat, groundLayer);
            Slab(root.transform, "DungeonEnvelope_NorthR", new Vector3(EmberfallVolcanoGeo.NorthSegCenterX, wy, EmberfallVolcanoGeo.NorthZ), new Vector3(EmberfallVolcanoGeo.NorthSegWidth, wh, wt), wallMat, groundLayer);
        }

        private static void Slab(Transform parent, string name, Vector3 position, Vector3 scale, Material material, int layer)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.layer = layer;

            // Collider-only safety net: rendering these slabs painted a giant dark wall
            // across the volcano's north face (players read the mouth as "locked off")
            // and doubled every real wall with a phantom copy at a larger radius.
            var renderer = obj.GetComponent<Renderer>();
            if (renderer != null)
                Destroy(renderer);
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
