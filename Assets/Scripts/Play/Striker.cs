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
        enum Trick { None, Dive }

        IStrikerInput _input;
        ActiveRagdoll _ragdoll;
        System.Func<float> _camYaw;
        Dribble _dribble;   // optional; when carrying, movement slows + facing slews (Control claws both back)

        public bool ControlEnabled = true;

        public void SetDribble(Dribble d) => _dribble = d;

        Trick _mode = Trick.None;
        // True while a diving header is in progress (for the DIVING HEADER goal callout).
        public bool IsDiving => _mode == Trick.Dive;
        // Busy with a trick (dive, etc.): the Dribble system suspends the leash while true.
        public bool IsBusy => _mode != Trick.None;
        // Is the leg-raise button for this side held? LMB raises the LEFT leg, RMB the RIGHT.
        // A volley only fires off a leg the player is deliberately raising, so just RUNNING
        // into a flying ball (fast gait swing, no button) never counts as a swing/volley.
        public bool LegRaiseHeld(bool leftSide)
            => _input != null && (leftSide ? _input.LeftLegHeld : _input.RightLegHeld);
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
        public bool TrickActive => _bicycleTimer > 0f && _mode != Trick.Dive
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
        float _airborneLock;   // grace after a normal jump before upright re-locks
        float _proneTimer;     // while >0 (counting down on the ground), stay in the trick
        float _airPitchTarget; // wheel-driven target lean (deg) about the right axis; clamped to +/-90
        float _legRaiseL, _legRaiseR;   // eased 0..1 leg-raise amounts (no snap-back on release)
        float _headerBend;              // eased 0..1 torso-forward amount for the airborne header
        float _lmbTimer, _rmbTimer;     // per-button grace windows; header needs both live at once

        // Diving header lifecycle.
        float _spaceHeld;      // how long Space held while grounded (tap vs hold-to-dive)
        float _diveAir;        // time since the dive started

        public void Init(IStrikerInput input, ActiveRagdoll ragdoll)
        {
            _input = input;
            _ragdoll = ragdoll;
            _facingYaw = ragdoll.FacingRotation.eulerAngles.y;
        }

        // Swap the input source at runtime (e.g. the host binding a remote player's net
        // input to this body). Callers keep the ragdoll; only the input changes.
        public void SetInput(IStrikerInput input) => _input = input;

        public void SetCameraYaw(System.Func<float> camYaw) => _camYaw = camYaw;

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

            Quaternion yawRot = Quaternion.Euler(0f, camYaw, 0f);
            Vector3 camFwd = yawRot * Vector3.forward;
            Vector3 camRight = yawRot * Vector3.right;

            Vector2 mv = _input.Move;                     // x = strafe, y = forward
            Vector3 wish = Vector3.ClampMagnitude(camFwd * mv.y + camRight * mv.x, 1f);
            // Build traits: lighter/shorter = quicker; sprint is weighted separately.
            float traitSpeed = _input.SprintHeld ? PlayerProfile.SprintSpeedMul : PlayerProfile.MoveSpeedMul;
            float speed = SimConfig.StrikerMoveSpeed * (_input.SprintHeld ? SimConfig.StrikerSprintMul : 1f) * traitSpeed;

            // While dribbling, the striker is SLOWER and turns SLOWER - Control claws both
            // back. dribbling = the Dribble component is actively carrying the ball.
            bool dribbling = _dribble != null && _dribble.Carrying;
            if (dribbling)
            {
                float t = PlayerProfile.DribbleTightness;  // 0 (no Control) .. 1 (full)
                speed *= Mathf.Lerp(SimConfig.DribbleMoveMulLow, SimConfig.DribbleMoveMulHigh, t);
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
                    _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, camYaw, turnRate * Time.deltaTime);
                }
                else _facingYaw = camYaw;
                _ragdoll.FacingRotation = Quaternion.Euler(0f, _facingYaw, 0f);
            }

            // --- trigger tricks / jump ---
            if (_mode == Trick.None)
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
            else AirPitchControl(grounded);   // mouse-wheel body pitch while airborne

            // Re-lock upright only in normal state, grounded, past the jump grace. (Not
            // while airborne - the mouse wheel is controlling his pitch there.)
            if (_mode == Trick.None && _airborneLock <= 0f && grounded && !_ragdoll.UprightLock)
                _ragdoll.UprightLock = true;

            // Leg control (LMB/RMB) works the same grounded OR airborne - bicycle kicks
            // come from raising legs while the wheel pitches him back. Run cycle only when
            // grounded and locked upright.
            if (_mode == Trick.None)
            {
                ApplyLegRaises(grounded);
                if (grounded && _ragdoll.UprightLock) RunCycle(wish.magnitude);
            }
        }

        // Mouse-wheel flips, ONLY while airborne. Scroll accumulates a TARGET lean angle
        // (about his central/right axis) that is CLAMPED to +/-90deg - parallel with the
        // ground. The whole body is spun toward that target and stops there, so scrolling
        // more once he is flat does nothing (no runaway spin). On the ground the upright
        // lock owns his orientation and the wheel does nothing.
        void AirPitchControl(bool grounded)
        {
            if (grounded)
            {
                // Landed: a bicycle connects in the air, so drop the latch immediately -
                // otherwise a stale window would mislabel the next grounded normal kick.
                _bicycleTimer = 0f;
                // Stop the spin and hand orientation back to balance/upright lock.
                if (_airPitchTarget != 0f || !_ragdoll.BalanceEnabled)
                {
                    _airPitchTarget = 0f;
                    _ragdoll.StopBodySpin();
                    _ragdoll.BalanceEnabled = true;
                }
                return;
            }

            // Free the whole body to tumble (upright lock/balance off).
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.BodyOrientTarget = null;

            // Airborne: arm/refresh the latched bicycle window the moment he's committed to
            // the flip (tipped back or scrolling the air-pitch target back). Held open a
            // generous window so a foot contact anywhere through the flip reads as a bike.
            ArmBicycleWindow();

            // Scroll moves the TARGET lean, clamped to +/-90 (parallel). Past that the
            // scroll is ignored, so he holds flat instead of spinning on.
            float scroll = _input.Scroll;
            if (Mathf.Abs(scroll) > SimConfig.ScrollDeadzone)
                _airPitchTarget = Mathf.Clamp(_airPitchTarget + Mathf.Sign(scroll) * SimConfig.AirPitchStep,
                                              -SimConfig.AirPitchLimit, SimConfig.AirPitchLimit);

            // Current lean = signed angle of his right-axis pitch away from upright. Drive
            // a spin velocity PROPORTIONAL to the remaining error every frame - this eases
            // to zero as he nears the target instead of hard-switching between full-spin
            // and StopBodySpin at the edge (which caused the wobble at the range end).
            float axisRoll = Vector3.SignedAngle(Vector3.up, _ragdoll.Pelvis.transform.up,
                                                 _ragdoll.FacingRotation * Vector3.right);
            float err = Mathf.DeltaAngle(axisRoll, _airPitchTarget);
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

        void ApplyLegRaises(bool grounded)
        {
            float k = SimConfig.LegRaiseEase * Time.deltaTime;

            if (grounded)
            {
                // On the ground: LMB/RMB raise the legs individually (kick setup), full lift.
                _headerBend = Mathf.MoveTowards(_headerBend, 0f, k);
                _legRaiseL = Mathf.MoveTowards(_legRaiseL, _input.LeftLegHeld  ? 1f : 0f, k);
                _legRaiseR = Mathf.MoveTowards(_legRaiseR, _input.RightLegHeld ? 1f : 0f, k);
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
                    float legTarget = SimConfig.HeaderLegRaiseMul;
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
                // Torso snaps forward fast when heading (quicker than the release ease-out).
                float kh = (heading ? SimConfig.HeaderBendEase : SimConfig.LegRaiseEase) * Time.deltaTime;
                _headerBend = Mathf.MoveTowards(_headerBend, heading ? 1f : 0f, kh);
            }

            if (_legRaiseL > 0.001f) RaiseLeg(Bone.ThighL, Bone.CalfL, _legRaiseL);
            if (_legRaiseR > 0.001f) RaiseLeg(Bone.ThighR, Bone.CalfR, _legRaiseR);
            if (_headerBend > 0.001f)
                _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(SimConfig.HeaderTorsoBend * _headerBend, 0f, 0f));
        }

        void RaiseLeg(Bone thigh, Bone calf, float amount)
        {
            // Cap the thigh at 90deg (leg straight out horizontal) - that's max reach for
            // bicycle contact; past 90 it tucks back toward the body and loses coverage.
            float thighDeg = Mathf.Min(SimConfig.LegSwingRaise * amount, SimConfig.LegRaiseMaxDeg);
            _ragdoll.SetPoseOverride(thigh, new Vector3(-thighDeg, 0f, 0f));
            _ragdoll.SetPoseOverride(calf, new Vector3(20f * amount, 0f, 0f));
        }

        // Human-ish run: thighs alternate fore/aft, the knee of the SWING (forward)
        // leg bends hard to pick the foot up, foot dorsiflexes. Stance leg stays straight.
        void RunCycle(float moveAmount)
        {
            if (moveAmount < 0.05f) { _gaitPhase = 0f; return; }
            bool sprint = _input.SprintHeld;
            // Sprinting quickens the cadence and lifts/folds the legs more.
            float rate = SimConfig.StrideRateMax * (sprint ? SimConfig.SprintStrideMul : 1f);
            _gaitPhase += Time.deltaTime * rate * moveAmount;

            GaitLeg(Bone.ThighL, Bone.CalfL, Bone.FootL, _gaitPhase, _input.LeftLegHeld, sprint);
            GaitLeg(Bone.ThighR, Bone.CalfR, Bone.FootR, _gaitPhase + Mathf.PI, _input.RightLegHeld, sprint);

            // Contralateral swing: RIGHT arm forward as the LEFT leg comes forward (and
            // vice versa). The leg thigh swings as -sin(phase) but GaitArm swings as
            // +sin(phase), so to move the right arm WITH the left leg it takes the
            // opposite phase, and the left arm takes the left-leg phase.
            GaitArm(Bone.UpperArmR, Bone.ForearmR, _gaitPhase + Mathf.PI);
            GaitArm(Bone.UpperArmL, Bone.ForearmL, _gaitPhase);

            float bob = Mathf.Sin(_gaitPhase * 2f) * 2.5f;
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(SimConfig.GaitTorsoLean + bob, 0f, 0f));
        }

        // Runner's arm carriage: the upper arm swings fore/aft, elbow held bent.
        void GaitArm(Bone upper, Bone fore, float phase)
        {
            float sw = Mathf.Sin(phase);
            _ragdoll.SetPoseOverride(upper, new Vector3(sw * SimConfig.ArmPumpSwing, 0f, 0f));
            _ragdoll.SetPoseOverride(fore, new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
        }

        void GaitLeg(Bone thigh, Bone calf, Bone foot, float phase, bool heldByPlayer, bool sprint)
        {
            if (heldByPlayer) return;   // player raise owns this leg
            float sw = Mathf.Sin(phase);
            float lift = Mathf.Max(0f, sw);            // 1 through the forward swing, 0 in stance
            float hipLift = sprint ? SimConfig.SprintThighLift : SimConfig.GaitThighLift;
            float kneeBend = sprint ? SimConfig.SprintKneeBend : SimConfig.GaitKneeBend;
            // Thigh swings fore/aft; during the forward swing add extra hip lift AND a
            // hard knee fold so the foot clears the ground instead of dragging.
            float thighAngle = -sw * SimConfig.GaitThighSwing - lift * hipLift;
            _ragdoll.SetPoseOverride(thigh, new Vector3(thighAngle, 0f, 0f));
            _ragdoll.SetPoseOverride(calf, new Vector3(lift * kneeBend, 0f, 0f));
            _ragdoll.SetPoseOverride(foot, new Vector3(-sw * SimConfig.GaitFootPoint, 0f, 0f));
        }


        // --------------------------------------------------- diving header
        // Starts like a NORMAL JUMP: an up + forward launch off the run. From there he
        // just follows the ballistic arc under plain gravity, carrying that same momentum,
        // and belly-flops when he lands. Pelvis yaw+roll are pinned and pitch is driven
        // face-down (DiveYawLock) so he is belly-first the whole way; locomotion off so
        // the launch isn't steered/arrested.
        void StartDive()
        {
            _mode = Trick.Dive;
            _spaceHeld = 0f;
            _diveAir = 0f;
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
            Vector3 fwd = _ragdoll.FacingRotation * Vector3.forward;
            _ragdoll.AddVelocityToAll(Vector3.up * SimConfig.DiveUpVel
                                      + fwd * SimConfig.DiveForwardVel);
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
            _airPitchTarget = 0f;
            _ragdoll.DiveYawLock = false;
            _ragdoll.DriveScale = 1f;      // stiffen back up
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;   // pop back to his feet
            _facingYaw = _ragdoll.FacingRotation.eulerAngles.y;
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        public void ForceRecover()
        {
            _mode = Trick.None;
            _airborneLock = 0f;
            _proneTimer = 0f;
            _spaceHeld = 0f;
            _airPitchTarget = 0f;
            _legRaiseL = 0f;
            _legRaiseR = 0f;
            _headerBend = 0f;
            _lmbTimer = 0f;
            _rmbTimer = 0f;
            _gaitPhase = 0f;
            _ragdoll.DiveYawLock = false;
            _ragdoll.DriveScale = 1f;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }
    }
}
