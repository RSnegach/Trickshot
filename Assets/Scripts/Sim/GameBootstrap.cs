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
            // Keep the player loop running while the window is not focused. This is a MULTIPLAYER
            // correctness requirement, not a convenience: a paused loop stops pumping the UDP
            // transport, so keepalives stop going out and every peer trips the 5s timeout. A host
            // who clicked another window used to end the match for everyone. Set here as well as in
            // PlayerSettings (see Assets/Editor/BuildAll.cs) so the editor behaves like a build.
            Application.runInBackground = true;

            // Resolution / window mode / vsync / UI scale, as the player last set them (Settings ->
            // Camera). Runs before anything draws so the first frame is already the right size.
            DisplaySettings.ApplyOnBoot();

            if (FindAnyObjectByType<GameBootstrap>() != null) return;
            var go = new GameObject("GameBootstrap");
            go.AddComponent<GameBootstrap>();
        }

        Transform _root;
        Camera _cam;
        GameObject _camGo;
        Light _sun;         // the one directional light; SkyDome aims it per venue

        void Awake()
        {
            ConfigurePhysics();
            _root = new GameObject("Trickshot").transform;

            // Lights
            // Ambient comes from the sky now (SkyDome sets AmbientMode.Skybox), so the three
            // hand-picked Trilight colours that used to live here are gone.
            _sun = MakeSun(_root);

            // Camera
            _camGo = new GameObject("MainCamera");
            _camGo.tag = "MainCamera";
            _cam = _camGo.AddComponent<Camera>();
            _cam.backgroundColor = new Color(0.5f, 0.62f, 0.78f);
            _cam.nearClipPlane = 0.05f;
            // 600, up from 400, for the same reason as the menu camera: every walled venue now gets
            // SurroundBuilder.Skyline, whose furthest buildings sit ~341 m from the pitch centre, and a
            // match camera can be most of a pitch length away from that centre - so 400 was close
            // enough to the far buildings to clip them in and out as play moved up and down the pitch.
            // Near clip is left at 0.05 here: unlike the menu reel, a match camera really can end up
            // close to a body.
            // 900, matching the menu. The far mountain range this was widened for is gone; the hills
            // that remain reach ~776 m from the pitch centre and a match camera can sit most of a pitch
            // length away from that again.
            _cam.farClipPlane = 900f;
            // Real sky, venue sun, matching haze. Re-applied whenever a mode picks a venue.
            SkyDome.Apply(_cam, _sun);
            _camGo.AddComponent<AudioListener>();

            // A client losing the host (host quit / timed out) can happen on ANY screen, including
            // mid-match where nothing used to notice - clients were left in a dead match with frozen
            // puppets. Unwind to the main menu from one place.
            Trickshot.Net.Multiplayer.HostConnectionLost -= OnHostConnectionLost;
            Trickshot.Net.Multiplayer.HostConnectionLost += OnHostConnectionLost;

            // Black screen + studio mark first; the main menu (backdrop stadium, crowd, music) is
            // built under it, so its cost is never a visible stutter. See StudioSplash.
            var splashGo = new GameObject("StudioSplash");
            splashGo.transform.SetParent(_root, false);
            splashGo.AddComponent<StudioSplash>().Init(() => ShowMainMenu());
        }

        void OnDestroy()
        {
            Trickshot.Net.Multiplayer.HostConnectionLost -= OnHostConnectionLost;
        }

        // Stats and achievements save on a worker thread (AtomicFileWriter); a save fired on the
        // last frame must land before the process goes.
        void OnApplicationQuit() => AtomicFileWriter.FlushAll();

        // The host is gone: drop whatever networked screen/match we were in and go back to the menu.
        // Multiplayer.End() has already run, so this is pure local cleanup.
        void OnHostConnectionLost()
        {
            Debug.Log("Lost connection to the host; returning to the main menu.");
            TearDownMatch();
            DestroyNetworkedUI();
            ShowMainMenu();
        }

        // Remove any pregame networked UI (lobby / browser / host setup / lobby-customize) so the
        // main menu isn't drawn underneath a stale panel after a disconnect.
        void DestroyNetworkedUI()
        {
            // No sort mode: the FindObjectsSortMode overloads are deprecated, and order is
            // irrelevant here (we destroy every match).
            foreach (var ui in FindObjectsByType<LobbyUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<SessionBrowserUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<HostSetupUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<MultiplayerHubUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<HostOrFindUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<CustomizeUI>()) Destroy(ui.gameObject);
            foreach (var ui in FindObjectsByType<SpeciesSelectUI>()) Destroy(ui.gameObject);
        }

        // ---- Screen flow: main menu -> pre-match settings -> match (+ pause menu) ----
        GameObject _matchRoot;   // holds everything spawned for a running match
        MenuBackground _menuBg;  // aesthetic bicycle-kick backdrop, alive only on the title screen

        void ShowMainMenu(bool skipSplash = false)
        {
            // Menu music loops unbroken across every pregame screen + the host lobby. Idempotent,
            // so re-entering the menu (or walking hub -> lobby -> customize) never restarts it.
            // Started here + at launch (Awake -> ShowMainMenu); stopped in BuildMode when a match
            // begins; resumed in TearDownMatch on the way back.
            AudioManager.Instance?.PlayMenuMusic();

            // Aesthetic backdrop behind the title screen. Built on its own camera + off-pitch
            // stage, torn down the instant a mode or the multiplayer flow is chosen so it never
            // fights the match camera or the customize preview.
            ShowMenuBackground();

            var menuGo = new GameObject("MenuUI");
            var menu = menuGo.AddComponent<MenuUI>();
            menu.Init(
                onChoose: mode => { HideMenuBackground(); Destroy(menuGo); ShowStadiumSelect(mode); },
                onMultiplayer: () => { HideMenuBackground(); Destroy(menuGo); ShowMultiplayerHub(); },
                input: GetInput(),
                skipSplash: skipSplash);
        }

        void ShowMenuBackground()
        {
            if (_menuBg != null) return;
            var bgGo = new GameObject("MenuBackground");
            _menuBg = bgGo.AddComponent<MenuBackground>();
            // The backdrop is purely cosmetic: NEVER let a failure in it block the menu. If Setup
            // throws (e.g. a stripped shader in a player build), tear it down and carry on so the
            // main menu still shows instead of the whole game blanking to the camera clear colour.
            try { _menuBg.Setup(); }
            catch (System.Exception e)
            {
                Debug.LogError("MenuBackground.Setup failed; continuing without the backdrop. " + e);
                Destroy(bgGo);
                _menuBg = null;
            }
        }

        void HideMenuBackground()
        {
            if (_menuBg == null) return;
            _menuBg.Teardown();
            _menuBg = null;
        }

        // ---- Multiplayer flow: hub (one button per networkable mode) -> that mode's Host/Find
        //      screen -> host setup / browser -> lobby -> networked match. Picking the mode on the
        //      hub IS the mode choice: every screen after it is locked to it (Host Setup draws no
        //      mode picker, the browser lists only that mode's lobbies). The old "Other Modes"
        //      catch-all with a picker inside Host Setup is gone. ----
        void ShowMultiplayerHub()
        {
            var go = new GameObject("MultiplayerHubUI");
            go.AddComponent<MultiplayerHubUI>().Init(
                onMode: m  => { Destroy(go); ShowHostOrFind(m); },
                onBack: () => { Destroy(go); ShowMainMenu(skipSplash: true); });
        }

        // One mode's Host/Find split, titled with the mode. Back from either destination returns
        // here, not to the hub, so "host, change your mind, find instead" is one step.
        void ShowHostOrFind(GameMode mode)
        {
            var go = new GameObject("HostOrFindUI");
            go.AddComponent<HostOrFindUI>().Init(
                onHost: () => { Destroy(go); ShowHostSetup(mode); },
                onJoin: () => { Destroy(go); ShowSessionBrowser(mode); },
                onBack: () => { Destroy(go); ShowMultiplayerHub(); },
                title:  PauseMenu.ModeName(mode).ToUpper());
        }

        void ShowHostSetup(GameMode mode)
        {
            var go = new GameObject("HostSetupUI");
            go.AddComponent<HostSetupUI>().Init(
                onCreated: () => { Destroy(go); ShowHostStadium(mode); },
                onBack:    () => { Destroy(go); ShowHostOrFind(mode); },
                mode:      mode);
        }

        // Host, after Create: pick the stadium on the same screen single player uses, and - for a
        // striker match - size the goal and set the AI keeper on the window beside it. Both are
        // written into the session config on the way to the lobby, so every joiner inherits them.
        // Back ends the just-created session and returns to the host setup. `mode` is the one
        // Host Setup was locked to, which is exactly what it wrote into the session config.
        void ShowHostStadium(GameMode mode)
        {
            var s = Trickshot.Net.Multiplayer.Session;
            if (s == null) { ShowHostSetup(mode); return; }
            // Match sizes its goal here too, on the same widget Striker uses. It does NOT take the
            // keeper level from this screen the way Striker does: a Match keeper is a roster slot
            // picked in the lobby (human or per-slot AI), so writing keeperAbility here would
            // silently overwrite that choice with whatever the goal panel's ladder happened to show.
            bool goalPanel = mode == GameMode.Striker || mode == GameMode.Match;
            bool takesKeeper = mode == GameMode.Striker;
            var cfg0 = s.Config;
            float sw = cfg0.goalScale <= 0.01f ? 1f : cfg0.goalScale;
            float sh = cfg0.goalScaleH <= 0.01f ? sw : cfg0.goalScaleH;

            var go = new GameObject("StadiumSelectUI");
            var ss = go.AddComponent<StadiumSelectUI>();
            ss.Init(
                onPicked: () =>
                {
                    var cfg = s.Config;
                    cfg.stadium = (byte)StadiumStyle.SelectedIndex;
                    if (goalPanel)
                    {
                        cfg.goalScale  = ss.GoalW / SimConfig.GoalWidthBase;
                        cfg.goalScaleH = ss.GoalH / SimConfig.GoalHeightBase;
                        if (takesKeeper)
                            cfg.keeperAbility = SimConfig.AiLevelAbility[Mathf.Clamp(ss.KeeperLevel, 0, SimConfig.AiLevelAbility.Length - 1)];
                    }
                    s.SetConfig(cfg);
                    Destroy(go); ShowLobby();
                },
                onBack: () => { Destroy(go); Trickshot.Net.Multiplayer.End(); ShowHostSetup(mode); },
                goalPanel: goalPanel,
                goalW: SimConfig.GoalWidthBase * sw, goalH: SimConfig.GoalHeightBase * sh,
                keeperLevel: SimConfig.NearestAiLevel(cfg0.keeperAbility));
        }

        void ShowSessionBrowser(GameMode mode)
        {
            var go = new GameObject("SessionBrowserUI");
            go.AddComponent<SessionBrowserUI>().Init(
                onJoined: () => { Destroy(go); ShowLobby(); },
                onBack:   () => { Destroy(go); ShowHostOrFind(mode); },
                mode:     mode);
        }

        void ShowLobby()
        {
            var go = new GameObject("LobbyUI");
            go.AddComponent<LobbyUI>().Init(
                onCustomize: () => { Destroy(go); ShowLobbyCustomize(); },
                onStart:     () => { Destroy(go); StartNetworkedMatch(); },
                onLeave:     () => { Destroy(go); Trickshot.Net.Multiplayer.End(); ShowMultiplayerHub(); });
        }

        // Customize your own player from the lobby, then return to the lobby. Re-sync the local
        // appearance to the session on the way out: the initial Hello / host self-set captured it
        // BEFORE this screen, so without this push remote players (and the roster) keep the
        // default look and cosmetics never show in the match.
        // Does the lobby's chosen mode offer the species screen (see PicksSpecies)? With no session
        // it does, so a torn-down lobby cannot skip a step by accident.
        bool LobbyPicksSpecies()
        {
            var s = Trickshot.Net.Multiplayer.Session;
            return s == null || PicksSpecies((GameMode)s.Config.mode);
        }

        void ShowLobbyCustomize()
        {
            // Humans only in Match mode, exactly as in single player: skip to the body screen and pin
            // the species, then resync so the roster shows Human rather than whatever the player was
            // last set to in another mode.
            if (!LobbyPicksSpecies())
            {
                Species.ApplySelection(Species.HumanId);
                Trickshot.Net.Multiplayer.Session?.UpdateLocalAppearance();
                LobbyCustomizeBody();
                return;
            }

            // Species first, same as the single-player path, so CustomizeUI can read the species once
            // on Init and build its tab set from it. Picking a species writes the appearance's species
            // byte immediately, so backing straight out to the lobby has to resync too or the roster
            // keeps the stale species.
            var sp = new GameObject("SpeciesSelectUI");
            System.Action resync = () => Trickshot.Net.Multiplayer.Session?.UpdateLocalAppearance();
            sp.AddComponent<SpeciesSelectUI>().Init(
                onPicked: () => { Destroy(sp); LobbyCustomizeBody(); },
                onBack:   () => { resync(); Destroy(sp); ShowLobby(); });
        }

        void LobbyCustomizeBody()
        {
            var go = new GameObject("CustomizeUI");
            System.Action resync = () => Trickshot.Net.Multiplayer.Session?.UpdateLocalAppearance();
            go.AddComponent<CustomizeUI>().Init(
                onDone: () => { resync(); Destroy(go); ShowLobby(); },
                // Straight back to the lobby when the species screen was skipped: routing through
                // ShowLobbyCustomize would skip forward to this same screen again and Back would
                // look broken.
                onBack: () => { resync(); Destroy(go);
                                if (LobbyPicksSpecies()) ShowLobbyCustomize(); else ShowLobby(); });
        }

        // Apply the host's synced config, then build the chosen mode with the session live.
        void StartNetworkedMatch()
        {
            var s = Trickshot.Net.Multiplayer.Session;
            var cfg = s.Config;
            StadiumStyle.SelectedIndex = cfg.stadium;
            var mode = (GameMode)cfg.mode;
            // Player pace in a networked match is the player's OWN - body build x Pace stat over the
            // base speed - with no host knob (HostSetupUI keeps player speed fixed for balance). This
            // static is only ever written by the single-player pre-match screens, so without this
            // reset a networked match ran at whatever multiplier the last single-player Accuracy or
            // Free Kick setup had left in it, on every peer independently.
            SimConfig.StrikerMoveSpeed = SimConfig.StrikerMoveSpeedBase;
            if (mode == GameMode.Match)
            {
                // cfg.perSide is an untrusted wire byte and this is a mutable static every later
                // consumer reads, so clamp at the boundary. The floor of 2 is the shirt invariant:
                // shirts are 0 = keeper and 1..perSide-1 = outfield, so a side of 1 has no legal
                // outfield shirt at all.
                SimConfig.MatchPerSide = Mathf.Clamp(cfg.perSide, 2, Trickshot.Net.NetSession.ScrimSlotsPerTeam);
                SimConfig.MatchSeconds = cfg.matchSec;
                // Keeper-ness comes from the SHIRT, not from NetRole. Match puts two teams on
                // the eight slots, so the away keeper is slot 4, and RoleOfSlot - which describes
                // the single-goal layout - calls slot 4 a shooter. Reading LocalRole therefore
                // brought an away keeper into the match flagged as an outfielder. Harmless today
                // (the net path returns before PlayerRole is used for anything but a nominal
                // argument) and a trap the moment it is not.
                SimConfig.PlayerRole =
                    Trickshot.Net.NetSession.ScrimShirtOfSlot(s.LocalSlot) == 0
                    ? SimConfig.MatchRole.Keeper : SimConfig.MatchRole.Outfield;
                // Canonical goal size, written on EVERY peer from the HOST'S config - the host
                // now sizes a Match goal on the stadium screen, exactly as Striker does. This used
                // to hardcode 7.32/2.44, which was itself a fix for leaving the mutable statics
                // alone (a host who last played a 1.5x set piece and a client who did not built the
                // goal frame and the goal-detection plane at DIFFERENT sizes in the same match - a
                // real desync that resolves to "the host and the client disagree about whether that
                // was a goal"). Reading the config keeps that property: it is one number, from one
                // peer, applied identically everywhere. An unauthored config (0) still falls back to
                // regulation inside ApplyConfigGoal.
                //
                // KeeperAbility is deliberately left alone by the Match path - a Match keeper is a
                // roster slot (human, or per-slot AI chosen in the lobby), not this screen's ladder.
                float keepAbility = SimConfig.KeeperAbility;
                ApplyConfigGoal(cfg);
                SimConfig.KeeperAbility = keepAbility;
                SimConfig.BallSpeedMul = 1f;
            }
            else if (mode == GameMode.SetPieces)
            {
                // Host-chosen goal size + AI keeper strength apply to everyone (mutable statics).
                ApplyConfigGoal(cfg);
            }
            else if (mode == GameMode.Accuracy)
            {
                // Goal size, and whether there is a keeper at all. Accuracy has no wall and no
                // target count any more, and its keeper ABILITY is written per round by the driver
                // rather than read from the config - so the config only says none-or-ramping, as a
                // negative keeperAbility (see HostSetupUI).
                ApplyConfigGoal(cfg);
                SimConfig.WallCount = 0;
                SimConfig.AccuracyNoKeeper = cfg.keeperAbility < 0f;
            }
            else if (mode == GameMode.Striker)
            {
                // Goal size + AI keeper from the host's stadium/goal screen. This branch did not exist:
                // a networked striker match played on whatever goal size and keeper each peer's OWN
                // statics happened to hold from its last single-player pre-match, so the host and a
                // client could disagree about the goal - the same desync the Match branch fixed.
                ApplyConfigGoal(cfg);
            }
            else if (mode == GameMode.TrickshotCup)
            {
                // Style / format / seed for EVERY peer from the host's config (CupLaunch is what
                // BuildMode reads). Never ApplyConfigGoal here: a cup goal is regulation on every
                // peer by construction (CupDirector.ApplyCupStatics, called above the arena build),
                // and its keeper is the stage ramp's, not the config's.
                CupLaunch.FromConfig(cfg);
            }
            BuildMode(mode);
        }

        // Goal width/height + AI keeper from a match config, on every peer. goalScaleH is the newer
        // field (a config without it - 0 - keeps the goal in proportion, as it always was).
        static void ApplyConfigGoal(in Trickshot.Net.MatchConfig cfg)
        {
            float sw = cfg.goalScale <= 0.01f ? 1f : cfg.goalScale;
            float sh = cfg.goalScaleH <= 0.01f ? sw : cfg.goalScaleH;
            SimConfig.GoalWidth  = SimConfig.GoalWidthBase  * sw;
            SimConfig.GoalHeight = SimConfig.GoalHeightBase * sh;
            SimConfig.KeeperAbility = Mathf.Clamp01(cfg.keeperAbility);
        }

        // Mode -> pick stadium -> (customize your player, striker modes) -> pre-match -> play.
        void ShowStadiumSelect(GameMode mode)
        {
            var go = new GameObject("StadiumSelectUI");
            var ss = go.AddComponent<StadiumSelectUI>();
            ss.Init(
                onPicked: () => { Destroy(go); AfterStadium(mode); },
                // Back lands on the menu Hub (the SP/MP/Career Stats page), not the splash:
                // skipSplash rebuilds MenuUI directly in its Hub phase, exactly like the
                // multiplayer hub's own Back does.
                onBack:   () => { Destroy(go); ShowMainMenu(skipSplash: true); });
        }

        // Every human-player mode gets the customize screen, keeper included (the keeper wears
        // the customized skin/cosmetics/jersey + gloves, same as multiplayer). Modes with no
        // customizable player would return false here. The cup included: the player is a body
        // in every round (the Jersey stage still runs; the cup paints the nation kit over it).
        static bool UsesCustomPlayer(GameMode mode) => true;
        // The keeper's customize flow skips the Skill stage (it only drives shot/movement traits
        // a KeeperController never reads); every other mode walks the full flow. The cup walks
        // it too even though it standardises shooting (SkillTree.MaxShootingOverride): the tree
        // still owns the movement traits, and the build is the player's outside the cup.
        static bool CustomizeSkipsSkill(GameMode mode) => mode == GameMode.Goalkeeper;

        // Match mode is HUMANS ONLY for now, so it skips the species screen and pins the selection
        // back to Human on the way past. A quadruped in a team match leans on team AI, keeper poses
        // and a ball-strike model that all assume a biped, and none of that has been done for one;
        // offering the pick and then playing a horse badly is worse than not offering it. Everything
        // else about the species is untouched, so restoring the pick is this one predicate.
        static bool PicksSpecies(GameMode mode) => mode != GameMode.Match;

        void AfterStadium(GameMode mode)
        {
            if (!UsesCustomPlayer(mode)) { ShowPrematch(mode); return; }
            if (!PicksSpecies(mode))
            {
                // Pin it here rather than trusting whatever the last mode left selected: the species
                // byte persists in the profile, so picking a horse in Striker and then starting a
                // Match mode would otherwise carry the horse in with no screen to change it back.
                Species.ApplySelection(Species.HumanId);
                ShowCustomize(mode);
                return;
            }
            ShowSpeciesSelect(mode);
        }

        // Species is the first step of the customize flow: it decides which appearance tabs exist,
        // what the body sliders measure, whether adult mode is offered and whether there is an
        // Instinct skill tab, so it has to be settled before CustomizeUI initializes.
        void ShowSpeciesSelect(GameMode mode)
        {
            var go = new GameObject("SpeciesSelectUI");
            go.AddComponent<SpeciesSelectUI>().Init(
                onPicked: () => { Destroy(go); ShowCustomize(mode); },
                onBack:   () => { Destroy(go); ShowStadiumSelect(mode); });
        }

        void ShowCustomize(GameMode mode)
        {
            var go = new GameObject("CustomizeUI");
            var cu = go.AddComponent<CustomizeUI>();
            cu.SkipSkill = CustomizeSkipsSkill(mode);
            cu.Init(
                onDone: () => { Destroy(go); ShowPrematch(mode); },
                // Back goes to whichever screen actually preceded this one. Sending Match mode to the
                // species screen it never saw would both show a screen that is meant to be gone and
                // strand Back in a loop between the two.
                onBack: () => { Destroy(go);
                                if (PicksSpecies(mode)) ShowSpeciesSelect(mode); else ShowStadiumSelect(mode); });
        }

        // Accuracy forks before the pre-match screen: PRACTICE goes on to it, CHALLENGE has nothing
        // to configure and starts the run directly. The cup has its own fork (Penalties / Free
        // Kicks) and no pre-match panel at all. Every other mode goes straight to pre-match.
        void ShowPrematch(GameMode mode)
        {
            if (mode == GameMode.Accuracy) { ShowAccuracyModePick(); return; }
            if (mode == GameMode.TrickshotCup) { ShowCupSetup(); return; }
            ShowPrematchPanel(mode);
        }

        // The Solo cup's fork (design 3.3): PENALTIES or FREE KICKS, nothing else to set - the
        // goal is regulation, the stage ramp owns the keeper, the field is always 32. Picking a
        // card IS the start of the cup: park the format and a fresh seed (CupLaunch) and build.
        // Back returns to Customize, the screen before it.
        void ShowCupSetup()
        {
            var go = new GameObject("CupSetupUI");
            go.AddComponent<CupSetupUI>().Init(
                onPick: f => { Destroy(go); CupLaunch.Solo(f); BuildMode(GameMode.TrickshotCup); },
                onBack: () => { Destroy(go);
                                if (UsesCustomPlayer(GameMode.TrickshotCup)) ShowCustomize(GameMode.TrickshotCup);
                                else ShowStadiumSelect(GameMode.TrickshotCup); });
        }

        void ShowAccuracyModePick()
        {
            var go = new GameObject("AccuracyModeUI");
            var am = go.AddComponent<AccuracyModeUI>();
            am.Init(
                // Both go to the pre-match panel now: it branches on SimConfig.AccuracyPractice
                // and gives the challenge a cut-down screen (a LOCKED regulation goal, since a
                // scored run has to be played on one goal, and a keeper yes/no - the only thing
                // about a challenge that is the player's to choose).
                onPractice:  () => { Destroy(go); ShowPrematchPanel(GameMode.Accuracy); },
                onChallenge: () => { Destroy(go); ShowPrematchPanel(GameMode.Accuracy); },
                onBack:      () => { Destroy(go);
                                     if (UsesCustomPlayer(GameMode.Accuracy)) ShowCustomize(GameMode.Accuracy);
                                     else ShowStadiumSelect(GameMode.Accuracy); });
        }

        void ShowPrematchPanel(GameMode mode)
        {
            var go = new GameObject("PrematchUI");
            var pm = go.AddComponent<PrematchUI>();
            pm.Init(mode,
                onStart: m => { Destroy(go); BuildMode(m); },
                // Back goes to the previous screen: the Practice/Challenge fork for Accuracy (which
                // is what opened this panel), else Customize for any mode with a customizable player
                // (all of them now, keeper included), else the stadium picker. Branch here rather
                // than via AfterStadium, which is a forward-router to Prematch.
                onBack:  () => { Destroy(go);
                                 if (mode == GameMode.Accuracy) ShowAccuracyModePick();
                                 else if (UsesCustomPlayer(mode)) ShowCustomize(mode);
                                 else ShowStadiumSelect(mode); });
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
            // The cup's pause overlay and its parked launch values never outlive the match: the
            // next mode's pause menu must freeze again, and a later BuildMode must not be able to
            // launch yesterday's cup.
            PauseMenu.Overlay = false;
            CupLaunch.Clear();

            // Back to menus: stop the crowd bed and resume menu music. Covers BOTH exits (quit to
            // main menu AND pause -> match setup, which reopens Prematch without ShowMainMenu).
            AudioManager.Instance?.EndMatch();
            AudioManager.Instance?.PlayMenuMusic();
        }

        void ReturnToMainMenu()
        {
            TearDownMatch();
            Trickshot.Net.Multiplayer.End();   // end any networked session on quit-to-menu
            ShowMainMenu();
        }

        // Client-only: leave a networked match without ending it for anyone else. For a CLIENT,
        // Multiplayer.End() just shuts this peer's transport; the host detects the drop
        // (NetSession.OnPeerLeft), frees the slot, and the match driver reverts that player to AI
        // so the host + remaining players keep going. (Host has no leave-without-ending: it owns
        // the authoritative sim and there's no host migration, so the host uses Main Menu instead.)
        void LeaveNetworkedMatch()
        {
            TearDownMatch();
            Trickshot.Net.Multiplayer.End();
            ShowMainMenu();
        }

        // Pause -> Restart Match: rebuild the same mode with the same settings, skipping the
        // pre-match screen. Deferred to the next frame because the request comes from the pause
        // menu's OnGUI and the outgoing match root (which owns that menu) is only destroyed at the
        // end of this frame; building the replacement here would run both for a frame.
        GameMode _restartMode;
        bool _restartPending;

        void RestartMatch(GameMode mode)
        {
            _restartMode = mode;
            _restartPending = true;
        }

        void Update()
        {
            if (!_restartPending) return;
            _restartPending = false;
            TearDownMatch();
            BuildMode(_restartMode);
            GameInput.CaptureCursor(true);   // straight back into play, not into a menu
        }

        // Pause -> Match Setup: tear the match down and reopen the pre-match config for the
        // same mode. Start rebuilds the match; Back walks to the previous pregame screen.
        void ReturnToMatchSetup(GameMode mode)
        {
            TearDownMatch();
            // End any networked session too. TearDownMatch destroys the match root - and with it the
            // NetPump - so a surviving session would sit here unpolled: its socket stays bound (the
            // next Host() then fails to bind 7777 and silently falls back to single-player), every
            // client times out, and the rx inbox grows unboundedly. The pre-match screen is a
            // single-player flow, so there is nothing left for the session to do.
            Trickshot.Net.Multiplayer.End();
            ShowPrematch(mode);
        }

        void BuildMode(GameMode mode)
        {
            // Audio hand-off at the menu -> match boundary: stop menu music and start the crowd
            // bed for this mode (also arms the lively swell timer / streak system). Done first so
            // it covers every mode, including the early-return Match-mode branch below.
            AudioManager.Instance?.BeginMatch(mode);

            // Everything for this match lives under _matchRoot so it can be torn down.
            _matchRoot = new GameObject("Match");
            _matchRoot.transform.SetParent(_root, false);
            var root = _matchRoot.transform;
            var cam = _cam;
            var camGo = _camGo;

            // Pause menu (Esc): Resume / Match Setup / Settings / Leave. The leave callback is a
            // CLIENT clean-leave (drops only this player, host + others play on); it's null in
            // single-player and for the host, where "Main Menu" ends the session for everyone.
            var pauseGo = new GameObject("PauseMenu");
            pauseGo.transform.SetParent(root, false);
            bool cup = mode == GameMode.TrickshotCup;
            bool net = Trickshot.Net.Multiplayer.IsActive;
            // The cup's quits go THROUGH the director (design 6.10 / 9.5): it marks the cup Ended
            // - the host broadcasts that so clients see the menu rather than "connection lost" -
            // and then calls the same ReturnToMainMenu / LeaveNetworkedMatch these closures
            // would. Looked up at click time: the director does not exist yet here, and a cup
            // that somehow has none falls back to the plain path.
            System.Action cupQuit = () =>
            {
                var d = CupDirector.Instance;
                if (d != null) d.QuitToMenu(); else ReturnToMainMenu();
            };
            System.Action cupLeave = () =>
            {
                var d = CupDirector.Instance;
                if (d != null) d.QuitToMenu(); else LeaveNetworkedMatch();
            };
            System.Action onLeave = Trickshot.Net.Multiplayer.IsClient ? (cup ? cupLeave : LeaveNetworkedMatch) : (System.Action)null;
            // Restart is single-player only: the net protocol has no match reset, so restarting
            // mid-session would strand every client. Never for the cup, in ANY style: a cup is
            // a bracket, and "restart" there is Play Again / New Cup on its own end cards.
            System.Action onRestart = net || cup ? (System.Action)null : () => RestartMatch(mode);
            // The full-screen setup (tear down + reopen the pre-match screen) is SINGLE-PLAYER only:
            // networked, ReturnToMatchSetup ends the session for everyone, and the host now has the
            // in-pause Match Setup (goal size + keeper, applied live) for what it can change. The
            // cup has no settings to reopen (PauseMatchSetup.RowsFor is 0 for it), so never there.
            System.Action onFullSetup = net || cup ? (System.Action)null : () => ReturnToMatchSetup(mode);
            var pause = pauseGo.AddComponent<PauseMenu>();
            pause.Init(cup ? cupQuit : ReturnToMainMenu, onFullSetup, GetInput(), onLeave, onRestart, mode);
            if (cup)
            {
                // The one quit entry, worded per style (design 6.10), and the overlay pause for the
                // multiplayer styles: the sim, the kick clock and the camera keep running under the
                // menu there; only Solo freezes. Cleared again in TearDownMatch.
                if (!net)
                    pause.SetCupLabels(CupText.QuitToMenu, CupText.ConfirmQuitTitle, CupText.ConfirmQuitSoloBody);
                else if (Trickshot.Net.Multiplayer.IsHost)
                    pause.SetCupLabels(CupText.EndMatch, CupText.ConfirmEndMatchTitle, CupText.ConfirmEndMatchBody);
                else
                    pause.SetCupLabels(CupText.QuitToMenu, CupText.ConfirmQuitTitle,
                                       CupLaunch.Style == CupStyle.Coop ? CupText.ConfirmQuitCoopBody
                                                                        : CupText.ConfirmQuitHeadToHeadBody);
                PauseMenu.Overlay = net;
            }

            // Networked match: pump the transport every frame for the match's lifetime.
            if (Trickshot.Net.Multiplayer.IsActive)
                pauseGo.AddComponent<Trickshot.Net.NetPump>();

            SkyDome.Apply(_cam, _sun);

            // Default the aim-target to the training goal; Match mode repoints it.
            SimConfig.AttackGoalCenter = SimConfig.GoalCenter;

            // Match mode builds its OWN two-goal, fully-walled pitch (centred at origin) then
            // wraps it with the shared stadium + crowd, sized to that pitch.
            if (mode == GameMode.Match) { BuildMatchMode(root, camGo); return; }

            // Single-goal venues use the regulation training pitch footprint. Reset in case a
            // prior Match-mode run repointed PitchLayout at its own field.
            PitchLayout.ResetToTraining();

            // The accuracy CHALLENGE never opens the pre-match screen, so nothing has run
            // PrematchUI.Apply for it - and the statics still hold whatever the last mode (very
            // likely an accuracy PRACTICE session, which sizes the goal by hand) left in them. A
            // scored run has to be played on the regulation goal at standard pace whatever came
            // before it. This has to happen HERE, above Arena.Build: that is what builds the goal
            // frame, net and backstops at the current size. Practice keeps what its setup wrote.
            if (mode == GameMode.Accuracy && !SimConfig.AccuracyPractice)
            {
                SimConfig.GoalWidth  = SimConfig.GoalWidthBase;
                SimConfig.GoalHeight = SimConfig.GoalHeightBase;
                SimConfig.BallSpeedMul = 1f;
                SimConfig.StrikerMoveSpeed = SimConfig.StrikerMoveSpeedBase;
                SimConfig.WallCount = 0;
            }
            // The cup, for the same reason and in the same place: regulation goal, BallSpeedMul 1,
            // StrikerMoveSpeed base, the 4-man wall at 9.15 m, PenaltyMode per format, no accuracy
            // flags, no placed spot, and the two standardised-shooting overrides - one idempotent
            // snapshot (design 9.6) the director puts back in its OnDestroy. Launch calls it again
            // and only rebuilds the goal if something still differs, which after this is nothing.
            if (mode == GameMode.TrickshotCup)
                CupDirector.ApplyCupStatics(CupLaunch.Format, CupStage.RoundOf32);

            // --- Shared: arena, full pitch, stadium, crowd, ball, camera controller ---
            // All single-goal modes play on the OPEN full pitch: no boundary walls. The old
            // training-field walls sat at x=+/-12 / z=-17 - mid-pitch on the regulation field,
            // between the 6- and 18-yard box edges - and bounced set-piece shots at all heights.
            // Every mode resolves a dead/out-of-play ball in code (rest timer or FieldWidth/Length
            // bounds), so no physical boundary is needed. Match mode builds its own walled arena.
            var arena = Arena.Build(root, boundaryWalls: false);
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
            Arena.BindBall(ball.transform, SimConfig.BallRadius);   // also re-bound on a goal rebuild

            var gameCam = camGo.AddComponent<GameCamera>();

            // The Trickshot Cup, every style, before the networked dispatches: one director for
            // the whole cup (design 9.5) and NO ball-mode driver - the director runs its own
            // session plumbing and builds each round under its own RoundRoot.
            if (mode == GameMode.TrickshotCup)
            {
                BuildCup(root, cam, gameCam, ball, arena);
                return;
            }

            // Networked striker: host-authoritative multi-player striker driver instead of
            // the single-player GameManager. (Match-mode networking is a later pass.)
            if (Trickshot.Net.Multiplayer.IsActive && mode == GameMode.Striker)
            {
                BuildNetStrikerMode(root, cam, gameCam, ball, arena);
                return;
            }
            // Networked set-pieces shootout: host-authoritative driver (keeper + rotating
            // shooters). Single-player SetPieces falls through to the free-kick build below.
            if (Trickshot.Net.Multiplayer.IsActive && mode == GameMode.SetPieces)
            {
                BuildNetSetPieces(root, cam, gameCam, ball, arena);
                return;
            }
            // Networked accuracy: same free-kick-at-targets gameplay, but shooters take turns and
            // compete on target points. Single-player Accuracy falls through to its own build below.
            if (Trickshot.Net.Multiplayer.IsActive && mode == GameMode.Accuracy)
            {
                BuildNetAccuracy(root, cam, gameCam, ball, arena);
                return;
            }

            switch (mode)
            {
                case GameMode.Goalkeeper: BuildKeeperMode(root, cam, gameCam, ball, arena); break;
                // Accuracy is now a free-kick shooting gallery (dead ball + SetPieceTaker at
                // pop-up targets), so it builds like a set piece, not like the crosser-served
                // challenge modes.
                case GameMode.Accuracy:   BuildAccuracyMode(root, cam, gameCam, ball, arena); break;
                case GameMode.FreeKick:
                case GameMode.SetPieces:  BuildFreeKickMode(root, cam, gameCam, ball, arena); break;
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
              .Configure(GetInput(), cam, gameCam, ball, crosser, reticle, launch, arena.goalCenter,
                         torso, limb, glove, root);
            LockCursor();
            ball.ResetTo(launch.position);
        }

        // Networked set-pieces shootout: keeper (slot 0, human or AI) + rotating shooters that
        // each take 10 free kicks. The NetSetPieceMatch driver spawns the bodies, wall, and ball
        // reset per attempt. Reuses the shared training arena/goal/ball built by BuildMode.
        void BuildNetSetPieces(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            ball.SetCamera(gameCam);
            Material torso = JerseyMaterial();
            Material limb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material glove = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            var go = new GameObject("NetSetPieceMatch");
            go.transform.SetParent(root, true);
            go.AddComponent<NetSetPieceMatch>()
              .Configure(GetInput(), cam, gameCam, ball, arena.goalCenter, torso, limb, glove, root);
            LockCursor();
        }

        // Networked accuracy: the same dead-ball rig as set pieces (keeper slot 0 human/AI/none +
        // rotating shooters), but the shooters are scored on the pop-up TARGETS they hit rather
        // than goals, and each turn ends on a host-chosen kick count or timer.
        void BuildNetAccuracy(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            ball.SetCamera(gameCam);
            Material torso = JerseyMaterial();
            Material limb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material glove = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            var go = new GameObject("NetAccuracyMatch");
            go.transform.SetParent(root, true);
            go.AddComponent<NetAccuracyMatch>()
              .Configure(GetInput(), cam, gameCam, ball, arena.goalCenter, torso, limb, glove, root);
            LockCursor();
        }

        // ------------------------------------------------ Trickshot Cup (all three styles)
        // The arena, pitch, stadium, crowd, ball and camera are already standing (BuildMode) and
        // persist for the WHOLE cup; the director builds and tears down each round's bodies,
        // referee, wall and coin under its own RoundRoot, so the stadium never flickers between
        // rounds (design 9.5). Style / format / seed come from CupLaunch: the Solo fork parked
        // them, or StartNetworkedMatch copied them out of the host's config on every peer. The
        // director opens on CHOOSE YOUR NATION, a menu, so the cursor is left free; its screens
        // own it from here (every round captures it on its own).
        void BuildCup(Transform root, Camera cam, GameCamera gameCam, BallController ball, Arena.Refs arena)
        {
            ball.SetCamera(gameCam);
            // Init the camera controller like every other builder does - without it GameCamera
            // has no Camera and its LateUpdate returns at once, so the free-kick Follow, the
            // keeper's KeeperFollow and the replay's Broadcast would never move the camera.
            // No striker here: the round driver re-targets it per kick through the rig.
            gameCam.Init(cam, ball.transform, null, null, arena.goalCenter);
            Material torso = JerseyMaterial();   // the cup paints the nation kit over it per body
            Material limb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material glove = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            // No session = Solo, whatever was parked (a stale MP style must not run a networked
            // phase machine against nobody); a session with a Solo style (never hosted, but an
            // unauthored config is 0) is Head to Head, as NetSession labels it.
            bool netActive = Trickshot.Net.Multiplayer.IsActive;
            var style = !netActive ? CupStyle.Solo
                      : CupLaunch.Style == CupStyle.Solo ? CupStyle.HeadToHead
                      : CupLaunch.Style;
            var director = CupDirector.Launch(root, style, CupLaunch.Format, CupLaunch.SeedForLaunch(),
                                              GetInput(), cam, gameCam, ball, arena.goalCenter,
                                              torso, limb, glove, ReturnToMainMenu);
            if (Trickshot.Net.Multiplayer.IsClient) director.OnLeave = LeaveNetworkedMatch;
            // Solo: Back / Esc on CHOOSE YOUR NATION returns to the fork (design 6.1) - the same
            // tear-down-and-reopen the pause menu's Match Setup uses, which lands on ShowCupSetup.
            if (!netActive) director.OnBackToSetup = () => ReturnToMatchSetup(GameMode.TrickshotCup);
            GameInput.CaptureCursor(false);
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
            // Dribbling is OFF in Striker mode: no capture, no first touch, no carry - the ball
            // is only ever struck or volleyed. The component stays bound (Striker null-checks it
            // and its speed/turn penalties are Carrying-gated) but FixedUpdate returns at once.
            BuildStrikerPlayer(root, ball, out var striker, out var ragdoll, out var dribble);
            dribble.Enabled = false;
            ball.NoCarry = true;   // and a dead touch is pushed clear of his feet, never parked there

            // AI keeper: an active-ragdoll goaltender (with gloves) that shuffles + dives. Shared
            // with every challenge mode via BuildAiKeeper (below) rather than a hand-inlined copy -
            // this used to duplicate that helper verbatim, which meant a future change made to one
            // could silently fail to apply to the other.
            // ALWAYS built here, even at "None": the keeper's difficulty can be changed from the
            // in-match setup, and a keeper that was never built cannot be turned on. At None he is
            // inert on his line (Goalkeeper's ability-0 branch) - the same body a networked striker
            // match always has in the seat.
            Goalkeeper keeper = BuildAiKeeper(root, ball, out _, alwaysBuild: true);

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, crosserRagdoll.Pelvis.transform, arena.goalCenter);
            ball.SetCamera(gameCam);   // auto ball-cam on a shot
            crosser.Init(reticle, ball, launch, crosserRagdoll);

            // The SP crosser is always an AI/planted server: the ball must NEVER touch his body
            // (perfect delivery) and no other player may crowd him. Ignore ball<->crosser collisions
            // and wrap him in a protective bubble that ejects other players but lets the ball pass.
            ball.IgnoreBody(crosserRagdoll, true);
            crosserGo.AddComponent<CrosserBubble>().Init(crosserRagdoll);

            var gmGo = new GameObject("GameManager");
            gmGo.transform.SetParent(root, true);
            var gm = gmGo.AddComponent<GameManager>();
            gm.Configure(GetInput(), crosser, reticle, ball, striker, ragdoll, keeper, gameCam, launch);
            LockCursor();

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
                                withGloves: false, appearance: PlayerProfile.Appearance);
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

        // The jersey vote's winning texture for `team` (0 = Home, 1 = Away), or null if nobody
        // nominated - or the winner's jersey simply hasn't finished its chunked transfer yet -
        // either way the caller keeps its own default rather than a material with no texture.
        static Texture2D JerseyVoteTex(Trickshot.Net.NetSession session, int team)
        {
            int winner = Trickshot.Net.NetSession.JerseyWinnerSlot(session.Roster, team);
            return winner >= 0 ? session.JerseyForSlot(winner) : null;
        }

        // Builds the ragdoll crosser + its launch point + the aim reticle.
        void BuildCrosser(Transform root, BallController ball,
                          out Crosser crosser, out ActiveRagdoll crosserRagdoll,
                          out Transform launch, out AimReticle reticle)
        {
            var crosserGo = new GameObject("Crosser");
            crosserGo.transform.SetParent(root, true);
            // The BODY is a child object, not the Crosser's own: the networked match rebuilds it for
            // whoever holds the seat - a human's own look, or this plain AI server - and destroys
            // the old one (NetStrikerMatch.RebuildCrosserBody), which it could not do to the object
            // the Crosser, its launch point and its bubble live on.
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(crosserGo.transform, false);
            crosserRagdoll = bodyGo.AddComponent<ActiveRagdoll>();
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

        // Builds an AI goalkeeper ragdoll (with gloves). Returns null if ability is ~0, unless
        // `alwaysBuild` (a mode whose keeper difficulty can change mid-match needs the body to exist).
        Goalkeeper BuildAiKeeper(Transform root, BallController ball, out ActiveRagdoll keeperRagdoll, bool alwaysBuild = false)
        {
            keeperRagdoll = null;
            if (!alwaysBuild && SimConfig.KeeperAbility <= 0.001f) return null;
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

        // ------------------------------------------------------- Accuracy (three strikes)
        // Accuracy is a solo strikes run: one dead-ball free kick per round at a single patrolling
        // target, from a random spot in the shot band. No wall, and the keeper is ALWAYS
        // built - his ability is set per round by the driver (starting at 1%), so the ability-0
        // skip the other dead-ball modes use would leave the run permanently keeperless.
        void BuildAccuracyMode(Transform root, Camera cam, GameCamera gameCam,
                               BallController ball, Arena.Refs arena)
        {
            BuildStrikerPlayer(root, ball, out var striker, out var ragdoll, out var dribble);
            dribble.Enabled = false;
            dribble.SetPieceActive = true;   // the parked ball is never magnet-captured to the feet

            // No keeper asked for (either screen's keeper row): build none at all, in both modes.
            // PRACTICE otherwise uses the ordinary guard, since its picker's "None" is ability 0.
            // CHALLENGE otherwise builds unconditionally: its ability is rewritten by
            // AccuracyGame.BeginRound before every shot, and tier 0 sits right on the build guard's
            // threshold, so the guard alone would leave the whole run keeperless.
            Goalkeeper keeper = null; ActiveRagdoll keeperRagdoll = null;
            if (SimConfig.AccuracyNoKeeper)
            {
                SimConfig.KeeperAbility = 0f;
            }
            else if (SimConfig.AccuracyPractice)
            {
                keeper = BuildAiKeeper(root, ball, out keeperRagdoll);
            }
            else
            {
                SimConfig.KeeperAbility = SimConfig.AccuracyKeeperAbility(1);
                keeper = BuildAiKeeper(root, ball, out keeperRagdoll, alwaysBuild: true);
            }

            gameCam.Init(cam, ball.transform, ragdoll.Pelvis.transform, null, arena.goalCenter);
            gameCam.SetFollow(ragdoll.Pelvis.transform, () => GetInput().Look, null, () => GetInput().CamViewPressed);
            ball.SetCamera(gameCam);
            striker.SetCameraYaw(() => gameCam.Yaw, () => gameCam.Pitch);

            var go = new GameObject("AccuracyGame");
            go.transform.SetParent(root, true);
            // The challenge end card offers Match Setup and Main Menu: the SAME closures the pause
            // menu is built with below, so the card is a shortcut to paths that already work.
            // Match Setup reopens the Practice/Challenge fork (ShowPrematch routes Accuracy there),
            // which is exactly what the pause menu's own entry does for this mode.
            go.AddComponent<AccuracyGame>()
              .Configure(GetInput(), ball, striker, ragdoll, keeper, keeperRagdoll, gameCam,
                         onMatchSetup: () => ReturnToMatchSetup(GameMode.Accuracy),
                         onMainMenu:   ReturnToMainMenu);

            LockCursor();
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
            gameCam.SetFollow(ragdoll.Pelvis.transform, () => GetInput().Look, null, () => GetInput().CamViewPressed);
            ball.SetCamera(gameCam);   // auto ball-cam on a shot
            striker.SetCameraYaw(() => gameCam.Yaw, () => gameCam.Pitch);

            var wall = new DefensiveWall();
            var go = new GameObject("FreeKickGame");
            go.transform.SetParent(root, true);
            var fk = go.AddComponent<FreeKickGame>();
            fk.Configure(GetInput(), ball, striker, ragdoll, keeper, keeperRagdoll, wall, gameCam);

            LockCursor();
        }

        // -------------------------------------------------------- Match mode
        void BuildMatchMode(Transform root, GameObject camGo)
        {
            // Networked Match mode is capped to fit the 8-slot model: 4-a-side incl keepers max
            // (slots 0-3 Home, 4-7 Away). Single-player keeps the full 3/5/11 options.
            bool net = Trickshot.Net.Multiplayer.IsActive;
            // The floor of 2 is the D6 shirt invariant, not defensive habit: shirt 0 is the keeper
            // and outfielders are 1..perSide-1, so perSide must be at least 2 for one outfielder to
            // have a legal shirt. Below that the old Max(1, perSide-1) below manufactured a shirt 1
            // in a squad of size 1, which is exactly the out-of-range a formation table indexed by
            // shirt would take. Net also clamps to the eight-slot board; NetSession.ScrimPerSide
            // clamps identically so the seating and the bodies cannot disagree.
            int perSide = net ? Mathf.Clamp(SimConfig.MatchPerSide, 2, Trickshot.Net.NetSession.ScrimSlotsPerTeam)
                              : Mathf.Max(2, SimConfig.MatchPerSide);
            var arena = MatchArena.Build(root, perSide);
            // The human (Home) attacks the +Z goal; aim assist / dribble / ball-cam target it.
            SimConfig.AttackGoalCenter = arena.homeGoalCenter;

            // Wrap the Match-mode pitch with the SAME stadium bowl + crowd the other venues use,
            // sized to this (centred) field. Point the shared PitchLayout contract at it first,
            // then build the shell + crowd (skip PitchBuilder - Match mode lays its own ground).
            // No SkyDome.Apply here: BuildMode's shared preamble already applied it once for
            // whichever style is active, moments before dispatching into this function - a second
            // call rebuilt the same sky material and re-ran DynamicGI.UpdateEnvironment for nothing.
            PitchLayout.ConfigureMatch(arena.halfLength * 2f, arena.halfWidth * 2f, 0f);
            StadiumBuilder.Build(root);
            _crowd = Crowd.Create(root);
            CrowdCheer.Register(_crowd);

            // Ball.
            var ballGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballGo.name = "Ball";
            ballGo.transform.SetParent(root, true);
            ballGo.transform.localScale = Vector3.one * (SimConfig.BallRadius * 2f);
            ballGo.GetComponent<Renderer>().sharedMaterial = Make.Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            ballGo.AddComponent<Rigidbody>();
            var ball = ballGo.AddComponent<BallController>();

            var gameCam = camGo.AddComponent<GameCamera>();

            // Team colours. Multiplayer's jersey vote (LobbyUI) can override either team's shared
            // torso with a WINNING candidate's actual painted kit - see NetSession.JerseyWinnerSlot.
            // Falls back to today's exact defaults (host's own kit for Home, plain red for Away)
            // when nobody nominated, or in single-player, which has no vote at all.
            Material homeTorso = JerseyMaterial();                          // player's painted kit for Home
            Material homeLimb  = Make.Mat(new Color(0.15f, 0.32f, 0.6f));
            Material awayTorso = Make.Mat(new Color(0.75f, 0.2f, 0.2f));
            Material awayLimb  = Make.Mat(new Color(0.5f, 0.13f, 0.13f));
            Material gloveMat  = Make.Mat(new Color(0.9f, 0.85f, 0.2f));

            if (net && Trickshot.Net.Multiplayer.Session != null)
            {
                var mpTex = JerseyVoteTex(Trickshot.Net.Multiplayer.Session, 0);
                if (mpTex != null) homeTorso = Make.MatTex(mpTex);
                mpTex = JerseyVoteTex(Trickshot.Net.Multiplayer.Session, 1);
                if (mpTex != null) awayTorso = Make.MatTex(mpTex);
            }

            // MULTIPLAYER: the host-authoritative NetMatch owns the sim + snapshots; it
            // builds the slot-mapped bodies (and, on the host, its own MatchGame). Single-player
            // falls through to the local MatchGame below.
            if (net)
            {
                ball.SetCamera(gameCam);
                var nsGo = new GameObject("NetMatch");
                nsGo.transform.SetParent(root, true);
                nsGo.AddComponent<NetMatch>()
                    .Configure(GetInput(), _cam, gameCam, ball, homeTorso, homeLimb, awayTorso, awayLimb, gloveMat, root, arena, perSide);
                return;
            }

            var gmGo = new GameObject("MatchGame");
            gmGo.transform.SetParent(root, true);
            var game = gmGo.AddComponent<MatchGame>();

            var home = new System.Collections.Generic.List<Footballer>();
            var away = new System.Collections.Generic.List<Footballer>();

            // Team size is TOTAL players per side INCLUDING the keeper, so outfield = perSide-1
            // (e.g. 11v11 = 10 outfield + 1 GK). perSide is clamped >= 2 above, so this is >= 1
            // with no Max() guard - and the guard had to go, because Max(1, perSide-1) was the thing
            // that let a perSide of 1 produce an outfielder wearing shirt 1.
            int outfield = perSide - 1;

            // SHIRT, not list index. Shirt 0 is ALWAYS the keeper and outfielders are 1..perSide-1,
            // the same convention the networked board derives from the slot
            // (NetSession.ScrimShirtOfSlot). This used to pass i, so shirt 0 named two different
            // bodies on the same team - the keeper AND the first outfielder - and the two ends of
            // that already disagreed: SimConfig.AiPace(team, 0, false) gave the first outfielder
            // the keeper's hash bucket while MatchGame.BuildStatRows had already called him
            // shirt 1, so his pace and his row on the post-match board came from different numbers.
            for (int t = 0; t < 2; t++)
            {
                var list = t == 0 ? home : away;
                Material torso = t == 0 ? homeTorso : awayTorso;
                Material limb  = t == 0 ? homeLimb  : awayLimb;
                for (int i = 0; i < outfield; i++)
                    list.Add(BuildFootballer(root, ball, game, t, keeper: false, torso, limb, gloveMat: null, shirt: i + 1));
            }

            // Keepers (both AI unless the player picks the keeper role for Home).
            bool humanKeeper = SimConfig.PlayerRole == SimConfig.MatchRole.Keeper;
            Footballer homeKeeper = null, awayKeeper = null;
            KeeperController humanKeeperCtrl = null; ActiveRagdoll humanKeeperRag = null;

            awayKeeper = BuildFootballer(root, ball, game, team: 1, keeper: true, awayTorso, awayLimb, gloveMat, shirt: 0);

            if (humanKeeper)
            {
                // Human keeper: a KeeperController ragdoll at the Home end (defends -Z goal).
                var kGo = new GameObject("HumanKeeper");
                kGo.transform.SetParent(root, true);
                humanKeeperRag = kGo.AddComponent<ActiveRagdoll>();
                // +Z, i.e. OUT toward the pitch. The away goal is at -Z and its mouth opens toward
                // +Z (MatchArena), so the -1 this used to pass pointed him at the back of his own
                // net - and the follow camera below matched it, so the player looked at netting.
                var facing = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up);
                // Human keeper wears the player's customized kit (homeTorso == the painted jersey)
                // + skin + cosmetics + gloves, same as the striker path. Position/facing stay
                // mode-specific (defends the away goal).
                humanKeeperRag.BuildScaled(new Vector3(0f, 0f, arena.awayGoalCenter.z + 1.0f), facing,
                                           homeTorso, Make.Mat(PlayerProfile.Appearance.Skin),
                                           PlayerProfile.HeightScale, PlayerProfile.GirthScale, PlayerProfile.MassMul,
                                           withGloves: true, appearance: PlayerProfile.Appearance);
                humanKeeperCtrl = kGo.AddComponent<KeeperController>();
                humanKeeperCtrl.Init(GetInput(), humanKeeperRag, ball);
                // Distribute into THIS pitch. The default is the 24 x 34 training arena, which on an
                // 11-a-side Match mode (68 x 104) would clamp every play-out to the same short punt.
                humanKeeperCtrl.AimBounds = new Vector2(arena.halfWidth - 1f, arena.halfLength - 1f);
                // 5th arg (goal Transform) is only used by the unused Broadcast cam; pass null.
                gameCam.Init(_cam, ball.transform, humanKeeperRag.Pelvis.transform, null, null);
                gameCam.SetKeeperFollow(humanKeeperRag.Pelvis.transform,
                    () => Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up), () => GetInput().Look);
                humanKeeperCtrl.SetLookYawSource(() => gameCam.KeeperLookYaw);
            }
            else
            {
                homeKeeper = BuildFootballer(root, ball, game, team: 0, keeper: true, homeTorso, homeLimb, gloveMat, shirt: 0);
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
            game.Configure(GetInput(), ball, gameCam, arena, SimConfig.PlayerRole,
                           home, away, homeKeeper, awayKeeper,
                           humanStriker, humanDribble, humanKeeperCtrl, humanKeeperRag);
            LockCursor();
        }

        // Builds one Match-mode footballer: an active ragdoll + Striker + Dribble + kick
        // detectors + a Footballer AI component. Striker/Dribble are DISABLED (AI/idle)
        // until the driver hands this body control.
        // `shirt` is the D6 identity: 0 = keeper, 1..perSide-1 = outfield, per team. It was named
        // `index` and every caller passed a list position, which is what made shirt 0 ambiguous.
        Footballer BuildFootballer(Transform root, BallController ball, MatchGame game,
                                   int team, bool keeper, Material torso, Material limb,
                                   Material gloveMat, int shirt)
        {
            var go = new GameObject((team == 0 ? "Home" : "Away") + (keeper ? "GK" : "P" + shirt));
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
            f.Init(game, ball, ragdoll, team, keeper, attackZ, Vector3.zero, shirt);
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
            // The keeper wears the player's customized kit + skin + head cosmetics on a body
            // scaled to their height/weight, plus goalkeeper gloves - same look as multiplayer.
            // Mass is BODY-only (MassMul, not EffectiveMassMul): the keeper flow skips the Skill
            // stage, so a prior striker session's massbonus/Immovable must not inflate the keeper.
            ragdoll.BuildScaled(SimConfig.KeeperStart, facing,
                                JerseyMaterial(), Make.Mat(PlayerProfile.Appearance.Skin),
                                PlayerProfile.HeightScale, PlayerProfile.GirthScale, PlayerProfile.MassMul,
                                withGloves: true, appearance: PlayerProfile.Appearance);
            var keeper = keeperGo.AddComponent<KeeperController>();
            keeper.Init(GetInput(), ragdoll, ball);

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

        static void LockCursor() => GameInput.CaptureCursor(true);

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
            // The striking bones come from the body's layout, so a quadruped detects off its FRONT
            // hooves instead of the biped's feet. BOTH sides are always listed so a bicycle scored
            // off either limb classifies (the right side is the strong-side default, but a
            // left-footed bike must count too). See BodyLayoutDef.StrikeBones.
            var strike = ragdoll.StrikeBones;
            for (int i = 0; i < strike.Length; i++)
                AddDetector(ragdoll.Rb(strike[i]), striker, ragdoll, ball);
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
            // Catch-up cap. Unity's default (0.333 s) lets one slow frame be followed by up to 16
            // physics steps in the next, each a full 22-ragdoll solve, which makes THAT frame slower
            // still - the spiral behind "it suddenly went choppy and stayed choppy". Five steps per
            // frame at most: past that the sim runs briefly slower than real time instead of
            // snowballing, and a healthy machine never gets near it. Solver iterations are left at
            // 20/8 on purpose: they are what keeps the joint chains stable under load, and lowering
            // them changes how the bodies move.
            Time.maximumDeltaTime = 0.1f;
        }

        Light MakeSun(Transform root)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(root, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.97f, 0.9f);
            l.intensity = 1.15f;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            return l;
        }
    }
}
