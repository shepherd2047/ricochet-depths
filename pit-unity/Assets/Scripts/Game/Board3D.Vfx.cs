// pit-vfx's RicochetVfx for four balls (pit-vfx/INTEGRATION.md): Ember, Rime (id "frost"), Spark and Ray get the pooled
// sprite + halo + trail, hit effects, the enemy flash / tint / status overlays, the chain bolt and the row laser. Every
// other ball keeps WorldView's sprite, trail and beam shapes. The core only reports what happened (Run.BallHits, the
// Beam.Src / Target / Targets tags); this file turns it into VfxBank calls once per frame, after Units().
// VfxBank ticks itself (execution order 1000, after GameRoot), so VfxHitFeedback writes its block after Board3D's.
using System.Collections.Generic;
using Pit.Core;
using RicochetVfx;
using UnityEngine;

namespace Pit.Game
{
    public sealed partial class Board3D
    {
        static readonly HashSet<string> VfxIds = new HashSet<string> { "ember", "frost", "spark", "ray" };

        /// <summary>True for the balls whose look comes from RicochetVfx (WorldView skips their sprite and beams).</summary>
        public static bool HasVfx(string ballId) { return ballId != null && VfxIds.Contains(ballId); }
        public static bool HasVfx(FieldBall b) { return b.Inst != null && !b.Baby && HasVfx(b.Inst.Id); }

        // the ball sprites' height above the floor (WorldView's ZBall) and the lift of hit points and row targets
        const float VfxBallZ = -0.4f, VfxHitZ = -0.45f;

        sealed class VfxProxy { public Transform T; public VfxBall H; public bool Seen; }
        readonly Dictionary<FieldBall, VfxProxy> vfxBalls = new Dictionary<FieldBall, VfxProxy>();
        readonly Stack<Transform> vfxFree = new Stack<Transform>();
        readonly List<FieldBall> vfxGone = new List<FieldBall>();
        readonly List<Vector3> vfxPts = new List<Vector3>(), vfxTargets = new List<Vector3>();

        /// <summary>VfxBank calls so far (hits, chains, row beams) and foes now burning / frozen: PitCapture times its shot.</summary>
        public int VfxHitCount, VfxChainCount, VfxRowCount, VfxBurning, VfxFrozen, VfxChainHops;

        Vector3 W(float bx, float by, float z) { return units.TransformPoint(P(bx, by, z)); }

        void Vfx(Run r)
        {
            var bank = VfxBank.Ensure();
            if (bank.viewCamera != Cam) bank.viewCamera = Cam;
            VfxBallsFollow(r);
            VfxHits(r);
            VfxStatuses(r);
            VfxBeams(r);
        }

        void VfxBallsFollow(Run r)
        {
            foreach (var kv in vfxBalls) kv.Value.Seen = false;
            foreach (var b in r.Field)
            {
                if (!HasVfx(b)) continue;
                VfxProxy px;
                if (!vfxBalls.TryGetValue(b, out px))
                {
                    var t = vfxFree.Count > 0 ? vfxFree.Pop() : new GameObject("vfx ball").transform;
                    t.SetParent(units, false);
                    t.localPosition = P(b.X, b.Y, VfxBallZ);
                    t.gameObject.SetActive(true);
                    px = new VfxProxy { T = t, H = VfxBank.Ball(b.Inst.Id, t) };
                    vfxBalls[b] = px;
                }
                px.T.localPosition = P(b.X, b.Y, VfxBallZ);
                px.Seen = true;
            }
            vfxGone.Clear();
            foreach (var kv in vfxBalls) if (!kv.Value.Seen) vfxGone.Add(kv.Key);
            foreach (var b in vfxGone) { ReleaseProxy(vfxBalls[b]); vfxBalls.Remove(b); }
        }

        void ReleaseProxy(VfxProxy px)
        {
            if (px.H != null) VfxBank.StopBall(px.H);
            px.T.gameObject.SetActive(false);
            vfxFree.Push(px.T);
        }

        void VfxHits(Run r)
        {
            foreach (var h in r.BallHits)
            {
                if (!HasVfx(h.Ball)) continue;
                float nx = h.X - h.Ex, ny = -(h.Y - h.Ey), nl = Mathf.Sqrt(nx * nx + ny * ny);
                var n = nl > 1e-3f ? units.TransformDirection(new Vector3(nx / nl, ny / nl, 0)) : -units.forward;
                VfxBank.Hit(h.Ball, W(h.X, h.Y, VfxBallZ), n);
                VfxHitCount++;
                View v;
                if (!live.TryGetValue(h.Enemy, out v)) continue;
                VfxBank.Flash(v.Go, h.Ball);
                v.Fx = true;
                // the status overlays show for statuses these balls put on (a burn from Cinderquake keeps its old look)
                if (h.Ball == "ember") v.BurnSrc = true;
                if (h.Ball == "frost") v.FrostSrc = true;
            }
            r.BallHits.Clear();
        }

        void VfxStatuses(Run r)
        {
            VfxBurning = VfxFrozen = 0;
            foreach (var e in r.Enemies)
            {
                View v;
                if (e.Dead || !live.TryGetValue(e.Id, out v) || !v.Seen) continue;
                bool burn = r.Has(e, "burn"), frz = r.Has(e, "freeze");
                if (!burn) v.BurnSrc = false;
                if (!frz) v.FrostSrc = false;
                burn &= v.BurnSrc; frz &= v.FrostSrc;
                if (burn != v.BurnOn) { VfxBank.Status(v.Go, "ember", burn); v.BurnOn = burn; v.Fx = true; }
                if (frz != v.FrostOn) { VfxBank.Status(v.Go, "frost", frz); v.FrostOn = frz; v.Fx = true; }
                if (burn) VfxBurning++;
                if (frz) VfxFrozen++;
            }
        }

        /// <summary>A foe's view stops (death starts): its statuses end (a frozen foe shatters).</summary>
        static void VfxDeath(View v)
        {
            if (v.BurnOn) VfxBank.Status(v.Go, "ember", false);
            if (v.FrostOn) VfxBank.Status(v.Go, "frost", false);
            v.BurnOn = v.FrostOn = v.BurnSrc = v.FrostSrc = false;
        }

        /// <summary>Before a view goes back to the pool (or is reused for another foe).</summary>
        static void VfxRelease(View v)
        {
            if (v.Fx) VfxBank.ClearStatus(v.Go);
            v.Fx = v.BurnOn = v.FrostOn = v.BurnSrc = v.FrostSrc = false;
        }

        void VfxBeams(Run r)
        {
            var list = r.Beams;
            for (int i = 0; i < list.Count; i++)
            {
                var bm = list[i];
                if (bm.Shown) continue;
                bm.Shown = true;
                if (!HasVfx(bm.Src)) continue;
                if (bm.Kind == BeamKind.Arc)
                {
                    // one hit's hops are consecutive in the list, each starting where the last one ended
                    vfxPts.Clear();
                    vfxPts.Add(W(bm.X1, bm.Y1, VfxHitZ));
                    vfxPts.Add(W(bm.X2, bm.Y2, VfxHitZ));
                    FlashLater(bm.Target, bm.Src, 0);
                    int hop = 1;
                    while (i + 1 < list.Count)
                    {
                        var nb = list[i + 1];
                        if (nb.Shown || nb.Kind != BeamKind.Arc || nb.Src != bm.Src || nb.X1 != list[i].X2 || nb.Y1 != list[i].Y2) break;
                        nb.Shown = true;
                        vfxPts.Add(W(nb.X2, nb.Y2, VfxHitZ));
                        FlashLater(nb.Target, nb.Src, hop++ * 0.07f);
                        i++;
                    }
                    VfxBank.Chain(vfxPts, 0.07f);
                    VfxChainCount++;
                    VfxChainHops = vfxPts.Count - 1;
                }
                else if (bm.Kind == BeamKind.Row)
                {
                    vfxTargets.Clear();
                    if (bm.Targets != null)
                        foreach (var id in bm.Targets)
                        {
                            View v;
                            if (live.TryGetValue(id, out v)) vfxTargets.Add(units.TransformPoint(v.Go.transform.localPosition + new Vector3(0, 0, VfxHitZ)));
                            FlashLater(id, bm.Src, 0.14f);
                        }
                    var a = W(0, bm.Y, 0);
                    VfxBank.RowBeam(a.y, a.x, W(r.Bw, bm.Y, 0).x, vfxTargets, W(bm.X, bm.Y, 0).x);
                    VfxRowCount++;
                }
            }
        }

        void FlashLater(int id, string ball, float delay)
        {
            if (id < 0) return;
            if (delay <= 0) { FlashFoe(id, ball); return; }
            VfxBank.Delay(delay, () => FlashFoe(id, ball));
        }

        void FlashFoe(int id, string ball)
        {
            View v;
            if (!live.TryGetValue(id, out v)) return;
            VfxBank.Flash(v.Go, ball);
            v.Fx = true;
        }

        /// <summary>New run: every effect and ball proxy goes.</summary>
        void VfxClear()
        {
            foreach (var kv in vfxBalls) { kv.Value.T.gameObject.SetActive(false); vfxFree.Push(kv.Value.T); }
            vfxBalls.Clear();
            if (VfxBank.Instance != null) VfxBank.ReleaseAll();
        }
    }
}
