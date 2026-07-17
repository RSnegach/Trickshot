using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Round state machine, scoring, goal detection, replay, and the IMGUI HUD.
    ///
    /// The player controls only the striker. Crosses are served automatically on a
    /// timer.
    ///
    /// Loop:
    ///   Serving  - crosser counts down and serves a ball to a random spot in the box,
    ///              telegraphing the landing point. Striker is fully controllable.
    ///   BallLive - the cross is in flight. Run to the spot, line up with the mouse
    ///              camera, jump / raise legs (LMB/RMB) / bicycle (F). Goal detection
    ///              is a frame-independent line-cross test.
    ///   Replay   - on a goal or clean trick, time slows and the broadcast camera
    ///              shows it, then the next serve is armed.
    ///
    /// Press R any time to reset the striker and re-arm serving.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Serving, BallLive, Replay }

        GameInput _input;
        Crosser _crosser;
        AimReticle _reticle;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        GameCamera _cam;
        Transform _launchPoint;
        ReplaySystem _replay;
        bool _replayGoalWasTrick;

        State _state = State.Serving;
        float _liveTime;
        float _replayTime;
        float _restTimer;
        Vector3 _prevBallPos;
        bool _resolved;

        int _goals, _trickGoals, _attempts, _saves;
        string _flash = "";
        float _flashTime;

        float _goalLineZ;

        public void Configure(GameInput input, Crosser crosser, AimReticle reticle, BallController ball,
                              Striker striker, ActiveRagdoll strikerRagdoll, Goalkeeper keeper,
                              GameCamera cam, Transform launchPoint)
        {
            _input = input;
            _crosser = crosser;
            _reticle = reticle;
            _ball = ball;
            _striker = striker;
            _strikerRagdoll = strikerRagdoll;
            _keeper = keeper;
            _cam = cam;
            _launchPoint = launchPoint;
            _goalLineZ = SimConfig.GoalCenter.z;

            // Camera follows the pelvis and is driven by mouse movement.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            // Minecraft third person: the camera yaw is the striker's look/turn axis.
            _striker.SetCameraYaw(() => _cam.Yaw);

            // Replay recorder: tracks the ball, all striker bones, and the keeper, and
            // pauses their driving scripts during playback.
            _replay = gameObject.AddComponent<ReplaySystem>();
            var tracked = new System.Collections.Generic.List<Transform> { _ball.transform };
            if (_keeper != null) tracked.Add(_keeper.transform);
            foreach (var t in _strikerRagdoll.BoneTransforms) if (t != null) tracked.Add(t);
            var drivers = new System.Collections.Generic.List<MonoBehaviour> { _striker, _strikerRagdoll, _ball };
            if (_keeper != null) drivers.Add(_keeper);
            _replay.Setup(tracked, drivers, 3.5f);

            EnterServing(SimConfig.ServeFirstDelay);
        }

        public void NotifyValidTrick()
        {
            Flash("TRICK CONNECT!");
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu

            _blinkClock += Time.unscaledDeltaTime;

            if (_state == State.Replay)
            {
                // During a replay ALL other input is locked out; only LMB skips it.
                // (R mid-replay used to corrupt the frozen/kinematic replay state.)
                if (_input.LeftClickPressed) SkipReplay();
                else TickReplay();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            if (_input.ResetPressed) { ResetRound(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            _striker.Tick();

            switch (_state)
            {
                case State.Serving:  TickServing(); break;
                case State.BallLive: TickLive();    break;
            }

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // -------------------------------------------------------------- states
        void EnterServing(float delay)
        {
            _state = State.Serving;
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(delay);
            _resolved = false;
        }

        void TickServing()
        {
            if (_crosser.Tick())
            {
                _attempts++;
                _state = State.BallLive;
                _liveTime = 0f;
                _restTimer = 0f;
                _prevBallPos = _ball.transform.position;
                Flash("CROSS!");
            }
        }

        void TickLive()
        {
            _liveTime += Time.deltaTime;

            Vector3 cur = _ball.transform.position;
            if (!_resolved && BallFullyInGoal(cur))
            {
                OnGoal(_ball.LastShotWasTrick);
                _prevBallPos = cur;
                return;
            }
            _prevBallPos = cur;

            if (_ball.Speed < 0.7f && cur.y < 1.0f) _restTimer += Time.deltaTime;
            else _restTimer = 0f;

            // Behind the goal line but NOT in the goal mouth = out of bounds (missed
            // wide/high/long). Give a small margin past the line before calling it.
            float halfGoal = SimConfig.GoalWidth * 0.5f;
            bool behindGoal = cur.z > _goalLineZ + 0.6f
                              && (Mathf.Abs(cur.x) > halfGoal || cur.y > SimConfig.GoalHeight);

            bool outOfPlay = cur.y < -3f
                             || Mathf.Abs(cur.x) > SimConfig.FieldWidth
                             || Mathf.Abs(cur.z) > SimConfig.FieldLength
                             || behindGoal;

            if (!_resolved && (_restTimer > 1.4f || _liveTime > 8f || outOfPlay))
                OnMiss();
        }

        bool _doReplay;        // this resolution shows a recorded replay (goal) vs. just waits (miss)
        bool _replayStarted;

        void TickReplay()
        {
            _replayTime -= Time.unscaledDeltaTime;

            if (_doReplay)
            {
                // Beat at live speed (see the ball hit the net), then roll the replay.
                if (!_replayStarted && _replayTime <= 0f)
                {
                    _replayStarted = true;
                    _cam.SetMode(GameCamera.Mode.Broadcast);
                    _replay.Play(0.45f);       // ~2.2x slow motion
                }
                else if (_replayStarted && !_replay.IsPlaying)
                {
                    NextServe();
                }
            }
            else if (_replayTime <= 0f)
            {
                NextServe();                    // miss: just a short delay then serve
            }
        }

        void SkipReplay()
        {
            // Cleanly tear down the replay before serving: if it is mid-playback this
            // restores rigidbodies to dynamic and re-enables the driver scripts, which
            // is the step that made a raw reset during replay bug out.
            if (_replay != null && _replay.IsPlaying) _replay.Stop();
            NextServe();
        }

        void NextServe()
        {
            _replayStarted = false;
            _doReplay = false;
            // Rapid-fire testing: leave the striker where it is (see ResetStrikerOnServe).
            if (SimConfig.ResetStrikerOnServe)
                _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            if (_keeper != null) _keeper.ResetTo(SimConfig.KeeperStart);
            _ball.ResetTo(_launchPoint.position);
            EnterServing(SimConfig.ServeInterval);
        }

        // A goal the instant the WHOLE ball is over the line and inside the frame.
        // Per-frame state test (not an interpolated crossing), so it can't be skipped
        // between samples: the trailing edge of the ball (z - r) must be past the line,
        // and the ball must be within the posts/bar and not yet at the back net.
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

        void OnGoal(bool trick)
        {
            _resolved = true;
            _goals++;
            // Speed of the ball as it crossed the line, in km/h (shown in the replay
            // overlay for the whole replay, not just a flash).
            _lastGoalKmh = _ball.Speed * 3.6f;
            Flash(trick ? "TRICK GOAL!" : "GOAL!");
            // Short beat at live speed (see it hit the net), then TickReplay rolls the
            // recorded sports replay.
            _state = State.Replay;
            _replayTime = 0.7f;
            _replayStarted = false;
            _doReplay = true;
        }

        void OnMiss()
        {
            _resolved = true;
            if (_keeper != null && Vector3.Distance(_ball.transform.position, _keeper.transform.position) < 2.2f)
            { _saves++; Flash("SAVED"); }
            else Flash("MISS");
            // No replay for misses: brief reset delay, then serve again.
            _state = State.Replay;
            _replayTime = 1.0f;
            _doReplay = false;
        }

        void ResetRound()
        {
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            if (_keeper != null) _keeper.ResetTo(SimConfig.KeeperStart);
            _ball.ResetTo(_launchPoint.position);
            EnterServing(SimConfig.ServeFirstDelay);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        float _blinkClock;
        float _lastGoalKmh;
        Texture2D _dotTex;
        Texture2D DotTex()
        {
            if (_dotTex == null)
            {
                _dotTex = new Texture2D(1, 1);
                _dotTex.SetPixel(0, 0, Color.white);
                _dotTex.Apply();
            }
            return _dotTex;
        }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

            GUI.Box(new Rect(8, 8, 250, 96), GUIContent.none);
            GUI.Label(new Rect(16, 12, 240, 20), $"Goals {_goals}   Trick {_trickGoals}", st);
            GUI.Label(new Rect(16, 32, 240, 20), $"Crosses {_attempts}   Saves {_saves}", st);
            GUI.Label(new Rect(16, 52, 240, 20), $"State: {_state}", st);
            GUI.Label(new Rect(16, 72, 240, 20), $"Ball {_ball.Speed:0.0} m/s", st);

            var help = "Move: WASD   Camera: Mouse   Ball cam: V\n"
                     + "Jump: Space   Left leg: LMB   Right leg: RMB   Recline (air): E   Reset: R";
            GUI.Label(new Rect(8, Screen.height - 44, 680, 40), help, st);

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 70, Screen.width, 40), _flash, CenteredBig(big));
            }

            // Only during an actual goal replay - NOT on the short miss wait, which
            // also uses the Replay state but shows no replay footage.
            if (_state == State.Replay && _doReplay)
                DrawReplayOverlay();
        }

        void DrawReplayOverlay()
        {
            // Big block anchored near the top-right corner: [dot] REPLAY, then [Click to Skip].
            float blockW = 300f;
            float right = Screen.width - 28f;
            float x = right - blockW;
            float y = 22f;
            float dotSize = 26f;

            // Blinking red dot (~2 Hz) at the left of the block.
            bool on = Mathf.Repeat(_blinkClock, 0.6f) < 0.35f;
            if (on)
            {
                var prev = GUI.color;
                GUI.color = new Color(0.95f, 0.15f, 0.12f);
                GUI.DrawTexture(new Rect(x, y + 10f, dotSize, dotSize), DotTex());
                GUI.color = prev;
            }

            // "REPLAY" left-aligned right after the dot.
            var label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(x + dotSize + 14f, y, blockW - dotSize - 14f, 52f), "REPLAY", label);

            // "[Click to Skip]" underneath.
            var sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.9f) }
            };
            GUI.Label(new Rect(x, y + 56f, blockW, 30f), "[Click to Skip]", sub);

            // Shot speed in km/h, under the skip line, for the whole replay.
            var speed = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 0.9f, 0.35f) }
            };
            GUI.Label(new Rect(x, y + 86f, blockW, 32f), $"{_lastGoalKmh:0} km/h", speed);
        }

        GUIStyle CenteredBig(GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.UpperCenter };
    }
}
