using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The AI goalkeeper. An active-ragdoll keeper that actually keeps goal, used by every mode
    /// with a keeper in it (striker practice, free kicks, accuracy, time trial, and both ends of a
    /// match via Footballer).
    ///
    /// What a keeper does, and what this now does:
    ///   - POSITIONS ON THE ANGLE. He stands on the line from the ball to the middle of his goal,
    ///     off his line in proportion to how far out the ball is. That is the whole job: from wide,
    ///     covering the near post also covers the far one, and a keeper welded to the centre of his
    ///     line (which is all the old one could do, world-X only) is beaten by geometry every time.
    ///   - COMES OFF HIS LINE. He moves in Z as well as X, so he closes down and narrows the angle
    ///     instead of waiting on the six-yard line for the ball to arrive.
    ///   - SWEEPS. A loose slow ball near his goal gets claimed rather than watched. He will not
    ///     pluck it out of a dribbler's feet - that needs a challenge, not a keeper.
    ///   - CATCHES AND HOLDS, then distributes (see KeeperHands). A ball he can gather is gathered;
    ///     anything struck too hard to catch is dived at and parried, which is what a keeper does.
    ///   - DIVES IN THREE BANDS. Which dive he plays comes from the PREDICTED BALL HEIGHT, not from
    ///     one dive with one number: LOW (star pose, flat, gloves on the turf), MID (star pose, full
    ///     layout - the star's tall bar of arm is the right hedge here because the height prediction
    ///     is least reliable in the middle), HIGH (KeeperPose.DiveHigh at a 55 deg layout, so the
    ///     reach goes up AND out). A high ball straight over him is a straight JUMP
    ///     (KeeperPose.Jump): a standing keeper's gloves top out at 2.32 m against a 2.44 m
    ///     crossbar, so not jumping at that ball is a goal by 12 cm.
    ///   - COMMITS LATER ON EASIER SETTINGS rather than moving slower. Difficulty buys reaction time
    ///     (0.55 s down to 0.05 s) and near-nothing else: dive reach moves only +-12% across the
    ///     whole ladder, on purpose.
    ///
    /// Everything scales with SimConfig.KeeperAbility: tracking sharpness, how far he dares come
    /// off his line, dive reach, and how cleanly he gathers.
    ///
    /// Driven by Tick() from the owning mode loop (not FixedUpdate), so it stays in lockstep with
    /// the rest of that mode.
    ///
    /// GEOMETRY. The keeper is told which goal he defends and which way the pitch is (`outSign`,
    /// the world-Z direction from his goal toward play), so the same brain works at the +Z goal in
    /// striker mode and at BOTH ends of a match. The old class hard-coded SimConfig.KeeperFaceDir.
    /// </summary>
    public class Goalkeeper : MonoBehaviour
    {
        ActiveRagdoll _ragdoll;
        BallController _ball;
        Quaternion _facing;
        Vector3 _goal;      // centre of the goal he defends, on the goal line
        float _out = -1f;   // world-Z sign from that goal toward the pitch

        readonly KeeperHands _hands = new KeeperHands();

        /// <summary>
        /// Optional: given the keeper's position, where to play a gathered ball. Null punts it
        /// straight upfield (all the single-goal modes have nobody to pass to).
        /// </summary>
        public System.Func<Vector3, Vector3> DistributeTarget;

        /// <summary>
        /// Full-match keeper: comes properly off his line and sweeps up loose balls. OFF by default,
        /// which is what the single-goal drills need. A free-kick keeper who wanders 5 m off his line
        /// is just asking to be chipped, and a striker-practice keeper who sprints out to grab the
        /// ball before it has even been served ends the drill.
        /// </summary>
        public bool Sweeper;

        enum State { Guard, Diving, Holding }
        State _state = State.Guard;
        float _diveAir, _diveGround, _diveCooldown, _diveDir;
        float _shufflePhase;
        float _ability;   // cached each Tick so the gaits can scale cadence with skill

        // Which height band the current dive is, so ManageDive holds the right pose and the right
        // layout for the whole flight instead of inferring it from _lowPose being null.
        enum Band { Low, Mid, High, Jump }
        Band _band = Band.Mid;
        Quaternion _diveOrient;   // the layout we drive and HOLD (same reason KeeperController does)
        float _diveStart;         // Time.time at launch: a WALL-CLOCK cap that survives frames where
                                  // Tick is never called at all (Footballer.AiKeeperTick returns
                                  // early for the whole of a knockdown)
        float _spread;             // seconds left holding the planted Split block (X4)

        // Reaction clock (D5).
        float _reactTimer, _lastClosing;
        bool _wasIncoming;

        // Per-passage reset (X5).
        float _passageClear;
        bool _passageDone;

        // ------------------------------------------------------------------ tuning
        // These are consts here rather than in SimConfig because every one is meaningful only to
        // this brain, and SimConfig's Ai*Keeper block is already read by nothing else.
        //
        // GRAVITY. SimConfig.Gravity is -19.6, TWICE real g (measured Physics.gravity this session:
        // (0, -19.60, 0)). So apex = v^2 / 39.2, not v^2 / 19.62. The old single dive used
        // AiKeeperDiveUp 3.0 * 0.875 = 2.625 m/s -> a 17.6 cm apex. At real gravity that same number
        // gives 35 cm, which is what it was plainly sized for and it was never rescaled. The HUMAN
        // keeper's verticals WERE sized for 2x g (KeeperDiveUpBase 3.98 -> 40 cm apex, KeeperJumpVel
        // 6.5 -> 108 cm), so the AI was the only one left behind.
        //
        // RIG GEOMETRY these are solved against (BodyLayout.Biped, hScale 1, read this session):
        // pelvis 1.02, shoulder anchor 1.40, elbow 1.08, wrist ~0.78, glove sphere 0.346 m world
        // radius. Along the body's long axis, pelvis -> glove SURFACE is about 1.30 m. A standing
        // keeper with both arms overhead therefore reaches 2.32 m, twelve centimetres UNDER a 2.44 m
        // crossbar. That single number is why the high band needs real vertical and why a straight
        // jump has to be its own action.
        const float HighBandTop = 1.70f;  // predicted ball height at/above this = the high band
        // Vertical launch speed per band, at FULL ability; DiveAbil() scales it.
        const float MidDiveUp  = 3.80f;   // Normal 3.65 -> apex 0.34, pelvis 1.36
        const float HighDiveUp = 5.60f;   // Normal 5.38 -> apex 0.74, pelvis 1.76
        const float JumpUp     = 4.40f;   // Normal 4.22 -> apex 0.45, gloves overhead at 2.78
        const float HighDiveHorizMul = 0.85f;  // he cannot have full height AND full reach
        const float HighLayoutDeg = 55f;  // roll from upright: out = 1.30*sin, up = 1.30*cos
        // Reach moves only +-12% across the whole ladder. D5 allows difficulty to change reaction
        // and decision quality, NOT physical reach, so this stays deliberately flat and the reaction
        // delay below does the real work.
        const float DiveAbilWorst = 0.80f, DiveAbilBest = 1.12f;

        // Reaction delay: seconds from the strike to the earliest possible commit. Sqrt-shaped so
        // the middle of the picker is not bunched at the slow end. Per tier, from
        // PrematchUI.KeeperVals {0, 0.25, 0.5, 0.75, 1}:
        //   None 0.55 (academic - see X6) / Easy 0.30 / Normal 0.20 / Hard 0.12 / Insane 0.05.
        const float ReactWorst = 0.55f, ReactBest = 0.05f;
        const float StrikeJump = 4f;      // closing speed rising this much in a frame = a new strike

        // Distance-aware dive dead band (X3). Inside it he does NOT dive: he can sidestep there in
        // the flight time the ball has left, and a dive would only take him off his feet.
        // StepRealised is the fraction of the COMMANDED strafe speed he actually achieves - MoveInput
        // is a target velocity that the locomotion force only partly reaches. 0.55 is an ESTIMATE,
        // NOT a measurement: the editor was sitting in the menu scene with no built keeper to sample,
        // so this is the one number in this block that still wants a live check (log
        // _ragdoll.GroundSpeed against |MoveInput| through a shuffle and divide).
        const float StepRealised = 0.55f;
        const float StepSettle = 0.08f;   // he is not moving the instant he decides to
        const float BandMin = 0.65f;      // a body width: inside this he is already in the way
        const float BandMax = 3.20f;      // beyond this a dive is the only thing that gets there
        const float SpreadHold = 0.30f;   // how long the planted Split block is held

        // Per-passage reset thresholds (X5).
        const float PassageMargin = 3f;   // ball this far OUTSIDE his claim zone...
        const float PassageDwell = 0.5f;  // ...and not closing, for this long = the passage is over

        // Run gait state (used when he is covering ground, not shuffling on his line).
        float _gaitPhase, _gaitWeight;
        readonly Vector3[] _gaitScratch = new Vector3[(int)Bone.Count];

        // Where the keeper actually is (his pelvis), for the save-proximity check.
        public Vector3 PelvisPos => _ragdoll != null && _ragdoll.Pelvis != null
                                    ? _ragdoll.Pelvis.position : transform.position;

        // The keeper's body, for SaveWatch (which matches ball contacts against it).
        public ActiveRagdoll Body => _ragdoll;

        // True if he is diving now or dived very recently (for the EPIC SAVE callout).
        public bool WasDivingSave => _state == State.Diving || _diveCooldown > SimConfig.AiKeeperDiveCooldown - 0.6f;

        /// <summary>True while he has the ball in his gloves.</summary>
        public bool HasBall => _hands.Holding;

        /// <summary>Single-goal modes: the training goal at +Z, keeper facing back down the pitch.</summary>
        public void Init(ActiveRagdoll ragdoll, BallController ball)
            => Init(ragdoll, ball, SimConfig.GoalCenter, Mathf.Sign(SimConfig.KeeperFaceDir.z));

        /// <summary>
        /// Full init. `goalCenter` is the middle of the goal line he defends; `outSign` is the
        /// world-Z direction from it toward play (so he faces that way).
        /// </summary>
        public void Init(ActiveRagdoll ragdoll, BallController ball, Vector3 goalCenter, float outSign)
        {
            _ragdoll = ragdoll;
            _ball = ball;
            _goal = new Vector3(goalCenter.x, 0f, goalCenter.z);
            _out = outSign >= 0f ? 1f : -1f;
            _facing = Quaternion.LookRotation(new Vector3(0f, 0f, _out), Vector3.up);
            if (_ragdoll != null) _ragdoll.FacingRotation = _facing;
            _hands.Init(ball, ragdoll);
        }

        public void Tick()
        {
            if (_ragdoll == null || _ragdoll.Pelvis == null || _ball == null) return;
            _ragdoll.ClearPoseOverrides();
            float dt = Time.deltaTime;
            if (_diveCooldown > 0f) _diveCooldown -= dt;
            if (_spread > 0f) _spread -= dt;
            _hands.Tick(dt);

            float ability = Mathf.Clamp01(SimConfig.KeeperAbility);
            _ability = ability;

            // X6. Ability 0 is "None" on the picker and has to mean NO KEEPER. The BODY is built by
            // the mode - GameBootstrap.BuildAiKeeper already skips it at <= 0.001 for the drills,
            // GameBootstrap's match keepers are built unconditionally - so only the mode can
            // genuinely remove him (see the contract notes at the bottom of this file). What CAN be
            // done here is stop him being a free extra defender: no tracking, no commit, no claim.
            // A statue on the line is still wrong, which is exactly why the request exists.
            if (ability <= 0.001f)
            {
                _ragdoll.MoveInput = Vector3.zero;
                _ragdoll.FacingRotation = _facing;
                _ragdoll.SetPose(KeeperPose.Ready, 8f);
                return;
            }

            Vector3 bpos = _ball.transform.position;
            Vector3 me = _ragdoll.Pelvis.position;
            Vector3 bvel = _ball.Rb != null ? _ball.Rb.linearVelocity : Vector3.zero;

            // Toward-goal is the negation of the out direction, so the same test works at both ends.
            float dz = (bpos.z - me.z) * _out;          // + = ball still out in the pitch
            float closing = bvel.z * -_out;             // + = travelling toward this goal
            bool incoming = dz < SimConfig.AiKeeperReactZ && dz > -1.5f && closing > 1f;

            // Where will the ball cross his plane? Lead its x by its velocity so the dive commits
            // to the right side. A sharper keeper reads the shot earlier: the lead time it
            // extrapolates over scales with ability, so a top keeper locks onto the correct crossing
            // point while a weak one under-predicts and arrives late or short.
            float predictX = bpos.x, predictY = bpos.y;
            float leadCap = SimConfig.AiKeeperDiveLead * Mathf.Lerp(0.75f, 1.35f, ability);
            float tRem = closing > 0.5f ? Mathf.Max(0f, dz / closing) : 0f;   // remaining flight time
            if (closing > 0.5f)
            {
                // x uses the CAPPED lead (a weak keeper under-predicts), y uses the true remaining
                // flight time - the band choice must be made on where the ball really will be.
                float t = Mathf.Clamp(tRem, 0f, leadCap);
                predictX = bpos.x + bvel.x * t;
                predictY = bpos.y + bvel.y * tRem + 0.5f * Physics.gravity.y * tRem * tRem;
            }

            // ---- reaction clock (D5) ----
            // Difficulty makes him COMMIT LATER, never move slower. The clock restarts on a new
            // STRIKE - closing speed stepping up by StrikeJump - and not merely on `incoming`,
            // because `incoming` is already true for a ball trundling at 1.5 m/s from 14 m out, by
            // which point any delay has long since expired and the lever would do nothing. The
            // incoming EDGE restarts it too, for a shot that was already on its way when he read it.
            if (closing > _lastClosing + StrikeJump || (incoming && !_wasIncoming)) _reactTimer = 0f;
            else _reactTimer += dt;
            _lastClosing = closing;
            _wasIncoming = incoming;
            bool reacted = _reactTimer >= Mathf.Lerp(ReactWorst, ReactBest, Mathf.Sqrt(ability));

            TickPassage(dt, bpos, closing);                    // X5
            TrackShot(bpos, predictX, predictY, closing, dz);   // D7 dev counter

            // ---- holding: stand up with it, then play it out ----
            if (_state == State.Holding)
            {
                if (!_hands.Hold(dt)) { Recover(); return; }
                _ragdoll.MoveInput = Vector3.zero;
                _ragdoll.FacingRotation = _facing;
                _ragdoll.SetPose(KeeperPose.Hold, 10f);   // elbows bent around the ball, not stood to attention
                if (_hands.HeldFor >= SimConfig.KeeperHoldTime * Mathf.Lerp(1.4f, 0.8f, ability))
                {
                    _hands.Release(DistributeAim(), lofted: true, ability);
                    Recover();
                }
                return;
            }

            // ---- mid dive: hold the layout, but gather anything that ends up under him ----
            if (_state == State.Diving)
            {
                ManageDive();
                if (_state == State.Diving) TryClaim(ability);
                return;
            }

            if (TryClaim(ability)) return;

            float halfGoal = SimConfig.GoalWidth * 0.5f - 0.4f;
            // Dive distance is measured from where he ACTUALLY is, not from the middle of the goal.
            // Measuring from centre meant a keeper who had shifted across dived the wrong distance.
            float offset = predictX - me.x;
            float absOff = Mathf.Abs(offset);
            bool central = absOff < SimConfig.AiKeeperSplitWidth;

            // X2. The old gate was `ability > 0.25f` against picker steps {0, 0.25, 0.5, 0.75, 1}
            // (PrematchUI.KeeperVals). EASY IS EXACTLY 0.25, so Easy never dived in its life - and
            // Rushing's `<= 0.3f` plus TryClaim's KeeperClaimMinAbility 0.30 caught it as well, so
            // two of the five settings were statues that neither dived, rushed nor claimed. The only
            // thing gated now is "no keeper was built at all".
            bool canCommit = incoming && _diveCooldown <= 0f && ability > 0.001f
                             && Mathf.Abs(predictX - _goal.x) <= halfGoal + 1.2f;

            if (canCommit)
            {
                if (predictY < SimConfig.AiKeeperLowBallHeight && central)
                {
                    // X4. LOW and already in front of him: a PLANTED block, legs and arms spread.
                    // The old code called this a dive - it entered State.Diving, killed balance and
                    // locomotion and burned the whole AiKeeperDiveCooldown - while applying NO
                    // impulse at all. So a central shot put him in a dive state that moved him
                    // nowhere and locked him out of the rebound for 1.1 s. Fixed by NOT entering the
                    // dive state, rather than by inventing an impulse: he is already where the ball
                    // is going, there is nothing to launch him at, and the useful thing after a
                    // central block is to still be on your feet for the second ball.
                    // Deliberately not gated on the reaction clock either - putting your legs out
                    // when the ball is at you is a flinch, not a decision.
                    _spread = SpreadHold;
                }
                else if (predictY >= HighBandTop && central && reacted)
                {
                    // Over his head and central: JUMP. Never wired before, so a ball at 2.2 m down
                    // the middle went in past a keeper standing flat-footed underneath it.
                    LaunchJump(ability);
                    return;
                }
                else if (reacted && absOff > DeadBand(tRem, ability))
                {
                    float dir = offset >= 0f ? 1f : -1f;
                    if (predictY < SimConfig.AiKeeperLowBallHeight)
                    {
                        // Low and wide. Inside splay reach it is a lunge in place; further out (a
                        // bottom corner) a proper low dive down and across. Beyond that he steps
                        // toward it first and commits on a later frame.
                        float splayReach   = SimConfig.AiKeeperSplayReach   * Mathf.Lerp(0.85f, 1.2f, ability);
                        float lowDiveReach = SimConfig.AiKeeperLowDiveReach * Mathf.Lerp(0.85f, 1.2f, ability);
                        if (absOff <= splayReach) { LaunchSplay(dir, ability); return; }
                        if (absOff <= lowDiveReach) { LaunchBandDive(Band.Low, dir, ability); return; }
                    }
                    else
                    {
                        LaunchBandDive(predictY < HighBandTop ? Band.Mid : Band.High, dir, ability);
                        return;
                    }
                }
            }

            // ---- planted spread block (X4): held HERE, not in a dive state ----
            if (_spread > 0f)
            {
                _ragdoll.MoveInput = Vector3.zero;
                _ragdoll.FacingRotation = _facing;
                _ragdoll.SetPose(KeeperPose.Split, 14f);
                return;
            }

            // ---- move ----
            Vector3 meFlat = new Vector3(me.x, 0f, me.z);
            Vector3 bFlat = new Vector3(bpos.x, 0f, bpos.z);
            bool rush = Rushing(bFlat, ability);

            Vector3 spot = GuardSpot(bFlat, ability);
            Vector3 target = spot;
            if (rush) target = bFlat;
            else if (incoming) target = new Vector3(Mathf.Clamp(predictX, _goal.x - halfGoal, _goal.x + halfGoal),
                                                   0f, spot.z);

            Vector3 delta = target - meFlat;
            // Tighter proportional band at higher ability -> saturates to full speed sooner, so he
            // corrects errors more sharply instead of drifting over.
            float band = SimConfig.KeeperGuardBand * Mathf.Lerp(1.2f, 0.7f, ability);
            Vector3 drive = new Vector3(Mathf.Clamp(delta.x / band, -1f, 1f), 0f,
                                        Mathf.Clamp(delta.z / band, -1f, 1f));
            if (drive.sqrMagnitude > 1f) drive.Normalize();

            // Renamed off "react" now that there is a real reaction CLOCK above. This is only how
            // urgently he tracks, and it is deliberately NOT touched by the reaction delay: D5 says
            // easier settings commit later, not that they move slower.
            float urgency = rush ? SimConfig.KeeperRushSpeedMul
                          : incoming ? 1f
                          : Mathf.Lerp(0.30f, 0.70f, ability);
            float speed = SimConfig.KeeperStrafeSpeed * Mathf.Lerp(0.45f, 2.0f, ability) * urgency;
            _ragdoll.MoveInput = drive * speed;

            // Running out to a ball, he faces where he is going; on his line he faces the pitch.
            if (rush && delta.magnitude > 1.5f)
                _ragdoll.FacingRotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            else
                _ragdoll.FacingRotation = _facing;

            _ragdoll.SetPose(KeeperPose.Ready, 8f);
            // Sidestepping on the line shuffles; covering real ground runs.
            if (_ragdoll.GroundSpeed > SimConfig.KeeperRunGaitSpeed) RunGait();
            else { _gaitWeight = 0f; ShuffleGait(drive.magnitude); }
        }

        /// <summary>
        /// Stand on the ball-to-goal-centre line, off the line in proportion to how far out the ball
        /// is. This is the single most important thing a keeper does and the thing the old X-only
        /// keeper could not do at all.
        /// </summary>
        Vector3 GuardSpot(Vector3 ballFlat, float ability)
        {
            Vector3 to = ballFlat - _goal;
            float d = to.magnitude;
            Vector3 dir = d > 0.05f ? to / d : new Vector3(0f, 0f, _out);

            float cap = Sweeper ? SimConfig.KeeperMaxOffLine : SimConfig.KeeperDrillOffLine;
            float off = Mathf.Clamp(d * SimConfig.KeeperAngleFrac, SimConfig.KeeperLineOffset, cap);
            off *= Mathf.Lerp(0.55f, 1.15f, ability);          // a braver keeper comes further out
            off = Mathf.Min(off, Mathf.Max(SimConfig.KeeperLineOffset, d - 1.2f));   // never past the ball

            Vector3 spot = _goal + dir * off;
            float wide = SimConfig.GoalWidth * 0.5f - 0.4f + SimConfig.KeeperWideAllow;
            spot.x = Mathf.Clamp(spot.x, _goal.x - wide, _goal.x + wide);
            // Never drift back behind his own goal line.
            float minZ = _goal.z + _out * SimConfig.KeeperLineOffset * 0.5f;
            spot.z = _out > 0f ? Mathf.Max(spot.z, minZ) : Mathf.Min(spot.z, minZ);
            spot.y = 0f;
            return spot;
        }

        // Come and get it: a loose, slow ball near his goal is his. A ball in someone's feet is not
        // (that is a challenge, not a save), and neither is one flying at him (that is a dive).
        bool Rushing(Vector3 ballFlat, float ability)
        {
            if (!Sweeper) return false;      // drills: he holds his goal, he doesn't go fetch
            // X2: was `<= 0.3f`, which sits ABOVE Easy's 0.25 step, so Easy never swept a loose ball.
            if (ability <= 0.001f || _ball.DribbleHold) return false;
            if (_ball.Speed > SimConfig.KeeperRushMaxSpeed) return false;
            return Vector3.Distance(ballFlat, _goal) < SimConfig.KeeperRushZone;
        }

        bool TryClaim(float ability)
        {
            Vector3 b = _ball.transform.position;
            if (Vector3.Distance(new Vector3(b.x, 0f, b.z), _goal) > SimConfig.KeeperClaimZone) return false;
            // Rejected claim -> he beats it away. The min-ability gate blocks the CATCH only: a poor
            // keeper still deflects, he just never holds. Ordered after the zone test so he never
            // punches a ball that is nothing to do with him.
            //
            // X2: that gate was SimConfig.KeeperClaimMinAbility (0.30) against picker steps
            // {0, 0.25, 0.5, 0.75, 1}, so EASY (0.25) could never gather a ball in its life -
            // every single shot at an Easy keeper was a parry. Only "no keeper" is gated now.
            // KeeperClaimMinAbility has no caller left after this (KeeperController only names it
            // in a comment); it belongs on X8's dead-constant list.
            if (ability <= 0.001f || !_hands.CanClaim(ability))
            {
                bool parried = _hands.TryParry(ability);
                if (parried) NoteSave();
                return false;
            }

            _hands.Claim();
            RestorePhysics();          // in case he gathered it mid-dive
            _state = State.Holding;
            _ragdoll.SetPose(KeeperPose.Hold, 10f);
            return true;
        }

        // Where a gathered ball gets played. Modes with teammates supply a target; the rest get a
        // punt upfield, angled slightly off centre so it isn't fed straight back to the shooter.
        Vector3 DistributeAim()
        {
            if (DistributeTarget != null)
            {
                Vector3 t = DistributeTarget(PelvisPos);
                t.y = SimConfig.BallRadius;
                return t;
            }
            float side = Random.value < 0.5f ? -1f : 1f;
            return new Vector3(_goal.x + side * SimConfig.KeeperDistributeRange * 0.35f,
                               SimConfig.BallRadius,
                               _goal.z + _out * SimConfig.KeeperDistributeRange);
        }

        void ShuffleGait(float moveAmt)
        {
            if (moveAmt < 0.15f) { _shufflePhase = 0f; return; }
            // Legs step faster at higher ability so the gait keeps pace with the quicker glide.
            _shufflePhase += Time.deltaTime * SimConfig.KeeperShuffleRate * moveAmt
                             * Mathf.Lerp(0.85f, 1.5f, _ability);
            float s = Mathf.Sin(_shufflePhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-liftL * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.KeeperShuffleKnee, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-liftR * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.KeeperShuffleKnee, 0f, 0f));
        }

        // The shared outfield run gait, for when he is actually covering ground. A keeper sprinting
        // out to a through ball in a sideways shuffle looked ridiculous.
        void RunGait()
        {
            var p = Gait.For(_ragdoll.Plan);
            float speed = _ragdoll.GroundSpeed;
            _gaitWeight = Gait.Weight(_gaitWeight, speed, true, Time.deltaTime);

            float sprint01;
            _gaitPhase += Time.deltaTime * Gait.Cadence(speed, _ragdoll.HeightScale, p, out sprint01);
            if (_gaitPhase > Mathf.PI * 2f) _gaitPhase -= Mathf.PI * 2f;

            var over = _gaitScratch;
            for (int i = 0; i < over.Length; i++) over[i] = Vector3.zero;
            Gait.Pose(over, p, _gaitPhase, _gaitWeight, sprint01, 0f, 0f);
            for (int i = 0; i < over.Length; i++) _ragdoll.SetPoseOverride((Bone)i, over[i]);
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        // The THREE-WAY dive (X3). One entry point, so the band -> (pose, layout, velocity) mapping
        // lives in exactly one place and a change to one band cannot silently disagree with another.
        //
        // POSE CHOICE IS GEOMETRY, NOT TASTE. KeeperPose.Dive spreads the arms PERPENDICULAR to the
        // body's long axis, so laid out flat it becomes a tall vertical bar of arm: about +-0.92 m
        // of glove either side of the shoulder line, but only the body's own 0.89 m of lateral
        // reach. KeeperPose.DiveHigh punches both arms ALONG the long axis past the head, so all
        // 1.30 m of extension points wherever the layout angle points it. Hence:
        //   LOW  star, KeeperDiveLayoutLow  84 deg - one glove sweeps the turf, one is at hip height
        //   MID  star, KeeperDiveLayoutHigh 90 deg - the tall arm bar, because the height PREDICTION
        //        is least reliable in the middle band (a spinning or deflected ball defeats the
        //        ballistic extrapolation), so the pose that hedges over the most height wins
        //   HIGH DiveHigh at 55 deg - 0.75 m of that extension up, 1.07 m of it out
        //
        // Reach at Normal ability, BALLISTIC from the pelvis at launch. The ragdoll is a sprung
        // assembly, not a point mass, and AddVelocityToAll ADDS to whatever he was already doing, so
        // treat these as ceilings and as the numbers the tuning was solved against:
        //   LOW  up 1.54 m/s, apex 0.06 m, horiz 7.18 m/s -> about 2.4 m across, gloves on the deck
        //   MID  up 3.65 m/s, apex 0.34 m, horiz 6.24 m/s -> about 2.05 m across, arm bar 0.44..2.28
        //   HIGH up 5.38 m/s, apex 0.74 m, horiz 5.30 m/s -> about 2.5 m across, glove at 2.50 m
        // Against the OLD single dive: up 2.63 m/s, apex 0.18 m, 1.65 m across, arm ceiling 2.12 m,
        // and nothing at all above 2.12 m.
        void LaunchBandDive(Band band, float dir, float ability)
        {
            BeginDive(band, dir, ability);

            // dir is the WORLD-X direction of the shot. Lunge in world X directly. The body roll is
            // about the facing's forward axis, so the roll sign follows which way this keeper's
            // LOCAL right points in world X - he must lie flat ON the side he is diving toward.
            Vector3 fwd = _facing * Vector3.forward;
            float rollDir = dir * RightSign();
            float abil = DiveAbil(ability);

            float horizMul = band == Band.Low ? 1.15f : band == Band.High ? HighDiveHorizMul : 1f;
            float up       = band == Band.Low ? SimConfig.AiKeeperLowDiveUp
                           : band == Band.Mid ? MidDiveUp : HighDiveUp;
            float layoutDeg = band == Band.Low ? SimConfig.KeeperDiveLayoutLow
                            : band == Band.Mid ? SimConfig.KeeperDiveLayoutHigh : HighLayoutDeg;

            float horiz = SimConfig.AiKeeperDiveHoriz * horizMul * abil
                          * (SimConfig.KeeperStrafeSpeed / 5.5f);
            // The pre-match jump slider scales every vertical, including the low band's - it used to
            // scale the full dive and the jump but not the low dive, which made the slider lie.
            up *= abil * (SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase);
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, up, 0f));

            _diveOrient = Quaternion.AngleAxis(-rollDir * layoutDeg, fwd) * _facing;
            _ragdoll.BodyOrientTarget = _diveOrient;
            _ragdoll.AddTorqueToPelvis(fwd * (-rollDir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(band == Band.High ? KeeperPose.DiveHigh : KeeperPose.Dive, 16f);
        }

        // Low ball WIDE of him but inside splay reach: a lunge that stays on the ground -
        // SaveLeft / SaveRight, small hop, no layout roll. He is not going airborne for a ball he
        // can get a leg and an arm to from where he already stands. The central half of the old
        // LaunchLowSave has moved out of here entirely and is no longer a dive at all (X4).
        //
        // This is the AI's version of the human's LMB/RMB reflex save (KeeperController.BeginSave)
        // - same move, same person doing it - so it now launches off that move's own constant
        // (KeeperSaveLunge) instead of a separate, larger AI-only number, with AiKeeperLowSaveUp
        // at 0 keeping it exactly as grounded as the human's version already is.
        void LaunchSplay(float dir, float ability)
        {
            BeginDive(Band.Low, dir, ability);
            // dir is WORLD X, but the splay poses are BODY-LOCAL shapes, so it has to be turned
            // into his own left/right first - the same RightSign() conversion the dives do for
            // their roll. Without it a keeper facing +Z (one end of a match) splayed away from
            // the ball while the lunge carried him toward it.
            float side = dir * RightSign();      // +1 = crossing on his own right
            _lowPose = side < 0f ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
            float horiz = SimConfig.KeeperSaveLunge * DiveAbil(ability)
                          * (SimConfig.KeeperStrafeSpeed / 5.5f);
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, SimConfig.AiKeeperLowSaveUp, 0f));
            _diveOrient = _facing;               // keep low, no full layout roll
            _ragdoll.BodyOrientTarget = _diveOrient;
            _ragdoll.SetPose(_lowPose, 16f);
        }

        // Straight up, both gloves overhead: KeeperPose.Jump, which the AI has never once used.
        // A standing keeper's gloves reach 2.32 m against a 2.44 m crossbar, so a ball over his head
        // that is not jumped at is a goal by twelve centimetres. JumpUp 4.40 * 0.96 at Normal =
        // 4.22 m/s, apex 0.45 m under 2x gravity, gloves at 2.78 m - a hand clearly over the bar.
        // Recipe lifted from KeeperController.Jump, which already works: the orient target is HELD
        // at _facing through the whole flight so the free pelvis cannot topple him forward, and he
        // comes down on his feet. LaunchVerticalAll (not AddVelocityToAll) because a jump must not
        // carry his sideways shuffle with it.
        void LaunchJump(float ability)
        {
            BeginDive(Band.Jump, 0f, ability);
            _diveOrient = _facing;
            _ragdoll.BodyOrientTarget = _diveOrient;
            _ragdoll.LaunchVerticalAll(JumpUp * DiveAbil(ability)
                                       * (SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase));
            _ragdoll.SetPose(KeeperPose.Jump, 16f);
        }

        void BeginDive(Band band, float dir, float ability)
        {
            _hands.Drop();
            _state = State.Diving;
            _band = band;
            _diveDir = dir;
            _lowPose = null;
            _spread = 0f;
            _diveAir = 0f; _diveGround = 0f;
            _diveStart = Time.time;
            // A jump is cheap - he lands on his feet and is ready again - so it must not lock him
            // out the way a full layout dive does. Measured intent: 0.6 s at Normal against 1.0 s.
            float cost = band == Band.Jump ? 0.55f : 1f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown * cost * Mathf.Lerp(1.25f, 0.7f, ability);
            _gaitWeight = 0f;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
        }

        // Reach multiplier across the difficulty ladder: 0.88 Easy / 0.96 Normal / 1.04 Hard /
        // 1.12 Insane. Deliberately a 24% total spread and no more. D5 forbids difficulty buying an
        // AI extra reach, and the ladder's real lever is the reaction delay, not the dive.
        static float DiveAbil(float ability) => Mathf.Lerp(DiveAbilWorst, DiveAbilBest, ability);

        // Which world-X direction this keeper's LOCAL right points. Facing -Z it is -X, facing +Z
        // it is +X, and the layout roll sign depends on it.
        float RightSign() => (_facing * Vector3.right).x >= 0f ? 1f : -1f;

        Vector3[] _lowPose;   // held pose during a low save (SaveLeft / SaveRight)

        void ManageDive()
        {
            // SOMETHING ELSE RE-STOOD HIM MID-DIVE. Knockdown clears these three flags on entry and
            // Knockdown.Recover sets all three back true; ActiveRagdoll.ResetTo does the same. Tick
            // is not called at all while he is down (Footballer.AiKeeperTick returns early on
            // IsDown), so _diveAir does not advance, and the old code came back out of a knockdown
            // still "diving" - holding a star pose on an upright, balanced body until 2.5 s of
            // TICKED time had drained. Any of the three being true means the dive is over.
            if (_ragdoll.BalanceEnabled || _ragdoll.LocomotionEnabled || _ragdoll.UprightLock)
            { Recover(); return; }

            // Hold the splay pose if we're in one, else the band's own pose. Held for the whole
            // flight, and so is the layout: a one-shot roll impulse cannot guarantee he is actually
            // laid out by the apex (the same reason KeeperController.DoDive drives it every frame).
            _ragdoll.SetPose(_lowPose ?? (_band == Band.High ? KeeperPose.DiveHigh
                                        : _band == Band.Jump ? KeeperPose.Jump
                                        : KeeperPose.Dive), 16f);
            _ragdoll.BodyOrientTarget = _diveOrient;
            if (_band != Band.Jump && _lowPose == null) HoldDiveLegs();

            _diveAir += Time.deltaTime;
            bool landed = _diveAir > SimConfig.KeeperDiveMinAir && _ragdoll.IsGrounded;
            if (landed) _diveGround += Time.deltaTime; else _diveGround = 0f;
            // THREE independent ways out, and the third is the one that matters: _diveAir is TICKED
            // time and can stall (knockdown, a mode that stops ticking him), Time.time cannot. With
            // it there is no path that leaves him prone indefinitely.
            if (_diveGround >= SimConfig.KeeperDiveSettle
                || _diveAir > SimConfig.KeeperDiveMaxTime
                || Time.time - _diveStart > SimConfig.KeeperDiveMaxTime + 1f)
                Recover();
        }

        // Knee folds through a lateral dive, same wiring as KeeperController.ManageDive: he lands on
        // the dive side, so the TOP leg (opposite the dive) is the one that folds up hard. Purely
        // cosmetic - the AI dive used to land with both legs straight, which read as a falling plank.
        // Re-applied every frame because Tick calls ClearPoseOverrides first.
        void HoldDiveLegs()
        {
            Bone leadThigh = _diveDir < 0f ? Bone.ThighR : Bone.ThighL;
            Bone leadCalf  = _diveDir < 0f ? Bone.CalfR  : Bone.CalfL;
            Bone backThigh = _diveDir < 0f ? Bone.ThighL : Bone.ThighR;
            Bone backCalf  = _diveDir < 0f ? Bone.CalfL  : Bone.CalfR;
            _ragdoll.SetPoseOverride(leadThigh, new Vector3(-SimConfig.KeeperDiveLeadKnee * 0.5f, 0f, 0f));
            _ragdoll.SetPoseOverride(leadCalf,  new Vector3(SimConfig.KeeperDiveLeadKnee, 0f, 0f));
            _ragdoll.SetPoseOverride(backThigh, new Vector3(-SimConfig.KeeperDiveBackKnee * 0.5f, 0f, 0f));
            _ragdoll.SetPoseOverride(backCalf,  new Vector3(SimConfig.KeeperDiveBackKnee, 0f, 0f));
        }

        void RestorePhysics()
        {
            _lowPose = null;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.SnapFacing(_facing);
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;
        }

        void Recover()
        {
            _state = State.Guard;
            _facing = Quaternion.LookRotation(new Vector3(0f, 0f, _out), Vector3.up);
            RestorePhysics();
            _ragdoll.SetPose(KeeperPose.Ready, 12f);
        }

        /// <summary>Drop the ball if he has it (a mode reset, a knockdown, a whistle).</summary>
        public void DropBall() => _hands.Drop();

        public void ResetTo(Vector3 basePos)
        {
            _hands.Drop();
            _state = State.Guard;
            _diveCooldown = 0f;
            _gaitPhase = 0f; _gaitWeight = 0f; _shufflePhase = 0f;
            _facing = Quaternion.LookRotation(new Vector3(0f, 0f, _out), Vector3.up);
            _lowPose = null;
            _spread = 0f;
            _reactTimer = 0f; _lastClosing = 0f; _wasIncoming = false;
            _passageClear = 0f; _passageDone = false;
            _armed = false;
            _ragdoll.ResetTo(basePos, _facing);
        }

        /// <summary>
        /// Clear him out of whatever he was doing and stand him up WITHOUT moving him - a whistle, a
        /// goal, a restart where ResetTo's teleport would be wrong. Nothing calls it yet; it exists
        /// so a mode does not have to fake it by resetting his position.
        /// </summary>
        public void ForceRecover()
        {
            _hands.Drop();
            _spread = 0f;
            _diveCooldown = 0f;
            _armed = false;
            Recover();
        }

        // ------------------------------------------------------------- per-passage reset (X5)
        /// <summary>
        /// A reset per PASSAGE OF PLAY, not per round. The drill modes get this free from ResetTo
        /// between attempts; a match calls ResetTo only at a kickoff, so a keeper who dived in
        /// the 8th minute was still inside AiKeeperDiveCooldown when play came back at him, and a
        /// keeper left in a dive state while the ball was at the other end stayed in it.
        ///
        /// Fires ONCE per change of ends: the ball is well outside his claim zone AND no longer
        /// closing on his goal, held for PassageDwell so a scramble that briefly squirts the ball
        /// clear does not count as possession flipping.
        /// </summary>
        void TickPassage(float dt, Vector3 bpos, float closing)
        {
            float fromGoal = Vector3.Distance(new Vector3(bpos.x, 0f, bpos.z), _goal);
            bool gone = fromGoal > SimConfig.KeeperClaimZone + PassageMargin && closing <= 0f;
            if (!gone) { _passageClear = 0f; _passageDone = false; return; }
            if (_passageDone) return;
            _passageClear += dt;
            if (_passageClear < PassageDwell) return;

            _passageDone = true;
            _diveCooldown = 0f;
            _reactTimer = 0f;
            _spread = 0f;
            _armed = false;
            if (_state == State.Diving) Recover();
            // NOT cleared here, because it cannot be from outside: KeeperHands._cooldown - the
            // 2.2 s KeeperClaimCooldown after a drop or a release, and the 0.35 s
            // KeeperParryCooldown after a parry - is private with no reset. See the contract note
            // at the bottom of this file. In practice the parry half expires on its own long before
            // play returns; the 2.2 s claim half is the one that can still bite on a quick break.
        }

        // ------------------------------------------------------------- dive dead band (X3)
        /// <summary>
        /// How far he can SIDESTEP in the ball's remaining flight time. Inside that he does not dive
        /// at all: he steps, and stays on his feet for the rebound.
        ///
        /// This replaces a flat SimConfig.AiKeeperDiveThresh of 1.6 m, which was wrong at both ends
        /// at once. Too WIDE close in: at 6 m a 25 m/s shot leaves him 0.18 s, in which 1.6 m needs
        /// nearly 9 m/s of realised strafe, so he stood and watched a ball he should have thrown
        /// himself at. Too NARROW far out: at 20 m he had 0.74 s, easily enough to walk 1.7 m and
        /// catch it, and instead he dived and put himself on the floor.
        ///
        /// Resulting band at Normal ability, against a 25 m/s shot with the keeper 1.5 m off his line
        /// (so dz = range - 1.5, step speed 5.5 * 1.225 * 0.55 = 3.71 m/s):
        ///   6 m range  -> 0.65 m  (the BandMin floor; the raw figure is 0.37 m)
        ///   12 m range -> 1.26 m
        ///   20 m range -> 2.45 m
        /// AiKeeperDiveThresh has no caller after this and belongs on X8's dead-constant list.
        /// </summary>
        float DeadBand(float tRem, float ability)
        {
            float step = SimConfig.KeeperStrafeSpeed * Mathf.Lerp(0.45f, 2.0f, ability) * StepRealised;
            return Mathf.Clamp(step * (tRem - StepSettle), BandMin, BandMax);
        }

        // ------------------------------------------------------------- dev save-rate counter (D7)
        // D7 asks for 60-70% of ON-TARGET undeflected shots saved at Normal. That target is only
        // enforceable if it can be read, so it is counted here - the one place that already knows
        // which goal this keeper defends and where the ball is heading.
        //
        // STATIC, shared by every keeper in the scene, because the number wanted is the match-wide
        // save rate. Read them straight out of the editor with no HUD work:
        //     Trickshot.Goalkeeper.DbgSaved / DbgConceded / DbgFaced
        //     Trickshot.Goalkeeper.DbgReset()      // zero them before a measurement run
        // The rate is DbgSaved / (DbgSaved + DbgConceded). DbgFaced is larger than that sum: a shot
        // can arm and then hit a post or be cleared by a defender, which resolves as neither.
        //
        // ARMED when a struck ball is closing on this goal and its predicted crossing point is
        // inside the frame. RESOLVED as a SAVE the moment the ball touches any part of this keeper's
        // body (so a leg block, a fingertip parry and a clean catch all count, which is what a save
        // is), or as CONCEDED when it crosses his goal line inside the posts and under the bar. The
        // in-goal test is derived from _goal and _out, so it works at BOTH ends - unlike the
        // hardcoded +Z single-goal assist in MatchGame (X7).
        //
        // LIMITATION, stated because it changes how the number reads: it cannot tell a DEFLECTED
        // shot from a clean one. A shot armed on target and then bent off a defender still counts,
        // and a shot deflected ONTO target after arming is timed from the wrong moment. Read the
        // figure as "shots this keeper faced on frame", a slightly larger set than D7's wording.
        public static int DbgFaced, DbgSaved, DbgConceded;
        public static void DbgReset() { DbgFaced = DbgSaved = DbgConceded = 0; }

        bool _armed;
        float _armedAt;
        const float ShotArmSpeed = 8f;   // slower than this is a pass or a trickle, not a shot

        void NoteSave()
        {
            if (!_armed) return;
            _armed = false;
            DbgSaved++;
        }

        void TrackShot(Vector3 bpos, float predictX, float predictY, float closing, float dz)
        {
            float halfMouth = SimConfig.GoalWidth * 0.5f;
            if (_armed)
            {
                if (_ball.BodyTouchedSince(_ragdoll, _armedAt, out _, out _)) { NoteSave(); return; }
                if ((bpos.z - _goal.z) * _out < 0f)      // past his goal line
                {
                    if (Mathf.Abs(bpos.x - _goal.x) <= halfMouth && bpos.y <= SimConfig.GoalHeight)
                        DbgConceded++;
                    _armed = false;
                }
                else if (closing < 0.5f) _armed = false; // cleared, blocked upfield, or dead
                return;
            }
            if (closing < ShotArmSpeed || dz <= 0f || dz > SimConfig.AiKeeperReactZ) return;
            if (Mathf.Abs(predictX - _goal.x) > halfMouth) return;
            if (predictY < 0f || predictY > SimConfig.GoalHeight) return;
            _armed = true;
            _armedAt = Time.time;
            DbgFaced++;
        }

        // ------------------------------------------------------------- what this file needs from
        // ------------------------------------------------------------- files it does not own
        // 1. GameBootstrap: the MATCH keepers (BuildFootballer(keeper: true), both teams) are
        //    built unconditionally, so "None" on the keeper picker leaves a keeper in the goal. The
        //    drill path already guards it - BuildAiKeeper returns null at KeeperAbility <= 0.001 -
        //    and MatchGame already tolerates null keepers (Configure null-checks both before
        //    adding them to _all). Same guard, same place. Until then, ability 0 gets the inert
        //    branch at the top of Tick, which is closer to the intent but is still a statue.
        // 2. KeeperHands: no way to clear its private _cooldown, so the per-passage reset above
        //    cannot clear the claim/parry cooldown. A one-line `public void ClearCooldown() =>
        //    _cooldown = 0f;` closes it.
        // 3. SimConfig: KeeperClaimMinAbility and AiKeeperDiveThresh both lose their last caller
        //    here. They belong with X8's other dead constants.
    }
}
