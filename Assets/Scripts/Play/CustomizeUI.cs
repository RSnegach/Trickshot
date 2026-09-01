using System.Collections;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player customization, shown after the stadium is picked and before the pre-match
    /// screen, for striker-based modes only. Four stages, Next/Back between them, with a
    /// live 3D model preview on the left:
    ///   1. BODY   - height + weight sliders with a live trait readout, and footedness.
    ///   2. SKILL  - spend a fixed point pool into a branching skill tree drawn as a
    ///               clickable node graph (six categories, capstone perks).
    ///   3. NAME   - name text + shirt number; baked into the jersey next stage.
    ///   4. JERSEY - paint on a 2D jersey canvas (color wheel, brush size + opacity,
    ///               drag to paint, undo, clear) AROUND the baked name/number.
    /// All results are written to PlayerProfile + SkillTree (read by the ragdoll builder,
    /// traits, and contact model). IMGUI, runtime-only, no assets.
    /// </summary>
    public class CustomizeUI : MonoBehaviour
    {
        System.Action _onDone;
        System.Action _onBack;

        // Body -> Skill tree -> Name -> Jersey (name/number before jersey so you can draw
        // around them).
        enum Stage { Body, Skill, Name, Jersey }
        Stage _stage = Stage.Body;

        // When true (keeper customize), the Skill stage is skipped in both directions - it only
        // drives shot/movement traits a keeper never uses. Set before Init.
        public bool SkipSkill;

        // Skill tree UI state.
        SkillTree.Category _skillCat = SkillTree.Category.Pace;
        // Adult-mode-only extra skill tab. UI-only (NOT a real SkillTree.Category), so it doesn't
        // touch the stats heptagon or node lists; its graph body is intentionally blank for now.
        bool _thirdLegTab;
        // Species-only extra skill tab, labelled SpeciesDef.InstinctTab ("Primate", "Ratite", ...).
        // Same shape as _thirdLegTab, with one difference worth knowing: Instinct IS a real
        // SkillTree.Category and its nodes use real football effect keys, so unlike Third Leg it DOES
        // move the stat heptagon. That is deliberate - one shared tree means the stat card stays
        // comparable across species, so a species perk that grants sprint has to read on the card.
        bool _instinctTab;

        // Body-stage appearance sub-menu (cycled by the arrows beside the BODY title). Index 0 is
        // always the BODY trait readout; 1..N are the SELECTED SPECIES' cosmetic slots, so a horse
        // gets COAT / MANE / MARKINGS / TACK where a human gets SKIN / HAIR / FACIAL / EXTRAS. An
        // index over Species.Current.Slots rather than a fixed enum, because the tab set is data.
        int _bodySub;
        Vector2 _apprScroll;                 // scroll for the option grids
        PlayerAppearance _lastPreviewAppr;   // detect appearance change to rebuild the preview
        bool _apprInit;

        // Working copies (committed to PlayerProfile on Done).
        float _height, _weight;
        bool _leftFooted;
        string _name;
        int _number;

        // Adult-mode gate. The toggle ARMS the confirmation popup; Continue there opens a random
        // knowledge-quiz popup; answering correctly finally flips _adultMode on. Turning the toggle
        // OFF is immediate (no popup). It's a joke gate: a wrong answer just serves another question.
        bool _adultMode;
        bool _adultPrompt;   // stage 1: the "are you 18" popup is showing (modal)
        bool _adultQuiz;     // stage 2: the knowledge-quiz popup is showing (modal)
        // Confirmation modal shown when leaving the Skill stage with adult mode on AND points spent
        // in the Third Leg tab: "You have assigned XX% of your Skill Points to your penis. Continue?"
        bool _thirdLegPrompt;

        /// <summary>
        /// A popup owns the screen. EVERY hand-rolled Event.current handler on this screen has to
        /// check this before touching the mouse.
        ///
        /// GUI.enabled = false is enough for real IMGUI controls - Unity reports a disabled control's
        /// event type as Ignore, so it neither responds nor consumes - but it does nothing at all to
        /// code that reads Event.current itself and calls e.Use(). IMGUI has no z-order and no
        /// modality: whoever Uses the event first wins, and every raw handler here runs BEFORE the
        /// popups are drawn. That is what broke the adult-mode answer buttons. The coat/skin colour
        /// wheel (SlotSubMenu -> WheelPick) and the preview drag rect both sit under where the quiz
        /// lays its answer rows, so the MouseDown was consumed on the way in and GUI.Button never
        /// saw the press it needs to claim hot control. Which slot is open - and so whether a wheel
        /// happens to be sitting over an answer - depends on the species' slot set, which is why it
        /// showed up on quadrupeds and not on humans.
        /// </summary>
        bool ModalUp => _adultPrompt || _adultQuiz || _thirdLegPrompt;
        int _quizIdx = -1;   // current question index into AdultQuiz.Bank
        int _quizPick = -1;  // the option the user clicked this question (-1 = none yet)
        float _quizFeedbackUntil;   // unscaled time until the red/green feedback clears + next Q

        // ---- Jersey canvas (ATLAS) ----
        // The jersey texture is a 256x520 atlas with two stacked 256x256 drawable regions:
        // BACK (bottom) and FRONT (above), plus a small plain band on top the side faces
        // sample. Region layout constants live in JerseyDesigns (single source of truth,
        // shared with the torso UV mapping in Make.JerseyBox).
        const int RegW = JerseyDesigns.W;          // 256, region width = atlas width
        const int RegH = JerseyDesigns.RegionH;    // 256, region height
        const int AtlasH = JerseyDesigns.AtlasH;   // 520, full atlas height
        const int BackY0 = JerseyDesigns.BackY0;   // 0
        const int FrontY0 = JerseyDesigns.FrontY0; // 256
        const int PlainY0 = JerseyDesigns.PlainY0; // 512

        Texture2D _canvas;               // the painted jersey atlas (front + back regions)
        Color32[] _pixels;               // CPU buffer we paint into, then Apply
        Color32[] _baseLayer;            // jersey base + design + name + number, WITHOUT paint strokes
        Color32[] _undoPixels;           // snapshot before the current stroke
        Texture2D _wheel;                // color-wheel picker texture
        Color _brushColor = new Color(0.9f, 0.1f, 0.1f);
        float _brushSize = 10f;          // radius in texture pixels
        float _brushOpacity = 1f;
        bool _painting;

        // Which region the player is currently drawing on: 0 = front, 1 = back.
        int _drawSide;
        // Selected predrawn design (null = none) + the picker's active tab + scroll.
        Design _selectedDesign;
        DesignTab _designTab = DesignTab.Nations;
        Vector2 _designScroll;

        // Eyedropper: when armed, the NEXT left-click anywhere on screen sets the brush colour
        // to the exact pixel under the cursor (read back from the screen), then disarms.
        bool _eyedropper;
        bool _picking;   // true while the end-of-frame screen read is in flight
        // Colour of the baked name + number on the back (player-chosen). White default.
        Color _identityColor = Color.white;

        // Mouse-wheel-click drag resizes the brush (drag left smaller, right bigger).
        bool _resizingBrush;
        float _resizeStartX;
        float _resizeStartSize;
        Vector2 _lastMouse;              // for the live brush-size ring cursor

        // Live 3D preview on the left, updated as the player changes things.
        PlayerPreview _preview;
        bool _draggingModel;             // jersey stage: click-drag on the preview to spin
        float _lastDragX;
        float _lastPreviewH, _lastPreviewW;   // detect body changes to rebuild the model
        bool _previewDirty;              // body changed; rebuild once the drag releases

        public void Init(System.Action onDone, System.Action onBack)
        {
            _onDone = onDone;
            _onBack = onBack;
            GameInput.CaptureCursor(false);

            _height = PlayerProfile.Height;
            _weight = PlayerProfile.Weight;
            _leftFooted = PlayerProfile.LeftFooted;
            _name = PlayerProfile.PlayerName;
            _number = PlayerProfile.Number;
            // Reflect the persisted adult state, but never for a species that has no adult mode.
            // Species.ApplySelection already clears it; this keeps the invariant local to the screen
            // that draws the toggle.
            _adultMode = PlayerProfile.Appearance.Adult && Species.Current.AllowsAdult;
            _bodySub = 0;   // always open on the BODY trait readout, whatever the species' tab set is

            BuildCanvas();
            BuildWheel();

            // Live 3D preview model on the left.
            var pg = new GameObject("PlayerPreview");
            _preview = pg.AddComponent<PlayerPreview>();
            _preview.Setup();
            _lastPreviewH = _height; _lastPreviewW = _weight;
        }

        void OnDestroy()
        {
            if (_preview != null) _preview.Teardown();
        }

        void BuildCanvas()
        {
            if (_canvas == null)
            {
                _canvas = new Texture2D(RegW, AtlasH, TextureFormat.RGBA32, false);
                _canvas.wrapMode = TextureWrapMode.Clamp;
            }
            // Base layer = jersey colour, then (optionally) the selected predrawn design on
            // BOTH regions, then the baked name + number on the BACK only. Paint strokes are
            // applied ON TOP of this, so Clear returns to the base (design + name/number).
            _baseLayer = new Color32[RegW * AtlasH];
            Color32 baseCol = PlayerProfile.JerseyBase;
            for (int i = 0; i < _baseLayer.Length; i++) _baseLayer[i] = baseCol;
            if (_selectedDesign != null) _selectedDesign.Apply(_baseLayer);   // fills front + back regions
            BakeIdentity(_baseLayer);                                         // back region only

            _pixels = (Color32[])_baseLayer.Clone();
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
            _undoPixels = (Color32[])_pixels.Clone();
        }

        // Apply a predrawn design (or null to clear back to plain): rebuild the base layer and
        // reset the paint on top of it (per "replace, then draw over"). Live via the shared canvas.
        void ApplyDesign(Design d)
        {
            _selectedDesign = d;
            BuildCanvas();
        }

        // Bake the number (large, centred) and name (small, above it) into the BACK region
        // only, as block glyphs, white with a dark outline so they read on any jersey colour.
        // The back face UVs (Make.JerseyBox) are upright, so glyphs baked upright here read
        // upright on the body (fixes the old upside-down back).
        void BakeIdentity(Color32[] buf)
        {
            string num = Mathf.Clamp(_number, 1, 99).ToString();
            // Number: big glyphs centred in the lower-middle of the back.
            DrawText(buf, BackY0, num, RegW / 2, (int)(RegH * 0.42f), 9, true);
            // Name: small glyphs across the upper-middle of the back.
            string nm = string.IsNullOrWhiteSpace(_name) ? "" : _name.ToUpper();
            if (nm.Length > 0) DrawText(buf, BackY0, nm, RegW / 2, (int)(RegH * 0.72f), 3, true);
        }

        // A hue/saturation color wheel (value fixed at 1); click to set the brush color.
        void BuildWheel()
        {
            const int n = 128;
            _wheel = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - r) / r, dy = (y - r) / r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > 1f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                float hue = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 1f) % 1f;
                px[y * n + x] = Color.HSVToRGB(hue, dist, 1f);
            }
            _wheel.SetPixels32(px);
            _wheel.Apply();
        }

        // ---- Minimal 5x7 pixel font (A-Z, 0-9, space). Each glyph is 7 rows of 5-bit
        // masks, top row first. Enough to stamp a name + number into the jersey texture. ----
        static readonly System.Collections.Generic.Dictionary<char, byte[]> Font = BuildFont();
        static System.Collections.Generic.Dictionary<char, byte[]> BuildFont()
        {
            // Rows are 5 bits (bit4=leftmost). Authored top-to-bottom.
            var f = new System.Collections.Generic.Dictionary<char, byte[]>();
            void G(char c, params byte[] rows) => f[c] = rows;
            G(' ',0,0,0,0,0,0,0);
            G('A',0x0E,0x11,0x11,0x1F,0x11,0x11,0x11); G('B',0x1E,0x11,0x11,0x1E,0x11,0x11,0x1E);
            G('C',0x0E,0x11,0x10,0x10,0x10,0x11,0x0E); G('D',0x1E,0x11,0x11,0x11,0x11,0x11,0x1E);
            G('E',0x1F,0x10,0x10,0x1E,0x10,0x10,0x1F); G('F',0x1F,0x10,0x10,0x1E,0x10,0x10,0x10);
            G('G',0x0E,0x11,0x10,0x17,0x11,0x11,0x0F); G('H',0x11,0x11,0x11,0x1F,0x11,0x11,0x11);
            G('I',0x0E,0x04,0x04,0x04,0x04,0x04,0x0E); G('J',0x07,0x02,0x02,0x02,0x12,0x12,0x0C);
            G('K',0x11,0x12,0x14,0x18,0x14,0x12,0x11); G('L',0x10,0x10,0x10,0x10,0x10,0x10,0x1F);
            G('M',0x11,0x1B,0x15,0x15,0x11,0x11,0x11); G('N',0x11,0x19,0x15,0x13,0x11,0x11,0x11);
            G('O',0x0E,0x11,0x11,0x11,0x11,0x11,0x0E); G('P',0x1E,0x11,0x11,0x1E,0x10,0x10,0x10);
            G('Q',0x0E,0x11,0x11,0x11,0x15,0x12,0x0D); G('R',0x1E,0x11,0x11,0x1E,0x14,0x12,0x11);
            G('S',0x0F,0x10,0x10,0x0E,0x01,0x01,0x1E); G('T',0x1F,0x04,0x04,0x04,0x04,0x04,0x04);
            G('U',0x11,0x11,0x11,0x11,0x11,0x11,0x0E); G('V',0x11,0x11,0x11,0x11,0x11,0x0A,0x04);
            G('W',0x11,0x11,0x11,0x15,0x15,0x1B,0x11); G('X',0x11,0x11,0x0A,0x04,0x0A,0x11,0x11);
            G('Y',0x11,0x11,0x0A,0x04,0x04,0x04,0x04); G('Z',0x1F,0x01,0x02,0x04,0x08,0x10,0x1F);
            G('0',0x0E,0x11,0x13,0x15,0x19,0x11,0x0E); G('1',0x04,0x0C,0x04,0x04,0x04,0x04,0x0E);
            G('2',0x0E,0x11,0x01,0x02,0x04,0x08,0x1F); G('3',0x1F,0x02,0x04,0x02,0x01,0x11,0x0E);
            G('4',0x02,0x06,0x0A,0x12,0x1F,0x02,0x02); G('5',0x1F,0x10,0x1E,0x01,0x01,0x11,0x0E);
            G('6',0x06,0x08,0x10,0x1E,0x11,0x11,0x0E); G('7',0x1F,0x01,0x02,0x04,0x08,0x08,0x08);
            G('8',0x0E,0x11,0x11,0x0E,0x11,0x11,0x0E); G('9',0x0E,0x11,0x11,0x0F,0x01,0x02,0x0C);
            return f;
        }

        // Draw a centred string into a region of the atlas. regionY0 is the region's bottom
        // atlas row; cx/cy are region-local (y up, 0..RegH). outline adds a dark border so
        // text reads on any colour. cy is roughly the glyph centre.
        void DrawText(Color32[] buf, int regionY0, string text, int cx, int cy, int scale, bool outline)
        {
            int glyphW = 5 * scale, glyphH = 7 * scale, space = scale;
            int total = text.Length * glyphW + (text.Length - 1) * space;
            int startX = cx - total / 2;
            int gy = cy - glyphH / 2;
            int px = startX;
            foreach (char raw in text)
            {
                char ch = char.ToUpper(raw);
                if (!Font.TryGetValue(ch, out var rows)) rows = Font[' '];
                DrawGlyph(buf, regionY0, rows, px, gy, scale, outline);
                px += glyphW + space;
            }
        }

        void DrawGlyph(Color32[] buf, int regionY0, byte[] rows, int gx, int gy, int scale, bool outline)
        {
            Color32 ink = _identityColor;   // player-chosen name/number colour (white default)
            // Dark outline normally; if the ink is itself very dark, outline in white so it reads.
            float lum = _identityColor.r * 0.299f + _identityColor.g * 0.587f + _identityColor.b * 0.114f;
            Color32 edge = lum < 0.35f ? new Color32(235, 235, 235, 255) : new Color32(20, 20, 20, 255);
            for (int r = 0; r < 7; r++)
            {
                byte mask = rows[r];
                for (int c = 0; c < 5; c++)
                {
                    if ((mask & (1 << (4 - c))) == 0) continue;
                    // top row (r=0) is highest in the region -> larger local y.
                    int bx = gx + c * scale;
                    int by = gy + (6 - r) * scale;
                    if (outline) FillBlock(buf, regionY0, bx - 1, by - 1, scale + 2, scale + 2, edge);
                }
            }
            // Second pass draws the ink so it sits over its own outline.
            for (int r = 0; r < 7; r++)
            {
                byte mask = rows[r];
                for (int c = 0; c < 5; c++)
                {
                    if ((mask & (1 << (4 - c))) == 0) continue;
                    int bx = gx + c * scale;
                    int by = gy + (6 - r) * scale;
                    FillBlock(buf, regionY0, bx, by, scale, scale, ink);
                }
            }
        }

        // Fill a block in region-local coords. Clamps to the region (0..RegW, 0..RegH) so
        // glyphs never bleed into the other region or the plain band. regionY0 shifts the
        // local rows to their atlas rows.
        static void FillBlock(Color32[] buf, int regionY0, int x0, int y0, int w, int h, Color32 col)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                if (y < 0 || y >= RegH) continue;         // stay inside this region vertically
                int ay = regionY0 + y;
                for (int x = x0; x < x0 + w; x++)
                {
                    if (x < 0 || x >= RegW) continue;
                    buf[ay * RegW + x] = col;
                }
            }
        }

        // Scale the whole customize screen up on big displays (see MenuScale). Wrapped so the
        // early returns inside DrawCustomize() can't leak the scaled GUI matrix.
        void OnGUI()
        {
            MenuScale.Begin();
            DrawCustomize();
            MenuScale.End();
        }

        void DrawCustomize()
        {
            // Preview column on the left + a control panel on the right. The column width comes from
            // PlayerPreview because it varies with the display (a quadruped is long rather than tall
            // and cannot fit a portrait column side-on) and because SpeciesSelectUI reads the same
            // source, which is what keeps the model from jumping across the screen on Next.
            float previewW = PlayerPreview.ColumnWidth;
            const float gap = PlayerPreview.ColumnGap;
            const float contentW = PlayerPreview.PanelW;
            float totalW = previewW + gap + contentW;
            const float panelH = PlayerPreview.PanelH;
            float ox = MenuScale.Width * 0.5f - totalW * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.38f, totalW + 460f);

            // Live 3D preview viewport (the camera renders into this rect). FRAME, not Panel: IMGUI
            // draws over every camera, so a filled plate here would hide the model.
            var previewRect = new Rect(ox, y, previewW, panelH);
            UITheme.Frame(previewRect, UITheme.Blue);
            if (_preview != null)
            {
                // Rebuild the model when the body changed, but DEBOUNCED: mark dirty while
                // a slider is dragged and only rebuild once the mouse is released, so we
                // don't tear down + recreate the ragdoll every frame of a drag.
                if (!Mathf.Approximately(_height, _lastPreviewH) || !Mathf.Approximately(_weight, _lastPreviewW))
                    _previewDirty = true;
                // Appearance changes (skin/hair/facial/accessory) also rebuild the model, same
                // debounce. Compare against the last-applied snapshot.
                if (!_apprInit || !ApprEquals(PlayerProfile.Appearance, _lastPreviewAppr))
                {
                    _previewDirty = true;
                    _lastPreviewAppr = PlayerProfile.Appearance;
                    _apprInit = true;
                }
                bool mouseDown = Input.GetMouseButton(0);
                if (_previewDirty && !mouseDown)
                {
                    PlayerProfile.Height = _height; PlayerProfile.Weight = _weight;
                    _preview.Rebuild();
                    _lastPreviewH = _height; _lastPreviewW = _weight;
                    _previewDirty = false;
                }
                // The preview camera wants REAL device pixels, but previewRect is in the scaled GUI
                // space - convert, or the 3D model renders in the wrong place/size on a big screen.
                _preview.ViewportPx = MenuScale.ToScreen(previewRect);
                _preview.AutoRotate = false;          // every stage: the player turns the model by dragging it
                HandleModelDrag(previewRect);
            }
            UITheme.Hint(new Rect(previewRect.x, previewRect.yMax - 26f, previewW, 20f), "Drag the model to spin it");

            // While an adult-mode popup (age confirm OR quiz) is up, disable + darken the whole
            // menu so it reads as modal; the popup itself re-enables GUI below. Hoisted this high
            // deliberately: IMGUI has no z-order, so the preset column and the Body sub-menu arrows
            // below are drawn BEFORE the popup and would otherwise take its clicks (CLEAR ALL behind
            // the Third Leg dialog was the worst of it). Everything newly covered is draw-only or a
            // real control, so no control IDs shift between passes.
            bool prevEnabled = GUI.enabled;
            if (ModalUp) GUI.enabled = false;   // ...and every raw handler checks ModalUp itself

            // Skill stage: one-click build presets down the left column + a live attribute
            // radar over the lower preview, so the shape updates as nodes are bought.
            if (_stage == Stage.Skill)
            {
                // Preset/RANDOMIZE column is hidden on the blank Third Leg tab; the radar heptagon
                // stays as-is (left unchanged per the request).
                if (!(_adultMode && _thirdLegTab)) SkillPresetButtons(previewRect);
                // CAP the radar's width and centre it. StatRadar sizes its heptagon off the SHORTER
                // side, so a wider column would leave the radius pinned by the 190 px height while
                // the dark backdrop kept stretching, giving a small chart on a wide slab. 260 is what
                // previewW - 40 resolved to back when the column was a fixed 300, so a small display
                // draws exactly what it drew before.
                float radarW = Mathf.Min(previewW - 40f, 260f);
                var radarRect = new Rect(previewRect.center.x - radarW * 0.5f, previewRect.yMax - 210f, radarW, 190f);
                UITheme.Chip(radarRect, new Color(0.05f, 0.06f, 0.09f, 0.82f), UITheme.Gold);
                StatRadar.Draw(radarRect);
            }

            // Control panel.
            float x = ox + previewW + gap;
            float panelW = contentW;
            UITheme.Panel(new Rect(x, y, panelW, panelH), UITheme.Gold);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            if (_stage == Stage.Body)
            {
                // "CUSTOMIZE -" prefix, then ‹ SUBMENU › arrows that cycle the appearance sub-menus.
                UITheme.Shadowed(new Rect(x + 28f, y + 14f, 220f, 36f), "CUSTOMIZE -", title, UITheme.Ink, 0.75f, 2f);
                float axl = x + 210f;
                var arrow = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
                var subName = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
                // The tab set is per species, so clamp before cycling: a species with fewer slots
                // must not leave the index pointing past the end of its list.
                int subCount = SubCount;
                if (_bodySub >= subCount) _bodySub = 0;
                if (GUI.Button(new Rect(axl, y + 16f, 30f, 30f), "‹", arrow))
                    { _bodySub = (_bodySub - 1 + subCount) % subCount; _apprScroll = Vector2.zero; }
                UITheme.Shadowed(new Rect(axl + 32f, y + 14f, 150f, 36f), SubName(_bodySub), subName, UITheme.Gold, 0.75f, 2f);
                if (GUI.Button(new Rect(axl + 184f, y + 16f, 30f, 30f), "›", arrow))
                    { _bodySub = (_bodySub + 1) % subCount; _apprScroll = Vector2.zero; }
            }
            else
                UITheme.Shadowed(new Rect(x + 28f, y + 14f, panelW - 56f, 36f), "CUSTOMIZE - " + _stage.ToString().ToUpper(), title, UITheme.Ink, 0.75f, 2f);

            // Which species is being customized, small and gold at the panel's top right. A separate
            // label rather than part of the title, because the Body title already hosts the ‹ ›
            // sub-menu arrows (which reach to x + 424, so this starts clear of them).
            var spTag = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight,
                normal = { textColor = UITheme.Gold }
            };
            UITheme.Label(new Rect(x + panelW - 128f, y + 18f, 100f, 22f), Species.Current.Name.ToUpper(), spTag);
            // Short gold rule under the title (same stub as the species screen). Clears every
            // stage's first row: Body starts at y+56, Skill y+52, Jersey y+58, Name y+76.
            UITheme.Fill(new Rect(x + 28f, y + 46f, 48f, 2.5f), UITheme.Gold);

            switch (_stage)
            {
                case Stage.Body:   BodyStage(x, y, panelW, panelH); break;
                case Stage.Skill:  SkillStage(x, y, panelW, panelH); break;
                case Stage.Jersey: JerseyStage(x, y, panelW, panelH); break;
                case Stage.Name:   NameStage(x, y, panelW, panelH); break;
            }

            NavButtons(x, y, panelW, panelH);

            GUI.enabled = prevEnabled;
            if (_adultPrompt) DrawAdultPrompt();
            else if (_adultQuiz) DrawAdultQuiz();
            else if (_thirdLegPrompt) DrawThirdLegPrompt();
        }

        // Shared modal chrome for the three customize popups: a full-screen dim behind a themed
        // panel with a gold top accent, so they match every other panel in the game.
        static void ModalPanel(Rect r)
        {
            UITheme.Fill(new Rect(0, 0, MenuScale.Width, MenuScale.Height), new Color(0.02f, 0.03f, 0.05f, 0.72f));
            UITheme.Panel(r, UITheme.Gold);
        }

        // Modal age-confirmation popup for ADULT MODE. Darkens the whole screen and shows a
        // centered dialog: Continue confirms (adult mode ON), Back cancels (stays OFF). Drawn last
        // in OnGUI, on top of the disabled/dimmed menu.
        void DrawAdultPrompt()
        {
            float w = 460f, h = 200f;
            float px = MenuScale.Width * 0.5f - w * 0.5f, py = MenuScale.Height * 0.5f - h * 0.5f;
            ModalPanel(new Rect(px, py, w, h));

            var msg = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(px + 30f, py + 26f, w - 60f, 90f),
                "You confirm you are over 18.", msg);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            float bw = 150f, bh = 42f, by = py + h - bh - 22f, gap = 24f;
            if (UITheme.Button(new Rect(px + w * 0.5f - bw - gap * 0.5f, by, bw, bh), "Back", btn, true))
            {
                _adultPrompt = false;   // cancel: popup away, background restored, stays OFF
                _adultMode = false;
            }
            var keepA = GUI.backgroundColor; GUI.backgroundColor = UITheme.GoodTint;
            bool goA = UITheme.Button(new Rect(px + w * 0.5f + gap * 0.5f, by, bw, bh), "Continue", btn);
            GUI.backgroundColor = keepA;
            if (goA)
            {
                // Confirmed 18+: keep the screen darkened and move to the knowledge quiz. Adult
                // mode only turns on after a correct answer (in DrawAdultQuiz).
                _adultPrompt = false;
                _adultQuiz = true;
                NextQuizQuestion();
            }
        }

        // Serve a fresh random question. Random.Range is fine here (customize UI, not the sim).
        void NextQuizQuestion()
        {
            int n = AdultQuiz.Bank.Length;
            if (n <= 0) { _adultQuiz = false; _adultMode = true; return; }   // empty bank: just pass
            int next = Random.Range(0, n);
            if (n > 1 && next == _quizIdx) next = (next + 1) % n;   // avoid repeating the same one back-to-back
            _quizIdx = next;
            _quizPick = -1;
            _quizFeedbackUntil = 0f;
        }

        // Stage-2 modal: a random multiple-choice question. Wrong pick flashes red (with the correct
        // one green) then serves the next question; a correct pick flashes green then closes the
        // modal with adult mode ON. Non-serious gate - you can always Back out.
        void DrawAdultQuiz()
        {
            // Guard BEFORE any drawing, so a stale index can't flash a dim over the menu and return.
            if (_quizIdx < 0 || _quizIdx >= AdultQuiz.Bank.Length) { NextQuizQuestion(); return; }
            var q = AdultQuiz.Bank[_quizIdx];

            float w = 560f, h = 380f;
            float px = MenuScale.Width * 0.5f - w * 0.5f, py = MenuScale.Height * 0.5f - h * 0.5f;
            ModalPanel(new Rect(px, py, w, h));

            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UITheme.Label(new Rect(px + 20f, py + 12f, w - 40f, 20f), "ADULT KNOWLEDGE CHECK", hdr);
            var msg = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(px + 30f, py + 36f, w - 60f, 70f), q.Text, msg);

            bool feedback = _quizPick >= 0;   // a pick is being shown with red/green outlines
            var ansStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            float ax = px + 40f, aw = w - 80f, ah = 44f, agap = 12f, ay0 = py + 118f;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(ax, ay0 + i * (ah + agap), aw, ah);

                // Use GUI.Button's own return value to detect the click. GUI.Button consumes the
                // mouse event internally, so a separate Event.current MouseDown check right after it
                // never fires (the event is already Used) - that was the bug: _quizPick stayed -1
                // and the quiz never resolved. Freeze the buttons while feedback shows so a second
                // click can't re-pick mid-flash.
                bool prev = GUI.enabled;
                if (feedback) GUI.enabled = false;
                bool clicked = UITheme.Button(r, "   " + q.A[i], ansStyle);
                GUI.enabled = prev;

                if (feedback)
                {
                    // Outline the correct answer green; if the user picked a wrong one, outline it red.
                    var oc = GUI.color;
                    if (i == q.Correct)
                    {
                        UITheme.Glow(new Rect(r.x - 10f, r.y - 6f, r.width + 20f, r.height + 12f),
                                     new Color(UITheme.Green.r, UITheme.Green.g, UITheme.Green.b, 0.18f));
                        GUI.color = UITheme.Green; DrawRectOutline(r, 3f);
                    }
                    else if (i == _quizPick) { GUI.color = UITheme.Red; DrawRectOutline(r, 3f); }
                    GUI.color = oc;
                }
                else if (clicked)
                {
                    _quizPick = i;
                    _quizFeedbackUntil = Time.unscaledTime + (i == q.Correct ? 0.6f : 0.9f);
                }
            }

            // After the feedback window: correct -> finish (adult mode ON); wrong -> next question.
            if (feedback && Time.unscaledTime >= _quizFeedbackUntil)
            {
                if (_quizPick == q.Correct) { _adultQuiz = false; _adultMode = true; _quizIdx = -1; }
                else NextQuizQuestion();
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            UITheme.Label(new Rect(px + 20f, py + h - 44f, w - 40f, 18f), "Answer correctly to enable Adult Mode.", note);
            var backBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (UITheme.Button(new Rect(px + w - 110f, py + h - 34f, 90f, 26f), "Cancel", backBtn, true))
            {
                _adultQuiz = false; _adultMode = false; _quizIdx = -1;
            }
        }

        // Confirmation modal when leaving the Skill stage with points spent in the Third Leg tab.
        // Reports the share of the whole skill-point pool sunk into it. Back stays on Skill; Continue
        // advances to Name. Drawn last in OnGUI on top of the dimmed menu (same look as the age gate).
        void DrawThirdLegPrompt()
        {
            int pct = Mathf.RoundToInt(SkillTree.ThirdLegSpent / (float)SkillTree.Budget * 100f);

            float w = 460f, h = 200f;
            float px = MenuScale.Width * 0.5f - w * 0.5f, py = MenuScale.Height * 0.5f - h * 0.5f;
            ModalPanel(new Rect(px, py, w, h));

            var msg = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(px + 30f, py + 26f, w - 60f, 90f),
                $"{pct}% of your points went to your penis. Continue?", msg);

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            float bw = 150f, bh = 42f, by = py + h - bh - 22f, gap = 24f;
            if (UITheme.Button(new Rect(px + w * 0.5f - bw - gap * 0.5f, by, bw, bh), "Back", btn, true))
                _thirdLegPrompt = false;   // stay on the Skill stage
            var keepT = GUI.backgroundColor; GUI.backgroundColor = UITheme.GoodTint;
            bool goT = UITheme.Button(new Rect(px + w * 0.5f + gap * 0.5f, by, bw, bh), "Continue", btn);
            GUI.backgroundColor = keepT;
            if (goT)
            {
                _thirdLegPrompt = false;
                _stage += 1;               // Skill -> Name (Skill is never the SkipSkill case here)
            }
        }

        // Every stage: click-drag anywhere on the preview to turn the model. Only grabs when
        // the press lands inside the preview rect, so control widgets elsewhere are unaffected.
        void HandleModelDrag(Rect previewRect)
        {
            if (ModalUp) { _draggingModel = false; return; }   // see ModalUp
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && previewRect.Contains(e.mousePosition))
            {
                _draggingModel = true; _lastDragX = e.mousePosition.x; e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingModel)
            {
                _preview.AddYaw((e.mousePosition.x - _lastDragX) * 0.6f);
                _lastDragX = e.mousePosition.x; e.Use();
            }
            else if (e.type == EventType.MouseUp && _draggingModel && e.button == 0)
            {
                _draggingModel = false; e.Use();
            }
        }

        // ------------------------------------------------------------- Body stage
        void BodyStage(float x, float y, float pw, float ph)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = UITheme.Ink } };
            float lx = x + 30f, lw = pw - 60f, row = y + 56f;

            var sp = Species.Current;

            // ADULT MODE toggle, above every other body control. Flipping it ON opens a modal
            // age-confirmation popup (drawn in OnGUI); _adultMode only becomes true once confirmed.
            // Flipping OFF is immediate. GUI.Toggle returns the new checkbox state each frame.
            // Only offered by species that have the anatomy for it (SpeciesDef.AllowsAdult); for the
            // rest the row is not drawn at all and the following controls move up into its space.
            if (sp.AllowsAdult)
            {
                // A compact chip rather than a full-width banner. The old rect was the whole 500px
                // content width at 15pt, so the one novelty switch on the screen was the largest
                // control on it. Width is hand-set instead of derived: it only has to fit the box
                // plus the label, and it must NOT track lw, or it goes back to spanning the panel.
                var togStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 12, fontStyle = FontStyle.Bold };
                bool want = GUI.Toggle(new Rect(lx, row, 148f, 20f), _adultMode, "  ADULT MODE", togStyle);
                if (want && !_adultMode && !_adultPrompt) _adultPrompt = true;   // arm the confirm popup
                else if (!want && _adultMode) _adultMode = false;                // turn off immediately
                // Mirror the resolved state into the appearance so it drives the model build + commits +
                // networks (covers quiz-success, toggle-off, and cancel uniformly). The OnGUI appearance
                // diff (ApprEquals) then rebuilds the preview when it changes.
                PlayerProfile.Appearance.Adult = _adultMode;
                row += 28f;
            }
            else _adultMode = false;   // also keeps the Third Leg skill tab hidden for this species

            // Axis label, unit, numeric format and range all come from the species (SpeciesAxis.Read),
            // because a horse is measured at the withers and an elephant's mass is nowhere near the
            // human band. Human reads "Height:  1.80 m" / "Weight:  75 kg" exactly as before.
            UITheme.Label(new Rect(lx, row, lw, 20f), sp.Size.Read(_height), st); row += 24f;
            _height = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _height, PlayerProfile.MinHeight, PlayerProfile.MaxHeight); row += 40f;

            UITheme.Label(new Rect(lx, row, lw, 20f), sp.Mass.Read(_weight), st); row += 24f;
            _weight = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _weight, PlayerProfile.MinWeight, PlayerProfile.MaxWeight); row += 44f;

            // Strong foot: two toggle buttons. The selected one is tinted bright green with
            // a bold label + check; the other is dimmed so the choice is unmistakable.
            UITheme.Label(new Rect(lx, row, lw, 20f), "Strong foot:", st); row += 24f;
            float bw = (lw - 10f) * 0.5f;
            if (FootButton(new Rect(lx, row, bw, 34f), "Left", _leftFooted))  _leftFooted = true;
            if (FootButton(new Rect(lx + bw + 10f, row, bw, 34f), "Right", !_leftFooted)) _leftFooted = false;
            row += 46f;

            // Commit body working values so traits compute off them.
            PlayerProfile.Height = _height;
            PlayerProfile.Weight = _weight;

            // Lower region: sub-index 0 (BODY) has nothing further of its own now - the sliders/
            // foot buttons above already cover it - so only the selected appearance sub-menu draws
            // anything down here, switched by the ‹ › arrows beside the title.
            if (_bodySub != 0)
            {
                var slot = SlotAt(_bodySub);
                if (slot != null) SlotSubMenu(slot, lx, row, lw, y + ph - 60f);
            }
        }

        // Sub-menu tab set: index 0 is the BODY trait readout, 1..N the current species' slots.
        static int SubCount => 1 + (Species.Current.Slots?.Length ?? 0);

        static SpeciesSlot SlotAt(int sub)
        {
            var slots = Species.Current.Slots;
            int i = sub - 1;
            return slots != null && i >= 0 && i < slots.Length ? slots[i] : null;
        }

        static string SubName(int sub) => sub == 0 ? "BODY" : (SlotAt(sub)?.Tab ?? "BODY");

        static bool ApprEquals(PlayerAppearance a, PlayerAppearance b)
            => a.SpeciesId == b.SpeciesId
               && a.HairStyle == b.HairStyle && a.FacialStyle == b.FacialStyle && a.Accessory == b.Accessory
               && a.Adult == b.Adult
               && Mathf.Approximately(a.MemberLen, b.MemberLen)
               && Mathf.Approximately(a.MemberGirth, b.MemberGirth)
               && Mathf.Approximately(a.BallSize, b.BallSize)
               && ApproxColor(a.Skin, b.Skin) && ApproxColor(a.HairColor, b.HairColor)
               && ApproxColor(a.FacialColor, b.FacialColor) && ApproxColor(a.AccessoryColor, b.AccessoryColor);

        // A reusable color-wheel picker: draws the wheel in `wheelRect`, and on click/drag inside
        // it returns the picked colour (else returns `current`). Consumes the event so it doesn't
        // fall through. (HandleWheel is hard-wired to _brushColor; this is the generic version.)
        Color WheelPick(Rect wheelRect, Color current)
        {
            GUI.DrawTexture(wheelRect, _wheel);
            if (ModalUp) return current;   // see ModalUp - draw it, but never take the click
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && wheelRect.Contains(e.mousePosition))
            {
                float fx = (e.mousePosition.x - wheelRect.x) / wheelRect.width;
                float fy = 1f - (e.mousePosition.y - wheelRect.y) / wheelRect.height;
                int wx = Mathf.Clamp(Mathf.RoundToInt(fx * (_wheel.width - 1)), 0, _wheel.width - 1);
                int wy = Mathf.Clamp(Mathf.RoundToInt(fy * (_wheel.height - 1)), 0, _wheel.height - 1);
                Color c = _wheel.GetPixel(wx, wy);
                if (c.a > 0.5f) { e.Use(); return new Color(c.r, c.g, c.b, 1f); }
            }
            return current;
        }

        // Value/brightness bar shown under a hue wheel. The wheel's value is fixed at 1, so it can't
        // reach dark shades; this bar covers that axis. Left = white, middle = the currently-picked
        // hue at full brightness, right = black - so dragging left brightens/desaturates toward
        // white and dragging right darkens toward black. Returns the new colour (else `current`).
        Texture2D _valueBar;      // cached gradient (rebuilt when the hue changes)
        float _valueBarHue = -1f, _valueBarSat = -1f;
        Color ValueBarSample(float h, float s, float t)
        {
            Color hue = Color.HSVToRGB(h, s, 1f);
            return t <= 0.5f ? Color.Lerp(Color.white, hue, t / 0.5f)
                             : Color.Lerp(hue, Color.black, (t - 0.5f) / 0.5f);
        }
        Color ValueBar(Rect bar, Color current)
        {
            Color.RGBToHSV(current, out float h, out float s, out _);
            // A near-grey current colour has no meaningful hue; keep a stable hue for the gradient.
            if (s < 0.02f) h = _valueBarHue >= 0f ? _valueBarHue : 0f;
            float satForBar = Mathf.Max(s, 0.85f);   // show a vivid hue mid-bar even if current is desaturated

            // (Re)build the gradient texture when the hue/sat changes.
            if (_valueBar == null || !Mathf.Approximately(h, _valueBarHue) || !Mathf.Approximately(satForBar, _valueBarSat))
            {
                const int n = 128;
                if (_valueBar == null) _valueBar = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                var px = new Color32[n];
                for (int i = 0; i < n; i++) px[i] = ValueBarSample(h, satForBar, i / (float)(n - 1));
                _valueBar.SetPixels32(px); _valueBar.Apply();
                _valueBarHue = h; _valueBarSat = satForBar;
            }
            GUI.DrawTexture(bar, _valueBar);

            // Handle position for `current`. The old readback reverse-engineered t from HSV, but on
            // the white->hue (left) half saturation is NOT linear in t (an RGB lerp of white->hue
            // doesn't give s = 2t), so the handle warped. Instead find the t whose gradient sample
            // best matches current - the exact inverse of ValueBarSample, so both halves track.
            float tCur = 0.5f;
            float best = float.MaxValue;
            for (int i = 0; i <= 64; i++)
            {
                float tt = i / 64f;
                Color c = ValueBarSample(h, satForBar, tt);
                float d = (c.r - current.r) * (c.r - current.r) + (c.g - current.g) * (c.g - current.g) + (c.b - current.b) * (c.b - current.b);
                if (d < best) { best = d; tCur = tt; }
            }

            if (ModalUp) return current;   // see ModalUp
            Event e = Event.current;
            Color result = current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && bar.Contains(e.mousePosition))
            {
                float t = Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width);
                tCur = t;
                result = ValueBarSample(h, satForBar, t);
                e.Use();
            }

            // Handle marker at the current position.
            float hx = bar.x + tCur * bar.width;
            var hRect = new Rect(hx - 2f, bar.y - 2f, 4f, bar.height + 4f);
            var pc = GUI.color; GUI.color = Color.white; DrawRectOutline(hRect, 2f); GUI.color = pc;
            return result;
        }

        // A row/grid of preset colour swatches; returns the picked colour (else `current`).
        Color SwatchRow(float x, float y, float w, Color current, Color[] cols, float sw = 30f, float gap = 6f)
        {
            Color result = current;
            int cols_n = Mathf.Max(1, Mathf.FloorToInt((w + gap) / (sw + gap)));
            for (int i = 0; i < cols.Length; i++)
            {
                float cx = x + (i % cols_n) * (sw + gap);
                float cy = y + (i / cols_n) * (sw + gap);
                var r = new Rect(cx, cy, sw, sw);
                bool on = ApproxColor(current, cols[i]);
                // Gold halo behind the pick, so the selection reads at a glance in a dense grid
                // instead of resting on a 3px outline alone.
                if (on) UITheme.Glow(new Rect(r.x - 6f, r.y - 6f, r.width + 12f, r.height + 12f),
                                     new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.35f));
                var pc = GUI.color; GUI.color = cols[i];
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = on ? UITheme.Gold : new Color(0f, 0f, 0f, 0.6f);
                DrawRectOutline(r, on ? 3f : 1f);
                GUI.color = pc;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) result = cols[i];
            }
            return result;
        }

        // Draw an option grid over Cosmetics entries (0..count-1), grouped by an optional label
        // function. Returns the newly-selected index (or `current`). Headgear cells can be
        // disabled. Used by every style slot's sub-menu.
        int OptionGrid(float x, float y, float w, float h, int count, int current,
                       System.Func<int, string> label, System.Func<int, bool> enabled)
        {
            int result = current;
            const float cw = 96f, chh = 30f, gap = 6f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w + gap) / (cw + gap)));
            int rows = Mathf.CeilToInt(count / (float)cols);
            var view = new Rect(0, 0, cols * (cw + gap), rows * (chh + gap));
            _apprScroll = GUI.BeginScrollView(new Rect(x, y, w, h), _apprScroll, view);
            var lbl = new GUIStyle(GUI.skin.button) { fontSize = 11, wordWrap = true };
            var lblSel = new GUIStyle(lbl) { fontStyle = FontStyle.Bold };
            lblSel.normal.textColor = UITheme.Gold;
            for (int i = 0; i < count; i++)
            {
                float cx = (i % cols) * (cw + gap), cy = (i / cols) * (chh + gap);
                var r = new Rect(cx, cy, cw, chh);
                bool en = enabled == null || enabled(i);
                bool sel = i == current;
                var prevEnabled = GUI.enabled;
                GUI.enabled = en;
                // Themed toggle carries the lit plate + gold underline, so the hand-drawn tint and
                // selection outline are gone.
                if (UITheme.Toggle(r, label(i), sel, sel ? lblSel : lbl) && en) result = i;
                GUI.enabled = prevEnabled;
            }
            GUI.EndScrollView();
            return result;
        }

        /// <summary>
        /// The ONE cosmetic sub-menu, for any species and any slot. Replaced the four hand-written
        /// Skin/Hair/Facial/Accessory menus: they differed only in which appearance field they wrote,
        /// which option list they counted, and their wheel heading, all of which are now data on
        /// SpeciesSlot + SpeciesCosmetics. Layout and widgets are unchanged, so the human screens
        /// look identical to before.
        /// </summary>
        void SlotSubMenu(SpeciesSlot slot, float lx, float row, float lw, float bottom)
        {
            byte sp = Species.SelectedId;

            // ---- Skin / coat / hide / fur / plumage: preset swatches over a free colour wheel ----
            if (slot.Kind == SlotKind.Skin)
            {
                var grp = new GUIStyle(GUI.skin.label)
                { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Gold } };
                UITheme.Label(new Rect(lx, row, lw, 20f), SpeciesCosmetics.SkinGroupLabel(sp), grp); row += 24f;
                PlayerProfile.Appearance.Skin = SwatchRow(lx, row, lw, PlayerProfile.Appearance.Skin,
                                                         SpeciesCosmetics.SkinSwatches(sp), 34f, 8f);
                row += 2 * (34f + 8f) + 12f;   // two rows of swatches
                UITheme.Label(new Rect(lx, row, lw, 20f), slot.ColorLabel, grp); row += 24f;
                float skinWsz = Mathf.Min(lw, bottom - row, 150f);
                PlayerProfile.Appearance.Skin = WheelPick(new Rect(lx, row, skinWsz, skinWsz), PlayerProfile.Appearance.Skin);
                return;
            }

            // ---- Style slots: option grid on the left, colour wheel + value bar on the right ----
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = UITheme.Ink } };
            bool headgearRule = slot.Kind == SlotKind.StyleC && SpeciesCosmetics.HasHeadgearRule(sp);
            float gridW = lw - 170f;
            float gridH = bottom - row - (headgearRule ? 24f : 4f);   // leave room for the hint line

            SetSlotIndex(slot.Kind, OptionGrid(lx, row, gridW, gridH,
                SpeciesCosmetics.Count(sp, slot.Kind), SlotIndex(slot.Kind),
                i => SpeciesCosmetics.Label(sp, slot.Kind, i),
                i => SpeciesCosmetics.Enabled(sp, slot.Kind, i)));

            // Human head rule, both directions: putting hair back on drops a headgear accessory,
            // and headgear cells stay disabled while there is hair (the Enabled callback above).
            // Hair and headgear occupy the same head, see Cosmetics.AttachAppearance.
            if (slot.Kind == SlotKind.StyleA && SpeciesCosmetics.HasHeadgearRule(sp)
                && !Cosmetics.IsBald(PlayerProfile.Appearance.HairStyle)
                && Cosmetics.AccessoryIsHeadgear(PlayerProfile.Appearance.Accessory))
                PlayerProfile.Appearance.Accessory = 0;

            if (headgearRule && !Cosmetics.IsBald(PlayerProfile.Appearance.HairStyle))
            {
                var hint = new GUIStyle(st) { fontSize = 11, normal = { textColor = UITheme.Gold } };
                UITheme.Label(new Rect(lx, row + gridH + 2f, gridW, 20f), "Headgear needs Bald hair.", hint);
            }

            float wx = lx + gridW + 14f, wsz = Mathf.Min(150f, lw - gridW - 14f);
            UITheme.Label(new Rect(wx, row, wsz, 18f), slot.ColorLabel, st);
            Color tint = WheelPick(new Rect(wx, row + 20f, wsz, wsz), SlotColor(slot.Kind));
            // The HSV wheel is fixed at full value so it can't reach dark shades; a value bar under
            // it goes white -> the picked hue -> black.
            SetSlotColor(slot.Kind, ValueBar(new Rect(wx, row + 26f + wsz, wsz, 22f), tint));
        }

        // The only four places that know which PlayerAppearance field a SlotKind drives. Everything
        // else works in terms of the slot, which is what lets one screen serve every species.
        static int SlotIndex(SlotKind k) => k switch
        {
            SlotKind.StyleA => PlayerProfile.Appearance.HairStyle,
            SlotKind.StyleB => PlayerProfile.Appearance.FacialStyle,
            SlotKind.StyleC => PlayerProfile.Appearance.Accessory,
            _ => 0,
        };

        static void SetSlotIndex(SlotKind k, int v)
        {
            switch (k)
            {
                case SlotKind.StyleA: PlayerProfile.Appearance.HairStyle = v; break;
                case SlotKind.StyleB: PlayerProfile.Appearance.FacialStyle = v; break;
                case SlotKind.StyleC: PlayerProfile.Appearance.Accessory = v; break;
            }
        }

        static Color SlotColor(SlotKind k) => k switch
        {
            SlotKind.StyleA => PlayerProfile.Appearance.HairColor,
            SlotKind.StyleB => PlayerProfile.Appearance.FacialColor,
            SlotKind.StyleC => PlayerProfile.Appearance.AccessoryColor,
            _ => PlayerProfile.Appearance.Skin,
        };

        static void SetSlotColor(SlotKind k, Color c)
        {
            switch (k)
            {
                case SlotKind.StyleA: PlayerProfile.Appearance.HairColor = c; break;
                case SlotKind.StyleB: PlayerProfile.Appearance.FacialColor = c; break;
                case SlotKind.StyleC: PlayerProfile.Appearance.AccessoryColor = c; break;
                case SlotKind.Skin:   PlayerProfile.Appearance.Skin = c; break;
            }
        }


        // A foot-choice toggle. Selected = lit green plate, bold gold label, gold underline;
        // unselected = the standard button plate in dim text. Returns true if clicked this frame.
        bool FootButton(Rect r, string label, bool selected)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = selected ? UITheme.Gold : UITheme.Dim;
            return UITheme.Toggle(r, label, selected, style, UITheme.GoodTint);
        }

        // ------------------------------------------------------------- Skill tree stage
        // Drawn as an ACTUAL node graph: nodes at their grid positions, connector lines to
        // prerequisites, clickable icon badges (left-click buys, right-click refunds), and
        // a detail strip for the selected node.
        string _selNode;   // currently selected node id (for the detail strip)

        // One style for every tab strip on this screen (skill categories, jersey designs), so they
        // all read as the same control.
        static GUIStyle TabStyle(bool sel)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
            s.normal.textColor = sel ? UITheme.Gold : UITheme.Dim;
            return s;
        }

        void SkillStage(float x, float y, float pw, float ph)
        {
            float lx = x + 28f, lw = pw - 56f;

            // Keep the appendage size multipliers in the appearance in sync with the tree, so the
            // live preview grows as Third Leg nodes are bought/refunded (via the ApprEquals diff).
            SyncAdultDims();

            var big = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Gold } };
            UITheme.Shadowed(new Rect(lx, y + 52f, lw, 24f), $"Skill points: {SkillTree.Remaining} / {SkillTree.Budget}", big, UITheme.Gold, 0.7f, 1.5f);

            // Category tabs. The seven football categories are SHARED by every species and shown as
            // usual. Two categories are NOT among them and ride conditional tabs appended at the end:
            // ThirdLeg (adult mode only) and Instinct (only for a species that has Instinct nodes).
            // One shared tree keeps the stat card comparable across species.
            var sp = Species.Current;
            bool hasInstinct = sp.InstinctTab != null && SkillTree.HasInstinct(Species.SelectedId);

            var cats = (SkillTree.Category[])System.Enum.GetValues(typeof(SkillTree.Category));
            int realCount = 0;
            foreach (var c in cats)
                if (c != SkillTree.Category.ThirdLeg && c != SkillTree.Category.Instinct) realCount++;
            int tabCount = realCount + (_adultMode ? 1 : 0) + (hasInstinct ? 1 : 0);
            float tw = (lw - (tabCount - 1) * 4f) / tabCount;
            int ti = 0;
            foreach (var c in cats)
            {
                // Shown separately below, so they must not consume a slot in this loop either.
                if (c == SkillTree.Category.ThirdLeg || c == SkillTree.Category.Instinct) continue;
                bool sel = _skillCat == c && !_thirdLegTab && !_instinctTab;
                if (UITheme.Toggle(new Rect(lx + ti * (tw + 4f), y + 84f, tw, 26f), c.ToString(), sel, TabStyle(sel)))
                    { _skillCat = c; _thirdLegTab = false; _instinctTab = false; }
                ti++;
            }
            // The two conditional tabs share a running index so they pack tight whether one or both
            // are present (`ti` continues from realCount).
            if (_adultMode)
            {
                if (UITheme.Toggle(new Rect(lx + ti * (tw + 4f), y + 84f, tw, 26f), "Third Leg", _thirdLegTab, TabStyle(_thirdLegTab)))
                    { _thirdLegTab = true; _instinctTab = false; }
                ti++;
            }
            else _thirdLegTab = false;   // adult mode off -> can't be on this tab
            if (hasInstinct)
            {
                // Labelled per species ("Primate", "Ratite", ...), not "Instinct".
                if (UITheme.Toggle(new Rect(lx + ti * (tw + 4f), y + 84f, tw, 26f), sp.InstinctTab, _instinctTab, TabStyle(_instinctTab)))
                    { _instinctTab = true; _thirdLegTab = false; }
                ti++;
            }
            else _instinctTab = false;   // this species has no Instinct nodes

            // Which category's graph we're drawing: a conditional tab shows its own tree, else the
            // selected football category. Everything below reads `drawCat`.
            var drawCat = _thirdLegTab ? SkillTree.Category.ThirdLeg
                        : _instinctTab ? SkillTree.Category.Instinct
                        : _skillCat;

            // Graph area for the selected category.
            var area = new Rect(lx, y + 120f, lw, ph - 120f - 130f);
            const float nodeSz = 46f;
            int maxTier = 3;   // rows 0..3
            float colPad = nodeSz;
            float usableW = area.width - colPad * 2f;
            float rowGap = (area.height - nodeSz) / maxTier;

            // Node centre for a node in this category.
            Vector2 Centre(SkillTree.Node n) => new Vector2(
                area.x + colPad + n.GridX * usableW,
                area.y + nodeSz * 0.5f + n.GridY * rowGap);

            // Pass 1: connector lines (node -> its prerequisite), drawn under the badges.
            foreach (var n in SkillTree.InCategory(drawCat))
            {
                if (string.IsNullOrEmpty(n.Requires)) continue;
                var req = SkillTree.ById(n.Requires);
                if (req == null) continue;
                bool lit = SkillTree.Owned.Contains(n.Id);
                DrawLine(Centre(req), Centre(n), lit ? UITheme.Green : new Color(0.30f, 0.33f, 0.40f), lit ? 3f : 2f);
            }

            // Pass 2: node badges.
            var costSt = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            foreach (var n in SkillTree.InCategory(drawCat))
            {
                Vector2 c = Centre(n);
                bool owned = SkillTree.Owned.Contains(n.Id);
                bool canBuy = SkillTree.CanBuy(n);
                bool capstone = n.Perk != null;
                var r = new Rect(c.x - nodeSz * 0.5f, c.y - nodeSz * 0.5f, nodeSz, nodeSz);

                // Blue halo on a buyable node, so the next legal purchase is obvious in the graph.
                if (canBuy && !owned)
                    UITheme.Glow(new Rect(r.x - 8f, r.y - 8f, r.width + 16f, r.height + 16f),
                                 new Color(UITheme.Blue.r, UITheme.Blue.g, UITheme.Blue.b, 0.22f));
                // Rounded plate instead of a hard square. No edge colour: Chip's edge is a LEFT
                // spine, which reads wrong on a square badge.
                UITheme.Chip(r, owned ? new Color(0.13f, 0.33f, 0.20f, 0.98f)
                              : canBuy ? new Color(0.15f, 0.20f, 0.33f, 0.98f)
                              : new Color(0.09f, 0.10f, 0.13f, 0.96f));
                var prev = GUI.color;
                // Capstone gets a gold ring.
                if (capstone) { GUI.color = UITheme.Gold; DrawRectOutline(r, 2f); }
                if (_selNode == n.Id) { GUI.color = Color.white; DrawRectOutline(new Rect(r.x-2,r.y-2,r.width+4,r.height+4), 2f); }

                // Procedural white line-art icon, tinted full for owned/buyable, dim for locked.
                var icon = SkillIcons.Get(n.Id);
                if (icon != null)
                {
                    GUI.color = (owned || canBuy) ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    float pad = 7f;
                    GUI.DrawTexture(new Rect(r.x + pad, r.y + pad - 3f, r.width - pad * 2f, r.height - pad * 2f - 4f),
                                    icon, ScaleMode.ScaleToFit, true);
                }
                GUI.color = prev;

                UITheme.Label(new Rect(r.x, r.yMax - 14f, r.width, 12f), owned ? "✓" : n.Cost.ToString(), costSt);

                // Click: select, then act. Clicking an OWNED node refunds it (and cascades
                // to every node built on top of it); clicking a buyable node buys it.
                // Right-click also refunds, for muscle memory.
                Event e = Event.current;
                if (!ModalUp && e.type == EventType.MouseDown && r.Contains(e.mousePosition))   // see ModalUp
                {
                    _selNode = n.Id;
                    if (owned) SkillTree.Refund(n);        // left OR right click on owned = refund (cascades)
                    else if (e.button == 1) SkillTree.Refund(n);
                    else if (canBuy) SkillTree.Buy(n);
                    e.Use();
                }
            }

            // Detail strip for the selected node.
            var selNode = _selNode != null ? SkillTree.ById(_selNode) : null;
            if (selNode != null && selNode.Cat == drawCat)
            {
                float dy = y + ph - 124f;
                var box = new Rect(lx, dy, lw, 58f);
                UITheme.Chip(box, new Color(0.10f, 0.12f, 0.17f, 0.97f), selNode.Perk != null ? UITheme.Gold : UITheme.Blue);
                var nameSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
                var descSt = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = UITheme.Dim } };
                string tag = selNode.Perk != null ? "  [CAPSTONE PERK]" : "";
                UITheme.Label(new Rect(box.x + 10f, box.y + 5f, lw - 130f, 18f), selNode.Name + tag, nameSt);
                UITheme.Label(new Rect(box.x + 10f, box.y + 26f, lw - 130f, 26f), selNode.Desc, descSt);

                var actBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
                var actRect = new Rect(box.xMax - 128f, box.y + 15f, 116f, 28f);
                if (SkillTree.Owned.Contains(selNode.Id))
                {
                    // Any owned node refunds; if dependents are built on it the refund
                    // cascades, so say so on the button.
                    bool cascades = SkillTree.HasOwnedDependents(selNode);
                    if (UITheme.Button(actRect, cascades ? "Refund chain" : $"Refund {selNode.Cost}", actBtn, true))
                        SkillTree.Refund(selNode);
                }
                else
                {
                    bool canBuy = SkillTree.CanBuy(selNode);
                    GUI.enabled = canBuy;
                    bool needReq = !string.IsNullOrEmpty(selNode.Requires) && !SkillTree.Owned.Contains(selNode.Requires);
                    var keepBuy = GUI.backgroundColor;
                    if (canBuy) GUI.backgroundColor = UITheme.GoodTint;
                    if (UITheme.Button(actRect, needReq ? "Needs prereq" : $"Buy {selNode.Cost}", actBtn)) SkillTree.Buy(selNode);
                    GUI.backgroundColor = keepBuy;
                    GUI.enabled = true;
                }
            }
        }

        // One-click build presets, overlaid down the left preview column during the skill
        // stage. Each wipes the tree and applies a themed spend; the currently-matching
        // preset (if any) is highlighted so you can see which build you're on.
        void SkillPresetButtons(Rect previewRect)
        {
            var presets = SkillTree.Presets;
            float edge = 24f, gap = 12f, pad = 10f;
            // Own column in the empty margin to the LEFT of the preview, not over the model.
            // Width fills the available margin (capped), right edge just left of the preview.
            float bw = Mathf.Min(200f, previewRect.x - edge - gap);
            float colX = Mathf.Max(edge, previewRect.x - gap - bw);

            float bh = 32f, bgap = 6f;
            float randBh = 30f, randGap = 14f;   // RANDOMIZE button sits above the QUICK BUILDS header
            // + 32 for the CLEAR ALL button below the preset list.
            float contentH = randBh + randGap + 26f + presets.Length * (bh + bgap) + 32f + 34f;
            float colY = previewRect.y + Mathf.Max(0f, (previewRect.height - contentH) * 0.5f);

            // Backing panel.
            UITheme.Panel(new Rect(colX - pad, colY - pad, bw + pad * 2f, contentH + pad * 2f), UITheme.Gold);

            // RANDOMIZE: roll a fresh legal random build (random node count from random areas). The
            // radar + 3D preview read live from SkillTree, so no explicit apply is needed.
            var randRect = new Rect(colX, colY, bw, randBh);
            UITheme.Chip(randRect, new Color(0.20f, 0.15f, 0.32f, 0.98f));
            var prevR = GUI.color; GUI.color = UITheme.Gold; DrawRectOutline(randRect, 1.5f); GUI.color = prevR;
            var shuf = SkillIcons.Get("_shuffle");
            if (shuf != null)
            {
                var p2 = GUI.color; GUI.color = Color.white;
                GUI.DrawTexture(new Rect(randRect.x + 8f, randRect.y + 5f, 20f, 20f), shuf, ScaleMode.ScaleToFit, true);
                GUI.color = p2;
            }
            var randSt = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            UITheme.Label(new Rect(randRect.x + 20f, randRect.y, randRect.width - 20f, randRect.height), "RANDOMIZE", randSt);
            if (GUI.Button(randRect, GUIContent.none, GUIStyle.none)) { SkillTree.Randomize(); _selNode = null; }

            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            UITheme.Label(new Rect(colX, colY + randBh + randGap, bw, 20f), "QUICK BUILDS", hdr);

            float row = colY + randBh + randGap + 26f;
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                bool active = PresetMatches(p);       // fully owned
                bool canAdd = !active && PresetCanAdd(p);
                var r = new Rect(colX, row, bw, bh);
                // Green = applied, normal = clickable, dark = nothing left it can afford.
                UITheme.Chip(r, active ? new Color(0.13f, 0.34f, 0.21f, 0.98f)
                              : canAdd ? new Color(0.12f, 0.14f, 0.19f, 0.96f)
                              : new Color(0.08f, 0.09f, 0.11f, 0.94f),
                             active ? UITheme.Green : (Color?)null);
                var prev = GUI.color;
                if (active) { GUI.color = UITheme.Gold; DrawRectOutline(r, 2f); }
                GUI.color = prev;

                var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                                                        normal = { textColor = active ? UITheme.Ink : canAdd ? UITheme.Dim : UITheme.Faint } };
                UITheme.Label(r, p.Name, lbl);
                // Presets TOGGLE and STACK: clicking an unapplied build adds it on top of the current
                // spend (skipping anything unaffordable), so several can be combined; clicking an
                // APPLIED (green) one deselects it and clears every node in the areas it covers,
                // refunding those points.
                if ((active || canAdd) && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (active) SkillTree.RemovePreset(p);
                    else SkillTree.ApplyPreset(p);
                    _selNode = null;
                }
                row += bh + bgap;
            }

            // CLEAR: presets no longer wipe the tree, so there has to be an explicit way to start over.
            var clearRect = new Rect(colX, row + 2f, bw, 26f);
            UITheme.Chip(clearRect, new Color(0.24f, 0.09f, 0.09f, 0.96f), UITheme.Red);
            var clearSt = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.84f, 0.82f) } };
            UITheme.Label(clearRect, "CLEAR ALL", clearSt);
            if (GUI.Button(clearRect, GUIContent.none, GUIStyle.none)) { SkillTree.Clear(); _selNode = null; }
            row += 30f;

            var note = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true, alignment = TextAnchor.UpperCenter, normal = { textColor = UITheme.Dim } };
            UITheme.Label(new Rect(colX, row + 2f, bw, 34f),
                      "Builds stack. Click green to remove.", note);
        }

        // A preset counts as APPLIED when every one of its nodes is owned. Presets now stack
        // additively (several can be active at once), so this is containment, not set equality.
        static bool PresetMatches(SkillTree.Preset p)
        {
            foreach (var id in p.Ids) if (!SkillTree.Owned.Contains(id)) return false;
            return true;
        }

        // Would clicking this preset grant anything? False when it's already fully applied, or when
        // nothing it wants can still be afforded (so the button can be shown as spent/unaffordable).
        static bool PresetCanAdd(SkillTree.Preset p)
        {
            foreach (var id in p.Ids)
            {
                var n = SkillTree.ById(id);
                if (n == null || SkillTree.Owned.Contains(id)) continue;
                if (SkillTree.ChainCost(n) <= SkillTree.Remaining) return true;
            }
            return false;
        }

        // Draw a straight line between two screen points using a rotated 1px texture.
        static void DrawLine(Vector2 a, Vector2 b, Color col, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var prev = GUI.color; var m = GUI.matrix;
            GUI.color = col;
            // Compose onto the CURRENT matrix rather than using GUIUtility.RotateAroundPivot, whose
            // pivot is screen-space: under MenuScale's scaled matrix that pivot is wrong and the
            // skill-tree connector lines fly off across the panel. (Same fix as StatRadar.Line.)
            GUI.matrix = m * Matrix4x4.TRS(a, Quaternion.Euler(0f, 0f, ang), Vector3.one)
                           * Matrix4x4.TRS(-a, Quaternion.identity, Vector3.one);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
            GUI.matrix = m; GUI.color = prev;
        }

        // Draw a rectangle outline (thickness t) using the current GUI.color.
        static void DrawRectOutline(Rect r, float t)
        {
            var tex = Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);                 // top
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);          // bottom
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);               // left
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);         // right
        }

        // ----------------------------------------------------------- Jersey stage
        void JerseyStage(float x, float y, float pw, float ph)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = UITheme.Ink } };
            float lx = x + 28f, top = y + 58f;

            // Eyedropper: while armed, the FIRST left-click anywhere grabs the exact screen
            // pixel under the cursor. Handled before any other control so it wins the click.
            if (_eyedropper && !_picking && !ModalUp)   // see ModalUp
            {
                Event ee = Event.current;
                if (ee.type == EventType.MouseDown && ee.button == 0)
                {
                    StartCoroutine(PickScreenPixel(ee.mousePosition));
                    ee.Use();
                }
                // Crosshair-ish cursor hint follows the mouse.
                var hintR = new Rect(Event.current.mousePosition.x + 12f, Event.current.mousePosition.y + 12f, 120f, 18f);
                var hs = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = UITheme.Gold } };
                UITheme.Label(hintR, "pick a colour", hs);
            }

            // --- FRONT / BACK draw-side tabs above the canvas ---
            float canvasSize = 260f;
            float halfTab = (canvasSize - 6f) * 0.5f;
            if (SideTab(new Rect(lx, top, halfTab, 24f), "FRONT", _drawSide == 0)) SetDrawSide(0);
            if (SideTab(new Rect(lx + halfTab + 6f, top, halfTab, 24f), "BACK", _drawSide == 1)) SetDrawSide(1);
            top += 30f;

            // --- Canvas shows ONLY the active region of the atlas (front or back) ---
            var canvasRect = new Rect(lx, top, canvasSize, canvasSize);
            float v0 = CurRegionY0 / (float)AtlasH;
            var texCoords = new Rect(0f, v0, 1f, RegH / (float)AtlasH);
            GUI.DrawTextureWithTexCoords(canvasRect, _canvas, texCoords);
            UITheme.Frame(canvasRect);   // border ONLY: the themed box plate would cover the canvas

            HandlePaint(canvasRect);
            HandleBrushResize(canvasRect);
            DrawBrushCursor(canvasRect, canvasSize);

            // Undo / Clear overlaid at the TOP-RIGHT corner of the canvas.
            var miniBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
            float ubw = 56f, ubh = 22f, ugap = 4f;
            var clearR = new Rect(canvasRect.xMax - ubw - 4f, canvasRect.y + 4f, ubw, ubh);
            var undoR = new Rect(clearR.x - ubw - ugap, canvasRect.y + 4f, ubw, ubh);
            if (UITheme.Button(undoR, "Undo", miniBtn)) Undo();
            if (UITheme.Button(clearR, "Clear", miniBtn, true)) ClearPaint();

            // --- Tools column (right of the canvas) ---
            float tx = lx + canvasSize + 16f, tw = (x + pw - 28f) - tx, tr = top;
            UITheme.Label(new Rect(tx, tr, tw, 18f), "Color", st); tr += 20f;
            float wheelSize = Mathf.Min(tw, 130f);
            var wheelRect = new Rect(tx, tr, wheelSize, wheelSize);
            GUI.DrawTexture(wheelRect, _wheel);
            HandleWheel(wheelRect);
            tr += wheelSize + 8f;

            // Current color swatch + eyedropper icon button beside it.
            var prev = GUI.color; GUI.color = _brushColor;
            GUI.DrawTexture(new Rect(tx, tr, 40f, 20f), Texture2D.whiteTexture);
            GUI.color = prev;
            // Hairline outline, not a box: the themed box plate is opaque and would hide the swatch.
            var swc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.30f);
            DrawRectOutline(new Rect(tx, tr, 40f, 20f), 1f); GUI.color = swc;
            EnsureEyedropperIcon();
            var edRect = new Rect(tx + 48f, tr - 4f, 28f, 28f);   // square button sized to the icon
            if (GUI.Button(edRect, GUIContent.none)) _eyedropper = !_eyedropper;
            // Highlight ring when armed.
            if (_eyedropper)
            {
                var hc = GUI.color; GUI.color = UITheme.Gold;
                DrawRectOutline(edRect, 2f); GUI.color = hc;
            }
            // Draw the icon inset within the button.
            GUI.DrawTexture(new Rect(edRect.x + 4f, edRect.y + 4f, 20f, 20f), _eyedropperIcon);
            tr += 30f;

            UITheme.Label(new Rect(tx, tr, tw, 18f), $"Brush size: {_brushSize:0}", st); tr += 20f;
            _brushSize = GUI.HorizontalSlider(new Rect(tx, tr, tw, 18f), _brushSize, 2f, 40f); tr += 26f;
            UITheme.Label(new Rect(tx, tr, tw, 18f), $"Opacity: {_brushOpacity:0.00}", st); tr += 20f;
            _brushOpacity = GUI.HorizontalSlider(new Rect(tx, tr, tw, 18f), _brushOpacity, 0.1f, 1f); tr += 28f;

            // (Name/number colour is chosen on the NAME stage, to the right of the back preview.)

            // --- Predrawn design picker (tabs + swatch grid) below the canvas ---
            float pickTop = top + canvasSize + 10f;
            DesignPicker(lx, pickTop, pw - 56f, (y + ph - 52f) - pickTop);
        }

        // Set the name/number colour and re-bake identity so it updates live on the model.
        void SetIdentityColor(Color c)
        {
            _identityColor = new Color(c.r, c.g, c.b, 1f);
            // Rebuild the base layer (design + freshly-coloured identity) and reset paint on top,
            // matching how ApplyDesign refreshes the live canvas.
            BuildCanvas();
        }

        // Read the exact colour of the screen pixel under `guiPos` (GUI coords, y-down) at the
        // end of the frame, set the brush colour to it, and disarm the eyedropper. ReadPixels
        // must run after the frame has rendered, hence the WaitForEndOfFrame.
        IEnumerator PickScreenPixel(Vector2 guiPos)
        {
            _picking = true;
            yield return new WaitForEndOfFrame();
            // guiPos is in the SCALED GUI space (see MenuScale); ReadPixels needs real device
            // pixels, so convert before sampling or the picked colour comes from the wrong pixel.
            Vector2 devPos = MenuScale.ToScreen(guiPos);
            int sx = Mathf.Clamp(Mathf.RoundToInt(devPos.x), 0, Screen.width - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(Screen.height - 1 - devPos.y), 0, Screen.height - 1); // GUI y-down -> screen y-up
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(sx, sy, 1, 1), 0, 0);
            tex.Apply();
            Color picked = tex.GetPixel(0, 0);
            Destroy(tex);
            // In a LINEAR colour-space project the framebuffer stores linear values, so a raw
            // read reinterpreted as sRGB looks too dark. Convert linear -> sRGB (.gamma) to
            // match what the eye saw on screen. No-op in a Gamma project.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                picked = picked.gamma;
            _brushColor = new Color(picked.r, picked.g, picked.b, 1f);
            _eyedropper = false;
            _picking = false;
        }

        // A FRONT/BACK tab button. Selected = lit plate, bold gold label.
        bool SideTab(Rect r, string label, bool selected)
        {
            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            };
            style.normal.textColor = selected ? UITheme.Gold : UITheme.Dim;
            return UITheme.Toggle(r, label, selected, style);
        }

        // Switch the region being drawn + snap the 3D preview to that side.
        void SetDrawSide(int side)
        {
            if (_drawSide == side) return;
            _drawSide = side;
            _painting = false;
            if (_preview != null) _preview.FaceSide(side == 1);
        }

        // Predrawn design picker: a row of category tabs + a scrollable swatch grid. Clicking
        // a swatch replaces the design on both regions (then the player can draw on top).
        void DesignPicker(float px, float py, float pwid, float pheight)
        {
            if (pheight < 60f) return;
            var tabs = (DesignTab[])System.Enum.GetValues(typeof(DesignTab));
            float tw = (pwid - (tabs.Length - 1) * 4f) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool sel = _designTab == tabs[i];
                if (UITheme.Toggle(new Rect(px + i * (tw + 4f), py, tw, 24f), tabs[i].ToString(), sel, TabStyle(sel)))
                { _designTab = tabs[i]; _designScroll = Vector2.zero; }
            }

            var gridRect = new Rect(px, py + 28f, pwid, pheight - 28f);
            var designs = JerseyDesigns.InTab(_designTab);

            // Grid metrics: "None" swatch first, then one per design.
            const float sw = 52f, sh = 66f, sgap = 8f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((gridRect.width - 16f) / (sw + sgap)));
            int items = designs.Count + 1;   // +1 for the "None" clear swatch
            int rows = Mathf.CeilToInt(items / (float)cols);
            var viewRect = new Rect(0f, 0f, cols * (sw + sgap), rows * (sh + sgap));

            _designScroll = GUI.BeginScrollView(gridRect, _designScroll, viewRect);
            var capSt = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = UITheme.Ink } };
            for (int i = 0; i < items; i++)
            {
                int cell = i;
                float cxp = (cell % cols) * (sw + sgap);
                float cyp = (cell / cols) * (sh + sgap);
                var cellRect = new Rect(cxp, cyp, sw, sw);

                if (i == 0)
                {
                    // "None": clears back to plain shirt (design = null).
                    bool selNone = _selectedDesign == null;
                    var pc = GUI.color; GUI.color = PlayerProfile.JerseyBase;
                    GUI.DrawTexture(cellRect, Texture2D.whiteTexture);
                    GUI.color = selNone ? UITheme.Gold : new Color(0f, 0f, 0f, 0.6f);
                    DrawRectOutline(cellRect, selNone ? 2f : 1f);
                    GUI.color = pc;
                    if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none)) ApplyDesign(null);
                    UITheme.Label(new Rect(cxp, cyp + sw + 1f, sw, 14f), "None", capSt);
                    continue;
                }

                var d = designs[i - 1];
                var thumb = JerseyDesigns.Thumb(d);
                if (thumb != null) GUI.DrawTexture(cellRect, thumb);
                bool sel = _selectedDesign == d;
                var pc2 = GUI.color;
                GUI.color = sel ? UITheme.Gold : new Color(0f, 0f, 0f, 0.6f);
                DrawRectOutline(cellRect, sel ? 2f : 1f);
                GUI.color = pc2;
                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none)) ApplyDesign(d);
                UITheme.Label(new Rect(cxp, cyp + sw + 1f, sw, 16f), d.Name, capSt);
            }
            GUI.EndScrollView();
        }

        // Reset the ACTIVE region's paint back to the base layer (keeps the baked name +
        // number and any predrawn design on that region; leaves the other region untouched).
        void ClearPaint()
        {
            _undoPixels = (Color32[])_pixels.Clone();   // so Undo restores the pre-clear strokes
            int y0 = CurRegionY0;
            int start = y0 * RegW, count = RegH * RegW;
            System.Array.Copy(_baseLayer, start, _pixels, start, count);
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
        }

        // Hold the middle (wheel) button and drag: left shrinks, right grows the brush.
        void HandleBrushResize(Rect canvasRect)
        {
            if (ModalUp) { _resizingBrush = false; return; }   // see ModalUp
            Event e = Event.current;
            _lastMouse = e.mousePosition;
            if (e.type == EventType.MouseDown && e.button == 2)
            {
                _resizingBrush = true;
                _resizeStartX = e.mousePosition.x;
                _resizeStartSize = _brushSize;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _resizingBrush)
            {
                float dx = e.mousePosition.x - _resizeStartX;
                _brushSize = Mathf.Clamp(_resizeStartSize + dx * SimConfig_BrushSizePerPixel, 2f, 40f);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 2)
            {
                _resizingBrush = false;
                e.Use();
            }
        }
        const float SimConfig_BrushSizePerPixel = 0.15f;   // brush px change per screen px dragged

        // A circular ring over the cursor showing the current brush footprint.
        Texture2D _ring;
        void EnsureRing()
        {
            if (_ring != null) return;
            const int n = 64;
            _ring = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                // Opaque only in a thin band near the edge -> a hollow ring.
                px[y * n + x] = (d > 0.82f && d <= 1f)
                    ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
            _ring.SetPixels32(px);
            _ring.Apply();
        }

        void DrawBrushCursor(Rect canvasRect, float canvasSize)
        {
            if (!canvasRect.Contains(_lastMouse)) return;
            EnsureRing();
            float pxPerTex = canvasSize / RegW;
            float rPx = _brushSize * pxPerTex;
            var prev = GUI.color;
            GUI.color = _resizingBrush ? Color.white
                      : new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.95f);
            GUI.DrawTexture(new Rect(_lastMouse.x - rPx, _lastMouse.y - rPx, rPx * 2f, rPx * 2f), _ring);
            GUI.color = prev;
        }

        // Procedural eyedropper icon (transparent PNG-style texture): a diagonal dropper with
        // a squeeze bulb at the top-right and the pointed tip at the lower-left. Built once.
        Texture2D _eyedropperIcon;
        void EnsureEyedropperIcon()
        {
            if (_eyedropperIcon != null) return;
            const int n = 32;
            var px = new Color32[n * n];
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 metal = new Color32(225, 228, 235, 255);   // barrel
            Color32 dark = new Color32(120, 128, 140, 255);    // outline/shadow
            Color32 bulb = new Color32(70, 130, 210, 255);     // squeeze bulb (blue)
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            // Barrel: a thick diagonal from lower-left tip (~4,4) to upper-right (~24,24).
            // Note: texture is y-up. We draw the dropper going up-right.
            for (int t = 0; t <= 26; t++)
            {
                float f = t / 26f;
                int cx = Mathf.RoundToInt(Mathf.Lerp(4f, 23f, f));
                int cy = Mathf.RoundToInt(Mathf.Lerp(4f, 23f, f));
                int rad = (t < 4) ? 1 : 2;   // taper to a point at the tip
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || x >= n || y < 0 || y >= n) continue;
                        px[y * n + x] = (Mathf.Abs(dx) == rad || Mathf.Abs(dy) == rad) ? dark : metal;
                    }
            }
            // Squeeze bulb: a filled disc at the top-right end.
            int bx = 25, by = 25, br = 5;
            for (int y = by - br; y <= by + br; y++)
                for (int x = bx - br; x <= bx + br; x++)
                {
                    if (x < 0 || x >= n || y < 0 || y >= n) continue;
                    int d2 = (x - bx) * (x - bx) + (y - by) * (y - by);
                    if (d2 <= br * br) px[y * n + x] = bulb;
                    else if (d2 <= (br + 1) * (br + 1)) px[y * n + x] = dark;
                }
            _eyedropperIcon = new Texture2D(n, n, TextureFormat.RGBA32, false);
            _eyedropperIcon.SetPixels32(px);
            _eyedropperIcon.Apply();
        }

        void HandlePaint(Rect canvasRect)
        {
            if (ModalUp) { _painting = false; return; }   // see ModalUp
            Event e = Event.current;
            bool inside = canvasRect.Contains(e.mousePosition);

            // LEFT button only paints; the middle button is reserved for brush resize
            // (HandleBrushResize runs after this, so we must not swallow button-2 here).
            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                _undoPixels = (Color32[])_pixels.Clone();   // snapshot for undo
                _painting = true;
                PaintAt(canvasRect, e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _painting)
            {
                if (inside) PaintAt(canvasRect, e.mousePosition);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _painting = false;
            }
        }

        // Atlas bottom row of the region currently being drawn (front or back).
        int CurRegionY0 => _drawSide == 1 ? BackY0 : FrontY0;

        void PaintAt(Rect canvasRect, Vector2 mouse)
        {
            // Map GUI point (y-down) to REGION-LOCAL pixel (y-up), then into the active region.
            float fx = (mouse.x - canvasRect.x) / canvasRect.width;
            float fy = 1f - (mouse.y - canvasRect.y) / canvasRect.height;
            int cx = Mathf.RoundToInt(fx * (RegW - 1));
            int cy = Mathf.RoundToInt(fy * (RegH - 1));   // local row within the region
            int rad = Mathf.RoundToInt(_brushSize);
            float a = _brushOpacity;
            Color32 bc = _brushColor;
            int y0 = CurRegionY0;

            int minX = Mathf.Max(0, cx - rad), maxX = Mathf.Min(RegW - 1, cx + rad);
            int minY = Mathf.Max(0, cy - rad), maxY = Mathf.Min(RegH - 1, cy + rad);   // clamp to region
            for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                float dx = px - cx, dy = py - cy;
                if (dx * dx + dy * dy > rad * rad) continue;
                int idx = (y0 + py) * RegW + px;   // shift local row into the atlas region
                Color32 dst = _pixels[idx];
                // Alpha blend the brush color over the existing pixel.
                _pixels[idx] = Color32.Lerp(dst, bc, a);
            }
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
        }

        void HandleWheel(Rect wheelRect)
        {
            if (ModalUp) return;   // see ModalUp
            Event e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && wheelRect.Contains(e.mousePosition))
            {
                float fx = (e.mousePosition.x - wheelRect.x) / wheelRect.width;
                float fy = 1f - (e.mousePosition.y - wheelRect.y) / wheelRect.height;
                int wx = Mathf.Clamp(Mathf.RoundToInt(fx * (_wheel.width - 1)), 0, _wheel.width - 1);
                int wy = Mathf.Clamp(Mathf.RoundToInt(fy * (_wheel.height - 1)), 0, _wheel.height - 1);
                Color c = _wheel.GetPixel(wx, wy);
                if (c.a > 0.5f) _brushColor = new Color(c.r, c.g, c.b, 1f);
                e.Use();
            }
        }

        void Undo()
        {
            if (_undoPixels == null) return;
            _pixels = (Color32[])_undoPixels.Clone();
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
        }

        // ------------------------------------------------------------- Name stage
        void NameStage(float x, float y, float pw, float ph)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = UITheme.Ink } };
            float lx = x + 30f, lw = pw - 60f, row = y + 76f;

            UITheme.Label(new Rect(lx, row, lw, 22f), "Name (shown on the back):", st); row += 26f;
            var tf = new GUIStyle(GUI.skin.textField) { fontSize = 18 };
            _name = GUI.TextField(new Rect(lx, row, lw, 32f), _name ?? "", 12, tf);
            row += 48f;

            UITheme.Label(new Rect(lx, row, lw, 22f), $"Number:  {_number}", st); row += 26f;
            float n = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _number, 1f, 99f);
            _number = Mathf.RoundToInt(n);
            row += 44f;

            // Preview of the back: base jersey color with the number + name, in the chosen
            // name/number colour.
            var preview = new Rect(lx, row, 200f, 240f);
            var prev = GUI.color; GUI.color = PlayerProfile.JerseyBase;
            GUI.DrawTexture(preview, Texture2D.whiteTexture);
            GUI.color = prev;
            UITheme.Frame(preview);   // border ONLY: a filled plate would cover the jersey swatch
            var numStyle = new GUIStyle(GUI.skin.label) { fontSize = 90, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = _identityColor } };
            UITheme.Label(new Rect(preview.x, preview.y + 40f, preview.width, 120f), _number.ToString(), numStyle);
            var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = _identityColor } };
            UITheme.Label(new Rect(preview.x, preview.y + 12f, preview.width, 30f), (_name ?? "").ToUpper(), nameStyle);

            // Name/number colour picker, to the RIGHT of the back preview.
            float cxp = preview.xMax + 24f, cyp = preview.y, cw = (x + pw - 30f) - cxp;
            if (cw > 90f)
            {
                UITheme.Label(new Rect(cxp, cyp, cw, 22f), "Name / number colour", st);
                var swatches = new (string n, Color c)[]
                {
                    ("White",  Color.white),
                    ("Black",  new Color(0.10f, 0.10f, 0.11f)),
                    ("Gold",   new Color(1f, 0.81f, 0.16f)),
                    ("Red",    new Color(0.82f, 0.12f, 0.16f)),
                    ("Royal",  new Color(0.11f, 0.29f, 0.78f)),
                    ("Green",  new Color(0.10f, 0.60f, 0.30f)),
                    ("Sky",    new Color(0.42f, 0.72f, 0.93f)),
                    ("Silver", new Color(0.75f, 0.76f, 0.80f)),
                };
                float sw = 34f, sgap = 8f, syp = cyp + 28f;
                int cols = Mathf.Max(1, Mathf.FloorToInt((cw + sgap) / (sw + sgap)));
                for (int i = 0; i < swatches.Length; i++)
                {
                    float bx = cxp + (i % cols) * (sw + sgap);
                    float by2 = syp + (i / cols) * (sw + sgap);
                    var sr = new Rect(bx, by2, sw, sw);
                    var pc = GUI.color; GUI.color = swatches[i].c;
                    GUI.DrawTexture(sr, Texture2D.whiteTexture);
                    bool sel = ApproxColor(_identityColor, swatches[i].c);
                    GUI.color = sel ? UITheme.Gold : new Color(0f, 0f, 0f, 0.6f);
                    DrawRectOutline(sr, sel ? 3f : 1f);
                    GUI.color = pc;
                    if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) SetIdentityColor(swatches[i].c);
                }
            }
        }

        static bool ApproxColor(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.04f && Mathf.Abs(a.g - b.g) < 0.04f && Mathf.Abs(a.b - b.b) < 0.04f;

        // -------------------------------------------------------------- Nav
        void NavButtons(float x, float y, float pw, float ph)
        {
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            // Anchor Back/Next to the far LEFT and RIGHT of the screen (not the panel), so
            // they clear the panel content and sit at the window edges.
            float by = MenuScale.Height - 72f;    // sit a little lower, closer to the screen bottom
            float bw = 150f, edge = 24f;

            if (UITheme.Button(new Rect(edge, by, bw, 44f), "Back", btn))
            {
                // Back off the first stage leaves the screen entirely, to the species picker, which
                // builds its OWN preview in this same frame. Hide ours first or both render the tail
                // of the frame. See PlayerPreview.Hide.
                if (_stage == Stage.Body) { enabled = false; if (_preview != null) _preview.Hide(); _onBack?.Invoke(); }
                else
                {
                    _stage -= 1;
                    if (SkipSkill && _stage == Stage.Skill) _stage -= 1;   // hop Skill (Name -> Body)
                }
            }

            // Flow is Body -> Skill -> Name -> Jersey; Jersey is last so it carries Confirm.
            string nextLabel = _stage == Stage.Jersey ? "Confirm" : "Next";
            var keepNav = GUI.backgroundColor; GUI.backgroundColor = UITheme.GoodTint;
            bool nextHit = UITheme.Button(new Rect(MenuScale.Width - edge - bw, by, bw, 44f), nextLabel, btn);
            GUI.backgroundColor = keepNav;
            if (nextHit)
            {
                // Same on Confirm: the preview camera sits at depth 5, above the match camera, so
                // leaving it live for the rest of the frame flashes a dark panel over the new match.
                if (_stage == Stage.Jersey) { Commit(); enabled = false; if (_preview != null) _preview.Hide(); _onDone?.Invoke(); }
                // Leaving Skill with adult mode on + points in Third Leg: gate on the funny
                // confirmation modal (it advances to Name on Continue) instead of advancing now.
                else if (_stage == Stage.Skill && _adultMode && SkillTree.ThirdLegSpent > 0)
                {
                    _thirdLegPrompt = true;
                }
                else
                {
                    Stage from = _stage;
                    _stage += 1;
                    if (SkipSkill && _stage == Stage.Skill) _stage += 1;   // hop Skill (Body -> Name)
                    // Entering the Jersey stage: bake the just-chosen name + number into
                    // the canvas base so the player paints around them, and point the 3D
                    // preview at the live canvas so strokes show on the model in real time.
                    if (from == Stage.Name && _stage == Stage.Jersey)
                    {
                        BuildCanvas();
                        _drawSide = 0;   // start on the front
                        if (_preview != null)
                        {
                            _preview.SetLiveJersey(_canvas);
                            _preview.FaceSide(false);   // show the chest to start
                        }
                    }
                }
            }
        }

        // Push the adult appendage size multipliers from the Third Leg skill nodes into the
        // appearance (1 = base with no nodes). Used live during the Skill stage for the preview and
        // again at Commit so the committed + networked appearance carries the final sizes.
        static void SyncAdultDims()
        {
            // A species with no adult mode holds the base dims regardless of the tree. Third Leg
            // nodes are not species-gated, so a node bought as a human stays owned after switching
            // to a horse; without this the horse would still commit and network non-base dims.
            // Species.ApplySelection zeroes them on the switch, this stops them being written back.
            if (!Species.Current.AllowsAdult)
            {
                PlayerProfile.Appearance.MemberLen   = 1f;
                PlayerProfile.Appearance.MemberGirth = 1f;
                PlayerProfile.Appearance.BallSize    = 1f;
                return;
            }
            PlayerProfile.Appearance.MemberLen   = SkillTree.Mul("length");
            PlayerProfile.Appearance.MemberGirth = SkillTree.Mul("girth");
            PlayerProfile.Appearance.BallSize    = SkillTree.Mul("ballsize");
        }

        void Commit()
        {
            SyncAdultDims();
            PlayerProfile.Height = _height;
            PlayerProfile.Weight = _weight;
            PlayerProfile.LeftFooted = _leftFooted;
            PlayerProfile.PlayerName = string.IsNullOrWhiteSpace(_name) ? "PLAYER" : _name.ToUpper();
            PlayerProfile.Number = _number;
            // Re-stamp the name + number ON TOP of the final paint so in game they always
            // show over any drawing (the base-layer copy is only a paint-around guide).
            BakeIdentity(_pixels);
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
            PlayerProfile.JerseyTex = _canvas;   // hand the painted texture to the profile
            PlayerProfile.JerseyPng = _canvas.EncodeToPNG();   // cache the PNG for network replication
        }
    }
}
