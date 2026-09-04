using System;
using System.Collections.Generic;
using System.Text;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// The state the shootout rules are evaluated over: who kicked first, how many kicks each side
    /// has in regulation, and the kicks so far in the order they were taken. A driver owns one per
    /// round and feeds it through <see cref="CupRoundRules"/>; the bracket stores the result.
    /// </summary>
    public sealed class RoundLine
    {
        /// <summary>Regulation kicks per side (CupTuning.KicksEach).</summary>
        public readonly int KicksEach;
        /// <summary>The side that took kick 1 (the coin winner).</summary>
        public readonly CupSide FirstKicker;
        /// <summary>Every kick so far, in order. Append through CupRoundRules.RecordKick.</summary>
        public readonly List<KickRecord> Kicks = new List<KickRecord>();

        public RoundLine(CupSide firstKicker, int kicksEach = CupTuning.KicksEach)
        {
            if (kicksEach < 1) throw new ArgumentOutOfRangeException(nameof(kicksEach));
            FirstKicker = firstKicker;
            KicksEach = kicksEach;
        }

        public int Count => Kicks.Count;

        public int Goals(CupSide side)
        {
            int n = 0;
            for (int i = 0; i < Kicks.Count; i++) if (Kicks[i].Side == side && Kicks[i].Scored) n++;
            return n;
        }

        public int Taken(CupSide side)
        {
            int n = 0;
            for (int i = 0; i < Kicks.Count; i++) if (Kicks[i].Side == side) n++;
            return n;
        }

        public int GoalsA => Goals(CupSide.A);
        public int GoalsB => Goals(CupSide.B);
        public int TakenA => Taken(CupSide.A);
        public int TakenB => Taken(CupSide.B);

        /// <summary>The most recent kick, or null.</summary>
        public KickRecord Last => Kicks.Count > 0 ? Kicks[Kicks.Count - 1] : null;

        public RoundLine Clone()
        {
            var c = new RoundLine(FirstKicker, KicksEach);
            for (int i = 0; i < Kicks.Count; i++) c.Kicks.Add(Kicks[i].Clone());
            return c;
        }

        public override string ToString() => CupRoundRules.Describe(this);
    }

    /// <summary>
    /// The shootout rules as pure functions - unit-tested in <see cref="CupSelfTest"/>, shared by
    /// the played round (CupRound driver), the simulator (<see cref="CupSim"/>) and the host's
    /// validation of a client-reported result.
    ///
    /// The rules (design 2.1):
    /// - KicksEach kicks per side, strictly alternating, the coin winner first. Sudden death keeps
    ///   the same order, so kick k (0-based) is always taken by FirstKicker when k is even.
    /// - EARLY FINISH: the round ends the moment one side cannot be caught - its lead is greater
    ///   than the kicks the other side has left. Examples with A first, 5 each:
    ///     A 3/3 vs B 0/3 (3-0, B has 2 left)        -> decided, A wins (B can reach at most 2).
    ///     A 3/3 vs B 0/2 (3-0, B has 3 left)        -> not decided (B could still reach 3).
    ///     A 3/4 vs B 1/3 (3-1, B has 2 left)        -> not decided (B could reach 3).
    ///     A 4/4 vs B 1/3 (4-1, B has 2 left)        -> decided, A wins.
    ///     A 2/4 vs B 4/4 (2-4, A has 1 left)        -> decided, B wins (A can reach at most 3).
    ///     A 5/5 vs B 4/4 (5-4, B has 1 left)        -> not decided (B can level).
    /// - SUDDEN DEATH: level after KicksEach each, then pairs of kicks (one each, same order); the
    ///   round is decided only after a COMPLETED pair in which one side scored and the other did
    ///   not. Within a pair, "kicks left" counts the pending kick, so 4-3 with A having taken 6
    ///   and B 5 is NOT decided (B has one to come); after B's kick it is 4-4 (continue) or 4-3
    ///   (A wins).
    /// Both phases are the one formula in <see cref="IsDecided"/>: a side has
    /// <c>max(KicksEach, takenByOther) - takenBySelf</c> kicks left.
    /// </summary>
    public static class CupRoundRules
    {
        /// <summary>Who takes kick k (0-based) given the first kicker: strict alternation, sudden death included.</summary>
        public static CupSide KickerAt(CupSide firstKicker, int kickIndex)
        {
            return (kickIndex & 1) == 0 ? firstKicker : CupSides.Other(firstKicker);
        }

        /// <summary>The side that takes the next kick.</summary>
        public static CupSide NextKicker(RoundLine line) => KickerAt(line.FirstKicker, line.Count);

        /// <summary>The 1-based number of the NEXT kick for a side ("KICK 3 of 5" is KickNumberFor(side) == 3).</summary>
        public static int KickNumberFor(RoundLine line, CupSide side) => line.Taken(side) + 1;

        /// <summary>The 1-based number of the next kick overall (sudden death's "KICK 7" counts per side: use KickNumberFor).</summary>
        public static int NextKickNumber(RoundLine line) => line.Count + 1;

        /// <summary>
        /// The 0-based pair the next kick belongs to. Kicks 0 and 1 are pair 0, 2 and 3 pair 1...
        /// In Free Kicks this is the spot index: both sides take pair n from spot n.
        /// </summary>
        public static int PairIndex(RoundLine line) => line.Count / 2;

        /// <summary>True when the next kick opens a new pair (both sides have taken the same number).</summary>
        public static bool PairComplete(RoundLine line) => (line.Count & 1) == 0;

        /// <summary>Both sides have taken all their regulation kicks.</summary>
        public static bool RegulationOver(RoundLine line)
        {
            return line.TakenA >= line.KicksEach && line.TakenB >= line.KicksEach;
        }

        /// <summary>
        /// The round has gone past regulation: level after KicksEach each (so the next kick is a
        /// sudden-death kick), or already into the pairs. False for a round decided in regulation.
        /// </summary>
        public static bool IsSuddenDeath(RoundLine line)
        {
            int reg = line.KicksEach * 2;
            if (line.Count > reg) return true;
            return line.Count == reg && line.GoalsA == line.GoalsB;
        }

        /// <summary>
        /// Kicks a side still has before the other side can no longer be waited for: in regulation
        /// the ones it has not taken; in sudden death the pending kick of the current pair.
        /// </summary>
        public static int KicksLeft(RoundLine line, CupSide side)
        {
            int taken = line.Taken(side);
            int other = line.Taken(CupSides.Other(side));
            int left = Math.Max(line.KicksEach, other) - taken;
            return left < 0 ? 0 : left;
        }

        /// <summary>The early-finish / sudden-death rule: is the round over, and who won.</summary>
        public static bool IsDecided(RoundLine line, out CupSide winner)
        {
            int gA = line.GoalsA, gB = line.GoalsB;
            int leftA = KicksLeft(line, CupSide.A);
            int leftB = KicksLeft(line, CupSide.B);
            if (gA > gB + leftB) { winner = CupSide.A; return true; }
            if (gB > gA + leftA) { winner = CupSide.B; return true; }
            winner = CupSide.A;
            return false;
        }

        public static bool IsOver(RoundLine line)
        {
            CupSide w;
            return IsDecided(line, out w);
        }

        /// <summary>The winner, or null while the round is live.</summary>
        public static CupSide? Winner(RoundLine line)
        {
            CupSide w;
            return IsDecided(line, out w) ? w : (CupSide?)null;
        }

        /// <summary>
        /// Append a kick. Throws if the round is already decided or the side is out of turn - both
        /// are driver bugs, and a silent append would corrupt the score.
        /// </summary>
        public static void RecordKick(RoundLine line, CupSide side, KickOutcome outcome)
        {
            CupSide w;
            if (IsDecided(line, out w))
                throw new InvalidOperationException("CupRoundRules.RecordKick: the round is already decided (" + Describe(line) + ")");
            var expected = NextKicker(line);
            if (side != expected)
                throw new InvalidOperationException("CupRoundRules.RecordKick: side " + CupSides.Name(side) + " is out of turn, " + CupSides.Name(expected) + " kicks next");
            line.Kicks.Add(new KickRecord(side, outcome));
        }

        /// <summary>Append a kick for whichever side is next; returns the record.</summary>
        public static KickRecord Record(RoundLine line, KickOutcome outcome)
        {
            var side = NextKicker(line);
            RecordKick(line, side, outcome);
            return line.Last;
        }

        /// <summary>
        /// Co-op shooter cycling: which position in the shooting order takes a side's N-th kick
        /// (0-based). With five shooters, kick 6 (index 5) wraps to shooter 1 (index 0).
        /// </summary>
        public static int CoopShooterFor(int sideKickIndex, int orderCount)
        {
            if (orderCount <= 0) return 0;
            int r = sideKickIndex % orderCount;
            return r < 0 ? r + orderCount : r;
        }

        /// <summary>The coin decides: a correct call kicks first, otherwise the other side does.</summary>
        public static CupSide FirstKickerFromCall(CupSide caller, CoinFace call, CoinFace result)
        {
            return call == result ? caller : CupSides.Other(caller);
        }

        /// <summary>The number of kicks in a regulation round with no early finish.</summary>
        public static int MaxRegulationKicks(int kicksEach = CupTuning.KicksEach) => kicksEach * 2;

        /// <summary>
        /// Replay a reported kick line under the rules: alternation from the first kicker, no kick
        /// after the decision, and (optionally) decided at the end. The host runs this on a
        /// client-authored result before folding it into the bracket. On success <paramref name="line"/>
        /// holds the replayed line (its scores are the authoritative ones).
        ///
        /// <paramref name="maxKicks"/> is the WIRE CAP, and it is the only length bound there is:
        /// alternation and the decidedness rules bound nothing on their own, so an undecided
        /// alternating line is legal at any length. Without the cap a modified client can report
        /// 255 kicks through CupRequest.RoundResult and every later CupState carries them (31 such
        /// rounds is about 4 KB in one reliable datagram, which DirectIpTransport never fragments).
        /// Pass CupTuning.MaxKicksInLine at every wire seam; 0 or less means no cap (tests only).
        /// </summary>
        public static bool Validate(IList<KickRecord> kicks, CupSide firstKicker, int kicksEach, bool requireDecided, out RoundLine line, out string error, int maxKicks = 0)
        {
            line = new RoundLine(firstKicker, kicksEach);
            error = null;
            if (kicks == null) { error = "no kick line"; return false; }
            if (maxKicks > 0 && kicks.Count > maxKicks)
            {
                error = "the line is " + kicks.Count + " kicks, over the " + maxKicks + "-kick cap";
                return false;
            }
            for (int i = 0; i < kicks.Count; i++)
            {
                var k = kicks[i];
                if (k == null) { error = "kick " + (i + 1) + " is null"; return false; }
                CupSide w;
                if (IsDecided(line, out w)) { error = "kick " + (i + 1) + " taken after the round was decided"; return false; }
                var expected = NextKicker(line);
                if (k.Side != expected) { error = "kick " + (i + 1) + " taken by " + CupSides.Name(k.Side) + ", expected " + CupSides.Name(expected); return false; }
                if ((int)k.Outcome > (int)KickOutcome.Miss) { error = "kick " + (i + 1) + " has outcome " + (int)k.Outcome; return false; }
                line.Kicks.Add(new KickRecord(k.Side, k.Outcome));
            }
            if (requireDecided && !IsOver(line)) { error = "the line is not decided (" + Describe(line) + ")"; return false; }
            return true;
        }

        /// <summary>"A:GOAL B:MISS A:GOAL ... (3-1)" for logs.</summary>
        public static string Describe(RoundLine line)
        {
            if (line == null) return "(null line)";
            var sb = new StringBuilder();
            for (int i = 0; i < line.Kicks.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(line.Kicks[i]);
            }
            if (line.Kicks.Count > 0) sb.Append(' ');
            sb.Append('(').Append(ScoreLine(line)).Append(')');
            CupSide w;
            if (IsDecided(line, out w)) sb.Append(" -> ").Append(CupSides.Name(w));
            return sb.ToString();
        }

        /// <summary>"3-1", or "5-4 SD" once the line went to sudden death (A first).</summary>
        public static string ScoreLine(RoundLine line) => CupText.ScoreLine(line.GoalsA, line.GoalsB, IsSuddenDeath(line));
    }
}
