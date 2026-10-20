// Freeze shell (see VfxStatus.cs for the burn).
using System.Collections.Generic;
using UnityEngine;

namespace RicochetVfx
{
    /// <summary>
    /// Freeze: a faceted ice shell (a scaled copy of each enemy mesh drawn with RicochetVfx/IceShell) that snaps on in
    /// four steps, glints while frozen and is removed when the freeze breaks (VfxHitFeedback spawns the shatter).
    /// </summary>
    public sealed class VfxIceShell : VfxStatus
    {
        public Material shellMaterial;
        public float growTime = 0.16f, breakTime = 0.06f, shellScale = 1.14f;

        static readonly int AmountId = Shader.PropertyToID("_Amount");
        static readonly int CenterId = Shader.PropertyToID("_ShellCenter");
        static readonly int ScaleId = Shader.PropertyToID("_ShellScale");

        readonly List<MeshRenderer> shells = new List<MeshRenderer>();
        readonly List<Mesh> baked = new List<Mesh>();
        MaterialPropertyBlock mpb;
        bool breaking;
        float breakAt;

        public void Attach(IList<Renderer> src)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in src)
            {
                Mesh m = null;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null) m = mf.sharedMesh;
                var sk = r as SkinnedMeshRenderer;
                if (sk != null) { m = new Mesh(); sk.BakeMesh(m, false); baked.Add(m); }
                if (m == null) continue;
                var go = new GameObject("IceShell");
                go.transform.SetParent(r.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = shellMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mpb.Clear();
                mpb.SetVector(CenterId, m.bounds.center);
                mpb.SetFloat(ScaleId, shellScale);
                mpb.SetFloat(AmountId, 0);
                mr.SetPropertyBlock(mpb);
                shells.Add(mr);
            }
        }

        public override void OnSpawn()
        {
            life = 0;
            breaking = false;
        }

        /// <summary>The freeze broke: the shell blinks white and disappears (the shatter burst is a separate effect).</summary>
        public void Break()
        {
            if (breaking) return;
            breaking = true;
            breakAt = age;
            StopEmitting();
        }

        public override void Stop()
        {
            Break();
        }

        public override void Step(float dt)
        {
            float a;
            if (breaking)
            {
                a = 1 + Mathf.Clamp01((age - breakAt) / breakTime);     // 1..2: the shader turns it white and thin
                if (age - breakAt >= breakTime) { VfxBank.Release(this); return; }
            }
            else a = Mathf.Floor(Mathf.Clamp01(age / growTime) * 4) / 4;     // snaps on in 4 steps
            foreach (var mr in shells)
            {
                if (mr == null) continue;
                mr.GetPropertyBlock(mpb);
                mpb.SetFloat(AmountId, a);
                mr.SetPropertyBlock(mpb);
            }
            base.Step(dt);
        }

        public override void OnRelease()
        {
            foreach (var mr in shells) if (mr != null) VfxBank.Kill(mr.gameObject);
            foreach (var m in baked) VfxBank.Kill(m);
            shells.Clear();
            baked.Clear();
        }
    }
}
