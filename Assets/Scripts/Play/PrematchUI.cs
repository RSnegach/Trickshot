using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Pre-match settings screen shown after a mode is picked. Every slider is a
    /// NORMALIZED multiplier that defaults to 1.00 (range 0-2), applied to a base value
    /// on Start. Start launches the match; Back returns to the main menu.
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

        // All normalized to 1.00 by default.
        float _goalWidth = 1f, _goalHeight = 1f, _ballSpeed = 1f;
        float _crossInterval = 1f, _keeperAbility = 1f, _strikerSpeed = 1f;   // striker
        float _shotDifficulty = 1f, _keeperSpeed = 1f, _keeperJump = 1f;      // keeper

        // Challenge-mode settings (raw values, not multipliers, so they read naturally).
        float _timeTrialSeconds = 60f;      // Time Trial round length
        float _accuracySeconds = 90f;       // Accuracy round length
        float _accuracyTargets = 4f;        // Accuracy targets up at once
        bool  _penaltyMode = false;         // Free Kick: penalty spot + no wall
        float _freeKickDistance = 20f;      // Free Kick: distance out
        float _wallCount = 4f;              // Free Kick: defenders in the wall
        float _wallDistance = 9.15f;        // Free Kick: wall distance
        float _wallOffset = 0f;             // Free Kick: wall lateral shift (-5..5)

        bool IsChallenge => _mode == GameMode.Freeplay || _mode == GameMode.TimeTrial
                            || _mode == GameMode.Accuracy || _mode == GameMode.FreeKick;

        public void Init(GameMode mode, System.Action<GameMode> onStart, System.Action onBack)
        {
            _mode = mode;
            _onStart = onStart;
            _onBack = onBack;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnGUI()
        {
            float panelW = 460f, panelH = 560f;
            float x = Screen.width * 0.5f - panelW * 0.5f;
            float y = Screen.height * 0.5f - panelH * 0.5f;
            GUI.Box(new Rect(x, y, panelW, panelH), GUIContent.none);

            var title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x + 30f, y + 12f, panelW - 200f, 40f), _mode.ToString().ToUpper() + " - SETUP", title);

            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            if (GUI.Button(new Rect(x + panelW - 160f, y + 16f, 130f, 30f), "Reset All", smallBtn))
                ResetAll();

            float row = y + 66f;
            float lx = x + 30f, lw = panelW - 60f;

            // All sliders: normalized multiplier, default 1.00, range 0-2, with a reset.
            _goalWidth  = Slider(lx, ref row, lw, "Goal width",   _goalWidth);
            _goalHeight = Slider(lx, ref row, lw, "Goal height",  _goalHeight);
            _ballSpeed  = Slider(lx, ref row, lw, "Ball velocity", _ballSpeed);

            if (_mode == GameMode.Striker)
            {
                _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval);
                _keeperAbility = Slider(lx, ref row, lw, "Keeper ability", _keeperAbility);
                _strikerSpeed  = Slider(lx, ref row, lw, "Striker speed",  _strikerSpeed);
            }
            else if (_mode == GameMode.Goalkeeper)
            {
                _shotDifficulty = Slider(lx, ref row, lw, "Shot difficulty", _shotDifficulty);
                _keeperSpeed    = Slider(lx, ref row, lw, "Keeper speed",    _keeperSpeed);
                _keeperJump     = Slider(lx, ref row, lw, "Keeper jump height", _keeperJump);
            }
            else
            {
                // Challenge modes reuse the striker player, so they share the striker
                // speed slider, plus their own mode-specific settings (raw values).
                _strikerSpeed = Slider(lx, ref row, lw, "Striker speed", _strikerSpeed);
                if (_mode == GameMode.Freeplay || _mode == GameMode.TimeTrial || _mode == GameMode.Accuracy)
                    _crossInterval = Slider(lx, ref row, lw, "Cross interval", _crossInterval);

                if (_mode == GameMode.TimeTrial)
                    _timeTrialSeconds = RawSlider(lx, ref row, lw, "Round time (s)", _timeTrialSeconds, 30f, 180f, "0");
                else if (_mode == GameMode.Accuracy)
                {
                    _accuracySeconds = RawSlider(lx, ref row, lw, "Round time (s)", _accuracySeconds, 30f, 180f, "0");
                    _accuracyTargets = RawSlider(lx, ref row, lw, "Targets up", _accuracyTargets, 1f, 8f, "0");
                }
                else if (_mode == GameMode.FreeKick)
                {
                    _penaltyMode = GUI.Toggle(new Rect(lx, row, lw, 24f), _penaltyMode, "  Penalty mode (spot, no wall)");
                    row += 34f;
                    if (!_penaltyMode)
                    {
                        _freeKickDistance = RawSlider(lx, ref row, lw, "Free kick distance (m)", _freeKickDistance, 11f, 35f, "0");
                        _wallCount    = RawSlider(lx, ref row, lw, "Wall players", _wallCount, 0f, 6f, "0");
                        _wallDistance = RawSlider(lx, ref row, lw, "Wall distance (m)", _wallDistance, 5f, 12f, "0.0");
                        _wallOffset   = RawSlider(lx, ref row, lw, "Wall offset (m)", _wallOffset, -5f, 5f, "0.0");
                    }
                }
            }

            var btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            float by = y + panelH - 70f;
            if (GUI.Button(new Rect(x + 30f, by, 180f, 50f), "Back", btn)) { enabled = false; _onBack?.Invoke(); }
            if (GUI.Button(new Rect(x + panelW - 210f, by, 180f, 50f), "Start", btn)) { Apply(); enabled = false; _onStart?.Invoke(_mode); }
        }

        // Map the 1.00-normalized multipliers onto the actual SimConfig values.
        void Apply()
        {
            SimConfig.GoalWidth  = BaseGoalWidth  * _goalWidth;
            SimConfig.GoalHeight = BaseGoalHeight * _goalHeight;
            SimConfig.BallSpeedMul = _ballSpeed;                 // already a multiplier

            if (_mode == GameMode.Striker)
            {
                SimConfig.ServeInterval    = BaseServeInterval * _crossInterval;
                SimConfig.KeeperAbility    = _keeperAbility;     // 0..2 -> clamped where used
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed * _strikerSpeed;
            }
            else if (_mode == GameMode.Goalkeeper)
            {
                SimConfig.ShotDifficulty    = _shotDifficulty;   // 0..2 -> clamped where used
                SimConfig.KeeperStrafeSpeed = BaseKeeperSpeed * _keeperSpeed;
                SimConfig.KeeperJumpVel     = BaseKeeperJump * _keeperJump;
            }
            else
            {
                // Challenge modes: striker player + their own settings.
                SimConfig.StrikerMoveSpeed = BaseStrikerSpeed * _strikerSpeed;
                SimConfig.ServeInterval    = BaseServeInterval * _crossInterval;
                SimConfig.TimeTrialSeconds = _timeTrialSeconds;
                SimConfig.AccuracySeconds  = _accuracySeconds;
                SimConfig.AccuracyTargetCount = Mathf.RoundToInt(_accuracyTargets);
                SimConfig.PenaltyMode      = _penaltyMode;
                SimConfig.FreeKickDistance = _freeKickDistance;
                SimConfig.WallCount        = Mathf.RoundToInt(_wallCount);
                SimConfig.WallDistance     = _wallDistance;
                SimConfig.WallLateralOffset = _wallOffset;
                // Free Kick / Penalty has no keeper ability slider; give it a competent keeper.
                if (_mode == GameMode.FreeKick && SimConfig.KeeperAbility < 0.01f)
                    SimConfig.KeeperAbility = 0.7f;
            }
        }

        // Normalized slider (0..2, default 1.00) with a per-row reset-to-1.0 button.
        float Slider(float lx, ref float row, float lw, string label, float val)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            var smallBtn = new GUIStyle(GUI.skin.button) { fontSize = 12 };

            GUI.Label(new Rect(lx, row, lw, 20f), $"{label}:  {val:0.00}x", st);
            row += 22f;

            float resetW = 54f, gap = 10f;
            float sliderW = lw - resetW - gap;
            val = GUI.HorizontalSlider(new Rect(lx, row + 4f, sliderW, 20f), val, 0f, 2f);
            if (GUI.Button(new Rect(lx + sliderW + gap, row, resetW, 24f), "1.0x", smallBtn))
                val = 1f;
            row += 40f;
            return val;
        }

        // Raw-value slider (min..max, shown with the given numeric format). No reset button.
        float RawSlider(float lx, ref float row, float lw, string label, float val,
                        float min, float max, string fmt)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUI.Label(new Rect(lx, row, lw, 20f), $"{label}:  {val.ToString(fmt)}", st);
            row += 22f;
            val = GUI.HorizontalSlider(new Rect(lx, row + 4f, lw, 20f), val, min, max);
            row += 40f;
            return val;
        }

        void ResetAll()
        {
            _goalWidth = _goalHeight = _ballSpeed = 1f;
            _crossInterval = _keeperAbility = _strikerSpeed = 1f;
            _shotDifficulty = _keeperSpeed = _keeperJump = 1f;
            _timeTrialSeconds = 60f; _accuracySeconds = 90f; _accuracyTargets = 4f;
            _penaltyMode = false; _freeKickDistance = 20f;
            _wallCount = 4f; _wallDistance = 9.15f; _wallOffset = 0f;
        }
    }
}
