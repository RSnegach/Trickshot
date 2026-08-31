using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The look of the whole game's UI in one place.
    ///
    /// Everything here is generated at runtime (no imported art, consistent with the rest of the
    /// build): rounded 9-slice plates with borders and gradients, soft drop shadows, radial glows.
    /// Two ways to use it:
    ///
    ///  1. GLOBAL, no per-file work. MenuScale.Begin() installs <see cref="Skin"/>, a copy of the
    ///     built-in GUISkin with our backgrounds and text colours patched in. Every menu that says
    ///     `new GUIStyle(GUI.skin.button)` picks up the new look for free, and all metrics
    ///     (padding, alignment, font sizes) stay exactly as they were, so nothing moves.
    ///
    ///  2. EXPLICIT, for the screens worth hand-finishing. Panel / Title / Section / Divider /
    ///     Bar / Glow / Shadowed draw the pieces the skin cannot express.
    ///
    /// Tints: selection highlights multiply the plate texture, so a plain saturated colour just
    /// darkens a dark plate. Use <see cref="SelTint"/> / <see cref="WarnTint"/> / <see cref="BadTint"/>
    /// instead - they are deliberately over 1.0 so they BRIGHTEN.
    /// </summary>
    public static class UITheme
    {
        // ---------------------------------------------------------------- palette
        public static readonly Color Ink    = new Color(0.949f, 0.957f, 0.973f);
        public static readonly Color Dim    = new Color(0.659f, 0.682f, 0.741f);
        public static readonly Color Faint  = new Color(0.431f, 0.463f, 0.533f);
        public static readonly Color Gold   = new Color(1.000f, 0.824f, 0.290f);
        public static readonly Color Blue   = new Color(0.239f, 0.545f, 1.000f);
        public static readonly Color Red    = new Color(1.000f, 0.361f, 0.302f);
        public static readonly Color Green  = new Color(0.294f, 0.816f, 0.478f);

        // Plate bodies. Panels are near-opaque so text always reads over a busy pitch.
        static readonly Color PanelTop = new Color(0.106f, 0.125f, 0.188f, 0.960f);
        static readonly Color PanelBot = new Color(0.055f, 0.067f, 0.106f, 0.960f);
        static readonly Color PanelEdge = new Color(0.404f, 0.451f, 0.561f, 0.550f);

        static readonly Color BtnTop   = new Color(0.176f, 0.208f, 0.278f, 0.980f);
        static readonly Color BtnBot   = new Color(0.106f, 0.129f, 0.180f, 0.980f);
        static readonly Color BtnEdge  = new Color(0.353f, 0.404f, 0.510f, 0.700f);

        static readonly Color HovTop   = new Color(0.235f, 0.290f, 0.396f, 0.995f);
        static readonly Color HovBot   = new Color(0.145f, 0.184f, 0.267f, 0.995f);
        static readonly Color HovEdge  = new Color(0.408f, 0.588f, 0.918f, 0.900f);

        static readonly Color DownTop  = new Color(0.129f, 0.239f, 0.404f, 1.000f);
        static readonly Color DownBot  = new Color(0.086f, 0.161f, 0.290f, 1.000f);
        static readonly Color DownEdge = new Color(0.478f, 0.686f, 1.000f, 0.950f);

        static readonly Color FieldTop = new Color(0.043f, 0.055f, 0.086f, 0.980f);
        static readonly Color FieldBot = new Color(0.067f, 0.082f, 0.125f, 0.980f);
        static readonly Color FieldEdge = new Color(0.310f, 0.353f, 0.451f, 0.650f);

        /// <summary>Multiply tints for "this row is selected / listening / clashing". Over 1.0 on
        /// purpose: GUI.backgroundColor multiplies the plate, so a &lt;=1 colour only darkens it.</summary>
        public static readonly Color SelTint  = new Color(0.62f, 1.30f, 2.20f);   // blue, brighter
        public static readonly Color WarnTint = new Color(2.30f, 1.75f, 0.55f);   // gold, brighter
        public static readonly Color BadTint  = new Color(2.30f, 0.95f, 0.80f);   // red, brighter
        public static readonly Color GoodTint = new Color(0.80f, 2.00f, 1.15f);   // green, brighter

        // ---------------------------------------------------------------- textures
        static Texture2D _px;            // 1x1 white
        static Texture2D _gradV;         // 1x64 white -> transparent (top sheen)
        static Texture2D _scrimV;        // 1x64 clear at the top -> white at the bottom
        static Texture2D _glow;          // 64x64 radial falloff
        static Texture2D _panel, _panelFlat, _chip, _frame, _btn, _btnHov, _btnDown, _field, _track, _thumb, _tab, _tabSel;

        const int Tex = 40;       // plate texture edge
        const int Rad = 9;        // corner radius
        const int Slice = 12;     // 9-slice border (> Rad so corners are never squashed)

        public static Texture2D Px
        {
            get
            {
                if (_px == null) _px = Solid(Color.white);
                return _px;
            }
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Vertical white-to-clear ramp, stretched to fake a glass sheen on plates.</summary>
        static Texture2D GradV
        {
            get
            {
                if (_gradV != null) return _gradV;
                const int N = 64;
                var t = new Texture2D(1, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                {
                    float u = y / (N - 1f);          // 0 bottom -> 1 top
                    float a = u * u * u;             // hugs the top edge
                    t.SetPixel(0, y, new Color(1f, 1f, 1f, a));
                }
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                t.hideFlags = HideFlags.HideAndDontSave;
                return _gradV = t;
            }
        }

        /// <summary>Clear at the top, opaque at the bottom, for a scrim that leaves the sky alone.
        /// Curved rather than linear so it stays near zero through the whole upper third instead of
        /// fading in from the first row.</summary>
        public static Texture2D ScrimV
        {
            get
            {
                if (_scrimV != null) return _scrimV;
                const int N = 64;
                var t = new Texture2D(1, N, TextureFormat.RGBA32, false);
                for (int y = 0; y < N; y++)
                {
                    float u = y / (N - 1f);                     // 0 bottom -> 1 top, as GradV
                    t.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - u, 1.6f)));
                }
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                t.hideFlags = HideFlags.HideAndDontSave;
                return _scrimV = t;
            }
        }

        /// <summary>Soft radial blob. Bilinear filtering does the smoothing when stretched, so a
        /// tiny texture is enough for shadows, bloom behind callouts, and menu vignettes.</summary>
        public static Texture2D GlowTex
        {
            get
            {
                if (_glow != null) return _glow;
                const int N = 64;
                var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
                var px = new Color[N * N];
                float c = (N - 1) * 0.5f;
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                        float a = Mathf.Clamp01(1f - d);
                        px[y * N + x] = new Color(1f, 1f, 1f, a * a);
                    }
                t.SetPixels(px);
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                t.hideFlags = HideFlags.HideAndDontSave;
                return _glow = t;
            }
        }

        /// <summary>
        /// Anti-aliased rounded plate with a vertical gradient body and a hairline border.
        ///
        /// Built from the rounded-box signed distance field, so the corners are smooth at any
        /// size and the border is a constant width ring inside the silhouette. Sized for 9-slice
        /// (Slice > Rad), which means the gradient survives in the fixed top/bottom bands while
        /// the stretched middle reads as flat - exactly right for buttons and panels.
        /// </summary>
        static Texture2D Plate(Color top, Color bot, Color edge, float edgePx = 1.4f)
        {
            var t = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false);
            var px = new Color[Tex * Tex];
            float half = Tex * 0.5f;
            for (int y = 0; y < Tex; y++)
            {
                // Gradient runs over the whole texture; only the unstretched bands show it.
                float u = y / (Tex - 1f);                       // 0 bottom -> 1 top
                Color body = Color.Lerp(bot, top, u);
                for (int x = 0; x < Tex; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - Rad);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - Rad);
                    float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                              + Mathf.Min(Mathf.Max(qx, qy), 0f) - Rad;

                    float cov   = Mathf.Clamp01(0.5f - d);              // silhouette coverage
                    float inner = Mathf.Clamp01(0.5f - (d + edgePx));   // body inside the border
                    float ring  = Mathf.Max(0f, cov - inner);

                    float a = body.a * inner + edge.a * ring;
                    Color outc;
                    if (a <= 1e-5f) outc = new Color(0f, 0f, 0f, 0f);
                    else
                    {
                        // Straight-alpha compositing of the border ring over the body.
                        float wb = body.a * inner / a, we = edge.a * ring / a;
                        outc = new Color(body.r * wb + edge.r * we,
                                         body.g * wb + edge.g * we,
                                         body.b * wb + edge.b * we, a);
                    }
                    px[y * Tex + x] = outc;
                }
            }
            t.SetPixels(px);
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static Texture2D PanelTex   { get { if (_panel == null)   _panel   = Plate(PanelTop, PanelBot, PanelEdge, 1.4f); return _panel; } }
        static Texture2D BtnTex     { get { if (_btn == null)      _btn     = Plate(BtnTop, BtnBot, BtnEdge, 1.3f);      return _btn; } }
        static Texture2D BtnHovTex  { get { if (_btnHov == null)   _btnHov  = Plate(HovTop, HovBot, HovEdge, 1.6f);      return _btnHov; } }
        static Texture2D BtnDownTex { get { if (_btnDown == null)  _btnDown = Plate(DownTop, DownBot, DownEdge, 1.6f);   return _btnDown; } }
        static Texture2D FieldTex   { get { if (_field == null)    _field   = Plate(FieldTop, FieldBot, FieldEdge, 1.3f); return _field; } }
        static Texture2D TrackTex   { get { if (_track == null)    _track   = Plate(new Color(0.035f, 0.043f, 0.067f, 0.95f), new Color(0.055f, 0.067f, 0.098f, 0.95f), new Color(0.27f, 0.31f, 0.40f, 0.6f), 1.1f); return _track; } }
        static Texture2D ThumbTex   { get { if (_thumb == null)    _thumb   = Plate(new Color(0.71f, 0.78f, 0.92f, 1f), new Color(0.44f, 0.53f, 0.71f, 1f), new Color(0.87f, 0.91f, 1f, 0.9f), 1.2f); return _thumb; } }
        static Texture2D TabTex     { get { if (_tab == null)      _tab     = Plate(new Color(0.098f, 0.118f, 0.176f, 0.92f), new Color(0.063f, 0.078f, 0.118f, 0.92f), new Color(0.27f, 0.31f, 0.40f, 0.45f), 1.2f); return _tab; } }
        static Texture2D TabSelTex  { get { if (_tabSel == null)   _tabSel  = Plate(new Color(0.161f, 0.239f, 0.376f, 1f), new Color(0.098f, 0.157f, 0.267f, 1f), Blue, 1.6f); return _tabSel; } }
        /// <summary>Flat (non-gradient) rounded plate, for small chips where a gradient reads as a bug.</summary>
        static Texture2D PanelFlatTex { get { if (_panelFlat == null) _panelFlat = Plate(PanelTop, PanelTop, PanelEdge, 1.4f); return _panelFlat; } }
        /// <summary>
        /// WHITE flat rounded plate. GUI.color MULTIPLIES the background texture, so drawing a chip
        /// through a tinted plate crushed the caller's colour to near black (0.10 * 0.10) - which is
        /// what blacked out the stadium picker's colour swatches. On white, the colour asked for is
        /// the colour that lands, and the SDF still supplies antialiased corners.
        /// </summary>
        static Texture2D ChipTex { get { if (_chip == null) _chip = Plate(Color.white, Color.white, Color.white, 1.4f); return _chip; } }
        /// <summary>Rounded outline with a fully transparent body, for framing a live 3D viewport.</summary>
        static Texture2D FrameTex { get { if (_frame == null) _frame = Plate(new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f), Color.white, 1.6f); return _frame; } }

        // ---------------------------------------------------------------- the skin
        static GUISkin _skin;

        /// <summary>
        /// A copy of whatever GUISkin was active on first use, with our plates and text colours
        /// patched over it. Copying (rather than building from scratch) is what keeps every
        /// existing layout pixel-identical: all the padding, margins, alignments, and font sizes
        /// are inherited untouched.
        /// </summary>
        public static GUISkin Skin
        {
            get
            {
                if (_skin != null) return _skin;
                var src = GUI.skin;
                _skin = Object.Instantiate(src);
                _skin.name = "TrickshotSkin";
                _skin.hideFlags = HideFlags.HideAndDontSave;

                // One assignment retypesets the entire game. Every GUIStyle in Hud and in here is
                // built with its font left unset, and IMGUI resolves that against GUI.skin.font at
                // draw time, so the skin is the only place this has to happen. Metrics are not
                // touched: sizes, padding and alignment all stay exactly as they were.
                if (UIFont.Body != null) _skin.font = UIFont.Body;

                var slice = new RectOffset(Slice, Slice, Slice, Slice);

                // Buttons and toggles used to brighten their LABEL to pure white on hover, which
                // read as a glare rather than as feedback. One text colour for every state now; the
                // hover cue is carried entirely by the plate (BtnHovTex) plus the marker bar and
                // glow that Button draws. Note the checked look of a toggle is carried by
                // onNormal.background, not by its text, so locking the colour costs nothing there.
                Skin4(_skin.button, BtnTex, BtnHovTex, BtnDownTex, slice, Ink, Ink, Ink);
                Skin4(_skin.box, PanelTex, PanelTex, PanelTex, slice, Dim, Dim, Dim);
                Skin4(_skin.window, PanelTex, PanelTex, PanelTex, slice, Ink, Ink, Ink);
                Skin4(_skin.textField, FieldTex, FieldTex, FieldTex, slice, Ink, Ink, Ink);
                Skin4(_skin.textArea, FieldTex, FieldTex, FieldTex, slice, Ink, Ink, Ink);
                Skin4(_skin.toggle, BtnTex, BtnHovTex, BtnDownTex, slice, Ink, Ink, Ink);
                Skin4(_skin.scrollView, FieldTex, FieldTex, FieldTex, slice, Dim, Dim, Dim);

                // Sliders + scrollbars: dark recessed track, bright grabber.
                var thin = new RectOffset(6, 6, 6, 6);
                Skin4(_skin.horizontalSlider, TrackTex, TrackTex, TrackTex, thin, Dim, Dim, Dim);
                Skin4(_skin.verticalSlider, TrackTex, TrackTex, TrackTex, thin, Dim, Dim, Dim);
                Skin4(_skin.horizontalSliderThumb, ThumbTex, ThumbTex, ThumbTex, thin, Ink, Ink, Ink);
                Skin4(_skin.verticalSliderThumb, ThumbTex, ThumbTex, ThumbTex, thin, Ink, Ink, Ink);
                Skin4(_skin.horizontalScrollbar, TrackTex, TrackTex, TrackTex, thin, Dim, Dim, Dim);
                Skin4(_skin.verticalScrollbar, TrackTex, TrackTex, TrackTex, thin, Dim, Dim, Dim);
                Skin4(_skin.horizontalScrollbarThumb, ThumbTex, ThumbTex, ThumbTex, thin, Ink, Ink, Ink);
                Skin4(_skin.verticalScrollbarThumb, ThumbTex, ThumbTex, ThumbTex, thin, Ink, Ink, Ink);

                // Labels stay backgroundless; only the colour is warmed up from stock grey.
                _skin.label.normal.textColor = Ink;
                _skin.label.hover.textColor = Ink;
                return _skin;
            }
        }

        static void Skin4(GUIStyle st, Texture2D normal, Texture2D hover, Texture2D active,
                          RectOffset border, Color cN, Color cH, Color cA)
        {
            if (st == null) return;
            st.border = border;
            st.normal.background = normal;   st.normal.textColor = cN;
            st.hover.background = hover;     st.hover.textColor = cH;
            st.active.background = active;   st.active.textColor = cA;
            st.focused.background = hover;   st.focused.textColor = cH;
            st.onNormal.background = active; st.onNormal.textColor = cA;
            st.onHover.background = active;  st.onHover.textColor = cA;
            st.onActive.background = active; st.onActive.textColor = cA;
            st.onFocused.background = active; st.onFocused.textColor = cA;
        }

        /// <summary>Install the skin. Cheap enough to call from every MenuScale.Begin().</summary>
        public static void Install()
        {
            var s = Skin;
            if (!ReferenceEquals(GUI.skin, s)) GUI.skin = s;
        }

        // ---------------------------------------------------------------- primitives
        public static void Fill(Rect r, Color c)
        {
            var p = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, Px);
            GUI.color = p;
        }

        /// <summary>Soft radial glow centred on the rect. Used for shadows and callout bloom.</summary>
        public static void Glow(Rect r, Color c)
        {
            var p = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, GlowTex);
            GUI.color = p;
        }

        /// <summary>Drop shadow under a plate: a glow blob spread past the edges. Drawn before the
        /// plate so the plate covers the bright middle and only the falloff shows.</summary>
        public static void Shadow(Rect r, float spread = 18f, float alpha = 0.45f)
        {
            Glow(new Rect(r.x - spread, r.y - spread * 0.6f, r.width + spread * 2f, r.height + spread * 2f),
                 new Color(0f, 0f, 0f, alpha));
        }

        /// <summary>
        /// The standard content plate: drop shadow, rounded body, glass sheen along the top, and
        /// an optional accent rule across the very top edge.
        /// </summary>
        public static void Panel(Rect r, Color? accent = null, bool shadow = true, float alpha = 1f)
        {
            if (shadow) Shadow(r, 20f, 0.42f * alpha);

            // Drawn through a GUIStyle rather than GUI.DrawTexture so the 9-slice border is
            // honoured: corners keep their radius no matter how tall the panel gets.
            _panelStyle ??= new GUIStyle { border = new RectOffset(Slice, Slice, Slice, Slice) };
            _panelStyle.normal.background = PanelTex;
            var pc = GUI.color; var pb = GUI.backgroundColor;
            GUI.backgroundColor = Color.white;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Plate9(_panelStyle, r);
            GUI.color = pc; GUI.backgroundColor = pb;

            // Glass sheen: brightest at the top, gone by a third of the way down.
            float sh = Mathf.Min(r.height * 0.34f, 54f);
            var g = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.055f * alpha);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 1f, r.width - 4f, sh), GradV);
            GUI.color = g;

            if (accent.HasValue)
            {
                var a = accent.Value; a.a *= alpha;
                Fill(new Rect(r.x + Rad * 0.5f, r.y, r.width - Rad, 2.5f), a);
                // Faint bleed under the rule so it reads as lit rather than painted on.
                var bleed = a; bleed.a = 0.14f * alpha;
                Glow(new Rect(r.x, r.y - 8f, r.width, 30f), bleed);
            }
        }

        static GUIStyle _panelStyle, _flatStyle, _frameStyle;

        /// <summary>
        /// Draw a 9-sliced plate, but ONLY on a repaint pass.
        ///
        /// GUIStyle.Draw throws outside EventType.Repaint, and an exception thrown from OnGUI
        /// aborts the remainder of THAT pass. OnGUI runs several passes per frame, so an
        /// unguarded plate killed the mouse-down pass before any button could read the click
        /// (nothing was clickable) and killed the layout pass partway (control ids no longer
        /// lined up with the repaint pass, so buttons drew with garbage hover/pressed state).
        /// Plates are decoration; skipping them on non-repaint passes costs nothing.
        /// </summary>
        static void Plate9(GUIStyle st, Rect r)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            st.Draw(r, GUIContent.none, false, false, false, false);
        }

        /// <summary>Small flat rounded chip (score box, clock pill, tag).</summary>
        public static void Chip(Rect r, Color body, Color? edge = null)
        {
            _flatStyle ??= new GUIStyle { border = new RectOffset(Slice, Slice, Slice, Slice) };
            // White plate, so GUI.color's multiply leaves `body` exactly as asked (see ChipTex).
            _flatStyle.normal.background = ChipTex;
            var p = GUI.backgroundColor; GUI.backgroundColor = Color.white;
            var c = GUI.color; GUI.color = body;
            Plate9(_flatStyle, r);
            GUI.color = c; GUI.backgroundColor = p;
            // Hairline top rim. A flat chip with no lighting cue reads as a hole, not a card.
            Fill(new Rect(r.x + Rad * 0.5f, r.y, Mathf.Max(0f, r.width - Rad), 1f), new Color(1f, 1f, 1f, 0.10f));
            if (edge.HasValue) Fill(new Rect(r.x, r.y, 3f, r.height), edge.Value);
        }

        /// <summary>
        /// Rounded border with NO fill. IMGUI draws over every camera, so framing a rect a live
        /// camera renders into (the model preview) or a texture already blitted into (the jersey
        /// canvas) has to leave the middle alone, or the content vanishes behind the plate.
        /// </summary>
        public static void Frame(Rect r, Color? accent = null)
        {
            _frameStyle ??= new GUIStyle { border = new RectOffset(Slice, Slice, Slice, Slice) };
            _frameStyle.normal.background = FrameTex;
            var p = GUI.backgroundColor; GUI.backgroundColor = Color.white;
            var c = GUI.color; GUI.color = PanelEdge;
            Plate9(_frameStyle, r);
            GUI.color = c; GUI.backgroundColor = p;
            if (accent.HasValue)
            {
                var a = accent.Value;
                Fill(new Rect(r.x + Rad * 0.5f, r.y, Mathf.Max(0f, r.width - Rad), 2.5f), a);
                var bleed = a; bleed.a = 0.14f;
                Glow(new Rect(r.x, r.y - 8f, r.width, 30f), bleed);
            }
        }

        /// <summary>Label with a drop shadow. The single biggest readability win over a pitch.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle st, Color col, float shadowAlpha = 0.7f, float off = 2f)
        {
            if (string.IsNullOrEmpty(text)) return;
            var keep = st.normal.textColor;
            st.normal.textColor = new Color(0f, 0f, 0f, shadowAlpha * col.a);
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, st);
            st.normal.textColor = col;
            GUI.Label(r, text, st);
            st.normal.textColor = keep;
        }

        /// <summary>Screen title: big shadowed text, with an optional short gold rule centred
        /// beneath it (pass showRule: false for a screen that shouldn't carry the rule line).</summary>
        public static void Title(Rect r, string text, int fontSize = 54, Color? rule = null, bool showRule = true)
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                UIFont.Heavy(_titleStyle);   // real bold cut; the Bold above is the fallback if it fails to load
            }
            _titleStyle.fontSize = fontSize;
            _titleStyle.normal.textColor = Ink;

            // Warm bloom behind the word so it lifts off the moving backdrop.
            Glow(new Rect(r.center.x - r.width * 0.42f, r.y + r.height * 0.1f, r.width * 0.84f, r.height * 0.9f),
                 new Color(0.06f, 0.10f, 0.18f, 0.5f));
            Shadowed(r, text, _titleStyle, Ink, 0.8f, 3f);

            if (!showRule) return;
            var rc = rule ?? Gold;
            float rw = Mathf.Min(140f, r.width * 0.3f);
            Fill(new Rect(r.center.x - rw * 0.5f, r.yMax - 6f, rw, 2.5f), rc);
            var bleed = rc; bleed.a = 0.22f;
            Glow(new Rect(r.center.x - rw * 0.7f, r.yMax - 16f, rw * 1.4f, 24f), bleed);
        }

        /// <summary>
        /// The "TRICKSHOT" wordmark with the K replaced by TitleGlyph.K (a figure shaped like the
        /// letter itself: arms up/down for the stem, legs branching up-and-out/down-and-out for the
        /// two diagonals). Same bloom+shadow+rule treatment as Title(), but the word is drawn as
        /// three pieces - "TRI", the glyph, "SHOT" - kerned by measuring the real font
        /// (GUIStyle.CalcSize) rather than a hardcoded offset, so it stays correct at any fontSize.
        /// Currently only called at the splash's hero size (the hub dropped its own copy - see
        /// MenuUI.DrawHub).
        /// </summary>
        public static void TitleWithKickK(Rect r, int fontSize = 132, Color? rule = null)
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                UIFont.Heavy(_titleStyle);
            }
            _titleStyle.fontSize = fontSize;
            _titleStyle.normal.textColor = Ink;

            Glow(new Rect(r.center.x - r.width * 0.42f, r.y + r.height * 0.1f, r.width * 0.84f, r.height * 0.9f),
                 new Color(0.06f, 0.10f, 0.18f, 0.5f));

            float wTri = _titleStyle.CalcSize(new GUIContent("TRI")).x;
            float wShot = _titleStyle.CalcSize(new GUIContent("SHOT")).x;
            // The glyph gets its OWN full-size slot, not a real "K" character's narrow advance
            // width - it runs taller AND wider than a plain letter (the kicking leg overshoots
            // above cap-height on purpose), so sizing off CalcSize("K") would either draw it tiny
            // (fit to the slot) or overlap "TRI"/"SHOT" (grown past the slot it was given).
            float kSize = fontSize * 1.1f;
            float total = wTri + kSize + wShot;
            float x = r.center.x - total * 0.5f;

            var triRect = new Rect(x, r.y, wTri, r.height); x += wTri;
            var kSquare = new Rect(x, r.center.y - kSize * 0.5f, kSize, kSize); x += kSize;
            var shotRect = new Rect(x, r.y, wShot, r.height);

            Shadowed(triRect, "TRI", _titleStyle, Ink, 0.8f, 3f);
            Shadowed(shotRect, "SHOT", _titleStyle, Ink, 0.8f, 3f);

            // The glyph carries the same drop-shadow treatment as the letters flanking it (an
            // offset dark copy, then the real one).
            var kShadow = new Rect(kSquare.x + 3f, kSquare.y + 3f, kSquare.width, kSquare.height);
            var pc = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(kShadow, TitleGlyph.K);
            GUI.color = Ink;
            GUI.DrawTexture(kSquare, TitleGlyph.K);
            GUI.color = pc;

            var rc = rule ?? Gold;
            float rw = Mathf.Min(140f, r.width * 0.3f);
            Fill(new Rect(r.center.x - rw * 0.5f, r.yMax - 6f, rw, 2.5f), rc);
            var bleed = rc; bleed.a = 0.22f;
            Glow(new Rect(r.center.x - rw * 0.7f, r.yMax - 16f, rw * 1.4f, 24f), bleed);
        }

        static GUIStyle _titleStyle, _sectionStyle, _hintStyle;

        /// <summary>Gold all-caps group header with a hairline rule running to the right edge.</summary>
        public static void Section(Rect r, string text)
        {
            _sectionStyle ??= new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _sectionStyle.normal.textColor = Gold;
            var content = new GUIContent(text);
            float tw = _sectionStyle.CalcSize(content).x;
            GUI.Label(r, content, _sectionStyle);
            float x0 = r.x + tw + 10f;
            if (r.xMax - x0 > 8f)
                Fill(new Rect(x0, r.y + r.height * 0.5f, r.xMax - x0, 1f), new Color(1f, 1f, 1f, 0.10f));
        }

        /// <summary>Hairline separator.</summary>
        public static void Divider(float x, float y, float w) =>
            Fill(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.09f));

        /// <summary>Small dim centred hint line (Esc to close, etc.).</summary>
        public static void Hint(Rect r, string text, TextAnchor align = TextAnchor.MiddleCenter)
        {
            _hintStyle ??= new GUIStyle { fontSize = 12, wordWrap = true };
            _hintStyle.alignment = align;
            _hintStyle.normal.textColor = Faint;
            GUI.Label(r, text, _hintStyle);
        }

        static GUIStyle _pulseStyle;
        /// <summary>
        /// An inviting call-to-action that breathes rather than sits still - "press any key to
        /// continue". Same alpha-pulse idiom Hud.Clock() uses under its 15s countdown, but at a
        /// slow, calm 2.2 (period ~2.9s) instead of Clock's urgent 7 - this is an invitation, not a
        /// warning. Gold rather than Faint/Dim: on a splash with nothing else to click, this IS the
        /// primary call to action, not a footnote.
        /// </summary>
        public static void PulseHint(Rect r, string text)
        {
            _pulseStyle ??= new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            UIFont.Heavy(_pulseStyle);
            _pulseStyle.fontSize = 20;

            float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.2f));
            var col = Gold; col.a = pulse;

            var bleed = Gold; bleed.a = 0.12f * pulse;
            Glow(new Rect(r.center.x - r.width * 0.3f, r.y - r.height * 0.3f, r.width * 0.6f, r.height * 1.6f), bleed);
            Shadowed(r, text, _pulseStyle, col, 0.7f * pulse, 2f);
        }

        /// <summary>
        /// Lit status dot with a soft halo, drawn at a centre point. State reads off the colour
        /// before the words next to it are parsed, which is the whole reason it exists.
        /// </summary>
        public static void Dot(float cx, float cy, Color col, float rad = 3f)
        {
            Glow(new Rect(cx - rad * 4.5f, cy - rad * 4.5f, rad * 9f, rad * 9f),
                 new Color(col.r, col.g, col.b, 0.7f));
            Fill(new Rect(cx - rad, cy - rad, rad * 2f, rad * 2f), col);
        }

        /// <summary>
        /// Indeterminate spinner: eight dots on a ring with the bright one chasing round. Driven by
        /// unscaledTime so it keeps turning while the game is paused or the sim is not running.
        /// </summary>
        public static void Spinner(Rect r, Color col)
        {
            float cx = r.center.x, cy = r.center.y;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - 3f;
            float head = Time.unscaledTime * 1.6f;
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                float phase = Mathf.Repeat(head - i / 8f, 1f);
                float k = 0.14f + 0.86f * Mathf.Pow(1f - phase, 3f);
                Fill(new Rect(cx + Mathf.Sin(a) * rad - 2f, cy - Mathf.Cos(a) * rad - 2f, 4f, 4f),
                     new Color(col.r, col.g, col.b, k));
            }
        }

        /// <summary>Horizontal meter: recessed track, gradient fill, bright leading edge.</summary>
        public static void Bar(Rect r, float t01, Color lo, Color hi)
        {
            t01 = Mathf.Clamp01(t01);
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x, r.y, r.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            float w = r.width * t01;
            if (w <= 0.5f) return;
            var c = Color.Lerp(lo, hi, t01);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            // Highlight along the top of the fill + a hot cap at the head.
            var top = Color.Lerp(c, Color.white, 0.45f); top.a = 0.5f;
            Fill(new Rect(r.x, r.y, w, Mathf.Max(1f, r.height * 0.32f)), top);
            Fill(new Rect(r.x + w - 2f, r.y, 2f, r.height), Color.Lerp(c, Color.white, 0.7f));
        }

        /// <summary>
        /// Menu button. The plate itself comes from the skin (so padding, font and size are the
        /// caller's); this adds a marker bar off the leading edge and a soft glow while hovered,
        /// which is what makes a row read as "the one I'm about to click". Pass markerBar: false
        /// for a screen that shouldn't carry the leading-edge line (the glow highlight still shows).
        /// </summary>
        public static bool Button(Rect r, string label, GUIStyle st, bool bad = false, bool markerBar = true)
        {
            LockText(st);
            var e = Event.current;
            if (e != null && r.Contains(e.mousePosition))
            {
                Color bar = bad ? Red : Gold;
                if (markerBar)
                    Fill(new Rect(r.x - 12f, r.y + 8f, 4f, Mathf.Max(4f, r.height - 16f)), bar);
                Glow(new Rect(r.x - 26f, r.y - 6f, r.width + 52f, r.height + 12f),
                     new Color(bar.r, bar.g, bar.b, 0.10f));
            }
            return GUI.Button(r, label, st);
        }

        /// <summary>
        /// A card that looks and HIGHLIGHTS like a button but cannot be clicked - the "Coming Soon"
        /// teaser at the bottom of the pickers.
        ///
        /// No GUI.Button, so there is no control: nothing to click and no mouse event consumed, and
        /// the buttons around it keep working. GUI.enabled = false would have been the obvious way
        /// and is wrong here - it suppresses the skin's hover plate and greys the label, which is
        /// exactly the highlight this card exists to keep. Hover comes from the same rect test
        /// Button uses, and the plate is swapped by hand. Returns true while hovered so the caller
        /// can warm the text.
        /// </summary>
        public static bool Tease(Rect r, string label, GUIStyle st)
        {
            LockText(st);
            var e = Event.current;
            bool hot = e != null && r.Contains(e.mousePosition);
            if (hot)
            {
                Fill(new Rect(r.x - 12f, r.y + 8f, 4f, Mathf.Max(4f, r.height - 16f)), Gold);
                Glow(new Rect(r.x - 26f, r.y - 6f, r.width + 52f, r.height + 12f),
                     new Color(Gold.r, Gold.g, Gold.b, 0.10f));
            }
            // GUI.Label draws normal.background then the text, so borrowing that one slot gives the
            // caller's font and padding on the real button plate. Restored immediately.
            var keep = st.normal.background;
            st.normal.background = hot ? BtnHovTex : BtnTex;
            GUI.Label(r, label, st);
            st.normal.background = keep;
            return hot;
        }

        /// <summary>
        /// Force every state's text colour to match the normal one.
        ///
        /// Belt to the skin's braces. Dozens of call sites build their style as
        /// new GUIStyle(GUI.skin.button) { normal = { textColor = X } }, which overrides ONLY the
        /// normal state - hover and active keep whatever they inherited, and the stock skin's is
        /// pure white. Fixing the skin cannot reach those, so the leading edge of every draw does.
        /// Idempotent and allocation-free, so calling it per frame is fine.
        /// </summary>
        public static void LockText(GUIStyle st)
        {
            if (st == null) return;
            Color c = st.normal.textColor;
            st.hover.textColor = c;
            st.active.textColor = c;
            st.focused.textColor = c;
            st.onNormal.textColor = c;
            st.onHover.textColor = c;
            st.onActive.textColor = c;
            st.onFocused.textColor = c;
        }

        /// <summary>Button held in an "on" state (current selection in a list of choices). Tints
        /// have to exceed 1.0: GUI.backgroundColor MULTIPLIES the plate, so a saturated colour
        /// would only darken it.</summary>
        public static bool Toggle(Rect r, string label, bool on, GUIStyle st, Color? tint = null)
        {
            var keep = GUI.backgroundColor;
            if (on) GUI.backgroundColor = tint ?? SelTint;
            bool hit = Button(r, label, st);
            GUI.backgroundColor = keep;
            if (on) Fill(new Rect(r.x + 5f, r.yMax - 3f, r.width - 10f, 2.5f), Gold);
            return hit;
        }

        static GUIStyle _cardTitleStyle, _cardSubStyle, _cardSoonStyle;
        /// <summary>
        /// A FIFA-style mode card: an icon zone, a bold title, a wrapped subtitle - the hub's
        /// panel-button, in place of the plain UITheme.Button list every screen has used until now.
        /// Panel()'s own hover-plate tint is off limits (it forces GUI.backgroundColor to white
        /// before drawing), so hover reads the same way Button()'s glow does instead: an ambient
        /// bloom around the whole card, no accent rule line. comingSoon draws a small gold "SOON"
        /// chip in the corner, so the not-yet-built status is visible before the click, not after.
        /// </summary>
        public static bool ModeCard(Rect r, Texture2D icon, string title, string subtitle, bool comingSoon = false)
        {
            var e = Event.current;
            bool hot = e != null && r.Contains(e.mousePosition);

            Panel(r, accent: null);
            if (hot)
                Glow(new Rect(r.x - 14f, r.y - 14f, r.width + 28f, r.height + 28f),
                     new Color(Gold.r, Gold.g, Gold.b, 0.10f));

            // The icon+title+subtitle block is centred in the card rather than pinned under a
            // fixed top pad: cards now size off the real canvas (see MenuUI.DrawHub) and can run
            // much taller than this content stack, which top-anchored would leave as a dead band
            // at the bottom of every card.
            const float iconSize = 150f, titleH = 34f, subH = 40f, blockGap = 8f;
            float contentH = iconSize + blockGap + titleH + 2f + subH;
            float blockY = r.y + Mathf.Max(20f, (r.height - contentH) * 0.5f);
            var iconRect = new Rect(r.center.x - iconSize * 0.5f, blockY, iconSize, iconSize);
            if (icon != null)
            {
                var pc = GUI.color; GUI.color = hot ? Ink : Dim;
                GUI.DrawTexture(iconRect, icon);
                GUI.color = pc;
            }

            _cardTitleStyle ??= new GUIStyle { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            UIFont.Heavy(_cardTitleStyle); _cardTitleStyle.fontSize = 20;
            var titleRect = new Rect(r.x, iconRect.yMax + 8f, r.width, titleH);
            Shadowed(titleRect, title, _cardTitleStyle, Ink);

            _cardSubStyle ??= new GUIStyle { fontSize = 13, wordWrap = true, alignment = TextAnchor.UpperCenter };
            _cardSubStyle.normal.textColor = Dim;
            var subRect = new Rect(r.x + 10f, titleRect.yMax + 2f, r.width - 20f, subH);
            GUI.Label(subRect, subtitle, _cardSubStyle);

            if (comingSoon)
            {
                var soonRect = new Rect(r.xMax - 66f, r.y + 10f, 56f, 22f);
                Chip(soonRect, Gold);
                _cardSoonStyle ??= new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _cardSoonStyle.normal.textColor = new Color(0.08f, 0.07f, 0.03f);
                GUI.Label(soonRect, "SOON", _cardSoonStyle);
            }

            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>
        /// Dim behind a menu, plus a soft dark disc behind its centred column, so white text reads
        /// over whatever the camera is showing.
        ///
        /// The dim is flat by default, which is right for the panel screens: there the scene behind
        /// is decoration and the panel is the content. Pass <paramref name="top"/> to ramp it
        /// instead - that alpha at the top of the screen, growing to <paramref name="tint"/> at the
        /// bottom - for the one screen where the backdrop IS the content.
        ///
        /// WHY THE RAMP EXISTS. The main menu was drawing 0.30 of near-black over every pixel and
        /// another 0.5 disc through the middle, so the backdrop reel reached the screen at 0.70 of
        /// its authored value at the edges and 0.35 in the centre. That is a flat multiply on
        /// everything: it took the sky's bright hazy horizon down to a grey wall and pushed the
        /// pitch's shadowed side to almost black, which read as there being no key light at all.
        /// Neither was fixable in the renderer, because neither was happening in the renderer.
        /// A menu only needs contrast where its text is, and both Title and Button already carry
        /// their own local backing (a bloom and a plate), so the full-screen half of this was
        /// paying for legibility that was already bought.
        /// </summary>
        public static void Scrim(float w, float h, float tint = 0.55f, float discW = 760f,
                                 float disc = 0.5f, float top = -1f)
        {
            var full = new Rect(0f, 0f, w, h);
            var ink  = new Color(0.02f, 0.03f, 0.05f, tint);

            if (top < 0f) Fill(full, ink);                      // flat, as every panel screen wants
            else
            {
                if (top > 0f) { ink.a = top; Fill(full, ink); }
                float rest = tint - top;
                if (rest > 0f)
                {
                    // GUI.color multiplies the texture, so the ramp's alpha scales this one.
                    ink.a = rest;
                    var p = GUI.color; GUI.color = ink;
                    GUI.DrawTexture(full, ScrimV);
                    GUI.color = p;
                }
            }

            if (disc > 0f)
                Glow(new Rect(w * 0.5f - discW * 0.5f, h * 0.5f - h * 0.62f, discW, h * 1.24f),
                     new Color(0f, 0f, 0f, disc));
        }
    }
}
