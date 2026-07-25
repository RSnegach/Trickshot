using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// ACCURACY challenge mode: a FREE-KICK shooting gallery. The player takes free kick after
    /// free kick from a dead ball (the same SetPieceTaker mechanic as free-kick mode: HOLD Space
    /// for the power meter, WASD for spin, mouse to aim) at coloured targets popped up across the
    /// goal mouth. Hitting a target scores its points and pops a fresh one elsewhere, so there are
    /// always AccuracyTargetCount targets up.
    ///
    /// The round is timed (SimConfig.AccuracySeconds) and the ball re-arms after every attempt
    /// until the clock runs out; a FINISHED banner then shows the score, and the best score of the
    /// SESSION is kept (SessionBest) so repeat rounds have something to beat. R restarts.
    ///
    /// The wall and the keeper are OPTIONAL obstacles configured in the pre-match menu: wall
    /// players 0 = no wall, keeper ability 0 = no keeper (an open goal is pure target practice).
    /// Audio follows the free-kick rules: a whistle as the shooter is set, and the set-piece
    /// goal/miss streak reactions (cheer + applause, boos on repeat misses).
    /// </summary>
    public class AccuracyGame : MonoBehaviour
    {
        GameInput _input;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        ActiveRagdoll _keeperRagdoll;
        DefensiveWall _wall;
        GameCamera _cam;

        // The set-piece taker: AI aesthetic runup + swing; the player controls the power meter
        // (Space) + WASD spin + mouse aim. It launches the ball by code.
        readonly SetPieceTaker _taker = new SetPieceTaker();
        readonly AccuracyBoard _board = new AccuracyBoard();

        /// <summary>Best score achieved this session (survives round restarts, not an app relaunch).</summary>
        public static int SessionBest;

        enum Phase { Armed, Live, Cooldown }
        Phase _phase;

        float _liveTime, _restTimer, _cooldown;
        bool _hitThisKick;      // did this attempt score a target? (drives the goal/miss audio)

        int _score, _attempts;
        float _timeLeft;
        bool _finished;

        string _flash = "";
        float _flashTime;

        Vector3 _ballSpot;      // dead-ball spot
        Vector3 _wallCenter;
        Vector3 _strikerBase;   // striker feet position behind the ball (run-up start)
        bool _wallActive;

        const float RunUp       = 3f;     // striker starts this far behind the ball
        const float KickSpeed   = 2.5f;   // ball speed that marks the kick as taken
        const float RestSpeed   = 0.7f;   // ball considered stopped below this
        const float RestHold    = 0.5f;   // seconds at rest before resolving
        const float MaxLiveTime = 5f;     // safety cap so an attempt always resolves
        const float ResetDelay  = 1.0f;   // callout time before re-arming (snappier than free kicks)

        public void Configure(GameInput input, BallController ball, Striker striker, ActiveRagdoll strikerRagdoll,
                              Goalkeeper keeper, ActiveRagdoll keeperRagdoll, DefensiveWall wall, GameCamera cam)
        {
            _input = input;
            _ball = ball;
            _striker = striker;
            _strikerRagdoll = strikerRagdoll;
            _keeper = keeper;
            _keeperRagdoll = keeperRagdoll;
            _wall = wall;
            _cam = cam;

            // Dead-ball spot: centred, FreeKickDistance out from goal, resting on the ground.
            _ballSpot = new Vector3(0f, SimConfig.BallRadius, SimConfig.GoalCenter.z - SimConfig.FreeKickDistance);
            // Wall centre: WallDistance along the ball->goal line, shifted sideways by the
            // pre-match wall offset (the wallCenter Build overload takes an explicit point, so the
            // offset has to be baked in here).
            Vector3 toGoalFlat = SimConfig.GoalCenter - _ballSpot; toGoalFlat.y = 0f;
            Vector3 wallDir = toGoalFlat.sqrMagnitude > 1e-4f ? toGoalFlat.normalized : Vector3.forward;
            Vector3 wallSide = Vector3.Cross(Vector3.up, wallDir);
            _wallCenter = _ballSpot + wallDir * SimConfig.WallDistance
                                    + wallSide * SimConfig.WallLateralOffset;
            RecomputeStrikerBase();
            _wallActive = SimConfig.WallCount > 0;   // 0 wall players = no wall at all

            // Set pieces get the arcadey loft + curl and stat-scaled assist.
            _ball.SetPieceShot = true;

            // Camera + striker turn axis: same wiring as striker/free-kick mode.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            _striker.SetCameraYaw(() => _cam.Yaw);
            _cam.SetMode(GameCamera.Mode.Follow);

            if (_wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, _wallCenter, SimConfig.WallCount);

            _board.Scored += OnTargetScored;
            _board.Build(transform, Mathf.Max(1, SimConfig.AccuracyTargetCount),
                         (uint)System.Environment.TickCount | 1u);

            BeginRound();
        }

        void BeginRound()
        {
            _score = 0;
            _attempts = 0;
            _timeLeft = SimConfig.AccuracySeconds;
            _finished = false;
            _flash = "";
            _flashTime = 0f;
            _board.SpawnAll();
            Arm();
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;

            if (_input.ResetPressed) { BeginRound(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            if (_finished)
            {
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // The taker owns the striker body during a set piece, so the player's Striker
            // locomotion is NOT ticked here - only the taker drives the ragdoll.
            _taker.Tick();
            if (_keeper != null) _keeper.Tick();
            if (_wall != null) _wall.Tick();
            _board.Tick(Time.deltaTime);

            switch (_phase)
            {
                case Phase.Armed:    TickArmed();    break;
                case Phase.Live:     TickLive();     break;
                case Phase.Cooldown: TickCooldown(); break;
            }

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f) EndRound();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Dead ball waiting to be struck: the kick is detected by the ball picking up pace.
        void TickArmed()
        {
            if (_ball.Speed > KickSpeed)
            {
                _phase = Phase.Live;
                _attempts++;
                _liveTime = 0f;
                _restTimer = 0f;
                _hitThisKick = false;
                if (_wall != null) _wall.TriggerJump();
            }
        }

        // Watch the struck ball until it stops / leaves play, then re-arm for the next kick.
        void TickLive()
        {
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            if (_ball.Speed < RestSpeed) _restTimer += Time.deltaTime; else _restTimer = 0f;

            bool outOfPlay = c.y < -3f
                             || Mathf.Abs(c.x) > SimConfig.FieldWidth
                             || Mathf.Abs(c.z) > SimConfig.FieldLength;
            bool dead = _restTimer > RestHold || _liveTime > MaxLiveTime;

            if (outOfPlay || dead)
            {
                // Free-kick audio rules: a scored target counts as the "goal" reaction, anything
                // else is a miss (so repeat misses boo, and hit streaks build the crowd swell).
                if (_hitThisKick) AudioManager.Instance?.OnSetPieceGoal(0);
                else { Flash("MISS"); AudioManager.Instance?.OnSetPieceMiss(0); }
                _phase = Phase.Cooldown;
                _cooldown = ResetDelay;
            }
        }

        void TickCooldown()
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown <= 0f) Arm();
        }

        // Re-arm: dead ball back on the spot, striker behind it, keeper home, wall grounded, and
        // the taker armed to read the power meter + WASD spin + mouse aim for this attempt.
        void Arm()
        {
            _ball.ResetTo(_ballSpot);
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(_strikerBase, Quaternion.identity);   // identity faces +Z (goal)
            if (_keeper != null && _keeperRagdoll != null) _keeper.ResetTo(SimConfig.KeeperStart);
            if (_wall != null) _wall.Ground();
            _taker.Begin(_input, _strikerRagdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                false, -1f,
                () => SetPieceTaker.LookAimPoint(_ballSpot, _cam.Yaw, _cam.Pitch, SimConfig.AttackGoalCenter.z));
            _phase = Phase.Armed;
            AudioManager.Instance?.PlayWhistle();   // shooter set behind the ball (first arm + every reset)
        }

        void RecomputeStrikerBase()
        {
            Vector3 toGoal = SimConfig.GoalCenter - _ballSpot; toGoal.y = 0f;
            Vector3 dir = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : Vector3.forward;
            _strikerBase = new Vector3(_ballSpot.x, 0f, _ballSpot.z) - dir * RunUp;
        }

        // A target was struck: bank the points (the board re-pops it itself).
        void OnTargetScored(int points, int index)
        {
            if (_finished) return;
            _score += points;
            _hitThisKick = true;
            Flash("+" + points);
        }

        void EndRound()
        {
            _timeLeft = 0f;
            _finished = true;
            if (_score > SessionBest) SessionBest = _score;
            _board.HideAll();
            _taker.Reset();
        }

        void Flash(string s) { _flash = s; _flashTime = 1.2f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            var p = Hud.PanelStart("ACCURACY", 4);
            Hud.Stat(ref p, "Score", _score.ToString());
            Hud.Stat(ref p, "Best", SessionBest.ToString());
            Hud.Stat(ref p, "Kicks", _attempts.ToString());
            Hud.Stat(ref p, "Targets up", _board.ActiveCount().ToString());

            Hud.Clock(_timeLeft, urgent: !_finished && _timeLeft <= 10f);
            Hud.Legend("HOLD Space power   Mouse aim   WASD spin   V ball cam   R restart");

            if (_finished)
            {
                Hud.Banner("FINISHED!", "Score: " + _score + "   Best: " + SessionBest, "Press R to play again");
                return;
            }

            Hud.Flash(_flash, _flashTime / 1.2f);
            DrawPowerMeter();
        }

        // Power meter while charging, mirroring the free-kick HUD.
        void DrawPowerMeter()
        {
            if (!_taker.IsCharging) return;
            float w = 260f, h = 18f;
            float x = Screen.width * 0.5f - w * 0.5f, y = Screen.height - 96f;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            float f = Mathf.Clamp01(_taker.Meter);
            GUI.color = f > 0.85f ? new Color(1f, 0.35f, 0.25f) : new Color(0.3f, 0.85f, 0.4f);
            GUI.DrawTexture(new Rect(x + 2f, y + 2f, (w - 4f) * f, h - 4f), Texture2D.whiteTexture);
            GUI.color = prev;
        }
    }
}
