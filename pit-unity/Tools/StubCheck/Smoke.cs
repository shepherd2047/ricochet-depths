// Drives full bot runs through the real view code (WorldView, Board3D, Screens, Gfx, Ui) on top of the stub, to catch
// exceptions and malformed meshes in the drawing paths, and checks the low-poly models against the style budgets.
// Odd seeds use the 3D board, even seeds the flat 2D path. Usage: dotnet run -c Release -- [seeds]
using System;
using Pit.Core;
using Pit.Game;
using UnityEngine;

static class Smoke
{
    static int Main(string[] args)
    {
        if (args.Length > 2 && args[0] == "models") { RenderModels(args[1], float.Parse(args[2])); return 0; }
        if (args.Length > 1 && args[0] == "atlas") { PixelArt.Build(); DumpAtlas(args[1]); return 0; }
        int seeds; if (args.Length == 0 || !int.TryParse(args[0], out seeds)) seeds = 2;
        var uiCam = new GameObject("ui-cam").AddComponent<Camera>();
        var board = new Board3D(new GameObject("pit").transform, uiCam);
        var gfx = new Gfx(new GameObject("root").transform, board.WorldRoot);
        var ui = new Ui(gfx);
        var world = new WorldView(gfx);
        var ads = new PlaceholderAds(AdConfig.Default);
        var screens = new Screens(gfx, ui) { SafeTop = 20, SafeBottom = 10, Board = board, Ads = ads };
        int frames = 0, errors = 0, revives = 0;
        Action<Action> frame = draw =>
        {
            gfx.Begin(360, 740, 3);
            ui.BeginFrame();
            draw();
            ui.EndFrame();
            gfx.End();
            frames++;
        };
        if (args.Length > 2 && args[0] == "frame")
        {
            // play one bot run up to the given turn, then software-render that frame (and one more after the next expansion)
            world.Units3D = true;
            var fr = new Run(38, Content.Heroes[0].Id);
            int want = int.Parse(args[2]), shots = 0; float lastD = 0;
            Action<string> shot = tag =>
            {
                frame(() => { world.Layout(fr, screens.HudTop, screens.HudBottom); board.Frame(fr, world, gfx); world.Draw(fr); bool p = false; screens.Hud(fr, world, ref p); screens.Menus(fr); });
                FrameRender.Render(args[1].Replace(".ppm", "-" + tag + ".ppm"), board, gfx, fr, 2);
                var c = new Vector2(fr.Bw / 2, fr.Bh / 2); var sp = board.BoardToScreen(c.x, c.y); var back = board.ScreenToBoard(sp.x, sp.y);
                Console.WriteLine($"{tag}: board {fr.Cols}x{fr.Rows} distance {board.Distance:F2}  round trip err {Math.Abs(back.x - c.x) + Math.Abs(back.y - c.y):F4}");
            };
            while (!fr.IsOver && fr.Time < 3000 && shots < 2)
            {
                if (fr.Mode == Mode.LevelUp || fr.Mode == Mode.Fuser) { Bot.AutoPick(fr); continue; }
                if (shots == 0 && fr.Turn >= want && fr.Mode == Mode.Playing && fr.Balls.Count > 0) { shot("t" + fr.Turn); shots++; lastD = board.Distance; }
                else if (shots == 1 && fr.Expand == null && fr.Cols > 7 && fr.Mode == Mode.Playing) { shot("wide"); shots++; }
                Bot.Drive(fr); fr.Step(1 / 60f);
            }
            return 0;
        }
        Resources.Root = System.IO.Path.GetFullPath("../../Assets/Resources");
        if (args.Length > 0 && args[0] == "audio") return AudioCheck();
        frame(() => { board.Idle(gfx); screens.Title(1.5f); });
        frame(() => { var nav = Nav.None; screens.HeroSelect(ref nav); });
        foreach (var h in Content.Heroes)
            for (int s = 1; s <= seeds; s++)
            {
                // odd seeds run with ads on: the revive prompt is drawn, then taken (as if watched)
                var r = new Run(s * 31 + 7, h.Id) { AdsOn = s % 2 == 1 };
                world.Units3D = s % 2 == 1;
                try
                {
                    int step = 0;
                    while (!r.IsOver && r.Time < 1500)
                    {
                        if (r.Mode == Mode.Revive)
                        {
                            frame(() => screens.Menus(r));
                            ads.ShowRewarded(AdConfig.Revive, ok => { if (ok) r.Revive(ads.Cfg.ReviveHp, ads.Cfg.ReviveRows); });
                            ads.Now += 1; frame(() => screens.AdPanel(ads)); ads.Now += 1.5f; frame(() => screens.AdPanel(ads)); ads.Close();
                            if (r.Mode != Mode.Playing) throw new Exception("revive did not resume the run");
                            revives++;
                            continue;
                        }
                        bool menu = r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser;
                        if (menu || step % 6 == 0)
                            frame(() =>
                            {
                                world.Layout(r, screens.HudTop, screens.HudBottom);
                                if (world.Units3D) board.Frame(r, world, gfx); else board.Idle(gfx);
                                world.Draw(r);
                                bool p = false;
                                screens.Hud(r, world, ref p);
                                screens.Menus(r);
                            });
                        if (menu)
                        {
                            // walk through the fuser sub-views once before choosing
                            if (r.Mode == Mode.Fuser && r.Menu.View == FuserView.Main)
                            {
                                if (r.ComboPairs().Count > 0) { r.Menu.View = FuserView.Combo; frame(() => screens.Menus(r)); r.Menu.Host = r.ComboPairs()[0][0]; frame(() => screens.Menus(r)); r.Menu.Host = null; }
                                if (r.EvolveRecipes().Count > 0) { r.Menu.View = FuserView.Evo; frame(() => screens.Menus(r)); }
                                r.Menu.View = FuserView.Main;
                                if (r.AdsOn) { frame(() => screens.Menus(r)); r.FuserAdUpgrade(); frame(() => screens.Menus(r)); }
                            }
                            Bot.AutoPick(r);
                            if (r.Menu != null && r.Menu.View == FuserView.Result) frame(() => screens.Menus(r));
                            continue;
                        }
                        if (step % 3000 == 1500) { r.Pause(); frame(() => screens.Menus(r)); r.Resume(); }
                        Bot.Drive(r);
                        r.Step(1 / 60f);
                        step++;
                    }
                    frame(() => { world.Layout(r, screens.HudTop, screens.HudBottom); world.Draw(r); bool p = false; screens.Hud(r, world, ref p); screens.Menus(r); });
                    Console.WriteLine($"{h.Id,-8} seed {s} {r.Mode,-9} turn {r.Turn,3}  frames so far {frames}");
                }
                catch (Exception ex)
                {
                    errors++;
                    Console.WriteLine($"{h.Id,-8} seed {s} ERROR {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        // style budgets (§8): foe ≤ 400 vertices, elite and boss ≤ 1500, hero ≤ 1000, wall module ≤ 2000
        int modelFoe = 0, modelHero = 0;
        foreach (var kv in Content.Enemies)
        {
            string mk;
            if (!Board3D.FoeModels.TryGetValue(kv.Key, out mk)) { errors++; Console.WriteLine($"MODEL foe {kv.Key}: no model mapped"); continue; }
            var ma = Models.Model(mk, false, true, null);
            if (ma == null) { errors++; Console.WriteLine($"MODEL foe {kv.Key}: {mk} not found"); continue; }
            modelFoe = Math.Max(modelFoe, ma.Mesh.VertexCount);
            if (ma.Mesh.VertexCount > 400) { errors++; Console.WriteLine($"BUDGET model {mk}: {ma.Mesh.VertexCount} > 400"); }
        }
        foreach (var hd in Content.Heroes)
        {
            string mk;
            var ma = Board3D.HeroModels.TryGetValue(hd.Id, out mk) ? Models.Model(mk, true, false, null) : null;
            if (ma == null) { errors++; Console.WriteLine($"MODEL hero {hd.Id}: no model"); continue; }
            modelHero = Math.Max(modelHero, ma.Mesh.VertexCount);
            if (ma.Mesh.VertexCount > 1000) { errors++; Console.WriteLine($"BUDGET model {mk}: {ma.Mesh.VertexCount} > 1000"); }
        }
        Console.WriteLine($"delivered models: foe max {modelFoe} verts, hero max {modelHero} verts");
        // the procedural fallbacks (bosses, pods, and any unit whose model is missing)
        foreach (var kv in Content.Enemies)
        {
            int n = Models.Foe(kv.Key, kv.Value.Color, kv.Value.Elite).VertexCount, cap = kv.Value.Elite ? 1500 : 400;
            if (n > cap) { errors++; Console.WriteLine($"BUDGET foe {kv.Key}: {n} > {cap}"); }
        }
        foreach (var m in new[] { Models.Bell(), Models.Hive(), Models.Maw() })
            if (m.VertexCount > 1500) { errors++; Console.WriteLine($"BUDGET boss {m.name}: {m.VertexCount} > 1500"); }
        foreach (var hd in Content.Heroes)
        {
            int n = Models.Hero(hd.Id, hd.Color).VertexCount;
            if (n > 1000) { errors++; Console.WriteLine($"BUDGET hero {hd.Id}: {n} > 1000"); }
        }
        foreach (var m in Models.WallModules())
            if (m.VertexCount > 2000) { errors++; Console.WriteLine($"BUDGET wall {m.name}: {m.VertexCount} > 2000"); }
        int cellsUsed = PixelArt.CellOf("ring") + 1;
        Console.WriteLine($"verts: foe max {Models.MaxFoeVerts}, boss max {Models.MaxBossVerts}, hero {Models.HeroVerts}, wall {Models.WallModules()[0].VertexCount}; atlas cells used {cellsUsed}/256");
        Console.WriteLine($"revives {revives}  frames {frames}  errors {errors}  gameobjects {GameObject.Created}");
        return errors > 0 ? 1 : 0;
    }

    /// <summary>Writes the pixel-art atlas as a binary PPM (rows top-down, alpha over the fog colour), scaled 4×.</summary>
    /// Runs one bot game through AudioBank with the real clip list and checks the delivery's runtime rules.
    static int AudioCheck()
    {
        var bank = new AudioBank(new GameObject("audio"));
        var r = new Run(77, Content.Heroes[0].Id);
        float t = 0; int fails = 0, cues = 0;
        bank.Play(Sfx.Hit, 0); if (AudioSource.Plays.Count != 0) { Console.WriteLine("FAIL played before unlock"); fails++; }
        bank.Unlock();
        while (!r.IsOver && r.Time < 900)
        {
            if (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser) { Bot.AutoPick(r); continue; }
            Bot.Drive(r); r.Step(1 / 60f); t += 1 / 60f;
            cues += r.Sounds.Count;
            bank.Drain(r, t);
            bank.Music(!r.IsOver, false);
        }
        var keys = new System.Collections.Generic.SortedDictionary<string, int>();
        foreach (var kv in AudioSource.Plays) { var k = kv.Key.Substring(kv.Key.LastIndexOf('/') + 1); k = k.Contains("loop") ? k : k.Substring(0, k.LastIndexOf('_')); int n; keys.TryGetValue(k, out n); keys[k] = n + kv.Value; }
        Console.WriteLine($"audio: {cues} cues over {t:F0}s, run ended {r.Mode}");
        foreach (var kv in keys) Console.WriteLine($"  {kv.Key,-16} {kv.Value}");
        foreach (var k in new[] { "ball_launch", "ball_wall", "ball_hit", "en_death", "pk_xp", "mus_battle_loop" })
            if (!keys.ContainsKey(k)) { Console.WriteLine("FAIL never played " + k); fails++; }
        if (r.Sounds.Count != 0) { Console.WriteLine("FAIL queue not drained"); fails++; }
        Console.WriteLine(fails == 0 ? "audio OK" : fails + " audio failures");
        return fails == 0 ? 0 : 1;
    }

    static void DumpAtlas(string path)
    {
        var px = Texture2D.LastPixels; int w = Texture2D.LastW, h = Texture2D.LastH, k = 4;
        using var f = System.IO.File.Create(path);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6 {w * k} {h * k} 255\n");
        f.Write(head, 0, head.Length);
        for (int y = h * k - 1; y >= 0; y--)
            for (int x = 0; x < w * k; x++)
            {
                var c = px[(y / k) * w + x / k];
                float a = c.a / 255f;
                f.WriteByte((byte)(c.r * a + 38 * (1 - a))); f.WriteByte((byte)(c.g * a + 41 * (1 - a))); f.WriteByte((byte)(c.b * a + 55 * (1 - a)));
            }
    }

    /// <summary>
    /// Software render of every model with the game's key light and trilight (no shadows or fog), viewed along +z like
    /// the game camera, tilted by `tilt` degrees to show volume. Back faces are culled, so wrong winding shows as holes.
    /// </summary>
    static void RenderModels(string path, float tilt)
    {
        var meshes = new System.Collections.Generic.List<Mesh>();
        foreach (var kv in Content.Enemies) meshes.Add(Models.Foe(kv.Key, kv.Value.Color, kv.Value.Elite));
        meshes.Add(Models.Foe("pod", 0, false));
        meshes.Add(Models.Bell()); meshes.Add(Models.Hive()); meshes.Add(Models.Maw());
        foreach (var hd in Content.Heroes) meshes.Add(Models.Hero(hd.Id, hd.Color));
        meshes.AddRange(Models.WallModules());
        int cols = 8, cell = 120, rows = (meshes.Count + cols - 1) / cols, W = cols * cell, H = rows * cell;
        var img = new float[W * H * 3]; var zb = new float[W * H];
        for (int i = 0; i < W * H; i++) { zb[i] = 1e9f; img[i * 3] = 38 / 255f; img[i * 3 + 1] = 41 / 255f; img[i * 3 + 2] = 55 / 255f; }
        float ta = tilt * Mathf.Deg2Rad, ct = Mathf.Cos(ta), st = Mathf.Sin(ta);
        Func<Vector3, Vector3> rot = p => new Vector3(p.x, p.y * ct - p.z * st, p.y * st + p.z * ct);   // tip the bottom edge toward the viewer
        var toLight = rot(new Vector3(0, 0.1736f, -0.9848f));
        float[] sky = { 54 / 255f, 58 / 255f, 66 / 255f }, eq = { 29 / 255f, 32 / 255f, 34 / 255f }, gr = { 12 / 255f, 11 / 255f, 9 / 255f };
        for (int mi = 0; mi < meshes.Count; mi++)
        {
            var m = meshes[mi];
            float ox = (mi % cols + 0.5f) * cell, oy = (mi / cols + 0.5f) * cell, sc = cell * 0.62f;
            for (int t = 0; t < m.T.Count; t += 3)
            {
                int a = m.T[t], b = m.T[t + 1], c = m.T[t + 2];
                var n = rot(m.N[a]);
                if (n.z >= 0) continue;   // faces away from the viewer at -z
                var pa = rot(m.V[a]); var pb = rot(m.V[b]); var pc = rot(m.V[c]);
                float ndl = Math.Max(0, Vector3.Dot(n, toLight)), up = -n.z;
                var col = m.C[a]; float em = m.U[a].x;
                float[] rgb = new float[3];
                for (int k = 0; k < 3; k++)
                {
                    float amb = up >= 0 ? eq[k] + (sky[k] - eq[k]) * up : eq[k] + (gr[k] - eq[k]) * -up;
                    float alb = (k == 0 ? col.r : k == 1 ? col.g : col.b) / 255f;
                    rgb[k] = Math.Min(1, alb * (amb + ndl) + alb * em * 3);
                }
                float x0 = ox + pa.x * sc, y0 = oy - pa.y * sc, x1 = ox + pb.x * sc, y1 = oy - pb.y * sc, x2 = ox + pc.x * sc, y2 = oy - pc.y * sc;
                int minx = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2)))), maxx = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
                int miny = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)))), maxy = Math.Min(H - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
                float den = (y1 - y2) * (x0 - x2) + (x2 - x1) * (y0 - y2);
                if (Math.Abs(den) < 1e-6f) continue;
                for (int y = miny; y <= maxy; y++)
                    for (int x = minx; x <= maxx; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((y1 - y2) * (px - x2) + (x2 - x1) * (py - y2)) / den, w1 = ((y2 - y0) * (px - x2) + (x0 - x2) * (py - y2)) / den, w2 = 1 - w0 - w1;
                        if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                        float z = w0 * pa.z + w1 * pb.z + w2 * pc.z;
                        int i = y * W + x;
                        if (z >= zb[i]) continue;
                        zb[i] = z; img[i * 3] = rgb[0]; img[i * 3 + 1] = rgb[1]; img[i * 3 + 2] = rgb[2];
                    }
            }
        }
        using var f = System.IO.File.Create(path);
        var head = System.Text.Encoding.ASCII.GetBytes($"P6 {W} {H} 255\n");
        f.Write(head, 0, head.Length);
        foreach (var v in img) f.WriteByte((byte)(Math.Min(1, v) * 255));
    }
}
