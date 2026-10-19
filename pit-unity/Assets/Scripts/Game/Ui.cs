// Pointer input (one touch or the mouse) and immediate-mode widgets. A widget registers its rectangle while it is
// drawn; a press is matched against last frame's rectangles, and a click fires when the press is released over the
// same widget.
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public sealed class Ui
    {
        struct Hit { public string Id; public int Data; public float X, Y, W, H; }

        readonly List<Hit> cur = new List<Hit>(), last = new List<Hit>();
        readonly Gfx g;

        // pointer state in design units
        public bool Down, Pressed, Released;
        /// <summary>Widgets clicked this frame (counted by Area); the host reads and zeroes it for the click sound.</summary>
        public int Clicks;
        public float X, Y, PressX, PressY, PressTime;
        int fingerId = -1;
        string pressId; int pressData;
        string clickId; int clickData;
        public bool PressOnWidget { get { return pressId != null; } }

        public Ui(Gfx gfx) { g = gfx; }

        // ------------------------------------------------------------ input
        public void Poll(float scale, float now)
        {
            Pressed = Released = false;
            clickId = null;
            bool down = Down; float sx = 0, sy = 0; bool have = false;
            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                bool found = false;
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var ti = Input.GetTouch(i);
                    if (fingerId < 0 ? ti.phase == TouchPhase.Began : ti.fingerId == fingerId) { t = ti; found = true; break; }
                }
                if (found)
                {
                    have = true; sx = t.position.x; sy = t.position.y;
                    if (t.phase == TouchPhase.Began) { down = true; fingerId = t.fingerId; }
                    else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { down = false; fingerId = -1; }
                }
                else if (Down && fingerId >= 0) { down = false; fingerId = -1; }
            }
            else
            {
                if (fingerId >= 0) { fingerId = -1; down = false; }
                else
                {
                    have = true; sx = Input.mousePosition.x; sy = Input.mousePosition.y;
                    down = Input.GetMouseButton(0);
                }
            }
            if (have) { X = sx / scale; Y = (Screen.height - sy) / scale; }
            if (down && !Down)
            {
                Pressed = true; PressX = X; PressY = Y; PressTime = now;
                var h = HitTest(X, Y);
                pressId = h.HasValue ? h.Value.Id : null;
                pressData = h.HasValue ? h.Value.Data : 0;
            }
            if (!down && Down)
            {
                Released = true;
                if (pressId != null)
                {
                    var h = HitTest(X, Y);
                    if (h.HasValue && h.Value.Id == pressId && h.Value.Data == pressData) { clickId = pressId; clickData = pressData; }
                }
            }
            Down = down;
            if (Released) pressId = null;
        }

        Hit? HitTest(float x, float y)
        {
            for (int i = last.Count - 1; i >= 0; i--)
            {
                var h = last[i];
                if (x >= h.X && x <= h.X + h.W && y >= h.Y && y <= h.Y + h.H) return h;
            }
            return null;
        }

        public void BeginFrame() { cur.Clear(); }
        public void EndFrame() { last.Clear(); last.AddRange(cur); }

        /// <summary>Registers a tappable rectangle; true on the frame it is clicked.</summary>
        public bool Area(string id, float x, float y, float w, float h, int data = 0, bool disabled = false)
        {
            if (!disabled) cur.Add(new Hit { Id = id, Data = data, X = x, Y = y, W = w, H = h });
            bool hit = !disabled && clickId == id && clickData == data;
            if (hit) Clicks++;
            return hit;
        }

        public bool Held(string id, int data = 0) { return Down && pressId == id && pressData == data; }
        bool IsHeld(string id, int data) { return Held(id, data); }

        // ------------------------------------------------------------ widgets
        public bool Button(string id, float x, float y, float w, float h, string label, bool accent = false, bool disabled = false,
            bool small = false, bool pulse = false, int data = 0)
        {
            bool held = IsHeld(id, data);
            float p = pulse ? 0.5f + 0.5f * Mathf.Sin(g.Time * 6.5f) : 0;
            Color32 fill = disabled ? new Color32(255, 255, 255, 13)
                : accent ? Col.A(Col.Accent, 0.85f + 0.15f * p)
                : held ? Col.Panel2 : new Color32(255, 255, 255, 26);
            if (held && accent) fill = Col.Mix(fill, Col.White, 0.25f);
            g.RoundRect(x, y, w, h, Mathf.Min(10, h / 3), fill);
            if (!string.IsNullOrEmpty(label))
                g.Text(label, x + w / 2, y + h / 2, small ? 12 : 15, disabled ? Col.Dim : accent ? Col.Hex(0x2a1600) : Col.Text);
            return Area(id, x, y, w, h, data, disabled);
        }

        public struct CardInfo
        {
            public string Title, Tag, Desc, Glyph;
            public uint Color, Color2;
            public bool Item, NoIcon;
        }

        /// <summary>A tappable card with an icon, a title, a tag and a wrapped description.</summary>
        public bool Card(string id, int data, float x, float y, float w, float h, CardInfo info, bool disabled = false, int maxLines = 4)
        {
            bool held = IsHeld(id, data);
            g.RoundRect(x, y, w, h, 12, held ? Col.Panel2 : Col.Panel);
            g.RoundRectStroke(x, y, w, h, 12, 2, disabled ? new Color32(255, 255, 255, 15) : Col.Hex(info.Color));
            float ir = Mathf.Min(22, h * 0.24f), tx = x + 14;
            if (!info.NoIcon)
            {
                if (info.Item) Icons.Item(g, x + 14 + ir, y + 14 + ir, ir, info.Color, info.Glyph);
                else Icons.Ball(g, x + 14 + ir, y + 14 + ir, ir, info.Color, info.Glyph, info.Color2);
                tx = x + 26 + ir * 2;
            }
            g.Text(info.Title, tx, y + 21, 16, disabled ? Col.Dim : Col.Text, Align.Left);
            if (!string.IsNullOrEmpty(info.Tag)) g.Text(info.Tag, tx, y + 40, 11, disabled ? Col.Dim : Col.Hex(info.Color), Align.Left);
            var lines = g.Wrap(info.Desc, 12, x + w - tx - 10);
            for (int i = 0; i < lines.Count && i < maxLines; i++)
                g.Text(lines[i], tx, y + 58 + i * 15, 12, disabled ? Col.Dim : Col.Hex(0xcfd6ff), Align.Left, false);
            return Area(id, x, y, w, h, data, disabled);
        }

        public void Bar(float x, float y, float w, float h, float k, Color32 col, string label = null)
        {
            g.RoundRect(x, y, w, h, h / 2, new Color32(0, 0, 0, 115));
            if (k > 0) g.RoundRect(x, y, Mathf.Max(h, w * Mathf.Min(1, k)), h, h / 2, col);
            if (!string.IsNullOrEmpty(label)) g.Text(label, x + w / 2, y + h / 2, h * 0.72f, Col.White);
        }

        public void Dim(float a = 0.88f) { g.Rect(-10, -10, g.W + 20, g.H + 20, new Color32(8, 6, 16, (byte)(a * 255))); }

        public void Heading(float y, string title, string sub, Color32 color)
        {
            g.Text(title, g.W / 2, y, 26, color);
            if (!string.IsNullOrEmpty(sub)) g.Text(sub, g.W / 2, y + 26, 13, Col.Dim, Align.Center, false);
        }
    }
}
