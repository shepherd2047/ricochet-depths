// Renders the preview clips frame by frame with fixed 1/30 s ticks (VfxBank.manualTick), so captures are exact and
// need no Play mode. Needs graphics (no -nographics):
//   Unity -batchmode -quit -projectPath pit-vfx/VfxLab -executeMethod VfxLabCapture.Run -vfxOut <dir> [-vfxSeconds 6] [-vfxW 540 -vfxH 960]
// Writes <dir>/<element>/f_000.png ... ; tools/make_previews.sh turns them into GIF/MP4.
using System;
using System.IO;
using RicochetVfx;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VfxLabCapture
{
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
        return def;
    }

    public static void Run()
    {
        int code = 0;
        try
        {
            string outDir = Arg("-vfxOut", "Captures");
            float seconds = float.Parse(Arg("-vfxSeconds", "6"), System.Globalization.CultureInfo.InvariantCulture);
            int W = int.Parse(Arg("-vfxW", "540")), H = int.Parse(Arg("-vfxH", "960"));
            string only = Arg("-vfxOnly", "");
            EditorSceneManager.OpenScene("Assets/VfxLabPreview/VfxLab.unity");
            var bank = VfxBank.Ensure();
            bank.manualTick = true;
            var demo = UnityEngine.Object.FindAnyObjectByType<VfxLabDemo>();
            var cam = bank.viewCamera;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            const float dt = 1f / 30f;
            for (int e = 0; e < VfxElements.Count; e++)
            {
                var el = (VfxElement)e;
                if (only != "" && only != el.ToString()) continue;
                string dir = Path.Combine(outDir, el.ToString());
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "f_*.png")) File.Delete(f);
                UnityEngine.Random.InitState(1234 + e);
                demo.Restart(el);
                int frames = Mathf.RoundToInt(seconds / dt);
                for (int f = 0; f < frames; f++)
                {
                    demo.Step(dt);
                    bank.Tick(dt);
                    if (f.ToString() == Arg("-vfxDump", ""))
                        foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
                            if (r.enabled) Debug.Log("DUMP " + r.name + " <" + (r.transform.parent ? r.transform.parent.name : "") + "> " + r.bounds + " ls=" + r.transform.lossyScale);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(dir, string.Format("f_{0:000}.png", f)), tex.EncodeToPNG());
                }
                Debug.Log("VfxLab: captured " + frames + " frames of " + el);
            }
            rt.Release();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            code = 1;
        }
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
