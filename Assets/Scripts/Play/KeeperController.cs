using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player-controlled goalkeeper (an active ragdoll with arms). Faces out toward the
    /// pitch and stays on the line.
    ///
    /// Controls:
    ///  - A / D .................. strafe sideways (and W/S in/out) for positioning.
    ///  - A/D + Space ............ upward dive; reach/height scale with prior speed.
    ///  - double-tap A / D ....... explosive low sideways dive just off the ground.
    ///  - Space (no direction) ... straight jump, arms up.
    ///  - LMB / RMB .............. one-time reflex lunge save, arm+leg out; auto-recovers
    ///                             even if held. Both = splayed split.
    /// All dives lay the body out horizontal and only get back up after landing.
    /// </summary>
    public class KeeperController : MonoBehaviour, IPlayerController
    {
        enum State { Ready, Saving, Diving }

        GameInput _input;
        ActiveRagdoll _ragdoll;
        Quaternion _facing;

        State _state = State.Ready;
        Vector3[] _airPose;   // pose held while in the air (Dive or Jump)

        // Dive lifecycle: landing detection.
        float _diveDir;       // -1 left / +1 right (for the leading-leg bend)
        Quaternion _diveOrient;  // held horizontal lay-out target for the current dive
        float _diveAir;       // time since dive launched
        float _diveGround;    // time spent settled on the ground after landing
        bool _diveIsJump;     // straight jump (stays upright) vs. lay-out dive
        float _saveReleaseTimer;

        // Double-tap detection for A/D dash dives.
        float _lastTapTime = -10f;
        float _lastTapDir;
        bool _dirWasDown;     // A or D held last frame (to detect fresh taps)

        public void Init(GameInput input, ActiveRagdoll ragdoll)
        {
            _input = input;
            _ragdoll = ragdoll;
            // Keeper faces out toward the pitch and never turns.
            _facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            _ragdoll.FacingRotation = _facing;
        }

        public void Tick()
        {
            if (_ragdoll.Pelvis == null) return;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.FacingRotation = _facing;   // never turns
            bool grounded = _ragdoll.IsGrounded;

            if (_state == State.Diving) { ManageDive(grounded); return; }
            if (_state == State.Saving) { ManageSave(); return; }

            Vector3 kRight = _facing * Vector3.right;   // keeper's right in world space

            // --- LMB / RMB reflex save: a lunge on the press edge, then he STAYS DOWN
            //     in the save pose for as long as any button is held (both = split).
            //     ManageSave holds/switches the pose and stands up on release. ---
            bool lmbClick = _input.LeftClickPressed;
            bool rmbClick = _input.RightClickPressed;
            if (lmbClick || rmbClick)
            {
                if (lmbClick && rmbClick) BeginSave(0f, kRight, KeeperPose.Split);
                else if (lmbClick)        BeginSave(-1f, kRight, KeeperPose.SaveLeft);
                else                      BeginSave(1f, kRight, KeeperPose.SaveRight);
                return;
            }

            float dir = _input.Move.x;                 // A = -1, D = +1 (his LEFT / RIGHT)
            float fb = _input.Move.y;                  // W = +1 forward, S = -1 back
            bool hasDir = Mathf.Abs(dir) > 0.4f;

            // --- Double-tap A or D = explosive LOW sideways dive, just off the ground. ---
            if (DetectDoubleTap(dir, hasDir))
            {
                LaunchDashDive(Mathf.Sign(dir));
                return;
            }

            // --- A/D + Space = upward dive; reach/height scale with prior speed. ---
            if (_input.JumpPressed && hasDir)
            {
                LaunchDive(Mathf.Sign(dir));
                return;
            }

            // Space with NO direction = straight jump up with arms up.
            if (_input.JumpPressed && grounded)
            {
                Jump();
                return;
            }

            // --- Normal: strafe/move relative to facing (covers sideways positioning). ---
            Move(dir, fb);
            _ragdoll.SetPose(KeeperPose.Ready, 8f);
        }

        // Fresh A/D tap this frame? Returns true only on a double-tap (two taps of the
        // same direction within the window).
        bool DetectDoubleTap(float dir, bool hasDir)
        {
            bool freshTap = hasDir && !_dirWasDown;
            _dirWasDown = hasDir;
            if (!freshTap) return false;

            float d = Mathf.Sign(dir);
            bool doubled = (Time.time - _lastTapTime) < SimConfig.KeeperDoubleTapWindow
                           && Mathf.Approximately(d, _lastTapDir);
            _lastTapTime = Time.time;
            _lastTapDir = d;
            if (doubled) { _lastTapTime = -10f; return true; }  // consume so it doesn't retrigger
            return false;
        }

        // Reflex save: one-time sideways lunge with arm+leg out, on his feet. Locomotion
        // steering is OFF so the lunge momentum carries him sideways instead of being
        // instantly arrested. Very short timer -> gets up immediately.
        Vector3[] _savePose;
        void BeginSave(float dir, Vector3 kRight, Vector3[] pose)
        {
            _state = State.Saving;
            _saveReleaseTimer = -1f;                 // -1 = still held
            _savePose = pose;
            _ragdoll.LocomotionEnabled = false;      // let the lunge carry
            _ragdoll.MoveInput = Vector3.zero;
            if (Mathf.Abs(dir) > 0.1f)
                _ragdoll.AddVelocityToAll(kRight * (dir * SimConfig.KeeperSaveLunge));
            _ragdoll.SetPose(pose, 16f);
        }

        void ManageSave()
        {
            bool lmb = _input.LeftLegHeld, rmb = _input.RightLegHeld;

            // Live-switch the held pose: both = split, else the one-sided reach.
            if (lmb || rmb)
            {
                _savePose = (lmb && rmb) ? KeeperPose.Split
                          : lmb ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                _saveReleaseTimer = -1f;             // stay down while held
            }
            _ragdoll.SetPose(_savePose, 16f);        // hold the reach

            // Released: brief settle, then stand.
            if (!lmb && !rmb)
            {
                if (_saveReleaseTimer < 0f) _saveReleaseTimer = SimConfig.KeeperSaveReleaseTime;
                _saveReleaseTimer -= Time.deltaTime;
                if (_saveReleaseTimer <= 0f) RecoverToReady();
            }
        }

        // Return to the ready stance, facing forward out toward the pitch.
        void RecoverToReady()
        {
            _state = State.Ready;
            _facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            _ragdoll.FacingRotation = _facing;   // face forward again after getting up
            _ragdoll.BodyOrientTarget = null;    // stop driving the dive lay-out
            _ragdoll.SnapFacing(_facing);        // hard-snap to forward (no wrong-way slew)
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;         // then keep it upright + facing
            _airPose = null;
            _ragdoll.SetPose(KeeperPose.Ready, 12f);
        }

        void Move(float dir, float fb)
        {
            Vector3 right = _facing * Vector3.right;      // keeper's right in world space
            Vector3 fwd = _facing * Vector3.forward;      // out toward the pitch
            Vector3 vel = right * (dir * SimConfig.KeeperStrafeSpeed)
                        + fwd * (fb * SimConfig.KeeperStrafeSpeed);

            // Clamp lateral shuffle to a window around centre (x only).
            float x = _ragdoll.Pelvis.position.x;
            if ((x > SimConfig.KeeperStrafeXLimit && vel.x > 0f) ||
                (x < -SimConfig.KeeperStrafeXLimit && vel.x < 0f))
                vel.x = 0f;

            _ragdoll.MoveInput = vel;
        }

        void Jump()
        {
            // Straight up, arms overhead. Stays upright (no lay-out), lands, gets up.
            _state = State.Diving;
            _diveIsJump = true;
            _diveAir = 0f; _diveGround = 0f;
            _airPose = KeeperPose.Jump;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
            _ragdoll.BodyOrientTarget = null;        // no active tilt; ballistic
            _ragdoll.LaunchVerticalAll(SimConfig.KeeperJumpVel);
            _ragdoll.SetPose(KeeperPose.Jump, 16f);
        }

        // A/D + Space: upward dive whose reach/height scale with how fast he was
        // already moving when Space was pressed (momentum carries into the dive).
        void LaunchDive(float dir)
        {
            float priorSpeed = new Vector3(_ragdoll.Pelvis.linearVelocity.x, 0f, _ragdoll.Pelvis.linearVelocity.z).magnitude;
            float horiz = SimConfig.KeeperDiveHorizBase + SimConfig.KeeperDiveHorizPerV * priorSpeed;
            float up = SimConfig.KeeperDiveUpBase + SimConfig.KeeperDiveUpPerV * priorSpeed;
            DoDive(dir, horiz, up, SimConfig.KeeperDiveLayoutHigh);
        }

        // Double-tap A/D: explosive LOW sideways dive, just off the ground (fixed).
        void LaunchDashDive(float dir)
        {
            DoDive(dir, SimConfig.KeeperDashDive, SimConfig.KeeperDashUp, SimConfig.KeeperDiveLayoutLow);
        }

        // Shared dive launch: sideways+up velocity, plus an ACTIVELY DRIVEN roll to a
        // rolled (near-)horizontal target that the ragdoll HOLDS - so he reliably reaches
        // that lay-out by the apex regardless of airtime (a one-shot impulse alone can't
        // guarantee "parallel at the high point"). Locomotion off so momentum carries.
        void DoDive(float dir, float horiz, float up, float layoutDeg)
        {
            _state = State.Diving;
            _diveIsJump = false;
            _diveDir = dir;
            _diveAir = 0f; _diveGround = 0f;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            Vector3 right = _facing * Vector3.right;
            Vector3 fwd = _facing * Vector3.forward;
            _ragdoll.AddVelocityToAll(right * (dir * horiz) + Vector3.up * up);

            // Target: facing rolled about the forward axis so the body lies flat ON the
            // dive side. Sign is -dir: diving right (dir=+1) must tip him onto his RIGHT
            // (the un-negated version tipped him the wrong way). Driven+held via BodyOrientTarget.
            _diveOrient = Quaternion.AngleAxis(-dir * layoutDeg, fwd) * _facing;
            _ragdoll.BodyOrientTarget = _diveOrient;

            // Initial roll kick in the same direction so the lay-out snaps in immediately.
            _ragdoll.AddTorqueToPelvis(fwd * (-dir * SimConfig.KeeperDiveRoll));

            _airPose = KeeperPose.Dive;
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        // Landing-gated recovery: hold the dive pose through the flight; only get up
        // AFTER he has come down and settled (fixes mid-air righting + cut-short reach).
        void ManageDive(bool grounded)
        {
            if (_airPose != null) _ragdoll.SetPose(_airPose, 16f);

            if (!_diveIsJump)
            {
                // Keep driving the horizontal lay-out the whole flight.
                _ragdoll.BodyOrientTarget = _diveOrient;

                // On a dive to one side he lands on that side, so the TOP leg (the one
                // opposite the dive direction) is the leading leg that folds up hard;
                // the bottom leg bends a little. (This is the flip of the earlier wiring
                // that read backwards.)
                Bone leadThigh = _diveDir < 0f ? Bone.ThighR : Bone.ThighL;
                Bone leadCalf  = _diveDir < 0f ? Bone.CalfR  : Bone.CalfL;
                Bone backThigh = _diveDir < 0f ? Bone.ThighL : Bone.ThighR;
                Bone backCalf  = _diveDir < 0f ? Bone.CalfL  : Bone.CalfR;
                _ragdoll.SetPoseOverride(leadThigh, new Vector3(-SimConfig.KeeperDiveLeadKnee * 0.5f, 0f, 0f));
                _ragdoll.SetPoseOverride(leadCalf,  new Vector3(SimConfig.KeeperDiveLeadKnee, 0f, 0f));
                _ragdoll.SetPoseOverride(backThigh, new Vector3(-SimConfig.KeeperDiveBackKnee * 0.4f, 0f, 0f));
                _ragdoll.SetPoseOverride(backCalf,  new Vector3(SimConfig.KeeperDiveBackKnee, 0f, 0f));
            }

            _diveAir += Time.deltaTime;

            // Consider him landed once he's been airborne a moment AND is back on the
            // ground (or the safety cap trips).
            bool landed = _diveAir > SimConfig.KeeperDiveMinAir && grounded;
            if (landed) _diveGround += Time.deltaTime; else _diveGround = 0f;

            if (_diveGround >= SimConfig.KeeperDiveSettle || _diveAir > SimConfig.KeeperDiveMaxTime)
            {
                RecoverToReady();
            }
        }

        public void ForceRecover()
        {
            _state = State.Ready;
            _saveReleaseTimer = -1f;
            _diveAir = 0f; _diveGround = 0f;
            _airPose = null;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(KeeperPose.Ready, 6f);
        }
    }
}
