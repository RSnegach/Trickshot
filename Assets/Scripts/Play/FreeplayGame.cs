using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Freeplay driver: endless practice with no scoring pressure and no clock.
    ///
    /// The crosser self-loops and serves forever, the ball lives freely, and the
    /// player controls only the striker (same control scheme as striker mode). A
    /// running goals tally is kept purely for feedback and "GOAL!" flashes on each
    /// one, but nothing gates the next serve and there is no keeper, no MISS/SAVE.
    ///
    /// Press R to recenter the striker. V toggles the ball cam.
    ///
    /// Mirrors GameManager's structure: it pumps _striker.Tick(), self-loops the
    /// crosser via _crosser.Tick(), watches the live ball with a frame-independent
    /// BallFullyInGoal test, and draws an IMGUI HUD in OnGUI.
    /// </summary>
    public class FreeplayGame : MonoBehaviour
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
        SimConfig.Delivery _delivery;
        float _refeedTimer;   // ball-at-feet: delay before respawning after it leaves play

        int _goals, _crosses;
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

            // Crosser paths have no live ball until the first serve; ball-at-feet has one
            // immediately (ConfigureDelivery clears _resolved for it).
            _resolved = true;
            ConfigureDelivery();
        }

        // Set up the crosser (or ball-at-feet) for the chosen freeplay delivery.
        void ConfigureDelivery()
        {
            _delivery = SimConfig.FreeplayDelivery;
            if (_delivery == SimConfig.Delivery.BallAtFeet)
            {
                // No crosser: park a stationary ball in front of the striker to strike.
                // Clear _resolved so the FIRST struck ball can score (Configure/Recenter
                // both leave it true; without this the first goal is never tallied).
                _ball.ResetTo(SimConfig.BallAtFeetSpot);
                _resolved = false;
                _refeedTimer = 0f;
                return;
            }

            // Crosser-driven deliveries. Point the crosser's target/origin per type.
            switch (_delivery)
            {
                case SimConfig.Delivery.AimSpot:
                    _crosser.TargetOverride = SimConfig.FreeplayAimTarget;
                    _crosser.OriginOverride = null;
                    break;
                case SimConfig.Delivery.CornerLeft:
                    _crosser.OriginOverride = CornerFlag(-1f);
                    _crosser.TargetOverride = SimConfig.ServeTarget;   // whip into the box
                    break;
                case SimConfig.Delivery.CornerRight:
                    _crosser.OriginOverride = CornerFlag(1f);
                    _crosser.TargetOverride = SimConfig.ServeTarget;
                    break;
                default: // AutoCross
                    _crosser.TargetOverride = null;
                    _crosser.OriginOverride = null;
                    break;
            }
            _crosser.Arm(SimConfig.ServeFirstDelay);
        }

        // Corner flag position: at the attacking goal line, out by the near touchline.
        Vector3 CornerFlag(float xSign)
        {
            float halfW = PitchLayout.HalfWidth - 0.5f;
            return new Vector3(xSign * halfW, 0.4f, SimConfig.GoalCenter.z - 0.5f);
        }

        void Update()
        {
            if (_input == null) return;
            if (PauseMenu.Paused) return;   // no gameplay/input behind the pause menu

            if (_input.ResetPressed) { Recenter(); return; }
            if (_input.BallCamPressed) _cam.ToggleBallCam();

            _striker.Tick();

            if (_delivery == SimConfig.Delivery.BallAtFeet)
            {
                BallAtFeetUpdate();
            }
            else
            {
                // The crosser self-loops and serves every ServeInterval. A serve marks the
                // current ball unresolved so its outcome is tallied once.
                if (_crosser.Tick())
                {
                    _crosses++;
                    _resolved = false;
                    Flash("CROSS!");
                }
                TrackOutcome();
            }

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Non-blocking watcher: tally a goal once per served ball for the callout, and
        // mark out-of-play balls resolved so a dead ball drifting into the mouth later
        // can't score a phantom goal. No MISS/SAVE in freeplay.
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

        // Ball-at-feet: strike the stationary ball; once it goes in or leaves play, wait
        // a beat then respawn it on the spot for another go.
        void BallAtFeetUpdate()
        {
            Vector3 c = _ball.transform.position;

            if (_refeedTimer > 0f)
            {
                _refeedTimer -= Time.deltaTime;
                if (_refeedTimer <= 0f) { _ball.ResetTo(SimConfig.BallAtFeetSpot); _resolved = false; }
                return;
            }

            if (!_resolved && BallFullyInGoal(c)) { OnGoal(); _refeedTimer = 1.2f; return; }

            bool leftPlay = c.y < -3f
                            || Mathf.Abs(c.x) > SimConfig.FieldWidth
                            || Mathf.Abs(c.z) > SimConfig.FieldLength
                            || (_ball.Speed < 0.4f && Vector3.Distance(c, SimConfig.BallAtFeetSpot) > 3f);
            if (leftPlay) { _resolved = true; _refeedTimer = 0.8f; }
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

        void Recenter()
        {
            _striker.ForceRecover();
            _strikerRagdoll.ResetTo(SimConfig.StrikerStart, Quaternion.identity);
            _cam.SetMode(GameCamera.Mode.Follow);
            _refeedTimer = 0f;
            _resolved = true;
            ConfigureDelivery();   // clears _resolved for ball-at-feet (live ball on the spot)
        }

        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // ----------------------------------------------------------------- HUD
        void OnGUI()
        {
            if (_input == null) return;
            Hud.Begin();

            var p = Hud.PanelStart("FREEPLAY", 3);
            Hud.Stat(ref p, "Delivery", DeliveryLabel(_delivery));
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "Crosses", _crosses.ToString());

            Hud.Legend("WASD move   Mouse aim   LMB/RMB legs   Space jump   Wheel air-pitch   V ball cam   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);
        }

        static string DeliveryLabel(SimConfig.Delivery d)
        {
            switch (d)
            {
                case SimConfig.Delivery.CornerLeft:  return "Corner (L)";
                case SimConfig.Delivery.CornerRight: return "Corner (R)";
                case SimConfig.Delivery.AimSpot:     return "Aimed cross";
                case SimConfig.Delivery.BallAtFeet:  return "Ball at feet";
                default:                             return "Auto cross";
            }
        }
    }
}
