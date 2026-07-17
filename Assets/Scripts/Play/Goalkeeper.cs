using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// AI goalkeeper for STRIKER mode: an active-ragdoll keeper (with gloves) that
    /// actually goaltends. He shuffles along the line to shadow the ball, and when a
    /// shot comes in far enough off-centre he commits a lateral DIVE (lunge + layout),
    /// reusing the same whole-body impulse + held-orientation the player keeper uses.
    /// Tracking sharpness and dive reach scale with SimConfig.KeeperAbility.
    ///
    /// Driven by Tick() from GameManager (not FixedUpdate), so it stays in lockstep with
    /// the rest of the striker-mode loop.
    /// </summary>
    public class Goalkeeper : MonoBehaviour
    {
        ActiveRagdoll _ragdoll;
        BallController _ball;
        Quaternion _facing;
        float _homeX;

        enum State { Guard, Diving }
        State _state = State.Guard;
        float _diveAir, _diveGround, _diveCooldown, _diveDir;
        float _shufflePhase;

        // Where the keeper actually is (his pelvis), for the save-proximity check.
        public Vector3 PelvisPos => _ragdoll != null && _ragdoll.Pelvis != null
                                    ? _ragdoll.Pelvis.position : transform.position;

        public void Init(ActiveRagdoll ragdoll, BallController ball)
        {
            _ragdoll = ragdoll;
            _ball = ball;
            // Faces out toward the pitch (same as the player keeper).
            _facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            _ragdoll.FacingRotation = _facing;
        }

        public void Tick()
        {
            if (_ragdoll.Pelvis == null || _ball == null) return;
            _ragdoll.ClearPoseOverrides();
            if (_diveCooldown > 0f) _diveCooldown -= Time.deltaTime;

            if (_state == State.Diving) { ManageDive(); return; }

            _ragdoll.FacingRotation = _facing;

            float ability = Mathf.Clamp01(SimConfig.KeeperAbility);
            Vector3 bpos = _ball.transform.position;
            float dz = bpos.z - _ragdoll.Pelvis.position.z;      // + = ball still out in the pitch
            bool incoming = dz < SimConfig.AiKeeperReactZ && dz > -1.5f && _ball.Rb.linearVelocity.z > 1f;

            // Where will the ball cross the line? Lead its x by its velocity so the dive
            // commits to the right side.
            float predictX = bpos.x;
            float vz = _ball.Rb.linearVelocity.z;
            if (vz > 0.5f)
            {
                float tToLine = Mathf.Clamp(dz / vz, 0f, SimConfig.AiKeeperDiveLead);
                predictX = bpos.x + _ball.Rb.linearVelocity.x * tToLine;
            }

            float halfGoal = SimConfig.GoalWidth * 0.5f - 0.4f;
            float offset = predictX - _homeX;

            // Commit to a dive when the shot is incoming, off-centre beyond the shuffle
            // window, and the ability roll says so (higher ability = dives more).
            if (incoming && Mathf.Abs(offset) > SimConfig.AiKeeperDiveThresh
                && _diveCooldown <= 0f && ability > 0.25f
                && Mathf.Abs(predictX) <= halfGoal + 1.2f)
            {
                LaunchDive(Mathf.Sign(offset), ability);
                return;
            }

            // Otherwise shuffle to shadow the (clamped) target x. Shuffle speed = the
            // keeper-speed slider scaled up by ability, so both sliders matter.
            float targetX = Mathf.Clamp(predictX, -halfGoal, halfGoal);
            float react = incoming ? 1f : 0.3f;
            float speed = SimConfig.KeeperStrafeSpeed * Mathf.Lerp(0.5f, 1.6f, ability) * react;
            Vector3 right = _facing * Vector3.right;
            float curX = _ragdoll.Pelvis.position.x;
            float dx = Mathf.Clamp(targetX - curX, -1f, 1f);
            _ragdoll.MoveInput = right * (dx * speed);

            _ragdoll.SetPose(KeeperPose.Ready, 8f);
            ShuffleGait(Mathf.Abs(dx));
        }

        void ShuffleGait(float moveAmt)
        {
            if (moveAmt < 0.15f) { _shufflePhase = 0f; return; }
            _shufflePhase += Time.deltaTime * SimConfig.KeeperShuffleRate * moveAmt;
            float s = Mathf.Sin(_shufflePhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-liftL * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.KeeperShuffleKnee, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-liftR * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.KeeperShuffleKnee, 0f, 0f));
        }

        void LaunchDive(float dir, float ability)
        {
            _state = State.Diving;
            _diveDir = dir;
            _diveAir = 0f; _diveGround = 0f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            Vector3 right = _facing * Vector3.right;
            Vector3 fwd = _facing * Vector3.forward;
            // Reach scales with the keeper-SPEED slider + ability; dive height scales with
            // the keeper-JUMP slider (relative to its 1.0x reference) + ability.
            float abilMul = Mathf.Lerp(0.6f, 1.15f, ability);
            float horiz = SimConfig.AiKeeperDiveHoriz * abilMul
                          * (SimConfig.KeeperStrafeSpeed / 5.5f);
            float up = SimConfig.AiKeeperDiveUp * abilMul
                       * (SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase);
            _ragdoll.AddVelocityToAll(right * (dir * horiz) + Vector3.up * up);

            // Held horizontal lay-out toward the dive side (same approach as player keeper).
            Quaternion layout = Quaternion.AngleAxis(-dir * SimConfig.KeeperDiveLayoutHigh, fwd) * _facing;
            _ragdoll.BodyOrientTarget = layout;
            _ragdoll.AddTorqueToPelvis(fwd * (-dir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        void ManageDive()
        {
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
            _diveAir += Time.deltaTime;
            bool grounded = _ragdoll.IsGrounded;
            bool landed = _diveAir > SimConfig.KeeperDiveMinAir && grounded;
            if (landed) _diveGround += Time.deltaTime; else _diveGround = 0f;
            if (_diveGround >= SimConfig.KeeperDiveSettle || _diveAir > SimConfig.KeeperDiveMaxTime)
                Recover();
        }

        void Recover()
        {
            _state = State.Guard;
            _facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.SnapFacing(_facing);
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;
            _ragdoll.SetPose(KeeperPose.Ready, 12f);
        }

        public void ResetTo(Vector3 basePos)
        {
            _homeX = 0f;
            _state = State.Guard;
            _diveCooldown = 0f;
            _ragdoll.ResetTo(new Vector3(0f, basePos.y, basePos.z),
                             Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up));
            _facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
        }
    }
}
