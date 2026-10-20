// The 3D presentation of the board (style.md §1–§6). The simulation stays 2D: Board3D only mirrors it.
//   • A perspective camera looks down into the pit at 40° from straight-on, from behind and above the hero (style.md §2
//     as ruled, LIGHTING.md). Each frame it is fitted so the near row plus 1.5 cells each side (the prop lane and half
//     a cell of wall) spans the screen width and the board sits between the HUD bands, so it eases back by itself while
//     the pit widens over 3 s. Touches are cast back onto the board plane (ScreenToBoard).
//   • The 2D World/Glow layers of Gfx are parented to WorldRoot, which lays design space flat onto the board plane.
//   • One white key light with soft shadows, two blue-grey wall fills and the copper furnace spot (per-pixel additional
//     lights), a trilight ambient, the pit-axis fog past the far-wall line (in the shaders, from _PitLineY / _PitFarZ;
//     built-in fog is off), and Bloom whose threshold only the over-bright
//     Glow layer, emissive eyes and the scenery's window panes reach.
//   • The pit-scene corridor kit (ASSEMBLY.md, spec §1.3a): no ceiling, a long corridor whose floor strips and wall
//     segments scroll toward the hero and recycle in 4-row groups, start → mid → end segments by run progress, and the
//     casting-pit gate that rides in out of the fog for the final boss. One pooled view per foe, boss and
//     the hero, animated at a stepped 10 fps. Heroes and foes are the art team's models (Models.Model, mapped below);
//     the three bosses and the brood pods are still procedural (no models delivered yet).
// The UI keeps its own orthographic camera, stacked on top as a URP overlay camera.
using System.Collections.Generic;
using Pit.Core;
using UnityEngine;
#if PIT_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace Pit.Game
{
    public sealed class Board3D
    {
        public const float Fov = 65, Near = 0.3f, Far = 1000;
        public const float Pitch = 40;                           // degrees away from looking straight at the plane
        const float MarginX = 1.5f;                              // cells kept visible past each side of the near row
        const float Margin = 0.5f;                               // cells kept visible past the far and near rows
        public const float K = 1 / Run.CELL;                    // world units per board unit: one cell = 1 unit
        const float AnimFps = 10;

        public static readonly Color32 FogColor = new Color32(38, 41, 55, 255);
        static readonly Color32 Sky = new Color32(54, 58, 66, 255), Equator = new Color32(29, 32, 34, 255), Ground = new Color32(12, 11, 9, 255);

        public readonly Camera Cam;
        public readonly Transform WorldRoot;
        readonly Transform root, board, units;
        readonly Material unitMat;
        readonly Shader unitShader;
        readonly Light spot;
        Run lastRun;

        // game ids -> delivered models. Foes without a model of their own borrow the closest archetype (art-bible §5).
        internal static readonly Dictionary<string, string> FoeModels = new Dictionary<string, string>
        {
            { "grunt", "enemy_shambler" }, { "mite", "enemy_mite" }, { "brick", "enemy_brute" }, { "archer", "enemy_spitter" },
            { "slab", "enemy_slab" }, { "popper", "enemy_broodpot" }, { "mender", "enemy_mender" }, { "mortar", "enemy_lobber" },
            { "flyer", "enemy_mite" }, { "skitter", "enemy_mite" }, { "dasher", "enemy_mite" },
            { "pavise", "enemy_brute" }, { "flanker", "enemy_brute" }, { "hulk", "enemy_brute" },
            { "nester", "enemy_broodpot" }, { "chiller", "enemy_spitter" }, { "lancer", "enemy_spitter" }, { "volleyer", "enemy_spitter" },
            { "delver", "enemy_shambler" }, { "burr", "enemy_shambler" }, { "gulper", "enemy_slab" }, { "hurler", "enemy_lobber" },
        };
        // six heroes, four models (art-bible §4): the closest look per twist; Scatterjack and Fanfare share one
        public static readonly Dictionary<string, string> HeroModels = new Dictionary<string, string>
        {
            { "tinker", "hero_tinker" }, { "needle", "hero_wren" }, { "bulwark", "hero_osk" }, { "ricco", "hero_juno" },
            { "scatter", "hero_wren" }, { "fanfare", "hero_osk" },
        };

        sealed class View
        {
            public GameObject Go, Body;
            public MeshFilter Mf;
            public MeshRenderer Mr;
            public MaterialPropertyBlock Mpb;
            public GameObject[] Plates;
            public int Id;
            public bool Seen;
            public float DieT = -1;
            public Vector3 Scale;
            // baked foe clips (pit-art monster_lib → Tools/bake_hero_anim.py at 12 fps); null for code-built foes
            public Dictionary<string, Models.Clip> Clips;
            public string Clip;
            public float ClipAt;
        }

        // the Attack clip's release frame: pit-art report, projectile on frame 8 of 16 at 24 fps
        const float AttackRelease = 8 / 24f;

        readonly Dictionary<int, View> live = new Dictionary<int, View>();
        readonly Stack<View> pool = new Stack<View>();
        readonly List<int> gone = new List<int>();
        View hero;
        string heroMesh;
        Models.Asset heroModel;
        // baked hero clips (pit-art rig_hero.py → Tools/bake_hero_anim.py), stepped at their baked 10 fps
        string heroClip;
        float heroT, heroClipAt, heroHitAt = -9, heroLastTime, heroLastHurt;
        Run heroRun;

        // camera basis and lens of the last fitted frame, for ScreenToBoard
        Vector3 camPos, camRight = Vector3.right, camUp = Vector3.up, camFwd = Vector3.forward;
        float tanV, tanH, viewW = 1, viewH = 1;

        /// <summary>Camera distance from its aim point on the board, last frame (world units).</summary>
        public float Distance { get; private set; }
        // the hero keeps its 7x9 on-screen size as the camera pulls back: its scale grows by Distance / the 7x9 distance
        float baseDist;
        public float HeroGrow { get; private set; } = 1;
        /// <summary>How far (cells, at 7x9) the hero model stands back toward the camera, off the launch point.</summary>
        public const float HeroBackCells = 0.35f;
        float heroK = 1;

        public Board3D(Transform parent, Camera uiCam)
        {
            root = new GameObject("Board3D").transform;
            root.SetParent(parent, false);

            var cg = new GameObject("Pit 3D Camera");
            cg.transform.SetParent(root, false);
            Cam = cg.AddComponent<Camera>();
            Cam.orthographic = false;
            Cam.fieldOfView = Fov;
            Cam.nearClipPlane = Near; Cam.farClipPlane = Far;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = FogColor;
            Cam.cullingMask = ~((1 << Gfx.UiLayer) | (1 << PreviewLayer));
            Cam.allowMSAA = false;
            Cam.allowHDR = true;          // only so bloom can see the over-bright layer
            Cam.useOcclusionCulling = false;
            Cam.depth = 0;

            uiCam.cullingMask = 1 << Gfx.UiLayer;
            uiCam.allowHDR = false;
#if PIT_URP
            var data = Cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
            var uiData = uiCam.GetUniversalAdditionalCameraData();
            uiData.renderType = CameraRenderType.Overlay;
            uiData.renderPostProcessing = false;
            uiData.renderShadows = false;
            data.cameraStack.Add(uiCam);

            var vg = new GameObject("Bloom Volume");
            vg.transform.SetParent(root, false);
            var vol = vg.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);    // above anything lit normally; reached by Glow (×2.4) and emissive eyes
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false);
            vol.sharedProfile = profile;
#else
            // built-in pipeline fallback: no bloom, the UI camera just draws after the 3D one
            uiCam.clearFlags = CameraClearFlags.Depth;
            uiCam.depth = 1;
#endif

            var lg = new GameObject("Key Light");
            lg.transform.SetParent(root, false);
            lg.transform.localRotation = Quaternion.Euler(10, 0, 0);   // LIGHTING.md: along the corridor, tipped 10° toward the player end
            var light = lg.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;

            // LIGHTING.md wall fill: one per wall, across the corridor into the wall it lights, about 80° from straight
            // down and tipped 30° toward the far end (pit-frame Euler (30, ±80, 0), here with up = -z)
            for (int side = -1; side <= 1; side += 2)
            {
                var fg = new GameObject(side < 0 ? "Fill Left" : "Fill Right");
                fg.transform.SetParent(root, false);
                float a = 80 * Mathf.Deg2Rad, t = 30 * Mathf.Deg2Rad;
                var dir = new Vector3(side * Mathf.Cos(t) * Mathf.Sin(a), Mathf.Sin(t), Mathf.Cos(t) * Mathf.Cos(a));
                fg.transform.localRotation = Quaternion.LookRotation(dir, new Vector3(0, 0, -1));
                var fl = fg.AddComponent<Light>();
                fl.type = LightType.Directional;
                fl.color = new Color32(0x96, 0xac, 0xbe, 255);
                fl.intensity = 0.8f;
                fl.shadows = LightShadows.None;
            }

            // LIGHTING.md theme light: the furnace mouth, a copper spot high behind the left wall at the far end
            var sg = new GameObject("Furnace Spot");
            sg.transform.SetParent(root, false);
            spot = sg.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color32(0xd9, 0x88, 0x41, 255);
            spot.shadows = LightShadows.None;
            spot.intensity = 60 * K * K;   // LIGHTING.md's 60 is for kit units; the world here is the kit scaled by K (inverse-square)
            spot.range = 25 * K;
            spot.spotAngle = 55; spot.innerSpotAngle = 30;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Sky;
            RenderSettings.ambientEquatorColor = Equator;
            RenderSettings.ambientGroundColor = Ground;
            RenderSettings.fog = false;   // LIGHTING.md: the shaders fog along the pit instead (PitFog)
            Shader.SetGlobalColor("_PitFogColor", FogColor);
            Shader.SetGlobalColor("_PitSky", Sky);
            Shader.SetGlobalColor("_PitEquator", Equator);
            Shader.SetGlobalColor("_PitGround", Ground);

            WorldRoot = new GameObject("World2D").transform;
            WorldRoot.SetParent(root, false);

            var sh = Resources.Load<Shader>("PitUnit");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (sh == null) sh = Shader.Find("Standard");
            unitShader = sh;
            unitMat = new Material(sh) { name = "pit-unit" };

            board = new GameObject("Pit").transform;
            board.SetParent(root, false);
            units = new GameObject("Units").transform;
            units.SetParent(board, false);
            corridor = new GameObject("Corridor").transform;
            corridor.SetParent(board, false);
            sceneMat = new Material(sh) { name = "T1_Scenery", enableInstancing = true };
            sceneMat.SetTexture("_MainTex", Resources.Load<Texture2D>("Scene/T1_Scenery_Atlas"));
            sceneMat.SetFloat("_Textured", 1);
            sceneMat.SetFloat("_TexEmit", 0);
            // ASSEMBLY §5: the window panes glow through the emission map (white, HDR +1)
            sceneMat.SetTexture("_EmisMap", Resources.Load<Texture2D>("Scene/T1_Scenery_Emit"));
            sceneMat.SetFloat("_EmisMapGain", 1);
            var bsh = Resources.Load<Shader>("PitBeam");
            beamMat = new Material(bsh != null ? bsh : sh) { name = "T1_Beam" };
            beamMat.SetColor("_Color", new Color32(0xe8, 0xa4, 0x56, 255));
        }

        GameObject MakeObject(string name, Transform parent, Mesh mesh, out MeshFilter mf)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = unitMat;
            return go;
        }

        static Vector3 P(float bx, float by, float z) { return new Vector3(bx * K, -by * K, z); }
        static float Step(float t) { return Mathf.Floor(t * AnimFps) / AnimFps; }

        // ------------------------------------------------------------ frame
        /// <summary>Menus: keep the camera (it paints the background) but hide the pit.</summary>
        public void Idle(Gfx g)
        {
            StopPreview();
            if (board.gameObject.activeSelf) board.gameObject.SetActive(false);
            Cam.transform.localPosition = new Vector3(0, 0, -10);
            Cam.transform.localRotation = Quaternion.identity;
        }

        public void Frame(Run r, WorldView wv, Gfx g)
        {
            StopPreview();
            if (!board.gameObject.activeSelf) board.gameObject.SetActive(true);
            if (r != lastRun) { Clear(); lastRun = r; }

            FitCamera(r, wv, g);
            HeroGrow = baseDist > 0 ? Mathf.Max(1, Distance / baseDist) : 1;
            wv.HeroScale = HeroGrow;
            wv.HeroBack = HeroBackCells * Run.CELL * HeroGrow;
            float shake = r.Shake > 0 ? r.Shake * 0.12f : 0;
            var jit = shake > 0 ? camRight * ((Random.value - 0.5f) * shake) + camUp * ((Random.value - 0.5f) * shake) : Vector3.zero;
            Cam.transform.localPosition = camPos + jit;
            Cam.transform.localRotation = Quaternion.Euler(-Pitch, 0, 0);
            // design units (WorldView's Sx/Sy) laid flat on the plane: board unit = K world units
            float q = K / wv.S;
            WorldRoot.localPosition = new Vector3(-q * wv.Ox, q * wv.Oy, 0);
            WorldRoot.localScale = new Vector3(q, q, 1);

            Scenery(r);
            Units(r);
            Hero(r);
            AimOverlay(r, wv, g);
        }

        /// <summary>
        /// The aim line and launch point drawn over the 3D view (HUD layer, projected from the board), so the taller
        /// hero never hides them.
        /// </summary>
        void AimOverlay(Run r, WorldView wv, Gfx g)
        {
            if (r.Mode == Mode.GameOver || r.IsOver) return;
            var p = r.P;
            float a = p.Aim * Mathf.Deg2Rad;
            var s0 = BoardToScreen(p.X, p.Y - p.R);
            var s1 = BoardToScreen(p.X + Mathf.Cos(a) * 2.2f, p.Y - p.R - Mathf.Sin(a) * 2.2f);
            var keep = g.Cur;
            g.Cur = Layer.Hud;
            g.Dashed(s0.x, s0.y, s1.x, s1.y, 1.5f, 3, 5, new Color32(255, 255, 255, 120));
            g.Ring(s0.x, s0.y, 2.5f, 1, new Color32(255, 255, 255, 150));
            g.Cur = keep;
        }

        // ------------------------------------------------------------ hero preview (hero pick, UI-SPEC §8.2)
        public const int PreviewLayer = 30;   // unnamed layer only the preview camera renders
        Camera previewCam;
        RenderTexture previewRt;
        Transform previewRoot, previewBody;
        MeshFilter previewMf;
        MeshRenderer previewMr;
        string previewId;
        int previewFrame = -10;

        /// <summary>
        /// Renders the hero's model, front-on and swaying, into a transparent RenderTexture of pw×ph pixels for the
        /// hero-pick well. Call every frame the screen is up; the camera stops on its own when it is not called.
        /// </summary>
        public Texture HeroPreview(string heroId, int pw, int ph, float t)
        {
            pw = Mathf.Clamp(pw, 16, 1024); ph = Mathf.Clamp(ph, 16, 1024);
            if (previewCam == null)
            {
                previewRoot = new GameObject("Hero Preview").transform;
                previewRoot.SetParent(root, false);
                previewRoot.localPosition = new Vector3(0, -500, -500);   // far above and below the pit: out of the board camera's view and the pit fog
                var bg = new GameObject("body"); bg.layer = PreviewLayer;
                previewBody = bg.transform; previewBody.SetParent(previewRoot, false);
                previewMf = bg.AddComponent<MeshFilter>();
                previewMr = bg.AddComponent<MeshRenderer>();
                previewMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var cg = new GameObject("Preview Camera");
                cg.transform.SetParent(previewRoot, false);
                previewCam = cg.AddComponent<Camera>();
                previewCam.cullingMask = 1 << PreviewLayer;
                previewCam.clearFlags = CameraClearFlags.SolidColor;
                previewCam.backgroundColor = new Color(0, 0, 0, 0);
                previewCam.fieldOfView = 26;
                previewCam.nearClipPlane = 0.1f; previewCam.farClipPlane = 20;
                previewCam.allowHDR = false; previewCam.allowMSAA = false;
                previewCam.depth = -5;
#if PIT_URP
                var pd = previewCam.GetUniversalAdditionalCameraData();
                pd.renderType = CameraRenderType.Base;
                pd.renderPostProcessing = false;
                pd.renderShadows = false;
                pd.antialiasing = AntialiasingMode.None;
#endif
                // the camera sits on the −y side and looks +y, like the board camera, so the key light comes from
                // behind it; the hero is turned to face it
                float a = 12 * Mathf.Deg2Rad, d = 3.1f;
                var target = new Vector3(0, 0, -0.5f);
                cg.transform.localPosition = target + new Vector3(0, -Mathf.Cos(a) * d, -Mathf.Sin(a) * d);
                cg.transform.localRotation = Quaternion.LookRotation(target - cg.transform.localPosition, new Vector3(0, 0, -1));
            }
            if (previewRt == null || previewRt.width != pw || previewRt.height != ph)
            {
                if (previewRt != null) { previewCam.targetTexture = null; previewRt.Release(); Object.Destroy(previewRt); }
                previewRt = new RenderTexture(pw, ph, 16, RenderTextureFormat.ARGB32) { name = "pit-hero-preview", filterMode = FilterMode.Point };
                previewCam.targetTexture = previewRt;
            }
            if (previewId != heroId)
            {
                string mk;
                var model = HeroModels.TryGetValue(heroId, out mk) ? Models.Model(mk, true, false, unitShader) : null;
                var hd = Content.Hero(heroId);
                previewMf.sharedMesh = model != null ? model.Mesh : Models.Hero(heroId, hd.Color);
                previewMr.sharedMaterial = model != null ? model.Mat : unitMat;
                float k = model != null ? 1 / Mathf.Max(model.Half.z, 0.5f * Mathf.Max(model.Half.x, model.Half.y) * 2) : 0.8f;
                previewBody.localScale = new Vector3(k, k, k);
                previewId = heroId;
            }
            // baked clips: the preview breathes in Idle; models face +y, turn to the camera, sway ±25° at the stepped rate
            Models.Clip idle;
            var pm = HeroModels.ContainsKey(heroId) ? Models.Model(HeroModels[heroId], true, false, unitShader) : null;
            if (pm != null && pm.Clips != null && pm.Clips.TryGetValue("Idle", out idle)) previewMf.sharedMesh = idle.At(t);
            previewBody.localRotation = Quaternion.Euler(0, 0, 180 + 25 * Mathf.Sin(Step(t) * 0.9f));
            previewFrame = Time.frameCount;
            if (!previewCam.enabled) previewCam.enabled = true;
            if (!previewRoot.gameObject.activeSelf) previewRoot.gameObject.SetActive(true);
            return previewRt;
        }

        /// <summary>Test hook: renders the preview now (batch-mode captures don't run the camera loop).</summary>
        public void RenderPreview() { if (previewCam != null && previewCam.enabled) previewCam.Render(); }

        void StopPreview()
        {
            if (previewCam != null && previewCam.enabled && Time.frameCount - previewFrame > 1)
            {
                previewCam.enabled = false;
                previewRoot.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------ test hooks (Tools/StubCheck renders frames with these)
        internal Vector3 CamPos { get { return camPos; } }
        internal Vector3 CamFwd { get { return camFwd; } }
        internal Vector3 CamUp { get { return camUp; } }
        internal float TanV { get { return tanV; } }

        /// <summary>Visits every visible mesh: mesh, position, scale, rotation about z in degrees (parents sit at the origin).</summary>
        internal void DebugVisit(System.Action<Mesh, Vector3, Vector3, float> f, Run r)
        {
            if (!board.gameObject.activeSelf) return;
            var c = corridor.localPosition;
            foreach (var gr in groups)
            {
                if (gr == null || !gr.T.gameObject.activeSelf) continue;
                var gp = c + gr.T.localPosition * K;
                if (gr.L.sharedMesh != null) f(gr.L.sharedMesh, gp + gr.L.transform.localPosition * K, new Vector3(K, K, K), 90);
                if (gr.R.sharedMesh != null) f(gr.R.sharedMesh, gp + gr.R.transform.localPosition * K, new Vector3(K, K, K), -90);
                if (gr.F.sharedMesh != null) f(gr.F.sharedMesh, gp, new Vector3(K, K, K), 0);
                if (gr.LL.sharedMesh != null) f(gr.LL.sharedMesh, gp + gr.L.transform.localPosition * K, new Vector3(K, K, K), 90);
                if (gr.RL.sharedMesh != null) f(gr.RL.sharedMesh, gp + gr.R.transform.localPosition * K, new Vector3(K, K, K), -90);
            }
            if (gate != null && gate.gameObject.activeSelf)
                foreach (var pc in gatePieces)
                    if (pc.sharedMesh != null) f(pc.sharedMesh, c + (gate.localPosition + pc.transform.localPosition) * K, pc.transform.localScale * K, 0);
            foreach (var kv in live) if (kv.Value.Go.activeSelf) f(kv.Value.Mf.sharedMesh, kv.Value.Go.transform.localPosition, Vector3.Scale(kv.Value.Go.transform.localScale, kv.Value.Body.transform.localScale), 0);
            if (hero != null && hero.Go.activeSelf) f(hero.Mf.sharedMesh, hero.Go.transform.localPosition, Vector3.Scale(hero.Go.transform.localScale, hero.Body.transform.localScale), Mathf.Round((r.P.Aim - 90) / 7.5f) * 7.5f);
        }

        // ------------------------------------------------------------ camera fit and picking
        /// <summary>
        /// Fits the pitched camera: the smallest distance that shows every board corner (plus MarginX across) inside the screen
        /// width and between the HUD bands, centred in that band. The near row is the widest on screen, so the far row's
        /// 7 columns fit with room to spare. At 9:16 and FOV 65 this lands at 16.9 units (19.1 kit units) for 7x9 and 27.3
        /// (30.7) for 13x15: about 4% further than LIGHTING.md's width-only fit (18.39 / 29.57), as the HUD bands bind.
        /// </summary>
        void FitCamera(Run r, WorldView wv, Gfx g)
        {
            float th = Pitch * Mathf.Deg2Rad;
            camFwd = new Vector3(0, Mathf.Sin(th), Mathf.Cos(th));    // toward the far wall and down into the pit
            camUp = new Vector3(0, Mathf.Cos(th), -Mathf.Sin(th));
            camRight = Vector3.right;
            viewW = g.W; viewH = g.H;
            tanV = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
            tanH = tanV * g.W / g.H;
            float aimY;
            Distance = Fit(r.Bw * K, r.Bh * K, wv, g, out aimY);
            baseDist = Fit(7 * Run.CELL * K, 9 * Run.CELL * K, wv, g, out _);
            camPos = new Vector3(r.Bw * K / 2, aimY, 0) - camFwd * Distance;
        }

        /// <summary>The fitted camera distance for a w x h board (world units), and the look-at row.</summary>
        float Fit(float w, float h, WorldView wv, Gfx g, out float aimY)
        {
            float bandTop = 1 - 2 * wv.Top / g.H, bandBot = 1 - 2 * wv.Bottom / g.H, band = bandTop - bandBot;
            aimY = -h / 2; float d = 11;
            for (int it = 0; it < 6; it++)
            {
                float lo = 2, hi = 80;
                for (int k = 0; k < 28; k++)
                {
                    float mid = (lo + hi) / 2;
                    if (Fits(new Vector3(w / 2, aimY, 0) - camFwd * mid, w, h, band)) hi = mid; else lo = mid;
                }
                d = hi;
                var pos = new Vector3(w / 2, aimY, 0) - camFwd * d;
                float far = Ndc(pos, new Vector3(0, Margin, 0)).y, near = Ndc(pos, new Vector3(0, -h - Margin, 0)).y;
                float err = (far + near) / 2 - (bandTop + bandBot) / 2;
                aimY += err * (h + 2 * Margin) / Mathf.Max(0.05f, far - near);
            }
            return d;
        }

        bool Fits(Vector3 pos, float w, float h, float band)
        {
            var a = Ndc(pos, new Vector3(-MarginX, Margin, 0)); var b = Ndc(pos, new Vector3(w + MarginX, Margin, 0));
            var c = Ndc(pos, new Vector3(-MarginX, -h, 0)); var e = Ndc(pos, new Vector3(w + MarginX, -h, 0));   // the near row binds the width
            float maxX = Mathf.Max(Mathf.Max(Mathf.Abs(a.x), Mathf.Abs(b.x)), Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(e.x)));
            return maxX <= 1 && a.y - c.y <= band;
        }

        Vector2 Ndc(Vector3 cam, Vector3 p)
        {
            var v = p - cam;
            float z = Mathf.Max(0.01f, Vector3.Dot(v, camFwd));
            return new Vector2(Vector3.Dot(v, camRight) / (z * tanH), Vector3.Dot(v, camUp) / (z * tanV));
        }

        /// <summary>Projects a board point (on the floor) to design screen units, y down. Inverse of ScreenToBoard.</summary>
        public Vector2 BoardToScreen(float bx, float by)
        {
            var n = Ndc(camPos, P(bx, by, 0));
            return new Vector2((n.x + 1) / 2 * viewW, (1 - n.y) / 2 * viewH);
        }

        /// <summary>
        /// Casts a point in design screen units (y down, as Ui reports it) from the camera onto the board plane and
        /// returns board coordinates (y down), so taps and aim work under the tilted view.
        /// </summary>
        public Vector2 ScreenToBoard(float sx, float sy)
        {
            float nx = sx / viewW * 2 - 1, ny = 1 - sy / viewH * 2;
            var dir = camFwd + camRight * (nx * tanH) + camUp * (ny * tanV);
            if (dir.z <= 1e-4f) return new Vector2(-999, -999);
            float t = -camPos.z / dir.z;
            var p = camPos + dir * t;
            return new Vector2(p.x / K, -p.y / K);
        }

        // ------------------------------------------------------------ corridor (pit-scene ASSEMBLY.md §4, LIGHTING.md)
        // The kit lives under Corridor in kit units (pit-scene's Blender frame: x across, +y toward the far end, up is
        // -z here), scaled by K. Corridor is pinned to the floor: it moves with Run.FloorShift, so units standing on the
        // board keep their place on it. Group slots hold absolute group indices G; a group whose rows have passed
        // k −16 is redrawn 9 groups further on, out in the fog. The walls stand one prop lane (1 cell) outside the board
        // edge; the lane piece shares its wall's transform, and its kerb marks the collision line (ASSEMBLY §1, §2).
        const int Groups = 9;
        static readonly string[][] Sets =
        {
            new[] { "T1_Start_A", "T1_Start_B", "T1_Start_C" },
            new[] { "T1_Mid_A", "T1_Mid_B", "T1_Mid_C" },
            new[] { "T1_End_A", "T1_End_B", "T1_End_C" },
        };

        sealed class Group
        {
            public int G = int.MinValue;
            public Transform T;
            public MeshFilter L, R, F, LL, RL;   // walls, floor strip, prop lanes
            public string Ln, Rn;
            public bool B;
        }

        readonly Transform corridor;
        readonly Material sceneMat, beamMat;
        readonly Group[] groups = new Group[Groups];
        Transform gate;
        MeshFilter[] gatePieces;
        Run sceneRun;
        float bhRef;
        int stripCols = -1;

        void Scenery(Run r)
        {
            const float C = Run.CELL;
            if (r != sceneRun)
            {
                sceneRun = r;
                bhRef = r.L.Rows * C;
                foreach (var gr in groups) if (gr != null) gr.G = int.MinValue;
            }
            float rows = r.Bh / C;
            corridor.localPosition = new Vector3(r.Bw / 2 * K, -(bhRef / 2 + r.FloorShift) * K, 0);
            corridor.localScale = new Vector3(K, K, K);
            float kBase = (r.Bh / 2 - bhRef / 2 - r.FloorShift) / C;   // + 4G + 1.5 = group G's centre row, k up from the board centre
            int cols = Mathf.Clamp(r.Expand != null ? r.Expand.ToCols : r.Cols, 7, 13) | 1;   // strips swap at the start of an expansion
            float wx = r.Bw / 2 + C;   // wall face and lane origin: one lane outside the board edge
            bool restrip = cols != stripCols;
            stripCols = cols;
            float left = TurnsToFinal(r);

            int gLow = Mathf.CeilToInt((-19 - kBase) / 4f);   // lowest group whose top row has not passed k −16
            Group prev = null;
            for (int n = 0; n < Groups; n++)
            {
                int G = gLow + n, slot = ((G % Groups) + Groups) % Groups;
                var gr = groups[slot] ?? (groups[slot] = NewGroup());
                if (gr.G != G) Draw(gr, G, r, kBase + 4 * G + 1.5f, rows, left, prev);
                else if (restrip) Strip(gr, cols);
                // walls follow the board edges every frame, so they slide out with an expansion (ASSEMBLY §4.4)
                gr.L.transform.localPosition = new Vector3(-wx, 0, 0);
                gr.R.transform.localPosition = new Vector3(wx, 0, 0);
                prev = gr;
            }

            // the casting-pit gate: rides in with the corridor and docks 3 cells past the far row as the final boss comes
            float kGate = rows / 2 + 3 + left;
            bool showGate = kGate <= 20 && Models.Scenery("T1_Gate_Lintel") != null;
            if (showGate && gate == null)
            {
                gate = new GameObject("Gate").transform;
                gate.SetParent(corridor, false);
                gatePieces = new[] { Piece("T1_Gate_Tower", gate), Piece("T1_Gate_Tower", gate), Piece("T1_Gate_Lintel", gate) };
                foreach (var pc in gatePieces) pc.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (gate != null)
            {
                if (gate.gameObject.activeSelf != showGate) gate.gameObject.SetActive(showGate);
                gate.localPosition = new Vector3(0, (kGate - kBase) * C, 0);
                gatePieces[0].transform.localPosition = new Vector3(-(wx + 1.2f), 0, 0);
                gatePieces[1].transform.localPosition = new Vector3(wx + 1.2f, 0, 0);
                gatePieces[2].transform.localScale = new Vector3((2 * wx + 2.4f) / 17f, 1, 1);
            }

            // LIGHTING.md: the fog starts at the far-wall line (the board's top edge) and follows the board's half length,
            // which tweens with an expansion; every group stays drawn (fogged to 0.8 → 1 at the treadmill's end, ASSEMBLY §4.6)
            Shader.SetGlobalFloat("_PitLineY", board.position.y);
            Shader.SetGlobalFloat("_PitFarZ", r.Bh / 2);
            // the copper spot follows the board size
            var centre = new Vector3(r.Bw / 2 * K, -r.Bh / 2 * K, 0);
            var sp = centre + Kit(-(r.Bw / 2) - 0.5f, (rows / 2 + 3) * C, 6);
            var aim = centre + Kit(r.Bw / 2, 0.15f * rows * C, 1.5f);
            spot.transform.localPosition = sp;
            spot.transform.localRotation = Quaternion.LookRotation(aim - sp, new Vector3(0, 0, -1));
        }

        /// <summary>Kit-frame offset (across, along the corridor, up) to the game's world frame.</summary>
        static Vector3 Kit(float x, float along, float up) { return new Vector3(x * K, along * K, -up * K); }

        /// <summary>Turns of corridor still to scroll before the final boss walks out and the scroll stops.</summary>
        static float TurnsToFinal(Run r)
        {
            float tl = r.L.TurnLen;
            if (r.Boss != null && r.Boss.BossDef.Final) return 0;
            if (r.FinalStarted) return r.Expand != null ? Mathf.Max(0, r.Expand.Dur - r.Expand.T) / tl : 0;
            return (r.TimeToFinal + 3) / tl;   // the board expansion (3 s) comes before the boss
        }

        Group NewGroup()
        {
            var gr = new Group();
            gr.T = new GameObject("Group").transform;
            gr.T.SetParent(corridor, false);
            gr.L = Piece(null, gr.T); gr.L.transform.localRotation = Quaternion.Euler(0, 0, 90);    // inner face (−y) toward +x
            gr.R = Piece(null, gr.T); gr.R.transform.localRotation = Quaternion.Euler(0, 0, -90);
            gr.F = Piece(null, gr.T);
            gr.F.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gr.LL = Piece(null, gr.L.transform);
            gr.RL = Piece(null, gr.R.transform);
            return gr;
        }

        MeshFilter Piece(string mesh, Transform parent)
        {
            MeshFilter mf;
            MakeObject(mesh ?? "piece", parent, null, out mf);
            SetMesh(mf, mesh != null ? Models.Scenery(mesh) : null);
            return mf;
        }

        /// <summary>A kit mesh with its materials: slot 0 T1_Scenery, slot 1 (the window segments) T1_Beam.</summary>
        void SetMesh(MeshFilter mf, Mesh m)
        {
            mf.sharedMesh = m;
            int n = m != null ? m.subMeshCount : 1;
            var mr = mf.GetComponent<MeshRenderer>();
            if (mr.sharedMaterials.Length != n || mr.sharedMaterial != sceneMat) mr.sharedMaterials = n > 1 ? new[] { sceneMat, beamMat } : new[] { sceneMat };
        }

        /// <summary>ASSEMBLY §4.3: segments by run progress (the turn these rows reach the far row ÷ the final boss
        /// turn, ±0.05 jitter), left ≠ right, no segment twice in a row on one side.</summary>
        void Draw(Group gr, int G, Run r, float kc, float rows, float left, Group prev)
        {
            gr.G = G;
            float finalTurn = r.Turn + left;
            float p = finalTurn > 0 ? Mathf.Clamp01((r.Turn + (kc - rows / 2)) / finalTurn) : 1;
            gr.Ln = Pick(r, p, G, 'L', null, prev != null && prev.G == G - 1 ? prev.Ln : null);
            gr.Rn = Pick(r, p, G, 'R', gr.Ln, prev != null && prev.G == G - 1 ? prev.Rn : null);
            SetMesh(gr.L, Models.Scenery(gr.Ln));
            SetMesh(gr.R, Models.Scenery(gr.Rn));
            // the lane beside each wall: same set, letter = (hash(seed, G, side, 99) >> 7) % 3 (ASSEMBLY §4.1)
            SetMesh(gr.LL, Models.Scenery(Lane(gr.Ln, Hash4((uint)r.Seed, (uint)G, 'L', 99))));
            SetMesh(gr.RL, Models.Scenery(Lane(gr.Rn, Hash4((uint)r.Seed, (uint)G, 'R', 99))));
            gr.B = Hash((uint)r.Seed, (uint)G, 7) % 3 == 0;
            Strip(gr, stripCols);
            gr.T.localPosition = new Vector3(0, (4 * G + 1.5f) * Run.CELL, 0);
        }

        // floor strips cover the board plus both lanes: W{cols + 2}
        void Strip(Group gr, int cols) { SetMesh(gr.F, Models.Scenery("T1_FloorStrip_W" + (cols + 2) + (gr.B ? "_B" : "_A"))); }

        static string Lane(string segment, uint h)
        {
            string set = segment.Substring(3, segment.Length - 5);   // "T1_Mid_A" -> "Mid"
            return "T1_Lane_" + set + "_" + (char)('A' + (h >> 7) % 3);
        }

        static string Pick(Run r, float p, int G, char side, string notThis, string prevSide)
        {
            uint h = Hash((uint)r.Seed, (uint)G, side);
            p += ((h % 1000) / 1000f - 0.5f) * 2 * 0.05f;
            var names = Sets[p < 0.3f ? 0 : p < 0.7f ? 1 : 2];
            int n = names.Length, i = (int)((h >> 10) % (uint)n);
            for (int t = 0; t < n; t++)
            {
                if (names[i] != notThis && !(n >= 3 && names[i] == prevSide)) break;
                i = (i + 1) % n;
            }
            return names[i];
        }

        static uint Hash(uint a, uint b, uint c)
        {
            unchecked
            {
                uint h = 2166136261;
                h = (h ^ a) * 16777619; h = (h ^ b) * 16777619; h = (h ^ c) * 16777619;
                return h;
            }
        }

        static uint Hash4(uint a, uint b, uint c, uint d) { unchecked { return (Hash(a, b, c) ^ d) * 16777619; } }

        // ------------------------------------------------------------ units
        View Get(int id)
        {
            View v;
            if (live.TryGetValue(id, out v)) return v;
            v = pool.Count > 0 ? pool.Pop() : NewView();
            v.Id = id; v.DieT = -1; v.Clips = null; v.Clip = null;
            if (!v.Go.activeSelf) v.Go.SetActive(true);
            for (int i = 0; i < 3; i++) if (v.Plates[i].activeSelf) v.Plates[i].SetActive(false);
            live[id] = v;
            return v;
        }

        View NewView()
        {
            var v = new View();
            v.Go = new GameObject("unit");
            v.Go.transform.SetParent(units, false);
            v.Body = MakeObject("body", v.Go.transform, null, out v.Mf);
            v.Mr = v.Body.GetComponent<MeshRenderer>();
            v.Mpb = new MaterialPropertyBlock();
            v.Plates = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                MeshFilter mf;
                v.Plates[i] = MakeObject("plate", v.Go.transform, Models.Plate(), out mf);
                // a property block like every other unit renderer: a plate drawn through the SRP batcher next to the
                // scenery (same shader, no block) left the corridor untextured and unfogged, i.e. white
                var pb = new MaterialPropertyBlock(); pb.SetFloat("_Flash", 0);
                v.Plates[i].GetComponent<MeshRenderer>().SetPropertyBlock(pb);
                v.Plates[i].SetActive(false);
            }
            return v;
        }

        void Clear()
        {
            foreach (var kv in live) { kv.Value.Go.SetActive(false); pool.Push(kv.Value); }
            live.Clear();
        }

        void Units(Run r)
        {
            foreach (var kv in live) kv.Value.Seen = false;
            float now = r.Time, st = Step(now);
            foreach (var e in r.Enemies)
            {
                if (e.Dead) continue;
                var v = Get(e.Id);
                v.Seen = true;
                bool hide = e.Under;
                if (v.Go.activeSelf == hide) v.Go.SetActive(!hide);
                if (hide) continue;

                string mk;
                var model = !e.Boss && !e.Pod && FoeModels.TryGetValue(e.Type, out mk) ? Models.Model(mk, false, true, unitShader) : null;
                Mesh mesh = model != null ? model.Mesh : MeshFor(e);
                v.Clips = model != null ? model.Clips : null;
                if (v.Clips != null) mesh = FoeClip(v, e, now);
                if (v.Mf.sharedMesh != mesh) { v.Mf.sharedMesh = mesh; v.Mr.sharedMaterial = model != null ? model.Mat : unitMat; }

                float cx = e.Cx + e.Ox, cy = e.Cy + e.Oy, z = 0;
                float grow = 1;
                if (e.Entry > 0) { cy -= e.Entry * 1.5f; grow = Mathf.Lerp(1, 0.6f, Mathf.Clamp01(e.Entry)); }
                if (e.Flying) z = -0.45f - 0.08f * Mathf.Sin(Step(now * 0.6f) * 10 + e.Id);

                // stepped poses: a two-frame idle breath, a hop while stepping, a crouch while winding up
                float phase = Step(now + e.Id * 0.137f);
                bool pose2 = Mathf.Repeat(phase, 1f) >= 0.5f, posed = v.Clips == null;   // baked clips carry their own poses
                float sxy = pose2 && posed ? 1.03f : 1, sz = pose2 && posed ? 0.93f : 1;
                if (e.StepProg >= 0 && posed) { z -= 0.12f * Mathf.Sin(Mathf.Clamp01(e.StepProg) * Mathf.PI); }
                if (e.Atk != null && !e.Atk.Lunge && posed) { sxy *= 1.06f; sz *= 0.85f; }
                if (e.Atk != null && e.Atk.Lunge && posed) { sz *= 1.1f; }

                float fw = e.W * K * 0.94f, fh = e.H * K * 0.94f, fz = Mathf.Sqrt(e.W * e.H) * K * 0.94f;
                if (e.Boss && !e.Pod) { fw = e.W * K; fh = e.H * K; }
                v.Scale = new Vector3(fw * sxy * grow, fh * sxy * grow, fz * sz * grow);
                v.Go.transform.localPosition = P(cx, cy, z);
                v.Go.transform.localScale = v.Scale;
                v.Go.transform.localRotation = Quaternion.identity;
                v.Body.transform.localScale = model != null ? Fit(model, fw, fh) : Vector3.one;

                float tint = 1;
                Color tintCol = Color.white;
                if (e.Boss && e.Brain != null)
                {
                    if (e.Brain is Bellwarden && e.Brain.Phase != null) tintCol = new Color(1, 0.35f, 0.3f, 1);
                    var hk = e.Brain as Hivekeeper;
                    if (hk != null) tint = hk.Exposed ? 1.4f : 0.25f;
                    if (e.Brain is Mawking && e.Brain.Enraged) tintCol = Color.Lerp(Color.white, new Color(1, 0.3f, 0.3f, 1), 0.5f + 0.5f * Mathf.Sin(st * 8));
                }
                v.Mpb.SetFloat("_Flash", e.Flash > 0 ? 1 : 0);
                v.Mpb.SetFloat("_Ice", r.Has(e, "freeze") ? 1 : 0);
                v.Mpb.SetFloat("_Bleed", e.Bleed > 0 ? 1 : 0);
                v.Mpb.SetFloat("_Seed", (e.Id * 0.618f) % 1);
                v.Mpb.SetColor("_EmisTint", new Color(tintCol.r * tint, tintCol.g * tint, tintCol.b * tint, 1));
                v.Mr.SetPropertyBlock(v.Mpb);
                Plates(v, e);
            }

            // foes that left the list this frame shrink away in stepped frames (the core already spawns particles)
            gone.Clear();
            foreach (var kv in live)
            {
                var v = kv.Value;
                if (v.Seen) continue;
                if (v.DieT < 0) v.DieT = 0;
                v.DieT += Time.unscaledDeltaTime;
                Models.Clip death;
                if (v.Clips != null && v.Clips.TryGetValue("Death", out death))
                {
                    // the baked Death plays through (it shrinks on its own), then the unit is gone
                    if (v.DieT >= death.Length + 0.1f) { v.Go.SetActive(false); gone.Add(kv.Key); continue; }
                    v.Mf.sharedMesh = death.At(v.DieT);
                    v.Mpb.SetFloat("_Flash", 0);
                    v.Mr.SetPropertyBlock(v.Mpb);
                    continue;
                }
                float k = 1 - Step(v.DieT) / 0.3f;
                if (k <= 0) { v.Go.SetActive(false); gone.Add(kv.Key); continue; }
                v.Go.transform.localScale = new Vector3(v.Scale.x * k, v.Scale.y * k, v.Scale.z * k * k);
                v.Mpb.SetFloat("_Flash", 1 - k);
                v.Mr.SetPropertyBlock(v.Mpb);
            }
            foreach (var id in gone) { pool.Push(live[id]); live.Remove(id); }
        }

        /// <summary>
        /// The baked foe clip for this frame, stepped (no blending, style.md §6). Attack (a melee wind-up, or a shot due
        /// within the release time so the projectile leaves on the release frame) and Hit play to their end; Move while
        /// stepping or entering; Idle otherwise, its phase offset per foe.
        /// </summary>
        static Mesh FoeClip(View v, Enemy e, float now)
        {
            string want = "Idle";
            float at = now;
            bool shotDue = e.Def != null && e.Def.ShootEvery != null && e.ShootT > 0 && e.ShootT <= AttackRelease;
            if (e.Atk != null) want = "Attack";
            else if (shotDue) { want = "Attack"; at = now - (AttackRelease - e.ShootT); }
            else if (e.Flash > 0) want = "Hit";
            else if (e.StepProg >= 0 || e.Entry > 0) want = "Move";
            Models.Clip cur;
            bool busy = v.Clip != null && v.Clips.TryGetValue(v.Clip, out cur) && !cur.Loop && now - v.ClipAt < cur.Length;
            int pri = want == "Attack" ? 2 : want == "Hit" ? 1 : 0, curPri = v.Clip == "Attack" ? 2 : v.Clip == "Hit" ? 1 : 0;
            if (want != v.Clip && (!busy || pri > curPri))
            {
                v.Clip = want;
                v.ClipAt = want == "Idle" || want == "Move" ? -e.Id * 0.37f : at;
            }
            Models.Clip c;
            if (!v.Clips.TryGetValue(v.Clip, out c) && !v.Clips.TryGetValue("Idle", out c)) return v.Mf.sharedMesh;
            return c.At(now - v.ClipAt);
        }

        /// <summary>
        /// Body scale under a view scaled (fw, fh, fz) that makes a model fill its footprint: uniform where the model's
        /// proportions allow, stretched along x only for the 2-wide foes (the models are ~1:1, the hitbox is 2:1).
        /// </summary>
        static Vector3 Fit(Models.Asset m, float fw, float fh)
        {
            float fz = Mathf.Sqrt(fw * fh);
            float s = Mathf.Min(fw / (2 * m.Half.x), fh / (2 * m.Half.y));
            float sx = fw > fh * 1.5f ? fw / (2 * m.Half.x) : s;
            return new Vector3(sx / fw, s / fh, s / fz);
        }

        static Mesh MeshFor(Enemy e)
        {
            if (e.Pod) return Models.Foe("pod", 0, false);
            if (e.Boss)
            {
                if (e.Brain is Bellwarden) return Models.Bell();
                if (e.Brain is Hivekeeper) return Models.Hive();
                if (e.Brain is Mawking) return Models.Maw();
                return Models.Foe("boss", e.Color, true);
            }
            return Models.Foe(e.Type, e.Def.Color, e.Def.Elite);
        }

        /// <summary>Shield plates of foes, and the Mawking's rotating plates, as unit-space children.</summary>
        void Plates(View v, Enemy e)
        {
            bool l, b, rgt;
            float th;
            var mk = e.Brain as Mawking;
            if (mk != null) { l = mk.Plates[0]; b = mk.Plates[1]; rgt = mk.Plates[2]; th = 0.35f; }
            else { l = e.ShieldL; b = e.ShieldF; rgt = e.ShieldR; th = 0.14f; }
            float tw = th / Mathf.Max(0.1f, e.W), tht = th / Mathf.Max(0.1f, e.H);
            Plate(v.Plates[0], l, new Vector3(-0.5f - tw / 2, 0, 0), new Vector3(tw, 0.8f, 0.6f));
            Plate(v.Plates[1], b, new Vector3(0, -0.5f - tht / 2, 0), new Vector3(0.8f, tht, 0.6f));
            Plate(v.Plates[2], rgt, new Vector3(0.5f + tw / 2, 0, 0), new Vector3(tw, 0.8f, 0.6f));
        }

        static void Plate(GameObject p, bool on, Vector3 pos, Vector3 size)
        {
            if (p.activeSelf != on) p.SetActive(on);
            if (!on) return;
            p.transform.localPosition = pos;
            p.transform.localScale = size;
        }

        void Hero(Run r)
        {
            if (hero == null) { hero = NewView(); hero.Go.name = "hero"; }
            if (!hero.Go.activeSelf) hero.Go.SetActive(true);   // stays down on the game-over screen, holding Death
            if (heroMesh != r.Hero.Id)
            {
                string mk;
                var model = HeroModels.TryGetValue(r.Hero.Id, out mk) ? Models.Model(mk, true, false, unitShader) : null;
                heroModel = model;
                hero.Mf.sharedMesh = model != null ? model.Mesh : Models.Hero(r.Hero.Id, r.Hero.Color);
                hero.Mr.sharedMaterial = model != null ? model.Mat : unitMat;
                // the hero is drawn 1.8 cells tall at 7x9, scaled by its height only; the hit circle (P.R) stays gameplay-only
                heroK = model != null ? 1.8f / (r.P.R * 2 * K * 1.15f * model.Half.z) : 1;
                heroMesh = r.Hero.Id;
            }
            var p = r.P;
            float st = Step(r.Time);
            float drive = Mathf.Sqrt(r.InputX * r.InputX + r.InputY * r.InputY);
            bool moving = drive > 0;
            bool animated = heroModel != null && heroModel.Clips != null && HeroAnimate(r, drive);
            // the procedural stand-in (no baked clips) bobs and leans instead
            float bob = animated ? 0 : moving ? 0.06f * Mathf.Abs(Mathf.Sin(st * 9)) : (Mathf.Repeat(st, 1) >= 0.5f ? 0.03f : 0);
            float size = p.R * 2 * K * 1.15f;
            hero.Go.transform.localPosition = P(p.X, p.Y + HeroBackCells * Run.CELL * HeroGrow, -bob);
            hero.Go.transform.localScale = new Vector3(size, size, size);
            float hk = heroK * (heroModel != null ? HeroGrow : 1);
            hero.Body.transform.localScale = new Vector3(hk, hk, hk);
            // faces its aim; leans a little into the move
            float lean = moving && !animated ? 12 : 0;
            hero.Go.transform.localRotation = Quaternion.Euler(-r.InputY * lean, r.InputX * lean, Mathf.Round((p.Aim - 90) / 7.5f) * 7.5f);
            hero.Mpb.SetFloat("_Flash", p.HurtT > 0 ? 0.6f : 0);
            hero.Mpb.SetFloat("_Ice", p.RootT > 0 ? 1 : 0);
            hero.Mpb.SetFloat("_Bleed", 0);
            hero.Mpb.SetFloat("_Seed", 0.3f);
            hero.Mpb.SetColor("_EmisTint", Color.white);
            hero.Mr.SetPropertyBlock(hero.Mpb);
        }

        /// <summary>
        /// Picks the hero's clip (Death on dying and game over, Victory once the final boss falls, Hit for a moment after
        /// each hurt, Run while moving (Walk for a short joystick push, or when there is no Run), Shoot while a volley is
        /// out and the hero stands, else Idle) and shows its stepped frame. The clip
        /// clock follows the sim's time, so pauses and game speed carry over, and real time once the run is over.
        /// </summary>
        /// <summary>The hero's drawn bounds in world units (1 unit = 1 cell), for the size check in PitCapture.</summary>
        public Bounds HeroBounds { get { return hero != null ? hero.Mr.bounds : new Bounds(); } }

        bool HeroAnimate(Run r, float drive)
        {
            var clips = heroModel.Clips;
            if (r != heroRun) { heroRun = r; heroT = 0; heroLastTime = r.Time; heroClip = null; heroHitAt = -9; heroLastHurt = 0; }
            heroT += r.IsOver ? Time.unscaledDeltaTime : Mathf.Max(0, r.Time - heroLastTime);
            heroLastTime = r.Time;
            var p = r.P;
            if (p.HurtT > heroLastHurt) heroHitAt = heroT;   // HurtT is reset to its full length on every hit
            heroLastHurt = p.HurtT;

            Models.Clip c;
            string want =
                r.Mode == Mode.Dying || r.Mode == Mode.GameOver ? "Death" :
                r.Mode == Mode.Victory || r.Mode == Mode.Clearing && r.Won ? "Victory" :
                clips.TryGetValue("Hit", out c) && heroT - heroHitAt < c.Length ? "Hit" :
                drive > 0 ? (drive >= 0.5f && clips.ContainsKey("Run") ? "Run" : "Walk") :
                r.Volley != null ? "Shoot" : "Idle";
            if (!clips.TryGetValue(want, out c)) { want = "Idle"; if (!clips.TryGetValue(want, out c)) return false; }
            if (want == "Hit" && heroClip == "Hit" && heroHitAt > heroClipAt) heroClipAt = heroHitAt;   // hit again: restart
            if (want != heroClip) { heroClip = want; heroClipAt = want == "Hit" ? heroHitAt : heroT; }
            // Shoot is a one-shot recoil: a long volley repeats it
            if (want == "Shoot" && heroT - heroClipAt >= c.Length) heroClipAt = heroT;
            hero.Mf.sharedMesh = c.At(heroT - heroClipAt);
            return true;
        }
    }
}
