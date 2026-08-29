using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Which skeleton a species wants. The rig discriminator: ActiveRagdoll.Build picks its bone
    /// table with BodyLayout.For(plan), so this field really does branch the built body.
    ///
    /// Quadruped is not a second skeleton. It is the same fixed 13 <see cref="Bone"/> members
    /// REPOSED: the torso becomes a horizontal barrel, the legs become the hind legs, the arms
    /// become the front legs, and the pelvis stays upright as the free root. See BodyLayout.
    /// </summary>
    public enum BodyPlan { Biped, Quadruped }

    /// <summary>
    /// One numeric dial on the customize Body stage (size or mass). Per species, because a horse
    /// is measured at the withers in a different band than a human is at the crown, and an
    /// elephant's mass is in tonnes not kilos.
    /// </summary>
    public struct SpeciesAxis
    {
        public string Label;    // "Height", "Withers", "Shoulder"
        public string Unit;     // "m", "kg"
        public string Format;   // numeric format for the readout, e.g. "0.00" or "0"
        public float  Min, Max, Default;

        public float Clamp(float v) => Mathf.Clamp(v, Min, Max);
        public float T(float v)     => Mathf.InverseLerp(Min, Max, v);
        // "Height:  1.80 m"
        public string Read(float v) => Label + ":  " + v.ToString(Format) + " " + Unit;
    }

    /// <summary>
    /// Per-species multipliers on the BODY baselines in PlayerProfile (before any skill tree).
    /// This is the ENTIRE cross-species balancing surface: one block per species, so tuning a
    /// horse against an elephant is six numbers, not a bespoke skill tree.
    ///
    /// Everything is 1f today (see Species.All) so no species plays differently yet. Balancing
    /// is deliberately deferred until the real models exist.
    /// </summary>
    public struct SpeciesBias
    {
        public float Move, Sprint, Jump, Shot, Push, Reach;

        public static SpeciesBias None => new SpeciesBias
        { Move = 1f, Sprint = 1f, Jump = 1f, Shot = 1f, Push = 1f, Reach = 1f };
    }

    /// <summary>
    /// What "airborne + BOTH mouse buttons" does to this species' body. Everything AFTER the contact
    /// is species-agnostic: Bone.Head touches the ball, it is a header, on every body, and the ball
    /// leaves the same way. Only the aid that gets the head there differs.
    ///
    /// It has to differ, because the biped aid is meaningless on a quadruped and then actively
    /// harmful. A human folds its chest 90 deg forward and converts standing height into 0.61 m of
    /// forward REACH. A quadruped already owns forward reach (its head sits 0.96 ahead of the barrel
    /// pivot, only 21 deg above horizontal) so pitching the barrel moves the head almost straight
    /// VERTICALLY instead. That is the axis a quadruped is short of, so it is the axis the aid buys:
    /// down for the horse, up for the elephant. And the 90 deg literal cannot simply be reused,
    /// because pose overrides POST-MULTIPLY the bone's REST rotation (ActiveRagdoll FixedUpdate) and
    /// the quadruped barrel already rests at Euler(90,0,0) - the same override would swing it a full
    /// 180 deg nose-first into the turf.
    ///
    /// Every field defaults to today's biped numbers, so a species that says nothing here behaves
    /// exactly as it does now. Only the two quadrupeds override it.
    /// </summary>
    public struct HeaderAction
    {
        // Additive local +X on the Torso, degrees. +X folds a bone FORWARD in its own rest frame
        // under either plan, so one sign convention covers both bodies: on a biped this is the chest
        // fold, on a quadruped it pitches the barrel nose-DOWN. Negative rears the barrel up.
        public float TorsoDeg;

        // Additive local +X on the Head, degrees. Positive drops the muzzle, negative lifts it.
        // Small on its own (the head's joint pivot is only 0.18 from its centre, so even 45 deg
        // moves it 0.14) - it is the garnish that sells the barrel pitch, not the reach itself.
        // Zero on a biped, which is why no code writes a Head override today.
        public float HeadDeg;

        // Where both raise limbs sit while heading: a biped's legs, a quadruped's front legs.
        public float LegRaiseMul;

        // Per-second rate the 0..1 pose blend eases in. High is snappy. The physics skeleton chases
        // the target through the joint drive, so this sets how fast the INTENT moves and the joint
        // lag supplies the smoothness for free.
        public float Ease;

        // ---- ball response ----
        //
        // NARROWING an earlier note that used to sit here and said there is NO per-species ball
        // response. What that note actually established is narrower than what it claimed: added
        // UPWARD loft does not work, because a headed ball must fit UNDER the 2.44 m bar with a whole
        // ball diameter of room (a 2.22 m ceiling on the contact surface) and the tallest species
        // already heads from about 2.06 m at the top of its Weight slider, so there is nothing to
        // scoop into and the +Z end of the arena has no wall to catch an overhit. Both fields below
        // move the ball AWAY from that ceiling, which is the safe direction, so the argument does not
        // reach them.

        // Trim on the header's PACE term (BallController scales inSpeed by this). 1 = today's
        // behaviour, which is what every biped uses.
        //
        // A quadruped needed one because the pace comes from the ball's POST-SOLVE flat speed, i.e.
        // the bounce off the head, and a quadruped's head arrives at that bounce much faster: the
        // horse's skull sits 1.23 m from the hip pivot against a human head's 0.61, so at the same
        // Ease rate it sweeps twice the linear speed, and the barrel it hangs off is drive-compensated
        // 14x to 28x (BoneSpec.DriveMul carries _massMul * _hScale^2) so it does not give under the
        // impact the way a human chest does. The result saturated the outgoing cap, which is
        // proportional to ShotPowerMul and so never restrained it, and every animal header came out a
        // maximum-power shot.
        //
        // Note what this is NOT: the excess was never the animal's MASS. ShotPowerMul is built from
        // WeightT/HeightT, which are InverseLerps inside each species' OWN slider band, so a default
        // elephant and a default human read the same 0.5 and get the same multiplier.
        //
        // These are aim-and-feel trims trued up by playing, not derived constants. Adjust them here.
        // Zero is read as "unset" and treated as 1 so a new species cannot silently lose its header.
        public float PaceMul;

        // Degrees the outgoing header is tilted DOWNWARD, about the axis across its own direction.
        // SPEED PRESERVING: it re-aims the shot, it does not add power. Zero on a biped.
        //
        // Flat per species rather than a formula on the contact height, which is the version that
        // looks more principled and is worse: the only reference height that makes a standing human
        // read zero is a standing human's, and then a JUMPING human reads high and gets taxed for
        // jumping. A per-species constant taxes exactly the two bodies that head from a standing
        // height a person has to leave the ground for. Clamped against
        // SimConfig.HeaderMaxDiveDeg so a ball already falling steeply is not driven into the turf.
        public float DownDeg;

        // Scale on the FINAL outgoing header velocity, applied AFTER the cap. 1 = today's behaviour.
        //
        // Distinct from PaceMul on purpose, and the distinction is the whole point: PaceMul trims the
        // INPUT pace term, so it only moves headers that come in below the cap and does nothing to a
        // species that saturates it. This one scales the result, cap included, so it is the only field
        // that actually lowers a species' header MAXIMUM. Sub-maximum headers scale by the same factor,
        // which is what keeps the two ends of the band consistent, and aim is untouched because the
        // DownDeg tilt is magnitude preserving and runs before this.
        //
        // Zero is read as "unset" and treated as 1, as with PaceMul.
        public float SpeedMul;

        /// <summary>Today's biped behaviour, to the constant. The default for every species.</summary>
        public static HeaderAction Biped => new HeaderAction
        {
            TorsoDeg    = SimConfig.HeaderTorsoBend,
            HeadDeg     = 0f,
            LegRaiseMul = SimConfig.HeaderLegRaiseMul,
            Ease        = SimConfig.HeaderBendEase,
            PaceMul     = 1f,
            DownDeg     = 0f,
            SpeedMul    = 1f,
        };
    }

    /// <summary>
    /// Which PlayerAppearance field a customize tab drives. The three style slots are
    /// REINTERPRETED per species rather than duplicated (a horse's StyleA is its mane, a human's
    /// is its hair) so the appearance struct stays small enough for the network roster row.
    /// </summary>
    public enum SlotKind { Skin, StyleA, StyleB, StyleC }

    /// <summary>One appearance tab for a species: its name, what it drives, and its colour label.</summary>
    public class SpeciesSlot
    {
        public string   Tab;          // tab name in the ‹ › cycler, e.g. "HAIR" / "MANE"
        public SlotKind Kind;
        // Label above the free colour wheel. For a style slot that is the tint of the style
        // ("Hair colour"); for the Skin slot it heads the wheel BELOW the preset swatch row, whose
        // own heading comes from SpeciesCosmetics.SkinGroupLabel.
        public string   ColorLabel;
    }

    /// <summary>
    /// One playable species. Everything that differs between a human, a horse and an elephant on
    /// the setup screens lives here as data, so there is exactly one species picker screen and
    /// exactly one customize screen no matter how many species exist.
    /// </summary>
    public class SpeciesDef
    {
        // Wire-stable id. Networked as a byte in PlayerAppearance, so NEVER reorder or reuse:
        // append new species at the END of Species.All with the next free id.
        public byte   Id;
        public string Name;
        public string Blurb;

        public BodyPlan Plan;

        public SpeciesAxis Size;   // the height/withers/shoulder slider
        public SpeciesAxis Mass;   // the weight slider

        // Visual proportion of the built body, relative to a default human. Applied on top of the
        // species-relative size, so the sliders stay in species units while the rendered body
        // still reads as "bigger than a person". This is the whole size ladder: see the note above
        // Species.All for the rule the numbers follow.
        public float VisualScale = 1f;
        public float VisualGirth = 1f;

        public SpeciesBias Bias = SpeciesBias.None;

        // What airborne + both buttons does to the BODY. Defaults to the biped numbers, so Human,
        // Gorilla and Ostrich need no entry and cannot drift. The ball response off the head is
        // species-independent; see HeaderAction.
        public HeaderAction Header = HeaderAction.Biped;

        public SpeciesSlot[] Slots;      // appearance tabs after the always-present BODY tab

        public bool AllowsAdult = false; // adult-mode cosmetic + the Third Leg skill tab

        /// <summary>
        /// Extra size multiplier for the adult-mode appendage (AnatomySim), ON TOP of a build scale
        /// that ALREADY carries the species' size. 1 = follow the build scale, which is every species
        /// today.
        ///
        /// This was 1.7 (Horse) and 1.9 (Elephant), authored on the assumption it was the only
        /// species term. It was not: AnatomySim's quadruped branch takes sqrt(GirthScale *
        /// HeightScale), which measures 1.176 on a default horse and 1.383 on a default elephant
        /// against a human's 1.013, so the species size was counted twice. Measured at each species'
        /// default slider position the piece came out 1.97x (horse) and 2.59x (elephant) the human's:
        /// 0.67 m and 0.88 m long at 0.112 m and 0.147 m diameter, on a body only 1.18x / 1.34x a
        /// person's. It read as a fifth leg, at 74% of a horse's hind-leg length and 88% of an
        /// elephant's. Note that 1.7 / 1.9 also sat outside the VisualScale ladder above, which is
        /// held under a 2x total spread on purpose.
        ///
        /// Two of the symptoms were geometric, not matters of taste. At the BOTTOM of the elephant's
        /// sliders the belly drops to 0.652 m - the barrel's depth scales with GIRTH while its centre
        /// height scales with HEIGHT, so a min-height build hangs lowest - and the tip landed 0.094 m
        /// BELOW the turf. At the TOP of both sliders the derived fore/aft anchor walked 0.019 m
        /// (horse) / 0.013 m (elephant) past the front face of the pelvis it measured its height from,
        /// so the shaft hung under the barrel with a gap above it.
        ///
        /// HEADROOM, if a future species wants a real bump: the largest value that still keeps the tip
        /// a ball radius (0.11 m) clear of the turf at base Third Leg skills, at the worst point on
        /// the sliders, is 1.715 on a horse and 1.380 on an elephant. That is a floor on the geometry,
        /// not a licence - anything above ~1.2 starts reading as a limb again.
        /// </summary>
        public float AdultScale = 1f;

        /// <summary>
        /// How much of the Third Leg skill ladder this species actually gets, 0..1. 1 = the full
        /// ladder (length x2.30, girth x2.10, balls x2.10 at ANACONDA); 0 = the skills do nothing.
        ///
        /// Exists because ANACONDA is one shared capstone across wildly different animals and a single
        /// multiplier cannot suit all of them. A human's base piece is small relative to a human, so
        /// x2.30 is a dramatic-but-plausible capstone. A stallion's and a bull elephant's base is
        /// already most of the way to what the animal really has, so the same x2.30 took them to 0.90 m
        /// and 1.06 m - roughly double a real stallion and past a real elephant, which is what "way
        /// too big" was pointing at.
        ///
        /// Set so each species' ANACONDA is NOTICEABLY above its own base and still zoologically
        /// defensible (real erect averages in brackets):
        ///     Human     base 0.150 m -> 0.341 m  (+128%)   [avg 0.14]  the capstone is meant to read
        ///     Horse     base 0.396 m -> 0.550 m  (+39%)    [avg 0.50]
        ///     Elephant  base 0.465 m -> 0.660 m  (+42%)    [avg 1.00]
        ///
        /// HONEST NOTE on the elephant: its base is only 47% of a real bull's, so even the capstone
        /// lands at 66% of life. Raising the base is what would fix that, and the base was explicitly
        /// kept - so ANACONDA is measured as a step above THIS GAME's species average, not against
        /// zoology. Matching zoology on the elephant would reproduce the 1.06 m that was rejected.
        ///
        /// Applies to length, girth and ball size together, so a capstone keeps the silhouette's
        /// proportions instead of turning the piece into a different shape.
        /// </summary>
        public float AdultGrowth = 1f;
        public bool WearsJersey = true;  // reserved: a kit-less species would skip the Jersey stage

        public string InstinctTab;       // Instinct tab label; null = this species has no Instinct tab

        // Preview camera framing (PlayerPreview.LateUpdate). A quadruped is long rather than tall,
        // so it needs the camera further back and the pivot lower than a person does. PreviewZ
        // slides the pivot FORWARD along the body's length: a quadruped's barrel is centred but its
        // head still sticks out front, so without it the animal sits off-centre and a side-on drag
        // pushes the muzzle clean out of frame. Zero PreviewZ for a biped.
        //
        // ALL FOUR scale by the build's height, DISTANCE INCLUDED. That was the bug behind a cropped
        // horse: PreviewHeight and PreviewZ scaled while PreviewDist did not, so the body grew inside
        // a fixed frustum as the Size slider went up and no authored distance could hold across the
        // range. Scaling the distance too makes the required framing scale-independent.
        //
        // PreviewDist is now authored for the VERTICAL fit alone. PlayerPreview raises it when the
        // column is too narrow to fit PreviewHalfW, which is what the numbers below rely on: the
        // horse's old 4.6 was inflated purely to survive a 300 px portrait column and left the animal
        // small in frame everywhere.
        public float PreviewDist   = 3.2f;
        public float PreviewHeight = 1.0f;
        public float PreviewZ      = 0f;

        /// <summary>
        /// Half the body's widest visible span about the preview pivot, at unit scale, on whichever
        /// axis is widest. The camera pulls back until this fits HORIZONTALLY, which is the term that
        /// actually crops a quadruped: fieldOfView is the VERTICAL fov, so a portrait column's
        /// horizontal half-extent is only tan(fov/2) * aspect, and for a 300x600 column that is
        /// 0.192*d against 0.384*d vertically.
        ///
        /// Measure it from the geometry, including decor, not from the bone table: on a horse the
        /// widest points are the MUZZLE and the TAIL, both decor, and on an elephant it is the trunk
        /// tip. A biped's is small and never binds; it is carried anyway so no species can crop.
        /// </summary>
        public float PreviewHalfW = 0.48f;

        // False = this species has no rig of its own yet and is drawn with the biped stand-in,
        // restyled only by VisualScale/VisualGirth. Surfaced on the picker so nobody mistakes the
        // placeholder for the finished model. True for Human and for the two quadrupeds.
        public bool ModelReady;

        public bool HasSlot(SlotKind k)
        {
            if (Slots == null) return false;
            for (int i = 0; i < Slots.Length; i++) if (Slots[i].Kind == k) return true;
            return false;
        }
    }

    /// <summary>
    /// The species catalog and the current selection, following the StadiumStyle.All /
    /// SelectedIndex house pattern. Selection is keyed by the wire-stable SpeciesDef.Id, not by
    /// array index, so the array can be reordered for presentation without breaking saves or peers.
    ///
    /// Go through ApplySelection to change species. It is the single funnel that keeps the rest of
    /// the profile coherent (ranges, style indices, skill nodes, adult mode).
    /// </summary>
    public static class Species
    {
        public const byte HumanId = 0;

        // The two species with bespoke body proportions and appendages, named because BodyLayout
        // has to branch on them (see BodyLayout.SpeciesOverride). Gorilla (3) and Ostrich (4) get no
        // constant on purpose: nothing branches on them, they are the shared biped table.
        public const byte HorseId    = 1;
        public const byte ElephantId = 2;

        public static byte SelectedId = HumanId;

        public static SpeciesDef Current => ById(SelectedId);

        public static SpeciesDef ById(byte id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
            return All[0];
        }

        public static int IndexOf(byte id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return 0;
        }

        /// <summary>
        /// True when this species may be PICKED right now. Gorilla and Ostrich still draw the biped
        /// stand-in, so they are hidden from the picker until their rigs land, and ModelReady is the
        /// gate. Their SpeciesDef entries STAY: the id rides the wire on PlayerAppearance and sits in
        /// saved profiles, so ById must keep resolving 3 and 4 forever. Never renumber or reuse an id.
        /// </summary>
        public static bool Selectable(byte id) => ById(id).ModelReady;

        /// <summary>
        /// Switch species and drag every dependent piece of state back into range, so nothing
        /// downstream has to defend itself against a horse holding a human's hair index or a
        /// 2 tonne human.
        ///
        /// Called by the picker screen ONLY. Do not call it from the appearance decode path: it
        /// writes the LOCAL PlayerProfile, so running it on a peer's decoded appearance would
        /// overwrite this player's own species, sliders and skill nodes with theirs.
        /// </summary>
        public static void ApplySelection(byte id)
        {
            var def = ById(id);
            // A saved profile (or a stale byte) can name a species the picker no longer offers. Land
            // on Human rather than leaving the player selected on a row that is not drawn, which
            // would look like a dead screen. This is the single funnel, so the guard belongs here.
            if (!def.ModelReady) def = All[0];
            SelectedId = def.Id;
            PlayerProfile.Appearance.SpeciesId = def.Id;

            // Sliders: the old value is in the OLD species' units. If it happens to fall inside
            // the new band keep it (re-picking the same species must not reset the dials); if it
            // does not, 75 kg of elephant is meaningless so land on the new default rather than
            // pinning to the range edge.
            PlayerProfile.Height = InBand(def.Size, PlayerProfile.Height) ? PlayerProfile.Height : def.Size.Default;
            PlayerProfile.Weight = InBand(def.Mass, PlayerProfile.Weight) ? PlayerProfile.Weight : def.Mass.Default;

            // Style indices point into a DIFFERENT catalog now. Clamp, and zero anything the new
            // species has no tab for so it cannot keep rendering.
            PlayerProfile.Appearance.HairStyle   = ClampSlot(def, SlotKind.StyleA, PlayerProfile.Appearance.HairStyle);
            PlayerProfile.Appearance.FacialStyle = ClampSlot(def, SlotKind.StyleB, PlayerProfile.Appearance.FacialStyle);
            PlayerProfile.Appearance.Accessory   = ClampSlot(def, SlotKind.StyleC, PlayerProfile.Appearance.Accessory);

            // Instinct nodes are species-gated; owned ones for another species would otherwise
            // keep paying into SkillTree.Mul while being invisible in the UI.
            SkillTree.DropForeignSpecies();

            if (!def.AllowsAdult)
            {
                PlayerProfile.Appearance.Adult = false;
                PlayerProfile.Appearance.MemberLen   = 1f;
                PlayerProfile.Appearance.MemberGirth = 1f;
                PlayerProfile.Appearance.BallSize    = 1f;
            }
        }

        static bool InBand(SpeciesAxis ax, float v) => v >= ax.Min && v <= ax.Max;

        static int ClampSlot(SpeciesDef def, SlotKind kind, int current)
        {
            if (!def.HasSlot(kind)) return 0;
            int count = SpeciesCosmetics.Count(def.Id, kind);
            if (count <= 0) return 0;
            return Mathf.Clamp(current, 0, count - 1);
        }

        /// <summary>Reset selection to Human and re-clamp. Used by PlayerProfile.ResetToDefault.</summary>
        public static void Reset()
        {
            SelectedId = HumanId;
            var def = All[0];
            PlayerProfile.Appearance.SpeciesId = HumanId;
            PlayerProfile.Height = def.Size.Default;
            PlayerProfile.Weight = def.Mass.Default;
        }

        static SpeciesAxis Axis(string label, string unit, string fmt, float min, float def, float max)
            => new SpeciesAxis { Label = label, Unit = unit, Format = fmt, Min = min, Max = max, Default = def };

        static SpeciesSlot Slot(string tab, SlotKind kind, string colorLabel)
            => new SpeciesSlot { Tab = tab, Kind = kind, ColorLabel = colorLabel };

        // NOTE on the numbers. The rule: EVERYTHING STAYS HUMAN SIZED and animals are stylized
        // down, ordered by real animal size. An elephant has to read as "big" without being
        // literally 3.2 m, a giraffe would be taller than a horse, a horse a little taller than a
        // person. So the Size/Mass sliders keep real-world-ish bands (they are what the player
        // reads on the dial) while VisualScale, which is what actually gets built, stays inside a
        // narrow ladder:
        //
        //     Human 1.00  <  Gorilla 1.08  <  Horse 1.18  <  Ostrich 1.20  <  Elephant 1.34
        //
        // The pitch, ball and goal are all human sized and the joint drives do not scale with mass
        // (SimConfig.JointSpring / JointMaxForce are constants), so the total spread across every
        // species stays under 2x. VisualGirth carries the rest of the read: a gorilla is short and
        // very wide (1.28), an ostrich is tall and spindly (0.80), an elephant is both (1.40).
        // Widen the ladder only alongside a physics pass, not on its own.
        public static readonly SpeciesDef[] All =
        {
            new SpeciesDef
            {
                Id = 0, Name = "Human", Plan = BodyPlan.Biped, ModelReady = true,
                Blurb = "Default build. Full cosmetics.",
                Size = Axis("Height", "m",  "0.00", 1.60f, 1.80f, 2.05f),
                Mass = Axis("Weight", "kg", "0",    55f,   75f,   110f),
                VisualScale = 1f, VisualGirth = 1f,
                AllowsAdult = true,
                Slots = new[]
                {
                    // "Everyone Else" is the existing human wheel heading, kept verbatim.
                    Slot("SKIN",   SlotKind.Skin,   "Everyone Else"),
                    Slot("HAIR",   SlotKind.StyleA, "Hair colour"),
                    Slot("FACIAL", SlotKind.StyleB, "Facial colour"),
                    Slot("EXTRAS", SlotKind.StyleC, "Accessory colour"),
                },
                InstinctTab = null,
                PreviewDist = 3.2f, PreviewHeight = 1.0f,
            },

            new SpeciesDef
            {
                Id = 1, Name = "Horse", Plan = BodyPlan.Quadruped, ModelReady = true,
                Blurb = "Quadruped. Straight-line pace.",
                Size = Axis("Withers", "m",  "0.00", 1.35f, 1.60f, 1.85f),
                Mass = Axis("Weight",  "kg", "0",    380f,  500f,  700f),
                VisualScale = 1.18f, VisualGirth = 1.15f,
                // 0.305: a stallion's base is already most of a real one, so the shared ANACONDA
                // ladder took him to 0.90 m - roughly double life. This lands him at 0.550 m, a
                // noticeable +39% over his own base and about a real stallion's average.
                AllowsAdult = true, AdultGrowth = 0.305f,
                Slots = new[]
                {
                    Slot("COAT",     SlotKind.Skin,   "Any colour"),
                    Slot("MANE",     SlotKind.StyleA, "Mane colour"),
                    Slot("MARKINGS", SlotKind.StyleB, "Marking colour"),
                    Slot("TACK",     SlotKind.StyleC, "Tack colour"),
                },
                InstinctTab = null,     // Equine tab removed; no Instinct nodes for species 1
                // THE NOD. Barrel pitches 18 deg nose-down, muzzle drops another 45 on top. That is
                // travel comparable to the biped's 0.61 m fold, spent on the axis a quadruped is short
                // of: it opens a whole band of low balls no biped can reach, which is the horse's
                // identity. Measured after the body reshape, the highest surface anywhere on the nodding
                // head is 1.162 m at default girth and 1.222 m at the top of the Weight slider, so the
                // nod cannot put a ball anywhere near the bar. It is a downward pose by construction.
                //
                // CORRECTION on an earlier note here: this used to claim 18/45 was a hard COLLISION
                // ceiling, because "Head and UpperArm are siblings under the Torso so no joint disables
                // collision between them". That is false. ActiveRagdoll.IgnoreSelfCollisions is a full
                // pairwise sweep over every collider the body owns, so that pair is ignored like all the
                // rest. 18/45 is a taste call with headroom above it, not a limit.
                //
                // These angles are only deliverable because the barrel's joint drive is inertia
                // compensated (BoneSpec.DriveMul). Uncompensated it is a 0.53 s first-order lag
                // against 0.62 s of hang time, so about a third of the pitch never arrived.
                Header = new HeaderAction
                {
                    TorsoDeg = 18f, HeadDeg = 45f,
                    LegRaiseMul = SimConfig.HeaderLegRaiseMul,
                    Ease = SimConfig.HeaderBendEase,
                    // The skull is 1.23 m from the hip pivot against a human head's 0.61, so at the
                    // same Ease rate it sweeps into the ball at twice the speed and the bounce came
                    // out saturating the outgoing cap. 0.60 brings it back off the cap.
                    PaceMul = 0.60f,
                    // A horse's head stands near 1.79 m without leaving the ground. 9 deg of extra
                    // dive is the trade for not having to jump for it.
                    DownDeg = 9f,
                    // 10% off the top of the whole header band, cap included. PaceMul above only
                    // moves headers that arrive below the cap, and a nod off a 1.23 m lever still
                    // reaches it often enough that the quadruped's ceiling read as the human's.
                    SpeedMul = 0.90f,
                },
                // Quadruped framing: pivot at mid-barrel height (the body's own visual centre is
                // near y 0.78 at unit scale) and pushed forward to the middle of the length span.
                //
                // The z span about the body origin runs -0.785 (tail tip) to +1.05 (muzzle, whose box
                // is pitched 90 so its length reads along z), so the midpoint is 0.13 and the half
                // span is 0.918. PreviewHalfW is padded to 1.02 to cover the pelvis rear, which is
                // GIRTH scaled while the framing is height scaled, so it swings back to about 1.00
                // behind the pivot at max weight on the smallest build.
                //
                // PreviewDist is now the VERTICAL fit alone: ear top 1.78 against the 0.78 pivot is a
                // 1.00 half extent, needing 2.61, padded to 2.9. The old 4.6 was inflated purely to
                // survive a 300 px portrait column and left the horse small in frame on every display.
                PreviewDist = 2.9f, PreviewHeight = 0.78f, PreviewZ = 0.13f, PreviewHalfW = 1.02f,
            },

            new SpeciesDef
            {
                Id = 2, Name = "Elephant", Plan = BodyPlan.Quadruped, ModelReady = true,
                Blurb = "Quadruped. Massive and immovable.",
                Size = Axis("Shoulder", "m",  "0.00", 2.40f, 3.20f, 4.00f),
                Mass = Axis("Mass",     "kg", "0",    2000f, 3500f, 6000f),
                VisualScale = 1.34f, VisualGirth = 1.40f,
                // 0.327: same reasoning as the horse. Full ladder put him at 1.06 m; this gives
                // 0.660 m, +42% over his own base. See AdultGrowth for why that is measured against
                // this game's base rather than against a real bull.
                AllowsAdult = true, AdultGrowth = 0.327f,
                Slots = new[]
                {
                    Slot("HIDE",  SlotKind.Skin,   "Any colour"),
                    Slot("EARS",  SlotKind.StyleA, "Ear tint"),
                    Slot("TUSKS", SlotKind.StyleB, "Tusk colour"),
                    Slot("TACK",  SlotKind.StyleC, "Tack colour"),
                },
                InstinctTab = null,     // Pachyderm tab removed; no Instinct nodes for species 2
                // THE TRUNK LIFT. HeadDeg -50 swings the trunk up and forward; TorsoDeg +12 plants the
                // barrel nose-DOWN under it. The trunk tip climbs 0.42 m from rest and the contact
                // surface lands at 1.989 m, above a JUMPING human's 1.72. The animal reads as dropping
                // its head and shoulders to sling the trunk up into the ball.
                //
                // THE BARREL DOES NOT REAR, and that is a correction, not a preference. A header has
                // to fit UNDER the 2.44 m bar with a whole ball diameter of room, because the +Z end
                // of the arena has no wall and a high header scores as a MISS, so the contact surface
                // has a hard 2.22 m ceiling. Two facts kill the rear against that number: at the top
                // of the Weight slider this elephant's skull top is already 2.177 m standing still,
                // and the head sits 1.39 m forward of the hip pivot so even 12 deg of barrel swings
                // it about 0.29 m. Rearing 18 put the contact at 3.10 m. Planting +12 instead buys
                // the room back (0.16 m of margin at max girth) and costs nothing visually, since the
                // trunk carries the whole read anyway.
                //
                // Ease is halved against the biped's 60 purely as a WEIGHT read, not because the
                // physics needs it. It is affordable: 30 ramps the intent in 0.033 s and the
                // compensated barrel's joint lag adds about 0.1 s, so the pose completes in ~0.13 s
                // against 0.62 s of hang time. Slower than that starts eating the window; the horse
                // keeps the full snap because a nod should look reflexive and a heave should not.
                //
                // No ball rule, which was the tempting mistake here. The contact HEIGHT is the whole
                // payoff and it needs no new physics. Trying to also add vertical to the ball fails on
                // arithmetic, not taste: the incoming vertical term swings wider across the ball
                // velocity slider than the entire under-bar budget, so one constant is a scoop at one
                // setting and a spike into the turf at another.
                //
                // Accepted, and shared with the horse now that both plant instead of one rearing: the
                // landing stumble. A nose-down barrel with the front hooves already at ground level
                // demands penetration, so there is a short window on touchdown where the shoulder
                // digs in. It reads as heaviness.
                Header = new HeaderAction
                {
                    TorsoDeg = 12f, HeadDeg = -50f,
                    LegRaiseMul = SimConfig.HeaderLegRaiseMul,
                    Ease = SimConfig.HeaderBendEase * 0.5f,
                    // The halved Ease already costs it some head speed, but the lever is 1.39 m and
                    // the barrel drive runs to 28x at the top of the Weight slider, which is where
                    // the bounce ran away. 0.65 is the trim; the horse needs slightly more.
                    PaceMul = 0.65f,
                    // Heads from about 2.08 m standing, the highest contact in the game, so it gets
                    // the largest dive. Still speed preserving, so this costs it nothing but aim.
                    DownDeg = 15f,
                    // Same 10% off the top as the horse. Kept identical on purpose: this is a trim on
                    // the quadruped PLAN's advantage at the ceiling, not a per-animal balance knob.
                    SpeedMul = 0.90f,
                },
                // Framing. The trunk tip reaches z +1.26 and the pelvis rear sits at -0.42 in height
                // units, so the pivot slides to 0.34 and the half span is 0.92. PreviewHalfW is 1.00
                // to cover the pelvis, which is GIRTH scaled against a height-scaled pivot and so
                // reaches about 1.03 behind it at default weight.
                //
                // PreviewDist covers the VERTICAL fit alone: the skull and ear tops reach 1.55, only
                // 0.77 above the pivot, so 2.03 suffices and 2.5 pads it. The old 5.2 existed to fit
                // the trunk into a 300 px portrait column; PlayerPreview now raises the distance for
                // that on its own, from the actual viewport aspect.
                PreviewDist = 2.5f, PreviewHeight = 0.78f, PreviewZ = 0.34f, PreviewHalfW = 1.00f,
            },

            new SpeciesDef
            {
                Id = 3, Name = "Gorilla", Plan = BodyPlan.Biped, ModelReady = false,
                Blurb = "Upright brawler. Brutal in the air.",
                Size = Axis("Height", "m",  "0.00", 1.45f, 1.70f, 1.95f),
                Mass = Axis("Weight", "kg", "0",    100f,  160f,  220f),
                VisualScale = 1.08f, VisualGirth = 1.28f,
                Slots = new[]
                {
                    Slot("FUR",    SlotKind.Skin,   "Any colour"),
                    Slot("CREST",  SlotKind.StyleA, "Crest colour"),
                    Slot("FACE",   SlotKind.StyleB, "Face colour"),
                    Slot("EXTRAS", SlotKind.StyleC, "Accessory colour"),
                },
                InstinctTab = "Primate",
                PreviewDist = 3.4f, PreviewHeight = 0.95f,
            },

            new SpeciesDef
            {
                Id = 4, Name = "Ostrich", Plan = BodyPlan.Biped, ModelReady = false,
                Blurb = "Biped. Very fast, very fragile.",
                Size = Axis("Height", "m",  "0.00", 1.90f, 2.20f, 2.60f),
                Mass = Axis("Weight", "kg", "0",    90f,   120f,  160f),
                VisualScale = 1.20f, VisualGirth = 0.80f,
                Slots = new[]
                {
                    Slot("PLUMAGE", SlotKind.Skin,   "Any colour"),
                    Slot("NECK",   SlotKind.StyleA, "Neck colour"),
                    Slot("BEAK",   SlotKind.StyleB, "Beak colour"),
                    Slot("EXTRAS", SlotKind.StyleC, "Accessory colour"),
                },
                InstinctTab = "Ratite",
                PreviewDist = 3.8f, PreviewHeight = 1.05f,
            },
        };
    }
}
