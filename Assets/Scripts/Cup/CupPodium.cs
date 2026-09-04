using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The podium (design 8.1, Solo and Head to Head): built on the real pitch at the penalty spot
    /// once the Final's round objects are gone (the arena, crowd and stadium persist, so the crowd
    /// is the backdrop). A stepped stone dais with gold trim, the champion on it in the nation
    /// kit lifting the trophy (TrophyLift re-played whenever it drops, an emote wheel with ONE
    /// curated page of standing emotes that leave the trophy arm alone), the losers as static
    /// display bodies in a horseshoe around the dais looking down, the rig's slow orbit with a
    /// drag takeover and wheel zoom, confetti in the nation's two kit colours, the fanfare and the
    /// crowd, a CHAMPIONS strip with the buttons after PodiumButtonsDelay, then the CUP SUMMARY
    /// through <c>onContinue</c> (the director's ContinueFromResults).
    ///
    /// Opens with a short hand-over beat: the trophy stands on the dais at the champion's feet
    /// and he looks down at it; at <see cref="HandOverSeconds"/> a cut (the orbit restarts from a
    /// new angle) puts it in his hands and the lift begins. The free-standing and the hand-held
    /// trophy are the two <see cref="CupTrophy"/> variants.
    ///
    /// Ownership: everything is built under the root the director hands over (its PodiumRoot,
    /// destroyed by EndPodium), and OnDestroy frees what Unity does not: the dais and trim meshes,
    /// the stone and gold materials, the kit cache (nation torsos, limb materials), the trophy
    /// (mesh via GeneratedMeshOwner), the confetti (its own OnDestroy), and it hands the camera
    /// back (Rig.Release) and un-ignores the ball. Bodies hang under the root and die with it.
    ///
    /// Cursor: the podium owns it (the director does not re-assert on unpause in this phase) and
    /// keeps it FREE - the buttons and the wheel need a pointer, and the orbit drag works with a
    /// free cursor (CupCameraRig.PodiumFreeCursorDrag). It re-frees after an unpause and after a
    /// wheel pick (CupEmoteWheel captures on close).
    ///
    /// MP (the net agent): <see cref="Bodies"/> lists the winner first, then the losers, each a
    /// CupBody with its VirtualSlot; only the WINNER emotes (<see cref="WinnerEmoteId"/> /
    /// <see cref="WinnerEmotePhase"/> for the snapshot, <see cref="PlayWinnerEmote"/> for a remote
    /// champion's pick). The losers are static display bodies with nothing to sync.
    /// </summary>
    public sealed class CupPodium : MonoBehaviour
    {
        // ---- text (design 8.1; exact strings) ---------------------------------------------------
        /// <summary>The bottom hint under the podium.</summary>
        public const string HintText = "B emotes - drag to orbit - Esc";

        // ---- local tunables (feel) ----------------------------------------------------------------
        /// <summary>The trophy stands on the dais this long before the cut that puts it in the champion's hands.</summary>
        public const float HandOverSeconds = 1.4f;
        /// <summary>The orbit angle the hand-over cut restarts from (deg; the podium opens at CupCameraRig.PodiumStartAngle).</summary>
        public const float HandOverCutAngle = 150f;
        /// <summary>Losers stand on a circle of this radius about the dais centre (m).</summary>
        public const float LoserRadius = 2.6f;
        /// <summary>The horseshoe: the angular step between neighbours is 200 / (n - 1) deg, clamped to this range.</summary>
        public const float LoserSpanMax = 200f;
        public const float LoserStepMin = 33.5f;   // 1.5 m chord at LoserRadius: shoulder to shoulder with room
        public const float LoserStepMax = 60f;
        /// <summary>A loser's facing is jittered this much either way so a line never reads as stamped (deg).</summary>
        public const float LoserFacingJitter = 8f;
        /// <summary>The dais has this many steps (equal rises to CupTuning.PedestalHeight) and each tread is this wide (m).</summary>
        public const int DaisSteps = 3;
        public const float DaisTread = 0.14f;
        /// <summary>The gold trim: tube radius, and how far the ring's centre sits below each tread's edge (m).</summary>
        public const float TrimTube = 0.018f;
        public const float TrimDrop = 0.012f;
        /// <summary>Where the trophy stands during the hand-over beat: this far in front of the champion's feet on the top tread (m).</summary>
        public const float StandTrophyForward = 0.34f;
        /// <summary>GUI depth: behind the pause menu (0) and the cards (-1), in front of the round HUD (5).</summary>
        public const int GuiDepth = 3;

        static readonly Color StoneColour = new Color(0.62f, 0.60f, 0.56f);
        static readonly Color StripColour = new Color(0.03f, 0.04f, 0.07f, 0.74f);

        /// <summary>
        /// The champion's left-arm HOLD while a wheel emote plays: the trophy arm stays straight
        /// up (a touch forward) so the trophy is held aloft in the left hand while the right arm
        /// waves, salutes or points. Re-applied every LateUpdate, after Celebration.Update has
        /// cleared the overrides and posed only the emote's own bones - which is why the curated
        /// page holds only emotes that never touch UpperArmL / ForearmL.
        /// </summary>
        public static readonly Vector3 HoldUpperArmL = new Vector3(-10f, 0f, 165f);
        public static readonly Vector3 HoldForearmL = Vector3.zero;

        /// <summary>
        /// The ONE wheel page of the podium: "Lift" re-plays the trophy lift; the rest are standing
        /// emotes verified against EmotePose.Apply to leave the LEFT arm alone (Wave: UpperArmR /
        /// ForearmR; Salute: UpperArmR / ForearmR / Torso; Point: UpperArmR / ForearmR / Torso /
        /// Head; Thinker: UpperArmR / ForearmR / Torso / Head). Dropped from the design's
        /// candidates: FistPump (sets UpperArmL (0, 0, 20) and ForearmL), Bow (UpperArmL (30k, 0,
        /// 25)) and Cheer (UpperArmL 120 + 45 pump) all drive the trophy arm; the physics emotes
        /// and the sinking ones (PushUps, Twerk, KneeSlide) are out by design.
        /// </summary>
        public static readonly (Celebration.Emote e, string name)[][] WheelPages =
        {
            new (Celebration.Emote, string)[]
            {
                (Celebration.Emote.TrophyLift, "Lift"),
                (Celebration.Emote.Wave,       "Wave"),
                (Celebration.Emote.Salute,     "Salute"),
                (Celebration.Emote.Point,      "Point"),
                (Celebration.Emote.Thinker,    "Thinker"),
            },
        };

        // ---- read model ----------------------------------------------------------------------------
        public CupDirector Director { get; private set; }
        public CupCameraRig Rig { get; private set; }
        public GameInput Input { get; private set; }
        public BallController Ball { get; private set; }
        /// <summary>The dais centre on the turf (the penalty spot).</summary>
        public Vector3 Centre { get; private set; }
        public int ChampionEntrant { get; private set; } = -1;
        /// <summary>CupNations index of the champion's kit.</summary>
        public int ChampionNation { get; private set; } = -1;
        /// <summary>The champion's net slot, -1 for an AI champion.</summary>
        public int ChampionSlot { get; private set; } = -1;
        public string ChampionName { get; private set; }
        /// <summary>The local player is the champion (the wheel is theirs).</summary>
        public bool ChampionIsLocal => ChampionSlot >= 0 && Director != null && ChampionSlot == Director.LocalSlot;
        /// <summary>The champion's body (Bodies[0]).</summary>
        public CupBody Winner { get; private set; }
        /// <summary>Every body: the winner first, then the losers in horseshoe order (the beaten finalist at the centre).</summary>
        public IReadOnlyList<CupBody> Bodies => _bodies;
        /// <summary>The trophy in the champion's hand (null until the hand-over cut).</summary>
        public CupTrophy Trophy { get; private set; }
        public CupConfetti Confetti { get; private set; }
        /// <summary>Seconds since the podium was built (Time.deltaTime: a Solo pause freezes it).</summary>
        public float Elapsed { get; private set; }
        /// <summary>The trophy is in the champion's hands (after the hand-over cut).</summary>
        public bool HandedOver { get; private set; }
        /// <summary>The buttons are showing (CupTuning.PodiumButtonsDelay elapsed).</summary>
        public bool ButtonsUp => Elapsed >= CupTuning.PodiumButtonsDelay;
        public bool WheelOpen => _wheelOpen;
        /// <summary>For the snapshot: the winner's emote id (255 none) and phase.</summary>
        public int WinnerEmoteId => Winner != null && Winner.Celeb != null && Winner.Celeb.Playing ? (int)Winner.Celeb.CurrentEmote : 255;
        public float WinnerEmotePhase => Winner != null && Winner.Celeb != null && Winner.Celeb.Playing ? Winner.Celeb.Progress01 : 0f;

        // ---- internals ----------------------------------------------------------------------------------
        readonly List<CupBody> _bodies = new List<CupBody>();
        Action _onContinue;
        Transform _root;
        CupKitCache _kits;
        Material _stone, _gold;
        Mesh _daisMesh, _trimMesh;
        CupTrophy _standTrophy;
        SeededRng _rng;
        bool _wheelOpen, _wheelWasOpen, _wasPaused;
        int _nextVirtual = CupRoundState.AiBodyIdBase;

        /// <summary>One button on the podium's row: the label, what it does, and whether it is the
        /// destructive one (UITheme.Button's `bad` tint).</summary>
        struct Btn
        {
            public string Label;
            public Action Act;
            public bool Bad;
        }

        // The button row is a MODEL, latched in Update and only iterated by OnGUI - the shape
        // CupLobbyUI._rows and CupResultsUI._tabs already use. Building it inside the GUI pass
        // allocated three lists plus a closure each on EVERY pass (several per frame) on the one
        // cup screen that is deliberately a long dwell, and derived the CONTROL COUNT from live
        // state inside the pass, which is exactly what IMGUI forbids.
        readonly List<Btn> _buttons = new List<Btn>();
        bool _rowSolo, _rowAuthority, _rowBuilt;

        static GUIStyle _titleStyle, _buttonStyle, _waitStyle;

        // ==========================================================================================
        // Build
        // ==========================================================================================

        /// <summary>
        /// Build the podium for the bracket's champion (the director's TryBeginPodium calls this).
        /// `root` is the director's fresh PodiumRoot (everything goes under it); `onContinue` is the
        /// Continue button (ContinueFromResults -> the CUP SUMMARY). Null (logged) when there is no
        /// champion to crown - the flow then goes straight to the results.
        /// </summary>
        public static CupPodium Begin(CupDirector d, Transform root, CupCameraRig rig, CupHud hud, GameInput input,
                                      Camera cam, GameCamera gameCam, BallController ball, Action onContinue)
        {
            if (d == null || d.Bracket == null) { CupLog.Warn("CupPodium: no director / bracket - no podium"); return null; }
            int champ = d.Bracket.Champion;
            if (champ < 0) champ = d.LocalEntrant;   // the Final's result should be in; fall back to the local winner
            if (!d.Bracket.IsValidEntrant(champ)) { CupLog.Warn("CupPodium: no champion to crown - no podium"); return null; }

            var parent = root != null ? root : d.transform;
            var go = new GameObject("CupPodium");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<CupPodium>();
            try
            {
                p.Setup(d, parent, rig, input, ball, champ, onContinue);
            }
            catch (Exception e)
            {
                CupLog.Error("CupPodium: build failed (" + e.Message + ")");
                Destroy(go);
                return null;
            }
            return p;
        }

        void Setup(CupDirector d, Transform root, CupCameraRig rig, GameInput input, BallController ball, int champ, Action onContinue)
        {
            Director = d;
            _root = root;
            Rig = rig;
            Input = input;
            Ball = ball;
            _onContinue = onContinue;
            _rng = new SeededRng(d.Seed).Fork(CupSalts.Podium);
            _kits = new CupKitCache();
            _gold = CupTrophy.MakeGold();
            _stone = Make.Mat(StoneColour, 0.22f, 0f);
            Centre = CupSpots.Ground(CupSpots.PenaltySpot);

            var e = d.Bracket.Entrants[champ];
            ChampionEntrant = champ;
            ChampionNation = e.NationIndex;
            ChampionSlot = e.IsHuman ? e.HumanSlot : -1;
            ChampionName = e.IsHuman ? e.HumanName : null;
            if (ChampionSlot >= 0 && string.IsNullOrEmpty(ChampionName)) ChampionName = CupBodies.NameFor(d, ChampionSlot);

            // The ball sits on the penalty spot - where the dais goes. Park it on the touchline side
            // of the pitch, out of every frame, and make it ignore every podium body.
            if (Ball != null) Ball.ResetTo(new Vector3(-(SimConfig.FieldWidth * 0.5f - 1.5f), SimConfig.BallRadius, Centre.z - 4f));

            BuildDais();
            BuildWinner();
            BuildLosers();

            Confetti = CupConfetti.Create(_root, Centre, CupNations.PrimaryColor(ChampionNation), CupNations.SecondaryColor(ChampionNation),
                                          d.Seed, CupSalts.Confetti);

            // Audio (design 8.1): the referee's triple already blew in the round; here the fanfare
            // (which plays the full goal celebration underneath) and the crowd's own celebration.
            AudioManager.Instance?.PlayFanfare();
            CrowdCheer.Celebrate();

            if (Rig != null) Rig.PodiumOrbit(Centre);
            GameInput.CaptureCursor(false);
            _wasPaused = PauseMenu.Paused;
            CupLog.Info("Podium: " + CupText.ChampionsStrip(CupNations.Name(ChampionNation), ChampionName) + ", " + (_bodies.Count - 1) + " loser(s)");
        }

        /// <summary>
        /// The stepped dais: DaisSteps stacked cylinders (each DaisTread narrower than the one
        /// below, rising PedestalHeight / DaisSteps) in stone, a gold trim ring proud of every
        /// tread's edge, and a convex MeshCollider so the champion's ragdoll has a floor to stand
        /// on (the ground probe accepts any static collider facing up).
        /// </summary>
        void BuildDais()
        {
            float rise = CupTuning.PedestalHeight / DaisSteps;
            float baseR = CupTuning.PedestalDiameter * 0.5f;
            var stone = new List<Mesh>();
            var trim = new List<Mesh>();
            for (int i = 0; i < DaisSteps; i++)
            {
                float r = baseR - DaisTread * i;
                float y0 = rise * i;
                var cyl = MeshGen.Cylinder(r, r, rise, 40, capBottom: i == 0, capTop: true);
                MeshGen.Transform(cyl, new Vector3(0f, y0, 0f));
                stone.Add(cyl);
                var ring = MeshGen.Torus(r + 0.005f, TrimTube, 40, 10);
                MeshGen.Transform(ring, new Vector3(0f, y0 + rise - TrimDrop, 0f));
                trim.Add(ring);
            }
            _daisMesh = MeshGen.Combine(stone.ToArray());
            _daisMesh.name = "CupDais";
            _trimMesh = MeshGen.Combine(trim.ToArray());
            _trimMesh.name = "CupDaisTrim";

            var dais = new GameObject("CupDais");
            dais.transform.SetParent(_root, false);
            dais.transform.position = Centre;
            dais.AddComponent<MeshFilter>().sharedMesh = _daisMesh;
            dais.AddComponent<MeshRenderer>().sharedMaterial = _stone;
            var col = dais.AddComponent<MeshCollider>();
            col.sharedMesh = _daisMesh;
            col.convex = true;

            var trimGo = new GameObject("CupDaisTrim");
            trimGo.transform.SetParent(dais.transform, false);
            trimGo.AddComponent<MeshFilter>().sharedMesh = _trimMesh;
            trimGo.AddComponent<MeshRenderer>().sharedMaterial = _gold;
        }

        /// <summary>
        /// The champion on the top tread facing -Z (the pitch; the orbit opens front-left, so his
        /// front is to the camera): the local human's own look and build, a remote human's roster
        /// look, or a plain AI body; the nation kit on every one. The trophy first stands at his
        /// feet for the hand-over beat.
        /// </summary>
        void BuildWinner()
        {
            var d = Director;
            var feet = Centre + Vector3.up * CupTuning.PedestalHeight;
            var facing = Quaternion.LookRotation(Vector3.back, Vector3.up);
            var b = new CupBody
            {
                Side = CupSide.A,
                Slot = ChampionSlot,
                Nation = ChampionNation,
                Role = CupBodyRole.Lineup,
                Active = true,
                LineupMark = feet,
                LineupFacing = facing,
                Name = ChampionSlot >= 0 ? ChampionName : (CupNations.IsValid(ChampionNation) ? CupNations.Name(ChampionNation) : "AI"),
                VirtualSlot = ChampionSlot >= 0 ? ChampionSlot : _nextVirtual++,
            };
            var go = new GameObject("CupPodium Winner " + b.Name);
            go.transform.SetParent(_root, true);
            b.Go = go;
            Material torso = _kits.Nation(ChampionNation, d.Torso);
            if (ChampionSlot >= 0)
            {
                var look = CupBodies.LookFor(ChampionSlot, d.LocalSlot);
                b.Ragdoll = CupBodies.BuildHuman(go, feet, facing, torso, _kits.Limb(look.Skin), false, look, ChampionSlot == d.LocalSlot);
            }
            else
            {
                b.Ragdoll = CupBodies.BuildAi(go, feet, facing, torso, _kits.Limb(LimbColour(ChampionNation)), false);
            }
            b.Celeb = go.AddComponent<Celebration>();
            b.Celeb.Init(b.Ragdoll);
            // Live and self-standing: balance on, locomotion on with zero input so the velocity
            // steer holds him on the top tread (the lineup's trick) while he sways and lifts.
            var rag = b.Ragdoll;
            rag.UprightLock = true;
            rag.BalanceEnabled = true;
            rag.LocomotionEnabled = true;
            rag.MoveInput = Vector3.zero;
            if (Ball != null) Ball.IgnoreBody(rag, true);
            Winner = b;
            _bodies.Add(b);

            // The free-standing trophy at his feet, on the tread's front edge, for the hand-over beat.
            _standTrophy = CupTrophy.Standing(_root, feet + Vector3.back * StandTrophyForward, Quaternion.identity, _gold);
        }

        /// <summary>
        /// The horseshoe (design 8.1): Head to Head = every other human still connected, plus AI
        /// bodies of the beaten finalist and the semi-finalists to make at least PodiumMinLosers;
        /// Solo = the beaten finalist, both semi-finalists and the four quarter-finalists (seven).
        /// Ordered by the stage they reached so the finalist stands at the centre behind the dais
        /// and the earliest losers at the ends; the arc opens toward -Z, the camera's side. Each is
        /// a kinematic display body (no solver cost) in one of the three looking-down poses, rolled
        /// from the Podium stream.
        /// </summary>
        void BuildLosers()
        {
            var d = Director;
            var cands = LoserCandidates();
            int n = cands.Count;
            if (n == 0) return;
            float step = n > 1 ? Mathf.Clamp(LoserSpanMax / (n - 1), LoserStepMin, LoserStepMax) : 0f;
            for (int i = 0; i < n; i++)
            {
                var c = cands[i];
                // Alternate outward from the centre: 0, -1, +1, -2, +2 ... steps.
                int k = (i + 1) / 2;
                float sign = (i % 2 == 1) ? -1f : 1f;
                float theta = sign * k * step * Mathf.Deg2Rad;
                Vector3 pos = Centre + new Vector3(Mathf.Sin(theta) * LoserRadius, 0f, Mathf.Cos(theta) * LoserRadius);
                Vector3 toCentre = Centre - pos;
                toCentre.y = 0f;
                float jitter = _rng.Range(-LoserFacingJitter, LoserFacingJitter);
                var facing = Quaternion.Euler(0f, jitter, 0f) * Quaternion.LookRotation(toCentre.normalized, Vector3.up);
                int variant = _rng.Range(0, 3);

                var b = new CupBody
                {
                    Side = CupSide.B,
                    Slot = c.slot,
                    Nation = c.nation,
                    Role = CupBodyRole.Lineup,
                    Active = true,
                    LineupIndex = i,
                    LineupMark = pos,
                    LineupFacing = facing,
                    Name = c.name,
                    VirtualSlot = c.slot >= 0 ? c.slot : _nextVirtual++,
                };
                var go = new GameObject("CupPodium Loser " + b.Name);
                go.transform.SetParent(_root, true);
                b.Go = go;
                Material torso = _kits.Nation(c.nation, d.Torso);
                if (c.slot >= 0)
                {
                    var look = CupBodies.LookFor(c.slot, d.LocalSlot);
                    b.Ragdoll = CupBodies.BuildHuman(go, pos, facing, torso, _kits.Limb(look.Skin), false, look, c.slot == d.LocalSlot);
                }
                else
                {
                    b.Ragdoll = CupBodies.BuildAi(go, pos, facing, torso, _kits.Limb(LimbColour(c.nation)), false);
                }
                var rag = b.Ragdoll;
                rag.BecomeDisplayBody();
                rag.DisplayPose(pos, facing, 0f, 0f, CupPoses.LoserPose(variant));
                // A posed puppet needs no solver - but BecomeDisplayBody stops NONE of the three
                // components that keep integrating (the menu vignettes' rule): ActiveRagdoll's own
                // FixedUpdate, HairSim and AnatomySim each run off their own clock. A human loser
                // carries both cosmetic sims (BuildHuman passes an appearance), and in Head to Head
                // every other connected human stands here, so leaving them live costs up to seven
                // full cosmetic solves behind a static pose - on the one screen already paying for
                // 200 confetti quads and an orbiting camera.
                rag.enabled = false;
                FreezeSims(rag);
                if (Ball != null) Ball.IgnoreBody(rag, true);
                _bodies.Add(b);
            }
        }

        /// <summary>
        /// The cosmetic sims a display body still runs on its own clock (see the menu scenes'
        /// MenuScene.SetSims): hair has no teleport handling and would drift off a posed body, and
        /// the anatomy sim integrates regardless of whether the bones move.
        /// </summary>
        static void FreezeSims(ActiveRagdoll rag)
        {
            if (rag == null) return;
            var hair = rag.GetComponentsInChildren<HairSim>(true);
            for (int i = 0; i < hair.Length; i++) if (hair[i] != null) hair[i].enabled = false;
            var anat = rag.GetComponentsInChildren<AnatomySim>(true);
            for (int i = 0; i < anat.Length; i++) if (anat[i] != null) anat[i].enabled = false;
        }

        struct Cand
        {
            public int entrant, slot, nation, stage;
            public string name;
        }

        /// <summary>The losers to show, best stage first (humans before AI on a shared stage).</summary>
        List<Cand> LoserCandidates()
        {
            var d = Director;
            var b = d.Bracket;
            var list = new List<Cand>();
            var seen = new HashSet<int>();
            if (d.Style != CupStyle.Solo)
            {
                // Every other human still connected, in their nation kit (their bracket entrant).
                var players = d.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (!p.Active || p.Slot == ChampionSlot || p.Entrant < 0 || p.Entrant == ChampionEntrant) continue;
                    if (!b.IsValidEntrant(p.Entrant) || seen.Contains(p.Entrant)) continue;
                    seen.Add(p.Entrant);
                    list.Add(new Cand { entrant = p.Entrant, slot = p.Slot, nation = b.Entrants[p.Entrant].NationIndex, stage = (int)b.StageReached(p.Entrant), name = p.Name });
                }
            }
            // AI fillers from the bracket, best stage first: the beaten finalist, the two
            // semi-finalists, the four quarter-finalists. Solo takes all seven; Head to Head only
            // enough to reach PodiumMinLosers.
            var fill = new List<int>();
            AddLosersOf(b, CupStage.Final, fill);
            AddLosersOf(b, CupStage.SemiFinal, fill);
            AddLosersOf(b, CupStage.QuarterFinal, fill);
            for (int i = 0; i < fill.Count; i++)
            {
                int e = fill[i];
                if (e < 0 || e == ChampionEntrant || seen.Contains(e) || !b.IsValidEntrant(e)) continue;
                bool wantMore = d.Style == CupStyle.Solo || list.Count < CupTuning.PodiumMinLosers;
                if (!wantMore) break;
                seen.Add(e);
                var ent = b.Entrants[e];
                list.Add(new Cand { entrant = e, slot = -1, nation = ent.NationIndex, stage = (int)b.StageReached(e), name = ent.DisplayName });
            }
            // Best stage first; a human before an AI on the same stage; the list order after that
            // (stable), so the layout is the same on every peer.
            var ordered = new List<Cand>(list);
            for (int i = 1; i < ordered.Count; i++)
            {
                var x = ordered[i];
                int j = i - 1;
                while (j >= 0 && Later(x, ordered[j])) { ordered[j + 1] = ordered[j]; j--; }
                ordered[j + 1] = x;
            }
            return ordered;
        }

        static bool Later(Cand a, Cand b)
        {
            if (a.stage != b.stage) return a.stage > b.stage;
            return a.slot >= 0 && b.slot < 0;
        }

        static void AddLosersOf(CupBracket b, CupStage stage, List<int> into)
        {
            var rounds = b.RoundsOf(stage);
            for (int i = 0; i < rounds.Count; i++)
            {
                var r = rounds[i];
                if (r != null && r.Done) into.Add(r.LoserEntrant);
            }
        }

        /// <summary>An AI body's shorts and socks: the nation's second kit colour, the project blue when it has none.</summary>
        static Color LimbColour(int nation)
            => nation >= 0 && CupNations.IsValid(nation) ? CupNations.SecondaryColor(nation) : CupBodies.AiLimbFallback;

        // ==========================================================================================
        // Per frame
        // ==========================================================================================

        void Update()
        {
            CupEmoteWheel.KeepAlive(_wheelOpen);   // Escape ownership, republished while open
            RefreshButtons();
            bool paused = PauseMenu.Paused;
            // PauseMenu.Resume re-captures the cursor unconditionally; the podium wants it free.
            if (_wasPaused && !paused) GameInput.CaptureCursor(false);
            _wasPaused = paused;
            if (paused && _wheelOpen)
            {
                CupEmoteWheel.ForceClosed(ref _wheelOpen);   // the menu owns the cursor now
                _wheelWasOpen = false;
            }
            // Escape closes the WHEEL rather than opening the pause menu behind it (CupEscape.Owned
            // reads CupEmoteWheel.AnyOpen, so PauseMenu skips the same press). ForceClosed, not
            // SetOpen: the podium keeps a free cursor, and the line below would only undo a capture.
            if (!paused && _wheelOpen && CupEmoteWheel.EscapePressed()) CupEmoteWheel.ForceClosed(ref _wheelOpen);
            // A pick closed the wheel inside its draw and CAPTURED the cursor (its own contract):
            // free it again, the podium is a pointer screen.
            if (_wheelWasOpen && !_wheelOpen) GameInput.CaptureCursor(false);
            _wheelWasOpen = _wheelOpen;

            if (PauseMenu.Frozen) return;
            Elapsed += Time.deltaTime;

            if (!HandedOver && Elapsed >= HandOverSeconds) HandOver();

            if (Winner != null && Winner.Alive)
            {
                var rag = Winner.Ragdoll;
                rag.MoveInput = Vector3.zero;
                // The default: the lift, re-played whenever it drops (design 8.1).
                if (HandedOver && Winner.Celeb != null && !Winner.Celeb.Playing) Winner.Celeb.Play(Celebration.Emote.TrophyLift);
            }

            // B toggles the wheel for the local champion once the trophy is in hand.
            if (!paused && HandedOver && ChampionIsLocal && Input != null && Input.EmotePressed && Winner != null && Winner.Alive)
                SetWheel(!_wheelOpen);
        }

        void LateUpdate()
        {
            if (PauseMenu.Frozen) return;
            if (Winner == null || !Winner.Alive) return;
            var rag = Winner.Ragdoll;
            var celeb = Winner.Celeb;
            if (!HandedOver)
            {
                // The hand-over beat: he looks down at the trophy at his feet.
                rag.ClearPoseOverrides();
                rag.SetPoseOverride(Bone.Head, new Vector3(30f, 0f, 0f));
                rag.SetPoseOverride(Bone.Torso, new Vector3(8f, 0f, 0f));
                return;
            }
            // A wheel emote owns the right arm and the body; the trophy arm is ours, held aloft.
            // TrophyLift is EXCLUDED only once it is past its own rise: it is a one-shot emote with
            // an ease-in from a hanging arm (k = SmoothStep(p / 0.35) in Celebration's TrophyLift
            // case), and Update re-Plays it from p = 0 the moment it ends, so a LOOPED lift drops
            // the trophy to the hip and re-raises it for the first 0.35 of every cycle. Holding
            // through that window is what keeps the trophy up between loops. Do not fix this in
            // Celebration: that ramp is shared by the intended first raise at the hand-over cut.
            if (celeb != null && celeb.Playing
                && (celeb.CurrentEmote != Celebration.Emote.TrophyLift || celeb.Progress01 < 0.35f))
            {
                rag.SetPoseOverride(Bone.UpperArmL, HoldUpperArmL);
                rag.SetPoseOverride(Bone.ForearmL, HoldForearmL);
            }
        }

        /// <summary>The cut: the standing trophy goes, the hand-held one appears, the lift starts, the orbit restarts from a new angle.</summary>
        void HandOver()
        {
            HandedOver = true;
            if (_standTrophy != null) { _standTrophy.Destroy(); _standTrophy = null; }
            if (Winner != null && Winner.Alive)
            {
                Trophy = CupTrophy.AttachToHand(Winner.Ragdoll, _gold);
                Winner.Ragdoll.ClearPoseOverrides();
                if (Winner.Celeb != null)
                {
                    if (Winner.Celeb.Playing) Winner.Celeb.Cancel();
                    Winner.Celeb.Play(Celebration.Emote.TrophyLift);
                }
            }
            if (Rig != null) Rig.PodiumOrbit(Centre, HandOverCutAngle, true);
        }

        /// <summary>Open / close the wheel. The cursor stays free either way (SetOpen would capture on close; the podium re-frees in Update).</summary>
        void SetWheel(bool open)
        {
            if (open == _wheelOpen) return;
            _wheelOpen = open;
            GameInput.CaptureCursor(false);
        }

        /// <summary>
        /// A remote champion's pick (the net agent, from the host's snapshot or the input frame):
        /// plays it on the winner when it is on the curated page, else ignored (a stray id must
        /// never drive the trophy arm).
        /// </summary>
        public void PlayWinnerEmote(Celebration.Emote e)
        {
            if (Winner == null || !Winner.Alive || Winner.Celeb == null || !HandedOver) return;
            if (!OnWheel(e)) return;
            CupEmoteWheel.Play(Winner.Celeb, null, e);
        }

        /// <summary>Is the emote on the podium's curated page?</summary>
        public static bool OnWheel(Celebration.Emote e)
        {
            var page = WheelPages[0];
            for (int i = 0; i < page.Length; i++) if (page[i].e == e) return true;
            return false;
        }

        // ==========================================================================================
        // UI (own OnGUI): the CHAMPIONS strip, the hint, the buttons after the delay, the wheel
        // ==========================================================================================

        /// <summary>
        /// Rebuild the button row when (and only when) the style or the authority flag moves. Solo
        /// and an authoritative MP peer get three buttons; a client gets End Match alone and the
        /// "waiting for host" line.
        /// </summary>
        void RefreshButtons()
        {
            bool solo = Director == null || Director.Style == CupStyle.Solo;
            bool authority = Director == null || Director.IsAuthority;
            if (_rowBuilt && solo == _rowSolo && authority == _rowAuthority) return;
            _rowSolo = solo;
            _rowAuthority = authority;
            _rowBuilt = true;
            _buttons.Clear();
            if (solo)
            {
                _buttons.Add(new Btn { Label = CupText.NewCup, Act = () => Director?.PlayAgain() });
                _buttons.Add(new Btn { Label = CupText.Continue, Act = () => _onContinue?.Invoke() });
                _buttons.Add(new Btn { Label = CupText.MainMenu, Act = () => Director?.QuitToMenu(), Bad = true });
            }
            else if (authority)
            {
                _buttons.Add(new Btn { Label = CupText.PlayAgain, Act = () => Director?.PlayAgain() });
                _buttons.Add(new Btn { Label = CupText.Continue, Act = () => _onContinue?.Invoke() });
                _buttons.Add(new Btn { Label = CupText.EndMatch, Act = () => Director?.EndMatch(), Bad = true });
            }
            else
            {
                _buttons.Add(new Btn { Label = CupText.EndMatch, Act = () => Director?.EndMatch(), Bad = true });
            }
        }

        void OnGUI()
        {
            Styles();
            GUI.depth = GuiDepth;
            Hud.Begin();
            float w = Hud.W, h = Hud.H;
            bool paused = PauseMenu.Paused;
            Action fire = null;

            // The title strip: a dark band with the champions line in gold.
            UITheme.Fill(new Rect(0f, 0f, w, 78f), StripColour);
            UITheme.Fill(new Rect(0f, 78f, w, 2f), new Color(UITheme.Gold.r, UITheme.Gold.g, UITheme.Gold.b, 0.8f));
            string strip = CupText.ChampionsStrip(CupNations.IsValid(ChampionNation) ? CupNations.Name(ChampionNation) : "", ChampionName);
            UITheme.Shadowed(new Rect(0f, 8f, w, 62f), strip, _titleStyle, UITheme.Gold, 0.8f, 3f);

            // Bottom hint (once the trophy is in hand; the beat before it is a cut, not a prompt).
            UITheme.Hint(new Rect(0f, h - 30f, w, 22f), HandedOver ? HintText : "");

            // Buttons: allocated on EVERY pass (parked off-screen and disabled before the delay
            // and under the pause menu), so control ids never shift under a click.
            bool show = ButtonsUp && !paused && !_wheelOpen;
            float bw = 170f, bh = 48f, gap = 20f, by = h - 100f;
            if (!_rowBuilt) RefreshButtons();   // OnGUI can precede the first Update
            int n = _buttons.Count;
            float total = n * bw + (n - 1) * gap;
            float bx = (w - total) * 0.5f;
            bool wasEnabled = GUI.enabled;
            GUI.enabled = show;
            for (int i = 0; i < n; i++)
            {
                var r = show ? new Rect(bx + i * (bw + gap), by, bw, bh) : new Rect(-1000f, -1000f, bw, bh);
                if (UITheme.Button(r, _buttons[i].Label, _buttonStyle, _buttons[i].Bad) && show) fire = _buttons[i].Act;
            }
            GUI.enabled = wasEnabled;
            if (!_rowSolo && !_rowAuthority && show)
                UITheme.Label(new Rect(0f, by - 28f, w, 22f), CupText.WaitingForHost, _waitStyle);

            // The wheel (the local champion's): drawn last, over everything. A pick closes it (and
            // captures the cursor; Update frees it again).
            if (_wheelOpen && !paused && Winner != null && Winner.Alive)
            {
                bool open = _wheelOpen;
                CupEmoteWheel.Draw(Winner.Celeb, Input, WheelPages, ref open);
                _wheelOpen = open;
            }

            Hud.End();
            // Intents after the scale block, never inside it: they may destroy this object.
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

        void OnDestroy()
        {
            // Bodies and props hang under the root and die with it; only the native objects and
            // the borrowed things need putting back here.
            if (Trophy != null) { Trophy.Destroy(); Trophy = null; }
            if (_standTrophy != null) { _standTrophy.Destroy(); _standTrophy = null; }
            if (Confetti != null) { Destroy(Confetti.gameObject); Confetti = null; }
            if (Ball != null)
            {
                for (int i = 0; i < _bodies.Count; i++)
                    if (_bodies[i] != null && _bodies[i].Alive) Ball.IgnoreBody(_bodies[i].Ragdoll, false);
            }
            _bodies.Clear();
            Winner = null;
            if (_kits != null) { _kits.Free(); _kits = null; }
            if (_stone != null) Destroy(_stone);
            if (_gold != null) Destroy(_gold);
            if (_daisMesh != null) Destroy(_daisMesh);
            if (_trimMesh != null) Destroy(_trimMesh);
            _stone = _gold = null;
            _daisMesh = _trimMesh = null;
            if (Rig != null) Rig.Release();
        }
    }
}
