// Import settings for the pit-audio clips (pit-audio/audio-list.md, "Unity WebGL import settings"), applied on import
// so a fresh checkout or a re-delivery needs no hand tweaking. SFX: mono, decompressed on load, 32 kHz. Music:
// stereo, compressed in memory, loaded in the background, preloaded. Written without an Editor at hand: reimport
// Assets/Resources/Audio once and check one clip's inspector.
using UnityEditor;
using UnityEngine;

namespace Pit.EditorTools
{
    public sealed class PitAudioImport : AssetPostprocessor
    {
        const string Sfx = "Assets/Resources/Audio/sfx/", Music = "Assets/Resources/Audio/music/";

        void OnPreprocessAudio()
        {
            bool sfx = assetPath.StartsWith(Sfx), music = assetPath.StartsWith(Music);
            if (!sfx && !music) return;
            var imp = (AudioImporter)assetImporter;
            var s = imp.defaultSampleSettings;
            if (sfx)
            {
                imp.forceToMono = true;
                imp.loadInBackground = false;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                s.sampleRateOverride = 32000;
            }
            else
            {
                imp.forceToMono = false;
                imp.loadInBackground = true;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            }
            s.compressionFormat = AudioCompressionFormat.Vorbis;    // WebGL re-encodes to AAC regardless
            s.quality = sfx ? 0.6f : 0.5f;
#if UNITY_2022_2_OR_NEWER
            s.preloadAudioData = true;
#else
            imp.preloadAudioData = true;
#endif
            imp.defaultSampleSettings = s;
        }
    }
}
