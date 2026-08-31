using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Player-controlled goalkeeper (an active ragdoll with arms). Faces out toward the
    /// pitch and stays on the line.
    ///
    /// Controls:
    ///  - A / D .................. strafe sideways (and W/S in/out) for positioning.
    ///  - A/D + Space ............ upward dive; reach/height scale with prior speed.
    ///  - double-tap A / D ....... explosive low sideways dive just off the ground.
    ///  - Space (no direction) ... straight jump, arms up.
    ///  - LMB / RMB .............. one-time reflex lunge save, arm+leg out; auto-recovers
    ///                             even if held. Both = splayed split.
    ///  - E / Q .................. roll out / punt a ball he has gathered.
    /// All dives lay the body out horizontal and only get back up after landing, then run a short
    /// stumble as he pushes back to his feet.
    ///
    /// He GATHERS a loose ball by himself whenever one is slow enough and inside his reach - on his
    /// line, or in the middle of a dive - and clamps it to his chest until he plays it out. That is
    /// the same KeeperHands primitive the AI keeper uses, so a human and an AI handle the ball
    /// identically; only the ability number feeding it differs (his Control stat, not a difficulty
    /// slider). A hard shot is never gathered, only parried, so shot-stopping is unchanged.
    /// </summary>
    public class KeeperController : MonoBehaviour, IPlayerController
    {
        enum State { Ready, Saving, Diving, Holding, Stumble }

        IStrikerInput _input;
        ActiveRagdoll _ragdoll;
        BallController _ball;
        readonly KeeperHands _hands = new KeeperHands();   // gather / hold / distribute
        Quaternion _facing;
        // Out toward the pitch. Resolved at Init from where the mode BUILT him rather than taken
        // from SimConfig.KeeperFaceDir, because that constant only describes the +Z goal that the
        // practice and set-piece modes use. Match defends the -Z goal, where it is exactly
        // backwards, and the keeper stood looking into his own net.
        Vector3 _faceDir = SimConfig.KeeperFaceDir;
        // Sign of "upfield" for him, latched at Init the same way _faceDir is. Must NOT be re-read
        // from the live pelvis: nothing leashes his z, so walking past halfway would flip it and
        // point his distribution at his own net. Default matches the default _faceDir (+Z goal).
        float _outZ = -1f;
        // Half-extents of the area he distributes into. Defaults to the single-goal training arena;
        // match is up to 68 x 104, so those modes overwrite it or every punt clamps short.
        public Vector2 AimBounds = new Vector2(SimConfig.FieldWidth * 0.45f, SimConfig.FieldLength * 0.45f);
        System.Func<float> _lookYaw;   // camera cone yaw (deg); the body turns to match
        float _shufflePhase;           // procedural shuffle-step cadence while moving

        State _state = State.Ready;
        Vector3[] _airPose;   // pose held while in the air (Dive or Jump)

        // True while diving or lunging (for the EPIC SAVE callout - a save made while
        // fully committed rather than a stationary block).
        // The keeper's body, for SaveWatch (which matches ball contacts against it).
        public ActiveRagdoll Body => _ragdoll;

        public bool IsCommitting => _state == State.Diving || _state == State.Saving;

        // True while airborne in a HIGH (full lay-out) dive specifically, not a low dash dive.
        // One of the two EPIC SAVE criteria (the other is ball speed at contact).
        public bool IsHighDive => _state == State.Diving && _diveIsHigh;

        // Dive lifecycle: landing detection.
        float _diveDir;       // -1 left / +1 right (for the leading-leg bend)
        Quaternion _diveOrient;  // held horizontal lay-out target for the current dive
        float _diveAir;       // time since dive launched
        float _diveGround;    // time spent settled on the ground after landing
        bool _diveIsJump;     // straight jump (stays upright) vs. lay-out dive
        bool _diveIsHigh;     // high (full lay-out) dive vs. low dash dive
        float _saveReleaseTimer;
        float _saveSettle;    // seconds GroundSpeed has stayed below KeeperSaveSettleSpeed, continuously

        // Getting up off the turf (see ManageStumble).
        float _stumbleTimer;
        float _stumbleTotal;

        // Double-tap detection for A/D dash dives.
        float _lastTapTime = -10f;
        float _lastTapDir;
        bool _dirWasDown;     // A or D held last frame (to detect fresh taps)

        /// <summary>
        /// `ball` is optional so a mode that has no interest in handling still compiles and simply
        /// gets a keeper who cannot catch, rather than a build error. Every real call site passes it.
        /// </summary>
        public void Init(IStrikerInput input, ActiveRagdoll ragdoll, BallController ball = null)
        {
            _input = input;
            _ragdoll = ragdoll;
            _ball = ball;
            if (ball != null) _hands.Init(ball, ragdoll);
            // Which way is out? Away from the goal he is standing in. Works for every mode without
            // any of them having to tell us: the practice/set-piece keeper is built at +Z and looks
            // down -Z, the match keeper is built at -Z and looks up +Z.
            if (_ragdoll.Pelvis != null && _ragdoll.Pelvis.position.z < 0f)
            {
                _faceDir = -SimConfig.KeeperFaceDir;
                _outZ = 1f;
            }
            // Keeper faces out toward the pitch; the body turns within a cone to match
            // the camera look (SetLookYawSource).
            _facing = Quaternion.LookRotation(_faceDir, Vector3.up);
            _ragdoll.FacingRotation = _facing;
        }

        /// <summary>True while he has the ball in his gloves.</summary>
        public bool HasBall => _hands.Holding;

        /// <summary>Source of the camera's cone yaw (deg); the keeper turns his body to
        /// it so facing and view stay locked together.</summary>
        public void SetLookYawSource(System.Func<float> lookYaw) => _lookYaw = lookYaw;

        public void Tick()
        {
            if (_ragdoll.Pelvis == null) return;
            _ragdoll.ClearPoseOverrides();
            _hands.Tick(Time.deltaTime);
            bool grounded = _ragdoll.IsGrounded;

            if (_state == State.Holding) { ManageHold(); return; }
            if (_state == State.Diving) { ManageDive(grounded); return; }
            if (_state == State.Saving) { ManageSave(); return; }
            // ManageStumble returns FALSE on the frame it ends or is cancelled and then deliberately
            // falls through to the ready path below, so the press that cancelled it is acted on in
            // that same frame instead of being eaten.
            if (_state == State.Stumble && ManageStumble()) return;

            // Ready: the body faces the camera's cone look. Single source of truth (the
            // camera owns the clamped yaw), so the body and view never desync.
            float yaw = _lookYaw != null ? _lookYaw() : 0f;
            _facing = Quaternion.LookRotation(_faceDir, Vector3.up)
                      * Quaternion.Euler(0f, yaw, 0f);
            _ragdoll.FacingRotation = _facing;

            Vector3 kRight = _facing * Vector3.right;   // keeper's right in world space

            // A loose ball inside his reach is gathered without being asked, the way a keeper does.
            // Checked BEFORE the save inputs on purpose: if it is already slow enough and close
            // enough to pick up, picking it up beats diving past it.
            if (TryClaim()) return;

            // --- LMB / RMB reflex save: a lunge on the press edge, then he STAYS DOWN
            //     in the save pose for as long as any button is held (both = split).
            //     ManageSave holds/switches the pose and stands up on release. ---
            bool lmbClick = _input.LeftClickPressed;
            bool rmbClick = _input.RightClickPressed;
            if (lmbClick || rmbClick)
            {
                if (lmbClick && rmbClick) BeginSave(0f, kRight, KeeperPose.Split);
                else if (lmbClick)        BeginSave(-1f, kRight, KeeperPose.SaveLeft);
                else                      BeginSave(1f, kRight, KeeperPose.SaveRight);
                return;
            }

            float dir = _input.Move.x;                 // A = -1, D = +1 (his LEFT / RIGHT)
            float fb = _input.Move.y;                  // W = +1 forward, S = -1 back
            bool hasDir = Mathf.Abs(dir) > 0.4f;

            // --- Double-tap A or D = explosive LOW sideways dive, just off the ground. ---
            if (DetectDoubleTap(dir, hasDir))
            {
                LaunchDashDive(Mathf.Sign(dir));
                return;
            }

            // --- A/D + Space = upward dive; reach/height scale with prior speed. ---
            if (_input.JumpPressed && hasDir)
            {
                LaunchDive(Mathf.Sign(dir));
                return;
            }

            // Space with NO direction = straight jump up with arms up.
            if (_input.JumpPressed && grounded)
            {
                Jump();
                return;
            }

            // --- Normal: strafe/move relative to facing (covers sideways positioning). ---
            Move(dir, fb);
            _ragdoll.SetPose(KeeperPose.Ready, 8f);
            ShuffleGait(dir, fb);
        }

        // Alternating steps while he moves on his line, layered over the Ready pose. The
        // body glides via velocity; these are cosmetic. Feet pick up with a hard knee
        // fold and the arms pump so it reads as a proper run, not a glide.
        void ShuffleGait(float dir, float fb, bool arms = true)
        {
            float moveAmt = Mathf.Max(Mathf.Abs(dir), Mathf.Abs(fb));
            if (moveAmt < 0.2f) { _shufflePhase = 0f; return; }
            _shufflePhase += Time.deltaTime * SimConfig.KeeperShuffleRate * moveAmt;

            float s = Mathf.Sin(_shufflePhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-liftL * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.KeeperShuffleKnee, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(-liftR * SimConfig.KeeperShuffleLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.KeeperShuffleKnee, 0f, 0f));

            // Arms pump opposite the same-side leg, elbows bent. Skipped while he is holding the
            // ball: the pump and the Hold clamp drive the same four bones, and additive overrides on
            // top of the clamp would just unfold it.
            if (!arms) return;
            float armL = -s, armR = s;
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(armL * SimConfig.ArmPumpSwing, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ForearmL,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(armR * SimConfig.ArmPumpSwing, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ForearmR,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
        }

        // Fresh A/D tap this frame? Returns true only on a double-tap (two taps of the
        // same direction within the window).
        bool DetectDoubleTap(float dir, bool hasDir)
        {
            bool freshTap = hasDir && !_dirWasDown;
            _dirWasDown = hasDir;
            if (!freshTap) return false;

            float d = Mathf.Sign(dir);
            bool doubled = (Time.time - _lastTapTime) < SimConfig.KeeperDoubleTapWindow
                           && Mathf.Approximately(d, _lastTapDir);
            _lastTapTime = Time.time;
            _lastTapDir = d;
            if (doubled) { _lastTapTime = -10f; return true; }  // consume so it doesn't retrigger
            return false;
        }

        // Reflex save: one-time sideways lunge with arm+leg out, on his feet. Locomotion
        // steering is OFF so the lunge momentum carries him sideways instead of being
        // instantly arrested. Very short timer -> gets up immediately.
        Vector3[] _savePose;
        void BeginSave(float dir, Vector3 kRight, Vector3[] pose)
        {
            _state = State.Saving;
            _saveReleaseTimer = -1f;                 // -1 = still held
            _saveSettle = 0f;
            _savePose = pose;
            _ragdoll.LocomotionEnabled = false;      // let the lunge carry
            _ragdoll.MoveInput = Vector3.zero;
            if (Mathf.Abs(dir) > 0.1f)
                _ragdoll.AddVelocityToAll(kRight * (dir * SimConfig.KeeperSaveLunge));
            _ragdoll.SetPose(pose, 16f);
        }

        void ManageSave()
        {
            // Gathering mid-lunge is fair game, same as mid-dive. This is also what makes
            // TryClaim's State.Saving restore arm reachable: without it, holding the button
            // pinned him in the save pose and the rebound stayed live until he let go.
            if (TryClaim()) return;

            bool lmb = _input.LeftLegHeld, rmb = _input.RightLegHeld;

            // Live-switch the held pose: both = split, else the one-sided reach.
            if (lmb || rmb)
            {
                _savePose = (lmb && rmb) ? KeeperPose.Split
                          : lmb ? KeeperPose.SaveLeft : KeeperPose.SaveRight;
                _saveReleaseTimer = -1f;             // stay down while held
            }
            _ragdoll.SetPose(_savePose, 16f);        // hold the reach

            // Released: brief settle, then stand - but ONLY once he has actually slowed down, not
            // just after a flat timer. BeginSave's lunge (KeeperSaveLunge) is a real velocity
            // impulse with LocomotionEnabled off, so it barely decays on its own; recovering on a
            // timer alone let a spammed LMB/RMB re-trigger BeginSave - another full lunge, additive
            // - before the previous one had bled off, compounding speed with every click.
            if (!lmb && !rmb)
            {
                if (_saveReleaseTimer < 0f) _saveReleaseTimer = SimConfig.KeeperSaveReleaseTime;
                _saveReleaseTimer -= Time.deltaTime;
                bool timerDone = _saveReleaseTimer <= 0f;
                if (timerDone && _ragdoll.GroundSpeed < SimConfig.KeeperSaveSettleSpeed)
                    _saveSettle += Time.deltaTime;
                else
                    _saveSettle = 0f;
                if (timerDone && _saveSettle >= SimConfig.KeeperSaveSettleTime) RecoverToReady();
            }
        }

        // Return to the ready stance, facing forward out toward the pitch. `stumble` routes him
        // through the getting-up beat first (ManageStumble) instead of appearing upright instantly.
        void RecoverToReady(bool stumble = false)
        {
            _state = State.Ready;
            _facing = Quaternion.LookRotation(_faceDir, Vector3.up);
            _ragdoll.FacingRotation = _facing;   // face forward again after getting up
            _ragdoll.BodyOrientTarget = null;    // stop driving the dive lay-out
            _ragdoll.SnapFacing(_facing);        // hard-snap to forward (no wrong-way slew)
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;         // then keep it upright + facing
            _airPose = null;

            if (stumble)
            {
                _state = State.Stumble;
                _stumbleTotal = Mathf.Max(0.05f, SimConfig.KeeperStumbleTime * PlayerProfile.RecoveryTimeMul);
                _stumbleTimer = _stumbleTotal;
                _ragdoll.SetPose(KeeperPose.Rise, 10f);
            }
            else _ragdoll.SetPose(KeeperPose.Ready, 12f);
        }

        void Move(float dir, float fb)
        {
            Vector3 right = _facing * Vector3.right;      // keeper's right in world space
            Vector3 fwd = _facing * Vector3.forward;      // out toward the pitch
            Vector3 vel = right * (dir * SimConfig.KeeperStrafeSpeed)
                        + fwd * (fb * SimConfig.KeeperStrafeSpeed);

            // Clamp lateral shuffle to a window around centre (x only).
            float x = _ragdoll.Pelvis.position.x;
            if ((x > SimConfig.KeeperStrafeXLimit && vel.x > 0f) ||
                (x < -SimConfig.KeeperStrafeXLimit && vel.x < 0f))
                vel.x = 0f;

            _ragdoll.MoveInput = vel;
        }

        void Jump()
        {
            // Straight up, arms overhead. Actively driven to stay UPRIGHT through the
            // flight (the free pelvis would otherwise tip forward and faceplant), lands
            // on his feet, gets up.
            _state = State.Diving;
            _diveIsJump = true;
            _diveAir = 0f; _diveGround = 0f;
            _airPose = KeeperPose.Jump;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
            _ragdoll.BodyOrientTarget = _facing;     // hold vertical, no forward topple
            _ragdoll.LaunchVerticalAll(SimConfig.KeeperJumpVel);
            _ragdoll.SetPose(KeeperPose.Jump, 16f);
        }

        // A/D + Space: upward dive whose reach/height scale with how fast he was
        // already moving when Space was pressed (momentum carries into the dive).
        void LaunchDive(float dir)
        {
            float priorSpeed = new Vector3(_ragdoll.Pelvis.linearVelocity.x, 0f, _ragdoll.Pelvis.linearVelocity.z).magnitude;
            float horiz = SimConfig.KeeperDiveHorizBase + SimConfig.KeeperDiveHorizPerV * priorSpeed;
            float up = SimConfig.KeeperDiveUpBase + SimConfig.KeeperDiveUpPerV * priorSpeed;
            // The jump-height setting also scales how high the high dive goes.
            up *= SimConfig.KeeperJumpVel / SimConfig.KeeperJumpVelBase;
            DoDive(dir, horiz, up, SimConfig.KeeperDiveLayoutHigh, isHigh: true);
        }

        // Double-tap A/D: explosive LOW sideways dive, just off the ground (fixed).
        void LaunchDashDive(float dir)
        {
            DoDive(dir, SimConfig.KeeperDashDive, SimConfig.KeeperDashUp, SimConfig.KeeperDiveLayoutLow, isHigh: false);
        }

        // Shared dive launch: sideways+up velocity, plus an ACTIVELY DRIVEN roll to a
        // rolled (near-)horizontal target that the ragdoll HOLDS - so he reliably reaches
        // that lay-out by the apex regardless of airtime (a one-shot impulse alone can't
        // guarantee "parallel at the high point"). Locomotion off so momentum carries.
        void DoDive(float dir, float horiz, float up, float layoutDeg, bool isHigh)
        {
            _state = State.Diving;
            _diveIsJump = false;
            _diveIsHigh = isHigh;
            _diveDir = dir;
            _diveAir = 0f; _diveGround = 0f;
            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;

            Vector3 right = _facing * Vector3.right;
            Vector3 fwd = _facing * Vector3.forward;
            _ragdoll.AddVelocityToAll(right * (dir * horiz) + Vector3.up * up);

            // Target: facing rolled about the forward axis so the body lies flat ON the
            // dive side. Sign is -dir: diving right (dir=+1) must tip him onto his RIGHT
            // (the un-negated version tipped him the wrong way). Driven+held via BodyOrientTarget.
            _diveOrient = Quaternion.AngleAxis(-dir * layoutDeg, fwd) * _facing;
            _ragdoll.BodyOrientTarget = _diveOrient;

            // Initial roll kick in the same direction so the lay-out snaps in immediately.
            _ragdoll.AddTorqueToPelvis(fwd * (-dir * SimConfig.KeeperDiveRoll));

            // High dive gets its own arms-overhead base pose; the low dash dive keeps the wide star.
            _airPose = isHigh ? KeeperPose.DiveHigh : KeeperPose.Dive;
            _ragdoll.SetPose(_airPose, 16f);
        }

        // Landing-gated recovery: hold the dive pose through the flight; only get up
        // AFTER he has come down and settled (fixes mid-air righting + cut-short reach).
        void ManageDive(bool grounded)
        {
            // Gathering it mid-dive is the whole difference between a save and a catch, so the claim
            // is tried here too. Only a slow ball passes CanClaim, so a rocket is still parried.
            if (TryClaim()) return;

            if (_airPose != null) _ragdoll.SetPose(_airPose, 16f);

            if (_diveIsJump)
            {
                // Keep driving him UPRIGHT the whole flight so he can't topple forward.
                _ragdoll.BodyOrientTarget = _facing;
            }
            else
            {
                // Keep driving the horizontal lay-out the whole flight.
                _ragdoll.BodyOrientTarget = _diveOrient;

                // On a dive to one side he lands on that side, so the TOP leg (the one
                // opposite the dive direction) is the leading leg that folds up hard;
                // the bottom leg bends a little. (This is the flip of the earlier wiring
                // that read backwards.)
                Bone leadThigh = _diveDir < 0f ? Bone.ThighR : Bone.ThighL;
                Bone leadCalf  = _diveDir < 0f ? Bone.CalfR  : Bone.CalfL;
                Bone backThigh = _diveDir < 0f ? Bone.ThighL : Bone.ThighR;
                Bone backCalf  = _diveDir < 0f ? Bone.CalfL  : Bone.CalfR;
                _ragdoll.SetPoseOverride(leadThigh, new Vector3(-SimConfig.KeeperDiveLeadKnee * 0.5f, 0f, 0f));
                _ragdoll.SetPoseOverride(leadCalf,  new Vector3(SimConfig.KeeperDiveLeadKnee, 0f, 0f));
                _ragdoll.SetPoseOverride(backThigh, new Vector3(-SimConfig.KeeperDiveBackKnee * 0.5f, 0f, 0f));
                _ragdoll.SetPoseOverride(backCalf,  new Vector3(SimConfig.KeeperDiveBackKnee, 0f, 0f));

                // The high dive's arms-overhead shape lives in its BASE pose (KeeperPose.DiveHigh,
                // set in DoDive), so no additive arm override here - overrides ADD to the base, so
                // layering onto the wide arms only tilted them instead of lifting them overhead.
            }

            _diveAir += Time.deltaTime;

            // Consider him landed once he's been airborne a moment AND is back on the
            // ground (or the safety cap trips).
            bool landed = _diveAir > SimConfig.KeeperDiveMinAir && grounded;
            if (landed) _diveGround += Time.deltaTime; else _diveGround = 0f;

            if (_diveGround >= SimConfig.KeeperDiveSettle || _diveAir > SimConfig.KeeperDiveMaxTime)
            {
                // A dive put him on the floor, so he stumbles up. A straight jump landed on his feet
                // and a lunge save never left them, so neither does (ManageSave calls this plain):
                // a reflex save has to stay snappy or it stops being a reflex.
                RecoverToReady(stumble: !_diveIsJump);
            }
        }

        // ================================================================ getting up

        // The beat between landing and being ready again. The AI keeper has one for free - it walks
        // back to its guard spot while balance re-engages - but the human keeper snapped from prone
        // to Ready in a single frame, which read as weightless. So: push up out of KeeperPose.Rise
        // while the leftover sideways momentum of the dive bleeds off, then blend to Ready over the
        // back half. Length scales with PlayerProfile.RecoveryTimeMul, the same Agility ladder that
        // already shortens time spent prone, so an agile keeper is up quicker.
        //
        // Cancellable, and that matters: a keeper frozen in an animation while a rebound is being
        // knocked back at him is worse than one with no stumble at all. Any save or jump press ends
        // it early. Returns false when it has ended, so Tick falls through and serves that press now.
        bool ManageStumble()
        {
            _stumbleTimer -= Time.deltaTime;
            float t = Mathf.Clamp01(1f - _stumbleTimer / _stumbleTotal);   // 0 = just landed, 1 = settled

            _ragdoll.FacingRotation = _facing;
            Vector3 kRight = _facing * Vector3.right;
            _ragdoll.MoveInput = kRight * (_diveDir * SimConfig.KeeperStumbleStep * (1f - t));

            // Rise for the first half, Ready for the second. SetPose early-outs when handed the pose
            // it already has, so calling it every frame costs nothing and the switch blends.
            _ragdoll.SetPose(t < 0.5f ? KeeperPose.Rise : KeeperPose.Ready, Mathf.Lerp(6f, 12f, t));
            // Ready half only. Gait overrides ADD to the base pose, and Rise is already a deep
            // crouch: stacked on it the knee folds past 200 degrees and the shin passes through
            // its own thigh, while the arm pump replaces the braced hands.
            if (t >= 0.5f) ShuffleGait(_diveDir * (1f - t) * 0.9f, 0f);
            else _shufflePhase = 0f;

            if (TryClaim()) return true;   // a rebound arriving as he gets up is fair game

            bool act = _input.LeftClickPressed || _input.RightClickPressed || _input.JumpPressed;
            if (_stumbleTimer <= 0f || act)
            {
                _state = State.Ready;
                _shufflePhase = 0f;
                _ragdoll.ClearPoseOverrides();
                return false;
            }
            return true;
        }

        // ================================================================ hands

        // A human keeper has no difficulty slider, so his hands come off his Control stat instead.
        // Note this deliberately does NOT apply the AI's KeeperClaimMinAbility gate: that gate exists
        // to make an EASY opponent keeper bad on purpose, and handing a player a keeper who
        // physically cannot pick a ball up is a bug, not a stat.
        float Hands => Mathf.Lerp(SimConfig.KeeperHumanHandsRaw, SimConfig.KeeperHumanHandsSkilled,
                                  Mathf.Clamp01(PlayerProfile.DribbleTightness));

        // Sign of "upfield" for him: away from the goal he was BUILT in. A fact about the pitch,
        // fixed once at Init, kept separate from _faceDir (which is a rendering decision a mode
        // makes) so distribution stays correct however a mode chooses to point him.
        float OutZ => _outZ;

        // Gather a loose ball if there is one to gather.
        //
        // The only zone rule is his own half. The AI needs a radius around its goal because it roams;
        // a human is pinned to his line by his legs and KeeperStrafeXLimit, and KeeperHands.CanClaim
        // already demands the ball be within arm's reach of his chest, so a radius here would add
        // nothing except a dependence on GoalCenter that a resized match pitch would break.
        bool TryClaim()
        {
            if (_ball == null || _ragdoll.Pelvis == null) return false;
            if (_ball.transform.position.z * OutZ > 0f) return false;   // upfield of halfway: not his to handle
            // Rejected claim -> parry. Without this the tighter gate would leave a shot bobbling off
            // his capsules on restitution alone, which is exactly the ball-through-the-keeper feel.
            if (!_hands.CanClaim(Hands)) { _hands.TryParry(Hands); return false; }

            _hands.Claim();
            // He may have taken it mid-dive or mid-lunge, both of which have physics torn down.
            if (_state == State.Diving || _state == State.Saving || _state == State.Stumble) RestoreAfterDive();
            _state = State.Holding;
            _shufflePhase = 0f;
            _ragdoll.SetPose(KeeperPose.Hold, 10f);
            return true;
        }

        // Undo everything a dive/lunge switched off. Same set RecoverToReady restores, minus the
        // state change, because a claim goes to Holding rather than Ready.
        void RestoreAfterDive()
        {
            _airPose = null;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.SnapFacing(_facing);
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;
        }

        // Holding: elbows bent around the ball (KeeperPose.Hold), still free to shuffle along his
        // line, arm pump off. E rolls it out flat, Q punts it. Sitting on it plays it out by itself -
        // both a nod to the six-second law and, more practically, a guarantee that a held ball can
        // never park the match (a disconnected or idle keeper would otherwise hold it forever).
        void ManageHold()
        {
            // Hold() returns false if something moved the ball out from under him: a mode reset, a
            // kickoff, the stuck-ball watchdog. Nothing can leave him welded to a ball that has gone.
            if (!_hands.Hold(Time.deltaTime))
            {
                _state = State.Ready;
                _ragdoll.SetPose(KeeperPose.Ready, 10f);
                return;
            }

            float yaw = _lookYaw != null ? _lookYaw() : 0f;
            _facing = Quaternion.LookRotation(_faceDir, Vector3.up) * Quaternion.Euler(0f, yaw, 0f);
            _ragdoll.FacingRotation = _facing;

            float dir = _input.Move.x, fb = _input.Move.y;
            Move(dir, fb);
            _ragdoll.SetPose(KeeperPose.Hold, 10f);
            ShuffleGait(dir, fb, arms: false);

            bool flat = _input.PassGroundPressed;
            bool punt = _input.PassLoftedPressed;
            if (flat || punt || _hands.HeldFor >= SimConfig.KeeperHumanHoldMax)
            {
                _hands.Release(DistributeAim(), lofted: !flat, Hands);   // no press = the auto-punt
                _state = State.Ready;
                _shufflePhase = 0f;
                _ragdoll.SetPose(KeeperPose.Ready, 10f);
            }
        }

        // Where a played ball goes: upfield, steered laterally by where he is looking. Clamped inside
        // the touchlines so a punt from the corner of the six-yard box does not sail into the crowd.
        Vector3 DistributeAim()
        {
            Vector3 p = _ragdoll.Pelvis.position;
            float yaw = _lookYaw != null ? _lookYaw() : 0f;
            Vector3 aim = p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, OutZ)
                              * SimConfig.KeeperDistributeRange;
            aim.x = Mathf.Clamp(aim.x, -AimBounds.x, AimBounds.x);
            aim.z = Mathf.Clamp(aim.z, -AimBounds.y, AimBounds.y);
            aim.y = SimConfig.BallRadius;
            return aim;
        }

        public void ForceRecover()
        {
            _hands.Drop();          // never hand a reset a keeper still holding the old ball
            _state = State.Ready;
            _saveReleaseTimer = -1f;
            _stumbleTimer = 0f;
            _diveAir = 0f; _diveGround = 0f;
            _airPose = null;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.SetPose(KeeperPose.Ready, 6f);
        }
    }
}
