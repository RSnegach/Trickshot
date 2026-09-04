using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The single game camera, with two modes:
    ///
    ///  Follow (default gameplay): an orbit camera around the striker driven by MOUSE
    ///  MOVEMENT only, fully decoupled from WASD. Moving the mouse pans yaw/pitch
    ///  around the striker. Toggle ball-lock (V) and the yaw instead swings to keep
    ///  the ball framed behind the striker, for reading the incoming cross.
    ///  Follow has three VANTAGES on that one orbit, cycled with T (see <see cref="View"/>):
    ///  Third (behind), First (the body's own eyes) and Front (in front, looking back at
    ///  him). They change only where the lens sits: the orbit yaw stays the body's facing
    ///  and the aim source, so steering, shooting and the yaw that goes on the wire are
    ///  identical in all three and switching can never desync a networked player.
    ///
    ///  Broadcast (replays): a diagonal vantage across the penalty area that frames
    ///  everyone and tracks the ball, used for the slow-motion replay after contact.
    ///  The viewer can take it over: the mouse orbits the focus and the wheel zooms.
    ///
    /// One component owns the Camera and the slow-motion timeScale so nothing fights
    /// over the transform.
    /// </summary>
    public class GameCamera : MonoBehaviour
    {
        public enum Mode { Follow, Broadcast, KeeperFollow }

        Camera _cam;
        Mode _mode = Mode.Follow;

        Transform _followTarget;
        System.Func<Vector2> _lookSource;   // mouse delta provider
        System.Func<float> _scrollSource;   // mouse wheel provider (replay zoom); optional
        System.Func<Quaternion> _facingSource;  // keeper facing provider
        // Broadcast (replay) orbit, owned by the viewer once they touch the mouse or wheel.
        float _bcYaw, _bcPitch, _bcDist, _bcDistTarget;
        bool _bcUser;                       // false: the automatic vantage is still driving
        // While an overlay owns the cursor (e.g. the cross-targeting map), freeze camera LOOK so
        // moving the mouse to click the map doesn't spin the view. Position smoothing still settles.
        public bool FreezeLook;
        float _yaw, _pitch = 22f;
        float _ballViewYaw;    // ball-cam camera yaw (frames the ball), separate from _yaw
        Vector3 _velPos;

        // Optional LOOK CLAMP for the cup's penalty camera (CupCameraRig / CupPenaltyCam): the mouse
        // yaw is held within +-_clampYawRange of _clampYawCenter and the pitch within
        // [_clampPitchMin, _clampPitchMax] INSTEAD of the follow camera's own pitch limits (the
        // penalty aim needs to look further UP than CamPitchMin allows, to reach the bar). Only
        // the yaw/pitch accumulation is clamped - this camera's yaw and pitch stay the aim source
        // (SetPieceTaker.LookAimPoint) while the rig places the camera itself. Off by default and
        // nothing outside the cup sets it, so every other mode is untouched.
        bool _clampLook;
        float _clampYawCenter, _clampYawRange, _clampPitchMin, _clampPitchMax;

        /// <summary>Clamp the follow look: yaw within +-yawRange of yawCenter, pitch within [pitchMin, pitchMax] (deg, +pitch = down).</summary>
        public void SetLookClamp(float yawCenter, float yawRange, float pitchMin, float pitchMax)
        {
            _clampLook = true;
            _clampYawCenter = yawCenter;
            _clampYawRange = Mathf.Max(0f, yawRange);
            _clampPitchMin = Mathf.Min(pitchMin, pitchMax);
            _clampPitchMax = Mathf.Max(pitchMin, pitchMax);
            _yaw = _clampYawCenter + Mathf.Clamp(Mathf.DeltaAngle(_clampYawCenter, _yaw), -_clampYawRange, _clampYawRange);
            _pitch = Mathf.Clamp(_pitch, _clampPitchMin, _clampPitchMax);
            _ballViewYaw = _yaw;
        }

        public void ClearLookClamp() => _clampLook = false;

        /// <summary>Set the follow look outright (deg, +pitch = down): a cut to face something, e.g. the goal at the start of a kick.</summary>
        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _ballViewYaw = yaw;
            _pitch = _clampLook ? Mathf.Clamp(pitch, _clampPitchMin, _clampPitchMax)
                                : Mathf.Clamp(pitch, SimConfig.CamPitchMin, SimConfig.CamPitchMax);
        }

        Transform _ball, _striker, _crosser, _goal;
        float _slowmoTimer;
        bool _ballCam;

        // Auto ball-cam pulse (fired on a shot): forces ball-cam for a few seconds, then
        // restores whatever the manual V toggle was set to.
        float _shotCamTimer;
        bool _shotCamPrevBallCam;

        public void Init(Camera cam, Transform ball, Transform striker, Transform crosser, Transform goal)
        {
            _cam = cam;
            _ball = ball;
            _striker = striker;
            _crosser = crosser;
            _goal = goal;
            _followTarget = striker;
        }

        /// <summary>Set the orbit target and the mouse-delta source for camera control. The
        /// optional wheel source is what zooms a replay (Broadcast) - modes without one keep a
        /// fixed-distance replay.</summary>
        public void SetFollow(Transform target, System.Func<Vector2> lookSource, System.Func<float> scrollSource = null,
                              System.Func<bool> viewToggleSource = null)
        {
            _followTarget = target;
            _lookSource = lookSource;
            if (scrollSource != null) _scrollSource = scrollSource;
            if (viewToggleSource != null) _viewToggleSource = viewToggleSource;
            _mode = Mode.Follow;
        }

        /// <summary>Keeper camera: sits behind the keeper looking in his facing
        /// direction (out toward the pitch), with a slight clamped mouse look.</summary>
        public void SetKeeperFollow(Transform target, System.Func<Quaternion> facingSource, System.Func<Vector2> lookSource,
                                    System.Func<float> scrollSource = null, System.Func<bool> viewToggleSource = null)
        {
            _followTarget = target;
            _facingSource = facingSource;
            _lookSource = lookSource;
            if (scrollSource != null) _scrollSource = scrollSource;
            if (viewToggleSource != null) _viewToggleSource = viewToggleSource;
            _keeperLookYaw = 0f;
            _keeperLookPitch = 0f;
            _keeperFrontYield = false;
            _keeperBallDist = float.MaxValue;
            _mode = Mode.KeeperFollow;
        }

        float _keeperLookYaw, _keeperLookPitch;
        bool _keeperFrontYield;   // the keeper's Front view is standing aside for a live shot

        /// <summary>How far out a ball still counts as "near" the keeper, so a yielded Front view holds (m).</summary>
        public const float KeeperFrontHoldRange = 22f;
        /// <summary>Inside this the ball is committed at him and Front must already have yielded (m).</summary>
        public const float KeeperFrontYieldRange = 16f;

        /// <summary>
        /// A shot is genuinely coming at the keeper: the ball is in front of him (the side he
        /// faces), within <see cref="KeeperFrontYieldRange"/>, and closing. Position-based rather
        /// than velocity-based because this camera is handed a Transform, not the BallController -
        /// and closing is measured from the previous frame's distance, so a ball sitting on a
        /// penalty spot never trips it.
        /// </summary>
        bool KeeperShotInbound(Vector3 pivot, Vector3 fwd)
        {
            if (_ball == null) return false;
            Vector3 toBall = _ball.position - pivot;
            float ahead = Vector3.Dot(toBall, fwd);          // + = on the side he faces
            float dist = toBall.magnitude;
            bool closing = dist < _keeperBallDist - 0.02f;   // moved at least 2 cm nearer this frame
            _keeperBallDist = dist;
            return ahead > 0f && dist < KeeperFrontYieldRange && closing;
        }

        /// <summary>The ball is still around the keeper, so a yielded Front view keeps holding.</summary>
        bool KeeperBallNear(Vector3 pivot, Vector3 fwd)
        {
            if (_ball == null) return false;
            Vector3 toBall = _ball.position - pivot;
            return Vector3.Dot(toBall, fwd) > -2f && toBall.magnitude < KeeperFrontHoldRange;
        }

        float _keeperBallDist = float.MaxValue;

        /// <summary>Keeper camera yaw within its cone (deg). The keeper reads this and
        /// turns his body to it, so the body and the camera stay in lock-step.</summary>
        public float KeeperLookYaw => _keeperLookYaw;

        public void SetMode(Mode m)
        {
            // A fresh replay starts on the automatic vantage; the viewer takes over from there.
            if (m == Mode.Broadcast && _mode != Mode.Broadcast) _bcUser = false;
            _mode = m;
        }
        public void TriggerSlowMo(float seconds) => _slowmoTimer = Mathf.Max(_slowmoTimer, seconds);
        public bool SlowMoActive => _slowmoTimer > 0f;

        /// <summary>Current camera yaw (deg). The striker uses this as its look/turn
        /// direction so movement is camera-relative, Minecraft third-person style.</summary>
        public float Yaw => _yaw;

        public float Pitch => _pitch;
        public Vector3 LookDirection() => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        // How far DOWN the keeper camera is angled within its allowed pitch range:
        // 0 = fully up, 1 = fully down (lowest angle). 0 when not in keeper mode so the
        // net always renders in other views.
        public float KeeperLookDownFraction
        {
            get
            {
                if (_mode != Mode.KeeperFollow) return 0f;
                float lim = SimConfig.KeeperCamLookPitch;
                // _keeperLookPitch > 0 tilts the view down (Unity +X euler), so +lim = lowest.
                return Mathf.InverseLerp(-lim, lim, _keeperLookPitch);
            }
        }

        public void ToggleBallCam()
        {
            // A manual toggle cancels an active shot-cam pulse and takes over from here.
            _shotCamTimer = 0f;
            _ballCam = !_ballCam;
        }
        public bool BallCam => _ballCam;

        /// <summary>
        /// Which vantage the FOLLOW camera uses. All three share one orbit (the same yaw / pitch
        /// and therefore the same steering and aim), so switching never changes how the player
        /// controls anything - only where the lens sits.
        ///
        ///  Third  - the default: behind the body at SimConfig.CamDistance, looking at it.
        ///  First  - the body's own eyes: at head height on the orbit yaw, looking out along it.
        ///  Front  - Third swung 180 degrees round the body, looking BACK at its face.
        /// </summary>
        public enum View { Third = 0, First = 1, Front = 2 }

        View _view = View.Third;

        /// <summary>The current follow vantage (Third by default).</summary>
        public View FollowView => _view;

        /// <summary>Third -> First -> Front -> Third. Bound to CamView (T) by every mode that has a camera.</summary>
        public void CycleView() => SetView(_view == View.Third ? View.First : _view == View.First ? View.Front : View.Third);

        /// <summary>
        /// Set the follow vantage outright. Kills the position smoothing for one frame so the
        /// change reads as a CUT rather than the camera flying through the body to its new side -
        /// a 180 degree swing at SmoothDamp's 0.08 s would sweep the lens across his face.
        /// </summary>
        public void SetView(View v)
        {
            if (_view == v) return;
            _view = v;
            _viewCut = true;
        }

        bool _viewCut;   // the next FollowUpdate snaps instead of smoothing (a view change is a cut)

        /// <summary>
        /// Where the follow camera's eye sits for the current view, and what it looks at.
        /// `pivot` is the body's origin (its feet) and `rot` the shared orbit rotation.
        /// </summary>
        void ViewPose(Vector3 pivot, Quaternion rot, Vector3 lookTarget, out Vector3 eye, out Vector3 aim)
        {
            switch (_view)
            {
                case View.First:
                    // On the orbit yaw at eye height, looking the way the orbit faces. The small
                    // forward offset clears the head geometry so the body's own hair/hat does not
                    // fill the lens; FirstEyeHeight is measured from the pivot (the feet).
                    eye = pivot + Vector3.up * EyeHeight() + rot * new Vector3(0f, 0f, FirstEyeForward);
                    aim = eye + rot * Vector3.forward * 10f;
                    break;
                case View.Front:
                    // Third, swung half a turn: the lens stands in FRONT of the body looking back
                    // at it. Same distance and height, so the framing matches the default view.
                    eye = pivot + Vector3.up * SimConfig.CamLookHeight
                          + rot * new Vector3(0f, 0f, SimConfig.CamDistance);
                    aim = lookTarget;
                    break;
                default:
                    eye = pivot + Vector3.up * SimConfig.CamLookHeight
                          + rot * new Vector3(0f, 0f, -SimConfig.CamDistance);
                    aim = lookTarget;
                    break;
            }
        }

        /// <summary>
        /// First-person eye height above the follow target's origin (m). The default suits a
        /// standing human (BodyLayout puts Bone.Head at 1.72 m; the eye sits a little under it),
        /// and <see cref="SetEyeHeightSource"/> overrides it for a scaled or non-human body.
        /// </summary>
        public const float FirstEyeHeight = 1.6f;
        /// <summary>
        /// First-person eye offset along the view (m). It must clear the head's own collider -
        /// the human head sphere is 0.19 m - and the camera's 0.3 m near plane, or the player
        /// sees the inside of his own skull.
        /// </summary>
        public const float FirstEyeForward = 0.42f;

        System.Func<float> _eyeHeightSource;
        System.Func<bool> _viewToggleSource;

        /// <summary>
        /// Supply the "cycle the view" edge (GameInput.CamViewPressed). Set once when a mode wires
        /// its camera and the camera polls it in Follow mode, so a mode does not have to repeat the
        /// key handling in its own update - and a mode that never sets it keeps the old behaviour.
        /// Pass a source that is already gated on whatever should suppress input (a pause menu, a
        /// modal), exactly as the look source is.
        /// </summary>
        public void SetViewToggleSource(System.Func<bool> src) => _viewToggleSource = src;

        /// <summary>
        /// Supply the first-person eye height for the current follow target (metres above its
        /// origin). A mode with a live ragdoll should pass its head bone's height so a tall, short
        /// or non-human body looks out of its own eyes; without it the human default is used.
        /// </summary>
        public void SetEyeHeightSource(System.Func<float> src) => _eyeHeightSource = src;

        float EyeHeight()
        {
            if (_eyeHeightSource == null) return FirstEyeHeight;
            float h = _eyeHeightSource();
            return h > 0.2f ? h : FirstEyeHeight;   // a collapsed / mid-rebuild body reports junk
        }

        /// <summary>Fire the auto ball-cam pulse: cut to ball-cam for `seconds`, then
        /// revert to whatever the manual toggle was. Called on a genuine shot. Only acts
        /// in Follow mode (keeper/broadcast views ignore it).</summary>
        public void PulseBallCam(float seconds)
        {
            if (_mode != Mode.Follow || seconds <= 0f) return;
            if (_shotCamTimer <= 0f) _shotCamPrevBallCam = _ballCam;   // remember only on the FIRST pulse
            _shotCamTimer = Mathf.Max(_shotCamTimer, seconds);
            _ballCam = true;
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            // While FROZEN, do nothing: otherwise UpdateSlowMo re-asserts Time.timeScale
            // back toward 1 every frame and defeats the pause freeze. An OVERLAY pause (the
            // multiplayer cup) freezes nothing, so the camera keeps running under the menu.
            if (PauseMenu.Frozen) return;
            UpdateSlowMo();

            // Auto ball-cam pulse countdown (real time, so slow-mo doesn't stretch it).
            if (_shotCamTimer > 0f)
            {
                _shotCamTimer -= Time.unscaledDeltaTime;
                if (_shotCamTimer <= 0f) _ballCam = _shotCamPrevBallCam;   // revert to the manual state
            }

            if (_mode == Mode.Follow) FollowUpdate();
            else if (_mode == Mode.KeeperFollow) KeeperFollowUpdate();
            else BroadcastUpdate();
        }

        void UpdateSlowMo()
        {
            float target = _slowmoTimer > 0f ? 0.28f : 1f;
            if (_slowmoTimer > 0f) _slowmoTimer -= Time.unscaledDeltaTime;
            float k = _slowmoTimer > 0f ? 12f : 8f;
            Time.timeScale = Mathf.Lerp(Time.timeScale, target, 1f - Mathf.Exp(-k * Time.unscaledDeltaTime));
            if (Time.timeScale > 0.999f) Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }

        void FollowUpdate()
        {
            if (_followTarget == null) return;
            float dt = Time.unscaledDeltaTime;
            Vector3 pivot = _followTarget.position;

            // The view cycle, polled here so every mode that wires a source gets it without
            // repeating the key handling. FreezeLook means an overlay owns the cursor.
            if (_viewToggleSource != null && !FreezeLook && _viewToggleSource()) CycleView();

            Vector2 look = (_lookSource != null && !FreezeLook) ? _lookSource() : Vector2.zero;
            float pitchMin = _clampLook ? _clampPitchMin : SimConfig.CamPitchMin;
            float pitchMax = _clampLook ? _clampPitchMax : SimConfig.CamPitchMax;
            _pitch = Mathf.Clamp(_pitch - look.y * SimConfig.CamPitchSpeed, pitchMin, pitchMax);

            float viewYaw;
            if (_ballCam && _ball != null)
            {
                // Ball cam OWNS the view. The camera orbits to frame the ball, and the
                // mouse yaw is IGNORED - _yaw (the striker's facing) is frozen so the
                // player can't blind-spin the striker while the camera is ball-locked.
                // Only mouse pitch is honoured. Toggle V off to steer again.
                Vector3 toBall = _ball.position - pivot; toBall.y = 0f;
                if (toBall.sqrMagnitude > 0.01f)
                {
                    float ballYaw = Mathf.Atan2(toBall.x, toBall.z) * Mathf.Rad2Deg;
                    _ballViewYaw = Mathf.LerpAngle(_ballViewYaw, ballYaw, 1f - Mathf.Exp(-6f * dt));
                }
                viewYaw = _ballViewYaw;
            }
            else
            {
                // Normal follow: the mouse drives _yaw (striker facing + camera).
                _yaw += look.x * SimConfig.CamYawSpeed;
                if (_clampLook)
                    _yaw = _clampYawCenter + Mathf.Clamp(Mathf.DeltaAngle(_clampYawCenter, _yaw), -_clampYawRange, _clampYawRange);
                viewYaw = _yaw;
                _ballViewYaw = _yaw;   // keep aligned so toggling into ball cam doesn't snap
            }

            Quaternion rot = Quaternion.Euler(_pitch, viewYaw, 0f);

            Vector3 lookAt = pivot + Vector3.up * SimConfig.CamLookHeight;
            if (_ballCam && _ball != null)
                lookAt = Vector3.Lerp(lookAt, _ball.position, 0.35f);

            Vector3 desired, aim;
            ViewPose(pivot, rot, lookAt, out desired, out aim);
            // First person is already at head height; only the outside views need the floor guard
            // (a low pitch swings them under the turf).
            if (_view != View.First && desired.y < 0.6f) desired.y = 0.6f;

            if (_viewCut)
            {
                // A view change is a CUT: snap and clear the smoothing velocity, or the lens sweeps
                // through the body on its way round.
                _viewCut = false;
                _velPos = Vector3.zero;
                _cam.transform.position = desired;
                _cam.transform.rotation = Quaternion.LookRotation((aim - desired).normalized, Vector3.up);
            }
            else
            {
                _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.08f, Mathf.Infinity, dt);
            }

            Vector3 toAim = aim - _cam.transform.position;
            if (toAim.sqrMagnitude > 1e-6f)
            {
                Quaternion want = Quaternion.LookRotation(toAim.normalized, Vector3.up);
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-14f * dt));
            }
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, Fov(58f), 1f - Mathf.Exp(-5f * dt));
        }

        // Keeper cam: behind the keeper along his facing, with a slight clamped mouse
        // look so you can glance side to side / up and down without leaving the view.
        void KeeperFollowUpdate()
        {
            if (_followTarget == null) return;
            float dt = Time.unscaledDeltaTime;

            // Accumulate a clamped mouse look (yaw within a cone, plus pitch). The keeper
            // BODY reads this same yaw and turns to it, and this camera pivots around a
            // FIXED forward base (facingSource), so the camera ends up directly behind
            // the turned body without the pan and the body turn compounding.
            Vector2 look = (_lookSource != null && !FreezeLook) ? _lookSource() : Vector2.zero;
            _keeperLookYaw = Mathf.Clamp(_keeperLookYaw + look.x * SimConfig.KeeperCamLookSpeed,
                                         -SimConfig.KeeperLookYawLimit, SimConfig.KeeperLookYawLimit);
            _keeperLookPitch = Mathf.Clamp(_keeperLookPitch - look.y * SimConfig.KeeperCamLookSpeed,
                                           -SimConfig.KeeperCamLookPitch, SimConfig.KeeperCamLookPitch);

            if (_viewToggleSource != null && !FreezeLook && _viewToggleSource()) CycleView();

            Quaternion facing = _facingSource != null ? _facingSource() : Quaternion.identity;
            // Apply the look offset around the fixed forward base.
            Quaternion viewRot = facing * Quaternion.Euler(_keeperLookPitch, _keeperLookYaw, 0f);
            Vector3 fwd = viewRot * Vector3.forward;
            Vector3 pivot = _followTarget.position;

            // FRONT is a spectating vantage for a keeper: it stands on the SHOOTER's side, so a
            // ball in its last few metres would be behind the lens - sight of the ball lost exactly
            // when the save is made. It yields to Third the moment a shot is genuinely inbound and
            // stays yielded until the ball is dead or back upfield, so the view can never cost a
            // goal; the player's chosen view is remembered and returns on its own.
            View view = _view;
            if (view == View.Front)
            {
                if (KeeperShotInbound(pivot, fwd)) _keeperFrontYield = true;
                else if (!KeeperBallNear(pivot, fwd)) _keeperFrontYield = false;
                if (_keeperFrontYield) view = View.Third;
            }
            else _keeperFrontYield = false;

            // The 3.0m height was sized against the base 2.44m goal. Set Pieces lets the host scale
            // the goal up to 1.5x (GameBootstrap.cs, cfg.goalScale), and this camera is one of that
            // mode's own - a fixed 3.0m no longer clears a 3.66m goal's own back frame/net at that
            // size, putting the camera below the crossbar looking up into the net structure from
            // behind. Scale with it so the camera always sits proportionally above whatever goal is
            // actually built; at the base 2.44m height this is exactly the old fixed 3.0m.
            float heightScale = SimConfig.GoalHeight / 2.44f;

            Vector3 desired, lookAt;
            switch (view)
            {
                case View.First:
                    // The keeper's own eyes, looking out along his look cone at the shot. No
                    // goal-height scaling: this is his head, not a vantage above the bar.
                    desired = pivot + Vector3.up * EyeHeight() + fwd * FirstEyeForward;
                    lookAt = desired + fwd * 10f;
                    break;
                case View.Front:
                    // Mirrored to the shooter's side, looking BACK at him. Same distance and
                    // height as the default, so the framing matches.
                    desired = pivot + fwd * 5.5f + Vector3.up * (3.0f * heightScale);
                    lookAt = pivot + Vector3.up * 0.9f;
                    break;
                default:
                    desired = pivot - fwd * 5.5f + Vector3.up * (3.0f * heightScale);
                    lookAt = pivot + fwd * 4f + Vector3.up * 0.9f;
                    break;
            }
            if (view != View.First && desired.y < 0.8f) desired.y = 0.8f;

            if (_viewCut)
            {
                // A view change is a cut: snap, or the lens sweeps through the keeper.
                _viewCut = false;
                _velPos = Vector3.zero;
                _cam.transform.position = desired;
                _cam.transform.rotation = Quaternion.LookRotation((lookAt - desired).normalized, Vector3.up);
            }
            else
            {
                _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.18f, Mathf.Infinity, dt);
            }

            Vector3 toLook = lookAt - _cam.transform.position;
            if (toLook.sqrMagnitude > 1e-6f)
            {
                Quaternion want = Quaternion.LookRotation(toLook.normalized, Vector3.up);
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-8f * dt));
            }
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, Fov(60f), 1f - Mathf.Exp(-5f * dt));
        }

        // Half of the 68m regulation width every current Broadcast caller (GameManager,
        // NetSetPieceMatch, NetStrikerMatch) always builds at - the dist/height clamps below were
        // tuned against that. Not reachable at any other width today: Broadcast is never wired into
        // Match, the one mode with a different pitch size. Scaling the clamps with the pitch
        // anyway costs nothing (this is 1.0 for every caller that exists right now) and means a
        // future Match broadcast camera doesn't inherit numbers tuned for a wider pitch.
        const float BroadcastRegulationHalfWidth = 34f;

        // The replay camera is an ORBIT about the auto-framed focus, expressed as yaw / pitch /
        // distance. Untouched, those three are re-derived from the automatic vantage every frame,
        // so a replay nobody touches looks exactly as it always did. The first mouse move or wheel
        // notch hands them to the viewer - from wherever the camera already is, so there is no snap
        // in either direction - and from then on the mouse orbits and the wheel zooms while the
        // focus keeps tracking the action. Every machine runs this against its own mouse, so
        // everyone watching a networked replay looks around it independently.
        void BroadcastUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 ballPos = _ball != null ? _ball.position : SimConfig.GoalCenter;
            Vector3 strikerPos = _striker != null ? _striker.position : SimConfig.StrikerStart;
            float pitchScale = PitchLayout.HalfWidth / BroadcastRegulationHalfWidth;

            Vector3 focus = Vector3.Lerp(GroupCenter(), ballPos, 0.5f);
            float spread = Vector3.Distance(ballPos, strikerPos);

            Vector2 look = (_lookSource != null && !FreezeLook) ? _lookSource() : Vector2.zero;
            float scroll = (_scrollSource != null && !FreezeLook) ? _scrollSource() : 0f;
            bool wheel = Mathf.Abs(scroll) > SimConfig.ScrollDeadzone;
            bool input = look.sqrMagnitude > 1e-6f || wheel;

            if (!_bcUser)
            {
                // The automatic vantage, as an orbit. The camera sits at focus + rot * (0,0,-dist),
                // so the offset's unit vector o gives pitch = asin(o.y) and yaw = atan2(-o.x, -o.z).
                float dist = Mathf.Clamp(12f + spread * 0.6f, 14f, 30f) * pitchScale;
                float height = Mathf.Clamp(9f + spread * 0.35f, 9f, 18f) * pitchScale;
                Vector3 dir = new Vector3(0.85f, 0f, -0.5f).normalized;
                Vector3 off = new Vector3(dir.x * dist, height, dir.z * dist);
                _bcDist = _bcDistTarget = off.magnitude;
                Vector3 o = off / _bcDist;
                _bcYaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
                _bcPitch = Mathf.Asin(Mathf.Clamp(o.y, -1f, 1f)) * Mathf.Rad2Deg;
                if (input) _bcUser = true;
            }
            if (_bcUser)
            {
                // Same feel as the follow camera: same yaw/pitch speeds, pitch clamped so it can
                // neither dip under the turf nor flip over the top.
                _bcYaw += look.x * SimConfig.CamYawSpeed;
                _bcPitch = Mathf.Clamp(_bcPitch - look.y * SimConfig.CamPitchSpeed,
                                       SimConfig.ReplayCamPitchMin, SimConfig.ReplayCamPitchMax);
                // Zoom is per NOTCH by sign, not by the raw value (Windows reports ~120 a notch,
                // other platforms 1), and eased so a flick of the wheel glides rather than steps.
                if (wheel)
                    _bcDistTarget *= Mathf.Pow(SimConfig.ReplayCamZoomPerNotch, Mathf.Sign(scroll));
                _bcDistTarget = Mathf.Clamp(_bcDistTarget, SimConfig.ReplayCamDistMin * pitchScale,
                                            SimConfig.ReplayCamDistMax * pitchScale);
                _bcDist = Mathf.Lerp(_bcDist, _bcDistTarget, 1f - Mathf.Exp(-SimConfig.ReplayCamZoomEase * dt));
            }

            Quaternion rot = Quaternion.Euler(_bcPitch, _bcYaw, 0f);
            Vector3 desired = focus + rot * new Vector3(0f, 0f, -_bcDist);
            if (desired.y < 0.6f) desired.y = 0.6f;
            // Under the viewer's hand the glide has to be short or the orbit lags the mouse; the
            // untouched vantage keeps its slow broadcast drift.
            float glide = _bcUser ? 0.08f : 0.35f;
            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, glide, Mathf.Infinity, dt);

            Vector3 lookAt = Vector3.Lerp(focus, ballPos, 0.55f) + Vector3.up * 1.2f;
            Quaternion want = Quaternion.LookRotation((lookAt - _cam.transform.position).normalized, Vector3.up);
            float turn = _bcUser ? 16f : 6f;
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-turn * dt));
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, Fov(46f), 1f - Mathf.Exp(-5f * dt));
        }

        // Each camera mode has its own tuned FOV; the player's Field of View option (Camera tab
        // in Settings) shifts all of them by the same amount so the relative feel is preserved.
        static float Fov(float baseFov) => Mathf.Clamp(baseFov + DisplaySettings.FovOffset, 34f, 78f);

        Vector3 GroupCenter()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            if (_ball != null)    { sum += _ball.position; n++; }
            if (_striker != null) { sum += _striker.position; n++; }
            if (_crosser != null) { sum += _crosser.position; n++; }
            if (_goal != null)    { sum += _goal.position; n++; }
            return n > 0 ? sum / n : SimConfig.GoalCenter;
        }

        void OnDisable()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = 0.02f;
        }
    }
}
