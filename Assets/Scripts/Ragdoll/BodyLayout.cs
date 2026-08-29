using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Collider shape a bone uses. Lifted out of ActiveRagdoll (it was a private nested enum)
    /// so the layout tables below can name it.
    /// </summary>
    public enum ColliderKind { Box, Sphere, CapsuleY }

    /// <summary>
    /// How far a bone's collider is fattened past its visible mesh. Stored as a CLASS, not a
    /// number, because the multiplier depends on the body being built: a keeper's limbs are
    /// thicker than a striker's, and the striker's arms are thicker than the keeper's. Resolved
    /// in ActiveRagdoll.HitboxScale at build time.
    /// </summary>
    public enum HitboxClass { None, Leg, Foot, Arm }

    /// <summary>
    /// Which appearance colour paints a decor piece.
    ///
    /// The three Style slots are not new fields. They are the SAME three (style index, colour) pairs
    /// the customize screen already exposes and the wire already carries, reinterpreted per species
    /// (see the SPECIES REINTERPRETATION table on PlayerAppearance): for a horse they are labelled
    /// MANE / MARKINGS / TACK, for an elephant EARS / TUSKS / TACK. Those pickers existed and drew
    /// nothing, because there was no animal geometry for them to paint. Decor is that geometry.
    /// </summary>
    public enum DecorTint
    {
        Limb,    // the body's own limb material: no tint, matches the bone it hangs off
        Skin,    // PlayerAppearance.Skin           (COAT / HIDE)
        StyleA,  // PlayerAppearance.HairColor      (MANE / EARS)
        StyleB,  // PlayerAppearance.FacialColor    (MARKINGS / TUSKS)
        StyleC,  // PlayerAppearance.AccessoryColor (TACK)
        Dark,    // fixed near-black. Hooves and toenails are keratin, not a player choice.
    }

    /// <summary>
    /// One species appendage: a neck, a trunk, an ear, a hoof.
    ///
    /// Decor is a CHILD of an existing bone and shares that bone's Rigidbody, which is the only way
    /// to add body parts without a 14th <see cref="Bone"/> member (that enum has 469 references).
    /// The precedent is the keeper glove in ActiveRagdoll.AddGlove, which does exactly this.
    ///
    /// Offset and Euler are in the BODY frame (+X right, +Y up, +Z the way the body faces), NOT in
    /// the parent bone's local frame; the build converts with Inverse(the parent's rest rotation).
    /// That is deliberate. The barrel sits at rest (90,0,0), so authoring a horse's neck in the
    /// barrel's local frame would mean writing every number in a frame where +Y is forward and +Z is
    /// straight down, which is how you end up with a neck inside the turf. Every rest rotation in
    /// both tables is axis aligned, so the conversion is an exact axis permutation and the
    /// non-uniform build scale still commutes with it.
    ///
    /// ONE Dims drives BOTH the visible mesh and the collider, so a Solid piece is FLUSH BY
    /// CONSTRUCTION. That is the whole reason decor is a table and not hand-built objects: it makes
    /// "the hitbox matches what you see" a structural property instead of a thing to keep in sync.
    ///
    /// GAMEPLAY, and this is the part that bites: a Solid piece resolves to its PARENT BONE, because
    /// ActiveRagdoll.BoneOf maps it back (see _decorOwner). So a muzzle on the Head counts as a
    /// HEADER and a hoof on a front leg counts as a KICK. Pick the parent for what the piece should
    /// MEAN, not only for where it should sit.
    /// </summary>
    public struct DecorSpec
    {
        public string Name;          // object name, e.g. "D_Neck". Prefixed D_ so it is never a bone.
        public Bone   Parent;        // owning bone: its Rigidbody carries the collider
        public ColliderKind Kind;
        public Vector3 Offset;       // BODY frame, from the PARENT BONE's centre, unit scale
        public Vector3 Euler;        // BODY frame
        public Vector3 Dims;         // same meaning as BoneSpec.Dims, per MakePart
        public bool    Solid;        // true = also emit a collider, flush to Dims
        public DecorTint Tint;

        /// <summary>
        /// Which style SLOT decides whether this piece is drawn, and for which of that slot's option
        /// indices. <see cref="GateMask"/> is a bitmask over the index: bit N set means "draw when the
        /// player has option N picked". Several bits is normal and is how one spec serves several
        /// options (a long tusk can be shared by "Long" and "Banded", with a separate ring spec adding
        /// the band). The option names and their order live in SpeciesCosmetics; they are the same
        /// indices, so the two have to move together.
        ///
        /// GateMask == 0 means UNGATED: always drawn, whatever the player picked. That is anatomy - a
        /// neck, a trunk, a hoof - and it is the default, which is why the encoding uses a zero mask
        /// rather than a nullable slot (SlotKind's own default is Skin, never a style index).
        ///
        /// GATING A SOLID PIECE CHANGES GAMEPLAY. A Solid piece is a collider, so making one optional
        /// makes a cosmetic picker change how wide the animal captures the ball. Every gated piece in
        /// both tables is therefore non-Solid, and where an option genuinely has to alter solid
        /// geometry (the elephant's ear shapes) every variant keeps the SAME outer extent so the
        /// header surface does not move. Do not gate anatomy.
        /// </summary>
        public SlotKind Gate;

        /// <summary>Bitmask of style indices this piece is drawn for. 0 = always. See <see cref="Gate"/>.</summary>
        public int GateMask;

        /// <summary>
        /// Scale this piece's Offset by GIRTH on every axis, instead of by the layout's usual mix.
        ///
        /// This exists because girth and height do not commute and a HEAD is a Sphere. ScaleDims gives
        /// a Sphere radius <c>0.15 * girth</c>, while Off scales an offset <c>(x*g, y*h, z*h)</c>. The
        /// two sliders are independent - girth spans 0.82 to 1.35 of a species' default and height
        /// 0.75 to 1.25 - so h/g ranges over a factor of 2.7. A decal placed 0.15 from the head centre
        /// therefore floats 5 cm off a lean build's skull and vanishes inside a heavy one's. No single
        /// fixed offset can sit on that surface.
        ///
        /// With this set, the offset and the radius carry the same factor, so the piece sits at a
        /// FIXED FRACTION of the radius at every build. That is the only way a face marking stays a
        /// face marking. Use it for anything placed against a Sphere bone or against a girth-scaled
        /// face; leave it off for anything that has to track a height-scaled length, which is why the
        /// neck, muzzle and trunk chains do not use it.
        /// </summary>
        public bool GirthOffset;

        /// <summary>
        /// Frictionless, exactly like <see cref="BoneSpec.Slick"/>, and on a quadruped it is the flag
        /// that actually matters. Grounding is a pelvis SphereCast, so a foot is meant to slide over
        /// the turf rather than catch on it. But a quadruped's LOWEST geometry is decor, not bone: the
        /// hoof and pad boxes hang BELOW the leg capsules whose Slick flag was doing the work, so the
        /// bone flag never touched the surface in contact and every stride caught. Mark the pieces
        /// that reach the ground.
        /// </summary>
        public bool Slick;
    }

    /// <summary>
    /// One bone of a body layout: where it sits, how it is shaped, what it hangs off.
    ///
    /// Positions are metres from the body's base (feet on the ground at y 0), authored at unit
    /// scale and facing +Z. ActiveRagdoll.Off scales them by the build's height/girth and the
    /// build rotates them by the body's facing.
    /// </summary>
    public struct BoneSpec
    {
        public Bone Bone;
        public Bone Parent;        // used for target nesting, physics nesting AND the joint
        public bool IsRoot;        // the pelvis: no joint, free root, driven by the balance PD

        public Vector3  Pos;       // centre offset from basePos
        public Vector3? TargetPos; // target-skeleton override; null = use Pos (only the feet differ)

        /// <summary>
        /// Rest rotation in the BODY frame. World rotation at build is facing * Euler(this).
        /// Zero for every biped bone, which is why the human build never needed it. The
        /// quadruped pitches its torso 90 deg to lie the barrel down.
        /// </summary>
        public Vector3 RestEuler;

        public ColliderKind Kind;
        public Vector3      Dims;   // see MakePart: meaning differs per kind
        public float        Mass;

        public bool        WearsJersey;  // torso only: uses torsoMat + the jersey-UV box

        /// <summary>
        /// WHICH faces of the jersey box carry the painted art. Only read when WearsJersey.
        /// Default (Chest) is the upright biped torso. A quadruped barrel rests pitched 90 deg, so
        /// its local +Z is the BELLY and its local -Z the SPINE; left at Chest it paints the whole
        /// design where nobody can see it and leaves the flanks solid. See Make.JerseyFaces.
        /// </summary>
        public Make.JerseyFaces JerseyFaces;

        public HitboxClass Hitbox;
        public bool        Slick;        // frictionless, for a bone that drags on the turf

        public Vector3 JointAnchor;      // in the CHILD's local frame, unscaled

        /// <summary>
        /// Inertia compensation for this bone's joint drive, as a multiple of the BIPED reference.
        /// 0 - the default, and every biped bone - means 1x, so today's drive is untouched.
        /// ActiveRagdoll.Build multiplies this base by _hScale^2 * _massMul, because a bone's
        /// rotational inertia goes as mass times lever squared.
        ///
        /// It exists because SimConfig.JointSpring and JointDamper are CONSTANTS, so drive
        /// authority per unit of inertia falls off as a rig gets longer or heavier. The quadruped
        /// barrel carries 8.3x the biped torso's inertia about its hip pivot at unit scale (22 kg
        /// of barrel plus a head on a 1.03 lever plus four front-leg segments, against 16 kg of
        /// chest and a head on a 0.61 lever), which the build scale takes to 11.6x on a horse and
        /// 25.5x on an elephant at the top of the Weight slider.
        ///
        /// Two things break if it is left uncompensated, and neither is subtle:
        ///
        ///   SAG. Gravity here is 2x real (SimConfig.Gravity -19.6), and the static drive error
        ///   that leaves on the barrel is 5.2 deg of permanent nose-down droop on a horse and
        ///   10.1 deg on a heavy elephant. An elephant asked to pitch 12 deg would have arrived
        ///   at almost nothing, because most of the command would have gone on cancelling droop.
        ///
        ///   AIRBORNE LAG. Striker.AirPitchControl calls SpinWholeBody every airborne frame, which
        ///   hard-assigns the SAME angular velocity to every bone, so the drive can never
        ///   accumulate relative velocity and each physics step only closes 0.5*(K/I)*dt^2 of the
        ///   remaining error. That is a first-order lag: 0.046 s on the human torso, but 0.53 s on
        ///   a horse barrel and 1.17 s on a heavy elephant's, against 0.62 s of hang time. An
        ///   airborne pose command landed at two thirds strength on a horse and 40% on an elephant.
        ///
        /// Spring, damper AND max force all scale by it together, which is the point: multiplying
        /// K and D by the same inertia ratio leaves the natural frequency and the damping ratio
        /// exactly where the biped has them. A compensated barrel behaves like the human torso,
        /// not like a stiffer, ringier version of itself.
        /// </summary>
        public float DriveMul;
    }

    /// <summary>One whole skeleton: the bones plus the few body-wide constants derived from them.</summary>
    public class BodyLayoutDef
    {
        public BoneSpec[] Bones;

        /// <summary>
        /// Scale local Z by the build HEIGHT instead of the girth. A quadruped's front-to-back
        /// span is length, and the barrel's length comes from its box dims.y (which scales by
        /// height), so the legs and head have to track height too or they drift out from under
        /// the barrel ends on a wide build. A biped has no length axis and keeps girth.
        /// </summary>
        public bool LengthAlongHeight;

        // Pelvis ground SphereCast (see ActiveRagdoll.UpdateGrounded). The distance is the
        // pelvis rest height plus a small margin, so it is per-plan.
        public float GroundProbeRadius;
        public float GroundProbeDist;

        /// <summary>
        /// Bones that get a KickDetector. Strong side first, matching the order the four
        /// detector sites used to hardcode.
        /// </summary>
        public Bone[] StrikeBones;

        /// <summary>
        /// Every bone that counts as a KICKING limb, which is a SUPERSET of StrikeBones: the
        /// detectors only need the two contact bones per side, but the ball code classifies a
        /// strike off any part of the limb (a biped scores off a thigh).
        ///
        /// This exists because BallController used to test the collider's name prefix
        /// ("P_Foot" / "P_Calf" / "P_Thigh"), which silently mis-classified every quadruped. The
        /// repose keeps the bone NAMES and changes what they ARE: a quadruped kicks with its FRONT
        /// legs, which are the UpperArm/Forearm bones, so no leg-shaped prefix ever matched and
        /// every hoof contact fell through to the dead-trap branch.
        /// </summary>
        public Bone[] LegBones;

        /// <summary>
        /// The limb the LMB / RMB leg-raise lifts, per side, as {upper, lower}. A biped raises its
        /// leg (thigh, calf); a quadruped raises its FRONT leg (upper arm, forearm), because the
        /// front legs are the strike limbs and the hind legs cannot reach a ball in front of the
        /// body. Without this the raise on a horse lifted the hocks and the volley gate, which
        /// requires the raise button for the STRUCK side, could never fire.
        /// </summary>
        public Bone[] RaiseL, RaiseR;

        /// <summary>
        /// Species appendages hung off the bones: a horse's neck and muzzle, an elephant's trunk and
        /// ears. Null means none, which is the human case and every placeholder species.
        ///
        /// This is per SPECIES, not per BodyPlan, which is why <see cref="BodyLayout.ForSpecies"/>
        /// clones the plan table before filling it in. A horse and an elephant share the quadruped
        /// SKELETON and share nothing about their silhouette.
        /// </summary>
        public DecorSpec[] Decor;

        public bool IsLegBone(Bone b)
        {
            if (LegBones == null) return false;
            for (int i = 0; i < LegBones.Length; i++) if (LegBones[i] == b) return true;
            return false;
        }

        public int IndexOf(Bone b)
        {
            for (int i = 0; i < Bones.Length; i++) if (Bones[i].Bone == b) return i;
            return -1;
        }

        /// <summary>
        /// Build-scale a layout offset. Heights (y) scale with the build height and lateral spacing
        /// (x) with girth, so a wider body's limbs sit further out. Z is girth for a biped but HEIGHT
        /// for a quadruped (see <see cref="LengthAlongHeight"/>). Default build = 1.
        ///
        /// Lives here rather than in ActiveRagdoll because BodyLayout.RootDriveMul needs the same
        /// arithmetic on a layout it is not building. ActiveRagdoll.Off forwards to it, so there is
        /// still one implementation.
        /// </summary>
        public Vector3 Off(Vector3 v, float hScale, float gScale)
            => new Vector3(v.x * gScale, v.y * hScale, v.z * (LengthAlongHeight ? hScale : gScale));

        int[] _parentBone;

        /// <summary>
        /// Parent BONE INDEX per bone, -1 for the root. Built once, lazily. Used by the display FK
        /// (ActiveRagdoll.DisplayPose), which walks the hierarchy by bone index rather than by
        /// table row. Bones a layout omits stay -1, which the FK treats as a root.
        /// </summary>
        public int[] ParentByBone
        {
            get
            {
                if (_parentBone == null)
                {
                    var p = new int[(int)Bone.Count];
                    for (int i = 0; i < p.Length; i++) p[i] = -1;
                    for (int i = 0; i < Bones.Length; i++)
                        p[(int)Bones[i].Bone] = Bones[i].IsRoot ? -1 : (int)Bones[i].Parent;
                    _parentBone = p;
                }
                return _parentBone;
            }
        }
    }

    /// <summary>
    /// The skeleton tables, one per BodyPlan.
    ///
    /// The 13-bone <see cref="Bone"/> enum is fixed (it has hundreds of references across the
    /// pose, celebration and keeper code), so a quadruped is not a new skeleton: it is the same
    /// 13 bones REPOSED. The torso becomes a horizontal barrel, the legs become the hind legs,
    /// and the arms become the front legs. Because every gameplay consumer touches bones only
    /// through additive SetPoseOverride offsets, the run cycle, poses and keeper logic all keep
    /// working on the reposed body with no changes.
    ///
    /// Both tables are authored in Bone enum order and every parent has a strictly lower enum
    /// index than its child, so one forward pass builds the targets, the parts, the joints and
    /// the display FK.
    /// </summary>
    public static class BodyLayout
    {
        public static BodyLayoutDef For(BodyPlan plan)
            => plan == BodyPlan.Quadruped ? Quadruped : Biped;

        /// <summary>
        /// Build-scale a Dims triple. Height on the length axis, girth on the two cross axes, which
        /// is per collider kind. Shared by MakePart, AddDecor and RootInertia so a bone, an appendage
        /// hung off it and the inertia estimate cannot scale differently.
        /// </summary>
        public static Vector3 ScaleDims(ColliderKind kind, Vector3 dims, float hScale, float gScale)
        {
            if (kind == ColliderKind.CapsuleY)
                return new Vector3(dims.x * gScale, dims.y * hScale, dims.z);
            if (kind == ColliderKind.Sphere)
                return new Vector3(dims.x * gScale, dims.y * gScale, dims.z);
            return new Vector3(dims.x * gScale, dims.y * hScale, dims.z * gScale); // Box
        }

        /// <summary>
        /// Per-axis gain correction for the FREE-ROOT balance torque, expressed as "how much harder
        /// this layout has to be driven than a biped at the same build". <see cref="Vector3.one"/>
        /// means no correction.
        ///
        /// THE BUG THIS FIXES. ActiveRagdoll drives the pelvis toward FacingRotation with
        /// JointMath.DriveTowardRotation, which applies ForceMode.Acceleration. That mode ignores the
        /// inertia tensor, so Unity converts the commanded angular acceleration into a torque using
        /// the PELVIS tensor alone - but the pelvis is joint-welded to the rest of the body (joint
        /// spring 6500 times a barrel's DriveMul is ~65000 N m/rad against the balance loop's ~17, so
        /// at rest it is rigid), and that torque has to swing the WHOLE assembly. The achieved
        /// acceleration is therefore the commanded one times I_root / I_total, per axis.
        ///
        /// A biped's limbs all sit close to its own yaw axis, so its assembly is only 6.6x its pelvis
        /// and the loss is mild. A quadruped hangs a horizontal barrel, a head and four legs at +-0.4 m
        /// of body length off the same free root, and its assembly is 135x the pelvis on a horse and
        /// 70x on an elephant. Damping ratio scales as the square root of that ratio, so the nominal
        /// 0.85 lands at (measured on the yaw axis, at the default build of each):
        ///
        ///     biped     r 0.1508   zeta 0.330   period 1.7 s   62% decayed per swing
        ///     horse     r 0.0074   zeta 0.073   period 7.6 s   37% decayed per swing
        ///     elephant  r 0.0143   zeta 0.102   period 5.5 s   47% decayed per swing
        ///
        /// A seven-second period losing a third of its amplitude per swing is a body still visibly
        /// rocking half a minute after it stopped moving, which is exactly the standing sway that was
        /// reported. BoneSpec.DriveMul already compensates every JOINT for this same reason; the root
        /// has no joint, so nothing was compensating it. This restores biped PARITY, not critical
        /// damping: a quadruped ends up at the human's zeta 0.330 and settles in about two swings.
        /// Going further would mean raising SimConfig.BalanceDamping, which changes the human.
        ///
        /// WHY A RATIO OF RATIOS, and why per axis:
        ///  - Normalising against the BIPED at the same hScale/gScale is what keeps a human EXACTLY
        ///    unchanged. For a biped the numerator and denominator are the same computation on the
        ///    same table, so the result is 1 by construction at every point on both sliders, not by
        ///    argument. Anchoring to an absolute target instead would have retuned the human, which
        ///    was not asked for.
        ///  - Per axis, because ForceMode.Acceleration resolves componentwise and the assembly's
        ///    inertia is wildly anisotropic: a barrel's roll axis is a fraction of its yaw and pitch.
        ///    A single scalar would over-drive roll by the yaw factor. Note that YAW is the axis that
        ///    matters most here, since FacingRotation is yaw-only, so the sway is a yaw oscillation.
        ///
        /// LIMITS, stated because they are real. This is an ESTIMATE of what Unity computes, so the
        /// correction is the right size rather than exact:
        ///  - Bone colliders only. Decor hung on a bone (a tail on the pelvis, a trunk on the head)
        ///    reshapes the tensor Unity actually divides by and is not modelled.
        ///  - Capsules are treated as solid cylinders, and the assembly is taken about the pelvis
        ///    CENTRE when a standing body's true pivot is nearer the ground. Both of these apply to the
        ///    biped denominator too, so they largely cancel in the ratio.
        ///  - HitboxClass does NOT cancel, and it is the one asymmetry worth naming. A biped's legs and
        ///    arms are FATTENED at build (Leg by SimConfig.LegHitboxScale, Foot by 1.6, Arm by up to
        ///    2.6) while every quadruped bone is HitboxClass.None, so Unity's real biped tensor is
        ///    slightly larger than this estimate and the quadruped's is accurate. Hand-checked as a few
        ///    percent: a thigh's own yaw inertia goes 0.028 to 0.073 at 1.6x, against a 0.589
        ///    parallel-axis term for the same bone, and the arms are 0.3 kg. It biases the correction
        ///    a few percent low, which is the harmless direction.
        /// massMul is deliberately not a parameter: it multiplies every mass in both the numerator and
        /// the denominator and cancels outright.
        /// </summary>
        public static Vector3 RootDriveMul(BodyLayoutDef layout, float hScale, float gScale)
        {
            // A biped (and every species that inherits the biped table unchanged) hands back exactly
            // one, with no float division to round it. Structurally redundant, since the general path
            // would also return one, but it makes the human guarantee bit-exact.
            if (layout == null || ReferenceEquals(layout, Biped)) return Vector3.one;

            RootInertia(layout, hScale, gScale, out Vector3 asmA, out Vector3 rootA);
            RootInertia(Biped,  hScale, gScale, out Vector3 asmB, out Vector3 rootB);

            return new Vector3(Axis(asmA.x, rootA.x, asmB.x, rootB.x),
                               Axis(asmA.y, rootA.y, asmB.y, rootB.y),
                               Axis(asmA.z, rootA.z, asmB.z, rootB.z));

            // (asm/root)_layout / (asm/root)_biped, rearranged to divide once.
            //
            // The zero test is a guard against a bad TABLE: a layout with no root bone, or a zero-mass
            // one. Falling back to 1 leaves such a body behaving exactly as it does today rather than
            // handing it a garbage torque.
            //
            // Both BOUNDS bind on real builds, so they are choices, not typo guards. Measured raw range
            // over the whole height x weight slider box:
            //
            //     horse     pitch 1.15..1.23   YAW 7.20..45.75   roll 0.67..1.19
            //     elephant  pitch 0.88..0.90   YAW 4.05..23.05   roll 0.42..0.69
            //
            // FLOOR of 1: never drive the balance torque WEAKER than a biped's. Both roll axes and the
            // elephant's pitch axis ask for less than 1, because a barrel's roll inertia really is
            // lower relative to its pelvis than a torso's is. Honouring that would be defensible, but
            // it would weaken the one torque holding a body upright to buy nothing anyone asked for, so
            // parity is the floor.
            //
            // CEILING of 24: covers both DEFAULT builds outright (horse 20.4, elephant 10.6) and all
            // but the tall-and-light corner of the horse's box. It exists because of the SOLVER, not
            // the physics. The physics is right at any magnitude, but this torque is sized against the
            // pelvis and only reaches the assembly through the joints, which Unity solves iteratively
            // with a finite iteration count. A large facing error (a player spinning the mouse) times a
            // large multiplier is exactly the input that leaks into the pelvis alone before the joints
            // catch it, which shows up as jitter. Past the ceiling the fix degrades gracefully rather
            // than failing: the clipped horse corner lands at zeta 0.24 instead of 0.33, against the
            // 0.05 it has with no correction at all. Whether 24 is too high is a play-mode question.
            float Axis(float aA, float rA, float aB, float rB)
            {
                if (rA <= 0f || rB <= 0f || aA <= 0f || aB <= 0f) return 1f;
                return Mathf.Clamp((aA * rB) / (rA * aB), 1f, 24f);
            }
        }

        /// <summary>
        /// Diagonal inertia in the BODY frame of (a) the whole welded assembly about the root bone's
        /// centre and (b) the root bone alone about its own centre. Bone colliders only, unit mass
        /// scale (see <see cref="RootDriveMul"/> for what that costs and why it is fine).
        /// </summary>
        static void RootInertia(BodyLayoutDef layout, float hScale, float gScale,
                                out Vector3 assembly, out Vector3 root)
        {
            Vector3 rootPos = Vector3.zero;
            for (int i = 0; i < layout.Bones.Length; i++)
                if (layout.Bones[i].IsRoot) { rootPos = layout.Bones[i].Pos; break; }

            assembly = Vector3.zero;
            root     = Vector3.zero;   // stays zero if the table has no root; Axis reads that as "skip"

            for (int i = 0; i < layout.Bones.Length; i++)
            {
                var s = layout.Bones[i];
                Vector3 own = OwnInertia(s.Kind, ScaleDims(s.Kind, s.Dims, hScale, gScale), s.Mass);

                // The bone's own diagonal sits in the BONE's frame, which for a pitched barrel is not
                // the body frame at all, so rotate it: I_body = R * diag * R^T, and the diagonal of
                // that is sum over k of (R * e_k)_i^2 * diag_k. Every rest euler in both tables is
                // axis aligned, so for the bones that matter this is an exact axis permutation.
                var  q    = Quaternion.Euler(s.RestEuler);
                Vector3 bx = q * Vector3.right, by = q * Vector3.up, bz = q * Vector3.forward;
                Vector3 body = new Vector3(
                    bx.x * bx.x * own.x + by.x * by.x * own.y + bz.x * bz.x * own.z,
                    bx.y * bx.y * own.x + by.y * by.y * own.y + bz.y * bz.y * own.z,
                    bx.z * bx.z * own.x + by.z * by.z * own.y + bz.z * bz.z * own.z);

                if (s.IsRoot) root = body;

                // Parallel axis about the root: I_ii += m * (|d|^2 - d_i^2).
                Vector3 d  = layout.Off(s.Pos - rootPos, hScale, gScale);
                float   r2 = d.sqrMagnitude;
                assembly += body + new Vector3(s.Mass * (r2 - d.x * d.x),
                                               s.Mass * (r2 - d.y * d.y),
                                               s.Mass * (r2 - d.z * d.z));
            }
        }

        /// <summary>Diagonal inertia of one collider about its own centre, in its own frame.</summary>
        static Vector3 OwnInertia(ColliderKind kind, Vector3 d, float mass)
        {
            if (kind == ColliderKind.Sphere)
            {
                // dims.y is the COLLIDER radius when positive and dims.x only the visible one, which is
                // how MakePart picks it. The biped head is the one bone where they differ (0.19 visible,
                // 0.22 collider) and Unity inerts the collider, so match it.
                float r = d.y > 0f ? d.y : d.x;
                float i = 0.4f * mass * r * r;
                return new Vector3(i, i, i);
            }
            if (kind == ColliderKind.CapsuleY)
            {
                // Solid cylinder, local +Y along the length. dims.x is the radius and dims.y the TOTAL
                // height (see the CapsuleCollider note on DecorSpec.Dims).
                float r = d.x, len = d.y;
                float side = mass * (3f * r * r + len * len) / 12f;
                return new Vector3(side, 0.5f * mass * r * r, side);
            }
            // Box, dims are FULL extents.
            return new Vector3(mass * (d.y * d.y + d.z * d.z) / 12f,
                               mass * (d.x * d.x + d.z * d.z) / 12f,
                               mass * (d.x * d.x + d.y * d.y) / 12f);
        }

        /// <summary>
        /// The layout for one species: its BodyPlan's table, then that species' own proportions and
        /// appendages laid over the top.
        ///
        /// A species with no overrides gets the SHARED plan table back, not a copy. That keeps the
        /// human path pointing at the exact same object it always did (so the lazily built
        /// ParentByBone is still built once for it), and it is why the placeholder species cannot
        /// drift: there is nothing per-species to drift.
        ///
        /// Cached, because Build calls this for every body it makes and the override tables allocate.
        /// Indexed by the species byte straight off the wire, so the array is sized for a byte and
        /// an unknown id lands on whatever Species.ById falls back to rather than throwing.
        /// </summary>
        public static BodyLayoutDef ForSpecies(byte speciesId)
        {
            if (_bySpecies == null) _bySpecies = new BodyLayoutDef[256];
            var hit = _bySpecies[speciesId];
            if (hit != null) return hit;

            var sp    = Species.ById(speciesId);
            var plan  = For(sp.Plan);
            var built = SpeciesOverride(sp.Id, plan) ?? plan;
            _bySpecies[speciesId] = built;
            return built;
        }
        static BodyLayoutDef[] _bySpecies;

        /// <summary>
        /// Per-species proportions and decor, or null to use the plan table unchanged.
        ///
        /// Only the two species the picker marks ModelReady get an entry. Gorilla and Ostrich are
        /// deliberately absent: they still show the biped stand-in and their placeholder tag has to
        /// keep meaning something.
        /// </summary>
        static BodyLayoutDef SpeciesOverride(byte id, BodyLayoutDef plan)
        {
            if (id == Species.HorseId)    return Horse(plan);
            if (id == Species.ElephantId) return Elephant(plan);
            return null;
        }

        // ---------------------------------------------------------------- biped
        /// <summary>
        /// The original humanoid rig, transcribed from the literal ActiveRagdoll.Build blocks
        /// so the human body stays bit-identical. Including the quirks: the feet's target bones
        /// sit 0.03 higher than their physics parts, and the head's collider radius (0.22) is
        /// slightly larger than its visible radius (0.19).
        /// </summary>
        public static readonly BodyLayoutDef Biped = new BodyLayoutDef
        {
            LengthAlongHeight = false,
            GroundProbeRadius = 0.18f,
            GroundProbeDist   = 1.05f,
            // Both legs, because a bicycle scored off either foot has to classify.
            StrikeBones = new[] { Bone.FootR, Bone.CalfR, Bone.FootL, Bone.CalfL },
            // Exactly the set the old "P_Foot" / "P_Calf" / "P_Thigh" name test matched, so the
            // human strike classification is unchanged.
            LegBones = new[]
            {
                Bone.FootR, Bone.CalfR, Bone.ThighR,
                Bone.FootL, Bone.CalfL, Bone.ThighL,
            },
            RaiseL = new[] { Bone.ThighL, Bone.CalfL },
            RaiseR = new[] { Bone.ThighR, Bone.CalfR },
            Bones = new[]
            {
                Root(Bone.Pelvis, V(0f, 1.02f, 0f), ColliderKind.Box, V(0.32f, 0.20f, 0.20f), 12f),

                // Only the torso wears the jersey, so the painted kit does not bleed onto the
                // shorts or the head.
                B(Bone.Torso, Bone.Pelvis, V(0f, 1.34f, 0f),
                  ColliderKind.Box, V(0.36f, 0.46f, 0.22f), 16f, V(0f, -0.23f, 0f), jersey: true),

                // Head hitbox is only slightly bigger than the drawn head and CENTRED on it, so
                // it lines up with the shape instead of reaching out in front.
                B(Bone.Head, Bone.Torso, V(0f, 1.72f, 0f),
                  ColliderKind.Sphere, V(0.19f, 0.22f, 0f), 4.5f, V(0f, -0.14f, 0f)),

                B(Bone.ThighL, Bone.Pelvis, V(-0.11f, 0.73f, 0f),
                  ColliderKind.CapsuleY, V(0.09f, 0.44f, 0f), 7f, V(0f, 0.22f, 0f), HitboxClass.Leg),
                B(Bone.ThighR, Bone.Pelvis, V(0.11f, 0.73f, 0f),
                  ColliderKind.CapsuleY, V(0.09f, 0.44f, 0f), 7f, V(0f, 0.22f, 0f), HitboxClass.Leg),

                B(Bone.CalfL, Bone.ThighL, V(-0.11f, 0.33f, 0f),
                  ColliderKind.CapsuleY, V(0.075f, 0.42f, 0f), 4f, V(0f, 0.21f, 0f), HitboxClass.Leg),
                B(Bone.CalfR, Bone.ThighR, V(0.11f, 0.33f, 0f),
                  ColliderKind.CapsuleY, V(0.075f, 0.42f, 0f), 4f, V(0f, 0.21f, 0f), HitboxClass.Leg),

                // Small low-profile feet with a much larger collider so the ball connects, and
                // frictionless so they slide over the turf instead of catching (grounding is a
                // pelvis SphereCast, not foot contact).
                B(Bone.FootL, Bone.CalfL, V(-0.11f, 0.06f, 0.06f),
                  ColliderKind.Box, V(0.09f, 0.05f, 0.17f), 1.5f, V(0f, 0.16f, -0.06f),
                  HitboxClass.Foot, slick: true, targetPos: V(-0.11f, 0.09f, 0.06f)),
                B(Bone.FootR, Bone.CalfR, V(0.11f, 0.06f, 0.06f),
                  ColliderKind.Box, V(0.09f, 0.05f, 0.17f), 1.5f, V(0f, 0.16f, -0.06f),
                  HitboxClass.Foot, slick: true, targetPos: V(0.11f, 0.09f, 0.06f)),

                // Arms: thin capsules that weigh almost nothing so they barely affect the body's
                // momentum, wrapped in a much fatter hitbox so the ball cannot phase through.
                B(Bone.UpperArmL, Bone.Torso, V(-0.26f, 1.40f, 0f),
                  ColliderKind.CapsuleY, V(0.05f, 0.30f, 0f), 0.3f, V(0f, 0.17f, 0f), HitboxClass.Arm),
                B(Bone.UpperArmR, Bone.Torso, V(0.26f, 1.40f, 0f),
                  ColliderKind.CapsuleY, V(0.05f, 0.30f, 0f), 0.3f, V(0f, 0.17f, 0f), HitboxClass.Arm),
                B(Bone.ForearmL, Bone.UpperArmL, V(-0.26f, 1.08f, 0f),
                  ColliderKind.CapsuleY, V(0.045f, 0.30f, 0f), 0.25f, V(0f, 0.16f, 0f), HitboxClass.Arm),
                B(Bone.ForearmR, Bone.UpperArmR, V(0.26f, 1.08f, 0f),
                  ColliderKind.CapsuleY, V(0.045f, 0.30f, 0f), 0.25f, V(0f, 0.16f, 0f), HitboxClass.Arm),
            },
        };

        // ------------------------------------------------------------ quadruped
        /// <summary>
        /// Horse and elephant. The barrel is CENTRED front to back (it spans z -0.40 to +0.44)
        /// so the species preview spins in place instead of orbiting, which puts the pelvis at
        /// the hips about 0.4 behind the visual centre. Camera follow and the radar track the
        /// pelvis, which is what a real quadruped rig does anyway.
        ///
        /// The pelvis stays UPRIGHT and stays the free root, so the upright lock, the balance PD
        /// torque, the locomotion force and the dive yaw lock are all untouched. Only the barrel
        /// hanging off it is horizontal.
        ///
        /// Total mass is about 73 against the human's 58.6, roughly 1.24x, deliberately not 2x:
        /// SimConfig.JointSpring and JointMaxForce do not scale with mass, so heavy bones make
        /// the joints relatively weaker and the animal sags. Heaviness is expressed through the
        /// Weight slider (_massMul) instead.
        /// </summary>
        public static readonly BodyLayoutDef Quadruped = new BodyLayoutDef
        {
            LengthAlongHeight = true,
            GroundProbeRadius = 0.18f,
            GroundProbeDist   = 0.95f,   // pelvis rest height 0.92 + margin
            // FRONT hooves strike. The hind legs sit behind the body under this repose, while
            // Dribble soft-magnets the ball to the FRONT and the shot aims forward, so a front
            // hoof is the only contact that works with the existing ball handling. Hoof first,
            // mirroring the biped's foot-first order.
            StrikeBones = new[] { Bone.ForearmR, Bone.UpperArmR, Bone.ForearmL, Bone.UpperArmL },
            // The FRONT legs, and ONLY those. The hind legs (Thigh/Calf/Foot) are deliberately
            // absent: they sit behind the body under this repose, so a hind contact is a scrappy
            // backwards knock and classifying it as a clean strike would let a horse shoot
            // backwards out of its own hocks.
            LegBones = new[]
            {
                Bone.ForearmR, Bone.UpperArmR,
                Bone.ForearmL, Bone.UpperArmL,
            },
            // The raise lifts the FRONT legs. Their rest rotation is upright in the body frame, the
            // same as a biped thigh, so RaiseLeg's negative-X swing throws the lower end forward and
            // up into a paw exactly as it throws a human shin forward. No new maths, just new bones.
            RaiseL = new[] { Bone.UpperArmL, Bone.ForearmL },
            RaiseR = new[] { Bone.UpperArmR, Bone.ForearmR },
            Bones = new[]
            {
                // Hips at the rear, still upright, still the free root.
                Root(Bone.Pelvis, V(0f, 0.92f, -0.40f), ColliderKind.Box, V(0.34f, 0.26f, 0.30f), 11f),

                // THE BARREL. Rest euler (90,0,0) pitches the box so its local +Y becomes the
                // length axis and its local +Z becomes the vertical depth. Since MakePart scales
                // a box as (x*girth, y*height, z*girth), barrel LENGTH then tracks height and
                // barrel DEPTH tracks girth, which is the coupling we want. Dims read as
                // width 0.40, length 0.84, depth 0.34; the barrel top (withers) lands near 1.19.
                // The joint anchor's local -Y is rearward on the pitched barrel.
                //
                // driveMul 8.3 is the ONLY bone in either table that needs one, and it is measured,
                // not tuned. Inertia about the hip pivot at (0, 0.98, -0.38): the 22 kg barrel box
                // contributes 22/12*(0.84^2+0.34^2) + 22*(0.04^2+0.40^2) = 5.06, the 4.5 kg head on
                // a 1.025 lever adds 4.81, and the four front-leg segments add 14.92, for
                // 24.8 kg*m^2 against the biped torso's 2.98. See BoneSpec.DriveMul for why leaving
                // that uncompensated both sags the barrel and swallows most of an airborne pose.
                B(Bone.Torso, Bone.Pelvis, V(0f, 1.02f, 0.02f),
                  ColliderKind.Box, V(0.40f, 0.84f, 0.34f), 22f, V(0f, -0.40f, 0.04f),
                  jersey: true, jerseyFaces: Make.JerseyFaces.Flank,
                  rest: V(90f, 0f, 0f), driveMul: 8.3f),

                // Up and forward of the shoulders, seated straight into the barrel's front top.
                // There is no neck bone (that would need a 14th Bone member), so the head is
                // placed close enough that the gap does not read.
                B(Bone.Head, Bone.Torso, V(0f, 1.34f, 0.58f),
                  ColliderKind.Sphere, V(0.21f, 0.25f, 0f), 4.5f, V(0f, -0.13f, -0.13f)),

                // HIND legs (the biped's legs), under the barrel's rear end.
                B(Bone.ThighL, Bone.Pelvis, V(-0.16f, 0.65f, -0.40f),
                  ColliderKind.CapsuleY, V(0.105f, 0.40f, 0f), 5.5f, V(0f, 0.20f, 0f), HitboxClass.Leg),
                B(Bone.ThighR, Bone.Pelvis, V(0.16f, 0.65f, -0.40f),
                  ColliderKind.CapsuleY, V(0.105f, 0.40f, 0f), 5.5f, V(0f, 0.20f, 0f), HitboxClass.Leg),

                B(Bone.CalfL, Bone.ThighL, V(-0.16f, 0.30f, -0.40f),
                  ColliderKind.CapsuleY, V(0.085f, 0.38f, 0f), 3f, V(0f, 0.19f, 0f), HitboxClass.Leg),
                B(Bone.CalfR, Bone.ThighR, V(0.16f, 0.30f, -0.40f),
                  ColliderKind.CapsuleY, V(0.085f, 0.38f, 0f), 3f, V(0f, 0.19f, 0f), HitboxClass.Leg),

                B(Bone.FootL, Bone.CalfL, V(-0.16f, 0.055f, -0.38f),
                  ColliderKind.Box, V(0.13f, 0.05f, 0.17f), 1.2f, V(0f, 0.05f, -0.02f),
                  HitboxClass.Foot, slick: true),
                B(Bone.FootR, Bone.CalfR, V(0.16f, 0.055f, -0.38f),
                  ColliderKind.Box, V(0.13f, 0.05f, 0.17f), 1.2f, V(0f, 0.05f, -0.02f),
                  HitboxClass.Foot, slick: true),

                // FRONT legs (the biped's arms), under the barrel's front end. Their rest
                // rotation is upright in the body frame, so local +X is the body's right and a
                // positive X offset swings the lower end backward exactly like a leg. That means
                // the striker's existing arm gait animates them as a correct fore/aft stride with
                // no change to the run cycle.
                //
                // HitboxClass.Leg, NOT Arm: the arm class exists because human arms are razor
                // thin (0.05 radius), and its 2.6x striker multiplier on an already leg-thick
                // 0.10 radius would give the STRIKING limb a 0.26 collider that grabs the ball
                // from an absurd distance. Leg's 1.6x matches the biped's striking foot.
                B(Bone.UpperArmL, Bone.Torso, V(-0.17f, 0.62f, 0.40f),
                  ColliderKind.CapsuleY, V(0.10f, 0.44f, 0f), 5f, V(0f, 0.22f, 0f), HitboxClass.Leg),
                B(Bone.UpperArmR, Bone.Torso, V(0.17f, 0.62f, 0.40f),
                  ColliderKind.CapsuleY, V(0.10f, 0.44f, 0f), 5f, V(0f, 0.22f, 0f), HitboxClass.Leg),

                // Slick like the hind hooves: these reach the ground, and grounding is a pelvis
                // SphereCast, so a front leg catching on turf would only fight locomotion.
                B(Bone.ForearmL, Bone.UpperArmL, V(-0.17f, 0.20f, 0.40f),
                  ColliderKind.CapsuleY, V(0.085f, 0.40f, 0f), 3f, V(0f, 0.20f, 0f),
                  HitboxClass.Leg, slick: true),
                B(Bone.ForearmR, Bone.UpperArmR, V(0.17f, 0.20f, 0.40f),
                  ColliderKind.CapsuleY, V(0.085f, 0.40f, 0f), 3f, V(0f, 0.20f, 0f),
                  HitboxClass.Leg, slick: true),
            },
        };

        // -------------------------------------------------------------- species
        /// <summary>
        /// THE HORSE. The quadruped table with equine proportions, plus a neck, muzzle, ears, mane,
        /// tail and four hooves.
        ///
        /// Three things are going on here and they are not independent, so they are worth stating
        /// together:
        ///
        /// 1. PROPORTIONS. A horse is NARROW and LONG. The barrel goes from 0.40 wide x 0.84 long x
        ///    0.34 deep to 0.34 x 0.90 x 0.46, which is a deeper chest on a slimmer frame. The legs
        ///    get thinner (thigh radius 0.105 to 0.095, forearm 0.085 to 0.088 visible but see below),
        ///    the hind stance widens to 0.175 and the FRONT stance NARROWS to 0.145. That last number
        ///    is not cosmetic: see the nutmeg note.
        ///
        /// 2. FLUSH HITBOXES. Every bone here is HitboxClass.None (see Flush), so the collider is the
        ///    visible mesh exactly. The plan table fattened the legs 1.6x so the ball would connect;
        ///    the honest version has to get that reach back from real geometry, so the visible legs
        ///    are GROWN and a solid hoof is bolted on the bottom of each one. Net capture width still
        ///    drops: forearm -35.3%, upper arm -34.4%, head -40.0%. A horse's legs and head genuinely
        ///    ARE slim, so that is the price of the hitbox matching the animal.
        ///
        /// 3. THE NUTMEG CHANNEL. Flush colliders on a four-legged body open a hole a human body does
        ///    not have: the gap under the barrel, between the front and hind legs. With the plan's
        ///    stance the widest clear gap ran past the 0.22 m ball diameter, so a ball could be
        ///    threaded straight through the horse. Closed with geometry rather than an invisible
        ///    blocker: widest clear gap is now 0.1075 / 0.1311 / 0.1770 m at min / default / max
        ///    girth, all under a ball.
        ///
        /// The head shrinks to a 0.15 radius skull because the NECK and MUZZLE now carry the shape.
        /// Its joint anchor moves to (0, -0.31, -0.25), which puts the poll exactly at the barrel's
        /// front-top corner, where a real neck attaches. Header numbers (18 / 45) are unchanged: the
        /// nod is downward, so it cannot put the ball over the bar.
        /// </summary>
        static BodyLayoutDef Horse(BodyLayoutDef plan)
        {
            var d = Clone(plan);
            Flush(d);

            // Hips: narrower and taller than the plan's, to sit inside a slim rump.
            Set(d, Bone.Pelvis, dims: V(0.30f, 0.30f, 0.32f));

            // Barrel: slimmer, longer, deeper. The anchor tracks the extra length so the hip joint
            // still lands exactly on the barrel's rear face ((0.02 - 0.45) * 1.18 = -0.507).
            // driveMul rises 8.3 -> 9.93 because the reshaped barrel and the moved head change the
            // inertia about that pivot. See BoneSpec.DriveMul.
            Set(d, Bone.Torso, dims: V(0.34f, 0.90f, 0.46f),
                anchor: V(0f, -0.45f, 0.04f), driveMul: 9.93f);

            // Skull only. Lifted and pushed forward to sit at the top of the neck, not on it.
            Set(d, Bone.Head, pos: V(0f, 1.52f, 0.72f), dims: V(0.15f, 0.15f, 0f),
                anchor: V(0f, -0.31f, -0.25f));

            // HIND legs: thinner, stance widened to 0.175.
            Set(d, Bone.ThighL, pos: V(-0.175f, 0.65f, -0.40f), dims: V(0.095f, 0.40f, 0f));
            Set(d, Bone.ThighR, pos: V(0.175f, 0.65f, -0.40f), dims: V(0.095f, 0.40f, 0f));
            Set(d, Bone.CalfL,  pos: V(-0.175f, 0.30f, -0.40f), dims: V(0.075f, 0.38f, 0f));
            Set(d, Bone.CalfR,  pos: V(0.175f, 0.30f, -0.40f), dims: V(0.075f, 0.38f, 0f));
            // Pasterns, not feet. The visible hoof is the D_HoofH decor bolted on top of these.
            Set(d, Bone.FootL,  pos: V(-0.175f, 0.055f, -0.38f), dims: V(0.11f, 0.07f, 0.13f));
            Set(d, Bone.FootR,  pos: V(0.175f, 0.055f, -0.38f), dims: V(0.11f, 0.07f, 0.13f));

            // FRONT legs (the strike limbs): FATTER than the plan's, to claw back some of the reach
            // the flush hitbox costs, and the stance NARROWS to 0.145 to close the nutmeg channel.
            Set(d, Bone.UpperArmL, pos: V(-0.145f, 0.62f, 0.40f), dims: V(0.105f, 0.44f, 0f));
            Set(d, Bone.UpperArmR, pos: V(0.145f, 0.62f, 0.40f), dims: V(0.105f, 0.44f, 0f));
            Set(d, Bone.ForearmL,  pos: V(-0.145f, 0.20f, 0.40f), dims: V(0.088f, 0.40f, 0f));
            Set(d, Bone.ForearmR,  pos: V(0.145f, 0.20f, 0.40f), dims: V(0.088f, 0.40f, 0f));

            d.Decor = new[]
            {
                // NECK. Runs from the barrel's front-top up to the skull. Its euler is +38.9, and the
                // sign matters: a POSITIVE X euler hangs a capsule's low end down-and-BACK, which is
                // exactly what a neck does from the head's point of view. It is SOLID, so the neck
                // heads the ball, which is the read a player expects.
                D("D_Neck",  Bone.Head, ColliderKind.CapsuleY,
                  V(0f, -0.155f, -0.125f), 38.9f, V(0.115f, 0.46f, 0f), true, DecorTint.Skin),

                // MUZZLE. A box laid along the body's +Z by its 90 euler, so it reads as a long jaw
                // rather than a snout. SOLID: this is the horse's header surface, and the header aid
                // (18 / 45) swings it down into the ball.
                D("D_Muzzle", Bone.Head, ColliderKind.Box,
                  V(0f, -0.045f, 0.20f), 90f, V(0.115f, 0.26f, 0.115f), true, DecorTint.Skin),

                // EARS. Slight backward tilt (-10) so they read as pricked forward off a nodding head.
                D("D_EarL", Bone.Head, ColliderKind.CapsuleY,
                  V(-0.075f, 0.145f, -0.03f), -10f, V(0.032f, 0.17f, 0f), true, DecorTint.Skin),
                D("D_EarR", Bone.Head, ColliderKind.CapsuleY,
                  V(0.075f, 0.145f, -0.03f), -10f, V(0.032f, 0.17f, 0f), true, DecorTint.Skin),

                // NO MANE ROW, deliberately. The mane is real simulated HAIR now, built by
                // Cosmetics.AttachAppearance from the same catalog, atlas and cards a human's hair
                // uses, on a rotation-only HairSim anchor tilted by Cosmetics.ManeTiltDeg to match
                // this neck's 38.9 pitch so the strands fall along the crest instead of off the face.
                // KEEP THAT CONSTANT EQUAL TO D_Neck's EULER.
                //
                // A flat crest box here would draw underneath the cards and double the mane, so it is
                // gone rather than gated. Hair style 0 is Bald, which reads as a roached mane, so the
                // "index 0 draws nothing" convention survives the move.

                // NO TAIL ROW either, and for the same reason. The tail is simulated HAIR too, built
                // by Cosmetics.AttachAppearance on a HairSim anchored to this Pelvis, from the same
                // catalog material, atlas and cards as the mane, so mane and tail always share a
                // colour. A capsule here would draw underneath the cards and double the tail, so it is
                // gone rather than gated.
                //
                // Two differences from the mane, both deliberate and both worth knowing before anyone
                // tries to "finish" this by pointing the tail at the style picker.
                //
                // The anchor is UNROTATED. HairSim's BackCluster root mode already gathers its strands
                // up-and-back at about 55 degrees off vertical, which is exactly where a dock sits, so
                // unlike ManeTiltDeg there is no tilt constant here to keep equal to a decor euler.
                //
                // And the tail is NOT the player's picked style. A style IS its RootMode, and Crown,
                // Ring, Strip and FrontSweep all scatter roots over the whole sphere or the FRONT of
                // it: on a head that reads as hair, on a PELVIS it sprouts from the belly and both
                // flanks. So one fixed BackCluster def draws instead, always, and only the COLOUR
                // follows the mane. A bald mane therefore still leaves a tail, which is the same call
                // the elephant's ears made - a missing body part reads as a broken model, not a choice.

                // SADDLE PAD (girth strap). A BAND, not a saddle: a Box pitched 90 so its 0.36 width
                // and 0.48 height both scale by GIRTH while only its 0.12 thickness scales by height.
                // That is what keeps it wrapped on the barrel at every girth. A piece placed by a
                // height-scaled offset against a girth-scaled surface drifts off the body as the
                // Weight slider moves, which is how the first version ended up floating.
                //
                // NOW GATED to TACK option 4, and NO LONGER SOLID. Two reasons, both load-bearing.
                // Solid: gating a collider would let a cosmetic picker change how wide the horse
                // captures the ball, and a cloth strap should not be a collider anyway. The barrel
                // underneath is flush and solid, so the only consequence is that a ball striking the
                // strap contacts the barrel about 0.01 m behind the strap's visible surface.
                // Gated: this band covers a vertical slice of the flank, which is exactly where the
                // jersey art is painted (see Make.JerseyFaces.Flank), so leaving it always-on hid part
                // of the kit on every horse. Option 0 now reveals the full flank.
                D("D_Girth", Bone.Torso, ColliderKind.Box,
                  V(0f, 0f, 0.10f), 90f, V(0.36f, 0.12f, 0.48f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(4)),

                // FETLOCK SOCKS. Boxes, not capsules: at max girth a capsule this short would have
                // 2r > height and Unity silently degrades it to a sphere. Slick too, because a leg
                // swinging through its stride digs a sock's trailing corner in below the hoof.
                D("D_SockFL", Bone.ForearmL, ColliderKind.Box,
                  V(0f, -0.068f, 0f), 0f, V(0.21f, 0.20f, 0.21f), true, DecorTint.StyleB, slick: true),
                D("D_SockFR", Bone.ForearmR, ColliderKind.Box,
                  V(0f, -0.068f, 0f), 0f, V(0.21f, 0.20f, 0.21f), true, DecorTint.StyleB, slick: true),

                // HOOVES. Front pair hangs off the FOREARM, which is a strike bone, so a hoof
                // contact resolves to a proper kick through _decorOwner. They are also the widest
                // part of the leg, which is where the flush forearm gets its capture width back.
                //
                // ALL FOUR ARE SLICK, and that is not decoration: a hoof bottom sits at -0.1995 in
                // its leg's local frame, BELOW the Slick leg capsule it hangs off, so the hoof is
                // what the turf actually touches. The Slick flag on Forearm/Foot was never reaching
                // the contact surface, which is why the gait caught and read as clunky.
                D("D_HoofFL", Bone.ForearmL, ColliderKind.Box,
                  V(0f, -0.152f, 0f), 0f, V(0.20f, 0.095f, 0.22f), true, DecorTint.Dark, slick: true),
                D("D_HoofFR", Bone.ForearmR, ColliderKind.Box,
                  V(0f, -0.152f, 0f), 0f, V(0.20f, 0.095f, 0.22f), true, DecorTint.Dark, slick: true),

                // Hind hooves. These also bridge the pre-existing pastern gap between Foot and Calf.
                D("D_HoofHL", Bone.FootL, ColliderKind.Box,
                  V(0f, 0.012f, 0f), 0f, V(0.135f, 0.125f, 0.16f), true, DecorTint.Dark, slick: true),
                D("D_HoofHR", Bone.FootR, ColliderKind.Box,
                  V(0f, 0.012f, 0f), 0f, V(0.135f, 0.125f, 0.16f), true, DecorTint.Dark, slick: true),

                // ============================ MARKINGS (StyleB) ============================
                // None / Star / Blaze / Snip / Stockings / Dappled. All NON-SOLID: a marking is paint,
                // and gating a collider would make a colour picker change capture width.
                //
                // Two constructions are used, and which one applies is decided by what the marking
                // sits on, not by taste:
                //
                //  - On the SKULL, a decal with girthOff. The skull is a Sphere of radius 0.15*girth,
                //    so an offset of length 0.15 scaled the same way lands exactly on the surface at
                //    every build and the plate straddles it half in, half out. Without girthOff the
                //    same plate floats 5 cm clear of a lean horse and disappears inside a heavy one.
                //  - On the MUZZLE, a SLEEVE: the identical offset and euler as D_Muzzle, with dims
                //    that exceed it on the axes that should show and fall short on the axes that
                //    should not. Every extent then carries the same scale factor as the muzzle's own,
                //    so the marking cannot come unstuck no matter where the sliders sit. This is why
                //    the numbers below are stated against the muzzle's 0.115 x 0.26 x 0.115.

                // STAR: a forehead patch. Offset length is 0.15005, i.e. the skull radius, and the
                // 0.030 thickness straddles it. The -21 euler lays the plate flat against the
                // forehead normal (0, 0.358, 0.934) rather than standing it up off the curve.
                D("D_MkStar", Bone.Head, ColliderKind.Box,
                  V(0f, 0.054f, 0.140f), -21f, V(0.062f, 0.062f, 0.030f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(1), girthOff: true),

                // BLAZE: a stripe the length of the face. A muzzle sleeve 0.145 deep against the
                // muzzle's 0.115, so it stands 0.015 proud top and bottom at every girth, and 0.30
                // long against 0.26, so it runs past the nose and buries its other end in the skull
                // instead of stopping in mid air. Only 0.042 wide, which is what makes it a stripe:
                // its side faces stay inside the muzzle and only the top shows.
                D("D_MkBlaze", Bone.Head, ColliderKind.Box,
                  V(0f, -0.045f, 0.20f), 90f, V(0.042f, 0.30f, 0.145f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(2)),

                // SNIP: the same sleeve trick, but a short band at the nose tip only.
                D("D_MkSnip", Bone.Head, ColliderKind.Box,
                  V(0f, -0.045f, 0.295f), 90f, V(0.075f, 0.075f, 0.145f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(3)),

                // STOCKINGS: a HIND pair. The FRONT socks (D_SockF*) are ungated, because they are
                // fetlock GEOMETRY paying for a flush collider, not a marking. So this option reads as
                // "all four legs socked" against the default "front only" - which is worth knowing
                // before wondering why the front socks never disappear.
                D("D_MkSockHL", Bone.CalfL, ColliderKind.Box,
                  V(0f, -0.062f, 0f), 0f, V(0.18f, 0.19f, 0.18f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(4)),
                D("D_MkSockHR", Bone.CalfR, ColliderKind.Box,
                  V(0f, -0.062f, 0f), 0f, V(0.18f, 0.19f, 0.18f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(4)),

                // DAPPLES: three dashes down the barrel. ONE box each, not one per side: 0.37 wide
                // against the barrel's 0.34 means each box pierces clean through and shows on BOTH
                // flanks, which halves the row count and guarantees the two sides match.
                //
                // Pitched 90 like the girth strap, and for the same reason. That pitch puts the
                // height-scaled dim on the barrel's LENGTH (also height-scaled) and the girth-scaled
                // dim on its DEPTH (also girth-scaled), so a dapple stays the same patch of flank as
                // either slider moves. Unpitched it would stretch one way and shrink the other.
                // The small vertical offsets only break up the row; they are height-scaled against a
                // girth-scaled flank, but at 0.05 the drift never carries a patch off the barrel.
                D("D_MkDap1", Bone.Torso, ColliderKind.Box,
                  V(0f, 0.05f, -0.22f), 90f, V(0.37f, 0.16f, 0.15f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(5)),
                D("D_MkDap2", Bone.Torso, ColliderKind.Box,
                  V(0f, -0.04f, 0.02f), 90f, V(0.37f, 0.13f, 0.19f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(5)),
                D("D_MkDap3", Bone.Torso, ColliderKind.Box,
                  V(0f, 0.06f, 0.24f), 90f, V(0.37f, 0.15f, 0.13f), false, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(5)),

                // ============================== TACK (StyleC) ==============================
                // None / Bridle / Halter / Blinkers / Saddle Pad. All NON-SOLID.
                //
                // Why there are no long diagonal cheek straps: a straight 0.24 strap laid on a sphere
                // of radius 0.15 stands 0.05 off it at the ends, which is a third of the head's radius
                // and reads as floating hardware. Head tack therefore lives where the geometry can
                // actually carry it - sleeves on the box muzzle, sleeves on the neck capsule, and one
                // piercing box across the skull whose overhang IS the browband.

                // NOSEBAND, thin. Bridle only. A muzzle sleeve: 0.162 deep against 0.115 stands proud
                // top and bottom, 0.138 wide against 0.115 stands proud at both cheeks, and 0.048
                // along the face makes it a band rather than a boot.
                D("D_TkNose", Bone.Head, ColliderKind.Box,
                  V(0f, -0.045f, 0.255f), 90f, V(0.138f, 0.048f, 0.162f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(1)),

                // NOSEBAND, broad. Shared by Halter and Blinkers, which is the whole point of a MASK
                // rather than an index: one spec, two options.
                D("D_TkNoseW", Bone.Head, ColliderKind.Box,
                  V(0f, -0.045f, 0.235f), 90f, V(0.138f, 0.10f, 0.162f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(2, 3)),

                // BROWBAND. Shared by all three bridle options. 0.335 wide against a 0.30 skull
                // diameter, both girth-scaled, so it pierces the head and shows as a strap end at each
                // temple at every build. Buried on its other two axes by construction.
                D("D_TkBrow", Bone.Head, ColliderKind.Box,
                  V(0f, 0.02f, 0.02f), 0f, V(0.335f, 0.055f, 0.115f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(1, 2, 3), girthOff: true),

                // THROATLATCH. A ring around the neck, placed 0.10 up the neck's own axis: the neck
                // sits at (0, -0.155, -0.125) pitched 38.9, so one step along (0, 0.7784, 0.6280)
                // lands here. Both offsets are height-scaled, so the ring holds its place on the neck
                // as the sliders move. A BOX, not a capsule: at 0.11 long and 0.135 across, a capsule
                // would have 2r > height and Unity would silently collapse it to a sphere.
                D("D_TkThroat", Bone.Head, ColliderKind.Box,
                  V(0f, -0.077f, -0.062f), 38.9f, V(0.27f, 0.11f, 0.27f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(1, 2)),

                // BLINKER CUPS. Plates standing off the sides of the head beside the eyes. Their inner
                // edge is inside the skull and their outer edge past it, so they are anchored and
                // visible at once.
                D("D_TkBlinkL", Bone.Head, ColliderKind.Box,
                  V(-0.115f, 0.03f, 0.055f), 0f, V(0.055f, 0.13f, 0.115f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(3), girthOff: true),
                D("D_TkBlinkR", Bone.Head, ColliderKind.Box,
                  V(0.115f, 0.03f, 0.055f), 0f, V(0.055f, 0.13f, 0.115f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(3), girthOff: true),
            };
            return d;
        }

        /// <summary>
        /// THE ELEPHANT. The quadruped table with pachyderm proportions, plus a three-segment trunk,
        /// slab ears, tusks and four foot pads.
        ///
        /// Everything is WIDER, not taller. The size rule is that animals are stylized down and stay
        /// human sized, so an elephant reads big through girth (VisualGirth 1.40, barrel 0.46 x 0.88
        /// x 0.52, pillar legs at 0.135 radius) rather than through height. The pillar legs are also
        /// what closes the nutmeg channel: widest clear gap 0.0804 / 0.0980 / 0.1323 m, all well
        /// under a 0.22 m ball.
        ///
        /// Flush hitboxes cost this species almost nothing, because an elephant IS thick: forearm
        /// -0.7%, upper arm -9.4%, head +4.0% against the plan's fattened colliders.
        ///
        /// THE TRUNK, and why the header numbers had to change. The trunk is three chained capsules
        /// with NEGATIVE eulers, which is what makes them hang down-and-FORWARD (a positive euler
        /// would hang them down-and-back, into the chest). Segments 2 and 3 are SOLVED from segment
        /// 1's tip, not hand-placed, so the chain cannot gap: their offsets are the exact numbers,
        /// not round ones, and they must not be tidied.
        ///
        /// The header aid lifts that trunk. The plan called for a REAR (torso -18) plus the lift, and
        /// it does not fit: a headed ball has to clear the 2.44 m bar with a whole ball diameter of
        /// room, so the contact surface has a hard 2.22 m ceiling, and at max girth the elephant's
        /// skull top is ALREADY at 2.177 m standing still. The rear put contact at 3.10 m, which is a
        /// miss, not a goal. So the rear is gone and Species.Elephant now uses torso +12, a nose-down
        /// PLANT: the animal drops its head and shoulders to swing the trunk up. Head stays -50. That
        /// lifts the trunk tip 0.42 m from rest, puts the contact surface at 1.989 m (a jumping human
        /// heads from 1.72), and leaves 0.16 m of ceiling margin at max girth.
        /// </summary>
        static BodyLayoutDef Elephant(BodyLayoutDef plan)
        {
            var d = Clone(plan);
            Flush(d);

            Set(d, Bone.Pelvis, dims: V(0.44f, 0.34f, 0.36f));

            // driveMul 8.3 -> 9.14: same inertia recompute as the horse, smaller because the
            // elephant's head sits back on a shorter lever.
            Set(d, Bone.Torso, dims: V(0.46f, 0.88f, 0.52f),
                anchor: V(0f, -0.44f, 0.04f), driveMul: 9.14f);

            // A big skull sat low and close to the shoulders, which is where an elephant's is. The
            // anchor puts the poll just inside the barrel's front face.
            Set(d, Bone.Head, pos: V(0f, 1.30f, 0.62f), dims: V(0.23f, 0.23f, 0f),
                anchor: V(0f, -0.03f, -0.17f));

            // PILLAR legs. Nearly as thick as they are long: at max girth the thigh and forearm run
            // 2r/len = 0.95, just inside the point where Unity degrades a capsule to a sphere.
            Set(d, Bone.ThighL, pos: V(-0.185f, 0.65f, -0.40f), dims: V(0.135f, 0.40f, 0f));
            Set(d, Bone.ThighR, pos: V(0.185f, 0.65f, -0.40f), dims: V(0.135f, 0.40f, 0f));
            Set(d, Bone.CalfL,  pos: V(-0.185f, 0.30f, -0.40f), dims: V(0.125f, 0.38f, 0f));
            Set(d, Bone.CalfR,  pos: V(0.185f, 0.30f, -0.40f), dims: V(0.125f, 0.38f, 0f));
            Set(d, Bone.FootL,  pos: V(-0.185f, 0.055f, -0.38f), dims: V(0.20f, 0.07f, 0.20f));
            Set(d, Bone.FootR,  pos: V(0.185f, 0.055f, -0.38f), dims: V(0.20f, 0.07f, 0.20f));

            Set(d, Bone.UpperArmL, pos: V(-0.17f, 0.62f, 0.40f), dims: V(0.145f, 0.44f, 0f));
            Set(d, Bone.UpperArmR, pos: V(0.17f, 0.62f, 0.40f), dims: V(0.145f, 0.44f, 0f));
            Set(d, Bone.ForearmL,  pos: V(-0.17f, 0.20f, 0.40f), dims: V(0.135f, 0.40f, 0f));
            Set(d, Bone.ForearmR,  pos: V(0.17f, 0.20f, 0.40f), dims: V(0.135f, 0.40f, 0f));

            d.Decor = new[]
            {
                // TRUNK, three tapering segments. There is no cone primitive in Make, so a taper is
                // stacked capsules of falling radius. All three are SOLID, so the whole trunk heads
                // the ball. Segment 1 is authored; segments 2 and 3 are SOLVED so each starts exactly
                // where the last one ended. DO NOT round these offsets.
                D("D_Trunk1", Bone.Head, ColliderKind.CapsuleY,
                  V(0f, -0.115f, 0.135f), -55f, V(0.088f, 0.26f, 0f), true, DecorTint.Skin),
                D("D_Trunk2", Bone.Head, ColliderKind.CapsuleY,
                  V(0f, -0.2135f, 0.3307f), -75f, V(0.070f, 0.22f, 0f), true, DecorTint.Skin),
                D("D_Trunk3", Bone.Head, ColliderKind.CapsuleY,
                  V(0f, -0.2161f, 0.508f), -110f, V(0.052f, 0.18f, 0f), true, DecorTint.Skin),

                // ============================== EARS (StyleA) ==============================
                // Plain / Notched / Wide / Torn. Thin vertical slabs standing off the sides of the
                // skull. SOLID, because an ear that big has to stop a ball or it reads as broken, and
                // index 0 is "Plain" rather than "None" for the same reason: an earless elephant looks
                // like a bug, not a choice.
                //
                // Solid AND gated is the one place that combination is allowed in either table, and it
                // is safe only because of a rule every variant below obeys: offset x stays at +/-0.215
                // and dims.x stays at 0.05, so the OUTER FACE sits at 0.24 girth on every option. The
                // header surface therefore does not move between ear styles and the picker cannot
                // change how wide the elephant captures the ball. Shapes differ by SUBTRACTION - a
                // notch, a torn corner - never by pushing the outer face further out.
                //
                // A shape that removes material is built as TWO boxes with a gap, not one box with a
                // bite taken out of it, because there is no CSG here. Both halves carry the same gate
                // bit, so they appear and vanish together.

                // PLAIN: one slab, the original ear.
                D("D_EarPlainL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, 0.02f, -0.075f), 0f, V(0.05f, 0.40f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(0)),
                D("D_EarPlainR", Bone.Head, ColliderKind.Box,
                  V(0.215f, 0.02f, -0.075f), 0f, V(0.05f, 0.40f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(0)),

                // NOTCHED: a tall upper slab and a shorter, shallower lower one. The lower piece is
                // 0.30 deep against the upper's 0.36 and sits 0.03 further forward, so the missing
                // material is a wedge out of the REAR MIDDLE, which is where a real notch is.
                D("D_EarNotchUL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, 0.115f, -0.075f), 0f, V(0.05f, 0.21f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(1)),
                D("D_EarNotchUR", Bone.Head, ColliderKind.Box,
                  V(0.215f, 0.115f, -0.075f), 0f, V(0.05f, 0.21f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(1)),
                D("D_EarNotchLL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, -0.13f, -0.045f), 0f, V(0.05f, 0.16f, 0.30f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(1)),
                D("D_EarNotchLR", Bone.Head, ColliderKind.Box,
                  V(0.215f, -0.13f, -0.045f), 0f, V(0.05f, 0.16f, 0.30f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(1)),

                // WIDE: bigger in the two axes that are safe to grow, 0.44 tall and 0.50 deep, and
                // still 0.05 thick at x 0.215. It reads as the biggest ear of the four without moving
                // the surface a ball can hit sideways.
                D("D_EarWideL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, 0.02f, -0.085f), 0f, V(0.05f, 0.44f, 0.50f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(2)),
                D("D_EarWideR", Bone.Head, ColliderKind.Box,
                  V(0.215f, 0.02f, -0.085f), 0f, V(0.05f, 0.44f, 0.50f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(2)),

                // TORN: a full-depth upper and a small forward-shifted lower stub, so the whole
                // rear-bottom corner is missing. Ragged where Notched is merely nicked.
                D("D_EarTornUL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, 0.075f, -0.075f), 0f, V(0.05f, 0.29f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(3)),
                D("D_EarTornUR", Bone.Head, ColliderKind.Box,
                  V(0.215f, 0.075f, -0.075f), 0f, V(0.05f, 0.29f, 0.36f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(3)),
                D("D_EarTornLL", Bone.Head, ColliderKind.Box,
                  V(-0.215f, -0.115f, -0.005f), 0f, V(0.05f, 0.09f, 0.22f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(3)),
                D("D_EarTornLR", Bone.Head, ColliderKind.Box,
                  V(0.215f, -0.115f, -0.005f), 0f, V(0.05f, 0.09f, 0.22f), true, DecorTint.StyleA,
                  gate: SlotKind.StyleA, gateMask: Bit(3)),

                // ============================== TUSKS (StyleB) ==============================
                // None / Short / Curved / Long / Banded. All ten rows are GIRTH-OFFSET and all of them
                // emerge BELOW the trunk. Both of those are corrections to a first pass that was wrong
                // at every build, so read the next two paragraphs before touching a number here.
                //
                // WHY girthOff, and it is not a preference. A decor Off() offset scales (x*g, y*h, z*h)
                // under LengthAlongHeight, but the skull is a SPHERE whose radius scales by g. So the
                // socket moves on h while the surface it must sit on moves on g, and for the elephant
                // r = h/g runs 0.532 to 1.459 across the two sliders, a 2.7x span. Measured on the old
                // h-scaled offsets: at MAX weight / MIN height every one of the four tusk variants was
                // entirely INSIDE the skull, so that build showed no tusks on any option, and the band
                // was buried at all five slider corners. girthOff makes the offset, the tusk radius and
                // the skull radius all carry g, so the attachment geometry is exactly scale invariant.
                // The shared socket below has |offset| = 0.2300, i.e. it lies exactly ON the 0.23 skull
                // sphere, which is what leaves the whole capsule outside the skull at every build.
                //
                // WHY the eulers turned DOWN, 72/68 to 96/100. The trunk descends forward at 35 deg
                // from a base at y -0.115. A tusk rooted BELOW that base and sweeping UP at 18 deg
                // drives straight into it, which is the interpenetration the old table had: lateral
                // trunk clearance measured -0.042 to -0.085 on Short, Curved and Long at EVERY build.
                // The tusks now start under the trunk and stay under it, and the hook comes from the
                // Curved variant's tip segment instead of from the root angle.
                //
                // These stay SOLID, which BREAKS the rule the rest of both tables follows, and the
                // reason is worth stating. Three arguments, in order of weight. (1) The tusks were
                // already Solid before they were gated, so making them non-solid would be a regression
                // dressed up as consistency. (2) A visible tusk that a ball passes clean through reads
                // far worse than a reach that changes by a few centimetres. (3) The reach delta is
                // small anyway: the elephant's front reach comes from the three-segment trunk, which is
                // ungated anatomy and extends about 1.1 m past the skull, so a 0.30 tusk adds nothing
                // to the forward extent. Option 0 (None) is the only real change, and it makes the
                // elephant slightly narrower at the muzzle. Accepted.
                //
                // The BAND is the exception inside the exception: it is paint on a tusk, not tusk, so
                // it is non-solid and Dark.

                // SHORT: the shared socket, 6 deg nose-down, thin and 0.20 long.
                D("D_TuskShortL", Bone.Head, ColliderKind.CapsuleY,
                  V(-0.145f, -0.1596f, 0.080f), 96f, V(0.034f, 0.20f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(1), girthOff: true),
                D("D_TuskShortR", Bone.Head, ColliderKind.CapsuleY,
                  V(0.145f, -0.1596f, 0.080f), 96f, V(0.034f, 0.20f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(1), girthOff: true),

                // CURVED: two segments, the tip hooking back UP at euler 62 to supply the sweep the
                // root angle no longer does. Placed by OVERLAP 0.06 along the base axis, NOT butted
                // onto the base's tip the way the trunk segments are, and that is forced rather than
                // sloppy: a girth-scaled offset cannot express an h-scaled LENGTH. The base's half
                // length carries h while the offset carries g, so a butt joint solved at one r gaps at
                // every other r. Checked at all five slider corners: the base's front face reaches
                // 0.048 to 0.131 while the tip's rear cap sits at -0.037 to 0.025, so the pair stays
                // joined, and the tip protrudes 0.026 to 0.047 past the base at every build.
                D("D_TuskCurveL", Bone.Head, ColliderKind.CapsuleY,
                  V(-0.145f, -0.1596f, 0.080f), 96f, V(0.038f, 0.18f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(2), girthOff: true),
                D("D_TuskCurveR", Bone.Head, ColliderKind.CapsuleY,
                  V(0.145f, -0.1596f, 0.080f), 96f, V(0.038f, 0.18f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(2), girthOff: true),
                D("D_TuskCurveTipL", Bone.Head, ColliderKind.CapsuleY,
                  V(-0.145f, -0.1659f, 0.1397f), 62f, V(0.030f, 0.16f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(2), girthOff: true),
                D("D_TuskCurveTipR", Bone.Head, ColliderKind.CapsuleY,
                  V(0.145f, -0.1659f, 0.1397f), 62f, V(0.030f, 0.16f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(2), girthOff: true),

                // LONG: shared by Long and Banded, which is the point of a MASK rather than an index.
                // Banded is this exact tusk plus the ring below, so the two options cannot drift apart.
                // Laid to euler 100, 10 deg nose-down, because 0.38 of tusk needs more trunk clearance
                // than any other variant and it is the one that used to foul worst.
                D("D_TuskLongL", Bone.Head, ColliderKind.CapsuleY,
                  V(-0.145f, -0.1596f, 0.080f), 100f, V(0.042f, 0.38f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(3, 4), girthOff: true),
                D("D_TuskLongR", Bone.Head, ColliderKind.CapsuleY,
                  V(0.145f, -0.1596f, 0.080f), 100f, V(0.042f, 0.38f, 0f), true, DecorTint.StyleB,
                  gate: SlotKind.StyleB, gateMask: Bit(3, 4), girthOff: true),

                // BAND, a ring on the long tusk, sitting 0.075 FORWARD of the socket along the same
                // euler-100 axis. Note what it is no longer solved from: the old row solved it from the
                // tusk's lower END, which is inside the skull, so the ring was buried at every build.
                // Forward of the socket instead gives |offset| = 0.2730 against a 0.23 skull, so it
                // shows everywhere. The 0.075 must also land on the VISIBLE part of the tusk, which
                // needs 0.075 <= 0.19r; the worst corner gives 0.101, so it clears.
                //
                // A BOX, not a capsule, and not by preference: at 0.07 long and 0.10 across, 2r/len
                // would be 1.6 and Unity SILENTLY collapses a capsule to a sphere once 2r exceeds
                // height. 0.10 against the tusk's 0.084 diameter is what makes the ring show.
                D("D_TuskBandL", Bone.Head, ColliderKind.Box,
                  V(-0.145f, -0.1726f, 0.1539f), 100f, V(0.10f, 0.07f, 0.10f), false, DecorTint.Dark,
                  gate: SlotKind.StyleB, gateMask: Bit(4), girthOff: true),
                D("D_TuskBandR", Bone.Head, ColliderKind.Box,
                  V(0.145f, -0.1726f, 0.1539f), 100f, V(0.10f, 0.07f, 0.10f), false, DecorTint.Dark,
                  gate: SlotKind.StyleB, gateMask: Bit(4), girthOff: true),

                // ============================== TACK (StyleC) ==============================
                // None / Head Cloth / Ankle Bands / Blanket. All NON-SOLID: cloth and webbing are not
                // colliders, and gating a collider would let a cosmetic picker change how the animal
                // captures the ball.

                // HEAD CLOTH. A draped panel over the poll. girthOff because it hangs on the head
                // SPHERE, whose radius scales by girth alone while a normal offset scales partly by
                // height - see DecorSpec.GirthOffset. The 0.50 width against the skull's 0.46 girth
                // diameter is what makes it read: it pierces the skull and shows as a flap at each
                // temple, so one row does the work of two and the sides cannot end up mismatched.
                D("D_TkCloth", Bone.Head, ColliderKind.Box,
                  V(0f, 0.055f, -0.03f), 0f, V(0.50f, 0.22f, 0.34f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(1), girthOff: true),

                // ANKLE BANDS, all four legs. Sized against each leg's own girth-scaled diameter so
                // every band stands proud by the same margin: 0.30 against the forearm's 0.27, 0.28
                // against the calf's 0.25. The front pair hangs off the FOREARM, a strike bone, but the
                // band is non-solid so it never takes a contact and cannot turn a band touch into a
                // kick.
                D("D_TkBandFL", Bone.ForearmL, ColliderKind.Box,
                  V(0f, -0.085f, 0f), 0f, V(0.30f, 0.07f, 0.30f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(2)),
                D("D_TkBandFR", Bone.ForearmR, ColliderKind.Box,
                  V(0f, -0.085f, 0f), 0f, V(0.30f, 0.07f, 0.30f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(2)),
                D("D_TkBandHL", Bone.CalfL, ColliderKind.Box,
                  V(0f, -0.135f, 0f), 0f, V(0.28f, 0.07f, 0.28f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(2)),
                D("D_TkBandHR", Bone.CalfR, ColliderKind.Box,
                  V(0f, -0.135f, 0f), 0f, V(0.28f, 0.07f, 0.28f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(2)),

                // CEREMONIAL BLANKET. Same girth-scaled band trick as the horse's girth strap: a Box
                // pitched 90 so its width and height both scale by GIRTH while only its thickness
                // scales by height, which is what keeps it wrapped on the barrel at every girth.
                //
                // NOW GATED to TACK option 3, and NO LONGER SOLID, for the same two reasons D_Girth
                // carries. Solid: a cloth should not be a collider, and gating one would make a
                // cosmetic picker change capture width. The barrel underneath is flush and solid, so
                // the only consequence is that a ball striking the blanket contacts the barrel about
                // 0.01 m behind its visible surface. Gated: the blanket covers a vertical slice of the
                // flank, which is exactly where the jersey art is painted (see Make.JerseyFaces.Flank),
                // so leaving it always-on hid part of the kit on every elephant. Option 0 now reveals
                // the full flank.
                D("D_Blanket", Bone.Torso, ColliderKind.Box,
                  V(0f, 0f, 0.02f), 90f, V(0.48f, 0.34f, 0.54f), false, DecorTint.StyleC,
                  gate: SlotKind.StyleC, gateMask: Bit(3)),

                // FOOT PADS. The front pair hangs off the FOREARM, a strike bone, so a pad contact
                // resolves to a kick. Wide and flat, like the real thing. All four are SLICK for the
                // same reason the horse's hooves are: the pad is the lowest geometry on the animal,
                // so it, and not the Slick leg capsule above it, is what the turf touches. A pad is
                // also the broadest contact patch in either species, so it caught the hardest.
                D("D_PadFL", Bone.ForearmL, ColliderKind.Box,
                  V(0f, -0.157f, 0f), 0f, V(0.30f, 0.085f, 0.32f), true, DecorTint.Dark, slick: true),
                D("D_PadFR", Bone.ForearmR, ColliderKind.Box,
                  V(0f, -0.157f, 0f), 0f, V(0.30f, 0.085f, 0.32f), true, DecorTint.Dark, slick: true),

                D("D_PadHL", Bone.FootL, ColliderKind.Box,
                  V(0f, 0.020f, 0f), 0f, V(0.235f, 0.14f, 0.235f), true, DecorTint.Dark, slick: true),
                D("D_PadHR", Bone.FootR, ColliderKind.Box,
                  V(0f, 0.020f, 0f), 0f, V(0.235f, 0.14f, 0.235f), true, DecorTint.Dark, slick: true),
            };
            return d;
        }

        // ---------------------------------------------------------------- sugar
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>
        /// Copy a layout so a species can edit its bones without touching the shared plan table.
        ///
        /// BoneSpec is a struct, so cloning the array deep-copies every bone. The arrays that are
        /// only ever READ (StrikeBones, LegBones, RaiseL/R) are shared by reference on purpose: no
        /// species override changes which bones strike or raise, only their shape.
        ///
        /// _parentBone is deliberately NOT copied. It is a private lazily built cache behind
        /// ParentByBone, and leaving it null lets the clone rebuild it from its OWN Bones array.
        /// </summary>
        static BodyLayoutDef Clone(BodyLayoutDef src)
            => new BodyLayoutDef
            {
                Bones             = (BoneSpec[])src.Bones.Clone(),
                LengthAlongHeight = src.LengthAlongHeight,
                GroundProbeRadius = src.GroundProbeRadius,
                GroundProbeDist   = src.GroundProbeDist,
                StrikeBones       = src.StrikeBones,
                LegBones          = src.LegBones,
                RaiseL            = src.RaiseL,
                RaiseR            = src.RaiseR,
                Decor             = src.Decor,
            };

        /// <summary>
        /// Overwrite the fields a species actually changes on one bone. Omitted arguments are left
        /// alone, so a table only states what differs from its plan.
        /// </summary>
        static void Set(BodyLayoutDef d, Bone b, Vector3? dims = null, Vector3? pos = null,
                        Vector3? anchor = null, float mass = 0f, float driveMul = 0f,
                        HitboxClass? hitbox = null)
        {
            int i = d.IndexOf(b);
            if (i < 0) return;
            if (dims.HasValue)   d.Bones[i].Dims        = dims.Value;
            if (pos.HasValue)    d.Bones[i].Pos         = pos.Value;
            if (anchor.HasValue) d.Bones[i].JointAnchor = anchor.Value;
            if (mass > 0f)       d.Bones[i].Mass        = mass;
            if (driveMul > 0f)   d.Bones[i].DriveMul    = driveMul;
            if (hitbox.HasValue) d.Bones[i].Hitbox      = hitbox.Value;
        }

        /// <summary>
        /// Strip every hitbox class off a layout, making each collider exactly its visible mesh.
        ///
        /// This is the "flush like a human" answer, and it is worth being precise about what that
        /// phrase means. A HUMAN's hitboxes are NOT 1.0x either: measured per-side overhang runs
        /// 0.030 m on the head, 0.054 thigh, 0.045 calf, 0.051 foot, 0.080 upper arm, 0.072 forearm.
        /// So the human standard is a 0.03 to 0.08 m tolerance band, not exactness. An animal with
        /// decor cannot use a band, because a fattened bone collider would swallow the appendages
        /// hung off it and the ball would stop short of a visibly separate tusk or hoof. So the
        /// quadrupeds go to genuinely flush and the VISIBLE meshes are grown to pay for the lost
        /// reach, which is a better deal anyway: the reach now comes from geometry you can see.
        /// </summary>
        static void Flush(BodyLayoutDef d)
        {
            for (int i = 0; i < d.Bones.Length; i++) d.Bones[i].Hitbox = HitboxClass.None;
        }

        /// <summary>
        /// One decor piece. Euler is a single X angle because every appendage in both tables pitches
        /// about X only, which is also why mirroring a left piece to the right needs nothing but the
        /// offset's sign flipped.
        ///
        /// gate / gateMask are OPTIONAL and default to ungated (always drawn). A piece that IS gated
        /// only draws when the player's pick for that slot has the matching bit set, so gateMask is
        /// written as a bit expression against the option list in SpeciesCosmetics, e.g.
        /// <c>Bit(1)</c> for option 1 only, <c>Bit(3, 4)</c> for a shape shared by two options.
        /// </summary>
        static DecorSpec D(string name, Bone parent, ColliderKind kind, Vector3 offset,
                           float eulerX, Vector3 dims, bool solid, DecorTint tint,
                           bool slick = false, SlotKind gate = SlotKind.Skin, int gateMask = 0,
                           bool girthOff = false)
            => new DecorSpec
            {
                Name = name, Parent = parent, Kind = kind,
                Offset = offset, Euler = new Vector3(eulerX, 0f, 0f),
                Dims = dims, Solid = solid, Tint = tint, Slick = slick,
                Gate = gate, GateMask = gateMask, GirthOffset = girthOff,
            };

        /// <summary>
        /// A GateMask over option indices. Reads at the call site as the option numbers themselves,
        /// which is the only way these tables stay checkable against the SpeciesCosmetics lists.
        /// </summary>
        static int Bit(int a) => 1 << a;
        static int Bit(int a, int b) => (1 << a) | (1 << b);
        static int Bit(int a, int b, int c) => (1 << a) | (1 << b) | (1 << c);

        static BoneSpec Root(Bone b, Vector3 pos, ColliderKind kind, Vector3 dims, float mass)
            => new BoneSpec
            {
                Bone = b, Parent = b, IsRoot = true,
                Pos = pos, Kind = kind, Dims = dims, Mass = mass,
            };

        static BoneSpec B(Bone b, Bone parent, Vector3 pos,
                          ColliderKind kind, Vector3 dims, float mass, Vector3 anchor,
                          HitboxClass hitbox = HitboxClass.None, bool slick = false,
                          bool jersey = false, Vector3? targetPos = null, Vector3 rest = default,
                          float driveMul = 0f,
                          Make.JerseyFaces jerseyFaces = Make.JerseyFaces.Chest)
            => new BoneSpec
            {
                Bone = b, Parent = parent, IsRoot = false,
                Pos = pos, TargetPos = targetPos, RestEuler = rest,
                Kind = kind, Dims = dims, Mass = mass,
                WearsJersey = jersey, JerseyFaces = jerseyFaces,
                Hitbox = hitbox, Slick = slick,
                JointAnchor = anchor, DriveMul = driveMul,
            };
    }
}
