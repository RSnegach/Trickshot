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
        GameInput _input;
        ShotServer _server;
        BallController _ball;
        KeeperController _keeper;
        ActiveRagdoll _keeperRagdoll;
        GameCamera _cam;

        float _liveTime, _restTimer;
        bool _resolved;
        bool _keeperTouched;    // did the ball contact the keeper this attempt
        bool _touchedEpic;       // latched at contact: shot fast enough OR a high dive -> EPIC SAVE

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

            // The camera pans in a cone from a FIXED forward base; the keeper reads that
            // same cone yaw and turns his body to it, so body + camera stay in lock-step.
            _cam.SetKeeperFollow(_keeperRagdoll.Pelvis.transform,
                                 () => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up),
                                 () => _input.Look);
            _keeper.SetLookYawSource(() => _cam.KeeperLookYaw);
            EnterWaiting(SimConfig.ServeFirstDelay);
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu
            if (_input.ResetPressed) { FullReset(); return; }

            _keeper.Tick();

            // Continuous rapid fire: the server self-loops and fires every ~2s no matter
            // what happened to the last ball. A fire opens a fresh (unresolved) attempt.
            if (_server.Tick())
            {
                _shots++;
                _resolved = false;
                _keeperTouched = false;
                _touchedEpic = false;
                _liveTime = 0f;
                _restTimer = 0f;
                Flash("SHOT!");
            }

            TrackOutcome();
            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void EnterWaiting(float delay)
        {
            _server.Arm(delay);
            _resolved = true;   // nothing live until the first fire
            _keeperTouched = false;
        }

        // Non-blocking outcome watcher: flags a goal/save/miss once per served ball for
        // the callout, without gating or delaying the next serve.
        void TrackOutcome()
        {
            if (_resolved) return;
            _liveTime += Time.deltaTime;
            Vector3 c = _ball.transform.position;

            if (!_keeperTouched && KeeperContactedBall())
            {
                _keeperTouched = true;
                // EPIC SAVE criteria, latched at the contact frame (before the ball is slowed by
                // the touch): the shot was travelling at least KeeperEpicSaveSpeed, OR the save was
                // made in a HIGH dive. Those are the only two criteria.
                _touchedEpic = _ball.Speed >= SimConfig.KeeperEpicSaveSpeed || _keeper.IsHighDive;
            }

            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            bool inGoal = c.z - r >= _goalLineZ && c.z <= _goalLineZ + SimConfig.GoalDepth
                          && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
            if (inGoal) { OnGoal(); return; }

            if (_ball.Speed < 0.7f) _restTimer += Time.deltaTime; else _restTimer = 0f;

            bool wide = c.z > _goalLineZ + 0.6f && (Mathf.Abs(c.x) > halfW || c.y > SimConfig.GoalHeight);
            bool dead = _restTimer > 0.4f || _liveTime > 1.9f;

            if (wide || dead)
            {
                if (_keeperTouched || Vector3.Distance(c, _keeperRagdoll.Pelvis.position) < 2.4f)
                    OnSave();
                else
                    OnMiss();
            }
        }

        bool KeeperContactedBall()
        {
            foreach (var t in _keeperRagdoll.BoneTransforms)
                if (t != null && Vector3.Distance(t.position, _ball.transform.position) < SimConfig.BallRadius + 0.28f)
                    return true;
            return false;
        }

        void OnGoal() { _resolved = true; _goals++; Flash("GOAL"); }
        // EPIC SAVE when the shot was hit hard enough OR stopped in a high dive (latched at
        // contact); otherwise a plain SAVE.
        void OnSave() { _resolved = true; _saves++; Flash(_touchedEpic ? "EPIC SAVE!" : "SAVE!"); CrowdCheer.Celebrate(); }
        void OnMiss() { _resolved = true; Flash("MISS"); }

        // R only: full reset of keeper + serve loop (not per-ball, which would yank the
        // player-controlled keeper around every 2s).
        void FullReset()
        {
            _keeper.ForceRecover();
            _keeperRagdoll.ResetTo(SimConfig.KeeperStart, Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up));
            EnterWaiting(SimConfig.ServeFirstDelay);
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            int resolved = _saves + _goals;
            int savePct = resolved > 0 ? Mathf.RoundToInt(100f * _saves / resolved) : 0;
            var p = Hud.PanelStart("GOALKEEPER", 4);
            Hud.Stat(ref p, "Saves", _saves.ToString());
            Hud.Stat(ref p, "Conceded", _goals.ToString());
            Hud.Stat(ref p, "Shots faced", _shots.ToString());
            Hud.Stat(ref p, "Save %", savePct + "%");

            Hud.Legend("WASD move   A/D+Space dive   LMB/RMB lunge save   LMB+RMB split   Space jump   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);
        }
    }
}
