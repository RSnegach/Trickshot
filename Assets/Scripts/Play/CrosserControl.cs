using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Human control for the crosser role. Two states:
    ///
    ///  ROAMING - the body is a normal Striker: WASD moves, LMB/RMB raise a leg, the ball is whatever
    ///            it is. Nothing here touches it. Press ENTER (Cross) to set up.
    ///  STANCE  - the ball comes to his feet and he steps back onto a short run-up on the side his
    ///            plant foot goes (a right-footer comes in from the ball's left, a left-footer the
    ///            mirror), and from there it is played like a free kick: the same SetPieceTaker runs
    ///            the meter, the spin keys, the botch windows, the run-in, the footed swing and the
    ///            hop. What differs is all Begin options - the charge button is LMB/RMB instead of
    ///            Space, the meter fills once and holds, the spin keys are two stacked axes, and the
    ///            launch is a ballistic drop onto a spot (or a ball rolled to it) instead of a shot.
    ///
    /// What the player controls in the stance:
    ///   LOOK       where it goes (yaw) and part of how high (pitch: look up for a floatier ball).
    ///   LMB / RMB  hold to fill the power meter, release to cross. The meter fills ONCE and sits at
    ///              the top until released - slower for a better passer (CrossMeterRateLow/High), so
    ///              the distance is easier to pick. Either button - which LEG kicks is his footedness,
    ///              not which button he pressed.
    ///   D / A      curl, the free kick's: the ball goes OUT to that side and bends back onto the
    ///              spot. D = out to his right, spinning counterclockwise from above. Over-holding
    ///              past a full charge botches, as a free kick's does.
    ///   W / S      pitch: W drives it down - a full W is played along the turf, a rolled ball -
    ///              and S floats it. Stacks with A/D: each axis charges on its own.
    /// Power decides DISTANCE and nothing else. Accuracy is his PASSING stat: it scatters the aim,
    /// the reach and the loft; it never shortens the ball on purpose - a bad passer hits it as far,
    /// it just goes somewhere else.
    ///
    /// The aim shows as a translucent line from the ball tracing the first part of the INTENDED
    /// flight (CrossPathLine): longer for a better passer, and his ball only follows it as well as
    /// he passes. The AI crosser keeps its landing reticle; this never uses it.
    ///
    /// While set he is untouchable: a CrosserBubble (the same one the AI crosser wears) pushes any
    /// other body out of a radius, and the ball ignores his own body so the run-up foot cannot
    /// knock the dead ball (the taker owns that, as it does for a free kick).
    ///
    /// Host-authoritative. The host runs this for every human crosser (local device or the remote
    /// player's NetInputSource) and is the only peer that moves the ball. A CLIENT that is itself
    /// the crosser runs the same thing in DISPLAY-ONLY mode - stance, meter, run-up, swing, the aim
    /// line - so the player sees and feels the kick they are making; the ball it shows is the host's,
    /// streamed. The camera yaw + pitch fed in must be the same source that body's Striker faces by,
    /// or the line would show an aim his body is not making.
    /// </summary>
    public class CrosserControl : MonoBehaviour
    {
        IStrikerInput _input;
        Crosser _crosser;
        BallController _ball;
        ActiveRagdoll _ragdoll;
        Striker _striker;
        System.Func<float> _camYaw, _camPitch;
        bool _displayOnly;
        bool _leftFooted;
        float _passAcc;     // 0..1 passing accuracy (scatter shrinks with it, the meter slows, the line grows)
        bool _maestro;      // perfect passing: no stat scatter (botch still applies)
        float _meterRate;   // bar/s, from the passing stat

        readonly SetPieceTaker _taker = new SetPieceTaker();
        bool _inStance;
        Vector3 _ballSpot;
        CrosserBubble _bubble;
        CrossPathLine _path;

        // ---- public read surface (HUD + driver) ----
        /// <summary>Set up and not yet through with the kick.</summary>
        public bool InStance => _inStance;
        /// <summary>Holding the charge button with the meter running.</summary>
        public bool IsCharging => _inStance && _taker.IsCharging && _taker.HasCharged;
        /// <summary>0..1 power meter, for Hud.Meter.</summary>
        public float Meter => _taker.Meter;
        /// <summary>Playing the kick (planted + swinging, or following through): a networked
        /// puppet should show a kick, and which leg is LeftFooted.</summary>
        public bool Swinging => _inStance && _taker.Swinging;
        public bool LeftFooted => _leftFooted;

        /// <summary>
        /// `leftFooted` / `passAcc01` / `maestro` are the CROSSER'S, not the local profile's: the host
        /// drives remote humans too, and their build is what should decide their kick. `displayOnly`
        /// = a client animating its own crosser; never moves the ball.
        /// </summary>
        public void Init(IStrikerInput input, Crosser crosser, BallController ball, ActiveRagdoll ragdoll,
                         Striker striker, bool displayOnly, bool leftFooted, float passAcc01, bool maestro)
        {
            _input = input; _crosser = crosser; _ball = ball; _ragdoll = ragdoll; _striker = striker;
            _displayOnly = displayOnly;
            _leftFooted = leftFooted;
            _passAcc = Mathf.Clamp01(passAcc01);
            _maestro = maestro;
            _meterRate = Mathf.Lerp(SimConfig.CrossMeterRateLow, SimConfig.CrossMeterRateHigh, Acc01);
            if (_crosser != null) _crosser.AutoServe = false;   // a human decides when to cross
        }

        // The passing accuracy everything scales by: Maestro is a perfect passer for all of it.
        float Acc01 => _maestro ? 1f : _passAcc;

        /// <summary>Camera yaw + pitch sources for the AIM. Local: the GameCamera; remote: the
        /// wire (NetInputSource.LookYaw / LookPitch). Must match what the body's Striker faces by.</summary>
        public void SetCameraYaw(System.Func<float> camYaw, System.Func<float> camPitch = null)
        { _camYaw = camYaw; _camPitch = camPitch; }

        public void Tick()
        {
            if (_input == null || _crosser == null || _ragdoll == null || _ragdoll.Pelvis == null) return;

            // Enter and R are the same key here: set up a cross, or set it up AGAIN if one is already
            // set (ball back to the feet, back onto the run-up, meter cleared).
            bool setUp = _input.CrossPressed || _input.ResetPressed;

            if (!_inStance)
            {
                // Roaming. The Striker owns the body; we only watch for the set-up key. Grounded and
                // not mid-trick, so a dive or a slide cannot be cut into a stance from the deck.
                if (setUp && _ragdoll.IsGrounded && (_striker == null || !_striker.IsBusy))
                    EnterStance();
                return;
            }

            // In the stance the taker owns the body. While it is only charging (no run-in yet) it
            // does not touch the pose or the move input itself - a free kick's driver has already
            // parked the taker - so hold him still here, and keep the aim line on his live intent.
            if (_taker.IsCharging)
            {
                // Re-set: the same set-up from scratch. Only while charging - once he is running in
                // the kick is committed, exactly as a free kick's is.
                if (setUp)
                {
                    _taker.Reset();
                    ExitStance();
                    EnterStance();
                    return;
                }
                _ragdoll.ClearPoseOverrides();
                _ragdoll.MoveInput = Vector3.zero;
                ShowPreview();
            }

            _taker.Tick();
            // The line shows the committed intent through the run-in, and goes at contact (the host
            // hides it in Launch too; a display-only client only has this).
            if (_taker.JustStruck) _path?.Hide();
            if (_taker.Done) ExitStance();
        }

        // ---- stance ----

        void EnterStance()
        {
            _inStance = true;
            if (_striker != null) { _striker.ForceRecover(); _striker.ControlEnabled = false; }

            // The ball comes to his feet: just ahead of the toes, on the turf. Then he steps back
            // onto the run-up: back along the aim line and OUT to the plant-foot side, so the kicking
            // leg swings through the ball with the other foot planted beside it. Right-footer: left of
            // the line. Left-footer: right of it.
            Vector3 feet = _ragdoll.Pelvis.position; feet.y = 0f;
            Vector3 aim = LookDir();
            Vector3 aimFlat = Flat(aim, _ragdoll.FacingRotation * Vector3.forward);
            _ballSpot = feet + aimFlat * 0.35f;
            _ballSpot.y = SimConfig.BallRadius;

            float side = _leftFooted ? 1f : -1f;
            Vector3 lateral = Vector3.Cross(Vector3.up, aimFlat);          // his right
            Vector3 start = _ballSpot - aimFlat * SimConfig.CrossStanceRunUp + lateral * (side * SimConfig.CrossStanceSide);
            start.y = 0f;
            Vector3 toBall = _ballSpot - start; toBall.y = 0f;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.MoveInput = Vector3.zero;
            _ragdoll.ResetTo(start, Quaternion.LookRotation(toBall.normalized, Vector3.up));

            if (!_displayOnly)
            {
                // Host only: the ball is real here, and so is the bubble (it moves rigidbodies).
                // The bubble is get-or-add and ENABLED, never destroyed and re-created: Destroy is
                // deferred, so a destroyed-this-frame bubble is still found by GetComponent, and the
                // AI crosser taking the seat back in that same frame would see one and add none.
                _ball.ResetTo(_ballSpot);
                _bubble = GetComponent<CrosserBubble>();
                if (_bubble == null) _bubble = gameObject.AddComponent<CrosserBubble>();
                _bubble.Init(_ragdoll, SimConfig.CrossStanceBubble);
                _bubble.enabled = true;
            }

            // Then it is a free kick. The "goal" handed over is only the hop direction (the launch is
            // ours), so it is a point down the aim line. The accuracy stat the botch windows widen by
            // is his PASSING, not his shooting. Footedness is his, whichever peer he is on.
            _taker.Begin(_input, _ragdoll, _ball, _ballSpot, _ballSpot + aimFlat * 10f,
                         displayOnly: _displayOnly,
                         combinedOverride: Acc01,
                         aimPoint: LookDir,               // frozen at release, a direction not a point
                         leftFootedOverride: _leftFooted ? 1 : 0,
                         chargeWithLegs: true,
                         launch: Launch,
                         dualAxisSpin: true,              // A/D and W/S stack
                         meterRate: _meterRate,           // slower for a better passer
                         meterHoldAtMax: true,            // fills once, sits at the top
                         curlChargeMul: SimConfig.CrossCurlChargeMul);   // A/D fills quicker; same botch window
        }

        void ExitStance()
        {
            _inStance = false;
            _path?.Hide();
            _crosser.HideTelegraph();   // an AI serve's marker handed over mid-telegraph, if any
            if (_bubble != null) { _bubble.enabled = false; _bubble = null; }   // roaming: touchable again
            if (_ball != null && _ragdoll != null) _ball.IgnoreBody(_ragdoll, false);
            if (_striker != null) _striker.ControlEnabled = true;
        }

        // ---- the cross itself ----

        // One solved cross - everything the launch and the preview share, so the line shows exactly
        // the flight the launch would play with no scatter.
        struct Flight
        {
            public bool ground;     // rolled along the turf (a full W) instead of flown
            public Vector3 from;    // ball centre at launch
            public Vector3 target;  // where it comes down / runs through
            public Vector3 flatDir; // flat aim direction actually used (post-scatter)
            public float tof;       // seconds to the target
            public Vector3 v0;      // launch velocity, already angled out by the curl
            public Vector3 accel;   // the curl's constant return acceleration (gravity NOT included)
        }

        // `curl`/`pitch` are the taker's signed axes (+ = D / W). The three errors are the scatter:
        // yaw in degrees, reach as a fraction, loft as a blend offset - all 0 for the preview.
        Flight Solve(Vector3 aim, float power, float curl, float pitch,
                     float yawErrDeg, float distErr, float loftErr)
        {
            var f = new Flight();
            f.from = _ballSpot;
            Vector3 flatDir = Flat(aim, _ragdoll.FacingRotation * Vector3.forward);
            if (Mathf.Abs(yawErrDeg) > 0.001f)
                flatDir = Quaternion.AngleAxis(yawErrDeg, Vector3.up) * flatDir;
            f.flatDir = flatDir;

            // Reach is the meter, from HIS FEET at zero to CrossAimFarReach at full - so the line
            // starts on the ball and grows as the bar fills. Nothing clamps it: a full-power ball
            // aimed past the goal line goes past the goal line, into the stands if that is where it
            // was aimed. The distance is the player's to get wrong. The bar-to-reach curve
            // (CrossReachCurve) keeps the short ball resolvable now that the top is a whole pitch.
            float reach = SimConfig.CrossAimFarReach
                          * Mathf.Pow(Mathf.Clamp01(power), Mathf.Max(1f, SimConfig.CrossReachCurve))
                          * (1f + distErr);
            reach = Mathf.Max(0.05f, reach);
            f.target = new Vector3(f.from.x + flatDir.x * reach, SimConfig.BallRadius, f.from.z + flatDir.z * reach);
            Vector3 right = Vector3.Cross(Vector3.up, flatDir);

            // Curl, the free kick's model: out to the key's side at CrossCurlOutSpeed x charge, and a
            // return acceleration of -2w/T bends it back so the net sideways drift over the flight is
            // zero (wT - wT) - more curve never means wider, it still lands on the spot.
            float side = curl > 0f ? 1f : curl < 0f ? -1f : 0f;
            // The bend grows with passing: the base out-speed at zero, CrossCurlSkillGain more at full.
            float outSpeed = SimConfig.CrossCurlOutSpeed * (1f + SimConfig.CrossCurlSkillGain * Acc01) * Mathf.Abs(curl);

            // Pitch: W (+) drives it down, S (-) floats it.
            float w = Mathf.Max(0f, pitch), s = Mathf.Max(0f, -pitch);
            f.ground = w >= SimConfig.CrossGroundPitchMin;
            if (f.ground)
            {
                // Rolled: a flat pace for the distance (Crosser.GroundRollSpeed's shape, without the
                // AI's "Cross speed" slider), frictionless until past the spot. The roll bends less.
                // Firmer than the AI's serve and NOT capped under BallRollSpeed: the ball is flagged
                // as a delivery, so rolling resistance still stops it past the spot whatever its pace
                // (see CrossGroundPaceHuman / BallController.HoldRollFrictionUntil).
                float speed = Mathf.Clamp(SimConfig.CrossGroundPaceHuman * Mathf.Sqrt(reach),
                                          SimConfig.CrossGroundMinSpeed, SimConfig.CrossGroundMaxSpeed);
                float outG = outSpeed * SimConfig.CrossGroundCurlMul;
                f.tof = reach / speed;
                f.v0 = flatDir * speed + right * (side * outG);
                f.accel = right * (-side * 2f * outG / f.tof);
                return f;
            }

            // Flown: loft is the look pitch + S, then W scales it down toward the flat drive as it
            // charges (the turf is the step past that). LaunchTo's solve lands on the target whatever
            // the time of flight, so loft is purely how long it hangs for the distance.
            float loft = Mathf.Clamp01(SimConfig.CrossLoftBase + aim.y * SimConfig.CrossPitchLoft
                                       + s * SimConfig.CrossLoftKeyGain + loftErr);
            loft *= 1f - Mathf.Clamp01(w / SimConfig.CrossGroundPitchMin);
            float k = Mathf.Lerp(SimConfig.CrossArcKGround, SimConfig.CrossArcKLoft, loft);
            // The AI's 1.8 s cap (CrossArcMaxTime) is not applied here: it exists to stop a half-field
            // AI cross ballooning, and a human long ball has to be allowed to hang. What bounds this
            // instead is LAUNCH SPEED - the flight is lengthened until the ball leaves no faster than
            // CrossMaxLaunchSpeed, so a full-power ball rises into a punt rather than a laser.
            f.tof = Mathf.Clamp(k * Mathf.Sqrt(reach), SimConfig.CrossArcMinTime, SimConfig.CrossHumanArcMaxTime);
            Vector3 g = Physics.gravity;
            f.tof = Mathf.Max(f.tof, MinFlightTimeForSpeed(f.target - f.from, -g.y, SimConfig.CrossMaxLaunchSpeed));
            Vector3 v0 = (f.target - f.from - 0.5f * g * (f.tof * f.tof)) / f.tof;   // LaunchTo's solve
            f.v0 = v0 + right * (side * outSpeed);
            f.accel = right * (-side * 2f * outSpeed / f.tof);
            return f;
        }

        // The shortest flight time at which a ballistic launch over `disp` (flat distance d, rise h)
        // leaves at or under `vmax`. From |v0|^2 = (d^2 + h^2)/t^2 + h g + g^2 t^2 / 4, solving
        // |v0| = vmax is a quadratic in t^2; the SMALLER root is the flatter of the two arcs that
        // fit, which is the one that changes the k-based flight the least. No real root means the
        // spot is out of range at that speed: return the minimum-speed time (the ~45 deg arc, where
        // d|v0|/dt = 0) so the ball at least leaves as slowly as the distance allows.
        static float MinFlightTimeForSpeed(Vector3 disp, float g, float vmax)
        {
            float h = disp.y;
            float d2 = disp.x * disp.x + disp.z * disp.z;
            float A = g * g * 0.25f;
            float B = h * g - vmax * vmax;
            float C = d2 + h * h;
            float disc = B * B - 4f * A * C;
            float t2 = disc >= 0f ? (-B - Mathf.Sqrt(disc)) / (2f * A) : Mathf.Sqrt(C / A);
            return t2 > 1e-4f ? Mathf.Sqrt(t2) : 0f;
        }

        // The taker fires this at contact with everything the player committed. Distance is the
        // meter, direction is the frozen look, pitch is look-pitch + W/S, curl is A/D, and accuracy
        // is the passing stat plus whatever the free-kick model botched.
        void Launch(SetPieceTaker.Commit c)
        {
            Vector3 aim = c.aim ?? LookDir();

            // Scatter: the intent (what the line showed) strays by the passing stat, in all three of
            // yaw, reach and loft, plus the botch share. Uniform, so a poor passer sometimes hits the
            // line and sometimes misses it by the full amount.
            float inacc = 1f - Acc01;
            float yawErr  = Random.Range(-1f, 1f) * (SimConfig.CrossScatterMaxDeg * inacc + c.botch * SimConfig.CrossBotchScatterDeg);
            float distErr = Random.Range(-1f, 1f) * (SimConfig.CrossScatterDistFrac * inacc + c.botch * SimConfig.CrossBotchDistFrac);
            float loftErr = Random.Range(-1f, 1f) * SimConfig.CrossScatterLoft * inacc;
            Flight f = Solve(aim, c.power, c.curl, c.pitch, yawErr, distErr, loftErr);
            float side = c.curl > 0f ? 1f : c.curl < 0f ? -1f : 0f;

            if (f.ground)
            {
                // The taker has just put the ball on the spot (on the turf). Straight KickTo, then the
                // hold and the curl - AFTER it, since KickTo clears both.
                _ball.KickTo(f.v0);
                // Frictionless out to the spot, held long enough to actually get there (the default
                // 6 s covered the AI's short serves, not an 80 m ball). Past it the ball is a loose
                // ball again and rolling resistance brings it to a stop, at any pace.
                _ball.HoldRollFrictionUntil(f.target, f.tof * 1.5f + 1f);
                if (side != 0f) _ball.SetCurl(f.accel, f.tof);
                // KickTo zeroes the spin, which would slide the ball like a puck. Spin it at EXACTLY
                // the rolling rate (v / r), not the dribble's cosmetic 2.2 rad/s per m/s: under-spun,
                // the turf's friction ate the slip - a solid sphere settles to (5v + 2rw) / 7, 15% of
                // the pace, inside the first metre - so every ground cross arrived short of the speed
                // it was solved at, and read as friction killing it.
                _ball.Rb.angularVelocity = Vector3.Cross(Vector3.up, f.flatDir) * (f.v0.magnitude / SimConfig.BallRadius);
            }
            else
            {
                // LaunchTo solves its v0 from a target: hand it the point f.v0 reaches at tof (the real
                // target shifted out by the curl's launch angle), so the ball leaves with exactly f.v0
                // and the return accel bends it back onto f.target. LaunchTo also divides the flight
                // time by "Cross speed" (BallSpeedMul) - the AI crosser's slider, only shown while the
                // AI crosses - so pre-multiply to cancel it: a human's cross is not scaled by whatever
                // it was last left at, and the curl compensation stays right.
                Vector3 g = Physics.gravity;
                Vector3 launchTarget = f.from + f.v0 * f.tof + g * (0.5f * f.tof * f.tof);
                _ball.LaunchTo(launchTarget, f.tof * Mathf.Max(0.1f, SimConfig.BallSpeedMul), f.accel, 0f);
                // Cosmetic spin on a curl: D = counterclockwise seen from above. Unity's +Y turn is
                // clockwise from above, hence the negative. There is no Magnus force in this project -
                // the bend is the accel above, this is paint.
                if (side != 0f)
                    _ball.Rb.angularVelocity = Vector3.up * (-side * SimConfig.CrossCurlSpinVis * Mathf.Abs(c.curl));
            }
            // His own follow-through foot must not re-hit the ball it just struck.
            _ball.SuppressStrikeFor(_ragdoll, 0.4f);
            _path?.Hide();
        }

        // The aim line: the first part of the flight the player would get RIGHT NOW with no scatter
        // (live meter, look, curl and pitch charges). How much of it: a fraction of the flight time
        // by his passing, capped in metres. A rolled ball's line lies on the turf.
        void ShowPreview()
        {
            Flight f = Solve(LookDir(), _taker.Meter, _taker.CurlAxis, _taker.PitchAxis, 0f, 0f, 0f);
            if (_path == null) _path = new CrossPathLine();
            float frac = Mathf.Lerp(SimConfig.CrossLineFracLow, SimConfig.CrossLineFracHigh, Acc01);
            if (f.ground)
                _path.Trace(f.from, f.v0, f.accel, f.tof * frac, SimConfig.CrossLineMaxMetres, flatY: 0.05f);
            else
                _path.Trace(f.from, f.v0, f.accel + Physics.gravity, f.tof * frac, SimConfig.CrossLineMaxMetres);
        }

        // 3D look direction from the camera yaw + pitch (a direction so the taker can freeze it at
        // release as its "aim point" - it only stores a Vector3, and a direction is all we need).
        Vector3 LookDir()
        {
            float yaw = _camYaw != null ? _camYaw() : _ragdoll.FacingRotation.eulerAngles.y;
            float pitch = _camPitch != null ? _camPitch() : 0f;
            return Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }

        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude > 1e-4f) return v.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 1e-4f ? fallback.normalized : Vector3.forward;
        }

        /// <summary>
        /// Leave the stance NOW, synchronously, so a caller about to re-fit this body (the AI taking
        /// the seat back) can do so on top of a clean one. Destroy() is deferred to end of frame, so
        /// relying on OnDestroy here would run our cleanup AFTER the caller's re-fit and undo it -
        /// specifically the ball-ignores-body flag the AI crosser needs on and ExitStance turns off.
        /// Idempotent; OnDestroy is the backstop for a teardown nobody called.
        /// </summary>
        public void Teardown()
        {
            if (!_inStance) return;
            _taker.Reset();
            ExitStance();
        }

        void OnDestroy()
        {
            Teardown();
            if (_path != null) { _path.Dispose(); _path = null; }
        }
    }
}
