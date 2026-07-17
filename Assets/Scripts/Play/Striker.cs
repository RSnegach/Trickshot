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

        GameInput _input;
        ActiveRagdoll _ragdoll;
        System.Func<float> _camYaw;

        public bool ControlEnabled = true;

        Trick _mode = Trick.None;
        // Bicycle window for KickDetector: airborne and actually tipped away from upright
        // (read from the real pelvis, since the flip is now a whole-body spin). Below the
        // threshold his pelvis-up still points mostly skyward. KickDetector re-confirms.
        public bool TrickActive
        {
            get
            {
                if (_mode != Trick.None || _ragdoll.Pelvis == null || _ragdoll.IsGrounded) return false;
                float upness = Vector3.Dot(_ragdoll.Pelvis.transform.up, Vector3.up);
                return upness < SimConfig.BicycleUpnessMax;
            }
        }

        float _facingYaw;
        public float Yaw => _facingYaw;

        float _gaitPhase;
        float _airborneLock;   // grace after a normal jump before upright re-locks
        float _proneTimer;     // while >0 (counting down on the ground), stay in the trick
        float _airPitchVel;    // wheel-driven whole-body spin velocity (deg/s) while airborne

        // Diving header lifecycle.
        float _spaceHeld;      // how long Space held while grounded (tap vs hold-to-dive)
        float _diveAir;        // time since the dive started

        public void Init(GameInput input, ActiveRagdoll ragdoll)
        {
            _input = input;
            _ragdoll = ragdoll;
            _facingYaw = ragdoll.FacingRotation.eulerAngles.y;
        }

        public void SetCameraYaw(System.Func<float> camYaw) => _camYaw = camYaw;

        void Update()
        {
            if (_airborneLock > 0f)
                _airborneLock = Mathf.Max(0f, _airborneLock - Time.deltaTime);
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
            float speed = SimConfig.StrikerMoveSpeed * (_input.SprintHeld ? SimConfig.StrikerSprintMul : 1f);
            _ragdoll.MoveInput = wish * speed;

            // Body faces where the mouse points (the camera yaw), set directly. camYaw
            // only changes while the mouse moves, so facing freezes the instant the
            // mouse is still - he never turns on his own. WASD is relative to this
            // facing: W/S run forward/back along it, A/D shuffle sideways (strafe).
            if (_mode == Trick.None)
            {
                _facingYaw = camYaw;
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
                ApplyLegRaises();
                if (grounded && _ragdoll.UprightLock) RunCycle(wish.magnitude);
            }
        }

        // Mouse-wheel flips, ONLY while airborne. Scroll builds a WHOLE-BODY spin about
        // his central (right) axis so he visibly rotates as one piece (front/back flips).
        // Driving only the pelvis (the old way) just arched the spine against the joints -
        // it never actually flipped. On the ground the upright lock owns his orientation
        // and the wheel does nothing.
        void AirPitchControl(bool grounded)
        {
            if (grounded)
            {
                // Landed: stop the spin and hand orientation back to balance/upright lock.
                if (_airPitchVel != 0f || !_ragdoll.BalanceEnabled)
                {
                    _airPitchVel = 0f;
                    _ragdoll.StopBodySpin();
                    _ragdoll.BalanceEnabled = true;
                }
                return;
            }

            // Free the whole body to tumble (upright lock/balance off).
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.BodyOrientTarget = null;

            // Each scroll event adds a small FIXED spin increment by sign only (raw scroll
            // magnitude is huge and mouse-dependent - ~120/notch on Windows - so any
            // proportional gain instantly slams the cap). The LOW cap is what actually
            // prevents pinwheeling: it bounds top spin to well under a rotation/sec.
            // A free-spin wheel fires several events and ramps toward the cap; friction
            // settles it quickly.
            float scroll = _input.Scroll;
            if (Mathf.Abs(scroll) > SimConfig.ScrollDeadzone)
                _airPitchVel = Mathf.Clamp(_airPitchVel + Mathf.Sign(scroll) * SimConfig.AirPitchImpulse,
                                           -SimConfig.AirPitchMaxSpeed, SimConfig.AirPitchMaxSpeed);
            bool wasSpinning = Mathf.Abs(_airPitchVel) > 1f;
            _airPitchVel = Mathf.MoveTowards(_airPitchVel, 0f, SimConfig.AirPitchDamp * Time.deltaTime);

            // Apply as a whole-body spin about the character's right axis (pitch/flip).
            if (Mathf.Abs(_airPitchVel) > 1f)
            {
                Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
                _ragdoll.SpinWholeBody(axis, _airPitchVel);
            }
            else if (wasSpinning)
            {
                // Spin just decayed to zero mid-air: cancel the leftover orbital linear
                // velocity so he stops flipping cleanly instead of coasting in circles.
                _ragdoll.StopBodySpin();
            }
        }

        void NormalJump()
        {
            _spaceHeld = 0f;               // consumed -> next hold must re-accumulate
            _ragdoll.UprightLock = false;
            // Standing jumps go full height; jumps taken on the move are lower, and
            // sprinting jumps lower still (momentum trades against pop).
            bool moving = _input.Move.sqrMagnitude > 0.16f;
            float jumpVel = SimConfig.JumpVelocity;
            if (moving)
            {
                jumpVel *= _input.SprintHeld ? SimConfig.SprintJumpMul : SimConfig.RunJumpMul;
                // Bleed off carried run momentum so a moving jump doesn't sail forward.
                _ragdoll.ScaleHorizontalVelocity(SimConfig.RunJumpForwardKeep);
            }
            _ragdoll.AddVelocityToAll(Vector3.up * jumpVel);
            _airborneLock = 0.35f;
        }

        void ApplyLegRaises()
        {
            if (_input.LeftLegHeld)  RaiseLeg(Bone.ThighL, Bone.CalfL);
            if (_input.RightLegHeld) RaiseLeg(Bone.ThighR, Bone.CalfR);
        }

        void RaiseLeg(Bone thigh, Bone calf)
        {
            _ragdoll.SetPoseOverride(thigh, new Vector3(-SimConfig.LegSwingRaise, 0f, 0f));
            _ragdoll.SetPoseOverride(calf, new Vector3(20f, 0f, 0f));
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
            _proneTimer = SimConfig.DiveProneTime;
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
            _airPitchVel = 0f;
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
            _airPitchVel = 0f;
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
