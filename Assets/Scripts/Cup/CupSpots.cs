using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Every position a cup round is laid out against, in one place: the penalty spot, the
    /// seeded free-kick spots (one per kick PAIR, generated lazily so sudden death never runs
    /// off the end of a schedule - design 2.1), the run-up start, the wall centre, the keeper's
    /// line, the lineup marks, the referee's mark, the hidden parking spot for a body that is not
    /// in this kick, and the two ball tests (goal / out of play) the verdict is built on.
    ///
    /// Geometry conventions (the single-goal arena): the goal is at <see cref="SimConfig.GoalCenter"/>
    /// (+Z), the taker shoots toward +Z, "behind the goal" is beyond it in +Z, and the lineups
    /// stand 1 m outside the 18-yard line (design 7.3). All ground positions are y = 0 (feet);
    /// only the ball spot carries the ball radius.
    ///
    /// The instance side wraps ONE per-round spots stream (CupSalts.Spots) and remembers every
    /// spot it has drawn in order, so peers that derive the same stream from the cup seed land on
    /// the same spot for the same pair index, however many kicks the round needs.
    /// </summary>
    public sealed class CupSpots
    {
        readonly SeededRng _rng;
        readonly List<Vector3> _spots = new List<Vector3>();

        /// <summary>Wrap the round's Spots stream (Setup.Stream(CupSalts.Spots(stage, index))).</summary>
        public CupSpots(SeededRng spotsStream)
        {
            _rng = spotsStream ?? new SeededRng(1u);
        }

        /// <summary>How many spots have been drawn so far.</summary>
        public int Count => _spots.Count;

        /// <summary>
        /// The free-kick spot of a kick pair (CupRoundRules.PairIndex). Extends the schedule from
        /// the stream on demand: asking for pair 7 draws pairs 0..7 in order, so the sequence is
        /// identical on every peer regardless of when each first asked.
        /// </summary>
        public Vector3 Spot(int pairIndex)
        {
            if (pairIndex < 0) pairIndex = 0;
            while (_spots.Count <= pairIndex) _spots.Add(Roll(_rng));
            return _spots[pairIndex];
        }

        // ---- the generators ------------------------------------------------------------------

        /// <summary>
        /// One legal cup free-kick spot: CupTuning.FreeKickMinDist..FreeKickMaxDist metres in front
        /// of the goal line (measured along the goal axis, the same way SetPieceMap.RandomSpot
        /// measures its band), up to CupTuning.FreeKickHalfWidth either side. The band starts past
        /// the 16.5 m box front, so a spot is always outside the box. Consumes two draws.
        /// </summary>
        public static Vector3 Roll(SeededRng rng)
        {
            float x = rng.Range(-CupTuning.FreeKickHalfWidth, CupTuning.FreeKickHalfWidth);
            float dist = rng.Range(CupTuning.FreeKickMinDist, CupTuning.FreeKickMaxDist);
            // Keep the spot on the playing surface whatever the pitch: the touchline is the hard
            // limit, with the same 2 m margin the placed-spot generator keeps.
            float halfX = Mathf.Max(1f, PitchLayout.HalfWidth - 2f);
            x = Mathf.Clamp(x, -halfX, halfX);
            var g = SimConfig.GoalCenter;
            return new Vector3(g.x + x, SimConfig.BallRadius, g.z - dist);
        }

        /// <summary>The penalty spot: CupTuning.PenaltyDistance out on the goal axis, at ball height.</summary>
        public static Vector3 PenaltySpot
        {
            get
            {
                var g = SimConfig.GoalCenter;
                return new Vector3(g.x, SimConfig.BallRadius, g.z - CupTuning.PenaltyDistance);
            }
        }

        /// <summary>The spot for a format: the penalty spot, or this pair's free-kick spot.</summary>
        public Vector3 SpotFor(CupFormat format, int pairIndex)
        {
            return format == CupFormat.Penalties ? PenaltySpot : Spot(pairIndex);
        }

        // ---- the free-kick scatter (owner's call: no lineup in Free Kicks) ----------------------

        /// <summary>
        /// The salt family of the scatter streams: forked off the round's Spots stream by
        /// (pair, kicking side), inside a 0x9000 block that no CupSalts family and not the bot's
        /// 0x8000 block uses. SeededRng.Fork is a pure function of (seed, salt) and never advances
        /// the parent, so drawing the marks leaves the spot schedule untouched and every peer that
        /// derives the same Spots stream lands on the same marks.
        /// </summary>
        const uint MarksFamily = 0x9000u;
        /// <summary>The pre-round idle layout's salt (no kick placed yet: both sides in the near band).</summary>
        const uint IdleMarksSalt = MarksFamily + 0x0FFFu;
        /// <summary>How many times a mark is redrawn before an overlapping one is accepted anyway.</summary>
        const int MarkRetries = 10;

        /// <summary>
        /// The scattered marks of one kick (design change, Free Kicks only): `ownCount` bodies of
        /// the taker's side CupTuning.FreeKickTeamDepthMin..Max behind the ball, fanned to either
        /// side of the run-up line (a loose group, not a line); `oppCount` bodies of the other side
        /// CupTuning.FreeKickOppDepthMin..Max back on the taker's LEFT (the referee stands on his
        /// right), away from the camera line. Deterministic per (stream, pair, side) so peers agree;
        /// marks keep CupTuning.FreeKickMarkClearance from each other, from the run-up start and
        /// from the referee's mark. Facings look at the goal with a seeded jitter. Every list is
        /// cleared first and filled in order, so a caller assigning them in a stable body order
        /// gets the same body on the same mark everywhere.
        /// </summary>
        public void FreeKickMarks(int pairIndex, CupSide kicker, Vector3 spot, int ownCount, int oppCount,
                                  List<Vector3> own, List<Quaternion> ownFacing, List<Vector3> opp, List<Quaternion> oppFacing)
        {
            uint salt = MarksFamily + (uint)Mathf.Max(0, pairIndex) * 2u + (kicker == CupSide.B ? 1u : 0u);
            var rng = _rng.Fork(salt);
            var taken = new List<Vector3> { RunUpStart(spot), RefereeMark(spot) };
            Scatter(rng, spot, ownCount, CupTuning.FreeKickTeamDepthMin, CupTuning.FreeKickTeamDepthMax,
                    CupTuning.FreeKickTeamLateralMin, CupTuning.FreeKickTeamLateralMax, 0, taken, own, ownFacing);
            Scatter(rng, spot, oppCount, CupTuning.FreeKickOppDepthMin, CupTuning.FreeKickOppDepthMax,
                    CupTuning.FreeKickOppLateralMin, CupTuning.FreeKickOppLateralMax, -1, taken, opp, oppFacing);
        }

        /// <summary>
        /// The idle layout before a free-kick round starts (Configure, the coin toss): nobody is
        /// on the ball yet, so BOTH sides stand in the near band, the human team on the left of
        /// the ball->goal line and the other side on its right, keepers among them. The band is
        /// measured back from the point of that line at the box front (PenaltyBoxDepth from
        /// goal), not from the spot itself: a 28 m spot would otherwise put the captains 20 m
        /// from the coin-toss marks, past the ceremony's approach timeout, and the Intro's real
        /// placement moves everyone under the card anyway. The ball on the spot is kept clear.
        /// </summary>
        public void FreeKickIdleMarks(Vector3 spot, int teamCount, int otherCount,
                                      List<Vector3> team, List<Quaternion> teamFacing, List<Vector3> other, List<Quaternion> otherFacing)
        {
            var rng = _rng.Fork(IdleMarksSalt);
            Vector3 ground = Ground(spot);
            Vector3 toGoal = SimConfig.GoalCenter - ground;
            toGoal.y = 0f;
            float dist = toGoal.magnitude;
            float pull = Mathf.Max(0f, dist - SimConfig.PenaltyBoxDepth);
            Vector3 anchor = dist > 1e-3f ? ground + toGoal / dist * pull : ground;
            var taken = new List<Vector3> { ground, RunUpStart(spot), RefereeMark(spot) };
            Scatter(rng, anchor, teamCount, CupTuning.FreeKickTeamDepthMin, CupTuning.FreeKickTeamDepthMax,
                    CupTuning.FreeKickTeamLateralMin, CupTuning.FreeKickTeamLateralMax, -1, taken, team, teamFacing);
            Scatter(rng, anchor, otherCount, CupTuning.FreeKickTeamDepthMin, CupTuning.FreeKickTeamDepthMax,
                    CupTuning.FreeKickTeamLateralMin, CupTuning.FreeKickTeamLateralMax, 1, taken, other, otherFacing);
        }

        /// <summary>
        /// Draw `count` marks behind `spot`: depth along -DirToGoal in [depthMin, depthMax], lateral
        /// along the taker's right in [latMin, latMax] scaled up with depth (a fan), on `side`
        /// (+1 right, -1 left, 0 = seeded per body). A draw closer than the clearance to any earlier
        /// mark is retried a few times, then accepted (a big Co-op group must never fail to place).
        /// Marks are clamped onto the pitch. Appends to `taken`; fills `pos` / `facing`.
        /// </summary>
        static void Scatter(SeededRng rng, Vector3 spot, int count, float depthMin, float depthMax, float latMin, float latMax,
                            int side, List<Vector3> taken, List<Vector3> pos, List<Quaternion> facing)
        {
            pos.Clear();
            facing.Clear();
            if (count <= 0) return;
            Vector3 back = -DirToGoal(spot);
            Vector3 right = Vector3.Cross(Vector3.up, DirToGoal(spot)).normalized;
            Vector3 origin = Ground(spot);
            float halfX = Mathf.Max(2f, PitchLayout.HalfWidth - 1.5f);
            float minZ = PitchLayout.FarGoalLineZ + 1.5f;
            for (int i = 0; i < count; i++)
            {
                Vector3 best = origin + back * depthMin;
                for (int attempt = 0; attempt < MarkRetries; attempt++)
                {
                    float depth = rng.Range(depthMin, depthMax);
                    // The fan: the further back, the further out, so the group opens away from
                    // the run-up line the camera looks down.
                    float t = Mathf.InverseLerp(depthMin, depthMax, depth);
                    float latFloor = Mathf.Lerp(latMin, Mathf.Lerp(latMin, latMax, 0.5f), t);
                    float lat = rng.Range(latFloor, latMax);
                    float sign = side != 0 ? side : (rng.Chance(0.5f) ? 1f : -1f);
                    Vector3 p = origin + back * depth + right * (sign * lat);
                    p.x = Mathf.Clamp(p.x, -halfX, halfX);
                    p.z = Mathf.Max(p.z, minZ);
                    p.y = 0f;
                    best = p;
                    if (Clear(p, taken)) break;
                }
                taken.Add(best);
                pos.Add(best);
                facing.Add(WatchFacing(best, rng.Range(-CupTuning.FreeKickWatchYawJitter, CupTuning.FreeKickWatchYawJitter)));
            }
        }

        static bool Clear(Vector3 p, List<Vector3> taken)
        {
            float min = CupTuning.FreeKickMarkClearance * CupTuning.FreeKickMarkClearance;
            for (int i = 0; i < taken.Count; i++)
            {
                Vector3 d = taken[i] - p;
                d.y = 0f;
                if (d.sqrMagnitude < min) return false;
            }
            return true;
        }

        /// <summary>A scattered body's facing: toward the goal centre from its mark, turned `yawJitter` degrees.</summary>
        public static Quaternion WatchFacing(Vector3 mark, float yawJitter)
        {
            Vector3 to = SimConfig.GoalCenter - mark;
            to.y = 0f;
            var look = to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to.normalized, Vector3.up) : LineupFacing;
            return look * Quaternion.Euler(0f, yawJitter, 0f);
        }

        // ---- derived marks -------------------------------------------------------------------

        /// <summary>A spot's ground point (feet level).</summary>
        public static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);

        /// <summary>Flat unit direction from a spot toward the goal centre (+Z on the axis).</summary>
        public static Vector3 DirToGoal(Vector3 spot)
        {
            Vector3 to = SimConfig.GoalCenter - spot; to.y = 0f;
            return to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.forward;
        }

        /// <summary>The facing a body at `spot` has when it looks at the goal.</summary>
        public static Quaternion FacingGoal(Vector3 spot) => Quaternion.LookRotation(DirToGoal(spot), Vector3.up);

        /// <summary>
        /// Where the taker starts: CupTuning.RunUpDistance behind the ball along the ball->goal
        /// line, so he always approaches toward the goal wherever the spot is (FreeKickGame's
        /// RecomputeStrikerBase).
        /// </summary>
        public static Vector3 RunUpStart(Vector3 spot) => Ground(spot) - DirToGoal(spot) * CupTuning.RunUpDistance;

        /// <summary>The wall centre: regulation CupTuning.WallDistance from the ball on the ball->goal line.</summary>
        public static Vector3 WallCenter(Vector3 spot) => Ground(spot) + DirToGoal(spot) * CupTuning.WallDistance;

        /// <summary>
        /// The keeper's spot for a format: ON the line for a penalty (KeeperPenaltyStart), his
        /// normal dead-ball depth for a free kick (KeeperStart) - the same pair FreeKickGame uses.
        /// </summary>
        public static Vector3 KeeperLine(CupFormat format)
        {
            return format == CupFormat.Penalties ? SimConfig.KeeperPenaltyStart : SimConfig.KeeperStart;
        }

        /// <summary>The keeper's facing: out toward play (KeeperFaceDir).</summary>
        public static Quaternion KeeperFacing => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);

        /// <summary>
        /// The referee's mark during play (design 7.1): CupTuning.RefereeSideOffset to the side of
        /// the ball spot, level with it - on the taker's right as he faces the goal - facing him.
        /// </summary>
        public static Vector3 RefereeMark(Vector3 spot)
        {
            Vector3 dir = DirToGoal(spot);
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            return Ground(spot) + right * CupTuning.RefereeSideOffset;
        }

        /// <summary>The z of the lineups: 1 m outside the 18-yard line (design 7.3).</summary>
        public static float LineupZ => SimConfig.GoalCenter.z - SimConfig.PenaltyBoxDepth - CupTuning.LineupBehindBox;

        /// <summary>
        /// A lineup mark: the human team at x = -LineupX and the other side at +LineupX, each a line
        /// of bodies CupTuning.LineupSpacing apart centred on that x, facing the goal.
        /// `teamSide` is the side the local team / human is on (Setup.TeamSide).
        /// </summary>
        public static Vector3 LineupMark(bool onTeamSide, int index, int count)
        {
            float baseX = onTeamSide ? -CupTuning.LineupX : CupTuning.LineupX;
            float half = (Mathf.Max(1, count) - 1) * 0.5f;
            float x = baseX + (index - half) * CupTuning.LineupSpacing;
            return new Vector3(SimConfig.GoalCenter.x + x, 0f, LineupZ);
        }

        /// <summary>Every lineup faces the goal (+Z).</summary>
        public static Quaternion LineupFacing => Quaternion.LookRotation(Vector3.forward, Vector3.up);

        /// <summary>
        /// Where a body that is NOT in this kick waits, hidden: well behind the goal, fanned so
        /// parked bodies never overlap each other. Hidden bodies are display bodies, so nothing
        /// there can be touched or seen; the spacing only matters if one is ever shown by mistake.
        /// </summary>
        public static Vector3 HideSpot(int index)
        {
            var g = SimConfig.GoalCenter;
            return new Vector3(g.x + (index - 3) * 1.5f, 0f, g.z + 9f);
        }

        // ---- the ball tests ------------------------------------------------------------------

        /// <summary>
        /// A goal the instant the WHOLE ball is over the line and inside the frame - the identical
        /// per-frame test FreeKickGame / NetSetPieceMatch / KeeperGame use (design 2.1).
        /// </summary>
        public static bool BallFullyInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius;
            float halfW = SimConfig.GoalWidth * 0.5f;
            float lineZ = SimConfig.GoalCenter.z;
            return c.z - r >= lineZ
                   && c.z <= lineZ + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r
                   && c.y >= r
                   && c.y <= SimConfig.GoalHeight - r;
        }

        /// <summary>
        /// Swept goal test: the WHOLE ball crossed the goal line between two samples AND sat inside
        /// the frame at the crossing. The driver latches it at physics rate, so a hard shot that hits
        /// the net and rebounds out between two rendered frames is still a goal, and a ball that
        /// sails over the bar and drops behind the net (inside the box test's depth band) never is:
        /// it crossed the plane above the bar. This replaces the per-frame box test as the goal
        /// verdict (the box test alone produced both of those mis-calls in play).
        /// </summary>
        public static bool CrossedGoalLine(Vector3 prev, Vector3 cur)
        {
            float r = SimConfig.BallRadius;
            float lineZ = SimConfig.GoalCenter.z;
            float a = prev.z - r, b = cur.z - r;          // the ball's NEAR edge along the goal axis
            if (a >= lineZ || b < lineZ) return false;    // not a "fully over" crossing on this segment
            float t = (lineZ - a) / Mathf.Max(1e-5f, b - a);
            Vector3 p = Vector3.LerpUnclamped(prev, cur, t);
            float halfW = SimConfig.GoalWidth * 0.5f;
            return Mathf.Abs(p.x) <= halfW - r && p.y >= r && p.y <= SimConfig.GoalHeight - r;
        }

        /// <summary>
        /// The ball has left play: under the turf, past the arena bounds, or behind the goal but
        /// outside the frame (FreeKickGame's fuller test, so a shot that sails over the bar
        /// resolves the moment it is clearly gone rather than when it stops rolling).
        /// </summary>
        public static bool BallOutOfPlay(Vector3 c)
        {
            float halfGoal = SimConfig.GoalWidth * 0.5f;
            float lineZ = SimConfig.GoalCenter.z;
            bool behindGoal = c.z > lineZ + 0.6f && (Mathf.Abs(c.x) > halfGoal || c.y > SimConfig.GoalHeight);
            return c.y < -3f
                   || Mathf.Abs(c.x) > SimConfig.FieldWidth
                   || Mathf.Abs(c.z) > SimConfig.FieldLength
                   || behindGoal;
        }
    }
}
