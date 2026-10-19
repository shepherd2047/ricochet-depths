// Engine-free basics: seeded RNG, math helpers, diminishing stat curves, stat blocks.
// Nothing in Pit.Core references UnityEngine, so the same files compile in the dotnet harness.
using System;
using System.Collections.Generic;

namespace Pit.Core
{
    /// <summary>mulberry32: small, fast and reproducible across platforms.</summary>
    public sealed class Rng
    {
        uint a;
        public Rng(long seed) { a = (uint)seed; if (a == 0) a = 1; }

        public double NextD()
        {
            unchecked
            {
                a += 0x6D2B79F5;
                uint t = (a ^ (a >> 15)) * (1u | a);
                t = (t + (t ^ (t >> 7)) * (61u | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public float Next() { return (float)NextD(); }
        public float Range(float lo, float hi) { return lo + (hi - lo) * (float)NextD(); }
        public int Int(int lo, int hi) { return lo + (int)Math.Floor(NextD() * (hi - lo + 1)); }
        public bool Chance(float p) { return NextD() < p; }
        public T Pick<T>(IList<T> list) { return list[(int)Math.Floor(NextD() * list.Count)]; }
        public T Weighted<T>(IList<T> list, Func<T, float> wf)
        {
            double tot = 0;
            for (int i = 0; i < list.Count; i++) tot += wf(list[i]);
            double r = NextD() * tot;
            for (int i = 0; i < list.Count; i++) { r -= wf(list[i]); if (r <= 0) return list[i]; }
            return list[list.Count - 1];
        }
        public List<T> Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(NextD() * (i + 1));
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
            return list;
        }
    }

    public static class M
    {
        public const float PI = (float)Math.PI;
        public const float Deg = PI / 180f;
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Abs(float v) { return v < 0 ? -v : v; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int Round(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }
        public static int Ceil(float v) { return (int)Math.Ceiling(v); }
        public static int Floor(float v) { return (int)Math.Floor(v); }

        // Diminishing-returns curves: piecewise linear through the spec samples, linear past the end.
        static readonly float[][] BallSpeed = { new[] { 0f, 1f }, new[] { 3f, 3.77f }, new[] { 6f, 6.07f }, new[] { 10f, 8.36f }, new[] { 15f, 10.17f } };
        static readonly float[][] CritC = { new[] { 0f, 1f }, new[] { 3f, 3.4f }, new[] { 6f, 5.01f }, new[] { 10f, 6.38f }, new[] { 15f, 7.71f } };
        static readonly float[][] FireC = { new[] { 0f, 1f }, new[] { 3f, 3.76f }, new[] { 6f, 6.07f }, new[] { 10f, 8.37f }, new[] { 15f, 10.01f } };
        static readonly float[][] MoveC = { new[] { 0f, 1f }, new[] { 3f, 3.53f }, new[] { 6f, 5.33f }, new[] { 10f, 6.71f }, new[] { 15f, 7.4f } };
        // Lead: "about s+1 while small", flattening later (own fit; the spec gives no samples).
        static readonly float[][] LeadC = { new[] { 0f, 1f }, new[] { 3f, 4f }, new[] { 6f, 6.6f }, new[] { 10f, 9f }, new[] { 15f, 11.5f } };

        public enum Curve { BallSpeed, Crit, Fire, Move, Lead }
        public static float W(Curve c, float s)
        {
            float[][] t = c == Curve.BallSpeed ? BallSpeed : c == Curve.Crit ? CritC : c == Curve.Fire ? FireC : c == Curve.Move ? MoveC : LeadC;
            if (s <= t[0][0]) return t[0][1];
            for (int i = 1; i < t.Length; i++)
            {
                if (s <= t[i][0])
                {
                    float[] a = t[i - 1], b = t[i];
                    return a[1] + (b[1] - a[1]) * (s - a[0]) / (b[0] - a[0]);
                }
            }
            float[] p = t[t.Length - 2], q = t[t.Length - 1];
            return q[1] + (q[1] - p[1]) / (q[0] - p[0]) * (s - q[0]);
        }

        /// <summary>XP needed to go from level L to L+1.</summary>
        public static int XpNeed(int L)
        {
            if (L == 0) return 8;
            if (L == 1) return 30;
            if (L == 2) return 80;
            return 110 + Round(5.25f * L * (L - 0.7f));
        }
    }

    /// <summary>Integer-ish stat table keyed by name. Missing keys read as 0.</summary>
    public sealed class Stats : Dictionary<string, float>
    {
        public Stats() { }
        public Stats(Stats o) : base(o) { }
        public float G(string k) { float v; return TryGetValue(k, out v) ? v : 0f; }
        public bool Has(string k) { float v; return TryGetValue(k, out v) && v != 0f; }
        public Stats With(string k, float v) { this[k] = v; return this; }
    }

    /// <summary>Stat keys used by balls and items.</summary>
    public static class K
    {
        public const string BonusMin = "bonusMin", BonusMax = "bonusMax", Mult = "mult", Heavy = "heavy";
        public const string Pierce = "pierce", Backstab = "backstab", SelfDestruct = "selfDestruct", Cooldown = "cooldown";
        public const string BurnMin = "burnMin", BurnMax = "burnMax", BurnDur = "burnDur", BurnCap = "burnCap";
        public const string FfMin = "ffMin", FfMax = "ffMax", FfDur = "ffDur", FfCap = "ffCap";
        public const string PoisonMin = "poisonMin", PoisonMax = "poisonMax", PoisonDur = "poisonDur", PoisonCap = "poisonCap";
        public const string BleedCap = "bleedCap", FreezeChance = "freezeChance", FreezeDur = "freezeDur";
        public const string SlowPct = "slowPct", SlowDur = "slowDur", BlindDur = "blindDur", CharmChance = "charmChance", Leech = "leech";
        public const string ChainMin = "chainMin", ChainMax = "chainMax", ChainCount = "chainCount";
        public const string RowMin = "rowMin", RowMax = "rowMax", ColMin = "colMin", ColMax = "colMax";
        public const string BombMin = "bombMin", BombMax = "bombMax", BombR = "bombR";
        public const string QuakeMin = "quakeMin", QuakeMax = "quakeMax", QuakeSize = "quakeSize";
        public const string FlashMin = "flashMin", FlashMax = "flashMax", Nuke = "nuke";
        public const string AuraR = "auraR", AuraMin = "auraMin", AuraMax = "auraMax", AuraSlow = "auraSlow";
        public const string SplitMax = "splitMax", EggMin = "eggMin", EggMax = "eggMax";
    }
}
