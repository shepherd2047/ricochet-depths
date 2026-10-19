// Reference runtime helpers for the Pit UI (uGUI). See pit-ui/UI-SPEC.md. Not compiled or tested in Unity yet.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PitUi
{
    /// Integer pixel scale: 1 canvas unit = 1 art pixel, scale = floor(screen width / 180).
    [RequireComponent(typeof(CanvasScaler))]
    public class PixelCanvasScaler : MonoBehaviour
    {
        public const int DesignWidth = 180;
        public const int MinScale = 2;
        int lastW, lastH;

        void OnEnable() => Apply();
        void Update() { if (Screen.width != lastW || Screen.height != lastH) Apply(); }

        void Apply()
        {
            lastW = Screen.width; lastH = Screen.height;
            var cs = GetComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            cs.referencePixelsPerUnit = 16;                 // sprite PPU, so a 1x1 texel = 1 canvas unit
            cs.scaleFactor = Mathf.Max(MinScale, Screen.width / DesignWidth);   // integer division on purpose
            GetComponent<Canvas>().pixelPerfect = true;
        }

        public static int Scale => Mathf.Max(MinScale, Screen.width / DesignWidth);
    }

    /// Fits this RectTransform to Screen.safeArea, then keeps TopBar clear of the TikTok capsule.
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaRoot : MonoBehaviour
    {
        [Tooltip("Optional: the top HUD row-2 container (level badge + XP bar). It is pushed below the capsule when needed.")]
        public RectTransform belowCapsule;
        [Tooltip("Optional: the top HUD row-1 container (pause, speed, gold). Its width is clipped to stop left of the capsule.")]
        public RectTransform leftOfCapsule;

        /// Capsule rect in screen pixels (origin bottom-left). Set this from the TikTok SDK at startup
        /// (menu-button bounding rect); until then the fallback below is used.
        public static Rect? CapsuleScreenRect;

        Rect lastSafe; Vector2Int lastScreen;

        void OnEnable() => Apply();
        void Update()
        {
            if (Screen.safeArea != lastSafe || lastScreen.x != Screen.width || lastScreen.y != Screen.height) Apply();
        }

        void Apply()
        {
            lastSafe = Screen.safeArea; lastScreen = new Vector2Int(Screen.width, Screen.height);
            var rt = (RectTransform)transform;
            var sa = Screen.safeArea;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            int s = PixelCanvasScaler.Scale;
            // fallback capsule: 52x22 art px, flush right, 0..22 art px below the safe top (UI-SPEC section 3)
            Rect cap = CapsuleScreenRect ?? new Rect(Screen.width - 52 * s, sa.yMax - 22 * s, 52 * s, 22 * s);
            float capBelowSafeTop = Mathf.Max(0, (sa.yMax - cap.yMin) / s);       // art px
            float capLeftFromSafeRight = Mathf.Max(0, (sa.xMax - cap.xMin) / s);  // art px
            if (belowCapsule != null)
            {
                var p = belowCapsule.anchoredPosition;
                p.y = -Mathf.Max(28, Mathf.Ceil(capBelowSafeTop) + 2);            // design: row 2 sits 28 below safe top
                belowCapsule.anchoredPosition = p;
            }
            if (leftOfCapsule != null)
            {
                float right = Mathf.Ceil(capLeftFromSafeRight) + 4;
                leftOfCapsule.offsetMax = new Vector2(-right, leftOfCapsule.offsetMax.y);
            }
        }
    }

    /// Draws a string with the bitmap digit fonts (hud / dmg / crit) from the UI atlas.
    /// Glyph sprites are named font_{font}_{0-9|plus|minus|slash|dot|bang|colon|pct|space|x|k}.
    [RequireComponent(typeof(RectTransform))]
    public class PixelNumber : MonoBehaviour
    {
        public enum Font { Hud, Dmg, Crit }
        public enum Align { Left, Center, Right }

        public Font font = Font.Hud;
        public Align align = Align.Left;
        public Color tint = Color.white;          // dmg font is white: tint per damage type (UI-SPEC section 8)
        [Min(1)] public int scale = 1;            // crit is shown at 2
        public Sprite[] glyphs;                   // all font_* sprites; assign the sliced atlas sub-assets
        [SerializeField] string text = "0";

        readonly List<Image> pool = new List<Image>();
        Dictionary<string, Sprite> map;

        public string Text { get => text; set { if (value != text) { text = value; Rebuild(); } } }

        static readonly Dictionary<char, string> Names = new Dictionary<char, string>
        { ['+'] = "plus", ['-'] = "minus", ['/'] = "slash", ['.'] = "dot", ['!'] = "bang", [':'] = "colon", ['%'] = "pct", [' '] = "space" };

        void OnEnable() => Rebuild();
        void OnValidate() { map = null; if (isActiveAndEnabled) Rebuild(); }

        string Prefix => "font_" + font.ToString().ToLowerInvariant() + "_";
        int Gap => font == Font.Hud ? 1 : -1;     // outlined fonts overlap their 1px outlines

        Sprite Glyph(char c)
        {
            if (map == null) { map = new Dictionary<string, Sprite>(); foreach (var g in glyphs) if (g) map[g.name] = g; }
            map.TryGetValue(Prefix + (Names.TryGetValue(c, out var n) ? n : c.ToString()), out var s);
            return s;
        }

        public void Rebuild()
        {
            if (glyphs == null || glyphs.Length == 0) return;
            var sprites = new List<Sprite>();
            foreach (var c in text) { var g = Glyph(char.ToLowerInvariant(c)); if (g) sprites.Add(g); }
            float width = 0; foreach (var g in sprites) width += g.rect.width + Gap;
            if (sprites.Count > 0) width -= Gap;
            float x = align == Align.Left ? 0 : align == Align.Center ? -Mathf.Round(width / 2) : -width;
            while (pool.Count < sprites.Count)
            {
                var go = new GameObject("g", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                var img = go.GetComponent<Image>(); img.raycastTarget = false;
                var r = img.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 0.5f); r.pivot = new Vector2(0, 0);
                pool.Add(img);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                var img = pool[i];
                img.gameObject.SetActive(i < sprites.Count);
                if (i >= sprites.Count) continue;
                var g = sprites[i];
                img.sprite = g; img.color = tint;
                float ax = align == Align.Left ? 0 : align == Align.Center ? 0.5f : 1;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(ax, 0.5f);
                img.rectTransform.sizeDelta = g.rect.size * scale;
                img.rectTransform.anchoredPosition = new Vector2(x * scale, -Mathf.Floor(g.rect.height / 2) * scale);
                x += g.rect.width + Gap;
            }
        }
    }
}
