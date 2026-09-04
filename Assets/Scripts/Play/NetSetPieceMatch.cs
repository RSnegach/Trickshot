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

        // ---- ACCURACY mode ----
        // The networked ACCURACY competition is the same dead-ball rig as the set-piece shootout
        // (keeper slot 0 human/AI/none + shooters taking turns), running the SAME three-strikes
        // game as single player (see AccuracyGame) one player at a time:
        //   * each round is ONE kick from a random spot in the shot band at a single
        //     patrolling target, and clearing it needs the ball in the goal AND through the target;
        //   * anything else is a strike, and a shooter is eliminated on their third;
        //   * difficulty comes from the shooter's OWN round number, so everyone meets the same
        //     ladder (SimConfig.AccuracyTier and friends) regardless of turn order;
        //   * an eliminated shooter gets their end screen, then play cycles to the next one.
        // Set by NetAccuracyMatch before Configure, so all the shared netcode below is reused.
        //
        // WIRE REUSE: the shootout tally already syncs scored[]/taken[] per slot, so accuracy sends
        // its score in scored[] and its STRIKES in taken[] rather than growing the packet. That is
        // also why ShooterDone tests taken[] against AccuracyStrikes.
        public bool AccuracyMode;
        AccuracyBoard _board;          // host: the live target board (authoritative hit detection)
        bool _hitThisKick;             // host: did the live attempt pass through the target? (also
                                       // the one-target-per-kick latch - see OnAccuracyScored)
        float _accReturnTimer;         // host: >0 while waiting to start the next round
        const float AccuracyReturnDelay = 0.9f;   // beat between a resolved shot and the next round

        // Host: the round each shooter is on (1-based), which drives their difficulty. Kept
        // separate from scored[]/taken[] so it reads clearly; it is always score + strikes + 1.
        int AccuracyRound(int slot) => _scored[slot] + _taken[slot] + 1;

        // The slot whose END SCREEN is showing, and how long is left on it. Elimination is the only
        // thing that pauses the rotation, so one timer covers it.
        byte _accEliminated = 255;
        const float AccuracyEndScreenHold = 4f;

        // SUDDEN DEATH (host format option). Same three strikes, but the shooters CYCLE one shot at
        // a time in a shuffled order instead of each playing a whole run out, and the match ends the
        // moment one player is left standing rather than when everyone has finished.
        //
        // The all-out case is the reason a cycle is tracked at all: if the last survivors all take
        // their third strike within the same cycle there is no last player standing, so that cycle
        // is VOIDED - the strike it added is given back to everyone who was still alive at the start
        // of it - and the round replays until it separates them.
        bool _accSuddenDeath;
        readonly int[] _accCycleStrikes = new int[NetSession.MaxSlots];  // strikes each had when the cycle began
        readonly System.Collections.Generic.List<int> _accCycleOrder = new System.Collections.Generic.List<int>();
        int _accCycleIdx;             // how many of this cycle's shooters have shot
        uint _accShuffleSeed = 1u;

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
        Transform _goal;   // for the Broadcast/replay camera's GroupCenter framing only
        NetSession _s;
        DefensiveWall _wall;

        readonly Body[] _bodies = new Body[NetSession.MaxSlots];
        int _localSlot;
        bool _localIsKeeper;

        uint _tick; float _snapAccum;
        string _flash = ""; float _flashTime;
        float _goalLineZ;
        Vector3 _ballSpot;          // dead-ball free-kick spot (host-placed, or per-round in Random mode)
        Vector3 _wallCenter;        // wall centre (host-placed, or derived from the ball in Random mode)

        // RANDOM mode: a fixed schedule of 10 outside-box spots, one per round, derived identically on
        // every peer from the synced seed. All shooters in round R use _randomSpots[R]; it changes each
        // round. Null when Random is off (then the single host-placed/default spot is used).
        Vector3[] _randomSpots;

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
        // Shared SAVE / EPIC SAVE / MISS verdict, off the ball's real contact log.
        readonly SaveWatch _save = new SaveWatch();
        QuickChatFeed _qcFeed;   // multiplayer quickchat feed + custom-text entry

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

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball, Transform goal,
                              Material torso, Material limb, Material glove, Transform root)
        {
            _input = input; _cam = gameCam; _ball = ball; _root = root; _goal = goal;
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
            // Random per-round spots: build the identical 10-spot schedule on every peer from the seed.
            if (cfg.fkRandom) _randomSpots = BuildRandomSpots(cfg.fkSeed, ShotsEach);

            // Accuracy: read the host's turn rule and build the target board. The board lives on
            // EVERY peer (so clients see the targets) but only the host's hits count - the host runs
            // the ball physics, and its board is seeded from the config so layouts match.
            if (AccuracyMode)
            {
                _accSuddenDeath = cfg.accSuddenDeath;
                _board = new AccuracyBoard();
                if (_s.IsHost) _board.Scored += OnAccuracyScored;
                // ONE target, and the only host setting is the format: the ladder is the round number.
                _board.Build(transform, 1, cfg.fkSeed | 1u);
                _accShuffleSeed = cfg.fkSeed | 1u;   // synced, so every peer shuffles alike

                // The SAME SHOT for every shooter: maxed shooting/control, and every body-derived
                // baseline evaluated at the default height and weight - so a run measures aim, not
                // somebody's skill tree or their body sliders. Both override a computed result,
                // never the saved profile. Cleared in OnDestroy.
                SkillTree.MaxShootingOverride = true;
                PlayerProfile.UniformBodyOverride = true;
            }
            _ball.SetPieceShot = true;   // arcadey loft + curl + stat-scaled assist
            _s.MatchEvent += OnMatchEvent;
            _s.BallKicked += OnBallKicked;
            _s.PostHit += OnPostHit;
            _s.ShootoutUpdated += OnShootoutUpdated;
            _qcFeed = gameObject.AddComponent<QuickChatFeed>();
            _qcFeed.Bind(_s);

            foreach (var slot in _s.Roster)
                SpawnBody(slot.slot, torso, limb, glove, root);

            // Build the live shooter turn order from the roster (occupied shooter slots).
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].isShooter) _shooterSlots.Add(i);

            var me = _bodies[_localSlot];
            _localIsKeeper = me != null && me.isKeeper;

            // A HUMAN KEEPER in accuracy is handicapped two ways, and both have to wait until the
            // roster says whether the local player is one:
            //   1. He cannot SEE the target - it would tell him exactly where the shot has to go,
            //      which is the shooter's whole problem. Visual only; the trigger stays live and the
            //      host's board is what scores. Shooters and spectators see it normally.
            //   2. He moves, dives and lunges at AccuracyKeeperHandicap of normal, because a human
            //      is otherwise a far better keeper than the AI ladder this mode is balanced around.
            if (AccuracyMode && _localIsKeeper)
            {
                _board?.SetVisualHidden(true);
                ApplyAccuracyKeeperHandicap();
            }
            if (me != null && me.ragdoll != null && me.ragdoll.Pelvis != null)
            {
                // No crosser in this mode (matches single-player Set Pieces/Accuracy), but the real
                // goal now reaches the Broadcast/replay camera's GroupCenter the same way it already
                // does for single-player - this was hardcoding null and framing visibly tighter.
                _cam.Init(cam, ball.transform, me.ragdoll.Pelvis.transform, null, _goal);
                if (_localIsKeeper)
                {
                    // Human keeper: identical to single-player goalkeeper mode. The camera pans in a
                    // cone from a FIXED forward base; the keeper reads that same cone yaw (KeeperLookYaw)
                    // and turns his body to it, so body + camera stay in lock-step.
                    _cam.SetKeeperFollow(me.ragdoll.Pelvis.transform,
                                         () => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up),
                                         () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
                }
                else
                {
                    _cam.SetFollow(me.ragdoll.Pelvis.transform, () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
                    _camTarget = me.ragdoll.Pelvis.transform;   // FollowActiveShooter tracks this
                    if (me.striker != null) me.striker.SetCameraYaw(() => _cam.Yaw, () => _cam.Pitch);
                }
            }

            // Defensive wall: built on EVERY peer from the synced points so clients see + can
            // aim around it (the host owns the ball physics + hop; the client wall is a visual/
            // collision stand-in that just sits there). Host also arms the first turn.
            // ACCURACY NEVER HAS A WALL. Leaving _wall null is what guarantees it: SimConfig
            // .WallCount is a shared mutable static and every Build call site below reads it live,
            // so "it happens to be 0" is a weaker promise than having nothing to build. Every use
            // of _wall in this file is already null-guarded.
            if (!AccuracyMode)
            {
                _wall = new DefensiveWall();
                _wall.Build(root, _ballSpot, _wallCenter, SimConfig.WallCount);   // host-placed centre
            }
            if (_s.IsHost)
            {
                _ball.ResetTo(_ballSpot);
                // Sudden death opens its first shuffled cycle rather than starting on shooter 0,
                // so the running order is randomised from the very first shot.
                if (_shooterSlots.Count == 0) { _activeShooter = 255; _over = true; }
                else if (AccuracyMode && _accSuddenDeath) BeginSuddenDeathCycle();
                else BeginTurn(0);
                BroadcastShootout();
            }

            // Replay recorder over local bodies + ball.
            var tracked = new List<Transform> { _ball.transform };
            var drivers = new List<MonoBehaviour>();
            for (int i = 0; i < _bodies.Length; i++)
                if (_bodies[i] != null) ReplaySystem.TrackBody(tracked, drivers, _bodies[i].ragdoll);
            _replay = gameObject.AddComponent<ReplaySystem>();
            _replay.Setup(tracked, drivers, SimConfig.ReplayWindow);
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
                    else
                    {
                        b.netInput = new NetInputSource(); striker.Init(b.netInput, ragdoll);
                        // Remote striker aim off the wire, same as the set-piece taker cone.
                        striker.SetCameraYaw(() => b.netInput != null ? b.netInput.LookYaw : 0f,
                                             () => b.netInput != null ? b.netInput.LookPitch : 0f);
                    }
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
                if (isLocal) kc.Init(_input, ragdoll, _ball);
                else { b.netInput = new NetInputSource(); kc.Init(b.netInput, ragdoll, _ball); }
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

        // Deterministic 10-spot schedule for Random mode: seeded System.Random so host + every client
        // derive the identical spots. Each spot is inside the attacking third, OUTSIDE the penalty box,
        // and within a sensible width so a wall + shot are always playable.
        static Vector3[] BuildRandomSpots(uint seed, int count)
        {
            var rng = new System.Random(unchecked((int)seed));
            var spots = new Vector3[count];
            for (int i = 0; i < count; i++)
                spots[i] = SetPieceMap.RandomSpot(rng);   // same generator single player's random spots use
            return spots;
        }

        // Apply the spot for the given round (0-based) in Random mode: set _ballSpot + a regulation
        // wall centre on the ball->goal line. No-op when Random is off.
        void ApplyRoundSpot(int round)
        {
            if (_randomSpots == null || _randomSpots.Length == 0) return;
            _ballSpot = _randomSpots[Mathf.Clamp(round, 0, _randomSpots.Length - 1)];
            Vector3 toGoal = SimConfig.GoalCenter - _ballSpot; toGoal.y = 0f;
            Vector3 dir = toGoal.sqrMagnitude > 1e-4f ? toGoal.normalized : Vector3.forward;
            _wallCenter = _ballSpot + dir * SimConfig.WallDistance;
        }

        void AttachKick(ActiveRagdoll ragdoll, Striker striker)
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
            if (tag == "WHISTLE") { AudioManager.Instance?.PlayWhistle(); return; }   // ref call, no HUD splash
            if (tag == "MISS") { AudioManager.Instance?.PlayMissBoosMaybe(); return; }
            // "ACCOUT:<slot>:<score>" puts up a knocked-out shooter's end screen (see
            // BroadcastAccuracyOut). It carries data, not a message, so it never goes to Flash.
            if (tag.StartsWith("ACCOUT:"))
            {
                var bits = tag.Split(':');
                if (bits.Length == 3 && int.TryParse(bits[1], out int slot) && int.TryParse(bits[2], out int sc))
                    ShowAccuracyOut(slot, sc);
                return;
            }
            Flash(tag);
        }
        // Client: 3D kick thud at the host-reported contact point (10 m rolloff, per-player).
        void OnBallKicked(Vector3 pos) => AudioManager.Instance?.PlayBallKick(pos);
        void OnPostHit(Vector3 pos, float speed) => AudioManager.Instance?.PlayPostHit(pos, speed);
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

                // Random mode: mirror the host's per-round spot so the client's wall + local
                // prediction line up with the authoritative ball. Round = the active shooter's taken.
                if (_randomSpots != null && s.activeShooter != 255 && s.taken != null
                    && s.activeShooter < s.taken.Length)
                {
                    ApplyRoundSpot(s.taken[s.activeShooter]);
                    if (_wall != null) _wall.Build(_root, _ballSpot, _wallCenter, SimConfig.WallCount);
                }
            }
            // Crowd streak stingers, driven off the replicated tally so they fire identically on
            // the host AND every client (this handler runs on both). Per shooter: a resolved
            // attempt is a +1 step in taken[]; +1 in scored[] too means a goal, else a miss. We
            // require a taken delta of exactly 1 so a departed shooter (OnRosterChanged jumps their
            // taken to ShotsEach) never registers as a phantom miss.
            if (s.taken != null && s.scored != null)
            {
                for (int i = 0; i < s.taken.Length && i < NetSession.MaxSlots; i++)
                {
                    int dt = s.taken[i]  - _prevTaken[i];
                    int dg = s.scored[i] - _prevScored[i];
                    if (dt == 1)
                    {
                        if (dg >= 1) AudioManager.Instance?.OnSetPieceGoal(i);
                        else         AudioManager.Instance?.OnSetPieceMiss(i);
                    }
                    _prevTaken[i]  = s.taken[i];
                    _prevScored[i] = s.scored[i];
                }
            }

            // Match-end applause (host + clients), once, on the shootout going over.
            if (s.over && !_over) AudioManager.Instance?.PlayApplauseOnly();

            _activeShooter = s.activeShooter;
            _over = s.over;
        }
        int _lastLocalTaken;
        readonly int[] _prevTaken  = new int[NetSession.MaxSlots];
        readonly int[] _prevScored = new int[NetSession.MaxSlots];
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
                    // Mark the shooter finished so the rotation skips them. Both modes read taken[]
                    // (shots in set pieces, strikes in accuracy), so one write covers both; clearing
                    // the body below is what actually makes it stick (see ShooterDone).
                    _taken[i] = Mathf.Max(ShotsEach, SimConfig.AccuracyStrikes);
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

        // A human keeper goes back to his spot on the line for the next kick exactly as the AI
        // does (Goalkeeper.ResetTo): recover whatever dive/hold he was in, unlock his input and
        // teleport the body. Only the AI used to be reset; a human stayed wherever the last dive
        // left him.
        void ResetHumanKeepers()
        {
            foreach (var b in _bodies)
            {
                if (b?.keeper == null || b.ragdoll == null) continue;
                b.keeper.InputLocked = false;
                b.keeper.ForceRecover();
                b.ragdoll.ResetTo(SimConfig.KeeperStart, Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up));
            }
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
            _save.Disarm();

            // Random mode: this shooter's round index = how many they've already taken (0..9). Move
            // the ball spot + wall to that round's shared spot before placing bodies/ball/wall.
            if (_randomSpots != null)
            {
                ApplyRoundSpot(_taken[_activeShooter]);
                if (_wall != null) _wall.Build(_root, _ballSpot, _wallCenter, SimConfig.WallCount);
            }

            // Accuracy: pick this round's spot and difficulty FIRST, so the body placement below
            // uses the real spot. (Doing it after would place everyone against the previous round's
            // spot and rely on the re-arm to move the shooter back.)
            if (AccuracyMode) SetUpAccuracyRound();

            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                var b = _bodies[i];
                if (b == null || b.striker == null) continue;
                bool active = i == _activeShooter;
                b.striker.ControlEnabled = active;
                b.ragdoll.ResetTo(active ? _ballSpot + new Vector3(0f, 0f, -3f) : ShooterWaitSpot(i),
                                  Quaternion.identity);
                b.striker.ForceRecover();
                // Restore ball<->body collision for the PARKED shooters (clearing any ignore left by
                // the previous turn's taker/auto launch), but keep the ACTIVE shooter ignored: the
                // taker owns the ball for the whole attempt and arms on a later frame, so leaving the
                // active body collidable here opens a window where its foot can physically graze the
                // dead ball - and because SetPieceShot skips the swing gate that fires a full-power
                // contact-point strike into a corner, ignoring the player's aim entirely.
                _ball.IgnoreBody(b.ragdoll, active);
            }
            if (_wall != null) _wall.Ground();
            foreach (var b in _bodies) if (b?.ai != null) b.ai.ResetTo(SimConfig.KeeperStart);
            ResetHumanKeepers();
            _ball.ResetTo(_ballSpot);
            // Re-arm the taker for the new active shooter next HostUpdate; reset the AI auto-kick +
            // the idle safety timer so a fresh shooter always gets a clean attempt.
            _takerArmed = false;
            _taker.Reset();
            _aiKickDelay = Random.Range(0.6f, 1.4f);
            _armedElapsed = 0f;

            // Whistle as the shooter is set behind the ball (first turn + each new turn). Host plays
            // locally and broadcasts so every client hears the same ref call.
            AudioManager.Instance?.PlayWhistle();
            _s.BroadcastEvent("WHISTLE");
        }

        // The between-rounds beat expired. Who shoots next depends on the format: the STRIKES game
        // keeps the same shooter until they are out, while SUDDEN DEATH always hands on after one
        // shot. Either way an elimination clears the end screen first.
        void AccuracyBeatOver()
        {
            bool wasEliminated = _accEliminated != 255;
            _accEliminated = 255;
            _accReturnTimer = 0f;

            if (!_accSuddenDeath)
            {
                // Strikes: the same shooter plays on until their third strike ends their run.
                if (wasEliminated) AdvanceTurn();
                else BeginAccuracyRound();
                return;
            }

            // Sudden death: one shot per visit, so the turn always moves on.
            _accCycleIdx++;
            AdvanceTurn();
        }

        // End of a full cycle through everyone who was alive when it started. If that cycle wiped
        // out ALL of them, nobody can be declared last standing, so the cycle is undone and replayed;
        // otherwise a fresh cycle begins over the survivors.
        void CloseSuddenDeathCycle()
        {
            bool anyAlive = false;
            for (int i = 0; i < _accCycleOrder.Count; i++)
                if (!ShooterDone(_accCycleOrder[i])) { anyAlive = true; break; }

            // The replay only makes sense when a cycle wiped out MORE THAN ONE player at once: that
            // is the case with no last-one-standing to declare. A lone shooter finishing their run
            // is simply the end of the match.
            if (!anyAlive && _accCycleOrder.Count > 1)
            {
                // Everyone still in went out together: give back the strike this cycle added to
                // each of them and replay it, so the round decides a winner rather than a draw.
                for (int i = 0; i < _accCycleOrder.Count; i++)
                {
                    int slot = _accCycleOrder[i];
                    _taken[slot] = _accCycleStrikes[slot];
                }
                Announce("ALL OUT - ROUND REPLAYED");
                BroadcastShootout();
            }

            BeginSuddenDeathCycle();
        }

        // Snapshot who is alive, shuffle them, and start the cycle on the first of them. Ends the
        // match here when only one player is left - that player is the winner.
        void BeginSuddenDeathCycle()
        {
            _accCycleOrder.Clear();
            for (int i = 0; i < _shooterSlots.Count; i++)
                if (!ShooterDone(_shooterSlots[i])) _accCycleOrder.Add(_shooterSlots[i]);

            // Last player standing: the match is over. "Standing" needs somebody to have been
            // knocked DOWN, though - with a lone shooter in the lobby there is nobody to outlast,
            // so that is played as an ordinary strikes run and ends when their run does, rather
            // than declaring them the survivor before they have taken a shot.
            bool soloLobby = _shooterSlots.Count <= 1;
            if (_accCycleOrder.Count == 0 || (_accCycleOrder.Count == 1 && !soloLobby))
            {
                EndShootout();
                return;
            }

            // Fisher-Yates on the synced LCG, so the order is the same on every peer and changes
            // between cycles rather than repeating the first one forever.
            for (int i = _accCycleOrder.Count - 1; i > 0; i--)
            {
                int j = Mathf.Min(i, (int)(NextShuffle() * (i + 1)));
                int tmp = _accCycleOrder[i]; _accCycleOrder[i] = _accCycleOrder[j]; _accCycleOrder[j] = tmp;
            }

            for (int i = 0; i < _accCycleOrder.Count; i++)
                _accCycleStrikes[_accCycleOrder[i]] = _taken[_accCycleOrder[i]];

            _accCycleIdx = 0;
            BeginTurnForSlot(_accCycleOrder[0]);
        }

        float NextShuffle()
        {
            _accShuffleSeed = _accShuffleSeed * 1664525u + 1013904223u;
            return (_accShuffleSeed >> 8) / 16777216f;
        }

        // Sudden death drives the order itself, so it needs to begin a turn by SLOT rather than by
        // index into _shooterSlots.
        void BeginTurnForSlot(int slot)
        {
            int idx = _shooterSlots.IndexOf(slot);
            if (idx < 0) { EndShootout(); return; }
            BeginTurn(idx);
            BroadcastShootout();
        }

        // Advance to the next live shooter that still has attempts left; end the match if none.
        void AdvanceTurn()
        {
            // Sudden death runs its own shuffled cycle rather than the round-robin below.
            if (AccuracyMode && _accSuddenDeath)
            {
                while (_accCycleIdx < _accCycleOrder.Count)
                {
                    int slot = _accCycleOrder[_accCycleIdx];
                    // Skip anyone knocked out earlier in this same cycle.
                    if (!ShooterDone(slot)) { BeginTurnForSlot(slot); return; }
                    _accCycleIdx++;
                }
                CloseSuddenDeathCycle();
                return;
            }

            for (int step = 1; step <= _shooterSlots.Count; step++)
            {
                int idx = (_turnIdx + step) % _shooterSlots.Count;
                if (!ShooterDone(_shooterSlots[idx])) { BeginTurn(idx); BroadcastShootout(); return; }
            }
            EndShootout();
        }

        // Everyone is finished. Restore the last shooter's ball collision + reset the taker (no more
        // BeginTurn will run to do it), so nothing leaks if the scene is reused.
        void EndShootout()
        {
            _takerArmed = false;
            _taker.Reset();
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].ragdoll != null) _ball.IgnoreBody(_bodies[i].ragdoll, false);
            _activeShooter = 255; _over = true;
            _phase = Phase.Settle; _settle = float.PositiveInfinity;
            BroadcastShootout();
        }

        // Has this shooter used up their turn? Set pieces: a fixed shot count. Accuracy: either a
        // fixed kick count, or - when the turn is TIMED - a turn is only "done" once it has been
        // played (the clock expiring is what ends it, handled in HostUpdate), so a timed shooter is
        // done when they have taken at least one kick and their clock has run out.
        bool ShooterDone(int slot)
        {
            // A slot with no body can never take a turn, so it is done by definition. This test has to
            // come FIRST, and it is the fix for a real stall: the leave handler marked a departed
            // shooter finished by writing _taken = ShotsEach (10), but in Accuracy "done" is measured
            // against _accKicks - which the host can set as high as 100 - or against _turnPlayed for a
            // timed turn, neither of which that write satisfies. So AdvanceTurn kept handing the ball
            // to a player who had already quit: HostDriveActiveShooter bails on the null body, nothing
            // moves, and the match sat there until the turn clock ran out (or forever, in kicks mode).
            var b = (slot >= 0 && slot < _bodies.Length) ? _bodies[slot] : null;
            if (b == null || !b.isShooter) return true;
            // Accuracy: a shooter is out for good on their third strike (strikes ride in taken[]).
            if (AccuracyMode) return _taken[slot] >= SimConfig.AccuracyStrikes;
            return _taken[slot] >= ShotsEach;
        }

        // A target was struck during the active shooter's live attempt: bank the points to that
        // shooter and flash it to every peer. Host only (its board is authoritative); the tally
        // itself is published later, by ResolveAttempt's broadcast (see the comment below).
        void OnAccuracyScored(int points, int index)
        {
            if (_activeShooter >= NetSession.MaxSlots) return;
            // ONE target per kick: ignore further triggers once this attempt has hit, so a ball
            // rolling around the goal mouth can't re-trigger off a single shot. This only LATCHES -
            // the round is graded in ResolveAttempt, because hitting the target scores nothing
            // without the goal to go with it.
            if (_hitThisKick) return;
            _hitThisKick = true;
            // No BroadcastShootout here: taken[] hasn't moved yet, and broadcasting scored[] alone
            // desyncs OnShootoutUpdated's per-attempt delta (it'd see dt=0 now, then dt=1/dg=0 at
            // ResolveAttempt's own broadcast - a made shot reads as a miss for the audio/streak
            // logic, and on a client - which never runs ResolveAttempt - that delta IS the only
            // goal/miss signal, so it never hears the cheer at all). ResolveAttempt's broadcast
            // already carries this bumped _scored value alongside the taken[] increment.
            Announce("+" + points);
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
            // The replay takes the camera over, so forget who we were orbiting: FollowActiveShooter
            // must re-apply SetFollow (not skip it as "unchanged") once the replay hands the camera back.
            _camTarget = null;
            _replay.Play(SimConfig.ReplaySlowMul);
            Flash("REPLAY  (click to skip)");
            // Replays off locally while the host rolls one: cast our skip vote straight away (the
            // replay ends once every human has voted), so an all-off lobby never sits through it.
            if (!GameplaySettings.Replays) _s.VoteSkip();
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
            if (_s == null || PauseMenu.Frozen) return;   // Frozen: an overlay pause never stops the sim

            // Quickchat (multiplayer): Tab types a custom message; while typing, gameplay is
            // suspended. Number keys 1-6 send a preset.
            if (_qcFeed != null)
            {
                if (_input.QuickChatTextPressed) _qcFeed.ToggleTextEntry();
                // Keep the spectating camera on the right shooter even while typing (a turn can
                // change mid-message), then suspend the rest of gameplay input.
                if (_qcFeed.Typing) { FollowActiveShooter(); return; }
                int qd = _input.QuickChatDigitPressed();
                if (qd > 0) _qcFeed.SendPreset(qd);
            }

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

            FollowActiveShooter();

            if (_flashTime > 0f) _flashTime -= Time.unscaledDeltaTime;
        }

        // A waiting SHOOTER watches the action from the ACTIVE shooter's camera instead of staring
        // at their own parked body off to the side. It is the same third-person follow the taker
        // themselves gets, so everyone sees the kick from the same view; the HUD (scoreboard,
        // status, quickchat, flashes) is untouched and keeps drawing locally as normal.
        //
        // The keeper is never retargeted - they are live every kick and need their own keeper-cone
        // view. Skipped while replaying (the replay owns the camera in Broadcast mode) and once the
        // shootout is over. Re-evaluated each frame but only applied on a CHANGE, so it self-corrects
        // for host-side BeginTurn and client-side ShootoutState alike without fighting the camera.
        void FollowActiveShooter()
        {
            if (_localIsKeeper || _replaying || _over) return;

            int watch = (_activeShooter != 255 && _activeShooter < _bodies.Length) ? _activeShooter : _localSlot;
            var body = (watch >= 0 && watch < _bodies.Length) ? _bodies[watch] : null;
            // Fall back to our own body if the active shooter has no spawned body on this peer.
            if (body == null || body.ragdoll == null || body.ragdoll.Pelvis == null)
            {
                body = (_localSlot >= 0 && _localSlot < _bodies.Length) ? _bodies[_localSlot] : null;
                if (body == null || body.ragdoll == null || body.ragdoll.Pelvis == null) return;
            }

            var target = body.ragdoll.Pelvis.transform;
            if (target == _camTarget) return;   // already watching them
            _camTarget = target;
            _cam.SetFollow(target, () => _input.Look, () => _input.Scroll, () => _input.CamViewPressed);
        }

        Transform _camTarget;   // whose pelvis the camera is currently orbiting

        bool LocalIsActiveShooter() => _localSlot == _activeShooter;

        void HostUpdate()
        {
            // Post-shot hold: freeze gameplay, keep physics + recorder + snapshots running.
            if (_goalHold > 0f)
            {
                _goalHold -= Time.deltaTime;
                // A human keeper keeps ticking through the hold with his input locked, so a dive he
                // was mid-way through lands and he stands up on his own, instead of freezing in the
                // lay-out with the last MoveInput still sliding him across the box.
                for (int i = 0; i < _bodies.Length; i++)
                    if (_bodies[i]?.keeper != null) { _bodies[i].keeper.InputLocked = true; _bodies[i].keeper.Tick(); }
                if (_goalHold <= 0f)
                {
                    // Replays off on the host (Settings > Gameplay): no replay for anyone, the turn
                    // just advances. On: the usual host-driven replay, AdvanceTurn when it ends.
                    if (GameplaySettings.Replays) _s.BeginReplay();
                    else if (_advanceAfterReplay) { _advanceAfterReplay = false; AdvanceTurn(); }
                }
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
                if (remote && b.netInput != null) b.netInput.Feed(_s.ConsumeInputForSlot(i));
                if (b.ai != null) b.ai.Tick();
                if (b.keeper != null) b.keeper.Tick();
            }

            // Authoritative taker: drive the active shooter's aesthetic runup + scripted launch
            // from that shooter's input (local device if the host holds the slot, else the slot's
            // networked input). An AI-held active shooter auto-fires after a short delay.
            HostDriveActiveShooter();

            if (_wall != null) _wall.Tick();

            if (!_over) HostTickAttempt();

            // Accuracy: run the beat between rounds. ONE timer covers both cases - it is simply
            // set longer when the shot that just resolved knocked a player out, because that wait
            // is their END SCREEN. What happens when it expires is decided by who is still in.
            if (AccuracyMode && !_over && _accReturnTimer > 0f)
            {
                _accReturnTimer -= Time.deltaTime;
                if (_accReturnTimer <= 0f) AccuracyBeatOver();
            }

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
                    // Aim ALWAYS comes from a look ray, exactly as in single-player free kicks:
                    //  - a REMOTE shooter's ray is rebuilt from its networked look yaw/pitch;
                    //  - the HOST-LOCAL shooter uses its own camera.
                    // Previously the host-local case passed null, which fell back to
                    // BallController's built-in CORNER auto-aim - so the host's own set pieces
                    // ignored where they were looking and flew to a corner regardless.
                    var nsrc = src as NetInputSource;
                    System.Func<Vector3> aim = nsrc != null
                        ? () => SetPieceTaker.LookAimPoint(_ballSpot, nsrc.LookYaw, nsrc.LookPitch, SimConfig.AttackGoalCenter.z)
                        : () => SetPieceTaker.LookAimPoint(_ballSpot, _cam.Yaw, _cam.Pitch, SimConfig.AttackGoalCenter.z);
                    // A remote shooter's footedness now comes off the wire (it used to fall back to
                    // the HOST'S foot, so every remote free kick was animated on the wrong leg for
                    // a left-footer). The local shooter keeps the -1 = own-profile default.
                    int footed = (_activeShooter == _localSlot) ? -1 : (_s.LeftFootedForSlot(_activeShooter) ? 1 : 0);
                    _taker.Begin(src, b.ragdoll, _ball, _ballSpot, SimConfig.AttackGoalCenter,
                                 displayOnly: false, combinedOverride: combined, aimPoint: aim,
                                 leftFootedOverride: footed);
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
                             displayOnly: true, combinedOverride: -1f,
                             aimPoint: () => SetPieceTaker.LookAimPoint(_ballSpot, _cam.Yaw, _cam.Pitch, SimConfig.AttackGoalCenter.z));
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
                        _phase = Phase.Live; _liveTime = _restTimer = 0f;
                        _save.Arm();
                        _hitThisKick = false;   // fresh attempt: re-arm the one-target-per-kick latch
                        if (_wall != null) _wall.TriggerJump();
                    }
                    break;

                case Phase.Live:
                    _liveTime += Time.deltaTime;
                    // Keeper contact, from the ball's touch log: real PhysX contacts with the impact
                    // speed recorded at the contact, so a fast shot cannot slip between two frames of a
                    // proximity check and EPIC reads the arrival speed rather than the post-touch one.
                    _save.Poll(_ball, KeeperRagdoll(), KeeperHighDive());
                    // Accuracy scores TARGETS (banked by OnAccuracyScored as they're struck), so a
                    // ball entering the goal isn't itself a score - the attempt just runs to rest.
                    if (!AccuracyMode && BallInGoal(c)) { ResolveAttempt(true); break; }
                    if (_ball.Speed < RestSpeed) _restTimer += Time.deltaTime; else _restTimer = 0f;
                    bool out_ = c.y < -3f || Mathf.Abs(c.x) > SimConfig.FieldWidth || Mathf.Abs(c.z) > SimConfig.FieldLength;
                    if (out_ || _restTimer > RestHold || _liveTime > MaxLiveTime)
                        ResolveAttempt(AccuracyMode && _hitThisKick);
                    break;

                case Phase.Settle:
                    break;
            }
        }

        // Score the attempt, broadcast the tally, and roll the post-shot hold; the turn advances
        // when the hold/replay ends (OnReplayEnded).
        void ResolveAttempt(bool goal)
        {
            // ---- ACCURACY: grade the round. Clearing it needs BOTH halves - in the goal and
            // through the target - and every other outcome is a strike, a scored goal that missed
            // the disc included. No replay between rounds; the strike tally IS the verdict.
            //
            // The shared taken[]++ below is deliberately NOT run first here: in this mode taken[]
            // counts STRIKES, so a cleared round must not touch it.
            if (AccuracyMode)
            {
                // TERSE callouts, matching single player: the outcome and nothing else. The round
                // number and the strike pips are both already on the accuracy board, which is up
                // for the whole match - so spelling them out again in a 1.6 s pill only made the
                // line too long to read before it faded.
                if (goal)
                {
                    _scored[_activeShooter]++;
                    Announce("GOAL");
                    AudioManager.Instance?.OnSetPieceGoal(_activeShooter);
                }
                else
                {
                    _taken[_activeShooter]++;
                    Announce("STRIKE " + _taken[_activeShooter]);
                    AudioManager.Instance?.OnSetPieceMiss(_activeShooter);
                }
                _board?.HideAll();
                BroadcastShootout();

                // Third strike: hold on this shooter's end screen before the turn moves on.
                bool out_ = _taken[_activeShooter] >= SimConfig.AccuracyStrikes;
                if (out_)
                {
                    _accEliminated = (byte)_activeShooter;
                    BroadcastAccuracyOut(_activeShooter, _scored[_activeShooter]);
                }
                _phase = Phase.Settle;
                _accReturnTimer = out_ ? AccuracyEndScreenHold : AccuracyReturnDelay;
                return;
            }

            _taken[_activeShooter]++;

            if (goal) { _scored[_activeShooter]++; Announce("GOAL!"); }
            else if (_save.Touched) Announce(_save.Callout());
            BroadcastShootout();
            _phase = Phase.Settle;
            _advanceAfterReplay = true;
            _goalHold = SimConfig.ReplayHold;   // brief live hold, then replay, then AdvanceTurn
        }

        // Accuracy: choose the active shooter's round - a fresh random spot in the band, a target
        // sized and paced for their tier, and a keeper set to match. Difficulty comes from THEIR own
        // round number, so turn order never changes how hard a round is. Placing the bodies against
        // the new spot is the caller's job.
        void SetUpAccuracyRound()
        {
            _hitThisKick = false;
            _accReturnTimer = 0f;
            if (_activeShooter >= NetSession.MaxSlots) return;

            int round = AccuracyRound(_activeShooter);
            SimConfig.KeeperAbility = SimConfig.AccuracyKeeperAbility(round);
            // The spot is host-authoritative and rides the snapshot like any other body position,
            // so clients need no seed of their own for it.
            _ballSpot = RandomAccuracySpot();
            _board?.SpawnPatrol(SimConfig.AccuracyTargetRadius(round), SimConfig.AccuracyTargetSpeed(round));
        }

        // Accuracy: start the SAME shooter's next round mid-turn (the strikes format, where a run
        // continues until the third strike). BeginTurn does its own placement, so this is the only
        // path that has to move the bodies itself.
        void BeginAccuracyRound()
        {
            SetUpAccuracyRound();
            ReArmAccuracyKick();
        }

        // A spot inside the D - the same band single player uses (see AccuracyGame.RandomSpot).
        // The far edge is an ARC, so x comes first and the depth is drawn against that column's own
        // reach (SimConfig.AccuracySpotFarAt).
        Vector3 RandomAccuracySpot()
        {
            float x = Random.Range(-SimConfig.AccuracySpotHalfW, SimConfig.AccuracySpotHalfW);
            float dist = Random.Range(SimConfig.AccuracySpotNear, SimConfig.AccuracySpotFarAt(x));
            return new Vector3(x, SimConfig.BallRadius, SimConfig.GoalCenter.z - dist);
        }

        // Put the ball + shooter on the current _ballSpot and re-arm the taker.
        void ReArmAccuracyKick()
        {
            var b = _activeShooter < NetSession.MaxSlots ? _bodies[_activeShooter] : null;
            if (b != null && b.ragdoll != null)
            {
                // Keep the ACTIVE shooter's body ignored: the taker re-arms on a later frame and owns
                // the ball for the whole attempt, so restoring collision here would let its foot graze
                // the dead ball and fire a camera-blind contact-point strike (see BeginTurn).
                _ball.IgnoreBody(b.ragdoll, true);
                b.ragdoll.ResetTo(_ballSpot + new Vector3(0f, 0f, -3f), Quaternion.identity);
                b.striker?.ForceRecover();
            }
            if (_wall != null) _wall.Ground();
            foreach (var kb in _bodies) if (kb?.ai != null) kb.ai.ResetTo(SimConfig.KeeperStart);
            ResetHumanKeepers();
            _ball.ResetTo(_ballSpot);
            _takerArmed = false;
            _taker.Reset();
            _aiKickDelay = Random.Range(0.6f, 1.4f);
            _armedElapsed = 0f;
            _hitThisKick = false;
            _accReturnTimer = 0f;   // the return has happened; don't let the timer fire again
            _phase = Phase.Armed;
            _liveTime = _restTimer = _settle = 0f;
            AudioManager.Instance?.PlayWhistle();
            _s.BroadcastEvent("WHISTLE");
        }


        void PublishSnapshotIfDue()
        {
            _snapAccum += Time.deltaTime;
            if (_snapAccum >= SimConfig.NetSnapshotInterval)
            {
                _snapAccum = 0f;
                // A local keeper sends its cone yaw (KeeperLookYaw); everyone else sends camera yaw.
                float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
                _s.SetLocalInput(_input.SampleFrame(_tick, wireYaw, _cam.Pitch));
                BroadcastSnapshot();
                _tick++;
            }
        }

        void ClientUpdate()
        {
            // Accuracy: clients pop/animate their own copy of the target board so the goal looks
            // right locally. Hit detection is HOST-authoritative (it owns the ball physics); a
            // client board only mirrors the visuals, and its own trigger hits score nothing because
            // the Scored handler is only subscribed on the host.
            if (AccuracyMode) _board?.Tick(Time.deltaTime);

            // A local keeper sends its cone yaw (KeeperLookYaw); everyone else sends camera yaw.
            float wireYaw = _localIsKeeper ? _cam.KeeperLookYaw : _cam.Yaw;
            _s.SetLocalInput(_input.SampleFrame(_tick++, wireYaw, _cam.Pitch));

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
                // Adult mode: the puppet's appendage follows the host's flag (AnatomySim eases it).
                if (body.ragdoll.Anatomy != null) body.ragdoll.Anatomy.Erect = sb.erect;
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
            if (b.striker != null && (b.striker.IsDiving || b.striker.IsTumbling)) return AnimState.Down;   // prone
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
                                         lastInputTick = _s.InputTickForSlot(i),
                                         erect = b.ragdoll.Anatomy != null && b.ragdoll.Anatomy.Erect });
            }
            _s.BroadcastSnapshot(new Snapshot
            {
                tick = _tick, ballPos = _ball.transform.position, ballVel = _ball.Rb.linearVelocity,
                // Populated here too even though only the match driver draws a landing telegraph
                // today. It is a field on the shared Snapshot, and a wire field whose meaning depends on
                // which driver sent it is a trap: these are the SHOT modes, so leaving it false would
                // have made "not guided" mean "no assist" in one driver and "nobody filled this in" in
                // the others, on nearly every ball.
                guided = _ball.Guided,
                homeScore = 0, awayScore = 0, bodies = list.ToArray(),
            });
        }

        bool BallInGoal(Vector3 c)
        {
            float r = SimConfig.BallRadius, halfW = SimConfig.GoalWidth * 0.5f;
            return c.z - r >= _goalLineZ && c.z <= _goalLineZ + SimConfig.GoalDepth
                   && Mathf.Abs(c.x) <= halfW - r && c.y >= r && c.y <= SimConfig.GoalHeight - r;
        }

        // Accuracy: slow the LOCAL human keeper's every movement. Set on the local peer only -
        // it scales that client's own controller, and the host re-simulates from the input it
        // sends, so a slower body produces slower input and no second write is needed.
        void ApplyAccuracyKeeperHandicap()
        {
            SimConfig.HumanKeeperSpeedMul = SimConfig.AccuracyKeeperHandicap;
        }

        // Slot 0 is the keeper: its ragdoll, and whether it is mid big-reach right now (human
        // keeper or AI Clanker - both get EPIC SAVE, the callout is about the stop, not who made it).
        ActiveRagdoll KeeperRagdoll() => _bodies[0] != null ? _bodies[0].ragdoll : null;
        bool KeeperHighDive()
        {
            var kb = _bodies[0];
            if (kb == null) return false;
            if (kb.keeper != null) return kb.keeper.IsHighDive;
            return kb.ai != null && kb.ai.WasDivingSave;
        }

        // Broadcast a callout AND flash it locally: BroadcastEvent only fires MatchEvent on clients,
        // so without the local flash the host is the one player who never sees its own verdict.
        void Announce(string tag) { _s.BroadcastEvent(tag); Flash(tag); }

        // A shooter is out. Every peer shows the same end screen for AccuracyEndScreenHold, so the
        // player who just went out sees their own final score and everyone else sees whose run
        // ended. The slot + score ride in the event tag - the shootout tally that follows carries
        // the same numbers, but this is what tells a client to put the CARD up at all.
        void BroadcastAccuracyOut(int slot, int score)
        {
            string tag = "ACCOUT:" + slot + ":" + score;
            _s.BroadcastEvent(tag);
            ShowAccuracyOut(slot, score);
        }

        // Local end-screen state (host and clients both, via the event above).
        int _accOutSlot = -1, _accOutScore;
        float _accOutUntil;

        void ShowAccuracyOut(int slot, int score)
        {
            _accOutSlot = slot;
            _accOutScore = score;
            _accOutUntil = Time.unscaledTime + AccuracyEndScreenHold;
        }

        static void LockCursor() => GameInput.CaptureCursor(true);

        void OnDestroy()
        {
            if (_s != null)
            {
                _s.MatchEvent -= OnMatchEvent;
                _s.BallKicked -= OnBallKicked; _s.PostHit -= OnPostHit;
                _s.ShootoutUpdated -= OnShootoutUpdated;
                _s.ReplayStarted -= OnReplayStarted;
                _s.ReplayEnded -= OnReplayEnded;
                _s.JerseyUpdated -= OnJerseyUpdated;
                _s.RosterChanged -= OnRosterChanged;
            }
            if (_ball != null) { _ball.SetPieceShot = false; if (_ball.Rb != null) _ball.Rb.isKinematic = false; }
            if (_board != null) { _board.Scored -= OnAccuracyScored; _board.Teardown(); _board = null; }
            // These are GLOBAL statics this mode borrowed, so they have to go back or the next mode
            // inherits maxed shooting, a uniform body and a slowed keeper.
            SkillTree.MaxShootingOverride = false;
            PlayerProfile.UniformBodyOverride = false;
            SimConfig.HumanKeeperSpeedMul = SimConfig.HumanKeeperSpeedBase;
        }

        /// <summary>
        /// The winning slot from the synced tally, or -1 when nobody can be crowned (a tie, or a
        /// board with nothing on it). Split out of WinnerText so the accuracy results card and the
        /// one-line banner cannot disagree about who won - they now ask the same question once.
        /// </summary>
        int WinnerSlot()
        {
            var st = _s.LatestShootout;
            if (st.scored == null) return -1;

            // SUDDEN DEATH is won by SURVIVING, not by scoring: the winner is whoever still has a
            // strike left. (A cycle that eliminates everyone at once is replayed rather than ending
            // the match - see CloseSuddenDeathCycle - so there is normally exactly one.)
            if (AccuracyMode && _accSuddenDeath)
            {
                int alive = 0, aliveSlot = -1;
                for (int i = 1; i < NetSession.CrosserSlot; i++)
                {
                    if (_bodies[i] == null || !_bodies[i].isShooter) continue;
                    int tk = i < st.taken.Length ? st.taken[i] : 0;
                    if (tk < SimConfig.AccuracyStrikes) { alive++; aliveSlot = i; }
                }
                if (alive == 1) return aliveSlot;
                // No survivor at all only happens if the match was ended some other way (everyone
                // left, say); fall through to the highest score on the board rather than claiming
                // a winner. (Nothing in this mode tracks a high score - see DrawAccuracyBoard.)
            }

            int best = -1;
            for (int i = 0; i < st.scored.Length; i++) best = Mathf.Max(best, st.scored[i]);
            int winners = 0, winSlot = -1;
            for (int i = 0; i < st.scored.Length; i++)
            {
                // "Played at all" is what taken[] proved in set pieces, where it counts SHOTS. In
                // accuracy it counts STRIKES, and a player can finish a run without ever taking one
                // - so there it has to be the roster that says who was playing.
                bool played = AccuracyMode
                            ? (i > 0 && i < NetSession.CrosserSlot && _bodies[i] != null && _bodies[i].isShooter)
                            : st.taken[i] > 0;
                if (played && st.scored[i] == best) { winners++; winSlot = i; }
            }
            return winners == 1 ? winSlot : -1;
        }

        /// <summary>Best score on the board, or 0 for an empty one.</summary>
        int BestScore()
        {
            var st = _s.LatestShootout;
            if (st.scored == null) return 0;
            int best = 0;
            for (int i = 0; i < st.scored.Length; i++) best = Mathf.Max(best, st.scored[i]);
            return best;
        }

        // Winner text from the synced tally (works on host + client via _s.LatestShootout).
        string WinnerText()
        {
            var st = _s.LatestShootout;
            if (st.scored == null) return "FULL TIME";

            int winSlot = WinnerSlot();
            int best = BestScore();
            if (winSlot < 0) return "TIE  (" + best + ")";

            // Sudden death is survived, not won on points - and the survivor's own score is the
            // number worth printing beside it.
            if (AccuracyMode && _accSuddenDeath)
                return RosterName(winSlot) + " SURVIVES  (" +
                       (winSlot < st.scored.Length ? st.scored[winSlot] : 0) + ")";
            // Accuracy is scored in ROUNDS CLEARED (no per-shot denominator).
            if (AccuracyMode) return RosterName(winSlot) + " WINS  (" + best + ")";
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
            string modeName = AccuracyMode ? "ACCURACY" : "SET PIECES";
            var p = Hud.PanelStart(_s.IsHost ? modeName + " (HOST)" : modeName, 2);
            Hud.Stat(ref p, "You are", youAre);
            var st = _s.LatestShootout;
            bool over = st.scored != null && st.over;
            Hud.Stat(ref p, "Status", over ? "FULL TIME" :
                     _activeShooter == 255 ? "..." :
                     (_localSlot == _activeShooter ? "YOUR SHOT" : RosterName(_activeShooter) + " to shoot"));

            Hud.Legend(youAre == "Keeper"
                ? "WASD move   Mouse aim   LMB/RMB dive/save   Space jump   E/Q throw   V ball cam"
                : "HOLD Space power   Mouse aim   WASD spin   V ball cam");
            Hud.Flash(_flash, _flashTime / 1.6f);

            // A knocked-out shooter's END SCREEN, shown on every peer for AccuracyEndScreenHold
            // (see BroadcastAccuracyOut). It sits where the turn clock used to: this mode has no
            // clock, and an elimination is the thing worth interrupting the HUD for.
            if (AccuracyMode && !over && _accOutSlot >= 0 && Time.unscaledTime < _accOutUntil)
                Hud.Banner(RosterName(_accOutSlot) + " IS OUT",
                           "Score: " + _accOutScore,
                           _accSuddenDeath ? "Last one standing wins" : "Next player up");

            DrawScoreboard(st);
            DrawPowerMeter();

            // Quickchat feed + custom-text box (multiplayer).
            if (_qcFeed != null) _qcFeed.Draw();
            Hud.End();
        }

        // Centered power meter shown while the LOCAL player is charging their set-piece shot.
        void DrawPowerMeter()
        {
            if (_localIsKeeper || !LocalIsActiveShooter() || !_taker.IsCharging) return;
            Hud.Meter(_taker.Meter, "POWER  (release to shoot)");
        }

        // Scoreboard: a clean dark card, per-shooter rows with the name, the running goal count,
        // and a strip of ShotsEach PIPS - green = scored, red = missed, dim = not taken yet. The
        // active shooter's row is highlighted; a winner banner shows at full time.
        void DrawScoreboard(ShootoutState st)
        {
            if (st.scored == null) return;

            // Palette.
            Color cActive = new Color(0.16f, 0.32f, 0.52f, 0.55f);
            Color cGoal   = UITheme.Green;
            Color cMiss   = UITheme.Red;
            Color cEmpty  = new Color(1f, 1f, 1f, 0.14f);

            int rows = 0;
            for (int i = 1; i < NetSession.CrosserSlot; i++) if (_bodies[i] != null && _bodies[i].isShooter) rows++;
            if (rows == 0) { if (st.over) DrawWinnerBanner(); return; }

            // Accuracy reads as a per-player CARD (name, score, strikes under the name) rather than
            // the shootout's column grid, so it draws its own rows.
            if (AccuracyMode) { DrawAccuracyBoard(st, rows); return; }

            // What the right-hand strip counts: set pieces pip their ten shots.
            int attempts = ShotsEach;
            bool pips = attempts > 0 && attempts <= 12;   // beyond that a pip is a sliver; use a bar

            float pad = 12f, headH = 34f, colH = 17f, rowH = 34f, w = 340f;
            float x = Hud.W - w - 22f, y = 84f;
            float panelH = headH + colH + rows * rowH + pad * 2f;

            // Themed card + gold section header (Hud.Card draws the header bar and its divider).
            Hud.Card(new Rect(x - pad, y - pad, w + pad * 2f, panelH), "SHOOTOUT   best of " + ShotsEach);

            var nameSt  = Hud.RowName;
            var goalsSt = Hud.RowValue;
            var colSt = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Faint } };

            // Best score on the board, so the leader can be marked. Ties mark everyone on it, which is
            // correct - there is no leader until somebody breaks it.
            int best = 0;
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].isShooter && i < st.scored.Length)
                    best = Mathf.Max(best, st.scored[i]);

            float cScoreX = x + w * 0.44f, cScoreW = w * 0.15f;
            float cTakenX = x + w * 0.59f, cTakenW = w * 0.14f;
            float progX   = x + w * 0.75f, progW  = w * 0.25f;
            float cy = y - pad + headH;
            UITheme.Label(new Rect(cScoreX, cy, cScoreW, colH), "GLS", colSt);
            UITheme.Label(new Rect(cTakenX, cy, cTakenW, colH), "KCK", colSt);

            float ry = cy + colH;
            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                if (_bodies[i] == null || !_bodies[i].isShooter) continue;
                bool active = i == _activeShooter && !st.over;
                // Lit band plus a gold spine on whoever is up, instead of a text arrow.
                if (active)
                {
                    UITheme.Fill(new Rect(x - pad, ry, w + pad * 2f, rowH), cActive);
                    UITheme.Fill(new Rect(x - pad, ry, 2.5f, rowH), UITheme.Gold);
                }
                else UITheme.Divider(x, ry + rowH - 1f, w);

                int sc = i < st.scored.Length ? st.scored[i] : 0;
                int tk = i < st.taken.Length ? st.taken[i] : 0;

                UITheme.Label(new Rect(x, ry, w * 0.44f, rowH), "  " + RosterName(i), nameSt);
                // Leader's score goes gold. RowValue is shared, so put the colour back after.
                Color keepC = goalsSt.normal.textColor;
                if (best > 0 && sc == best) goalsSt.normal.textColor = UITheme.Gold;
                UITheme.Label(new Rect(cScoreX, ry, cScoreW, rowH), sc.ToString(), goalsSt);
                goalsSt.normal.textColor = keepC;
                UITheme.Label(new Rect(cTakenX, ry, cTakenW, rowH),
                          attempts > 0 ? tk + "/" + attempts : tk.ToString(), goalsSt);

                float py = ry + (rowH - 10f) * 0.5f;
                if (pips)
                {
                    // One pip per attempt, green if it went in and red if it did not.
                    float gap = 3f, pipW = (progW - gap * (attempts - 1)) / attempts;
                    for (int s = 0; s < attempts; s++)
                    {
                        Color pc = s < sc ? cGoal : (s < tk ? cMiss : cEmpty);
                        var pr = new Rect(progX + s * (pipW + gap), py, pipW, 10f);
                        UITheme.Fill(pr, pc);
                        if (s < sc)
                            UITheme.Fill(new Rect(pr.x, pr.y, pr.width, 1f), new Color(1f, 1f, 1f, 0.35f));
                    }
                }
                else if (attempts > 0)
                {
                    // Too many attempts to pip: a plain fill bar of the same information.
                    UITheme.Fill(new Rect(progX, py, progW, 10f), cEmpty);
                    float f = Mathf.Clamp01(tk / (float)attempts);
                    if (f > 0f) UITheme.Fill(new Rect(progX, py, progW * f, 10f), UITheme.Gold);
                }
                ry += rowH;
            }

            if (st.over) DrawWinnerBanner();
        }

        // ACCURACY board: one shared card listing every shooter - their name with their STRIKES
        // as pips directly underneath it, and their score on the right. Nothing else: this is the
        // whole state of the game, and there is no high score in multiplayer (that is a
        // single-player career stat, and a session best would just be the leader's score again).
        //
        // Everything here comes off the synced ShootoutState, so every peer draws the identical
        // board - it is one shared scoreboard, not a per-player view.
        void DrawAccuracyBoard(ShootoutState st, int rows)
        {
            // At FULL TIME the results card carries every figure this board does, from the same
            // tally, and the two overlap at any window narrower than the design width. Hand the
            // screen over rather than draw both.
            if (st.over) { DrawWinnerBanner(); return; }

            Color cActive = new Color(0.16f, 0.32f, 0.52f, 0.55f);
            Color cMiss   = UITheme.Red;
            Color cEmpty  = new Color(1f, 1f, 1f, 0.14f);

            const float pad = 12f, headH = 34f, colH = 17f, rowH = 46f, w = 340f;
            float x = Hud.W - w - 22f, y = 84f;
            float panelH = headH + colH + rows * rowH + pad * 2f;

            Hud.Card(new Rect(x - pad, y - pad, w + pad * 2f, panelH),
                     _accSuddenDeath ? "SUDDEN DEATH" : "STRIKES");

            var nameSt = Hud.RowName;
            var scoreSt = Hud.RowValue;
            var colSt = new GUIStyle(GUI.skin.label)
            { fontSize = 11, alignment = TextAnchor.MiddleRight, normal = { textColor = UITheme.Faint } };

            float cy = y - pad + headH;
            UITheme.Label(new Rect(x + w * 0.66f, cy, w * 0.30f, colH), "SCORE", colSt);

            float ry = cy + colH;
            for (int i = 1; i < NetSession.CrosserSlot; i++)
            {
                if (_bodies[i] == null || !_bodies[i].isShooter) continue;

                int sc = i < st.scored.Length ? st.scored[i] : 0;
                int tk = i < st.taken.Length ? st.taken[i] : 0;
                bool active = i == _activeShooter && !st.over;
                bool out_ = tk >= SimConfig.AccuracyStrikes;

                // Lit band plus a gold spine on whoever is up, instead of a text arrow.
                if (active)
                {
                    UITheme.Fill(new Rect(x - pad, ry, w + pad * 2f, rowH), cActive);
                    UITheme.Fill(new Rect(x - pad, ry, 2.5f, rowH), UITheme.Gold);
                }
                else UITheme.Divider(x, ry + rowH - 1f, w);

                // Name on the top line, score right-aligned against it.
                UITheme.Label(new Rect(x, ry + 2f, w * 0.66f, 24f), "  " + RosterName(i), nameSt);
                UITheme.Label(new Rect(x + w * 0.66f, ry + 2f, w * 0.30f, 24f), sc.ToString(), scoreSt);

                // Strikes UNDER the name: one pip per strike, red once spent. Disc, not Dot - Dot
                // wraps its square fill in a glow that bleeds into the neighbouring pips.
                const float dotR = 5.5f, gap = 7f;
                float dx = x + 12f, dy = ry + 32f;
                for (int k = 0; k < SimConfig.AccuracyStrikes; k++)
                    UITheme.Disc(new Rect(dx + k * (dotR * 2f + gap), dy - dotR, dotR * 2f, dotR * 2f),
                                 k < tk ? cMiss : cEmpty);

                // Knocked out: dim the whole row so who is still in reads at a glance. Drawn LAST so
                // it covers the row's own text rather than sitting under it.
                if (out_)
                    UITheme.Fill(new Rect(x - pad, ry, w + pad * 2f, rowH), new Color(0f, 0f, 0f, 0.45f));

                ry += rowH;
            }
            // No full-time branch here: this method returns early when st.over (see the top), so
            // reaching the end of the row loop means the match is still live.
        }

        void DrawWinnerBanner()
        {
            // ACCURACY gets a full results card - the mode has a per-player score AND a strike
            // count, and a one-line banner could only ever name the winner. Set pieces keep the
            // shared card, where the scoreboard beside it already tells the whole story.
            if (AccuracyMode) { DrawAccuracyResults(); return; }
            // The shared end-of-round card, so full time looks the same in every mode.
            Hud.Banner(WinnerText(), null, null);
        }

        // ------------------------------------------------------------ accuracy results card
        // FULL TIME in multiplayer accuracy: the winner large at the top with a crown against
        // their name, then everyone else beneath them, all showing the same two figures (rounds
        // cleared and strikes spent). It covers the screen on its own scrim rather than sitting
        // beside the live board - the board is drawn from the same tally and would only repeat it,
        // and at anything narrower than the design width the two overlap.
        //
        // Everything here reads the SYNCED ShootoutState, so every peer draws the identical card.
        // There are no buttons: the net protocol has no match reset (GameBootstrap leaves both the
        // restart and the setup callbacks null in multiplayer), so the pause menu's End Match /
        // Main Menu remains the only honest way out and this card must not pretend otherwise.
        static GUIStyle _resHdr, _resName, _resKey, _resVal, _resSmallName, _resSmallVal, _resTie;
        void DrawAccuracyResults()
        {
            var st = _s.LatestShootout;
            if (st.scored == null) { Hud.Banner(WinnerText(), null, null); return; }

            EnsureResultStyles();

            int winSlot = WinnerSlot();

            // Everyone who played, best first. Score descending, then FEWER strikes first - a
            // player who matched a score without spending as many strikes finished the cleaner run.
            var rows = new List<int>();
            for (int i = 1; i < NetSession.CrosserSlot; i++)
                if (_bodies[i] != null && _bodies[i].isShooter) rows.Add(i);
            rows.Sort((a, b) =>
            {
                // The crowned winner always leads, whatever the sort would otherwise say: in
                // sudden death the survivor can be outscored by somebody they outlasted.
                if (a == winSlot) return -1;
                if (b == winSlot) return 1;
                int sa = ScoreOf(st, a), sb = ScoreOf(st, b);
                if (sa != sb) return sb.CompareTo(sa);
                return StrikesOf(st, a).CompareTo(StrikesOf(st, b));
            });

            float w = Mathf.Min(560f, Hud.W - 80f);
            float headH = winSlot >= 0 ? 150f : 118f;
            float rowH = 54f;
            int others = Mathf.Max(0, rows.Count - (winSlot >= 0 ? 1 : 0));
            float h = headH + others * rowH + 46f;
            float x = Hud.W * 0.5f - w * 0.5f, y = Mathf.Max(24f, Hud.H * 0.5f - h * 0.5f);

            UITheme.Scrim(Hud.W, Hud.H, 0.6f, w + 220f);
            UITheme.Panel(new Rect(x, y, w, h), UITheme.Gold);

            // Header: the format, so a card kept on screen still says which game was played.
            UITheme.Label(new Rect(x, y + 12f, w, 20f),
                          _accSuddenDeath ? "FULL TIME  ·  SUDDEN DEATH" : "FULL TIME  ·  THREE STRIKES",
                          _resHdr);

            float cy = y + 38f;

            if (winSlot >= 0)
            {
                // THE WINNER, given the room the result deserves: a crown, their name, and their
                // two figures at a size nothing else on the card competes with.
                var band = new Rect(x + 16f, cy, w - 32f, 96f);
                UITheme.Glow(band, new Color(1f, 0.82f, 0.29f, 0.16f));
                UITheme.Fill(band, new Color(1f, 0.82f, 0.29f, 0.08f));
                UITheme.Fill(new Rect(band.x, band.y, 3f, band.height), UITheme.Gold);

                // No tag line under the name: the header above already names the format and the
                // crown already says they won, so it only restated both. The name is centred in the
                // band's height now that nothing sits under it.
                Crown(band.x + 30f, band.y + 30f, 15f, UITheme.Gold);
                UITheme.Shadowed(new Rect(band.x + 54f, band.y + 28f, band.width - 70f, 40f),
                                 RosterName(winSlot), _resName, UITheme.Ink, 0.7f, 2f);

                // The two figures, right-aligned so the losers' own figures line up under them.
                Figures(band.xMax - 210f, band.y + 14f, 210f, ScoreOf(st, winSlot), StrikesOf(st, winSlot),
                        _resVal, _resKey, big: true);
                cy = band.yMax + 12f;
            }
            else
            {
                // Nobody to crown: a tie on the top score. Say so where the winner would have been,
                // CENTRED - _resName is left-aligned for a name sitting beside a crown, which would
                // run this headline off the card's left edge.
                // Centred both ways: _resTie is MiddleCenter for the horizontal, and the box spans
                // the FULL 68 this branch advances cy by, so the line also sits in the middle of the
                // space the card reserved for it (a shorter box left it sitting high).
                UITheme.Shadowed(new Rect(x, cy, w, 68f), "TIE  (" + BestScore() + ")",
                                 _resTie, UITheme.Ink, 0.7f, 2f);
                cy += 68f;
            }

            UITheme.Divider(x + 20f, cy, w - 40f);
            cy += 8f;

            // Everyone else, in the same shape at a smaller size.
            for (int k = 0; k < rows.Count; k++)
            {
                int slot = rows[k];
                if (slot == winSlot) continue;

                var r = new Rect(x + 16f, cy, w - 32f, rowH);
                if (slot == _localSlot)
                {
                    // The local player's own row, marked the way the live board marks the active
                    // shooter - a lit band and a spine, not a different text colour.
                    UITheme.Fill(r, new Color(0.16f, 0.32f, 0.52f, 0.45f));
                    UITheme.Fill(new Rect(r.x, r.y, 2.5f, r.height), Hud.SlotColor(slot));
                }

                UITheme.Label(new Rect(r.x + 14f, r.y + 4f, r.width - 230f, 30f),
                              RosterName(slot), _resSmallName);
                Figures(r.xMax - 210f, r.y + 6f, 210f, ScoreOf(st, slot), StrikesOf(st, slot),
                        _resSmallVal, _resKey, big: false);

                UITheme.Divider(r.x, r.yMax, r.width);
                cy += rowH;
            }

            Hud.Legend("Esc  pause menu");
        }

        static int ScoreOf(ShootoutState st, int slot)
            => st.scored != null && slot < st.scored.Length ? st.scored[slot] : 0;
        static int StrikesOf(ShootoutState st, int slot)
            => st.taken != null && slot < st.taken.Length ? st.taken[slot] : 0;

        // One player's two figures: rounds cleared, and strikes spent out of the allowance. Drawn
        // as a pair so the winner's block and a loser's row cannot drift apart.
        static void Figures(float x, float y, float w, int score, int strikes,
                            GUIStyle valSt, GUIStyle keySt, bool big)
        {
            float half = w * 0.5f;
            float valH = big ? 44f : 30f;
            UITheme.Shadowed(new Rect(x, y, half, valH), score.ToString(), valSt, UITheme.Ink, 0.6f, 2f);
            UITheme.Shadowed(new Rect(x + half, y, half, valH),
                             strikes + "/" + SimConfig.AccuracyStrikes, valSt,
                             strikes >= SimConfig.AccuracyStrikes ? UITheme.Red : UITheme.Ink, 0.6f, 2f);
            UITheme.Label(new Rect(x, y + valH, half, 16f), "CLEARED", keySt);
            UITheme.Label(new Rect(x + half, y + valH, half, 16f), "STRIKES", keySt);
        }

        // A crown, scanline-filled from UITheme.Fill spans the way Hud.Star draws its star - there
        // is no vector helper in the IMGUI layer and no icon asset for one.
        //
        // Drawn as the CLASSIC silhouette rather than three separate spikes: a solid band, and
        // above it one polygon whose outline zig-zags down into two V notches and back up, so the
        // points share their bases the way a real crown's do. Same even-odd scanline fill
        // MenuIcons.FillPoly uses for its baked icons, but straight into IMGUI space.
        static void Crown(float cx, float cy, float r, Color col)
        {
            float w = r * 2f, h = r * 1.6f;
            float left = cx - r, top = cy - h * 0.5f, bottom = top + h;
            float bandTop = bottom - h * 0.30f;

            // Band across the base.
            UITheme.Fill(new Rect(left, bandTop, w, bottom - bandTop), col);

            // Points: left tip, notch, tall centre tip, notch, right tip - closed along the band.
            float notchY = top + h * 0.42f;
            float sideTop = top + h * 0.16f;   // outer points sit a little below the centre one
            var pts = new float[]
            {
                left,          bandTop,
                left,          sideTop,
                left + w*0.25f, notchY,
                cx,            top,
                left + w*0.75f, notchY,
                left + w,      sideTop,
                left + w,      bandTop,
            };

            int rows = Mathf.CeilToInt(bandTop - top);
            var xs = new List<float>(8);
            for (int i = 0; i < rows; i++)
            {
                float yy = top + i + 0.5f;
                xs.Clear();
                for (int p = 0; p < pts.Length; p += 2)
                {
                    float ax = pts[p], ay = pts[p + 1];
                    int q = (p + 2) % pts.Length;
                    float bx = pts[q], by = pts[q + 1];
                    // Half-open crossing test, so a vertex on the scanline counts exactly once.
                    if ((ay <= yy && by > yy) || (by <= yy && ay > yy))
                        xs.Add(ax + (yy - ay) / (by - ay) * (bx - ax));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    UITheme.Fill(new Rect(xs[k], top + i, xs[k + 1] - xs[k], 1f), col);
            }
        }

        static void EnsureResultStyles()
        {
            if (_resHdr != null) return;
            _resHdr       = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Gold } };
            _resName      = new GUIStyle { fontSize = 30, alignment = TextAnchor.MiddleLeft,   normal = { textColor = UITheme.Ink } };
            _resKey       = new GUIStyle { fontSize = 11, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Faint } };
            _resVal       = new GUIStyle { fontSize = 38, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            _resSmallName = new GUIStyle { fontSize = 18, alignment = TextAnchor.MiddleLeft,   normal = { textColor = UITheme.Ink } };
            _resSmallVal  = new GUIStyle { fontSize = 24, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            _resTie       = new GUIStyle { fontSize = 30, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Ink } };
            // Real bold cut on everything large, as the rest of the HUD does.
            UIFont.Heavy(_resName);
            UIFont.Heavy(_resVal);
            UIFont.Heavy(_resSmallVal);
            UIFont.Heavy(_resTie);
        }
    }
}
