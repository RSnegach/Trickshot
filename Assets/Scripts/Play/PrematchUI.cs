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
        // are the same numbers SimConfig.ApplyScrimmageStatics resets to, and two hand-copied lists of
        // one set of defaults is how the goal-size leak survived as long as it did.
        const float BaseGoalWidth = SimConfig.GoalWidthBase, BaseGoalHeight = SimConfig.GoalHeightBase;
        const float BaseServeInterval = 3.5f;
        const float BaseStrikerSpeed = SimConfig.StrikerMoveSpeedBase;
        const float BaseKeeperSpeed = SimConfig.KeeperStrafeSpeedBase;
        static readonly float BaseKeeperJump = SimConfig.KeeperJumpVelBase;

        // ---- Multiplier sliders (value is a multiplier; ranges chosen so every point
        //      on the slider is playable). Defaults are all 1.0x except abilities. ----
        // STATIC so the last-dialed setup survives leaving and reopening this screen
        // (e.g. pausing to Match Setup mid-match). Resets to defaults only on a new
        // session or the Reset All button.
        static float _goalWidth = 1f, _goalHeight = 1f, _ballSpeed = 1f;
        static float _crossInterval = 1f, _keeperAbility = 0.5f, _strikerSpeed = 1f;   // striker
        static float _shotDifficulty = 0.5f, _keeperSpeed = 1f, _keeperJump = 1f;      // keeper

        // ---- Challenge-mode raw settings ----
        static float _timeTrialSeconds = 60f;
        static float _accuracySeconds = 90f;
        static float _accuracyTargets = 4f;
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

        // Freeplay delivery
        static SimConfig.Delivery _delivery = SimConfig.Delivery.AutoCross;
        static Vector3 _aimTarget = SimConfig.ServeTarget;   // where an aimed cross lands

        // Scrimmage
        static int _scrimPerSide = 3;                                  // 3 / 5 / 11
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
            // Scrimmage: team size + position + difficulty + match length picker rows, no goal/ball
            // sliders. The position grid wraps at PosPerRow shirts and every extra button row costs
            // exactly one RowH, so this stays an exact count rather than an estimate.
            if (_mode == GameMode.Scrimmage)
                return 3 + GridRows(Mathf.Max(1, _scrimPerSide), PosPerRow);

            int n = 3; // goal width, goal height, ball velocity (all modes)
            if (_mode == GameMode.Striker) n += 3;
            else if (_mode == GameMode.Goalkeeper) n += 3;
            else
            {
                n += 1; // striker speed
                n += 1; // keeper ability (every mode in this branch can carry an AI keeper)
                if (_mode == GameMode.Freeplay)
                {
                    n += 1; // delivery picker
                    if (_delivery != SimConfig.Delivery.BallAtFeet) n += 1; // cross interval
                    if (_delivery == SimConfig.Delivery.AimSpot) n += 3;    // aim map (~154px)
                }
                else if (_mode == GameMode.TimeTrial) n += 2;   // cross interval + round time
                // Accuracy: time + targets + wall count. The spot and the wall are PLACED on the
                // map panel now, so the distance/wall-distance/wall-offset rows are gone.
                else if (_mode == GameMode.Accuracy)  n += 3;
                // Free kick: penalty toggle (+ wall players; spot + wall are placed on the map panel)
                else if (_mode == GameMode.FreeKick)  n += _penaltyMode ? 1 : 2;
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
            float panelH = HeadH + RowCount() * RowH + FootH;
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
                             _mode.ToString().ToUpper() + " - SETUP", title, UITheme.Ink, 0.75f, 2f);
            // Short gold stub under the wordmark, then a hairline closing the header band.
            UITheme.Fill(new Rect(x + 30f, y + 52f, 54f, 2.5f), UITheme.Gold);
            UITheme.Divider(x + 30f, y + HeadH - 12f, PanelW - 60f);

            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (UITheme.Button(new Rect(x + PanelW - 130f, y + 20f, 110f, 30f), "Reset All", smallBtn))
                ResetAll();

            float row = y + HeadH;
            float lx = x + 30f, lw = PanelW - 60f;

            // Scrimmage: pickers only (no goal/ball sliders), then Back/Start.
            if (_mode == GameMode.Scrimmage)
            {
                ScrimmagePickers(lx, ref row, lw);
                DrawNav(x, y, panelH);
                return;
            }

            // Multiplier sliders with per-slider ranges. Goal/ball apply to every mode.
            _goalWidth  = Slider(lx, ref row, lw, "Goal width",   _goalWidth,  0.6f, 1.5f, 1f);
            _goalHeight = Slider(lx, ref row, lw, "Goal height",  _goalHeight, 0.6f, 1.5f, 1f);
            _ballSpeed  = Slider(lx, ref row, lw, "Ball velocity", _ballSpeed, 0.5f, 2f,   1f);

            if (_mode == GameMode.Striker)
            {
                _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval, 0.4f, 2f, 1f);
                _keeperAbility = KeeperPicker(lx, ref row, lw, "Keeper", _keeperAbility);
                _strikerSpeed  = Slider(lx, ref row, lw, "Striker speed",  _strikerSpeed,  0.5f, 1.8f, 1f);
            }
            else if (_mode == GameMode.Goalkeeper)
            {
                _shotDifficulty = Slider(lx, ref row, lw, "Shot difficulty", _shotDifficulty, 0f,   1f, 0.5f);
                _keeperSpeed    = Slider(lx, ref row, lw, "Keeper speed",    _keeperSpeed,    0.5f, 1.8f, 1f);
                _keeperJump     = Slider(lx, ref row, lw, "Keeper jump height", _keeperJump,  0.6f, 1.6f, 1f);
            }
            else
            {
                _strikerSpeed = Slider(lx, ref row, lw, "Striker speed", _strikerSpeed, 0.5f, 1.8f, 1f);
                // Every mode down here can carry an AI keeper, so this picker is shared: free kicks,
                // the accuracy gallery, freeplay and the time trial all read it (None = no keeper is
                // built at all, an open goal).
                _keeperAbility = KeeperPicker(lx, ref row, lw, "Keeper", _keeperAbility);

                if (_mode == GameMode.Freeplay)
                {
                    DeliveryPicker(lx, ref row, lw);
                    // Cross interval only matters when the crosser is delivering.
                    if (_delivery != SimConfig.Delivery.BallAtFeet)
                        _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval, 0.4f, 2f, 1f);
                    if (_delivery == SimConfig.Delivery.AimSpot)
                        AimMap(lx, ref row, lw);
                }
                // Accuracy has no crosser any more (it's a dead-ball free-kick gallery), so the
                // cross interval only applies to Time Trial here.
                else if (_mode == GameMode.TimeTrial)
                    _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval, 0.4f, 2f, 1f);

                if (_mode == GameMode.TimeTrial)
                    _timeTrialSeconds = RawSlider(lx, ref row, lw, "Round time", _timeTrialSeconds, 30f, 180f, "0", "s");
                else if (_mode == GameMode.Accuracy)
                {
                    // Accuracy is a free-kick shooting gallery, so it carries the free-kick furniture
                    // too - but the spot and the wall are PLACED on the map panel to the right, the
                    // same control the multiplayer host uses, so only the wall SIZE is a number here.
                    // The old distance / wall distance / wall offset sliders described the same three
                    // degrees of freedom the map already covers, and described them blindly: you
                    // dialled numbers and found out where the kick actually was once the match built.
                    _accuracySeconds = RawSlider(lx, ref row, lw, "Round time", _accuracySeconds, 30f, 180f, "0", "s");
                    _accuracyTargets = RawSlider(lx, ref row, lw, "Targets up", _accuracyTargets, 1f, 8f, "0", "");
                    _wallCount    = RawSlider(lx, ref row, lw, "Wall players (0 = no wall)", _wallCount, 0f, 6f, "0", "");
                }
                else if (_mode == GameMode.FreeKick)
                {
                    // The spot and the wall are PLACED on the map panel (right of this one) instead of
                    // being dialed in with distance/offset sliders. Only the wall size is a number.
                    _penaltyMode = Toggle(lx, ref row, lw, "Penalty mode (spot, no wall)", _penaltyMode);
                    if (!_penaltyMode)
                        _wallCount = RawSlider(lx, ref row, lw, "Wall players", _wallCount, 0f, 6f, "0", "");
                }
            }

            // Both dead-ball modes get the same placement panel the multiplayer host uses, drawn on
            // the right (the stat card owns the left). A penalty is a fixed spot with no wall, so
            // there is nothing to place for one.
            if (PlacesSetPiece())
            {
                if (!_fkInit) { SetPieceMap.DefaultPlacement(out _fkBall, out _fkWall); _fkInit = true; }
                SetPieceMap.DrawSetupPanel(x + PanelW + 16f, y, 300f, 300f,
                                           ref _fkBall, ref _fkWall, ref _fkEdit, ref _fkRandom,
                                           "Random spot each attempt.");
            }

            DrawNav(x, y, panelH);
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

        // Scrimmage pickers: team size (per side) and the human's role.
        // Scrimmage pickers: team size, the POSITION the human plays, AI difficulty, match length.
        // All four are the one LadderPicker the keeper ladder uses. They were four hand-rolled copies
        // of the same button loop, which is also how they came to disagree about button gaps (8 px
        // here, 6 px on the keeper row).
        static readonly int[]    ScrimSizes     = { 3, 5, 11 };
        static readonly string[] ScrimSizeNames = { "3 v 3", "5 v 5", "11 v 11" };
        static readonly float[]  ScrimMins      = { 2f, 3f, 5f, 10f };
        static readonly string[] ScrimMinNames  = { "2 min", "3 min", "5 min", "10 min" };

        void ScrimmagePickers(float lx, ref float row, float lw)
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
            // Normal inside SimConfig.ApplyScrimmageStatics, on every peer.
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
            GUI.Label(new Rect(lx, row, lw, 20f), "Rest are AI.", RowValue());
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
            GUI.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
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
        bool PlacesSetPiece() => _mode == GameMode.Accuracy
                             || (_mode == GameMode.FreeKick && !_penaltyMode);

        // Map the sliders onto SimConfig values.
        void Apply()
        {
            // Scrimmage only uses its own pickers - but it must still WRITE the shared dead-ball
            // statics, not skip them. GoalWidth/GoalHeight/BallSpeedMul are mutable statics that only
            // the set-piece and accuracy paths ever assign, so returning here left whatever the last
            // mode set: play a 1.5x-goal set piece, back out, start a scrimmage, and the scrimmage runs
            // with a 10.98 m goal. That is not cosmetic - SimConfig.GoalWidth is read by the goal
            // detection in BallController, by the keeper's own positioning (Goalkeeper.cs:158 and :243)
            // and by the AI's aim (Footballer.cs:309), so a stale value mis-sizes all three at once and
            // is a plausible contributor to "most shots go in".
            //
            // Scrimmage has no goal-size picker and is not getting one, so these are canonical
            // regulation values written to close a leak rather than new settings.
            if (_mode == GameMode.Scrimmage)
            {
                SimConfig.ScrimmagePerSide = _scrimPerSide;
                SimConfig.ScrimmageMatchSeconds = _scrimMatchMin * 60f;
                // Shirt, not role: ScrimmageRole is derived from it inside the reset below, so a
                // position and a role cannot disagree about whether you are the keeper.
                SimConfig.ScrimmageShirt = _scrimShirt;
                SimConfig.AiLevel = _scrimAi;
                // The three goal/ball lines that used to sit here were only three of the SEVEN statics
                // that leak in from a previously played mode - keeper ability, striker speed, keeper
                // strafe speed and keeper jump leak the same way and were not being written. They are
                // now reset in one place shared with the networked branch, because a second copy of
                // the list is what let the goal size drift in the first place.
                SimConfig.ApplyScrimmageStatics(networked: false);
                return;
            }

            SimConfig.GoalWidth  = BaseGoalWidth  * _goalWidth;
            SimConfig.GoalHeight = BaseGoalHeight * _goalHeight;
            SimConfig.BallSpeedMul = _ballSpeed;

            if (_mode == GameMode.Striker)
            {
                SimConfig.ServeInterval    = BaseServeInterval * _crossInterval;
                SimConfig.KeeperAbility    = _keeperAbility;
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed * _strikerSpeed;
            }
            else if (_mode == GameMode.Goalkeeper)
            {
                SimConfig.ShotDifficulty    = _shotDifficulty;
                SimConfig.KeeperStrafeSpeed = BaseKeeperSpeed * _keeperSpeed;
                SimConfig.KeeperJumpVel     = BaseKeeperJump * _keeperJump;
            }
            else
            {
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed * _strikerSpeed;
                SimConfig.ServeInterval    = BaseServeInterval * _crossInterval;
                if (_mode == GameMode.Freeplay)
                {
                    SimConfig.FreeplayDelivery = _delivery;
                    SimConfig.FreeplayAimTarget = _aimTarget;
                }
                // One keeper slider covers all of these modes (0 = no keeper is built at all).
                SimConfig.KeeperAbility    = _keeperAbility;
                SimConfig.TimeTrialSeconds = _timeTrialSeconds;
                SimConfig.AccuracySeconds  = _accuracySeconds;
                SimConfig.AccuracyTargetCount = Mathf.RoundToInt(_accuracyTargets);
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
            GUI.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
            GUI.Label(new Rect(lx, row, sliderW, 20f), $"{val:0.00}x", RowValue());
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
            GUI.Label(new Rect(lx, row, lw, 20f), label, RowLabel());
            GUI.Label(new Rect(lx, row, lw, 20f), val.ToString(fmt) + u, RowValue());
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, lw, 20f), val, min, max);
            EndRow(lx, ref row, lw);
            return val;
        }

        // Freeplay delivery: a row of buttons picking how the ball comes in.
        static readonly SimConfig.Delivery[] Deliveries =
        {
            SimConfig.Delivery.AutoCross, SimConfig.Delivery.CornerLeft,
            SimConfig.Delivery.CornerRight, SimConfig.Delivery.AimSpot,
            SimConfig.Delivery.BallAtFeet,
        };
        static readonly string[] DeliveryNames = { "Auto", "Cnr L", "Cnr R", "Aim", "Feet" };

        void DeliveryPicker(float lx, ref float row, float lw)
        {
            GUI.Label(new Rect(lx, row, lw, 20f), "Ball delivery:", RowLabel());
            float bw = (lw - 4f * (Deliveries.Length - 1)) / Deliveries.Length;
            for (int i = 0; i < Deliveries.Length; i++)
            {
                bool sel = _delivery == Deliveries[i];
                var s = PickStyle(sel); s.fontSize = 12;
                if (UITheme.Toggle(new Rect(lx + i * (bw + 4f), row + 22f, bw, 26f), DeliveryNames[i], sel, s))
                    _delivery = Deliveries[i];
            }
            EndRow(lx, ref row, lw);
        }

        // Clickable top-down map of the penalty box; click to place where the aimed cross
        // lands. X spans the goal width; the vertical axis spans out from the goal line.
        void AimMap(float lx, ref float row, float lw)
        {
            UITheme.Hint(new Rect(lx, row, lw, 18f), "Click to place where the cross lands:", TextAnchor.MiddleLeft);
            row += 22f;

            float mapW = lw, mapH = 120f;
            var mapRect = new Rect(lx, row, mapW, mapH);
            // Pitch band shown: full goal width (+margin) across, 18m out from the line deep.
            float halfShown = SimConfig.GoalWidth * 0.5f + 3f;
            float depthShown = 18f;

            // Turf-coloured plate in the chosen venue's shade, darkened so markings read over it.
            Color turf = StadiumStyle.Active != null ? StadiumStyle.Active.Grass : new Color(0.15f, 0.35f, 0.18f);
            UITheme.Chip(mapRect, new Color(turf.r * 0.45f, turf.g * 0.45f, turf.b * 0.45f, 0.96f));
            // Six-yard hint band and the centre line, faint, purely for orientation.
            UITheme.Fill(new Rect(mapRect.center.x - mapW * 0.22f, mapRect.y + 1f, mapW * 0.44f, 1f), new Color(1f, 1f, 1f, 0.12f));
            UITheme.Fill(new Rect(mapRect.center.x, mapRect.y + 8f, 1f, mapH - 10f), new Color(1f, 1f, 1f, 0.10f));

            // Goal (thin bar along the top edge).
            float goalPxHalf = (SimConfig.GoalWidth * 0.5f / halfShown) * (mapW * 0.5f);
            UITheme.Fill(new Rect(mapRect.center.x - goalPxHalf, mapRect.y + 2f, goalPxHalf * 2f, 6f), new Color(1f, 1f, 1f, 0.85f));

            // Handle a click inside the map -> world aim target.
            Event e = Event.current;
            if (e.type == EventType.MouseDown && mapRect.Contains(e.mousePosition))
            {
                float fx = (e.mousePosition.x - mapRect.x) / mapW;        // 0..1 left->right
                float fy = (e.mousePosition.y - mapRect.y) / mapH;        // 0..1 top(goal)->bottom(out)
                float wx = Mathf.Lerp(-halfShown, halfShown, fx);
                float wz = SimConfig.GoalCenter.z - Mathf.Lerp(0f, depthShown, fy);
                _aimTarget = new Vector3(wx, 0.25f, wz);
                e.Use();
            }

            // Draw the current marker.
            float mfx = Mathf.InverseLerp(-halfShown, halfShown, _aimTarget.x);
            float mfy = Mathf.InverseLerp(0f, depthShown, SimConfig.GoalCenter.z - _aimTarget.z);
            float mx = mapRect.x + mfx * mapW, my = mapRect.y + mfy * mapH;
            UITheme.Glow(new Rect(mx - 16f, my - 16f, 32f, 32f), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.55f));
            UITheme.Fill(new Rect(mx - 7f, my - 1f, 14f, 2f), UITheme.Gold);
            UITheme.Fill(new Rect(mx - 1f, my - 7f, 2f, 14f), UITheme.Gold);

            row += mapH + 12f;
        }

        bool Toggle(float lx, ref float row, float lw, string label, bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15 };
            st.normal.textColor = UITheme.Ink;
            st.onNormal.textColor = st.onHover.textColor =
                st.onActive.textColor = st.onFocused.textColor = UITheme.Gold;
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "  " + label, st);
            EndRow(lx, ref row, lw);
            return val;
        }

        void ResetAll()
        {
            _goalWidth = _goalHeight = _ballSpeed = 1f;
            _crossInterval = _strikerSpeed = _keeperSpeed = _keeperJump = 1f;
            _keeperAbility = _shotDifficulty = 0.5f;
            _timeTrialSeconds = 60f; _accuracySeconds = 90f; _accuracyTargets = 4f;
            _penaltyMode = false; _freeKickDistance = 20f;
            _wallCount = 4f; _wallDistance = 9.15f; _wallOffset = 0f;
            _fkInit = false; _fkEdit = 0; _fkRandom = false;
            _delivery = SimConfig.Delivery.AutoCross; _aimTarget = SimConfig.ServeTarget;
            // These are static and survived Reset All, so a scrimmage kept whatever was last dialled
            // in even after the button said everything was back to default.
            _scrimPerSide = 3; _scrimShirt = 2; _scrimPos = SimConfig.ScrimPos.ST;
            _scrimAi = SimConfig.AiDifficulty.Normal; _scrimMatchMin = 3f;
        }
    }
}
