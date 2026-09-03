using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Pre-match settings screen shown after the stadium is picked. Each slider carries
    /// its own sane min/max/default so no setting can break play (e.g. ball velocity
    /// cannot hit 0, which used to fire the ball straight up). Ability/difficulty are the
    /// only 0-based sliders, because 0 there is meaningful (a passive keeper / easy shot).
    ///
    /// The panel height is sized to the number of rows the current mode shows, so nothing
    /// crowds together or overflows.
    /// </summary>
    public class PrematchUI : MonoBehaviour
    {
        GameMode _mode;
        System.Action<GameMode> _onStart;
        System.Action _onBack;

        // Base values the 1.00 multipliers map to. Sourced from SimConfig rather than retyped: these
        // are the same numbers SimConfig.ApplyMatchStatics resets to, and two hand-copied lists of
        // one set of defaults is how the goal-size leak survived as long as it did.
        const float BaseGoalWidth = SimConfig.GoalWidthBase, BaseGoalHeight = SimConfig.GoalHeightBase;
        // (The cross cadence base moved with its slider - see CrossMap.BaseServeInterval.)
        const float BaseStrikerSpeed = SimConfig.StrikerMoveSpeedBase;
        const float BaseKeeperSpeed = SimConfig.KeeperStrafeSpeedBase;
        static readonly float BaseKeeperJump = SimConfig.KeeperJumpVelBase;

        // ---- Multiplier sliders (value is a multiplier; ranges chosen so every point
        //      on the slider is playable). Defaults are all 1.0x except abilities. ----
        // STATIC so the last-dialed setup survives leaving and reopening this screen
        // (e.g. pausing to Match Setup mid-match). Resets to defaults only on a new
        // session or the Reset All button.
        static float _goalWidth = 1f, _goalHeight = 1f, _ballSpeed = 1f;
        readonly GoalEditor _goalEditor = new GoalEditor();   // Striker: the goal picture (edits _goalWidth/_goalHeight)
        static float _keeperAbility = 0.5f, _strikerSpeed = 1f;   // striker
        static float _shotDifficulty = 0.5f, _keeperSpeed = 1f, _keeperJump = 1f;      // keeper
        static float _keeperServeInterval = SimConfig.KeeperServeIntervalBase;         // keeper: seconds between shots
        // Accuracy practice: target speed + size, both 0-100 over the challenge's own range.
        static float _accPracticeSpeed = SimConfig.AccuracyPracticeSpeed01 * 100f;
        static float _accPracticeSize  = SimConfig.AccuracyPracticeSize01 * 100f;

        // ---- Challenge-mode raw settings ----
        // (Accuracy has none: its difficulty is the round number - see SimConfig.AccuracyTier.)
        static bool  _penaltyMode = false;
        static float _freeKickDistance = 20f;
        static float _wallCount = 4f;
        static float _wallDistance = 9.15f;
        static float _wallOffset = 0f;

        // Free-kick PLACEMENT: picked on the same map + RANDOM SPOTS control the multiplayer host
        // uses (SetPieceMap.DrawSetupPanel), which replaces the old distance/wall-offset sliders.
        static bool    _fkInit;
        static Vector3 _fkBall, _fkWall;
        static int     _fkEdit;      // 0 = ball, 1 = wall
        static bool    _fkRandom;    // fresh legal spot every attempt

        // Match
        static int _scrimPerSide = 3;                                  // incl. keeper: 2/3/5 (see ScrimSizes)
        // POSITION, not role. Shirt 0 is the keeper, so "GK" is one button on the position picker
        // rather than a separate two-button role row that could disagree with it. Both are kept
        // because they answer different questions: _scrimShirt is what the sim needs (identity), and
        // _scrimPos is what the PLAYER picked, which is what survives a team-size change - pick CB at
        // 11-a-side, drop to 5, and you are still CB (shirt 1) instead of whoever wears 3 now.
        static int _scrimShirt = 2;
        static SimConfig.ScrimPos _scrimPos = SimConfig.ScrimPos.ST;
        static SimConfig.AiDifficulty _scrimAi = SimConfig.AiDifficulty.Normal;
        static float _scrimMatchMin = 3f;                              // match length, minutes

        // ---- Layout ----
        const float PanelW = 480f;
        const float RowH = 52f;      // vertical space per slider row (label + track + gap)
        const float HeadH = 78f;     // title area
        const float FootH = 84f;     // Back/Start buttons area

        public void Init(GameMode mode, System.Action<GameMode> onStart, System.Action onBack)
        {
            _mode = mode;
            _onStart = onStart;
            _onBack = onBack;
            GameInput.CaptureCursor(false);
        }

        // How many slider/toggle rows this mode shows, so the panel is sized to fit.
        int RowCount()
        {
            // Match: team size + position + difficulty + match length picker rows, no goal/ball
            // SLIDERS (its goal is the GoalEditor picture, sized separately in DrawSetup exactly as
            // Striker's is). The position grid wraps at PosPerRow shirts and every extra button row
            // costs exactly one RowH, so this stays an exact count rather than an estimate.
            if (_mode == GameMode.Match)
                return 3 + GridRows(Mathf.Max(1, _scrimPerSide), PosPerRow);

            // Striker has no slider rows at all: its goal size + keeper are the goal picture
            // (GoalEditor, sized separately in DrawSetup), its shot speed and cross interval live on
            // the in-match cross map, and its striker speed is fixed. Every other mode: goal width +
            // height + shot speed.
            if (_mode == GameMode.Striker) return 0;
            // Accuracy: the goal picture carries the goal size and the keeper in BOTH modes (sized
            // separately in DrawSetup, like Striker's). PRACTICE adds the two target sliders under
            // it; the CHALLENGE adds nothing - its target and its keeper's strength are the round
            // ladder's, and the only choice it offers is whether there is a keeper at all.
            if (_mode == GameMode.Accuracy) return SimConfig.AccuracyPractice ? 2 : 0;
            int n = 3;
            if (_mode == GameMode.Goalkeeper) n += 4;   // difficulty + speed + jump + shot interval
            else
            {
                n += 1; // striker speed
                n += 1; // keeper ability (every mode in this branch can carry an AI keeper)
                // Free kick: penalty toggle (+ wall players; spot + wall are placed on the map panel)
                if (_mode == GameMode.FreeKick)  n += _penaltyMode ? 1 : 2;
            }
            return n;
        }

        // Scale the whole setup screen up on big displays (see MenuScale): the fixed sizes below
        // are unchanged, they just cover more of the screen and stop the header being cramped.
        // Wrapped so the early returns inside DrawSetup() can't leak the scaled GUI matrix.
        void OnGUI()
        {
            MenuScale.Begin();
            DrawSetup();
            MenuScale.End();
        }

        void DrawSetup()
        {
            // Striker's panel is taller than its (zero) slider rows say: it holds the goal picture.
            // Striker's and Match's panels are taller than their slider-row count says: both hold
            // the goal picture.
            bool goalPic = _mode == GameMode.Striker || _mode == GameMode.Match
                        || _mode == GameMode.Accuracy;   // practice: the same goal + keeper control
            // FootH reserves a band for Back/Start - but DrawNav pins those to the SCREEN bottom,
            // not to the panel, so that band is empty plate. Accuracy is where it reads worst (the
            // challenge screen ends right after the keeper row and the goal picture is the last
            // thing in it), so it closes the gap; the other modes keep the height they were laid
            // out against.
            float foot = _mode == GameMode.Accuracy ? 18f : FootH;
            float panelH = HeadH + RowCount() * RowH + foot + (goalPic ? GoalEditor.ContentH + 8f : 0f);
            float x = MenuScale.Width * 0.5f - PanelW * 0.5f;
            float y = MenuScale.Height * 0.5f - panelH * 0.5f;
            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.40f, PanelW + 520f);
            UITheme.Panel(new Rect(x, y, PanelW, panelH), UITheme.Blue);

            // Attribute card to the LEFT of the settings panel (custom-player modes only;
            // keeper mode uses a fixed keeper, so no card).
            if (_mode != GameMode.Goalkeeper) DrawStatCard(x - 300f, y);

            // Title on ONE line: the old rect (PanelW - 200) was too narrow for the longer mode
            // names ("SETPIECES - SETUP" wrapped and clipped), so give it the full width left of
            // the Reset All button and stop it wrapping.
            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink }, wordWrap = false, clipping = TextClipping.Overflow };
            UITheme.Shadowed(new Rect(x + 30f, y + 14f, PanelW - 160f, 44f),
                             (_mode == GameMode.Accuracy
                                  ? (SimConfig.AccuracyPractice ? "ACCURACY - PRACTICE" : "ACCURACY - CHALLENGE")
                                  : _mode.ToString().ToUpper() + " - SETUP"),
                             title, UITheme.Ink, 0.75f, 2f);
            // Short gold stub under the wordmark, then a hairline closing the header band.
            UITheme.Fill(new Rect(x + 30f, y + 52f, 54f, 2.5f), UITheme.Gold);
            UITheme.Divider(x + 30f, y + HeadH - 12f, PanelW - 60f);

            // Reset All restores the sliders this screen owns. The accuracy CHALLENGE has no
            // sliders - its one control is the keeper Yes/No, which Reset All does not touch - so
            // the button would do nothing visible there.
            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            bool showReset = !(_mode == GameMode.Accuracy && !SimConfig.AccuracyPractice);
            if (showReset && UITheme.Button(new Rect(x + PanelW - 130f, y + 20f, 110f, 30f), "Reset All", smallBtn))
                ResetAll();

            float row = y + HeadH;
            float lx = x + 30f, lw = PanelW - 60f;

            // Match: the pickers, then the goal picture (the same GoalEditor control Striker and
            // the multiplayer host both use), then Back/Start. No ball sliders.
            if (_mode == GameMode.Match)
            {
                MatchPickers(lx, ref row, lw);
                // Goal size only: the keeper level shown under the picture is Match's own AI
                // difficulty ladder, already picked above in MatchPickers, so this passes a throwaway
                // and ignores what comes back rather than offering a second control that would
                // silently disagree with the first.
                float mgw = BaseGoalWidth * _goalWidth, mgh = BaseGoalHeight * _goalHeight;
                int mlvl = SimConfig.NearestAiLevel(SimConfig.AiLevelAbility[Mathf.Clamp((int)_scrimAi, 0, SimConfig.AiLevelAbility.Length - 1)]);
                _goalEditor.Draw(new Rect(lx, row + 4f, lw, GoalEditor.ContentH), ref mgw, ref mgh, ref mlvl, framed: false);
                _goalWidth = mgw / BaseGoalWidth; _goalHeight = mgh / BaseGoalHeight;
                row += GoalEditor.ContentH + 8f;
                DrawNav(x, y, panelH);
                return;
            }

            // ACCURACY owns its whole screen, in the layout every single-player setup screen
            // follows: the goal picture, the keeper row under it, then whatever the mode adds.
            //   PRACTICE  - draggable goal, the five-step keeper ladder, the two target sliders,
            //               and the placement map on the right.
            //   CHALLENGE - the SAME picture but LOCKED at regulation (a scored run is played on
            //               one goal, so a score means the same thing every time) and a keeper
            //               Yes/No, since its strength is the round ladder's to set. Nothing else:
            //               no target sliders, no map - those are what make it a challenge.
            if (_mode == GameMode.Accuracy)
            {
                bool practice = SimConfig.AccuracyPractice;

                // Open on whatever was last chosen, so the keeper row agrees with the flag; from
                // here the picture's row is the authority and writes back in Apply().
                if (SimConfig.AccuracyNoKeeper && _keeperAbility > 0.001f) _keeperAbility = 0f;

                float agw = BaseGoalWidth * _goalWidth, agh = BaseGoalHeight * _goalHeight;
                int alvl = SimConfig.NearestAiLevel(_keeperAbility);
                _goalEditor.Draw(new Rect(lx, row + 4f, lw, GoalEditor.ContentH), ref agw, ref agh, ref alvl,
                                 framed: false, locked: !practice,
                                 keeperRow: practice ? GoalEditor.KeeperRow.Ladder
                                                     : GoalEditor.KeeperRow.YesNo);
                // Store the size back only when it was EDITABLE. A locked picture hands back the
                // regulation goal whatever it was given, and _goalWidth/_goalHeight are static and
                // shared with practice - so writing it here would silently reset a goal the player
                // had sized in practice just by looking at the challenge screen. The challenge is
                // played on regulation regardless (GameBootstrap forces it before Arena.Build).
                if (practice) { _goalWidth = agw / BaseGoalWidth; _goalHeight = agh / BaseGoalHeight; }
                _keeperAbility = SimConfig.AiLevelAbility[alvl];
                row += GoalEditor.ContentH + 8f;

                if (practice)
                {
                    // 0-100 across the CHALLENGE's own range, so practice can never ask for a target
                    // the challenge would never show: speed 0 parks it and 100 is round-91+ pace;
                    // size 0 is the challenge's smallest target and 100 its largest.
                    _accPracticeSpeed = RawSlider(lx, ref row, lw, "Target speed", _accPracticeSpeed, 0f, 100f, "0", "");
                    _accPracticeSize  = RawSlider(lx, ref row, lw, "Target size",  _accPracticeSize,  0f, 100f, "0", "");
                }

                // RETURN, like the Match branch above: falling through to the shared chains below
                // would add a Striker speed slider and a SECOND keeper picker (disagreeing with the
                // goal picture's own), and put more rows in the panel than RowCount() sizes it for.
                DrawPlacementPanel(x, y);
                DrawNav(x, y, panelH);
                return;
            }
            else if (_mode == GameMode.Striker)
            {
                // The goal as a picture you drag to size, with the keeper's difficulty under it -
                // the identical control the multiplayer host gets (GoalEditor), embedded here. It
                // edits the same width/height multipliers the sliders did (same 0.6x-1.5x range) and
                // the same keeper ability, so Reset All and the apply block below are unchanged.
                // Shot speed + cross interval live on the in-match cross map; striker speed is fixed.
                float gw = BaseGoalWidth * _goalWidth, gh = BaseGoalHeight * _goalHeight;
                int lvl = SimConfig.NearestAiLevel(_keeperAbility);
                _goalEditor.Draw(new Rect(lx, row + 4f, lw, GoalEditor.ContentH), ref gw, ref gh, ref lvl, framed: false);
                _goalWidth = gw / BaseGoalWidth; _goalHeight = gh / BaseGoalHeight;
                _keeperAbility = SimConfig.AiLevelAbility[lvl];
                row += GoalEditor.ContentH + 8f;
            }
            else
            {
                // Multiplier sliders with per-slider ranges. Goal size + shot speed for every mode
                // but Striker (whose shot speed and cross interval are on the in-match cross map).
                _goalWidth  = Slider(lx, ref row, lw, "Goal width",   _goalWidth,  0.6f, 1.5f, 1f);
                _goalHeight = Slider(lx, ref row, lw, "Goal height",  _goalHeight, 0.6f, 1.5f, 1f);
                _ballSpeed  = Slider(lx, ref row, lw, "Shot speed",   _ballSpeed,  0.5f, 2f,   1f);
            }

            if (_mode == GameMode.Striker) { }   // its only controls are the goal picture above
            else if (_mode == GameMode.Goalkeeper)
            {
                _shotDifficulty = Slider(lx, ref row, lw, "Shot difficulty", _shotDifficulty, 0f,   1f, 0.5f);
                _keeperSpeed    = Slider(lx, ref row, lw, "Keeper speed",    _keeperSpeed,    0.5f, 1.8f, 1f);
                _keeperJump     = Slider(lx, ref row, lw, "Keeper jump height", _keeperJump,  0.6f, 1.6f, 1f);
                // How often the server fires. The drill is a continuous loop, so this is the whole
                // pace of the mode: at the low end shots arrive before you are back on your line.
                _keeperServeInterval = RawSlider(lx, ref row, lw, "Shot interval", _keeperServeInterval,
                                                 1f, 8f, "0.0", "s");
            }
            else
            {
                _strikerSpeed = Slider(lx, ref row, lw, "Striker speed", _strikerSpeed, 0.5f, 1.8f, 1f);
                // Free kicks / set pieces only - accuracy returns above, and picks its keeper on the
                // goal picture instead. None = no keeper is built at all.
                _keeperAbility = KeeperPicker(lx, ref row, lw, "Keeper", _keeperAbility);

                if (_mode == GameMode.FreeKick)
                {
                    // The spot and the wall are PLACED on the map panel (right of this one) instead of
                    // being dialed in with distance/offset sliders. Only the wall size is a number.
                    _penaltyMode = Toggle(lx, ref row, lw, "Penalty mode (spot, no wall)", _penaltyMode);
                    if (!_penaltyMode)
                        _wallCount = RawSlider(lx, ref row, lw, "Wall players", _wallCount, 0f, 6f, "0", "");
                }
            }

            DrawPlacementPanel(x, y);
            DrawNav(x, y, panelH);
        }

        // The placement panel the multiplayer host uses, drawn to the RIGHT of the settings panel
        // (the stat card owns the left). A penalty is a fixed spot with no wall, so there is nothing
        // to place for one; accuracy practice places a ball but has no wall at all.
        void DrawPlacementPanel(float x, float y)
        {
            if (!PlacesSetPiece()) return;
            if (!_fkInit) { SetPieceMap.DefaultPlacement(out _fkBall, out _fkWall); _fkInit = true; }
            SetPieceMap.DrawSetupPanel(x + PanelW + 16f, y, 300f, 300f,
                                       ref _fkBall, ref _fkWall, ref _fkEdit, ref _fkRandom,
                                       "Random spot each attempt.",
                                       showWall: _mode != GameMode.Accuracy);
        }

        // Player attribute card: radar chart + numeric stat list, from the current build.
        void DrawStatCard(float x, float y)
        {
            float w = 280f, h = 430f;
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Blue);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Gold } };
            UITheme.Shadowed(new Rect(x + 14f, y + 8f, w - 28f, 22f),
                             (PlayerProfile.PlayerName ?? "PLAYER").ToUpper(), title, UITheme.Gold, 0.7f, 1.5f);

            StatRadar.Draw(new Rect(x + 10f, y + 30f, w - 20f, 200f));
            StatRadar.DrawList(x + 24f, y + 240f, w - 48f);
        }

        // Back/Start anchored to the far left/right screen edges.
        void DrawNav(float x, float y, float panelH)
        {
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            float by = MenuScale.Height - 72f;    // sit a little lower, closer to the screen bottom
            float bw = 170f, edge = 24f;
            if (UITheme.Button(new Rect(edge, by, bw, 48f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }

            // Start is the primary action, so it carries a standing green tint (tints MULTIPLY
            // the plate, which is why GoodTint's components exceed 1).
            var keep = GUI.backgroundColor;
            GUI.backgroundColor = UITheme.GoodTint;
            bool start = UITheme.Button(new Rect(MenuScale.Width - edge - bw, by, bw, 48f), "Start", btn);
            GUI.backgroundColor = keep;
            if (start) { Apply(); enabled = false; _onStart?.Invoke(_mode); }
        }

        // Match pickers: team size (per side) and the human's role.
        // Match pickers: team size, the POSITION the human plays, AI difficulty, match length.
        // All four are the one LadderPicker the keeper ladder uses. They were four hand-rolled copies
        // of the same button loop, which is also how they came to disagree about button gaps (8 px
        // here, 6 px on the keeper row).
        // Values are roster sizes INCLUDING the keeper (shirt 0), the "N a side" convention the
        // pitch sizing uses - so 2 a side is a keeper and one outfielder, the authored {GK, ST}
        // formation, and it is labelled 2 v 2 for the same reason 3 is labelled 3 v 3. perSide 1
        // would be a keeper and NOBODY, the shirt invariant every consumer clamps away
        // (Footballer.PerSide, GameBootstrap's Max(2, ...)).
        //
        // Capped at 5 a side: 11v11 was a mostly-AI match wearing a big number. Single player has no
        // seating limit of its own (the 8-slot board only binds multiplayer), so it keeps 5 where the
        // host picker stops at 4 - these lists are deliberately allowed to differ, because the reason
        // multiplayer stops earlier does not exist here.
        static readonly int[]    ScrimSizes     = { 2, 3, 5 };
        static readonly string[] ScrimSizeNames = { "2 v 2", "3 v 3", "5 v 5" };
        static readonly float[]  ScrimMins      = { 2f, 3f, 5f, 10f };
        static readonly string[] ScrimMinNames  = { "2 min", "3 min", "5 min", "10 min" };

        void MatchPickers(float lx, ref float row, float lw)
        {
            int before = _scrimPerSide;
            _scrimPerSide = ScrimSizes[LadderPicker(lx, ref row, lw, "Team size:", ScrimSizeNames,
                                                    Mathf.Max(0, System.Array.IndexOf(ScrimSizes, _scrimPerSide)))];

            // Team size changed: keep the POSITION and re-derive the shirt from the new formation. A
            // position the smaller shape does not carry (LW at 3-a-side) falls back to the striker,
            // which is the highest shirt by construction.
            if (_scrimPerSide != before)
            {
                int s = SimConfig.ShirtForPosition(_scrimPerSide, _scrimPos);
                _scrimShirt = s >= 0 ? s : _scrimPerSide - 1;
            }
            _scrimShirt = Mathf.Clamp(_scrimShirt, 0, Mathf.Max(0, _scrimPerSide - 1));

            _scrimShirt = PositionPicker(lx, ref row, lw, _scrimPerSide, _scrimShirt);
            _scrimPos   = SimConfig.PositionOf(_scrimPerSide, _scrimShirt);

            // Single player picks the tier. Multiplayer never reaches this screen and is forced to
            // Normal inside SimConfig.ApplyMatchStatics, on every peer.
            _scrimAi = (SimConfig.AiDifficulty)LadderPicker(lx, ref row, lw, "Difficulty:",
                                                           SimConfig.AiLevelNames, (int)_scrimAi);

            int mi = 1;
            for (int i = 0; i < ScrimMins.Length; i++)
                if (Mathf.Approximately(_scrimMatchMin, ScrimMins[i])) mi = i;
            _scrimMatchMin = ScrimMins[LadderPicker(lx, ref row, lw, "Match length:", ScrimMinNames, mi)];
        }

        // ---- Position picker (Pro Clubs style) ----
        // One button per SHIRT, labelled with the position that shirt plays in this formation, wrapped
        // at PosPerRow. Buttons are shirts and NOT position names because a formation can carry the
        // same position twice (11-a-side has two CBs and two CMs) and the sim identifies a body by its
        // shirt, never by its label. Shirt 0 is the keeper, so picking GK is picking the keeper role -
        // there is no second role control that could contradict it.
        //
        // Static so the multiplayer lobby can draw the identical grid, where a click is a SLOT REQUEST
        // through the host's existing slot table rather than a local write: first come first served, a
        // taken shirt is refused, and nothing new goes on the wire.
        const int PosPerRow = 6;
        static int _posNamesFor = -1;
        static string[] _posNames;

        public static int PositionPicker(float lx, ref float row, float lw, int perSide, int shirt)
        {
            var form = SimConfig.Formation(perSide);
            if (_posNamesFor != perSide || _posNames == null || _posNames.Length != form.Length)
            {
                _posNames = new string[form.Length];
                for (int i = 0; i < form.Length; i++) _posNames[i] = i + " " + form[i];
                _posNamesFor = perSide;   // cached: enum ToString per shirt per frame is pure garbage
            }
            // Nobody has to pick anything for a match to start - every shirt the humans do not take is
            // already filled by an AI body, so this only ever moves WHICH body you are.
            UITheme.Label(new Rect(lx, row, lw, 20f), "Rest are AI.", RowValue());
            return LadderPicker(lx, ref row, lw, "You play:", _posNames,
                                Mathf.Clamp(shirt, 0, form.Length - 1), PosPerRow, 12);
        }

        // ---- ONE discrete-button picker for every ladder on this screen ----
        // `cur` is the selected index and the picked index comes back, so the caller keeps the mapping
        // from index to value and this method never learns what a row means. Wide sets wrap at perRow,
        // and each extra button row costs exactly one RowH so RowCount() stays an exact count.
        public static int GridRows(int n, int perRow)
            => perRow <= 0 ? 1 : Mathf.Max(1, (n + perRow - 1) / perRow);

        public static int LadderPicker(float lx, ref float row, float lw, string label,
                                       string[] names, int cur, int perRow = 0, int fontSize = 13)
        {
            if (names == null || names.Length == 0) { EndRow(lx, ref row, lw); return 0; }
            UITheme.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
            int per = perRow <= 0 ? names.Length : Mathf.Min(perRow, names.Length);
            int rows = GridRows(names.Length, per);
            const float gap = 6f;
            float bw = (lw - gap * (per - 1)) / per;
            cur = Mathf.Clamp(cur, 0, names.Length - 1);
            for (int i = 0; i < names.Length; i++)
            {
                int r = i / per, c = i % per;
                bool sel = i == cur;
                var s = PickStyle(sel); s.fontSize = fontSize;
                var rect = new Rect(lx + c * (bw + gap), row + 22f + r * RowH, bw, 28f);
                if (UITheme.Toggle(rect, names[i], sel, s)) cur = i;
            }
            row += RowH * (rows - 1);   // EndRow adds the last one, and puts the divider under it
            EndRow(lx, ref row, lw);
            return cur;
        }

        // Which modes place their own dead ball. Accuracy joined Free Kick here so both dead-ball
        // modes are set up through the identical map instead of one map and one set of sliders.
        // Accuracy PRACTICE places its ball here; the CHALLENGE picks a fresh random spot in the
        // shot band (SimConfig.AccuracySpotNear/Far) every round, so it shows no map at all.
        bool PlacesSetPiece() => (_mode == GameMode.FreeKick && !_penaltyMode)
                              || (_mode == GameMode.Accuracy && SimConfig.AccuracyPractice);

        // Map the sliders onto SimConfig values.
        void Apply()
        {
            // Match only uses its own pickers - but it must still WRITE the shared dead-ball
            // statics, not skip them. GoalWidth/GoalHeight/BallSpeedMul are mutable statics that only
            // the set-piece and accuracy paths ever assign, so returning here left whatever the last
            // mode set: play a 1.5x-goal set piece, back out, start a match, and the match runs
            // with a 10.98 m goal. That is not cosmetic - SimConfig.GoalWidth is read by the goal
            // detection in BallController, by the keeper's own positioning (Goalkeeper.cs:158 and :243)
            // and by the AI's aim (Footballer.cs:309), so a stale value mis-sizes all three at once and
            // is a plausible contributor to "most shots go in".
            //
            // Match DOES have a goal-size picker now (the same GoalEditor picture Striker uses), so
            // the goal is restored after ApplyMatchStatics below rather than left at the regulation
            // value that reset writes. Everything else it resets is still the leak-closing behaviour.
            if (_mode == GameMode.Match)
            {
                SimConfig.MatchPerSide = _scrimPerSide;
                SimConfig.MatchSeconds = _scrimMatchMin * 60f;
                // Shirt, not role: PlayerRole is derived from it inside the reset below, so a
                // position and a role cannot disagree about whether you are the keeper.
                SimConfig.MatchShirt = _scrimShirt;
                SimConfig.AiLevel = _scrimAi;
                // The three goal/ball lines that used to sit here were only three of the SEVEN statics
                // that leak in from a previously played mode - keeper ability, striker speed, keeper
                // strafe speed and keeper jump leak the same way and were not being written. They are
                // now reset in one place shared with the networked branch, because a second copy of
                // the list is what let the goal size drift in the first place.
                SimConfig.ApplyMatchStatics(networked: false);
                // AFTER the reset, which unconditionally writes the regulation goal: this screen's
                // picture is a deliberate choice and has to outlive it. (The networked path never
                // calls ApplyMatchStatics, so the host's goal is safe there for the same reason.)
                SimConfig.GoalWidth  = BaseGoalWidth  * _goalWidth;
                SimConfig.GoalHeight = BaseGoalHeight * _goalHeight;
                return;
            }

            SimConfig.GoalWidth  = BaseGoalWidth  * _goalWidth;
            SimConfig.GoalHeight = BaseGoalHeight * _goalHeight;
            // Every mode that DRAWS the shot-speed slider writes it here; Striker's lives on the
            // cross map and accuracy practice has no such slider, so neither writes a stale value.
            if (_mode != GameMode.Striker && _mode != GameMode.Accuracy)
                SimConfig.BallSpeedMul = _ballSpeed;

            if (_mode == GameMode.Striker)
            {
                // Shot speed + cross interval are NOT written here any more: the cross map's Crosser
                // tab owns them and seeds both statics itself (CrossMap.Apply, called from the
                // driver's Configure), so writing a stale slider value here would just be overwritten
                // a moment later - and would silently win for the modes that never open that panel.
                // Striker speed has no slider in this mode any more: base pace.
                SimConfig.KeeperAbility    = _keeperAbility;
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed;
            }
            else if (_mode == GameMode.Accuracy)
            {
                // PRACTICE only - the challenge never opens this screen. The keeper comes off the
                // goal picture's keeper row - a None..Insane ladder in practice, a Yes/No in the
                // challenge - and both answer "no keeper" as ability 0.
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed;
                SimConfig.KeeperAbility    = _keeperAbility;
                // One write for both screens, so the ladder's "None" and the yes/no row's "No"
                // cannot end up disagreeing with the flag the round ladder reads.
                SimConfig.AccuracyNoKeeper = _keeperAbility <= 0.001f;
                SimConfig.WallCount        = 0;   // accuracy never has a wall

                if (SimConfig.AccuracyPractice)
                {
                    // Target size/pace are practice's alone, stored 0-1 across the challenge's range.
                    SimConfig.AccuracyPracticeSpeed01 = _accPracticeSpeed / 100f;
                    SimConfig.AccuracyPracticeSize01  = _accPracticeSize / 100f;

                    // The ball spot is whatever was placed on the map to the right; it is a real
                    // placement, so the driver reads it exactly as free-kick mode does.
                    if (!_fkInit) { SetPieceMap.DefaultPlacement(out _fkBall, out _fkWall); _fkInit = true; }
                    SimConfig.SetPiecePlaced      = true;
                    SimConfig.SetPieceBallSpot    = _fkBall;
                    SimConfig.SetPieceWallCenter  = _fkWall;
                    SimConfig.SetPieceRandomSpots = _fkRandom;
                }
                else
                {
                    // The challenge picks its own spot every round; a placement left over from a
                    // practice session must not follow it in.
                    SimConfig.SetPiecePlaced      = false;
                    SimConfig.SetPieceRandomSpots = false;
                }
            }
            else if (_mode == GameMode.Goalkeeper)
            {
                SimConfig.ShotDifficulty    = _shotDifficulty;
                SimConfig.KeeperStrafeSpeed = BaseKeeperSpeed * _keeperSpeed;
                SimConfig.KeeperJumpVel     = BaseKeeperJump * _keeperJump;
                SimConfig.KeeperServeInterval = _keeperServeInterval;
            }
            else
            {
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed * _strikerSpeed;
                // No ServeInterval here: these are dead-ball modes (SetPieceTaker), none of which
                // build a Crosser, so the cadence this used to write was never read. The only mode
                // with an auto-crosser is Striker, and its cadence lives on the cross map now.
                // One keeper slider covers all of these modes (0 = no keeper is built at all).
                SimConfig.KeeperAbility    = _keeperAbility;
                SimConfig.PenaltyMode      = _penaltyMode;
                SimConfig.FreeKickDistance = _freeKickDistance;
                SimConfig.WallCount        = Mathf.RoundToInt(_wallCount);
                SimConfig.WallDistance     = _wallDistance;
                SimConfig.WallLateralOffset = _wallOffset;

                // Publish the placed free kick. Both dead-ball modes place one now; a penalty is
                // always the fixed 11 m spot, so it opts out.
                bool placed = PlacesSetPiece();
                if (placed && !_fkInit) { SetPieceMap.DefaultPlacement(out _fkBall, out _fkWall); _fkInit = true; }
                SimConfig.SetPiecePlaced      = placed;
                SimConfig.SetPieceBallSpot    = _fkBall;
                SimConfig.SetPieceWallCenter  = _fkWall;
                SimConfig.SetPieceRandomSpots = placed && _fkRandom;
            }
        }

        // ---- shared row furniture ----
        // Reused rather than built per call so every row lines up and reads as one table.
        static GUIStyle _rowLbl, _rowVal;
        static GUIStyle RowLabel()
        {
            _rowLbl ??= new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _rowLbl.normal.textColor = UITheme.Ink;
            return _rowLbl;
        }
        static GUIStyle RowValue()
        {
            _rowVal ??= new GUIStyle(GUI.skin.label)
            { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            _rowVal.normal.textColor = UITheme.Gold;
            return _rowVal;
        }
        // Selected choice buttons carry the tint; the label goes gold to match.
        static GUIStyle PickStyle(bool sel)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
            if (sel) s.normal.textColor = UITheme.Gold;
            return s;
        }
        // Hairline between rows, so a long settings list reads as a table instead of a wall.
        static void EndRow(float lx, ref float row, float lw)
        {
            UITheme.Divider(lx, row + RowH - 5f, lw);
            row += RowH;
        }

        // Keeper strength as five named steps instead of a bare 0..1 slider. "0.35x" told the player
        // nothing about what they were about to face, and None is not a slider end-stop but a
        // different mode: at 0 no goalkeeper is built at all (see GameBootstrap), so the goal is
        // open. The values are the same 0..1 SimConfig.KeeperAbility the slider fed, so nothing
        // downstream changes. Snapped to the nearest step on entry, so a value left over from the
        // old slider (or from a future retune of these steps) still lands on a named button.
        // The names and the five values MOVED to SimConfig.AiLevelNames / AiLevelAbility, because the
        // AI difficulty ladder is the same five steps and a second local copy of them is a guarantee
        // that one day they disagree about what "Hard" means. The picker itself is now the shared
        // LadderPicker, so this row and the difficulty row are the same widget rather than two.
        float KeeperPicker(float lx, ref float row, float lw, string label, float val)
            => SimConfig.AiLevelAbility[
                   LadderPicker(lx, ref row, lw, label + ":", SimConfig.AiLevelNames,
                                SimConfig.NearestAiLevel(val))];

        // Multiplier slider (min..max, shown as "x") with a per-row reset to its default.
        float Slider(float lx, ref float row, float lw, string label, float val,
                     float min, float max, float def)
        {
            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };
            float resetW = 52f, gap = 10f, sliderW = lw - resetW - gap;

            // Name on the left, value right-aligned over the track: the pair reads as a spec sheet.
            UITheme.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
            UITheme.Label(new Rect(lx, row, sliderW, 20f), $"{val:0.00}x", RowValue());
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, sliderW, 20f), val, min, max);
            if (UITheme.Button(new Rect(lx + sliderW + gap, row + 20f, resetW, 24f), "reset", smallBtn))
                val = def;
            EndRow(lx, ref row, lw);
            return val;
        }

        // Raw-value slider (min..max, formatted with an optional unit suffix).
        float RawSlider(float lx, ref float row, float lw, string label, float val,
                        float min, float max, string fmt, string unit)
        {
            string u = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            UITheme.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
            UITheme.Label(new Rect(lx, row, lw, 20f), val.ToString(fmt) + u, RowValue());
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, lw, 20f), val, min, max);
            EndRow(lx, ref row, lw);
            return val;
        }

        bool Toggle(float lx, ref float row, float lw, string label, bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            st.normal.textColor = UITheme.Ink;
            st.onNormal.textColor = st.onHover.textColor =
                st.onActive.textColor = st.onFocused.textColor = UITheme.Gold;
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "", st);
            UITheme.Label(new Rect(lx, row + 6f, lw, 26f), "  " + label, st);
            EndRow(lx, ref row, lw);
            return val;
        }

        void ResetAll()
        {
            _goalWidth = _goalHeight = _ballSpeed = 1f;
            _strikerSpeed = _keeperSpeed = _keeperJump = 1f;
            // The cross panel's settings are static for the same reason these are, so Reset All has
            // to clear them too or the two halves of "everything back to default" disagree.
            CrossMap.ResetSession();
            _keeperAbility = _shotDifficulty = 0.5f;
            _keeperServeInterval = SimConfig.KeeperServeIntervalBase;
            _accPracticeSpeed = 35f; _accPracticeSize = 100f;
            _penaltyMode = false; _freeKickDistance = 20f;
            _wallCount = 4f; _wallDistance = 9.15f; _wallOffset = 0f;
            _fkInit = false; _fkEdit = 0; _fkRandom = false;
            // These are static and survived Reset All, so a match kept whatever was last dialled
            // in even after the button said everything was back to default.
            _scrimPerSide = 3; _scrimShirt = 2; _scrimPos = SimConfig.ScrimPos.ST;
            _scrimAi = SimConfig.AiDifficulty.Normal; _scrimMatchMin = 3f;
        }
    }
}
