using UnityEngine;

namespace AldenFall.World
{
    /// <summary>
    /// Procedural bow / arrow / quiver meshes so archery reads as archery without art assets.
    /// Everything is built from thin coloured cubes (no colliders, shadows off). Arrows are built
    /// along +Z (local forward) with the head leading, so a projectile flying along its direction
    /// shows point-first. A single shared material per colour keeps draw calls low.
    /// </summary>
    public static class ArcheryArt
    {
        private static readonly Color ShaftWood = new Color(0.42f, 0.28f, 0.16f);
        private static readonly Color Steel     = new Color(0.74f, 0.76f, 0.80f);
        private static readonly Color Feather   = new Color(0.86f, 0.86f, 0.90f);
        private static readonly Color BowWood   = new Color(0.34f, 0.21f, 0.11f);
        private static readonly Color Leather   = new Color(0.30f, 0.20f, 0.12f);

        private static Material _wood, _steel, _feather, _bow, _leather;
        private static Material Wood    => _wood    ? _wood    : (_wood    = Mat(ShaftWood));
        private static Material SteelM  => _steel   ? _steel   : (_steel   = Mat(Steel));
        private static Material FeatherM=> _feather ? _feather : (_feather = Mat(Feather));
        private static Material BowM    => _bow     ? _bow     : (_bow     = Mat(BowWood));
        private static Material LeatherM=> _leather ? _leather : (_leather = Mat(Leather));

        private static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Build an arrow (length along +Z, steel head at +Z, fletching at the tail) as
        /// children of <paramref name="parent"/>. Used for projectiles and dropped loot alike.</summary>
        public static void AttachArrow(Transform parent, float length = 1.1f)
        {
            float r = Mathf.Max(0.018f, length * 0.02f);

            // Shaft: a thin cube stretched along +Z.
            Box("Shaft", parent, Vector3.zero, new Vector3(r, r, length), Quaternion.identity, Wood);

            // Head: a slim steel point ahead of the shaft (tapered look via a narrow, longer box).
            Box("Head", parent, new Vector3(0f, 0f, length * 0.54f),
                new Vector3(r * 2.1f, r * 2.1f, length * 0.16f), Quaternion.Euler(0f, 45f, 0f), SteelM);

            // Fletching: three feathers spaced 120 degrees around the tail.
            for (int i = 0; i < 3; i++)
            {
                var rot = Quaternion.Euler(0f, 0f, i * 120f);
                Vector3 offset = rot * new Vector3(r * 2.4f, 0f, -length * 0.40f);
                Box($"Fletch{i}", parent, offset,
                    new Vector3(r * 0.4f, r * 6f, length * 0.14f), rot, FeatherM);
            }
        }

        /// <summary>Build a stylised recurve bow: a curved wooden stave (belly toward -Z) with a
        /// straight string on the +... front. Long axis is local Y (held upright in a hand).</summary>
        public static GameObject BuildBow(float height = 1.35f)
        {
            var root = new GameObject("Bow");
            const int seg = 7;
            float half = height * 0.5f;
            float bellyDepth = height * 0.22f;   // how far the stave bows away from the string

            float tipTopY = 0f, tipBotY = 0f;
            for (int i = 0; i < seg; i++)
            {
                float t = i / (float)(seg - 1) - 0.5f;      // -0.5 .. 0.5
                float y = t * height;
                float z = -(0.25f - t * t) * (bellyDepth * 4f); // parabola: mid pushes -Z, tips at 0
                float tangentDeg = t * 70f;                  // tilt each seg to follow the arc
                Box($"Limb{i}", root.transform, new Vector3(0f, y, z),
                    new Vector3(0.045f, height / seg * 1.15f, 0.05f),
                    Quaternion.Euler(tangentDeg, 0f, 0f), BowM);
                if (i == 0) tipBotY = y;
                if (i == seg - 1) tipTopY = y;
            }

            // Grip wrap at the belly centre.
            Box("Grip", root.transform, new Vector3(0f, 0f, -bellyDepth), new Vector3(0.07f, height * 0.18f, 0.07f),
                Quaternion.identity, LeatherM);

            // String: a taut line from tip to tip at z=0 (the tips sit at z=0).
            Box("String", root.transform, new Vector3(0f, (tipTopY + tipBotY) * 0.5f, 0f),
                new Vector3(0.012f, half * 2f, 0.012f), Quaternion.identity, SteelM);

            return root;
        }

        /// <summary>Build a back quiver: a leather tube with a few arrow tails poking out. Long axis
        /// is local Y. Used as the off-hand "arrows" visual whenever a bow is drawn.</summary>
        public static GameObject BuildQuiver()
        {
            var root = new GameObject("Quiver");
            Box("Tube", root.transform, Vector3.zero, new Vector3(0.16f, 0.5f, 0.16f), Quaternion.identity, LeatherM);
            for (int i = 0; i < 4; i++)
            {
                float ang = i * 90f + 20f;
                Vector3 off = Quaternion.Euler(0f, ang, 0f) * new Vector3(0.05f, 0f, 0f);
                Box($"Nock{i}", root.transform, off + new Vector3(0f, 0.34f, 0f),
                    new Vector3(0.02f, 0.24f, 0.02f), Quaternion.Euler(6f, ang, 0f), Wood);
                Box($"Feather{i}", root.transform, off + new Vector3(0f, 0.44f, 0f),
                    new Vector3(0.03f, 0.1f, 0.01f), Quaternion.Euler(6f, ang, 0f), FeatherM);
            }
            return root;
        }
    }
}
