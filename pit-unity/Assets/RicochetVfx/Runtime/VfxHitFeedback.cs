// Per-enemy hit feedback through a MaterialPropertyBlock (no material copies). VfxBank adds it on first use.
// Shader properties written (see RicochetHitFx.hlsl; _Flash and _Ice have the same meaning as in pit-unity's PitUnit):
//   _Flash    0..1  white hit flash, two pixel-rate steps, 0.1 s
//   _HitTint  rgb = element colour, a = amount (decays over 0.45 s)
//   _Ice      0..1  frozen overlay
//   _Burn     0..1  charred surface with flickering embers
//   _Shock    0..1  electric rim flicker
//   _VfxSeed  per-enemy random so identical enemies flicker out of step
using System.Collections.Generic;
using UnityEngine;

namespace RicochetVfx
{
    [DisallowMultipleComponent]
    public sealed class VfxHitFeedback : MonoBehaviour
    {
        public float flashTime = 0.1f, tintTime = 0.45f, shockTime = 0.35f;

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int TintId = Shader.PropertyToID("_HitTint");
        static readonly int IceId = Shader.PropertyToID("_Ice");
        static readonly int BurnId = Shader.PropertyToID("_Burn");
        static readonly int ShockId = Shader.PropertyToID("_Shock");
        static readonly int SeedId = Shader.PropertyToID("_VfxSeed");

        readonly List<Renderer> rends = new List<Renderer>();
        MaterialPropertyBlock mpb;
        float flash, tintAmt, shock, ice, burn, seed;
        Color tint;
        bool burning, frozen, shocked, dirty;
        VfxStatus burnFx;
        VfxIceShell shell;

        bool inited;

        void Awake() { Init(); }
        void OnEnable() { if (inited) VfxBank.Register(this); }
        void OnDisable() { VfxBank.Unregister(this); }

        /// <summary>Idempotent setup; VfxBank calls it too because Awake does not run in the editor capture.</summary>
        internal void Init()
        {
            if (inited) return;
            inited = true;
            Collect();
            seed = Random.value * 10f;
            VfxBank.Register(this);
        }

        /// <summary>Finds the enemy's own mesh renderers (not VFX). Call again if the model is swapped.</summary>
        public void Collect()
        {
            rends.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (r.GetComponentInParent<VfxBehaviour>() != null || r.name == "IceShell") continue;
                rends.Add(r);
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
        }

        public Bounds WorldBounds()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool first = true;
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        public void Flash(VfxElement e)
        {
            flash = 1;
            tint = VfxElements.BaseColor(e);
            tintAmt = 1;
            if (e == VfxElement.Spark) shock = 1;
            dirty = true;
        }

        public void SetStatus(VfxElement e, bool on)
        {
            var set = VfxBank.Ensure().bank.Get(e);
            switch (e)
            {
                case VfxElement.Ember:
                    burning = on;
                    if (on && burnFx == null && set.status != null)
                    {
                        var b = WorldBounds();
                        burnFx = VfxBank.Spawn<VfxStatus>(set.status, b.center, VfxBank.VfxFrame(Vector3.right), transform);
                        burnFx.FitShapes(b, 0.8f);
                    }
                    else if (!on && burnFx != null) { if (burnFx.live) burnFx.Stop(); burnFx = null; }
                    break;
                case VfxElement.Rime:
                    if (on && !frozen)
                    {
                        var b = WorldBounds();
                        if (set.status != null)
                        {
                            shell = VfxBank.Spawn<VfxIceShell>(set.status, b.center, VfxBank.VfxFrame(Vector3.right), transform);
                            shell.Attach(rends);
                            shell.FitShapes(b, 1f);
                        }
                    }
                    else if (!on && frozen)
                    {
                        if (shell != null && shell.live) shell.Break();
                        shell = null;
                        var b = WorldBounds();
                        if (set.statusEnd != null)
                            VfxBank.Spawn<VfxOneShot>(set.statusEnd, b.center, VfxBank.VfxFrame(Vector3.right)).FitShapes(b, 0.9f);
                        flash = Mathf.Max(flash, 0.6f);
                        tint = VfxElements.BaseColor(VfxElement.Rime);
                        tintAmt = 1;
                    }
                    frozen = on;
                    break;
                case VfxElement.Spark:
                    shocked = on;
                    break;
            }
            dirty = true;
        }

        /// <summary>Every status off; a frozen enemy shatters. Call before destroying the enemy.</summary>
        public void ClearAll()
        {
            SetStatus(VfxElement.Ember, false);
            SetStatus(VfxElement.Rime, false);
            SetStatus(VfxElement.Spark, false);
        }

        /// <summary>Drops every status without effects and clears the shader values at once (scene resets).</summary>
        public void ResetNow()
        {
            if (burnFx != null && burnFx.live) VfxBank.Release(burnFx);
            if (shell != null && shell.live) VfxBank.Release(shell);
            burnFx = null; shell = null;
            burning = frozen = shocked = false;
            flash = tintAmt = shock = ice = burn = 0;
            dirty = true;
            Step(0);
        }

        internal void Step(float dt)
        {
            flash = Mathf.Max(0, flash - dt / flashTime);
            tintAmt = Mathf.Max(0, tintAmt - dt / tintTime);
            shock = shocked ? 1 : Mathf.Max(0, shock - dt / shockTime);
            ice = Mathf.MoveTowards(ice, frozen ? 1 : 0, dt / (frozen ? 0.15f : 0.06f));
            burn = Mathf.MoveTowards(burn, burning ? 1 : 0, dt / 0.25f);
            bool any = flash > 0 || tintAmt > 0 || shock > 0 || ice > 0 || burn > 0;
            if (!any && !dirty) return;
            dirty = any;

            // pixel-rate steps: the flash is full, then half, then gone; the tint fades in quarters
            float f = flash > 0.5f ? 1 : flash > 0 ? 0.5f : 0;
            var t = tint; t.a = Mathf.Ceil(tintAmt * 4) / 4;
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(FlashId, f);
                mpb.SetColor(TintId, t);
                mpb.SetFloat(IceId, ice);
                mpb.SetFloat(BurnId, burn);
                mpb.SetFloat(ShockId, shock);
                mpb.SetFloat(SeedId, seed);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
