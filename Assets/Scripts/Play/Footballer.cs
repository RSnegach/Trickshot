using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One outfield player in a scrimmage. Holds team + role data and, when NOT the
    /// human-controlled one, runs a functional AI that drives the ActiveRagdoll:
    ///
    ///  - Closest man ON the ball: DRIBBLE-CARRY toward goal (nudging the ball ahead, steering
    ///    around the nearest defender), and once in range SHOOT an arced set-piece-style shot at
    ///    the far corner (away from the goal centre a keeper shadows) - or, if a forward teammate
    ///    is in a CLEAR passing lane, chip it to their feet leading their run. Opponent on the
    ///    ball nearby: tackle.
    ///  - Off the ball: attackers make forward runs into open channels ahead of the carrier;
    ///    defenders drop goal-side between the ball and their own goal. Spacing keeps width.
    ///
    /// Movement uses the same ActiveRagdoll.MoveInput steering + a procedural run gait as the
    /// player striker, so AI and human move identically. Team attack direction is +Z for Home,
    /// -Z for Away. The ScrimmageGame ticks these (AiTick) so it stays in lockstep and can flip
    /// _controlled on the switched player. Shots go out via BallController.LaunchLofted/LaunchTo
    /// so the AI obeys the same airborne, no-controllable-spin scrimmage rule as the human.
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

        Knockdown _knock;
        public Knockdown Knock => _knock != null ? _knock : (_knock = GetComponent<Knockdown>());
        public bool IsDown => Knock != null && Knock.Down;

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
            if (IsDown) return;   // knocked over: the Knockdown component owns the body
            Ragdoll.ClearPoseOverrides();
            if (_kickCooldown > 0f) _kickCooldown -= Time.deltaTime;

            Vector3 me = Pos; me.y = 0f;
            Vector3 ball = _ball.transform.position; ball.y = 0f;
            bool teamHasBall = _game.PossessionTeam == Team;

            float ballDist = Vector3.Distance(me, ball);
            bool onBall = ballDist < SimConfig.AiChaseStopDist + SimConfig.BallRadius + 0.35f;

            Vector3 target;
            if (isClosest)
            {
                if (!teamHasBall)
                {
                    // Opponent (or a loose ball) we're closest to: chase, and lunge to win it if near.
                    target = ball;
                    if (ballDist < SimConfig.AiTackleRange && _kickCooldown <= 0f) TryTackle(me, ball);
                }
                else if (onBall && _kickCooldown <= 0f)
                {
                    // We're on our own ball: decide shoot / pass / carry.
                    OnBallAct(me, ball, out target);
                }
                else
                {
                    target = ball;   // closing in on our ball
                }
            }
            else
            {
                target = SupportSpot(me, ball, teamHasBall);
            }

            // Inter-player spacing: push away from the nearest teammate so they don't stack.
            target += Separation(me);

            // Clamp inside the pitch.
            target.x = Mathf.Clamp(target.x, -_game.HalfWidth + 0.5f, _game.HalfWidth - 0.5f);
            target.z = Mathf.Clamp(target.z, -_game.HalfLength + 0.5f, _game.HalfLength - 0.5f);

            // Carrying the ball uses a slightly different drive (nudges the ball ahead); everything
            // else is a plain run to the target.
            if (_carrying) DriveCarry(me, target, ball);
            else Drive(me, target);
            _carrying = false;   // reset each tick; OnBallAct re-sets it when it chooses to carry
        }

        bool _carrying;   // set by OnBallAct for this tick when the decision is to dribble the ball

        // Off-ball positioning. Attackers make a forward RUN into an open channel ahead of the
        // carrier (staggered by formation x so they spread); defenders drop goal-side between the
        // ball and their own goal. Slides with play instead of hanging on the kickoff anchor.
        Vector3 SupportSpot(Vector3 me, Vector3 ball, bool teamHasBall)
        {
            if (teamHasBall)
            {
                // Attack: push AHEAD of the ball toward goal, holding this player's formation width.
                float ahead = SimConfig.AiSupportSpread;
                Vector3 spot = new Vector3(_homeSpot.x, 0f, ball.z + AttackZ * ahead);
                // Keep width from the kickoff x but drift toward the ball's half of the pitch a little.
                spot.x = Mathf.Lerp(_homeSpot.x, ball.x, 0.35f);
                return spot;
            }
            // Defend: sit goal-side of the ball (between ball and own goal), tracking its x.
            Vector3 def = new Vector3(Mathf.Lerp(_homeSpot.x, ball.x, 0.5f), 0f,
                                      ball.z - AttackZ * SimConfig.AiSupportSpread * 0.6f);
            return def;
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

        // On our own ball: SHOOT if in range and roughly facing goal; else chip to a teammate in a
        // CLEAR forward lane; else DRIBBLE-CARRY toward goal (sets _carrying so AiTick drives + nudges
        // the ball). `target` is where to steer when carrying/closing.
        void OnBallAct(Vector3 me, Vector3 ball, out Vector3 target)
        {
            target = ball;
            Vector3 toGoal = TargetGoal - ball; toGoal.y = 0f;
            float goalDist = toGoal.magnitude;
            Vector3 gdir = goalDist > 0.1f ? toGoal / goalDist : new Vector3(0f, 0f, AttackZ);
            bool facingGoal = Vector3.Dot(gdir, new Vector3(0f, 0f, AttackZ)) >= SimConfig.AiShootConeDot;

            // SHOOT: in range + facing goal -> arced shot at the FAR corner (away from the ball's x,
            // where the keeper shadows), with a little scatter so it's not automatic.
            if (goalDist < SimConfig.AiShootRange && facingGoal)
            {
                Shoot(ball);
                return;
            }

            // PASS: a forward teammate in a clear lane gets a leading chip.
            var mate = BestOpenMate(ball, out Vector3 lead);
            if (mate != null)
            {
                _kickCooldown = SimConfig.AiKickCooldown;
                Vector3 to = lead - ball; to.y = 0f;
                float d = to.magnitude;
                Vector3 pdir = to / Mathf.Max(0.01f, d);
                Vector3 v = pdir * Mathf.Clamp(d * 1.15f, 9f, SimConfig.PassGroundSpeed + 7f) + Vector3.up * 1.4f;
                _ball.KickTo(v);
                return;
            }

            // CARRY: dribble toward goal, steering around the nearest defender in the way.
            _carrying = true;
            Vector3 goalward = ball + gdir * 6f;
            Vector3 avoid = DefenderAvoidOffset(ball, gdir);
            target = goalward + avoid;
        }

        // Arced shot on goal at the far post relative to the ball's x, via the scrimmage lofted
        // launch (airborne, no controllable spin) using LaunchTo's ballistic solve so it actually
        // dips under the bar. Aims away from goal centre to wrong-foot the shadowing keeper.
        void Shoot(Vector3 ball)
        {
            _kickCooldown = SimConfig.AiKickCooldown;
            float halfGoal = SimConfig.GoalWidth * 0.5f - SimConfig.BallRadius - 0.3f;
            // Far corner: opposite side of centre from where the ball is.
            float side = ball.x >= 0f ? -1f : 1f;
            float aimX = side * halfGoal;
            aimX += Random.Range(-SimConfig.AiShotScatter, SimConfig.AiShotScatter);
            aimX = Mathf.Clamp(aimX, -halfGoal, halfGoal);
            float aimY = Mathf.Clamp(SimConfig.GoalHeight * 0.55f + Random.Range(-0.3f, 0.3f),
                                     0.4f, SimConfig.GoalHeight - 0.2f);
            Vector3 aim = new Vector3(aimX, aimY, TargetGoal.z);
            // Flight time scales a little with distance so near shots stay flat-ish, far ones arc more.
            float dist = Vector3.Distance(ball, aim);
            float t = Mathf.Clamp(dist / 22f, 0.35f, 0.9f);
            _ball.LaunchTo(aim, t, Vector3.zero, 0f);
        }

        // A teammate ahead toward goal, within pass range, whose passing LANE is not blocked by an
        // opponent. Returns the best one + a lead point (ahead of them along their travel). Null if none.
        Footballer BestOpenMate(Vector3 ball, out Vector3 lead)
        {
            lead = ball;
            var team = _game.TeamList(Team);
            var opp = _game.TeamList(Team == 0 ? 1 : 0);
            Footballer best = null; float bestScore = 0.45f;
            foreach (var f in team)
            {
                if (f == null || f == this || f.IsKeeper || f.IsDown) continue;
                Vector3 fp = f.Pos; fp.y = 0f;
                Vector3 to = fp - ball; to.y = 0f;
                float d = to.magnitude;
                if (d < 5f || d > SimConfig.PassMaxRange) continue;
                if ((fp.z - ball.z) * AttackZ < 2f) continue;         // must be forward
                if (!LaneClear(ball, fp, opp)) continue;              // no opponent sitting in the lane
                float fwdness = Vector3.Dot(to.normalized, new Vector3(0f, 0f, AttackZ));
                if (fwdness > bestScore)
                {
                    bestScore = fwdness; best = f;
                    // Lead the runner: nudge the target ahead of them toward goal.
                    lead = fp + new Vector3(0f, 0f, AttackZ) * (SimConfig.AiOutfieldSpeed * SimConfig.AiPassLeadTime);
                }
            }
            return best;
        }

        // True if no opponent is within AiLaneCheckRadius of the segment ball->mate (a clear lane).
        bool LaneClear(Vector3 a, Vector3 b, System.Collections.Generic.List<Footballer> opp)
        {
            Vector3 ab = b - a; ab.y = 0f;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.01f) return true;
            for (int i = 0; i < opp.Count; i++)
            {
                var o = opp[i];
                if (o == null || o.IsKeeper || o.IsDown) continue;
                Vector3 p = o.Pos; p.y = 0f;
                float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
                Vector3 closest = a + ab * u;
                if ((p - closest).sqrMagnitude < SimConfig.AiLaneCheckRadius * SimConfig.AiLaneCheckRadius)
                    return false;
            }
            return true;
        }

        // A sideways steer offset to dribble AROUND the nearest opponent between the ball and goal.
        Vector3 DefenderAvoidOffset(Vector3 ball, Vector3 gdir)
        {
            var opp = _game.TeamList(Team == 0 ? 1 : 0);
            Footballer near = null; float bestD = SimConfig.AiDefenderAvoid;
            foreach (var o in opp)
            {
                if (o == null || o.IsKeeper || o.IsDown) continue;
                Vector3 to = o.Pos - ball; to.y = 0f;
                if (Vector3.Dot(to, gdir) <= 0f) continue;      // only defenders AHEAD (between us + goal)
                float d = to.magnitude;
                if (d < bestD) { bestD = d; near = o; }
            }
            if (near == null) return Vector3.zero;
            // Step to whichever side of the defender has more room (toward the nearer touchline gap).
            Vector3 right = Vector3.Cross(Vector3.up, gdir);
            Vector3 rel = near.Pos - ball;
            float sideSign = Vector3.Dot(rel, right) >= 0f ? -1f : 1f;   // go the opposite side of the defender
            float strength = (SimConfig.AiDefenderAvoid - bestD) / SimConfig.AiDefenderAvoid;   // closer -> steer harder
            return right * (sideSign * 4f * strength);
        }

        // Dribble drive: run toward `target` but keep the ball a step ahead by nudging it in the
        // travel direction when it lags, so the carrier takes it with them instead of leaving it.
        void DriveCarry(Vector3 me, Vector3 target, Vector3 ball)
        {
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : new Vector3(0f, 0f, AttackZ);
            Ragdoll.MoveInput = dir * SimConfig.AiCarrySpeed;
            Ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
            RunGait(1f);

            // If the ball has fallen behind/beside the run, give it a gentle forward nudge so it
            // stays ahead of the feet. Gated by the touch cooldown so it isn't a continuous boot.
            if (_kickCooldown <= 0f)
            {
                Vector3 ballAhead = ball - me; ballAhead.y = 0f;
                bool ballLagging = Vector3.Dot(ballAhead, dir) < SimConfig.BallRadius + 0.15f;
                if (ballLagging)
                {
                    Vector3 flat = _ball.Rb.linearVelocity; flat.y = 0f;
                    if (flat.magnitude < SimConfig.AiCarryNudge)
                        _ball.KickTo(dir * SimConfig.AiCarryNudge);
                    _kickCooldown = 0.18f;   // short: keeps the carry lively without booting it away
                }
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
