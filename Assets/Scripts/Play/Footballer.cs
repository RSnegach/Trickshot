using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One outfield player in a scrimmage. Holds team + role data and, when NOT the
    /// human-controlled one, runs a simple functional AI that drives the ActiveRagdoll:
    ///
    ///  - If this player is the team's closest to the ball, CHASE it and, once on it,
    ///    kick toward the opponent goal (or pass/clear).
    ///  - Otherwise SUPPORT: spread into space ahead of the ball (attacking) or drop
    ///    goal-side between the ball and own goal (defending).
    ///
    /// Movement uses the same ActiveRagdoll.MoveInput steering + a procedural run gait as
    /// the player striker, so AI and human move identically. Team attack direction is +Z
    /// for Home, -Z for Away. The ScrimmageGame ticks these (AiTick) so it stays in
    /// lockstep and can flip _controlled on the switched player.
    /// </summary>
    public class Footballer : MonoBehaviour
    {
        public int Team;              // 0 = Home, 1 = Away (for kit + HUD only)
        public bool IsKeeper;
        public ActiveRagdoll Ragdoll;
        BallController _ball;
        ScrimmageGame _game;

        // Attack direction (world Z sign), assigned at Init - NOT derived from team, because
        // in keeper role Home defends the +Z goal (so attacks -Z), the opposite of outfield
        // role. The goal this player attacks is the one at that Z end; own goal is the other.
        public float AttackZ = 1f;
        public Vector3 TargetGoal => AttackZ > 0f ? _game.HomeGoal : _game.AwayGoal;   // HomeGoal is +Z
        public Vector3 OwnGoal    => AttackZ > 0f ? _game.AwayGoal : _game.HomeGoal;

        Vector3 _homeSpot;            // formation anchor (kickoff / rest position)
        float _gaitPhase;
        float _kickCooldown;

        public Vector3 Pos => Ragdoll != null && Ragdoll.Pelvis != null ? Ragdoll.Pelvis.position : transform.position;

        public void Init(ScrimmageGame game, BallController ball, ActiveRagdoll ragdoll, int team, bool keeper, float attackZ, Vector3 homeSpot)
        {
            _game = game; _ball = ball; Ragdoll = ragdoll; Team = team; IsKeeper = keeper; AttackZ = attackZ; _homeSpot = homeSpot;
            Ragdoll.FacingRotation = Quaternion.LookRotation(new Vector3(0f, 0f, AttackZ), Vector3.up);
        }

        // Called by ScrimmageGame each frame for every AI (non-controlled) outfielder.
        public void AiTick(bool isClosest)
        {
            if (Ragdoll == null || Ragdoll.Pelvis == null || _ball == null) return;
            Ragdoll.ClearPoseOverrides();
            if (_kickCooldown > 0f) _kickCooldown -= Time.deltaTime;

            Vector3 me = Pos; me.y = 0f;
            Vector3 ball = _ball.transform.position; ball.y = 0f;
            bool weAttackPlus = AttackZ > 0f;
            bool ballInOurAttackingHalf = weAttackPlus ? ball.z > 0f : ball.z < 0f;

            Vector3 target;
            if (isClosest)
            {
                // Chase the ball; once on it, kick.
                target = ball;
                float dist = Vector3.Distance(me, ball);
                if (dist < SimConfig.AiChaseStopDist + SimConfig.BallRadius + 0.3f && _kickCooldown <= 0f)
                    TryKick(me, ball);
            }
            else
            {
                // Support / mark. Attacking team: spread ahead of the ball toward goal.
                // Defending: sit goal-side, between ball and own goal.
                bool teamHasBall = _game.PossessionTeam == Team;
                if (teamHasBall)
                {
                    float sideSign = (GetInstanceID() % 2 == 0) ? 1f : -1f;
                    target = ball + new Vector3(sideSign * SimConfig.AiSupportSpread, 0f, AttackZ * SimConfig.AiSupportSpread);
                }
                else
                {
                    Vector3 toOwn = (OwnGoal - ball); toOwn.y = 0f;
                    target = ball + toOwn.normalized * (SimConfig.AiSupportSpread * 0.7f);
                }
                // Keep formation width so they don't all clump on the ball.
                target = Vector3.Lerp(target, _homeSpot, 0.25f);
            }

            // Clamp inside the pitch.
            target.x = Mathf.Clamp(target.x, -_game.HalfWidth + 0.5f, _game.HalfWidth - 0.5f);
            target.z = Mathf.Clamp(target.z, -_game.HalfLength + 0.5f, _game.HalfLength - 0.5f);

            Drive(me, target);
        }

        // AI keeper: hover just in front of the OWN goal line, shadow the ball's x within
        // the goal width, and rush out to clear if the ball gets close to the goal.
        public void AiKeeperTick()
        {
            if (Ragdoll == null || Ragdoll.Pelvis == null || _ball == null) return;
            Ragdoll.ClearPoseOverrides();
            if (_kickCooldown > 0f) _kickCooldown -= Time.deltaTime;

            Vector3 me = Pos; me.y = 0f;
            Vector3 ball = _ball.transform.position; ball.y = 0f;
            float half = SimConfig.GoalWidth * 0.5f;
            float guardZ = OwnGoal.z + AttackZ * 1.0f;   // 1m in front of the line, toward the pitch

            float distToBall = Vector3.Distance(me, ball);
            bool ballNearGoal = Mathf.Abs(ball.z - OwnGoal.z) < 8f && Mathf.Abs(ball.x) < half + 3f;

            Vector3 target;
            if (ballNearGoal && distToBall < 3.5f)
            {
                target = ball;   // rush + clear
                if (distToBall < SimConfig.BallRadius + 1.0f && _kickCooldown <= 0f)
                {
                    _kickCooldown = 0.5f;
                    Vector3 up = new Vector3(0f, 0f, AttackZ);           // clear up the pitch
                    Vector3 side = new Vector3(Mathf.Sign(ball.x == 0 ? 1f : ball.x), 0f, 0f) * 0.4f;
                    _ball.KickTo((up + side).normalized * (SimConfig.AiKickBoneImpulse + 4f) + Vector3.up * 2f);
                }
            }
            else
            {
                target = new Vector3(Mathf.Clamp(ball.x, -half, half), 0f, guardZ);
            }
            Drive(me, target);
        }

        // Steer toward a target with the run gait; face travel direction.
        void Drive(Vector3 me, Vector3 target)
        {
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.zero;
            float speed = dist > 0.4f ? SimConfig.AiOutfieldSpeed : 0f;
            Ragdoll.MoveInput = dir * speed;

            if (dir.sqrMagnitude > 0.01f)
                Ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);

            RunGait(speed / Mathf.Max(0.1f, SimConfig.AiOutfieldSpeed));
        }

        // Decide what to do with the ball when on it: shoot if near the target goal,
        // otherwise drive it forward (a firm touch toward goal). Kicks via the ball.
        void TryKick(Vector3 me, Vector3 ball)
        {
            _kickCooldown = 0.5f;
            Vector3 toGoal = TargetGoal - ball; toGoal.y = 0f;
            float goalDist = toGoal.magnitude;
            Vector3 dir = goalDist > 0.1f ? toGoal / goalDist : new Vector3(0f, 0f, AttackZ);

            if (goalDist < SimConfig.AiShootRange)
            {
                // Shoot: firm and slightly lofted at the goal.
                Vector3 v = (dir + Vector3.up * 0.18f).normalized * (SimConfig.DribbleShotSpeed * 0.95f);
                _ball.KickTo(v);
            }
            else
            {
                // Drive forward: a controlled push up the pitch.
                Vector3 v = dir * SimConfig.AiKickBoneImpulse + Vector3.up * 0.4f;
                _ball.KickTo(v);
            }
        }

        // Cosmetic alternating-leg run + arm pump (same shape as the striker gait).
        void RunGait(float amount)
        {
            if (amount < 0.05f) { _gaitPhase = 0f; return; }
            _gaitPhase += Time.deltaTime * SimConfig.StrideRateMax * amount;
            float s = Mathf.Sin(_gaitPhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            Ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-s * SimConfig.GaitThighSwing - liftL * SimConfig.GaitThighLift, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.GaitKneeBend, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(s * SimConfig.GaitThighSwing - liftR * SimConfig.GaitThighLift, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.GaitKneeBend, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(s * SimConfig.ArmPumpSwing, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.ForearmR,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * SimConfig.ArmPumpSwing, 0f, 0f));
            Ragdoll.SetPoseOverride(Bone.ForearmL,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            Ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        public void ResetTo(Vector3 spot)
        {
            _homeSpot = spot;
            _kickCooldown = 0f;
            _gaitPhase = 0f;
            Ragdoll.ResetTo(spot, Quaternion.LookRotation(new Vector3(0f, 0f, AttackZ), Vector3.up));
        }
    }
}
