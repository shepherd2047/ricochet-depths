// Chain lightning between two enemies: a jagged main bolt plus one short fork, re-jagged 20 times a second.
// Full brightness for 0.1 s, then it flickers out (every other frame dim) over the rest of its life.
using UnityEngine;

namespace RicochetVfx
{
    public sealed class VfxBolt : VfxBehaviour
    {
        public VfxRibbon main, fork;
        public float width = 0.36f, forkWidth = 0.2f, amp = 0.32f, fps = 20f, holdTime = 0.1f;
        Vector3 from, to;
        uint seed;
        int frame = -1;

        public void Fire(Vector3 a, Vector3 b)
        {
            from = a; to = b;
            seed = (uint)Random.Range(1, int.MaxValue);
            frame = -1;
            Rebuild(0);
        }

        public override void Step(float dt)
        {
            int f = Mathf.FloorToInt(age * fps);
            if (f != frame) Rebuild(f);
            base.Step(dt);
        }

        void Rebuild(int f)
        {
            frame = f;
            float sc = VfxBank.Scale, t = age;
            float heat = t < holdTime ? 1f : Mathf.Lerp(1f, 0.35f, (t - holdTime) / Mathf.Max(0.01f, life - holdTime));
            if (t >= holdTime && f % 2 == 1) heat *= 0.45f;
            float alpha = t < holdTime ? 1f : 1f - 0.6f * (t - holdTime) / Mathf.Max(0.01f, life - holdTime);
            var c = new Color(heat, 0, 0, alpha);
            float dist = Vector3.Distance(from, to);
            int segs = Mathf.Clamp(Mathf.RoundToInt(dist / (0.28f * sc)), 4, 24);
            uint s = seed * 7u + (uint)f;

            main.ClearPoints();
            VfxJag.Line(main, from, to, segs, amp * sc, width * sc, c, s);
            main.Build();

            fork.ClearPoints();
            int at = Mathf.Clamp(Mathf.RoundToInt(segs * (0.35f + 0.3f * VfxJag.Hash(s, 9))), 1, segs - 1);
            var p = main.points[at];
            var side = Vector3.Cross(to - from, VfxBank.CamPos - p).normalized * (VfxJag.Hash(s, 10) < 0.5f ? 1 : -1);
            var end = p + (side * 0.45f + (to - from).normalized * 0.3f + VfxBank.Up * 0.15f) * sc;
            var fc = c; fc.r *= 0.8f;
            VfxJag.Line(fork, p, end, 3, amp * 0.6f * sc, forkWidth * sc, fc, s + 5);
            fork.Build();
        }

        public override void OnRelease()
        {
            main.Hide();
            fork.Hide();
        }
    }
}
