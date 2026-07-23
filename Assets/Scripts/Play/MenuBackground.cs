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
        const float CrosserOutZ        = 11f;    // metres in front of goal the shooter stands
        const float ReadyHold          = 1.4f;   // settle + framing pause before each serve
        const float LiveDuration       = 2.6f;   // time a shot + save is allowed to play before reset

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
        Vector3 _pivotSmooth;   // damped camera pivot

        float _clock;           // free-running unscaled seconds
        float _phaseT;          // seconds in the current phase
        int _phase;             // 0 = ready (waiting to serve), 1 = live (shot + save playing)

        // Mutable SimConfig sliders we set for the reel and restore on teardown.
        float _savedKeeperAbility;
        float _savedBallSpeedMul;

        public void Setup()
        {
            _goalC = SimConfig.GoalCenter;
            _keeperHome = SimConfig.KeeperStart;

            // Punchy, high-ability shots for the reel. Cache first, restore in OnDestroy.
            _savedKeeperAbility = SimConfig.KeeperAbility;
            _savedBallSpeedMul = SimConfig.BallSpeedMul;
            SimConfig.KeeperAbility = KeeperAbilityLevel;
            SimConfig.BallSpeedMul = BallSpeedBoost;

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

            // Start in the ready phase; the first serve fires after ReadyHold once bodies settle.
            _phase = 0;
            _phaseT = 0f;
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

            // AI shooter (Crosser: a live ragdoll that plays a run-up + swing and code-launches the
            // ball at contact), same wiring as GameBootstrap.BuildCrosser but reticle-free and manual.
            Vector3 crosserSpot = new Vector3(0f, 0f, _goalC.z - CrosserOutZ);
            var crosserGo = new GameObject("BgShooter");
            crosserGo.transform.SetParent(transform, true);
            _crosserRag = crosserGo.AddComponent<ActiveRagdoll>();
            Vector3 toGoal = _goalC - crosserSpot; toGoal.y = 0f;
            var cFacing = Quaternion.LookRotation(toGoal.normalized, Vector3.up);
            _crosserRag.Build(crosserSpot, cFacing,
                              M(new Color(0.15f, 0.32f, 0.6f)), M(new Color(0.12f, 0.26f, 0.5f)),
                              withGloves: false);
            _crosser = crosserGo.AddComponent<Crosser>();
            var launch = Make.Empty("BgLaunch", crosserSpot + new Vector3(0f, 0.4f, 0.5f), crosserGo.transform).transform;
            // Reticle-free (the null is now guarded inside Crosser), manual serve so WE time each shot.
            _crosser.Init(null, _ball, launch, _crosserRag);
            _crosser.AutoServe = false;
            _crosser.SetOrigin(crosserSpot);            // stand facing goal, launch from ahead of the feet
            _crosser.Arm(0f);                           // AutoServe false -> stays idle until ServeNow

            // Ball home = where SetOrigin puts the launch point (spot + aimDir*0.7 + up*0.4), aimDir +Z.
            _ballHome = crosserSpot + Vector3.forward * 0.7f + Vector3.up * 0.4f;
            _ball.ResetTo(_ballHome);
        }

        void Update()
        {
            if (_cam == null) return;
            float dt = Time.unscaledDeltaTime;
            _clock += dt;
            _phaseT += dt;

            // Drive the live AI every frame. The shooter idles until ServeNow; the keeper always
            // reads the ball and reacts.
            if (_crosser != null) _crosser.Tick();
            if (_keeper != null) _keeper.Tick();

            RunServeCycle();
            DirectLive(dt);
        }

        // Serve loop: wait, fire a powerful accurate shot, let the save play out, hard-reset, repeat.
        void RunServeCycle()
        {
            if (_phase == 0)   // ready: bodies settling, framing the goal, about to serve
            {
                if (_phaseT >= ReadyHold && _crosser != null && _crosser.ReadyToServe)
                {
                    _ball.ResetTo(_ballHome);
                    _crosser.ServeNow(PickShotTarget(), lofted: false, powerMul: 0f);  // driven flat + fast
                    _phase = 1;
                    _phaseT = 0f;
                }
            }
            else               // live: shot in flight, keeper diving; then reset for the next one
            {
                if (_phaseT >= LiveDuration)
                {
                    if (_keeper != null) _keeper.ResetTo(_keeperHome);
                    _ball.ResetTo(_ballHome);
                    _phase = 0;
                    _phaseT = 0f;
                }
            }
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

        // ---- Camera: follow the live action. Orbit + LookAt like PlayerPreview, but the pivot
        // tracks the ball and slides toward the keeper as the ball nears the line so saves are
        // framed. Wide and damped so ragdoll scrappiness reads as live action. No GameCamera, no
        // Time.timeScale. ----
        void DirectLive(float dt)
        {
            Vector3 ballPos = _ball != null ? _ball.transform.position : _goalC;
            Vector3 keeperPos = _keeper != null ? _keeper.PelvisPos : _goalC;

            // 0 when the ball is well out, 1 when it reaches the line -> blend the look to the keeper.
            float nearLine = Mathf.Clamp01(Mathf.InverseLerp(6f, 1.5f, _goalC.z - ballPos.z));
            Vector3 pivotTarget = Vector3.Lerp(ballPos, keeperPos, nearLine) + new Vector3(0f, 1.0f, 0f);
            _pivotSmooth = Vector3.Lerp(_pivotSmooth, pivotTarget, 1f - Mathf.Exp(-dt * 4f));

            float yaw = 62f + Mathf.Sin(_clock * 0.22f) * 12f;   // slow side-on drift, shows the dive
            float pitch = 9f;
            float dist = 11.5f;

            // Subtle handheld drift so the frame feels alive (deterministic, unscaled-clock driven).
            yaw += (Mathf.PerlinNoise(_clock * 0.6f, 0f) - 0.5f) * 1.6f;
            pitch += (Mathf.PerlinNoise(0f, _clock * 0.6f) - 0.5f) * 1.0f;

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

            // Materials created here are not owned by any GameObject, so free them explicitly.
            for (int i = 0; i < _mats.Count; i++)
                if (_mats[i] != null) Destroy(_mats[i]);
            _mats.Clear();
        }
    }
}
