using System;
using System.Collections.Generic;
using System.Text;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// The phases of ONE played round, as the round driver (<see cref="CupRoundDriver"/>) walks
    /// them for every kick. Rides the wire as a byte in <see cref="CupRoundState.Phase"/>: append,
    /// never reorder.
    ///
    /// Idle -> (Begin) Intro -> Placing -> WhistleRaise -> Armed -> Live -> Verdict
    ///   -> Scored (a goal: the scorer's free window)  or  WalkBack (miss / save)
    ///   -> Placing (next kick)  or  Decided (win beat / dejection)  -> Over.
    /// </summary>
    public enum RoundPhase : byte
    {
        /// <summary>Configured, nothing running (before Begin, or after Abort).</summary>
        Idle = 0,
        /// <summary>The round intro card (nations, stage, first kick). CupTuning.IntroSeconds.</summary>
        Intro = 1,
        /// <summary>Bodies to their marks under the cut: taker to the spot, keeper to the line, lineups, referee.</summary>
        Placing = 2,
        /// <summary>The referee's forearm-to-mouth raise before the whistle. CupTuning.WhistleRaiseSeconds.</summary>
        WhistleRaise = 3,
        /// <summary>Whistle blown, the taker may go; the kick clock runs. CupTuning.KickClock.</summary>
        Armed = 4,
        /// <summary>The ball is struck and travelling; the verdict is pending.</summary>
        Live = 5,
        /// <summary>The verdict is in (GOAL / SAVED / MISS) and flashed; the kick is recorded.</summary>
        Verdict = 6,
        /// <summary>A goal: the scorer's free run-and-emote window, scorer-only skip. CupTuning.ScoredWindow.</summary>
        Scored = 7,
        /// <summary>A miss or save: the walk-back cinematic. CupTuning.WalkBackMax.</summary>
        WalkBack = 8,
        /// <summary>The round is decided: the winners' free window or the losers' dejection beat.</summary>
        Decided = 9,
        /// <summary>Everything done; the director may read the result and tear the round down.</summary>
        Over = 10,
    }

    /// <summary>
    /// Who simulates a round. Local = this machine owns it outright (Solo, and Head to Head rounds
    /// against AI); Host = this machine is the host of a networked, host-simulated round (Co-op,
    /// and human-vs-human Head to Head rounds); Client = this machine mirrors a host-simulated
    /// round from <see cref="CupRoundState"/> and snapshots.
    /// </summary>
    public enum RoundAuthority : byte { Local = 0, Host = 1, Client = 2 }

    /// <summary>
    /// The replicated state of one played round: everything a client needs to draw the HUD and
    /// choreography of a host-simulated round, and everything a late joiner needs to catch up
    /// (design 9.4, the CupRoundState message). Plain data with a versioned wire form; the driver
    /// fills it with <see cref="CupRoundDriver.CaptureState"/> and consumes it with
    /// <see cref="CupRoundDriver.ApplyState"/>. Bodies are referenced by VIRTUAL body id: a human's
    /// body is its slot (0..7), AI bodies and the referee use ids from <see cref="AiBodyIdBase"/>
    /// up (the snapshot's slot byte carries them the same way).
    /// </summary>
    public sealed class CupRoundState
    {
        /// <summary>Bump when the wire layout changes; ReadFrom refuses other versions.</summary>
        public const byte WireVersion = 1;
        /// <summary>"No body" for the body-id fields.</summary>
        public const int NoBody = -1;
        /// <summary>AI bodies and the referee take virtual ids from here (humans are 0..7).</summary>
        public const int AiBodyIdBase = 8;

        // ---- which round ----------------------------------------------------------------------
        public CupStage Stage;
        /// <summary>The round's index within its stage (CupRound.Index).</summary>
        public int RoundIndex;
        public int EntrantA = -1;
        public int EntrantB = -1;

        // ---- where it is ----------------------------------------------------------------------
        public RoundPhase Phase;
        /// <summary>Seconds in the current phase on the authority.</summary>
        public float PhaseTime;
        /// <summary>0-based index of the NEXT kick overall (== kicks taken so far).</summary>
        public int KickIndex;
        public int ScoreA;
        public int ScoreB;
        /// <summary>The kicks taken so far, in order (2 bits per outcome on the wire).</summary>
        public List<KickRecord> Kicks = new List<KickRecord>();
        /// <summary>The side taking the next kick.</summary>
        public CupSide Kicker;
        /// <summary>The coin winner; null before the toss.</summary>
        public CupSide? FirstKicker;
        public bool SuddenDeath;
        /// <summary>Seconds left on the kick clock while Armed (CupTuning.KickClock at the whistle).</summary>
        public float KickClockRemaining;
        /// <summary>Free Kicks: the spot index of the current pair (CupRoundRules.PairIndex).</summary>
        public int SpotIndex;

        // ---- the coin -------------------------------------------------------------------------
        /// <summary>The side whose call decides kick-off.</summary>
        public CupSide CoinCaller;
        /// <summary>The official call; null until made (the 5 s timeout makes it HEADS).</summary>
        public CoinFace? CoinCall;
        /// <summary>The face the coin settled on; null until the ceremony ends.</summary>
        public CoinFace? CoinResult;

        // ---- the last kick and its windows -----------------------------------------------------
        public KickOutcome? LastOutcome;
        /// <summary>The slot that scored the last goal (-1: nobody / an AI).</summary>
        public int LastScorerSlot = -1;
        /// <summary>The scorer's 5 s window is open (scorer-only skip).</summary>
        public bool ScoredWindowOpen;
        /// <summary>The winners' 5 s free window is open (scorer / winning keeper skip).</summary>
        public bool WinBeatOpen;
        /// <summary>Seconds left in whichever window is open.</summary>
        public float WindowRemaining;
        /// <summary>The unanimous replay skip: votes so far / voters (humans with a body in the round).</summary>
        public int SkipVotes;
        public int SkipVoters;

        // ---- who is on the ball ---------------------------------------------------------------
        /// <summary>Virtual body id of the taker of the current kick (NoBody between kicks).</summary>
        public int TakerBodyId = NoBody;
        /// <summary>Virtual body id of the keeper facing the current kick.</summary>
        public int KeeperBodyId = NoBody;
        /// <summary>The human slot taking / keeping the current kick, -1 when it is an AI.</summary>
        public int TakerSlot = -1;
        public int KeeperSlot = -1;

        /// <summary>The authority's tick when this state was captured (monotonic; a stale state is dropped).</summary>
        public uint Tick;

        public CupRoundState() { }

        /// <summary>Copy every field from another state (deep-copies the kick list).</summary>
        public void CopyFrom(CupRoundState o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            Stage = o.Stage;
            RoundIndex = o.RoundIndex;
            EntrantA = o.EntrantA;
            EntrantB = o.EntrantB;
            Phase = o.Phase;
            PhaseTime = o.PhaseTime;
            KickIndex = o.KickIndex;
            ScoreA = o.ScoreA;
            ScoreB = o.ScoreB;
            Kicks.Clear();
            for (int i = 0; i < o.Kicks.Count; i++) Kicks.Add(o.Kicks[i].Clone());
            Kicker = o.Kicker;
            FirstKicker = o.FirstKicker;
            SuddenDeath = o.SuddenDeath;
            KickClockRemaining = o.KickClockRemaining;
            SpotIndex = o.SpotIndex;
            CoinCaller = o.CoinCaller;
            CoinCall = o.CoinCall;
            CoinResult = o.CoinResult;
            LastOutcome = o.LastOutcome;
            LastScorerSlot = o.LastScorerSlot;
            ScoredWindowOpen = o.ScoredWindowOpen;
            WinBeatOpen = o.WinBeatOpen;
            WindowRemaining = o.WindowRemaining;
            SkipVotes = o.SkipVotes;
            SkipVoters = o.SkipVoters;
            TakerBodyId = o.TakerBodyId;
            KeeperBodyId = o.KeeperBodyId;
            TakerSlot = o.TakerSlot;
            KeeperSlot = o.KeeperSlot;
            Tick = o.Tick;
        }

        public CupRoundState Clone()
        {
            var c = new CupRoundState();
            c.CopyFrom(this);
            return c;
        }

        /// <summary>Reset to the state of a round that has not started (keeps which round it is).</summary>
        public void ResetPlay()
        {
            Phase = RoundPhase.Idle;
            PhaseTime = 0f;
            KickIndex = 0;
            ScoreA = ScoreB = 0;
            Kicks.Clear();
            Kicker = CupSide.A;
            FirstKicker = null;
            SuddenDeath = false;
            KickClockRemaining = 0f;
            SpotIndex = 0;
            CoinCaller = CupSide.A;
            CoinCall = null;
            CoinResult = null;
            LastOutcome = null;
            LastScorerSlot = -1;
            ScoredWindowOpen = false;
            WinBeatOpen = false;
            WindowRemaining = 0f;
            SkipVotes = SkipVoters = 0;
            TakerBodyId = KeeperBodyId = NoBody;
            TakerSlot = KeeperSlot = -1;
        }

        /// <summary>Field-by-field equality (the kick list included); Tick is ignored.</summary>
        public bool SameAs(CupRoundState o)
        {
            if (o == null) return false;
            if (Stage != o.Stage || RoundIndex != o.RoundIndex || EntrantA != o.EntrantA || EntrantB != o.EntrantB) return false;
            if (Phase != o.Phase || PhaseTime != o.PhaseTime || KickIndex != o.KickIndex) return false;
            if (ScoreA != o.ScoreA || ScoreB != o.ScoreB || Kicks.Count != o.Kicks.Count) return false;
            for (int i = 0; i < Kicks.Count; i++) if (!Kicks[i].SameAs(o.Kicks[i])) return false;
            if (Kicker != o.Kicker || FirstKicker != o.FirstKicker || SuddenDeath != o.SuddenDeath) return false;
            if (KickClockRemaining != o.KickClockRemaining || SpotIndex != o.SpotIndex) return false;
            if (CoinCaller != o.CoinCaller || CoinCall != o.CoinCall || CoinResult != o.CoinResult) return false;
            if (LastOutcome != o.LastOutcome || LastScorerSlot != o.LastScorerSlot) return false;
            if (ScoredWindowOpen != o.ScoredWindowOpen || WinBeatOpen != o.WinBeatOpen || WindowRemaining != o.WindowRemaining) return false;
            if (SkipVotes != o.SkipVotes || SkipVoters != o.SkipVoters) return false;
            if (TakerBodyId != o.TakerBodyId || KeeperBodyId != o.KeeperBodyId || TakerSlot != o.TakerSlot || KeeperSlot != o.KeeperSlot) return false;
            return true;
        }

        /// <summary>"3-1", or "5-4 SD" (A first).</summary>
        public string ScoreLine => CupText.ScoreLine(ScoreA, ScoreB, SuddenDeath);

        // ---- wire -----------------------------------------------------------------------------
        // Flag byte 1: nullable fields. Flag byte 2: booleans and the two sides.
        const int F1HasFirst = 1, F1FirstIsB = 2, F1HasCall = 4, F1CallTails = 8, F1HasResult = 16, F1ResultTails = 32, F1HasOutcome = 64;
        const int F2SuddenDeath = 1, F2ScoredWindow = 2, F2WinBeat = 4, F2KickerIsB = 8, F2CallerIsB = 16;

        /// <summary>Append the versioned record (~30 bytes + one byte per two kicks).</summary>
        public void WriteTo(CupByteWriter w)
        {
            if (w == null) throw new ArgumentNullException(nameof(w));
            w.U8(WireVersion);
            w.U8((int)Stage);
            w.U8(RoundIndex);
            w.Slot(EntrantA);
            w.Slot(EntrantB);
            w.U8((int)Phase);
            w.F(PhaseTime);
            w.U8(Math.Max(0, Math.Min(255, KickIndex)));
            w.U8(Math.Max(0, Math.Min(255, ScoreA)));
            w.U8(Math.Max(0, Math.Min(255, ScoreB)));

            int f1 = 0;
            if (FirstKicker.HasValue) { f1 |= F1HasFirst; if (FirstKicker.Value == CupSide.B) f1 |= F1FirstIsB; }
            if (CoinCall.HasValue) { f1 |= F1HasCall; if (CoinCall.Value == CoinFace.Tails) f1 |= F1CallTails; }
            if (CoinResult.HasValue) { f1 |= F1HasResult; if (CoinResult.Value == CoinFace.Tails) f1 |= F1ResultTails; }
            if (LastOutcome.HasValue) f1 |= F1HasOutcome;
            w.U8(f1);
            int f2 = 0;
            if (SuddenDeath) f2 |= F2SuddenDeath;
            if (ScoredWindowOpen) f2 |= F2ScoredWindow;
            if (WinBeatOpen) f2 |= F2WinBeat;
            if (Kicker == CupSide.B) f2 |= F2KickerIsB;
            if (CoinCaller == CupSide.B) f2 |= F2CallerIsB;
            w.U8(f2);
            if (LastOutcome.HasValue) w.U8((int)LastOutcome.Value);

            w.F(KickClockRemaining);
            w.U8(Math.Max(0, Math.Min(255, SpotIndex)));
            w.Slot(LastScorerSlot);
            w.F(WindowRemaining);
            w.U8(Math.Max(0, Math.Min(255, SkipVotes)));
            w.U8(Math.Max(0, Math.Min(255, SkipVoters)));
            w.Slot(TakerBodyId);
            w.Slot(KeeperBodyId);
            w.Slot(TakerSlot);
            w.Slot(KeeperSlot);
            w.U32(Tick);

            int n = Math.Min(Kicks.Count, 255);
            w.U8(n);
            for (int i = 0; i < n; i += 2)
            {
                int lo = Kicks[i].ToNibble();
                int hi = i + 1 < n ? Kicks[i + 1].ToNibble() : 0;
                w.U8(lo | (hi << 4));
            }
        }

        /// <summary>Parse a record written by <see cref="WriteTo"/>; FormatException on a bad or truncated one.</summary>
        public static CupRoundState ReadFrom(CupByteReader r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            int v = r.U8();
            if (v != WireVersion) throw new FormatException("CupRoundState: wire version " + v + ", expected " + WireVersion);
            var s = new CupRoundState();
            s.Stage = (CupStage)r.U8();
            if (!CupStages.IsValid(s.Stage)) throw new FormatException("CupRoundState: bad stage " + (int)s.Stage);
            s.RoundIndex = r.U8();
            s.EntrantA = r.Slot();
            s.EntrantB = r.Slot();
            int ph = r.U8();
            if (ph > (int)RoundPhase.Over) throw new FormatException("CupRoundState: bad phase " + ph);
            s.Phase = (RoundPhase)ph;
            s.PhaseTime = r.F();
            s.KickIndex = r.U8();
            s.ScoreA = r.U8();
            s.ScoreB = r.U8();

            int f1 = r.U8();
            int f2 = r.U8();
            s.FirstKicker = (f1 & F1HasFirst) != 0 ? ((f1 & F1FirstIsB) != 0 ? CupSide.B : CupSide.A) : (CupSide?)null;
            s.CoinCall = (f1 & F1HasCall) != 0 ? ((f1 & F1CallTails) != 0 ? CoinFace.Tails : CoinFace.Heads) : (CoinFace?)null;
            s.CoinResult = (f1 & F1HasResult) != 0 ? ((f1 & F1ResultTails) != 0 ? CoinFace.Tails : CoinFace.Heads) : (CoinFace?)null;
            s.SuddenDeath = (f2 & F2SuddenDeath) != 0;
            s.ScoredWindowOpen = (f2 & F2ScoredWindow) != 0;
            s.WinBeatOpen = (f2 & F2WinBeat) != 0;
            s.Kicker = (f2 & F2KickerIsB) != 0 ? CupSide.B : CupSide.A;
            s.CoinCaller = (f2 & F2CallerIsB) != 0 ? CupSide.B : CupSide.A;
            if ((f1 & F1HasOutcome) != 0)
            {
                int o = r.U8();
                if (o > (int)KickOutcome.Miss) throw new FormatException("CupRoundState: bad outcome " + o);
                s.LastOutcome = (KickOutcome)o;
            }

            s.KickClockRemaining = r.F();
            s.SpotIndex = r.U8();
            s.LastScorerSlot = r.Slot();
            s.WindowRemaining = r.F();
            s.SkipVotes = r.U8();
            s.SkipVoters = r.U8();
            s.TakerBodyId = r.Slot();
            s.KeeperBodyId = r.Slot();
            s.TakerSlot = r.Slot();
            s.KeeperSlot = r.Slot();
            s.Tick = r.U32();

            int n = r.U8();
            s.Kicks = new List<KickRecord>(n);
            for (int i = 0; i < n; i += 2)
            {
                int b = r.U8();
                s.Kicks.Add(KickRecord.FromNibble(b & 15));
                if (i + 1 < n) s.Kicks.Add(KickRecord.FromNibble((b >> 4) & 15));
            }
            return s;
        }

        public byte[] ToBytes()
        {
            var w = new CupByteWriter(64);
            WriteTo(w);
            return w.ToArray();
        }

        public static CupRoundState FromBytes(byte[] data) => ReadFrom(new CupByteReader(data));

        /// <summary>One log line: "QF #1 Armed 3.2s kick 5 (3-1) kicker B clock 8.1".</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(CupStages.Short(Stage)).Append(" #").Append(RoundIndex).Append(' ').Append(Phase);
            sb.Append(' ').Append(PhaseTime.ToString("0.0")).Append("s kick ").Append(KickIndex + 1);
            sb.Append(" (").Append(ScoreLine).Append(") kicker ").Append(CupSides.Name(Kicker));
            if (FirstKicker.HasValue) sb.Append(" first ").Append(CupSides.Name(FirstKicker.Value));
            if (Phase == RoundPhase.Armed) sb.Append(" clock ").Append(KickClockRemaining.ToString("0.0"));
            if (LastOutcome.HasValue) sb.Append(" last ").Append(CupText.Verdict(LastOutcome.Value));
            if (ScoredWindowOpen) sb.Append(" [scored window]");
            if (WinBeatOpen) sb.Append(" [win beat]");
            return sb.ToString();
        }

        public override string ToString() => Describe();
    }
}
