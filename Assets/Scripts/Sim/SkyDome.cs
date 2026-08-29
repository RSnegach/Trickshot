using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Trickshot
{
    /// <summary>
    /// The sky. Puts a photographed panorama behind every camera, picked per venue, and lights the
    /// scene off it: the pitch's ambient and its reflections both come from whichever sky is up.
    ///
    /// This used to be Unity's procedural atmosphere model. A photo wins outright and for a reason
    /// that has nothing to do with the model being bad: real cloud is the thing the eye checks a
    /// sky against, and no analytic sky has any. The 4K panoramas are Poly Haven CC0 skies
    /// (see Resources/Sky/Sky-License.txt), tone-mapped to 8-bit by Tools/skyprep.py because the
    /// project renders in gamma space with no post stack, so HDR range would only clip.
    ///
    /// A photo's sun is IN the image, which the procedural sky's never was. Every shadow on the
    /// pitch has to point where the sky says it should, so skyprep.py measured each sun's angle out
    /// of the pixels and StadiumStyle carries a SkyRotation that yaws the sky until its sun lands
    /// on the venue's authored light direction. That way the venues keep the shadow directions they
    /// were tuned with instead of all inheriting whatever time of day the photographer had.
    ///
    /// Cost: one full-screen textured draw behind the scene, and one DynamicGI.UpdateEnvironment
    /// per venue build. Nothing per frame. The five skies together are under 2 MB on disk.
    ///
    /// BUILD SAFETY. Shader.Find only resolves shaders the build kept, and Unity's own
    /// Skybox/Panoramic is referenced by nothing here, so it would resolve in the editor and come
    /// back null for every player. Both the shader and the textures therefore live in Resources,
    /// which is always included, and are loaded by path - the same route Resources/Shaders/HairCard
    /// already takes. Behind that sit two more rungs: the built-in procedural sky, then the
    /// camera's flat background colour. The sky is never black.
    /// </summary>
    public static class SkyDome
    {
        // The front end's sky is fixed, deliberately: picking Sunset Beach and backing out used to
        // turn the whole main menu orange.
        //
        // REVISITED, because the front end did read as a painted wall - "the sky is just boring gray".
        // The note that used to sit here compared kloofendal against qwantani_noon on brightness and
        // cloud detail over the visible band and picked the brighter, flatter one. That comparison was
        // sound but it was answering the wrong question, because it assumed detail over that band was
        // obtainable at all.
        //
        // IT IS NOT. Measured across four panoramas in 5-degree elevation bands (Tools/skyfetch.py),
        // every one of them peaks in cloud detail at 35..50 degrees of elevation and is flattest right
        // at the horizon - which is atmospheric haze doing exactly what haze does:
        //
        //     elevation      noon    mid_morning   kloppenheim_06   kloofendal_48d
        //      0..5         0.066       0.089          0.097            0.091
        //     10..15        0.076       0.100          0.095            0.103
        //     35..50        0.129       0.153          0.120            0.167
        //
        // The menu camera pitches DOWN, so with a 46 degree lens the top of frame is at
        // -pitch + 23 degrees: it sees 0..+8 today, and even at zero downtilt would only reach +23.
        // The cloudy part of every sky is permanently out of shot. Swapping qwantani_noon for
        // qwantani_mid_morning (a measured 35% more detail in-band) was tried first and was visually
        // indistinguishable, which is the proof.
        //
        // THEN CORRECTED AGAIN, because "bright blue with cloud" is the actual brief and the warm
        // late-afternoon sky that briefly sat here was the WORST bright option for exactly that. Scoring
        // seventeen panoramas over the visible band on blueness (mean B minus mean R) and chroma, not
        // just on brightness and cloud:
        //
        //     sky                        lum     detail   blue     sat
        //     qwantani                  0.749    0.095   +0.241   0.27   <- chosen
        //     kloofendal_43d_clear      0.701    0.105   +0.174   0.22
        //     qwantani_afternoon        0.735    0.080   +0.165   0.20
        //     qwantani_noon (before)    0.786    0.083   +0.131   0.16
        //     qwantani_late_afternoon   0.700    0.129   +0.061   0.11   <- warm, and the least blue
        //
        // AND THEN ONE MORE CORRECTION, because that table has a hole in it: "detail" there is luminance
        // standard deviation, which counts a smooth vertical gradient as detail. It is not a cloud
        // detector, and every sky in the visible band has a gradient. Measuring CLOUD properly - subtract
        // a horizontally blurred copy, so a vertical gradient cancels and only high spatial frequency
        // survives - reorders the list completely:
        //
        //     sky                        lum     cloud    blue
        //     kloofendal_48d_p_cloudy   0.673   0.0128   +0.116   <- chosen; 8.5x the cloud
        //     kloppenheim_02            0.725   0.0041   +0.128
        //     kloofendal_43d_clear      0.693   0.0017   +0.182
        //     qwantani                  0.740   0.0015   +0.252   <- bluest, and essentially cloudless
        //     qwantani_noon             0.776   0.0011   +0.140
        //
        // BLUE AND CLOUD GENUINELY TRADE OFF here, which is why this took three passes: cloud is white,
        // so any sky with real cumulus in the band has a less blue average, and the bluest skies in the
        // band are the clear ones. qwantani_puresky was the bluest sky measured and had almost no cloud
        // at all - which is exactly what "still does not look bright blue with cloud" was pointing at.
        //
        // So take the cloud, then put the blue back by GRADING rather than by choosing a clearer sky:
        // MenuGrade lifts blue and trims red, and MenuExposure makes up the brightness this sky gives
        // away (0.673 against qwantani's 0.740). Measured on the graded result that lands near lum 0.73
        // and blue +0.18, i.e. brighter and bluer than the cloudless sky it replaced, while keeping the
        // cumulus. Grading a sky was previously dismissed as a fix for the front end being dark, and it
        // was rightly dismissed THEN - that was a UI scrim problem sitting downstream of it (see below).
        // It is a legitimate tool for hue, which is a different question.
        //
        // THE OTHER HALF OF THIS IS THE CAMERA, and it had to move: blueness and cloud BOTH increase with
        // elevation, because haze desaturates the horizon. For this same sky the band scores
        //     0..8 deg  -> blue +0.174, sat 0.20, detail 0.074
        //     0..17 deg -> blue +0.241, sat 0.27, detail 0.095
        // so MenuBackground's downtilt dropped from 15 to 7 (its pivot rising to keep the camera at the
        // same height), which is what actually lets the blue and the cloud into shot. Choosing a bluer
        // sky without opening the band up would have moved very little.
        //
        // Still NOT changed: the key light's own angle. The sky implies a 28.8 degree sun; the light
        // points where it always did. If shadow length reads wrong, that light is the thing to touch.
        //
        // What this sky was NOT responsible for: the front end being called dark and gloomy. That
        // was MenuUI painting a flat 0.30 scrim over every pixel, so the backdrop reached the screen
        // at 0.70 whatever sky was loaded (see UITheme.Scrim). Exposure, tint, ground blend, sky
        // choice and mip bias were all tried against it first and all of them failed, because they
        // all sit upstream of the composite. Do not re-litigate the sky for a UI problem.
        const string MenuSky      = "Sky/kloofendal_48d_partly_cloudy_puresky";
        // Blue up, red down, green untouched. Deliberately mild: this multiplies the CLOUDS too, and a
        // heavier hand turns white cumulus into blue-grey cumulus, which looks like weather rather than
        // a summer afternoon.
        static readonly Color MenuGrade = new Color(0.97f, 1.00f, 1.06f);
        // Makes up the brightness this sky gives away against the cloudless alternatives.
        const float MenuExposure = 1.08f;
        // 95.8, derived from the original 96.2 rather than from scratch. That value was stated as
        // "menu light yaw 150 - this sky's measured sun yaw 53.8", but a fresh measurement of the same
        // JPEG puts its sun at yaw 213.4, so the 53.8 is in a different azimuth convention than a naive
        // column-to-azimuth read produces. Rather than guess which is right, keep the known-good pairing
        // and shift it by the DIFFERENCE, which is convention-independent: this sky's sun measures 213.8
        // against noon's 213.4, so 0.4 degrees round, so the rotation moves 0.4 the other way.
        const float  MenuRotation = 95.8f;
        // Matched to this sky's own colour just under the horizon, so the far edge of the pitch fades
        // into haze instead of ending at a grey slab. Blend pulled well below the 0.55 default for the
        // same reason: at 8 degrees of downtilt the below-horizon band is most of the frame.
        static readonly Color MenuGround = new Color(0.70f, 0.75f, 0.79f);
        const float MenuGroundBlend = 0.40f;
        // Slightly over 1 to lift the shadow side. The reel runs on ONE key light (see
        // MenuBackground.Awake), and with a single light everything facing away from it is ambient
        // only, which read as unlit rather than shaded.
        const float MenuAmbient = 1.08f;
        static readonly Color MenuFlat   = new Color(0.44f, 0.60f, 0.82f);

        static Material _pano, _proc;
        static bool _panoTried;
        static readonly Dictionary<string, Texture2D> _tex = new Dictionary<string, Texture2D>();

        /// <summary>Fixed clean-midday sky for the front end. Ignores the selected venue.</summary>
        public static void ApplyMenu(Camera cam, Light sun)
        {
            var m = Dress(MenuSky, MenuRotation, MenuExposure, MenuGrade, MenuGround, MenuGroundBlend);
            if (m == null) { Flat(cam, MenuFlat); return; }

            Env(m, sun, MenuAmbient);
            RenderSettings.fog = false;
            Frame(cam, MenuFlat);
        }

        /// <summary>Sky, sun and haze for the selected venue.</summary>
        public static void Apply(Camera cam, Light sun, bool aimSun = true)
        {
            var s = StadiumStyle.Active;
            if (aimSun) AimSun(sun);

            var m = Dress(s.SkyTex, s.SkyRotation, s.SkyExposure, s.SkyTint, s.SkyGround);
            if (m == null) { Flat(cam, s.Sky); return; }

            Env(m, sun, s.AmbientBoost);

            RenderSettings.fog        = s.FogDensity > 0f;
            RenderSettings.fogMode    = FogMode.ExponentialSquared;
            RenderSettings.fogColor   = s.FogColor;
            RenderSettings.fogDensity = s.FogDensity;
            Frame(cam, s.Sky);
        }

        /// <summary>Point the directional light where the venue says, in the venue's colour.</summary>
        public static void AimSun(Light sun)
        {
            if (sun == null) return;
            var s = StadiumStyle.Active;
            sun.transform.rotation = Quaternion.Euler(s.SunEuler);
            sun.color     = s.SunColor;
            sun.intensity = s.SunIntensity;
            // Assigned every venue, not only the ones that lower it: the sun is one shared light and
            // a venue that left this at 1 has to put it back, or whichever venue was loaded first
            // keeps its shadow strength for the rest of the session.
            sun.shadowStrength = Mathf.Clamp01(s.ShadowStrength);
        }

        // ------------------------------------------------------------------ material
        static Material Dress(string texPath, float rotation, float exposure, Color grade, Color ground,
                              float groundBlend = 0.55f)
        {
            var tex = Panorama(texPath);
            if (tex != null)
            {
                var m = Panoramic();
                if (m != null)
                {
                    m.SetTexture("_MainTex", tex);
                    Set(m, "_Rotation", Mathf.Repeat(rotation, 360f));
                    Set(m, "_Exposure", exposure);
                    SetC(m, "_Tint", grade);
                    SetC(m, "_GroundColor", ground);
                    // How far up from the horizon the ground colour reaches. The shader defaults this
                    // to 0.55, which fully replaces the photo by 19.5 degrees below the horizon; the
                    // menu wants less, because its camera sits low and that band is most of the frame.
                    Set(m, "_GroundBlend", groundBlend);
                    return m;
                }
            }

            // Rung two. Shouldn't ever run - a Resources texture and a Resources shader are both
            // guaranteed to be in the build - so it aims for plausible rather than faithful: a
            // clean midday atmosphere in the venue's ground colour, no per-venue mood.
            var p = Procedural();
            if (p == null) return null;
            Set(p, "_SunDisk", 2f);
            Set(p, "_SunSize", 0.045f);
            Set(p, "_SunSizeConvergence", 5f);
            Set(p, "_AtmosphereThickness", 1.0f);
            Set(p, "_Exposure", 1.25f);
            SetC(p, "_SkyTint", new Color(0.55f, 0.62f, 0.76f));
            SetC(p, "_GroundColor", ground);
            return p;
        }

        static Material Panoramic()
        {
            if (_panoTried) return _pano;
            _panoTried = true;
            var sh = Resources.Load<Shader>("Shaders/SkyPanoramic");
            if (sh == null) return null;
            _pano = new Material(sh) { name = "TrickshotSky" };
            return _pano;
        }

        static Material Procedural()
        {
            if (_proc != null) return _proc;

            // Clone whatever the scene shipped with, which is what keeps Skybox/Procedural in the
            // build in the first place. Skip it if it is already ours: Apply overwrote
            // RenderSettings.skybox, and cloning that back would just hand out the panoramic again.
            var src = RenderSettings.skybox;
            if (src != null && src.shader != null && src.shader.name != "Trickshot/SkyPanoramic")
                _proc = new Material(src);
            else
            {
                var sh = Shader.Find("Skybox/Procedural");
                if (sh == null) return null;
                _proc = new Material(sh);
            }
            _proc.name = "TrickshotSkyFallback";
            return _proc;
        }

        static Texture2D Panorama(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_tex.TryGetValue(path, out var cached) && cached != null) return cached;

            var t = Resources.Load<Texture2D>(path);
            if (t == null) return null;
            // Wrap in longitude, clamp in latitude. The shader keeps its sampling half a texel off
            // each pole anyway, but a repeating V would put a hairline of nadir across the zenith.
            t.wrapModeU = TextureWrapMode.Repeat;
            t.wrapModeV = TextureWrapMode.Clamp;
            _tex[path] = t;
            return t;
        }

        // ------------------------------------------------------------------ environment
        static void Env(Material m, Light sun, float ambient)
        {
            RenderSettings.skybox = m;
            RenderSettings.sun    = sun;
            // Ambient and reflections both read off the sky, so choosing a sky is the whole of the
            // venue's lighting: an overcast panorama flattens the players by itself.
            RenderSettings.ambientMode           = AmbientMode.Skybox;
            RenderSettings.ambientIntensity      = ambient;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity   = 0.65f;
            DynamicGI.UpdateEnvironment();
        }

        static void Frame(Camera cam, Color fallback)
        {
            if (cam == null) return;
            cam.clearFlags      = CameraClearFlags.Skybox;
            cam.backgroundColor = fallback;   // only ever seen if the skybox draw itself fails
        }

        static void Flat(Camera cam, Color c)
        {
            if (cam == null) return;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = c;
        }

        static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        static void SetC(Material m, string prop, Color v)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, v);
        }
    }
}
