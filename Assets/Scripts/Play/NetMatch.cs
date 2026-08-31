using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Networked MATCH (host-authoritative), capped to fit the 8-slot model:
    ///   slots 0-3 = HOME (0 = keeper, 1-3 = outfield), slots 4-7 = AWAY (4 = keeper, 5-7 = outfield).
    /// Team is a pure function of slot (slot &lt; 4 = Home), so nothing on the wire carries a team bit.
    /// That is up to 4-a-side incl. keepers (3v3 + keepers); larger matches stay single-player.
    ///
    /// SHIRT is the per-team identity: shirt 0 is the keeper and 1..perSide-1 are outfield on BOTH
    /// sides, so the Away keeper wears shirt 0 exactly as the Home keeper does. Every peer derives
    /// it from the slot through NetSession.ScrimShirtOfSlot rather than reading it off the wire,
    /// which is what makes SimConfig.AiPace(team, shirt, keeper) agree host-side and client-side,
    /// and it is the same convention single player now uses (GameBootstrap.BuildFootballer).
    ///
    /// The HOST reuses a real MatchGame to run the whole sim (ball, AI, possession, passing,
    /// tackles, goals, clock, kickoff) - it just marks the networked human slots so MatchGame
    /// leaves them to net-driven control instead of AI, and it does not own the camera/HUD/local
    /// input (this driver does). The host then streams a snapshot per body + ball + score + clock.
    /// Clients build the same slot bodies as kinematic puppets and render them from the snapshot
    /// interpolation buffer; the local player's own body is client-predicted + reconciled. Mirrors
    /// NetStrikerMatch's structure and reuses its interpolation / anim-state / reconciliation.
    /// </summary>
    public class NetMatch : MonoBehaviour
    {
        class Body
        {
            public ActiveRagdoll ragdoll;
            public Footballer footballer;   // the AI/team body (host + client both build one)
            public Striker striker;         // outfield control (human slots)
            public Dribble dribble;         // ball control (host: enabled for every human slot)
            public KeeperController keeper;  // human keeper control
            public Celebration celeb;        // emote driver (host sim + local owner)
            public NetInputSource netInput;  // host: remote slots' input adapter
            public bool isKeeper;
            public int team;                 // 0 = Home, 1 = Away
            public bool wasHuman;
            // Pass charge (host only): how long this slot has held each pass key. Per body, so
            // two players charging passes at once don't share one timer.
            // This slot's pass power bar. Was a pair of bare float timers, which had no notion of a
            // hold being ARMED and so could not tell a real charge from a hold carried in off a
            // call-for-pass (see Passing.Bar).
            public readonly Passing.Bar bar = new Passing.Bar();
            // client anim/interp
            public float animPhase;
            public Vector3 lastInterpPos;
            public bool hasLastInterp;
        }

        GameInput _input;
        GameCamera _cam;
        BallController _ball;
        Transform _root;
        NetSession _s;
        MatchGame _game;   // host only: the real sim
        // CLIENT ONLY. The host drives its landing telegraph through MatchGame, which does not exist
        // on a client, so a client owns one directly. Same predictor, same gates, same clamp.
        // Full time, latched on BOTH edges. Rising publishes the table; FALLING clears it, and the
        // falling edge is not optional: ClientUpdate is the only other place a clear could live and it
        // never runs on a host, so a host that rematched would have kept drawing the old board.
        bool _hostFullTime;
        readonly MatchStatsUI _statsUI = new MatchStatsUI();
        AimReticle _clientLanding;
        Vector3 _clientBallPrev;
        bool _clientBallHas;
        MatchArena.Refs _arena;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;
        bool _localIsKeeper;
        uint _tick; float _snapAccum;
        string _flash = ""; float _flashTime;
        QuickChatFeed _qcFeed;   // multiplayer quickchat feed + custom-text entry

        // Slot conventions for the capped two-team board. Delegated to NetSession rather than
        // restated here, because NetSession.SlotAllowed now REFUSES a seat whose shirt is past
        // perSide - so the split it uses and the split this driver builds bodies from have to be
        // the same one, or the session seats a player the driver then gives no body.
        static int TeamOfSlot(int slot) => NetSession.ScrimTeamOfSlot(slot);
        static bool KeeperSlot(int slot) => NetSession.ScrimShirtOfSlot(slot) == 0;

        int _perSide = NetSession.ScrimSlotsPerTeam;   // per side incl keeper, capped to the board

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                              Material homeTorso, Material homeLimb, Material awayTorso, Material awayLimb,
                              Material glove, Transform root, MatchArena.Refs arena, int perSide)
        {
            _input = input; _cam = gameCam; _ball = ball; _root = root; _arena = arena;
            // Same clamp NetSession.ScrimPerSide applies to the seating. Keep them identical.
            _perSide = Mathf.Clamp(perSide, 2, NetSession.ScrimSlotsPerTeam);
            _s = Multiplayer.Session;
            // NetSession OUTLIVES a match - created once, dropped only on Leave - so a second match in
            // the same session would open still holding the previous one's post-match board.
            _s?.ClearMatchStats();
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _s.MatchEvent += OnMatchEvent;
            _s.BallKicked += OnBallKicked;
            _s.PostHit += OnPostHit;
            _s.JerseyUpdated += OnJerseyUpdated;
            _s.RosterChanged += OnRosterChanged;
            _qcFeed = gameObject.AddComponent<QuickChatFeed>();
            _qcFeed.Bind(_s);

            // HOST: create the MatchGame component FIRST so the Footballers built in SpawnBody
            // can take a valid game ref (Footballer reads _game.HomeGoal/PossessionTeam in AiTick).
            // It is Configured below, after all bodies exist.
            if (_s.IsHost)
            {
                var gmGo = new GameObject("NetMatchSim");
                gmGo.transform.SetParent(_root, true);
                _game = gmGo.AddComponent<MatchGame>();
                _game.ConfigureNetHost();
            }

            // Build a body per active slot (host + client both). Team + role are derived from slot.
            for (int slot = 0; slot < NetSession.MaxSlots; slot++)
                SpawnBody(slot, homeTorso, homeLimb, awayTorso, awayLimb, glove);

            // Camera follows the local body.
            var me = _bodies[_localSlot];
            _localIsKeeper = me != null && me.isKeeper;
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                if (_localIsKeeper)
                    _cam.SetKeeperFollow(me.ragdoll.Pelvis.transform,
                                         () => Quaternion.LookRotation(KeeperFace(_localSlot), Vector3.up),
                                         () => _input.Look);
                else
                {
                    _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look);
                    if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
                }
            }

            // HOST: Configure the (already-created) MatchGame over the spawned bodies to run
            // the full sim, marking the networked human slots so it leaves them to net control.
            if (_s.IsHost)
            {
                var home = new List<Footballer>();
                var away = new List<Footballer>();
                Footballer homeKeeper = null, awayKeeper = null;
                for (int slot = 0; slot < NetSession.MaxSlots; slot++)
                {
                    var b = _bodies[slot];
                    if (b == null || b.footballer == null) continue;
                    if (b.isKeeper) { if (b.team == 0) homeKeeper = b.footballer; else awayKeeper = b.footballer; }
                    else (b.team == 0 ? home : away).Add(b.footballer);
                    if (b.striker != null || b.keeper != null) _game.MarkNetControlled(b.footballer);   // net-driven, not AI
                }
                // Outfield role is nominal (the net host owns control per slot).
                _game.Configure(_input, _ball, _cam, _arena, SimConfig.MatchRole.Outfield,
                                home, away, homeKeeper, awayKeeper, null, null, null, null);

                // Attach each human's SLOT to its stat row, AFTER Configure has built them. The row
                // carries the name from here on rather than resolving it at draw time: a player who
                // leaves mid-match can no longer be found in the roster, and their line would otherwise
                // decay to a placeholder on the post-match board.
                for (int slot = 0; slot < NetSession.MaxSlots; slot++)
                {
                    var b = _bodies[slot];
                    if (b == null || b.ragdoll == null || !b.wasHuman) continue;
                    _game.MarkStatSlot(b.ragdoll, slot, RosterName(slot));
                }
            }

            LockCursor();
        }

        // Facing a keeper slot defends: Home keeper (slot 0) defends the -Z (away) goal, Away keeper
        // the +Z. This matches MatchGame.KeeperSpot orientation.
        // Out toward the pitch for a keeper in `slot`. Team 0 attacks +Z, so it DEFENDS the -Z
        // goal and its keeper looks up +Z. This used to be the other way round, which pointed the
        // team 0 keeper camera into its own netting (SpawnBody builds the body facing attackZ, which
        // was already right, so only the camera and the controller disagreed with it).
        Vector3 KeeperFace(int slot) => TeamOfSlot(slot) == 0 ? new Vector3(0f, 0f, 1f) : new Vector3(0f, 0f, -1f);

        void SpawnBody(int slot, Material homeTorso, Material homeLimb, Material awayTorso, Material awayLimb, Material glove)
        {
            var rosterSlot = _s.RosterSlot(slot);
            bool human = rosterSlot.human;
            bool ai = rosterSlot.ai;
            bool isLocal = slot == _localSlot;
            if (!human && !ai && !isLocal) return;   // empty slot: no body
            // Respect the per-side cap. NetSession.SlotAllowed now refuses to SEAT a human past it,
            // so the !human escape is belt over braces rather than the only guard it used to be.
            // The !isLocal escape stays and is not redundant: a peer the host refused carries
            // LocalSlot 255, Configure clamps that to 7, and shirt 3 on a 3-a-side board would then
            // leave the local player with no body and no camera at all. A stray body is the better
            // failure, and SessionBrowserUI bounces a refused joiner before the lobby anyway.
            int shirt = NetSession.ScrimShirtOfSlot(slot);   // 0 = keeper, 1.. = outfield
            if (shirt >= _perSide && !human && !isLocal) return;

            int team = TeamOfSlot(slot);
            bool keeper = KeeperSlot(slot);
            bool hostSim = _s.IsHost;
            float attackZ = team == 0 ? 1f : -1f;
            var facing = Quaternion.LookRotation(new Vector3(0f, 0f, attackZ), Vector3.up);
            // Spread by SHIRT, so Home 1 and Away 1 start on the same lane of their own half. This
            // was slot % 4, which is arithmetically the same number - written as the shirt so the
            // coupling is visible instead of coincidental.
            Vector3 start = new Vector3((shirt - 1.5f) * 2f, 0f, attackZ * -_arena.halfLength * 0.3f);

            var go = new GameObject((team == 0 ? "Home" : "Away") + (keeper ? "GK" : "P" + shirt));
            go.transform.SetParent(_root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();

            // Kit: Home slots wear the (painted) home kit + per-slot networked jersey when present;
            // Away slots wear the away kit. Human slots get a skin-tinted own-copy limb + cosmetics.
            Material teamTorso = team == 0 ? homeTorso : awayTorso;
            Material teamLimb  = team == 0 ? homeLimb  : awayLimb;
            Material slotLimb = human ? Make.Mat(rosterSlot.appearance.Skin) : teamLimb;
            PlayerAppearance? appr = human ? rosterSlot.appearance : (PlayerAppearance?)null;
            Texture2D jt = human ? _s.JerseyForSlot(slot) : null;
            Material slotTorso = (team == 0 && jt != null) ? Make.MatTex(jt) : teamTorso;
            ragdoll.Build(start, facing, slotTorso, slotLimb, withGloves: keeper && glove != null, appearance: appr);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, team = team, wasHuman = human };

            // Every body gets a Footballer (the host MatchGame drives AI ones; a Footballer is
            // harmless on a client puppet - it is never ticked there). Attach a Striker/Dribble too
            // so a human slot can be controlled, exactly like BuildFootballer does.
            var striker = go.AddComponent<Striker>();
            striker.Init(_input, ragdoll);   // input is only read when ControlEnabled; puppets never tick
            striker.ControlEnabled = false;
            var dribble = go.AddComponent<Dribble>();
            dribble.Init(_input, striker, ragdoll, _ball);
            dribble.Enabled = false;   // opted in below, per human slot, host only
            striker.SetDribble(dribble);
            b.dribble = dribble;
            var celeb = go.AddComponent<Celebration>(); celeb.Init(ragdoll); b.celeb = celeb;
            go.AddComponent<Knockdown>().Init(ragdoll);

            if (hostSim)
            {
                AttachKickDetectors(ragdoll, striker);
                var f = go.AddComponent<Footballer>();
                // The shirt is derived from the slot, so it is the SAME on every peer - which is what
                // makes the derived pace agree host-side and client-side (see SimConfig.AiPace).
                f.Init(_game, _ball, ragdoll, team, keeper, attackZ, Vector3.zero, shirt);
                b.footballer = f;

                if (human)
                {
                    // Networked human slot: drive its controller from local device or the wire.
                    if (keeper)
                    {
                        var kc = go.AddComponent<KeeperController>();
                        if (isLocal) kc.Init(_input, ragdoll, _ball);
                        else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll, _ball); }
                        kc.AimBounds = new Vector2(_arena.halfWidth - 1f, _arena.halfLength - 1f);
                        kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.KeeperLookYaw)
                                                    : (() => b.netInput != null ? b.netInput.LookYaw : 0f));
                        b.keeper = kc;
                    }
                    else
                    {
                        // Bind the DRIBBLE to the same source as the striker. Without this every
                        // body's Dribble read the LOCAL device, so the host's sprint and mouse
                        // clicks reached every remote player's ball.
                        if (isLocal) { striker.SetInput(_input); dribble.SetInput(_input); }
                        else
                        {
                            b.netInput = new NetInputSource();
                            striker.SetInput(b.netInput);
                            dribble.SetInput(b.netInput);
                            // A remote striker AIMS with his own camera, off the wire. Without this
                            // his volleys launched down whatever way his body happened to be facing.
                            striker.SetCameraYaw(() => b.netInput != null ? b.netInput.LookYaw : 0f,
                                                 () => b.netInput != null ? b.netInput.LookPitch : 0f);
                        }
                        striker.ControlEnabled = true;
                        // EVERY human outfielder dribbles, not just the host's own body. Safe
                        // because Dribble arbitrates possession through one static holder, so
                        // only whoever reaches the ball first is carrying it.
                        dribble.Enabled = true;
                        b.striker = striker;
                    }
                }
            }
            else
            {
                // Client: only the local body is a live predicted ragdoll; everyone else is a puppet.
                if (isLocal)
                {
                    var f = go.AddComponent<Footballer>();
                    f.Init(null, _ball, ragdoll, team, keeper, attackZ, Vector3.zero, shirt);
                    b.footballer = f;
                    if (keeper)
                    {
                        // No ball handed over on a client, for the same reason dribbling stays off
                        // below: the ball here is a kinematic puppet lerped from host snapshots, so a
                        // local gather would pin and punt a ball the host does not know is held. The
                        // HOST runs this slot's keeper from the wire, E/Q included, and streams it back.
                        var kc = go.AddComponent<KeeperController>(); kc.Init(_input, ragdoll);
                        kc.SetLookYawSource(() => _cam.KeeperLookYaw);
                        b.keeper = kc;
                    }
                    else
                    {
                        striker.SetInput(_input);
                        dribble.SetInput(_input);
                        striker.ControlEnabled = true;
                        // Dribbling stays OFF on a client: the ball there is a kinematic puppet
                        // interpolated from host snapshots, so a local carry would fight it. The
                        // HOST runs this slot's Dribble from the wire and the result streams back.
                        b.striker = striker;
                    }
                }
                else ragdoll.BecomeDisplayBody();
            }

            _bodies[slot] = b;
        }

        void AttachKickDetectors(ActiveRagdoll ragdoll, Striker striker)
        {
            // Layout-driven: a biped's feet and calves, a quadruped's front hooves.
            var strike = ragdoll.StrikeBones;
            for (int i = 0; i < strike.Length; i++)
                AddDet(ragdoll.Rb(strike[i]), striker, ragdoll);
        }
        void AddDet(Rigidbody rb, Striker striker, ActiveRagdoll ragdoll)
        {
            if (rb == null) return;
            rb.gameObject.AddComponent<KickDetector>().Init(striker, ragdoll, _ball);
        }

        void OnMatchEvent(string tag)
        {
            // Referee whistles are audio-only crowd/ref cues, not HUD callouts.
            if (tag == "WHISTLE")  { AudioManager.Instance?.PlayWhistle();       return; }
            if (tag == "WHISTLE3") { AudioManager.Instance?.PlayWhistleTriple(); return; }
            Flash(tag);
        }
        // Client: 3D kick thud at the host-reported contact point (10 m rolloff, per-player).
        void OnBallKicked(Vector3 pos) => AudioManager.Instance?.PlayBallKick(pos);
        void OnPostHit(Vector3 pos, float speed) => AudioManager.Instance?.PlayPostHit(pos, speed);
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        void Update()
        {
            if (_s == null || PauseMenu.Paused) return;

            // Quickchat (multiplayer): Tab types a custom message; while typing, gameplay is
            // suspended. Number keys 1-6 send a preset.
            if (_qcFeed != null)
            {
                if (_input.QuickChatTextPressed) _qcFeed.ToggleTextEntry();
                if (_qcFeed.Typing) return;
                int qd = _input.QuickChatDigitPressed();
                if (qd > 0) _qcFeed.SendPreset(qd);
            }

            // Local emote + control for the local player's own body (client + host predict locally).
            var me = _bodies[_localSlot];
            if (me != null)
            {
                if (me.celeb != null && !me.celeb.Playing)
                {
                    int eid = _input.EmoteId;
                    if (eid >= 0 && eid != 255) me.celeb.Play((Celebration.Emote)eid);
                }
                bool emoting = me.celeb != null && me.celeb.Playing;
                // Knocked over: Knockdown owns the body, so the controller must not steer against it
                // (the same suspension MatchGame applies to the single-player human).
                bool meBlocked = emoting || (me.footballer != null && me.footballer.IsDown);
                if (!meBlocked)
                {
                    if (me.striker != null && me.striker.ControlEnabled) me.striker.Tick();
                    if (me.keeper != null) me.keeper.Tick();
                }
                // OUTSIDE the block gate, and it has to be: TickNetPass is where the bar DISARMS, so
                // skipping it while he is down or emoting froze a half-full charge and its fired latch,
                // and swallowed the release that would have cleared them. It takes `blocked` and
                // disarms instead of charging.
                // Passing is HOST-AUTHORITATIVE: only the host moves the ball, so a client's key press
                // reaches here as net input on the host's own copy of that slot.
                // The HOST-LOCAL slot reads its LIVE PlayerProfile, not the wire. This path was
                // passing at the old neutral substitute too - TickNetPass runs for the host's own slot
                // and NetPass had no local branch - so the host was playing its own match with a
                // stranger's passing stats while its real tree sat one call away.
                if (_s.IsHost && _game != null && me.striker != null && me.striker.ControlEnabled)
                    _game.TickNetPass(me.footballer, me.striker, me.dribble, _input, me.bar,
                                      _cam.Yaw, meBlocked,
                                      PlayerProfile.PassPowerMul, PlayerProfile.PassAccuracyMul,
                                      PlayerProfile.PerkMaestro);
            }

            // Full-time edges on the HOST. The table is published once, from the same stats the host's
            // own board draws, so both peers render identical numbers from one source.
            if (_s.IsHost && _game != null)
            {
                bool ft = _game.FullTime;
                if (ft && !_hostFullTime)
                {
                    _hostFullTime = true;
                    _s.BroadcastMatchStats(_game.WireStats());
                    // The host's own MP lifetime stats, from the same live row the broadcast just
                    // used. MatchGame.EndMatch's own SP hook can't find "me" here - AssignControl is
                    // skipped in net-host mode, so _controlled/_humanKeeperRagdoll are never set -
                    // this is the symmetric hook for that case, keyed on slot instead. Host only: a
                    // client has no scoring authority of its own to derive a result from, so MP
                    // recording for a client is a separate pass, same as the existing SP scope note.
                    MatchGame.PlayerStat mine = null;
                    foreach (var row in _game.Stats) if (row.slot == _localSlot) { mine = row; break; }
                    if (mine != null)
                    {
                        int myScore = mine.team == 0 ? _game.HomeScore : _game.AwayScore;
                        int theirScore = mine.team == 0 ? _game.AwayScore : _game.HomeScore;
                        int result = myScore > theirScore ? 1 : (myScore < theirScore ? -1 : 0);
                        CareerStats.RecordMatchEnd(true, result, mine.goals, mine.assists, mine.shots,
                            mine.tackles, mine.saves, mine.conceded, mine.passes, mine.passesDone, mine.motm);
                    }
                }
                else if (!ft && _hostFullTime) { _hostFullTime = false; _s.ClearMatchStats(); }
            }

            bool showingBoard = _s.IsHost ? _hostFullTime : (_clientFullTime && _s.HasMatchStats);
            if (showingBoard) _statsUI.TickInput();

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void HostUpdate()
        {
            // Feed remote human slots' networked input into their controllers (ticked by the local
            // control path for the host-local slot, and here for remote slots).
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || i == _localSlot) continue;
                if (b.netInput != null) b.netInput.Feed(_s.InputForSlot(i));
                // Start a remote player's emote from their wire pick so the host sims it and streams
                // id+phase to everyone (matches NetStrikerMatch). One-shot: EmoteId != 255 only on
                // the tick it changes.
                if (b.netInput != null && b.celeb != null && !b.celeb.Playing)
                {
                    int reid = b.netInput.EmoteId;
                    if (reid != 255) b.celeb.Play((Celebration.Emote)reid);
                }
                bool remoteEmoting = b.celeb != null && b.celeb.Playing;
                bool remoteBlocked = remoteEmoting || (b.footballer != null && b.footballer.IsDown);
                if (!remoteBlocked)
                {
                    if (b.striker != null && b.striker.ControlEnabled) b.striker.Tick();
                    if (b.keeper != null) b.keeper.Tick();
                }
                // Outside the gate, same reason as the host-local slot above: this is where the bar
                // disarms. The remote's own look yaw drives the pass direction.
                if (_game != null && b.netInput != null && b.striker != null && b.striker.ControlEnabled)
                {
                    // That slot's real passing build, derived on the host from the node mask its owner
                    // sent (NetSession.PassStatsForSlot). A slot that has sent nothing reads uninvested.
                    _s.PassStatsForSlot(i, out float pw, out float ac, out bool mo);
                    _game.TickNetPass(b.footballer, b.striker, b.dribble, b.netInput, b.bar,
                                      b.netInput.LookYaw, remoteBlocked, pw, ac, mo);
                }
            }
            // MatchGame (its own Update) runs the ball/AI/possession/goals/clock this frame.

            PublishSnapshotIfDue();
        }

        void PublishSnapshotIfDue()
        {
            _snapAccum += Time.deltaTime;
            if (_snapAccum < SimConfig.NetSnapshotInterval) return;
            _snapAccum = 0f;
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw, _cam.Pitch));
            BroadcastSnapshot();
            _tick++;
        }

        void BroadcastSnapshot()
        {
            var list = new List<BodyState>();
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || b.ragdoll == null || b.ragdoll.Pelvis == null) continue;
                Vector3 p = b.ragdoll.Pelvis.position; p.y = 0f;
                byte eid = 255, eph = 0;
                if (b.celeb != null && b.celeb.Playing) { eid = (byte)b.celeb.CurrentEmote; eph = (byte)Mathf.Clamp(Mathf.RoundToInt(b.celeb.Progress01 * 255f), 0, 255); }
                bool down = b.footballer != null && b.footballer.IsDown;
                list.Add(new BodyState
                {
                    slot = (byte)i, pos = p, yaw = b.ragdoll.FacingRotation.eulerAngles.y,
                    down = down, emoteId = eid, emotePhase = eph, anim = (byte)AnimStateOf(b),
                    lastInputTick = _s.InputTickForSlot(i),
                });
            }
            int home = _game != null ? _game.HomeScore : 0;
            int away = _game != null ? _game.AwayScore : 0;
            ushort clock = (ushort)(_game != null ? Mathf.Max(0, Mathf.RoundToInt(_game.ClockRemaining)) : 0);
            _s.BroadcastSnapshot(new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
                // No ballistic solve predicts a steered ball, so the client's landing telegraph has to
                // know when it must hide. Needs no clearing anywhere: _assistRemaining is zeroed inside
                // BallController.ResetTo, so kickoff and full time already clear it at source.
                guided = _ball.Guided,
                homeScore = (byte)Mathf.Min(255, home), awayScore = (byte)Mathf.Min(255, away),
                clockSec = clock, bodies = list.ToArray(),
            });
        }

        static AnimState AnimStateOf(Body b)
        {
            if (b.ragdoll == null) return AnimState.Idle;
            if (b.footballer != null && b.footballer.IsDown) return AnimState.Down;
            if (b.keeper != null && b.keeper.IsCommitting) return AnimState.Dive;
            if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            if (b.striker != null && b.striker.IsSitting) return AnimState.Sit;
            if (b.ragdoll.MoveInput.sqrMagnitude > 0.6f) return AnimState.Run;
            return AnimState.Idle;
        }

        int _homeScore, _awayScore, _clockSec;
        bool _clientFullTime;   // client-side latch so full-time applause plays exactly once

        /// <summary>
        /// A client's own PREDICTED pass bar. The authoritative charge lives on the host and is not on
        /// the wire, so the client re-runs the identical step from its own device input to draw a bar
        /// that tracks it. The fire result is DISCARDED - the host decides whether a pass happens.
        ///
        /// It carries the same gates as the host (Passing.CanPlay, plus down and emote), because without
        /// them the client's bar filled and maxed on frames where the host was disarming - which is the
        /// state a player is in most of the time.
        ///
        /// It will still lag the host by about one one-way latency, since the host's timer starts when
        /// the first held frame arrives. At a 0.6 s fill that is a few percent; fixing it properly means
        /// a per-slot charge byte on the snapshot, not a second guess on the client.
        /// </summary>
        void TickClientBar()
        {
            var me = (_localSlot >= 0 && _localSlot < _bodies.Length) ? _bodies[_localSlot] : null;
            if (me == null || _localIsKeeper || _ball == null) return;

            bool blocked = _clientFullTime
                           || (me.celeb != null && me.celeb.Playing)
                           || (me.footballer != null && me.footballer.IsDown);
            Vector3 pos = me.ragdoll != null && me.ragdoll.Pelvis != null ? me.ragdoll.Pelvis.position
                                                                         : Vector3.zero;
            bool canPlay = Passing.CanPlay(_ball, pos, me.dribble != null && me.dribble.Carrying,
                                           blocked, out _);
            Passing.StepAll(me.bar, _input, canPlay, true, out _, out _);

            // Hold the run locally too. The host does this for the authoritative body; without it here
            // the client's own prediction would steer with the camera while the host's did not, and the
            // body would visibly fight its own reconciliation for the length of every aim.
            if (me.striker != null)
            {
                if (me.bar.AnyArmed) me.striker.LockRun(_cam.Yaw);
                else me.striker.ReleaseRun();
            }
        }

        /// <summary>
        /// A client's landing telegraph. Same predictor and same clamp as the host's, but the VELOCITY
        /// has to be differenced from consecutive interpolated positions: the client's ball is a
        /// kinematic puppet driven by Rb.position, so its rigidbody velocity is not the ball's real
        /// velocity. Differencing is exact for a position-lerped puppet over one frame.
        ///
        /// `guided` is replicated (Snapshot.guided) rather than derived, because the assist state is
        /// host-side and private - without it a client's disc was wrong for the 0.45 s of every shot's
        /// assist window, exactly where the host's correctly hid.
        /// </summary>
        void DriveClientLanding(Vector3 ballPos, bool guided)
        {
            if (_clientLanding == null) return;

            Vector3 vel = Vector3.zero;
            if (_clientBallHas && Time.deltaTime > 0.0001f)
                vel = (ballPos - _clientBallPrev) / Time.deltaTime;
            _clientBallPrev = ballPos; _clientBallHas = true;

            if (_clientFullTime || guided
                || ballPos.y - SimConfig.BallRadius < SimConfig.ScrimReticleMinHeight
                || !BallController.PredictLanding(ballPos, vel, out Vector3 land, out float t)
                || t < SimConfig.ScrimReticleMinTime || t > SimConfig.ScrimReticleMaxTime)
            {
                _clientLanding.Hide();
                return;
            }
            land.x = Mathf.Clamp(land.x, -_arena.halfWidth, _arena.halfWidth);
            land.z = Mathf.Clamp(land.z, -_arena.halfLength, _arena.halfLength);
            _clientLanding.Show(land);
        }

        void ClientUpdate()
        {
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw, _cam.Pitch));

            ReconcileLocalBody();
            ApplyAuthoritativeDown();
            TickClientBar();
            if (_clientLanding == null)
            {
                var rg = new GameObject("LandingReticle");
                rg.transform.SetParent(transform, false);
                _clientLanding = rg.AddComponent<AimReticle>();
                _clientLanding.Init(Make.Unlit(SimConfig.ScrimReticleTint));
                _clientLanding.Hide();
            }

            if (!_s.SampleInterpolated(SimConfig.NetInterpDelay, out var a, out var bSnap, out float f))
                return;
            int prevClock = _clockSec, prevHome = _homeScore, prevAway = _awayScore;
            _homeScore = bSnap.homeScore; _awayScore = bSnap.awayScore; _clockSec = bSnap.clockSec;

            // Per-goal crowd audio on CLIENTS, edge-triggered off the replicated score. The host
            // runs MatchGame.OnGoal; clients don't run that sim, so mirror it off score deltas.
            // Scores only ever rise by one (no join-in-progress in a match), so a plain delta is
            // safe; OnMatchGoal ignores a no-op and handles the trailing-margin boos itself.
            if (_homeScore > prevHome || _awayScore > prevAway)
                AudioManager.Instance?.OnMatchGoal(_homeScore, _awayScore);

            // Full-time applause on CLIENTS, edge-triggered off the replicated clock reaching 0.
            // The host plays it in MatchGame.EndMatch; clients don't run that sim, so mirror it
            // off the synced clock (once, on the transition to zero).
            // Clock RISING again means a fresh match: drop the board and the received table with it.
            if (_clientFullTime && _clockSec > prevClock)
            {
                _clientFullTime = false;
                _s.ClearMatchStats();
            }
            if (!_clientFullTime && prevClock > 0 && _clockSec == 0)
            {
                _clientFullTime = true;
                AudioManager.Instance?.PlayGoalCelebration();
            }

            for (int i = 0; i < _bodies.Length; i++)
            {
                var body = _bodies[i];
                if (body == null || i == _localSlot) continue;
                if (!FindBody(a, i, out var sa)) continue;
                if (!FindBody(bSnap, i, out var sb)) sb = sa;
                Vector3 pos = Vector3.Lerp(sa.pos, sb.pos, f);
                float yaw = Mathf.LerpAngle(sa.yaw, sb.yaw, f);
                var facing = Quaternion.Euler(0f, yaw, 0f);
                byte emoteId = sb.emoteId != 255 ? sb.emoteId : sa.emoteId;
                if (emoteId != 255)
                {
                    float ephase = Mathf.Lerp(sa.emotePhase / 255f, sb.emotePhase / 255f, f);
                    body.ragdoll.DisplayEmote(pos, facing, emoteId, ephase);
                    continue;
                }
                float speed = 0f;
                if (body.hasLastInterp) { Vector3 d = pos - body.lastInterpPos; d.y = 0f; speed = d.magnitude / Mathf.Max(1e-4f, Time.deltaTime); }
                body.lastInterpPos = pos; body.hasLastInterp = true;
                float moveAmount = Mathf.Clamp01(speed / SimConfig.StrikerMoveSpeed);
                body.animPhase += Time.deltaTime * SimConfig.StrideRateMax * moveAmount / (2f * Mathf.PI);
                AnimState st = sb.down ? AnimState.Down : (AnimState)sb.anim;
                body.ragdoll.DisplayAnim(pos, facing, st, body.animPhase, moveAmount);
            }

            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(a.ballPos, bSnap.ballPos, f);
            // NEAREST end of the same bracket the position came from, not the union of both. ORing
            // would bias every disagreement toward hiding, which sounds safe but is paid entirely by
            // clients on the one piece of information the telegraph carries; nearest makes the error
            // symmetric at half a snapshot interval and host/client-neutral.
            DriveClientLanding(_ball.Rb.position, f < 0.5f ? a.guided : bSnap.guided);
        }

        // The host owns knockdowns, but a client keeps SIMULATING its own body locally (remote
        // bodies are kinematic puppets that already render sb.down as AnimState.Down). So the
        // streamed flag has to be replayed onto the local body or the victim is the one player who
        // never sees himself fall. Cosmetic only: the host's snapshot position still wins through
        // ReconcileLocalBody, so the local tumble direction does not have to agree with the host's.
        // This slot's display name off the synced roster. Same shape NetSetPieceMatch uses for its
        // scoreboard, so a player reads the same name either side of a mode change.
        string RosterName(int slot)
        {
            var r = _s.Roster;
            if (r != null) for (int i = 0; i < r.Length; i++) if (r[i].slot == slot) return r[i].name;
            return "Player " + slot;
        }

        void ApplyAuthoritativeDown()
        {
            var me = _bodies[_localSlot];
            if (me == null || me.footballer == null || me.footballer.Knock == null) return;
            if (!_s.HasSnapshot) return;
            if (!FindBody(_s.LatestSnapshot, _localSlot, out var auth)) return;
            var knock = me.footballer.Knock;
            // Zero dir: Knockdown topples him along his own facing (Knockdown.Fell handles it).
            if (auth.down && !knock.Down) knock.Fell(Vector3.zero);
            else if (!auth.down && knock.Down) knock.Cancel();   // catch clock drift on the way back up
        }

        void ReconcileLocalBody()
        {
            var me = _bodies[_localSlot];
            if (me == null || me.ragdoll == null || me.ragdoll.Pelvis == null) return;
            if (!me.ragdoll.IsGrounded) return;
            if (!_s.HasSnapshot) return;
            if (!FindBody(_s.LatestSnapshot, _localSlot, out var auth)) return;
            Vector3 pred = me.ragdoll.Pelvis.position; pred.y = 0f;
            Vector3 target = auth.pos; target.y = 0f;
            Vector3 err = target - pred;
            float d = err.magnitude;
            if (d < SimConfig.ReconcileDeadzone) return;
            if (d > SimConfig.ReconcileSnap) { me.ragdoll.ShiftAll(err); return; }
            me.ragdoll.ShiftAll(err * Mathf.Clamp01(SimConfig.ReconcileRate * Time.deltaTime));
        }

        static bool FindBody(in Snapshot s, int slot, out BodyState bs)
        {
            if (s.bodies != null)
                for (int i = 0; i < s.bodies.Length; i++)
                    if (s.bodies[i].slot == slot) { bs = s.bodies[i]; return true; }
            bs = default; return false;
        }

        void OnJerseyUpdated(int slot)
        {
            if (slot < 0 || slot >= _bodies.Length) return;
            var b = _bodies[slot];
            if (b == null || b.ragdoll == null || b.team != 0) return;   // only Home wears painted kits
            var tex = _s.JerseyForSlot(slot);
            if (tex != null) b.ragdoll.SetTorsoMaterial(Make.MatTex(tex));
        }

        void OnRosterChanged()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.wasHuman) continue;
                if (_s.RosterSlot(i).human) continue;   // still human
                // Human left: hand the body back to AI (host) / keep puppeting (client). Removing
                // it from the MatchGame's net-controlled set lets the AI loop resume driving it.
                b.wasHuman = false; b.netInput = null;
                // Recover BEFORE dropping the reference. Clearing ControlEnabled makes Tick
                // early-return forever, so a human who quits mid-trick left the ragdoll at the
                // trick's DriveScale with UprightLock and LocomotionEnabled off and no code path
                // that could ever restore them - the AI Footballer inherited a permanently limp
                // body it could not stand up or steer. This was already reachable by quitting
                // during a diving header; the slide's new limp phase widens the window.
                // ForceRecover is idempotent, so calling it on an idle striker costs nothing.
                if (b.striker != null) { b.striker.ForceRecover(); b.striker.ControlEnabled = false; b.striker = null; }
                // Hand the ball back too: an abandoned body must not stay the carrier.
                if (b.dribble != null) { b.dribble.ForceRelease(); b.dribble.Enabled = false; }
                b.keeper = null;
                if (_game != null && b.footballer != null) _game.UnmarkNetControlled(b.footballer);
                // FREEZE their stat row. The body survives and an AI takes it over, so without this the
                // line keeps accruing and becomes a chimera of one human's match and one bot's - and a
                // frozen row is also barred from man of the match, which it should be.
                if (_game != null && b.ragdoll != null) _game.FreezeStatRow(b.ragdoll);
            }
        }

        void OnDestroy()
        {
            if (_s != null) { _s.MatchEvent -= OnMatchEvent; _s.BallKicked -= OnBallKicked; _s.PostHit -= OnPostHit; _s.JerseyUpdated -= OnJerseyUpdated; _s.RosterChanged -= OnRosterChanged; }
            if (_ball != null && _ball.Rb != null) _ball.Rb.isKinematic = false;
            _statsUI.Teardown();
        }

        static void LockCursor() => GameInput.CaptureCursor(true);

        void OnGUI()
        {
            if (_s == null) return;
            Hud.Begin();
            int home = _s.IsHost && _game != null ? _game.HomeScore : _homeScore;
            int away = _s.IsHost && _game != null ? _game.AwayScore : _awayScore;
            float clock = _s.IsHost && _game != null ? _game.ClockRemaining : _clockSec;
            // FULL TIME: the board owns the screen on both peers. The live furniture is suppressed
            // rather than drawn under it - and this HAS to be gated here as well as in MatchGame,
            // because MatchGame.OnGUI returns immediately in net-host mode and never draws any of
            // the networked HUD. The host gates on its own latch; a client gates on the table arriving,
            // which is the only full-time signal it can act on without inventing one.
            bool showBoard = _s.IsHost ? _hostFullTime : (_clientFullTime && _s.HasMatchStats);
            if (showBoard)
            {
                var rows = _s.IsHost && _game != null
                         ? _game.Stats
                         : MatchGame.FromWire(_s.LatestMatchStats, RosterName);
                int hs = _s.IsHost && _game != null ? _game.HomeScore : _homeScore;
                int as_ = _s.IsHost && _game != null ? _game.AwayScore : _awayScore;
                // "Mine" resolved from THIS SAME rows list, this frame - a client's rows are rebuilt
                // fresh from the wire every call (FromWire allocates new PlayerStat instances), so a
                // reference held across frames would compare wrong; slot survives the wire intact.
                MatchGame.PlayerStat myRow = null;
                foreach (var row in rows) if (row.slot == _localSlot) { myRow = row; break; }
                _statsUI.Draw(rows, hs, as_, myRow);
                // Quickchat can still be opened over the board (Update()'s Typing gate only skips
                // TickInput, so A/D don't fight the flip keys with typed text) - draw it here too,
                // or a player who opens it gets a text box that's silently never rendered.
                if (_qcFeed != null) _qcFeed.Draw();
                Hud.End();
                return;
            }

            // Same broadcast bug the local match draws, off the replicated score/clock.
            Hud.Scoreboard("HOME", UITheme.Blue, home, away, "AWAY", UITheme.Red, Mathf.Max(0f, clock),
                           sub: _localIsKeeper ? "IN GOAL" : null);
            // Pass power bar, bottom left, for whichever slot this peer is driving. On the host this is
            // the authoritative charge; on a client it is the predicted one (see TickClientBar).
            var meBody = (_localSlot >= 0 && _localSlot < _bodies.Length) ? _bodies[_localSlot] : null;
            if (!_localIsKeeper && meBody != null
                && meBody.bar.Showing(out Passing.PassKind bk, out float bt))
                Hud.PowerBar(RosterName(_localSlot), bt, MatchGame.PassKindName(bk));

            // Shot charge bar, same slot as the local match HUD. striker.ShotCharge01/ShotInRange
            // read the LOCAL peer's own input, same as the pass bar above does through `bar` - this is
            // the predicted charge/range, not a value fed by the host.
            if (!_localIsKeeper && meBody != null && meBody.striker != null && meBody.striker.WantsChargedShot)
                Hud.ShotBar(meBody.striker.ShotCharge01, true, meBody.striker.ShotInRange);

            Hud.Legend(_localIsKeeper ? "WASD move   Mouse aim   LMB/RMB dive   Space jump   E/Q throw"
                                      : "WASD move   LMB/RMB legs   E pass   Q loft   X chip   C tackle   B emote   V ball cam");
            Hud.Flash(_flash, _flashTime / 1.6f);

            // Player indicators: one coloured chevron per HUMAN slot, colour keyed to the slot so no
            // two people can share one. wasHuman is cleared by OnRosterChanged when someone leaves, so
            // an AI-reclaimed body drops its marker. The local slot is always drawn: SpawnBody builds a
            // local body even for a roster row that has not arrived yet.
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null) continue;
                if (!b.wasHuman && i != _localSlot) continue;
                Hud.PlayerMarker(b.ragdoll, Hud.SlotColor(i));
            }

            // Quickchat feed + custom-text box (multiplayer).
            if (_qcFeed != null) _qcFeed.Draw();
            Hud.End();
        }
    }
}
