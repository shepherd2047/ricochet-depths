// Command line: Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitSmoke.Run
// The real-Editor counterpart of Tools/StubCheck: bot runs pushed through the real view code (Board3D, WorldView,
// Screens, Gfx, Ui, AudioBank) on real UnityEngine types, plus checks that the shaders and audio clips resolve.
// Exits 1 on any exception or missing asset.
using System;
using Pit.Core;
using Pit.Game;
using UnityEditor;
using UnityEngine;

namespace Pit.EditorTools
{
    public static class PitSmoke
    {
        public static void Run()
        {
            int errors = 0, frames = 0;
            foreach (var sh in new[] { "PitShape", "PitUnit" })
                if (Resources.Load<Shader>(sh) == null) { Debug.LogError("PitSmoke: shader not found: " + sh); errors++; }
            foreach (var clip in new[] { "Audio/sfx/ball_hit_1", "Audio/sfx/ui_click_1", "Audio/music/mus_battle_loop" })
                if (Resources.Load<AudioClip>(clip) == null) { Debug.LogError("PitSmoke: clip not found: " + clip); errors++; }

            var root = new GameObject("PitSmoke");
            try
            {
                var uiCam = new GameObject("ui-cam").AddComponent<Camera>();
                uiCam.transform.SetParent(root.transform, false);
                var board = new Board3D(root.transform, uiCam);
                var layers = new GameObject("layers"); layers.transform.SetParent(root.transform, false);
                var gfx = new Gfx(layers.transform, board.WorldRoot);
                var ui = new Ui(gfx);
                var world = new WorldView(gfx) { Units3D = true };
                var screens = new Screens(gfx, ui) { SafeTop = 20, SafeBottom = 10 };
                var audio = new AudioBank(root);
                audio.Unlock();
                Action<Action> frame = draw =>
                {
                    gfx.Begin(360, 740, 3);
                    ui.BeginFrame();
                    draw();
                    ui.EndFrame();
                    gfx.End();
                    frames++;
                };
                frame(() => { board.Idle(gfx); screens.Title(1.5f); });
                frame(() => { var nav = Nav.None; screens.HeroSelect(ref nav); });
                float t = 0;
                foreach (var h in Content.Heroes)
                {
                    var r = new Pit.Core.Run(h.Id.GetHashCode() & 0xffff, h.Id);
                    try
                    {
                        int step = 0;
                        while (!r.IsOver && r.Time < 1500)
                        {
                            if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser)
                            {
                                frame(() => screens.Menus(r));
                                Bot.AutoPick(r);
                                continue;
                            }
                            if (step % 6 == 0)
                                frame(() =>
                                {
                                    world.Layout(r, screens.HudTop, screens.HudBottom);
                                    board.Frame(r, world, gfx);
                                    world.Draw(r);
                                    bool p = false;
                                    screens.Hud(r, world, ref p);
                                    screens.Menus(r);
                                });
                            Bot.Drive(r);
                            r.Step(1 / 60f);
                            t += 1 / 60f;
                            audio.Drain(r, t);
                            step++;
                        }
                        frame(() => { world.Layout(r, screens.HudTop, screens.HudBottom); world.Draw(r); bool p = false; screens.Hud(r, world, ref p); screens.Menus(r); });
                        Debug.Log("PitSmoke: " + h.Id + " " + r.Mode + " turn " + r.Turn + ", frames so far " + frames);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Debug.LogError("PitSmoke: " + h.Id + " " + ex);
                    }
                }
                Debug.Log("PitSmoke: camera distance on the last board " + board.Distance.ToString("0.00"));
            }
            catch (Exception ex)
            {
                errors++;
                Debug.LogError("PitSmoke: setup " + ex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
            Debug.Log("PitSmoke: frames " + frames + ", errors " + errors);
            if (Application.isBatchMode) EditorApplication.Exit(errors == 0 ? 0 : 1);
        }
    }
}
