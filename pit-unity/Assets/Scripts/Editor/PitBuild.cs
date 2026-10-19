// Editor menu: create the (empty) boot scene, apply the size-focused WebGL player settings and build.
// The game builds itself at runtime (GameRoot.Boot), so the scene only has to exist.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Pit.EditorTools
{
    public static class PitBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string OutDir = "Builds/WebGL";

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
            var g = BuildTargetGroup.WebGL;
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
            PlayerSettings.defaultWebScreenWidth = 360;
            PlayerSettings.defaultWebScreenHeight = 640;
            Debug.Log("Pit: WebGL size settings applied (IL2CPP master, stripping High, engine stripping, explicit exceptions only, Brotli, WebGL2 only)");
        }

        [MenuItem("Pit/Build WebGL")]
        public static void BuildWebGL() { Build(false); }

        [MenuItem("Pit/Build WebGL (TikTok define)")]
        public static void BuildWebGLTikTok() { Build(true); }

        /// <summary>Command line: Unity -batchmode -quit -projectPath . -executeMethod Pit.EditorTools.PitBuild.BuildCli</summary>
        public static void BuildCli() { Build(System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pitTikTok") >= 0); }

        static void Build(bool tiktok)
        {
            if (!File.Exists(ScenePath)) CreateScene();
            ApplySettings();
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL);
            bool has = defines.Contains("PIT_TIKTOK");
            if (tiktok && !has) defines = (defines.Length > 0 ? defines + ";" : "") + "PIT_TIKTOK";
            if (!tiktok && has) defines = defines.Replace(";PIT_TIKTOK", "").Replace("PIT_TIKTOK;", "").Replace("PIT_TIKTOK", "");
            PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL, defines);

            var opts = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log("Pit: build " + s.result + ", " + (s.totalSize / 1024f / 1024f).ToString("0.0") + " MB, " + s.totalTime);
            if (Application.isBatchMode && s.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
