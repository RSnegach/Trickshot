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
        // How the ball was last struck by the player, for goal callouts. Set at contact
        // (header/diving-header here, bicycle by KickDetector), cleared on reset/launch.
        public ShotType LastShotType = ShotType.Normal;

        // Arcade aim-assist: after a striker touch, briefly steer the flat velocity
        // partway toward the goal so more shots are on target (subtle).
        float _assistRemaining;
        float _assistCooldown;
        float _accuracyMul = 1f;   // goal-steer strength for the current assist window (per body part)

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
            _col.material = Make.PhysMat("Ball", SimConfig.BallBounciness, 0.2f, 0.2f);

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
            LastShotType = ShotType.Normal;
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
                if (_assistRemaining <= 0f) _accuracyMul = 1f;
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
            // Accuracy = how strongly the shot is steered toward goal, set per contact
            // (strong foot full, weak foot half, body low, header high).
            float steer = SimConfig.AssistSteerFrac * _accuracyMul;
            Vector3 desiredDir = Vector3.Slerp(flat.normalized, toGoal.normalized, Mathf.Clamp01(steer));
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

            // Was this a striker limb? (KickDetector lives on limbs, or any ActiveRagdoll bone.)
            var ragdoll = c.collider.GetComponentInParent<ActiveRagdoll>();
            if (ragdoll == null) return;

            // Which body part struck the ball. The collider lives ON the P_<Bone> object
            // (its visual child collider is destroyed at build), so read the collider's
            // OWN transform name, not its parent (parent is the next bone up the chain).
            string part = c.collider.transform.name;   // e.g. "P_Head", "P_FootR", "P_Torso"
            bool header = part == "P_Head";
            if (!header && _assistCooldown > 0f) return;
            Vector3 v = Rb.linearVelocity;

            var striker = ragdoll.GetComponent<Striker>();
            if (header)
                LastShotType = (striker != null && striker.IsDiving) ? ShotType.DivingHeader : ShotType.Header;

            // Build trait: a heavier/taller player strikes harder (player only; AI = 1.0).
            float shotMul = striker != null ? PlayerProfile.ShotPowerMul : 1f;

            // Per-part accuracy + power. A LEG/FOOT is a real strike (full on the strong
            // side, weak + less powerful on the other); the HEAD uses heading rules;
            // anything else (torso, arms, pelvis) is a scrappy touch that mostly kills the
            // ball so it drops at the player's feet (a trap), imparting almost no power.
            bool isLeg = part.StartsWith("P_Foot") || part.StartsWith("P_Calf") || part.StartsWith("P_Thigh");
            bool leftSide = part.EndsWith("L");
            bool deadTrap = false;
            if (header)
            {
                _accuracyMul = SimConfig.HeaderAccuracyMul;
            }
            else if (isLeg)
            {
                bool strong = striker == null || (leftSide == PlayerProfile.LeftFooted);
                _accuracyMul = strong ? SimConfig.StrongFootAccuracy : SimConfig.WeakFootAccuracy;
                if (!strong) shotMul *= SimConfig.WeakFootPowerMul;

                // Kick-vs-run: only a fast-SWINGING leg imparts power. The struck bone's
                // own speed distinguishes a kick from just running into the ball. Below the
                // floor it is a dead touch (the ball barely moves - lets the player dribble
                // / control it instead of shooting by walking into it).
                float boneSpeed = c.collider.attachedRigidbody != null
                    ? c.collider.attachedRigidbody.linearVelocity.magnitude : 0f;
                float kick = Mathf.InverseLerp(SimConfig.KickSpeedFloor, SimConfig.KickSpeedFull, boneSpeed);
                if (kick <= 0.001f) deadTrap = true;
                else shotMul *= kick;   // scale strike power by how hard the leg swung
            }
            else
            {
                // Body / arms / pelvis: kill it. Low accuracy, and treat as a dead trap.
                _accuracyMul = SimConfig.BodyAccuracy;
                shotMul *= SimConfig.BodyPowerMul;
                deadTrap = true;
            }

            // A dead touch traps the ball: strip most of its velocity so it drops and
            // settles at the player's feet, then skip the strike amplification entirely.
            if (deadTrap)
            {
                Rb.linearVelocity *= SimConfig.DeadTouchPower;
                Rb.angularVelocity *= 0.3f;
                _assistCooldown = 0.25f;
                return;
            }

            if (header)
            {
                // REDIRECT onto a mostly-goal-ward horizontal line (a glancing touch is
                // steered toward goal, not just sped up in its old direction) and give it
                // real pace, floored so even a soft header flies. Vertical is largely
                // flattened so it drives in low and hard.
                Vector3 toGoal = SimConfig.GoalCenter - Rb.position; toGoal.y = 0f;
                if (toGoal.sqrMagnitude < 0.01f) toGoal = Vector3.forward;
                toGoal.Normalize();

                Vector3 flatIn = new Vector3(v.x, 0f, v.z);
                float inSpeed = flatIn.magnitude;
                Vector3 dir = flatIn.sqrMagnitude > 0.01f
                    ? Vector3.Slerp(flatIn.normalized, toGoal, SimConfig.HeaderGoalBias)
                    : toGoal;

                float speed = Mathf.Max(SimConfig.HeaderMinSpeed,
                                        inSpeed * SimConfig.HeaderPowerMul) * shotMul;
                speed = Mathf.Min(speed, SimConfig.StrikeHorizMax * SimConfig.HeaderPowerMul * shotMul);

                Vector3 flat = dir * speed;
                Rb.linearVelocity = new Vector3(flat.x, v.y * SimConfig.HeaderVerticalKeep, flat.z);

                // Swerve toward goal via curl + spin.
                Vector3 lateral = Vector3.Cross(Vector3.up, toGoal);
                _curlAccel = lateral * SimConfig.HeaderSwerve;
                _curlRemaining = SimConfig.AssistDuration + 0.3f;
                Rb.angularVelocity += new Vector3(0f, SimConfig.HeaderSwerve, 0f);
            }
            else
            {
                // Normal strike: amplify the ball's existing horizontal velocity (scaled
                // by the striker's shot-power trait).
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                flat = Vector3.ClampMagnitude(flat * SimConfig.StrikeHorizBoost * shotMul,
                                              SimConfig.StrikeHorizMax * shotMul);
                Rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }

            _assistRemaining = SimConfig.AssistDuration;
            _assistCooldown = 0.4f;   // don't re-trigger every micro-contact
            // _accuracyMul (set above per body part) drives the goal-steer during the window.
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
            LastShotType = ShotType.Normal;
            _trail.emitting = false;
            _trail.Clear();
        }

        public float Speed => Rb.linearVelocity.magnitude;
    }
}
