// Editor menu "Pit/Setup URP": creates the URP pipeline asset with the settings style.md §6 asks for (HDR for bloom
// only, MSAA off, main-light soft shadows out to 30 units, no additional lights) and assigns it to Graphics and every
// quality level. Properties are set through SerializedObject so a renamed field shows up as a warning in the console
// instead of failing silently. Written without an Editor at hand: run it once and check the console.
#if PIT_URP
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pit.EditorTools
{
    public static class PitUrp
    {
        const string Dir = "Assets/Settings";
        const string RendererPath = Dir + "/PitRenderer.asset", PipelinePath = Dir + "/PitURP.asset";

        [MenuItem("Pit/Setup URP")]
        public static void Setup()
        {
            Directory.CreateDirectory(Dir);
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, RendererPath);
            }
            if (rd.postProcessData == null)
                rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (rd.postProcessData == null) Debug.LogWarning("Pit: PostProcessData not found; assign it on " + RendererPath + " or bloom will not run.");
            EditorUtility.SetDirty(rd);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(asset, PipelinePath);
            }

            var so = new SerializedObject(asset);
            Set(so, "m_SupportsHDR", true);
            Set(so, "m_MSAA", 1);
            Set(so, "m_RenderScale", 1f);
            Set(so, "m_MainLightRenderingMode", 1);          // per pixel
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_MainLightShadowmapResolution", 1024);
            Set(so, "m_AdditionalLightsRenderingMode", 0);   // disabled
            Set(so, "m_ShadowDistance", 30f);
            Set(so, "m_ShadowCascadeCount", 1);
            Set(so, "m_SoftShadowsSupported", true);
            Set(so, "m_SupportsCameraDepthTexture", false);
            Set(so, "m_SupportsCameraOpaqueTexture", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(cur, false);
            AssetDatabase.SaveAssets();
            Debug.Log("Pit: URP asset at " + PipelinePath + " assigned to Graphics and all quality levels.");
        }

        static void Set(SerializedObject so, string name, object value)
        {
            var p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning("Pit: URP asset has no field " + name + "; set it by hand."); return; }
            if (value is bool) p.boolValue = (bool)value;
            else if (value is int) p.intValue = (int)value;
            else if (value is float) p.floatValue = (float)value;
        }
    }
}
#endif
