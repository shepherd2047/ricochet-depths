// Dev autopilot used by the headless harness and the in-game dev toggle. Not a player-facing feature.
namespace Pit.Core
{
    public static class Bot
    {
        /// <summary>Sets r.InputX/InputY. Samples candidate spots near the floor and picks the one with the least
        /// predicted danger (shots, telegraphs, foes about to lunge), preferring the spot under the next falling ball.</summary>
        public static void Drive(Run r)
        {
            var p = r.P;
            FieldBall fall = null;
            foreach (var b in r.Field)
                if (!b.Recall && b.Vy > 0 && !b.Temp && (fall == null || b.Y > fall.Y)) fall = b;
            float want = fall != null ? fall.X : p.X;
            float bestX = p.X, bestY = p.Y, best = float.MaxValue;
            for (int iy = 0; iy < 3; iy++)
            {
                float cy = r.Bh - 0.6f - iy * 0.9f;
                for (int ix = -6; ix <= 6; ix++)
                {
                    float cx = M.Clamp(p.X + ix * 0.5f, p.R, r.Bw - p.R);
                    float score = Danger(r, cx, cy) + M.Abs(cx - want) * 0.6f + iy * 0.8f + M.Abs(ix) * 0.05f;
                    if (score < best) { best = score; bestX = cx; bestY = cy; }
                }
            }
            float dx2 = bestX - p.X, dy2 = bestY - p.Y;
            r.InputX = M.Clamp(dx2 * 3, -1, 1); r.InputY = M.Clamp(dy2 * 3, -1, 1);
        }

        static float Danger(Run r, float x, float y)
        {
            var p = r.P;
            float d = 0, dist = M.Sqrt((x - p.X) * (x - p.X) + (y - p.Y) * (y - p.Y));
            float travel = dist / M.Max(0.5f, r.Move);
            foreach (var sh in r.Shots)
            {
                for (int k = 0; k <= 8; k++)
                {
                    float t = k * 0.1f;
                    // where we would be at time t while walking to (x, y)
                    float f = travel > 0 ? M.Min(1, t / travel) : 1;
                    float px = M.Lerp(p.X, x, f), py = M.Lerp(p.Y, y, f);
                    float qx = sh.X + sh.Vx * t - px, qy = sh.Y + sh.Vy * t - py, rr = sh.R + p.R + 0.2f;
                    if (qx * qx + qy * qy < rr * rr) { d += 30 - k * 2; break; }
                }
            }
            foreach (var h in r.Hazards)
            {
                bool inside = false;
                switch (h.Kind)
                {
                    case HazardKind.Blast: inside = (x - h.X) * (x - h.X) + (y - h.Y) * (y - h.Y) < (h.R + 0.5f) * (h.R + 0.5f); break;
                    case HazardKind.Lance: inside = M.Abs(x - h.X) < h.W / 2 + 0.5f; break;
                    case HazardKind.Sweep: inside = M.Abs(y - h.Y) < h.W / 2 + 0.5f; break;
                    case HazardKind.Beam:
                        {
                            float vx = x - h.X, vy = y - h.Y;
                            inside = vx * h.Dx + vy * h.Dy > 0 && M.Abs(vx * h.Dy - vy * h.Dx) < h.W / 2 + 0.5f;
                            break;
                        }
                }
                if (inside) d += 60 + (h.T < travel + 0.3f ? 40 : 0);
            }
            foreach (var e in r.Enemies)
            {
                if (e.Dead || e.Boss || e.Mount != null) continue;
                float ex = M.Clamp(x, e.X, e.X + e.W), ey = M.Clamp(y, e.Y, e.Bottom);
                float dd = M.Sqrt((x - ex) * (x - ex) + (y - ey) * (y - ey));
                if (dd < 0.6f) d += 25;
                else if (e.Atk != null && dd < e.H / 2 + 1.6f) d += 6;
            }
            return d;
        }

        /// <summary>Resolves open menus like a sensible player: fill ball slots, grow balls, then items;
        /// evolve when possible, combine only once the ball slots are full, otherwise take free upgrades.</summary>
        public static void AutoPick(Run r)
        {
            if (r.Mode == Mode.LevelUp)
            {
                int best = 0; float bestScore = -1;
                for (int i = 0; i < r.Menu.Options.Count; i++)
                {
                    var o = r.Menu.Options[i];
                    float s = o.Kind == OptKind.BallNew ? (r.Balls.Count < 3 ? 4 : 2.2f)
                        : o.Kind == OptKind.BallUp ? 3 + 0.3f * o.Ball.Level
                        : o.Kind == OptKind.ItemUp ? 2.5f
                        : (o.Id == "locket" || o.Id == "knuckles" || o.Id == "trigger" ? 2.4f : 1.8f);
                    if (s > bestScore) { bestScore = s; best = i; }
                }
                r.ChooseLevel(best);
            }
            else if (r.Mode == Mode.Fuser)
            {
                var m = r.Menu;
                if (m.View == FuserView.Result) { r.CloseMenu(); return; }
                var evo = r.EvolveRecipes();
                if (evo.Count > 0) { r.FuserEvolve(evo[0]); return; }
                var combos = r.ComboPairs();
                bool slotsFull = r.Balls.Count >= Run.Slots && r.Balls.TrueForAll(b => b.Level >= 3 || b.Fused != null);
                if (combos.Count > 0 && slotsFull) { r.FuserCombine(combos[0][0], combos[0][1]); return; }
                r.FuserFree();
            }
        }
    }
}
