using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Round state machine, scoring, goal detection, replay, and the IMGUI HUD.
    ///
    /// Loop:
    ///   Aiming   - ball parked at the crosser. Aim the reticle (mouse), hold Space to
    ///              charge, release to cross. You can already move the striker (WASD).
    ///   BallLive - the cross is in flight. Control the striker: run to the spot, turn
    ///              to line up, jump / bicycle kick (LMB or F). Goal detection is a
    ///              frame-independent line-cross test so fast shots still register.
    ///   Replay   - on a goal or a clean trick, time slows and the broadcast camera
    ///              shows it, then the round resets.
    ///
    /// Press R any time to reset.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        enum State { Aiming, BallLive, Replay }

        GameInput _input;
        Crosser _crosser;
        AimReticle _reticle;
        BallController _ball;
        Striker _striker;
        ActiveRagdoll _strikerRagdoll;
        Goalkeeper _keeper;
        GameCamera _cam;
        Transform _launchPoint;

        State _state = State.Aiming;
        float _liveTime;
        float _replayTime;
        float _restTimer;
        Vector3 _prevBallPos;
        bool _resolved;

        int _goals, _trickGoals, _attempts, _saves;
        string _flash = "";
        float _flashTime;

        // goal geometry
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

            EnterAiming();
        }

        public void NotifyValidTrick()
        {
            Flash("TRICK CONNECT!");
            _cam.SetMode(GameCamera.Mode.Broadcast);
            _cam.TriggerSlowMo(1.1f);
        }

        void Update()
        {
            if (_input == null) return;

            if (_input.ResetPressed) { ResetRound(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            // Striker is controllable during aiming and live play.
            if (_state == State.Aiming || _state == State.BallLive)
                _striker.Tick();

            switch (_state)
            {
                case State.Aiming:   TickAiming();  break;
                case State.BallLive: TickLive();    break;
                case State.Replay:   TickReplay();  break;
            }

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // -------------------------------------------------------------- states
        void EnterAiming()
        {
            _state = State.Aiming;
            _reticle.Active = true;
            _crosser.ResetCharge();
            _ball.ResetTo(_launchPoint.position);
            // Follow the pelvis rigidbody (it moves); the Striker component's own
            // GameObject sits at origin and never translates.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _striker.Yaw);
            _cam.SetMode(GameCamera.Mode.Follow);
            _resolved = false;
        }

        void TickAiming()
        {
            // keep the ball parked until launch
            if (!_crosser.Charging || !_input.ChargeReleased)
                _ball.ResetTo(_launchPoint.position);

            if (_crosser.TickAiming())
            {
                _attempts++;
                _state = State.BallLive;
                _reticle.Active = false;
                _liveTime = 0f;
                _restTimer = 0f;
                _prevBallPos = _ball.transform.position;
                Flash("CROSS!");
            }
        }

        void TickLive()
        {
            _liveTime += Time.deltaTime;

            // frame-independent goal line crossing (ball moving toward +Z into mouth)
            Vector3 cur = _ball.transform.position;
            if (!_resolved && CrossedGoalLine(_prevBallPos, cur))
            {
                OnGoal(_ball.LastShotWasTrick);
                _prevBallPos = cur;
                return;
            }
            _prevBallPos = cur;

            // rest / out-of-play detection
            if (_ball.Speed < 0.7f && cur.y < 1.0f) _restTimer += Time.deltaTime;
            else _restTimer = 0f;

            bool outOfPlay = cur.y < -3f
                             || Mathf.Abs(cur.x) > SimConfig.FieldWidth
                             || Mathf.Abs(cur.z) > SimConfig.FieldLength;

            if (!_resolved && (_restTimer > 1.4f || _liveTime > 8f || outOfPlay))
            {
                OnMiss();
            }
        }

        void TickReplay()
        {
            _replayTime -= Time.unscaledDeltaTime;
            if (_replayTime <= 0f)
                ResetRound();
        }

        bool CrossedGoalLine(Vector3 prev, Vector3 cur)
        {
            // must be moving in +Z and straddle the line this frame
            if (prev.z > _goalLineZ || cur.z < _goalLineZ) return false;
            float dz = cur.z - prev.z;
            if (dz <= 0.0001f) return false;
            float t = (_goalLineZ - prev.z) / dz;
            Vector3 at = Vector3.Lerp(prev, cur, Mathf.Clamp01(t));
            float halfW = SimConfig.GoalWidth * 0.5f;
            return Mathf.Abs(at.x) <= halfW && at.y >= 0f && at.y <= SimConfig.GoalHeight;
        }

        void OnGoal(bool trick)
        {
            _resolved = true;
            _goals++;
            if (trick) { _trickGoals++; Flash("TRICK GOAL!"); }
            else Flash("GOAL!");
            EnterReplay(2.1f, trick ? 1.6f : 1.1f);
        }

        void OnMiss()
        {
            _resolved = true;
            // crude save credit: ball ended near the keeper
            if (Vector3.Distance(_ball.transform.position, _keeper.transform.position) < 2.2f)
            { _saves++; Flash("SAVED"); }
            else Flash("MISS");
            EnterReplay(1.1f, 0f);
        }

        void EnterReplay(float seconds, float slowmo)
        {
            _state = State.Replay;
            _replayTime = seconds;
            if (slowmo > 0f)
            {
                _cam.SetMode(GameCamera.Mode.Broadcast);
                _cam.TriggerSlowMo(slowmo);
            }
        }

        void ResetRound()
        {
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            _keeper.ResetTo(SimConfig.KeeperStart);
            _ball.ResetTo(_launchPoint.position);
            _reticle.Active = true;
            EnterAiming();
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };

            GUI.Box(new Rect(8, 8, 250, 108), GUIContent.none);
            GUI.Label(new Rect(16, 12, 240, 20), $"Goals {_goals}   Trick {_trickGoals}", st);
            GUI.Label(new Rect(16, 32, 240, 20), $"Attempts {_attempts}   Saves {_saves}", st);
            GUI.Label(new Rect(16, 52, 240, 20), $"State: {_state}", st);
            GUI.Label(new Rect(16, 72, 240, 20), $"Ball {_ball.Speed:0.0} m/s", st);

            // charge bar
            if (_state == State.Aiming)
            {
                float w = 220f * _crosser.Charge;
                GUI.Box(new Rect(16, 92, 220, 14), GUIContent.none);
                GUI.color = Color.Lerp(Color.green, Color.red, _crosser.Charge);
                GUI.Box(new Rect(16, 92, w, 14), GUIContent.none);
                GUI.color = Color.white;
            }

            // controls
            var help = "Aim: Mouse   Charge cross: hold Space   Curl: Q/E\n"
                     + "Striker move: WASD   Jump: Space   Bicycle: LMB / F   Ball cam: V   Reset: R";
            GUI.Label(new Rect(8, Screen.height - 44, 640, 40), help, st);

            // flash
            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 70, Screen.width, 40), _flash, CenteredBig(big));
            }
        }

        GUIStyle CenteredBig(GUIStyle s)
        {
            var c = new GUIStyle(s) { alignment = TextAnchor.UpperCenter };
            return c;
        }
    }
}
