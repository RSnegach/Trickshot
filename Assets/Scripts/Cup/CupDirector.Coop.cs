using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// CO-OP (design section 5): up to 8 humans are ONE nation. The lobby votes for the nation
    /// (majority, else CAPTAIN DECIDES); before every stage the Captain (the host) fills the
    /// shooting order (N-1 shooters + the keeper) by drag-and-drop or the slot-machine lever on
    /// <see cref="CupOrderUI"/>; every round is host-simulated (RoundAuthority.Host / Client)
    /// with the team in a lineup, shooters cycling through the order and the keeper keeping every
    /// opponent kick. Lost -> GAME OVER + results; won the Final -> the trophy lift, then the
    /// CHAMPIONS results.
    ///
    /// Authority. The HOST drives every phase change below (the Captain IS the host, so the
    /// order intents are host-local and ride CupState). A CLIENT runs the same partial with
    /// IsAuthority false: it only reacts to PHASE ENTRIES (the PhaseSerial latch - the model it
    /// reads was applied by CupDirector.Net before the entry fires), starts its own round under
    /// the loading card, runs its own coin-toss ceremony (the Captain's call reaches it through
    /// the CupState echo), and never SetPhase()s itself. Its round driver is a Client that
    /// mirrors the host's CupRoundState and snapshots; its Begin comes from the host's state.
    ///
    /// Phase map (every ENTRY runs once per SetPhase through the PhaseSerial latch, from the tick):
    ///   NationPick   NationPickerUI (Co-op variant: counters on the flags, changeable votes).
    ///                Host gate: AllPicked and MajorityReached -> DecideTeamNation; AllPicked with
    ///                no majority -> the screen's CAPTAIN DECIDES button (CaptainDecides()). Once
    ///                TeamNation >= 0 -> BuildBracket (one entrant, YOUR TEAM under the Captain's
    ///                slot), the career "entered" write, Bracket.
    ///   Bracket      CupBracketScreen for CupTuning.BracketScreenSeconds (the team's nation outlined
    ///                with every name), then the order screen. A client ends its previous round here.
    ///   OrderPick    CupOrderUI. The host installs the starting order (the last stage's, collapsed
    ///                for leavers, or an all-empty one), un-readies everyone; the Captain's
    ///                SetOrder / PullLever ride CupState; gate: CoopOrderComplete and AllReady and the
    ///                slot machine's reel finished -> Loading. A keeper who left last round is
    ///                flagged (<see cref="CoopKeeperLeft"/>) so the screen prompts the Captain.
    ///   Loading      The loading card; StartRound(the team's round) one frame later on EVERY peer
    ///                (host: Host authority, clients: Client), NotifyLoaded once the scene is
    ///                built; the host holds the card until LoadBarrierOpen (everyone loaded or 10 s)
    ///                and moves to CoinToss; a client keeps its card until that phase arrives.
    ///   CoinToss     BeginCoinToss on every peer: the Captain is the official caller, the whole team
    ///                calls as predictions, the calls band (6.12) after the flash; onDone on the host
    ///                -> EnsureCoinOutcome, the career coin write, Driver.Begin(), Round. A client only
    ///                writes its own coin call to its career.
    ///   Round        UpdateLiveRow every tick. Host, at Driver.Phase == Over: RecordResult, the
    ///                career round write; won the Final -> SimulateAiRounds, CupCareer.Won,
    ///                TrophyLift (NO EndRound: the lift runs on the round's bodies); won -> EndRound,
    ///                SimulateAiRounds, AdvanceStage, the stage career write, Bracket; lost ->
    ///                EndRound, SimulateAiRounds, GameOver. A client writes its career round once the
    ///                host's result has landed in its bracket and waits for the host's phase.
    ///   GameOver     CupResultsUI (GameOver): stage tabs + TOTAL, per-player columns; End Match /
    ///                Play Again (Captain; clients see "waiting for the captain").
    ///   TrophyLift   BeginTrophyLift() on every peer (a client's runs the camera and UI over its
    ///                puppets, which keep following the host's snapshots past Over); its Continue
    ///                (host) ends the lift and the round and leads to Results.
    ///   Results      CupResultsUI (Champions): the same two buttons.
    ///   Ended        The session dissolves (host EndMatch) or this client left; nothing ticks.
    ///
    /// Leavers (design 5 / 10): ApplyLeave (the shell) drops them from the order and collapses the
    /// slots; this partial sees the player's Left flag on EVERY peer (the host from the roster,
    /// a client from the CupState echo) and, while a round stands, tells the driver
    /// (CupRoundDriver.HumanLeft): the leaver's body is retired and a leaving keeper's gloves pass
    /// to the lowest-ordered shooter for the rest of the round; the next order screen prompts the
    /// Captain. The host leaving ends the session for everyone (the Captain is the host).
    ///
    /// Career (9.7): entered at the draw, the stage as each one opens, every own kick at its
    /// verdict (the shell's stats listener), the coin after each toss, the round at its end, the
    /// cup on a won Final (CupCareer.Won with the Co-op flag: "Team Player"). Per-player,
    /// per-stage results for the GAME OVER / CHAMPIONS table live in CupStatsLedger, attached
    /// here at every cup start so no screen has to be the one that remembers.
    /// </summary>
    public partial class CupDirector
    {
        /// <summary>After the last reel stops, the chips settle this long before the order is live again (the host's gate waits it out too).</summary>
        public const float CoopReelSettle = 0.35f;

        // The screens of the phase in progress (at most one of each; closed on every entry).
        NationPickerUI _coopPicker;
        CupBracketScreen _coopBracket;
        CupOrderUI _coopOrder;
        CupResultsUI _coopResults;

        int _coopSerial = -1;           // the PhaseSerial whose entry actions have run
        int _coopLoadShownFrame = -1;   // Loading: the frame the card was shown (the build waits one)
        bool _coopRoundStarted;         // Loading: StartRound has run for this stage's round
        bool _coopLoadedSent;           // Loading: NotifyLoaded sent for this round
        bool _coopTossDone;             // CoinToss: the ceremony's onDone landed
        bool _coopDrawFailed;           // NationPick: BuildBracket refused (logged once, not retried every frame)
        bool _coopCareerWritten;        // Round (client): the career round write happened once the result landed
        bool _coopEnteredWritten;       // Client: the career "entered" write for this cup
        bool _coopUnplayableLogged;     // Client: a missing driver was logged once for this round
        int _coopLeverSeen = -1;        // LeverPulls last seen (a change = a pull = the reel)
        float _coopLeverAt = -100f;     // when the last pull landed here (unscaled)
        int[] _coopOrderSeen = new int[0];   // the order as of the last tick (who kept before a leave collapsed it)
        readonly HashSet<int> _coopLeftSeen = new HashSet<int>();   // slots whose leave has been handled

        /// <summary>
        /// Co-op: the keeper left during the last round (his gloves passed to the lowest-ordered
        /// shooter for the rest of it). The order screen prompts the Captain to pick a new keeper
        /// (design 5); cleared once the next round starts.
        /// </summary>
        public bool CoopKeeperLeft { get; private set; }

        /// <summary>
        /// Co-op: the order is COMPLETE - one entry per active player, every entry an active
        /// player's slot, none empty (-1) and none twice; index 0 is the keeper. The OrderPick gate
        /// and MakeRoundSetup insist on it; the screen's Ready enables on it.
        /// </summary>
        public bool CoopOrderComplete
        {
            get
            {
                var team = ActiveSlots();
                var order = CoopOrder;
                if (team.Count == 0 || order == null || order.Length != team.Count) return false;
                for (int i = 0; i < order.Length; i++)
                {
                    int v = order[i];
                    if (v < 0 || !team.Contains(v)) return false;
                    for (int j = 0; j < i; j++) if (order[j] == v) return false;
                }
                return true;
            }
        }

        /// <summary>How long the slot machine runs for a team of `slots`: the lever arc, the spin, the staggered stops, the settle (design 6.8).</summary>
        public static float CoopReelSeconds(int slots)
            => CupTuning.LeverSeconds + CupTuning.ReelSpinSeconds + CupTuning.ReelStopGap * Mathf.Max(0, slots - 1) + CoopReelSettle;

        /// <summary>Co-op: the slot machine's reel is still turning on this machine (the order is installed, the faces have not all landed).</summary>
        public bool CoopReelSpinning => Time.unscaledTime - _coopLeverAt < CoopReelSeconds(ActiveCount);

        /// <summary>Called from Update every frame while Style == Coop.</summary>
        void CoopTick()
        {
            CoopWatchLeavers();
            CoopWatchLever();

            if (_coopSerial != PhaseSerial)
            {
                _coopSerial = PhaseSerial;
                CoopEnter(Phase);
            }

            bool host = IsAuthority;
            switch (Phase)
            {
                case CupPhase.NationPick:
                    if (!host) break;
                    // The vote gate (design 5.2): everyone has picked and one nation holds a
                    // majority. No majority once everyone has picked = the screen's CAPTAIN
                    // DECIDES button (CaptainDecides()); either way TeamNation lands here.
                    if (TeamNation < 0 && AllPicked && MajorityReached)
                    {
                        int votes;
                        int nation = MajorityNation(out votes);
                        if (nation >= 0) DecideTeamNation(nation);
                    }
                    if (TeamNation >= 0 && Bracket == null && !_coopDrawFailed)
                    {
                        if (BuildBracket())
                        {
                            CoopCupEntered();
                            SetPhase(CupPhase.Bracket);
                        }
                        else
                        {
                            // Logged by BuildBracket. Sit here rather than spin: the Captain can
                            // still End Match; the picks cannot change once a team nation is set.
                            _coopDrawFailed = true;
                        }
                    }
                    break;

                case CupPhase.Bracket:
                    // 5 s, no button (design 2.7); the host's timer is authoritative (the phase
                    // change is a CupState), clients only animate the bar.
                    if (host && PhaseTime >= CupTuning.BracketScreenSeconds) CoopEnterOrderPick();
                    break;

                case CupPhase.OrderPick:
                    // Gate (design 6.8): all slots filled and all ready - and never while the
                    // slot machine's reels still turn (a Ready that stood before the pull would
                    // otherwise cut the animation short on every screen).
                    if (host && CoopOrderComplete && AllReady && !CoopReelSpinning)
                    {
                        ClearLoaded();
                        SetPhase(CupPhase.Loading);
                    }
                    break;

                case CupPhase.Loading:
                    CoopTickLoading();
                    break;

                case CupPhase.CoinToss:
                    CoopTickCoinToss();
                    break;

                case CupPhase.Round:
                    CoopTickRound();
                    break;

                case CupPhase.GameOver:
                case CupPhase.TrophyLift:
                case CupPhase.Results:
                    // The results screens and the lift drive themselves through the intents
                    // (EndMatch / PlayAgain / ContinueFromResults, host-only where it matters).
                    break;

                case CupPhase.StageComplete:
                case CupPhase.Lobby:
                case CupPhase.Interstitial:
                case CupPhase.Podium:
                    // Never visited in Co-op (no lobby between stages: bracket then the order
                    // screen; the ending is the trophy lift). A stray host state naming one of
                    // them is simply waited out.
                    break;

                case CupPhase.Ended:
                    break;
            }

            CoopRememberOrder();
        }

        // ==========================================================================================
        // Phase entries
        // ==========================================================================================

        void CoopEnter(CupPhase phase)
        {
            CloseCoopScreens();
            switch (phase)
            {
                case CupPhase.NationPick:
                    _coopDrawFailed = false;
                    _coopEnteredWritten = false;
                    CoopKeeperLeft = false;
                    // The results ledger has to be listening from the first whistle of the cup
                    // (Play Again lands here too); the picker attaches it as well - idempotent.
                    CupStatsLedger.Attach(this);
                    MenuBackdrop();
                    _coopPicker = NationPickerUI.Create(MatchRoot, this, CupStyle.Coop);
                    break;

                case CupPhase.Bracket:
                    // A client's round of the previous stage ends here (the host ended its own
                    // before moving on); harmless when nothing stands. Its career round write
                    // comes first: the host's result and its next phase ride ONE CupState (the
                    // coalescer flushes on the phase edge), so this entry can be the first tick
                    // that sees the round Done - the Round tick never gets another look.
                    CoopClientRoundConcluded();
                    if (Driver != null) EndRound();
                    if (!IsAuthority) CoopClientStageOpened();
                    MenuBackdrop();
                    _coopBracket = CupBracketScreen.Create(MatchRoot, this);
                    break;

                case CupPhase.OrderPick:
                    MenuBackdrop();
                    _coopOrder = CupOrderUI.Create(MatchRoot, this);
                    break;

                case CupPhase.Loading:
                    CoopEnterLoading();
                    break;

                case CupPhase.CoinToss:
                    _coopTossDone = false;
                    // A client whose card is still up drops it now (the host's barrier opened);
                    // a late loader starts its round here rather than never.
                    if (!IsAuthority)
                    {
                        CoopEnsureRoundStarted();
                        if (Loading != null && Loading.Visible && !Loading.HideRequested) Loading.Hide();
                    }
                    if (Driver == null || !Driver.Configured) { CoopRoundUnplayable(); break; }
                    BeginCoinToss(() => _coopTossDone = true);
                    break;

                case CupPhase.Round:
                    _coopCareerWritten = false;
                    _coopUnplayableLogged = false;
                    if (!IsAuthority)
                    {
                        CoopEnsureRoundStarted();
                        if (Loading != null && Loading.Visible) Loading.HideImmediate();
                    }
                    break;

                case CupPhase.GameOver:
                    CoopClientRoundConcluded();   // see the Bracket entry
                    if (Driver != null) EndRound();
                    if (Loading != null) Loading.HideImmediate();
                    MenuBackdrop();
                    _coopResults = CupResultsUI.Create(MatchRoot, this, CupResultsMode.GameOver);
                    break;

                case CupPhase.TrophyLift:
                    // The lift runs on the ROUND's bodies (design 8.2): no EndRound before it. A
                    // client's puppets keep following the host's snapshots for the whole cinematic
                    // and the free window (the host's driver publishes past Over).
                    CoopClientRoundConcluded();   // the won Final's career write (see the Bracket entry)
                    if (!IsAuthority) CoopEnsureRoundStarted();
                    if (Loading != null) Loading.HideImmediate();
                    if (!BeginTrophyLift())
                    {
                        // Nothing to lift with (logged): the fanfare it would have played, then
                        // the results it leads to. A client waits for the host's Results.
                        EndRound();
                        AudioManager.Instance?.PlayFanfare();
                        if (IsAuthority) SetPhase(CupPhase.Results);
                    }
                    break;

                case CupPhase.Results:
                    CoopClientRoundConcluded();   // a Final that skipped the lift (see the Bracket entry)
                    EndTrophyLift();
                    if (Driver != null) EndRound();
                    if (Loading != null) Loading.HideImmediate();
                    MenuBackdrop();
                    _coopResults = CupResultsUI.Create(MatchRoot, this, CupResultsMode.Champions);
                    break;

                case CupPhase.Ended:
                    if (Loading != null) Loading.HideImmediate();
                    break;
            }
        }

        /// <summary>The draw is made (host): the career "entered" write for the team's nation, the first stage.</summary>
        void CoopCupEntered()
        {
            if (TeamNation >= 0) CupCareer.Entered(TeamNation, Style);
            CupCareer.StageReached(Stage, Style);
        }

        /// <summary>
        /// Client: the career write for the round standing here, once its result has landed in
        /// this bracket (the host's CupState). Called from the Round tick and from every entry
        /// that ends the round, because the result and the phase that follows it arrive together;
        /// the latch makes the two paths one write. A no-op on the host (CoopFinishRound writes).
        /// </summary>
        void CoopClientRoundConcluded()
        {
            if (IsAuthority || _coopCareerWritten) return;
            var r = CurrentRound;
            if (r == null || !r.Done) return;
            _coopCareerWritten = true;
            RecordLocalRoundCareer(r);
        }

        /// <summary>A client's stage opened (the host's Bracket phase): the career "entered" write on the first one, the stage on every one.</summary>
        void CoopClientStageOpened()
        {
            if (Bracket == null) return;
            if (!_coopEnteredWritten)
            {
                _coopEnteredWritten = true;
                if (TeamNation >= 0) CupCareer.Entered(TeamNation, Style);
            }
            CupCareer.StageReached(Stage, Style);
        }

        /// <summary>
        /// Host: the bracket screen is done - open the order screen. The starting order is the
        /// last stage's when it still fits the team (a leaver's slot already collapsed, so a
        /// returning team is one Ready away), else one empty slot per player for the Captain to
        /// fill. Everyone is un-readied: a Ready is for THIS stage's order.
        /// </summary>
        void CoopEnterOrderPick()
        {
            var team = ActiveSlots();
            int[] initial;
            if (CoopOrderComplete) initial = (int[])CoopOrder.Clone();
            else
            {
                initial = new int[team.Count];
                for (int i = 0; i < initial.Length; i++) initial[i] = -1;
            }
            ApplyOrder(initial);
            ClearReady();
            SetPhase(CupPhase.OrderPick);
        }

        void CoopEnterLoading()
        {
            _coopRoundStarted = false;
            _coopLoadedSent = false;
            _coopUnplayableLogged = false;
            _coopLoadShownFrame = Time.frameCount;
            var round = CoopRound();
            if (Loading != null)
            {
                int a = round != null ? NationOfEntrant(round.EntrantA) : -1;
                int b = round != null ? NationOfEntrant(round.EntrantB) : -1;
                Loading.Show(Stage, a, b, CupTuning.LoadingMinSeconds);
            }
        }

        /// <summary>The team's round this stage: the one the host is running (CupState) or, before that arrives, the bracket's (both name the same round in Co-op).</summary>
        CupRound CoopRound() => HostRound ?? LocalRoundThisStage;

        void CoopTickLoading()
        {
            if (!_coopRoundStarted)
            {
                // One rendered frame after Show: the card is on screen before the build hitch,
                // so the bodies never pop in (design 6.4).
                if (Time.frameCount <= _coopLoadShownFrame) return;
                CoopStartRound();
                return;
            }
            if (Driver == null) { CoopRoundUnplayable(); return; }
            if (!_coopLoadedSent && Driver.SceneBuilt)
            {
                _coopLoadedSent = true;
                NotifyLoaded();   // the barrier ack (design 6.4); local on the host
            }
            if (Loading != null && Loading.Visible)
            {
                // The host holds the card until every peer has acked or the barrier times out,
                // then lets it fade; a client keeps its card until the host's CoinToss arrives
                // (the CoinToss entry hides it), so nobody watches an empty pitch while others load.
                if (IsAuthority && !Loading.HideRequested && Driver.SceneBuilt && Loading.MinElapsed && LoadBarrierOpen) Loading.Hide();
                return;
            }
            if (IsAuthority) SetPhase(CupPhase.CoinToss);
        }

        /// <summary>StartRound the team's round on this peer (Host / Client per AuthorityFor); once per stage entry.</summary>
        void CoopStartRound()
        {
            _coopRoundStarted = true;
            CoopKeeperLeft = false;   // the prompt was shown on the order screen; the new round has its own order
            var round = CoopRound();
            var drv = round != null ? StartRound(round) : null;
            if (drv == null || !drv.Configured) CoopRoundUnplayable();
        }

        /// <summary>A client that reached the toss / the round / the lift without a round of its own (a slow loader, a late CupState) starts it now.</summary>
        void CoopEnsureRoundStarted()
        {
            if (IsAuthority) return;
            if (Driver != null && Driver.Configured) return;
            var round = CoopRound();
            if (round == null || !round.Ready) return;
            if (_coopRoundStarted && Driver == null && _coopUnplayableLogged) return;   // already tried and failed this stage
            CupLog.Info("Co-op: starting the round late (" + CupStages.Short(round.Stage) + " #" + round.Index + ")");
            CoopStartRound();
            _coopLoadedSent = Driver != null && Driver.SceneBuilt;
            if (_coopLoadedSent) NotifyLoaded();
        }

        void CoopTickCoinToss()
        {
            if (!CoopConsumeToss()) return;
            if (!IsAuthority) return;   // the host's Begin reaches this driver through CupRoundState
            Driver.Begin();   // Intro: everyone re-parked, the card, the cursor captured
            SetPhase(CupPhase.Round);
        }

        /// <summary>
        /// The ceremony's onDone landed: the outcome made definite on the driver and the local
        /// call banked to the career. True once per toss. A client's ceremony starts later than
        /// the host's (the state's round trip) and may still be on its calls band when the host's
        /// Round phase arrives, so the Round tick consumes it too - the coin write is never lost.
        /// </summary>
        bool CoopConsumeToss()
        {
            if (!_coopTossDone) return false;
            _coopTossDone = false;
            if (Driver == null || !Driver.Configured) { CoopRoundUnplayable(); return false; }
            // The ceremony recorded the outcome on the driver as the flip started; this only
            // fills in when no ceremony ran (no scene, or cut short). On a client the host's
            // CupRoundState rewrites the line anyway; its own career coin write is what matters.
            EnsureCoinOutcome();
            RecordLocalCoinCall();
            return true;
        }

        void CoopTickRound()
        {
            if (!IsAuthority) CoopConsumeToss();
            if (Driver == null) { CoopRoundUnplayable(); return; }
            UpdateLiveRow();
            if (Driver.Phase != RoundPhase.Over) return;
            if (!IsAuthority)
            {
                // The host's result lands in this bracket through CupState; the career round
                // write waits for it, and the host's next phase decides what follows.
                CoopClientRoundConcluded();
                return;
            }
            CoopFinishRound();
        }

        /// <summary>
        /// Host: the team's round is over. The result into the bracket, the career, then: a won
        /// Final keeps the round standing for the trophy lift; any other result tears the round
        /// down, resolves the stage's AI rounds and leads to the next stage or GAME OVER.
        /// </summary>
        void CoopFinishRound()
        {
            var round = CurrentRound;
            var line = Driver != null ? Driver.Line : null;
            if (round == null)
            {
                CupLog.Error("Co-op: round over with no CurrentRound");
                EndRound();
                SetPhase(CupPhase.GameOver);
                return;
            }
            if (!RecordResult(round, line))
            {
                // Only reachable after an Abort (an undecided line): the round cannot be replayed
                // and the bracket cannot hold a hole, so the sim settles it. Logged by RecordResult.
                CupLog.Warn("Co-op: the played round had no decided line - simulating it");
                if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                RefreshPlayersFromBracket();
                Notify();
            }
            RecordLocalRoundCareer(round);

            bool won = round.Done && round.WinnerEntrant == LocalEntrant;
            if (won && CupStages.IsLast(Stage))
            {
                // Won the Final (design 8.2): the trophy lift runs on the round's bodies, so the
                // round stays up - BeginTrophyLift in the TrophyLift entry, EndRound from its Continue.
                SimulateAiRounds(Stage);   // none at the Final; kept for symmetry with every other stage
                CupCareer.Won(TeamNation, Style);
                SetPhase(CupPhase.TrophyLift);
                return;
            }

            EndRound();
            SimulateAiRounds(Stage);
            if (!won)
            {
                SetPhase(CupPhase.GameOver);
                return;
            }
            if (!AdvanceStage())
            {
                // The stage is not complete (a simulated AI round is missing): finish it rather
                // than strand the team; a second refusal is a bracket bug worth a loud log.
                CupLog.Warn("Co-op: AdvanceStage refused at " + CupStages.Short(Stage) + " - simulating the missing rounds");
                SimulateAiRounds(Stage);
                if (!AdvanceStage())
                {
                    CupLog.Error("Co-op: the " + CupStages.Name(Stage) + " cannot advance - ending the cup");
                    SetPhase(CupPhase.GameOver);
                    return;
                }
            }
            CupCareer.StageReached(Stage, Style);
            SetPhase(CupPhase.Bracket);
        }

        /// <summary>
        /// No driver where the flow expected one. Host: never a soft lock - the round is settled
        /// by the sim and the flow moves on as if it had been played, with a log line to find.
        /// Client: logged once; the host's phases keep arriving and the screens still draw.
        /// </summary>
        void CoopRoundUnplayable()
        {
            if (!IsAuthority)
            {
                if (!_coopUnplayableLogged)
                {
                    _coopUnplayableLogged = true;
                    CupLog.Error("Co-op: no round to play on this peer at " + CupStages.Short(Stage) + " - following the host's phases without one");
                }
                return;
            }
            CupLog.Error("Co-op: the team's round at " + CupStages.Short(Stage) + " could not be played - simulating it");
            EndRound();
            if (Loading != null) Loading.HideImmediate();
            var round = LocalRoundThisStage;
            if (Bracket == null || round == null || !round.Ready)
            {
                SetPhase(CupPhase.GameOver);
                return;
            }
            if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
            RefreshPlayersFromBracket();
            SimulateAiRounds(Stage);
            Notify();
            bool won = round.WinnerEntrant == LocalEntrant;
            if (!won) { SetPhase(CupPhase.GameOver); return; }
            if (CupStages.IsLast(Stage))
            {
                // No bodies to lift with: straight to the CHAMPIONS results.
                CupCareer.Won(TeamNation, Style);
                SetPhase(CupPhase.Results);
                return;
            }
            if (AdvanceStage()) { CupCareer.StageReached(Stage, Style); SetPhase(CupPhase.Bracket); }
            else SetPhase(CupPhase.GameOver);
        }

        // ==========================================================================================
        // Leavers and the lever (watched on every peer)
        // ==========================================================================================

        /// <summary>
        /// A player whose Left flag just rose (the host set it in ApplyLeave; a client got it in
        /// CupState): while a round stands the driver retires the body and hands a keeper's
        /// gloves on; a keeper's leave is remembered for the next order screen's prompt.
        /// </summary>
        void CoopWatchLeavers()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.Left || _coopLeftSeen.Contains(p.Slot)) continue;
                _coopLeftSeen.Add(p.Slot);
                CoopPlayerLeft(p.Slot);
            }
        }

        void CoopPlayerLeft(int slot)
        {
            bool wasKeeper = _coopOrderSeen.Length > 0 && _coopOrderSeen[0] == slot;
            var drv = Driver;
            if (drv != null && drv.Configured && drv.Setup != null)
            {
                if (drv.Setup.CoopKeeperSlot == slot) wasKeeper = true;
                drv.HumanLeft(slot);
            }
            if (wasKeeper && slot != CaptainSlot) CoopKeeperLeft = true;
            var p = PlayerAt(slot);
            CupLog.Info("Co-op: " + (p != null ? p.Name : "slot " + slot) + " left" + (wasKeeper ? " (the keeper)" : "") + "; " + ActiveCount + " remain");
            if (IsAuthority) Notify();
        }

        /// <summary>The lever: a change of LeverPulls (to a non-zero count) is a pull - the order is already installed, the reels start now.</summary>
        void CoopWatchLever()
        {
            int pulls = LeverPulls;
            if (pulls == _coopLeverSeen) return;
            if (_coopLeverSeen >= 0 && pulls > 0 && Phase == CupPhase.OrderPick) _coopLeverAt = Time.unscaledTime;
            _coopLeverSeen = pulls;
        }

        void CoopRememberOrder()
        {
            var o = CoopOrder;
            if (o == null) { _coopOrderSeen = new int[0]; return; }
            if (_coopOrderSeen.Length != o.Length) _coopOrderSeen = new int[o.Length];
            Array.Copy(o, _coopOrderSeen, o.Length);
        }

        /// <summary>Close whatever screen the previous phase left (each Close destroys its object and unhooks).</summary>
        void CloseCoopScreens()
        {
            if (_coopPicker != null) { _coopPicker.Close(); _coopPicker = null; }
            if (_coopBracket != null) { _coopBracket.Close(); _coopBracket = null; }
            if (_coopOrder != null) { _coopOrder.Close(); _coopOrder = null; }
            if (_coopResults != null) { _coopResults.Close(); _coopResults = null; }
            EndPodium();   // never used in Co-op; kept symmetrical with the other styles' close paths
        }
    }
}
