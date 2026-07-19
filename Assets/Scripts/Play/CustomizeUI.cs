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

        // Skill tree UI state.
        SkillTree.Category _skillCat = SkillTree.Category.Pace;

        // Working copies (committed to PlayerProfile on Done).
        float _height, _weight;
        bool _leftFooted;
        string _name;
        int _number;

        // ---- Jersey canvas ----
        const int TexSize = 256;         // jersey texture resolution
        Texture2D _canvas;               // the painted jersey (front + back share it)
        Color32[] _pixels;               // CPU buffer we paint into, then Apply
        Color32[] _baseLayer;            // jersey base + name + number, WITHOUT paint (redrawn under strokes)
        Color32[] _undoPixels;           // snapshot before the current stroke
        Texture2D _wheel;                // color-wheel picker texture
        Color _brushColor = new Color(0.9f, 0.1f, 0.1f);
        float _brushSize = 10f;          // radius in texture pixels
        float _brushOpacity = 1f;
        bool _painting;

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
                _canvas = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
                _canvas.wrapMode = TextureWrapMode.Clamp;
            }
            // Base layer = jersey colour + baked name + number (chosen in the prior stage).
            // The paint is applied ON TOP of this, so Clear returns to the base (keeping
            // the name/number) rather than a blank shirt.
            _baseLayer = new Color32[TexSize * TexSize];
            Color32 baseCol = PlayerProfile.JerseyBase;
            for (int i = 0; i < _baseLayer.Length; i++) _baseLayer[i] = baseCol;
            BakeIdentity(_baseLayer);

            _pixels = (Color32[])_baseLayer.Clone();
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
            _undoPixels = (Color32[])_pixels.Clone();
        }

        // Bake the number (large, centred) and name (small, above it) into the texture as
        // block glyphs, in white with a dark outline so they read on any jersey colour.
        void BakeIdentity(Color32[] buf)
        {
            Color32 ink = new Color32(255, 255, 255, 255);
            string num = Mathf.Clamp(_number, 1, 99).ToString();
            // Number: big glyphs centred in the lower-middle of the shirt.
            DrawText(buf, num, TexSize / 2, (int)(TexSize * 0.42f), 9, true);
            // Name: small glyphs across the upper-middle.
            string nm = string.IsNullOrWhiteSpace(_name) ? "" : _name.ToUpper();
            if (nm.Length > 0) DrawText(buf, nm, TexSize / 2, (int)(TexSize * 0.72f), 3, true);
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

        // Draw a centred string into buf at (cx, cy) with the given per-pixel block scale.
        // outline adds a 1-block dark border so text reads on any colour. cy is texture-space
        // (y up). Text is drawn so cy is roughly the glyph centre.
        void DrawText(Color32[] buf, string text, int cx, int cy, int scale, bool outline)
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
                DrawGlyph(buf, rows, px, gy, scale, outline);
                px += glyphW + space;
            }
        }

        void DrawGlyph(Color32[] buf, byte[] rows, int gx, int gy, int scale, bool outline)
        {
            Color32 ink = new Color32(255, 255, 255, 255);
            Color32 edge = new Color32(20, 20, 20, 255);
            for (int r = 0; r < 7; r++)
            {
                byte mask = rows[r];
                for (int c = 0; c < 5; c++)
                {
                    if ((mask & (1 << (4 - c))) == 0) continue;
                    // top row (r=0) is highest in the shirt -> larger texture y.
                    int bx = gx + c * scale;
                    int by = gy + (6 - r) * scale;
                    if (outline) FillBlock(buf, bx - 1, by - 1, scale + 2, scale + 2, edge);
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
                    FillBlock(buf, bx, by, scale, scale, ink);
                }
            }
        }

        static void FillBlock(Color32[] buf, int x0, int y0, int w, int h, Color32 col)
        {
            for (int y = y0; y < y0 + h; y++)
            {
                if (y < 0 || y >= TexSize) continue;
                for (int x = x0; x < x0 + w; x++)
                {
                    if (x < 0 || x >= TexSize) continue;
                    buf[y * TexSize + x] = col;
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

            // Control panel.
            float x = ox + previewW + gap;
            float panelW = contentW;
            GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
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

            // Live trait readout using the working values (commit first so the profile
            // computes off them).
            PlayerProfile.Height = _height;
            PlayerProfile.Weight = _weight;

            var hdr = new GUIStyle(st) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(lx, row, lw, 20f), "Resulting traits:", hdr); row += 26f;
            Trait(lx, ref row, lw, "Move speed",  PlayerProfile.MoveSpeedMul);
            Trait(lx, ref row, lw, "Sprint speed", PlayerProfile.SprintSpeedMul);
            Trait(lx, ref row, lw, "Jump height", PlayerProfile.JumpMul);
            Trait(lx, ref row, lw, "Shot power",  PlayerProfile.ShotPowerMul);
            Trait(lx, ref row, lw, "Push / strength", PlayerProfile.PushMul);
            Trait(lx, ref row, lw, "Reach",       PlayerProfile.ReachMul);
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

            var help = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.8f, 0.8f, 0.83f) } };
            GUI.Label(new Rect(lx, y + ph - 62f, lw, 20f), "Click an empty node to buy, an owned node to refund it (and anything built on top). Lines show prerequisites; the ringed node is a capstone perk.", help);
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            float lx = x + 28f, top = y + 60f;

            // Canvas on the left (square), tools on the right.
            float canvasSize = 300f;
            var canvasRect = new Rect(lx, top, canvasSize, canvasSize);
            GUI.DrawTexture(canvasRect, _canvas);
            GUI.Box(canvasRect, GUIContent.none);   // border

            HandlePaint(canvasRect);
            HandleBrushResize(canvasRect);
            DrawBrushCursor(canvasRect, canvasSize);

            // Tools column.
            float tx = lx + canvasSize + 24f, tw = pw - (canvasSize + 24f) - 56f, tr = top;
            GUI.Label(new Rect(tx, tr, tw, 18f), "Color", st); tr += 20f;
            float wheelSize = Mathf.Min(tw, 140f);
            var wheelRect = new Rect(tx, tr, wheelSize, wheelSize);
            GUI.DrawTexture(wheelRect, _wheel);
            HandleWheel(wheelRect);
            tr += wheelSize + 8f;

            // Current color swatch.
            var prev = GUI.color; GUI.color = _brushColor;
            GUI.DrawTexture(new Rect(tx, tr, 40f, 20f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(new Rect(tx, tr, 40f, 20f), GUIContent.none);
            tr += 30f;

            GUI.Label(new Rect(tx, tr, tw, 18f), $"Brush size: {_brushSize:0}", st); tr += 20f;
            _brushSize = GUI.HorizontalSlider(new Rect(tx, tr, tw, 18f), _brushSize, 2f, 40f); tr += 28f;
            GUI.Label(new Rect(tx, tr, tw, 18f), $"Opacity: {_brushOpacity:0.00}", st); tr += 20f;
            _brushOpacity = GUI.HorizontalSlider(new Rect(tx, tr, tw, 18f), _brushOpacity, 0.1f, 1f); tr += 30f;

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(tx, tr, tw * 0.48f, 26f), "Undo", btn)) Undo();
            if (GUI.Button(new Rect(tx + tw * 0.52f, tr, tw * 0.48f, 26f), "Clear", btn)) ClearPaint();

            GUI.Label(new Rect(lx, top + canvasSize + 6f, canvasSize, 34f),
                      "Left-click drag to paint. Hold the MOUSE-WHEEL button and drag\n"
                      + "left/right to shrink/grow the brush.", st);
        }

        // Reset the paint back to the base layer (keeps the baked name + number).
        void ClearPaint()
        {
            _pixels = (Color32[])_baseLayer.Clone();
            _canvas.SetPixels32(_pixels);
            _canvas.Apply();
            _undoPixels = (Color32[])_pixels.Clone();
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
            float pxPerTex = canvasSize / TexSize;
            float rPx = _brushSize * pxPerTex;
            var prev = GUI.color;
            GUI.color = _resizingBrush ? Color.white
                      : new Color(_brushColor.r, _brushColor.g, _brushColor.b, 0.95f);
            GUI.DrawTexture(new Rect(_lastMouse.x - rPx, _lastMouse.y - rPx, rPx * 2f, rPx * 2f), _ring);
            GUI.color = prev;
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

        void PaintAt(Rect canvasRect, Vector2 mouse)
        {
            // Map GUI point (y-down) to texture pixel (y-up).
            float fx = (mouse.x - canvasRect.x) / canvasRect.width;
            float fy = 1f - (mouse.y - canvasRect.y) / canvasRect.height;
            int cx = Mathf.RoundToInt(fx * (TexSize - 1));
            int cy = Mathf.RoundToInt(fy * (TexSize - 1));
            int rad = Mathf.RoundToInt(_brushSize);
            float a = _brushOpacity;
            Color32 bc = _brushColor;

            int minX = Mathf.Max(0, cx - rad), maxX = Mathf.Min(TexSize - 1, cx + rad);
            int minY = Mathf.Max(0, cy - rad), maxY = Mathf.Min(TexSize - 1, cy + rad);
            for (int py = minY; py <= maxY; py++)
            for (int px = minX; px <= maxX; px++)
            {
                float dx = px - cx, dy = py - cy;
                if (dx * dx + dy * dy > rad * rad) continue;
                int idx = py * TexSize + px;
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

            // Preview of the back: base jersey color with the number + name.
            var preview = new Rect(lx, row, 200f, 240f);
            var prev = GUI.color; GUI.color = PlayerProfile.JerseyBase;
            GUI.DrawTexture(preview, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Box(preview, GUIContent.none);
            var numStyle = new GUIStyle(GUI.skin.label) { fontSize = 90, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(preview.x, preview.y + 40f, preview.width, 120f), _number.ToString(), numStyle);
            var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(preview.x, preview.y + 12f, preview.width, 30f), (_name ?? "").ToUpper(), nameStyle);
        }

        // -------------------------------------------------------------- Nav
        void NavButtons(float x, float y, float pw, float ph)
        {
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
            float by = y + ph - 64f;

            if (GUI.Button(new Rect(x + 28f, by, 160f, 44f), "Back", btn))
            {
                if (_stage == Stage.Body) { enabled = false; _onBack?.Invoke(); }
                else _stage -= 1;
            }

            // Flow is Body -> Skill -> Name -> Jersey; Jersey is last so it carries Confirm.
            string nextLabel = _stage == Stage.Jersey ? "Confirm" : "Next";
            if (GUI.Button(new Rect(x + pw - 188f, by, 160f, 44f), nextLabel, btn))
            {
                if (_stage == Stage.Jersey) { Commit(); enabled = false; _onDone?.Invoke(); }
                else
                {
                    Stage from = _stage;
                    _stage += 1;
                    // Entering the Jersey stage: bake the just-chosen name + number into
                    // the canvas base so the player paints around them, and point the 3D
                    // preview at the live canvas so strokes show on the model in real time.
                    if (from == Stage.Name && _stage == Stage.Jersey)
                    {
                        BuildCanvas();
                        if (_preview != null) _preview.SetLiveJersey(_canvas);
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
        }
    }
}
