// Procedural pixel art (style.md §1, §5, §7): every ball, item, pickup and shot is a 16×16 sprite baked at startup
// into one point-filtered atlas. Colours come from per-element ramps that run from very dark to near white, so only
// the bright end reaches the bloom threshold. Also holds the 3×5 pixel font used for numbers.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public static class PixelArt
    {
        public const int Cell = 16, Grid = 16, Size = Cell * Grid;   // 256×256 atlas of 16 px cells
        public const int White = 0;                                    // a solid white cell, used by untextured UI shapes

        public static Texture2D Atlas { get; private set; }
        static readonly Dictionary<string, int> cells = new Dictionary<string, int>();
        static Color32[] px;
        static int next = 1;

        // ------------------------------------------------------------ colour ramps (64 entries, dark → near white)
        public sealed class Ramp
        {
            public readonly Color32[] C = new Color32[64];
            public Ramp(params uint[] keys)
            {
                for (int i = 0; i < 64; i++)
                {
                    float t = i / 63f * (keys.Length - 1);
                    int a = Mathf.Min(keys.Length - 2, Mathf.FloorToInt(t));
                    C[i] = Color32.Lerp(Col.Hex(keys[a]), Col.Hex(keys[a + 1]), t - a);
                }
            }
            public Color32 At(float t) { return C[Mathf.Clamp(Mathf.RoundToInt(t * 63), 0, 63)]; }
        }

        public static readonly Dictionary<string, Ramp> Ramps = new Dictionary<string, Ramp>
        {
            { "fire",   new Ramp(0x1a0604, 0x7a1c08, 0xe8541a, 0xffb347, 0xfff4d0) },
            { "ice",    new Ramp(0x041418, 0x0e5a64, 0x2ec8c0, 0x9ff0d8, 0xfaffe0) },
            { "bolt",   new Ramp(0x14101e, 0x4a4058, 0x8c8478, 0xe8dcae, 0xfffbec) },
            { "poison", new Ramp(0x061404, 0x1e5a10, 0x5cc22a, 0xc8f070, 0xf8ffe0) },
            { "iron",   new Ramp(0x050506, 0x2c2836, 0x6a6278, 0xc8b88a, 0xfff2c8) },
            { "blood",  new Ramp(0x140205, 0x5a0a18, 0xc8243a, 0xff8a8a, 0xfff0f0) },
            { "spirit", new Ramp(0x0c0818, 0x3a2a70, 0x8c78e8, 0xd8ccff, 0xffffff) },
            { "earth",  new Ramp(0x0e0904, 0x4a3218, 0x9c7040, 0xe0c48c, 0xfff4dc) },
            { "light",  new Ramp(0x14120a, 0x5a5020, 0xc8b450, 0xfff2b0, 0xffffff) },
        };

        static readonly Dictionary<string, string> ballRamp = new Dictionary<string, string>
        {
            { "ember", "fire" }, { "frost", "ice" }, { "spark", "bolt" }, { "venom", "poison" }, { "anvil", "iron" },
            { "wisp", "spirit" }, { "thorn", "blood" }, { "tremor", "earth" }, { "leech", "blood" }, { "glare", "light" },
            { "rimeburn", "spirit" }, { "powderkeg", "fire" }, { "hailstorm", "ice" }, { "nightpin", "spirit" },
            { "rotbriar", "poison" }, { "cinderquake", "fire" }, { "redtithe", "blood" }, { "noonflash", "light" },
            { "blightbomb", "poison" }, { "grapeshot", "iron" },
        };

        /// <summary>The element ramp for a ball, or a generated one (dark → its colour → white) for the rest.</summary>
        public static Ramp RampFor(string ballId, uint color)
        {
            string k;
            if (ballRamp.TryGetValue(ballId, out k)) return Ramps[k];
            string key = "c" + color.ToString("x6");
            Ramp r;
            if (!Ramps.TryGetValue(key, out r))
            {
                uint dark = Scale(color, 0.12f), mid = Scale(color, 0.5f);
                r = new Ramp(dark, mid, color, Mix(color, 0xffffff, 0.6f), 0xffffff);
                Ramps[key] = r;
            }
            return r;
        }

        static uint Scale(uint c, float k)
        {
            uint r = (uint)Mathf.Min(255, ((c >> 16) & 255) * k), g = (uint)Mathf.Min(255, ((c >> 8) & 255) * k), b = (uint)Mathf.Min(255, (c & 255) * k);
            return (r << 16) | (g << 8) | b;
        }
        static uint Mix(uint a, uint b, float t)
        {
            var c = Color32.Lerp(Col.Hex(a), Col.Hex(b), t);
            return ((uint)c.r << 16) | ((uint)c.g << 8) | c.b;
        }

        // ------------------------------------------------------------ 5×5 glyphs (original)
        static readonly Dictionary<string, string[]> glyphs = new Dictionary<string, string[]>
        {
            { "flame",  new[] { "..#..", ".##..", ".###.", "#####", ".###." } },
            { "snow",   new[] { "#.#.#", ".###.", "##.##", ".###.", "#.#.#" } },
            { "bolt",   new[] { "...#.", "..#..", ".###.", "..#..", ".#..." } },
            { "drop",   new[] { "..#..", ".###.", "#####", "#####", ".###." } },
            { "square", new[] { "#####", "#...#", "#...#", "#...#", "#####" } },
            { "ring",   new[] { ".###.", "#...#", "#...#", "#...#", ".###." } },
            { "spike",  new[] { "..#..", "..#..", ".###.", ".###.", "#####" } },
            { "hline",  new[] { ".....", ".....", "#####", ".....", "....." } },
            { "vline",  new[] { "..#..", "..#..", "..#..", "..#..", "..#.." } },
            { "cross",  new[] { "..#..", "..#..", "#####", "..#..", "..#.." } },
            { "quake",  new[] { ".....", "#...#", ".#.#.", "#.#.#", "....." } },
            { "egg",    new[] { ".###.", "##.##", "#####", "#####", ".###." } },
            { "twin",   new[] { ".....", "##.##", "##.##", ".....", "....." } },
            { "fang",   new[] { "#####", "##.##", "#...#", "#...#", "....." } },
            { "eye",    new[] { ".....", ".###.", "##.##", ".###.", "....." } },
            { "skull",  new[] { ".###.", "#.#.#", "#####", ".#.#.", "....." } },
            { "clover", new[] { ".#.#.", "#####", ".###.", "#####", ".#.#." } },
            { "fist",   new[] { ".###.", "#####", "#####", "####.", ".###." } },
            { "mitt",   new[] { "#.#.#", "#.#.#", "#####", "#####", ".###." } },
            { "arrow",  new[] { "..#..", ".###.", "#.#.#", "..#..", "..#.." } },
            { "boot",   new[] { ".##..", ".##..", ".##..", ".####", ".####" } },
            { "heart",  new[] { ".#.#.", "#####", "#####", ".###.", "..#.." } },
            { "shell",  new[] { "..#..", ".#.#.", "#.#.#", "#####", "....." } },
            { "gem",    new[] { "..#..", ".###.", "#####", ".###.", "..#.." } },
            { "magnet", new[] { "#...#", "#...#", "#...#", "#####", ".###." } },
            { "veil",   new[] { ".###.", "#####", "#####", "#####", "#.#.#" } },
            { "crown",  new[] { "#.#.#", "#####", "#####", ".....", "....." } },
        };

        // ------------------------------------------------------------ atlas
        public static void Build()
        {
            if (Atlas != null) return;
            px = new Color32[Size * Size];
            Fill(White, (x, y) => new Color32(255, 255, 255, 255));
            foreach (var kv in Content.Balls)
            {
                var d = kv.Value;
                Bake("ball:" + d.Id, BallPixels(d.Id, d.Color, d.Glyph, null, 0));
            }
            foreach (var kv in Content.Items) Bake("item:" + kv.Key, ItemPixels(kv.Value.Color, kv.Value.Glyph));
            Bake("baby", BallPixels("baby", 0xe8ecff, null, null, 0));
            Bake("gem:s", GemPixels(new Ramp(0x041a0a, 0x1e8a3a, 0x6dff8e, 0xf0fff0)));
            Bake("gem:m", GemPixels(new Ramp(0x04121a, 0x1e6a8a, 0x6be3ff, 0xf0faff)));
            Bake("gem:l", GemPixels(new Ramp(0x1a0418, 0x8a1e80, 0xff7bf2, 0xfff0fc)));
            Bake("gold", CoinPixels());
            Bake("heal", CrossPixels());
            Bake("fuser", FuserPixels());
            Bake("shot:arrow", ArrowPixels());
            Bake("shot:icicle", OrbPixels(Ramps["ice"], 4));
            Bake("shot:fireball", OrbPixels(Ramps["fire"], 5));
            Bake("shot:venom", OrbPixels(Ramps["poison"], 4));
            Bake("shot:bolt", OrbPixels(Ramps["light"], 4));
            Bake("shot:boulder", OrbPixels(Ramps["earth"], 6));
            Bake("dot", (x, y) => x >= 6 && x < 10 && y >= 6 && y < 10 ? new Color32(255, 255, 255, 255) : default(Color32));
            Bake("soft", (x, y) =>
            {
                float d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
                byte a = (byte)(Mathf.Clamp01(1 - d / 8f) * 255);
                return new Color32(255, 255, 255, a);
            });
            Bake("ring", (x, y) =>
            {
                float d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
                return d > 6 && d < 7.6f ? new Color32(255, 255, 255, 255) : default(Color32);
            });
            Atlas = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            Atlas.filterMode = FilterMode.Point;
            Atlas.wrapMode = TextureWrapMode.Clamp;
            Atlas.SetPixels32(px);
            Atlas.Apply(false, true);
            px = null;
        }

        /// <summary>Atlas cell for a sprite key; -1 if missing.</summary>
        public static int CellOf(string key) { int c; return cells.TryGetValue(key, out c) ? c : -1; }

        /// <summary>UV rectangle (u0, v0, u1, v1) of a cell. Row 0 is the top of the atlas image.</summary>
        public static Vector4 Uv(int cell)
        {
            if (cell < 0) cell = White;
            int cx = cell % Grid, cy = cell / Grid;
            float u0 = cx * Cell / (float)Size, u1 = (cx + 1) * Cell / (float)Size;
            float v1 = 1 - cy * Cell / (float)Size, v0 = 1 - (cy + 1) * Cell / (float)Size;
            return new Vector4(u0, v0, u1, v1);
        }

        /// <summary>A UV point inside the white cell, for flat-coloured shapes.</summary>
        public static Vector2 WhiteUv { get { var r = Uv(White); return new Vector2((r.x + r.z) / 2, (r.y + r.w) / 2); } }

        delegate Color32 PixelFn(int x, int y);   // y = 0 is the top row

        static void Bake(string key, PixelFn f)
        {
            if (next >= Grid * Grid) return;
            int cell = next++;
            cells[key] = cell;
            Fill(cell, f);
        }

        static void Fill(int cell, PixelFn f)
        {
            int cx = cell % Grid, cy = cell / Grid;
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                {
                    // texture rows run bottom-up; sprite rows run top-down
                    int tx = cx * Cell + x, ty = Size - 1 - (cy * Cell + y);
                    px[ty * Size + tx] = f(x, y);
                }
        }

        static bool Glyph(string name, int x, int y, int ox, int oy)
        {
            string[] g;
            if (name == null || !glyphs.TryGetValue(name, out g)) return false;
            int gx = x - ox, gy = y - oy;
            return gx >= 0 && gx < 5 && gy >= 0 && gy < 5 && g[gy][gx] == '#';
        }

        static PixelFn BallPixels(string id, uint color, string glyph, string id2, uint color2)
        {
            var ramp = RampFor(id, color);
            return (x, y) =>
            {
                float dx = x - 7.5f, dy = y - 7.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 7f) return default(Color32);
                if (d > 6.1f) return ramp.At(0.05f);                                  // dark outline
                // light from the upper left; quantised to 4 bands for a pixel look
                float nz = Mathf.Sqrt(Mathf.Max(0, 1 - (d / 6.1f) * (d / 6.1f)));
                float l = Mathf.Clamp01(0.35f + 0.5f * (-dx * 0.45f - dy * 0.55f) / 6.1f + 0.35f * nz);
                l = Mathf.Floor(l * 4) / 4 * 0.75f + 0.2f;
                if (x == 5 && y == 4 || x == 4 && y == 5) l = 1;                       // highlight
                if (Glyph(glyph, x, y, 6, 6)) l = Mathf.Max(0.08f, l - 0.45f);
                return ramp.At(l);
            };
        }

        static PixelFn ItemPixels(uint color, string glyph)
        {
            var ramp = RampFor("item", color);
            return (x, y) =>
            {
                bool corner = (x < 2 || x > 13) && (y < 2 || y > 13);
                if (x < 1 || x > 14 || y < 1 || y > 14 || corner) return default(Color32);
                if (x == 1 || x == 14 || y == 1 || y == 14 || ((x == 2 || x == 13) && (y == 2 || y == 13))) return ramp.At(0.05f);
                float l = 0.75f - (y - 2) / 12f * 0.35f;
                if (y == 2 || x == 2) l = 0.9f;
                if (Glyph(glyph, x, y, 6, 6)) l = 0.15f;
                return ramp.At(Mathf.Floor(l * 5) / 5);
            };
        }

        static PixelFn GemPixels(Ramp r)
        {
            return (x, y) =>
            {
                int dx = Mathf.Abs(x * 2 - 15), dy = Mathf.Abs(y * 2 - 15);
                if (dx * 0.8f + dy * 0.62f > 9.5f) return default(Color32);
                if (dx * 0.8f + dy * 0.62f > 8f) return r.At(0.1f);
                float l = x < 8 ? (y < 8 ? 1f : 0.6f) : (y < 8 ? 0.75f : 0.45f);
                return r.At(l);
            };
        }

        static PixelFn CoinPixels()
        {
            var r = new Ramp(0x1a1004, 0x8a6010, 0xffd84a, 0xfff8d0);
            return (x, y) =>
            {
                float d = Mathf.Sqrt((x - 7.5f) * (x - 7.5f) + (y - 7.5f) * (y - 7.5f));
                if (d > 5.5f) return default(Color32);
                if (d > 4.6f) return r.At(0.15f);
                if (x == 7 || x == 8) return r.At(y > 4 && y < 11 ? 0.45f : 0.7f);
                return r.At(x < 7 && y < 7 ? 1 : 0.7f);
            };
        }

        static PixelFn CrossPixels()
        {
            var r = Ramps["blood"];
            return (x, y) =>
            {
                bool bar = (x >= 6 && x <= 9 && y >= 3 && y <= 12) || (y >= 6 && y <= 9 && x >= 3 && x <= 12);
                bool edge = (x >= 5 && x <= 10 && y >= 2 && y <= 13) || (y >= 5 && y <= 10 && x >= 2 && x <= 13);
                return bar ? r.At(x < 8 && y < 8 ? 0.95f : 0.7f) : edge ? r.At(0.1f) : default(Color32);
            };
        }

        static PixelFn FuserPixels()
        {
            var r = Ramps["fire"];
            return (x, y) =>
            {
                if (x < 1 || x > 14 || y < 3 || y > 13) return default(Color32);
                if (x == 1 || x == 14 || y == 3 || y == 13) return r.At(0.1f);
                if (y >= 7 && y <= 9 && x >= 4 && x <= 11) return r.At(1f);
                return r.At(y < 6 ? 0.75f : 0.55f);
            };
        }

        static PixelFn ArrowPixels()
        {
            var r = new Ramp(0x041a10, 0x1e8a5a, 0x9dffcf, 0xf0fff8);
            // points up; the renderer rotates the quad to the flight direction
            return (x, y) =>
            {
                if ((x == 7 || x == 8) && y >= 4 && y <= 13) return r.At(0.6f);
                if (y >= 1 && y <= 4 && Mathf.Abs(x - 7.5f) <= (y - 0.5f)) return r.At(1f);
                if (y >= 12 && y <= 14 && (x == 6 || x == 9)) return r.At(0.4f);
                return default(Color32);
            };
        }

        static PixelFn OrbPixels(Ramp r, float rad)
        {
            return (x, y) =>
            {
                float dx = x - 7.5f, dy = y - 7.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > rad + 0.5f) return default(Color32);
                if (d > rad - 0.5f) return r.At(0.15f);
                float l = Mathf.Floor(Mathf.Clamp01(0.85f - (dx + dy) / (rad * 3)) * 3) / 3 * 0.7f + 0.3f;
                return r.At(l);
            };
        }

        // ------------------------------------------------------------ 3×5 pixel font (original)
        static readonly Dictionary<char, string> font = new Dictionary<char, string>
        {
            { '0', "###" + "#.#" + "#.#" + "#.#" + "###" }, { '1', ".#." + "##." + ".#." + ".#." + "###" },
            { '2', "##." + "..#" + ".#." + "#.." + "###" }, { '3', "##." + "..#" + ".#." + "..#" + "##." },
            { '4', "#.#" + "#.#" + "###" + "..#" + "..#" }, { '5', "###" + "#.." + "##." + "..#" + "##." },
            { '6', ".##" + "#.." + "###" + "#.#" + "###" }, { '7', "###" + "..#" + ".#." + ".#." + ".#." },
            { '8', "###" + "#.#" + "###" + "#.#" + "###" }, { '9', "###" + "#.#" + "###" + "..#" + "##." },
            { 'A', ".#." + "#.#" + "###" + "#.#" + "#.#" }, { 'B', "##." + "#.#" + "##." + "#.#" + "##." },
            { 'C', ".##" + "#.." + "#.." + "#.." + ".##" }, { 'D', "##." + "#.#" + "#.#" + "#.#" + "##." },
            { 'E', "###" + "#.." + "##." + "#.." + "###" }, { 'F', "###" + "#.." + "##." + "#.." + "#.." },
            { 'G', ".##" + "#.." + "#.#" + "#.#" + ".##" }, { 'H', "#.#" + "#.#" + "###" + "#.#" + "#.#" },
            { 'I', "###" + ".#." + ".#." + ".#." + "###" }, { 'J', "..#" + "..#" + "..#" + "#.#" + ".#." },
            { 'K', "#.#" + "#.#" + "##." + "#.#" + "#.#" }, { 'L', "#.." + "#.." + "#.." + "#.." + "###" },
            { 'M', "#.#" + "###" + "###" + "#.#" + "#.#" }, { 'N', "##." + "#.#" + "#.#" + "#.#" + "#.#" },
            { 'O', ".#." + "#.#" + "#.#" + "#.#" + ".#." }, { 'P', "##." + "#.#" + "##." + "#.." + "#.." },
            { 'Q', ".#." + "#.#" + "#.#" + "##." + ".##" }, { 'R', "##." + "#.#" + "##." + "#.#" + "#.#" },
            { 'S', ".##" + "#.." + ".#." + "..#" + "##." }, { 'T', "###" + ".#." + ".#." + ".#." + ".#." },
            { 'U', "#.#" + "#.#" + "#.#" + "#.#" + "###" }, { 'V', "#.#" + "#.#" + "#.#" + "#.#" + ".#." },
            { 'W', "#.#" + "#.#" + "###" + "###" + "#.#" }, { 'X', "#.#" + "#.#" + ".#." + "#.#" + "#.#" },
            { 'Y', "#.#" + "#.#" + ".#." + ".#." + ".#." }, { 'Z', "###" + "..#" + ".#." + "#.." + "###" },
            { '+', "..." + ".#." + "###" + ".#." + "..." }, { '-', "..." + "..." + "###" + "..." + "..." },
            { '/', "..#" + "..#" + ".#." + "#.." + "#.." }, { '.', "..." + "..." + "..." + "..." + ".#." },
            { ',', "..." + "..." + "..." + ".#." + "#.." }, { ':', "..." + ".#." + "..." + ".#." + "..." },
            { '!', ".#." + ".#." + ".#." + "..." + ".#." }, { '?', "##." + "..#" + ".#." + "..." + ".#." },
            { '%', "#.#" + "..#" + ".#." + "#.." + "#.#" }, { '\'', ".#." + ".#." + "..." + "..." + "..." },
            { '(', ".#." + "#.." + "#.." + "#.." + ".#." }, { ')', ".#." + "..#" + "..#" + "..#" + ".#." },
            { '×', "..." + "#.#" + ".#." + "#.#" + "..." }, { '·', "..." + "..." + ".#." + "..." + "..." },
            { '…', "..." + "..." + "..." + "..." + "#.#" },
        };

        /// <summary>Lit cells of a character in the 3×5 font (lowercase maps to uppercase); null for a space.</summary>
        public static string FontGlyph(char c)
        {
            string s;
            if (font.TryGetValue(char.ToUpperInvariant(c), out s)) return s;
            return null;
        }
    }
}
