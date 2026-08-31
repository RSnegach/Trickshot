using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The pass model. Every pass in the game - human, AI, call-for-pass, keeper distribution -
    /// is solved and launched through here, so weight and flight are consistent everywhere.
    ///
    /// PACE COMES FROM DISTANCE, not from the button. That single change is what separates a pass
    /// that feels like football from one that does not. A fixed launch speed (the old flat
    /// PassGroundSpeed) either dies short on a 30 m switch or blasts a 6 m square ball past the
    /// receiver's shins. Here distance sets the pace and the hold only trims it, so a tap is a
    /// weighted pass and a hold is a driven one AT ANY RANGE.
    ///
    /// Lofted passes are solved ballistically to LAND on the target (BallController.LaunchTo)
    /// instead of following a fixed arc, so a chip drops at the receiver's feet rather than sailing
    /// over them at 8 m and dribbling to a stop at 40.
    ///
    /// Leading is a time-of-flight solve, not a fixed fraction: work out how long the ball is in
    /// the air, then aim where the receiver will BE. Two fixed-point iterations converge fine
    /// because flight time barely moves once the aim is roughly right.
    ///
    /// Error is a property of the pass, not a flat dice roll. Distance, pressure on the passer,
    /// how hard it was struck, and whether it was hit first time all widen the cone; passing
    /// accuracy closes it (Maestro = pinpoint).
    ///
    /// Target choice weighs alignment with the aim, forward progress, the receiver's space, range
    /// fit, and whether the lane is blocked, and it will play a THROUGH BALL into the space ahead
    /// of a runner instead of only ever at their feet.
    /// </summary>
    public static class Passing
    {
        /// <summary>Ground, air (lofted) and chip. Each has its own button and its own range band.</summary>
        public enum PassKind { Ground = 0, Air = 1, Chip = 2 }

        /// <summary>
        /// One player's pass power bar: three independent charges, one per pass button.
        ///
        /// ARMED is the part that is not obvious, and it is what stops the mode's most common sequence
        /// from misfiring. The same buttons CALL for a pass while you have no ball, so a player holding
        /// the call button as the ball arrives would otherwise slide straight into a charge and, with
        /// fire-at-full, hit a maximum-range pass they never asked for. A charge therefore only arms on
        /// a button going DOWN while the ball is playable; a hold carried in from a call is inert until
        /// released and pressed again.
        ///
        /// FIRED latches a hold that has already played its pass, so fire-at-full cannot also fire again
        /// on the release. It is cleared when the button comes up, never while it is still down.
        /// </summary>
        public class Bar
        {
            public float ground, air, chip;
            public bool groundArmed, airArmed, chipArmed;
            public bool groundFired, airFired, chipFired;

            public void Clear()
            {
                ground = air = chip = 0f;
                groundArmed = airArmed = chipArmed = false;
                groundFired = airFired = chipFired = false;
            }

            /// <summary>Is any of the three being charged? Drives the run lock while aiming.</summary>
            public bool AnyArmed => groundArmed || airArmed || chipArmed;

            /// <summary>Charge on one kind, 0..1.</summary>
            public float Charge01Of(PassKind k)
                => Passing.Charge01(k == PassKind.Chip ? chip : k == PassKind.Air ? air : ground);

            /// <summary>
            /// Which bar to DRAW, and how full. Only one can be armed at a time in practice, but if two
            /// buttons are down the fuller one is shown - it is the one about to fire.
            /// Returns false when nothing is charging, which is when the bar hides.
            /// </summary>
            public bool Showing(out PassKind kind, out float t01)
            {
                kind = PassKind.Ground; t01 = 0f;
                if (groundArmed && ground > 0f) { kind = PassKind.Ground; t01 = Passing.Charge01(ground); }
                if (airArmed   && Passing.Charge01(air)  > t01) { kind = PassKind.Air;  t01 = Passing.Charge01(air); }
                if (chipArmed  && Passing.Charge01(chip) > t01) { kind = PassKind.Chip; t01 = Passing.Charge01(chip); }
                return t01 > 0f;
            }
        }

        /// <summary>
        /// THE possession gate: can this body play a pass at all right now? One implementation, called
        /// by the single-player path, by the networked host, and by a client drawing its own predicted
        /// bar - because when those three disagree the client shows a bar filling for a pass the host
        /// will never play.
        ///
        /// `blocked` folds in being knocked down or mid-emote. `firstTime` means the ball is arriving
        /// rather than settled: playable, at an accuracy cost.
        ///
        /// One caveat on a CLIENT: the ball there is a kinematic puppet lerped from snapshots, so
        /// ball.Speed can read near zero while the real ball is moving, making `settled` a little
        /// permissive. That only ever makes a PREDICTED bar appear slightly early; the host still
        /// decides whether a pass happens.
        /// </summary>
        public static bool CanPlay(BallController ball, Vector3 bodyPos, bool carrying, bool blocked,
                                   out bool firstTime)
        {
            firstTime = false;
            if (blocked || ball == null) return false;
            if (carrying) return true;

            Vector3 me = bodyPos; me.y = 0f;
            Vector3 bp = ball.transform.position;
            Vector3 b = bp; b.y = 0f;
            float d = Vector3.Distance(me, b);
            if (d <= SimConfig.BallRadius + 1.1f && ball.Speed < 8f) return true;
            if (d <= SimConfig.PassFirstTouchRadius && bp.y < 1.3f) { firstTime = true; return true; }
            return false;
        }

        /// <summary>
        /// Can this body STRIKE the ball right now - real foot-contact range, not CanPlay's passing-
        /// forgiveness radius. A shot is not a pass: releasing a pass without the ball glued to the
        /// foot is the right feel, but a shot firing at a ball reported as "3 feet to the left" of
        /// the striker and still finding the goal is not - the ball has to actually be interacting
        /// with the body. Same XZ-flattened-against-the-pelvis shape as CanPlay's own check (this
        /// project has no per-foot reference point to test against instead), just SimConfig.
        /// ShotContactRadius (0.35 m) in place of CanPlay's 1.1 m. No speed/first-time distinction:
        /// Striker mode is volley-only, so an arriving ball has to be shootable exactly like a
        /// settled one the moment it is close enough.
        /// </summary>
        public static bool CanShoot(BallController ball, Vector3 bodyPos, bool blocked)
        {
            if (blocked || ball == null) return false;
            Vector3 me = bodyPos; me.y = 0f;
            Vector3 b = ball.transform.position; b.y = 0f;
            return Vector3.Distance(me, b) <= SimConfig.BallRadius + SimConfig.ShotContactRadius;
        }

        /// <summary>Seconds held -> 0..1 bar fill.</summary>
        public static float Charge01(float held) => Mathf.Clamp01(held / SimConfig.PassMaxCharge);

        /// <summary>
        /// Advance ONE pass button's charge by a frame and report whether it fired this frame.
        ///
        /// `canPlay` is the possession gate: false disarms and zeroes, so a bar can never fill while the
        /// player has no ball, is down, or is mid-emote. `fresh` is the anti-stale-input gate and is the
        /// reason fire-at-full is safe over a network: the host re-feeds the LAST received InputFrame
        /// every tick whether or not a new one arrived (NetMatch feeds NetSession.InputForSlot
        /// unconditionally), and a client stops sending entirely while paused or typing in quickchat. A
        /// held bit therefore stays pinned true indefinitely. Under the old fire-on-release rule that was
        /// harmless, because Released never became true; with fire-at-full it would charge to full and
        /// play a pass nobody asked for. So the charge only accumulates on a frame whose input is NEW.
        /// The device path always passes fresh = true.
        /// </summary>
        public static bool StepCharge(bool held, bool pressed, bool released, bool canPlay, bool fresh,
                                      ref float charge, ref bool armed, ref bool fired,
                                      out float fireCharge01)
        {
            fireCharge01 = 0f;

            if (!canPlay)
            {
                charge = 0f; armed = false;
                if (!held) fired = false;      // consumed only until the button comes up
                return false;
            }

            // Arm on the button going down WITH the ball. A hold that began without it never arms.
            if (pressed) { armed = true; fired = false; charge = 0f; }

            if (armed && !fired && held && fresh)
            {
                charge += Time.deltaTime;
                // CAP. Under cap-and-wait the bar sits at full until the button comes up, so the timer
                // must stop rather than run away: nothing reads a charge past full, and letting it
                // accumulate for the length of a hold is just an unbounded number waiting to surprise
                // somebody who reads it raw.
                if (charge > SimConfig.PassMaxCharge) charge = SimConfig.PassMaxCharge;
            }

            // FIRE AT FULL, when enabled. Plays immediately rather than waiting for the release.
            if (armed && !fired && SimConfig.PassAutoFireAtFull && charge >= SimConfig.PassMaxCharge)
            {
                fireCharge01 = 1f;
                fired = true; armed = false; charge = 0f;   // zeroed, so the bar does not read 100% with the ball gone
                return true;
            }

            if (released)
            {
                bool play = armed && !fired;
                float c = Charge01(charge);
                armed = false; charge = 0f; fired = false;   // button up: ready to arm again
                if (play)
                {
                    // A press and release inside ONE frame reads held = false with released = true on the
                    // device path, and that has always fired a minimum-weight pass. Keep it.
                    fireCharge01 = c;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Step all three of a bar's buttons for one frame and report which one fired.
        ///
        /// Single entry point on purpose: the single-player path, the networked host and a client
        /// drawing its own predicted bar all go through this, so none of them can drift into stepping
        /// the bar differently from the others.
        ///
        /// Ground wins a tie, then air, then chip. Ties only happen if two buttons are held and both
        /// cross full on the same frame, which needs them pressed within a frame of each other.
        /// </summary>
        public static bool StepAll(Bar bar, IStrikerInput input, bool canPlay, bool fresh,
                                   out PassKind kind, out float fireCharge01)
        {
            kind = PassKind.Ground; fireCharge01 = 0f;
            if (bar == null || input == null) return false;

            bool g = StepCharge(input.PassGroundHeld, input.PassGroundPressed, input.PassGroundReleased,
                                canPlay, fresh, ref bar.ground, ref bar.groundArmed, ref bar.groundFired,
                                out float gc);
            bool a = StepCharge(input.PassLoftedHeld, input.PassLoftedPressed, input.PassLoftedReleased,
                                canPlay, fresh, ref bar.air, ref bar.airArmed, ref bar.airFired,
                                out float ac);
            bool c = StepCharge(input.PassChipHeld, input.PassChipPressed, input.PassChipReleased,
                                canPlay, fresh, ref bar.chip, ref bar.chipArmed, ref bar.chipFired,
                                out float cc);

            if (g) { kind = PassKind.Ground; fireCharge01 = gc; return true; }
            if (a) { kind = PassKind.Air;    fireCharge01 = ac; return true; }
            if (c) { kind = PassKind.Chip;   fireCharge01 = cc; return true; }
            return false;
        }

        /// <summary>
        /// How far along the look ray this charge aims. The bar picks DISTANCE, which is what makes a tap
        /// a short ball and a full bar the longest that type plays.
        /// </summary>
        public static float AimRange(PassKind kind, float charge01, float powerMul)
        {
            float min, max;
            switch (kind)
            {
                case PassKind.Chip: min = SimConfig.PassRangeChipMin;   max = SimConfig.PassRangeChipMax;   break;
                case PassKind.Air:  min = SimConfig.PassRangeAirMin;    max = SimConfig.PassRangeAirMax;    break;
                default:            min = SimConfig.PassRangeGroundMin; max = SimConfig.PassRangeGroundMax; break;
            }
            // Power lifts only the TOP of the band. Multiplying the whole band would raise the MINIMUM
            // with it and delete the short pass for an invested build, which is backwards: investing in
            // passing should widen what you can play, not take the simple ball away.
            max *= Mathf.Max(0.2f, powerMul);
            if (kind == PassKind.Chip) max = Mathf.Min(max, SimConfig.PassRangeChipCap);
            return Mathf.Lerp(min, Mathf.Max(min, max), Mathf.Clamp01(charge01));
        }

        /// <summary>
        /// The aim point for a look-ray pass: straight down the player's look yaw, at the charged range.
        /// This REPLACES BestTarget for a human pass. BestTarget stays for the AI and for call-for-pass,
        /// where there is no camera to read.
        /// </summary>
        public static Vector3 LookAim(Vector3 from, float lookYaw, PassKind kind, float charge01, float powerMul)
        {
            Vector3 dir = Quaternion.Euler(0f, lookYaw, 0f) * Vector3.forward;
            Vector3 aim = from + dir * AimRange(kind, charge01, powerMul);
            aim.y = SimConfig.BallRadius;
            return aim;
        }

        /// <summary>
        /// Flight time for a chip, solved from a FIXED APEX rather than from the distance. That is what
        /// separates it from the air pass: an air pass gets flatter as it gets longer, a chip always goes
        /// the same height and only looks flatter, so a receiver can time a header or a bicycle off it
        /// without having to judge the arc first.
        /// Gravity is read from Physics.gravity, not hardcoded - this project runs 2x gravity by design
        /// (SimConfig.Gravity = -19.6).
        /// </summary>
        public static float ChipTime(float fromY, float toY)
        {
            float g = Mathf.Abs(Physics.gravity.y);
            if (g < 0.01f) g = Mathf.Abs(SimConfig.Gravity);
            float apex = Mathf.Max(0.5f, SimConfig.PassChipApexY);
            float up   = Mathf.Sqrt(2f * apex / g);                                     // launch -> apex
            float down = Mathf.Sqrt(2f * Mathf.Max(0.1f, apex + fromY - toY) / g);      // apex -> landing
            return up + down;
        }

        // ---------------------------------------------------------------- weight

        /// <summary>Launch speed for a ground pass that has to travel `dist` metres.</summary>
        public static float GroundSpeed(float dist, float charge01, float powerMul)
        {
            float v = SimConfig.PassGroundBase + Mathf.Max(0f, dist) * SimConfig.PassGroundPerMetre;
            v *= Drive(charge01) * Mathf.Max(0.2f, powerMul);
            return Mathf.Clamp(v, SimConfig.PassGroundMin, SimConfig.PassGroundMax);
        }

        /// <summary>Time of flight for a lofted pass of `dist` metres. A tap floats, a hold drives.</summary>
        public static float LoftTime(float dist, float charge01)
        {
            float t = SimConfig.PassLoftBaseTime + Mathf.Max(0f, dist) * SimConfig.PassLoftTimePerMetre;
            t *= Mathf.Lerp(SimConfig.PassLoftFloatMul, SimConfig.PassLoftDrivenMul, Mathf.Clamp01(charge01));
            return Mathf.Clamp(t, SimConfig.PassLoftTimeMin, SimConfig.PassLoftTimeMax);
        }

        // Hold maps to a narrow DRIVE band, not a wide power band: the distance solve already
        // supplies the pace, so a tap must only take a little off it or long passes die short.
        static float Drive(float charge01)
            => Mathf.Lerp(SimConfig.PassChargeMinMul, SimConfig.PassChargeMaxMul, Mathf.Clamp01(charge01));

        /// <summary>How long a pass of this distance is in flight (for leading a runner).</summary>
        public static float Flight(float dist, bool lofted, float charge01, float powerMul)
            => lofted ? LoftTime(dist, charge01)
                      : dist / Mathf.Max(1f, GroundSpeed(dist, charge01, powerMul));

        /// <summary>Kind-aware flight time. A chip is solved from its apex, not its length.</summary>
        public static float Flight(float dist, PassKind kind, float charge01, float powerMul)
            => kind == PassKind.Chip ? ChipTime(SimConfig.BallRadius, SimConfig.BallRadius)
                                     : Flight(dist, kind == PassKind.Air, charge01, powerMul);

        // ---------------------------------------------------------------- error

        /// <summary>0..1 passing accuracy from a PassAccuracyMul. Maestro is pinpoint.</summary>
        public static float Accuracy01(float accMul, bool maestro)
            => maestro ? 1f : Mathf.Clamp01((accMul - 1f) / 0.85f);

        /// <summary>Half-angle of the aim error cone, in degrees.</summary>
        public static float ScatterDeg(float acc01, float dist, float pressure01, float charge01, bool firstTime)
        {
            float deg = SimConfig.PassScatterMaxDeg * (1f - Mathf.Clamp01(acc01));
            deg *= 1f + SimConfig.PassScatterPerMetre * Mathf.Max(0f, dist);
            deg *= 1f + SimConfig.PassScatterPressure * Mathf.Clamp01(pressure01);
            deg *= 0.8f + 0.2f * Drive(charge01);
            if (firstTime) deg *= SimConfig.PassFirstTimeScatterMul;
            return deg;
        }

        /// <summary>Fractional error on the weight of the pass (over/under hit).</summary>
        public static float Wobble(float acc01, bool firstTime)
        {
            float w = SimConfig.PassPowerWobble * (1f - Mathf.Clamp01(acc01));
            if (firstTime) w *= SimConfig.PassFirstTimeScatterMul;
            return w;
        }

        /// <summary>0..1 how closed-down the passer is (nearest opponent inside PassPressureRadius).</summary>
        public static float Pressure01(Vector3 at, List<Footballer> opps)
        {
            if (opps == null) return 0f;
            float best = SimConfig.PassPressureRadius;
            for (int i = 0; i < opps.Count; i++)
            {
                var o = opps[i];
                if (o == null || o.IsDown) continue;
                Vector3 p = o.Pos; p.y = 0f;
                Vector3 a = at; a.y = 0f;
                float d = Vector3.Distance(p, a);
                if (d < best) best = d;
            }
            return 1f - Mathf.Clamp01(best / SimConfig.PassPressureRadius);
        }

        // ---------------------------------------------------------------- launch

        /// <summary>
        /// Solve and hit a pass from the ball's current position at `aim`.
        ///
        /// The ball is nudged forward (and up, for a loft) off the passer so it does not launch
        /// straight into their own torso, which is what used to flatten chips into the turf. Only
        /// the PASSER is held off the ball afterwards, so a receiver can still take it first time.
        /// </summary>
        public static void Launch(BallController ball, Vector3 aim, bool lofted, float charge01,
                                  float powerMul, ActiveRagdoll passer, float scatterDeg, float wobble)
            => Launch(ball, aim, lofted ? PassKind.Air : PassKind.Ground, charge01, powerMul, passer,
                      scatterDeg, wobble);

        /// <summary>Kind-aware launch. The bool overload above is what the AI and keeper paths call.</summary>
        public static void Launch(BallController ball, Vector3 aim, PassKind kind, float charge01,
                                  float powerMul, ActiveRagdoll passer, float scatterDeg, float wobble)
        {
            if (ball == null) return;
            bool lofted = kind != PassKind.Ground;

            Vector3 from = ball.transform.position;
            Vector3 flat = aim - from; flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 dir = dist > 0.05f ? flat / dist
                        : (passer != null ? passer.FacingRotation * Vector3.forward : Vector3.forward);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            // Scatter is a YAW error about the aim, so a misplaced pass still travels roughly the
            // right distance - it goes to the wrong player, which is what a bad pass looks like.
            if (scatterDeg > 0.01f)
                dir = Quaternion.AngleAxis(Random.Range(-scatterDeg, scatterDeg), Vector3.up) * dir;
            float weight = wobble > 0.001f ? 1f + Random.Range(-wobble, wobble) : 1f;

            // Spawn clear of the PASSER'S BODY, not the ball's position. The carried ball sits about
            // 0.72 m AHEAD of the body (DribbleNearDistance), so offsetting from the ball was only safe
            // while the aim was roughly forward - a look ray can point behind, and that put the spawn
            // inside the passer's own legs and fired the pass straight into them.
            Vector3 root = from;
            if (passer != null && passer.Pelvis != null)
            {
                Vector3 pp = passer.Pelvis.position;
                root = new Vector3(pp.x, from.y, pp.z);
            }
            Vector3 spawn = root + dir * SimConfig.PassSpawnFromBody;
            spawn.y = Mathf.Max(SimConfig.BallRadius, spawn.y);
            if (lofted) spawn += Vector3.up * SimConfig.PassSpawnLift;
            ball.ResetTo(spawn);

            if (lofted)
            {
                // Mis-weighting a lofted pass changes WHERE IT LANDS (short/long), which is the
                // whole tell on a bad chip. Solve the flight to that landing point.
                float land = Mathf.Max(1.5f, dist * weight);
                Vector3 target = spawn + dir * land;
                target.y = Mathf.Max(SimConfig.BallRadius, aim.y);
                float t = kind == PassKind.Chip ? ChipTime(spawn.y, target.y)
                                                : LoftTime(land, charge01);
                ball.LaunchTo(target, t, Vector3.zero, 0f);
                // A chip hangs long enough that the passer can run under their own ball and head it.
                // That is legal in open play and it is half the reason the pass exists, so the strike
                // suppression stays short rather than covering the whole flight.
                ball.SuppressStrikeFor(passer, 0.3f);
            }
            else
            {
                float v = GroundSpeed(dist, charge01, powerMul) * weight;
                ball.KickTo(dir * v + Vector3.up * SimConfig.PassGroundLift, passer);
            }
        }

        // ---------------------------------------------------------------- target choice

        /// <summary>A pass option: who, where to put it, and whether it is a ball into space.</summary>
        public struct Option
        {
            public Footballer mate;
            public Vector3 aim;      // world point to play the ball to (already led)
            public float score;
            public bool through;     // played into space ahead of the runner, not at their feet
        }

        /// <summary>
        /// Pick the best pass from `from` given the aim direction. Weighs alignment with where the
        /// player is pointing, forward progress, the receiver's space, range fit, and the lane, and
        /// offers a through ball when a runner has grass in front of them.
        /// </summary>
        public static bool BestTarget(Vector3 from, Vector3 aimDir, float attackZ, bool lofted, float charge01,
                                      float powerMul, List<Footballer> mates, List<Footballer> opps,
                                      Footballer exclude, out Option best)
        {
            best = default;
            if (mates == null) return false;

            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 0.0001f) aimDir = new Vector3(0f, 0f, attackZ);
            aimDir.Normalize();

            Vector3 fromFlat = new Vector3(from.x, 0f, from.z);
            float cone = SimConfig.PassAimConeDot;
            bool found = false;

            for (int i = 0; i < mates.Count; i++)
            {
                var f = mates[i];
                if (f == null || f == exclude || f.IsDown) continue;
                Vector3 fp = f.Pos; fp.y = 0f;
                Vector3 to = fp - fromFlat;
                float d = to.magnitude;
                if (d < 1.5f || d > SimConfig.PassMaxRange) continue;

                float dot = Vector3.Dot(aimDir, to / d);
                if (dot < cone) continue;

                // Lead the runner to where they will be when the ball arrives.
                Vector3 footAim = Lead(fromFlat, fp, f.Vel, lofted, charge01, powerMul, 1f);
                Option o = Score(fromFlat, aimDir, attackZ, footAim, f, opps, cone, false);

                // THROUGH BALL: a runner with grass ahead gets it played in front of them.
                Vector3 run = f.Vel; run.y = 0f;
                if (run.magnitude > SimConfig.PassThroughSpeedMin
                    && Vector3.Dot(run.normalized, new Vector3(0f, 0f, attackZ)) > 0.3f)
                {
                    Vector3 space = footAim + run.normalized
                                    * Mathf.Clamp(run.magnitude * SimConfig.PassThroughLeadMul, 2f, 9f);
                    if (NearestOppDist(space, opps) > SimConfig.PassThroughSpaceMin)
                    {
                        Option t = Score(fromFlat, aimDir, attackZ, space, f, opps, cone, true);
                        t.score *= SimConfig.PassThroughBonus;
                        if (t.score > o.score) o = t;
                    }
                }

                if (!found || o.score > best.score) { best = o; found = true; }
            }

            return found && best.score > SimConfig.PassMinScore;
        }

        static Option Score(Vector3 from, Vector3 aimDir, float attackZ, Vector3 aim, Footballer mate,
                            List<Footballer> opps, float cone, bool through)
        {
            Vector3 to = aim - from; to.y = 0f;
            float d = Mathf.Max(0.01f, to.magnitude);
            float dot = Vector3.Dot(aimDir, to / d);

            float align = Mathf.Clamp01((dot - cone) / Mathf.Max(0.01f, 1f - cone));
            float fwd = Mathf.Clamp01(((aim.z - from.z) * attackZ) / 18f);
            float open = Mathf.Clamp01(NearestOppDist(aim, opps) / SimConfig.PassOpenRadius);
            float fit = 1f - Mathf.Clamp01(Mathf.Abs(d - SimConfig.PassIdealRange) / SimConfig.PassRangeFalloff);
            bool lane = LaneClear(from, aim, opps, SimConfig.PassLaneRadius);

            float score = align * SimConfig.PassWeightAlign
                        + fwd   * SimConfig.PassWeightForward
                        + open  * SimConfig.PassWeightOpen
                        + fit   * SimConfig.PassWeightRange
                        + (lane ? SimConfig.PassWeightLane : 0f);

            return new Option { mate = mate, aim = aim, score = score, through = through };
        }

        /// <summary>Where to aim so a pass meets a moving receiver.</summary>
        public static Vector3 Lead(Vector3 from, Vector3 receiverPos, Vector3 receiverVel, bool lofted,
                                   float charge01, float powerMul, float leadMul)
        {
            Vector3 vel = receiverVel; vel.y = 0f;
            Vector3 aim = receiverPos;
            for (int i = 0; i < 2; i++)
            {
                float d = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(aim.x, 0f, aim.z));
                float t = Flight(d, lofted, charge01, powerMul);
                aim = receiverPos + vel * (t * SimConfig.PassLeadMul * leadMul);
            }
            aim.y = SimConfig.BallRadius;
            return aim;
        }

        /// <summary>Distance from `at` to the nearest standing opponent (huge if there are none).</summary>
        public static float NearestOppDist(Vector3 at, List<Footballer> opps)
        {
            if (opps == null) return 999f;
            Vector3 a = new Vector3(at.x, 0f, at.z);
            float best = 999f;
            for (int i = 0; i < opps.Count; i++)
            {
                var o = opps[i];
                if (o == null || o.IsDown) continue;
                Vector3 p = o.Pos; p.y = 0f;
                float d = Vector3.Distance(p, a);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>True if no standing opponent sits within `radius` of the segment a-b.</summary>
        public static bool LaneClear(Vector3 a, Vector3 b, List<Footballer> opps, float radius)
        {
            if (opps == null) return true;
            a.y = 0f; b.y = 0f;
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.01f) return true;
            float r2 = radius * radius;
            for (int i = 0; i < opps.Count; i++)
            {
                var o = opps[i];
                if (o == null || o.IsKeeper || o.IsDown) continue;
                Vector3 p = o.Pos; p.y = 0f;
                float u = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
                if ((p - (a + ab * u)).sqrMagnitude < r2) return false;
            }
            return true;
        }
    }
}
