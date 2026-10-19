// HUD, in-run menus (level-up, fuser, pause, results) and the title and hero-select screens.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public enum Nav { None, Start, PickHero, Again, Heroes, Restart, Quit }

    public sealed class Screens
    {
        readonly Gfx g;
        readonly Ui ui;
        public float SafeTop, SafeBottom;
        public int SpeedSetting = 1;
        public bool BotOn;

        public Screens(Gfx gfx, Ui u) { g = gfx; ui = u; }

        public float SlotSize { get { return Mathf.Min(44, (g.W - 40) / 8.6f); } }
        public float HudTop { get { return SafeTop + 70; } }
        public float HudBottom { get { return SlotSize * 2 + 52 + SafeBottom; } }

        // ------------------------------------------------------------ HUD
        public void Hud(Run r, WorldView v, ref bool pauseClicked)
        {
            g.Cur = Layer.Hud;
            float W = g.W, top = SafeTop;
            var p = r.P;
            float bw = W - 16 - 96;
            ui.Bar(8, top + 6, bw * 0.62f, 18, (float)p.Hp / p.MaxHp, Col.Hp, null);
            g.Circle(8 + bw * 0.62f + 16, top + 15, 6, Col.Gold);
            if (ui.Button("speed", W - 94, top + 2, 42, 28, SpeedSetting + "x", small: true)) SpeedSetting = SpeedSetting == 1 ? 2 : 1;
            if (ui.Button("pause", W - 48, top + 2, 40, 28, "II", small: true)) pauseClicked = true;
            int need = M.XpNeed(p.Level);
            ui.Bar(8, top + 32, W - 16, 12, (float)p.Xp / need, Col.Xp, null);

            // boss bar on the board
            var boss = r.Boss;
            if (boss != null && !boss.Dead && boss.Brain != null && !boss.Brain.Descending)
                ui.Bar(v.Ox + 10, v.Oy + 6, r.Bw * v.S - 20, 12, boss.Hp / boss.MaxHp, Col.Hex(0xc45bff), null);

            // slots
            float slot = SlotSize, by = g.H - SafeBottom - slot * 2 - 40;
            for (int i = 0; i < Run.Slots; i++)
            {
                float x = 52 + i * (slot + 8), y = by + 2 + slot / 2 - 8;
                g.Ring(x + slot / 2, y, slot / 2 - 2, 1.5f, new Color32(255, 255, 255, 30));
                if (i < r.Balls.Count)
                {
                    var b = r.Balls[i];
                    Icons.Ball(g, x + slot / 2, y, slot / 2 - 5, b.Def.Color, b.Def.Glyph, b.Fused != null ? Content.Balls[b.Fused].Color : 0);
                    if (b.State != BallState.Ready)
                    {
                        g.Circle(x + slot / 2, y, slot / 2 - 5, new Color32(18, 15, 29, 140));
                        if (b.State == BallState.Cool && b.CdMax > 0)
                            g.Arc(x + slot / 2, y, slot / 2 - 3, 2.5f, Mathf.PI / 2, Mathf.PI / 2 + Mathf.PI * 2 * (1 - b.Cd / b.CdMax), Col.Hex(0xffe14d, 0.8f));
                    }
                    Pips(x + slot / 2, y + slot / 2 + 2, b.Fused != null ? -1 : b.Level);
                }
                float iy = by + slot + 26 + slot / 2 - 4;
                g.RoundRectStroke(x + 4, iy - slot / 2 + 4, slot - 8, slot - 8, 7, 1.5f, new Color32(255, 255, 255, 30));
                if (i < r.Items.Count)
                {
                    var it = r.Items[i];
                    Icons.Item(g, x + slot / 2, iy, slot / 2 - 7, it.Def.Color, it.Def.Glyph);
                    Pips(x + slot / 2, iy + slot / 2 - 1, it.Level);
                }
            }
            float bx = 52 + Run.Slots * (slot + 8) + 4, byy = by + 2 + slot / 2 - 8;
            g.Circle(bx + 8, byy, 7, Col.Hex(0xe8ecff));

            g.Cur = Layer.HudText;
            g.Text("HP " + Mathf.Max(0, p.Hp) + " / " + p.MaxHp, 8 + bw * 0.31f, top + 15, 13, Col.White);
            g.Text(r.Gold.ToString(), 8 + bw * 0.62f + 26, top + 15, 14, Col.Gold, Align.Left);
            g.Text("Lv " + p.Level, 14, top + 38, 10, Col.White, Align.Left);
            g.Text(p.Xp + " / " + need, W - 14, top + 38, 10, Col.White, Align.Right);
            string tl;
            if (r.Boss != null) tl = r.Boss.BossDef.Final ? "FINAL BOSS" : "BOSS FIGHT";
            else if (r.FinalStarted) tl = "The deep stirs...";
            else
            {
                int nextBoss = 0;
                foreach (var t in r.L.BossTurns) if (t > r.Turn) { nextBoss = t; break; }
                tl = "Turn " + r.Turn + (nextBoss > 0 ? "  ·  boss at " + nextBoss : "  ·  final in " + Mathf.CeilToInt(r.TimeToFinal) + "s");
            }
            g.Text(tl, 10, top + 58, 12, Col.Text, Align.Left);
            g.Text("Pressure ×" + r.Ddm.ToString("0.00"), W - 10, top + 58, 12, r.Ddm > 1.01f ? Col.Hex(0xff9f6b) : Col.Dim, Align.Right);
            if (boss != null && !boss.Dead && boss.Brain != null && !boss.Brain.Descending)
                g.Text(boss.Name + "  " + Mathf.Max(0, Mathf.CeilToInt(boss.Hp)), v.Ox + r.Bw * v.S / 2, v.Oy + 12, 9, Col.White);
            g.Text("BALLS", 10, by + 4, 10, Col.Dim, Align.Left);
            g.Text("ITEMS", 10, by + slot + 30, 10, Col.Dim, Align.Left);
            g.Text("×" + r.Babies, bx + 19, byy + 1, 12, Col.Text, Align.Left);
            if (BotOn) g.Text("AUTO", W / 2, top + 58, 11, Col.Hex(0x6dff8e));

            g.Cur = Layer.Hud;
            if (p.Points > 0 && r.Mode == Mode.Playing && !BotOn)
                if (ui.Button("lvl", g.W - 96, by + slot + 10, 88, 32, "+" + p.Points + " LEVEL", accent: true, small: true, pulse: true)) r.OpenLevelUp();

            // banner
            if (r.Banner != null)
            {
                var bm = r.Banner;
                float a = Mathf.Min(1, Mathf.Min(bm.T * 2, (2.6f - bm.T) * 4));
                float yb = v.Oy + r.Bh * v.S * 0.36f;
                bool sub = !string.IsNullOrEmpty(bm.Sub);
                g.Rect(0, yb, g.W, sub ? 58 : 40, new Color32(0, 0, 0, (byte)(128 * a)));
                g.Cur = Layer.HudText;
                g.Text(bm.Title, g.W / 2, yb + 20, 22, Col.Hex(bm.Color, a));
                if (sub) g.Text(bm.Sub, g.W / 2, yb + 44, 12, Col.A(Col.Text, a), Align.Center, false);
                g.Cur = Layer.Hud;
            }
        }

        void Pips(float x, float y, int lv)
        {
            var prev = g.Cur;
            if (lv < 0)
            {
                g.Cur = (Layer)(((int)prev & ~1) + 1);
                g.Text("FUSED", x, y + 2, 9, Col.Accent);
                g.Cur = prev;
                return;
            }
            for (int i = 0; i < 3; i++) g.Circle(x - 8 + i * 8, y + 2, 2.6f, i < lv ? Col.Hex(0xffe14d) : new Color32(255, 255, 255, 46));
        }

        // ------------------------------------------------------------ in-run menus
        public Nav Menus(Run r)
        {
            g.Cur = Layer.Menu;
            float W = g.W, H = g.H, pw = Mathf.Min(W - 24, 440), px = (W - pw) / 2;
            if (r.Mode == Mode.LevelUp && r.Menu != null)
            {
                ui.Dim();
                float ch = 116, total = r.Menu.Options.Count * (ch + 10);
                float y0 = Mathf.Max(SafeTop + 80, (H - total) / 2 - 20);
                Text(() => ui.Heading(y0 - 46, "LEVEL UP", "Level " + r.P.Level + " · choose one", Col.Accent));
                for (int i = 0; i < r.Menu.Options.Count; i++)
                {
                    var info = r.Info(r.Menu.Options[i]);
                    if (CardT("pick", i, px, y0 + i * (ch + 10), pw, ch, new Ui.CardInfo { Title = info.Title, Tag = info.Tag, Desc = info.Desc, Color = info.Color, Glyph = info.Glyph, Item = info.IsItem }))
                    { r.ChooseLevel(i); return Nav.None; }
                }
                float by = y0 + total + 4; int cost = r.RerollCost;
                if (BtnT("reroll", px, by, pw / 2 - 5, 42, "Reroll · " + cost + "g", disabled: r.Gold < cost)) r.RerollLevel();
                if (BtnT("later", px + pw / 2 + 5, by, pw / 2 - 5, 42, "Later")) r.LaterLevel();
            }
            else if (r.Mode == Mode.Fuser && r.Menu != null)
            {
                ui.Dim();
                Fuser(r, px, pw);
            }
            else if (r.Mode == Mode.Paused)
            {
                ui.Dim();
                Text(() => ui.Heading(H * 0.3f, "PAUSED", r.L.Name + " · turn " + r.Turn + " · " + r.Hero.Name, Col.Accent));
                if (BtnT("resume", px + 30, H * 0.3f + 60, pw - 60, 48, "Resume", accent: true)) r.Resume();
                if (BtnT("restart", px + 30, H * 0.3f + 120, pw - 60, 44, "Restart run")) return Nav.Restart;
                if (BtnT("quit", px + 30, H * 0.3f + 174, pw - 60, 44, "Change hero")) return Nav.Heroes;
                g.Cur = Layer.MenuText;
                var lines = g.Wrap("Drag anywhere to move. Balls fire on their own at the nearest threat; tap a foe to focus it. Catch falling balls to throw them again sooner.", 12, pw - 40);
                for (int i = 0; i < lines.Count; i++) g.Text(lines[i], W / 2, H * 0.3f + 250 + i * 16, 12, Col.Dim, Align.Center, false);
                g.Cur = Layer.Menu;
            }
            else if (r.IsOver)
            {
                ui.Dim();
                bool win = r.Mode == Mode.Victory;
                Text(() => ui.Heading(H * 0.22f, win ? "PIT CLEARED" : "KNOCKED OUT",
                    win ? "The Mawking is silent. " + r.Hero.Name + " climbs out." : "The pit claims " + r.Hero.Name + ".", win ? Col.Hex(0xffe14d) : Col.Hex(0xff6b6b)));
                var rows = new List<string[]>
                {
                    new[] { "Turn reached", r.Turn.ToString() },
                    new[] { "Bosses broken", r.Stats.BossesKilled + " / 3" },
                    new[] { "Level", r.P.Level.ToString() },
                    new[] { "Foes defeated", r.Kills.ToString() },
                    new[] { "Damage dealt", r.Stats.Dmg.ToString() },
                    new[] { "Fusers opened", r.Stats.Fusers.ToString() },
                    new[] { "Board clears", r.Stats.Clears.ToString() },
                    new[] { "Time", Mathf.FloorToInt(r.Time / 60) + "m " + Mathf.FloorToInt(r.Time % 60) + "s" },
                };
                g.Cur = Layer.MenuText;
                for (int i = 0; i < rows.Count; i++)
                {
                    float y = H * 0.22f + 64 + i * 24;
                    g.Text(rows[i][0], px + 40, y, 14, Col.Dim, Align.Left, false);
                    g.Text(rows[i][1], px + pw - 40, y, 14, Col.Text, Align.Right, false);
                }
                g.Cur = Layer.Menu;
                float by = H * 0.22f + 64 + rows.Count * 24 + 20;
                if (BtnT("again", px + 30, by, pw - 60, 50, "Play again", accent: true)) return Nav.Again;
                if (BtnT("heroes", px + 30, by + 60, pw - 60, 44, "Change hero")) return Nav.Heroes;
            }
            return Nav.None;
        }

        void Fuser(Run r, float px, float pw)
        {
            float H = g.H, W = g.W;
            var m = r.Menu;
            if (m.View == FuserView.Main)
            {
                var pairs = r.ComboPairs();
                int pairCount = 0;
                foreach (var p in pairs) if (p[0].Uid < p[1].Uid) pairCount++;
                var recs = r.EvolveRecipes();
                float y0 = Mathf.Max(SafeTop + 110, H / 2 - 220), ch = 112;
                Text(() => ui.Heading(y0 - 50, "FUSER", "Pick one. Combine and Evolve need Lv 3 pieces.", Col.Accent));
                if (CardT("fz-free", 0, px, y0, pw, ch, new Ui.CardInfo { Title = "Free Upgrades", Tag = "ALWAYS", Color = 0x6dff8e, Glyph = "arrow", Item = true,
                    Desc = "Level up 1–5 random balls or items you own. A stingy roll is made up for next time." })) { r.FuserFree(); return; }
                if (CardT("fz-combo", 0, px, y0 + ch + 10, pw, ch, new Ui.CardInfo { Title = "Combine", Tag = pairCount > 0 ? pairCount + " PAIR" + (pairCount > 1 ? "S" : "") + " READY" : "NEEDS TWO LV 3 BALLS",
                    Color = 0xffb347, Glyph = "cross", Item = true,
                    Desc = "Fuse two maxed balls: one keeps every effect of both and frees a slot. The result can no longer grow." }, pairCount == 0))
                { m.View = FuserView.Combo; m.Host = null; return; }
                if (CardT("fz-evo", 0, px, y0 + (ch + 10) * 2, pw, ch, new Ui.CardInfo { Title = "Evolve", Tag = recs.Count > 0 ? recs.Count + " RECIPE" + (recs.Count > 1 ? "S" : "") + " READY" : "NO RECIPE READY",
                    Color = 0xc58bff, Glyph = "crown", Item = true,
                    Desc = "Turn a matching set of Lv 3 pieces into a new, stronger form that starts at Lv 1 and keeps growing." }, recs.Count == 0))
                { m.View = FuserView.Evo; return; }
                bool canFuse = pairCount > 0 || recs.Count > 0;
                if (BtnT("fz-skip", px, y0 + (ch + 10) * 3 + 4, pw, 40, canFuse ? "Walk away · +50 gold" : "Walk away")) { r.FuserAbandon(); return; }
                var book = new List<string>();
                foreach (var kv in Content.Balls)
                    if (kv.Value.Recipe != null && kv.Value.Recipe.Length == 2)
                        book.Add(Content.Balls[kv.Value.Recipe[0]].Name + "+" + Content.Balls[kv.Value.Recipe[1]].Name + "→" + kv.Value.Name);
                g.Cur = Layer.MenuText;
                var lines = g.Wrap("Recipes: " + string.Join("   ", book), 11, pw, false);
                for (int i = 0; i < lines.Count && i < 7; i++) g.Text(lines[i], W / 2, y0 + (ch + 10) * 3 + 66 + i * 14, 11, Col.Dim, Align.Center, false);
                g.Cur = Layer.Menu;
            }
            else if (m.View == FuserView.Combo)
            {
                float y = Mathf.Max(SafeTop + 110, H / 2 - 230);
                Text(() => ui.Heading(y - 50, "COMBINE", m.Host != null ? "Now pick the ball to absorb" : "Pick the ball that stays", Col.Accent));
                var pairs = r.ComboPairs();
                var list = new List<OwnedBall>();
                foreach (var p in pairs)
                {
                    var b = m.Host != null ? (p[0] == m.Host ? p[1] : null) : p[0];
                    if (b != null && !list.Contains(b)) list.Add(b);
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var b = list[i];
                    if (CardT("fz-ball", b.Uid, px, y + i * 96, pw, 88, new Ui.CardInfo { Title = b.Def.Name, Tag = "Lv 3", Color = b.Def.Color, Glyph = b.Def.Glyph, Desc = Run.DescribeBall(b.Stats) }, false, 2))
                    {
                        if (m.Host == null) m.Host = b;
                        else r.FuserCombine(m.Host, b);
                        return;
                    }
                }
                if (BtnT("fz-back", px, y + list.Count * 96 + 6, pw, 40, "Back")) { if (m.Host != null) m.Host = null; else m.View = FuserView.Main; }
            }
            else if (m.View == FuserView.Evo)
            {
                float ye = Mathf.Max(SafeTop + 110, H / 2 - 200);
                Text(() => ui.Heading(ye - 50, "EVOLVE", "The pieces are consumed", Col.Hex(0xc58bff)));
                var recs = r.EvolveRecipes();
                for (int i = 0; i < recs.Count; i++)
                {
                    var rec = recs[i];
                    string name, glyph, desc, parts = ""; uint col;
                    if (rec.IsBall)
                    {
                        var d = Content.Balls[rec.Id]; name = d.Name; glyph = d.Glyph; col = d.Color; desc = Run.DescribeBall(Run.BallStats(rec.Id, 1));
                        foreach (var p in rec.BallParts) parts += (parts.Length > 0 ? " + " : "") + p.Def.Name;
                    }
                    else
                    {
                        var d = Content.Items[rec.Id]; name = d.Name; glyph = d.Glyph; col = d.Color; desc = r.DescribeItem(rec.Id, 1);
                        foreach (var p in rec.ItemParts) parts += (parts.Length > 0 ? " + " : "") + p.Def.Name;
                    }
                    if (CardT("fz-recipe", i, px, ye + i * 112, pw, 104, new Ui.CardInfo { Title = name, Tag = parts, Color = col, Glyph = glyph, Item = !rec.IsBall, Desc = desc }, false, 3))
                    { r.FuserEvolve(rec); return; }
                }
                if (BtnT("fz-back", px, ye + recs.Count * 112 + 6, pw, 40, "Back")) m.View = FuserView.Main;
            }
            else if (m.View == FuserView.Result)
            {
                float yr = H * 0.32f;
                Text(() => ui.Heading(yr - 40, m.Title ?? "", "", Col.Hex(0x6dff8e)));
                int line = 0;
                g.Cur = Layer.MenuText;
                if (m.Results != null)
                    foreach (var res in m.Results)
                    {
                        float px2 = res.Small ? 12 : 16;
                        foreach (var l in g.Wrap(res.Text, px2, pw - 30, !res.Small)) { g.Text(l, W / 2, yr + 10 + line * 22, px2, Col.Hex(res.Color), Align.Center, !res.Small); line++; }
                    }
                g.Cur = Layer.Menu;
                if (BtnT("fz-done", px + 30, yr + 30 + line * 22, pw - 60, 48, "Continue", accent: true)) r.CloseMenu();
            }
        }

        // Shapes and their labels sit on the same pair of layers; these helpers keep that straight.
        void Text(System.Action a) { var prev = g.Cur; g.Cur = (Layer)(((int)prev & ~1) + 1); a(); g.Cur = prev; }

        bool BtnT(string id, float x, float y, float w, float h, string label, bool accent = false, bool disabled = false)
        {
            // the label must go to the text layer above the button's shape layer
            bool clicked = ui.Button(id, x, y, w, h, null, accent, disabled);
            var prev = g.Cur; g.Cur = (Layer)(((int)prev & ~1) + 1);
            g.Text(label, x + w / 2, y + h / 2, 15, disabled ? Col.Dim : accent ? Col.Hex(0x2a1600) : Col.Text);
            g.Cur = prev;
            return clicked;
        }

        bool CardT(string id, int data, float x, float y, float w, float h, Ui.CardInfo info, bool disabled = false, int maxLines = 4)
        {
            return ui.Card(id, data, x, y, w, h, info, disabled, maxLines);
        }

        // ------------------------------------------------------------ title and hero select
        public Nav Title(float t)
        {
            g.Cur = Layer.Menu;
            float W = g.W, H = g.H;
            for (int i = 0; i < 7; i++)
            {
                float ph = t * (0.6f + i * 0.13f) + i;
                float x = W * (0.12f + 0.76f * Mathf.Abs(Mathf.Repeat(ph * 0.37f, 2) - 1)), y = H * (0.15f + 0.5f * Mathf.Abs(Mathf.Repeat(ph * 0.29f, 2) - 1));
                var d = Content.Balls[Content.BaseBalls[i % Content.BaseBalls.Count]];
                g.Circle(x, y, 8, Col.Hex(d.Color, 0.5f));
            }
            float pw = Mathf.Min(W - 48, 360);
            bool go = BtnT("start", (W - pw) / 2, H * 0.58f, pw, 54, "Descend", accent: true);
            g.Cur = Layer.MenuText;
            float big = Mathf.Min(44, W * 0.11f);
            g.Text("RICOCHET", W / 2, H * 0.3f, big, Col.Accent);
            g.Text("DEPTHS", W / 2, H * 0.3f + Mathf.Min(46, W * 0.115f), big, Col.Text);
            g.Text("A bouncing-ball roguelite", W / 2, H * 0.3f + 86, 13, Col.Dim, Align.Center, false);
            var lines = g.Wrap("Drag to move · balls fire on their own · tap a foe to focus it · catch returning balls · break two wardens, then the Mawking.", 12, pw);
            for (int i = 0; i < lines.Count; i++) g.Text(lines[i], W / 2, H * 0.58f + 84 + i * 17, 12, Col.Dim, Align.Center, false);
            g.Cur = Layer.Menu;
            return go ? Nav.Start : Nav.None;
        }

        public string HeroSelect(ref Nav nav)
        {
            g.Cur = Layer.Menu;
            float W = g.W, H = g.H;
            float pw = Mathf.Min(W - 20, 460), px = (W - pw) / 2;
            int cols = 2;
            float gap = 8, cw = (pw - gap) / cols;
            float top = SafeTop + 64;
            float ch = Mathf.Min(200, (H - top - SafeBottom - 70 - gap * 2) / 3);
            string picked = null;
            for (int i = 0; i < Content.Heroes.Count; i++)
            {
                var h = Content.Heroes[i];
                float x = px + (i % cols) * (cw + gap), y = top + (i / cols) * (ch + gap);
                bool clicked = ui.Area("hero", x, y, cw, ch, i);
                g.Cur = Layer.Menu;
                g.RoundRect(x, y, cw, ch, 12, Col.Panel);
                g.RoundRectStroke(x, y, cw, ch, 12, 2, Col.Hex(h.Color));
                // portrait
                float pr = Mathf.Min(16, ch * 0.1f), cx = x + 12 + pr, cy = y + 12 + pr;
                g.Circle(cx, cy, pr, Col.Hex(0xffd29a));
                g.Pie(cx, cy - pr * 0.25f, pr, 0, Mathf.PI, Col.Hex(h.Color));
                g.Circle(cx - pr * 0.3f, cy + pr * 0.1f, pr * 0.12f, Col.Ink);
                g.Circle(cx + pr * 0.3f, cy + pr * 0.1f, pr * 0.12f, Col.Ink);
                var sb = Content.Balls[h.StartBall];
                Icons.Ball(g, x + cw - 12 - pr * 0.8f, cy, pr * 0.8f, sb.Color, sb.Glyph);
                // stat bars
                float sy = y + 24 + pr * 2, bh = 5, bwid = cw - 60;
                for (int s = 0; s < 6; s++)
                {
                    float yy = sy + s * (bh + 4);
                    g.RoundRect(x + 48, yy, bwid, bh, 2.5f, new Color32(0, 0, 0, 115));
                    g.RoundRect(x + 48, yy, bwid * h.Stats[s] / 8f, bh, 2.5f, Col.Hex(h.Color, 0.85f));
                }
                g.Cur = Layer.MenuText;
                g.Text(h.Name, cx + pr + 8, cy - 6, 14, Col.Text, Align.Left);
                g.Text(h.Title, cx + pr + 8, cy + 9, 10, Col.Dim, Align.Left, false);
                for (int s = 0; s < 6; s++)
                {
                    float yy = sy + s * (bh + 4) + bh / 2;
                    g.Text(Content.StatShort[s], x + 10, yy, 8, Col.Dim, Align.Left);
                    g.Text(h.Stats[s].ToString(), x + cw - 8, yy, 8, Col.Text, Align.Right);
                }
                float ty = sy + 6 * (bh + 4) + 8;
                var lines = g.Wrap(sb.Name + " ball. " + h.TwistText, 10, cw - 20, false);
                int maxL = Mathf.Max(1, Mathf.FloorToInt((y + ch - ty - 4) / 12));
                for (int k = 0; k < lines.Count && k < maxL; k++) g.Text(lines[k], x + 10, ty + k * 12, 10, Col.Hex(0xcfd6ff), Align.Left, false);
                g.Cur = Layer.Menu;
                if (clicked) picked = h.Id;
            }
            g.Cur = Layer.MenuText;
            g.Text("CHOOSE YOUR DIVER", W / 2, SafeTop + 30, 20, Col.Accent);
            g.Text("Each one starts with a different ball and plays by a different rule", W / 2, SafeTop + 50, 11, Col.Dim, Align.Center, false);
            g.Cur = Layer.Menu;
            if (BtnT("back", px, H - SafeBottom - 56, pw, 44, "Back")) nav = Nav.Quit;
            if (picked != null) nav = Nav.PickHero;
            return picked;
        }
    }
}
