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
        static readonly bool EnableSniper = false;   // static readonly (not const) so the guarded block isn't flagged unreachable

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

            ShowMainMenu();
        }

        // ---- Screen flow: main menu -> pre-match settings -> match (+ pause menu) ----
        GameObject _matchRoot;   // holds everything spawned for a running match

        void ShowMainMenu()
        {
            var menuGo = new GameObject("MenuUI");
            var menu = menuGo.AddComponent<MenuUI>();
            menu.Init(
                onChoose: mode => { Destroy(menuGo); ShowStadiumSelect(mode); },
                onMultiplayer: () => { Destroy(menuGo); ShowMultiplayerHub(); });
        }

        // ---- Multiplayer flow: hub -> host setup / browser -> lobby -> networked match ----
        void ShowMultiplayerHub()
        {
            var go = new GameObject("MultiplayerHubUI");
            go.AddComponent<MultiplayerHubUI>().Init(
                onHost: () => { Destroy(go); ShowHostSetup(); },
                onJoin: () => { Destroy(go); ShowSessionBrowser(); },
                onBack: () => { Destroy(go); ShowMainMenu(); });
        }

        void ShowHostSetup()
        {
            var go = new GameObject("HostSetupUI");
            go.AddComponent<HostSetupUI>().Init(
                onCreated: () => { Destroy(go); ShowLobby(); },
                onBack:    () => { Destroy(go); ShowMultiplayerHub(); });
        }

        void ShowSessionBrowser()
        {
            var go = new GameObject("SessionBrowserUI");
            go.AddComponent<SessionBrowserUI>().Init(
                onJoined: () => { Destroy(go); ShowLobby(); },
                onBack:   () => { Destroy(go); ShowMultiplayerHub(); });
        }

        void ShowLobby()
        {
            var go = new GameObject("LobbyUI");
            go.AddComponent<LobbyUI>().Init(
                onCustomize: () => { Destroy(go); ShowLobbyCustomize(); },
                onStart:     () => { Destroy(go); StartNetworkedMatch(); },
                onLeave:     () => { Destroy(go); Trickshot.Net.Multiplayer.End(); ShowMultiplayerHub(); });
        }

        // Customize your own player from the lobby, then return to the lobby.
        void ShowLobbyCustomize()
        {
            var go = new GameObject("CustomizeUI");
            go.AddComponent<CustomizeUI>().Init(
                onDone: () => { Destroy(go); ShowLobby(); },
                onBack: () => { Destroy(go); ShowLobby(); });
        }

        // Apply the host's synced config, then build the chosen mode with the session live.
        void StartNetworkedMatch()
        {
            var s = Trickshot.Net.Multiplayer.Session;
            var cfg = s.Config;
            StadiumStyle.SelectedIndex = cfg.stadium;
            var mode = (GameMode)cfg.mode;
            if (mode == GameMode.Scrimmage)
            {
                SimConfig.ScrimmagePerSide = cfg.perSide;
                SimConfig.ScrimmageMatchSeconds = cfg.matchSec;
                // This peer plays whatever slot the host assigned it (keeper slot 0 -> keeper).
                SimConfig.ScrimmageRole = s.LocalRole == Trickshot.Net.NetRole.Keeper
                    ? SimConfig.ScrimRole.Keeper : SimConfig.ScrimRole.Outfield;
            }
            BuildMode(mode);
        }

        // Mode -> pick stadium -> (customize your player, striker modes) -> pre-match -> play.
        void ShowStadiumSelect(GameMode mode)
        {
            var go = new GameObject("StadiumSelectUI");
            var ss = go.AddComponent<StadiumSelectUI>();
            ss.Init(
                onPicked: () => { Destroy(go); AfterStadium(mode); },
                onBack:   () => { Destroy(go); ShowMainMenu(); });
        }

        // Customization applies to the player STRIKER, so keeper mode skips it.
        static bool UsesCustomPlayer(GameMode mode) => mode != GameMode.Goalkeeper;

        void AfterStadium(GameMode mode)
        {
            if (UsesCustomPlayer(mode)) ShowCustomize(mode);
            else ShowPrematch(mode);
        }

        void ShowCustomize(GameMode mode)
        {
            var go = new GameObject("CustomizeUI");
            var cu = go.AddComponent<CustomizeUI>();
            cu.Init(
                onDone: () => { Destroy(go); ShowPrematch(mode); },
                onBack: () => { Destroy(go); ShowStadiumSelect(mode); });
        }

        void ShowPrematch(GameMode mode)
        {
            var go = new GameObject("PrematchUI");
            var pm = go.AddComponent<PrematchUI>();
            pm.Init(mode,
                onStart: m => { Destroy(go); BuildMode(m); },
                // Back goes to the previous screen: Customize for striker modes, the
                // stadium picker for keeper mode (which skips Customize). AfterStadium is
                // a forward-router and would re-show Prematch for keeper, so branch here.
                onBack:  () => { Destroy(go); if (UsesCustomPlayer(mode)) ShowCustomize(mode); else ShowStadiumSelect(mode); });
        }

        // Tears down the running match (match objects + camera controller) and restores
        // time. Shared by both pause-menu exits.
        void TearDownMatch()
        {
            if (_matchRoot != null) Destroy(_matchRoot);
            var gc = _camGo.GetComponent<GameCamera>();
            if (gc != null) Destroy(gc);
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }

        void ReturnToMainMenu()
        {
            TearDownMatch();
            Trickshot.Net.Multiplayer.End();   // end any networked session on quit-to-menu
            ShowMainMenu();
        }

        // Pause -> Match Setup: tear the match down and reopen the pre-match config for the
        // same mode. Start rebuilds the match; Back walks to the previous pregame screen.
        void ReturnToMatchSetup(GameMode mode)
        {
            TearDownMatch();
            ShowPrematch(mode);
        }

        void BuildMode(GameMode mode)
        {
            // Everything for this match lives under _matchRoot so it can be torn down.
            _matchRoot = new GameObject("Match");
            _matchRoot.transform.SetParent(_root, false);
            var root = _matchRoot.transform;
            var cam = _cam;
            var camGo = _camGo;

            // Pause menu (Esc): Resume / Match Setup / Options / Main Menu.
            var pauseGo = new GameObject("PauseMenu");
            pauseGo.transform.SetParent(root, false);
            pauseGo.AddComponent<PauseMenu>().Init(ReturnToMainMenu, () => ReturnToMatchSetup(mode), GetInput());

            // Networked match: pump the transport every frame for the match's lifetime.
            if (Trickshot.Net.Multiplayer.IsActive)
                pauseGo.AddComponent<Trickshot.Net.NetPump>();

            _cam.backgroundColor = StadiumStyle.Active.Sky;

            // Default the aim-target to the training goal; scrimmage repoints it.
            SimConfig.AttackGoalCenter = SimConfig.GoalCenter;

            // Scrimmage builds its OWN two-goal, fully-walled pitch (not the single-goal
            // training arena / regulation pitch / stadium), then spawns teams.
            if (mode == GameMode.Scrimmage) { BuildScrimmageMode(root, camGo); return; }

            // --- Shared: arena, full pitch, stadium, crowd, ball, camera controller ---
            // Striker mode (single-player + networked) plays on an OPEN field: no boundary
            // walls around the pitch. Other single-goal modes keep the walls.
            var arena = Arena.Build(root, boundaryWalls: mode != GameMode.Striker);
            // Full pitch markings + far goal, the stadium bowl, and the animated crowd.
            // All read the shared PitchLayout contract so they line up. Crowd is stored so
            // goal callouts can make it Celebrate().
            PitchBuilder.Build(root);
            StadiumBuilder.Build(root);
            _crowd = Crowd.Create(root);
            CrowdCheer.Register(_crowd);   // drivers call CrowdCheer.Celebrate() on goals

            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            ballGo.transform.SetParent(root, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            var ball = ballGo.AddComponent<BallController>();
            if (arena.net != null) arena.net.SetBall(ball.transform, SimConfig.BallRadius);

            var gameCam = camGo.AddComponent<GameCamera>();

            // Networked striker: host-authoritative multi-player striker driver instead of
            // the single-player GameManager. (Scrimmage networking is a later pass.)
            if (Trickshot.Net.Multiplayer.IsActive && mode == GameMode.Striker)
            {
                BuildNetStrikerMode(root, cam, gameCam, ball, arena);
                return;
            }

            switch (mode)
            {
                case GameMode.Goalkeeper: BuildKeeperMode(root, cam, gameCam, ball, arena); break;
                case GameMode.Freeplay:
                case GameMode.TimeTrial:
                case GameMode.Accuracy:   BuildChallengeMode(mode, root, cam, gameCam, ball, arena); break;
                case GameMode.FreeKick:   BuildFreeKickMode(root, cam, gameCam, ball, arena); break;
                default:                  BuildStrikerMode(root, cam, gameCam, ball, arena); break;
            }
        }

        // Networked striker: shared arena + crosser + ball, plus the NetStrikerMatch driver
        // which spawns a body per slot and runs the host-authoritative sync.
        void BuildNetStrikerMode(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            BuildCrosser(root, ball, out var crosser, out var crosserRagdoll, out var launch, out var reticle);
            ball.SetCamera(gameCam);

            Material torso = JerseyMaterial();
            Material limb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material glove = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            var go = new GameObject("NetStrikerMatch");
            go.transform.SetParent(root, true);
            go.AddComponent<NetStrikerMatch>()
              .Configure(GetInput(), cam, gameCam, ball, crosser, reticle, launch, torso, limb, glove, root);
            LockCursor();
            ball.ResetTo(launch.position);
        }

        Crowd _crowd;   // shared crowd, so modes can Celebrate() on goals

        // ---------------------------------------------------------- Striker mode
        void BuildStrikerMode(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            // Crosser is now an active-ragdoll character that plays a leg-swing; the ball
            // still launches perfectly by code. Faces roughly toward the goal centre.
            var crosserGo = new GameObject("Crosser");
            crosserGo.transform.SetParent(root, true);
            var crosserRagdoll = crosserGo.AddComponent<ActiveRagdoll>();
            Vector3 toGoalFlat = SimConfig.GoalCenter - SimConfig.CrosserStart; toGoalFlat.y = 0f;
            var crosserFacing = Quaternion.LookRotation(toGoalFlat.normalized, Vector3.up);
            crosserRagdoll.Build(SimConfig.CrosserStart, crosserFacing,
                                 Make.Mat(new Color(0.85f, 0.5f, 0.2f)), Make.Mat(new Color(0.65f, 0.38f, 0.15f)),
                                 withGloves: false);
            var crosser = crosserGo.AddComponent<Crosser>();
            var launch = Make.Empty("LaunchPoint", SimConfig.CrosserStart + new Vector3(0f, 0.4f, 0.5f), crosserGo.transform).transform;

            var reticleGo = Make.Empty("AimReticle", SimConfig.ReticleStart, root);
            var reticle = reticleGo.AddComponent<AimReticle>();
            reticle.Init(Make.Glow(new Color(1f, 0.85f, 0.2f)));

            // Player striker: scaled to the customized build and wearing the painted jersey.
            // Striker mode is shooting-on-goal, so dribbling stays OFF (default).
            BuildStrikerPlayer(root, ball, out var striker, out var ragdoll, out _);

            // AI keeper: an active-ragdoll goaltender (with gloves) that shuffles + dives.
            Goalkeeper keeper = null;
            if (SimConfig.KeeperAbility > 0.001f)
            {
                var keeperGo = new GameObject("Goalkeeper");
                keeperGo.transform.SetParent(root, true);
                var keeperRagdoll = keeperGo.AddComponent<ActiveRagdoll>();
                var kFacing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
                keeperRagdoll.Build(SimConfig.KeeperStart, kFacing,
                                    Make.Mat(new Color(0.9f, 0.85f, 0.2f)), Make.Mat(new Color(0.7f, 0.62f, 0.15f)));
                keeper = keeperGo.AddComponent<Goalkeeper>();
                keeper.Init(keeperRagdoll, ball);
            }

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, crosserRagdoll.Pelvis.transform, arena.goalCenter);
            ball.SetCamera(gameCam);   // auto ball-cam on a shot
            crosser.Init(reticle, ball, launch, crosserRagdoll);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root, true);
            var gm = gmGo.AddComponent<GameManager>();
            gm.Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, keeper, gameCam, launch);
            LockCursor();

            foreach (var kd in striker.GetComponentsInChildren<KickDetector>())
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

        // ---- Shared builders reused by the challenge modes ----

        // Builds the player striker (ragdoll + Striker + kick detectors), scaled to the
        // customized height/weight and wearing the painted jersey. Returns the striker,
        // ragdoll, and its Dribble component (disabled by default; the mode opts in).
        void BuildStrikerPlayer(Transform root, BallController ball,
                                out Striker striker, out ActiveRagdoll ragdoll, out Dribble dribble)
        {
            var strikerGo = new GameObject("Striker");
            strikerGo.transform.SetParent(root, true);
            ragdoll = strikerGo.AddComponent<ActiveRagdoll>();
            Material torso = JerseyMaterial();
            Material limbs = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            ragdoll.BuildScaled(SimConfig.StrikerStart, Quaternion.identity, torso, limbs,
                                PlayerProfile.HeightScale, PlayerProfile.GirthScale, PlayerProfile.EffectiveMassMul,
                                withGloves: false);
            striker = strikerGo.AddComponent<Striker>();
            striker.Init(GetInput(), ragdoll);
            AttachKickDetectors(ragdoll, striker, ball);

            // Arcade close-control dribbling: soft-magnet the ball to the feet, release on
            // a kick. Lives on the striker so it ticks with him and tears down with the match.
            // DISABLED by default - only a real-match mode enables it (dribble.Enabled = true);
            // the goal-shooting modes leave it off so the ball never snaps to the feet.
            dribble = strikerGo.AddComponent<Dribble>();
            dribble.Init(GetInput(), striker, ragdoll, ball);
            striker.SetDribble(dribble);   // striker slows + turns slower while carrying
        }

        // Torso material for the player: the painted jersey texture if one exists, else
        // the plain jersey base colour.
        static Material JerseyMaterial()
        {
            return PlayerProfile.JerseyTex != null
                ? Make.MatTex(PlayerProfile.JerseyTex)
                : Make.Mat(PlayerProfile.JerseyBase);
        }

        // Builds the ragdoll crosser + its launch point + the aim reticle.
        void BuildCrosser(Transform root, BallController ball,
                          out Crosser crosser, out ActiveRagdoll crosserRagdoll,
                          out Transform launch, out AimReticle reticle)
        {
            var crosserGo = new GameObject("Crosser");
            crosserGo.transform.SetParent(root, true);
            crosserRagdoll = crosserGo.AddComponent<ActiveRagdoll>();
            Vector3 toGoalFlat = SimConfig.GoalCenter - SimConfig.CrosserStart; toGoalFlat.y = 0f;
            var crosserFacing = Quaternion.LookRotation(toGoalFlat.normalized, Vector3.up);
            crosserRagdoll.Build(SimConfig.CrosserStart, crosserFacing,
                                 Make.Mat(new Color(0.85f, 0.5f, 0.2f)), Make.Mat(new Color(0.65f, 0.38f, 0.15f)),
                                 withGloves: false);
            crosser = crosserGo.AddComponent<Crosser>();
            launch = Make.Empty("LaunchPoint", SimConfig.CrosserStart + new Vector3(0f, 0.4f, 0.5f), crosserGo.transform).transform;
            var reticleGo = Make.Empty("AimReticle", SimConfig.ReticleStart, root);
            reticle = reticleGo.AddComponent<AimReticle>();
            reticle.Init(Make.Glow(new Color(1f, 0.85f, 0.2f)));
            crosser.Init(reticle, ball, launch, crosserRagdoll);
        }

        // Builds an AI goalkeeper ragdoll (with gloves). Returns null if ability is ~0.
        Goalkeeper BuildAiKeeper(Transform root, BallController ball, out ActiveRagdoll keeperRagdoll)
        {
            keeperRagdoll = null;
            if (SimConfig.KeeperAbility <= 0.001f) return null;
            var keeperGo = new GameObject("Goalkeeper");
            keeperGo.transform.SetParent(root, true);
            keeperRagdoll = keeperGo.AddComponent<ActiveRagdoll>();
            var kFacing = Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
            keeperRagdoll.Build(SimConfig.KeeperStart, kFacing,
                                Make.Mat(new Color(0.9f, 0.85f, 0.2f)), Make.Mat(new Color(0.7f, 0.62f, 0.15f)));
            var keeper = keeperGo.AddComponent<Goalkeeper>();
            keeper.Init(keeperRagdoll, ball);
            return keeper;
        }

        // ------------------------------------------- Freeplay / Time Trial / Accuracy
        void BuildChallengeMode(GameMode mode, Transform root, Camera cam, GameCamera gameCam,
                                BallController ball, Arena.Refs arena)
        {
            BuildCrosser(root, ball, out var crosser, out var crosserRagdoll, out var launch, out var reticle);
            BuildStrikerPlayer(root, ball, out var striker, out var ragdoll, out var dribble);

            // Freeplay is the open sandbox: enable dribbling there. Time Trial / Accuracy are
            // score-on-goal, so they leave it off (the ball never sticks to the feet).
            dribble.Enabled = mode == GameMode.Freeplay;

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, crosserRagdoll.Pelvis.transform, arena.goalCenter);
            ball.SetCamera(gameCam);   // auto ball-cam on a shot

            var go = new GameObject(mode + "Game");
            go.transform.SetParent(root, true);
            if (mode == GameMode.Freeplay)
                go.AddComponent<FreeplayGame>().Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, gameCam, launch);
            else if (mode == GameMode.TimeTrial)
                go.AddComponent<TimeTrialGame>().Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, gameCam, launch);
            else
                go.AddComponent<AccuracyGame>().Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, gameCam, launch);

            LockCursor();
            ball.ResetTo(launch.position);
        }

        // ------------------------------------------------ Free Kick / Penalty mode
        void BuildFreeKickMode(Transform root, Camera cam, GameCamera gameCam,
                               BallController ball, Arena.Refs arena)
        {
            BuildStrikerPlayer(root, ball, out var striker, out var ragdoll, out var dribble);
            // Set piece: dribbling stays OFF, and the set-piece flag guarantees the ball
            // parked at the spot is never auto-captured to the feet as the taker walks up.
            dribble.Enabled = false;
            dribble.SetPieceActive = true;
            var keeper = BuildAiKeeper(root, ball, out var keeperRagdoll);

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, null, arena.goalCenter);
            gameCam.SetFollow(ragdoll.Pelvis.transform, () => GetInput().Look);
            ball.SetCamera(gameCam);   // auto ball-cam on a shot
            striker.SetCameraYaw(() => gameCam.Yaw);

            var wall = new DefensiveWall();
            var go = new GameObject("FreeKickGame");
            go.transform.SetParent(root, true);
            var fk = go.AddComponent<FreeKickGame>();
            fk.Configure(GetInput(), ball, striker, ragdoll, keeper, keeperRagdoll, wall, gameCam);

            foreach (var kd in striker.GetComponentsInChildren<KickDetector>())
                kd.OnValidTrick += fk.NotifyValidTrick;

            LockCursor();
        }

        // -------------------------------------------------------- Scrimmage mode
        void BuildScrimmageMode(Transform root, GameObject camGo)
        {
            int perSide = SimConfig.ScrimmagePerSide;
            var arena = ScrimmageArena.Build(root, perSide);
            // The human (Home) attacks the +Z goal; aim assist / dribble / ball-cam target it.
            SimConfig.AttackGoalCenter = arena.homeGoalCenter;

            // Ball.
            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            ballGo.transform.SetParent(root, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            var ball = ballGo.AddComponent<BallController>();

            var gameCam = camGo.AddComponent<GameCamera>();

            // Team colours.
            Material homeTorso = JerseyMaterial();                          // player's painted kit for Home
            Material homeLimb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material awayTorso = Make.Mat(new Color(0.75f, 0.2f, 0.2f));
            Material awayLimb  = Make.Mat(new Color(0.5f, 0.13f, 0.13f));
            Material gloveMat  = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            var gmGo = new GameObject("ScrimmageGame");
            gmGo.transform.SetParent(root, true);
            var game = gmGo.AddComponent<ScrimmageGame>();

            var home = new System.Collections.Generic.List<Footballer>();
            var away = new System.Collections.Generic.List<Footballer>();

            // Team size is TOTAL players per side INCLUDING the keeper, so outfield = perSide-1
            // (e.g. 11v11 = 10 outfield + 1 GK). At least one outfielder regardless.
            int outfield = Mathf.Max(1, perSide - 1);

            // Spawn outfielders for both teams.
            for (int t = 0; t < 2; t++)
            {
                var list = t == 0 ? home : away;
                Material torso = t == 0 ? homeTorso : awayTorso;
                Material limb  = t == 0 ? homeLimb  : awayLimb;
                for (int i = 0; i < outfield; i++)
                    list.Add(BuildFootballer(root, ball, game, t, keeper: false, torso, limb, gloveMat: null, index: i));
            }

            // Keepers (both AI unless the player picks the keeper role for Home).
            bool humanKeeper = SimConfig.ScrimmageRole == SimConfig.ScrimRole.Keeper;
            Footballer homeKeeper = null, awayKeeper = null;
            KeeperController humanKeeperCtrl = null; ActiveRagdoll humanKeeperRag = null;

            awayKeeper = BuildFootballer(root, ball, game, team: 1, keeper: true, awayTorso, awayLimb, gloveMat, index: 0);

            if (humanKeeper)
            {
                // Human keeper: a KeeperController ragdoll at the Home end (defends -Z goal).
                var kGo = new GameObject("HumanKeeper");
                kGo.transform.SetParent(root, true);
                humanKeeperRag = kGo.AddComponent<ActiveRagdoll>();
                var facing = Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.up);
                humanKeeperRag.Build(new Vector3(0f, 0f, arena.awayGoalCenter.z + 1.0f), facing, homeTorso, homeLimb);
                humanKeeperCtrl = kGo.AddComponent<KeeperController>();
                humanKeeperCtrl.Init(GetInput(), humanKeeperRag);
                // 5th arg (goal Transform) is only used by the unused Broadcast cam; pass null.
                gameCam.Init(_cam, ball.transform, humanKeeperRag.Pelvis.transform, null, null);
                gameCam.SetKeeperFollow(humanKeeperRag.Pelvis.transform,
                    () => Quaternion.LookRotation(new Vector3(0f, 0f, -1f), Vector3.up), () => GetInput().Look);
                humanKeeperCtrl.SetLookYawSource(() => gameCam.KeeperLookYaw);
            }
            else
            {
                homeKeeper = BuildFootballer(root, ball, game, team: 0, keeper: true, homeTorso, homeLimb, gloveMat, index: 0);
                // Outfield role: the driver assigns control to a fixed Home player and sets
                // the camera follow. Init with a valid transform; 5th arg (goal) unused -> null.
                gameCam.Init(_cam, ball.transform, home[0].Ragdoll.Pelvis.transform, null, null);
            }

            // The human's striker/dribble refs point at whichever Home player is controlled;
            // the driver assigns control in Configure/Kickoff. Pass the first home player's
            // components as an initial handle.
            Striker humanStriker = home.Count > 0 ? home[0].GetComponent<Striker>() : null;
            Dribble humanDribble = home.Count > 0 ? home[0].GetComponent<Dribble>() : null;

            ball.SetCamera(gameCam);
            game.Configure(GetInput(), ball, gameCam, arena, SimConfig.ScrimmageRole,
                           home, away, homeKeeper, awayKeeper,
                           humanStriker, humanDribble, humanKeeperCtrl, humanKeeperRag);
            LockCursor();
        }

        // Builds one scrimmage footballer: an active ragdoll + Striker + Dribble + kick
        // detectors + a Footballer AI component. Striker/Dribble are DISABLED (AI/idle)
        // until the driver hands this body control.
        Footballer BuildFootballer(Transform root, BallController ball, ScrimmageGame game,
                                   int team, bool keeper, Material torso, Material limb,
                                   Material gloveMat, int index)
        {
            var go = new GameObject((team == 0 ? "Home" : "Away") + (keeper ? "GK" : "P" + index));
            go.transform.SetParent(root, true);
            var ragdoll = go.AddComponent<ActiveRagdoll>();
            var facing = Quaternion.LookRotation(new Vector3(0f, 0f, team == 0 ? 1f : -1f), Vector3.up);
            ragdoll.Build(new Vector3(0f, 0f, 0f), facing, torso, limb, withGloves: keeper && gloveMat != null);

            var striker = go.AddComponent<Striker>();
            striker.Init(GetInput(), ragdoll);
            striker.ControlEnabled = false;   // AI by default; driver flips this on takeover
            AttachKickDetectors(ragdoll, striker, ball);

            var dribble = go.AddComponent<Dribble>();
            dribble.Init(GetInput(), striker, ragdoll, ball);
            striker.SetDribble(dribble);
            dribble.Enabled = false;

            // Celebration emotes (played when the human controls this body + opens the wheel).
            go.AddComponent<Celebration>().Init(ragdoll);
            // Knockdown: fall over when tackled / slide-tackled.
            go.AddComponent<Knockdown>().Init(ragdoll);

            var f = go.AddComponent<Footballer>();
            // Home (team 0) attacks +Z (HomeGoal), Away attacks -Z, in every role.
            float attackZ = team == 0 ? 1f : -1f;
            f.Init(game, ball, ragdoll, team, keeper, attackZ, Vector3.zero);
            return f;
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
            LockCursor();
        }

        static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            // BOTH legs get detectors so a bicycle scored off either foot classifies (the
            // right foot is the strong-side default, but a left-foot bike must count too).
            AddDetector(ragdoll.Rb(Bone.FootR), striker, ragdoll, ball);
            AddDetector(ragdoll.Rb(Bone.CalfR), striker, ragdoll, ball);
            AddDetector(ragdoll.Rb(Bone.FootL), striker, ragdoll, ball);
            AddDetector(ragdoll.Rb(Bone.CalfL), striker, ragdoll, ball);
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
