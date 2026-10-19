// Command line (needs graphics, so no -nographics):
//   Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitCapture.Run -pitShot out.png [-pitTurn 12] [-pitHero tinker]
// Plays a bot run to the given turn through the real view code and renders one 1080x1920 gameplay frame (3D pit plus
// the UI overlay camera) to a PNG.
using System;
using Pit.Core;
using Pit.Game;
using UnityEditor;
using UnityEngine;

namespace Pit.EditorTools
{
    public static class PitCapture
    {
        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            string path = Arg("-pitShot", "Builds/shot.png"), heroId = Arg("-pitHero", "tinker");
            int turn = int.Parse(Arg("-pitTurn", "10"));
            const int W = 1080, H = 1920;
            const float scale = W / 360f;
            int code = 0;
            var root = new GameObject("PitCapture");
            try
            {
                var uiCam = new GameObject("ui-cam").AddComponent<Camera>();
                uiCam.transform.SetParent(root.transform, false);
                uiCam.orthographic = true;
                uiCam.nearClipPlane = -100; uiCam.farClipPlane = 100;
                var board = new Board3D(root.transform, uiCam);
                var layers = new GameObject("layers"); layers.transform.SetParent(root.transform, false);
                var gfx = new Gfx(layers.transform, board.WorldRoot);
                var ui = new Ui(gfx);
                var world = new WorldView(gfx) { Units3D = true };
                var screens = new Screens(gfx, ui) { SafeTop = 20, SafeBottom = 10 };
                float w = W / scale, h = H / scale;
                uiCam.orthographicSize = h / 2;
                uiCam.transform.localPosition = new Vector3(w / 2, -h / 2, -10);

                var r = new Pit.Core.Run(1234, heroId);
                while (!r.IsOver && r.Time < 3000 && !(r.Turn >= turn && r.Mode == Mode.Playing && r.Balls.Count > 0))
                {
                    if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) { Bot.AutoPick(r); continue; }
                    Bot.Drive(r);
                    r.Step(1 / 60f);
                }
                gfx.Time = r.Time;
                gfx.Begin(w, h, scale);
                ui.BeginFrame();
                world.Layout(r, screens.HudTop, screens.HudBottom);
                board.Frame(r, world, gfx);
                world.Draw(r);
                bool p = false;
                screens.Hud(r, world, ref p);
                screens.Menus(r);
                ui.EndFrame();
                gfx.End();

                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                board.Cam.targetTexture = rt;
                board.Cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                board.Cam.targetTexture = null;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("PitCapture: " + path + " hero " + heroId + " turn " + r.Turn + " board " + r.Cols + "x" + r.Rows +
                          " foes " + r.Enemies.Count + " distance " + board.Distance.ToString("0.00"));
            }
            catch (Exception ex)
            {
                code = 1;
                Debug.LogError("PitCapture: " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
