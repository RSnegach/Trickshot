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
            public Vector3 targetPos;       // client interp
            public float targetYaw;
        }

        GameInput _input;
        GameCamera _cam;
        BallController _ball;
        Transform _root;
        NetSession _s;
        DefensiveWall _wall;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;

        uint _tick; float _snapAccum;
        string _flash = ""; float _flashTime;
        float _goalLineZ;
        Vector3 _ballSpot;          // dead-ball free-kick spot (host-placed)
        Vector3 _wallCenter;        // host-placed wall centre
        Vector3 _ballTargetPos;     // client ball interp

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
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, null);
                _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look);
                if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw);
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
            ragdoll.Build(start, facing, torso, limb, withGloves: keeper && glove != null);

            var b = new Body { ragdoll = ragdoll, isKeeper = keeper, isShooter = !keeper,
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
                kc.SetLookYawSource(isLocal ? (System.Func<float>)(() => _cam.Yaw)
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
            _activeShooter = s.activeShooter;
            _over = s.over;
        }
        void Flash(string s) { _flash = s; _flashTime = 1.6f; }

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
            }
            if (_wall != null) _wall.Ground();
            foreach (var b in _bodies) if (b?.ai != null) b.ai.ResetTo(SimConfig.KeeperStart);
            _ball.ResetTo(_ballSpot);
        }

        // Advance to the next live shooter that still has attempts left; end the match if none.
        void AdvanceTurn()
        {
            for (int step = 1; step <= _shooterSlots.Count; step++)
            {
                int idx = (_turnIdx + step) % _shooterSlots.Count;
                if (_taken[_shooterSlots[idx]] < ShotsEach) { BeginTurn(idx); BroadcastShootout(); return; }
            }
            // Everyone finished their 10.
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

            // Local body ticks (host + client predict the local body). Only the active shooter
            // or the keeper actually has control enabled.
            var me = _bodies[_localSlot];
            if (me != null && me.striker != null && LocalIsActiveShooter()) me.striker.Tick();

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

            // Drive bodies: active shooter's remote input, keeper (human/AI). Parked shooters
            // have ControlEnabled=false so their Tick is a no-op, but skip to save work.
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null) continue;
                bool remote = i != _localSlot;
                if (remote && b.netInput != null) b.netInput.Feed(_s.InputForSlot(i));
                if (remote && b.striker != null && i == _activeShooter) b.striker.Tick();
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null) b.keeper.Tick();
            }
            if (_wall != null) _wall.Tick();

            if (!_over) HostTickAttempt();

            PublishSnapshotIfDue();
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
                _s.SetLocalInput(_input.SampleFrame(_tick, _cam.Yaw));
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            _s.SetLocalInput(_input.SampleFrame(_tick++, _cam.Yaw));
            if (_s.HasSnapshot)
            {
                var snap = _s.LatestSnapshot;
                if (snap.bodies != null)
                    foreach (var bs in snap.bodies)
                    {
                        if (bs.slot >= _bodies.Length) continue;
                        var b = _bodies[bs.slot];
                        if (b == null || bs.slot == _localSlot) continue;
                        b.targetPos = bs.pos; b.targetYaw = bs.yaw;
                    }
                _ballTargetPos = snap.ballPos;
            }
            float k = 1f - Mathf.Exp(-SimConfig.NetInterpRate * Time.deltaTime);
            for (int i = 0; i < _bodies.Length; i++)
            {
                var b = _bodies[i];
                if (b == null || i == _localSlot) continue;
                Vector3 cur = b.ragdoll.Pelvis != null ? FlatFeet(b.ragdoll) : b.targetPos;
                Vector3 pos = Vector3.Lerp(cur, b.targetPos, k);
                float yaw = Mathf.LerpAngle(b.ragdoll.FacingRotation.eulerAngles.y, b.targetYaw, k);
                b.ragdoll.DisplaySnap(pos, Quaternion.Euler(0f, yaw, 0f));
            }
            _ball.Rb.isKinematic = true;
            _ball.Rb.position = Vector3.Lerp(_ball.Rb.position, _ballTargetPos, k);
        }

        static Vector3 FlatFeet(ActiveRagdoll r) { var p = r.Pelvis.position; p.y = 0f; return p; }

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
                : "WASD approach   Mouse aim   LMB/RMB legs   Space jump   V ball cam");
            Hud.Flash(_flash, _flashTime / 1.6f);

            DrawScoreboard(st);
        }

        // Scoreboard: each shooter's name + scored/taken, active row highlighted, final banner.
        void DrawScoreboard(ShootoutState st)
        {
            if (st.scored == null) return;
            float w = 300f, x = Screen.width - w - 20f, y = 90f, rowH = 26f;
            var name = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            var score = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
            var hdr = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.86f, 0.32f) } };

            var prev = GUI.color; GUI.color = new Color(0.07f, 0.08f, 0.11f, 0.85f);
            int rows = 0;
            for (int i = 1; i < NetSession.CrosserSlot; i++) if (_bodies[i] != null && _bodies[i].isShooter) rows++;
            GUI.DrawTexture(new Rect(x - 8f, y - 8f, w + 16f, (rows + 1) * rowH + 20f), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(x, y, w, rowH), "SHOOTOUT — best of " + ShotsEach, hdr);
            float ry = y + rowH + 4f;
            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                if (_bodies[i] == null || !_bodies[i].isShooter) continue;
                bool active = i == _activeShooter && !(st.over);
                if (active) { var c0 = GUI.color; GUI.color = new Color(0.16f, 0.3f, 0.5f, 0.6f); GUI.DrawTexture(new Rect(x - 4f, ry, w + 8f, rowH - 2f), Texture2D.whiteTexture); GUI.color = c0; }
                int sc = i < st.scored.Length ? st.scored[i] : 0;
                int tk = i < st.taken.Length ? st.taken[i] : 0;
                GUI.Label(new Rect(x, ry, w * 0.6f, rowH), RosterName(i) + (active ? "  ▶" : ""), name);
                GUI.Label(new Rect(x, ry, w, rowH), sc + " / " + tk, score);
                ry += rowH;
            }

            if (st.over)
            {
                var banner = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 60f), WinnerText(), banner);
            }
        }
    }
}
