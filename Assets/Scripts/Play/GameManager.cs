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
        GameInput _input;
        Crosser _crosser;
        AimReticle _reticle;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        GameCamera _cam;
        Transform _launchPoint;

        bool _resolved;        // has the current served ball's outcome been called out yet

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

            // Constant rapid-fire in striker mode: no post-goal replay (it would freeze
            // the world and fight the every-2s serve), so no replay recorder here.
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;   // no live ball yet
        }

        public void NotifyValidTrick()
        {
            Flash("TRICK CONNECT!");
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu

            if (_input.ResetPressed) { ResetRound(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            _striker.Tick();
            if (_keeper != null) _keeper.Tick();   // AI keeper goaltends

            // Constant rapid-fire: the crosser self-loops and serves every ServeInterval
            // no matter what happened to the last ball. A serve marks the current ball
            // unresolved so its outcome can be called out once.
            if (_crosser.Tick())
            {
                _attempts++;
                _resolved = false;
                Flash("CROSS!");
            }

            // Watch the live ball for a goal / miss / save purely to flash a callout.
            // Never blocks or delays the next serve.
            TrackOutcome();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Non-blocking outcome watcher: flags a goal/miss/save once per served ball for
        // the callout, without gating serves or freezing for a replay.
        void TrackOutcome()
        {
            if (_resolved) return;
            Vector3 c = _ball.transform.position;

            if (BallFullyInGoal(c)) { OnGoal(_ball.LastShotWasTrick); return; }

            float halfGoal = SimConfig.GoalWidth * 0.5f;
            bool behindGoal = c.z > _goalLineZ + 0.6f
                              && (Mathf.Abs(c.x) > halfGoal || c.y > SimConfig.GoalHeight);
            bool outOfPlay = c.y < -3f
                             || Mathf.Abs(c.x) > SimConfig.FieldWidth
                             || Mathf.Abs(c.z) > SimConfig.FieldLength
                             || behindGoal;
            if (outOfPlay) OnMiss();
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
            if (trick) _trickGoals++;
            Flash("GOAL!");   // plain callout, no shot-type specification
            CrowdCheer.Celebrate();
        }

        void OnMiss()
        {
            _resolved = true;
            // A save close to the keeper is normal; one where he had to DIVE (far from his
            // guard spot, i.e. a big lateral reach) is an EPIC SAVE.
            if (_keeper != null && Vector3.Distance(_ball.transform.position, _keeper.PelvisPos) < 2.2f)
            {
                _saves++;
                Flash(_keeper.WasDivingSave ? "EPIC SAVE!" : "SAVE!");
            }
            else Flash("MISS");
        }

        void ResetRound()
        {
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            if (_keeper != null) _keeper.ResetTo(SimConfig.KeeperStart);
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

            GUI.Box(new Rect(8, 8, 250, 76), GUIContent.none);
            GUI.Label(new Rect(16, 12, 240, 20), $"Goals {_goals}   Trick {_trickGoals}", st);
            GUI.Label(new Rect(16, 32, 240, 20), $"Crosses {_attempts}   Saves {_saves}", st);
            GUI.Label(new Rect(16, 52, 240, 20), $"Ball {_ball.Speed:0.0} m/s", st);

            var help = "Move: WASD   Camera: Mouse   Ball cam: V\n"
                     + "Jump: Space   Left leg: LMB   Right leg: RMB   Air pitch: Mouse wheel   Reset: R";
            GUI.Label(new Rect(8, Screen.height - 44, 700, 40), help, st);

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 70, Screen.width, 40), _flash, CenteredBig(big));
            }
        }

        GUIStyle CenteredBig(GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.UpperCenter };
    }
}
