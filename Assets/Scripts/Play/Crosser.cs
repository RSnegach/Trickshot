using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The crosser: a simple capsule near the wing that charges and launches a cross
    /// toward the aim reticle. Charge (hold Space) selects the cross character:
    /// low charge = lofted and slow, full charge = driven and fast. Q/E at release
    /// curl the ball inward/outward during flight.
    /// </summary>
    public class Crosser : MonoBehaviour
    {
        GameInput _input;
        AimReticle _reticle;
        BallController _ball;
        Transform _launchPoint;

        public float Charge { get; private set; }   // 0..1, for the HUD
        public bool Charging { get; private set; }

        public void Init(GameInput input, AimReticle reticle, BallController ball, Transform launchPoint)
        {
            _input = input;
            _reticle = reticle;
            _ball = ball;
            _launchPoint = launchPoint;
        }

        /// <summary>Called by GameManager only while the cross is being aimed.</summary>
        /// <returns>true on the frame the ball is launched.</returns>
        public bool TickAiming()
        {
            if (_input.ChargeHeld)
            {
                Charging = true;
                Charge = Mathf.Min(1f, Charge + Time.deltaTime / SimConfig.MaxChargeTime);
            }

            // Keep the ball parked at the crosser's feet until launch.
            if (Charging && !_input.ChargeReleased)
                _ball.ResetTo(_launchPoint.position);

            if (Charging && _input.ChargeReleased)
            {
                Launch();
                return true;
            }
            return false;
        }

        void Launch()
        {
            float t = Mathf.Lerp(SimConfig.CrossTimeLoft, SimConfig.CrossTimeDrive, Charge);
            Vector3 target = _reticle.TargetPoint + Vector3.up * 0.25f;

            float curlDir = _input.CurlAxis; // may be 0
            // Curl is lateral relative to the launch->target direction.
            Vector3 flat = target - _launchPoint.position;
            flat.y = 0f;
            Vector3 lateral = Vector3.Cross(Vector3.up, flat.normalized);
            Vector3 curlAccel = lateral * (curlDir * SimConfig.MaxCurlAccel);
            float spin = curlDir * 18f;

            _ball.ResetTo(_launchPoint.position);
            _ball.LaunchTo(target, t, curlAccel, spin);

            Charging = false;
            Charge = 0f;
        }

        public void ResetCharge()
        {
            Charging = false;
            Charge = 0f;
        }
    }
}
