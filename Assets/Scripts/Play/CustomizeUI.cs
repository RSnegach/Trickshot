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

        // Body-stage appearance sub-menu (cycled by the arrows beside the BODY title).
        enum BodySub { Traits, Skin, Hair, Facial, Accessories }
        BodySub _bodySub = BodySub.Traits;
        Vector2 _apprScroll;                 // scroll for the option grids
        PlayerAppearance _lastPreviewAppr;   // detect appearance change to rebuild the preview
        bool _apprInit;

        // Working copies (committed to PlayerProfile on Done).
        float _height, _weight;
        bool _leftFooted;
        string _name;
        int _number;

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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _height = PlayerProfile.Height;
            _weight = PlayerProfile.Weight;
            _leftFooted = PlayerProfile.LeftFooted;
            _name = PlayerProfile.PlayerName;
            _number = PlayerProfile.Number;

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

        void OnGUI()
        {
            // Preview column on the left + a control panel on the right.
            const float previewW = 300f, gap = 16f;
            float contentW = 560f;
            float totalW = previewW + gap + contentW;
            float panelH = 600f;
            float ox = Screen.width * 0.5f - totalW * 0.5f;
            float y = Screen.height * 0.5f - panelH * 0.5f;

            // Live 3D preview viewport (the camera renders into this rect).
            var previewRect = new Rect(ox, y, previewW, panelH);
            GUI.Box(previewRect, GUIContent.none);
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
                _preview.ViewportPx = previewRect;
                _preview.AutoRotate = _stage != Stage.Jersey;   // jersey: drag to spin
                if (_stage == Stage.Jersey) HandleModelDrag(previewRect);
            }
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.LowerCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.7f) } };
            if (_stage == Stage.Jersey)
                GUI.Label(new Rect(previewRect.x, previewRect.yMax - 26f, previewW, 20f), "Drag the model to spin it", hint);

            // Skill stage: one-click build presets down the left column + a live attribute
            // radar over the lower preview, so the shape updates as nodes are bought.
            if (_stage == Stage.Skill)
            {
                SkillPresetButtons(previewRect);
                var radarRect = new Rect(previewRect.x + 20f, previewRect.yMax - 210f, previewW - 40f, 190f);
                var pc = GUI.color; GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.72f);
                GUI.DrawTexture(radarRect, Texture2D.whiteTexture); GUI.color = pc;
                StatRadar.Draw(radarRect);
            }

            // Control panel.
            float x = ox + previewW + gap;
            float panelW = contentW;
            GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            if (_stage == Stage.Body)
            {
                // "CUSTOMIZE -" prefix, then ‹ SUBMENU › arrows that cycle the appearance sub-menus.
                GUI.Label(new Rect(x + 28f, y + 14f, 220f, 36f), "CUSTOMIZE -", title);
                float axl = x + 210f;
                var arrow = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
                var subName = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
                int subCount = System.Enum.GetValues(typeof(BodySub)).Length;
                if (GUI.Button(new Rect(axl, y + 16f, 30f, 30f), "‹", arrow))
                    { _bodySub = (BodySub)(((int)_bodySub - 1 + subCount) % subCount); _apprScroll = Vector2.zero; }
                GUI.Label(new Rect(axl + 32f, y + 14f, 150f, 36f), SubName(_bodySub), subName);
                if (GUI.Button(new Rect(axl + 184f, y + 16f, 30f, 30f), "›", arrow))
                    { _bodySub = (BodySub)(((int)_bodySub + 1) % subCount); _apprScroll = Vector2.zero; }
            }
            else
                GUI.Label(new Rect(x + 28f, y + 14f, panelW - 56f, 36f), "CUSTOMIZE - " + _stage.ToString().ToUpper(), title);

            switch (_stage)
            {
                case Stage.Body:   BodyStage(x, y, panelW, panelH); break;
                case Stage.Skill:  SkillStage(x, y, panelW, panelH); break;
                case Stage.Jersey: JerseyStage(x, y, panelW, panelH); break;
                case Stage.Name:   NameStage(x, y, panelW, panelH); break;
            }

            NavButtons(x, y, panelW, panelH);
        }

        // Jersey stage: click-drag anywhere on the preview to spin the model.
        void HandleModelDrag(Rect previewRect)
        {
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            float lx = x + 30f, lw = pw - 60f, row = y + 66f;

            GUI.Label(new Rect(lx, row, lw, 20f), $"Height:  {_height:0.00} m", st); row += 24f;
            _height = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _height, PlayerProfile.MinHeight, PlayerProfile.MaxHeight); row += 40f;

            GUI.Label(new Rect(lx, row, lw, 20f), $"Weight:  {_weight:0} kg", st); row += 24f;
            _weight = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _weight, PlayerProfile.MinWeight, PlayerProfile.MaxWeight); row += 44f;

            // Strong foot: two toggle buttons. The selected one is tinted bright green with
            // a bold label + check; the other is dimmed so the choice is unmistakable.
            GUI.Label(new Rect(lx, row, lw, 20f), "Strong foot:", st); row += 24f;
            float bw = (lw - 10f) * 0.5f;
            if (FootButton(new Rect(lx, row, bw, 34f), "Left", _leftFooted))  _leftFooted = true;
            if (FootButton(new Rect(lx + bw + 10f, row, bw, 34f), "Right", !_leftFooted)) _leftFooted = false;
            row += 46f;

            // Commit body working values so traits compute off them.
            PlayerProfile.Height = _height;
            PlayerProfile.Weight = _weight;

            // Lower region: the trait readout (default) OR the selected appearance sub-menu,
            // switched by the ‹ › arrows beside the title.
            switch (_bodySub)
            {
                case BodySub.Traits:
                    var hdr = new GUIStyle(st) { fontStyle = FontStyle.Bold };
                    GUI.Label(new Rect(lx, row, lw, 20f), "Resulting traits:", hdr); row += 26f;
                    Trait(lx, ref row, lw, "Move speed",  PlayerProfile.MoveSpeedMul);
                    Trait(lx, ref row, lw, "Sprint speed", PlayerProfile.SprintSpeedMul);
                    Trait(lx, ref row, lw, "Jump height", PlayerProfile.JumpMul);
                    Trait(lx, ref row, lw, "Shot power",  PlayerProfile.ShotPowerMul);
                    Trait(lx, ref row, lw, "Push / strength", PlayerProfile.PushMul);
                    Trait(lx, ref row, lw, "Reach",       PlayerProfile.ReachMul);
                    break;
                case BodySub.Skin:        SkinSubMenu(lx, row, lw, y + ph - 60f); break;
                case BodySub.Hair:        HairSubMenu(lx, row, lw, y + ph - 60f); break;
                case BodySub.Facial:      FacialSubMenu(lx, row, lw, y + ph - 60f); break;
                case BodySub.Accessories: AccessorySubMenu(lx, row, lw, y + ph - 60f); break;
            }
        }

        static string SubName(BodySub s) => s switch
        {
            BodySub.Traits => "BODY",
            BodySub.Skin => "SKIN",
            BodySub.Hair => "HAIR",
            BodySub.Facial => "FACIAL",
            BodySub.Accessories => "EXTRAS",
            _ => "BODY",
        };

        static bool ApprEquals(PlayerAppearance a, PlayerAppearance b)
            => a.HairStyle == b.HairStyle && a.FacialStyle == b.FacialStyle && a.Accessory == b.Accessory
               && ApproxColor(a.Skin, b.Skin) && ApproxColor(a.HairColor, b.HairColor)
               && ApproxColor(a.FacialColor, b.FacialColor) && ApproxColor(a.AccessoryColor, b.AccessoryColor);

        // A reusable color-wheel picker: draws the wheel in `wheelRect`, and on click/drag inside
        // it returns the picked colour (else returns `current`). Consumes the event so it doesn't
        // fall through. (HandleWheel is hard-wired to _brushColor; this is the generic version.)
        Color WheelPick(Rect wheelRect, Color current)
        {
            GUI.DrawTexture(wheelRect, _wheel);
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
                var pc = GUI.color; GUI.color = cols[i];
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = ApproxColor(current, cols[i]) ? new Color(1f, 0.9f, 0.3f) : new Color(0f, 0f, 0f, 0.6f);
                DrawRectOutline(r, ApproxColor(current, cols[i]) ? 3f : 1f);
                GUI.color = pc;
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) result = cols[i];
            }
            return result;
        }

        // Dark neutrals the HSV wheel can't reach (its value is fixed at 1, so it tops out at
        // full-brightness hues and pure white; black and dark greys are unreachable). Offered as
        // swatches under the hair/facial/accessory wheels. Pure black is first.
        static readonly Color[] _darkSwatches =
        {
            new Color(0.02f, 0.02f, 0.02f), new Color(0.15f, 0.15f, 0.16f),
            new Color(0.32f, 0.32f, 0.34f), new Color(0.55f, 0.55f, 0.57f),
        };

        // Human-looking skin tones for the "Human" group.
        static readonly Color[] _humanSkins =
        {
            new Color(0.98f, 0.85f, 0.75f), new Color(0.94f, 0.78f, 0.66f), new Color(0.87f, 0.69f, 0.55f),
            new Color(0.80f, 0.61f, 0.46f), new Color(0.68f, 0.49f, 0.35f), new Color(0.55f, 0.38f, 0.26f),
            new Color(0.42f, 0.28f, 0.19f), new Color(0.30f, 0.20f, 0.14f),
        };

        void SkinSubMenu(float lx, float row, float lw, float bottom)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var grp = new GUIStyle(st) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.9f, 0.4f) } };
            GUI.Label(new Rect(lx, row, lw, 20f), "Human", grp); row += 24f;
            PlayerProfile.Appearance.Skin = SwatchRow(lx, row, lw, PlayerProfile.Appearance.Skin, _humanSkins, 34f, 8f);
            row += 2 * (34f + 8f) + 12f;   // two rows of swatches
            GUI.Label(new Rect(lx, row, lw, 20f), "Everyone Else", grp); row += 24f;
            float wsz = Mathf.Min(lw, bottom - row, 150f);
            PlayerProfile.Appearance.Skin = WheelPick(new Rect(lx, row, wsz, wsz), PlayerProfile.Appearance.Skin);
        }

        // Draw an option grid over Cosmetics entries (0..count-1), grouped by an optional label
        // function. Returns the newly-selected index (or `current`). Headgear cells can be
        // disabled. Used by Hair/Facial/Accessory sub-menus.
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
            for (int i = 0; i < count; i++)
            {
                float cx = (i % cols) * (cw + gap), cy = (i / cols) * (chh + gap);
                var r = new Rect(cx, cy, cw, chh);
                bool en = enabled == null || enabled(i);
                bool sel = i == current;
                var prevBg = GUI.backgroundColor; var prevEnabled = GUI.enabled;
                GUI.enabled = en;
                if (sel) GUI.backgroundColor = new Color(0.25f, 0.6f, 0.9f);
                if (GUI.Button(r, label(i), lbl) && en) result = i;
                GUI.backgroundColor = prevBg; GUI.enabled = prevEnabled;
                if (sel) { var pc = GUI.color; GUI.color = new Color(1f, 0.9f, 0.3f); DrawRectOutline(r, 2f); GUI.color = pc; }
            }
            GUI.EndScrollView();
            return result;
        }

        void HairSubMenu(float lx, float row, float lw, float bottom)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            float gridW = lw - 170f;
            float gridH = bottom - row - 4f;
            PlayerProfile.Appearance.HairStyle = OptionGrid(lx, row, gridW, gridH,
                Cosmetics.Hair.Count, PlayerProfile.Appearance.HairStyle,
                i => Cosmetics.Hair[i].Group.ToString()[0] + ": " + Cosmetics.Hair[i].Name, null);
            // If hair becomes non-bald while a headgear accessory is on, clear the accessory.
            if (!Cosmetics.IsBald(PlayerProfile.Appearance.HairStyle)
                && Cosmetics.AccessoryIsHeadgear(PlayerProfile.Appearance.Accessory))
                PlayerProfile.Appearance.Accessory = 0;
            // Hair colour wheel on the right.
            float wx = lx + gridW + 14f, wsz = Mathf.Min(150f, lw - gridW - 14f);
            GUI.Label(new Rect(wx, row, wsz, 18f), "Hair colour", st);
            PlayerProfile.Appearance.HairColor = WheelPick(new Rect(wx, row + 20f, wsz, wsz), PlayerProfile.Appearance.HairColor);
            // The HSV wheel is fixed at full value so it can't reach black; offer it as a swatch.
            PlayerProfile.Appearance.HairColor = SwatchRow(wx, row + 26f + wsz, wsz,
                PlayerProfile.Appearance.HairColor, _darkSwatches, 26f, 6f);
        }

        void FacialSubMenu(float lx, float row, float lw, float bottom)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            float gridW = lw - 170f;
            float gridH = bottom - row - 4f;
            PlayerProfile.Appearance.FacialStyle = OptionGrid(lx, row, gridW, gridH,
                Cosmetics.Facial.Count, PlayerProfile.Appearance.FacialStyle,
                i => Cosmetics.Facial[i].Name, null);
            float wx = lx + gridW + 14f, wsz = Mathf.Min(150f, lw - gridW - 14f);
            GUI.Label(new Rect(wx, row, wsz, 18f), "Facial colour", st);
            PlayerProfile.Appearance.FacialColor = WheelPick(new Rect(wx, row + 20f, wsz, wsz), PlayerProfile.Appearance.FacialColor);
            PlayerProfile.Appearance.FacialColor = SwatchRow(wx, row + 26f + wsz, wsz,
                PlayerProfile.Appearance.FacialColor, _darkSwatches, 26f, 6f);
        }

        void AccessorySubMenu(float lx, float row, float lw, float bottom)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            float gridW = lw - 170f;
            float gridH = bottom - row - 24f;
            bool bald = Cosmetics.IsBald(PlayerProfile.Appearance.HairStyle);
            PlayerProfile.Appearance.Accessory = OptionGrid(lx, row, gridW, gridH,
                Cosmetics.Accessories.Count, PlayerProfile.Appearance.Accessory,
                i => Cosmetics.Accessories[i].Name,
                i => !Cosmetics.Accessories[i].Headgear || bald);   // headgear needs bald hair
            if (!bald)
            {
                var hint = new GUIStyle(st) { fontSize = 11, normal = { textColor = new Color(0.85f, 0.8f, 0.5f) } };
                GUI.Label(new Rect(lx, row + gridH + 2f, gridW, 20f), "Headgear needs Bald hair.", hint);
            }
            float wx = lx + gridW + 14f, wsz = Mathf.Min(150f, lw - gridW - 14f);
            GUI.Label(new Rect(wx, row, wsz, 18f), "Accessory colour", st);
            PlayerProfile.Appearance.AccessoryColor = WheelPick(new Rect(wx, row + 20f, wsz, wsz), PlayerProfile.Appearance.AccessoryColor);
            PlayerProfile.Appearance.AccessoryColor = SwatchRow(wx, row + 26f + wsz, wsz,
                PlayerProfile.Appearance.AccessoryColor, _darkSwatches, 26f, 6f);
        }

        void Trait(float lx, ref float row, float lw, string label, float mul)
        {
            // Bar centred on 1.0x: green above, red below.
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            GUI.Label(new Rect(lx, row, 150f, 18f), label, st);
            float barX = lx + 160f, barW = lw - 200f, barH = 12f;
            GUI.Box(new Rect(barX, row + 2f, barW, barH), GUIContent.none);
            float t = Mathf.InverseLerp(0.6f, 1.5f, mul);
            var prev = GUI.color;
            GUI.color = mul >= 1f ? new Color(0.3f, 0.8f, 0.35f) : new Color(0.85f, 0.55f, 0.25f);
            GUI.Box(new Rect(barX, row + 2f, barW * Mathf.Clamp01(t), barH), GUIContent.none);
            GUI.color = prev;
            GUI.Label(new Rect(barX + barW + 6f, row, 44f, 18f), $"{mul:0.00}x", st);
            row += 20f;
        }

        // A foot-choice toggle. Selected = bright green fill, bold label + check, gold
        // outline; unselected = dim grey. Returns true if clicked this frame.
        bool FootButton(Rect r, string label, bool selected)
        {
            var prevBg = GUI.backgroundColor;
            var prevCol = GUI.color;

            // Filled background panel so the selected state is obvious regardless of skin.
            GUI.color = selected ? new Color(0.20f, 0.65f, 0.28f) : new Color(0.20f, 0.21f, 0.25f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            if (selected) { GUI.color = new Color(1f, 0.85f, 0.3f); DrawRectOutline(r, 2f); }
            GUI.color = prevCol;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = selected ? FontStyle.Bold : FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = selected ? Color.white : new Color(0.7f, 0.7f, 0.74f) }
            };
            GUI.Label(r, selected ? label + "  ✓" : label, style);

            // Invisible hit area over the whole panel.
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none);
            GUI.backgroundColor = prevBg;
            return clicked;
        }

        // ------------------------------------------------------------- Skill tree stage
        // Drawn as an ACTUAL node graph: nodes at their grid positions, connector lines to
        // prerequisites, clickable icon badges (left-click buys, right-click refunds), and
        // a detail strip for the selected node.
        string _selNode;   // currently selected node id (for the detail strip)

        void SkillStage(float x, float y, float pw, float ph)
        {
            float lx = x + 28f, lw = pw - 56f;

            var big = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
            GUI.Label(new Rect(lx, y + 52f, lw, 24f), $"Skill points: {SkillTree.Remaining} / {SkillTree.Budget}", big);

            // Category tabs.
            var cats = (SkillTree.Category[])System.Enum.GetValues(typeof(SkillTree.Category));
            float tw = (lw - (cats.Length - 1) * 4f) / cats.Length;
            for (int i = 0; i < cats.Length; i++)
            {
                bool sel = _skillCat == cats[i];
                var tb = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) tb.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (tw + 4f), y + 84f, tw, 26f), cats[i].ToString(), tb))
                    _skillCat = cats[i];
            }

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
            foreach (var n in SkillTree.InCategory(_skillCat))
            {
                if (string.IsNullOrEmpty(n.Requires)) continue;
                var req = SkillTree.ById(n.Requires);
                if (req == null) continue;
                bool lit = SkillTree.Owned.Contains(n.Id);
                DrawLine(Centre(req), Centre(n), lit ? new Color(0.4f, 0.85f, 0.5f) : new Color(0.4f, 0.4f, 0.45f), lit ? 3f : 2f);
            }

            // Pass 2: node badges.
            var costSt = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.9f, 0.4f) } };
            foreach (var n in SkillTree.InCategory(_skillCat))
            {
                Vector2 c = Centre(n);
                bool owned = SkillTree.Owned.Contains(n.Id);
                bool canBuy = SkillTree.CanBuy(n);
                bool capstone = n.Perk != null;
                var r = new Rect(c.x - nodeSz * 0.5f, c.y - nodeSz * 0.5f, nodeSz, nodeSz);

                var prev = GUI.color;
                GUI.color = owned ? new Color(0.25f, 0.6f, 0.32f)
                          : canBuy ? new Color(0.28f, 0.34f, 0.5f)
                          : new Color(0.18f, 0.18f, 0.22f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                // Capstone gets a gold ring.
                if (capstone) { GUI.color = new Color(1f, 0.85f, 0.3f); DrawRectOutline(r, 2f); }
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

                GUI.Label(new Rect(r.x, r.yMax - 14f, r.width, 12f), owned ? "✓" : n.Cost.ToString(), costSt);

                // Click: select, then act. Clicking an OWNED node refunds it (and cascades
                // to every node built on top of it); clicking a buyable node buys it.
                // Right-click also refunds, for muscle memory.
                Event e = Event.current;
                if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
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
            if (selNode != null && selNode.Cat == _skillCat)
            {
                float dy = y + ph - 124f;
                var box = new Rect(lx, dy, lw, 58f);
                var prev = GUI.color; GUI.color = new Color(0.12f, 0.13f, 0.16f); GUI.DrawTexture(box, Texture2D.whiteTexture); GUI.color = prev;
                var nameSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
                var descSt = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.85f,0.85f,0.88f) } };
                string tag = selNode.Perk != null ? "  [CAPSTONE PERK]" : "";
                GUI.Label(new Rect(box.x + 10f, box.y + 5f, lw - 130f, 18f), selNode.Name + tag, nameSt);
                GUI.Label(new Rect(box.x + 10f, box.y + 26f, lw - 130f, 26f), selNode.Desc, descSt);

                var actBtn = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
                var actRect = new Rect(box.xMax - 128f, box.y + 15f, 116f, 28f);
                if (SkillTree.Owned.Contains(selNode.Id))
                {
                    // Any owned node refunds; if dependents are built on it the refund
                    // cascades, so say so on the button.
                    bool cascades = SkillTree.HasOwnedDependents(selNode);
                    if (GUI.Button(actRect, cascades ? "Refund chain" : $"Refund {selNode.Cost}", actBtn))
                        SkillTree.Refund(selNode);
                }
                else
                {
                    bool canBuy = SkillTree.CanBuy(selNode);
                    GUI.enabled = canBuy;
                    bool needReq = !string.IsNullOrEmpty(selNode.Requires) && !SkillTree.Owned.Contains(selNode.Requires);
                    if (GUI.Button(actRect, needReq ? "Needs prereq" : $"Buy {selNode.Cost}", actBtn)) SkillTree.Buy(selNode);
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
            float contentH = 26f + presets.Length * (bh + bgap) + 34f;
            float colY = previewRect.y + Mathf.Max(0f, (previewRect.height - contentH) * 0.5f);

            // Backing panel.
            var prevC = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(new Rect(colX - pad, colY - pad, bw + pad * 2f, contentH + pad * 2f), Texture2D.whiteTexture);
            GUI.color = prevC;

            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
            GUI.Label(new Rect(colX, colY, bw, 20f), "QUICK BUILDS", hdr);

            float row = colY + 26f;
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                bool active = PresetMatches(p);
                var prev = GUI.color;
                GUI.color = active ? new Color(0.22f, 0.55f, 0.3f) : new Color(0.2f, 0.21f, 0.26f);
                var r = new Rect(colX, row, bw, bh);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                if (active) { GUI.color = new Color(1f, 0.85f, 0.3f); DrawRectOutline(r, 2f); }
                GUI.color = prev;

                var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(r, p.Name, lbl);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { SkillTree.ApplyPreset(p); _selNode = null; }
                row += bh + bgap;
            }

            var note = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.8f, 0.8f, 0.83f) } };
            GUI.Label(new Rect(colX, row + 2f, bw, 34f), "Presets replace your spend. Tweak nodes after.", note);
        }

        // A preset "matches" when the owned set is exactly its node list.
        static bool PresetMatches(SkillTree.Preset p)
        {
            if (SkillTree.Owned.Count != p.Ids.Length) return false;
            foreach (var id in p.Ids) if (!SkillTree.Owned.Contains(id)) return false;
            return true;
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
            GUIUtility.RotateAroundPivot(ang, a);
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = Color.white } };
            float lx = x + 28f, top = y + 58f;

            // Eyedropper: while armed, the FIRST left-click anywhere grabs the exact screen
            // pixel under the cursor. Handled before any other control so it wins the click.
            if (_eyedropper && !_picking)
            {
                Event ee = Event.current;
                if (ee.type == EventType.MouseDown && ee.button == 0)
                {
                    StartCoroutine(PickScreenPixel(ee.mousePosition));
                    ee.Use();
                }
                // Crosshair-ish cursor hint follows the mouse.
                var hintR = new Rect(Event.current.mousePosition.x + 12f, Event.current.mousePosition.y + 12f, 120f, 18f);
                var hs = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(1f, 0.9f, 0.3f) } };
                GUI.Label(hintR, "pick a colour", hs);
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
            GUI.Box(canvasRect, GUIContent.none);   // border

            HandlePaint(canvasRect);
            HandleBrushResize(canvasRect);
            DrawBrushCursor(canvasRect, canvasSize);

            // Undo / Clear overlaid at the TOP-RIGHT corner of the canvas.
            var miniBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
            float ubw = 56f, ubh = 22f, ugap = 4f;
            var clearR = new Rect(canvasRect.xMax - ubw - 4f, canvasRect.y + 4f, ubw, ubh);
            var undoR = new Rect(clearR.x - ubw - ugap, canvasRect.y + 4f, ubw, ubh);
            if (GUI.Button(undoR, "Undo", miniBtn)) Undo();
            if (GUI.Button(clearR, "Clear", miniBtn)) ClearPaint();

            // --- Tools column (right of the canvas) ---
            float tx = lx + canvasSize + 16f, tw = (x + pw - 28f) - tx, tr = top;
            GUI.Label(new Rect(tx, tr, tw, 18f), "Color", st); tr += 20f;
            float wheelSize = Mathf.Min(tw, 130f);
            var wheelRect = new Rect(tx, tr, wheelSize, wheelSize);
            GUI.DrawTexture(wheelRect, _wheel);
            HandleWheel(wheelRect);
            tr += wheelSize + 8f;

            // Current color swatch + eyedropper icon button beside it.
            var prev = GUI.color; GUI.color = _brushColor;
            GUI.DrawTexture(new Rect(tx, tr, 40f, 20f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(new Rect(tx, tr, 40f, 20f), GUIContent.none);
            EnsureEyedropperIcon();
            var edRect = new Rect(tx + 48f, tr - 4f, 28f, 28f);   // square button sized to the icon
            if (GUI.Button(edRect, GUIContent.none)) _eyedropper = !_eyedropper;
            // Highlight ring when armed.
            if (_eyedropper)
            {
                var hc = GUI.color; GUI.color = new Color(1f, 0.9f, 0.3f);
                DrawRectOutline(edRect, 2f); GUI.color = hc;
            }
            // Draw the icon inset within the button.
            GUI.DrawTexture(new Rect(edRect.x + 4f, edRect.y + 4f, 20f, 20f), _eyedropperIcon);
            tr += 30f;

            GUI.Label(new Rect(tx, tr, tw, 18f), $"Brush size: {_brushSize:0}", st); tr += 20f;
            _brushSize = GUI.HorizontalSlider(new Rect(tx, tr, tw, 18f), _brushSize, 2f, 40f); tr += 26f;
            GUI.Label(new Rect(tx, tr, tw, 18f), $"Opacity: {_brushOpacity:0.00}", st); tr += 20f;
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
            int sx = Mathf.Clamp(Mathf.RoundToInt(guiPos.x), 0, Screen.width - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(Screen.height - 1 - guiPos.y), 0, Screen.height - 1); // GUI y-down -> screen y-up
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

        // A FRONT/BACK tab button. Selected = bright fill + gold outline.
        bool SideTab(Rect r, string label, bool selected)
        {
            var prevCol = GUI.color;
            GUI.color = selected ? new Color(0.20f, 0.55f, 0.75f) : new Color(0.20f, 0.21f, 0.25f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            if (selected) { GUI.color = new Color(1f, 0.85f, 0.3f); DrawRectOutline(r, 2f); }
            GUI.color = prevCol;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = selected ? Color.white : new Color(0.7f, 0.7f, 0.74f) }
            };
            GUI.Label(r, label, style);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
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
                var tb = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) tb.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(px + i * (tw + 4f), py, tw, 24f), tabs[i].ToString(), tb))
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
            var capSt = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = Color.white } };
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
                    GUI.color = selNone ? new Color(1f, 0.85f, 0.3f) : new Color(0f, 0f, 0f, 0.6f);
                    DrawRectOutline(cellRect, selNone ? 2f : 1f);
                    GUI.color = pc;
                    if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none)) ApplyDesign(null);
                    GUI.Label(new Rect(cxp, cyp + sw + 1f, sw, 14f), "None", capSt);
                    continue;
                }

                var d = designs[i - 1];
                var thumb = JerseyDesigns.Thumb(d);
                if (thumb != null) GUI.DrawTexture(cellRect, thumb);
                bool sel = _selectedDesign == d;
                var pc2 = GUI.color;
                GUI.color = sel ? new Color(1f, 0.85f, 0.3f) : new Color(0f, 0f, 0f, 0.6f);
                DrawRectOutline(cellRect, sel ? 2f : 1f);
                GUI.color = pc2;
                if (GUI.Button(cellRect, GUIContent.none, GUIStyle.none)) ApplyDesign(d);
                GUI.Label(new Rect(cxp, cyp + sw + 1f, sw, 16f), d.Name, capSt);
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            float lx = x + 30f, lw = pw - 60f, row = y + 76f;

            GUI.Label(new Rect(lx, row, lw, 22f), "Name (shown on the back):", st); row += 26f;
            var tf = new GUIStyle(GUI.skin.textField) { fontSize = 18 };
            _name = GUI.TextField(new Rect(lx, row, lw, 32f), _name ?? "", 12, tf);
            row += 48f;

            GUI.Label(new Rect(lx, row, lw, 22f), $"Number:  {_number}", st); row += 26f;
            float n = GUI.HorizontalSlider(new Rect(lx, row, lw, 20f), _number, 1f, 99f);
            _number = Mathf.RoundToInt(n);
            row += 44f;

            // Preview of the back: base jersey color with the number + name, in the chosen
            // name/number colour.
            var preview = new Rect(lx, row, 200f, 240f);
            var prev = GUI.color; GUI.color = PlayerProfile.JerseyBase;
            GUI.DrawTexture(preview, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(preview, GUIContent.none);
            var numStyle = new GUIStyle(GUI.skin.label) { fontSize = 90, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = _identityColor } };
            GUI.Label(new Rect(preview.x, preview.y + 40f, preview.width, 120f), _number.ToString(), numStyle);
            var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = _identityColor } };
            GUI.Label(new Rect(preview.x, preview.y + 12f, preview.width, 30f), (_name ?? "").ToUpper(), nameStyle);

            // Name/number colour picker, to the RIGHT of the back preview.
            float cxp = preview.xMax + 24f, cyp = preview.y, cw = (x + pw - 30f) - cxp;
            if (cw > 90f)
            {
                GUI.Label(new Rect(cxp, cyp, cw, 22f), "Name / number colour", st);
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
                    GUI.color = sel ? new Color(1f, 0.9f, 0.3f) : new Color(0f, 0f, 0f, 0.6f);
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
            float by = Screen.height - 100f;   // fixed 100px from the screen bottom, clear of panel content
            float bw = 150f, edge = 24f;

            if (GUI.Button(new Rect(edge, by, bw, 44f), "Back", btn))
            {
                if (_stage == Stage.Body) { enabled = false; _onBack?.Invoke(); }
                else
                {
                    _stage -= 1;
                    if (SkipSkill && _stage == Stage.Skill) _stage -= 1;   // hop Skill (Name -> Body)
                }
            }

            // Flow is Body -> Skill -> Name -> Jersey; Jersey is last so it carries Confirm.
            string nextLabel = _stage == Stage.Jersey ? "Confirm" : "Next";
            if (GUI.Button(new Rect(Screen.width - edge - bw, by, bw, 44f), nextLabel, btn))
            {
                if (_stage == Stage.Jersey) { Commit(); enabled = false; _onDone?.Invoke(); }
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

        void Commit()
        {
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
