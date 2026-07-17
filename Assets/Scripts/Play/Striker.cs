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
        float _diveCharge;     // how long Space+W held (grounded) toward triggering
        float _crouchTimer;    // brief knee-bend before the leap
        float _diveAir;        // time since the dive launched
        bool _diveLaunched;    // crouch done, in the air
        Quaternion _diveOrient;// held belly-down target (drives lay-out, no runaway spin)

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

            // Body faces the camera look direction while in normal control.
            if (_mode == Trick.None)
            {
                _facingYaw = camYaw;
                _ragdoll.FacingRotation = Quaternion.Euler(0f, _facingYaw, 0f);
            }

            // --- trigger tricks / jump ---
            if (_mode == Trick.None)
            {
                // Hold Space + W (grounded) to charge a diving header. A quick Space tap
                // (no W, or not held long enough) is a normal jump.
                bool holdingDiveInputs = _input.JumpHeld && _input.ForwardHeld && grounded;
                if (holdingDiveInputs)
                {
                    _diveCharge += Time.deltaTime;
                    if (_diveCharge >= SimConfig.DiveChargeTime) StartDive();
                }
                else
                {
                    _diveCharge = 0f;
                    if (_input.JumpPressed && grounded) NormalJump();
                    else if (_input.ReclineHeld && !grounded) StartRecline();
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
            _ragdoll.UprightLock = false;
            _ragdoll.AddVelocityToAll(Vector3.up * SimConfig.JumpVelocity);
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
            _gaitPhase += Time.deltaTime * SimConfig.StrideRateMax * moveAmount;

            GaitLeg(Bone.ThighL, Bone.CalfL, Bone.FootL, _gaitPhase, _input.LeftLegHeld);
            GaitLeg(Bone.ThighR, Bone.CalfR, Bone.FootR, _gaitPhase + Mathf.PI, _input.RightLegHeld);

            float bob = Mathf.Sin(_gaitPhase * 2f) * 2.5f;
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(SimConfig.GaitTorsoLean + bob, 0f, 0f));
        }

        void GaitLeg(Bone thigh, Bone calf, Bone foot, float phase, bool heldByPlayer)
        {
            if (heldByPlayer) return;   // player raise owns this leg
            float sw = Mathf.Sin(phase);
            float lift = Mathf.Max(0f, sw);            // 1 through the forward swing, 0 in stance
            // Thigh swings fore/aft; during the forward swing add extra hip lift AND a
            // hard knee fold so the foot clears the ground instead of dragging.
            float thighAngle = -sw * SimConfig.GaitThighSwing - lift * SimConfig.GaitThighLift;
            _ragdoll.SetPoseOverride(thigh, new Vector3(thighAngle, 0f, 0f));
            _ragdoll.SetPoseOverride(calf, new Vector3(lift * SimConfig.GaitKneeBend, 0f, 0f));
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
        // Phase 1: crouch (still grounded, upright, bending knees) for a beat, then
        // launch. _diveLaunched flips false->true when the crouch ends.
        void StartDive()
        {
            _mode = Trick.Dive;
            _diveCharge = 0f;
            _crouchTimer = SimConfig.DiveCrouchTime;
            _diveLaunched = false;
            _diveAir = 0f;
            _proneTimer = SimConfig.DiveProneTime;
            // Belly-down target: facing pitched forward about the right axis so he lands
            // face/chest down. Driven+held (no runaway spin).
            Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
            _diveOrient = Quaternion.AngleAxis(SimConfig.DiveLayoutDeg, axis) * _ragdoll.FacingRotation;
            // Stay upright & locked during the crouch so he loads before leaving ground.
            _ragdoll.SetPose(RagdollPose.Load, 16f);
        }

        void ManageDive(bool grounded)
        {
            if (!_diveLaunched)
            {
                // Crouch: bend the knees briefly, still on the ground and upright.
                _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-SimConfig.DiveCrouchKnee * 0.6f, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-SimConfig.DiveCrouchKnee * 0.6f, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.CalfL, new Vector3(SimConfig.DiveCrouchKnee, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.CalfR, new Vector3(SimConfig.DiveCrouchKnee, 0f, 0f));
                _crouchTimer -= Time.deltaTime;
                if (_crouchTimer <= 0f) LaunchDive();
                return;
            }

            _diveAir += Time.deltaTime;
            if (!grounded)
            {
                // Drive-and-HOLD the belly-down orientation (no constant spin), so he
                // arcs over and lands chest-first, then stays down.
                _ragdoll.BodyOrientTarget = _diveOrient;
                _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(15f, 0f, 0f));   // reach forward
                _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(25f, 0f, 0f));  // legs trail
                _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(25f, 0f, 0f));
            }
            else if (_diveAir > 0.2f && (_proneTimer -= Time.deltaTime) <= 0f)
            {
                EndTrick();
            }
        }

        // Phase 2: leap up and forward in an arc.
        void LaunchDive()
        {
            _diveLaunched = true;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
            Vector3 fwd = _ragdoll.FacingRotation * Vector3.forward;
            _ragdoll.AddVelocityToAll(fwd * SimConfig.DiveForwardVel + Vector3.up * SimConfig.DiveUpVel);
            _ragdoll.SetPose(RagdollPose.Stand, 16f);
        }

        void EndTrick()
        {
            _mode = Trick.None;
            _diveLaunched = false;
            _diveCharge = 0f;
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
            _diveCharge = 0f;
            _diveLaunched = false;
            _gaitPhase = 0f;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }
    }
}
