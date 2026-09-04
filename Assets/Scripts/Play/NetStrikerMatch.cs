using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;   // Keyboard, for the cross map's Escape-also-closes
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Networked striker match (host-authoritative). Shooting practice with several humans
    /// on the pitch: one keeper slot + shooter slots, a crosser feeding balls, one shared
    /// ball, shared goal scoring.
    ///
    ///  HOST: owns the real physics. Spawns a Striker body per slot; the LOCAL slot reads
    ///        the device, remote slots read their NetInputSource (fed from the wire). Runs
    ///        the crosser + ball + goal detection, and each FixedUpdate broadcasts a
    ///        Snapshot (every body's pelvis pos/yaw + the ball) and goal events.
    ///
    ///  CLIENT: spawns display-puppet bodies for every slot. Its OWN body is a real,
    ///        client-predicted Striker (instant response); all OTHER bodies + the ball are
    ///        kinematic puppets lerped toward the latest host snapshot. Sends local input to
    ///        the host each fixed step.
    ///
    /// Bodies are indexed by slot (0 = keeper, 1..N = shooters), matching NetSession.
    /// </summary>
    public class NetStrikerMatch : MonoBehaviour
    {
        // Per-slot body + control.
        class Body
        {
            public ActiveRagdoll ragdoll;
            public Striker striker;         // null for the keeper/crosser puppet
            public NetInputSource netInput; // host: remote slots' input adapter
            public Goalkeeper ai;           // host: AI keeper when no human holds slot 0
            public KeeperController keeper; // host: human keeper controller (slot 0 with a human)
            public CrosserControl crosserCtl; // host: human crosser controller (slot 7 with a human)
            public Celebration celeb;       // emote driver (host sim + local owner); null on pure puppets
            public bool isKeeper;
            public bool isCrosser;
            public bool wasHuman;   // spawned for a human (despawn if they leave) vs an AI Clanker
            // client interp targets
            public Vector3 targetPos;
            public float targetYaw;
            // client: emote to display on this puppet (255 = none) + its 0..1 phase.
            public int emoteId = 255;
            public float emotePhase;
            // client: free-running anim phase (run cadence) + last interpolated pos (for move speed).
            public float animPhase;
            public Vector3 lastInterpPos;
            public bool hasLastInterp;
        }

        GameInput _input;
        GameCamera _cam;
        BallController _ball;
        Crosser _crosser;
        Transform _goal;   // for the Broadcast/replay camera's GroupCenter framing only
        AimReticle _reticle;
        Transform _launch;
        NetSession _s;
        // For bodies spawned MID-MATCH (a seat changing hands): what Configure was given.
        Camera _rawCam;
        Material _torso, _limb, _glove;
        Transform _spawnRoot;
        // Where the human crosser was last placed from the panel (host), so a relay that did not
        // move the spot does not re-plant him.
        Vector3 _lastPlacedSpot;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;

        uint _tick;
        int _goals;
        string _flash = ""; float _flashTime;
        float _goalLineZ;


        // Cross-targeting map (M). The exact panel single-player uses (CrossMap.DrawOverlay), so the
        // two cannot drift apart - only the permissions differ.
        //
        // OPEN TO EVERYONE, and everyone may EDIT while the crosser is an AI: an edit is a request to
        // the host (NetSession.RequestCrosserSetup), the host validates and relays it, and every peer
        // - the editor included - adopts the relay. That single ordering is why two players dragging
        // the same slider cannot end up disagreeing. Only the host actually SIMULATES the crosser, so
        // only the host writes the values into SimConfig/Crosser.
        //
        // When a HUMAN holds the crosser seat the panel goes read-only for everyone (they aim their
        // own deliveries), except the host's crosser dropdown, which is how the seat is handed back.
        bool _crossOpen;   // panel up? (the SETTINGS are the replicated NetSession.CrosserSetup)
        bool CrossMapAvailable => _s != null && _crosser != null;
        // Pending local edit, coalesced so a slider DRAG is a few packets rather than one per frame
        // (this rides the reliable channel). Flushed by PublishCrossEditIfDue.
        bool _crossDirty;
        float _crossNextPublish;
        const float CrossPublishInterval = 0.1f;
        // A crosser reassignment picked on the panel, applied from Update (see DrawCrossOverlay).
        // -2 = none, -1 = the AI, >= 0 = that slot: a human to move in, or (with _pendingAssignAi) an
        // AI seat whose Clanker swaps into the crosser seat.
        int _pendingAssign = -2;
        bool _pendingAssignAi;
        // The plain AI server's look (the bootstrap's colours), built once and reused across every
        // hand-back so a seat that changes hands a few times does not leak a material a time.
        Material _aiCrosserTorso, _aiCrosserLimb;
        // The replay recorder holds transforms; a body rebuilt while a replay was ROLLING could not
        // be re-tracked at the time, so it is re-tracked when that replay ends.
        bool _replayStale;

        bool _localIsCrosser;
        bool _localIsKeeper;

        // Emote wheel (B): pick a celebration; it plays on the local body and syncs to everyone.
        bool _wheelOpen;
        int _wheelPage;   // which of Celebration.Pages is showing (arrows cycle it)
        QuickChatFeed _qcFeed;   // multiplayer quickchat feed + custom-text entry

        // Per-machine post-goal replay (each peer replays its own local view).
        ReplaySystem _replay;
        bool _replaying;
        // Host-only: after a goal, keep playing live for ReplayHold seconds (the recorder keeps
        // buffering the ball settling in the net) before freezing + rolling the replay, so most
        // of the replay is AFTER the ball crosses the line. >0 = counting down to BeginReplay.
        float _goalHold;

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball, Crosser crosser,
                              AimReticle reticle, Transform launch, Transform goal,
                              Material torso, Material limb, Material glove, Transform root)
        {
            _input = input; _cam = gameCam; _ball = ball; _crosser = crosser; _reticle = reticle; _launch = launch;
            _goal = goal;
            _rawCam = cam; _torso = torso; _limb = limb; _glove = glove; _spawnRoot = root;
            _ball.NoCarry = true;   // striker mode has no carry: a dead touch is pushed clear of his feet
            _s = Multiplayer.Session;
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _goalLineZ = SimConfig.GoalCenter.z;
            _s.MatchEvent += OnMatchEvent;
            _s.BallKicked += OnBallKicked;
            _s.PostHit += OnPostHit;
            _qcFeed = gameObject.AddComponent<QuickChatFeed>();
            _qcFeed.Bind(_s);

            // Spawn a body per active slot from the roster (keeper slot 0, crosser slot N-1,
            // shooters between).
            foreach (var slot in _s.Roster)
                SpawnBody(slot.slot, torso, limb, glove, root);

            // Camera + role flags follow the LOCAL body (and re-follow it if my seat changes).
            AttachCamera();

            // The crosser panel is replicated (NetSession.CrosserSetup). The HOST seeds the session
            // from whatever this machine had dialled in - carrying a single-player setup into the
            // match it hosts - and that publish is what every client adopts. A client instead takes
            // the session's value, because the host's is the one that counts.
            if (_s.IsHost) _s.SeedCrosserSetup(CrossMap.ToWire(CrossMap.Session, _s.CrosserAiName));
            else CrossMap.FromWire(ref CrossMap.Session, _s.CrosserSetup);
            _lastPlacedSpot = CrossMap.Session.spot;
            _s.CrosserSetupChanged += OnCrosserSetupChanged;

            // Seed the world from the cross panel before the first serve: it owns shot speed + cross
            // interval now that they are off the pre-match screen. Host only - it is the only peer
            // that serves, and the only one whose copy is read.
            if (_s.IsHost) { CrossMap.Apply(CrossMap.Session, _crosser); _crosser.Arm(SimConfig.ServeFirstDelay); _ball.ResetTo(_launch.position); }

            SetupReplay();
            _s.ReplayStarted += OnReplayStarted;
            _s.ReplayEnded += OnReplayEnded;
            _s.JerseyUpdated += OnJerseyUpdated;
            _s.RosterChanged += OnRosterChanged;

            SyncKeeperVisibility();   // a None keeper is hidden from the first frame on a client too
            LockCursor();
        }

        // Point the camera at MY body and refresh the role flags the HUD + input routing read. Called
        // at build and again whenever my seat changes mid-match (the host reassigning the crosser).
        // GameCamera.Init only re-targets - it keeps the yaw/pitch the player was looking with - so a
        // player who stops crossing keeps their view and simply finds it on their new body.
        void AttachCamera()
        {
            var me = _bodies[_localSlot];
            _localIsCrosser = me != null && me.isCrosser;
            _localIsKeeper = me != null && me.isKeeper;
            if (me == null || me.ragdoll == null || me.ragdoll.Pelvis == null) return;

            // Single-player's own Striker builder passes the real crosser + goal into the same
            // slots (GameBootstrap.BuildStrikerMode) so the Broadcast/replay camera's
            // GroupCenter can widen its framing to include them after a shot; this was passing
            // null for both despite already holding a live _crosser reference, giving every
            // networked single-goal match a visibly tighter post-shot framing than its
            // single-player twin.
            Transform crosserT = _crosser != null && _crosser.Ragdoll != null && _crosser.Ragdoll.Pelvis != null
                                ? _crosser.Ragdoll.Pelvis.transform : null;
            _cam.Init(_rawCam, _ball.transform, me.ragdoll.Pelvis.transform, crosserT, _goal);
            if (_localIsKeeper)
            {
                // Human keeper: identical to single-player goalkeeper mode. The camera pans
                // in a cone from a FIXED forward base; the keeper reads that same cone yaw
                // (KeeperLookYaw) and turns his body to it, so body + camera stay in lock-step.
                _cam.SetKeeperFollow(me.ragdoll.Pelvis.transform,
                                     () => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up),
                                     () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
            }
            else
            {
                _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
                if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
                // A local human crosser aims with the SAME camera yaw his Striker turns to, so
                // CrosserControl's solve never disagrees with which way his body is facing.
                // Pitch too: looking up floats the cross.
                if (me.crosserCtl != null) me.crosserCtl.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
            }
        }

        void SpawnBody(int slot, Material torso, Material limb, Material glove, Transform root)
        {
            bool keeper  = slot == 0;
            bool crosser = slot == NetSession.CrosserSlot;
            bool isLocal = slot == _localSlot;
            bool hostSim = _s.IsHost;

            // Read this slot's state from the SYNCED roster (authoritative on host AND client),
            // NOT _slotOwner (which is host-only). A slot is "human", "ai" (a Clanker the host
            // left on), or empty (open). occupied = anything that should have a body.
            var rosterSlot = _s.RosterSlot(slot);
            bool human = rosterSlot.human;
            bool ai    = rosterSlot.ai;
            bool occupied = human || ai;

            // The CROSSER slot always has a ball-feeder on the host: if no human holds it, the AI
            // auto-serve loop runs (regardless of the slot's AI toggle) so crosses keep coming and
            // shooters can always call for a pass. Only a CLIENT with an unheld crosser slot idles
            // it (the client never sims the feeder; it just renders the host's ball). This is what
            // makes the MP AI crosser cross consistently instead of standing idle.
            if (crosser)
            {
                if (hostSim) { SpawnCrosserBody(slot, isLocal, hostSim, human); return; }
                if (!isLocal && !occupied) { if (_crosser != null) _crosser.Idle(); return; }
                SpawnCrosserBody(slot, isLocal, hostSim, human);
                return;
            }

            // An empty (open) non-crosser slot spawns nothing: no AI keeper, no inert shooter.
            // The local slot is always the human themselves, so it's never skipped.
            if (!occupied && !isLocal) return;

            var go = new GameObject("NetSlot" + slot);
            go.transform.SetParent(root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();
            Vector3 start = SlotStart(slot, keeper);
            var facing = Quaternion.LookRotation(keeper ? SimConfig.KeeperFaceDir : Vector3.forward, Vector3.up);
            // A HUMAN slot wears its synced appearance (skin + head cosmetics), keeper or not. Give
            // it its OWN limb material (a copy) so the per-slot skin tint doesn't mutate the shared
            // one used by other bodies. A human keeper still gets gloves on top of the cosmetics
            // (gloves + appearance are independent branches in Build). AI bodies use the shared
            // limb material, no cosmetics.
            bool wantsLook = human;
            Material slotLimb = wantsLook ? Make.Mat(rosterSlot.appearance.Skin) : limb;
            PlayerAppearance? appr = wantsLook ? rosterSlot.appearance : (PlayerAppearance?)null;
            // Per-slot painted jersey: a human's own networked kit if it has arrived, else the
            // shared team torso (also the fallback for AI / not-yet-received jerseys). A late
            // arrival is swapped in live via OnJerseyUpdated below.
            Texture2D jt = human ? _s.JerseyForSlot(slot) : null;
            Material slotTorso = jt != null ? Make.MatTex(jt) : torso;
            ragdoll.Build(start, facing, slotTorso, slotLimb, withGloves: keeper && glove != null, appearance: appr);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, wasHuman = human, targetPos = start, targetYaw = facing.eulerAngles.y };

            if (!keeper)
            {
                var striker = go.AddComponent<Striker>();
                b.striker = striker;
                // Striker mode is VOLLEY ONLY: no carry at all. The Dribble component is still
                // built and bound (SetDribble/Init keep every downstream null check happy and the
                // shot paths read it), but it stays DISABLED, so the ball never leashes to his feet
                // and a settled ball is struck where it lies instead of being nudged along.
                var dribble = go.AddComponent<Dribble>();
                striker.SetDribble(dribble);
                if (hostSim)
                {
                    if (isLocal) striker.Init(_input, ragdoll);          // host's own device
                    else
                    {
                        b.netInput = new NetInputSource(); striker.Init(b.netInput, ragdoll);
                        // A remote striker AIMS with his own camera, off the wire. Without this his
                        // volleys launched down whatever way his body happened to be facing.
                        striker.SetCameraYaw(() => b.netInput != null ? b.netInput.LookYaw : 0f,
                                             () => b.netInput != null ? b.netInput.LookPitch : 0f);
                    }
                    dribble.Init(isLocal ? (IStrikerInput)_input : b.netInput, striker, ragdoll, _ball);
                    dribble.Enabled = false;
                    AttachKick(ragdoll, striker);
                    // Host sims every outfield body's emote on the real ragdoll (so its pose +
                    // phase can be streamed to clients).
                    b.celeb = go.AddComponent<Celebration>(); b.celeb.Init(ragdoll);
                }
                else
                {
                    if (isLocal)
                    {
                        striker.Init(_input, ragdoll);                   // client-predicted local player
                        dribble.Init(_input, striker, ragdoll, _ball);   // bound, but left disabled (host sims it)
                        // The owner plays their own emote locally on the real body for instant feedback.
                        b.celeb = go.AddComponent<Celebration>(); b.celeb.Init(ragdoll);
                    }
                    else { striker.ControlEnabled = false; ragdoll.BecomeDisplayBody(); }  // remote puppet
                }
            }
            else if (!hostSim)
            {
                ragdoll.BecomeDisplayBody();   // client keeper puppet
            }
            else if (!human)
            {
                // Host keeper, AI (Clanker) in the slot: AI goaltender. (An open slot already
                // returned above, so reaching here with !human means ai.)
                var gk = go.AddComponent<Goalkeeper>(); gk.Init(ragdoll, _ball); b.ai = gk;
            }
            else
            {
                // Host keeper, a human holds slot 0: drive the real KeeperController from the
                // local device (host keeper) or this slot's NetInputSource (remote keeper).
                var kc = go.AddComponent<KeeperController>();
                if (isLocal) { kc.Init(_input, ragdoll, _ball); }
                else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll, _ball); }
                // Local keeper reads the cone yaw (KeeperLookYaw) so body + camera lock-step, exactly
                // like single-player. _cam.Yaw is stale in KeeperFollow mode. Remote keepers read the
                // yaw streamed over the wire (also the cone yaw; see SampleFrame below).
                kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.KeeperLookYaw) : (() => b.netInput != null ? b.netInput.LookYaw : 0f));
                b.keeper = kc;
                // Human keepers can emote too (host sims it on the real body -> streamed out).
                b.celeb = go.AddComponent<Celebration>(); b.celeb.Init(ragdoll);
            }

            _bodies[slot] = b;
        }

        // Per-machine replay over this peer's local bodies + ball. Each machine plays back what IT
        // recorded (host = true physics, clients = their interpolated view). Re-run whenever a body
        // is rebuilt (the crosser seat changing hands): the recorder holds transforms, and the new
        // body would otherwise never appear in a replay.
        void SetupReplay()
        {
            if (_replay == null) _replay = gameObject.AddComponent<ReplaySystem>();
            else if (_replay.IsPlaying) { _replayStale = true; return; }   // re-track once it ends
            _replayStale = false;
            var tracked = new List<Transform> { _ball.transform };
            var drivers = new List<MonoBehaviour>();
            for (int i = 0; i < _bodies.Length; i++)
                if (_bodies[i] != null) ReplaySystem.TrackBody(tracked, drivers, _bodies[i].ragdoll);
            _replay.Setup(tracked, drivers, SimConfig.ReplayWindow);
        }

        /// <summary>
        /// Rebuild the shared crosser body for whoever holds the seat. A HUMAN wears their own
        /// synced look - species, build, skin, cosmetics and painted kit, exactly as a shooter body
        /// does in SpawnBody; the AI server is the plain orange body the bootstrap builds. The one
        /// bootstrap body used to serve everyone, so a human in the seat crossed as an orange
        /// default. Runs on host and client alike (both hold a body per seat); the caller then fits
        /// it (FitHumanCrosser / RestoreAiCrosser). The Crosser's own reference, the bubble and the
        /// replay recorder are re-pointed here; the ball's ignore list is set by the fit.
        /// </summary>
        ActiveRagdoll RebuildCrosserBody(int slot, bool human)
        {
            var old = _crosser.Ragdoll;
            // Where the old body stood (the panel's spot at worst), facing the goal.
            Vector3 pos = CrossMap.Session.spot; pos.y = 0f;
            Vector3 toGoal = SimConfig.GoalCenter - pos; toGoal.y = 0f;
            Quaternion facing = toGoal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toGoal.normalized, Vector3.up)
                                                            : Quaternion.identity;
            if (old != null && old.Pelvis != null) { pos = old.Pelvis.position; pos.y = 0f; facing = old.FacingRotation; }

            Material torso, limb; PlayerAppearance? appr = null;
            if (human)
            {
                var rs = _s.RosterSlot(slot);
                limb = Make.Mat(rs.appearance.Skin);
                appr = rs.appearance;
                var jt = _s.JerseyForSlot(slot);              // a late kit still lands via OnJerseyUpdated
                torso = jt != null ? Make.MatTex(jt) : _torso;
            }
            else
            {
                _aiCrosserTorso ??= Make.Mat(new Color(0.85f, 0.5f, 0.2f));
                _aiCrosserLimb  ??= Make.Mat(new Color(0.65f, 0.38f, 0.15f));
                torso = _aiCrosserTorso; limb = _aiCrosserLimb;
            }

            // Deferred Destroy on purpose: the teardown that follows a hand-back (RestoreAiCrosser)
            // may still read the old body this frame. Never the Crosser's own object.
            if (old != null && old.gameObject != _crosser.gameObject) Destroy(old.gameObject);
            var go = new GameObject("Body");
            go.transform.SetParent(_crosser.transform, false);
            var rag = go.AddComponent<ActiveRagdoll>();
            rag.Build(pos, facing, torso, limb, withGloves: false, appearance: appr);
            _crosser.SetRagdoll(rag);
            var bub = _crosser.GetComponent<CrosserBubble>();
            if (bub != null) bub.Init(rag);
            SetupReplay();
            return rag;
        }

        // The crosser slot reuses the pre-built _crosser (its ragdoll is already placed on the
        // wing). Host + human -> CrosserControl drives it (AutoServe off). Host + no human ->
        // the AI auto-serve loop (unchanged). Client -> display puppet.
        void SpawnCrosserBody(int slot, bool isLocal, bool hostSim, bool human)
        {
            var ragdoll = _crosser.Ragdoll;
            var b = new Body { ragdoll = ragdoll, isCrosser = true, wasHuman = human };
            if (ragdoll != null && ragdoll.Pelvis != null)
            {
                b.targetPos = ragdoll.Pelvis.position; b.targetPos.y = 0f;
                b.targetYaw = ragdoll.FacingRotation.eulerAngles.y;
            }

            if (human) FitHumanCrosser(b, slot, isLocal, hostSim);
            else if (hostSim) RestoreAiCrosser(ragdoll);
            else if (ragdoll != null) ragdoll.BecomeDisplayBody();   // client: the AI crosser is a puppet

            _bodies[slot] = b;
        }

        /// <summary>
        /// Wire the shared crosser body for a HUMAN in the seat - at match build, or when the host
        /// hands the seat over mid-match (AdoptHumanCrosser). One implementation for both, and for
        /// every peer: the host drives the real Striker + CrosserControl off the device or the wire;
        /// a client's own crosser is predicted with a DISPLAY-ONLY stance (meter, run-up, swing -
        /// the ball stays the host's, streamed); anyone else's crosser is a puppet.
        /// </summary>
        void FitHumanCrosser(Body b, int slot, bool isLocal, bool hostSim)
        {
            b.wasHuman = true; b.ai = null;
            // The seat's body becomes THIS player's own look before anything is wired to it.
            b.ragdoll = RebuildCrosserBody(slot, human: true);
            var ragdoll = b.ragdoll;
            if (ragdoll != null && ragdoll.Pelvis != null)
            {
                b.targetPos = ragdoll.Pelvis.position; b.targetPos.y = 0f;
                b.targetYaw = ragdoll.FacingRotation.eulerAngles.y;
                b.hasLastInterp = false;
            }
            // Idle, not just AutoServe off: a serve the AI had already telegraphed would otherwise
            // still fire from the new human's feet a moment later. Idle cancels it and drops the
            // reticle.
            _crosser.Idle();                                         // a human decides deliveries
            _crosser.Cosmetic = false;                               // a Striker owns pose + movement
            _crosser.ServeFromFeet = true;                           // launch from where they stand
            if (ragdoll == null) return;

            if (!hostSim && !isLocal) { ragdoll.BecomeDisplayBody(); return; }   // remote crosser puppet

            // A LIVE body from here: the host's real one, or the client's own predicted one. Mid-
            // match this body may have been a kinematic puppet a moment ago (a client that just
            // became the crosser), so give it its physics back before anything drives it.
            ragdoll.BecomeLiveBody();
            ragdoll.LocomotionEnabled = true;                        // un-plant: he walks like a shooter

            if (hostSim)
            {
                // A HUMAN crosser strikes off his own feet and moves freely, so the AI-only ball
                // shield + protective bubble must NOT apply. Off, not destroyed: the stance turns the
                // bubble back on while he is set (CrosserControl), and the AI re-inits it if it
                // takes the seat back.
                _ball.IgnoreBody(ragdoll, false);
                var bub = _crosser.GetComponent<CrosserBubble>();
                if (bub != null) bub.enabled = false;
            }

            // Movement: a Striker off the device (local) or the wire (remote, host only). The
            // crosser's own Striker must not ALSO read LMB/RMB as a shot - those are the cross.
            var striker = ragdoll.GetComponent<Striker>();
            if (striker == null) striker = ragdoll.gameObject.AddComponent<Striker>();
            b.striker = striker;
            IStrikerInput src;
            if (isLocal) src = _input;
            else { if (b.netInput == null) b.netInput = new NetInputSource(); src = b.netInput; }
            striker.Init(src, ragdoll);
            striker.ControlEnabled = true;
            striker.ShootingEnabled = false;
            if (!isLocal)
            {
                // A remote human crosser AIMS with his own camera, off the wire - same source his
                // Striker's facing already uses (see SpawnBody's non-crosser twin of this).
                striker.SetCameraYaw(() => b.netInput != null ? b.netInput.LookYaw : 0f,
                                     () => b.netInput != null ? b.netInput.LookPitch : 0f);
            }

            // The kick is THEIRS: a remote player's footedness comes off the wire
            // (NetSession.LeftFootedForSlot) and their passing off their synced loadout - not the
            // host's own profile, which is what every remote kick used to be animated and
            // scattered with.
            bool footed = isLocal ? PlayerProfile.LeftFooted : _s.LeftFootedForSlot(slot);
            float acc; bool maestro;
            if (isLocal) { acc = LocalPassAcc(); maestro = PlayerProfile.PerkMaestro; }
            else
            {
                _s.PassStatsForSlot(slot, out _, out float accMul, out maestro);
                acc = Mathf.Clamp01((accMul - 1f) / 0.85f);
            }
            var cc = _crosser.GetComponent<CrosserControl>();
            if (cc == null) cc = _crosser.gameObject.AddComponent<CrosserControl>();
            cc.Init(src, _crosser, _ball, ragdoll, striker, displayOnly: !hostSim,
                    leftFooted: footed, passAcc01: acc, maestro: maestro);
            if (!isLocal) cc.SetCameraYaw(() => b.netInput != null ? b.netInput.LookYaw : 0f,
                                          () => b.netInput != null ? b.netInput.LookPitch : 0f);
            // (A LOCAL crosser's camera sources are wired by AttachCamera.)
            b.crosserCtl = cc;
        }

        /// <summary>
        /// Put the crosser back to the AI auto-serve loop (planted, cosmetic swing, panel applied).
        /// Shared by the two ways that happens - spawning into an AI crosser slot, and a human
        /// crosser leaving/being replaced mid-match - because the leave path used to only flip
        /// AutoServe back on, leaving the body mobile and Striker-driven with the panel unapplied.
        /// </summary>
        void RestoreAiCrosser(ActiveRagdoll ragdoll)
        {
            if (_crosser == null) return;
            // Fully restore the planted state in case a human previously held this slot and left it
            // mobile (Striker-driven, locomotion on): re-plant the ragdoll, drop any Striker, and
            // re-arm the serve loop so it feeds balls consistently instead of standing idle.
            // IMMEDIATE, not deferred. A reassignment can hand the seat from one human to another
            // inside one frame (two roster pushes back to back): the AI takes it here, then
            // FitHumanCrosser re-fits it for the newcomer. With a deferred Destroy that re-fit would
            // GetComponent the DYING Striker/CrosserControl, wire the new player to it, and lose him
            // at end of frame. Teardown first so the stance leaves nothing behind on the body.
            var strayCc = _crosser.GetComponent<CrosserControl>();
            if (strayCc != null) { strayCc.Teardown(); DestroyImmediate(strayCc); }
            var stray = ragdoll != null ? ragdoll.GetComponent<Striker>() : null;
            if (stray != null) DestroyImmediate(stray);
            _crosser.Cosmetic = true;
            _crosser.ServeFromFeet = false;
            _crosser.AutoServe = true;
            // Re-apply the panel BEFORE planting: reverting the slot to AI is exactly when the
            // target/delivery/cadence have to come back (a human crosser ignored all of them),
            // and PlantAt faces him at TargetOverride, so the target has to be current first.
            CrossMap.Apply(CrossMap.Session, _crosser);
            if (ragdoll != null)
            {
                ragdoll.UprightLock = true;
                ragdoll.LocomotionEnabled = false;
                ragdoll.MoveInput = Vector3.zero;
                // Plant through the Crosser so the spot + facing are RECORDED, not just applied
                // once: an inline ResetTo leaves _plantHome null, so the drift backstop is dead
                // and every contact hop walks him ~0.6 m off the wing toward the shooters with
                // nothing pulling him back. PlantAt (not SetOrigin) keeps Origin at the fixed
                // _launch point, which is what the ball resets to on the wire. It also faces him
                // at the delivery target rather than the goal centre, which is where he kicks.
                // The cross panel's placed spot, not the fixed default: a host who moved him on
                // the map and then had a human take and drop the crosser slot would otherwise
                // find him snapped back to the wing with the panel still showing where he was
                // meant to be.
                _crosser.PlantAt(CrossMap.Session.spot);
                // AI/planted server: the ball must never touch his body, and no other player may
                // crowd him. Ignore ball<->crosser collisions and wrap him in a protective bubble
                // (ejects other players, lets the ball pass). Host-side only (physics runs here).
                if (_s != null && _s.IsHost)
                {
                    _ball.IgnoreBody(ragdoll, true);
                    // Get-or-add and (re)Init + enable, not add-if-absent: a human crosser's stance
                    // leaves its bubble on this same object DISABLED (CrosserControl.ExitStance), and
                    // an add-if-absent would find it and leave the AI with a bubble that is off.
                    var bub = _crosser.GetComponent<CrosserBubble>();
                    if (bub == null) bub = _crosser.gameObject.AddComponent<CrosserBubble>();
                    bub.Init(ragdoll);
                    bub.enabled = true;
                }
            }
            _crosser.Arm(SimConfig.ServeFirstDelay);                  // start the serve countdown now
            // On a CLIENT the AI crosser is a puppet of the host's snapshots. This body may have just
            // been this client's own predicted crosser (they were moved to a shooter seat), which
            // is a live physics body - make it a puppet, or the snapshots and the physics fight.
            if (_s != null && !_s.IsHost && ragdoll != null) ragdoll.BecomeDisplayBody();
        }

        // A player left mid-match: the roster row for their slot is no longer human. Despawn that
        // body so it doesn't freeze as a statue for everyone. A keeper slot swaps to an AI keeper
        // (play must continue with someone in goal); shooter/crosser bodies just disappear. Runs on
        // host + client (both hold a body per slot); the host also stops driving/broadcasting it.
        // The authoritative crosser panel changed (someone edited it, or the host renamed the AI).
        // Adopt it into the panel every peer draws, and - on the host, the only peer that serves -
        // push it at the live crosser so the very next cross uses it.
        void OnCrosserSetupChanged()
        {
            // Not while THIS peer has an edit in flight: the relay we are about to adopt may be the
            // echo of an older packet of our own, and writing it back over a slider the player is
            // still dragging makes the control visibly fight the hand holding it. Our own pending
            // edit is newer by definition, and it publishes a moment later anyway.
            if (!_crossDirty) CrossMap.FromWire(ref CrossMap.Session, _s.CrosserSetup);
            if (!_s.IsHost || _crosser == null) return;

            // A HUMAN crosser moved his own spot (the only thing he can set): put him there, facing
            // the goal - unless he is mid-stance, where a teleport would tear the kick apart. Only
            // when the spot actually changed, so a relay about anything else does not re-plant him.
            var cb = _bodies[NetSession.CrosserSlot];
            if (cb != null && cb.wasHuman)
            {
                Vector3 spot = CrossMap.Session.spot; spot.y = 0f;
                bool moved = (spot - _lastPlacedSpot).sqrMagnitude > 0.01f;
                bool free = cb.crosserCtl == null || !cb.crosserCtl.InStance;
                if (moved && free && cb.ragdoll != null)
                {
                    Vector3 toGoal = SimConfig.GoalCenter - spot; toGoal.y = 0f;
                    var facing = toGoal.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toGoal.normalized, Vector3.up)
                                                             : cb.ragdoll.FacingRotation;
                    cb.ragdoll.ResetTo(spot, facing);
                    _lastPlacedSpot = spot;
                }
                return;
            }

            CrossMap.Apply(CrossMap.Session, _crosser);
            // Re-plant the AI server on its spot (a human crosser walks; handled above).
            if (_crosser.AutoServe) _crosser.SetOrigin(CrossMap.Session.spot);
            _lastPlacedSpot = CrossMap.Session.spot;
        }

        // The roster changed: someone left, or the host moved a player between seats (the crosser
        // dropdown). Runs on host + client alike; every peer keeps a body per seat.
        void OnRosterChanged()
        {
            // The match config rides the same push. The host's in-match setup can resize the goal /
            // re-set the AI keeper mid-match: take it here so every client's goal matches the host's.
            ApplyConfigGoal();

            // 1. Did MY seat move? Take it now, so the spawns below know which body is mine.
            int mySlot = _s.LocalSlot;
            bool localMoved = mySlot >= 0 && mySlot < NetSession.MaxSlots && mySlot != _localSlot;
            if (localMoved) _localSlot = mySlot;
            bool localRebuilt = false;
            bool crosserRebuilt = false;   // the shared crosser body was replaced: the camera's framing holds it

            // 2. Humans who LEFT a seat (left the match, or were moved to another seat).
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.wasHuman) continue;      // only human-spawned bodies react to a leave
                if (_s.RosterSlot(i).human) continue;         // still human: nothing changed

                if (i == 0)
                {
                    // Keeper: swap to an AI keeper in place so the goal stays covered.
                    if (b.striker != null) b.striker.ControlEnabled = false;
                    b.keeper = null; b.netInput = null; b.wasHuman = false;
                    if (_s.IsHost && b.ai == null && b.ragdoll != null)
                    { var gk = b.ragdoll.gameObject.AddComponent<Goalkeeper>(); gk.Init(b.ragdoll, _ball); b.ai = gk; }
                    continue;
                }
                // Crosser: the crosser ragdoll is a shared prebuilt object (not a per-slot spawn),
                // so don't destroy it - just hand it back to the AI auto-serve loop.
                if (b.isCrosser)
                {
                    b.striker = null; b.netInput = null; b.crosserCtl = null; b.wasHuman = false;
                    // Back to the plain AI body: the human's look leaves with them.
                    b.ragdoll = RebuildCrosserBody(i, human: false);
                    b.hasLastInterp = false;
                    crosserRebuilt = true;
                    RestoreAiCrosser(b.ragdoll);
                    continue;
                }
                // Shooter: remove the body (no shooter AI in striker mode).
                if (b.ragdoll != null) Destroy(b.ragdoll.gameObject);
                _bodies[i] = null;
            }

            // 3. Humans who ARRIVED in a seat mid-match (the other half of a move). The crosser seat
            //    re-fits its shared body for the newcomer; any other seat gets a fresh body, on top
            //    of whatever AI was sitting there.
            var roster = _s.Roster;
            for (int r = 0; r < roster.Length; r++)
            {
                if (!roster[r].human) continue;
                int i = roster[r].slot;
                if (i < 0 || i >= _bodies.Length) continue;
                var b = _bodies[i];
                if (b != null && b.wasHuman) continue;        // already a human's body: nothing to do
                if (i == NetSession.CrosserSlot)
                {
                    if (b != null) { FitHumanCrosser(b, i, i == _localSlot, _s.IsHost); crosserRebuilt = true; }
                }
                else
                {
                    if (b != null && b.ragdoll != null) Destroy(b.ragdoll.gameObject);   // e.g. the AI keeper
                    _bodies[i] = null;
                    SpawnBody(i, _torso, _limb, _glove, _spawnRoot);
                }
                if (i == _localSlot) localRebuilt = true;
            }

            // 4. Whoever moved keeps their view: the camera re-targets to my (new) body, and the
            //    role flags the HUD + input routing read follow it.
            if (localMoved || localRebuilt || crosserRebuilt) AttachCamera();

            SyncKeeperVisibility();
        }

        // A CLIENT'S keeper puppet has no Goalkeeper to park itself: hide it when the host's AI
        // keeper is at None (the host's real keeper is parked off the pitch, so the puppet would be
        // standing 80 m away for anyone who looked), show it again above None. A HUMAN keeper is
        // never hidden, and the host's own body is handled by its Goalkeeper.
        void SyncKeeperVisibility()
        {
            if (_s == null || _s.IsHost) return;
            var kb = _bodies[0];
            if (kb == null || kb.ragdoll == null || kb.wasHuman) return;
            Goalkeeper.SetVisible(kb.ragdoll, SimConfig.KeeperAbility > 0.001f);
        }

        // Goal size + AI keeper from the synced config. Only when the goal actually changed size does
        // it get rebuilt (Arena.RebuildGoal); on the host that is already done by GoalSetup.Apply, so
        // the push it makes lands here as a no-op.
        void ApplyConfigGoal()
        {
            var cfg = _s.Config;
            float sw = cfg.goalScale <= 0.01f ? 1f : cfg.goalScale;
            float sh = cfg.goalScaleH <= 0.01f ? sw : cfg.goalScaleH;
            float w = SimConfig.GoalWidthBase * sw, h = SimConfig.GoalHeightBase * sh;
            SimConfig.KeeperAbility = Mathf.Clamp01(cfg.keeperAbility);
            if (Mathf.Approximately(w, SimConfig.GoalWidth) && Mathf.Approximately(h, SimConfig.GoalHeight)) return;
            SimConfig.GoalWidth = w; SimConfig.GoalHeight = h;
            Arena.RebuildGoal();
        }

        // A slot's networked jersey finished arriving after its body was built: swap the torso kit
        // live so the remote player's painted jersey shows without a rebuild.
        void OnJerseyUpdated(int slot)
        {
            if (slot < 0 || slot >= _bodies.Length) return;
            var b = _bodies[slot];
            if (b == null || b.ragdoll == null) return;
            var tex = _s.JerseyForSlot(slot);
            if (tex != null) b.ragdoll.SetTorsoMaterial(Make.MatTex(tex));
        }

        static Vector3 SlotStart(int slot, bool keeper)
        {
            if (keeper) return SimConfig.KeeperStart;
            // Fan shooters across the edge of the box.
            float x = (slot - 2) * 2.2f;
            return SimConfig.StrikerStart + new Vector3(x, 0f, 0f);
        }

        void AttachKick(ActiveRagdoll ragdoll, Striker striker)
        {
            // Striking bones come from the body's layout: a biped's feet and calves, a quadruped's
            // front hooves. Both sides are listed so a bicycle off either limb classifies.
            // Matches GameBootstrap.
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
            // A miss is an audio-only crowd reaction on clients (occasional boos); don't splash a
            // "MISS" callout across every HUD. The host already booed + skipped the flash locally.
            if (tag == "MISS") { AudioManager.Instance?.PlayMissBoosMaybe(); return; }

            Flash(tag);
            // On a goal, stand the LOCAL striker back up. A trick finish (diving header / bicycle)
            // leaves him prone + limp, and his Tick() is suspended through the goal hold + replay,
            // so without this he'd stay slumped on the deck for the whole celebration. Only the
            // local body has a live Striker to recover (remote bodies are host-driven / puppeted).
            if (tag == "GOAL!") { RecoverLocalStriker(); AudioManager.Instance?.PlayGoalCelebration(); }
        }

        // Client: host reported a ball-body contact at `pos`; play the 3D kick thud there
        // (attenuated to this player's own position by the 10 m rolloff).
        void OnBallKicked(Vector3 pos) => AudioManager.Instance?.PlayBallKick(pos);
        void OnPostHit(Vector3 pos, float speed) => AudioManager.Instance?.PlayPostHit(pos, speed);
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Pop the local striker upright out of any trick/limp pose. Safe to call on host or client
        // (the local shooter always owns a real Striker); no-op for a local keeper/crosser body.
        void RecoverLocalStriker()
        {
            var me = _bodies[_localSlot];
            if (me != null && me.striker != null) me.striker.ForceRecover();
        }

        // Emote wheel open/close: free the cursor so the radial menu is clickable, re-lock on close.
        void SetWheelOpen(bool open)
        {
            _wheelOpen = open;
            GameInput.CaptureCursor(!open);
        }

        // Cross map open/close. Mirrors GameManager.SetCrossMapOpen: free the cursor while placing,
        // hold the view still, and on close settle the crosser.
        void SetCrossMapOpen(bool open)
        {
            _crossOpen = open;
            CrossMap.NoteOpenState(open);   // so PauseMenu skips the Escape that closed this
            GameInput.CaptureCursor(!open);
            if (_cam != null) _cam.FreezeLook = open;
            if (!open)
            {
                PublishCrossEditIfDue(force: true);   // the last slider position must not be swallowed
                CrossMap.CancelTransientUI();   // drop a half-open dropdown / half-typed rename
                // HOST ONLY, and only for the AI server: it is the only peer that simulates the
                // crosser, and SetOrigin would yank a human crosser off his own feet.
                if (_s != null && _s.IsHost && _crosser != null && _crosser.AutoServe)
                {
                    CrossMap.Apply(CrossMap.Session, _crosser);
                    _crosser.SetOrigin(CrossMap.Session.spot);
                }
            }
        }

        /// <summary>
        /// Draw the shared cross panel with this peer's permissions, then carry out whatever it
        /// asked for. Every change goes through the session (RequestCrosserSetup) rather than being
        /// applied locally, so the host's relay is the single point at which any peer - the editor
        /// included - adopts a new value.
        /// </summary>
        void DrawCrossOverlay()
        {
            bool humanCrosser = _s.RosterSlot(NetSession.CrosserSlot).human;
            bool isCrosser = humanCrosser && _localSlot == NetSession.CrosserSlot;
            var meBody = _bodies[_localSlot];
            bool inStance = isCrosser && meBody?.crosserCtl != null && meBody.crosserCtl.InStance;
            var candidates = _s.IsHost ? CrosserCandidates() : null;
            var perms = new CrossMap.Perms
            {
                // AI crossing: everyone may place it, aim it and set how it serves - it is where
                // their own crosses come from and arrive. Human crossing: only that human may move
                // his own spot (and not mid-stance), and the AI's controls are not shown at all.
                canEditTarget = !humanCrosser,
                canEditSpot = humanCrosser ? (isCrosser && !inStance) : true,
                aiControls = !humanCrosser,
                isHost = _s.IsHost,
                networked = true,
                humanCrosser = humanCrosser,
                isCrosser = isCrosser,
                // Live whenever it has something to offer: another human, or the AI while a human
                // crosses. A host alone can still hand the seat to the AI and go play as a striker.
                dropdownEnabled = _s.IsHost && (humanCrosser || (candidates != null && candidates.Count > 0)),
                crosserName = humanCrosser ? _s.RosterSlot(NetSession.CrosserSlot).name : _s.CrosserAiName,
                aiName = _s.CrosserAiName,
                candidates = candidates,
            };

            var res = CrossMap.DrawOverlay(ref CrossMap.Session, _crosser, perms);

            // An edit is a REQUEST. Note we do not write our own copy here: the host's relay comes
            // back through OnCrosserSetupChanged and that is what everyone (including us) adopts.
            //
            // COALESCED, because a slider drag reports an edit on every frame it moves and this rides
            // the RELIABLE channel (resent until acked) - publishing per frame would put a hundred
            // ordered packets on the wire for one drag. Mark it dirty here and let the throttle in
            // Update send at most one per PublishInterval, plus a final one when the drag ends.
            if (res.edited) _crossDirty = true;
            // res.spotMoved is deliberately NOT acted on directly - the re-plant happens in
            // OnCrosserSetupChanged once the authoritative value lands, so every peer moves him at
            // the same point in the sequence rather than the editor moving him early.

            if (res.rename != null) _s.RenameCrosserAi(res.rename);
            // A reassignment is DEFERRED to Update. This runs inside OnGUI, and on the host the
            // session applies the move synchronously - roster push, RosterChanged, bodies destroyed
            // and built, the camera re-targeted - none of which belongs inside an IMGUI event pass.
            if (res.assignCrosser != -2) { _pendingAssign = res.assignCrosser; _pendingAssignAi = res.assignAi; }
        }

        /// <summary>
        /// Send a pending cross-panel edit, at most one per CrossPublishInterval. `force` bypasses
        /// the throttle for the end of an interaction (the panel closing), so the last position of a
        /// slider is never the one that got swallowed by the timer.
        /// </summary>
        void PublishCrossEditIfDue(bool force = false)
        {
            if (!_crossDirty || _s == null) return;
            if (!force && Time.unscaledTime < _crossNextPublish) return;
            _crossDirty = false;
            _crossNextPublish = Time.unscaledTime + CrossPublishInterval;
            _s.RequestCrosserSetup(CrossMap.ToWire(CrossMap.Session, _s.CrosserAiName));
        }

        // The local player's passing accuracy, 0..1, the same reading the match's pass model makes
        // (PassAccuracyMul 1..1.85 -> 0..1), so a human cross scatters like a pass does.
        static float LocalPassAcc() => Mathf.Clamp01((PlayerProfile.PassAccuracyMul - 1f) / 0.85f);

        // Who the host could hand the crosser seat to, off the roster (rebuilt on the host by every
        // push, so it is never stale here): every human holding a seat except whoever is crossing -
        // the host included, and ALONE included, because a host who handed the seat to the AI has to
        // be able to take it back - plus, while a HUMAN crosses, every AI seat: picking a Clanker
        // swaps it into the crosser seat and the human into its seat (NetSession.AssignCrosserAi).
        // While the AI crosses the AI seats are not offered; an AI-for-AI swap changes nothing.
        // Mid-match too: the session moves the player and OnRosterChanged rebuilds bodies + camera.
        //
        // (Superseded note, kept for the history of the greyed button it caused:)
        // NONE while the host is the only human: the one name would be the host's own, and a crosser
        // with nobody in the box to cross to is not a choice worth offering. The AI is not on this
        // list (it is the dropdown's own first row), so a lone host can still hand the seat to it.
        List<(int slot, string name, bool ai)> CrosserCandidates()
        {
            var list = new List<(int, string, bool)>();
            var roster = _s.Roster;
            if (roster == null) return list;
            bool humanCrosser = _s.RosterSlot(NetSession.CrosserSlot).human;
            for (int i = 0; i < roster.Length; i++)
            {
                var r = roster[i];
                if (r.slot == NetSession.CrosserSlot) continue;
                if (r.human) list.Add((r.slot, r.name, false));
                else if (r.ai && humanCrosser) list.Add((r.slot, r.name, true));
            }
            return list;
        }

        // A clickable radial emote menu (B). Clicking a slice records the pick on the input
        // (SetEmotePick -> reaches the host via SampleFrame, which streams it to everyone) and
        // plays it immediately on the local body for instant owner feedback, then closes.
        void DrawEmoteWheel()
        {
            float cx = Hud.W * 0.5f, cy = Hud.H * 0.5f;
            Hud.Scrim(0.55f);

            int pages = Celebration.Pages.Length;
            _wheelPage = ((_wheelPage % pages) + pages) % pages;
            var page = Celebration.Pages[_wheelPage];
            int n = page.Length;
            float rad = 210f;
            var lbl = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n * i) * Mathf.Deg2Rad;
                float sx = cx + Mathf.Sin(ang) * rad;
                float sy = cy - Mathf.Cos(ang) * rad;
                float bw = 132f, bh = 42f;
                var r = new Rect(sx - bw * 0.5f, sy - bh * 0.5f, bw, bh);
                if (UITheme.Button(r, page[i].name, lbl))
                {
                    _input.SetEmotePick((int)page[i].e);   // sync to host -> everyone
                    var me = _bodies[_localSlot];
                    if (me != null && me.celeb != null) me.celeb.Play(page[i].e);   // instant local feedback
                    SetWheelOpen(false);
                    return;
                }
            }

            // Left/right arrows flanking the ring cycle the pages.
            var arrow = new GUIStyle(GUI.skin.button) { fontSize = 30, fontStyle = FontStyle.Bold };
            if (UITheme.Button(new Rect(cx - rad - 96f, cy - 26f, 52f, 52f), "‹", arrow)) _wheelPage--;
            if (UITheme.Button(new Rect(cx + rad + 44f, cy - 26f, 52f, 52f), "›", arrow)) _wheelPage++;

            // Page dots + hint at the centre.
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Hud.Dim } };
            UITheme.Label(new Rect(cx - 160f, cy - 20f, 320f, 22f), "Click an emote  ·  B to close", hint);
            Hud.PageDots(cx, cy + 16f, pages, _wheelPage);
        }

        // Replay start (any peer): freeze local control, cut to broadcast cam, roll playback.
        void OnReplayStarted()
        {
            if (_replay == null || _replaying) return;
            _replaying = true;
            _cam.SetMode(GameCamera.Mode.Broadcast);
            _replay.Play(SimConfig.ReplaySlowMul);
            Flash("REPLAY  (click to skip)");
        }

        // Replay end (host tallied all skips, or buffer finished): resume + re-arm serving.
        void OnReplayEnded()
        {
            if (_replayStale) SetupReplay();   // a body was rebuilt while this replay rolled
            if (!_replaying) return;
            _replaying = false;
            if (_replay != null) _replay.Stop();
            _cam.SetMode(GameCamera.Mode.Follow);
            if (_s.IsHost) { _crosser.Arm(0.6f); _ball.ResetTo(_launch.position); }
        }

        // -------------------------------------------------------------- loop
        void Update()
        {
            if (_s == null || PauseMenu.Frozen) return;   // Frozen: an overlay pause never stops the sim

            // Quickchat (multiplayer): Tab opens/submits the custom text box; while typing, gameplay
            // input is suspended so keystrokes don't drive the player. Number keys 1-6 send a preset.
            if (_qcFeed != null)
            {
                if (_input.QuickChatTextPressed) _qcFeed.ToggleTextEntry();
                if (_qcFeed.Typing) return;   // suspend gameplay control while typing
                int qd = _input.QuickChatDigitPressed();
                if (qd > 0) _qcFeed.SendPreset(qd);
            }

            // --- Replay: no gameplay control; click to vote-skip; host ends when its own
            //     playback finishes (a natural end for everyone) or all humans have voted. ---
            if (_replaying)
            {
                if (_input.LeftClickPressed) _s.VoteSkip();
                if (_s.IsHost && (_replay == null || !_replay.IsPlaying)) _s.EndReplayHost();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // Cross-targeting map (M). Open to everyone; what a given peer may DO with it is decided
            // per-control when it is drawn (DrawCrossOverlay). Same toggle and same Escape-also-
            // closes behaviour single-player has. While it is up the local striker doesn't tick and
            // the cursor is freed, so clicks hit the panel instead of steering - the sim keeps
            // running underneath either way, because a networked match never stops for one player.
            //
            // It does NOT close itself when a human takes the crosser seat: the panel goes read-only
            // and says who is crossing, which is more use than vanishing mid-look, and it is how the
            // host reaches the dropdown to take the seat back.
            //
            // The rename field owns the keyboard while it is up: M is a letter someone may want in
            // a name, and Escape should back out of the FIELD rather than the whole panel.
            bool renaming = CrossMap.Renaming;
            if (renaming)
            {
                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                    CrossMap.CancelRename();
            }
            else if (CrossMapAvailable && _input.CrossMapPressed) SetCrossMapOpen(!_crossOpen);
            else if (_crossOpen && (!CrossMapAvailable
                     || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)))
                SetCrossMapOpen(false);

            // R: with an AI crosser, multiplayer re-serves the shared ball to the crosser
            // (host-authoritative; no player reset). A HUMAN crosser instead refills a ball at
            // their own feet if one isn't already there (handled host-side in HostUpdate, so a
            // remote crosser's R works too). (Single-player R still fully resets via GameManager.)
            if (_input.ResetPressed && _s.IsHost && _crosser.AutoServe) { _goalHold = 0f; _crosser.Arm(0.4f); _ball.ResetTo(_launch.position); }

            // Q cycles how the AI crosser delivers - Ground / Low / High - for everyone: it is the
            // cross map's own setting, published exactly as an edit on the map is (a request the
            // host relays). Only while the AI crosses; a human crosser shapes his own.
            // (This replaced the old Q/E call-for-pass.)
            if (_input.PassLoftedPressed && !_crossOpen && !_s.RosterSlot(NetSession.CrosserSlot).human)
            {
                CrossMap.Session.delivery = CrossMap.NextDelivery(CrossMap.Session.delivery);
                Flash("CROSS: " + CrossMap.DeliveryName(CrossMap.Session.delivery));
                _crossDirty = true;
                PublishCrossEditIfDue(force: true);
            }

            // Emote wheel (B): any local body that can emote (has a Celebration). Toggling frees
            // the cursor so the radial menu is clickable.
            if (_input.EmotePressed && _bodies[_localSlot]?.celeb != null)
                SetWheelOpen(!_wheelOpen);

            // Local player: tick its own controller (host + client both predict the local body).
            // Shooters tick their Striker; a local keeper/crosser control is ticked host-side in
            // HostUpdate (they own the authoritative body); a client-local keeper/crosser has no
            // predicted control this pass (their body follows the host snapshot).
            var me = _bodies[_localSlot];
            // Local emote: start my celebration from the device pick (a property read - does NOT
            // consume the one-shot; SampleFrame later sends it over the wire so the host streams
            // it to the others). Reading before SampleFrame means it plays this frame. Works for a
            // local shooter OR a local (host) keeper - both have a celeb; only the null-check gates.
            if (me != null && me.celeb != null && !me.celeb.Playing)
            {
                int eid = _input.EmoteId;
                if (eid >= 0 && eid != 255) me.celeb.Play((Celebration.Emote)eid);
            }
            // Tick my controller unless I'm emoting (so movement doesn't fight the pose) or the
            // cross map is up (the cursor is freed for it, so a click must place a marker rather
            // than swing a leg - same suspension single-player applies).
            if (me != null && me.striker != null && !_crossOpen
                && (me.celeb == null || !me.celeb.Playing)) me.striker.Tick();
            // A CLIENT that is the crosser runs its own display-only stance (the host ticks the real
            // one in HostUpdate, for the local host crosser and every remote one alike).
            if (!_s.IsHost && me != null && me.crosserCtl != null && !_crossOpen) me.crosserCtl.Tick();

            // Flush a coalesced cross-panel edit (see PublishCrossEditIfDue). In Update, not OnGUI,
            // so it runs once a frame rather than once per IMGUI event pass.
            PublishCrossEditIfDue();

            // A crosser reassignment picked on the panel (host only; the session refuses the rest).
            if (_pendingAssign != -2)
            {
                int pick = _pendingAssign; bool ai = _pendingAssignAi;
                _pendingAssign = -2; _pendingAssignAi = false;
                if (pick == -1) _s.ClearCrosser();
                else if (ai) _s.AssignCrosserAi(pick);
                else _s.AssignCrosser(pick);
            }

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void HostUpdate()
        {
            // Post-goal hold: freeze GAMEPLAY (no crosser serve, no controllers, no re-detect)
            // but keep physics + the recorder running and keep publishing snapshots, so the
            // ball settles in the net on-screen and the replay window captures AFTER the line.
            // Mirrors the single-player hold (GameManager returns early during _replayHold). The
            // crosser must NOT tick here or its auto-serve could yank the shared ball out of the
            // net mid-hold and corrupt the replay.
            if (_goalHold > 0f)
            {
                _goalHold -= Time.deltaTime;
                if (_goalHold <= 0f) _s.BeginReplay();
                PublishSnapshotIfDue();
                return;
            }

            // Feed remote slots' latest input, tick their controllers + the AI keeper.
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null) continue;
                bool remote = i != _localSlot;
                // Remote human slots: refresh their input adapter from the wire first.
                if (remote && b.netInput != null) b.netInput.Feed(_s.ConsumeInputForSlot(i));
                // Emote: start a REMOTE body's celebration from its wire input (the local body's
                // emote is started in Update from the device, before SampleFrame consumes it).
                // Runs on the real ragdoll so the pose + phase can be streamed to every client.
                if (remote && b.celeb != null && b.netInput != null && !b.celeb.Playing)
                {
                    int eid = b.netInput.EmoteId;
                    if (eid >= 0 && eid != 255) b.celeb.Play((Celebration.Emote)eid);
                }
                // Tick whichever controller this body has (shooter / human keeper / human
                // crosser). The local body's Striker is already ticked in Update(); its human
                // keeper/crosser controls are ticked here (they run host-side only).
                // While emoting, suspend the striker so movement doesn't fight the pose. Also block
                // the LOCAL keeper while ITS emote wheel is open: the wheel frees the cursor and a
                // click to pick an emote slice would otherwise be read by KeeperController as an
                // LMB/RMB reflex save (a phantom dive/lunge). Remote keepers ignore our wheel.
                bool emoting = b.celeb != null && b.celeb.Playing;
                bool localWheel = !remote && _wheelOpen;
                if (remote && b.striker != null && !emoting) b.striker.Tick();
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null && !emoting && !localWheel) b.keeper.Tick();
                // (The host's OWN crosser is not ticked while its cross map is up: the cursor is
                // freed for the map, so a click there must not start a charge underneath it - the
                // same suspension its Striker gets in Update.)
                // (R on a human crosser is handled INSIDE CrosserControl now - it sets the stance up
                // exactly as Enter does, ball to the feet included - so the old R-refill is gone.)
                if (b.crosserCtl != null && !(!remote && _crossOpen))
                    b.crosserCtl.Tick();
            }

            // Crosser + ball + goal detection (authoritative). A goal starts the LIVE hold above.
            _crosser.Tick();
            Vector3 c = _ball.transform.position;
            if (!_replaying && BallInGoal(c))
            {
                _goals++; _s.BroadcastEvent("GOAL!"); _goalHold = SimConfig.ReplayHold;
                _shotLive = false; _save.Disarm();   // consumed as a goal, not a miss/save
                // Host's own goal: BroadcastEvent only fires MatchEvent on CLIENTS, so stand the
                // host's local striker up here too (a trick finish otherwise stays limp through
                // the hold + replay). Flash the callout locally to match the clients' HUD.
                // Cheer here too: BroadcastEvent only fires OnMatchEvent on CLIENTS, so the host
                // plays its own goal celebration locally (clients get it via OnMatchEvent above).
                Flash("GOAL!");
                RecoverLocalStriker();
                AudioManager.Instance?.PlayGoalCelebration();
            }
            else if (!_replaying)
            {
                HostTrackMiss(c);
            }

            PublishSnapshotIfDue();
        }
        float _snapAccum;

        // Publish a snapshot every fixed-ish step (throttled). Called from the normal host tick
        // AND during the post-goal hold, so clients keep seeing the ball settle in the net.
        void PublishSnapshotIfDue()
        {
            _snapAccum += Time.deltaTime;
            if (_snapAccum >= SimConfig.NetSnapshotInterval)
            {
                _snapAccum = 0f;
                // A local keeper sends its cone yaw (KeeperLookYaw); everyone else sends camera yaw.
                float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
                _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw, _cam.Pitch));   // (host records its own input too)
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            // Send my input to the host each frame. A local keeper sends its cone yaw (KeeperLookYaw).
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw, _cam.Pitch));

            // Mirror the authoritative score so the client HUD shows the real goal count (goal
            // detection is host-only; a client's local _goals never increments on its own).
            if (_s.HasSnapshot) _goals = _s.LatestSnapshot.homeScore;

            // Reconcile our own PREDICTED body against the host's authoritative state (the local
            // body is simulated immediately from input; here we correct drift/mispredictions).
            ReconcileLocalBody();

            // Render remote bodies + ball at (now - InterpDelay), interpolating between the two
            // buffered snapshots bracketing that render time. This is smooth regardless of when
            // packets actually arrive (no teleport on a late/dropped snapshot).
            if (!_s.SampleInterpolated(SimConfig.NetInterpDelay, out var a, out var b, out float f))
                return;

            for (int i = 0; i < _bodies.Length; i++)
            {
                var body = _bodies[i];
                if (body == null || i == _localSlot) continue;   // our own body is predicted, not puppeted
                if (!FindBody(a, i, out var sa)) continue;       // no state for this slot yet
                if (!FindBody(b, i, out var sb)) sb = sa;         // absent in the newer snap: hold the older

                Vector3 pos = Vector3.Lerp(sa.pos, sb.pos, f);
                float yaw = Mathf.LerpAngle(sa.yaw, sb.yaw, f);
                var facing = Quaternion.Euler(0f, yaw, 0f);
                // Emote id/phase from the newest of the two samples (an emote is a discrete event,
                // not a value to blend); phase advances with f for a smooth dance. An active emote
                // overrides the locomotion anim state.
                // Adult mode: the puppet's appendage follows the host's flag (AnatomySim eases it).
                if (body.ragdoll.Anatomy != null) body.ragdoll.Anatomy.Erect = sb.erect;
                byte emoteId = sb.emoteId != 255 ? sb.emoteId : sa.emoteId;
                if (emoteId != 255)
                {
                    float ephase = Mathf.Lerp(sa.emotePhase / 255f, sb.emotePhase / 255f, f);
                    body.ragdoll.DisplayEmote(pos, facing, emoteId, ephase);
                }
                else
                {
                    // Measured horizontal speed from the interpolated motion drives the run cadence
                    // + amount, so a body only "runs" as fast as it is actually moving.
                    float speed = 0f;
                    if (body.hasLastInterp)
                    {
                        Vector3 d = pos - body.lastInterpPos; d.y = 0f;
                        speed = d.magnitude / Mathf.Max(1e-4f, Time.deltaTime);
                    }
                    body.lastInterpPos = pos; body.hasLastInterp = true;
                    float moveAmount = Mathf.Clamp01(speed / SimConfig.StrikerMoveSpeed);
                    body.animPhase += Time.deltaTime * SimConfig.StrideRateMax * moveAmount / (2f * Mathf.PI);
                    body.ragdoll.DisplayAnim(pos, facing, (AnimState)(sb.anim), body.animPhase, moveAmount);
                }
            }

            // Ball: interpolate between the two snapshots too (host owns physics; client is display).
            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(a.ballPos, b.ballPos, f);
        }

        // Server reconciliation for the local predicted body. The active ragdoll is not
        // deterministically re-simulatable, so instead of rollback+replay we do bounded error
        // correction: compare the predicted feet position to the host's authoritative position
        // (freshest snapshot), leave small expected prediction lag alone, softly ease back a
        // moderate divergence, and hard-snap a large one (a real misprediction, e.g. an unpredicted
        // collision or knockback on the host). Only x/z (grounded movement); vertical is left to
        // local physics. Skipped while emoting (pose-driven) or airborne (trick/jump).
        void ReconcileLocalBody()
        {
            var me = _bodies[_localSlot];
            if (me == null || me.ragdoll == null || me.ragdoll.Pelvis == null) return;
            if (me.celeb != null && me.celeb.Playing) return;
            if (me.striker != null && (me.striker.IsBusy || !me.ragdoll.IsGrounded)) return;
            // Not through a crossing stance. Entering it steps the body back onto its run-up, which
            // is past ReconcileSnap; the client does that on its own Enter and the host a few ticks
            // later on the wire, so for those ticks the host's snapshot still says "where you were
            // standing" and this would snap the body back there, then forward again. The taker runs
            // the same run-up on both peers from the same input, so the divergence is small and is
            // corrected the moment the stance ends and this resumes.
            if (me.crosserCtl != null && me.crosserCtl.InStance) return;
            if (!_s.HasSnapshot) return;
            if (!FindBody(_s.LatestSnapshot, _localSlot, out var auth)) return;

            Vector3 pred = me.ragdoll.Pelvis.position; pred.y = 0f;
            Vector3 target = auth.pos; target.y = 0f;
            Vector3 err = target - pred;
            float d = err.magnitude;
            if (d < SimConfig.ReconcileDeadzone) return;                 // within expected lag: ignore
            if (d > SimConfig.ReconcileSnap) { me.ragdoll.ShiftAll(err); return; }   // big miss: snap
            // Moderate: ease a fraction of the error this frame (smooth pull-back).
            me.ragdoll.ShiftAll(err * Mathf.Clamp01(SimConfig.ReconcileRate * Time.deltaTime));
        }

        // Find a slot's BodyState in a snapshot. Returns false if absent. When found in `a` but not
        // `b` (or vice versa), callers pass the same snapshot twice so the lerp is a no-op hold.
        static bool FindBody(in Snapshot s, int slot, out BodyState bs)
        {
            if (s.bodies != null)
                for (int i = 0; i < s.bodies.Length; i++)
                    if (s.bodies[i].slot == slot) { bs = s.bodies[i]; return true; }
            bs = default; return false;
        }

        // Host: derive a body's animation state for the snapshot so clients play the matching
        // canned anim on their puppet. Emotes are handled separately (emoteId), so this covers
        // locomotion/action states. Priority: keeper dive > airborne > moving > idle.
        static AnimState AnimStateOf(Body b)
        {
            if (b.ragdoll == null) return AnimState.Idle;
            if (b.keeper != null && b.keeper.IsCommitting) return AnimState.Dive;
            if (b.ai != null && b.ai.WasDivingSave) return AnimState.Dive;
            // A human crosser mid-kick: the swing on the leg he actually kicks with, so every other
            // screen shows a left-footer swinging his left leg. The run-in before it is a Run.
            if (b.crosserCtl != null && b.crosserCtl.Swinging)
                return b.crosserCtl.LeftFooted ? AnimState.KickL : AnimState.Kick;
            if (b.striker != null)
            {
                if (b.striker.IsDiving || b.striker.IsTumbling) return AnimState.Down;   // prone: diving header, or down off a flip
                if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            }
            else if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            // Moving on the deck -> run. MoveInput is the controller's desired velocity.
            if (b.striker != null && b.striker.IsSitting) return AnimState.Sit;
            if (b.ragdoll.MoveInput.sqrMagnitude > 0.6f) return AnimState.Run;
            return AnimState.Idle;
        }

        void BroadcastSnapshot()
        {
            var list = new List<BodyState>();
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || b.ragdoll.Pelvis == null) continue;
                Vector3 p = b.ragdoll.Pelvis.position; p.y = 0f;
                // Stream the emote this body is playing (id + quantized phase) so clients can
                // replay the dance on their puppet. 255 = not emoting.
                byte eid = 255, eph = 0;
                if (b.celeb != null && b.celeb.Playing)
                {
                    eid = (byte)b.celeb.CurrentEmote;
                    eph = (byte)Mathf.Clamp(Mathf.RoundToInt(b.celeb.Progress01 * 255f), 0, 255);
                }
                list.Add(new BodyState { slot = (byte)i, pos = p, yaw = b.ragdoll.FacingRotation.eulerAngles.y,
                                         down = false, emoteId = eid, emotePhase = eph, anim = (byte)AnimStateOf(b),
                                         lastInputTick = _s.InputTickForSlot(i),
                                         erect = b.ragdoll.Anatomy != null && b.ragdoll.Anatomy.Erect });
            }
            var snap = new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
                // Populated here too even though only the match driver draws a landing telegraph
                // today. It is a field on the shared Snapshot, and a wire field whose meaning depends on
                // which driver sent it is a trap: these are the SHOT modes, so leaving it false would
                // have made "not guided" mean "no assist" in one driver and "nobody filled this in" in
                // the others, on nearly every ball.
                guided = _ball.Guided,
                homeScore = (byte)Mathf.Min(255, _goals), awayScore = 0, bodies = list.ToArray(),
            };
            _s.BroadcastSnapshot(snap);
        }

        bool BallInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            return c.z - r >= _goalLineZ && c.z <= _goalLineZ + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
        }

        // --- Miss detection (host-authoritative), so the crowd can boo an occasional wasted shot.
        // MP Striker is continuous play, so there's no discrete attempt like the set pieces. We
        // define a SHOT conservatively: the ball is moving goalward at real pace. A shot MISSES if,
        // while live, it passes the goal line (wide/high/over the frame) or leaves the field, and
        // it did NOT score (goal is handled above and clears _shotLive). This never fires on a
        // dribble, a pass, or a slow loose ball - only on a struck ball that reaches the goal
        // region and fails. One boo per shot; re-arms only after the ball is calm again.
        bool _shotLive;
        readonly SaveWatch _save = new SaveWatch();   // shared SAVE / EPIC SAVE verdict
        void HostTrackMiss(Vector3 c)
        {
            float halfW = SimConfig.GoalWidth * 0.5f;
            bool goalward = _ball.Rb.linearVelocity.z > SimConfig.MissShotSpeed;

            // Arm a shot: fast ball headed at goal, still in front of the line.
            if (!_shotLive && goalward && c.z < _goalLineZ) { _shotLive = true; _save.Arm(); return; }
            if (!_shotLive) return;

            // Keeper contact, from the ball's touch log (real contacts, impact speed taken at the
            // contact). A stop that stays in play never reaches the line tests below, so resolve it
            // as soon as the touched ball settles - otherwise a catch produced no callout at all.
            _save.Poll(_ball, KeeperRagdoll(), KeeperHighDive());
            if (_save.SettledAfterTouch(_ball)) { _shotLive = false; Announce(_save.Callout()); return; }

            // Resolve the live shot the moment it reaches/leaves the goal line plane without
            // being in the frame (BallInGoal already handled the score + cleared _shotLive), or
            // it flies out of the field entirely.
            bool pastLine  = c.z - SimConfig.BallRadius >= _goalLineZ;                 // crossed the line, not a goal
            bool wideOrOut = Mathf.Abs(c.x) > halfW || c.y > SimConfig.GoalHeight;     // outside the frame
            bool offField  = c.y < -3f || Mathf.Abs(c.x) > SimConfig.FieldWidth || Mathf.Abs(c.z) > SimConfig.FieldLength;

            if ((pastLine && wideOrOut) || offField)
            {
                _shotLive = false;
                // Touched by the keeper on the way = a save, wherever it ended up (parried wide is
                // still a save). Only an untouched failed shot is a miss.
                if (_save.Touched) { Announce(_save.Callout()); return; }
                _s.BroadcastEvent("MISS");                     // clients boo via OnMatchEvent
                AudioManager.Instance?.PlayMissBoosMaybe();    // host plays locally (BroadcastEvent skips host)
            }
        }

        // Slot 0 is the keeper: its ragdoll, and whether it is mid big-reach (human or AI Clanker).
        ActiveRagdoll KeeperRagdoll() => _bodies[0] != null ? _bodies[0].ragdoll : null;
        bool KeeperHighDive()
        {
            var kb = _bodies[0];
            if (kb == null) return false;
            if (kb.keeper != null) return kb.keeper.IsHighDive;
            return kb.ai != null && kb.ai.WasDivingSave;
        }

        // Broadcast a callout AND flash it locally: BroadcastEvent only fires MatchEvent on clients.
        void Announce(string tag) { _s.BroadcastEvent(tag); Flash(tag); }

        static void LockCursor() => GameInput.CaptureCursor(true);

        void OnDestroy()
        {
            if (_s != null) { _s.MatchEvent -= OnMatchEvent; _s.BallKicked -= OnBallKicked; _s.PostHit -= OnPostHit; _s.ReplayStarted -= OnReplayStarted; _s.ReplayEnded -= OnReplayEnded; _s.JerseyUpdated -= OnJerseyUpdated; _s.RosterChanged -= OnRosterChanged; _s.CrosserSetupChanged -= OnCrosserSetupChanged; }
            CrossMap.CancelTransientUI();   // never come back onto a half-typed rename
            if (_ball != null && _ball.Rb != null) _ball.Rb.isKinematic = false;
        }

        void OnGUI()
        {
            if (_s == null) return;
            Hud.Begin();
            var meBody = _bodies[_localSlot];
            string youAre = meBody == null ? "Shooter " + _localSlot
                          : meBody.isKeeper ? "Keeper"
                          : meBody.isCrosser ? "Crosser" : "Shooter " + _localSlot;
            var p = Hud.PanelStart(_s.IsHost ? "STRIKER (HOST)" : "STRIKER (CLIENT)", 2);
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "You are", youAre);
            Hud.Legend(_localIsCrosser
                ? (meBody?.crosserCtl != null && meBody.crosserCtl.InStance
                    ? "Mouse aim   HOLD LMB/RMB power, release to cross   A/D curl   W drive (full = along the ground)   S float"
                    : "WASD move   LMB/RMB legs   ENTER set up a cross   R new ball   V ball cam")
                : (youAre == "Keeper"
                    ? "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   E/Q throw   V ball cam"
                    : "WASD move   Mouse aim   LMB/RMB legs   Space jump   Q cross type   V ball cam   R reset"
                      + (CrossMapAvailable ? "   M cross map" : "")
                      + Keybinds.ThirdLegHint(PlayerProfile.Appearance.Adult)));
            Hud.Flash(_flash, _flashTime / 1.6f);

            // Emote wheel overlay (B). Gated on Paused as well as _wheelOpen: only Update is
            // pause-gated, so an already-open wheel kept drawing REAL buttons under the pause menu
            // and they stayed clickable through it (IMGUI has no occlusion; the pause scrim is a
            // plain DrawTexture and eats no events).
            if (_wheelOpen && !PauseMenu.Paused) DrawEmoteWheel();

            // Cross-targeting overlay. Same panel as single-player; only the permissions differ.
            if (_crossOpen && !PauseMenu.Paused) DrawCrossOverlay();

            // Quickchat feed + custom-text box (multiplayer).
            if (_qcFeed != null) _qcFeed.Draw();

            // Human crosser's power meter - the free kick's own widget, because it IS the free
            // kick's meter (CrosserControl runs a SetPieceTaker). Drawn on the host's local crosser
            // and on a client crosser alike: the client runs the stance display-only, so its meter
            // is real even though its ball is not.
            if (_localIsCrosser)
            {
                var meCtl = _bodies[_localSlot]?.crosserCtl;
                if (meCtl != null && meCtl.IsCharging) Hud.Meter(meCtl.Meter, "POWER  (release to cross)");
            }

            Hud.End();
        }
    }
}
