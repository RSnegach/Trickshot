using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The AI goalkeeper. An active-ragdoll keeper that actually keeps goal, used by every mode
    /// with a keeper in it (striker practice, free kicks, accuracy, time trial, and both ends of a
    /// scrimmage via Footballer).
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
    ///   - DIVES, low or full layout, at the predicted crossing point.
    ///
    /// Everything scales with SimConfig.KeeperAbility: tracking sharpness, how far he dares come
    /// off his line, dive reach, and how cleanly he gathers.
    ///
    /// Driven by Tick() from the owning mode loop (not FixedUpdate), so it stays in lockstep with
    /// the rest of that mode.
    ///
    /// GEOMETRY. The keeper is told which goal he defends and which way the pitch is (`outSign`,
    /// the world-Z direction from his goal toward play), so the same brain works at the +Z goal in
    /// striker mode and at BOTH ends of a scrimmage. The old class hard-coded SimConfig.KeeperFaceDir.
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
            _hands.Tick(dt);

            float ability = Mathf.Clamp01(SimConfig.KeeperAbility);
            _ability = ability;

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
            if (closing > 0.5f)
            {
                float t = Mathf.Clamp(dz / closing, 0f, leadCap);
                predictX = bpos.x + bvel.x * t;
                float tf = Mathf.Max(0f, dz / closing);
                predictY = bpos.y + bvel.y * tf + 0.5f * Physics.gravity.y * tf * tf;
            }

            float halfGoal = SimConfig.GoalWidth * 0.5f - 0.4f;
            // Dive distance is measured from where he ACTUALLY is, not from the middle of the goal.
            // Measuring from centre meant a keeper who had shifted across dived the wrong distance.
            float offset = predictX - me.x;
            float absOff = Mathf.Abs(offset);
            bool lowBall = predictY < SimConfig.AiKeeperLowBallHeight;

            // Reach windows widen with ability: a stronger keeper commits to balls a weak one can't
            // get to. Kept in step with the dive velocity, which also scales with ability.
            float splayReach   = SimConfig.AiKeeperSplayReach   * Mathf.Lerp(0.85f, 1.2f, ability);
            float lowDiveReach = SimConfig.AiKeeperLowDiveReach * Mathf.Lerp(0.85f, 1.2f, ability);
            bool canCommit = incoming && _diveCooldown <= 0f && ability > 0.25f
                             && Mathf.Abs(predictX - _goal.x) <= halfGoal + 1.2f;

            if (canCommit)
            {
                if (lowBall)
                {
                    // LOW ball: within splay reach -> Split (central) / SaveLeft-Right splay in
                    // place; further out (toward a bottom corner) -> a LOW DIVE, down and across.
                    // Beyond that he steps toward it first and commits on a later frame.
                    if (absOff <= splayReach) { LaunchLowSave(offset, ability); return; }
                    if (absOff <= lowDiveReach) { LaunchLowDive(Mathf.Sign(offset), ability); return; }
                }
                else if (absOff > SimConfig.AiKeeperDiveThresh)
                {
                    LaunchDive(Mathf.Sign(offset), ability);
                    return;
                }
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

            float react = rush ? SimConfig.KeeperRushSpeedMul
                        : incoming ? 1f
                        : Mathf.Lerp(0.30f, 0.70f, ability);
            float speed = SimConfig.KeeperStrafeSpeed * Mathf.Lerp(0.45f, 2.0f, ability) * react;
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
            if (ability <= 0.3f || _ball.DribbleHold) return false;
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
            if (ability < SimConfig.KeeperClaimMinAbility || !_hands.CanClaim(ability))
            {
                _hands.TryParry(ability);
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

        void LaunchDive(float dir, float ability)
        {
            BeginDive(dir, ability);

            // dir is the WORLD-X direction of the shot. Lunge in world X directly. The body roll is
            // about the facing's forward axis, so the roll sign follows which way this keeper's
            // LOCAL right points in world X - he must lie flat ON the side he is diving toward.
            Vector3 fwd = _facing * Vector3.forward;
            float rollDir = dir * RightSign();
            float abilMul = Mathf.Lerp(0.6f, 1.15f, ability);
            float horiz = SimConfig.AiKeeperDiveHoriz * abilMul * (SimConfig.KeeperStrafeSpeed / 5.5f);
            float up = SimConfig.AiKeeperDiveUp * abilMul
                       * (SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase);
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, up, 0f));

            Quaternion layout = Quaternion.AngleAxis(-rollDir * SimConfig.KeeperDiveLayoutHigh, fwd) * _facing;
            _ragdoll.BodyOrientTarget = layout;
            _ragdoll.AddTorqueToPelvis(fwd * (-rollDir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        // Low / grounded shot: the keeper spreads to block down low rather than launching into a
        // full airborne dive. Central -> Split (both legs out); to a side -> SaveLeft/SaveRight
        // splay lunge, staying low with only a small hop.
        // offset is the SIGNED world-X gap from the keeper to the predicted crossing point - the
        // same number the commit was made on. It used to take only the sign and then re-derive
        // central/wide from the ball's CURRENT x against the goal centre, which is a different
        // measurement: a roller already covered (offset near 0) but still wide of centre got a full
        // sideways splay away from it, and Mathf.Sign(0f) is +1 so that lunge always went +X.
        void LaunchLowSave(float offset, float ability)
        {
            float dir = offset >= 0f ? 1f : -1f;      // world-X side he has to cover
            BeginDive(dir, ability);
            bool central = Mathf.Abs(offset) < SimConfig.AiKeeperSplitWidth;   // already in front of him

            if (central)
            {
                _lowPose = KeeperPose.Split;         // stay planted, spread wide + low
                _ragdoll.BodyOrientTarget = _facing;
            }
            else
            {
                // dir is WORLD X, but the splay poses are BODY-LOCAL shapes, so it has to be
                // turned into his own left/right first - the same RightSign() conversion the two
                // airborne dives do for their roll. Without it a keeper facing +Z (one end of a
                // scrimmage) splayed away from the ball while the lunge carried him toward it.
                float side = dir * RightSign();      // +1 = crossing on his own right
                _lowPose = side < 0f ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                float horiz = SimConfig.AiKeeperDiveHoriz * 0.75f * Mathf.Lerp(0.6f, 1.15f, ability)
                              * (SimConfig.KeeperStrafeSpeed / 5.5f);
                _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, SimConfig.AiKeeperLowSaveUp, 0f));
                _ragdoll.BodyOrientTarget = _facing;   // keep low, no full layout roll
            }
            _ragdoll.SetPose(_lowPose, 16f);
        }

        // Low ball toward a bottom corner: a full lunging dive kept LOW (strong horizontal, small
        // lift) with the layout roll, so he goes down and across rather than up over a rolling ball.
        void LaunchLowDive(float dir, float ability)
        {
            BeginDive(dir, ability);

            Vector3 fwd = _facing * Vector3.forward;
            float rollDir = dir * RightSign();
            float abilMul = Mathf.Lerp(0.6f, 1.15f, ability);
            float horiz = SimConfig.AiKeeperDiveHoriz * 1.15f * abilMul * (SimConfig.KeeperStrafeSpeed / 5.5f);
            float up = SimConfig.AiKeeperLowDiveUp * abilMul;
            _ragdoll.AddVelocityToAll(new Vector3(dir * horiz, up, 0f));

            Quaternion layout = Quaternion.AngleAxis(-rollDir * SimConfig.KeeperDiveLayoutLow, fwd) * _facing;
            _ragdoll.BodyOrientTarget = layout;
            _ragdoll.AddTorqueToPelvis(fwd * (-rollDir * SimConfig.KeeperDiveRoll));
            _ragdoll.SetPose(KeeperPose.Dive, 16f);
        }

        void BeginDive(float dir, float ability)
        {
            _hands.Drop();
            _state = State.Diving;
            _diveDir = dir;
            _lowPose = null;
            _diveAir = 0f; _diveGround = 0f;
            _diveCooldown = SimConfig.AiKeeperDiveCooldown * Mathf.Lerp(1.25f, 0.7f, ability);
            _gaitWeight = 0f;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
        }

        // Which world-X direction this keeper's LOCAL right points. Facing -Z it is -X, facing +Z
        // it is +X, and the layout roll sign depends on it.
        float RightSign() => (_facing * Vector3.right).x >= 0f ? 1f : -1f;

        Vector3[] _lowPose;   // held pose during a low save (Split / SaveLeft / SaveRight)

        void ManageDive()
        {
            // Hold the low-save splay pose if we're in one, else the airborne dive layout.
            _ragdoll.SetPose(_lowPose ?? KeeperPose.Dive, 16f);
            _diveAir += Time.deltaTime;
            bool landed = _diveAir > SimConfig.KeeperDiveMinAir && _ragdoll.IsGrounded;
            if (landed) _diveGround += Time.deltaTime; else _diveGround = 0f;
            if (_diveGround >= SimConfig.KeeperDiveSettle || _diveAir > SimConfig.KeeperDiveMaxTime)
                Recover();
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
            _ragdoll.ResetTo(basePos, _facing);
        }
    }
}
