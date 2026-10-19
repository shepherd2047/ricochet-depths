// Original bosses. Each boss is an Enemy driven by a BossBrain: descend, then cycle phases between idle
// gaps without repeating a phase twice in a row; enraged below half health.
using System.Collections.Generic;

namespace Pit.Core
{
    public abstract class BossBrain
    {
        protected readonly Run R;
        protected readonly Enemy B;
        public string Phase;
        string lastPhase;
        protected float phaseT = 2.5f, pt;
        protected int pc;
        public bool Descending = true;
        protected float landY = 1.4f, sway, homeX, swayAmp = 0.8f, swaySpeed = 0.6f;

        protected BossBrain(Run r, Enemy e) { R = r; B = e; homeX = e.X; }

        public static BossBrain Create(BossKind k, Run r, Enemy e)
        {
            switch (k)
            {
                case BossKind.Hivekeeper: return new Hivekeeper(r, e);
                case BossKind.Mawking: return new Mawking(r, e);
                default: return new Bellwarden(r, e);
            }
        }

        public bool Enraged { get { return B.Hp < B.MaxHp / 2; } }
        public virtual bool Sealed(Enemy e) { return Descending; }
        public virtual bool Blocks(Enemy e, float nx, float ny) { return false; }
        public virtual bool CrownHit(Enemy e, float nx, float ny) { return false; }
        public virtual void OnDamaged(Enemy e, int dmg) { }
        public virtual void OnPartDead(Enemy boss, Enemy part) { }
        protected abstract string[] Phases { get; }
        protected abstract void Tick(string phase);
        protected virtual void Landed() { }
        protected virtual void Always(float dt) { }

        public void Update(Enemy e, float dt)
        {
            if (Descending)
            {
                e.Y += 2 * dt;
                Crush();
                if (e.Y >= landY) { e.Y = landY; Descending = false; R.Shake = 0.5f; Landed(); }
                return;
            }
            sway += dt;
            e.X = M.Clamp(homeX + M.Sin(sway * swaySpeed) * swayAmp, 0, R.Bw - e.W);
            Crush();
            Always(dt);
            if (Phase == null)
            {
                phaseT -= dt;
                if (phaseT > 0) return;
                var opts = new List<string>();
                foreach (var ph in Phases) if (ph != lastPhase) opts.Add(ph);
                Phase = R.R.Pick(opts); lastPhase = Phase; pt = 0; pc = 0;
                return;
            }
            pt -= dt;
            if (pt > 0) return;
            Tick(Phase);
        }

        // bosses flatten any foe they touch
        void Crush()
        {
            foreach (var o in R.Enemies.ToArray())
                if (o != B && !o.Dead && !o.Pod && o.Mount == null && Run.Overlap(B, o)) R.KillEnemy(o, true);
        }

        protected void EndPhase(float idle, float idleEnraged) { Phase = null; phaseT = Enraged ? idleEnraged : idle; }

        protected float Cx { get { return B.Cx; } }
        protected float By { get { return B.Bottom; } }

        protected void Bolt(float x, float y, float a, float speed)
        {
            R.Shots.Add(new Shot { X = x, Y = y, Vx = M.Cos(a) * speed, Vy = M.Sin(a) * speed, R = 0.14f, Dmg = 5, Hp = 1, Kind = ShotKind.Bolt });
        }
        protected void Blast(float x, float y, float r, float warn, float dmg)
        {
            R.Hazards.Add(new Hazard { Kind = HazardKind.Blast, X = M.Clamp(x, 0.4f, R.Bw - 0.4f), Y = M.Clamp(y, By + 0.6f, R.Bh - 0.3f), R = r, T = warn, Max = warn, Dmg = dmg, Src = "boss" });
        }
        protected void Summon(string[] types, float y)
        {
            for (int s = 0; s < types.Length; s++)
            {
                var d = Content.Enemies[types[s]];
                float w = d.W * Run.CELL;
                float span = R.Bw - w;
                float x = M.Clamp(span * (s + 0.5f) / types.Length + R.R.Range(-0.3f, 0.3f), 0, span);
                var m = R.MakeEnemy(types[s], x, y, R.Turn, 0);
                m.Entry = 0.6f + s * 0.08f;
                bool clash = Run.Overlap(m, B);
                foreach (var o in R.Enemies) if (!o.Dead && o.Mount == null && Run.Overlap(m, o)) clash = true;
                if (!clash) R.Enemies.Add(m);
            }
        }
    }

    // ------------------------------------------------------------------ mid boss 1
    // A great bell. Hits on its brass crown (bounced in from above) always crit.
    public sealed class Bellwarden : BossBrain
    {
        static readonly string[] P = { "fan", "toll", "summon", "lances" };
        public Bellwarden(Run r, Enemy e) : base(r, e) { landY = 1.4f; }
        protected override string[] Phases { get { return P; } }
        public override bool CrownHit(Enemy e, float nx, float ny) { return ny < -0.5f; }

        protected override void Tick(string ph)
        {
            bool en = Enraged;
            var p = R.P;
            switch (ph)
            {
                case "fan":
                    {
                        int n = en ? 7 : 5, salvos = en ? 4 : 3;
                        float baseA = M.Atan2(p.Y - By, p.X - Cx);
                        for (int i = 0; i < n; i++) Bolt(Cx, By - 0.2f, baseA + (i - (n - 1) / 2f) * (70f / (n - 1)) * M.Deg, 3.8f);
                        pc++; pt = 0.6f;
                        if (pc >= salvos) EndPhase(2.6f, 1.8f);
                        break;
                    }
                case "toll":
                    {
                        int count = en ? 5 : 3;
                        for (int t = 0; t < count; t++)
                            Blast(p.X + (t == 0 ? 0 : R.R.Range(-2.5f, 2.5f)), p.Y + (t == 0 ? 0 : R.R.Range(-2f, 1f)), 1.2f, 2 + t * 0.15f, 10);
                        EndPhase(2.6f, 1.8f);
                        break;
                    }
                case "summon":
                    Summon(new[] { "grunt", "archer", "brick", "grunt", "archer", "grunt" }, By + 0.15f);
                    EndPhase(2.6f, 1.8f);
                    break;
                case "lances":
                    {
                        float lx = M.Clamp(p.X + (pc == 0 ? 0 : R.R.Range(-1f, 1f)), 0.4f, R.Bw - 0.4f);
                        R.Hazards.Add(new Hazard { Kind = HazardKind.Lance, X = lx, Y = By, W = 0.8f, T = 1.5f, Max = 1.5f, Dmg = 10, Src = "boss" });
                        pc++; pt = en ? 0.55f : 0.8f;
                        if (pc >= (en ? 5 : 3)) EndPhase(2.6f, 1.8f);
                        break;
                    }
            }
        }
    }

    // ------------------------------------------------------------------ mid boss 2
    // A hive core circled by pods. The core is sealed until every pod is popped; then it is bare for a while
    // before it grows new pods.
    public sealed class Hivekeeper : BossBrain
    {
        static readonly string[] P = { "ring", "podshot", "swarm", "spores" };
        static readonly EnemyDef PodDef = new EnemyDef { Id = "pod", Name = "Hive Pod", W = 1, H = 1, Hp = 1.5f, Melee = 0, Color = 0xb8d060 };
        readonly List<Enemy> pods = new List<Enemy>();
        float spin, exposedT;
        const float Radius = 2.7f;

        public Hivekeeper(Run r, Enemy e) : base(r, e) { landY = 1.8f; swayAmp = 1.2f; swaySpeed = 0.45f; }
        protected override string[] Phases { get { return P; } }
        public override bool Sealed(Enemy e) { return Descending || pods.Exists(p => !p.Dead); }
        public bool Exposed { get { return !Descending && !pods.Exists(p => !p.Dead); } }
        public float ExposedT { get { return exposedT; } }

        protected override void Landed() { Grow(6); }

        void Grow(int n)
        {
            pods.RemoveAll(p => p.Dead);
            for (int i = 0; i < n; i++)
            {
                float hp = M.Max(1, M.Round(B.MaxHp * 0.07f));
                var pod = new Enemy
                {
                    Id = R.MakeId(), Type = "pod", Def = PodDef, Pod = true, Parent = B, W = Run.CELL * 0.9f, H = Run.CELL * 0.9f,
                    Hp = hp, MaxHp = hp, Turn = R.Turn, Orbit = i * 2 * M.PI / n, Entry = 0.4f
                };
                Place(pod);
                pods.Add(pod);
                R.Enemies.Add(pod);
            }
            R.Text(Cx, B.Y, "THE HIVE REGROWS", 0xb8d060, 1.1f);
        }

        void Place(Enemy pod)
        {
            pod.X = Cx + M.Cos(pod.Orbit + spin) * Radius - pod.W / 2;
            pod.Y = B.Cy + M.Sin(pod.Orbit + spin) * Radius - pod.H / 2;
        }

        protected override void Always(float dt)
        {
            spin += dt * (Enraged ? 0.9f : 0.6f);
            foreach (var p in pods) if (!p.Dead) Place(p);
            if (Exposed)
            {
                exposedT += dt;
                if (exposedT >= 9) { exposedT = 0; Grow(Enraged ? 5 : 4); }
            }
        }

        public override void OnPartDead(Enemy boss, Enemy part)
        {
            if (!pods.Exists(p => !p.Dead)) { exposedT = 0; R.ShowBanner("THE CORE IS BARE", 0xb8d060); }
        }

        protected override void Tick(string ph)
        {
            bool en = Enraged;
            var p = R.P;
            switch (ph)
            {
                case "ring":
                    {
                        int n = en ? 16 : 12;
                        float off = pc * 0.5f * 2 * M.PI / n;
                        for (int i = 0; i < n; i++) Bolt(Cx, B.Cy, off + i * 2 * M.PI / n, 3.4f);
                        pc++; pt = 0.7f;
                        if (pc >= (en ? 3 : 2)) EndPhase(2.4f, 1.7f);
                        break;
                    }
                case "podshot":
                    foreach (var pod in pods)
                        if (!pod.Dead && pod.Entry <= 0) { float a = M.Atan2(p.Y - pod.Cy, p.X - pod.Cx); Bolt(pod.Cx, pod.Cy, a, 3.6f); }
                    pc++; pt = 0.8f;
                    if (pc >= (en ? 3 : 2)) EndPhase(2.4f, 1.7f);
                    break;
                case "swarm":
                    Summon(new[] { "mite", "mite", "grunt", "mite", "mite", "grunt", "mite" }, B.Bottom + Radius - 0.5f);
                    EndPhase(2.4f, 1.7f);
                    break;
                case "spores":
                    {
                        int count = en ? 6 : 4;
                        for (int t = 0; t < count; t++)
                            Blast(p.X + (t == 0 ? 0 : R.R.Range(-2.8f, 2.8f)), p.Y + (t == 0 ? 0 : R.R.Range(-2.5f, 0.5f)), 1.0f, 2.2f + t * 0.12f, 10);
                        EndPhase(2.4f, 1.7f);
                        break;
                    }
            }
        }
    }

    // ------------------------------------------------------------------ final boss
    // A vast stone maw. Stone plates cover some of its faces and rotate; balls glance off plated faces.
    // Below half health it carries two plates and turns them faster.
    public sealed class Mawking : BossBrain
    {
        static readonly string[] P = { "maw", "spiral", "quake", "summon", "sweep" };
        public readonly bool[] Plates = new bool[3];   // 0 left, 1 front (bottom), 2 right
        public readonly bool[] NextPlates = new bool[3];
        public float PlateT, Warn;
        float spiralA;

        public Mawking(Run r, Enemy e) : base(r, e) { landY = 1.0f; swayAmp = 1.0f; swaySpeed = 0.35f; Plates[1] = true; PlateT = 4; }
        protected override string[] Phases { get { return P; } }

        public override bool Blocks(Enemy e, float nx, float ny)
        {
            if (ny > 0.5f) return Plates[1];
            if (nx < -0.5f) return Plates[0];
            if (nx > 0.5f) return Plates[2];
            return false;
        }

        protected override void Always(float dt)
        {
            if (B.Hp < B.MaxHp / 4) { swayAmp = M.Max(0.2f, (R.Bw - B.W) / 2 - 0.1f); swaySpeed = 0.7f; homeX = (R.Bw - B.W) / 2; }
            PlateT -= dt;
            if (Warn <= 0 && PlateT <= 1)
            {
                // pick the next faces and telegraph them for a second
                int want = Enraged ? 2 : 1;
                for (int i = 0; i < 3; i++) NextPlates[i] = false;
                var faces = R.R.Shuffle(new List<int> { 0, 1, 2 });
                for (int i = 0; i < want; i++) NextPlates[faces[i]] = true;
                Warn = 1;
            }
            if (Warn > 0)
            {
                Warn -= dt;
                if (Warn <= 0)
                {
                    for (int i = 0; i < 3; i++) Plates[i] = NextPlates[i];
                    PlateT = Enraged ? 2.5f : 4;
                    Warn = 0;
                }
            }
        }

        protected override void Tick(string ph)
        {
            bool en = Enraged;
            var p = R.P;
            switch (ph)
            {
                case "maw":
                    {
                        float lx = M.Clamp(p.X, 1, R.Bw - 1);
                        R.Hazards.Add(new Hazard { Kind = HazardKind.Lance, X = lx, Y = By, W = 2.0f, T = 1.5f, Max = 1.5f, Dmg = 15, Src = "boss" });
                        pc++; pt = 1.1f;
                        if (pc >= (en ? 3 : 2)) EndPhase(2.2f, 1.5f);
                        break;
                    }
                case "spiral":
                    {
                        int arms = en ? 3 : 2;
                        for (int i = 0; i < arms; i++) Bolt(Cx, By - 0.4f, spiralA + i * 2 * M.PI / arms, 3.6f);
                        spiralA += 0.33f;
                        pc++; pt = 0.12f;
                        if (pc >= 25) EndPhase(2.2f, 1.5f);
                        break;
                    }
                case "quake":
                    {
                        int count = en ? 7 : 5;
                        for (int t = 0; t < count; t++)
                            Blast(p.X + (t == 0 ? 0 : R.R.Range(-3.5f, 3.5f)), p.Y + (t == 0 ? 0 : R.R.Range(-3f, 1f)), 1.6f, 2 + t * 0.1f, 10);
                        EndPhase(2.2f, 1.5f);
                        break;
                    }
                case "summon":
                    Summon(new[] { "grunt", "archer", "chiller", "hulk", "dasher", "archer", "grunt" }, By + 0.2f);
                    EndPhase(2.2f, 1.5f);
                    break;
                case "sweep":
                    {
                        float y = M.Clamp(p.Y + (pc == 0 ? 0 : R.R.Range(-1.2f, 1.2f)), By + 1, R.Bh - 0.5f);
                        R.Hazards.Add(new Hazard { Kind = HazardKind.Sweep, X = R.Bw / 2, Y = y, W = 1.3f, T = 2f, Max = 2f, Dmg = 10, Src = "boss" });
                        pc++; pt = 0.9f;
                        if (pc >= (en ? 3 : 2)) EndPhase(2.2f, 1.5f);
                        break;
                    }
            }
        }
    }
}
