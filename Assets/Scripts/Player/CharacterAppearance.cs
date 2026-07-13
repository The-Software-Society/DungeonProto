using UnityEngine;
using AldenFall.Items;

namespace AldenFall.Player
{
    /// <summary>
    /// The character-customization baseline: gender (which naked base body is
    /// active), skin tone, hair style + color. The base bodies (Visual_Male /
    /// Visual_Female, Meshy-generated) are both built into the player at scene
    /// build; this activates the chosen one BEFORE other components cache
    /// their animator (DefaultExecutionOrder). Armor/weapons attach onto the
    /// active skeleton via Equipment exactly as before, gear goes ON the
    /// baseline, never baked into it.
    ///
    /// Gender applies on next play session (equipment/animator rebind);
    /// skin, hair style, and hair color apply live.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CharacterAppearance : MonoBehaviour
    {
        private const string GenderKey = "appearance_gender";
        private const string SkinKey = "appearance_skin";
        private const string HairStyleKey = "appearance_hair_style";
        private const string HairColorKey = "appearance_hair_color";
        private const bool UseGeneratedHairFallback = true;

        public static readonly Color[] SkinTones =
        {
            new Color(1f, 0.87f, 0.74f),     // fair
            new Color(0.94f, 0.76f, 0.58f),  // tan
            new Color(0.72f, 0.51f, 0.35f),  // brown
            new Color(0.45f, 0.31f, 0.22f),  // deep
        };

        public static readonly Color[] HairColors =
        {
            new Color(0.22f, 0.15f, 0.09f),  // dark brown
            new Color(0.85f, 0.68f, 0.3f),   // blonde
            new Color(0.55f, 0.2f, 0.12f),   // auburn
            new Color(0.16f, 0.16f, 0.18f),  // black
        };

        public static readonly string[] HairStyleIds = { null, "hair_short", "hair_long", "hair_ponytail" };
        public static readonly string[] HairStyleNames = { "None", "Short", "Long", "Ponytail" };

        public int Gender { get; private set; }        // 0 = male, 1 = female
        public int SkinTone { get; private set; }
        public int HairStyle { get; private set; }
        public int HairColor { get; private set; }

        private GameObject activeBody;
        private GameObject hairInstance;

        private void Awake()
        {
            Gender = PlayerPrefs.GetInt(GenderKey, 0);
            SkinTone = PlayerPrefs.GetInt(SkinKey, 0);
            HairStyle = PlayerPrefs.GetInt(HairStyleKey, 1);
            HairColor = PlayerPrefs.GetInt(HairColorKey, 0);

            ActivateBody();
        }

        private void Start()
        {
            // Library + bones exist by now
            ApplySkin();
            ApplyHair();
        }

        // ==================== SELECTION (UI calls these) ====================

        /// <summary>Saved immediately; the body swap applies next session.</summary>
        public void SetGender(int gender)
        {
            Gender = Mathf.Clamp(gender, 0, 1);
            PlayerPrefs.SetInt(GenderKey, Gender);
        }

        public void SetSkinTone(int index)
        {
            SkinTone = Mathf.Clamp(index, 0, SkinTones.Length - 1);
            PlayerPrefs.SetInt(SkinKey, SkinTone);
            ApplySkin();
        }

        public void SetHairStyle(int index)
        {
            HairStyle = Mathf.Clamp(index, 0, HairStyleIds.Length - 1);
            PlayerPrefs.SetInt(HairStyleKey, HairStyle);
            ApplyHair();
        }

        public void SetHairColor(int index)
        {
            HairColor = Mathf.Clamp(index, 0, HairColors.Length - 1);
            PlayerPrefs.SetInt(HairColorKey, HairColor);
            ApplyHair();
        }

        /// <summary>Runtime network profile application; does not touch PlayerPrefs.</summary>
        public void ApplyNetworkSelection(int gender, int skinTone, int hairStyle, int hairColor)
        {
            Gender = Mathf.Clamp(gender, 0, 1);
            SkinTone = Mathf.Clamp(skinTone, 0, SkinTones.Length - 1);
            HairStyle = Mathf.Clamp(hairStyle, 0, HairStyleIds.Length - 1);
            HairColor = Mathf.Clamp(hairColor, 0, HairColors.Length - 1);

            ActivateBody();
            ApplySkin();
            ApplyHair();
        }

        // ==================== APPLICATION ====================

        private void ActivateBody()
        {
            var male = FindDeep(transform, "Visual_Male");
            var female = FindDeep(transform, "Visual_Female");

            // Both base bodies present: enable the chosen one only. If the
            // Meshy bases haven't been generated yet there is just the legacy
            // "Hero" visual, nothing to switch, everything still works.
            if (male != null && female != null)
            {
                bool isFemale = Gender == 1;
                male.gameObject.SetActive(!isFemale);
                female.gameObject.SetActive(isFemale);
                activeBody = (isFemale ? female : male).gameObject;
            }
            else
            {
                activeBody = (male ?? female)?.gameObject;
            }
        }

        private void ApplySkin()
        {
            var body = ActiveBodyRoot();
            if (body == null) return;

            // Tint via property block, never instantiates materials
            var block = new MaterialPropertyBlock();
            foreach (var renderer in body.GetComponentsInChildren<Renderer>())
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", SkinTones[SkinTone]);
                block.SetColor("_Color", SkinTones[SkinTone]);
                renderer.SetPropertyBlock(block);
            }
        }

        private void ApplyHair()
        {
            if (hairInstance != null)
                Destroy(hairInstance);

            string hairId = HairStyleIds[HairStyle];
            if (string.IsNullOrEmpty(hairId)) return;

            var prefab = ItemVisualLibrary.Instance?.GetModel(hairId);
            var head = FindBone("head");
            var parent = head != null ? head : ActiveBodyRoot()?.transform;
            if (parent == null) return;

            if (prefab != null && !UseGeneratedHairFallback)
            {
                hairInstance = Instantiate(prefab, parent, false);
                hairInstance.name = "Hair";

                // Bounds-normalize to head scale (Meshy props arrive at arbitrary sizes)
                var prefabRenderers = hairInstance.GetComponentsInChildren<Renderer>();
                if (prefabRenderers.Length > 0)
                {
                    var bounds = prefabRenderers[0].bounds;
                    foreach (var r in prefabRenderers) bounds.Encapsulate(r.bounds);
                    float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, 0.001f);
                    hairInstance.transform.localScale = Vector3.one * (0.3f / longest / Mathf.Max(parent.lossyScale.x, 0.001f));
                    if (head != null)
                        hairInstance.transform.localPosition = Vector3.up * 0.08f;
                    else if (TryGetBodyBounds(out var bodyBounds))
                        hairInstance.transform.position = HairAnchorFromBounds(bodyBounds);
                }
            }
            else
            {
                hairInstance = CreateFallbackHair();
            }

            var renderers = hairInstance.GetComponentsInChildren<Renderer>();
            var block = new MaterialPropertyBlock();
            foreach (var renderer in renderers)
            {
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", HairColors[HairColor]);
                block.SetColor("_Color", HairColors[HairColor]);
                renderer.SetPropertyBlock(block);
                if (renderer.material != null)
                {
                    renderer.material.color = HairColors[HairColor];
                    if (renderer.material.HasProperty("_BaseColor"))
                        renderer.material.SetColor("_BaseColor", HairColors[HairColor]);
                    if (renderer.material.HasProperty("_Color"))
                        renderer.material.SetColor("_Color", HairColors[HairColor]);
                }
            }
        }

        private GameObject CreateFallbackHair()
        {
            var root = new GameObject("Hair");
            root.transform.SetParent(transform, true);
            root.transform.rotation = transform.rotation;

            if (TryGetBodyBounds(out var bodyBounds))
            {
                root.transform.position = HairAnchorFromBounds(bodyBounds);
                root.transform.localScale = Vector3.one * Mathf.Clamp(bodyBounds.size.y / 3.8f, 0.42f, 0.65f);
            }
            else
            {
                root.transform.localPosition = new Vector3(0f, 1.65f, 0.05f);
                root.transform.localScale = Vector3.one * 0.5f;
            }

            switch (HairStyle)
            {
                case 2:
                    CreateHairPiece(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.24f, 0.07f, 0.18f), Quaternion.identity);
                    CreateHairPiece(root.transform, PrimitiveType.Sphere, new Vector3(0f, -0.07f, -0.06f), new Vector3(0.18f, 0.16f, 0.08f), Quaternion.identity);
                    break;
                case 3:
                    CreateHairPiece(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.22f, 0.07f, 0.17f), Quaternion.identity);
                    CreateHairPiece(root.transform, PrimitiveType.Capsule, new Vector3(0f, -0.06f, -0.18f), new Vector3(0.035f, 0.13f, 0.035f), Quaternion.Euler(90f, 0f, 0f));
                    break;
                default:
                    CreateHairPiece(root.transform, PrimitiveType.Sphere, new Vector3(0f, 0.02f, 0.02f), new Vector3(0.22f, 0.06f, 0.16f), Quaternion.identity);
                    break;
            }

            return root;
        }

        private bool TryGetBodyBounds(out Bounds bounds)
        {
            bounds = default;
            var body = ActiveBodyRoot();
            if (body == null) return false;

            var renderers = body.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;

            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return true;
        }

        private static Vector3 HairAnchorFromBounds(Bounds bounds)
        {
            return new Vector3(
                bounds.center.x,
                bounds.max.y - bounds.size.y * 0.1f,
                bounds.center.z + bounds.size.z * 0.01f);
        }

        private static void CreateHairPiece(Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Quaternion localRotation)
        {
            var piece = GameObject.CreatePrimitive(type);
            piece.name = type == PrimitiveType.Capsule ? "HairTail" : "HairCap";
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = localRotation;
            piece.transform.localScale = localScale;

            var collider = piece.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = piece.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = new Material(Shader.Find("Sprites/Default"));
        }

        // ==================== HELPERS ====================

        private GameObject ActiveBodyRoot()
        {
            if (activeBody != null) return activeBody;
            var visual = transform.Find("Visual");
            return visual != null ? visual.gameObject : null;
        }

        private Transform FindBone(string keyword)
        {
            var root = ActiveBodyRoot();
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.ToLowerInvariant().Contains(keyword))
                    return t;
            return null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name)
                    return t;
            return null;
        }
    }
}
