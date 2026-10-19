// Balls: aiming, volleys (with hero twists), flight, bounces, pierce, shields, catching, recall, side balls.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        // ------------------------------------------------------------ aiming (spec 4.1)
        public float? AimAngle()
        {
            Enemy t = null;
            if (P.Target >= 0)
            {
                t = Enemies.Find(e => e.Id == P.Target && Visible(e) && !Invulnerable(e));
                if (t == null) P.Target = -1;
            }
            if (t == null)
            {
                float best = -1;
                foreach (var e in Enemies)
                {
                    if (!Visible(e) || Invulnerable(e) || e.Mount != null) continue;
                    float score = e.Bottom - M.Abs(e.Cx - P.X) * 0.15f;
                    if (score > best) { best = score; t = e; }
                }
            }
            if (t == null) return null;
            float tx = t.Cx, ty = t.Boss ? t.Y + t.H * 0.75f : t.Cy;
            return ClampAim(M.Atan2(-(ty - P.Y), tx - P.X) / M.Deg);
        }

        static float ClampAim(float a)
        {
            // pointing below the player snaps to the nearer limit: -90..20 -> 20, everything else -> 160
            if (a < 20 || a > 160) a = (a < 20 && a > -90) ? 20 : 160;
            return a;
        }

        int BabiesAvailable() { return System.Math.Max(0, Babies - BabiesOut - BabyCharge.Count); }
        bool HasTarget()
        {
            foreach (var e in Enemies) if (Visible(e) && !Invulnerable(e) && e.Mount == null) return true;
            return false;
        }

        // ------------------------------------------------------------ volleys (spec 4.2)
        void StartVolley()
        {
            var mains = Balls.FindAll(b => b.State == BallState.Ready);
            int babies = BabiesAvailable();
            if (mains.Count == 0 && babies == 0) return;
            var q = new List<OwnedBall>();
            int fired = 0, n = mains.Count;
            for (int i = 0; i < n; i++)
            {
                int quota = M.Round(babies * i / (float)System.Math.Max(1, n));
                while (fired < quota) { q.Add(null); fired++; }
                q.Add(mains[i]);
            }
            while (fired < babies) { q.Add(null); fired++; }

            if (Tw == Twist.Fan)
            {
                // everything leaves in the same frame, alternating either side of the aim
                float? ang = AimAngle();
                if (ang == null) return;
                P.Aim = ang.Value;
                int count = q.Count;
                float spread = M.Min(count * M.PI / 24, 2 * M.PI / 5) / M.Deg;
                float step = count > 1 ? spread / (count - 1) : 0;
                for (int k = 0; k < count; k++)
                {
                    int j = k == 0 ? 0 : (k % 2 == 1 ? (k + 1) / 2 : -(k / 2));
                    Launch(q[k], ClampAim(ang.Value + j * step), false);
                }
                P.VolleySlow = 0.5f;
                return;
            }
            Volley = new Volley();
            Volley.Q.AddRange(q);
        }

        void StepVolley(float dt)
        {
            if (Volley == null)
            {
                if (HasTarget()) StartVolley();
                return;
            }
            var v = Volley;
            if (Tw == Twist.Scatter)
            {
                // rapid fire: anything that comes back joins the running volley
                foreach (var b in Balls) if (b.State == BallState.Ready && !v.Q.Contains(b)) v.Q.Add(b);
                int queuedBabies = v.Q.FindAll(x => x == null).Count;
                for (int i = queuedBabies; i < BabiesAvailable(); i++) v.Q.Add(null);
            }
            v.T -= dt;
            while (v.T <= 0 && v.Q.Count > 0)
            {
                float? ang = AimAngle();
                if (ang == null) { Volley = null; return; }
                P.Aim = ang.Value;
                var inst = v.Q[0]; v.Q.RemoveAt(0);
                if (inst != null ? inst.State == BallState.Ready : BabiesAvailable() > 0) Launch(inst, ang.Value, false);
                v.T += 1 / Fire;
            }
            if (v.Q.Count == 0 && Tw != Twist.Scatter) { Volley = null; P.VolleySlow = 0.5f; }
            if (v.Q.Count == 0 && Tw == Twist.Scatter && v.T <= 0) { Volley = null; P.VolleySlow = 0.5f; }
        }

        FieldBall Launch(OwnedBall inst, float angle, bool temp)
        {
            if (Tw == Twist.Scatter) angle += R.Range(-0.2f, 0.2f) / M.Deg;
            Sound(Sfx.Launch);
            float a = angle * M.Deg, v = V0;
            if (inst != null && inst.Stats.Has(K.Heavy)) v *= 1 - inst.Stats.G(K.Heavy) / 100f;
            var b = new FieldBall
            {
                Inst = inst, Baby = inst == null, Temp = temp, X = P.X, Y = P.Y - P.R,
                Vx = M.Cos(a) * v, Vy = -M.Sin(a) * v, V0 = v, R = inst != null ? 0.17f : 0.12f,
                Color = inst != null ? inst.Def.Color : 0xe8ecff, Color2 = inst != null && inst.Fused != null ? Content.Balls[inst.Fused].Color : 0
            };
            if (inst != null) inst.State = BallState.Field; else if (!temp) BabiesOut++;
            Field.Add(b);
            return b;
        }

        /// <summary>Side balls (egg babies, split copies): temporary, vanish when they leave the board.</summary>
        void SpawnSide(float x, float y, float dx, float dy, OwnedBall inst, bool clone)
        {
            if (Field.Count >= SideBallCap) return;
            float v = V0;
            Field.Add(new FieldBall
            {
                Inst = clone ? inst : null, Baby = !clone, Clone = clone, Temp = true, X = x, Y = y,
                Vx = dx * v, Vy = dy * v, V0 = v, R = clone ? 0.15f : 0.11f,
                Color = clone ? inst.Def.Color : 0xf0e6c8, Color2 = 0
            });
        }

        void ReturnBall(FieldBall b)
        {
            b.Gone = true;
            if (b.Temp) return;
            if (b.Inst != null) { if (b.Inst.State == BallState.Field) b.Inst.State = BallState.Ready; }
            else { BabiesOut--; BabyCharge.Add(Cooldown); }
        }

        void DestroyBall(FieldBall b)
        {
            b.Gone = true;
            if (b.Temp) return;
            if (b.Inst != null)
            {
                float cd = b.Inst.Stats.Has(K.Cooldown) ? b.Inst.Stats.G(K.Cooldown) : Cooldown;
                b.Inst.State = BallState.Cool; b.Inst.Cd = cd; b.Inst.CdMax = cd;
            }
            else { BabiesOut--; BabyCharge.Add(Cooldown); }
        }

        // ------------------------------------------------------------ bounces (spec 4.3 / 4.4)
        void Bounced(FieldBall b)
        {
            b.Hit.Clear();
            b.Bounces++;
            float sp = M.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy), ns = M.Min(b.V0 * 2, sp + Accel);
            if (sp > 0) { b.Vx *= ns / sp; b.Vy *= ns / sp; }
            if (Tw == Twist.Scatter)
            {
                float j = R.Range(-0.2f, 0.2f), c = M.Cos(j), s = M.Sin(j);
                float vx = b.Vx * c - b.Vy * s, vy = b.Vx * s + b.Vy * c;
                b.Vx = vx; b.Vy = vy;
            }
        }

        static void Reflect(FieldBall b, float nx, float ny)
        {
            float dot = b.Vx * nx + b.Vy * ny;
            if (dot < 0) { b.Vx -= 2 * dot * nx; b.Vy -= 2 * dot * ny; }
        }

        struct Contact { public float Nx, Ny, Pen; }

        static bool CircleRect(FieldBall b, Enemy e, out Contact c)
        {
            float cx = M.Clamp(b.X, e.X, e.X + e.W), cy = M.Clamp(b.Y, e.Y, e.Y + e.H);
            float dx = b.X - cx, dy = b.Y - cy, d2 = dx * dx + dy * dy;
            c = new Contact();
            if (d2 > b.R * b.R) return false;
            if (d2 > 1e-9f) { float d = M.Sqrt(d2); c.Nx = dx / d; c.Ny = dy / d; c.Pen = b.R - d; return true; }
            // centre inside: push out along the least-penetration axis
            float l = b.X - e.X, r = e.X + e.W - b.X, t = b.Y - e.Y, bo = e.Y + e.H - b.Y, m = M.Min(M.Min(l, r), M.Min(t, bo));
            if (m == l) { c.Nx = -1; c.Pen = l + b.R; }
            else if (m == r) { c.Nx = 1; c.Pen = r + b.R; }
            else if (m == t) { c.Ny = -1; c.Pen = t + b.R; }
            else { c.Ny = 1; c.Pen = bo + b.R; }
            return true;
        }

        bool ShieldBlocks(Enemy e, Contact c)
        {
            if (e.Brain != null) return e.Brain.Blocks(e, c.Nx, c.Ny);
            return (e.ShieldF && c.Ny > 0.5f) || (e.ShieldL && c.Nx < -0.5f) || (e.ShieldR && c.Nx > 0.5f);
        }

        bool Ghosted(FieldBall b)
        {
            return !b.Top && (ItemLv("veil") > 0 || ItemLv("mantle") > 0);
        }

        // ------------------------------------------------------------ flight
        void StepBall(FieldBall b, float dt)
        {
            if (b.Recall) { StepRecall(b, dt); return; }
            var st = b.St;
            if (b.BossCd > 0) b.BossCd -= dt;
            if (b.FlashCd > 0) b.FlashCd -= dt;
            if (b.SpeedMul > 1) b.SpeedMul = M.Max(1, b.SpeedMul - 3 * dt);
            else if (b.SpeedMul < 1) b.SpeedMul = M.Min(1, b.SpeedMul + 2 * dt);
            float sp = M.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy) * b.SpeedMul;
            int steps = System.Math.Max(1, M.Ceil(sp * dt / (b.R * 0.7f)));
            float h = dt / steps * b.SpeedMul;
            for (int s = 0; s < steps && !b.Gone && !b.Recall; s++)
            {
                b.X += b.Vx * h; b.Y += b.Vy * h;
                // side walls
                if (b.X < b.R) { b.X = b.R; b.Vx = M.Abs(b.Vx); WallHit(b, false); }
                else if (b.X > Bw - b.R) { b.X = Bw - b.R; b.Vx = -M.Abs(b.Vx); WallHit(b, false); }
                // the far wall: invisible, in the fog at the corridor's farthest visible line (spec §4.4)
                if (b.Y < FarY + b.R) { b.Y = FarY + b.R; b.Vy = M.Abs(b.Vy); WallHit(b, true); if (b.Recall) break; }
                // out of the bottom: side balls simply vanish, others turn back to the shooter
                if (b.Y > Bh + b.R) { if (b.Temp) b.Gone = true; else b.Recall = true; break; }
                // catch box (paddle heroes swat instead)
                if (b.Vy > 0 && M.Abs(b.X - P.X) < 0.5f * CatchW && M.Abs(b.Y - P.Y) < 0.5f)
                {
                    if (Tw == Twist.Paddle) Swat(b);
                    else { Caught(b); break; }
                }
                for (int i = 0; i < Enemies.Count && !b.Gone; i++)
                {
                    var e = Enemies[i];
                    if (e.Dead || e.Mount != null || e.Flying || e.Under) continue;
                    Contact c;
                    if (!CircleRect(b, e, out c)) continue;
                    bool fromAbove = b.Vy > 0 && c.Ny < -0.5f;
                    bool ghost = st != null && st.Has(K.Pierce);
                    bool pierce = ghost || (st != null && st.Has(K.Backstab) && fromAbove) || Ghosted(b)
                        || (Tw == Twist.PierceToWall && !b.WallTouched) || Has(e, "charm");
                    bool already = b.Hit.Contains(e.Id) || (e.Boss && b.BossCd > 0);
                    bool blocked = !ghost && !already && !Invulnerable(e) && ShieldBlocks(e, c);
                    if (blocked)
                    {
                        b.Hit.Add(e.Id);
                        Burst(b.X, b.Y, 0xc8d0e0, 3);
                        pierce = false;
                    }
                    else if (!already && !Invulnerable(e))
                    {
                        b.Hit.Add(e.Id);
                        b.Idle = 0;
                        if (e.Boss) b.BossCd = 0.2f;
                        bool crown = e.Brain != null && e.Brain.CrownHit(e, c.Nx, c.Ny);
                        HitEnemy(b, e, new HitInfo { Crown = crown, Backstab = st != null && st.Has(K.Backstab) && fromAbove, Nx = c.Nx, Ny = c.Ny });
                        if (st != null && st.Has(K.SelfDestruct)) { DestroyBall(b); break; }
                        if (!e.Dead && e.Def != null && e.Def.Swallow > 0 && !Has(e, "freeze") && e.Swallowed.Count < e.Def.Swallow)
                        {
                            b.Swallowed = true; b.Gone = true;
                            e.Swallowed.Add(b);
                            Text(e.Cx, e.Y, "GULP", 0xc58bff, 0.8f);
                            break;
                        }
                    }
                    if (!pierce)
                    {
                        b.X += c.Nx * c.Pen; b.Y += c.Ny * c.Pen;
                        bool wasHit = b.Hit.Contains(e.Id);
                        Reflect(b, c.Nx, c.Ny); Bounced(b);
                        if (wasHit) b.Hit.Add(e.Id);
                        if (Invulnerable(e) || blocked) b.Idle++;
                    }
                }
                if (b.Gone) break;
                for (int j = 0; j < Shots.Count; j++)
                {
                    var sh = Shots[j];
                    if (sh.Dead || sh.Hp < 0) continue;
                    float ddx = b.X - sh.X, ddy = b.Y - sh.Y, rr = b.R + sh.R;
                    if (ddx * ddx + ddy * ddy < rr * rr)
                    {
                        float dl = M.Sqrt(ddx * ddx + ddy * ddy); if (dl < 1e-4f) dl = 1;
                        if (--sh.Hp <= 0) { sh.Dead = true; Burst(sh.X, sh.Y, 0xffcf8a, 4); }
                        if (!(st != null && st.Has(K.Pierce))) { Reflect(b, ddx / dl, ddy / dl); Bounced(b); }
                    }
                }
            }
            if (b.Idle >= 20 && !b.Recall) b.Recall = true;
            if (st != null && st.Has(K.AuraR) && !b.Gone) StepAura(b, st, dt);
        }

        void WallHit(FieldBall b, bool top)
        {
            b.WallTouched = true;
            b.Idle++;
            Sound(Sfx.Wall);
            if (top)
            {
                b.Top = true;
                if (Tw == Twist.Ricochet && !b.Temp) { b.Recall = true; return; }
            }
            Bounced(b);
        }

        void StepRecall(FieldBall b, float dt)
        {
            // turn toward the shooter within 0.25 s and reach speed 8 within 0.125 s (spec 4.5)
            b.RecallT += dt;
            float dx = P.X - b.X, dy = P.Y - b.Y, d = M.Sqrt(dx * dx + dy * dy);
            if (d < 1e-4f) d = 1e-4f;
            float sp = M.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            sp += (8 - sp) * M.Min(1, dt / 0.125f);
            float a = M.Atan2(b.Vy, b.Vx), want = M.Atan2(dy, dx);
            if (b.RecallT >= 0.25f) a = want;
            else
            {
                float diff = want - a;
                while (diff > M.PI) diff -= 2 * M.PI;
                while (diff < -M.PI) diff += 2 * M.PI;
                float maxTurn = M.PI * dt / 0.25f;
                a += M.Clamp(diff, -maxTurn, maxTurn);
            }
            b.Vx = M.Cos(a) * sp; b.Vy = M.Sin(a) * sp;
            b.X += b.Vx * dt; b.Y += b.Vy * dt;
            foreach (var e in Enemies)
            {
                Contact c;
                if (!e.Dead && e.Mount == null && !b.Hit.Contains(e.Id) && CircleRect(b, e, out c))
                {
                    b.Hit.Add(e.Id);
                    if (!ShieldBlocks(e, c)) HitEnemy(b, e, new HitInfo { Nx = c.Nx, Ny = c.Ny });
                }
            }
            if (d < 0.25f || (Mode == Mode.Dying && d < 0.6f) || b.RecallT > 4) ReturnBall(b);
        }

        void StepAura(FieldBall b, Stats st, float dt)
        {
            b.AuraT -= dt;
            if (b.AuraT > 0) return;
            b.AuraT = 0.125f;
            float r = st.G(K.AuraR);
            foreach (var e in Enemies.ToArray())
            {
                if (e.Dead || b.Aura.Contains(e.Id) || Invulnerable(e)) continue;
                float nx = M.Clamp(b.X, e.X, e.X + e.W) - b.X, ny = M.Clamp(b.Y, e.Y, e.Bottom) - b.Y;
                if (nx * nx + ny * ny > r * r) continue;
                b.Aura.Add(e.Id);
                if (!e.Boss) ApplyTimed(e, "slow", 3, st.G(K.AuraSlow));
                float sc = Aoe * (st.G(K.Mult) > 100 ? st.G(K.Mult) / 100f : 1);
                DealDamage(e, RollInt(st.G(K.AuraMin), st.G(K.AuraMax)) * sc, 0xa8f0ff, false, true);
                if (!e.Dead && st.Has(K.FreezeChance) && R.Chance(st.G(K.FreezeChance) / 100f * 0.25f)) Freeze(e, st.G(K.FreezeDur));
            }
        }

        // ------------------------------------------------------------ catch / swat
        void Caught(FieldBall b)
        {
            b.Recall = true;
            Sound(Sfx.Catch);
            Text(b.X, b.Y - 0.4f, "catch!", 0x9fe8ff, 0.7f);
            Echo(b);
        }

        void Swat(FieldBall b)
        {
            float off = M.Clamp((b.X - P.X) / (0.5f * CatchW), -1, 1);
            float a = (90 - off * 60) * M.Deg;
            float sp = M.Max(M.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy), b.V0);
            b.Vx = M.Cos(a) * sp; b.Vy = -M.Sin(a) * sp;
            b.Y = P.Y - 0.5f;
            b.Paddle = System.Math.Min(3, b.Paddle + 1);
            b.SpeedMul = 1.4f;
            b.Idle = 0;
            b.Hit.Clear();
            Sound(Sfx.Catch);
            Text(b.X, b.Y - 0.4f, "swat x" + (b.Paddle + 1), 0x7fd8ff, 0.7f);
            Echo(b);
        }

        void Echo(FieldBall b)
        {
            float chance = ItemLv("halo") > 0 ? 100 : ItemVal("echo");
            if (b.Temp || chance <= 0 || !R.Chance(chance / 100f)) return;
            float? ang = AimAngle();
            if (ang != null) Launch(null, ang.Value, true);
        }

        // ------------------------------------------------------------ swallowed balls
        void SpitAll(Enemy e)
        {
            if (e.Swallowed.Count == 0) return;
            foreach (var b in e.Swallowed)
            {
                float a = R.Range(200, 340) * M.Deg;
                float sp = M.Max(b.V0, 4);
                b.Gone = false; b.Swallowed = false; b.Recall = false; b.Idle = 0; b.Hit.Clear();
                b.X = M.Clamp(e.Cx, b.R, Bw - b.R); b.Y = M.Min(e.Bottom + b.R + 0.05f, Bh - 0.2f);
                b.Vx = M.Cos(a) * sp; b.Vy = -M.Sin(a) * sp;
                b.Hit.Add(e.Id);
                Field.Add(b);
            }
            e.Swallowed.Clear();
        }

        // If every ball is swallowed, force them all home (spec 4.5).
        void CheckAllSwallowed()
        {
            bool any = false;
            foreach (var e in Enemies) if (e.Swallowed.Count > 0) { any = true; break; }
            if (!any) return;
            foreach (var b in Field) if (!b.Temp) return;
            foreach (var b in Balls) if (b.State != BallState.Field) return;
            foreach (var e in Enemies)
            {
                foreach (var b in e.Swallowed) { b.Swallowed = false; ReturnBall(b); }
                e.Swallowed.Clear();
            }
            Text(P.X, P.Y - 1, "balls recalled", 0xc58bff, 0.8f);
        }
    }
}
