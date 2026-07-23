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
        // PNG encoding of JerseyTex, cached when the jersey is finalized. Networked to other peers
        // (chunked) so remote bodies wear this player's painted kit; null -> nothing to send.
        public static byte[] JerseyPng;
        public static Color JerseyBase = new Color(0.2f, 0.45f, 0.85f);

        // ---- Appearance (skin + head cosmetics) ----
        // The LOCAL player's look. The same PlayerAppearance struct rides the wire per slot so
        // remote players show their own look. Cosmetics are purely visual (no colliders).
        public static PlayerAppearance Appearance = PlayerAppearance.Default;

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
        // Every base stat is scaled down 15% (BaseStatScale) EXCEPT jump height, which keeps its
        // full baseline. Applied OUTSIDE the clamp so the whole band shifts down uniformly.
        const float BaseStatScale = 0.85f;   // -15% to every base stat except jump height
        static float BodyMove   => BaseStatScale * Mathf.Clamp(1f + (0.5f - WeightT) * 0.30f + (0.5f - HeightT) * 0.10f, 0.75f, 1.25f);
        static float BodySprint => BaseStatScale * Mathf.Clamp(1f + (0.5f - WeightT) * 0.40f + (0.5f - HeightT) * 0.12f, 0.7f, 1.3f);
        static float BodyJump   => Mathf.Clamp(1f + (0.5f - WeightT) * 0.45f + (0.5f - HeightT) * 0.18f, 0.65f, 1.35f);
        static float BodyShot   => BaseStatScale * Mathf.Clamp(1f + (WeightT - 0.5f) * 0.45f + (HeightT - 0.5f) * 0.15f, 0.75f, 1.35f);
        static float BodyPush   => BaseStatScale * Mathf.Clamp(1f + (WeightT - 0.5f) * 0.6f  + (HeightT - 0.5f) * 0.2f,  0.7f, 1.5f);
        static float BodyReach  => BaseStatScale * Mathf.Clamp(1f + (HeightT - 0.5f) * 0.35f, 0.85f, 1.2f);

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
        public static float PassPowerMul    => SkillTree.Mul("passpower");  // faster/harder passes
        public static float PassAccuracyMul => SkillTree.Mul("passacc");    // less scatter on passes (Maestro ~ perfect)

        // Dribble close-control, 0 (no Control) .. 1 (fully invested trap nodes), derived
        // from the same trap stat as first touch. Drives a tighter carry, faster + sharper
        // turning, and higher move speed with the ball, plus a wider capture net - so a
        // Control build keeps the ball glued and mobile; a raw build is loose and ponderous.
        // TrapMul is 1.0 with an empty tree; the Control trap nodes (First Touch +0.25,
        // Cushion +0.25, Close Control +0.15, Dribbler +0.20) stack to 1.85, so map
        // [1.0 .. 1.85] onto [0 .. 1].
        public static float DribbleTightness => Mathf.InverseLerp(1f, 1.85f, TrapMul);

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
        public static bool PerkMaestro      => SkillTree.HasPerk("maestro");

        // ---- 0-100 attribute card (radar + list). Each maps a trait multiplier onto a
        //      0..100 rating on a readable curve, combining the height/weight body baseline
        //      with skill-tree investment. One rating per skill-tree category + a physical. ----
        // A mul of ~1.0 (default build, no nodes) sits near 50; heavy investment approaches
        // ~95. Helper: map [loMul..hiMul] onto [10..99].
        static int Rate(float mul, float loMul, float hiMul)
            => Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(10f, 99f, Mathf.InverseLerp(loMul, hiMul, mul))), 1, 99);

        public static int PaceStat     => Rate((MoveSpeedMul + SprintSpeedMul) * 0.5f, 0.75f, 1.7f);
        public static int ShootingStat => Rate(ShotPowerMul * (0.6f + 0.4f * ShotAccuracyMul), 0.8f, 2.1f);
        public static int PassingStat  => Rate(PassPowerMul * 0.4f + PassAccuracyMul * 0.6f, 0.9f, 1.9f);
        public static int HeadingStat  => Rate(HeaderPowerMul * 0.5f + HeaderAccuracyMul * 0.5f, 0.9f, 2.0f);
        public static int PhysicalStat => Rate(PushMul, 0.7f, 2.2f);
        public static int ControlStat  => Rate(TrapMul * (0.7f + 0.3f * WeakFootMul), 0.95f, 2.0f);
        public static int AgilityStat  => Rate(JumpMul * (0.6f + 0.4f * AirFlipMul), 0.75f, 1.9f);

        // Radar axes, in draw order (clockwise from top). Label + value.
        public static (string label, int value)[] StatCard => new[]
        {
            ("PAC", PaceStat), ("SHO", ShootingStat), ("PAS", PassingStat),
            ("PHY", PhysicalStat), ("DRI", ControlStat), ("AGI", AgilityStat), ("HEA", HeadingStat),
        };

        public static void ResetToDefault()
        {
            Height = DefaultHeight;
            Weight = DefaultWeight;
            PlayerName = "PLAYER";
            Number = 10;
            LeftFooted = false;
            JerseyTex = null;
            JerseyPng = null;
            Appearance = PlayerAppearance.Default;
            SkillTree.Clear();
        }
    }

    /// <summary>
    /// A player's cosmetic appearance: skin tone plus head cosmetics (hair, facial hair,
    /// accessory), each a style index into the Cosmetics catalogs and a tint colour. Purely
    /// visual - nothing here ever gets a collider. Small + value-type so it packs onto the
    /// network roster row (see NetMessages.LobbySlot) for per-player MP appearance.
    /// Style index 0 means "none" for hair (bald), facial hair (clean-shaven), and accessory.
    /// </summary>
    public struct PlayerAppearance
    {
        public Color Skin;
        public int   HairStyle;
        public Color HairColor;
        public int   FacialStyle;
        public Color FacialColor;
        public int   Accessory;
        public Color AccessoryColor;

        public static PlayerAppearance Default => new PlayerAppearance
        {
            Skin           = new Color(0.85f, 0.65f, 0.52f),
            HairStyle      = 0,                                   // bald (no hair mesh)
            HairColor      = new Color(0.15f, 0.10f, 0.08f),
            FacialStyle    = 0,                                   // clean-shaven
            FacialColor    = new Color(0.15f, 0.10f, 0.08f),
            Accessory      = 0,                                   // none
            AccessoryColor = Color.white,
        };
    }
}
