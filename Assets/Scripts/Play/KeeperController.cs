using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player-controlled goalkeeper (an active ragdoll with arms). Faces out toward the
    /// pitch and stays on the line.
    ///
    /// Controls:
    ///  - A / D .................. strafe sideways along the goal line.
    ///  - A/D + Space (charge) ... dive in the A/D direction. How long SPACE is held
    ///                             before release = horizontal power; how long W is held
    ///                             (while charging) = height. So a top-corner save is
    ///                             W + Space + A/D held together for a while. Space alone
    ///                             (no direction) does nothing - no diving header.
    ///  - LMB (hold) ............. drop on the RIGHT knee, left leg + left arm out.
    ///  - RMB (hold) ............. mirror (left knee, right leg + right arm out).
    ///  - LMB + RMB (hold) ....... splayed split with both arms thrown out.
    /// </summary>
    public class KeeperController : MonoBehaviour, IPlayerController
    {
        enum State { Ready, Diving }

        GameInput _input;
        ActiveRagdoll _ragdoll;
        Quaternion _facing;

        State _state = State.Ready;
        float _diveDir;       // -1 left, +1 right
        float _recoverTimer;
        Vector3[] _airPose;   // pose held while in the air (Dive or Jump)

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

            // --- Saves (held) take priority over strafing, but not over a live dive. ---
            bool lmb = _input.LeftLegHeld;   // LMB
            bool rmb = _input.RightLegHeld;  // RMB
            if (lmb && rmb) { _ragdoll.SetPose(KeeperPose.Split, 14f); _ragdoll.MoveInput = Vector3.zero; return; }
            if (lmb)        { _ragdoll.SetPose(KeeperPose.SaveLeft, 14f); _ragdoll.MoveInput = Vector3.zero; return; }
            if (rmb)        { _ragdoll.SetPose(KeeperPose.SaveRight, 14f); _ragdoll.MoveInput = Vector3.zero; return; }

            float dir = _input.Move.x;                 // A = -1, D = +1 (his LEFT / RIGHT)
            float fb = _input.Move.y;                  // W = +1 forward, S = -1 back
            bool hasDir = Mathf.Abs(dir) > 0.4f;

            // --- Dive: EXPLOSIVE the instant Space is pressed while holding A or D.
            //     Direction from A/D; W held at the moment of press adds height. Space
            //     with no A/D direction is a straight jump, not a dive. ---
            if (_input.JumpPressed && hasDir)
            {
                LaunchDive(Mathf.Sign(dir), _input.ForwardHeld);
                return;
            }

            // Space with NO direction = straight jump up with arms up.
            if (_input.JumpPressed && grounded)
            {
                Jump();
                return;
            }

            // --- Normal: move relative to facing. A/D strafe (his left/right), W/S
            //     forward/back. Because he faces -Z, "his right" = world -X, which is
            //     why raw world +X felt reversed before. ---
            Move(dir, fb);
            _ragdoll.SetPose(KeeperPose.Ready, 8f);
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
            // Straight up, arms overhead. LaunchVerticalAll cancels horizontal velocity
            // so he goes STRAIGHT up (no backward drift). Reuse Diving-state recovery.
            _state = State.Diving;
            _recoverTimer = SimConfig.KeeperRecoverTime;
            _airPose = KeeperPose.Jump;
            _ragdoll.UprightLock = false;
            _ragdoll.LaunchVerticalAll(SimConfig.KeeperJumpVel);
            _ragdoll.SetPose(KeeperPose.Jump, 16f);
        }

        // Explosive one-shot dive the instant Space is pressed. dir = his left(-1)/right(+1),
        // highDive = was W held at press (top-corner). No charging.
        void LaunchDive(float dir, bool highDive)
        {
            _state = State.Diving;
            _recoverTimer = SimConfig.KeeperRecoverTime;
            _diveDir = dir;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;

            Vector3 right = _facing * Vector3.right;   // keeper's right in world space
            float up = SimConfig.KeeperDiveUpBase + (highDive ? SimConfig.KeeperDiveUp : 0f);
            Vector3 launch = right * (dir * SimConfig.KeeperDiveHoriz) + Vector3.up * up;
            _ragdoll.AddVelocityToAll(launch);

            // One-shot roll so he lays out horizontally toward the dive side.
            _ragdoll.AddTorqueToPelvis(right * (-dir * 12f));

            _airPose = KeeperPose.Dive;
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        void ManageDive(bool grounded)
        {
            if (_airPose != null) _ragdoll.SetPose(_airPose, 16f);
            // Count down always (a prone keeper may not report grounded); recover on
            // timeout so he reliably springs back to his feet. UprightLock snaps the
            // pelvis upright, so re-locking is what makes him pop up fast.
            _recoverTimer -= Time.deltaTime;
            if (_recoverTimer <= 0f)
            {
                _state = State.Ready;
                _ragdoll.BalanceEnabled = true;
                _ragdoll.UprightLock = true;
                _airPose = null;
                _ragdoll.SetPose(KeeperPose.Ready, 10f);
            }
        }

        public void ForceRecover()
        {
            _state = State.Ready;
            _recoverTimer = 0f;
            _airPose = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.UprightLock = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(KeeperPose.Ready, 6f);
        }
    }
}
