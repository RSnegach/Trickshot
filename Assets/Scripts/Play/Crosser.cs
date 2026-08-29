using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Auto-server. An active-ragdoll character on the wing that plays a full kicking SWING at the
    /// ball and launches a perfectly-solved cross at the moment of contact. The swing is cosmetic:
    /// the ball is always delivered on target regardless of the pose.
    ///
    /// It telegraphs the landing point with the reticle, winds the kicking leg back, then swings
    /// through - the launch fires as the leg passes through the ball, and the body carries on into a
    /// follow-through and a rebalance instead of freezing on the contact frame. The whole animation
    /// lives in KickSwing, shared with the set-piece taker, and it kicks with the player's OWN foot.
    /// GameManager pumps Tick() and reads JustServed.
    /// </summary>
    public class Crosser : MonoBehaviour
    {
        AimReticle _reticle;
        BallController _ball;
        Transform _launchPoint;
        ActiveRagdoll _ragdoll;
        public ActiveRagdoll Ragdoll => _ragdoll;   // so the net match can slot/puppet the crosser body

        float _timer;
        Vector3 _pendingTarget;
        float _pendingTime;
        Vector3 _pendingCurl;
        float _pendingSpin;
        bool _telegraphed;
        // KickSwing clock. 0..1 is the windup into contact; it keeps advancing past 1 through the
        // follow-through and the rebalance, then parks negative (idle). It used to stop dead at 1 and
        // hold the contact pose until the next serve armed, which is the freeze on the wing.
        float _kickT = -1f;
        float _hopGrace;     // counts down after the contact hop before the upright lock re-engages

        // Delivery overrides (freeplay). If TargetOverride is set, serves land there
        // instead of SimConfig.ServeTarget. If OriginOverride is set, the ball launches
        // from that world point (a corner flag) instead of the crosser's launch point.
        public Vector3? TargetOverride;
        public Vector3? OriginOverride;

        // Where a planted (AI) crosser belongs, captured by PlantAt/SetOrigin. Used to put him back
        // if he gets shoved off it. Null for a crosser nobody planted - and the menu reel RELIES on
        // it staying null, because its shooter jogs from his run-up start to his plant spot and an
        // armed snap-back would yank him home mid-run-up. Do not plant from Init for that reason.
        Vector3? _plantHome;
        Quaternion _plantFacing = Quaternion.identity;
        // A plant requested before Init bound the ragdoll, replayed from Init. No mode does that
        // today (every builder runs ActiveRagdoll.Build then Crosser.Init before any plant), so
        // this is insurance only: without it such a caller would move the launch origin and leave
        // the body standing wherever it was built, with no error to show why.
        Vector3? _pendingPlant;

        public bool JustServed { get; private set; }

        // When true (default), the crosser auto-serves on the ServeInterval loop. Set false so
        // it stays idle until ServeNow() is called (a human crosser, or a striker's called
        // pass). The cosmetic swing + perfect launch are shared by both paths.
        public bool AutoServe = true;

        // When true (default = an AI/planted crosser), the crosser plays the cosmetic leg-swing
        // pose and stays upright-locked. A MOBILE HUMAN crosser sets this false: a Striker owns
        // its pose + locomotion, so the swing is skipped and the body isn't re-planted.
        public bool Cosmetic = true;

        // When true, a serve launches the ball from the crosser's OWN FEET (a mobile human
        // crosser) rather than the fixed launch point. Set with Cosmetic=false.
        public bool ServeFromFeet;

        // AI/auto crosser delivery: false (default) = a LOFTED cross through the air; true = a
        // fast, flat GROUND cross. Toggled from the cross map's Crosser tab. Only affects the
        // auto/aimed serve (PickServe); the human crosser chooses per-serve via tap/hold.
        public bool GroundCross;

        public void Init(AimReticle reticle, BallController ball, Transform launchPoint, ActiveRagdoll ragdoll)
        {
            _reticle = reticle;
            _ball = ball;
            _launchPoint = launchPoint;
            _ragdoll = ragdoll;
            // A planted (AI/cosmetic) crosser stands upright-locked and doesn't walk. A mobile
            // human crosser (Cosmetic=false) leaves locomotion to its Striker - don't plant it.
            if (_ragdoll != null && Cosmetic)
            {
                _ragdoll.UprightLock = true;
                _ragdoll.LocomotionEnabled = false;
                _ragdoll.MoveInput = Vector3.zero;
            }
            // A plant that arrived before the body did: apply it now. Safe at this point because
            // every builder finishes ActiveRagdoll.Build before calling Init - Sim/GameBootstrap.cs
            // 601->635 and 708->716, Play/MenuBackground.cs 327->339 - so ResetTo has bones to move.
            if (_pendingPlant.HasValue && _ragdoll != null && Cosmetic)
            {
                _ragdoll.ResetTo(_pendingPlant.Value, _plantFacing);
                _pendingPlant = null;
            }
        }

        // Stand a planted (AI) crosser at `spot`, face him at his delivery target, and remember the
        // spot as his home so the drift backstop in ApplyKickPose can pull him back. Does NOT touch
        // where the ball launches from, which is why it is split out of SetOrigin: most modes want the
        // plant and already own their launch point. Returns the flat aim direction for SetOrigin.
        // No effect on a mobile human crosser (Cosmetic=false: it walks + serves from its feet).
        //
        // EVERY MODE WITH AN AI CROSSER MUST CALL THIS AT SETUP, and none of them used to. _plantHome
        // was written only by SetOrigin, and SetOrigin ran only when the player opened the cross map,
        // so on a fresh round the snap-back below was dead code and nothing held him in place. Each
        // contact hop (KickSwing.Hop: KickHopVel 1.9 up, KickHopDrift 1.1 forward, KickHopSide 0.7
        // lateral) then walked him about 0.6 m down the delivery line, which points at the shooter's
        // start spot - so after a handful of crosses he was standing next to the shooter while the
        // ball kept resting at the fixed wing launch point. That is the whole bug.
        //
        // Where the drift actually happens, because it is not where it looks: gravity here is
        // SimConfig.Gravity = -19.6, twice real, so 1.9 m/s of hop is only 0.194 s of airtime. The
        // undamped window is the POSE clock, which free-runs from contact to KickRecoverEnd 2.10 at
        // CrosserWindupTime 0.45 = 0.495 s, and the idle damper is gated on _kickT < 0. So most of the
        // travel is him sliding on frictionless feet while still posing, not the airborne part.
        public Vector3 PlantAt(Vector3 spot)
        {
            spot.y = 0f;
            // Face where he is actually crossing TO (the target), not always the goal. The launch
            // is solved origin->target, so if the crosser stands downfield of the target this makes
            // his body + swing turn to the target (a cutback looks deliberate, not a backward boot).
            // Falls back to facing the goal only if there is no target set.
            Vector3 aim = TargetOverride ?? SimConfig.ServeTarget;
            Vector3 toAim = aim - spot; toAim.y = 0f;
            if (toAim.sqrMagnitude < 0.0001f) { toAim = SimConfig.GoalCenter - spot; toAim.y = 0f; }
            if (toAim.sqrMagnitude < 0.0001f) toAim = Vector3.forward;
            Vector3 aimDir = toAim.normalized;
            // Also fixes a second symptom of the same gap: _plantFacing defaults to identity, and
            // ApplyKickPose writes FacingRotation = _plantFacing * yaw, so before any plant the FIRST
            // swing snapped him from facing the goal round to facing world +Z.
            _plantFacing = Quaternion.LookRotation(aimDir, Vector3.up);
            _plantHome = spot;
            // ResetTo is a generic "stand up fresh" and hands the body back to the walk controller
            // (LocomotionEnabled = true, MoveInput zero). Left as-is deliberately: that is the exact
            // state a manual cross-map placement has always produced, and its zero-input damping is
            // the second thing that stops the hop drift. Do not "tidy" it back to false without
            // re-testing the wing for skating.
            if (_ragdoll == null) _pendingPlant = spot;      // Init will apply it
            else if (Cosmetic) { _ragdoll.ResetTo(spot, _plantFacing); _pendingPlant = null; }
            return aimDir;
        }

        // PlantAt, plus move the launch origin to just ahead of the planted spot. ONLY for callers that
        // own the origin - the cross map, which is where this behaviour has always lived. Note it moves
        // the ball's rest point 1.6 m from the default wing launch point and shortens the default cross
        // flight from 1.294 s to 1.190 s, so a mode that just wants the body planted must call PlantAt.
        public void SetOrigin(Vector3 spot)
        {
            spot.y = 0f;
            Vector3 aimDir = PlantAt(spot);
            // Launch from ~ball height, pushed WELL forward toward the aim so the ball rests clearly
            // ahead of the crosser, never inside his legs. Combined with IgnoreBody (set at build) the
            // crosser can never deflect the ball, so every delivery solves a clean arc to the target.
            OriginOverride = spot + aimDir * 1.4f + Vector3.up * 0.4f;
        }

        public void Arm(float firstDelay)
        {
            // Auto mode counts down to the next serve; manual mode stays idle until ServeNow.
            _timer = AutoServe ? firstDelay : float.PositiveInfinity;
            _telegraphed = false;
            _manualPending = false;
            _kickT = -1f;
            JustServed = false;
            if (_reticle != null) _reticle.Hide();
            _ball.ResetTo(Origin);
        }

        /// <summary>Park the crosser fully idle: no pending serve, no telegraph, reticle hidden.
        /// Used when the crosser slot is empty and the host disabled AI fill (it never serves
        /// and is never ticked, so it just stands on the wing).</summary>
        public void Idle()
        {
            AutoServe = false;
            _timer = float.PositiveInfinity;
            _telegraphed = false;
            _manualPending = false;
            _kickT = -1f;
            JustServed = false;
            if (_reticle != null) _reticle.Hide();
        }

        /// <summary>Advance the serve timer and self-loop: winds up + swings the leg, fires
        /// a perfect cross at contact, then re-arms ServeInterval later. Returns true on the
        /// frame the ball launches.</summary>
        public bool Tick()
        {
            JustServed = false;

            // ~windup before launch: pick the target, show the telegraph, start the swing.
            // AutoServe picks a default serve; a manual serve (ServeNow) has already set the
            // pending target/time and only needs the swing to play out.
            if (!_telegraphed && _timer <= SimConfig.CrosserWindupTime)
            {
                if (AutoServe && !_manualPending) PickServe();
                if (_reticle != null) _reticle.Show(_pendingTarget);
                _telegraphed = true;
            }

            // Drive the swing clock. Up to contact it is read off the serve timer, so it always lands
            // exactly on 1 as the ball goes; after contact it free-runs at the same rate through the
            // follow-through and the rebalance. Skipped for a mobile human crosser (its Striker owns
            // the pose).
            if (_telegraphed && _timer > 0f)
                _kickT = 1f - Mathf.Clamp01(_timer / SimConfig.CrosserWindupTime);   // 0 -> 1 at contact
            else if (_kickT >= 1f && !KickSwing.Finished(_kickT))
                _kickT += Time.deltaTime / SimConfig.CrosserWindupTime;
            if (Cosmetic) ApplyKickPose();

            _timer -= Time.deltaTime;
            if (_timer <= 0f && _telegraphed)
            {
                Launch();
                // Auto mode re-arms the constant loop; manual mode goes idle until ServeNow.
                _timer = AutoServe ? SimConfig.ServeInterval : float.PositiveInfinity;
                _telegraphed = false;
                _manualPending = false;
                return true;
            }
            return false;
        }

        // Manual serve to a chosen target: a driven (low, flat) or chipped (high, floaty) ball,
        // its flight time scaled by powerMul (a hold-charge 0..1 floats it more), with optional
        // aim scatter (deg) so low-passing/low-crossing players misplace it. Used by the human
        // crosser and by the striker's call-for-pass. Plays the same windup swing.
        public void ServeNow(Vector3 target, bool lofted, float powerMul, float scatterDeg = 0f)
        {
            float baseTime = lofted ? SimConfig.CrossTimeLoft : SimConfig.CrossTimeDrive;
            float floatMul = Mathf.Lerp(SimConfig.CrossChargeFlatMul, SimConfig.CrossChargeFloatMul,
                                        Mathf.Clamp01(powerMul));
            if (scatterDeg > 0.01f)
            {
                float ang = Random.Range(-scatterDeg, scatterDeg);
                Vector3 from = Origin; from.y = 0f;
                Vector3 flat = target; flat.y = 0f;
                Vector3 rel = flat - from;
                rel = Quaternion.AngleAxis(ang, Vector3.up) * rel;
                target = new Vector3(from.x + rel.x, target.y, from.z + rel.z);
            }
            _pendingTarget = target;
            _pendingTime = Mathf.Max(0.2f, baseTime * floatMul);
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
            _manualPending = true;
            _telegraphed = false;
            _kickT = 0f;
            _timer = SimConfig.CrosserWindupTime;   // start the windup now; launches after it
        }
        bool _manualPending;

        // True once idle (manual mode, nothing pending) so a driver knows it can ServeNow.
        public bool ReadyToServe => !_telegraphed && !_manualPending;

        // Windup, strike, follow-through and rebalance, on the player's own foot. All pose overrides,
        // cleared each frame by the ragdoll driver. The upright lock is released by the contact hop and
        // re-engages here once he is back on the turf, which is the same grace-then-relock shape
        // Striker.NormalJump uses for a jump.
        void ApplyKickPose()
        {
            if (_ragdoll == null) return;
            _ragdoll.ClearPoseOverrides();

            if (_hopGrace > 0f) _hopGrace = Mathf.Max(0f, _hopGrace - Time.deltaTime);
            if (_hopGrace <= 0f && _ragdoll.IsGrounded && !_ragdoll.UprightLock)
                _ragdoll.UprightLock = true;

            // IDLE: nail him down. Init turns locomotion off, so until the first PlantAt nothing
            // damps a velocity he acquires - the follow-through hop's drift, or a striker simply
            // running into him, left him skating across the pitch and never stopping. Killing
            // horizontal velocity every idle frame is the fix, but it is gated on _kickT < 0, and the
            // pose clock free-runs 0.495 s past contact - so ~0.6 m per serve still gets through,
            // mostly with him sliding on frictionless feet rather than airborne (only 0.194 s of that
            // is air, at this project's 2x gravity). That clears CrosserPlantDrift 0.6 on the FIRST
            // hop, so the snap below is the real backstop - and it needs _plantHome, which is why
            // every AI-crosser mode now plants at setup instead of waiting for the cross map.
            if (_kickT < 0f)
            {
                if (_ragdoll.IsGrounded)
                {
                    _ragdoll.ScaleHorizontalVelocity(0f);
                    if (_plantHome.HasValue && _ragdoll.Pelvis != null)
                    {
                        Vector3 p = _ragdoll.Pelvis.position; p.y = 0f;
                        Vector3 h = _plantHome.Value; h.y = 0f;
                        if ((p - h).sqrMagnitude > SimConfig.CrosserPlantDrift * SimConfig.CrosserPlantDrift)
                            _ragdoll.ResetTo(_plantHome.Value, _plantFacing);
                    }
                }
                return;
            }
            // BODY YAW is the pelvis substitute (the pelvis cannot be posed - see KickSwing). He
            // addresses the ball off the delivery line and turns THROUGH it as the hip fires, which is
            // what a hip rotation looks like from outside while both feet are committed. Applied as an
            // offset to the plant facing, so it turns relative to where he was aiming.
            float yaw = KickSwing.YawOffset(_kickT, KickSwing.LocalFoot, SimConfig.CrosserWindupTime);
            _ragdoll.FacingRotation = _plantFacing * Quaternion.Euler(0f, yaw, 0f);
            KickSwing.Pose(_ragdoll, _kickT, KickSwing.LocalFoot, SimConfig.CrosserWindupTime);
            if (KickSwing.Finished(_kickT)) _kickT = -1f;   // done: back to a plain stand
        }

        void PickServe()
        {
            // Landing spot: the delivery override (aim spot / corner target) or the default
            // cross target. No curl (predictable practice).
            Vector3 target = TargetOverride ?? SimConfig.ServeTarget;
            if (GroundCross)
            {
                // GROUND: a fast, flat, low ball - land at ball height. Distance-scaled time keeps
                // it quick+flat whether the target is near or across the box.
                target.y = SimConfig.BallRadius;
                _pendingTarget = target;
                _pendingTime = CrossFlightTime(Origin, target, ground: true);
            }
            else
            {
                // AIR (default): a lofted cross. Distance-scaled time gives a consistent launch angle
                // so it arcs naturally at ANY range and still drops onto the target.
                _pendingTarget = target;
                _pendingTime = CrossFlightTime(Origin, target, ground: false);
            }
            _pendingCurl = Vector3.zero;
            _pendingSpin = 0f;
        }

        // Time of flight scaled by the horizontal origin->target distance so the launch ANGLE is
        // roughly constant at any range (a near and a far cross arc the same shape). LaunchTo solves
        // ballistically for this t, so the ball lands EXACTLY on target regardless of the value.
        static float CrossFlightTime(Vector3 origin, Vector3 target, bool ground)
        {
            Vector3 d = target - origin; d.y = 0f;
            float dist = d.magnitude;
            float k = ground ? SimConfig.CrossArcKGround : SimConfig.CrossArcKAir;
            return Mathf.Clamp(k * Mathf.Sqrt(dist), SimConfig.CrossArcMinTime, SimConfig.CrossArcMaxTime);
        }

        // A mobile human crosser (ServeFromFeet) launches from its own pelvis position; else the
        // OriginOverride (placed AI spot) or the fixed wing launch point.
        Vector3 Origin
        {
            get
            {
                if (ServeFromFeet && _ragdoll != null && _ragdoll.Pelvis != null)
                {
                    var p = _ragdoll.Pelvis.position; p.y = 0.4f; return p;
                }
                return OriginOverride ?? _launchPoint.position;
            }
        }

        void Launch()
        {
            _ball.ResetTo(Origin);
            _ball.LaunchTo(_pendingTarget, _pendingTime, _pendingCurl, _pendingSpin);
            if (_reticle != null) _reticle.Hide();
            // Contact: the clock is at 1 and free-runs from here through the follow-through, and the
            // strike lifts him off the plant leg. A mobile human crosser is skipped - its Striker owns
            // the body, and popping it mid-stride would fight its locomotion.
            _kickT = 1f;
            if (Cosmetic && _ragdoll != null)
            {
                KickSwing.Hop(_ragdoll, _pendingTarget - _ragdoll.Pelvis.position, KickSwing.LocalFoot);
                _hopGrace = SimConfig.KickHopGrace;
            }
            JustServed = true;
        }
    }
}
