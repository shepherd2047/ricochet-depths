// HUD, in-run menus (level-up, crucible, pause, results), the boss banner, floating numbers and the title and
// hero-pick screens, laid out from pit-ui/LAYOUT-TABLES.md and drawn with the pit-ui atlas (UiSkin) and Pixelify Sans.
// Coordinates in the tables are art px on a 180x320 grid; 1 art px is 2 design units here. Each element is placed by
// its group (UI-SPEC §4): the top group hangs from the safe-area top, the bottom group sits on the safe-area bottom,
// and everything else is centred on the middle of the safe area. Extra width is margin on both sides.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public enum Nav { None, Start, PickHero, Again, Heroes, Restart, Quit }

    public sealed class Screens
    {
        const float A = Gfx.Art;

        readonly Gfx g;
        readonly Ui ui;
        public float SafeTop, SafeBottom;
        public float SpeedSetting = 1;
        public bool BotOn;
        /// <summary>Set by the host: the 3D board (hero preview, projecting damage numbers) and the sound toggles.</summary>
        public Board3D Board;
        public AudioBank Audio;
        public string SelHero = "tinker";
        /// <summary>Set by the host when ads are on (ads.md); null hides every ad button.</summary>
        public IAdService Ads;
        public AdConfig AdCfg = AdConfig.Default;

        // HUD animation state
        float lastTime, xpShown, hpLag = 1, hpHold;
        int lastLevel = -1;
        Run lastRun;
        OwnedBall partner;
        float confirmRestart = -10, confirmQuit = -10;

        public static readonly Color32 Cream = Col.Hex(0xf2eee4), Body = Col.Hex(0xd9d4c7), Muted = Col.Hex(0x8fb0b8),
            Faint = Col.Hex(0x5e7f89), Copper = Col.Hex(0xd98841), CopperHi = Col.Hex(0xf2b872), Gold = Col.Hex(0xe6ca5c),
            RedHi = Col.Hex(0xff7a7a), Ink = Col.Hex(0x0b0f11);

        public Screens(Gfx gfx, Ui u) { g = gfx; ui = u; }

        // ------------------------------------------------------------ layout groups
        float X(float ax) { return Mathf.Round((g.W - 180 * A) / 2 / A) * A + ax * A; }
        float TY(float ay) { return SafeTop + (ay - 22) * A; }
        float BY(float ay) { return g.H - SafeBottom - (303 - ay) * A; }
        // the design frame's safe area runs 22..303 art px, whose middle is 162.5: y 160 sits 2.5 px above it
        float MY(float ay) { return Mathf.Round(((SafeTop + g.H - SafeBottom) / 2 - 2.5f * A) / A) * A + (ay - 160) * A; }

        /// <summary>Top of the board band: under the HUD rows and the boss bar (top group, y 88).</summary>
        public float HudTop { get { return TY(88); } }
        /// <summary>Height kept clear at the bottom: the HP bar and the slot strip (bottom group, from y 260).</summary>
        public float HudBottom { get { return SafeBottom + (303 - 258) * A; } }

        bool SoundOn { get { return Audio == null || Audio.SoundOn; } }
        bool MusicOn { get { return Audio == null || Audio.MusicOn; } }

        // ------------------------------------------------------------ widgets
        void T(string s, float x, float y, float w, float h, float pt, Color32 c, Align al = Align.Center, bool bold = false, bool shadow = true)
        {
            float tx = al == Align.Left ? x : al == Align.Right ? x + w : x + w / 2;
            if (shadow) g.TextS(s, tx, y + h / 2, pt, c, al, bold);
            else g.Text(s, tx, y + h / 2, pt, c, al, bold);
        }

        /// <summary>Wrapped text from the top of the box, 115 % line height, cut to maxLines. Returns its height.</summary>
        float Para(string s, float x, float y, float w, float pt, Color32 c, int maxLines, Align al = Align.Left, bool bold = false)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var lines = g.Wrap(s, pt, w, bold);
            // long text steps down to 8 pt before it is cut, so card descriptions keep their numbers
            while (lines.Count > maxLines && pt > 8) { pt--; lines = g.Wrap(s, pt, w, bold); }
            float lh = pt * 1.15f;
            int n = Mathf.Min(lines.Count, maxLines);
            for (int i = 0; i < n; i++) T(lines[i], x, y + i * lh, w, lh, pt, c, al, bold);
            return n * lh;
        }

        void Dim(float a) { if (!g.Skin("white", -10, -10, g.W + 20, g.H + 20, Col.Hex(0x06080a, a))) g.Rect(-10, -10, g.W + 20, g.H + 20, Col.Hex(0x06080a, a)); }

        void Ribbon(float ax, float aw, string label)
        {
            g.Skin("ribbon", X(ax), MY(44), aw * A, 20 * A, Col.White);
            T(label, X(ax), MY(44), aw * A, 17 * A, 16, Cream, Align.Center, true);
        }

        /// <summary>
        /// A pit-ui button (UI-SPEC §7.1): the face sits 1 px above the lip, and on press the sprite drops and the label
        /// with it by 2 art px. cost puts the label left with a coin and a right-aligned number. inert draws it but
        /// takes no taps (HUD buttons under a modal).
        /// </summary>
        bool Btn(string id, float x, float y, float w, float h, string label, float pt, bool primary = false, string icon = null,
            bool disabled = false, bool inert = false, string digits = null, string cost = null, int data = 0, Color32? iconTint = null)
        {
            bool held = !disabled && !inert && ui.Held(id, data);
            string spr = disabled ? "btn_disabled" : (primary ? "btn_primary" : "btn_secondary") + (held ? "_pressed" : "");
            if (!g.Skin(spr, x, y, w, h, Col.White)) g.RoundRect(x, y, w, h, 4, primary ? Copper : Col.Panel2);
            float dy = held ? 2 * A : 0, cy = y + (h - 2 * A) / 2 + dy;
            var tc = disabled ? Faint : Cream;
            if (icon != null) g.SkinAt(icon, x + w / 2 - 8 * A, cy - 8 * A, 1, disabled ? Col.A(Col.White, 0.4f) : iconTint ?? Col.White);
            if (digits != null) g.Digits("hud", digits, x + w / 2, cy - 4 * A, 1, disabled ? Col.A(Col.White, 0.5f) : Col.White, Align.Center);
            if (cost != null)
            {
                if (label != null) T(label, x + 6 * A, cy - h / 2 + A, w, h - 2 * A, pt, tc, Align.Left, true);
                g.SkinAt("ic_coin_s", x + w - 32 * A, y + 7 * A + dy, 1, disabled ? Col.A(Col.White, 0.4f) : Col.White);
                g.Digits("hud", cost, x + w - 7 * A, y + 8 * A + dy, 1, disabled ? Col.A(Col.White, 0.5f) : Col.White, Align.Right);
            }
            else if (label != null) T(label, x, cy - (h - 2 * A) / 2, w, h - 2 * A, pt, tc, Align.Center, true);
            return !inert && ui.Area(id, x, y, w, h, data, disabled);
        }

        /// <summary>
        /// A rewarded-ad button: the play icon marks it as an ad, the label sits right of it. Greyed with "Ad not
        /// available" under it when no ad can load (ads.md §3).
        /// </summary>
        bool AdBtn(string id, string placement, float x, float y, float w, float h, string label, float pt, bool primary = false, bool noteLeft = false)
        {
            bool ready = Ads != null && Ads.RewardedReady(placement);
            bool held = ready && ui.Held(id);
            bool hit = Btn(id, x, y, w, h, null, 0, primary, disabled: !ready);
            float dy = held ? 2 * A : 0, cy = y + (h - 2 * A) / 2 + dy;
            g.SkinAt("ic_play", x + 4 * A, cy - 8 * A, 1, ready ? Col.White : Col.A(Col.White, 0.4f));
            T(label, x + 20 * A, cy - (h - 2 * A) / 2, w - 22 * A, h - 2 * A, pt, ready ? Cream : Faint, Align.Center, true);
            if (!ready && noteLeft) T("Ad not available", x - 64 * A, y + (h - 9 * A) / 2, 62 * A, 9 * A, 9, Faint, Align.Right);
            else if (!ready) T("Ad not available", x - 20 * A, y + h + A, w + 40 * A, 9 * A, 9, Faint);
            return hit && ready;
        }

        /// <summary>A ball icon: the atlas sprite if pit-ui drew one for it, else the procedural one. True for a sprite.</summary>
        bool BallIcon(string id, float x, float y, float size, string fused = null)
        {
            var spr = UiSkin.Ball(id);
            if (spr != null) { g.Skin(spr, x, y, size, size, Col.White); return true; }
            BallDef d;
            if (id == null || !Content.Balls.TryGetValue(id, out d)) return false;
            uint c2 = 0; BallDef fd;
            if (fused != null && Content.Balls.TryGetValue(fused, out fd)) c2 = fd.Color;
            Icons.Ball(g, x + size / 2, y + size / 2, size * 0.42f, d.Color, d.Glyph, c2);
            return false;
        }

        bool ItemIcon(string id, float x, float y, float size)
        {
            var spr = UiSkin.Item(id);
            if (spr != null) { g.Skin(spr, x, y, size, size, Col.White); return true; }
            ItemDef d;
            if (id == null || !Content.Items.TryGetValue(id, out d)) return false;
            Icons.Item(g, x + size / 2, y + size / 2, size * 0.4f, d.Color, d.Glyph);
            return false;
        }

        /// <summary>Darkens a rect; over a procedural icon it has to go on the shape batch, which draws above the skin.</summary>
        void Shade(float x, float y, float w, float h, float a, bool overShapes)
        {
            var c = Col.Hex(0x06080a, a);
            if (overShapes || !g.Skin("white", x, y, w, h, c)) g.Rect(x, y, w, h, c);
        }

        void Pips(float x, float y, int on, int next)
        {
            for (int i = 0; i < 3; i++)
                g.SkinAt(i < on ? "pip_on" : i < on + next ? "pip_next" : "pip_off", x + i * 7 * A, y, 1, Col.White);
        }

        void Bar(string fill, float x, float y, float w, float h, float k)
        {
            g.Skin("bar_frame", x, y, w, h, Col.White);
            float fw = Mathf.Round((w / A - 4) * Mathf.Clamp01(k)) * A;
            if (fw >= 2 * A) g.Skin(fill, x + 2 * A, y + 2 * A, fw, h - 4 * A, Col.White);
        }

        static string Short(long n)
        {
            if (n >= 1000000) return (n / 1000000f).ToString("0.0") + "kk";
            if (n >= 10000) return (n / 1000f).ToString("0.0") + "k";
            return n.ToString();
        }

        /// <summary>True for strings the digit fonts can show (damage, heals); words stay in the world-space pixel font.</summary>
        public static bool IsNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool digit = false;
            foreach (char ch in s)
            {
                if (ch >= '0' && ch <= '9') digit = true;
                else if ("+-/x.!:%k".IndexOf(ch) < 0) return false;
            }
            return digit;
        }

        // ------------------------------------------------------------ HUD
        public void Hud(Run r, WorldView v, ref bool pauseClicked)
        {
            float dt = Mathf.Clamp(g.Time - lastTime, 0, 0.1f); lastTime = g.Time;
            if (r != lastRun) { lastRun = r; xpShown = 0; hpLag = 1; hpHold = 0; lastLevel = -1; partner = null; }
            g.Cur = Layer.Hud;
            var p = r.P;
            bool modal = r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser || r.Mode == Mode.Paused || r.Mode == Mode.Revive || r.IsOver;

            FloatNumbers(r);

            // row 1: pause, speed, gold
            if (Btn("pause", X(4), TY(24), 24 * A, 24 * A, null, 0, icon: "ic_pause", inert: modal)) pauseClicked = true;
            string sp = SpeedSetting >= 4 ? "x4" : SpeedSetting >= 3 ? "x3" : SpeedSetting >= 2 ? "x2" : SpeedSetting >= 1.5f ? "x1.5" : "x1";   // x3 / x4 from the dev panel
            if (Btn("speed", X(30), TY(24), 24 * A, 24 * A, null, 0, digits: sp, inert: modal))
                SpeedSetting = SpeedSetting >= 2 ? 1 : SpeedSetting >= 1.5f ? 2 : 1.5f;
            g.Skin("inset", X(58), TY(28), 52 * A, 16 * A, Col.White);
            g.SkinAt("ic_coin_s", X(61), TY(31), 1, Col.White);
            g.Digits("hud", r.Gold.ToString(), X(106), TY(32), 1, Col.White, Align.Right);

            // row 2: level, xp (eased; snaps back on level-up), turn text or the boss bar
            g.SkinAt("badge_level", X(4), TY(50), 1, Col.White);
            g.Digits("hud", p.Level.ToString(), X(13), TY(55), 1, Col.White, Align.Center);
            float xk = (float)p.Xp / Mathf.Max(1, M.XpNeed(p.Level));
            if (p.Level != lastLevel) { xpShown = lastLevel < 0 ? xk : 0; lastLevel = p.Level; }
            xpShown = xk < xpShown ? xk : Mathf.MoveTowards(xpShown, xk, dt * 4);
            Bar("bar_xp", X(24), TY(54), 152 * A, 10 * A, xpShown);
            var boss = r.Boss;
            if (boss != null && !boss.Dead && boss.Brain != null && !boss.Brain.Descending)
            {
                T(boss.Name.ToUpperInvariant(), X(24), TY(66), 152 * A, 9 * A, 9, CopperHi, Align.Center, true);
                Bar("bar_boss", X(24), TY(76), 152 * A, 10 * A, boss.Hp / boss.MaxHp);
            }
            else
            {
                string tl;
                if (r.Boss != null) tl = r.Boss.BossDef.Final ? "Final boss" : "Boss fight";
                else if (r.FinalStarted) tl = "The deep stirs...";
                else
                {
                    int nextBoss = 0;
                    foreach (var t in r.L.BossTurns) if (t > r.Turn) { nextBoss = t; break; }
                    tl = "Turn " + r.Turn + (nextBoss > 0 ? " · Boss at " + nextBoss : " · Final in " + Mathf.CeilToInt(r.TimeToFinal) + "s");
                }
                if (BotOn) tl = "AUTO · " + tl;
                T(tl, X(24), TY(65), 152 * A, 8 * A, 9, Muted, Align.Right);
            }

            // HP: the cream lag bar holds the old value 0.35 s, then shrinks over 0.25 s
            float hk = Mathf.Clamp01((float)Mathf.Max(0, p.Hp) / Mathf.Max(1, p.MaxHp));
            if (hk >= hpLag) { hpLag = hk; hpHold = 0.35f; }
            else if (hpHold > 0) hpHold -= dt;
            else hpLag = Mathf.MoveTowards(hpLag, hk, dt * 4);
            g.SkinAt("ic_heart_s", X(4), BY(263), 1, Col.White);
            g.Skin("bar_frame", X(15), BY(262), 161 * A, 12 * A, Col.White);
            float lw = Mathf.Round(157 * hpLag) * A, fw = Mathf.Round(157 * hk) * A;
            if (lw >= 2 * A) g.Skin("bar_hp_lag", X(17), BY(264), lw, 8 * A, Col.White);
            if (fw >= 2 * A) g.Skin("bar_hp", X(17), BY(264), fw, 8 * A, Col.White);
            g.Digits("hud", Mathf.Max(0, p.Hp) + "/" + p.MaxHp, X(95), BY(265), 1, Col.White, Align.Center);

            // the slot strip goes above the dim while the level-up or crucible modal is open (Menus draws it then)
            if (r.Mode != Mode.LevelUp && r.Mode != Mode.Fuser) Slots(r);

            if (p.Points > 0 && r.Mode == Mode.Playing && !BotOn)
                if (Btn("lvl", X(112), BY(232), 64 * A, 24 * A, "LEVEL +" + p.Points, 11, primary: true)) r.OpenLevelUp();

            Banner(r);
        }

        void Slots(Run r)
        {
            for (int i = 0; i < Run.Slots; i++)
            {
                float x = X(4 + 21 * i), y = BY(278);
                if (i < r.Balls.Count)
                {
                    var b = r.Balls[i];
                    g.Skin(b.Fused != null ? "slot_fused" : "slot_ball", x, y, 20 * A, 20 * A, Col.White);
                    bool spr = BallIcon(b.Id, x + 2 * A, y + 2 * A, 16 * A, b.Fused);
                    if (b.State == BallState.Cool && b.CdMax > 0)
                    {
                        // cooling down: the shade drains from the top as the ball comes back
                        float k = Mathf.Clamp01(b.Cd / b.CdMax), sh = Mathf.Round(16 * k) * A;
                        Shade(x + 2 * A, y + 2 * A, 16 * A, sh, 0.6f, !spr);
                    }
                    else if (b.State != BallState.Ready) Shade(x + 2 * A, y + 2 * A, 16 * A, 16 * A, 0.45f, !spr);
                }
                else g.Skin("slot_empty", x, y, 20 * A, 20 * A, Col.White);
            }
            for (int i = 0; i < Run.Slots; i++)
            {
                float x = X(93 + 21 * i), y = BY(278);
                if (i < r.Items.Count)
                {
                    g.Skin("slot_item", x, y, 20 * A, 20 * A, Col.White);
                    ItemIcon(r.Items[i].Id, x + 2 * A, y + 2 * A, 16 * A);
                }
                else g.Skin("slot_empty", x, y, 20 * A, 20 * A, Col.White);
            }
        }

        /// <summary>
        /// Damage and heal numbers in the pit-ui digit fonts (UI-SPEC §7.7), projected from the board to the screen:
        /// rise with ease-out, fade at the end, crits in the crit font at ×2 (×3 for the first two frames), above the rest.
        /// </summary>
        void FloatNumbers(Run r)
        {
            if (Board == null || !UiSkin.Loaded) return;
            for (int pass = 0; pass < 2; pass++)
                foreach (var t in r.Texts)
                {
                    if (!IsNumber(t.S)) continue;
                    bool crit = t.Size >= 1.35f && t.Color == 0xffe14d;
                    if (crit != (pass == 1)) continue;
                    var s = Board.BoardToScreen(t.X, t.Y);
                    float age = Mathf.Clamp01((0.7f - t.T) / 0.7f), ease = 1 - (1 - age) * (1 - age);
                    float rise = Mathf.Round(ease * (crit ? 14 : 10)) * A, alpha = Mathf.Clamp01(t.T / 0.25f);
                    if (crit)
                    {
                        float mult = 0.7f - t.T < 2 / 60f ? 3 : 2;
                        string str = t.S.EndsWith("!") ? t.S : t.S + "!";
                        g.Digits("crit", str, s.x, s.y - rise - 11 * A * mult / 2, mult, Col.A(Col.White, alpha), Align.Center);
                    }
                    else
                    {
                        // UI-SPEC tints: hero hurt and heals have their own; hits keep the ball's colour
                        uint c = t.Color == 0xff5d5d ? 0xff7a7au : t.Color == 0x6dff8e ? 0xb8f27au : t.Color;
                        g.Digits("dmg", t.S, s.x, s.y - rise - 9 * A / 2, 1, Col.Hex(c, alpha), Align.Center);
                    }
                }
        }

        /// <summary>
        /// The banner (UI-SPEC §7.8), full canvas width: the stripes slide in from both sides over 0.2 s, the text
        /// blinks three times, and the body collapses over the last 0.15 s.
        /// </summary>
        void Banner(Run r)
        {
            var bm = r.Banner;
            if (bm == null) return;
            float el = 2.6f - bm.T, k = Mathf.Clamp01(el / 0.2f), hk = Mathf.Clamp01(bm.T / 0.15f);
            float W = g.W, bh = Mathf.Round(40 * hk) * A, y = MY(132) - bh / 2;
            float slide = (1 - k) * W;
            g.SkinTiled("banner_stripe", -slide, y - 8 * A, W, 8 * A, 0, Col.White);
            g.SkinTiled("banner_stripe", slide, y + bh, W, 8 * A, 0, Col.White);
            if (bh > 0) g.Skin("banner_body", 0, y, W, bh, Col.White);
            bool on = el > 0.95f || el < 0.2f || Mathf.Repeat(el - 0.2f, 0.25f) < 0.15f;
            if (hk >= 1 && k >= 1 && on)
            {
                T(bm.Title, 0, MY(114), W, 20 * A, 24, Col.Hex(bm.Color), Align.Center, true);
                if (!string.IsNullOrEmpty(bm.Sub)) T(bm.Sub.ToUpperInvariant(), 0, MY(134), W, 14 * A, 12, Cream, Align.Center, true);
            }
        }

        // ------------------------------------------------------------ in-run menus
        public Nav Menus(Run r)
        {
            g.Cur = Layer.Menu;
            if (r.Mode == Mode.LevelUp && r.Menu != null) { Dim(0.72f); LevelUp(r); Slots(r); }
            else if (r.Mode == Mode.Fuser && r.Menu != null) { Dim(0.72f); Fuser(r); Slots(r); }
            else if (r.Mode == Mode.Paused) { Dim(0.78f); return Pause(r); }
            else if (r.Mode == Mode.Revive) { Dim(0.80f); Revive(r); }
            else if (r.IsOver) { Dim(0.80f); return End(r); }
            return Nav.None;
        }

        void LevelUp(Run r)
        {
            Ribbon(30, 120, "LEVEL UP!");
            var opts = r.Menu.Options;
            for (int i = 0; i < opts.Count && i < 3; i++)
            {
                var o = opts[i];
                var info = r.Info(o);
                float cx = X(10), cy = MY(66 + 54 * i);
                g.Skin("card", cx, cy, 160 * A, 52 * A, Col.White);
                if (ui.Held("pick", i)) g.Skin("select", cx - 2 * A, cy - 2 * A, 164 * A, 56 * A, Col.White);
                g.Skin(info.IsItem ? "slot_item" : "slot_ball", cx + 7 * A, cy + 10 * A, 36 * A, 36 * A, Col.White);
                string id = o.Id ?? (o.Ball != null ? o.Ball.Id : o.Item != null ? o.Item.Id : null);
                if (info.IsItem) ItemIcon(id, cx + 9 * A, cy + 12 * A, 32 * A);
                else BallIcon(id, cx + 9 * A, cy + 12 * A, 32 * A, o.Ball != null ? o.Ball.Fused : null);
                T(info.Title, cx + 50 * A, cy + 6 * A, 70 * A, 12 * A, 14, Cream, Align.Left, true);
                int cur = o.Ball != null ? o.Ball.Level : o.Item != null ? o.Item.Level : 0;
                Pips(cx + 50 * A, cy + 21 * A, cur, 1);
                bool isNew = o.Kind == OptKind.BallNew || o.Kind == OptKind.ItemNew;
                g.Skin(isNew ? "tag_green" : "tag_gold", cx + 122 * A, cy + 7 * A, 30 * A, 11 * A, Col.White);
                T(isNew ? "NEW" : "LV " + (cur + 1), cx + 122 * A, cy + 7 * A, 30 * A, 10 * A, 9, Ink, Align.Center, true, false);
                Para(info.Desc, cx + 50 * A, cy + 29 * A, 102 * A, 11, Body, 3);
                if (ui.Area("pick", cx, cy, 160 * A, 52 * A, i)) { r.ChooseLevel(i); return; }
            }
            int cost = r.RerollCost;
            // ads.md R2: a free reroll beside the paid one, 3 per run
            bool adReroll = Ads != null && r.AdRerolls < AdCfg.RerollsPerRun;
            if (adReroll && AdBtn("ad-reroll", AdConfig.Reroll, X(8), MY(232), 48 * A, 24 * A, "FREE", 12))
                Ads.ShowRewarded(AdConfig.Reroll, ok => { if (ok && r.Mode == Mode.LevelUp) r.RerollFree(); });
            float rx = adReroll ? 60 : 46, rw = adReroll ? 74 : 88;
            if (Btn("reroll", X(rx), MY(232), rw * A, 24 * A, "REROLL", adReroll ? 12 : 13, cost: cost.ToString(), disabled: r.Gold < cost)) r.RerollLevel();
            // not in the pit-ui mockup: the game lets you bank a level-up for later (the pulsing LEVEL +n button)
            if (Btn("later", X(138), MY(232), 24 * A, 24 * A, null, 0, icon: "ic_clock")) r.LaterLevel();
        }

        /// <summary>A crucible option card (UI-SPEC §7.4); an unavailable one is greyed and takes no taps.</summary>
        bool Option(string id, int row, string title, string desc, bool on, System.Action<float, float> icons, int lines = 3)
        {
            float cx = X(10), cy = MY(66 + 54 * row);
            g.Skin(on ? "card_copper" : "card_disabled", cx, cy, 160 * A, 52 * A, Col.White);
            if (on && ui.Held(id)) g.Skin("select", cx - 2 * A, cy - 2 * A, 164 * A, 56 * A, Col.White);
            g.Skin("inset", cx + 6 * A, cy + 10 * A, 62 * A, 36 * A, Col.White);
            if (icons != null) icons(cx, cy);
            T(title, cx + 74 * A, cy + 7 * A, 80 * A, 12 * A, 13, on ? Cream : Faint, Align.Left, true);
            Para(desc, cx + 74 * A, cy + 21 * A, 80 * A, 10, on ? Body : Faint, lines);
            return ui.Area(id, cx, cy, 160 * A, 52 * A, 0, !on);
        }

        void Lock(float cx, float cy) { g.SkinAt("ic_lock", cx + 29 * A, cy + 20 * A, 1, Col.A(Col.White, 0.5f)); }

        void Fuser(Run r)
        {
            var m = r.Menu;
            if (m.View == FuserView.Main)
            {
                Ribbon(25, 130, "THE CRUCIBLE");
                var pairs = r.ComboPairs();
                OwnedBall[] first = null; int pairCount = 0;
                foreach (var p in pairs) if (p[0].Uid < p[1].Uid) { pairCount++; if (first == null) first = p; }
                var recs = r.EvolveRecipes();
                // ads.md R3: "+1" on the Free Upgrades card adds a piece (count 2-5), once per Crucible, while anything can level
                bool adPlus = Ads != null && !m.ExtraAdUsed && AdCfg.ExtraCardsPerCrucible > 0 && r.FuserCanUpgrade();
                string freeTitle = m.FreeBonus > 0 ? "FREE UPGRADES +1" : "FREE UPGRADES";
                string freeDesc = "Level up " + (1 + m.FreeBonus) + "-5 of your balls and items at random.";
                if (Option("fz-free", 0, freeTitle, freeDesc, true, (cx, cy) =>
                {
                    int n = 0;
                    foreach (var b in r.Balls) { if (n >= 3) break; if (b.Fused == null && b.Level < 3) BallIcon(b.Id, cx + (13 + 16 * n++) * A, cy + 20 * A, 16 * A); }
                    foreach (var it in r.Items) { if (n >= 3) break; if (it.Level < 3) ItemIcon(it.Id, cx + (13 + 16 * n++) * A, cy + 20 * A, 16 * A); }
                }, adPlus ? 2 : 3)) { r.FuserFree(); return; }
                if (adPlus && AdBtn("ad-plus", AdConfig.Crucible, X(10) + 112 * A, MY(66) + 34 * A, 42 * A, 15 * A, "+1", 11, noteLeft: true))
                    Ads.ShowRewarded(AdConfig.Crucible, ok => { if (ok && r.Mode == Mode.Fuser && r.Menu == m) r.FuserAdUpgrade(); });
                if (Option("fz-combo", 1, "COMBINE", pairCount > 0 ? "Merge two Lv 3 balls. The result stops levelling." : "Needs two Lv 3 balls that can merge.",
                    pairCount > 0, (cx, cy) =>
                {
                    if (first == null) { Lock(cx, cy); return; }
                    BallIcon(first[0].Id, cx + 17 * A, cy + 20 * A, 16 * A);
                    g.SkinAt("ic_plus", cx + 31 * A, cy + 20 * A, 1, Col.White);
                    BallIcon(first[1].Id, cx + 42 * A, cy + 20 * A, 16 * A);
                })) { m.View = FuserView.Combo; m.Host = null; partner = null; return; }
                string evoDesc = "No recipe ready. Max the right pieces first.";
                if (recs.Count > 0) evoDesc = RecipeParts(recs[0]) + " become " + RecipeName(recs[0]) + "." + (recs.Count > 1 ? " (+" + (recs.Count - 1) + " more)" : "");
                if (Option("fz-evo", 2, "EVOLVE", evoDesc, recs.Count > 0, (cx, cy) =>
                {
                    if (recs.Count == 0) { Lock(cx, cy); return; }
                    RecipeIcons(recs[0], cx, cy);
                })) { m.View = FuserView.Evo; return; }
                bool canFuse = pairCount > 0 || recs.Count > 0;
                if (Btn("fz-skip", X(46), MY(232), 88 * A, 24 * A, "SKIP", 13, cost: canFuse ? "+50" : null)) { r.FuserAbandon(); return; }
            }
            else if (m.View == FuserView.Combo) Combine(r, m);
            else if (m.View == FuserView.Evo)
            {
                Ribbon(30, 120, "EVOLVE");
                var recs = r.EvolveRecipes();
                for (int i = 0; i < recs.Count && i < 3; i++)
                {
                    var rec = recs[i];
                    string desc = rec.IsBall ? Run.DescribeBall(Run.BallStats(rec.Id, 1)) : r.DescribeItem(rec.Id, 1);
                    float cx = X(10), cy = MY(66 + 54 * i);
                    g.Skin("card_copper", cx, cy, 160 * A, 52 * A, Col.White);
                    if (ui.Held("fz-recipe", i)) g.Skin("select", cx - 2 * A, cy - 2 * A, 164 * A, 56 * A, Col.White);
                    g.Skin("inset", cx + 6 * A, cy + 10 * A, 62 * A, 36 * A, Col.White);
                    RecipeIcons(rec, cx, cy);
                    T(RecipeName(rec).ToUpperInvariant(), cx + 74 * A, cy + 7 * A, 80 * A, 12 * A, 13, Cream, Align.Left, true);
                    Para(desc, cx + 74 * A, cy + 21 * A, 80 * A, 10, Body, 3);
                    if (ui.Area("fz-recipe", cx, cy, 160 * A, 52 * A, i)) { r.FuserEvolve(rec); return; }
                }
                if (Btn("fz-back", X(14), MY(232), 60 * A, 24 * A, "BACK", 13)) m.View = FuserView.Main;
            }
            else if (m.View == FuserView.Result)
            {
                Ribbon(20, 140, string.IsNullOrEmpty(m.Title) ? "DONE" : m.Title.ToUpperInvariant());
                g.Skin("panel", X(14), MY(70), 152 * A, 152 * A, Col.White);
                float y = MY(80);
                if (m.Results != null)
                    foreach (var res in m.Results)
                    {
                        float pt = res.Small ? 10 : 12;
                        y += Para(res.Text, X(22), y, 136 * A, pt, Col.Hex(res.Color), 3, Align.Center, !res.Small) + 2 * A;
                        if (y > MY(214)) break;
                    }
                if (Btn("fz-done", X(30), MY(232), 120 * A, 26 * A, "CONTINUE", 15, primary: true)) r.CloseMenu();
            }
        }

        static string RecipeName(Recipe rec) { return rec.IsBall ? Content.Balls[rec.Id].Name : Content.Items[rec.Id].Name; }

        static string RecipeParts(Recipe rec)
        {
            var names = new List<string>();
            if (rec.IsBall) foreach (var p in rec.BallParts) names.Add(p.Def.Name);
            else foreach (var p in rec.ItemParts) names.Add(p.Def.Name);
            return string.Join(" + ", names);
        }

        void RecipeIcons(Recipe rec, float cx, float cy)
        {
            var ids = new List<string>();
            if (rec.IsBall) foreach (var p in rec.BallParts) ids.Add(p.Id);
            else foreach (var p in rec.ItemParts) ids.Add(p.Id);
            System.Func<string, float, bool> icon = (id, ax) => rec.IsBall ? BallIcon(id, cx + ax * A, cy + 20 * A, 16 * A) : ItemIcon(id, cx + ax * A, cy + 20 * A, 16 * A);
            if (ids.Count > 0) icon(ids[0], 4);
            if (ids.Count > 1) { g.SkinAt("ic_plus", cx + 18 * A, cy + 20 * A, 1, Col.White); icon(ids[1], 29); }
            g.SkinAt("ic_arrow", cx + 43 * A, cy + 20 * A, 1, Col.White);
            icon(rec.Id, 54);
        }

        /// <summary>
        /// UI-SPEC §7.5: four ball slots; the first tap marks the HOST, the second the PARTNER, a tap on a marked ball
        /// unmarks it. Balls that can't take part are dimmed. COMBINE stays disabled until both are picked.
        /// </summary>
        void Combine(Run r, Menu m)
        {
            Ribbon(30, 120, "COMBINE");
            Para("Pick a host, then a partner. Both must be Lv 3.", X(14), MY(70), 152 * A, 10, Body, 2, Align.Center);
            var pairs = r.ComboPairs();
            var host = m.Host;
            if (partner != null && (host == null || !r.Balls.Contains(partner))) partner = null;
            System.Func<OwnedBall, bool> hostable = b => pairs.Exists(p => p[0] == b);
            System.Func<OwnedBall, bool> partnerOk = b => host != null && pairs.Exists(p => p[0] == host && p[1] == b);
            for (int i = 0; i < 4; i++)
            {
                float x = X(12 + 40 * i), y = MY(94);
                if (i >= r.Balls.Count) { g.Skin("slot_empty", x, y, 36 * A, 36 * A, Col.White); continue; }
                var b = r.Balls[i];
                bool can = b == host || b == partner || hostable(b) || partnerOk(b);
                g.Skin(b.Fused != null ? "slot_fused" : "slot_ball", x, y, 36 * A, 36 * A, Col.White);
                bool spr = BallIcon(b.Id, x + 2 * A, y + 2 * A, 32 * A, b.Fused);
                if (b.Fused == null) Pips(X(20 + 40 * i), MY(133), b.Level, 0);
                if (!can) Shade(x, y, 36 * A, 36 * A, 0.55f, !spr);
                if (b == host || b == partner)
                {
                    g.Skin("select", x - 2 * A, y - 2 * A, 40 * A, 40 * A, Col.White);
                    g.Skin(b == host ? "tag_gold" : "tag_green", x, MY(141), 36 * A, 11 * A, Col.White);
                    T(b == host ? "HOST" : "PARTNER", x, MY(141), 36 * A, 10 * A, b == host ? 9 : 8, Ink, Align.Center, true, false);
                }
                if (can && ui.Area("cb-ball", x, y, 36 * A, 36 * A, b.Uid))
                {
                    if (b == host) { m.Host = null; partner = null; }
                    else if (b == partner) partner = null;
                    else if (host == null) m.Host = b;
                    else if (partnerOk(b)) partner = b;
                    else { m.Host = b; partner = null; }
                    return;
                }
            }
            g.Skin("panel", X(14), MY(158), 152 * A, 70 * A, Col.White);
            string desc;
            if (host != null)
            {
                BallIcon(host.Id, X(46), MY(172), 16 * A);
                g.SkinAt("ic_plus", X(66), MY(175), 1, Col.White);
                if (partner != null) BallIcon(partner.Id, X(80), MY(172), 16 * A);
                g.SkinAt("ic_arrow", X(100), MY(176), 1, Col.White);
                BallIcon(host.Id, X(116), MY(172), 16 * A);
                if (partner != null) BallIcon(partner.Id, X(124), MY(176), 16 * A);
                desc = partner != null ? host.Def.Name + " stays Lv 3 and gains every effect of " + partner.Def.Name + ". It can no longer level up."
                    : "Now pick the partner it absorbs.";
            }
            else desc = "Pick the ball that stays.";
            Para(desc, X(22), MY(192), 136 * A, 10, Body, 3, Align.Center);
            if (Btn("cb-back", X(14), MY(232), 60 * A, 24 * A, "BACK", 13)) { m.Host = null; partner = null; m.View = FuserView.Main; return; }
            if (Btn("cb-go", X(80), MY(232), 86 * A, 24 * A, "COMBINE", 13, primary: true, disabled: host == null || partner == null))
            { var pb = partner; partner = null; r.FuserCombine(host, pb); }
        }

        Nav Pause(Run r)
        {
            g.Skin("panel", X(14), MY(54), 152 * A, 238 * A, Col.White);
            Ribbon(40, 100, "PAUSED");
            T("LOADOUT", X(22), MY(70), 136 * A, 10 * A, 10, Muted, Align.Left, true);
            for (int i = 0; i < 4; i++)
            {
                float x = X(23 + 35 * i), y = MY(82);
                if (i < r.Balls.Count)
                {
                    var b = r.Balls[i];
                    g.Skin(b.Fused != null ? "slot_fused" : "slot_ball", x, y, 30 * A, 30 * A, Col.White);
                    BallIcon(b.Id, x + 7 * A, y + 7 * A, 16 * A, b.Fused);
                    if (b.Fused == null) Pips(x + 5 * A, MY(113), b.Level, 0);
                }
                else g.Skin("slot_empty", x, y, 30 * A, 30 * A, Col.White);
                y = MY(124);
                if (i < r.Items.Count)
                {
                    var it = r.Items[i];
                    g.Skin("slot_item", x, y, 30 * A, 30 * A, Col.White);
                    ItemIcon(it.Id, x + 7 * A, y + 7 * A, 16 * A);
                    Pips(x + 5 * A, MY(155), it.Level, 0);
                }
                else g.Skin("slot_empty", x, y, 30 * A, 30 * A, Col.White);
            }
            if (Btn("resume", X(30), MY(166), 120 * A, 26 * A, "RESUME", 15, primary: true)) r.Resume();
            // restart and quit throw the run away, so they ask for a second tap
            bool cr = g.Time - confirmRestart < 2.5f, cq = g.Time - confirmQuit < 2.5f;
            if (Btn("restart", X(30), MY(198), 120 * A, 24 * A, cr ? "TAP AGAIN" : "RESTART", 13))
            { if (cr) { confirmRestart = -10; return Nav.Restart; } confirmRestart = g.Time; confirmQuit = -10; }
            if (Btn("quit", X(30), MY(228), 120 * A, 24 * A, cq ? "TAP AGAIN" : "QUIT TO TITLE", 13))
            { if (cq) { confirmQuit = -10; return Nav.Quit; } confirmQuit = g.Time; confirmRestart = -10; }
            Toggles(X(62), MY(260), X(94), MY(260));
            return Nav.None;
        }

        void Toggles(float sx, float sy, float mx, float my)
        {
            if (Btn("snd", sx, sy, 24 * A, 24 * A, null, 0, icon: SoundOn ? "ic_sound" : "ic_mute") && Audio != null) Audio.SoundOn = !Audio.SoundOn;
            if (Btn("mus", mx, my, 24 * A, 24 * A, null, 0, icon: "ic_music", iconTint: MusicOn ? Col.White : Col.A(Col.White, 0.4f)) && Audio != null)
                Audio.MusicOn = !Audio.MusicOn;
        }

        /// <summary>ads.md R1: the revive prompt (spec §1 state list), once per run.</summary>
        void Revive(Run r)
        {
            T("OUT OF HP", X(10), MY(60), 160 * A, 26 * A, 28, RedHi, Align.Center, true);
            g.Skin("panel", X(14), MY(96), 152 * A, 96 * A, Col.White);
            g.SkinAt("ic_heart_s", X(86), MY(108), 1, Col.White);
            Para("Watch an ad to get back up with " + Mathf.RoundToInt(AdCfg.ReviveHp * 100) + "% HP. The " + AdCfg.ReviveRows +
                 " rows nearest you are cleared.", X(24), MY(124), 132 * A, 12, Body, 4, Align.Center);
            Para("Once per run.", X(24), MY(172), 132 * A, 10, Muted, 1, Align.Center);
            if (AdBtn("ad-revive", AdConfig.Revive, X(26), MY(204), 128 * A, 28 * A, "WATCH AD · REVIVE", 13, primary: true))
                Ads.ShowRewarded(AdConfig.Revive, ok => { if (ok && r.Mode == Mode.Revive) r.Revive(AdCfg.ReviveHp, AdCfg.ReviveRows); });
            if (Btn("giveup", X(46), MY(248), 88 * A, 24 * A, "GIVE UP", 13)) r.GiveUp();
        }

        /// <summary>
        /// The placeholder ad (ads.md §4): black, "AD", the placement and a 2 s countdown. Rewarded: Skip during the
        /// countdown, × after it. Interstitial: closes itself. Drawn over everything; the host stops the run meanwhile.
        /// </summary>
        public void AdPanel(PlaceholderAds ads)
        {
            g.Cur = Layer.Menu;
            if (!g.Skin("white", -10, -10, g.W + 20, g.H + 20, Col.Hex(0x000000))) g.Rect(-10, -10, g.W + 20, g.H + 20, Col.Hex(0x000000));
            T("AD", 0, MY(110), g.W, 40 * A, 48, Cream, Align.Center, true);
            T((ads.Rewarded ? "rewarded · " : "interstitial · ") + ads.Placement, 0, MY(152), g.W, 12 * A, 11, Muted);
            float left = ads.Left;
            if (left > 0) T(Mathf.CeilToInt(left).ToString(), 0, MY(172), g.W, 20 * A, 24, Gold, Align.Center, true);
            if (ads.Rewarded)
            {
                if (left > 0) { if (Btn("ad-skip", X(60), MY(232), 60 * A, 24 * A, "SKIP", 13)) ads.Skip(); }
                else if (Btn("ad-close", g.W - 32 * A, TY(24), 24 * A, 24 * A, null, 0, icon: "ic_close")) ads.Close();
            }
            if (ads.FailLoads) T("AD LOADS FAIL (F8)", 0, BY(284), g.W, 12 * A, 9, RedHi);
        }

        Nav End(Run r)
        {
            bool win = r.Mode == Mode.Victory;
            T(win ? "PIT CLEARED!" : "CREW LOST", X(10), MY(42), 160 * A, 26 * A, 28, win ? Gold : RedHi, Align.Center, true);
            T(win ? "The Mawking falls silent." : "Fell on turn " + r.Turn + ".", X(10), MY(70), 160 * A, 12 * A, 12, Body);
            g.Skin("panel", X(14), MY(90), 152 * A, 148 * A, Col.White);
            var rows = new[]
            {
                new[] { "ic_clock", "Turns", r.Turn.ToString() },
                new[] { "ic_skull", "Foes broken", Short(r.Kills) },
                new[] { "ic_sword", "Damage dealt", Short(r.Stats.Dmg) },
                new[] { "ic_star", "Best hit", Short(r.Stats.BestHit) },
                new[] { "ic_coin", "Gold earned", Short(r.Gold + r.Stats.GoldSpent) },
                new[] { "ic_gem", "Bosses broken", r.Stats.BossesKilled + "/3" },
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = MY(100 + 22 * i);
                g.Skin("inset", X(22), y, 136 * A, 20 * A, Col.White);
                g.SkinAt(rows[i][0], X(25), y + 2 * A, 1, Col.White);
                T(rows[i][1], X(44), y, 70 * A, 20 * A, 11, Body, Align.Left);
                g.Digits("hud", rows[i][2], X(153), y + 6 * A, 1, Col.White, Align.Right);
            }
            if (Btn("home", X(14), MY(250), 60 * A, 26 * A, null, 0, icon: "ic_home")) return Nav.Quit;
            if (Btn("again", X(80), MY(250), 86 * A, 26 * A, win ? "PLAY AGAIN" : "RETRY", 14, primary: true)) return Nav.Again;
            return Nav.None;
        }

        // ------------------------------------------------------------ title and hero pick
        public Nav Title(float t)
        {
            g.Cur = Layer.Menu;
            // the soft copper glow behind the logo (the only non-pixel element, UI-SPEC §8.1)
            for (int i = 0; i < 6; i++) g.Circle(X(90), MY(100), (60 - i * 9) * A, Col.Hex(0xd98841, 0.045f));
            T("RICOCHET", X(10), MY(66), 160 * A, 26 * A, 34, Cream, Align.Center, true);
            T("DEPTHS", X(10), MY(92), 160 * A, 32 * A, 46, Copper, Align.Center, true);
            T("Bounce. Break. Go deeper.", X(10), MY(128), 160 * A, 12 * A, 12, Muted);
            string[] balls = { "ember", "frost", "spark", "venom", "ray" };
            float[] bx = { 26, 52, 82, 112, 138 }, by = { 150, 160, 152, 162, 150 };
            for (int i = 0; i < 5; i++)
                BallIcon(balls[i], X(bx[i]), MY(by[i] + Mathf.Round(Mathf.Sin(t * 1.3f + i * 1.7f))), 16 * A);
            bool go = Btn("play", X(40), MY(196), 100 * A, 30 * A, "PLAY", 18, primary: true);
            Para("Drag to move. Balls fire on their own. Tap a foe to focus it.", X(20), MY(234), 140 * A, 10, Muted, 2, Align.Center);
            Toggles(X(4), BY(278), X(30), BY(278));
            T("demo v0.1", X(120), BY(284), 56 * A, 12 * A, 9, Faint, Align.Right);
            return go ? Nav.Start : Nav.None;
        }

        public string HeroSelect(ref Nav nav)
        {
            g.Cur = Layer.Menu;
            Dim(0.55f);
            if (Btn("back", X(4), TY(24), 24 * A, 24 * A, null, 0, icon: "ic_back")) nav = Nav.Quit;
            T("CHOOSE A HERO", X(32), TY(26), 90 * A, 20 * A, 14, Cream, Align.Left, true);
            var h = Content.Hero(SelHero);

            g.Skin("inset", X(20), MY(56), 140 * A, 94 * A, Col.White);
            Texture tex = null;
            if (Board != null) tex = Board.HeroPreview(h.Id, Mathf.RoundToInt(136 * A * g.Scale), Mathf.RoundToInt(90 * A * g.Scale), g.Time);
            if (tex != null) g.Picture(tex, X(22), MY(58), 136 * A, 90 * A);
            else { var em = UiSkin.Hero(h.Id); if (em != null) g.SkinAt(em, X(54), MY(60), 3, Col.White); }

            T(h.Name.ToUpperInvariant(), X(10), MY(154), 160 * A, 14 * A, 18, Cream, Align.Center, true);
            T(h.Title, X(10), MY(168), 160 * A, 10 * A, 11, Col.Hex(h.Color));
            Para(Content.Balls[h.StartBall].Name + " ball. " + h.TwistText, X(14), MY(180), 152 * A, 11, Body, 3, Align.Center);

            // the mockup has four 38 px tiles; six heroes need 28 px ones to fit the 180 px row
            int n = Content.Heroes.Count;
            float ts = n <= 4 ? 38 : 28, gap = n <= 4 ? 4 : 1, x0 = Mathf.Floor((180 - (n * ts + (n - 1) * gap)) / 2), ty = n <= 4 ? 204 : 208;
            for (int i = 0; i < n; i++)
            {
                var hd = Content.Heroes[i];
                float x = X(x0 + i * (ts + gap)), y = MY(ty);
                bool sel = hd.Id == h.Id;
                g.Skin("card", x, y, ts * A, ts * A, Col.White);
                var em = UiSkin.Hero(hd.Id);
                if (em != null) g.SkinAt(em, x + (ts - 24) / 2 * A, y + (ts - 24) / 2 * A, 1, Col.White);
                if (sel) g.Skin("select", x - 2 * A, y - 2 * A, (ts + 4) * A, (ts + 4) * A, Col.White);
                T(hd.Name, x - 2 * A, y + (ts + 1) * A, (ts + 4) * A, 9 * A, n <= 4 ? 9 : 8, sel ? Cream : Muted);
                if (ui.Area("hero", x, y, ts * A, ts * A, i)) SelHero = hd.Id;
            }
            if (Btn("start", X(30), MY(262), 120 * A, 28 * A, "START RUN", 16, primary: true)) { nav = Nav.PickHero; return SelHero; }
            return null;
        }
    }
}
