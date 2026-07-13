#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

namespace AldenFall.Editor
{
    /// <summary>
    /// Generates the UI sprite set procedurally: rounded 9-slice panels, borders,
    /// circles, glows, and skill/ability glyph icons, SDF-style per-pixel drawing
    /// with anti-aliased edges. Everything the HUD uses comes from here, so the
    /// whole visual language is regenerable and consistent.
    /// </summary>
    public static class UISpriteGenerator
    {
        private const string Dir = "Assets/UI/Sprites";
        // Runtime-built panels (vendor, forge, quest log, dev tester) load the
        // core chrome set through Resources so they match the baked HUD.
        private const string ResourcesDir = "Assets/UI/Resources/UISprites";
        private static readonly string[] RuntimeShared =
            { "panel_r16", "panel_border_r16", "slot_r10", "slot_border_r10", "bar_r6" };

        public static void GenerateAll()
        {
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(ResourcesDir);

            // Panels & structure
            Save("panel_r16", RoundedRect(64, 64, 16, filled: true), border: 24);
            Save("panel_border_r16", RoundedRect(64, 64, 16, filled: false, strokeWidth: 2f), border: 24);
            Save("slot_r10", RoundedRect(48, 48, 10, filled: true), border: 16);
            Save("slot_border_r10", RoundedRect(48, 48, 10, filled: false, strokeWidth: 2f), border: 16);
            Save("bar_r6", RoundedRect(48, 48, 12, filled: true), border: 16);
            Save("bar_border_r6", RoundedRect(48, 48, 12, filled: false, strokeWidth: 2f), border: 16);
            Save("circle", Circle(64, filled: true));
            Save("circle_border", Circle(64, filled: false, strokeWidth: 2.5f));
            Save("glow_soft", RadialGlow(96));
            Save("shadow_soft", RadialGlow(96, exponent: 2.6f));

            // Ability + skill glyphs (white on transparent, tinted at runtime)
            Save("icon_sword", IconSword(64));
            Save("icon_fireball", IconFireball(64));
            Save("icon_nova", IconNova(64));
            Save("icon_strength", IconChevrons(64));
            Save("icon_shield", IconShield(64));
            Save("icon_magic", IconStar(64));
            Save("icon_armor", IconShield(64)); // armor items reuse the shield glyph
            Save("icon_bag", IconBag(64));

            // v4 glyphs: skills + abilities + food
            Save("icon_fish", IconFish(64));
            Save("icon_flame", IconFlame(64));
            Save("icon_dash", IconDash(64));
            Save("icon_cleave", IconCleave(64));
            Save("icon_bulwark", IconBulwark(64));
            Save("icon_food", IconFood(64));
            Save("icon_bolt", IconBolt(64));

            // v5 glyphs: 3-ability combat trees
            Save("icon_flurry", IconFlurry(64));
            Save("icon_slow", IconSlow(64));
            Save("icon_whirl", IconWhirl(64));
            Save("icon_slam", IconSlam(64));
            Save("icon_taunt", IconTaunt(64));
            Save("icon_bash", IconBash(64));
            Save("icon_veil", IconVeil(64));

            // v9 glyphs: distinct skill icons (Jon: icons were reused/mismatched)
            Save("icon_heart", IconHeart(64));      // Hitpoints
            Save("icon_bow", IconBow(64));          // Ranged / Archery
            Save("icon_pickaxe", IconPickaxe(64));  // Mining
            Save("icon_axe", IconAxe(64));          // Woodcutting
            Save("icon_anvil", IconAnvil(64));      // Smithing
            Save("icon_arrow", IconArrow(64));      // Fletching + arrow ammo

            AssetDatabase.Refresh();
            Debug.Log("UI sprites generated → " + Dir);
        }

        // ==================== SHAPES ====================

        private static Color32[] RoundedRect(int w, int h, float radius, bool filled,
            float strokeWidth = 2f)
        {
            return Draw(w, h, (x, y) =>
            {
                float d = RoundedRectSDF(x, y, w, h, radius);
                return filled
                    ? AA(-d)                       // inside is negative
                    : AA(strokeWidth / 2f - Mathf.Abs(d));
            });
        }

        private static Color32[] Circle(int size, bool filled, float strokeWidth = 2f)
        {
            float r = size / 2f - 2f;
            return Draw(size, size, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) - r;
                return filled ? AA(-d) : AA(strokeWidth / 2f - Mathf.Abs(d));
            });
        }

        private static Color32[] RadialGlow(int size, float exponent = 1.8f)
        {
            return Draw(size, size, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                return Mathf.Pow(Mathf.Clamp01(1f - d), exponent);
            });
        }

        // ==================== GLYPHS ====================

        private static Color32[] IconSword(int s)
        {
            // Diagonal blade + crossguard + pommel
            Vector2 tip = new Vector2(s * 0.78f, s * 0.80f);
            Vector2 guardMid = new Vector2(s * 0.34f, s * 0.36f);
            Vector2 gripEnd = new Vector2(s * 0.20f, s * 0.22f);
            Vector2 guardA = new Vector2(s * 0.22f, s * 0.48f);
            Vector2 guardB = new Vector2(s * 0.46f, s * 0.24f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float blade = Capsule(p, guardMid, tip, s * 0.055f);
                float guard = Capsule(p, guardA, guardB, s * 0.035f);
                float grip = Capsule(p, gripEnd, guardMid, s * 0.03f);
                float pommel = Vector2.Distance(p, gripEnd) - s * 0.05f;
                return AA(-Mathf.Min(blade, guard, grip, pommel));
            });
        }

        private static Color32[] IconFireball(int s)
        {
            // Teardrop flame: circle base narrowing into a single swept tip
            Vector2 c = new Vector2(s * 0.5f, s * 0.38f);
            Vector2 tip = new Vector2(s * 0.58f, s * 0.82f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float orb = Vector2.Distance(p, c) - s * 0.21f;
                // Tapered tongue: chain of shrinking circles toward the tip
                float tongue = float.MaxValue;
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f;
                    Vector2 pos = Vector2.Lerp(c + Vector2.up * s * 0.05f, tip, t * t);
                    float radius = Mathf.Lerp(s * 0.17f, s * 0.02f, t);
                    tongue = Mathf.Min(tongue, Vector2.Distance(p, pos) - radius);
                }
                float body = Mathf.Min(orb, tongue);
                // Inner core cutout for depth
                float core = Vector2.Distance(p, c) - s * 0.085f;
                return Mathf.Clamp01(AA(-body) - AA(-core) * 0.5f);
            });
        }

        private static Color32[] IconNova(int s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float ring = Mathf.Abs(Vector2.Distance(p, c) - s * 0.26f) - s * 0.045f;
                float core = Vector2.Distance(p, c) - s * 0.08f;
                // 4 burst spokes
                float spokes = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float angle = i * Mathf.PI / 2f + Mathf.PI / 4f;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    spokes = Mathf.Min(spokes,
                        Capsule(p, c + dir * s * 0.32f, c + dir * s * 0.44f, s * 0.03f));
                }
                return AA(-Mathf.Min(ring, core, spokes));
            });
        }

        private static Color32[] IconChevrons(int s)
        {
            // Three ascending chevrons (strength)
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float best = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    float baseY = s * (0.24f + i * 0.2f);
                    Vector2 left = new Vector2(s * 0.28f, baseY);
                    Vector2 mid = new Vector2(s * 0.5f, baseY + s * 0.14f);
                    Vector2 right = new Vector2(s * 0.72f, baseY);
                    best = Mathf.Min(best, Capsule(p, left, mid, s * 0.045f));
                    best = Mathf.Min(best, Capsule(p, mid, right, s * 0.045f));
                }
                return AA(-best);
            });
        }

        private static Color32[] IconShield(int s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.56f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                // Shield: box top + circle bottom, subtract inner copy for outline feel
                float halfW = s * 0.24f;
                float boxD = Mathf.Max(Mathf.Abs(p.x - c.x) - halfW, Mathf.Abs(p.y - (c.y + s * 0.06f)) - s * 0.16f);
                float tipD = Vector2.Distance(p, new Vector2(c.x, c.y - s * 0.14f)) - halfW;
                float outer = Mathf.Min(boxD, tipD);
                // Emboss line down the middle
                float line = Capsule(p, new Vector2(c.x, c.y - s * 0.28f), new Vector2(c.x, c.y + s * 0.2f), s * 0.018f);
                return Mathf.Clamp01(AA(-outer) - AA(-line) * 0.5f);
            });
        }

        private static Color32[] IconStar(int s)
        {
            // 4-point sparkle (magic)
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float v = Capsule(p, c + Vector2.up * s * 0.34f, c - Vector2.up * s * 0.34f, s * 0.045f);
                float hz = Capsule(p, c + Vector2.right * s * 0.34f, c - Vector2.right * s * 0.34f, s * 0.045f);
                float dot1 = Vector2.Distance(p, c + new Vector2(s * 0.2f, s * 0.2f)) - s * 0.035f;
                float dot2 = Vector2.Distance(p, c - new Vector2(s * 0.2f, s * 0.2f)) - s * 0.035f;
                float core = Vector2.Distance(p, c) - s * 0.09f;
                return AA(-Mathf.Min(v, hz, dot1, dot2, core));
            });
        }

        private static Color32[] IconBag(int s)
        {
            Vector2 c = new Vector2(s * 0.5f, s * 0.42f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float body = RoundedRectSDF(p.x - (c.x - s * 0.24f), p.y - (c.y - s * 0.2f), s * 0.48f, s * 0.4f, s * 0.1f);
                float handle = Mathf.Abs(Vector2.Distance(p, new Vector2(c.x, c.y + s * 0.18f)) - s * 0.13f) - s * 0.032f;
                float handleClip = p.y > c.y + s * 0.18f ? handle : float.MaxValue;
                return AA(-Mathf.Min(body, handleClip));
            });
        }

        private static Color32[] IconFish(int s)
        {
            // Body pointing right, forked tail on the left, small eye cutout
            Vector2 nose = new Vector2(s * 0.78f, s * 0.5f);
            Vector2 tailBase = new Vector2(s * 0.34f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                // Body: chain of shrinking circles from mid to nose (teardrop)
                float body = float.MaxValue;
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f;
                    Vector2 pos = Vector2.Lerp(tailBase + Vector2.right * s * 0.08f, nose, t);
                    float radius = Mathf.Lerp(s * 0.17f, s * 0.03f, t * t);
                    body = Mathf.Min(body, Vector2.Distance(p, pos) - radius);
                }
                float tailTop = Capsule(p, tailBase, new Vector2(s * 0.18f, s * 0.66f), s * 0.045f);
                float tailBot = Capsule(p, tailBase, new Vector2(s * 0.18f, s * 0.34f), s * 0.045f);
                float eye = Vector2.Distance(p, new Vector2(s * 0.62f, s * 0.54f)) - s * 0.035f;
                return Mathf.Clamp01(AA(-Mathf.Min(body, tailTop, tailBot)) - AA(-eye));
            });
        }

        private static Color32[] IconFlame(int s)
        {
            // Upright candle-flame teardrop with an inner core cutout
            Vector2 baseC = new Vector2(s * 0.5f, s * 0.34f);
            Vector2 tip = new Vector2(s * 0.56f, s * 0.82f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float orb = Vector2.Distance(p, baseC) - s * 0.2f;
                float tongue = float.MaxValue;
                for (int i = 0; i <= 6; i++)
                {
                    float t = i / 6f;
                    Vector2 pos = Vector2.Lerp(baseC + Vector2.up * s * 0.04f, tip, t * t);
                    float radius = Mathf.Lerp(s * 0.16f, s * 0.02f, t);
                    tongue = Mathf.Min(tongue, Vector2.Distance(p, pos) - radius);
                }
                float core = Vector2.Distance(p, baseC + Vector2.up * s * 0.02f) - s * 0.07f;
                return Mathf.Clamp01(AA(-Mathf.Min(orb, tongue)) - AA(-core) * 0.6f);
            });
        }

        private static Color32[] IconDash(int s)
        {
            // Arrow lunging right with two speed lines trailing
            Vector2 tip = new Vector2(s * 0.8f, s * 0.5f);
            Vector2 back = new Vector2(s * 0.4f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float shaft = Capsule(p, back, tip, s * 0.05f);
                float headUp = Capsule(p, tip, tip + new Vector2(-s * 0.16f, s * 0.14f), s * 0.045f);
                float headDn = Capsule(p, tip, tip + new Vector2(-s * 0.16f, -s * 0.14f), s * 0.045f);
                float line1 = Capsule(p, new Vector2(s * 0.16f, s * 0.62f), new Vector2(s * 0.3f, s * 0.62f), s * 0.03f);
                float line2 = Capsule(p, new Vector2(s * 0.12f, s * 0.38f), new Vector2(s * 0.28f, s * 0.38f), s * 0.03f);
                return AA(-Mathf.Min(shaft, headUp, headDn, Mathf.Min(line1, line2)));
            });
        }

        private static Color32[] IconCleave(int s)
        {
            // Heavy diagonal slash with two motion arcs
            Vector2 a = new Vector2(s * 0.22f, s * 0.78f);
            Vector2 b = new Vector2(s * 0.8f, s * 0.24f);
            Vector2 c = new Vector2(s * 0.42f, s * 0.42f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float slash = Capsule(p, a, b, s * 0.06f);
                float arc1 = Mathf.Abs(Vector2.Distance(p, c) - s * 0.34f) - s * 0.022f;
                if (p.x > c.x + s * 0.2f || p.y > c.y + s * 0.34f) arc1 = float.MaxValue;
                float arc2 = Mathf.Abs(Vector2.Distance(p, c) - s * 0.46f) - s * 0.02f;
                if (p.x > c.x + s * 0.24f || p.y > c.y + s * 0.42f) arc2 = float.MaxValue;
                return AA(-Mathf.Min(slash, Mathf.Min(arc1, arc2)));
            });
        }

        private static Color32[] IconBulwark(int s)
        {
            // Shield outline with a bold upward chevron inside
            Vector2 c = new Vector2(s * 0.5f, s * 0.56f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float halfW = s * 0.24f;
                float boxD = Mathf.Max(Mathf.Abs(p.x - c.x) - halfW, Mathf.Abs(p.y - (c.y + s * 0.06f)) - s * 0.16f);
                float tipD = Vector2.Distance(p, new Vector2(c.x, c.y - s * 0.14f)) - halfW;
                float outer = Mathf.Min(boxD, tipD);
                float outline = Mathf.Abs(outer) - s * 0.028f;
                Vector2 mid = new Vector2(c.x, c.y + s * 0.08f);
                float chevL = Capsule(p, new Vector2(c.x - s * 0.13f, c.y - s * 0.08f), mid, s * 0.04f);
                float chevR = Capsule(p, new Vector2(c.x + s * 0.13f, c.y - s * 0.08f), mid, s * 0.04f);
                return AA(-Mathf.Min(outline, Mathf.Min(chevL, chevR)));
            });
        }

        private static Color32[] IconFood(int s)
        {
            // Drumstick: meaty end upper-right, bone toward lower-left
            Vector2 meat = new Vector2(s * 0.58f, s * 0.6f);
            Vector2 boneEnd = new Vector2(s * 0.24f, s * 0.26f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float body = Vector2.Distance(p, meat) - s * 0.21f;
                float bone = Capsule(p, meat, boneEnd, s * 0.045f);
                float knob1 = Vector2.Distance(p, boneEnd + new Vector2(-s * 0.045f, s * 0.045f)) - s * 0.05f;
                float knob2 = Vector2.Distance(p, boneEnd + new Vector2(s * 0.045f, -s * 0.045f)) - s * 0.05f;
                float bite = Vector2.Distance(p, meat + new Vector2(s * 0.16f, s * 0.14f)) - s * 0.07f;
                return Mathf.Clamp01(AA(-Mathf.Min(body, bone, Mathf.Min(knob1, knob2))) - AA(-bite) * 0.7f);
            });
        }

        private static Color32[] IconBolt(int s)
        {
            // Zigzag lightning bolt
            Vector2 p1 = new Vector2(s * 0.6f, s * 0.84f);
            Vector2 p2 = new Vector2(s * 0.42f, s * 0.52f);
            Vector2 p3 = new Vector2(s * 0.56f, s * 0.48f);
            Vector2 p4 = new Vector2(s * 0.38f, s * 0.16f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float a = Capsule(p, p1, p2, s * 0.05f);
                float b = Capsule(p, p2, p3, s * 0.05f);
                float c = Capsule(p, p3, p4, s * 0.05f);
                return AA(-Mathf.Min(a, Mathf.Min(b, c)));
            });
        }

        private static Color32[] IconFlurry(int s)
        {
            // Three parallel diagonal slashes (quick stabs)
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float best = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    float off = (i - 1) * s * 0.2f;
                    Vector2 a = new Vector2(s * 0.28f + off, s * 0.24f);
                    Vector2 b = new Vector2(s * 0.52f + off, s * 0.76f);
                    best = Mathf.Min(best, Capsule(p, a, b, s * 0.042f));
                }
                return AA(-best);
            });
        }

        private static Color32[] IconSlow(int s)
        {
            // Two descending chevrons over a ground line (crippled/slowed)
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float best = float.MaxValue;
                for (int i = 0; i < 2; i++)
                {
                    float baseY = s * (0.72f - i * 0.22f);
                    Vector2 left = new Vector2(s * 0.3f, baseY);
                    Vector2 mid = new Vector2(s * 0.5f, baseY - s * 0.14f);
                    Vector2 right = new Vector2(s * 0.7f, baseY);
                    best = Mathf.Min(best, Capsule(p, left, mid, s * 0.045f));
                    best = Mathf.Min(best, Capsule(p, mid, right, s * 0.045f));
                }
                float ground = Capsule(p, new Vector2(s * 0.26f, s * 0.18f), new Vector2(s * 0.74f, s * 0.18f), s * 0.03f);
                return AA(-Mathf.Min(best, ground));
            });
        }

        private static Color32[] IconWhirl(int s)
        {
            // Cyclone: broken ring with flick tails + center dot (spin)
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                Vector2 d = p - c;
                float angle = Mathf.Atan2(d.y, d.x);
                float ringOuter = Mathf.Abs(d.magnitude - s * 0.3f) - s * 0.045f;
                // Gap the ring in two opposing quadrants so it reads as motion
                bool gap = (angle > 0.3f && angle < 1.3f) || (angle < -1.85f && angle > -2.85f);
                float ring = gap ? float.MaxValue : ringOuter;
                float core = d.magnitude - s * 0.09f;
                // Flick tails at the gap ends
                float tail1 = Capsule(p, c + new Vector2(s * 0.28f, s * 0.1f), c + new Vector2(s * 0.42f, s * 0.2f), s * 0.032f);
                float tail2 = Capsule(p, c + new Vector2(-s * 0.28f, -s * 0.1f), c + new Vector2(-s * 0.42f, -s * 0.2f), s * 0.032f);
                return AA(-Mathf.Min(Mathf.Min(ring, core), Mathf.Min(tail1, tail2)));
            });
        }

        private static Color32[] IconSlam(int s)
        {
            // Bold arrow slamming down into a cracked ground line
            Vector2 top = new Vector2(s * 0.5f, s * 0.82f);
            Vector2 tip = new Vector2(s * 0.5f, s * 0.36f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float shaft = Capsule(p, top, tip, s * 0.055f);
                float headL = Capsule(p, tip, tip + new Vector2(-s * 0.15f, s * 0.15f), s * 0.05f);
                float headR = Capsule(p, tip, tip + new Vector2(s * 0.15f, s * 0.15f), s * 0.05f);
                float ground = Capsule(p, new Vector2(s * 0.2f, s * 0.2f), new Vector2(s * 0.8f, s * 0.2f), s * 0.035f);
                float crackL = Capsule(p, new Vector2(s * 0.32f, s * 0.2f), new Vector2(s * 0.24f, s * 0.1f), s * 0.025f);
                float crackR = Capsule(p, new Vector2(s * 0.68f, s * 0.2f), new Vector2(s * 0.76f, s * 0.1f), s * 0.025f);
                return AA(-Mathf.Min(Mathf.Min(shaft, Mathf.Min(headL, headR)),
                                     Mathf.Min(ground, Mathf.Min(crackL, crackR))));
            });
        }

        private static Color32[] IconTaunt(int s)
        {
            // Bold exclamation mark with radiating shout lines
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float bar = Capsule(p, new Vector2(c.x, s * 0.74f), new Vector2(c.x, s * 0.4f), s * 0.06f);
                float dot = Vector2.Distance(p, new Vector2(c.x, s * 0.22f)) - s * 0.06f;
                float lines = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float ang = Mathf.PI * (0.32f + i * 0.12f);
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    // mirror left/right pairs
                    Vector2 d2 = new Vector2(i < 2 ? dir.x : -dir.x, dir.y);
                    lines = Mathf.Min(lines, Capsule(p, c + d2 * s * 0.3f, c + d2 * s * 0.42f, s * 0.028f));
                }
                return AA(-Mathf.Min(Mathf.Min(bar, dot), lines));
            });
        }

        private static Color32[] IconBash(int s)
        {
            // Impact burst: heavy core with radiating spokes
            Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float core = Vector2.Distance(p, c) - s * 0.13f;
                float spokes = float.MaxValue;
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * Mathf.PI / 3f + Mathf.PI / 6f;
                    Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    float len = (i % 2 == 0) ? s * 0.4f : s * 0.3f;
                    spokes = Mathf.Min(spokes, Capsule(p, c + dir * s * 0.19f, c + dir * len, s * 0.035f));
                }
                return AA(-Mathf.Min(core, spokes));
            });
        }

        private static Color32[] IconVeil(int s)
        {
            // Crescent moon (fade from sight) with two drift sparkles
            Vector2 c = new Vector2(s * 0.46f, s * 0.52f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float moon = Vector2.Distance(p, c) - s * 0.26f;
                float bite = Vector2.Distance(p, c + new Vector2(s * 0.14f, s * 0.08f)) - s * 0.22f;
                float crescent = Mathf.Max(moon, -bite);
                float spark1 = Vector2.Distance(p, new Vector2(s * 0.76f, s * 0.7f)) - s * 0.04f;
                float spark2 = Vector2.Distance(p, new Vector2(s * 0.7f, s * 0.3f)) - s * 0.03f;
                return AA(-Mathf.Min(crescent, Mathf.Min(spark1, spark2)));
            });
        }

        // ==================== SDF HELPERS ====================

        private static Color32[] IconHeart(int s)
        {
            Vector2 lL = new Vector2(s * 0.35f, s * 0.64f), lR = new Vector2(s * 0.65f, s * 0.64f);
            Vector2 tip = new Vector2(s * 0.5f, s * 0.20f);
            float r = s * 0.15f;
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float lobes = Mathf.Min(Vector2.Distance(p, lL) - r, Vector2.Distance(p, lR) - r);
                float vL = Capsule(p, new Vector2(s * 0.22f, s * 0.60f), tip, s * 0.10f);
                float vR = Capsule(p, new Vector2(s * 0.78f, s * 0.60f), tip, s * 0.10f);
                return AA(-Mathf.Min(lobes, Mathf.Min(vL, vR)));
            });
        }

        private static Color32[] IconBow(int s)
        {
            Vector2 top = new Vector2(s * 0.32f, s * 0.84f), mid = new Vector2(s * 0.16f, s * 0.5f), bot = new Vector2(s * 0.32f, s * 0.16f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float bowU = Capsule(p, top, mid, s * 0.045f);
                float bowL = Capsule(p, mid, bot, s * 0.045f);
                float str = Capsule(p, top, bot, s * 0.02f);
                float shaft = Capsule(p, new Vector2(s * 0.24f, s * 0.5f), new Vector2(s * 0.86f, s * 0.5f), s * 0.028f);
                float hd1 = Capsule(p, new Vector2(s * 0.74f, s * 0.60f), new Vector2(s * 0.88f, s * 0.5f), s * 0.03f);
                float hd2 = Capsule(p, new Vector2(s * 0.74f, s * 0.40f), new Vector2(s * 0.88f, s * 0.5f), s * 0.03f);
                return AA(-Mathf.Min(Mathf.Min(bowU, bowL), Mathf.Min(Mathf.Min(str, shaft), Mathf.Min(hd1, hd2))));
            });
        }

        private static Color32[] IconPickaxe(int s)
        {
            Vector2 hTop = new Vector2(s * 0.5f, s * 0.80f), hBot = new Vector2(s * 0.5f, s * 0.16f);
            Vector2 headL = new Vector2(s * 0.18f, s * 0.60f), headR = new Vector2(s * 0.82f, s * 0.60f), headC = new Vector2(s * 0.5f, s * 0.72f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float handle = Capsule(p, hTop, hBot, s * 0.04f);
                float pickL = Capsule(p, headC, headL, s * 0.033f);
                float pickR = Capsule(p, headC, headR, s * 0.033f);
                return AA(-Mathf.Min(handle, Mathf.Min(pickL, pickR)));
            });
        }

        private static Color32[] IconAxe(int s)
        {
            Vector2 hTop = new Vector2(s * 0.60f, s * 0.84f), hBot = new Vector2(s * 0.40f, s * 0.14f);
            Vector2 bladeC = new Vector2(s * 0.66f, s * 0.70f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float handle = Capsule(p, hTop, hBot, s * 0.038f);
                float blade = Vector2.Distance(p, bladeC) - s * 0.19f;
                return AA(-Mathf.Min(handle, blade));
            });
        }

        private static Color32[] IconAnvil(int s)
        {
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float top = Capsule(p, new Vector2(s * 0.22f, s * 0.64f), new Vector2(s * 0.82f, s * 0.64f), s * 0.085f);
                float horn = Capsule(p, new Vector2(s * 0.22f, s * 0.64f), new Vector2(s * 0.08f, s * 0.56f), s * 0.04f);
                float waist = Capsule(p, new Vector2(s * 0.5f, s * 0.62f), new Vector2(s * 0.5f, s * 0.34f), s * 0.06f);
                float baseCap = Capsule(p, new Vector2(s * 0.26f, s * 0.28f), new Vector2(s * 0.74f, s * 0.28f), s * 0.075f);
                return AA(-Mathf.Min(Mathf.Min(top, horn), Mathf.Min(waist, baseCap)));
            });
        }

        private static Color32[] IconArrow(int s)
        {
            Vector2 tail = new Vector2(s * 0.18f, s * 0.28f), head = new Vector2(s * 0.82f, s * 0.72f);
            return Draw(s, s, (x, y) =>
            {
                Vector2 p = new Vector2(x, y);
                float shaft = Capsule(p, tail, head, s * 0.03f);
                float h1 = Capsule(p, head, new Vector2(s * 0.62f, s * 0.72f), s * 0.03f);
                float h2 = Capsule(p, head, new Vector2(s * 0.82f, s * 0.52f), s * 0.03f);
                float f1 = Capsule(p, tail, new Vector2(s * 0.30f, s * 0.18f), s * 0.028f);
                float f2 = Capsule(p, tail, new Vector2(s * 0.10f, s * 0.34f), s * 0.028f);
                return AA(-Mathf.Min(Mathf.Min(shaft, Mathf.Min(h1, h2)), Mathf.Min(f1, f2)));
            });
        }

        private static float RoundedRectSDF(float x, float y, float w, float h, float r)
        {
            // Distance from point to rounded-rect boundary (negative = inside)
            Vector2 half = new Vector2(w / 2f - 2f, h / 2f - 2f);
            Vector2 p = new Vector2(x - w / 2f, y - h / 2f);
            Vector2 q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + Vector2.one * r;
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude
                 + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        /// <summary>Anti-aliased coverage from a signed distance (positive = covered).</summary>
        private static float AA(float d) => Mathf.Clamp01(d + 0.5f);

        private static Color32[] Draw(int w, int h, System.Func<float, float, float> coverage)
        {
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    byte a = (byte)(Mathf.Clamp01(coverage(x + 0.5f, y + 0.5f)) * 255);
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
                }
            return pixels;
        }

        private static void Save(string name, Color32[] pixels, int border = 0)
        {
            int size = (int)Mathf.Sqrt(pixels.Length); // all sprites are square
            int w = size, h = size;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            WriteSprite($"{Dir}/{name}.png", png, border);
            if (System.Array.IndexOf(RuntimeShared, name) >= 0)
                WriteSprite($"{ResourcesDir}/{name}.png", png, border);
        }

        private static void WriteSprite(string path, byte[] png, int border)
        {
            File.WriteAllBytes(path, png);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            if (border > 0)
                importer.spriteBorder = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
        }
    }
}
#endif
