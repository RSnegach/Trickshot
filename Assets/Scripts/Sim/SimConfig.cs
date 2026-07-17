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
        public static readonly Vector3 CrosserStart   = new Vector3(9.5f, 0f, FieldLength * 0.5f - 5.5f);
        public static readonly Vector3 StrikerStart   = new Vector3(-1.5f, 0f, FieldLength * 0.5f - 8.5f);
        public static readonly Vector3 KeeperStart    = new Vector3(0f, 0f, FieldLength * 0.5f - 0.6f);
        public static readonly Vector3 ReticleStart   = new Vector3(0f, 0.02f, FieldLength * 0.5f - 8.5f);

        // ---- Goalkeeper (player-controlled keeper mode) ----
        // Keeper stands on the line facing OUT toward the pitch (-Z).
        public static readonly Vector3 KeeperFaceDir = new Vector3(0f, 0f, -1f);
        public static float KeeperStrafeSpeed = 5.5f;  // A/D strafe + W/S move speed (pre-match slider)
        public const float KeeperStrafeXLimit = 4.2f;  // how far off centre he can shuffle

        // LMB/RMB reflex save: one-time sideways lunge, arm+leg out. He STAYS DOWN in
        // the save pose for as long as the button(s) are held, then gets up on release.
        public const float KeeperSaveLunge = 7f;
        public const float KeeperSaveReleaseTime = 0.12f; // brief settle after release before standing

        // Upward dive (A/D + Space): reach/height scale with prior speed. More hang time
        // so there is a real apex where he is laid out flat.
        public const float KeeperDiveHorizBase = 2.0f;   // horizontal reach at standstill
        public const float KeeperDiveHorizPerV = 0.45f;  // extra horizontal per m/s of prior speed
        public const float KeeperDiveUpBase = 3.6f;      // upward pop at standstill (hang time for the apex)
        public const float KeeperDiveUpPerV = 0.25f;     // extra height per m/s of prior speed

        // Double-tap A/D: explosive low sideways dive; legs leave the ground as he lays out.
        public const float KeeperDashDive = 7.0f;        // horizontal speed of the low dash dive
        public const float KeeperDashUp = 2.6f;          // lift so his legs come off the ground
        public const float KeeperDoubleTapWindow = 0.3f; // seconds between taps to count as a double-tap

        // Dive lay-out: the pelvis is actively driven to a rolled (horizontal) target and
        // HELD there, so he reliably reaches that tilt by the apex regardless of airtime.
        // 90 = fully parallel to the ground.
        public const float KeeperDiveLayoutLow = 84f;    // low dash dive: nearly flat
        public const float KeeperDiveLayoutHigh = 90f;   // high dive: fully parallel at the apex
        public const float KeeperDiveRoll = 90f;         // strong initial roll kick -> lays out near-instantly
        public const float KeeperDiveLeadKnee = 130f;    // leading leg folds up hard
        public const float KeeperDiveBackKnee = 95f;     // back leg also bends a good amount
        public const float KeeperDiveMinAir = 0.25f;     // min airborne time before we check for landing
        public const float KeeperDiveSettle = 0.25f;     // time on the ground after landing before getting up
        public const float KeeperDiveMaxTime = 2.5f;     // hard safety cap so a dive can never get stuck
        public static float KeeperJumpVel = 6.5f;        // straight-up jump (Space); pre-match slider
        public const float KeeperJumpVelBase = 6.5f;     // 1.0x reference for jump/dive-height scaling
        // Keeper camera slight mouse look (clamped, stays a behind-view).
        public const float KeeperCamLookYaw = 18f;        // max deg left/right the view pans
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
        public const float CrossTimeLoft = 1.55f;  // loftier serve: floaty, slow
        public const float CrossTimeDrive = 0.95f; // driven serve: faster, flatter
        public const float MaxCurlAccel = 8f;      // lateral accel while airborne

        // ---- Auto serve ----
        public const float ServeFirstDelay = 1.6f; // before the first cross
        // Seconds between crosses (striker mode) - set from the pre-match screen.
        public static float ServeInterval = 3.5f;

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
        public const float ServeTime = 1.25f;       // fixed time of flight
        // Fixed landing spot (same every serve): centred, a bit off the goal line.
        public static readonly Vector3 ServeTarget =
            new Vector3(0f, 0.25f, GoalCenter.z - 8f);

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

        // ---- Recline (E held, airborne) ----
        // One-shot backward angular impulse to the pelvis (F-style): the jointed body
        // brakes it so he flips onto his back and stops. Bicycle pose lifts the leg.
        public const float ReclineImpulse = 15f;
        public const float ReclineProneTime = 1.2f;  // stays flat on back this long after landing

        // ---- Dive header (W + Space, forward dive) ----
        // Diving header (hold Space + W): crouch, then leap up and forward in an arc,
        // land belly-down, recover. Driven to a held horizontal orientation (like the
        // keeper dive) so it does NOT spin - the old constant spin ran away violently.
        public const float DiveHoldTime = 0.22f;      // hold Space (moving fwd) this long -> dive; shorter = tap-jump
        public const float DiveCrouchTime = 0.12f;   // brief knee-bend before launch
        public const float DiveForwardVel = 7.0f;    // forward launch speed
        public const float DiveUpVel = 5.0f;         // upward launch speed (a real arc)
        public const float DiveLayoutDeg = 45f;      // pitch forward only partway (not all the way over)
        public const float DiveProneTime = 0.9f;     // stays prone this long after landing
        public const float DiveCrouchKnee = 55f;     // knee bend during the pre-jump crouch
        // (removed DiveSpinRate: the dive now uses a held orientation target, not a spin.)
        public const float BalanceFrequency = 3.2f;
        public const float BalanceDamping = 0.85f;

        // ---- Striker locomotion ----
        public static float StrikerMoveSpeed = 4.8f;   // pre-match slider
        public const float StrikerSprintMul = 1.8f;  // Shift-held speed multiplier
        public const float StrikerAccel = 22f;      // applied to every bone (whole-body translation)
        public const float JumpVelocity = 8.0f;     // m/s upward added to the whole body on jump (higher)
        public const float BicycleBackSpin = 14f;   // angular impulse for backward rotation

        // ---- Run cycle (procedural gait) ----
        public const float StrideRateMax = 9f;      // gait phase speed (rad/s) at full run
        public const float GaitThighSwing = 55f;    // deg the thighs swing fore/aft
        public const float GaitThighLift = 40f;     // extra deg the swing leg lifts (foot clears ground)
        public const float GaitKneeBend = 105f;     // deg the knee folds to pick up the foot
        public const float GaitFootPoint = 20f;     // deg foot dorsiflex through the stride
        // Sprint gait: knees fold harder toward the body and lift higher, faster cadence.
        public const float SprintStrideMul = 1.5f;  // faster leg cadence when sprinting
        public const float SprintThighLift = 75f;   // higher knee lift when sprinting
        public const float SprintKneeBend = 150f;   // knee folds more toward the body
        public const float GaitTorsoLean = 8f;      // deg forward lean while running
        public const float LegSwingRaise = 130f;    // deg a leg raises on LMB/RMB (knee to chest)

        // ---- Trick validation ----
        public const float BicycleWindow = 0.85f;   // seconds the kick stays "live"
        public const float BicycleMinInvert = 0.5f; // pelvis-up dot world-up below this (reclined ~60deg+)
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
        public const float AssistSteerFrac = 0.30f;  // how far the flat velocity direction bends toward goal (0..1)
        public const float AssistDuration = 0.45f;   // seconds the curve is applied after contact
        public const float AssistMinSpeed = 3.5f;    // only assist shots hit with some pace
        public const float AssistMaxAccel = 18f;     // cap on the sideways/steer accel

        // ---- Strike power (on striker contact) ----
        public const float StrikeHorizBoost = 1.6f;  // multiply horizontal velocity when struck
        public const float StrikeHorizMax = 26f;     // cap on resulting horizontal speed (m/s)

        // ---- Headers (head contact) get a little extra ----
        public const float HeaderPowerMul = 1.7f;    // extra power vs a normal strike
        public const float HeaderSwerve = 11f;       // added swerve (spin + lateral curl)
        public const float HeaderAccuracyMul = 1.9f; // stronger goal-ward steer than a normal contact
        // A header REDIRECTS the ball onto a goal-ward horizontal line (not just faster
        // in its old direction), so even a glancing touch flies fast toward goal.
        public const float HeaderGoalBias = 0.85f;   // 0..1: how strongly it aims at goal
        public const float HeaderMinSpeed = 15f;     // floor horizontal speed off a header (m/s)
        public const float HeaderVerticalKeep = 0.35f; // fraction of incoming vertical kept (stays flat)
    }
}
