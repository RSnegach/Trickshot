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

        // Camera to pulse into ball-cam on a genuine shot (optional; null in modes that
        // don't want it). Set by the mode builder.
        GameCamera _cam;
        public void SetCamera(GameCamera cam) => _cam = cam;

        // Dribble hand-off. While the Dribble component is carrying the ball it OWNS it:
        // the striker's own strike/trap contact logic is skipped so the run cycle's foot
        // taps don't fight the soft-magnet. Dribble toggles this on capture/release. After
        // a dribble shot a brief suppression stops the launching foot from re-striking.
        public bool DribbleHold { get; set; }   // true while the Dribble component is carrying
        float _strikeSuppress;                   // >0: skip striker strike logic (post-shot settle)
        public void SuppressStrike(float t) => _strikeSuppress = Mathf.Max(_strikeSuppress, t);

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
            if (_strikeSuppress > 0f) _strikeSuppress -= Time.fixedDeltaTime;

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

            // Strike/redirect logic is ONLY for the human player striker. An AI keeper or
            // crosser just deflects the ball with normal physics - otherwise an AI keeper's
            // head/foot touch would get steered toward the goal it defends (own goals).
            var striker = ragdoll.GetComponent<Striker>();
            if (striker == null) return;

            // While the ball is being dribbled (or just after a dribble shot), the Dribble
            // component owns the ball's motion. Skip the strike/trap logic so the run-cycle
            // foot contacts don't fight the soft-magnet or double-hit a released shot.
            if (DribbleHold || _strikeSuppress > 0f) return;

            // Which body part struck the ball. The collider lives ON the P_<Bone> object
            // (its visual child collider is destroyed at build), so read the collider's
            // OWN transform name, not its parent (parent is the next bone up the chain).
            string part = c.collider.transform.name;   // e.g. "P_Head", "P_FootR", "P_Torso"
            bool header = part == "P_Head";
            if (!header && _assistCooldown > 0f) return;
            Vector3 v = Rb.linearVelocity;

            // Aim assist only applies when the striker is actually FACING the opponents'
            // goal. Turned side-on or facing his own goal -> no goal-ward steering of any
            // kind (foot shot keeps its true direction, header is a plain deflection).
            // EXCEPTION: a bicycle attempt (airborne + reclined) is kicked back over the
            // head, so the body faces AWAY from goal by design - the trick mechanic itself
            // aims it goalward, so it should keep its assist. Treat it as facing the goal.
            bool bicycleAttempt = striker.TrickActive;
            Vector3 faceFwd = ragdoll.FacingRotation * Vector3.forward; faceFwd.y = 0f;
            Vector3 faceToGoal = SimConfig.GoalCenter - ragdoll.Pelvis.transform.position; faceToGoal.y = 0f;
            bool facingGoal = bicycleAttempt
                              || (faceToGoal.sqrMagnitude > 0.01f && faceFwd.sqrMagnitude > 0.01f
                                  && Vector3.Dot(faceFwd.normalized, faceToGoal.normalized) >= SimConfig.AssistFacingDot);

            if (header)
                LastShotType = striker.IsDiving ? ShotType.DivingHeader : ShotType.Header;

            // From here the striker is the human player, so all traits/skills apply.
            // Build trait * Shooting tree: heavier/taller + shot nodes hit harder.
            float shotMul = PlayerProfile.ShotPowerMul;
            // Cannon capstone raises the speed ceiling so shots can fly much faster.
            float capMul = PlayerProfile.PerkCannon ? SimConfig.CannonCapMul : 1f;

            // Per-part accuracy + power. A LEG/FOOT is a real strike (full on the strong
            // side, weak + less powerful on the other); the HEAD uses heading rules;
            // anything else (torso, arms, pelvis) is a scrappy touch that mostly kills the
            // ball so it drops at the player's feet (a trap), imparting almost no power.
            bool isLeg = part.StartsWith("P_Foot") || part.StartsWith("P_Calf") || part.StartsWith("P_Thigh");
            bool leftSide = part.EndsWith("L");
            bool deadTrap = false;
            if (header)
            {
                // Heading tree scales accuracy + power.
                _accuracyMul = SimConfig.HeaderAccuracyMul * PlayerProfile.HeaderAccuracyMul;
                shotMul *= PlayerProfile.HeaderPowerMul;
            }
            else if (isLeg)
            {
                // Strong foot = full; weak = reduced, but the Control tree's weak-foot node
                // (and the Silky capstone -> both feet strong) claw that back.
                bool strong = (leftSide == PlayerProfile.LeftFooted) || PlayerProfile.PerkSilky;
                if (strong)
                {
                    _accuracyMul = SimConfig.StrongFootAccuracy;
                }
                else
                {
                    float wf = PlayerProfile.WeakFootMul;   // 1.0..~1.7 with Control nodes
                    _accuracyMul = SimConfig.WeakFootAccuracy * wf;
                    shotMul *= SimConfig.WeakFootPowerMul * wf;
                }
                _accuracyMul += PlayerProfile.ShotAccuracyMul - 1f;   // Shooting/Control accuracy nodes

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
            // The Control tree's first-touch nodes deaden it further (ball settles closer).
            if (deadTrap)
            {
                float trap = SimConfig.DeadTouchPower / PlayerProfile.TrapMul;   // Control tree deadens further
                Rb.linearVelocity *= trap;
                Rb.angularVelocity *= 0.3f;
                _assistCooldown = 0.25f;
                return;
            }

            // Not facing the opponents' goal -> no goal-ward help. Zero the steer window so
            // ApplyGoalAssist does nothing, and (below) the header won't be bent to goal.
            if (!facingGoal) _accuracyMul = 0f;

            if (header)
            {
                // REDIRECT onto a mostly-goal-ward horizontal line (a glancing touch is
                // steered toward goal, not just sped up in its old direction) and give it
                // real pace, floored so even a soft header flies. Vertical is largely
                // flattened so it drives in low and hard. Only when FACING the goal; a
                // header while turned away is a plain deflection along its incoming line.
                Vector3 toGoal = SimConfig.GoalCenter - Rb.position; toGoal.y = 0f;
                if (toGoal.sqrMagnitude < 0.01f) toGoal = Vector3.forward;
                toGoal.Normalize();

                // Aerial capstone: steer harder toward goal and keep more of the incoming
                // pace/vertical (a more dangerous header).
                bool aerial = PlayerProfile.PerkAerial;
                float goalBias = !facingGoal ? 0f : (aerial ? SimConfig.AerialGoalBias : SimConfig.HeaderGoalBias);
                float vKeep = aerial ? SimConfig.AerialPaceKeep : SimConfig.HeaderVerticalKeep;

                Vector3 flatIn = new Vector3(v.x, 0f, v.z);
                float inSpeed = flatIn.magnitude;
                // Facing goal: bias toward it (falling back to toGoal if no incoming line).
                // Not facing: keep the ball's own incoming line, or the head's facing dir
                // if it arrived nearly straight down.
                Vector3 dir;
                if (facingGoal)
                    dir = flatIn.sqrMagnitude > 0.01f ? Vector3.Slerp(flatIn.normalized, toGoal, goalBias) : toGoal;
                else
                    dir = flatIn.sqrMagnitude > 0.01f ? flatIn.normalized : faceFwd.normalized;

                float speed = Mathf.Max(SimConfig.HeaderMinSpeed,
                                        inSpeed * SimConfig.HeaderPowerMul) * shotMul;
                speed = Mathf.Min(speed, SimConfig.StrikeHorizMax * SimConfig.HeaderPowerMul * shotMul * capMul);

                Vector3 flat = dir * speed;
                Rb.linearVelocity = new Vector3(flat.x, v.y * vKeep, flat.z);

                // Swerve toward goal via curl + spin - only when facing the goal. A header
                // while turned away flies straight (no goal-ward curl).
                if (facingGoal)
                {
                    Vector3 lateral = Vector3.Cross(Vector3.up, toGoal);
                    _curlAccel = lateral * SimConfig.HeaderSwerve;
                    _curlRemaining = SimConfig.AssistDuration + 0.3f;
                    Rb.angularVelocity += new Vector3(0f, SimConfig.HeaderSwerve, 0f);
                }
                else
                {
                    _curlAccel = Vector3.zero;
                    _curlRemaining = 0f;
                    Rb.angularVelocity *= 0.2f;
                }
            }
            else
            {
                // Normal strike: amplify the ball's existing horizontal velocity (scaled
                // by the striker's shot-power trait).
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                flat = Vector3.ClampMagnitude(flat * SimConfig.StrikeHorizBoost * shotMul,
                                              SimConfig.StrikeHorizMax * shotMul * capMul);
                Rb.linearVelocity = new Vector3(flat.x, v.y, flat.z);
                // Minimal swerve by default: clear any curl carried from the serve and
                // damp the spin so a struck shot flies mostly straight.
                _curlAccel = Vector3.zero;
                _curlRemaining = 0f;
                Rb.angularVelocity *= 0.2f;
            }

            _assistRemaining = SimConfig.AssistDuration;
            _assistCooldown = 0.4f;   // don't re-trigger every micro-contact
            // _accuracyMul (set above per body part) drives the goal-steer during the window.

            // Auto ball-cam: a dead trap already returned above, so this is a real strike
            // (foot shot or header). Cut to ball-cam only for a genuine SHOT taken IN THE
            // SIGHT CONE (facing the goal, same gate as the aim assist) and hit with real
            // pace. A struck ball while turned side-on / away, or a slow touch, doesn't cut.
            if (_cam != null && facingGoal)
            {
                Vector3 outV = Rb.linearVelocity; outV.y = 0f;
                if (outV.magnitude >= SimConfig.ShotCamMinSpeed)
                    _cam.PulseBallCam(SimConfig.ShotCamSeconds);
            }
        }

        // A shot launched by the Dribble component (release-on-kick). Sets the shot
        // velocity, then folds into the SAME systems a normal strike uses: the facing-
        // gated goal assist and the 2s ball-cam pulse. Suppresses re-strike/re-capture so
        // the launching foot doesn't immediately re-hit the ball.
        public void DribbleShot(Vector3 dir, float speed, bool facingGoal)
        {
            Rb.linearVelocity = dir * speed;
            Rb.angularVelocity = Vector3.zero;
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;

            if (facingGoal)
            {
                // Strong-foot-style accuracy plus the Shooting/Control accuracy nodes.
                _accuracyMul = SimConfig.StrongFootAccuracy + (PlayerProfile.ShotAccuracyMul - 1f);
                _assistRemaining = SimConfig.AssistDuration;
                if (_cam != null)
                {
                    Vector3 flat = dir * speed; flat.y = 0f;
                    if (flat.magnitude >= SimConfig.ShotCamMinSpeed) _cam.PulseBallCam(SimConfig.ShotCamSeconds);
                }
            }
            else _accuracyMul = 0f;

            _assistCooldown = 0.4f;
            SuppressStrike(SimConfig.DribbleRecaptureCooldown);
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
            _strikeSuppress = 0f;
            DribbleHold = false;   // never leave the leash flag stuck after a reset (would disable strikes)
            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = false;
            _trail.Clear();
        }

        public float Speed => Rb.linearVelocity.magnitude;
    }
}
