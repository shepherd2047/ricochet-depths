// Ricochet Depths VFX entry point. The game calls the static methods below; everything is pooled.
//
//   VfxBank.Ball("ember", ballTransform)        sprite + halo + element trail that follows the ball
//   VfxBank.StopBall(handle)                     ball gone: the trail fades out, then the handle returns to the pool
//   VfxBank.Hit("frost", pos, normal)            hit effect at a contact point (normal = bounce normal on the board)
//   VfxBank.Flash(enemy, "spark")                white hit flash + element tint on the enemy's renderers
//   VfxBank.Status(enemy, VfxElement.Ember, on)  burn flicker / freeze shell (+ shatter on off) / shock flicker
//   VfxBank.Chain(from, to)                      one jagged chain-lightning arc
//   VfxBank.RowBeam(rowY, xMin, xMax, targets)   horizontal row laser: charge, beam, fade, a spark on each target
//
// Board convention is pit-unity's Board3D: the board is the world XY plane (x right, y toward the far wall) and "up"
// out of the floor is -Z. Set VfxBank.Instance.up if your board differs.
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RicochetVfx
{
    [DefaultExecutionOrder(1000)]
    public sealed class VfxBank : MonoBehaviour
    {
        public static VfxBank Instance { get; private set; }

        public VfxBankAsset bank;
        [Tooltip("Camera the ribbons (trails, bolts, beam) face. Empty = Camera.main.")]
        public Camera viewCamera;
        [Tooltip("Out of the board floor. pit-unity Board3D: (0, 0, -1).")]
        public Vector3 up = new Vector3(0, 0, -1);
        [Tooltip("World units per board cell. Scales every effect.")]
        public float scale = 1f;
        [Tooltip("Height of the row beam above the floor, in cells.")]
        public float beamHeight = 0.45f;
        [Tooltip("Editor capture: VfxBank does not tick itself; call Tick(dt).")]
        public bool manualTick;

        public static Vector3 Up { get { return Instance != null ? Instance.up.normalized : new Vector3(0, 0, -1); } }
        public static float Scale { get { return Instance != null ? Instance.scale : 1f; } }
        public static float Now { get; private set; }

        public static Vector3 CamPos
        {
            get
            {
                var c = Instance != null && Instance.viewCamera != null ? Instance.viewCamera : Camera.main;
                return c != null ? c.transform.position : Vector3.zero;
            }
        }

        static readonly int VfxTimeId = Shader.PropertyToID("_VfxTime");

        readonly Dictionary<GameObject, Stack<VfxBehaviour>> pools = new Dictionary<GameObject, Stack<VfxBehaviour>>();
        readonly List<VfxBehaviour> live = new List<VfxBehaviour>();
        readonly List<VfxBehaviour> scratch = new List<VfxBehaviour>();
        readonly List<VfxHitFeedback> feedback = new List<VfxHitFeedback>();
        readonly List<KeyValuePair<float, Action>> delays = new List<KeyValuePair<float, Action>>();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            if (bank == null) bank = Resources.Load<VfxBankAsset>("RicochetVfx/VfxBank");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!manualTick) Tick(Time.deltaTime);
        }

        /// <summary>The bank in the scene, or a new one loading Resources/RicochetVfx/VfxBank.</summary>
        public static VfxBank Ensure()
        {
            if (Instance != null) return Instance;
            var found = FindAnyObjectByType<VfxBank>();
            if (found != null) { found.Awake(); return found; }
            var go = new GameObject("VfxBank");
            var b = go.AddComponent<VfxBank>();
            if (Instance == null) b.Awake();    // Awake does not run in edit mode
            return b;
        }

        // ---------------------------------------------------------------- game API

        /// <summary>Ball sprite, halo and element trail following <paramref name="ball"/>. Null for unknown ids.</summary>
        public static VfxBall Ball(string ballId, Transform ball)
        {
            VfxElement e;
            if (!VfxElements.TryParse(ballId, out e)) return null;
            var b = Spawn<VfxBall>(Ensure().bank.Get(e).ball, ball.position, Quaternion.identity);
            b.Follow(ball);
            return b;
        }

        /// <summary>Same as Ball: the trail always comes with the sprite.</summary>
        public static VfxBall Trail(string ballId, Transform ball)
        {
            return Ball(ballId, ball);
        }

        public static void StopBall(VfxBall b)
        {
            if (b != null && b.live) b.Detach();
        }

        public static void Hit(string ballId, Vector3 pos, Vector3 normal)
        {
            VfxElement e;
            if (VfxElements.TryParse(ballId, out e)) Hit(e, pos, normal);
        }

        public static void Hit(VfxElement e, Vector3 pos, Vector3 normal)
        {
            Spawn<VfxBehaviour>(Ensure().bank.Get(e).hit, pos, VfxFrame(normal));
        }

        /// <summary>White hit flash plus the element tint on every renderer under <paramref name="enemy"/>.</summary>
        public static void Flash(GameObject enemy, string ballId)
        {
            VfxElement e;
            if (enemy != null && VfxElements.TryParse(ballId, out e)) Feedback(enemy).Flash(e);
        }

        public static void Status(GameObject enemy, string ballId, bool on)
        {
            VfxElement e;
            if (enemy != null && VfxElements.TryParse(ballId, out e)) Status(enemy, e, on);
        }

        /// <summary>Ember: burn. Rime: freeze shell; turning it off shatters it. Spark: shock flicker. Ray: none.</summary>
        public static void Status(GameObject enemy, VfxElement e, bool on)
        {
            if (enemy != null) Feedback(enemy).SetStatus(e, on);
        }

        /// <summary>Ends every status on the enemy (call before destroying it). A frozen enemy shatters.</summary>
        public static void ClearStatus(GameObject enemy)
        {
            if (enemy == null) return;
            var hf = enemy.GetComponent<VfxHitFeedback>();
            if (hf != null) { hf.Init(); hf.ClearAll(); }
        }

        public static VfxBolt Chain(Vector3 from, Vector3 to)
        {
            var b = Spawn<VfxBolt>(Ensure().bank.chainBolt, to, Quaternion.identity);
            b.Fire(from, to);
            return b;
        }

        /// <summary>A chain through several points, one jump every <paramref name="stepDelay"/> seconds.</summary>
        public static void Chain(IList<Vector3> points, float stepDelay = 0.07f)
        {
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Vector3 a = points[i], b = points[i + 1];
                if (i == 0) Chain(a, b); else Delay(i * stepDelay, () => Chain(a, b));
            }
        }

        /// <summary>
        /// Row laser across the board at world y = <paramref name="rowY"/> from x = xMin to xMax, lifted beamHeight above
        /// the floor. <paramref name="originX"/> is where the ball hit (the charge flare); NaN = the middle.
        /// </summary>
        public static VfxRowBeam RowBeam(float rowY, float xMin, float xMax, IList<Vector3> targets = null, float originX = float.NaN)
        {
            var I = Ensure();
            var lift = Up * I.beamHeight * I.scale;
            if (float.IsNaN(originX)) originX = (xMin + xMax) / 2;
            var a = new Vector3(xMin, rowY, 0) + lift;
            var b = new Vector3(xMax, rowY, 0) + lift;
            var o = new Vector3(originX, rowY, 0) + lift;
            return RowBeam(a, b, o, targets);
        }

        /// <summary>Row laser between two world points (general form).</summary>
        public static VfxRowBeam RowBeam(Vector3 from, Vector3 to, Vector3 origin, IList<Vector3> targets = null)
        {
            var beam = Spawn<VfxRowBeam>(Ensure().bank.rowBeam, origin, VfxFrame(to - from));
            beam.Fire(from, to, origin, targets);
            return beam;
        }

        /// <summary>Runs <paramref name="a"/> after <paramref name="seconds"/> of VFX time.</summary>
        public static void Delay(float seconds, Action a)
        {
            Ensure().delays.Add(new KeyValuePair<float, Action>(Now + seconds, a));
        }

        // ---------------------------------------------------------------- clock and pool

        /// <summary>Advances every live effect. Called from Update unless manualTick is set.</summary>
        public void Tick(float dt)
        {
            Now += dt;
            Shader.SetGlobalFloat(VfxTimeId, Now);

            for (int i = 0; i < delays.Count; i++)
                if (delays[i].Key <= Now) { var a = delays[i].Value; delays.RemoveAt(i--); a(); }

            scratch.Clear();
            scratch.AddRange(live);
            foreach (var b in scratch)
            {
                if (b == null) { live.Remove(b); continue; }
                if (!b.live) continue;
                b.age += dt;
                b.Step(dt);
            }

            if (manualTick)
                foreach (var b in live)
                {
                    if (b == null || !b.live) continue;
                    foreach (var ps in b.systems)
                        if (ps != null && ps.gameObject.activeInHierarchy) ps.Simulate(dt, false, false, false);
                }

            for (int i = feedback.Count - 1; i >= 0; i--)
            {
                if (feedback[i] == null) { feedback.RemoveAt(i); continue; }
                feedback[i].Step(dt);
            }
        }

        public static T Spawn<T>(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent = null) where T : VfxBehaviour
        {
            var I = Ensure();
            Stack<VfxBehaviour> pool;
            if (!I.pools.TryGetValue(prefab, out pool)) I.pools[prefab] = pool = new Stack<VfxBehaviour>();
            VfxBehaviour b = null;
            while (b == null && pool.Count > 0) b = pool.Pop();
            if (b == null)
            {
                var go = Instantiate(prefab, I.transform);
                go.name = prefab.name;
                b = go.GetComponent<VfxBehaviour>();
                b.prefab = prefab;
                b.CacheSystems();
            }
            var t = b.transform;
            t.SetParent(parent != null ? parent : I.transform, false);
            t.SetPositionAndRotation(pos, rot);
            var s = prefab.transform.localScale * I.scale;
            if (parent != null) s = new Vector3(s.x / parent.lossyScale.x, s.y / parent.lossyScale.y, s.z / parent.lossyScale.z);
            t.localScale = s;
            b.gameObject.SetActive(true);
            b.age = 0;
            b.live = true;
            b.ResetEmission();
            foreach (var ps in b.systems)
            {
                ps.Clear(false);
                var g = ps.GetComponent<VfxGravity>();
                if (g != null) g.Apply(ps);
                if (I.manualTick) ps.Simulate(0, false, true, false);
                else ps.Play(false);
            }
            I.live.Add(b);
            b.OnSpawn();
            return (T)b;
        }

        public static void Release(VfxBehaviour b)
        {
            if (b == null || !b.live) return;
            var I = Ensure();
            b.live = false;
            b.OnRelease();
            I.live.Remove(b);
            foreach (var ps in b.systems) if (ps != null) ps.Clear(false);
            b.gameObject.SetActive(false);
            b.transform.SetParent(I.transform, false);
            Stack<VfxBehaviour> pool;
            if (b.prefab != null && I.pools.TryGetValue(b.prefab, out pool)) pool.Push(b);
            else Kill(b.gameObject);
        }

        /// <summary>Destroy that also works in the editor capture (edit mode).</summary>
        public static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Releases every live effect and clears pending delays (scene reset, capture restarts).</summary>
        public static void ReleaseAll()
        {
            var I = Ensure();
            I.delays.Clear();
            I.scratch.Clear();
            I.scratch.AddRange(I.live);
            foreach (var b in I.scratch) if (b != null) Release(b);
            I.live.Clear();
        }

        internal static void Register(VfxHitFeedback f)
        {
            var I = Ensure();
            if (!I.feedback.Contains(f)) I.feedback.Add(f);
        }

        internal static void Unregister(VfxHitFeedback f)
        {
            if (Instance != null) Instance.feedback.Remove(f);
        }

        static VfxHitFeedback Feedback(GameObject enemy)
        {
            var hf = enemy.GetComponent<VfxHitFeedback>();
            if (hf == null) hf = enemy.AddComponent<VfxHitFeedback>();
            hf.Init();
            return hf;
        }

        /// <summary>Local +Y = up out of the floor, local +Z = <paramref name="forward"/> projected on the board.</summary>
        public static Quaternion VfxFrame(Vector3 forward)
        {
            var u = Up;
            forward = Vector3.ProjectOnPlane(forward, u);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(Vector3.right, u);
            return Quaternion.LookRotation(forward.normalized, u);
        }
    }
}
