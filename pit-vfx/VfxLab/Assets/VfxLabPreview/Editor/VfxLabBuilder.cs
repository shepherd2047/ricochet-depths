// Builds the preview: URP asset (HDR on for Bloom, MSAA off, soft main-light shadows), the VFX assets, and the scene
// Assets/VfxLabPreview/VfxLab.unity: a dark corridor floor (#262937 look), side walls, the game's 40° camera,
// key light, Bloom volume (pit-unity's values: threshold 1.05, intensity 0.9, scatter 0.65), seven pit-art enemies,
// the VfxBank and the VfxLabDemo driver.
// Batch mode: -executeMethod VfxLabBuilder.Build
using System.IO;
using RicochetVfx;
using RicochetVfx.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class VfxLabBuilder
{
    const string Dir = "Assets/VfxLabPreview";
    const string ScenePath = Dir + "/VfxLab.unity";
    static readonly Color Fog = new Color32(0x26, 0x29, 0x37, 255);

    [MenuItem("Ricochet VFX/Build Lab Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Dir + "/Settings");
        PlayerSettings.colorSpace = ColorSpace.Gamma;     // same as pit-unity
        SetupUrp();
        VfxPrefabBuilder.BuildAll();
        var enemyMat = EnemyMaterial();
        var floorMat = FloorMaterial();
        var wallMat = UnitMat("M_Lab_Wall", Texture2D.whiteTexture, new Color32(0x1c, 0x1f, 0x29, 255));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // camera: 40° from straight down toward the far wall, like Board3D
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Fog;
        cam.fieldOfView = 60;
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 100;
        cam.allowHDR = true; cam.allowMSAA = false;
        camGo.transform.rotation = Quaternion.Euler(-40, 0, 0);
        camGo.transform.position = new Vector3(0, -0.6f, 0) - camGo.transform.forward * 14.5f;
        var cd = camGo.AddComponent<UniversalAdditionalCameraData>();
        cd.renderPostProcessing = true;
        cd.antialiasing = AntialiasingMode.None;

        var lg = new GameObject("Key Light").AddComponent<Light>();
        lg.type = LightType.Directional;
        lg.intensity = 1;
        lg.shadows = LightShadows.Soft;
        lg.transform.rotation = Quaternion.Euler(10, 0, 0);

        var vol = new GameObject("Bloom Volume").AddComponent<Volume>();
        vol.isGlobal = true;
        var profilePath = Dir + "/Settings/VfxLabBloom.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, profilePath); }
        Bloom bloom;
        if (!profile.TryGet(out bloom)) bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1.05f);
        bloom.intensity.Override(0.9f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(false);
        EditorUtility.SetDirty(profile);
        vol.sharedProfile = profile;

        // corridor: floor in the board plane (z = 0, facing -Z) and two low side walls
        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Floor";
        Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.transform.position = new Vector3(0, 1, 0);
        floor.transform.localScale = new Vector3(9, 22, 1);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        floorMat.mainTextureScale = new Vector2(9, 22);
        foreach (var side in new[] { -1, 1 })
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
            w.name = side < 0 ? "Wall L" : "Wall R";
            Object.DestroyImmediate(w.GetComponent<Collider>());
            w.transform.position = new Vector3(side * 4.1f, 1, -0.6f);
            w.transform.localScale = new Vector3(1.2f, 22, 1.2f);
            w.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
        }

        // enemies (pit-art lowpoly models, converted to FBX)
        var layout = new (string, float, float)[]
        {
            ("enemy_shambler", -3, 2), ("enemy_brute", -1, 2), ("enemy_spitter", 1, 2), ("enemy_shambler", 3, 2),
            ("enemy_brute", -2, 0), ("enemy_spitter", 0, 0), ("enemy_shambler", 2, 0),
        };
        var enemies = new GameObject[layout.Length];
        for (int i = 0; i < layout.Length; i++)
            enemies[i] = Enemy(layout[i].Item1, new Vector3(layout[i].Item2, layout[i].Item3, 0), enemyMat, i);

        var bankGo = new GameObject("VfxBank");
        var bank = bankGo.AddComponent<VfxBank>();
        bank.bank = AssetDatabase.LoadAssetAtPath<VfxBankAsset>("Assets/RicochetVfx/Resources/RicochetVfx/VfxBank.asset");
        bank.viewCamera = cam;
        bank.up = new Vector3(0, 0, -1);

        var ball = new GameObject("Ball");
        var demo = new GameObject("VfxLabDemo").AddComponent<VfxLabDemo>();
        demo.ball = ball.transform;
        demo.enemies = enemies;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("VfxLab: scene built at " + ScenePath);
    }

    static GameObject Enemy(string model, Vector3 at, Material mat, int i)
    {
        string path = Dir + "/Models/" + model + ".fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(path);
        if (mi != null && (!mi.isReadable || mi.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            mi.isReadable = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.SaveAndReimport();
        }
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var root = new GameObject("Enemy" + i + "_" + model.Replace("enemy_", ""));
        root.transform.position = at;
        var m = (GameObject)PrefabUtility.InstantiatePrefab(src);
        m.transform.SetParent(root.transform, false);
        // model: up (+Y) to the board's up (-Z), facing the player (-Y)
        m.transform.localRotation = Quaternion.Euler(-90, 0, 0) * Quaternion.Euler(0, 180, 0);
        foreach (var r in m.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int k = 0; k < mats.Length; k++) mats[k] = mat;
            r.sharedMaterials = mats;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
        // normalise height to ~0.9 cell and stand on the floor (z = 0) at the cell centre
        var b = Bounds(m);
        float h = b.size.z;
        if (h > 1e-4f) m.transform.localScale *= (model == "enemy_brute" ? 1.0f : 0.9f) / h;
        b = Bounds(m);
        m.transform.position += new Vector3(at.x - b.center.x, at.y - b.center.y, -b.max.z);
        return root;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material UnitMat(string name, Texture tex, Color col)
    {
        string path = Dir + "/Settings/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("RicochetVfx/Unit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_MainTex", tex);
        m.SetColor("_Color", col);
        m.SetFloat("_EmisGain", 0);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material EnemyMaterial()
    {
        var alb = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Models/enemy_theme1_albedo.png");
        var emi = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Models/enemy_theme1_emissive.png");
        var m = UnitMat("M_Lab_Enemy", alb, Color.white);
        m.SetTexture("_EmisMap", emi);
        m.SetFloat("_EmisGain", 1.6f);
        return m;
    }

    static Material FloorMaterial()
    {
        // 16×16 stone tile, one per cell, values near the fog colour so the lit floor reads as #262937
        string path = Dir + "/Settings/T_Lab_Floor.png";
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        var rnd = new System.Random(3);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                bool grout = x == 0 || y == 0 || (y == 8 && x > 0) || (x == 8 && y < 8);
                float n = (float)rnd.NextDouble() * 0.025f;
                var c = grout ? new Color(0.075f, 0.08f, 0.105f) : new Color(0.118f + n, 0.127f + n, 0.172f + n);
                t.SetPixel(x, y, c);
            }
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return UnitMat("M_Lab_Floor", AssetDatabase.LoadAssetAtPath<Texture2D>(path), Color.white);
    }

    static void SetupUrp()
    {
        string rdPath = Dir + "/Settings/VfxLabRenderer.asset", urpPath = Dir + "/Settings/VfxLabURP.asset";
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
        if (rd == null) { rd = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(rd, rdPath); }
        if (rd.postProcessData == null)
            rd.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
        EditorUtility.SetDirty(rd);
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpPath);
        if (asset == null) { asset = UniversalRenderPipelineAsset.Create(rd); AssetDatabase.CreateAsset(asset, urpPath); }
        var so = new SerializedObject(asset);
        Set(so, "m_SupportsHDR", true);
        Set(so, "m_MSAA", 1);
        Set(so, "m_MainLightShadowsSupported", true);
        Set(so, "m_MainLightShadowmapResolution", 1024);
        Set(so, "m_ShadowDistance", 40f);
        Set(so, "m_ShadowCascadeCount", 1);
        Set(so, "m_SoftShadowsSupported", true);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        GraphicsSettings.defaultRenderPipeline = asset;
        int cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = asset; }
        QualitySettings.SetQualityLevel(cur, false);
        AssetDatabase.SaveAssets();
    }

    static void Set(SerializedObject so, string name, object v)
    {
        var p = so.FindProperty(name);
        if (p == null) { Debug.LogWarning("VfxLab: URP asset has no field " + name); return; }
        if (v is bool) p.boolValue = (bool)v; else if (v is int) p.intValue = (int)v; else if (v is float) p.floatValue = (float)v;
    }
}
