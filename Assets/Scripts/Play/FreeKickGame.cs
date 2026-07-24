using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// FREE KICK / PENALTY challenge mode driver.
    ///
    /// A stationary dead ball is placed in front of goal. The player (the existing
    /// active-ragdoll striker) runs up and strikes it. In free-kick mode a configurable
    /// defensive WALL of blockers stands between the ball and goal; an AI keeper guards
    /// the line. In penalty mode there is no wall.
    ///
    /// Loop per attempt:
    ///   Armed    - the ball sits dead on the spot, striker positioned behind it. When
    ///              the player kicks it (ball speed jumps) the attempt goes live and the
    ///              wall hops.
    ///   Live     - watch the ball: GOAL if it fully crosses into the goal mouth (same
    ///              test KeeperGame/GameManager use), SAVE if the keeper touched it,
    ///              BLOCKED if a wall blocker touched it, else MISS. Resolves when the
    ///              ball comes to rest, leaves play, or a safety timeout elapses.
    ///   Cooldown - brief callout, then reset the ball to the spot, reposition the
    ///              striker, and re-arm.
    ///
    /// The striker, keeper, camera, and ball are built by GameBootstrap and handed in via
    /// Configure. The wall is built here (skipped in penalty mode). Respects PauseMenu.
    /// </summary>
    public class FreeKickGame : MonoBehaviour
    {
        GameInput _input;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        ActiveRagdoll _keeperRagdoll;
        DefensiveWall _wall;
        GameCamera _cam;

        // The set-piece taker: AI aesthetic runup + swing, player controls only the power meter
        // (Space) + WASD spin. It launches the ball by code, so the striker never physically kicks.
        readonly SetPieceTaker _taker = new SetPieceTaker();

        enum Phase { Armed, Live, Cooldown }
        Phase _phase;

        float _liveTime, _restTimer, _cooldown;
        bool _keeperTouched, _wallTouched;

        int _attempts, _goals;
        string _flash = ""; float _flashTime;

        float _goalLineZ;
        Vector3 _ballSpot;      // dead-ball spot
        Vector3 _strikerBase;   // striker feet position behind the ball
        bool _wallActive;       // false in penalty mode

        // Regulation-ish penalty distance (not a pre-match field; free kicks use
        // SimConfig.FreeKickDistance instead).
        const float PenaltyDistance = 11f;
        const float RunUp        = 3f;     // striker starts this far behind the ball
        const float KickSpeed    = 2.5f;   // ball speed that marks the kick as taken
        const float RestSpeed    = 0.7f;   // ball considered stopped below this
        const float RestHold     = 0.6f;   // seconds at rest before resolving
        const float MaxLiveTime  = 6f;     // safety cap so an attempt always resolves
        const float ResetDelay   = 1.4f;   // callout time before re-arming

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
            _goalLineZ = SimConfig.GoalCenter.z;

            // Dead-ball spot: centred, in front of goal. Penalty is closer (11 m); a free
            // kick is further out (SimConfig.FreeKickDistance). Ball rests on the ground.
            float dist = SimConfig.PenaltyMode ? PenaltyDistance : SimConfig.FreeKickDistance;
            _ballSpot = new Vector3(0f, SimConfig.BallRadius, SimConfig.GoalCenter.z - dist);
            _strikerBase = new Vector3(_ballSpot.x, 0f, _ballSpot.z - RunUp);
            _wallActive = !SimConfig.PenaltyMode;

            // Set pieces get the arcadey loft + curl and stat-scaled (near-zero default) assist.
            _ball.SetPieceShot = true;

            // Camera + striker turn axis: same wiring as striker mode (mouse orbits and
            // sets the striker's facing yaw).
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            _striker.SetCameraYaw(() => _cam.Yaw);
            _cam.SetMode(GameCamera.Mode.Follow);

            if (_wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, SimConfig.WallCount, SimConfig.WallDistance, SimConfig.WallLateralOffset);

            Arm();
        }

        /// <summary>Wired by GameBootstrap from the striker's KickDetectors (optional).</summary>
        public void NotifyValidTrick() => Flash("TRICK CONNECT!");

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu

            if (_input.ResetPressed) { FullReset(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            // The taker owns the striker body during a set piece (aesthetic runup + swing), so the
            // player's Striker locomotion is NOT ticked here. Only the taker drives the ragdoll.
            _taker.Tick();
            if (_keeper != null) _keeper.Tick();
            if (_wall != null) _wall.Tick();

            switch (_phase)
            {
                case Phase.Armed:    TickArmed();    break;
                case Phase.Live:     TickLive();     break;
                case Phase.Cooldown: TickCooldown(); break;
            }

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
                _keeperTouched = false;
                _wallTouched = false;
                if (_wall != null) _wall.TriggerJump();
                Flash("STRIKE!");
            }
        }

        // Watch the struck ball for the outcome, then hand off to the cooldown callout.
        void TickLive()
        {
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            if (!_keeperTouched && KeeperContactedBall()) _keeperTouched = true;
            if (!_wallTouched && WallContactedBall()) _wallTouched = true;

            if (BallFullyInGoal(c)) { Resolve(Outcome.Goal); return; }

            if (_ball.Speed < RestSpeed) _restTimer += Time.deltaTime; else _restTimer = 0f;

            float halfGoal = SimConfig.GoalWidth * 0.5f;
            bool behindGoal = c.z > _goalLineZ + 0.6f
                              && (Mathf.Abs(c.x) > halfGoal || c.y > SimConfig.GoalHeight);
            bool outOfPlay = c.y < -3f
                             || Mathf.Abs(c.x) > SimConfig.FieldWidth
                             || Mathf.Abs(c.z) > SimConfig.FieldLength
                             || behindGoal;
            bool dead = _restTimer > RestHold || _liveTime > MaxLiveTime;

            if (outOfPlay || dead)
            {
                if (_keeperTouched
                    || (_keeper != null && Vector3.Distance(c, _keeper.PelvisPos) < 2.4f))
                    Resolve(Outcome.Save);
                else if (_wallTouched)
                    Resolve(Outcome.Blocked);
                else
                    Resolve(Outcome.Miss);
            }
        }

        void TickCooldown()
        {
            _cooldown -= Time.deltaTime;
            if (_cooldown <= 0f) Arm();
        }

        enum Outcome { Goal, Save, Blocked, Miss }

        void Resolve(Outcome o)
        {
            switch (o)
            {
                case Outcome.Goal:    _goals++; Flash("GOAL!"); AudioManager.Instance?.OnSetPieceGoal(0); break;
                case Outcome.Save:    Flash("SAVE!");    AudioManager.Instance?.OnSetPieceMiss(0); break;
                case Outcome.Blocked: Flash("BLOCKED!"); AudioManager.Instance?.OnSetPieceMiss(0); break;
                default:              Flash("MISS");     AudioManager.Instance?.OnSetPieceMiss(0); break;
            }
            _phase = Phase.Cooldown;
            _cooldown = ResetDelay;
        }

        // Re-arm: dead ball back on the spot, striker behind it, keeper home, wall grounded, and
        // the taker armed to read the power meter + WASD spin for this attempt.
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
            AudioManager.Instance?.PlayWhistle();   // whistle as the shooter is set behind the ball (first arm + every reset)
        }

        // R: rebuild the wall from current settings and re-arm.
        void FullReset()
        {
            if (_wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, SimConfig.WallCount, SimConfig.WallDistance, SimConfig.WallLateralOffset);
            else if (_wall != null)
                _wall.Clear();
            Arm();
            Flash("RESET");
        }

        // A goal the instant the WHOLE ball is over the line and inside the frame - the
        // same per-frame state test KeeperGame / GameManager use.
        bool BallFullyInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius;
            float halfW = SimConfig.GoalWidth * 0.5f;
            return c.z - r >= _goalLineZ
                   && c.z <= _goalLineZ + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r
                   && c.y >= r
                   && c.y <= SimConfig.GoalHeight - r;
        }

        bool KeeperContactedBall()
        {
            if (_keeperRagdoll == null) return false;
            Vector3 bp = _ball.transform.position;
            foreach (var t in _keeperRagdoll.BoneTransforms)
                if (t != null && Vector3.Distance(t.position, bp) < SimConfig.BallRadius + 0.28f)
                    return true;
            return false;
        }

        bool WallContactedBall()
        {
            if (_wall == null) return false;
            Vector3 bp = _ball.transform.position;
            var blockers = _wall.Blockers;
            for (int i = 0; i < blockers.Count; i++)
            {
                var go = blockers[i];
                if (go == null) continue;
                var col = go.GetComponent<Collider>();
                if (col == null) continue;
                if (Vector3.Distance(col.ClosestPoint(bp), bp) < SimConfig.BallRadius + 0.05f)
                    return true;
            }
            return false;
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            float dist = SimConfig.PenaltyMode ? PenaltyDistance : SimConfig.FreeKickDistance;
            int scorePct = _attempts > 0 ? Mathf.RoundToInt(100f * _goals / _attempts) : 0;
            var p = Hud.PanelStart(SimConfig.PenaltyMode ? "PENALTIES" : "FREE KICK", 4);
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "Attempts", _attempts.ToString());
            Hud.Stat(ref p, "Scored %", scorePct + "%");
            Hud.Stat(ref p, "Distance", $"{dist:0.0} m");

            Hud.Legend(SimConfig.PenaltyMode
                ? "HOLD Space power   Mouse aim   WASD spin   TAP Space dribble   V ball cam   R reset"
                : $"Wall {SimConfig.WallCount} @ {SimConfig.WallDistance:0.0}m    HOLD Space power   Mouse aim   WASD spin   TAP Space dribble   V ball cam   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);

            DrawPowerMeter();
        }

        // Centered power meter shown while charging: a green -> yellow -> red bar that reflects the
        // oscillating taker meter. Release (handled by the taker) commits at the shown level.
        void DrawPowerMeter()
        {
            if (!_taker.IsCharging) return;
            float w = 320f, h = 22f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height - 90f;

            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.85f);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), Texture2D.whiteTexture);

            float f = Mathf.Clamp01(_taker.Meter);
            // green (low) -> yellow (mid) -> red (high).
            Color fill = f < 0.5f ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(0.95f, 0.85f, 0.2f), f * 2f)
                                  : Color.Lerp(new Color(0.95f, 0.85f, 0.2f), new Color(0.9f, 0.2f, 0.16f), (f - 0.5f) * 2f);
            GUI.color = new Color(0.14f, 0.15f, 0.19f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, w * f, h), Texture2D.whiteTexture);
            GUI.color = prev;

            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, y - 20f, w, 18f), "POWER  (release to shoot)", lbl);
        }
    }
}
