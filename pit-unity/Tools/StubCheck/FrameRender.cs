// Software render of one game frame from the stub's data: the 3D board (Board3D.DebugVisit) through the fitted, pitched
// camera with the key light, trilight and fog; then the World/Glow overlay laid on the board plane, textured from the
// pixel atlas; then the HUD/menu shapes on top. TextMesh labels are not drawn. This previews composition and colour
// only; it is not URP (no shadows, no bloom).
using System;
using System.Collections.Generic;
using Pit.Core;
using Pit.Game;
using UnityEngine;

static class FrameRender
{
    public static void Render(string path, Board3D b, Gfx g, Run r, int scale)
    {
        int W = (int)(g.W * scale), H = (int)(g.H * scale);
        var img = new float[W * H * 3]; var zb = new float[W * H];
        for (int i = 0; i < W * H; i++) { zb[i] = 1e9f; img[i * 3] = 38 / 255f; img[i * 3 + 1] = 41 / 255f; img[i * 3 + 2] = 55 / 255f; }
        var cp = b.CamPos; var f = b.CamFwd; var u = b.CamUp; var rt = Vector3.right;
        float tanV = b.TanV, tanH = tanV * g.W / g.H;
        Func<Vector3, Vector3> proj = p =>
        {
            var v = p - cp; float z = Math.Max(0.05f, Vector3.Dot(v, f));
            return new Vector3((Vector3.Dot(v, rt) / (z * tanH) + 1) / 2 * W, (1 - Vector3.Dot(v, u) / (z * tanV)) / 2 * H, z);
        };
        var toLight = new Vector3(0, -0.1736f, -0.9848f);
        float[] sky = { 54 / 255f, 58 / 255f, 66 / 255f }, eq = { 29 / 255f, 32 / 255f, 34 / 255f }, gr = { 12 / 255f, 11 / 255f, 9 / 255f }, fog = { 38 / 255f, 41 / 255f, 55 / 255f };

        b.DebugVisit((mesh, pos, scl, rotz) =>
        {
            if (mesh == null) return;
            float a = rotz * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            Func<Vector3, Vector3> rz = v => new Vector3(v.x * ca - v.y * sa, v.x * sa + v.y * ca, v.z);
            for (int t = 0; t < mesh.T.Count; t += 3)
            {
                var wv = new Vector3[3]; var sv = new Vector3[3];
                for (int k = 0; k < 3; k++) { wv[k] = pos + rz(Vector3.Scale(mesh.V[mesh.T[t + k]], scl)); sv[k] = proj(wv[k]); }
                var n0 = mesh.N[mesh.T[t]];
                var n = rz(new Vector3(n0.x / scl.x, n0.y / scl.y, n0.z / scl.z)).normalized;
                if (Vector3.Dot(n, (wv[0] + wv[1] + wv[2]) / 3 - cp) >= 0) continue;
                var c = mesh.C[mesh.T[t]]; float em = mesh.U[mesh.T[t]].x;
                float ndl = Math.Max(0, Vector3.Dot(n, toLight)), up = -n.z;
                float dist = ((wv[0] + wv[1] + wv[2]) / 3 - cp).magnitude, fk = Mathf.Clamp01((dist - 30) / 20);
                var rgb = new float[3];
                for (int k = 0; k < 3; k++)
                {
                    float amb = up >= 0 ? eq[k] + (sky[k] - eq[k]) * up : eq[k] + (gr[k] - eq[k]) * -up;
                    float alb = (k == 0 ? c.r : k == 1 ? c.g : c.b) / 255f;
                    float v = alb * (amb + ndl) + alb * em * 3;
                    rgb[k] = v * (1 - fk) + fog[k] * fk;
                }
                Raster(img, zb, W, H, sv, null, rgb, 1, true, null);
            }
        }, r);

        var atlas = Texture2D.LastPixels; int aw = Texture2D.LastW, ah = Texture2D.LastH;
        var wr = b.WorldRoot; var wp = wr.localPosition; var ws = wr.localScale;
        foreach (int pass in new[] { 0, 6, 2, 4 })
            g.DebugBatches((layer, V, U, C, I) =>
            {
                if (layer != pass) return;
                bool world = layer == 0 || layer == 6;
                float boost = layer == 6 ? Gfx.GlowBoost : 1;
                for (int t = 0; t + 2 < I.Count; t += 3)
                {
                    var sv = new Vector3[3]; var uv = new Vector2[3];
                    for (int k = 0; k < 3; k++)
                    {
                        var v = V[I[t + k]];
                        sv[k] = world ? proj(new Vector3(v.x * ws.x + wp.x, v.y * ws.y + wp.y, v.z)) : new Vector3(v.x * scale, -v.y * scale, 0);
                        uv[k] = U[I[t + k]];
                    }
                    var c = C[I[t]];
                    Raster(img, zb, W, H, sv, uv, new[] { c.r / 255f * boost, c.g / 255f * boost, c.b / 255f * boost }, c.a / 255f, world, (x, y) =>
                    {
                        int px = Math.Min(aw - 1, Math.Max(0, (int)(x * aw))), py = Math.Min(ah - 1, Math.Max(0, (int)(y * ah)));
                        return atlas[py * aw + px];
                    });
                }
            });

        using var fs = System.IO.File.Create(path);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6 {W} {H} 255\n");
        fs.Write(head, 0, head.Length);
        foreach (var v in img) fs.WriteByte((byte)(Math.Min(1, Math.Max(0, v)) * 255));
    }

    static void Raster(float[] img, float[] zb, int W, int H, Vector3[] s, Vector2[] uv, float[] rgb, float alpha, bool ztest,
        Func<float, float, Color32> tex)
    {
        float x0 = s[0].x, y0 = s[0].y, x1 = s[1].x, y1 = s[1].y, x2 = s[2].x, y2 = s[2].y;
        float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
        if (Math.Abs(den) < 1e-6f) return;
        int minx = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), maxx = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int miny = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), maxy = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
        bool opaque = uv == null;
        for (int y = miny; y <= maxy; y++)
            for (int x = minx; x <= maxx; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den, w1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den, w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                int i = y * W + x;
                float z = w0 * s[0].z + w1 * s[1].z + w2 * s[2].z;
                if (ztest && z > zb[i] + 1e-3f) continue;
                if (opaque) { zb[i] = z; img[i * 3] = rgb[0]; img[i * 3 + 1] = rgb[1]; img[i * 3 + 2] = rgb[2]; continue; }
                var tc = tex(w0 * uv[0].x + w1 * uv[1].x + w2 * uv[2].x, w0 * uv[0].y + w1 * uv[1].y + w2 * uv[2].y);
                float a = alpha * tc.a / 255f;
                if (a <= 0) continue;
                img[i * 3] += (rgb[0] * tc.r / 255f - img[i * 3]) * a;
                img[i * 3 + 1] += (rgb[1] * tc.g / 255f - img[i * 3 + 1]) * a;
                img[i * 3 + 2] += (rgb[2] * tc.b / 255f - img[i * 3 + 2]) * a;
            }
    }
}
