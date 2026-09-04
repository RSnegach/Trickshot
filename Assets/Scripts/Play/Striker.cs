using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player control of the active-ragdoll striker (the only thing the player drives).
    ///
    /// Movement is Minecraft-style third person: the MOUSE drives the camera yaw, the
    /// body faces that yaw while grounded, and WASD moves relative to it - W forward
    /// along the look direction, S back, A/D strafe (keep facing forward while sliding
    /// sideways).
    ///
    ///  - Grounded, the pelvis is hard-locked upright so he cannot fall over, and a
    ///    procedural run cycle picks up alternating feet with bent knees.
    ///  - Space jumps. While AIRBORNE the MOUSE WHEEL pitches him about his central
    ///    axis (scroll back to lie flat for a bicycle kick; raise legs with LMB/RMB).
    ///    Space held while moving does a forward diving header, landing belly-down and
    ///    staying prone briefly. LMB/RMB raise the legs.
    /// </summary>
    public class Striker : MonoBehaviour, IPlayerController
    {
        // SlideLimp is the tail of a sliding challenge: limp on the deck, then up. It lives HERE
        // rather than in its own bool because every gate in this file tests `_mode == Trick.None`
        // (facing steer, jump/dive arming, the upright re-lock, the run cycle, leg raises, slide/sit
        // arming), so a third state is excluded from all of them for free - and because ForceRecover
        // and Knockdown.Fell already key off _mode/IsBusy, so every existing bail-out path recovers
        // it with no new call sites. A separate bool would have needed each one taught about it.
        // Tumble is the landing of a body that comes down tipped over (a bicycle kick, a scrolled
        // flip): down on the back or front, limp, then up - see StartTumble. Same reasoning as
        // SlideLimp for being a mode rather than a bool.
        enum Trick { None, Dive, SlideLimp, Tumble }

        IStrikerInput _input;
        ActiveRagdoll _ragdoll;
        System.Func<float> _camYaw;
        System.Func<float> _camPitch;   // AIM only; the body never pitches (see SetCameraYaw)
        Dribble _dribble;   // optional; when carrying, movement slows + facing slews (Control claws both back)

        public bool ControlEnabled = true;
        // A human CROSSER's body carries a real Striker for movement/pose (see NetStrikerMatch.
        // SpawnCrosserBody), but LMB/RMB on that body are CrosserControl's - the crosser's own
        // footedness+charge for a delivery, not a shot. Narrower than ControlEnabled on purpose:
        // that gate gone would also kill his movement, which the crosser still needs.
        public bool ShootingEnabled = true;

        /// <summary>
        /// Ignore the player's Acrobat perk on this body: air pitch keeps the normal 115-degree
        /// clamp and a tipped landing always tumbles. The perk is a global read of the local skill
        /// tree (PlayerProfile.PerkAcrobat), which is right for a match but wrong for an AUTHORED
        /// body - the menu vignettes choreograph a bicycle kick that must end on the striker's
        /// back, and an Acrobat player would silently get the other branch (a full flip that lands
        /// on its feet) and never see the beat the panel is advertising.
        /// </summary>
        public bool IgnoreAcrobat;

        bool Acrobat => !IgnoreAcrobat && PlayerProfile.PerkAcrobat;

        public void SetDribble(Dribble d) => _dribble = d;

        Trick _mode = Trick.None;
        // True while a diving header is in progress (for the DIVING HEADER goal callout).
        public bool IsDiving => _mode == Trick.Dive;
        // Down after landing tipped over (a missed bicycle, a flip). The host streams it as Down.
        public bool IsTumbling => _mode == Trick.Tumble;

        // One-shot, armed at dive launch and consumed by the match sim the first time this
        // dive fells an opponent, so a single dive cannot mow down a line of players. Never needs
        // clearing: it is only ever read while IsDiving, and the next StartDive re-arms it.
        public bool DiveHitPending;
        // Busy with a trick (dive, etc.): the Dribble system suspends the leash while true.
        public bool IsBusy => _mode != Trick.None || _sitting || _sliding;
        // Is the leg-raise button for this side held? LMB raises the LEFT leg, RMB the RIGHT.
        // A volley only fires off a leg the player is deliberately raising, so just RUNNING
        // into a flying ball (fast gait swing, no button) never counts as a swing/volley.
        public bool LegRaiseHeld(bool leftSide)
            => _input != null && !_sitting && (leftSide ? _input.LeftLegHeld : _input.RightLegHeld);
        // Flat forward direction the striker faces (== camera yaw while grounded). The
        // dribble carry point sits along this, and dribble shots launch along it.
        public Vector3 FacingForward
        {
            get { Vector3 f = _ragdoll.FacingRotation * Vector3.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }
        }
        // Latched bicycle window. A bicycle is a fast whole-body flip: the pelvis sweeps
        // through the reclined cone in ~2 frames, so reading the angle at the exact contact
        // frame (the old approach) missed most attempts - the ball-cam, assist, and trick
        // classification would all silently see "not a bike". Instead we ARM a window the
        // moment the player commits (airborne + leaning back, or scrolling the air-pitch
        // target back past a threshold) and hold it open BicycleWindow seconds, so contact,
        // camera, and assist all read a stable, generous "yes". KickDetector still re-checks
        // the real inversion for the LEGAL trick bonus, so illegal shots don't get the boost.
        float _bicycleTimer;   // >0 while a bicycle attempt is latched live

        // True while a bicycle attempt is latched (used by KickDetector's gate, the ball's
        // assist, and the ball-cam). Never during a diving header, and NEVER while grounded:
        // a bicycle connects in the air by definition. The grounded check matters because
        // IsGrounded flips true in the physics phase (FixedUpdate) BEFORE the Update-phase
        // code that zeros the latch on landing, so without it a touchdown foot contact on
        // the landing frame would be miscredited as a bicycle. IsGrounded is a stable state
        // read (not the fast-sweeping pelvis angle the latch exists to smooth over), so
        // reading it at contact is reliable.
        // `_mode == Trick.None` rather than `!= Trick.Dive`: a strictly tighter guard, so the new
        // SlideLimp state cannot be read as a bicycle either. Belt and braces - a slide starts
        // grounded and _bicycleTimer is only armed airborne and zeroed on landing - but the tighter
        // test costs nothing and cannot regress the dive, which was already excluded.
        public bool TrickActive => _bicycleTimer > 0f && _mode == Trick.None
                                   && _ragdoll != null && !_ragdoll.IsGrounded;

        // Arm/refresh the latched window when the airborne body commits to a flip. Called
        // each airborne frame from AirPitchControl. Loose thresholds (arm EARLY in the flip
        // and on scroll intent) so contact anywhere through the flip counts.
        void ArmBicycleWindow()
        {
            if (_ragdoll.Pelvis == null) return;
            float upness = Vector3.Dot(_ragdoll.Pelvis.transform.up, Vector3.up);
            bool tipped = upness < SimConfig.BicycleArmUpness;
            bool leaned = Mathf.Abs(_airPitchTarget) > SimConfig.BicycleArmPitch;
            if (tipped || leaned) _bicycleTimer = SimConfig.BicycleWindow;   // (re)arm to full
        }

        float _facingYaw;
        public float Yaw => _facingYaw;

        float _gaitPhase;
        float _gaitWeight;     // 0..1 fade of the run pose; NEVER a phase reset (that popped)
        readonly Vector3[] _gaitScratch = new Vector3[(int)Bone.Count];
        float _airborneLock;   // grace after a normal jump before upright re-locks
        // The mouse wheel is only read between a JUMP and the LANDING that follows it. A free-
        // spinning wheel (an unlatched scroll ring) keeps reporting scroll for a second or more
        // after a flick. On the ground that read nothing - but the ground probe flickers while a
        // landed body settles, and every not-grounded frame of that settle took the still-spinning
        // wheel as a fresh air-pitch: upright lock off, whole-body spin on, then grounded again and
        // reset - the wobble in a circle after a bicycle kick. Armed by NormalJump; dropped on the
        // first grounded frame AFTER the probe has read airborne (not on the takeoff frames, where it
        // still reads the turf he is leaving), and by every trick start/end and recovery.
        bool _wheelArmed;
        bool _leftGround;      // has the probe read airborne since the last jump?
        float _proneTimer;     // while >0 (counting down on the ground), stay in the trick
        float _tumbleTime;     // seconds since a tumble began (ManageTumble's landing grace + hard cap)
        float _airPitchTarget; // wheel-driven target lean (deg) about the right axis; clamped to +/-90 (or uncapped for Acrobat full flips)
        // Acrobat full-flip: an UNWRAPPED accumulated lean angle so the body can chase a target
        // past +/-180 and actually loop all the way around (a plain SignedAngle/DeltaAngle wraps,
        // so 360==0 and the ease would just take the short way to upright). Seeded on takeoff.
        float _flipAngle;      // accumulated rotation (deg), unbounded, across the +/-180 seam
        float _lastAxisRoll;   // previous frame's wrapped axis roll, for the unwrap delta
        bool _flipSeed = true; // true until _lastAxisRoll is seeded for the current airborne window
        float _legRaiseL, _legRaiseR;   // eased 0..1 leg-raise amounts (no snap-back on release)
        float _headerBend;              // eased 0..1 header-pose amount (torso fold, or barrel pitch + nod)
        float _lmbTimer, _rmbTimer;     // per-button grace windows; header needs both live at once
        // Sit gesture state. _lmbDownT/_rmbDownT are the per-side PRESS-EDGE windows the
        // simultaneity test reads (distinct from _lmbTimer/_rmbTimer, which are the airborne
        // header's HELD grace). _sitDrop is the eased hip drop in metres, and it is what gates
        // jumping and the gait, so the ramp back up counts as still seated.
        bool _sitting;
        // Aiming a pass must not steer the run. See LockRun.
        float _runYaw;      // heading the run is held at while aiming
        float _runLockT;    // 1 = run fully held at _runYaw, 0 = fully following the camera
        bool _runLocked;
        bool _sliding;         // riding out a sliding challenge (LMB+RMB pushed forward)
        // LMB+RMB were both held while AIRBORNE (the header pose) and have not been let go since.
        // Resolved on the first grounded frame - see ResolveHeaderLanding - and cleared by any
        // release. Without it, a header hold carried through touchdown left both legs at full raise
        // (the grounded branch of ApplyLegRaises) while the run carried on: seated in mid-air.
        bool _airHold;
        float _slideTimer;     // seconds left committed to the slide
        float _slideRecover;   // lockout after getting up, so he cannot chain slides
        float _lmbDownT, _rmbDownT;
        float _sitDrop;
        // True while the sit gesture owns the body. The host reads it to stream AnimState.Sit.
        public bool IsSitting => _sitting;

        /// <summary>
        /// Hold the RUN at its current heading while the player aims a pass with the mouse.
        ///
        /// This exists because the mouse does two jobs on this scheme: it turns the body AND it is the
        /// camera, and a look-ray pass adds a third - it aims. Without the split, pointing at a
        /// team-mate off to the side swung the body and the run with it, so every non-forward pass cost
        /// you your momentum and there was no way to play one while running anywhere else. FIFA does not
        /// have the problem because the run is the left stick and the pass is a separate axis; this is
        /// the same separation, reached by freezing one of them rather than adding a stick.
        ///
        /// While locked, WASD stays relative to the heading captured here and the camera is free to
        /// point the pass. Nothing about the pass aim reads this - the aim is always the live camera yaw.
        /// Idempotent: the heading is captured on the transition in, so calling it every frame is fine.
        /// </summary>
        public void LockRun(float yaw)
        {
            if (!_runLocked) { _runLocked = true; _runYaw = yaw; }
        }

        /// <summary>Stop holding the run. It eases back to the camera over PassAimBlendTime.</summary>
        public void ReleaseRun() => _runLocked = false;

        public bool RunLocked => _runLocked;
        /// <summary>Down in a sliding challenge. MatchGame reads this to resolve the contact, so
        /// the tackle and the animation can never disagree about whether he is actually sliding.</summary>
        public bool IsSliding => _sliding;

        // This body's header aid, cached from the SPECIES IT WAS BUILT AS. Read off the ragdoll and
        // never off PlayerProfile: the host binds a remote peer's net input onto a Striker driving
        // that peer's body (see SetInput), so the local player's species is the wrong answer for
        // every body but its own. Safe to cache in Init because the ragdoll never changes after it.
        HeaderAction _header = HeaderAction.Biped;

        // Which limb the raise lifts, per side, as {upper, lower}. A biped's legs, a quadruped's
        // FRONT legs. Also cached from the layout: the hind legs sit behind a quadruped's body and
        // cannot reach a ball in front of it.
        Bone[] _raiseL, _raiseR;

        // Diving header lifecycle.
        float _spaceHeld;      // how long Space held while grounded (tap vs hold-to-dive)
        float _diveAir;        // time since the dive started

        public void Init(IStrikerInput input, ActiveRagdoll ragdoll)
        {
            _input = input;
            _ragdoll = ragdoll;
            _facingYaw = ragdoll.FacingRotation.eulerAngles.y;
            _header = Species.ById(ragdoll.SpeciesId).Header;
            _raiseL = ragdoll.RaiseChain(true);
            _raiseR = ragdoll.RaiseChain(false);
        }

        // Swap the input source at runtime (e.g. the host binding a remote player's net
        // input to this body). Callers keep the ragdoll; only the input changes.
        public void SetInput(IStrikerInput input) => _input = input;

        // Bind the look sources. camPitch is optional and only used for AIM (the body never
        // pitches): a volley leaves down the camera ray, so the shot solve needs both angles.
        // Callers that pass yaw only keep the old flat-aim behaviour.
        public void SetCameraYaw(System.Func<float> camYaw, System.Func<float> camPitch = null)
        {
            _camYaw = camYaw;
            _camPitch = camPitch;
        }

        // Does this body have a real look source (a local camera, or a remote player's streamed
        // yaw/pitch)? An AI body has none, so the shot solve falls back to aiming at the goal.
        public bool HasLookAim => _camYaw != null;
        public float LookYaw   => _camYaw != null ? _camYaw() : _facingYaw;
        public float LookPitch => _camPitch != null ? _camPitch() : 0f;

        void Update()
        {
            if (_airborneLock > 0f)
                _airborneLock = Mathf.Max(0f, _airborneLock - Time.deltaTime);
            // The latched bicycle window bleeds down in real time; AirPitchControl re-arms
            // it to full while the airborne body is still committed to the flip.
            if (_bicycleTimer > 0f)
                _bicycleTimer = Mathf.Max(0f, _bicycleTimer - Time.deltaTime);
        }

        public void Tick()
        {
            if (!ControlEnabled || _ragdoll.Pelvis == null) return;

            _ragdoll.ClearPoseOverrides();

            bool grounded = _ragdoll.IsGrounded;
            float camYaw = _camYaw != null ? _camYaw() : _facingYaw;

            // The run's heading. Normally the camera's, but held at its own while a pass is aimed and
            // EASED back afterwards rather than snapped - resuming instantly would kick his run
            // sideways by however far the aim had swung (see LockRun).
            _runLockT = Mathf.MoveTowards(_runLockT, _runLocked ? 1f : 0f,
                                          Time.deltaTime / Mathf.Max(0.01f, SimConfig.PassAimBlendTime));
            float moveYaw = _runLockT > 0.001f ? Mathf.LerpAngle(camYaw, _runYaw, _runLockT) : camYaw;

            Quaternion yawRot = Quaternion.Euler(0f, moveYaw, 0f);
            Vector3 camFwd = yawRot * Vector3.forward;
            Vector3 camRight = yawRot * Vector3.right;

            Vector2 mv = _input.Move;                     // x = strafe, y = forward
            Vector3 wish = Vector3.ClampMagnitude(camFwd * mv.y + camRight * mv.x, 1f);
            // Build traits: lighter/shorter = quicker; sprint is weighted separately.
            float traitSpeed = _input.SprintHeld ? PlayerProfile.SprintSpeedMul : PlayerProfile.MoveSpeedMul;
            // Widen what Pace is worth (see SimConfig.PaceSpeedGain). Only the ABOVE-1 side is
            // scaled: doubling the deviation both ways would have made a heavy uninvested build
            // slower than it is today, which is a nerf nobody asked for.
            if (traitSpeed > 1f) traitSpeed = 1f + (traitSpeed - 1f) * SimConfig.PaceSpeedGain;
            float speed = SimConfig.StrikerMoveSpeed * (_input.SprintHeld ? SimConfig.StrikerSprintMul : 1f) * traitSpeed;
            // Hard ceiling, applied after everything (see SimConfig.SprintSpeedCeiling). The dribble
            // multipliers below cut into this, which is correct - the cap is on how fast he can ever
            // travel, not on how fast the trait maths is allowed to claim he does.
            speed = Mathf.Min(speed, SimConfig.SprintSpeedCeiling);

            // While dribbling, the striker is SLOWER and turns SLOWER - Control claws both
            // back. dribbling = the Dribble component is actively carrying the ball.
            bool dribbling = _dribble != null && _dribble.Carrying;
            bool closeControl = dribbling && _input.CloseControlHeld;
            if (dribbling)
            {
                float t = PlayerProfile.DribbleTightness;  // 0 (no Control) .. 1 (full)
                speed *= Mathf.Lerp(SimConfig.DribbleMoveMulLow, SimConfig.DribbleMoveMulHigh, t);
                // Close control trades pace for touch: a shuffle, but the ball stays under you.
                if (closeControl) speed *= SimConfig.DribbleCloseSpeedMul;
            }
            _ragdoll.MoveInput = wish * speed;

            // Body faces where the mouse points (the camera yaw). Normally set directly, so
            // facing freezes the instant the mouse stops (he never turns on his own). While
            // DRIBBLING the facing SLEWS toward the aim at a Control-scaled turn rate, so a
            // raw build turns ponderously with the ball and a Control build turns sharply.
            if (_mode == Trick.None)
            {
                if (dribbling)
                {
                    float t = PlayerProfile.DribbleTightness;
                    float turnRate = Mathf.Lerp(SimConfig.DribbleTurnRateLow, SimConfig.DribbleTurnRateHigh, t);
                    if (closeControl) turnRate *= SimConfig.DribbleCloseTurnMul;   // pivot on the ball
                    _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, moveYaw, turnRate * Time.deltaTime);
                }
                else _facingYaw = moveYaw;
                _ragdoll.FacingRotation = Quaternion.Euler(0f, _facingYaw, 0f);
            }

            // --- adult mode: appendage to attention while the ThirdLeg bind is held ---
            // Written every tick, so a release (or a dropped remote frame pinning the bit) always
            // reaches the body; the sim itself eases and gates the hitbox (AnatomySim).
            var anatomy = _ragdoll.Anatomy;
            if (anatomy != null) anatomy.Erect = _input.ThirdLegHeld;

            // --- header hold carried through touchdown (forward = high dive, else sit) ---
            ResolveHeaderLanding(grounded);

            // --- sit gesture (LMB+RMB together, grounded only) ---
            UpdateSit(grounded);

            // --- charged shot (ONE leg button, grounded, ball in range) ---
            // AFTER UpdateSit deliberately: the sit/slide gesture claims the both-buttons case and
            // sets _sitting/_sliding on this same frame, and the charge gate reads both.
            if (ShootingEnabled) UpdateShotCharge(grounded);

            // --- trigger tricks / jump ---
            // Blocked while seated AND through the stand-up ramp: he gets to his feet first, so a
            // jump press that ends the sit cannot also launch him on the same frame.
            if (_mode == Trick.None && !_sitting && _sitDrop <= 0.02f)
            {
                // He faces his movement direction, so "moving" is enough to arm the dive
                // (it launches along that facing).
                bool moving = wish.sqrMagnitude > 0.16f;
                // Only accumulate hold-time while grounded AND holding; leaving the
                // ground (any jump) resets it, so chained taps can never build into a
                // dive - the dive needs a continuous grounded hold.
                if (_input.JumpHeld && grounded) _spaceHeld += Time.deltaTime;
                else if (!grounded) _spaceHeld = 0f;

                if (grounded && moving)
                {
                    // Moving: distinguish a tap (jump) from a hold (diving header).
                    if (_input.JumpHeld && _spaceHeld >= SimConfig.DiveHoldTime)
                        StartDive();
                    else if (_input.JumpReleased && _spaceHeld < SimConfig.DiveHoldTime)
                    { NormalJump(); _spaceHeld = 0f; }
                }
                else if (_input.JumpPressed && grounded)
                {
                    // Standing still: jump straight up immediately (tap or hold).
                    NormalJump();
                    _spaceHeld = 0f;
                }
            }

            if (_mode == Trick.Dive) ManageDive(grounded);
            // SlideLimp must be routed AWAY from AirPitchControl: its grounded branch sets
            // BalanceEnabled = true whenever balance is off, which would fight the limp on its very
            // first frame. Every other _mode gate in this method already tests == Trick.None and so
            // excludes SlideLimp on its own; this dispatch and TrickActive were the only two that did
            // not. The upright re-lock at the bottom of Tick is one of the gated ones, which is what
            // stops him being pinned upright the instant the limp starts.
            else if (_mode == Trick.SlideLimp) ManageSlideLimp();
            else if (_mode == Trick.Tumble) ManageTumble(grounded);
            else AirPitchControl(grounded);   // mouse-wheel body pitch while airborne

            // Re-lock upright only in normal state, grounded, past the jump grace. (Not
            // while airborne - the mouse wheel is controlling his pitch there.)
            if (_mode == Trick.None && _airborneLock <= 0f && grounded && !_ragdoll.UprightLock && !_sitting)
                _ragdoll.UprightLock = true;

            // Leg control (LMB/RMB) works the same grounded OR airborne - bicycle kicks
            // come from raising legs while the wheel pitches him back.
            //
            // ORDER MATTERS NOW. The gait runs FIRST and writes the base pose (already scaled down
            // per limb by how far that limb is raised), then the raises ADD on top. The gait is also
            // called unconditionally, with "allowed" false when airborne or off the upright lock, so
            // a grounding flicker fades the run out over a few frames instead of snapping the legs
            // to rest for one.
            if (_mode == Trick.None)
            {
                bool seated = _sitting || _sitDrop > 0.02f;
                RunCycle(grounded && _ragdoll.UprightLock && !seated);
                if (seated)
                {
                    // Bleed any half-built raise out so ApplyLegRaises resumes from rest once he
                    // is back on his feet, instead of popping a stale raise back in.
                    float kb = SimConfig.LegRaiseEase * Time.deltaTime;
                    _legRaiseL = Mathf.MoveTowards(_legRaiseL, 0f, kb);
                    _legRaiseR = Mathf.MoveTowards(_legRaiseR, 0f, kb);
                    _headerBend = Mathf.MoveTowards(_headerBend, 0f, kb);
                }
                else ApplyLegRaises(grounded);
            }
        }

        // Mouse-wheel flips, ONLY while airborne. Scroll accumulates a TARGET lean angle
        // (about his central/right axis) that is CLAMPED to +/-90deg - parallel with the
        // ground. The whole body is spun toward that target and stops there, so scrolling
        // more once he is flat does nothing (no runaway spin). On the ground the upright
        // lock owns his orientation and the wheel does nothing.
        // Clear the air-pitch target + the Acrobat unwrapped-flip accumulator. Called on landing
        // and on every recovery/trick-end path that zeroes air-pitch, so a flip can never carry
        // stale rotation into the next jump.
        void ResetFlipState()
        {
            _airPitchTarget = 0f;
            _flipAngle = 0f;
            _flipSeed = true;
        }

        void AirPitchControl(bool grounded)
        {
            if (grounded)
            {
                // Landed: a bicycle connects in the air, so drop the latch immediately -
                // otherwise a stale window would mislabel the next grounded normal kick.
                _bicycleTimer = 0f;
                // And the wheel with it, once this is a real landing (see _wheelArmed).
                if (_leftGround) { _wheelArmed = false; _leftGround = false; }
                if (_airPitchTarget != 0f || !_ragdoll.BalanceEnabled)
                {
                    // TIPPED OVER when the floor comes up - past TumbleUpness from upright, which
                    // is any real bicycle or a flip scrolled to horizontal - he goes DOWN, back or
                    // front first, and gets up (StartTumble). "grounded" here is the pelvis probe,
                    // which reads turf up to a metre before he touches it, so snapping upright on
                    // it (what this did for every landing) had a horizontal body pop to its feet in
                    // mid-air and never hit the ground. Acrobat keeps that: it chases a full
                    // rotation and lands on its feet by design, and that behaviour stays as it is.
                    bool acrobatic = Acrobat;
                    float upness = Vector3.Dot(_ragdoll.Pelvis.transform.up, Vector3.up);
                    if (!acrobatic && upness < SimConfig.TumbleUpness)
                    {
                        StartTumble();
                        return;
                    }
                    // On his feet (or near enough): stop the spin and hand orientation back to
                    // balance/upright lock.
                    ResetFlipState();
                    _ragdoll.StopBodySpin();
                    _ragdoll.BalanceEnabled = true;
                }
                _flipSeed = true;   // re-seed the unwrap for the next airborne window
                return;
            }

            // Free the whole body to tumble (upright lock/balance off).
            _leftGround = true;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.BodyOrientTarget = null;

            // Airborne: arm/refresh the latched bicycle window the moment he's committed to
            // the flip (tipped back or scrolling the air-pitch target back). Held open a
            // generous window so a foot contact anywhere through the flip reads as a bike.
            ArmBicycleWindow();

            // Scroll moves the TARGET lean. Default: clamped to +/-AirPitchLimit (~horizontal),
            // so scrolling past parallel does nothing (no runaway spin). ACROBAT: the clamp opens
            // to +/-AcrobatFlipLimit so scrolling drives the target past 180 and the body loops all
            // the way around into full forward/backward flips.
            bool acrobat = Acrobat;
            float targetLimit = acrobat ? SimConfig.AcrobatFlipLimit : SimConfig.AirPitchLimit;
            float scroll = _wheelArmed ? _input.Scroll : 0f;
            if (Mathf.Abs(scroll) > SimConfig.ScrollDeadzone)
                _airPitchTarget = Mathf.Clamp(_airPitchTarget + Mathf.Sign(scroll) * SimConfig.AirPitchStep,
                                              -targetLimit, targetLimit);

            // Current lean = signed angle of his right-axis pitch away from upright (wraps +/-180).
            float axisRoll = Vector3.SignedAngle(Vector3.up, _ragdoll.Pelvis.transform.up,
                                                 _ragdoll.FacingRotation * Vector3.right);
            // Maintain an UNWRAPPED accumulated angle by summing the per-frame delta across the
            // +/-180 seam. This is what lets a full 360+ flip be reachable (a wrapped error would
            // always ease to the nearest upright and never loop).
            if (_flipSeed) { _lastAxisRoll = axisRoll; _flipSeed = false; }
            else { _flipAngle += Mathf.DeltaAngle(_lastAxisRoll, axisRoll); _lastAxisRoll = axisRoll; }

            // Drive a spin velocity PROPORTIONAL to the remaining error every frame - eases to
            // zero as he nears the target (no hard switch to StopBodySpin at the edge). Acrobat
            // chases the UNWRAPPED angle (so it commits to the full rotation); default chases the
            // wrapped shortest-path error exactly as before.
            float err = acrobat ? (_airPitchTarget - _flipAngle)
                                : Mathf.DeltaAngle(axisRoll, _airPitchTarget);
            Vector3 spinAxis = _ragdoll.FacingRotation * Vector3.right;
            // Agility tree makes air control snappier: scale gain + cap by the flip mul.
            float flip = PlayerProfile.AirFlipMul;
            float w = Mathf.Clamp(err * SimConfig.AirPitchGain * flip,
                                  -SimConfig.AirPitchMaxSpeed * flip, SimConfig.AirPitchMaxSpeed * flip);
            _ragdoll.SpinWholeBody(spinAxis, w);   // w -> 0 smoothly as err -> 0
        }

        void NormalJump()
        {
            _spaceHeld = 0f;               // consumed -> next hold must re-accumulate
            _wheelArmed = true;            // the wheel is live from here until he lands
            _leftGround = false;
            _ragdoll.UprightLock = false;
            // Standing jumps go full height; jumps taken on the move are lower, and
            // sprinting jumps lower still (momentum trades against pop).
            bool moving = _input.Move.sqrMagnitude > 0.16f;
            float jumpVel = SimConfig.JumpVelocity * PlayerProfile.JumpMul;   // light/short jump highest
            if (moving)
            {
                jumpVel *= _input.SprintHeld ? SimConfig.SprintJumpMul : SimConfig.RunJumpMul;
                // Bleed off carried run momentum so a moving jump doesn't sail forward.
                _ragdoll.ScaleHorizontalVelocity(SimConfig.RunJumpForwardKeep);
            }
            _ragdoll.AddVelocityToAll(Vector3.up * jumpVel);
            _airborneLock = 0.35f;
        }

        // --------------------------------------------------- charged shot
        // HOLD a leg button, grounded, with the ball in range, to CHARGE a shot with that foot.
        // RELEASE fires; holding to FULL fires itself. LMB is the left boot, RMB the right.
        //
        // THERE IS NO NEW WINDUP ANIMATION. The existing _legRaiseL/_legRaiseR easing IS the charge
        // visual: the leg comes up as the charge builds, at LegRaiseEase 8/sec, and it already eases
        // back down with no snap-back on release. The only change ApplyLegRaises needed was to bring
        // a leg down after an AUTO-fire, where the button is still held.
        //
        // WHY GROUNDED-GATED. Airborne, ONE held leg button already means "raise that leg for a
        // bicycle kick" and BOTH mean "header pose" (the airborne branch of ApplyLegRaises), so a
        // charge that ran in the air would fight those poses for the same two buttons. Verified,
        // and it is the one hard input constraint on this system.
        //
        // WHAT DOES NOT CONFLICT, checked against the real code rather than assumed:
        //  - THE DIVING HEADER IS SPACE, not a leg button. Tick reads _input.JumpHeld/JumpReleased/
        //    JumpPressed for the jump-vs-dive split and _spaceHeld accumulates only on Space. An
        //    earlier review claimed the dive shared the leg buttons; it was wrong.
        //  - LMB+RMB grounded is sit (pulled back) or slide (pushed forward), and UpdateSit's
        //    `uncommitted` test refuses both while either _legRaise exceeds SitRaiseMax 0.5. At
        //    LegRaiseEase 8/sec a raise crosses 0.5 in 0.5/8 = 62 ms, so the interlock DOES hold -
        //    but only past that. Answering the question directly: yes, a part-charged shot can still
        //    become a slide, for the first 62 ms of the charge, which is 10% of PassMaxCharge 0.6 s.
        //    That is a feature and not a hole: a 62 ms twitch is a mis-click, not a shot you had
        //    committed to. Past it, the raised boot vetoes the slide, and the charge is disarmed by
        //    the both-held test below instead - so the second button is a deliberate CANCEL.
        BallController _ball;
        /// <summary>Wire the match ball. Preferred over the lazy find in UpdateShotCharge.</summary>
        public void SetBall(BallController b) => _ball = b;

        float _shotChargeL, _shotChargeR;
        bool _shotArmedL, _shotArmedR, _shotFiredL, _shotFiredR;
        bool _prevLegL, _prevLegR;   // for the press/release edges (IStrikerInput has no leg Released)

        /// <summary>0..1 charge of whichever foot is charging, for a HUD power bar.</summary>
        public float ShotCharge01 => Passing.Charge01(Mathf.Max(_shotChargeL, _shotChargeR));

        /// <summary>
        /// Whether a charge held RIGHT NOW would actually count. Passing.StepCharge zeroes charge on
        /// every tick canPlay is false (out of range, or busy), so ShotCharge01 reads 0 in both "not
        /// holding" and "holding, out of range" - a HUD reading only the charge cannot tell them
        /// apart, which is exactly how "the mechanic ran, drew nothing" looked from outside. Mirrors
        /// UpdateShotCharge's own gate so it cannot silently disagree with it.
        /// </summary>
        public bool ShotInRange
        {
            get
            {
                if (_ragdoll == null || _ragdoll.Pelvis == null) return false;
                if (_ball == null) _ball = FindAnyObjectByType<BallController>();
                if (_ball == null) return false;
                bool busy = _sitting || _sliding || _mode != Trick.None;
                return Passing.CanShoot(_ball, _ragdoll.Pelvis.position, busy);
            }
        }

        /// <summary>
        /// Live test: is this body holding exactly one leg button, grounded and free to strike?
        /// Read by BallController.ChargeOwnsShot to stop Dribble's press-edge flat release firing out
        /// from under a charge. Deliberately does NOT test ball range - the only caller already has
        /// the ball at the feet - so this stays free of the ball reference and cannot be stale.
        /// </summary>
        public bool WantsChargedShot
        {
            get
            {
                if (!ShootingEnabled || !ControlEnabled || _input == null || _ragdoll == null || _ragdoll.Pelvis == null) return false;
                if (!_ragdoll.IsGrounded || _sitting || _sliding || _mode != Trick.None) return false;
                return _input.LeftLegHeld != _input.RightLegHeld;   // exactly one: two is the gesture
            }
        }

        // A full bend needs him this far to the side of the ball-to-aim line at contact. Same ~68%-
        // of-the-gate proportion the old 0.9 m was against the old 1.32 m possession-gate radius,
        // rescaled to the shot gate's own tighter 0.57 m (Passing.CanShoot: BallRadius + ShotContact
        // Radius) - curling hard still means approaching from a real angle, which still costs time
        // and telegraphs, but "full" has to stay reachable inside the range a shot can fire from at
        // all now that shooting no longer shares passing's forgiveness radius.
        const float ShotCurlOffsetFull = 0.39f;

        // Shared probe buffer. Only touched from FireChargedShot, once per shot, synchronously.
        static readonly Collider[] _pressureHits = new Collider[24];

        void UpdateShotCharge(bool grounded)
        {
            // Edges from the HELD bits rather than IStrikerInput.LeftClickPressed: the interface has
            // no leg-button Released at all, and the network path already derives LeftClickPressed
            // from the same legL bit, so this is the identical edge by a shorter route and the local
            // and remote paths cannot drift apart.
            bool legL = _input.LeftLegHeld, legR = _input.RightLegHeld;
            bool pressL = legL && !_prevLegL, relL = !legL && _prevLegL;
            bool pressR = legR && !_prevLegR, relR = !legR && _prevLegR;
            _prevLegL = legL; _prevLegR = legR;

            // SetBall is the wiring a mode builder should use; the find is a backstop so this works
            // in every mode today without teaching each builder about it. Re-resolved while null,
            // which also covers a mode that swaps the ball object between rounds.
            if (_ball == null) _ball = FindAnyObjectByType<BallController>();

            bool gesture = legL && legR;                                 // sit / slide / header
            bool busy = _sitting || _sliding || _mode != Trick.None;
            bool inRange = _ball != null && _ragdoll.Pelvis != null
                           && Passing.CanShoot(_ball, _ragdoll.Pelvis.position, busy);
            bool canPlay = grounded && !gesture && !busy && inRange;

            // Reuse Passing.StepCharge rather than writing a second charger. It already owns the
            // arming (a hold that began without the ball never arms), the cap, the release
            // bookkeeping, and the `fresh` gate - and that gate is what makes fire-at-full safe over
            // a network: the host re-feeds the last received InputFrame every tick, so a client that
            // goes quiet leaves a held bit pinned true forever, and without freshness a dropped
            // connection would charge and fire a shot nobody asked for.
            bool fireL = Passing.StepCharge(legL, pressL, relL, canPlay, _input.Fresh,
                                            ref _shotChargeL, ref _shotArmedL, ref _shotFiredL, out float cL);
            bool fireR = Passing.StepCharge(legR, pressR, relR, canPlay, _input.Fresh,
                                            ref _shotChargeR, ref _shotArmedR, ref _shotFiredR, out float cR);

            // AUTO-FIRE AT FULL. StepCharge has this, behind SimConfig.PassAutoFireAtFull - a
            // compile-time const, and false, because PASSING is deliberately cap-and-wait. Flipping
            // it would change every pass in the game, so the shot's auto-release sits out here and
            // reuses everything else. The asymmetry is the point: a pass lets you hold at full and
            // pick your moment, a shot does not, so committing to max power costs you the timing.
            // The three writes match StepCharge's own fire-at-full branch exactly, so its release
            // branch still clears `fired` when the button comes up.
            if (!fireL && _shotArmedL && !_shotFiredL && _shotChargeL >= SimConfig.PassMaxCharge)
            { fireL = true; cL = 1f; _shotFiredL = true; _shotArmedL = false; _shotChargeL = 0f; }
            if (!fireR && _shotArmedR && !_shotFiredR && _shotChargeR >= SimConfig.PassMaxCharge)
            { fireR = true; cR = 1f; _shotFiredR = true; _shotArmedR = false; _shotChargeR = 0f; }

            // One boot per frame. Left wins a tie, which needs both pressed inside a frame of each
            // other - and that is the sit/slide gesture, which has already disarmed both above.
            if (fireL) FireChargedShot(true, cL);
            else if (fireR) FireChargedShot(false, cR);
        }

        void FireChargedShot(bool leftFoot, float charge01)
        {
            if (_ball == null || _ragdoll.Pelvis == null) return;

            // End the carry BEFORE the launch, the order Dribble.ReleaseShot uses, so our own carry
            // claim - which makes the carrier's own contacts non-strikes - cannot swallow the shot.
            if (_dribble != null && _dribble.Carrying) _dribble.ForceRelease();

            Vector3 bp = _ball.Rb.position;
            Vector3 me = _ragdoll.Pelvis.position;

            // AIM IS YAW ONLY. See LaunchChargedShot for why pitch is not read: CamPitchMin -6 deg
            // caps look-aim height at 0.105 * distance.
            float yaw = _camYaw != null ? _camYaw() : _facingYaw;
            Vector3 aimDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // CURL FROM WHICH SIDE OF THE BALL HE STRIKES. His flat offset from the ball along the
            // aim-right axis: standing right of the ball-to-aim line means the boot arrives on the
            // ball's right side. Sign follows the convention the strike path in BallController
            // already committed to ("bends the SAME way it was struck"). No button, no modifier -
            // the only way to curl hard is to come in at an angle.
            Vector3 right = Vector3.Cross(Vector3.up, aimDir);
            Vector3 off = me - bp; off.y = 0f;
            float curl01 = Mathf.Clamp(Vector3.Dot(off, right) / ShotCurlOffsetFull, -1f, 1f);

            Vector3 toGoal = SimConfig.AttackGoalCenter - bp; toGoal.y = 0f;
            float dist = toGoal.magnitude;
            float dot = toGoal.sqrMagnitude > 0.01f ? Vector3.Dot(aimDir, toGoal.normalized) : -1f;
            bool facingGoal = dot >= SimConfig.AssistFacingDot;
            bool camShouldCut = dot < SimConfig.ShotCamFaceAwayDot;

            // OFF-BALANCE: 8 m/s of ground speed and up reads as fully off balance. Planted is ~0.
            float offBalance = Mathf.Clamp01(_ragdoll.GroundSpeed / 8f);

            // OFF-FACING, the LARGER of two angles, because either alone misses a real case. While
            // grounded and not dribbling the body is pinned to the camera yaw, so body-vs-aim is ~0
            // and only MOMENTUM-vs-aim catches a shot struck across a sprint or a strafe. While
            // DRIBBLING the facing slews at a Control-scaled rate, so body-vs-aim is the one that
            // catches a whipped mouse followed straight by a strike.
            Vector3 mv = _ragdoll.MoveInput; mv.y = 0f;
            float momentumDeg = mv.sqrMagnitude > 0.25f ? Vector3.Angle(mv.normalized, aimDir) : 0f;
            float offFacingDeg = Mathf.Max(momentumDeg, Mathf.Abs(Mathf.DeltaAngle(_facingYaw, yaw)));

            bool weakFoot = !((leftFoot == PlayerProfile.LeftFooted) || PlayerProfile.PerkSilky);

            float scatter = BallController.ShotScatterDeg(
                charge01, dist, offBalance, offFacingDeg, ShotPressure01(me, aimDir), weakFoot,
                Passing.Accuracy01(PlayerProfile.ShotAccuracyMul, false));

            // Counted as a shot only when it is goal-directed, same rule Dribble.ReleaseShot uses,
            // so a backward tap-out is not filed as a shot on goal.
            if (facingGoal) Dribble.ShotFired?.Invoke(_ragdoll);
            _ball.LaunchChargedShot(aimDir, charge01, curl01, scatter, _ragdoll, facingGoal, camShouldCut);
        }

        // Defensive pressure on the strike, 0..1 over PassPressureRadius 3.5 m.
        //
        // A PROXIMITY PROBE, not a roster read, because Striker holds no team lists and should not
        // gain one for an error term - Passing.Pressure01 needs a List<Footballer> that nothing here
        // can supply. Two candid limits: ActiveRagdoll carries no team, so a TEAM-MATE standing on
        // top of him counts as pressure; and the buffer is 24 colliders, so a genuine scrum could
        // truncate. The forward-hemisphere test below cuts most of the first (a body behind you is
        // not pressuring your shot), and truncation only ever under-reports, which is the safe
        // direction. Handing Striker an opponent list would fix both properly - see the contract note.
        float ShotPressure01(Vector3 at, Vector3 aimDir)
        {
            int n = Physics.OverlapSphereNonAlloc(at, SimConfig.PassPressureRadius, _pressureHits,
                                                  ~0, QueryTriggerInteraction.Ignore);
            float best = SimConfig.PassPressureRadius;
            for (int i = 0; i < n; i++)
            {
                var c = _pressureHits[i];
                if (c == null) continue;
                var rag = c.GetComponentInParent<ActiveRagdoll>();
                if (rag == null || rag == _ragdoll || rag.Pelvis == null) continue;
                Vector3 d = rag.Pelvis.position - at; d.y = 0f;
                float m = d.magnitude;
                if (m < 1e-3f || m >= best) continue;
                if (Vector3.Dot(d / m, aimDir) < -0.2f) continue;   // behind him: not pressure
                best = m;
            }
            return 1f - Mathf.Clamp01(best / SimConfig.PassPressureRadius);
        }

        void ApplyLegRaises(bool grounded)
        {
            float k = SimConfig.LegRaiseEase * Time.deltaTime;

            if (grounded)
            {
                // On the ground: LMB/RMB raise the legs individually (kick setup), full lift. That
                // raise IS the shot charge visual - see UpdateShotCharge.
                //
                // A leg whose shot has already FIRED comes down even though the button is still
                // held. That case exists only because a full charge AUTO-FIRES: on a release-fire
                // the button is up and the raise falls out on its own, but after an auto-fire he
                // would otherwise stand with his boot in the air and the ball long gone.
                // _shotFired* stays true until the button comes up, which is exactly that window,
                // and it is false whenever the charge system is not engaged (no ball in range), so
                // holding a leg up for a volley or a bicycle is untouched.
                _headerBend = Mathf.MoveTowards(_headerBend, 0f, k);
                _legRaiseL = Mathf.MoveTowards(_legRaiseL, _input.LeftLegHeld  && !_shotFiredL ? 1f : 0f, k);
                _legRaiseR = Mathf.MoveTowards(_legRaiseR, _input.RightLegHeld && !_shotFiredR ? 1f : 0f, k);
            }
            else
            {
                // Airborne:
                //  - BOTH LMB+RMB = a HEADER: legs come forward only minimally and the
                //    torso leans pronouncedly forward. A short per-button grace window
                //    (GK-split-style) lets a few-ms-apart press still count as "both".
                //  - a SINGLE button = raise THAT leg fully (knee to chest) for a bicycle
                //    kick, independent of the other leg.
                if (_input.LeftLegHeld)  _lmbTimer = SimConfig.HeaderGrace;
                else if (_lmbTimer > 0f) _lmbTimer -= Time.deltaTime;
                if (_input.RightLegHeld)  _rmbTimer = SimConfig.HeaderGrace;
                else if (_rmbTimer > 0f) _rmbTimer -= Time.deltaTime;

                bool heading = _lmbTimer > 0f && _rmbTimer > 0f;
                if (heading)
                {
                    float legTarget = _header.LegRaiseMul;
                    _legRaiseL = Mathf.MoveTowards(_legRaiseL, legTarget, k);
                    _legRaiseR = Mathf.MoveTowards(_legRaiseR, legTarget, k);
                }
                else
                {
                    // Single leg SNAPS up fast and high for a bicycle kick (a much quicker
                    // ease-in than the grounded/header raise).
                    float ks = SimConfig.BicycleLegEase * Time.deltaTime;
                    _legRaiseL = Mathf.MoveTowards(_legRaiseL, _input.LeftLegHeld  ? SimConfig.BicycleLegRaiseMul : 0f, ks);
                    _legRaiseR = Mathf.MoveTowards(_legRaiseR, _input.RightLegHeld ? SimConfig.BicycleLegRaiseMul : 0f, ks);
                }
                // The pose snaps in fast when heading (quicker than the release ease-out). The rate
                // is per species: a human chest folds instantly, an elephant heaves a heavy barrel
                // against its own balance torque and wants a frame or two more.
                float kh = (heading ? _header.Ease : SimConfig.LegRaiseEase) * Time.deltaTime;
                _headerBend = Mathf.MoveTowards(_headerBend, heading ? 1f : 0f, kh);
            }

            if (_raiseL != null && _legRaiseL > 0.001f) RaiseLeg(_raiseL[0], _raiseL[1], _legRaiseL);
            if (_raiseR != null && _legRaiseR > 0.001f) RaiseLeg(_raiseR[0], _raiseR[1], _legRaiseR);
            if (_headerBend > 0.001f)
            {
                // Both channels are additive local +X, POST-multiplied onto the bone's REST
                // rotation, so the same sign folds a bone forward in its own rest frame whatever
                // that frame is. On a biped: the chest folds 90 and the head channel is unused (zero
                // Torso-relative, which is what a human header has always done). On a quadruped:
                // the barrel pitches nose-down a little and the head channel carries the rest, either
                // swinging the muzzle down with it (horse, positive) or slinging the trunk up against
                // it (elephant, negative).
                _ragdoll.AddPoseOverride(Bone.Torso, new Vector3(_header.TorsoDeg * _headerBend, 0f, 0f));
                if (_header.HeadDeg != 0f)
                    _ragdoll.AddPoseOverride(Bone.Head, new Vector3(_header.HeadDeg * _headerBend, 0f, 0f));
            }
        }

        // Lift one strike limb: a biped's leg, a quadruped's FRONT leg. Same maths for both, because
        // both rest upright in the body frame, so a negative X throws the lower end forward and up.
        void RaiseLeg(Bone upper, Bone lower, float amount)
        {
            // Cap the upper at 90deg (limb straight out horizontal) - that's max reach for
            // bicycle contact; past 90 it tucks back toward the body and loses coverage.
            float upperDeg = Mathf.Min(SimConfig.LegSwingRaise * amount, SimConfig.LegRaiseMaxDeg);
            // ADDITIVE, on top of whatever the gait left. The gait already scaled this limb's stride
            // by (1 - amount), so the two sum to one continuous pose across the whole raise and the
            // whole release. Overwriting was the release pop.
            _ragdoll.AddPoseOverride(upper, new Vector3(-upperDeg, 0f, 0f));
            _ragdoll.AddPoseOverride(lower, new Vector3(20f * amount, 0f, 0f));
        }

        // The run, per body plan, from the shared table in Gait. Everything species-specific lives
        // there; this only owns the phase and the fade weight.
        //
        // Three things changed and each one killed a specific piece of jank:
        //   CADENCE comes from MEASURED speed, not from input magnitude. Tapping a key used to run
        //   the legs at full rate over a body still accelerating from a standstill.
        //   THE PHASE IS NEVER RESET. It used to snap to zero any time the stick came near centre,
        //   which teleported both legs to rest between two steps. The weight fades instead.
        //   A RAISED LIMB IS SCALED, not skipped. Skipping handed the limb over on one frame and
        //   took it back on another; scaling crosses smoothly in both directions.
        void RunCycle(bool allowed)
        {
            var p = Gait.For(_ragdoll.Plan);
            float speed = _ragdoll.GroundSpeed;
            _gaitWeight = Gait.Weight(_gaitWeight, speed, allowed, Time.deltaTime);

            float sprint01;
            _gaitPhase += Time.deltaTime * Gait.Cadence(speed, _ragdoll.HeightScale, p, out sprint01);
            if (_gaitPhase > Mathf.PI * 2f) _gaitPhase -= Mathf.PI * 2f;

            var over = _gaitScratch;
            for (int i = 0; i < over.Length; i++) over[i] = Vector3.zero;
            Gait.Pose(over, p, _gaitPhase, _gaitWeight, sprint01, _legRaiseL, _legRaiseR);
            // Written unconditionally, zeros included: Tick already cleared the overrides, and the
            // raises below ADD to whatever is here.
            for (int i = 0; i < over.Length; i++) _ragdoll.SetPoseOverride((Bone)i, over[i]);
        }


        // --------------------------------------------------- diving header
        // Starts like a NORMAL JUMP: an up + forward launch off the run. From there he
        // just follows the ballistic arc under plain gravity, carrying that same momentum,
        // and belly-flops when he lands. Pelvis yaw+roll are pinned and pitch is driven
        // face-down (DiveYawLock) so he is belly-first the whole way; locomotion off so
        // the launch isn't steered/arrested.
        // `high`: the header-hold landing dive (HeaderDiveUpVel/ForwardVel) instead of the Space
        // dive's low, long launch. Same lifecycle either way - ManageDive, the prone clock, EndTrick.
        void StartDive(bool high = false)
        {
            _mode = Trick.Dive;
            _spaceHeld = 0f;
            _diveAir = 0f;
            _airHold = false;
            _wheelArmed = false;   // a dive never reads the wheel, and it ends on the ground
            DiveHitPending = true;   // this dive may still fell one opponent
            _proneTimer = Mathf.Max(SimConfig.DiveProneMinTime,
                                    SimConfig.DiveProneTime * PlayerProfile.RecoveryTimeMul);
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;   // preserve the launch, don't steer it

            // Belly-down orientation held throughout; go limp so the spine can't fight it.
            _ragdoll.DiveYawFacing = _ragdoll.FacingRotation;
            _ragdoll.DiveLayoutPitch = SimConfig.DiveLayoutPitch;
            _ragdoll.DiveYawLock = true;
            _ragdoll.DriveScale = SimConfig.DiveDriveScale;

            // Kill carried run momentum first so the dive is a controlled short hop, not
            // run-speed + launch (which sent him flying). Then a modest up + forward
            // launch; gravity arcs him into the flop.
            _ragdoll.ScaleHorizontalVelocity(0f);
            // The landing dive launches off touchdown with the fall still in the bones: kill that
            // too, or the drop nets most of the pop away and "high" comes out as a belly-flop.
            if (high) _ragdoll.ScaleVerticalVelocity(0f);
            Vector3 fwd = _ragdoll.FacingRotation * Vector3.forward;
            float up  = high ? SimConfig.HeaderDiveUpVel      : SimConfig.DiveUpVel;
            float fwv = high ? SimConfig.HeaderDiveForwardVel : SimConfig.DiveForwardVel;
            _ragdoll.AddVelocityToAll(Vector3.up * up + fwd * fwv);
        }

        // --------------------------------------------------- header hold through touchdown
        // Airborne, LMB+RMB together is the header pose (ApplyLegRaises). If the player is STILL
        // holding both when the ground comes up, the hold is resolved here rather than falling
        // through to the grounded raise branch, which cocked both legs at full lift under a run
        // that carried on - the "sitting in mid-air" look. Two outcomes, keyed the way the grounded
        // gesture is (SimConfig.BothButtonMoveDeadzone):
        //   - pushing FORWARD: he lays out into a HIGH diving header off the landing and has to
        //     recover from the deck like any dive (StartDive(high: true));
        //   - anything else (still, pulled back, strafing): he goes straight down into the sit,
        //     exactly as if he had pressed both while standing idle (BeginSit).
        // The hold must be CARRIED from the air: a fresh press on the ground is still UpdateSit's
        // (press-edge sit or slide), and letting either button go in the air clears it, so a
        // header you released before landing does nothing on touchdown.
        void ResolveHeaderLanding(bool grounded)
        {
            bool bothHeld = _input.LeftLegHeld && _input.RightLegHeld;
            if (!bothHeld) { _airHold = false; return; }
            bool free = _mode == Trick.None && !_sitting && !_sliding;
            if (!free) return;
            if (!grounded) { _airHold = true; return; }
            if (!_airHold) return;

            _airHold = false;
            // Coming down TIPPED (a scrolled flip with both buttons down) is AirPitchControl's
            // tumble, which runs later this same tick; a sit started here first would leave him
            // seated AND tumbling, with the sit's height pin under a limp body.
            float upness = Vector3.Dot(_ragdoll.Pelvis.transform.up, Vector3.up);
            if (upness < SimConfig.TumbleUpness) return;
            if (_input.Move.y > SimConfig.BothButtonMoveDeadzone) StartDive(high: true);
            else BeginSit();
        }

        void ManageDive(bool grounded)
        {
            _diveAir += Time.deltaTime;
            if (!grounded)
            {
                // Light reach forward + trailing legs. The body is limp (DiveDriveScale)
                // and the pelvis pitch is driven face-down by DiveYawLock, so this just
                // shapes the pose slightly; it can't hold him upright.
                _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(15f, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(25f, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(25f, 0f, 0f));
            }
            else if (_diveAir > 0.15f && (_proneTimer -= Time.deltaTime) <= 0f)
            {
                EndTrick();
            }
        }

        void EndTrick()
        {
            _mode = Trick.None;
            _spaceHeld = 0f;
            _wheelArmed = false;   // every trick ends on the deck: nothing to pitch until the next jump
            _leftGround = false;
            ResetFlipState();
            _ragdoll.DiveYawLock = false;
            _ragdoll.DriveScale = 1f;      // stiffen back up
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;   // pop back to his feet
            // Hand the height back unconditionally. The dive never sets these, but the slide's limp
            // phase routes through here and a non-zero EmoteHeightOffset PD-pins the pelvis to a
            // fixed height every frame - the exact trap Knockdown.Fell documents. Clearing it here
            // as well as at limp start makes the restore idempotent, so an EndTrick from any path
            // leaves a body that can stand.
            _sitDrop = 0f;
            _ragdoll.EmoteHeightOffset = 0f;
            _facingYaw = _ragdoll.FacingRotation.eulerAngles.y;
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        // --------------------------------------------------- slide tail: limp, then up
        // The end of a sliding challenge, and deliberately the SAME mechanism as the diving header
        // rather than a parallel one: DriveScale down, upright/balance/locomotion off, one countdown
        // on _proneTimer, and EndTrick as the single restore. Everything that already recovers a dive
        // therefore already recovers this - ForceRecover zeroes _mode and _proneTimer and restores
        // all four flags, and Knockdown.Fell tears it down via IsBusy.
        //
        // It does NOT hand off to Knockdown.Fell, which was the tempting shortcut. Fell calls
        // ForceRecover on a busy striker and then applies KnockdownImpulse 5.5 m/s plus an upward pop
        // and a spin: that is "felled by a tackle", not "the slide finished", and it would re-launch
        // him at the moment he is supposed to be running out of momentum.
        void StartSlideLimp()
        {
            _sliding = false;
            _slideTimer = 0f;
            _mode = Trick.SlideLimp;

            // Hand the height back BEFORE going limp. While EmoteHeightOffset is non-zero the pelvis
            // is PD-driven to a fixed height every frame, which would pin a limp body at slide height
            // instead of letting it settle (Knockdown.cs:33-41 hit the same thing with a felled
            // sitter). Zeroing _sitDrop also keeps UpdateSit's ramp-down branch writing -0f from here
            // on, so nothing re-applies the drop.
            _sitDrop = 0f;
            _ragdoll.EmoteHeightOffset = 0f;

            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;   // already false from the launch; idempotent
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.DriveScale = SimConfig.SlideLimpDriveScale;
            // Blend out of the Slide pose while limp so EndTrick's SetPose(Stand) has nothing left to
            // do and there is no pop on the way up. At this DriveScale the pose is barely enforced
            // anyway; it just stops the legs being authored splayed when he stands.
            _ragdoll.SetPose(RagdollPose.Stand, SimConfig.SitPoseSpeed);

            float limp = Mathf.Max(SimConfig.SlideLimpMinTime,
                                   SimConfig.SlideLimpTime * PlayerProfile.RecoveryTimeMul);
            _proneTimer = limp;
            // SlideRecover is documented as "s after standing up before he can slide again", and the
            // lockout bleeds down every frame including through the limp, so it has to cover the limp
            // too. Set at limp START rather than in EndTrick because EndTrick is shared with the dive
            // and a diving header should not arm a slide lockout.
            _slideRecover = limp + SimConfig.SlideRecover;
        }

        // Deliberately NOT gated on grounded, unlike ManageDive: a striker shoved off the deck as the
        // slide ends would otherwise stay limp forever with no input that could recover him. A limp
        // that always ends after a bounded time is worth more than one that waits for a landing.
        void ManageSlideLimp()
        {
            if ((_proneTimer -= Time.deltaTime) <= 0f) EndTrick();
        }

        // --------------------------------------------------- tumble: landed tipped over
        // A body that comes down to the turf tipped past TumbleUpness - a bicycle kick, a flip
        // scrolled to horizontal - goes DOWN, back or front first, and gets up the way a diving
        // header does: limp, upright/balance/locomotion off, one countdown, EndTrick restores. The
        // same mechanism as the dive and the slide's limp for the same reason those share it: every
        // recovery path already knows it (ForceRecover, Knockdown.Fell via IsBusy).
        //
        // Nothing here moves a bone by hand. The fall is the physics - continuous collision on every
        // bone against the turf slab, FloorRescue as the last-resort backstop - and the carry servo
        // and upright lock that put a body INTO the ground before are both off until EndTrick, when
        // the servo lifts him off a floor it has actually seen (_floorValid). So this cannot drive
        // him through the turf, the goal or anything else.
        void StartTumble()
        {
            _mode = Trick.Tumble;
            _tumbleTime = 0f;
            _spaceHeld = 0f;
            _bicycleTimer = 0f;
            _wheelArmed = false;
            _leftGround = false;
            ResetFlipState();
            _proneTimer = Mathf.Max(SimConfig.DiveProneMinTime,
                                    SimConfig.TumbleProneTime * PlayerProfile.RecoveryTimeMul);
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.DriveScale = SimConfig.TumbleDriveScale;
            // The spin he arrived with is kept (no StopBodySpin): the fall carries it, as a real one
            // would. Raises and the header fold are dropped so a stale one cannot pop back in when
            // he stands.
            _legRaiseL = 0f; _legRaiseR = 0f; _headerBend = 0f;
            _ragdoll.SetPose(RagdollPose.Stand, SimConfig.SitPoseSpeed);
        }

        void ManageTumble(bool grounded)
        {
            _tumbleTime += Time.deltaTime;
            // The prone clock runs once he has had time to actually land (the probe reads grounded
            // with the pelvis still a metre up); the hard cap covers a body that never settles.
            bool landed = grounded && _tumbleTime > 0.25f;
            if ((landed && (_proneTimer -= Time.deltaTime) <= 0f) || _tumbleTime > SimConfig.TumbleMaxTime)
                EndTrick();
        }

        // --------------------------------------------------- sit down
        // LMB+RMB pressed TOGETHER while standing puts him on his backside. Four conditions keep
        // it from stealing an ordinary strike:
        //   - it fires on the SECOND button's PRESS EDGE with the other side's edge still inside
        //     SitWindow, so press-one, swing, press-other stays two normal leg raises;
        //   - no shot is being CHARGED (a boot held with the ball in range is a committed strike).
        //     This used to test the cosmetic leg raise against SitRaiseMax instead, and at
        //     LegRaiseEase 8/s a bare raise crossed 0.5 in 62 ms - so unless both buttons landed
        //     inside 62 ms the sit was refused, which is why it was near-impossible to do on purpose;
        //   - GROUNDED only. Airborne both-down is the header and is left exactly as it was;
        //   - NO MOVEMENT held on the stick (pulled back also counts). The same combo pushed
        //     FORWARD is a sliding challenge instead, so the stick is the discriminator and the two
        //     are mutually exclusive by intent rather than by how fast he happened to be running
        //     (which is what SitMaxSpeed used to arbitrate, and why neither was reachable deliberately).
        //
        // The hips are dropped through EmoteHeightOffset, which is the shipped lever for this: a
        // non-zero value hands the whole-body carry servo off and PD-drives the pelvis to its
        // captured rest height plus the offset. UprightLock stays ON - it constrains pelvis
        // rotation only, never position, so he sinks without ever being able to topple.
        void UpdateSit(bool grounded)
        {
            if (_lmbDownT > 0f) _lmbDownT = Mathf.Max(0f, _lmbDownT - Time.deltaTime);
            if (_rmbDownT > 0f) _rmbDownT = Mathf.Max(0f, _rmbDownT - Time.deltaTime);
            bool lEdge = _input.LeftClickPressed, rEdge = _input.RightClickPressed;
            if (lEdge) _lmbDownT = SimConfig.SitWindow;
            if (rEdge) _rmbDownT = SimConfig.SitWindow;

            if (_slideRecover > 0f) _slideRecover = Mathf.Max(0f, _slideRecover - Time.deltaTime);

            bool bothHeld = _input.LeftLegHeld && _input.RightLegHeld;
            float push = _input.Move.y;                 // + forward, - back, in camera space

            // A slide COMMITS: it runs on its own timer and neither a release nor a change of stick
            // cancels it, because a real sliding challenge cannot be taken back halfway. Handled
            // before the sit so a slide in progress owns the body outright.
            if (_sliding)
            {
                _slideTimer -= Time.deltaTime;
                _ragdoll.MoveInput = Vector3.zero;                  // no steering once he is down
                // Skids to a stop, framerate-independently. SlideFriction is authored per 60 Hz frame
                // and this runs in Update, so a raw multiply made the slide's length a function of the
                // monitor: measured 3.34 m at 30 fps down to 0.69 m at 240. Raised to dt*60 it holds
                // 2.24-2.32 m across 30-240 fps. (There is a residual half-tick wobble because
                // AddVelocityToAll's pending VelocityChange may not have been integrated yet when the
                // first multiply lands. It favours travel and is under a percent; not worth chasing.)
                _ragdoll.ScaleHorizontalVelocity(Mathf.Pow(SimConfig.SlideFriction, Time.deltaTime * 60f));
                _sitDrop = Mathf.MoveTowards(_sitDrop, SimConfig.SlideDrop * _ragdoll.HeightScale,
                                             SimConfig.SitDropEase * 2f * Time.deltaTime);
                if (_slideTimer <= 0f || !grounded)
                {
                    // He does NOT snap upright any more: he goes limp and gets up, the way a diving
                    // header does. Airborne still ends the travelling phase, and going limp is the
                    // better answer there too (a slide off a step should tumble, not pop upright).
                    // StartSlideLimp owns the hip drop and the EmoteHeightOffset teardown, so return
                    // straight out rather than falling through to the write below.
                    StartSlideLimp();
                    return;
                }
                _ragdoll.EmoteHeightOffset = -_sitDrop;
                return;
            }

            if (!_sitting)
            {
                bool together = (lEdge || rEdge) && _lmbDownT > 0f && _rmbDownT > 0f;
                bool uncommitted = !_shotArmedL && !_shotArmedR;   // not charging a shot (see above)
                bool armed = together && bothHeld && uncommitted && grounded
                             && _mode == Trick.None && !_input.JumpHeld;

                // FORWARD -> slide. Lunge along the way he is facing, drop the pose, start the clock.
                if (armed && push > SimConfig.BothButtonMoveDeadzone && _slideRecover <= 0f)
                {
                    _sliding = true;
                    _slideTimer = SimConfig.SlideDuration;
                    _lmbDownT = 0f; _rmbDownT = 0f;
                    _ragdoll.MoveInput = Vector3.zero;
                    // THIS is why the slide never travelled. Zeroing MoveInput alone is not enough:
                    // ApplyLocomotion keeps running and reads zero as "brake to a standstill", which
                    // is StrikerAccel 22 x StrikerMoveSpeed 3.8 = 83.6 m/s^2 of deceleration on every
                    // bone with a 45 ms time constant. Measured (fixedDeltaTime 0.014, from a 6.5 m/s
                    // launch): 0.268 m of travel from the brake alone, 0.206 m with the old per-frame
                    // friction on top - against the ~2.3 m the friction alone would allow. StartDive
                    // already does exactly this ("preserve the launch, don't steer it"); the slide
                    // just never copied it. EndTrick/ForceRecover both restore it.
                    _ragdoll.LocomotionEnabled = false;
                    Vector3 fwd = Quaternion.Euler(0f, _facingYaw, 0f) * Vector3.forward;
                    // Keep the run he arrives with - a tackle out of a sprint SHOULD go further than
                    // one from a standstill - but cap the total (see SimConfig.SlideLaunchMax).
                    // Computed as a SCALAR against the pre-lunge speed rather than by measuring after
                    // the fact, because AddVelocityToAll is AddForce(VelocityChange) and Unity defers
                    // that to the next physics step: GroundSpeed read immediately afterwards still
                    // reports the old velocity. Treating the carried run as if it were all along fwd
                    // over-estimates the result when he is strafing, so the cap errs toward
                    // under-adding, which is the safe direction.
                    // NOTE the cap has to SCALE, not just add less. AddVelocityToAll is additive, so
                    // clamping only the ADDED amount does nothing once the carried run already exceeds
                    // the ceiling: at that point the added term is zero and he rides the full arrival
                    // speed. A Pace build crosses 12 m/s at traitSpeed 1.75, well short of maxed, so
                    // that is reachable - it would have given 6.97 m of slide at SprintSpeedCeiling
                    // 19.7 while also deleting the lunge kick entirely, which is a worse feel than
                    // either behaviour on its own.
                    float carried = _ragdoll.GroundSpeed;
                    float launch = Mathf.Min(carried + SimConfig.SlideLunge, SimConfig.SlideLaunchMax);
                    if (carried > launch && carried > 1e-4f)
                        _ragdoll.ScaleHorizontalVelocity(launch / carried);   // brake to the ceiling
                    else
                        _ragdoll.AddVelocityToAll(fwd * (launch - carried));
                    _ragdoll.SetPose(RagdollPose.Slide, SimConfig.SlidePoseSpeed);
                    _ragdoll.EmoteHeightOffset = -_sitDrop;
                    return;
                }
                // NO MOVEMENT (a still stick, or pulled back) -> sit. Strafing is neither.
                if (armed && push <= SimConfig.BothButtonMoveDeadzone
                    && Mathf.Abs(_input.Move.x) <= SimConfig.BothButtonMoveDeadzone)
                    BeginSit();
            }
            else if (!bothHeld || _input.JumpPressed || !grounded)
            {
                // Up on either button releasing, on a jump press, or the instant he leaves the deck.
                StandUp();
            }

            if (_sitting)
            {
                _ragdoll.MoveInput = Vector3.zero;   // no walking around on his backside
                _sitDrop = Mathf.MoveTowards(_sitDrop, SimConfig.SitDrop * _ragdoll.HeightScale,
                                             SimConfig.SitDropEase * Time.deltaTime);
            }
            else if (_sitDrop > 0f)
                _sitDrop = Mathf.MoveTowards(_sitDrop, 0f, SimConfig.SitDropEase * Time.deltaTime);

            // Reaching exactly 0 is what hands the carry servo back and lifts him to stand height.
            _ragdoll.EmoteHeightOffset = -_sitDrop;
        }

        // Down on his backside. Shared by the standing gesture (UpdateSit) and the header hold
        // carried through touchdown (ResolveHeaderLanding), so both sits are the same sit.
        void BeginSit()
        {
            _sitting = true;
            _airHold = false;
            _lmbDownT = 0f; _rmbDownT = 0f;
            _ragdoll.MoveInput = Vector3.zero;
            _ragdoll.UprightLock = false;     // let the pelvis tilt so the butt reaches the turf
            _ragdoll.BalanceEnabled = false;   // balance PD drives toward upright (pitch=0), fighting the sit
            _ragdoll.SetPose(RagdollPose.Sit, SimConfig.SitPoseSpeed);
        }

        void StandUp()
        {
            _sitting = false;
            _lmbDownT = 0f; _rmbDownT = 0f;
            _ragdoll.UprightLock = true;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.SetPose(RagdollPose.Stand, SimConfig.SitPoseSpeed);
        }

        public void ForceRecover()
        {
            _mode = Trick.None;
            _airborneLock = 0f;
            _proneTimer = 0f;
            _spaceHeld = 0f;
            ResetFlipState();
            _legRaiseL = 0f;
            _legRaiseR = 0f;
            _headerBend = 0f;
            _lmbTimer = 0f;
            _rmbTimer = 0f;
            _sitting = false;
            _sliding = false;
            _airHold = false;
            _wheelArmed = false;
            _leftGround = false;
            _slideTimer = 0f;
            _slideRecover = 0f;
            _runLocked = false;
            _runLockT = 0f;
            _lmbDownT = 0f;
            _rmbDownT = 0f;
            _sitDrop = 0f;
            _ragdoll.EmoteHeightOffset = 0f;
            _ragdoll.UprightLock = true;
            _gaitPhase = 0f;
            _gaitWeight = 0f;
            _ragdoll.DiveYawLock = false;
            _ragdoll.DriveScale = 1f;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            // UprightLock too. It was missing, and normally that is invisible because Tick re-locks on
            // the next grounded frame - but ForceRecover exists precisely for the cases where Tick
            // never runs again (control handed to the AI, roster change, match end), and then nothing
            // restores it: neither Footballer nor MatchGame ever writes UprightLock. Knockdown
            // .Recover sets it explicitly, which is why that path never showed the hole.
            _ragdoll.UprightLock = true;
            _ragdoll.LocomotionEnabled = true;
            // And STOP him. MoveInput is only ever written by Tick (line ~297), so a body recovered
            // at a moment Tick stops running keeps whatever steering it held on its last live frame
            // and walks off under locomotion with a frozen gait - the striker who "freezes and
            // slides" when a goal cuts to the replay hold, where GameManager suspends his Tick for
            // ReplayHold seconds and then the replay's own kinematic freeze takes over. Clearing it
            // here covers every ForceRecover caller, all of which are handing the body away.
            _ragdoll.MoveInput = Vector3.zero;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }
    }
}
