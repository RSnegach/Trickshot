using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

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
        bool _goalCounted;      // ...and has this served ball already been called a GOAL? (see
                               // TrackOutcome: a goal must still count after a miss/save callout)
        readonly SaveWatch _save = new SaveWatch();   // shared SAVE / EPIC SAVE / MISS verdict

        int _goals, _trickGoals, _attempts, _saves;
        string _flash = "";
        float _flashTime;

        float _goalLineZ;

        // Cross-targeting map (M): while open, aiming is frozen and clicks place where the crosser
        // delivers AND where the (AI) crosser stands, plus the delivery type and the two serve
        // sliders. The SETTINGS live in CrossMap.Session (shared with the networked striker driver,
        // and persistent across a rebuild the way the pre-match sliders they replaced were); only
        // whether the panel is up is per-match state.
        bool _crossOpen;

        // Escape ownership while the map is up now lives on CrossMap, because the networked striker
        // driver opens the same panel and PauseMenu has to skip its Escape for EITHER of them.
        // Kept as an alias so PauseMenu's existing call site reads the same as before.
        public static bool CrossMapEscapeOwned => CrossMap.EscapeOwned;

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

            // Camera follows the pelvis and is driven by mouse movement; the wheel zooms replays.
            _cam.SetFollow(_strikerRagdoll.Pelvis.transform, () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
            // Minecraft third person: the camera yaw is the striker's look/turn axis.
            _striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);

            _cam.SetMode(GameCamera.Mode.Follow);
            // Plant the AI crosser BEFORE the first serve. PlantAt and not SetOrigin deliberately:
            // SetOrigin would also claim the launch origin, which moves the ball's rest point 1.6 m
            // off the wing launch point and shortens every default cross from 1.294 s to 1.190 s of
            // flight. Nobody asked for that; the origin stays the cross map's business. What this buys
            // is _plantHome (which arms the drift snap-back) and a real _plantFacing instead of
            // identity, so his first swing no longer turns him to face world +Z.
            // Seed the world from the cross panel, which now OWNS shot speed + cross interval (they
            // moved off the pre-match screen). Applied before the first Arm so the opening serve
            // already uses this panel's cadence rather than whatever a previously played mode left
            // in the statics.
            CrossMap.Apply(CrossMap.Session, _crosser);
            _crosser.PlantAt(CrossMap.Session.spot);
            _crosser.Arm(SimConfig.ServeFirstDelay);
            _resolved = true;   // no live ball yet

            SetupReplay();
        }

        // Build the replay recorder over the ball + striker + keeper bodies. GameManager pauses
        // its own control while a replay plays; the only drivers are the bodies' own cosmetic sims
        // that ReplaySystem.TrackBody asks to pause.
        void SetupReplay()
        {
            var tracked = new List<Transform> { _ball.transform };
            var drivers = new List<MonoBehaviour>();
            ReplaySystem.TrackBody(tracked, drivers, _strikerRagdoll);
            if (_keeper != null) ReplaySystem.TrackBody(tracked, drivers, _keeper.Body);
            _replay = gameObject.AddComponent<ReplaySystem>();
            _replay.Setup(tracked, drivers, SimConfig.ReplayWindow);
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Frozen) return;   // no gameplay/input behind a FREEZING pause menu

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
                // Replays off (Settings > Gameplay): the goal hold still plays out, then it goes
                // straight back to serving instead of rolling the slow-mo.
                if (_replayHold <= 0f) { if (GameplaySettings.Replays) StartReplay(); else EndReplay(); }
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // Cross-targeting map (M): toggle. While open, the striker doesn't tick (aiming
            // is frozen) so you can click the map without steering, and the cursor is freed.
            // Escape also closes it (never opens it) - a second way out for a mouse-only reflex,
            // matching every other overlay in the game (settings, quickchat, pause itself).
            if (_input.CrossMapPressed) SetCrossMapOpen(!_crossOpen);
            else if (_crossOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetCrossMapOpen(false);
            if (_crossOpen)
            {
                if (_keeper != null) _keeper.Tick();
                if (_crosser.Tick()) { _attempts++; _resolved = false; _goalCounted = false; _save.Arm(); CareerStats.RecordStrikerCross(); }
                TrackOutcome();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;   // skip striker control + ball-cam toggle while the map is up
            }

            if (_input.BallCamPressed) _cam.ToggleBallCam();

            // Q cycles how the AI crosser delivers - Ground / Low / High - the very setting the
            // cross map's Crosser tab shows, so the next ball's shape can be changed without
            // opening the map. (This replaced the old Q/E call-for-pass.)
            if (_input.PassLoftedPressed)
            {
                CrossMap.Session.delivery = CrossMap.NextDelivery(CrossMap.Session.delivery);
                CrossMap.Apply(CrossMap.Session, _crosser);
                Flash("CROSS: " + CrossMap.DeliveryName(CrossMap.Session.delivery));
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
                _goalCounted = false;
                _save.Arm();
                CareerStats.RecordStrikerCross();
            }

            // Watch the live ball for a goal / miss / save purely to flash a callout.
            // Never blocks or delays the next serve.
            TrackOutcome();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Non-blocking outcome watcher: flags a goal/miss/save once per served ball for
        // the callout, without gating serves or freezing for a replay. A GOAL is special-
        // cased to still count after the miss/save callout already fired (see below).
        void TrackOutcome()
        {
            Vector3 c = _ball.transform.position;

            // A goal counts ANY time the whole ball is in the net, even after this serve's
            // miss/save callout already went up. Those callouts are deliberately early and
            // non-blocking: a cross that sails wide resolves MISS while the ball is still
            // chaseable, and a keeper parry that settles resolves SAVE with the ball live at
            // his feet - and the striker's next action on the SAME ball (running down the
            // wide cross, smashing the rebound) is a real shot that can go in. The old
            // _resolved gate swallowed exactly those goals: move the cross target/crosser on
            // the map so deliveries stop arriving at the default box spot, and every rebound
            // goal silently stopped counting - no banner, no replay. _goalCounted keeps the
            // once-per-serve rule intact (a ball sitting in the net can't re-count while the
            // replay hold queues).
            if (!_goalCounted && BallFullyInGoal(c)) { OnGoal(_ball.LastShotWasTrick); return; }

            if (_resolved) return;   // this served ball's miss/save callout already happened

            // Keeper contact, from the ball's touch log (never proximity).
            _save.Poll(_ball, _keeper != null ? _keeper.Body : null,
                       _keeper != null && _keeper.WasDivingSave);

            // A save that stays IN PLAY (caught, smothered, parried down) never leaves the field, so
            // the out-of-play test below would swallow the callout entirely. Call it once the touched
            // ball has settled. Checked AFTER the goal test, so a parry that trickles in is a goal.
            if (_save.SettledAfterTouch(_ball)) { OnSave(); return; }

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
            _goalCounted = true;
            _goals++;
            if (trick) _trickGoals++;
            CareerStats.RecordStrikerGoal(trick);
            Flash("GOAL!");   // plain callout, no shot-type specification
            CrowdCheer.Celebrate();
            AudioManager.Instance?.PlayGoalCelebration();   // cheer + applause, cuts any lively swell
            // Stand the striker back up on the goal. A trick finish (diving header / bicycle)
            // leaves him prone + limp (DriveScale low, upright lock off), and his Tick() is
            // suspended through the replay hold + replay, so without this he'd stay slumped on
            // the deck for the whole celebration. ForceRecover pops him upright immediately.
            _striker.ForceRecover();
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

        // A keeper TOUCH is a save, wherever the ball ends up. This used to test the ball's resting
        // distance to the keeper, which called every parried-clear save a MISS and called an
        // untouched ball that happened to die near him a SAVE.
        void OnSave()
        {
            _resolved = true;
            _saves++;
            CareerStats.RecordStrikerShotDenied();
            Flash(_save.Callout());
        }

        void OnMiss()
        {
            _resolved = true;
            if (_save.Touched) { _saves++; CareerStats.RecordStrikerShotDenied(); Flash(_save.Callout()); return; }
            AudioManager.Instance?.PlayMissBoosMaybe();   // occasional boos (~1 in 5-6)
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
            // R resets the striker and keeper, so put the crosser back too: the snap-back tolerates
            // CrosserPlantDrift (0.6 m) of wander and a reset should clear that as well. PlantAt, so a
            // player who HAS placed him with the cross map keeps their chosen launch origin.
            _crosser.PlantAt(CrossMap.Session.spot);
            _crosser.Arm(SimConfig.ServeFirstDelay);   // Arm also ResetTo's the ball to the launch origin
            _resolved = true;
            _goalCounted = false;
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Open/close the cross map: free the cursor while open, re-lock on close, and push
        // the chosen landing spot to the crosser so subsequent crosses go there.
        void SetCrossMapOpen(bool open)
        {
            _crossOpen = open;
            CrossMap.NoteOpenState(open);
            GameInput.CaptureCursor(!open);
            if (_cam != null) _cam.FreezeLook = open;   // hold the view still while placing on the map
            if (!open)
            {
                CrossMap.Apply(CrossMap.Session, _crosser);     // target + delivery + the two serve sliders
                _crosser.SetOrigin(CrossMap.Session.spot);      // relocate the (AI) crosser to the placed spot
            }
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

            Hud.Legend("WASD move   Mouse aim   LMB/RMB legs   Space jump   Wheel air-pitch   Q cross type   V ball cam   T view   M cross map   R reset"
                       + Keybinds.ThirdLegHint(PlayerProfile.Appearance.Adult));
            Hud.Flash(_flash, _flashTime / 1.6f);

            // Cross-targeting overlay. Shared with the networked striker driver so both behave
            // identically; a moved crosser spot re-plants him (and claims the launch origin).
            // The !Paused gate matters even though the Escape interlock stops the pause menu opening
            // over an open map: Escape is not the only way in (a lost window focus, a future pause
            // path), and these are real clickable controls that IMGUI would let a click reach
            // straight through the pause scrim.
            if (_crossOpen && !PauseMenu.Paused)
            {
                // Single-player: one player, everything editable, no crosser picker.
                var r = CrossMap.DrawOverlay(ref CrossMap.Session, _crosser, CrossMap.Perms.SinglePlayer);
                if (r.spotMoved) _crosser.SetOrigin(CrossMap.Session.spot);   // faces the target too
            }

            Hud.End();
        }
    }
}
