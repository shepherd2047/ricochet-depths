// A ball's look: the 16×16 sprite, its pixel halo and the element trail (a ribbon plus a few trail particles).
//   Ember: tapered trail whose tail drifts up like heat, rising sparks.
//   Rime:  thin, long frost streak, frost flakes that hang in the air.
//   Spark: zig-zag segments re-jagged 15 times a second, small zaps.
//   Ray:   straight hard beam line, lens glints left behind.
using UnityEngine;

namespace RicochetVfx
{
    public sealed class VfxBall : VfxBehaviour
    {
        public VfxElement element;
        public Transform sprite, halo;
        public VfxRibbon ribbon;
        public ParticleSystem trailFx;
        [Tooltip("Seconds of history the trail shows.")] public float trailTime = 0.3f;
        [Tooltip("Trail width at the head, in cells.")] public float trailWidth = 0.3f;
        [Tooltip("Cells between trail particles.")] public float emitSpacing = 0.15f;
        public float haloPulse = 0.1f;

        const int Max = 64;
        readonly Vector3[] hp = new Vector3[Max];
        readonly float[] ht = new float[Max];
        int count;
        Transform target;
        bool detached;
        Vector3 last;
        float dist;
        uint seed;
        Vector3 haloBase, spriteBase;
        System.Random rng;

        public override void OnSpawn()
        {
            life = 0;
            if (haloBase == Vector3.zero) { haloBase = halo.localScale; spriteBase = sprite.localScale; }
            seed = (uint)Random.Range(1, int.MaxValue);
            rng = new System.Random((int)seed);
            count = 0;
            dist = 0;
            detached = false;
            sprite.gameObject.SetActive(true);
            halo.gameObject.SetActive(true);
            ribbon.Hide();
        }

        public void Follow(Transform t)
        {
            target = t;
            transform.position = t.position;
            last = t.position;
            count = 0;
            Push(t.position);
        }

        /// <summary>The ball is gone: hide the sprite, let the trail run out, then return to the pool.</summary>
        public void Detach()
        {
            target = null;
            detached = true;
            sprite.gameObject.SetActive(false);
            halo.gameObject.SetActive(false);
        }

        void Push(Vector3 p)
        {
            for (int i = Mathf.Min(count, Max - 1); i > 0; i--) { hp[i] = hp[i - 1]; ht[i] = ht[i - 1]; }
            hp[0] = p; ht[0] = VfxBank.Now;
            count = Mathf.Min(count + 1, Max);
        }

        public override void Step(float dt)
        {
            float now = VfxBank.Now, sc = VfxBank.Scale;
            if (target != null)
            {
                if (!target.gameObject.activeInHierarchy) Detach();
                else transform.position = target.position;
            }
            var p = transform.position;
            if (!detached && (count == 0 || (p - hp[0]).sqrMagnitude > 0.0004f * sc * sc)) Push(p);
            while (count > 0 && now - ht[count - 1] > trailTime) count--;
            if (detached && count == 0) { ribbon.Hide(); VfxBank.Release(this); return; }

            if (!detached)
            {
                var seg = p - last;
                float len = seg.magnitude, step = emitSpacing * sc;
                dist += len;
                while (dist >= step) { dist -= step; EmitTrail(p - seg.normalized * dist, seg / Mathf.Max(dt, 1e-4f)); }
                last = p;

                // halo breathes in steps at 12 fps (pixel-rate animation)
                float k = VfxJag.Hash(seed, (uint)Mathf.FloorToInt(now * 12));
                halo.localScale = haloBase * (1 + haloPulse * (k * 2 - 1));
                sprite.localScale = spriteBase;
            }
            BuildTrail(now, p);
        }

        void EmitTrail(Vector3 pos, Vector3 vel)
        {
            if (trailFx == null) return;
            float sc = VfxBank.Scale;
            var up = VfxBank.Up;
            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            var rnd = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f);
            switch (element)
            {
                case VfxElement.Ember:
                    ep.position = pos + rnd * 0.12f * sc;
                    ep.velocity = (up * (0.7f + 0.6f * (float)rng.NextDouble()) + rnd * 0.6f - vel.normalized * 0.4f) * sc;
                    break;
                case VfxElement.Rime:
                    ep.position = pos + rnd * 0.18f * sc;
                    ep.velocity = (rnd * 0.3f + up * 0.05f) * sc;
                    break;
                case VfxElement.Spark:
                    ep.position = pos + rnd * 0.1f * sc;
                    ep.velocity = rnd.normalized * 1.6f * sc;
                    break;
                default:
                    ep.position = pos;
                    ep.velocity = Vector3.zero;
                    break;
            }
            trailFx.Emit(ep, 1);
        }

        void BuildTrail(float now, Vector3 head)
        {
            ribbon.ClearPoints();
            float sc = VfxBank.Scale, w0 = trailWidth * sc;
            var up = VfxBank.Up;
            int n = count;
            if (element == VfxElement.Spark) { BuildZigZag(now, head, w0); return; }
            for (int i = -1; i < n; i++)
            {
                if (i == -1 && detached) continue;
                var p = i < 0 ? head : hp[i];
                float a = i < 0 ? 0 : Mathf.Clamp01((now - ht[i]) / trailTime);
                float w, heat, alpha;
                switch (element)
                {
                    case VfxElement.Ember:
                        p += up * (a * a * 0.35f * sc);    // the tail drifts up like heat
                        w = w0 * Mathf.Pow(1 - a, 0.7f);
                        heat = 1 - 0.8f * a;
                        alpha = 1 - a;
                        break;
                    case VfxElement.Rime:
                        w = w0 * (1 - 0.6f * a);
                        heat = 1 - 0.45f * a;
                        alpha = 1 - a * a;
                        break;
                    default:    // Ray: a hard, even line that only fades at the very end
                        w = w0 * (1 - a * a * a);
                        heat = 1 - 0.15f * a;
                        alpha = 1 - a * a * a * a;
                        break;
                }
                ribbon.Add(p, w, new Color(heat, VfxJag.Hash(seed, (uint)i + 7), 0, alpha));
            }
            ribbon.Build();
        }

        void BuildZigZag(float now, Vector3 head, float w0)
        {
            // walk the history polyline and drop a vertex every `seg` cells, offset alternately to either side
            float sc = VfxBank.Scale, seg = 0.13f * sc, amp = 0.12f * sc;
            uint frame = (uint)Mathf.FloorToInt(now * 15);
            var cam = VfxBank.CamPos;
            Vector3 prev = detached ? (count > 0 ? hp[0] : head) : head;
            float prevT = detached ? (count > 0 ? ht[0] : now) : now;
            float carry = 0;
            int k = 0;
            ribbon.Add(prev, w0, new Color(1, 0, 0, 1));
            for (int i = detached ? 1 : 0; i < count; i++)
            {
                var d = hp[i] - prev;
                float len = d.magnitude;
                if (len < 1e-5f) continue;
                var side = Vector3.Cross(d, cam - hp[i]).normalized;
                bool any = false;
                for (float at = seg - carry; at <= len; at += seg)
                {
                    float t = at / len;
                    float a = Mathf.Clamp01((now - Mathf.Lerp(prevT, ht[i], t)) / trailTime);
                    k++;
                    float s = (k % 2 == 0 ? 1 : -1) * (0.4f + 0.6f * VfxJag.Hash(seed, frame, (uint)k));
                    float alpha = (1 - a) * (VfxJag.Hash(seed + 3, frame, (uint)k) < 0.12f ? 0.3f : 1f);
                    ribbon.Add(prev + d * t + side * s * amp * (1 - a * 0.5f), w0 * (1 - 0.5f * a),
                        new Color(1 - 0.3f * a, VfxJag.Hash(seed, (uint)k), 0, alpha));
                    carry = len - at;
                    any = true;
                }
                if (!any) carry += len;
                prev = hp[i];
                prevT = ht[i];
            }
            ribbon.Build();
        }

        public override void OnRelease()
        {
            target = null;
            ribbon.Hide();
        }
    }
}
