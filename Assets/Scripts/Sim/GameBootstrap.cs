using UnityEngine;
using UnityEngine.InputSystem;

namespace Trickshot
{
    /// <summary>
    /// Single entry point. Builds the entire prototype at runtime so you just open
    /// the project and press Play: physics settings, lights, camera, arena, ball,
    /// crosser, active-ragdoll striker, keeper, input, and the game manager. Nothing
    /// is wired in the scene.
    ///
    /// It self-installs on load, so it also runs from the near-empty Main scene with
    /// no GameObjects in it.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        // Hidden 4th role. Off by default; flip to spawn a dormant Sniper scaffold
        // (see Sniper.cs). Even when spawned it does nothing until sniper.Active = true.
        const bool EnableSniper = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindAnyObjectByType<GameBootstrap>() != null) return;
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
        }

        Transform _root;
        Camera _cam;
        GameObject _camGo;

        void Awake()
        {
            ConfigurePhysics();
            _root = new GameObject("Trickshot").transform;

            // Lights
            MakeSun(_root);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.2f);

            // Camera
            _camGo = new GameObject("MainCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.backgroundColor = new Color(0.5f, 0.62f, 0.78f);
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 400f;
            _camGo.AddComponent<AudioListener>();

            // Start at the menu; the arena + chosen mode are built on selection.
            var menuGo = new GameObject("MenuUI");
            var menu = menuGo.AddComponent<MenuUI>();
            menu.Init(BuildMode);
        }

        void BuildMode(GameMode mode)
        {
            var root = _root;
            var cam = _cam;
            var camGo = _camGo;

            // --- Shared: arena, ball, camera controller ---
            var arena = Arena.Build(root);

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            ballGo.transform.SetParent(root, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            var ball = ballGo.AddComponent<BallController>();
            if (arena.net != null) arena.net.SetBall(ball.transform, SimConfig.BallRadius);

            var gameCam = camGo.AddComponent<GameCamera>();

            if (mode == GameMode.Goalkeeper) BuildKeeperMode(root, cam, gameCam, ball, arena);
            else BuildStrikerMode(root, cam, gameCam, ball, arena);
        }

        // ---------------------------------------------------------- Striker mode
        void BuildStrikerMode(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            var crosserGo = Make.Capsule("Crosser", 0.35f, 1.8f, SimConfig.CrosserStart + Vector3.up * 0.9f,
                                          Make.Mat(new Color(0.85f, 0.5f, 0.2f)), root);
            var crosser = crosserGo.AddComponent<Crosser>();
            var launch = Make.Empty("LaunchPoint", SimConfig.CrosserStart + new Vector3(0f, 0.5f, -0.4f), crosserGo.transform).transform;

            var reticleGo = Make.Empty("AimReticle", SimConfig.ReticleStart, root);
            var reticle = reticleGo.AddComponent<AimReticle>();
            reticle.Init(Make.Glow(new Color(1f, 0.85f, 0.2f)));

            var strikerGo = new GameObject("Striker");
            strikerGo.transform.SetParent(root, true);
            var ragdoll = strikerGo.AddComponent<ActiveRagdoll>();
            ragdoll.Build(SimConfig.StrikerStart, Quaternion.identity,
                          Make.Mat(new Color(0.2f, 0.45f, 0.85f)), Make.Mat(new Color(0.15f, 0.32f, 0.6f)));
            var striker = strikerGo.AddComponent<Striker>();
            striker.Init(GetInput(), ragdoll);
            AttachKickDetectors(ragdoll, striker, ball);

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, crosserGo.transform, arena.goalCenter);
            crosser.Init(reticle, ball, launch);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root, true);
            var gm = gmGo.AddComponent<GameManager>();
            gm.Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, null, gameCam, launch);

            foreach (var kd in strikerGo.GetComponentsInChildren<KickDetector>())
                kd.OnValidTrick += gm.NotifyValidTrick;

            if (EnableSniper)
            {
                var sniperGo = Make.Capsule("Sniper", 0.35f, 1.8f, SimConfig.SniperPerch,
                                            Make.Mat(new Color(0.15f, 0.15f, 0.18f)), root);
                var sniper = sniperGo.AddComponent<Sniper>();
                sniper.Init(ragdoll.Pelvis.transform, ball.transform);
            }

            ball.ResetTo(launch.position);
        }

        // -------------------------------------------------------- Goalkeeper mode
        void BuildKeeperMode(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            // The player IS the keeper: an active ragdoll (with arms) on the line.
            var keeperGo = new GameObject("KeeperPlayer");
            keeperGo.transform.SetParent(root, true);
            var ragdoll = keeperGo.AddComponent<ActiveRagdoll>();
            var facing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            ragdoll.Build(SimConfig.KeeperStart, facing,
                          Make.Mat(new Color(0.9f, 0.75f, 0.2f)), Make.Mat(new Color(0.7f, 0.55f, 0.15f)));
            var keeper = keeperGo.AddComponent<KeeperController>();
            keeper.Init(GetInput(), ragdoll);

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, null, arena.goalCenter);

            // Shot feeder (no crosser): on-target shots every few seconds.
            var serverGo = Make.Empty("ShotServer", Vector3.zero, root);
            var server = serverGo.AddComponent<ShotServer>();
            server.Init(ball);

            var kgGo = new GameObject("KeeperGame");
            kgGo.transform.SetParent(root, true);
            var kg = kgGo.AddComponent<KeeperGame>();
            kg.Configure(GetInput(), server, ball, keeper, ragdoll, gameCam);
        }

        GameInput _input;
        GameInput GetInput()
        {
            if (_input == null)
            {
                var go = new GameObject("GameInput");
                _input = go.AddComponent<GameInput>();
                _input.Init();
            }
            return _input;
        }

        void AttachKickDetectors(ActiveRagdoll ragdoll, Striker striker, BallController ball)
        {
            // Kicking leg = right foot + right calf.
            AddDetector(ragdoll.Rb(Bone.FootR), striker, ragdoll, ball);
            AddDetector(ragdoll.Rb(Bone.CalfR), striker, ragdoll, ball);
        }

        void AddDetector(Rigidbody rb, Striker striker, ActiveRagdoll ragdoll, BallController ball)
        {
            if (rb == null) return;
            var kd = rb.gameObject.AddComponent<KickDetector>();
            kd.Init(striker, ragdoll, ball);
        }

        void ConfigurePhysics()
        {
            Physics.gravity = new Vector3(0f, SimConfig.Gravity, 0f);
            Physics.defaultSolverIterations = 20;
            Physics.defaultSolverVelocityIterations = 8;
            Time.fixedDeltaTime = 0.02f;
            Physics.defaultContactOffset = 0.005f;
        }

        void MakeSun(Transform root)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(root, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.97f, 0.9f);
            l.intensity = 1.15f;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
        }
    }
}
