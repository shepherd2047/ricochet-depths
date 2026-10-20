// Gravity for a particle system along the board's down (-VfxBank.Up), set when the effect spawns. Unity's own
// gravity pulls along world -Y, which on pit-unity's board is toward the player, not into the floor.
using UnityEngine;

namespace RicochetVfx
{
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class VfxGravity : MonoBehaviour
    {
        [Tooltip("Cells per second² toward the floor. Negative rises (flames, heat).")]
        public float g = 6f;

        public void Apply(ParticleSystem ps)
        {
            var f = ps.forceOverLifetime;
            f.enabled = true;
            f.space = ParticleSystemSimulationSpace.World;
            var v = -VfxBank.Up * g * VfxBank.Scale;
            f.x = v.x; f.y = v.y; f.z = v.z;
        }
    }
}
