// Small vector glyphs for balls and items, and the round/square icons that carry them.
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public static class Icons
    {
        static readonly List<Vector2> pts = new List<Vector2>(16);

        static void P(float x, float y) { pts.Add(new Vector2(x, y)); }

        public static void Glyph(Gfx g, string name, float x, float y, float r, Color32 c)
        {
            float lw = Mathf.Max(1.2f, r * 0.18f);
            pts.Clear();
            switch (name)
            {
                case "flame":
                    P(x, y - r * 0.62f); P(x + r * 0.38f, y + r * 0.05f); P(x + r * 0.25f, y + r * 0.42f); P(x, y + r * 0.55f);
                    P(x - r * 0.25f, y + r * 0.42f); P(x - r * 0.38f, y + r * 0.05f);
                    g.Poly(pts, c); break;
                case "snow":
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * Mathf.PI / 3;
                        g.Line(x + Mathf.Cos(a) * r * 0.55f, y + Mathf.Sin(a) * r * 0.55f, x - Mathf.Cos(a) * r * 0.55f, y - Mathf.Sin(a) * r * 0.55f, lw, c);
                    }
                    break;
                case "bolt":
                    g.Line(x + r * 0.15f, y - r * 0.6f, x - r * 0.25f, y + r * 0.05f, lw, c);
                    g.Line(x - r * 0.25f, y + r * 0.05f, x + r * 0.15f, y + r * 0.05f, lw, c);
                    g.Line(x + r * 0.15f, y + r * 0.05f, x - r * 0.15f, y + r * 0.6f, lw, c);
                    break;
                case "drop":
                    P(x, y - r * 0.58f); P(x + r * 0.36f, y + r * 0.1f); P(x + r * 0.22f, y + r * 0.42f); P(x, y + r * 0.52f);
                    P(x - r * 0.22f, y + r * 0.42f); P(x - r * 0.36f, y + r * 0.1f);
                    g.Poly(pts, c); break;
                case "square": g.Rect(x - r * 0.35f, y - r * 0.35f, r * 0.7f, r * 0.7f, c); break;
                case "ring": g.Ring(x, y, r * 0.38f, lw, c); break;
                case "spike": g.Tri(x, y - r * 0.6f, x + r * 0.25f, y + r * 0.4f, x - r * 0.25f, y + r * 0.4f, c); break;
                case "hline": g.Line(x - r * 0.6f, y, x + r * 0.6f, y, lw * 1.2f, c); break;
                case "vline": g.Line(x, y - r * 0.6f, x, y + r * 0.6f, lw * 1.2f, c); break;
                case "cross":
                    g.Line(x - r * 0.55f, y, x + r * 0.55f, y, lw, c); g.Line(x, y - r * 0.55f, x, y + r * 0.55f, lw, c); break;
                case "quake":
                    g.Line(x - r * 0.6f, y + r * 0.1f, x - r * 0.25f, y - r * 0.25f, lw, c);
                    g.Line(x - r * 0.25f, y - r * 0.25f, x + r * 0.05f, y + r * 0.3f, lw, c);
                    g.Line(x + r * 0.05f, y + r * 0.3f, x + r * 0.3f, y - r * 0.2f, lw, c);
                    g.Line(x + r * 0.3f, y - r * 0.2f, x + r * 0.6f, y + r * 0.1f, lw, c);
                    break;
                case "egg": g.Ellipse(x, y + r * 0.05f, r * 0.32f, r * 0.45f, c); break;
                case "twin": g.Circle(x - r * 0.22f, y, r * 0.24f, c); g.Circle(x + r * 0.22f, y, r * 0.24f, c); break;
                case "fang":
                    g.Tri(x - r * 0.45f, y - r * 0.35f, x - r * 0.05f, y - r * 0.35f, x - r * 0.25f, y + r * 0.5f, c);
                    g.Tri(x + r * 0.05f, y - r * 0.35f, x + r * 0.45f, y - r * 0.35f, x + r * 0.25f, y + r * 0.5f, c); break;
                case "eye":
                    g.Ellipse(x, y, r * 0.55f, r * 0.3f, c);
                    g.Circle(x, y, r * 0.15f, new Color32(255, 255, 255, 170)); break;
                case "skull":
                    g.Circle(x, y - r * 0.1f, r * 0.42f, c); g.Rect(x - r * 0.22f, y + r * 0.2f, r * 0.44f, r * 0.3f, c); break;
                case "clover":
                    for (int k = 0; k < 4; k++) g.Circle(x + Mathf.Cos(k * Mathf.PI / 2) * r * 0.25f, y + Mathf.Sin(k * Mathf.PI / 2) * r * 0.25f, r * 0.22f, c);
                    break;
                case "fist": g.RoundRect(x - r * 0.45f, y - r * 0.3f, r * 0.9f, r * 0.6f, r * 0.15f, c); break;
                case "mitt": g.Arc(x, y + r * 0.05f, r * 0.42f, lw, -0.1f * Mathf.PI, 1.1f * Mathf.PI, c); break;
                case "arrow":
                    g.Line(x - r * 0.5f, y + r * 0.3f, x, y - r * 0.4f, lw, c); g.Line(x, y - r * 0.4f, x + r * 0.5f, y + r * 0.3f, lw, c); break;
                case "boot":
                    g.Line(x - r * 0.2f, y - r * 0.5f, x - r * 0.2f, y + r * 0.35f, lw, c); g.Line(x - r * 0.2f, y + r * 0.35f, x + r * 0.5f, y + r * 0.35f, lw, c); break;
                case "heart":
                    g.Circle(x - r * 0.2f, y - r * 0.1f, r * 0.24f, c); g.Circle(x + r * 0.2f, y - r * 0.1f, r * 0.24f, c);
                    g.Tri(x - r * 0.43f, y, x + r * 0.43f, y, x, y + r * 0.5f, c); break;
                case "shell": g.Pie(x, y + r * 0.2f, r * 0.45f, 0, Mathf.PI, c); break;
                case "gem":
                    P(x, y - r * 0.55f); P(x + r * 0.45f, y); P(x, y + r * 0.55f); P(x - r * 0.45f, y);
                    g.Poly(pts, c); break;
                case "magnet": g.Arc(x, y, r * 0.4f, lw * 1.3f, Mathf.PI, Mathf.PI * 2, c); break;
                case "veil":
                    g.Pie(x, y - r * 0.05f, r * 0.42f, 0, Mathf.PI, c);
                    g.Rect(x - r * 0.42f, y - r * 0.05f, r * 0.84f, r * 0.4f, c);
                    break;
                case "crown":
                    P(x - r * 0.5f, y + r * 0.35f); P(x - r * 0.5f, y - r * 0.3f); P(x - r * 0.2f, y); P(x, y - r * 0.45f);
                    P(x + r * 0.2f, y); P(x + r * 0.5f, y - r * 0.3f); P(x + r * 0.5f, y + r * 0.35f);
                    g.Poly(pts, c); break;
                default: g.Circle(x, y, r * 0.2f, c); break;
            }
        }

        static readonly Color32 GlyphInk = new Color32(0, 0, 0, 140);

        public static void Ball(Gfx g, float x, float y, float r, uint color, string glyph, uint color2 = 0)
        {
            g.Circle(x, y, r, Col.Hex(color));
            if (color2 != 0) g.Pie(x, y, r, -Mathf.PI / 2, Mathf.PI / 2, Col.Hex(color2));
            g.Ring(x, y, r, 2, new Color32(255, 255, 255, 90));
            Glyph(g, glyph, x, y, r, GlyphInk);
        }

        public static void Item(Gfx g, float x, float y, float r, uint color, string glyph)
        {
            g.RoundRect(x - r, y - r, r * 2, r * 2, r * 0.35f, Col.Hex(color));
            g.RoundRectStroke(x - r, y - r, r * 2, r * 2, r * 0.35f, 2, new Color32(255, 255, 255, 77));
            Glyph(g, glyph, x, y, r, GlyphInk);
        }
    }
}
