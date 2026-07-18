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

        // True if he is diving now or dived very recently (for the EPIC SAVE callout).
        public bool WasDivingSave => _state == State.Diving || _diveCooldown > SimConfig.AiKeeperDiveCooldown - 0.6f;

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

            // Predict the ball height as it reaches the line (ballistic, from current vy).
            float predictY = bpos.y;
            if (vz > 0.5f)
            {
                float tToLine = Mathf.Max(0f, dz / vz);
                predictY = bpos.y + _ball.Rb.linearVelocity.y * tToLine + 0.5f * Physics.gravity.y * tToLine * tToLine;
            }
            bool lowBall = predictY < SimConfig.AiKeeperLowBallHeight;

            float absOff = Mathf.Abs(offset);
            bool canCommit = incoming && _diveCooldown <= 0f && ability > 0.25f
                             && Mathf.Abs(predictX) <= halfGoal + 1.2f;

            if (canCommit)
            {
                if (lowBall)
                {
                    // LOW ball:
                    //  - within splay reach of the keeper -> Split (central) / SaveLeft-Right
                    //    splay in place: spread down low, no big movement.
                    //  - further out (toward a bottom corner) -> a LOW DIVE: drive down and
                    //    across. He steps toward it first (shuffle below), only committing
                    //    the dive once it's within diveable range, so he "takes a step or two".
                    if (absOff <= SimConfig.AiKeeperSplayReach)
                    {
                        LaunchLowSave(Mathf.Sign(offset), ability);
                        return;
                    }
                    if (absOff <= SimConfig.AiKeeperLowDiveReach)
                    {
                        LaunchLowDive(Mathf.Sign(offset), ability);
                        return;
                    }
                    // Too far to reach yet: fall through to shuffle a step toward it.
                }
                else if (absOff > SimConfig.AiKeeperDiveThresh)
                {
                    LaunchDive(Mathf.Sign(offset), ability);
                    return;
                }
            }

            // Otherwise shuffle to shadow the (clamped) target x. MoveInput is a WORLD
            // velocity, so drive world +X directly - using the keeper's local right
            // (which points -X since he faces -Z) sent him the wrong way, off the map.
            // When no shot is incoming, hold the line by returning toward centre instead
            // of chasing the ball's resting x all over the pitch.
            float targetX = incoming ? Mathf.Clamp(predictX, -halfGoal, halfGoal) : _homeX;
            float react = incoming ? 1f : 0.3f;
            float speed = SimConfig.KeeperStrafeSpeed * Mathf.Lerp(0.5f, 1.6f, ability) * react;
            float curX = _ragdoll.Pelvis.position.x;
            float dx = Mathf.Clamp(targetX - curX, -1f, 1f);
            _ragdoll.MoveInput = new Vector3(dx * speed, 0f, 0f);

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
            _lowPose = null;   // this is a full layout dive, not a low splay
            _diveAir = 0f; _diveGround = 0f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            // dir is the WORLD-X direction of the shot. Lunge in world X directly. The
            // body roll is about the facing's forward axis; since the keeper's local right
            // points -X, the roll sign is the negation of the world-X dir so he lies flat
            // ON the side he's diving toward.
            Vector3 fwd = _facing * Vector3.forward;
            float rollDir = -dir;
            float abilMul = Mathf.Lerp(0.6f, 1.15f, ability);
            float horiz = SimConfig.AiKeeperDiveHoriz * abilMul
                          * (SimConfig.KeeperStrafeSpeed / 5.5f);
            float up = SimConfig.AiKeeperDiveUp * abilMul
                       * (SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase);
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, up, 0f));

            // Held horizontal lay-out toward the dive side (same approach as player keeper).
            Quaternion layout = Quaternion.AngleAxis(-rollDir * SimConfig.KeeperDiveLayoutHigh, fwd) * _facing;
            _ragdoll.BodyOrientTarget = layout;
            _ragdoll.AddTorqueToPelvis(fwd * (-rollDir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        // Low / grounded shot: the keeper spreads to block down low rather than launching
        // into a full airborne dive. Central -> Split (both legs out); to a side ->
        // SaveLeft/SaveRight splay lunge, staying low with only a small hop.
        void LaunchLowSave(float dir, float ability)
        {
            _state = State.Diving;
            _diveDir = dir;
            _diveAir = 0f; _diveGround = 0f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            bool central = Mathf.Abs(_ball.transform.position.x - _homeX) < SimConfig.AiKeeperSplitWidth;

            if (central)
            {
                // Split: stay planted, spread wide + low. Minimal launch.
                _lowPose = KeeperPose.Split;
                _ragdoll.BodyOrientTarget = _facing;   // stay upright-ish
            }
            else
            {
                // Splay lunge to the side: low horizontal push, small hop, arm+leg out.
                _lowPose = dir < 0f ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                float horiz = SimConfig.AiKeeperDiveHoriz * 0.75f * Mathf.Lerp(0.6f, 1.15f, ability)
                              * (SimConfig.KeeperStrafeSpeed / 5.5f);
                _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, SimConfig.AiKeeperLowSaveUp, 0f));
                _ragdoll.BodyOrientTarget = _facing;   // keep low, no full layout roll
            }
            _ragdoll.SetPose(_lowPose, 16f);
        }

        // Low ball toward a bottom corner: a full lunging dive, but kept LOW (strong
        // horizontal, small upward) with the layout roll, so he goes down and across to
        // reach it rather than launching up over a rolling ball.
        void LaunchLowDive(float dir, float ability)
        {
            _state = State.Diving;
            _diveDir = dir;
            _lowPose = null;   // uses the layout Dive pose, not a splay
            _diveAir = 0f; _diveGround = 0f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            Vector3 fwd = _facing * Vector3.forward;
            float rollDir = -dir;
            float abilMul = Mathf.Lerp(0.6f, 1.15f, ability);
            // More horizontal reach than a high dive, much less lift (stays low).
            float horiz = SimConfig.AiKeeperDiveHoriz * 1.15f * abilMul
                          * (SimConfig.KeeperStrafeSpeed / 5.5f);
            float up = SimConfig.AiKeeperLowDiveUp * abilMul;
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, up, 0f));

            Quaternion layout = Quaternion.AngleAxis(-rollDir * SimConfig.KeeperDiveLayoutLow, fwd) * _facing;
            _ragdoll.BodyOrientTarget = layout;
            _ragdoll.AddTorqueToPelvis(fwd * (-rollDir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        Vector3[] _lowPose;   // held pose during a low save (Split / SaveLeft / SaveRight)

        void ManageDive()
        {
            // Hold the low-save splay pose if we're in one, else the airborne dive layout.
            _ragdoll.SetPose(_lowPose ?? KeeperPose.Dive, 16f);
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
            _lowPose = null;
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
