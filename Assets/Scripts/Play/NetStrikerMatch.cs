using System.Collections.Generic;
using UnityEngine;
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
        AimReticle _reticle;
        Transform _launch;
        NetSession _s;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;

        uint _tick;
        int _goals;
        string _flash = ""; float _flashTime;
        float _goalLineZ;


        // Cross-targeting map (crosser role only): where the human crosser's deliveries land.
        bool _crossMapOpen;
        Vector3 _crossTarget = SimConfig.ServeTarget;
        bool _localIsCrosser;
        bool _localIsKeeper;

        // Emote wheel (B): pick a celebration; it plays on the local body and syncs to everyone.
        bool _wheelOpen;
        int _wheelPage;   // which of Celebration.Pages is showing (arrows cycle it)

        // Per-machine post-goal replay (each peer replays its own local view).
        ReplaySystem _replay;
        bool _replaying;
        // Host-only: after a goal, keep playing live for ReplayHold seconds (the recorder keeps
        // buffering the ball settling in the net) before freezing + rolling the replay, so most
        // of the replay is AFTER the ball crosses the line. >0 = counting down to BeginReplay.
        float _goalHold;

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball, Crosser crosser,
                              AimReticle reticle, Transform launch,
                              Material torso, Material limb, Material glove, Transform root)
        {
            _input = input; _cam = gameCam; _ball = ball; _crosser = crosser; _reticle = reticle; _launch = launch;
            _s = Multiplayer.Session;
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _goalLineZ = SimConfig.GoalCenter.z;
            _s.MatchEvent += OnMatchEvent;

            // Spawn a body per active slot from the roster (keeper slot 0, crosser slot N-1,
            // shooters between).
            foreach (var slot in _s.Roster)
                SpawnBody(slot.slot, torso, limb, glove, root);

            // Camera follows the LOCAL body; local striker turns to camera yaw.
            var me = _bodies[_localSlot];
            _localIsCrosser = me != null && me.isCrosser;
            _localIsKeeper = me != null && me.isKeeper;
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                if (_localIsKeeper)
                {
                    // Human keeper: identical to single-player goalkeeper mode. The camera pans
                    // in a cone from a FIXED forward base; the keeper reads that same cone yaw
                    // (KeeperLookYaw) and turns his body to it, so body + camera stay in lock-step.
                    _cam.SetKeeperFollow(me.ragdoll.Pelvis.transform,
                                         () => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up),
                                         () => _input.Look);
                }
                else
                {
                    _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look);
                    if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw);
                }
            }

            if (_s.IsHost) { _crosser.Arm(SimConfig.ServeFirstDelay); _ball.ResetTo(_launch.position); }

            // Per-machine replay over this peer's local bodies + ball. Each machine plays
            // back what IT recorded (host = true physics, clients = their interpolated view).
            var tracked = new List<Transform> { _ball.transform };
            for (int i = 0; i < _bodies.Length; i++)
                if (_bodies[i] != null) tracked.AddRange(_bodies[i].ragdoll.BoneTransforms);
            _replay = gameObject.AddComponent<ReplaySystem>();
            _replay.Setup(tracked, null, SimConfig.ReplayWindow);
            _s.ReplayStarted += OnReplayStarted;
            _s.ReplayEnded += OnReplayEnded;
            _s.JerseyUpdated += OnJerseyUpdated;
            _s.RosterChanged += OnRosterChanged;

            LockCursor();
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
                if (hostSim)
                {
                    if (isLocal) striker.Init(_input, ragdoll);          // host's own device
                    else { b.netInput = new NetInputSource(); striker.Init(b.netInput, ragdoll); }
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
                if (isLocal) { kc.Init(_input, ragdoll); }
                else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll); }
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

            if (!hostSim)
            {
                if (isLocal) { /* client-predicted local crosser: keep real ragdoll */ }
                else if (ragdoll != null) ragdoll.BecomeDisplayBody();   // remote crosser puppet
            }
            else if (human)
            {
                _crosser.AutoServe = false;                              // human decides deliveries
                _crosser.Cosmetic = false;                               // a Striker owns pose + movement
                _crosser.ServeFromFeet = true;                           // launch from where they stand
                // The crosser was planted at Init (locomotion off); un-plant it so the Striker
                // can move it like a shooter.
                ragdoll.LocomotionEnabled = true;
                // Move freely like a shooter: drive the crosser ragdoll with a Striker.
                var striker = ragdoll.gameObject.AddComponent<Striker>();
                b.striker = striker;
                IStrikerInput src = isLocal ? (IStrikerInput)_input : (b.netInput = new NetInputSource());
                striker.Init(src, ragdoll);
                var cc = _crosser.gameObject.AddComponent<CrosserControl>();
                cc.Init(src, _crosser, () => _crossMapOpen, () => _crossTarget);
                b.crosserCtl = cc;
            }
            else
            {
                // AI auto-serve loop (planted). Fully restore the planted state in case a human
                // previously held this slot and left it mobile (Striker-driven, locomotion on):
                // re-plant the ragdoll, drop any Striker, and re-arm the serve loop so it feeds
                // balls consistently instead of standing idle.
                var stray = ragdoll != null ? ragdoll.GetComponent<Striker>() : null;
                if (stray != null) Destroy(stray);
                var strayCc = _crosser.GetComponent<CrosserControl>();
                if (strayCc != null) Destroy(strayCc);
                _crosser.Cosmetic = true;
                _crosser.ServeFromFeet = false;
                _crosser.AutoServe = true;
                if (ragdoll != null)
                {
                    ragdoll.UprightLock = true;
                    ragdoll.LocomotionEnabled = false;
                    ragdoll.MoveInput = Vector3.zero;
                    Vector3 toGoal = SimConfig.GoalCenter - SimConfig.CrosserStart; toGoal.y = 0f;
                    ragdoll.ResetTo(SimConfig.CrosserStart,
                                    Quaternion.LookRotation(toGoal.normalized, Vector3.up));
                }
                _crosser.Arm(SimConfig.ServeFirstDelay);                  // start the serve countdown now
            }

            _bodies[slot] = b;
        }

        // A player left mid-match: the roster row for their slot is no longer human. Despawn that
        // body so it doesn't freeze as a statue for everyone. A keeper slot swaps to an AI keeper
        // (play must continue with someone in goal); shooter/crosser bodies just disappear. Runs on
        // host + client (both hold a body per slot); the host also stops driving/broadcasting it.
        void OnRosterChanged()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.wasHuman) continue;      // only human-spawned bodies react to a leave
                if (_s.RosterSlot(i).human) continue;         // still human: nothing changed

                // This human left mid-match.
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
                    if (_crosser != null) { _crosser.AutoServe = true; }
                    continue;
                }
                // Shooter: remove the body (no shooter AI in striker mode).
                if (b.ragdoll != null) Destroy(b.ragdoll.gameObject);
                _bodies[i] = null;
            }
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
            // Both legs (bicycle off either foot must classify) - matches GameBootstrap.
            AddDet(ragdoll.Rb(Bone.FootR), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.CalfR), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.FootL), striker, ragdoll);
            AddDet(ragdoll.Rb(Bone.CalfL), striker, ragdoll);
        }
        void AddDet(Rigidbody rb, Striker striker, ActiveRagdoll ragdoll)
        {
            if (rb == null) return;
            rb.gameObject.AddComponent<KickDetector>().Init(striker, ragdoll, _ball);
        }

        void OnMatchEvent(string tag)
        {
            Flash(tag);
            // On a goal, stand the LOCAL striker back up. A trick finish (diving header / bicycle)
            // leaves him prone + limp, and his Tick() is suspended through the goal hold + replay,
            // so without this he'd stay slumped on the deck for the whole celebration. Only the
            // local body has a live Striker to recover (remote bodies are host-driven / puppeted).
            if (tag == "GOAL!") RecoverLocalStriker();
        }
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Pop the local striker upright out of any trick/limp pose. Safe to call on host or client
        // (the local shooter always owns a real Striker); no-op for a local keeper/crosser body.
        void RecoverLocalStriker()
        {
            var me = _bodies[_localSlot];
            if (me != null && me.striker != null) me.striker.ForceRecover();
        }

        // Crosser's cross-targeting map: free the cursor while open (to click the map), re-lock
        // on close. The chosen _crossTarget feeds CrosserControl (via the closure in SpawnBody).
        void SetCrossMapOpen(bool open)
        {
            _crossMapOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        // Emote wheel open/close: free the cursor so the radial menu is clickable, re-lock on close.
        void SetWheelOpen(bool open)
        {
            _wheelOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        // A clickable radial emote menu (B). Clicking a slice records the pick on the input
        // (SetEmotePick -> reaches the host via SampleFrame, which streams it to everyone) and
        // plays it immediately on the local body for instant owner feedback, then closes.
        void DrawEmoteWheel()
        {
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

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
                if (GUI.Button(r, page[i].name, lbl))
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
            if (GUI.Button(new Rect(cx - rad - 96f, cy - 26f, 52f, 52f), "‹", arrow)) _wheelPage--;
            if (GUI.Button(new Rect(cx + rad + 44f, cy - 26f, 52f, 52f), "›", arrow)) _wheelPage++;

            // Page dots + hint at the centre.
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(cx - 160f, cy - 20f, 320f, 22f), "Click an emote  ·  B to close", hint);
            float dotW = 16f, gap = 8f, totalW = pages * dotW + (pages - 1) * gap;
            for (int d = 0; d < pages; d++)
            {
                var dr = new Rect(cx - totalW * 0.5f + d * (dotW + gap), cy + 8f, dotW, dotW);
                var pc = GUI.color; GUI.color = d == _wheelPage ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(dr, Texture2D.whiteTexture); GUI.color = pc;
            }
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
            if (!_replaying) return;
            _replaying = false;
            if (_replay != null) _replay.Stop();
            _cam.SetMode(GameCamera.Mode.Follow);
            if (_s.IsHost) { _crosser.Arm(0.6f); _ball.ResetTo(_launch.position); }
        }

        // -------------------------------------------------------------- loop
        void Update()
        {
            if (_s == null || PauseMenu.Paused) return;

            // --- Replay: no gameplay control; click to vote-skip; host ends when its own
            //     playback finishes (a natural end for everyone) or all humans have voted. ---
            if (_replaying)
            {
                if (_input.LeftClickPressed) _s.VoteSkip();
                if (_s.IsHost && (_replay == null || !_replay.IsPlaying)) _s.EndReplayHost();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // R: with an AI crosser, multiplayer re-serves the shared ball to the crosser
            // (host-authoritative; no player reset). A HUMAN crosser instead refills a ball at
            // their own feet if one isn't already there (handled host-side in HostUpdate, so a
            // remote crosser's R works too). (Single-player R still fully resets via GameManager.)
            if (_input.ResetPressed && _s.IsHost && _crosser.AutoServe) { _goalHold = 0f; _crosser.Arm(0.4f); _ball.ResetTo(_launch.position); }

            // Local crosser: M toggles the cross-targeting map (freeze aim, free the cursor).
            if (_localIsCrosser && _input.CrossMapPressed) SetCrossMapOpen(!_crossMapOpen);

            // Emote wheel (B): any local body that can emote (has a Celebration). Toggling frees
            // the cursor so the radial menu is clickable. Not while the cross map is up.
            if (_input.EmotePressed && !_crossMapOpen && _bodies[_localSlot]?.celeb != null)
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
            // Tick my controller unless I'm emoting (so movement doesn't fight the pose).
            if (me != null && me.striker != null && (me.celeb == null || !me.celeb.Playing)) me.striker.Tick();

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // Host: a shooter pressed Q/E without the ball -> AI crosser serves a low/high ball to
        // that shooter's feet, scattered by that player's passing accuracy. First caller this
        // frame wins (the crosser serves one ball). Remote slots' pass edges come from their
        // NetInputSource (fed just above in HostUpdate); the local host reads its own device.
        void HostCheckCallForPass()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || b.isKeeper || b.isCrosser || b.ragdoll == null || b.ragdoll.Pelvis == null) continue;
                IStrikerInput src = (i == _localSlot) ? (IStrikerInput)_input : b.netInput;
                if (src == null) continue;
                bool low = src.PassGroundPressed, high = src.PassLoftedPressed;
                if (!low && !high) continue;
                Vector3 target = b.ragdoll.Pelvis.position; target.y = SimConfig.BallRadius;
                float acc = Mathf.Clamp01((PlayerProfile.PassAccuracyMul - 1f) / 0.85f);
                if (PlayerProfile.PerkMaestro) acc = 1f;
                float scatter = SimConfig.PassScatterMaxDeg * (1f - acc);
                _crosser.ServeNow(target, high, 0.5f, scatter);
                Flash(high ? "CALL: HIGH" : "CALL: LOW");
                return;   // one serve per frame
            }
        }

        // Host: a human crosser pressed R. Drop a fresh ball at their feet, but ONLY if the current
        // ball has been served away (it isn't already resting on/near them) - so tapping R when a
        // ball is already spawned does nothing. Host-authoritative: the client's R arrives via the
        // wire (its NetInputSource), and the resulting ball position streams back in the snapshot.
        void HostRefillCrosserBall(Body b)
        {
            if (b == null || b.ragdoll == null || b.ragdoll.Pelvis == null) return;
            Vector3 feet = b.ragdoll.Pelvis.position; feet.y = SimConfig.BallRadius;
            Vector3 ballFlat = _ball.transform.position; ballFlat.y = feet.y;
            if (Vector3.Distance(ballFlat, feet) < SimConfig.CrosserRefillDist) return;   // one already there
            _ball.ResetTo(feet);
            Flash("NEW BALL");
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
                if (remote && b.netInput != null) b.netInput.Feed(_s.InputForSlot(i));
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
                // While emoting, suspend the striker so movement doesn't fight the pose.
                bool emoting = b.celeb != null && b.celeb.Playing;
                if (remote && b.striker != null && !emoting) b.striker.Tick();
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null && !emoting) b.keeper.Tick();   // suspend keeper control while emoting
                if (b.crosserCtl != null)
                {
                    b.crosserCtl.Tick();
                    // Human crosser presses R to drop a fresh ball at their feet, but only if the
                    // current ball has been served away (isn't already sitting on them). Uses this
                    // body's own input (local device or remote wire), so any human crosser can refill.
                    IStrikerInput csrc = (i == _localSlot) ? (IStrikerInput)_input : b.netInput;
                    if (csrc != null && csrc.ResetPressed) HostRefillCrosserBall(b);
                }
            }

            // Call-for-pass: when the crosser is AI (no human in the crosser slot), any human
            // shooter pressing Q/E asks for a low/high ball to their feet. Host-authoritative.
            if (_crosser.AutoServe && _crosser.ReadyToServe) HostCheckCallForPass();

            // Crosser + ball + goal detection (authoritative). A goal starts the LIVE hold above.
            if (_crosser.Tick()) Flash("CROSS!");
            Vector3 c = _ball.transform.position;
            if (!_replaying && BallInGoal(c))
            {
                _goals++; _s.BroadcastEvent("GOAL!"); _goalHold = SimConfig.ReplayHold;
                // Host's own goal: BroadcastEvent only fires MatchEvent on CLIENTS, so stand the
                // host's local striker up here too (a trick finish otherwise stays limp through
                // the hold + replay). Flash the callout locally to match the clients' HUD.
                Flash("GOAL!");
                RecoverLocalStriker();
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
                _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw));   // (host records its own input too)
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            // Send my input to the host each frame. A local keeper sends its cone yaw (KeeperLookYaw).
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw));

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
            if (b.striker != null)
            {
                if (b.striker.IsDiving) return AnimState.Down;   // diving header -> prone layout
                if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            }
            else if (!b.ragdoll.IsGrounded) return AnimState.Jump;
            // Moving on the deck -> run. MoveInput is the controller's desired velocity.
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
                                         lastInputTick = _s.InputTickForSlot(i) });
            }
            var snap = new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
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

        static void LockCursor() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        void OnDestroy()
        {
            if (_s != null) { _s.MatchEvent -= OnMatchEvent; _s.ReplayStarted -= OnReplayStarted; _s.ReplayEnded -= OnReplayEnded; _s.JerseyUpdated -= OnJerseyUpdated; _s.RosterChanged -= OnRosterChanged; }
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
                ? "WASD move   M aim map   Tap Q/E driven   Hold Q/E chip   R new ball   V ball cam"
                : (youAre == "Keeper"
                    ? "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   V ball cam"
                    : "WASD move   Mouse aim   LMB/RMB legs   Space jump   Q/E call low/high   V ball cam   R reset"));
            Hud.Flash(_flash, _flashTime / 1.6f);

            // Emote wheel overlay (B).
            if (_wheelOpen) DrawEmoteWheel();

            // Crosser's cross-targeting overlay (aim where deliveries land).
            if (_localIsCrosser && _crossMapOpen)
            {
                var prev = GUI.color; GUI.color = new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
                float w = 380f, h = 300f;
                var mapRect = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);
                var hdr = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(mapRect.x, mapRect.y - 34f, w, 28f), "WHERE SHOULD YOUR CROSSES LAND?", hdr);
                CrossMap.Draw(mapRect, ref _crossTarget, interactive: true);
                var tip = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.85f, 0.85f, 0.9f) } };
                GUI.Label(new Rect(mapRect.x, mapRect.yMax + 6f, w, 22f), "Click to set target.  M to close.  Then tap Q/E = driven, hold = chip.", tip);
            }
        }
    }
}
