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

        // Attack direction (world Z sign), assigned at Init. The goal this player attacks is the one
        // at that Z end; own goal is the other.
        //
        // This used to claim it was NOT derived from team, "because in keeper role Home defends the +Z
        // goal, the opposite of outfield role". That is false and was actively misleading:
        // GameBootstrap sets `attackZ = team == 0 ? 1f : -1f` with no role branch at all, the human
        // keeper is built at the -Z (Away) goal, and ScrimmageGame's own kickoff comment says so in as
        // many words. Home attacks +Z in every role.
        public float AttackZ = 1f;
        public Vector3 TargetGoal => AttackZ > 0f ? _game.HomeGoal : _game.AwayGoal;   // HomeGoal is +Z
        public Vector3 OwnGoal    => AttackZ > 0f ? _game.AwayGoal : _game.HomeGoal;

        Vector3 _homeSpot;            // formation anchor (kickoff / rest position)
        float _gaitPhase;
        float _gaitWeight;
        readonly Vector3[] _gaitScratch = new Vector3[(int)Bone.Count];
        float _kickCooldown;
        float _carryTouchTimer;   // counts down to this bot's next dribble touch
        bool _carryColl;          // ball<->own-body collision currently suspended for a carry

        Knockdown _knock;
        public Knockdown Knock => _knock != null ? _knock : (_knock = GetComponent<Knockdown>());
        public bool IsDown => Knock != null && Knock.Down;

        Striker _strk;
        // The Striker on this body. Every scrimmage footballer has one (GameBootstrap
        // BuildFootballer and NetScrimmageMatch SpawnBody both add it), but it is only ticked
        // while a human drives the body, so an AI body's trick state stays inert.
        public Striker Strk => _strk != null ? _strk : (_strk = GetComponent<Striker>());

        public Vector3 Pos => Ragdoll != null && Ragdoll.Pelvis != null ? Ragdoll.Pelvis.position : transform.position;

        // Where this player is heading, for leading a pass. MoveInput IS the desired world
        // horizontal velocity, which tracks intent better than the pelvis rigidbody (that one
        // jitters with every ragdoll correction).
        public Vector3 Vel => Ragdoll != null ? Ragdoll.MoveInput : Vector3.zero;

        /// <summary>How fast this body runs, as a multiple of SimConfig.AiOutfieldSpeed. Derived from
        /// team + shirt so every peer agrees without syncing it (see SimConfig.AiPace).</summary>
        public float PaceMul { get; private set; } = 1f;

        public void Init(ScrimmageGame game, BallController ball, ActiveRagdoll ragdoll, int team, bool keeper, float attackZ, Vector3 homeSpot,
                         int shirt = 0)
        {
            _game = game; _ball = ball; Ragdoll = ragdoll; Team = team; IsKeeper = keeper; AttackZ = attackZ; _homeSpot = homeSpot;
            PaceMul = SimConfig.AiPace(team, shirt, keeper);
            Ragdoll.FacingRotation = Quaternion.LookRotation(new Vector3(0f, 0f, AttackZ), Vector3.up);
        }

        // Called by ScrimmageGame each frame for every AI (non-controlled) outfielder.
        public void AiTick(bool isClosest)
        {
            if (Ragdoll == null || Ragdoll.Pelvis == null || _ball == null) return;
            // Knocked over: the Knockdown component owns the body. Give the ball back first -
            // a felled bot must not keep carry ownership (nor keep the ball phasing through it).
            if (IsDown) { SetCarryCollision(false); return; }
            Ragdoll.ClearPoseOverrides();
            if (_kickCooldown > 0f) _kickCooldown -= Time.deltaTime;
            if (_carryTouchTimer > 0f) _carryTouchTimer -= Time.deltaTime;

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
            else { SetCarryCollision(false); Drive(me, target); }
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

        // AI keeper. This is the SAME brain as the striker-mode Goalkeeper, pointed at this end of
        // the pitch, so a scrimmage keeper positions on the angle, comes off his line, sweeps up
        // loose balls, catches what he can hold, dives at what he cannot, and plays it out.
        //
        // What used to be here shadowed the ball's x on a fixed line 1 m off his goal and hoofed
        // anything that came within 3.5 m of him. That is a wall with legs, not a goalkeeper.
        public void AiKeeperTick()
        {
            if (Ragdoll == null || Ragdoll.Pelvis == null || _ball == null) return;
            // Knocked over: hand the ball back and let Knockdown own the body.
            if (IsDown) { if (_gk != null) _gk.DropBall(); return; }
            if (_kickCooldown > 0f) _kickCooldown -= Time.deltaTime;
            Keeper.Tick();
        }

        Goalkeeper _gk;

        /// <summary>The goalkeeping brain, built on first use (only keepers ever get one).</summary>
        public Goalkeeper Keeper
        {
            get
            {
                if (_gk == null)
                {
                    _gk = gameObject.AddComponent<Goalkeeper>();
                    _gk.Init(Ragdoll, _ball, OwnGoal, AttackZ);
                    _gk.Sweeper = true;         // full match: off his line, sweeping, distributing
                    _gk.DistributeTarget = DistributeAim;
                }
                return _gk;
            }
        }

        /// <summary>True while this keeper has the ball in his gloves.</summary>
        public bool KeeperHoldingBall => _gk != null && _gk.HasBall;

        // Where a gathered ball gets played: the best pass on, else straight upfield.
        Vector3 DistributeAim(Vector3 from)
        {
            Vector3 upfield = new Vector3(0f, 0f, AttackZ);
            var mates = _game.TeamList(Team);
            var opps  = _game.TeamList(Team == 0 ? 1 : 0);
            if (Passing.BestTarget(from, upfield, AttackZ, true, 0.7f, 1f, mates, opps, this, out var opt))
                return opt.aim;
            return from + upfield * SimConfig.KeeperDistributeRange;
        }

        // Steer toward a target with the run gait; face travel direction.
        void Drive(Vector3 me, Vector3 target)
        {
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.zero;
            float speed = dist > 0.4f ? SimConfig.AiOutfieldSpeed * PaceMul : 0f;
            Ragdoll.MoveInput = dir * speed;

            if (dir.sqrMagnitude > 0.01f)
                Ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);

            // Normalised against THIS body's top speed, not the shared base, or a quick player's gait
            // would run past 1 and a slow one would never reach a full stride.
            RunGait(speed / Mathf.Max(0.1f, SimConfig.AiOutfieldSpeed * PaceMul));
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

            // PASS: through the shared pass model, so a bot's pass has the same weight, lead and
            // error as a human's - and can be a ball into space rather than always at feet.
            if (_kickCooldown <= 0f)
            {
                var mates = _game.TeamList(Team);
                var opps  = _game.TeamList(Team == 0 ? 1 : 0);
                Vector3 aimDir = new Vector3(0f, 0f, AttackZ);
                // Chip it when the route out is crowded, roll it when it isn't.
                bool loft = !Passing.LaneClear(ball, ball + aimDir * 9f, opps, SimConfig.PassLaneRadius);
                float charge = SimConfig.AiPassCharge;
                if (Passing.BestTarget(ball, aimDir, AttackZ, loft, charge, 1f, mates, opps, this, out var opt))
                {
                    _kickCooldown = SimConfig.AiKickCooldown;
                    SetCarryCollision(false);   // hand the ball back before striking it
                    if (_game != null) _game.NoteAiPass(Ragdoll);
                    float acc = SimConfig.AiPassAccuracy;
                    float d = Vector3.Distance(ball, opt.aim);
                    float press = Passing.Pressure01(ball, opps);
                    Passing.Launch(_ball, opt.aim, loft, charge, 1f, Ragdoll,
                                   Passing.ScatterDeg(acc, d, press, charge, false),
                                   Passing.Wobble(acc, false));
                    return;
                }
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
            if (_game != null) _game.NoteShotBy(Ragdoll);
            // Flight time scales a little with distance so near shots stay flat-ish, far ones arc more.
            float dist = Vector3.Distance(ball, aim);
            float t = Mathf.Clamp(dist / 22f, 0.35f, 0.9f);
            _ball.LaunchTo(aim, t, Vector3.zero, 0f);
        }

        // Lane checks live in Passing now, so a bot and a human read the same blocked lane.
        bool LaneClear(Vector3 a, Vector3 b, System.Collections.Generic.List<Footballer> opp)
            => Passing.LaneClear(a, b, opp, SimConfig.AiLaneCheckRadius);

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
            Ragdoll.MoveInput = dir * (SimConfig.AiCarrySpeed * PaceMul);
            Ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
            RunGait(1f);

            // Bots dribble through the SAME touch primitive the human carrier uses (see Dribble):
            // one kick per stride toward where the next stride wants the ball, with the ball a free
            // rolling rigidbody in between. That is what makes an AI carry poachable.
            // A human carrier owns the ball outright, so stand off while one is on it.
            if (Dribble.Holder != null) { SetCarryCollision(false); return; }
            SetCarryCollision(true);

            // Touch on cadence, or EARLY if the ball has fallen level with the feet or drifted off
            // the running line - literally the same corrective test the player's carry uses.
            if (_carryTouchTimer > 0f && !Dribble.NeedsCorrectiveTouch(me, dir, ball)) return;

            float interval = Dribble.StrideInterval(Ragdoll, false);
            float touchDist = Dribble.TouchDistance(Ragdoll.GroundSpeed, SimConfig.AiDribbleTightness, false);
            Dribble.Touch(_ball, me, dir, Ragdoll.MoveInput, interval, touchDist, SimConfig.AiTouchErrorDeg);
            _carryTouchTimer = interval;
        }

        // Suspend/restore ball collision with this bot's own limbs while it carries, exactly as a
        // human carry does. Latched so it isn't re-applied over every collider every tick.
        void SetCarryCollision(bool on)
        {
            if (_carryColl == on) return;
            _carryColl = on;
            Dribble.SetCarryCollision(_ball, Ragdoll, on);
            if (_ball == null) return;
            // Register the carry on the ball as well, so this bot's own gait taps are not read as
            // strikes while EVERY other body can still tackle, shoot or volley it off its feet.
            if (on) _ball.SetDribbleCarrier(Ragdoll);
            else if (_ball.DribbleCarrier == Ragdoll) _ball.SetDribbleCarrier(null);
        }

        void OnDisable() => SetCarryCollision(false);

        // Cosmetic run, from the SHARED gait table (Gait), so a bot's legs move exactly like a
        // player's on the same body plan. Fades on a weight rather than early-returning: the old
        // version left its last pose frozen on the bones the moment the bot stopped moving.
        void RunGait(float amount)
        {
            var p = Gait.For(Ragdoll.Plan);
            float speed = Ragdoll.GroundSpeed;
            _gaitWeight = Gait.Weight(_gaitWeight, speed, amount >= 0.05f, Time.deltaTime);

            float sprint01;
            _gaitPhase += Time.deltaTime * Gait.Cadence(speed, Ragdoll.HeightScale, p, out sprint01);
            if (_gaitPhase > Mathf.PI * 2f) _gaitPhase -= Mathf.PI * 2f;

            var over = _gaitScratch;
            for (int i = 0; i < over.Length; i++) over[i] = Vector3.zero;
            Gait.Pose(over, p, _gaitPhase, _gaitWeight, sprint01, 0f, 0f);
            for (int i = 0; i < over.Length; i++) Ragdoll.SetPoseOverride((Bone)i, over[i]);
            Ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        public void ResetTo(Vector3 spot)
        {
            _homeSpot = spot;
            _kickCooldown = 0f;
            _carryTouchTimer = 0f;
            SetCarryCollision(false);
            _gaitPhase = 0f;
            _gaitWeight = 0f;
            // A keeper must let go of a held ball on a reset, or he stays welded to a ball the
            // match has already moved on from.
            if (_gk != null) { _gk.ResetTo(spot); return; }
            Ragdoll.ResetTo(spot, Quaternion.LookRotation(new Vector3(0f, 0f, AttackZ), Vector3.up));
        }
    }
}
