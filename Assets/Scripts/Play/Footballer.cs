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
            bool teamHasBall = _game.PossessionTeam == Team;

            Vector3 target;
            if (isClosest)
            {
                // Only the closest man presses the ball; once on it, act.
                target = ball;
                float dist = Vector3.Distance(me, ball);
                if (!teamHasBall && dist < SimConfig.AiTackleRange && _kickCooldown <= 0f)
                {
                    // Opponent has it and we're close: lunge in to win the ball.
                    TryTackle(me, ball);
                }
                else if (dist < SimConfig.AiChaseStopDist + SimConfig.BallRadius + 0.3f && _kickCooldown <= 0f)
                {
                    TryKick(me, ball);
                }
            }
            else
            {
                // Everyone else holds a ROLE SLOT: their kickoff anchor shifted toward the
                // ball's end of the pitch, so the shape slides with play but stays spread.
                // Attacking: push the anchor forward (toward the target goal). Defending:
                // drop it goal-side (toward own goal). This keeps width instead of clumping.
                float shift = teamHasBall ? SimConfig.AiSupportSpread : -SimConfig.AiSupportSpread * 0.8f;
                Vector3 anchor = _homeSpot + new Vector3(0f, 0f, AttackZ * shift);
                // Slide the whole line toward the ball's z a little so they don't hang back.
                anchor.z = Mathf.Lerp(anchor.z, ball.z, 0.35f);
                // Track the ball's x band so the nearest-side players cover it.
                anchor.x = Mathf.Lerp(anchor.x, ball.x, 0.2f);
                target = anchor;
            }

            // Inter-player spacing: push away from the nearest teammate so they don't stack.
            target += Separation(me);

            // Clamp inside the pitch.
            target.x = Mathf.Clamp(target.x, -_game.HalfWidth + 0.5f, _game.HalfWidth - 0.5f);
            target.z = Mathf.Clamp(target.z, -_game.HalfLength + 0.5f, _game.HalfLength - 0.5f);

            Drive(me, target);
        }

        // Small steering offset that pushes this player away from any teammate within a
        // spacing radius, so outfielders keep their distance instead of piling on the ball.
        Vector3 Separation(Vector3 me)
        {
            float radius = SimConfig.AiSeparationRadius;
            Vector3 push = Vector3.zero;
            var team = _game.TeamList(Team);
            for (int i = 0; i < team.Count; i++)
            {
                var o = team[i];
                if (o == null || o == this || o.IsKeeper) continue;
                Vector3 d = me - o.Pos; d.y = 0f;
                float dist = d.magnitude;
                if (dist > 0.01f && dist < radius)
                    push += d / dist * (radius - dist);   // stronger the closer they are
            }
            return push;
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
                    _kickCooldown = SimConfig.AiKickCooldown;
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

        // AI tackle: lunge at the ball and, if it reaches, win it off the opponent.
        void TryTackle(Vector3 me, Vector3 ball)
        {
            _kickCooldown = SimConfig.TackleCooldown;
            Vector3 to = ball - me; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                Ragdoll.AddVelocityToAll(to.normalized * SimConfig.TackleLunge);
            if (to.magnitude <= SimConfig.TackleReach)
                _game.WinBallForAi(this);
        }

        // Decide what to do with the ball when on it: SHOOT if in range of the target
        // goal; else PASS to an open teammate further forward; else DRIVE it up the pitch.
        void TryKick(Vector3 me, Vector3 ball)
        {
            _kickCooldown = SimConfig.AiKickCooldown;
            Vector3 toGoal = TargetGoal - ball; toGoal.y = 0f;
            float goalDist = toGoal.magnitude;
            Vector3 dir = goalDist > 0.1f ? toGoal / goalDist : new Vector3(0f, 0f, AttackZ);

            if (goalDist < SimConfig.AiShootRange)
            {
                Vector3 v = (dir + Vector3.up * 0.18f).normalized * (SimConfig.DribbleShotSpeed * 0.95f);
                _ball.KickTo(v);
                return;
            }

            // Look for a teammate meaningfully further toward goal to pass to.
            var mate = BestForwardMate(ball);
            if (mate != null)
            {
                Vector3 to = mate.Pos - ball; to.y = 0f;
                float d = to.magnitude;
                Vector3 pdir = to / Mathf.Max(0.01f, d);
                // Ground pass, a touch of lift, speed scaled to distance.
                Vector3 v = pdir * Mathf.Clamp(d * 1.1f, 8f, SimConfig.PassGroundSpeed + 6f) + Vector3.up * 1.2f;
                _ball.KickTo(v);
                return;
            }

            // Drive forward: a controlled push up the pitch.
            _ball.KickTo(dir * SimConfig.AiKickBoneImpulse + Vector3.up * 0.4f);
        }

        // A teammate who is clearly ahead (toward the target goal) and not too far, best in
        // line with the forward direction. Returns null if nobody good to pass to.
        Footballer BestForwardMate(Vector3 ball)
        {
            var team = _game.TeamList(Team);
            Footballer best = null; float bestScore = 0.35f;   // require a decent forward option
            foreach (var f in team)
            {
                if (f == null || f == this || f.IsKeeper) continue;
                Vector3 to = f.Pos - ball; to.y = 0f;
                float d = to.magnitude;
                if (d < 4f || d > SimConfig.PassMaxRange) continue;
                // Must be forward of the ball (toward goal) by the attack sign.
                if ((f.Pos.z - ball.z) * AttackZ < 2f) continue;
                float fwdness = Vector3.Dot(to.normalized, new Vector3(0f, 0f, AttackZ));
                if (fwdness > bestScore) { bestScore = fwdness; best = f; }
            }
            return best;
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
