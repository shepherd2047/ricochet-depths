// Effects that stay on an enemy while a status lasts: the burn flames (VfxStatus) and the freeze shell (VfxIceShell).
using System.Collections.Generic;
using UnityEngine;

namespace RicochetVfx
{
    public class VfxStatus : VfxOneShot
    {
        public float linger = 0.8f;

        public override void OnSpawn()
        {
            life = 0;
        }

        /// <summary>Stop emitting, move off the enemy (it may be destroyed) and return to the pool once the
        /// particles are out.</summary>
        public virtual void Stop()
        {
            StopEmitting();
            transform.SetParent(VfxBank.Instance.transform, true);
            life = age + linger;
        }
    }
}
