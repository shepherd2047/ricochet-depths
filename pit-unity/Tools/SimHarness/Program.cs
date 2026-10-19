// Usage: dotnet run -c Release -- [seeds=8] [hero=all|id] [maxMinutes=30]
// Plays full runs at fixed 1/60 steps with the dev bot and auto-picking, and prints how far each run got.
using System;
using System.Collections.Generic;
using System.Linq;
using Pit.Core;

static class Program
{
    static int Main(string[] args)
    {
        int seeds = args.Length > 0 ? int.Parse(args[0]) : 8;
        string heroArg = args.Length > 1 ? args[1] : "all";
        float maxMin = args.Length > 2 ? float.Parse(args[2]) : 30;
        // tuning overrides for balance sweeps, e.g. XPMULT=2
        var xm = Environment.GetEnvironmentVariable("XPMULT");
        if (xm != null) Content.Level.XpMult = float.Parse(xm);
        Console.WriteLine($"xpMult {Content.Level.XpMult}");
        var heroes = heroArg == "all" ? Content.Heroes.Select(h => h.Id).ToList() : new List<string> { heroArg };
        int wins = 0, total = 0, errors = 0;
        var reached = new int[4];
        foreach (var hero in heroes)
        {
            for (int s = 1; s <= seeds; s++)
            {
                total++;
                var r = new Run(s * 7919 + 17, hero);
                string err = null;
                int maxBossSeen = 0;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    const float dt = 1 / 60f;
                    while (!r.IsOver && r.Time < maxMin * 60)
                    {
                        if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) Bot.AutoPick(r);
                        Bot.Drive(r);
                        r.Step(dt);
                        if (r.Boss != null) maxBossSeen = Math.Max(maxBossSeen, r.BossesPassed + 1);
                    }
                }
                catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message + " @ " + ex.StackTrace.Split('\n')[0].Trim(); errors++; }
                bool won = r.Mode == Mode.Victory;
                if (won) wins++;
                reached[Math.Min(3, maxBossSeen)]++;
                string balls = string.Join(",", r.Balls.Select(b => b.Id + b.Level + (b.Fused != null ? "+" + b.Fused : "")));
                string items = string.Join(",", r.Items.Select(i => i.Id + i.Level));
                string hurt = string.Join(" ", r.Stats.Hurt.OrderByDescending(k => k.Value).Take(4).Select(k => k.Key + "=" + k.Value));
                Console.WriteLine($"{hero,-8} seed{s,2} {(won ? "WIN " : r.Mode.ToString().ToUpper().PadRight(4))} turn {r.Turn,3} t={r.Time,5:0}s lvl {r.P.Level,2} bossesKilled {r.BossesPassed} sawBoss {maxBossSeen} ddm {r.Ddm:0.00} fusers {r.Stats.Fusers} clears {r.Stats.Clears} kills {r.Kills} | {balls} | {items} | hurt {hurt} | {sw.ElapsedMilliseconds}ms");
                foreach (var l in r.Stats.Log) Console.WriteLine("      " + l);
                if (err != null) Console.WriteLine("      ERROR " + err);
            }
        }
        Console.WriteLine($"\nruns {total}  wins {wins}  errors {errors}  furthest boss seen: none {reached[0]}, b1 {reached[1]}, b2 {reached[2]}, final {reached[3]}");
        return errors > 0 ? 1 : 0;
    }
}
