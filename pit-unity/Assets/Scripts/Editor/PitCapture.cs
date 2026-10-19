// Command line (needs graphics, so no -nographics):
//   Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitCapture.Run -pitShot out.png [-pitTurn 12] [-pitHero tinker]
//     [-pitScreen hud|title|heroes|levelup|crucible|pause|end|clips] [-pitW 1080 -pitH 1920] [-pitFinal] [-pitRec 4]
// -pitScreen clips renders every baked frame of the hero's clips (Models.Clip) as a sheet, one clip per row.
// -pitRec s plays on for s more seconds after the shot and writes a 10 fps frame sequence next to it (out_000.png ...).
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

        static void Shot(Camera cam, int W, int H, string path)
        {
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }

        /// <summary>Every baked frame of the hero's clips, seen from the gameplay pitch, one row per clip (256 px tiles).</summary>
        static void ClipSheet(string path, string heroId, Transform root)
        {
            const int T = 256;
            string mk;
            var a = Board3D.HeroModels.TryGetValue(heroId, out mk) ? Models.Model(mk, true, false, Resources.Load<Shader>("PitUnit")) : null;
            if (a == null || a.Clips == null) throw new Exception("no baked clips for " + heroId);
            Shader.SetGlobalColor("_PitSky", new Color32(54, 58, 66, 255));
            Shader.SetGlobalColor("_PitEquator", new Color32(29, 32, 34, 255));
            Shader.SetGlobalColor("_PitGround", new Color32(12, 11, 9, 255));
            Shader.SetGlobalFloat("_PitLineY", 100);
            var go = new GameObject("clip"); go.transform.SetParent(root, false);
            var mf = go.AddComponent<MeshFilter>(); var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = a.Mat;
            var lg = new GameObject("key").AddComponent<Light>(); lg.transform.SetParent(root, false);
            lg.type = LightType.Directional; lg.transform.localRotation = Quaternion.Euler(10, 0, 0);
            var cam = new GameObject("cam").AddComponent<Camera>(); cam.transform.SetParent(root, false);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(38, 41, 55, 255);
            cam.fieldOfView = 30; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50;
            // the gameplay view: 40° from straight down, from behind (models face +y once faceAway turns them)
            float th = 40 * Mathf.Deg2Rad;
            var fwd = new Vector3(0, Mathf.Sin(th), Mathf.Cos(th));
            cam.transform.localPosition = new Vector3(0, 0, -a.Half.z * 0.5f) - fwd * 3.2f;
            cam.transform.localRotation = Quaternion.LookRotation(fwd, new Vector3(0, Mathf.Cos(th), -Mathf.Sin(th)));
            string[] order = { "Idle", "Walk", "Shoot", "Hit", "Death", "Victory" };
            int cols = 0; foreach (var c in a.Clips.Values) cols = Mathf.Max(cols, c.Frames.Length);
            var sheet = new Texture2D(cols * T, order.Length * T, TextureFormat.RGB24, false);
            var px = new Color32[cols * T * order.Length * T];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(38, 41, 55, 255);
            sheet.SetPixels32(px);
            var rt = new RenderTexture(T, T, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var tile = new Texture2D(T, T, TextureFormat.RGB24, false);
            for (int row = 0; row < order.Length; row++)
            {
                Models.Clip c;
                if (!a.Clips.TryGetValue(order[row], out c)) continue;
                for (int f = 0; f < c.Frames.Length; f++)
                {
                    mf.sharedMesh = c.Frames[f];
                    cam.Render();
                    RenderTexture.active = rt;
                    tile.ReadPixels(new Rect(0, 0, T, T), 0, 0);
                    sheet.SetPixels(f * T, (order.Length - 1 - row) * T, T, T, tile.GetPixels());
                }
                Debug.Log("PitCapture: clip " + order[row] + " " + c.Frames.Length + " frames, loop " + c.Loop);
            }
            RenderTexture.active = null;
            cam.targetTexture = null;
            sheet.Apply();
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log("PitCapture: " + path);
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
            if (which == "clips")
            {
                try { ClipSheet(path, heroId, root.transform); }
                catch (Exception ex) { code = 1; Debug.LogError("PitCapture: " + ex); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
                if (Application.isBatchMode) EditorApplication.Exit(code);
                return;
            }
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

                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
                Shot(board.Cam, W, H, path);
                float rec = float.Parse(Arg("-pitRec", "0"), System.Globalization.CultureInfo.InvariantCulture);
                string stem = System.IO.Path.ChangeExtension(path, null);
                for (int n = 0; n < rec * 10; n++)
                {
                    for (int k = 0; k < 6 && !r.IsOver; k++)
                    {
                        if (final) r.P.Hp = r.P.MaxHp;
                        if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) Bot.AutoPick(r);
                        else { Bot.Drive(r); r.Step(1 / 60f); }
                    }
                    gfx.Time = r.Time;
                    gfx.Begin(w, h, scale);
                    ui.BeginFrame();
                    world.Layout(r, screens.HudTop, screens.HudBottom);
                    board.Frame(r, world, gfx);
                    world.Draw(r);
                    bool p = false;
                    screens.Hud(r, world, ref p);
                    ui.EndFrame();
                    gfx.End();
                    Shot(board.Cam, W, H, stem + "_" + n.ToString("000") + ".png");
                }
                Debug.Log("PitCapture: " + path + " hero " + heroId + " turn " + r.Turn + " board " + r.Cols + "x" + r.Rows +
                          " foes " + r.Enemies.Count + " distance " + board.Distance.ToString("0.00") + " scroll " + r.Scroll.ToString("0.0"));
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
