using System;
using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// The round driver's SCENE: everything Configure builds under the round root and OnDestroy
    /// frees - the bodies of both sides (design 7.3), the referee (7.1), the nation kits (2.4),
    /// the ball placement, the free-kick spots and wall (2.1), the replay recorder, the camera
    /// calls through <see cref="CupCameraRig"/>, and the cross-agent additions (the body list,
    /// the callout event, the choreography hook, the replay vote). The kick cycle itself is in
    /// CupRoundDriver.Kick.cs; the public surface and bookkeeping stay in CupRoundDriver.cs.
    ///
    /// Body model. A human who both takes and keeps (Solo, Head to Head) has TWO bodies with the
    /// same slot - a shooter body and a gloved keeper body - because gloves and keeper hitboxes are
    /// baked at Build. Only one is Active per kick; the other is parked hidden behind the goal, and
    /// the swap happens under the placement cut. Co-op humans have one body each (the keeper slot's
    /// is gloved). An AI side has one shooter body per human shooter it mirrors (one in a 1v1) plus
    /// an AI keeper. The referee is a body too (Role Referee) so the HUD, the coin toss and the
    /// snapshot can address him, but he is never in a lineup.
    ///
    /// Free Kicks (owner's call) have NO lineup: a body's LineupMark / LineupFacing there are its
    /// scatter mark for the current kick (AssignFreeKickMarks), recomputed at every placement
    /// because the spot moves once both sides have shot. Penalties keep the lineup marks planned
    /// once in PlanBodies.
    /// </summary>
    public partial class CupRoundDriver
    {
        // ==========================================================================================
        // Cross-agent contract additions
        // ==========================================================================================

        /// <summary>A HUD callout: GOAL / SAVED / MISS at the verdict, REPLAY (click to skip) at a replay. CupHud draws it through Hud.Flash.</summary>
        public event Action<string> Callout;

        /// <summary>Every body of the round, the referee included, in spawn order (stable for the whole round).</summary>
        public IReadOnlyList<CupBody> Bodies => _bodies;

        /// <summary>The local human's ACTIVE body (the shooter or keeper body that is out this kick); null for a spectator.</summary>
        public CupBody LocalBody { get; private set; }

        /// <summary>The referee's body record (Role Referee). <see cref="RefereeActor"/> is the component that moves him.</summary>
        public CupBody Referee { get; private set; }

        /// <summary>The referee component: marks, the whistle raise, the ceremony walk.</summary>
        public CupReferee RefereeActor => _ref;

        /// <summary>The choreography (set by the choreography agent through AttachChoreo; may be null - every call is guarded).</summary>
        public CupChoreo Choreo { get; private set; }

        /// <summary>The camera rig, set by whoever creates the round (the director). Null falls back to GameCamera directly.</summary>
        public CupCameraRig Rig { get; set; }

        /// <summary>The one set-piece taker of the round (the HUD reads IsCharging / Meter for the power bar and clock ring).</summary>
        public SetPieceTaker Taker => _taker;

        /// <summary>The free-kick wall (null in Penalties).</summary>
        public DefensiveWall Wall => _wall;

        /// <summary>The body on the ball this kick (null between kicks).</summary>
        public CupBody TakerCupBody => _takerBody;

        /// <summary>The body on the line this kick.</summary>
        public CupBody KeeperCupBody => _keeperBody;

        /// <summary>The body that took the LAST kick (the scorer of a goal, the shooter walking back).</summary>
        public CupBody LastTakerBody => _lastTaker;

        /// <summary>The body that kept the LAST kick (the winning keeper of a decisive save).</summary>
        public CupBody LastKeeperBody => _lastKeeper;

        /// <summary>Where the taker starts his run-up for the current spot (CupTuning.RunUpDistance behind the ball).</summary>
        public Vector3 RunUpStart => CupSpots.RunUpStart(BallSpotPos);

        /// <summary>A replay is on screen (the unanimous skip vote is open).</summary>
        public bool ReplayPlaying { get; private set; }
        /// <summary>Skip votes so far / voters needed (every human with a body in the round; Local authority: 1).</summary>
        public int ReplaySkipVotes { get; private set; }
        public int ReplaySkipNeeded { get; private set; }

        /// <summary>
        /// CupHud sets this while its emote wheel is open (the wheel frees the cursor): the local
        /// input is gated so the pick's click cannot read as a leg raise or a keeper lunge.
        /// </summary>
        public bool EmoteWheelOpen { get; set; }

        /// <summary>The local device is cut right now: the pause menu is up (an overlay in MP), the emote wheel is open, or a replay plays.</summary>
        public bool LocalInputSuspended => PauseMenu.Paused || EmoteWheelOpen || ReplayPlaying;

        /// <summary>
        /// The local player's body may run and emote right now. Local / Host: the body's own
        /// Freed flag (the scorer's window, the whole scoring side in Free Kicks, the winners'
        /// beat). Client: derived from the mirrored state, since a puppet is never Freed - the
        /// scorer during a scored window, in Free Kicks anyone on the side that just scored, and
        /// the winning side during the win beat. CupHud opens the emote wheel and the freed legend
        /// on this, so a freed teammate gets the wheel and not only the scorer.
        /// </summary>
        public bool LocalIsFreed
        {
            get
            {
                if (Setup == null || !Setup.LocalHasBody) return false;
                if (Authority != RoundAuthority.Client)
                {
                    var b = LocalBody;
                    return b != null && b.Freed && b.Active && !b.Parked;
                }
                if (ScoredWindowOpen)
                {
                    if (LastScorerSlot >= 0 && LastScorerSlot == Setup.LocalSlot) return true;
                    if (Setup.Format == CupFormat.FreeKicks && Line != null && Line.Count > 0 && Line.Last.Side == Setup.LocalSide) return true;
                }
                if (WinBeatOpen)
                {
                    var w = Winner;
                    return w.HasValue && w.Value == Setup.LocalSide;
                }
                return false;
            }
        }

        /// <summary>Seconds left in the open scored window / win beat (0 when none is open).</summary>
        public float WindowRemaining
        {
            get
            {
                if (ScoredWindowOpen) return Mathf.Max(0f, CupTuning.ScoredWindow - PhaseTime);
                if (WinBeatOpen) return Mathf.Max(0f, _decidedSeconds - PhaseTime);
                return 0f;
            }
        }

        /// <summary>The official coin call and the face the coin settled on (recorded by the coin toss through SetCoinOutcome; ride CupRoundState).</summary>
        public CoinFace? CoinCall { get; private set; }
        public CoinFace? CoinResult { get; private set; }

        /// <summary>The scene has been built (bodies, referee, wall, replay) - the loading card may drop.</summary>
        public bool SceneBuilt => _sceneBuilt;

        /// <summary>The coin toss records its outcome here so the wire state carries it (call before Begin).</summary>
        public void SetCoinOutcome(CoinFace? call, CoinFace result)
        {
            CoinCall = call;
            CoinResult = result;
        }

        /// <summary>Bind the choreography (the choreography agent creates it after the driver exists).</summary>
        public void AttachChoreo(CupChoreo c)
        {
            Choreo = c;
        }

        /// <summary>
        /// The body that stands at the coin toss for a side (design 7.1): the human's shooter body
        /// (Solo / Head to Head), the Captain's body in Co-op (the keeper's when the Captain has
        /// none), the AI side's first shooter.
        /// </summary>
        public CupBody CaptainBody(CupSide side)
        {
            if (Setup == null) return null;
            if (Setup.Style == CupStyle.Coop && side == Setup.TeamSide)
            {
                int captain = Director != null ? Director.CaptainSlot : -1;
                var c = BodyOfSlot(captain, false) ?? BodyOfSlot(captain, true);
                if (c != null) return c;
                return BodyOfSlot(Setup.CoopKeeperSlot, true) ?? FirstOnSide(side, false) ?? FirstOnSide(side, true);
            }
            if (Setup.SideIsHuman(side))
            {
                int slot = Setup.HumanSlotOf(side);
                return BodyOfSlot(slot, false) ?? BodyOfSlot(slot, true);
            }
            return FirstOnSide(side, false) ?? FirstOnSide(side, true);
        }

        /// <summary>A human's body: the shooter body, or the gloved keeper body. Null when that human has no such body here (a retired leaver's body never answers).</summary>
        public CupBody BodyOfSlot(int slot, bool keeperBody)
        {
            if (slot < 0) return null;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Slot == slot && b.IsKeeperBody == keeperBody && b.Role != CupBodyRole.Referee && !b.Gone) return b;
            }
            return null;
        }

        /// <summary>The body with a wire id (CupRoundState body ids / the snapshot's slot byte), or null.</summary>
        public CupBody BodyByVirtualSlot(int virtualSlot)
        {
            if (virtualSlot < 0) return null;
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].VirtualSlot == virtualSlot) return _bodies[i];
            return null;
        }

        /// <summary>Every body on a side (the referee excluded), in spawn order.</summary>
        public List<CupBody> BodiesOn(CupSide side)
        {
            var list = new List<CupBody>();
            for (int i = 0; i < _bodies.Count; i++)
                if (_bodies[i].Side == side && _bodies[i].Role != CupBodyRole.Referee) list.Add(_bodies[i]);
            return list;
        }

        /// <summary>The local player's click to skip the replay. Counts once per human; the replay ends when every voter has clicked.</summary>
        public void VoteSkipReplay()
        {
            if (!ReplayPlaying || Setup == null) return;
            if (Authority == RoundAuthority.Client)
            {
                // A client's vote rides the session's existing replay-skip plumbing (SkipVote);
                // the host's driver counts it through VoteSkipReplayBy (NetSession.SkipVoteReceived,
                // see CupRoundDriver.Net.cs) and the state's SkipVotes echoes it back here.
                var s = Multiplayer.Session;
                if (s != null) s.VoteSkip();
                return;
            }
            VoteSkipReplayBy(Setup.LocalSlot);
        }

        /// <summary>Host / Local: count a human's replay-skip vote (a remote human's arrives through the net agent).</summary>
        public void VoteSkipReplayBy(int slot)
        {
            if (!ReplayPlaying) return;
            if (slot < 0) return;
            // The vote is UNANIMOUS AMONG THE HUMANS WITH A BODY in the round (design 2.1), and
            // ReplaySkipNeeded counts exactly those - so only those may vote. A bodiless watcher
            // of a host round (a Client driver with no body, or a lobby spectator) sends the same
            // SkipVote message the participants do; counted, two watchers' clicks could end a
            // replay neither participant had skipped. Their click is simply not a vote here.
            if (BodyOfSlot(slot, false) == null && BodyOfSlot(slot, true) == null) return;
            if (!_skipVoted.Add(slot)) return;   // one click counts once
            ReplaySkipVotes = _skipVoted.Count;
            // Latched: the click arrives from the HUD's OnGUI pass, and ending the replay places
            // the next kick (bodies, ball, cameras) - that belongs in the sim tick, not in a GUI event.
            if (ReplaySkipVotes >= ReplaySkipNeeded) _replayEndRequested = true;
        }

        /// <summary>
        /// Host: a REMOTE human's click to skip the open window (CupRequest.SkipCelebration).
        /// Honoured only from the scorer, or the keeper who made the winning save - the same rule
        /// CanLocalSkip applies to the local player.
        /// </summary>
        public void SkipCelebrationBy(int slot)
        {
            if (!Configured || slot < 0 || Authority == RoundAuthority.Client) return;
            if (!MaySkip(slot)) return;
            _skipRequested = true;
        }

        /// <summary>Capture the whole read model into a fresh wire state (Host authority; the director broadcasts it).</summary>
        public CupRoundState BuildState()
        {
            var s = new CupRoundState();
            CaptureState(s);
            return s;
        }

        /// <summary>
        /// Client: the net agent tells the driver a host replay started / ended (the session's
        /// ReplayStarted / ReplayEnded events), so the camera goes to the broadcast view and the
        /// HUD shows the vote line.
        /// </summary>
        public void ClientSetReplay(bool playing)
        {
            if (Authority != RoundAuthority.Client) return;
            if (playing == ReplayPlaying) return;
            ReplayPlaying = playing;
            _skipVoted.Clear();
            ReplaySkipVotes = 0;
            if (playing)
            {
                CamReplay();
                Callout?.Invoke(CupText.ReplayFlash);
            }
            else
            {
                ApplyClientCamera(true);
            }
        }

        /// <summary>
        /// Client: pose a puppet from a snapshot body (position, facing yaw, emote id + phase).
        /// The net agent maps the snapshot's slot byte to <see cref="CupBody.VirtualSlot"/> and
        /// calls this per body per frame; a parked twin is left hidden.
        /// </summary>
        public void ApplyBodyPose(int virtualSlot, Vector3 pos, float yaw, int emoteId, float emotePhase)
        {
            var b = BodyByVirtualSlot(virtualSlot);
            if (b == null || !b.Alive || b.Parked) return;
            var facing = Quaternion.Euler(0f, yaw, 0f);
            if (emoteId >= 0 && emoteId != 255) b.Ragdoll.DisplayEmote(pos, facing, emoteId, emotePhase);
            else b.Ragdoll.DisplaySnap(pos, facing);
        }

        // ==========================================================================================
        // Scene state
        // ==========================================================================================

        readonly List<CupBody> _bodies = new List<CupBody>();
        CupBody _takerBody, _keeperBody, _lastTaker, _lastKeeper;
        CupReferee _ref;
        CupKitCache _kits;
        DefensiveWall _wall;
        int _wallPair = -1;
        CupSpots _spots;
        ReplaySystem _replay;
        readonly SetPieceTaker _taker = new SetPieceTaker();
        readonly SaveWatch _save = new SaveWatch();
        SeededRng _botRng;
        Transform _spotMarker;
        CupLocalInput _localGate;
        readonly Dictionary<int, NetInputSource> _netInputs = new Dictionary<int, NetInputSource>();
        readonly HashSet<int> _skipVoted = new HashSet<int>();
        int _nextVirtual;
        bool _sceneBuilt;
        float _savedKeeperAbility;
        bool _savedAbilityValid;
        bool _savedSetPieceShot;
        bool _cursorCaptured;

        // ==========================================================================================
        // Build (Configure) and teardown (OnDestroy)
        // ==========================================================================================

        partial void OnConfigured()
        {
            var s = Setup;
            _sceneBuilt = false;
            _bodies.Clear();
            _netInputs.Clear();
            _skipVoted.Clear();
            _takerBody = _keeperBody = _lastTaker = _lastKeeper = null;
            _nextVirtual = CupRoundState.AiBodyIdBase;
            ReplayPlaying = false;
            ReplaySkipVotes = ReplaySkipNeeded = 0;
            CoinCall = null;
            CoinResult = null;
            _kicksAnnounced = Line != null ? Line.Count : 0;   // a resumed line does not re-announce its kicks
            _placeFailed = false;
            _clientBegun = false;

            // Borrowed statics: the stage's keeper ability is written per kick (design 2.2), so
            // remember what we found and put it back on teardown, like every other borrower.
            _savedKeeperAbility = SimConfig.KeeperAbility;
            _savedAbilityValid = true;
            _savedSetPieceShot = s.Ball.SetPieceShot;

            _kits = new CupKitCache();
            _localGate = new CupLocalInput(s.Input, () => LocalInputSuspended);
            // The bot draws from its own family so it can never share a stream with the coin,
            // the spots or the dejection roll (see CupBotTaker.Salt).
            _botRng = s.Stream(CupBotTaker.Salt(s.Stage, Data.Index));
            _spots = new CupSpots(s.Stream(CupSalts.Spots(s.Stage, Data.Index)));

            // The ball spot of the first kick (pair 0) and its marker.
            var marker = new GameObject("BallSpot");
            marker.transform.SetParent(s.Root, false);
            _spotMarker = marker.transform;
            Vector3 spot = _spots.SpotFor(s.Format, CupRoundRules.PairIndex(Line));
            _spotMarker.position = spot;
            SetBallSpot(_spotMarker, spot, CupRoundRules.PairIndex(Line));

            // Set pieces get the arcadey loft + curl and the stat-scaled assist (FreeKickGame does
            // the same for the whole session); restored on teardown because the ball outlives us.
            s.Ball.SetPieceShot = true;

            PlanBodies();
            // Free Kicks: no lineup (owner's call). The idle layout every body is BUILT on is the
            // scatter behind the first spot; the kick's own marks are recomputed at every
            // placement (the spot moves once both sides have shot).
            if (s.Format == CupFormat.FreeKicks) AssignFreeKickIdleMarks(spot);
            BuildBodies();
            BuildReferee(spot);

            // Free kicks: a regulation wall on the ball->goal line, rebuilt once per pair (the
            // spot changes when both sides have shot). Penalties never have one.
            if (s.Format == CupFormat.FreeKicks)
            {
                _wall = new DefensiveWall();
                RebuildWallFor(CupRoundRules.PairIndex(Line), spot);
            }

            // Replay recorder over every body (the referee included - he is in shot) and the ball.
            // The cup passes its OWN window rather than changing SimConfig.ReplayWindow (design 2.1).
            var tracked = new List<Transform> { s.Ball.transform };
            var drivers = new List<MonoBehaviour>();
            for (int i = 0; i < _bodies.Count; i++) ReplaySystem.TrackBody(tracked, drivers, _bodies[i].Ragdoll);
            _replay = gameObject.AddComponent<ReplaySystem>();
            _replay.Setup(tracked, drivers, CupTuning.ReplayWindow);

            // Everyone to their marks, the ball on the spot: the layout the coin toss starts from.
            ParkAllAtMarks();
            s.Ball.ResetTo(spot);
            RefreshLocalBody();
            _sceneBuilt = true;
            CupLog.Info("CupRoundDriver: built " + CupStages.Short(s.Stage) + " #" + Data.Index + " " + Setup.Format
                        + " " + Authority + " with " + _bodies.Count + " bodies at " + CupSpots.Ground(spot));
        }

        partial void OnTornDown()
        {
            // Stop whatever was running (a replay freezes rigidbodies it may not own for long).
            if (_replay != null)
            {
                if (_replay.IsPlaying) _replay.Stop();
                _replay.enabled = true;
            }
            ReplayPlaying = false;
            _takerArmed = false;
            _taker.Reset();
            for (int i = 0; i < _bodies.Count; i++)
                if (_bodies[i].Bot != null) _bodies[i].Bot.Disarm();

            // The wall owns three materials; the bodies' GameObjects die with the root but their
            // painted kits and limb materials would not.
            if (_wall != null) { _wall.Clear(); _wall = null; }
            if (_kits != null) { _kits.Free(); _kits = null; }

            // Borrowed statics and the shared ball. The keeper ability goes back only while it
            // still reads as OUR write (the stage ramp) or the value we found: on a whole-cup
            // teardown the director's RestoreCupStatics may already have put the PRE-CUP value
            // back (OnDestroy order across a destroyed hierarchy is not guaranteed), and
            // re-asserting the ramp over it would leak this stage's keeper into the next mode.
            if (_savedAbilityValid)
            {
                float now = SimConfig.KeeperAbility;
                bool ours = Setup != null && Mathf.Approximately(now, CupTuning.KeeperAbility(Setup.Stage));
                if (ours || Mathf.Approximately(now, _savedKeeperAbility)) SimConfig.KeeperAbility = _savedKeeperAbility;
                _savedAbilityValid = false;
            }
            if (Setup != null && Setup.Ball != null) Setup.Ball.SetPieceShot = _savedSetPieceShot;

            // Hand the camera back and free the cursor for whatever screen follows.
            CamRelease();
            if (_cursorCaptured) { GameInput.CaptureCursor(false); _cursorCaptured = false; }
            _bodies.Clear();
            _netInputs.Clear();
            _sceneBuilt = false;
        }

        // ---- planning ------------------------------------------------------------------------

        /// <summary>
        /// Decide every body of the round from the setup - who is on which side, which slot,
        /// which nation, shooter or gloved keeper body - and its lineup mark, before anything is
        /// built. Order: side A then side B; within a side the shooters then the keeper.
        /// </summary>
        void PlanBodies()
        {
            var s = Setup;
            for (int si = 0; si < 2; si++)
            {
                var side = CupSides.At(si);
                int nation = NationOfSide(side);
                var persons = new List<CupBody[]>();   // one entry per PERSON (a human twin is one person, two bodies)

                if (s.Style == CupStyle.Coop && side == s.TeamSide)
                {
                    // One body per human in the order; the keeper (order[0]) last in the lineup.
                    var order = s.CoopOrderSlots;
                    for (int i = 1; i < order.Length; i++) persons.Add(new[] { Plan(side, order[i], nation, false) });
                    if (order.Length > 0) persons.Add(new[] { Plan(side, order[0], nation, true) });
                }
                else if (s.Style != CupStyle.Coop && s.SideIsHuman(side))
                {
                    // Solo / Head to Head: a shooter body AND a keeper body for the same person.
                    int slot = s.HumanSlotOf(side);
                    persons.Add(new[] { Plan(side, slot, nation, false), Plan(side, slot, nation, true) });
                }
                else
                {
                    // The AI nation: as many shooters as the human side has (one in a 1v1), plus a keeper.
                    int shooters = 1;
                    if (s.Style == CupStyle.Coop) shooters = Mathf.Max(1, s.CoopOrderSlots.Length - 1);
                    for (int i = 0; i < shooters; i++) persons.Add(new[] { Plan(side, -1, nation, false) });
                    persons.Add(new[] { Plan(side, -1, nation, true) });
                }

                bool onTeamSide = side == s.TeamSide;
                for (int p = 0; p < persons.Count; p++)
                {
                    var mark = CupSpots.LineupMark(onTeamSide, p, persons.Count);
                    foreach (var b in persons[p])
                    {
                        b.LineupIndex = p;
                        b.LineupMark = mark;
                        b.LineupFacing = CupSpots.LineupFacing;
                        _bodies.Add(b);
                    }
                }
            }
        }

        CupBody Plan(CupSide side, int slot, int nation, bool keeperBody)
        {
            var b = new CupBody
            {
                Side = side,
                Slot = slot,
                Nation = nation,
                IsKeeperBody = keeperBody,
                Role = CupBodyRole.Lineup,
                Active = true,
            };
            if (slot >= 0)
            {
                b.Name = CupBodies.NameFor(Director, slot);
                // A human's PRIMARY body carries their slot as its wire id; a twin (the keeper body
                // of a Solo / Head to Head human) is an extra body and takes an AI-range id.
                bool primary = !keeperBody || Setup.Style == CupStyle.Coop;
                b.VirtualSlot = primary ? slot : _nextVirtual++;
            }
            else
            {
                b.Name = nation >= 0 && CupNations.IsValid(nation) ? CupNations.Name(nation) : "AI";
                b.VirtualSlot = _nextVirtual++;
            }
            return b;
        }

        /// <summary>The nation index a side wears, -1 when the bracket cannot say (the director's plain torso is used).</summary>
        int NationOfSide(CupSide side)
        {
            if (Data == null || Director == null || Director.Bracket == null) return -1;
            int e = Data.Entrant(side);
            if (!Director.Bracket.IsValidEntrant(e)) return -1;
            return Director.Bracket.Entrants[e].NationIndex;
        }

        // ---- building --------------------------------------------------------------------------

        void BuildBodies()
        {
            var s = Setup;
            bool client = Authority == RoundAuthority.Client;
            var aiLimbs = new Dictionary<int, Material>();

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                var go = new GameObject("CupBody " + CupSides.Name(b.Side) + " " + b.Name + (b.IsKeeperBody ? " (keeper)" : " (shooter)"));
                go.transform.SetParent(s.Root, true);
                b.Go = go;
                Material torso = _kits.Nation(b.Nation, s.Torso);
                // A keeper body is BUILT on the keeper's line facing out: KeeperController.Init reads
                // which way "out" is from the pelvis z at Init (a body south of the halfway line is
                // taken for the -Z goal's keeper), so a keeper built at the lineup mark (z < 0)
                // would face into his own net for the whole round. ParkAllAtMarks moves him after.
                Vector3 buildAt = b.IsKeeperBody ? CupSpots.KeeperLine(s.Format) : b.LineupMark;
                Quaternion buildFacing = b.IsKeeperBody ? CupSpots.KeeperFacing : b.LineupFacing;

                if (b.IsHuman)
                {
                    bool local = b.Slot == s.LocalSlot;
                    var look = CupBodies.LookFor(b.Slot, s.LocalSlot);
                    // Each human body gets its own limb material: Build tints it to the skin.
                    b.Ragdoll = CupBodies.BuildHuman(go, buildAt, buildFacing, torso, _kits.Limb(look.Skin),
                                                     b.IsKeeperBody, look, local);
                    var input = client ? null : InputForSlot(b.Slot);
                    if (!client && input == null)
                    {
                        // A human we cannot drive from here (a Local-authority round that somehow
                        // carries a second human): a puppet is better than a controller that reads
                        // a null input. The director's AuthorityFor is meant to make this unreachable.
                        CupLog.Warn("CupRoundDriver: no input source for slot " + b.Slot + " under " + Authority + " authority - built as a display body");
                    }
                    if (!client && input != null)
                    {
                        if (b.IsKeeperBody)
                        {
                            b.Keeper = go.AddComponent<KeeperController>();
                            b.Keeper.Init(input, b.Ragdoll, s.Ball);
                            // The local keeper reads the camera's cone yaw so body and view lock
                            // step; a remote keeper reads the yaw streamed with his input.
                            var net = b.NetInput;
                            if (local && s.GameCam != null) b.Keeper.SetLookYawSource(() => s.GameCam.KeeperLookYaw);
                            else if (net != null) b.Keeper.SetLookYawSource(() => net.LookYaw);
                        }
                        else
                        {
                            b.Striker = go.AddComponent<Striker>();
                            b.Striker.Init(input, b.Ragdoll);
                            b.Striker.ControlEnabled = false;   // the taker owns the body; freed only in a window
                            b.Striker.SetBall(s.Ball);
                            var net = b.NetInput;
                            if (local && s.GameCam != null) b.Striker.SetCameraYaw(() => s.GameCam.Yaw, () => s.GameCam.Pitch);
                            else if (net != null) b.Striker.SetCameraYaw(() => net.LookYaw, () => net.LookPitch);
                            CupBodies.AttachKick(b.Ragdoll, b.Striker, s.Ball);
                        }
                    }
                    else
                    {
                        b.Ragdoll.BecomeDisplayBody();   // a client mirrors the host's snapshot
                    }
                }
                else
                {
                    Material limb;
                    if (!aiLimbs.TryGetValue(b.Nation, out limb))
                    {
                        // Shorts and socks in the nation's second kit colour (its flag's runner-up),
                        // so a side reads as one team; the project's AI blue when there is none.
                        Color c = b.Nation >= 0 && CupNations.IsValid(b.Nation) ? CupNations.SecondaryColor(b.Nation) : CupBodies.AiLimbFallback;
                        limb = _kits.Limb(c);
                        aiLimbs[b.Nation] = limb;
                    }
                    b.Ragdoll = CupBodies.BuildAi(go, buildAt, buildFacing, torso, limb, b.IsKeeperBody);
                    if (!client)
                    {
                        if (b.IsKeeperBody)
                        {
                            // The two-argument Init faces him out from the +Z goal (outSign -1 =
                            // Sign(KeeperFaceDir.z)), exactly as GameBootstrap.BuildAiKeeper does. A
                            // +1 would face him into his own net and he would never read a shot as
                            // incoming - the silent failure the build notes warn about.
                            b.Ai = go.AddComponent<Goalkeeper>();
                            b.Ai.Init(b.Ragdoll, s.Ball);
                        }
                        else
                        {
                            b.Bot = new CupBotTaker(_botRng);
                        }
                    }
                    else
                    {
                        b.Ragdoll.BecomeDisplayBody();
                    }
                }

                b.Celeb = go.AddComponent<Celebration>();
                b.Celeb.Init(b.Ragdoll);
            }
        }

        void BuildReferee(Vector3 spot)
        {
            var s = Setup;
            Vector3 mark = CupSpots.RefereeMark(spot);
            Vector3 toTaker = CupSpots.RunUpStart(spot) - mark; toTaker.y = 0f;
            var facing = toTaker.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toTaker.normalized, Vector3.up) : Quaternion.identity;
            _ref = CupReferee.Create(s.Root, s.Ball, _kits.Referee(), _kits.Limb(CupBodies.RefereeLimb), mark, facing);
            if (Authority == RoundAuthority.Client) _ref.Body.BecomeDisplayBody();
            Referee = new CupBody
            {
                Side = CupSide.A,
                Slot = -1,
                VirtualSlot = _nextVirtual++,
                Role = CupBodyRole.Referee,
                Ragdoll = _ref.Body,
                Celeb = _ref.Celeb,
                Go = _ref.gameObject,
                Name = "Referee",
                Nation = -1,
                Active = true,
                LineupMark = mark,
                LineupFacing = facing,
            };
            _bodies.Add(Referee);
        }

        /// <summary>The input source for a human's bodies: the gated local device, or (Host) that slot's wire input.</summary>
        IStrikerInput InputForSlot(int slot)
        {
            if (slot == Setup.LocalSlot) return _localGate;
            if (Authority != RoundAuthority.Host) return null;
            NetInputSource n;
            if (!_netInputs.TryGetValue(slot, out n))
            {
                n = new NetInputSource();
                _netInputs[slot] = n;
            }
            // Both of a human's bodies share the one adapter (fed once per tick per slot).
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].Slot == slot) _bodies[i].NetInput = n;
            return n;
        }

        /// <summary>Host: refresh every remote human's input adapter from the session (once per slot per tick).</summary>
        void FeedRemoteInputs()
        {
            if (_netInputs.Count == 0) return;
            var s = Multiplayer.Session;
            if (s == null) return;   // a host-simulated round with no session cannot happen; guarded anyway
            foreach (var kv in _netInputs) kv.Value.Feed(s.ConsumeInputForSlot(kv.Key));
        }

        // ---- a human dropped out mid-round (design 10) ------------------------------------------

        /// <summary>
        /// A human left the session while this round stands. Called by the director on EVERY
        /// peer that has this round (the host from the roster drop / the Quit request, a client
        /// from the CupState echo of the player's Left flag), so the bodies agree everywhere.
        ///
        /// Co-op (design 5 / 10): the leaver is dropped from the round's order and the slots
        /// collapse. A leaving KEEPER is replaced by the lowest-ordered shooter for the rest of the
        /// round - gloves and keeper hitboxes are baked at Build, so his GLOVED body is not
        /// rebuilt but re-slotted to the new keeper and re-wired to that human's input (the local
        /// device, or the wire adapter both of his bodies now share); that human then has two
        /// bodies swapped under the placement cut exactly like a Solo twin (IsTwinKeeper /
        /// IsTwinShooter read the pair through HasTwin). The leaver's own shooter body is retired
        /// (<see cref="CupBody.Gone"/>): hidden at the next placement, never placed, never a
        /// replay-skip voter, never streamed. A leaver who is ON THE BALL keeps his body until the
        /// verdict - the kick clock's auto-shot finishes the attempt (never a walkover).
        ///
        /// Solo / Head to Head: nothing to re-slot (the side plays on with its stale input: the
        /// taker auto-fires at the clock, the keeper stands); the adapter is dropped so the host
        /// stops consuming a dead seat.
        /// </summary>
        public void HumanLeft(int slot)
        {
            if (!_sceneBuilt || Setup == null || slot < 0) return;
            var s = Setup;
            bool client = Authority == RoundAuthority.Client;
            int newKeeper = -1;
            bool wasKeeper = false;

            if (s.Style == CupStyle.Coop)
            {
                var keep = new List<int>(s.CoopOrderSlots.Length);
                for (int i = 0; i < s.CoopOrderSlots.Length; i++) if (s.CoopOrderSlots[i] != slot) keep.Add(s.CoopOrderSlots[i]);
                wasKeeper = s.CoopKeeperSlot == slot;
                s.CoopOrderSlots = keep.ToArray();
                if (wasKeeper)
                {
                    newKeeper = keep.Count > 0 ? keep[0] : -1;
                    s.CoopKeeperSlot = newKeeper;
                    // The keeper slot stands for the team in HumanSlotOf / the stats fallback.
                    if (s.TeamSide == CupSide.A) s.HumanSlotA = newKeeper; else s.HumanSlotB = newKeeper;
                }
            }
            if (!client) _netInputs.Remove(slot);

            var shooter = BodyOfSlot(slot, false);
            var gloves = BodyOfSlot(slot, true);
            if (gloves != null)
            {
                if (newKeeper >= 0) HandGlovesTo(gloves, newKeeper);
                else if (s.Style == CupStyle.Coop) Retire(gloves);   // nobody left on the team to keep
                // Solo / Head to Head: the twin keeper body stays (the side plays on).
            }
            if (shooter != null && s.Style == CupStyle.Coop) Retire(shooter);

            // Roles and the local camera may have changed hands (the new keeper is on the line
            // right now, or the local player just inherited the gloves).
            bool localTaker = _takerBody != null && _takerBody.IsHuman && _takerBody.Slot == s.LocalSlot;
            bool localKeeper = _keeperBody != null && _keeperBody.IsHuman && _keeperBody.Slot == s.LocalSlot;
            SetLocalRoles(localTaker, localKeeper, s.LocalHasBody && !localTaker && !localKeeper);
            RefreshLocalBody();
            if (Phase >= RoundPhase.Placing && Phase <= RoundPhase.Verdict && !client) CamForLocalRole();
            CupLog.Info("CupRoundDriver: slot " + slot + " left mid-round" + (wasKeeper ? " (was keeping; slot " + newKeeper + " keeps now)" : "")
                        + ", order now " + s.CoopOrderSlots.Length + " deep");
        }

        /// <summary>Retire a leaver's body: never placed again; hidden now unless it is mid-kick (the next placement parks it).</summary>
        void Retire(CupBody b)
        {
            if (b == null) return;
            b.Gone = true;
            bool midKick = (b == _takerBody || b == _keeperBody) && Phase >= RoundPhase.Placing && Phase <= RoundPhase.Verdict;
            if (!midKick && b.Alive && !b.Parked) CupBodies.Park(b, CupSpots.HideSpot(HideIndexFor(b)), Setup.Ball);
        }

        /// <summary>
        /// A leaving Co-op keeper's gloved body goes to the new keeper: re-slotted (the wire id
        /// stays the body's), re-wired to the new keeper's input on the authority, and the pair
        /// of bodies that human now owns is swapped under the cut like a twin - so the one that
        /// is out this kick stays out and the other hides.
        /// </summary>
        void HandGlovesTo(CupBody gloves, int newSlot)
        {
            var s = Setup;
            gloves.Slot = newSlot;
            gloves.Name = CupBodies.NameFor(Director, newSlot);
            if (Authority != RoundAuthority.Client)
            {
                var input = InputForSlot(newSlot);   // also re-points NetInput on every body of that slot
                if (gloves.Keeper != null && input != null)
                {
                    gloves.Keeper.SetInput(input);
                    var net = gloves.NetInput;
                    if (newSlot == s.LocalSlot && s.GameCam != null) gloves.Keeper.SetLookYawSource(() => s.GameCam.KeeperLookYaw);
                    else if (net != null) gloves.Keeper.SetLookYawSource(() => net.LookYaw);
                }
            }
            var own = BodyOfSlot(newSlot, false);
            bool keepingNow = _keeperBody == gloves && !gloves.Parked;
            if (keepingNow)
            {
                // The gloves are on the line: the new keeper's shooter body leaves the lineup.
                if (own != null && own != _takerBody && !own.Parked) CupBodies.Park(own, CupSpots.HideSpot(HideIndexFor(own)), s.Ball);
            }
            else if (!gloves.Parked && gloves != _keeperBody)
            {
                // The team is taking (or between kicks): the gloves hide until the team keeps.
                CupBodies.Park(gloves, CupSpots.HideSpot(HideIndexFor(gloves)), s.Ball);
            }
        }

        /// <summary>A hide spot index nobody else uses right now (the parked bodies already there, plus one).</summary>
        int HideIndexFor(CupBody b)
        {
            int n = 0;
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i] != b && _bodies[i].Parked) n++;
            return n;
        }

        /// <summary>
        /// Does a human body have a live counterpart (the other of shooter / gloves) in this round?
        /// Always for a Solo / Head to Head human (both are planned); in Co-op only once a leaving
        /// keeper's gloves were handed to a shooter (HumanLeft), which is what makes that pair swap
        /// under the cut like a twin.
        /// </summary>
        bool HasTwin(CupBody b)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var o = _bodies[i];
                if (o == b || o.Gone || o.Role == CupBodyRole.Referee || !o.IsHuman) continue;
                if (o.Slot == b.Slot && o.IsKeeperBody != b.IsKeeperBody) return true;
            }
            return false;
        }

        /// <summary>The wall for a pair: cleared and rebuilt on the pair's spot (DefensiveWall.Build clears first).</summary>
        void RebuildWallFor(int pair, Vector3 spot)
        {
            if (_wall == null) return;
            _wall.Build(Setup.Root, CupSpots.Ground(spot), CupSpots.WallCenter(spot), CupTuning.WallCount);
            _wallPair = pair;
        }

        // ---- placement helpers -----------------------------------------------------------------

        /// <summary>
        /// The idle layout: every body at its lineup mark facing the goal, a human twin's keeper
        /// body parked hidden, the referee on his mark, roles all Lineup. The coin toss starts from
        /// here and the Intro card returns to it.
        ///
        /// Free Kicks: the marks are the scatter (see AssignFreeKickMarks). Before the round has
        /// begun (Idle: the coin toss) both sides stand in the near band behind the first spot;
        /// from the Intro on the layout is the FIRST KICK's - the taker already at the run-up
        /// start, the keeper on his line - so the card lifts on the kick itself and the
        /// placement cut moves nobody.
        /// </summary>
        void ParkAllAtMarks()
        {
            // Free Kicks from the Intro on: the FIRST KICK's layout, twins included - the human's
            // keeper body is the one out when the other side kicks first, so the card lifts on
            // the kick itself. The idle layout (and every penalty layout) shows the shooter twin.
            bool placedKick = Setup.Format == CupFormat.FreeKicks && Phase != RoundPhase.Idle;
            if (Setup.Format == CupFormat.FreeKicks)
            {
                if (placedKick) AssignFreeKickMarks(CupRoundRules.PairIndex(Line), BallSpotPos, Kicker);
                else AssignFreeKickIdleMarks(BallSpotPos);
            }
            int hide = 0;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Role == CupBodyRole.Referee) continue;
                b.Role = CupBodyRole.Lineup;
                bool park = b.Gone || (placedKick
                    ? (IsTwinKeeper(b) && b.Side == Kicker) || (IsTwinShooter(b) && b.Side != Kicker)
                    : IsTwinKeeper(b));
                if (park) CupBodies.Park(b, CupSpots.HideSpot(hide++), Setup.Ball);
                else CupBodies.Unpark(b, b.LineupMark, b.LineupFacing, Setup.Ball);
            }
            if (_ref != null) _ref.SetMark(CupSpots.RefereeMark(BallSpotPos), CupSpots.RunUpStart(BallSpotPos));
            RefreshLocalBody();   // the twin that is out may have changed
        }

        // ---- free-kick marks (owner's call: no lineup in Free Kicks) --------------------------

        /// <summary>
        /// Write this kick's scatter into every body's LineupMark / LineupFacing (in Free Kicks the
        /// "lineup mark" IS the body's mark for the kick): the taker at the run-up start behind
        /// the ball, the keeper on his line, the taker's own side CupTuning.FreeKickTeamDepthMin..Max
        /// behind the ball in a loose group facing the goal, the other side's non-keeping bodies
        /// CupTuning.FreeKickOppDepthMin..Max back on the taker's left. A human's parked twin keeps
        /// whatever it had (it is hidden). Marks come from the round's Spots stream per (pair,
        /// kicker) through CupSpots.FreeKickMarks and are assigned in spawn order, so every peer
        /// that simulates the round stands the same body on the same mark. Called at every
        /// placement (the spot changes once both sides have shot) and at the Intro.
        /// </summary>
        void AssignFreeKickMarks(int pair, Vector3 spot, CupSide kicker)
        {
            var taker = TakerBodyFor(kicker);
            var keeper = KeeperBodyFor(CupSides.Other(kicker));
            var own = new List<CupBody>();
            var opp = new List<CupBody>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Role == CupBodyRole.Referee || b.Gone) continue;
                if (b == taker)
                {
                    b.LineupMark = CupSpots.RunUpStart(spot);
                    b.LineupFacing = CupSpots.FacingGoal(spot);
                    continue;
                }
                if (b == keeper)
                {
                    b.LineupMark = CupSpots.KeeperLine(Setup.Format);
                    b.LineupFacing = CupSpots.KeeperFacing;
                    continue;
                }
                // The twin that hides this kick needs no mark (PlaceForKick parks it).
                bool hidden = (IsTwinKeeper(b) && b.Side == kicker) || (IsTwinShooter(b) && b.Side != kicker);
                if (hidden) continue;
                if (b.Side == kicker) own.Add(b); else opp.Add(b);
            }
            var ownPos = new List<Vector3>(); var ownFace = new List<Quaternion>();
            var oppPos = new List<Vector3>(); var oppFace = new List<Quaternion>();
            _spots.FreeKickMarks(pair, kicker, spot, own.Count, opp.Count, ownPos, ownFace, oppPos, oppFace);
            for (int i = 0; i < own.Count && i < ownPos.Count; i++) { own[i].LineupMark = ownPos[i]; own[i].LineupFacing = ownFace[i]; }
            for (int i = 0; i < opp.Count && i < oppPos.Count; i++) { opp[i].LineupMark = oppPos[i]; opp[i].LineupFacing = oppFace[i]; }
        }

        /// <summary>
        /// The pre-round scatter (Configure, the coin toss): nobody is on the ball, both sides in
        /// the near band behind the first spot - the human team on the taker's left, the other side
        /// on his right - keepers among their side. Human keeper twins are skipped (parked hidden
        /// in the idle layout); everyone else is assigned in spawn order.
        /// </summary>
        void AssignFreeKickIdleMarks(Vector3 spot)
        {
            var team = new List<CupBody>();
            var other = new List<CupBody>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Role == CupBodyRole.Referee || b.Gone || IsTwinKeeper(b)) continue;
                if (b.Side == Setup.TeamSide) team.Add(b); else other.Add(b);
            }
            var teamPos = new List<Vector3>(); var teamFace = new List<Quaternion>();
            var otherPos = new List<Vector3>(); var otherFace = new List<Quaternion>();
            _spots.FreeKickIdleMarks(spot, team.Count, other.Count, teamPos, teamFace, otherPos, otherFace);
            for (int i = 0; i < team.Count && i < teamPos.Count; i++) { team[i].LineupMark = teamPos[i]; team[i].LineupFacing = teamFace[i]; }
            for (int i = 0; i < other.Count && i < otherPos.Count; i++) { other[i].LineupMark = otherPos[i]; other[i].LineupFacing = otherFace[i]; }
        }

        /// <summary>A human's KEEPER body that hides on own kicks: every Solo / Head to Head human's, and in Co-op a pair made by a keeper hand-over (HumanLeft).</summary>
        bool IsTwinKeeper(CupBody b) => b.IsHuman && b.IsKeeperBody && !b.Gone && (Setup.Style != CupStyle.Coop || HasTwin(b));
        /// <summary>A human's SHOOTER body that hides on opponent kicks (the other half of the pair above).</summary>
        bool IsTwinShooter(CupBody b) => b.IsHuman && !b.IsKeeperBody && !b.Gone && (Setup.Style != CupStyle.Coop || HasTwin(b));

        CupBody FirstOnSide(CupSide side, bool keeperBody)
        {
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Side == side && b.IsKeeperBody == keeperBody && b.Role != CupBodyRole.Referee) return b;
            }
            return null;
        }

        /// <summary>The body that takes a side's next kick: the human's shooter body, the Co-op shooter in order, or the AI shooter whose turn it is.</summary>
        CupBody TakerBodyFor(CupSide side)
        {
            int slot = TakerSlotForNextKick;
            if (slot >= 0)
            {
                // A lone Co-op keeper shoots too (TakerSlotForNextKick says so); use whatever body they have.
                return BodyOfSlot(slot, false) ?? BodyOfSlot(slot, true);
            }
            // AI: cycle its shooters by that side's kick count, like the Co-op order does.
            var shooters = new List<CupBody>();
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Side == side && !b.IsHuman && !b.IsKeeperBody && b.Role != CupBodyRole.Referee) shooters.Add(b);
            }
            if (shooters.Count == 0) return FirstOnSide(side, true);
            int idx = Line != null ? CupRoundRules.CoopShooterFor(Line.Taken(side), shooters.Count) : 0;
            return shooters[idx];
        }

        /// <summary>The body that keeps against a side's kick: the human's gloved body, the Co-op keeper, or the AI keeper.</summary>
        CupBody KeeperBodyFor(CupSide keepingSide)
        {
            int slot = KeeperSlotForNextKick;
            if (slot >= 0) return BodyOfSlot(slot, true) ?? BodyOfSlot(slot, false);
            return FirstOnSide(keepingSide, true);
        }

        /// <summary>Recompute LocalBody: the local human's Active body, else any body of theirs, else null (a spectator).</summary>
        void RefreshLocalBody()
        {
            LocalBody = null;
            if (Setup == null || Setup.LocalSlot < 0) return;
            CupBody any = null, active = null;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b.Slot != Setup.LocalSlot) continue;
                if (any == null) any = b;
                if (b.Active && !b.Parked) active = b;
            }
            LocalBody = active ?? any;
        }

        /// <summary>Every human slot with a body in this round (the replay-skip voters; a leaver's retired body does not vote); Local authority always has exactly one.</summary>
        int CountHumansWithBodies()
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < _bodies.Count; i++) if (_bodies[i].IsHuman && !_bodies[i].Gone) seen.Add(_bodies[i].Slot);
            return Mathf.Max(1, seen.Count);
        }

        /// <summary>Is any human (a body of theirs) on a side?</summary>
        bool AnyHumanOn(CupSide side)
        {
            for (int i = 0; i < _bodies.Count; i++)
                if (_bodies[i].Side == side && _bodies[i].IsHuman && _bodies[i].Role != CupBodyRole.Referee) return true;
            return false;
        }

        /// <summary>
        /// May a human skip the open window? The scored window: the scorer only. The win beat: the
        /// scorer of the winning kick, or the keeper who made the winning save (design 7.4 / 7.7).
        /// </summary>
        bool MaySkip(int slot)
        {
            if (slot < 0) return false;
            if (ScoredWindowOpen) return LastScorerSlot == slot;
            if (WinBeatOpen)
            {
                if (LastScorerSlot == slot) return true;
                var w = Winner;
                if (LastOutcome.HasValue && LastOutcome.Value != KickOutcome.Goal && _lastKeeper != null
                    && _lastKeeper.IsHuman && _lastKeeper.Slot == slot && w.HasValue && w.Value == _lastKeeper.Side)
                    return true;
            }
            return false;
        }

        // ==========================================================================================
        // Cameras: through the rig when there is one, straight to GameCamera otherwise
        // ==========================================================================================

        Vector2 LocalLook() => LocalInputSuspended || Setup == null || Setup.Input == null ? Vector2.zero : Setup.Input.Look;
        float LocalScroll() => LocalInputSuspended || Setup == null || Setup.Input == null ? 0f : Setup.Input.Scroll;

        // Every camera call below stands down while a spectator view mirrors a remote camera on
        // this machine (CamMirrored, CupRoundDriver.Net.cs): a phase cut must not fight the pose
        // the stream is reproducing.
        void CamTaker(CupBody b)
        {
            if (CamMirrored) return;
            if (b == null || !b.Alive) return;
            if (Rig != null) { Rig.TakerView(b.Ragdoll, BallSpotPos, SimConfig.AttackGoalCenter, Setup.Format); return; }
            var cam = Setup.GameCam;
            if (cam == null) return;
            cam.SetFollow(b.Pelvis, LocalLook, LocalScroll);
            cam.SetMode(GameCamera.Mode.Follow);
        }

        void CamKeeper(CupBody b)
        {
            if (CamMirrored) return;
            if (b == null || !b.Alive) return;
            if (Rig != null) { Rig.KeeperView(b.Ragdoll); return; }
            var cam = Setup.GameCam;
            if (cam == null) return;
            cam.SetKeeperFollow(b.Pelvis, () => CupSpots.KeeperFacing, LocalLook, LocalScroll);
        }

        void CamLineup(CupBody b)
        {
            if (CamMirrored) return;
            if (b == null || !b.Alive) return;
            if (Rig != null) { Rig.LineupView(b.Ragdoll); return; }
            var cam = Setup.GameCam;
            if (cam == null) return;
            cam.SetFollow(b.Pelvis, LocalLook, LocalScroll);
            cam.SetMode(GameCamera.Mode.Follow);
        }

        void CamHold(CupBody b)
        {
            if (CamMirrored) return;
            if (b == null || !b.Alive) return;
            if (Rig != null) { Rig.HoldOn(b.Ragdoll); return; }
            var cam = Setup.GameCam;
            if (cam == null) return;
            cam.SetFollow(b.Pelvis, LocalLook, LocalScroll);
            cam.SetMode(GameCamera.Mode.Follow);
        }

        void CamReplay()
        {
            if (CamMirrored) return;
            if (Rig != null) { Rig.ReplayView(); return; }
            var cam = Setup != null ? Setup.GameCam : null;
            if (cam != null) cam.SetMode(GameCamera.Mode.Broadcast);
        }

        void CamRelease()
        {
            if (CamMirrored) return;
            if (Rig != null) { Rig.Release(); return; }
            var cam = Setup != null ? Setup.GameCam : null;
            if (cam != null) cam.SetMode(GameCamera.Mode.Follow);
        }

        /// <summary>The camera for the local player's role this kick (taker / keeper / lineup); a spectator keeps whatever it had.</summary>
        void CamForLocalRole()
        {
            if (LocalIsTaker) CamTaker(_takerBody);
            else if (LocalIsKeeper) CamKeeper(_keeperBody);
            else if (LocalInLineup) CamLineup(LocalBody);
        }
    }
}
