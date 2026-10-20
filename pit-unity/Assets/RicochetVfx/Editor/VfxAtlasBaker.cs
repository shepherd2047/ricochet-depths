// Bakes RicochetVfxAtlas.png (256×256, point sampled, uncompressed RGBA32 = 256 KB in memory): every VFX sprite as a
// grey mask (r = brightness that indexes the ramp, a = coverage) plus the four 64×2 element ramps. Everything is drawn
// procedurally here, pixel by pixel, so the art is original and repeatable.
//
// Layout, 16×16 px cells, (col, row) with row 0 at the top:
//   row 0  : balls Ember (0), Rime (1), Spark (2), Ray (3); halo 32×32 at cols 8–9, rows 0–1
//   row 2  : flame flipbook (0–3), spark dot (4), soft ember blob (5)
//   row 3  : ice shard A (0), ice shard B (1), snowflake A (2), snowflake B (3), frost ring (4), sparkle (5)
//   row 4  : electric dot (0), electric glyph flipbook (1–4), star flash (5)
//   row 5  : lens glint (0), thin ring (1), diamond (2), laser flare flipbook (3–5)
//   row 6  : hit flash flipbook (0–2)
//   bottom : ramps at pixel rows y = 0–1 (texture space): Ember x 0–63, Rime 64–127, Spark 128–191, Ray 192–255
using System;
using UnityEditor;
using UnityEngine;

namespace RicochetVfx.EditorTools
{
    public static class VfxAtlasBaker
    {
        public const int Size = 256, Cell = 16;
        public const string Path = "Assets/RicochetVfx/Textures/RicochetVfxAtlas.png";

        // atlas rows/cols used by the prefab builder
        public const int RowBalls = 0, RowFire = 2, RowIce = 3, RowZap = 4, RowRay = 5, RowFlash = 6;

        static Color32[] px;

        // ramp stops (t, colour); dark end very dark, top near white (style.md §5). Mid stops are the Content.cs colours.
        static readonly (float, uint)[][] Ramps =
        {
            new[] { (0f, 0x120402u), (0.15f, 0x380E06u), (0.42f, 0xBF581Du), (0.62f, 0xFF7B39u), (0.84f, 0xFFE699u), (1f, 0xFFFBEAu) },
            new[] { (0f, 0x04101Au), (0.15f, 0x0A2533u), (0.42f, 0x47AAB2u), (0.62f, 0x7FD8FFu), (0.84f, 0xD9FFF9u), (1f, 0xF6FFFFu) },
            new[] { (0f, 0x140E04u), (0.18f, 0x3A2A0Cu), (0.42f, 0xB88A2Au), (0.66f, 0xFFE14Du), (0.86f, 0xFFFBCCu), (1f, 0xFFFFF4u) },
            new[] { (0f, 0x1A0616u), (0.18f, 0x4A1446u), (0.44f, 0xB04AA6u), (0.66f, 0xFF9DF0u), (0.86f, 0xFFE3FAu), (1f, 0xFFFFFFu) },
        };

        [MenuItem("Ricochet VFX/Bake Atlas")]
        public static void Bake()
        {
            px = new Color32[Size * Size];
            DrawRamps();
            DrawBalls();
            DrawHalo(128, 0);
            DrawFire();
            DrawIce();
            DrawZap();
            DrawRay();
            DrawFlash();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(Path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(Path);
            ti.textureType = TextureImporterType.Default;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = Size;
            ti.sRGBTexture = true;
            ti.SaveAndReimport();
        }

        // ------------------------------------------------------------------ pixel helpers (top-down coordinates)

        static void P(int ax, int ay, float g, float a = 1)
        {
            if (ax < 0 || ax >= Size || ay < 0 || ay >= Size) return;
            byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(g) * 255);
            px[(Size - 1 - ay) * Size + ax] = new Color32(v, v, v, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255));
        }

        static float G(int ax, int ay)
        {
            var c = px[(Size - 1 - ay) * Size + ax];
            return c.a == 0 ? -1 : c.r / 255f;
        }

        /// <summary>Cell-local pixel; keeps the brighter value.</summary>
        static void C(int col, int row, int x, int y, float g)
        {
            if (x < 0 || x >= Cell || y < 0 || y >= Cell) return;
            int ax = col * Cell + x, ay = row * Cell + y;
            if (G(ax, ay) >= g) return;
            P(ax, ay, g);
        }

        static void CSet(int col, int row, int x, int y, float g)
        {
            if (x < 0 || x >= Cell || y < 0 || y >= Cell) return;
            P(col * Cell + x, row * Cell + y, g);
        }

        static float Q(float v, int steps) { return Mathf.Round(Mathf.Clamp01(v) * steps) / steps; }

        static void Line(int col, int row, int x0, int y0, int x1, int y1, float g)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                C(col, row, x0, y0, g);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        static void Each(Action<int, int, float, float, float> f, float cx = 7.5f, float cy = 7.5f)
        {
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    f(x, y, dx, dy, Mathf.Sqrt(dx * dx + dy * dy));
                }
        }

        // ------------------------------------------------------------------ ramps

        static void DrawRamps()
        {
            for (int r = 0; r < 4; r++)
                for (int i = 0; i < 64; i++)
                {
                    float t = i / 63f;
                    var stops = Ramps[r];
                    int k = 0;
                    while (k < stops.Length - 2 && t > stops[k + 1].Item1) k++;
                    float u = Mathf.InverseLerp(stops[k].Item1, stops[k + 1].Item1, t);
                    var c = Color.Lerp(Hex(stops[k].Item2), Hex(stops[k + 1].Item2), u);
                    var c32 = (Color32)c;
                    c32.a = 255;
                    px[0 * Size + r * 64 + i] = c32;     // texture rows y = 0 and 1
                    px[1 * Size + r * 64 + i] = c32;
                }
        }

        static Color Hex(uint v) { return VfxElements.Hex(v); }

        // ------------------------------------------------------------------ balls (16×16, 1 px dark outline)

        static float Shade(float dx, float dy)
        {
            float nx = dx / 5.5f, ny = dy / 5.5f, nz = Mathf.Sqrt(Mathf.Max(0, 1 - nx * nx - ny * ny));
            var l = new Vector3(-0.5f, -0.6f, 0.62f).normalized;
            return Mathf.Clamp01(nx * l.x + ny * l.y + nz * l.z);
        }

        static void BallDisc(int col, Func<float, float, float, float> body)
        {
            Each((x, y, dx, dy, r) =>
            {
                if (r > 6.6f) return;
                CSet(col, RowBalls, x, y, r > 5.6f ? 0.03f : body(dx, dy, r));
            });
        }

        static void DrawBalls()
        {
            // Ember: dark coal with a glowing crack
            BallDisc(0, (dx, dy, r) => 0.1f + Q(Shade(dx, dy), 3) * 0.26f);
            int[,] crack = { { 4, 5 }, { 5, 6 }, { 6, 6 }, { 7, 7 }, { 7, 8 }, { 8, 9 }, { 9, 9 }, { 10, 10 }, { 11, 10 }, { 6, 9 }, { 5, 10 }, { 10, 8 }, { 11, 7 } };
            for (int i = 0; i < crack.GetLength(0); i++)
            {
                int x = crack[i, 0], y = crack[i, 1];
                foreach (var o in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    if (G(x + o.Item1, y + o.Item2) > 0.05f) C(0, 0, x + o.Item1, y + o.Item2, 0.6f);
            }
            for (int i = 0; i < crack.GetLength(0); i++) CSet(0, 0, crack[i, 0], crack[i, 1], 0.93f);
            CSet(0, 0, 7, 7, 1f); CSet(0, 0, 8, 9, 1f);
            CSet(0, 0, 5, 3, 0.5f);

            // Rime: faceted frost pebble, six facets around a bright top facet
            var L = new Vector3(-0.5f, -0.6f, 0.62f).normalized;
            BallDisc(1, (dx, dy, r) =>
            {
                if (r < 2.4f) return 0.78f;
                int k = Mathf.FloorToInt((Mathf.Atan2(dy, dx) + Mathf.PI) / (Mathf.PI / 3)) % 6;
                float th = (k + 0.5f) * Mathf.PI / 3 - Mathf.PI;
                var n = new Vector3(Mathf.Cos(th) * 0.6f, Mathf.Sin(th) * 0.6f, 0.8f).normalized;
                return 0.3f + Q(Mathf.Clamp01(Vector3.Dot(n, L)), 4) * 0.42f;
            });
            CSet(1, 0, 5, 4, 1f);
            C(1, 0, 4, 4, 0.86f); C(1, 0, 6, 4, 0.86f); C(1, 0, 5, 3, 0.86f); C(1, 0, 5, 5, 0.86f);
            CSet(1, 0, 10, 10, 0.86f);

            // Spark: brass-blue bead crossed by a zig-zag arc that pierces the outline
            BallDisc(2, (dx, dy, r) => 0.2f + Q(Shade(dx, dy), 4) * 0.34f);
            int[,] arc = { { 1, 9 }, { 2, 9 }, { 3, 8 }, { 4, 7 }, { 5, 7 }, { 6, 8 }, { 7, 9 }, { 8, 9 }, { 9, 8 }, { 10, 7 }, { 11, 7 }, { 12, 8 }, { 13, 7 }, { 14, 7 } };
            for (int i = 0; i < arc.GetLength(0); i++)
            {
                int x = arc[i, 0], y = arc[i, 1];
                if (G(2 * Cell + x, y - 1) > 0.05f) C(2, 0, x, y - 1, 0.7f);
                if (G(2 * Cell + x, y + 1) > 0.05f) C(2, 0, x, y + 1, 0.7f);
            }
            for (int i = 0; i < arc.GetLength(0); i++) CSet(2, 0, arc[i, 0], arc[i, 1], i == 0 || i == arc.GetLength(0) - 1 ? 0.8f : 1f);
            CSet(2, 0, 5, 4, 0.85f);

            // Ray: lens disc, darker glass ring, bright cross glint through it
            BallDisc(3, (dx, dy, r) => r > 4.5f ? 0.42f + Q(Shade(dx, dy), 3) * 0.2f : r > 3f ? 0.26f : 0.6f);
            Each((x, y, dx, dy, r) =>
            {
                bool cross = Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f;
                if (!cross || r > 7.6f) return;
                CSet(3, 0, x, y, r < 1 ? 1f : Mathf.Max(0.62f, 0.95f - r * 0.05f));
            });
            CSet(3, 0, 5, 4, 0.9f);
        }

        /// <summary>32×32 pixel halo: four hard-edged rings, used additively behind every ball.</summary>
        static void DrawHalo(int ax0, int ay0)
        {
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 15.5f, dy = y - 15.5f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float v = 1 - r / 15.5f;
                    if (v <= 0) continue;
                    float s = Mathf.Ceil(v * 4) / 4;
                    P(ax0 + x, ay0 + y, 0.1f + 0.62f * s * s);
                }
        }

        // ------------------------------------------------------------------ particles

        static void DrawFire()
        {
            for (int f = 0; f < 4; f++)
            {
                float H = 1 - 0.17f * f;
                Each((x, y, dx, dy, r) =>
                {
                    float hn = (15 - y) / (15f * H);
                    if (hn > 1) return;
                    float w = 5.6f * Mathf.Pow(1 - hn, 0.7f) * Mathf.Min(1, (hn + 0.1f) * 4) * (1 - 0.12f * f);
                    float cx = 7.5f + Mathf.Sin(hn * 3 + f * 1.6f) * 1.6f * hn;
                    float ddx = Mathf.Abs(x - cx);
                    if (ddx >= w) return;
                    float v = 1 - ddx / w * 0.55f - hn * 0.6f;
                    CSet(f, RowFire, x, y, v > 0.75f ? 1f : v > 0.55f ? 0.8f : v > 0.35f ? 0.55f : 0.3f);
                });
            }
            Each((x, y, dx, dy, r) =>
            {
                float d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d < 1) CSet(4, RowFire, x, y, 1f);
                else if (d < 2) CSet(4, RowFire, x, y, 0.82f);
                else if (d < 3) CSet(4, RowFire, x, y, 0.55f);
                else if (d < 4) CSet(4, RowFire, x, y, 0.3f);
            });
            Each((x, y, dx, dy, r) =>
            {
                float v = 1 - r / 7.5f;
                if (v > 0) CSet(5, RowFire, x, y, 0.2f + 0.55f * Mathf.Ceil(v * 3) / 3);
            });
        }

        static bool InPoly(float x, float y, Vector2[] p)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        static void DrawIce()
        {
            var a = new[] { new Vector2(8, 0), new Vector2(11.5f, 8), new Vector2(8, 16), new Vector2(4.5f, 7) };
            Each((x, y, dx, dy, r) =>
            {
                if (!InPoly(x + 0.5f, y + 0.5f, a)) return;
                bool edge = !InPoly(x + 1.5f, y + 0.5f, a);
                CSet(0, RowIce, x, y, edge ? 1f : x < 8 ? 0.5f : 0.78f);
            });
            var b = new[] { new Vector2(3, 3), new Vector2(14, 6), new Vector2(6, 14) };
            Each((x, y, dx, dy, r) =>
            {
                if (!InPoly(x + 0.5f, y + 0.5f, b)) return;
                bool edge = !InPoly(x + 1.5f, y + 1.5f, b);
                CSet(1, RowIce, x, y, edge ? 0.95f : 0.45f + Q((x + y) / 26f, 3) * 0.35f);
            });
            // snowflake A: six-ish arms with tip bars
            for (int d = -5; d <= 5; d++)
            {
                float g = d == 0 ? 1f : 0.92f - Mathf.Abs(d) * 0.05f;
                C(2, RowIce, 7 + d, 7, g); C(2, RowIce, 7, 7 + d, g);
                if (Mathf.Abs(d) <= 3) { C(2, RowIce, 7 + d, 7 + d, 0.7f); C(2, RowIce, 7 + d, 7 - d, 0.7f); }
            }
            foreach (var s in new[] { -4, 4 }) { C(2, RowIce, 7 + s, 6, 0.6f); C(2, RowIce, 7 + s, 8, 0.6f); C(2, RowIce, 6, 7 + s, 0.6f); C(2, RowIce, 8, 7 + s, 0.6f); }
            // snowflake B: tiny plus and cross
            for (int d = -2; d <= 2; d++) { C(3, RowIce, 7 + d, 7, d == 0 ? 1 : 0.8f); C(3, RowIce, 7, 7 + d, d == 0 ? 1 : 0.8f); }
            C(3, RowIce, 6, 6, 0.5f); C(3, RowIce, 8, 8, 0.5f); C(3, RowIce, 6, 8, 0.5f); C(3, RowIce, 8, 6, 0.5f);
            // frost ring
            Each((x, y, dx, dy, r) =>
            {
                if (r >= 5.5f && r <= 7.2f) CSet(4, RowIce, x, y, 0.88f);
                else if (r >= 4.5f && r < 5.5f) CSet(4, RowIce, x, y, 0.45f);
            });
            // sparkle plus
            float[] arm = { 1f, 0.85f, 0.6f, 0.4f, 0.22f };
            for (int d = 0; d < arm.Length; d++)
            {
                C(5, RowIce, 7 + d, 7, arm[d]); C(5, RowIce, 7 - d, 7, arm[d]); C(5, RowIce, 7, 7 + d, arm[d]); C(5, RowIce, 7, 7 - d, arm[d]);
            }
        }

        static void DrawZap()
        {
            Each((x, y, dx, dy, r) =>
            {
                float d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d < 1) CSet(0, RowZap, x, y, 1f);
                else if (d < 2.2f) CSet(0, RowZap, x, y, 0.6f);
            });
            var rnd = new System.Random(7);
            for (int f = 0; f < 4; f++)
            {
                int col = 1 + f, x = 0, y = 4 + rnd.Next(8);
                int mx = 0, my = 0, steps = 0;
                while (x < 15)
                {
                    int nx = Math.Min(15, x + 2 + rnd.Next(2)), ny = Mathf.Clamp(y + rnd.Next(-4, 5), 1, 14);
                    Line(col, RowZap, x, y, nx, ny, 1f);
                    if (++steps == 3) { mx = nx; my = ny; }
                    x = nx; y = ny;
                }
                int by = my < 8 ? my + 4 : my - 4;
                Line(col, RowZap, mx, my, Math.Min(15, mx + 3), by, 0.75f);
                // soft one-pixel halo around the strokes
                for (int yy = 0; yy < Cell; yy++)
                    for (int xx = 0; xx < Cell; xx++)
                        if (G(col * Cell + xx, RowZap * Cell + yy) < 0)
                            foreach (var o in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            {
                                int ax = xx + o.Item1, ay = yy + o.Item2;
                                if (ax >= 0 && ax < Cell && ay >= 0 && ay < Cell && G(col * Cell + ax, RowZap * Cell + ay) > 0.7f)
                                { CSet(col, RowZap, xx, yy, 0.4f); break; }
                            }
            }
            // star flash: long horizontal/vertical, short diagonals
            Each((x, y, dx, dy, r) =>
            {
                if (Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f) C(5, RowZap, x, y, 1 - r / 9f);
                if (Mathf.Abs(Mathf.Abs(dx) - Mathf.Abs(dy)) < 0.6f && r < 5) C(5, RowZap, x, y, 0.8f - r * 0.1f);
                if (r < 1.6f) C(5, RowZap, x, y, 1f);
            });
        }

        static void DrawRay()
        {
            // lens glint: thin 4-point star
            for (int d = 0; d <= 7; d++)
            {
                float g = d <= 1 ? 1f : 1 - d * 0.11f;
                C(0, RowRay, 7 + d, 7, g); C(0, RowRay, 7 - d, 7, g); C(0, RowRay, 7, 7 + d, g); C(0, RowRay, 7, 7 - d, g);
            }
            C(0, RowRay, 6, 6, 0.55f); C(0, RowRay, 8, 8, 0.55f); C(0, RowRay, 6, 8, 0.55f); C(0, RowRay, 8, 6, 0.55f);
            Each((x, y, dx, dy, r) => { if (r >= 6f && r <= 7.3f) CSet(1, RowRay, x, y, 0.9f); });
            Each((x, y, dx, dy, r) =>
            {
                float d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d <= 4) CSet(2, RowRay, x, y, 1 - d * 0.17f);
            });
            // flare flipbook: small star → big star with a ring → broken ring
            for (int f = 0; f < 3; f++)
            {
                int col = 3 + f;
                int len = f == 0 ? 4 : 7;
                Each((x, y, dx, dy, r) =>
                {
                    bool cross = Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f;
                    if (f < 2)
                    {
                        if (cross && r <= len + 0.5f) C(col, RowRay, x, y, 1 - r / (len + 2f) * 0.6f);
                        if (r <= (f == 0 ? 2f : 3f)) C(col, RowRay, x, y, 1f);
                        if (f == 1 && r >= 4.6f && r <= 5.6f) C(col, RowRay, x, y, 0.6f);
                    }
                    else
                    {
                        if (r >= 6f && r <= 7.2f && ((x + y) & 1) == 0) C(col, RowRay, x, y, 0.5f);
                        if (cross && r <= 7.5f && ((int)r & 1) == 0) C(col, RowRay, x, y, 0.45f);
                    }
                });
            }
        }

        static void DrawFlash()
        {
            Each((x, y, dx, dy, r) =>
            {
                if (r <= 7.2f) CSet(0, RowFlash, x, y, r < 5 ? 1f : 0.8f);
                if (r >= 4.6f && r <= 7.2f) CSet(1, RowFlash, x, y, 0.85f);
                else if (r <= 2.2f) CSet(1, RowFlash, x, y, 0.6f);
                if (r >= 6f && r <= 7.2f && ((x + y) & 1) == 0) CSet(2, RowFlash, x, y, 0.5f);
            });
        }
    }
}
