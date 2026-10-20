// Editor menu: create the (empty) boot scene, apply the size-focused WebGL player settings and build.
// The game builds itself at runtime (GameRoot.Boot), so the scene only has to exist.
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pit.EditorTools
{
    public static class PitBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutDir = "Builds/WebGL", FastDir = "Builds/WebGLFast";

        [MenuItem("Pit/Create Boot Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("Pit: boot scene saved to " + ScenePath);
        }

        [MenuItem("Pit/Apply WebGL Size Settings")]
        public static void ApplySettings()
        {
            var g = NamedBuildTarget.WebGL;
            PlayerSettings.companyName = "Pit";
            PlayerSettings.productName = "Ricochet Depths";
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            PlayerSettings.SetScriptingBackend(g, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(g, ManagedStrippingLevel.High);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetIl2CppCompilerConfiguration(g, Il2CppCompilerConfiguration.Master);
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;   // TikTok packaging guide
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.nameFilesAsHashes = true;   // cache-friendly names for the CDN
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.runInBackground = false;
            PlayerSettings.SplashScreen.show = false;        // the Unity logo alone is 2.7 MB of data (optional since Unity 6)
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.defaultWebScreenWidth = 360;
            PlayerSettings.defaultWebScreenHeight = 640;
            Debug.Log("Pit: WebGL size settings applied (IL2CPP master, stripping High, engine stripping, explicit exceptions only, Brotli, WebGL2 only)");
        }

        [MenuItem("Pit/Build WebGL")]
        public static void BuildWebGL() { Build(false); }

        [MenuItem("Pit/Build WebGL (TikTok define)")]
        public static void BuildWebGLTikTok() { Build(true); }

        /// <summary>Command line: Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitBuild.BuildCli
        /// [-pitTikTok] [-pitFast]. -pitFast is the iteration build into Builds/WebGLFast: no compression (the Brotli pass
        /// alone is about a minute), IL2CPP Debug and the "build times" wasm optimisation. Bigger and slower to run, never
        /// for release; the size settings are put back afterwards.</summary>
        public static void BuildCli()
        {
            var a = System.Environment.GetCommandLineArgs();
            Build(System.Array.IndexOf(a, "-pitTikTok") >= 0, System.Array.IndexOf(a, "-pitFast") >= 0);
        }

        static void Build(bool tiktok) { Build(tiktok, false); }

        static void Build(bool tiktok, bool fast)
        {
            if (!File.Exists(ScenePath)) CreateScene();
            ApplySettings();
            var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL);
            bool has = defines.Contains("PIT_TIKTOK");
            if (tiktok && !has) defines = (defines.Length > 0 ? defines + ";" : "") + "PIT_TIKTOK";
            if (!tiktok && has) defines = defines.Replace(";PIT_TIKTOK", "").Replace("PIT_TIKTOK;", "").Replace("PIT_TIKTOK", "");
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.WebGL, defines);
            string opt = EditorUserBuildSettings.GetPlatformSettings("WebGL", "CodeOptimization");
            if (fast)
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.WebGL, Il2CppCompilerConfiguration.Debug);
                EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", "BuildTimes");
            }
            string outDir = fast ? FastDir : OutDir;

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            if (fast) { ApplySettings(); EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", opt ?? ""); }
            var s = report.summary;
            long bytes = 0;     // the WebGL report's totalSize is 0, so measure the output folder
            if (Directory.Exists(outDir)) foreach (var f in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
            Debug.Log("Pit: " + (fast ? "fast " : "") + "build " + s.result + ", " + (bytes / 1024f / 1024f).ToString("0.0") + " MB on disk, " + s.totalTime);
            if (Application.isBatchMode && s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
