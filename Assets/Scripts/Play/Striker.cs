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
    public class Striker : MonoBehaviour
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
            _ragdoll.MoveInput = wish * SimConfig.StrikerMoveSpeed;

            // Body faces the camera look direction while in normal control.
            if (_mode == Trick.None)
            {
                _facingYaw = camYaw;
                _ragdoll.FacingRotation = Quaternion.Euler(0f, _facingYaw, 0f);
            }

            // --- trigger tricks / jump ---
            if (_mode == Trick.None)
            {
                bool forward = mv.y > 0.5f;
                if (_input.JumpPressed && grounded)
                {
                    if (forward) StartDive();
                    else NormalJump();
                }
                else if (_input.ReclineHeld && !grounded)
                {
                    StartRecline();
                }
            }

            if (_mode == Trick.Recline) ManageRecline(grounded);
            else if (_mode == Trick.Dive) ManageDive(grounded);

            // Re-lock upright only in normal state, grounded, past the jump grace.
            if (_mode == Trick.None && _airborneLock <= 0f && grounded && !_ragdoll.UprightLock)
                _ragdoll.UprightLock = true;

            // Leg raises run AFTER trick pose so clicks visibly override the rest pose.
            ApplyLegRaises();

            if (_mode == Trick.None && grounded && _ragdoll.UprightLock)
                RunCycle(wish.magnitude);
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
            _ragdoll.SetPose(RagdollPose.Stand, 16f);
        }

        void ManageRecline(bool grounded)
        {
            // Rotate the WHOLE body backward TOWARD flat-on-the-back (~170 deg tilt),
            // then stop - no runaway multi-spin. Only steer while airborne.
            if (!grounded)
            {
                Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
                float remaining = SimConfig.ReclineTargetTilt - _ragdoll.TorsoTiltDegrees();
                _ragdoll.SpinWholeBodyToward(-axis, remaining, SimConfig.ReclineSpinRate);
            }

            // Torso tips back; legs sit cocked at rest (clicks raise them higher).
            _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(-SimConfig.ReclineTorsoLean, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-SimConfig.ReclineRestLeg, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-SimConfig.ReclineRestLeg, 0f, 0f));

            // Hold while E is down; when released, recover after lying briefly.
            if (_input.ReclineHeld)
                _proneTimer = SimConfig.ReclineProneTime;
            else if (grounded && (_proneTimer -= Time.deltaTime) <= 0f)
                EndTrick();
        }

        // ------------------------------------------------------------ dive header
        void StartDive()
        {
            _mode = Trick.Dive;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _proneTimer = SimConfig.DiveProneTime;
            Vector3 fwd = _ragdoll.FacingRotation * Vector3.forward;
            _ragdoll.AddVelocityToAll(fwd * SimConfig.DiveForwardVel + Vector3.up * SimConfig.DiveUpVel);
            _ragdoll.SetPose(RagdollPose.Stand, 16f);
        }

        void ManageDive(bool grounded)
        {
            if (!grounded)
            {
                // Rotate the WHOLE body forward so it lands belly-down, not on its feet
                // (forward pitch about the character's right axis).
                Vector3 axis = _ragdoll.FacingRotation * Vector3.right;
                _ragdoll.SpinWholeBody(axis, SimConfig.DiveSpinRate);
                _ragdoll.SetPoseOverride(Bone.Torso, new Vector3(20f, 0f, 0f));   // reach forward
                // Legs trail straight behind.
                _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(30f, 0f, 0f));
                _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(30f, 0f, 0f));
            }
            else if ((_proneTimer -= Time.deltaTime) <= 0f)
            {
                EndTrick();
            }
        }

        void EndTrick()
        {
            _mode = Trick.None;
            _ragdoll.BalanceEnabled = true;
            _facingYaw = _ragdoll.FacingRotation.eulerAngles.y;
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        public void ForceRecover()
        {
            _mode = Trick.None;
            _airborneLock = 0f;
            _proneTimer = 0f;
            _gaitPhase = 0f;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }
    }
}
