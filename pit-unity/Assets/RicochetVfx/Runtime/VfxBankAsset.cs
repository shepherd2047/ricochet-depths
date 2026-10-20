// Prefab table for VfxBank. The asset lives at Resources/RicochetVfx/VfxBank so VfxBank can find it on its own.
using System;
using UnityEngine;

namespace RicochetVfx
{
    [CreateAssetMenu(menuName = "Ricochet VFX/Bank", fileName = "VfxBank")]
    public sealed class VfxBankAsset : ScriptableObject
    {
        [Serializable]
        public sealed class ElementSet
        {
            public VfxElement element;
            public GameObject ball;     // VfxBall: 16×16 sprite, halo, trail
            public GameObject hit;      // VfxOneShot: hit effect at the contact point
            public GameObject status;   // Ember: VfxStatus burn; Rime: VfxIceShell; others none
            public GameObject statusEnd; // Rime: shatter when the freeze breaks
        }

        public ElementSet[] elements = new ElementSet[VfxElements.Count];
        public GameObject chainBolt;    // VfxBolt
        public GameObject rowBeam;      // VfxRowBeam
        public GameObject rowHit;       // VfxOneShot: the spark on each enemy the row beam crosses

        [Tooltip("World size of the 16×16 ball sprite (16 px per unit = 1).")]
        public float ballSize = 0.5f;

        public ElementSet Get(VfxElement e)
        {
            return elements[(int)e];
        }
    }
}
