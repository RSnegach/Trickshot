using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Purely aesthetic main-menu backdrop: a slow-motion goal reel shown as a soccer highlight
    /// edit. A striker plays five different finishes past a keeper (bicycle, volley, header,
    /// diving header, driven low), and the driven one the keeper saves. Each clip plays on a fully
    /// scripted, deterministic timeline while a cinematic director cuts between a wide angle and
    /// slow replay angles, then advances to the next clip and loops. Not interactive.
    ///
    /// The puppets are kinematic display bodies posed frame by frame. Each finish drives many
    /// per-bone eulers as functions of the clip's action-time, so the limbs articulate through the
    /// jump, kick, and dive instead of the body tilting as one rigid piece. Aerial moves get a real
    /// ballistic pelvis arc through basePos; only genuine whole-body moves (the bicycle inversion,
    /// the diving-header layout) use a root pitch/roll. Reference angles come from RagdollPose,
    /// KeeperPose, and SimConfig (HeaderTorsoBend, DiveLayoutPitch, KeeperDiveLayoutHigh/Low), but
    /// the per-frame motion is authored here, not replayed from a networked AnimState.
    ///
    /// The ball follows a scripted BallPath, not physics. Its collider is kept so contact with the
    /// keeper reads true, but it is a kinematic body: on the save clip it rebounds off the keeper's
    /// glove along a scripted deflection, and on the goal clips it routes past the beaten keeper so
    /// it never phases through the body.
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
            // Keep the SphereCollider so contact with the keeper's glove reads true, and give it a
            // bouncy PhysicsMaterial matching the in-game ball. Nothing simulates against it though:
            // the ball is a kinematic body driven along BallPath every frame, and both puppets are
            // kinematic too, so no collision is ever resolved. The rebound off the keeper on the
            // save clip is scripted into BallPath, not physics.
            if (_ball.TryGetComponent<Collider>(out var col))
                col.material = Make.PhysMat("BgBall", SimConfig.BallBounciness, 0.2f, 0.2f);
            // Sphere primitives ship no Rigidbody. Use TryGetComponent (Unity's ?? does not work on
            // components: a missing one is a fake-null, not real null, so ?? would skip AddComponent
            // and the .isKinematic set below would throw MissingComponentException).
            var ballRb = _ball.TryGetComponent<Rigidbody>(out var existing) ? existing : _ball.AddComponent<Rigidbody>();
            ballRb.isKinematic = true;
            ballRb.useGravity = false;

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

        // Ball flight. The four goal clips: crossed / passed in (a: 0 -> 0.5), struck across the
        // line (0.5 -> 0.78), driven into the back netting (0.78 -> 0.9), then billows down and
        // settles inside the goal (0.9 -> 1). The last two legs live inside the goal box so the
        // shot visibly GOES IN and the FlexNet bulges where it hits. crossArc/overArc shape each
        // leg. The beaten keeper dives low and lands under the flight (see PoseKeeper), so the
        // ball clears the body without phasing through it.
        //
        // The Normal clip is the SAVE: the ball is driven at goal, but at the save instant it hits
        // the keeper's glove at KeeperSavePt and rebounds up and wide, settling outside the post.
        // It never crosses the goal line. The keeper is posed to reach that same point.
        Vector3 BallPath(in Clip c, float a)
        {
            if (c.kind == ShotType.Normal) return SavePath(c, a);

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

            // Keeper flyover: while the ball is passing the keeper's plane, floor its height above
            // the laid-out, grounded keeper's full reach so it never grazes the body on its way in.
            // Pose-independent (does not depend on the exact dive angles), so retuning the keeper
            // can't silently reintroduce the phase-through. 1.2 = laid-body top ~0.9 + BallRadius +
            // margin. The low finishes (header, diving header) are the ones this actually lifts; the
            // bicycle/volley already fly higher than this. Not applied to the save clip (SavePath).
            if (Mathf.Abs(p.z - c.keeper.z) < 0.8f)
                p.y = Mathf.Max(p.y, 1.2f);
            return p;
        }

        // The save clip's ball flight. Driven in low (0 -> 0.5), carries toward the save point
        // (0.5 -> savePhase), rebounds off the glove up and wide (deflect leg), then drops and rolls
        // to rest OUTSIDE the post. Deterministic and scripted; the collider only makes the contact
        // read true (nothing simulates). Stage-relative, matching the goal path's return convention.
        Vector3 SavePath(in Clip c, float a)
        {
            Vector3 savePt = KeeperSavePt(c) - Stage;               // back to Stage-relative
            const float saveA = 0.70f;                              // the ball meets the glove here
            // Where the parry sends it: up and wide, clearing the post on the keeper's dive side.
            Vector3 parryApex = new Vector3(c.keeperDir * 4.2f, 1.9f, GoalZ - 0.4f);
            Vector3 parryRest = new Vector3(c.keeperDir * 4.6f, SimConfig.BallRadius, GoalZ - 1.6f);

            if (a <= 0.5f)
            {
                float t = a / 0.5f;
                Vector3 p = Vector3.Lerp(c.origin, c.contact, t);
                p.y += Mathf.Sin(t * Mathf.PI) * c.crossArc;
                return p;
            }
            if (a <= saveA)
            {
                float t = (a - 0.5f) / (saveA - 0.5f);
                Vector3 p = Vector3.Lerp(c.contact, savePt, t);     // driven at goal, into the glove
                p.y += Mathf.Sin(t * Mathf.PI) * 0.25f;             // slight lift off the turf
                return p;
            }
            if (a <= 0.85f)
            {
                float t = (a - saveA) / (0.85f - saveA);
                Vector3 p = Vector3.Lerp(savePt, parryApex, t);     // kicks off the glove, up and wide
                p.y += Mathf.Sin(t * Mathf.PI) * 0.6f;              // parabola over the parry
                return p;
            }
            {
                float t = (a - 0.85f) / 0.15f;
                return Vector3.Lerp(parryApex, parryRest, t * t);   // drops and settles wide of the post
            }
        }

        // ---- Striker: one pose builder per finish. Each drives many bone eulers across 'a' so the
        // limbs articulate through the move; reference angles come from RagdollPose/SimConfig. ----
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
                    // Back to goal (face is yawed 180). A bicycle kick IS a whole-body backward
                    // inversion, so the root genuinely pitches back; on top of that the pelvis
                    // follows a real leap-and-fall arc and both legs scissor while the arms fling
                    // out to spin the body over. Nothing is a static held pose.
                    float air    = Bump(a, 0.26f, 0.82f);            // pelvis leaves the ground and lands
                    float invert = Ramp(a, 0.30f, 0.50f) * (1f - Ramp(a, 0.68f, 0.88f)); // tip back, hold, recover
                    float load   = 1f - Ramp(a, 0.06f, 0.26f);       // crouch/gather before the leap
                    float scis   = Ramp(a, 0.34f, 0.54f);            // legs reach the scissor
                    float kick   = Bump(a, 0.42f, 0.60f);            // kicking leg snaps through contact

                    float rootPitch = -invert * 112f;                // throws the legs up and over toward goal
                    float rootRoll  = c.keeperDir * invert * 8f;     // slight corkscrew for style

                    // Kicking (right) leg whips up and over; the calf snaps straight through the ball.
                    _striBones[(int)Bone.ThighR] = new Vector3(-55f * scis - 70f * kick, 0f, 0f);
                    _striBones[(int)Bone.CalfR]  = new Vector3(30f * load + 15f * scis - 55f * kick, 0f, 0f);
                    _striBones[(int)Bone.FootR]  = new Vector3(-25f * kick, 0f, 0f);
                    // Plant (left) leg counter-scissors to drive the rotation the other way.
                    _striBones[(int)Bone.ThighL] = new Vector3(40f * load - 55f * scis, 0f, 0f);
                    _striBones[(int)Bone.CalfL]  = new Vector3(70f * scis, 0f, 0f);
                    _striBones[(int)Bone.FootL]  = new Vector3(-15f * scis, 0f, 0f);
                    // Torso and head lead the inversion; the head tucks as the body goes over.
                    _striBones[(int)Bone.Torso] = new Vector3(12f * load - 30f * invert, 0f, 0f);
                    _striBones[(int)Bone.Head]  = new Vector3(-25f * invert, 0f, 0f);
                    // Arms fling out and back for balance and to spin the body over.
                    float armSwing = Ramp(a, 0.30f, 0.56f);
                    _striBones[(int)Bone.UpperArmL] = new Vector3(60f * armSwing, 0f,  95f * armSwing);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(60f * armSwing, 0f, -95f * armSwing);
                    _striBones[(int)Bone.ForearmL]  = new Vector3(-20f * armSwing, 0f, 0f);
                    _striBones[(int)Bone.ForearmR]  = new Vector3(-20f * armSwing, 0f, 0f);

                    Quaternion root = face * Quaternion.Euler(rootPitch, 0f, rootRoll);
                    Vector3 pelvis = Stage + c.striker + new Vector3(0f, PelvisY + air * 1.15f, air * 0.25f);
                    _striker.DisplayPose(PelvisAnchor(pelvis, root), face, rootPitch, rootRoll, _striBones);
                    break;
                }
                case ShotType.Header:
                {
                    // Climbing header on a cross, faces goal (+Z). No root lean: the vertical reach
                    // is a genuine pelvis ARC via basePos, and the pose articulates through many
                    // per-limb eulers over 'a'. Crouch to load, spring, tuck the knees at the apex,
                    // snap the neck through the ball, then land with the feet planted again.
                    float air    = Bump(a, 0.20f, 0.85f);            // the jump: up and back down
                    float crouch = 1f - Ramp(a, 0.10f, 0.28f);       // gather before the spring
                    float land   = Ramp(a, 0.78f, 0.94f);            // re-plant on the way down
                    float loadW  = Mathf.Max(crouch, land);
                    float tuck   = Bump(a, 0.24f, 0.62f);            // knees tuck up at the apex
                    float nod    = Bump(a, 0.40f, 0.66f);            // neck/torso snap into the ball
                    float coil   = Bump(a, 0.26f, 0.46f);            // torso coils back before the nod

                    float thighX = -35f * loadW - 55f * tuck;
                    float calfX  =  60f * loadW + 70f * tuck;
                    float footX  = -15f * loadW;
                    _striBones[(int)Bone.ThighL] = new Vector3(thighX, 0f, 0f);
                    _striBones[(int)Bone.ThighR] = new Vector3(thighX, 0f, 0f);
                    _striBones[(int)Bone.CalfL]  = new Vector3(calfX, 0f, 0f);
                    _striBones[(int)Bone.CalfR]  = new Vector3(calfX, 0f, 0f);
                    _striBones[(int)Bone.FootL]  = new Vector3(footX, 0f, 0f);
                    _striBones[(int)Bone.FootR]  = new Vector3(footX, 0f, 0f);
                    _striBones[(int)Bone.Torso] = new Vector3(12f * loadW - 18f * coil + SimConfig.HeaderTorsoBend * nod, 0f, 0f);
                    _striBones[(int)Bone.Head]  = new Vector3(-16f * coil + 45f * nod, 0f, 0f);
                    float armUp = -95f * air;                        // arms swing up to climb
                    _striBones[(int)Bone.UpperArmL] = new Vector3(armUp, 0f,  22f * air);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(armUp, 0f, -22f * air);
                    _striBones[(int)Bone.ForearmL]  = new Vector3(25f * air, 0f, 0f);
                    _striBones[(int)Bone.ForearmR]  = new Vector3(25f * air, 0f, 0f);

                    float arc = 1.15f * air;
                    Vector3 basePos = Stage + c.striker + new Vector3(0f, arc, 0f);
                    _striker.DisplayPose(basePos, face, 0f, 0f, _striBones);
                    break;
                }
                case ShotType.DivingHeader:
                {
                    // Faces goal (+Z). Genuine horizontal layout via rootPitch (belly-down), plus a
                    // real up-and-out pelvis arc so it flies rather than slides. Arms are thrown
                    // forward at the ball, the neck snaps through contact, and the legs trail then
                    // whip up behind as the body lays out. Pelvis-anchored (the body rotates about
                    // it). This routes clear of the keeper: see the DivingHeader leg in BallPath.
                    float launch = Ramp(a, 0.32f, 0.54f);            // leaves the feet and commits
                    float lay    = Ramp(a, 0.34f, 0.52f) * (1f - Ramp(a, 0.82f, 0.96f)); // roll flat, hold, ease at land
                    float air    = Bump(a, 0.34f, 0.86f);            // rise then fall of the dive
                    float head   = Bump(a, 0.46f, 0.66f);            // neck snaps into the ball
                    float trail  = Ramp(a, 0.36f, 0.62f);            // legs whip up behind

                    float rootPitch = lay * SimConfig.DiveLayoutPitch;   // 90 = belly-down (genuine)

                    // Arms reach forward and out to attack the ball and frame the header.
                    _striBones[(int)Bone.UpperArmL] = new Vector3(-120f * launch, 0f,  30f * launch);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(-120f * launch, 0f, -30f * launch);
                    _striBones[(int)Bone.ForearmL]  = new Vector3(-25f * launch, 0f, 0f);
                    _striBones[(int)Bone.ForearmR]  = new Vector3(-25f * launch, 0f, 0f);
                    // Torso/neck drive into the ball, snapping through contact.
                    _striBones[(int)Bone.Torso] = new Vector3(18f * lay + 20f * head, 0f, 0f);
                    _striBones[(int)Bone.Head]  = new Vector3(30f * head, 0f, 0f);
                    // Legs trail, then fold up behind the body as it lays out.
                    _striBones[(int)Bone.ThighL] = new Vector3(20f * lay - 30f * trail, 0f, 0f);
                    _striBones[(int)Bone.ThighR] = new Vector3(20f * lay - 30f * trail, 0f, 0f);
                    _striBones[(int)Bone.CalfL]  = new Vector3(50f * trail, 0f, 0f);
                    _striBones[(int)Bone.CalfR]  = new Vector3(50f * trail, 0f, 0f);
                    _striBones[(int)Bone.FootL]  = new Vector3(-15f * trail, 0f, 0f);
                    _striBones[(int)Bone.FootR]  = new Vector3(-15f * trail, 0f, 0f);

                    Quaternion root = face * Quaternion.Euler(rootPitch, 0f, 0f);
                    float fwd = Ramp(a, 0.32f, 0.80f) * 1.5f;            // travels out horizontally toward the cross
                    Vector3 pelvis = Stage + c.striker + new Vector3(0f, PelvisY + air * 0.6f, fwd);
                    _striker.DisplayPose(PelvisAnchor(pelvis, root), face, rootPitch, 0f, _striBones);
                    break;
                }
                case ShotType.Volley:
                {
                    // Faces goal (+Z). Half-volley on a dropping ball: load the plant leg, a small
                    // genuine pelvis hop, the kicking leg swings UP to meet the ball at hip height
                    // and the calf snaps straight through, torso leaning back over the plant. All
                    // articulated across 'a'; no root lean, the hop is a short basePos arc.
                    float wind  = Ramp(a, 0.06f, 0.32f);             // load onto the plant leg
                    float swing = Ramp(a, 0.34f, 0.56f);             // kicking leg swings up to the ball
                    float thru  = Ramp(a, 0.50f, 0.66f);             // snap and follow through
                    float hop   = Bump(a, 0.34f, 0.64f);             // leaves the ground briefly
                    float lean  = Ramp(a, 0.20f, 0.58f);             // torso leans back off the swing

                    // Kicking (right) leg lifts to meet the drop; calf snaps straight through contact.
                    _striBones[(int)Bone.ThighR] = new Vector3(35f * wind - 95f * swing, 0f, 20f * swing);
                    _striBones[(int)Bone.CalfR]  = new Vector3(55f * wind + 40f * swing - 75f * thru, 0f, 0f);
                    _striBones[(int)Bone.FootR]  = new Vector3(-20f * thru, 0f, 0f);
                    // Plant (left) leg loads then extends off the ground with the hop.
                    _striBones[(int)Bone.ThighL] = new Vector3(-30f * wind + 10f * hop, 0f, 0f);
                    _striBones[(int)Bone.CalfL]  = new Vector3(50f * wind - 20f * hop, 0f, 0f);
                    _striBones[(int)Bone.FootL]  = new Vector3(-12f * wind, 0f, 0f);
                    // Torso leans back and opens toward the swing; head watches the ball.
                    _striBones[(int)Bone.Torso] = new Vector3(-22f * lean, 18f * swing, 12f * lean);
                    _striBones[(int)Bone.Head]  = new Vector3(10f * lean, 0f, 0f);
                    // Left arm flung up/out for balance; right arm sweeps down and across the strike.
                    _striBones[(int)Bone.UpperArmL] = new Vector3(-40f * swing, 0f,  70f * swing);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(30f * swing, 0f, -55f * swing);
                    _striBones[(int)Bone.ForearmL]  = new Vector3(-30f * swing, 0f, 0f);
                    _striBones[(int)Bone.ForearmR]  = new Vector3(-15f * swing, 0f, 0f);

                    Vector3 basePos = Stage + c.striker + new Vector3(0f, hop * 0.35f, 0f);
                    _striker.DisplayPose(basePos, face, 0f, 0f, _striBones);
                    break;
                }
                default: // Normal: planted first-time driven LOW finish. Faces goal (+Z), no hop, no root lean.
                {
                    float wind  = Ramp(a, 0.05f, 0.30f);
                    float drive = Ramp(a, 0.30f, 0.58f);
                    float plant = Bump(a, 0.00f, 0.60f);
                    _striBones[(int)Bone.ThighR] = new Vector3( 40f * wind - 65f * drive, 0f, 0f);
                    _striBones[(int)Bone.CalfR]  = new Vector3( 55f * wind - 65f * drive, 0f, 0f);
                    _striBones[(int)Bone.FootR]  = new Vector3(-15f * drive,              0f, 0f);
                    _striBones[(int)Bone.ThighL] = new Vector3(-25f * plant, 0f, 0f);
                    _striBones[(int)Bone.CalfL]  = new Vector3( 45f * plant, 0f, 0f);
                    _striBones[(int)Bone.FootL]  = new Vector3(-12f * plant, 0f, 0f);
                    _striBones[(int)Bone.Torso]  = new Vector3(18f * Ramp(a, 0.15f, 0.55f), 12f * wind - 24f * drive, 0f);
                    _striBones[(int)Bone.Head]   = new Vector3(6f * Ramp(a, 0.10f, 0.55f), 0f, 0f);
                    _striBones[(int)Bone.UpperArmL] = new Vector3( 20f * wind - 45f * drive, 0f,  8f);
                    _striBones[(int)Bone.UpperArmR] = new Vector3(-20f * wind + 45f * drive, 0f, -8f);
                    _striBones[(int)Bone.ForearmL]  = new Vector3(20f + 15f * drive, 0f, 0f);
                    _striBones[(int)Bone.ForearmR]  = new Vector3(20f + 15f * wind,  0f, 0f);
                    Vector3 basePos = Stage + c.striker;
                    _striker.DisplayPose(basePos, face, 0f, 0f, _striBones);
                    break;
                }
            }
        }

        // ---- Keeper: reads the shot late, commits, dives, and lands on the turf. Beaten on the
        // four goal clips (dives under the flight); makes the stop on the driven clip (reaches the
        // ball at KeeperSavePt). Reach targets come from KeeperPose, driven across 'a' here. ----
        void PoseKeeper(in Clip c, float a)
        {
            if (_keeper == null) return;
            for (int i = 0; i < _keepBones.Length; i++) _keepBones[i] = Vector3.zero;
            Quaternion face = Quaternion.LookRotation(Vector3.back, Vector3.up); // keeper faces the play (-Z)
            // Genuine dive envelopes: gather, push off the near foot, then extend to full stretch.
            // The dive travels on a real arc: a short leap to the apex (hopUp), then the body comes
            // all the way DOWN and LANDS flat on the turf and stays there (land). On the goal clips
            // the shot clears the grounded body overhead. This is what stops the old phase-through
            // and the old float: the keeper no longer hangs in the air after laying out, he hits
            // the ground under the flight.
            float commit = Ramp(a, 0.40f, 0.60f);   // decides and pushes off
            float reach  = Ramp(a, 0.48f, 0.74f);   // full extension toward the shot
            float hopUp  = Bump(a, 0.42f, 0.66f);   // brief rise as he leaves his feet
            float land   = Ramp(a, 0.56f, 0.90f);   // then down onto the turf, and it holds at 1

            switch (c.kind)
            {
                case ShotType.Bicycle:
                case ShotType.Header:
                case ShotType.Volley:
                {
                    // High dive, laid out and beaten. Genuine KeeperPose.Dive spread reached through
                    // a push-then-extend, plus a push-off from the legs so it does not just hang.
                    LerpPose(_keepBones, KeeperPose.Ready, KeeperPose.Dive, reach);
                    _keepBones[(int)Bone.CalfL] = new Vector3(55f * commit - 45f * reach, 0f, 0f);
                    _keepBones[(int)Bone.CalfR] = new Vector3(55f * commit - 45f * reach, 0f, 0f);
                    float roll = c.keeperDir * SimConfig.KeeperDiveLayoutHigh * commit;
                    Quaternion root = face * Quaternion.Euler(0f, 0f, roll);
                    // Out toward the corner, a brief leap (hopUp), then the laid-out body comes all
                    // the way down and LANDS on the turf (land -> 1 holds it there, ~0.32 for the
                    // rolled-flat pelvis) instead of floating.
                    float pelvisY = Mathf.Lerp(PelvisY, 0.32f, land) + hopUp * 0.45f;
                    Vector3 pelvis = Stage + c.keeper + new Vector3(c.keeperDir * 1.4f * reach, pelvisY, 0f);
                    _keeper.DisplayPose(PelvisAnchor(pelvis, root), face, 0f, roll, _keepBones);
                    break;
                }
                case ShotType.DivingHeader:
                {
                    // Dives across LOW and ends up flat on the turf; the header clears him overhead
                    // and inside the far post (see the DivingHeader leg in BallPath). Nearly flat
                    // layout (genuine Low), pelvis driven down to the ground so nothing intersects.
                    LerpPose(_keepBones, KeeperPose.Ready, KeeperPose.Dive, reach);
                    _keepBones[(int)Bone.CalfL] = new Vector3(55f * commit - 45f * reach, 0f, 0f);
                    _keepBones[(int)Bone.CalfR] = new Vector3(55f * commit - 45f * reach, 0f, 0f);
                    float roll = c.keeperDir * SimConfig.KeeperDiveLayoutLow * commit;
                    Quaternion root = face * Quaternion.Euler(0f, 0f, roll);
                    // Barely leaves the ground on a low dash and lands flat on the turf and holds.
                    float pelvisY = Mathf.Lerp(PelvisY, 0.28f, land) + hopUp * 0.28f;
                    Vector3 pelvis = Stage + c.keeper + new Vector3(c.keeperDir * 1.5f * reach, pelvisY, 0f);
                    _keeper.DisplayPose(PelvisAnchor(pelvis, root), face, 0f, roll, _keepBones);
                    break;
                }
                default: // Driven: this is the one the keeper SAVES. He reaches the ball at KeeperSavePt.
                {
                    // Genuine low block (SaveLeft/SaveRight) flung along the ground toward the ball.
                    // The pelvis is placed so the outstretched glove arrives at KeeperSavePt exactly
                    // when the ball does; BallPath then rebounds the ball off that same point. The
                    // keeper commits fully here (uses 'save' extension), so it looks like a real stop.
                    Vector3[] save = c.keeperDir < 0 ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                    float stop  = Ramp(a, 0.46f, 0.68f);   // committed by the save instant (~0.68)
                    float settle = Ramp(a, 0.70f, 0.92f);  // then he comes down onto the turf
                    LerpPose(_keepBones, KeeperPose.Ready, save, stop);
                    // Sit the pelvis a glove's length in from the save point on the keeper's dive
                    // side, low to the turf, so the extended arm/glove overlaps the ball there.
                    Vector3 savePt = KeeperSavePt(c);
                    Vector3 pelvis = savePt + new Vector3(-c.keeperDir * 0.55f, -0.15f, -0.15f);
                    pelvis = Vector3.Lerp(Stage + c.keeper + new Vector3(0f, PelvisY, 0f), pelvis, stop);
                    // After the parry he drops to the ground rather than hanging at the save height.
                    pelvis.y = Mathf.Lerp(pelvis.y, 0.34f, settle);
                    _keeper.DisplayPose(pelvis, face, 0f, 0f, _keepBones);
                    break;
                }
            }
        }

        // World-space point where the keeper's glove meets the ball on the SAVE clip (Normal). The
        // ball is deflected off this exact point in BallPath, and the keeper is posed to reach it,
        // so the stop reads as real contact. Out to the keeper's dive side, low, just in front of
        // the line. Kept as one function so the pose and the ball path can never drift apart.
        static Vector3 KeeperSavePt(in Clip c)
            => Stage + new Vector3(c.keeper.x + c.keeperDir * 0.9f, 0.85f, GoalZ - 0.7f);

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
