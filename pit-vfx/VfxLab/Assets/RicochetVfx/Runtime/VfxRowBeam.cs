// Ray: horizontal laser across the whole row.
//   charge (0.14 s): a thin flickering pilot line, particles converge on the hit point, the flare there grows;
//   beam (0.22 s):   full-width bright core and soft outer glow, overshooting for the first frames, a spark on every
//                    enemy in the row and a flash at both board edges;
//   fade (0.30 s):   the core thins, the glow lingers, the line shimmers apart and drops sparkles.
using System.Collections.Generic;
using UnityEngine;

namespace RicochetVfx
{
    public sealed class VfxRowBeam : VfxBehaviour
    {
        public VfxRibbon core, glow;
        public ParticleSystem sparkle, caps;
        public float chargeTime = 0.14f, holdTime = 0.22f, fadeTime = 0.3f;
        public float coreWidth = 0.28f, glowWidth = 1.1f;
        public int points = 32;

        Vector3 from, to;
        readonly List<Vector3> targets = new List<Vector3>();
        bool fired, faded;
        uint seed;

        public void Fire(Vector3 a, Vector3 b, Vector3 origin, IList<Vector3> hitTargets)
        {
            from = a; to = b;
            targets.Clear();
            if (hitTargets != null) targets.AddRange(hitTargets);
            fired = faded = false;
            seed = (uint)Random.Range(1, int.MaxValue);
            life = chargeTime + holdTime + fadeTime + 0.5f;
            Build(0);
        }

        public override void Step(float dt)
        {
            float t = age;
            if (!fired && t >= chargeTime)
            {
                fired = true;
                var bank = VfxBank.Instance.bank;
                foreach (var p in targets)
                    VfxBank.Spawn<VfxBehaviour>(bank.rowHit, p, VfxBank.VfxFrame(Vector3.Cross(VfxBank.Up, to - from)));
                Emit(caps, from, Vector3.zero); Emit(caps, to, Vector3.zero);
            }
            if (!faded && t >= chargeTime + holdTime)
            {
                faded = true;
                float sc = VfxBank.Scale;
                for (int i = 0; i < 14; i++)
                {
                    float u = (i + VfxJag.Hash(seed, (uint)i)) / 14f;
                    Emit(sparkle, Vector3.Lerp(from, to, u), VfxBank.Up * (0.4f + 0.6f * VfxJag.Hash(seed, (uint)i, 1)) * sc);
                }
            }
            Build(t);
            base.Step(dt);
        }

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel)
        {
            if (ps == null) return;
            ps.Emit(new ParticleSystem.EmitParams { position = pos, velocity = vel, applyShapeToPosition = false }, 1);
        }

        void Build(float t)
        {
            float sc = VfxBank.Scale;
            uint frame = (uint)Mathf.FloorToInt(t * 24);
            float cw, gw, heat, shimmer, gHeat;
            if (t < chargeTime)
            {
                float k = t / chargeTime;
                bool on = frame % 2 == 0;
                cw = (0.03f + 0.04f * k) * sc * (on ? 1 : 0.6f);
                gw = 0.25f * k * sc;
                heat = (0.55f + 0.35f * k) * (on ? 1 : 0.7f);
                gHeat = 0.35f;
                shimmer = 0.4f;
            }
            else if (t < chargeTime + holdTime)
            {
                float k = (t - chargeTime) / holdTime;
                float pop = k < 0.12f ? 1.3f : 1f;
                cw = coreWidth * sc * pop;
                gw = glowWidth * sc * pop;
                heat = 1f;
                gHeat = 0.6f;
                shimmer = 0.08f;
            }
            else if (t < chargeTime + holdTime + fadeTime)
            {
                float k = (t - chargeTime - holdTime) / fadeTime;
                cw = coreWidth * sc * Mathf.Pow(1 - k, 1.5f);
                gw = glowWidth * sc * (1 - 0.55f * k);
                heat = 1f - 0.6f * k;
                gHeat = 0.6f * (1 - k);
                shimmer = 0.15f + 0.85f * k;
            }
            else
            {
                core.Hide(); glow.Hide();
                return;
            }
            core.ClearPoints(); glow.ClearPoints();
            for (int i = 0; i < points; i++)
            {
                float u = i / (float)(points - 1);
                var p = Vector3.Lerp(from, to, u);
                float edge = Mathf.Min(1, Mathf.Min(u, 1 - u) * 24);     // short taper at the board edges
                float n = VfxJag.Hash(seed, frame, (uint)i);
                core.Add(p, cw * (0.6f + 0.4f * edge), new Color(heat, n, shimmer, 1));
                glow.Add(p, gw * (0.7f + 0.3f * edge), new Color(gHeat, n, shimmer, 1));
            }
            core.Build(); glow.Build();
        }

        public override void OnRelease()
        {
            core.Hide(); glow.Hide();
        }
    }
}
