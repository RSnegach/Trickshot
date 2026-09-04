using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Everything a round driver needs to build and run ONE round, handed over in one bag by
    /// <see cref="CupDirector.MakeRoundSetup"/>. Plain fields: the director fills them, the flow
    /// may adjust <see cref="FirstKicker"/> after the coin toss (through
    /// <see cref="CupRoundDriver.SetFirstKicker"/>), the driver reads them.
    /// </summary>
    public sealed class CupRoundSetup
    {
        // ---- what is being played --------------------------------------------------------------
        public CupStyle Style;
        public CupFormat Format;
        /// <summary>The stage, which is the whole difficulty: CupTuning.KeeperAbility(Stage) / TakerCombined / TakerPower.</summary>
        public CupStage Stage;

        // ---- who is on the human side(s) --------------------------------------------------------
        /// <summary>Co-op: the shooting order, slot per order index (index 0 = the keeper, 1.. = shooters in order). Empty otherwise.</summary>
        public int[] CoopOrderSlots = new int[0];
        /// <summary>Co-op: the slot keeping this stage (CoopOrderSlots[0]); -1 otherwise.</summary>
        public int CoopKeeperSlot = -1;
        /// <summary>The local player's side is A (false = B). Meaningless for a pure spectator (see LocalHasBody).</summary>
        public bool LocalOnSideA;
        /// <summary>The local player's net slot (Solo: 0; a spectator with no body still has a slot).</summary>
        public int LocalSlot = -1;
        /// <summary>The human slot owning side A / B in Solo and Head to Head (-1 = an AI nation). Co-op: see TeamSide/CoopOrderSlots.</summary>
        public int HumanSlotA = -1;
        public int HumanSlotB = -1;
        /// <summary>Co-op: the side the human team is on. Solo / Head to Head: the local side (== LocalOnSideA).</summary>
        public CupSide TeamSide = CupSide.A;

        // ---- randomness and the coin -----------------------------------------------------------
        /// <summary>
        /// The CUP ROOT stream (new SeededRng(cupSeed)). Never draw from it directly: fork with
        /// CupSalts.Coin/Spots/Dejection(Stage, roundIndex) so every peer lands on the same stream.
        /// </summary>
        public SeededRng Rng;
        /// <summary>The side taking kick 1 (the coin winner). Set before Begin (SetFirstKicker).</summary>
        public CupSide FirstKicker = CupSide.A;
        /// <summary>The side whose call decides kick-off, and the human slot making it (-1 = an AI calls).</summary>
        public CupSide CoinCaller = CupSide.A;
        public int CoinCallerSlot = -1;

        // ---- scene references (from GameBootstrap through the director) ------------------------
        /// <summary>The per-round root every transient object goes under (destroyed by the director after the round).</summary>
        public Transform Root;
        public GameInput Input;
        public Camera Cam;
        public GameCamera GameCam;
        public BallController Ball;
        /// <summary>The goal centre transform (Arena.Refs.goalCenter).</summary>
        public Transform Goal;
        public Material Torso, Limb, Glove;

        /// <summary>The local player's side (A when LocalOnSideA).</summary>
        public CupSide LocalSide => LocalOnSideA ? CupSide.A : CupSide.B;

        /// <summary>Is a side played by humans (a Solo / Head to Head human, or the Co-op team)?</summary>
        public bool SideIsHuman(CupSide side)
        {
            if (Style == CupStyle.Coop) return side == TeamSide;
            return (side == CupSide.A ? HumanSlotA : HumanSlotB) >= 0;
        }

        /// <summary>The human slot owning a side, or -1 (Co-op: the keeper slot stands for the team).</summary>
        public int HumanSlotOf(CupSide side)
        {
            if (Style == CupStyle.Coop) return side == TeamSide ? CoopKeeperSlot : -1;
            return side == CupSide.A ? HumanSlotA : HumanSlotB;
        }

        /// <summary>Does the local player have a body in this round (taker/keeper/lineup), as opposed to watching?</summary>
        public bool LocalHasBody
        {
            get
            {
                if (LocalSlot < 0) return false;
                if (HumanSlotA == LocalSlot || HumanSlotB == LocalSlot || CoopKeeperSlot == LocalSlot) return true;
                for (int i = 0; i < CoopOrderSlots.Length; i++) if (CoopOrderSlots[i] == LocalSlot) return true;
                return false;
            }
        }

        /// <summary>Every human slot with a body in the round, in a stable order (the replay-skip voters).</summary>
        public List<int> HumanSlotsWithBodies()
        {
            var list = new List<int>();
            if (Style == CupStyle.Coop)
            {
                for (int i = 0; i < CoopOrderSlots.Length; i++) if (CoopOrderSlots[i] >= 0 && !list.Contains(CoopOrderSlots[i])) list.Add(CoopOrderSlots[i]);
                if (CoopKeeperSlot >= 0 && !list.Contains(CoopKeeperSlot)) list.Add(CoopKeeperSlot);
            }
            else
            {
                if (HumanSlotA >= 0) list.Add(HumanSlotA);
                if (HumanSlotB >= 0 && HumanSlotB != HumanSlotA) list.Add(HumanSlotB);
            }
            return list;
        }

        /// <summary>The per-round stream for a CupSalts family (Rng.Fork with the round's (stage, index)).</summary>
        public SeededRng Stream(uint salt) => (Rng ?? new SeededRng(0u)).Fork(salt);

        /// <summary>Sanity check before Configure; returns false with a reason rather than throwing.</summary>
        public bool IsValid(out string error)
        {
            if (Rng == null) { error = "Rng is null (pass new SeededRng(cupSeed))"; return false; }
            if (Root == null) { error = "Root is null (the director's RoundRoot)"; return false; }
            if (Ball == null) { error = "Ball is null"; return false; }
            if (Cam == null) { error = "Cam is null"; return false; }
            if (!CupStages.IsValid(Stage)) { error = "bad stage " + (int)Stage; return false; }
            if (Style == CupStyle.Coop && CoopOrderSlots.Length == 0 && CoopKeeperSlot < 0) { error = "Co-op round with no order"; return false; }
            error = null;
            return true;
        }
    }

    /// <summary>
    /// The round driver: bodies, referee, taker, keeper, wall, ball, verdicts, replays, whistle
    /// sequencing and choreography for ONE round (design 9.1). This file is the PUBLIC SURFACE
    /// and the pure bookkeeping (kick line, scores, state capture); the bodies, cameras and
    /// choreography are IMPLEMENTED BY AGENT C1 in further partial files of this class, through
    /// the partial hooks at the bottom (OnConfigured / OnBegin / OnTick / OnAbort / OnSkip /
    /// OnPhaseChanged / OnKickResolved / OnStateApplied). Nothing here throws on a missing
    /// implementation: a driver with no C1 code Configures, Begins into Intro and sits there.
    ///
    /// Authority (see <see cref="RoundAuthority"/>): Local and Host simulate; Client mirrors a
    /// <see cref="CupRoundState"/> through <see cref="ApplyState"/>. Every read-model property is
    /// settable only from inside the class (private set), so screens and the HUD can never push.
    ///
    /// Lifecycle: the director creates it under a fresh RoundRoot and calls Configure while the
    /// loading card is up (the scene is built under that cover); the coin toss runs; the flow calls
    /// SetFirstKicker then Begin; Phase walks Intro..Over; the director reads <see cref="Line"/>
    /// and tears the RoundRoot down. Abort ends it early (a leaver, End Match).
    /// </summary>
    public partial class CupRoundDriver : MonoBehaviour
    {
        // ---- configuration --------------------------------------------------------------------
        /// <summary>The cup this round belongs to.</summary>
        public CupDirector Director { get; private set; }
        /// <summary>The bracket record being played (entrants, stage, index). Its result is written by the director, not here.</summary>
        public CupRound Data { get; private set; }
        public RoundAuthority Authority { get; private set; }
        /// <summary>The bag Configure was given (mutable until Begin; FirstKicker through SetFirstKicker).</summary>
        public CupRoundSetup Setup { get; private set; }
        /// <summary>Configure has run.</summary>
        public bool Configured { get; private set; }
        /// <summary>Begin has run and the round is not Over.</summary>
        public bool Running => Configured && Phase != RoundPhase.Idle && Phase != RoundPhase.Over;
        /// <summary>The local input (GameInput implements IStrikerInput). C1 may substitute a wrapper before arming a taker.</summary>
        public IStrikerInput LocalInput { get; private set; }

        // ---- read model (HUD, screens, spectators) ----------------------------------------------
        public RoundPhase Phase { get; private set; }
        /// <summary>Seconds in the current phase (Time.deltaTime: frozen by a Solo pause, running in MP).</summary>
        public float PhaseTime { get; private set; }
        /// <summary>The live kick line under the rules (CupRoundRules); the director stores it in the bracket at the end.</summary>
        public RoundLine Line { get; private set; }
        public int ScoreA { get; private set; }
        public int ScoreB { get; private set; }
        /// <summary>0-based index of the NEXT kick overall (== Line.Count).</summary>
        public int KickIndex { get; private set; }
        /// <summary>The side taking the next kick.</summary>
        public CupSide Kicker { get; private set; }
        public bool SuddenDeath { get; private set; }
        /// <summary>Seconds left on the kick clock while Armed; CupTuning.KickClock at the whistle, 0 otherwise.</summary>
        public float KickClockRemaining { get; private set; }
        /// <summary>The local player takes the current kick.</summary>
        public bool LocalIsTaker { get; private set; }
        /// <summary>The local player keeps the current kick.</summary>
        public bool LocalIsKeeper { get; private set; }
        /// <summary>The local player stands in a lineup for the current kick (look cone, no movement).</summary>
        public bool LocalInLineup { get; private set; }
        /// <summary>The verdict of the last kick; null before the first.</summary>
        public KickOutcome? LastOutcome { get; private set; }
        /// <summary>The slot that scored the last goal (-1: nobody, or an AI).</summary>
        public int LastScorerSlot { get; private set; } = -1;
        /// <summary>The scorer's 5 s window is open.</summary>
        public bool ScoredWindowOpen { get; private set; }
        /// <summary>The winners' 5 s free window is open.</summary>
        public bool WinBeatOpen { get; private set; }
        /// <summary>The local player may click to skip the open window (the scorer, or the winning keeper).</summary>
        public bool CanLocalSkip { get; private set; }
        /// <summary>The body taking the current kick (null between kicks / for a client without bodies).</summary>
        public ActiveRagdoll TakerBody { get; private set; }
        /// <summary>The body keeping the current kick.</summary>
        public ActiveRagdoll KeeperBody { get; private set; }
        /// <summary>The ball-spot marker transform (null until the scene is built).</summary>
        public Transform BallSpot { get; private set; }
        /// <summary>Where the ball sits for the current kick (the penalty spot, or this pair's free-kick spot).</summary>
        public Vector3 BallSpotPos { get; private set; }
        /// <summary>Free Kicks: the spot index of the current pair (CupRoundRules.PairIndex); 0 in Penalties.</summary>
        public int SpotIndex { get; private set; }
        /// <summary>Which side's turn the coin decided (Setup.FirstKicker once set).</summary>
        public CupSide FirstKicker => Setup != null ? Setup.FirstKicker : CupSide.A;
        /// <summary>The winner once decided, null while live.</summary>
        public CupSide? Winner => Line != null ? CupRoundRules.Winner(Line) : null;
        /// <summary>The round is decided under the rules (Phase may still be Decided / Over choreography).</summary>
        public bool IsDecided => Line != null && CupRoundRules.IsOver(Line);

        // ---- events ------------------------------------------------------------------------------
        public event Action<RoundPhase> PhaseChanged;
        /// <summary>(outcome, kicking side, scorer slot or -1) after every kick is recorded.</summary>
        public event Action<KickOutcome, CupSide, int> KickResolved;
        /// <summary>The winning side, the moment the rules decide it.</summary>
        public event Action<CupSide> RoundDecided;
        /// <summary>The referee started his forearm-to-mouth raise (WhistleRaise phase entered).</summary>
        public event Action WhistleRaised;
        /// <summary>The whistle blew (Armed phase entered).</summary>
        public event Action Whistled;

        // ---- lifecycle -------------------------------------------------------------------------
        /// <summary>
        /// Store the round and its setup and derive the read model from them (scores, the kick line
        /// so far, the next kicker, the default ball spot). Never throws: a bad setup is logged and
        /// the driver stays unconfigured (Begin then does nothing). Builds nothing itself - C1's
        /// OnConfigured builds the scene under Setup.Root.
        /// </summary>
        public void Configure(CupDirector director, CupRound data, RoundAuthority authority, CupRoundSetup setup)
        {
            Director = director;
            Data = data;
            Authority = authority;
            Setup = setup;
            Configured = false;

            if (data == null) { CupLog.Error("CupRoundDriver.Configure: round data is null"); return; }
            if (setup == null) { CupLog.Error("CupRoundDriver.Configure: setup is null"); return; }
            string why;
            if (!setup.IsValid(out why)) { CupLog.Error("CupRoundDriver.Configure: " + why); return; }
            if (!data.Ready) CupLog.Warn("CupRoundDriver.Configure: round " + CupStages.Short(data.Stage) + " #" + data.Index + " has an undecided entrant");

            LocalInput = setup.Input;

            // The kick line: resume from the record when it already has kicks (a late joiner, a
            // re-Configure after a leaver), else a fresh line from the setup's first kicker.
            if (data.Kicks.Count > 0 && data.FirstKicker.HasValue)
            {
                setup.FirstKicker = data.FirstKicker.Value;
                Line = data.ToLine(CupTuning.KicksEach);
            }
            else
            {
                Line = new RoundLine(setup.FirstKicker, CupTuning.KicksEach);
            }
            RefreshFromLine();

            KickClockRemaining = 0f;
            LocalIsTaker = LocalIsKeeper = LocalInLineup = false;
            LastOutcome = null;
            LastScorerSlot = -1;
            ScoredWindowOpen = WinBeatOpen = CanLocalSkip = false;
            TakerBody = KeeperBody = null;
            BallSpot = null;
            SpotIndex = 0;
            BallSpotPos = DefaultBallSpot(setup.Format);

            Phase = RoundPhase.Idle;
            PhaseTime = 0f;
            Configured = true;

            OnConfigured();   // IMPLEMENTED BY AGENT C1: build bodies, referee, wall, ball, cameras under Setup.Root
        }

        /// <summary>
        /// Set the coin winner after the toss (before Begin). Rewrites the kick line only while it
        /// is empty; a line with kicks keeps its first kicker (the toss cannot be redone mid-round).
        /// </summary>
        public void SetFirstKicker(CupSide side)
        {
            if (Setup == null) return;
            Setup.FirstKicker = side;
            if (Data != null) Data.FirstKicker = side;
            if (Line == null || Line.Count == 0)
            {
                Line = new RoundLine(side, CupTuning.KicksEach);
                RefreshFromLine();
            }
            else if (Line.FirstKicker != side)
            {
                CupLog.Warn("CupRoundDriver.SetFirstKicker: line already has " + Line.Count + " kick(s) with first kicker " + CupSides.Name(Line.FirstKicker) + "; ignored");
            }
        }

        /// <summary>Start the round: Intro card first. A no-op unless Configured and Idle.</summary>
        public void Begin()
        {
            if (!Configured) { CupLog.Warn("CupRoundDriver.Begin: not configured"); return; }
            if (Phase != RoundPhase.Idle) { CupLog.Warn("CupRoundDriver.Begin: already begun (" + Phase + ")"); return; }
            SetPhase(RoundPhase.Intro);
            OnBegin();   // IMPLEMENTED BY AGENT C1
        }

        /// <summary>End the round early (a leaver, End Match): straight to Over, no choreography.</summary>
        public void Abort()
        {
            if (!Configured) return;
            ScoredWindowOpen = WinBeatOpen = CanLocalSkip = false;
            KickClockRemaining = 0f;
            OnAbort();   // IMPLEMENTED BY AGENT C1: stop takers/keepers, free the cursor, park cameras
            if (Phase != RoundPhase.Over) SetPhase(RoundPhase.Over);
        }

        /// <summary>
        /// The local player's click to skip the open window (scored window / win beat). Honoured
        /// only while <see cref="CanLocalSkip"/>; on a Client this is the director's job to send
        /// (CupRequest.SkipCelebration) - the driver only skips under Local / Host authority.
        /// </summary>
        public void SkipCelebration()
        {
            if (!Configured || !CanLocalSkip) return;
            if (Authority == RoundAuthority.Client) return;   // the director raised the request; the host's state will close the window
            OnSkipCelebration();   // IMPLEMENTED BY AGENT C1: close the open window for everyone
        }

        /// <summary>
        /// Move to a phase: resets PhaseTime, fires PhaseChanged (and WhistleRaised / Whistled for
        /// those two phases). Driver-internal in spirit; a choreography helper on the same round
        /// may call it, screens must not.
        /// </summary>
        public void SetPhase(RoundPhase next)
        {
            var prev = Phase;
            Phase = next;
            PhaseTime = 0f;
            if (next == RoundPhase.Armed) KickClockRemaining = CupTuning.KickClock;
            else if (next != RoundPhase.Live) KickClockRemaining = 0f;
            OnPhaseChanged(prev, next);   // IMPLEMENTED BY AGENT C1 (optional)
            PhaseChanged?.Invoke(next);
            if (next == RoundPhase.WhistleRaise) WhistleRaised?.Invoke();
            else if (next == RoundPhase.Armed) Whistled?.Invoke();
        }

        // ---- kick bookkeeping (shared by Local and Host authority) ------------------------------
        /// <summary>
        /// Record a kick for the side on the ball: appends to the line, updates the scores, the
        /// next kicker, sudden death, LastOutcome / LastScorerSlot, fires KickResolved and, when
        /// the rules decide the round, RoundDecided. Returns the winner or null. Does NOT change the
        /// phase - the choreography does that (Scored / WalkBack / Decided). Throws nothing: an
        /// out-of-turn or post-decision kick is logged and ignored.
        /// </summary>
        public CupSide? ResolveKick(KickOutcome outcome, int scorerSlot)
        {
            if (Line == null) { CupLog.Error("CupRoundDriver.ResolveKick: no line (not configured)"); return null; }
            CupSide side = CupRoundRules.NextKicker(Line);
            try
            {
                CupRoundRules.RecordKick(Line, side, outcome);
            }
            catch (InvalidOperationException e)
            {
                CupLog.Error("CupRoundDriver.ResolveKick: " + e.Message);
                return CupRoundRules.Winner(Line);
            }
            LastOutcome = outcome;
            LastScorerSlot = outcome == KickOutcome.Goal ? scorerSlot : -1;
            RefreshFromLine();
            if (Data != null)
            {
                // Mirror the live line onto the record so a state capture / late joiner sees it;
                // the director's RecordResult writes the FINAL result (Done, winner).
                Data.FirstKicker = Line.FirstKicker;
                Data.Kicks.Clear();
                for (int i = 0; i < Line.Kicks.Count; i++) Data.Kicks.Add(Line.Kicks[i].Clone());
                Data.ScoreA = ScoreA;
                Data.ScoreB = ScoreB;
                Data.SuddenDeath = SuddenDeath;
            }
            OnKickResolved(outcome, side, scorerSlot);   // IMPLEMENTED BY AGENT C1 (optional)
            KickResolved?.Invoke(outcome, side, LastScorerSlot);
            CupSide winner;
            if (CupRoundRules.IsDecided(Line, out winner))
            {
                RoundDecided?.Invoke(winner);
                return winner;
            }
            return null;
        }

        /// <summary>The scoreboard sub-line: "KICK 3 of 5", or "SUDDEN DEATH - KICK 7" (per-side number).</summary>
        public string KickLabel
        {
            get
            {
                if (Line == null) return "";
                int n = CupRoundRules.KickNumberFor(Line, Kicker);
                return SuddenDeath ? CupText.SuddenDeathKick(n) : CupText.KickOf(n, Line.KicksEach);
            }
        }

        /// <summary>The Co-op shooter for the current kick: index into Setup.CoopOrderSlots (1-based slots, index 0 is the keeper), or -1 when the kicking side is not the team.</summary>
        public int CoopShooterOrderIndex
        {
            get
            {
                if (Setup == null || Setup.Style != CupStyle.Coop || Line == null) return -1;
                if (Kicker != Setup.TeamSide) return -1;
                int shooters = Setup.CoopOrderSlots.Length - 1;   // minus the keeper
                if (shooters <= 0) return -1;
                return 1 + CupRoundRules.CoopShooterFor(Line.Taken(Kicker), shooters);
            }
        }

        /// <summary>The human slot taking the next kick, or -1 for an AI (Co-op cycles the order; the keeper never shoots unless alone).</summary>
        public int TakerSlotForNextKick
        {
            get
            {
                if (Setup == null) return -1;
                if (Setup.Style == CupStyle.Coop)
                {
                    int oi = CoopShooterOrderIndex;
                    if (oi < 0)
                    {
                        // A team with only a keeper (everyone else left): the keeper shoots too.
                        return (Kicker == Setup.TeamSide && Setup.CoopOrderSlots.Length == 1) ? Setup.CoopOrderSlots[0] : -1;
                    }
                    return oi < Setup.CoopOrderSlots.Length ? Setup.CoopOrderSlots[oi] : -1;
                }
                return Setup.HumanSlotOf(Kicker);
            }
        }

        /// <summary>The human slot keeping the next kick, or -1 for an AI keeper.</summary>
        public int KeeperSlotForNextKick
        {
            get
            {
                if (Setup == null) return -1;
                var keeping = CupSides.Other(Kicker);
                if (Setup.Style == CupStyle.Coop) return keeping == Setup.TeamSide ? Setup.CoopKeeperSlot : -1;
                return Setup.HumanSlotOf(keeping);
            }
        }

        // ---- replication -------------------------------------------------------------------------
        /// <summary>Fill a wire state from the read model (Host authority; the director broadcasts it).</summary>
        public void CaptureState(CupRoundState into)
        {
            if (into == null) return;
            into.Stage = Data != null ? Data.Stage : (Setup != null ? Setup.Stage : CupStage.RoundOf32);
            into.RoundIndex = Data != null ? Data.Index : 0;
            into.EntrantA = Data != null ? Data.EntrantA : -1;
            into.EntrantB = Data != null ? Data.EntrantB : -1;
            into.Phase = Phase;
            into.PhaseTime = PhaseTime;
            into.KickIndex = KickIndex;
            into.ScoreA = ScoreA;
            into.ScoreB = ScoreB;
            into.Kicks.Clear();
            if (Line != null) for (int i = 0; i < Line.Kicks.Count; i++) into.Kicks.Add(Line.Kicks[i].Clone());
            into.Kicker = Kicker;
            into.FirstKicker = Line != null && (Line.Count > 0 || Phase != RoundPhase.Idle) ? Line.FirstKicker : (CupSide?)null;
            into.SuddenDeath = SuddenDeath;
            into.KickClockRemaining = KickClockRemaining;
            into.SpotIndex = SpotIndex;
            into.CoinCaller = Setup != null ? Setup.CoinCaller : CupSide.A;
            into.LastOutcome = LastOutcome;
            into.LastScorerSlot = LastScorerSlot;
            into.ScoredWindowOpen = ScoredWindowOpen;
            into.WinBeatOpen = WinBeatOpen;
            into.TakerSlot = TakerSlotForNextKick;
            into.KeeperSlot = KeeperSlotForNextKick;
            into.Tick = Director != null ? Director.Tick : 0u;
            OnStateCaptured(into);   // IMPLEMENTED BY AGENT C1 (optional): body ids, window remaining, skip votes, coin call/result
        }

        /// <summary>
        /// Client authority: mirror a host state onto the read model. Rebuilds the line from the
        /// kick list, fires PhaseChanged when the phase moved, KickResolved for every kick that
        /// arrived since the last apply, and RoundDecided when the line became decided. Bodies
        /// and cameras react in C1's OnStateApplied.
        /// </summary>
        public void ApplyState(CupRoundState s)
        {
            if (s == null || !Configured) return;
            if (s.Tick < _lastAppliedTick && _lastAppliedTick != 0u) return;   // stale
            _lastAppliedTick = s.Tick;

            bool decidedBefore = Line != null && CupRoundRules.IsOver(Line);
            int kicksBefore = Line != null ? Line.Count : 0;
            var first = s.FirstKicker ?? (Setup != null ? Setup.FirstKicker : CupSide.A);
            if (Setup != null) Setup.FirstKicker = first;
            RoundLine line;
            string err;
            if (!CupRoundRules.Validate(s.Kicks, first, CupTuning.KicksEach, false, out line, out err))
            {
                CupLog.Warn("CupRoundDriver.ApplyState: bad kick line from the host (" + err + "); keeping the local line");
                line = Line ?? new RoundLine(first, CupTuning.KicksEach);
            }
            Line = line;
            RefreshFromLine();
            KickClockRemaining = s.KickClockRemaining;
            SpotIndex = s.SpotIndex;
            LastOutcome = s.LastOutcome;
            LastScorerSlot = s.LastScorerSlot;
            ScoredWindowOpen = s.ScoredWindowOpen;
            WinBeatOpen = s.WinBeatOpen;
            if (Data != null)
            {
                Data.FirstKicker = Line.FirstKicker;
                Data.Kicks.Clear();
                for (int i = 0; i < Line.Kicks.Count; i++) Data.Kicks.Add(Line.Kicks[i].Clone());
                Data.ScoreA = ScoreA;
                Data.ScoreB = ScoreB;
                Data.SuddenDeath = SuddenDeath;
            }

            for (int i = kicksBefore; i < Line.Kicks.Count; i++)
            {
                var k = Line.Kicks[i];
                KickResolved?.Invoke(k.Outcome, k.Side, i == Line.Kicks.Count - 1 && k.Scored ? LastScorerSlot : -1);
            }
            CupSide winner;
            if (!decidedBefore && CupRoundRules.IsDecided(Line, out winner)) RoundDecided?.Invoke(winner);

            if (s.Phase != Phase)
            {
                var prev = Phase;
                Phase = s.Phase;
                PhaseTime = s.PhaseTime;
                OnPhaseChanged(prev, Phase);
                PhaseChanged?.Invoke(Phase);
                if (Phase == RoundPhase.WhistleRaise) WhistleRaised?.Invoke();
                else if (Phase == RoundPhase.Armed) Whistled?.Invoke();
            }
            else
            {
                PhaseTime = s.PhaseTime;
            }
            OnStateApplied(s);   // IMPLEMENTED BY AGENT C1 (optional): local roles, bodies, cameras, windows
        }

        uint _lastAppliedTick;

        // ---- setters for the partial implementation (same class, other files) --------------------
        // C1's partial files assign the read model through these so every write goes through one
        // place (the properties are private-set, which a partial file can also reach directly).
        protected void SetLocalRoles(bool taker, bool keeper, bool lineup)
        {
            LocalIsTaker = taker;
            LocalIsKeeper = keeper;
            LocalInLineup = lineup;
        }

        protected void SetBodies(ActiveRagdoll taker, ActiveRagdoll keeper)
        {
            TakerBody = taker;
            KeeperBody = keeper;
        }

        protected void SetBallSpot(Transform marker, Vector3 pos, int spotIndex)
        {
            BallSpot = marker;
            BallSpotPos = pos;
            SpotIndex = spotIndex;
        }

        protected void SetWindows(bool scoredWindow, bool winBeat, bool localCanSkip)
        {
            ScoredWindowOpen = scoredWindow;
            WinBeatOpen = winBeat;
            CanLocalSkip = localCanSkip;
        }

        protected void SetKickClock(float remaining)
        {
            KickClockRemaining = remaining < 0f ? 0f : remaining;
        }

        // ---- internals ------------------------------------------------------------------------
        void RefreshFromLine()
        {
            if (Line == null) return;
            ScoreA = Line.GoalsA;
            ScoreB = Line.GoalsB;
            KickIndex = Line.Count;
            Kicker = CupRoundRules.NextKicker(Line);
            SuddenDeath = CupRoundRules.IsSuddenDeath(Line);
        }

        /// <summary>The penalty spot (11 m out on the goal axis); Free Kicks start there too until C1 draws the pair's spot.</summary>
        public static Vector3 DefaultBallSpot(CupFormat format)
        {
            var g = SimConfig.GoalCenter;
            return new Vector3(g.x, 0f, g.z - CupTuning.PenaltyDistance);
        }

        void Update()
        {
            if (!Configured) return;
            float dt = Time.deltaTime;
            PhaseTime += dt;
            if (Phase == RoundPhase.Armed && Authority != RoundAuthority.Client && KickClockRemaining > 0f)
            {
                KickClockRemaining -= dt;
                if (KickClockRemaining < 0f) KickClockRemaining = 0f;
            }
            OnTick(dt);   // IMPLEMENTED BY AGENT C1: the phase machine, takers, keepers, cameras, verdicts
        }

        void OnDestroy()
        {
            OnTornDown();   // IMPLEMENTED BY AGENT C1: free materials/meshes the round created, restore cameras
        }

        // ---- partial hooks: IMPLEMENTED BY AGENT C1 in CupRoundDriver.*.cs ------------------------
        // Classic partial methods: each is optional, void, private; a missing implementation
        // compiles to nothing, which is what makes this skeleton runnable today.
        /// <summary>After Configure stored everything: build the scene under Setup.Root.</summary>
        partial void OnConfigured();
        /// <summary>After Begin moved to Intro.</summary>
        partial void OnBegin();
        /// <summary>Every Update while Configured (dt = Time.deltaTime).</summary>
        partial void OnTick(float dt);
        /// <summary>Abort was called (before the phase is forced to Over).</summary>
        partial void OnAbort();
        /// <summary>The local player clicked to skip an open window (Local / Host authority only).</summary>
        partial void OnSkipCelebration();
        /// <summary>SetPhase moved prev -> next (before the public events fire).</summary>
        partial void OnPhaseChanged(RoundPhase prev, RoundPhase next);
        /// <summary>ResolveKick recorded a kick (before the public event fires).</summary>
        partial void OnKickResolved(KickOutcome outcome, CupSide side, int scorerSlot);
        /// <summary>CaptureState filled the common fields; add body ids, window timer, skip votes, coin fields.</summary>
        partial void OnStateCaptured(CupRoundState into);
        /// <summary>ApplyState mirrored a host state; react with bodies, cameras and local roles.</summary>
        partial void OnStateApplied(CupRoundState s);
        /// <summary>OnDestroy: free what the round built.</summary>
        partial void OnTornDown();
    }
}
