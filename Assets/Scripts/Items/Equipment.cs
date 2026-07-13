using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using AldenFall.Stats;

namespace AldenFall.Items
{
    /// <summary>
    /// Six-slot equipment (main hand/helm/chest/legs/cape/off hand). The Weapon
    /// slot is the MAIN HAND and Focus is the OFF HAND: they are fully
    /// independent, so a sword + focus (or wand + focus) both apply. Equipping:
    ///   1. writes the item's bonuses into the StatBlock (source = slot name)
    ///   2. attaches the item's model to the right bone so gear is VISIBLE
    /// Bone lookup is fuzzy so it works across differently-named Meshy rigs.
    /// </summary>
    public class Equipment : MonoBehaviour
    {
        [Header("Events")]
        public UnityEvent OnEquipmentChanged;

        private Inventory inventory;
        private StatBlock stats;
        private readonly Dictionary<EquipSlot, string> equipped = new Dictionary<EquipSlot, string>();
        private readonly Dictionary<EquipSlot, GameObject> visuals = new Dictionary<EquipSlot, GameObject>();

        public static readonly EquipSlot[] Slots =
        {
            EquipSlot.Weapon, EquipSlot.Helm, EquipSlot.Chest, EquipSlot.Legs, EquipSlot.Cape,
            EquipSlot.Focus, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Necklace,
            EquipSlot.Ring1, EquipSlot.Ring2,
        };

        private void Awake()
        {
            inventory = GetComponent<Inventory>();
            stats = GetComponent<StatBlock>();
        }

        public string GetEquipped(EquipSlot slot) =>
            equipped.TryGetValue(slot, out var id) ? id : null;

        public ItemDef GetEquippedDef(EquipSlot slot) => ItemDatabase.Get(GetEquipped(slot));

        public bool IsEquipped(string itemId) => equipped.ContainsValue(itemId);

        // Convenience for older callers/UI
        public string WeaponId => GetEquipped(EquipSlot.Weapon);
        public float WeaponPower => stats != null ? stats.Get(StatType.WeaponPower) : 0f;

        // ---------- two-hand queries (ability/auto gating) ----------

        /// <summary>True when a Sword-class weapon is in the main hand (melee ability gate).</summary>
        public bool HasSwordMainHand
        {
            get
            {
                var main = GetEquippedDef(EquipSlot.Weapon);
                return main != null && main.WeaponKind == WeaponKind.Sword;
            }
        }

        /// <summary>
        /// True when the player holds ANY magic implement: a focus in the off
        /// hand or a wand in the main hand (magic ability + magic auto gate).
        /// </summary>
        public bool HasMagicImplement
        {
            get
            {
                if (GetEquippedDef(EquipSlot.Focus) != null) return true;
                var main = GetEquippedDef(EquipSlot.Weapon);
                return main != null && main.WeaponKind == WeaponKind.Wand;
            }
        }

        /// <summary>True when a Bow is in the main hand (archery auto + ability gate).</summary>
        public bool HasBow
        {
            get
            {
                var main = GetEquippedDef(EquipSlot.Weapon);
                return main != null && main.WeaponKind == WeaponKind.Bow;
            }
        }

        /// <summary>Equip from inventory; the displaced item returns to the bag.</summary>
        public bool Equip(string itemId)
        {
            var def = ItemDatabase.Get(itemId);
            if (def == null || def.Slot == EquipSlot.None) return false;
            if (inventory != null && !inventory.Contains(itemId)) return false;

            // Rings are authored as Ring1 but fill Ring1 then Ring2.
            EquipSlot slot = def.Slot;
            if (slot == EquipSlot.Ring1 && GetEquipped(EquipSlot.Ring1) != null && GetEquipped(EquipSlot.Ring2) == null)
                slot = EquipSlot.Ring2;

            string previous = GetEquipped(slot);
            if (previous == itemId) return true;

            inventory?.Remove(itemId);
            if (previous != null)
                inventory?.Add(previous);

            equipped[slot] = itemId;
            stats?.ApplyModifiers($"equip:{slot}",
                ItemPlus.ScaledBonuses(def, ItemPlus.PlusOf(itemId)));
            RefreshVisual(slot, def);

            OnEquipmentChanged?.Invoke();
            return true;
        }

        /// <summary>Wipe every equipped item (stats + visuals) WITHOUT returning them to the
        /// bag, for a save-load restore that rebuilds equipment from scratch.</summary>
        public void ClearAll()
        {
            foreach (var slot in new System.Collections.Generic.List<EquipSlot>(equipped.Keys))
                stats?.ClearModifiers($"equip:{slot}");
            equipped.Clear();
            foreach (var visual in visuals.Values)
                if (visual != null) Destroy(visual);
            visuals.Clear();
            OnEquipmentChanged?.Invoke();
        }

        /// <summary>Unequip a slot back into the bag (if there's room).</summary>
        public bool Unequip(EquipSlot slot)
        {
            string itemId = GetEquipped(slot);
            if (itemId == null) return false;
            if (inventory != null && inventory.IsFull) return false;

            equipped.Remove(slot);
            stats?.ClearModifiers($"equip:{slot}");
            inventory?.Add(itemId);
            RefreshVisual(slot, null);

            OnEquipmentChanged?.Invoke();
            return true;
        }

        // ==================== VISUALS ====================

        private void RefreshVisual(EquipSlot slot, ItemDef def)
        {
            if (visuals.TryGetValue(slot, out var old) && old != null)
                Destroy(old);
            visuals.Remove(slot);

            if (def == null) return;

            var library = ItemVisualLibrary.Instance;
            var modelPrefab = library != null ? library.GetModel(def.Id) : null;

            GameObject visual = modelPrefab != null
                ? Instantiate(modelPrefab)
                : CreateFallbackShape(slot, def);

            AttachToSlot(visual, slot);
            visuals[slot] = visual;
        }

        private GameObject CreateFallbackShape(EquipSlot slot, ItemDef def)
        {
            PrimitiveType shape = slot switch
            {
                EquipSlot.Weapon => PrimitiveType.Cube,
                EquipSlot.Helm => PrimitiveType.Sphere,
                _ => PrimitiveType.Cube,
            };
            var obj = GameObject.CreatePrimitive(shape);
            Destroy(obj.GetComponent<Collider>());
            var renderer = obj.GetComponent<MeshRenderer>();
            renderer.material.color = def.Tint;
            return obj;
        }

        private void AttachToSlot(GameObject visual, EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: AttachWeapon(visual); break;
                case EquipSlot.Helm: AttachTo(visual, FindBone("head"), 0.34f, Vector3.up * 0.06f); break;
                case EquipSlot.Chest: AttachTo(visual, FindBone("chest", "spine2", "spine"), 0.52f, Vector3.zero); break;
                case EquipSlot.Legs: AttachTo(visual, FindBone("hips", "pelvis"), 0.5f, Vector3.down * 0.15f); break;
                case EquipSlot.Cape: AttachCape(visual); break;
                case EquipSlot.Gloves: AttachTo(visual, FindBone("hand_r", "lowerarm_r", "forearm_r", "hand"), 0.16f, Vector3.zero); break;
                case EquipSlot.Boots: AttachTo(visual, FindBone("foot_r", "calf_r", "foot", "toe_r"), 0.2f, Vector3.zero); break;
                case EquipSlot.Necklace: AttachTo(visual, FindBone("neck", "spine2", "chest"), 0.16f, Vector3.up * 0.08f); break;
                case EquipSlot.Ring1: AttachTo(visual, FindBone("hand_r", "hand"), 0.05f, Vector3.zero); break;
                case EquipSlot.Ring2: AttachTo(visual, FindBone("hand_l", "hand_r", "hand"), 0.05f, Vector3.zero); break;
                case EquipSlot.Focus: AttachFocus(visual); break;
            }
        }

        /// <summary>Off-hand focus: small orb held in the left hand, no blade alignment.</summary>
        private void AttachFocus(GameObject visual)
        {
            Transform hand = FindBone("hand_l", "lefthand", "hand.l", "hand");
            if (hand == null)
            {
                visual.transform.SetParent(transform, false);
                NormalizeSize(visual.transform, 0.4f);
                CenterOnParent(visual.transform, -transform.right * 0.45f + transform.up * 1.0f);
                return;
            }

            visual.transform.SetParent(hand, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.rotation = transform.rotation;
            NormalizeSize(visual.transform, 0.42f);
            CenterOnParent(visual.transform, GripAxisWorld(hand) * 0.08f);
        }

        /// <summary>
        /// Generic armor attach: bounds-normalize to a target size, keep the
        /// character's upright orientation (baked as a local offset from the
        /// bone's current pose), center on the bone.
        /// </summary>
        private void AttachTo(GameObject visual, Transform bone, float targetSize, Vector3 worldOffset)
        {
            if (bone == null) bone = transform;
            visual.transform.SetParent(bone, false);
            visual.transform.localPosition = Vector3.zero;
            // Upright in world space right now; follows the bone afterwards
            visual.transform.rotation = transform.rotation;

            NormalizeSize(visual.transform, targetSize);
            CenterOnParent(visual.transform, worldOffset);
        }

        private void AttachCape(GameObject visual)
        {
            var bone = FindBone("chest", "spine2", "spine", "neck");
            if (bone == null) bone = transform;
            visual.transform.SetParent(bone, false);
            visual.transform.rotation = transform.rotation;
            NormalizeSize(visual.transform, 1.0f);
            // Hang behind the back
            CenterOnParent(visual.transform, -transform.forward * 0.16f - transform.up * 0.35f);
        }

        private void AttachWeapon(GameObject visual)
        {
            Transform hand = FindBone("hand_r", "righthand", "hand.r", "hand");

            if (hand == null)
            {
                // No rig: hang at the hip
                visual.transform.SetParent(transform, false);
                NormalizeSize(visual.transform, 1.05f);
                CenterOnParent(visual.transform, transform.right * 0.45f + transform.up * 1.0f);
                return;
            }

            visual.transform.SetParent(hand, false);
            visual.transform.localPosition = Vector3.zero;

            // Grip axis: from the hand bone toward its deepest descendant
            // (the fingertips), the natural "out of the fist" direction.
            Vector3 gripAxis = GripAxisWorld(hand);

            // Blade = the model's longest dimension. Rotate so it lies along
            // the grip axis, tilted slightly forward for a held look.
            var bounds = WorldBounds(visual.transform);
            Vector3 bladeAxisLocal = LongestAxis(visual.transform, bounds);
            Vector3 desired = (gripAxis + transform.forward * 0.35f).normalized;
            visual.transform.rotation = Quaternion.FromToRotation(
                visual.transform.TransformDirection(bladeAxisLocal), desired) * visual.transform.rotation;

            NormalizeSize(visual.transform, 1.05f);

            // Grip near the pommel: slide the blade so ~25% sits behind the hand
            bounds = WorldBounds(visual.transform);
            float length = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Vector3 centerToHand = hand.position - bounds.center;
            visual.transform.position += centerToHand + desired * (length * 0.25f);
        }

        // ---------- helpers ----------

        private Vector3 GripAxisWorld(Transform hand)
        {
            Transform deepest = hand;
            int bestDepth = 0;
            void Walk(Transform t, int depth)
            {
                if (depth > bestDepth) { bestDepth = depth; deepest = t; }
                foreach (Transform child in t) Walk(child, depth + 1);
            }
            Walk(hand, 0);

            if (deepest != hand)
            {
                Vector3 axis = deepest.position - hand.position;
                if (axis.sqrMagnitude > 0.0001f) return axis.normalized;
            }
            return transform.forward;
        }

        private static Bounds WorldBounds(Transform t)
        {
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(t.position, Vector3.one * 0.1f);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private static Vector3 LongestAxis(Transform t, Bounds worldBounds)
        {
            // Which local axis spans the longest world dimension?
            Vector3 size = worldBounds.size;
            Vector3 worldAxis = size.x >= size.y && size.x >= size.z ? Vector3.right
                              : size.y >= size.z ? Vector3.up : Vector3.forward;
            return t.InverseTransformDirection(worldAxis).normalized;
        }

        private static void NormalizeSize(Transform t, float targetSize)
        {
            var bounds = WorldBounds(t);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
            t.localScale *= targetSize / longest;
        }

        private void CenterOnParent(Transform t, Vector3 worldOffset)
        {
            var bounds = WorldBounds(t);
            t.position += (t.parent.position - bounds.center) + worldOffset;
        }

        private Transform FindBone(params string[] keywords)
        {
            Transform best = null;
            int bestScore = 0;
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace(".", "");
                for (int i = 0; i < keywords.Length; i++)
                {
                    string key = keywords[i].Replace("_", "").Replace(".", "");
                    if (!n.Contains(key)) continue;
                    // Earlier keywords are better matches; shorter names are more canonical
                    int score = (keywords.Length - i) * 10 - Mathf.Min(n.Length, 9);
                    // Prefer right-side bones for the weapon search
                    if (n.Contains("right") || n.EndsWith("r")) score += 3;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = t;
                    }
                }
            }
            return best;
        }
    }
}
