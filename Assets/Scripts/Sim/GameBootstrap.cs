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

        // Goalkeeper temporarily removed while tuning the striker. Flip on to restore.
        const bool EnableKeeper = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindAnyObjectByType<GameBootstrap>() != null) return;
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
        }

        void Awake()
        {
            ConfigurePhysics();
            var root = new GameObject("Trickshot").transform;

            // Lights
            MakeSun(root);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.4f, 0.42f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.22f, 0.2f);

            // Camera
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.backgroundColor = new Color(0.5f, 0.62f, 0.78f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();

            // Arena
            var arena = Arena.Build(root);

            // Ball
            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            ballGo.transform.SetParent(root, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            var ball = ballGo.AddComponent<BallController>();

            // Wire the ball into the flexible net so it billows on contact.
            if (arena.net != null) arena.net.SetBall(ball.transform, SimConfig.BallRadius);

            // Crosser (capsule near the wing) + launch point at its feet
            var crosserGo = Make.Capsule("Crosser", 0.35f, 1.8f, SimConfig.CrosserStart + Vector3.up * 0.9f,
                                          Make.Mat(new Color(0.85f, 0.5f, 0.2f)), root);
            var crosser = crosserGo.AddComponent<Crosser>();
            var launch = Make.Empty("LaunchPoint", SimConfig.CrosserStart + new Vector3(0f, 0.5f, -0.4f), crosserGo.transform).transform;

            // Reticle
            var reticleGo = Make.Empty("AimReticle", SimConfig.ReticleStart, root);
            var reticle = reticleGo.AddComponent<AimReticle>();
            reticle.Init(Make.Glow(new Color(1f, 0.85f, 0.2f)));

            // Striker (active ragdoll)
            var strikerGo = new GameObject("Striker");
            strikerGo.transform.SetParent(root, true);
            var ragdoll = strikerGo.AddComponent<ActiveRagdoll>();
            ragdoll.Build(SimConfig.StrikerStart, Quaternion.identity,
                          Make.Mat(new Color(0.2f, 0.45f, 0.85f)), Make.Mat(new Color(0.15f, 0.32f, 0.6f)));
            var striker = strikerGo.AddComponent<Striker>();
            striker.Init(GetInput(), ragdoll);
            // ragdoll pelvis transform is what the camera follows / faces
            AttachKickDetectors(ragdoll, striker, ball);

            // Keeper (kinematic capsule) - removed for now, gated off.
            Goalkeeper keeper = null;
            if (EnableKeeper)
            {
                var keeperGo = Make.Capsule("Goalkeeper", 0.38f, 1.9f, SimConfig.KeeperStart + Vector3.up * 0.95f,
                                             Make.Mat(new Color(0.9f, 0.85f, 0.2f)), root);
                var keeperRb = keeperGo.AddComponent<Rigidbody>();
                keeperRb.isKinematic = true;
                keeperRb.useGravity = false;
                keeperRb.interpolation = RigidbodyInterpolation.Interpolate;
                keeper = keeperGo.AddComponent<Goalkeeper>();
                keeper.Init(ball);
            }

            // Camera controller (mouse-orbit follow; GameManager sets the look source)
            var gameCam = camGo.AddComponent<GameCamera>();
            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, crosserGo.transform, arena.goalCenter);

            // Wire crosser (auto-server)
            crosser.Init(reticle, ball, launch);

            // Game manager
            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root, true);
            var gm = gmGo.AddComponent<GameManager>();
            gm.Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, keeper, gameCam, launch);

            // Route valid tricks to the manager for slow-mo replay.
            foreach (var kd in strikerGo.GetComponentsInChildren<KickDetector>())
                kd.OnValidTrick += gm.NotifyValidTrick;

            // Hidden 4th role: dormant sniper scaffold (off by default).
            if (EnableSniper)
            {
                var sniperGo = Make.Capsule("Sniper", 0.35f, 1.8f, SimConfig.SniperPerch,
                                            Make.Mat(new Color(0.15f, 0.15f, 0.18f)), root);
                var sniper = sniperGo.AddComponent<Sniper>();
                sniper.Init(ragdoll.Pelvis.transform, ball.transform);
                // sniper.Active stays false until the role is fleshed out.
            }

            // ball starts parked
            ball.ResetTo(launch.position);
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
