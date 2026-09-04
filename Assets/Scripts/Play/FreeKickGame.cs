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
        bool _wallTouched;
        readonly SaveWatch _save = new SaveWatch();   // shared SAVE / EPIC SAVE / MISS verdict

        int _attempts, _goals;
        string _flash = ""; float _flashTime;

        float _goalLineZ;
        Vector3 _ballSpot;      // dead-ball spot (= where the shooter stands to strike)
        Vector3 _wallCenter;    // wall centre (placed on the map)
        Vector3 _strikerBase;   // striker feet position behind the ball (run-up start)
        bool _wallActive;       // false in penalty mode

        // In-match placement map (M): pick the ball/shooter spot + the wall centre on a top-down
        // map of the attacking third. While open, the taker is not ticked and the camera is frozen.
        bool _mapOpen;
        int _mapEdit;           // 0 = ball, 1 = wall

        // RANDOM SPOTS (pre-match toggle, the same one the multiplayer host has): instead of one
        // placed spot, roll a fresh legal free-kick spot for every attempt. Never used for penalties.
        bool _randomSpots;
        System.Random _rng;

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

            _wallActive = !SimConfig.PenaltyMode;
            _randomSpots = _wallActive && SimConfig.SetPieceRandomSpots;

            // Dead-ball spot + wall. A penalty is the fixed 11 m spot with no wall. A free kick takes
            // the spot and wall PLACED on the pre-match map (the same control the multiplayer host
            // uses); with nothing placed it falls back to the old centred FreeKickDistance derivation.
            // Random spots ignore all of this and roll a fresh spot per attempt (see Arm).
            if (SimConfig.PenaltyMode)
            {
                _ballSpot = new Vector3(0f, SimConfig.BallRadius, SimConfig.GoalCenter.z - PenaltyDistance);
                _wallCenter = _ballSpot + (SimConfig.GoalCenter - _ballSpot).normalized * SimConfig.WallDistance;
            }
            else if (SimConfig.SetPiecePlaced)
            {
                _ballSpot = new Vector3(SimConfig.SetPieceBallSpot.x, SimConfig.BallRadius, SimConfig.SetPieceBallSpot.z);
                _wallCenter = new Vector3(SimConfig.SetPieceWallCenter.x, 0f, SimConfig.SetPieceWallCenter.z);
            }
            else
            {
                _ballSpot = new Vector3(0f, SimConfig.BallRadius, SimConfig.GoalCenter.z - SimConfig.FreeKickDistance);
                _wallCenter = _ballSpot + (SimConfig.GoalCenter - _ballSpot).normalized * SimConfig.WallDistance;
            }
            RecomputeStrikerBase();

            // Set pieces get the arcadey loft + curl and stat-scaled (near-zero default) assist.
            _ball.SetPieceShot = true;

            // Camera + striker turn axis: same wiring as striker mode (mouse orbits and
            // sets the striker's facing yaw).
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look, null, () => _input.CamViewPressed);
            _striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
            _cam.SetMode(GameCamera.Mode.Follow);

            // Random mode rebuilds the wall off the rolled spot in Arm(), so skip the one-off build.
            if (!_randomSpots && _wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, _wallCenter, SimConfig.WallCount);

            Arm();
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Frozen) return;   // no gameplay/input behind a FREEZING pause menu

            // Placement map (M): pick the ball/shooter spot + wall on a top-down map. While open,
            // free the cursor, freeze the camera, and do NOT tick the taker (so it can't charge or
            // fire under the map). Closing re-applies the placement and re-arms.
            if (_input.CrossMapPressed) SetMapOpen(!_mapOpen);
            if (_mapOpen)
            {
                if (_keeper != null) _keeper.Tick();
                if (_wall != null) _wall.Tick();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

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
                CareerStats.RecordFreeKickAttempt();
                _liveTime = 0f;
                _restTimer = 0f;
                _save.Arm();
                _wallTouched = false;
                if (_wall != null) _wall.TriggerJump();
            }
        }

        // Watch the struck ball for the outcome, then hand off to the cooldown callout.
        void TickLive()
        {
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            _save.Poll(_ball, _keeperRagdoll, _keeper != null && _keeper.WasDivingSave);
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
                if (_save.Touched)
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
                case Outcome.Goal:    _goals++; CareerStats.RecordFreeKickGoal(); Flash("GOAL!"); AudioManager.Instance?.OnSetPieceGoal(0); break;
                case Outcome.Save:    Flash(_save.Callout()); AudioManager.Instance?.OnSetPieceMiss(0); break;
                case Outcome.Blocked: AudioManager.Instance?.OnSetPieceMiss(0); break;
                default:              AudioManager.Instance?.OnSetPieceMiss(0); break;
            }
            _phase = Phase.Cooldown;
            _cooldown = ResetDelay;
        }

        // Re-arm: dead ball back on the spot, striker behind it, keeper home, wall grounded, and
        // the taker armed to read the power meter + WASD spin for this attempt.
        void Arm()
        {
            if (_randomSpots) RollRandomSpot();
            _ball.ResetTo(_ballSpot);
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(_strikerBase, Quaternion.identity);   // identity faces +Z (goal)
            // Penalties put him ON the line; a free kick leaves him at his normal open-play depth.
            if (_keeper != null && _keeperRagdoll != null)
                _keeper.ResetTo(SimConfig.PenaltyMode ? SimConfig.KeeperPenaltyStart : SimConfig.KeeperStart);
            if (_wall != null) _wall.Ground();
            _taker.Begin(_input, _strikerRagdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                false, -1f,
                () => SetPieceTaker.LookAimPoint(_ballSpot, _cam.Yaw, _cam.Pitch, SimConfig.AttackGoalCenter.z));
            _phase = Phase.Armed;
            AudioManager.Instance?.PlayWhistle();   // whistle as the shooter is set behind the ball (first arm + every reset)
        }

        // Roll the next random free-kick spot and re-derive everything hanging off it: the run-up
        // start and a regulation wall on the ball->goal line. Same generator (and same geometry) the
        // networked set-piece match uses for its seeded round spots.
        void RollRandomSpot()
        {
            if (_rng == null) _rng = new System.Random();
            _ballSpot = SetPieceMap.RandomSpot(_rng);
            Vector3 toGoal = SimConfig.GoalCenter - _ballSpot; toGoal.y = 0f;
            Vector3 dir = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : Vector3.forward;
            _wallCenter = _ballSpot + dir * SimConfig.WallDistance;
            RecomputeStrikerBase();
            if (_wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, _wallCenter, SimConfig.WallCount);
        }

        // Striker run-up start = RunUp metres behind the ball, along the ball->goal line so he
        // always approaches toward goal regardless of where the ball was placed.
        void RecomputeStrikerBase()
        {
            Vector3 toGoal = SimConfig.GoalCenter - _ballSpot; toGoal.y = 0f;
            Vector3 dir = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : Vector3.forward;
            _strikerBase = new Vector3(_ballSpot.x, 0f, _ballSpot.z) - dir * RunUp;
        }

        // Open/close the placement map: free the cursor + freeze the camera while open, and on
        // close re-derive the ball/striker/wall from the placed markers and re-arm the attempt so
        // the in-world shooter + ball + wall sit EXACTLY where the reticles were placed.
        void SetMapOpen(bool open)
        {
            _mapOpen = open;
            GameInput.CaptureCursor(!open);
            if (_cam != null) _cam.FreezeLook = open;
            if (!open)
            {
                _ballSpot.y = SimConfig.BallRadius;
                RecomputeStrikerBase();
                if (_wallActive && _wall != null)
                    _wall.Build(transform, _ballSpot, _wallCenter, SimConfig.WallCount);
                Arm();
            }
        }

        // R: rebuild the wall from current settings and re-arm.
        void FullReset()
        {
            if (_wallActive && _wall != null)
                _wall.Build(transform, _ballSpot, _wallCenter, SimConfig.WallCount);
            else if (_wall != null)
                _wall.Clear();
            Arm();
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

            // Distances are measured off the live placement, not the pre-match numbers, because the
            // spot and the wall are placed on the map (and re-rolled in random mode).
            float dist = Flat(_ballSpot, SimConfig.GoalCenter);
            float wallDist = Flat(_ballSpot, _wallCenter);
            int scorePct = _attempts > 0 ? Mathf.RoundToInt(100f * _goals / _attempts) : 0;
            var p = Hud.PanelStart(SimConfig.PenaltyMode ? "PENALTIES" : "FREE KICK", 4);
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "Attempts", _attempts.ToString());
            Hud.Stat(ref p, "Scored %", scorePct + "%");
            Hud.Stat(ref p, "Distance", $"{dist:0.0} m");

            Hud.Legend(SimConfig.PenaltyMode
                ? "HOLD Space power   Mouse aim   WASD spin   M placement   V ball cam   T view   R reset"
                : $"Wall {SimConfig.WallCount} @ {wallDist:0.0}m    HOLD Space power   Mouse aim   WASD spin   M placement   V ball cam   T view   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);

            DrawPowerMeter();
            // !Paused as well as _mapOpen: only Update is pause-gated, so an already-open map kept
            // drawing real clickable controls under the pause menu (IMGUI has no occlusion and the
            // pause scrim eats no events).
            if (_mapOpen && !PauseMenu.Paused) DrawPlacementMap();
            Hud.End();
        }

        // Placement overlay: a top-down map of the attacking third. Click to place the ball/shooter
        // spot (gold) or the wall centre (red); the in-world shooter + ball + wall move to match
        // EXACTLY on close. Mirrors the crossmap overlay in GameManager.
        void DrawPlacementMap()
        {
            Hud.Scrim(0.45f);

            float w = 360f, h = 360f;
            var mapRect = new Rect(Hud.W * 0.5f - w * 0.5f, Hud.H * 0.5f - h * 0.5f, w, h);

            // Ball / Wall edit toggle. Penalty mode has no wall, so only the ball is placeable, and
            // random mode has nothing to place at all (the spot is rolled per attempt).
            GUI.enabled = !_randomSpots;
            if (_wallActive)
            {
                if (Hud.Seg(new Rect(mapRect.x, mapRect.y - 30f, w * 0.5f - 4f, 24f), "Shooter", _mapEdit == 0)) _mapEdit = 0;
                if (Hud.Seg(new Rect(mapRect.x + w * 0.5f + 4f, mapRect.y - 30f, w * 0.5f - 4f, 24f), "Wall", _mapEdit == 1)) _mapEdit = 1;
            }
            else _mapEdit = 0;

            SetPieceMap.Draw(mapRect, ref _ballSpot, ref _wallCenter, _mapEdit);
            GUI.enabled = true;

            Hud.OverlayLabel(mapRect,
                             _mapEdit == 1 ? "PLACE THE WALL" : "PLACE THE SHOOTER",
                             _randomSpots
                                 ? "Random spot every attempt."
                                 : "Click to place the " + (_mapEdit == 1 ? "wall" : "shooter") + ".  M to close.",
                             60f);

            // Random spots on/off without leaving the match (the pre-match toggle, in place). Turning
            // it off keeps the spot the last roll landed on, so it can be nudged from there.
            if (_wallActive
                && Hud.Seg(new Rect(mapRect.x + w * 0.25f, mapRect.yMax + 30f, w * 0.5f, 26f),
                           _randomSpots ? "RANDOM SPOTS: ON" : "RANDOM SPOTS: OFF", _randomSpots))
                _randomSpots = !_randomSpots;
        }

        // Flat (XZ) distance between two world points.
        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // Centered power meter shown while charging: a green -> yellow -> red bar that reflects the
        // oscillating taker meter. Release (handled by the taker) commits at the shown level.
        void DrawPowerMeter()
        {
            if (!_taker.IsCharging) return;
            Hud.Meter(_taker.Meter, "POWER  (release to shoot)");
        }
    }
}
