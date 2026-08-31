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

        // Scripted knuckle (S) air wiggle: an OSCILLATING lateral force (as opposed to the constant
        // _curlAccel) so the ball snakes side to side in flight. _wiggleDir is the horizontal axis to
        // wiggle along, _wiggleAmp the peak accel (scaled by shot power at launch), _wiggleFreq the
        // rate, _wiggleElapsed the running clock, _wiggleRemaining the time left. Cleared on every
        // launch/reset so a knuckle can never bleed into the next shot.
        Vector3 _wiggleDir;
        float _wiggleAmp;
        float _wiggleFreq;
        float _wiggleElapsed;
        float _wiggleRemaining;

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
        bool _assistVertOff;       // OVERPOWERED set-piece: skip the VERTICAL steer so the intended
                                   // over-the-bar loft is not predicted back down onto goal height.
        float _bikeCamCooldown;    // guard so one bicycle flip cuts to ball-cam only once
        float _kickSfxCooldown;    // guard so one contact (several bones) plays only one kick thud
        float _postSfxCooldown;    // same, for the woodwork: a deflection can clip the post and the
                                   // bar within a frame or two, which should be one clang not three

        // Camera to pulse into ball-cam on a genuine shot (optional; null in modes that
        // don't want it). Set by the mode builder.
        GameCamera _cam;
        public void SetCamera(GameCamera cam) => _cam = cam;

        // Dribble hand-off, scoped to the CARRIER'S OWN BODY. While someone carries, THAT body's
        // contacts skip the strike/trap logic, because its gait swings real legs through the ball
        // and those taps must not become shots. Every OTHER body strikes normally, which is what
        // lets a defender poke, shoot or volley the ball straight off a carrier's feet.
        //
        // This used to be a plain global bool plus a global strike suppression re-armed on EVERY
        // dribble touch. That deadened shooting and volleying for everyone while anyone (a bot
        // included) had the ball, and kept them dead for up to two thirds of a second AFTER
        // possession was lost - so tackling a carrier and shooting the loose ball did nothing.
        public ActiveRagdoll DribbleCarrier { get; private set; }

        /// <summary>Is any body carrying the ball right now.</summary>
        public bool DribbleHold => DribbleCarrier != null;

        /// <summary>Claim carry ownership of the ball, or clear it with null.</summary>
        public void SetDribbleCarrier(ActiveRagdoll carrier) => DribbleCarrier = carrier;

        float _strikeSuppress;                   // >0: skip striker strike logic (post-shot settle)
        public void SuppressStrike(float t) => _strikeSuppress = Mathf.Max(_strikeSuppress, t);

        // Post-launch settle scoped to the body that JUST launched the ball. A kicking foot must
        // not re-strike its own shot or pass, but that is the ONLY body that needs holding off.
        // Suppressing globally (the old behaviour) also killed the rebound volley, the block and
        // the first-time shot for EVERY other player for up to half a second after every shot,
        // pass and tackle. Set pieces still use the global window on purpose.
        ActiveRagdoll _selfSuppressBody;
        float _selfSuppress;

        /// <summary>Block strikes from ONE body for t seconds. A null body falls back to global.</summary>
        public void SuppressStrikeFor(ActiveRagdoll body, float t)
        {
            if (body == null) { SuppressStrike(t); return; }
            _selfSuppressBody = body;   // newest launcher wins; only one foot is ever mid-follow-through
            _selfSuppress = t;
        }

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

        // Match: while true, a DELIBERATE LMB/RMB shot (dribble release + AI shot) launches
        // AIRBORNE like a set piece - an arced ballistic shot instead of a flat drive - but with
        // NO controllable spin (WASD is movement in match). Set by MatchGame for the
        // session; loose-ball trapping / open-play contacts are unaffected (they stay grounded).
        public bool MatchLoftKicks { get; set; }

        // NO-CARRY modes (Striker, Freeplay, Time Trial): no Dribble is enabled anywhere, so a
        // dead touch has nothing to hand the ball to and used to leave it resting between the
        // striker's boots. While true a dead touch is pushed clear of the body instead (see the
        // deadTrap branch in OnCollisionEnter). Set by GameBootstrap / NetStrikerMatch.
        public bool NoCarry { get; set; }

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
            // Arm the post-bonus ceiling clamp (see FixedUpdate). Three steps, because the bonus is a
            // VelocityChange added from a collision callback: callbacks fire after that step has already
            // integrated, so the impulse lands on the NEXT integration and is first visible to the
            // FixedUpdate after that. One step would clamp a velocity that has not been boosted yet.
            _trickCeilTimer = Time.fixedDeltaTime * 3f;
            return true;
        }

        // Countdown for the post-trick-bonus speed clamp.
        float _trickCeilTimer;

        // Keep a bicycle UNDER the bar.
        //
        // Trading vertical for goalward pace (SimConfig.BicycleVKeep*/BicycleBonusLift*) lowers the
        // average bike but does not bound the worst one, because the launch is a solve plus an
        // AddForce bonus on top and a steep contact can still clear the crossbar. So this is the
        // last word, and it is geometric rather than statistical: solve the flight time to the
        // goal-line plane the ball is actually heading at, then cap the rise so it arrives at most
        // BicycleBarClear under the bar. A shot already on target is untouched - vy only ever comes
        // down - and drag lengthens the real flight, which lands the ball lower than the vacuum
        // solve says, so the error is always in the wanted direction.
        //
        // It lives here rather than in the strike solve for the same reason the ceiling clamp above
        // does: the trick bonus is a VelocityChange applied from a collision callback AFTER the
        // strike, so capping earlier would cap a number that is about to have lift added to it.
        // Re-running it on each of the three window steps costs nothing and cannot over-tighten:
        // once the velocity is ON the capped parabola, every later point of that same parabola
        // solves back to the velocity it already has.
        //
        // GoalHeight and AttackGoalCenter are the live statics match setup writes, so a rescaled or
        // relocated goal is handled with no extra work, and Sign(v.z) means it serves either end of a
        // match pitch. NOT SimConfig.GoalCenter: that one is readonly at FieldLength*0.5 = 17 m,
        // which is nowhere near a match goal line (24, 34 or 52 m out).
        void CapUnderCrossbar()
        {
            Vector3 v = Rb.linearVelocity;
            if (v.y <= 0f) return;                        // already falling
            if (Mathf.Abs(v.z) < 1f) return;              // not travelling at either goal

            float goalZ = Mathf.Sign(v.z) * Mathf.Abs(SimConfig.AttackGoalCenter.z);
            float t = (goalZ - Rb.position.z) / v.z;      // time to the goal-line plane
            if (t <= 0.05f) return;                       // on top of the line, or already past it

            float g     = Mathf.Abs(Physics.gravity.y);
            float want  = Mathf.Max(0.3f, SimConfig.GoalHeight - SimConfig.BicycleBarClear);
            float vyMax = (want - Rb.position.y + 0.5f * g * t * t) / t;
            if (v.y <= vyMax) return;

            v.y = Mathf.Max(0f, vyMax);
            Rb.linearVelocity = v;
        }

        // The fastest a struck ball may ever leave: the open-play strike ceiling at FULL swing, on
        // the current build, clamped to the best human. Same three terms the strike path builds its
        // own shotCeil from, before the per-contact factors are folded in.
        //
        // Exists because the bicycle bonus (KickDetector.ValidHitBonus) is a VelocityChange added
        // after the strike has already been clamped, so it was the one power source in the game that
        // sat outside every ceiling. It cost a human nothing, since a bike's post-solve strike lands
        // below the cap and the bonus filled real headroom, but a quadruped's front-leg bounce arrives
        // AT the cap and the bonus stacked on top of the maximum. Clamping there against THIS instead
        // of the local shotCeil is deliberate: shotCeil carries the swing-speed and weak-foot factors,
        // both of which are <= 1, so a soft-swung bike would lose its whole bonus to the clamp.
        public static float StrikeSpeedCeiling
            => SimConfig.StrikeHorizMax
               * Mathf.Min(PlayerProfile.ShotPowerMul, PlayerProfile.HumanShotPowerMax)
               * (PlayerProfile.PerkCannon ? SimConfig.CannonCapMul : 1f);

        // ---- Body-touch log: every ball-vs-player contact, newest last, written from
        // OnCollisionEnter (real PhysX contacts, and the ball is ContinuousDynamic so nothing
        // tunnels). Modes read this to decide SAVE vs MISS. They used to poll bone-to-ball distance
        // once a frame, which blinks past a fast shot (30 m/s covers half a metre between frames),
        // or guess from where the ball came to rest, which calls a parried-clear save a miss.
        struct BodyTouch { public ActiveRagdoll body; public float time; public float speed; }
        readonly BodyTouch[] _touchLog = new BodyTouch[8];
        int _touchNext;

        // Impact speed of the newest contact with `body` at or after `since`, or false if none.
        // Ring order does not matter: take the newest qualifying entry.
        public bool BodyTouchedSince(ActiveRagdoll body, float since, out float impactSpeed, out float touchTime)
        {
            impactSpeed = 0f; touchTime = 0f;
            if (body == null) return false;
            bool found = false;
            for (int i = 0; i < _touchLog.Length; i++)
            {
                var t = _touchLog[i];
                if (t.body != body || t.time < since || t.time < touchTime) continue;
                impactSpeed = t.speed; touchTime = t.time; found = true;
            }
            return found;
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
            _wiggleRemaining = 0f;   // a lob/cross never snakes; kill any leftover knuckle wiggle
            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = true;
            _trail.Clear();
        }

        // Spin flavour for a scripted set-piece launch (chosen by the taker's WASD hold).
        public enum SetPieceSpin { None, CurveLeft, CurveRight, TopSpin, Knuckle }

        // SCRIPTED set-piece launch (the taker's power meter + WASD spin). Aim defaults CENTRAL and
        // saveable; the skill stat `combined` (0..1, accuracy-dominant) pulls it toward a goal CORNER,
        // tightens scatter, AND drives the swerve. `power01` (the on-screen bar) picks the pace between
        // the base and max launch speeds; `powerStat01` (the power STAT) only scales that speed CEILING
        // a little, never the height - so the power stat scales up LESS than accuracy and max power can
        // never loft the ball over the bar on a clean shot. Curl is TARGET-RELATIVE: the ball is
        // launched biased to the swing side and curled back so it RETURNS to the aim x (more curve
        // never means wider). A clean vy cap keeps a well-struck shot near goal height; but
        // OVERPOWERING the bar (`overcharge01`, distinct from the sideways-spraying `botch01`) adds
        // UNCAPPED upward velocity ON TOP of the cap, INDEPENDENT of the power stat - so any overpowered
        // bar sails over the bar (a weak-power striker too, just with less forward pace). Body-collider-
        // disabled + SuppressStrike on the caller side keep the aesthetic runup from re-triggering a
        // physical strike, so this fully OWNS the shot.
        public void LaunchSetPiece(float power01, SetPieceSpin spin, float spinCharge01,
                                   float botch01, float combined, Vector3 goalCenter,
                                   float overcharge01 = 0f, float powerStat01 = 0.5f,
                                   Vector3? aimPoint = null)
        {
            power01 = Mathf.Clamp01(power01);
            spinCharge01 = Mathf.Clamp01(spinCharge01);
            botch01 = Mathf.Clamp01(botch01);
            combined = Mathf.Clamp01(combined);
            overcharge01 = Mathf.Clamp01(overcharge01);
            powerStat01 = Mathf.Clamp01(powerStat01);

            float gMag = Mathf.Abs(Physics.gravity.y);
            Vector3 p0 = Rb.position;

            // Pure ACCURACY stat, back-solved from the combined stat the taker passes
            // (combined = 0.8*accuracy + 0.2*power). The offset spray and the over-bar loft key off
            // THIS, never off power, so shot power does not affect where the ball sprays.
            float accOnly = Mathf.Clamp01((combined - 0.2f * powerStat01) / 0.8f);

            // Launch speed from the power BAR, between an empty-bar floor and a stat-scaled ceiling.
            // The power STAT sets the whole band: at 0 stat a FULL bar tops out near SetPieceMinStatSpeed
            // (~10 m/s, a weak lob), at full stat a full bar reaches SetPieceMaxSpeed. The empty end of
            // the bar is always SetPieceLaunchFloorFrac of that ceiling so the bar always has travel.
            // NOTE: SetPieceBaseSpeed is deliberately NOT used here (it is shared with the open-play
            // strike launch); the scripted set-piece speed is rebuilt from the two set-piece constants.
            // Cannon lifts the top of the range. Height is never touched by any of this.
            float capMul = PlayerProfile.PerkCannon ? SimConfig.CannonCapMul : 1f;
            // SetPieceSpeedCeilMul lifts the TOP of the stat band only: at 0 power stat the ceiling is
            // still SetPieceMinStatSpeed, so the stat scaling is the same lerp it always was.
            float statCeil = Mathf.Lerp(SimConfig.SetPieceMinStatSpeed,
                                        SimConfig.SetPieceMaxSpeed * capMul * SimConfig.SetPieceSpeedCeilMul,
                                        powerStat01);
            float launch = Mathf.Lerp(statCeil * SimConfig.SetPieceLaunchFloorFrac, statCeil, power01);

            // Goal-ward flat direction (fall back to +Z toward goal).
            Vector3 toGoal = goalCenter - p0; toGoal.y = 0f;
            Vector3 shotDir = toGoal.sqrMagnitude > 0.01f ? toGoal.normalized : Vector3.forward;
            Vector3 shotRight = Vector3.Cross(Vector3.up, shotDir);

            // Aim source. The look-ray drivers (SP FreeKickGame / MP NetSetPieceMatch) pass an
            // explicit aim point from the camera; the AI / auto path passes null and keeps the
            // existing corner auto-aim unchanged.
            Vector3 aim;
            // Set when the look aim is egregiously off the goal-ward line (outside the aim cone):
            // the shot is forced wide and ALL goal-ward help (curl-return, horizontal + vertical
            // steer) is cut below so nothing drags it back into a corner. Look-ray path only.
            bool forceOffTarget = false;
            if (aimPoint.HasValue)
            {
                aim = aimPoint.Value;
                // accuracy scatter: low accuracy sprays wide, high accuracy is tight. Keyed to the
                // ACCURACY stat ONLY (accOnly) so shot power never changes the spray.
                float scatterAmt = SimConfig.SetPieceLookScatterMax * (1f - accOnly);
                aim += new Vector3(Random.Range(-scatterAmt, scatterAmt),
                                   Random.Range(-scatterAmt, scatterAmt), 0f);
                aim.y = Mathf.Max(0.05f, aim.y);

                // AIM CONE: how far the aim ray sits off the ball->goal line, on the flat. Beyond the
                // cone half-angle (looking egregiously to the side) the shot is forced off target
                // regardless of accuracy - the corrective steer/curl are disabled below and the aim is
                // shoved further out so it clears the post. Inside the cone, accuracy is untouched.
                Vector3 aimFlat = aim - p0; aimFlat.y = 0f;
                if (aimFlat.sqrMagnitude > 0.01f)
                {
                    Vector3 aimFlatDir = aimFlat.normalized;
                    if (Vector3.Angle(shotDir, aimFlatDir) > SimConfig.SetPieceAimConeHalfAngle)
                    {
                        forceOffTarget = true;
                        float side = Vector3.Dot(aimFlatDir, shotRight) >= 0f ? 1f : -1f;
                        aim += shotRight * (side * SimConfig.SetPieceOffTargetPush);
                    }
                }

                // aim.y (from camera pitch) OWNS launch height. Do NOT reintroduce power-based height.
                _assistTarget = aim;
            }
            else
            {
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

                aim = goalCenter + shotRight * aimX + Vector3.up * (aimY - goalCenter.y);
                _assistTarget = aim;
            }

            // Flat launch DIRECTION toward the aim; keep the power-picked flat SPEED.
            Vector3 toAimFlat = aim - p0; toAimFlat.y = 0f;
            float horizDist = toAimFlat.magnitude;
            Vector3 flatDir = horizDist > 0.01f ? (toAimFlat / horizDist) : shotDir;
            float flatSpeed = Mathf.Max(1f, launch);

            // Solve vy so the ball is at the aim HEIGHT when it crosses the goal line, then cap the
            // apex so the power STAT / a clean bar can never send it over the bar.
            float tActual = Mathf.Clamp(horizDist / flatSpeed, 0.2f, 2.5f);
            float vy = (aim.y - p0.y) / tActual + 0.5f * gMag * tActual;
            // The cap is what a steep camera angle runs into, so SetPieceApexCeilMul is the height
            // ceiling: aiming higher keeps buying height for longer before it clips. Pitch still owns
            // where inside that range the shot lands (vy is solved from aim.y above, untouched).
            float allowedApex = Mathf.Max(0.3f, (SimConfig.GoalHeight - p0.y + SimConfig.SetPieceApexMargin)
                                                * SimConfig.SetPieceApexCeilMul);
            float vyMax = Mathf.Sqrt(2f * gMag * allowedApex);
            if (vy > vyMax) vy = vyMax;

            // OVER-THE-BAR LOFT: extra upward velocity ON TOP of the clean apex cap, so a shot can
            // balloon over the crossbar. Driven by shot POWER up and ACCURACY down: a high-power shot
            // with LOW accuracy sails well over the bar; investing accuracy pulls the loft down; at
            // MAX accuracy it is ZERO, so the ball caps right at the crossbar. A small overcharge bonus
            // adds on top so over-holding the bar still sails a touch higher.
            float loft = 0f;
            {
                // Gate the power-driven loft to the HIGH-RED end of the bar: below SetPieceLoftGate it
                // is ZERO (the shot just travels slow + low, its pace owned by the speed band above), and
                // from the gate to a full bar it ramps up to the SAME loft it had at full power before.
                // So a low-accuracy player only balloons the ball when they deliberately hold the bar up
                // into the red, not on a mid-bar shot. The overcharge bonus is unchanged (max-only).
                float loftGate = Mathf.InverseLerp(SimConfig.SetPieceLoftGate, 1f, power01);
                loft = loftGate * (1f - accOnly) * SimConfig.SetPieceLoftVy
                       + overcharge01 * SimConfig.SetPieceOverchargeVy;
                vy += loft;
            }

            // TARGET-RELATIVE curl for a curve shot: bias the launch OUT to the swing side and set a
            // lateral curl that brings it back so it arrives on the aim x (a banana that returns).
            // Lateral offset w, curl accel -2w/t^2 over the flight => net sideways displacement 0.
            //
            // Swerve is driven PRIMARILY by the accuracy stat: this factor scales the curl magnitude
            // from a small floor (raw striker) up to full bend (fully invested), with the WASD hold
            // only modulating within that. The cosmetic ball-spin (angularVelocity) stays keyed to
            // the WASD charge so the visual roll still reads.
            float swerve = (SimConfig.SetPieceCurlAccFloor
                            + (1f - SimConfig.SetPieceCurlAccFloor) * combined)
                           * (0.5f + spinCharge01);
            _curlAccel = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            _curlRemaining = 0f;
            _wiggleRemaining = 0f;
            _wiggleAmp = 0f;
            _wiggleElapsed = 0f;
            // A shot forced off target (aim outside the cone) gets NO curl/spin steering: it must fly
            // straight to the shoved-wide aim and miss, so a curve can't return it to a corner.
            if (!forceOffTarget && (spin == SetPieceSpin.CurveLeft || spin == SetPieceSpin.CurveRight))
            {
                float side = spin == SetPieceSpin.CurveRight ? 1f : -1f;
                // SetPieceCurveMaxMul raises the A/D bend ceiling. It scales the out-bias AND the
                // return accel (which is derived from w), so the banana arc gets wider while still
                // netting zero sideways displacement - more curve never means a wider miss. Accuracy
                // and the WASD hold scale into it exactly as before via `swerve`. W/S are untouched.
                float w = SimConfig.SetPieceCurl * swerve * 0.5f * SimConfig.SetPieceCurveMaxMul;   // out-speed, accuracy-driven
                flatDir = (flatDir * flatSpeed + shotRight * (side * w)).normalized;   // launch angled out
                _curlAccel = shotRight * (-side * 2f * w / Mathf.Max(0.1f, tActual)); // curl back to aim
                _curlRemaining = tActual;
                // Cosmetic roll follows the bend so the visual spin still reads at the new ceiling.
                Rb.angularVelocity = Vector3.up * (side * SimConfig.SetPieceCurl * SimConfig.SetPieceCurveMaxMul * spinCharge01);
            }
            else if (!forceOffTarget && spin == SetPieceSpin.TopSpin)
            {
                // Dips: downward curl + forward roll, accuracy-driven.
                _curlAccel = Vector3.down * (SimConfig.SetPieceCurl * SimConfig.SetPieceTopSpinMul * swerve);
                _curlRemaining = tActual;
                Rb.angularVelocity = shotRight * (SimConfig.SetPieceCurl * spinCharge01);
            }
            else if (!forceOffTarget && spin == SetPieceSpin.Knuckle)
            {
                // Pronounced side-to-side AIR WIGGLE with no spin (keeper-fooling). An OSCILLATING
                // lateral force (see FixedUpdate) snakes the ball left-right over the flight. HORIZONTAL
                // only: a vertical component would add height after the apex cap and could clear the
                // bar, so the wiggle is purely sideways and the vy cap alone owns the height. Amplitude
                // scales LINEARLY with shot POWER - a weak knuckle barely wobbles, a full-power one
                // snakes hard. A random phase kick starts it swinging either direction.
                _wiggleDir = shotRight;
                _wiggleAmp = SimConfig.SetPieceWiggleAmp * power01;
                _wiggleFreq = SimConfig.SetPieceWiggleFreq;
                _wiggleElapsed = Random.Range(0f, Mathf.PI * 2f);   // random starting phase
                _wiggleRemaining = tActual;
                _curlAccel = Vector3.zero;
                _curlRemaining = 0f;
                Rb.angularVelocity = Vector3.zero;
            }

            Vector3 flat = flatDir * flatSpeed;
            Rb.linearVelocity = new Vector3(flat.x, vy, flat.z);

            // Assist window: steer toward the 3D aim over the flight. For a CURVE shot the curl
            // already lands the ball on the aim x (out then back), so skip the HORIZONTAL steer
            // (it would flatten the banana); the vertical steer still pulls the height onto target.
            // CURVE and KNUCKLE both own their horizontal path (the banana returns to aim x; the
            // knuckle intentionally snakes), so skip the HORIZONTAL steer for both or it flattens them.
            // NO SPIN = an honest straight shot: it flies exactly along the aim ray with NO goal-ward
            // help at all. Previously a plain shot still got the horizontal steer, so looking off to
            // the side and shooting "straight" was dragged back onto the goal - the ball never went
            // where you actually aimed. Now only a curve/knuckle (which own their own path) and a
            // plain shot skip the flat steer; the difference is that a plain shot ALSO skips the
            // vertical steer below, so nothing bends it off the ray you picked.
            bool noSpin = spin == SetPieceSpin.None;
            _assistFlatOff = noSpin
                             || spin == SetPieceSpin.CurveLeft || spin == SetPieceSpin.CurveRight
                             || spin == SetPieceSpin.Knuckle;
            // Any real over-bar loft: turn OFF the vertical steer so the loft survives the flight
            // (otherwise ApplyGoalAssist predicts the lofted ball straight back down onto aim height).
            // A no-spin shot also drops the VERTICAL steer, so the whole flight is pure ballistics
            // along the aim ray (the launch solve already puts it at the aimed height on the goal
            // plane; steering after that would just re-target it).
            _assistVertOff = loft > 0.2f || noSpin;
            // Forced off target (aim outside the cone): cut BOTH steers so no goal-ward help can pull
            // the shot back onto the frame. The ball follows its ballistic path to the shoved-wide aim.
            if (forceOffTarget) { _assistFlatOff = true; _assistVertOff = true; }
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
            // Post-bonus ceiling. The bicycle bonus is added to the ball AFTER the strike has been
            // clamped, so it was the one power source that could push a shot past the game's ceiling.
            // It has to be enforced here rather than at the AddForce: the impulse is not in the
            // velocity yet at that point, and the ball's own strike callback may write velocity either
            // before or after the detector's callback runs. Reading it a couple of steps later is the
            // only place the final number exists, whatever the order was.
            //
            // What this equalizes: a human's bike leaves the boot below the ceiling, so the bonus fills
            // real headroom and mostly survives; a quadruped's front-leg contact already arrives AT the
            // ceiling, so its bonus was landing on top of the maximum. Both now top out at the same
            // speed, and it is the human-clamped one.
            if (_trickCeilTimer > 0f)
            {
                _trickCeilTimer -= Time.fixedDeltaTime;
                Rb.linearVelocity = Vector3.ClampMagnitude(Rb.linearVelocity, StrikeSpeedCeiling);
                CapUnderCrossbar();
            }

            if (_curlRemaining > 0f)
            {
                Rb.AddForce(_curlAccel, ForceMode.Acceleration);
                _curlRemaining -= Time.fixedDeltaTime;
            }

            // Knuckle (S) air wiggle: an oscillating sideways accel so the ball snakes left-right in
            // flight. Amplitude was scaled by shot power at launch; a sine of the running clock swings
            // it both directions. Purely horizontal so it never adds height past the apex cap.
            if (_wiggleRemaining > 0f)
            {
                _wiggleElapsed += Time.fixedDeltaTime * _wiggleFreq;
                Rb.AddForce(_wiggleDir * (_wiggleAmp * Mathf.Sin(_wiggleElapsed)), ForceMode.Acceleration);
                _wiggleRemaining -= Time.fixedDeltaTime;
            }

            if (_assistCooldown > 0f) _assistCooldown -= Time.fixedDeltaTime;
            if (_strikeSuppress > 0f) _strikeSuppress -= Time.fixedDeltaTime;
            if (_selfSuppress > 0f)
            {
                _selfSuppress -= Time.fixedDeltaTime;
                if (_selfSuppress <= 0f) _selfSuppressBody = null;
            }
            if (_bikeCamCooldown > 0f) _bikeCamCooldown -= Time.fixedDeltaTime;
            if (_kickSfxCooldown > 0f) _kickSfxCooldown -= Time.fixedDeltaTime;
            if (_postSfxCooldown > 0f) _postSfxCooldown -= Time.fixedDeltaTime;
            if (_trickBonusCooldown > 0f) _trickBonusCooldown -= Time.fixedDeltaTime;

            if (_assistRemaining > 0f)
            {
                _assistRemaining -= Time.fixedDeltaTime;
                ApplyGoalAssist();
                if (_assistRemaining <= 0f) _accuracyMul = 1f;
            }

            ApplyRollingResistance();
        }

        // ROLLING RESISTANCE - the one thing missing from a ball on the turf.
        //
        // Coulomb friction cannot slow a rolling sphere: once it rolls without slipping the contact
        // patch has zero relative velocity, so the ball/turf averaged friction (0.225 on the training
        // pitch, 0.4 in match) produces no decelerating force, and PhysX has no rolling-friction
        // coefficient for a sphere. That left Rigidbody damping as the only damper, and both terms are
        // 0.02 - about 2% per second - so a loose ball rolled the length of the pitch at very nearly the
        // speed it was struck and a keeper closing at StrikerMoveSpeed could never catch it.
        //
        // Deliberately NOT fixed by raising BallDrag. Every flight path in the game is a vacuum solve
        // (LaunchTo, LaunchSetPiece, the set-piece/volley ballistic blend, CapUnderCrossbar,
        // ApplyGoalAssist's vertical predictor, Goalkeeper's dive prediction), so global drag would
        // land every cross, chip pass, free kick and penalty short of where it was solved to.
        //
        // Four gates, so this only ever bites on a ball that is actually ROLLING:
        //   - grounded: centre at or below VolleyMinBallHeight, the same "off the turf" line the volley
        //     gate uses. A ball in flight sits above it.
        //   - not bouncing: |vy| within BallRollMaxVy. A low skimming bounce keeps its pace.
        //   - loose, not struck: flat speed within BallRollSpeed, far below StrikeHorizMax and
        //     DribbleShotSpeed, so no shot or lofted set piece is ever damped at launch.
        //   - no live assist window: a guided shot is still in flight and owns its own velocity.
        // Kinematic guard because the MP client ball and a replay body are kinematic and must not be
        // written; the rest of FixedUpdate's timers still tick for them.
        void ApplyRollingResistance()
        {
            if (Rb.isKinematic) return;
            if (_assistRemaining > 0f) return;
            if (Rb.position.y > SimConfig.VolleyMinBallHeight) return;

            Vector3 v = Rb.linearVelocity;
            if (Mathf.Abs(v.y) > SimConfig.BallRollMaxVy) return;

            Vector3 flat = new Vector3(v.x, 0f, v.z);
            float speed = flat.magnitude;
            if (speed > SimConfig.BallRollSpeed) return;

            // Kill the last of it rather than letting the ball creep for seconds. Vertical is left
            // alone so a settling ball still sinks onto the turf normally.
            if (speed <= SimConfig.BallRollStop)
            {
                Rb.linearVelocity = new Vector3(0f, v.y, 0f);
                Rb.angularVelocity *= 0.5f;
                return;
            }

            // Constant deceleration, which is what rolling resistance actually is, so the ball stops
            // instead of asymptoting. One step at 50Hz removes BallRollDecel*0.02 = 0.064 m/s, well
            // under BallRollStop, so this can never push the roll into reverse.
            Rb.AddForce(flat / speed * -SimConfig.BallRollDecel, ForceMode.Acceleration);
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
            // Skipped when the bar was OVERPOWERED (_assistVertOff): the launch added an intentional
            // over-the-bar loft, and steering toward goal height would predict it straight back down.
            if (_assistTarget.y > 0.05f && !_assistVertOff)
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
            // Knuckle (S) wiggle is a flight-only trick: the instant the ball touches ANYTHING
            // (ground, goal frame, keeper, wall, post), stop snaking and bounce normally. Done
            // first, before any early-return below, so every contact type kills it.
            _wiggleRemaining = 0f;

            // Net backstop: kill the rebound in code (material combine can't beat the
            // ball's own Maximum bounce). Keep a little velocity so it slides down.
            if (c.collider.GetComponentInParent<NetBackstop>() != null)
            {
                Rb.linearVelocity *= 0.12f;
                Rb.angularVelocity *= 0.3f;
                return;
            }

            // Woodwork: clang, then let it bounce. Nothing here touches velocity - the frame's own
            // bouncy material already gives the deflection. Broadcast as well as played, because a
            // client's ball is kinematic and never generates this collision locally.
            if (c.collider.GetComponentInParent<GoalFrame>() != null)
            {
                if (_postSfxCooldown <= 0f)
                {
                    _postSfxCooldown = 0.09f;
                    Vector3 postPos = c.contactCount > 0 ? c.GetContact(0).point : Rb.position;
                    float postSpeed = c.relativeVelocity.magnitude;
                    AudioManager.Instance?.PlayPostHit(postPos, postSpeed);
                    if (Trickshot.Net.Multiplayer.IsHost)
                        Trickshot.Net.Multiplayer.Session.BroadcastPostHit(postPos, postSpeed);
                }
                return;
            }

            // Was this a striker limb? (KickDetector lives on limbs, or any ActiveRagdoll bone.)
            var ragdoll = c.collider.GetComponentInParent<ActiveRagdoll>();
            if (ragdoll == null) return;

            // Log the contact before any of the striker-only early-returns below, so a keeper /
            // defender touch is recorded even though none of the strike logic runs for it.
            // relativeVelocity is the IMPACT speed (OnCollisionEnter runs after the solver, so
            // Rb.linearVelocity is already post-bounce) - that is what the EPIC SAVE gate wants.
            _touchLog[_touchNext] = new BodyTouch { body = ragdoll, time = Time.time,
                                                    speed = c.relativeVelocity.magnitude };
            _touchNext = (_touchNext + 1) % _touchLog.Length;

            // Ball hit a PLAYER body (any keeper/striker/footballer) -> 3D kick thud at the contact
            // point. One per contact (cooldown swallows the extra bones of a single touch). In MP the
            // client ball is kinematic so this only fires on host/SP; the host also broadcasts the
            // position so every client plays it spatialised to their own player (10 m rolloff).
            if (_kickSfxCooldown <= 0f)
            {
                _kickSfxCooldown = 0.08f;
                Vector3 hitPos = c.contactCount > 0 ? c.GetContact(0).point : Rb.position;
                AudioManager.Instance?.PlayBallKick(hitPos);
                if (Trickshot.Net.Multiplayer.IsHost) Trickshot.Net.Multiplayer.Session.BroadcastBallKick(hitPos);
            }

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

            // The CARRIER'S OWN contacts are not strikes: the touch model owns the ball for that
            // body, and its gait swings physics legs through it every stride. Anyone ELSE hitting
            // the ball IS a genuine strike - a tackle, a poke, a first-time shot, a volley - so it
            // falls straight through to the logic below. Only the brief post-shot suppression is
            // global, and that exists to stop a launching foot re-hitting its own shot.
            if (ragdoll == DribbleCarrier || _strikeSuppress > 0f) return;

            // The body that just launched the ball cannot re-strike its own shot/pass for a moment.
            // Scoped to THAT body, so a rebound off the keeper is still volleyable by anyone else.
            if (_selfSuppress > 0f && ragdoll == _selfSuppressBody) return;

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
            // OWN transform, not its parent (parent is the next bone up the chain).
            //
            // Resolve WHICH ROLE it plays from the BONE, not from the object's name. The quadruped
            // repose keeps all 13 bone names and changes what they ARE, so a name prefix lies: a
            // horse kicks with the UpperArm/Forearm bones, which no leg-shaped prefix matches.
            // Null = not one of the body's parts (a keeper glove hitbox), which classifies as a
            // body touch exactly as an unrecognised name did.
            //
            // The name is still read, but only for the L/R suffix, which is honest under either
            // plan (the left front leg is "P_ForearmL" just as the left foot is "P_FootL").
            string part = c.collider.transform.name;   // e.g. "P_Head", "P_FootR", "P_Torso"
            Bone? struck = ragdoll.BoneOf(c.collider.transform);
            bool header = struck == Bone.Head;
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
            //
            // CLAMPED to the best a human can reach, because every ceiling downstream of here is
            // RELATIVE to shotMul and so scales with the very build it is meant to bound: a species
            // whose build traits or species-gated nodes push ShotPowerMul past a human's best would
            // otherwise raise its own top speed with it. The clamp cannot touch a human by
            // construction (it is their own maximum) and only binds on a species that exceeds it, so
            // a light animal is not held to a light human's output.
            float shotMul = Mathf.Min(PlayerProfile.ShotPowerMul, PlayerProfile.HumanShotPowerMax);
            // Cannon capstone raises the speed ceiling so shots can fly much faster.
            float capMul = PlayerProfile.PerkCannon ? SimConfig.CannonCapMul : 1f;

            // Per-part accuracy + power. A LEG/FOOT is a real strike (full on the strong
            // side, weak + less powerful on the other); the HEAD uses heading rules;
            // anything else (torso, arms, pelvis) is a scrappy touch that mostly kills the
            // ball so it drops at the player's feet (a trap), imparting almost no power.
            // A KICKING limb per this body's plan: a biped's thigh/calf/foot, a quadruped's two
            // FRONT legs. The layout owns the set (BodyLayoutDef.LegBones); for a human it is
            // exactly what the old name-prefix test matched.
            bool isLeg = struck.HasValue && ragdoll.IsLegBone(struck.Value);
            // Which SIDE was struck, resolved from the BONE rather than from the collider's name.
            // The name test only ever worked because a bone's part is named "P_<Bone>". Species
            // DECOR breaks that: a hoof or a foot pad is a child object of a bone with its own
            // name, so an unsuffixed left-side appendage would score as the RIGHT (strong) side
            // and the volley gate below would then poll LegRaiseHeld for the wrong leg, so a
            // legitimate volley would never fire. Falls back to the name when the bone is unknown
            // (a keeper glove), which is exactly where the old answer was already correct.
            bool leftSide = struck.HasValue ? IsLeftBone(struck.Value) : part.EndsWith("L");
            bool deadTrap = false;
            bool volley = false;   // flying ball + swinging leg -> free-kick launch (set in the isLeg branch)
            if (header)
            {
                // Heading tree scales accuracy + power. The boost is FULL on an airborne header
                // (a timed jump) and only GroundedHeaderBoostFrac of it when standing, so jumping
                // to head the ball is rewarded. Scale the amount ABOVE the 1.0 baseline, not the
                // whole multiplier, so a grounded header still works - just weaker + less accurate.
                float headAcc = SimConfig.HeaderAccuracyMul * PlayerProfile.HeaderAccuracyMul;
                float headPow = PlayerProfile.HeaderPowerMul;
                if (!ragdoll.IsGrounded)
                {
                    _accuracyMul = headAcc;
                    shotMul *= headPow;
                }
                else
                {
                    float f = SimConfig.GroundedHeaderBoostFrac;
                    _accuracyMul = 1f + (headAcc - 1f) * f;
                    shotMul *= 1f + (headPow - 1f) * f;
                }
                // Same ceiling as the shot, re-applied because headPow just multiplied on top of an
                // already-clamped shotMul. Bound is the product of the two human maxima, so a human
                // maxing both trees is still exactly at their own limit and unaffected.
                shotMul = Mathf.Min(shotMul,
                                    PlayerProfile.HumanShotPowerMax * PlayerProfile.HumanHeaderPowerMax);
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
                // EXCLUDE a set piece as well. A set-piece mode already takes the launch path
                // below via SetPieceShot, and the volley-only tuning (reduced side curl, forward
                // bottom drive, imprecise in-frame aim) must never reach a free kick or penalty.
                // Without this a taker holding a leg button on a ball bouncing off the wall would
                // strike with volley rules mid free kick.
                if (!deadTrap && !bicycleAttempt && !SetPieceShot
                    && Rb.position.y > SimConfig.VolleyMinBallHeight
                    && striker.LegRaiseHeld(leftSide))
                    volley = true;
            }
            else
            {
                // Anything that is neither the head nor a kicking limb on this plan: a torso or
                // pelvis on either body, a biped's arms, a quadruped's HIND legs (which sit behind
                // it under the repose). Kill it. Low accuracy, and treat as a dead trap.
                //
                // Note this branch has no SetPieceShot escape hatch, unlike the leg path above, so
                // whatever lands here traps even on a free kick. That is why the front-leg
                // classification had to be fixed rather than worked around: a quadruped's every
                // hoof contact, free kicks and penalties included, used to arrive here.
                _accuracyMul = SimConfig.BodyAccuracy;
                shotMul *= SimConfig.BodyPowerMul;
                deadTrap = true;
            }

            // A dead touch traps the ball: strip most of its velocity so it drops and
            // settles at the player's feet, then skip the strike amplification entirely.
            // The Control tree's first-touch nodes deaden it further (ball settles closer).
            if (deadTrap)
            {
                // NO-CARRY MODE: there is no dribble to hand the ball to, so trapping it just
                // parked it between his boots and every follow-up swing was point blank. Push the
                // ball AWAY from the pelvis instead, with a floor on the outward speed, and lock
                // this body out briefly so the trailing leg of the same stride cannot re-glue it.
                if (NoCarry)
                {
                    Vector3 away = Rb.position - (ragdoll.Pelvis != null ? ragdoll.Pelvis.position : Rb.position);
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-4f)
                        away = faceFwd.sqrMagnitude > 0.01f ? faceFwd : Vector3.forward;
                    away.y = 0f; away.Normalize();

                    Vector3 keep = Rb.linearVelocity * SimConfig.NoCarryTouchKeep;
                    float outward = keep.x * away.x + keep.z * away.z;
                    if (outward < SimConfig.NoCarryTouchMinSpeed)
                        keep += away * (SimConfig.NoCarryTouchMinSpeed - outward);
                    Rb.linearVelocity = keep;
                    Rb.angularVelocity *= 0.3f;
                    _assistCooldown = 0.25f;
                    SuppressStrikeFor(ragdoll, SimConfig.NoCarryTouchSuppress);
                    return;
                }

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
            _assistVertOff = false;   // and always use the normal vertical steer (only overpowered set-pieces skip it)

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

                // NOTE, since it is the obvious thing to reach for and it does not work: added
                // UPWARD loft is not available to any species, however much an elephant's aid pose
                // looks like it is asking for it.
                //
                //   The redirect SCALES the incoming vertical, and the incoming term spans about
                //   -4 m/s across cross distance and swings 7.5 m/s across the ball-velocity slider,
                //   which is wider than the whole under-bar budget. So a constant added loft is a
                //   scoop at one setting and a spike into the turf at another, and the Aerial perk's
                //   higher keep (0.5 vs 0.35) makes the capstone REDUCE it, which is backwards.
                //
                //   Sizing it as a target apex instead, clamped under the live bar, is well defined
                //   but delivers nothing. The ball must FIT under the bar while touching the animal,
                //   so the contact surface has a hard ceiling of 2.44 - 2*0.11 = 2.22 m, and an
                //   elephant at the top of its Weight slider already heads from about 2.06 m. The
                //   clamp would have almost nothing to aim into. And over the bar is not cosmetic:
                //   the +Z end of the arena has no wall, so a high header scores as a MISS.
                //
                // What IS species-specific is two trims that both point the other way, DOWN and
                // SLOWER, so none of the above applies to them: HeaderAction.PaceMul and .DownDeg,
                // read from the striker's own species rather than the local profile because the body
                // that headed the ball may be a remote peer or an AI. Both are 1 / 0 on a biped, so
                // the human response is untouched to the bit.
                var hdr = Species.ById(ragdoll.SpeciesId).Header;

                float headVy = v.y * vKeep;

                Vector3 flatIn = new Vector3(v.x, 0f, v.z);
                // PACE. `v` is read POST-SOLVE, so flatIn is the bounce off the head and carries the
                // head's own speed into the contact, not just the ball's incoming pace. That is the
                // term a quadruped inflates: see HeaderAction.PaceMul for the two reasons (a head on a
                // twice-as-long lever, and a barrel drive-compensated 14x to 28x that will not give).
                // Zero is treated as unset so a species added without a value keeps its header.
                float inSpeed = flatIn.magnitude * (hdr.PaceMul > 0f ? hdr.PaceMul : 1f);
                // Facing goal: bias toward it (falling back to toGoal if no incoming line).
                // Not facing: keep the ball's own incoming line, or the head's facing dir
                // if it arrived nearly straight down.
                Vector3 dir;
                if (facingGoal)
                    dir = flatIn.sqrMagnitude > 0.01f ? Vector3.Slerp(flatIn.normalized, toGoal, goalBias) : toGoal;
                else
                    dir = flatIn.sqrMagnitude > 0.01f ? flatIn.normalized : faceFwd.normalized;

                float headCeil = SimConfig.StrikeHorizMax * SimConfig.HeaderPowerMul * shotMul * capMul;
                float speed = Mathf.Max(SimConfig.HeaderMinSpeed,
                                        inSpeed * SimConfig.HeaderPowerMul) * shotMul;
                speed = Mathf.Min(speed, headCeil);

                Vector3 flat = dir * speed;
                // Named headOut, not outV: the shot-cam block further down this same method already
                // owns `outV` at the method scope, and C# treats that as covering this nested block.
                Vector3 headOut = new Vector3(flat.x, headVy, flat.z);

                // AIM DOWN. A body that heads from a standing height a person has to jump for sends
                // the ball flat and long from up there, which is how a horse or an elephant ended up
                // clearing everything. Tilt the whole vector down about the axis across its own
                // direction, which PRESERVES SPEED: this re-aims the header, it does not weaken it
                // (PaceMul above is the part that does). Clamped at HeaderMaxDiveDeg so a ball that
                // arrived falling steeply is not driven straight into the turf.
                if (hdr.DownDeg > 0f)
                {
                    float mag = headOut.magnitude;
                    if (mag > 0.01f)
                    {
                        float horiz = new Vector2(headOut.x, headOut.z).magnitude;
                        float pitch = Mathf.Atan2(headOut.y, horiz) * Mathf.Rad2Deg;
                        float want  = Mathf.Max(pitch - hdr.DownDeg, -SimConfig.HeaderMaxDiveDeg);
                        // Already steeper than the clamp: leave it alone rather than pulling it UP,
                        // which a plain Max on the target would do to a near-vertical drop.
                        if (want < pitch)
                        {
                            float r = want * Mathf.Deg2Rad;
                            Vector3 fd = horiz > 0.01f ? new Vector3(headOut.x, 0f, headOut.z) / horiz : dir;
                            headOut = fd * (mag * Mathf.Cos(r)) + Vector3.up * (mag * Mathf.Sin(r));
                        }
                    }
                }

                // The ceiling has to bound the WHOLE vector, not just the horizontal. `speed` was
                // clamped to headCeil above, but headVy was bolted on afterwards and never was, so the
                // ball could leave faster than the ceiling by its vertical component alone. That gap is
                // species-skewed rather than neutral: headVy is the post-solve v.y off a head sitting
                // at quadruped height on a barrel carrying ~8x a human torso's inertia, so it is
                // reliably larger there. Applied last, after the tilt, which is magnitude-preserving,
                // so this is the final word on what gets written and the aim survives untouched.
                headOut = Vector3.ClampMagnitude(headOut, headCeil);

                // Species trim on the finished vector, applied after the clamp so it lowers the
                // MAXIMUM and not merely the sub-maximum headers. 1 on every biped, so the human
                // header is bit-identical to before. Scaling the vector rather than headCeil keeps the
                // whole band consistent: a header that came in under the cap loses the same 10% as one
                // that was pinned to it, and the direction is untouched either way.
                float hdrSpeedMul = hdr.SpeedMul > 0f ? hdr.SpeedMul : 1f;
                if (hdrSpeedMul != 1f) headOut *= hdrSpeedMul;

                Rb.linearVelocity = headOut;

                // Swerve toward goal via curl + spin - only when facing the goal. A header
                // while turned away flies straight (no goal-ward curl).
                if (facingGoal)
                {
                    Vector3 lateral = Vector3.Cross(Vector3.up, toGoal);
                    _curlAccel = lateral * SimConfig.HeaderSwerve;
                    _curlRemaining = SimConfig.AssistDuration + 0.3f;
                    _wiggleRemaining = 0f;   // a header never snakes; drop any leftover knuckle wiggle
                    Rb.angularVelocity += new Vector3(0f, SimConfig.HeaderSwerve, 0f);
                }
                else
                {
                    _curlAccel = Vector3.zero;
                    _curlRemaining = 0f;
                    _wiggleRemaining = 0f;
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

                    // ---- A VOLLEY LEAVES DOWN THE CAMERA LOOK RAY ----
                    // Direction is the camera YAW and the loft is its PITCH, so aiming high lofts
                    // the shot and aiming at the turf drives it low. This replaces the "straight at
                    // the goal centre when facing it" rule for volleys ONLY: a scripted set piece
                    // (SetPieceShot, i.e. free kick / penalty) still uses the aim above, and a
                    // striker with no bound camera (an AI body) falls through unchanged.
                    bool lookVolley = volley && striker.HasLookAim;
                    float lookYaw = 0f, lookPitch = 0f, lookSlope = SimConfig.SetPieceLoft;
                    if (lookVolley)
                    {
                        lookYaw = striker.LookYaw; lookPitch = striker.LookPitch;
                        Vector3 lookDir = Quaternion.Euler(lookPitch, lookYaw, 0f) * Vector3.forward;
                        Vector3 lookFlat = new Vector3(lookDir.x, 0f, lookDir.z);
                        if (lookFlat.sqrMagnitude > 1e-4f)
                        {
                            shotDir = lookFlat.normalized;
                            // Rise per unit of ground travel, clamped: the floor keeps a flat-aimed
                            // volley off the turf, the cap stops a near-vertical aim ballooning.
                            lookSlope = Mathf.Clamp(lookDir.y / lookFlat.magnitude,
                                                    SimConfig.VolleyLookMinLoft, SimConfig.VolleyLookSlopeMax);
                        }
                        else lookVolley = false;   // degenerate ray (looking dead up): keep the old aim
                    }

                    // Scripted launch speed: a power-scaled floor, hard-capped (Cannon raises it).
                    float launch = Mathf.Min(SimConfig.SetPieceBaseSpeed * shotMul,
                                             SimConfig.SetPieceMaxSpeed * capMul);
                    flat = shotDir * launch;
                    vy   = launch * (lookVolley ? lookSlope : SimConfig.SetPieceLoft);

                    // Strike frame: right = across the shot, up = world up. side>0 = struck on
                    // the ball's right, vert>0 = struck high.
                    Vector3 shotRight = Vector3.Cross(Vector3.up, shotDir);
                    float side = Vector3.Dot(strikeOffset, shotRight);   // -1..1 (right positive)
                    float vert = strikeOffset.y;                          // -1..1 (up positive)
                    float curlMag = SimConfig.SetPieceCurl * PlayerProfile.ShotPowerMul;

                    _curlAccel = Vector3.zero; _curlRemaining = 0f;
                    _wiggleRemaining = 0f;   // physical set piece: no scripted-wiggle carryover
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

                    if (lookVolley)
                    {
                        // AIM = where the LOOK RAY crosses the goal plane. Accuracy only TIGHTENS it:
                        // a raw striker scatters around that point, a maxed one lands on it. A ray that
                        // is genuinely OFF FRAME is left where it is, so a bad aim is a real miss
                        // instead of being dragged back between the posts by the assist.
                        Vector3 aimP = SetPieceTaker.LookAimPoint(Rb.position, lookYaw, lookPitch,
                                                                  SimConfig.AttackGoalCenter.z);
                        float lookScatter = SimConfig.VolleyAimScatter
                                            * (1f - combined * SimConfig.VolleyAimTighten);
                        aimP += shotRight * (Random.Range(-1f, 1f) * halfInside * lookScatter)
                                + Vector3.up * (Random.Range(-1f, 1f) * SimConfig.GoalHeight * lookScatter);

                        float aimLat = Vector3.Dot(aimP - SimConfig.AttackGoalCenter, shotRight);
                        bool onFrame = Mathf.Abs(aimLat) <= SimConfig.GoalWidth * 0.5f + SimConfig.VolleyLookOffFrame
                                       && aimP.y <= SimConfig.GoalHeight + SimConfig.VolleyLookOffFrame;
                        if (onFrame)
                            aimP.y = Mathf.Clamp(aimP.y, SimConfig.BallRadius,
                                                 SimConfig.GoalHeight - SimConfig.VolleyBarClear);
                        _assistTarget = aimP;
                    }
                    else if (volley)
                    {
                        // VOLLEY AIM (free kicks keep the corner aim above, untouched). The corner
                        // solve was too precise: every volley rifled the same top corner. Aim instead
                        // at a random point inside a window bounded by the LIVE goal opening - always
                        // UNDER the crossbar and BETWEEN the posts, both scaled by the match-setup
                        // goal size (GoalWidth / GoalHeight are mutable statics the setup writes).
                        // Skill still reads: it pulls the aim toward the struck side and shrinks the
                        // scatter, but never to zero, so the same contact never repeats exactly.
                        float scatter = SimConfig.VolleyAimScatter
                                        * (1f - combined * SimConfig.VolleyAimTighten);

                        float latPull = latSign * halfInside * SimConfig.VolleyAimLatFrac * combined;
                        float lat = Mathf.Clamp(latPull + Random.Range(-1f, 1f) * halfInside * scatter,
                                                -halfInside, halfInside);

                        // Struck LOW -> aim the upper window, struck HIGH -> the lower one, otherwise
                        // mid. Raw players sit mid-goal; skill leans it toward the picked band.
                        float loF = SimConfig.VolleyAimLowFrac, hiF = SimConfig.VolleyAimTopFrac;
                        float midF = 0.5f * (loF + hiF);
                        float bandF = vert <= SimConfig.SetPieceLowStrike ? hiF
                                    : (vert >= SimConfig.SetPieceTopThresh ? loF : midF);
                        float yFrac = Mathf.Lerp(midF, bandF, combined) + Random.Range(-1f, 1f) * scatter;
                        float volleyY = Mathf.Clamp(yFrac, loF, hiF) * SimConfig.GoalHeight;

                        _assistTarget = SimConfig.AttackGoalCenter + shotRight * lat
                                        + Vector3.up * volleyY;
                    }

                    if (Mathf.Abs(side) >= SimConfig.SetPieceSideThresh)
                    {
                        // Side spin: bend the SAME way the ball was struck (Coriolis feel) -
                        // struck on the right curls right, struck left curls left - scaled by how
                        // far off-centre the contact was. Lateral accel across the shot.
                        float s = Mathf.Sign(side) * Mathf.Clamp01(Mathf.Abs(side));
                        // A VOLLEY bends far less than a free kick and for a shorter window: at full
                        // curl over AssistDuration+0.5s a side contact accrued more lateral speed than
                        // it had forward pace, so the ball slid sideways and stopped closing on goal.
                        // The pace it loses to the bend is handed back as goalward speed.
                        // The curve comes off the CONTACT SIDE (above) and its magnitude scales with
                        // the SKILL stat: a raw striker gets the base bend, a fully invested one up to
                        // VolleyCurveStatMul x it. Free kicks keep the flat curlMag.
                        float curveStat = 1f + (SimConfig.VolleyCurveStatMul - 1f) * combined;
                        float bend = volley ? curlMag * SimConfig.VolleyCurlMul * curveStat : curlMag;
                        _curlAccel = shotRight * (s * bend);
                        _curlRemaining = volley ? SimConfig.AssistDuration * SimConfig.VolleyCurlTimeMul
                                                : SimConfig.AssistDuration + 0.5f;
                        Rb.angularVelocity = Vector3.up
                                             * (s * (volley ? curlMag * SimConfig.VolleySpinMul * curveStat : curlMag));
                        if (volley) flat = shotDir * (launch * SimConfig.VolleySidePaceMul);
                    }
                    else if (vert >= SimConfig.SetPieceTopThresh)
                    {
                        // Top spin: struck high -> dips. Curl DOWNWARD over the flight + forward
                        // roll spin about the shot-right axis.
                        _curlAccel = Vector3.down * (curlMag * SimConfig.SetPieceTopSpinMul);
                        _curlRemaining = SimConfig.AssistDuration + 0.5f;
                        Rb.angularVelocity = shotRight * curlMag;
                    }
                    else if (volley && vert <= SimConfig.SetPieceKnuckleVert)
                    {
                        // VOLLEY, struck under the ball: NO chip and NO knuckle. It just drives
                        // forward at a modest loft, power-scaled like every other volley (launch
                        // already carries shotMul). The chip/knuckle pair below is free-kick only.
                        vy   = launch * SimConfig.VolleyBottomLoft;
                        flat = shotDir * (launch * SimConfig.VolleyBottomPaceMul);
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
                    float shotCeil = SimConfig.StrikeHorizMax * shotMul * capMul;
                    flat = Vector3.ClampMagnitude(flat * SimConfig.StrikeHorizBoost * shotMul, shotCeil);
                    vy = v.y;
                    // Bound the WHOLE vector at that same ceiling, for the reason the header block
                    // spells out: vy passes through untouched, so a ball struck upward left faster than
                    // the ceiling by its vertical component alone. A quadruped strikes with a front leg
                    // of roughly 2x a human foot's mass from a taller body, so its post-solve v.y is
                    // the larger one and the bypass favoured it. Scale flat and vy TOGETHER so only the
                    // speed is bounded and the launch angle is preserved. Deliberately inside this
                    // else: the set-piece branch above owns its own absolute cap and a derived apex
                    // ceiling, and must not be re-clamped against an open-play number.
                    // BICYCLE: kill the high looper. Trade vertical for goalward pace, both scaled
                    // by a Shooting+Control blend, so an invested player drives the bike in low and
                    // a raw one still loops it. Applied BEFORE the whole-vector clamp below, which
                    // then bounds it at the same ceiling every other strike is held to - this lowers
                    // the launch angle, it does not raise the ceiling.
                    if (bicycleAttempt)
                    {
                        float bike = PlayerProfile.BicycleSkill01;
                        vy   *= Mathf.Lerp(SimConfig.BicycleVKeepRaw, SimConfig.BicycleVKeepSkilled, bike);
                        flat *= Mathf.Lerp(SimConfig.BicyclePaceRaw,  SimConfig.BicyclePaceSkilled,  bike);
                    }
                    float total = Mathf.Sqrt(flat.sqrMagnitude + vy * vy);
                    if (total > shotCeil && total > 0.001f)
                    {
                        float k = shotCeil / total;
                        flat *= k; vy *= k;
                    }
                    _curlAccel = Vector3.zero;
                    _curlRemaining = 0f;
                    _wiggleRemaining = 0f;
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

        // ------------------------------------------------------------------ charged shot
        // The player's deliberate shot. HOLD a leg button with the ball in range to charge, RELEASE
        // to fire, hold to FULL and it fires itself. Striker.UpdateShotCharge owns the timing; this
        // owns the flight.
        //
        // ONE EXPRESSIVE AXIS. The charge picks elevation AND power together, so the shot you can
        // aim is the shot you can't hit hard:
        //     light -> scooped, high, soft   (the chip over a keeper who has come out)
        //     mid   -> placed, mid height, bends most
        //     full  -> flattest, hardest, driven, and it commits you (auto-fire)
        //
        // ELEVATION IS NOT CAMERA PITCH, deliberately. SimConfig.CamPitchMin is -6 deg, so look
        // pitch can only ever point tan(6) = 0.105 * distance above the ball: 20.8 m of range to
        // aim at a 2.19 m crossbar, while every match shot is taken inside 20 m. Look supplies
        // yaw. Height supplies itself.
        //
        // Numbers, solved in the project's 2x gravity (SimConfig.Gravity -19.6), ball starting at
        // BallRadius 0.22 and no drag, at PlayerProfile.ShotPowerMul 1.0:
        //   light 13 m/s @ 44deg -> apex 2.08 m at 4.3 m, 1.98 m high at 6 m, lands at 8.6 m.
        //                           Clears a 1.8 m keeper who has rushed 6 m off his line, and
        //                           dies short of the net from any real range - which is the point.
        //   mid  19.5 m/s @ 27deg -> apex 2.00 m at 7.9 m, arrives 1.67 m at 12 m, 0.07 m at 16 m.
        //   full   26 m/s @ 17deg -> arrives 1.06 m at 16 m, grazes the line at 20 m.
        //
        // WHAT THE KEEPER GETS. At Normal his reaction is SimConfig.AiTable Normal.react = 0.32 s,
        // so the flight time minus 0.32 is what he has to actually move:
        //   full drive from 12 m: 0.48 s flight -> 0.16 s. He saves what is already near him.
        //   mid shot  from 12 m: 0.69 s flight -> 0.37 s. He reaches a fair way.
        //   light chip from  8 m: 0.86 s flight -> 0.54 s, but it is over his head, so it is his
        //                        High band / Jump that has to be right, not his feet.
        // That spread is what the 60-70% save target at Normal rests on. It is arithmetic from the
        // launch numbers plus his published reaction time, NOT a measured save rate - the save also
        // depends on his reach and dive bands, which live in Goalkeeper.cs. Treat it as the intent
        // and re-measure before calling it tuned.
        const float ShotElevLightDeg = 44f;
        const float ShotElevMidDeg   = 27f;
        const float ShotElevFullDeg  = 17f;
        const float ShotSpeedLight   = 13f;
        const float ShotSpeedFull    = 26f;   // == SimConfig.StrikeHorizMax, the open-play strike ceiling
        // Curl: 4 m/s^2 for 0.9 s. Over a 0.69 s mid-range flight that is 0.5*4*0.69^2 = 0.95 m of
        // bend, and 1.69 m over a 0.92 s one - about a quarter of a 7.32 m goal. Enough to beat a
        // keeper who has committed, nowhere near enough to be a homing missile. A twelfth of
        // SetPieceCurl's 12 m/s^2, which is a dead-ball number and far too much for open play.
        const float ShotCurlAccMax  = 4.0f;
        const float ShotCurlSeconds = 0.9f;

        // Placement error, as a half-angle cone in DEGREES, summed per term. Every term is
        // something the player did, so a wild shot is always traceable to a choice:
        //   base       1.0  a planted, square, unpressured strike is nearly true but never exact
        //   power    + 2.0  at full charge, 0 at light: hitting through it costs placement
        //   range    + 0.08 per metre to the goal (1.28 deg at 16 m)
        //   balance  + 2.2  at 8 m/s of ground speed and up (a sprint), 0 planted
        //   facing   + 3.0  at 90deg between his momentum/body and the shot, 0 square
        //   pressure + 1.6  with an opponent on top of him inside PassPressureRadius 3.5 m
        //   weakfoot + 2.5  off the wrong boot, divided by the Control weak-foot node
        // Then the whole cone is scaled by (1 - 0.7 * shooting accuracy), so a maxed shooter keeps
        // 30% of it. Worst case sums to 13.6 deg = +-3.8 m at 16 m (over half the goal width);
        // a planted square full-power 10 m shot on a maxed build is 1.14 deg = +-0.20 m.
        const float ShotScatterBaseDeg     = 1.0f;
        const float ShotScatterPowerDeg    = 2.0f;
        const float ShotScatterPerMetreDeg = 0.08f;
        const float ShotScatterBalanceDeg  = 2.2f;
        const float ShotScatterFacingDeg   = 3.0f;
        const float ShotScatterPressureDeg = 1.6f;
        const float ShotScatterWeakFootDeg = 2.5f;
        const float ShotScatterWorstDeg    = 13.6f;   // the sum, used only to normalise the weight wobble

        /// <summary>
        /// The shot error cone, in degrees of half-angle. One function so a human shot and an AI shot
        /// are wrong in the same way and are beatable in the same way - the AI's difficulty tier only
        /// changes `accuracy01`, it does not get a different error model.
        ///
        /// `offBalance01` is ground speed / 8 m/s clamped. `offFacingDeg` is the angle between where
        /// he is going (or pointing) and where he is shooting. `pressure01` is 0..1 closeness of the
        /// nearest opponent. `weakFoot` reads PlayerProfile's weak-foot node, so an AI body should
        /// pass false (PlayerProfile is the local human's build, not the bot's).
        /// </summary>
        public static float ShotScatterDeg(float charge01, float dist, float offBalance01,
                                           float offFacingDeg, float pressure01, bool weakFoot,
                                           float accuracy01)
        {
            float deg = ShotScatterBaseDeg;
            deg += ShotScatterPowerDeg * Mathf.Clamp01(charge01);
            deg += ShotScatterPerMetreDeg * Mathf.Max(0f, dist);
            deg += ShotScatterBalanceDeg * Mathf.Clamp01(offBalance01);
            deg += ShotScatterFacingDeg * Mathf.Clamp01(offFacingDeg / 90f);
            deg += ShotScatterPressureDeg * Mathf.Clamp01(pressure01);
            if (weakFoot) deg += ShotScatterWeakFootDeg / Mathf.Max(1f, PlayerProfile.WeakFootMul);
            return deg * (1f - 0.7f * Mathf.Clamp01(accuracy01));
        }

        /// <summary>
        /// Launch a charged shot. THE entry point for both the human (Striker.FireChargedShot) and
        /// the AI, so an AI shot flies, bends, mis-places and is saved by exactly the same rules.
        ///
        /// `flatDir` is the flat aim direction and is the ONLY thing aim supplies - height comes off
        /// `charge01`. `curl01` is -1..+1 for which side of the ball the boot came in on (+ = struck
        /// on the right, bends right, matching the sign convention the strike path in this file
        /// already uses). `scatterDeg` comes from ShotScatterDeg.
        /// </summary>
        public void LaunchChargedShot(Vector3 flatDir, float charge01, float curl01, float scatterDeg,
                                      ActiveRagdoll shooter, bool facingGoal, bool camShouldCut = false)
        {
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 1e-4f) flatDir = transform.forward;
            flatDir.Normalize();
            charge01 = Mathf.Clamp01(charge01);
            curl01 = Mathf.Clamp(curl01, -1f, 1f);
            scatterDeg = Mathf.Max(0f, scatterDeg);

            // Error goes on the AIM, before anything is derived from it. Yaw takes the full cone;
            // elevation takes half. Both, not just yaw: a pass that misses goes to the wrong player
            // (which is why Passing.Launch is yaw-only), but a shot that misses goes over the bar,
            // and that is half of what a bad shot looks like.
            if (scatterDeg > 0.01f)
                flatDir = Quaternion.AngleAxis(Random.Range(-scatterDeg, scatterDeg), Vector3.up) * flatDir;

            // Elevation off the charge, through the mid point rather than straight from light to
            // full - a plain lerp would put "placed, mid height" at the average of a chip and a
            // drive, which is neither.
            float elev = charge01 < 0.5f
                       ? Mathf.Lerp(ShotElevLightDeg, ShotElevMidDeg, charge01 * 2f)
                       : Mathf.Lerp(ShotElevMidDeg, ShotElevFullDeg, (charge01 - 0.5f) * 2f);
            if (scatterDeg > 0.01f) elev += Random.Range(-scatterDeg, scatterDeg) * 0.5f;
            elev = Mathf.Clamp(elev, 2f, 60f);

            float speed = Mathf.Lerp(ShotSpeedLight, ShotSpeedFull, charge01) * PlayerProfile.ShotPowerMul;
            // Mis-hit weight, scaled by the same cone: up to +-10% on a shot sprayed at the worst
            // case, near nothing on a clean one. A shanked shot should also be badly struck.
            if (scatterDeg > 0.01f)
                speed *= 1f + Random.Range(-1f, 1f) * 0.10f * Mathf.Clamp01(scatterDeg / ShotScatterWorstDeg);
            speed = Mathf.Clamp(speed, 4f, StrikeSpeedCeiling);

            // Scuff the ball clear of the striking boot. Only when it is genuinely inside his legs:
            // a carried ball sits ~0.72 m ahead of the body, so a shot aimed anywhere but forward
            // would otherwise launch through his own shin. Cheaper and less destructive than
            // ResetTo (which drops carry claims and every timer) - this only moves it.
            if (shooter != null && shooter.Pelvis != null)
            {
                Vector3 pp = shooter.Pelvis.position;
                Vector3 d = Rb.position - pp; d.y = 0f;
                if (d.magnitude < 0.55f)
                {
                    Vector3 spawn = new Vector3(pp.x, Mathf.Max(SimConfig.BallRadius, Rb.position.y), pp.z)
                                    + flatDir * 0.55f;
                    Rb.position = spawn;
                    transform.position = spawn;
                }
            }

            float a = elev * Mathf.Deg2Rad;
            Vector3 flat = flatDir * (speed * Mathf.Cos(a));
            Rb.linearVelocity = new Vector3(flat.x, speed * Mathf.Sin(a), flat.z);

            // Curl peaks at MID charge and falls off both ways, so the bend is on the same axis as
            // everything else rather than a separate button. A full drive is struck through the ball
            // and is in the air too briefly to bend; a soft chip has too little pace to. Sin gives
            // that shape in one call; the 0.25 floor keeps a deliberately angled chip from being
            // dead straight.
            float shape = 0.25f + 0.75f * Mathf.Sin(charge01 * Mathf.PI);
            float mag = ShotCurlAccMax * Mathf.Abs(curl01) * shape;
            bool bends = mag > 0.05f;
            if (bends)
            {
                float s = Mathf.Sign(curl01);
                _curlAccel = Vector3.Cross(Vector3.up, flatDir) * (s * mag);
                _curlRemaining = ShotCurlSeconds;
                // Cosmetic roll only. There is no Magnus force in this project - angularVelocity is
                // never read back (see PredictLanding) - so the bend is _curlAccel and this is paint.
                Rb.angularVelocity = Vector3.up * (s * mag * 3f);
            }
            else
            {
                _curlAccel = Vector3.zero;
                _curlRemaining = 0f;
                Rb.angularVelocity = Vector3.zero;
            }
            _wiggleRemaining = 0f;
            _wiggleAmp = 0f;

            if (facingGoal)
            {
                _accuracyMul = SimConfig.StrongFootAccuracy + (PlayerProfile.ShotAccuracyMul - 1f);
                _assistTarget = Vector3.zero;   // centre-goal horizontal nudge, no corner/height aim
                // A curler gets NO horizontal steer: the steer flattens the bend it was aimed with,
                // which is the same reason the set-piece curve shot sets this. And no vertical steer
                // ever, or the assist would predict a deliberate chip back down to goal height.
                _assistFlatOff = bends;
                _assistVertOff = true;
                _assistRemaining = SimConfig.AssistDuration;
            }
            else
            {
                _accuracyMul = 0f;
                _assistRemaining = 0f;
                _assistFlatOff = false;
                _assistVertOff = false;
            }

            if (camShouldCut && _cam != null && flat.magnitude >= SimConfig.ShotCamMinSpeed)
                _cam.PulseBallCam(SimConfig.ShotCamSeconds);

            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = true;
            _trail.Clear();
            // Shooter-scoped, same reasoning as DribbleShot: the striking boot must not re-hit its
            // own shot, and nobody else's volley on the rebound should be blanked.
            SuppressStrikeFor(shooter, SimConfig.DribbleRecaptureCooldown);
        }

        // Is the body about to launch a flat shot actually mid-charge on a real one?
        //
        // Dribble.FixedUpdate releases a carried ball as an INSTANT flat shot on the leg button's
        // press edge, which is the same edge that starts a charge - so without this the charge could
        // never build on a carried ball, the old shot would fire first every time. Testing it HERE,
        // at the moment of the call, rather than pre-arming a suppression window from Striker, is
        // what closes the ordering race: Dribble runs in FixedUpdate and Striker.Tick in Update, so
        // a physics step can see the press before Striker ever does.
        //
        // Striker is on the ragdoll's own GameObject (the strike path in this file already does this
        // exact lookup), and WantsChargedShot reads the live input, so this is never stale.
        //
        // Consequence worth stating: on a carried ball the press now ALWAYS ends the carry and
        // always yields to the charge, so a charge that is then cancelled (he leaves the ground, the
        // ball rolls out of range) produces no shot at all. That reads as a shank, and Dribble's own
        // 0.45 s recapture cooldown lets him pick the ball back up. A double shot would be worse.
        static bool ChargeOwnsShot(ActiveRagdoll shooter)
        {
            if (shooter == null) return false;
            var st = shooter.GetComponent<Striker>();
            return st != null && st.WantsChargedShot;
        }

        // A shot launched by the Dribble component (release-on-kick). Sets the shot
        // velocity, then folds into the SAME systems a normal strike uses: the facing-
        // gated goal assist and the 2s ball-cam pulse. Suppresses re-strike/re-capture so
        // the launching foot doesn't immediately re-hit the ball.
        public void DribbleShot(Vector3 dir, float speed, bool facingGoal, bool camShouldCut,
                                ActiveRagdoll shooter = null)
        {
            // Yield to a charge in progress (see ChargeOwnsShot). Dribble.WantsKick now refuses the
            // press outright so this should be unreachable from that path, but the suppression below is
            // kept because reaching here at all means the ball is live at the feet with collision
            // restored: without it the charging boot strikes its own ball. LaunchChargedShot will fire this
            // shot properly on release or at full.
            if (ChargeOwnsShot(shooter)) { SuppressStrikeFor(shooter, SimConfig.PassMaxCharge + 0.1f); return; }

            Rb.linearVelocity = dir * speed;
            Rb.angularVelocity = Vector3.zero;
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;
            _wiggleRemaining = 0f;

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

            // Hold off THIS shooter only. No _assistCooldown write and no global suppression: both
            // used to blank out everyone else's shot and volley on the rebound for ~0.4s.
            SuppressStrikeFor(shooter, SimConfig.DribbleRecaptureCooldown);
        }

        // Match deliberate LMB/RMB shot: launch AIRBORNE like a set piece (arced, no controllable
        // spin - WASD is movement in match) instead of DribbleShot's flat drive. `dir` is the flat
        // aim direction (the striker's facing); the ball is lofted along it at a fixed launch angle,
        // scaled by shot power. Same facing-gated goal assist as DribbleShot (horizontal steer only
        // when facing goal; vertical steer stays off so the loft survives). No curl/wiggle ever.
        public void LaunchLofted(Vector3 dir, float speed, bool facingGoal, bool camShouldCut,
                                 ActiveRagdoll shooter = null)
        {
            // Same yield as DribbleShot. Scoped to the SHOOTER, so an AI footballer's lofted shot
            // (Footballer launches through here too) is never swallowed by a human's charge.
            if (ChargeOwnsShot(shooter)) { SuppressStrikeFor(shooter, SimConfig.PassMaxCharge + 0.1f); return; }

            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;
            dir.Normalize();

            // Split the launch speed into a forward run + an upward pop at a fixed elevation, so the
            // ball actually leaves the ground and arcs. Cap the vertical so a big shot can't balloon
            // straight up (mirrors the set-piece apex idea without the goal-locked solve).
            float ang = SimConfig.ScrimLoftAngleDeg * Mathf.Deg2Rad;
            Vector3 flat = dir * (speed * Mathf.Cos(ang));
            float vy = Mathf.Min(speed * Mathf.Sin(ang), SimConfig.ScrimLoftMaxVy);
            Rb.linearVelocity = new Vector3(flat.x, vy, flat.z);
            Rb.angularVelocity = Vector3.zero;

            // No spin/curl/knuckle: this is a plain arced shot.
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;
            _wiggleRemaining = 0f;
            _wiggleAmp = 0f;

            // Same aim-assist gating as DribbleShot, but keep the VERTICAL steer OFF so the launch
            // loft isn't predicted back down onto goal height (matches the set-piece over-bar rule).
            if (facingGoal)
            {
                _accuracyMul = SimConfig.StrongFootAccuracy + (PlayerProfile.ShotAccuracyMul - 1f);
                _assistTarget = Vector3.zero;       // centre-goal horizontal steer only (no corner/height aim)
                _assistFlatOff = false;
                _assistVertOff = true;              // let the arc fly; don't flatten it back to goal height
                _assistRemaining = SimConfig.AssistDuration;
            }
            else _accuracyMul = 0f;

            if (camShouldCut && _cam != null)
            {
                Vector3 f = flat; f.y = 0f;
                if (f.magnitude >= SimConfig.ShotCamMinSpeed) _cam.PulseBallCam(SimConfig.ShotCamSeconds);
            }

            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = true;
            _trail.Clear();
            // Shooter-scoped, same reasoning as DribbleShot.
            SuppressStrikeFor(shooter, SimConfig.DribbleRecaptureCooldown);
        }

        // Generic kick/pass: set the ball's velocity directly and clear curl/spin. Used by
        // AI footballers and the passing system (no aim assist - AI/passes aim themselves).
        // Briefly blocks the KICKER'S OWN re-strike so its foot doesn't hit the ball twice. Pass
        // the kicking body: without it the block is global and a receiver cannot first-time a
        // short pass, which killed one-twos.
        public void KickTo(Vector3 velocity, ActiveRagdoll kicker = null)
        {
            Rb.linearVelocity = velocity;
            Rb.angularVelocity = Vector3.zero;
            _curlAccel = Vector3.zero;
            _curlRemaining = 0f;
            _wiggleRemaining = 0f;
            _assistRemaining = 0f;
            _accuracyMul = 1f;
            SuppressStrikeFor(kicker, 0.3f);
        }

        public void ResetTo(Vector3 pos)
        {
            Rb.position = pos;
            transform.position = pos;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            _curlRemaining = 0f;
            _curlAccel = Vector3.zero;
            _wiggleRemaining = 0f;
            _wiggleAmp = 0f;
            _wiggleElapsed = 0f;
            _assistRemaining = 0f;
            _assistCooldown = 0f;
            _assistFlatOff = false;
            _assistVertOff = false;
            _strikeSuppress = 0f;
            _selfSuppress = 0f;
            _selfSuppressBody = null;
            Dribble.ReleaseHolder();   // nobody is carrying a ball that just teleported...
            DribbleCarrier = null;     // ...and drop the claim even if the carrier was a bot
            LastShotWasTrick = false;
            LastShotType = ShotType.Normal;
            _trail.emitting = false;
            _trail.Clear();
        }

        public float Speed => Rb.linearVelocity.magnitude;

        /// <summary>
        /// A shot's goal-assist window is live, so the ball is being STEERED and no ballistic solve
        /// predicts where it lands. Read by the match landing reticle, which hides while it is true.
        /// </summary>
        public bool Guided => _assistRemaining > 0f;

        /// <summary>
        /// Where an airborne ball will come down, and in how long. Closed form, no stepping.
        ///
        /// Solves for the ball CENTRE reaching its RESTING height (y = BallRadius), not the turf at
        /// y = 0. Solving to 0 puts the answer materially long - the ball stops falling 0.22 m early,
        /// which at a typical descent is a fifth of a metre of consistent overshoot.
        ///
        ///     BallRadius = pos.y + vy*t - 0.5*G*t^2   =>   t = ( vy + sqrt(vy^2 + 2*G*h) ) / G
        ///
        /// with h = pos.y - BallRadius. For h &gt; 0 the discriminant exceeds vy^2, so the numerator is
        /// positive whether the ball is rising or falling and this root is always the DESCENDING one;
        /// there is no no-solution branch to write. h &lt;= 0 is rejected before the sqrt.
        ///
        /// WHY A VACUUM SOLVE IS THE RIGHT ONE HERE, not an approximation to apologise for. Every flight
        /// path this project launches is itself a vacuum solve: LaunchTo computes v0 from
        /// (target - p0 - 0.5*g*t^2)/t with no drag term. Modelling the 0.02 linear damping would make
        /// the predictor disagree with the launch by the same ~1.5% in the OPPOSITE direction, and the
        /// disc would sit short of where the ball really lands. There is no Magnus force in the project
        /// (angularVelocity is written for cosmetic roll and never read back), and passes carry zero curl
        /// and zero wiggle. Gravity is read from Physics.gravity because this project runs 2x gravity.
        ///
        /// The one force this CANNOT model is ApplyGoalAssist, which steers a shot with up to
        /// AssistMaxAccel for AssistDuration. Callers must check <see cref="Guided"/> and hide instead:
        /// its inputs are private, and on a client the ball is a kinematic puppet with no such state at
        /// all, so a stepped predictor could not agree between host and client either.
        /// </summary>
        public static bool PredictLanding(Vector3 pos, Vector3 vel, out Vector3 land, out float time)
        {
            land = pos; time = 0f;
            float g = Mathf.Abs(Physics.gravity.y);
            if (g < 0.01f) g = Mathf.Abs(SimConfig.Gravity);

            float h = pos.y - SimConfig.BallRadius;
            if (h <= 0.0001f) return false;                 // already at rest height: nothing to predict

            float t = (vel.y + Mathf.Sqrt(vel.y * vel.y + 2f * g * h)) / g;
            if (t <= 0.0001f || float.IsNaN(t)) return false;

            time = t;
            land = new Vector3(pos.x + vel.x * t, SimConfig.BallRadius, pos.z + vel.z * t);
            return true;
        }

        /// <summary>
        /// The five left-side bones, spelled out instead of testing the enum's name, so this costs
        /// no allocation on a collision path. Bone has 13 members and is fixed, so the list cannot
        /// silently fall out of date.
        /// </summary>
        static bool IsLeftBone(Bone b)
            => b == Bone.ThighL || b == Bone.CalfL || b == Bone.FootL
            || b == Bone.UpperArmL || b == Bone.ForearmL;
    }
}
