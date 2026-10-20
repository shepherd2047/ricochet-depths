// The layered hero rig (pit-art ANIM.md "Engine side", method A): per-bone clips baked by Tools/bake_hero_rig.py into
// Resources/Models/<name>_rig.bytes. Each shown frame composes a lower-body clip (Hips and legs) under an upper-body
// clip (Spine and up, its _L / _F / _R aim variants slerped bone by bone), or one full-body clip over both, then runs
// FK over the bones and skins the bind-pose mesh on the CPU into one dynamic mesh. The caller steps it at 10 fps.
// The maths is plain floats (Blender's armature space, z up), converted to the game's frame only when writing the
// mesh: (s x, s y, -z) with s = -1 for heroes, as Models.Model does for the rest mesh.
using System.Collections.Generic;
using UnityEngine;

namespace Pit.Game
{
    public sealed class HeroRig
    {
        /// <summary>A baked clip: per frame, per bone, rotation (w x y z) and location of pose_bone.matrix_basis.</summary>
        public sealed class Clip
        {
            public bool Loop; public int Frames; public float[] Q, L;
            /// <summary>The frame shown t seconds in at fps: loops wrap, one-shots hold their last frame.</summary>
            public int FrameAt(float t, float fps)
            {
                int i = Mathf.FloorToInt(Mathf.Max(0, t) * fps);
                return Loop ? i % Frames : Mathf.Min(i, Frames - 1);
            }
            /// <summary>Seconds until a one-shot reaches its last frame.</summary>
            public float Length(float fps) { return (Frames - 1) / fps; }
        }

        /// <summary>The shared rig of one model: bones, bind pose, skin weights, clips and the rest mesh's topology.</summary>
        public sealed class Data
        {
            public int Bones, Verts; public float Fps;
            public int[] Parent; public bool[] Upper;
            public float[] Rel, InvRest;       // 12 floats per bone: inv(rest parent) * rest (rest for the root), inv(rest)
            public float[] P, N, W; public byte[] B;   // rest positions / normals (3 per vertex), 4 bones + weights per vertex
            public Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
            public List<Vector2> Uv; public List<Color32> Col; public List<int> Tri;
            public float S; public string Name;
            public bool Has(string clip) { return Clips.ContainsKey(clip); }
        }

        /// <summary>Loads Resources/Models/&lt;name&gt;_rig.bytes for a rest mesh of uv.Count vertices; null when absent.</summary>
        public static Data Load(string name, float s, List<Vector2> uv, List<Color32> col, List<int> tri)
        {
            var ta = Resources.Load<TextAsset>("Models/" + name + "_rig");
            if (ta == null) return null;
            var b = ta.bytes;
            if (b.Length < 16 || b[0] != 'R' || b[1] != 'I' || b[2] != 'G' || b[3] != '1') { Debug.LogWarning("HeroRig: " + name + "_rig is not RIG1"); return null; }
            var d = new Data { Name = name, S = s, Uv = uv, Col = col, Tri = tri };
            d.Bones = I(b, 4); d.Verts = I(b, 8); d.Fps = F(b, 12);
            if (d.Verts != uv.Count) { Debug.LogWarning("HeroRig: " + name + "_rig has " + d.Verts + " vertices, the mesh " + uv.Count); return null; }
            int o = 16, nb = d.Bones, nv = d.Verts;
            d.Parent = new int[nb]; d.Upper = new bool[nb];
            var rest = new float[nb * 12];
            for (int i = 0; i < nb; i++)
            {
                o += 1 + b[o];   // name, unused at runtime
                d.Parent[i] = I(b, o); d.Upper[i] = b[o + 4] != 0; o += 5;
                for (int k = 0; k < 12; k++, o += 4) rest[i * 12 + k] = F(b, o);
            }
            d.Rel = new float[nb * 12]; d.InvRest = new float[nb * 12];
            var inv = new float[12];
            for (int i = 0; i < nb; i++)
            {
                Inverse(rest, i * 12, d.InvRest, i * 12);
                if (d.Parent[i] < 0) System.Array.Copy(rest, i * 12, d.Rel, i * 12, 12);
                else Mul(d.InvRest, d.Parent[i] * 12, rest, i * 12, d.Rel, i * 12);
            }
            d.P = new float[nv * 3]; d.N = new float[nv * 3]; d.B = new byte[nv * 4]; d.W = new float[nv * 4];
            for (int j = 0; j < nv; j++)
            {
                for (int k = 0; k < 3; k++) { d.P[j * 3 + k] = F(b, o + k * 4); d.N[j * 3 + k] = F(b, o + 12 + k * 4); }
                o += 24;
                for (int k = 0; k < 4; k++) { d.B[j * 4 + k] = b[o + k]; d.W[j * 4 + k] = b[o + 4 + k] / 255f; }
                o += 8;
            }
            int nc = I(b, o); o += 4;
            for (int c = 0; c < nc; c++)
            {
                string cn = System.Text.Encoding.ASCII.GetString(b, o + 1, b[o]); o += 1 + b[o];
                var clip = new Clip { Loop = b[o] != 0, Frames = I(b, o + 1) };
                o += 5;
                clip.Q = new float[clip.Frames * nb * 4]; clip.L = new float[clip.Frames * nb * 3];
                for (int f = 0; f < clip.Frames; f++)
                    for (int i = 0; i < nb; i++, o += 14)
                    {
                        int qi = (f * nb + i) * 4, li = (f * nb + i) * 3;
                        for (int k = 0; k < 4; k++) clip.Q[qi + k] = System.BitConverter.ToInt16(b, o + k * 2) / 32767f;
                        for (int k = 0; k < 3; k++) clip.L[li + k] = System.BitConverter.ToInt16(b, o + 8 + k * 2) / 4096f;
                    }
                d.Clips[cn] = clip;
            }
            return d;
        }

        // ------------------------------------------------------------ one posed instance (the hero, the hero-pick preview)
        public readonly Data D;
        public readonly Mesh Mesh;
        readonly float[] q, l, world, skin;
        readonly List<Vector3> pos, nor;

        public HeroRig(Data d)
        {
            D = d;
            q = new float[d.Bones * 4]; l = new float[d.Bones * 3];
            world = new float[d.Bones * 12]; skin = new float[d.Bones * 12];
            pos = new List<Vector3>(d.Verts); nor = new List<Vector3>(d.Verts);
            for (int i = 0; i < d.Verts; i++) { pos.Add(Vector3.zero); nor.Add(Vector3.zero); }
            Mesh = new Mesh { name = d.Name + "_rig" };
            Mesh.MarkDynamic();
            for (int i = 0; i < d.Bones; i++) q[i * 4] = 1;
            Write(true);
        }

        public enum Part { Lower, Upper, All }

        /// <summary>Poses the part's bones with clip a at ta, slerped toward clip b at tb by w (b may be null for w = 0).</summary>
        public void Set(Part part, string a, float ta, string b, float tb, float w)
        {
            Clip ca, cb = null;
            if (!D.Clips.TryGetValue(a, out ca)) return;
            if (w > 0 && (b == null || !D.Clips.TryGetValue(b, out cb))) w = 0;
            int fa = ca.FrameAt(ta, D.Fps), fb = cb != null ? cb.FrameAt(tb, D.Fps) : 0, nb = D.Bones;
            for (int i = 0; i < nb; i++)
            {
                if (part == Part.Lower && D.Upper[i] || part == Part.Upper && !D.Upper[i]) continue;
                int qa = (fa * nb + i) * 4, la = (fa * nb + i) * 3;
                if (w <= 0)
                {
                    for (int k = 0; k < 4; k++) q[i * 4 + k] = ca.Q[qa + k];
                    for (int k = 0; k < 3; k++) l[i * 3 + k] = ca.L[la + k];
                    continue;
                }
                int qb = (fb * nb + i) * 4, lb = (fb * nb + i) * 3;
                Slerp(ca.Q, qa, cb.Q, qb, w, q, i * 4);
                for (int k = 0; k < 3; k++) l[i * 3 + k] = ca.L[la + k] + (cb.L[lb + k] - ca.L[la + k]) * w;
            }
        }

        /// <summary>The upper body: base + "_L" / "_F" / "_R" at t, aim -1 (the character's left) .. +1 (its right).</summary>
        public void Aim(string clipBase, float t, float aim)
        {
            aim = Mathf.Clamp(aim, -1, 1);
            Set(Part.Upper, clipBase + "_F", t, clipBase + (aim < 0 ? "_L" : "_R"), t, Mathf.Abs(aim));
        }

        /// <summary>FK over the bones, then linear-blend skinning of the bind pose into Mesh.</summary>
        public void Skin() { Write(false); }

        void Write(bool first)
        {
            int nb = D.Bones, nv = D.Verts;
            var basis = new float[12];
            for (int i = 0; i < nb; i++)
            {
                Basis(q, i * 4, l, i * 3, basis);
                var rel = new float[12];
                Mul(D.Rel, i * 12, basis, 0, rel, 0);
                if (D.Parent[i] < 0) System.Array.Copy(rel, 0, world, i * 12, 12);
                else Mul(world, D.Parent[i] * 12, rel, 0, world, i * 12);
                Mul(world, i * 12, D.InvRest, i * 12, skin, i * 12);
            }
            float s = D.S;
            for (int j = 0; j < nv; j++)
            {
                float px = D.P[j * 3], py = D.P[j * 3 + 1], pz = D.P[j * 3 + 2];
                float nx = D.N[j * 3], ny = D.N[j * 3 + 1], nz = D.N[j * 3 + 2];
                float ox = 0, oy = 0, oz = 0, mx = 0, my = 0, mz = 0;
                for (int k = 0; k < 4; k++)
                {
                    float w = D.W[j * 4 + k];
                    if (w <= 0) continue;
                    int m = D.B[j * 4 + k] * 12;
                    ox += w * (skin[m] * px + skin[m + 1] * py + skin[m + 2] * pz + skin[m + 3]);
                    oy += w * (skin[m + 4] * px + skin[m + 5] * py + skin[m + 6] * pz + skin[m + 7]);
                    oz += w * (skin[m + 8] * px + skin[m + 9] * py + skin[m + 10] * pz + skin[m + 11]);
                    mx += w * (skin[m] * nx + skin[m + 1] * ny + skin[m + 2] * nz);
                    my += w * (skin[m + 4] * nx + skin[m + 5] * ny + skin[m + 6] * nz);
                    mz += w * (skin[m + 8] * nx + skin[m + 9] * ny + skin[m + 10] * nz);
                }
                float nl = Mathf.Sqrt(mx * mx + my * my + mz * mz);
                if (nl > 1e-6f) { mx /= nl; my /= nl; mz /= nl; }
                pos[j] = new Vector3(s * ox, s * oy, -oz);
                nor[j] = new Vector3(s * mx, s * my, -mz);
            }
            Mesh.SetVertices(pos);
            Mesh.SetNormals(nor);
            if (first) { Mesh.SetColors(D.Col); Mesh.SetUVs(0, D.Uv); Mesh.SetTriangles(D.Tri, 0, true); }
        }

        // ------------------------------------------------------------ 3x4 affine maths (rows 0-2 of a 4x4, row-major)
        static void Mul(float[] a, int ao, float[] b, int bo, float[] o, int oo)
        {
            var r = new float[12];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 4; j++)
                    r[i * 4 + j] = a[ao + i * 4] * b[bo + j] + a[ao + i * 4 + 1] * b[bo + 4 + j] + a[ao + i * 4 + 2] * b[bo + 8 + j];
                r[i * 4 + 3] += a[ao + i * 4 + 3];
            }
            System.Array.Copy(r, 0, o, oo, 12);
        }

        static void Inverse(float[] m, int mo, float[] o, int oo)
        {
            float a = m[mo], b = m[mo + 1], c = m[mo + 2], d = m[mo + 4], e = m[mo + 5], f = m[mo + 6], g = m[mo + 8], h = m[mo + 9], k = m[mo + 10];
            float A = e * k - f * h, B = -(d * k - f * g), C = d * h - e * g;
            float det = a * A + b * B + c * C, id = 1 / det;
            var r = new float[12];
            r[0] = A * id; r[1] = -(b * k - c * h) * id; r[2] = (b * f - c * e) * id;
            r[4] = B * id; r[5] = (a * k - c * g) * id; r[6] = -(a * f - c * d) * id;
            r[8] = C * id; r[9] = -(a * h - b * g) * id; r[10] = (a * e - b * d) * id;
            for (int i = 0; i < 3; i++) r[i * 4 + 3] = -(r[i * 4] * m[mo + 3] + r[i * 4 + 1] * m[mo + 7] + r[i * 4 + 2] * m[mo + 11]);
            System.Array.Copy(r, 0, o, oo, 12);
        }

        /// <summary>T(loc) * R(quat) as a 3x4.</summary>
        static void Basis(float[] q, int qo, float[] l, int lo, float[] o)
        {
            float w = q[qo], x = q[qo + 1], y = q[qo + 2], z = q[qo + 3];
            float n = Mathf.Sqrt(w * w + x * x + y * y + z * z);
            if (n > 1e-6f) { w /= n; x /= n; y /= n; z /= n; }
            o[0] = 1 - 2 * (y * y + z * z); o[1] = 2 * (x * y - w * z); o[2] = 2 * (x * z + w * y); o[3] = l[lo];
            o[4] = 2 * (x * y + w * z); o[5] = 1 - 2 * (x * x + z * z); o[6] = 2 * (y * z - w * x); o[7] = l[lo + 1];
            o[8] = 2 * (x * z - w * y); o[9] = 2 * (y * z + w * x); o[10] = 1 - 2 * (x * x + y * y); o[11] = l[lo + 2];
        }

        static void Slerp(float[] a, int ao, float[] b, int bo, float t, float[] o, int oo)
        {
            float aw = a[ao], ax = a[ao + 1], ay = a[ao + 2], az = a[ao + 3];
            float bw = b[bo], bx = b[bo + 1], by = b[bo + 2], bz = b[bo + 3];
            float dot = aw * bw + ax * bx + ay * by + az * bz;
            if (dot < 0) { dot = -dot; bw = -bw; bx = -bx; by = -by; bz = -bz; }
            float ka, kb;
            if (dot > 0.9995f) { ka = 1 - t; kb = t; }
            else
            {
                float th = (float)System.Math.Acos(dot), sn = Mathf.Sin(th);
                ka = Mathf.Sin((1 - t) * th) / sn; kb = Mathf.Sin(t * th) / sn;
            }
            o[oo] = ka * aw + kb * bw; o[oo + 1] = ka * ax + kb * bx; o[oo + 2] = ka * ay + kb * by; o[oo + 3] = ka * az + kb * bz;
        }

        static int I(byte[] b, int o) { return System.BitConverter.ToInt32(b, o); }
        static float F(byte[] b, int o) { return System.BitConverter.ToSingle(b, o); }
    }
}
