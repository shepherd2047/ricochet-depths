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
        readonly Batch[] skins = new Batch[Layers];   // pit-ui atlas quads; Hud and Menu only, drawn under that layer's shapes
        MeshRenderer picture;                          // one textured quad (the hero preview), between Menu skin and shapes
        Mesh pictureMesh;
        bool pictureUsed;
        readonly List<Label>[] labels = new List<Label>[Layers];
        readonly int[] labelUsed = new int[Layers];
        readonly Transform root, worldRoot;
        readonly Font font, fontBold;
        float unitsPerEm = 0.1f;          // TextMesh world size of one font texel at characterSize 1 (calibrated below)
        bool calibrated;
        // Unity's dynamic-font size is not the TMP font size the pit-ui tables use: measured against pit-ui/mockup/01.png
        // ("RICOCHET", 46 pt Bold: 151 of 360 px wide, cap height 21 px), text has to be rasterised this much larger
        const float FontFit = 1.735f;
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
            // pit-ui skin: same shader, the ui atlas instead of the procedural one
            UiSkin.Load();
            if (UiSkin.Loaded)
                foreach (int i in new[] { (int)Layer.Hud, (int)Layer.Menu })
                {
                    var mat = new Material(shader);
                    mat.SetTexture("_MainTex", UiSkin.Atlas);
                    mat.SetFloat("_Intensity", 1);
                    mat.SetFloat("_ZTest", 8);
                    var go = new GameObject("skin-" + (Layer)i);
                    go.transform.SetParent(root, false);
                    go.layer = UiLayer;
                    var b = new Batch();
                    b.Mesh = new Mesh { name = "pit-skin-" + i };
                    b.Mesh.MarkDynamic();
                    b.Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
                    b.Renderer = go.AddComponent<MeshRenderer>();
                    b.Renderer.sharedMaterial = mat;
                    b.Renderer.sortingOrder = i * 10 - 3;
                    skins[i] = b;
                }
            {
                var go = new GameObject("picture");
                go.transform.SetParent(root, false);
                go.layer = UiLayer;
                pictureMesh = new Mesh { name = "pit-picture" };
                pictureMesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = pictureMesh;
                picture = go.AddComponent<MeshRenderer>();
                var mat = new Material(shader);
                mat.SetFloat("_Intensity", 1);
                mat.SetFloat("_ZTest", 8);
                picture.sharedMaterial = mat;
                picture.sortingOrder = (int)Layer.Menu * 10 - 2;
                go.SetActive(false);
            }
            // Pixelify Sans (OFL, pit-ui/assets/fonts); the built-in font only if it is missing
            font = Resources.Load<Font>("UI/PixelifySans-Regular");
            fontBold = Resources.Load<Font>("UI/PixelifySans-Bold");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (fontBold == null) fontBold = font;
            var pg = new GameObject("text-probe");
            pg.transform.SetParent(root, false);
            pg.transform.position = new Vector3(-10000, 0, 0);
            probe = pg.AddComponent<TextMesh>();
            probe.font = font; probe.fontSize = 100; probe.characterSize = 1; probe.text = "H";
            pg.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ------------------------------------------------------------ frame
        public void Begin(float w, float h, float scale)
        {
            W = w; H = h; Scale = scale;
            Z = 0;
            pictureUsed = false;
            for (int i = 0; i < Layers; i++)
            {
                labelUsed[i] = 0;
                foreach (var b in new[] { batches[i], skins[i] })
                {
                    if (b == null) continue;
                    b.V.Clear(); b.U.Clear(); b.C.Clear(); b.I.Clear();
                }
            }
            // glyphs are rasterised at screen size and drawn 1 texel per pixel, so bilinear stays sharp; point filtering
            // dropped columns whenever a label landed on a half pixel (C read as O, 5 as S)
            if (font != null && font.material != null && font.material.mainTexture != null) font.material.mainTexture.filterMode = FilterMode.Bilinear;
            if (fontBold != null && fontBold.material != null && fontBold.material.mainTexture != null) fontBold.material.mainTexture.filterMode = FilterMode.Bilinear;
            if (!calibrated && probe != null)
            {
                float hb = probe.GetComponent<MeshRenderer>().bounds.size.y;
                if (hb > 0.01f)
                {
                    CharacterInfo ci;
                    font.RequestCharactersInTexture("H", 100, FontStyle.Normal);
                    if (font.GetCharacterInfo('H', out ci, 100, FontStyle.Normal) && ci.glyphHeight > 0) unitsPerEm = hb / ci.glyphHeight;
                    else unitsPerEm = hb / 0.7f / 100f;   // cap height is about 0.7 em
                    calibrated = true;
                    Object.Destroy(probe.gameObject); probe = null;
                }
            }
        }

        public void End()
        {
            if (picture.gameObject.activeSelf != pictureUsed) picture.gameObject.SetActive(pictureUsed);
            for (int i = 0; i < Layers; i++)
            {
                var sk = skins[i];
                if (sk != null)
                {
                    sk.Mesh.Clear();
                    sk.Mesh.SetVertices(sk.V);
                    sk.Mesh.SetUVs(0, sk.U);
                    sk.Mesh.SetColors(sk.C);
                    sk.Mesh.SetTriangles(sk.I, 0, false);
                    sk.Mesh.bounds = new Bounds(new Vector3(W / 2, -H / 2, 0), new Vector3(W * 4 + 100, H * 4 + 100, 10));
                }
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
            for (int i = 0; i < Layers; i += 2) if (skins[i] != null) f(100 + i, skins[i].V, skins[i].U, skins[i].C, skins[i].I);
        }

        // ------------------------------------------------------------ pit-ui skin (atlas sprites, 1 art px = 2 design units)
        public const float Art = 2;

        Batch SkinBatch { get { int i = (int)Cur & ~1; return skins[i] ?? skins[(int)Layer.Hud]; } }

        void SkinQuad(Batch b, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, Color32 c)
        {
            if (x1 <= x0 || y1 <= y0) return;
            int i = b.V.Count;
            b.V.Add(new Vector3(x0 + Ox, -(y0 + Oy), 0)); b.U.Add(new Vector2(u0, v1)); b.C.Add(c);
            b.V.Add(new Vector3(x1 + Ox, -(y0 + Oy), 0)); b.U.Add(new Vector2(u1, v1)); b.C.Add(c);
            b.V.Add(new Vector3(x1 + Ox, -(y1 + Oy), 0)); b.U.Add(new Vector2(u1, v0)); b.C.Add(c);
            b.V.Add(new Vector3(x0 + Ox, -(y1 + Oy), 0)); b.U.Add(new Vector2(u0, v0)); b.C.Add(c);
            b.I.Add(i); b.I.Add(i + 1); b.I.Add(i + 2);
            b.I.Add(i); b.I.Add(i + 2); b.I.Add(i + 3);
        }

        /// <summary>
        /// An atlas sprite in the rect (design units, y down). Sprites with borders are 9-sliced, the borders drawn at
        /// 1 texel per art px; the others stretch. False if the sprite is missing (callers then fall back).
        /// </summary>
        public bool Skin(string name, float x, float y, float w, float h, Color32 tint)
        {
            var s = UiSkin.Get(name);
            if (s == null || tint.a == 0 || w <= 0 || h <= 0) return s != null;
            var b = SkinBatch;
            if (b == null) return false;
            if (s.L + s.R + s.T + s.B == 0) { SkinQuad(b, x, y, x + w, y + h, s.U0, s.V0, s.U1, s.V1, tint); return true; }
            float du = (s.U1 - s.U0) / s.W, dv = (s.V1 - s.V0) / s.H;
            float l = Mathf.Min(s.L * Art, w / 2), r = Mathf.Min(s.R * Art, w / 2), t = Mathf.Min(s.T * Art, h / 2), bo = Mathf.Min(s.B * Art, h / 2);
            float[] xs = { x, x + l, x + w - r, x + w }, ys = { y, y + t, y + h - bo, y + h };
            float[] us = { s.U0, s.U0 + s.L * du, s.U1 - s.R * du, s.U1 }, vs = { s.V1, s.V1 - s.T * dv, s.V0 + s.B * dv, s.V0 };
            for (int j = 0; j < 3; j++)
                for (int i = 0; i < 3; i++)
                    SkinQuad(b, xs[i], ys[j], xs[i + 1], ys[j + 1], us[i], vs[j + 1], us[i + 1], vs[j], tint);
            return true;
        }

        /// <summary>A sprite at its own size times mult (art px), top-left at (x, y).</summary>
        public bool SkinAt(string name, float x, float y, float mult, Color32 tint)
        {
            var s = UiSkin.Get(name);
            if (s == null) return false;
            return Skin(name, x, y, s.W * Art * mult, s.H * Art * mult, tint);
        }

        /// <summary>Repeats a sprite across the rect at its own size (the boss banner stripes); scroll is in art px.</summary>
        public void SkinTiled(string name, float x, float y, float w, float h, float scroll, Color32 tint)
        {
            var s = UiSkin.Get(name);
            if (s == null || w <= 0) return;
            var b = SkinBatch;
            float tw = s.W * Art, off = Mathf.Repeat(scroll * Art, tw);
            for (float px = x - off; px < x + w; px += tw)
            {
                float a = Mathf.Max(px, x), e = Mathf.Min(px + tw, x + w);
                float ua = s.U0 + (s.U1 - s.U0) * (a - px) / tw, ue = s.U0 + (s.U1 - s.U0) * (e - px) / tw;
                SkinQuad(b, a, y, e, y + h, ua, s.V0, ue, s.V1, tint);
            }
        }

        static string GlyphName(char ch)
        {
            switch (ch)
            {
                case '+': return "plus"; case '-': return "minus"; case '/': return "slash"; case 'x': return "x";
                case '.': return "dot"; case '!': return "bang"; case ':': return "colon"; case '%': return "pct";
                case 'k': return "k"; case ' ': return "space";
                default: return ch >= '0' && ch <= '9' ? ch.ToString() : null;
            }
        }

        /// <summary>Width in design units of a string in a pit-ui digit font (hud, dmg or crit) at mult.</summary>
        public float DigitsWidth(string font, string s, float mult = 1)
        {
            float w = 0; UiSkin.Spr last = null;
            foreach (char ch in s)
            {
                var g = GlyphName(ch); var sp = g != null ? UiSkin.Get("font_" + font + "_" + g) : null;
                if (sp == null) { w += 4; continue; }
                w += sp.Advance; last = sp;
            }
            if (last != null) w += last.W - last.Advance;   // the last glyph's full width, not its advance
            return w * Art * mult;
        }

        /// <summary>
        /// A string in a pit-ui bitmap digit font; (x, y) is the anchor (left, centre or right edge; y the glyph top),
        /// snapped to whole art px. Unknown characters leave a gap.
        /// </summary>
        public void Digits(string font, string s, float x, float y, float mult, Color32 tint, Align al = Align.Left)
        {
            if (string.IsNullOrEmpty(s) || tint.a == 0) return;
            float w = DigitsWidth(font, s, mult), u = Art * mult;
            float x0 = al == Align.Left ? x : al == Align.Right ? x - w : x - w / 2;
            float snap = Scale > 0 ? Scale * Art : 1;
            x0 = Mathf.Round(x0 * snap) / snap; y = Mathf.Round(y * snap) / snap;
            foreach (char ch in s)
            {
                var g = GlyphName(ch); var sp = g != null ? UiSkin.Get("font_" + font + "_" + g) : null;
                if (sp == null) { x0 += 4 * u; continue; }
                Skin(sp.Name, x0, y, sp.W * u, sp.H * u, tint);
                x0 += sp.Advance * u;
            }
        }

        /// <summary>One texture in a rect over the Menu skin (the hero preview RenderTexture). One per frame.</summary>
        public void Picture(Texture tex, float x, float y, float w, float h)
        {
            if (tex == null) return;
            pictureUsed = true;
            picture.sharedMaterial.SetTexture("_MainTex", tex);
            pictureMesh.Clear();
            pictureMesh.vertices = new[] { new Vector3(x, -y, 0), new Vector3(x + w, -y, 0), new Vector3(x + w, -(y + h), 0), new Vector3(x, -(y + h), 0) };
            pictureMesh.uv = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            pictureMesh.colors32 = new[] { Col.White, Col.White, Col.White, Col.White };
            pictureMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            pictureMesh.bounds = new Bounds(new Vector3(W / 2, -H / 2, 0), new Vector3(W * 4 + 100, H * 4 + 100, 10));
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

        /// <summary>Pixelify text with the pit-ui hard ink shadow, 1 art px straight down (UI-SPEC §6.1).</summary>
        public void TextS(string s, float x, float y, float px, Color32 c, Align al = Align.Center, bool bold = true)
        {
            if (string.IsNullOrEmpty(s) || c.a == 0) return;
            Text(s, x, y + Art, px, Col.A(Ink, c.a / 255f), al, bold, 0);
            Text(s, x, y, px, c, al, bold, 1);
        }

        public static readonly Color32 Ink = Col.Hex(0x0b0f11);

        public void Text(string s, float x, float y, float px, Color32 c, Align al = Align.Center, bool bold = true) { Text(s, x, y, px, c, al, bold, 1); }

        void Text(string s, float x, float y, float px, Color32 c, Align al, bool bold, int order)
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
                l.Mr = l.Go.GetComponent<MeshRenderer>();
                ls.Add(l);
            }
            var f = bold ? fontBold : font;
            if (l.Tm.font != f) { l.Tm.font = f; l.Mr.sharedMaterial = f.material; l.LastSize = -1; l.LastText = null; }
            l.Mr.sortingOrder = li * 10 + order;   // shadows (0) under their text (1)
            labelUsed[li]++;
            if (!l.Go.activeSelf) l.Go.SetActive(true);
            int size = Mathf.Clamp(Mathf.RoundToInt(px * Scale * FontFit), 6, 300);
            if (l.LastSize != size) { l.Tm.fontSize = size; l.LastSize = size; }
            l.Tm.characterSize = Scale > 0 ? 1f / (Scale * unitsPerEm) : px * FontFit / (size * unitsPerEm);
            l.Tm.fontStyle = FontStyle.Normal;   // weight comes from the font file
            if (l.LastText != s) { l.Tm.text = s; l.LastText = s; }
            l.Tm.color = c;
            l.Tm.anchor = al == Align.Left ? TextAnchor.MiddleLeft : al == Align.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
            l.Tm.alignment = al == Align.Left ? TextAlignment.Left : al == Align.Right ? TextAlignment.Right : TextAlignment.Center;
            float lx = x + Ox, ly = y + Oy;
            if (!IsWorld(li) && Scale > 0) { lx = Mathf.Round(lx * Scale) / Scale; ly = Mathf.Round(ly * Scale) / Scale; }
            l.Go.transform.localPosition = new Vector3(lx, -ly, IsWorld(li) ? Z : 0);
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
            var f = bold ? fontBold : font;
            if (f == null || !f.dynamic) return s.Length * px * 0.55f;
            int size = 32;
            f.RequestCharactersInTexture(s, size, FontStyle.Normal);
            float adv = 0;
            CharacterInfo ci;
            foreach (char ch in s) adv += f.GetCharacterInfo(ch, out ci, size, FontStyle.Normal) ? ci.advance : size * 0.55f;
            return adv / size * px;   // advances already match the rendered size (checked in the Editor captures)
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
