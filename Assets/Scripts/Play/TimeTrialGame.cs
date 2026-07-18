using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Time-trial driver: score as many goals as possible before a countdown expires.
    ///
    /// Plays exactly like freeplay/striker mode (the crosser self-loops, the player
    /// controls only the striker) but a clock runs down from SimConfig.TimeTrialSeconds.
    /// Each ball fully in the goal is +1. When the clock hits 0 serving freezes and a
    /// "TIME! Goals: N" summary is shown; R restarts the timer and resets the score.
    ///
    /// Mirrors GameManager's structure: it pumps _striker.Tick(), self-loops the crosser
    /// via _crosser.Tick() while the clock is live, watches the ball with a frame-
    /// independent BallFullyInGoal test, and draws an IMGUI HUD in OnGUI.
    /// </summary>
    public class TimeTrialGame : MonoBehaviour
    {
        GameInput _input;
        Crosser _crosser;
        AimReticle _reticle;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        GameCamera _cam;
        Transform _launchPoint;

        bool _resolved;   // has the current served ball's outcome been tallied yet
        bool _finished;   // clock hit 0: serving frozen, summary shown

        int _goals, _crosses;
        float _timeLeft;
        string _flash = "";
        float _flashTime;

        float _goalLineZ;

        public void Configure(GameInput input, Crosser crosser, AimReticle reticle, BallController ball,
                              Striker striker, ActiveRagdoll strikerRagdoll, GameCamera cam, Transform launchPoint)
        {
            _input = input;
            _crosser = crosser;
            _reticle = reticle;
            _ball = ball;
            _striker = striker;
            _strikerRagdoll = strikerRagdoll;
            _cam = cam;
            _launchPoint = launchPoint;
            _goalLineZ = SimConfig.GoalCenter.z;

            // Camera follows the pelvis and is driven by mouse movement.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look);
            // Minecraft third person: the camera yaw is the striker's look/turn axis.
            _striker.SetCameraYaw(() => _cam.Yaw);

            _cam.SetMode(GameCamera.Mode.Follow);
            StartRun();
        }

        // Arm the clock + serve loop from scratch and clear the score.
        void StartRun()
        {
            _finished = false;
            _goals = 0;
            _crosses = 0;
            _timeLeft = SimConfig.TimeTrialSeconds;
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;   // no live ball yet
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu; also freezes the clock

            if (_input.BallCamPressed) _cam.ToggleBallCam();

            if (_finished)
            {
                // Summary screen: only R (restart) is live. The striker still ticks so
                // the world isn't locked, but no serving and no scoring.
                _striker.Tick();
                if (_input.ResetPressed) StartRun();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // R recenters the striker mid-run (does not reset the clock or score).
            if (_input.ResetPressed) { Recenter(); return; }

            _striker.Tick();

            // Count the clock down in real time (guarded against pause above; there is no
            // slow-mo in this mode, so scaled delta is fine).
            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f) { _timeLeft = 0f; EndRun(); return; }

            // The crosser self-loops and serves every ServeInterval. A serve marks the
            // current ball unresolved so its outcome is tallied once.
            if (_crosser.Tick())
            {
                _crosses++;
                _resolved = false;
                Flash("CROSS!");
            }

            TrackOutcome();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Non-blocking watcher: tally a goal once per served ball, and mark out-of-play
        // balls resolved so a dead ball drifting into the mouth later can't score a
        // phantom goal. No MISS/SAVE in time trial.
        void TrackOutcome()
        {
            if (_resolved) return;
            Vector3 c = _ball.transform.position;

            if (BallFullyInGoal(c)) { OnGoal(); return; }

            float halfGoal = SimConfig.GoalWidth * 0.5f;
            bool behindGoal = c.z > _goalLineZ + 0.6f
                              && (Mathf.Abs(c.x) > halfGoal || c.y > SimConfig.GoalHeight);
            bool outOfPlay = c.y < -3f
                             || Mathf.Abs(c.x) > SimConfig.FieldWidth
                             || Mathf.Abs(c.z) > SimConfig.FieldLength
                             || behindGoal;
            if (outOfPlay) _resolved = true;
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

        void OnGoal()
        {
            _resolved = true;
            _goals++;
            Flash("GOAL!");
        }

        // Clock expired: stop serving, hide the telegraph, and lock into the summary.
        void EndRun()
        {
            _finished = true;
            _resolved = true;
            _reticle.Hide();
            Flash("TIME!");
        }

        void Recenter()
        {
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        static string Clock(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60:0}:{total % 60:00}";
        }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            var clock = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };

            GUI.Box(new Rect(8, 8, 250, 76), GUIContent.none);
            GUI.Label(new Rect(16, 12, 240, 20), "TIME TRIAL", st);
            GUI.Label(new Rect(16, 32, 240, 20), $"Goals {_goals}   Crosses {_crosses}", st);
            GUI.Label(new Rect(16, 52, 240, 20), $"Ball {_ball.Speed:0.0} m/s", st);

            // Big centered clock along the top.
            GUI.Label(new Rect(0, 10, Screen.width, 60), Clock(_timeLeft), clock);

            var help = "Move: WASD   Camera: Mouse   Ball cam: V\n"
                     + "Jump: Space   Left leg: LMB   Right leg: RMB   Air pitch: Mouse wheel   Reset: R";
            GUI.Label(new Rect(8, Screen.height - 44, 700, 40), help, st);

            if (_finished)
            {
                // Centered end-of-run summary.
                var summary = new GUIStyle(big) { alignment = TextAnchor.UpperCenter };
                GUI.Label(new Rect(0, Screen.height * 0.5f - 60f, Screen.width, 46), $"TIME! Goals: {_goals}", summary);
                GUI.Label(new Rect(0, Screen.height * 0.5f - 6f, Screen.width, 30), "Press R to restart", CenteredBig(st));
                return;
            }

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 80, Screen.width, 40), _flash, CenteredBig(big));
            }
        }

        GUIStyle CenteredBig(GUIStyle s) => new GUIStyle(s) { alignment = TextAnchor.UpperCenter };
    }
}
