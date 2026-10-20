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
        PlaceholderAds ads;
        int runsEnded;
        float lastInterstitial = -1e9f;
        bool adMuted;

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
            if (FindAnyObjectByType<GameRoot>() != null) return;
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

            foreach (var c in Camera.allCameras) c.enabled = false;
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
            audio = new AudioBank(gameObject);
            ads = new PlaceholderAds(AdConfig.Default);
            screens = new Screens(gfx, ui) { Board = board, Audio = audio, SelHero = heroId, Ads = ads, AdCfg = ads.Cfg };
            seed = System.Environment.TickCount & 0x7fffffff;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            clock += dt;
            // pit-ui integer scale: s screen px per art px, so the atlas lands on whole pixels; 1 art px = 2 design units
            int s = Mathf.Max(1, Mathf.Min(Screen.width / 180, Screen.height / 300));
            float scale = s / 2f;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            float w = Screen.width / scale, h = Screen.height / scale;

            cam.orthographicSize = h / 2;
            cam.transform.localPosition = new Vector3(w / 2, -h / 2, -10);
            var ins = platform.SafeInsets;
            screens.SafeTop = ins.x / scale;
            screens.SafeBottom = ins.y / scale;

            ads.Now = clock;
            ads.Tick();
            if (PlaceholderAds.DebugToggleAllowed && Input.GetKeyDown(KeyCode.F8)) ads.FailLoads = !ads.FailLoads;
            // ads.md §4: an ad on screen stops the run's clock and mutes the game
            if (ads.Showing != adMuted) { adMuted = ads.Showing; AudioListener.pause = adMuted; }

            ui.Poll(scale, clock);
            if (ui.Pressed && !audio.Unlocked) audio.Unlock();
            gfx.Time = clock;
            gfx.Begin(w, h, scale);
            ui.BeginFrame();
            if (screen != Screen_.Game) board.Idle(gfx);

            if (ads.Showing)
            {
                // the game stays drawn underneath (frozen) so the board has a frame; only the ad takes input
                if (screen == Screen_.Game && run != null) { world.Layout(run, screens.HudTop, screens.HudBottom); board.Frame(run, world, gfx); }
                screens.AdPanel(ads);
            }
            else switch (screen)
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
            run = new Run(seed, heroId) { AdsOn = true };
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
                if (ui.Pressed && !ui.PressOnWidget && ui.Y > screens.HudTop - 30) { stick = true; stickMoved = false; stickX = ui.X; stickY = ui.Y; }
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
            if (screens.BotOn && r.Mode == Mode.Revive) r.GiveUp();   // the bot never watches ads
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
            if (nav == Nav.None) return;
            System.Action go = () =>
            {
                if (nav == Nav.Again || nav == Nav.Restart) NewRun();
                else if (nav == Nav.Heroes) { screen = Screen_.Heroes; run = null; }
                else if (nav == Nav.Quit) { screen = Screen_.Title; run = null; }
            };
            if (r.IsOver && (nav == Nav.Again || nav == Nav.Quit) && InterstitialDue(r))
            {
                lastInterstitial = clock;
                ads.ShowInterstitial(AdConfig.Results, go);
            }
            else go();
        }

        /// <summary>
        /// ads.md §2: only on the results screen's Retry/Home, never after the first run, never in the first 180 s,
        /// 180 s apart, and not in a run where a rewarded ad was watched.
        /// </summary>
        bool InterstitialDue(Run r)
        {
            var c = ads.Cfg;
            runsEnded++;
            return runsEnded >= c.InterstitialFirstRun && clock >= c.InterstitialAfterLaunch &&
                   clock - lastInterstitial >= c.InterstitialGap && !r.AdWatched;
        }

        // backgrounded (TikTok onHide): pause the run and mute everything until we come back
        void OnApplicationPause(bool paused) { AudioListener.pause = paused || adMuted; if (paused && run != null) run.Pause(); }
        void OnApplicationFocus(bool focus) { AudioListener.pause = !focus || adMuted; if (!focus && run != null) run.Pause(); }
    }
}
