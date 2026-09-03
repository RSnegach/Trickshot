using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// "Match Setup" as an overlay INSIDE the pause menu, so a running match can be re-tuned
    /// without being torn down and rebuilt. Same shape as SettingsMenu: a plain class the PauseMenu
    /// owns and swaps in for its own body while open (see PauseMenu._setupOpen).
    ///
    /// STRIKER: the goal window (GoalEditor) beside a small panel - the goal's size, dragged like a
    /// picture, and the AI keeper's difficulty under it. That is the whole of it: nothing else in a
    /// striker match is changed from here. Applies at once (GoalSetup.Apply): goal detection and
    /// the keeper read the statics every frame and Arena rebuilds the goal's frame in place. Hosting,
    /// the values ride the match config to every client.
    ///
    /// ACCURACY has no live settings at all (see RowsFor), so its Match Setup entry goes straight
    /// back to the full pre-match flow - the Practice/Challenge fork - rather than opening a card.
    ///
    /// THE REMAINING MODES keep their live-safe sliders - SimConfig statics the running sim re-reads
    /// every frame (or every serve/shot), so moving one takes effect immediately with nothing to
    /// rebuild:
    ///
    ///   Shot speed        BallSpeedMul       -> BallController.LaunchTo, per launch
    ///   Keeper ability    KeeperAbility      -> Goalkeeper, per decision
    ///   Striker speed     StrikerMoveSpeed   -> Striker.Tick, per frame
    ///   Keeper speed      KeeperStrafeSpeed  -> KeeperController.Move, per frame
    ///   Keeper jump       KeeperJumpVel      -> KeeperController.Jump, per jump
    ///   Shot difficulty   ShotDifficulty     -> ShotServer, per shot
    ///
    /// What is deliberately NOT here: Match's team-size/position/AI pickers, which are baked into
    /// the pitch when the arena is BUILT. They stay on the full-screen pre-match screen, which
    /// rebuilds the match around them; the "Full Setup" button here is the way back to it.
    /// </summary>
    public class PauseMatchSetup
    {
        readonly GameMode _mode;
        readonly GoalEditor _goal = new GoalEditor();

        public PauseMatchSetup(GameMode mode) { _mode = mode; }

        /// <summary>Does this mode have anything live-tunable at all? A mode with no rows would
        /// otherwise open an empty card, so PauseMenu hides the entry instead.</summary>
        public static bool HasLiveSettings(GameMode mode) => mode == GameMode.Striker || RowsFor(mode) > 0;

        // Row count per mode, so the card is sized to its content (same idea as PrematchUI.RowCount).
        // 0 means "nothing live-tunable": HasLiveSettings then reports false and PauseMenu sends
        // Match Setup straight to the full pre-match screen instead of opening an empty card.
        static int RowsFor(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Striker:    return 0;   // the goal window instead (see DrawStriker)
                case GameMode.Goalkeeper: return 4;   // shot speed, shot difficulty, keeper speed, keeper jump
                case GameMode.Match:      return 0;   // team size / position / AI level all need a rebuild
                // Accuracy has NO live-tunable settings, and the three generic ones this used to
                // offer were all wrong for it: shot speed and striker speed are not part of the mode
                // (a dead-ball taker with a fixed run-up), and its keeper ability is owned by the
                // round ladder in Challenge and by the goal picture in Practice - so a slider here
                // would be overwritten on the next round, or would silently disagree with the
                // picker that set it. Its real settings are the Practice/Challenge screens.
                case GameMode.Accuracy:   return 0;
                default:                  return 3;   // shot speed, striker speed, keeper ability
            }
        }

        const float RowH = 52f, HeadH = 72f, FootH = 64f, PanelW = 460f;

        /// <summary>
        /// Draw the panel. `onClose` backs out to the pause buttons; `onFullSetup` is the escape
        /// hatch to the old full-screen pre-match screen (which tears the match down and rebuilds
        /// it), for the settings that cannot apply live. `onFullSetup` may be null (networked
        /// matches have no rebuild path).
        /// </summary>
        public void Draw(System.Action onClose, System.Action onFullSetup)
        {
            if (_mode == GameMode.Striker) { DrawStriker(onClose, onFullSetup); return; }

            int rows = RowsFor(_mode);
            float h = HeadH + rows * RowH + FootH;
            float x = MenuScale.Width * 0.5f - PanelW * 0.5f;
            float y = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, PanelW + 260f);
            UITheme.Panel(new Rect(x, y, PanelW, h), UITheme.Gold);

            var title = _titleSt ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 24f, y + 14f, PanelW - 48f, 34f), "MATCH SETUP", title, UITheme.Ink, 0.7f, 2f);
            UITheme.Hint(new Rect(x + 24f, y + 44f, PanelW - 48f, 20f), "Applies immediately - no restart.");
            UITheme.Divider(x + 24f, y + HeadH - 10f, PanelW - 48f);

            float row = y + HeadH;
            float lx = x + 28f, lw = PanelW - 56f;

            if (_mode == GameMode.Goalkeeper)
            {
                SimConfig.BallSpeedMul  = Slider(lx, ref row, lw, "Shot speed", SimConfig.BallSpeedMul, 0.5f, 2f, "0.00");
                SimConfig.ShotDifficulty = Slider(lx, ref row, lw, "Shot difficulty", SimConfig.ShotDifficulty, 0f, 1f, "0.00");
                Speed(lx, ref row, lw, "Keeper speed", ref SimConfig.KeeperStrafeSpeed, SimConfig.KeeperStrafeSpeedBase, 0.5f, 1.8f);
                Speed(lx, ref row, lw, "Keeper jump height", ref SimConfig.KeeperJumpVel, SimConfig.KeeperJumpVelBase, 0.6f, 1.6f);
            }
            else if (_mode != GameMode.Match)
            {
                SimConfig.BallSpeedMul = Slider(lx, ref row, lw, "Shot speed", SimConfig.BallSpeedMul, 0.5f, 2f, "0.00");
                Speed(lx, ref row, lw, "Striker speed", ref SimConfig.StrikerMoveSpeed, SimConfig.StrikerMoveSpeedBase, 0.5f, 1.8f);
                SimConfig.KeeperAbility = Slider(lx, ref row, lw, "Keeper", SimConfig.KeeperAbility, 0f, 1f, "0.00");
            }

            DrawNav(x, y, PanelW, h, onClose, onFullSetup);
        }

        // Striker: a small panel (title, hint, buttons) with the goal window to its right. The goal
        // window is the only control; it is the same widget the host used on the stadium screen.
        void DrawStriker(System.Action onClose, System.Action onFullSetup)
        {
            const float leftW = 300f, h = GoalEditor.PanelH;
            float total = leftW + 16f + GoalEditor.PanelW;
            float x = MenuScale.Width * 0.5f - total * 0.5f;
            float y = MenuScale.Height * 0.5f - h * 0.5f;

            UITheme.Scrim(MenuScale.Width, MenuScale.Height, 0.42f, total + 260f);
            UITheme.Panel(new Rect(x, y, leftW, h), UITheme.Gold);

            var title = _titleSt ??= new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = UITheme.Ink } };
            UITheme.Shadowed(new Rect(x + 24f, y + 14f, leftW - 48f, 34f), "MATCH SETUP", title, UITheme.Ink, 0.7f, 2f);
            UITheme.Divider(x + 24f, y + 58f, leftW - 48f);
            var hint = _hintSt ??= new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = UITheme.Dim } };
            UITheme.Label(new Rect(x + 24f, y + 70f, leftW - 48f, 120f),
                          "Drag the goal's sides or corners to resize it. The goal line stays put.\n\n"
                          + "Pick the goalkeeper's difficulty under it.\n\n"
                          + "Applies immediately - no restart.", hint);

            // The live values, edited in place. Any change is applied at once.
            float w = SimConfig.GoalWidth, hgt = SimConfig.GoalHeight;
            int lvl = GoalSetup.KeeperLevel;
            if (_goal.Draw(new Rect(x + leftW + 16f, y, GoalEditor.PanelW, h), ref w, ref hgt, ref lvl))
                GoalSetup.Apply(w, hgt, lvl);

            DrawNav(x, y, leftW, h, onClose, onFullSetup);
        }

        // Cached: this panel draws inside the pause menu over a networked match that keeps running.
        static GUIStyle _titleSt, _hintSt, _navBtnSt;

        void DrawNav(float x, float y, float w, float h, System.Action onClose, System.Action onFullSetup)
        {
            var btn = _navBtnSt ??= new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            float by = y + h - 48f;
            // Full Setup rebuilds the match, so it carries the settings this panel cannot (Match's
            // team/position pickers, and the pre-match sliders). Absent in a networked match (no rebuild).
            if (onFullSetup != null && UITheme.Button(new Rect(x + 24f, by, 130f, 34f), "Full Setup...", btn))
            { onClose?.Invoke(); onFullSetup.Invoke(); return; }
            if (UITheme.Button(new Rect(x + w - 24f - 110f, by, 110f, 34f), "Back", btn)) onClose?.Invoke();
        }

        // A 0..1-style raw slider row writing straight to its static.
        static float Slider(float lx, ref float row, float lw, string label, float val,
                            float min, float max, string fmt)
        {
            UITheme.Label(new Rect(lx, row, lw, 20f), label, Hud.RowName);
            UITheme.Label(new Rect(lx, row, lw, 20f), val.ToString(fmt), Hud.RowValue);
            val = GUI.HorizontalSlider(new Rect(lx, row + 24f, lw, 20f), val, min, max);
            row += RowH;
            return val;
        }

        // A MULTIPLIER row over a static that stores an absolute value: shown and edited as a
        // multiple of its base so the label reads like the pre-match screen's ("1.20x"), while the
        // static keeps the absolute number the sim wants.
        static void Speed(float lx, ref float row, float lw, string label, ref float target,
                          float base_, float min, float max)
        {
            float mul = base_ > 0.0001f ? target / base_ : 1f;
            mul = Mathf.Clamp(mul, min, max);
            UITheme.Label(new Rect(lx, row, lw, 20f), label, Hud.RowName);
            UITheme.Label(new Rect(lx, row, lw, 20f), mul.ToString("0.00") + "x", Hud.RowValue);
            mul = GUI.HorizontalSlider(new Rect(lx, row + 24f, lw, 20f), mul, min, max);
            target = base_ * mul;
            row += RowH;
        }
    }
}
