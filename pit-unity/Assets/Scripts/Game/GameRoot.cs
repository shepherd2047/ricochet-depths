// Entry point. Builds the cameras, the 3D board and the drawing layers in code (no scene setup needed), runs the
// simulation at a fixed 60 Hz and draws the current screen every frame.
using Pit.Core;
using UnityEngine;

namespace Pit.Game
{
    public sealed class GameRoot : MonoBehaviour
    {
        enum Screen_ { Title, Heroes, Game }

        const float DesignW = 360, DesignH = 640, Step = 1 / 60f;

        Camera cam;
        Board3D board;
        Gfx gfx;
        Ui ui;
        WorldView world;
        Screens screens;
        IPlatform platform;
        AudioBank audio;

        Screen_ screen = Screen_.Title;
        Run run;
        string heroId = "tinker";
        int seed;
        float acc, clock;

        // floating joystick
        bool stick, stickMoved;
        float stickX, stickY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindObjectOfType<GameRoot>() != null) return;
            var go = new GameObject("Pit");
            DontDestroyOnLoad(go);
            go.AddComponent<GameRoot>();
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Input.multiTouchEnabled = true;
            platform = PlatformHost.Current;
            platform.OnBoot();

            foreach (var c in FindObjectsOfType<Camera>()) c.enabled = false;
            var cg = new GameObject("Pit UI Camera");
            cg.transform.SetParent(transform, false);
            cam = cg.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Col.Bg;
            cam.nearClipPlane = -100; cam.farClipPlane = 100;
            cam.useOcclusionCulling = false;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.depth = 1;
            board = new Board3D(transform, cam);

            var layers = new GameObject("Layers");
            layers.transform.SetParent(transform, false);
            gfx = new Gfx(layers.transform, board.WorldRoot);
            ui = new Ui(gfx);
            world = new WorldView(gfx) { Units3D = true };
            screens = new Screens(gfx, ui);
            audio = new AudioBank(gameObject);
            seed = System.Environment.TickCount & 0x7fffffff;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            clock += dt;
            float scale = Mathf.Min(Screen.width / DesignW, Screen.height / DesignH);
            if (scale <= 0) return;
            float w = Screen.width / scale, h = Screen.height / scale;

            cam.orthographicSize = h / 2;
            cam.transform.localPosition = new Vector3(w / 2, -h / 2, -10);
            var ins = platform.SafeInsets;
            screens.SafeTop = ins.x / scale;
            screens.SafeBottom = ins.y / scale;

            ui.Poll(scale, clock);
            if (ui.Pressed && !audio.Unlocked) audio.Unlock();
            gfx.Time = clock;
            gfx.Begin(w, h, scale);
            ui.BeginFrame();
            if (screen != Screen_.Game) board.Idle(gfx);

            switch (screen)
            {
                case Screen_.Title:
                    if (screens.Title(clock) == Nav.Start) screen = Screen_.Heroes;
                    break;
                case Screen_.Heroes:
                    {
                        var nav = Nav.None;
                        var picked = screens.HeroSelect(ref nav);
                        if (nav == Nav.PickHero && picked != null) { heroId = picked; NewRun(); }
                        else if (nav == Nav.Quit) screen = Screen_.Title;
                        break;
                    }
                case Screen_.Game:
                    GameFrame(dt);
                    break;
            }

            ui.EndFrame();
            gfx.End();

            if (ui.Clicks > 0) audio.Click(clock);
            ui.Clicks = 0;
            if (screen == Screen_.Game && run != null)
            {
                audio.Drain(run, clock);
                audio.Music(!run.IsOver, run.Mode == Mode.Paused);
            }
            else audio.Music(false, false);
        }

        void NewRun()
        {
            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
            run = new Run(seed, heroId);
            acc = 0;
            stick = false;
            screen = Screen_.Game;
        }

        void GameFrame(float dt)
        {
            var r = run;
            world.Layout(r, screens.HudTop, screens.HudBottom);

            // ---- input
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) { if (r.Mode == Mode.Paused) r.Resume(); else r.Pause(); }
            if (Input.GetKeyDown(KeyCode.B)) screens.BotOn = !screens.BotOn;   // dev autopilot
            bool playing = r.Mode == Mode.Playing || r.Mode == Mode.Intro || r.Mode == Mode.Clearing;
            float ix = 0, iy = 0;
            if (playing)
            {
                if (ui.Pressed && !ui.PressOnWidget && ui.Y > screens.SafeTop + 50) { stick = true; stickMoved = false; stickX = ui.X; stickY = ui.Y; }
                if (stick && ui.Down)
                {
                    float dx = ui.X - stickX, dy = ui.Y - stickY;
                    if (dx * dx + dy * dy > 64) stickMoved = true;
                    if (stickMoved)
                    {
                        // the drag's direction on the board (cast through the tilted camera), its strength from the
                        // on-screen drag length
                        var b0 = board.ScreenToBoard(stickX, stickY); var b1 = board.ScreenToBoard(ui.X, ui.Y);
                        float bx = b1.x - b0.x, by = b1.y - b0.y, bl = Mathf.Sqrt(bx * bx + by * by);
                        float k = Mathf.Min(1, Mathf.Sqrt(dx * dx + dy * dy) / 46);
                        if (bl > 1e-4f) { ix = bx / bl * k; iy = by / bl * k; }
                    }
                }
                if (stick && ui.Released)
                {
                    if (!stickMoved && clock - ui.PressTime < 0.3f)
                    {
                        var wpos = board.ScreenToBoard(ui.X, ui.Y);   // the camera is tilted, so cast onto the board plane
                        r.TapWorld(wpos.x, wpos.y);
                    }
                    stick = false;
                }
            }
            else stick = false;
            float kx = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
            float ky = (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0);
            if (kx != 0 || ky != 0) { ix = kx; iy = ky; }
            r.InputX = ix; r.InputY = iy;

            // ---- simulate
            if (screens.BotOn && (r.Mode == Mode.LevelUp || r.Mode == Mode.Fuser)) Bot.AutoPick(r);
            acc += dt * screens.SpeedSetting;
            int steps = 0;
            while (acc >= Step && steps < 8)
            {
                if (screens.BotOn) Bot.Drive(r);
                r.Step(Step);
                acc -= Step; steps++;
            }
            if (steps == 8) acc = 0;

            // ---- draw
            world.Layout(r, screens.HudTop, screens.HudBottom);
            board.Frame(r, world, gfx);
            world.Draw(r);
            bool pause = false;
            screens.Hud(r, world, ref pause);
            if (pause) r.Pause();
            if (stick && stickMoved)
            {
                gfx.Cur = Layer.Hud;
                gfx.Ring(stickX, stickY, 46, 2, new Color32(255, 255, 255, 90));
                float dx = ui.X - stickX, dy = ui.Y - stickY, d = Mathf.Sqrt(dx * dx + dy * dy), k = d > 46 ? 46 / d : 1;
                gfx.Circle(stickX + dx * k, stickY + dy * k, 18, new Color32(255, 255, 255, 90));
            }
            var nav = screens.Menus(r);
            if (nav == Nav.Again || nav == Nav.Restart) NewRun();
            else if (nav == Nav.Heroes) { screen = Screen_.Heroes; run = null; }
        }

        // backgrounded (TikTok onHide): pause the run and mute everything until we come back
        void OnApplicationPause(bool paused) { AudioListener.pause = paused; if (paused && run != null) run.Pause(); }
        void OnApplicationFocus(bool focus) { AudioListener.pause = !focus; if (!focus && run != null) run.Pause(); }
    }
}
