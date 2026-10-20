// Player: movement, contact damage, shots and their effects, telegraphed hazards, pickups and XP.
namespace Pit.Core
{
    public sealed partial class Run
    {
        void StepPlayer(float dt)
        {
            var p = P;
            p.MoveLock = M.Max(0, p.MoveLock - dt);
            p.ContactCd = M.Max(0, p.ContactCd - dt);
            p.HurtT = M.Max(0, p.HurtT - dt);
            p.RootT = M.Max(0, p.RootT - dt);
            p.SlowT = M.Max(0, p.SlowT - dt);
            float sp = Move * (Volley != null ? 0.7f : (p.VolleySlow > 0 ? M.Lerp(1, 0.7f, p.VolleySlow / 0.5f) : 1));
            if (p.SlowT > 0) sp *= 1 - p.SlowPct;
            p.VolleySlow = M.Max(0, p.VolleySlow - dt);
            if (Mode == Mode.Intro)
            {
                float ty = Bh - 0.6f;
                p.Y = M.Max(ty, p.Y - dt * (1.5f / 1.3f) * 1.2f);
                return;
            }
            // damage over time from venom and fire shots
            if ((p.PoisonT > 0 || p.BurnT > 0) && Mode == Mode.Playing)
            {
                p.DotAcc += dt;
                if (p.DotAcc >= 1)
                {
                    p.DotAcc -= 1;
                    if (p.PoisonT > 0) HurtPlayer(1, "venom");
                    if (p.BurnT > 0) HurtPlayer(2, "fire");
                }
                p.PoisonT = M.Max(0, p.PoisonT - dt);
                p.BurnT = M.Max(0, p.BurnT - dt);
            }
            if (p.MoveLock <= 0 && p.RootT <= 0 && Mode == Mode.Playing)
            {
                float mx = InputX, my = InputY, ml = M.Sqrt(mx * mx + my * my);
                if (ml > 1) { mx /= ml; my /= ml; }
                p.X += mx * sp * dt; p.Y += my * sp * dt;
            }
            p.X = M.Clamp(p.X, p.R, Bw - p.R);
            p.Y = M.Clamp(p.Y, p.R + 0.2f, HeroYMax);
            foreach (var e in Enemies)
            {
                if (e.Dead || e.Entry > 0 || e.Flying || e.Under || e.Mount != null) continue;
                float cx = M.Clamp(p.X, e.X, e.X + e.W), cy = M.Clamp(p.Y, e.Y, e.Bottom);
                float dx = p.X - cx, dy = p.Y - cy, d2 = dx * dx + dy * dy;
                if (d2 >= p.R * p.R) continue;
                float d = M.Sqrt(d2); if (d < 1e-3f) d = 1e-3f;
                float nx = d2 > 1e-9f ? dx / d : 0, ny = d2 > 1e-9f ? dy / d : 1;
                p.X += nx * (p.R - d); p.Y += ny * (p.R - d);
                if (p.ContactCd <= 0 && !Invulnerable(e) && !Has(e, "charm"))
                {
                    p.ContactCd = 1; p.MoveLock = 0.7f;
                    float mult = e.Boss ? (e.BossDef.Final ? 4 : 2) : e.Def.Elite ? 2 : 1;
                    HurtPlayer(5 * mult, "contact");
                }
            }
            p.X = M.Clamp(p.X, p.R, Bw - p.R);
            p.Y = M.Clamp(p.Y, p.R + 0.2f, HeroYMax);
        }

        void StepShots(float dt)
        {
            var p = P;
            foreach (var s in Shots)
            {
                if (s.Dead) continue;
                s.X += s.Vx * dt; s.Y += s.Vy * dt;
                float dx = s.X - p.X, dy = s.Y - p.Y, rr = s.R + p.R * 0.8f;
                if (dx * dx + dy * dy < rr * rr) { s.Dead = true; ShotHit(s); }
                if (s.X < -1 || s.X > Bw + 1 || s.Y < -1 || s.Y > Bh + 1) s.Dead = true;
            }
            Shots.RemoveAll(s => s.Dead);
            foreach (var h in Hazards)
            {
                h.T -= dt;
                if (h.T > 0 || h.Done) continue;
                h.Done = true;
                switch (h.Kind)
                {
                    case HazardKind.Blast:
                        {
                            float dx = p.X - h.X, dy = p.Y - h.Y, rr = h.R + p.R * 0.5f;
                            if (dx * dx + dy * dy < rr * rr) HurtPlayer(h.Dmg, h.Src);
                            AddBeam(new Beam { Kind = BeamKind.Ring, X = h.X, Y = h.Y, R = h.R, T = 0.3f, Color = 0xff7043 });
                            Burst(h.X, h.Y, 0xff7043, h.Small ? 5 : 10);
                            break;
                        }
                    case HazardKind.Lance:
                        if (M.Abs(p.X - h.X) < h.W / 2 + p.R * 0.6f && p.Y > h.Y) HurtPlayer(h.Dmg, h.Src);
                        AddBeam(new Beam { Kind = BeamKind.Lance, X = h.X, Y = h.Y, W = h.W, T = 0.3f, Color = 0xff4d6d });
                        break;
                    case HazardKind.Sweep:
                        if (M.Abs(p.Y - h.Y) < h.W / 2 + p.R * 0.6f) HurtPlayer(h.Dmg, h.Src);
                        AddBeam(new Beam { Kind = BeamKind.Sweep, Y = h.Y, W = h.W, T = 0.3f, Color = 0xff4d6d });
                        break;
                    case HazardKind.Beam:
                        {
                            // distance from the player to the ray (origin, direction)
                            float vx = p.X - h.X, vy = p.Y - h.Y, along = vx * h.Dx + vy * h.Dy;
                            float perp = M.Abs(vx * h.Dy - vy * h.Dx);
                            if (along > 0 && perp < h.W / 2 + p.R * 0.6f) HurtPlayer(h.Dmg, h.Src);
                            AddBeam(new Beam { Kind = BeamKind.Laser, X1 = h.X, Y1 = h.Y, X2 = h.X + h.Dx * 40, Y2 = h.Y + h.Dy * 40, W = h.W, T = 0.3f, Color = 0xff5d8f });
                            break;
                        }
                }
            }
            Hazards.RemoveAll(h => h.Done);
        }

        void ShotHit(Shot s)
        {
            var p = P;
            switch (s.Kind)
            {
                case ShotKind.Icicle: HurtPlayer(s.Dmg, "icicle"); p.RootT = M.Max(p.RootT, 1); Text(p.X, p.Y - 0.9f, "FROZEN", 0x9fe8ff, 0.8f); break;
                case ShotKind.Venom: HurtPlayer(s.Dmg, "venom"); p.PoisonT = M.Max(p.PoisonT, 4); break;
                case ShotKind.Fireball: HurtPlayer(s.Dmg, "fire"); p.BurnT = M.Max(p.BurnT, 3); break;
                case ShotKind.Boulder: HurtPlayer(15, "boulder"); break;
                default: HurtPlayer(s.Dmg, s.Kind == ShotKind.Bolt ? "boss" : "arrow"); break;
            }
        }

        void StepDrops(float dt)
        {
            var p = P;
            bool vacuum = Mode == Mode.Clearing;
            foreach (var d in Drops)
            {
                d.Age += dt;
                float dx = p.X - d.X, dy = p.Y - d.Y, dist = M.Sqrt(dx * dx + dy * dy);
                if (dist < 1e-4f) dist = 1e-4f;
                if (vacuum || d.Flying || dist < Pickup || d.Age > (d.Kind == DropKind.Fuser ? 3 : 6)) d.Flying = true;
                if (d.Flying)
                {
                    float v = 4 + d.Age * 4, k = M.Min(dist, v * dt) / dist;
                    d.X += dx * k; d.Y += dy * k;
                }
                if (dist < 0.45f) Collect(d);
            }
            Drops.RemoveAll(d => d.Got);
        }

        void Collect(Drop d)
        {
            d.Got = true;
            switch (d.Kind)
            {
                case DropKind.Gem: Sound(Sfx.Xp); GainXp(d.V); break;
                case DropKind.Gold: Gold += d.V; Text(P.X, P.Y - 0.8f, "+" + d.V + "g", 0xffd84a, 0.8f); break;
                case DropKind.Heal:
                    if (P.Hp >= P.MaxHp) { Gold += 3; Text(P.X, P.Y - 0.8f, "+3g", 0xffd84a, 0.8f); }
                    else Heal(d.V);
                    break;
                case DropKind.Fuser: PendingFuser++; Stats.Fusers++; break;
            }
        }

        public void GainXp(int v)
        {
            var p = P;
            p.Xp += v;
            while (p.Xp >= M.XpNeed(p.Level))
            {
                p.Xp -= M.XpNeed(p.Level);
                p.Level++;
                Sound(Sfx.LevelUp);
                // +1 stat drawn by the hero's weights (its starting stats + 1)
                var rng = new Rng(Seed + p.Level * 131L);
                var idx = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4, 5 };
                int k = rng.Weighted(idx, s => Hero.Stats[s] + 1);
                p.Stats[k]++;
                Derive();
                p.Points++;
                Text(p.X, p.Y - 1, "LEVEL UP  +1 " + Content.StatNames[k], 0xffe14d, 1.1f);
            }
        }
    }
}
