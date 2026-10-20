// Camera-facing strip mesh through a polyline. Trails, chain bolts, crackle arcs and the row beam are all ribbons drawn
// with RicochetVfx/Ribbon. Vertex colour: r = heat (position on the element ramp), g = per-point random,
// b = extra shimmer (0..1), a = alpha. uv.x runs 0 at the first point to 1 at the last, uv.y 0..1 across.
using System.Collections.Generic;
using UnityEngine;

namespace RicochetVfx
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class VfxRibbon : MonoBehaviour
    {
        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        // Scratch lists callers fill before Build.
        public readonly List<Vector3> points = new List<Vector3>();
        public readonly List<float> widths = new List<float>();
        public readonly List<Color> colors = new List<Color>();

        void Ensure()
        {
            if (mesh != null) return;
            mesh = new Mesh { name = "VfxRibbon" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        public void ClearPoints()
        {
            points.Clear(); widths.Clear(); colors.Clear();
        }

        public void Add(Vector3 p, float width, Color c)
        {
            points.Add(p); widths.Add(width); colors.Add(c);
        }

        public void Hide()
        {
            ClearPoints();
            Ensure();
            mesh.Clear();
        }

        /// <summary>Builds the strip from points / widths / colors (world space).</summary>
        public void Build()
        {
            Ensure();
            int n = points.Count;
            verts.Clear(); cols.Clear(); uvs.Clear(); tris.Clear();
            mesh.Clear();
            if (n < 2) return;
            var cam = VfxBank.CamPos;
            var w2l = transform.worldToLocalMatrix;
            for (int i = 0; i < n; i++)
            {
                var p = points[i];
                var tan = i == 0 ? points[1] - points[0] : i == n - 1 ? points[n - 1] - points[n - 2] : points[i + 1] - points[i - 1];
                var side = Vector3.Cross(tan, cam - p);
                if (side.sqrMagnitude < 1e-10f) side = Vector3.Cross(tan, VfxBank.Up);
                side = side.normalized * (widths[i] * 0.5f);
                verts.Add(w2l.MultiplyPoint3x4(p - side));
                verts.Add(w2l.MultiplyPoint3x4(p + side));
                float u = i / (float)(n - 1);
                uvs.Add(new Vector2(u, 0)); uvs.Add(new Vector2(u, 1));
                cols.Add(colors[i]); cols.Add(colors[i]);
                if (i > 0)
                {
                    int a = 2 * i - 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, true);
        }
    }

    /// <summary>Jagged lines for lightning, and a small deterministic hash so captures repeat exactly.</summary>
    public static class VfxJag
    {
        public static float Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return (x & 0xffffff) / 16777216f;
        }

        public static float Hash(uint a, uint b, uint c = 0)
        {
            return Hash(a * 73856093u ^ b * 19349663u ^ c * 83492791u);
        }

        /// <summary>
        /// Appends a jagged line a→b to <paramref name="r"/>: <paramref name="segs"/> segments, offsets up to
        /// <paramref name="amp"/> across the view (and a little along up), alternating sides and tapering at the ends.
        /// </summary>
        public static void Line(VfxRibbon r, Vector3 a, Vector3 b, int segs, float amp, float width, Color c, uint seed, bool skipFirst = false)
        {
            var d = b - a;
            var side = Vector3.Cross(d, VfxBank.CamPos - (a + b) * 0.5f).normalized;
            var up = VfxBank.Up;
            for (int i = skipFirst ? 1 : 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float taper = Mathf.Sin(t * Mathf.PI);
                float s = (i % 2 == 0 ? 1 : -1) * (0.35f + 0.65f * Hash(seed, (uint)i, 1));
                float u = (Hash(seed, (uint)i, 2) - 0.5f) * 0.6f;
                var p = a + d * t + (side * s + up * u) * amp * taper;
                var cc = c; cc.g = Hash(seed, (uint)i, 3);
                r.Add(p, width * (0.75f + 0.25f * taper), cc);
            }
        }
    }
}
