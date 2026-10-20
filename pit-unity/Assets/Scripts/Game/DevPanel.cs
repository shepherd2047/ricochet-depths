// Dev panel: a showcase overlay that drives the run directly (Core/Run.Dev.cs). Opened with the DEV button at the
// HUD's top right, or F1 / the backquote key. The sim stands still while it is open; changes apply when it closes.
// Tabs: RUN (levels, HP and god mode, gold, straight to any boss, clear the board, autopilot, game speed),
// BALLS and ITEMS (add any of them, evolved ones included, level them 1..3, remove them).
// Left out of TikTok builds (PIT_TIKTOK) unless PIT_DEV is defined too.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public sealed class DevPanel
    {
#if PIT_TIKTOK && !PIT_DEV
        public const bool Enabled = false;
#else
        public const bool Enabled = true;
#endif
        enum Tab { Run, Balls, Items }

        readonly Gfx g;
        readonly Ui ui;
        Tab tab;
        bool manualPicks;
        public bool Open;
        /// <summary>The open tab: 0 RUN, 1 BALLS, 2 ITEMS.</summary>
        public int Page { get { return (int)tab; } set { tab = (Tab)value; } }

        static readonly Color32 PanelBg = Col.Hex(0x14111f, 0.96f), Edge = Col.Hex(0xffb347, 0.55f), Head = Col.Hex(0xffb347),
            Label = Col.Hex(0x8b86a8), On = Col.Hex(0x6dff8e);

        public DevPanel(Gfx gfx, Ui u) { g = gfx; ui = u; }

        /// <summary>The DEV button over the HUD's top right and the hotkeys; call after the HUD and the menus are drawn.</summary>
        public void Toggle(Screens s)
        {
            if (!Enabled) return;
            if (Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.BackQuote)) Open = !Open;
            float x0 = Mathf.Round((g.W - 180 * Gfx.Art) / 2 / Gfx.Art) * Gfx.Art;
            g.Cur = Layer.Menu;   // over the level-up and crucible dims too
            if (!Open && ui.Button("dev_open", x0 + 148 * Gfx.Art, s.SafeTop + 2 * Gfx.Art, 28 * Gfx.Art, 22 * Gfx.Art, "DEV", small: true)) Open = true;
        }

        /// <summary>Draws the open panel and applies what was tapped. Call last, so it sits on top and takes the input.</summary>
        public void Draw(Run r, Screens s)
        {
            if (!Enabled || !Open) return;
            g.Cur = Layer.Menu;
            g.Rect(-10, -10, g.W + 20, g.H + 20, Col.Hex(0x06080a, 0.55f));
            ui.Area("dev_bg", -10, -10, g.W + 20, g.H + 20);
            float pw = Mathf.Min(g.W - 16, 344), px = (g.W - pw) / 2, py = s.SafeTop + 8, ph = g.H - s.SafeTop - s.SafeBottom - 16;
            g.RoundRect(px - 2, py - 2, pw + 4, ph + 4, 12, Edge);
            g.RoundRect(px, py, pw, ph, 10, PanelBg);
            float x = px + 12, w = pw - 24, y = py + 10;

            // header: title, tabs, close
            g.Text("DEV PANEL", x, y + 11, 14, Head, Align.Left);
            if (ui.Button("dev_close", px + pw - 12 - 64, y, 64, 24, "CLOSE", accent: true, small: true)) Open = false;
            y += 32;
            string[] names = { "RUN", "BALLS", "ITEMS" };
            float tw = (w - 12) / 3;
            for (int i = 0; i < 3; i++)
                if (ui.Button("dev_tab", x + i * (tw + 6), y, tw, 28, names[i], accent: (int)tab == i, small: true, data: i)) tab = (Tab)i;
            y += 40;

            if (tab == Tab.Run) RunTab(r, s, x, y, w);
            else if (tab == Tab.Balls) BallsTab(r, x, y, w);
            else ItemsTab(r, x, y, w);
        }

        // ------------------------------------------------------------ RUN
        void RunTab(Run r, Screens s, float x, float y, float w)
        {
            var p = r.P;
            bool over = r.IsOver;
            const float bh = 28, gap = 6;

            Section("LEVEL " + p.Level + (p.Points > 0 ? "  ·  " + p.Points + " pick" + (p.Points > 1 ? "s" : "") + " waiting" : ""), x, ref y);
            float b4 = (w - 3 * gap) / 4;
            if (ui.Button("dev_lv", x, y, b4, bh, "+1", small: true, disabled: over, data: 1)) Levels(r, 1);
            if (ui.Button("dev_lv", x + (b4 + gap), y, b4, bh, "+5", small: true, disabled: over, data: 5)) Levels(r, 5);
            if (ui.Button("dev_lv", x + 2 * (b4 + gap), y, b4, bh, "+10", small: true, disabled: over, data: 10)) Levels(r, 10);
            if (ui.Button("dev_lv", x + 3 * (b4 + gap), y, b4, bh, "+25", small: true, disabled: over, data: 25)) Levels(r, 25);
            y += bh + gap;
            if (ui.Button("dev_picks", x, y, w, bh, manualPicks ? "PICKS: I CHOOSE (level-up cards open)" : "PICKS: AUTO (the bot picks for each level)", accent: !manualPicks, small: true))
                manualPicks = !manualPicks;
            y += bh + 14;

            Section("HERO  ·  HP " + Mathf.Max(0, p.Hp) + "/" + p.MaxHp + "  ·  GOLD " + r.Gold, x, ref y);
            if (ui.Button("dev_heal", x, y, b4, bh, "FULL HP", small: true, disabled: over)) r.DevHeal();
            if (ui.Button("dev_god", x + (b4 + gap), y, b4, bh, r.God ? "GOD ON" : "GOD OFF", accent: r.God, small: true)) r.God = !r.God;
            if (ui.Button("dev_gold", x + 2 * (b4 + gap), y, b4, bh, "+100 G", small: true, data: 100)) r.DevGold(100);
            if (ui.Button("dev_gold", x + 3 * (b4 + gap), y, b4, bh, "+1000 G", small: true, data: 1000)) r.DevGold(1000);
            y += bh + 14;

            Section("JUMP TO BOSS" + (r.Boss != null ? "  ·  now: " + r.Boss.Name : ""), x, ref y);
            var bosses = r.L.Bosses;
            for (int i = 0; i < bosses.Length; i++)
            {
                var bd = Content.Bosses[bosses[i]];
                string label = (i + 1) + ". " + bd.Name.ToUpperInvariant() + (bd.Final ? "  (FINAL)" : "");
                if (ui.Button("dev_boss", x, y, w, bh, label, small: true, disabled: over, data: i)) { r.DevBoss(i); Open = false; }
                y += bh + gap;
            }
            float b2 = (w - gap) / 2;
            if (ui.Button("dev_killboss", x, y, b2, bh, "KILL BOSS", small: true, disabled: over || r.Boss == null)) r.DevKillBoss();
            if (ui.Button("dev_clear", x + b2 + gap, y, b2, bh, "CLEAR FOES", small: true, disabled: over)) r.DevClear();
            y += bh + 14;

            Section("PACE  (tune live, then tell Claude the numbers)", x, ref y);
            Stepper("dev_pace", 0, x, y, w, "GAME SPEED", GameRoot.GameSpeed, v => GameRoot.GameSpeed = v, 0.5f, 3, 0.1f);
            y += bh + gap;
            Stepper("dev_pace", 1, x, y, w, "MAP DESCENT", r.Descent, v => r.Descent = v, 0.5f, 3, 0.05f);
            y += bh + 14;

            Section("PLAY", x, ref y);
            if (ui.Button("dev_bot", x, y, b4, bh, s.BotOn ? "BOT ON" : "BOT OFF", accent: s.BotOn, small: true)) s.BotOn = !s.BotOn;
            float[] speeds = { 1, 2, 3, 4 };
            float sw = (w - b4 - 4 * gap) / 4;
            for (int i = 0; i < speeds.Length; i++)
                if (ui.Button("dev_speed", x + b4 + gap + i * (sw + gap), y, sw, bh, "x" + speeds[i], accent: Mathf.Abs(s.SpeedSetting - speeds[i]) < 0.01f, small: true, data: i))
                    s.SpeedSetting = speeds[i];
            y += bh + 10;
            g.Text("Turn " + r.Turn + "  ·  time " + Mathf.FloorToInt(r.Time) + "s  ·  kills " + r.Kills + "  ·  hero " + r.Hero.Name, x, y + 6, 10, Label, Align.Left);
        }

        /// <summary>n level-ups; unless picks are manual, each pick is made the way the bot would.</summary>
        void Levels(Run r, int n)
        {
            if (r.Mode == Mode.Intro) r.Mode = Mode.Playing;
            r.DevLevels(n);
            if (manualPicks) return;
            for (int k = 0; k < 200 && (r.P.Points > 0 || r.Mode == Mode.LevelUp); k++)
            {
                if (r.Mode != Mode.LevelUp) r.OpenLevelUp();
                if (r.Mode == Mode.LevelUp) Bot.AutoPick(r); else if (r.Mode != Mode.Playing) break;
            }
        }

        // ------------------------------------------------------------ BALLS / ITEMS
        void BallsTab(Run r, float x, float y, float w)
        {
            Section("OWNED  " + r.Balls.Count + "/" + Run.Slots, x, ref y);
            OwnedBall drop = null;
            for (int i = 0; i < r.Balls.Count; i++)
            {
                var b = r.Balls[i];
                var d = Content.Balls[b.Id];
                if (OwnedRow("dev_ob", i, x, y, w, d.Name + (b.Fused != null ? " + " + Content.Balls[b.Fused].Name : ""), d.Color, b.Level, out int dl, out bool rm))
                {
                    if (dl != 0) r.DevLevelBall(b, dl);
                    if (rm) drop = b;
                }
                y += 32;
            }
            if (drop != null) r.DevRemoveBall(drop);
            if (r.Balls.Count == 0) g.Text("No balls: babies only", x, y + 10, 11, Label, Align.Left);
            y += 32 * (Run.Slots - r.Balls.Count) + 8;
            Section("ADD (tap)", x, ref y);
            var ids = new List<string>(Content.Balls.Keys);
            ids.Sort((a, b) => Content.Balls[a].Evolved.CompareTo(Content.Balls[b].Evolved));
            bool full = r.Balls.Count >= Run.Slots;
            Grid("dev_ab", ids, id => Content.Balls[id].Name, id => Content.Balls[id].Color, id => Content.Balls[id].Evolved, id => full, x, y, w,
                id => r.DevAddBall(id));
            if (full) g.Text("Slots full: remove one to add another", x, g.H - 30, 10, Label, Align.Left);
        }

        void ItemsTab(Run r, float x, float y, float w)
        {
            Section("OWNED  " + r.Items.Count + "/" + Run.Slots, x, ref y);
            OwnedItem drop = null;
            for (int i = 0; i < r.Items.Count; i++)
            {
                var it = r.Items[i];
                if (OwnedRow("dev_oi", i, x, y, w, it.Def.Name, it.Def.Color, it.Level, out int dl, out bool rm))
                {
                    if (dl != 0) r.DevLevelItem(it, dl);
                    if (rm) drop = it;
                }
                y += 32;
            }
            if (drop != null) r.DevRemoveItem(drop);
            if (r.Items.Count == 0) g.Text("No items yet", x, y + 10, 11, Label, Align.Left);
            y += 32 * (Run.Slots - r.Items.Count) + 8;
            Section("ADD (tap)", x, ref y);
            var ids = new List<string>(Content.Items.Keys);
            ids.Sort((a, b) => Content.Items[a].Evolved.CompareTo(Content.Items[b].Evolved));
            bool full = r.Items.Count >= Run.Slots;
            Grid("dev_ai", ids, id => Content.Items[id].Name, id => Content.Items[id].Color, id => Content.Items[id].Evolved,
                id => full || r.ItemLv(id) > 0, x, y, w, id => r.DevAddItem(id));
        }

        /// <summary>One owned ball or item: colour dot, name, level with − / +, and remove. True when something was tapped.</summary>
        bool OwnedRow(string id, int i, float x, float y, float w, string name, uint color, int level, out int dl, out bool rm)
        {
            dl = 0; rm = false;
            g.RoundRect(x, y, w, 28, 8, Col.Hex(0xffffff, 0.06f));
            g.Circle(x + 14, y + 14, 6, Col.Hex(color));
            g.Text(name, x + 26, y + 14, 12, Col.Text, Align.Left);
            float bx = x + w - 4 * 30 - 8;
            if (ui.Button(id + "_dn", bx, y + 2, 26, 24, "-", small: true, disabled: level <= 1, data: i)) dl = -1;
            g.Text("Lv" + level, bx + 30 + 13, y + 14, 11, Head);
            if (ui.Button(id + "_up", bx + 60, y + 2, 26, 24, "+", small: true, disabled: level >= 3, data: i)) dl = 1;
            if (ui.Button(id + "_rm", bx + 90, y + 2, 34, 24, "DEL", small: true, data: i)) rm = true;
            return dl != 0 || rm;
        }

        void Grid(string id, List<string> ids, System.Func<string, string> name, System.Func<string, uint> color, System.Func<string, bool> evolved,
            System.Func<string, bool> disabled, float x, float y, float w, System.Func<string, bool> add)
        {
            const int cols = 3;
            const float ch = 26, gap = 5;
            float cw = (w - (cols - 1) * gap) / cols;
            for (int i = 0; i < ids.Count; i++)
            {
                float cx = x + (i % cols) * (cw + gap), cy = y + (i / cols) * (ch + gap);
                if (cy + ch > g.H - 40) break;
                bool off = disabled(ids[i]);
                if (ui.Button(id, cx, cy, cw, ch, null, small: true, disabled: off, data: i)) add(ids[i]);
                g.Circle(cx + 10, cy + ch / 2, 4.5f, Col.Hex(color(ids[i]), off ? 0.4f : 1));
                string n = name(ids[i]);
                if (n.Length > 13) n = n.Substring(0, 12) + ".";
                g.Text(n, cx + 19, cy + ch / 2, 10, evolved(ids[i]) ? (off ? Col.A(Head, 0.4f) : Head) : (off ? Col.Dim : Col.Text), Align.Left);
            }
        }

        /// <summary>A label, the value as "1.44x", and - / + buttons that step it within lo..hi.</summary>
        void Stepper(string id, int i, float x, float y, float w, string label, float v, System.Action<float> set, float lo, float hi, float step)
        {
            g.RoundRect(x, y, w, 28, 8, Col.Hex(0xffffff, 0.06f));
            g.Text(label, x + 10, y + 14, 11, Col.Text, Align.Left);
            float bx = x + w - 4 - 3 * 34 - 40;
            if (ui.Button(id + "_dn", bx, y + 2, 34, 24, "-", small: true, disabled: v <= lo + 1e-3f, data: i)) set(Mathf.Max(lo, Mathf.Round((v - step) * 100) / 100));
            g.Text(v.ToString("0.00") + "x", bx + 34 + 37, y + 14, 12, Head);
            if (ui.Button(id + "_up", bx + 34 + 74, y + 2, 34, 24, "+", small: true, disabled: v >= hi - 1e-3f, data: i)) set(Mathf.Min(hi, Mathf.Round((v + step) * 100) / 100));
        }

        void Section(string title, float x, ref float y)
        {
            g.Text(title, x, y + 6, 11, Label, Align.Left);
            y += 16;
        }
    }
}
