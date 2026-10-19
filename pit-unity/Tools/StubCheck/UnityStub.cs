// Minimal signatures of the UnityEngine (and URP) API used by Assets/Scripts/Game, written from memory of the public
// API. Functional where the smoke test needs it (maths, meshes, components); empty elsewhere.
#pragma warning disable
using System.Collections.Generic;
namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
#if PIT_URP
    public class VolumeComponent : ScriptableObject { }
    public class VolumeParameter<T> { public T value; public bool overrideState; public void Override(T v) { value = v; overrideState = true; } }
    public sealed class MinFloatParameter : VolumeParameter<float> { }
    public sealed class ClampedFloatParameter : VolumeParameter<float> { }
    public sealed class BoolParameter : VolumeParameter<bool> { }
    public sealed class VolumeProfile : ScriptableObject
    {
        public readonly List<VolumeComponent> components = new List<VolumeComponent>();
        public T Add<T>(bool overrides = false) where T : VolumeComponent { var c = ScriptableObject.CreateInstance<T>(); components.Add(c); return c; }
    }
    public sealed class Volume : MonoBehaviour { public bool isGlobal; public float priority; public VolumeProfile sharedProfile; }
#endif
}
#if PIT_URP
namespace UnityEngine.Rendering.Universal
{
    public enum CameraRenderType { Base, Overlay }
    public enum AntialiasingMode { None, FastApproximateAntialiasing, SubpixelMorphologicalAntiAliasing }
    public sealed class UniversalAdditionalCameraData : MonoBehaviour
    {
        public CameraRenderType renderType; public bool renderPostProcessing, renderShadows; public AntialiasingMode antialiasing;
        public readonly List<Camera> cameraStack = new List<Camera>();
    }
    public static class CameraExtensions
    {
        public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera c)
        {
            var d = c.gameObject.GetComponent<UniversalAdditionalCameraData>();
            return d != null ? d : c.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }
    }
    public sealed class Bloom : VolumeComponent
    {
        public readonly MinFloatParameter threshold = new MinFloatParameter(), intensity = new MinFloatParameter();
        public readonly ClampedFloatParameter scatter = new ClampedFloatParameter();
        public readonly BoolParameter highQualityFiltering = new BoolParameter();
    }
}
#endif
namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        static byte L(byte x, byte y, float t) { return (byte)System.Math.Round(x + (y - x) * Mathf.Clamp01(t)); }
        public static Color32 Lerp(Color32 a, Color32 b, float t) { return new Color32(L(a.r, b.r, t), L(a.g, b.g, t), L(a.b, b.b, t), L(a.a, b.a, t)); }
        public static implicit operator Color(Color32 c) { return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color Lerp(Color x, Color y, float t) { t = Mathf.Clamp01(t); return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t); }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Vector4 { public float x, y, z, w; public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; } }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 back { get { return new Vector3(0, 0, -1); } }
        public float magnitude { get { return Mathf.Sqrt(x * x + y * y + z * z); } }
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return new Vector3(-a.x, -a.y, -a.z); }
        public static Vector3 operator *(Vector3 a, float k) { return new Vector3(a.x * k, a.y * k, a.z * k); }
        public static Vector3 operator *(float k, Vector3 a) { return a * k; }
        public static Vector3 operator /(Vector3 a, float k) { return new Vector3(a.x / k, a.y / k, a.z / k); }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
    }
    public struct Quaternion { public static Quaternion identity { get { return default; } } public static Quaternion Euler(float x, float y, float z) { return default; } }
    public struct Bounds { public Bounds(Vector3 c, Vector3 s) { size = s; } public Vector3 size; }
    public struct Rect { public float yMin, yMax, xMin, xMax; }
    public static class Mathf
    {
        public const float PI = (float)System.Math.PI, Deg2Rad = PI / 180f;
        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; } public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
        public static float Clamp01(float v) { return Clamp(v, 0, 1); }
        public static int RoundToInt(float v) { return (int)System.Math.Round(v); } public static int CeilToInt(float v) { return (int)System.Math.Ceiling(v); } public static int FloorToInt(float v) { return (int)System.Math.Floor(v); }
        public static float Round(float v) { return (float)System.Math.Round(v); } public static float Floor(float v) { return (float)System.Math.Floor(v); }
        public static float Sqrt(float v) { return (float)System.Math.Sqrt(v); } public static float Min(float a, float b) { return a < b ? a : b; } public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) if (x < m) m = x; return m; } public static int Min(int a, int b) { return a < b ? a : b; } public static int Max(int a, int b) { return a > b ? a : b; }
        public static float Cos(float v) { return (float)System.Math.Cos(v); } public static float Sin(float v) { return (float)System.Math.Sin(v); } public static float Abs(float v) { return System.Math.Abs(v); } public static int Abs(int v) { return System.Math.Abs(v); }
        public static float Tan(float v) { return (float)System.Math.Tan(v); } public static float Atan2(float y, float x) { return (float)System.Math.Atan2(y, x); } public static float Pow(float a, float b) { return (float)System.Math.Pow(a, b); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); } public static float Repeat(float t, float l) { return t - (float)System.Math.Floor(t / l) * l; }
    }
    public enum FindObjectsSortMode { None, InstanceID }
    public class Object { public string name; public static void Destroy(Object o) { } public static T FindAnyObjectByType<T>() where T : Object { return null; } public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : Object { return new T[0]; } public static void DontDestroyOnLoad(Object o) { } }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject { return System.Activator.CreateInstance<T>(); } }
    public class Shader : Object { public static Shader Find(string n) { return null; } public static void SetGlobalColor(string n, Color c) { } }
    public class Material : Object
    {
        public Material(Shader s) { }
        public void SetTexture(string n, Texture2D t) { } public void SetFloat(string n, float v) { }
    }
    public sealed class MaterialPropertyBlock { public void SetFloat(string n, float v) { } public void SetColor(string n, Color c) { } }
    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public sealed class Texture2D : Object
    {
        readonly int w, h;
        public FilterMode filterMode; public TextureWrapMode wrapMode;
        public Texture2D(int w, int h, TextureFormat f, bool mips) { this.w = w; this.h = h; }
        public static Color32[] LastPixels; public static int LastW, LastH;
        public void SetPixels32(Color32[] px) { if (px.Length != w * h) throw new System.Exception("pixel count"); LastPixels = px; LastW = w; LastH = h; }
        public void Apply(bool mips, bool noLonger) { }
    }
    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat; public Bounds bounds;
        public void MarkDynamic() { } public void Clear() { VertexCount = 0; }
        public int VertexCount, IndexCount;
        public static int MaxVerts;
        public List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public List<Color32> C = new List<Color32>(); public List<Vector2> U = new List<Vector2>(); public List<int> T = new List<int>();
        public void SetVertices(List<Vector3> v) { V = new List<Vector3>(v); VertexCount = v.Count; if (indexFormat == Rendering.IndexFormat.UInt16 && v.Count > 65535) throw new System.Exception("16-bit mesh with " + v.Count + " vertices"); MaxVerts = System.Math.Max(MaxVerts, v.Count); }
        public void SetColors(List<Color32> c) { C = new List<Color32>(c); if (c.Count != VertexCount) throw new System.Exception("colour count"); }
        public void SetNormals(List<Vector3> n) { N = new List<Vector3>(n); if (n.Count != VertexCount) throw new System.Exception("normal count"); }
        public void SetUVs(int ch, List<Vector2> u) { U = new List<Vector2>(u); if (u.Count != VertexCount) throw new System.Exception("uv count"); }
        public void SetTriangles(List<int> t, int sub, bool calc) { T = new List<int>(t); IndexCount = t.Count; if (t.Count % 3 != 0) throw new System.Exception("tri count"); foreach (var i in t) if (i < 0 || i >= VertexCount) throw new System.Exception("index out of range " + i); }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); } }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        public Vector3 position, localPosition, localScale = new Vector3(1, 1, 1); public Quaternion rotation, localRotation;
        public void SetParent(Transform p, bool w) { }
    }
    public sealed class GameObject : Object
    {
        readonly List<Component> comps = new List<Component>();
        public GameObject(string n) { name = n; activeSelf = true; transform = new Transform(); transform.gameObject = this; transform.transform = transform; comps.Add(transform); }
        public Transform transform; public bool activeSelf; public int layer;
        public static int Created;
        public T AddComponent<T>() where T : Component
        {
            var c = (T)System.Activator.CreateInstance(typeof(T)); c.gameObject = this; c.transform = transform; comps.Add(c);
            if (c is TextMesh && GetComponent<MeshRenderer>() == null) AddComponent<MeshRenderer>();
            Created++;
            return c;
        }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T) return (T)c; return null; }
        public void SetActive(bool v) { activeSelf = v; }
    }
    public class Renderer : Component
    {
        public Material sharedMaterial; public int sortingOrder; public Bounds bounds; public Rendering.ShadowCastingMode shadowCastingMode;
        public void SetPropertyBlock(MaterialPropertyBlock b) { }
    }
    public sealed class MeshRenderer : Renderer { }
    public sealed class MeshFilter : Component { public Mesh sharedMesh; }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left, Center, Right }
    public struct CharacterInfo { public int advance; }
    public sealed class Font : Object
    {
        public Material material; public bool dynamic;
        public void RequestCharactersInTexture(string s, int size, FontStyle st) { }
        public bool GetCharacterInfo(char c, out CharacterInfo info, int size, FontStyle st) { info = default; return true; }
    }
    public sealed class TextMesh : Component { public Font font; public int fontSize; public float characterSize; public string text; public Color color; public TextAnchor anchor; public TextAlignment alignment; public FontStyle fontStyle; }
    // Audio: Load<AudioClip> finds the real files under Assets/Resources so the bank's rules run against the delivery
    public class AudioClip : Object { public float length = 0.15f; }
    public class AudioListener : Behaviour { public static bool pause; }
    public class AudioSource : Behaviour
    {
        public static readonly System.Collections.Generic.Dictionary<string, int> Plays = new System.Collections.Generic.Dictionary<string, int>();
        public float pitch = 1, volume = 1; public bool loop, playOnAwake = true, isPlaying; public AudioClip clip;
        public void Play() { isPlaying = true; int n; Plays.TryGetValue(clip.name, out n); Plays[clip.name] = n + 1; }
        public void Pause() { isPlaying = false; } public void UnPause() { isPlaying = true; } public void Stop() { isPlaying = false; }
    }
    public static class Resources
    {
        public static string Root;
        public static T Load<T>(string p) where T : Object
        {
            if (typeof(T) != typeof(AudioClip) || Root == null) return null;
            var f = System.IO.Path.Combine(Root, p + ".mp3");
            return System.IO.File.Exists(f) ? (T)(Object)new AudioClip { name = p, length = p.Contains("music") ? 30 : 0.15f } : null;
        } public static T GetBuiltinResource<T>(string p) where T : Object { return typeof(T) == typeof(Font) ? (T)(Object)new Font() : null; } }
    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    public sealed class Camera : Behaviour
    {
        public static Camera[] allCameras { get { return new Camera[0]; } }
        public bool orthographic, useOcclusionCulling, allowHDR, allowMSAA; public float orthographicSize, nearClipPlane, farClipPlane, fieldOfView, depth;
        public CameraClearFlags clearFlags; public Color backgroundColor; public int cullingMask = -1;
    }
    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Hard, Soft }
    public sealed class Light : Behaviour { public LightType type; public Color color; public float intensity, shadowStrength; public LightShadows shadows; }
    public enum FogMode { Linear = 1, Exponential, ExponentialSquared }
    public static class RenderSettings
    {
        public static Rendering.AmbientMode ambientMode; public static Color ambientSkyColor, ambientEquatorColor, ambientGroundColor, fogColor;
        public static bool fog; public static FogMode fogMode; public static float fogStartDistance, fogEndDistance;
    }
    public enum TouchPhase { Began, Moved, Stationary, Ended, Canceled }
    public struct Touch { public int fingerId; public Vector2 position; public TouchPhase phase; }
    public enum KeyCode { None, A, B, D, P, S, W, Escape, LeftArrow, RightArrow, UpArrow, DownArrow }
    public static class Input
    {
        public static int touchCount; public static bool multiTouchEnabled; public static Vector3 mousePosition;
        public static Touch GetTouch(int i) { return default; } public static bool GetMouseButton(int b) { return false; }
        public static bool GetKeyDown(KeyCode k) { return false; } public static bool GetKey(KeyCode k) { return false; }
    }
    public static class Screen { public static int width, height; public static Rect safeArea; }
    public static class Time { public static float unscaledDeltaTime = 1 / 60f; }
    public static class Application { public static int targetFrameRate; }
    public static class QualitySettings { public static int vSyncCount; }
    public static class Random { static readonly System.Random r = new System.Random(1); public static float value { get { return (float)r.NextDouble(); } } }
    public static class Debug { public static void Log(object o) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad }
    [System.AttributeUsage(System.AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
}
