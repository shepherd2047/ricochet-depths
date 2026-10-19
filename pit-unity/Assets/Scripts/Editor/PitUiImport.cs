// Import settings for the pit-ui atlas (pit-ui/UI-SPEC.md §5): point filter, no mipmaps, clamp, uncompressed RGBA32,
// no NPOT scaling. Gfx samples it as a plain texture and slices it from ui_atlas.json itself, so it is not imported as
// a sprite sheet. The pit-scene corridor atlas (Resources/Scene, pit-scene/ASSEMBLY.md §5) is the opposite: bilinear
// with mipmaps, no alpha, max 1024, compressed.
using UnityEditor;
using UnityEngine;

namespace Pit.EditorTools
{
    public sealed class PitUiImport : AssetPostprocessor
    {
        const string Ui = "Assets/Resources/UI/", Scene = "Assets/Resources/Scene/";

        void OnPreprocessTexture()
        {
            if (assetPath.StartsWith(Scene))
            {
                var si = (TextureImporter)assetImporter;
                si.textureType = TextureImporterType.Default;
                si.sRGBTexture = true;
                si.alphaSource = TextureImporterAlphaSource.None;
                si.filterMode = FilterMode.Bilinear;
                si.mipmapEnabled = true;
                si.wrapMode = TextureWrapMode.Clamp;
                si.maxTextureSize = 1024;
                si.textureCompression = TextureImporterCompression.Compressed;
                return;
            }
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
