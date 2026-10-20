// One-shot effects: hits, the freeze shatter, the per-enemy row spark. The particle systems carry the look;
// VfxCrackle adds the electric arcs of the Spark hit.
using UnityEngine;

namespace RicochetVfx
{
    public class VfxOneShot : VfxBehaviour
    {
        /// <summary>Sizes every box-shaped emitter to a world bounds (shatter fills the frozen enemy's volume).</summary>
        public void FitShapes(Bounds world, float k = 1f)
        {
            CacheSystems();
            foreach (var ps in systems)
            {
                var sh = ps.shape;
                if (!sh.enabled || sh.shapeType != ParticleSystemShapeType.Box) continue;
                var local = ps.transform.InverseTransformVector(world.size);
                sh.scale = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) * k;
            }
        }
    }
}
