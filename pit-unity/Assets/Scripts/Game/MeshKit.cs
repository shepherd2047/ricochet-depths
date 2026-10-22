// Procedural low-poly meshes (style.md §2, §3, §8): flat-shaded, jittered, coloured per face through vertex colours
// (no textures, so the texture budgets are trivially met). Every recipe is original and kept under the vertex budgets:
// foes ≤ 400, elites and bosses ≤ 1500, the hero ≤ 1000, wall modules ≤ 2000.
//
// Unit space: the footprint is x, y in [-0.5, 0.5] (y up on screen, so -y is the side facing the hero), and the body
// rises toward the camera along -z. uv.x marks emissive faces (only foes' eyes and weak spots glow).
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public sealed class MeshKit
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Vector3> n = new List<Vector3>();
        readonly List<Color32> c = new List<Color32>();
        readonly List<Vector2> u = new List<Vector2>();
        readonly List<int> t = new List<int>();
        System.Random rng;

        public MeshKit(int seed) { rng = new System.Random(seed); }
        public int Verts { get { return v.Count; } }

        float J(float a) { return ((float)rng.NextDouble() * 2 - 1) * a; }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetColors(c);
            m.SetUVs(0, u);
            m.SetTriangles(t, 0, true);
            return m;
        }

        // ------------------------------------------------------------ faces
        /// <summary>A flat triangle, wound so its normal points away from <paramref name="inside"/>.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 d, Color32 col, Vector3 inside, float emit = 0)
        {
            var nn = Vector3.Cross(b - a, d - a);
            if (Vector3.Dot(nn, (a + b + d) / 3 - inside) < 0) { var x = b; b = d; d = x; nn = -nn; }
            nn = nn.normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(d);
            for (int k = 0; k < 3; k++) { n.Add(nn); c.Add(col); u.Add(new Vector2(emit, 0)); }
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 col, Vector3 inside, float emit = 0)
        {
            Tri(a, b, d, col, inside, emit);
            Tri(a, d, e, col, inside, emit);
        }

        // ------------------------------------------------------------ solids
        /// <summary>A jittered box. faceTint darkens the sides relative to the top (the face toward the camera).</summary>
        public void Box(Vector3 center, Vector3 size, Color32 col, float jitter = 0, float emit = 0, Color32? top = null)
        {
            var h = size / 2;
            var p = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var s = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                p[i] = center + Vector3.Scale(s, h) + new Vector3(J(jitter), J(jitter), J(jitter));
            }
            var tc = top ?? col;
            Quad(p[0], p[1], p[3], p[2], tc, center, emit);   // z- (toward camera)
            Quad(p[4], p[5], p[7], p[6], col, center, emit);  // z+
            Quad(p[0], p[1], p[5], p[4], col, center, emit);  // y-
            Quad(p[2], p[3], p[7], p[6], col, center, emit);  // y+
            Quad(p[0], p[2], p[6], p[4], col, center, emit);  // x-
            Quad(p[1], p[3], p[7], p[5], col, center, emit);  // x+
        }

        /// <summary>
        /// Surface of revolution around an axis through <paramref name="center"/>. profile[i] = (radius, offset along the
        /// axis); sides is the number of facets. Radii of 0 close the ends.
        /// </summary>
        public void Lathe(Vector3 center, Vector3 axis, Vector2[] profile, int sides, Color32 col, float jitter = 0,
            float emit = 0, float sx = 1, float sy = 1, Color32? cap = null)
        {
            axis = axis.normalized;
            var a1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var a2 = Vector3.Cross(axis, a1);
            var ring = new Vector3[profile.Length, sides];
            for (int r = 0; r < profile.Length; r++)
                for (int s = 0; s < sides; s++)
                {
                    float ang = (s + (r % 2) * 0.5f) * Mathf.PI * 2 / sides;
                    float rad = profile[r].x * (1 + (profile[r].x > 0 ? J(jitter) : 0));
                    ring[r, s] = center + axis * profile[r].y + a1 * (Mathf.Cos(ang) * rad * sx) + a2 * (Mathf.Sin(ang) * rad * sy);
                }
            for (int r = 0; r < profile.Length - 1; r++)
            {
                var mid = center + axis * ((profile[r].y + profile[r + 1].y) / 2);
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    var p0 = ring[r, s]; var p1 = ring[r, s1]; var p2 = ring[r + 1, s1]; var p3 = ring[r + 1, s];
                    if (profile[r].x <= 0) Tri(p0, p2, p3, col, mid, emit);
                    else if (profile[r + 1].x <= 0) Tri(p0, p1, p2, col, mid, emit);
                    else if (r % 2 == 0) { Tri(p0, p1, p2, col, mid, emit); Tri(p0, p2, p3, col, mid, emit); }
                    else { Tri(p0, p1, p3, col, mid, emit); Tri(p1, p2, p3, col, mid, emit); }
                }
            }
            // flat caps where the profile does not close
            var capCol = cap ?? col;
            if (profile[0].x > 0) Cap(ring, 0, sides, center + axis * profile[0].y, capCol, center + axis * profile[1].y);
            int last = profile.Length - 1;
            if (profile[last].x > 0) Cap(ring, last, sides, center + axis * profile[last].y, capCol, center + axis * profile[last - 1].y);
        }

        void Cap(Vector3[,] ring, int r, int sides, Vector3 mid, Color32 col, Vector3 inside)
        {
            for (int s = 0; s < sides; s++) Tri(mid, ring[r, s], ring[r, (s + 1) % sides], col, inside);
        }

        /// <summary>A low-poly blob: a lathe around z with a rounded profile. size = footprint (x, y) and height (z).</summary>
        public void Blob(Vector3 baseCenter, Vector3 size, Color32 col, int sides = 7, float jitter = 0.08f, Color32? top = null)
        {
            var prof = new[]
            {
                new Vector2(0.38f, 0), new Vector2(0.5f, -0.25f), new Vector2(0.46f, -0.62f), new Vector2(0.28f, -0.9f), new Vector2(0, -1f),
            };
            for (int i = 0; i < prof.Length; i++) prof[i].y *= size.z;
            Lathe(baseCenter, Vector3.forward, prof, sides, col, jitter, 0, size.x, size.y, top);
        }

        /// <summary>A cone from base (radius) to tip.</summary>
        public void Spike(Vector3 basePos, Vector3 tip, float radius, Color32 col, int sides = 4, float emit = 0)
        {
            var axis = tip - basePos;
            Lathe(basePos, axis, new[] { new Vector2(0, 0), new Vector2(radius, 0.0001f), new Vector2(0, axis.magnitude) }, sides, col, 0, emit);
        }

        public void Eye(Vector3 pos, float r, Color32 col)
        {
            Lathe(pos, Vector3.forward, new[] { new Vector2(0, 0.4f * r), new Vector2(r, 0), new Vector2(0, -0.6f * r) }, 5, col, 0, 1);
        }
    }

    /// <summary>Colour rules from style.md §3, as HSV clamps.</summary>
    public static class Palette
    {
        public static Color32 Hsv(float h, float s, float v)
        {
            h = Mathf.Repeat(h, 1) * 6;
            int i = Mathf.FloorToInt(h);
            float f = h - i, p = v * (1 - s), q = v * (1 - s * f), w = v * (1 - s * (1 - f));
            float r, g, b;
            switch (i % 6)
            {
                case 0: r = v; g = w; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = w; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = w; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return new Color32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), 255);
        }

        public static void ToHsv(uint rgb, out float h, out float s, out float v)
        {
            float r = ((rgb >> 16) & 255) / 255f, g = ((rgb >> 8) & 255) / 255f, b = (rgb & 255) / 255f;
            float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b)), d = mx - mn;
            v = mx; s = mx > 0 ? d / mx : 0;
            if (d <= 0) h = 0;
            else if (mx == r) h = Mathf.Repeat((g - b) / d / 6, 1);
            else if (mx == g) h = ((b - r) / d + 2) / 6;
            else h = ((r - g) / d + 4) / 6;
        }

        /// <summary>A foe's body colour: its identity hue, desaturated and dark (V 0–30 %, S 15–45 %).</summary>
        public static Color32 FoeBody(uint rgb, float v = 0.26f)
        {
            float h, s, vv; ToHsv(rgb, out h, out s, out vv);
            return Hsv(h, Mathf.Clamp(s * 0.55f, 0.15f, 0.45f), v);
        }

        public static readonly Color32 Accent = Hsv(0.13f, 0.75f, 0.72f);       // small yellow accents (V 55–85 %)
        public static readonly Color32 Bone = Hsv(0.11f, 0.25f, 0.6f);
        public static readonly Color32 Steel = Hsv(0.62f, 0.18f, 0.33f);
        public static readonly Color32 Stone = Hsv(0.68f, 0.22f, 0.24f);       // scenery: V 10–33 %, S 15–45 %
        public static readonly Color32 StoneDark = Hsv(0.7f, 0.25f, 0.14f);
        public static readonly Color32 Floor = Hsv(0.66f, 0.2f, 0.16f);
        public static readonly Color32 FloorAlt = Hsv(0.7f, 0.24f, 0.13f);
        public static readonly Color32 Moss = Hsv(0.33f, 0.35f, 0.18f);

        /// <summary>Glowing eye colour; drawn emissive so it is the only part of a foe that blooms.</summary>
        public static Color32 EyeGlow(uint rgb)
        {
            float h, s, v; ToHsv(rgb, out h, out s, out v);
            return Hsv(Mathf.Lerp(h, 0.12f, 0.6f), 0.7f, 1f);
        }
    }

    /// <summary>The recipes. One mesh per foe type, cached; bosses and the hero have their own.</summary>
    public static class Models
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();
        public static int MaxFoeVerts, MaxBossVerts, HeroVerts;

        static int Seed(string s) { int h = 17; foreach (char ch in s) h = h * 31 + ch; return h; }

        public static Mesh Foe(string type, uint color, bool elite)
        {
            Mesh m;
            if (cache.TryGetValue(type, out m)) return m;
            var k = new MeshKit(Seed(type));
            var body = Palette.FoeBody(color);
            var belly = Palette.FoeBody(color, 0.14f);
            var top = Palette.FoeBody(color, 0.3f);
            var eye = Palette.EyeGlow(color);
            var acc = Palette.Accent;
            float H = 0.72f;   // standard height toward the camera
            switch (type)
            {
                case "grunt":
                    k.Blob(new Vector3(0, 0.03f, 0), new Vector3(0.84f, 0.8f, H), body, 7, 0.08f, top);
                    k.Box(new Vector3(-0.42f, -0.1f, -0.25f), new Vector3(0.16f, 0.3f, 0.2f), belly, 0.03f);
                    k.Box(new Vector3(0.42f, -0.1f, -0.25f), new Vector3(0.16f, 0.3f, 0.2f), belly, 0.03f);
                    k.Box(new Vector3(0, -0.36f, -0.3f), new Vector3(0.12f, 0.06f, 0.06f), acc);
                    Eyes(k, 0.16f, -0.3f, -0.55f, 0.07f, eye);
                    break;
                case "mite":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.7f, 0.7f, 0.5f), body, 6, 0.06f, top);
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = i < 2 ? -1 : 1, sy = i % 2 == 0 ? -0.15f : 0.2f;
                        k.Box(new Vector3(sx * 0.4f, sy, -0.06f), new Vector3(0.22f, 0.06f, 0.06f), belly);
                    }
                    Eyes(k, 0.12f, -0.26f, -0.38f, 0.07f, eye);
                    break;
                case "brick":
                    k.Box(new Vector3(0, 0.02f, -0.34f), new Vector3(0.82f, 0.78f, 0.68f), body, 0.05f, 0, top);
                    k.Box(new Vector3(-0.36f, -0.05f, -0.62f), new Vector3(0.28f, 0.36f, 0.16f), belly, 0.03f);
                    k.Box(new Vector3(0.36f, -0.05f, -0.62f), new Vector3(0.28f, 0.36f, 0.16f), belly, 0.03f);
                    k.Box(new Vector3(0, -0.4f, -0.25f), new Vector3(0.3f, 0.05f, 0.08f), acc);
                    Eyes(k, 0.15f, -0.38f, -0.56f, 0.065f, eye);
                    break;
                case "archer":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.72f, 0.72f, 0.85f), body, 7, 0.06f, top);
                    k.Lathe(new Vector3(0, -0.3f, -0.45f), Vector3.down, new[] { new Vector2(0.1f, 0), new Vector2(0.08f, 0.22f), new Vector2(0.11f, 0.26f) }, 5, belly);
                    k.Box(new Vector3(0, -0.56f, -0.45f), new Vector3(0.16f, 0.04f, 0.16f), acc);
                    Eyes(k, 0.14f, -0.25f, -0.72f, 0.06f, eye);
                    break;
                case "slab":
                    k.Box(new Vector3(0, 0, -0.24f), new Vector3(0.94f, 0.82f, 0.48f), body, 0.04f, 0, top);
                    for (int i = 0; i < 4; i++) k.Box(new Vector3(-0.33f + i * 0.22f, 0.05f, -0.52f), new Vector3(0.18f, 0.6f, 0.08f), belly, 0.02f);
                    k.Box(new Vector3(0, -0.42f, -0.2f), new Vector3(0.5f, 0.04f, 0.06f), acc);
                    Eyes(k, 0.3f, -0.36f, -0.42f, 0.045f, eye);
                    break;
                case "popper":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.86f, 0.86f, 0.7f), body, 7, 0.1f, top);
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 1.7f + 0.4f;
                        k.Box(new Vector3(Mathf.Cos(a) * 0.24f, Mathf.Sin(a) * 0.24f, -0.5f), new Vector3(0.18f, 0.18f, 0.18f), top, 0.03f);
                    }
                    k.Box(new Vector3(0, 0, -0.68f), new Vector3(0.08f, 0.08f, 0.06f), acc);
                    Eyes(k, 0.14f, -0.3f, -0.48f, 0.06f, eye);
                    break;
                case "flyer":
                    k.Blob(new Vector3(0, 0, -0.1f), new Vector3(0.5f, 0.6f, 0.45f), body, 6, 0.06f, top);
                    k.Quad(new Vector3(-0.2f, 0.05f, -0.35f), new Vector3(-0.55f, 0.3f, -0.42f), new Vector3(-0.6f, -0.1f, -0.38f), new Vector3(-0.2f, -0.15f, -0.33f), belly, new Vector3(-0.3f, 0, 0));
                    k.Quad(new Vector3(-0.2f, 0.05f, -0.35f), new Vector3(-0.55f, 0.3f, -0.42f), new Vector3(-0.6f, -0.1f, -0.38f), new Vector3(-0.2f, -0.15f, -0.33f), belly, new Vector3(-0.3f, 0, -1));
                    k.Quad(new Vector3(0.2f, 0.05f, -0.35f), new Vector3(0.55f, 0.3f, -0.42f), new Vector3(0.6f, -0.1f, -0.38f), new Vector3(0.2f, -0.15f, -0.33f), belly, new Vector3(0.3f, 0, 0));
                    k.Quad(new Vector3(0.2f, 0.05f, -0.35f), new Vector3(0.55f, 0.3f, -0.42f), new Vector3(0.6f, -0.1f, -0.38f), new Vector3(0.2f, -0.15f, -0.33f), belly, new Vector3(0.3f, 0, -1));
                    k.Spike(new Vector3(0, -0.25f, -0.3f), new Vector3(0, -0.45f, -0.3f), 0.05f, acc);
                    Eyes(k, 0.1f, -0.22f, -0.48f, 0.05f, eye);
                    break;
                case "pavise":
                    k.Blob(new Vector3(0, 0.08f, 0), new Vector3(0.74f, 0.7f, 0.7f), body, 7, 0.06f, top);
                    Eyes(k, 0.14f, -0.2f, -0.62f, 0.06f, eye);
                    break;
                case "mender":
                    k.Blob(new Vector3(0, 0.02f, 0), new Vector3(0.78f, 0.78f, 0.72f), body, 7, 0.06f, top);
                    k.Box(new Vector3(0, 0.08f, -0.74f), new Vector3(0.1f, 0.34f, 0.06f), acc);
                    k.Box(new Vector3(0, 0.08f, -0.74f), new Vector3(0.34f, 0.1f, 0.06f), acc);
                    Eyes(k, 0.15f, -0.3f, -0.52f, 0.06f, eye);
                    break;
                case "skitter":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.7f, 0.82f, 0.44f), body, 6, 0.05f, top);
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = i < 2 ? -1 : 1, sy = -0.2f + (i % 2) * 0.4f;
                        k.Box(new Vector3(sx * 0.42f, sy, -0.08f), new Vector3(0.26f, 0.05f, 0.05f), belly, 0.02f);
                    }
                    k.Spike(new Vector3(-0.1f, -0.38f, -0.2f), new Vector3(-0.14f, -0.56f, -0.2f), 0.04f, acc);
                    k.Spike(new Vector3(0.1f, -0.38f, -0.2f), new Vector3(0.14f, -0.56f, -0.2f), 0.04f, acc);
                    Eyes(k, 0.12f, -0.3f, -0.36f, 0.055f, eye);
                    break;
                case "mortar":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.8f, 0.78f, 0.55f), body, 7, 0.05f, top);
                    k.Lathe(new Vector3(0, 0.08f, -0.45f), Vector3.back, new[] { new Vector2(0.15f, 0), new Vector2(0.13f, 0.32f), new Vector2(0.16f, 0.34f) }, 6, Palette.Steel);
                    k.Box(new Vector3(0, 0.08f, -0.8f), new Vector3(0.1f, 0.1f, 0.02f), acc);
                    Eyes(k, 0.16f, -0.3f, -0.42f, 0.055f, eye);
                    break;
                case "nester":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.82f, 0.8f, 0.6f), body, 7, 0.07f, top);
                    k.Blob(new Vector3(0, 0.15f, -0.45f), new Vector3(0.32f, 0.38f, 0.3f), Palette.Bone, 6, 0.03f);
                    Eyes(k, 0.15f, -0.3f, -0.45f, 0.06f, eye);
                    break;
                case "chiller":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.76f, 0.76f, 0.66f), body, 7, 0.06f, top);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.256f;
                        var b = new Vector3(Mathf.Cos(a) * 0.18f, Mathf.Sin(a) * 0.18f + 0.05f, -0.5f);
                        k.Spike(b, b + new Vector3(Mathf.Cos(a) * 0.15f, Mathf.Sin(a) * 0.15f, -0.25f), 0.06f, Palette.Hsv(0.52f, 0.35f, 0.42f));
                    }
                    Eyes(k, 0.14f, -0.3f, -0.48f, 0.06f, eye);
                    break;
                case "delver":
                    k.Blob(new Vector3(0, 0.06f, 0), new Vector3(0.78f, 0.74f, 0.6f), body, 7, 0.07f, top);
                    k.Spike(new Vector3(0, -0.28f, -0.3f), new Vector3(0, -0.62f, -0.3f), 0.17f, Palette.Steel, 5);
                    k.Box(new Vector3(0, -0.3f, -0.3f), new Vector3(0.36f, 0.04f, 0.04f), acc);
                    Eyes(k, 0.17f, -0.2f, -0.52f, 0.055f, eye);
                    break;
                case "lancer":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.72f, 0.74f, 0.7f), body, 7, 0.05f, top);
                    k.Spike(new Vector3(0, -0.2f, -0.4f), new Vector3(0, -0.66f, -0.36f), 0.07f, acc, 4);
                    Eyes(k, 0.16f, -0.25f, -0.6f, 0.055f, eye);
                    break;
                case "burr":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.78f, 0.78f, 0.62f), body, 7, 0.05f, top);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        var b = new Vector3(Mathf.Cos(a) * 0.34f, Mathf.Sin(a) * 0.34f, -0.3f);
                        k.Spike(b, b + new Vector3(Mathf.Cos(a) * 0.24f, Mathf.Sin(a) * 0.24f, -0.06f), 0.06f, i % 2 == 0 ? Palette.Bone : acc);
                    }
                    Eyes(k, 0.13f, -0.25f, -0.52f, 0.055f, eye);
                    break;
                case "dasher":
                    k.Lathe(new Vector3(0, 0.45f, -0.25f), Vector3.down, new[] { new Vector2(0.18f, 0), new Vector2(0.36f, 0.35f), new Vector2(0.28f, 0.75f), new Vector2(0, 0.95f) }, 5, body, 0.04f, 0, 0.75f, 1);
                    k.Box(new Vector3(-0.3f, 0.32f, -0.42f), new Vector3(0.05f, 0.2f, 0.12f), acc);
                    k.Box(new Vector3(0.3f, 0.32f, -0.42f), new Vector3(0.05f, 0.2f, 0.12f), acc);
                    Eyes(k, 0.12f, -0.25f, -0.42f, 0.05f, eye);
                    break;
                case "gulper":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.94f, 0.86f, 0.62f), body, 9, 0.06f, top);
                    k.Box(new Vector3(0, -0.34f, -0.3f), new Vector3(0.58f, 0.14f, 0.22f), Palette.Hsv(0, 0, 0.03f), 0.02f);
                    for (int i = 0; i < 4; i++) k.Spike(new Vector3(-0.21f + i * 0.14f, -0.36f, -0.42f), new Vector3(-0.21f + i * 0.14f, -0.36f, -0.3f), 0.035f, Palette.Bone, 3);
                    Eyes(k, 0.24f, -0.24f, -0.54f, 0.045f, eye);
                    break;
                case "flanker":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.68f, 0.74f, 0.7f), body, 7, 0.05f, top);
                    Eyes(k, 0.13f, -0.28f, -0.6f, 0.055f, eye);
                    k.Box(new Vector3(0, -0.36f, -0.28f), new Vector3(0.1f, 0.04f, 0.06f), acc);
                    break;
                case "hurler":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.86f, 0.8f, 0.6f), body, 8, 0.05f, top);
                    k.Box(new Vector3(0.36f, -0.05f, -0.48f), new Vector3(0.16f, 0.4f, 0.14f), belly, 0.03f);
                    k.Box(new Vector3(0.36f, -0.3f, -0.55f), new Vector3(0.22f, 0.22f, 0.2f), Palette.Hsv(0.06f, 0.4f, 0.3f), 0.05f);
                    k.Box(new Vector3(-0.2f, -0.38f, -0.3f), new Vector3(0.16f, 0.04f, 0.06f), acc);
                    Eyes(k, 0.2f, -0.3f, -0.46f, 0.045f, eye);
                    break;
                case "hulk":
                    k.Box(new Vector3(0, 0.05f, -0.32f), new Vector3(0.8f, 0.72f, 0.64f), body, 0.04f, 0, top);
                    k.Blob(new Vector3(0, -0.15f, -0.5f), new Vector3(0.42f, 0.36f, 0.32f), top, 6, 0.04f);
                    k.Box(new Vector3(-0.42f, 0, -0.55f), new Vector3(0.24f, 0.42f, 0.24f), belly, 0.03f);
                    k.Box(new Vector3(0.42f, 0, -0.55f), new Vector3(0.24f, 0.42f, 0.24f), belly, 0.03f);
                    k.Spike(new Vector3(-0.42f, 0.05f, -0.66f), new Vector3(-0.5f, 0.12f, -0.86f), 0.06f, acc);
                    k.Spike(new Vector3(0.42f, 0.05f, -0.66f), new Vector3(0.5f, 0.12f, -0.86f), 0.06f, acc);
                    k.Box(new Vector3(0, -0.35f, -0.4f), new Vector3(0.5f, 0.04f, 0.04f), acc);
                    Eyes(k, 0.1f, -0.3f, -0.72f, 0.04f, eye);
                    break;
                case "volleyer":
                    k.Blob(new Vector3(0, 0.05f, 0), new Vector3(0.86f, 0.84f, 0.66f), body, 9, 0.05f, top);
                    for (int i = -1; i <= 1; i++)
                        k.Lathe(new Vector3(i * 0.2f, -0.28f, -0.42f), new Vector3(i * 0.3f, -1, 0), new[] { new Vector2(0.07f, 0), new Vector2(0.06f, 0.24f), new Vector2(0.08f, 0.26f) }, 5, Palette.Steel);
                    k.Box(new Vector3(0, 0.25f, -0.62f), new Vector3(0.5f, 0.06f, 0.06f), acc);
                    Eyes(k, 0.12f, -0.18f, -0.62f, 0.04f, eye);
                    break;
                case "pod":
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.9f, 0.9f, 0.6f), Palette.Hsv(0.22f, 0.4f, 0.26f), 7, 0.06f, Palette.Hsv(0.22f, 0.42f, 0.3f));
                    k.Eye(new Vector3(0, 0, -0.62f), 0.12f, Palette.Hsv(0.2f, 0.6f, 1));
                    break;
                default:
                    k.Blob(new Vector3(0, 0, 0), new Vector3(0.8f, 0.8f, H), body, 7, 0.07f, top);
                    Eyes(k, 0.15f, -0.28f, -0.55f, 0.06f, eye);
                    break;
            }
            if (elite) k.Box(new Vector3(0, 0.4f, -0.2f), new Vector3(0.6f, 0.05f, 0.1f), acc);   // elites wear a gold band
            MaxFoeVerts = Mathf.Max(MaxFoeVerts, k.Verts);
            m = k.Build("foe-" + type);
            cache[type] = m;
            return m;
        }

        static void Eyes(MeshKit k, float dx, float y, float z, float r, Color32 c)
        {
            k.Eye(new Vector3(-dx, y, z), r, c);
            k.Eye(new Vector3(dx, y, z), r, c);
        }

        // ------------------------------------------------------------ delivered models (pit-art/models/lowpoly)
        /// <summary>A delivered low-poly model: mesh in unit space (feet at z = 0, rising along -z), its material and
        /// its half-extents (x, y footprint from the pivot; z height).</summary>
        public sealed class Asset { public Mesh Mesh; public Material Mat; public Vector3 Half; public Dictionary<string, Clip> Clips; public HeroRig.Data Rig; }
        /// <summary>A baked clip: one mesh per stepped frame (no blending between them, style.md §6).</summary>
        public sealed class Clip
        {
            public Mesh[] Frames; public bool Loop; public float Fps;
            public float Length { get { return Frames.Length / Fps; } }
            /// <summary>The frame shown t seconds into the clip; a one-shot clip holds its last frame.</summary>
            public Mesh At(float t)
            {
                int i = Mathf.FloorToInt(Mathf.Max(0, t) * Fps);
                return Frames[Loop ? i % Frames.Length : Mathf.Min(i, Frames.Length - 1)];
            }
        }
        static readonly Dictionary<string, Asset> assets = new Dictionary<string, Asset>();
        /// <summary>Models that share a theme atlas (pit-art monster_lib: &lt;atlas&gt;_albedo + &lt;atlas&gt;_emissive).</summary>
        static readonly Dictionary<string, string> Atlas = new Dictionary<string, string>
        {
            { "enemy_shambler", "enemy_theme1" }, { "enemy_brute", "enemy_theme1" }, { "enemy_spitter", "enemy_theme1" },
        };

        /// <summary>
        /// Loads Resources/Models/&lt;name&gt;.bytes (written by Tools/glb_to_bytes.py from the art team's GLB, Blender axes:
        /// -y front, z up) and its _albedo texture. Foes keep their front toward the hero (-y); faceAway turns the model
        /// round so the hero shows its back to the camera and faces its aim (+y). glow fakes the missing emissive map:
        /// the light painted accents (eyes, seams, flues) are lifted over the bloom threshold in the shader.
        /// </summary>
        public static Asset Model(string name, bool faceAway, bool glow, Shader shader)
        {
            Asset a;
            if (assets.TryGetValue(name, out a)) return a;
            var ta = Resources.Load<TextAsset>("Models/" + name);
            if (ta == null) { assets[name] = null; return null; }
            var b = ta.bytes;
            // v1: nv, ni, 32-byte verts. v2 (current glb_to_bytes.py): nv, nsub, nsub counts, 36-byte verts (+ RGBA, unused
            // here); the submeshes are merged. A v1 index count is at least 3, so a second int below 3 means v2.
            int nv = System.BitConverter.ToInt32(b, 0), ni = System.BitConverter.ToInt32(b, 4), o = 8, stride = 32;
            if (ni < 3)
            {
                int nsub = ni; ni = 0;
                for (int k = 0; k < nsub; k++, o += 4) ni += System.BitConverter.ToInt32(b, o);
                stride = 36;
            }
            var pos = new List<Vector3>(nv); var nor = new List<Vector3>(nv); var uv = new List<Vector2>(nv); var col = new List<Color32>(nv);
            float s = faceAway ? -1 : 1;
            var mn = new Vector3(1e9f, 1e9f, 1e9f); var mx = -mn;
            for (int i = 0; i < nv; i++, o += stride)
            {
                var p = new Vector3(s * F(b, o), s * F(b, o + 4), -F(b, o + 8));
                pos.Add(p); mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
                nor.Add(new Vector3(s * F(b, o + 12), s * F(b, o + 16), -F(b, o + 20)));
                uv.Add(new Vector2(F(b, o + 24), F(b, o + 28)));
                col.Add(new Color32(255, 255, 255, 255));
            }
            var tri = new List<int>(ni);
            for (int i = 0; i < ni; i++, o += 2) tri.Add(System.BitConverter.ToUInt16(b, o));
            // the axis change mirrors the model; keep Unity's front face (normal = Cross(b - a, c - a)) on the outside
            float agree = 0;
            for (int i = 0; i < ni; i += 3)
                agree += Vector3.Dot(Vector3.Cross(pos[tri[i + 1]] - pos[tri[i]], pos[tri[i + 2]] - pos[tri[i]]), nor[tri[i]] + nor[tri[i + 1]] + nor[tri[i + 2]]);
            if (agree < 0) for (int i = 0; i < ni; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; }
            var m = new Mesh { name = name };
            m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(tri, 0, true);
            var clips = Clips(name, s, uv, col, tri);
            var mat = new Material(shader) { name = name };
            string tex;
            if (!Atlas.TryGetValue(name, out tex)) tex = name;
            mat.SetTexture("_MainTex", Resources.Load<Texture2D>("Models/" + tex + "_albedo"));
            mat.SetFloat("_Textured", 1);
            // a delivered emissive map replaces the light-albedo guess (_TexEmit 2: the glow is the map's colour)
            var emis = glow ? Resources.Load<Texture2D>("Models/" + tex + "_emissive") : null;
            if (emis != null) mat.SetTexture("_EmisMap", emis);
            mat.SetFloat("_TexEmit", emis != null ? 2 : glow ? 1 : 0);
            a = new Asset
            {
                Mesh = m, Mat = mat,
                Half = new Vector3(Mathf.Max(-mn.x, mx.x), Mathf.Max(-mn.y, mx.y), mx.z - mn.z),
                Clips = clips,
                // the layered hero rig (Tools/bake_hero_rig.py), composed and skinned at runtime by HeroRig
                Rig = faceAway ? HeroRig.Load(name, s, uv, col, tri) : null,
            };
            assets[name] = a;
            if (glow) MaxFoeVerts = Mathf.Max(MaxFoeVerts, nv); else HeroVerts = Mathf.Max(HeroVerts, nv);
            return a;
        }

        static float F(byte[] b, int o) { return System.BitConverter.ToSingle(b, o); }

        /// <summary>
        /// Loads Resources/Models/&lt;name&gt;_anim.bytes (Tools/bake_hero_anim.py: the rig's clips sampled at a stepped
        /// fps, positions as int16 over the clip bounds, normals as int8) into one mesh per frame, sharing the rest
        /// mesh's uvs, colours and (already rewound) triangles. Null when the model has no baked clips.
        /// </summary>
        static Dictionary<string, Clip> Clips(string name, float s, List<Vector2> uv, List<Color32> col, List<int> tri)
        {
            var ta = Resources.Load<TextAsset>("Models/" + name + "_anim");
            if (ta == null) return null;
            var b = ta.bytes;
            int nv = System.BitConverter.ToInt32(b, 0), nc = System.BitConverter.ToInt32(b, 4), o = 36;
            if (nv != uv.Count) return null;
            float fps = F(b, 8);
            var mn = new Vector3(F(b, 12), F(b, 16), F(b, 20)); var span = new Vector3(F(b, 24), F(b, 28), F(b, 32)) - mn;
            int stride = (nv * 9 + 3) & ~3;
            var clips = new Dictionary<string, Clip>();
            var pos = new List<Vector3>(nv); var nor = new List<Vector3>(nv);
            for (int c = 0; c < nc; c++)
            {
                string cn = System.Text.Encoding.ASCII.GetString(b, o + 1, b[o]); o += 1 + b[o];
                var clip = new Clip { Loop = b[o] != 0, Fps = fps, Frames = new Mesh[System.BitConverter.ToInt32(b, o + 1)] };
                o += 5;
                for (int f = 0; f < clip.Frames.Length; f++, o += stride)
                {
                    pos.Clear(); nor.Clear();
                    for (int i = 0, q = o; i < nv; i++, q += 9)
                    {
                        float x = mn.x + (System.BitConverter.ToInt16(b, q) + 32768) / 65535f * span.x;
                        float y = mn.y + (System.BitConverter.ToInt16(b, q + 2) + 32768) / 65535f * span.y;
                        float z = mn.z + (System.BitConverter.ToInt16(b, q + 4) + 32768) / 65535f * span.z;
                        pos.Add(new Vector3(s * x, s * y, -z));
                        nor.Add(new Vector3(s * (sbyte)b[q + 6], s * (sbyte)b[q + 7], -(sbyte)b[q + 8]) / 127f);
                    }
                    var m = new Mesh { name = name + "_" + cn + f };
                    m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(tri, 0, true);
                    clip.Frames[f] = m;
                }
                clips[cn] = clip;
            }
            return clips;
        }

        // ------------------------------------------------------------ corridor kit (pit-scene/kit, ASSEMBLY.md)
        static readonly Dictionary<string, Mesh> scenery = new Dictionary<string, Mesh>();
        public static int SceneryVerts;   // largest kit piece loaded, for the budget check

        /// <summary>
        /// Loads a pit-scene kit piece from Resources/Scene/&lt;name&gt;.bytes (Tools/glb_to_bytes.py: one submesh per
        /// material slot, vertex colour with the light pools' alpha; Blender axes as
        /// in pit-scene/tools/assemble.py: x across, +y along the corridor toward the far end, z up). Kept in kit units
        /// (1 cell = 1.125); the corridor root scales it. z is flipped to the game's frame, where up is -z.
        /// </summary>
        public static Mesh Scenery(string name)
        {
            Mesh m;
            if (scenery.TryGetValue(name, out m)) return m;
            var ta = Resources.Load<TextAsset>("Scene/" + name);
            if (ta == null) { scenery[name] = null; Debug.LogWarning("Models: Resources/Scene/" + name + " missing"); return null; }
            var b = ta.bytes;
            int nv = System.BitConverter.ToInt32(b, 0), nsub = System.BitConverter.ToInt32(b, 4), o = 8;
            var counts = new int[nsub];
            for (int s = 0; s < nsub; s++, o += 4) counts[s] = System.BitConverter.ToInt32(b, o);
            var pos = new List<Vector3>(nv); var nor = new List<Vector3>(nv); var uv = new List<Vector2>(nv); var col = new List<Color32>(nv);
            for (int i = 0; i < nv; i++, o += 36)
            {
                pos.Add(new Vector3(F(b, o), F(b, o + 4), -F(b, o + 8)));
                nor.Add(new Vector3(F(b, o + 12), F(b, o + 16), -F(b, o + 20)));
                uv.Add(new Vector2(F(b, o + 24), F(b, o + 28)));
                col.Add(new Color32(b[o + 32], b[o + 33], b[o + 34], b[o + 35]));
            }
            m = new Mesh { name = name };
            m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetUVs(0, uv);
            m.subMeshCount = nsub;
            List<int> tri0 = null;
            for (int s = 0; s < nsub; s++)
            {
                var tri = new List<int>(counts[s]);
                for (int i = 0; i < counts[s]; i++, o += 2) tri.Add(System.BitConverter.ToUInt16(b, o));
                // the z flip mirrors the mesh: keep the winding that agrees with the normals
                float agree = 0;
                for (int i = 0; i < tri.Count; i += 3)
                    agree += Vector3.Dot(Vector3.Cross(pos[tri[i + 1]] - pos[tri[i]], pos[tri[i + 2]] - pos[tri[i]]), nor[tri[i]] + nor[tri[i + 1]] + nor[tri[i + 2]]);
                if (agree < 0) for (int i = 0; i < tri.Count; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; }
                m.SetTriangles(tri, s, true);
                if (s == 0) tri0 = tri;
            }
            if (name.StartsWith("T1_FloorStrip") && tri0 != null) FloorBase(m, pos, nor, uv, col, tri0);
            scenery[name] = m;
            SceneryVerts = Mathf.Max(SceneryVerts, nv);
            return m;
        }

        /// <summary>
        /// The floor strips are loose bevelled slabs with nothing under them, so the seams showed the black void. A plane
        /// under the slabs, in a tile's own texture colour a little darker, keeps the seams readable but quiet. The bevels
        /// themselves sample a near-black texel of the atlas (the thick black lines between tiles): they take the tile
        /// top's texel instead, a shade darker, so a seam is a soft edge.
        /// </summary>
        const float SeamShade = 0.72f, BevelShade = 0.84f, TopInset = 0.005f;
        static void FloorBase(Mesh m, List<Vector3> pos, List<Vector3> nor, List<Vector2> uv, List<Color32> col, List<int> tri0)
        {
            var tri = new List<int>(tri0);
            // a tile top's middle: the first triangle whose corners all face straight up (up is -z)
            Vector2 tuv = uv[0];
            for (int i = 0; i < tri.Count; i += 3)
                if (nor[tri[i]].z < -0.99f && nor[tri[i + 1]].z < -0.99f && nor[tri[i + 2]].z < -0.99f)
                {
                    tuv = new Vector2((uv[tri[i]].x + uv[tri[i + 1]].x + uv[tri[i + 2]].x) / 3, (uv[tri[i]].y + uv[tri[i + 1]].y + uv[tri[i + 2]].y) / 3);
                    break;
                }
            // the tile tops reach a texel or two into the atlas's black tile border: pull each corner in past it
            var inset = new Vector2[pos.Count];
            for (int i = 0; i < tri.Count; i += 3)
                for (int a = 0; a < 3; a++)
                {
                    int v = tri[i + a], v1 = tri[i + (a + 1) % 3], v2 = tri[i + (a + 2) % 3];
                    if (nor[v].z > -0.99f) continue;
                    float du = uv[v].x < Mathf.Max(uv[v1].x, uv[v2].x) - 1e-4f ? TopInset : uv[v].x > Mathf.Min(uv[v1].x, uv[v2].x) + 1e-4f ? -TopInset : 0;
                    float dv = uv[v].y < Mathf.Max(uv[v1].y, uv[v2].y) - 1e-4f ? TopInset : uv[v].y > Mathf.Min(uv[v1].y, uv[v2].y) + 1e-4f ? -TopInset : 0;
                    inset[v] = new Vector2(du, dv);
                }
            for (int i = 0; i < pos.Count; i++) uv[i] = new Vector2(uv[i].x + inset[i].x, uv[i].y + inset[i].y);
            for (int i = 0; i < pos.Count; i++)
                if (nor[i].z > -0.99f)
                {
                    uv[i] = tuv;
                    var k = col[i];
                    col[i] = new Color32((byte)(k.r * BevelShade), (byte)(k.g * BevelShade), (byte)(k.b * BevelShade), k.a);
                }
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z = float.MinValue;
            foreach (var p in pos) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); z = Mathf.Max(z, p.z); }
            z += 0.002f;   // just under the slabs' feet (up is -z)
            int v0 = pos.Count;
            pos.Add(new Vector3(x0, y0, z)); pos.Add(new Vector3(x1, y0, z));
            pos.Add(new Vector3(x1, y1, z)); pos.Add(new Vector3(x0, y1, z));
            byte c = (byte)(255 * SeamShade);
            for (int k = 0; k < 4; k++) { nor.Add(new Vector3(0, 0, -1)); uv.Add(tuv); col.Add(new Color32(c, c, c, 255)); }
            var q = new List<int> { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 };
            if (Vector3.Dot(Vector3.Cross(pos[v0 + 1] - pos[v0], pos[v0 + 2] - pos[v0]), Vector3.back) < 0) { q[1] = v0 + 2; q[2] = v0 + 1; q[4] = v0 + 3; q[5] = v0 + 2; }
            tri.AddRange(q);
            m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetUVs(0, uv);
            m.SetTriangles(tri, 0, true);
        }

        // ------------------------------------------------------------ XP gems (style.md §1, 经验宝石)
        /// <summary>The XP gem crystal of a value tier (V &lt; 10, &lt; 100, ≥ 100): pit-art's "B Cluster", one, two or three crystals.</summary>
        public static Mesh Gem(int tier) { return GemPiece("gem_t" + (tier + 1)); }
        /// <summary>The glowing ring on the floor under a gem of that tier.</summary>
        public static Mesh GemRing(int tier) { return GemPiece("gem_ring_t" + (tier + 1)); }

        /// <summary>
        /// Loads Resources/Models/&lt;name&gt;.bytes from Tools/gem_glb_to_bytes.py (pit-art models/lowpoly/gem_xp.glb, see
        /// GEM_INTEGRATION.md): untextured, in cells, standing on z = 0, facet colour in the vertex colour and the emissive
        /// strength in uv.x. z is flipped to the game's frame, where up is -z.
        /// </summary>
        const float GemEmis = 0.4f;   // pit-art's emissive is tuned for its preview: PitUnit's ×3 on top washed the facets out to white
        static Mesh GemPiece(string name)
        {
            Mesh m;
            if (cache.TryGetValue(name, out m)) return m;
            var ta = Resources.Load<TextAsset>("Models/" + name);
            if (ta == null) { Debug.LogWarning("Models: Resources/Models/" + name + " missing"); cache[name] = null; return null; }
            var b = ta.bytes;
            int nv = System.BitConverter.ToInt32(b, 0), ni = System.BitConverter.ToInt32(b, 8), o = 12;
            var pos = new List<Vector3>(nv); var nor = new List<Vector3>(nv); var uv = new List<Vector2>(nv); var col = new List<Color32>(nv);
            for (int i = 0; i < nv; i++, o += 36)
            {
                pos.Add(new Vector3(F(b, o), F(b, o + 4), -F(b, o + 8)));
                nor.Add(new Vector3(F(b, o + 12), F(b, o + 16), -F(b, o + 20)));
                uv.Add(new Vector2(F(b, o + 24) * GemEmis, F(b, o + 28)));
                col.Add(new Color32(b[o + 32], b[o + 33], b[o + 34], 255));
            }
            var tri = new List<int>(ni);
            for (int i = 0; i < ni; i++, o += 2) tri.Add(System.BitConverter.ToUInt16(b, o));
            // the z flip mirrors the mesh: keep the winding that agrees with the normals
            float agree = 0;
            for (int i = 0; i < ni; i += 3)
                agree += Vector3.Dot(Vector3.Cross(pos[tri[i + 1]] - pos[tri[i]], pos[tri[i + 2]] - pos[tri[i]]), nor[tri[i]] + nor[tri[i + 1]] + nor[tri[i + 2]]);
            if (agree < 0) for (int i = 0; i < ni; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; }
            m = new Mesh { name = name };
            m.SetVertices(pos); m.SetNormals(nor); m.SetColors(col); m.SetUVs(0, uv); m.SetTriangles(tri, 0, true);
            cache[name] = m;
            return m;
        }

        // ------------------------------------------------------------ parts that toggle at runtime
        public static Mesh Plate()
        {
            Mesh m;
            if (cache.TryGetValue("plate", out m)) return m;
            var k = new MeshKit(3);
            k.Box(new Vector3(0, 0, -0.5f), new Vector3(1, 1, 1), Palette.Steel, 0.02f, 0, Palette.Hsv(0.62f, 0.15f, 0.4f));
            m = k.Build("plate");
            cache["plate"] = m;
            return m;
        }

        // ------------------------------------------------------------ bosses
        public static Mesh Bell()
        {
            Mesh m;
            if (cache.TryGetValue("bell", out m)) return m;
            var k = new MeshKit(11);
            var body = Palette.Hsv(0.72f, 0.4f, 0.28f);
            // the bell's mouth faces the hero (-y); the body tapers toward the top of the screen
            k.Lathe(new Vector3(0, 0.5f, -0.35f), Vector3.down, new[]
            {
                new Vector2(0, 0), new Vector2(0.16f, 0.02f), new Vector2(0.24f, 0.15f), new Vector2(0.27f, 0.45f),
                new Vector2(0.36f, 0.75f), new Vector2(0.5f, 0.92f), new Vector2(0.44f, 0.97f),
            }, 10, body, 0.02f, 0, 0.7f, 1, Palette.Hsv(0.72f, 0.4f, 0.12f));   // sx is depth, sy width for a y axis
            k.Lathe(new Vector3(0, -0.47f, -0.35f), Vector3.down, new[] { new Vector2(0.08f, 0), new Vector2(0.11f, 0.06f), new Vector2(0, 0.12f) }, 6, Palette.Steel);
            // crown spikes: the glowing weak spot
            for (int i = -1; i <= 1; i++)
                k.Spike(new Vector3(i * 0.14f, 0.44f, -0.42f), new Vector3(i * 0.18f, 0.62f, -0.5f), 0.06f, Palette.Accent, 4, 1);
            k.Eye(new Vector3(0, 0.05f, -0.72f), 0.1f, Palette.Hsv(0.12f, 0.5f, 1));
            MaxBossVerts = Mathf.Max(MaxBossVerts, k.Verts);
            m = k.Build("boss-bell");
            cache["bell"] = m;
            return m;
        }

        public static Mesh Hive()
        {
            Mesh m;
            if (cache.TryGetValue("hive", out m)) return m;
            var k = new MeshKit(12);
            var body = Palette.Hsv(0.2f, 0.42f, 0.24f);
            k.Lathe(new Vector3(0, 0, 0), Vector3.back, new[] { new Vector2(0.48f, 0), new Vector2(0.5f, 0.25f), new Vector2(0.44f, 0.55f), new Vector2(0.3f, 0.7f) }, 6, body, 0, 0, 1, 1, Palette.Hsv(0.2f, 0.42f, 0.3f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3;
                k.Box(new Vector3(Mathf.Cos(a) * 0.36f, Mathf.Sin(a) * 0.36f, -0.6f), new Vector3(0.14f, 0.14f, 0.08f), Palette.Hsv(0.2f, 0.4f, 0.14f), 0.01f);
            }
            k.Eye(new Vector3(0, 0, -0.74f), 0.16f, Palette.Hsv(0.12f, 0.7f, 1));
            MaxBossVerts = Mathf.Max(MaxBossVerts, k.Verts);
            m = k.Build("boss-hive");
            cache["hive"] = m;
            return m;
        }

        public static Mesh Maw()
        {
            Mesh m;
            if (cache.TryGetValue("maw", out m)) return m;
            var k = new MeshKit(13);
            var body = Palette.Hsv(0.98f, 0.45f, 0.26f);
            k.Box(new Vector3(0, 0.05f, -0.3f), new Vector3(0.92f, 0.86f, 0.6f), body, 0.03f, 0, Palette.Hsv(0.98f, 0.45f, 0.3f));
            k.Box(new Vector3(0, 0.25f, -0.62f), new Vector3(0.7f, 0.3f, 0.08f), Palette.Hsv(0.98f, 0.4f, 0.16f), 0.02f);
            k.Box(new Vector3(0, -0.28f, -0.5f), new Vector3(0.62f, 0.24f, 0.2f), Palette.Hsv(0, 0, 0.03f), 0.01f);
            for (int i = 0; i < 6; i++)
            {
                float x = -0.25f + i * 0.1f;
                k.Spike(new Vector3(x, -0.17f, -0.6f), new Vector3(x, -0.27f, -0.6f), 0.035f, Palette.Bone, 3);
                k.Spike(new Vector3(x + 0.05f, -0.39f, -0.6f), new Vector3(x + 0.05f, -0.3f, -0.6f), 0.035f, Palette.Bone, 3);
            }
            k.Eye(new Vector3(-0.2f, 0.25f, -0.68f), 0.08f, Palette.Hsv(0.1f, 0.5f, 1));
            k.Eye(new Vector3(0.2f, 0.25f, -0.68f), 0.08f, Palette.Hsv(0.1f, 0.5f, 1));
            MaxBossVerts = Mathf.Max(MaxBossVerts, k.Verts);
            m = k.Build("boss-maw");
            cache["maw"] = m;
            return m;
        }

        // ------------------------------------------------------------ hero
        /// <summary>
        /// The hero: a hooded figure in cool, dark tones (V 12–40 %) with one saturated dark block in the hero's colour,
        /// so heroes read apart from foes by hue. Faces +y (its aim) in unit space; eyes do not glow.
        /// </summary>
        public static Mesh Hero(string id, uint color)
        {
            Mesh m;
            if (cache.TryGetValue("hero:" + id, out m)) return m;
            var k = new MeshKit(Seed(id));
            var cloth = Palette.Hsv(0.6f, 0.3f, 0.22f);
            var clothTop = Palette.Hsv(0.6f, 0.28f, 0.3f);
            float h, s, v; Palette.ToHsv(color, out h, out s, out v);
            var mark = Palette.Hsv(h, 0.9f, 0.4f);
            var skin = Palette.Hsv(0.07f, 0.3f, 0.38f);
            k.Lathe(new Vector3(0, 0, 0), Vector3.back, new[]
            {
                new Vector2(0.42f, 0), new Vector2(0.46f, 0.2f), new Vector2(0.36f, 0.55f), new Vector2(0.3f, 0.7f),
            }, 8, cloth, 0.02f, 0, 1, 0.85f, clothTop);
            // head with the coloured hood
            k.Lathe(new Vector3(0, 0.02f, -0.7f), Vector3.back, new[] { new Vector2(0.2f, 0), new Vector2(0.24f, 0.12f), new Vector2(0.16f, 0.26f), new Vector2(0, 0.32f) }, 7, mark, 0.02f);
            k.Box(new Vector3(0, 0.2f, -0.86f), new Vector3(0.22f, 0.12f, 0.1f), skin, 0.01f);
            // launcher arm along +y (aim direction)
            k.Box(new Vector3(0.26f, 0.32f, -0.5f), new Vector3(0.12f, 0.36f, 0.12f), Palette.Steel, 0.01f);
            k.Lathe(new Vector3(0.26f, 0.48f, -0.5f), Vector3.up, new[] { new Vector2(0.08f, 0), new Vector2(0.09f, 0.12f), new Vector2(0.06f, 0.14f) }, 6, Palette.Hsv(0.6f, 0.2f, 0.36f));
            k.Box(new Vector3(-0.3f, 0.05f, -0.45f), new Vector3(0.12f, 0.24f, 0.12f), cloth, 0.01f);
            HeroVerts = k.Verts;
            m = k.Build("hero-" + id);
            cache["hero:" + id] = m;
            return m;
        }

        // ------------------------------------------------------------ scenery (§2): one theme of 8 wall modules
        public static Mesh[] WallModules()
        {
            var list = new Mesh[8];
            for (int i = 0; i < 8; i++)
            {
                Mesh m;
                string key = "wall" + i;
                if (!cache.TryGetValue(key, out m))
                {
                    var k = new MeshKit(100 + i);
                    // a 1×1 footprint stack of stones rising from z = +1.5 (below the floor) to z = -1.2
                    int courses = 3 + i % 2;
                    float z = 1.5f, step = 2.7f / courses;
                    for (int c = 0; c < courses; c++)
                    {
                        var col = c % 2 == 0 ? Palette.Stone : Palette.Hsv(0.69f, 0.24f, 0.2f);
                        if (i == 5 && c == courses - 1) col = Palette.Moss;
                        float off = (c + i) % 2 == 0 ? 0.08f : -0.08f;
                        k.Box(new Vector3(off, 0, z - step / 2), new Vector3(0.96f, 0.96f, step * 0.94f), col, 0.05f, 0,
                            c == courses - 1 ? Palette.Hsv(0.68f, 0.2f, 0.3f) : (Color32?)null);
                        z -= step;
                    }
                    if (i == 2) k.Box(new Vector3(0, 0, -1.3f), new Vector3(0.5f, 0.5f, 0.2f), Palette.StoneDark, 0.06f);
                    if (i == 6) k.Spike(new Vector3(0.1f, 0.1f, -1.2f), new Vector3(0.12f, 0.1f, -1.5f), 0.15f, Palette.StoneDark, 5);
                    m = k.Build("wall-" + i);
                    cache[key] = m;
                }
                list[i] = m;
            }
            return list;
        }

        /// <summary>Floor tiles for a cols × rows grid of cells (1 world unit each), origin at the top-left, y down.</summary>
        public static Mesh Floor(int cols, int rows)
        {
            var k = new MeshKit(cols * 97 + rows);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var col = (x + y) % 2 == 0 ? Palette.Floor : Palette.FloorAlt;
                    k.Box(new Vector3(x + 0.5f, -(y + 0.5f), 0.15f), new Vector3(0.94f, 0.94f, 0.3f), col, 0.025f);
                }
            return k.Build("floor");
        }

        /// <summary>Broken pillars sinking into the fog beyond the walls (props, §2).</summary>
        public static Mesh Abyss(float w, float h)
        {
            var k = new MeshKit(7);
            for (int i = 0; i < 14; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                float x = side < 0 ? -2.5f - i * 0.35f : w + 2.5f + i * 0.35f;
                float y = -(h * (i / 14f)) + 3;
                float zt = 4 + (i * 7) % 26;
                k.Lathe(new Vector3(x, y, zt), Vector3.forward, new[] { new Vector2(0.9f, 0), new Vector2(0.8f, 30) }, 6, Palette.StoneDark, 0.1f, 0, 1, 1, Palette.Stone);
            }
            return k.Build("abyss");
        }
    }
}
