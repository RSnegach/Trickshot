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
            public Striker striker;         // null for the keeper puppet
            public NetInputSource netInput; // host: remote slots' input adapter
            public Goalkeeper ai;           // host: AI keeper when no human holds slot 0
            public bool isKeeper;
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

        // Per-machine post-goal replay (each peer replays its own local view).
        ReplaySystem _replay;
        bool _replaying;

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball, Crosser crosser,
                              AimReticle reticle, Transform launch,
                              Material torso, Material limb, Material glove, Transform root)
        {
            _input = input; _cam = gameCam; _ball = ball; _crosser = crosser; _reticle = reticle; _launch = launch;
            _s = Multiplayer.Session;
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _goalLineZ = SimConfig.GoalCenter.z;
            _s.MatchEvent += OnMatchEvent;

            // Spawn a body per active slot from the roster (keeper slot 0 + shooter slots).
            foreach (var slot in _s.Roster)
                SpawnBody(slot.slot, slot.slot == 0, torso, limb, glove, root);

            // Camera follows the LOCAL body; local striker turns to camera yaw.
            var me = _bodies[_localSlot];
            if (me != null && me.ragdoll.Pelvis != null)
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

        void SpawnBody(int slot, bool keeper, Material torso, Material limb, Material glove, Transform root)
        {
            var go = new GameObject("NetSlot" + slot);
            go.transform.SetParent(root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();
            Vector3 start = SlotStart(slot, keeper);
            var facing = Quaternion.LookRotation(keeper ? SimConfig.KeeperFaceDir : Vector3.forward, Vector3.up);
            ragdoll.Build(start, facing, torso, limb, withGloves: keeper && glove != null);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, targetPos = start, targetYaw = facing.eulerAngles.y };

            bool isLocal = slot == _localSlot;
            bool hostSim = _s.IsHost;

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
            else
            {
                // Host keeper: simple AI goaltender (reuse Goalkeeper if a human doesn't hold it).
                if (!_s.SlotIsHuman(0)) { var gk = go.AddComponent<Goalkeeper>(); gk.Init(ragdoll, _ball); b.striker = null; b.netInput = null; b.ai = gk; }
                else { b.netInput = new NetInputSource(); }   // (human keeper: keeper control TBD; treated as AI-less puppet host-side)
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
            if (_input.ResetPressed && _s.IsHost) { _crosser.Arm(0.4f); _ball.ResetTo(_launch.position); }

            // Local player: tick its Striker (host + client both predict the local body).
            var me = _bodies[_localSlot];
            if (me != null && me.striker != null) me.striker.Tick();

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        void HostUpdate()
        {
            // Feed remote slots' latest input, tick their strikers + the AI keeper.
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || i == _localSlot) continue;
                if (b.netInput != null && b.striker != null) { b.netInput.Feed(_s.InputForSlot(i)); b.striker.Tick(); }
                if (b.ai != null) b.ai.Tick();
            }

            // Crosser + ball + goal detection (authoritative). A goal rolls the replay for
            // everyone (OnReplayEnded re-arms the crosser + ball afterward).
            if (_crosser.Tick()) Flash("CROSS!");
            Vector3 c = _ball.transform.position;
            if (BallInGoal(c)) { _goals++; _s.BroadcastEvent("GOAL!"); _s.BeginReplay(); }

            // Publish a snapshot every fixed-ish step (throttled).
            _snapAccum += Time.deltaTime;
            if (_snapAccum >= SimConfig.NetSnapshotInterval)
            {
                _snapAccum = 0f;
                _s.SetLocalInput(_input.SampleFrame(_tick, _cam.Yaw));   // (host records its own input too)
                BroadcastSnapshot();
                _tick++;
            }
        }
        float _snapAccum;

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
            var p = Hud.PanelStart(_s.IsHost ? "STRIKER (HOST)" : "STRIKER (CLIENT)", 2);
            Hud.Stat(ref p, "Goals", _goals.ToString());
            Hud.Stat(ref p, "You are", _bodies[_localSlot] != null && _bodies[_localSlot].isKeeper ? "Keeper" : "Shooter " + _localSlot);
            Hud.Legend("WASD move   Mouse aim   LMB/RMB legs   Space jump   V ball cam   R reset");
            Hud.Flash(_flash, _flashTime / 1.6f);
        }
    }
}
