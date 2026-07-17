using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Goalkeeper-mode game loop: feed on-target shots every few seconds, detect
    /// GOAL / SAVE / MISS with a screen callout, no replay, then reset and feed again.
    /// The player controls the keeper (KeeperController); GameBootstrap builds the rest.
    /// </summary>
    public class KeeperGame : MonoBehaviour
    {
        enum State { Waiting, Live, Resolve }

        GameInput _input;
        ShotServer _server;
        BallController _ball;
        KeeperController _keeper;
        ActiveRagdoll _keeperRagdoll;
        GameCamera _cam;

        State _state = State.Waiting;
        float _liveTime, _restTimer, _resolveTimer;
        bool _resolved;
        bool _keeperTouched;    // did the ball contact the keeper this attempt

        int _goals, _saves, _shots;
        string _flash = ""; float _flashTime;
        float _goalLineZ;

        public void Configure(GameInput input, ShotServer server, BallController ball,
                             KeeperController keeper, ActiveRagdoll keeperRagdoll, GameCamera cam)
        {
            _input = input;
            _server = server;
            _ball = ball;
            _keeper = keeper;
            _keeperRagdoll = keeperRagdoll;
            _cam = cam;
            _goalLineZ = SimConfig.GoalCenter.z;

            _cam.SetKeeperFollow(_keeperRagdoll.Pelvis.transform, () => _keeperRagdoll.FacingRotation, () => _input.Look);
            EnterWaiting(SimConfig.ServeFirstDelay);
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu
            if (_input.ResetPressed) { ResetShot(SimConfig.ServeFirstDelay); return; }

            _keeper.Tick();

            switch (_state)
            {
                case State.Waiting: if (_server.Tick()) BeginLive(); break;
                case State.Live:    TickLive(); break;
                case State.Resolve: TickResolve(); break;
            }
            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void EnterWaiting(float delay)
        {
            _state = State.Waiting;
            _server.Arm(delay);
            _resolved = false;
            _keeperTouched = false;
        }

        void BeginLive()
        {
            _state = State.Live;
            _liveTime = 0f;
            _restTimer = 0f;
            _shots++;
            Flash("SHOT!");
        }

        void TickLive()
        {
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            // Did the ball touch the keeper's body? (used to credit a save)
            if (!_keeperTouched && KeeperContactedBall()) _keeperTouched = true;

            // Whole ball over the line inside the frame = goal.
            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            bool inGoal = c.z - r >= _goalLineZ && c.z <= _goalLineZ + SimConfig.GoalDepth
                          && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
            if (!_resolved && inGoal) { OnGoal(); return; }

            if (_ball.Speed < 0.7f) _restTimer += Time.deltaTime; else _restTimer = 0f;

            bool wide = c.z > _goalLineZ + 0.6f && (Mathf.Abs(c.x) > halfW || c.y > SimConfig.GoalHeight);
            bool dead = _restTimer > 1.2f || _liveTime > 6f;

            if (!_resolved && (wide || dead))
            {
                // Ball didn't go in. If the keeper got a touch (or it died near him),
                // credit a save; otherwise it's a miss (shot off target / no attempt).
                if (_keeperTouched || Vector3.Distance(c, _keeperRagdoll.Pelvis.position) < 2.4f)
                    OnSave();
                else
                    OnMiss();
            }
        }

        bool KeeperContactedBall()
        {
            // Cheap proximity check against the keeper's bones.
            foreach (var t in _keeperRagdoll.BoneTransforms)
                if (t != null && Vector3.Distance(t.position, _ball.transform.position) < SimConfig.BallRadius + 0.28f)
                    return true;
            return false;
        }

        void OnGoal() { _resolved = true; _goals++; Flash("GOAL"); EnterResolve(1.2f); }
        void OnSave() { _resolved = true; _saves++; Flash("SAVE!"); EnterResolve(1.2f); }
        void OnMiss() { _resolved = true; Flash("MISS"); EnterResolve(1.0f); }

        void EnterResolve(float t) { _state = State.Resolve; _resolveTimer = t; }

        void TickResolve()
        {
            _resolveTimer -= Time.deltaTime;
            if (_resolveTimer <= 0f) ResetShot(SimConfig.ServeInterval);
        }

        void ResetShot(float delay)
        {
            _keeper.ForceRecover();
            _keeperRagdoll.ResetTo(SimConfig.KeeperStart, Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up));
            EnterWaiting(delay);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        void OnGUI()
        {
            if (_input == null) return;
            var st = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = Color.white } };
            var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter, normal = { textColor = Color.white } };

            GUI.Box(new Rect(8, 8, 250, 76), GUIContent.none);
            GUI.Label(new Rect(16, 12, 240, 20), $"Saves {_saves}   Goals {_goals}", st);
            GUI.Label(new Rect(16, 32, 240, 20), $"Shots {_shots}", st);
            GUI.Label(new Rect(16, 52, 240, 20), $"Ball {_ball.Speed:0.0} m/s", st);

            var help = "Move: WASD   Jump: Space   Dive: A/D + Space\n"
                     + "Lunge/save left: LMB   right: RMB   Split: LMB+RMB   Reset: R";
            GUI.Label(new Rect(8, Screen.height - 44, 720, 40), help, st);

            if (_flashTime > 0f)
            {
                var c = big.normal.textColor; c.a = Mathf.Clamp01(_flashTime / 1.6f); big.normal.textColor = c;
                GUI.Label(new Rect(0, 70, Screen.width, 44), _flash, big);
            }
        }
    }
}
