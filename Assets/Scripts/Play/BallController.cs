using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The match ball. Marker component (other scripts find the ball by
    /// GetComponent&lt;BallController&gt;) plus physics tuning, optional curl while
    /// airborne, a motion trail, and reset.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class BallController : MonoBehaviour
    {
        public Rigidbody Rb { get; private set; }
        SphereCollider _col;
        TrailRenderer _trail;

        // Lateral curl acceleration applied while the ball is in the air, decaying
        // over the flight. Set by the Crosser at launch. +x world curls one way.
        Vector3 _curlAccel;
        float _curlRemaining;

        public bool LastShotWasTrick;   // set by KickDetector when a valid trick connects

        void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            _col = GetComponent<SphereCollider>();
            Rb.mass = SimConfig.BallMass;
            Rb.linearDamping = SimConfig.BallDrag;
            Rb.angularDamping = SimConfig.BallAngularDrag;
            Rb.interpolation = RigidbodyInterpolation.Interpolate;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Rb.maxAngularVelocity = 60f;
            _col.radius = 0.5f; // primitive sphere radius in local space
            _col.material = Make.PhysMat("Ball", SimConfig.BallBounciness, 0.4f, 0.4f);

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.35f;
            _trail.startWidth = 0.18f;
            _trail.endWidth = 0f;
            _trail.material = Make.Glow(new Color(1f, 0.95f, 0.4f));
            _trail.emitting = false;
        }

        public void LaunchTo(Vector3 targetPoint, float timeOfFlight, Vector3 curlAccel, float spin)
        {
            Vector3 g = Physics.gravity;
            Vector3 p0 = Rb.position;
            // v0 such that p0 + v0*t + 0.5*g*t^2 = target at t = timeOfFlight
            Vector3 v0 = (targetPoint - p0 - 0.5f * g * timeOfFlight * timeOfFlight) / timeOfFlight;

            Rb.linearVelocity = v0;
            Rb.angularVelocity = new Vector3(spin, 0f, 0f);
            _curlAccel = curlAccel;
            _curlRemaining = timeOfFlight;
            LastShotWasTrick = false;
            _trail.emitting = true;
            _trail.Clear();
        }

        void FixedUpdate()
        {
            if (_curlRemaining > 0f)
            {
                Rb.AddForce(_curlAccel, ForceMode.Acceleration);
                _curlRemaining -= Time.fixedDeltaTime;
            }
        }

        public void ResetTo(Vector3 pos)
        {
            Rb.position = pos;
            transform.position = pos;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            _curlRemaining = 0f;
            _curlAccel = Vector3.zero;
            LastShotWasTrick = false;
            _trail.emitting = false;
            _trail.Clear();
        }

        public float Speed => Rb.linearVelocity.magnitude;
    }
}
