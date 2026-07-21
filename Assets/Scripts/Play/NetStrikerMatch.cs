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
            public bool isKeeper;
            public bool isCrosser;
            // client interp targets
            public Vector3 targetPos;
            public float targetYaw;
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

        // Client interp target for the ball.
        Vector3 _ballTargetPos;

        // Cross-targeting map (crosser role only): where the human crosser's deliveries land.
        bool _crossMapOpen;
        Vector3 _crossTarget = SimConfig.ServeTarget;
        bool _localIsCrosser;

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
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look);
                if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw);
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

            // An empty (open) slot spawns nothing: no AI keeper, no auto-crosser, no inert
            // shooter. The local slot is always the human themselves, so it's never skipped.
            if (!occupied && !isLocal)
            {
                // Crosser slot left open: silence the auto-serve loop so no AI ball-feeder runs.
                // Call-for-pass is gated on _crosser.AutoServe, so no crosses come unless a human
                // takes the crosser role or the host toggles the crosser AI on.
                if (crosser && _crosser != null) { _crosser.AutoServe = false; _crosser.Idle(); }
                return;
            }

            if (crosser) { SpawnCrosserBody(slot, isLocal, hostSim, human); return; }

            var go = new GameObject("NetSlot" + slot);
            go.transform.SetParent(root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();
            Vector3 start = SlotStart(slot, keeper);
            var facing = Quaternion.LookRotation(keeper ? SimConfig.KeeperFaceDir : Vector3.forward, Vector3.up);
            ragdoll.Build(start, facing, torso, limb, withGloves: keeper && glove != null);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, targetPos = start, targetYaw = facing.eulerAngles.y };

            if (!keeper)
            {
                var striker = go.AddComponent<Striker>();
                b.striker = striker;
                if (hostSim)
                {
                    if (isLocal) striker.Init(_input, ragdoll);          // host's own device
                    else { b.netInput = new NetInputSource(); striker.Init(b.netInput, ragdoll); }
                    AttachKick(ragdoll, striker);
                }
                else
                {
                    if (isLocal) striker.Init(_input, ragdoll);          // client-predicted local player
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
                kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.Yaw) : (() => b.netInput != null ? b.netInput.LookYaw : 0f));
                b.keeper = kc;
            }

            _bodies[slot] = b;
        }

        // The crosser slot reuses the pre-built _crosser (its ragdoll is already placed on the
        // wing). Host + human -> CrosserControl drives it (AutoServe off). Host + no human ->
        // the AI auto-serve loop (unchanged). Client -> display puppet.
        void SpawnCrosserBody(int slot, bool isLocal, bool hostSim, bool human)
        {
            var ragdoll = _crosser.Ragdoll;
            var b = new Body { ragdoll = ragdoll, isCrosser = true };
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
                var cc = _crosser.gameObject.AddComponent<CrosserControl>();
                IStrikerInput src = isLocal ? (IStrikerInput)_input : (b.netInput = new NetInputSource());
                cc.Init(src, _crosser, () => _crossMapOpen, () => _crossTarget);
                b.crosserCtl = cc;
            }
            else
            {
                _crosser.AutoServe = true;                               // AI auto-serve loop
            }

            _bodies[slot] = b;
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

        void OnMatchEvent(string tag) { Flash(tag); }
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // Crosser's cross-targeting map: free the cursor while open (to click the map), re-lock
        // on close. The chosen _crossTarget feeds CrosserControl (via the closure in SpawnBody).
        void SetCrossMapOpen(bool open)
        {
            _crossMapOpen = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
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

            // R: multiplayer re-serves the shared ball to the crosser (host-authoritative);
            // no player reset. (Single-player R still fully resets via GameManager.)
            // A human crosser shouldn't auto-refill the ball on R (they deliver manually).
            if (_input.ResetPressed && _s.IsHost && _crosser.AutoServe) { _goalHold = 0f; _crosser.Arm(0.4f); _ball.ResetTo(_launch.position); }

            // Local crosser: M toggles the cross-targeting map (freeze aim, free the cursor).
            if (_localIsCrosser && _input.CrossMapPressed) SetCrossMapOpen(!_crossMapOpen);

            // Local player: tick its own controller (host + client both predict the local body).
            // Shooters tick their Striker; a local keeper/crosser control is ticked host-side in
            // HostUpdate (they own the authoritative body); a client-local keeper/crosser has no
            // predicted control this pass (their body follows the host snapshot).
            var me = _bodies[_localSlot];
            if (me != null && me.striker != null) me.striker.Tick();

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
                // Tick whichever controller this body has (shooter / human keeper / human
                // crosser). The local body's Striker is already ticked in Update(); its human
                // keeper/crosser controls are ticked here (they run host-side only).
                if (remote && b.striker != null) b.striker.Tick();
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null) b.keeper.Tick();
                if (b.crosserCtl != null) b.crosserCtl.Tick();
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
                _s.SetLocalInput(_input.SampleFrame(_tick, _cam.Yaw));   // (host records its own input too)
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            // Send my input to the host each frame.
            _s.SetLocalInput(_input.SampleFrame(_tick++, _cam.Yaw));

            // Apply the latest snapshot: lerp puppet bodies + ball toward host state.
            if (_s.HasSnapshot)
            {
                var snap = _s.LatestSnapshot;
                if (snap.bodies != null)
                    foreach (var bs in snap.bodies)
                    {
                        if (bs.slot >= _bodies.Length) continue;
                        var b = _bodies[bs.slot];
                        if (b == null || bs.slot == _localSlot) continue;   // don't puppet our own predicted body
                        b.targetPos = bs.pos; b.targetYaw = bs.yaw;
                    }
                _ballTargetPos = snap.ballPos;
            }

            float k = 1f - Mathf.Exp(-SimConfig.NetInterpRate * Time.deltaTime);
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || i == _localSlot) continue;   // our own body is predicted, not puppeted
                Vector3 cur = b.ragdoll.Pelvis != null ? FlatFeet(b.ragdoll) : b.targetPos;
                Vector3 pos = Vector3.Lerp(cur, b.targetPos, k);
                float yaw = Mathf.LerpAngle(b.ragdoll.FacingRotation.eulerAngles.y, b.targetYaw, k);
                b.ragdoll.DisplaySnap(pos, Quaternion.Euler(0f, yaw, 0f));
            }
            // Ball: lerp its position (client ball is display-only; host owns physics).
            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(_ball.Rb.position, _ballTargetPos, k);
        }

        // Feet-level base position of a ragdoll (pelvis minus stand offset).
        static Vector3 FlatFeet(ActiveRagdoll r)
        {
            var p = r.Pelvis.position; p.y = 0f; return p;
        }

        void BroadcastSnapshot()
        {
            var list = new List<BodyState>();
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || b.ragdoll.Pelvis == null) continue;
                Vector3 p = b.ragdoll.Pelvis.position; p.y = 0f;
                list.Add(new BodyState { slot = (byte)i, pos = p, yaw = b.ragdoll.FacingRotation.eulerAngles.y, down = false });
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
            if (_s != null) { _s.MatchEvent -= OnMatchEvent; _s.ReplayStarted -= OnReplayStarted; _s.ReplayEnded -= OnReplayEnded; }
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
                ? "WASD move   M aim map   Tap Q/E driven   Hold Q/E chip   V ball cam"
                : (youAre == "Keeper"
                    ? "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   V ball cam"
                    : "WASD move   Mouse aim   LMB/RMB legs   Space jump   Q/E call low/high   V ball cam   R reset"));
            Hud.Flash(_flash, _flashTime / 1.6f);

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
