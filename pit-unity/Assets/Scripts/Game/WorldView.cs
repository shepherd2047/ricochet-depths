// Draws the board: pit, telegraphs, drops, foes (stacks, flyers, burrowers, shields, statuses), the three bosses,
// shots, balls, beams, the player, particles and floating numbers.
// With Units3D (the game), Board3D renders the pit, foes, bosses and hero as meshes, and this class draws only what
// lies on or floats above the play plane: floor decals (telegraphs, shadows, mounds) at the plane, pixel sprites for
// balls, shots, drops and VFX on the over-bright Glow layer just above the units, and status marks and pixel-font
// numbers above everything. Without it (the stub check's 2D path) it draws the whole board flat, as before.
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public sealed class WorldView
    {
        readonly Gfx g;
        public float Ox, Oy, S = 40, Top, Bottom;    // board origin and pixels per board unit
        public float HeroScale = 1, HeroBack;           // set by Board3D: the hero's camera-distance scale, model offset (board units)
        public bool NumbersOnHud = true;
        public bool Units3D;

        // heights above the board plane, in world units (negative is up; a foe is about 0.7 tall)
        const float ZDecal = -0.01f, ZHeroMark = -0.07f, ZDrop = -0.15f, ZBeam = -0.3f, ZFx = -0.45f, ZShot = -0.4f, ZBall = -0.4f, ZMark = -1.3f, ZText = -1.4f;
        readonly System.Collections.Generic.Dictionary<string, int> spriteCells = new System.Collections.Generic.Dictionary<string, int>();

        int Cell(string key)
        {
            int c;
            if (!spriteCells.TryGetValue(key, out c)) { c = PixelArt.CellOf(key); spriteCells[key] = c; }
            return c;
        }

        public WorldView(Gfx gfx) { g = gfx; }

        public float Sx(float x) { return Ox + x * S; }
        public float Sy(float y) { return Oy + y * S; }
        public Vector2 ToWorld(float px, float py) { return new Vector2((px - Ox) / S, (py - Oy) / S); }

        public void Layout(Run r, float top, float bottom)
        {
            float aw = g.W - 16, ah = g.H - top - bottom;
            S = Mathf.Min(aw / r.Bw, ah / r.Bh);
            Ox = (g.W - r.Bw * S) / 2;
            Oy = top + (ah - r.Bh * S) / 2;
            Top = top; Bottom = g.H - bottom;
        }

        public void Draw(Run r)
        {
            if (Units3D) { Draw3D(r); return; }
            g.Cur = Layer.World;
            float shake = r.Shake > 0 ? r.Shake * 6 : 0;
            g.Ox = shake > 0 ? (Random.value - 0.5f) * shake : 0;
            g.Oy = shake > 0 ? (Random.value - 0.5f) * shake : 0;

            DrawPit(r);
            foreach (var h in r.Hazards) DrawHazard(r, h);
            foreach (var d in r.Drops) DrawDrop(r, d);
            foreach (var e in r.Enemies)
            {
                if (e.Dead) continue;
                if (e.Boss) DrawBoss(r, e);
                else DrawEnemy(r, e);
            }
            DrawTarget(r);
            foreach (var sh in r.Shots) DrawShot(sh);
            foreach (var b in r.Field) DrawBall(b);
            foreach (var bm in r.Beams) DrawBeam(r, bm);
            if (r.Mode != Mode.GameOver) DrawPlayer(r);
            foreach (var f in r.Fx) g.Rect(Sx(f.X) - 2, Sy(f.Y) - 2, 4, 4, Col.Hex(f.Color, Mathf.Min(1, f.T * 3)));

            g.Cur = Layer.WorldText;
            foreach (var t in r.Texts)
                g.TextOutlined(t.S, Sx(t.X), Sy(t.Y), 12 * t.Size, Col.Hex(t.Color, Mathf.Min(1, t.T * 3)));
            DrawHpNumbers(r);
            g.Ox = g.Oy = 0;
        }

        /// <summary>
        /// The overlay for the 3D board, in board-plane design units (Board3D maps them onto the floor; they are not
        /// screen coordinates any more). Screen shake moves the 3D camera instead of the shapes.
        /// </summary>
        void Draw3D(Run r)
        {
            g.Ox = g.Oy = 0;
            g.Cur = Layer.World; g.Z = ZDecal;
            DrawLines(r);
            foreach (var h in r.Hazards) DrawHazard(r, h);
            foreach (var e in r.Enemies) if (!e.Dead) DrawFloorMarks(r, e);
            DrawTarget(r);
            g.Z = ZHeroMark;   // above the kit's raised floor slabs, which hide ZDecal shapes away from the grout
            if (r.Mode != Mode.GameOver) DrawPlayerMarks(r);
            g.Z = ZDecal;

            g.Cur = Layer.Glow;
            g.Z = ZDrop;
            foreach (var d in r.Drops) DrawDropSprite(r, d);
            g.Z = ZBeam;
            foreach (var bm in r.Beams) DrawBeam(r, bm);
            g.Z = ZFx;
            foreach (var f in r.Fx) g.Rect(Sx(f.X) - 1.5f, Sy(f.Y) - 1.5f, 3, 3, Col.Hex(f.Color, Mathf.Min(1, f.T * 3)));
            g.Z = ZShot;
            foreach (var sh in r.Shots) DrawShotSprite(sh);
            g.Z = ZBall;
            foreach (var b in r.Field) if (!Board3D.HasVfx(b)) DrawBallSprite(b);   // the four RicochetVfx balls: Board3D.Vfx

            g.Z = ZMark;
            foreach (var e in r.Enemies) if (!e.Dead) DrawRaisedMarks(r, e);
            g.Cur = Layer.World;
            g.Z = ZText;
            DrawHpPixels(r);
            foreach (var t in r.Texts)
            {
                if (NumbersOnHud && Screens.IsNumber(t.S)) continue;   // Screens draws these in the pit-ui digit fonts
                g.PixelText(t.S, Sx(t.X), Sy(t.Y), 7.5f * t.Size, Col.Hex(t.Color, Mathf.Min(1, t.T * 3)));
            }
            g.Z = 0;
        }

        void DrawLines(Run r)
        {
            float w = r.Bw * S, h = r.Bh * S;
            float al = Sy(r.AttackLine);
            g.Dashed(Ox, al, Ox + w, al, 1.5f, 6, 6, new Color32(255, 93, 108, 80));
            if (r.Expand != null)
            {
                float k = Mathf.Clamp01(r.Expand.T / r.Expand.Dur);
                g.RoundRectStroke(Ox - 2, Oy - 2, w + 4, h + 4, 4, 2, Col.Hex(0xffe14d, 0.5f + 0.5f * Mathf.Sin(k * 30)));
            }
        }

        float Lift(Run r, Enemy e)
        {
            float y = 0;
            if (e.Entry > 0) y -= e.Entry * S * 1.5f;
            return y;
        }

        /// <summary>What a foe leaves on the floor: a burrow mound, a flyer's shadow, a pod's tether.</summary>
        void DrawFloorMarks(Run r, Enemy e)
        {
            float x = Sx(e.X + e.Ox) + 1, y = Sy(e.Y + e.Oy) + 1 + Lift(r, e), w = e.W * S - 2, h = e.H * S - 2;
            if (e.Under)
            {
                g.Ellipse(x + w / 2, y + h * 0.6f, w * 0.45f, h * 0.3f, Col.Hex(0x2a2018, 0.9f));
                g.Ellipse(x + w / 2, y + h * 0.55f, w * 0.3f, h * 0.16f, Col.Hex(0x4a3a28, 0.9f));
                return;
            }
            if (e.Flying) g.Ellipse(x + w / 2, y + h / 2, w * 0.4f, h * 0.32f, new Color32(0, 0, 0, 110));
            if (e.Pod && e.Parent != null) g.Line(x + w / 2, y + h / 2, Sx(e.Parent.Cx), Sy(e.Parent.Cy), 2, Col.Hex(0x6a7a30, 0.6f));
            if (e.HasteT > 0 || (e.Def != null && e.Def.Haste))
            {
                var hc = Col.Hex(0xffe14d, 0.45f);
                for (int i = 0; i < 3; i++) g.Line(x + w * (0.25f + i * 0.25f), y - 3, x + w * (0.25f + i * 0.25f), y - 9 - i * 2, 1.5f, hc);
            }
        }

        /// <summary>Marks that float above a foe: wind-up glow, statuses, swallowed balls, boss tells.</summary>
        void DrawRaisedMarks(Run r, Enemy e)
        {
            if (e.Under) return;
            float x = Sx(e.X + e.Ox) + 1, y = Sy(e.Y + e.Oy) + 1 + Lift(r, e), w = e.W * S - 2, h = e.H * S - 2;
            if (e.Boss && !e.Pod)
            {
                var hk = e.Brain as Hivekeeper;
                if (hk != null && hk.Exposed)
                {
                    float cx = x + w / 2, cy = y + h / 2, rr = Mathf.Min(w, h) * 0.5f;
                    float left = Mathf.Max(0, 9 - hk.ExposedT) / 9;
                    g.Arc(cx, cy, rr + 6, 3, Mathf.PI / 2, Mathf.PI / 2 + Mathf.PI * 2 * left, Col.Hex(0xffe14d, 0.9f));
                }
                var mk = e.Brain as Mawking;
                if (mk != null && mk.Warn > 0)
                {
                    var warn = Col.Hex(0xffe14d, 0.45f + 0.45f * Mathf.Sin(r.Time * 20));
                    float th = Mathf.Max(6, S * 0.35f);
                    for (int i = 0; i < 3; i++)
                    {
                        if (!mk.NextPlates[i]) continue;
                        if (i == 0) g.RoundRectStroke(x - th - 2, y + h * 0.1f, th, h * 0.8f, 3, 2, warn);
                        if (i == 1) g.RoundRectStroke(x + w * 0.1f, y + h + 2, w * 0.8f, th, 3, 2, warn);
                        if (i == 2) g.RoundRectStroke(x + w + 2, y + h * 0.1f, th, h * 0.8f, 3, 2, warn);
                    }
                }
                return;
            }
            if (e.Atk != null && !e.Atk.Lunge && e.Def != null)
            {
                float k = 1 - e.Atk.T / Mathf.Max(0.01f, r.L.TurnLen * e.Def.Windup);
                g.RoundRectStroke(x - 2, y - 2, w + 4, h + 4, 4, 2, new Color32(255, 70, 60, (byte)(255 * (0.3f + 0.7f * Mathf.Clamp01(k)))));
            }
            if (e.Swallowed.Count > 0)
                for (int i = 0; i < e.Swallowed.Count && i < 4; i++)
                    g.Circle(x + w * 0.3f + i * 5, y + h * 0.5f, 2.5f, Col.Hex(e.Swallowed[i].Color));
            if (r.Has(e, "charm"))
            {
                var pc = Col.Hex(0xff8ad8);
                g.Circle(x + w - 6, y + 4, 3, pc); g.Circle(x + w - 1, y + 4, 3, pc); g.Tri(x + w - 9, y + 5, x + w + 2, y + 5, x + w - 3.5f, y + 11, pc);
            }
            int n = 0;
            Pip(r, e, "burn", 0xff9447, x, y, ref n, 1);
            Pip(r, e, "ff", 0xc58bff, x, y, ref n, 1);
            Pip(r, e, "poison", 0x7ee35a, x, y, ref n, 1);
            Pip(r, e, "slow", 0x6bc6ff, x, y, ref n, 1);
            Pip(r, e, "blind", 0xd8d8e8, x, y, ref n, 1);
            if (e.Bleed > 0) { g.Circle(x + 4 + n * 6, y + 4, 2.5f, Col.Hex(0xff5d7a)); n++; }
        }

        void DrawHpPixels(Run r)
        {
            foreach (var e in r.Enemies)
            {
                if (e.Dead || e.Boss || e.Under) continue;
                float w = e.W * S - 2, h = e.H * S - 2;
                if (w <= 14) continue;
                float x = Sx(e.X + e.Ox) + 1, y = Sy(e.Y + e.Oy) + 1 + Lift(r, e);
                g.PixelText(Mathf.CeilToInt(e.Hp).ToString(), x + w / 2, y + h * 0.8f, Mathf.Clamp(h * 0.2f, 6, 9), new Color32(255, 255, 255, 235));
            }
        }

        /// <summary>The ground ring's colour: Tinker's cobalt cloak, else the hero's own colour.</summary>
        static uint RingColor(string id, uint fallback) { return id == "tinker" ? 0x2f63ffu : fallback; }

        void DrawPlayerMarks(Run r)
        {
            var p = r.P;
            float px = Sx(p.X), py = Sy(p.Y), pr = p.R * S;
            // the aim line is drawn by Board3D over the 3D view; here the catch strip, the drop shadow under the model and
            // the ground ring (1.3 cells across in the hero's signature colour) on the hero's true position
            g.Rect(px - 0.5f * r.CatchW * S, py - 0.5f * S, r.CatchW * S, S, new Color32(107, 227, 255, 22));
            float cs = Run.CELL * S * HeroScale, sy = py + HeroBack * S;
            g.Ellipse(px, sy, 0.42f * cs, 0.34f * cs, new Color32(0, 0, 0, 70));
            g.Ellipse(px, sy, 0.3f * cs, 0.24f * cs, new Color32(0, 0, 0, 70));
            uint rc = RingColor(r.Hero.Id, r.Hero.Color);
            g.Circle(px, py, 0.65f * cs, Col.Hex(rc, 0.12f));
            g.Ring(px, py, 0.65f * cs, 0.07f * cs, Col.Hex(rc, 0.75f));
            g.Ring(px, py, 0.65f * cs + 0.06f * cs, 0.05f * cs, Col.Hex(rc, 0.22f));
            if (r.Tw == Twist.Paddle) g.RoundRect(px - 0.5f * r.CatchW * S, py - pr - 6, r.CatchW * S, 4, 2, Col.Hex(0x7fd8ff, 0.8f));
            if (p.PoisonT > 0) g.Ring(px, py, pr + 3, 1.5f, Col.Hex(0x7ee35a, 0.7f));
            if (p.BurnT > 0) g.Ring(px, py, pr + 5, 1.5f, Col.Hex(0xff9447, 0.7f));
        }

        void DrawDropSprite(Run r, Drop d)
        {
            float x = Sx(d.X), y = Sy(d.Y);
            switch (d.Kind)
            {
                case DropKind.Gem:
                    {
                        if (Units3D) break;   // Board3D.Gems: 3D cut gems (style.md §1)
                        float rr = Mathf.Min(13, 5 + Mathf.Sqrt(d.V) * 1.5f);
                        g.Sprite(Cell(d.V >= 100 ? "gem:l" : d.V >= 10 ? "gem:m" : "gem:s"), x, y, rr * 2.6f, Col.White);
                        break;
                    }
                case DropKind.Gold: g.Sprite(Cell("gold"), x, y, 14, Col.White); break;
                case DropKind.Heal: g.Sprite(Cell("heal"), x, y, 15, Col.White); break;
                case DropKind.Fuser: g.Sprite(Cell("fuser"), x, y, 26 * (1 + 0.15f * Mathf.Sin(r.Time * 8)), Col.White); break;
            }
        }

        void DrawShotSprite(Shot sh)
        {
            float x = Sx(sh.X), y = Sy(sh.Y), rr = Mathf.Max(3, sh.R * S);
            switch (sh.Kind)
            {
                case ShotKind.Arrow:
                    {
                        float l = Mathf.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy) + 1e-4f;
                        g.Sprite(Cell("shot:arrow"), x, y, rr * 4, Col.White, Mathf.Atan2(sh.Vx / l, -sh.Vy / l));
                        break;
                    }
                case ShotKind.Icicle: g.Sprite(Cell("shot:icicle"), x, y, rr * 3.6f, Col.White); break;
                case ShotKind.Fireball: g.Sprite(Cell("shot:fireball"), x, y, rr * 3.2f, Col.White); break;
                case ShotKind.Venom: g.Sprite(Cell("shot:venom"), x, y, rr * 3.6f, Col.White); break;
                case ShotKind.Bolt: g.Sprite(Cell("shot:bolt"), x, y, rr * 3.6f, Col.White); break;
                case ShotKind.Boulder: g.Sprite(Cell("shot:boulder"), x, y, rr * 2.7f, Col.White); break;
            }
        }

        void DrawBallSprite(FieldBall b)
        {
            float rr = Mathf.Max(3, b.R * S), x = Sx(b.X), y = Sy(b.Y);
            var c = Col.Hex(b.Color);
            for (int i = 2; i < b.Trail.Count; i += 2)
                g.Line(Sx(b.Trail[i - 2]), Sy(b.Trail[i - 1]), Sx(b.Trail[i]), Sy(b.Trail[i + 1]), rr * (0.3f + 0.6f * i / b.Trail.Count), Col.A(c, 0.2f));
            float a = b.Recall ? 0.6f : 1;
            int cell = b.Baby || b.Inst == null ? Cell("baby") : Cell("ball:" + b.Inst.Id);
            float size = rr * 2 * 16f / 14f;
            if (cell >= 0) g.Sprite(cell, x, y, size, Col.A(Col.White, a));
            else g.Circle(x, y, rr, Col.A(c, a));
            if (b.Color2 != 0) g.Sprite(Cell("ring"), x, y, size * 1.2f, Col.Hex(b.Color2, 0.9f * a));
            if (b.Paddle > 0) g.Ring(x, y, rr + 2, 1.5f, Col.Hex(0x7fd8ff, 0.4f + 0.2f * b.Paddle));
            var st = b.St;
            if (st != null && st.Has(K.AuraR)) g.Ring(x, y, st.G(K.AuraR) * S, 1, new Color32(168, 240, 255, 60));
        }

        // ------------------------------------------------------------ board
        void DrawPit(Run r)
        {
            float w = r.Bw * S, h = r.Bh * S;
            g.RoundRect(Ox - 6, Oy - 6, w + 12, h + 12, 10, Col.Wall);
            g.Rect(Ox, Oy, w, h, Col.Pit);
            var grid = new Color32(255, 255, 255, 9);
            float cs = Run.CELL * S;
            for (int gx = 0; gx <= r.Bw / Run.CELL + 0.01f; gx++) g.Rect(Ox + gx * cs - 0.5f, Oy, 1, h, grid);
            for (int gy = 0; gy <= r.Bh / Run.CELL + 0.01f; gy++) g.Rect(Ox, Oy + gy * cs - 0.5f, w, 1, grid);
            float al = Sy(r.AttackLine);
            g.Dashed(Ox, al, Ox + w, al, 1.5f, 6, 6, new Color32(255, 93, 108, 64));
            if (r.Expand != null)
            {
                float k = Mathf.Clamp01(r.Expand.T / r.Expand.Dur);
                g.RoundRectStroke(Ox - 6, Oy - 6, w + 12, h + 12, 10, 2, Col.Hex(0xffe14d, 0.5f + 0.5f * Mathf.Sin(k * 30)));
            }
        }

        void DrawHazard(Run r, Hazard h)
        {
            float k = h.Max > 0 ? 1 - h.T / h.Max : 1;
            switch (h.Kind)
            {
                case HazardKind.Blast:
                    g.Circle(Sx(h.X), Sy(h.Y), h.R * S, Col.Hex(0xff5a3c, 0.12f + 0.25f * k));
                    g.Ring(Sx(h.X), Sy(h.Y), h.R * S * k, 2, Col.Hex(0xff7850, 0.9f));
                    if (!float.IsNaN(h.FromX))
                    {
                        // the lobbed shell travels from the mortar to the mark
                        float px = Mathf.Lerp(h.FromX, h.X, k), py = Mathf.Lerp(h.FromY, h.Y, k) - Mathf.Sin(k * Mathf.PI) * 2.5f;
                        g.Circle(Sx(px), Sy(py), 0.18f * S, Col.Hex(0x3b2a12));
                        g.Ring(Sx(px), Sy(py), 0.18f * S, 1.5f, Col.Hex(0xff9447));
                    }
                    break;
                case HazardKind.Lance:
                    g.Rect(Sx(h.X - h.W / 2), Sy(h.Y), h.W * S, (r.Bh - h.Y) * S, Col.Hex(0xff4d6d, 0.1f + 0.25f * k));
                    g.Rect(Sx(h.X) - 1, Sy(h.Y), 2, (r.Bh - h.Y) * S, Col.Hex(0xff4d6d, 0.5f));
                    break;
                case HazardKind.Sweep:
                    g.Rect(Ox, Sy(h.Y - h.W / 2), r.Bw * S, h.W * S, Col.Hex(0xff4d6d, 0.1f + 0.25f * k));
                    g.Rect(Ox, Sy(h.Y) - 1, r.Bw * S, 2, Col.Hex(0xff4d6d, 0.5f));
                    break;
                case HazardKind.Beam:
                    {
                        float x2 = h.X + h.Dx * 40, y2 = h.Y + h.Dy * 40;
                        g.Line(Sx(h.X), Sy(h.Y), Sx(x2), Sy(y2), h.W * S, Col.Hex(0xff5d8f, 0.08f + 0.22f * k));
                        g.Dashed(Sx(h.X), Sy(h.Y), Sx(x2), Sy(y2), 1.5f, 5, 5, Col.Hex(0xff5d8f, 0.7f));
                        break;
                    }
            }
        }

        void DrawDrop(Run r, Drop d)
        {
            float x = Sx(d.X), y = Sy(d.Y);
            switch (d.Kind)
            {
                case DropKind.Gem:
                    {
                        float rr = Mathf.Min(9, 3 + Mathf.Sqrt(d.V) * 1.2f);
                        var c = Col.Hex(d.V >= 100 ? 0xff7bf2u : d.V >= 10 ? 0x6be3ffu : 0x6dff8eu);
                        g.Quad(x, y - rr, x + rr * 0.7f, y, x, y + rr, x - rr * 0.7f, y, c);
                        break;
                    }
                case DropKind.Gold:
                    g.Circle(x, y, 5.5f, Col.Hex(0xa07a10)); g.Circle(x, y, 4.2f, Col.Gold); break;
                case DropKind.Heal:
                    g.Rect(x - 6, y - 2, 12, 4, Col.Hex(0xff6b8b)); g.Rect(x - 2, y - 6, 4, 12, Col.Hex(0xff6b8b)); break;
                case DropKind.Fuser:
                    {
                        float p = 1 + 0.15f * Mathf.Sin(r.Time * 8);
                        g.RoundRect(x - 12 * p, y - 10 * p, 24 * p, 20 * p, 5, Col.Hex(0xffb347));
                        g.Rect(x - 8 * p, y - 3 * p, 16 * p, 6 * p, Col.Hex(0xff5a1f));
                        g.RoundRectStroke(x - 12 * p, y - 10 * p, 24 * p, 20 * p, 5, 2, Col.Hex(0xfff3d6));
                        break;
                    }
            }
        }

        // ------------------------------------------------------------ foes
        void DrawEnemy(Run r, Enemy e)
        {
            float x = Sx(e.X + e.Ox) + 1, y = Sy(e.Y + e.Oy) + 1, w = e.W * S - 2, h = e.H * S - 2;
            float alpha = 1;
            if (e.Entry > 0) { y -= e.Entry * S * 1.5f; alpha = Mathf.Max(0, 1 - e.Entry); }
            if (e.Pod) { DrawPod(r, e, x, y, w, h, alpha); return; }

            if (e.Under)
            {
                // burrowed: only a mound of dirt shows, and balls pass over it
                g.Ellipse(x + w / 2, y + h * 0.75f, w * 0.45f, h * 0.2f, Col.Hex(0x5a4430, 0.85f * alpha));
                g.Ellipse(x + w / 2, y + h * 0.7f, w * 0.3f, h * 0.1f, Col.Hex(0x8a6a48, 0.85f * alpha));
                return;
            }
            float lift = 0;
            if (e.Flying)
            {
                lift = S * 0.25f + Mathf.Sin(r.Time * 6 + e.Id) * S * 0.06f;
                g.Ellipse(x + w / 2, y + h * 0.9f, w * 0.38f, h * 0.12f, new Color32(0, 0, 0, (byte)(90 * alpha)));
                y -= lift;
            }

            bool frozen = r.Has(e, "freeze");
            var col = e.Flash > 0 ? Col.White : frozen ? Col.Hex(0x9fe8ff) : Col.Hex(e.Def.Color);
            col = Col.A(col, alpha);
            float rad = Mathf.Min(w, h) * 0.22f;
            g.RoundRect(x, y, w, h, rad, col);
            if (e.Def.Elite) g.RoundRectStroke(x, y, w, h, rad, 2.5f, Col.Hex(0xffe14d, alpha));
            if (e.Def.Stackable) g.RoundRectStroke(x + 3, y + 3, w - 6, h - 6, 4, 2, new Color32(0, 0, 0, (byte)(70 * alpha)));
            if (e.Mount != null) g.Rect(x + w * 0.15f, y + h - 3, w * 0.7f, 3, new Color32(0, 0, 0, (byte)(80 * alpha)));

            if (e.Flying)
            {
                float flap = Mathf.Sin(r.Time * 18 + e.Id) * h * 0.18f;
                var wc = Col.A(Col.Hex(0xe8ecff), 0.7f * alpha);
                g.Tri(x, y + h * 0.4f, x - w * 0.35f, y + h * 0.2f - flap, x, y + h * 0.65f, wc);
                g.Tri(x + w, y + h * 0.4f, x + w + w * 0.35f, y + h * 0.2f - flap, x + w, y + h * 0.65f, wc);
            }
            if (e.Def.Spiked)
            {
                var sc = Col.A(Col.Hex(0xe0e0f0), alpha);
                for (int i = 0; i < 4; i++)
                {
                    float t = (i + 0.5f) / 4;
                    g.Tri(x + w * t - 3, y, x + w * t + 3, y, x + w * t, y - 5, sc);
                    g.Tri(x + w * t - 3, y + h, x + w * t + 3, y + h, x + w * t, y + h + 5, sc);
                }
            }
            if (e.HasteT > 0 || e.Def.Haste)
            {
                var hc = Col.Hex(0xffe14d, 0.5f * alpha);
                for (int i = 0; i < 3; i++) g.Line(x - 4 - i * 3, y + h * (0.3f + i * 0.2f), x - 10 - i * 3, y + h * (0.3f + i * 0.2f) - 4, 1.5f, hc);
            }

            // face
            float ex = x + w / 2, ey = y + h * 0.42f, er = Mathf.Max(1.5f, Mathf.Min(w, h) * 0.09f);
            var ink = Col.A(Col.Ink, alpha);
            bool blind = r.Has(e, "blind");
            if (blind)
            {
                g.Line(ex - w * 0.26f, ey, ex - w * 0.1f, ey, 2, ink);
                g.Line(ex + w * 0.1f, ey, ex + w * 0.26f, ey, 2, ink);
            }
            else
            {
                g.Circle(ex - w * 0.18f, ey, er, ink);
                g.Circle(ex + w * 0.18f, ey, er, ink);
            }
            if (e.Atk != null)
            {
                g.Line(ex - w * 0.3f, ey - er * 2.2f, ex - w * 0.08f, ey - er, 2, ink);
                g.Line(ex + w * 0.3f, ey - er * 2.2f, ex + w * 0.08f, ey - er, 2, ink);
            }
            DrawMarks(e, x, y, w, h, ex, er, alpha);

            // shields
            var shc = Col.A(Col.Hex(0xc9d2e8), alpha);
            if (e.ShieldF) g.RoundRect(x - 2, y + h - 4, w + 4, 6, 3, shc);
            if (e.ShieldL) g.RoundRect(x - 5, y + 2, 5, h - 4, 2.5f, shc);
            if (e.ShieldR) g.RoundRect(x + w, y + 2, 5, h - 4, 2.5f, shc);

            // melee wind-up glow
            if (e.Atk != null && !e.Atk.Lunge)
            {
                float k = 1 - e.Atk.T / Mathf.Max(0.01f, r.L.TurnLen * e.Def.Windup);
                g.RoundRectStroke(x - 2, y - 2, w + 4, h + 4, 6, 3, new Color32(255, 80, 80, (byte)(255 * (0.3f + 0.7f * Mathf.Clamp01(k)) * alpha)));
            }
            if (e.Swallowed.Count > 0)
                for (int i = 0; i < e.Swallowed.Count && i < 4; i++)
                    g.Circle(x + w * 0.3f + i * 5, y + h * 0.66f, 2.5f, Col.Hex(e.Swallowed[i].Color, alpha));
            if (r.Has(e, "charm"))
            {
                var pc = Col.Hex(0xff8ad8, alpha);
                g.Circle(x + w - 6, y + 4, 3, pc); g.Circle(x + w - 1, y + 4, 3, pc); g.Tri(x + w - 9, y + 5, x + w + 2, y + 5, x + w - 3.5f, y + 11, pc);
            }

            // status pips
            int n = 0;
            Pip(r, e, "burn", 0xff9447, x, y, ref n, alpha);
            Pip(r, e, "ff", 0xc58bff, x, y, ref n, alpha);
            Pip(r, e, "poison", 0x7ee35a, x, y, ref n, alpha);
            Pip(r, e, "slow", 0x6bc6ff, x, y, ref n, alpha);
            if (e.Bleed > 0) { g.Circle(x + 4 + n * 6, y + 4, 2.5f, Col.Hex(0xff5d7a, alpha)); n++; }
        }

        void DrawMarks(Enemy e, float x, float y, float w, float h, float ex, float er, float alpha)
        {
            var ink = Col.A(Col.Ink, alpha);
            switch (e.Type)
            {
                case "archer": case "volleyer": g.Tri(ex - w * 0.1f, y + h * 0.7f, ex + w * 0.1f, y + h * 0.7f, ex, y + h * 0.92f, ink); break;
                case "mender":
                    g.Rect(ex - w * 0.05f, y + h * 0.58f, w * 0.1f, h * 0.3f, Col.Hex(0xf2fff4, alpha));
                    g.Rect(ex - w * 0.15f, y + h * 0.68f, w * 0.3f, h * 0.1f, Col.Hex(0xf2fff4, alpha)); break;
                case "mortar": g.Rect(ex - w * 0.12f, y + h * 0.62f, w * 0.24f, h * 0.3f, Col.Hex(0x3b2a12, alpha)); break;
                case "popper":
                    g.Circle(x + w * 0.25f, y + h * 0.75f, er * 1.1f, new Color32(255, 255, 255, (byte)(90 * alpha)));
                    g.Circle(x + w * 0.7f, y + h * 0.7f, er * 1.1f, new Color32(255, 255, 255, (byte)(90 * alpha))); break;
                case "lancer": g.Rect(ex - 1.5f, y + h * 0.6f, 3, h * 0.38f, Col.Hex(0xff5d8f, alpha)); break;
                case "chiller": g.Tri(ex - w * 0.12f, y + h * 0.68f, ex + w * 0.12f, y + h * 0.68f, ex, y + h * 0.95f, Col.Hex(0xe8fbff, alpha)); break;
                case "hurler": g.Circle(ex, y + h * 0.76f, er * 1.6f, Col.Hex(0xffd04a, alpha)); break;
                case "nester": g.Ellipse(ex, y + h * 0.76f, w * 0.12f, h * 0.14f, Col.Hex(0xf0e0c0, alpha)); break;
                case "gulper": g.Ellipse(ex, y + h * 0.74f, w * 0.26f, h * 0.12f, ink); break;
                case "dasher": g.Line(ex - w * 0.2f, y + h * 0.78f, ex + w * 0.2f, y + h * 0.78f, 2, ink); break;
            }
        }

        void Pip(Run r, Enemy e, string key, uint c, float x, float y, ref int n, float alpha)
        {
            if (!r.Has(e, key)) return;
            g.Circle(x + 4 + n * 6, y + 4, 2.5f, Col.Hex(c, alpha));
            n++;
        }

        void DrawPod(Run r, Enemy e, float x, float y, float w, float h, float alpha)
        {
            float cx = x + w / 2, cy = y + h / 2, rr = Mathf.Min(w, h) / 2;
            if (e.Parent != null) g.Line(cx, cy, Sx(e.Parent.Cx), Sy(e.Parent.Cy), 2, Col.Hex(0x6a7a30, 0.5f * alpha));
            g.Circle(cx, cy, rr, Col.Hex(e.Flash > 0 ? 0xffffffu : 0xb8d060u, alpha));
            g.Ring(cx, cy, rr, 2, Col.Hex(0x4a5a20, alpha));
            g.Circle(cx, cy, rr * 0.35f, Col.Hex(0x6a7a30, alpha));
        }

        void DrawHpNumbers(Run r)
        {
            foreach (var e in r.Enemies)
            {
                if (e.Dead || e.Boss || e.Under) continue;
                float w = e.W * S - 2, h = e.H * S - 2;
                if (w <= 14) continue;
                float x = Sx(e.X + e.Ox) + 1, y = Sy(e.Y + e.Oy) + 1;
                float alpha = 1;
                if (e.Entry > 0) { y -= e.Entry * S * 1.5f; alpha = Mathf.Max(0, 1 - e.Entry); }
                if (e.Flying) y -= S * 0.25f + Mathf.Sin(r.Time * 6 + e.Id) * S * 0.06f;
                g.Text(Mathf.CeilToInt(e.Hp).ToString(), x + w / 2, y + h * (e.Pod ? 0.5f : 0.76f), Mathf.Clamp(h * 0.3f, 9, 14),
                    e.Pod ? Col.A(Col.Ink, alpha) : new Color32(255, 255, 255, (byte)(235 * alpha)));
            }
        }

        // ------------------------------------------------------------ bosses
        void DrawBoss(Run r, Enemy e)
        {
            var brain = e.Brain;
            if (brain is Bellwarden) DrawBell(r, e);
            else if (brain is Hivekeeper) DrawHive(r, e, (Hivekeeper)brain);
            else if (brain is Mawking) DrawMaw(r, e, (Mawking)brain);
            else g.RoundRect(Sx(e.X), Sy(e.Y), e.W * S, e.H * S, 12, Col.Hex(e.Color));
        }

        static readonly System.Collections.Generic.List<Vector2> pts = new System.Collections.Generic.List<Vector2>(32);

        void DrawBell(Run r, Enemy e)
        {
            float x = Sx(e.X + e.Ox), y = Sy(e.Y + e.Oy), w = e.W * S, h = e.H * S;
            var body = e.Flash > 0 ? Col.Hex(0xd8ccff) : Col.Hex(0x6a5498);
            pts.Clear();
            pts.Add(new Vector2(x + w * 0.5f, y + h * 0.55f));
            pts.Add(new Vector2(x + w * 0.18f, y + h * 0.18f));
            pts.Add(new Vector2(x + w * 0.35f, y + h * 0.05f));
            pts.Add(new Vector2(x + w * 0.65f, y + h * 0.05f));
            pts.Add(new Vector2(x + w * 0.82f, y + h * 0.18f));
            pts.Add(new Vector2(x + w * 0.9f, y + h * 0.75f));
            pts.Add(new Vector2(x + w, y + h * 0.95f));
            pts.Add(new Vector2(x, y + h * 0.95f));
            pts.Add(new Vector2(x + w * 0.1f, y + h * 0.75f));
            pts.Add(new Vector2(x + w * 0.18f, y + h * 0.18f));
            g.Poly(pts, body);
            g.Rect(x, y + h * 0.88f, w, h * 0.12f, Col.Hex(0x3a2c58));
            // crown: the weak spot on top
            float pulse = 0.6f + 0.4f * Mathf.Sin(r.Time * 5);
            var cc = Col.Hex(0xffd84a, 0.65f + 0.35f * pulse);
            for (int i = 0; i < 3; i++)
            {
                float cx = x + w * (0.32f + i * 0.18f);
                g.Tri(cx - w * 0.08f, y + h * 0.08f, cx + w * 0.08f, y + h * 0.08f, cx, y - h * 0.08f, cc);
            }
            g.Rect(x + w * 0.24f, y + h * 0.05f, w * 0.52f, h * 0.05f, cc);
            // eye
            bool busy = e.Brain.Phase != null;
            g.Ellipse(x + w * 0.5f, y + h * 0.45f, w * 0.16f, h * 0.11f, Col.Ink);
            float look = Mathf.Clamp((r.P.X - e.Cx) * 0.03f, -0.06f, 0.06f);
            g.Circle(x + w * (0.5f + look), y + h * 0.46f, h * 0.06f, Col.Hex(busy ? 0xff6b6bu : 0xffe9a8u));
            for (int j = 0; j < 5; j++) g.Line(x + w * (0.3f + j * 0.1f), y + h * 0.66f, x + w * (0.3f + j * 0.1f), y + h * 0.84f, 3, new Color32(0, 0, 0, 100));
        }

        void DrawHive(Run r, Enemy e, Hivekeeper hk)
        {
            float cx = Sx(e.Cx + e.Ox), cy = Sy(e.Cy + e.Oy), rr = Mathf.Min(e.W, e.H) * S * 0.5f;
            bool bare = hk.Exposed;
            pts.Clear();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3 + Mathf.PI / 6;
                pts.Add(new Vector2(cx + Mathf.Cos(a) * rr, cy + Mathf.Sin(a) * rr));
            }
            var body = e.Flash > 0 ? Col.White : bare ? Col.Hex(0xd8e870) : Col.Hex(0x6a7a30);
            g.Poly(pts, body);
            for (int i = 0; i < 6; i++) g.Line(pts[i].x, pts[i].y, pts[(i + 1) % 6].x, pts[(i + 1) % 6].y, 3, Col.Hex(0x3a4410));
            if (bare)
            {
                float left = Mathf.Max(0, 9 - hk.ExposedT) / 9;
                g.Arc(cx, cy, rr + 6, 3, Mathf.PI / 2, Mathf.PI / 2 + Mathf.PI * 2 * left, Col.Hex(0xffe14d, 0.8f));
            }
            else g.Ring(cx, cy, rr * 0.55f, 2, Col.Hex(0x3a4410, 0.8f));
            g.Circle(cx, cy, rr * 0.28f, Col.Hex(bare ? 0xff6b6bu : 0x3a4410u));
        }

        void DrawMaw(Run r, Enemy e, Mawking mk)
        {
            float x = Sx(e.X + e.Ox), y = Sy(e.Y + e.Oy), w = e.W * S, h = e.H * S;
            var body = e.Flash > 0 ? Col.Hex(0xffd0d0) : Col.Hex(0x8a2e3e);
            g.RoundRect(x, y, w, h, w * 0.18f, body);
            g.RoundRect(x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.3f, w * 0.1f, Col.Hex(0x5a1c28));
            // eyes
            float look = Mathf.Clamp((r.P.X - e.Cx) * 0.02f, -0.04f, 0.04f);
            for (int i = -1; i <= 1; i += 2)
            {
                g.Circle(x + w * (0.5f + i * 0.2f), y + h * 0.23f, h * 0.08f, Col.Hex(0xfff0c0));
                g.Circle(x + w * (0.5f + i * 0.2f + look), y + h * 0.24f, h * 0.04f, Col.Ink);
            }
            // maw with teeth
            float my = y + h * 0.5f, mh = h * 0.32f;
            bool open = e.Brain.Phase == "maw";
            if (open) mh *= 1.3f;
            g.RoundRect(x + w * 0.15f, my, w * 0.7f, mh, 8, Col.Ink);
            for (int t = 0; t < 7; t++)
            {
                float tx = x + w * (0.18f + t * 0.1f);
                g.Tri(tx, my, tx + w * 0.07f, my, tx + w * 0.035f, my + mh * 0.3f, Col.Hex(0xfff0e0));
                g.Tri(tx, my + mh, tx + w * 0.07f, my + mh, tx + w * 0.035f, my + mh * 0.7f, Col.Hex(0xfff0e0));
            }
            // rotating plates; the next arrangement blinks during the warning
            var plate = Col.Hex(0xc9d2e8);
            var warn = Col.Hex(0xffe14d, 0.4f + 0.4f * Mathf.Sin(r.Time * 20));
            float th = Mathf.Max(6, S * 0.35f);
            for (int i = 0; i < 3; i++)
            {
                bool on = mk.Plates[i], next = mk.Warn > 0 && mk.NextPlates[i];
                if (!on && !next) continue;
                var c = on ? plate : warn;
                if (i == 0) g.RoundRect(x - th - 2, y + h * 0.1f, th, h * 0.8f, 3, c);
                if (i == 1) g.RoundRect(x + w * 0.1f, y + h + 2, w * 0.8f, th, 3, c);
                if (i == 2) g.RoundRect(x + w + 2, y + h * 0.1f, th, h * 0.8f, 3, c);
                if (on && next) g.RoundRectStroke(i == 0 ? x - th - 2 : i == 2 ? x + w + 2 : x + w * 0.1f, i == 1 ? y + h + 2 : y + h * 0.1f,
                    i == 1 ? w * 0.8f : th, i == 1 ? th : h * 0.8f, 3, 2, warn);
            }
            if (mk.Enraged) g.RoundRectStroke(x - 2, y - 2, w + 4, h + 4, w * 0.18f, 2, Col.Hex(0xff4d6d, 0.5f + 0.5f * Mathf.Sin(r.Time * 8)));
        }

        void DrawTarget(Run r)
        {
            if (r.P.Target < 0) return;
            var t = r.Enemies.Find(e => e.Id == r.P.Target && !e.Dead);
            if (t == null) return;
            float x = Sx(t.X) - 3, y = Sy(t.Y) - 3, w = t.W * S + 6, h = t.H * S + 6;
            var c = Col.Hex(0xffe14d);
            float k = Mathf.Min(w, h) * 0.3f;
            g.Line(x, y, x + k, y, 2, c); g.Line(x, y, x, y + k, 2, c);
            g.Line(x + w, y, x + w - k, y, 2, c); g.Line(x + w, y, x + w, y + k, 2, c);
            g.Line(x, y + h, x + k, y + h, 2, c); g.Line(x, y + h, x, y + h - k, 2, c);
            g.Line(x + w, y + h, x + w - k, y + h, 2, c); g.Line(x + w, y + h, x + w, y + h - k, 2, c);
        }

        // ------------------------------------------------------------ projectiles, balls, beams
        void DrawShot(Shot sh)
        {
            float x = Sx(sh.X), y = Sy(sh.Y), rr = Mathf.Max(3, sh.R * S);
            switch (sh.Kind)
            {
                case ShotKind.Arrow:
                    {
                        float l = Mathf.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy) + 1e-4f, ux = sh.Vx / l, uy = sh.Vy / l;
                        g.Line(x - ux * rr * 2.2f, y - uy * rr * 2.2f, x + ux * rr, y + uy * rr, 2.5f, Col.Hex(0x9dffcf));
                        g.Tri(x + ux * rr * 1.8f, y + uy * rr * 1.8f, x - uy * rr * 0.8f, y + ux * rr * 0.8f, x + uy * rr * 0.8f, y - ux * rr * 0.8f, Col.Hex(0xe0fff0));
                        break;
                    }
                case ShotKind.Icicle: g.Circle(x, y, rr, Col.Hex(0xbff4ff)); g.Ring(x, y, rr, 1.5f, Col.Hex(0x5ab0e0)); break;
                case ShotKind.Fireball: g.Circle(x, y, rr * 1.2f, Col.Hex(0xff7043, 0.5f)); g.Circle(x, y, rr, Col.Hex(0xffb347)); break;
                case ShotKind.Venom: g.Circle(x, y, rr, Col.Hex(0x7ee35a)); g.Circle(x - rr * 0.3f, y - rr * 0.3f, rr * 0.3f, Col.Hex(0xd0ffb0)); break;
                case ShotKind.Bolt: g.Circle(x, y, rr, Col.Hex(0xffcf5a)); g.Ring(x, y, rr, 1.2f, new Color32(0, 0, 0, 128)); break;
                case ShotKind.Boulder: g.Circle(x, y, rr, Col.Hex(0x8a7058)); g.Ring(x, y, rr, 2, Col.Hex(0x4a3828)); break;
            }
        }

        void DrawBall(FieldBall b)
        {
            float rr = Mathf.Max(3, b.R * S);
            var c = Col.Hex(b.Color);
            for (int i = 2; i < b.Trail.Count; i += 2)
                g.Line(Sx(b.Trail[i - 2]), Sy(b.Trail[i - 1]), Sx(b.Trail[i]), Sy(b.Trail[i + 1]), rr * (0.4f + 0.6f * i / b.Trail.Count), Col.A(c, 0.22f));
            float a = b.Recall ? 0.6f : 1;
            g.Circle(Sx(b.X), Sy(b.Y), rr, Col.A(c, a));
            if (b.Color2 != 0) g.Pie(Sx(b.X), Sy(b.Y), rr, -Mathf.PI / 2, Mathf.PI / 2, Col.Hex(b.Color2, a));
            if (b.Paddle > 0) g.Ring(Sx(b.X), Sy(b.Y), rr + 2, 1.5f, Col.Hex(0x7fd8ff, 0.4f + 0.2f * b.Paddle));
            var st = b.St;
            if (st != null && st.Has(K.AuraR)) g.Ring(Sx(b.X), Sy(b.Y), st.G(K.AuraR) * S, 1, new Color32(168, 240, 255, 90));
        }

        void DrawBeam(Run r, Beam bm)
        {
            if (Units3D && (bm.Kind == BeamKind.Arc || bm.Kind == BeamKind.Row) && Board3D.HasVfx(bm.Src)) return;   // RicochetVfx bolt / laser
            var c = Col.Hex(bm.Color, Mathf.Min(1, bm.T * 5));
            switch (bm.Kind)
            {
                case BeamKind.Arc:
                    {
                        float mx = (bm.X1 + bm.X2) / 2 + (Random.value - 0.5f) * 0.4f, my = (bm.Y1 + bm.Y2) / 2 + (Random.value - 0.5f) * 0.4f;
                        g.Line(Sx(bm.X1), Sy(bm.Y1), Sx(mx), Sy(my), 2, c);
                        g.Line(Sx(mx), Sy(my), Sx(bm.X2), Sy(bm.Y2), 2, c);
                        break;
                    }
                case BeamKind.Row: g.Rect(Ox, Sy(bm.Y) - 2, r.Bw * S, 4, c); break;
                case BeamKind.Col: g.Rect(Sx(bm.X) - 2, Oy, 4, r.Bh * S, c); break;
                case BeamKind.Ring: g.Ring(Sx(bm.X), Sy(bm.Y), bm.R * S, 3, c); break;
                case BeamKind.Square: g.RoundRectStroke(Sx(bm.X - bm.R), Sy(bm.Y - bm.R), bm.R * 2 * S, bm.R * 2 * S, 3, 3, c); break;
                case BeamKind.Lance: g.Rect(Sx(bm.X - bm.W / 2), Sy(bm.Y), bm.W * S, (r.Bh - bm.Y) * S, c); break;
                case BeamKind.Laser: g.Line(Sx(bm.X1), Sy(bm.Y1), Sx(bm.X2), Sy(bm.Y2), Mathf.Max(3, bm.W * S), c); break;
                case BeamKind.Sweep: g.Rect(Ox, Sy(bm.Y - bm.W / 2), r.Bw * S, bm.W * S, c); break;
                case BeamKind.Flash: g.Rect(Ox, Oy, r.Bw * S, r.Bh * S, Col.A(c, 0.3f)); break;
            }
        }

        // ------------------------------------------------------------ player
        void DrawPlayer(Run r)
        {
            var p = r.P;
            float px = Sx(p.X), py = Sy(p.Y), pr = p.R * S;
            float a = p.Aim * Mathf.Deg2Rad;
            g.Dashed(px, py, px + Mathf.Cos(a) * S * 2.2f, py - Mathf.Sin(a) * S * 2.2f, 2, 3, 6, new Color32(255, 255, 255, 90));
            g.Rect(px - 0.5f * r.CatchW * S, py - 0.5f * S, r.CatchW * S, S, new Color32(107, 227, 255, 18));
            if (r.Tw == Twist.Paddle) g.RoundRect(px - 0.5f * r.CatchW * S, py - pr - 6, r.CatchW * S, 4, 2, Col.Hex(0x7fd8ff, 0.8f));
            var heroCol = Col.Hex(r.Hero.Color);
            if (p.RootT > 0) g.Ring(px, py + pr * 0.6f, pr * 1.1f, 2, Col.Hex(0x9feaff));
            if (p.PoisonT > 0) g.Ring(px, py, pr + 3, 1.5f, Col.Hex(0x7ee35a, 0.7f));
            if (p.BurnT > 0) g.Ring(px, py, pr + 5, 1.5f, Col.Hex(0xff9447, 0.7f));
            g.Circle(px, py, pr, p.HurtT > 0 ? Col.Hex(0xff8080) : Col.Hex(0xffd29a));
            g.Pie(px, py - pr * 0.25f, pr, 0, Mathf.PI, heroCol);
            g.Circle(px - pr * 0.3f, py + pr * 0.1f, pr * 0.12f, Col.Ink);
            g.Circle(px + pr * 0.3f, py + pr * 0.1f, pr * 0.12f, Col.Ink);
        }
    }
}
