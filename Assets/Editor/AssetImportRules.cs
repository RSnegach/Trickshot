using UnityEditor;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Import settings for the downloaded art and audio in Resources, applied in code rather than
    /// by hand.
    ///
    /// Unity's defaults are wrong for both of these in ways that are quiet rather than obvious. A
    /// texture imports at a 2048 ceiling, so the 4K skies would silently arrive at half resolution
    /// and the only symptom would be a soft sky nobody could explain. A .png imports as a colour
    /// map, so the grass normal would be compressed as RGB with its blue channel intact and its
    /// green channel starved - DXT5nm exists precisely because a normal map wants the opposite.
    ///
    /// Doing it here instead of in the .meta files means the rule is readable, reviewable, and
    /// travels with the code: drop another sky into Resources/Sky and it imports correctly with no
    /// further work. Bump GetVersion to force everything back through these rules.
    /// </summary>
    public class AssetImportRules : AssetPostprocessor
    {
        public override uint GetVersion() => 3;

        void OnPreprocessTexture()
        {
            var ti = assetImporter as TextureImporter;
            if (ti == null) return;
            string p = assetPath.Replace('\\', '/');

            // ---- equirectangular skies ----
            if (p.Contains("/Resources/Sky/"))
            {
                ti.textureType       = TextureImporterType.Default;
                ti.maxTextureSize    = 4096;
                ti.mipmapEnabled     = true;      // the shader samples with explicit gradients
                ti.wrapModeU         = TextureWrapMode.Repeat;   // longitude wraps
                ti.wrapModeV         = TextureWrapMode.Clamp;    // latitude must not
                ti.filterMode        = FilterMode.Bilinear;
                ti.textureCompression = TextureImporterCompression.Compressed;
                return;
            }

            // ---- hair atlases (alpha-cutout cards) ----
            // Cards are clipped against these masks, and a plain mip chain averages the strand lines
            // into a value BELOW the cutoff at distance, so far-off hair erodes to threads. Coverage-
            // preserving mips keep the same fraction of texels above the reference at every level.
            // The tileable stipple is a decal mask and wants Repeat; the two atlases must Clamp.
            if (p.Contains("/Resources/Hair/"))
            {
                bool stipple = p.EndsWith("Stipple.png");
                ti.textureType             = TextureImporterType.Default;
                ti.maxTextureSize          = 2048;
                ti.mipmapEnabled           = true;
                ti.mipMapsPreserveCoverage = !stipple;
                ti.alphaTestReferenceValue = 0.4f;
                ti.wrapMode                = stipple ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.filterMode              = FilterMode.Bilinear;
                ti.textureCompression      = TextureImporterCompression.CompressedHQ;
                return;
            }

            // ---- turf detail pair ----
            if (p.Contains("/Resources/Turf/"))
            {
                bool normal = p.EndsWith("Grass_DetailNormal.png");
                ti.textureType    = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.maxTextureSize = 1024;
                ti.mipmapEnabled  = true;
                ti.wrapMode       = TextureWrapMode.Repeat;
                ti.filterMode     = FilterMode.Bilinear;
                // The pitch is the one surface always seen at a grazing angle, which is exactly the
                // case trilinear filtering smears and anisotropic filtering does not.
                ti.anisoLevel     = 8;
                ti.textureCompression = TextureImporterCompression.Compressed;
            }
        }

        void OnPreprocessAudio()
        {
            var ai = assetImporter as AudioImporter;
            if (ai == null) return;
            string p = assetPath.Replace('\\', '/');

            // ---- the woodwork clang ----
            // Scoped to this one clip on purpose. The rest of Resources/Audio is crowd loops and
            // music, where Vorbis is the right answer and PCM would cost tens of megabytes.
            //
            // This one is the opposite case: 52 KB, and its whole character is a 6 ms metallic
            // transient. Vorbis smears exactly that, and it is pitch-shifted at play time, which
            // makes any codec artefact audible at a different frequency each hit. Decompressed on
            // load because it fires on a collision, so there is no time to stream it.
            if (p.EndsWith("/Resources/Audio/post_hit.wav"))
            {
                var s = ai.defaultSampleSettings;
                s.loadType          = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.PCM;
                s.preloadAudioData  = true;
                ai.defaultSampleSettings = s;
                ai.forceToMono = true;      // already mono; states the intent for a 3D source
            }
        }
    }
}
