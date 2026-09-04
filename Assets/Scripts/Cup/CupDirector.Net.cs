using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The director's SHARED multiplayer plumbing (design 9.3 / 9.4 / 9.5), style-neutral: the
    /// Head to Head and Co-op partials never touch the session, they call intents and read the
    /// model exactly as Solo does, and this file makes the model agree across peers.
    ///
    /// HOST:   StateChanged -> a CupState broadcast, coalesced to 10/s (a phase change goes at
    ///         once, so a client's phase edge is never delayed by the coalescer); CupRequest ->
    ///         validated (slot ownership, phase) and applied through the public Apply* methods;
    ///         RosterChanged -> ApplyLeave for a dropped human; the spectate relay table mirrored
    ///         into the session; a spectator dropped when the player it watches stops playing.
    /// CLIENT: RequestRaised -> CupRequest; CupState -> the whole model re-applied (players,
    ///         team, order, the draw rebuilt from the seed + picks and the played results, then
    ///         the phase), a new seed = Play Again, phase Ended = the host ended the cup (leave
    ///         on the next tick, never from inside the packet handler); its own live row reported
    ///         to the host while a locally simulated round runs.
    /// BOTH:   the spectate view (CupSpectatorView) opened / closed on the local player's
    ///         SpectatingSlot; the local round streamed while somebody watches it; the podium's
    ///         champion emote on the snapshot channel.
    ///
    /// The loading barrier: every peer NotifyLoaded()s once its round is built; the host reads
    /// <see cref="LoadBarrierOpen"/> (everyone acked, or CupTuning.LoadBarrierTimeout passed).
    /// Between rounds EndRound calls <see cref="NetRoundEnded"/> (snapshot buffer + slot inputs
    /// forgotten; the tick counter never resets). Play Again keeps the session and its
    /// MatchStarted flag: it is a phase change with a new seed, nothing more.
    /// </summary>
    public partial class CupDirector
    {
        NetSession _net;
        bool _netBound;
        bool _netHost;
        bool _netLostAsClient;   // bound as a client, then the session died: IsAuthority must not flip to true (see CupDirector.IsAuthority)
        // Host: the coalesced broadcast.
        bool _stateDirty, _stateForce;
        float _stateSentAt = -100f;
        int _lastStateBytes;
        // Client: what has been applied.
        bool _stateApplied;
        uint _lastStateTick;
        uint _lastPhaseSerial;
        bool _endedPending;
        bool _versionWarned, _hashWarned, _styleWarned;
        // Spectating and streaming.
        CupSpectatorView _spectator;
        int _spectatingShown = -1;
        int _stopSpectateSentFor = -1;   // the target a forced StopSpectating was already sent for (NetTickSpectate)
        uint _streamSeq;
        float _streamAccum;
        // Client: the local round's live row.
        float _liveAccum;
        float _liveSentAt = -100f;
        int _liveSentOpp = -2, _liveSentFor = -1, _liveSentAgainst = -1, _liveSentKick = -1;
        bool _liveSentPlaying;
        // The podium's champion emote.
        float _podiumSnapAccum;
        uint _podiumSnapTick;
        int _podiumEmoteSeen = 255;
        CupPodium _podiumSynced;   // the podium the counters above belong to
        readonly NetInputSource _podiumInput = new NetInputSource();
        readonly List<BodyState> _podiumBodies = new List<BodyState>(1);

        /// <summary>The bound session (null in Solo or without a session).</summary>
        public NetSession Net => _net;
        /// <summary>The wire is bound (a networked cup with a live session).</summary>
        public bool NetBound => _netBound;
        /// <summary>The bytes of the last CupState the host sent (diagnostics; the worst case is about 620).</summary>
        public int LastStateBytes => _lastStateBytes;
        /// <summary>The spectate view in progress, null when the local player is not watching anyone.</summary>
        public CupSpectatorView Spectator => _spectator;
        /// <summary>
        /// The round the HOST is running right now (its CurrentRound), as this peer's own bracket
        /// round object; null when the host runs none. Host: CurrentRound itself. Client: from
        /// CupState - the head-to-head phase plays human rounds one at a time in an order only the
        /// host knows, so a client StartRound()s THIS round on its Loading entry, not a guess.
        /// </summary>
        public CupRound HostRound => IsAuthority ? CurrentRound : _hostRound;
        CupRound _hostRound;

        /// <summary>
        /// The host's loading barrier for the current round (design 6.4): every active player has
        /// sent CupRequest.Loaded, or CupTuning.LoadBarrierTimeout has passed since the phase
        /// began (a slow loader joins from the state). Always open outside a networked cup.
        /// </summary>
        public bool LoadBarrierOpen => !IsNetworked || AllLoaded || PhaseTime >= CupTuning.LoadBarrierTimeout;

        /// <summary>Is any active player watching this slot right now?</summary>
        public bool AnySpectating(int slot)
        {
            if (slot < 0) return false;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Active && p.Slot != slot && p.SpectatingSlot == slot) return true;
            }
            return false;
        }

        /// <summary>Somebody is watching the local player (host: derived; client: the CupState echo).</summary>
        public bool LocalSpectated
        {
            get
            {
                if (IsAuthority) return AnySpectating(LocalSlot);
                var me = LocalPlayer;
                return me != null && me.Spectated;
            }
        }

        // ==========================================================================================
        // Bind / unbind / tick
        // ==========================================================================================

        void NetBind()
        {
            NetUnbind();
            if (!IsNetworked) return;
            var s = Multiplayer.Session;
            if (s == null || !s.Active) return;
            _net = s;
            _netBound = true;
            _netHost = s.IsHost;
            _netLostAsClient = false;
            _stateApplied = false;
            _lastPhaseSerial = 0;
            _spectatingShown = -1;
            _net.CupStreamReceived += NetOnStream;
            if (_netHost)
            {
                StateChanged += NetOnStateChanged;
                PhaseChanged += NetOnPhaseChanged;
                _net.CupRequestReceived += NetOnRequest;
                _net.RosterChanged += NetOnRosterChanged;
                _stateDirty = true;
                _stateForce = true;   // the launch state goes out at once
            }
            else
            {
                RequestRaised += NetOnRequestRaised;
                _net.CupStateReceived += NetOnState;
                // A state that arrived before this director existed (the host launched first) is
                // applied now rather than waiting for the host's next change.
                if (_net.HasCupState) NetOnState(_net.LatestCupState);
            }
        }

        void NetUnbind()
        {
            if (_spectator != null) { _spectator.Close(); _spectator = null; }
            _spectatingShown = -1;
            if (!_netBound) return;
            if (_net != null)
            {
                _net.CupStreamReceived -= NetOnStream;
                _net.CupRequestReceived -= NetOnRequest;
                _net.RosterChanged -= NetOnRosterChanged;
                _net.CupStateReceived -= NetOnState;
            }
            StateChanged -= NetOnStateChanged;
            PhaseChanged -= NetOnPhaseChanged;
            RequestRaised -= NetOnRequestRaised;
            _netBound = false;
            _net = null;
        }

        void NetTick()
        {
            if (!_netBound) return;
            if (_net == null || !_net.Active)
            {
                // The session died under us (a client lost the host): the pump's HostConnectionLost
                // tears the match down; until then nothing here may touch a dead transport - and
                // the flow must not start deciding for a cup that is over (IsAuthority).
                if (!_netHost) _netLostAsClient = true;
                NetUnbind();
                return;
            }
            if (_endedPending)
            {
                // The host ended the cup (CupState Ended). Deferred out of the packet handler: the
                // leave shuts the transport down, which must not happen inside its own Poll.
                _endedPending = false;
                (OnLeave ?? OnMainMenu)?.Invoke();
                return;
            }
            float dt = Time.deltaTime;
            if (_netHost) NetFlushState(false);
            else NetTickLiveRow(dt);
            NetTickSpectate(dt);
            NetTickPodium(dt);
        }

        /// <summary>EndRound's wire duties (design 9.5): the snapshot history and every slot's buffered input are forgotten; the tick counter keeps counting.</summary>
        void NetRoundEnded()
        {
            if (!_netBound || _net == null) return;
            _net.ClearSnapshotBuffer();
            if (_netHost) _net.ResetAllSlotInputs();
            _net.ReplayVotesExternal = false;
            _liveSentAt = -100f;
        }

        /// <summary>A client leaving on purpose tells the host first (CupRequest.Quit); the host applies the leave at once.</summary>
        void NetSendQuit()
        {
            if (!_netBound || _netHost || _net == null) return;
            _net.SendCupRequest(CupNet.Request(CupRequestKind.Quit, 0, null));
        }

        /// <summary>
        /// Head to Head, the owner of a locally simulated round (design 9.3): report the finished
        /// round to the host (CupRequest.RoundResult, the CupRound record). The owner has already
        /// RecordResult()ed it locally; the host validates the line under the rules and folds it
        /// into its bracket, and the echo in CupState matches what the owner already holds. A
        /// no-op on the authority (its own RecordResult IS the truth) and outside a session.
        /// </summary>
        public void ReportRoundResult(CupRound round)
        {
            if (round == null || !IsNetworked || IsAuthority) return;
            RaiseRequest(CupRequestKind.RoundResult, 0, CupNet.PackRound(round));
        }

        /// <summary>
        /// Host: a remote owner's live row for the lobby (CupRequest.LiveRow). `playing` false
        /// clears the row. Also usable by a flow that knows a remote participant's line (a
        /// host-simulated round's second human).
        /// </summary>
        public bool ApplyLiveRow(int slot, int opponentNation, int scoreFor, int scoreAgainst, int kick, bool playing)
        {
            var p = PlayerAt(slot);
            if (p == null || !p.Active) return false;
            if (playing) p.SetLive(opponentNation, scoreFor, scoreAgainst, kick);
            else p.ClearLive();
            Notify();
            return true;
        }

        // ==========================================================================================
        // Host: broadcast and requests
        // ==========================================================================================

        void NetOnPhaseChanged(CupPhase phase)
        {
            // A phase edge goes out with the StateChanged that SetPhase fires right after this.
            _stateForce = true;
        }

        void NetOnStateChanged()
        {
            if (!_netBound || !_netHost || _net == null) return;
            // Derived facts the model does not keep itself: who is watched, and a watcher whose
            // target stopped playing goes back to the lobby with them (design 4, "Spectate target
            // finishes"). ApplySpectate only ever admits a Playing target, so this is its inverse.
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.SpectatingSlot >= 0)
                {
                    var t = PlayerAt(p.SpectatingSlot);
                    // ...and a player with a round of their own on (Playing) watches nobody.
                    if (t == null || !t.Active || !t.Playing || !p.Active || p.Playing) p.SpectatingSlot = -1;
                }
            }
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                p.Spectated = AnySpectating(p.Slot);
                _net.SetCupSpectating(p.Slot, p.Active ? p.SpectatingSlot : -1);
            }
            _stateDirty = true;
            if (_stateForce) NetFlushState(true);
        }

        void NetFlushState(bool force)
        {
            if (!_netBound || !_netHost || _net == null || !_net.Active) return;
            if (!_stateDirty && !force && !_stateForce) return;
            float now = Time.unscaledTime;
            if (!force && !_stateForce && now - _stateSentAt < CupNet.StateCoalesceSeconds) return;
            var m = CupNet.BuildState(this);
            _net.BroadcastCupState(m);
            _lastStateBytes = CupNet.SizeOf(m);
            _stateSentAt = now;
            _stateDirty = false;
            _stateForce = false;
        }

        /// <summary>Host: a client's intent. The slot is the session's authoritative sender; every kind is gated on ownership and phase before it touches the model.</summary>
        void NetOnRequest(int slot, CupRequestMsg m)
        {
            if (!_netBound || !_netHost) return;
            if (slot == LocalSlot) return;   // the host applies its own intents directly (SendCupRequest's host path is a safety net only)
            var p = PlayerAt(slot);
            if (p == null)
            {
                CupLog.Warn("CupDirector: request " + (CupRequestKind)m.kind + " from unknown slot " + slot + " - dropped");
                return;
            }
            var kind = (CupRequestKind)m.kind;
            switch (kind)
            {
                case CupRequestKind.PickNation:
                    ApplyPick(slot, m.arg);
                    break;
                case CupRequestKind.Ready:
                    ApplyReady(slot, m.arg != 0);
                    break;
                case CupRequestKind.Spectate:
                    ApplySpectate(slot, m.arg);
                    break;
                case CupRequestKind.Unspectate:
                    ApplySpectate(slot, -1);
                    break;
                case CupRequestKind.RoundResult:
                {
                    // Only Head to Head plays rounds away from the host, and an owner may only
                    // report a round its OWN entrant is in (design 9.3, trust note).
                    if (Style != CupStyle.HeadToHead || Bracket == null) break;
                    var round = CupNet.UnpackRound(m.payload);
                    if (round == null) break;
                    if (p.Entrant < 0 || !round.Involves(p.Entrant))
                    {
                        CupLog.Warn("CupDirector: slot " + slot + " reported a round it is not in - refused");
                        break;
                    }
                    var owned = CupStages.IsValid(round.Stage) && round.Index >= 0 && round.Index < CupStages.RoundsIn(round.Stage)
                        ? Bracket.Round(round.Stage, round.Index) : null;
                    if (owned == null || !owned.Ready || AuthorityFor(owned) != RoundAuthority.Local)
                    {
                        // Only a round played AWAY from the host (one human, simulated on its
                        // owner's machine) is ever reported; a human-vs-human round is the host's
                        // own simulation and its participants have no result to send.
                        CupLog.Warn("CupDirector: slot " + slot + " reported a round the host simulates itself - refused");
                        break;
                    }
                    if (!NetFirstKickerAgrees(round, p))
                    {
                        // The kick-off is seed-derived (design 2.5 / 7.1): the coin's face is the
                        // round's Coin stream, the call is the one this owner sent (CallCoin
                        // reaches the host before the result on the same reliable stream, HEADS
                        // when they never called). A line that starts from the other side did not
                        // come from that toss; the wave watchdog settles the round by the sim.
                        CupLog.Warn("CupDirector: slot " + slot + " reported " + CupStages.Short(round.Stage) + " #" + round.Index
                                    + " with a first kicker the coin did not produce - refused");
                        break;
                    }
                    ApplyRoundResult(round);
                    break;
                }
                case CupRequestKind.Loaded:
                    ApplyLoaded(slot);
                    break;
                case CupRequestKind.SetOrder:
                    if (Style != CupStyle.Coop || slot != CaptainSlot) break;
                    ApplyOrder(CupNet.UnpackOrder(m.payload));
                    break;
                case CupRequestKind.PullLever:
                    if (Style != CupStyle.Coop || slot != CaptainSlot || Phase != CupPhase.OrderPick) break;
                    RollOrder();
                    break;
                case CupRequestKind.CallCoin:
                    // The same gate CallCoin applies locally - plus Head to Head's parallel tosses,
                    // which each owner runs under the shared Round phase (CupDirector.HeadToHead).
                    if (Phase != CupPhase.CoinToss && !(Style == CupStyle.HeadToHead && Phase == CupPhase.Round)) break;
                    ApplyCoinCall(slot, m.arg != 0 ? CoinFace.Tails : CoinFace.Heads);
                    break;
                case CupRequestKind.SkipCelebration:
                    if (Driver != null) Driver.SkipCelebrationBy(slot);   // the driver applies the scorer / winning-keeper rule
                    break;
                case CupRequestKind.CaptainDecides:
                    if (Style != CupStyle.Coop || slot != CaptainSlot || Phase != CupPhase.NationPick) break;
                    if (p.HasPicked && AllPicked && !MajorityReached) DecideTeamNation(p.Nation);
                    break;
                case CupRequestKind.Continue:
                case CupRequestKind.PlayAgain:
                    // Host-only intents (design 6.6: a client sees "waiting for the host").
                    CupLog.Warn("CupDirector: " + kind + " from slot " + slot + " refused (host only)");
                    break;
                case CupRequestKind.Quit:
                    ApplyLeave(slot);
                    break;
                case CupRequestKind.LiveRow:
                {
                    // The lobby's live rows exist in Head to Head only (design 6.3); Co-op has no
                    // lobby and its one round is the host's own, so a row from a Co-op client is
                    // noise (a Notify and a CupState per kick for nothing).
                    if (Style != CupStyle.HeadToHead) break;
                    int opp, sf, sa, kick;
                    bool playing;
                    if (CupNet.UnpackLiveRow(m.payload, out opp, out sf, out sa, out kick, out playing))
                        ApplyLiveRow(slot, opp, sf, sa, kick, playing);
                    break;
                }
                default:
                    CupLog.Warn("CupDirector: unknown request kind " + m.kind + " from slot " + slot);
                    break;
            }
        }

        /// <summary>
        /// Host: does a reported round's first kicker follow from the coin? The face is the round's
        /// Coin stream's first draw (what the owner's ceremony showed - CupCoinToss draws the same),
        /// the official caller is CoinCallerFor's, and the call is the one the host holds for that
        /// caller - the owner's own (their CallCoin request precedes their RoundResult on the same
        /// reliable stream; an idle caller is HEADS by design 10), or an AI's second draw. A round
        /// whose caller is somebody else's (never a parallel round) is not judged here.
        /// </summary>
        bool NetFirstKickerAgrees(CupRound reported, CupPlayer owner)
        {
            if (Bracket == null || reported == null || !reported.FirstKicker.HasValue) return false;
            var r = Bracket.Round(reported.Stage, reported.Index);
            if (r == null || !r.Ready) return true;   // ApplyRoundResult refuses it anyway
            CupSide callerSide;
            int callerSlot;
            CoinCallerFor(r, out callerSide, out callerSlot);
            var stream = new SeededRng(Seed).Fork(CupSalts.Coin(r.Stage, r.Index));
            var result = stream.Coin();
            var aiCall = stream.Coin();
            CoinFace call;
            if (callerSlot < 0) call = aiCall;
            else if (callerSlot != owner.Slot) return true;
            else call = owner.CoinCall ?? CoinFace.Heads;
            return reported.FirstKicker.Value == CupRoundRules.FirstKickerFromCall(callerSide, call, result);
        }

        /// <summary>Host: a human whose seat is no longer held has left (the transport dropped them, or they quit). The style partials decide what a Left player means from ApplyLeave's flags.</summary>
        void NetOnRosterChanged()
        {
            if (!_netBound || !_netHost || _net == null) return;
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (p.Left || p.Slot == LocalSlot) continue;
                var row = _net.RosterSlot(p.Slot);
                if (!row.human) ApplyLeave(p.Slot);
            }
        }

        // ==========================================================================================
        // Client: requests out, state in
        // ==========================================================================================

        void NetOnRequestRaised(CupRequestKind kind, int arg, byte[] payload)
        {
            if (!_netBound || _netHost || _net == null) return;
            _net.SendCupRequest(CupNet.Request(kind, arg, payload));
        }

        void NetOnState(CupStateMsg m)
        {
            if (!_netBound || _netHost) return;
            if (m.version != CupNet.StateVersion)
            {
                if (!_versionWarned) CupLog.Error("CupDirector: CupState version " + m.version + " (this build speaks " + CupNet.StateVersion + ") - ignored");
                _versionWarned = true;
                return;
            }
            if (_stateApplied && m.tick < _lastStateTick) return;   // reordered (cannot happen on the reliable channel; kept honest)
            _stateApplied = true;
            _lastStateTick = m.tick;
            NetApplyState(in m);
        }

        /// <summary>Client: make the local model the host's. Order matters: seed, captain, players, team, order, the draw and its results, and only then the phase (a flow's entry sees a complete model).</summary>
        void NetApplyState(in CupStateMsg m)
        {
            var phase = (CupPhase)m.phase;
            if (!_styleWarned && ((CupStyle)m.style != Style || (CupFormat)m.format != Format))
            {
                CupLog.Error("CupDirector: the host plays " + CupText.Label((CupStyle)m.style, (CupFormat)m.format) + ", this peer launched " + CupText.Label(Style, Format));
                _styleWarned = true;
            }
            if (m.seed != Seed)
            {
                // Play Again (design 9.5): the same lobby, a new seed, back to CHOOSE YOUR NATION.
                // ResetForNewCup enters NationPick itself; that entry is the host's, so its serial
                // is taken as applied and the phase step below does not enter it a second time.
                ResetForNewCup(m.seed);
                if (phase == CupPhase.NationPick) _lastPhaseSerial = m.phaseSerial;
            }
            SetCaptainSlot(m.captainSlot);
            NetApplyPlayers(in m);
            TeamNation = m.teamNation;
            var order = new int[m.order != null ? m.order.Length : 0];
            for (int i = 0; i < order.Length; i++) order[i] = CupNet.OrderSlotFromByte(m.order[i]);   // 255 = an empty slot
            CoopOrder = order;
            LeverPulls = m.leverPulls;
            RecountNationVotes();
            NetApplyBracket(in m);
            _hostRound = CupNet.UnpackRoundId(Bracket, m.currentRound);

            if (phase == CupPhase.Ended)
            {
                if (Phase != CupPhase.Ended) SetPhase(CupPhase.Ended);
                _lastPhaseSerial = m.phaseSerial;
                _endedPending = true;   // leave on the next tick (see NetTick)
                return;
            }
            if (m.phaseSerial != _lastPhaseSerial)
            {
                _lastPhaseSerial = m.phaseSerial;
                SetPhase(phase, m.phaseTime);
            }
            else Notify();
        }

        void NetApplyPlayers(in CupStateMsg m)
        {
            if (m.players == null) return;
            bool added = false;
            float now = Time.unscaledTime;
            for (int i = 0; i < m.players.Length; i++)
            {
                var row = m.players[i];
                var p = PlayerAt(row.slot);
                if (p == null)
                {
                    // A seat this director never saw (the roster had not caught up at Launch).
                    string name = "Player " + row.slot;
                    if (_net != null)
                    {
                        var rs = _net.RosterSlot(row.slot);
                        if (rs.human && !string.IsNullOrEmpty(rs.name)) name = rs.name;
                    }
                    p = new CupPlayer(row.slot, name);
                    _players.Add(p);
                    added = true;
                }
                bool local = row.slot == LocalSlot;
                p.Nation = row.nation;
                p.Entrant = row.entrant;
                p.Ready = (row.status & CupPlayerStatus.Ready) != 0;
                p.Out = (row.status & CupPlayerStatus.Out) != 0;
                p.ReplacedByAi = (row.status & CupPlayerStatus.ReplacedByAi) != 0;
                p.Left = (row.status & CupPlayerStatus.Left) != 0;
                p.Loaded = (row.status & CupPlayerStatus.Loaded) != 0;
                p.Spectated = (row.status & CupPlayerStatus.Spectated) != 0;
                p.SpectatingSlot = row.spectating == 255 ? -1 : row.spectating;

                // The local live row: while THIS machine simulates the local round, or right
                // after it reported a change, its own numbers are fresher than the host's echo.
                bool keepLive = local && ((Driver != null && Driver.Configured && Driver.Authority == RoundAuthority.Local)
                                          || now - _liveSentAt < CupNet.LiveRowEchoGrace);
                if (!keepLive)
                {
                    bool playing = (row.status & CupPlayerStatus.Playing) != 0;
                    if (playing) p.SetLive(row.liveOpponent, row.liveFor, row.liveAgainst, row.liveKick);
                    else p.ClearLive();
                }

                // The coin: the local call lights at once in CallCoin; an echo that has not caught
                // up yet must not put it out mid-toss. "Mid-toss" is the CoinToss phase OR a
                // ceremony open on this peer (Toss != null): Head to Head's parallel tosses run
                // under the shared Round phase, and a state sent before the call landed would
                // otherwise null the call the ceremony is about to judge.
                if ((row.coin & CupCoinBits.HasCall) != 0)
                    p.CoinCall = (row.coin & CupCoinBits.CallTails) != 0 ? CoinFace.Tails : CoinFace.Heads;
                else if (!(local && p.CoinCall.HasValue && (Phase == CupPhase.CoinToss || Toss != null)))
                    p.CoinCall = null;
                p.CoinCallRight = (row.coin & CupCoinBits.HasVerdict) != 0 ? (bool?)((row.coin & CupCoinBits.Right) != 0) : null;
                p.CoinCallsMade = row.coinMade;
                p.CoinCallsRight = row.coinRight;
            }
            if (added) _players.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        }

        /// <summary>
        /// Client: the draw. Rebuilt from the seed and the entrant picks the rows carry (a pure
        /// function on every peer), the leavers marked, every Done round applied - a simulated
        /// one re-run by CupSim on the same forked stream, a played one from its kick line - and
        /// each complete stage fed forward, exactly as the host's AdvanceStage did. The shape is
        /// checked against the host's hash; a mismatch is a build difference and is logged once.
        /// </summary>
        void NetApplyBracket(in CupStateMsg m)
        {
            if (!m.hasBracket)
            {
                if (Bracket != null)
                {
                    Bracket = null;
                    LocalEntrant = -1;
                    RefreshPlayersFromBracket();
                }
                return;
            }
            if (Bracket == null || Bracket.Seed != Seed)
            {
                if (!NetBuildBracket(in m)) return;
            }
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                if (!p.ReplacedByAi) continue;
                int e = Bracket.EntrantOfHuman(p.Slot);
                if (e >= 0) Bracket.MarkReplacedByAi(e);
            }
            var results = m.results;
            for (int s = 0; s < CupStages.Count; s++)
            {
                var stage = (CupStage)s;
                if (results != null)
                    for (int i = 0; i < results.Length; i++)
                        if (results[i].stage == s) NetApplyResult(in results[i]);
                if (s < CupStages.Count - 1 && Bracket.StageComplete(stage))
                {
                    var next = Bracket.RoundsOf(CupStages.Next(stage));
                    bool fed = true;
                    for (int i = 0; i < next.Count; i++) if (!next[i].Ready) { fed = false; break; }
                    if (!fed) Bracket.Advance(stage);
                }
            }
            if (m.bracketHash != 0u && CupNet.BracketHash(Bracket) != m.bracketHash && !_hashWarned)
            {
                CupLog.Error("CupDirector: the rebuilt draw does not match the host's (hash " + CupNet.BracketHash(Bracket).ToString("X8") + " vs " + m.bracketHash.ToString("X8") + ") - the nation pool differs between builds");
                _hashWarned = true;
            }
            var hostStage = (CupStage)m.stage;
            if (CupStages.IsValid(hostStage)) Stage = hostStage;
            RefreshPlayersFromBracket();
            SimConfig.KeeperAbility = CupTuning.KeeperAbility(Stage);
        }

        bool NetBuildBracket(in CupStateMsg m)
        {
            var humans = new List<(int nationIndex, int humanSlot, string humanName)>();
            if (Style == CupStyle.Coop)
            {
                if (TeamNation < 0) { CupLog.Error("CupDirector: the host has a draw but no team nation reached this peer"); return false; }
                humans.Add((TeamNation, CaptainSlot, CupText.YourTeam));
            }
            else if (m.players != null)
            {
                // The entrants are the players who were in the draw when it was made - Left or
                // not since - which is what `entrant >= 0` says. Names are display only.
                for (int i = 0; i < m.players.Length; i++)
                {
                    var row = m.players[i];
                    if (row.entrant < 0 || row.nation < 0) continue;
                    var p = PlayerAt(row.slot);
                    humans.Add((row.nation, row.slot, p != null ? p.Name : "Player " + row.slot));
                }
            }
            if (humans.Count == 0) { CupLog.Error("CupDirector: the host has a draw but no entrant humans reached this peer"); return false; }
            try
            {
                Bracket = CupBracket.Build(Seed, Format, humans, CupNations.ResolvedPool());
            }
            catch (Exception e)
            {
                CupLog.Error("CupDirector: rebuilding the draw failed (" + e.Message + ")");
                Bracket = null;
                return false;
            }
            Stage = CupStage.RoundOf32;
            _hashWarned = false;
            return true;
        }

        void NetApplyResult(in CupResultRow row)
        {
            var stage = (CupStage)row.stage;
            if (!CupStages.IsValid(stage) || row.index >= CupStages.RoundsIn(stage)) return;
            var r = Bracket.Round(stage, row.index);
            if (r == null || !r.Ready)
            {
                CupLog.Warn("CupDirector: result for " + CupStages.Short(stage) + " #" + row.index + " before its entrants are known - skipped");
                return;
            }
            if (row.simulated)
            {
                // The host's verdict wins. A round THIS peer played and recorded locally but the
                // host settled by the sim (its report refused under the rules, or the wave
                // watchdog beat it) is re-run here from the same stream, so the two brackets
                // never feed different winners forward (SetResult logs the overwrite).
                if (r.Done && r.Simulated) return;
                if (r.Done) CupLog.Warn("CupDirector: the host simulated " + CupStages.Short(stage) + " #" + row.index + " over this peer's played result - following the host");
                try { CupSim.Simulate(r, Bracket, CupSim.StreamFor(Bracket, r)); }
                catch (Exception e) { CupLog.Error("CupDirector: re-simulating " + CupStages.Short(stage) + " #" + row.index + " failed (" + e.Message + ")"); }
                return;
            }
            var kicks = CupNet.UnpackKicks(row.kicks);
            if (r.Done && !r.Simulated && r.ScoreA == row.scoreA && r.ScoreB == row.scoreB && r.Kicks.Count == kicks.Count) return;
            try
            {
                Bracket.SetResult(r, row.scoreA, row.scoreB, kicks, row.suddenDeath, row.firstKicker != 0 ? CupSide.B : CupSide.A, false);
            }
            catch (Exception e)
            {
                CupLog.Error("CupDirector: applying " + CupStages.Short(stage) + " #" + row.index + " failed (" + e.Message + ")");
            }
        }

        /// <summary>Client, the owner of a local round: the lobby's live row for this player rides CupRequest.LiveRow whenever it changes (rate-limited).</summary>
        void NetTickLiveRow(float dt)
        {
            if (Style != CupStyle.HeadToHead) return;   // the only style with a lobby to show a row in
            var me = LocalPlayer;
            if (me == null || _net == null) return;
            _liveAccum += dt;
            bool changed = me.Playing != _liveSentPlaying || me.LiveOpponentNation != _liveSentOpp
                        || me.LiveScoreFor != _liveSentFor || me.LiveScoreAgainst != _liveSentAgainst || me.LiveKick != _liveSentKick;
            if (!changed || _liveAccum < CupNet.LiveRowInterval) return;
            _liveAccum = 0f;
            _liveSentPlaying = me.Playing;
            _liveSentOpp = me.LiveOpponentNation;
            _liveSentFor = me.LiveScoreFor;
            _liveSentAgainst = me.LiveScoreAgainst;
            _liveSentKick = me.LiveKick;
            _liveSentAt = Time.unscaledTime;
            _net.SendCupRequest(CupNet.Request(CupRequestKind.LiveRow, 0, CupNet.PackLiveRow(me)));
        }

        // ==========================================================================================
        // Spectating (both roles): the view, and the stream out
        // ==========================================================================================

        void NetTickSpectate(float dt)
        {
            var me = LocalPlayer;
            int want = me != null && me.Active ? me.SpectatingSlot : -1;
            // Nobody watches while playing: a round of our own standing with a body for us means
            // the mirrored camera would hide our own (CamMirrored). Drop the view here and tell the
            // host to clear the row, whatever a flow forgot.
            if (want >= 0 && Driver != null && Driver.Configured && Driver.Setup != null && Driver.Setup.LocalHasBody)
            {
                // Once per target: on a client the host's echo clears the row a round trip later,
                // and a request per frame until then is a burst of reliable packets for nothing.
                if (_stopSpectateSentFor != want) { _stopSpectateSentFor = want; StopSpectating(); }
                want = -1;
            }
            else _stopSpectateSentFor = -1;
            if (want != _spectatingShown)
            {
                if (want >= 0)
                {
                    if (_spectator == null) _spectator = CupSpectatorView.Create(this);
                    _spectator.SetTarget(want);
                }
                else if (_spectator != null)
                {
                    _spectator.Close();
                    _spectator = null;
                }
                _spectatingShown = want;
            }

            // The owner of a watched round streams its view (design 4): only while somebody
            // watches, only with a built round to show, at 20 Hz.
            bool stream = LocalSpectated && Driver != null && Driver.Configured && Driver.SceneBuilt && Rig != null && _net != null;
            if (!stream) { _streamAccum = 0f; return; }
            _streamAccum += dt;
            if (_streamAccum < CupNet.StreamInterval) return;
            _streamAccum = 0f;
            _net.SendCupStream(CupNet.BuildStream(Driver, Rig, Ball, ++_streamSeq, true));
        }

        void NetOnStream(CupStreamMsg m)
        {
            if (_spectator != null) _spectator.OnStream(in m);
        }

        // ==========================================================================================
        // The podium (Head to Head): only the champion's emote crosses the wire (design 8.1)
        // ==========================================================================================

        void NetTickPodium(float dt)
        {
            if (Phase != CupPhase.Podium || _podium == null || _net == null) return;
            if (_podiumSynced != _podium)
            {
                // A fresh podium (Play Again's second cup): the emote edge and the snapshot cursor
                // start clean, or the first pick of this podium could read as "already seen".
                _podiumSynced = _podium;
                _podiumEmoteSeen = 255;
                _podiumSnapTick = 0u;
                _podiumSnapAccum = 0f;
            }
            var winner = _podium.Winner;
            if (winner == null || !winner.Alive) return;
            if (_netHost)
            {
                // A remote champion's wheel pick arrives as an input frame (the podium's wheel
                // sets GameInput's one-shot emote, which the client samples into its frames).
                int champ = _podium.ChampionSlot;
                if (champ >= 0 && champ != LocalSlot)
                {
                    _podiumInput.Feed(_net.ConsumeInputForSlot(champ));
                    int eid = _podiumInput.EmoteId;
                    if (eid != 255 && eid >= 0 && eid <= (int)Celebration.Emote.WhistleRaise)
                        _podium.PlayWinnerEmote((Celebration.Emote)eid);
                }
                _podiumSnapAccum += dt;
                if (_podiumSnapAccum < SimConfig.NetSnapshotInterval) return;
                _podiumSnapAccum = 0f;
                Vector3 p = winner.Pelvis != null ? winner.Pelvis.position : Vector3.zero;
                p.y = 0f;
                _podiumBodies.Clear();
                _podiumBodies.Add(new BodyState
                {
                    slot = (byte)Mathf.Clamp(winner.VirtualSlot, 0, 255), pos = p,
                    yaw = winner.Ragdoll.FacingRotation.eulerAngles.y, down = false,
                    emoteId = (byte)Mathf.Clamp(_podium.WinnerEmoteId, 0, 255),
                    emotePhase = (byte)Mathf.Clamp(Mathf.RoundToInt(_podium.WinnerEmotePhase * 255f), 0, 255),
                    anim = 0, lastInputTick = champ >= 0 ? _net.InputTickForSlot(champ) : 0u,
                    erect = winner.Ragdoll.Anatomy != null && winner.Ragdoll.Anatomy.Erect,
                });
                _net.BroadcastSnapshot(new Snapshot
                {
                    tick = Tick, ballPos = Ball != null ? Ball.transform.position : Vector3.zero, ballVel = Vector3.zero,
                    guided = false, homeScore = 0, awayScore = 0, bodies = _podiumBodies.ToArray(),
                });
            }
            else
            {
                if (_podium.ChampionIsLocal)
                {
                    // The champion's wheel picks ride input frames (no round is sampling them now).
                    float yaw = GameCam != null ? GameCam.Yaw : 0f, pitch = GameCam != null ? GameCam.Pitch : 0f;
                    if (Input != null) _net.SetLocalInput(Input.SampleFrame(Tick, yaw, pitch));
                    return;
                }
                if (!_net.HasSnapshot) return;
                var snap = _net.LatestSnapshot;
                if (snap.tick <= _podiumSnapTick || snap.bodies == null) return;
                _podiumSnapTick = snap.tick;
                for (int i = 0; i < snap.bodies.Length; i++)
                {
                    var b = snap.bodies[i];
                    if (b.slot != winner.VirtualSlot) continue;
                    int eid = b.emoteId;
                    if (eid == _podiumEmoteSeen) break;
                    _podiumEmoteSeen = eid;
                    // The lift itself is re-played by every peer's podium on its own whenever the
                    // champion idles; only a wheel pick needs the wire.
                    if (eid != 255 && eid != (int)Celebration.Emote.TrophyLift && eid <= (int)Celebration.Emote.WhistleRaise)
                        _podium.PlayWinnerEmote((Celebration.Emote)eid);
                    break;
                }
            }
        }
    }
}
