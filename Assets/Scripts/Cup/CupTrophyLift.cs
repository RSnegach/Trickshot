using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The Co-op win (design 8.2): a CupTuning.TrophyLiftSeconds (14 s) scripted cinematic on the
    /// rig's shot list, then a free window, then the CHAMPIONS results through <c>onContinue</c>.
    ///
    ///   Shot 1 (0 - 4.5 s)   Cut to the centre circle: the team jogs in from the goal side (the
    ///                        walk gait, CupPoses) to a cluster round the centre spot, the AI nation
    ///                        walks off toward the touchline, the referee applauds (Clap) from the
    ///                        circle's edge. A low front shot tracking the Captain, pushing in.
    ///   Shot 2 (4.5 - 9 s)   The cut hands the Captain the trophy (CupTrophy.AttachToHand) and he
    ///                        lifts it (TrophyLift, re-played whenever it drops); teammates Cheer /
    ///                        HandsUp / FistPump on staggered starts; confetti; fanfare; the crowd.
    ///                        The camera arcs from low front out and up.
    ///   Shot 3 (9 - 14 s)    A high slow orbit, no cut.
    ///   Free window          The orbit continues (PodiumOrbit, drag / wheel zoom); everyone can
    ///                        move (the driver's Strikers / keeper controller, ticked here) and
    ///                        emote (B); the Captain keeps the trophy in hand - his left arm is
    ///                        held aloft under every emote and gait; Continue (the host / Captain)
    ///                        -> onContinue; clients see "waiting for the captain".
    ///
    /// Runs on the ROUND's bodies (the driver's CupBody records, alive until the flow calls
    /// EndRound), so it must begin BEFORE EndRound - the director's BeginTrophyLift does that and
    /// wraps onContinue to end the lift and the round together. This component lives under the
    /// director (not the round root) and owns only what it adds: the trophy, the confetti and the
    /// gold material, freed in OnDestroy; the camera is handed back there too.
    ///
    /// Bodies are moved only where they are simulated (the round's Local / Host authority); a
    /// client's puppets follow the host's snapshot, so the jog, the lift and the cheers reach it
    /// as positions and emote ids on the wire, exactly like the round's choreography.
    /// </summary>
    public sealed class CupTrophyLift : MonoBehaviour
    {
        // ---- text ---------------------------------------------------------------------------------
        /// <summary>The bottom hint of the free window.</summary>
        public const string HintFree = "WASD move - B emotes - drag to orbit";

        // ---- local tunables (feel) ----------------------------------------------------------------
        /// <summary>The jog into the circle: speed (m/s) and how far out the cut places the team (m).</summary>
        public const float JogSpeed = 3.2f;
        public const float JogInDistance = 10f;
        /// <summary>Teammates cluster this far from the Captain on the centre spot (m); a body counts as arrived inside this radius.</summary>
        public const float TeamRingRadius = 1.7f;
        public const float ArriveRadius = 0.3f;
        /// <summary>The AI nation walks off at this speed toward the touchline (m/s).</summary>
        public const float WalkOffSpeed = CupTuning.WalkSpeed;
        /// <summary>The referee's mark: on the centre circle's line (9.15 m), back-left of the Captain.</summary>
        public static readonly Vector3 RefereeOffset = new Vector3(-5.2f, 0f, 7.5f);
        /// <summary>Shot lengths (s): the jog, the lift arc, the high orbit; they sum to CupTuning.TrophyLiftSeconds.</summary>
        public const float ShotJog = 4.5f;
        public const float ShotLift = 4.5f;
        public static float ShotOrbit => Mathf.Max(1f, CupTuning.TrophyLiftSeconds - ShotJog - ShotLift);
        /// <summary>Teammates' cheers start this far apart and re-play after this gap (s).</summary>
        public const float CheerStagger = 0.3f;
        public const float CheerGap = 0.25f;
        /// <summary>How fast a jogging body turns (deg/s).</summary>
        public const float TurnRate = 320f;
        /// <summary>GUI depth (behind the pause menu at 0).</summary>
        public const int GuiDepth = 3;

        static readonly Color StripColour = new Color(0.03f, 0.04f, 0.07f, 0.74f);
        /// <summary>The teammates' cheers (design 8.2), cycled from a staggered start.</summary>
        public static readonly Celebration.Emote[] TeamEmotes =
        {
            Celebration.Emote.Cheer, Celebration.Emote.HandsUp, Celebration.Emote.FistPump,
        };

        // ---- read model ----------------------------------------------------------------------------
        public CupDirector Director { get; private set; }
        public CupCameraRig Rig { get; private set; }
        /// <summary>The team's bodies (the Captain included), alive while the round root stands.</summary>
        public IReadOnlyList<CupBody> Team => _team;
        public CupBody Captain { get; private set; }
        public CupBody Referee { get; private set; }
        /// <summary>The centre spot (PitchLayout's pitch centre).</summary>
        public Vector3 Centre { get; private set; }
        /// <summary>The team's nation (CupNations index).</summary>
        public int Nation { get; private set; } = -1;
        public CupTrophy Trophy { get; private set; }
        public CupConfetti Confetti { get; private set; }
        /// <summary>Seconds since Begin.</summary>
        public float Elapsed { get; private set; }
        /// <summary>The trophy is in the Captain's hands (from shot 2).</summary>
        public bool HandedOver { get; private set; }
        /// <summary>The cinematic is over and the team is free to move and emote.</summary>
        public bool FreeWindow { get; private set; }
        public bool WheelOpen => _wheelOpen;

        // ---- internals ----------------------------------------------------------------------------------
        sealed class Track
        {
            public CupBody Body;
            public Vector3 Mark;
            public Quaternion Facing = Quaternion.identity;
            public float Speed, GaitPhase;
            public bool Walking, Arrived;
            public float CheerAt;
            public int CheerIndex;
        }

        readonly List<CupBody> _team = new List<CupBody>();
        readonly List<Track> _tracks = new List<Track>();
        readonly List<Track> _aiTracks = new List<Track>();
        Track _refTrack;
        CupRoundDriver _driver;
        GameInput _input;
        Material _gold;
        Action _onContinue;
        bool _wheelOpen, _wheelWasOpen, _wasPaused;
        bool _simulates;

        static GUIStyle _titleStyle, _buttonStyle, _waitStyle;

        // ==========================================================================================
        // Build
        // ==========================================================================================

        /// <summary>
        /// Start the lift for a won Final. `team` are the round's bodies on the team's side (the
        /// Captain among them), `captain` the Captain's body, `referee` the round's referee record;
        /// the driver is read from the director for the AI nation, the local body and the input
        /// gate. Null (logged) when there is nothing to run it on - the flow then goes straight to
        /// the results.
        /// </summary>
        public static CupTrophyLift Begin(CupDirector d, IList<CupBody> team, CupBody captain, CupBody referee, CupCameraRig rig, Action onContinue)
        {
            if (d == null) { CupLog.Warn("CupTrophyLift: no director - skipped"); return null; }
            var go = new GameObject("CupTrophyLift");
            go.transform.SetParent(d.transform, false);
            var lift = go.AddComponent<CupTrophyLift>();
            try
            {
                if (!lift.Setup(d, team, captain, referee, rig, onContinue))
                {
                    Destroy(go);
                    return null;
                }
            }
            catch (Exception e)
            {
                CupLog.Error("CupTrophyLift: build failed (" + e.Message + ")");
                Destroy(go);
                return null;
            }
            return lift;
        }

        bool Setup(CupDirector d, IList<CupBody> team, CupBody captain, CupBody referee, CupCameraRig rig, Action onContinue)
        {
            Director = d;
            Rig = rig;
            _driver = d.Driver;
            _input = d.Input;
            _onContinue = onContinue;
            _simulates = _driver == null || _driver.Authority != RoundAuthority.Client;
            if (team != null)
                for (int i = 0; i < team.Count; i++)
                {
                    var b = team[i];
                    if (b == null || !b.Alive || b.Parked || b.Role == CupBodyRole.Referee) continue;
                    _team.Add(b);
                }
            if (captain == null || !captain.Alive) captain = _team.Count > 0 ? _team[0] : null;
            if (captain == null) { CupLog.Warn("CupTrophyLift: no bodies to lift with - skipped"); return false; }
            if (!_team.Contains(captain)) _team.Insert(0, captain);
            Captain = captain;
            Referee = referee != null && referee.Alive ? referee : null;
            Nation = captain.Nation >= 0 ? captain.Nation : (d.TeamNation >= 0 ? d.TeamNation : (_team[0].Nation));
            Centre = new Vector3(0f, 0f, PitchLayout.PitchCenterZ);
            _gold = CupTrophy.MakeGold();

            // Take every body back from the round's choreography (a win beat's cheers may still be
            // cycling) before this runs them; the driver's Freed windows are closed at Over.
            var choreo = _driver != null ? _driver.Choreo : null;
            for (int i = 0; i < _team.Count; i++)
            {
                var b = _team[i];
                if (choreo != null) choreo.ParkBody(b);
                CupBodies.Hold(b);
                _tracks.Add(new Track { Body = b, Mark = TeamMark(i), Facing = Quaternion.LookRotation(Vector3.back, Vector3.up) });
            }
            if (_driver != null)
            {
                var other = _driver.BodiesOn(CupSides.Other(Captain.Side));
                for (int i = 0; i < other.Count; i++)
                {
                    var b = other[i];
                    if (b == null || !b.Alive || b.Parked || b.Role == CupBodyRole.Referee) continue;
                    if (choreo != null) choreo.ParkBody(b);
                    CupBodies.Hold(b);
                    _aiTracks.Add(new Track { Body = b });
                }
            }
            if (Referee != null) _refTrack = new Track { Body = Referee };

            // The shot list (design 8.2). Every cue hangs off a shot's start so the picture and the
            // choreography can never drift apart.
            Func<Vector3> captainChest = () => Captain != null && Captain.Alive ? Captain.Pelvis.position + Vector3.up * 0.5f : Centre + Vector3.up * 1.4f;
            Func<Vector3> centreHigh = () => Centre + Vector3.up * 1.1f;
            var shots = new List<CupShot>();
            var jog = CupShot.Move(Centre + new Vector3(-1.5f, 0.8f, -9.5f), Centre + new Vector3(-2.2f, 1.0f, -6.5f), captainChest, 42f, ShotJog, cut: true);
            jog.OnStart = CueJogIn;
            shots.Add(jog);
            var lift = CupShot.Arc(Centre, 200f, 250f, 4.5f, 6.5f, 1.3f, 3.2f, captainChest, 46f, ShotLift, cut: true);
            lift.OnStart = CueHandOver;
            shots.Add(lift);
            shots.Add(CupShot.Arc(Centre, 250f, 278f, 6.5f, 7.2f, 3.2f, 4.2f, centreHigh, 50f, ShotOrbit, cut: false));
            if (Rig != null) Rig.Cinematic(shots, BeginFreeWindow);
            else
            {
                // No rig (a headless test): run the cues on a timer instead.
                CueJogIn();
                _cueTimerNoRig = true;
            }

            GameInput.CaptureCursor(false);   // nothing needs a captured cursor: the window's controls are pointer ones
            _wasPaused = PauseMenu.Paused;
            CupLog.Info("Trophy lift: " + CupText.ChampionsStrip(CupNations.IsValid(Nation) ? CupNations.Name(Nation) : "", null) + ", " + _team.Count + " in the team");
            return true;
        }

        bool _cueTimerNoRig;

        /// <summary>Where a team body ends up: the Captain on the spot, the rest on a ring behind and beside him (open toward the camera, -Z).</summary>
        Vector3 TeamMark(int index)
        {
            if (index == 0) return Centre;
            int n = _team.Count - 1;
            float step = n > 1 ? Mathf.Clamp(200f / (n - 1), 33.5f, 60f) : 0f;
            int i = index - 1;
            int k = (i + 1) / 2;
            float sign = (i % 2 == 1) ? -1f : 1f;
            float theta = sign * k * step * Mathf.Deg2Rad;
            return Centre + new Vector3(Mathf.Sin(theta) * TeamRingRadius, 0f, Mathf.Cos(theta) * TeamRingRadius);
        }

        // ==========================================================================================
        // Cues (fired at the shots' starts)
        // ==========================================================================================

        /// <summary>Shot 1's cut: the team is placed JogInDistance out on the goal side and jogs in; the AI walks off; the referee claps from the circle's edge.</summary>
        void CueJogIn()
        {
            if (!_simulates) return;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var t = _tracks[i];
                var b = t.Body;
                if (b == null || !b.Alive) continue;
                Vector3 start = t.Mark + Vector3.forward * JogInDistance;
                CupBodies.Stand(b, start, Quaternion.LookRotation(Vector3.back, Vector3.up), null);
                t.Walking = true;
                t.Arrived = false;
                t.Speed = JogSpeed;
                t.GaitPhase = 0f;
            }
            for (int i = 0; i < _aiTracks.Count; i++)
            {
                var t = _aiTracks[i];
                var b = t.Body;
                if (b == null || !b.Alive) continue;
                Vector3 g = b.GroundPos;
                t.Mark = new Vector3(PitchLayout.HalfWidth + 4f, 0f, g.z);
                t.Facing = Quaternion.LookRotation(Vector3.right, Vector3.up);
                t.Walking = true;
                t.Arrived = false;
                t.Speed = WalkOffSpeed;
                t.GaitPhase = 0f;
            }
            if (_refTrack != null && Referee != null && Referee.Alive)
            {
                Vector3 spot = Centre + RefereeOffset;
                Vector3 to = Centre - spot; to.y = 0f;
                var facing = Quaternion.LookRotation(to.normalized, Vector3.up);
                var actor = _driver != null ? _driver.RefereeActor : null;
                if (actor != null) actor.Snap(spot, facing);
                else Referee.Ragdoll.ResetTo(spot, facing);
                _refTrack.Mark = spot;
                _refTrack.Facing = facing;
            }
        }

        /// <summary>Shot 2's cut: the trophy into the Captain's hands, the lift, the cheers, the confetti, the fanfare, the crowd.</summary>
        void CueHandOver()
        {
            if (HandedOver) return;
            HandedOver = true;
            if (Captain != null && Captain.Alive)
            {
                Trophy = CupTrophy.AttachToHand(Captain.Ragdoll, _gold);
                if (_simulates)
                {
                    var t = Find(Captain);
                    if (t != null) { t.Walking = false; t.Arrived = true; CupPoses.Stop(Captain.Ragdoll, t.Facing); }
                    PlayLift();
                }
            }
            if (_simulates)
            {
                int n = 0;
                for (int i = 0; i < _tracks.Count; i++)
                {
                    var t = _tracks[i];
                    if (t.Body == Captain || t.Body == null || !t.Body.Alive) continue;
                    t.Walking = false;
                    t.Arrived = true;
                    CupPoses.Stop(t.Body.Ragdoll, t.Facing);
                    t.CheerAt = Elapsed + n * CheerStagger;
                    t.CheerIndex = n % TeamEmotes.Length;
                    n++;
                }
            }
            Confetti = CupConfetti.Create(transform, Centre, CupNations.PrimaryColor(Nation), CupNations.SecondaryColor(Nation),
                                          Director.Seed, CupSalts.Confetti);
            AudioManager.Instance?.PlayFanfare();
            CrowdCheer.Celebrate();
        }

        /// <summary>The cinematic ended: the orbit carries on, the team is free, the Continue button is up.</summary>
        void BeginFreeWindow()
        {
            if (FreeWindow) return;
            FreeWindow = true;
            float angle = Rig != null ? 278f : 0f;
            if (Rig != null) Rig.PodiumOrbit(Centre, angle, true);
            if (!_simulates) return;
            for (int i = 0; i < _tracks.Count; i++)
            {
                var b = _tracks[i].Body;
                if (b == null || !b.Alive) continue;
                _tracks[i].Walking = false;
                if (b.Celeb != null && b.Celeb.Playing && b != Captain) b.Celeb.Cancel();
                CupBodies.Free(b);
                // Movement is camera-relative: the round bound each Striker's yaw to GameCamera,
                // which a rig view disables (its yaw goes stale), so point them at the rig's camera.
                if (b.Striker != null && Rig != null) b.Striker.SetCameraYaw(CamYaw, CamPitch);
                if (b.Keeper != null)
                {
                    b.Keeper.InputLocked = false;
                    b.Keeper.HoldLine = false;
                    if (Rig != null) b.Keeper.SetLookYawSource(CamYaw);
                }
            }
        }

        float CamYaw() => Rig != null ? Rig.CamRot.eulerAngles.y : 0f;
        float CamPitch() => 0f;

        void PlayLift()
        {
            if (Captain == null || !Captain.Alive || Captain.Celeb == null) return;
            if (Captain.Celeb.Playing) Captain.Celeb.Cancel();
            Captain.Ragdoll.MoveInput = Vector3.zero;
            Captain.Celeb.Play(Celebration.Emote.TrophyLift);
        }

        // ==========================================================================================
        // Per frame
        // ==========================================================================================

        void Update()
        {
            CupEmoteWheel.KeepAlive(_wheelOpen);   // Escape ownership, republished while open
            bool paused = PauseMenu.Paused;
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;
            if (paused && _wheelOpen) { CupEmoteWheel.ForceClosed(ref _wheelOpen); SyncWheelGate(); }
            // Escape closes the WHEEL rather than opening the pause menu behind it (CupEscape.Owned
            // reads CupEmoteWheel.AnyOpen, so PauseMenu skips the same press). ForceClosed, not
            // SetOpen: the line below owns the cursor for this screen.
            else if (_wheelOpen && CupEmoteWheel.EscapePressed()) { CupEmoteWheel.ForceClosed(ref _wheelOpen); SyncWheelGate(); }
            if (_wheelWasOpen && !_wheelOpen) { GameInput.CaptureCursor(false); SyncWheelGate(); }
            _wheelWasOpen = _wheelOpen;

            if (PauseMenu.Frozen) return;
            float dt = Time.deltaTime;
            Elapsed += dt;

            if (_cueTimerNoRig)
            {
                if (!HandedOver && Elapsed >= ShotJog) CueHandOver();
                if (!FreeWindow && Elapsed >= CupTuning.TrophyLiftSeconds) BeginFreeWindow();
            }

            if (_simulates)
            {
                // Walks (the jog in, the AI walking off).
                for (int i = 0; i < _tracks.Count; i++) TickWalk(_tracks[i], dt);
                for (int i = 0; i < _aiTracks.Count; i++) TickWalk(_aiTracks[i], dt);

                // The referee applauds from the circle's edge for as long as the picture lasts.
                if (Referee != null && Referee.Alive && Referee.Celeb != null && !Referee.Celeb.Playing && !FreeWindow)
                {
                    Referee.Ragdoll.MoveInput = Vector3.zero;
                    Referee.Celeb.Play(Celebration.Emote.Clap);
                }

                if (HandedOver && !FreeWindow)
                {
                    // The Captain lifts, re-played whenever it drops; the teammates cheer on their stagger.
                    if (Captain != null && Captain.Alive && Captain.Celeb != null && !Captain.Celeb.Playing) PlayLift();
                    for (int i = 0; i < _tracks.Count; i++)
                    {
                        var t = _tracks[i];
                        var b = t.Body;
                        if (b == Captain || b == null || !b.Alive || b.Celeb == null) continue;
                        if (b.Celeb.Playing) continue;
                        if (Elapsed < t.CheerAt) continue;
                        b.Ragdoll.MoveInput = Vector3.zero;
                        b.Celeb.Play(TeamEmotes[t.CheerIndex % TeamEmotes.Length]);
                        t.CheerIndex++;
                        t.CheerAt = Elapsed + CheerGap;
                    }
                }

                if (FreeWindow) TickFreeBodies();
            }

            // B toggles the local body's wheel in the free window.
            if (FreeWindow && !paused && _input != null && _input.EmotePressed && LocalBody() != null) SetWheel(!_wheelOpen);
        }

        void LateUpdate()
        {
            if (PauseMenu.Frozen || !_simulates) return;
            float dt = Time.deltaTime;
            // The jog and walk gaits, re-applied after every controller and Celebration ran.
            for (int i = 0; i < _tracks.Count; i++) GaitOf(_tracks[i], dt);
            for (int i = 0; i < _aiTracks.Count; i++) GaitOf(_aiTracks[i], dt);
            // The Captain keeps the trophy aloft in his left hand under every other emote and gait.
            if (HandedOver && Captain != null && Captain.Alive)
            {
                var c = Captain.Celeb;
                // Past its own rise only: TrophyLift eases in from a hanging arm over the first
                // 0.35 of its length and is re-Played from zero every time it ends (PlayLift in
                // Update), so without this window the trophy dips to the hip and climbs again once
                // per loop. Same rule as CupPodium.LateUpdate - see the note there.
                bool lifting = c != null && c.Playing && c.CurrentEmote == Celebration.Emote.TrophyLift
                            && c.Progress01 >= 0.35f;
                if (!lifting)
                {
                    Captain.Ragdoll.SetPoseOverride(Bone.UpperArmL, CupPodium.HoldUpperArmL);
                    Captain.Ragdoll.SetPoseOverride(Bone.ForearmL, CupPodium.HoldForearmL);
                }
            }
        }

        void TickWalk(Track t, float dt)
        {
            if (!t.Walking) return;
            var b = t.Body;
            if (b == null || !b.Alive) { t.Walking = false; return; }
            float left = CupPoses.Steer(b.Ragdoll, t.Mark, t.Speed, TurnRate, dt);
            if (left <= ArriveRadius)
            {
                t.Walking = false;
                t.Arrived = true;
                CupPoses.Stop(b.Ragdoll, t.Facing);
            }
        }

        void GaitOf(Track t, float dt)
        {
            if (!t.Walking) return;
            var b = t.Body;
            if (b == null || !b.Alive) return;
            var rag = b.Ragdoll;
            rag.ClearPoseOverrides();
            CupPoses.WalkGait(rag, ref t.GaitPhase, dt, CupPoses.GaitAmount(rag.MoveInput.magnitude));
        }

        /// <summary>The free window's bodies: each human's controller off its input (the driver does not tick them at Over), unless an emote owns it.</summary>
        void TickFreeBodies()
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                var b = _tracks[i].Body;
                if (b == null || !b.Alive || !b.Freed) continue;
                bool emoting = b.Celeb != null && b.Celeb.Playing;
                if (!emoting && b.NetInput != null && b.Celeb != null)
                {
                    // A remote human's pick from its wire input (the local player's comes from the wheel).
                    int eid = b.NetInput.EmoteId;
                    if (eid >= 0 && eid != 255 && eid <= (int)Celebration.Emote.WhistleRaise)
                    {
                        if (b != Captain || CupPodium.OnWheel((Celebration.Emote)eid)) { b.Celeb.Play((Celebration.Emote)eid); emoting = true; }
                    }
                }
                if (emoting) continue;
                if (b.Striker != null) b.Striker.Tick();
                else if (b.Keeper != null) { b.Keeper.InputLocked = false; b.Keeper.HoldLine = false; b.Keeper.Tick(); }
            }
        }

        /// <summary>The local human's body in the team (null for a spectator).</summary>
        CupBody LocalBody()
        {
            var d = Director;
            if (d == null) return null;
            for (int i = 0; i < _team.Count; i++)
                if (_team[i] != null && _team[i].Alive && _team[i].IsHuman && _team[i].Slot == d.LocalSlot) return _team[i];
            return null;
        }

        Track Find(CupBody b)
        {
            for (int i = 0; i < _tracks.Count; i++) if (_tracks[i].Body == b) return _tracks[i];
            return null;
        }

        void SetWheel(bool open)
        {
            if (open == _wheelOpen) return;
            _wheelOpen = open;
            GameInput.CaptureCursor(false);
            SyncWheelGate();
        }

        /// <summary>The driver's local input gate (CupLocalInput) idles the device while the wheel is up.</summary>
        void SyncWheelGate()
        {
            if (_driver != null) _driver.EmoteWheelOpen = _wheelOpen;
        }

        // ==========================================================================================
        // UI (own OnGUI): the CHAMPIONS strip from the hand-over, the free window's hint and Continue
        // ==========================================================================================

        void OnGUI()
        {
            Styles();
            GUI.depth = GuiDepth;
            Hud.Begin();
            float w = Hud.W, h = Hud.H;
            bool paused = PauseMenu.Paused;
            Action fire = null;

            if (HandedOver)
            {
                UITheme.Fill(new Rect(0f, 0f, w, 78f), StripColour);
                UITheme.Fill(new Rect(0f, 78f, w, 2f), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.8f));
                string strip = CupText.ChampionsStrip(CupNations.IsValid(Nation) ? CupNations.Name(Nation) : "", null);
                UITheme.Shadowed(new Rect(0f, 8f, w, 62f), strip, _titleStyle, UITheme.Gold, 0.8f, 3f);
            }
            UITheme.Hint(new Rect(0f, h - 30f, w, 22f), FreeWindow ? HintFree : "");

            // Continue: the host's (Captain's); a client sees the waiting line. Allocated every pass.
            bool authority = Director == null || Director.IsAuthority;
            bool show = FreeWindow && !paused && !_wheelOpen;
            float bw = 170f, bh = 48f, by = h - 100f;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = show && authority;
            var r = show && authority ? new Rect((w - bw) * 0.5f, by, bw, bh) : new Rect(-1000f, -1000f, bw, bh);
            if (UITheme.Button(r, CupText.Continue, _buttonStyle) && show && authority) fire = _onContinue;
            GUI.enabled = wasEnabled;
            if (show && !authority)
                UITheme.Label(new Rect(0f, by + 12f, w, 22f), CupText.WaitingForCaptain, _waitStyle);

            if (_wheelOpen && !paused)
            {
                var local = LocalBody();
                var pages = local == Captain ? CupPodium.WheelPages : Celebration.Pages;
                bool open = _wheelOpen;
                // A client's body is a puppet of the host's snapshots: the pick rides the input
                // frame to the host (SetEmotePick) and comes back posed; no local Celebration.Play
                // on kinematic bones. The simulating peer plays it at once for instant feedback.
                CupEmoteWheel.Draw(_simulates && local != null ? local.Celeb : null, _input, pages, ref open);
                _wheelOpen = open;
            }

            Hud.End();
            fire?.Invoke();
        }

        static void Styles()
        {
            if (_titleStyle != null) return;
            _titleStyle = new GUIStyle { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            UIFont.Heavy(_titleStyle);
            _titleStyle.normal.textColor = UITheme.Gold;
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            _waitStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = UITheme.Dim } };
        }

        // ==========================================================================================
        // Teardown
        // ==========================================================================================

        /// <summary>End the lift now (the flow's Continue / a leave): frees what it added; the bodies are the round's.</summary>
        public void End()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_driver != null) _driver.EmoteWheelOpen = false;
            if (Trophy != null) { Trophy.Destroy(); Trophy = null; }
            if (Confetti != null) { Destroy(Confetti.gameObject); Confetti = null; }
            if (_gold != null) Destroy(_gold);
            _gold = null;
            _tracks.Clear();
            _aiTracks.Clear();
            _team.Clear();
            if (Rig != null) Rig.Release();
        }
    }
}
