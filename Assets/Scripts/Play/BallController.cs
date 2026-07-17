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

        // Arcade aim-assist: after a striker touch, briefly steer the flat velocity
        // partway toward the goal so more shots are on target (subtle).
        float _assistRemaining;
        float _assistCooldown;

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
            // Ball-speed multiplier shortens the flight time (faster ball) while still
            // solving to hit the same target - scaling v0 directly would miss.
            timeOfFlight = Mathf.Max(0.2f, timeOfFlight / Mathf.Max(0.1f, SimConfig.BallSpeedMul));

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

            if (_assistCooldown > 0f) _assistCooldown -= Time.fixedDeltaTime;

            if (_assistRemaining > 0f)
            {
                _assistRemaining -= Time.fixedDeltaTime;
                ApplyGoalAssist();
            }
        }

        // Bend the ball's horizontal velocity slightly toward the goal without changing
        // its speed much: steer the flat direction a fraction toward goal, apply the
        // difference as a capped acceleration. Vertical motion is left alone.
        void ApplyGoalAssist()
        {
            Vector3 v = Rb.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float speed = flat.magnitude;
            if (speed < SimConfig.AssistMinSpeed) return;
            if (v.z <= 0.1f) return; // only help shots already heading toward the goal (+Z)

            Vector3 toGoal = SimConfig.GoalCenter - Rb.position; toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 0.01f) return;
            Vector3 desiredDir = Vector3.Slerp(flat.normalized, toGoal.normalized, SimConfig.AssistSteerFrac);
            Vector3 desiredVel = desiredDir * speed;                 // preserve horizontal speed
            Vector3 delta = desiredVel - flat;
            Vector3 accel = Vector3.ClampMagnitude(delta / Mathf.Max(0.02f, SimConfig.AssistDuration),
                                                   SimConfig.AssistMaxAccel);
            Rb.AddForce(accel, ForceMode.Acceleration);
        }

        void OnCollisionEnter(Collision c)
        {
            // Net backstop: kill the rebound in code (material combine can't beat the
            // ball's own Maximum bounce). Keep a little velocity so it slides down.
            if (c.collider.GetComponentInParent<NetBackstop>() != null)
            {
                Rb.linearVelocity *= 0.12f;
                Rb.angularVelocity *= 0.3f;
                return;
            }

            if (_assistCooldown > 0f) return;
            // Was this a striker limb? (KickDetector lives on limbs, or any ActiveRagdoll bone.)
            var ragdoll = c.collider.GetComponentInParent<ActiveRagdoll>();
            if (ragdoll == null) return;

            // Punch the horizontal velocity so a struck ball drives away with pace
            // (leave vertical alone so lobs/loft still feel right). Capped so it stays
            // arcadey-fast, not absurd.
            Vector3 v = Rb.linearVelocity;
            Vector3 flat = new Vector3(v.x, 0f, v.z);
            flat = Vector3.ClampMagnitude(flat * SimConfig.StrikeHorizBoost, SimConfig.StrikeHorizMax);
            Rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);

            _assistRemaining = SimConfig.AssistDuration;
            _assistCooldown = 0.4f;   // don't re-trigger every micro-contact
        }

        public void ResetTo(Vector3 pos)
        {
            Rb.position = pos;
            transform.position = pos;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            _curlRemaining = 0f;
            _curlAccel = Vector3.zero;
            _assistRemaining = 0f;
            _assistCooldown = 0f;
            LastShotWasTrick = false;
            _trail.emitting = false;
            _trail.Clear();
        }

        public float Speed => Rb.linearVelocity.magnitude;
    }
}
