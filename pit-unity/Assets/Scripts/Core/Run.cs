// One run of the game: state, setup, derived player numbers and the fixed-step loop.
// World units: origin at the board's top-left, y grows downward, 1 cell = 1.125 units.
using System.Collections.Generic;

namespace Pit.Core
{
    public sealed partial class Run
    {
        public const float CELL = 1.125f;
        public const int Slots = 4;
        public const int SideBallCap = 1000;

        public readonly int Seed;
        public readonly HeroDef Hero;
        public readonly LevelDef L = Content.Level;
        public readonly Rng R;

        public Mode Mode = Mode.Intro;
        public Mode PrevMode;
        public float IntroT = 1.3f, ModeT, PopT;
        public float GameSpeed = 1;

        public int Cols, Rows;
        public float Bw, Bh;
        public float Time, Skipped, NextWaveIn;
        public int Turn = 1;
        public float Ddm = 1;
        public int BossesPassed;
        public bool FinalStarted;
        // spec §1.3a treadmill corridor: Scroll counts cells the floor has moved toward the player (1 per turn length at
        // ScrollSpeed 1), FloorShift is the same in board units plus the board expansions' shifts, so the view can
        // keep the corridor under the units. FarY is the invisible far wall (spec §4.4) the balls bounce on: the far
        // edge of the board's top row, rows × CELL from the near edge, so board y 0; it moves back with an expansion.
        // New waves spawn above it and only come into play once the floor scrolls them over it.
        public float Scroll, ScrollSpeed = 1, FloorShift;
        public const float FarY = 0;
        public Expansion Expand;
        public Enemy Boss;

        public readonly Player P = new Player();
        public readonly List<OwnedBall> Balls = new List<OwnedBall>();
        public readonly List<OwnedItem> Items = new List<OwnedItem>();
        public List<FieldBall> Field = new List<FieldBall>();
        public Volley Volley;
        public int BabiesOut;
        public readonly List<float> BabyCharge = new List<float>();

        public List<Enemy> Enemies = new List<Enemy>();
        readonly List<Enemy> deferred = new List<Enemy>();
        public List<Shot> Shots = new List<Shot>();
        public List<Hazard> Hazards = new List<Hazard>();
        public List<Drop> Drops = new List<Drop>();
        public List<Particle> Fx = new List<Particle>();
        public List<FloatText> Texts = new List<FloatText>();
        /// <summary>Sound cues raised since the view last cleared the list.</summary>
        public readonly List<Sfx> Sounds = new List<Sfx>();
        public List<Beam> Beams = new List<Beam>();
        /// <summary>Direct ball hits on foes since the view last cleared the list (hit VFX); view-only, capped.</summary>
        public readonly List<BallHit> BallHits = new List<BallHit>();

        readonly Dictionary<int, Wave> waves = new Dictionary<int, Wave>();
        int waveSeq, nextId = 1;

        public int Gold, Kills, Rerolls, PointsUsed, LastFreeCount, PendingFuser;
        public bool AutoMenu = true;
        // ads.md: the host turns rewarded ads on (bot runs leave them off, so balance sweeps compare with earlier ones).
        // With them on, death first opens the revive prompt (Mode.Revive, sim frozen) while the revive is unused.
        public bool AdsOn, ReviveUsed, AdWatched;
        public int AdRerolls;
        public float Shake;
        public Menu Menu;
        public Banner Banner;
        public readonly RunStats Stats = new RunStats();
        public readonly HashSet<string> BannedCombos = new HashSet<string>();

        /// <summary>Movement input, -1..1 per axis, set by the host each frame.</summary>
        public float InputX, InputY;
        /// <summary>How far down (board y) the hero may walk: the view sets it to the bottom edge of what the camera
        /// shows above the HUD. NaN (no view, the sims) keeps the old limit, half a cell above the board's near edge.</summary>
        public float HeroMaxY = float.NaN;
        float HeroYMax { get { return float.IsNaN(HeroMaxY) ? Bh - 0.5f : M.Max(Bh - 0.5f, HeroMaxY); } }

        // ------------------------------------------------------------ derived player numbers (recomputed by Derive)
        public float ItemPower, Aoe, StatusK, Str, BabyMin, BabyMax, V0, Fire, Accel, Cooldown, Crit, Move, Defense, CatchW, Pickup, BurnBoost;
        public int Babies;

        public Twist Tw { get { return Hero.Twist; } }

        public Run(int seed, string heroId)
        {
            Seed = seed;
            Hero = Content.Hero(heroId);
            R = new Rng((uint)(seed ^ 0x5bd1e995));
            Cols = L.Cols; Rows = L.Rows; Bw = Cols * CELL; Bh = Rows * CELL;
            P.X = Bw / 2; P.Y = Bh + 1.5f;
            P.Stats = (int[])Hero.Stats.Clone();
            AddBall(Hero.StartBall);
            Derive();
            P.Hp = P.MaxHp;
        }

        int Uid() { return nextId++; }
        public float S(Stat s) { return P.Stats[(int)s]; }

        public int ItemLv(string id)
        {
            for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return Items[i].Level;
            return 0;
        }
        public float ItemVal(string id)
        {
            int lv = ItemLv(id);
            if (lv == 0) return 0;
            var d = Content.Items[id];
            return M.Round(d.Vals[lv - 1] * (d.NoPower ? 1 : ItemPower));
        }
        public int ItemValFor(string id, int level)
        {
            var d = Content.Items[id];
            return M.Round(d.Vals[level - 1] * (d.NoPower ? 1 : ItemPower));
        }

        public void Derive()
        {
            const float m = 1f; // grade multiplier: no meta progression in this demo
            ItemPower = 0.82f + 0.05f * S(Stat.Int) * m;
            Aoe = (0.75f + 0.035f * S(Stat.Int) * m) * (1 + ItemVal("lens") / 100f);
            StatusK = 0.7f + 0.05f * S(Stat.Int) * m;
            Str = 1 + 0.055f * S(Stat.Str) * m;
            Babies = M.Floor(M.W(M.Curve.Lead, S(Stat.Lead)) * m);
            BabyMin = 10 + S(Stat.Lead) / 5f; BabyMax = 16 + S(Stat.Lead) / 5f;
            V0 = 4.5f + 0.4f * M.W(M.Curve.BallSpeed, S(Stat.Spd)) * m;
            float wf = M.W(M.Curve.Fire, S(Stat.Dex)) * m;
            Fire = (1.8f + 0.4f * wf) * (1 + ItemVal("trigger") / 100f) * (Tw == Twist.Scatter ? 2 : 1);
            Accel = 0.05f * wf;
            Cooldown = M.Max(0.01f, 1.5f - 0.035f * wf) * 3;
            Crit = 0.0008f * M.W(M.Curve.Crit, S(Stat.Dex)) * m;
            Move = (2.5f + 0.13f * M.W(M.Curve.Move, S(Stat.Spd)) * m) * (1 + (ItemVal("boots") + ItemVal("mantle")) / 100f);
            Defense = 0.95f + 0.016f * S(Stat.End) * m;
            CatchW = 1 + (ItemVal("mitt") + ItemVal("halo")) / 100f;
            Pickup = 1.3f * (1 + ItemVal("lodestone") / 100f);
            BurnBoost = 1 + ItemVal("kindling") / 100f;
            int old = P.MaxHp;
            P.MaxHp = M.Round(10 * S(Stat.End) * m) + (int)ItemVal("locket");
            if (P.MaxHp > old && old > 1) P.Hp += P.MaxHp - old;
            if (P.Hp > P.MaxHp) P.Hp = P.MaxHp;
        }

        // ------------------------------------------------------------ owned balls and items
        public static Stats BallStats(string id, int level)
        {
            var d = Content.Balls[id];
            var st = new Stats(d.Base);
            if (!st.ContainsKey(K.Mult)) st[K.Mult] = 100;
            for (int i = 0; i < level - 1; i++)
                foreach (var kv in d.Tiers[i]) st[kv.Key] = st.G(kv.Key) + kv.Value;
            return st;
        }

        public OwnedBall AddBall(string id, int slot = -1)
        {
            var b = new OwnedBall { Uid = Uid(), Id = id, Stats = BallStats(id, 1), State = BallState.Ready };
            if (slot >= 0 && slot <= Balls.Count) Balls.Insert(slot, b); else Balls.Add(b);
            return b;
        }
        public void LevelBall(OwnedBall b, int n = 1)
        {
            b.Level = System.Math.Min(3, b.Level + n);
            b.Stats = BallStats(b.Id, b.Level);
        }
        public void AddItem(string id, int slot = -1)
        {
            var it = new OwnedItem { Id = id };
            if (slot >= 0 && slot <= Items.Count) Items.Insert(slot, it); else Items.Add(it);
            Derive();
        }
        public void LevelItem(OwnedItem it, int n = 1) { it.Level = System.Math.Min(3, it.Level + n); Derive(); }

        // ------------------------------------------------------------ fx helpers
        public void Sound(Sfx s) { if (Sounds.Count < 64) Sounds.Add(s); }
        void RaiseHit(FieldBall b, Enemy e)
        {
            if (b.Inst != null && !b.Baby && BallHits.Count < 64)
                BallHits.Add(new BallHit { Ball = b.Inst.Id, Enemy = e.Id, X = b.X, Y = b.Y, Ex = e.Cx, Ey = e.Cy });
        }
        public void Text(float x, float y, string s, uint color = 0xffffff, float size = 1)
        {
            if (Texts.Count > 80) Texts.RemoveAt(0);
            Texts.Add(new FloatText { X = x, Y = y, S = s, Color = color, Size = size, T = 0.7f });
        }
        public void Burst(float x, float y, uint color, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (Fx.Count > 400) Fx.RemoveAt(0);
                float a = R.Next() * M.PI * 2, v = 1 + R.Next() * 3;
                Fx.Add(new Particle { X = x, Y = y, Vx = M.Cos(a) * v, Vy = M.Sin(a) * v, T = 0.4f + R.Next() * 0.3f, Color = color });
            }
        }
        public void ShowBanner(string title, uint color, string sub = "")
        {
            Banner = new Banner { Title = title, Color = color, Sub = sub, T = 2.6f };
        }
        void AddBeam(Beam b) { if (Beams.Count < 200) Beams.Add(b); }

        void StepFx(float dt)
        {
            foreach (var f in Fx) { f.T -= dt; f.X += f.Vx * dt; f.Y += f.Vy * dt; f.Vy += 6 * dt; }
            Fx.RemoveAll(f => f.T <= 0);
            foreach (var t in Texts) { t.T -= dt; t.Y -= 0.8f * dt; }
            Texts.RemoveAll(t => t.T <= 0);
            foreach (var b in Beams) b.T -= dt;
            Beams.RemoveAll(b => b.T <= 0);
            foreach (var b in Field)
            {
                b.Trail.Add(b.X); b.Trail.Add(b.Y);
                if (b.Trail.Count > 10) b.Trail.RemoveRange(0, 2);
            }
            Shake = M.Max(0, Shake - dt);
            if (Banner != null) { Banner.T -= dt; if (Banner.T <= 0) Banner = null; }
        }

        // ------------------------------------------------------------ corridor scroll (spec §1.3a)
        void StepScroll(float dt)
        {
            if (Boss != null && Boss.BossDef.Final) ScrollSpeed = 0;   // the corridor ends: the final boss walks out of it
            else if (Mode == Mode.Dying || Mode == Mode.Clearing) ScrollSpeed = M.Max(0, ScrollSpeed - dt);
            float d = ScrollSpeed * dt / L.TurnLen;
            if (d <= 0) return;
            Scroll += d;
            FloorShift += d * CELL;
            // pickups lie on the floor and ride it; ones that slide 2 units past the bottom are gone
            foreach (var o in Drops) if (!o.Flying) o.Y += d * CELL;
            Drops.RemoveAll(o => !o.Flying && o.Y > Bh + 2);
        }

        // ------------------------------------------------------------ main step
        public bool IsMenu { get { return Mode == Mode.LevelUp || Mode == Mode.Fuser || Mode == Mode.Paused; } }
        public bool IsOver { get { return Mode == Mode.GameOver || Mode == Mode.Victory; } }

        public void Step(float dt)
        {
            var m = Mode;
            if (m != Mode.Intro && m != Mode.Playing && m != Mode.Clearing && m != Mode.Dying) return;
            Time += dt;
            if (m == Mode.Intro)
            {
                IntroT -= dt;
                StepScroll(dt);
                StepPlayer(dt);
                if (IntroT <= 0) { Mode = Mode.Playing; SpawnTick(); }
                StepFx(dt);
                return;
            }
            if (m == Mode.Playing)
            {
                if (Expand != null) StepExpand(dt);
                else
                {
                    NextWaveIn -= dt;
                    if (NextWaveIn <= 0) SpawnTick();
                    CheckClearBonus();
                }
            }
            if (m == Mode.Clearing)
            {
                ModeT += dt;
                PopT += dt * 30;
                while (PopT >= 1)
                {
                    PopT -= 1;
                    var e = Enemies.Find(o => !o.Dead);
                    if (e != null) { e.Dead = true; Burst(e.Cx, e.Cy, e.Color, 6); DropGem(e.Cx, e.Cy, GemValue(e)); }
                }
                if (ModeT > 3 && Drops.Count == 0) Mode = Mode.Victory;
            }
            if (m == Mode.Dying)
            {
                ModeT -= dt;
                if (ModeT <= 0) Mode = Mode.GameOver;
            }
            StepPlayer(dt);
            if (m == Mode.Playing) StepVolley(dt);
            for (int i = 0; i < Field.Count; i++) if (!Field[i].Gone) StepBall(Field[i], dt);
            Field.RemoveAll(b => b.Gone);
            CheckAllSwallowed();
            foreach (var b in Balls)
                if (b.State == BallState.Cool) { b.Cd -= dt; if (b.Cd <= 0) b.State = BallState.Ready; }
            for (int c = BabyCharge.Count - 1; c >= 0; c--) { BabyCharge[c] -= dt; if (BabyCharge[c] <= 0) BabyCharge.RemoveAt(c); }
            if (m == Mode.Playing || m == Mode.Dying) { StepEnemies(dt); StepShots(dt); }
            if (deferred.Count > 0) { Enemies.AddRange(deferred); deferred.Clear(); }
            Enemies.RemoveAll(e => e.Dead);
            StepScroll(dt);
            StepDrops(dt);
            StepFx(dt);
            if (Mode == Mode.Playing && Expand == null) CheckMenus();
        }

        public void TapWorld(float x, float y)
        {
            var e = Enemies.Find(o => !o.Dead && x >= o.X - 0.2f && x <= o.X + o.W + 0.2f && y >= o.Y - 0.2f && y <= o.Y + o.H + 0.2f);
            P.Target = e != null ? e.Id : -1;
        }

        public void Pause() { if (Mode == Mode.Playing || Mode == Mode.Intro) { PrevMode = Mode; Mode = Mode.Paused; } }
        public void Resume() { if (Mode == Mode.Paused) Mode = PrevMode; }

        /// <summary>Seconds until the final boss arrives by the level's time rule (spec 1.4).</summary>
        public float TimeToFinal { get { return M.Max(0, L.TargetSeconds - 2 * L.TurnLen - (Time + Skipped)); } }
    }
}
