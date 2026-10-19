// The pit-ui atlas (Resources/UI/ui_atlas.png plus ui_atlas.json, see pit-ui/UI-SPEC.md §5): sprite rects, 9-slice
// borders and digit-font advances, read from the json's "list" (rects in texels from the bottom-left of the full
// 2048x1024 sheet). The shipped PNG is cropped to the used top rows, so v is measured from the texture's own top.
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public static class UiSkin
    {
        [System.Serializable] class Entry { public string name; public int x, y, w, h; public int[] border; public int advance; }
        [System.Serializable] class Doc { public int[] size; public Entry[] list; }

        public sealed class Spr
        {
            public string Name;
            public int W, H, Advance;
            public int L, B, R, T;     // 9-slice borders in texels
            public float U0, V0, U1, V1;
        }

        static readonly Dictionary<string, Spr> map = new Dictionary<string, Spr>();
        public static Texture2D Atlas { get; private set; }
        public static bool Loaded { get { return Atlas != null; } }

        public static void Load()
        {
            if (Atlas != null) return;
            Atlas = Resources.Load<Texture2D>("UI/ui_atlas");
            var json = Resources.Load<TextAsset>("UI/ui_atlas");
            if (Atlas == null || json == null) { Atlas = null; Debug.LogWarning("UiSkin: Resources/UI/ui_atlas missing"); return; }
            Atlas.filterMode = FilterMode.Point;
            Atlas.wrapMode = TextureWrapMode.Clamp;
            var doc = JsonUtility.FromJson<Doc>(json.text);
            float tw = Atlas.width, th = Atlas.height;
            float sheetH = doc.size != null && doc.size.Length == 2 ? doc.size[1] : th;
            foreach (var e in doc.list)
            {
                // texel rows counted from the top of the sheet, which is also the top of the cropped texture
                float top = sheetH - (e.y + e.h);
                var s = new Spr
                {
                    Name = e.name, W = e.w, H = e.h, Advance = e.advance,
                    U0 = e.x / tw, U1 = (e.x + e.w) / tw,
                    V1 = 1 - top / th, V0 = 1 - (top + e.h) / th,
                };
                if (e.border != null && e.border.Length == 4) { s.L = e.border[0]; s.B = e.border[1]; s.R = e.border[2]; s.T = e.border[3]; }
                map[e.name] = s;
            }
        }

        public static Spr Get(string name)
        {
            Spr s;
            return name != null && map.TryGetValue(name, out s) ? s : null;
        }

        public static bool Has(string name) { return Get(name) != null; }

        // ------------------------------------------------------------ game id -> atlas sprite (placeholder art names
        // in pit-ui that match a real ball or item; everything else keeps its procedural icon)
        static readonly Dictionary<string, string> balls = new Dictionary<string, string>
        {
            { "ember", "ball_ember" }, { "frost", "ball_rime" }, { "spark", "ball_spark" }, { "venom", "ball_venom" },
            { "anvil", "ball_anvil" }, { "wisp", "ball_wisp" }, { "thorn", "ball_thorn" }, { "ray", "ball_ray" },
            { "brood", "ball_cluster" }, { "powderkeg", "ball_powder_keg" },
        };
        static readonly Dictionary<string, string> items = new Dictionary<string, string>
        {
            { "lens", "item_soot_lens" }, { "lodestone", "item_slag_magnet" }, { "kindling", "item_furnace_coal" },
        };
        static readonly Dictionary<string, string> heroes = new Dictionary<string, string>
        {
            { "tinker", "hero_tinker" }, { "needle", "hero_wren" }, { "bulwark", "hero_osk" },
            { "ricco", "hero_juno" }, { "scatter", "hero_wren" }, { "fanfare", "hero_osk" },
        };

        public static string Ball(string id) { string s; return id != null && balls.TryGetValue(id, out s) && Has(s) ? s : null; }
        public static string Item(string id) { string s; return id != null && items.TryGetValue(id, out s) && Has(s) ? s : null; }
        public static string Hero(string id) { string s; return id != null && heroes.TryGetValue(id, out s) && Has(s) ? s : null; }
    }
}
