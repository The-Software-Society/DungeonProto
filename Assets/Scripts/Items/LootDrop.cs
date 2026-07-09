using UnityEngine;
using AldenFall.Multiplayer;

namespace AldenFall.Items
{
    /// <summary>
    /// A world item pickup: bobbing, spinning, glowing. Auto-collected when the
    /// player walks close. Spawned by enemies on death via LootDrop.Spawn.
    /// </summary>
    public class LootDrop : MonoBehaviour
    {
        private const float PickupRange = 1.8f;
        private const float Lifetime = 120f;

        private string itemId;
        private Transform player;
        private float baseY;
        private float age;
        private float pickupDelay;
        private Light glow;

        public string ItemId => itemId;

        /// <summary>
        /// Add to the bag if there's room, otherwise drop at the owner's feet —
        /// a created item must NEVER silently vanish (cooking with a full bag).
        /// Returns true when it went into the bag.
        /// </summary>
        public static bool GiveOrDrop(Inventory inventory, string itemId, Transform owner)
        {
            if (inventory != null && inventory.Add(itemId)) return true;

            Vector3 pos = owner.position + owner.forward * 1.2f;
            Spawn(itemId, pos, pickupDelay: 2f);
            return false;
        }

        public static LootDrop Spawn(string itemId, Vector3 position, float pickupDelay = 0f)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null) return null;

            var obj = new GameObject($"Loot_{def.Id}");
            obj.transform.position = position + Vector3.up * 0.5f;
            var drop = obj.AddComponent<LootDrop>();
            drop.itemId = itemId;
            drop.pickupDelay = pickupDelay;
            drop.BuildVisual(def);
            return drop;
        }

        private void BuildVisual(ItemDef def)
        {
            var library = ItemVisualLibrary.Instance;
            var modelPrefab = library != null ? library.GetModel(def.Id) : null;

            GameObject visual;
            if (modelPrefab != null)
            {
                visual = Instantiate(modelPrefab, transform);
                // Normalize to pickup size
                var renderers = visual.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
                    visual.transform.localScale *= 0.7f / longest;
                }
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(visual.GetComponent<Collider>());
                visual.transform.SetParent(transform, false);
                visual.transform.localScale = Vector3.one * 0.35f;
                var renderer = visual.GetComponent<MeshRenderer>();
                renderer.material.color = def.RarityColor;
                if (renderer.material.HasProperty("_EmissionColor"))
                {
                    renderer.material.EnableKeyword("_EMISSION");
                    renderer.material.SetColor("_EmissionColor", def.RarityColor * 1.5f);
                }
            }
            visual.transform.localPosition = Vector3.zero;

            // Rarity glow
            var glowObj = new GameObject("Glow");
            glowObj.transform.SetParent(transform, false);
            glow = glowObj.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = def.RarityColor;
            glow.intensity = 1.6f;
            glow.range = 3.5f;
            glow.shadows = LightShadows.None;

            baseY = transform.position.y;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age > Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // Bob + spin
            transform.position = new Vector3(
                transform.position.x,
                baseY + Mathf.Sin(Time.time * 2.2f) * 0.15f,
                transform.position.z);
            transform.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);

            // Pulse the glow slightly
            if (glow != null)
                glow.intensity = 1.6f + Mathf.Sin(Time.time * 3f) * 0.35f;

            TryPickup();
        }

        private void TryPickup()
        {
            // Grace period so an item the player just dropped isn't instantly
            // vacuumed back into the bag.
            if (age < pickupDelay) return;

            if (player == null)
            {
                var playerObj = LocalPlayerLocator.Find();
                if (playerObj == null) return;
                player = playerObj.transform;
            }

            if (Vector3.Distance(transform.position, player.position + Vector3.up) > PickupRange)
                return;

            var inventory = player.GetComponent<Inventory>();
            if (inventory != null && inventory.Add(itemId))
            {
                Destroy(gameObject);
            }
        }
    }
}
