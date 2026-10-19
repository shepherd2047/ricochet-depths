// Command line (needs graphics, so no -nographics):
//   Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitCapture.Run -pitShot out.png [-pitTurn 12] [-pitHero tinker]
//     [-pitScreen hud|title|heroes|levelup|crucible|pause|end] [-pitW 1080 -pitH 1920] [-pitFinal]
// -pitFinal keeps the hero alive and stops once the final boss has been on the board for -pitAfter seconds (default 0),
// i.e. with the gate docked.
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
            string which = Arg("-pitScreen", "hud");
            int W = int.Parse(Arg("-pitW", "1080")), H = int.Parse(Arg("-pitH", "1920"));
            // the game's integer UI scale (GameRoot): s screen px per art px
            float scale = Mathf.Max(1, Mathf.Min(W / 180, H / 300)) / 2f;
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
                var screens = new Screens(gfx, ui) { SafeTop = 44, SafeBottom = 34, Board = board, SelHero = heroId };
                float w = W / scale, h = H / scale;
                uiCam.orthographicSize = h / 2;
                uiCam.aspect = (float)W / H;   // batch mode has no game view, so the overlay would keep a default aspect
                board.Cam.aspect = (float)W / H;
                uiCam.transform.localPosition = new Vector3(w / 2, -h / 2, -10);

                var r = new Pit.Core.Run(1234, heroId);
                bool final = Arg("-pitFinal", null) != null;
                float after = float.Parse(Arg("-pitAfter", "0"), System.Globalization.CultureInfo.InvariantCulture), finalAt = 0;
                while (!r.IsOver && r.Time < 3000)
                {
                    if (final)
                    {
                        r.P.Hp = r.P.MaxHp;
                        if (r.Boss != null && r.Boss.BossDef.Final && r.Mode == Mode.Playing && r.Balls.Count > 0 && (finalAt += 1 / 60f) > after) break;
                        if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) { Bot.AutoPick(r); continue; }
                        Bot.Drive(r); r.Step(1 / 60f); continue;
                    }
                    bool ready = r.Turn >= turn && r.Balls.Count > 0;
                    if (which == "levelup" && ready && r.Mode == Mode.LevelUp) break;
                    if (which == "crucible" && ready && r.Mode == Mode.Fuser) break;
                    if (which != "end" && which != "levelup" && which != "crucible" && ready && r.Mode == Mode.Playing) break;
                    if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) { Bot.AutoPick(r); continue; }
                    Bot.Drive(r);
                    r.Step(1 / 60f);
                }
                if (which == "pause") r.Pause();
                // two frames, so eased bars and the lag bar settle and the preview camera has its target
                for (int f = 0; f < 2; f++)
                {
                    gfx.Time = r.Time + f * 0.5f;
                    gfx.Begin(w, h, scale);
                    ui.BeginFrame();
                    if (which == "title") { board.Idle(gfx); screens.Title(gfx.Time); }
                    else if (which == "heroes") { board.Idle(gfx); var nav = Nav.None; screens.HeroSelect(ref nav); }
                    else
                    {
                        world.Layout(r, screens.HudTop, screens.HudBottom);
                        board.Frame(r, world, gfx);
                        world.Draw(r);
                        bool p = false;
                        screens.Hud(r, world, ref p);
                        screens.Menus(r);
                    }
                    ui.EndFrame();
                    gfx.End();
                }
                board.RenderPreview();

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
                          " foes " + r.Enemies.Count + " distance " + board.Distance.ToString("0.00") + " farY " + r.FarY.ToString("0.00") + " scroll " + r.Scroll.ToString("0.0"));
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
