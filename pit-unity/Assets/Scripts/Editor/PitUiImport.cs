// Import settings for the pit-ui atlas (pit-ui/UI-SPEC.md §5): point filter, no mipmaps, clamp, uncompressed RGBA32,
// no NPOT scaling. Gfx samples it as a plain texture and slices it from ui_atlas.json itself, so it is not imported as
// a sprite sheet.
using UnityEditor;
using UnityEngine;

namespace Pit.EditorTools
{
    public sealed class PitUiImport : AssetPostprocessor
    {
        const string Ui = "Assets/Resources/UI/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Ui)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
