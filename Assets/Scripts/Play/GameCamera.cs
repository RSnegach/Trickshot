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
        public void SetFollow(Transform target, System.Func<Vector2> lookSource, System.Func<float> scrollSource = null)
        {
            _followTarget = target;
            _lookSource = lookSource;
            if (scrollSource != null) _scrollSource = scrollSource;
            _mode = Mode.Follow;
        }

        /// <summary>Keeper camera: sits behind the keeper looking in his facing
        /// direction (out toward the pitch), with a slight clamped mouse look.</summary>
        public void SetKeeperFollow(Transform target, System.Func<Quaternion> facingSource, System.Func<Vector2> lookSource,
                                    System.Func<float> scrollSource = null)
        {
            _followTarget = target;
            _facingSource = facingSource;
            _lookSource = lookSource;
            if (scrollSource != null) _scrollSource = scrollSource;
            _keeperLookYaw = 0f;
            _keeperLookPitch = 0f;
            _mode = Mode.KeeperFollow;
        }

        float _keeperLookYaw, _keeperLookPitch;

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
            // While paused, do nothing: otherwise UpdateSlowMo re-asserts Time.timeScale
            // back toward 1 every frame and defeats the pause freeze.
            if (PauseMenu.Paused) return;
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

            Vector2 look = (_lookSource != null && !FreezeLook) ? _lookSource() : Vector2.zero;
            _pitch = Mathf.Clamp(_pitch - look.y * SimConfig.CamPitchSpeed, SimConfig.CamPitchMin, SimConfig.CamPitchMax);

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
                viewYaw = _yaw;
                _ballViewYaw = _yaw;   // keep aligned so toggling into ball cam doesn't snap
            }

            Quaternion rot = Quaternion.Euler(_pitch, viewYaw, 0f);
            Vector3 offset = rot * new Vector3(0f, 0f, -SimConfig.CamDistance);
            Vector3 desired = pivot + Vector3.up * SimConfig.CamLookHeight + offset;
            if (desired.y < 0.6f) desired.y = 0.6f;

            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.08f, Mathf.Infinity, dt);

            Vector3 lookAt = pivot + Vector3.up * SimConfig.CamLookHeight;
            if (_ballCam && _ball != null)
                lookAt = Vector3.Lerp(lookAt, _ball.position, 0.35f);
            Quaternion want = Quaternion.LookRotation((lookAt - _cam.transform.position).normalized, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-14f * dt));
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

            Quaternion facing = _facingSource != null ? _facingSource() : Quaternion.identity;
            // Apply the look offset around the fixed forward base.
            Quaternion viewRot = facing * Quaternion.Euler(_keeperLookPitch, _keeperLookYaw, 0f);
            Vector3 fwd = viewRot * Vector3.forward;
            Vector3 pivot = _followTarget.position;

            // The 3.0m height was sized against the base 2.44m goal. Set Pieces lets the host scale
            // the goal up to 1.5x (GameBootstrap.cs, cfg.goalScale), and this camera is one of that
            // mode's own - a fixed 3.0m no longer clears a 3.66m goal's own back frame/net at that
            // size, putting the camera below the crossbar looking up into the net structure from
            // behind. Scale with it so the camera always sits proportionally above whatever goal is
            // actually built; at the base 2.44m height this is exactly the old fixed 3.0m.
            float heightScale = SimConfig.GoalHeight / 2.44f;
            Vector3 desired = pivot - fwd * 5.5f + Vector3.up * (3.0f * heightScale);
            if (desired.y < 0.8f) desired.y = 0.8f;
            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.18f, Mathf.Infinity, dt);

            Vector3 lookAt = pivot + fwd * 4f + Vector3.up * 0.9f;
            Quaternion want = Quaternion.LookRotation((lookAt - _cam.transform.position).normalized, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-8f * dt));
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
        // in Options) shifts all of them by the same amount so the relative feel is preserved.
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
