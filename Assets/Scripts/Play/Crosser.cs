using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Auto-server. The player no longer crosses; this capsule on the wing serves a
    /// ball to a random spot in the box on a timer. It telegraphs the landing point
    /// with the reticle a moment before launch, then fires a projectile-solved cross
    /// (loft or drive chosen at random, optional curl). GameManager pumps Tick() and
    /// reads WantsServe()/DidServe().
    /// </summary>
    public class Crosser : MonoBehaviour
    {
        AimReticle _reticle;
        BallController _ball;
        Transform _launchPoint;

        float _timer;
        Vector3 _pendingTarget;
        float _pendingTime;
        Vector3 _pendingCurl;
        float _pendingSpin;
        bool _telegraphed;

        public bool JustServed { get; private set; }

        public void Init(AimReticle reticle, BallController ball, Transform launchPoint)
        {
            _reticle = reticle;
            _ball = ball;
            _launchPoint = launchPoint;
        }

        public void Arm(float firstDelay)
        {
            _timer = firstDelay;
            _telegraphed = false;
            JustServed = false;
            _reticle.Hide();
            _ball.ResetTo(_launchPoint.position);
        }

        /// <summary>Advance the serve timer. Returns true on the frame a ball launches.</summary>
        public bool Tick()
        {
            JustServed = false;

            // Park the ball until it is served.
            if (!_telegraphed || _timer > 0f)
                _ball.ResetTo(_launchPoint.position);

            // ~0.7s before launch, pick the target and show the telegraph.
            if (!_telegraphed && _timer <= 0.7f)
            {
                PickServe();
                _reticle.Show(_pendingTarget);
                _telegraphed = true;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f && _telegraphed)
            {
                Launch();
                return true;
            }
            return false;
        }

        void PickServe()
        {
            // Same landing spot and flight every serve, no curl (predictable practice).
            _pendingTarget = SimConfig.ServeTarget;
            _pendingTime = SimConfig.ServeTime;
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
        }

        void Launch()
        {
            _ball.ResetTo(_launchPoint.position);
            _ball.LaunchTo(_pendingTarget, _pendingTime, _pendingCurl, _pendingSpin);
            _reticle.Hide();
            JustServed = true;
        }
    }
}
