using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One shot of a scripted cinematic (design 7.8: "a scripted shot list (position, look-at, fov,
    /// duration, ease)"), played by <see cref="CupCameraRig.Cinematic"/>. Two kinds:
    ///  - a POSITION shot moves the camera from <see cref="From"/> to <see cref="To"/> over
    ///    <see cref="Seconds"/> (equal = a static shot);
    ///  - an ORBIT shot (<see cref="Orbit"/>) rides a circle about <see cref="OrbitCentre"/>, its
    ///    angle, radius and height each easing from the start value to the end value.
    /// The look-at is a point or a delegate (a moving body); the FOV eases from <see cref="Fov"/>
    /// to <see cref="FovEnd"/> (negative = constant). <see cref="Cut"/> snaps in at the shot's
    /// start; false eases from wherever the camera was. <see cref="OnStart"/> fires the frame the
    /// shot begins - the choreography's cue for whatever happens under that cut.
    /// </summary>
    public sealed class CupShot
    {
        public float Seconds = 3f;
        public bool Cut = true;
        // Position shot
        public Vector3 From, To;
        // Orbit shot
        public bool Orbit;
        public Vector3 OrbitCentre;
        public float AngleFrom, AngleTo;
        public float RadiusFrom = 6f, RadiusTo = 6f;
        public float HeightFrom = 2f, HeightTo = 2f;
        // Aim
        public Vector3 LookPoint;
        public Func<Vector3> LookAt;
        public float Fov = 50f;
        public float FovEnd = -1f;
        /// <summary>0..1 -> 0..1 blend for the motion; null = smoothstep.</summary>
        public Func<float, float> Ease;
        public Action OnStart;

        /// <summary>A static or moving position shot.</summary>
        public static CupShot Move(Vector3 from, Vector3 to, Func<Vector3> lookAt, float fov, float seconds, bool cut = true)
        {
            return new CupShot { From = from, To = to, LookAt = lookAt, Fov = fov, Seconds = seconds, Cut = cut };
        }

        /// <summary>An orbit segment: angle / radius / height each easing start -> end.</summary>
        public static CupShot Arc(Vector3 centre, float angleFrom, float angleTo, float radiusFrom, float radiusTo,
                                  float heightFrom, float heightTo, Func<Vector3> lookAt, float fov, float seconds, bool cut = true)
        {
            return new CupShot
            {
                Orbit = true, OrbitCentre = centre, AngleFrom = angleFrom, AngleTo = angleTo,
                RadiusFrom = radiusFrom, RadiusTo = radiusTo, HeightFrom = heightFrom, HeightTo = heightTo,
                LookAt = lookAt, Fov = fov, Seconds = seconds, Cut = cut,
            };
        }

        /// <summary>The camera position at blend k.</summary>
        public Vector3 PositionAt(float k)
        {
            if (!Orbit) return Vector3.Lerp(From, To, k);
            float a = Mathf.Lerp(AngleFrom, AngleTo, k) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(RadiusFrom, RadiusTo, k);
            float h = Mathf.Lerp(HeightFrom, HeightTo, k);
            return OrbitCentre + new Vector3(Mathf.Sin(a) * r, h, Mathf.Cos(a) * r);
        }

        /// <summary>The camera angle at the end of an orbit shot (a following PodiumOrbit picks the orbit up from here).</summary>
        public float EndAngle => Orbit ? AngleTo : 0f;
    }

    /// <summary>
    /// Every camera the cup uses, behind one switch (design 7.8 / 7.9 / 8.1). The round driver and
    /// the choreography ask for a VIEW; the rig either hands the camera to <see cref="GameCamera"/>
    /// (free-kick taker = Follow, keeper = KeeperFollow, replay = Broadcast) or places it itself
    /// (penalty cam, lineup cone, coin toss, walk-back two-shot, hold, podium orbit, spectator
    /// mirror). Callers never touch the Camera transform.
    ///
    /// How the two owners share one Camera without fighting:
    ///  - Rig-owned views DISABLE the GameCamera component (its LateUpdate stops writing the
    ///    transform, its mouse look stops accumulating). No execution-order tricks, nothing to
    ///    undo but "enabled".
    ///  - The PENALTY view is the exception: GameCamera stays enabled in Follow mode because its
    ///    yaw/pitch ARE the aim (SetPieceTaker.LookAimPoint), and this component - which runs after
    ///    it ([DefaultExecutionOrder]) - overwrites position, rotation and FOV every LateUpdate
    ///    with the penalty framing (CupPenaltyCam). The look clamp lives in GameCamera
    ///    (SetLookClamp), the one small edit the cup made there.
    ///  - Delegated views re-enable GameCamera and set its mode; the rig does nothing per frame.
    ///
    /// timeScale discipline (borrowed from MenuSceneStage): the rig never writes Time.timeScale
    /// itself. GameCamera.UpdateSlowMo owns it while enabled, and GameCamera.OnDisable resets it
    /// when a rig view disables the component - the same reset the menu relies on. Release()
    /// restores 1 / 0.02 defensively unless a slow-mo is still easing or the game is frozen.
    ///
    /// Everything is gated on <see cref="PauseMenu.Frozen"/> (a Solo pause), never on Paused: an
    /// overlay pause (MP) keeps the camera running under the menu. Mouse look is read only while
    /// the cursor is captured and no menu is up, so a free cursor (wheel, cards) never spins a view.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class CupCameraRig : MonoBehaviour
    {
        /// <summary>Which framing is in force. Delegate = GameCamera is driving (Follow / KeeperFollow / Broadcast).</summary>
        public enum View { None, Penalty, Delegate, Lineup, CoinToss, WalkBack, Hold, Podium, Mirror, Cinematic }

        // ---- local tunables (tune) --------------------------------------------------------------
        /// <summary>Lineup cone camera: behind the body along its facing, and up (m).</summary>
        public const float LineupCamBack = 4.2f;
        public const float LineupCamHeight = 2.3f;
        public const float LineupCamFov = 60f;
        /// <summary>Walk-back shot 1 (low, beside the goal): lateral gap outside the post, height, FOV.</summary>
        public const float WalkBackSideGap = 1.6f;
        public const float WalkBackLowHeight = 0.7f;
        public const float WalkBackTrackFov = 38f;
        /// <summary>Walk-back shot 2 (wide, behind the lineup): behind, up, toward the pitch centre, FOV.</summary>
        public const float WalkBackWideBack = 5.5f;
        public const float WalkBackWideHeight = 2.2f;
        public const float WalkBackWideInset = 1.5f;
        public const float WalkBackWideFov = 50f;
        /// <summary>Podium orbit FOV and the drag sensitivity of the orbit height (m per mouse unit).</summary>
        public const float PodiumFov = 50f;
        public const float PodiumDragHeightRate = 0.02f;
        public const float PodiumHeightMin = 0.9f;
        public const float PodiumHeightMax = 5.5f;
        /// <summary>Spectator mirror smoothing (1/s) - the stream arrives at 20 Hz.</summary>
        public const float MirrorRate = 18f;
        /// <summary>The podium orbit's default start angle (deg): front-left, so the trophy faces the camera.</summary>
        public const float PodiumStartAngle = 200f;

        /// <summary>
        /// Podium: a mouse drag (LMB) steers the orbit even while the cursor is FREE (the podium
        /// and the trophy lift's free window keep the cursor free for their buttons and wheel, and
        /// "drag to orbit" has to work there). Off, the drag needs a captured cursor like every
        /// other rig view.
        /// </summary>
        public bool PodiumFreeCursorDrag = true;

        Camera _cam;
        GameCamera _gameCam;
        GameInput _input;
        View _view = View.None;

        // Penalty
        readonly CupPenaltyCam _pen = new CupPenaltyCam();
        // Lineup cone
        ActiveRagdoll _lineBody;
        float _lineBaseYaw, _lineYaw, _linePitch;
        // Fixed shot (coin toss)
        Vector3 _fixedPos, _fixedLook;
        float _fixedFov;
        // Walk-back two-shot
        ActiveRagdoll _wbShooter;
        Vector3 _wbSlot, _wbLowPos, _wbWidePos;
        float _wbSeconds, _wbTime;
        bool _wbCut;
        // Hold
        ActiveRagdoll _holdBody;
        Vector3 _holdPos;
        // Podium orbit
        Vector3 _orbitCentre;
        float _orbitAngle, _orbitDist, _orbitDistTarget, _orbitHeight, _dragTimer;
        // Mirror
        Vector3 _mirPos;
        Quaternion _mirRot = Quaternion.identity;
        float _mirFov = 58f;
        bool _mirFresh;
        // Cinematic
        IList<CupShot> _shots;
        int _shotIndex;
        float _shotTime;
        bool _shotStarted;
        Action _shotsDone;

        Vector3 _velPos;
        bool _snap;   // the next placement cuts instead of easing

        /// <summary>The view in force.</summary>
        public View Current => _view;
        /// <summary>True while the rig places the camera itself (anything but None / Delegate).</summary>
        public bool OwnsCamera => _view != View.None && _view != View.Delegate;
        /// <summary>The penalty helper (diagnostics: solved FOV, post fraction).</summary>
        public CupPenaltyCam Penalty => _pen;

        // ---- the spectator stream reads these ------------------------------------------------
        public Vector3 CamPos => _cam != null ? _cam.transform.position : Vector3.zero;
        public Quaternion CamRot => _cam != null ? _cam.transform.rotation : Quaternion.identity;
        public float CamFov => _cam != null ? _cam.fieldOfView : 58f;

        /// <summary>Create the rig under <paramref name="root"/> (the match root: it lives for the whole cup).</summary>
        public static CupCameraRig Create(Transform root, Camera cam, GameCamera gameCam, GameInput input)
        {
            var go = new GameObject("CupCameraRig");
            if (root != null) go.transform.SetParent(root, false);
            var rig = go.AddComponent<CupCameraRig>();
            rig._cam = cam;
            rig._gameCam = gameCam;
            rig._input = input;
            return rig;
        }

        // ================================================================ views
        /// <summary>
        /// The shooter's camera. Penalties: the FIFA penalty cam (CupPenaltyCam) with GameCamera's
        /// yaw/pitch clamped about the goal. Free kicks: plain GameCamera.Follow. In both the look
        /// is cut to face the goal at the start of the kick.
        /// </summary>
        public void TakerView(ActiveRagdoll body, Vector3 ballSpot, Vector3 goalCenter, CupFormat format)
        {
            if (_gameCam == null) return;
            Transform pivot = body != null && body.Pelvis != null ? body.Pelvis.transform : null;
            _gameCam.SetFollow(pivot, LookSource, ScrollSource);
            _gameCam.SetMode(GameCamera.Mode.Follow);
            _gameCam.FreezeLook = false;

            if (format == CupFormat.Penalties)
            {
                Vector3 takerPos = pivot != null ? pivot.position : ballSpot - Vector3.forward * CupTuning.RunUpDistance;
                bool lefty = PlayerProfile.LeftFooted;
                _pen.Latch(ballSpot, goalCenter, takerPos, lefty);
                float pMin, pMax;
                CupPenaltyCam.PitchClamp(out pMin, out pMax);
                _gameCam.ClearLookClamp();
                _gameCam.SetLook(_pen.YawToGoal, _pen.PitchToGoal);
                _gameCam.SetLookClamp(_pen.YawToGoal, CupTuning.PenaltyCamYawLimit, pMin, pMax);
                _view = View.Penalty;
            }
            else
            {
                _gameCam.ClearLookClamp();
                Vector3 axis = goalCenter - ballSpot;
                float yaw0 = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
                _gameCam.SetLook(yaw0, 14f);
                _pen.Clear();
                _view = View.Delegate;
            }
        }

        /// <summary>The keeper's camera: GameCamera.KeeperFollow about the fixed forward (KeeperFaceDir).</summary>
        public void KeeperView(ActiveRagdoll body)
        {
            if (_gameCam == null) return;
            Transform pivot = body != null && body.Pelvis != null ? body.Pelvis.transform : null;
            _gameCam.ClearLookClamp();
            _gameCam.FreezeLook = false;
            _gameCam.SetKeeperFollow(pivot, KeeperFacing, LookSource, ScrollSource);
            _pen.Clear();
            _view = View.Delegate;
        }

        /// <summary>
        /// The lineup look cone (design 7.3): a chase view from behind the body, base yaw toward the
        /// goal, mouse look within +-LineupYawLimit / LineupPitchMin..Max (deg, +pitch = down), no movement.
        /// </summary>
        public void LineupView(ActiveRagdoll body)
        {
            _lineBody = body;
            Vector3 from = body != null && body.Pelvis != null ? body.Pelvis.position : Vector3.zero;
            Vector3 toGoal = SimConfig.GoalCenter - from;
            _lineBaseYaw = Mathf.Atan2(toGoal.x, toGoal.z) * Mathf.Rad2Deg;
            _lineYaw = 0f;
            _linePitch = 6f;
            _pen.Clear();
            SetOwnView(View.Lineup);
        }

        /// <summary>The coin toss static wide shot (design 7.1): CoinCamDistance out along towardCamera, CoinCamHeight up, FOV CoinCamFov, aimed at the referee's chest.</summary>
        public void CoinTossView(Vector3 refereePos, Vector3 towardCamera)
        {
            Vector3 dir = towardCamera;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = -Vector3.forward;   // the goal side: the ceremony stands in front of the goal
            dir.Normalize();
            _fixedPos = refereePos + dir * CupTuning.CoinCamDistance + Vector3.up * CupTuning.CoinCamHeight;
            _fixedLook = refereePos + Vector3.up * 1.3f;
            _fixedFov = CupTuning.CoinCamFov;
            _pen.Clear();
            SetOwnView(View.CoinToss);
        }

        /// <summary>
        /// The walk-back two-shot (design 7.5): WalkBackTrackShot seconds low from beside the goal
        /// following the shooter's head, then a cut to a wide shot from behind the lineup that
        /// watches them arrive. After <paramref name="seconds"/> the wide shot simply holds.
        /// </summary>
        public void WalkBackView(ActiveRagdoll shooter, Vector3 lineupSlot, float seconds)
        {
            _wbShooter = shooter;
            _wbSlot = lineupSlot;
            _wbSeconds = Mathf.Max(0.1f, seconds);
            _wbTime = 0f;
            _wbCut = false;

            // Shot 1: low, just outside the post on the shooter's lineup side, a hair in front of the line.
            float side = lineupSlot.x >= 0f ? 1f : -1f;
            Vector3 g = SimConfig.GoalCenter;
            _wbLowPos = new Vector3(g.x + side * (SimConfig.GoalWidth * 0.5f + WalkBackSideGap), WalkBackLowHeight, g.z - 0.6f);

            // Shot 2: behind the lineup (away from the goal), a little toward the pitch centre so the
            // line of bodies reads as a line rather than a stack.
            Vector3 away = lineupSlot - g;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-4f) away = -Vector3.forward;
            away.Normalize();
            Vector3 inward = new Vector3(-side, 0f, 0f);
            _wbWidePos = lineupSlot + away * WalkBackWideBack + inward * WalkBackWideInset + Vector3.up * WalkBackWideHeight;
            _pen.Clear();
            SetOwnView(View.WalkBack);
        }

        /// <summary>Keep the camera where it is and turn to follow this body (the scored window, the dejection beat).</summary>
        public void HoldOn(ActiveRagdoll body)
        {
            _holdBody = body;
            _holdPos = _cam != null ? _cam.transform.position : Vector3.zero;
            _pen.Clear();
            // No snap: the current framing IS the framing; only the rotation tracks from here.
            SetOwnView(View.Hold, snap: false);
        }

        /// <summary>The replay: GameCamera.Broadcast (auto vantage until the viewer takes the orbit over).</summary>
        public void ReplayView()
        {
            if (_gameCam == null) return;
            _gameCam.ClearLookClamp();
            _gameCam.FreezeLook = false;
            _gameCam.SetMode(GameCamera.Mode.Broadcast);
            _pen.Clear();
            _view = View.Delegate;
        }

        /// <summary>
        /// The podium orbit (design 8.1): PodiumOrbitRps around <paramref name="centre"/> at
        /// PodiumOrbitRadius / PodiumOrbitHeight; a mouse drag (LMB) takes the orbit over for
        /// PodiumDragTakeover seconds; the wheel zooms PodiumZoomMin..Max.
        /// </summary>
        public void PodiumOrbit(Vector3 centre) => PodiumOrbit(centre, PodiumStartAngle, false);

        /// <summary>
        /// The podium orbit from a given start angle (deg about +Y, 0 = behind the centre on +Z),
        /// optionally RESTARTED while already orbiting - a cut to a new angle (the podium's
        /// hand-over cut; the trophy lift picking the orbit up where its last shot ended), with the
        /// radius and height back at the tunables and any drag takeover cleared.
        /// </summary>
        public void PodiumOrbit(Vector3 centre, float startAngle, bool restart)
        {
            bool fresh = _view != View.Podium || restart;
            _orbitCentre = centre;
            if (fresh)
            {
                _orbitAngle = startAngle;
                _orbitDist = _orbitDistTarget = CupTuning.PodiumOrbitRadius;
                _orbitHeight = CupTuning.PodiumOrbitHeight;
                _dragTimer = 0f;
            }
            _pen.Clear();
            SetOwnView(View.Podium, snap: fresh);
        }

        /// <summary>The orbit's current angle (deg), for a caller that wants to continue it elsewhere.</summary>
        public float OrbitAngle => _orbitAngle;

        /// <summary>
        /// Play a shot list (design 7.8): the coin toss, the walk-back and the podium have their own
        /// views; this is the generic one the trophy lift (8.2) and any future sequence use. Each
        /// shot runs for its Seconds then the next begins (its OnStart fires that frame); after the
        /// last, the camera HOLDS the final pose and `onDone` fires once - the caller then picks the
        /// next view (a PodiumOrbit, a Release). Unscaled time like every rig view; nothing moves
        /// while PauseMenu.Frozen. An empty list fires onDone at once and changes nothing.
        /// </summary>
        public void Cinematic(IList<CupShot> shots, Action onDone = null)
        {
            if (shots == null || shots.Count == 0)
            {
                onDone?.Invoke();
                return;
            }
            _shots = shots;
            _shotIndex = 0;
            _shotTime = 0f;
            _shotStarted = false;
            _shotsDone = onDone;
            _pen.Clear();
            // The first shot decides whether it cuts or eases; SetOwnView's snap is that flag.
            SetOwnView(View.Cinematic, snap: shots[0].Cut);
        }

        /// <summary>The shot in progress (null outside a cinematic or after its last shot ended).</summary>
        public CupShot CurrentShot => _view == View.Cinematic && _shots != null && _shotIndex < _shots.Count ? _shots[_shotIndex] : null;
        /// <summary>Seconds into the current shot.</summary>
        public float ShotTime => _shotTime;
        /// <summary>The cinematic has played its last shot (the camera is holding its final pose).</summary>
        public bool CinematicDone => _view == View.Cinematic && _shots != null && _shotIndex >= _shots.Count;

        /// <summary>Spectator: reproduce a remote camera pose exactly (eased between 20 Hz samples).</summary>
        public void MirrorView(Vector3 pos, Quaternion rot, float fov)
        {
            _mirPos = pos;
            _mirRot = rot;
            _mirFov = Mathf.Clamp(fov, 10f, 120f);
            if (_view != View.Mirror) _mirFresh = true;
            _pen.Clear();
            SetOwnView(View.Mirror, snap: false);
        }

        /// <summary>Hand the camera back: GameCamera enabled in Follow mode, no clamp, no frozen look, time restored.</summary>
        public void Release()
        {
            _view = View.None;
            _pen.Clear();
            _lineBody = _wbShooter = _holdBody = null;
            _shots = null;
            _shotsDone = null;
            if (_gameCam != null)
            {
                _gameCam.ClearLookClamp();
                _gameCam.FreezeLook = false;
                _gameCam.SetMode(GameCamera.Mode.Follow);
                if (!PauseMenu.Frozen) _gameCam.enabled = true;
            }
            // Never leave a time scale behind (MenuSceneStage's rule), but never break an easing
            // slow-mo or a Solo pause either: both own the value right now.
            if (!PauseMenu.Frozen && (_gameCam == null || !_gameCam.SlowMoActive))
            {
                Time.timeScale = 1f;
                Time.fixedDeltaTime = 0.02f;
            }
        }

        // ================================================================ per frame
        void LateUpdate()
        {
            if (_cam == null) return;
            if (PauseMenu.Frozen) return;
            SyncGameCamera();
            float dt = Time.unscaledDeltaTime;
            switch (_view)
            {
                case View.Penalty: PenaltyUpdate(dt); break;
                case View.Lineup: LineupUpdate(dt); break;
                case View.CoinToss: FixedUpdateShot(dt); break;
                case View.WalkBack: WalkBackUpdate(dt); break;
                case View.Hold: HoldUpdate(dt); break;
                case View.Podium: PodiumUpdate(dt); break;
                case View.Mirror: MirrorUpdate(dt); break;
                case View.Cinematic: CinematicUpdate(dt); break;
                default: break;   // None / Delegate: GameCamera is driving
            }
            _snap = false;
        }

        void OnDestroy()
        {
            Release();
        }

        // GameCamera runs for Penalty (it owns the aim) and the delegated views; every other view
        // switches it off so it neither writes the transform nor accumulates mouse look.
        void SyncGameCamera()
        {
            if (_gameCam == null) return;
            bool want = _view == View.None || _view == View.Delegate || _view == View.Penalty;
            if (_gameCam.enabled != want) _gameCam.enabled = want;
        }

        void SetOwnView(View v, bool snap = true)
        {
            _view = v;
            _snap = snap;
            _velPos = Vector3.zero;
            if (_gameCam != null) _gameCam.FreezeLook = false;
        }

        void PenaltyUpdate(float dt)
        {
            if (_gameCam == null || !_pen.Latched) return;
            _pen.Apply(_cam, _gameCam.Yaw, _gameCam.Pitch, _snap ? 0f : dt);
        }

        void LineupUpdate(float dt)
        {
            if (_lineBody == null || _lineBody.Pelvis == null) return;
            Vector2 look = LookDelta();
            _lineYaw = Mathf.Clamp(_lineYaw + look.x * SimConfig.CamYawSpeed, -CupTuning.LineupYawLimit, CupTuning.LineupYawLimit);
            _linePitch = Mathf.Clamp(_linePitch - look.y * SimConfig.CamPitchSpeed, CupTuning.LineupPitchMin, CupTuning.LineupPitchMax);

            float yaw = _lineBaseYaw + _lineYaw;
            Vector3 flat = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 fwd = Quaternion.Euler(_linePitch, yaw, 0f) * Vector3.forward;
            Vector3 pivot = _lineBody.Pelvis.position;
            Vector3 desired = pivot - flat * LineupCamBack + Vector3.up * LineupCamHeight;
            if (desired.y < 0.8f) desired.y = 0.8f;
            Vector3 lookAt = pivot + fwd * 6f + Vector3.up * 1.0f;
            Place(desired, lookAt, Fov(LineupCamFov), 0.12f, 10f, dt);
        }

        void FixedUpdateShot(float dt)
        {
            // A static shot: no easing at all, it is a cut and a hold.
            _cam.transform.position = _fixedPos;
            Vector3 d = _fixedLook - _fixedPos;
            if (d.sqrMagnitude > 1e-6f) _cam.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            _cam.fieldOfView = Fov(_fixedFov);
        }

        void WalkBackUpdate(float dt)
        {
            _wbTime += dt;
            bool wide = _wbTime >= CupTuning.WalkBackTrackShot;
            Vector3 shooterPelvis = _wbShooter != null && _wbShooter.Pelvis != null ? _wbShooter.Pelvis.position : _wbSlot;
            if (!wide)
            {
                Transform head = _wbShooter != null ? _wbShooter.Phys(Bone.Head) : null;
                Vector3 target = head != null ? head.position : shooterPelvis + Vector3.up * 0.7f;
                _cam.transform.position = _wbLowPos;
                Turn(target, 8f, _snap ? 0f : dt);
                _cam.fieldOfView = Fov(WalkBackTrackFov);
            }
            else
            {
                bool cut = !_wbCut;
                _wbCut = true;
                Vector3 target = Vector3.Lerp(_wbSlot + Vector3.up * 1.0f, shooterPelvis + Vector3.up * 1.0f, 0.6f);
                _cam.transform.position = _wbWidePos;
                Turn(target, 6f, cut ? 0f : dt);
                _cam.fieldOfView = cut ? Fov(WalkBackWideFov) : Mathf.Lerp(_cam.fieldOfView, Fov(WalkBackWideFov), 1f - Mathf.Exp(-5f * dt));
            }
        }

        void HoldUpdate(float dt)
        {
            _cam.transform.position = _holdPos;
            if (_holdBody == null || _holdBody.Pelvis == null) return;
            Turn(_holdBody.Pelvis.position + Vector3.up * 0.9f, 8f, dt);
        }

        void PodiumUpdate(float dt)
        {
            // A drag is LMB + motion; with PodiumFreeCursorDrag the motion is read even while the
            // cursor is free (the podium's buttons need it free), never while a menu is up.
            Vector2 look = PodiumFreeCursorDrag && _input != null && !PauseMenu.Paused && _input.LeftLegHeld
                ? _input.Look
                : LookDelta();
            float scroll = ScrollDelta();
            bool dragging = _input != null && _input.LeftLegHeld && look.sqrMagnitude > 1e-6f;
            if (dragging)
            {
                _orbitAngle += look.x * SimConfig.CamYawSpeed;
                _orbitHeight = Mathf.Clamp(_orbitHeight + look.y * PodiumDragHeightRate, PodiumHeightMin, PodiumHeightMax);
                _dragTimer = CupTuning.PodiumDragTakeover;
            }
            else if (_dragTimer > 0f)
            {
                _dragTimer -= dt;
            }
            else
            {
                _orbitAngle += 360f * CupTuning.PodiumOrbitRps * dt;
            }
            if (Mathf.Abs(scroll) > SimConfig.ScrollDeadzone)
                _orbitDistTarget *= Mathf.Pow(SimConfig.ReplayCamZoomPerNotch, Mathf.Sign(scroll));
            _orbitDistTarget = Mathf.Clamp(_orbitDistTarget, CupTuning.PodiumZoomMin, CupTuning.PodiumZoomMax);
            _orbitDist = Mathf.Lerp(_orbitDist, _orbitDistTarget, 1f - Mathf.Exp(-SimConfig.ReplayCamZoomEase * dt));

            float a = _orbitAngle * Mathf.Deg2Rad;
            Vector3 desired = _orbitCentre + new Vector3(Mathf.Sin(a) * _orbitDist, _orbitHeight, Mathf.Cos(a) * _orbitDist);
            Vector3 lookAt = _orbitCentre + Vector3.up * 1.0f;
            Place(desired, lookAt, Fov(PodiumFov), dragging ? 0.05f : 0.2f, dragging ? 16f : 6f, dt);
        }

        void CinematicUpdate(float dt)
        {
            if (_shots == null) return;
            if (_shotIndex >= _shots.Count) return;   // holding the last pose; onDone already fired
            var s = _shots[_shotIndex];
            if (s == null) { AdvanceShot(); return; }
            if (!_shotStarted)
            {
                _shotStarted = true;
                _shotTime = 0f;
                _snap = s.Cut;
                _velPos = Vector3.zero;
                s.OnStart?.Invoke();
            }
            float seconds = Mathf.Max(0.01f, s.Seconds);
            float t = Mathf.Clamp01(_shotTime / seconds);
            float k = s.Ease != null ? Mathf.Clamp01(s.Ease(t)) : Mathf.SmoothStep(0f, 1f, t);
            Vector3 desired = s.PositionAt(k);
            Vector3 lookAt = s.LookAt != null ? s.LookAt() : s.LookPoint;
            float fov = Fov(s.FovEnd >= 0f ? Mathf.Lerp(s.Fov, s.FovEnd, k) : s.Fov);
            // A cut lands exactly on the shot's path; an eased entry SmoothDamps onto it and then
            // follows it (a tight time so the path, not the damping, is what the eye reads).
            Place(desired, lookAt, fov, 0.12f, 9f, dt);
            _shotTime += dt;
            if (_shotTime >= seconds) AdvanceShot();
        }

        void AdvanceShot()
        {
            _shotIndex++;
            _shotStarted = false;
            _shotTime = 0f;
            if (_shotIndex >= _shots.Count)
            {
                var cb = _shotsDone;
                _shotsDone = null;
                cb?.Invoke();
            }
        }

        void MirrorUpdate(float dt)
        {
            if (_mirFresh)
            {
                _cam.transform.position = _mirPos;
                _cam.transform.rotation = _mirRot;
                _cam.fieldOfView = _mirFov;
                _mirFresh = false;
                return;
            }
            float k = 1f - Mathf.Exp(-MirrorRate * dt);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, _mirPos, k);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, _mirRot, k);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _mirFov, k);
        }

        // ================================================================ helpers
        /// <summary>Ease the camera to a position, turn toward a point, ease the FOV; a pending snap cuts instead.</summary>
        void Place(Vector3 desired, Vector3 lookAt, float fov, float smoothTime, float turnRate, float dt)
        {
            if (_snap)
            {
                _cam.transform.position = desired;
                _velPos = Vector3.zero;
                Turn(lookAt, turnRate, 0f);
                _cam.fieldOfView = fov;
                return;
            }
            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, smoothTime, Mathf.Infinity, dt);
            Turn(lookAt, turnRate, dt);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, fov, 1f - Mathf.Exp(-5f * dt));
        }

        void Turn(Vector3 lookAt, float rate, float dt)
        {
            Vector3 d = lookAt - _cam.transform.position;
            if (d.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            _cam.transform.rotation = dt <= 0f ? want : Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>The player's FOV option applies to the fixed rig views like it does to GameCamera's (never to the solved penalty FOV).</summary>
        static float Fov(float baseFov) => Mathf.Clamp(baseFov + DisplaySettings.FovOffset, 34f, 78f);

        /// <summary>Mouse look for the rig's own views: only while the cursor is captured and no menu is up.</summary>
        Vector2 LookDelta()
        {
            if (_input == null || PauseMenu.Paused || !GameInput.CursorCaptured) return Vector2.zero;
            return _input.Look;
        }

        float ScrollDelta()
        {
            if (_input == null || PauseMenu.Paused) return 0f;
            return _input.Scroll;
        }

        Vector2 LookSource() => _input != null ? _input.Look : Vector2.zero;
        float ScrollSource() => _input != null ? _input.Scroll : 0f;
        static Quaternion KeeperFacing() => Quaternion.LookRotation(SimConfig.KeeperFaceDir, Vector3.up);
    }
}
