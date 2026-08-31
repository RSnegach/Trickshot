using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Ball control the way football games actually do it: DISCRETE TOUCHES on a free ball,
    /// not a leash. This replaced a continuous spring "soft magnet", and the difference is the
    /// whole feel of the game.
    ///
    /// WHY A SPRING IS WRONG. A spring holds the ball at a point every single frame. That has
    /// no touches in it, so it has no rhythm, no error, and nothing a defender can poke away -
    /// the ball is welded to a moving anchor, and the player is a forklift. Every real football
    /// game instead leaves the ball a plain rigidbody and has the carrier KICK it, once per
    /// stride, hard enough to arrive where the next stride wants it. Between those kicks nobody
    /// owns the ball: it rolls, it slows down, it can be intercepted mid-roll.
    ///
    /// THE MECHANISM, in full:
    ///  - CADENCE. A touch lands once per step, and the step comes from Gait.Cadence - the same
    ///    function that drives the visible legs - so contact happens when a foot is actually
    ///    there. Faster running = more frequent touches.
    ///  - PACE SETS THE TOUCH. At a walk the ball is knocked barely a stride ahead (close
    ///    control). At full sprint it is knocked a long way out and the player runs onto it -
    ///    the KNOCK-ON. Top speed therefore costs control, which is the trade every football
    ///    game makes and the reason sprinting past a defender is a gamble.
    ///  - CLOSE CONTROL (modifier key). Shortest touches, more of them, reduced pace, much
    ///    quicker turn. For beating a man in a tight space. Sprint is ignored while it is held.
    ///  - EARLY TOUCHES. If the ball drops level with the feet or drifts out to one side, a
    ///    corrective touch fires immediately instead of waiting for cadence. This is what makes
    ///    the carry self-correct rather than drift away.
    ///  - TURNING. Facing change since the last touch shortens the push toward the body, so a
    ///    sharp turn drags the ball around you instead of squirting it off at the old angle -
    ///    and scatters the touch more, because that is a hard thing to do.
    ///  - FIRST TOUCH. Taking possession cushions whatever pace the ball arrived with. How dead
    ///    that touch is comes straight off the Control stat.
    ///  - ERROR. Every touch is scattered a few degrees, scaled by pace, turn sharpness and
    ///    (inversely) Control. A raw build sprays the ball; a Control build keeps it glued.
    ///
    /// Possession is never granted for a ball that is airborne, genuinely fast, or closing on
    /// the feet faster than a trap could cushion, so a served cross or a struck shot is never
    /// swallowed. Exactly one body can carry at a time (see the static holder), which is both
    /// physically true and what lets every body in a match run this component at once.
    ///
    /// Collision between the ball and the CARRIER'S OWN colliders is suspended for the duration
    /// of a carry. That is deliberate: the ragdoll's legs are physics bodies swinging at gait
    /// speed, so leaving them live would punt the ball on random frames and fight every touch.
    /// The touch model supplies those impulses instead. The ball still collides with everything
    /// else - ground, walls, keepers, other players - so it can be tackled and intercepted.
    ///
    /// SHOOTING AND VOLLEYING ARE NEVER SUPPRESSED GLOBALLY. Carry ownership is registered on the
    /// ball itself (BallController.DribbleCarrier), so only the CARRIER'S own contacts skip the
    /// strike path - every other body strikes as normal, which is what lets a defender shoot or
    /// volley the ball straight off a carrier's feet. And a capture is refused outright while a
    /// leg button is held, so a ball you are lining up to strike is never trapped out from under
    /// you at the last moment.
    /// </summary>
    public class Dribble : MonoBehaviour
    {
        /// <summary>
        /// Raised when a carried ball is STRUCK at goal, with the striker's body. Exists so match-stat
        /// tracking can count a human's shots without Dribble having to know what a MatchGame is.
        /// Subscribers must unsubscribe: it is static, so a driver that forgets keeps a dead match alive.
        /// </summary>
        public static System.Action<ActiveRagdoll> ShotFired;

        IStrikerInput _input;
        Striker _striker;
        ActiveRagdoll _ragdoll;
        BallController _ball;

        bool _carrying;
        float _cooldown;          // after a shot, don't re-capture for a moment
        float _touchTimer;        // counts down to the next touch
        Vector3 _lastTouchFace;   // facing at the last touch, for measuring turn sharpness

        // Exactly ONE body holds the ball at a time - it is one ball. Tracking the holder
        // statically is what makes it safe to leave this component enabled on every body in a
        // match: whoever gets there first carries, and nobody else can capture underneath them.
        static Dribble _holder;

        /// <summary>Whoever is carrying the ball right now, or null.</summary>
        public static Dribble Holder => _holder;

        /// <summary>Drop the ball from whoever is carrying it. FORCED, no contest - whistles, resets,
        /// kickoffs and teleports are not challenges, they are the ball leaving play. A TACKLE must go
        /// through ContestTackle below instead.</summary>
        public static void ReleaseHolder()
        {
            if (_holder != null) _holder.ForceRelease();
        }

        // ---------------------------------------------------------------------------------------
        // THE TACKLE CONTEST
        //
        // MEASURED BEFORE THIS EXISTED, in a live 5-a-side (Normal, keeper 0.5, gravity -19.60,
        // fdt 0.0200), by invoking Footballer.TryTackle at a swept distance over 10 approach angles,
        // 20 trials each, and reading MatchGame._flash for the win:
        //
        //     0.20 m 20/20   1.00 m 20/20   1.55 m 20/20   1.65 m 0/20   2.20 m 0/20
        //     0.60 m 20/20   1.40 m 20/20   1.60 m 19/20   1.80 m 0/20   3.00 m 0/20
        //
        // 200 trials, ZERO variance, no dependence on angle, on the carrier, or on anything else.
        // (1.60 read 19/20 only from float error on the sample circle's radius.) The tackle was a
        // STEP FUNCTION on |ball - tackler| <= SimConfig.TackleReach 1.6, and that is the whole of
        // the "tackles always steal the ball" complaint.
        //
        // AND 1.6 m WAS FREE BY CONSTRUCTION. A carrier at a sprint knocks the ball
        // DribbleSprintDistance 2.35 m AHEAD of himself, and AiChaseStopDist 0.6 means a presser
        // closes to 0.6 m of the BALL - so the presser sits inside the reach on every approach,
        // permanently. The only stochastic element in the whole system was whether the bot bothered:
        // Random.value < Lerp(0.35, 1, Decision), which at Normal (0.60) is 0.74. With
        // TackleCooldown 0.9 s a pressed carrier lost the ball in 0.9/0.74 = 1.2 s of a defender
        // arriving.
        //
        // WHAT REPLACES IT. A challenge must first be IN POSITION (a hard gate, because "you were
        // behind him" is something a player can see and fix), then wins on a product of four terms.
        // The numbers are set against real tackle success, which sits near half of attempts:
        //
        //     square challenge, average carrier                       0.34  <- TackleBaseWin
        //     head-on, on the ball, closing hard, into a sprinter
        //       who has just knocked it out in front                   0.80  <- TackleWinMax
        //     from behind, at full stretch, flat-footed, against
        //       close control                                          0.05  <- TackleWinMin
        //
        // At 0.34 behind the same 0.74 attempt roll a carrier survives 1/(0.74*0.34) = 4.0 attempts,
        // about 3.6 s of sustained pressure against the measured 1.2 s. THAT is the target: three or
        // four goes, not one.
        //
        // LIMITS, plainly. (1) The tuning constants live here rather than in SimConfig because
        // SimConfig is owned elsewhere this round; they belong next to TackleReach. (2) Close control
        // and the Control stat can only be read for a HUMAN carry - a bot carries the ball through
        // Footballer, not through this component - so those two terms drop out of a bot-vs-bot
        // challenge. Everything else, including the whole vulnerability window, is geometry and
        // applies to both. (3) "Foul" here only means the carrier goes down and the tackler is
        // punished for longer; match has no free kick to award yet.
        // ---------------------------------------------------------------------------------------

        public enum TackleResult
        {
            NoCarrier,   // nobody was carrying: this was never a tackle, do NOT knock the ball
            WrongSide,   // trailing the carrier, or out of reach: not a position to challenge from
            Won,         // ball won; the carry is ALREADY released, caller may knock it loose
            Beaten,      // lost the contest - tackler is off balance
            Foul         // lost a COMMITTED (slide) challenge - carrier goes down, tackler punished
        }

        public const float TackleBaseWin       = 0.34f;  // square challenge, average carrier
        public const float TackleWinMin        = 0.05f;
        public const float TackleWinMax        = 0.80f;
        // How much FURTHER from the ball than the carrier a challenger may still be. 1.10 m lets a
        // defender who has drawn roughly level poke it away (the angle term then docks him to
        // TackleBehindMul); it denies one still trailing by two metres, which was the case that read
        // as the ball teleporting backwards out of your feet.
        public const float TackleBallSideSlack = 1.10f;
        public const float TackleBehindMul     = 0.45f;  // arriving from directly behind the carrier
        public const float TackleFrontMul      = 1.15f;  // meeting him head-on
        public const float TackleCleanDist     = 0.55f;  // AiChaseStopDist 0.6 minus a hair: right on it
        public const float TackleOnItMul       = 1.35f;
        public const float TackleStretchMul    = 0.30f;  // at the very edge of SimConfig.TackleReach
        public const float TackleFlatMul       = 0.75f;  // challenger not closing on the ball at all
        public const float TackleChargeMul     = 1.30f;
        public const float TackleChargeSpeed   = 4f;     // m/s of closing speed that earns the full bonus
        // Carrier terms. The GAP is the real vulnerability window: the touch model itself puts the
        // ball DribbleNearDistance 0.72 m out at a walk and 2.35 m out at a sprint, so "he has pushed
        // it too far in front" is already simulated and the contest only has to read it. Pace is kept
        // as a SEPARATE term because a sprinter also cannot change direction - which is what finally
        // makes the pace/control trade this class comment claims real actually cost something.
        public const float TackleGapBonus      = 0.50f;
        public const float TackleSprintBonus   = 0.40f;
        public const float TackleCloseCtrlCut  = 0.40f;
        public const float TackleTightnessCut  = 0.30f;

        /// <summary>
        /// Contest a tackle against whoever is carrying. On Won the carry is ALREADY released and the
        /// caller may knock the ball loose; on ANY other result the caller must leave the ball alone.
        /// `flash` is a curt reason for the HUD, because a player who cannot see why he lost the ball
        /// reads every loss as broken - which is exactly how we got here.
        /// </summary>
        public static TackleResult ContestTackle(ActiveRagdoll tackler, ActiveRagdoll carrier,
                                                Vector3 ballPos, bool committed, out string flash)
        {
            flash = null;
            if (tackler == null || tackler.Pelvis == null) return TackleResult.NoCarrier;

            // Prefer the caller's carrier, fall back to the human holder. NO CARRIER MEANS A LOOSE
            // BALL, AND A LOOSE BALL IS NOT A TACKLE. The old path still knocked it away and felled
            // the nearest opponent for it (the AI gate is _acting != Phase.Attack, which includes
            // Phase.Loose), which was a second way possession changed for free.
            Dribble hold = _holder;
            if (carrier == null && hold != null) carrier = hold._ragdoll;
            if (carrier == null || carrier.Pelvis == null || carrier == tackler) return TackleResult.NoCarrier;
            if (hold != null && hold._ragdoll != carrier) hold = null;   // the holder is somebody else

            Vector3 ball = ballPos; ball.y = 0f;
            Vector3 me = tackler.Pelvis.position; me.y = 0f;
            Vector3 him = carrier.Pelvis.position; him.y = 0f;
            float myBall = Vector3.Distance(me, ball);
            float hisBall = Vector3.Distance(him, ball);

            // HARD GATE. A rule, not a multiplier, on purpose: it is the most legible kind of loss.
            if (myBall > SimConfig.TackleReach) { flash = "TOO FAR"; return TackleResult.WrongSide; }
            if (myBall > hisBall + TackleBallSideSlack) { flash = "WRONG SIDE"; return TackleResult.WrongSide; }

            // 1. ANGLE. Compare the two approach vectors INTO the ball: +1 means we arrive on the same
            //    line (from behind him), -1 head-on. From behind is where fouls come from, not tackles.
            Vector3 mine = ball - me, his = ball - him;
            float sameLine = (mine.sqrMagnitude > 1e-4f && his.sqrMagnitude > 1e-4f)
                           ? Vector3.Dot(mine.normalized, his.normalized) : 0f;
            float angleMul = Mathf.Lerp(TackleFrontMul, TackleBehindMul,
                                        Mathf.InverseLerp(-1f, 1f, sameLine));

            // 2. TIMING, as distance from the ball at the instant of the challenge. The sweep above
            //    proves 0.20 m and 1.59 m used to be identical; this is the term that separates them,
            //    and it is the one a player learns first - get closer, win more.
            float timeMul = Mathf.Lerp(TackleOnItMul, TackleStretchMul,
                                       Mathf.InverseLerp(TackleCleanDist, SimConfig.TackleReach, myBall));

            // 3. MOMENTUM: closing speed RELATIVE to the carrier, along the line to the ball. A
            //    flat-footed defender standing beside a runner should not take it off him.
            Vector3 rel = tackler.MoveInput - carrier.MoveInput; rel.y = 0f;
            Vector3 toBall = mine.sqrMagnitude > 1e-4f ? mine.normalized : Vector3.forward;
            float closing = Mathf.Max(0f, Vector3.Dot(rel, toBall));
            float moMul = Mathf.Lerp(TackleFlatMul, TackleChargeMul,
                                     Mathf.Clamp01(closing / TackleChargeSpeed));

            // 4. CARRIER STATE. The gap is pure geometry so it works for a bot carrier too; the two
            //    Dribble-only terms simply drop out for one (see LIMITS above).
            float gap01 = Mathf.Clamp01(Mathf.InverseLerp(SimConfig.DribbleNearDistance,
                                                         SimConfig.DribbleLoseRadius, hisBall));
            float carrierMul = 1f + TackleGapBonus * gap01
                                  + TackleSprintBonus * Sprint01(carrier.GroundSpeed);
            if (hold != null)
            {
                if (hold.CloseControl) carrierMul -= TackleCloseCtrlCut;
                carrierMul -= TackleTightnessCut * Mathf.Clamp01(hold.Tightness);
            }
            carrierMul = Mathf.Clamp(carrierMul, 0.35f, 1.9f);

            float p = Mathf.Clamp(TackleBaseWin * angleMul * timeMul * moMul * carrierMul,
                                  TackleWinMin, TackleWinMax);

            if (Random.value < p)
            {
                if (hold != null) hold.ForceRelease();
                flash = "TACKLE!";
                return TackleResult.Won;
            }

            // MISSING HAS TO COST. Without this the contest is only a SLOWER steal - a defender would
            // hold the button until a roll landed, and at 0.74 attempts a second that is four seconds.
            // Beaten 0.55 s against TackleCooldown 0.9 s is about 60% of his next attempt window: a
            // real cost he recovers from. A committed slide that misses is a foul and costs double.
            var mine_kd = tackler.GetComponentInParent<Knockdown>();
            if (mine_kd != null)
                mine_kd.Stumble(committed ? Knockdown.BeatenSlideTime : Knockdown.BeatenTime);

            if (!committed) { flash = "BEATEN"; return TackleResult.Beaten; }

            var his_kd = carrier.GetComponentInParent<Knockdown>();
            if (his_kd != null) his_kd.Fell(him - me);
            flash = "FOUL";
            return TackleResult.Foul;
        }

        // Master switch: dribbling is only ENABLED in modes where the ball is live. Dead-ball
        // galleries (free kick, penalty, accuracy) leave it false. Off by default; the mode
        // builder opts in.
        public bool Enabled = false;

        // Set-piece suspension: a free kick / penalty (or any dead-ball setup) turns this on
        // so the ball parked at the spot is NOT auto-captured while the taker walks up. The
        // game mode clears it once the kick is taken / play is live again.
        public bool SetPieceActive = false;

        public bool Carrying => _carrying;

        /// <summary>Close-control modifier held (shortest touches, less pace, sharper turns).</summary>
        public bool CloseControl => _input != null && _input.CloseControlHeld;

        public void Init(IStrikerInput input, Striker striker, ActiveRagdoll ragdoll, BallController ball)
        {
            _input = input;
            _striker = striker;
            _ragdoll = ragdoll;
            _ball = ball;
        }

        /// <summary>
        /// Swap the input source at runtime, the same way Striker.SetInput does. The HOST binds a
        /// remote player's NetInputSource here; without this, every body in a networked match
        /// read the local device and one player's sprint/shoot reached everyone's ball.
        /// </summary>
        public void SetInput(IStrikerInput input) => _input = input;

        // Tightness 0..1 from the Control trap stat: shorter touches, wider capture net,
        // deader first touch, less scatter.
        float Tightness => PlayerProfile.DribbleTightness;

        float CaptureRadius => SimConfig.DribbleCaptureRadius + SimConfig.DribbleTrapCaptureBonus * Tightness;

        // Flat position of the feet.
        Vector3 Feet()
        {
            Vector3 f = _ragdoll.Pelvis.position;
            f.y = 0f;
            return f;
        }

        void OnDisable()
        {
            // Never leave a destroyed/disabled body as the holder: a stale static reference would
            // lock possession out for every body in the NEXT match.
            if (_holder == this) _holder = null;
            if (_carrying) StopCarry();
        }

        void FixedUpdate()
        {
            // A torn-down body must not stay the carrier: that would keep ball ownership (and so
            // the strike skip, and the ignored collision) alive on a corpse forever.
            if (_ball == null || _ragdoll == null || _ragdoll.Pelvis == null) { StopCarry(); return; }

            // Off entirely unless the mode enables dribbling, and never during a set piece
            // (free kick / penalty) - the ball must stay parked at the spot.
            if (!Enabled || SetPieceActive) { StopCarry(); return; }

            float dt = Time.fixedDeltaTime;
            if (_cooldown > 0f) _cooldown -= dt;

            // No carrying airborne or mid-trick (dive/bicycle own the body and the ball).
            bool canDribble = _ragdoll.IsGrounded && !_striker.IsBusy && _striker.ControlEnabled;
            if (!canDribble) { StopCarry(); return; }

            Vector3 feet = Feet();
            Vector3 face = _striker.FacingForward;
            Vector3 ballFlat = _ball.Rb.position; ballFlat.y = 0f;
            float ballDist = Vector3.Distance(ballFlat, feet);

            if (_carrying)
            {
                // A leg button strikes the ball as a real shot and ends the carry.
                if (WantsKick()) { ReleaseShot(); return; }

                // Knocked away (tackle, keeper, wall, bad bounce) or lifted off the deck:
                // possession is gone. No leash pulls it back.
                if (ballDist > SimConfig.DribbleLoseRadius || !BallIsLow()) { StopCarry(); return; }

                TickCarry(dt, feet, face);
            }
            else if (CanCapture(feet, ballDist))
            {
                StartCarry();
            }
        }

        // The ball is on the deck (or skipping along it) rather than in flight.
        bool BallIsLow() => _ball.Rb.position.y <= SimConfig.BallRadius * SimConfig.DribbleMaxBallHeight;

        /// <summary>
        /// Can this body take the ball right now? Near the feet, low, off cooldown, nobody else
        /// already on it, and not arriving faster RELATIVE TO THIS PLAYER than a trap could
        /// cushion. That last test is the one that keeps a served cross or a struck shot from
        /// being swallowed while still letting a carrier take a ball rolling along at their pace.
        /// </summary>
        bool CanCapture(Vector3 feet, float ballDist)
        {
            if (_cooldown > 0f) return false;
            // A HELD LEG BUTTON means the player is lining up a strike, so never trap the ball out
            // from under them. This matters most in the 0.25m-0.48m band where a ball is high
            // enough to volley (VolleyMinBallHeight) but still low enough to capture: without this
            // veto, a ball dropping through that band while you hold the leg up is swallowed as a
            // dribble touch and the volley never fires.
            if (_input != null && (_input.LeftLegHeld || _input.RightLegHeld)) return false;
            if (_holder != null && _holder != this) return false;
            if (ballDist > CaptureRadius) return false;
            if (!BallIsLow()) return false;

            var rb = _ball.Rb;
            if (rb.linearVelocity.magnitude > SimConfig.DribbleCaptureMaxSpeed) return false;

            Vector3 ballVel = rb.linearVelocity; ballVel.y = 0f;
            Vector3 myVel = _ragdoll.MoveInput; myVel.y = 0f;
            return (ballVel - myVel).magnitude <= SimConfig.DribbleCaptureApproachMax;
        }

        // One carried step: touch on cadence, or early if the ball is no longer where it should
        // be. Between touches this does nothing but keep the rolling spin looking right.
        void TickCarry(float dt, Vector3 feet, Vector3 face)
        {
            _touchTimer -= dt;

            Vector3 ballFlat = _ball.Rb.position; ballFlat.y = 0f;

            if (_touchTimer > 0f && !NeedsCorrectiveTouch(feet, face, ballFlat))
            {
                RollSpin(_ball);
                return;
            }

            PushTouch(feet, face);
        }

        /// <summary>
        /// Is the ball no longer where the carry wants it, so a touch is due NOW rather than on
        /// the next stride? True when it has fallen level with the feet, or drifted off the
        /// running line. The lateral band opens out with how far ahead the ball is, because a
        /// sprint knock-on lands metres away and its own aim scatter would otherwise read as
        /// "wide" on every single touch. Shared with the AI carrier.
        /// </summary>
        public static bool NeedsCorrectiveTouch(Vector3 feet, Vector3 face, Vector3 ballFlat)
        {
            Vector3 toBall = ballFlat - feet; toBall.y = 0f;
            float ahead = Vector3.Dot(toBall, face);
            if (ahead < SimConfig.BallRadius + SimConfig.DribblePushMinAhead) return true;

            float side = Vector3.Dot(toBall, Vector3.Cross(Vector3.up, face));
            float tol = SimConfig.DribbleSideTolerance + SimConfig.DribbleSideToleranceFrac * ahead;
            return Mathf.Abs(side) > tol;
        }

        // Work out this touch's cadence, distance and scatter from pace / Control / turn
        // sharpness, then hand it to the shared touch primitive.
        void PushTouch(Vector3 feet, Vector3 face)
        {
            bool close = CloseControl;
            float speed = _ragdoll.GroundSpeed;
            float sprint01 = Sprint01(speed);

            float interval = StrideInterval(_ragdoll, close);

            float dist = TouchDistance(speed, Tightness, close);

            // Turning: drag the ball in toward the body, and scatter the touch more.
            float turn01 = _lastTouchFace.sqrMagnitude > 1e-4f
                         ? Mathf.Clamp01(Vector3.Angle(_lastTouchFace, face) / SimConfig.DribbleTurnTightenDeg)
                         : 0f;
            dist *= Mathf.Lerp(1f, SimConfig.DribbleTurnTightenMul, turn01);

            // Control cuts the base scatter outright and blunts the pace/turn penalties.
            float t = Tightness;
            float err = SimConfig.DribbleTouchErrorDeg * (1f - t)
                      + SimConfig.DribbleTouchErrorSpeedDeg * sprint01 * (1f - 0.6f * t)
                      + SimConfig.DribbleTurnErrorDeg * turn01 * (1f - 0.6f * t);
            if (close) err *= SimConfig.DribbleCloseErrorMul;

            Vector3 myVel = _ragdoll.MoveInput; myVel.y = 0f;
            Touch(_ball, feet, face, myVel, interval, dist, err);

            _touchTimer = interval;
            _lastTouchFace = face;
        }

        /// <summary>
        /// How far in front of the feet a carrier at this pace knocks the ball. Walk -> sprint
        /// (the knock-on), pulled in by the Control stat, pulled in much further by close
        /// control. Shared with the AI carrier so bots and humans knock it the same distance.
        /// </summary>
        public static float TouchDistance(float groundSpeed, float tightness, bool closeControl)
        {
            float d = Mathf.Lerp(SimConfig.DribbleNearDistance, SimConfig.DribbleSprintDistance,
                                 Sprint01(groundSpeed));
            d *= 1f - SimConfig.DribbleTrapTightenMax * Mathf.Clamp01(tightness);
            if (closeControl) d *= SimConfig.DribbleCloseDistMul;
            return d;
        }

        // 0 at a walk, 1 at full sprint. Same breakpoints the gait uses, so the touch model and
        // the legs agree on what "sprinting" means.
        static float Sprint01(float speed)
            => Mathf.Clamp01(Mathf.InverseLerp(SimConfig.StrikerMoveSpeed * 0.9f,
                                               SimConfig.StrikerMoveSpeed * SimConfig.StrikerSprintMul,
                                               speed));

        /// <summary>
        /// Seconds between touches for this body: one per STEP, taken from the same gait cadence
        /// that drives the visible legs (a full cycle is 2pi, so a step is pi). Clamped so a
        /// standstill does not stall the carry and a sprint does not machine-gun it.
        /// </summary>
        public static float StrideInterval(ActiveRagdoll ragdoll, bool closeControl)
        {
            var p = Gait.For(ragdoll.Plan);
            float sprint01;
            float cadence = Gait.Cadence(ragdoll.GroundSpeed, ragdoll.HeightScale, p, out sprint01);
            float step = cadence > 0.01f ? Mathf.PI / cadence : SimConfig.DribbleTouchIntervalMax;
            step *= SimConfig.DribbleTouchStrideFrac;
            if (closeControl) step *= SimConfig.DribbleCloseIntervalMul;
            return Mathf.Clamp(step, SimConfig.DribbleTouchIntervalMin, SimConfig.DribbleTouchIntervalMax);
        }

        /// <summary>
        /// THE touch primitive - the single place a carried ball is ever kicked, used by the
        /// player's carry and by the AI carrier so bots and humans control the ball identically.
        ///
        /// Aims the ball at where the carrier will want it one touch from now: their feet
        /// advanced by their own velocity, plus `touchDist` along their facing. The velocity is
        /// whatever covers that gap in `interval`, over-hit slightly for what rolling friction
        /// will eat, scattered by `errorDeg`, capped so a touch can never become a pass, and
        /// given a few centimetres of hop so it reads as a kick rather than a slide.
        /// </summary>
        public static void Touch(BallController ball, Vector3 feet, Vector3 face, Vector3 carrierVel,
                                 float interval, float touchDist, float errorDeg)
        {
            var rb = ball.Rb;
            Vector3 ballFlat = rb.position; ballFlat.y = 0f;

            face.y = 0f;
            face = face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward;
            carrierVel.y = 0f;

            // Where the ball should be by the next touch: ahead of where the carrier will be.
            Vector3 target = feet + carrierVel * interval + face * touchDist;

            Vector3 want = (target - ballFlat) / Mathf.Max(0.05f, interval) * SimConfig.DribbleRollLossComp;
            if (errorDeg > 0.01f)
                want = Quaternion.AngleAxis(Random.Range(-errorDeg, errorDeg), Vector3.up) * want;

            // DEADBAND, not a floor. If the ball is already sitting where the next stride wants
            // it, kill it dead under the studs. Clamping up to a minimum instead would have a
            // standing player quietly walking the ball away from himself, one nudge per step.
            float need = want.magnitude;
            if (need < SimConfig.DribbleTouchMinSpeed)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                return;
            }

            Vector3 dir = want / need;
            Vector3 push = dir * Mathf.Min(need, SimConfig.DribbleTouchMaxSpeed);

            rb.linearVelocity = new Vector3(push.x, SimConfig.DribbleTouchHop, push.z);
            RollSpin(ball);

            // NO SuppressStrike here, deliberately. The carrier's gait does swing physics legs
            // through the ball, but those contacts are already handled two ways that cost nobody
            // else anything: the carrier's own colliders are ignored for the whole carry, and the
            // ball skips the strike path for the CARRIER'S body specifically. Suppressing strikes
            // globally on every touch (the old behaviour) also killed everyone else's shot and
            // volley, on this ball, for as long as anyone was dribbling it.
        }

        // Rolling spin about the axis perpendicular to travel, for looks.
        static void RollSpin(BallController ball)
        {
            var rb = ball.Rb;
            Vector3 flat = rb.linearVelocity; flat.y = 0f;
            if (flat.sqrMagnitude <= 0.04f) return;
            rb.angularVelocity = Vector3.Cross(Vector3.up, flat.normalized) * (flat.magnitude * SimConfig.DribbleSpinScale);
        }

        void StartCarry()
        {
            _carrying = true;
            _holder = this;
            _ball.SetDribbleCarrier(_ragdoll);   // only THIS body's contacts stop being strikes
            IgnoreStrikerCollision(true);        // the touch model owns the ball; swinging legs don't

            // FIRST TOUCH. Cushion whatever pace it arrived with - dead at the feet for a Control
            // build, bouncing away off the shin for a raw one - then settle briefly before the
            // first pushing touch, so taking the ball reads as a trap and not a snap.
            var rb = _ball.Rb;
            float keep = Mathf.Lerp(SimConfig.DribbleFirstTouchKeepRaw,
                                    SimConfig.DribbleFirstTouchKeepSkilled, Tightness);
            Vector3 kept = rb.linearVelocity; kept.y = 0f;
            kept *= keep;

            float err = SimConfig.DribbleTouchErrorDeg * (1f - Tightness);
            if (kept.sqrMagnitude > 0.04f && err > 0.01f)
                kept = Quaternion.AngleAxis(Random.Range(-err, err), Vector3.up) * kept;

            rb.linearVelocity = new Vector3(kept.x, 0f, kept.z);
            RollSpin(_ball);

            _touchTimer = SimConfig.DribbleFirstTouchSettle;
            _lastTouchFace = _striker != null ? _striker.FacingForward : Vector3.forward;
        }

        void StopCarry()
        {
            if (!_carrying) return;
            _carrying = false;
            if (_holder == this) _holder = null;
            // Clear the claim only if it is still OURS, so releasing never steals another body's.
            if (_ball != null && _ball.DribbleCarrier == _ragdoll) _ball.SetDribbleCarrier(null);
            IgnoreStrikerCollision(false);
        }

        // Toggle physical collision between the ball and this body's own colliders. See the
        // class note: the gait's legs would otherwise punt the ball on random frames.
        void IgnoreStrikerCollision(bool ignore) => SetCarryCollision(_ball, _ragdoll, ignore);

        /// <summary>
        /// Suspend (or restore) collision between the ball and ONE body's own colliders for the
        /// duration of a carry. Shared with the AI carrier so a bot's swinging legs don't fight
        /// its own touches either. See the class note for why a carry needs this.
        /// </summary>
        public static void SetCarryCollision(BallController ball, ActiveRagdoll ragdoll, bool ignore)
        {
            if (ball == null || ragdoll == null) return;
            var ballCol = ball.GetComponent<Collider>();
            if (ballCol == null) return;
            var own = ragdoll.OwnColliders;
            for (int i = 0; i < own.Count; i++)
                if (own[i] != null) Physics.IgnoreCollision(ballCol, own[i], ignore);
        }

        // A kick request: a leg button (LMB/RMB) pressed this frame. ONLY the button releases
        // the ball as a shot - the gait swings the feet past any speed threshold, so a
        // fast-swing test would boot the ball just from running. Button-only means carrying is
        // a pure carry and you shoot on purpose.
        bool WantsKick()
        {
            if (_input == null) return false;
            if (!(_input.LeftClickPressed || _input.RightClickPressed)) return false;
            // A charge in progress owns the ball: do not take the press as a flat release. Suppressing
            // it HERE rather than inside the launcher matters, and two separate defects say why.
            //
            //  1. ReleaseShot has already run StopCarry() by the time a launcher could bail, so the
            //     ball is live at the feet with ball-vs-own-limb collision restored and NOT covered by
            //     SuppressStrikeFor - which is the only thing that stops the launching boot re-hitting
            //     it. The leg then rises through a ball sitting 0.72-2.35 m ahead for up to
            //     PassMaxCharge, and a leg-bone contact is an unconditional strike with no speed floor.
            //     The overwhelmingly likely outcome was a full uncharged punt a frame or two after the
            //     press, which killed the charged shot in its main use case.
            //  2. ReleaseShot fires Dribble.ShotFired before it launches, so bailing later still banked
            //     the stat. MatchGame counts that into shots-on-goal, and the keeper save-rate
            //     target is measured as saves / (saves + conceded) over exactly that counter - so the
            //     denominator doubled for every human shot.
            //
            // Order-safe across the Update/FixedUpdate split: Dribble runs in FixedUpdate and
            // Striker.Tick from Update, but WantsChargedShot is a LIVE property over the held-button
            // flags with no cached Update state, so it reads correctly from either clock.
            return !(_striker != null && _striker.WantsChargedShot);
        }

        // End the carry and launch the ball as a shot along the facing/aim direction, scaled by
        // the striker's shot power. Routes through BallController.DribbleShot so it shares the
        // facing-gated goal assist + ball-cam pulse with normal strikes. Then hold off
        // re-capture so the same touch doesn't immediately re-take the ball.
        void ReleaseShot()
        {
            float speed = SimConfig.DribbleShotSpeed * PlayerProfile.ShotPowerMul;

            // Same sight-cone gate as a struck shot: only assist when facing the goal.
            Vector3 toGoal = SimConfig.AttackGoalCenter - _ragdoll.Pelvis.position; toGoal.y = 0f;
            Vector3 face = _striker.FacingForward;
            float dot = toGoal.sqrMagnitude > 0.01f ? Vector3.Dot(face, toGoal.normalized) : -1f;
            bool facingGoal = dot >= SimConfig.AssistFacingDot;        // tight cone: aim assist
            // Ball-cam ONLY for a shot facing AWAY from goal (over-shoulder).
            bool camShouldCut = dot < SimConfig.ShotCamFaceAwayDot;

            StopCarry();   // end the carry BEFORE the shot, so our own carry claim can't block it
            // A shot, for whoever is counting. Dribble holds no reference to a game driver and should
            // not gain one for a statistic, so it announces instead - and only when the strike is
            // actually goal-directed, or a backward tap-out would be filed as a shot on goal. Every
            // human body has its own Dribble, so this covers the single-player striker and every
            // networked slot alike.
            if (facingGoal) ShotFired?.Invoke(_ragdoll);
            // Match: a deliberate shot leaves the ground and follows set-piece flight (arced, no
            // controllable spin). Elsewhere it's the usual flat dribble drive.
            if (_ball.MatchLoftKicks)
                _ball.LaunchLofted(_striker.FacingForward, speed, facingGoal, camShouldCut, _ragdoll);
            else
                _ball.DribbleShot((_striker.FacingForward + Vector3.up * SimConfig.DribbleShotLift).normalized,
                                  speed, facingGoal, camShouldCut, _ragdoll);
            _cooldown = SimConfig.DribbleRecaptureCooldown;
        }

        // Give up the ball on hard resets, tackles and passes.
        public void ForceRelease()
        {
            StopCarry();
            _cooldown = SimConfig.DribbleRecaptureCooldown;
        }
    }
}
