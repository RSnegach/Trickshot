using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// HEAD TO HEAD (design section 4, 6.3): up to 8 humans share one bracket, each on their own
    /// nation. Every human-vs-AI round of a stage is played AT ONCE on its owner's machine
    /// (RoundAuthority.Local: the owner simulates, reports the result to the host with
    /// CupRequest.RoundResult and streams its view while somebody spectates it); when two humans
    /// are drawn together that round is played ONE AT A TIME on the host (Host / Client authority:
    /// the host simulates, both participants control, everyone else watches the host's snapshots
    /// through a Client driver and mirrors a participant's camera). The host is the cup authority:
    /// it owns the players, the bracket results, the phase and the gates; CupDirector.Net turns
    /// every StateChanged into a CupState and every client intent into a CupRequest, so this file
    /// never touches the session - it calls intents, reads the model, and on the host moves the
    /// phase. A CLIENT runs the same partial with IsAuthority false: it is driven by PHASE
    /// ENTRIES (the PhaseSerial latch, fired from the tick once the host's model has been applied)
    /// and never SetPhase()s itself.
    ///
    /// Phase map (the host's SetPhase calls; every peer's entry actions):
    ///   NationPick   NationPickerUI (the strip variant): picks arrive as ApplyPick on the host
    ///                (distinct nations, first request wins; a refused race never echoes, the
    ///                picker's card snaps back). Host: AllPicked -> BuildBracket -> Bracket.
    ///   Bracket      CupBracketScreen, CupTuning.BracketScreenSeconds on the host's timer; the
    ///                career "entered" / "stage reached" writes; then the stage's first WAVE.
    ///   Loading      A wave: every peer with a pending human-vs-AI round this stage StartRound()s
    ///                it locally under the loading card and NotifyLoaded()s once built; a peer with
    ///                no round (drawn against a human, eliminated) waits in the cup lobby and acks
    ///                at once. Host: LoadBarrierOpen (AllLoaded or the 10 s timeout) -> Round.
    ///                For a HOST ROUND (two humans): the host StartRound()ed before this phase, so
    ///                CupState carries the round; every other peer StartRound(HostRound)s it as a
    ///                Client (participants get bodies and control, the rest puppets to watch) and
    ///                acks; host: LoadBarrierOpen -> CoinToss.
    ///   CoinToss     Host rounds only: the ceremony on every peer (the official caller is the
    ///                seeded side's human; everyone present, spectators included, calls as a
    ///                prediction); host: onDone -> Driver.Begin() -> Round. A parallel wave never
    ///                visits this phase: each owner runs its own toss under Round (the coin-call
    ///                gates accept a call while a ceremony is open - see CallCoin).
    ///   Round        A wave: each owner runs its own toss -> Begin -> play; at Over the owner
    ///                RecordResult()s locally, ReportRoundResult()s (a no-op on the host), writes
    ///                its career and returns to the lobby while the others finish (live rows from
    ///                CupState, Spectate on a playing row). Host: every wave round in -> Lobby, or
    ///                straight to the Interstitial / Podium. A host round: the host feeds inputs,
    ///                publishes snapshots and keeps both participants' rows live; at Over it
    ///                records the result; clients mirror and, at Over, close their round.
    ///   Lobby        CupLobbyUI: rows, Spectate, View Bracket, Ready (auto for the eliminated),
    ///                Quit. Host: AI rounds simulated (a leaver's too), a late wave when a leaver
    ///                turned a human round into a human-vs-AI one, the next human-vs-human round
    ///                (Interstitial), nobody alive -> simulate to the end -> Podium; stage complete
    ///                AND AllReady -> AdvanceStage -> Bracket; the Final done -> Podium.
    ///   Interstitial "HEAD TO HEAD - up next" for the two participants (the others keep the lobby
    ///                with its "Head to head next: A vs B" gate line); host: after the beat
    ///                StartRound(next) then Loading.
    ///   Podium       CupPodium on every peer (spawned identically from the bracket + players; the
    ///                champion's emote rides the snapshot channel): host Play Again / Continue /
    ///                End Match, client End Match + "waiting for the host".
    ///   Results      The CUP SUMMARY (CupResultsUI.Summary): End Match / Play Again.
    ///   Ended        Everything closed; the host is tearing the match down, a client leaves on the
    ///                next tick (CupDirector.Net).
    ///
    /// Leavers (host): ApplyLeave ran (RosterChanged / CupRequest.Quit): the nation is AI from
    /// here (the bracket marks it, its later rounds are simulated by SimulateAiRounds, the row
    /// reads "Alice (AI)"). A leaver mid-HOST-ROUND keeps playing through the AI: the driver hands
    /// their bodies to CupBotTaker / Goalkeeper (CupRoundDriver.HandSlotToAi), never a walkover.
    /// A leaver mid-PARALLEL-round: their round is now AI-vs-AI and is simulated. A leaver whose
    /// pending human-vs-human round became human-vs-AI hands the other human a late wave.
    ///
    /// Coin calls: everyone present calls; the official call decides kick-off; Head to Head shows
    /// no verdict and no band (the ceremony does that per style); the local player's call goes
    /// to the career (CupCareer.CoinCalled) here, from the ceremony's own pick, and the host judges
    /// each owner's parallel call against THAT owner's round (ResolveCoinCalls' Head to Head scope +
    /// H2HResolveParallelCalls) for the summary's tally.
    /// </summary>
    public partial class CupDirector
    {
        // ---- local tunables (feel; the designed beats are in CupTuning) ------------------------
        /// <summary>The "HEAD TO HEAD - up next" card the two participants see before their round (s).</summary>
        public const float HeadToHeadInterstitialSeconds = 3f;
        /// <summary>
        /// Host: a wave round whose owner stopped playing without a result arriving is simulated
        /// after this (s). It covers the SILENT case only - a crash, or a peer that went quiet
        /// short of the roster noticing. A result the host actually received and REFUSED needs no
        /// wait at all: H2HRoundResultRefused settles that round on the next watchdog pass, so the
        /// reporting player is not left staring at a finished round for ten seconds.
        /// </summary>
        public const float HeadToHeadResultGrace = 10f;
        /// <summary>Host: a wave still unfinished after this long is settled by the sim (s). Longer than any real round.</summary>
        public const float HeadToHeadWaveCap = 480f;
        /// <summary>A refused draw (BuildBracket false) is retried this often rather than every frame.</summary>
        const float H2HDrawRetry = 2f;

        /// <summary>This peer's own sub-flow inside a wave (Loading / Round on the host's clock).</summary>
        enum H2HLocal : byte { None, Loading, Toss, Playing, Done }

        // ---- screens (at most one of each; closed on the next entry) ----------------------------
        NationPickerUI _h2hPicker;
        CupBracketScreen _h2hBracket;
        CupLobbyUI _h2hLobby;
        CupResultsUI _h2hResults;
        Action _h2hCardDraw, _h2hWatchDraw;
        bool _h2hCardHooked, _h2hWatchHooked;
        static GUIStyle _h2hCardTitle, _h2hCardStage, _h2hCardName, _h2hCardCode, _h2hCardVs, _h2hWatchBtn;

        // ---- latches ------------------------------------------------------------------------------
        int _h2hSerial = -1;           // the PhaseSerial whose entry actions have run
        float _h2hDrawRetryAt;         // NationPick: the next BuildBracket attempt after a refusal
        uint _h2hEnteredSeed;          // CupCareer.Entered written for this seed
        uint _h2hStageSeed;            // CupCareer.StageReached written for (seed, stage)
        int _h2hStageWritten = -1;
        uint _h2hWonSeed;              // CupCareer.Won written for this seed
        bool _h2hWasPaused;

        // ---- the wave on this peer ---------------------------------------------------------------
        bool _h2hWavePrepared;         // Loading / CoinToss / Round entry ran PrepareWave for this wave
        bool _h2hHostRound;            // the round standing here is host-simulated (Host / Client authority)
        bool _h2hRoundStarted;         // the local round's StartRound has run (or failed) this wave
        bool _h2hLoadedSent;           // NotifyLoaded sent for this wave
        H2HLocal _h2hLocal;
        int _h2hLoadShownFrame;
        bool _h2hTossDone;
        CoinFace? _h2hTossPick;        // the ceremony's local pick, captured at its end for the career
        CupRoundDriver _h2hHooked;     // the driver whose PhaseChanged we listen to
        bool _h2hCamDirty;             // a bodiless watcher's camera must be re-asserted (a placement cut)
        CupRound _h2hNextRound;        // Interstitial: the round the card names

        // ---- host bookkeeping ----------------------------------------------------------------------
        readonly List<CupRound> _h2hWaveRounds = new List<CupRound>();
        float _h2hWaveStartedAt;
        readonly Dictionary<int, float> _h2hOwnerStoppedAt = new Dictionary<int, float>();
        readonly HashSet<int> _h2hOwnerSeenPlaying = new HashSet<int>();
        readonly HashSet<int> _h2hOwnerRefused = new HashSet<int>();
        readonly HashSet<int> _h2hLeftSeen = new HashSet<int>();

        // ==========================================================================================
        // Tick
        // ==========================================================================================

        /// <summary>Called from Update every frame while Style == HeadToHead (after NetTick applied any CupState).</summary>
        void HeadToHeadTick()
        {
            if (_h2hSerial != PhaseSerial)
            {
                _h2hSerial = PhaseSerial;
                HeadToHeadEnter(Phase);
            }
            H2HTickLeavers();
            H2HCursorAfterUnpause();

            switch (Phase)
            {
                case CupPhase.NationPick:
                    // Picking is the ready (design 4.3): the draw is made the moment everyone
                    // active has picked. Host only; a client sees the draw arrive in CupState.
                    if (IsAuthority && AllPicked && Bracket == null && Time.unscaledTime >= _h2hDrawRetryAt)
                    {
                        if (BuildBracket()) SetPhase(CupPhase.Bracket);
                        else _h2hDrawRetryAt = Time.unscaledTime + H2HDrawRetry;   // logged by BuildBracket; a pick may still change
                    }
                    break;

                case CupPhase.Bracket:
                    // 5 s, no button; the host's timer is authoritative (the bar reads PhaseTime).
                    if (IsAuthority && PhaseTime >= CupTuning.BracketScreenSeconds) H2HStartWave(true);
                    break;

                case CupPhase.Loading:
                    H2HTickLoading();
                    break;

                case CupPhase.CoinToss:
                    H2HTickCoinToss();
                    break;

                case CupPhase.Round:
                    H2HTickRound();
                    break;

                case CupPhase.Lobby:
                    if (IsAuthority) H2HHostAdvance(false);
                    H2HLobbyBackdrop();
                    break;

                case CupPhase.Interstitial:
                    H2HTickInterstitial();
                    break;

                case CupPhase.Podium:
                case CupPhase.Results:
                    // The podium and the summary drive themselves through the intents (host:
                    // Play Again / Continue / End Match; client: End Match); the phase rides CupState.
                    break;

                case CupPhase.StageComplete:
                case CupPhase.OrderPick:
                case CupPhase.TrophyLift:
                case CupPhase.GameOver:
                    // Never visited in Head to Head (the lobby is the between-stages screen; the
                    // eliminated spectate; the ending is the podium).
                    break;

                case CupPhase.Ended:
                    break;
            }
        }

        // ==========================================================================================
        // Phase entries (once per SetPhase, from the tick, with the host's model already applied)
        // ==========================================================================================

        void HeadToHeadEnter(CupPhase phase)
        {
            bool wave = phase == CupPhase.Loading || phase == CupPhase.CoinToss || phase == CupPhase.Round;
            // The lobby survives into a wave (a peer without a round keeps it) and the interstitial
            // (the non-participants keep it); every other entry starts from a clean screen.
            CloseH2HScreens(keepLobby: wave || phase == CupPhase.Lobby || phase == CupPhase.Interstitial);
            // A LOADING entry always OPENS a wave (the only two SetPhase(Loading) calls in this
            // flow are H2HStartWave and H2HTickInterstitial), so the per-wave latches have to be
            // dropped there as well as on a non-wave entry: a LATE wave (design 10, a leaver's
            // pending human-vs-human round became human-vs-AI) is opened from H2HHostAdvance while
            // the Phase is still Round, and clearing only on a non-wave entry left the latch true
            // from the previous wave - H2HPrepareWave was skipped on every peer, so the owner never
            // StartRound()ed, nobody re-acked the barrier (_h2hLoadedSent was still set) and the
            // wave sat until HeadToHeadWaveCap. CoinToss / Round keep the latch: they are entered
            // MID-wave and must not re-prepare it.
            if (!wave || phase == CupPhase.Loading) _h2hWavePrepared = false;

            switch (phase)
            {
                case CupPhase.NationPick:
                    H2HEndAnyRound();
                    _h2hDrawRetryAt = 0f;
                    MenuBackdrop();
                    _h2hPicker = NationPickerUI.Create(MatchRoot, this, CupStyle.HeadToHead);   // no Back: the lobby committed
                    break;

                case CupPhase.Bracket:
                    H2HEndAnyRound();
                    CloseLobby();
                    H2HCareerOnDraw();
                    MenuBackdrop();
                    // Create() shows director.Stage, "THE DRAW" for the Round of 32 only, and reads
                    // PhaseTime for its bar - the host's value on a client, so every bar agrees.
                    _h2hBracket = CupBracketScreen.Create(MatchRoot, this);
                    break;

                case CupPhase.Loading:
                    if (!_h2hWavePrepared) H2HPrepareWave();
                    break;

                case CupPhase.CoinToss:
                    // Host rounds only. A peer that bound late lands here without a Loading entry.
                    if (!_h2hWavePrepared) H2HPrepareWave();
                    _h2hTossDone = false;
                    if (Loading != null) Loading.Hide();   // the barrier opened; Hide waits for its own minimum
                    break;

                case CupPhase.Round:
                    if (!_h2hWavePrepared) H2HPrepareWave();
                    if (_h2hHostRound && Loading != null) Loading.Hide();
                    break;

                case CupPhase.Lobby:
                    H2HEndAnyRound();
                    EnsureLobby();
                    break;

                case CupPhase.Interstitial:
                    H2HEndAnyRound();
                    H2HEnterInterstitial();
                    break;

                case CupPhase.Podium:
                    // Exactly as Solo enters it: the Final's result is in the bracket, the round's
                    // bodies are gone, the podium spawns its own from the bracket + players. On
                    // every peer, since bodies are seeded; only the champion's emote crosses the
                    // wire (CupDirector.Net). Its buttons already branch on Style / IsAuthority.
                    H2HEndAnyRound();
                    CloseLobby();
                    if (!TryBeginPodium())
                    {
                        // No champion to crown (logged by the podium): the fanfare it would have
                        // played; the host leads on to the summary, a client waits for the echo.
                        AudioManager.Instance?.PlayFanfare();
                        if (IsAuthority) SetPhase(CupPhase.Results);
                    }
                    break;

                case CupPhase.Results:
                    H2HEndAnyRound();
                    CloseLobby();
                    MenuBackdrop();
                    _h2hResults = CupResultsUI.Create(MatchRoot, this, CupResultsMode.Summary);
                    break;

                case CupPhase.Ended:
                    CloseLobby();
                    H2HEndAnyRound();
                    if (Loading != null) Loading.HideImmediate();
                    break;
            }
        }

        // ==========================================================================================
        // The draw and the career writes tied to it
        // ==========================================================================================

        /// <summary>The Bracket entry: "entered" once per cup, "stage reached" once per stage the local player is alive for.</summary>
        void H2HCareerOnDraw()
        {
            var me = LocalPlayer;
            if (Bracket == null || me == null) return;
            if (_h2hEnteredSeed != Seed && me.Nation >= 0)
            {
                _h2hEnteredSeed = Seed;
                CupCareer.Entered(me.Nation, Style);
            }
            if (me.Alive && (_h2hStageSeed != Seed || _h2hStageWritten != (int)Stage))
            {
                _h2hStageSeed = Seed;
                _h2hStageWritten = (int)Stage;
                CupCareer.StageReached(Stage, Style);
            }
        }

        /// <summary>A decided round the local player was in: the career round write, and the cup on a won Final.</summary>
        void H2HRoundConcluded(CupRound round)
        {
            if (round == null || !round.Done) return;
            RecordLocalRoundCareer(round);
            var me = LocalPlayer;
            if (me != null && LocalEntrant >= 0 && CupStages.IsLast(round.Stage) && round.WinnerEntrant == LocalEntrant && _h2hWonSeed != Seed)
            {
                _h2hWonSeed = Seed;
                CupCareer.Won(me.Nation, Style);
            }
        }

        // ==========================================================================================
        // Waves: the parallel phase (Loading -> Round) and the host round (Loading -> CoinToss -> Round)
        // ==========================================================================================

        /// <summary>
        /// Host: open a wave for every pending human-vs-AI round of the stage (design 4.5): the
        /// ready gate is reset for the stage's first wave (the pick's ready would advance the
        /// lobby at once), the loading acks are cleared for the barrier, and Loading goes out.
        /// The wave's rounds are remembered so the host knows when it is over.
        /// </summary>
        void H2HStartWave(bool firstOfStage)
        {
            if (!IsAuthority) return;
            if (firstOfStage) ClearReady();
            ClearLoaded();
            _h2hWaveRounds.Clear();
            H2HPendingParallelRounds(_h2hWaveRounds);
            _h2hWaveStartedAt = Time.unscaledTime;
            _h2hOwnerStoppedAt.Clear();
            _h2hOwnerSeenPlaying.Clear();
            _h2hOwnerRefused.Clear();
            if (_h2hWaveRounds.Count == 0)
            {
                // Nothing to play in parallel (every surviving human is drawn against another
                // human): straight to the head-to-head phase, or the lobby, with no empty wave.
                H2HHostAdvance(false);
                return;
            }
            SetPhase(CupPhase.Loading);
        }

        /// <summary>
        /// This peer's part in the wave the host just opened: a HOST ROUND (CupState carries it and
        /// it has two humans) is joined - the host already runs it, every other peer builds a
        /// Client driver: the two participants get bodies and control, everyone else puppets to
        /// watch through, with a participant's camera mirrored (Spectate) - or the peer's own
        /// pending human-vs-AI round is started locally under the loading card, or, with neither,
        /// the peer waits in the lobby and acks the barrier at once.
        /// </summary>
        void H2HPrepareWave()
        {
            _h2hWavePrepared = true;
            _h2hHostRound = false;
            _h2hRoundStarted = false;
            _h2hLoadedSent = false;
            _h2hTossDone = false;
            _h2hTossPick = null;
            _h2hLocal = H2HLocal.None;

            var hr = HostRound;
            bool hostRound = hr != null && hr.Ready && !hr.Done && AuthorityFor(hr) != RoundAuthority.Local;
            if (hostRound)
            {
                _h2hHostRound = true;
                CloseLobby();
                H2HShowLoading(hr);
                if (IsAuthority)
                {
                    // The host StartRound()ed before SetPhase(Loading) (H2HTickInterstitial); only
                    // a host that somehow lost it builds here.
                    if (Driver == null || CurrentRound != hr) H2HHookDriver(StartRound(hr));
                    else H2HHookDriver(Driver);
                }
                else
                {
                    // A participant's driver drives its own body from the host's state; a
                    // watcher's driver poses puppets. Both follow the host's snapshots.
                    H2HHookDriver(StartRound(hr));
                }
                _h2hRoundStarted = true;
                if (Driver != null && Driver.Configured && Driver.Setup != null && !Driver.Setup.LocalHasBody)
                {
                    // Watching: mirror a participant's camera from the start (design 4: spectators
                    // share the shooter's view); Esc frees the camera, the watch bar re-picks.
                    int watch = H2HDefaultWatchSlot(hr);
                    if (watch >= 0) Spectate(watch);
                    H2HHookWatchBar();
                }
                return;
            }

            var mine = H2HPendingLocalRound();
            if (mine != null)
            {
                if (Driver != null && CurrentRound == mine && Driver.Configured)
                {
                    // Already playing it (a wave re-opened under a running round): keep going.
                    _h2hRoundStarted = true;
                    _h2hLocal = Driver.Phase == RoundPhase.Idle ? H2HLocal.Loading : H2HLocal.Playing;
                    H2HHookDriver(Driver);
                    return;
                }
                CloseLobby();
                H2HShowLoading(mine);
                _h2hLoadShownFrame = Time.frameCount;
                _h2hLocal = H2HLocal.Loading;   // StartRound one rendered frame later (the card is up first)
                return;
            }

            // No round this wave: the lobby, live rows and Spectate; the barrier ack at once.
            EnsureLobby();
            H2HSendLoaded();
        }

        void H2HShowLoading(CupRound r)
        {
            if (Loading == null || r == null) return;
            if (Loading.Visible) return;
            Loading.Show(r.Stage, NationOfEntrant(r.EntrantA), NationOfEntrant(r.EntrantB), CupTuning.LoadingMinSeconds);
        }

        void H2HSendLoaded()
        {
            if (_h2hLoadedSent) return;
            _h2hLoadedSent = true;
            NotifyLoaded();
        }

        void H2HTickLoading()
        {
            if (_h2hHostRound)
            {
                if (Driver != null && Driver.Configured && Driver.SceneBuilt) H2HSendLoaded();
                else if (Driver == null && _h2hRoundStarted) H2HSendLoaded();   // nothing to build here
                if (IsAuthority)
                {
                    if (Driver == null || !Driver.Configured) { H2HHostRoundUnplayable(); return; }
                    if (_h2hLoadedSent && LoadBarrierOpen) SetPhase(CupPhase.CoinToss);
                }
                return;
            }
            H2HTickLocalFlow();
            if (_h2hLocal == H2HLocal.None || _h2hLocal == H2HLocal.Done || (Driver != null && Driver.SceneBuilt)) H2HSendLoaded();
            if (IsAuthority && _h2hLoadedSent && LoadBarrierOpen) SetPhase(CupPhase.Round);
        }

        /// <summary>The host round's coin toss (design 6.11 / 7.1): everyone present calls; the host Begin()s on its onDone.</summary>
        void H2HTickCoinToss()
        {
            if (!_h2hHostRound)
            {
                // A parallel wave never uses this phase; a host that got here anyway moves on.
                if (IsAuthority) SetPhase(CupPhase.Round);
                return;
            }
            if (Driver == null || !Driver.Configured)
            {
                if (IsAuthority) H2HHostRoundUnplayable();
                return;
            }
            if (Toss == null && !_h2hTossDone)
            {
                // A clean screen first: the call buttons under the fading card would be a wasted
                // second of the 5 s window.
                if (Loading != null && Loading.Visible) return;
                if (_h2hLocal != H2HLocal.Toss)
                {
                    _h2hLocal = H2HLocal.Toss;
                    H2HBeginToss(clearAll: IsAuthority);
                }
                if (!_h2hTossDone) return;
            }
            if (!_h2hTossDone) return;
            _h2hTossDone = false;
            if (IsAuthority)
            {
                // The ceremony recorded the outcome on the driver at the flip; this only fills in
                // when it was cut short. Begin -> Intro (bodies re-parked, the card, the cursor).
                EnsureCoinOutcome();
                H2HRecordLocalCoin();
                Driver.Begin();
                H2HSetHostRoundLive();
                _h2hLocal = H2HLocal.Playing;
                SetPhase(CupPhase.Round);
            }
            else
            {
                // The driver Begin()s from the host's CupRoundState (ApplyState moves it into Intro,
                // the driver shows the card and captures the cursor there); only the career here.
                H2HRecordLocalCoin();
                _h2hLocal = H2HLocal.Playing;
            }
        }

        void H2HTickRound()
        {
            if (_h2hHostRound)
            {
                if (Driver == null)
                {
                    if (IsAuthority) H2HHostRoundUnplayable();
                    return;
                }
                if (IsAuthority)
                {
                    H2HSetHostRoundLive();
                    H2HWatcherCamera();   // a host that is not a participant watches like a client
                    if (Driver.Phase == RoundPhase.Over) H2HFinishHostRound();
                }
                else
                {
                    // A ceremony still finishing after the host's Begin (its clock is its own):
                    // the career coin write lands whenever it ends.
                    if (_h2hTossDone) { _h2hTossDone = false; H2HRecordLocalCoin(); _h2hLocal = H2HLocal.Playing; }
                    H2HWatcherCamera();
                    if (Driver.Phase == RoundPhase.Over)
                    {
                        H2HClientRoundOver();
                        EnsureLobby();   // waiting for the host's next phase (lobby, interstitial, podium)
                    }
                }
                return;
            }

            // The parallel wave: this peer's own round, and on the host the wave's bookkeeping.
            H2HTickLocalFlow();
            if (IsAuthority) H2HHostAdvance(true);
            H2HLobbyBackdrop();
        }

        /// <summary>
        /// This peer's own human-vs-AI round inside a wave: the build one frame after the card,
        /// the card down once built and the barrier has opened, the toss (the local human calls;
        /// timeout HEADS), EnsureCoinOutcome + the career coin write, Begin, the live row every
        /// tick, and at Over the result into the bracket, to the host, the career, the teardown
        /// and the lobby.
        /// </summary>
        void H2HTickLocalFlow()
        {
            switch (_h2hLocal)
            {
                case H2HLocal.Loading:
                    if (!_h2hRoundStarted)
                    {
                        // One rendered frame after Show: the card is on screen before the build
                        // hitch, so the bodies never pop in (design 6.4).
                        if (Time.frameCount <= _h2hLoadShownFrame) return;
                        _h2hRoundStarted = true;
                        var mine = H2HPendingLocalRound();
                        var drv = mine != null ? StartRound(mine) : null;
                        if (drv == null || !drv.Configured) { H2HLocalUnplayable(); return; }
                        H2HHookDriver(drv);
                        return;
                    }
                    if (Driver == null || !Driver.Configured) { H2HLocalUnplayable(); return; }
                    if (Phase == CupPhase.Loading) return;   // the barrier: the card stays up for the others
                    if (Loading != null && Loading.Visible)
                    {
                        if (!Loading.HideRequested && Driver.SceneBuilt && Loading.MinElapsed) Loading.Hide();
                        return;
                    }
                    _h2hLocal = H2HLocal.Toss;
                    _h2hTossDone = false;
                    H2HBeginToss(clearAll: false);
                    break;

                case H2HLocal.Toss:
                    if (!_h2hTossDone) return;
                    _h2hTossDone = false;
                    if (Driver == null || !Driver.Configured) { H2HLocalUnplayable(); return; }
                    EnsureCoinOutcome();
                    H2HRecordLocalCoin();
                    Driver.Begin();   // Intro: everyone re-parked, the card, the cursor captured
                    _h2hLocal = H2HLocal.Playing;
                    break;

                case H2HLocal.Playing:
                    if (Driver == null) { H2HLocalUnplayable(); return; }
                    UpdateLiveRow();   // a client's row reaches the host through CupRequest.LiveRow (automatic)
                    if (Driver.Phase == RoundPhase.Over) H2HFinishLocalRound();
                    break;
            }
        }

        /// <summary>The local round is over: the result recorded and reported, the career, the teardown, the lobby.</summary>
        void H2HFinishLocalRound()
        {
            var round = CurrentRound;
            var line = Driver != null ? Driver.Line : null;
            if (round != null)
            {
                if (!RecordResult(round, line))
                {
                    // Only after an Abort (an undecided line): the round cannot be replayed and the
                    // bracket cannot hold a hole, so the sim settles it (logged by RecordResult).
                    CupLog.Warn("Head to Head: the local round had no decided line - simulating it");
                    if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                    RefreshPlayersFromBracket();
                    Notify();
                }
                ReportRoundResult(round);   // client -> host (CupRequest.RoundResult); a no-op on the host
                H2HRoundConcluded(round);
            }
            H2HUnhookDriver();
            EndRound();
            _h2hLocal = H2HLocal.Done;
            EnsureLobby();
        }

        /// <summary>
        /// No driver where this peer expected its own round (StartRound refused, a build that
        /// failed). Never a soft lock: the sim settles it, the host is told (a client's report is
        /// a valid line like any other), and the peer waits in the lobby.
        /// </summary>
        void H2HLocalUnplayable()
        {
            var round = H2HPendingLocalRound() ?? CurrentRound;
            CupLog.Error("Head to Head: the local round at " + CupStages.Short(Stage) + " could not be played - simulating it");
            H2HUnhookDriver();
            EndRound();
            if (Loading != null) Loading.HideImmediate();
            if (round != null && Bracket != null && round.Ready)
            {
                if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                RefreshPlayersFromBracket();
                Notify();
                ReportRoundResult(round);
                H2HRoundConcluded(round);
            }
            _h2hLocal = H2HLocal.Done;
            EnsureLobby();
        }

        // ---- the host round (two humans, host-simulated) ----------------------------------------

        /// <summary>Host: both participants' live rows from the driver (the local one through UpdateLiveRow, the remote one directly).</summary>
        void H2HSetHostRoundLive()
        {
            var d = Driver;
            var r = CurrentRound;
            if (d == null || r == null || Bracket == null) return;
            UpdateLiveRow();
            for (int side = 0; side < 2; side++)
            {
                var cs = CupSides.At(side);
                int slot = d.Setup != null ? d.Setup.HumanSlotOf(cs) : -1;
                if (slot < 0 || slot == LocalSlot) continue;
                int e = r.Entrant(cs);
                int opp = NationOfEntrant(r.OpponentOf(e));
                int own = side == 0 ? d.ScoreA : d.ScoreB;
                int theirs = side == 0 ? d.ScoreB : d.ScoreA;
                var p = PlayerAt(slot);
                if (p == null || !p.Active) continue;
                if (p.Playing && p.LiveOpponentNation == opp && p.LiveScoreFor == own && p.LiveScoreAgainst == theirs && p.LiveKick == d.KickIndex + 1) continue;
                ApplyLiveRow(slot, opp, own, theirs, d.KickIndex + 1, true);
            }
        }

        /// <summary>Host: the human-vs-human round is over - the result, the career, the teardown, then whatever the bracket calls for next.</summary>
        void H2HFinishHostRound()
        {
            var round = CurrentRound;
            var line = Driver != null ? Driver.Line : null;
            if (round != null)
            {
                if (!RecordResult(round, line))
                {
                    CupLog.Warn("Head to Head: the host round had no decided line - simulating it");
                    if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                    RefreshPlayersFromBracket();
                    Notify();
                }
                H2HRoundConcluded(round);
            }
            H2HUnhookDriver();
            H2HUnhookWatchBar();   // a host that only watched had the bar
            if (LocalPlayer != null && LocalPlayer.SpectatingSlot >= 0) StopSpectating();
            EndRound();
            _h2hHostRound = false;
            _h2hLocal = H2HLocal.Done;
            H2HHostAdvance(false);
        }

        /// <summary>
        /// Client: the host's round is over (its Over state, or the host already moved the phase
        /// on) - the result from the mirrored line when it decides (the CupState echo confirms it
        /// either way), the career, the teardown, the cursor freed. The caller decides what screen
        /// follows (the lobby from the Round tick; a phase entry brings its own).
        /// </summary>
        void H2HClientRoundOver()
        {
            var round = CurrentRound;
            if (round != null && Driver != null && Driver.IsDecided && !round.Done) RecordResult(round, Driver.Line);
            if (round != null) H2HRoundConcluded(round);
            if (LocalPlayer != null && LocalPlayer.SpectatingSlot >= 0) StopSpectating();
            H2HUnhookDriver();
            H2HUnhookWatchBar();
            EndRound();
            _h2hHostRound = false;
            _h2hLocal = H2HLocal.Done;
            GameInput.CaptureCursor(false);
        }

        /// <summary>Host: the human-vs-human round has no scene to play in - the sim settles it (design 10: never a soft lock) and the flow moves on.</summary>
        void H2HHostRoundUnplayable()
        {
            var round = CurrentRound ?? H2HNextHeadToHeadRound();
            CupLog.Error("Head to Head: the host round at " + CupStages.Short(Stage) + " could not be played - simulating it");
            H2HUnhookDriver();
            EndRound();
            if (Loading != null) Loading.HideImmediate();
            if (round != null && Bracket != null && round.Ready && !round.Done)
            {
                CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                RefreshPlayersFromBracket();
                Notify();
                H2HRoundConcluded(round);
            }
            _h2hHostRound = false;
            _h2hLocal = H2HLocal.Done;
            H2HHostAdvance(false);
        }

        // ==========================================================================================
        // The host's stage machine
        // ==========================================================================================

        /// <summary>
        /// Host: where the stage stands and what comes next. While a wave runs (waveRunning) it
        /// only watches the wave's rounds come in; otherwise it opens a late wave for any pending
        /// human-vs-AI round (a leaver turned a human round into one), the next human-vs-human
        /// round (Interstitial), the podium when the cup is complete or nobody is alive (the rest
        /// simulated), the lobby while the stage completes, and the next stage once AllReady.
        /// AI-vs-AI rounds (a leaver's included) are simulated here whenever they appear.
        /// </summary>
        void H2HHostAdvance(bool waveRunning)
        {
            if (!IsAuthority || Bracket == null) return;
            SimulateAiRounds(Stage);
            H2HResolveParallelCalls();

            if (waveRunning)
            {
                H2HWaveWatchdog();
                if (!H2HWaveDone()) return;
            }

            if (Bracket.IsComplete)
            {
                if (Phase != CupPhase.Podium) SetPhase(CupPhase.Podium);
                return;
            }

            var pending = new List<CupRound>();
            H2HPendingParallelRounds(pending);
            if (pending.Count > 0)
            {
                // A late wave (a human-vs-human round whose other human left): the owner plays it
                // locally like any other; everyone else waits in the lobby.
                H2HStartWave(false);
                return;
            }
            var next = H2HNextHeadToHeadRound();
            if (next != null)
            {
                if (Phase != CupPhase.Interstitial) SetPhase(CupPhase.Interstitial);
                return;
            }
            if (!Bracket.StageComplete(Stage))
            {
                // A pending round with no owner to play it (cannot happen after SimulateAiRounds;
                // guarded so the cup can never stall on it).
                CupLog.Warn("Head to Head: " + CupStages.Short(Stage) + " has a pending round nobody can play - simulating it");
                CupSim.SimulateStage(Bracket, Stage, new SeededRng(Seed), false);
                RefreshPlayersFromBracket();
                Notify();
                if (!Bracket.StageComplete(Stage)) return;
            }
            if (!Bracket.AnyHumanAlive())
            {
                // Design 10: nobody human is left in the draw - the rest is simulated and the
                // podium crowns the AI champion, the connected humans standing round it.
                H2HSimulateToEnd();
                if (Phase != CupPhase.Podium) SetPhase(CupPhase.Podium);
                return;
            }
            if (CupStages.IsLast(Stage))
            {
                // The Final is done but IsComplete said no: unreachable, kept honest.
                if (Phase != CupPhase.Podium) SetPhase(CupPhase.Podium);
                return;
            }
            if (Phase != CupPhase.Lobby)
            {
                SetPhase(CupPhase.Lobby);
                return;
            }
            // The ready gate (design 4.8): every surviving human ready (the eliminated are auto-ready).
            if (AllReady && AdvanceStage()) SetPhase(CupPhase.Bracket);
        }

        /// <summary>Host: every wave round is Done (reported, simulated for a leaver, or settled by the watchdog).</summary>
        bool H2HWaveDone()
        {
            for (int i = 0; i < _h2hWaveRounds.Count; i++)
            {
                var r = _h2hWaveRounds[i];
                if (r != null && r.Ready && !r.Done) return false;
            }
            return true;
        }

        /// <summary>
        /// Host: the owner's report for a wave round was REFUSED (the rules, the wrong first
        /// kicker, a round the host simulates itself). The refusal is immediate and knowable, so
        /// the round does not wait out HeadToHeadResultGrace - the next watchdog pass simulates it
        /// and the owner follows through NetApplyResult. Only the silent case needs the timeout.
        /// Called from the CupRequest.RoundResult handler; a no-op outside a Head to Head wave.
        /// </summary>
        void H2HRoundResultRefused(int ownerSlot)
        {
            if (Style != CupStyle.HeadToHead || !IsAuthority || ownerSlot < 0) return;
            _h2hOwnerRefused.Add(ownerSlot);
        }

        /// <summary>
        /// Host: a wave round whose owner's row stopped reading Playing without a result following
        /// within HeadToHeadResultGrace (a crash short of the roster noticing) is settled by the
        /// sim, as is a round whose report was refused outright (H2HRoundResultRefused, no wait)
        /// and everything left after HeadToHeadWaveCap. The first result wins (ApplyRoundResult
        /// refuses a Done round), so a late report cannot flip a stage that has moved on.
        /// </summary>
        void H2HWaveWatchdog()
        {
            float now = Time.unscaledTime;
            bool capped = now - _h2hWaveStartedAt > HeadToHeadWaveCap;
            for (int i = 0; i < _h2hWaveRounds.Count; i++)
            {
                var r = _h2hWaveRounds[i];
                if (r == null || !r.Ready || r.Done) continue;
                int owner = H2HOwnerOf(r);
                var p = owner >= 0 ? PlayerAt(owner) : null;
                bool settle = capped || (owner >= 0 && _h2hOwnerRefused.Contains(owner));
                if (p != null && p.Active && owner != LocalSlot)
                {
                    if (p.Playing)
                    {
                        _h2hOwnerSeenPlaying.Add(owner);
                        _h2hOwnerStoppedAt.Remove(owner);
                    }
                    else if (_h2hOwnerSeenPlaying.Contains(owner))
                    {
                        float at;
                        if (!_h2hOwnerStoppedAt.TryGetValue(owner, out at)) { _h2hOwnerStoppedAt[owner] = now; at = now; }
                        if (now - at > HeadToHeadResultGrace) settle = true;
                    }
                }
                if (!settle) continue;
                bool refused = owner >= 0 && _h2hOwnerRefused.Remove(owner);
                CupLog.Warn("Head to Head: " + (refused ? "refused result" : "no result") + " for " + CupStages.Short(r.Stage)
                            + " #" + r.Index + " from slot " + owner + " - simulating it");
                CupSim.Simulate(r, Bracket, CupSim.StreamFor(Bracket, r));
                RefreshPlayersFromBracket();
                Notify();
            }
        }

        /// <summary>Host: simulate every remaining stage (the knocked-out card's presses in one go), the podium next.</summary>
        void H2HSimulateToEnd()
        {
            int guard = 0;
            while (Bracket != null && !Bracket.IsComplete && guard++ < CupStages.Count + 1)
            {
                if (SimulateRest() == null) break;
            }
        }

        // ---- bracket queries shared by host and clients (pure functions of the model) --------------

        /// <summary>Pending rounds of the stage with exactly one human in control (the wave's rounds).</summary>
        void H2HPendingParallelRounds(List<CupRound> into)
        {
            into.Clear();
            if (Bracket == null) return;
            var pending = Bracket.PendingRounds(Stage);
            for (int i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                bool a = Bracket.Entrants[r.EntrantA].IsHuman, b = Bracket.Entrants[r.EntrantB].IsHuman;
                if (a != b) into.Add(r);
            }
        }

        /// <summary>The first pending human-vs-human round of the stage (lowest index), or null. Identical on every peer.</summary>
        CupRound H2HNextHeadToHeadRound()
        {
            if (Bracket == null) return null;
            var pending = Bracket.PendingRounds(Stage);
            for (int i = 0; i < pending.Count; i++)
            {
                var r = pending[i];
                if (Bracket.Entrants[r.EntrantA].IsHuman && Bracket.Entrants[r.EntrantB].IsHuman) return r;
            }
            return null;
        }

        /// <summary>The local player's pending human-vs-AI round this stage (played here), or null.</summary>
        CupRound H2HPendingLocalRound()
        {
            var me = LocalPlayer;
            if (me == null || !me.Active || Bracket == null) return null;
            var r = LocalRoundThisStage;
            if (r == null || !r.Ready || r.Done) return null;
            return AuthorityFor(r) == RoundAuthority.Local ? r : null;
        }

        /// <summary>The human slot owning a human-vs-AI round, -1 for none.</summary>
        int H2HOwnerOf(CupRound r)
        {
            if (r == null || Bracket == null) return -1;
            var ea = Bracket.Entrants[r.EntrantA];
            var eb = Bracket.Entrants[r.EntrantB];
            if (ea.IsHuman && !eb.IsHuman) return ea.HumanSlot;
            if (eb.IsHuman && !ea.IsHuman) return eb.HumanSlot;
            return -1;
        }

        /// <summary>The participant a bodiless watcher of a host round mirrors first: the lower slot of the two.</summary>
        int H2HDefaultWatchSlot(CupRound r)
        {
            if (r == null || Bracket == null) return -1;
            int a = Bracket.Entrants[r.EntrantA].IsHuman ? Bracket.Entrants[r.EntrantA].HumanSlot : -1;
            int b = Bracket.Entrants[r.EntrantB].IsHuman ? Bracket.Entrants[r.EntrantB].HumanSlot : -1;
            if (a >= 0 && b >= 0) return Mathf.Min(a, b);
            return a >= 0 ? a : b;
        }

        // ==========================================================================================
        // The coin (design 6.11, Head to Head reading: everyone calls, nothing shown, career counted)
        // ==========================================================================================

        /// <summary>
        /// Start the ceremony for the round standing here. A HOST ROUND's toss on the host clears
        /// every call first (one toss for everyone present); a PARALLEL toss - and every toss on a
        /// client - clears only the local player's, because the other players' calls belong to
        /// their own rounds (the host judges each against the right round: ResolveCoinCalls'
        /// Head to Head scope and H2HResolveParallelCalls). The local pick is captured at the end
        /// for the career write, independent of the host's echo.
        /// </summary>
        void H2HBeginToss(bool clearAll)
        {
            if (Toss != null) { Toss.Cancel(); Toss = null; }
            if (clearAll) ClearCoinCalls();
            else
            {
                var me = LocalPlayer;
                if (me != null) { me.CoinCall = null; me.CoinCallRight = null; }
            }
            Notify();
            _h2hTossPick = null;
            CupCoinToss toss = null;
            toss = CupCoinToss.Begin(this, Driver, Rig, () =>
            {
                _h2hTossPick = toss != null ? toss.LocalPick : (CoinFace?)null;
                Toss = null;
                _h2hTossDone = true;
            });
            // A null return fired onDone synchronously (no scene to run the ceremony in).
            Toss = toss;
        }

        /// <summary>Career (design 9.7): the local player's call for the toss that just landed, right or wrong.</summary>
        void H2HRecordLocalCoin()
        {
            var d = Driver;
            if (d == null || !d.CoinResult.HasValue) { _h2hTossPick = null; return; }
            CoinFace? pick = _h2hTossPick;
            var me = LocalPlayer;
            if (!pick.HasValue && me != null) pick = me.CoinCall;
            _h2hTossPick = null;
            if (pick.HasValue) CupCareer.CoinCalled(pick.Value, d.CoinResult.Value, Style);
        }

        /// <summary>
        /// Host: judge a remote owner's parallel-round call once THEIR round is in (the result
        /// tells the host the toss is long done): the round's seeded face (the Coin stream's first
        /// draw, exactly what their ceremony showed) against the call the request carried. Counted
        /// once (a judged call carries a verdict); the summary's "coin calls" column reads it.
        /// </summary>
        void H2HResolveParallelCalls()
        {
            if (!IsAuthority || Bracket == null) return;
            bool changed = false;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Slot == LocalSlot || !p.CoinCall.HasValue || p.CoinCallRight.HasValue || p.Entrant < 0) continue;
                var r = Bracket.RoundOfEntrant(Stage, p.Entrant);
                if (r == null || !r.Done) continue;
                // Only a round this player owned alone: a host round's callers were judged by the
                // ceremony at the flip (their verdicts are set), a leaver's round is nobody's.
                if (H2HOwnerOf(r) != p.Slot) continue;
                var face = new SeededRng(Seed).Fork(CupSalts.Coin(r.Stage, r.Index)).Coin();
                bool right = p.CoinCall.Value == face;
                p.CoinCallRight = right;
                p.CoinCallsMade++;
                if (right) p.CoinCallsRight++;
                changed = true;
            }
            if (changed) Notify();
        }

        // ==========================================================================================
        // The interstitial (design 4.7: "HEAD TO HEAD - up next" for the two participants)
        // ==========================================================================================

        void H2HEnterInterstitial()
        {
            _h2hNextRound = HostRound != null && !HostRound.Done && AuthorityFor(HostRound) != RoundAuthority.Local
                ? HostRound
                : H2HNextHeadToHeadRound();
            bool participant = _h2hNextRound != null && LocalEntrant >= 0 && _h2hNextRound.Involves(LocalEntrant);
            if (participant)
            {
                CloseLobby();
                MenuBackdrop();
                if (_h2hCardDraw == null) _h2hCardDraw = H2HDrawInterstitial;
                if (!_h2hCardHooked) { AddGuiHook(_h2hCardDraw); _h2hCardHooked = true; }
                GameInput.CaptureCursor(false);
            }
            else
            {
                EnsureLobby();   // its gate line reads "Head to head next: Alice vs Bob"
            }
        }

        /// <summary>Host: after the beat, the round is started HERE (so CupState carries it) and the loading barrier opens.</summary>
        void H2HTickInterstitial()
        {
            H2HLobbyBackdrop();
            if (!IsAuthority || PhaseTime < HeadToHeadInterstitialSeconds) return;
            var next = H2HNextHeadToHeadRound();
            if (next == null)
            {
                // The pairing dissolved during the card (a participant left): back to the lobby,
                // whose tick opens the late wave for the human who stayed.
                SetPhase(CupPhase.Lobby);
                return;
            }
            H2HShowLoading(next);
            var drv = StartRound(next);   // Host authority: the scene builds under the card
            if (drv == null || !drv.Configured)
            {
                H2HHostRoundUnplayable();
                return;
            }
            H2HHookDriver(drv);
            _h2hHostRound = true;
            _h2hRoundStarted = true;
            H2HSetHostRoundLive();   // both participants read Playing (enables Spectate on their rows)
            ClearLoaded();
            SetPhase(CupPhase.Loading);
        }

        void H2HDrawInterstitial()
        {
            if (Phase != CupPhase.Interstitial || PauseMenu.Paused) return;   // no controls here: an early return is safe
            H2HStyles();
            var r = _h2hNextRound ?? H2HNextHeadToHeadRound();
            MenuScale.Begin();
            try
            {
                float w = MenuScale.Width, h = MenuScale.Height;
                UITheme.Scrim(w, h, 0.55f, 900f);
                const float plateW = 640f, plateH = 250f;
                var p = new Rect(w * 0.5f - plateW * 0.5f, h * 0.5f - plateH * 0.5f - 20f, plateW, plateH);
                UITheme.Glow(new Rect(p.x - 120f, p.y - 90f, p.width + 240f, p.height + 180f), new Color(0f, 0f, 0f, 0.45f));
                UITheme.Panel(p, UITheme.Gold);
                UITheme.Shadowed(new Rect(p.x, p.y + 16f, p.width, 40f), CupText.HeadToHeadUpNext, _h2hCardTitle, UITheme.Gold, 0.7f, 2f);
                UITheme.Shadowed(new Rect(p.x, p.y + 58f, p.width, 22f), CupStages.Header(Stage), _h2hCardStage, UITheme.Dim, 0.5f, 1f);
                UITheme.Fill(new Rect(p.x + 40f, p.y + 86f, p.width - 80f, 1f), new Color(1f, 1f, 1f, 0.09f));

                float cy = p.y + 140f;
                if (r != null && Bracket != null)
                {
                    H2HDrawSide(r.EntrantA, p.x + 40f, cy, false);
                    H2HDrawSide(r.EntrantB, p.xMax - 40f, cy, true);
                }
                UITheme.Shadowed(new Rect(p.x + p.width * 0.5f - 40f, cy - 18f, 80f, 36f), "vs", _h2hCardVs, UITheme.Dim, 0.6f, 1f);

                // The beat's bar: the host's timer (PhaseTime rides CupState), no button.
                float t = Mathf.Clamp01(PhaseTime / HeadToHeadInterstitialSeconds);
                UITheme.Bar(new Rect(p.x + 40f, p.yMax - 30f, p.width - 80f, 8f), t, UITheme.Gold, UITheme.Gold);
            }
            finally
            {
                MenuScale.End();
            }
        }

        void H2HDrawSide(int entrant, float edgeX, float cy, bool right)
        {
            const float flag = 56f, textW = 200f;
            if (!Bracket.IsValidEntrant(entrant)) return;
            var e = Bracket.Entrants[entrant];
            int nation = e.NationIndex;
            float fx = right ? edgeX - flag : edgeX;
            CupUiKit.Flag(new Rect(fx, cy - flag * 0.5f, flag, flag), nation);
            float tx = right ? fx - 14f - textW : fx + flag + 14f;
            _h2hCardName.alignment = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            _h2hCardCode.alignment = _h2hCardName.alignment;
            UITheme.Shadowed(new Rect(tx, cy - 24f, textW, 28f), e.DisplayName, _h2hCardName, UITheme.Ink, 0.6f, 1f);
            string nat = CupNations.IsValid(nation) ? CupNations.Name(nation) + "  " + CupNations.Code(nation) : "-";
            UITheme.Shadowed(new Rect(tx, cy + 6f, textW, 20f), nat, _h2hCardCode, UITheme.Dim, 0.4f, 1f);
        }

        // ==========================================================================================
        // Watching a host round without a body: the mirrored camera, and the bar that picks it
        // ==========================================================================================

        /// <summary>
        /// A bodiless watcher of a host round sees the host's snapshots through its Client driver
        /// and, while spectating a participant, that participant's camera (CupSpectatorView
        /// mirrors it and holds the driver's own cuts off). Not mirroring anyone - after Esc, or
        /// after a phase cut released the rig - the broadcast camera (auto vantage, orbit on
        /// mouse) so the round is never watched from wherever the last cut left the lens.
        /// </summary>
        void H2HWatcherCamera()
        {
            var d = Driver;
            if (d == null || d.Setup == null || d.Setup.LocalHasBody || Rig == null) return;
            if (Spectator != null) { _h2hCamDirty = false; return; }
            // A cut that released the rig (Intro, Over) reads as View.None; a placement cut leaves
            // whatever the last window held (the driver frames nothing for a spectator), so the
            // driver's Placing edge asks for the broadcast view again through _h2hCamDirty.
            if (_h2hCamDirty || Rig.Current == CupCameraRig.View.None)
            {
                Rig.ReplayView();
                _h2hCamDirty = false;
            }
        }

        void H2HHookWatchBar()
        {
            if (_h2hWatchDraw == null) _h2hWatchDraw = H2HDrawWatchBar;
            if (!_h2hWatchHooked) { AddGuiHook(_h2hWatchDraw); _h2hWatchHooked = true; }
        }

        void H2HUnhookWatchBar()
        {
            if (_h2hWatchHooked && _h2hWatchDraw != null) RemoveGuiHook(_h2hWatchDraw);
            _h2hWatchHooked = false;
        }

        /// <summary>"Watch Alice / Watch Bob / Free camera" for a watcher of a host round (the lobby is closed while it runs).</summary>
        void H2HDrawWatchBar()
        {
            var d = Driver;
            var r = CurrentRound;
            // These terms only change in Update (a driver comes and goes between frames), so Layout
            // and Repaint always agree on whether these controls exist. PauseMenu.Paused is NOT one
            // of them - it is written from INSIDE an IMGUI pass (the menu's own Resume button runs
            // its action straight out of UITheme.Button), so a pass can begin paused and end
            // unpaused. Returning on it would allocate this bar's 2-3 controls for the first time in
            // an event that never saw their Layout and shift every id drawn after them - including
            // the coin overlay's ClickBlocker and HEADS / TAILS, which are hooked AFTER this bar and
            // are live at the same time for a watcher (CupCoinToss.Draw carries the same note).
            // The hiding happens below, by the same parking the coin buttons use.
            if (!_h2hHostRound || d == null || d.Setup == null || d.Setup.LocalHasBody || r == null || Bracket == null) return;
            H2HStyles();
            MenuScale.Begin();
            Action fire = null;
            try
            {
                float w = MenuScale.Width, h = MenuScale.Height;
                var me = LocalPlayer;
                int watching = me != null ? me.SpectatingSlot : -1;
                const float bw = 150f, bh = 34f, gap = 8f;
                float y = h - 110f;
                var labels = new List<string>(3);
                var acts = new List<Action>(3);
                var lit = new List<bool>(3);
                for (int side = 0; side < 2; side++)
                {
                    var e = Bracket.Entrants[r.Entrant(CupSides.At(side))];
                    if (!e.IsHuman) continue;
                    int slot = e.HumanSlot;
                    labels.Add("Watch " + e.DisplayName);
                    acts.Add(() => Spectate(slot));
                    lit.Add(watching == slot);
                }
                labels.Add("Free camera");
                acts.Add(() => { StopSpectating(); if (Rig != null) Rig.ReplayView(); });
                lit.Add(watching < 0);
                float total = labels.Count * bw + (labels.Count - 1) * gap;
                float x = w * 0.5f - total * 0.5f;
                // Under the pause menu the bar hides by PARKING its buttons off-screen and
                // disabling them - never by skipping them, which would shift every control id
                // drawn after this hook (see the note above the guard).
                bool live = !PauseMenu.Paused;
                bool prevEnabled = GUI.enabled;
                GUI.enabled = live;
                for (int i = 0; i < labels.Count; i++)
                {
                    var br = live ? new Rect(x + i * (bw + gap), y, bw, bh) : new Rect(-1000f, -1000f, bw, bh);
                    var keep = GUI.backgroundColor;
                    if (live && lit[i]) GUI.backgroundColor = UITheme.SelTint;
                    if (UITheme.Button(br, labels[i], _h2hWatchBtn) && live && !lit[i]) fire = acts[i];
                    GUI.backgroundColor = keep;
                }
                GUI.enabled = prevEnabled;
                // Text only (GUI.Label allocates no control), so it is safe to skip outright.
                if (live) UITheme.Hint(new Rect(0f, y - 22f, w, 18f), "Watching the head to head");
            }
            finally
            {
                MenuScale.End();
            }
            fire?.Invoke();
        }

        // ==========================================================================================
        // Leavers (host): a participant of the host round plays on through the AI
        // ==========================================================================================

        /// <summary>
        /// Host: a player newly flagged Left (ApplyLeave ran from RosterChanged or CupRequest.Quit).
        /// The bracket side is done (the nation is AI, later rounds simulated, the row reads
        /// "(AI)"); what is left is the ROUND IN PROGRESS: in a host round their bodies are handed
        /// to CupBotTaker / Goalkeeper so the round finishes with AI on their side (design 10). A
        /// parallel round of theirs is now AI-vs-AI and H2HHostAdvance simulates it.
        /// </summary>
        void H2HTickLeavers()
        {
            if (!IsAuthority) return;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.Left || _h2hLeftSeen.Contains(p.Slot)) continue;
                _h2hLeftSeen.Add(p.Slot);
                var d = Driver;
                if (d != null && d.Configured && d.Authority == RoundAuthority.Host && d.HandSlotToAi(p.Slot))
                    CupLog.Info("Head to Head: " + p.DisplayName + " left mid-round - the AI plays on for their side");
            }
        }

        // ==========================================================================================
        // Helpers
        // ==========================================================================================

        /// <summary>The cup lobby (design 6.3), created once and kept across the wave / interstitial entries that keep it.</summary>
        void EnsureLobby()
        {
            if (_h2hLobby != null) return;
            _h2hLobby = CupLobbyUI.Create(MatchRoot, this);
            // Customize (design 6.3) needs the lobby customize path (GameBootstrap.ShowLobbyCustomize),
            // which returns to the MULTIPLAYER LobbyUI and drives its preview camera against the
            // live arena - not safe mid-cup without an editor pass (Solo disables it for the same
            // reason). TODO(h2h-customize): a cup-aware customize (Species -> Customize, back here).
            _h2hLobby.OnCustomizeRequested = null;
        }

        void CloseLobby()
        {
            if (_h2hLobby == null) return;
            _h2hLobby.Close();
            _h2hLobby = null;
        }

        /// <summary>A waiting peer's backdrop: the static wide shot behind the scrimmed lobby, unless a spectate view or a round owns the camera.</summary>
        void H2HLobbyBackdrop()
        {
            if (_h2hLobby == null || Driver != null || Spectator != null || Rig == null) return;
            if (Rig.Current != CupCameraRig.View.CoinToss) MenuBackdrop();
        }

        /// <summary>
        /// End whatever round stands (the toss cancelled, the HUD unbound, the root destroyed); a
        /// no-op with none. A CLIENT whose host round is still up when the host's next phase lands
        /// (its Over state and the phase change can arrive in one poll) concludes it first, so the
        /// career round write is never lost on that edge.
        /// </summary>
        void H2HEndAnyRound()
        {
            if (!IsAuthority && _h2hHostRound && Driver != null && Driver.Configured) H2HClientRoundOver();
            H2HUnhookDriver();
            H2HUnhookWatchBar();
            if (Driver != null || CurrentRound != null || RoundRoot != null) EndRound();
            _h2hHostRound = false;
            _h2hLocal = H2HLocal.None;
            _h2hTossDone = false;
            _h2hCamDirty = false;
        }

        void H2HHookDriver(CupRoundDriver drv)
        {
            H2HUnhookDriver();
            if (drv == null) return;
            _h2hHooked = drv;
            drv.PhaseChanged += H2HOnDriverPhase;
        }

        void H2HUnhookDriver()
        {
            if (_h2hHooked != null) _h2hHooked.PhaseChanged -= H2HOnDriverPhase;
            _h2hHooked = null;
        }

        void H2HOnDriverPhase(RoundPhase phase)
        {
            // The driver's Placing cut frames the local role only; a bodiless watcher re-takes the
            // broadcast view from the next tick (never from inside the driver's event: the
            // driver's own camera call for the phase runs after this and would win).
            if (phase == RoundPhase.Placing) _h2hCamDirty = true;
        }

        /// <summary>
        /// PauseMenu.Resume re-captures the cursor unconditionally. The director frees it again
        /// for every menu phase; a PARALLEL toss runs under the Round phase (CursorShouldBeFree
        /// says captured there, rightly, for the round that follows), so its HEADS / TAILS
        /// buttons need this edge of their own.
        /// </summary>
        void H2HCursorAfterUnpause()
        {
            bool paused = PauseMenu.Paused;
            if (_h2hWasPaused && !paused && Toss != null) GameInput.CaptureCursor(false);
            _h2hWasPaused = paused;
        }

        /// <summary>Close whatever screen the previous phase left (each Close destroys its object and unhooks).</summary>
        void CloseH2HScreens(bool keepLobby)
        {
            if (_h2hPicker != null) { _h2hPicker.Close(); _h2hPicker = null; }
            if (_h2hBracket != null) { _h2hBracket.Close(); _h2hBracket = null; }
            if (_h2hResults != null) { _h2hResults.Close(); _h2hResults = null; }
            if (!keepLobby) CloseLobby();
            if (_h2hCardHooked && _h2hCardDraw != null) RemoveGuiHook(_h2hCardDraw);
            _h2hCardHooked = false;
            H2HUnhookWatchBar();
            EndPodium();
        }

        static void H2HStyles()
        {
            if (_h2hCardTitle != null) return;
            _h2hCardTitle = new GUIStyle { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _h2hCardStage = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            _h2hCardName = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Ink } };
            _h2hCardCode = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = UITheme.Dim } };
            _h2hCardVs = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
            _h2hWatchBtn = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            UIFont.Heavy(_h2hCardTitle);
            UIFont.Heavy(_h2hCardName);
        }
    }
}
