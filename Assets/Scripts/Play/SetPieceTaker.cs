using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Drives a free-kick / set-piece attempt where the RUN-UP and SWING are purely aesthetic
    /// (AI-driven) and the player controls ONLY a power meter (Space) plus WASD spin. The ball is
    /// launched by CODE (BallController.LaunchSetPiece), never by a physical foot contact, so the
    /// striker's body collider is disabled during the kick and the ball is guaranteed on-frame.
    ///
    /// Flow (one attempt):
    ///   Idle     - waiting to be armed.
    ///   Charging - the ball is dead on the spot; the player HOLDS Space to swing an oscillating
    ///              power meter (green->red ping-pong) and HOLDS WASD to charge spin (D/A curve,
    ///              W topspin, S knuckle), all silently. RELEASING Space commits. Pegging the meter
    ///              at max too long OVERCHARGES, or holding a spin dir too long OVER-CHARGES it: both
    ///              feed a `botch` amount that sprays the shot.
    ///   Runup    - the AI striker runs in to the ball (Footballer-style gait) and plays a swing.
    ///   Struck   - the scripted launch has fired; the ball is live.
    ///   Settle   - brief hold, then Idle.
    ///
    /// Both the single-player driver (local device input) and the multiplayer host (the active
    /// shooter's IStrikerInput, local or networked) own one of these and pump Tick() each frame.
    /// It reuses the existing wire: Space = Jump bit, WASD = Move axis, so no new netcode is needed.
    /// </summary>
    public class SetPieceTaker
    {
        public enum State { Idle, Charging, Runup, Struck, Settle }

        IStrikerInput _input;
        ActiveRagdoll _ragdoll;
        BallController _ball;
        Vector3 _ballSpot;
        Vector3 _goalCenter;
        bool _displayOnly;     // client prediction: animate + meter HUD, but do NOT launch the ball
                               // (the host is authoritative; the client ball is kinematic).
        float _combinedOverride = -1f;   // >=0 forces the skill stat (MP remote shooter); <0 = local profile.
        System.Func<Vector3> _aimPoint;  // null = use BallController's built-in corner auto-aim (AI path).
                                         // non-null = the driver's look-ray aim (SP free kick / MP set piece).

        State _state = State.Idle;
        float _meter;          // 0..1 power meter value (ping-pongs while charging)
        float _meterDir = 1f;  // sweep direction
        float _pegTime;        // seconds the meter has been pegged at max (overcharge timer)
        float _spinDir;        // per-direction spin charge accumulators (0..1, then over)
        float _spinCharge;     // 0..1 chosen spin magnitude at release
        float _spinOverTime;   // seconds a spin dir held past full (over-hold botch)
        BallController.SetPieceSpin _spin;
        float _committedPower, _committedSpinCharge, _committedBotch, _committedCombined;
        float _committedOvercharge;   // overcharge (power-bar) botch ONLY -> drives the over-the-bar loft
        float _committedPowerStat;    // 0..1 power STAT -> scales ONLY the launch-speed ceiling (not height)
        BallController.SetPieceSpin _committedSpin;
        Vector3? _committedAim;   // look-ray aim captured at release (null = built-in auto-aim)
        float _spaceHeldTime;     // seconds Space has been held this attempt (gates a genuine charge)

        float _phaseTime;      // time in the current Runup/Struck/Settle phase
        float _gaitPhase;
        // Which foot this taker kicks with. Footedness is authored (PlayerProfile.LeftFooted, set on
        // the customize screen) but is NOT on the wire, so the host animating a REMOTE shooter has no
        // way to know theirs - Begin takes an override for that case, the same shape _combinedOverride
        // already uses for the skill stat, and falls back to the local profile.
        bool _leftFooted;
        float _hopGrace;       // counts down after the contact hop before the upright lock re-engages
        bool _launched;
        bool _charged;         // the player has begun holding Space this attempt (gates commit)
        float _releaseTime;    // seconds Space has read UP since the last held frame (debounce vs a 1-frame input drop)
        bool _awaitingRelease; // Space was ALREADY held when this attempt armed (stale actuation carried in
                               // from the menu/confirm that launched the mode, or the input map enabling
                               // mid-press). Blocks all charging until Space is released once, so the FIRST
                               // attempt of a round can't commit off a held key with no genuine release.

        // ---- public read surface (HUD + driver) ----
        public State Phase => _state;
        public bool Active => _state != State.Idle;              // an attempt is in progress
        public bool IsCharging => _state == State.Charging;
        public float Meter => _meter;                            // 0..1 for the power bar
        public bool JustStruck { get; private set; }             // true only on the launch frame
        public bool Done => _state == State.Idle;
        public bool HasCharged => _charged;                      // player has begun holding Space this attempt

        // Arm a fresh attempt: the ball is on `ballSpot`, the striker ragdoll is placed behind it
        // (facing the goal) by the driver's re-arm. `input` is the taker's input source (local
        // device or the active shooter's net input on the host).
        // `combinedOverride` >= 0 forces the skill stat (used in MP where a remote shooter's skill
        // tree is not synced to the host: pass a neutral value). < 0 = derive from the local
        // PlayerProfile (single-player and the local shooter).
        // `leftFootedOverride`: -1 = use the local PlayerProfile (single player and the local
        // shooter), 0 = right footed, 1 = left footed. Same -1-means-local idiom as combinedOverride.
        public void Begin(IStrikerInput input, ActiveRagdoll ragdoll, BallController ball,
                          Vector3 ballSpot, Vector3 goalCenter, bool displayOnly = false,
                          float combinedOverride = -1f, System.Func<Vector3> aimPoint = null,
                          int leftFootedOverride = -1)
        {
            _input = input; _ragdoll = ragdoll; _ball = ball;
            _ballSpot = ballSpot; _goalCenter = goalCenter;
            _displayOnly = displayOnly;
            _combinedOverride = combinedOverride;
            _aimPoint = aimPoint;
            _state = State.Charging;
            _meter = 0f; _meterDir = 1f; _pegTime = 0f;
            _spinDir = 0f; _spinCharge = 0f; _spinOverTime = 0f;
            _spin = BallController.SetPieceSpin.None;
            _leftFooted = leftFootedOverride < 0 ? KickSwing.LocalFoot : leftFootedOverride == 1;
            _hopGrace = 0f;
            _phaseTime = 0f; _plantTime = 0f; _gaitPhase = 0f; _launched = false; _charged = false;
            _releaseTime = 0f;
            _spaceHeldTime = 0f;
            // If Space is ALREADY down the moment we arm (a stale actuation carried in from the
            // menu/confirm that opened this mode, or the input map enabling mid-press), require a
            // genuine release before charging can begin. Otherwise the first attempt's very first
            // Tick reads Space held, latches _charged, and commits with no real release.
            _awaitingRelease = input != null && input.JumpHeld;
            _committedAim = null;
            JustStruck = false;
            // The taker OWNS the ball for the whole attempt, so the shooter's body must not be able
            // to touch it from the moment we arm - not just from release. Re-enabling collision here
            // let the runup/plant foot physically graze the dead ball, and because SetPieceShot skips
            // the swing-speed gate, even a gentle brush fired a full-power PHYSICAL set-piece strike
            // whose target is derived from the CONTACT POINT (a top/bottom corner) and completely
            // ignores the camera. That is why shots flew to the top of the net no matter where the
            // player looked. The ball is launched by code (LaunchSetPiece) with the look-ray aim, so
            // physical contact is never wanted while a taker is live.
            SetColliders(false);
            if (_ragdoll != null) _ragdoll.UprightLock = true;
        }

        // Ray from the dead-ball spot along the camera look direction, intersected with the
        // goal plane (z = goalPlaneZ). Returns the world aim point; y comes from camera pitch.
        public static Vector3 LookAimPoint(Vector3 from, float yaw, float pitch, float goalPlaneZ)
        {
            Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
            float dz = goalPlaneZ - from.z;

            // Camera pointing AWAY from the goal (the look ray never crosses the goal plane on the
            // goal side): there is no honest aim point in front. Project the look direction straight
            // onto the goal plane far off to whichever side it faces, so the aim lands well OUTSIDE
            // the shot cone and the caller forces the shot wide (no laser-curl back to a corner).
            // Previously this faked a forward point (t = |dz|), which let a backward look still aim
            // on-goal and curve into the top corner.
            bool towardGoal = (dir.z * dz) > 0f && Mathf.Abs(dir.z) >= 0.05f;
            if (!towardGoal)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude < 1e-4f) flat = new Vector3(Mathf.Sign(yaw == 0f ? 1f : yaw), 0f, 0f);
                Vector3 wide = from + flat.normalized * (Mathf.Abs(dz) + 30f);   // far to the side, off-cone
                wide.z = goalPlaneZ;   // on the goal plane so downstream math is well-formed
                wide.y = Mathf.Max(0.05f, from.y + dir.y * Mathf.Abs(dz));
                return wide;
            }

            float t = dz / dir.z;
            Vector3 p = from + dir * t;
            p.y = Mathf.Max(0.05f, p.y);
            return p;
        }

        // Force back to idle (turn change / reset). Restores the body collider.
        public void Reset()
        {
            _state = State.Idle;
            JustStruck = false;
            SetColliders(true);
        }

        public void Tick()
        {
            JustStruck = false;
            if (_ragdoll == null || _ragdoll.Pelvis == null || _input == null) return;

            switch (_state)
            {
                case State.Charging: TickCharging(); break;
                case State.Runup:    TickRunup();    break;
                case State.Struck:   TickStruck();   break;
                case State.Settle:   TickSettle();   break;
            }
        }

        // Skill-only combined stat: full Shooting+Control -> 1 regardless of body build. Widens the
        // good windows + pulls the aim to a corner + tightens scatter + drives the swerve. In THIS
        // mode accuracy is the primary driver, so it is weighted heavily over power (0.8 vs 0.2);
        // the power stat's real influence lives in PowerStat() below (launch-speed ceiling only).
        // Honors an explicit override (MP remote shooter, whose skill tree is not synced).
        float Combined()
        {
            if (_combinedOverride >= 0f) return Mathf.Clamp01(_combinedOverride);
            float accStat = Mathf.Clamp01((PlayerProfile.ShotAccuracyMul - 1f) / 0.97f);
            float powStat = Mathf.Clamp01((SkillTree.Mul("shotpower") - 1f) / 0.68f);
            return Mathf.Clamp01(0.8f * accStat + 0.2f * powStat);
        }

        // 0..1 power STAT, skill-only (SkillTree, not the body-coupled ShotPowerMul so build never
        // gates it). Scales ONLY the launch-SPEED ceiling in LaunchSetPiece, never the height. When
        // an override is set (MP remote shooter) we can't read the stat, so use a neutral mid value.
        float PowerStat()
        {
            if (_combinedOverride >= 0f) return 0.5f;
            return Mathf.Clamp01((SkillTree.Mul("shotpower") - 1f) / 0.68f);
        }

        void TickCharging()
        {
            float combined = Combined();

            // A Space that was already held when this attempt armed is stale: swallow it until it
            // is released once. Until then, no charging and no commit can happen, so the first
            // attempt of a round can't fire off a held key. A genuine fresh press then charges
            // normally. (The host's AFK watchdog still fires if a human truly never engages.)
            if (_awaitingRelease)
            {
                if (!_input.JumpHeld) _awaitingRelease = false;
                return;
            }

            // Oscillate the power meter while Space is held; release commits. We only commit once
            // the player has ACTUALLY begun charging (held Space at least once) so an attempt that
            // starts with Space up does not instantly fire on frame one.
            if (_input.JumpHeld)
            {
                _charged = true;
                _releaseTime = 0f;   // still held: a phantom 1-frame drop can't accrue enough to commit
                _spaceHeldTime += Time.deltaTime;   // total hold this attempt (gates a genuine charge)
                _meter += _meterDir * SimConfig.SetPieceMeterRate * Time.deltaTime;
                if (_meter >= 1f) { _meter = 1f; _meterDir = -1f; }
                else if (_meter <= 0f) { _meter = 0f; _meterDir = 1f; }

                // Overcharge: pegged at the very top too long -> botch. Accuracy widens the window.
                bool pegged = _meter > 0.97f;
                _pegTime = pegged ? _pegTime + Time.deltaTime : 0f;

                // WASD spin charge (silent). Move.x sign -> curve L/R, Move.y sign -> topspin/knuckle.
                // A dominant axis wins so a diagonal does not fight itself.
                Vector2 mv = _input.Move;
                BallController.SetPieceSpin heldSpin = BallController.SetPieceSpin.None;
                if (Mathf.Abs(mv.x) > Mathf.Abs(mv.y))
                {
                    if (mv.x > 0.3f) heldSpin = BallController.SetPieceSpin.CurveRight;
                    else if (mv.x < -0.3f) heldSpin = BallController.SetPieceSpin.CurveLeft;
                }
                else
                {
                    if (mv.y > 0.3f) heldSpin = BallController.SetPieceSpin.TopSpin;
                    else if (mv.y < -0.3f) heldSpin = BallController.SetPieceSpin.Knuckle;
                }

                if (heldSpin != BallController.SetPieceSpin.None)
                {
                    if (heldSpin != _spin) { _spin = heldSpin; _spinDir = 0f; _spinOverTime = 0f; }
                    _spinDir += SimConfig.SetPieceSpinChargeRate * Time.deltaTime;
                    if (_spinDir >= 1f)
                    {
                        _spinDir = 1f;
                        _spinOverTime += Time.deltaTime;   // over-hold botch timer (widened by accuracy)
                    }
                    _spinCharge = _spinDir;
                }

                return;
            }

            // Space is up. Wait until the player has begun charging before a release can commit,
            // so an attempt armed with Space up does not fire on frame one.
            if (!_charged) return;

            // A near-instant TAP is not a shot: require a genuine minimum charge before any commit.
            // A normal keypress (~0.1s) used to fall in the old tap-pass window and fired the moment
            // Space came up - "the first set piece fires as soon as I hit space". Now a sub-threshold
            // press commits NOTHING; reset the charge state so the next real hold charges from scratch.
            if (_spaceHeldTime < SimConfig.SetPieceMinChargeTime)
            {
                _charged = false;
                _meter = 0f; _meterDir = 1f; _pegTime = 0f;
                _spinDir = 0f; _spinCharge = 0f; _spinOverTime = 0f;
                _spin = BallController.SetPieceSpin.None;
                _spaceHeldTime = 0f; _releaseTime = 0f;
                return;
            }

            // Debounce the release: Space must stay up for a short continuous window before we
            // commit. A genuine release clears this in a blink; a single dropped input frame
            // (focus blip / hitch) that briefly reads Space up gets reset by the held branch next
            // frame, so it can no longer fire the shot mid-hold. The meter keeps its value while we
            // wait, so a real release commits the value that was on the bar when the key came up.
            _releaseTime += Time.deltaTime;
            if (_releaseTime < SimConfig.SetPieceReleaseDebounce) return;

            // Released: commit. A quick tap still fires a soft, central shot.
            _committedCombined = combined;
            _committedPower = _meter;
            _committedPowerStat = PowerStat();
            _committedSpin = _spin;
            _committedSpinCharge = _spinCharge;

            // Freeze the aim ray at release (the camera can move during the runup).
            _committedAim = _aimPoint != null ? (Vector3?)_aimPoint() : null;

            // Botch from overcharge + spin over-hold, each widened by accuracy. 0 = clean.
            float overWin = SimConfig.SetPieceOverchargeTime * (1f + combined);
            float spinWin = SimConfig.SetPieceSpinOverTime * (1f + combined);
            float overchargeBotch = Mathf.Clamp01(_pegTime / Mathf.Max(0.05f, overWin));
            float spinBotch = Mathf.Clamp01(_spinOverTime / Mathf.Max(0.05f, spinWin));
            // Scatter botch (sprays the target) is the worse of the two. Overcharge is ALSO kept
            // separate: only overpowering the power BAR lofts the ball over the bar (spin over-hold
            // sprays sideways but must NOT skyrocket), and that loft is independent of the power stat.
            _committedBotch = Mathf.Clamp01(Mathf.Max(overchargeBotch, spinBotch));
            _committedOvercharge = overchargeBotch;

            _state = State.Runup;
            _phaseTime = 0f;
            _gaitPhase = 0f;
            SetColliders(false);   // body cannot knock the ball during the runup/flight
        }

        // Aesthetic run-in toward the ball (Footballer.Drive/RunGait style), then a brief swing, then
        // the scripted launch. Player input is IGNORED here (runup + swing are not player-controlled).
        void TickRunup()
        {
            _phaseTime += Time.deltaTime;
            _ragdoll.ClearPoseOverrides();

            Vector3 me = _ragdoll.Pelvis.position; me.y = 0f;
            Vector3 target = _ballSpot; target.y = 0f;
            Vector3 to = target - me; to.y = 0f;
            float dist = to.magnitude;
            Vector3 dir = dist > 0.05f ? to / dist : Vector3.forward;

            float plant = SimConfig.SetPiecePlantOffset;
            if (dist > plant)
            {
                // Still approaching: steer + gait toward the ball.
                _ragdoll.MoveInput = dir * SimConfig.SetPieceRunupSpeed;
                _ragdoll.FacingRotation = Quaternion.LookRotation(dir, Vector3.up);
                RunGait(1f);
            }
            else
            {
                // Planted beside the ball: stop, play the cosmetic swing, then launch at contact.
                // _plantTime accrues only after arrival, so the swing starts when he reaches the
                // ball, not when the runup began.
                if (_plantTime <= 0f) _plantFacing = _ragdoll.FacingRotation;   // the arrival frame
                _ragdoll.MoveInput = Vector3.zero;
                _plantTime += Time.deltaTime;
                // 0..1 across the swing time, so contact lands exactly on the KickSwing clock's 1.
                float swing = Mathf.Clamp01(_plantTime / SimConfig.SetPieceSwingTime);
                ApplySwingPose(swing);
                if (!_launched && swing >= 1f)
                {
                    _launched = true;
                    // Display-only (client prediction): animate the swing but never touch the ball;
                    // the host owns the launch and the client ball is kinematic (snapshot-driven).
                    if (!_displayOnly)
                    {
                        _ball.ResetTo(_ballSpot);
                        _ball.LaunchSetPiece(_committedPower, _committedSpin, _committedSpinCharge,
                                             _committedBotch, _committedCombined, _goalCenter,
                                             _committedOvercharge, _committedPowerStat,
                                             _committedAim);
                    }
                    // The strike lifts him off the plant leg, toward goal. Fired even when display
                    // only: it is the animation, not the shot, and a predicting client must show the
                    // same body the host does.
                    KickSwing.Hop(_ragdoll, _goalCenter - _ballSpot, _leftFooted);
                    _hopGrace = SimConfig.KickHopGrace;
                    JustStruck = true;
                    _state = State.Struck;
                    _phaseTime = 0f;
                }
            }
        }

        // Time since the taker reached the plant (drives the swing timer, separate from the runup).
        float _plantTime;
        // Facing captured on arrival at the plant, so the swing's address angle is an offset from the
        // run-in line rather than an absolute heading.
        Quaternion _plantFacing = Quaternion.identity;

        // Play the swing OUT: the follow-through and then the rebalance onto the landing. This used to
        // hold ApplySwingPose(1f) - the contact pose, frozen - for a quarter of a second and then drop
        // straight to a stand, so the taker locked mid-kick and popped upright. The clock now runs on
        // past contact at the same rate the swing did, and the phase ends when the body is square.
        void TickStruck()
        {
            _phaseTime += Time.deltaTime;
            _ragdoll.ClearPoseOverrides();
            _ragdoll.MoveInput = Vector3.zero;
            float t = 1f + _phaseTime / SimConfig.SetPieceSwingTime;
            ApplySwingPose(t);
            if (KickSwing.Finished(t)) { _state = State.Settle; _phaseTime = 0f; }
        }

        void TickSettle()
        {
            _phaseTime += Time.deltaTime;
            _ragdoll.ClearPoseOverrides();
            if (_phaseTime > SimConfig.SetPieceSettleTime)
            {
                SetColliders(true);
                _state = State.Idle;
            }
        }

        // Cosmetic alternating-leg run gait (same shape as Footballer.RunGait), no player input.
        void RunGait(float amount)
        {
            if (amount < 0.05f) { _gaitPhase = 0f; return; }
            _gaitPhase += Time.deltaTime * SimConfig.StrideRateMax * amount;
            float s = Mathf.Sin(_gaitPhase);
            float liftL = Mathf.Max(0f, s), liftR = Mathf.Max(0f, -s);
            _ragdoll.SetPoseOverride(Bone.ThighL, new Vector3(-s * SimConfig.GaitThighSwing - liftL * SimConfig.GaitThighLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfL,  new Vector3(liftL * SimConfig.GaitKneeBend, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ThighR, new Vector3(s * SimConfig.GaitThighSwing - liftR * SimConfig.GaitThighLift, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.CalfR,  new Vector3(liftR * SimConfig.GaitKneeBend, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmR, new Vector3(s * SimConfig.ArmPumpSwing, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ForearmR,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.UpperArmL, new Vector3(-s * SimConfig.ArmPumpSwing, 0f, 0f));
            _ragdoll.SetPoseOverride(Bone.ForearmL,  new Vector3(-SimConfig.ArmPumpElbow, 0f, 0f));
            _ragdoll.SetPose(RagdollPose.Stand, 5f);
        }

        // The full kick, on this taker's own foot: windup, strike, follow-through, rebalance. See
        // KickSwing, which the AI crosser and the menu shooter play too. `t` is the KickSwing clock -
        // 1 is contact, and it keeps going after that.
        //
        // Also re-engages the upright lock that the contact hop released, once he is back on the turf.
        // Same grace-then-relock shape Striker.NormalJump uses.
        void ApplySwingPose(float t)
        {
            if (_hopGrace > 0f) _hopGrace = Mathf.Max(0f, _hopGrace - Time.deltaTime);
            if (_hopGrace <= 0f && _ragdoll.IsGrounded && !_ragdoll.UprightLock)
                _ragdoll.UprightLock = true;
            // Same body-yaw substitute the crosser uses, off the facing captured on arrival at the
            // plant, so the address angle is an offset from the line he ran in on.
            float yaw = KickSwing.YawOffset(t, _leftFooted, SimConfig.SetPieceSwingTime);
            _ragdoll.FacingRotation = _plantFacing * Quaternion.Euler(0f, yaw, 0f);
            KickSwing.Pose(_ragdoll, t, _leftFooted, SimConfig.SetPieceSwingTime);
        }

        // Make the BALL ignore this body during the kick so the aesthetic runup foot passes THROUGH
        // the parked ball (the shot is launched by code, not a physical kick). We do NOT disable the
        // body's own colliders - the feet still need to collide with the turf to stay grounded and
        // run. `on` = collisions active (restored); false = ignored during the runup/flight.
        void SetColliders(bool on)
        {
            if (_ball != null && _ragdoll != null) _ball.IgnoreBody(_ragdoll, !on);
        }
    }
}
