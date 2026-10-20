// Base for every pooled effect. VfxBank steps all live effects from one clock, so the same code runs in Play mode and
// in the editor capture (manual ticks, where Unity does not update particles or call Update by itself).
using UnityEngine;

namespace RicochetVfx
{
    public abstract class VfxBehaviour : MonoBehaviour
    {
        [Tooltip("Seconds until the effect returns to the pool; 0 = only when released by code.")]
        public float life = 1f;

        [System.NonSerialized] public GameObject prefab;
        [System.NonSerialized] public float age;
        [System.NonSerialized] public bool live;
        internal ParticleSystem[] systems;
        internal bool[] emits;

        internal void CacheSystems()
        {
            if (systems != null) return;
            systems = GetComponentsInChildren<ParticleSystem>(true);
            emits = new bool[systems.Length];
            for (int i = 0; i < systems.Length; i++) emits[i] = systems[i].emission.enabled;
        }

        /// <summary>Emission back to the prefab's setting (pooled instances may have been stopped).</summary>
        internal void ResetEmission()
        {
            for (int i = 0; i < systems.Length; i++) { var em = systems[i].emission; em.enabled = emits[i]; }
        }

        /// <summary>Called by VfxBank right after the instance is placed and activated.</summary>
        public virtual void OnSpawn() { }

        /// <summary>Called by VfxBank right before the instance goes back to the pool.</summary>
        public virtual void OnRelease() { }

        public virtual void Step(float dt)
        {
            if (life > 0 && age >= life) VfxBank.Release(this);
        }

        /// <summary>Stop emitting on every child system; particles already out finish their life. (Disabling the emission
        /// module rather than ps.Stop keeps this working under manual Simulate ticks.)</summary>
        protected void StopEmitting()
        {
            CacheSystems();
            foreach (var ps in systems) { var em = ps.emission; em.enabled = false; }
        }
    }
}
