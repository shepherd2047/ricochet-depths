// Damage: direct hits, statuses, area effects, kills, drops, player damage and healing.
using System.Collections.Generic;

namespace Pit.Core
{
    public struct HitInfo
    {
        public bool Crown, Backstab;
        public float Nx, Ny;   // contact normal (rect -> ball)
    }

    public sealed partial class Run
    {
        public bool Won;

        public static bool Overlap(Enemy a, Enemy b) { return a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y; }

        public bool Invulnerable(Enemy e)
        {
            if (e.Entry > 0 || e.Y + e.H * 0.5f < 0 || e.Flying || e.Under || e.Falling) return true;
            if (e.Brain != null) return e.Brain.Sealed(e);
            return false;
        }
        public static bool Visible(Enemy e) { return !e.Dead && e.Y + e.H > 0.05f; }

        int GemValue(Enemy e)
        {
            float mult = e.Boss ? e.BossDef.HpMult : e.Def.Hp;
            return M.Round(mult * (M.Floor((30 + 5 * e.Turn) / 75f) + 1) * L.XpMult);
        }

        int RollInt(float lo, float hi) { return R.Int(M.Round(lo), M.Round(M.Max(lo, hi))); }

        public int DealDamage(Enemy e, float raw, uint color = 0xffffff, bool crit = false, bool quiet = false)
        {
            if (e.Dead || Invulnerable(e)) return 0;
            int dmg = System.Math.Max(1, M.Ceil(raw / Ddm));
            e.Hp -= dmg; e.Flash = 0.08f;
            Stats.Dmg += dmg;
            if (dmg > Stats.BestHit) Stats.BestHit = dmg;
            if (!quiet) Text(e.Cx + (R.Next() - 0.5f) * 0.4f, e.Y + e.H * 0.4f, dmg.ToString(), crit ? 0xffe14du : color, crit ? 1.4f : 1);
            if (e.Boss) BossShed(e);
            if (e.Brain != null && e.Hp > 0) e.Brain.OnDamaged(e, dmg);
            if (e.Hp <= 0) KillEnemy(e, false);
            return dmg;
        }

        void BossShed(Enemy e)
        {
            while (e.Hp > 0 && e.Hp <= e.MaxHp * e.NextShed / 6f && e.XpLeft > 100)
            {
                int v = (e.XpTotal - 100) / 6 + 1;
                e.XpLeft -= v; e.NextShed--;
                DropGem(e.X + R.Next() * e.W, e.Bottom, v);
            }
        }

        // ------------------------------------------------------------ statuses on enemies (spec 3.5)
        void ApplyStack(Enemy e, string key, float dmg, float cap, float dur)
        {
            Status s;
            if (e.St.TryGetValue(key, out s)) { s.Dmg = M.Max(s.Dmg, dmg); s.Stacks = System.Math.Min((int)cap, s.Stacks + 1); s.T = M.Max(s.T, dur); }
            else e.St[key] = new Status { Dmg = dmg, Stacks = 1, T = dur };
            // burn climbs up a stack
            if (key == "burn")
                foreach (var o in Enemies) if (o.Mount == e && !o.Dead) ApplyStack(o, key, dmg, cap, dur);
        }
        public void ApplyTimed(Enemy e, string key, float dur, float pct = 0)
        {
            Status s;
            if (e.St.TryGetValue(key, out s)) { s.T = M.Max(s.T, dur); if (pct > 0) s.Pct = M.Max(s.Pct, pct); }
            else e.St[key] = new Status { T = dur, Pct = pct };
        }
        public bool Has(Enemy e, string key) { return e.St.ContainsKey(key); }

        public void Freeze(Enemy e, float dur)
        {
            if (e.Boss || e.Pod) return;
            ApplyTimed(e, "freeze", dur);
            Text(e.Cx, e.Y, "FROZEN", 0x9fe8ff, 0.8f);
            SpitAll(e);
        }

        // ------------------------------------------------------------ direct hits (spec 4.6)
        void HitEnemy(FieldBall b, Enemy e, HitInfo info)
        {
            if (Invulnerable(e)) return;
            var st = b.St;
            float baseDmg;
            if (st == null) baseDmg = RollInt(BabyMin, BabyMax);
            else
            {
                int l = b.Inst.Level - 1;
                float mult = st.G(K.Mult) / 100f;
                baseDmg = RollInt((18 + 6 * l + st.G(K.BonusMin)) * mult * Str, (32 + 12 * l + st.G(K.BonusMax)) * mult * Str);
                if (b.Clone) baseDmg = M.Max(1, M.Round(baseDmg * 0.5f));
            }
            float adds = 0;
            if (Has(e, "freeze") || Has(e, "ff")) adds += 0.25f;
            if (info.Backstab) adds += 0.3f;
            if (Tw == Twist.Ricochet) adds += M.Min(1, 0.05f * b.Bounces);
            if (b.Paddle > 0) adds += System.Math.Min(3, b.Paddle);
            if (!b.Top && (ItemLv("veil") > 0 || ItemLv("mantle") > 0)) adds += (ItemVal("veil") + (ItemLv("mantle") > 0 ? 30 : 0)) / 100f;
            float dmg = baseDmg * (1 + adds);
            if (st != null) dmg *= 1 + (ItemVal("knuckles") + (ItemLv("signet") > 0 ? 30 : 0)) / 100f;
            float noCrit = (1 - Crit) * (1 - ItemVal("pin") / 100f) * (1 - ItemVal("signet") / 100f);
            bool crit = info.Crown || R.Next() > noCrit;
            if (crit) dmg += ((ItemLv("signet") > 0 ? 3 : 2) - 1) * baseDmg;
            if (e.Bleed > 0) dmg += e.Bleed;
            if (st != null && st.Has(K.BleedCap)) e.Bleed = M.Min(st.G(K.BleedCap), e.Bleed + 2);
            Sound(crit ? Sfx.Crit : Sfx.Hit);
            DealDamage(e, dmg, 0xffffff, crit);
            if (e.Def != null && e.Def.Spiked && !e.Dead) SpikeBack(e);
            if (st != null)
            {
                float half = b.Clone ? 0.5f : 1;
                if (!e.Dead) ApplyEffects(e, st, half, 1);
                ApplyArea(b, e, st, info, half);
                if (st.Has(K.Leech) && R.Chance(st.G(K.Leech) / 100f)) Heal(1);
            }
        }

        // Hit effects from a ball's stats. k scales chances (area triggers use 0.25 for freeze), half halves values for side balls.
        void ApplyEffects(Enemy e, Stats st, float half, float chanceK)
        {
            float sk = StatusK * (st.G(K.Mult) > 100 ? st.G(K.Mult) / 100f : 1) * half;
            if (st.Has(K.BurnMin)) ApplyStack(e, "burn", M.Max(1, RollInt(st.G(K.BurnMin), st.G(K.BurnMax)) * sk * BurnBoost), st.G(K.BurnCap), st.G(K.BurnDur));
            if (st.Has(K.FfMin)) ApplyStack(e, "ff", M.Max(1, RollInt(st.G(K.FfMin), st.G(K.FfMax)) * sk * BurnBoost), st.G(K.FfCap), st.G(K.FfDur));
            if (st.Has(K.PoisonMin)) ApplyStack(e, "poison", M.Max(1, RollInt(st.G(K.PoisonMin), st.G(K.PoisonMax)) * sk), st.G(K.PoisonCap), st.G(K.PoisonDur));
            if (st.Has(K.FreezeChance) && R.Chance(st.G(K.FreezeChance) / 100f * chanceK)) Freeze(e, st.G(K.FreezeDur));
            if (st.Has(K.BlindDur)) ApplyTimed(e, "blind", st.G(K.BlindDur));
            if (st.Has(K.CharmChance) && !e.Boss && !e.Pod && R.Chance(st.G(K.CharmChance) / 100f * chanceK))
            {
                ApplyTimed(e, "charm", 5);
                e.Atk = null;
                Text(e.Cx, e.Y, "CHARMED", 0xff9df0, 0.8f);
            }
        }

        // Area effects, one per direct hit (spec 4.8). Each target rolls its own damage.
        void ApplyArea(FieldBall b, Enemy e, Stats st, HitInfo info, float half)
        {
            float cx = e.Cx, cy = e.Cy;
            float sc = Aoe * (st.G(K.Mult) > 100 ? st.G(K.Mult) / 100f : 1) * half;
            uint col = b.Color;
            if (st.Has(K.ChainMin))
            {
                var near = new List<KeyValuePair<float, Enemy>>();
                foreach (var o in Enemies)
                {
                    if (o == e || !Visible(o) || Invulnerable(o)) continue;
                    float dx = o.Cx - cx, dy = o.Cy - cy, d = M.Sqrt(dx * dx + dy * dy);
                    bool below = M.Abs(dx) < 0.6f && dy > 0;
                    if (d < CELL * 1.6f || below) near.Add(new KeyValuePair<float, Enemy>(d, o));
                }
                near.Sort((p, q) => p.Key.CompareTo(q.Key));
                float px = cx, py = cy;
                int n = System.Math.Min(near.Count, (int)st.G(K.ChainCount));
                for (int i = 0; i < n; i++)
                {
                    var o = near[i].Value;
                    AddBeam(new Beam { Kind = BeamKind.Arc, X1 = px, Y1 = py, X2 = o.Cx, Y2 = o.Cy, T = 0.18f, Color = 0xffe14d });
                    px = o.Cx; py = o.Cy;
                    DealDamage(o, RollInt(st.G(K.ChainMin), st.G(K.ChainMax)) * sc, 0xffe14d);
                }
            }
            if (st.Has(K.RowMin))
            {
                AddBeam(new Beam { Kind = BeamKind.Row, Y = cy, T = 0.2f, Color = col });
                foreach (var o in Enemies.ToArray())
                    if (o != e && Visible(o) && o.Y < cy && o.Bottom > cy) DealDamage(o, RollInt(st.G(K.RowMin), st.G(K.RowMax)) * sc, col);
            }
            if (st.Has(K.ColMin))
            {
                AddBeam(new Beam { Kind = BeamKind.Col, X = cx, T = 0.2f, Color = col });
                foreach (var o in Enemies.ToArray())
                    if (o != e && Visible(o) && o.X < cx && o.X + o.W > cx) DealDamage(o, RollInt(st.G(K.ColMin), st.G(K.ColMax)) * sc, col);
            }
            if (st.Has(K.QuakeMin))
            {
                float hs = st.G(K.QuakeSize) / 2;
                AddBeam(new Beam { Kind = BeamKind.Square, X = cx, Y = cy, R = hs, T = 0.3f, Color = 0xc89a62 });
                Shake = M.Max(Shake, 0.1f);
                foreach (var o in Enemies.ToArray())
                    if (o != e && Visible(o) && o.X < cx + hs && o.X + o.W > cx - hs && o.Y < cy + hs && o.Bottom > cy - hs)
                        DealDamage(o, RollInt(st.G(K.QuakeMin), st.G(K.QuakeMax)) * sc, 0xc89a62);
            }
            if (st.Has(K.BombMin))
            {
                float br = st.G(K.BombR);
                AddBeam(new Beam { Kind = BeamKind.Ring, X = b.X, Y = b.Y, R = br, T = 0.3f, Color = 0xff8a3a });
                Shake = M.Max(Shake, 0.15f);
                foreach (var o in Enemies.ToArray())
                {
                    if (!Visible(o)) continue;
                    float nx = M.Clamp(b.X, o.X, o.X + o.W), ny = M.Clamp(b.Y, o.Y, o.Bottom);
                    if ((nx - b.X) * (nx - b.X) + (ny - b.Y) * (ny - b.Y) <= br * br) DealDamage(o, RollInt(st.G(K.BombMin), st.G(K.BombMax)) * sc, 0xff8a3a);
                }
            }
            if (st.Has(K.Nuke))
            {
                AddBeam(new Beam { Kind = BeamKind.Flash, T = 0.35f, Color = 0x9cff3c });
                foreach (var o in Enemies.ToArray()) if (Visible(o) && !Invulnerable(o)) ApplyEffects(o, st, half, 0.25f);
            }
            if (st.Has(K.FlashMin) && b.FlashCd <= 0)
            {
                b.FlashCd = 1;
                AddBeam(new Beam { Kind = BeamKind.Flash, T = 0.25f, Color = 0xfff6d0 });
                foreach (var o in Enemies.ToArray())
                {
                    if (!Visible(o) || Invulnerable(o) || o == e) continue;
                    DealDamage(o, RollInt(st.G(K.FlashMin), st.G(K.FlashMax)) * sc, 0xfff6d0, false, true);
                    if (!o.Dead) ApplyEffects(o, st, half, 0.25f);
                }
            }
            if (st.Has(K.EggMin) && Field.Count < SideBallCap)
            {
                int n = RollInt(st.G(K.EggMin), st.G(K.EggMax));
                float vx = b.Vx, vy = b.Vy, dot = vx * info.Nx + vy * info.Ny;
                if (dot < 0) { vx -= 2 * dot * info.Nx; vy -= 2 * dot * info.Ny; }
                float baseA = M.Atan2(vy, vx);
                for (int i = 0; i < n; i++)
                {
                    float a = baseA + R.Range(-30, 30) * M.Deg;
                    SpawnSide(b.X, b.Y, M.Cos(a), M.Sin(a), null, false);
                }
            }
            if (st.Has(K.SplitMax) && !b.Clone && b.Splits < st.G(K.SplitMax) && Field.Count < SideBallCap)
            {
                b.Splits++;
                float a = M.Atan2(b.Vy, b.Vx) + (R.Chance(0.5f) ? 1 : -1) * R.Range(25, 45) * M.Deg;
                SpawnSide(b.X, b.Y, M.Cos(a), M.Sin(a), b.Inst, true);
            }
        }

        void SpikeBack(Enemy e)
        {
            if (e.SpikeCd > 0) return;
            e.SpikeCd = 1;
            float a = R.Next() * M.PI * 2, d = R.Next() * 2;
            Hazards.Add(new Hazard { Kind = HazardKind.Blast, X = M.Clamp(P.X + M.Cos(a) * d, 0.3f, Bw - 0.3f), Y = M.Clamp(P.Y + M.Sin(a) * d, 0.3f, Bh - 0.3f),
                R = 0.5f, T = 1.5f, Max = 1.5f, Dmg = 10, Small = true, FromX = e.Cx, FromY = e.Cy, Src = "spikes" });
        }

        // ------------------------------------------------------------ deaths and drops
        public void KillEnemy(Enemy e, bool crushed)
        {
            if (e.Dead) return;
            e.Dead = true;
            Kills++;
            Sound(Sfx.Death);
            Burst(e.Cx, e.Cy, e.Color, e.Boss ? 60 : 10);
            SpitAll(e);
            if (e.Boss) { DropGem(e.Cx, e.Cy, e.XpLeft); OnBossDead(e); return; }
            if (e.Brain == null && e.Parent != null && e.Parent.Brain != null) e.Parent.Brain.OnPartDead(e.Parent, e);
            int xp = GemValue(e);
            DropGem(e.Cx, e.Cy, xp);
            if (R.Chance(0.02f + M.Min(0.25f, xp / 600f)))
            {
                int amt = R.Int(L.GoldMin, L.GoldMax) + (Ddm > 1 ? M.Round(2 * (Ddm - 1)) : 0);
                Drops.Add(new Drop { Kind = DropKind.Gold, X = e.Cx + 0.2f, Y = e.Cy, V = amt });
            }
            float ratio = P.Hp / (float)P.MaxHp;
            if (ratio < 0.5f && R.Chance((0.5f - ratio) * 0.05f)) Drops.Add(new Drop { Kind = DropKind.Heal, X = e.Cx, Y = e.Cy, V = 8 });
            if (e.Def.PopMax > 0 && !crushed) PopMites(e);
            // a stack loses its base: the next rider drops into place (invulnerable while falling)
            foreach (var o in Enemies)
                if (o.Mount == e && !o.Dead) { o.Mount = e.Mount; if (o.Mount == null) { o.Falling = true; o.X = e.X + (e.W - o.W) / 2; o.FallY = e.Bottom - o.H; } }
            WaveGone(e);
        }

        void PopMites(Enemy e)
        {
            int n = R.Int(e.Def.PopMin, e.Def.PopMax), tries = 0;
            float mw = Content.Enemies["mite"].W * CELL;
            while (n > 0 && tries++ < 24)
            {
                float x = M.Clamp(e.X + R.Range(-0.6f, e.W + 0.1f), 0, Bw - mw), y = e.Y + R.Range(-0.6f, e.H);
                var m = MakeEnemy("mite", x, y, Turn, 0);
                if (Blocked(m, null)) continue;
                deferred.Add(m); n--;
            }
        }

        /// <summary>True when the rect of <paramref name="m"/> overlaps a grounded foe (ignores flyers, burrowers, riders).</summary>
        bool Blocked(Enemy m, Enemy self)
        {
            foreach (var o in Enemies)
                if (o != m && o != self && !o.Dead && !o.Flying && !o.Under && o.Mount == null && !o.Pod && Overlap(m, o)) return true;
            foreach (var o in deferred) if (o != m && Overlap(m, o)) return true;
            return false;
        }

        void DropGem(float x, float y, int v) { if (v > 0) Drops.Add(new Drop { Kind = DropKind.Gem, X = x, Y = y, V = v }); }

        public void Heal(int n)
        {
            if (Mode != Mode.Playing) return;
            int before = P.Hp;
            P.Hp = System.Math.Min(P.MaxHp, P.Hp + n);
            if (P.Hp > before) Text(P.X, P.Y - 0.6f, "+" + (P.Hp - before), 0x6dff8e);
        }

        public void HurtPlayer(float baseDmg, string src)
        {
            if (Mode != Mode.Playing || P.Hp <= 0) return;
            int dmg = M.Ceil(M.Ceil(baseDmg * (1 + 0.2f * (L.D - 1))) / Defense);
            P.Hp -= dmg; P.HurtT = 0.3f;
            Sound(Sfx.Hurt);
            int prev; Stats.Hurt.TryGetValue(src, out prev); Stats.Hurt[src] = prev + dmg;
            Shake = M.Max(Shake, 0.2f);
            Text(P.X, P.Y - 0.6f, "-" + dmg, 0xff5d5d, 1.2f);
            if (P.Hp <= 0)
            {
                P.Hp = 0;
                Mode = Mode.Dying; ModeT = 2;
                foreach (var b in Field) b.Recall = true;
                Stats.Log.Add("died turn " + Turn + " t=" + (int)Time + " to " + src);
            }
        }

        void OnBossDead(Enemy e)
        {
            Boss = null;
            BossesPassed++;
            Stats.BossesKilled++;
            Stats.Log.Add(e.BossDef.Name + " killed turn " + Turn + " t=" + (int)Time);
            foreach (var o in Enemies) if (o.Parent == e && !o.Dead) { o.Dead = true; Burst(o.Cx, o.Cy, o.Color, 8); }
            Shake = 1;
            if (e.BossDef.Final)
            {
                Won = true;
                Mode = Mode.Clearing; ModeT = 0; PopT = 0;
                Hazards.Clear(); Shots.Clear();
                ShowBanner("VICTORY", 0xffe14d, e.BossDef.Name + " sinks into the dark.");
            }
            else
            {
                Hazards.RemoveAll(h => h.Src == "boss");
                // demo design (not in the spec): a breather after each mid boss
                int before = P.Hp;
                P.Hp = System.Math.Min(P.MaxHp, P.Hp + P.MaxHp / 2);
                if (P.Hp > before) Text(P.X, P.Y - 0.8f, "+" + (P.Hp - before), 0x6dff8e, 1.3f);
                ShowBanner(e.BossDef.Name.ToUpperInvariant() + " FALLS", 0xffe14d, "Deeper still... (+50% HP)");
                NextWaveIn = M.Min(NextWaveIn, L.TurnLen);
            }
        }
    }
}
