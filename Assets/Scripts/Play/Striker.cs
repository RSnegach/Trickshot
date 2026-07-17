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
    ///  - Space jumps. E (held, airborne) reclines onto his back (bicycle setup) and
    ///    STAYS down after landing for a moment. W+Space does a forward diving header,
    ///    landing belly-down and staying prone briefly. LMB/RMB raise the legs.
    ///
    /// Tricks (recline / dive) release the upright lock and keep it off through the
    /// landing via a prone timer, so the body actually rests flat instead of snapping
    /// upright.
    /// </summary>
    public class Striker : MonoBehaviour, IPlayerController
    {
        enum Trick { None, Recline, Dive }

        GameInput _input;
        ActiveRagdoll _ragdoll;
        System.Func<float> _camYaw;

        public bool ControlEnabled = true;

        Trick _mode = Trick.None;
        public bool TrickActive => _mode == Trick.Recline;   // bicycle window for KickDetector

        float _facingYaw;
        public float Yaw => _facingYaw;

        float _gaitPhase;
        float _airborneLock;   // grace after a normal jump before upright re-locks
        float _proneTimer;     // while >0 (counting down on the ground), stay in the trick

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

            // Body faces the MOVEMENT direction (not the mouse). Movement stays
            // camera-relative; the mouse only orbits the camera. Standing still, he
            // keeps his last facing. Turn smoothly toward the wish direction.
            if (_mode == Trick.None && wish.sqrMagnitude > 0.02f)
            {
                float wishYaw = Mathf.Atan2(wish.x, wish.z) * Mathf.Rad2Deg;
                _facingYaw = Mathf.MoveTowardsAngle(_facingYaw, wishYaw,
                                                    SimConfig.TurnRateDeg * Time.deltaTime);
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

                if (!_input.JumpHeld)
                {
                    _spaceHeld = 0f;
                    if (_input.ReclineHeld && !grounded) StartRecline();
                }
            }

            if (_mode == Trick.Recline) ManageRecline(grounded);
            else if (_mode == Trick.Dive) ManageDive(grounded);

            // Re-lock upright only in normal state, grounded, past the jump grace.
            if (_mode == Trick.None && _airborneLock <= 0f && grounded && !_ragdoll.UprightLock)
                _ragdoll.UprightLock = true;

            // Leg raises + run cycle only in normal control (not mid-dive/recline, whose
            // pose overrides would otherwise be clobbered).
            if (_mode == Trick.None)
            {
                ApplyLegRaises();
                if (grounded && _ragdoll.UprightLock) RunCycle(wish.magnitude);
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
                jumpVel *= _input.SprintHeld ? SimConfig.SprintJumpMul : SimConfig.RunJumpMul;
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

            float bob = Mathf.Sin(_gaitPhase * 2f) * 2.5f;
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(SimConfig.GaitTorsoLean + bob, 0f, 0f));
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

        // ------------------------------------------------------------ recline
        void StartRecline()
        {
            _mode = Trick.Recline;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _proneTimer = SimConfig.ReclineProneTime;

            // ONE-SHOT backward angular impulse to the pelvis only (the approach the
            // old F bicycle used, which worked). Because the pelvis is jointed to the
            // rest of the body, that connected mass resists and brakes the spin, so the
            // body flips onto its back and STOPS - no runaway. Setting a whole-body
            // angular velocity (the previous attempt) had nothing to brake it and spun
            // forever. The Bicycle pose auto-lifts the kicking leg.
            Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
            _ragdoll.AddTorqueToPelvis(-axis * SimConfig.ReclineImpulse);
            _ragdoll.SetPose(RagdollPose.Bicycle, 16f);
        }

        void ManageRecline(bool grounded)
        {
            // The one-shot impulse in StartRecline does the rotation; nothing to drive
            // per-frame. Just hold until E is released and we have settled on the ground.
            if (_input.ReclineHeld)
                _proneTimer = SimConfig.ReclineProneTime;
            else if (grounded && (_proneTimer -= Time.deltaTime) <= 0f)
                EndTrick();
        }

        // --------------------------------------------------- diving header
        // He JUMPS first: an up + forward launch off the run, then tips forward into a
        // belly-down header. The pelvis yaw+roll are pinned (DiveYawLock) so the chest
        // stays square-forward the whole way and never twists. Lands, then recovers.
        void StartDive()
        {
            _mode = Trick.Dive;
            _spaceHeld = 0f;
            _diveAir = 0f;
            _proneTimer = SimConfig.DiveProneTime;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;   // let the launch carry, don't steer it

            // Chest stays facing forward: pin pelvis yaw+roll to the current facing and
            // drive the pitch face-down. Go limp so the stiff spine can't hold him upright.
            _ragdoll.DiveYawFacing = _ragdoll.FacingRotation;
            _ragdoll.DiveLayoutPitch = SimConfig.DiveLayoutPitch;
            _ragdoll.DiveYawLock = true;
            _ragdoll.DriveScale = SimConfig.DiveDriveScale;

            // Jump: up + forward off the run direction, added on top of run momentum.
            Vector3 fwd = _ragdoll.FacingRotation * Vector3.forward;
            _ragdoll.AddVelocityToAll(Vector3.up * SimConfig.DiveUpVel + fwd * SimConfig.DiveForwardVel);

            // One-shot forward-tilt torque about the right axis -> tips into the header.
            Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
            _ragdoll.AddTorqueToPelvis(axis * SimConfig.DiveForwardImpulse);
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
