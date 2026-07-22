using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Networked SET-PIECES shootout (host-authoritative). One player is the goalkeeper
    /// (slot 0, human or AI); every other human is a SHOOTER. Shooters take turns, one at a
    /// time, each taking a fixed number of free kicks (ShotsEach). Most goals wins; ties are
    /// allowed. A defensive wall stands in front, and the arcadey set-piece shot physics
    /// (loft + curl, stat-scaled assist) apply.
    ///
    /// Host owns the ball, keeper AI, wall, goal detection, turn rotation, and the score
    /// tally, which it broadcasts (ShootoutState) so every peer shows the same scoreboard.
    /// Clients puppet all non-local bodies + the ball from snapshots, and client-predict
    /// their own body, exactly like NetStrikerMatch.
    ///
    /// Slots: 0 = keeper, 1..MaxSlots-2 = shooters. The crosser slot is unused here.
    /// </summary>
    public class NetSetPieceMatch : MonoBehaviour
    {
        const int ShotsEach = 10;

        class Body
        {
            public ActiveRagdoll ragdoll;
            public Striker striker;         // shooters (+ the local body); null for keeper puppet
            public NetInputSource netInput; // host: remote slots' input adapter
            public Goalkeeper ai;           // host: AI keeper on slot 0 when no human holds it
            public KeeperController keeper; // host: human keeper controller
            public bool isKeeper;
            public bool isShooter;
            public bool wasHuman;           // spawned for a human (despawn on leave) vs AI Clanker
            public Vector3 targetPos;       // client interp
            public float targetYaw;
            // client: free-running anim phase (run cadence) + last interpolated pos (move speed).
            public float animPhase;
            public Vector3 lastInterpPos;
            public bool hasLastInterp;
        }

        GameInput _input;
        GameCamera _cam;
        BallController _ball;
        Transform _root;
        NetSession _s;
        DefensiveWall _wall;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;
        bool _localIsKeeper;

        uint _tick; float _snapAccum;
        string _flash = ""; float _flashTime;
        float _goalLineZ;
        Vector3 _ballSpot;          // dead-ball free-kick spot (host-placed)
        Vector3 _wallCenter;        // host-placed wall centre

        // ---- shootout state (host-authoritative) ----
        readonly int[] _scored = new int[NetSession.MaxSlots];
        readonly int[] _taken  = new int[NetSession.MaxSlots];
        readonly List<int> _shooterSlots = new List<int>();   // live shooter slots, turn order
        int _turnIdx = -1;          // index into _shooterSlots of the active shooter
        int _activeShooter = 255;   // active shooter SLOT (255 = none / over)
        bool _over;

        // per-attempt phase (host)
        enum Phase { Armed, Live, Settle }
        Phase _phase = Phase.Armed;
        float _liveTime, _restTimer, _settle;
        bool _keeperTouched;

        // The set-piece taker for the active shooter (HOST only drives the scripted launch). The
        // local player also runs one for HUD prediction (its meter) even as a client. AI/parked
        // shooters do not take set-piece kicks in this shootout, so the taker is only armed for a
        // human active shooter; an AI active shooter falls back to an auto scripted launch.
        readonly SetPieceTaker _taker = new SetPieceTaker();
        bool _takerArmed;           // host: is _taker currently armed for the active shooter
        float _aiKickDelay;         // host: countdown for an AI active shooter's auto launch
        float _armedElapsed;        // host: time the current attempt has been Armed (idle safety)
        const float ArmedIdleTimeout = 12f;   // if a human never charges (AFK), auto-fire so the match progresses
        const float RunupWatchdog    = 4f;    // if a committed shot's AI runup never reaches the ball, force-fire

        // post-shot replay hold (reused pattern from NetStrikerMatch)
        ReplaySystem _replay;
        bool _replaying;
        float _goalHold;
        bool _advanceAfterReplay;   // set when an attempt resolved; advance turn when the hold/replay ends

        const float KickSpeed = 2.5f, RestSpeed = 0.7f, RestHold = 0.6f, MaxLiveTime = 6f;

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                              Material torso, Material limb, Material glove, Transform root)
        {
            _input = input; _cam = gameCam; _ball = ball; _root = root;
            _s = Multiplayer.Session;
            _localSlot = Mathf.Clamp(_s.LocalSlot, 0, NetSession.MaxSlots - 1);
            _goalLineZ = SimConfig.GoalCenter.z;
            // Host-placed free-kick spot + wall centre (synced in MatchConfig). fkPlaced tells
            // us the host set them; otherwise use the centred default (spot outside the box).
            var cfg = _s.Config;
            if (cfg.fkPlaced)
            {
                _ballSpot = new Vector3(cfg.fkBallX, SimConfig.BallRadius, cfg.fkBallZ);
                _wallCenter = new Vector3(cfg.fkWallX, 0f, cfg.fkWallZ);
            }
            else
            {
                _ballSpot = new Vector3(0f, SimConfig.BallRadius, SimConfig.GoalCenter.z - SimConfig.FreeKickDistance);
                _wallCenter = _ballSpot + (SimConfig.GoalCenter - _ballSpot).normalized * SimConfig.WallDistance;
            }
            _ball.SetPieceShot = true;   // arcadey loft + curl + stat-scaled assist
            _s.MatchEvent += OnMatchEvent;
            _s.ShootoutUpdated += OnShootoutUpdated;

            foreach (var slot in _s.Roster)
                SpawnBody(slot.slot, torso, limb, glove, root);

            // Build the live shooter turn order from the roster (occupied shooter slots).
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].isShooter) _shooterSlots.Add(i);

            var me = _bodies[_localSlot];
            _localIsKeeper = me != null && me.isKeeper;
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                if (_localIsKeeper)
                {
                    // Human keeper: identical to single-player goalkeeper mode. The camera pans in a
                    // cone from a FIXED forward base; the keeper reads that same cone yaw (KeeperLookYaw)
                    // and turns his body to it, so body + camera stay in lock-step.
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

            // Defensive wall: built on EVERY peer from the synced points so clients see + can
            // aim around it (the host owns the ball physics + hop; the client wall is a visual/
            // collision stand-in that just sits there). Host also arms the first turn.
            _wall = new DefensiveWall();
            _wall.Build(root, _ballSpot, _wallCenter, SimConfig.WallCount);   // host-placed centre
            if (_s.IsHost)
            {
                _ball.ResetTo(_ballSpot);
                if (_shooterSlots.Count > 0) BeginTurn(0);
                else { _activeShooter = 255; _over = true; }
                BroadcastShootout();
            }

            // Replay recorder over local bodies + ball.
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
            bool keeper = slot == 0;
            bool crosser = slot == NetSession.CrosserSlot;
            if (crosser) return;   // no crosser role in set pieces

            bool isLocal = slot == _localSlot;
            bool hostSim = _s.IsHost;
            var rosterSlot = _s.RosterSlot(slot);
            bool human = rosterSlot.human;
            bool ai = rosterSlot.ai;
            bool occupied = human || ai;
            if (!occupied && !isLocal) return;   // empty slot: nothing

            var go = new GameObject("SPSlot" + slot);
            go.transform.SetParent(root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();
            Vector3 start = keeper ? SimConfig.KeeperStart : ShooterWaitSpot(slot);
            var facing = Quaternion.LookRotation(keeper ? SimConfig.KeeperFaceDir : Vector3.forward, Vector3.up);
            // Human slots wear their synced appearance on an own-copy limb material, keeper or not.
            // A human keeper still gets gloves on top of the cosmetics (gloves + appearance are
            // independent branches in Build). AI bodies use the shared material and no cosmetics.
            bool wantsLook = human;
            Material slotLimb = wantsLook ? Make.Mat(rosterSlot.appearance.Skin) : limb;
            PlayerAppearance? appr = wantsLook ? rosterSlot.appearance : (PlayerAppearance?)null;
            // Per-slot painted jersey (human's own networked kit if arrived, else the shared team
            // torso). A late arrival is swapped in live via OnJerseyUpdated.
            Texture2D jt = human ? _s.JerseyForSlot(slot) : null;
            Material slotTorso = jt != null ? Make.MatTex(jt) : torso;
            ragdoll.Build(start, facing, slotTorso, slotLimb, withGloves: keeper && glove != null, appearance: appr);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, isShooter = !keeper, wasHuman = human,
                               targetPos = start, targetYaw = facing.eulerAngles.y };

            if (!keeper)
            {
                var striker = go.AddComponent<Striker>();
                b.striker = striker;
                if (hostSim)
                {
                    if (isLocal) striker.Init(_input, ragdoll);
                    else { b.netInput = new NetInputSource(); striker.Init(b.netInput, ragdoll); }
                    AttachKick(ragdoll, striker);
                }
                else
                {
                    if (isLocal) striker.Init(_input, ragdoll);   // client-predicted local shooter
                    else { striker.ControlEnabled = false; ragdoll.BecomeDisplayBody(); }
                }
                // Host parks non-active shooters each turn (BeginTurn). The client-local shooter
                // keeps ControlEnabled = true but only Ticks when it's the active shooter (the
                // LocalIsActiveShooter gate in Update), so it can't move out of turn.
            }
            else if (!hostSim)
            {
                ragdoll.BecomeDisplayBody();   // client keeper puppet
            }
            else if (!human)
            {
                var gk = go.AddComponent<Goalkeeper>(); gk.Init(ragdoll, _ball); b.ai = gk;
            }
            else
            {
                var kc = go.AddComponent<KeeperController>();
                if (isLocal) kc.Init(_input, ragdoll);
                else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll); }
                // Local keeper reads the cone yaw (KeeperLookYaw) so body + camera lock-step, exactly
                // like single-player. _cam.Yaw is stale in KeeperFollow mode. Remote keepers read the
                // yaw streamed over the wire (also the cone yaw; see SampleFrame below).
                kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.KeeperLookYaw)
                                            : (() => b.netInput != null ? b.netInput.LookYaw : 0f));
                b.keeper = kc;
            }

            _bodies[slot] = b;
        }

        // Where a shooter waits when it's not their turn: fanned behind the ball spot.
        Vector3 ShooterWaitSpot(int slot) =>
            _ballSpot + new Vector3((slot - 3) * 2.0f, 0f, -4f);

        void AttachKick(ActiveRagdoll ragdoll, Striker striker)
        {
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
        // Clients (and the host) learn the active shooter + over-state from the synced tally.
        // The client needs _activeShooter so LocalIsActiveShooter gates its own prediction; the
        // host already set these authoritatively before broadcasting (harmless to re-apply).
        void OnShootoutUpdated(ShootoutState s)
        {
            // On a client, re-arm the display taker for the NEXT shot. The active shooter changes
            // only via this synced tally (BeginTurn is host-only); but with a SINGLE shooter the
            // active slot never changes, so also re-arm whenever this client's own attempt count
            // advances (its last shot resolved). Either signal clears the stale armed state.
            if (!_s.IsHost)
            {
                int myTaken = (s.taken != null && _localSlot < s.taken.Length) ? s.taken[_localSlot] : 0;
                if (s.activeShooter != _activeShooter || myTaken != _lastLocalTaken)
                {
                    _takerArmed = false;
                    _taker.Reset();
                }
                _lastLocalTaken = myTaken;
            }
            _activeShooter = s.activeShooter;
            _over = s.over;
        }
        int _lastLocalTaken;
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

        // A player left mid-shootout: despawn their body so it doesn't freeze as a statue. The
        // keeper swaps to an AI keeper (goal must stay covered). A departed shooter is marked
        // finished (taken = ShotsEach) so AdvanceTurn skips them; if they were the active shooter,
        // advance the turn. The turn-order list itself is left intact (AdvanceTurn gates on taken).
        void OnRosterChanged()
        {
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.wasHuman) continue;
                if (_s.RosterSlot(i).human) continue;   // still human

                if (b.isKeeper)
                {
                    if (b.keeper != null) b.keeper = null;
                    b.netInput = null; b.wasHuman = false;
                    if (_s.IsHost && b.ai == null && b.ragdoll != null)
                    { var gk = b.ragdoll.gameObject.AddComponent<Goalkeeper>(); gk.Init(b.ragdoll, _ball); b.ai = gk; }
                    continue;
                }

                // Shooter left: mark finished so the rotation skips them, then remove the body.
                if (_s.IsHost)
                {
                    _taken[i] = ShotsEach;
                    bool wasActive = i == _activeShooter;
                    if (b.ragdoll != null) Destroy(b.ragdoll.gameObject);
                    _bodies[i] = null;
                    BroadcastShootout();
                    if (wasActive && !_over && _phase == Phase.Armed) { _takerArmed = false; _taker.Reset(); AdvanceTurn(); }
                }
                else
                {
                    if (b.ragdoll != null) Destroy(b.ragdoll.gameObject);
                    _bodies[i] = null;
                }
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

        // ------------------------------------------------------------ turn flow (host)
        // Put shooter `_shooterSlots[idx]` on the spot, enable only their control, ground the
        // wall + keeper, reset the ball, and start the Armed phase.
        void BeginTurn(int idx)
        {
            _turnIdx = idx;
            _activeShooter = _shooterSlots[idx];
            _phase = Phase.Armed;
            _liveTime = _restTimer = _settle = 0f;
            _keeperTouched = false;

            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                var b = _bodies[i];
                if (b == null || b.striker == null) continue;
                bool active = i == _activeShooter;
                b.striker.ControlEnabled = active;
                b.ragdoll.ResetTo(active ? _ballSpot + new Vector3(0f, 0f, -3f) : ShooterWaitSpot(i),
                                  Quaternion.identity);
                b.striker.ForceRecover();
                // Restore ball<->body collision for every shooter, clearing any ignore left by the
                // previous turn's taker/auto launch. The active shooter re-ignores when it strikes.
                _ball.IgnoreBody(b.ragdoll, false);
            }
            if (_wall != null) _wall.Ground();
            foreach (var b in _bodies) if (b?.ai != null) b.ai.ResetTo(SimConfig.KeeperStart);
            _ball.ResetTo(_ballSpot);
            // Re-arm the taker for the new active shooter next HostUpdate; reset the AI auto-kick +
            // the idle safety timer so a fresh shooter always gets a clean attempt.
            _takerArmed = false;
            _taker.Reset();
            _aiKickDelay = Random.Range(0.6f, 1.4f);
            _armedElapsed = 0f;
        }

        // Advance to the next live shooter that still has attempts left; end the match if none.
        void AdvanceTurn()
        {
            for (int step = 1; step <= _shooterSlots.Count; step++)
            {
                int idx = (_turnIdx + step) % _shooterSlots.Count;
                if (_taken[_shooterSlots[idx]] < ShotsEach) { BeginTurn(idx); BroadcastShootout(); return; }
            }
            // Everyone finished their 10. Restore the last shooter's ball collision + reset the
            // taker (no more BeginTurn will run to do it), so nothing leaks if the scene is reused.
            _takerArmed = false;
            _taker.Reset();
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].ragdoll != null) _ball.IgnoreBody(_bodies[i].ragdoll, false);
            _activeShooter = 255; _over = true;
            _phase = Phase.Settle; _settle = float.PositiveInfinity;
            BroadcastShootout();
            Flash(WinnerText());
        }

        void BroadcastShootout()
        {
            var st = new ShootoutState
            {
                activeShooter = (byte)_activeShooter,
                over = _over,
                scored = new byte[NetSession.MaxSlots],
                taken  = new byte[NetSession.MaxSlots],
            };
            for (int i = 0; i < NetSession.MaxSlots; i++)
            {
                st.scored[i] = (byte)Mathf.Min(255, _scored[i]);
                st.taken[i]  = (byte)Mathf.Min(255, _taken[i]);
            }
            _s.BroadcastShootout(st);
        }

        // ------------------------------------------------------------ replay hooks
        void OnReplayStarted()
        {
            if (_replay == null || _replaying) return;
            _replaying = true;
            _cam.SetMode(GameCamera.Mode.Broadcast);
            _replay.Play(SimConfig.ReplaySlowMul);
            Flash("REPLAY  (click to skip)");
        }
        void OnReplayEnded()
        {
            if (!_replaying) return;
            _replaying = false;
            if (_replay != null) _replay.Stop();
            _cam.SetMode(GameCamera.Mode.Follow);
            if (_s.IsHost && _advanceAfterReplay) { _advanceAfterReplay = false; AdvanceTurn(); }
        }

        // ------------------------------------------------------------ loop
        void Update()
        {
            if (_s == null || PauseMenu.Paused) return;

            if (_replaying)
            {
                if (_input.LeftClickPressed) _s.VoteSkip();
                if (_s.IsHost && (_replay == null || !_replay.IsPlaying)) _s.EndReplayHost();
                if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
                return;
            }

            // A local SHOOTER does NOT run Striker locomotion in set pieces (no movement) - the
            // SetPieceTaker owns the body. The host drives the AUTHORITATIVE taker in HostUpdate;
            // a non-host client runs a DISPLAY-ONLY taker for its own active shot (meter HUD + body
            // animation prediction), which never touches the host-owned kinematic ball.
            var me = _bodies[_localSlot];
            if (!_s.IsHost && LocalIsActiveShooter() && me != null && me.striker != null)
                ClientDriveTaker(me);

            if (_s.IsHost) HostUpdate();
            else ClientUpdate();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        bool LocalIsActiveShooter() => _localSlot == _activeShooter;

        void HostUpdate()
        {
            // Post-shot hold: freeze gameplay, keep physics + recorder + snapshots running.
            if (_goalHold > 0f)
            {
                _goalHold -= Time.deltaTime;
                if (_goalHold <= 0f) _s.BeginReplay();
                PublishSnapshotIfDue();
                return;
            }

            // Feed remote inputs + tick keepers/AI. Shooters do NOT run Striker locomotion (no
            // movement in set pieces); the active shooter is driven by the SetPieceTaker below.
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null) continue;
                bool remote = i != _localSlot;
                if (remote && b.netInput != null) b.netInput.Feed(_s.InputForSlot(i));
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null) b.keeper.Tick();
            }

            // Authoritative taker: drive the active shooter's aesthetic runup + scripted launch
            // from that shooter's input (local device if the host holds the slot, else the slot's
            // networked input). An AI-held active shooter auto-fires after a short delay.
            HostDriveActiveShooter();

            if (_wall != null) _wall.Tick();

            if (!_over) HostTickAttempt();

            PublishSnapshotIfDue();
        }

        // Host: drive the active shooter's SetPieceTaker (authoritative scripted launch). Only in
        // the Armed phase (a struck shot is Live; the taker keeps ticking its follow-through/settle
        // harmlessly). A human active shooter is driven by their input; an AI one auto-fires.
        void HostDriveActiveShooter()
        {
            // Only drive the taker while the ball is still ARMED (dead on the spot). The runup +
            // scripted launch all happen here; once the launch trips ball speed the phase flips to
            // Live and we stop touching the taker (no re-arm flicker, no double launch).
            if (_over || _activeShooter == 255 || _phase != Phase.Armed) return;
            var b = _bodies[_activeShooter];
            if (b == null || b.ragdoll == null || !b.isShooter) return;

            bool human = _s.RosterSlot(_activeShooter).human;

            if (human)
            {
                if (!_takerArmed)
                {
                    // The active shooter's input: local device if the host holds the slot, else
                    // that slot's networked input (fed above). Remote skill trees are not synced,
                    // so a remote shooter gets a neutral-competent combined stat; the host-local
                    // shooter uses its real profile.
                    IStrikerInput src = (_activeShooter == _localSlot) ? (IStrikerInput)_input : b.netInput;
                    float combined = (_activeShooter == _localSlot) ? -1f : 0.6f;
                    _taker.Begin(src, b.ragdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                                 displayOnly: false, combinedOverride: combined);
                    _takerArmed = true;
                }
                _taker.Tick();

                // Safety timers so a turn can never hang (the whole shootout would stall):
                //  - AFK: the player never charges -> the taker sits in Charging. Fire after the
                //    idle timeout. An ENGAGED charger (HasCharged) does NOT accrue this, so holding
                //    the meter a long time is never auto-fired.
                //  - Stuck runup: the player committed but the AI runup never reaches the ball
                //    (knocked/obstructed) so it never launches. Fire after a shorter watchdog once
                //    committed. Both only matter while still Armed (pre-launch).
                if (_phase == Phase.Armed && _taker.Active)
                {
                    _armedElapsed += Time.deltaTime;
                    bool afkStuck = !_taker.HasCharged && _armedElapsed > ArmedIdleTimeout;
                    bool runupStuck = _taker.HasCharged && _armedElapsed > RunupWatchdog;
                    if (afkStuck || runupStuck) AutoLaunch(b, 0.6f);
                }
                else if (_phase != Phase.Armed) _armedElapsed = 0f;
            }
            else if (_phase == Phase.Armed)
            {
                // AI active shooter: no meter; auto-fire after a short delay with a competent shot.
                _aiKickDelay -= Time.deltaTime;
                if (_aiKickDelay <= 0f) AutoLaunch(b, 0.7f);
            }
        }

        // Host: an auto (AI / AFK) scripted launch for the active shooter with a competent power +
        // a random spin flavour. Makes the ball ignore the shooter's body (like the taker) so the
        // parked body cannot deflect the launched ball, then launches by code.
        void AutoLaunch(Body b, float combined)
        {
            _ball.IgnoreBody(b.ragdoll, true);
            var spins = new[] { BallController.SetPieceSpin.None, BallController.SetPieceSpin.CurveLeft,
                                BallController.SetPieceSpin.CurveRight, BallController.SetPieceSpin.TopSpin };
            var spin = spins[Random.Range(0, spins.Length)];
            _ball.ResetTo(_ballSpot);
            // AI never overpowers the bar (overcharge 0 -> stays under the crossbar); its power stat
            // tracks its competence so the pace reads right.
            _ball.LaunchSetPiece(Random.Range(0.55f, 0.8f), spin, Random.Range(0.4f, 0.9f),
                                 0f, Mathf.Clamp01(combined), SimConfig.AttackGoalCenter,
                                 0f, Mathf.Clamp01(combined));
            _takerArmed = false;
        }

        // Client (non-host) prediction: run a DISPLAY-ONLY taker for the local active shot so the
        // player sees the power meter + their body animate. It never launches (the host owns the
        // authoritative launch; the client ball is kinematic and snapshot-driven).
        void ClientDriveTaker(Body me)
        {
            if (!_takerArmed)
            {
                _taker.Begin(_input, me.ragdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                             displayOnly: true, combinedOverride: -1f);
                _takerArmed = true;
            }
            _taker.Tick();
        }

        // Per-attempt state machine (host): detect the kick, watch for goal/miss, resolve.
        void HostTickAttempt()
        {
            Vector3 c = _ball.transform.position;
            switch (_phase)
            {
                case Phase.Armed:
                    if (_ball.Speed > KickSpeed)
                    {
                        _phase = Phase.Live; _liveTime = _restTimer = 0f; _keeperTouched = false;
                        if (_wall != null) _wall.TriggerJump();
                        Flash("STRIKE!");
                    }
                    break;

                case Phase.Live:
                    _liveTime += Time.deltaTime;
                    if (!_keeperTouched && KeeperContactedBall(c)) _keeperTouched = true;
                    if (BallInGoal(c)) { ResolveAttempt(true); break; }
                    if (_ball.Speed < RestSpeed) _restTimer += Time.deltaTime; else _restTimer = 0f;
                    bool out_ = c.y < -3f || Mathf.Abs(c.x) > SimConfig.FieldWidth || Mathf.Abs(c.z) > SimConfig.FieldLength;
                    if (out_ || _restTimer > RestHold || _liveTime > MaxLiveTime) ResolveAttempt(false);
                    break;

                case Phase.Settle:
                    break;
            }
        }

        // Score the attempt, broadcast the tally, and roll the post-shot hold; the turn advances
        // when the hold/replay ends (OnReplayEnded).
        void ResolveAttempt(bool goal)
        {
            _taken[_activeShooter]++;
            if (goal) { _scored[_activeShooter]++; _s.BroadcastEvent("GOAL!"); }
            else _s.BroadcastEvent(_keeperTouched ? "SAVED!" : "MISS");
            BroadcastShootout();
            _phase = Phase.Settle;
            _advanceAfterReplay = true;
            _goalHold = SimConfig.ReplayHold;   // brief live hold, then replay, then AdvanceTurn
        }

        void PublishSnapshotIfDue()
        {
            _snapAccum += Time.deltaTime;
            if (_snapAccum >= SimConfig.NetSnapshotInterval)
            {
                _snapAccum = 0f;
                // A local keeper sends its cone yaw (KeeperLookYaw); everyone else sends camera yaw.
                float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
                _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw));
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            // A local keeper sends its cone yaw (KeeperLookYaw); everyone else sends camera yaw.
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw));

            // Reconcile our own predicted body (mainly the local keeper, who moves freely) against
            // the host's authoritative state.
            ReconcileLocalBody();

            // Render remote bodies + ball at (now - InterpDelay), interpolating between the two
            // buffered snapshots bracketing that render time (smooth under uneven packet arrival).
            if (!_s.SampleInterpolated(SimConfig.NetInterpDelay, out var a, out var bSnap, out float f))
                return;

            for (int i = 0; i < _bodies.Length; i++)
            {
                var body = _bodies[i];
                if (body == null || i == _localSlot) continue;
                if (!FindBody(a, i, out var sa)) continue;
                if (!FindBody(bSnap, i, out var sb)) sb = sa;
                Vector3 pos = Vector3.Lerp(sa.pos, sb.pos, f);
                float yaw = Mathf.LerpAngle(sa.yaw, sb.yaw, f);
                float speed = 0f;
                if (body.hasLastInterp) { Vector3 d = pos - body.lastInterpPos; d.y = 0f; speed = d.magnitude / Mathf.Max(1e-4f, Time.deltaTime); }
                body.lastInterpPos = pos; body.hasLastInterp = true;
                float moveAmount = Mathf.Clamp01(speed / SimConfig.StrikerMoveSpeed);
                body.animPhase += Time.deltaTime * SimConfig.StrideRateMax * moveAmount / (2f * Mathf.PI);
                body.ragdoll.DisplayAnim(pos, Quaternion.Euler(0f, yaw, 0f), (AnimState)(sb.anim), body.animPhase, moveAmount);
            }
            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(a.ballPos, bSnap.ballPos, f);
        }

        // Bounded server reconciliation of the local predicted body (see NetStrikerMatch for the
        // rationale: the ragdoll isn't re-simulatable, so we ease/snap toward authoritative rather
        // than rollback+replay). Skipped while a set-piece taker owns the body or it is airborne.
        void ReconcileLocalBody()
        {
            var me = _bodies[_localSlot];
            if (me == null || me.ragdoll == null || me.ragdoll.Pelvis == null) return;
            if (!me.isKeeper) return;   // only the free-moving local keeper predicts; shooters are taker-owned
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

        // Find a slot's BodyState in a snapshot (false if absent).
        static bool FindBody(in Snapshot s, int slot, out BodyState bs)
        {
            if (s.bodies != null)
                for (int i = 0; i < s.bodies.Length; i++)
                    if (s.bodies[i].slot == slot) { bs = s.bodies[i]; return true; }
            bs = default; return false;
        }

        // Host: a body's animation state for the snapshot (keeper dive > airborne > moving > idle).
        static AnimState AnimStateOf(Body b)
        {
            if (b.ragdoll == null) return AnimState.Idle;
            if (b.keeper != null && b.keeper.IsCommitting) return AnimState.Dive;
            if (b.ai != null && b.ai.WasDivingSave) return AnimState.Dive;
            if (b.striker != null && !b.ragdoll.IsGrounded) return AnimState.Jump;
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
                list.Add(new BodyState { slot = (byte)i, pos = p, yaw = b.ragdoll.FacingRotation.eulerAngles.y,
                                         down = false, emoteId = 255, anim = (byte)AnimStateOf(b),
                                         lastInputTick = _s.InputTickForSlot(i) });
            }
            _s.BroadcastSnapshot(new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
                homeScore = 0, awayScore = 0, bodies = list.ToArray(),
            });
        }

        bool BallInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            return c.z - r >= _goalLineZ && c.z <= _goalLineZ + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
        }

        bool KeeperContactedBall(Vector3 bp)
        {
            var kb = _bodies[0];
            if (kb == null || kb.ragdoll == null) return false;
            foreach (var t in kb.ragdoll.BoneTransforms)
                if (t != null && Vector3.Distance(t.position, bp) < SimConfig.BallRadius + 0.28f) return true;
            return false;
        }

        static void LockCursor() { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }

        void OnDestroy()
        {
            if (_s != null)
            {
                _s.MatchEvent -= OnMatchEvent;
                _s.ShootoutUpdated -= OnShootoutUpdated;
                _s.ReplayStarted -= OnReplayStarted;
                _s.ReplayEnded -= OnReplayEnded;
                _s.JerseyUpdated -= OnJerseyUpdated;
                _s.RosterChanged -= OnRosterChanged;
            }
            if (_ball != null) { _ball.SetPieceShot = false; if (_ball.Rb != null) _ball.Rb.isKinematic = false; }
        }

        // Winner text from the synced tally (works on host + client via _s.LatestShootout).
        string WinnerText()
        {
            var st = _s.LatestShootout;
            if (st.scored == null) return "FULL TIME";
            int best = -1;
            for (int i = 0; i < st.scored.Length; i++) best = Mathf.Max(best, st.scored[i]);
            int winners = 0, winSlot = -1;
            for (int i = 0; i < st.scored.Length; i++)
                if (st.taken[i] > 0 && st.scored[i] == best) { winners++; winSlot = i; }
            if (winners != 1) return "TIE  (" + best + ")";
            return RosterName(winSlot) + " WINS  (" + best + "/" + ShotsEach + ")";
        }

        string RosterName(int slot)
        {
            var r = _s.Roster;
            if (r != null) for (int i = 0; i < r.Length; i++) if (r[i].slot == slot) return r[i].name;
            return "Shooter " + slot;
        }

        void OnGUI()
        {
            if (_s == null) return;
            Hud.Begin();
            var me = _bodies[_localSlot];
            string youAre = me != null && me.isKeeper ? "Keeper" : "Shooter " + _localSlot;
            var p = Hud.PanelStart(_s.IsHost ? "SET PIECES (HOST)" : "SET PIECES", 2);
            Hud.Stat(ref p, "You are", youAre);
            var st = _s.LatestShootout;
            bool over = st.scored != null && st.over;
            Hud.Stat(ref p, "Status", over ? "FULL TIME" :
                     _activeShooter == 255 ? "..." :
                     (_localSlot == _activeShooter ? "YOUR SHOT" : RosterName(_activeShooter) + " to shoot"));

            Hud.Legend(youAre == "Keeper"
                ? "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   V ball cam"
                : "HOLD Space power (release to shoot)   A/D curl   W topspin   S knuckle   V ball cam");
            Hud.Flash(_flash, _flashTime / 1.6f);

            DrawScoreboard(st);
            DrawPowerMeter();
        }

        // Centered power meter shown while the LOCAL player is charging their set-piece shot.
        void DrawPowerMeter()
        {
            if (_localIsKeeper || !LocalIsActiveShooter() || !_taker.IsCharging) return;
            float w = 320f, h = 22f;
            float x = (Screen.width - w) * 0.5f, y = Screen.height - 92f;

            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.88f);
            GUI.DrawTexture(new Rect(x - 3f, y - 3f, w + 6f, h + 6f), Texture2D.whiteTexture);
            float f = Mathf.Clamp01(_taker.Meter);
            Color fill = f < 0.5f ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(0.95f, 0.85f, 0.2f), f * 2f)
                                  : Color.Lerp(new Color(0.95f, 0.85f, 0.2f), new Color(0.9f, 0.2f, 0.16f), (f - 0.5f) * 2f);
            GUI.color = new Color(0.14f, 0.15f, 0.19f, 0.9f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, w * f, h), Texture2D.whiteTexture);
            GUI.color = prev;
            var lbl = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(x, y - 20f, w, 18f), "POWER  (release to shoot)", lbl);
        }

        // Scoreboard: a clean dark card, per-shooter rows with the name, the running goal count,
        // and a strip of ShotsEach PIPS - green = scored, red = missed, dim = not taken yet. The
        // active shooter's row is highlighted; a winner banner shows at full time.
        void DrawScoreboard(ShootoutState st)
        {
            if (st.scored == null) return;

            // Palette.
            Color cPanel  = new Color(0.06f, 0.07f, 0.10f, 0.90f);
            Color cHeadBar = new Color(0.10f, 0.12f, 0.17f, 0.95f);
            Color cActive = new Color(0.16f, 0.32f, 0.52f, 0.55f);
            Color cGold   = new Color(1f, 0.86f, 0.32f);
            Color cGoal   = new Color(0.24f, 0.85f, 0.36f);
            Color cMiss   = new Color(0.85f, 0.26f, 0.22f);
            Color cEmpty  = new Color(1f, 1f, 1f, 0.14f);

            int rows = 0;
            for (int i = 1; i < NetSession.CrosserSlot; i++) if (_bodies[i] != null && _bodies[i].isShooter) rows++;
            if (rows == 0) { if (st.over) DrawWinnerBanner(); return; }

            float pad = 12f, headH = 34f, rowH = 34f, w = 340f;
            float x = Screen.width - w - 22f, y = 84f;
            float panelH = headH + rows * rowH + pad * 2f;

            var prev = GUI.color;
            // Card + header bar.
            GUI.color = cPanel;
            GUI.DrawTexture(new Rect(x - pad, y - pad, w + pad * 2f, panelH), Texture2D.whiteTexture);
            GUI.color = cHeadBar;
            GUI.DrawTexture(new Rect(x - pad, y - pad, w + pad * 2f, headH), Texture2D.whiteTexture);
            GUI.color = prev;

            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = cGold } };
            GUI.Label(new Rect(x, y - pad, w, headH), "  SHOOTOUT   best of " + ShotsEach, hdr);

            var nameSt  = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            var goalsSt = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };

            float ry = y - pad + headH;
            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                if (_bodies[i] == null || !_bodies[i].isShooter) continue;
                bool active = i == _activeShooter && !st.over;
                if (active) { GUI.color = cActive; GUI.DrawTexture(new Rect(x - pad, ry, w + pad * 2f, rowH), Texture2D.whiteTexture); GUI.color = prev; }

                int sc = i < st.scored.Length ? st.scored[i] : 0;
                int tk = i < st.taken.Length ? st.taken[i] : 0;

                GUI.Label(new Rect(x, ry, w * 0.42f, rowH), (active ? "▸ " : "  ") + RosterName(i), nameSt);
                GUI.Label(new Rect(x + w * 0.42f, ry, w * 0.14f, rowH), sc.ToString(), goalsSt);

                // Pip strip on the right: fill green up to `sc`, red for missed attempts, dim rest.
                float pipsX = x + w * 0.58f, pipsW = w * 0.42f;
                float gap = 3f, pipW = (pipsW - gap * (ShotsEach - 1)) / ShotsEach, pipH = 10f;
                float py = ry + (rowH - pipH) * 0.5f;
                for (int s = 0; s < ShotsEach; s++)
                {
                    Color pc = s < sc ? cGoal : (s < tk ? cMiss : cEmpty);
                    GUI.color = pc;
                    GUI.DrawTexture(new Rect(pipsX + s * (pipW + gap), py, pipW, pipH), Texture2D.whiteTexture);
                }
                GUI.color = prev;
                ry += rowH;
            }

            if (st.over) DrawWinnerBanner();
        }

        void DrawWinnerBanner()
        {
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.82f);
            GUI.DrawTexture(new Rect(0, Screen.height * 0.4f - 8f, Screen.width, 76f), Texture2D.whiteTexture);
            GUI.color = prev;
            var banner = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60f), WinnerText(), banner);
        }
    }
}
