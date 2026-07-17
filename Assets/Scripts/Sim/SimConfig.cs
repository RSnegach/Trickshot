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
        public const float GoalWidth   = 7.32f; // regulation-ish
        public const float GoalHeight  = 2.44f;
        public const float GoalDepth   = 3.0f;   // deeper goal box

        // ---- Net (procedural flexible mesh + backstop) ----
        public const int NetCols = 26;           // grid resolution across the width (finer cells)
        public const int NetRows = 13;           // grid resolution down the height
        public const float NetStiffness = 7f;    // weak pull back to rest (soft, keeps the pocket)
        public const float NetLinkStiffness = 55f; // structural springs between neighbors: spreads the bulge wide
        public const float NetDamping = 3.5f;    // velocity damping on nodes
        public const float NetBallPush = 6.5f;   // how hard the ball billows the net
        public const float NetMaxStretch = 1.6f; // max node displacement from rest (m) (deep billow)
        public const float PenaltyBoxDepth = 16.5f;
        public const float PenaltyBoxWidth = 20f; // slightly narrower than field

        // Goal sits at +Z end. Crosser starts near a wing at -Z / +X corner.
        public static readonly Vector3 GoalCenter    = new Vector3(0f, 0f, FieldLength * 0.5f);
        public static readonly Vector3 CrosserStart   = new Vector3(9.5f, 0f, FieldLength * 0.5f - 5.5f);
        public static readonly Vector3 StrikerStart   = new Vector3(-1.5f, 0f, FieldLength * 0.5f - 8.5f);
        public static readonly Vector3 KeeperStart    = new Vector3(0f, 0f, FieldLength * 0.5f - 0.6f);
        public static readonly Vector3 ReticleStart   = new Vector3(0f, 0.02f, FieldLength * 0.5f - 8.5f);

        // ---- Physics ----
        public const float Gravity = -19.6f;      // 2x real gravity: snappier, arcade feel
        public const float BallMass = 0.43f;
        public const float BallRadius = 0.22f;
        public const float BallDrag = 0.05f;
        public const float BallAngularDrag = 0.05f;
        public const float BallBounciness = 0.55f;

        // ---- Cross tuning ----
        public const float CrossTimeLoft = 1.55f;  // loftier serve: floaty, slow
        public const float CrossTimeDrive = 0.95f; // driven serve: faster, flatter
        public const float MaxCurlAccel = 8f;      // lateral accel while airborne

        // ---- Auto serve ----
        public const float ServeFirstDelay = 1.6f; // before the first cross
        public const float ServeInterval = 2.0f;   // seconds between crosses (rapid-fire testing)
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
        // Whole-body backspin: rotate toward flat-on-the-back and stop, so it does not
        // over-rotate into a full spin. Rate is the max deg/s; target is the torso tilt
        // (deg from upright) to settle at (~180 = fully on the back).
        public const float ReclineSpinRate = 300f;
        public const float ReclineTargetTilt = 170f;
        public const float ReclineTorsoLean = 55f;   // deg the torso tips back during recline
        public const float ReclineRestLeg = 45f;     // deg legs sit at while reclined (not clicking)
        public const float ReclineProneTime = 1.2f;  // stays flat on back this long after landing

        // ---- Dive header (W + Space, forward dive) ----
        public const float DiveForwardVel = 6.0f;    // forward launch speed
        public const float DiveUpVel = 3.0f;         // upward launch speed
        public const float DiveSpinRate = 300f;      // whole-body forward-pitch rate (deg/s) to go belly-down
        public const float DiveProneTime = 1.1f;     // stays prone this long after landing
        public const float BalanceFrequency = 3.2f;
        public const float BalanceDamping = 0.85f;

        // ---- Striker locomotion ----
        public const float StrikerMoveSpeed = 4.8f;
        public const float StrikerAccel = 22f;      // applied to every bone (whole-body translation)
        public const float JumpVelocity = 6.0f;     // m/s upward added to the whole body on jump
        public const float BicycleBackSpin = 14f;   // angular impulse for backward rotation

        // ---- Run cycle (procedural gait) ----
        public const float StrideRateMax = 9f;      // gait phase speed (rad/s) at full run
        public const float GaitThighSwing = 55f;    // deg the thighs swing fore/aft
        public const float GaitThighLift = 40f;     // extra deg the swing leg lifts (foot clears ground)
        public const float GaitKneeBend = 105f;     // deg the knee folds to pick up the foot
        public const float GaitFootPoint = 20f;     // deg foot dorsiflex through the stride
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
    }
}
