using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// THE one procedural run cycle, shared by every body in the game that runs: the local striker
    /// (Striker.RunCycle), an AI bot (Footballer.RunGait) and a remote player's kinematic puppet
    /// (ActiveRagdoll.DisplayAnim, AnimState.Run). Three separate copies of these formulas used to
    /// exist, and every one of them carried the same two bugs, which is why the gait lives here now.
    ///
    /// WHY IT READS AS RUNNING AT ALL. The body does not walk. It GLIDES, steered by
    /// ActiveRagdoll.ApplyLocomotion, and the limbs are purely cosmetic pose overrides laid on top -
    /// no foot here pushes on anything. So the only thing that makes a glide read as a run is that
    /// the CADENCE matches the ground speed. Cadence therefore comes from MEASURED horizontal speed
    /// over a stride length, not from input magnitude: pressing a key used to jump the legs straight
    /// to full cadence while the body was still accelerating from a standstill, and legs cycling at
    /// full rate over a body barely moving is exactly what the skating looked like.
    ///
    /// THE BONE MAPPING IS PER PLAN, and this is the part that bites. Bone has 13 members and a
    /// quadruped is those same 13 REPOSED: Thigh/Calf/Foot are its HIND legs and UpperArm/Forearm are
    /// its FRONT legs. So "arm pump" on a quadruped is a front-leg stride, and the old constant
    /// -ArmPumpElbow (a human's permanently bent elbow) folded a horse's front knee FORWARD and then
    /// held it there for the whole cycle, in stance as well as swing. Local +X pitches a limb's lower
    /// end BACKWARD, which is the way a human knee, an equine hock and an equine carpus all fold, so
    /// every fold angle in both profiles below is POSITIVE and a quadruped's front knee only folds
    /// while that leg is in the air.
    ///
    /// PHASING is per plan too. A biped runs CONTRALATERAL (right arm forward with the left leg). A
    /// quadruped trots on DIAGONAL PAIRS (hind left with front right). Same sine, swapped front
    /// phases; nothing else differs.
    ///
    /// Everything here is stateless. Callers own the phase and the fade weight, because the three
    /// call sites tick on different clocks (Update, Update, and a network-driven display clock).
    /// </summary>
    public static class Gait
    {
        /// <summary>
        /// One body plan's gait, in degrees and metres. Human numbers reference the existing
        /// SimConfig constants so the human run keeps its exact tuning and there is still one place
        /// to tweak it; quadruped numbers are literals here, next to the anatomy notes that justify
        /// them, the same way BodyLayout keeps its per-plan geometry.
        /// </summary>
        public struct Profile
        {
            // Metres of ground covered per FULL cycle (each leg once). Cadence = 2pi * speed /
            // stride, so this is the only thing that sets tempo. Scaled by the build height, so a
            // bigger animal takes longer strides at the same speed and its legs cycle slower.
            public float StrideWalk, StrideSprint;

            // HIND limb (biped: the leg). Thigh/Calf/Foot.
            public float HipSwing;        // fore/aft swing amplitude
            public float HipLift;         // extra hip flexion through the forward swing
            public float HipLiftSprint;   // ...at full sprint
            public float KneeFold;        // knee fold through the forward swing (picks the foot up)
            public float KneeFoldSprint;
            public float KneeStance;      // small permanent bend, so a stance leg is not a rigid stick
            public float TipPoint;        // ankle / fetlock articulation

            // FORE limb (biped: the arm). UpperArm/Forearm.
            public float FrontSwing;      // fore/aft swing amplitude
            public float FrontSwingSign;  // +1 keeps the biped's arm-pump sign; -1 makes a front leg
                                          // protract in phase with a hind leg
            public float FrontFoldConst;  // fold held all cycle. A human's bent elbow. ZERO on an
                                          // animal: a front leg is a straight column in stance.
            public float FrontFoldSwing;  // fold added only through the swing. An equine carpus.

            public float TorsoLean;       // constant forward lean. Zero on a barrel already rest-pitched 90.
            public float TorsoBob;        // small pitch oscillation at twice cadence

            public bool FrontPhaseFlip;   // false: front-left takes the hind-left phase (biped
                                          // contralateral). true: front-left takes the OPPOSITE
                                          // phase, pairing it with hind-right (quadruped trot).
            public bool RaiseGatesFront;  // which limb pair a player leg-raise owns and the gait must
                                          // yield to. A biped raises its LEGS (hind); a quadruped
                                          // raises its FRONT legs - see BodyLayoutDef.RaiseL/RaiseR.
        }

        static readonly Profile _biped = new Profile
        {
            // 2.66 m at 3.80 m/s is 8.98 rad/s and 3.18 m at 6.84 m/s is 13.5 rad/s, which are the
            // old StrideRateMax and StrideRateMax * SprintStrideMul to two decimals. The human tempo
            // is deliberately unchanged; what changed is that it now tracks real speed in between.
            StrideWalk   = 2.66f,
            StrideSprint = 3.18f,

            HipSwing       = SimConfig.GaitThighSwing,
            HipLift        = SimConfig.GaitThighLift,
            HipLiftSprint  = SimConfig.SprintThighLift,
            KneeFold       = SimConfig.GaitKneeBend,
            KneeFoldSprint = SimConfig.SprintKneeBend,
            KneeStance     = SimConfig.GaitKneeStance,
            TipPoint       = SimConfig.GaitFootPoint,

            FrontSwing     = SimConfig.ArmPumpSwing,
            FrontSwingSign = 1f,
            FrontFoldConst = -SimConfig.ArmPumpElbow,
            FrontFoldSwing = 0f,

            TorsoLean = SimConfig.GaitTorsoLean,
            TorsoBob  = 2.5f,

            FrontPhaseFlip  = false,
            RaiseGatesFront = false,
        };

        static readonly Profile _quadruped = new Profile
        {
            // A trot, not a human run. Stride is long relative to the body and the joints barely
            // move: a horse at speed swings its whole limb about 20 deg either side of vertical and
            // folds the hock maybe 40, where the human profile above folds a knee 145. Feeding the
            // human numbers into a quadruped is what made the legs windmill.
            StrideWalk   = 2.55f,
            StrideSprint = 3.15f,

            // HIND: hip, stifle+hock (one bone here), fetlock.
            HipSwing       = 20f,
            HipLift        = 24f,
            HipLiftSprint  = 34f,
            KneeFold       = 40f,
            KneeFoldSprint = 52f,
            KneeStance     = 8f,
            TipPoint       = 12f,

            // FRONT: shoulder + carpus. The carpus folds BACKWARD (positive X) and ONLY in the air.
            // Standing on it, it is straight - which is the whole bug report: it used to be folded
            // 65 deg the wrong way, permanently, on both front legs.
            FrontSwing     = 22f,
            FrontSwingSign = -1f,
            FrontFoldConst = 0f,
            FrontFoldSwing = 34f,

            // The barrel rests pitched 90 deg already, so a "forward lean" here would pitch the
            // whole animal nose-down into the turf. Only a small bob survives.
            TorsoLean = 0f,
            TorsoBob  = 1.2f,

            FrontPhaseFlip  = true,
            RaiseGatesFront = true,
        };

        public static Profile For(BodyPlan plan)
            => plan == BodyPlan.Quadruped ? _quadruped : _biped;

        /// <summary>
        /// Phase advance in rad/s for a body moving at <paramref name="speed"/> m/s, plus the smooth
        /// 0..1 sprint blend that widens the amplitudes. The blend is derived from SPEED, not from
        /// the sprint button: on a bool the cadence and every amplitude stepped the instant the key
        /// went down, which is a visible snap mid-stride.
        /// </summary>
        public static float Cadence(float speed, float heightScale, in Profile p, out float sprint01)
        {
            sprint01 = Mathf.Clamp01(Mathf.InverseLerp(SimConfig.StrikerMoveSpeed * 0.9f,
                                                       SimConfig.StrikerMoveSpeed * SimConfig.StrikerSprintMul,
                                                       speed));
            float stride = Mathf.Lerp(p.StrideWalk, p.StrideSprint, sprint01)
                           * Mathf.Max(0.5f, heightScale);
            return Mathf.Min(2f * Mathf.PI * speed / Mathf.Max(0.2f, stride), SimConfig.GaitRateMax);
        }

        /// <summary>
        /// Ease the gait's 0..1 blend weight toward where this speed says it should be. This is what
        /// replaced resetting the phase to zero on a stop: a reset snapped both legs to rest between
        /// two steps, so releasing the stick popped. Fading the WEIGHT instead relaxes the same pose
        /// toward rest and leaves the phase where it was for the next stride. Release is faster than
        /// engage so leaving the ground drops the run pose before the jump pose needs it.
        /// </summary>
        public static float Weight(float current, float speed, bool allowed, float dt)
        {
            float target = allowed
                ? Mathf.Clamp01((speed - SimConfig.GaitMinSpeed) / SimConfig.GaitFadeSpeed)
                : 0f;
            float k = (target > current ? SimConfig.GaitFadeIn : SimConfig.GaitFadeOut) * dt;
            return Mathf.MoveTowards(current, target, k);
        }

        /// <summary>
        /// Write the whole run pose into <paramref name="over"/> (additive local euler per bone,
        /// indexed by <see cref="Bone"/>, length <see cref="Bone.Count"/>). The caller zeroes the
        /// array first and owns what it does with it, so this works for both a live ragdoll's pose
        /// overrides and a kinematic puppet's display FK.
        ///
        /// raiseL/raiseR are the player's 0..1 leg-raise amounts. The gait yields to them limb by
        /// limb rather than dropping out entirely, which is what stopped the release from popping:
        /// at 0.5 raised, the limb carries half a raise and half a stride.
        /// </summary>
        public static void Pose(Vector3[] over, in Profile p, float phase, float weight,
                                float sprint01, float raiseL, float raiseR)
        {
            if (over == null || weight <= 0.0005f) return;
            float keepL = 1f - Mathf.Clamp01(raiseL);
            float keepR = 1f - Mathf.Clamp01(raiseR);
            float hindL  = weight * (p.RaiseGatesFront ? 1f : keepL);
            float hindR  = weight * (p.RaiseGatesFront ? 1f : keepR);
            float frontL = weight * (p.RaiseGatesFront ? keepL : 1f);
            float frontR = weight * (p.RaiseGatesFront ? keepR : 1f);

            float opp = phase + Mathf.PI;

            Hind(over, Bone.ThighL, Bone.CalfL, Bone.FootL, phase, hindL, p, sprint01);
            Hind(over, Bone.ThighR, Bone.CalfR, Bone.FootR, opp,   hindR, p, sprint01);

            // Biped: front-left rides the hind-left phase, so the RIGHT arm goes forward with the
            // left leg. Quadruped: flipped, which pairs front-left with hind-right - a trot.
            Front(over, Bone.UpperArmL, Bone.ForearmL, p.FrontPhaseFlip ? opp : phase, frontL, p);
            Front(over, Bone.UpperArmR, Bone.ForearmR, p.FrontPhaseFlip ? phase : opp, frontR, p);

            over[(int)Bone.Torso] = new Vector3(
                (p.TorsoLean + Mathf.Sin(phase * 2f) * p.TorsoBob) * weight, 0f, 0f);
        }

        // Hind limb: upper swings fore/aft and flexes further through the forward half; the middle
        // joint folds BACKWARD (positive X) to pick the foot up, and holds a small bend in stance so
        // the limb is not a straight stick.
        static void Hind(Vector3[] over, Bone upper, Bone mid, Bone tip,
                         float phase, float w, in Profile p, float sprint01)
        {
            if (w <= 0.0005f) return;
            float sw = Mathf.Sin(phase);
            float lift = Mathf.Max(0f, sw);          // 1 through the forward swing, 0 in stance
            float hipLift = Mathf.Lerp(p.HipLift, p.HipLiftSprint, sprint01);
            float fold    = Mathf.Lerp(p.KneeFold, p.KneeFoldSprint, sprint01);
            over[(int)upper] = new Vector3((-sw * p.HipSwing - lift * hipLift) * w, 0f, 0f);
            over[(int)mid]   = new Vector3((p.KneeStance + lift * fold) * w, 0f, 0f);
            over[(int)tip]   = new Vector3((-sw * p.TipPoint) * w, 0f, 0f);
        }

        // Fore limb: a human's pumping arm or a quadruped's front leg. The only structural
        // difference is where the lower segment's fold comes from - a constant for the human elbow,
        // swing-only for an equine carpus.
        static void Front(Vector3[] over, Bone upper, Bone lower, float phase, float w, in Profile p)
        {
            if (w <= 0.0005f) return;
            float sw = Mathf.Sin(phase);
            float lift = Mathf.Max(0f, sw);
            over[(int)upper] = new Vector3((p.FrontSwingSign * sw * p.FrontSwing) * w, 0f, 0f);
            over[(int)lower] = new Vector3((p.FrontFoldConst + lift * p.FrontFoldSwing) * w, 0f, 0f);
        }
    }
}
