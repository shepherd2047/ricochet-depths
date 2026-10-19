// The 3D presentation of the board (style.md §1–§6). The simulation stays 2D: Board3D only mirrors it.
//   • A perspective camera looks down into the pit at 30° from straight-on, from behind and above the hero. Each frame
//     it is fitted so the whole board (7 columns plus half a cell each side) sits between the HUD bands, so it eases
//     back by itself while the pit widens over 3 s. Touches are cast back onto the board plane (ScreenToBoard).
//   • The 2D World/Glow layers of Gfx are parented to WorldRoot, which lays design space flat onto the board plane.
//   • One white key light with soft shadows, a trilight ambient, linear fog in the background colour, and Bloom whose
//     threshold only the over-bright Glow layer and emissive eyes reach.
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
        public const float Pitch = 30;                           // degrees away from looking straight at the plane
        const float Margin = 0.5f;                               // world units of floor kept visible past each side
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
        internal static readonly Dictionary<string, string> HeroModels = new Dictionary<string, string>
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
        }

        readonly Dictionary<int, View> live = new Dictionary<int, View>();
        readonly Stack<View> pool = new Stack<View>();
        readonly List<int> gone = new List<int>();
        View hero;
        string heroMesh;

        // camera basis and lens of the last fitted frame, for ScreenToBoard
        Vector3 camPos, camRight = Vector3.right, camUp = Vector3.up, camFwd = Vector3.forward;
        float tanV, tanH, viewW = 1, viewH = 1;

        /// <summary>Camera distance from its aim point on the board, last frame (world units).</summary>
        public float Distance { get; private set; }

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
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 30;
            RenderSettings.fogEndDistance = 50;
            RenderSettings.fogColor = FogColor;
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
                previewRoot.localPosition = new Vector3(0, 0, -500);   // far above the pit, out of the board camera's view
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
            // models face +y; turn to the camera, sway ±25° at the stepped animation rate
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
            }
            if (gate != null && gate.gameObject.activeSelf)
                foreach (var pc in gatePieces)
                    if (pc.sharedMesh != null) f(pc.sharedMesh, c + (gate.localPosition + pc.transform.localPosition) * K, pc.transform.localScale * K, 0);
            foreach (var kv in live) if (kv.Value.Go.activeSelf) f(kv.Value.Mf.sharedMesh, kv.Value.Go.transform.localPosition, Vector3.Scale(kv.Value.Go.transform.localScale, kv.Value.Body.transform.localScale), 0);
            if (hero != null && hero.Go.activeSelf) f(hero.Mf.sharedMesh, hero.Go.transform.localPosition, Vector3.Scale(hero.Go.transform.localScale, hero.Body.transform.localScale), Mathf.Round((r.P.Aim - 90) / 7.5f) * 7.5f);
        }

        // ------------------------------------------------------------ camera fit and picking
        /// <summary>
        /// Fits the pitched camera: the smallest distance that shows every board corner (plus Margin) inside the screen
        /// width and between the HUD bands, centred in that band. The near row is the widest on screen, so the far row's
        /// 7 columns fit with room to spare. At 9:16 and FOV 65 this lands at about 14.7 units for 7x9 (style.md §2: ~15).
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
            float w = r.Bw * K, h = r.Bh * K;
            float bandTop = 1 - 2 * wv.Top / g.H, bandBot = 1 - 2 * wv.Bottom / g.H, band = bandTop - bandBot;
            float aimY = -h / 2, d = 11;
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
            Distance = d;
            camPos = new Vector3(w / 2, aimY, 0) - camFwd * d;
        }

        bool Fits(Vector3 pos, float w, float h, float band)
        {
            var a = Ndc(pos, new Vector3(-Margin, Margin, 0)); var b = Ndc(pos, new Vector3(w + Margin, Margin, 0));
            var c = Ndc(pos, new Vector3(-Margin, -h - Margin, 0)); var e = Ndc(pos, new Vector3(w + Margin, -h - Margin, 0));
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
        // k −16 is redrawn 9 groups further on, out in the fog.
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
            public MeshFilter L, R, F;
            public string Ln, Rn;
            public bool B;
        }

        readonly Transform corridor;
        readonly Material sceneMat;
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
                gr.L.transform.localPosition = new Vector3(-r.Bw / 2, 0, 0);
                gr.R.transform.localPosition = new Vector3(r.Bw / 2, 0, 0);
                bool vis = kBase + 4 * G < rows / 2 + 12;   // fully fogged beyond (ASSEMBLY §4.6)
                if (gr.T.gameObject.activeSelf != vis) gr.T.gameObject.SetActive(vis);
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
                gatePieces[0].transform.localPosition = new Vector3(-(r.Bw / 2 + 1.2f), 0, 0);
                gatePieces[1].transform.localPosition = new Vector3(r.Bw / 2 + 1.2f, 0, 0);
                gatePieces[2].transform.localScale = new Vector3((r.Bw + 2.4f) / 17f, 1, 1);
            }

            // LIGHTING.md: fog follows the camera distance, the copper spot follows the board size
            RenderSettings.fogStartDistance = Distance + 2 * K;
            RenderSettings.fogEndDistance = Distance + 14 * K;
            var centre = new Vector3(r.Bw / 2 * K, -r.Bh / 2 * K, 0);
            var sp = centre + Kit(-(r.Bw / 2) - 0.5f, (rows / 2 + 3) * C, 6);
            var aim = centre + Kit(r.Bw / 2, 0.15f * rows * C, 1.5f);
            spot.transform.localPosition = sp;
            spot.transform.localRotation = Quaternion.LookRotation(aim - sp, new Vector3(0, 0, -1));

            // spec §4.4: the far wall is the farthest floor line on screen, deep in the fog
            var top = ScreenToBoard(viewW / 2, 0);
            r.FarY = top.x > -998 ? Mathf.Min(top.y, -C) : -6 * C;
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
            return gr;
        }

        MeshFilter Piece(string mesh, Transform parent)
        {
            MeshFilter mf;
            var go = MakeObject(mesh ?? "piece", parent, mesh != null ? Models.Scenery(mesh) : null, out mf);
            go.GetComponent<MeshRenderer>().sharedMaterial = sceneMat;
            return mf;
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
            gr.L.sharedMesh = Models.Scenery(gr.Ln);
            gr.R.sharedMesh = Models.Scenery(gr.Rn);
            gr.B = Hash((uint)r.Seed, (uint)G, 7) % 3 == 0;
            Strip(gr, stripCols);
            gr.T.localPosition = new Vector3(0, (4 * G + 1.5f) * Run.CELL, 0);
        }

        static void Strip(Group gr, int cols) { gr.F.sharedMesh = Models.Scenery("T1_FloorStrip_W" + cols + (gr.B ? "_B" : "_A")); }

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

        // ------------------------------------------------------------ units
        View Get(int id)
        {
            View v;
            if (live.TryGetValue(id, out v)) return v;
            v = pool.Count > 0 ? pool.Pop() : NewView();
            v.Id = id; v.DieT = -1;
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
                if (v.Mf.sharedMesh != mesh) { v.Mf.sharedMesh = mesh; v.Mr.sharedMaterial = model != null ? model.Mat : unitMat; }

                float cx = e.Cx + e.Ox, cy = e.Cy + e.Oy, z = 0;
                float grow = 1;
                if (e.Entry > 0) { cy -= e.Entry * 1.5f; grow = Mathf.Lerp(1, 0.6f, Mathf.Clamp01(e.Entry)); }
                if (e.Flying) z = -0.45f - 0.08f * Mathf.Sin(Step(now * 0.6f) * 10 + e.Id);

                // stepped poses: a two-frame idle breath, a hop while stepping, a crouch while winding up
                float phase = Step(now + e.Id * 0.137f);
                bool pose2 = Mathf.Repeat(phase, 1f) >= 0.5f;
                float sxy = pose2 ? 1.03f : 1, sz = pose2 ? 0.93f : 1;
                if (e.StepProg >= 0) { z -= 0.12f * Mathf.Sin(Mathf.Clamp01(e.StepProg) * Mathf.PI); }
                if (e.Atk != null && !e.Atk.Lunge) { sxy *= 1.06f; sz *= 0.85f; }
                if (e.Atk != null && e.Atk.Lunge) { sz *= 1.1f; }

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
                float k = 1 - Step(v.DieT) / 0.3f;
                if (k <= 0) { v.Go.SetActive(false); gone.Add(kv.Key); continue; }
                v.Go.transform.localScale = new Vector3(v.Scale.x * k, v.Scale.y * k, v.Scale.z * k * k);
                v.Mpb.SetFloat("_Flash", 1 - k);
                v.Mr.SetPropertyBlock(v.Mpb);
            }
            foreach (var id in gone) { pool.Push(live[id]); live.Remove(id); }
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
            bool show = r.Mode != Mode.GameOver;
            if (hero.Go.activeSelf != show) hero.Go.SetActive(show);
            if (!show) return;
            if (heroMesh != r.Hero.Id)
            {
                string mk;
                var model = HeroModels.TryGetValue(r.Hero.Id, out mk) ? Models.Model(mk, true, false, unitShader) : null;
                hero.Mf.sharedMesh = model != null ? model.Mesh : Models.Hero(r.Hero.Id, r.Hero.Color);
                hero.Mr.sharedMaterial = model != null ? model.Mat : unitMat;
                // a model fills the hero's circle, but stands no taller than one cell
                float k = model != null ? Mathf.Min(1 / (2 * Mathf.Max(model.Half.x, model.Half.y)), 1 / (r.P.R * 2 * K * 1.15f * model.Half.z)) : 1;
                hero.Body.transform.localScale = new Vector3(k, k, k);
                heroMesh = r.Hero.Id;
            }
            var p = r.P;
            float st = Step(r.Time);
            bool moving = r.InputX != 0 || r.InputY != 0;
            float bob = moving ? 0.06f * Mathf.Abs(Mathf.Sin(st * 9)) : (Mathf.Repeat(st, 1) >= 0.5f ? 0.03f : 0);
            float size = p.R * 2 * K * 1.15f;
            hero.Go.transform.localPosition = P(p.X, p.Y, -bob);
            hero.Go.transform.localScale = new Vector3(size, size, size);
            // faces its aim; leans a little into the move
            float lean = moving ? 12 : 0;
            hero.Go.transform.localRotation = Quaternion.Euler(-r.InputY * lean, r.InputX * lean, Mathf.Round((p.Aim - 90) / 7.5f) * 7.5f);
            hero.Mpb.SetFloat("_Flash", p.HurtT > 0 ? 0.6f : 0);
            hero.Mpb.SetFloat("_Ice", p.RootT > 0 ? 1 : 0);
            hero.Mpb.SetFloat("_Bleed", 0);
            hero.Mpb.SetFloat("_Seed", 0.3f);
            hero.Mpb.SetColor("_EmisTint", Color.white);
            hero.Mr.SetPropertyBlock(hero.Mpb);
        }
    }
}
