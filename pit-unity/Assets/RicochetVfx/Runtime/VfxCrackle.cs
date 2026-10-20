// Spark hit arcs.
using UnityEngine;

namespace RicochetVfx
{
    /// <summary>Spark hit: a few jagged arcs crackle out from the contact point, re-jagged 20 times a second.</summary>
    public sealed class VfxCrackle : VfxOneShot
    {
        public VfxRibbon[] arcs;
        public float arcLength = 0.75f, arcWidth = 0.12f, duration = 0.24f, fps = 20f;
        uint seed;
        int frame = -1;

        public override void OnSpawn()
        {
            seed = (uint)Random.Range(1, int.MaxValue);
            frame = -1;
        }

        public override void Step(float dt)
        {
            if (age < duration)
            {
                int f = Mathf.FloorToInt(age * fps);
                if (f != frame)
                {
                    frame = f;
                    float sc = VfxBank.Scale, k = age / duration;
                    float heat = (f % 2 == 1 ? 0.55f : 1f) * (1 - 0.5f * k);
                    var c = transform.position;
                    for (int i = 0; i < arcs.Length; i++)
                    {
                        var r = arcs[i];
                        r.ClearPoints();
                        uint s = seed + (uint)i * 101u;
                        // fan out over the half space in front of the contact (local +Z = surface normal)
                        float ang = (i + VfxJag.Hash(s, 1) * 0.8f) / arcs.Length * Mathf.PI - Mathf.PI / 2;
                        var dir = transform.TransformDirection(new Vector3(Mathf.Sin(ang), (VfxJag.Hash(s, 2) - 0.3f) * 0.8f, Mathf.Cos(ang))).normalized;
                        float len = arcLength * sc * (0.55f + 0.45f * VfxJag.Hash(s, 3)) * (0.6f + 0.4f * Mathf.Min(1, k * 4));
                        VfxJag.Line(r, c, c + dir * len, 5, 0.16f * sc, arcWidth * sc, new Color(heat, 0, 0, 1), s * 31u + (uint)f);
                        r.Build();
                    }
                }
            }
            else if (frame >= 0)
            {
                frame = -1;
                foreach (var r in arcs) r.Hide();
            }
            base.Step(dt);
        }

        public override void OnRelease()
        {
            foreach (var r in arcs) r.Hide();
        }
    }
}
