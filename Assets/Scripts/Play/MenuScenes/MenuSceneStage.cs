using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Owns the live mode vignettes behind the Single Player / Multiplayer panels: one sub-stage
    /// per mode, one shared camera, one RenderTexture per panel, and the global state a live scene
    /// needs. The menu screen tells it which panel the mouse is over; everything else is here.
    ///
    /// ONE SCENE SIMULATES AT A TIME. Physics is a single global PhysX world (no per-scene
    /// simulation anywhere in this project), so "only the hovered panel is live" means every other
    /// scene's bodies are kinematic and its ball is parked. Leaving a panel resets that scene to
    /// its built pose and re-freezes it, so coming back plays the same beat from the top.
    ///
    /// GLOBALS. A live scene needs the same knobs the title reel uses (slow-mo for readability, a
    /// keeper ability that is not the player's last "None" pick, and an attack goal pointing at
    /// this stage rather than the real pitch). Every one of them is saved when first written and
    /// restored ONLY IF still holding the value this stage wrote - MenuBackground owns the same
    /// statics on the Single Player screen and can be destroyed before or after this object, and a
    /// blind restore in the wrong order is what would leak slow-mo into a match.
    ///
    /// The sub-stages sit far up the +Z axis, spaced apart, at x = 0. Three things fix that
    /// placement:
    ///   - ON THE AXIS, so the direction from any actor to the readonly SimConfig.GoalCenter is
    ///     +Z. KickDetector's bicycle bonus and DefensiveWall's facing both steer by it.
    ///   - POSITIVE Z, because shipped code assumes a goal is at positive z: BallController's
    ///     under-the-crossbar cap solves its goal plane as Sign(ballVz) * Abs(AttackGoalCenter.z),
    ///     which at a negative-z stage would put the plane thousands of metres the wrong way and
    ///     silently disable the clamp that keeps a bicycle kick under the bar.
    ///   - FAR AWAY, because isolation here is purely spatial (the project has no layers and every
    ///     camera renders everything): both full-screen menu cameras stop at 900 m, and
    ///     PlayerPreview (1000,0,1000) and the editor cosmetic gallery (2000,0,2000) already own
    ///     their own patches of world.
    /// </summary>
    // Renders in LateUpdate and must see the final pose: HairSim uploads its mesh in ITS
    // LateUpdate, and with every script at the default order Unity may run them either way round,
    // which would capture hair a frame stale. A high order puts this last, provably.
    [DefaultExecutionOrder(1000)]
    public class MenuSceneStage : MonoBehaviour
    {
        public const float StageZ = 3000f;       // first sub-stage, straight up +Z
        public const float StageSpacing = 120f;  // between sub-stages: further than any scene reaches

        // Slow-mo while a scene plays, matching the title reel so the two menus read alike.
        const float SlowMo = 0.7f;
        const float KeeperAbility = 0.6f;
        // A scene renders for a moment after it is frozen so the hair, net and any settling body
        // reach a still pose before the panel keeps that picture.
        const float SettleSeconds = 0.5f;

        class Panel
        {
            public GameMode Mode;
            public MenuScene Scene;
            public RenderTexture Rt;
            public int PxW, PxH;
            public float Settle;      // > 0 while a frozen scene is still being re-rendered
            public bool Dirty = true; // needs a render even when frozen (first frame, resize)
        }

        readonly List<Panel> _panels = new List<Panel>();
        readonly Dictionary<GameMode, int> _byMode = new Dictionary<GameMode, int>();
        Camera _cam;
        int _live = -1;          // index of the simulating scene, or -1
        int _built;              // how many sub-stages have been built so far (one per frame)

        // Globals we set while a scene is live, with the value we wrote so a restore can tell
        // whether somebody else has since taken ownership. Saved ONCE, on the transition from "no
        // scene live" to "a scene live" - saving on every hover change would snapshot our own
        // previous value and restore that instead of the real one.
        bool _globalsHeld;
        float _savedTimeScale, _savedFixedDt, _savedKeeperAbility, _savedBallSpeed, _savedStrikerSpeed;
        Vector3 _savedAttackGoal;
        float _wroteKeeperAbility, _wroteBallSpeed, _wroteStrikerSpeed;
        Vector3 _wroteAttackGoal;

        /// <summary>Build the camera and queue one sub-stage per listed mode. Modes with no scene
        /// are skipped, so a panel for them simply shows no picture.</summary>
        public void Setup(IList<GameMode> modes)
        {
            var camGo = new GameObject("MenuSceneCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            // TRANSPARENT, not the sky: these panels show the figures alone over the menu's own
            // panel plate, so the camera clears to zero alpha and the RenderTexture carries only
            // what the bodies cover. No AudioListener (the main camera owns the only one) and
            // never a GameCamera, which would drive Time.timeScale globally.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 70f;   // shorter than the gap between sub-stages
            _cam.enabled = false;      // rendered by hand, into a texture, never to the screen

            // Own key + fill so a panel is lit the same on both submenus. The title reel parks
            // every directional light that exists when it starts and restores them when it dies,
            // so a stage must not depend on whatever happens to be on: these are created after it
            // and are left alone. Full mask, since the project has no layers - they also reach the
            // world, which the reel's own lights already do.
            var key = new GameObject("MenuSceneKey");
            key.transform.SetParent(transform, false);
            var kl = key.AddComponent<Light>();
            kl.type = LightType.Directional;
            kl.color = new Color(1f, 0.97f, 0.92f);
            kl.intensity = 1.15f;
            kl.transform.rotation = Quaternion.Euler(38f, 152f, 0f);
            kl.shadows = LightShadows.None;   // nothing to catch a shadow: there is no ground
            kl.cullingMask = ~0;

            var fillGo = new GameObject("MenuSceneFill");
            fillGo.transform.SetParent(transform, false);
            var fl = fillGo.AddComponent<Light>();
            fl.type = LightType.Directional;
            fl.color = new Color(0.80f, 0.87f, 1f);
            fl.intensity = 0.30f;
            fl.transform.rotation = Quaternion.Euler(20f, -20f, 0f);
            fl.shadows = LightShadows.None;
            fl.cullingMask = ~0;

            for (int i = 0; i < modes.Count; i++)
            {
                var scene = Create(modes[i]);
                if (scene == null) continue;
                _byMode[modes[i]] = _panels.Count;
                _panels.Add(new Panel { Mode = modes[i], Scene = scene });
            }
        }

        static MenuScene Create(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Striker:    return new StrikerScene();
                case GameMode.Goalkeeper: return new KeeperScene();
                case GameMode.Match:      return new MatchScene();
                case GameMode.Accuracy:   return new AccuracyScene();
                case GameMode.FreeKick:   return new FreeKickScene();
                case GameMode.SetPieces:  return new FreeKickScene();
                // The cup: a penalty driven low into the corner past a diving keeper, the
                // fist pump, and the trophy in the foreground (design 3.1).
                case GameMode.TrickshotCup: return new CupScene();
                default: return null;
            }
        }

        /// <summary>The texture for a mode's panel, or null while its stage is still being built.
        /// The caller passes the panel's inner rect in DEVICE pixels so the texture matches the
        /// camera's aspect exactly and the picture is never stretched.</summary>
        public RenderTexture Texture(GameMode mode, int pxW, int pxH)
        {
            if (!_byMode.TryGetValue(mode, out int i) || i >= _built) return null;
            var p = _panels[i];
            pxW = Mathf.Clamp(pxW, 16, 4096);
            pxH = Mathf.Clamp(pxH, 16, 4096);
            if (p.Rt == null || p.PxW != pxW || p.PxH != pxH)
            {
                if (p.Rt != null) p.Rt.Release();
                if (p.Rt != null) Destroy(p.Rt);
                // ARGB32 explicitly: the panel is composited over the menu, so the alpha the
                // transparent clear writes has to survive into the texture.
                p.Rt = new RenderTexture(pxW, pxH, 24, RenderTextureFormat.ARGB32)
                { name = "MenuScene_" + p.Mode };
                p.PxW = pxW; p.PxH = pxH;
                p.Dirty = true;   // a fresh texture holds nothing until it is rendered
            }
            return p.Rt;
        }

        /// <summary>Which panel the mouse is over, by mode; pass null for none. Changing it
        /// resets and freezes the old scene and starts the new one from its first frame.</summary>
        public void SetHover(GameMode? mode)
        {
            int want = -1;
            if (mode.HasValue && _byMode.TryGetValue(mode.Value, out int i) && i < _built) want = i;
            if (want == _live) return;

            if (_live >= 0)
            {
                var old = _panels[_live];
                old.Scene.Reset();
                old.Scene.Freeze();
                old.Settle = SettleSeconds;   // keep drawing until the still picture stops moving
                old.Dirty = true;
            }
            _live = want;
            if (_live >= 0)
            {
                TakeGlobals();
                var p = _panels[_live];
                // Reset BEFORE Thaw, and on the way IN as well as the way out. The scene has to
                // start from its built pose with a zeroed clock every time: the outgoing reset
                // above cannot do it alone, because the first hover of a freshly built scene never
                // had one, and a scene whose bodies were settling when it was frozen would
                // otherwise resume from wherever they came to rest.
                p.Scene.Reset();
                p.Scene.Thaw();
                p.Dirty = true;
            }
            else ReleaseGlobals();
        }

        void Update()
        {
            // Build one sub-stage per frame: a scene is a turf slab, a goal, a net and two bodies,
            // and building five in one frame is a visible hitch on the menu.
            if (_built < _panels.Count)
            {
                var p = _panels[_built];
                var root = new GameObject("Stage_" + p.Mode);
                root.transform.SetParent(transform, false);
                Vector3 origin = new Vector3(0f, 0f, StageZ + StageSpacing * _built);
                root.transform.position = origin;
                p.Scene.Init(this, root.transform, origin);
                p.Scene.Build();
                p.Scene.Freeze();
                p.Dirty = true;
                _built++;
            }

            if (_live >= 0)
            {
                var p = _panels[_live];
                if (!p.Scene.Done)
                {
                    // The attack goal follows the live scene: BallController's assist and
                    // under-bar cap steer by it, and a menu body must not aim at the real pitch.
                    SimConfig.AttackGoalCenter = p.Scene.GoalCenter;
                    _wroteAttackGoal = SimConfig.AttackGoalCenter;
                    p.Scene.Tick(Time.deltaTime);
                }
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < _built; i++)
            {
                var p = _panels[i];
                if (p.Rt == null) continue;
                bool live = i == _live;
                if (p.Settle > 0f) p.Settle -= Time.unscaledDeltaTime;
                if (!live && p.Settle <= 0f && !p.Dirty) continue;
                Render(p);
                p.Dirty = false;
            }
        }

        // Ambient is a GLOBAL the menu does not own: the title reel's sky ambient on one screen,
        // the customize preview's raised PreviewAmbient on another. Left alone, the same figure
        // renders correctly on one submenu and blown white on the other. Pin it for the duration
        // of the render and put it straight back, so nothing else on screen shifts.
        const float SceneAmbient = 0.42f;

        void Render(Panel p)
        {
            float keepAmb = RenderSettings.ambientIntensity;
            var keepMode = RenderSettings.ambientMode;
            var keepLight = RenderSettings.ambientLight;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.38f, 0.41f, 0.48f);
            RenderSettings.ambientIntensity = SceneAmbient;

            p.Scene.Frame(out var camPos, out var lookAt, out float fov);
            _cam.transform.position = camPos;
            _cam.transform.rotation = Quaternion.LookRotation(lookAt - camPos, Vector3.up);
            _cam.fieldOfView = fov;
            var keep = _cam.targetTexture;
            _cam.targetTexture = p.Rt;
            _cam.Render();
            _cam.targetTexture = keep;

            RenderSettings.ambientMode = keepMode;
            RenderSettings.ambientLight = keepLight;
            RenderSettings.ambientIntensity = keepAmb;
        }

        // ---- globals ---------------------------------------------------------------------------

        void TakeGlobals()
        {
            if (_globalsHeld) return;
            _globalsHeld = true;
            _savedTimeScale = Time.timeScale;
            _savedFixedDt = Time.fixedDeltaTime;
            _savedKeeperAbility = SimConfig.KeeperAbility;
            _savedBallSpeed = SimConfig.BallSpeedMul;
            _savedStrikerSpeed = SimConfig.StrikerMoveSpeed;
            _savedAttackGoal = SimConfig.AttackGoalCenter;

            // Slow-mo is SET, not multiplied: on the Single Player screen the title reel is alive
            // and has already put the world at 0.7, and stacking would crawl. Matching it also
            // means the two submenus pace the same.
            Time.timeScale = SlowMo;
            Time.fixedDeltaTime = 0.02f * SlowMo;
            // The keeper parks himself invisible at ability <= 0.001, which is exactly what the
            // player's last "None" keeper pick leaves behind.
            SimConfig.KeeperAbility = KeeperAbility;
            // Both of these are hand-timed in the choreographies, and both are mutable statics the
            // last screen to touch them leaves behind: the title reel pins BallSpeedMul at 1.5
            // (which would shorten every flight time asked for here), and a pre-match speed slider
            // leaves StrikerMoveSpeed wherever the player put it (which would move every run-up).
            SimConfig.BallSpeedMul = 1f;
            SimConfig.StrikerMoveSpeed = SimConfig.StrikerMoveSpeedBase;
            _wroteKeeperAbility = SimConfig.KeeperAbility;
            _wroteBallSpeed = SimConfig.BallSpeedMul;
            _wroteStrikerSpeed = SimConfig.StrikerMoveSpeed;
            _wroteAttackGoal = SimConfig.AttackGoalCenter;
        }

        /// <summary>
        /// Put the globals back. MenuBackground writes several of the same statics and restores
        /// them unconditionally in its own OnDestroy, which runs at the END of the frame it was
        /// destroyed in - so for the ones it shares we restore only what is still ours, or we
        /// would overwrite its restore with a stale snapshot.
        ///
        /// TIME IS THE EXCEPTION and is restored unconditionally, because a leaked slow-mo is the
        /// one failure that follows the player into a match: nothing on the menu-to-match path
        /// resets Time.timeScale, and GameCamera only eases it back once a match camera exists.
        /// </summary>
        void ReleaseGlobals()
        {
            if (!_globalsHeld) return;
            _globalsHeld = false;
            Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
            Time.fixedDeltaTime = _savedFixedDt > 0f ? _savedFixedDt : 0.02f;
            if (Mathf.Approximately(SimConfig.KeeperAbility, _wroteKeeperAbility))
                SimConfig.KeeperAbility = _savedKeeperAbility;
            if (Mathf.Approximately(SimConfig.BallSpeedMul, _wroteBallSpeed))
                SimConfig.BallSpeedMul = _savedBallSpeed;
            if (Mathf.Approximately(SimConfig.StrikerMoveSpeed, _wroteStrikerSpeed))
                SimConfig.StrikerMoveSpeed = _savedStrikerSpeed;
            if (SimConfig.AttackGoalCenter == _wroteAttackGoal)
                SimConfig.AttackGoalCenter = _savedAttackGoal;
        }

        /// <summary>
        /// Stop simulating and hand every global back RIGHT NOW, then destroy. The owning screen
        /// calls this before it invokes its pick/back callback: Object.Destroy is deferred to the
        /// end of the frame while the next screen is built synchronously, so waiting for OnDestroy
        /// would run a live scene - and hold the menu's slow-mo - into the frame that builds a
        /// match. This is the same shape as GameBootstrap.HideMenuBackground.
        /// </summary>
        public void Teardown()
        {
            SetHover(null);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            ReleaseGlobals();
            for (int i = 0; i < _panels.Count; i++)
            {
                var p = _panels[i];
                p.Scene?.Destroy();
                if (p.Rt != null) { p.Rt.Release(); Destroy(p.Rt); p.Rt = null; }
            }
            _panels.Clear();
            _byMode.Clear();
        }
    }
}
