// Slices ui_atlas.png from ui_atlas.json: sprite rects, 9-slice borders, point filter, no compression.
// Put ui_atlas.png and ui_atlas.json side by side anywhere under Assets/, then run
// Tools > Pit UI > Slice UI Atlas (or reimport the PNG: the postprocessor does it automatically).
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class PitUiAtlasImporter : AssetPostprocessor
{
    [System.Serializable] class Entry { public string name; public int x, y, w, h; public int[] border; public int advance; }
    [System.Serializable] class Doc { public int pixelsPerUnit; public Entry[] list; }

    const string AtlasName = "ui_atlas";

    void OnPreprocessTexture()
    {
        if (Path.GetFileNameWithoutExtension(assetPath) != AtlasName) return;
        var json = Path.ChangeExtension(assetPath, ".json");
        if (!File.Exists(json)) { Debug.LogWarning($"[PitUI] {json} missing; atlas not sliced"); return; }
        Apply((TextureImporter)assetImporter, JsonUtility.FromJson<Doc>(File.ReadAllText(json)));
    }

    static void Apply(TextureImporter ti, Doc doc)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.spritePixelsPerUnit = doc.pixelsPerUnit;      // 16, matches style.md
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed; // block compression smears 1px detail
        ti.maxTextureSize = 2048;
        ti.npotScale = TextureImporterNPOTScale.None;
#pragma warning disable 618   // spritesheet is deprecated but still the simplest path that works without the 2D Sprite package
        ti.spritesheet = doc.list.Select(e => new SpriteMetaData
        {
            name = e.name,
            rect = new Rect(e.x, e.y, e.w, e.h),
            alignment = (int)SpriteAlignment.Center,
            pivot = new Vector2(0.5f, 0.5f),
            border = e.border != null && e.border.Length == 4
                ? new Vector4(e.border[0], e.border[1], e.border[2], e.border[3])   // L, B, R, T
                : Vector4.zero,
        }).ToArray();
#pragma warning restore 618
    }

    [MenuItem("Tools/Pit UI/Slice UI Atlas")]
    static void Reslice()
    {
        var guid = AssetDatabase.FindAssets(AtlasName + " t:Texture2D").FirstOrDefault();
        if (guid == null) { Debug.LogError("[PitUI] ui_atlas.png not found under Assets/"); return; }
        AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
    }
}
#endif
