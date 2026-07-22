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
        Vector3 _assistTarget;     // where the goal-steer aims this window; defaults to goal centre, set-piece re-aims a corner
        bool _assistFlatOff;       // set-piece curve shot: skip HORIZONTAL goal-steer so the intentional
                                   // out-then-back curl is not flattened; the vertical steer still applies.
        float _bikeCamCooldown;    // guard so one bicycle flip cuts to ball-cam only once

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

        // Toggle physical collision between the ball and every collider of `ragdoll`. Set-piece
        // takers ignore the taker's body during the aesthetic runup so the run-in foot passes
        // THROUGH the parked ball (the ball is launched by code, not a physical kick), then
        // restore it. Only touches ball<->that-body pairs, so the body still stands on the turf
        // and other bodies are unaffected.
        public void IgnoreBody(ActiveRagdoll ragdoll, bool ignore)
        {
            if (ragdoll == null || _col == null) return;
            var cols = ragdoll.OwnColliders;
            if (cols == null) return;
            for (int i = 0; i < cols.Count; i++)
                if (cols[i] != null) Physics.IgnoreCollision(_col, cols[i], ignore);
        }

        // Set-piece mode: while true, a struck shot (free kick / penalty) gets extra loft +
        // curl by default and its goal-assist is near-zero unless the player has invested in
        // Shooting accuracy/power - so default set pieces are hard + arcadey, and a well-built
        // striker can bend one in. Set by FreeKickGame for the whole session.
        public bool SetPieceShot { get; set; }

        // Shared, ball-side trick-bonus guard. Each leg bone carries its OWN KickDetector
        // (foot + calf, both legs), and Unity fires each collider's callback independently,
        // so a per-detector cooldown can't stop the calf AND the foot of the same flip from
        // each applying the bonus (a 2x-4x overpowered shot). This lives on the ONE ball so
        // the first bone to connect claims the bonus and the rest are locked out for the
        // window. Returns true only for the first caller while live.
        float _trickBonusCooldown;
        public bool TryClaimTrickBonus()
        {
            if (_trickBonusCooldown > 0f) return false;
            _trickBonusCooldown = SimConfig.BicycleWindow;
            return true;
        }

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

        // Spin flavour for a scripted set-piece launch (chosen by the taker's WASD hold).
        public enum SetPieceSpin { None, CurveLeft, CurveRight, TopSpin, Knuckle }

        // SCRIPTED set-piece launch (the taker's power meter + WASD spin). Aim defaults CENTRAL and
        // saveable; the skill stat `combined` (0..1) pulls it toward a goal CORNER and tightens.
        // `power01` picks the pace between the base and max launch speeds. Curl is TARGET-RELATIVE:
        // the ball is launched biased to the swing side and curled back so it RETURNS to the aim x
        // (more curve never means wider). A hard vy cap keeps every shot near goal height, so an
        // overcharged/over-held botch (`botch01`) sprays wide but never skyrockets. Body-collider-
        // disabled + SuppressStrike on the caller side keep the aesthetic runup from re-triggering
        // a physical strike, so this fully OWNS the shot.
        public void LaunchSetPiece(float power01, SetPieceSpin spin, float spinCharge01,
                                   float botch01, float combined, Vector3 goalCenter)
        {
            power01 = Mathf.Clamp01(power01);
            spinCharge01 = Mathf.Clamp01(spinCharge01);
            botch01 = Mathf.Clamp01(botch01);
            combined = Mathf.Clamp01(combined);

            float gMag = Mathf.Abs(Physics.gravity.y);
            Vector3 p0 = Rb.position;

            // Launch speed from the power meter (stat-scaled ceiling, Cannon lifts the cap).
            float capMul = PlayerProfile.PerkCannon ? SimConfig.CannonCapMul : 1f;
            float launch = Mathf.Lerp(SimConfig.SetPieceBaseSpeed,
                                      SimConfig.SetPieceMaxSpeed * capMul, power01);

            // Goal-ward flat direction (fall back to +Z toward goal).
            Vector3 toGoal = goalCenter - p0; toGoal.y = 0f;
            Vector3 shotDir = toGoal.sqrMagnitude > 0.01f ? toGoal.normalized : Vector3.forward;
            Vector3 shotRight = Vector3.Cross(Vector3.up, shotDir);

            // Corner selection. Topspin dips -> aim the BOTTOM corner; a plain/curl/knuckle shot
            // aims higher. Lateral corner is chosen by the curve side (curveLeft -> left post),
            // else centred. `combined` scales how far from centre toward the corner we aim.
            bool aimBottom = spin == SetPieceSpin.TopSpin;
            float half = SimConfig.GoalWidth * 0.5f - SimConfig.BallRadius - SimConfig.SetPieceCornerInset;
            float lat = 0f;
            if (spin == SetPieceSpin.CurveLeft)  lat = -1f;
            else if (spin == SetPieceSpin.CurveRight) lat = 1f;
            float aimX = lat * Mathf.Max(0f, half) * combined * SimConfig.SetPieceCornerPull;
            float cornerY = aimBottom ? (SimConfig.BallRadius + SimConfig.SetPieceCornerInset)
                                      : Mathf.Max(0.3f, SimConfig.GoalHeight - SimConfig.SetPieceCornerInset);
            // Default aim CENTRAL (mid-goal height, centre); accuracy pulls toward the corner.
            float aimY = Mathf.Lerp(SimConfig.GoalHeight * 0.5f, cornerY, combined);

            // Botch scatter: an overcharge / over-held spin sprays the target. Accuracy shrinks it
            // (the taker already scales botch01, but clamp the residual here too).
            float scatterMul = botch01 * (1f - 0.5f * combined);
            aimX += (Random.value * 2f - 1f) * SimConfig.SetPieceBotchScatterX * scatterMul;
            aimY += (Random.value * 2f - 1f) * SimConfig.SetPieceBotchScatterY * scatterMul;

            Vector3 aim = goalCenter + shotRight * aimX + Vector3.up * (aimY - goalCenter.y);
            _assistTarget = aim;

            // Flat launch DIRECTION toward the aim; keep the power-picked flat SPEED.
            Vector3 toAimFlat = aim - p0; toAimFlat.y = 0f;
            float horizDist = toAimFlat.magnitude;
            Vector3 flatDir = horizDist > 0.01f ? (toAimFlat / horizDist) : shotDir;
            float flatSpeed = Mathf.Max(1f, launch);

            // Solve vy so the ball is at the aim HEIGHT when it crosses the goal line, then cap the
            // apex so power can never send it over the bar.
            float tActual = Mathf.Clamp(horizDist / flatSpeed, 0.2f, 2.5f);
            float vy = (aim.y - p0.y) / tActual + 0.5f * gMag * tActual;
            float allowedApex = Mathf.Max(0.3f, SimConfig.GoalHeight - p0.y + SimConfig.SetPieceApexMargin);
            float vyMax = Mathf.Sqrt(2f * gMag * allowedApex);
            if (vy > vyMax) vy = vyMax;

            // TARGET-RELATIVE curl for a curve shot: bias the launch OUT to the swing side and set a
            // lateral curl that brings it back so it arrives on the aim x (a banana that returns).
            // Lateral offset w, curl accel -2w/t^2 over the flight => net sideways displacement 0.
            _curlAccel = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            _curlRemaining = 0f;
            if (spin == SetPieceSpin.CurveLeft || spin == SetPieceSpin.CurveRight)
            {
                float side = spin == SetPieceSpin.CurveRight ? 1f : -1f;
                float w = SimConfig.SetPieceCurl * (0.5f + spinCharge01) * 0.5f;   // out-speed, charge-scaled
                flatDir = (flatDir * flatSpeed + shotRight * (side * w)).normalized;   // launch angled out
                _curlAccel = shotRight * (-side * 2f * w / Mathf.Max(0.1f, tActual)); // curl back to aim
                _curlRemaining = tActual;
                Rb.angularVelocity = Vector3.up * (side * SimConfig.SetPieceCurl * spinCharge01);
            }
            else if (spin == SetPieceSpin.TopSpin)
            {
                // Dips: downward curl + forward roll, stronger with charge.
                _curlAccel = Vector3.down * (SimConfig.SetPieceCurl * SimConfig.SetPieceTopSpinMul * (0.5f + spinCharge01));
                _curlRemaining = tActual;
                Rb.angularVelocity = shotRight * (SimConfig.SetPieceCurl * spinCharge01);
            }
            else if (spin == SetPieceSpin.Knuckle)
            {
                // Wobble with no spin, charge-scaled (keeper-fooling). HORIZONTAL only: a vertical
                // curl component would add height AFTER the apex cap and could clear the bar, so the
                // knuckle wobbles side to side and the vy cap alone owns the height.
                float km = SimConfig.SetPieceCurl * SimConfig.SetPieceKnuckleMul * (0.5f + spinCharge01);
                _curlAccel = shotRight * (Random.Range(-1f, 1f) * km);
                _curlRemaining = tActual;
                Rb.angularVelocity = Vector3.zero;
            }

            Vector3 flat = flatDir * flatSpeed;
            Rb.linearVelocity = new Vector3(flat.x, vy, flat.z);

            // Assist window: steer toward the 3D aim over the flight. For a CURVE shot the curl
            // already lands the ball on the aim x (out then back), so skip the HORIZONTAL steer
            // (it would flatten the banana); the vertical steer still pulls the height onto target.
            _assistFlatOff = spin == SetPieceSpin.CurveLeft || spin == SetPieceSpin.CurveRight;
            _accuracyMul = SimConfig.SetPieceAssistFloor
                           + combined * (SimConfig.SetPieceAssistMax - SimConfig.SetPieceAssistFloor);
            _assistRemaining = SimConfig.AssistDuration;
            _assistCooldown = 0.4f;

            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = true;
            _trail.Clear();
            // The taker disables the body collider, but suppress physical strikes too so nothing
            // the runup foot brushes can re-enter the OnCollisionEnter set-piece branch.
            SuppressStrike(0.5f);
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
            if (_bikeCamCooldown > 0f) _bikeCamCooldown -= Time.fixedDeltaTime;
            if (_trickBonusCooldown > 0f) _trickBonusCooldown -= Time.fixedDeltaTime;

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

            // Aim point: normally the goal centre, but a set-piece strike re-aims this at a
            // goal CORNER (see the accuracy branch in OnCollisionEnter), so the steer places
            // the ball toward the post/edge instead of the middle. Zero => fall back to centre.
            Vector3 aim = _assistTarget.sqrMagnitude > 0.01f ? _assistTarget : SimConfig.AttackGoalCenter;
            Vector3 toGoal = aim - Rb.position; toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 0.01f) return;
            // Accuracy = how strongly the shot is steered toward the aim point, set per contact
            // (strong foot full, weak foot half, body low, header high; set piece scales hard).
            // Horizontal goal-steer. Skipped for a set-piece CURVE shot (_assistFlatOff): its curl
            // already returns the ball to the aim x, and steering would flatten the intended bend.
            if (!_assistFlatOff)
            {
                float steer = SimConfig.AssistSteerFrac * _accuracyMul;
                Vector3 desiredDir = Vector3.Slerp(flat.normalized, toGoal.normalized, Mathf.Clamp01(steer));
                Vector3 desiredVel = desiredDir * speed;                 // preserve horizontal speed
                Vector3 delta = desiredVel - flat;
                Vector3 accel = Vector3.ClampMagnitude(delta / Mathf.Max(0.02f, SimConfig.AssistDuration),
                                                       SimConfig.AssistMaxAccel);
                Rb.AddForce(accel, ForceMode.Acceleration);
            }

            // VERTICAL steer toward the aim HEIGHT - only when the window carries a real height
            // target (set-pieces set _assistTarget.y to a corner; open-play/header leave it ~0, so
            // this is a no-op for them). Pulls the shot onto the corner height mid-flight so a
            // guided free kick converges vertically instead of relying on the launch loft alone.
            if (_assistTarget.y > 0.05f)
            {
                // Predictive: where will the ball be vertically when it reaches the goal line,
                // under gravity, if we do nothing? Steer that predicted height toward the target.
                float dist = Mathf.Max(0.1f, aim.z - Rb.position.z);
                float tHit = dist / Mathf.Max(1f, v.z);
                float predY = Rb.position.y + v.y * tHit + 0.5f * Physics.gravity.y * tHit * tHit;
                float yErr = _assistTarget.y - predY;
                float vAccel = Mathf.Clamp(yErr * SimConfig.AssistVertFrac * _accuracyMul,
                                           -SimConfig.AssistMaxAccel, SimConfig.AssistMaxAccel);
                Rb.AddForce(Vector3.up * vAccel, ForceMode.Acceleration);
            }
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

            // Strike/redirect logic is ONLY for the HUMAN-controlled striker. An AI keeper,
            // crosser, or AI footballer (which also carries a Striker for takeover, but with
            // ControlEnabled off) just deflects the ball with normal physics - otherwise its
            // head/foot touch would get steered toward the goal it attacks (or its own).
            var striker = ragdoll.GetComponent<Striker>();
            if (striker == null || !striker.ControlEnabled) return;

            // BICYCLE BALL-CAM (decided FIRST, before any of the strike-path early-returns).
            // The old code only pulsed ball-cam at the very bottom of the strike block, which
            // an assist-cooldown / dead-trap / dribble-suppress return would silently skip -
            // so a bike whose foot touch landed just after a stray limb brush never cut to
            // ball-cam. The Striker now LATCHES a bicycle window (see Striker.TrickActive), so
            // any ball contact from the flipping striker while that window is live cuts to
            // ball-cam here, up front, regardless of what the strike logic does afterward.
            // A short cooldown means one flip only triggers once even if several bones brush.
            if (striker.TrickActive && _bikeCamCooldown <= 0f)
            {
                _bikeCamCooldown = SimConfig.BicycleWindow;
                if (_cam != null) _cam.PulseBallCam(SimConfig.ShotCamSeconds);
            }

            // While the ball is being dribbled (or just after a dribble shot), the Dribble
            // component owns the ball's motion. Skip the strike/trap logic so the run-cycle
            // foot contacts don't fight the soft-magnet or double-hit a released shot.
            if (DribbleHold || _strikeSuppress > 0f) return;

            // Where on the ball was it struck (offset from centre), for set-piece spin. Unit
            // vector from ball centre toward the contact point. Captured now while `c` is live.
            Vector3 strikeOffset = Vector3.zero;
            if (c.contactCount > 0)
            {
                strikeOffset = c.GetContact(0).point - Rb.position;
                if (strikeOffset.sqrMagnitude > 1e-6f) strikeOffset.Normalize();
            }

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
            Vector3 faceToGoal = SimConfig.AttackGoalCenter - ragdoll.Pelvis.transform.position; faceToGoal.y = 0f;
            float facingDot = (faceToGoal.sqrMagnitude > 0.01f && faceFwd.sqrMagnitude > 0.01f)
                              ? Vector3.Dot(faceFwd.normalized, faceToGoal.normalized) : -1f;
            bool facingGoal = bicycleAttempt || facingDot >= SimConfig.AssistFacingDot;
            // Auto ball-cam: ONLY for shots taken FACING AWAY from the opponents' goal - the
            // bicycle / over-shoulder shots you can't otherwise watch. When the striker is
            // facing the goal (dead-ahead in the cone OR side-on), he can already see it, so
            // the cam does NOT snap. Bicycles always qualify (their body faces away by design).
            bool camShouldCut = bicycleAttempt || facingDot < SimConfig.ShotCamFaceAwayDot;

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
            bool volley = false;   // flying ball + swinging leg -> free-kick launch (set in the isLeg branch)
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
                // SET PIECE EXCEPTION: a set-piece strike is scripted off a dead ball, so the
                // foot's swing speed is IRRELEVANT - any clean contact launches at full power.
                // Skip the swing gate entirely so there is no dead trap and no speed penalty.
                if (!SetPieceShot)
                {
                    float boneSpeed = c.collider.attachedRigidbody != null
                        ? c.collider.attachedRigidbody.linearVelocity.magnitude : 0f;
                    float kick = Mathf.InverseLerp(SimConfig.KickSpeedFloor, SimConfig.KickSpeedFull, boneSpeed);
                    if (kick <= 0.001f) deadTrap = true;
                    else shotMul *= kick;   // scale strike power by how hard the leg swung
                }

                // VOLLEY: an AIRBORNE ball (any ball off the turf, see VolleyMinBallHeight) met
                // by a SWINGING leg is launched with the free-kick rules (loft + contact-point
                // curl, stat-scaled) instead of being trapped. A "swing" REQUIRES the leg-raise
                // button (LMB left / RMB right) for the struck side to be held - a fast RUNNING
                // gait swing with no button never volleys. Balls rolling on the ground and
                // planted legs are unaffected.
                // EXCLUDE a bicycle attempt (bicycleAttempt == striker.TrickActive, latched from
                // pelvis recline + air-pitch lean): a bike is its own trick shot and must NOT
                // feel like a set piece/volley - it keeps the plain amplified-strike path + its
                // own trick bonus (KickDetector) and ball-cam.
                if (!deadTrap && !bicycleAttempt && Rb.position.y > SimConfig.VolleyMinBallHeight
                    && striker.LegRaiseHeld(leftSide))
                    volley = true;
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

            // A volley (flying ball, swinging leg) borrows the free-kick launch rules for THIS
            // contact only, without touching the session-wide SetPieceShot flag (so modes can't
            // leak state). setPiece drives the loft/curl + set-piece accuracy branches below.
            bool setPiece = SetPieceShot || volley;
            if (volley) LastShotType = ShotType.Volley;

            // Default the goal-steer aim to the goal centre. A set-piece strike overrides this
            // with a corner (see the accuracy branch below), scaled by Shooting stats.
            _assistTarget = Vector3.zero;
            _assistFlatOff = false;   // physical strikes always use the normal horizontal steer

            if (header)
            {
                // REDIRECT onto a mostly-goal-ward horizontal line (a glancing touch is
                // steered toward goal, not just sped up in its old direction) and give it
                // real pace, floored so even a soft header flies. Vertical is largely
                // flattened so it drives in low and hard. Only when FACING the goal; a
                // header while turned away is a plain deflection along its incoming line.
                Vector3 toGoal = SimConfig.AttackGoalCenter - Rb.position; toGoal.y = 0f;
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
                Vector3 flat;
                float vy;

                if (setPiece)
                {
                    // SET PIECE (free kick / penalty) OR VOLLEY: the ball is dead / floated, so
                    // this is a scripted LAUNCH - NOT an amplification of whatever pace the ball
                    // already had (the foot's swing speed is irrelevant, see the swing gate
                    // above). Any clean contact fires it HIGH, FAST and GOALWARD. Shooting POWER
                    // scales the launch speed and the bend; the Cannon capstone lifts the ceiling.
                    // WHERE on the ball it is struck picks the spin/bend:
                    //   struck RIGHT side  -> curls RIGHT   (bends the SAME way it was struck)
                    //   struck LEFT side   -> curls LEFT
                    //   struck TOP         -> top spin (dips)
                    //   struck BOTTOM      -> CHIP (scooped high + soft) ... 20% of the time
                    //                          (rising with power) it KNUCKLES a flat power shot.

                    // Launch straight at goal when facing it; else keep the struck direction
                    // (the ball's own line, or the foot's facing as a last fallback).
                    Vector3 toGoal = SimConfig.AttackGoalCenter - Rb.position; toGoal.y = 0f;
                    Vector3 inFlat = new Vector3(v.x, 0f, v.z);
                    Vector3 shotDir;
                    if (facingGoal && toGoal.sqrMagnitude > 0.01f) shotDir = toGoal.normalized;
                    else if (inFlat.sqrMagnitude > 0.01f)          shotDir = inFlat.normalized;
                    else                                           shotDir = faceFwd.sqrMagnitude > 0.01f ? faceFwd.normalized : Vector3.forward;

                    // Scripted launch speed: a power-scaled floor, hard-capped (Cannon raises it).
                    float launch = Mathf.Min(SimConfig.SetPieceBaseSpeed * shotMul,
                                             SimConfig.SetPieceMaxSpeed * capMul);
                    flat = shotDir * launch;
                    vy   = launch * SimConfig.SetPieceLoft;

                    // Strike frame: right = across the shot, up = world up. side>0 = struck on
                    // the ball's right, vert>0 = struck high.
                    Vector3 shotRight = Vector3.Cross(Vector3.up, shotDir);
                    float side = Vector3.Dot(strikeOffset, shotRight);   // -1..1 (right positive)
                    float vert = strikeOffset.y;                          // -1..1 (up positive)
                    float curlMag = SimConfig.SetPieceCurl * PlayerProfile.ShotPowerMul;

                    _curlAccel = Vector3.zero; _curlRemaining = 0f;
                    Rb.angularVelocity = Vector3.zero;

                    // ---- Guided placement: accuracy + strike location, NOT power ----
                    // SKILL-ONLY combined stat: full Shooting+Control drives this to 1 regardless
                    // of body build. accStat maxes at the 1.97 shot-acc ceiling; powStat uses the
                    // SKILL power mul (SkillTree, not the body-coupled ShotPowerMul) normalized to
                    // its 1.68 skill ceiling, so weight/height never gate set-piece accuracy.
                    float accStat = Mathf.Clamp01((PlayerProfile.ShotAccuracyMul - 1f) / 0.97f);
                    float powStat = Mathf.Clamp01((SkillTree.Mul("shotpower") - 1f) / 0.68f);
                    float combined = Mathf.Clamp01(0.6f * accStat + 0.4f * powStat);
                    _accuracyMul = SimConfig.SetPieceAssistFloor
                                   + combined * (SimConfig.SetPieceAssistMax - SimConfig.SetPieceAssistFloor);

                    // 3D corner target the shot is steered toward, using the LIVE (goalScale-
                    // adjusted) goal opening so it holds regardless of goal size. Lateral: toward
                    // the post on the struck side, distance scaling with the skill stat (0=centre,
                    // 1=just inside the post). Vertical: struck LOW on the ball -> TOP corner (it
                    // climbs); struck HIGH -> BOTTOM corner (it dips). This lines up with the spin
                    // branches below (bottom strike lofts, top strike dips) and lets a skilled
                    // striker pick the corner by contact point.
                    float latSign = Mathf.Abs(side) > 0.05f ? Mathf.Sign(side) : 0f;
                    float halfInside = Mathf.Max(0f, SimConfig.GoalWidth * 0.5f - SimConfig.BallRadius - SimConfig.SetPieceCornerInset);
                    bool aimTop = vert <= SimConfig.SetPieceLowStrike;
                    float cornerY = aimTop ? Mathf.Max(0.3f, SimConfig.GoalHeight - SimConfig.SetPieceCornerInset)
                                           : (SimConfig.BallRadius + SimConfig.SetPieceCornerInset);
                    // Both axes scale with skill from CENTRE (raw) to the true CORNER (maxed): a
                    // raw striker's shot sits mid-goal (central, saveable, still on frame), a fully
                    // invested one hunts the actual corner. Keeps default difficulty honest while
                    // rewarding investment, on both the lateral and vertical axis.
                    float aimY = Mathf.Lerp(SimConfig.GoalHeight * 0.5f, cornerY, combined);
                    _assistTarget = SimConfig.AttackGoalCenter
                                    + shotRight * (latSign * halfInside * combined)
                                    + Vector3.up * aimY;

                    if (Mathf.Abs(side) >= SimConfig.SetPieceSideThresh)
                    {
                        // Side spin: bend the SAME way the ball was struck (Coriolis feel) -
                        // struck on the right curls right, struck left curls left - scaled by how
                        // far off-centre the contact was. Lateral accel across the shot.
                        float s = Mathf.Sign(side) * Mathf.Clamp01(Mathf.Abs(side));
                        _curlAccel = shotRight * (s * curlMag);
                        _curlRemaining = SimConfig.AssistDuration + 0.5f;
                        Rb.angularVelocity = Vector3.up * (s * curlMag);
                    }
                    else if (vert >= SimConfig.SetPieceTopThresh)
                    {
                        // Top spin: struck high -> dips. Curl DOWNWARD over the flight + forward
                        // roll spin about the shot-right axis.
                        _curlAccel = Vector3.down * (curlMag * SimConfig.SetPieceTopSpinMul);
                        _curlRemaining = SimConfig.AssistDuration + 0.5f;
                        Rb.angularVelocity = shotRight * curlMag;
                    }
                    else if (vert <= SimConfig.SetPieceKnuckleVert)
                    {
                        // Struck the BOTTOM of the ball. DEFAULT = CHIP: scooped up high and soft
                        // with backspin so it floats and drops. But a 20% base chance - rising
                        // LINEARLY with Shooting power - it comes off as a KNUCKLE instead: a
                        // flat, fast power shot with no spin and a random wobble whose size also
                        // scales linearly with power (keeper-fooling at high power).
                        float knuckleChance = Mathf.Clamp01(SimConfig.SetPieceKnuckleChance * PlayerProfile.ShotPowerMul);
                        if (Random.value < knuckleChance)
                        {
                            // KNUCKLE power shot: flatten the loft, drive it faster, add wobble.
                            vy   = launch * SimConfig.SetPieceLoft * 0.4f;
                            flat = shotDir * (launch * SimConfig.SetPieceKnucklePaceMul);
                            float knuckleMag = SimConfig.SetPieceCurl * SimConfig.SetPieceKnuckleMul * PlayerProfile.ShotPowerMul;
                            Vector3 wob = shotRight * Random.Range(-1f, 1f) + Vector3.up * Random.Range(-0.5f, 0.5f);
                            _curlAccel = wob.normalized * knuckleMag;
                            _curlRemaining = SimConfig.AssistDuration + 0.5f;
                            Rb.angularVelocity = Vector3.zero;   // knuckle = no spin
                        }
                        else
                        {
                            // CHIP: high scoop, soft forward pace, backspin (opposite sense of
                            // top spin) so it lofts up and settles.
                            vy   = launch * SimConfig.SetPieceChipLoft;
                            flat = shotDir * (launch * SimConfig.SetPieceChipPaceMul);
                            Rb.angularVelocity = shotRight * (-curlMag * SimConfig.SetPieceTopSpinMul);
                        }
                    }
                    // else: struck dead-centre -> a clean, straight driven shot (curl cleared).

                    // ---- Blend the open-loop launch toward a ballistic solve that REACHES the
                    // 3D corner, by the skill stat. At combined=1 the trajectory is fully
                    // determined by the target (power no longer causes flyover); at combined~0 it
                    // stays the raw struck shot. Preserves the struck flat SPEED so a knuckle/chip
                    // still reads as fast/soft; only the DIRECTION + launch height are guided.
                    {
                        float gMag = Mathf.Abs(Physics.gravity.y);
                        Vector3 rawFlat = flat;
                        float flatSpeed = Mathf.Max(1f, rawFlat.magnitude);

                        // Guided horizontal DIRECTION toward the corner (keep the raw SPEED).
                        Vector3 toTargetFlat = _assistTarget - Rb.position; toTargetFlat.y = 0f;
                        Vector3 guidedDir = toTargetFlat.sqrMagnitude > 0.01f ? toTargetFlat.normalized : shotDir;
                        Vector3 blendedDir = Vector3.Slerp(rawFlat.sqrMagnitude > 0.01f ? rawFlat.normalized : shotDir,
                                                           guidedDir, combined);
                        flat = blendedDir * flatSpeed;

                        // Solve vy for the ACTUAL horizontal flight time at that speed, so the ball
                        // is at the corner HEIGHT when it crosses the goal line (self-consistent for
                        // any launch speed / goal distance). vy_solve = dy/t + 0.5*g*t.
                        float horizDist = new Vector3(toTargetFlat.x, 0f, toTargetFlat.z).magnitude;
                        float tActual = Mathf.Clamp(horizDist / flatSpeed, 0.2f, 2.5f);
                        float vySolve = (_assistTarget.y - Rb.position.y) / tActual + 0.5f * gMag * tActual;
                        vy = Mathf.Lerp(vy, vySolve, combined);
                    }

                    // ---- HARD VERTICAL CEILING (binds on EVERY set-piece shot) ----
                    // Power must never be a vertical driver: cap vy so the ballistic apex can
                    // clear the crossbar by at most SetPieceApexMargin. apex = vy^2 / (2|g|) above
                    // the launch point; allow up to (GoalHeight - launchY + margin). A miss can
                    // still go left/right, but it stays near goal height - never skyrockets.
                    {
                        float gMag = Mathf.Abs(Physics.gravity.y);
                        float allowedApex = Mathf.Max(0.3f, SimConfig.GoalHeight - Rb.position.y + SimConfig.SetPieceApexMargin);
                        float vyMax = Mathf.Sqrt(2f * gMag * allowedApex);
                        if (vy > vyMax) vy = vyMax;
                    }
                }
                else
                {
                    // Normal open-play strike: amplify the ball's existing horizontal velocity
                    // (scaled by the striker's shot-power trait), clear any curl carried from the
                    // serve, and damp the spin so a struck shot flies mostly straight.
                    flat = new Vector3(v.x, 0f, v.z);
                    flat = Vector3.ClampMagnitude(flat * SimConfig.StrikeHorizBoost * shotMul,
                                                  SimConfig.StrikeHorizMax * shotMul * capMul);
                    vy = v.y;
                    _curlAccel = Vector3.zero;
                    _curlRemaining = 0f;
                    Rb.angularVelocity *= 0.2f;
                }
                Rb.linearVelocity = new Vector3(flat.x, vy, flat.z);
            }

            _assistRemaining = SimConfig.AssistDuration;
            _assistCooldown = 0.4f;   // don't re-trigger every micro-contact
            // Set-piece accuracy (skill-only combined stat), the 3D corner target (_assistTarget),
            // the ballistic launch blend, and the hard vertical ceiling are all set INSIDE the
            // set-piece launch block above (they must run before Rb.linearVelocity is assigned so
            // the guided flat/vy take effect). ApplyGoalAssist then steers toward _assistTarget
            // (now including height) over the window. Open-play/header shots keep their per-part
            // _accuracyMul and centre aim set earlier.

            // Auto ball-cam: a dead trap already returned above, so this is a real strike
            // (foot shot or header). Cut to ball-cam ONLY for a shot taken facing AWAY from
            // goal (bicycle / over-shoulder) with real pace - never for a forward or side-on
            // shot the striker can already watch.
            if (_cam != null && camShouldCut)
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
        public void DribbleShot(Vector3 dir, float speed, bool facingGoal, bool camShouldCut)
        {
            Rb.linearVelocity = dir * speed;
            Rb.angularVelocity = Vector3.zero;
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;

            // Assist uses the tight cone.
            if (facingGoal)
            {
                // Strong-foot-style accuracy plus the Shooting/Control accuracy nodes.
                _accuracyMul = SimConfig.StrongFootAccuracy + (PlayerProfile.ShotAccuracyMul - 1f);
                _assistRemaining = SimConfig.AssistDuration;
            }
            else _accuracyMul = 0f;

            // Ball-cam cut ONLY for a shot facing away from goal (rare on a dribble shot).
            if (camShouldCut && _cam != null)
            {
                Vector3 flat = dir * speed; flat.y = 0f;
                if (flat.magnitude >= SimConfig.ShotCamMinSpeed) _cam.PulseBallCam(SimConfig.ShotCamSeconds);
            }

            _assistCooldown = 0.4f;
            SuppressStrike(SimConfig.DribbleRecaptureCooldown);
        }

        // Generic kick/pass: set the ball's velocity directly and clear curl/spin. Used by
        // AI footballers and the passing system (no aim assist - AI/passes aim themselves).
        // Suppresses the strike/dribble hooks briefly so the kicking foot doesn't re-hit it.
        public void KickTo(Vector3 velocity)
        {
            Rb.linearVelocity = velocity;
            Rb.angularVelocity = Vector3.zero;
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;
            _assistRemaining = 0f;
            _accuracyMul = 1f;
            SuppressStrike(0.3f);
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
            _assistFlatOff = false;
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
