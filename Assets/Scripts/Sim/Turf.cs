using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Procedural turf. Builds one seamless set of grass maps at runtime and hands back a material
    /// tiled to a ground slab's footprint, so pitches read as mown grass instead of flat green.
    ///
    /// Generated rather than imported because the project has no art pipeline (everything is built
    /// from code at boot) and the grass colour is per-venue data, so a baked PNG would have to be
    /// re-authored for every stadium. One 768 tile costs about a quarter of a second, once per
    /// colour per run, during a load transition that already builds thousands of primitives.
    ///
    /// THREE MAPS, NOT ONE, and that is the point. Albedo alone cannot show a mow band, because a
    /// real band is not a colour: it is the same grass leaning toward you or away from you, catching
    /// the light differently. Painting it as brightness gives you stripes on a carpet that look
    /// identical from every angle. So the same fields also drive a normal map and a smoothness map,
    /// which is what makes the bands strengthen, fade and swap as the camera swings round the pitch.
    /// In albedo they are worth only +-5.5%; any more and they read as paint again.
    ///
    /// What is in the tile, most to least visible at distance:
    ///   - Mow bands. Two per tile, alternating toward and away, with the mower's wheel line on
    ///     the seam and the flattened grass either side of it.
    ///   - Clump drift, 0.6 to 4 m: watering and growth variation.
    ///   - Nap grain, 4 to 20 cm, with soil showing through where the nap thins.
    /// Only the first two survive mipping, so those are what carries the pitch past about 15 m. The
    /// grain and the scanned detail layer are what you see standing on it.
    ///
    /// TILING IS THE WHOLE TRICK. The value noise wraps on its own lattice (see VN) and the mow
    /// bands divide the tile evenly, so everything repeats with no seam. Callers must use INTEGER
    /// tile counts (Tiles) or the wrap lands mid-band and paints a hard line across the pitch.
    ///
    /// On top of all that sits a photographed lawn as the Standard shader's detail layer, tiled far
    /// tighter, which supplies the blade texture this generator cannot: see Detail below.
    /// </summary>
    public static class Turf
    {
        const int   Res            = 768;   // one tile
        const float TileMetres     = 12f;   // world size of one tile
        const int   StripesPerTile = 2;     // -> 6 m mow bands
        const float DetailMetres   = 0.9f;  // world size of the scanned lawn in Resources/Turf

        // Amplitudes, as a fraction of luminance. Set by rendering the tile, mipping it down to
        // what the camera actually sees at 40 m, and looking at both.
        const float BandAmp     = 0.055f;   // mow band in albedo. Small on purpose: see the class note.
        const float NapAmp      = 0.46f;    // grain, 4..20 cm. Mipped away by about 15 m.
        const float PatchAmp    = 0.30f;    // clump drift, 0.6..4 m. Survives mipping.
        const float CoreAmp     = 0.060f;   // the mower's wheel line, right on the band seam
        const float ShoulderAmp = 0.025f;   // and the grass its tyre flattened either side
        const float SoilMax     = 0.18f;    // how far the gaps go toward bare soil
        const float DryMax      = 0.12f;    // how far the thin areas go toward straw
        const float BandTilt    = 0.30f;    // band tilt in the normal, about 17 degrees
        const float GrainTilt   = 1.5f;     // nap gradient into the normal

        static readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();

        /// <summary>Turf material for a ground slab of this world footprint.</summary>
        public static Material Ground(Color baseColour, float sizeX, float sizeZ, float smoothness = 0.12f)
        {
            var tex = Texture(baseColour);
            var m = tex != null ? Make.MatTex(tex, smoothness) : Make.Mat(baseColour, smoothness);
            m.mainTextureScale = new Vector2(Tiles(sizeX), Tiles(sizeZ));
            Surface(m, smoothness);
            Detail(m, sizeX, sizeZ);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Turf material in the selected venue's grass colour.</summary>
        public static Material Ground(float sizeX, float sizeZ)
            => Ground(StadiumStyle.Active.Grass, sizeX, sizeZ);

        // Integer tiles only (see the class note).
        static float Tiles(float metres) => Mathf.Max(1f, Mathf.Round(metres / TileMetres));

        // ------------------------------------------------------------------ the fields
        // Everything below is derived from four fields, all of them wrapping on the tile:
        //
        //   lean      which way this row's mower pass laid the grass, +1 toward the light, -1 away,
        //             fading to 0 at the seam so the two passes meet instead of butting.
        //   nap       fine directional grain. Small period across the mow direction and a large one
        //             along it, so the grain runs the way the mower dragged it.
        //   patch     low frequency clump and watering drift.
        //   core /    the wheel line on the seam and the flattened shoulder either side of it.
        //   shoulder
        //
        // Two things about the noise, both learned the hard way. The octave periods are chosen NOT
        // to share factors: at 80/40/16 the lattices lined up and the grain read as woven cloth.
        // And the octave weights are concentrated rather than spread, because a weighted sum of N
        // octaves has std sqrt(sum(w^2)) times one octave's - so piling on octaves at equal weight
        // drives the field toward its mean and quietly removes the contrast you meant to add.

        static float Nap(float u, float v)
            => VN(u, v,  79, 293) * 0.52f
             + VN(u, v,  37, 151) * 0.27f
             + VN(u, v, 131,  47) * 0.21f;   // cross grain, so it is not perfectly combed

        static float Patch(float u, float v)
            => VN(u + 0.31f, v + 0.17f,  3,  7) * 0.46f
             + VN(u + 0.73f, v + 0.41f,  7, 11) * 0.31f
             + VN(u + 0.19f, v + 0.83f, 13, 19) * 0.23f;

        // The nap is wanted by all three maps, and the normal wants it at four neighbours as well,
        // so it is built once into an array instead of six times per pixel. 2.25 MB, kept for the
        // run: the next venue load needs it again, and it is a fraction of one tile's own mip chain.
        static float[] _nap;

        static float[] NapField()
        {
            if (_nap != null) return _nap;
            var n = new float[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                float v = (float)y / Res;
                for (int x = 0; x < Res; x++) n[y * Res + x] = Nap((float)x / Res, v);
            }
            _nap = n;
            return n;
        }

        // ------------------------------------------------------------------ albedo
        /// <summary>The tiling colour map for a grass colour. Cached: one build per colour per run.</summary>
        public static Texture2D Texture(Color baseColour)
        {
            Color32 c32 = baseColour;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b;
            if (_cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var tex = NewMap("Turf", linear: false);
            var nap = NapField();

            // Bare soil under the thin patches, and straw where the grass is dry: soil is redder,
            // much darker and far less saturated, straw is warmer and lighter. Both derived from the
            // venue colour rather than fixed, so a dark northern pitch does not get desert dirt.
            var soil = new Color(Mathf.Clamp01(baseColour.r * 1.35f + 0.02f),
                                 Mathf.Clamp01(baseColour.g * 0.78f),
                                 Mathf.Clamp01(baseColour.b * 0.55f));
            var straw = new Color(Mathf.Clamp01(baseColour.r * 1.30f + 0.10f),
                                  Mathf.Clamp01(baseColour.g * 1.10f + 0.05f),
                                  Mathf.Clamp01(baseColour.b * 0.72f));

            var px = new Color32[Res * Res];
            for (int y = 0; y < Res; y++)
            {
                float v = (float)y / Res;
                Band(v, out float lean, out float core, out float shoulder);
                float bandLum = (1f + lean * BandAmp)
                              * (1f + core * CoreAmp - shoulder * ShoulderAmp);

                for (int x = 0; x < Res; x++)
                {
                    float u = (float)x / Res;
                    float na = nap[y * Res + x];
                    float pa = Patch(u, v);

                    float lum = bandLum
                              * (1f + (na - 0.5f) * NapAmp)
                              * (1f + (pa - 0.5f) * PatchAmp);

                    // Soil shows only where a thin spot in the nap lands on a sparse spot in its own
                    // field. Two conditions rather than one, so the gaps stay scattered instead of
                    // outlining every dark streak in the grain.
                    float gap = SS(0.72f, 1f, 1f - na) * SS(0.55f, 1f, VN(u + 0.05f, v + 0.61f, 90, 90));
                    float dry = SS(0.68f, 1f, VN(u + 0.37f, v + 0.11f, 5, 4)) * DryMax;

                    // Chroma drift. Thin grass reads yellower, thick reads deeper and bluer. Mild,
                    // and it is what stops a single base colour reading as a flat green tint. The
                    // map this replaced instead lerped 55% toward straw on a 3x3 lattice, which put
                    // a bright olive blotch in the same place every 12 m across the whole pitch.
                    float warm = (pa - 0.5f) * 0.16f + (na - 0.5f) * 0.10f;
                    Color c = baseColour;
                    c.r *= 1f + warm * 0.55f;
                    c.b *= 1f - warm * 0.75f;
                    c = Color.Lerp(c, straw, dry);
                    c = Color.Lerp(c, soil, gap * SoilMax) * lum;

                    px[y * Res + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f),
                                                  (byte)(Mathf.Clamp01(c.g) * 255f),
                                                  (byte)(Mathf.Clamp01(c.b) * 255f), 255);
                }
            }

            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            _cache[key] = tex;
            return tex;
        }

        // ------------------------------------------------------------------ normal + smoothness
        // Both are colour independent, so they are built once and shared by every venue, and they
        // are built together because they come out of the same two fields.
        static Texture2D _bump, _gloss;

        static void Surface(Material m, float smoothness)
        {
            if (!m.HasProperty("_MetallicGlossMap")) return;   // Standard got stripped: plain turf
            if (_bump == null) BuildSurface();

            m.SetTexture("_BumpMap", _bump);
            m.EnableKeyword("_NORMALMAP");

            m.SetTexture("_MetallicGlossMap", _gloss);
            m.EnableKeyword("_METALLICGLOSSMAP");
            // With that keyword on, the shader reads metallic from .r and smoothness from .a and
            // ignores _Glossiness entirely, so the caller's smoothness has to arrive some other way.
            // It rides in on _GlossMapScale, which multiplies whatever the map's alpha says. The map
            // is baked centred on 0.5 (so the +-60% swing fits without clipping), hence the 2.
            // Clamped because the shader declares it Range(0,1): a caller asking for smoothness
            // above 0.5 would silently get 0.5. Nothing does, and matte is the right answer for turf.
            m.SetFloat("_GlossMapScale", Mathf.Clamp01(smoothness * 2f));
        }

        static void BuildSurface()
        {
            var nap = NapField();
            _bump  = NewMap("TurfNormal", linear: true);
            _gloss = NewMap("TurfGloss",  linear: true);
            var nrm = new Color32[Res * Res];
            var gls = new Color32[Res * Res];

            for (int y = 0; y < Res; y++)
            {
                Band((float)y / Res, out float lean, out _, out _);
                int ym = ((y - 1) + Res) % Res, yp = (y + 1) % Res;

                for (int x = 0; x < Res; x++)
                {
                    int i = y * Res + x;
                    int xm = ((x - 1) + Res) % Res, xp = (x + 1) % Res;

                    // Central differences of the nap, wrapped, so the grain gets real relief and
                    // catches a highlight along its length instead of only darkening the albedo.
                    float gu = nap[y * Res + xp] - nap[y * Res + xm];
                    float gv = nap[yp * Res + x] - nap[ym * Res + x];

                    float nx = lean * BandTilt - gu * GrainTilt;
                    float ny = -gv * GrainTilt;
                    float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);
                    nx *= inv; ny *= inv;

                    // UnpackNormalmapRGorAG does x *= w, then xy = xy*2-1, then rebuilds z. So x
                    // goes in R, y in G, B is never read, and A MUST be 255 or x is multiplied away.
                    nrm[i] = new Color32((byte)(Mathf.Clamp01(nx * 0.5f + 0.5f) * 255f),
                                         (byte)(Mathf.Clamp01(ny * 0.5f + 0.5f) * 255f), 255, 255);

                    // Grass leaning toward the viewer is glossier than grass leaning away, which is
                    // the other half of why a mow band shows. Centred on 0.5: see Surface.
                    float g = 0.5f * (1f + lean * 0.55f + (nap[i] - 0.5f) * 0.30f);
                    gls[i] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(g) * 255f));   // r = metallic
                }
            }

            _bump.SetPixels32(nrm);
            _bump.Apply(updateMipmaps: true, makeNoLongerReadable: true);
            _gloss.SetPixels32(gls);
            _gloss.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        }

        // One mower pass, and where in it this row sits. Row constant, so it is hoisted out of the
        // inner loop in all three builds.
        static void Band(float v, out float lean, out float core, out float shoulder)
        {
            float bandPos = v * StripesPerTile;
            bool  toward  = ((int)bandPos & 1) == 0;
            float edge    = Mathf.Abs(Mathf.Repeat(bandPos, 1f) - 0.5f) * 2f;   // 0 mid, 1 seam
            lean     = (toward ? 1f : -1f) * (1f - SS(0.86f, 1f, edge));
            core     = SS(0.986f, 1f, edge);
            shoulder = SS(0.940f, 0.990f, edge) * (1f - core);
        }

        static Texture2D NewMap(string name, bool linear)
            => new Texture2D(Res, Res, TextureFormat.RGBA32, mipChain: true, linear: linear)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 9,   // the pitch is nearly always seen at a grazing angle
            };

        // ------------------------------------------------------------------ blade detail
        // A photographed lawn (ambientCG Grass005, CC0 - see Resources/Turf/Turf-License.txt) laid
        // over the generated tile as the Standard shader's DETAIL layer, rather than baked into it.
        //
        // Baking was the obvious idea and it is wrong twice. The scan covers about a metre of
        // ground, so stretching it across a 12 m tile turns blades into fronds; and baking at an
        // honest pixels-per-metre would cost tens of MB per grass colour, with the cache holding one
        // per venue. As a detail layer it keeps its own tiling, so the generated maps stay in charge
        // of everything that has to be low frequency and per-venue - mow bands, colour, wear - and
        // the scan supplies only blade texture, once, shared by every pitch in the game.
        static Texture2D _detail, _detailNrm;
        static bool _detailTried;

        static void Detail(Material m, float sizeX, float sizeZ)
        {
            // No detail slot means Make fell back off Standard onto Legacy Diffuse, which happens
            // only if Standard got stripped from the build. Plain generated turf, no complaints.
            if (!m.HasProperty("_DetailAlbedoMap")) return;

            if (!_detailTried)
            {
                _detailTried = true;
                _detail    = Resources.Load<Texture2D>("Turf/Grass_Detail");
                _detailNrm = Resources.Load<Texture2D>("Turf/Grass_DetailNormal");
            }
            if (_detail == null) return;   // missing art: plain generated turf, no crash

            m.SetTexture("_DetailAlbedoMap", _detail);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(DetailTiles(sizeX), DetailTiles(sizeZ)));
            m.SetFloat("_UVSec", 0f);              // detail rides the same UV set as the main tile
            m.EnableKeyword("_DETAIL_MULX2");
            // Detail albedo is multiplied by two, so mid grey is the value that changes nothing. The
            // image was mean-normalised to exactly that when it was prepared, which is why it
            // modulates the venue's grass colour instead of replacing it with the scan's own green.
            // The scan's ambient occlusion is folded into it, because _OcclusionMap samples at the
            // MAIN tiling and so cannot carry a one-metre map.

            if (_detailNrm == null) return;
            m.SetTexture("_DetailNormalMap", _detailNrm);
            m.SetFloat("_DetailNormalMapScale", 0.55f);   // full strength reads as gravel, not grass
            m.EnableKeyword("_NORMALMAP");   // no per-pixel normal path at all without it
        }

        // Integer tiles again, and for the same reason as the main texture: the scan wraps, and a
        // fractional count drops the seam somewhere across the middle of the pitch. The count has to
        // scale with the slab or the blades scale with it instead - a stadium ground plane runs to
        // well over 100 m, and a fixed count would stretch a one-metre lawn across every metre of it.
        static float DetailTiles(float metres) => Mathf.Max(1f, Mathf.Round(metres / DetailMetres));

        // GLSL-style smoothstep. Unity's Mathf.SmoothStep interpolates BETWEEN from and to, which
        // is a different function and would return 0.88..1 where this returns 0..1.
        static float SS(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // ---- tileable value noise ----
        // Sampled in 0..1 UV with a per-axis integer lattice period, so the field wraps exactly on
        // the tile edge. Different periods per axis is what makes the grain directional. A constant
        // offset added to u or v only shifts the phase: the lattice still wraps on its own period.
        static float VN(float u, float v, int perU, int perV)
        {
            float x = u * perU, y = v * perV;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = H(x0,      y0,      perU, perV), b = H(x0 + 1, y0,      perU, perV);
            float c = H(x0,      y0 + 1,  perU, perV), d = H(x0 + 1, y0 + 1,  perU, perV);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float H(int x, int y, int perU, int perV)
        {
            uint n = (uint)(Wrap(x, perU) * 374761393 + Wrap(y, perV) * 668265263 + 1013904223);
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xFFFFFFu) / 16777215f;
        }

        static int Wrap(int v, int per) => ((v % per) + per) % per;
    }
}
