using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Central tuning + shared constants for the whole prototype.
    /// Everything that another script might want to reference (layer names,
    /// physics numbers, arena dimensions) lives here so there is a single
    /// source of truth. No scene wiring needed: the bootstrap reads this.
    /// </summary>
    public static class SimConfig
    {
        // No custom layers/tags: layers can't be created reliably at runtime, so
        // the sim uses Physics.IgnoreCollision for ragdoll self-collision and
        // component lookups (BallController, Goal) instead of tags/layermasks.

        // ---- Arena dimensions (metres) ----
        // The playable box is a training-ground slice, not a full pitch.
        public const float FieldLength = 34f;   // along Z, toward goal
        public const float FieldWidth  = 24f;   // along X
        // Goal size is set from the pre-match screen, so these are mutable (not const).
        public static float GoalWidth  = 7.32f;  // regulation-ish
        public static float GoalHeight = 2.44f;
        public static float GoalDepth  = 3.0f;    // deeper goal box

        // ---- Net (position-based-dynamics cloth) ----
        public const int NetCols = 24;             // grid resolution across the width
        public const int NetRows = 12;             // grid resolution down the height
        public const int NetConstraintIters = 2;   // PBD distance-constraint passes/frame (fewer = looser, stretchier)
        public const float NetReturn = 0.45f;      // slow drift back to rest -> pocket lingers (looser feel)
        public const float NetDamping = 0.99f;     // velocity retained per step (0..1); higher = flowier/looser
        public const float NetMaxStretch = 4.8f;   // max node displacement from rest (m); deeper billow
        // Ball push field: nodes within this of the ball centre get shoved to its
        // surface. Must exceed the gap the backstop leaves (~one ball radius) or the
        // net never billows. Bigger = wider, deeper pocket.
        public const float NetBallReach = 0.85f;
        public const float PenaltyBoxDepth = 16.5f;
        public const float PenaltyBoxWidth = 20f; // slightly narrower than field

        // Goal sits at +Z end. Crosser starts near a wing at -Z / +X corner.
        public static readonly Vector3 GoalCenter    = new Vector3(0f, 0f, FieldLength * 0.5f);
        // The goal the human striker is AIMING AT. Aim assist, dribble shots, and the auto
        // ball-cam steer toward this. Defaults to the training goal; scrimmage repoints it
        // to the actual attacked goal (at the pitch half-length) so shots aren't aimed short.
        public static Vector3 AttackGoalCenter = new Vector3(0f, 0f, FieldLength * 0.5f);
        public static readonly Vector3 CrosserStart   = new Vector3(9.5f, 0f, FieldLength * 0.5f - 5.5f);
        public static readonly Vector3 StrikerStart   = new Vector3(-1.5f, 0f, FieldLength * 0.5f - 8.5f);
        public static readonly Vector3 KeeperStart    = new Vector3(0f, 0f, FieldLength * 0.5f - 0.6f);
        public static readonly Vector3 ReticleStart   = new Vector3(0f, 0.02f, FieldLength * 0.5f - 8.5f);

        // ---- Goalkeeper (player-controlled keeper mode) ----
        // Keeper stands on the line facing OUT toward the pitch (-Z).
        public static readonly Vector3 KeeperFaceDir = new Vector3(0f, 0f, -1f);
        public static float KeeperStrafeSpeed = 5.5f;  // A/D strafe + W/S move speed (pre-match slider)
        public const float KeeperStrafeXLimit = 4.2f;  // how far off centre he can shuffle

        // Keeper look cone: the camera pans within this yaw and the body turns to match,
        // so he faces where the mouse points within a limited cone.
        public const float KeeperLookYawLimit = 40f;   // max deg left/right of straight-forward

        // Keeper run gait: alternating steps while moving on his line (body glides).
        public const float KeeperShuffleRate = 13f;    // step cadence
        public const float KeeperShuffleLift = 55f;    // thigh lift per step (pronounced foot pickup)
        public const float KeeperShuffleKnee = 120f;   // knee fold on the lifted leg

        // LMB/RMB reflex save: one-time sideways lunge, arm+leg out. He STAYS DOWN in
        // the save pose for as long as the button(s) are held, then gets up on release.
        public const float KeeperSaveLunge = 7f;
        public const float KeeperSaveReleaseTime = 0.12f; // brief settle after release before standing

        // Upward dive (A/D + Space): reach/height scale with prior speed. More hang time
        // so there is a real apex where he is laid out flat.
        public const float KeeperDiveHorizBase = 3.58f;  // horizontal reach at standstill (~80% of full-speed reach)
        public const float KeeperDiveHorizPerV = 0.163f; // extra horizontal per m/s of prior speed
        public const float KeeperDiveUpBase = 3.98f;     // upward pop at standstill (~80% of full-speed height)
        public const float KeeperDiveUpPerV = 0.181f;    // extra height per m/s of prior speed

        // Double-tap A/D: explosive low sideways dive; legs leave the ground as he lays out.
        public const float KeeperDashDive = 7.7f;        // horizontal speed of the low dash dive
        public const float KeeperDashUp = 2.2f;          // lift so his legs come off the ground
        public const float KeeperDoubleTapWindow = 0.3f; // seconds between taps to count as a double-tap

        // Dive lay-out: the pelvis is actively driven to a rolled (horizontal) target and
        // HELD there, so he reliably reaches that tilt by the apex regardless of airtime.
        // 90 = fully parallel to the ground.
        public const float KeeperDiveLayoutLow = 84f;    // low dash dive: nearly flat
        public const float KeeperDiveLayoutHigh = 90f;   // high dive: fully parallel at the apex
        public const float KeeperDiveRoll = 90f;         // strong initial roll kick -> lays out near-instantly
        public const float KeeperDiveLeadKnee = 130f;    // leading leg folds up hard
        public const float KeeperDiveBackKnee = 95f;     // back leg also bends a good amount
        public const float KeeperDiveArmSwing = 55f;     // high dive: top arm swings over toward the dive to close the arm gap
        public const float KeeperDiveMinAir = 0.25f;     // min airborne time before we check for landing
        public const float KeeperDiveSettle = 0.25f;     // time on the ground after landing before getting up
        public const float KeeperDiveMaxTime = 2.5f;     // hard safety cap so a dive can never get stuck
        public static float KeeperJumpVel = 6.5f;        // straight-up jump (Space); pre-match slider
        public const float KeeperJumpVelBase = 6.5f;     // 1.0x reference for jump/dive-height scaling
        // Keeper camera slight mouse look (clamped, stays a behind-view). Yaw is carried
        // by the keeper's body facing now, so the camera only pitches.
        public const float KeeperCamLookPitch = 12f;      // max deg up/down
        public const float KeeperCamLookSpeed = 0.06f;    // deg per mouse-delta unit

        // ---- Physics ----
        public const float Gravity = -19.6f;      // 2x real gravity: snappier, arcade feel
        public const float BallMass = 0.43f;
        public const float BallRadius = 0.22f;
        public const float BallDrag = 0.02f;        // lower -> keeps pace, rolls further
        public const float BallAngularDrag = 0.02f;
        public const float BallBounciness = 0.55f;

        // ---- Cross tuning ----
        public const float CrossTimeLoft = 1.55f;  // loftier serve: floaty, slow (chipped)
        public const float CrossTimeDrive = 0.95f; // driven serve: faster, flatter (low)
        public const float MaxCurlAccel = 8f;      // lateral accel while airborne
        // Human crosser charge: hold time (0..CrossMaxCharge s) scales flight time between a
        // hard/flat serve and the type's nominal float, so a longer hold floats it more.
        public const float CrossMaxCharge = 0.6f;  // seconds of hold for max floatiness
        public const float CrossTapMaxHold = 0.18f; // held below this = a tap (driven); above = a chip
        public const float CrossChargeFlatMul = 0.8f;  // bare tap: 0.8x the type's flight time (flatter/faster)
        public const float CrossChargeFloatMul = 1.5f;  // full hold: 1.5x (much floatier/higher at max charge)

        // ---- Crosser (ragdoll leg-swing before a perfect launch) ----
        // He plants, plays a right-leg swing, and the ball leaves at contact - but the
        // launch is still solved perfectly by code every time (the swing is cosmetic).
        public const float CrosserWindupTime = 0.45f; // time from telegraph->contact the leg swings through
        public const float CrosserSwingThigh = 95f;   // deg the kicking thigh swings through (back then through)
        public const float CrosserSwingCalf = 70f;    // deg the knee extends on follow-through
        public const float CrosserPlantLean = 12f;    // deg torso lean into the kick

        // ---- AI goalkeeper (striker mode): a ragdoll that shuffles + dives ----
        public const float AiKeeperReactZ = 14f;      // ball within this Z of goal -> keeper reacts
        public const float AiKeeperDiveThresh = 1.6f; // |x| offset beyond which he dives instead of shuffling
        public const float AiKeeperDiveLead = 0.9f;   // predicted ball-x lead time for the dive commit (s)
        public const float AiKeeperDiveCooldown = 1.1f; // min seconds between dives
        public const float AiKeeperDiveHoriz = 6.5f;  // dive lunge speed (scaled by ability)
        public const float AiKeeperDiveUp = 3.0f;     // dive upward pop (scaled by ability)
        // Low / grounded shots. By how far the ball is off the keeper (predicted x minus
        // his x):
        //   within AiKeeperSplayReach   -> Split (central) / SaveLeft-Right splay in place;
        //   within AiKeeperLowDiveReach -> a LOW dive (down + across to a bottom corner);
        //   beyond that                 -> shuffle a step or two toward it first, then dive.
        public const float AiKeeperLowBallHeight = 1.0f;  // predicted ball height below this = low save
        public const float AiKeeperSplitWidth = 1.2f;     // |ball x - centre| under this = Split, else side splay
        public const float AiKeeperLowSaveUp = 1.2f;      // small hop on a side splay (stays low)
        public const float AiKeeperSplayReach = 1.6f;     // low ball within this of the keeper = splay/split in place
        public const float AiKeeperLowDiveReach = 4.5f;   // low ball within this = commit a low dive; beyond = step closer first
        public const float AiKeeperLowDiveUp = 1.6f;      // small upward pop on a low dive (stays low)

        // ---- Challenge modes (set from their pre-match screens) ----
        // Time Trial: round length in seconds.
        public static float TimeTrialSeconds = 60f;
        // Accuracy: round length and how many targets are up at once.
        public static float AccuracySeconds = 90f;
        public static int   AccuracyTargetCount = 4;
        // Free Kick / Penalty: where the dead ball sits and the defensive wall setup.
        public static float FreeKickDistance = 20f;    // metres out from goal for a free kick
        public static bool  PenaltyMode = false;        // true = penalty spot, no wall
        public static int   WallCount = 4;              // defenders in the wall
        public static float WallDistance = 9.15f;       // wall distance from the ball (regulation)
        public static float WallLateralOffset = 0f;     // shift the wall along the goal-parallel axis

        // ---- Auto serve ----
        public const float ServeFirstDelay = 1.6f; // before the first cross
        // Seconds between crosses (striker mode) - set from the pre-match screen.
        public static float ServeInterval = 3.5f;
        // Keeper mode: fixed continuous 2s cadence, and a snappy resolve so callouts
        // don't hold up the next ball.
        public const float KeeperServeInterval = 2f;
        public const float KeeperResolveTime = 0.4f;

        // ---- Pre-match match settings (set from PrematchUI) ----
        // Striker mode: how good the AI keeper is (0 = does nothing, 1 = very active).
        public static float KeeperAbility = 0.5f;
        // Keeper mode: how hard the served shots are (0 = easy/slow, 1 = fast/tight).
        public static float ShotDifficulty = 0.5f;
        // Global multiplier on launched ball speed (crosses + shots). Pre-match slider.
        public static float BallSpeedMul = 1.0f;
        // Testing: leave the striker wherever it is between serves (no teleport back to
        // start). Set true to restore per-serve repositioning.
        public const bool ResetStrikerOnServe = false;
        public const float ServeTime = 1.25f;       // fixed time of flight (legacy default)
        // AI/auto crosser delivery. Crosses LOFT through the air by default (a high arc that
        // drops onto the target); GROUND is a fast, flat, low ball (only when toggled in the
        // cross map's Crosser tab). Longer flight time to the same target = higher arc.
        public const float CrossServeAirTime = 1.9f;      // lofted arc, clearly airborne
        public const float CrossServeGroundTime = 0.7f;   // driven low + fast
        // Default landing spot (same every serve): centred, a bit off the goal line.
        public static readonly Vector3 ServeTarget =
            new Vector3(0f, 0.25f, GoalCenter.z - 8f);

        // ---- Freeplay delivery (set from the Freeplay pre-match screen) ----
        // How the ball comes to the player in freeplay.
        public enum Delivery { AutoCross, CornerLeft, CornerRight, AimSpot, BallAtFeet }
        public static Delivery FreeplayDelivery = Delivery.AutoCross;
        // Where an AimSpot cross lands (X across the mouth, Z off the line). Set by the
        // clickable penalty-box map. Defaults to the standard cross target.
        public static Vector3 FreeplayAimTarget = new Vector3(0f, 0.25f, GoalCenter.z - 8f);
        // Where a ball-at-feet spawns and respawns (in front of the striker's start).
        public static readonly Vector3 BallAtFeetSpot =
            new Vector3(0f, BallRadius, GoalCenter.z - 10f);

        // ---- Camera (mouse orbit / ball lock) ----
        public const float CamYawSpeed = 0.42f;    // deg per mouse-delta unit
        public const float CamPitchSpeed = 0.28f;
        public const float CamPitchMin = -6f;
        public const float CamPitchMax = 68f;
        public const float CamDistance = 6.2f;
        public const float CamLookHeight = 1.25f;

        // ---- Ragdoll drive ----
        public const float JointSpring = 6500f;     // snappier: limbs reach the pose fast
        public const float JointDamper = 150f;      // a touch more damping -> smoother, less jitter
        public const float JointMaxForce = 60000f;  // finite, but strong enough for quick swings

        // ---- Air flip control (mouse wheel, airborne only) ----
        // Scroll moves a TARGET lean angle about his right axis, clamped to +/-90 (parallel
        // with the ground). The whole body is spun toward that target and stops there, so
        // scrolling past parallel does nothing - no runaway spin. Scroll to lie flat for a
        // bicycle kick.
        public const float ScrollDeadzone = 0.0001f;     // ignore only true zero/noise
        public const float AirPitchStep = 30f;           // deg the target lean moves per scroll event (by sign)
        public const float AirPitchLimit = 115f;         // target clamp: 90 = parallel; a bit past horizontal (not a full 180 flip)
        public const float AirPitchGain = 8f;            // how hard he spins toward the target (1/s)
        public const float AirPitchMaxSpeed = 500f;      // cap on the spin speed toward the target (deg/s)
        // Acrobat capstone: the air-pitch target clamp opens to this so scrolling drives the body
        // past parallel and all the way around into full 360 forward/backward flips (chained up to
        // this many deg of headroom each way). Only used when PlayerProfile.PerkAcrobat is owned.
        public const float AcrobatFlipLimit = 720f;

        // ---- Dive header (hold Space while moving forward) ----
        // Carried run momentum is zeroed, then a modest up + forward launch tips him into
        // a belly-down header; gravity arcs him into the flop. Kept small so he doesn't
        // travel far. Pelvis yaw+roll pinned so the chest stays square.
        public const float DiveHoldTime = 0.28f;      // hold Space (moving fwd) this long -> dive; below = tap-jump
        public const float DiveUpVel = 2f;            // small upward pop (mostly horizontal dive)
        public const float DiveForwardVel = 10f;      // forward launch reach (dominant component)
        public const float DiveLayoutPitch = 90f;     // target forward pitch (deg); 90 = belly-down
        public const float DivePitchGain = 10f;       // how hard the pelvis is driven to that pitch
        public const float DiveDriveScale = 0.15f;    // limp body during the dive (spine won't hold upright)
        public const float DiveProneTime = 1.5f;      // base time prone after a dive/flop lands (Agility recovery nodes cut this)
        public const float DiveProneMinTime = 0.55f;  // floor: recovery upgrades can't drop below this
        public const float AcrobatRecoveryMul = 1.4f; // Acrobat capstone: extra divisor on prone recovery time
        public const float BalanceFrequency = 3.2f;
        public const float BalanceDamping = 0.85f;

        // ---- Striker locomotion ----
        public static float StrikerMoveSpeed = 4.8f;   // pre-match slider
        public const float StrikerSprintMul = 1.8f;  // Shift-held speed multiplier
        public const float StrikerAccel = 22f;      // applied to every bone (whole-body translation)
        public const float JumpVelocity = 8.0f;     // m/s upward added to the whole body on a standing jump
        public const float RunJumpMul = 1.0f;        // running jumps now go full height (more vertical pop)
        public const float SprintJumpMul = 0.85f;    // sprinting jumps a touch lower than that
        public const float RunJumpForwardKeep = 0.5f; // fraction of run momentum kept on a moving jump (toned down)
        public const float BicycleBackSpin = 14f;   // angular impulse for backward rotation

        // ---- Run cycle (procedural gait) ----
        // The body GLIDES via velocity; the limbs are cosmetic pose overrides. Keep the
        // fore/aft thigh swing modest (too much reads as skating over the glide) but pick
        // the feet up high with a hard knee fold so it looks like a smooth high-knee run.
        public const float StrideRateMax = 9f;      // gait phase speed (rad/s) at full run
        public const float GaitThighSwing = 32f;    // deg the thighs swing fore/aft (small -> no scissor/skate)
        public const float GaitThighLift = 70f;     // deg the swing leg lifts (pronounced foot pickup)
        public const float GaitKneeBend = 145f;     // deg the knee folds to pick up the foot (high knee, heel up)
        public const float GaitFootPoint = 20f;     // deg foot dorsiflex through the stride
        // Sprint gait: knees fold harder toward the body and lift higher, faster cadence.
        public const float SprintStrideMul = 1.5f;  // faster leg cadence when sprinting
        public const float SprintThighLift = 95f;   // higher knee lift when sprinting
        public const float SprintKneeBend = 160f;   // knee folds more toward the body
        public const float GaitTorsoLean = 8f;      // deg forward lean while running
        public const float LegSwingRaise = 130f;    // deg a leg raises on LMB/RMB (knee to chest)
        public const float LegRaiseMaxDeg = 90f;     // hard cap on thigh raise: 90 = straight out (max bicycle reach)
        public const float LegRaiseEase = 8f;        // how fast a leg raise / header bend eases in-out (per sec); no snap-back
        public const float BicycleLegEase = 22f;      // single airborne leg snaps up this fast (bicycle kick)
        public const float BicycleLegRaiseMul = 1.35f; // and this much higher than a normal raise
        public const float HeaderLegRaiseMul = 0.25f; // airborne header: legs come forward only minimally
        public const float HeaderTorsoBend = 90f;    // deg the torso folds forward on an airborne header (snappy, far)
        public const float HeaderBendEase = 60f;     // how fast the torso snaps forward into the header (very fast)
        public const float HeaderGrace = 0.12f;      // sec an airborne header stays live after the click (GK-split-style)
        // Arm pump (both keeper + striker): upper arms swing fore/aft opposite the legs,
        // elbows held bent. Reads as a runner's arm carriage over the glide.
        public const float ArmPumpSwing = 45f;      // deg upper arm swings fore/aft
        public const float ArmPumpElbow = 65f;      // deg the elbow stays folded

        // Moonwalk celebration: steady backward glide speed (m/s) while the shuffle pose plays.
        public const float MoonwalkGlideSpeed = 2.2f;

        // ---- Trick validation ----
        // A bicycle is a fast whole-body flip: the pelvis sweeps through the "reclined"
        // cone in a couple of frames, so reading the angle at the exact contact frame is
        // unreliable. Instead the Striker LATCHES a bicycle window the moment the player
        // commits (airborne + leaning back past the arm threshold, or scrolling the
        // air-pitch target back), and holds it open BicycleWindow seconds so contact,
        // camera, and assist all read a stable "yes". Arm loose, gate legal shots tight.
        public const float BicycleWindow = 0.85f;   // seconds the latched attempt stays "live"
        public const float BicycleArmUpness = 0.72f; // pelvis-up dot world-up below this (tipped ~44deg+) ARMS the window
        public const float BicycleArmPitch = 55f;    // OR: air-pitch target leaned past this many deg arms it
        public const float ValidHitBonus = 6.5f;     // extra ball speed on a clean trick

        // ---- Sniper (hidden 4th role, dormant scaffold) ----
        // A shooter perched high in the stadium that tries to hit the striker or the
        // ball. Off by default; flesh out via Sniper.cs. Perch is high above a corner
        // looking across the box.
        public static readonly Vector3 SniperPerch =
            new Vector3(FieldWidth * 0.5f + 6f, 20f, GoalCenter.z - 4f);
        public const float SniperFireInterval = 2.5f;  // seconds between shots
        public const float SniperAimTime = 0.9f;        // lead-in aim time before a shot
        public const float SniperRange = 120f;          // hitscan range
        public const float SniperLead = 0.15f;          // how much to lead a moving target (s)

        // ---- Arcade aim assist (on striker contact) ----
        // Subtle, brief curve that biases a struck ball toward the goal so more shots
        // are on target, without removing the challenge. Kept small on purpose.
        // Base aim assist is LOW so an untrained player is inaccurate; Shooting/Control
        // skill nodes raise it noticeably (ShotAccuracyMul stacks on top).
        public const float AssistSteerFrac = 0.16f;  // base goal-ward bend (0..1); low by default
        public const float AssistDuration = 0.45f;   // seconds the curve is applied after contact
        public const float AssistMinSpeed = 3.5f;    // only assist shots hit with some pace
        public const float AssistMaxAccel = 18f;     // cap on the sideways/steer accel
        // Aim assist only kicks in when the striker is actually FACING the opponents' goal.
        // Facing dir . dir-to-goal must exceed this cosine (0.34 ~= within a ~70deg half
        // cone). Turned side-on or facing his own goal -> no assist at all.
        public const float AssistFacingDot = 0.34f;

        // ---- Auto ball-cam on a shot ----
        // After a genuine SHOT (not a trap/dead touch), the camera snaps to ball-cam for
        // this long so the player can watch it fly on or off target, then reverts.
        public const float ShotCamSeconds = 2f;
        // A contact only counts as a "shot" (worth cutting to ball-cam) if the ball leaves
        // with at least this much horizontal pace toward the goal.
        public const float ShotCamMinSpeed = 8f;
        // The auto ball-cam now ONLY cuts for a shot taken FACING AWAY from the opponents'
        // goal - the bicycle / over-shoulder shots the striker can't watch himself. When he's
        // facing the goal (dead-ahead in the cone OR merely side-on) he can already see it, so
        // no cam. A shot counts as "facing away" when (facing . dir-to-goal) is below this
        // cosine. -0.2 ~= turned more than ~100deg off goal (clearly over the shoulder / behind).
        // Bicycles always qualify regardless (their latched trick state forces the cut).
        public const float ShotCamFaceAwayDot = -0.2f;

        // ---- Strike power (on striker contact) ----
        // Base power is modest by default; Shooting nodes + body traits multiply it up.
        public const float StrikeHorizBoost = 1.25f; // multiply horizontal velocity when struck (low base)
        public const float StrikeHorizMax = 26f;     // cap on resulting horizontal speed (m/s)

        // ---- Body-part accuracy + power (which part of the body struck the ball) ----
        // Accuracy = the fraction of AssistSteerFrac applied (how much the shot is helped
        // toward goal). Strong foot/leg is the reference (full); weak side is half; a body
        // (torso/pelvis) touch is scrappy and inaccurate. Head is handled by heading rules.
        public const float ArmHitboxScale = 1.9f;    // arm collider radius vs the thin visible arm (stops ball phasing)
        public const float LegHitboxScale = 1.6f;    // keeper/striker leg collider radius vs the visible leg
        // The keeper multiplies its arm/leg/foot/glove hitboxes by this on top of the base
        // scales, so every limb is chunkier than the visible body part and saves connect off
        // any part of an arm, leg, hand, or foot. Striker keeps the base scales (boost = 1).
        public const float KeeperHitboxBoost = 1.6f;
        // Extra reach on the keeper's GLOVES (hands) on top of KeeperHitboxBoost, so a dive
        // connects on a near-miss for more dramatic saves. 1.35 * 1.6 base -> a big catch radius.
        public const float KeeperGloveReach = 1.35f;
        public const float StrongFootAccuracy = 1.0f;
        public const float WeakFootAccuracy   = 0.3f;    // weak leg/foot: much less accurate
        public const float WeakFootPowerMul   = 0.6f;    // and weaker
        public const float BodyAccuracy       = 0.1f;    // body/arms: basically no aim help
        public const float BodyPowerMul       = 0.25f;   // body/arms: super weak - traps the ball, drops it

        // ---- Set pieces (free kick / penalty) + volleys: scripted power, stat-scaled ----
        // A set-piece strike IGNORES the foot's swing speed - the ball is dead and the strike is
        // scripted, so any clean contact leaves the boot high, fast, and goalward. WHERE on the
        // ball it is struck picks the spin/bend; Shooting POWER scales the launch speed + bend;
        // Shooting ACCURACY (+ power) scales the goal-steer. Values are tuned aggressive on
        // purpose - the old set pieces were far too weak even at max stats.
        public const float SetPieceBaseSpeed   = 22f;    // goalward launch speed floor at power 1.0 (m/s)
        public const float SetPieceMaxSpeed    = 42f;    // hard cap on a set-piece launch (x Cannon ceiling)
        public const float SetPieceLoft        = 0.32f;  // up-velocity as a fraction of launch speed (gentle; the vy cap + ballistic solve own the real height)
        public const float SetPieceCurl        = 12.0f;  // base curl/bend accel (x ShotPowerMul) - pronounced
        public const float SetPieceAssistFloor = 0.08f;  // goal-steer with NO shooting investment (near zero)
        public const float SetPieceAssistMax   = 1.6f;   // goal-steer fully invested in Shooting power + accuracy (drastic)
        // ---- Guided placement (accuracy + strike location drive the shot, NOT power) ----
        // The set-piece launch blends toward a ballistic solve that REACHES a 3D goal corner,
        // by the skill-only combined stat; a hard vy cap keeps every shot near goal height so a
        // miss never skyrockets. See BallController set-piece launch block.
        public const float SetPieceCornerInset  = 0.35f; // how far inside the post/bar the corner aim sits (m)
        public const float SetPieceLowStrike     = 0.20f; // struck-height dot at/below this -> aim the TOP corner
        public const float SetPieceFlightTime    = 0.72f; // nominal flight time for the ballistic corner solve (s)
        public const float SetPieceApexMargin    = 0.55f; // most the launch apex may clear the crossbar by (m) - the hard vertical ceiling
        public const float AssistVertFrac        = 0.55f; // vertical goal-steer strength (x _accuracyMul), capped by AssistMaxAccel

        // ---- Set-piece TAKER: AI aesthetic runup + Space power meter + WASD spin ----
        // The striker's runup + swing are purely cosmetic (AI-driven). The player controls ONLY
        // an oscillating power meter (Space) and WASD spin (held silently). Release commits, runs
        // the runup, and fires a scripted LaunchSetPiece. Overcharging power or over-holding spin
        // botches the shot. See Play/SetPieceTaker.cs.
        public const float SetPieceMeterRate     = 1.6f;  // power-meter ping-pong speed (full sweeps/sec-ish)
        public const float SetPieceOverchargeTime = 0.45f; // seconds pegged at max power before it OVERCHARGES (botch); Accuracy widens this
        public const float SetPieceSpinChargeRate = 1.1f;  // WASD spin charge build rate (per second held, 0..1 then over)
        public const float SetPieceSpinOverTime  = 1.35f; // seconds holding a spin dir past full before it BOTCHES; Accuracy widens this
        public const float SetPieceBotchScatterX = 3.2f;  // max horizontal target scatter (m) at full botch
        public const float SetPieceBotchScatterY = 1.1f;  // max vertical target scatter (m) at full botch (still capped by apex)
        public const float SetPieceCornerPull    = 0.85f; // how far combined pulls the aim from centre toward a corner (0..1 of the half-goal)
        // Power STAT scales LESS than accuracy in this mode: it only nudges the launch-SPEED
        // ceiling the on-screen bar can reach (never height). At the minimum power stat the bar
        // tops out at this fraction of the base->max speed range; at full stat, the full range.
        // A small spread by design, so the power stat "scales up less" than the accuracy stat
        // (which drives placement, swerve, corner pull and the assist window).
        public const float SetPiecePowerStatFloor = 0.82f;
        // OVERPOWERING the on-screen power BAR (overcharge, not the power stat) lofts the ball
        // OVER the crossbar: this much extra UPWARD velocity (m/s) is added at full overcharge,
        // ON TOP of the clean apex cap and INDEPENDENT of the power stat/bar. So a clean max-power
        // shot still stays under the bar, but any overpowered bar sails over - a weak-power
        // striker clears it too, just with less forward pace ("over the bar, just slower").
        public const float SetPieceOverchargeVy  = 7.0f;
        // Swerve is driven PRIMARILY by the accuracy stat: curl magnitude scales from this floor
        // (a raw striker still bends a little) up to full at max accuracy. The WASD spin hold only
        // modulates within that band, so a low-accuracy striker can't buy a big banana with WASD.
        public const float SetPieceCurlAccFloor  = 0.25f;
        public const float SetPieceRunupSpeed    = 5.5f;  // run-in speed (m/s); matches a brisk approach (the driver places the taker ~3m back)
        public const float SetPiecePlantOffset   = 0.55f; // stops the run-in this far short of the ball (plant beside it)
        public const float SetPieceSwingTime     = 0.22f; // seconds of cosmetic leg swing after the plant before contact
        public const float SetPieceSettleTime    = 0.8f;  // taker Settle hold after the strike before it goes Idle
        // Spin is chosen by WHERE the ball is struck (contact point in the shot frame):
        public const float SetPieceSideThresh  = 0.30f;  // |side dot| beyond this -> side spin (bends the SAME way struck)
        public const float SetPieceTopThresh   = 0.45f;  // struck-height dot above this -> top spin (dips)
        public const float SetPieceTopSpinMul  = 0.8f;   // downward-curl strength for top spin (x base curl)
        public const float SetPieceKnuckleVert = -0.10f; // struck at/below this height = the chip / knuckle zone
        public const float SetPieceKnuckleChance = 0.20f; // 20% base chance a bottom strike knuckles instead of chipping (rises linearly w/ power)
        public const float SetPieceKnucklePaceMul = 1.15f; // a knuckle drives flatter + faster than a chip
        public const float SetPieceKnuckleMul  = 0.9f;   // wobble strength of a knuckle (x base curl, scales LINEARLY with power) - pronounced
        public const float SetPieceChipLoft    = 0.95f;  // a bottom-strike chip scoops up high (up-vel fraction of launch)
        public const float SetPieceChipPaceMul = 0.65f;  // ...with softer forward pace than a driven shot

        // ---- Kick vs. run-into: only a SWINGING leg imparts real power ----
        // The struck bone's own speed decides how live the touch is. A kick swings the
        // foot/leg fast; just running into the ball translates the whole body at move
        // speed. Below the floor speed the touch barely nudges the ball (a trap/dribble);
        // above the full speed it strikes at full power; it lerps between.
        public const float KickSpeedFloor = 4f;      // bone speed (m/s) below this = a dead touch
        public const float KickSpeedFull  = 9f;      // bone speed at/above this = a full strike
        public const float DeadTouchPower = 0.12f;   // velocity kept on a dead (non-kicking) touch

        // ---- Volley: a FLYING ball met by a SWINGING leg launches like a free kick ----
        // A ball whose centre is above this height (m) is "flying"; a swinging leg (kick > 0)
        // that hits it fires the set-piece launch (loft + contact-point curl, stat-scaled)
        // instead of trapping. The bar is set to just off the turf: a ball resting on the
        // ground has its centre at BallRadius (0.22), so BallRadius + 0.03 means the ball
        // counts as airborne the moment its underside clears ~3cm of grass. The small buffer
        // keeps a still, jittering ball from falsely registering as a volley.
        public const float VolleyMinBallHeight = BallRadius + 0.03f;   // 0.25

        // ---- Dribble (soft-magnet close control) ----
        // The ball auto-sticks to a carry point just in front of the grounded striker's
        // feet whenever it is close and slow, travelling with him arcade-style. A kick
        // (leg button or a genuinely fast leg swing) releases it as a real shot.
        //
        // Capture: ball must be within CaptureRadius of the carry point, moving slower
        // than CaptureMaxBallSpeed, with the striker grounded and not mid-trick.
        public const float DribbleCaptureRadius   = 1.4f;   // how near the carry point the ball is grabbed
        public const float DribbleCaptureMaxSpeed  = 12f;   // ball must be slower than this to be captured (m/s)
        public const float DribbleReleaseRadius    = 2.0f;  // if the ball ends up beyond this from the carry point, drop the leash
        // Carry point: sits this far in front of the feet at a walk; sprint pushes it out
        // toward the far distance (heavier touch). Height rides at the ball radius. These
        // are the LOOSE defaults (no Control); the Control trap stat pulls them in a lot
        // (see DribbleTrapTightenMax) so investing in Control gives a visibly tighter touch.
        public const float DribbleNearDistance     = 0.75f; // carry distance at a stand/walk, no Control
        public const float DribbleSprintDistance    = 1.7f;  // carry distance at full sprint, no Control
        // Follow spring: acceleration = k * offset - c * relativeVel. Higher k = stickier,
        // higher damp = settles without overshooting past the carry point.
        public const float DribbleFollowAccel      = 48f;    // spring stiffness toward the carry point (very sticky)
        public const float DribbleFollowDamp       = 14f;    // velocity damping (near-critical: no overshoot/orbit)
        public const float DribbleMaxAccel         = 160f;   // cap on the follow acceleration (m/s^2)
        // Feed-forward of the striker's velocity so the ball tracks the MOVING carry point
        // without lagging behind it. The carry point travels at the striker's speed, so the
        // ball must too; anything below 1.0 leaves a steady-state trailing offset that grows
        // with speed (why the ball used to lag behind on the run). 1.0 = the ball keeps pace
        // and sits on the carry point in front of the feet.
        public const float DribbleLeadSpeedFrac    = 1.0f;   // match striker speed: no trailing lag
        public const float DribbleSpinScale        = 2.2f;   // rolling spin visual per m/s of carry speed
        // Shot on release (kick): the carried ball is launched in the aim/facing direction.
        public const float DribbleShotSpeed        = 17f;    // base release shot speed (m/s), scaled by ShotPowerMul
        public const float DribbleShotLift         = 0.16f;  // upward fraction added so it isn't a pure ground roll
        public const float DribbleRecaptureCooldown = 0.45f; // after a shot, don't re-grab the ball for this long
        // Control trap stat tightens the touch: at full DribbleTightness (1) the carry
        // sits this fraction closer and captures from this much wider a net.
        public const float DribbleTrapTightenMax   = 0.62f;  // up to 62% closer carry with full Control (0.75->0.29 walk)
        public const float DribbleTrapCaptureBonus  = 0.6f;  // up to +0.6m capture radius with full Control

        // While carrying, the striker moves SLOWER and turns SLOWER by default; the Control
        // trap stat claws both back (a Control build dribbles nearly at full pace and turns
        // sharply, a raw build is ponderous with the ball). DribbleTightness (0..1) lerps
        // each penalty from its "no Control" value to "full Control".
        public const float DribbleMoveMulLow  = 0.62f;  // move-speed factor while dribbling, no Control
        public const float DribbleMoveMulHigh = 0.92f;  // move-speed factor while dribbling, full Control
        // Turn rate = how fast the facing yaw slews toward the mouse aim while carrying
        // (deg/sec). Low with no Control (ponderous), snappy with full Control.
        public const float DribbleTurnRateLow  = 220f;  // deg/sec facing slew while dribbling, no Control
        public const float DribbleTurnRateHigh = 620f;  // deg/sec facing slew while dribbling, full Control

        // ---- Scrimmage (full match: two goals, teams, AI, passing) ----
        // Chosen role + team size come from the pre-match screen.
        public enum ScrimRole { Outfield, Keeper }
        public static ScrimRole ScrimmageRole = ScrimRole.Outfield;
        public static int ScrimmagePerSide = 3;   // TOTAL players per side incl. keeper (3/5/11 => outfield = this-1)
        public static float ScrimmageMatchSeconds = 180f;   // match length (pre-match option); counts down to full time

        // The scrimmage pitch is its OWN square-ish field centred on origin, sized to the
        // team count, with a goal at each end (+Z and -Z) and walls all round. Independent
        // of the single-goal training arena so nothing else has to change.
        public static float ScrimHalfLength(int perSide) => perSide >= 11 ? 52f : perSide >= 5 ? 34f : 24f;
        public static float ScrimHalfWidth(int perSide)  => perSide >= 11 ? 34f : perSide >= 5 ? 22f : 16f;

        // Player (human) attacks +Z and defends -Z, matching the Striker/KeeperController
        // hardcoded facing. The team attacking +Z is "Home"; attacking -Z is "Away".
        public const float ScrimKickoffBallHeight = 0.3f;
        public const float ScrimKickoffFreeze     = 1.2f;   // ball/scoring frozen this long after kickoff/goal
        // Out-of-play safety: if the ball somehow sits nearly still against a wall for this
        // long, drop it back to a sensible in-play spot so a match can't stall.
        public const float ScrimStuckTime         = 4f;
        public const float ScrimStuckSpeed        = 0.5f;   // "nearly still" threshold (m/s)

        // Passing (controlled outfielder). A pass picks the teammate nearest the aim ray.
        public const float PassGroundSpeed   = 12f;   // ground (rolled) pass base speed (m/s), scaled by PassPowerMul
        public const float PassLoftedSpeed   = 13f;   // lofted (chipped) pass base speed
        public const float PassLoftedArc     = 0.55f; // upward fraction of a lofted pass (higher = floatier)
        // Where a launched pass spawns relative to the passer, so it clears their own body
        // instead of rising into their torso (which flattened lofted passes to the ground).
        public const float PassSpawnForward  = 0.6f;   // metres forward along the pass direction
        public const float PassSpawnLift     = 0.7f;   // extra metres up for a lofted pass
        public const float PassAimConeDot    = 0.2f;  // teammate must be within this cone of the aim to be picked
        public const float PassMaxRange      = 45f;   // don't target teammates further than this
        public const float PassLeadFrac      = 0.25f; // lead a moving target by this fraction of range/speed
        // Hold Q/E to charge: a tap is a soft pass, a full hold a hard/fast one. The charge
        // fraction (0..1 over PassMaxCharge seconds) scales speed between these bounds.
        public const float PassMaxCharge     = 0.6f;   // seconds of hold to reach full power
        public const float PassChargeMinMul  = 0.55f;  // speed factor for a bare tap
        public const float PassChargeMaxMul  = 1.6f;   // speed factor at full charge
        // Accuracy scatter: at PassAccuracyMul = 1 (no Passing nodes) a pass is knocked off
        // its intended line by up to this angle + a power wobble; investment shrinks it to
        // ~0 (Maestro perk = pinpoint). Harder-charged passes also scatter a touch more.
        public const float PassScatterMaxDeg = 22f;    // max aim error at low passing (deg)
        public const float PassPowerWobble   = 0.18f;  // +/- fraction of speed randomised at low passing

        // Auto-switch: control the teammate nearest the ball (outfield role). A manual
        // switch key cycles too. A brief lockout stops rapid flip-flopping.
        public const float SwitchLockout     = 0.6f;  // min seconds on a player before an auto-switch

        // Outfield AI.
        public const float AiOutfieldSpeed    = 5.0f;  // base run speed for AI outfielders (keeps pace with play)
        public const float AiChaseStopDist    = 0.6f;  // stop closing when this near the ball
        public const float AiShootRange       = 18f;   // shoot when this close to the target goal with the ball
        public const float AiSupportSpread    = 7f;    // how far off-ball teammates spread from the carrier
        public const float AiKickBoneImpulse  = 9f;    // forward-drive velocity an AI adds to push the ball up the pitch
        public const float AiKickCooldown     = 0.35f; // min seconds between AI touches (flow without ping-ponging)
        public const float AiSeparationRadius = 3.8f;  // AI teammates keep at least this far apart

        // Tackling / ball-winning. A tackle is a short forward lunge; if it reaches the ball
        // it dispossesses the carrier (kills their dribble) and knocks the ball loose.
        public const float TackleLunge     = 6.5f;  // forward lunge velocity of the tackler
        public const float TackleReach     = 1.6f;  // distance to the ball at which the tackle wins it
        public const float TackleCooldown  = 0.9f;  // seconds before the same player can tackle again
        public const float TackleKnock     = 4.5f;  // how hard the won ball is knocked away from the carrier
        public const float AiTackleRange    = 2.2f;  // an AI defender lunges when this close to an opponent carrier

        // Knockdowns: a tackled player (or one caught by a slide tackle) falls over, goes
        // limp for a moment, then gets back up.
        public const float KnockdownTime    = 1.4f;  // seconds down before recovering
        public const float KnockdownImpulse = 5.5f;  // shove velocity applied to the felled player
        public const float KnockdownSpin    = 6f;    // tumble spin (deg/s about a horizontal axis)
        // Slide tackle: holding BOTH legs (LMB+RMB) while moving fast into an opponent
        // fells them (and the slider). It connects within this range at this min speed.
        public const float SlideTackleRange  = 1.7f;  // contact distance to the target
        public const float SlideTackleMinSpeed = 3.5f; // must be moving at least this fast to count as a slide
        public const float SlideTackleCooldown = 1.2f;

        // ---- Post-goal replay ----
        // On-screen replay duration = ReplayWindow / ReplaySlowMul. 4s of real action played
        // at 0.36x slow-mo = ~11s on screen, all slowed. The 4s window (up from 2s) starts ~2s
        // earlier so the replay captures the PASS/CROSS + build-up, not just the shot.
        public const float ReplayWindow   = 4f;     // seconds of action buffered for the replay
        public const float ReplaySlowMul  = 0.36f;  // playback speed (0..1); 4/0.36 = ~11s watched
        // Live delay after the goal before the replay freezes + rolls. Physics keeps running
        // (and the recorder keeps buffering) during it, so the hold ends the captured 4s window
        // AFTER the ball crosses the line. At 1.3s the window is roughly [goal-2.7s .. goal+1.3s]
        // - it opens on the pass/build-up and closes on the ball settling in the net.
        public const float ReplayHold     = 1.3f;

        // ---- Networking (host-authoritative snapshot sync) ----
        public const float NetSnapshotInterval = 0.05f;  // host broadcasts ~20 snapshots/sec
        public const float NetInterpRate       = 14f;    // client puppet/ball lerp sharpness (1/s) - legacy fallback
        // Remote bodies are rendered this far in the PAST, interpolating between the two buffered
        // snapshots bracketing (now - delay). ~2 snapshot intervals so a late/dropped packet has a
        // neighbor to interpolate to instead of teleporting. The local player is predicted, not delayed.
        public const float NetInterpDelay      = 0.1f;   // 100 ms interpolation delay
        // Client server-reconciliation of the local predicted body (bounded error correction, not
        // rollback - the ragdoll isn't re-simulatable). Error below the deadzone is expected
        // prediction lag and ignored; a moderate error eases back at ReconcileRate/sec; an error
        // past ReconcileSnap is a real misprediction and is hard-snapped.
        public const float ReconcileDeadzone   = 0.35f;  // m of allowed predicted-vs-authoritative drift
        public const float ReconcileRate       = 6f;     // fraction/sec eased back for a moderate error
        public const float ReconcileSnap       = 2.5f;   // m error above which we hard-snap

        // ---- Skill-tree capstone perk magnitudes ----
        public const float CannonCapMul     = 1.5f;   // Cannon: raises the shot-speed ceiling
        public const float ImmovableMassMul = 1.6f;   // Immovable: extra effective mass (push resistance)
        public const float AfterburnerMul   = 1.15f;  // Afterburners: extra sprint speed on top
        public const float AerialPaceKeep   = 0.5f;   // Aerial: header keeps this fraction of vertical (vs HeaderVerticalKeep)
        public const float AerialGoalBias   = 0.95f;  // Aerial: header steers harder to goal (vs HeaderGoalBias)

        // ---- Headers (head contact). Low base power/accuracy; the Heading tree ramps
        //      both up noticeably (HeaderPowerMul/HeaderAccuracyMul from the profile). ----
        public const float HeaderPowerMul = 1.3f;    // extra power vs a normal strike (low base)
        public const float HeaderSwerve = 3f;        // added swerve (spin + lateral curl) - minimal by default
        public const float HeaderAccuracyMul = 1.35f; // base goal-ward steer on a header (low; Heading tree adds more)
        // A header REDIRECTS the ball onto a goal-ward horizontal line (not just faster
        // in its old direction), so even a glancing touch flies fast toward goal.
        public const float HeaderGoalBias = 0.85f;   // 0..1: how strongly it aims at goal
        public const float HeaderMinSpeed = 15f;     // floor horizontal speed off a header (m/s)
        public const float HeaderVerticalKeep = 0.35f; // fraction of incoming vertical kept (stays flat)
    }
}
