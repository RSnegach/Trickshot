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

        // Base values the 1.00 multipliers map to.
        const float BaseGoalWidth = 7.32f, BaseGoalHeight = 2.44f, BaseServeInterval = 3.5f;
        const float BaseStrikerSpeed = 4.8f, BaseKeeperSpeed = 5.5f;
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

        // Freeplay delivery
        static SimConfig.Delivery _delivery = SimConfig.Delivery.AutoCross;
        static Vector3 _aimTarget = SimConfig.ServeTarget;   // where an aimed cross lands

        // Scrimmage
        static int _scrimPerSide = 3;                                  // 3 / 5 / 11
        static SimConfig.ScrimRole _scrimRole = SimConfig.ScrimRole.Outfield;
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // How many slider/toggle rows this mode shows, so the panel is sized to fit.
        int RowCount()
        {
            // Scrimmage: team size + role + match length picker rows, no goal/ball sliders.
            if (_mode == GameMode.Scrimmage) return 3;

            int n = 3; // goal width, goal height, ball velocity (all modes)
            if (_mode == GameMode.Striker) n += 3;
            else if (_mode == GameMode.Goalkeeper) n += 3;
            else
            {
                n += 1; // striker speed
                if (_mode == GameMode.Freeplay)
                {
                    n += 1; // delivery picker
                    if (_delivery != SimConfig.Delivery.BallAtFeet) n += 1; // cross interval
                    if (_delivery == SimConfig.Delivery.AimSpot) n += 3;    // aim map (~154px)
                }
                else if (_mode == GameMode.TimeTrial) n += 2;   // cross interval + round time
                else if (_mode == GameMode.Accuracy)  n += 3;   // cross interval + time + targets
                else if (_mode == GameMode.FreeKick)  n += _penaltyMode ? 1 : 5; // toggle (+4 wall rows)
            }
            return n;
        }

        void OnGUI()
        {
            float panelH = HeadH + RowCount() * RowH + FootH;
            float x = Screen.width * 0.5f - PanelW * 0.5f;
            float y = Screen.height * 0.5f - panelH * 0.5f;
            GUI.Box(new Rect(x, y, PanelW, panelH), GUIContent.none);

            // Attribute card to the LEFT of the settings panel (custom-player modes only;
            // keeper mode uses a fixed keeper, so no card).
            if (_mode != GameMode.Goalkeeper) DrawStatCard(x - 300f, y);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x + 30f, y + 16f, PanelW - 200f, 40f), _mode.ToString().ToUpper() + " - SETUP", title);

            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(x + PanelW - 150f, y + 20f, 120f, 30f), "Reset All", smallBtn))
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
                _keeperAbility = Slider(lx, ref row, lw, "Keeper ability", _keeperAbility, 0f,   1f, 0.5f);
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

                if (_mode == GameMode.Freeplay)
                {
                    DeliveryPicker(lx, ref row, lw);
                    // Cross interval only matters when the crosser is delivering.
                    if (_delivery != SimConfig.Delivery.BallAtFeet)
                        _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval, 0.4f, 2f, 1f);
                    if (_delivery == SimConfig.Delivery.AimSpot)
                        AimMap(lx, ref row, lw);
                }
                else if (_mode == GameMode.TimeTrial || _mode == GameMode.Accuracy)
                    _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval, 0.4f, 2f, 1f);

                if (_mode == GameMode.TimeTrial)
                    _timeTrialSeconds = RawSlider(lx, ref row, lw, "Round time", _timeTrialSeconds, 30f, 180f, "0", "s");
                else if (_mode == GameMode.Accuracy)
                {
                    _accuracySeconds = RawSlider(lx, ref row, lw, "Round time", _accuracySeconds, 30f, 180f, "0", "s");
                    _accuracyTargets = RawSlider(lx, ref row, lw, "Targets up", _accuracyTargets, 1f, 8f, "0", "");
                }
                else if (_mode == GameMode.FreeKick)
                {
                    _penaltyMode = Toggle(lx, ref row, lw, "Penalty mode (spot, no wall)", _penaltyMode);
                    if (!_penaltyMode)
                    {
                        _freeKickDistance = RawSlider(lx, ref row, lw, "Free kick distance", _freeKickDistance, 11f, 35f, "0", "m");
                        _wallCount    = RawSlider(lx, ref row, lw, "Wall players", _wallCount, 0f, 6f, "0", "");
                        _wallDistance = RawSlider(lx, ref row, lw, "Wall distance", _wallDistance, 5f, 12f, "0.0", "m");
                        _wallOffset   = RawSlider(lx, ref row, lw, "Wall offset", _wallOffset, -6f, 6f, "0.0", "m");
                    }
                }
            }

            DrawNav(x, y, panelH);
        }

        // Player attribute card: radar chart + numeric stat list, from the current build.
        void DrawStatCard(float x, float y)
        {
            float w = 280f, h = 430f;
            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = new Color(0.16f, 0.55f, 0.95f, 0.9f); GUI.DrawTexture(new Rect(x, y, w, 3f), Texture2D.whiteTexture);
            GUI.color = prev;

            var title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.86f, 0.32f) } };
            GUI.Label(new Rect(x + 14f, y + 8f, w - 28f, 22f), (PlayerProfile.PlayerName ?? "PLAYER").ToUpper(), title);

            StatRadar.Draw(new Rect(x + 10f, y + 30f, w - 20f, 200f));
            StatRadar.DrawList(x + 24f, y + 240f, w - 48f);
        }

        // Back/Start anchored to the far left/right screen edges.
        void DrawNav(float x, float y, float panelH)
        {
            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            float by = y + panelH - FootH + 20f;
            float bw = 170f, edge = 24f;
            if (GUI.Button(new Rect(edge, by, bw, 48f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(Screen.width - edge - bw, by, bw, 48f), "Start", btn)) { Apply(); enabled = false; _onStart?.Invoke(_mode); }
        }

        // Scrimmage pickers: team size (per side) and the human's role.
        void ScrimmagePickers(float lx, ref float row, float lw)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };

            GUI.Label(new Rect(lx, row, lw, 20f), "Team size:", st);
            int[] sizes = { 3, 5, 11 };
            string[] sizeNames = { "3 v 3", "5 v 5", "11 v 11" };
            float bw = (lw - 8f * (sizes.Length - 1)) / sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                bool sel = _scrimPerSide == sizes[i];
                var b = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) b.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (bw + 8f), row + 22f, bw, 28f), sizeNames[i], b)) _scrimPerSide = sizes[i];
            }
            row += RowH;

            GUI.Label(new Rect(lx, row, lw, 20f), "You play as:", st);
            string[] roleNames = { "Outfielder", "Goalkeeper" };
            var roles = new[] { SimConfig.ScrimRole.Outfield, SimConfig.ScrimRole.Keeper };
            float rbw = (lw - 8f) * 0.5f;
            for (int i = 0; i < roles.Length; i++)
            {
                bool sel = _scrimRole == roles[i];
                var b = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) b.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (rbw + 8f), row + 22f, rbw, 28f), roleNames[i], b)) _scrimRole = roles[i];
            }
            row += RowH;

            GUI.Label(new Rect(lx, row, lw, 20f), "Match length:", st);
            float[] mins = { 2f, 3f, 5f, 10f };
            string[] minNames = { "2 min", "3 min", "5 min", "10 min" };
            float mbw = (lw - 8f * (mins.Length - 1)) / mins.Length;
            for (int i = 0; i < mins.Length; i++)
            {
                bool sel = Mathf.Approximately(_scrimMatchMin, mins[i]);
                var b = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) b.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (mbw + 8f), row + 22f, mbw, 28f), minNames[i], b)) _scrimMatchMin = mins[i];
            }
            row += RowH;
        }

        // Map the sliders onto SimConfig values.
        void Apply()
        {
            // Scrimmage only uses its two pickers.
            if (_mode == GameMode.Scrimmage)
            {
                SimConfig.ScrimmagePerSide = _scrimPerSide;
                SimConfig.ScrimmageRole = _scrimRole;
                SimConfig.ScrimmageMatchSeconds = _scrimMatchMin * 60f;
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
                SimConfig.TimeTrialSeconds = _timeTrialSeconds;
                SimConfig.AccuracySeconds  = _accuracySeconds;
                SimConfig.AccuracyTargetCount = Mathf.RoundToInt(_accuracyTargets);
                SimConfig.PenaltyMode      = _penaltyMode;
                SimConfig.FreeKickDistance = _freeKickDistance;
                SimConfig.WallCount        = Mathf.RoundToInt(_wallCount);
                SimConfig.WallDistance     = _wallDistance;
                SimConfig.WallLateralOffset = _wallOffset;
                if (_mode == GameMode.FreeKick && SimConfig.KeeperAbility < 0.01f)
                    SimConfig.KeeperAbility = 0.7f;
            }
        }

        // Multiplier slider (min..max, shown as "x") with a per-row reset to its default.
        float Slider(float lx, ref float row, float lw, string label, float val,
                     float min, float max, float def)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };

            GUI.Label(new Rect(lx, row, lw, 20f), $"{label}:  {val:0.00}x", st);
            float resetW = 52f, gap = 10f, sliderW = lw - resetW - gap;
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, sliderW, 20f), val, min, max);
            if (GUI.Button(new Rect(lx + sliderW + gap, row + 20f, resetW, 24f), "reset", smallBtn))
                val = def;
            row += RowH;
            return val;
        }

        // Raw-value slider (min..max, formatted with an optional unit suffix).
        float RawSlider(float lx, ref float row, float lw, string label, float val,
                        float min, float max, string fmt, string unit)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            string u = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            GUI.Label(new Rect(lx, row, lw, 20f), $"{label}:  {val.ToString(fmt)}{u}", st);
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, lw, 20f), val, min, max);
            row += RowH;
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUI.Label(new Rect(lx, row, lw, 20f), "Ball delivery:", st);
            float bw = (lw - 4f * (Deliveries.Length - 1)) / Deliveries.Length;
            for (int i = 0; i < Deliveries.Length; i++)
            {
                bool sel = _delivery == Deliveries[i];
                var s = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = sel ? FontStyle.Bold : FontStyle.Normal };
                if (sel) s.normal.textColor = new Color(1f, 0.9f, 0.3f);
                if (GUI.Button(new Rect(lx + i * (bw + 4f), row + 22f, bw, 26f), DeliveryNames[i], s))
                    _delivery = Deliveries[i];
            }
            row += RowH;
        }

        // Clickable top-down map of the penalty box; click to place where the aimed cross
        // lands. X spans the goal width; the vertical axis spans out from the goal line.
        void AimMap(float lx, ref float row, float lw)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(0.85f, 0.85f, 0.88f) } };
            GUI.Label(new Rect(lx, row, lw, 18f), "Click to place where the cross lands:", st);
            row += 22f;

            float mapW = lw, mapH = 120f;
            var mapRect = new Rect(lx, row, mapW, mapH);
            // Pitch band shown: full goal width (+margin) across, 18m out from the line deep.
            float halfShown = SimConfig.GoalWidth * 0.5f + 3f;
            float depthShown = 18f;

            GUI.Box(mapRect, GUIContent.none);
            var grass = new GUIStyle(GUI.skin.box); // just use box; draw markers as small boxes
            // Goal (thin bar along the top edge).
            float goalPxHalf = (SimConfig.GoalWidth * 0.5f / halfShown) * (mapW * 0.5f);
            GUI.Box(new Rect(mapRect.center.x - goalPxHalf, mapRect.y + 2f, goalPxHalf * 2f, 6f), GUIContent.none);

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
            var prev = GUI.color; GUI.color = new Color(1f, 0.85f, 0.2f);
            GUI.Box(new Rect(mapRect.x + mfx * mapW - 5f, mapRect.y + mfy * mapH - 5f, 10f, 10f), GUIContent.none);
            GUI.color = prev;

            row += mapH + 12f;
        }

        bool Toggle(float lx, ref float row, float lw, string label, bool val)
        {
            var st = new GUIStyle(GUI.skin.toggle) { fontSize = 15, normal = { textColor = Color.white }, onNormal = { textColor = Color.white } };
            val = GUI.Toggle(new Rect(lx, row + 6f, lw, 26f), val, "  " + label, st);
            row += RowH;
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
            _delivery = SimConfig.Delivery.AutoCross; _aimTarget = SimConfig.ServeTarget;
        }
    }
}
