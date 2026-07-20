using System.Collections.Generic;
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

        // Cross-targeting map (M): while open, aiming is frozen and clicks place where the
        // crosser delivers. The chosen target overrides the crosser's default landing spot.
        bool _crossMapOpen;
        Vector3 _crossTarget = SimConfig.ServeTarget;

        // Post-goal broadcast replay. Records a rolling window; on a goal it freezes play
        // and plays the last few seconds in slow motion (LMB skips). Then serving resumes.
        ReplaySystem _replay;
        bool _replaying;
        float _replayHold;   // brief delay after a goal before the replay starts

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

            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;   // no live ball yet

            SetupReplay();
        }

        // Build the replay recorder over the ball + striker + keeper bones. GameManager
        // pauses its own control while a replay plays, so no external drivers are needed.
        void SetupReplay()
        {
            var tracked = new List<Transform> { _ball.transform };
            if (_strikerRagdoll != null) tracked.AddRange(_strikerRagdoll.BoneTransforms);
            if (_keeper != null)
            {
                var kr = _keeper.GetComponent<ActiveRagdoll>();
                if (kr != null) tracked.AddRange(kr.BoneTransforms);
            }
            _replay = gameObject.AddComponent<ReplaySystem>();
            _replay.Setup(tracked, null, SimConfig.ReplayWindow);
        }

        public void NotifyValidTrick()
        {
            Flash("TRICK CONNECT!");
        }

        // Striker calls the AI crosser for a pass to his feet: low (driven) or high (chipped).
        // Scatter scales inversely with the player's passing accuracy (like a scrimmage pass),
        // so a low-passing striker gets a looser ball. A full hold isn't needed here (a call is
        // a single press), so power is nominal.
        void CallForCross(bool lofted)
        {
            Vector3 target = _strikerRagdoll.Pelvis.position; target.y = SimConfig.BallRadius;
            float acc = Mathf.Clamp01((PlayerProfile.PassAccuracyMul - 1f) / 0.85f);
            if (PlayerProfile.PerkMaestro) acc = 1f;
            float scatter = SimConfig.PassScatterMaxDeg * (1f - acc);
            _crosser.ServeNow(target, lofted, 0.5f, scatter);
            _attempts++; _resolved = false;
            Flash(lofted ? "CALL: HIGH" : "CALL: LOW");
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu

            if (_input.ResetPressed) { ResetRound(); return; }

            // --- Post-goal replay state machine ---
            // After a goal, hold briefly, then play the broadcast replay. LMB skips it.
            // While replaying (or waiting to), no striker/crosser control runs.
            if (_replaying)
            {
                if (_input.LeftClickPressed || (_replay != null && !_replay.IsPlaying))
                    EndReplay();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }
            if (_replayHold > 0f)
            {
                _replayHold -= Time.unscaledDeltaTime;
                if (_replayHold <= 0f) StartReplay();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // Cross-targeting map (M): toggle. While open, the striker doesn't tick (aiming
            // is frozen) so you can click the map without steering, and the cursor is freed.
            if (_input.CrossMapPressed) SetCrossMapOpen(!_crossMapOpen);
            if (_crossMapOpen)
            {
                if (_keeper != null) _keeper.Tick();
                if (_crosser.Tick()) { _attempts++; _resolved = false; Flash("CROSS!"); }
                TrackOutcome();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;   // skip striker control + ball-cam toggle while the map is up
            }

            if (_input.BallCamPressed) _cam.ToggleBallCam();

            // Call for a pass from the AI crosser: Q = low (driven), E = high (chipped),
            // delivered to the striker's feet with passing-accuracy scatter. Only when the
            // crosser is idle (not mid-serve) so calls don't stack.
            if (_crosser.ReadyToServe)
            {
                if (_input.PassGroundPressed) { CallForCross(lofted: false); }
                else if (_input.PassLoftedPressed) { CallForCross(lofted: true); }
            }

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
            _replayHold = SimConfig.ReplayHold;   // arm the post-goal replay
        }

        // Freeze play, cut to the broadcast camera, and roll the buffered slow-mo replay.
        void StartReplay()
        {
            if (_replay == null) return;
            _replaying = true;
            _cam.SetMode(GameCamera.Mode.Broadcast);
            _reticle.Hide();
            _replay.Play(SimConfig.ReplaySlowMul);
            Flash("REPLAY  (click to skip)");
        }

        // End the replay (finished or skipped): restore control + camera + re-arm serving.
        void EndReplay()
        {
            _replaying = false;
            _replayHold = 0f;
            if (_replay != null) _replay.Stop();
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;
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
            // R during a replay (or the brief pre-replay hold) must first tear the replay
            // down, else the ReplaySystem keeps the bodies kinematic and overwrites the
            // reset poses each frame, freezing play.
            if (_replaying || _replayHold > 0f)
            {
                _replaying = false;
                _replayHold = 0f;
                if (_replay != null) _replay.Stop();
            }
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            if (_keeper != null) _keeper.ResetTo(SimConfig.KeeperStart);
            _cam.SetMode(GameCamera.Mode.Follow);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Open/close the cross map: free the cursor while open, re-lock on close, and push
        // the chosen landing spot to the crosser so subsequent crosses go there.
        void SetCrossMapOpen(bool open)
        {
            _crossMapOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (!open) _crosser.TargetOverride = _crossTarget;   // apply the picked target
        }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            int conversion = _attempts > 0 ? Mathf.RoundToInt(100f * _goals / _attempts) : 0;
            var p = Hud.PanelStart("STRIKER", 5);
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "Trick goals", _trickGoals.ToString());
            Hud.Stat(ref p, "Crosses", _attempts.ToString());
            Hud.Stat(ref p, "Conversion", conversion + "%");
            Hud.Stat(ref p, "Keeper saves", _saves.ToString());

            Hud.Legend("WASD move   Mouse aim   LMB/RMB legs   Space jump   Wheel air-pitch   V ball cam   M cross map   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);

            // Cross-targeting overlay.
            if (_crossMapOpen)
            {
                var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;

                float w = 380f, h = 300f;
                var mapRect = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
                var hdr = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(mapRect.x, mapRect.y - 34f, w, 28f), "WHERE SHOULD CROSSES LAND?", hdr);
                if (CrossMap.Draw(mapRect, ref _crossTarget, interactive: true))
                    _crosser.TargetOverride = _crossTarget;   // live-apply on each click
                var tip = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.85f,0.85f,0.9f) } };
                GUI.Label(new Rect(mapRect.x, mapRect.yMax + 6f, w, 22f), "Click to set the target.  M to close.", tip);
            }
        }
    }
}
