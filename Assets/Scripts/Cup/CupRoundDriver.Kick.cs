using System;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The round driver's KICK CYCLE (design 7.2), shared by every style:
    ///
    ///   Intro (3 s card) -> Placing (taker to the spot + run-up, keeper to the line, lineups and
    ///   referee to marks; an AI taker walks in) -> WhistleRaise (0.4 s) -> Armed (the kick clock,
    ///   CupTuning.KickClock, then the weak auto-shot) -> Live (the attempt state machine both existing drivers
    ///   share) -> Verdict (GOAL / SAVED / MISS, recorded under the rules, 1.3 s hold so the ball
    ///   settles on the recording) -> Scored (the scorer's 5 s free window, scorer-only skip) or
    ///   WalkBack (the shooter walks to the lineup, up to 3.5 s) -> the replay of a goal or a save
    ///   (3 s window at 0.45, unanimous skip) -> Placing again, or, once the rules decide the
    ///   round, Decided (the winners' 5 s free beat / the losers' 4 s dejection, the referee's
    ///   full-time triple) -> the replay -> Over.
    ///
    /// A decisive kick goes Verdict -> Decided directly: the win beat IS its celebration window,
    /// and the replay follows the beat, so the results screen comes right after it.
    ///
    /// FREE KICKS (owner's call) differ in three places, penalties keep the design: (1) there is
    /// no lineup and nobody walks - every taker, human or AI, is placed straight at the run-up
    /// start, and the other bodies stand scattered behind him (CupRoundDriver.Scene.cs,
    /// AssignFreeKickMarks); (2) a miss / save has no walk-back: the WalkBack phase is a
    /// CupTuning.FreeKickMissBeat dejection on the shooter where he stands, then the cut; (3) a
    /// goal frees the WHOLE scoring side's humans for the scored window, not only the scorer
    /// (the skip stays the scorer's). The kick clock stops the instant the kick is taken: it is
    /// zeroed at the Live transition so no HUD or wire state can show a ring after the strike.
    ///
    /// Authority. Local and Host run the simulation below (Host also feeds remote humans from
    /// their wire input and hands the state to the director to broadcast). A Client runs none of
    /// it: bodies are puppets the net agent poses from snapshots, and ApplyState / OnStateApplied
    /// drive the local roles, the cameras and the HUD from the host's CupRoundState.
    /// </summary>
    public partial class CupRoundDriver
    {
        // The attempt state machine constants both dead-ball drivers use (FreeKickGame / NetSetPieceMatch).
        const float KickSpeed = 2.5f;     // ball speed that marks the kick as taken
        const float RestSpeed = 0.7f;     // ball considered stopped below this
        const float RestHold = 0.6f;      // seconds at rest before resolving
        const float MaxLiveTime = 6f;     // safety cap so an attempt always resolves
        // Cup-only beats that are not gameplay tuning (CupTuning holds the designed ones).
        const float PlacingSettle = 0.45f;    // a snapped body settles this long before the raise
        const float AiWalkTimeout = 8f;       // an AI walk-in that never arrives is cut to the spot
        // An attempt already under way at the clock's expiry (charging with a real hold, running up,
        // striking or settling) gets this long past the clock to resolve before the watchdog steps
        // in. It has to cover the slowest legal finish - a full-power release, the run-up and the
        // swing - or the watchdog would still cut off the very shot it is meant to be waiting for.
        // Generous on purpose: the cost of waiting is a beat of dead time, the cost of cutting in is
        // a scored goal wiped and replaced with a miss.
        const float AttemptGrace = 6f;
        const float WalkBackFallback = 1.4f;  // no choreography attached: the classic callout beat instead of a walk
        const float DecidedRaiseAt = 0.15f;   // the full-time raise starts this far into Decided

        // ---- per-kick / per-phase state ----
        bool _takerArmed;        // the SetPieceTaker is live for the current attempt
        bool _autoLaunched;      // the kick clock fired the weak auto-shot
        bool _liveWallTouched;   // a wall blocker touched the ball this attempt (reads SAVED)
        bool _goalLatched;       // the physics-rate sweep saw the ball cross the line inside the frame
        Vector3 _sweepPrev;      // the ball's position at the previous sweep sample
        bool _sweepValid;        // _sweepPrev holds a sample from this attempt
        float _liveTime, _restTimer;
        bool _aiWalkPending, _aiArrived;   // an AI taker is walking from the lineup to the spot
        bool _walkArrived;                 // the walk-back choreography reported arrival
        bool _ringFrozen;                  // the replay ring is held on the verdict (recorder disabled)
        bool _replayPending;               // a replay is owed after the open window / beat
        bool _replayThenOver;              // what follows the replay: Over (decisive kick) or Placing
        bool _skipRequested;               // the scorer / winning keeper clicked to skip the open window
        bool _choreoSkip;                  // Solo only: a click asked to cut a walking cinematic short
        bool _decidedRaised, _tripleBlown; // the full-time raise and triple whistle, once each
        float _decidedSeconds;             // how long the Decided beat lasts on this machine
        bool _clientPhaseDirty;            // Client: a host phase change arrived this apply
        bool _clientTakerDisplay;          // Client: the local taker's DISPLAY-ONLY SetPieceTaker is armed (the HUD's power meter)
        bool _placeFailed;                 // PlaceForKick found no body: abort on the next tick
        bool _replayEndRequested;          // the skip vote is unanimous: end the replay on the next sim tick
        int _kicksAnnounced;               // kicks whose callout + crowd have fired (a client catches up in OnStateApplied)
        bool _clientBegun;                 // Client: Begin's duties (the cursor, the intro card) done for this round
        readonly ChargeGate _chargeGate = new ChargeGate();   // the human taker's input, Space masked for the Begin call only

        /// <summary>
        /// A pass-through <see cref="IStrikerInput"/> whose charge button reads UP while
        /// <see cref="MaskHeld"/> is set - held for exactly the SetPieceTaker.Begin call, so an
        /// anticipating hold at the whistle is not mistaken for a stale press (see ArmTaker).
        /// Every other member is the inner source's, so the gate, the wire adapter and the
        /// debounce behave exactly as before.
        /// </summary>
        sealed class ChargeGate : IStrikerInput
        {
            public IStrikerInput Inner;
            public bool MaskHeld;
            public Vector2 Move => Inner != null ? Inner.Move : Vector2.zero;
            public float Scroll => Inner != null ? Inner.Scroll : 0f;
            public bool SprintHeld => Inner != null && Inner.SprintHeld;
            public bool CloseControlHeld => Inner != null && Inner.CloseControlHeld;
            public bool JumpPressed => !MaskHeld && Inner != null && Inner.JumpPressed;
            public bool JumpHeld => !MaskHeld && Inner != null && Inner.JumpHeld;
            public bool JumpReleased => !MaskHeld && Inner != null && Inner.JumpReleased;
            public bool LeftLegHeld => !MaskHeld && Inner != null && Inner.LeftLegHeld;
            public bool RightLegHeld => !MaskHeld && Inner != null && Inner.RightLegHeld;
            public bool ResetPressed => Inner != null && Inner.ResetPressed;
            public bool LeftClickPressed => Inner != null && Inner.LeftClickPressed;
            public bool RightClickPressed => Inner != null && Inner.RightClickPressed;
            public bool PassGroundPressed => Inner != null && Inner.PassGroundPressed;
            public bool PassLoftedPressed => Inner != null && Inner.PassLoftedPressed;
            public bool PassGroundHeld => Inner != null && Inner.PassGroundHeld;
            public bool PassLoftedHeld => Inner != null && Inner.PassLoftedHeld;
            public bool PassGroundReleased => Inner != null && Inner.PassGroundReleased;
            public bool PassLoftedReleased => Inner != null && Inner.PassLoftedReleased;
            public bool PassChipPressed => Inner != null && Inner.PassChipPressed;
            public bool PassChipHeld => Inner != null && Inner.PassChipHeld;
            public bool PassChipReleased => Inner != null && Inner.PassChipReleased;
            public bool Fresh => Inner == null || Inner.Fresh;
            public int EmoteId => Inner != null ? Inner.EmoteId : 255;
            public bool CrossPressed => Inner != null && Inner.CrossPressed;
            public bool ThirdLegHeld => Inner != null && Inner.ThirdLegHeld;
        }

        // ==========================================================================================
        // Lifecycle hooks
        // ==========================================================================================

        partial void OnBegin()
        {
            // Each round captures the cursor itself (the loading / coin screens left it free); a
            // spectator with no body keeps a free pointer for the lobby's Esc.
            if (Setup != null && Setup.LocalHasBody) { GameInput.CaptureCursor(true); _cursorCaptured = true; }
            ShowIntroCard();
        }

        partial void OnAbort()
        {
            if (!_sceneBuilt) return;
            if (ReplayPlaying) { ReplayPlaying = false; if (_replay != null && _replay.IsPlaying) _replay.Stop(); }
            if (_replay != null) _replay.enabled = true;
            _replayPending = false;
            _replayEndRequested = false;
            _ringFrozen = false;
            EndRoundVisuals();
        }

        /// <summary>
        /// Solo: cut a walking cinematic short (CupRoundDriver.SkipChoreography has already checked
        /// the style, authority and phase). Every walk in progress is LANDED on its mark first - the
        /// arrival callbacks run exactly as they would have - and only then is the beat ended, so
        /// nothing is left standing between two positions.
        /// </summary>
        partial void OnSkipChoreography()
        {
            if (Choreo != null) Choreo.LandAllWalks();
            // The phase ticks read this on their next pass and close out from wherever they are.
            _choreoSkip = true;
            if (Phase == RoundPhase.WalkBack) _walkArrived = true;
        }

        partial void OnSkipCelebration()
        {
            // Local / Host only (the skeleton refuses a Client): CanLocalSkip already said the local
            // player may; the open window closes on the next tick for everyone.
            _skipRequested = true;
        }

        partial void OnKickResolved(KickOutcome outcome, CupSide side, int scorerSlot)
        {
            // Local / Host: ResolveKick just recorded the kick. (ApplyState fires the public
            // KickResolved event but not this partial, so a client catches up in OnStateApplied.)
            AnnounceKick(outcome, side);
            _kicksAnnounced = Line != null ? Line.Count : _kicksAnnounced + 1;
        }

        /// <summary>
        /// The verdict's callout and crowd, once per kick on every machine. Terse words, as the
        /// accuracy modes settled on: the pips and the scoreboard already carry the round.
        /// </summary>
        void AnnounceKick(KickOutcome outcome, CupSide side)
        {
            Callout?.Invoke(CupText.Verdict(outcome));
            var audio = AudioManager.Instance;
            int key = _takerBody != null ? _takerBody.VirtualSlot : (int)side;
            if (audio == null) return;
            if (outcome == KickOutcome.Goal)
            {
                audio.OnSetPieceGoal(key);
                CrowdCheer.Celebrate();
            }
            else
            {
                audio.OnSetPieceMiss(key);
                if (outcome == KickOutcome.Miss) audio.PlayMissBoosMaybe();
            }
        }

        partial void OnPhaseChanged(RoundPhase prev, RoundPhase next)
        {
            if (!_sceneBuilt) return;
            if (Authority == RoundAuthority.Client)
            {
                // The host moved on; OnStateApplied (which follows in the same ApplyState) sets
                // roles and cameras once the body ids of the new phase are in.
                _clientPhaseDirty = true;
                return;
            }
            switch (next)
            {
                case RoundPhase.Intro:
                    // The ceremony may have walked the captains and the referee anywhere: back to
                    // the marks under the card, and the camera to a neutral follow.
                    ParkAllAtMarks();
                    Setup.Ball.ResetTo(BallSpotPos);
                    CamRelease();
                    break;
                case RoundPhase.Placing:
                    PlaceForKick();
                    break;
                case RoundPhase.WhistleRaise:
                    if (_ref != null) _ref.RaiseWhistle();
                    break;
                case RoundPhase.Armed:
                    ArmTaker();
                    break;
                case RoundPhase.Live:
                    // The kick is taken: the clock stops HERE, not when the phase machine next
                    // looks at it. The skeleton's SetPhase leaves the remaining seconds untouched
                    // on the Armed -> Live edge and the HUD ring keys off Phase == Armed, but the
                    // wire state carries the number too - zeroed, nothing downstream can ever show
                    // a ring or a countdown on a ball that is already in flight.
                    SetKickClock(0f);
                    _save.Arm();
                    _liveWallTouched = false;
                    _goalLatched = false;
                    _sweepPrev = Setup.Ball.transform.position;
                    _sweepValid = true;
                    _liveTime = _restTimer = 0f;
                    if (_wall != null) _wall.TriggerJump();
                    break;
                case RoundPhase.Verdict:
                    SetWindows(false, false, false);
                    break;
                case RoundPhase.Scored:
                    OpenScoredWindow();
                    break;
                case RoundPhase.WalkBack:
                    BeginWalkBack();
                    break;
                case RoundPhase.Decided:
                    BeginDecided();
                    break;
                case RoundPhase.Over:
                    EndRoundVisuals();
                    break;
            }
        }

        partial void OnTick(float dt)
        {
            if (!_sceneBuilt) return;
            if (PauseMenu.Frozen)
            {
                // A Solo pause stops time: hold the replay too (its clock is unscaled), or it plays
                // on under the menu the way every older mode's does.
                if (ReplayPlaying && _replay != null && _replay.enabled) _replay.enabled = false;
                return;
            }
            if (ReplayPlaying && _replay != null && !_replay.enabled && !_ringFrozen) _replay.enabled = true;

            switch (Authority)
            {
                case RoundAuthority.Client: ClientTick(dt); break;
                case RoundAuthority.Host: HostTick(dt); break;
                default: LocalTick(dt); break;
            }
        }

        // ==========================================================================================
        // Authority ticks
        // ==========================================================================================

        /// <summary>Solo, and Head to Head rounds against AI: this machine owns everything.</summary>
        void LocalTick(float dt) => SimTick(dt);

        /// <summary>
        /// The host of a networked round: the SAME simulation as Local, with every remote human's
        /// input adapter refreshed from the session first, then the wire (CupRoundDriver.Net.cs):
        /// the snapshot over _bodies (VirtualSlot, pelvis, facing yaw, emote id + phase) at the
        /// snapshot interval, BuildState() on every phase / kick edge and every half second, and
        /// the replay bridged onto the session's ReplayStart / SkipVote / ReplayEnd messages.
        /// Runs at every phase, Over included: the Co-op trophy lift moves the round's bodies
        /// after the round is over and its remote humans keep moving in the free window.
        /// </summary>
        void HostTick(float dt)
        {
            FeedRemoteInputs();
            SimTick(dt);
            NetHostTick(dt);
        }

        /// <summary>
        /// A client simulates nothing: the local input goes out and the host's snapshots are posed
        /// onto the puppets (CupRoundDriver.Net.cs); cameras and roles react in OnStateApplied; the
        /// emote wheel and the skip clicks reach the host through the director's requests.
        /// </summary>
        void ClientTick(float dt)
        {
            NetClientTick(dt);
            ClientTickDisplayTaker();
        }

        // ---- the client taker's power meter -----------------------------------------------------

        /// <summary>
        /// Client: arm or stand down the local taker's DISPLAY-ONLY SetPieceTaker with the host's
        /// state (the NetSetPieceMatch.ClientDriveTaker idiom). The real charge, run-up and strike
        /// happen on the host from this peer's wire input, and the local body is a puppet of the
        /// host's snapshots - but the HUD reads <see cref="Taker"/> for the power meter and the
        /// clock ring, and a client taker used to charge blind. The display taker runs the same
        /// meter off the same gated device (Space masked for the Begin call exactly as ArmTaker
        /// does, so a hold at the whistle sweeps from the whistle), never launches the ball
        /// (displayOnly) and never fights the puppet: DisplaySnap re-poses it every frame from the
        /// snapshot, and its MoveInput / pose writes land on kinematic bones. Armed while the
        /// host's phase is Armed and the local player takes; reset the moment either stops.
        /// </summary>
        void ClientSyncDisplayTaker(bool wantArmed)
        {
            if (Authority != RoundAuthority.Client) return;
            if (!wantArmed)
            {
                if (_clientTakerDisplay) { _taker.Reset(); _clientTakerDisplay = false; }
                return;
            }
            if (_clientTakerDisplay) return;
            var body = LocalBody;
            var s = Setup;
            if (body == null || !body.Alive || body.IsKeeperBody || s == null || s.Ball == null || _localGate == null) return;
            Vector3 spot = BallSpotPos;
            Vector3 goal = SimConfig.AttackGoalCenter;
            Func<Vector3> aim = s.GameCam != null
                ? () => SetPieceTaker.LookAimPoint(spot, s.GameCam.Yaw, s.GameCam.Pitch, goal.z)
                : (Func<Vector3>)null;
            _chargeGate.Inner = _localGate;
            _chargeGate.MaskHeld = true;
            _taker.Begin(_chargeGate, body.Ragdoll, s.Ball, spot, goal,
                         displayOnly: true, combinedOverride: -1f, aimPoint: aim, leftFootedOverride: -1);
            _chargeGate.MaskHeld = false;
            _clientTakerDisplay = true;
        }

        /// <summary>Client: tick the display taker (the meter sweeps, the release plays the swing on the puppet's pose writes, nothing launches).</summary>
        void ClientTickDisplayTaker()
        {
            if (!_clientTakerDisplay) return;
            if (Phase != RoundPhase.Armed || !LocalIsTaker) { _taker.Reset(); _clientTakerDisplay = false; return; }
            _taker.Tick();
        }

        /// <summary>The kick cycle, one frame.</summary>
        void SimTick(float dt)
        {
            if (ReplayPlaying)
            {
                if (_replayEndRequested || _replay == null || !_replay.IsPlaying) EndReplay();
                return;
            }
            switch (Phase)
            {
                case RoundPhase.Idle:
                    if (_ref != null) _ref.Tick();   // the ceremony walks him; between walks he stands clean
                    break;
                case RoundPhase.Intro:
                    if (_ref != null) _ref.Tick();
                    if (PhaseTime >= CupTuning.IntroSeconds) SetPhase(RoundPhase.Placing);
                    break;
                case RoundPhase.Placing: TickPlacing(dt); break;
                case RoundPhase.WhistleRaise: TickWhistleRaise(dt); break;
                case RoundPhase.Armed: TickArmed(dt); break;
                case RoundPhase.Live: TickLive(dt); break;
                case RoundPhase.Verdict: TickVerdict(dt); break;
                case RoundPhase.Scored: TickScored(dt); break;
                case RoundPhase.WalkBack: TickWalkBack(dt); break;
                case RoundPhase.Decided: TickDecided(dt); break;
                case RoundPhase.Over: break;
            }
        }

        // ==========================================================================================
        // Placing
        // ==========================================================================================

        /// <summary>
        /// The cut before a kick: resolve who takes and who keeps, swap a human's twin bodies,
        /// move the ball (and, in Free Kicks, the wall) to this pair's spot, everyone else to
        /// their marks, the referee to his, the cameras to the local role. Penalties: the marks
        /// are the lineup, and an AI taker starts from it and walks in when the choreography is
        /// attached (design 7.3) while a human is placed straight at the run-up start. Free
        /// Kicks: the marks are this kick's scatter behind the taker (AssignFreeKickMarks) and
        /// EVERY taker is placed straight at the run-up start - nobody walks.
        /// </summary>
        void PlaceForKick()
        {
            var s = Setup;
            var kicker = Kicker;
            var keeping = CupSides.Other(kicker);

            // This pair's spot (Free Kicks change it once both sides have shot).
            int pair = CupRoundRules.PairIndex(Line);
            Vector3 spot = _spots.SpotFor(s.Format, pair);
            if (_spotMarker != null) _spotMarker.position = spot;
            SetBallSpot(_spotMarker, spot, s.Format == CupFormat.FreeKicks ? pair : 0);
            if (_wall != null && pair != _wallPair) RebuildWallFor(pair, spot);
            bool freeKicks = s.Format == CupFormat.FreeKicks;

            // The stage ramp, written per kick (a mode that ran before us may have left its own).
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(s.Stage);

            _takerBody = TakerBodyFor(kicker);
            _keeperBody = KeeperBodyFor(keeping);
            if (_takerBody == null || _keeperBody == null)
            {
                // Latched, not Abort() here: this runs inside SetPhase(Placing), and an Abort would
                // nest a second SetPhase whose Over event fired BEFORE the outer Placing event.
                CupLog.Error("CupRoundDriver.PlaceForKick: no taker or keeper body for kick " + (KickIndex + 1) + " - aborting the round");
                _placeFailed = true;
                return;
            }

            // Free Kicks: this kick's scatter marks (the spot moved, or the other side is on the
            // ball now, so the groups swap ends). Every non-playing body reads its mark below.
            if (freeKicks) AssignFreeKickMarks(pair, spot, kicker);

            // Reset the attempt machinery before anyone moves.
            _takerArmed = false;
            _autoLaunched = false;
            _taker.Reset();
            _save.Disarm();
            _skipRequested = false;
            _choreoSkip = false;
            _aiWalkPending = _aiArrived = false;
            _walkArrived = false;
            SetWindows(false, false, false);

            // Roles and twin visibility. A twin pair is one person: the shooter body is out on
            // own kicks, the keeper body on opponent kicks; the other hides behind the goal.
            int hide = 0;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Role == CupBodyRole.Referee) continue;
                if (b == _takerBody) b.Role = CupBodyRole.Taker;
                else if (b == _keeperBody) b.Role = CupBodyRole.Keeper;
                else b.Role = CupBodyRole.Lineup;

                bool park = b.Gone || (IsTwinKeeper(b) && b.Side == kicker) || (IsTwinShooter(b) && b.Side != kicker);
                if (park) { if (!b.Parked || !b.Gone) CupBodies.Park(b, CupSpots.HideSpot(hide++), s.Ball); continue; }

                if (b == _takerBody)
                {
                    // Penalties: an AI taker walks in from the lineup (design 7.3). Free Kicks:
                    // nobody walks - every taker, human or AI, starts AT the ball, on the run-up
                    // start behind the spot (owner's call). A human is always placed straight there.
                    bool walkIn = !b.IsHuman && Choreo != null && !freeKicks;
                    if (walkIn)
                    {
                        // From the lineup, on foot; the choreography reports arrival.
                        CupBodies.Unpark(b, b.LineupMark, b.LineupFacing, s.Ball);
                        _aiWalkPending = true;
                    }
                    else
                    {
                        CupBodies.Unpark(b, CupSpots.RunUpStart(spot), CupSpots.FacingGoal(spot), s.Ball);
                    }
                }
                else if (b == _keeperBody)
                {
                    // ResetHumanKeepers / Goalkeeper.ResetTo: unlocked, recovered, on the line, facing out.
                    CupBodies.Unpark(b, CupSpots.KeeperLine(s.Format), CupSpots.KeeperFacing, s.Ball);
                    if (b.Keeper != null) b.Keeper.InputLocked = false;
                }
                else
                {
                    CupBodies.Unpark(b, b.LineupMark, b.LineupFacing, s.Ball);
                }
            }

            // The ball: dead on the spot, ignoring the taker for the whole attempt (the taker owns
            // it and arms on a later frame - a collidable taker body could graze the dead ball and
            // fire a contact-point strike that ignores the player's aim; see NetSetPieceMatch.BeginTurn)
            // and the referee always.
            if (_wall != null) _wall.Ground();
            s.Ball.ResetTo(spot);
            s.Ball.IgnoreBody(_takerBody.Ragdoll, true);
            if (_ref != null)
            {
                _ref.SetMark(CupSpots.RefereeMark(spot), CupSpots.RunUpStart(spot));
                s.Ball.IgnoreBody(_ref.Body, true);
            }

            // Who the local player is this kick, and their camera.
            bool localTaker = _takerBody.IsHuman && _takerBody.Slot == s.LocalSlot;
            bool localKeeper = _keeperBody.IsHuman && _keeperBody.Slot == s.LocalSlot;
            bool localLineup = s.LocalHasBody && !localTaker && !localKeeper;
            SetLocalRoles(localTaker, localKeeper, localLineup);
            SetBodies(_takerBody.Ragdoll, _keeperBody.Ragdoll);
            RefreshLocalBody();
            CamForLocalRole();

            // The lineups take their pose; an AI taker sets off.
            if (Choreo != null)
            {
                Choreo.OnPlacing(this);
                if (_aiWalkPending) Choreo.OnAiTakerToSpot(this, _takerBody, () => _aiArrived = true);
            }
        }

        void TickPlacing(float dt)
        {
            if (_placeFailed) { _placeFailed = false; Abort(); return; }
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, false);
            if (_aiWalkPending)
            {
                // A single-player click cuts the walk short: fall through to the snap below, which
                // plants him on the run-up start exactly as an arrival would.
                if (_choreoSkip) { _choreoSkip = false; }
                else if (!_aiArrived && PhaseTime < AiWalkTimeout) return;
                // Arrived (or the walk never finished): plant him on the run-up start exactly, so
                // the run-in always starts from the authored distance.
                _aiWalkPending = false;
                CupBodies.Stand(_takerBody, CupSpots.RunUpStart(BallSpotPos), CupSpots.FacingGoal(BallSpotPos), Setup.Ball);
                Setup.Ball.IgnoreBody(_takerBody.Ragdoll, true);
                return;   // one settle frame after the snap
            }
            if (PhaseTime >= PlacingSettle || _choreoSkip)
            {
                _choreoSkip = false;
                SetPhase(RoundPhase.WhistleRaise);
            }
        }

        // ==========================================================================================
        // Whistle raise -> Armed -> Live
        // ==========================================================================================

        void TickWhistleRaise(float dt)
        {
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, false);
            if (_wall != null) _wall.Tick();
            if (PhaseTime >= CupTuning.WhistleRaiseSeconds)
            {
                // The whistle audio fires at the END of the raise (the emote holds the hand at the
                // mouth through it and drops it CupTuning.WhistleHoldAfter later on its own).
                AudioManager.Instance?.PlayWhistle();
                SetPhase(RoundPhase.Armed);
            }
        }

        /// <summary>
        /// Arm the taker for this kick. A human is driven by their own input (the gated local
        /// device, or on the host their wire input) with the look-ray aim (LookAimPoint off the
        /// camera yaw / pitch - the penalty camera keeps those as the aim source) and
        /// combinedOverride -1: with SkillTree.MaxShootingOverride on for the whole cup, -1 IS the
        /// standardised ceiling (design 2.6). An AI taker is the bot with the stage's combined stat
        /// and BallController's corner auto-aim (aimPoint null).
        /// </summary>
        void ArmTaker()
        {
            var s = Setup;
            var tb = _takerBody;
            if (tb == null || !tb.Alive) return;
            Vector3 spot = BallSpotPos;
            Vector3 goal = SimConfig.AttackGoalCenter;

            if (tb.IsHuman)
            {
                bool local = tb.Slot == s.LocalSlot;
                IStrikerInput src = local ? (IStrikerInput)_localGate : tb.NetInput;
                if (src == null) { CupLog.Error("CupRoundDriver.ArmTaker: no input for slot " + tb.Slot); AutoLaunch(); return; }
                var net = tb.NetInput;
                Func<Vector3> aim;
                if (local && s.GameCam != null)
                    aim = () => SetPieceTaker.LookAimPoint(spot, s.GameCam.Yaw, s.GameCam.Pitch, goal.z);
                else if (net != null)
                    aim = () => SetPieceTaker.LookAimPoint(spot, net.LookYaw, net.LookPitch, goal.z);
                else
                    aim = null;
                // Footedness is not on the wire for a remote shooter beyond the session's flag.
                int footed = local ? -1 : (Multiplayer.Session != null && Multiplayer.Session.LeftFootedForSlot(tb.Slot) ? 1 : 0);
                // A Space already DOWN at the whistle is a real charge here, not a stale press:
                // the cup gives the taker a visible get-ready beat (the placement settle and the
                // referee's raise) that a player naturally holds Space through, waiting for the
                // whistle. SetPieceTaker.Begin would latch its stale-actuation guard on that hold
                // (_awaitingRelease): the meter never moves, the release commits NOTHING, and the
                // player's second press is the only charge that counts - which, missed, left the
                // clock to run out and the weak auto-shot to take the kick. The guard exists for a
                // key carried in from a menu, which no kick here can inherit (a whistle is never
                // less than a full beat away from the last thing Space did), so Begin is handed a
                // one-call mask that reads Space UP; the very next tick reads the real hold and the
                // meter starts sweeping from the whistle.
                _chargeGate.Inner = src;
                _chargeGate.MaskHeld = true;
                _taker.Begin(_chargeGate, tb.Ragdoll, s.Ball, spot, goal,
                             displayOnly: false, combinedOverride: -1f, aimPoint: aim, leftFootedOverride: footed);
                _chargeGate.MaskHeld = false;
            }
            else
            {
                if (tb.Bot == null) tb.Bot = new CupBotTaker(_botRng);
                tb.Bot.Arm(_taker, s.Stage);   // BEFORE Begin, so Begin reads Space up
                _taker.Begin(tb.Bot, tb.Ragdoll, s.Ball, spot, goal,
                             displayOnly: false, combinedOverride: CupTuning.TakerCombined(s.Stage), aimPoint: null,
                             leftFootedOverride: tb.Bot.LeftFooted ? 1 : 0);
            }
            _takerArmed = true;
            _autoLaunched = false;
        }

        void TickArmed(float dt)
        {
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, false);
            TickTaker(dt);
            if (_wall != null) _wall.Tick();

            // The kick has been taken when the ball picks up pace (the other set-piece modes' test),
            // OR the taker reports the strike, OR the ball has left the spot: belt and braces, because
            // a launch the speed test alone did not see left the round Armed with the clock running
            // and the weak auto-shot then wiped the real result (reported in free kicks).
            bool struck = _takerArmed && _taker.Phase == SetPieceTaker.State.Struck;
            bool moved = (Setup.Ball.transform.position - BallSpotPos).sqrMagnitude > 0.09f;   // > 30 cm off the spot
            if (Setup.Ball.Speed > KickSpeed || struck || moved)
            {
                SetPhase(RoundPhase.Live);
                return;
            }

            // The kick clock (design 2.1): CupTuning.KickClock from the whistle, then the weak auto-shot.
            //
            // A SHOT ALREADY UNDER WAY IS NEVER CUT OFF. AutoLaunch resets the ball to the spot and
            // fires its own weak shot, so firing it over a live attempt destroys the real kick - a
            // ball on its way in is scored as the substitute's miss. `AttemptInFlight` covers the
            // whole attempt including a genuine charge, because a player who releases on the last
            // tick of the clock is still Charging on the frame it reaches zero, with the strike a
            // frame or two away. Only a taker who never engaged is timed out at zero; anyone mid
            // attempt gets AttemptGrace to resolve, and the ball's own flight is then judged by the
            // Live phase (which has its own approach test and CupTuning.LiveHardCap backstop).
            if (!_autoLaunched && KickClockRemaining <= 0f)
            {
                bool inFlight = _takerArmed && _taker.AttemptInFlight;
                if (!inFlight || PhaseTime >= CupTuning.KickClock + AttemptGrace) AutoLaunch();
            }
        }

        /// <summary>The bot's clock, then the taker (the taker keeps ticking its follow-through / settle after the strike until Done).</summary>
        void TickTaker(float dt)
        {
            if (!_takerArmed) return;
            var tb = _takerBody;
            if (tb != null && tb.Bot != null) tb.Bot.Tick(dt);
            _taker.Tick();
            if (_taker.Done && Phase != RoundPhase.Armed) _takerArmed = false;
        }

        /// <summary>
        /// The idle / stuck watchdog's shot (NetSetPieceMatch.AutoLaunch): a weak, seeded-spin
        /// scripted launch from the spot, the taker stood down so nothing else touches the ball.
        /// </summary>
        void AutoLaunch()
        {
            var tb = _takerBody;
            var ball = Setup.Ball;
            _autoLaunched = true;
            _takerArmed = false;
            if (tb != null && tb.Bot != null) tb.Bot.Disarm();
            _taker.Reset();   // restores ball<->body collision and the state; re-ignored below
            if (tb != null && tb.Alive)
            {
                tb.Ragdoll.MoveInput = Vector3.zero;
                tb.Ragdoll.ClearPoseOverrides();
                tb.Ragdoll.UprightLock = true;
                ball.IgnoreBody(tb.Ragdoll, true);
            }
            float combined = tb != null && tb.IsHuman ? 0.6f : CupTuning.TakerCombined(Setup.Stage);
            var spins = new[] { BallController.SetPieceSpin.None, BallController.SetPieceSpin.CurveLeft,
                                BallController.SetPieceSpin.CurveRight, BallController.SetPieceSpin.TopSpin };
            var spin = spins[_botRng.Range(0, spins.Length)];
            ball.ResetTo(BallSpotPos);
            // Never overpowers the bar (overcharge 0); its power stat tracks its competence.
            ball.LaunchSetPiece(CupTuning.AutoLaunchPower, spin, _botRng.Range(0.4f, 0.9f), 0f,
                                Mathf.Clamp01(combined), SimConfig.AttackGoalCenter, 0f, Mathf.Clamp01(combined));
        }

        /// <summary>
        /// Physics-rate goal sweep while the ball is live on the authority. Update-rate sampling
        /// alone lets a fast shot enter the net and bounce back out between two rendered frames
        /// (a goal read as a miss), so the crossing is latched here from consecutive physics
        /// positions and TickLive consumes the latch.
        /// </summary>
        void FixedUpdate()
        {
            if (!Configured || Phase != RoundPhase.Live || Authority == RoundAuthority.Client) return;
            if (Setup == null || Setup.Ball == null) return;
            Vector3 c = Setup.Ball.transform.position;
            if (_sweepValid && !_goalLatched && CupSpots.CrossedGoalLine(_sweepPrev, c)) _goalLatched = true;
            _sweepPrev = c;
            _sweepValid = true;
        }

        void TickLive(float dt)
        {
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, false);
            TickTaker(dt);
            if (_wall != null) _wall.Tick();

            var ball = Setup.Ball;
            _liveTime += dt;
            Vector3 c = ball.transform.position;

            // Keeper contact from the ball's touch log (real PhysX contacts), so a fast shot cannot
            // slip between two frames of a proximity check.
            _save.Poll(ball, _keeperBody != null ? _keeperBody.Ragdoll : null, KeeperHighDive());
            if (!_liveWallTouched && WallContactedBall()) _liveWallTouched = true;

            // Goal: the physics-rate sweep (FixedUpdate) latched a line crossing inside the frame.
            // The rendered-frame sweep below is a second sample of the same test, never the box
            // test: a net rebound between two frames read as a miss and a ball dropping behind the
            // net after clearing the bar read as a goal under the old per-frame "inside the goal
            // box" check (design 2.1 wants the whole ball over the line, inside the frame).
            // ...and the same per-frame "whole ball inside the goal" box test every other set-piece
            // mode uses (owner's call: identical goal determination to those modes), so a ball that
            // settles in the net is a goal by either reading.
            if (_goalLatched || (_sweepValid && CupSpots.CrossedGoalLine(_sweepPrev, c)) || CupSpots.BallFullyInGoal(c))
            {
                _goalLatched = true;
                Verdict(KickOutcome.Goal);
                return;
            }
            _sweepPrev = c;
            _sweepValid = true;

            // Bound for the goal: not yet fully over the line and still moving toward it faster
            // than a creep. Neither the rest hold nor the 6 s cap may call a verdict on such a
            // ball - a slow roll toward the line used to be called a MISS a metre short of it,
            // because RestSpeed (0.7) sits above the pace a ball still covers ground at. The roll
            // dies for good below SimConfig.BallRollStop (BallController zeroes it), so "clearly
            // stopped" means exactly that; a ball still rolling is given until it stops, crosses
            // or leaves play, under one unconditional cap so a held or jittering ball never
            // stalls the round.
            float speed = ball.Speed;
            Vector3 vel = ball.Rb != null ? ball.Rb.linearVelocity : Vector3.zero;
            bool approaching = c.z - SimConfig.BallRadius < SimConfig.GoalCenter.z
                               && vel.z > CupTuning.LiveApproachSpeed
                               && speed > CupTuning.LiveApproachSpeed;
            if (speed < RestSpeed) _restTimer += dt; else _restTimer = 0f;
            bool stopped = _restTimer > RestHold && !approaching;
            bool timedOut = (_liveTime > MaxLiveTime && !approaching) || _liveTime > CupTuning.LiveHardCap;
            if (CupSpots.BallOutOfPlay(c) || stopped || timedOut)
            {
                // Verdict order: goal, then a keeper touch, then a wall touch, then a miss. A wall
                // stop reads SAVED too (design 2.1), never "blocked".
                Verdict(_save.Touched || _liveWallTouched ? KickOutcome.Saved : KickOutcome.Miss);
            }
        }

        bool KeeperHighDive()
        {
            var k = _keeperBody;
            if (k == null) return false;
            if (k.Ai != null) return k.Ai.WasDivingSave;
            if (k.Keeper != null) return k.Keeper.IsHighDive;
            return false;
        }

        bool WallContactedBall()
        {
            if (_wall == null || !_wall.HasBlockers) return false;
            Vector3 bp = Setup.Ball.transform.position;
            var blockers = _wall.Blockers;
            for (int i = 0; i < blockers.Count; i++)
            {
                var go = blockers[i];
                if (go == null) continue;
                var col = go.GetComponent<Collider>();
                if (col == null) continue;
                if (Vector3.Distance(col.ClosestPoint(bp), bp) < SimConfig.BallRadius + 0.05f) return true;
            }
            return false;
        }

        // ==========================================================================================
        // Verdict and what follows
        // ==========================================================================================

        /// <summary>Record the kick under the rules (ResolveKick fires the events + the callout) and hold on the recording.</summary>
        void Verdict(KickOutcome outcome)
        {
            _lastTaker = _takerBody;
            _lastKeeper = _keeperBody;
            int scorer = outcome == KickOutcome.Goal && _takerBody != null && _takerBody.IsHuman ? _takerBody.Slot : -1;
            if (_takerBody != null && _takerBody.Bot != null) _takerBody.Bot.Disarm();
            ResolveKick(outcome, scorer);
            SetPhase(RoundPhase.Verdict);
        }

        void TickVerdict(float dt)
        {
            if (_ref != null) _ref.Tick();
            // A human keeper keeps ticking through the hold with his input locked, so a dive he was
            // mid-way through lands and he stands up; the taker plays its follow-through out.
            TickKeeperOnLine(dt, true);
            TickTaker(dt);
            if (_wall != null) _wall.Tick();

            if (PhaseTime < SimConfig.ReplayHold) return;   // the ball settles on the recording first

            // Hold the replay ring on this moment: a goal or a save replays AFTER the window (design
            // 2.1), and the window would otherwise scroll the kick out of a 3 s buffer.
            _replayPending = GameplaySettings.Replays && LastOutcome.HasValue && LastOutcome.Value != KickOutcome.Miss && _replay != null;
            if (_replayPending && _replay != null) { _replay.enabled = false; _ringFrozen = true; }

            if (IsDecided) { _replayThenOver = true; SetPhase(RoundPhase.Decided); }
            else if (LastOutcome == KickOutcome.Goal) { _replayThenOver = false; SetPhase(RoundPhase.Scored); }
            else { _replayThenOver = false; SetPhase(RoundPhase.WalkBack); }
        }

        // ---- the scorer's window (7.4) ----

        void OpenScoredWindow()
        {
            var b = _lastTaker;
            _skipRequested = false;
            if (b != null) CupBodies.Free(b);

            // Free Kicks (owner's call): the WHOLE scoring side's humans get the window -
            // locomotion and the wheel - not only the scorer; the AI side stays put in its
            // scatter. Every active, visible human body on the scorer's side that is not the
            // keeper of this kick (there is none on the kicking side by construction). Penalties
            // keep the lineup standing arm in arm, scorer only. The SKIP stays the scorer's.
            bool teamWindow = Setup.Format == CupFormat.FreeKicks && b != null;
            if (teamWindow)
            {
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var t = _bodies[i];
                    if (t == b || t.Role == CupBodyRole.Referee || !t.IsHuman || !t.Active || t.Parked || t == _keeperBody) continue;
                    if (t.Side == b.Side) CupBodies.Free(t);
                }
            }

            bool localScorer = LastScorerSlot >= 0 && LastScorerSlot == Setup.LocalSlot;
            SetWindows(true, false, localScorer);
            if (b != null) CamHold(b);
            // A freed teammate leaves the fixed look cone for a following view of his own body,
            // like the scorer; the keeper and a spectator keep their cameras.
            var local = LocalBody;
            if (teamWindow && local != null && local != b && local.Freed) CamHold(local);
            if (Choreo != null) Choreo.OnScored(this, b);
        }

        void TickScored(float dt)
        {
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, true);
            TickTaker(dt);
            TickFreedBodies(dt);
            if (PhaseTime >= CupTuning.ScoredWindow || _skipRequested) CloseWindowAndContinue();
        }

        // ---- the walk-back (7.5), or in Free Kicks the miss beat ----

        /// <summary>
        /// Penalties: the beaten shooter walks to his lineup slot under the rig's two-shot.
        /// Free Kicks (owner's call): no walk - he stands the taker down and plays one of the
        /// dejection trio where he is (a seeded variant, the losing-miss set) for
        /// CupTuning.FreeKickMissBeat, then the cut. His camera holds (the Follow view is already
        /// on him; a keeping human keeps his own).
        /// </summary>
        void BeginWalkBack()
        {
            _walkArrived = false;
            SetWindows(false, false, false);
            if (Choreo == null || _lastTaker == null) return;
            if (Setup.Format == CupFormat.FreeKicks)
            {
                // The swing's settle (ClearPoseOverrides every frame) would fight the emote for
                // its first frames: the attempt is over, so the taker is stood down first. Reset
                // hands the dead ball its collision back, which is harmless with the ball away.
                _takerArmed = false;
                _taker.Reset();
                Choreo.OnMissDeject(this, _lastTaker);
                return;
            }
            Choreo.OnWalkBack(this, _lastTaker, () => _walkArrived = true);
        }

        void TickWalkBack(float dt)
        {
            if (_ref != null) _ref.Tick();
            TickKeeperOnLine(dt, true);
            TickTaker(dt);
            bool done;
            if (Choreo == null) done = PhaseTime >= WalkBackFallback;   // no choreography: the classic callout beat
            // Free Kicks has no walk-back: this phase is the 3 s dejection on the spot, which is a
            // performance rather than a transit, so a Solo click does not cut it (CanSkipChoreography
            // excludes it too - this test only has to leave the beat alone).
            else if (Setup.Format == CupFormat.FreeKicks) done = PhaseTime >= CupTuning.FreeKickMissBeat;
            else done = _walkArrived || PhaseTime >= CupTuning.WalkBackMax;
            if (done) { _choreoSkip = false; CloseWindowAndContinue(); }
        }

        /// <summary>Close whatever window was open, then the owed replay or the next placement cut.</summary>
        void CloseWindowAndContinue()
        {
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].Freed) CupBodies.Hold(_bodies[i]);
            SetWindows(false, false, false);
            _skipRequested = false;
            if (_replayPending) StartReplay();
            else SetPhase(RoundPhase.Placing);
        }

        // ---- the replay (2.1) ----

        void StartReplay()
        {
            _replayPending = false;
            if (_replay == null) { AfterReplay(); return; }
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].Freed) CupBodies.Hold(_bodies[i]);
            if (_keeperBody != null && _keeperBody.Keeper != null) _keeperBody.Keeper.InputLocked = true;
            _skipVoted.Clear();
            ReplaySkipVotes = 0;
            ReplaySkipNeeded = CountHumansWithBodies();

            // THE COMPONENT MUST STAY DISABLED UNTIL Play() HAS RUN. The ring was frozen at the
            // verdict (recorder off) precisely so it still holds the kick after the 5 s scored
            // window. Re-enabling it here - as this used to - restarts FixedUpdate recording into
            // that same ring for the frames before Play(), appending stills of the aftermath and,
            // over a long enough window, evicting the goal from the head of a 150-frame buffer.
            // That is why the replay ran but never showed the goal, in every style.
            //
            // Play() sets _recording = false itself and Stop() sets it back, so the recorder only
            // needs to be live again AFTER playback; EndReplay re-enables the component.
            _replay.Play(CupTuning.ReplaySlow);
            if (!_replay.IsPlaying)
            {
                // Too few frames buffered: nothing to show. Hand the recorder back before leaving.
                _replay.enabled = true;
                _ringFrozen = false;
                AfterReplay();
                return;
            }
            // Playing: the component ticks its own Update-driven playback, so it must be enabled
            // now, but Play() has already latched recording off - nothing more reaches the ring.
            _replay.enabled = true;
            _ringFrozen = false;
            ReplayPlaying = true;
            CamReplay();
            Callout?.Invoke(CupText.ReplayFlash);
        }

        void EndReplay()
        {
            if (_replay != null && _replay.IsPlaying) _replay.Stop();
            if (_replay != null) _replay.enabled = true;
            _ringFrozen = false;
            _replayEndRequested = false;
            ReplayPlaying = false;
            _skipVoted.Clear();
            AfterReplay();
        }

        void AfterReplay()
        {
            if (_replayThenOver) SetPhase(RoundPhase.Over);
            else SetPhase(RoundPhase.Placing);
        }

        // ---- decided: the win beat / the dejection (7.6, 7.7) ----

        void BeginDecided()
        {
            var w = Winner;
            var winner = w ?? CupSide.A;
            _skipRequested = false;
            _decidedRaised = _tripleBlown = false;
            // The beat is the winners' 5 s where a human won, the losers' 4 s otherwise (a human
            // vs human round on the host runs the longer one; the losers deject and then stand).
            _decidedSeconds = AnyHumanOn(winner) ? CupTuning.WinBeat : CupTuning.DejectionBeat;

            // Winners run free: every ACTIVE human body on the winning side, the scorer included.
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Role == CupBodyRole.Referee || !b.Active || b.Parked) continue;
                if (b.Side == winner && b.IsHuman) CupBodies.Free(b);
            }
            bool localCanSkip = MaySkipLocalWinBeat(winner);
            SetWindows(false, true, localCanSkip);

            var local = LocalBody;
            if (local != null) CamHold(local);

            if (Choreo != null)
            {
                Choreo.OnRoundDecided(this, winner);
                Choreo.OnWinBeat(this, winner);
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var b = _bodies[i];
                    if (b.Role == CupBodyRole.Referee || !b.Active || b.Parked || b.Side == winner) continue;
                    Choreo.OnLoseBeat(this, b);
                }
            }

            var audio = AudioManager.Instance;
            if (audio != null)
            {
                if (Setup.LocalHasBody && Setup.LocalSide == winner) { audio.PlayGoalCelebration(); CrowdCheer.Celebrate(); }
                else audio.PlayApplauseOnly();
            }
        }

        bool MaySkipLocalWinBeat(CupSide winner)
        {
            int slot = Setup.LocalSlot;
            if (slot < 0) return false;
            if (LastScorerSlot == slot) return true;
            return LastOutcome.HasValue && LastOutcome.Value != KickOutcome.Goal && _lastKeeper != null
                   && _lastKeeper.IsHuman && _lastKeeper.Slot == slot && _lastKeeper.Side == winner;
        }

        void TickDecided(float dt)
        {
            if (_ref != null) _ref.Tick();
            // Full time: one raise, then the triple (design 7.1).
            if (!_decidedRaised && PhaseTime >= DecidedRaiseAt)
            {
                _decidedRaised = true;
                if (_ref != null) _ref.RaiseWhistle();
            }
            if (!_tripleBlown && PhaseTime >= DecidedRaiseAt + CupTuning.WhistleRaiseSeconds)
            {
                _tripleBlown = true;
                AudioManager.Instance?.PlayWhistleTriple();
            }
            TickTaker(dt);
            // The winning keeper is freed (his controller ticks unlocked); a losing keeper holds.
            TickKeeperOnLine(dt, !(_keeperBody != null && _keeperBody.Freed));
            TickFreedBodies(dt);
            if (_wall != null) _wall.Tick();

            if (PhaseTime >= _decidedSeconds || _skipRequested)
            {
                for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].Freed) CupBodies.Hold(_bodies[i]);
                SetWindows(false, false, false);
                _skipRequested = false;
                if (_replayPending) StartReplay();
                else SetPhase(RoundPhase.Over);
            }
        }

        /// <summary>Over / Abort: nothing moves, the windows close, the cursor is freed for the screen that follows.</summary>
        void EndRoundVisuals()
        {
            _takerArmed = false;
            _taker.Reset();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Bot != null) b.Bot.Disarm();
                if (b.Freed) CupBodies.Hold(b);
                if (b.Keeper != null) b.Keeper.InputLocked = true;
            }
            SetWindows(false, false, false);
            _skipRequested = false;
            if (_cursorCaptured) { GameInput.CaptureCursor(false); _cursorCaptured = false; }
        }

        // ==========================================================================================
        // Shared per-frame helpers
        // ==========================================================================================

        /// <summary>The keeper on the line: the AI brain, or the human controller (locked = the post-shot hold).</summary>
        void TickKeeperOnLine(float dt, bool locked)
        {
            var k = _keeperBody;
            if (k == null || !k.Alive || k.Parked) return;
            // The Decided beat's dejection / cheer runs on the keeper too: while the choreography
            // owns him neither brain may tick (the AI's would ClearPoseOverrides and re-plant him
            // every frame, the human's would stand a fallen body up).
            if (Choreo != null && Choreo.OwnsBody(k)) return;
            // Penalty rule (owner's call): the keeper may shuffle along his line but not come off
            // it toward the ball until it is struck. Held through placement, the whistle raise and
            // the armed wait; released the moment the phase is Live.
            bool hold = Phase == RoundPhase.Placing || Phase == RoundPhase.WhistleRaise || Phase == RoundPhase.Armed;
            if (k.Ai != null) { k.Ai.HoldLine = hold; k.Ai.Tick(); return; }
            if (k.Keeper != null)
            {
                if (k.Celeb != null && k.Celeb.Playing) return;   // an emote owns the pose
                k.Keeper.InputLocked = locked;
                k.Keeper.HoldLine = hold;
                k.Keeper.HoldLineZ = SimConfig.GoalCenter.z;
                k.Keeper.Tick();
            }
        }

        /// <summary>
        /// Bodies in a free window: a human shooter body runs its Striker off its input (unless it
        /// is emoting - movement would fight the pose); a remote human's emote pick is started here
        /// from its wire input (the local player's is started by CupHud straight on Celeb, which is
        /// why the device's EmoteId is never read here).
        /// </summary>
        void TickFreedBodies(float dt)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (!b.Freed || !b.Alive || b.Parked) continue;
                bool emoting = b.Celeb != null && b.Celeb.Playing;
                if (!emoting && b.NetInput != null && b.Celeb != null)
                {
                    // Bound by the WHEEL, not by the enum: the cup's five choreography emotes are
                    // the TOP of the enum (TrophyLift 33 .. WhistleRaise 37) and are played only by
                    // code, so the old `eid <= WhistleRaise` test let a remote human pick every one
                    // of them off the wire - including the three 4 s dejections the driver plays on
                    // the LOSING side, which a teammate could then stand in through a whole winners'
                    // beat. The local player can only ever pick from Celebration.Pages (CupHud hands
                    // the wheel exactly that), so this is the same set, enforced for remotes.
                    int eid = b.NetInput.EmoteId;
                    if (eid >= 0 && eid != 255 && Celebration.OnWheel(eid))
                    {
                        b.Celeb.Play((Celebration.Emote)eid);
                        emoting = true;
                    }
                }
                if (emoting) continue;
                if (b.Striker != null) b.Striker.Tick();
                // THIS kick's keeper body ticks in TickKeeperOnLine. A freed keeper body that is
                // NOT keeping (a Co-op team keeper standing with his side when it scores a free
                // kick) has nobody else to run his controller: unlocked, off his line, here.
                if (b.Keeper != null && b != _keeperBody)
                {
                    b.Keeper.InputLocked = false;
                    b.Keeper.HoldLine = false;
                    b.Keeper.Tick();
                }
            }
        }

        void ShowIntroCard()
        {
            if (Setup == null || Setup.Root == null || Data == null) return;
            int nationA = NationOfSide(CupSide.A), nationB = NationOfSide(CupSide.B);
            var card = CupIntroCard.Create(Setup.Root);
            if (card != null) card.Show(Setup.Stage, nationA, nationB, FirstKicker);
        }

        // ==========================================================================================
        // Replication seams
        // ==========================================================================================

        partial void OnStateCaptured(CupRoundState into)
        {
            into.TakerBodyId = _takerBody != null ? _takerBody.VirtualSlot : CupRoundState.NoBody;
            into.KeeperBodyId = _keeperBody != null ? _keeperBody.VirtualSlot : CupRoundState.NoBody;
            into.WindowRemaining = WindowRemaining;
            into.SkipVotes = ReplayPlaying ? ReplaySkipVotes : 0;
            into.SkipVoters = ReplayPlaying ? ReplaySkipNeeded : 0;
            into.CoinCall = CoinCall;
            into.CoinResult = CoinResult;
        }

        /// <summary>
        /// Client: advance the round's ball spot to the host's SpotIndex.
        ///
        /// PlaceForKick is the ONLY writer of BallSpotPos after Configure, and it never runs on a
        /// client (OnPhaseChanged returns early for one), so without this the spot stays on pair 0
        /// for the whole round. In Penalties that is invisible - the spot is constant - but in Free
        /// Kicks it moves once both sides have shot (design 2.1), and two live consumers read it:
        /// CamTaker frames the taker with Rig.TakerView(.., BallSpotPos, ..), whose starting yaw is
        /// the bearing from the spot to the goal, and ClientSyncDisplayTaker captures it as the
        /// origin of the local taker's look-aim ray - which is the same ray the host's ArmTaker
        /// fires the REAL shot along, off this client's yaw/pitch. A stale spot therefore both
        /// frames and aims a Co-op free kick from a point up to a band's width away.
        ///
        /// No wire field is needed: the client owns the identical schedule (_spots, forked in
        /// Configure from the round's Spots salt) and CupSpots.Spot extends lazily in order, so
        /// SpotFor lands on the same Vector3 on every peer. The wall follows the spot here too,
        /// under the same guard PlaceForKick uses - it is built on every peer in Configure but
        /// only ever rebuilt from PlaceForKick, so it was stranded on pair 0 for the same reason.
        ///
        /// Runs FIRST in OnStateApplied, before ClientSyncDisplayTaker arms the display taker on
        /// the host's Armed edge, because that call closes over the spot by value.
        /// </summary>
        void ClientSyncBallSpot(CupRoundState s)
        {
            if (Setup == null || _spots == null) return;
            Vector3 spot = _spots.SpotFor(Setup.Format, s.SpotIndex);
            if ((spot - BallSpotPos).sqrMagnitude <= 1e-4f) return;
            if (_spotMarker != null) _spotMarker.position = spot;
            SetBallSpot(_spotMarker, spot, s.SpotIndex);
            if (_wall != null && s.SpotIndex != _wallPair) RebuildWallFor(s.SpotIndex, spot);
            _clientPhaseDirty = true;   // the camera's starting bearing is solved from the spot
        }

        partial void OnStateApplied(CupRoundState s)
        {
            if (!_sceneBuilt || Authority != RoundAuthority.Client) return;
            ClientSyncBallSpot(s);
            var taker = BodyByVirtualSlot(s.TakerBodyId);
            var keeper = BodyByVirtualSlot(s.KeeperBodyId);
            bool bodiesChanged = taker != _takerBody || keeper != _keeperBody;
            if (taker != null) _takerBody = taker;
            if (keeper != null) _keeperBody = keeper;

            // (A client never Begin()s - the host's state moves it into Intro - so OnBegin's duties,
            // the cursor and the intro card, are done ONCE per round by ApplyClientCamera on the
            // first played phase seen, guarded by _clientBegun. Two agents once added that seam
            // here AND there, which put two intro cards on a client's screen; this is the one place.)

            // Every kick that arrived since the last apply gets its callout and crowd here (the
            // skeleton's ApplyState fires KickResolved but not the OnKickResolved partial).
            if (Line != null)
            {
                while (_kicksAnnounced < Line.Count)
                {
                    var k = Line.Kicks[_kicksAnnounced++];
                    AnnounceKick(k.Outcome, k.Side);
                }
            }
            if (s.LastOutcome.HasValue && (s.Phase == RoundPhase.Verdict || s.Phase == RoundPhase.Scored || s.Phase == RoundPhase.WalkBack || s.Phase == RoundPhase.Decided))
            {
                _lastTaker = _takerBody;
                _lastKeeper = _keeperBody;
            }
            SetBodies(_takerBody != null ? _takerBody.Ragdoll : null, _keeperBody != null ? _keeperBody.Ragdoll : null);

            // Roles and twin visibility follow the host's kicker; puppets the host is not showing
            // stay hidden here too.
            if (bodiesChanged || _clientPhaseDirty)
            {
                int hide = 0;
                for (int i = 0; i < _bodies.Count; i++)
                {
                    var b = _bodies[i];
                    if (b.Role == CupBodyRole.Referee) continue;
                    if (b == _takerBody) b.Role = CupBodyRole.Taker;
                    else if (b == _keeperBody) b.Role = CupBodyRole.Keeper;
                    else b.Role = CupBodyRole.Lineup;
                    bool park = b.Gone || (IsTwinKeeper(b) && b.Side == s.Kicker) || (IsTwinShooter(b) && b.Side != s.Kicker);
                    if (park && !b.Parked) CupBodies.Park(b, CupSpots.HideSpot(hide++), null);
                    else if (!park && b.Parked)
                    {
                        Goalkeeper.SetVisible(b.Ragdoll, true);
                        b.Parked = false;
                        b.Active = true;
                    }
                }
            }

            bool localTaker = s.TakerSlot >= 0 && s.TakerSlot == Setup.LocalSlot;
            bool localKeeper = s.KeeperSlot >= 0 && s.KeeperSlot == Setup.LocalSlot;
            SetLocalRoles(localTaker, localKeeper, Setup.LocalHasBody && !localTaker && !localKeeper);
            RefreshLocalBody();
            ClientSyncDisplayTaker(localTaker && s.Phase == RoundPhase.Armed);

            // Windows and the skip right, from the host's state.
            bool canSkip = false;
            if (s.ScoredWindowOpen) canSkip = s.LastScorerSlot >= 0 && s.LastScorerSlot == Setup.LocalSlot;
            else if (s.WinBeatOpen)
            {
                var w = Winner;
                canSkip = (s.LastScorerSlot >= 0 && s.LastScorerSlot == Setup.LocalSlot)
                       || (s.LastOutcome.HasValue && s.LastOutcome.Value != KickOutcome.Goal && localKeeper
                           && w.HasValue && w.Value == Setup.LocalSide);
            }
            SetWindows(s.ScoredWindowOpen, s.WinBeatOpen, canSkip);
            _decidedSeconds = s.WinBeatOpen ? s.PhaseTime + s.WindowRemaining : _decidedSeconds;
            if (ReplayPlaying) { ReplaySkipVotes = s.SkipVotes; ReplaySkipNeeded = Mathf.Max(1, s.SkipVoters); }
            CoinCall = s.CoinCall;
            CoinResult = s.CoinResult;

            if (_clientPhaseDirty || bodiesChanged) ApplyClientCamera(false);
            _clientPhaseDirty = false;
        }

        /// <summary>Client: the camera for the current phase and local role (a spectator mirrors the host's view through the rig's MirrorView, which the net agent feeds).</summary>
        void ApplyClientCamera(bool afterReplay)
        {
            if (ReplayPlaying && !afterReplay) { CamReplay(); return; }
            // A client never runs Begin (the host's state moves its phase), so Begin's two duties
            // happen here on the first played phase seen: the cursor is captured for a local body
            // (the keeper and lineup cones read the mouse only while it is captured) and the
            // intro card is shown when that phase is the Intro (a late joiner skips the card).
            if (!_clientBegun && Phase != RoundPhase.Idle && Phase != RoundPhase.Over)
            {
                _clientBegun = true;
                if (Setup != null && Setup.LocalHasBody && !_cursorCaptured) { GameInput.CaptureCursor(true); _cursorCaptured = true; }
                if (Phase == RoundPhase.Intro) ShowIntroCard();
            }
            switch (Phase)
            {
                case RoundPhase.Intro:
                    CamRelease();
                    break;
                case RoundPhase.Placing:
                case RoundPhase.WhistleRaise:
                case RoundPhase.Armed:
                case RoundPhase.Live:
                case RoundPhase.Verdict:
                    CamForLocalRole();
                    break;
                case RoundPhase.Scored:
                    CamHold(_lastTaker ?? LocalBody);
                    break;
                case RoundPhase.Decided:
                    CamHold(LocalBody ?? _lastTaker);
                    break;
                case RoundPhase.Over:
                    CamRelease();
                    if (_cursorCaptured) { GameInput.CaptureCursor(false); _cursorCaptured = false; }
                    break;
            }
        }
    }
}
