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
        public const float GoalDepth   = 1.6f;
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
        public const float MaxChargeTime = 1.25f; // seconds to full charge
        public const float CrossTimeLoft = 1.55f; // low charge: floaty, slow
        public const float CrossTimeDrive = 0.92f; // full charge: driven, fast
        public const float MaxCurlAccel = 9f;      // lateral accel while airborne

        // ---- Ragdoll drive ----
        public const float JointSpring = 3200f;
        public const float JointDamper = 160f;
        public const float JointMaxForce = 24000f; // finite: strong impacts can overpower
        public const float BalanceFrequency = 3.2f;
        public const float BalanceDamping = 0.85f;

        // ---- Striker locomotion ----
        public const float StrikerMoveSpeed = 4.5f;
        public const float StrikerAccel = 26f;
        public const float JumpImpulse = 6.2f;
        public const float BicycleBackSpin = 14f; // angular impulse for backward rotation

        // ---- Trick validation ----
        public const float BicycleWindow = 0.85f;   // seconds the kick stays "live"
        public const float BicycleMinInvert = 0.15f; // pelvis-up dot world-up must be below this
        public const float ValidHitBonus = 6.5f;     // extra ball speed on a clean trick
    }
}
