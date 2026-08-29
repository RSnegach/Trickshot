using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One outfield player in a scrimmage: team + shirt identity, and - when no human is driving
    /// this body - a POSITIONAL brain that runs the ActiveRagdoll.
    ///
    /// WHAT WAS WRONG, plainly, because the fix only reads against it. There was no role model here
    /// at all: IsKeeper was the only distinction any body had, and Init received a shirt and threw
    /// it away. The one man nearest the ball chased it and EVERY other outfielder was sent to a
    /// single ball-relative band (ball.z +- 7 m, x lerped 35-50% toward the ball), so ten players
    /// tracked one point and moved as a blob - not literally "all chase the ball", and
    /// indistinguishable from it on screen. SimConfig.Ai was referenced zero times, so Easy and
    /// Insane outfielders were byte-identical. Everything ran every frame, spacing scan included.
    ///
    /// WHAT IT DOES NOW:
    ///  - ROLE from (perSide, shirt) through SimConfig.Formation / SimConfig.PositionAnchor. Shirt 0
    ///    is always the keeper. NO SECOND TABLE IS AUTHORED HERE - that table already existed and
    ///    said in its own comment that nothing read it yet. This is the reader.
    ///  - PHASES: Restart / Loose / Attack / Defend, each with a per-role target. The whole block
    ///    slides up and down the pitch on ONE scalar (how far the ball has progressed), so ten
    ///    players read one number and each lands somewhere different. That is the anti-blob.
    ///  - PRESSING: the nearest man presses. A second joins only when the ball is in our own third
    ///    or we are winning the race to it. Everyone else holds their slot. That is the fix.
    ///  - ON BALL: shoot / pass / carry / clear, passes and clearances through the shared Passing
    ///    model so a bot's ball carries the same weight, lead and error as a human's.
    ///  - DIFFICULTY: SimConfig.Ai and nothing else - reaction time, decision quality, execution
    ///    error, first touch, and a FRACTION of the pace the body already owns. No extra top speed,
    ///    no extra reach, no knowledge a human could not have.
    ///
    /// CADENCE. The decision (phase, slot, press rank, marking) runs at ThinkHz and is staggered by
    /// shirt so 22 brains never decide on the same frame; only steering + gait run per frame. Per
    /// think a brain does two indexed passes over the team lists (~2 x perSide distance tests) and
    /// nothing else; the on-ball man additionally runs Passing.BestTarget and up to two
    /// Passing.LaneClear calls, both analytic. At 11-a-side that is 20 brains x 8 Hz x ~20 tests
    /// = ~3.2k distance tests/s for positioning, DOWN from the ~12k/s the old per-frame Separation
    /// scan alone cost. Nothing allocates per tick (indexed for over List, struct locals, out-struct
    /// returns, preallocated _gaitScratch) and there is not one raycast in the brain.
    ///
    /// HOST ONLY, guaranteed three ways: AiTick/AiKeeperTick have exactly one caller
    /// (ScrimmageGame.Update); a client's Footballer is deliberately built with a null game
    /// (NetScrimmageMatch.SpawnBody) and appears in no list a ScrimmageGame ticks; and AiTick returns
    /// immediately on a null game so a mis-wire cannot start a second sim behind the snapshots. Every
    /// output is ragdoll drive plus ball impulses, which the host already streams. The brain's
    /// Random draws are host-local and nothing a client recomputes depends on them, so there is no
    /// shared-RNG requirement to get wrong.
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

        Vector3 _homeSpot;            // spawn spot; the shape fallback before the arena is sized
        float _gaitPhase;
        float _gaitWeight;
        readonly Vector3[] _gaitScratch = new Vector3[(int)Bone.Count];
        float _kickCooldown;
        float _carryTouchTimer;   // counts down to this bot's next dribble touch
        bool _carryColl;          // ball<->own-body collision currently suspended for a carry

        /// <summary>Squad number. 0 is ALWAYS the keeper; 1..PerSide-1 are outfield, on both teams.</summary>
        public int Shirt { get; private set; }
        /// <summary>Roster size INCLUDING the keeper, resolved from SimConfig.ScrimmagePerSide.</summary>
        public int PerSide { get; private set; } = 3;
        /// <summary>What this shirt plays in this shape. Pure function of (PerSide, Shirt).</summary>
        public SimConfig.ScrimPos Slot { get; private set; }
        // Authored anchor from SimConfig.PositionAnchor: x as a fraction of half-width, y as a
        // fraction of half-length measured INTO OWN HALF (1 = own goal line, so higher y = deeper).
        // Cached at Init because resolving it is a table walk, not a constant.
        Vector2 _anchor;

        // ---- brain tunables ----
        // These live here rather than in SimConfig because nothing outside this brain reads them and
        // SimConfig is not mine to extend this pass. Promote the block wholesale when the AI
        // positioning contract lands; the names are already SimConfig-shaped.
        const float ThinkHz          = 8f;     // decisions/second. Steering + gait still run per frame.
        const float ShapeAttackShift = 1.00f;  // half-lengths a FULL attack translates the block up
        const float ShapeBallPullAtk = 0.30f;  // lateral drift toward the ball's x while attacking
        const float ShapeBallPullDef = 0.45f;  // ...and defending (a block shifts ball-side harder)
        const float ShapeMaxDrift    = 0.34f;  // cap on that drift, as a fraction of half-width, so a
                                               // winger keeps the pitch wide instead of following play in
        const float PressCoverBack   = 4.0f;   // metres the second man sits goal-side of the ball
        const float PressLead        = 0.55f;  // max seconds of ball travel the first man leads by
        const float ClearProg        = 0.30f;  // ball this far up the pitch or less = our own third
        const float RestartRadius    = 1.6f;   // ball this near the centre spot AND still = a restart
        const float ShapeSlop        = 2.6f;   // metres of standing error at ErrorRate 1

        enum Phase { Restart, Loose, Attack, Defend }
        Phase _seen = Phase.Restart;    // what the brain can see right now
        Phase _acting = Phase.Restart;  // what it is still ACTING on - reaction lag lives in the gap
        float _reactLeft;               // seconds until _seen becomes _acting
        // WALL-CLOCK stamp of when the Restart read first became true, or -1 while it is false.
        // Restart is inferred from ball position rather than signalled, so it has to time out or a ball
        // resting on the centre spot deadlocks the match. A stamp rather than an accumulator because
        // this is read from Think, which runs at 8 Hz, so summing a per-frame delta here would
        // undercount the elapsed time by whatever the think interval happens to be.
        float _restartSince = -1f;
        float _thinkLeft;               // seconds to the next decision
        Vector3 _target;                // cached steering target, refreshed only on a think
        bool _haveTarget;
        int _rank;                      // mates nearer the ball than me: 0 = nearest, 1 = second
        bool _wasClosest;
        Footballer _mark;               // the opponent in my zone, for a defensive pick-up

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

            // ROLE. perSide is not a new parameter because no caller has one to hand over: it is the
            // same static (SimConfig.ScrimmagePerSide) that GameBootstrap and NetScrimmageMatch each
            // clamp before they spawn anybody, and StartNetworkedMatch writes it on EVERY peer from
            // the host's config byte. Host and client therefore resolve the identical role from the
            // identical two numbers with NOTHING extra on the wire.
            //
            // NO REACHABLE OUT-OF-RANGE PAIR, by three independent clamps: perSide is clamped to
            // 2..11 here (2 is the shirt invariant - a side of 1 has no legal outfield shirt at all,
            // and 11 is the length of the authored table), shirt is clamped to 0..perSide-1 here, and
            // SimConfig.Formation/PositionAnchor clamp again on the way in. A shirt past the roster
            // degrades to the LAST authored row (the striker) - wrong-but-legal, not an exception in
            // the middle of a match. NetSession.SlotAllowed already refuses to SEAT a human past the
            // per-side cap, so this is the belt under those braces rather than the only guard.
            PerSide = Mathf.Clamp(SimConfig.ScrimmagePerSide, 2, 11);
            Shirt   = Mathf.Clamp(shirt, 0, PerSide - 1);
            Slot    = SimConfig.PositionOf(PerSide, Shirt);
            _anchor = SimConfig.PositionAnchor(PerSide, Shirt);

            // Stagger the think clock by shirt so 22 brains never decide on the same frame. The
            // golden-ratio step spreads ANY roster size evenly with no table and no two shirts
            // landing on the same slice.
            _thinkLeft = (Shirt * 0.618f % 1f) / ThinkHz;

            Ragdoll.FacingRotation = Quaternion.LookRotation(new Vector3(0f, 0f, AttackZ), Vector3.up);
        }

        // Called by ScrimmageGame each frame for every AI (non-controlled) outfielder. THE ONLY
        // CALLER. Two rates live here: this method (timers, steering, gait - every frame) and Think
        // (phase, slot, press rank, marking, on-ball choice - at ThinkHz).
        public void AiTick(bool isClosest)
        {
            if (Ragdoll == null || Ragdoll.Pelvis == null || _ball == null) return;
            // HOST-ONLY GUARD, not a null-safety habit. A client's Footballer is built with a null
            // game on purpose (NetScrimmageMatch.SpawnBody) precisely so it has no brain; if one is
            // ever ticked there anyway it must do NOTHING rather than fight the snapshot puppet and
            // diverge. This is the third of the three guarantees named in the class comment.
            if (_game == null) return;
            // Knocked over: the Knockdown component owns the body. Give the ball back first -
            // a felled bot must not keep carry ownership (nor keep the ball phasing through it).
            if (IsDown) { SetCarryCollision(false); _carrying = false; _haveTarget = false; return; }
            Ragdoll.ClearPoseOverrides();
            float dt = Time.deltaTime;
            if (_kickCooldown > 0f) _kickCooldown -= dt;
            if (_carryTouchTimer > 0f) _carryTouchTimer -= dt;
            // Reaction lag runs on the FRAME clock, not the think clock: the delay is in seconds and
            // has to mean seconds, or an 8 Hz quantisation would swallow Insane's 0.09 s entirely.
            if (_reactLeft > 0f && (_reactLeft -= dt) <= 0f) _acting = _seen;

            Vector3 me = Pos; me.y = 0f;
            Vector3 ball = _ball.transform.position; ball.y = 0f;
            float ballDist = Vector3.Distance(me, ball);
            bool onBall = ballDist < SimConfig.AiChaseStopDist + SimConfig.BallRadius + 0.35f;

            // A carry that has lost the ball must end on the FRAME, not at the next think: otherwise
            // DriveCarry keeps kicking at a ball up to 125 ms after somebody else took it.
            if (_carrying && ballDist > SimConfig.DribbleLoseRadius)
            { _carrying = false; SetCarryCollision(false); _thinkLeft = 0f; }

            // Rethink on the clock, or IMMEDIATELY when this body's job could have changed under it -
            // it just became (or stopped being) the nearest man, or the ball is at its feet. Without
            // those two escapes the 8 Hz clock would let a bot stand over a live ball for 125 ms.
            _thinkLeft -= dt;
            // NOT "|| onBall". Any bot within 1.17 m of the ball would have re-run the whole Think at
            // frame rate instead of 8 Hz, and both difficulty gates inside OnBallAct are per-call
            // Random rolls - so a blocked-lane shot became p=0.05 PER FRAME (about a third of a second
            // at Insane, 0.04 s at Normal) and "a good brain will not fire into a defender's shins"
            // stopped meaning anything. It also multiplied the cost model by the frame rate for exactly
            // the bodies doing the most work. The remaining escapes still cover a genuinely new
            // situation; possession changes reach it through isClosest.
            if (_thinkLeft <= 0f || !_haveTarget || isClosest != _wasClosest)
            {
                _thinkLeft = 1f / ThinkHz;
                _wasClosest = isClosest;
                Think(me, ball, ballDist, onBall, isClosest);
            }

            // Carrying the ball uses a slightly different drive (nudges the ball ahead); everything
            // else is a plain run to the cached target.
            if (_carrying) DriveCarry(me, _target, ball);
            else { SetCarryCollision(false); Drive(me, _target); }
        }

        bool _carrying;   // set by OnBallAct when the decision is to dribble; cleared by every Think

        // THE DECISION, at ThinkHz. Everything the brain needs comes from two fused indexed passes -
        // mates give the press rank and the spacing push, opponents give the nearest man to the ball
        // and the man in my zone - so positioning costs ~2 x perSide distance tests per THINK rather
        // than per frame. No allocation: indexed for over List<T> (no enumerator), struct locals,
        // struct-out returns. No raycast: Passing.LaneClear is analytic point-to-segment.
        void Think(Vector3 me, Vector3 ball, float ballDist, bool onBall, bool isClosest)
        {
            var tune  = SimConfig.Ai;
            var mates = _game.TeamList(Team);
            var opps  = _game.TeamList(Team == 0 ? 1 : 0);
            float hw = _game.HalfWidth, hl = _game.HalfLength;

            // My zone in world space: the authored anchor, phase-independent. Point-mirrored by
            // AttackZ so Away's shape is Home's rotated 180 degrees rather than reflected.
            Vector3 zone = new Vector3(_anchor.x * hw * AttackZ, 0f, -_anchor.y * hl * AttackZ);

            int rank = 0;
            Vector3 push = Vector3.zero;
            float sep = SimConfig.AiSeparationRadius;
            for (int i = 0; i < mates.Count; i++)
            {
                var o = mates[i];
                if (o == null || o == this || o.IsKeeper || o.IsDown) continue;
                Vector3 op = o.Pos; op.y = 0f;
                if (Vector3.Distance(op, ball) < ballDist) rank++;
                Vector3 d = me - op;
                float dist = d.magnitude;
                if (dist > 0.01f && dist < sep) push += d / dist * (sep - dist);
            }
            _rank = rank;

            float oppToBall = 999f;
            Footballer mark = null; float markD = float.MaxValue;
            for (int i = 0; i < opps.Count; i++)
            {
                var o = opps[i];
                if (o == null || o.IsKeeper || o.IsDown) continue;
                Vector3 op = o.Pos; op.y = 0f;
                float db = Vector3.Distance(op, ball);
                if (db < oppToBall) oppToBall = db;
                // Pick up by proximity to MY ZONE, not to my body. Marking by body distance is how
                // two defenders end up on one striker while a runner arrives behind them free.
                float dz = Vector3.Distance(op, zone);
                if (dz < markD) { markD = dz; mark = o; }
            }
            _mark = markD <= hw * 0.55f ? mark : null;

            // ---- PHASE ----
            // Restart is INFERRED (ball parked on the centre spot) because ScrimmageGame keeps its
            // kickoff freeze private; a PlayLive flag is a contract request. The inference misfires
            // only if a live ball comes to rest within 1.6 m of the exact centre, which costs one
            // 125 ms slice of shape-holding and nothing else. Loose means genuinely nobody's: no
            // carrier registered anywhere, and either the ball is travelling or neither side is near.
            Vector3 bv = _ball.Rb != null ? _ball.Rb.linearVelocity : Vector3.zero;
            Vector3 bvFlat = new Vector3(bv.x, 0f, bv.z);
            float ballSpeed = bvFlat.magnitude;
            Phase seen;
            if (ball.sqrMagnitude < RestartRadius * RestartRadius && ballSpeed < 0.6f) seen = Phase.Restart;
            else if (Dribble.Holder == null && _ball.DribbleCarrier == null
                     && (ballSpeed > 6f || Mathf.Min(ballDist, oppToBall) > 3.5f)) seen = Phase.Loose;
            else seen = _game.PossessionTeam == Team ? Phase.Attack : Phase.Defend;

            if (seen != _seen)
            {
                _seen = seen;
                // DIFFICULTY BUYS REACTION TIME and, here, nothing else. Until this expires the
                // brain keeps acting on the phase it last committed to - so an Easy defender really
                // does keep running forward for half a second after his team loses the ball, and an
                // Insane one has turned in 0.09 s. At None (9.99 s) the phase never commits at all,
                // which is the tier's own description: built, takes no decisions.
                //
                // ONLY re-arm when the newly seen phase is not already the one being acted on. Arming
                // unconditionally was a deadlock: `seen` is driven by PossessionTeam, which is a bare
                // nearest-outfielder test and flips whenever two jostling players swap order, and by a
                // Loose test whose threshold uses this bot's OWN distance to the ball, so it crosses as
                // he runs in. Any A-B chatter faster than ReactionDelay (0.32 s at Normal, 0.55 s at
                // Easy, against a 0.125 s think interval) re-armed forever and _acting never committed
                // - leaving it stuck on its initial value, Phase.Restart, where press is false for
                // everyone. Twenty outfielders walking to their kickoff anchors, ignoring the ball.
                _reactLeft = _seen == _acting ? 0f : tune.ReactionDelay;
            }
            if (_reactLeft <= 0f) _acting = _seen;
            // A restart is a whistle, not a read. Nobody needs half a second to notice a kickoff.
            //
            // But the Restart TEST is only a guess - a ball at rest within RestartRadius of the centre
            // spot - and it is equally true of any live ball that simply stops there. Left un-timed that
            // is a permanent deadlock: press is false for every bot, OnBallAct is gated off, and
            // ScrimmageGame's StuckBallWatchdog cannot rescue it because that only fires near a WALL.
            // The ball would sit on the centre spot for the rest of the clock with only a human able to
            // touch it. So hold Restart for the length of a real kickoff freeze and then fall through to
            // Loose, which sends the nearest bots at it.
            if (_seen == Phase.Restart)
            {
                if (_restartSince < 0f) _restartSince = Time.time;
                if (Time.time - _restartSince <= SimConfig.ScrimKickoffFreeze)
                { _acting = Phase.Restart; _reactLeft = 0f; }
                else
                { _seen = Phase.Loose; if (_reactLeft <= 0f) _acting = Phase.Loose; }
            }
            else _restartSince = -1f;

            float prog01 = BallProgress(ball);
            _target = ShapeSpot(ball, prog01, _acting, tune);

            // ---- WHO PRESSES ----
            // This one block is the answer to "they all chase the ball": at most two men leave shape.
            bool press;
            switch (_acting)
            {
                case Phase.Restart: press = false; break;
                // A loose ball is worth a second man, but only one who is actually close enough to
                // reach it - not the far-side full back.
                case Phase.Loose:   press = _rank == 0 || (_rank == 1 && ballDist < 14f); break;
                // Attacking, exactly one man goes to the ball: the carrier. Everyone else is a
                // passing option, which is the whole point of holding shape.
                case Phase.Attack:  press = _rank == 0 || isClosest; break;
                // Defending, the nearest man presses; a SECOND joins only where doubling up pays -
                // the ball in our own third, or us winning the race so the tackle is on.
                default:            press = _rank == 0
                                            || (_rank == 1 && (prog01 < ClearProg + 0.08f
                                                               || ballDist < oppToBall + 1.5f)); break;
            }

            if (press && _rank == 0)
            {
                // LEAD the ball instead of running at where it IS. Running at the current position is
                // why a bot trails a rolling ball for ever. The lead is capped at PressLead seconds so
                // nobody sprints to a point the ball will never reach.
                float top = Mathf.Max(1f, SimConfig.AiOutfieldSpeed * PaceMul * Mathf.Max(0.35f, tune.PaceUse));
                _target = ball + bvFlat * Mathf.Clamp(ballDist / top, 0f, PressLead);
                // The lunge itself: reach is SimConfig.TackleReach for every tier (see TryTackle).
                // What the tier changes is whether the bot commits at all.
                if (_acting != Phase.Attack && ballDist < SimConfig.AiTackleRange && _kickCooldown <= 0f
                    && Random.value < Mathf.Lerp(0.35f, 1f, tune.Decision))
                    TryTackle(me, ball);
            }
            else if (press)
            {
                // COVER: goal-side of the ball, pulled part-way back to my own slot so the second man
                // screens the space behind the presser instead of standing next to him.
                _target = ball - new Vector3(0f, 0f, AttackZ) * PressCoverBack;
                _target.x = Mathf.Lerp(_target.x, zone.x, 0.45f);
            }
            else if (_acting == Phase.Defend && _mark != null && _anchor.y >= 0.5f)
            {
                // Zonal pick-up, DEFENDERS ONLY (anchor y >= 0.5 is the back line). A winger tracking
                // a full back the length of the pitch is not football, it is the old blob with extra
                // steps. How far the blend goes is decision quality.
                Vector3 mp = _mark.Pos; mp.y = 0f;
                Vector3 own = OwnGoal; own.y = 0f;
                Vector3 goalSide = mp + (own - mp).normalized * 1.6f;
                _target = Vector3.Lerp(_target, goalSide, Mathf.Lerp(0.25f, 0.8f, tune.Decision));
            }

            // ---- ON BALL ----
            // Only the man over his own team's ball takes a decision, and _rank == 0 makes that
            // exactly one player per team even when two bodies are inside the same metre.
            _carrying = false;
            if (onBall && _rank == 0 && _kickCooldown <= 0f && Dribble.Holder == null
                && _acting != Phase.Restart && _game.PossessionTeam == Team)
                OnBallAct(me, ball, prog01, tune, mates, opps);

            // EXECUTION ERROR as standing in slightly the wrong place. Refreshed per think, never per
            // frame, or it reads as jitter rather than as a defender who is a yard out. Skipped for a
            // presser: fumbling the approach to the ball is the tackle gate's job, not this.
            if (!press && !_carrying && tune.ErrorRate > 0.01f)
                _target += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f))
                           * (ShapeSlop * Mathf.Clamp01(tune.ErrorRate));

            _target += push;   // teammate spacing, so a shape never collapses into a queue
            _target.x = Mathf.Clamp(_target.x, -hw + 0.5f, hw - 0.5f);
            _target.z = Mathf.Clamp(_target.z, -hl + 0.5f, hl - 0.5f);
            _haveTarget = true;
        }

        /// <summary>0 at our own goal line, 1 at the goal we attack. Mirrors for Away through AttackZ,
        /// so one scalar describes "how far up the pitch play is" for both teams.</summary>
        float BallProgress(Vector3 ball)
            => Mathf.Clamp01((ball.z * AttackZ + _game.HalfLength) / Mathf.Max(1f, 2f * _game.HalfLength));

        // The slot this player holds in the block, in world space.
        //
        // SimConfig.PositionAnchor is the DEFENSIVE shape: every z is inside own half, which is why
        // the whole table is legal at a kickoff. Attack is that same shape TRANSLATED up the pitch,
        // and a defender translates less than a forward (0.45 + 0.55 x how attacking the role is) so
        // the shape stretches into a 4-3-3 instead of sliding as a slab. Measured at 11-a-side
        // (half-length 52.5) with the ball on the opponent goal line: ST lands at z = +46, i.e. 6.5 m
        // off the line; CB at z = -1.2, a high line just short of halfway. Defending with the ball on
        // our own line: CB at -34.6 (17.9 m off our goal line), ST dropped to -4.2, just inside our
        // half. Those are the two extremes; everything between is the lerp.
        //
        // HOW FAR along that translation we are is the one scalar - driven by where the BALL is, not
        // by who is nearest it. Ten players read it and each lands somewhere different. That is the
        // whole anti-blob mechanism, and it is four lines long.
        Vector3 ShapeSpot(Vector3 ball, float prog01, Phase phase, SimConfig.AiTuning tune)
        {
            float hw = _game.HalfWidth, hl = _game.HalfLength;
            // Arena not sized yet (Configure has not run): the spawn spot, rather than collapsing
            // every slot onto the centre spot.
            if (hw < 1f || hl < 1f) return _homeSpot;

            float ax = _anchor.x * hw * AttackZ;
            float zDefend = -_anchor.y;                                                   // attack-direction units
            float zAttack = zDefend + ShapeAttackShift * (0.45f + 0.55f * (1f - _anchor.y));

            float pushUp = phase switch
            {
                Phase.Attack => Mathf.Lerp(0.35f, 1.00f, prog01),   // we have it: commit with the ball
                Phase.Defend => Mathf.Lerp(0.00f, 0.55f, prog01),   // they have it: press up, then sit
                Phase.Loose  => Mathf.Lerp(0.18f, 0.78f, prog01),   // nobody's: between the two
                _            => 0f,                                 // Restart: the authored shape
            };

            Vector3 spot = new Vector3(ax, 0f, Mathf.Lerp(zDefend, zAttack, pushUp) * hl * AttackZ);
            if (phase == Phase.Restart) return spot;

            // Slide with the ball's x, CAPPED. Uncapped is how the old band pulled every winger into
            // the middle of the pitch behind the ball.
            float pull = phase == Phase.Defend ? ShapeBallPullDef : ShapeBallPullAtk;
            spot.x = ax + Mathf.Clamp((ball.x - ax) * pull, -ShapeMaxDrift * hw, ShapeMaxDrift * hw);

            // A defender is never caught upfield of the ball, whatever the block says.
            if (_anchor.y >= 0.5f && (ball.z - spot.z) * AttackZ < 0f) spot.z = ball.z - AttackZ * 2.0f;

            // DECISION QUALITY is the only thing that erodes shape, and it is the last word: a poor
            // brain drifts ball-ward rather than holding its line. Even Easy (0.35) keeps 71% of its
            // slot, so a low tier is LOOSE, not the blob this replaced.
            float hold = Mathf.Lerp(0.55f, 1f, tune.Decision);
            if (hold >= 0.999f) return spot;
            return Vector3.Lerp(new Vector3(ball.x, 0f, ball.z - AttackZ * 3f), spot, hold);
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
        //
        // PACE USE is a fraction of the pace this body ALREADY OWNS - SimConfig.AiPace (0.80x-1.24x,
        // derived from team+shirt) times tune.PaceUse (0.75 Easy, 0.88 Normal, 1.00 Insane). A tier
        // can only ever hold a bot back from its own top speed; it can never hand out speed the body
        // does not have. That is SimConfig's rule, written into the units here instead of left as an
        // intention. The 0.35 floor is for the None tier, whose PaceUse is 0: statues in formation
        // look broken, so they walk into shape and stop there.
        void Drive(Vector3 me, Vector3 target)
        {
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.zero;
            float top = SimConfig.AiOutfieldSpeed * PaceMul * Mathf.Max(0.35f, SimConfig.Ai.PaceUse);
            // Ease off over the last ~2 m instead of running flat out into the slot and stopping
            // dead. That overshoot-and-snap is a big part of why the old block moved like a shoal.
            float speed = dist > 0.4f ? top * Mathf.Clamp01(dist / 2.2f + 0.35f) : 0f;
            Ragdoll.MoveInput = dir * speed;

            if (dir.sqrMagnitude > 0.01f)
                Ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);

            // Normalised against THIS body's top speed, not the shared base, or a quick player's gait
            // would run past 1 and a slow one would never reach a full stride.
            RunGait(speed / Mathf.Max(0.1f, top));
        }

        // AI tackle: lunge at the ball and, if it reaches, win it off the opponent.
        //
        // REACH IS NOT SCALED BY DIFFICULTY - SimConfig.TackleReach for every tier, the same figure
        // the human's tackle uses. What a tier buys is how often the lunge is attempted at all (the
        // Decision-weighted gate at the call site in Think). Deliberate: a tier may change when and
        // how well a bot acts, never what its body can do.
        void TryTackle(Vector3 me, Vector3 ball)
        {
            _kickCooldown = SimConfig.TackleCooldown;
            MatchProbe.TackleAttempt(ProbeTackle.Ai);   // ATTEMPT. The win is the reach test three lines down.
            Vector3 to = ball - me; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                Ragdoll.AddVelocityToAll(to.normalized * SimConfig.TackleLunge);
            if (to.magnitude <= SimConfig.TackleReach)
                _game.WinBallForAi(this);
        }

        // On our own ball, in priority order: SHOOT (in range, facing, lane), PASS (the shared
        // Passing model), CLEAR (own third, pressed, nothing on), CARRY. Sets _target for the
        // steering path and _carrying when the choice is to dribble.
        //
        // DIFFICULTY touches this in exactly three places, all of them JUDGEMENT and none of them
        // ability: whether the bot will fire into a blocked lane, whether it bothers to look for a
        // pass at all, and how much error the strike carries. Range, power and reach are the same at
        // every tier.
        void OnBallAct(Vector3 me, Vector3 ball, float prog01, SimConfig.AiTuning tune,
                       System.Collections.Generic.List<Footballer> mates,
                       System.Collections.Generic.List<Footballer> opps)
        {
            _target = ball;
            Vector3 toGoal = TargetGoal - ball; toGoal.y = 0f;
            float goalDist = toGoal.magnitude;
            Vector3 gdir = goalDist > 0.1f ? toGoal / goalDist : new Vector3(0f, 0f, AttackZ);
            bool facingGoal = Vector3.Dot(gdir, new Vector3(0f, 0f, AttackZ)) >= SimConfig.AiShootConeDot;
            float press01 = Passing.Pressure01(ball, opps);
            // The ONE conversion from tier to execution. ErrorRate is a SCALE ON SCATTER, so its
            // complement is the accuracy the shared Passing error model wants. At Normal (0.40) that
            // is 0.60, which is within a whisker of the flat AiPassAccuracy 0.62 this replaced - so
            // the balance anchor did not move when difficulty arrived.
            float acc01 = 1f - Mathf.Clamp01(tune.ErrorRate);

            // SHOOT. A good brain will not fire into a defender's shins; a poor one often does, which
            // is self-nerfing and legible on screen instead of being an invisible stat.
            if (goalDist < SimConfig.AiShootRange && facingGoal)
            {
                Vector3 aim = ShotAim(ball, tune);
                if (LaneClear(ball, aim, opps) || Random.value > tune.Decision) { Shoot(ball, aim); return; }
            }

            // PASS. Whether the bot even LOOKS is the decision gate (Easy 0.64, Normal 0.78, Insane
            // 0.97); how well it lands is acc01. The option itself comes from the shared model, so a
            // bot's pass has the same weight, lead and through-ball logic as a human's.
            if (Random.value < Mathf.Lerp(0.45f, 1f, tune.Decision))
            {
                Vector3 aimDir = new Vector3(0f, 0f, AttackZ);
                // Chip it when the route out is crowded, roll it when it isn't.
                bool loft = !LaneClear(ball, ball + aimDir * 9f, opps);
                float charge = SimConfig.AiPassCharge;
                if (Passing.BestTarget(ball, aimDir, AttackZ, loft, charge, 1f, mates, opps, this, out var opt))
                {
                    _kickCooldown = SimConfig.AiKickCooldown;
                    SetCarryCollision(false);   // hand the ball back before striking it
                    _game.NoteAiPass(Ragdoll);
                    Passing.Launch(_ball, opt.aim, loft, charge, 1f, Ragdoll,
                                   Passing.ScatterDeg(acc01, Vector3.Distance(ball, opt.aim), press01, charge, false),
                                   Passing.Wobble(acc01, false));
                    return;
                }
            }

            // CLEAR. Pressed in our own third with nothing on: get it long and toward the touchline
            // rather than dribble out of our own box. Still a Passing.Launch on purpose - a clearance
            // then carries the same error model as everything else and is interceptable like anything
            // else, instead of being a magic teleport upfield. Flagged firstTime so it is scruffier.
            if (prog01 < ClearProg && press01 > 0.45f)
            {
                _kickCooldown = SimConfig.AiKickCooldown;
                SetCarryCollision(false);
                _game.NoteAiPass(Ragdoll);
                float side = ball.x >= 0f ? 1f : -1f;
                Vector3 aim = new Vector3(Mathf.Clamp(side * _game.HalfWidth * 0.8f,
                                                     -_game.HalfWidth + 1f, _game.HalfWidth - 1f),
                                          SimConfig.BallRadius,
                                          Mathf.Clamp(ball.z + AttackZ * Mathf.Min(24f, _game.HalfLength),
                                                      -_game.HalfLength + 1f, _game.HalfLength - 1f));
                Passing.Launch(_ball, aim, true, 1f, 1f, Ragdoll,
                               Passing.ScatterDeg(acc01, Vector3.Distance(ball, aim), press01, 1f, true),
                               Passing.Wobble(acc01, true));
                return;
            }

            // CARRY: dribble toward goal, steering around the nearest defender in the way.
            _carrying = true;
            _target = ball + gdir * 6f + DefenderAvoidOffset(ball, gdir);
        }

        // Where an AI shot is AIMED: the far corner relative to the ball's x (away from the centre a
        // keeper shadows), scattered by the tier's execution error. Measured: at Normal (ErrorRate
        // 0.40) the scatter multiplier is 0.94, i.e. 1.03 m against the 1.1 m flat figure this
        // replaced - so Normal is unchanged and only the ends of the ladder move (Easy 1.5 m,
        // Insane 0.55 m). Aim, and only aim, lives here.
        Vector3 ShotAim(Vector3 ball, SimConfig.AiTuning tune)
        {
            float halfGoal = SimConfig.GoalWidth * 0.5f - SimConfig.BallRadius - 0.3f;
            float side = ball.x >= 0f ? -1f : 1f;
            float scat = SimConfig.AiShotScatter * Mathf.Lerp(0.35f, 2.0f, Mathf.Clamp01(tune.ErrorRate));
            float aimX = Mathf.Clamp(side * halfGoal + Random.Range(-scat, scat), -halfGoal, halfGoal);
            float aimY = Mathf.Clamp(SimConfig.GoalHeight * 0.55f
                                     + Random.Range(-0.3f, 0.3f) * (0.4f + Mathf.Clamp01(tune.ErrorRate)),
                                     0.4f, SimConfig.GoalHeight - 0.2f);
            return new Vector3(aimX, aimY, TargetGoal.z);
        }

        // Arced shot on goal through the scrimmage lofted launch (airborne, no controllable spin),
        // using LaunchTo's ballistic solve so it dips under the bar rather than sailing over it.
        //
        // CONTRACT: this is the ONE place an AI strikes at goal, and the LaunchTo call is the only
        // line of it that is a flight model. When the shooting author lands the shared AI shot entry
        // point, that single line is replaced and nothing else in this file changes - the aim
        // decision stays in ShotAim, the flight moves there. There is deliberately no second path.
        //
        // The SetCarryCollision(false) is new and is a fix: a bot that shot mid-carry used to keep
        // its limbs phased through the ball it had just struck.
        void Shoot(Vector3 ball, Vector3 aim)
        {
            _kickCooldown = SimConfig.AiKickCooldown;
            SetCarryCollision(false);
            _game.NoteShotBy(Ragdoll);
            // Flight time scales a little with distance so near shots stay flat-ish, far ones arc more.
            float t = Mathf.Clamp(Vector3.Distance(ball, aim) / 22f, 0.35f, 0.9f);
            _ball.LaunchTo(aim, t, Vector3.zero, 0f);
            _target = ball;
        }

        // Lane checks live in Passing now, so a bot and a human read the same blocked lane. Analytic
        // point-to-segment, NOT a raycast - which is why 22 brains can afford to ask.
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
        //
        // FIRST TOUCH is the difficulty knob here. tune.FirstTouch IS the bot's effective Control
        // level, replacing the flat AiDribbleTightness 0.55 that sat between Normal (0.60) and Easy
        // (0.35), and it also scales the per-touch aim scatter (1.8x AiTouchErrorDeg at zero touch
        // down to 0.5x at full). So an Insane carrier takes short clean touches and an Easy one
        // knocks it too far and off line - at the SAME top speed. Nothing about the body changes.
        void DriveCarry(Vector3 me, Vector3 target, Vector3 ball)
        {
            var tune = SimConfig.Ai;
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : new Vector3(0f, 0f, AttackZ);
            Ragdoll.MoveInput = dir * (SimConfig.AiCarrySpeed * PaceMul * Mathf.Max(0.35f, tune.PaceUse));
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

            float tight = Mathf.Clamp01(tune.FirstTouch);
            float interval = Dribble.StrideInterval(Ragdoll, false);
            float touchDist = Dribble.TouchDistance(Ragdoll.GroundSpeed, tight, false);
            Dribble.Touch(_ball, me, dir, Ragdoll.MoveInput, interval, touchDist,
                          SimConfig.AiTouchErrorDeg * Mathf.Lerp(1.8f, 0.5f, tight));
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
