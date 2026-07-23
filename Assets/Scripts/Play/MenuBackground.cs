using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Purely aesthetic main-menu backdrop: a slow-motion goal reel shown as a real soccer
    /// highlight edit. A striker buries a series of different finishes past a beaten keeper
    /// (bicycle kick, volley, header, diving header, driven low finish); each goal plays out on
    /// a fully scripted, deterministic timeline and a cinematic director cuts between a live wide
    /// angle and slow replay angles, then advances to the next goal and loops. Not interactive.
    ///
    /// Every puppet pose is driven from the game's genuine animation data: RagdollPose (Stand /
    /// Load / Bicycle), KeeperPose (Ready / Dive / SaveLeft / SaveRight), and the DisplayAnim
    /// Jump / Kick bone sets, plus the SimConfig layout angles (HeaderTorsoBend, DiveLayoutPitch,
    /// KeeperDiveLayoutHigh/Low). No hand-invented skeleton eulers. Aerial moves pivot about the
    /// PELVIS (anchoring basePos so the pelvis stays put while the body inverts / lays out), which
    /// is why the legs read correctly instead of slinging about the feet.
    ///
    /// Built the same way the customize preview is (PlayerPreview): a dedicated camera plus an
    /// off-pitch staging area far from the real arena, its own light, and procedural geometry, so
    /// it never touches gameplay, the match camera, or the customize preview. The action is
    /// sampled by a normalized "action time" a in [0,1] rather than physics, so any camera can
    /// replay the same instant at any speed. Slow motion comes from advancing a slowly, never from
    /// Time.timeScale (which would slow real gameplay physics).
    ///
    /// Lives behind the IMGUI MenuUI (which always draws on top), so this is a pure backdrop. The
    /// owner (GameBootstrap) creates it when the title screen shows and tears it down the moment a
    /// mode or the multiplayer flow is chosen.
    /// </summary>
    public class MenuBackground : MonoBehaviour
    {
        // Staging area well away from the real pitch AND from PlayerPreview's stage (1000,1000)
        // so nothing ever overlaps or shares a light.
        static readonly Vector3 Stage = new Vector3(2000f, 0f, 2000f);

        // The single physical goal. Dimensions mirror the real game (SimConfig.GoalWidth/Height/
        // Depth) so the FlexNet built here is identical to the in-match net. Every goal clip
        // finishes inside this box.
        const float GoalZ = 9f;          // goal mouth line (local Z)
        const float PostX = 3.66f;       // GoalWidth 7.32 / 2
        const float PostH = 2.44f;       // GoalHeight
        const float GoalDepth = 3.0f;    // SimConfig.GoalDepth

        // Pelvis rest height (feet-relative) used by ActiveRagdoll.DisplaySnap/DisplayPose. The
        // puppets are built at unit scale (BuildScaled 1,1,1) so Off(0,1.02,0) == (0,1.02,0);
        // anchoring the pelvis therefore just subtracts root*(0,PelvisY,0) from the target.
        const float PelvisY = 1.02f;

        Camera _cam;
        Light _light;
        ActiveRagdoll _striker;
        ActiveRagdoll _keeper;
        GameObject _ball;
        FlexNet _net;
        readonly List<Material> _mats = new List<Material>();   // freed on teardown

        // Per-frame bone-euler scratch for each puppet (indexed by Bone). Genuine pose tables are
        // lerped into these; DisplayPose reads them.
        readonly Vector3[] _striBones = new Vector3[(int)Bone.Count];
        readonly Vector3[] _keepBones = new Vector3[(int)Bone.Count];

        float _clock;    // free-running unscaled seconds since setup

        // ---- One scripted goal: where the actors stand, the ball's waypoints, and the kind of
        // finish (which decides the striker/keeper poses). All positions are Stage-relative. ----
        struct Clip
        {
            public ShotType kind;
            public Vector3 striker;    // striker feet on the ground
            public Vector3 keeper;     // keeper feet on the ground
            public Vector3 origin;     // ball start (cross / pass in)
            public Vector3 contact;    // where the striker meets it
            public Vector3 goalT;      // across the line, into the corner
            public Vector3 netPunch;   // drives the back netting (net bulges here)
            public Vector3 netRest;    // billows down and settles, inside the goal
            public float crossArc;     // apex of the incoming ball's arc
            public float overArc;      // arc from contact to the line (negative = a downward nod)
            public int keeperDir;      // -1 dives toward -X, +1 toward +X
            public bool facesGoal;     // striker front toward +Z (goal). false = back to goal (bicycle)
            public Shot[] shots;       // the replay cut for this goal
        }

        // A single camera in the edit: which rig, the action-time window it sweeps, and how long
        // it holds. Replays re-show the same instant from another angle / at another speed.
        struct Shot { public int rig; public float aFrom, aTo, dur; }

        // Flattened edit: one entry per (clip, shot), walked by the unscaled clock.
        struct Seg { public int clip, rig; public float aFrom, aTo, dur; }

        Clip[] _clips;
        Seg[] _segs;
        float _loopDur;

        public void Setup()
        {
            _clips = BuildClips();

            // Flatten every clip's shot list into one ordered edit and total its runtime.
            var segs = new List<Seg>();
            for (int ci = 0; ci < _clips.Length; ci++)
            {
                var sh = _clips[ci].shots;
                for (int j = 0; j < sh.Length; j++)
                {
                    segs.Add(new Seg { clip = ci, rig = sh[j].rig, aFrom = sh[j].aFrom, aTo = sh[j].aTo, dur = sh[j].dur });
                    _loopDur += sh[j].dur;
                }
            }
            _segs = segs.ToArray();

            // Dedicated camera behind the menu (main camera is depth 0; IMGUI draws over both).
            var camGo = new GameObject("MenuBgCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.44f, 0.60f, 0.82f);   // stadium sky
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 200f;
            _cam.depth = 1;                 // over the main camera, under any later preview cam
            _cam.fieldOfView = 42f;
            // No AudioListener here: the main camera already owns the one listener.

            // Warm key light angled across the pitch.
            var lgo = new GameObject("MenuBgLight");
            lgo.transform.SetParent(transform, false);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.color = new Color(1f, 0.97f, 0.9f);
            _light.intensity = 1.15f;
            _light.transform.rotation = Quaternion.Euler(48f, 150f, 0f);
            _light.cullingMask = ~0;

            BuildScene();
            BuildActors();

            // Pose everything at the first frame of the edit so there is no 1-frame pop.
            Animate(0, _segs.Length > 0 ? _segs[0].aFrom : 0f);
            Direct2(0, 0, 0f);
        }

        // ---- The goal reel. Each clip reuses the one physical goal; the actors are repositioned
        // every frame by DisplayPose, so moving them between goals is free. ----
        Clip[] BuildClips()
        {
            return new[]
            {
                // Bicycle: back to goal, scissors a right-wing cross over his head into the top
                // corner. Genuine RagdollPose.Bicycle, body inverted about the pelvis.
                new Clip
                {
                    kind = ShotType.Bicycle, facesGoal = false, keeperDir = -1,
                    striker = new Vector3(0.2f, 0f, 2.3f), keeper = new Vector3(0.6f, 0f, 7.9f),
                    origin = new Vector3(7.0f, 0.4f, 4.2f), contact = new Vector3(0.2f, 1.55f, 2.55f),
                    goalT = new Vector3(-2.6f, 2.05f, GoalZ + 0.35f),
                    netPunch = new Vector3(-2.3f, 1.60f, GoalZ + GoalDepth - 0.8f),
                    netRest = new Vector3(-2.1f, 0.60f, GoalZ + GoalDepth - 0.7f),
                    crossArc = 1.3f, overArc = 0.7f,
                    shots = new[]
                    {
                        new Shot { rig = 0, aFrom = 0.00f, aTo = 0.72f, dur = 3.4f },
                        new Shot { rig = 3, aFrom = 0.52f, aTo = 1.00f, dur = 3.2f },
                    },
                },
                // Volley: faces goal, meets a dropping lofted ball on the half-volley and rifles it
                // into the far side. Genuine DisplayAnim.Kick swing, planted with a small hop.
                new Clip
                {
                    kind = ShotType.Volley, facesGoal = true, keeperDir = 1,
                    striker = new Vector3(-0.9f, 0f, 3.2f), keeper = new Vector3(0.4f, 0f, 7.9f),
                    origin = new Vector3(6.2f, 3.6f, 4.0f), contact = new Vector3(-0.9f, 1.05f, 3.4f),
                    goalT = new Vector3(2.5f, 1.35f, GoalZ + 0.35f),
                    netPunch = new Vector3(2.3f, 1.00f, GoalZ + GoalDepth - 0.9f),
                    netRest = new Vector3(2.1f, 0.50f, GoalZ + GoalDepth - 0.8f),
                    crossArc = 2.2f, overArc = 0.5f,
                    shots = new[]
                    {
                        new Shot { rig = 2, aFrom = 0.30f, aTo = 0.70f, dur = 2.9f },
                        new Shot { rig = 1, aFrom = 0.44f, aTo = 0.90f, dur = 3.0f },
                    },
                },
                // Header: faces goal, climbs on a left-wing cross and nods it down into the corner.
                // Genuine DisplayAnim.Jump tuck + SimConfig.HeaderTorsoBend fold, pelvis-anchored.
                new Clip
                {
                    kind = ShotType.Header, facesGoal = true, keeperDir = -1,
                    striker = new Vector3(0.6f, 0f, 3.6f), keeper = new Vector3(0.0f, 0f, 7.9f),
                    origin = new Vector3(-6.5f, 3.0f, 4.6f), contact = new Vector3(0.5f, 2.15f, 3.8f),
                    goalT = new Vector3(-2.6f, 0.80f, GoalZ + 0.35f),
                    netPunch = new Vector3(-2.4f, 0.55f, GoalZ + GoalDepth - 1.0f),
                    netRest = new Vector3(-2.3f, 0.35f, GoalZ + GoalDepth - 0.9f),
                    crossArc = 1.6f, overArc = -0.25f,   // downward nod
                    shots = new[]
                    {
                        new Shot { rig = 0, aFrom = 0.10f, aTo = 0.74f, dur = 3.2f },
                        new Shot { rig = 3, aFrom = 0.52f, aTo = 1.00f, dur = 3.1f },
                    },
                },
                // Diving header: faces goal, launches horizontal and heads a low driven cross in.
                // Genuine belly-down layout (SimConfig.DiveLayoutPitch) + ManageDive shaping.
                new Clip
                {
                    kind = ShotType.DivingHeader, facesGoal = true, keeperDir = -1,
                    striker = new Vector3(2.0f, 0f, 4.0f), keeper = new Vector3(-0.4f, 0f, 7.9f),
                    origin = new Vector3(-6.0f, 1.2f, 4.8f), contact = new Vector3(1.9f, 1.05f, 4.2f),
                    goalT = new Vector3(-1.4f, 0.95f, GoalZ + 0.35f),
                    netPunch = new Vector3(-1.3f, 0.70f, GoalZ + GoalDepth - 1.0f),
                    netRest = new Vector3(-1.3f, 0.40f, GoalZ + GoalDepth - 0.9f),
                    crossArc = 0.7f, overArc = 0.25f,
                    shots = new[]
                    {
                        new Shot { rig = 2, aFrom = 0.30f, aTo = 0.78f, dur = 3.0f },
                        new Shot { rig = 3, aFrom = 0.56f, aTo = 1.00f, dur = 3.0f },
                    },
                },
                // Driven finish: faces goal, first-time low drive off a square pass into the far
                // corner. Genuine DisplayAnim.Kick swing; keeper genuinely beaten low (SaveRight).
                new Clip
                {
                    kind = ShotType.Normal, facesGoal = true, keeperDir = 1,
                    striker = new Vector3(-0.5f, 0f, 3.0f), keeper = new Vector3(1.4f, 0f, 7.9f),
                    origin = new Vector3(5.6f, 0.35f, 3.6f), contact = new Vector3(-0.6f, 0.40f, 3.2f),
                    goalT = new Vector3(2.8f, 0.50f, GoalZ + 0.35f),
                    netPunch = new Vector3(2.6f, 0.40f, GoalZ + GoalDepth - 0.9f),
                    netRest = new Vector3(2.5f, 0.28f, GoalZ + GoalDepth - 0.8f),
                    crossArc = 0.25f, overArc = 0.06f,   // ground pass, low drive
                    shots = new[]
                    {
                        new Shot { rig = 1, aFrom = 0.10f, aTo = 0.80f, dur = 3.0f },
                        new Shot { rig = 3, aFrom = 0.52f, aTo = 1.00f, dur = 3.0f },
                    },
                },
            };
        }

        // ---- Static dressing: pitch, and the real goal frame + FlexNet (no stands) ----
        void BuildScene()
        {
            Material grass = M(new Color(0.16f, 0.34f, 0.16f), 0.05f);
            Material stripe = M(new Color(0.19f, 0.39f, 0.19f), 0.05f);

            // Pitch: a big thin slab, with a few mowing stripes for depth cues.
            Make.Box("BgPitch", new Vector3(46f, 0.1f, 46f), Stage + new Vector3(0f, -0.05f, 6f), grass, transform, collider: false);
            for (int i = -5; i <= 5; i++)
            {
                if ((i & 1) == 0) continue;
                Make.Box("BgStripe", new Vector3(46f, 0.11f, 3.6f), Stage + new Vector3(0f, -0.045f, 6f + i * 3.6f), stripe, transform, collider: false);
            }

            // Goal frame, built exactly like the real game (Arena.cs): round white cylindrical
            // posts + crossbar + back frame, at the mouth line.
            Vector3 goalCenter = Stage + new Vector3(0f, 0f, GoalZ);
            float gw = PostX * 2f, gh = PostH, gd = GoalDepth, postR = 0.07f;
            Material frameMat = M(Color.white, 0.3f);
            Make.Cylinder("BgPostL", postR, gh, goalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, transform);
            Make.Cylinder("BgPostR", postR, gh, goalCenter + new Vector3(gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, transform);
            Make.Cylinder("BgBar", postR, gw + postR * 2f, goalCenter + new Vector3(0f, gh, 0f), 0, frameMat, transform);
            Make.Cylinder("BgBackPostL", postR, gh, goalCenter + new Vector3(-gw * 0.5f, gh * 0.5f, gd), 1, frameMat, transform);
            Make.Cylinder("BgBackPostR", postR, gh, goalCenter + new Vector3(gw * 0.5f, gh * 0.5f, gd), 1, frameMat, transform);
            Make.Cylinder("BgRailL", postR * 0.7f, gd, goalCenter + new Vector3(-gw * 0.5f, gh, gd * 0.5f), 2, frameMat, transform);
            Make.Cylinder("BgRailR", postR * 0.7f, gd, goalCenter + new Vector3(gw * 0.5f, gh, gd * 0.5f), 2, frameMat, transform);

            // The real see-through FlexNet: a line-grid cloth sim wrapping back + sides + top.
            // Its node coords are goal-local (origin at the line centre, +Z into the goal), so
            // the object sits at the mouth-line centre with identity rotation, matching our +Z.
            var netMat = Make.Unlit(new Color(0.92f, 0.92f, 0.98f, 1f)); _mats.Add(netMat);
            var netGo = new GameObject("BgFlexNet");
            netGo.transform.SetParent(transform, false);
            netGo.transform.position = goalCenter;
            netGo.transform.rotation = Quaternion.identity;
            netGo.AddComponent<MeshFilter>();
            netGo.AddComponent<MeshRenderer>();
            _net = netGo.AddComponent<FlexNet>();
            _net.Build(gw, gh, gd, SimConfig.NetCols, SimConfig.NetRows, netMat);
            // No backstops needed: the ball is scripted (visual only), not physical. The net
            // still bulges because SetBall (wired in BuildActors) pushes nodes near the ball.
        }

        // ---- Actors: two kinematic display puppets (striker + keeper) ----
        void BuildActors()
        {
            Quaternion faceField = Quaternion.LookRotation(Vector3.back, Vector3.up); // front toward -Z at rest
            Vector3 striSpawn = Stage + (_clips.Length > 0 ? _clips[0].striker : Vector3.zero);
            Vector3 keepSpawn = Stage + (_clips.Length > 0 ? _clips[0].keeper : Vector3.zero);

            // Striker: identical kit to the in-game player striker (BuildStrikerPlayer):
            // painted jersey texture if one exists else the plain team colour on the torso,
            // team-blue base on the limbs (tinted to the player's skin by the appearance),
            // no gloves, wearing the player's actual appearance. Body kept at scale 1 so the
            // pose choreography stays aligned to the ball and the pelvis anchor math holds.
            var striGo = new GameObject("BgStriker");
            striGo.transform.SetParent(transform, false);
            _striker = striGo.AddComponent<ActiveRagdoll>();
            Material sTorso = PlayerProfile.JerseyTex != null
                ? Own(Make.MatTex(PlayerProfile.JerseyTex))
                : M(PlayerProfile.JerseyBase);
            Material sLimb = M(new Color(0.15f, 0.32f, 0.6f));
            _striker.BuildScaled(striSpawn, faceField, sTorso, sLimb, 1f, 1f, 1f,
                                 withGloves: false, appearance: PlayerProfile.Appearance);
            _striker.BecomeDisplayBody();

            // Keeper: identical to the in-game AI goalkeeper (BuildAiKeeper): yellow torso +
            // yellow limbs, gloves on, and NO appearance override (so the limbs stay keeper
            // yellow rather than tinting to skin).
            var keepGo = new GameObject("BgKeeper");
            keepGo.transform.SetParent(transform, false);
            _keeper = keepGo.AddComponent<ActiveRagdoll>();
            Material kTorso = M(new Color(0.9f, 0.85f, 0.2f));     // keeper kit
            Material kLimb = M(new Color(0.7f, 0.62f, 0.15f));
            _keeper.BuildScaled(keepSpawn, faceField, kTorso, kLimb, 1f, 1f, 1f,
                                withGloves: true, appearance: null);
            _keeper.BecomeDisplayBody();

            Material ballMat = M(new Color(0.95f, 0.95f, 0.97f), 0.35f);
            _ball = Make.Sphere("BgBall", SimConfig.BallRadius * 2f, striSpawn, ballMat, transform);
            if (_ball.TryGetComponent<Collider>(out var col)) Destroy(col);   // visual only

            // Wire the ball into the net so it bulges when the shot buries itself.
            if (_net != null) _net.SetBall(_ball.transform, SimConfig.BallRadius);
        }

        void Update()
        {
            if (_cam == null || _segs == null || _segs.Length == 0) return;
            _clock += Time.unscaledDeltaTime;

            // Where are we in the edit? Find the active segment and its local progress.
            float loopT = _loopDur > 0f ? Mathf.Repeat(_clock, _loopDur) : 0f;
            int seg = 0; float acc = 0f;
            for (int i = 0; i < _segs.Length; i++)
            {
                if (loopT < acc + _segs[i].dur) { seg = i; break; }
                acc += _segs[i].dur;
            }
            float s = Mathf.Clamp01((loopT - acc) / Mathf.Max(0.001f, _segs[seg].dur));

            // Ease the action-time sweep so slow replays glide (ease-in-out).
            float se = s * s * (3f - 2f * s);
            float a = Mathf.Lerp(_segs[seg].aFrom, _segs[seg].aTo, se);

            Animate(_segs[seg].clip, a);
            Direct2(_segs[seg].clip, _segs[seg].rig, s);
        }

        // ---- Deterministic action for one clip, sampled by action-time a in [0,1] ----
        void Animate(int clipIdx, float a)
        {
            a = Mathf.Clamp01(a);
            Clip c = _clips[clipIdx];

            if (_ball != null)
            {
                _ball.transform.position = Stage + BallPath(c, a);
                _ball.transform.Rotate(Vector3.right, 900f * Time.unscaledDeltaTime, Space.World); // visible spin
            }

            PoseStriker(c, a);
            PoseKeeper(c, a);
        }

        // Ball flight: crossed / passed in (a: 0 -> 0.5), struck across the line (0.5 -> 0.78),
        // driven into the back netting (0.78 -> 0.9), then billows down and settles inside the
        // goal (0.9 -> 1). The last two legs live inside the goal box so the shot visibly GOES IN
        // and the FlexNet bulges where it hits. crossArc/overArc shape each leg per clip.
        Vector3 BallPath(in Clip c, float a)
        {
            Vector3 p;
            if (a <= 0.5f)
            {
                float t = a / 0.5f;
                p = Vector3.Lerp(c.origin, c.contact, t);
                p.y += Mathf.Sin(t * Mathf.PI) * c.crossArc;
            }
            else if (a <= 0.78f)
            {
                float t = (a - 0.5f) / 0.28f;
                p = Vector3.Lerp(c.contact, c.goalT, t);
                p.y += Mathf.Sin(t * Mathf.PI) * c.overArc;   // negative arc = a downward header
            }
            else if (a <= 0.9f)
            {
                float t = (a - 0.78f) / 0.12f;
                p = Vector3.Lerp(c.goalT, c.netPunch, t);      // punches the back net
            }
            else
            {
                float t = (a - 0.9f) / 0.1f;
                p = Vector3.Lerp(c.netPunch, c.netRest, t * t); // accelerating fall, settles
            }
            return p;
        }

        // ---- Striker: one pose builder per finish, all from genuine animation data. ----
        void PoseStriker(in Clip c, float a)
        {
            if (_striker == null) return;
            Quaternion face = c.facesGoal
                ? Quaternion.LookRotation(Vector3.forward, Vector3.up)   // front toward +Z (goal)
                : Quaternion.LookRotation(Vector3.back, Vector3.up);     // back to goal (bicycle)
            for (int i = 0; i < _striBones.Length; i++) _striBones[i] = Vector3.zero;

            switch (c.kind)
            {
                case ShotType.Bicycle:
                {
                    float air = Bump(a, 0.30f, 0.74f);
                    float lay = Bump(a, 0.30f, 0.70f);               // 0..1..0 backward inversion
                    float rootPitch = -lay * 100f;                   // tips backward, legs up/over toward goal
                    float rootRoll = -lay * 6f;                      // a touch of lean for style
                    // Genuine RagdollPose.Bicycle, eased in over the leap and back out on the way down.
                    float blend = Mathf.Clamp01(Mathf.InverseLerp(0.30f, 0.46f, a))
                                  * (1f - Mathf.Clamp01(Mathf.InverseLerp(0.70f, 0.82f, a)));
                    LerpPose(_striBones, RagdollPose.Stand, RagdollPose.Bicycle, blend);
                    // Whip the kicking (right) thigh through the ball around contact (a ~ 0.5).
                    float whip = Mathf.Clamp01(Mathf.InverseLerp(0.44f, 0.54f, a));
                    _striBones[(int)Bone.ThighR] = new Vector3(Mathf.Lerp(-70f, -115f, whip), 0f, 0f);

                    Quaternion root = face * Quaternion.Euler(rootPitch, 0f, rootRoll);
                    Vector3 pelvis = Stage + c.striker + new Vector3(0f, PelvisY + air * 1.05f, air * 0.2f);
                    _striker.DisplayPose(PelvisAnchor(pelvis, root), face, rootPitch, rootRoll, _striBones);
                    break;
                }
                case ShotType.Header:
                {
                    float air = Bump(a, 0.30f, 0.74f);
                    float nod = Bump(a, 0.40f, 0.62f);
                    // Genuine DisplayAnim.Jump tuck ...
                    _striBones[(int)Bone.ThighL] = new Vector3(-30f, 0f, 0f);
                    _striBones[(int)Bone.ThighR] = new Vector3(-30f, 0f, 0f);
                    _striBones[(int)Bone.CalfL] = new Vector3(40f, 0f, 0f);
                    _striBones[(int)Bone.CalfR] = new Vector3(40f, 0f, 0f);
                    _striBones[(int)Bone.UpperArmL] = new Vector3(0f, 0f, 40f);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(0f, 0f, -40f);
                    // ... plus the genuine SimConfig header fold snapping through contact.
                    _striBones[(int)Bone.Torso] = new Vector3(SimConfig.HeaderTorsoBend * nod, 0f, 0f);
                    _striBones[(int)Bone.Head] = new Vector3(SimConfig.HeaderTorsoBend * 0.3f * nod, 0f, 0f);

                    Vector3 pelvis = Stage + c.striker + new Vector3(0f, PelvisY + air * 1.1f, air * 0.25f);
                    _striker.DisplayPose(PelvisAnchor(pelvis, face), face, 0f, 0f, _striBones);
                    break;
                }
                case ShotType.DivingHeader:
                {
                    float air = Bump(a, 0.34f, 0.78f);
                    float lay = Ramp(a, 0.34f, 0.52f) * (1f - Ramp(a, 0.80f, 0.94f)); // roll flat, hold, ease at land
                    float rootPitch = lay * SimConfig.DiveLayoutPitch;   // 90 = belly-down (genuine)
                    // Genuine ManageDive shaping (Striker.ManageDive): light torso + trailing legs.
                    _striBones[(int)Bone.Torso] = new Vector3(15f * lay, 0f, 0f);
                    _striBones[(int)Bone.ThighL] = new Vector3(25f * lay, 0f, 0f);
                    _striBones[(int)Bone.ThighR] = new Vector3(25f * lay, 0f, 0f);

                    Quaternion root = face * Quaternion.Euler(rootPitch, 0f, 0f);
                    float fwd = Ramp(a, 0.34f, 0.78f) * 1.4f;            // launches out horizontally
                    Vector3 pelvis = Stage + c.striker + new Vector3(0f, PelvisY + air * 0.5f, fwd);
                    _striker.DisplayPose(PelvisAnchor(pelvis, root), face, rootPitch, 0f, _striBones);
                    break;
                }
                default: // Volley / Normal driven: planted, genuine DisplayAnim.Kick swing.
                {
                    float windup = Mathf.Clamp01(Mathf.InverseLerp(0.30f, 0.44f, a));
                    float thru = Mathf.Clamp01(Mathf.InverseLerp(0.44f, 0.56f, a));
                    // Cock the leg back (Load-like), then swing through to the Kick values.
                    _striBones[(int)Bone.ThighR] = new Vector3(Mathf.Lerp(windup * 25f, -70f, thru), 0f, 0f);
                    _striBones[(int)Bone.CalfR] = new Vector3(Mathf.Lerp(windup * 60f, 20f, thru), 0f, 0f);
                    _striBones[(int)Bone.Torso] = new Vector3(12f * Mathf.Max(windup, thru), 0f, 0f);
                    _striBones[(int)Bone.UpperArmL] = new Vector3(0f, 0f, 45f);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(0f, 0f, -25f);

                    // A volley meets a dropping ball, so give it a small hop; the driven finish stays planted.
                    float hop = c.kind == ShotType.Volley ? Bump(a, 0.40f, 0.60f) * 0.25f : 0f;
                    Vector3 feet = Stage + c.striker + new Vector3(0f, hop, 0f);
                    _striker.DisplayPose(feet, face, 0f, 0f, _striBones);
                    break;
                }
            }
        }

        // ---- Keeper: reads the shot late, commits, and is beaten. All poses genuine (KeeperPose). ----
        void PoseKeeper(in Clip c, float a)
        {
            if (_keeper == null) return;
            for (int i = 0; i < _keepBones.Length; i++) _keepBones[i] = Vector3.zero;
            Quaternion face = Quaternion.LookRotation(Vector3.back, Vector3.up); // keeper faces the play (-Z)
            float react = Ramp(a, 0.42f, 0.72f);   // commits only after the ball is struck

            switch (c.kind)
            {
                case ShotType.Bicycle:
                case ShotType.Header:
                case ShotType.Volley:
                {
                    // High dive, laid out and beaten: genuine KeeperPose.Dive + full layout roll.
                    LerpPose(_keepBones, KeeperPose.Ready, KeeperPose.Dive, react);
                    float roll = c.keeperDir * SimConfig.KeeperDiveLayoutHigh * react;
                    Quaternion root = face * Quaternion.Euler(0f, 0f, roll);
                    Vector3 pelvis = Stage + c.keeper + new Vector3(c.keeperDir * 1.1f * react, PelvisY + react * 0.5f, 0f);
                    _keeper.DisplayPose(PelvisAnchor(pelvis, root), face, 0f, roll, _keepBones);
                    break;
                }
                case ShotType.DivingHeader:
                {
                    // Dives across low but the header squeezes past: nearly flat (genuine Low layout).
                    LerpPose(_keepBones, KeeperPose.Ready, KeeperPose.Dive, react);
                    float roll = c.keeperDir * SimConfig.KeeperDiveLayoutLow * react;
                    Quaternion root = face * Quaternion.Euler(0f, 0f, roll);
                    Vector3 pelvis = Stage + c.keeper + new Vector3(c.keeperDir * 1.3f * react, PelvisY + react * 0.25f, 0f);
                    _keeper.DisplayPose(PelvisAnchor(pelvis, root), face, 0f, roll, _keepBones);
                    break;
                }
                default: // Driven: genuine low block (SaveLeft/SaveRight), beaten to the corner.
                {
                    Vector3[] save = c.keeperDir < 0 ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                    LerpPose(_keepBones, KeeperPose.Ready, save, react);
                    Vector3 feet = Stage + c.keeper + new Vector3(c.keeperDir * 0.6f * react, 0f, 0f);
                    _keeper.DisplayPose(feet, face, 0f, 0f, _keepBones);
                    break;
                }
            }
        }

        // Anchor a pose so the PELVIS lands on pelvisTarget while the body leans by `root`. Undoes
        // the pelvis rest offset DisplayPose will re-add. Assumes unit build scale (see PelvisY).
        static Vector3 PelvisAnchor(Vector3 pelvisTarget, Quaternion root)
            => pelvisTarget - root * new Vector3(0f, PelvisY, 0f);

        // 0 -> 1 -> 0 over [lo,hi] (a sine bump, for airborne / inversion envelopes).
        static float Bump(float a, float lo, float hi)
            => Mathf.Sin(Mathf.Clamp01(Mathf.InverseLerp(lo, hi, a)) * Mathf.PI);

        // Smooth 0 -> 1 ramp over [lo,hi] (ease-in-out).
        static float Ramp(float a, float lo, float hi)
        {
            float t = Mathf.Clamp01(Mathf.InverseLerp(lo, hi, a));
            return t * t * (3f - 2f * t);
        }

        // Blend two genuine pose tables (both length Bone.Count) into dst.
        static void LerpPose(Vector3[] dst, Vector3[] from, Vector3[] to, float t)
        {
            for (int i = 0; i < dst.Length; i++) dst[i] = Vector3.Lerp(from[i], to[i], t);
        }

        // ---- Camera director. Orbit math matches PlayerPreview: pos = pivot + Euler(pitch,yaw,0)
        // * (0,0,-distance); LookAt(pivot). Pivots follow the active clip's landmarks so every
        // goal is framed by the same four rigs. ----
        void Direct2(int clipIdx, int rig, float s)
        {
            Clip c = _clips[clipIdx];
            Vector3 striker = Stage + c.striker + new Vector3(0f, 1.3f, 0f);
            Vector3 goal = Stage + new Vector3(0f, 1.2f, GoalZ);
            float cornerX = c.goalT.x;   // which corner the ball enters

            Vector3 pivot; float pitch, yaw, dist, fov;
            switch (rig)
            {
                default:
                case 0: // Live wide, side-on: the strike silhouette, slow push-in with a pan.
                    pivot = striker + new Vector3(0f, 0.2f, 0f);
                    pitch = 6f;
                    yaw = Mathf.Lerp(76f, 98f, s);
                    dist = Mathf.Lerp(12.5f, 10.5f, s);
                    fov = 42f;
                    break;
                case 1: // Low goal-line, from inside the goal looking back out at the play.
                    pivot = goal + new Vector3(0f, -0.2f, -0.4f);
                    pitch = -3f;
                    yaw = Mathf.Lerp(171f, 189f, s);
                    dist = Mathf.Lerp(7.2f, 5.8f, s);
                    fov = 40f;
                    break;
                case 2: // Orbit behind the striker toward goal: the leap and strike.
                    pivot = striker + new Vector3(0f, 0.4f, 0f);
                    pitch = 12f;
                    yaw = Mathf.Lerp(346f, 22f, s);   // sweeps through 0 (directly behind, -Z)
                    dist = Mathf.Lerp(4.8f, 3.9f, s);
                    fov = 46f;
                    break;
                case 3: // Top-corner / keeper's side: the ball beating the dive into the net.
                    pivot = Stage + new Vector3(cornerX * 0.5f, 1.7f, GoalZ - 0.2f);
                    pitch = 9f;
                    dist = Mathf.Lerp(5.4f, 4.1f, s);
                    fov = 44f;
                    // Sit outside whichever corner the ball enters and look back in.
                    yaw = cornerX < 0f ? Mathf.Lerp(150f, 128f, s) : Mathf.Lerp(210f, 232f, s);
                    break;
            }

            // Subtle handheld drift so the frame feels alive (deterministic, unscaled clock driven).
            float nx = (Mathf.PerlinNoise(_clock * 0.6f, 0f) - 0.5f) * 2f;
            float ny = (Mathf.PerlinNoise(0f, _clock * 0.6f) - 0.5f) * 2f;
            yaw += nx * 0.8f;
            pitch += ny * 0.5f;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = pivot + rot * new Vector3(0f, 0f, -dist);
            _cam.transform.position = pos;
            _cam.transform.LookAt(pivot);
            _cam.fieldOfView = fov;
        }

        Material M(Color c, float smoothness = 0.1f, float metallic = 0f)
        {
            var m = Make.Mat(c, smoothness, metallic);
            _mats.Add(m);
            return m;
        }

        // Track an already-built material for teardown (e.g. Make.MatTex).
        Material Own(Material m) { _mats.Add(m); return m; }

        public void Teardown()
        {
            if (this != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Materials created here are not owned by any GameObject, so free them explicitly.
            for (int i = 0; i < _mats.Count; i++)
                if (_mats[i] != null) Destroy(_mats[i]);
            _mats.Clear();
        }
    }
}
