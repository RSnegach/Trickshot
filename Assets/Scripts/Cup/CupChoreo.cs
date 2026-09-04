using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The body-level choreography of a cup round (design 7.3 - 7.7): the lineups with their arms
    /// round each other's shoulders, an AI taker's walk from the lineup to the spot, the beaten
    /// shooter's walk back, the scorer's window, the winners' beat and the losers' dejection trio.
    /// The round driver calls the On* hooks at the right phases and never moves a lineup body
    /// itself; this component owns those bodies' poses and motion until the driver's next
    /// placement cut takes them back (CupBodies.Stand / Unpark reset a body outright, so a cut
    /// always wins over whatever this was doing).
    ///
    /// It runs only where bodies are SIMULATED (Local and Host authority): the driver fires no
    /// hooks on a Client, whose bodies are puppets posed from the host's snapshot - the emotes and
    /// walks below reach a client as emote ids and positions on the wire.
    ///
    /// Two rules make it coexist with the other body owners:
    ///  - Poses are (re)applied in LateUpdate, after every driver and Celebration has run its
    ///    Update: whoever ClearPoseOverrides'd this frame loses to the choreography for bodies it
    ///    owns. Motion (MoveInput, facing) is written in Update; the ragdoll consumes it in
    ///    FixedUpdate.
    ///  - It never touches a body the driver is running: a Freed body (Striker on), the body a
    ///    SetPieceTaker or a keeper controller is driving, or one playing an emote it did not
    ///    start. <see cref="OwnsBody"/> tells the driver the reverse - which keeper it must leave
    ///    alone while a dejection or a celebration plays out on him.
    ///
    /// Every roll (dejection variant, an AI scorer's emote) comes from the round's Dejection
    /// stream in call order, so a re-run of the same round choreographs the same way.
    ///
    /// FREE KICKS (owner's call) have no lineup and no walking: the driver places every taker at
    /// the run-up start and the rest scattered behind him (CupSpots.FreeKickMarks), so OnPlacing
    /// puts those bodies into a casual WATCH stand instead of the arms-round-shoulders line, the
    /// walk-in and walk-back hooks are never called, and a missed or saved free kick plays one of
    /// the dejection trio on the shooter where he stands (<see cref="OnMissDeject"/>) until the
    /// driver's next cut. Penalties keep the design's lineup choreography unchanged.
    /// </summary>
    public sealed class CupChoreo : MonoBehaviour
    {
        // ---- local tunables (feel, not gameplay - the designed numbers live in CupTuning) -----
        /// <summary>A walker counts as arrived inside this flat radius of its mark (m).</summary>
        public const float ArriveRadius = 0.3f;
        /// <summary>How fast a walking body turns its facing (deg/s): a visible turn before the first step.</summary>
        public const float TurnRate = 320f;
        /// <summary>An AI taker walks to the spot with purpose: this much over CupTuning.WalkSpeed.</summary>
        public const float WalkInSpeedMul = 1.35f;
        /// <summary>Seconds a walk-in may take before its speed is raised to make it (far free-kick spots become a jog).</summary>
        public const float WalkInBudget = 4f;
        /// <summary>The walk-back solves its speed to arrive this long BEFORE CupTuning.WalkBackMax, so the rig's cut shows him arriving rather than covering a snap.</summary>
        public const float WalkBackArriveSlack = 0.3f;
        /// <summary>An AI scorer's run before his emote (m/s, s).</summary>
        public const float ScorerRunSpeed = 4.2f;
        public const float ScorerRunSeconds = 1.2f;
        /// <summary>Pause between an AI body's consecutive emotes, and the stagger down a celebrating line.</summary>
        public const float EmoteGap = 0.25f;
        public const float WinStagger = 0.18f;
        /// <summary>The backward shove that drops a DejectFall body (velocity change on the torso / head, m/s).</summary>
        public const float FallPushTorso = 2.0f;
        public const float FallPushHead = 2.6f;
        /// <summary>The lineup's head cone toward the ball (deg).</summary>
        public const float LineupHeadYaw = 45f;
        public const float LineupHeadPitch = 20f;
        /// <summary>How far a beaten shooter drops his head on the walk back (deg).</summary>
        public const float WalkBackHeadDown = 18f;

        enum Mode { None, Lineup, Walk, Deject, Celebrate }

        /// <summary>What the choreography is doing with one body.</summary>
        sealed class Track
        {
            public CupBody Body;
            public Mode Mode;
            // Lineup
            public Vector3[] Pose;
            public float Breath;
            // Walk
            public Vector3 Target;
            public Quaternion ArriveFacing = Quaternion.identity;
            public float Speed, MaxSeconds, Elapsed, GaitPhase, GaitAmount;
            public Action OnArrived;
            public bool HeadDown;
            // Deject
            public int Variant;
            public bool Fallen;
            public float T;
            // Celebrate
            public Celebration.Emote[] Emotes;
            public float NextAt;
            public int Played, MaxPlays;
            public bool WasPlaying;
        }

        readonly List<Track> _tracks = new List<Track>();
        // Adjacent lineup bodies whose colliders ignore each other (see EnterLineup): [i] and [i+1] are a pair.
        readonly List<CupBody> _neighbourPairs = new List<CupBody>();
        CupRoundDriver _driver;
        CupCameraRig _rig;
        SeededRng _rng;
        float _clock;
        CupSide? _winner;
        int _protagonistVariant = -1;

        /// <summary>Create the choreography for a round under its root (the driver's AttachChoreo binds it).</summary>
        public static CupChoreo Create(Transform root, CupRoundDriver driver, CupCameraRig rig)
        {
            var go = new GameObject("CupChoreo");
            if (root != null) go.transform.SetParent(root, false);
            var c = go.AddComponent<CupChoreo>();
            c._driver = driver;
            c._rig = rig;
            var setup = driver != null ? driver.Setup : null;
            var data = driver != null ? driver.Data : null;
            c._rng = setup != null && data != null
                ? setup.Stream(CupSalts.Dejection(setup.Stage, data.Index))
                : new SeededRng(0x5EEDu);
            return c;
        }

        /// <summary>The rig this choreography frames its beats on (the driver's, set by the director).</summary>
        public CupCameraRig Rig
        {
            get => _rig;
            set => _rig = value;
        }

        /// <summary>
        /// Is the choreography driving this body right now (a walk, a dejection or a celebration)?
        /// The driver leaves a keeper alone while this is true - an AI keeper's brain would clear
        /// the pose and re-plant him every tick, a human keeper's controller would stand him up.
        /// </summary>
        public bool OwnsBody(CupBody body)
        {
            var t = Find(body);
            return t != null && (t.Mode == Mode.Walk || t.Mode == Mode.Deject || t.Mode == Mode.Celebrate);
        }

        // ==========================================================================================
        // Driver hooks (design 7.2)
        // ==========================================================================================

        /// <summary>
        /// After every placement cut: the driver has just stood everyone at their marks (taker at
        /// the run-up start or the lineup, keeper on the line, the rest on their lineup marks), so
        /// every walk and beat still running is over and both lineups take their pose.
        /// </summary>
        public void OnPlacing(CupRoundDriver driver)
        {
            if (driver != null) _driver = driver;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                t.Mode = Mode.None;
                t.OnArrived = null;
                t.Fallen = false;
            }
            _winner = null;
            _protagonistVariant = -1;
            RestoreNeighbourCollisions(null);
            if (_driver == null) return;
            bool freeKicks = _driver.Setup != null && _driver.Setup.Format == CupFormat.FreeKicks;
            if (freeKicks)
            {
                // The scatter: each body on its own mark, in a casual stand, watching the ball.
                EnterWatch(LineupOf(CupSide.A), CupSide.A);
                EnterWatch(LineupOf(CupSide.B), CupSide.B);
            }
            else
            {
                EnterLineup(LineupOf(CupSide.A), CupSide.A);
                EnterLineup(LineupOf(CupSide.B), CupSide.B);
            }
        }

        /// <summary>
        /// A missed or saved FREE KICK (owner's call, replaces the walk-back for that format):
        /// the shooter plays one of the three dejections where he stands - the losing-miss trio,
        /// the variant rolled from the round's Dejection stream in call order so peers agree -
        /// and the driver cuts to the next kick after CupTuning.FreeKickMissBeat. Nothing walks;
        /// the shooter's camera simply holds on him. The driver stands the taker down before
        /// calling this, so the swing's settle cannot wipe the pose.
        /// </summary>
        public void OnMissDeject(CupRoundDriver driver, CupBody shooter)
        {
            if (driver != null) _driver = driver;
            int variant = _rng.Range(0, 3);   // drawn even for an unmovable body: the stream stays in step
            if (!Movable(shooter)) return;
            Deject(shooter, variant);
        }

        /// <summary>
        /// An AI taker sets off from his lineup mark for the run-up start (design 7.3: "AI takers
        /// walk from their lineup to the spot"). `onArrived` is the driver's, which then plants him
        /// exactly on the mark; the driver also cuts the walk at its own timeout, so a body that
        /// gets stuck costs seconds, never the round.
        /// </summary>
        public void OnAiTakerToSpot(CupRoundDriver driver, CupBody body, Action onArrived)
        {
            if (driver != null) _driver = driver;
            if (!Movable(body)) { onArrived?.Invoke(); return; }
            Vector3 target = _driver != null ? _driver.RunUpStart : body.GroundPos;
            float dist = Vector3.Distance(body.GroundPos, target);
            // A walk, unless the spot is so far (a wide free kick) that a walk would eat the
            // budget - then a jog, which the gait amount reads off the speed.
            float speed = Mathf.Max(CupTuning.WalkSpeed * WalkInSpeedMul, dist / WalkInBudget);
            var facing = _driver != null ? CupSpots.FacingGoal(_driver.BallSpotPos) : body.LineupFacing;
            Walk(body, target, speed, facing, WalkInBudget + 3f, false, onArrived);
        }

        /// <summary>
        /// The scorer's five seconds (design 7.4). A human runs and emotes on his own (the driver
        /// freed his Striker, the HUD opens his wheel); an AI scorer gets a short run away from the
        /// goal and a seeded celebration or two. The camera is the driver's (HoldOn the scorer).
        /// </summary>
        public void OnScored(CupRoundDriver driver, CupBody scorer)
        {
            if (driver != null) _driver = driver;
            if (!Movable(scorer) || scorer.IsHuman) return;
            Vector3 away = scorer.LineupMark - scorer.GroundPos;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-3f) away = -Vector3.forward;
            away.Normalize();
            Vector3 target = scorer.GroundPos + away * (ScorerRunSpeed * ScorerRunSeconds);
            var facing = Quaternion.LookRotation(away, Vector3.up);
            var body = scorer;
            Walk(body, target, ScorerRunSpeed, facing, ScorerRunSeconds, false,
                 () => Celebrate(body, CupPoses.ScorerEmotes, 2, 0f));
        }

        /// <summary>
        /// The beaten shooter turns and walks to his lineup slot at walking pace while the rig runs
        /// the two-shot (design 7.5). `onArrived` is the driver's cue for the next cut; the driver
        /// cuts at CupTuning.WalkBackMax regardless, and the cut hides any snap.
        ///
        /// The pace is sized against THIS walk, the way OnAiTakerToSpot sizes the walk-in: the
        /// cinematic is meant to end on the arrival (design 7.5), so a mark further out than
        /// CupTuning.WalkSpeed reaches in the budget is walked faster rather than never reached.
        /// WalkSpeed is the floor - a short walk keeps its brisk-walk pace - and the derivation in
        /// its doc comment covers a FIVE-body line; an 8-strong Co-op lineup puts the outermost
        /// mark 10.4 m away (3.7 s at 2.85), which is what this solve absorbs. The 0.3 s of slack
        /// lands the arrival before the cut instead of on it.
        /// </summary>
        public void OnWalkBack(CupRoundDriver driver, CupBody shooter, Action onArrived)
        {
            if (driver != null) _driver = driver;
            if (!Movable(shooter)) { onArrived?.Invoke(); return; }
            if (_rig != null) _rig.WalkBackView(shooter.Ragdoll, shooter.LineupMark, CupTuning.WalkBackMax);
            var body = shooter;
            float dist = Vector3.Distance(body.GroundPos, body.LineupMark);
            float speed = Mathf.Max(CupTuning.WalkSpeed, dist / Mathf.Max(0.5f, CupTuning.WalkBackMax - WalkBackArriveSlack));
            Walk(body, body.LineupMark, speed, body.LineupFacing, CupTuning.WalkBackMax, true, () =>
            {
                // Back in the line: the arms go round the shoulders again, him included.
                EnterLineup(LineupOf(body.Side, body), body.Side);
                onArrived?.Invoke();
            });
        }

        /// <summary>
        /// The rules decided the round. Latches the winner and rolls the PROTAGONIST's dejection
        /// variant first (design 7.6: "one of three, rolled by the round RNG"): the shooter whose
        /// miss lost it, or the keeper who conceded the winning goal. The lineups' variants are
        /// rolled after, in OnLoseBeat's call order, so the sequence is fixed for the round.
        /// </summary>
        public void OnRoundDecided(CupRoundDriver driver, CupSide winner)
        {
            if (driver != null) _driver = driver;
            _winner = winner;
            _protagonistVariant = _rng.Range(0, 3);
        }

        /// <summary>The winners' beat (design 7.7): humans are already Freed by the driver; the AI bodies on that side cheer.</summary>
        public void OnWinBeat(CupRoundDriver driver, CupSide winner)
        {
            if (driver != null) _driver = driver;
            _winner = winner;
            if (_driver == null) return;
            WinBeat(_driver.BodiesOn(winner));
        }

        /// <summary>One losing body dejects (design 7.6): the protagonist gets the round's roll, the lineup its own (never the fall - a whole line on its backs is a farce).</summary>
        public void OnLoseBeat(CupRoundDriver driver, CupBody loser)
        {
            if (driver != null) _driver = driver;
            if (!Movable(loser)) return;
            int variant;
            if (IsProtagonist(loser) && _protagonistVariant >= 0)
            {
                variant = _protagonistVariant;
                _protagonistVariant = -1;   // spent: a second body can never take the same roll
            }
            else
            {
                variant = _rng.Range(0, 2);
            }
            Deject(loser, variant);
        }

        // ==========================================================================================
        // Body-level actions (the contract's public verbs)
        // ==========================================================================================

        /// <summary>
        /// Put a side's visible lineup into the arms-round-shoulders set: sorted by lineup index
        /// (index runs toward the character's right), each body drapes an arm over each neighbour
        /// it has, the ends one arm, a lone body stands hands on hips. Bodies stay live (balance
        /// on, locomotion holding them on the mark) so they sway; the pose is re-applied every
        /// LateUpdate for as long as they are in the line. Parked, inactive and referee bodies are
        /// skipped, so a human's hidden twin never gets an arm.
        /// </summary>
        public void EnterLineup(IList<CupBody> bodies, CupSide side)
        {
            if (bodies == null) return;
            var line = new List<CupBody>();
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b == null || !b.Alive || b.Parked || !b.Active || b.Role == CupBodyRole.Referee || b.Side != side) continue;
                if (b.Freed) continue;   // a freed body is the driver's (it runs)
                line.Add(b);
            }
            line.Sort((x, y) => x.LineupIndex.CompareTo(y.LineupIndex));
            for (int i = 0; i < line.Count; i++)
            {
                var b = line[i];
                bool left = i > 0 && line[i - 1].LineupIndex != b.LineupIndex;
                bool right = i < line.Count - 1 && line[i + 1].LineupIndex != b.LineupIndex;
                // Neighbours 0.62 m apart with an arm behind each other's neck cannot collide with
                // each other on the way to the pose: PhysX resolved that overlap by folding both
                // arms straight up (hands meeting above the heads) or shoving the pair apart by
                // half a metre (both seen in play mode). The pose itself is solved clear of the
                // neighbour's volume (CupPoses), so with the pair ignoring each other it settles
                // exactly there; the ignore is lifted the moment either body leaves the line.
                if (right) IgnoreNeighbourCollisions(b, line[i + 1]);
                var t = TrackFor(b);
                t.Mode = Mode.Lineup;
                t.Pose = CupPoses.ArmsRound(left, right);
                t.Breath = b.LineupIndex * 1.7f + (b.IsKeeperBody ? 0.9f : 0f);
                t.OnArrived = null;
                t.Fallen = false;
                var rag = b.Ragdoll;
                rag.MoveInput = Vector3.zero;
                rag.UprightLock = true;
                rag.BalanceEnabled = true;
                // Locomotion ON with zero input: the velocity steer holds the body on its mark
                // for the minutes a round lasts (a nudge from a neighbour's arm would otherwise
                // walk it off the line); the sway is rotational and untouched by it.
                rag.LocomotionEnabled = true;
                rag.FacingRotation = b.LineupFacing;
                if (b.Striker != null) b.Striker.ControlEnabled = false;
            }
        }

        /// <summary>
        /// Put a side's scattered bodies (Free Kicks) into a casual watching stand on the marks
        /// the driver gave them: balance on, LOCOMOTION OFF (nobody touches anybody in the
        /// scatter, so nothing can nudge a body off its mark, and a body that is not steered
        /// stands more naturally), free look cone on the local human exactly as the lineup has,
        /// heads following the ball, a seeded stand per body (hands on hips / clasped behind the
        /// back / one hand on a hip) re-applied every LateUpdate. Same filters as EnterLineup:
        /// parked, inactive, freed and referee bodies are skipped; no neighbour collision
        /// ignores are needed because the marks keep CupTuning.FreeKickMarkClearance apart.
        /// </summary>
        public void EnterWatch(IList<CupBody> bodies, CupSide side)
        {
            if (bodies == null) return;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (b == null || !b.Alive || b.Parked || !b.Active || b.Role == CupBodyRole.Referee || b.Side != side) continue;
                if (b.Freed) continue;   // a freed body is the driver's (it runs)
                var t = TrackFor(b);
                t.Mode = Mode.Lineup;
                t.Pose = CupPoses.WatchPose(_rng.Range(0, 3));
                t.Breath = b.LineupIndex * 1.7f + (b.IsKeeperBody ? 0.9f : 0f) + (b.Side == CupSide.B ? 0.6f : 0f);
                t.OnArrived = null;
                t.Fallen = false;
                var rag = b.Ragdoll;
                rag.MoveInput = Vector3.zero;
                rag.UprightLock = true;
                rag.BalanceEnabled = true;
                rag.LocomotionEnabled = false;
                rag.FacingRotation = b.LineupFacing;
                if (b.Striker != null) b.Striker.ControlEnabled = false;
            }
        }

        /// <summary>Open a free window for a body (CupBodies.Free) and stop choreographing it.</summary>
        public void FreeBody(CupBody body)
        {
            if (body == null) return;
            Release(body);
            CupBodies.Free(body);
        }

        /// <summary>
        /// Stop choreographing a body and leave it standing square where it is (no walk, no pose,
        /// control off). This is a HOLD, not a hide: hiding a body behind the goal is the driver's
        /// (CupBodies.Park with a hide spot), which also owns the twin bookkeeping that goes with it.
        /// </summary>
        public void ParkBody(CupBody body)
        {
            if (body == null) return;
            Release(body);
            if (!body.Alive || body.Parked) return;
            var rag = body.Ragdoll;
            if (body.Celeb != null && body.Celeb.Playing) body.Celeb.Cancel();
            if (body.Striker != null) body.Striker.ControlEnabled = false;
            body.Freed = false;
            rag.BalanceEnabled = true;
            rag.UprightLock = true;
            rag.LocomotionEnabled = true;
            CupPoses.Stop(rag, rag.FacingRotation);
        }

        /// <summary>
        /// Play a dejection on a body: 0 knees + face in hands, 1 hands on hips, 2 arms on the head
        /// and a fall straight back (design 7.6). The fall is two-stage: the emote poses the arms,
        /// and CupTuning.DejectionFallHold later Update frees the body (balance off, upright off)
        /// and shoves the top of it backward, so gravity does the fall with the hands still on the
        /// head. Once the emote has ended the body stays down: the arms are held by pose overrides
        /// and the balance is kept off until the driver's next placement or the teardown.
        /// </summary>
        public void Deject(CupBody body, int variant)
        {
            if (!Movable(body)) return;
            var t = TrackFor(body);
            t.Mode = Mode.Deject;
            t.Variant = ((variant % 3) + 3) % 3;
            t.Fallen = false;
            t.T = 0f;
            t.OnArrived = null;
            var rag = body.Ragdoll;
            rag.MoveInput = Vector3.zero;
            if (body.Striker != null) body.Striker.ControlEnabled = false;
            body.Freed = false;
            rag.LocomotionEnabled = true;
            rag.UprightLock = true;
            rag.BalanceEnabled = true;
            if (body.Celeb != null)
            {
                if (body.Celeb.Playing) body.Celeb.Cancel();   // Play snapshots the control flags: never stack
                body.Celeb.Play(CupPoses.DejectEmote(t.Variant));
            }
        }

        /// <summary>
        /// The winners' five seconds for a list of bodies: humans are the driver's (Freed, they run
        /// and pick from the wheel); every AI body on the list cycles through seeded standing
        /// cheers, staggered down the line so it never moves as one.
        /// </summary>
        public void WinBeat(IList<CupBody> bodies)
        {
            if (bodies == null) return;
            int n = 0;
            for (int i = 0; i < bodies.Count; i++)
            {
                var b = bodies[i];
                if (!Movable(b) || b.Role == CupBodyRole.Referee) continue;
                if (b.IsHuman) { Release(b); continue; }
                Celebrate(b, CupPoses.WinEmotes, 3, n * WinStagger);
                n++;
            }
        }

        // ==========================================================================================
        // Per frame
        // ==========================================================================================

        void Update()
        {
            if (PauseMenu.Frozen) return;   // a Solo pause stops the beats with the sim; the MP overlay does not
            float dt = Time.deltaTime;
            _clock += dt;
            for (int i = 0; i < _tracks.Count; i++)   // index loop: an arrival callback may add tracks
            {
                var t = _tracks[i];
                var b = t.Body;
                if (b == null || !b.Alive || b.Parked) { t.Mode = Mode.None; continue; }
                switch (t.Mode)
                {
                    case Mode.Walk: TickWalk(t, dt); break;
                    case Mode.Deject: TickDeject(t, dt); break;
                    case Mode.Celebrate: TickCelebrate(t); break;
                }
            }
        }

        void LateUpdate()
        {
            if (PauseMenu.Frozen) return;
            float dt = Time.deltaTime;
            var ball = _driver != null && _driver.Setup != null && _driver.Setup.Ball != null ? _driver.Setup.Ball.transform : null;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                var b = t.Body;
                if (b == null || !b.Alive || b.Parked || t.Mode == Mode.None) continue;
                if (b.Freed) continue;   // the driver runs it (Striker / keeper controller)
                var rag = b.Ragdoll;
                bool emoting = b.Celeb != null && b.Celeb.Playing;
                switch (t.Mode)
                {
                    case Mode.Lineup:
                        if (emoting) break;   // an emote owns the pose
                        rag.ClearPoseOverrides();
                        CupPoses.Apply(rag, t.Pose);
                        CupPoses.Breathe(rag, _clock, t.Breath, 1f);
                        // Heads follow the ball within the cone: the line watches the kick.
                        if (ball != null) CupPoses.LookAt(rag, ball.position, LineupHeadYaw, LineupHeadPitch);
                        break;
                    case Mode.Walk:
                        rag.ClearPoseOverrides();
                        CupPoses.WalkGait(rag, ref t.GaitPhase, dt, t.GaitAmount);
                        if (t.HeadDown) rag.AddPoseOverride(Bone.Head, new Vector3(WalkBackHeadDown, 0f, 0f));
                        break;
                    case Mode.Deject:
                        if (t.Fallen)
                        {
                            // Celebration.End restores the flags it snapshotted and clears the
                            // pose; the fallen body must stay down with its hands on its head.
                            rag.BalanceEnabled = false;
                            rag.UprightLock = false;
                            rag.LocomotionEnabled = false;
                            rag.MoveInput = Vector3.zero;
                            if (!emoting)
                            {
                                rag.ClearPoseOverrides();
                                CupPoses.Apply(rag, CupPoses.DejectFallArms);
                            }
                        }
                        else if (!emoting)
                        {
                            // The knees / hips emote ran out before the beat did: a slumped stand.
                            rag.ClearPoseOverrides();
                            CupPoses.Apply(rag, CupPoses.DejectedIdle);
                            CupPoses.Breathe(rag, _clock, t.Breath, 0.8f);
                        }
                        break;
                    case Mode.Celebrate:
                        break;   // the emotes own the pose; between two, a stand
                }
            }
        }

        void TickWalk(Track t, float dt)
        {
            var rag = t.Body.Ragdoll;
            t.Elapsed += dt;
            Vector3 aim = t.Target;
            Vector3 obstacle;
            if (AvoidPoint(t.Body, out obstacle)) aim = Detour(t.Body.GroundPos, t.Target, obstacle);
            CupPoses.Steer(rag, aim, t.Speed, TurnRate, dt);
            t.GaitAmount = CupPoses.GaitAmount(rag.MoveInput.magnitude);
            // Arrival is measured against the real mark, whatever the detour is steering at.
            Vector3 left = t.Target - t.Body.GroundPos;
            left.y = 0f;
            if (left.magnitude <= ArriveRadius || t.Elapsed >= t.MaxSeconds) Arrive(t);
        }

        /// <summary>The walker keeps this much clearance from the referee (m): a body-and-arm's width plus a step.</summary>
        public const float AvoidRadius = 1.3f;

        /// <summary>
        /// Debug / test only: the point walks detour around when there is no round driver to
        /// supply the referee (CupDebugCapture stages walks on loose bodies). Null = none.
        /// </summary>
        public Vector3? DebugAvoidPoint { get; set; }

        /// <summary>
        /// The one thing standing on a walker's way: the referee, on his mark beside the ball.
        /// Seen in play mode: a left-side free-kick spot puts his mark (3 m to the taker's right)
        /// squarely on the AI taker's line from the +6 lineup, and the walk-in shoved him 1.3 m
        /// off it. Nothing else stands between a lineup and a spot.
        /// </summary>
        bool AvoidPoint(CupBody walker, out Vector3 point)
        {
            point = Vector3.zero;
            if (walker == null || walker.Role == CupBodyRole.Referee) return false;
            var rf = _driver != null ? _driver.RefereeActor : null;
            if (rf != null && rf.Alive) { point = CupSpots.Ground(rf.Body.Pelvis.position); return true; }
            if (DebugAvoidPoint.HasValue) { point = CupSpots.Ground(DebugAvoidPoint.Value); return true; }
            return false;
        }

        /// <summary>
        /// Where to aim right now so the straight walk `me` -> `goal` passes `obstacle` with
        /// AvoidRadius to spare: the goal itself unless the obstacle sits on the line ahead, else
        /// a point beside the obstacle on the side it is NOT on. Once the walker is level with the
        /// obstacle the aim returns to the goal, so the path is a shallow S, not a stop-and-turn.
        /// </summary>
        public static Vector3 Detour(Vector3 me, Vector3 goal, Vector3 obstacle)
        {
            Vector3 toGoal = goal - me;
            toGoal.y = 0f;
            float len = toGoal.magnitude;
            if (len < 0.5f) return goal;
            Vector3 dir = toGoal / len;
            Vector3 toObs = obstacle - me;
            toObs.y = 0f;
            float along = Vector3.Dot(toObs, dir);
            if (along < 0.3f || along > len + 0.5f) return goal;          // behind us, or beyond the mark
            Vector3 side = Vector3.Cross(Vector3.up, dir);                 // the path's right
            float lateral = Vector3.Dot(toObs, side);
            if (Mathf.Abs(lateral) >= AvoidRadius) return goal;            // already clear
            float pass = lateral >= 0f ? -AvoidRadius : AvoidRadius;       // pass on the far side
            Vector3 d = CupSpots.Ground(obstacle) + side * pass;
            d.y = goal.y;
            return d;
        }

        /// <summary>Debug / test only: walk a loose body to a mark at a speed (CupDebugCapture stages the detour and the gaits with it).</summary>
        [UnityEngine.Scripting.Preserve]
        public void DebugWalk(CupBody body, Vector3 target, float speed, Action onArrived)
        {
            if (body == null || !body.Alive) return;
            Vector3 to = target - body.GroundPos;
            to.y = 0f;
            var facing = to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to.normalized, Vector3.up) : body.Ragdoll.FacingRotation;
            Walk(body, target, speed, facing, 20f, false, onArrived);
        }

        void Arrive(Track t)
        {
            CupPoses.Stop(t.Body.Ragdoll, t.ArriveFacing);
            t.Mode = Mode.None;
            t.GaitAmount = 0f;
            var cb = t.OnArrived;
            t.OnArrived = null;
            cb?.Invoke();
        }

        void TickDeject(Track t, float dt)
        {
            t.T += dt;
            if (t.Variant == CupPoses.FallVariant && !t.Fallen && t.T >= CupTuning.DejectionFallHold) DropBody(t);
        }

        /// <summary>Free the body and shove its top backward: it goes over onto its back, arms still on its head.</summary>
        void DropBody(Track t)
        {
            var rag = t.Body.Ragdoll;
            t.Fallen = true;
            rag.MoveInput = Vector3.zero;
            rag.BalanceEnabled = false;
            rag.UprightLock = false;
            rag.LocomotionEnabled = false;
            Vector3 back = -(rag.FacingRotation * Vector3.forward);
            back.y = 0f;
            if (back.sqrMagnitude < 1e-4f) back = -Vector3.forward;
            back.Normalize();
            // Top-heavy push, not a whole-body shove: the feet are slick, so an equal velocity on
            // every part would slide the body backward standing up instead of toppling it.
            var torso = rag.Rb(Bone.Torso);
            var head = rag.Rb(Bone.Head);
            if (torso != null) torso.AddForce(back * FallPushTorso, ForceMode.VelocityChange);
            if (head != null) head.AddForce(back * FallPushHead + Vector3.up * 0.3f, ForceMode.VelocityChange);
        }

        void TickCelebrate(Track t)
        {
            var c = t.Body.Celeb;
            if (c == null) { t.Mode = Mode.None; return; }
            if (c.Playing) { t.WasPlaying = true; return; }
            if (t.WasPlaying)
            {
                t.WasPlaying = false;
                t.NextAt = _clock + EmoteGap;
            }
            if (t.Played >= t.MaxPlays || _clock < t.NextAt) return;
            var e = t.Emotes[_rng.Range(0, t.Emotes.Length)];
            t.Body.Ragdoll.MoveInput = Vector3.zero;
            c.Play(e);
            t.Played++;
            t.WasPlaying = true;
        }

        // ==========================================================================================
        // Internals
        // ==========================================================================================

        void Walk(CupBody body, Vector3 target, float speed, Quaternion arriveFacing, float maxSeconds, bool headDown, Action onArrived)
        {
            var t = TrackFor(body);
            t.Mode = Mode.Walk;
            t.Target = CupSpots.Ground(target);
            t.Speed = Mathf.Max(0.1f, speed);
            t.ArriveFacing = arriveFacing;
            t.MaxSeconds = Mathf.Max(0.1f, maxSeconds);
            t.Elapsed = 0f;
            t.GaitPhase = 0f;
            t.GaitAmount = 0f;
            t.HeadDown = headDown;
            t.OnArrived = onArrived;
            t.Fallen = false;
            var rag = body.Ragdoll;
            if (body.Celeb != null && body.Celeb.Playing) body.Celeb.Cancel();
            if (body.Striker != null) body.Striker.ControlEnabled = false;
            body.Freed = false;
            rag.UprightLock = true;
            rag.BalanceEnabled = true;
            rag.LocomotionEnabled = true;
            rag.ClearPoseOverrides();
            rag.SetPose(RagdollPose.Stand, 5f);
        }

        void Celebrate(CupBody body, Celebration.Emote[] emotes, int maxPlays, float delay)
        {
            if (!Movable(body) || emotes == null || emotes.Length == 0) return;
            var t = TrackFor(body);
            t.Mode = Mode.Celebrate;
            t.Emotes = emotes;
            t.MaxPlays = Mathf.Max(1, maxPlays);
            t.Played = 0;
            t.NextAt = _clock + Mathf.Max(0f, delay);
            t.WasPlaying = false;
            t.OnArrived = null;
            t.Fallen = false;
            var rag = body.Ragdoll;
            rag.MoveInput = Vector3.zero;
            rag.UprightLock = true;
            rag.BalanceEnabled = true;
            rag.LocomotionEnabled = true;
            rag.ClearPoseOverrides();
            rag.SetPose(RagdollPose.Stand, 5f);
        }

        void Release(CupBody body)
        {
            RestoreNeighbourCollisions(body);
            var t = Find(body);
            if (t == null) return;
            t.Mode = Mode.None;
            t.OnArrived = null;
            t.Fallen = false;
        }

        /// <summary>Every collider pair between two adjacent lineup bodies ignores each other (see EnterLineup); remembered so it can be lifted.</summary>
        void IgnoreNeighbourCollisions(CupBody a, CupBody b)
        {
            if (a == null || b == null || a == b || !a.Alive || !b.Alive) return;
            for (int i = 0; i + 1 < _neighbourPairs.Count; i += 2)
                if ((_neighbourPairs[i] == a && _neighbourPairs[i + 1] == b) || (_neighbourPairs[i] == b && _neighbourPairs[i + 1] == a)) return;
            SetPairCollision(a, b, true);
            _neighbourPairs.Add(a);
            _neighbourPairs.Add(b);
        }

        /// <summary>Lift the neighbour ignores involving `body` (null = every pair): a body leaving the line collides again.</summary>
        void RestoreNeighbourCollisions(CupBody body)
        {
            for (int i = _neighbourPairs.Count - 2; i >= 0; i -= 2)
            {
                var a = _neighbourPairs[i];
                var b = _neighbourPairs[i + 1];
                if (body != null && a != body && b != body) continue;
                SetPairCollision(a, b, false);
                _neighbourPairs.RemoveRange(i, 2);
            }
        }

        static void SetPairCollision(CupBody a, CupBody b, bool ignore)
        {
            if (a == null || b == null || a.Ragdoll == null || b.Ragdoll == null) return;
            var ca = a.Ragdoll.GetComponentsInChildren<Collider>(true);
            var cb = b.Ragdoll.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < ca.Length; i++)
            {
                if (ca[i] == null) continue;
                for (int j = 0; j < cb.Length; j++)
                    if (cb[j] != null) Physics.IgnoreCollision(ca[i], cb[j], ignore);
            }
        }

        /// <summary>A body this component may move: built, live, visible, not the driver's referee.</summary>
        static bool Movable(CupBody b) => b != null && b.Alive && !b.Parked && b.Active && b.Role != CupBodyRole.Referee;

        /// <summary>The bodies standing in a side's lineup this kick (Role Lineup), plus an optional extra (the shooter rejoining).</summary>
        List<CupBody> LineupOf(CupSide side, CupBody extra = null)
        {
            var list = new List<CupBody>();
            if (_driver == null) return list;
            var all = _driver.BodiesOn(side);
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == extra || b.Role == CupBodyRole.Lineup) list.Add(b);
            }
            if (extra != null && !list.Contains(extra)) list.Add(extra);
            return list;
        }

        /// <summary>The losing body the round turned on: the shooter of a losing miss / save, or the keeper beaten by the winning goal.</summary>
        bool IsProtagonist(CupBody b)
        {
            if (_driver == null || b == null) return false;
            var last = _driver.LastOutcome;
            if (!last.HasValue) return false;
            if (last.Value == KickOutcome.Goal) return _driver.LastKeeperBody == b;
            return _driver.LastTakerBody == b;
        }

        Track Find(CupBody body)
        {
            if (body == null) return null;
            for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].Body == body) return _tracks[i];
            return null;
        }

        Track TrackFor(CupBody body)
        {
            var t = Find(body);
            if (t != null) return t;
            t = new Track { Body = body };
            _tracks.Add(t);
            return t;
        }

        void OnDestroy()
        {
            // Nothing owned here outlives the round root: no materials, no meshes. A body still
            // down from a fall is the driver's to reset (its next Stand restores every flag). The
            // neighbour ignores are lifted in case a body outlives this component (a re-Configure).
            RestoreNeighbourCollisions(null);
            _tracks.Clear();
        }
    }
}
