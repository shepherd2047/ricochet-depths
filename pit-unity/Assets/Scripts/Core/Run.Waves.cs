// Turn loop: wave building and placement, DDM, clear bonus, boss schedule and board expansion.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        public static int HpBase(int turn, int n)
        {
            int B = n == 0 ? 0 : n == 1 ? 2 : n == 2 ? 5 : n * (n + 1);
            return 65 + M.Round(2.45f * turn) + 50 * B;
        }

        List<string> EnemyPool(int turn)
        {
            var o = new List<string>();
            foreach (var kv in Content.Enemies) if (kv.Value.From <= turn) o.Add(kv.Key);
            return o;
        }

        public Enemy MakeEnemy(string type, float x, float y, int turn, int waveId)
        {
            var d = Content.Enemies[type];
            float hp = System.Math.Max(1, M.Round(HpBase(turn, BossesPassed) * d.Hp * Ddm));
            var e = new Enemy
            {
                Id = Uid(), Type = type, Def = d, X = x, Y = y, W = d.W * CELL, H = d.H * CELL,
                Hp = hp, MaxHp = hp, Turn = turn, WaveId = waveId
            };
            float tl = L.TurnLen;
            if (d.ShootEvery != null) e.ShootT = R.Range(d.ShootEvery[0], d.ShootEvery[1]) * tl;
            if (d.HealEvery != null) e.AbilT = R.Range(d.HealEvery[0], d.HealEvery[1]) * tl;
            if (d.LobEvery != null) e.AbilT = R.Range(d.LobEvery[0], d.LobEvery[1]) * tl;
            if (d.LaserEvery != null) e.LaserT = R.Range(d.LaserEvery[0], d.LaserEvery[1]) * tl;
            if (d.HatchEvery != null) e.HatchT = R.Range(d.HatchEvery[0], d.HatchEvery[1]) * tl;
            if (d.FlyGround != null) e.StyleT = R.Range(d.FlyGround[0], d.FlyGround[1]) * tl;
            if (d.BurrowUp != null) e.StyleT = R.Range(d.BurrowUp[0], d.BurrowUp[1]) * tl;
            if (d.StepEvery != null) { e.StepT = R.Range(d.StepEvery[0], d.StepEvery[1]) * tl; e.StepDir = R.Chance(0.5f) ? 1 : -1; }
            if (d.ShieldFront) e.ShieldF = true;
            if (d.ShieldSide) { if (R.Chance(0.5f)) e.ShieldL = true; else e.ShieldR = true; }
            return e;
        }

        struct Slot { public string Type; public int Cx, Cy; public List<string> Riders; }

        // Wave layout in half-cell units relative to the wave's top-left (spec 1.3: random column, shift right to fit).
        List<Slot> LayoutWave(int turn, int h, float densityK)
        {
            var rng = new Rng(Seed + 10000L * turn);
            int cols2 = Cols * 2;
            var grid = new bool[h * 2, cols2];
            var outp = new List<Slot>();
            var pool = EnemyPool(turn);
            float density = (0.17f + 0.18f * M.Min(1, turn / 100f)) * densityK;   // demo tuning, not from the spec
            for (int r = 0; r < h; r++)
            {
                int n = System.Math.Max(1, M.Round(Cols * density * rng.Range(0.7f, 1.25f)));
                for (int k = 0; k < n; k++)
                {
                    string type = rng.Weighted(pool, t => Content.Enemies[t].Weight);
                    var d = Content.Enemies[type];
                    int w2 = M.Round(d.W * 2), h2 = M.Round(d.H * 2);
                    if (h2 > (h - r) * 2) continue;      // tall foes must fit inside the wave
                    int c2 = rng.Int(0, Cols - 1) * 2;
                    if (d.W < 1 && rng.Chance(0.5f)) c2 += 1;
                    int r2 = r * 2 + (d.H < 1 && rng.Chance(0.5f) ? 1 : 0);
                    for (; c2 + w2 <= cols2; c2++)
                    {
                        bool ok = true;
                        for (int yy = r2; yy < r2 + h2 && ok; yy++)
                            for (int xx = c2; xx < c2 + w2; xx++) if (grid[yy, xx]) { ok = false; break; }
                        if (!ok) continue;
                        for (int yy = r2; yy < r2 + h2; yy++) for (int xx = c2; xx < c2 + w2; xx++) grid[yy, xx] = true;
                        var slot = new Slot { Type = type, Cx = c2, Cy = r2 };
                        // stacking: from turn 20 a stackable foe may carry riders
                        if (d.Stackable && turn >= 20 && rng.Chance(0.12f + 0.08f * M.Min(1, (turn - 20) / 100f)))
                        {
                            slot.Riders = new List<string>();
                            int nr = rng.Chance(0.3f) ? 2 : 1;
                            for (int q = 0; q < nr; q++) slot.Riders.Add(rng.Pick(new[] { "grunt", "archer", "grunt", turn >= 35 ? "chiller" : "mite" }));
                        }
                        outp.Add(slot);
                        break;
                    }
                }
            }
            return outp;
        }

        int WaveHeight(int turn)
        {
            var rng = new Rng(Seed + 10000L * turn + 77);
            int maxH = System.Math.Min(6, 1 + M.Ceil(turn / 12f));
            int h = rng.Int(1, maxH);
            if (BossesPassed < L.BossTurns.Length && Boss == null && turn < L.BossTurns[BossesPassed])
                h = System.Math.Min(h, L.BossTurns[BossesPassed] - turn);
            return System.Math.Max(1, h);
        }

        Wave RegisterWave(int turn, int h)
        {
            var w = new Wave { Id = ++waveSeq, Turn = turn, H = h };
            foreach (int ft in L.FurnaceTurns) if (ft >= turn && ft < turn + h) w.Furnace = true;
            waves[w.Id] = w;
            return w;
        }

        void PlaceWave(Wave wave, float topY, float entry, float densityK)
        {
            var lay = LayoutWave(wave.Turn, wave.H, densityK);
            for (int i = 0; i < lay.Count; i++)
            {
                var p = lay[i];
                var e = MakeEnemy(p.Type, p.Cx * CELL / 2, topY + p.Cy * CELL / 2, wave.Turn, wave.Id);
                if (entry > 0) e.Entry = entry + (i / (float)System.Math.Max(1, lay.Count)) * 0.5f;
                Enemies.Add(e);
                wave.Alive++;
                if (p.Riders == null) continue;
                var mount = e;
                foreach (var rt in p.Riders)
                {
                    var r = MakeEnemy(rt, e.X, e.Y, wave.Turn, wave.Id);
                    r.Mount = mount;
                    r.Entry = e.Entry;
                    Enemies.Add(r);
                    wave.Alive++;
                    mount = r;
                }
            }
        }

        float TopmostY()
        {
            float m = 0;
            foreach (var e in Enemies) if (!e.Boss && !e.Pod && !e.Dead && e.Mount == null) m = M.Min(m, e.Y);
            return m;
        }

        bool BossDue()
        {
            if (Boss != null || Expand != null || FinalStarted) return false;
            if (BossesPassed < L.BossTurns.Length) return Turn >= L.BossTurns[BossesPassed];
            return Time + Skipped >= L.TargetSeconds - 2 * L.TurnLen;
        }

        void SpawnTick()
        {
            UpdateDdm();
            if (BossDue()) { StartBoss(); return; }
            if (Won) return;
            int h = WaveHeight(Turn);
            var wave = RegisterWave(Turn, h);
            float lift = Rows >= 15 && Boss == null ? 3 * CELL : 0;
            PlaceWave(wave, TopmostY() - h * CELL - lift, 0, Boss != null ? 0.6f : 1f);
            Turn += h;
            NextWaveIn = h * L.TurnLen;
        }

        // Clear bonus: pre-spawn about half a board of waves straight into the top half (spec 2.3).
        void PreSpawn()
        {
            var list = new List<Wave>();
            int rows = 0, cap = Rows / 2;
            while (rows < cap)
            {
                if (BossesPassed < L.BossTurns.Length && Turn >= L.BossTurns[BossesPassed]) break;
                int h = System.Math.Min(WaveHeight(Turn), cap - rows + 1);
                list.Add(RegisterWave(Turn, h));
                Turn += h; rows += h;
                Skipped += h * L.TurnLen;
            }
            float y = rows * CELL;
            foreach (var w in list) { y -= w.H * CELL; PlaceWave(w, y, 0.6f, 1); }
            NextWaveIn = L.TurnLen;
        }

        void CheckClearBonus()
        {
            if (Boss != null || Time < 4 || Won) return;
            foreach (var e in Enemies) if (!e.Dead && (e.Bottom > 0 || e.Entry > 0.5f)) return;
            int gold = M.Round((10 + 5 * L.L + L.D * (L.L + 1)) * (1 + 0.1f * (L.D - 1)));
            Gold += gold; Stats.Clears++;
            Ddm += 0.5f;
            ShowBanner("BOARD CLEAR", 0x6dff8e, "+" + gold + " gold · the pit stirs");
            PreSpawn();
        }

        void UpdateDdm()
        {
            float lowest = -99; bool any = false;
            foreach (var e in Enemies)
            {
                if (e.Boss || e.Pod || e.Dead || e.Bottom <= 0) continue;
                any = true; lowest = M.Max(lowest, e.Bottom);
            }
            bool allTop = !any || lowest <= 3 * CELL;
            float d = Ddm;
            if (Boss != null) d = allTop ? (d < 2 ? M.Min(2, d + 0.125f) : d) : d - 1;
            else if (any && lowest >= Bh * 0.75f) d -= 1;
            else if (any && lowest >= Bh * 0.5f) d -= 0.5f;
            else if (allTop && d < 2) d = M.Min(2, d + 0.125f);
            Ddm = M.Max(1, d);
        }

        void WaveGone(Enemy e)
        {
            Wave w;
            if (e.WaveId == 0 || !waves.TryGetValue(e.WaveId, out w)) return;
            w.Alive--;
            if (w.Alive <= 0 && w.Furnace && !w.Dropped)
            {
                w.Dropped = true;
                Drops.Add(new Drop { Kind = DropKind.Fuser, X = M.Clamp(e.Cx, 0.5f, Bw - 0.5f), Y = M.Clamp(e.Cy, 0.5f, Bh - 0.5f), V = 1 });
                Text(e.Cx, e.Y, "FUSER!", 0xffb347, 1.4f);
            }
        }

        // ------------------------------------------------------------ bosses: expansion then spawn
        public string NextBossId { get { return L.Bosses[System.Math.Min(BossesPassed, L.Bosses.Length - 1)]; } }

        void StartBoss()
        {
            string id = NextBossId;
            if (Content.Bosses[id].Final) FinalStarted = true;
            // spec 1.3: cols = 7 + 2(n+1), rows = 9 + 2(n+1)
            Expand = new Expansion { FromCols = Cols, FromRows = Rows, ToCols = L.Cols + 2 * (BossesPassed + 1), ToRows = L.Rows + 2 * (BossesPassed + 1), BossId = id };
            Shake = 0.6f;
            ShowBanner("THE GROUND GIVES WAY", 0xffb347);
        }

        void StepExpand(float dt)
        {
            var ex = Expand;
            float k = M.Min(dt, ex.Dur - ex.T) / ex.Dur;
            ex.T += dt;
            float dx = CELL * (ex.ToCols - ex.FromCols) / 2f * k, dy = CELL * (ex.ToRows - ex.FromRows) * k;
            Bw += dx * 2; Bh += dy;
            foreach (var o in Enemies) { o.X += dx; o.Y += dy; }
            foreach (var o in Field) { o.X += dx; o.Y += dy; }
            foreach (var o in Drops) { o.X += dx; o.Y += dy; }
            foreach (var o in Shots) { o.X += dx; o.Y += dy; }
            foreach (var o in Hazards) { o.X += dx; o.Y += dy; if (!float.IsNaN(o.FromX)) { o.FromX += dx; o.FromY += dy; } }
            P.X += dx; P.Y += dy;
            FloorShift += dy;
            Shake = M.Max(Shake, 0.25f);
            if (ex.T >= ex.Dur)
            {
                Cols = ex.ToCols; Rows = ex.ToRows; Bw = Cols * CELL; Bh = Rows * CELL;
                Expand = null;
                SpawnBoss(ex.BossId);
                NextWaveIn = L.TurnLen * 2;
            }
        }

        void SpawnBoss(string id)
        {
            var B = Content.Bosses[id];
            float w = B.W * CELL, h = B.H * CELL;
            float hpMult = B.HpMult * (B.Final ? 1 + 0.25f * (L.D - 1) : 1);
            float hp = M.Round(HpBase(Turn, BossesPassed) * hpMult * Ddm);
            var e = new Enemy
            {
                Id = Uid(), Type = id, BossDef = B, Boss = true, Def = BossEnemyDef(B),
                X = (Bw - w) / 2, Y = -h, W = w, H = h, Hp = hp, MaxHp = hp, Turn = Turn
            };
            e.XpTotal = e.XpLeft = GemValue(e) + 400 * (BossesPassed + 1);
            e.Brain = BossBrain.Create(B.Kind, this, e);
            Enemies.Add(e);
            Boss = e;
            ShowBanner(B.Name.ToUpperInvariant(), 0xff6b6b, B.Blurb);
        }

        static EnemyDef BossEnemyDef(BossDef b)
        {
            return new EnemyDef { Id = b.Id, Name = b.Name, W = b.W, H = b.H, Hp = b.HpMult, Spd = 0, Melee = 0, Color = b.Color };
        }

        public int MakeId() { return Uid(); }
    }
}
