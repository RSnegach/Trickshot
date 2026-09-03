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
        // The bands come from the SELECTED SPECIES (see Species.All), because a horse is measured
        // at the withers in a different range than a human is at the crown, and 75 kg is a person
        // but not an elephant. Human is 1.60 to 2.05 m and 55 to 110 kg, i.e. unchanged.
        // Species.ApplySelection re-clamps Height/Weight whenever the band moves.
        public static float MinHeight     => Species.Current.Size.Min;
        public static float MaxHeight     => Species.Current.Size.Max;
        public static float DefaultHeight => Species.Current.Size.Default;
        public static float MinWeight     => Species.Current.Mass.Min;
        public static float MaxWeight     => Species.Current.Mass.Max;
        public static float DefaultWeight => Species.Current.Mass.Default;

        // Literal initializers on purpose: these run during static field init, and reading
        // DefaultHeight here would pull in Species' static state mid-initialization for no gain.
        // The real values are set by Species.ApplySelection / ResetToDefault.
        public static float Height = 1.80f;
        public static float Weight = 75f;

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
        /// <summary>
        /// UNIFORM BODY: every body-derived stat baseline is evaluated as though the player were the
        /// species' DEFAULT height and weight. Set by the accuracy drivers, alongside
        /// SkillTree.MaxShootingOverride, so a scored run is the same shot for everybody.
        ///
        /// It overrides the NORMALISED slider positions rather than Height/Weight themselves, which
        /// is what keeps it a stats-only change: the visual scale, mass and the ragdoll build all
        /// read the raw values (see HeightScale / MassMul), so the player still looks and weighs
        /// exactly like the character they made - they just shoot like the default one.
        ///
        /// One switch covers every body coupling there is: shot, move, sprint, jump, push and reach
        /// are all functions of these two.
        /// </summary>
        public static bool UniformBodyOverride;

        public static float HeightT => UniformBodyOverride
            ? Mathf.InverseLerp(MinHeight, MaxHeight, DefaultHeight)
            : Mathf.InverseLerp(MinHeight, MaxHeight, Height);
        public static float WeightT => UniformBodyOverride
            ? Mathf.InverseLerp(MinWeight, MaxWeight, DefaultWeight)
            : Mathf.InverseLerp(MinWeight, MaxWeight, Weight);

        // Where this build sits inside its OWN species' band: 1.0 at that species' default.
        // Species-relative, so a mid-range horse and a mid-range human both read 1.0 here.
        // Used for the body-shape maths, which must not care how big the species is overall.
        public static float BodyHeightScale => Height / DefaultHeight;

        // Physical scale factors for the ragdoll geometry. VisualScale is the species' size
        // relative to a person, so a horse renders bigger than a human of the same slider
        // position. Human VisualScale/VisualGirth are 1f, so the human build is unchanged.
        public static float HeightScale => BodyHeightScale * Species.Current.VisualScale;
        // Girth from weight, but partly discounted by height (a tall heavy player is
        // lean, a short heavy player is stocky). Kept in a sane visual band.
        public static float GirthScale
        {
            get
            {
                // Deliberately BodyHeightScale, not HeightScale: dividing by the species' overall
                // size would push every big species to the bottom of its own weight band.
                float bmiIsh = Weight / (BodyHeightScale * BodyHeightScale); // weight adjusted for frame
                float t = Mathf.InverseLerp(MinWeight, MaxWeight, bmiIsh);
                return Mathf.Lerp(0.82f, 1.35f, t) * Species.Current.VisualGirth;
            }
        }

        // Mass multiplier vs the default build (drives push resistance + shot inertia). Both terms
        // are species-relative, so a default elephant is 1.0 like a default human: cross-species
        // mass advantage is a balancing decision (SpeciesBias.Push), not a side effect of the
        // slider units.
        public static float MassMul => Weight / DefaultWeight;

        // ---- Body baselines (1.0 = default build), from height/weight only ----
        // Every base stat is scaled down 15% (BaseStatScale) EXCEPT jump height, which keeps its
        // full baseline. Applied OUTSIDE the clamp so the whole band shifts down uniformly.
        //
        // The species Bias multiplies OUTSIDE the clamp too, for the same reason: it shifts a
        // species' whole band rather than squeezing builds against the human clamp. Every Bias is
        // 1f today (see SpeciesBias.None), so no species plays differently yet. This is the hook
        // the cross-species balancing pass edits.
        const float BaseStatScale = 0.85f;   // -15% to every base stat except jump height
        static SpeciesBias Bias => Species.Current.Bias;
        static float BodyMove   => BaseStatScale * Bias.Move   * Mathf.Clamp(1f + (0.5f - WeightT) * 0.30f + (0.5f - HeightT) * 0.10f, 0.75f, 1.25f);
        static float BodySprint => BaseStatScale * Bias.Sprint * Mathf.Clamp(1f + (0.5f - WeightT) * 0.40f + (0.5f - HeightT) * 0.12f, 0.7f, 1.3f);
        static float BodyJump   =>                 Bias.Jump   * Mathf.Clamp(1f + (0.5f - WeightT) * 0.45f + (0.5f - HeightT) * 0.18f, 0.65f, 1.35f);
        // Shot is the one baseline written as a FUNCTION of its inputs rather than a property, so the
        // cross-species ceiling below can evaluate it at another species' bias and at the top of both
        // sliders without a second copy of these coefficients. A second copy is exactly how a ceiling
        // goes stale: retune the 0.45 here and a hard-coded ceiling silently stops matching it.
        static float ShotAt(float weightT, float heightT, SpeciesBias bias)
            => BaseStatScale * bias.Shot * Mathf.Clamp(1f + (weightT - 0.5f) * 0.45f + (heightT - 0.5f) * 0.15f, 0.75f, 1.35f);
        static float BodyShot   => ShotAt(WeightT, HeightT, Bias);
        static float BodyPush   => BaseStatScale * Bias.Push   * Mathf.Clamp(1f + (WeightT - 0.5f) * 0.6f  + (HeightT - 0.5f) * 0.2f,  0.7f, 1.5f);
        static float BodyReach  => BaseStatScale * Bias.Reach  * Mathf.Clamp(1f + (HeightT - 0.5f) * 0.35f, 0.85f, 1.2f);

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

        // ---- The cross-species POWER CEILING ----
        // The most shot power and the most header power a HUMAN can ever reach. BallController clamps
        // every species to these, so nothing hits the ball harder than the best human can, whatever
        // its body plan. Both are DERIVED and neither may become a literal: ShotAt keeps the body
        // coefficients in one place and SkillTree.MaxMul sums whatever shooting/heading nodes exist,
        // so retuning the tree or the baseline moves the ceiling with it.
        //
        // Two things make this non-trivial rather than decorative:
        //  - WeightT / HeightT are InverseLerps inside each species' OWN slider band, so a maxed
        //    elephant and a maxed human both reach t = 1 and the body term is already species-neutral
        //    at 1.105. The asymmetry lives entirely in the TREE, where the horse's species-gated Heavy
        //    Hoof adds +12% shot power no human can buy. Evaluating MaxMul against HumanId is what
        //    excludes it.
        //  - Read at the top of BOTH sliders, not at the current build. This is a ceiling on what the
        //    game allows, not a scaling of the current body, so a light horse is not held to a light
        //    human's output. It only ever binds where a species exceeds the human's best case.
        public static float HumanShotPowerMax
            => ShotAt(1f, 1f, Species.ById(Species.HumanId).Bias)
               * SkillTree.MaxMul("shotpower", Species.HumanId);
        public static float HumanHeaderPowerMax => SkillTree.MaxMul("headpower", Species.HumanId);

        // Dribble close-control, 0 (no Control) .. 1 (fully invested trap nodes), derived
        // from the same trap stat as first touch. Drives a tighter carry, faster + sharper
        // turning, and higher move speed with the ball, plus a wider capture net - so a
        // Control build keeps the ball glued and mobile; a raw build is loose and ponderous.
        // TrapMul is 1.0 with an empty tree; the Control trap nodes (First Touch +0.25,
        // Cushion +0.25, Close Control +0.15, Dribbler +0.20) stack to 1.85, so map
        // [1.0 .. 1.85] onto [0 .. 1].
        public static float DribbleTightness => Mathf.InverseLerp(1f, 1.85f, TrapMul);

        // Shooting + Control blend, 0..1, SKILL ONLY (no body coupling, so weight/height never
        // gate it). Shooting side is the skill-tree power mul normalized to its 1.68 ceiling -
        // the same normalization the set-piece accuracy stat uses - and Control is the trap
        // ladder. Drives bicycle-kick trajectory: the more invested, the flatter and faster the
        // bike leaves, which is what kills the un-saveable-looking high looper.
        public static float BicycleSkill01 =>
            Mathf.Clamp01(0.6f * Mathf.Clamp01((SkillTree.Mul("shotpower") - 1f) / 0.68f)
                        + 0.4f * Mathf.Clamp01(DribbleTightness));

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

        public static int PaceStat     => Rate((MoveSpeedMul + SprintSpeedMul) * 0.5f, 0.82f, 2.2f);
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
            // Species first: it owns the height/weight bands, so resetting it before the sliders
            // means DefaultHeight/DefaultWeight below already read the human values.
            Species.Reset();
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
    ///
    /// SPECIES REINTERPRETATION - important. The three (style index, colour) pairs are NOT
    /// human-specific. They are three generic cosmetic slots whose meaning depends on SpeciesId:
    ///
    ///     field pair        SlotKind   Human      Horse       Elephant
    ///     HairStyle/Color   StyleA     hair       mane        ears
    ///     FacialStyle/Color StyleB     facial     markings    tusks
    ///     Accessory/Color   StyleC     accessory  tack        tack
    ///
    /// Per-species fields were rejected: the struct would grow by three ints and three colours per
    /// species and stop fitting comfortably on the roster row. The cost of reinterpreting is that
    /// an index only means anything alongside its SpeciesId, so ALWAYS resolve them together
    /// (SpeciesCosmetics.Count / Label), and never carry an index across a species change without
    /// going through Species.ApplySelection, which re-clamps them.
    /// </summary>
    public struct PlayerAppearance
    {
        // Which species these cosmetics belong to (SpeciesDef.Id; 0 = Human). Wire-stable.
        public byte  SpeciesId;
        public Color Skin;
        public int   HairStyle;
        public Color HairColor;
        public int   FacialStyle;
        public Color FacialColor;
        public int   Accessory;
        public Color AccessoryColor;
        public bool  Adult;          // adult-mode extra cosmetic (the pelvis appendage)
        // Adult "Third Leg" skill multipliers for the appendage (1 = base). Driven by the
        // SkillTree ThirdLeg nodes; networked so remote bodies show the right size.
        public float MemberLen;      // member length multiplier
        public float MemberGirth;    // member thickness multiplier
        public float BallSize;       // berry radius multiplier

        public static PlayerAppearance Default => new PlayerAppearance
        {
            SpeciesId      = Species.HumanId,
            Skin           = new Color(0.85f, 0.65f, 0.52f),
            HairStyle      = 0,                                   // bald (no hair mesh)
            HairColor      = new Color(0.15f, 0.10f, 0.08f),
            FacialStyle    = 0,                                   // clean-shaven
            FacialColor    = new Color(0.15f, 0.10f, 0.08f),
            Accessory      = 0,                                   // none
            AccessoryColor = Color.white,
            Adult          = false,
            MemberLen      = 1f,
            MemberGirth    = 1f,
            BallSize       = 1f,
        };
    }
}
