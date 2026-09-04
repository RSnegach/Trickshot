using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// SOLO (design section 3): one human, 31 AI nations, the human's round played each stage
    /// and the rest simulated, a podium at the end. No session, every intent applies locally,
    /// pausing freezes the game (timeScale 0, so PhaseTime freezes too).
    ///
    /// Phase map (the flow this file drives; every ENTRY runs once per SetPhase through the
    /// PhaseSerial latch, from the tick - never from inside a GUI pass):
    ///   NationPick   NationPickerUI (Solo variant). The pick is the confirm: on AllPicked ->
    ///                BuildBracket(), the career "entered" write, the career-best latch for the
    ///                KNOCKED OUT card, then Bracket ("THE DRAW"). Back / Esc -> OnBackToSetup
    ///                (the fork screen) or, without one, the main menu.
    ///   Bracket      CupBracketScreen for CupTuning.BracketScreenSeconds, no button, then
    ///                Loading. Later stages show the stage header and the shrunken tree.
    ///   Loading      Loading.Show over the local round, StartRound one frame later (the card is
    ///                on screen before the build hitch), Hide once the scene is built and the
    ///                minimum has elapsed, CoinToss once the fade is out.
    ///   CoinToss     BeginCoinToss: the local player calls (5 s timeout = HEADS), the referee's
    ///                ceremony, the flash; onDone -> EnsureCoinOutcome, the career coin write,
    ///                Driver.Begin(), Round.
    ///   Round        Driver.Phase walks Intro..Over; UpdateLiveRow() keeps the row current. On
    ///                Over: RecordResult(CurrentRound, Driver.Line), the career round write,
    ///                EndRound(), SimulateAiRounds(Stage); lost -> GameOver; won the Final ->
    ///                Podium; else StageComplete.
    ///   StageComplete  The cup lobby with ONE row (CupLobbyUI): the result, the "Simulating the
    ///                rest of the stage" reveal, View Bracket, Customize (disabled in Solo, see
    ///                EnterStageComplete), Continue (= Ready), Quit to Menu. ClearReady on entry,
    ///                or the pick's ready would advance it at once. AllReady -> AdvanceStage(),
    ///                the career stage write, Bracket.
    ///   Lobby        (unused in Solo; StageComplete is its lobby.)
    ///   GameOver     The KNOCKED OUT card (6.7): its buttons call SimulateRest() per press (the
    ///                tree fills stage by stage, the last press crowns the AI champion),
    ///                PlayAgain(), QuitToMenu().
    ///   Podium       CupPodium (TryBeginPodium, design 8.1): the champion lifts the trophy on
    ///                the dais, the beaten nations stand round it; after PodiumButtonsDelay its
    ///                New Cup / Continue / Main Menu buttons lead on (PlayAgain / Results /
    ///                QuitToMenu). Refused only with no champion: the fanfare, straight to Results.
    ///   Results      The CUP SUMMARY table (6.6): New Cup (PlayAgain) / Main Menu (QuitToMenu).
    ///   Ended        Every screen closed; the main-menu callback is tearing the match down.
    /// Career stats (9.7) are written as they happen: entered at the draw, the stage as each one
    /// opens, every own kick at its verdict (the shell's stats listener), the coin after each
    /// toss, the round at its end, the cup on the Final.
    /// </summary>
    public partial class CupDirector
    {
        // The screens of the phase in progress (at most one of each; closed on every entry).
        NationPickerUI _soloPicker;
        CupBracketScreen _soloBracket;
        CupLobbyUI _soloLobby;
        CupKnockedOutUI _soloKnockedOut;
        CupResultsUI _soloResults;

        int _soloSerial = -1;          // the PhaseSerial whose entry actions have run
        int _soloLoadShownFrame = -1;  // Loading: the frame the card was shown (the build waits one)
        bool _soloRoundStarted;        // Loading: StartRound has run for this entry
        bool _soloTossDone;            // CoinToss: the ceremony's onDone landed
        bool _soloDrawFailed;          // NationPick: BuildBracket refused (logged once, not retried every frame)

        /// <summary>Called from Update every frame while Style == Solo.</summary>
        void SoloTick()
        {
            if (_soloSerial != PhaseSerial)
            {
                _soloSerial = PhaseSerial;
                SoloEnter(Phase);
            }

            switch (Phase)
            {
                case CupPhase.NationPick:
                    // The pick is the confirm (design 3.4): the moment the local player has
                    // picked, the draw is made and shown. Nothing else gates it in Solo.
                    if (AllPicked && Bracket == null && !_soloDrawFailed)
                    {
                        if (BuildBracket())
                        {
                            SoloCupEntered();
                            SetPhase(CupPhase.Bracket);
                        }
                        else
                        {
                            // Logged by BuildBracket. Sit here rather than spin: the player can
                            // still Back out or quit; a second pick never arrives in Solo.
                            _soloDrawFailed = true;
                        }
                    }
                    break;

                case CupPhase.Bracket:
                    // 5 s, no button (design 2.7). A Solo pause freezes PhaseTime with the bar.
                    if (PhaseTime >= CupTuning.BracketScreenSeconds) SetPhase(CupPhase.Loading);
                    break;

                case CupPhase.Loading:
                    SoloTickLoading();
                    break;

                case CupPhase.CoinToss:
                    if (_soloTossDone)
                    {
                        _soloTossDone = false;
                        if (Driver == null || !Driver.Configured) { SoloRoundUnplayable(); break; }
                        // The ceremony recorded the outcome on the driver as the flip started;
                        // this only fills in when no ceremony ran (no scene, or cut short).
                        EnsureCoinOutcome();
                        RecordLocalCoinCall();
                        Driver.Begin();   // Intro: everyone re-parked, the card, the cursor captured
                        SetPhase(CupPhase.Round);
                    }
                    break;

                case CupPhase.Round:
                    if (Driver == null) { SoloRoundUnplayable(); break; }
                    UpdateLiveRow();
                    if (Driver.Phase == RoundPhase.Over) SoloFinishRound();
                    break;

                case CupPhase.StageComplete:
                    // Continue = the Ready toggle (design 3.7); the eliminated never reach here.
                    if (AllReady)
                    {
                        if (AdvanceStage())
                        {
                            CupCareer.StageReached(Stage, Style);
                            SetPhase(CupPhase.Bracket);
                        }
                        else
                        {
                            // The stage is not complete (a simulated AI round is missing): finish
                            // it rather than strand the player on a Continue that does nothing.
                            CupLog.Warn("Solo: AdvanceStage refused at " + CupStages.Short(Stage) + " - simulating the missing rounds");
                            SimulateAiRounds(Stage);
                            if (!Bracket.StageComplete(Stage)) { ClearReady(); }   // hand it back; nothing more we can do this frame
                        }
                    }
                    break;

                case CupPhase.Lobby:
                    // Not used in Solo (StageComplete is the one-row lobby). Nothing to do.
                    break;

                case CupPhase.GameOver:
                    // The KNOCKED OUT card drives itself through the intents.
                    break;

                case CupPhase.Podium:
                    // The podium (when present) drives itself: ContinueFromResults -> Results,
                    // or its own New Cup / Main Menu buttons.
                    break;

                case CupPhase.Results:
                    // The CUP SUMMARY table drives itself: New Cup / Main Menu.
                    break;

                case CupPhase.OrderPick:
                case CupPhase.Interstitial:
                case CupPhase.TrophyLift:
                    // Never visited in Solo.
                    break;

                case CupPhase.Ended:
                    break;
            }
        }

        // ==========================================================================================
        // Phase entries
        // ==========================================================================================

        void SoloEnter(CupPhase phase)
        {
            CloseSoloScreens();
            switch (phase)
            {
                case CupPhase.NationPick:
                    _soloDrawFailed = false;
                    MenuBackdrop();
                    _soloPicker = NationPickerUI.Create(MatchRoot, this, CupStyle.Solo, SoloBackToSetup);
                    break;

                case CupPhase.Bracket:
                    // Create() shows director.Stage with "THE DRAW" only for the Round of 32 and
                    // reads PhaseTime for its bar, so the bar and this tick's timer agree.
                    MenuBackdrop();
                    _soloBracket = CupBracketScreen.Create(MatchRoot, this);
                    break;

                case CupPhase.Loading:
                    EnterSoloLoading();
                    break;

                case CupPhase.CoinToss:
                    _soloTossDone = false;
                    if (Driver == null || !Driver.Configured) { SoloRoundUnplayable(); break; }
                    BeginCoinToss(() => _soloTossDone = true);
                    break;

                case CupPhase.Round:
                    break;

                case CupPhase.StageComplete:
                    EnterSoloStageComplete();
                    break;

                case CupPhase.GameOver:
                    MenuBackdrop();
                    _soloKnockedOut = CupKnockedOutUI.Create(MatchRoot, this);
                    break;

                case CupPhase.Podium:
                    // The podium (design 8.1): the champion on the dais with the trophy, the seven
                    // beaten AI nations round it, confetti, the fanfare; its Continue is
                    // ContinueFromResults (-> Results, the CUP SUMMARY), its New Cup / Main Menu
                    // the director's PlayAgain / QuitToMenu. The round's bodies are gone by now
                    // (SoloFinishRound's EndRound), so it spawns its own from the bracket.
                    if (!TryBeginPodium())
                    {
                        // No champion to crown (the podium logged why): the fanfare it would have
                        // played, then the summary it leads to anyway.
                        AudioManager.Instance?.PlayFanfare();
                        SetPhase(CupPhase.Results);
                    }
                    break;

                case CupPhase.Results:
                    MenuBackdrop();
                    _soloResults = CupResultsUI.Create(MatchRoot, this, CupResultsMode.Summary);
                    break;

                case CupPhase.Ended:
                    if (Loading != null) Loading.HideImmediate();
                    break;
            }
        }

        /// <summary>The draw is made: career "entered", the career-best latch, the first stage.</summary>
        void SoloCupEntered()
        {
            var me = LocalPlayer;
            if (me != null && me.Nation >= 0) CupCareer.Entered(me.Nation, Style);
            // The KNOCKED OUT card compares against the best BEFORE this cup; latch it before
            // StageReached writes the Round of 32 into the save (which would make every later
            // "new career best" read false).
            var data = CareerStats.Data;
            CupKnockedOutUI.BestStageBefore = data != null ? CupCareer.BestStage(data.SP) : null;
            CupCareer.StageReached(Stage, Style);
        }

        void EnterSoloLoading()
        {
            _soloRoundStarted = false;
            _soloLoadShownFrame = Time.frameCount;
            var round = LocalRoundThisStage;
            if (Loading != null)
            {
                int a = round != null ? NationOfEntrant(round.EntrantA) : -1;
                int b = round != null ? NationOfEntrant(round.EntrantB) : -1;
                Loading.Show(Stage, a, b, CupTuning.LoadingMinSeconds);
            }
        }

        void SoloTickLoading()
        {
            if (!_soloRoundStarted)
            {
                // One rendered frame after Show: the card is on screen before the build hitch,
                // so the bodies never pop in (design 6.4).
                if (Time.frameCount <= _soloLoadShownFrame) return;
                _soloRoundStarted = true;
                var round = LocalRoundThisStage;
                var drv = round != null ? StartRound(round) : null;
                if (drv == null || !drv.Configured) { SoloRoundUnplayable(); return; }
                return;
            }
            if (Driver == null) { SoloRoundUnplayable(); return; }
            if (Loading != null && Loading.Visible)
            {
                // Hide waits for the minimum on its own; the fade runs after. The toss starts on
                // a clean screen so the caller gets the whole call window with the buttons up.
                if (!Loading.HideRequested && Driver.SceneBuilt && Loading.MinElapsed) Loading.Hide();
                return;
            }
            SetPhase(CupPhase.CoinToss);
        }

        void EnterSoloStageComplete()
        {
            // The pick set Ready (picking is confirming); AdvanceStage clears it for the stages
            // after, but this screen's Continue must always be a press, never a leftover.
            ClearReady();
            MenuBackdrop();
            _soloLobby = CupLobbyUI.Create(MatchRoot, this);
            // Customize (design 6.3) needs the lobby customize path (GameBootstrap.
            // ShowLobbyCustomize), which returns to the MULTIPLAYER lobby and drives its preview
            // camera against the live arena; neither is safe mid-cup without an editor pass. No
            // handler = the lobby draws the button disabled. TODO(solo-customize): route it
            // through a cup-aware customize (Species -> Customize, back to this screen) once
            // the preview camera has been checked against the standing arena.
            _soloLobby.OnCustomizeRequested = null;
        }

        /// <summary>
        /// The local round is over: the result into the bracket, the career, the round torn
        /// down, the stage's AI rounds resolved, then the screen the result calls for.
        /// </summary>
        void SoloFinishRound()
        {
            var round = CurrentRound;
            var line = Driver != null ? Driver.Line : null;
            if (round == null)
            {
                CupLog.Error("Solo: round over with no CurrentRound");
                EndRound();
                SetPhase(CupPhase.GameOver);
                return;
            }
            if (!RecordResult(round, line))
            {
                // Only reachable after an Abort (an undecided line): the round cannot be
                // replayed and the bracket cannot hold a hole, so the sim settles it. Logged by
                // RecordResult.
                CupLog.Warn("Solo: the played round had no decided line - simulating it");
                if (!round.Done) CupSim.Simulate(round, Bracket, CupSim.StreamFor(Bracket, round));
                RefreshPlayersFromBracket();
                Notify();
            }
            RecordLocalRoundCareer(round);
            EndRound();
            SimulateAiRounds(Stage);

            bool won = round.Done && round.WinnerEntrant == LocalEntrant;
            if (!won)
            {
                SetPhase(CupPhase.GameOver);
            }
            else if (CupStages.IsLast(Stage))
            {
                var me = LocalPlayer;
                CupCareer.Won(me != null ? me.Nation : -1, Style);
                SetPhase(CupPhase.Podium);
            }
            else
            {
                SetPhase(CupPhase.StageComplete);
            }
        }

        /// <summary>
        /// No driver where the flow expected one (StartRound refused, a scene that failed to
        /// build). Never a soft lock: the round is settled by the sim and the flow moves on as if
        /// it had been played, with a log line to find.
        /// </summary>
        void SoloRoundUnplayable()
        {
            CupLog.Error("Solo: the local round at " + CupStages.Short(Stage) + " could not be played - simulating it");
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
            if (!won) SetPhase(CupPhase.GameOver);
            else if (CupStages.IsLast(Stage)) SetPhase(CupPhase.Podium);
            else SetPhase(CupPhase.StageComplete);
        }

        /// <summary>Solo Back / Esc on CHOOSE YOUR NATION: the fork screen (design 6.1), or the menu.</summary>
        void SoloBackToSetup()
        {
            if (Phase != CupPhase.NationPick) return;
            SetPhase(CupPhase.Ended);
            (OnBackToSetup ?? OnMainMenu)?.Invoke();
        }

        /// <summary>Close whatever screen the previous phase left (each Close destroys its object and unhooks).</summary>
        void CloseSoloScreens()
        {
            if (_soloPicker != null) { _soloPicker.Close(); _soloPicker = null; }
            if (_soloBracket != null) { _soloBracket.Close(); _soloBracket = null; }
            if (_soloLobby != null) { _soloLobby.Close(); _soloLobby = null; }
            if (_soloKnockedOut != null) { _soloKnockedOut.Close(); _soloKnockedOut = null; }
            if (_soloResults != null) { _soloResults.Close(); _soloResults = null; }
            EndPodium();
        }
    }
}
