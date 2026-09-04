using System;

// PURE FILE: no UnityEngine. It also compiles in a plain .NET console app (see CupSelfTest).

namespace Trickshot
{
    /// <summary>
    /// How the cup is played. Solo is single player (one human, 31 AI nations, simulated AI rounds
    /// and a podium); Head to Head is up to 8 humans sharing one bracket, each on their own nation;
    /// Co-op is up to 8 humans as ONE nation with a shooting order and a human keeper per stage.
    /// The value rides the wire as a byte in <c>MatchConfig.cupStyle</c>: append, never reorder.
    /// </summary>
    public enum CupStyle : byte { Solo = 0, HeadToHead = 1, Coop = 2 }

    /// <summary>
    /// The kick format inside the cup. Penalties = the spot at 11 m, no wall, keeper on the line.
    /// Free Kicks = every kick PAIR has its own seeded spot in the cup band, a four-man wall at
    /// regulation distance. Rides the wire as <c>MatchConfig.cupFormat</c>: append, never reorder.
    /// </summary>
    public enum CupFormat : byte { Penalties = 0, FreeKicks = 1 }

    /// <summary>
    /// The five bracket levels. Called STAGES, never "rounds" - a round is one match between two
    /// nations. Numeric value == depth in the tree, which the helpers rely on (16 >> stage rounds).
    /// </summary>
    public enum CupStage : byte { RoundOf32 = 0, RoundOf16 = 1, QuarterFinal = 2, SemiFinal = 3, Final = 4 }

    /// <summary>
    /// The verdict on one kick. A free kick stopped by the wall is SAVED, never "blocked"; there is
    /// no EPIC tier in the cup (a save is a strike against the shooter). Packed into 2 bits on the
    /// wire (<see cref="CupRound"/>), so at most four values may ever exist.
    /// </summary>
    public enum KickOutcome : byte { Goal = 0, Saved = 1, Miss = 2 }

    /// <summary>Which side of a round: A is the round's first-listed entrant, B the second.</summary>
    public enum CupSide : byte { A = 0, B = 1 }

    /// <summary>The two faces of the referee's coin.</summary>
    public enum CoinFace : byte { Heads = 0, Tails = 1 }

    /// <summary>Stage arithmetic and names. Everything is a function of the enum value.</summary>
    public static class CupStages
    {
        /// <summary>Number of stages in a cup (Round of 32 through the Final).</summary>
        public const int Count = 5;
        public const CupStage First = CupStage.RoundOf32;
        public const CupStage Last = CupStage.Final;

        /// <summary>Long name: "Round of 32", "Round of 16", "Quarter-finals", "Semi-finals", "Final".</summary>
        public static string Name(CupStage s)
        {
            switch (s)
            {
                case CupStage.RoundOf32: return "Round of 32";
                case CupStage.RoundOf16: return "Round of 16";
                case CupStage.QuarterFinal: return "Quarter-finals";
                case CupStage.SemiFinal: return "Semi-finals";
                case CupStage.Final: return "Final";
                default: return "Stage " + (int)s;
            }
        }

        /// <summary>Short tag: R32, R16, QF, SF, F.</summary>
        public static string Short(CupStage s)
        {
            switch (s)
            {
                case CupStage.RoundOf32: return "R32";
                case CupStage.RoundOf16: return "R16";
                case CupStage.QuarterFinal: return "QF";
                case CupStage.SemiFinal: return "SF";
                case CupStage.Final: return "F";
                default: return "S" + (int)s;
            }
        }

        /// <summary>Upper-case header form: "ROUND OF 16", "QUARTER-FINALS", "FINAL".</summary>
        public static string Header(CupStage s) => Name(s).ToUpperInvariant();

        /// <summary>How many rounds (matches) the stage holds: 16, 8, 4, 2, 1.</summary>
        public static int RoundsIn(CupStage s) => IsValid(s) ? (16 >> (int)s) : 0;

        /// <summary>How many entrants enter the stage: 32, 16, 8, 4, 2.</summary>
        public static int EntrantsIn(CupStage s) => RoundsIn(s) * 2;

        public static bool IsValid(CupStage s) => (int)s >= 0 && (int)s < Count;
        public static bool IsLast(CupStage s) => s == CupStage.Final;
        public static bool IsFirst(CupStage s) => s == CupStage.RoundOf32;

        /// <summary>The stage after this one; the Final returns itself (there is nothing after it).</summary>
        public static CupStage Next(CupStage s) => IsLast(s) ? s : (CupStage)((int)s + 1);

        /// <summary>The stage after this one, false at the Final.</summary>
        public static bool TryNext(CupStage s, out CupStage next)
        {
            next = Next(s);
            return !IsLast(s);
        }

        /// <summary>The stage before this one; the Round of 32 returns itself.</summary>
        public static CupStage Previous(CupStage s) => IsFirst(s) ? s : (CupStage)((int)s - 1);

        /// <summary>Stage from its 0-based index, clamped into range.</summary>
        public static CupStage At(int i)
        {
            if (i < 0) i = 0;
            if (i >= Count) i = Count - 1;
            return (CupStage)i;
        }
    }

    /// <summary>Side helpers.</summary>
    public static class CupSides
    {
        public static CupSide Other(CupSide s) => s == CupSide.A ? CupSide.B : CupSide.A;
        public static int Index(CupSide s) => s == CupSide.A ? 0 : 1;
        public static CupSide At(int i) => i == 0 ? CupSide.A : CupSide.B;
        public static string Name(CupSide s) => s == CupSide.A ? "A" : "B";
    }

    /// <summary>Coin helpers.</summary>
    public static class CoinFaces
    {
        public static CoinFace Other(CoinFace f) => f == CoinFace.Heads ? CoinFace.Tails : CoinFace.Heads;
    }

    /// <summary>
    /// The salts every cup consumer forks its <see cref="SeededRng"/> streams with, so that two peers
    /// deriving "the coin of round 3 of the Quarter-finals" from the same cup seed land on the same
    /// stream without ever having consumed the same number of draws. Every family gets its own
    /// 0x1000 block; a per-round salt is <c>family + stage * 16 + index</c>. Never reuse a value.
    /// </summary>
    public static class CupSalts
    {
        /// <summary>The bracket draw and placement (<see cref="CupBracket.Build"/>).</summary>
        public const uint Draw = 0x0001u;
        /// <summary>Podium loser poses.</summary>
        public const uint Podium = 0x7000u;
        /// <summary>Podium confetti.</summary>
        public const uint Confetti = 0x7001u;

        const uint SimFamily = 0x1000u;
        const uint CoinFamily = 0x2000u;
        const uint SpotsFamily = 0x3000u;
        const uint CallerFamily = 0x4000u;
        const uint DejectionFamily = 0x5000u;
        const uint OrderFamily = 0x6000u;

        static uint RoundSalt(uint family, CupStage stage, int index) => family + (uint)stage * 16u + (uint)index;

        /// <summary>A simulated AI-vs-AI round's kick line (<see cref="CupSim"/>).</summary>
        public static uint Sim(CupStage stage, int index) => RoundSalt(SimFamily, stage, index);
        /// <summary>The referee's coin result for a played round.</summary>
        public static uint Coin(CupStage stage, int index) => RoundSalt(CoinFamily, stage, index);
        /// <summary>The free-kick spots of a played round (one per kick pair, generated lazily).</summary>
        public static uint Spots(CupStage stage, int index) => RoundSalt(SpotsFamily, stage, index);
        /// <summary>Which side is the official coin caller when two humans meet.</summary>
        public static uint Caller(CupStage stage, int index) => RoundSalt(CallerFamily, stage, index);
        /// <summary>Which of the three dejection emotes a losing side plays.</summary>
        public static uint Dejection(CupStage stage, int index) => RoundSalt(DejectionFamily, stage, index);
        /// <summary>The Co-op slot-machine permutation for a stage's shooting order.</summary>
        public static uint Order(CupStage stage) => OrderFamily + (uint)stage;
    }

    /// <summary>
    /// EVERY tunable of the cup in one place, named as the design doc names them. Gameplay,
    /// choreography, camera, coin, podium and simulation numbers live here; IMGUI pixel layout
    /// does not (that belongs beside the screen that draws it). Values marked (tune) are the
    /// starting guesses the design doc flagged for playtesting.
    /// </summary>
    public static class CupTuning
    {
        // ---- field --------------------------------------------------------------------------
        /// <summary>The field is always 32 nations.</summary>
        public const int Entrants = 32;
        /// <summary>At most 8 humans share a bracket (the net board has 8 slots).</summary>
        public const int MaxHumans = 8;

        // ---- stage ramp (AI strength, a pure function of the stage - never a knob) ------------
        /// <summary>AI keeper ability (SimConfig.KeeperAbility) per stage, R32..Final.</summary>
        public static readonly float[] KeeperAbilityByStage = { 0.20f, 0.40f, 0.60f, 0.80f, 1.00f };
        /// <summary>AI taker strength t per stage, R32..Final; maps through TakerMin/Max and PowerMin/Max.</summary>
        public static readonly float[] TakerTByStage = { 0.20f, 0.40f, 0.60f, 0.80f, 1.00f };
        /// <summary>combined = Lerp(TakerMin, TakerMax, t) for the aim model (SetPieceTaker combinedOverride). (tune)</summary>
        public const float TakerMin = 0.35f;
        public const float TakerMax = 0.95f;
        /// <summary>Power-bar target = Lerp(PowerMin, PowerMax, t). The launch ceiling stays the human one. (tune)</summary>
        public const float PowerMin = 0.55f;
        public const float PowerMax = 0.85f;
        /// <summary>The bot taker waits this long after the whistle before charging (seconds, seeded).</summary>
        public const float BotDelayMin = 0.8f;
        public const float BotDelayMax = 1.6f;

        public static float KeeperAbility(CupStage stage) => KeeperAbilityByStage[ClampStage(stage)];
        public static float TakerT(CupStage stage) => TakerTByStage[ClampStage(stage)];
        /// <summary>The aim-model strength handed to SetPieceTaker.Begin(combinedOverride) for an AI taker.</summary>
        public static float TakerCombined(CupStage stage) => Lerp(TakerMin, TakerMax, TakerT(stage));
        /// <summary>The power-meter fraction an AI taker charges to.</summary>
        public static float TakerPower(CupStage stage) => Lerp(PowerMin, PowerMax, TakerT(stage));

        // ---- the round ----------------------------------------------------------------------
        /// <summary>Regulation kicks per side; level after these means sudden death.</summary>
        public const int KicksEach = 5;
        /// <summary>
        /// Seconds the taker has from the whistle before the weak auto-shot fires. The clock is
        /// INVISIBLE by design (owner's call): nothing draws a dial, a number or a warning as it
        /// runs down, so this is a pure gameplay deadline and the taker judges the moment off the
        /// pitch. It still crosses the wire in CupRoundState for the auto-shot to fire on the
        /// authority. There is deliberately no "ring" constant beside it - a warning window with
        /// nothing rendering it would read as a knob that moves something.
        /// </summary>
        public const float KickClock = 30f;
        /// <summary>Power of the existing weak auto-shot the kick clock fires (AutoLaunch(0.6)).</summary>
        public const float AutoLaunchPower = 0.6f;

        // ---- formats ------------------------------------------------------------------------
        /// <summary>Penalty spot distance from the goal line (m).</summary>
        public const float PenaltyDistance = 11f;
        /// <summary>Free-kick band: distance from goal (m), and the half-width across (m). (tune the width)</summary>
        public const float FreeKickMinDist = 17f;
        public const float FreeKickMaxDist = 28f;
        public const float FreeKickHalfWidth = 18f;
        /// <summary>Four-man wall at the regulation distance (m).</summary>
        public const int WallCount = 4;
        public const float WallDistance = 9.15f;

        // ---- free kicks: the scatter and the miss beat (owner's call, replaces the lineup) ------
        // In Free Kicks there is NO lineup and NO walking to or from one: every taker starts at
        // the run-up start behind the ball, and the bodies that are neither taking nor keeping
        // stand scattered BEHIND the taker. Distances are measured back from the ball along the
        // ball->goal line (depth) and sideways from that line (lateral); the run-up start sits
        // RunUpDistance back on the line, the referee RefereeSideOffset to the right of the ball.
        /// <summary>The taker's own team: 3-8 m behind the ball, a loose group, facing the goal.</summary>
        public const float FreeKickTeamDepthMin = 3f;
        public const float FreeKickTeamDepthMax = 8f;
        /// <summary>
        /// The team fans out from the run-up line: the lateral offset grows with depth
        /// (LateralMin at DepthMin, up to LateralMax at DepthMax), so the follow camera behind the
        /// taker looks between them rather than through a back.
        /// </summary>
        public const float FreeKickTeamLateralMin = 2.4f;
        public const float FreeKickTeamLateralMax = 5.5f;
        /// <summary>The opposing side's non-keeping bodies: further back and off to the side, away from the camera line.</summary>
        public const float FreeKickOppDepthMin = 10f;
        public const float FreeKickOppDepthMax = 14f;
        public const float FreeKickOppLateralMin = 4f;
        public const float FreeKickOppLateralMax = 9f;
        /// <summary>No two scattered bodies (nor a body and the run-up start / the referee) closer than this (m).</summary>
        public const float FreeKickMarkClearance = 1.2f;
        /// <summary>A scattered body faces the goal with this much seeded yaw either way (deg), so a group never reads as clones.</summary>
        public const float FreeKickWatchYawJitter = 12f;
        /// <summary>A missed / saved free kick: the shooter dejects where he stands for this long, then the cut (no walk-back).</summary>
        public const float FreeKickMissBeat = 3f;

        // ---- verdict (both formats) --------------------------------------------------------
        /// <summary>
        /// A live ball still short of being fully over the goal line and moving toward it faster
        /// than this (m/s, along the goal axis) is BOUND FOR THE GOAL: the rest and time-out
        /// verdicts wait for it. BallController kills a roll below SimConfig.BallRollStop (0.35),
        /// so a creeping ball reads stopped as soon as the roll dies, never before.
        /// </summary>
        public const float LiveApproachSpeed = 0.15f;
        /// <summary>The unconditional cap on a live attempt (s): a ball bound for the goal is given this long, then the verdict is called on what it did.</summary>
        public const float LiveHardCap = 20f;

        // ---- timings (seconds) --------------------------------------------------------------
        /// <summary>The bracket screen, every style, no button.</summary>
        public const float BracketScreenSeconds = 5f;
        /// <summary>The official caller has this long to call; expiry calls HEADS for them.</summary>
        public const float CoinCallTimeout = 5f;
        /// <summary>From the call to the flash: the ceremony (flight, bounce, hold).</summary>
        public const float CoinCeremonySeconds = 3f;
        /// <summary>The loading card shows at least this long.</summary>
        public const float LoadingMinSeconds = 1.5f;
        /// <summary>MP "everyone loaded" barrier gives up after this long and the toss starts anyway.</summary>
        public const float LoadBarrierTimeout = 10f;
        /// <summary>The round intro card (nations, stage, first kick).</summary>
        public const float IntroSeconds = 3f;
        /// <summary>The referee's forearm-to-mouth raise before EVERY whistle; the whistle plays at its end.</summary>
        public const float WhistleRaiseSeconds = 0.4f;
        /// <summary>He holds the raise this long after the whistle, then drops it.</summary>
        public const float WhistleHoldAfter = 0.5f;
        /// <summary>The scorer's free run-and-emote window; the scorer may click to skip it for everyone.</summary>
        public const float ScoredWindow = 5f;
        /// <summary>The cut that puts the scorer back in the lineup after the window.</summary>
        public const float ScoredCutSeconds = 0.3f;
        /// <summary>The walk-back cinematic after a miss/save is cut off at this long.</summary>
        public const float WalkBackMax = 3.5f;
        /// <summary>
        /// Walking speed of the shooter on the walk-back (m/s).
        ///
        /// SIZED AGAINST THE WALK, not chosen for its own sake: the cinematic is supposed to end on
        /// the arrival (design 7.5, "a cut to a wide shot from behind the lineup as they arrive"),
        /// so the longest walk must fit inside WalkBackMax or the cut always hides a snap instead.
        /// The penalty spot is GoalCenter.z - PenaltyDistance (11) and LineupZ is
        /// GoalCenter.z - PenaltyBoxDepth (16.5) - LineupBehindBox (1), so the walk is 6.5 m of z
        /// against LineupX (6) plus up to two LineupSpacing (0.62) steps of x for the outermost
        /// slot in a five-body line: sqrt(7.24^2 + 6.5^2) = 9.73 m, needing 2.78 m/s to arrive in
        /// 3.5 s. 2.85 leaves a small margin over the outermost mark and still reads as a brisk
        /// walk rather than a jog. Raise this, not WalkBackMax: the 3.5 s beat is a pacing decision
        /// in design 2.7's timing table, while the pace is only ever a consequence of the geometry.
        /// (The earlier 1.6 needed 6.1 s and never once reached the line.)
        /// </summary>
        public const float WalkSpeed = 2.85f;
        /// <summary>The first (low tracking) shot of the walk-back two-shot sequence.</summary>
        public const float WalkBackTrackShot = 1.5f;
        /// <summary>Won the round: the whole lineup is free to move and emote this long (skippable by the scorer / winning keeper).</summary>
        public const float WinBeat = 5f;
        /// <summary>Lost the round: the dejection beat before results.</summary>
        public const float DejectionBeat = 4f;
        /// <summary>In the falling dejection, the arms-on-head pose holds this long before balance drops.</summary>
        public const float DejectionFallHold = 0.8f;
        /// <summary>Replay recording window (seconds of real play kept) - passed to ReplaySystem.Setup, never SimConfig.ReplayWindow.</summary>
        public const float ReplayWindow = 3f;
        /// <summary>Replay playback speed multiplier (about 6.7 s watched). (tune)</summary>
        public const float ReplaySlow = 0.45f;
        /// <summary>The Co-op calls band shows this long after the coin settles.</summary>
        public const float CallsBandSeconds = 3f;
        /// <summary>Bracket screen: Round of 32 names fade in over this long, in tree order.</summary>
        public const float RevealSeconds = 1.2f;
        /// <summary>Cup lobby: result rows stagger in this far apart.</summary>
        public const float RowStagger = 0.05f;
        /// <summary>Nation picker strip: a picked flag pops in (EaseOutBack) over this long, unscaled.</summary>
        public const float FlagPopSeconds = 0.25f;
        /// <summary>Co-op slot machine: lever arc, reel spin, and the gap between slots stopping.</summary>
        public const float LeverSeconds = 0.3f;
        public const float ReelSpinSeconds = 1.8f;
        public const float ReelStopGap = 0.25f;
        /// <summary>Podium: the buttons appear this long after the podium is built.</summary>
        public const float PodiumButtonsDelay = 3f;
        /// <summary>The Co-op trophy-lift cinematic length before the free window.</summary>
        public const float TrophyLiftSeconds = 14f;

        // ---- marks and choreography (metres / degrees) ---------------------------------------
        /// <summary>Lineup bodies stand this far apart (same as DefensiveWall.ShoulderSpacing).</summary>
        public const float LineupSpacing = 0.62f;
        /// <summary>Lineups at x = -LineupX (human team) and +LineupX (AI team).</summary>
        public const float LineupX = 6f;
        /// <summary>Lineups stand this far outside the 18-yard line (GoalCenter.z - PenaltyBoxDepth - LineupBehindBox).</summary>
        public const float LineupBehindBox = 1f;
        /// <summary>The referee's mark during play: this far to the side of the ball, level with it.</summary>
        public const float RefereeSideOffset = 3f;
        /// <summary>The taker's run-up start behind the ball.</summary>
        public const float RunUpDistance = 3f;
        /// <summary>Coin toss: the captains stand this far either side of the referee, facing him.</summary>
        public const float CaptainOffset = 1.2f;
        /// <summary>Lineup look cone: yaw either way, and the pitch range (degrees).</summary>
        public const float LineupYawLimit = 50f;
        public const float LineupPitchMin = -10f;
        public const float LineupPitchMax = 25f;

        // ---- the coin ------------------------------------------------------------------------
        /// <summary>A gold MeshGen.Cylinder disc: diameter and thickness (m).</summary>
        public const float CoinDiameter = 0.25f;
        public const float CoinThickness = 0.02f;
        /// <summary>Up and down on a scripted arc (s), spinning this many revolutions per second.</summary>
        public const float CoinFlightSeconds = 1.4f;
        public const float CoinSpinRps = 6f;
        /// <summary>Lands this far in front of the referee, one small bounce, settles face-up on the seeded result.</summary>
        public const float CoinLandDistance = 1f;
        /// <summary>Hold on the settled coin before the flash.</summary>
        public const float CoinHoldSeconds = 0.6f;

        // ---- cameras -------------------------------------------------------------------------
        /// <summary>Coin toss framing: static wide shot from the goal side, low angle.</summary>
        public const float CoinCamDistance = 6f;
        public const float CoinCamHeight = 1.6f;
        public const float CoinCamFov = 55f;
        /// <summary>
        /// Penalty camera: the FLOOR on how far behind the ball the camera stands (m), on the
        /// ball-to-goal line.
        ///
        /// CupPenaltyCam OWNS the effective placement, and this floor never wins: the rig takes
        /// Max(PenaltyCamBack, takerBehind + CupPenaltyCam.MinBehindTaker), and with the taker at
        /// RunUpDistance (3 m) behind the ball and MinBehindTaker 4 m that is 7 m every time. The
        /// height is owned outright by CupPenaltyCam.CamHeight (2.4 m); there is deliberately no
        /// PenaltyCamHeight here any more, because a constant with no reader looks like a tuning
        /// knob and moves nothing. Tune the rig's two constants, not this one.
        /// </summary>
        public const float PenaltyCamBack = 3f;
        /// <summary>
        /// The framing TARGET: the posts sit at this fraction and (1 - this) of the frame width when
        /// looking at the goal centre. It is a target, not a guarantee - keeping the ball in frame is
        /// the hard rule and wins at the real 7 m / 2.4 m placement, which lands the outer post
        /// nearer 31% / 69% (see CupPenaltyCam's Solve).
        /// </summary>
        public const float PenaltyCamPostFrac = 0.11f;
        /// <summary>Penalty camera look clamp so the goal never leaves the frame (degrees).</summary>
        public const float PenaltyCamYawLimit = 25f;
        public const float PenaltyCamPitchMin = -5f;
        public const float PenaltyCamPitchMax = 20f;

        // ---- podium --------------------------------------------------------------------------
        /// <summary>Slow orbit: radius (m), height (m), revolutions per second.</summary>
        public const float PodiumOrbitRadius = 6f;
        public const float PodiumOrbitHeight = 2.2f;
        public const float PodiumOrbitRps = 0.08f;
        /// <summary>A mouse drag takes the orbit over for this long; the wheel zooms within the range.</summary>
        public const float PodiumDragTakeover = 4f;
        public const float PodiumZoomMin = 3f;
        public const float PodiumZoomMax = 10f;
        /// <summary>The stepped dais: diameter and height (m).</summary>
        public const float PedestalDiameter = 1.6f;
        public const float PedestalHeight = 0.6f;
        /// <summary>The trophy stands about this tall (m), parented to the LEFT forearm at this local y (the AddGlove offset).</summary>
        public const float TrophyHeight = 0.45f;
        public const float TrophyForearmY = -0.22f;
        /// <summary>Confetti quads, dropped from this height, in the nation's two kit colours.</summary>
        public const int ConfettiCount = 200;
        public const float ConfettiHeight = 8f;
        /// <summary>The podium shows at least this many losers (AI bodies fill in).</summary>
        public const int PodiumMinLosers = 3;

        // ---- simulated AI rounds (CupSim) ----------------------------------------------------
        /// <summary>P(goal) = clamp(SimBaseGoalP + SimStrengthSlope * (taker01 - keeper01), SimMinP, SimMaxP).</summary>
        public const float SimBaseGoalP = 0.72f;
        public const float SimStrengthSlope = 0.20f;
        public const float SimMinP = 0.45f;
        public const float SimMaxP = 0.92f;
        /// <summary>
        /// Sudden death in a SIMULATED round is capped at this many pairs (so a line is at most
        /// 2 * KicksEach + 2 * this = 30 kicks). The last allowed pair is drawn from the conditional
        /// distribution of a decisive pair, which gives exactly the winner distribution of unbounded
        /// play - only the line length is truncated. See CupSim.
        /// </summary>
        public const int SimMaxSuddenDeathPairs = 10;
        /// <summary>Share of simulated non-goals recorded as SAVED (the rest are MISS) - pip flavour only.</summary>
        public const float SimSaveShare = 0.55f;

        /// <summary>
        /// The hard ceiling on the number of kicks in ANY round's line - played as well as
        /// simulated - and therefore a WIRE bound, not a gameplay preference.
        ///
        /// Every played round rides CupState as its full kick line (3 + ceil(n/2) bytes each, 31
        /// rounds), and CupState goes out on NetChannel.Reliable, which DirectIpTransport never
        /// fragments: one payload, one datagram. The codec writes the kick count as a u8, so an
        /// unbounded line is bounded only at 255, which puts the worst-case CupState at about
        /// 4.2 KB - IP-fragmented and, off loopback, likely dropped outright, which would silently
        /// stall every client's model. It is also reachable from OUTSIDE: a modified or buggy
        /// client can report any 255-kick line through CupRequest.RoundResult, whose 1 KB payload
        /// admits it easily. Sudden death is genuinely unbounded in the rules (pairs continue while
        /// both score) and a Round-of-32 keeper sits at ability 0.20, so long lines are not only a
        /// hostile case.
        ///
        /// The value is the one CupSim has always used for the same reason - 2 * KicksEach for the
        /// regulation kicks plus 2 * SimMaxSuddenDeathPairs - so a played line and a simulated one
        /// can never disagree about what lengths are legal. At this cap the worst-case CupState is
        /// about 876 B, inside the ~1.2 KB single-datagram budget.
        ///
        /// ENFORCED AT BOTH ENDS:
        ///   * a longer REPORTED line is refused - CupRoundRules.Validate takes this as its
        ///     maxKicks and both wire seams pass it (the host's CupDirector.ApplyRoundResult, which
        ///     CupLog.Warns and lets the wave watchdog settle the round, and a client's
        ///     CupRoundDriver.ApplyState, which keeps its local line); and
        ///   * the LIVE round cannot grow past it - CupRoundDriver.CapOutcome overrides the last
        ///     allowed kick's outcome when the pair would otherwise be level, so the line always
        ///     ends DECIDED (the bracket's SetResult accepts nothing else).
        /// </summary>
        public const int MaxKicksInLine = KicksEach * 2 + SimMaxSuddenDeathPairs * 2;

        // ---- nations -------------------------------------------------------------------------
        /// <summary>Strength range of the nation table (hidden flavour; biases CupSim only).</summary>
        public const int StrengthMin = 1;
        public const int StrengthMax = 99;

        // ---- career / achievements -----------------------------------------------------------
        /// <summary>"Giant Killer": beat a nation at least this much stronger than yours.</summary>
        public const int GiantKillerMargin = 30;
        /// <summary>"Pundit": this many correct coin calls.</summary>
        public const int PunditCalls = 25;

        // ---- helpers -------------------------------------------------------------------------
        /// <summary>Plain linear interpolation with t clamped to 0..1 (no UnityEngine in this file).</summary>
        public static float Lerp(float a, float b, float t)
        {
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return a + (b - a) * t;
        }

        /// <summary>Normalise a table strength (StrengthMin..StrengthMax) to 0..1.</summary>
        public static float Strength01(int strength)
        {
            float t = (strength - StrengthMin) / (float)(StrengthMax - StrengthMin);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return t;
        }

        static int ClampStage(CupStage s)
        {
            int i = (int)s;
            if (i < 0) i = 0;
            if (i >= CupStages.Count) i = CupStages.Count - 1;
            return i;
        }
    }

    /// <summary>
    /// The user-facing strings that must be EXACT (some of them are classified by Hud.KindOf, some
    /// are read back in tests, all of them are what the design doc promised). Anything with a
    /// parameter is a builder over those constants so a caller never re-spells them.
    /// </summary>
    public static class CupText
    {
        // ---- titles --------------------------------------------------------------------------
        public const string Title = "TRICKSHOT CUP";
        public const string TitleMixed = "Trickshot Cup";
        public const string ChooseYourNation = "CHOOSE YOUR NATION";
        public const string CoinToss = "COIN TOSS";
        public const string CoinTossHeader = "COIN TOSS - call it";
        public const string TheDraw = "THE DRAW";
        public const string ShootingOrder = "SHOOTING ORDER";
        public const string CupSummary = "CUP SUMMARY";
        public const string GameOver = "GAME OVER";
        public const string Champions = "CHAMPIONS";
        public const string Calls = "CALLS";
        public const string Total = "TOTAL";
        public const string Novelty = "NOVELTY";
        public const string HeadToHeadUpNext = "HEAD TO HEAD - up next";

        // ---- style / format names ----------------------------------------------------------
        public const string SoloName = "Solo";
        public const string HeadToHeadName = "Head to Head";
        public const string CoopName = "Co-op";
        public const string PenaltiesName = "Penalties";
        public const string FreeKicksName = "Free Kicks";

        public static string StyleName(CupStyle s)
        {
            switch (s)
            {
                case CupStyle.Solo: return SoloName;
                case CupStyle.HeadToHead: return HeadToHeadName;
                case CupStyle.Coop: return CoopName;
                default: return "Style " + (int)s;
            }
        }

        public static string FormatName(CupFormat f)
        {
            switch (f)
            {
                case CupFormat.Penalties: return PenaltiesName;
                case CupFormat.FreeKicks: return FreeKicksName;
                default: return "Format " + (int)f;
            }
        }

        /// <summary>One-line blurb under the selected play style on the host setup screen.</summary>
        public static string StyleBlurb(CupStyle s)
        {
            switch (s)
            {
                case CupStyle.Solo: return "One nation, five stages, a podium at the end.";
                case CupStyle.HeadToHead: return "Every player on their own nation; meet from the Round of 16.";
                case CupStyle.Coop: return "Everyone on one nation: a shooting order and a keeper per stage.";
                default: return "";
            }
        }

        /// <summary>Lobby advert / browser label: "Trickshot Cup - Head to Head - Penalties".</summary>
        public static string Label(CupStyle style, CupFormat format)
            => TitleMixed + " - " + StyleName(style) + " - " + FormatName(format);

        /// <summary>Browser meta: "Head to Head - Penalties".</summary>
        public static string Meta(CupStyle style, CupFormat format)
            => StyleName(style) + " - " + FormatName(format);

        /// <summary>The upper-case tag under a screen title: "TRICKSHOT CUP - HEAD TO HEAD - PENALTIES".</summary>
        public static string TitleTag(CupStyle style, CupFormat format)
            => Title + " - " + StyleName(style).ToUpperInvariant() + " - " + FormatName(format).ToUpperInvariant();

        /// <summary>Panel / lobby title with the stage: "TRICKSHOT CUP - ROUND OF 16".</summary>
        public static string StageTitle(CupStage stage) => Title + " - " + CupStages.Header(stage);

        /// <summary>Order screen title: "SHOOTING ORDER - ROUND OF 16".</summary>
        public static string OrderTitle(CupStage stage) => ShootingOrder + " - " + CupStages.Header(stage);

        // ---- verdicts (Hud.Flash) -----------------------------------------------------------
        public const string Goal = "GOAL";
        public const string Saved = "SAVED";
        public const string Miss = "MISS";

        public static string Verdict(KickOutcome o)
        {
            switch (o)
            {
                case KickOutcome.Goal: return Goal;
                case KickOutcome.Saved: return Saved;
                default: return Miss;
            }
        }

        // ---- the coin ------------------------------------------------------------------------
        public const string Heads = "HEADS";
        public const string Tails = "TAILS";
        public const string CallIt = "CALL IT";
        public const string CaptainDecides = "CAPTAIN DECIDES";
        public const string DecidesKickOff = "DECIDES KICK-OFF";
        /// <summary>Appended to the upper-case nation name: "GHANA KICK FIRST".</summary>
        public const string KickFirstSuffix = " KICK FIRST";

        public static string CoinName(CoinFace f) => f == CoinFace.Heads ? Heads : Tails;
        public static string KickFirst(string nationName) => (nationName ?? "").ToUpperInvariant() + KickFirstSuffix;

        // ---- round end (Hud.Banner) ----------------------------------------------------------
        public const string KnockedOut = "KNOCKED OUT";
        /// <summary>Appended to the upper-case nation name in the winning banner: "BRAZIL WIN 4-2".</summary>
        public const string WinSuffix = " WIN";
        public const string SuddenDeath = "SUDDEN DEATH";
        /// <summary>The tag beside a score that went to sudden death: "5-4 SD".</summary>
        public const string SuddenDeathTag = "SD";

        /// <summary>"4-2", or "5-4 SD" when the round went to sudden death.</summary>
        public static string ScoreLine(int a, int b, bool suddenDeath)
            => suddenDeath ? a + "-" + b + " " + SuddenDeathTag : a + "-" + b;

        /// <summary>"BRAZIL WIN 4-2" (own score first).</summary>
        public static string WinLine(string nationName, int own, int theirs)
            => (nationName ?? "").ToUpperInvariant() + WinSuffix + " " + own + "-" + theirs;

        /// <summary>"KNOCKED OUT 2-3" (own score first).</summary>
        public static string KnockedOutLine(int own, int theirs) => KnockedOut + " " + own + "-" + theirs;

        /// <summary>The Solo card: "KNOCKED OUT IN THE ROUND OF 16".</summary>
        public static string KnockedOutIn(CupStage stage) => KnockedOut + " IN THE " + CupStages.Header(stage);

        // ---- in-round HUD --------------------------------------------------------------------
        /// <summary>Scoreboard sub-line in regulation: "KICK 3 of 5".</summary>
        public static string KickOf(int kickNumber, int kicksEach) => "KICK " + kickNumber + " of " + kicksEach;
        /// <summary>Scoreboard sub-line in sudden death: "SUDDEN DEATH - KICK 7".</summary>
        public static string SuddenDeathKick(int kickNumber) => SuddenDeath + " - KICK " + kickNumber;
        public const string Taking = "Taking";
        public const string Keeping = "Keeping";
        public const string InTheLineup = "In the lineup";
        public static string Watching(string playerName) => "Watching " + playerName;

        // ---- skips ---------------------------------------------------------------------------
        public const string ClickToSkip = "CLICK TO SKIP";
        /// <summary>The unanimous replay skip: "CLICK TO SKIP  2/3" (two spaces, as designed).</summary>
        public static string ClickToSkipVotes(int votes, int voters) => ClickToSkip + "  " + votes + "/" + voters;
        /// <summary>The existing replay flash wording, kept for the cup's replay start.</summary>
        public const string ReplayFlash = "REPLAY (click to skip)";

        // ---- lobby / bracket ------------------------------------------------------------------
        public const string You = "YOU";
        public const string YourTeam = "YOUR TEAM";
        public const string EntrantRole = "Entrant";
        public const string AiTag = "(AI)";
        /// <summary>A leaver's row: "Alice (AI)".</summary>
        public static string AiName(string humanName) => humanName + " " + AiTag;
        public const string Ready = "Ready";
        public const string SimulatingRest = "Simulating the rest of the stage";
        public const string YourRoundStillOn = "your round is still on";
        public const string WaitingForHost = "waiting for the host";
        public const string WaitingForCaptain = "waiting for the captain";
        public const string CaptainIsChoosing = "Captain is choosing";
        public const string MajorityReached = "majority reached";
        public static string TakenBy(string playerName) => "taken by " + playerName;
        public static string Decides(string captainName) => captainName + " decides";

        /// <summary>"Won 4-2" / "Won 5-4 SD".</summary>
        public static string StatusWon(int own, int theirs, bool suddenDeath) => "Won " + ScoreLine(own, theirs, suddenDeath);
        /// <summary>"Out (lost 2-3 to BRA)".</summary>
        public static string StatusOut(int own, int theirs, string opponentCode) => "Out (lost " + own + "-" + theirs + " to " + opponentCode + ")";
        /// <summary>"Playing vs GHA - 2-1 - kick 4".</summary>
        public static string StatusPlaying(string opponentCode, int own, int theirs, int kickNumber)
            => "Playing vs " + opponentCode + " - " + own + "-" + theirs + " - kick " + kickNumber;
        public static string StatusSpectating(string playerName) => "Spectating " + playerName;
        public static string WaitingForRounds(int n) => "Waiting for " + n + (n == 1 ? " round" : " rounds") + " to finish";
        public static string WaitingForPlayers(string names) => "Waiting for " + names;
        public static string HeadToHeadNext(string a, string b) => "Head to head next: " + a + " vs " + b;
        /// <summary>The intro / loading card pairing: "BRA vs GHA".</summary>
        public static string Versus(string codeA, string codeB) => codeA + " vs " + codeB;
        /// <summary>Podium title strip: "CHAMPIONS - BRAZIL - Alice" (playerName may be null for an AI champion).</summary>
        public static string ChampionsStrip(string nationName, string playerName)
            => string.IsNullOrEmpty(playerName)
                ? Champions + " - " + (nationName ?? "").ToUpperInvariant()
                : Champions + " - " + (nationName ?? "").ToUpperInvariant() + " - " + playerName;

        // ---- buttons -------------------------------------------------------------------------
        public const string SimulateToEnd = "Simulate to end";
        public const string NewCup = "New Cup";
        public const string MainMenu = "Main Menu";
        public const string PlayAgain = "Play Again";
        public const string EndMatch = "End Match";
        public const string QuitToMenu = "Quit to Menu";
        public const string ViewBracket = "View Bracket";
        public const string Customize = "Customize";
        public const string Spectate = "Spectate";
        public const string Continue = "Continue";
        public const string ListInFind = "List in Find a Session";
        public const string PlayStyle = "Play style";
        public const string Format = "Format";

        // ---- confirms ------------------------------------------------------------------------
        public const string ConfirmQuitTitle = "Quit the cup?";
        public const string ConfirmQuitSolo = "Quit the cup? This ends it.";
        public const string ConfirmQuitSoloBody = "This ends it.";
        public const string ConfirmQuitHeadToHead = "Quit the cup? An AI plays your nation from here.";
        public const string ConfirmQuitHeadToHeadBody = "An AI plays your nation from here.";
        public const string ConfirmQuitCoopBody = "You are dropped from the order.";
        public const string ConfirmEndMatchTitle = "End the cup?";
        public const string ConfirmEndMatchBody = "Ends the cup for everyone.";

        // ---- achievements ---------------------------------------------------------------------
        public const string AchChampion = "Champion";
        public const string AchGiantKiller = "Giant Killer";
        public const string AchCleanSheet = "Clean Sheet";
        public const string AchColdBlooded = "Cold Blooded";
        public const string AchTeamPlayer = "Team Player";
        public const string AchPundit = "Pundit";
    }

    /// <summary>
    /// Logging for the pure cup layer: UnityEngine.Debug inside Unity, Console outside it, so the
    /// same files compile in the editor and in the console self-test project.
    /// </summary>
    public static class CupLog
    {
        public static void Info(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log("[Cup] " + msg);
#else
            Console.WriteLine("[Cup] " + msg);
#endif
        }

        public static void Warn(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogWarning("[Cup] " + msg);
#else
            Console.WriteLine("[Cup] WARNING: " + msg);
#endif
        }

        public static void Error(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError("[Cup] " + msg);
#else
            Console.Error.WriteLine("[Cup] ERROR: " + msg);
#endif
        }
    }
}
