using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The customized player: height, weight, jersey art, name and number, plus the
    /// derived TRAIT multipliers those physical attributes produce. One static Active
    /// profile is read by the ragdoll builder (scale + mass + jersey) and by the
    /// movement / jump / shot / push code (trait multipliers), so a build the player
    /// dials in on the Customize screen is reflected everywhere.
    ///
    /// Trait philosophy: REALISTIC TRADEOFFS - every build is viable.
    ///   Tall + heavy  -> more mass, harder shot, stronger push, higher reach,
    ///                    but slower acceleration/sprint and lower jump.
    ///   Short + light -> faster, more agile, higher jump,
    ///                    but weaker shot/push and easily shoved off the ball.
    /// The two axes are separated: HEIGHT mainly drives reach/leverage + a mild
    /// speed/jump cost; WEIGHT drives mass/power/push + the bigger agility cost.
    /// </summary>
    public static class PlayerProfile
    {
        // ---- Raw attributes (the sliders) ----
        // Height in metres (default 1.80). Range covers a short-and-nippy to a
        // tall-target-man build.
        public const float MinHeight = 1.60f, MaxHeight = 2.05f, DefaultHeight = 1.80f;
        // Weight in kg (default 75). Light winger to heavy powerhouse.
        public const float MinWeight = 55f, MaxWeight = 110f, DefaultWeight = 75f;

        public static float Height = DefaultHeight;
        public static float Weight = DefaultWeight;

        // ---- Identity ----
        public static string PlayerName = "PLAYER";
        public static int Number = 10;

        // Strong foot: shots off the strong-side leg/foot get full accuracy; the weak
        // side gets half. (Head is governed by heading rules; body contacts are weak.)
        public static bool LeftFooted = false;

        // ---- Jersey art (painted on the 2D canvas; applied to the torso material) ----
        public static Texture2D JerseyTex;      // null -> plain team colour
        public static Color JerseyBase = new Color(0.2f, 0.45f, 0.85f);

        // ---- Normalized positions on each axis (0 = min, 1 = max) ----
        public static float HeightT => Mathf.InverseLerp(MinHeight, MaxHeight, Height);
        public static float WeightT => Mathf.InverseLerp(MinWeight, MaxWeight, Weight);

        // Physical scale factors for the ragdoll geometry.
        public static float HeightScale => Height / DefaultHeight;                 // vertical
        // Girth from weight, but partly discounted by height (a tall heavy player is
        // lean, a short heavy player is stocky). Kept in a sane visual band.
        public static float GirthScale
        {
            get
            {
                float bmiIsh = Weight / (HeightScale * HeightScale); // weight adjusted for frame
                float t = Mathf.InverseLerp(MinWeight, MaxWeight, bmiIsh);
                return Mathf.Lerp(0.82f, 1.35f, t);
            }
        }

        // Mass multiplier vs the default build (drives push resistance + shot inertia).
        public static float MassMul => Weight / DefaultWeight;

        // ---- Body baselines (1.0 = default build), from height/weight only ----
        static float BodyMove   => Mathf.Clamp(1f + (0.5f - WeightT) * 0.30f + (0.5f - HeightT) * 0.10f, 0.75f, 1.25f);
        static float BodySprint => Mathf.Clamp(1f + (0.5f - WeightT) * 0.40f + (0.5f - HeightT) * 0.12f, 0.7f, 1.3f);
        static float BodyJump   => Mathf.Clamp(1f + (0.5f - WeightT) * 0.45f + (0.5f - HeightT) * 0.18f, 0.65f, 1.35f);
        static float BodyShot   => Mathf.Clamp(1f + (WeightT - 0.5f) * 0.45f + (HeightT - 0.5f) * 0.15f, 0.75f, 1.35f);
        static float BodyPush   => Mathf.Clamp(1f + (WeightT - 0.5f) * 0.6f  + (HeightT - 0.5f) * 0.2f,  0.7f, 1.5f);
        static float BodyReach  => Mathf.Clamp(1f + (HeightT - 0.5f) * 0.35f, 0.85f, 1.2f);

        // ---- Final TRAIT multipliers = body baseline * skill-tree bonus (STACKED). ----
        public static float MoveSpeedMul   => BodyMove   * SkillTree.Mul("move");
        public static float SprintSpeedMul => BodySprint * SkillTree.Mul("sprint")
                                              * (PerkAfterburners ? SimConfig.AfterburnerMul : 1f);
        public static float JumpMul        => BodyJump   * SkillTree.Mul("jump");
        public static float ShotPowerMul   => BodyShot   * SkillTree.Mul("shotpower");
        public static float PushMul        => BodyPush   * SkillTree.Mul("push") * SkillTree.Mul("massbonus")
                                              * (PerkImmovable ? SimConfig.ImmovableMassMul : 1f);
        public static float ReachMul       => BodyReach  * SkillTree.Mul("reach");

        // Effective mass for the ragdoll build: weight + strength "massbonus" nodes +
        // the Immovable capstone. Heavier bones = harder to shove off the ball.
        public static float EffectiveMassMul => MassMul * SkillTree.Mul("massbonus")
                                                * (PerkImmovable ? SimConfig.ImmovableMassMul : 1f);

        // ---- Skill-only multipliers (no body baseline; 1.0 with an empty tree) ----
        public static float ShotAccuracyMul => SkillTree.Mul("shotacc");    // extra goal-steer on shots
        public static float HeaderPowerMul  => SkillTree.Mul("headpower");
        public static float HeaderAccuracyMul => SkillTree.Mul("headacc");
        public static float WeakFootMul     => SkillTree.Mul("weakfoot");   // scales weak-foot accuracy + power
        public static float TrapMul         => SkillTree.Mul("trap");       // better first touch (deader trap)
        public static float AirFlipMul      => SkillTree.Mul("flip");       // air-pitch spin responsiveness

        // Ground-recovery time after a dive/flop. Agility "recovery" nodes store NEGATIVE
        // amounts, so Mul("recovery") < 1 shortens the prone time; the Acrobat capstone
        // divides it further. Result is a multiplier on SimConfig.DiveProneTime, floored.
        public static float RecoveryTimeMul => SkillTree.Mul("recovery")
                                               / (PerkAcrobat ? SimConfig.AcrobatRecoveryMul : 1f);

        // ---- Capstone perks ----
        public static bool PerkAfterburners => SkillTree.HasPerk("afterburners");
        public static bool PerkCannon       => SkillTree.HasPerk("cannon");
        public static bool PerkAerial       => SkillTree.HasPerk("aerial");
        public static bool PerkImmovable    => SkillTree.HasPerk("immovable");
        public static bool PerkSilky        => SkillTree.HasPerk("silky");
        public static bool PerkAcrobat      => SkillTree.HasPerk("acrobat");

        public static void ResetToDefault()
        {
            Height = DefaultHeight;
            Weight = DefaultWeight;
            PlayerName = "PLAYER";
            Number = 10;
            LeftFooted = false;
            JerseyTex = null;
            SkillTree.Clear();
        }
    }
}
