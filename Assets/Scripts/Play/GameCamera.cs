using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The single game camera, with two modes:
    ///
    ///  Follow (default gameplay): a third-person camera that sits behind the
    ///  character the player currently controls and turns with that character's
    ///  facing. Run into place, turn away from goal to set up the bike, and the
    ///  camera orbits with you. Movement in Striker is camera-relative, so this
    ///  is the usual stable third-person loop.
    ///
    ///  Broadcast (replays): a diagonal vantage across the penalty area that frames
    ///  everyone and tracks the ball, used for the slow-motion replay after contact.
    ///
    /// One component owns the Camera and the slow-motion timeScale so nothing fights
    /// over the transform.
    /// </summary>
    public class GameCamera : MonoBehaviour
    {
        public enum Mode { Follow, Broadcast }

        Camera _cam;
        Mode _mode = Mode.Follow;

        // follow refs
        Transform _followTarget;
        System.Func<float> _yawSource;
        float _curYaw;
        Vector3 _velPos;

        // broadcast refs
        Transform _ball, _striker, _crosser, _goal;
        float _slowmoTimer;

        // follow tuning
        const float FollowDist = 5.8f;
        const float FollowHeight = 2.9f;
        const float LookHeight = 1.25f;

        public void Init(Camera cam, Transform ball, Transform striker, Transform crosser, Transform goal)
        {
            _cam = cam;
            _ball = ball;
            _striker = striker;
            _crosser = crosser;
            _goal = goal;
            _followTarget = striker;
            _curYaw = 0f;
        }

        public void SetFollow(Transform target, System.Func<float> yawSource)
        {
            _followTarget = target;
            _yawSource = yawSource;
            _mode = Mode.Follow;
            if (yawSource != null) _curYaw = yawSource();
        }

        public void SetMode(Mode m) => _mode = m;
        public void TriggerSlowMo(float seconds) => _slowmoTimer = Mathf.Max(_slowmoTimer, seconds);
        public bool SlowMoActive => _slowmoTimer > 0f;

        // Ball-aware follow: stays behind you (body turn still orbits), but aims at a
        // point biased toward the ball and widens FOV as the ball separates, so you
        // can read the incoming cross's depth and height. Toggled with V.
        bool _ballCam;
        public void ToggleBallCam() => _ballCam = !_ballCam;
        public bool BallCam => _ballCam;

        void LateUpdate()
        {
            if (_cam == null) return;
            UpdateSlowMo();

            if (_mode == Mode.Follow) FollowUpdate();
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

            float wantYaw = _yawSource != null ? _yawSource() : _curYaw;
            _curYaw = Mathf.LerpAngle(_curYaw, wantYaw, 1f - Mathf.Exp(-7f * dt));

            Vector3 fwd = new Vector3(Mathf.Sin(_curYaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_curYaw * Mathf.Deg2Rad));
            Vector3 pivot = _followTarget.position;
            Vector3 desired = pivot - fwd * FollowDist + Vector3.up * FollowHeight;
            if (desired.y < 1.1f) desired.y = 1.1f;

            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.14f, Mathf.Infinity, dt);

            Vector3 lookAt = pivot + Vector3.up * LookHeight;
            float targetFov = 55f;
            if (_ballCam && _ball != null)
            {
                // Bias the aim toward the ball and open up FOV with separation so both
                // the striker and the incoming ball stay in frame.
                float sep = Vector3.Distance(_ball.position, pivot);
                float w = Mathf.Clamp01(sep / 16f);                 // 0 when ball is on you, 1 far away
                lookAt = Vector3.Lerp(pivot + Vector3.up * LookHeight, _ball.position, 0.35f + 0.3f * w);
                targetFov = Mathf.Lerp(55f, 74f, w);
            }
            Quaternion want = Quaternion.LookRotation((lookAt - _cam.transform.position).normalized, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-10f * dt));
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, targetFov, 1f - Mathf.Exp(-5f * dt));
        }

        void BroadcastUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 ballPos = _ball != null ? _ball.position : SimConfig.GoalCenter;
            Vector3 strikerPos = _striker != null ? _striker.position : SimConfig.StrikerStart;

            Vector3 focus = Vector3.Lerp(GroupCenter(), ballPos, 0.5f);
            float spread = Vector3.Distance(ballPos, strikerPos);
            float dist = Mathf.Clamp(12f + spread * 0.6f, 14f, 30f);
            float height = Mathf.Clamp(9f + spread * 0.35f, 9f, 18f);

            Vector3 dir = new Vector3(0.85f, 0f, -0.5f).normalized;
            Vector3 desired = focus + new Vector3(dir.x * dist, height, dir.z * dist);
            _cam.transform.position = Vector3.SmoothDamp(_cam.transform.position, desired, ref _velPos, 0.35f, Mathf.Infinity, dt);

            Vector3 lookAt = Vector3.Lerp(focus, ballPos, 0.55f) + Vector3.up * 1.2f;
            Quaternion want = Quaternion.LookRotation((lookAt - _cam.transform.position).normalized, Vector3.up);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, want, 1f - Mathf.Exp(-6f * dt));
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, 46f, 1f - Mathf.Exp(-5f * dt));
        }

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
