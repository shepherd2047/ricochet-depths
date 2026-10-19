// Immediate-mode 2D drawing: every frame the view code submits shapes, pixel sprites and text, and Gfx turns them into
// one vertex-coloured, atlas-textured mesh per layer plus a pool of TextMesh labels. Coordinates are design units with
// y pointing down (like a canvas).
// The World and Glow layers live under a separate root that Board3D lays onto the 3D board plane, so the tilted
// perspective camera sees them in place on the floor. Their shapes can float above the plane (Z, in world units, negative
// is up off the floor). Glow is drawn over-bright so only it (balls, shots, beams, particles) reaches the bloom threshold.
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public enum Layer { World = 0, WorldText = 1, Hud = 2, HudText = 3, Menu = 4, MenuText = 5, Glow = 6, GlowText = 7 }
    public enum Align { Left, Center, Right }

    public static class Col
    {
        public static Color32 Hex(uint rgb, float a = 1)
        {
            return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255), 0, 255));
        }
        public static Color32 A(Color32 c, float a) { c.a = (byte)Mathf.Clamp(Mathf.RoundToInt(c.a * a), 0, 255); return c; }
        public static Color32 Mix(Color32 a, Color32 b, float t) { return Color32.Lerp(a, b, t); }

        public static readonly Color32 Bg = Hex(0x120f1d), Pit = Hex(0x1c1830), Wall = Hex(0x3a3358), Text = Hex(0xf2efff),
            Dim = Hex(0x8b86a8), Panel = Hex(0x221d38), Panel2 = Hex(0x2d2748), Accent = Hex(0xffb347), Hp = Hex(0xff5d6c),
            Xp = Hex(0x6be3ff), Gold = Hex(0xffd84a), Ink = Hex(0x120f1d), White = Hex(0xffffff), Black = Hex(0x000000);
    }

    public sealed class Gfx
    {
        sealed class Batch
        {
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool World;
            public readonly List<Vector3> V = new List<Vector3>(4096);
            public readonly List<Vector2> U = new List<Vector2>(4096);
            public readonly List<Color32> C = new List<Color32>(4096);
            public readonly List<int> I = new List<int>(8192);
        }

        sealed class Label
        {
            public GameObject Go;
            public TextMesh Tm;
            public MeshRenderer Mr;
            public string LastText;
            public int LastSize = -1;
        }

        const int Layers = 8;
        public const int UiLayer = 5;       // Unity's built-in "UI" layer; only the overlay camera renders it
        public const float GlowBoost = 2.4f; // HDR multiplier of the Glow layer (bloom threshold is about 1)

        readonly Batch[] batches = new Batch[Layers];
        readonly List<Label>[] labels = new List<Label>[Layers];
        readonly int[] labelUsed = new int[Layers];
        readonly Transform root, worldRoot;
        readonly Font font;
        float unitsPerEm = 0.1f;          // TextMesh world height of 1 em at fontSize 1, characterSize 1 (calibrated below)
        bool calibrated;
        TextMesh probe;

        public Layer Cur = Layer.World;
        public float W, H, Scale;           // design size and screen pixels per design unit
        public float Ox, Oy;                // global offset (screen shake)
        public float Time;

        Vector2 uv;

        /// <summary>Height of World/Glow shapes above the board plane, in world units (negative is up off the floor).</summary>
        public float Z;

        bool IsWorld(int layer) { return (layer & ~1) == 0 || (layer & ~1) == 6; }

        public Gfx(Transform uiParent, Transform worldParent = null)
        {
            root = uiParent;
            worldRoot = worldParent != null ? worldParent : uiParent;
            PixelArt.Build();
            uv = PixelArt.WhiteUv;
            var shader = Resources.Load<Shader>("PitShape");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            for (int i = 0; i < Layers; i++)
            {
                labels[i] = new List<Label>();
                if (i % 2 == 1) continue;   // odd layers hold text only
                bool world = IsWorld(i);
                var mat = new Material(shader);
                mat.SetTexture("_MainTex", PixelArt.Atlas);
                mat.SetFloat("_Intensity", i == (int)Layer.Glow ? GlowBoost : 1);
                // world shapes are depth-tested against the 3D units; UI always draws on top
                mat.SetFloat("_ZTest", world ? 4 : 8);   // CompareFunction.LessEqual : Always
                var go = new GameObject("shapes-" + (Layer)i);
                go.transform.SetParent(world ? worldRoot : root, false);
                go.layer = world ? 0 : UiLayer;
                var b = new Batch { World = world };
                b.Mesh = new Mesh { name = "pit-shapes-" + i };
                b.Mesh.MarkDynamic();
                b.Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
                b.Renderer = go.AddComponent<MeshRenderer>();
                b.Renderer.sharedMaterial = mat;
                b.Renderer.sortingOrder = i * 10;
                batches[i] = b;
            }
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var pg = new GameObject("text-probe");
            pg.transform.SetParent(root, false);
            pg.transform.position = new Vector3(-10000, 0, 0);
            probe = pg.AddComponent<TextMesh>();
            probe.font = font; probe.fontSize = 100; probe.characterSize = 1; probe.text = "Hg";
            pg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ------------------------------------------------------------ frame
        public void Begin(float w, float h, float scale)
        {
            W = w; H = h; Scale = scale;
            Z = 0;
            for (int i = 0; i < Layers; i++)
            {
                labelUsed[i] = 0;
                var b = batches[i];
                if (b == null) continue;
                b.V.Clear(); b.U.Clear(); b.C.Clear(); b.I.Clear();
            }
            if (!calibrated && probe != null)
            {
                float hb = probe.GetComponent<MeshRenderer>().bounds.size.y;
                if (hb > 0.01f)
                {
                    // "Hg" spans about 0.95 em in most sans fonts
                    unitsPerEm = hb / 0.95f / 100f;
                    calibrated = true;
                    Object.Destroy(probe.gameObject); probe = null;
                }
            }
        }

        public void End()
        {
            for (int i = 0; i < Layers; i++)
            {
                var b = batches[i];
                if (b != null)
                {
                    b.Mesh.Clear();
                    b.Mesh.SetVertices(b.V);
                    b.Mesh.SetUVs(0, b.U);
                    b.Mesh.SetColors(b.C);
                    b.Mesh.SetTriangles(b.I, 0, false);
                    b.Mesh.bounds = new Bounds(new Vector3(W / 2, -H / 2, 0), new Vector3(W * 4 + 100, H * 4 + 100, 10));
                }
                var ls = labels[i];
                for (int k = labelUsed[i]; k < ls.Count; k++) if (ls[k].Go.activeSelf) ls[k].Go.SetActive(false);
            }
        }

        Batch B { get { return batches[(int)Cur & ~1]; } }

        /// <summary>Test hook: the current frame's shape batches (layer, vertices, uvs, colours, indices).</summary>
        internal void DebugBatches(System.Action<int, List<Vector3>, List<Vector2>, List<Color32>, List<int>> f)
        {
            for (int i = 0; i < Layers; i += 2) if (batches[i] != null) f(i, batches[i].V, batches[i].U, batches[i].C, batches[i].I);
        }

        // ------------------------------------------------------------ primitives
        int Vert(Batch b, float x, float y, Color32 c)
        {
            b.V.Add(new Vector3(x + Ox, -(y + Oy), b.World ? Z : 0));
            b.U.Add(uv);
            b.C.Add(c);
            return b.V.Count - 1;
        }

        // ------------------------------------------------------------ pixel sprites
        /// <summary>An atlas sprite (PixelArt cell) centred on (x, y), size design units across, rotated by angle radians.</summary>
        public void Sprite(int cell, float x, float y, float size, Color32 tint, float angle = 0)
        {
            if (cell < 0 || tint.a == 0 || size <= 0) return;
            var r = PixelArt.Uv(cell);
            var b = B;
            float h = size / 2, cs = Mathf.Cos(angle) * h, sn = Mathf.Sin(angle) * h;
            // corners: top-left, top-right, bottom-right, bottom-left (y down)
            uv = new Vector2(r.x, r.w); int i = Vert(b, x - cs + sn, y - sn - cs, tint);
            uv = new Vector2(r.z, r.w); Vert(b, x + cs + sn, y + sn - cs, tint);
            uv = new Vector2(r.z, r.y); Vert(b, x + cs - sn, y + sn + cs, tint);
            uv = new Vector2(r.x, r.y); Vert(b, x - cs - sn, y - sn + cs, tint);
            uv = PixelArt.WhiteUv;
            b.I.Add(i); b.I.Add(i + 1); b.I.Add(i + 2);
            b.I.Add(i); b.I.Add(i + 2); b.I.Add(i + 3);
        }

        public void Sprite(string key, float x, float y, float size, Color32 tint, float angle = 0)
        {
            Sprite(PixelArt.CellOf(key), x, y, size, tint, angle);
        }

        /// <summary>
        /// Text in the 3×5 pixel font, drawn as quads on the current layer. px is the cap height in design units;
        /// pixels snap to whole screen pixels so the font stays crisp.
        /// </summary>
        public void PixelText(string s, float x, float y, float px, Color32 c, Align al = Align.Center, bool shadow = true)
        {
            if (string.IsNullOrEmpty(s) || c.a == 0) return;
            float p = px / 5;
            if (Scale > 0) p = Mathf.Max(1, Mathf.Round(p * Scale)) / Scale;
            float w = PixelWidth(s, p * 5);
            float x0 = al == Align.Left ? x : al == Align.Right ? x - w : x - w / 2, y0 = y - p * 2.5f;
            if (Scale > 0) { x0 = Mathf.Round(x0 * Scale) / Scale; y0 = Mathf.Round(y0 * Scale) / Scale; }
            if (shadow) PixelRun(s, x0 + p, y0 + p, p, Col.A(Col.Black, c.a / 255f * 0.75f));
            PixelRun(s, x0, y0, p, c);
        }

        public float PixelWidth(string s, float px)
        {
            float p = px / 5;
            return string.IsNullOrEmpty(s) ? 0 : (s.Length * 4 - 1) * p;
        }

        void PixelRun(string s, float x, float y, float p, Color32 c)
        {
            for (int k = 0; k < s.Length; k++, x += p * 4)
            {
                var gl = PixelArt.FontGlyph(s[k]);
                if (gl == null) continue;
                for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        if (gl[row * 3 + col] != '#') continue;
                        // merge horizontal runs into one quad
                        int run = 1;
                        while (col + run < 3 && gl[row * 3 + col + run] == '#') run++;
                        Rect(x + col * p, y + row * p, p * run, p, c);
                        col += run - 1;
                    }
            }
        }

        public void Tri(float x1, float y1, float x2, float y2, float x3, float y3, Color32 c)
        {
            if (c.a == 0) return;
            var b = B;
            int i = Vert(b, x1, y1, c); Vert(b, x2, y2, c); Vert(b, x3, y3, c);
            b.I.Add(i); b.I.Add(i + 1); b.I.Add(i + 2);
        }

        public void Quad(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4, Color32 c)
        {
            if (c.a == 0) return;
            var b = B;
            int i = Vert(b, x1, y1, c); Vert(b, x2, y2, c); Vert(b, x3, y3, c); Vert(b, x4, y4, c);
            b.I.Add(i); b.I.Add(i + 1); b.I.Add(i + 2);
            b.I.Add(i); b.I.Add(i + 2); b.I.Add(i + 3);
        }

        public void Rect(float x, float y, float w, float h, Color32 c)
        {
            if (w <= 0 || h <= 0) return;
            Quad(x, y, x + w, y, x + w, y + h, x, y + h, c);
        }

        public void RectGrad(float x, float y, float w, float h, Color32 top, Color32 bottom)
        {
            if (w <= 0 || h <= 0) return;
            var b = B;
            int i = Vert(b, x, y, top); Vert(b, x + w, y, top); Vert(b, x + w, y + h, bottom); Vert(b, x, y + h, bottom);
            b.I.Add(i); b.I.Add(i + 1); b.I.Add(i + 2);
            b.I.Add(i); b.I.Add(i + 2); b.I.Add(i + 3);
        }

        public void Line(float x1, float y1, float x2, float y2, float w, Color32 c)
        {
            float dx = x2 - x1, dy = y2 - y1, l = Mathf.Sqrt(dx * dx + dy * dy);
            if (l < 1e-4f) return;
            float nx = -dy / l * w / 2, ny = dx / l * w / 2;
            Quad(x1 + nx, y1 + ny, x2 + nx, y2 + ny, x2 - nx, y2 - ny, x1 - nx, y1 - ny, c);
        }

        public void Dashed(float x1, float y1, float x2, float y2, float w, float dash, float gap, Color32 c)
        {
            float dx = x2 - x1, dy = y2 - y1, l = Mathf.Sqrt(dx * dx + dy * dy);
            if (l < 1e-4f) return;
            float ux = dx / l, uy = dy / l;
            for (float s = 0; s < l; s += dash + gap)
            {
                float e = Mathf.Min(l, s + dash);
                Line(x1 + ux * s, y1 + uy * s, x1 + ux * e, y1 + uy * e, w, c);
            }
        }

        int Segs(float r) { return Mathf.Clamp(Mathf.CeilToInt(r * Scale * 0.6f), 8, 48); }

        public void Circle(float x, float y, float r, Color32 c) { Pie(x, y, r, 0, Mathf.PI * 2, c); }

        /// <summary>Filled wedge from angle a0 to a1 (radians, 0 = right, counter-clockwise on screen).</summary>
        public void Pie(float x, float y, float r, float a0, float a1, Color32 c)
        {
            if (r <= 0 || c.a == 0) return;
            var b = B;
            int n = Mathf.Max(3, Mathf.CeilToInt(Segs(r) * Mathf.Abs(a1 - a0) / (Mathf.PI * 2)));
            int ci = Vert(b, x, y, c);
            for (int k = 0; k <= n; k++)
            {
                float a = a0 + (a1 - a0) * k / n;
                Vert(b, x + Mathf.Cos(a) * r, y - Mathf.Sin(a) * r, c);
                if (k > 0) { b.I.Add(ci); b.I.Add(ci + k); b.I.Add(ci + k + 1); }
            }
        }

        public void Ring(float x, float y, float r, float w, Color32 c) { Arc(x, y, r, w, 0, Mathf.PI * 2, c); }

        public void Arc(float x, float y, float r, float w, float a0, float a1, Color32 c)
        {
            if (r <= 0 || c.a == 0) return;
            var b = B;
            float ro = r + w / 2, ri = Mathf.Max(0, r - w / 2);
            int n = Mathf.Max(3, Mathf.CeilToInt(Segs(r) * Mathf.Abs(a1 - a0) / (Mathf.PI * 2)));
            int s = b.V.Count;
            for (int k = 0; k <= n; k++)
            {
                float a = a0 + (a1 - a0) * k / n, cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                Vert(b, x + cs * ro, y - sn * ro, c);
                Vert(b, x + cs * ri, y - sn * ri, c);
                if (k > 0)
                {
                    int p = s + (k - 1) * 2;
                    b.I.Add(p); b.I.Add(p + 2); b.I.Add(p + 1);
                    b.I.Add(p + 1); b.I.Add(p + 2); b.I.Add(p + 3);
                }
            }
        }

        public void Ellipse(float x, float y, float rx, float ry, Color32 c)
        {
            if (rx <= 0 || ry <= 0 || c.a == 0) return;
            var b = B;
            int n = Segs(Mathf.Max(rx, ry));
            int ci = Vert(b, x, y, c);
            for (int k = 0; k <= n; k++)
            {
                float a = Mathf.PI * 2 * k / n;
                Vert(b, x + Mathf.Cos(a) * rx, y + Mathf.Sin(a) * ry, c);
                if (k > 0) { b.I.Add(ci); b.I.Add(ci + k); b.I.Add(ci + k + 1); }
            }
        }

        readonly List<Vector2> path = new List<Vector2>(64);

        void RoundPath(float x, float y, float w, float h, float r)
        {
            path.Clear();
            r = Mathf.Max(0, Mathf.Min(r, w / 2, h / 2));
            int n = r * Scale < 2 ? 0 : Mathf.Clamp(Mathf.CeilToInt(r * Scale / 3), 2, 8);
            Corner(x + w - r, y + r, r, 0, n);          // top right, going counter-clockwise on screen
            Corner(x + r, y + r, r, 1, n);              // top left
            Corner(x + r, y + h - r, r, 2, n);          // bottom left
            Corner(x + w - r, y + h - r, r, 3, n);      // bottom right
        }

        void Corner(float cx, float cy, float r, int q, int n)
        {
            if (n == 0) { path.Add(new Vector2(cx + (q == 0 || q == 3 ? r : -r), cy + (q < 2 ? -r : r))); return; }
            for (int k = 0; k <= n; k++)
            {
                float a = (q + (float)k / n) * Mathf.PI / 2;
                path.Add(new Vector2(cx + Mathf.Cos(a) * r, cy - Mathf.Sin(a) * r));
            }
        }

        public void RoundRect(float x, float y, float w, float h, float r, Color32 c)
        {
            if (w <= 0 || h <= 0 || c.a == 0) return;
            RoundPath(x, y, w, h, r);
            FillPath(x + w / 2, y + h / 2, c);
        }

        public void RoundRectStroke(float x, float y, float w, float h, float r, float lw, Color32 c)
        {
            if (w <= 0 || h <= 0 || c.a == 0) return;
            RoundPath(x, y, w, h, r);
            var outer = new List<Vector2>(path);
            RoundPath(x + lw, y + lw, w - 2 * lw, h - 2 * lw, Mathf.Max(0, r - lw));
            if (path.Count != outer.Count) { RoundPath(x + lw, y + lw, w - 2 * lw, h - 2 * lw, r); }
            if (path.Count != outer.Count)
            {
                // corner resolution differs; fall back to four bars
                Rect(x, y, w, lw, c); Rect(x, y + h - lw, w, lw, c); Rect(x, y + lw, lw, h - 2 * lw, c); Rect(x + w - lw, y + lw, lw, h - 2 * lw, c);
                return;
            }
            var b = B;
            int s = b.V.Count, n = outer.Count;
            for (int k = 0; k < n; k++) { Vert(b, outer[k].x, outer[k].y, c); Vert(b, path[k].x, path[k].y, c); }
            for (int k = 0; k < n; k++)
            {
                int p = s + k * 2, q = s + ((k + 1) % n) * 2;
                b.I.Add(p); b.I.Add(q); b.I.Add(p + 1);
                b.I.Add(p + 1); b.I.Add(q); b.I.Add(q + 1);
            }
        }

        /// <summary>Convex polygon from a list of points (fan from its first point).</summary>
        public void Poly(IList<Vector2> pts, Color32 c)
        {
            if (pts.Count < 3 || c.a == 0) return;
            var b = B;
            int s = b.V.Count;
            for (int k = 0; k < pts.Count; k++) Vert(b, pts[k].x, pts[k].y, c);
            for (int k = 1; k < pts.Count - 1; k++) { b.I.Add(s); b.I.Add(s + k); b.I.Add(s + k + 1); }
        }

        void FillPath(float cx, float cy, Color32 c)
        {
            var b = B;
            int ci = Vert(b, cx, cy, c), n = path.Count;
            for (int k = 0; k < n; k++) Vert(b, path[k].x, path[k].y, c);
            for (int k = 0; k < n; k++) { b.I.Add(ci); b.I.Add(ci + 1 + k); b.I.Add(ci + 1 + (k + 1) % n); }
        }

        // ------------------------------------------------------------ text
        Layer TextLayer { get { return (Layer)(((int)Cur & ~1) + 1); } }

        public void Text(string s, float x, float y, float px, Color32 c, Align al = Align.Center, bool bold = true)
        {
            if (string.IsNullOrEmpty(s) || c.a == 0 || font == null) return;
            int li = (int)TextLayer;
            var ls = labels[li];
            Label l;
            if (labelUsed[li] < ls.Count) l = ls[labelUsed[li]];
            else
            {
                l = new Label();
                l.Go = new GameObject("label");
                l.Go.transform.SetParent(IsWorld(li) ? worldRoot : root, false);
                l.Go.layer = IsWorld(li) ? 0 : UiLayer;
                l.Tm = l.Go.AddComponent<TextMesh>();
                l.Tm.font = font;
                l.Mr = l.Go.GetComponent<MeshRenderer>();
                l.Mr.sharedMaterial = font.material;
                l.Mr.sortingOrder = li * 10;
                ls.Add(l);
            }
            labelUsed[li]++;
            if (!l.Go.activeSelf) l.Go.SetActive(true);
            int size = Mathf.Clamp(Mathf.RoundToInt(px * Scale), 6, 160);
            if (l.LastSize != size) { l.Tm.fontSize = size; l.LastSize = size; }
            l.Tm.characterSize = px / (size * unitsPerEm * 1f);
            l.Tm.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            if (l.LastText != s) { l.Tm.text = s; l.LastText = s; }
            l.Tm.color = c;
            l.Tm.anchor = al == Align.Left ? TextAnchor.MiddleLeft : al == Align.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            l.Tm.alignment = al == Align.Left ? TextAlignment.Left : al == Align.Right ? TextAlignment.Right : TextAlignment.Center;
            l.Go.transform.localPosition = new Vector3(x + Ox, -(y + Oy), IsWorld(li) ? Z : 0);
        }

        /// <summary>Text with a dark offset copy behind it, for numbers over busy backgrounds.</summary>
        public void TextOutlined(string s, float x, float y, float px, Color32 c, Align al = Align.Center)
        {
            var sh = Col.A(Col.Black, c.a / 255f * 0.7f);
            Text(s, x + 1, y + 1, px, sh, al);
            Text(s, x, y, px, c, al);
        }

        public float Measure(string s, float px, bool bold = true)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (font == null || !font.dynamic) return s.Length * px * 0.55f;
            int size = 32;
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            font.RequestCharactersInTexture(s, size, style);
            float adv = 0;
            CharacterInfo ci;
            foreach (char ch in s) adv += font.GetCharacterInfo(ch, out ci, size, style) ? ci.advance : size * 0.55f;
            return adv / size * px;
        }

        public List<string> Wrap(string text, float px, float maxW, bool bold = false)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            var words = text.Split(' ');
            string cur = "";
            foreach (var w in words)
            {
                string t = cur.Length > 0 ? cur + " " + w : w;
                if (cur.Length > 0 && Measure(t, px, bold) > maxW) { lines.Add(cur); cur = w; }
                else cur = t;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines;
        }
    }
}
