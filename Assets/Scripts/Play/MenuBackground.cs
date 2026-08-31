using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Purely aesthetic main-menu backdrop: a LIVE AI-vs-AI shooting drill filmed by a cinematic
    /// camera. A visible AI shooter (Crosser) plays a run-up and swing and rifles a real physics ball
    /// at the goal; the autonomous AI Goalkeeper reads the shot, dives, and physically saves it or is
    /// beaten. Nothing here is scripted or puppeted: both bodies are live active ragdolls (never
    /// BecomeDisplayBody), the ball is a normal dynamic Rigidbody, and the save is a genuine physics
    /// collision. A slow serve cycle resets the ball and keeper between shots and loops. Not
    /// interactive.
    ///
    /// It runs at the REAL SimConfig goal coordinates, NOT an off-pitch offset: the AI keeper and the
    /// ball launcher read the goal position from static SimConfig.GoalCenter (which is readonly, so it
    /// cannot be relocated), and on the title screen no match exists there yet, so the space is empty.
    /// PlayerPreview stages far away at (1000,1000), so nothing overlaps.
    ///
    /// It has its own camera (depth 1, over the main camera, under any later preview cam) and its own
    /// light, and adds NO AudioListener (the main camera owns the one listener). It never attaches
    /// GameCamera (which drives the global Time.timeScale) and never touches Time.timeScale itself, so
    /// it cannot leak into or slow real gameplay. It tunes a few mutable SimConfig sliders
    /// (KeeperAbility, BallSpeedMul) for punchy shots and restores them on teardown.
    ///
    /// Lives behind the IMGUI MenuUI (which always draws on top), so this is a pure backdrop. The
    /// owner (GameBootstrap) creates it when the title screen shows and tears it down the moment a
    /// mode or the multiplayer flow is chosen; everything spawned is parented under this object, so
    /// Destroy cascades it all away.
    /// </summary>
    public class MenuBackground : MonoBehaviour
    {
        // ---- Tuning (all local so the reel is easy to eyeball-adjust) ----
        // Was 1.0 (max): deliberately the fastest tracking + longest dive reach, for a flashier
        // reel. But dive reach and reaction speed are the SAME ability lever in Goalkeeper.cs, and
        // pinning it to the literal maximum showcases the high dive's reach at its absolute upper
        // bound - reported as diving too far out of the goal. 0.6 keeps him quick and competent
        // without deliberately dialing reach to the ceiling of what the AI ladder allows.
        const float KeeperAbilityLevel = 0.6f;
        const float BallSpeedBoost     = 1.5f;   // multiplies launch speed (shortens flight) for punch
        const float PlantOutZ          = 11f;    // metres in front of goal the shooter plants + strikes from
        // Orbit geometry, sized so the camera stays inside the playing surface now that a stadium
        // surrounds it. Everything here is RELATIVE to _goalC (= SimConfig.GoalCenter), which is derived
        // from the active PitchLayout and is NOT a fixed number - it measures 17 under ResetToTraining
        // today and moves with the layout, so the arithmetic below is written in terms of GoalCenter.z
        // rather than a literal. Pivot 13 m off the goal line, radius 11.5.
        const float MenuOrbitBackZ     = 13f;
        const float MenuOrbitRadius    = 11.5f;
        // Base downtilt, oscillating +/-3 as it laps. This went 8 -> 15 -> 7 and the round trip is worth
        // recording, because the two arguments pull opposite ways and the second one won:
        //   8 originally, to keep the sky band in frame.
        //   15 once the stadium existed, on the grounds that the band was full of roof and pylons so the
        //     sky no longer mattered and the camera might as well look down into the bowl.
        //   7 now, because looking down into the bowl is what made the sky "boring gray". A downtilt of
        //     p with a 46 degree lens puts the top of frame at (23 - p) degrees of elevation, so 15 was
        //     showing only 0..8 - and near the horizon every panorama is hazy, desaturated and cloudless.
        //     Measured on the chosen sky, opening the band from 0..8 to 0..17 takes blueness from +0.174
        //     to +0.241 and chroma from 0.20 to 0.27. The sky cannot be fixed by picking a different
        //     image while the camera only looks at haze.
        const float MenuOrbitPitch     = 7f;
        // Pivot height, RAISED from 1.6 to hold the camera where it was while the tilt flattens. Camera
        // height is MenuOrbitPivotY + MenuOrbitRadius * sin(pitch), so dropping the tilt from 15 to 7
        // costs 11.5 * (sin15 - sin7) = 1.6 m, and the pivot absorbs exactly that. Net effect: the camera
        // sits at the same height as before and sees the same amount of stadium, but aims flatter, so sky
        // replaces turf at the top of frame rather than the bowl shrinking.
        const float MenuOrbitPivotY    = 3.2f;
        const float RunBackDist        = 5f;     // how far behind the plant spot the run-up starts
        const float RunupSpeed         = 5.0f;   // jog-in speed (m/s)
        const float PlantStopDist      = 0.35f;  // within this of the plant spot -> stop and swing
        const float RunupTimeout       = 3.0f;   // safety: force the swing if the jog stalls
        const float LiveDuration       = 2.9f;   // time a shot + save is allowed to play before reset
        const float SlowMo             = 0.7f;   // global time scale while the reel plays (restored on teardown)
        // Raised from 22. With the keeper-aware slowdown below, the lap now spends most of its time at
        // the slow rate, so the cruise between interesting angles wanted to be brisker to compensate -
        // otherwise adding the slowdown just made the whole orbit feel sluggish.
        const float OrbitSpeed         = 30f;    // deg/sec the camera circles the action (full 360 loop)
        // ...and the rate it drops to while the keeper is in shot, so the lap lingers on the save
        // instead of sweeping past it. Eased between the two, never stepped: a sudden rate change on a
        // slow orbit is very visible.
        const float OrbitSpeedKeeper   = 8f;     // deg/sec while the keeper is framed
        // The easing window, in degrees off the view axis. The lens is 46 vertical, which at 16:9 is
        // about 74 horizontal, so the keeper leaves frame near 37 degrees off centre. Full slow inside
        // 12, fully back up to speed by 40 - just past the frame edge, so the camera has finished
        // accelerating before he reappears on the other side of the lap.
        const float KeeperHoldInner    = 12f;
        const float KeeperHoldOuter    = 40f;

        // Cosmetic run gait, mirroring Footballer.RunGait (procedural leg + arm pump via pose overrides).
        float _gaitPhase;

        Camera _cam;
        Light _light;
        readonly List<Light> _parked = new List<Light>();   // suns switched off for the reel
        ActiveRagdoll _keeperRag;
        ActiveRagdoll _crosserRag;
        Goalkeeper _keeper;
        Crosser _crosser;
        BallController _ball;
        FlexNet _net;
        readonly List<Material> _mats = new List<Material>();   // freed on teardown

        Vector3 _goalC;         // SimConfig.GoalCenter, cached
        Vector3 _keeperHome;    // where the keeper resets to between shots
        Vector3 _ballHome;      // where the ball sits before each swing (matches Crosser.SetOrigin)
        Vector3 _plantSpot;     // where the shooter plants and strikes from (feet, y=0)
        Vector3 _runStart;      // where the shooter begins the run-up (behind the plant spot)
        Quaternion _shootFacing;// shooter facing toward goal (+Z)

        float _clock;           // free-running unscaled seconds
        // The orbit angle is now INTEGRATED rather than computed as _clock * OrbitSpeed, because the
        // rate varies with what is in frame (see DirectLive). Keeping it as a product of the clock would
        // make a varying rate impossible without the angle jumping the moment the rate changed.
        float _orbitYaw;        // accumulated degrees around the pivot
        float _phaseT;          // seconds in the current phase
        int _phase;             // 0 = run-up (jog in + gait), 1 = live (swing + shot + save)

        // Globals we set for the reel and restore on teardown so nothing leaks into a match.
        float _savedKeeperAbility;
        float _savedBallSpeedMul;
        float _savedTimeScale;
        float _savedFixedDt;

        public void Setup()
        {
            _goalC = SimConfig.GoalCenter;
            _keeperHome = SimConfig.KeeperStart;

            // Punchy, high-ability shots for the reel. Cache first, restore in OnDestroy.
            _savedKeeperAbility = SimConfig.KeeperAbility;
            _savedBallSpeedMul = SimConfig.BallSpeedMul;
            SimConfig.KeeperAbility = KeeperAbilityLevel;
            SimConfig.BallSpeedMul = BallSpeedBoost;

            // Gentle slow-mo for the whole reel (the game slows replays the same way). Cache and
            // restore synchronously on teardown so it can never leak into real gameplay.
            _savedTimeScale = Time.timeScale;
            _savedFixedDt = Time.fixedDeltaTime;
            Time.timeScale = SlowMo;
            Time.fixedDeltaTime = 0.02f * SlowMo;

            // Dedicated camera behind the menu (main camera is depth 0; IMGUI draws over both).
            var camGo = new GameObject("MenuBgCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.44f, 0.60f, 0.82f);   // stadium sky
            _cam.nearClipPlane = 0.15f;
            // 600, up from 200, because there is a CITY out there now. SurroundBuilder.Skyline puts
            // buildings 153-341 m from the middle of the pitch, and the orbit sits 40-51 m from that
            // middle, so the furthest building is about 392 m from the lens. At the old 200 everything
            // past halfway was simply cut, and because the camera laps (moving about 23 m across a lap)
            // the buildings sitting near the boundary crossed it twice a lap and popped in and out.
            //
            // Near clip raised off 0.05 at the same time. Pushing the far plane out 3x stretches the
            // depth range, and 0.05..600 is a 12000:1 ratio; nothing on this reel ever comes within a
            // metre of the lens (the orbit is a fixed 11.5 m radius about a pivot on the pitch), so the
            // extra precision is free.
            // 900. This went 200 -> 600 -> 1200 -> 900 and the last step is just bookkeeping: 1200 was
            // sized for a far mountain range that has since been removed. What is left out there is
            // SurroundBuilder.Terrain's hills, whose furthest centre measures 602 m with a radius up to
            // 174 m, and the orbit sits 40-51 m off the pitch centre on the far side of the lap - so the
            // worst camera-to-far-edge distance is about 827 m. 900 covers it with 73 m spare.
            //
            // The reason to care at all: at 600 the city skyline sat beyond the plane and popped in and
            // out twice a lap as the camera moved. Anything placed further out than this has to move the
            // plane with it.
            _cam.farClipPlane = 900f;
            _cam.depth = 1;                 // over the main camera, under any later preview cam
            _cam.fieldOfView = 46f;   // DirectLive resets this every frame; matched so frame one does not pop
            // No AudioListener here: the main camera already owns the one listener.

            // ONE directional light for the reel. GameBootstrap's sun is still alive behind the
            // menu, aimed at yaw -35, while this shot is lit from 150: two directional lights 175
            // degrees apart each land N.L = sin(48) = 0.74 on flat ground, so the pitch was getting
            // 1.7x the diffuse it was authored for AND every surface was lit from both sides, which
            // cancels the modelling. Turf read as flat bright card because of it. Park them here and
            // put them back in OnDestroy. Runs at Awake, so anything created later (PlayerPreview
            // makes its own light for the customise screen) is left alone.
            var lit = FindObjectsByType<Light>(FindObjectsInactive.Exclude);
            for (int i = 0; i < lit.Length; i++)
            {
                if (lit[i].type != LightType.Directional || !lit[i].enabled) continue;
                lit[i].enabled = false;
                _parked.Add(lit[i]);
            }

            // Warm key light angled across the pitch.
            var lgo = new GameObject("MenuBgLight");
            lgo.transform.SetParent(transform, false);
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Directional;
            _light.color = new Color(1f, 0.97f, 0.9f);
            _light.intensity = 1.25f;
            _light.transform.rotation = Quaternion.Euler(50f, 150f, 0f);   // 50 = the menu sky's own sun
            _light.cullingMask = ~0;
            // The sun this replaces cast soft shadows; without these the reel has none at all.
            _light.shadows = LightShadows.Soft;
            _light.shadowStrength = 0.58f;

            // Fill. Not a second sun: at 0.28 against the key's 1.25 this is a 22% ratio, which is
            // what a fill is, whereas the pair this replaced were both 1.15 and cancelled each other.
            // It exists because one directional light leaves every surface facing away from yaw 150
            // on ambient alone, and that read as unlit rather than shaded. No shadows, so it costs a
            // forward pass and nothing else. Sky-blue because that is the colour of the light a real
            // shadow gets, which is bounce off the sky.
            var fgo = new GameObject("MenuBgFill");
            fgo.transform.SetParent(transform, false);
            var fill = fgo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.80f, 0.87f, 1f);
            fill.intensity = 0.28f;
            fill.transform.rotation = Quaternion.Euler(22f, 330f, 0f);
            fill.shadows = LightShadows.None;
            fill.cullingMask = ~0;

            // Real sky behind the backdrop. Deliberately the FIXED menu sky, not the selected
            // venue's: the front end should not change mood with the stadium picker. The light
            // keeps its own direction (it is aimed for this shot) and the sky puts its disk there.
            SkyDome.ApplyMenu(_cam, _light);

            BuildScene();
            BuildActors();

            // Start in the run-up phase: the shooter jogs in from _runStart, then plants and swings.
            BeginRunup();
        }

        // ---- Static dressing: ground (with a real collider), pitch stripes, goal frame + net +
        // backstops. Built at the real goal coords so the AI aims true. ----
        void BuildScene()
        {
            // THE REAL PITCH AND THE REAL STADIUM, not a turf patch. This used to be a bare 44 x 48
            // slab with a goal on it and nothing else in view, so the front end was a goal in a field.
            // Everything needed already existed and was only ever used by matches:
            //
            //   PitchLayout.ResetToTraining puts a regulation 105 x 68 pitch down with its ATTACKING
            //   goal at SimConfig.GoalCenter.z - which is exactly where this scene's goal already is,
            //   so the two align with no new arithmetic.
            //   PitchBuilder.Build lays the marked pitch, its ground collider (with a turf physics
            //   material) and the FAR goal. It builds no near goal, so it does not collide with the one
            //   assembled below.
            //   StadiumBuilder.Build raises the bowl: raked stands, advertising perimeter, back walls,
            //   cantilevered roofs, corner infill towers, floodlight pylons and a player tunnel.
            //   Crowd.Create fills the seats.
            //
            // ALL OF IT IS FREE PER FRAME, which is the constraint. PitchBuilder and Crowd both call
            // StaticBatchingUtility.Combine, the crowd has no Update at all, and StadiumBuilder adds no
            // real Lights - its floodlamps are emissive materials. CrowdCheer is deliberately NOT
            // registered: a reacting crowd would move transforms and break the static batch for a
            // backdrop nobody is playing.
            PitchLayout.ResetToTraining();
            PitchBuilder.Build(transform);
            StadiumBuilder.Build(transform);
            Crowd.Create(transform);
            // Goal frame (round white posts + crossbar + back frame), mirroring Arena, at the mouth.
            float gw = SimConfig.GoalWidth, gh = SimConfig.GoalHeight, gd = SimConfig.GoalDepth, postR = 0.07f;
            Material frameMat = M(Color.white, 0.3f);
            var woodwork = Make.PhysMat("BgPost", 0.6f, 0.3f, 0.3f);
            Make.Cylinder("BgPostL", postR, gh, _goalC + new Vector3(-gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, transform, woodwork);
            Make.Cylinder("BgPostR", postR, gh, _goalC + new Vector3(gw * 0.5f, gh * 0.5f, 0f), 1, frameMat, transform, woodwork);
            Make.Cylinder("BgBar", postR, gw + postR * 2f, _goalC + new Vector3(0f, gh, 0f), 0, frameMat, transform, woodwork);
            Make.Cylinder("BgBackPostL", postR, gh, _goalC + new Vector3(-gw * 0.5f, gh * 0.5f, gd), 1, frameMat, transform, woodwork);
            Make.Cylinder("BgBackPostR", postR, gh, _goalC + new Vector3(gw * 0.5f, gh * 0.5f, gd), 1, frameMat, transform, woodwork);
            Make.Cylinder("BgRailL", postR * 0.7f, gd, _goalC + new Vector3(-gw * 0.5f, gh, gd * 0.5f), 2, frameMat, transform, woodwork);
            Make.Cylinder("BgRailR", postR * 0.7f, gd, _goalC + new Vector3(gw * 0.5f, gh, gd * 0.5f), 2, frameMat, transform, woodwork);

            // See-through FlexNet cloth (line grid), goal-local origin at the mouth centre.
            var netMat = Make.Unlit(new Color(0.92f, 0.92f, 0.98f, 1f)); _mats.Add(netMat);
            var netGo = new GameObject("BgFlexNet");
            netGo.transform.SetParent(transform, false);
            netGo.transform.position = _goalC;
            netGo.transform.rotation = Quaternion.identity;
            netGo.AddComponent<MeshFilter>();
            netGo.AddComponent<MeshRenderer>();
            _net = netGo.AddComponent<FlexNet>();
            _net.Build(gw, gh, gd, SimConfig.NetCols, SimConfig.NetRows, netMat);

            // Invisible backstops so a real shot that beats the keeper stops in the net instead of
            // sailing through the visual-only mesh. Minimum bounce-combine kills the rebound.
            var netPhys = Make.PhysMat("BgNet", 0f, 0.95f, 0.95f, PhysicsMaterialCombine.Minimum);
            MakeBackstop(new Vector3(gw, gh, 0.06f), _goalC + new Vector3(0f, gh * 0.5f, gd), netPhys);
            MakeBackstop(new Vector3(0.06f, gh, gd), _goalC + new Vector3(-gw * 0.5f, gh * 0.5f, gd * 0.5f), netPhys);
            MakeBackstop(new Vector3(0.06f, gh, gd), _goalC + new Vector3(gw * 0.5f, gh * 0.5f, gd * 0.5f), netPhys);
            MakeBackstop(new Vector3(gw, 0.06f, gd), _goalC + new Vector3(0f, gh, gd * 0.5f), netPhys);
        }

        void MakeBackstop(Vector3 size, Vector3 pos, PhysicsMaterial phys)
        {
            var go = Make.Box("BgBackstop", size, pos, null, transform, collider: true);
            if (go.TryGetComponent<Renderer>(out var r)) Destroy(r);
            if (go.TryGetComponent<Collider>(out var c)) c.material = phys;
            go.AddComponent<NetBackstop>();   // ball deadens its rebound on contact
        }

        // ---- Actors: a live AI keeper + a live AI shooter (both real physics ragdolls) + a dynamic
        // ball. NONE of them are turned kinematic (no BecomeDisplayBody). ----
        void BuildActors()
        {
            // Dynamic physics ball (same build as GameBootstrap's match ball; left DYNAMIC).
            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "BgBall";
            ballGo.transform.SetParent(transform, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = M(new Color(0.95f, 0.95f, 0.97f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            _ball = ballGo.AddComponent<BallController>();
            if (_net != null) _net.SetBall(_ball.transform, SimConfig.BallRadius);

            // AI keeper (live ragdoll), same wiring as GameBootstrap.BuildAiKeeper.
            var keeperGo = new GameObject("BgKeeper");
            keeperGo.transform.SetParent(transform, true);
            _keeperRag = keeperGo.AddComponent<ActiveRagdoll>();
            var kFacing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            _keeperRag.Build(_keeperHome, kFacing,
                             M(new Color(0.9f, 0.85f, 0.2f)), M(new Color(0.7f, 0.62f, 0.15f)));
            _keeper = keeperGo.AddComponent<Goalkeeper>();
            _keeper.Init(_keeperRag, _ball);
            _keeper.ResetTo(_keeperHome);

            // AI shooter (Crosser: a live ragdoll that plays a cosmetic swing and code-launches the
            // ball at contact), same wiring as GameBootstrap.BuildCrosser but reticle-free and manual.
            // We add a run-up in front of it: the body jogs from _runStart to _plantSpot under its own
            // locomotion + a procedural gait, then the Crosser swing fires.
            _plantSpot = new Vector3(0f, 0f, _goalC.z - PlantOutZ);
            _runStart = _plantSpot - Vector3.forward * RunBackDist;   // start further from goal (-Z)
            Vector3 toGoal = _goalC - _plantSpot; toGoal.y = 0f;
            _shootFacing = Quaternion.LookRotation(toGoal.normalized, Vector3.up);

            var crosserGo = new GameObject("BgShooter");
            crosserGo.transform.SetParent(transform, true);
            _crosserRag = crosserGo.AddComponent<ActiveRagdoll>();
            _crosserRag.Build(_runStart, _shootFacing,
                              M(new Color(0.15f, 0.32f, 0.6f)), M(new Color(0.12f, 0.26f, 0.5f)),
                              withGloves: false);
            _crosser = crosserGo.AddComponent<Crosser>();
            // Launch point at GROUND level, a bit ahead of the plant spot, so the ball is struck
            // from the turf just in front of the shooter. We deliberately do NOT call
            // Crosser.SetOrigin (which forces a 0.4m-high origin and re-plants the ragdoll): with
            // OriginOverride left null the Crosser launches from this fixed ground point, and the
            // ball rests here through the run-up, so there is no teleport hop up to hip height.
            Vector3 launchPos = _plantSpot + new Vector3(0f, SimConfig.BallRadius, 0.6f);
            var launch = Make.Empty("BgLaunch", launchPos, crosserGo.transform).transform;
            // Reticle-free (the null is now guarded inside Crosser), manual serve so WE time each shot.
            _crosser.Init(null, _ball, launch, _crosserRag);
            _crosser.AutoServe = false;

            // The shooter launches the ball by CODE, never a physical kick. Permanently ignore
            // collision between the ball and the shooter's own body so the swinging leg can't
            // deflect the launched ball sideways (that was making every shot squirt to a side
            // regardless of the aimed target). Only ball<->shooter pairs are ignored; the keeper's
            // body still collides with the ball, so saves are unaffected.
            _ball.IgnoreBody(_crosserRag, true);

            _ballHome = launchPos;   // ball rests on the ground here and launches from here
            _ball.ResetTo(_ballHome);
        }

        void Update()
        {
            if (_cam == null) return;
            // If Setup didn't fully wire the actors (e.g. a build issue aborted it), do nothing
            // rather than NRE every frame. The backdrop just stays static; the menu is unaffected.
            if (_crosserRag == null || _ball == null || _keeper == null) return;
            // Use SCALED delta so the whole reel (gait, timers, camera pacing) slows together with
            // the physics under our SlowMo timeScale. Keeper always reads the ball and reacts.
            float dt = Time.deltaTime;
            _clock += dt;
            _phaseT += dt;

            if (_keeper != null) _keeper.Tick();
            RunServeCycle(dt);
            DirectLive(dt);
        }

        // Phase machine: run in with a cosmetic gait, plant + swing + launch, let the save play, reset.
        void RunServeCycle(float dt)
        {
            if (_phase == 0)   // RUN-UP: jog from _runStart toward the plant spot, legs pumping.
            {
                // NOTE: do NOT tick the Crosser here - Crosser.Tick calls ClearPoseOverrides every
                // frame and would wipe the gait we set below. The Crosser stays idle until the swing.
                Vector3 me = _crosserRag.Pelvis != null ? _crosserRag.Pelvis.position : _plantSpot;
                Vector3 flat = new Vector3(me.x, 0f, me.z);
                Vector3 to = _plantSpot - flat; to.y = 0f;
                float dist = to.magnitude;
                Vector3 dir = dist > 0.05f ? to / dist : Vector3.forward;

                if (dist > PlantStopDist && _phaseT < RunupTimeout)
                {
                    // Steer + gait toward the plant spot (same idiom as SetPieceTaker.TickRunup).
                    _crosserRag.UprightLock = true;
                    _crosserRag.LocomotionEnabled = true;
                    _crosserRag.MoveInput = dir * RunupSpeed;
                    _crosserRag.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
                    RunGait(1f);
                }
                else
                {
                    // Arrived: stop, clear the gait, hand off to the Crosser's swing (KickSwing:
                    // windup, strike, follow-through, rebalance) which launches the ball at contact.
                    // Do NOT call SetOrigin - it would force a hip-high launch origin and re-plant the
                    // body; the Crosser uses the fixed ground launch point, so the BALL leaves the turf
                    // flat. The shooter himself does come off the ground on the follow-through, which
                    // releases his upright lock; the jog branch above re-asserts it every frame, so a
                    // shot that ends mid-air can never leave him lying down for the next repetition.
                    _crosserRag.MoveInput = Vector3.zero;
                    _crosserRag.FacingRotation = _shootFacing;
                    _crosserRag.ClearPoseOverrides();
                    _gaitPhase = 0f;
                    _ball.ResetTo(_ballHome);
                    _crosser.Arm(0f);                                   // idle-armed (AutoServe false)
                    _crosser.ServeNow(PickShotTarget(), lofted: false, powerMul: 0f); // driven flat + fast
                    _phase = 1;
                    _phaseT = 0f;
                }
            }
            else               // LIVE: tick the Crosser (plays the swing + fires), keeper dives.
            {
                if (_crosser != null) _crosser.Tick();
                if (_phaseT >= LiveDuration)
                {
                    if (_keeper != null) _keeper.ResetTo(_keeperHome);
                    _ball.ResetTo(_ballHome);
                    // Teleport the shooter back to the run-up start for the next repetition.
                    _crosserRag.ResetTo(_runStart, _shootFacing);
                    BeginRunup();
                }
            }
        }

        void BeginRunup()
        {
            _phase = 0;
            _phaseT = 0f;
            _gaitPhase = 0f;
        }

        // Cosmetic alternating-leg run + arm pump (same shape as Footballer.RunGait), applied as
        // pose overrides on top of the live locomotion so the legs visibly stride during the jog.
        void RunGait(float amount)
        {
            if (_crosserRag == null) return;
            _crosserRag.ClearPoseOverrides();
            if (amount < 0.05f) { _gaitPhase = 0f; return; }
            _gaitPhase += Time.deltaTime * SimConfig.StrideRateMax * amount;
            float s = Mathf.Sin(_gaitPhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _crosserRag.SetPoseOverride(Bone.ThighL, new Vector3(-s * SimConfig.GaitThighSwing - liftL * SimConfig.GaitThighLift, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.GaitKneeBend, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.ThighR, new Vector3(s * SimConfig.GaitThighSwing - liftR * SimConfig.GaitThighLift, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.GaitKneeBend, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.UpperArmR, new Vector3(s * SimConfig.ArmPumpSwing, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.ForearmR,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * SimConfig.ArmPumpSwing, 0f, 0f));
            _crosserRag.SetPoseOverride(Bone.ForearmL,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
        }

        // A hard, accurate shot into a random spot inside the goal mouth (hunts both corners and
        // varies height). Flat (lofted:false) + BallSpeedBoost = a rifled drive, not a floaty cross.
        Vector3 PickShotTarget()
        {
            float halfW = Mathf.Max(0.4f, SimConfig.GoalWidth * 0.5f - 0.6f);
            float tx = Random.Range(-halfW, halfW);
            float ty = Random.Range(0.4f, Mathf.Max(0.6f, SimConfig.GoalHeight - 0.4f));
            return new Vector3(tx, ty, _goalC.z);
        }

        // ---- Camera: a slow, continuous FULL 360 orbit around a FIXED pivot at the centre of the
        // scene (midway between the shooter and the goal). It does NOT track or zoom on the ball -
        // it just circles the whole scene at a constant radius, so the shot, keeper, and goal all
        // pass through frame as it comes around. No GameCamera, no direct timeScale writes here. ----
        void DirectLive(float dt)
        {
            // Fixed pivot: centre of the action (shooter is at goalC.z - PlantOutZ, goal at goalC.z).
            Vector3 pivot = _goalC + new Vector3(0f, MenuOrbitPivotY, -MenuOrbitBackZ);

            // Full slow circle, but at a VARIABLE rate: it eases down while the keeper is in shot and
            // back up once he is out of frame. Measured horizontally only - the framing question is
            // "has he gone off the side of the picture", and the downtilt just changes camera height,
            // which cannot push him out sideways. So pitch is not needed here and is left to below.
            //
            // Explicit integration: this frame's rate comes from where the camera ALREADY is, then the
            // angle advances. Solving the rate against the angle it produces would be simultaneous and
            // buys nothing at these speeds.
            //
            // Honest limitation: this tracks the keeper's HOME spot, not his live body. That is
            // deliberate - a keeper mid-dive is several metres off his line, and steering the orbit rate
            // off a moving ragdoll would surge the camera every time he threw himself sideways. His home
            // spot and the goal centre are within a metre of each other, so framing the spot frames him.
            Vector3 camNow = pivot + Quaternion.Euler(0f, _orbitYaw, 0f)
                                     * new Vector3(0f, 0f, -MenuOrbitRadius);
            Vector3 viewAxis = pivot - camNow;                       viewAxis.y = 0f;
            Vector3 toKeeper = _keeperHome - camNow;                 toKeeper.y = 0f;
            float offAxis = Vector3.Angle(viewAxis, toKeeper);       // deg off centre, horizontal
            float centred = 1f - Mathf.Clamp01((offAxis - KeeperHoldInner)
                                               / (KeeperHoldOuter - KeeperHoldInner));
            centred = centred * centred * (3f - 2f * centred);       // smoothstep the transition
            _orbitYaw += Mathf.Lerp(OrbitSpeed, OrbitSpeedKeeper, centred) * dt;
            float yaw = _orbitYaw;
            // Downtilt, and the reason it is no longer 8: the original 8 existed to keep the top of the
            // picture ABOVE the horizon haze that every shipped panorama has, because sky was all there
            // was up there. The stadium fills that band now, so the constraint is gone and the camera can
            // sit higher and look down into the bowl. Height works out at
            //   MenuOrbitPivotY + MenuOrbitRadius * sin(pitch) = 3.2 + 11.5 * sin(7 +/- 3)
            // which is 4.0 m at the bottom of the oscillation, 4.6 mean, 5.2 at the top - the same three
            // numbers as the old 1.6 + 11.5 * sin(15 +/- 3), which is the point of raising the pivot.
            float pitch = MenuOrbitPitch + Mathf.Sin(_clock * 0.15f) * 3f;   // gentle rise/fall as it circles
            // The radius has a HARD CEILING now that a stadium surrounds the lap, and it is worth
            // recording so it is not raised past it by eye. The far point of a lap sits at
            //   GoalCenter.z - MenuOrbitBackZ + MenuOrbitRadius = 17 - 13 + 11.5 = 15.5
            // i.e. 1.5 m SHORT of the goal line, and the stand behind that goal fronts a further
            // PitchLayout.StandFrontGap (8 m) back at 25, so the lap clears the structure by 9.5 m.
            // (Measured live: camera y = 5.1 at radius 13, pivot 13 m out - both matched prediction.)
            // Before the stadium existed this orbited 15 about a pivot only 5.5 m off the line, which
            // swung the camera through the advertising boards and into the lower tier once every lap.
            float dist = MenuOrbitRadius;                          // constant radius; frames the scene

            // Subtle handheld drift so the frame feels alive.
            yaw += (Mathf.PerlinNoise(_clock * 0.5f, 0f) - 0.5f) * 1.4f;
            pitch += (Mathf.PerlinNoise(0f, _clock * 0.5f) - 0.5f) * 1.0f;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            _cam.transform.position = pivot + rot * new Vector3(0f, 0f, -dist);
            _cam.transform.LookAt(pivot);
            _cam.fieldOfView = 46f;
        }

        Material M(Color c, float smoothness = 0.1f, float metallic = 0f)
        {
            var m = Make.Mat(c, smoothness, metallic);
            _mats.Add(m);
            return m;
        }

        public void Teardown()
        {
            if (this != null) Destroy(gameObject);
        }

        void OnDestroy()
        {
            // Restore the SimConfig sliders we tuned for the reel so nothing leaks into a match.
            SimConfig.KeeperAbility = _savedKeeperAbility;
            SimConfig.BallSpeedMul = _savedBallSpeedMul;

            // Restore time scale synchronously (BEFORE any match physics runs), mirroring how
            // GameCamera.OnDisable resets it. Never leave the menu's slow-mo applied globally.
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
            Time.fixedDeltaTime = _savedFixedDt > 0f ? _savedFixedDt : 0.02f;

            // Put the gameplay sun back. Parked in Awake, see the note there.
            for (int i = 0; i < _parked.Count; i++)
                if (_parked[i] != null) _parked[i].enabled = true;
            _parked.Clear();

            // Materials created here are not owned by any GameObject, so free them explicitly.
            for (int i = 0; i < _mats.Count; i++)
                if (_mats[i] != null) Destroy(_mats[i]);
            _mats.Clear();
        }
    }
}
