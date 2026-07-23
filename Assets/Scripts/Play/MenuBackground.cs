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
        const float KeeperAbilityLevel = 0.85f;  // high enough to reach fast shots and save a good share
        const float BallSpeedBoost     = 1.5f;   // multiplies launch speed (shortens flight) for punch
        const float PlantOutZ          = 11f;    // metres in front of goal the shooter plants + strikes from
        const float RunBackDist        = 5f;     // how far behind the plant spot the run-up starts
        const float RunupSpeed         = 5.0f;   // jog-in speed (m/s)
        const float PlantStopDist      = 0.35f;  // within this of the plant spot -> stop and swing
        const float RunupTimeout       = 3.0f;   // safety: force the swing if the jog stalls
        const float LiveDuration       = 2.9f;   // time a shot + save is allowed to play before reset
        const float SlowMo             = 0.7f;   // global time scale while the reel plays (restored on teardown)
        const float OrbitSpeed         = 22f;    // deg/sec the camera circles the action (full 360 loop)

        // Cosmetic run gait, mirroring Footballer.RunGait (procedural leg + arm pump via pose overrides).
        float _gaitPhase;

        Camera _cam;
        Light _light;
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
        Vector3 _pivotSmooth;   // damped camera pivot

        float _clock;           // free-running unscaled seconds
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

            // Start in the run-up phase: the shooter jogs in from _runStart, then plants and swings.
            BeginRunup();
            _pivotSmooth = _goalC + new Vector3(0f, 1.2f, -4f);
        }

        // ---- Static dressing: ground (with a real collider), pitch stripes, goal frame + net +
        // backstops. Built at the real goal coords so the AI aims true. ----
        void BuildScene()
        {
            Material grass = M(new Color(0.16f, 0.34f, 0.16f), 0.05f);
            Material stripe = M(new Color(0.19f, 0.39f, 0.19f), 0.05f);

            // Turf: a big slab with a SOLID collider (a live ragdoll grounds via a downward
            // spherecast and needs static ground under it; the old backdrop used collider:false
            // because its puppets were kinematic). Top surface at y = 0 so feet rest on it.
            var ground = Make.Box("BgGround", new Vector3(44f, 0.4f, 48f),
                                  _goalC + new Vector3(0f, -0.2f, -7f), grass, transform, collider: true);
            if (ground.TryGetComponent<Collider>(out var gcol))
                gcol.material = Make.PhysMat("BgTurf", 0.0f, 0.6f, 0.6f);
            for (int i = -5; i <= 5; i++)
            {
                if ((i & 1) == 0) continue;
                Make.Box("BgStripe", new Vector3(44f, 0.02f, 3.6f),
                         _goalC + new Vector3(0f, 0.011f, -7f + i * 3.6f), stripe, transform, collider: false);
            }

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
            var launch = Make.Empty("BgLaunch", _plantSpot + new Vector3(0f, 0.4f, 0.5f), crosserGo.transform).transform;
            // Reticle-free (the null is now guarded inside Crosser), manual serve so WE time each shot.
            _crosser.Init(null, _ball, launch, _crosserRag);
            _crosser.AutoServe = false;

            // Ball home = where SetOrigin (called at plant) puts the launch point:
            // plantSpot + aimDir*0.7 + up*0.4, aimDir = +Z toward goal.
            _ballHome = _plantSpot + Vector3.forward * 0.7f + Vector3.up * 0.4f;
            _ball.ResetTo(_ballHome);
        }

        void Update()
        {
            if (_cam == null) return;
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
                    // Arrived: stop, clear the gait, hand off to the Crosser's cosmetic swing which
                    // launches the ball at contact. Re-plant the launch origin at the exact stop spot.
                    _crosserRag.MoveInput = Vector3.zero;
                    _crosserRag.FacingRotation = _shootFacing;
                    _crosserRag.ClearPoseOverrides();
                    _gaitPhase = 0f;
                    _crosser.SetOrigin(_plantSpot);
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

        // ---- Camera: a slow, continuous FULL 360 orbit around the live action. The pivot tracks
        // the ball and slides toward the keeper as the ball nears the line so saves stay framed;
        // the yaw circles all the way around at OrbitSpeed. Wide and damped so ragdoll scrappiness
        // reads as live action. No GameCamera, no direct timeScale writes here. ----
        void DirectLive(float dt)
        {
            Vector3 ballPos = _ball != null ? _ball.transform.position : _goalC;
            Vector3 keeperPos = _keeper != null ? _keeper.PelvisPos : _goalC;

            // 0 when the ball is well out, 1 when it reaches the line -> blend the look to the keeper.
            float nearLine = Mathf.Clamp01(Mathf.InverseLerp(6f, 1.5f, _goalC.z - ballPos.z));
            Vector3 pivotTarget = Vector3.Lerp(ballPos, keeperPos, nearLine) + new Vector3(0f, 1.0f, 0f);
            _pivotSmooth = Vector3.Lerp(_pivotSmooth, pivotTarget, 1f - Mathf.Exp(-dt * 4f));

            // Full slow circle. _clock is scaled time, so the orbit slows with the reel.
            float yaw = _clock * OrbitSpeed;
            float pitch = 11f + Mathf.Sin(_clock * 0.15f) * 3f;   // gentle rise/fall as it circles
            float dist = 12f;

            // Subtle handheld drift so the frame feels alive.
            yaw += (Mathf.PerlinNoise(_clock * 0.5f, 0f) - 0.5f) * 1.4f;
            pitch += (Mathf.PerlinNoise(0f, _clock * 0.5f) - 0.5f) * 1.0f;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            _cam.transform.position = _pivotSmooth + rot * new Vector3(0f, 0f, -dist);
            _cam.transform.LookAt(_pivotSmooth);
            _cam.fieldOfView = 42f;
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

            // Materials created here are not owned by any GameObject, so free them explicitly.
            for (int i = 0; i < _mats.Count; i++)
                if (_mats[i] != null) Destroy(_mats[i]);
            _mats.Clear();
        }
    }
}
