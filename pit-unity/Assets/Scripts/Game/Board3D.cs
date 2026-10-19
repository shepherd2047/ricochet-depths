// The 3D presentation of the board (style.md §1–§6). The simulation stays 2D: Board3D only mirrors it.
//   • A perspective camera looks down into the pit at 30° from straight-on, from behind and above the hero. Each frame
//     it is fitted so the whole board (7 columns plus half a cell each side) sits between the HUD bands, so it eases
//     back by itself while the pit widens over 3 s. Touches are cast back onto the board plane (ScreenToBoard).
//   • The 2D World/Glow layers of Gfx are parented to WorldRoot, which lays design space flat onto the board plane.
//   • One white key light with soft shadows, a trilight ambient, linear fog in the background colour, and Bloom whose
//     threshold only the over-bright Glow layer and emissive eyes reach.
//   • Low-poly floor tiles, modular walls and broken pillars sinking into the fog; one pooled view per foe, boss and
//     the hero, animated at a stepped 10 fps.
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
        readonly Mesh[] wallMods;
        readonly GameObject floor;
        readonly MeshFilter floorMf;
        readonly List<GameObject> walls = new List<GameObject>();
        int floorCols = -1, floorRows = -1;
        Run lastRun;

        sealed class View
        {
            public GameObject Go;
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
            Cam.cullingMask = ~(1 << Gfx.UiLayer);
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
            lg.transform.localRotation = Quaternion.Euler(-10, 0, 0);  // down into the pit, 10° toward the far wall, so shadows fall away from the camera
            var light = lg.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.85f;

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
            unitMat = new Material(sh) { name = "pit-unit" };

            board = new GameObject("Pit").transform;
            board.SetParent(root, false);
            units = new GameObject("Units").transform;
            units.SetParent(board, false);
            floor = MakeObject("Floor", board, null, out floorMf);
            var abyss = Models.Abyss(9, 14);
            MeshFilter amf;
            MakeObject("Abyss", board, abyss, out amf).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wallMods = Models.WallModules();
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
            if (board.gameObject.activeSelf) board.gameObject.SetActive(false);
            Cam.transform.localPosition = new Vector3(0, 0, -10);
            Cam.transform.localRotation = Quaternion.identity;
        }

        public void Frame(Run r, WorldView wv, Gfx g)
        {
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

        // ------------------------------------------------------------ test hooks (Tools/StubCheck renders frames with these)
        internal Vector3 CamPos { get { return camPos; } }
        internal Vector3 CamFwd { get { return camFwd; } }
        internal Vector3 CamUp { get { return camUp; } }
        internal float TanV { get { return tanV; } }

        /// <summary>Visits every visible mesh: mesh, position, scale, rotation about z in degrees (parents sit at the origin).</summary>
        internal void DebugVisit(System.Action<Mesh, Vector3, Vector3, float> f, Run r)
        {
            if (!board.gameObject.activeSelf) return;
            f(floorMf.sharedMesh, Vector3.zero, new Vector3(1, 1, 1), 0);
            foreach (var w in walls) if (w.activeSelf) f(w.GetComponent<MeshFilter>().sharedMesh, w.transform.localPosition, new Vector3(1, 1, 1), 0);
            foreach (var kv in live) if (kv.Value.Go.activeSelf) f(kv.Value.Mf.sharedMesh, kv.Value.Go.transform.localPosition, kv.Value.Go.transform.localScale, 0);
            if (hero != null && hero.Go.activeSelf) f(hero.Mf.sharedMesh, hero.Go.transform.localPosition, hero.Go.transform.localScale, Mathf.Round((r.P.Aim - 90) / 7.5f) * 7.5f);
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

        void Scenery(Run r)
        {
            int cols = Mathf.CeilToInt(r.Bw / Run.CELL - 0.01f), rows = Mathf.CeilToInt(r.Bh / Run.CELL - 0.01f);
            if (cols != floorCols || rows != floorRows)
            {
                floorCols = cols; floorRows = rows;
                floorMf.sharedMesh = Models.Floor(cols, rows);
            }
            floor.transform.localPosition = Vector3.zero;
            // modular walls follow the board edges every frame, so they slide out smoothly while the pit expands
            float w = r.Bw * K, h = r.Bh * K;
            int n = 0;
            for (int y = -1; y <= rows; y++) { Wall(n++, -0.5f, -(y + 0.5f), y * 7 + 1); Wall(n++, w + 0.5f, -(y + 0.5f), y * 5 + 3); }
            for (int x = 0; x < cols; x++) { Wall(n++, x + 0.5f, 0.5f, x * 3 + 2); Wall(n++, x + 0.5f, -h - 0.5f, x * 11 + 5); }
            for (int i = n; i < walls.Count; i++) if (walls[i].activeSelf) walls[i].SetActive(false);
        }

        void Wall(int i, float x, float y, int variant)
        {
            if (i >= walls.Count)
            {
                MeshFilter mf;
                walls.Add(MakeObject("wall", board, null, out mf));
            }
            var go = walls[i];
            if (!go.activeSelf) go.SetActive(true);
            var mesh = wallMods[((variant % 8) + 8) % 8];
            var f = go.GetComponent<MeshFilter>();
            if (f.sharedMesh != mesh) f.sharedMesh = mesh;
            go.transform.localPosition = new Vector3(x, y, 0);
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
            v.Go = MakeObject("unit", units, null, out v.Mf);
            v.Mr = v.Go.GetComponent<MeshRenderer>();
            v.Mpb = new MaterialPropertyBlock();
            v.Plates = new GameObject[3];
            for (int i = 0; i < 3; i++)
            {
                MeshFilter mf;
                v.Plates[i] = MakeObject("plate", v.Go.transform, Models.Plate(), out mf);
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

                Mesh mesh = MeshFor(e);
                if (v.Mf.sharedMesh != mesh) v.Mf.sharedMesh = mesh;

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
            if (heroMesh != r.Hero.Id) { hero.Mf.sharedMesh = Models.Hero(r.Hero.Id, r.Hero.Color); heroMesh = r.Hero.Id; }
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
