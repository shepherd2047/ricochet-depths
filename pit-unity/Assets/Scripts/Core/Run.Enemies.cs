// Enemies: advance and blocking, stacks, flyers, burrowers, side-steppers, charm, melee and ranged abilities.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        readonly List<Enemy> moved = new List<Enemy>();

        // foes lunge at the hero (and blow up) when they touch the bottom edge of the screen, or when the hero touches them
        public float AttackLine { get { return (float.IsNaN(ViewBottom) ? Bh : M.Max(Bh, ViewBottom)) - 0.05f; } }

        void StepEnemies(float dt)
        {
            float tl = L.TurnLen;
            var list = Enemies.FindAll(e => !e.Dead && !e.Boss && e.Mount == null);
            list.Sort((a, b) => b.Bottom.CompareTo(a.Bottom));
            moved.Clear();
            // haste auras refresh first so everyone sees them this frame
            foreach (var e in list)
            {
                if (e.Pod || !e.Def.Haste || e.Entry > 0) continue;
                foreach (var o in list)
                    if (o != e && RectDist(e, o) <= 1) o.HasteT = 0.3f;
            }
            foreach (var e in list)
            {
                e.Flash = M.Max(0, e.Flash - dt);
                e.HasteT = M.Max(0, e.HasteT - dt);
                e.SpikeCd = M.Max(0, e.SpikeCd - dt);
                StepStatus(e, dt);
                if (e.Dead) continue;
                if (e.Pod) { if (e.Entry > 0) e.Entry -= dt; continue; }   // pods are steered by their boss
                if (e.Entry > 0) { e.Entry -= dt; moved.Add(e); continue; }
                if (e.Falling)
                {
                    e.Y += 7 * dt;
                    if (e.Y >= e.FallY) { e.Y = e.FallY; e.Falling = false; }
                    moved.Add(e);
                    continue;
                }
                bool frozen = Has(e, "freeze"), charmed = Has(e, "charm");
                if (!frozen) StepStyle(e, dt, tl);
                // ---- advance
                if (!frozen && e.Atk == null)
                {
                    float baseSp = CELL / tl * e.Def.Spd * Descent, sp = baseSp;
                    if (e.Def.FlyGround != null) sp = e.Flying ? baseSp * 3 : 0;
                    if (e.Under) sp = baseSp * 1.5f;
                    Status s;
                    if (e.St.TryGetValue("slow", out s)) sp *= 1 - (s.Pct > 0 ? s.Pct : 30) / 100f;
                    if (Has(e, "poison")) sp *= 0.8f;
                    if (e.HasteT > 0 && sp > 0) sp = M.Max(sp, CELL / tl * Descent);
                    if (charmed)
                    {
                        e.Y = M.Max(0, e.Y - CELL / tl * dt);
                    }
                    else
                    {
                        float dy = sp * dt;
                        if (!e.Flying && !e.Under)
                        {
                            foreach (var o in moved)
                                if (o.X < e.X + e.W - 0.01f && o.X + o.W > e.X + 0.01f && o.Y >= e.Bottom - 0.02f) dy = M.Min(dy, M.Max(0, o.Y - e.Bottom));
                            if (Boss != null && !Boss.BossDef.Final)
                            {
                                var bo = Boss;
                                if (bo.X < e.X + e.W && bo.X + bo.W > e.X && bo.Y >= e.Bottom - 0.02f) dy = M.Min(dy, M.Max(0, bo.Y - e.Bottom));
                            }
                        }
                        e.Y += dy;
                    }
                }
                if (!frozen) StepSideStep(e, dt, tl);
                if (!e.Flying && !e.Under) moved.Add(e);
                if (e.Y > Bh + 1) { RemoveStack(e); continue; }
                if (Invulnerable(e)) continue;
                if (charmed) { CharmTick(e, dt, tl); continue; }
                StepMelee(e, dt, tl, frozen);
                if (e.Dead || frozen) continue;
                StepAbilities(e, e, dt, tl);
            }
            // riders sit on their mount; they shoot and heal, the base does the walking and fighting
            foreach (var r in Enemies)
            {
                if (r.Dead || r.Mount == null) continue;
                var m = r.Mount;
                r.X = m.Cx - r.W / 2 + m.Ox;
                r.Y = m.Y - r.H * 0.38f + m.Oy;
                r.Entry = m.Entry;
                r.Flash = M.Max(0, r.Flash - dt);
                StepStatus(r, dt);
                if (r.Dead || Invulnerable(r) || Has(r, "freeze")) continue;
                StepAbilities(r, r, dt, tl);
            }
            var boss = Boss;
            if (boss != null && !boss.Dead)
            {
                boss.Flash = M.Max(0, boss.Flash - dt);
                StepStatus(boss, dt);
                if (!boss.Dead) boss.Brain.Update(boss, dt);
            }
        }

        static float RectDist(Enemy a, Enemy b)
        {
            float dx = M.Max(0, M.Max(a.X - (b.X + b.W), b.X - (a.X + a.W)));
            float dy = M.Max(0, M.Max(a.Y - b.Bottom, b.Y - a.Bottom));
            return M.Sqrt(dx * dx + dy * dy);
        }

        void RemoveStack(Enemy e)
        {
            e.Dead = true;
            SpitAll(e);
            WaveGone(e);
            foreach (var o in Enemies) if (o.Mount == e && !o.Dead) RemoveStack(o);
        }

        // ---- flyers and burrowers alternate between a grounded phase and an invulnerable one
        void StepStyle(Enemy e, float dt, float tl)
        {
            var d = e.Def;
            if (d.FlyGround != null)
            {
                e.StyleT -= dt;
                if (e.StyleT > 0) return;
                if (!e.Flying) { e.Flying = true; e.Atk = null; e.StyleT = R.Range(d.FlyAir[0], d.FlyAir[1]) * tl; }
                else if (!Blocked(e, e)) { e.Flying = false; e.StyleT = R.Range(d.FlyGround[0], d.FlyGround[1]) * tl; }
                else e.StyleT = 0.2f;
            }
            else if (d.BurrowUp != null)
            {
                if (e.SpitT > 0)
                {
                    e.SpitT -= dt;
                    if (e.SpitT <= 0) FireShot(e, ShotKind.Venom, d.ShotDmg, d.ShotSpeed, 1);
                }
                e.StyleT -= dt;
                if (e.StyleT > 0) return;
                if (!e.Under) { e.Under = true; e.Atk = null; e.StyleT = R.Range(d.BurrowDown[0], d.BurrowDown[1]) * tl; }
                else if (!Blocked(e, e)) { e.Under = false; e.StyleT = R.Range(d.BurrowUp[0], d.BurrowUp[1]) * tl; e.SpitT = 1; }
                else e.StyleT = 0.2f;
            }
        }

        void StepSideStep(Enemy e, float dt, float tl)
        {
            var d = e.Def;
            if (d.StepEvery == null || e.Atk != null) return;
            if (e.StepProg >= 0)
            {
                e.StepProg = M.Min(1, e.StepProg + dt / 0.75f);
                e.X = M.Lerp(e.StepFromX, e.StepToX, e.StepProg);
                if (e.StepProg >= 1) e.StepProg = -1;
                return;
            }
            e.StepT -= dt;
            if (e.StepT > 0) return;
            e.StepT = R.Range(d.StepEvery[0], d.StepEvery[1]) * tl;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float tx = e.X + e.StepDir * CELL;
                if (tx >= 0 && tx + e.W <= Bw + 0.01f)
                {
                    var probe = new Enemy { X = M.Min(e.X, tx), Y = e.Y, W = e.W + CELL, H = e.H };
                    if (!Blocked(probe, e)) { e.StepFromX = e.X; e.StepToX = tx; e.StepProg = 0; return; }
                }
                e.StepDir = -e.StepDir;   // preferred side is blocked: try the other way next time
            }
        }

        // Charmed foes walk upward and trade blows with the foes they touch every quarter turn.
        void CharmTick(Enemy e, float dt, float tl)
        {
            e.Atk = null; e.Ox = e.Oy = 0;
            e.CharmTick -= dt;
            if (e.CharmTick > 0) return;
            e.CharmTick = tl / 4;
            float dmg = M.Max(10, HpBase(Turn, BossesPassed) / 3f);
            foreach (var o in Enemies.ToArray())
            {
                if (o == e || o.Dead || Has(o, "charm") || o.Mount != null) continue;
                if (RectDist(e, o) > 0.15f) continue;
                DealDamage(o, dmg, 0xff9df0);
                DealDamage(e, dmg, 0xff9df0, false, true);
                if (e.Dead) return;
            }
        }

        float StackMelee(Enemy e)
        {
            float m = e.Def.Melee;
            foreach (var o in Enemies) if (!o.Dead && IsAbove(o, e)) m += o.Def.Melee;
            return m;
        }
        static bool IsAbove(Enemy rider, Enemy baseE)
        {
            for (var m = rider.Mount; m != null; m = m.Mount) if (m == baseE) return true;
            return false;
        }

        void StepMelee(Enemy e, float dt, float tl, bool frozen)
        {
            float cx = e.Cx, cy = e.Cy;
            bool atLine = e.Bottom >= AttackLine;
            bool blind = Has(e, "blind");
            // the hero's hit circle touching the foe's box (nearest point of the box within P.R)
            float nx = M.Max(e.X, M.Min(P.X, e.X + e.W)), ny = M.Max(e.Y, M.Min(P.Y, e.Y + e.H));
            bool touch = (P.X - nx) * (P.X - nx) + (P.Y - ny) * (P.Y - ny) <= P.R * P.R;
            bool inRange = atLine || (!blind && touch);
            if (e.Atk == null)
            {
                if (inRange && Time - e.LastAtk >= 1 && Mode == Mode.Playing) e.Atk = new Attack { T = tl * e.Def.Windup };
                return;
            }
            if (!e.Atk.Lunge)
            {
                if (!frozen) e.Atk.T -= dt;
                if (!inRange) { e.Atk = null; return; }
                if (e.Atk.T <= 0) { e.Atk.Lunge = true; e.Atk.T = 0.25f; }
                return;
            }
            e.Atk.T -= dt;
            float k = 1 - e.Atk.T / 0.25f;
            e.Ox = (P.X - cx) * 0.6f * k; e.Oy = (P.Y - cy) * 0.6f * k;
            if (e.Atk.T > 0) return;
            if (blind && R.Chance(0.5f)) Text(cx, e.Y, "MISS", 0xcfd6ff, 0.9f);
            else
            {
                HurtPlayer(StackMelee(e), e.Def.Elite ? "elite" : "melee");
                Ddm = M.Max(1, Ddm - 1);
            }
            Burst(cx, cy, 0xff5d5d, 12);
            if (e.Def.Elite)
            {
                // elites survive and back off to the attack line
                e.Atk = null; e.Ox = e.Oy = 0; e.LastAtk = Time;
                e.Y = M.Min(e.Y, AttackLine - e.H) - CELL * 0.5f;
                return;
            }
            RemoveStack(e);
        }

        // ---- ranged and support abilities; `src` is where projectiles come from (a rider fires from on top of its stack)
        void StepAbilities(Enemy e, Enemy src, float dt, float tl)
        {
            var d = e.Def;
            float cx = src.Cx, cy = src.Cy;
            if (d.ShootEvery != null)
            {
                e.ShootT -= dt;
                if (e.ShootT <= 0)
                {
                    e.ShootT = R.Range(d.ShootEvery[0], d.ShootEvery[1]) * tl;
                    FireShot(src, d.Shot, d.ShotDmg, d.ShotSpeed, d.ShotCount);
                }
            }
            if (d.HealEvery != null)
            {
                e.AbilT -= dt;
                if (e.AbilT <= 0)
                {
                    e.AbilT = R.Range(d.HealEvery[0], d.HealEvery[1]) * tl;
                    float R2 = d.HealRange * CELL; bool any = false;
                    foreach (var o in Enemies)
                    {
                        if (o.Dead || o.Boss || o.Hp >= o.MaxHp) continue;
                        if (M.Abs(o.Cx - cx) <= R2 && M.Abs(o.Cy - cy) <= R2)
                        {
                            o.Hp = M.Min(o.MaxHp, o.Hp + M.Round(o.MaxHp * d.HealPct)); any = true;
                            Text(o.Cx, o.Y, "+", 0x5be07a, 1.2f);
                        }
                    }
                    if (any) AddBeam(new Beam { Kind = BeamKind.Ring, X = cx, Y = cy, R = R2, T = 0.35f, Color = 0x5be07a });
                }
            }
            if (d.LobEvery != null)
            {
                e.AbilT -= dt;
                if (e.AbilT <= 0)
                {
                    e.AbilT = R.Range(d.LobEvery[0], d.LobEvery[1]) * tl;
                    Hazards.Add(new Hazard { Kind = HazardKind.Blast, X = P.X, Y = P.Y, R = d.LobR, T = d.LobWarn, Max = d.LobWarn, Dmg = d.LobDmg, FromX = cx, FromY = cy, Src = "mortar" });
                }
            }
            if (d.LaserEvery != null)
            {
                e.LaserT -= dt;
                if (e.LaserT <= 0)
                {
                    e.LaserT = R.Range(d.LaserEvery[0], d.LaserEvery[1]) * tl;
                    float dx = P.X - cx, dy = P.Y - cy, l = M.Sqrt(dx * dx + dy * dy);
                    if (l < 1e-3f) l = 1;
                    Hazards.Add(new Hazard { Kind = HazardKind.Beam, X = cx, Y = src.Bottom, Dx = dx / l, Dy = dy / l, W = d.LaserW, T = d.LaserWarn, Max = d.LaserWarn, Dmg = d.LaserDmg, Src = "laser" });
                }
            }
            if (d.HatchEvery != null)
            {
                e.HatchT -= dt;
                if (e.HatchT <= 0)
                {
                    e.HatchT = R.Range(d.HatchEvery[0], d.HatchEvery[1]) * tl;
                    Hatch(e);
                }
            }
        }

        void Hatch(Enemy e)
        {
            float mw = Content.Enemies["mite"].W * CELL;
            float[][] spots = { new[] { e.X - mw - 0.02f, e.Y + e.H - mw }, new[] { e.X + e.W + 0.02f, e.Y + e.H - mw }, new[] { e.X + (e.W - mw) / 2, e.Bottom + 0.02f }, new[] { e.X + (e.W - mw) / 2, e.Y - mw - 0.02f } };
            foreach (var s in spots)
            {
                if (s[0] < 0 || s[0] + mw > Bw) continue;
                var m = MakeEnemy("mite", s[0], s[1], Turn, 0);
                if (Blocked(m, null)) continue;
                m.Entry = 0.3f;
                deferred.Add(m);
                Burst(m.Cx, m.Cy, e.Def.Color, 5);
                return;
            }
        }

        public void FireShot(Enemy src, ShotKind kind, float dmg, float speed, int count)
        {
            float cx = src.Cx, by = src.Bottom;
            float a = M.Atan2(P.Y - src.Cy, P.X - cx);
            if (Has(src, "blind")) a += (R.Chance(0.5f) ? 1 : -1) * R.Range(0, 45) * M.Deg;
            for (int i = 0; i < count; i++)
            {
                float aa = a + (i - (count - 1) / 2f) * 12 * M.Deg;
                bool hard = kind == ShotKind.Fireball || kind == ShotKind.Venom || kind == ShotKind.Boulder;
                float r = kind == ShotKind.Fireball ? 0.18f : kind == ShotKind.Venom ? 0.15f : kind == ShotKind.Boulder ? 0.3f : 0.12f;
                Shots.Add(new Shot { X = cx, Y = by, Vx = M.Cos(aa) * speed, Vy = M.Sin(aa) * speed, R = r, Dmg = dmg, Hp = hard ? -1 : 1, Kind = kind });
            }
        }

        void StepStatus(Enemy e, float dt)
        {
            if (e.St.Count == 0) return;
            var keys = new List<string>(e.St.Keys);
            foreach (var k in keys)
            {
                var s = e.St[k];
                s.T -= dt;
                if (s.Stacks > 0)
                {
                    s.Acc += dt;
                    if (s.Acc >= 1)
                    {
                        s.Acc -= 1;
                        uint col = k == "poison" ? 0x7ee35au : k == "ff" ? 0xc58bffu : 0xff9447u;
                        DealDamage(e, s.Dmg * s.Stacks, col);
                        if (e.Dead) return;
                    }
                }
                if (s.T <= 0) e.St.Remove(k);
            }
        }
    }
}
