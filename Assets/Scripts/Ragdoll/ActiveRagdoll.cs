using System.Collections.Generic;
using UnityEngine;
using Trickshot.Net;   // AnimState (networked animation state, played on the display puppet)

namespace Trickshot
{
    /// <summary>
    /// A controllable active ragdoll.
    ///
    /// Two skeletons, per the design:
    ///  - Target skeleton: an invisible nested hierarchy of transforms describing
    ///    the intended pose (stand / load / bicycle). Poses lerp these.
    ///  - Physics skeleton: visible rigid parts joined by ConfigurableJoints whose
    ///    slerp drives chase the matching target bone's local rotation.
    ///
    /// The pelvis is the free root: it is not driven by a joint but by a PD balance
    /// torque (keeps it upright and facing a chosen direction) plus a locomotion
    /// force. Ball impacts and collisions can overpower the finite joint forces,
    /// which is where the ragdoll comedy and the "bad timing = awkward failure"
    /// behaviour come from.
    ///
    /// Built entirely from code (Build()); no prefabs, no scene wiring.
    /// </summary>
    public class ActiveRagdoll : MonoBehaviour
    {
        readonly Transform[] _target = new Transform[(int)Bone.Count];
        readonly Rigidbody[] _rb = new Rigidbody[(int)Bone.Count];
        readonly ConfigurableJoint[] _joint = new ConfigurableJoint[(int)Bone.Count];
        readonly Quaternion[] _jointStartLocal = new Quaternion[(int)Bone.Count];

        // Per-bone drive multiplier, 1 unless the layout asks for inertia compensation. Resolved
        // once at build from BoneSpec.DriveMul times the build scale, because it depends on
        // _hScale / _massMul which are only known then. See BoneSpec.DriveMul.
        readonly float[] _driveMul = new float[(int)Bone.Count];
        readonly Quaternion[] _targetRestLocal = new Quaternion[(int)Bone.Count];
        readonly List<Collider> _ownColliders = new List<Collider>();

        /// <summary>
        /// Which bone each SOLID DECOR collider belongs to (a horse's muzzle to the Head, a hoof to
        /// its front leg). Decor hangs off a bone's Rigidbody as a child object, so the collider the
        /// ball reports is NOT the bone's own transform and <see cref="BoneOf"/> would miss it.
        ///
        /// An explicit map, deliberately, instead of walking up the transform hierarchy to the
        /// nearest Rigidbody. A parent walk would also catch the KEEPER GLOVE, which is the same
        /// shape of object, and that would silently change what a save means: the glove currently
        /// resolves to null and classifies as a body touch, but its owner is ForearmL/R, which on a
        /// QUADRUPED keeper is a front leg and therefore in LegBones. Every glove save would become a
        /// full kick. The glove stays out of this map and keeps its documented null.
        /// </summary>
        readonly Dictionary<Transform, Bone> _decorOwner = new Dictionary<Transform, Bone>();

        // The frictionless ground-contact material, built in Build and reused by AddDecor. Shared
        // rather than rebuilt per piece because Make.PhysMat allocates and the species preview
        // rebuilds this whole body on every drag frame.
        PhysicsMaterial _slickMat;

        // Cosmetic (hair/facial/accessory) tint materials created at build time. Tracked so
        // they can be freed on teardown - destroying a GameObject does NOT free its materials,
        // and the preview rebuilds this body repeatedly, so untracked materials would leak.
        readonly List<Material> _cosmeticMats = new List<Material>();
        public void RegisterCosmeticMaterial(Material m) { if (m != null) _cosmeticMats.Add(m); }
        void OnDestroy()
        {
            for (int i = 0; i < _cosmeticMats.Count; i++)
                if (_cosmeticMats[i] != null) Destroy(_cosmeticMats[i]);
            _cosmeticMats.Clear();
            // The two this class allocates for itself rather than receiving from a caller.
            if (_slickMat != null) Destroy(_slickMat);
            for (int i = 0; i < _ownedMats.Count; i++)
                if (_ownedMats[i] != null) Destroy(_ownedMats[i]);
            _ownedMats.Clear();
        }

        // Materials Build made on its own (the gloves), as opposed to the caller-owned torso/limb
        // pair and the registered cosmetic ones.
        readonly List<Material> _ownedMats = new List<Material>();

        Vector3[] _poseFrom = RagdollPose.Stand;
        Vector3[] _poseTo   = RagdollPose.Stand;
        float _poseT = 1f;
        float _poseSpeed = 6f;

        // Additive per-bone rotation offsets (Euler deg) layered on top of the blended
        // pose. The Striker drives these each frame for the procedural run cycle and
        // the airborne per-leg swings. Reset to zero each frame by the controller.
        readonly Vector3[] _poseOverride = new Vector3[(int)Bone.Count];

        // Set by the controller (Striker) each frame.
        public Vector3 MoveInput;            // desired world-space horizontal velocity
        public Quaternion FacingRotation = Quaternion.identity;
        public bool BalanceEnabled = true;
        public bool LocomotionEnabled = true; // when false, no velocity steering (impulses carry freely)
        public float DriveScale = 1f;        // 0..1 global motor strength multiplier
        float _lastDriveScale = float.NaN;   // DriveScale the joints were last configured with (see FixedUpdate)
        public bool IsGrounded { get; private set; }
        // When BalanceEnabled is false but this is set, the pelvis is actively driven
        // toward this orientation (used to lay the keeper out horizontal in a dive).
        public Quaternion? BodyOrientTarget;

        // Hard upright lock: while true, the pelvis cannot pitch or roll (only yaw),
        // so the character physically cannot fall over while standing or running.
        // The controller disables this the instant it jumps or starts a bicycle, so
        // the body is free to leave the ground and flip. Implemented with rigidbody
        // rotation constraints, which no impact or motor can overpower.
        public bool UprightLock = true;
        bool _lockApplied;

        // Dive lock: pelvis may PITCH forward freely (the belly-down fall) but its yaw
        // and roll are pinned to DiveYawFacing, so the chest stays square-forward and
        // never twists sideways through the dive. Set by the striker's diving header.
        public bool DiveYawLock = false;
        public Quaternion DiveYawFacing = Quaternion.identity;
        public float DiveLayoutPitch = 90f;   // target forward pitch (deg); 90 = fully belly-down

        public Rigidbody Pelvis => _rb[(int)Bone.Pelvis];
        public Rigidbody Rb(Bone b) => _rb[(int)b];
        public Transform Phys(Bone b) => _rb[(int)b] != null ? _rb[(int)b].transform : null;

        /// <summary>
        /// Which bone a struck collider belongs to, or null if it is not one of this body's parts
        /// (a keeper glove hitbox, say). The collider sits ON the P_&lt;Bone&gt; object, so this is a
        /// straight transform match. Ask this rather than parsing the object's name: the quadruped
        /// repose keeps the bone names and changes their roles, so a name prefix lies.
        /// </summary>
        public Bone? BoneOf(Transform part)
        {
            if (part == null) return null;
            for (int i = 0; i < _rb.Length; i++)
                if (_rb[i] != null && _rb[i].transform == part) return (Bone)i;
            // Solid species decor: a child of the bone, so it needs the explicit map. This is what
            // makes a muzzle count as a HEADER and a hoof count as a KICK. See _decorOwner.
            Bone owner;
            if (_decorOwner.TryGetValue(part, out owner)) return owner;
            return null;
        }
        public IReadOnlyList<Collider> OwnColliders => _ownColliders;

        /// <summary>
        /// The adult-mode appendage built on this body by Cosmetics.AttachAdult, or null. The Striker
        /// drives its Erect flag from the ThirdLeg bind and BallController asks it whether a struck
        /// collider is its hitbox; both go through here rather than a GetComponentInChildren walk.
        /// </summary>
        public AnatomySim Anatomy { get; set; }

        /// <summary>
        /// Adopt a collider built AFTER the body (the adult-mode hitbox) as one of this body's own,
        /// so IsOwn answers true for it and the ground probe skips it. Deliberately NOT given a
        /// BoneOf entry: the ball resolves it through AnatomySim.IsHitbox instead. Self-collision
        /// ignores are a separate call (<see cref="IgnoreOwnCollisionsWith"/>) because PhysX refuses
        /// IgnoreCollision on a disabled collider and forgets the pair when one is disabled, and this
        /// hitbox spends most of its life disabled.
        /// </summary>
        public void RegisterExtraCollider(Collider c)
        {
            if (c != null && !_ownColliders.Contains(c)) _ownColliders.Add(c);
        }

        /// <summary>Ignore collisions between `c` and every other enabled own collider. Call each
        /// time `c` is (re)enabled - the ignore state does not survive it being disabled.</summary>
        public void IgnoreOwnCollisionsWith(Collider c)
        {
            if (c == null || !c.enabled) return;
            for (int i = 0; i < _ownColliders.Count; i++)
            {
                var o = _ownColliders[i];
                if (o != null && o != c && o.enabled) Physics.IgnoreCollision(c, o, true);
            }
        }

        // Swap the TORSO's jersey material at runtime (only the Torso part wears the jersey; the
        // visual mesh keeps its jersey UVs, so a material swap re-skins the kit without touching
        // the mapping). Used when a remote player's networked jersey arrives after the body exists.
        public void SetTorsoMaterial(Material m)
        {
            var t = Phys(Bone.Torso);
            if (t == null || m == null) return;
            // The JERSEY mesh specifically, by name, NOT GetComponentInChildren<Renderer>().
            // MakePart always names a bone's visible mesh "v", and decor adds sibling renderers under
            // the torso (a horse's neck and mane hang off the barrel). A depth-first search would
            // return whichever child happens to come first and could paint a mane with the away kit.
            var v = t.Find("v");
            var r = v != null ? v.GetComponent<Renderer>() : null;
            if (r != null) r.sharedMaterial = m;
        }

        /// <summary>All physics-bone transforms, for the replay recorder.</summary>
        public Transform[] BoneTransforms
        {
            get
            {
                var arr = new Transform[(int)Bone.Count];
                for (int i = 0; i < arr.Length; i++) arr[i] = _rb[i] != null ? _rb[i].transform : null;
                return arr;
            }
        }

        Transform _physRoot;
        Transform _targetRoot;

        // Build-time body scaling (1 = default build). Set by BuildScaled before laying
        // out parts: heights (Y offsets) scale by _hScale, girth (part X/Z + capsule
        // length) by _gScale, and every bone mass by _massMul. Grounding uses _hScale.
        float _hScale = 1f, _gScale = 1f, _massMul = 1f;

        // Carry servo state. _standPelvisY is where the hips BELONG above the turf for this build,
        // read straight off the authored rig (layout positions are metres from the body's base with
        // the feet at y 0), so it is correct per species and per height slider with nothing to tune.
        float _standPelvisY = 1f;
        float _groundY;
        // True only when UpdateGrounded actually IDENTIFIED a floor this tick, as opposed to falling
        // back to a fabricated height. The carry servo is gated on this: acting on the fallback is what
        // drove bodies into the turf, because the fallback sits below the body by construction.
        bool _floorValid;
        public float HeightScale => _hScale;
        public float GirthScale => _gScale;   // multiplier applied to the visible head radius (cosmetics scale by this)

        // Keeper-only extra hitbox thickness: the keeper (withGloves) gets fatter arm/leg/foot
        // colliders than the striker so saves connect off any limb. 1 = no boost (striker).
        float _keeperHb = 1f;

        // The skeleton table this body was built from, chosen by the species' BodyPlan. Every
        // layout consumer reads it (Build, ResetTo, DisplaySnap, the display FK, grounding, the
        // kick detectors), so the bone numbers live in exactly one place. Defaults to the biped
        // so anything that queries this body before Build cannot null-deref.
        BodyLayoutDef _layout = BodyLayout.Biped;

        // Per-axis gain correction for the free-root BALANCE torque, computed at build from the
        // layout's welded inertia (see BodyLayout.RootDriveMul). One for a biped, so a human is
        // unaffected. The joints get the same treatment via BoneSpec.DriveMul; the root has no joint
        // and so had nothing, which is what made a quadruped sway at rest.
        Vector3 _rootDriveMul = Vector3.one;

        // Yaw-ring settle, the joint-level counterpart to _rootDriveMul above. Rate in 1/s, ZERO for a
        // biped so the human path never touches it. See SettleYawRing.
        float _yawSettleRate;
        // Yaw rate (rad/s, about world up) ApplyUprightLock actually commanded this step. The pelvis's
        // own angularVelocity cannot report it: the lock steers yaw with MoveRotation and then zeroes
        // the spin, so the number exists nowhere else.
        float _lockYawRate;

        // Which species this body was built as, captured from the PASSED appearance in Build. Read
        // it instead of PlayerProfile anywhere a behaviour differs per species: the same Striker
        // class drives remote peers and AI bodies, so the local player's species is the wrong
        // answer for any body but its own. Human until Build says otherwise.
        byte _speciesId = Species.HumanId;

        /// <summary>The species this body was built as. Prefer this over the global PlayerProfile.</summary>
        public byte SpeciesId => _speciesId;

        /// <summary>Body plan of the built skeleton. Quadruped means the 13 bones are reposed.</summary>
        public BodyPlan Plan => Species.ById(_speciesId).Plan;

        /// <summary>Bones the kick detectors attach to: the biped's feet/calves, a quadruped's front hooves.</summary>
        public Bone[] StrikeBones => _layout.StrikeBones;

        /// <summary>The limb the leg-raise lifts on the given side, as {upper, lower}.</summary>
        public Bone[] RaiseChain(bool left) => left ? _layout.RaiseL : _layout.RaiseR;

        /// <summary>
        /// Is this bone a KICKING limb on this body? A biped's legs, a quadruped's FRONT legs.
        /// The ball code needs it to tell a real strike from a scrappy body touch, and a name
        /// prefix cannot: the repose keeps the bone NAMES and changes only what they ARE.
        /// </summary>
        public bool IsLegBone(Bone b) => _layout.IsLegBone(b);

        /// <summary>
        /// VISIBLE head radius in metres on THIS body, girth already applied.
        ///
        /// The cosmetics attach path needs it because hair has to be combed over the actual skull:
        /// MakePart draws a Sphere bone at dims.x * 2 diameter (dims.y is only an optional collider
        /// override), so the visible radius is the girth-scaled dims.x. For a human this evaluates to
        /// 0.19 * girth, exactly what HairSim derives on its own, so passing it changes nothing there.
        /// A horse skull is 0.15, which HairSim could not have guessed, and 0.04 m of error puts every
        /// mane root visibly off the head.
        /// </summary>
        public float HeadVisualRadius
        {
            get
            {
                int i = _layout.IndexOf(Bone.Head);
                // No head in the table (impossible in both shipped plans): human nominal 0.19.
                if (i < 0) return 0.19f * _gScale;
                var s = _layout.Bones[i];
                return ScaleDims(s.Kind, s.Dims).x;
            }
        }

        // ---------------------------------------------------------------- build
        // Build the player with a custom height/girth/mass from a PlayerProfile-style set
        // of scales, then delegate to the normal Build. Only the player striker uses this;
        // everyone else builds at 1.0 via Build().
        public void BuildScaled(Vector3 basePos, Quaternion facing, Material torsoMat, Material limbMat,
                                float heightScale, float girthScale, float massMul, bool withGloves = true,
                                PlayerAppearance? appearance = null)
        {
            _hScale = Mathf.Max(0.5f, heightScale);
            _gScale = Mathf.Max(0.5f, girthScale);
            _massMul = Mathf.Max(0.3f, massMul);
            Build(basePos, facing, torsoMat, limbMat, withGloves, appearance);
        }

        // withGloves: the keeper wears big white gloves (with hitboxes); the striker does not.
        // appearance (optional): when non-null this body is a real player - the limb material is
        // tinted to the skin colour and head cosmetics (hair/facial/accessory) are attached. Null
        // (AI crosser/keeper/footballers) leaves the passed limb colour and adds no cosmetics.
        public void Build(Vector3 basePos, Quaternion facing, Material torsoMat, Material limbMat,
                          bool withGloves = true, PlayerAppearance? appearance = null)
        {
            // Skin tone: tint the shared limb material (head + arms + legs + pelvis + feet). The
            // torso keeps its jersey (torsoMat) untouched. Done before the parts are built so
            // every limbMat part picks up the colour.
            if (appearance.HasValue && limbMat != null)
                limbMat.color = appearance.Value.Skin;
            FacingRotation = facing;
            // The keeper (withGloves) gets extra-thick limb hitboxes so any bit of arm/leg/foot
            // stops the ball. The striker keeps its normal (already-fattened) hitboxes.
            _keeperHb = withGloves ? SimConfig.KeeperHitboxBoost : 1f;

            _targetRoot = Make.Empty("TargetSkeleton", basePos, transform).transform;
            _targetRoot.rotation = facing;
            _physRoot = Make.Empty("PhysicsSkeleton", basePos, transform).transform;
            _physRoot.rotation = facing;

            // The skeleton comes from a per-BodyPlan table. A quadruped is not a new skeleton: it
            // is the same 13 bones REPOSED (barrel for the torso, hind legs for the legs, front
            // legs for the arms), so nothing downstream changes. See BodyLayout.
            //
            // Read the PASSED appearance, never the global PlayerProfile: this same builder makes
            // AI keepers, crossers, menu-background actors and remote peers, and they must not
            // inherit the local player's species. A null appearance is an AI body: Human.
            _speciesId = appearance?.SpeciesId ?? Species.HumanId;
            _layout = BodyLayout.ForSpecies(_speciesId);
            // Free-root balance compensation, once per build. Vector3.one for every biped, so the
            // human path is untouched. See BodyLayout.RootDriveMul for why it exists at all.
            _rootDriveMul = BodyLayout.RootDriveMul(_layout, _hScale, _gScale);
            _yawSettleRate = ReferenceEquals(_layout, BodyLayout.Biped) ? 0f : SimConfig.StandYawSettleRate;
            var bones = _layout.Bones;

            // Standing pelvis height for the carry servo, from the table rather than measured: a
            // measurement taken at spawn would capture whatever half-settled height the body
            // happened to be at on its first frame.
            int pelvisRow = _layout.IndexOf(Bone.Pelvis);
            _standPelvisY = (pelvisRow >= 0 ? bones[pelvisRow].Pos.y : 1f) * _hScale;

            // --- target skeleton: nested empties at the physics parts' centres, so their local
            // rotations map 1:1 to the joints. The table is in Bone enum order with every parent
            // ahead of its child, so a single forward pass places the whole hierarchy.
            //
            // The offset is ROTATED by `facing`, exactly as SnapLayout does it and for exactly the
            // reason documented there: a layout offset is in the BODY's frame, so it has to orbit
            // with the body. Build used to skip the rotation while still applying `facing` to the
            // rotations, which built a body whose bones were laid out for straight-ahead but turned
            // to face somewhere else. On a biped that is nearly invisible (the arms and legs sit at
            // +/-0.26 and +/-0.11 in x, so a 180 turn swaps left and right and little else). On a
            // QUADRUPED it is not: its bones run down +/-Z, so a keeper built facing -Z got a barrel
            // and a head hung off the BACK of the pelvis, and he read as standing backwards. Worse,
            // it was permanent - AddJoint autoconfigures its connected anchors, so the wrong
            // arrangement is baked into the joints and ResetTo's correct snap gets dragged back to
            // it. Rotating here is the fix, and it also puts Build, SnapLayout, DisplaySnap and
            // AddDecor (whose "facing cancels out" algebra assumes precisely this) on one
            // convention.
            for (int i = 0; i < bones.Length; i++)
            {
                var s = bones[i];
                Vector3 p = s.TargetPos ?? s.Pos;
                MakeTarget(s.Bone, s.IsRoot ? _targetRoot : _target[(int)s.Parent],
                           basePos + facing * Off(p.x, p.y, p.z), facing * Quaternion.Euler(s.RestEuler));
            }

            // Capture the target bones' rest LOCAL rotations. Identity for every biped bone (they
            // are all built aligned to 'facing'), non-identity wherever a layout pitches a bone.
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_target[i] != null) _targetRestLocal[i] = _target[i].localRotation;

            // --- physics skeleton ---
            // Only the bone the table marks WearsJersey (the torso) uses torsoMat, which may carry
            // the painted texture. Everything else uses limbMat so the jersey art does NOT bleed
            // onto the shorts or the head.
            for (int i = 0; i < bones.Length; i++)
            {
                var s = bones[i];
                MakePart(s, s.IsRoot ? _physRoot : Phys(s.Parent),
                         basePos + facing * Off(s.Pos.x, s.Pos.y, s.Pos.z),   // see the target pass above
                         facing * Quaternion.Euler(s.RestEuler),
                         s.WearsJersey ? torsoMat : limbMat, HitboxScale(s.Hitbox));
            }

            // Frictionless ground-contact bones (the feet, plus a quadruped's front hooves).
            // Grounding is a pelvis SphereCast, not foot contact, so slick bones slide over the
            // turf instead of catching and making the run janky. Minimum friction-combine forces
            // the contact to ~0 regardless of the turf's own value.
            //
            // Held in a field, not a local, because AddDecor below needs it too. On a quadruped the
            // BONE flag alone was never enough: a hoof or a pad box hangs lower than the leg capsule
            // it is bolted to, so the decor is the surface in contact and the slick capsule above it
            // never touches turf. That is what made the gait catch.
            _slickMat = Make.PhysMat("Feet", 0f, 0f, 0f,
                                     PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);
            for (int i = 0; i < bones.Length; i++)
            {
                if (!bones[i].Slick) continue;
                var srb = _rb[(int)bones[i].Bone];
                if (srb != null) srb.GetComponent<Collider>().material = _slickMat;
            }

            // Joints: child -> parent (connectedBody). The root pelvis has no joint (free root).
            for (int i = 0; i < (int)Bone.Count; i++) _driveMul[i] = 1f;
            for (int i = 0; i < bones.Length; i++)
            {
                var s = bones[i];
                if (s.IsRoot) continue;
                // Rotational inertia goes as mass times lever squared, so a bone's drive
                // compensation is its table base times _massMul times _hScale squared. Height, not
                // girth: both quadruped levers that matter (the barrel's own half-length and the
                // head's 1.03 offset) lie in the y-z plane, which LengthAlongHeight puts on
                // _hScale. Zero in the table means no compensation, which is every biped bone.
                if (s.DriveMul > 0f)
                    _driveMul[(int)s.Bone] = s.DriveMul * _massMul * _hScale * _hScale;
                AddJoint(s.Bone, s.Parent, Off(s.JointAnchor.x, s.JointAnchor.y, s.JointAnchor.z));
            }

            // Species appendages. BEFORE the self-collision sweep, so the one pairwise pass below
            // covers decor too and no appendage can ever jam the rig against its own body.
            AddDecor(appearance, limbMat, facing);

            IgnoreSelfCollisions();

            // Big white gloves at the hand end of each forearm (keeper only).
            if (withGloves)
            {
                AddGlove(Bone.ForearmL);
                AddGlove(Bone.ForearmR);
            }

            // Head cosmetics for a real player (hair/facial/accessory). Purely visual: attached
            // as collider-less children of the head, never registered as hitboxes, so they never
            // affect the ball. Skipped entirely for AI bodies (appearance == null).
            if (appearance.HasValue)
                Cosmetics.AttachAppearance(this, appearance.Value);
        }

        void AddGlove(Bone forearm)
        {
            var rb = _rb[(int)forearm];
            if (rb == null) return;
            // Big white glove at the hand end of the forearm, WITH a hitbox: its sphere
            // collider is added to the forearm rigidbody (as a child object sharing the
            // body) and registered as an own-collider so it doesn't self-collide.
            var gloveMat = Make.Mat(Color.white, 0.2f);
            _ownedMats.Add(gloveMat);
            var glove = Make.Sphere("Glove", 0.32f, rb.transform.position, gloveMat, rb.transform);
            glove.transform.localPosition = new Vector3(0f, -0.19f, 0f);
            glove.transform.localScale = Vector3.one * 0.32f;
            var sc = glove.GetComponent<SphereCollider>();  // keep + use as the hitbox
            if (sc != null)
            {
                // Local radius; *0.32 scale. Boosted by _keeperHb AND an extra glove-reach
                // factor so the hand hitbox reaches well past the visible glove - a dive
                // connects on a near-miss for more dramatic saves.
                sc.radius = 0.5f * _keeperHb * SimConfig.KeeperGloveReach;
                _ownColliders.Add(sc);
                // Ignore collisions with every existing own-collider (self-collision).
                for (int i = 0; i < _ownColliders.Count - 1; i++)
                    if (_ownColliders[i] != null) Physics.IgnoreCollision(sc, _ownColliders[i], true);
            }
        }

        /// <summary>
        /// Build this species' appendages: a horse's neck, muzzle, ears, mane and hooves, an
        /// elephant's trunk, ears and tusks. Each is a child object of an existing bone and shares
        /// that bone's Rigidbody, which is how the body gains parts without a 14th Bone member.
        /// Same recipe as AddGlove, generalised and driven from BodyLayoutDef.Decor.
        ///
        /// ONE Dims drives both the mesh and the collider and NOTHING here applies a colliderScale,
        /// so a Solid piece is FLUSH with what you see by construction rather than by upkeep.
        ///
        /// A piece with a GateMask only draws when the appearance's pick for its slot matches, which
        /// is what makes the animal EARS / MARKINGS / TUSKS / TACK pickers do anything at all. The
        /// index comes from the PASSED appearance, never PlayerProfile: the same builder makes AI
        /// keepers, crossers, menu actors and remote peers, so reading the global profile would dress
        /// every body on the pitch in the local player's choices.
        /// </summary>
        // Every built decor piece by name with its SCALED dims, so a cosmetic pass can dress the
        // real geometry (a noseband around the built muzzle box, a sleeve on the built neck).
        readonly System.Collections.Generic.Dictionary<string, (Transform t, Vector3 dims)> _decorBuilt = new System.Collections.Generic.Dictionary<string, (Transform, Vector3)>();
        public bool TryGetDecor(string name, out Transform t, out Vector3 scaledDims)
        {
            if (_decorBuilt.TryGetValue(name, out var e) && e.t != null) { t = e.t; scaledDims = e.dims; return true; }
            t = null; scaledDims = Vector3.zero; return false;
        }

        void AddDecor(PlayerAppearance? appearance, Material limbMat, Quaternion facing)
        {
            var decor = _layout.Decor;
            if (decor == null) return;
            _decorBuilt.Clear();

            // One material per tint, not per piece: Make.Mat allocates, and the species preview
            // rebuilds this body on every drag frame.
            var tintMats = new Material[6];

            for (int i = 0; i < decor.Length; i++)
            {
                var d = decor[i];
                if (!DecorGatePasses(d, appearance)) continue;
                int pi = _layout.IndexOf(d.Parent);
                var rb = pi < 0 ? null : _rb[(int)d.Parent];
                if (rb == null) continue;

                // BODY frame -> the parent bone's LOCAL frame. The parent was built at
                // facing * Euler(RestEuler) and the body frame IS facing, so peeling off the rest
                // rotation is the entire conversion and facing cancels out. Exact rather than
                // approximate, because every rest rotation in both tables is axis aligned and every
                // bone root sits at unit scale.
                var toLocal = Quaternion.Inverse(Quaternion.Euler(_layout.Bones[pi].RestEuler));

                var dims = d.GirthDims ? d.Dims * _gScale : ScaleDims(d.Kind, d.Dims);
                var mat  = d.Hidden ? null : DecorMaterial(d.Tint, appearance, limbMat, tintMats);

                // GirthOffset pieces scale on girth alone, so they hold a fixed fraction of a
                // girth-scaled radius instead of drifting against it. See DecorSpec.GirthOffset.
                var raw = d.GirthOffset
                        ? d.Offset * _gScale
                        : Off(d.Offset.x, d.Offset.y, d.Offset.z);

                var go = new GameObject(d.Name);
                _decorBuilt[d.Name] = (go.transform, dims);
                go.transform.SetParent(rb.transform, false);
                go.transform.localPosition = toLocal * raw;
                go.transform.localRotation = toLocal * Quaternion.Euler(d.Euler);
                go.transform.localScale    = Vector3.one; // the collider must stay unscaled

                Collider col = null;
                GameObject visual;
                switch (d.Kind)
                {
                    case ColliderKind.Sphere:
                        if (d.Solid)
                        {
                            var sc = go.AddComponent<SphereCollider>();
                            sc.radius = dims.y > 0f ? dims.y : dims.x;
                            col = sc;
                        }
                        visual = d.Hidden ? null : Make.Sphere("v", dims.x * 2f, go.transform.position, mat, go.transform);
                        break;
                    case ColliderKind.CapsuleY:
                        if (d.Solid)
                        {
                            var cc = go.AddComponent<CapsuleCollider>();
                            cc.direction = 1; // Y
                            cc.radius = dims.x;
                            cc.height = dims.y;
                            col = cc;
                        }
                        visual = d.Hidden ? null : Make.Capsule("v", dims.x, dims.y, go.transform.position, mat, go.transform);
                        break;
                    default: // Box
                        if (d.Solid)
                        {
                            var bc = go.AddComponent<BoxCollider>();
                            bc.size = dims;
                            col = bc;
                        }
                        visual = d.Hidden ? null : Make.Box("v", dims, go.transform.position, mat, go.transform, collider: false);
                        break;
                }

                if (visual != null)   // Hidden rows: collider only, Cosmetics draws the piece
                {
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.identity;
                    var vcol = visual.GetComponent<Collider>();
                    if (vcol != null) Destroy(vcol);
                }

                if (col != null)
                {
                    _ownColliders.Add(col);
                    // The map BallController reads through BoneOf. Without this a muzzle strike is an
                    // unrecognised collider and falls through to the scrappy-touch branch instead of
                    // being the header it looks like.
                    _decorOwner[go.transform] = d.Parent;

                    // Ground-contact decor gets the same frictionless material the slick BONES get.
                    // This is the piece that was missing: a hoof or a pad hangs below its leg's
                    // capsule, so it, not the capsule, is what the turf touches. Without it the leg
                    // flag was decorative and every stride bit into the ground.
                    if (d.Slick && _slickMat != null) col.material = _slickMat;
                }
            }
        }

        /// <summary>
        /// Whether a decor piece is drawn for this appearance's style picks.
        ///
        /// GateMask 0 is ANATOMY and always passes: a neck, a trunk, a hoof. Anything else is an
        /// option, and it draws only when the live index for its slot has the matching bit set. The
        /// mask is a bitfield rather than an index so ONE spec can serve several options, which is how
        /// a long tusk is shared by "Long" and "Banded" while a separate ring adds only the band.
        ///
        /// An out-of-range index simply matches nothing, so a saved profile from a longer list
        /// degrades to bare anatomy instead of throwing. A body with no appearance at all is an AI
        /// build (always Human, which has no decor), so gated pieces are skipped there.
        /// </summary>
        bool DecorGatePasses(in DecorSpec d, PlayerAppearance? a)
        {
            if (d.GateMask == 0) return true;
            if (!a.HasValue) return false;
            int idx;
            switch (d.Gate)
            {
                case SlotKind.StyleA: idx = a.Value.HairStyle;   break;
                case SlotKind.StyleB: idx = a.Value.FacialStyle; break;
                case SlotKind.StyleC: idx = a.Value.Accessory;   break;
                default: return true;   // Skin is a colour, never an index. Treat as ungated.
            }
            if (idx < 0 || idx > 30) return false;
            return (d.GateMask & (1 << idx)) != 0;
        }

        /// <summary>
        /// The colour a decor piece is painted, resolved from the appearance slots the customize
        /// screen already exposes (see the SPECIES REINTERPRETATION table on PlayerAppearance). Those
        /// pickers existed and drew nothing until there was animal geometry to paint.
        /// Cached per tint in <paramref name="cache"/>. Registered for teardown, since destroying a
        /// GameObject does not free its materials and the preview rebuilds constantly.
        /// </summary>
        Material DecorMaterial(DecorTint tint, PlayerAppearance? a, Material limbMat, Material[] cache)
        {
            if (tint == DecorTint.Limb) return limbMat;
            int k = (int)tint;
            if (cache[k] != null) return cache[k];

            // No appearance means an AI body, which is always Human and so has no decor at all. Fall
            // back to the limb material rather than inventing a colour, defensively.
            if (!a.HasValue && tint != DecorTint.Dark) return limbMat;

            Color c;
            switch (tint)
            {
                case DecorTint.Skin:   c = a.Value.Skin; break;
                case DecorTint.StyleA: c = a.Value.HairColor; break;
                case DecorTint.StyleB: c = a.Value.FacialColor; break;
                case DecorTint.StyleC: c = a.Value.AccessoryColor; break;
                default:               c = new Color(0.12f, 0.10f, 0.09f); break; // Dark: keratin
            }
            var m = Make.Mat(c, 0.15f);
            RegisterCosmeticMaterial(m);
            cache[k] = m;
            return m;
        }

        /// <summary>
        /// Build-scale a Dims triple, at this body's scale. Height on the length axis, girth on the
        /// two cross axes, which is per collider kind. Used by MakePart and AddDecor so a bone and an
        /// appendage hung off it cannot scale differently and slide apart on a tall or wide build.
        ///
        /// Forwards to BodyLayout, which also needs it while estimating a layout's inertia and so
        /// cannot go through an instance. One implementation, two callers with different scales.
        /// </summary>
        Vector3 ScaleDims(ColliderKind kind, Vector3 dims)
            => BodyLayout.ScaleDims(kind, dims, _hScale, _gScale);

        // Layout offset, at this body's scale. Forwards to BodyLayoutDef.Off for the same reason
        // ScaleDims does: heights (y) scale with the build height, and lateral spacing (x) with
        // girth so a wider body's limbs sit further out. Z is girth for a biped, but HEIGHT for a
        // quadruped, whose front-to-back span is length and has to track the barrel (see
        // BodyLayoutDef.LengthAlongHeight). Default build = 1.
        Vector3 Off(float x, float y, float z)
            => _layout.Off(new Vector3(x, y, z), _hScale, _gScale);

        // How far a bone's collider is fattened past its visible mesh. Leg and foot hitboxes are
        // fattened so the ball connects reliably instead of clipping through, and the keeper
        // (_keeperHb) fattens them further still so a save connects off any part of a limb. Arms
        // invert that: they are razor thin, and the OUTFIELD body wants them FATTER than the
        // keeper's so an arm touch reliably traps the ball instead of glancing off. _keeperHb > 1
        // is what identifies the keeper.
        float HitboxScale(HitboxClass k)
        {
            switch (k)
            {
                case HitboxClass.Leg:  return SimConfig.LegHitboxScale * _keeperHb;
                case HitboxClass.Foot: return 1.6f * _keeperHb;
                case HitboxClass.Arm:
                    return (_keeperHb > 1f ? SimConfig.ArmHitboxScale : SimConfig.StrikerArmHitboxScale) * _keeperHb;
                default: return 1f;
            }
        }

        void MakeTarget(Bone b, Transform parent, Vector3 worldPos, Quaternion rot)
        {
            var go = Make.Empty("T_" + b, worldPos, parent);
            go.transform.rotation = rot;   // facing * the bone's rest rotation from the layout
            _target[(int)b] = go.transform;
        }

        void MakePart(in BoneSpec spec, Transform parent, Vector3 worldPos, Quaternion rot,
                      Material mat, float colliderScale)
        {
            Bone b = spec.Bone;
            ColliderKind kind = spec.Kind;
            Vector3 dims = spec.Dims;
            float mass = spec.Mass;
            // Apply the build scale to this part: girth widens X/Z (and capsule radius),
            // height lengthens the vertical extent, and mass scales with weight. For a
            // CapsuleY, dims.x is radius (girth) and dims.y is length (height). For a
            // Sphere, dims.x is visible radius and dims.y an optional collider-radius
            // override (both girth). For a Box, dims = full size (x/z girth, y height).
            dims = ScaleDims(kind, dims);
            mass *= _massMul;

            var go = new GameObject("P_" + b);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.rotation = rot;
            go.transform.localScale = Vector3.one; // never scale a physics bone root

            Collider col;
            GameObject visual;
            switch (kind)
            {
                case ColliderKind.Sphere:
                {
                    var sc = go.AddComponent<SphereCollider>();
                    // dims.y (if > 0) is a collider-radius override so the hitbox can be
                    // bigger than the visible sphere (a generous header hitbox). It stays
                    // CENTRED on the visible head so it never reaches out in front.
                    sc.radius = dims.y > 0f ? dims.y : dims.x;
                    col = sc;
                    visual = Make.Sphere("v", dims.x * 2f, worldPos, mat, go.transform);
                    break;
                }
                case ColliderKind.CapsuleY:
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.direction = 1; // Y
                    // colliderScale thickens the hitbox radius beyond the visible capsule
                    // (used to fatten thin arms so the ball stops phasing through them).
                    cc.radius = dims.x * colliderScale;
                    cc.height = dims.y;
                    col = cc;
                    visual = Make.Capsule("v", dims.x, dims.y, worldPos, mat, go.transform);
                    break;
                }
                default: // Box
                {
                    var bc = go.AddComponent<BoxCollider>();
                    // Enlarge the hitbox on X/Z only (not vertical) so a bigger foot
                    // collider doesn't poke through the ground and cause jitter.
                    bc.size = new Vector3(dims.x * colliderScale, dims.y, dims.z * colliderScale);
                    col = bc;
                    // The torso wears the jersey: give it a custom-UV box so the painted atlas maps
                    // onto the body upright and undoubled. WHICH faces get the art is per body plan
                    // (spec.JerseyFaces): chest/back for an upright biped torso, flanks/spine for a
                    // quadruped barrel, which rests pitched 90 deg. Other boxes (feet, a quadruped's
                    // pelvis) use the plain primitive cube.
                    visual = spec.WearsJersey
                        ? Make.JerseyBox("v", dims, worldPos, mat, go.transform, spec.JerseyFaces)
                        : Make.Box("v", dims, worldPos, mat, go.transform, collider: false);
                    break;
                }
            }
            // The visual is a child; make it follow the bone exactly and never collide.
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            var vcol = visual.GetComponent<Collider>();
            if (vcol != null) Destroy(vcol);
            _ownColliders.Add(col);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.solverIterations = 24;
            rb.solverVelocityIterations = 12;
            rb.maxAngularVelocity = 40f;
            rb.angularDamping = 0.05f;
            rb.linearDamping = 0.05f;
            _rb[(int)b] = rb;
        }

        void AddJoint(Bone child, Bone parent, Vector3 anchorLocal)
        {
            var rb = _rb[(int)child];
            var j = rb.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = _rb[(int)parent];
            j.autoConfigureConnectedAnchor = true;
            j.anchor = anchorLocal;

            j.xMotion = ConfigurableJointMotion.Locked;
            j.yMotion = ConfigurableJointMotion.Locked;
            j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = ConfigurableJointMotion.Free;
            j.angularYMotion = ConfigurableJointMotion.Free;
            j.angularZMotion = ConfigurableJointMotion.Free;

            j.rotationDriveMode = RotationDriveMode.Slerp;
            j.enablePreprocessing = false;
            j.projectionMode = JointProjectionMode.PositionAndRotation;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 15f;

            // axis / secondaryAxis define the joint space used by SetTargetRotationLocal.
            j.axis = Vector3.right;          // local X
            j.secondaryAxis = Vector3.up;    // local Y

            ApplyDrive(j, 1f, _driveMul[(int)child]);
            _lastDriveScale = float.NaN;   // a new joint: FixedUpdate rewrites every drive next step

            _joint[(int)child] = j;
            // The child's rotation RELATIVE TO ITS PARENT at build time.
            // JointMath.SetTargetRotationLocal computes Inverse(target) * start, so the drive
            // relaxes to identity exactly when the requested local rotation equals this. Storing
            // the real build-time value instead of assuming identity is what lets a layout pitch a
            // bone (the quadruped barrel): the rest offset then cancels against _targetRestLocal at
            // the zero pose, so the bone sits relaxed instead of fighting its own joint. Every
            // biped bone is built aligned to 'facing', so this is still identity there.
            _jointStartLocal[(int)child] = Quaternion.Inverse(_rb[(int)parent].rotation) * rb.rotation;
        }

        // scale is the global 0..1 limpness knob; boneMul is the layout's per-bone inertia
        // compensation. All three drive terms take boneMul, so multiplying spring and damper by the
        // same factor leaves the natural frequency and the damping ratio where the biped has them
        // instead of producing a stiff, ringy bone. boneMul is 1 for every biped bone, and
        // 6500*1, 150, 60000 is exactly what this used to write.
        void ApplyDrive(ConfigurableJoint j, float scale, float boneMul)
        {
            var drive = new JointDrive
            {
                positionSpring = SimConfig.JointSpring * scale * boneMul,
                positionDamper = SimConfig.JointDamper * boneMul,
                maximumForce = SimConfig.JointMaxForce * boneMul
            };
            j.slerpDrive = drive;
        }

        void IgnoreSelfCollisions()
        {
            for (int i = 0; i < _ownColliders.Count; i++)
                for (int k = i + 1; k < _ownColliders.Count; k++)
                    Physics.IgnoreCollision(_ownColliders[i], _ownColliders[k], true);
        }

        // ----------------------------------------------------------------- pose
        public void SetPose(Vector3[] pose, float speed = 6f)
        {
            if (pose == _poseTo) return;
            // start the blend from wherever we are now
            _poseFrom = CurrentBlend();
            _poseTo = pose;
            _poseT = 0f;
            _poseSpeed = speed;
        }

        Vector3[] CurrentBlend()
        {
            var res = new Vector3[(int)Bone.Count];
            for (int i = 0; i < res.Length; i++)
                res[i] = Vector3.Lerp(_poseFrom[i], _poseTo[i], _poseT);
            return res;
        }

        // -------------------------------------------------------------- driving
        void FixedUpdate()
        {
            if (Pelvis == null) return;

            FloorRescue();

            _poseT = Mathf.Min(1f, _poseT + Time.fixedDeltaTime * _poseSpeed);

            // Keep the whole target skeleton oriented to the current facing, so when the
            // body turns (e.g. facing the mouse while idle) the limb pose turns with it
            // instead of the legs fighting the old direction it was built in.
            if (_targetRoot != null) _targetRoot.rotation = FacingRotation;

            // 1) Push the target skeleton toward the blended pose + additive override.
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_target[i] == null) continue;
                Vector3 e = Vector3.Lerp(_poseFrom[i], _poseTo[i], _poseT) + _poseOverride[i];
                _target[i].localRotation = _targetRestLocal[i] * Quaternion.Euler(e);
            }

            // 2) Drive each joint's target rotation to match its target bone. The drive itself
            // (spring / damper / max force) only ever changes with DriveScale, and writing a joint's
            // slerpDrive is a native joint reconfiguration - 13 per body per physics step, 264 in a
            // full match - so it is written only when the scale actually moved. The target rotation
            // is the real per-step work and is always written.
            bool driveDirty = DriveScale != _lastDriveScale;
            if (driveDirty) _lastDriveScale = DriveScale;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                var j = _joint[i];
                if (j == null) continue;
                if (driveDirty) ApplyDrive(j, DriveScale, _driveMul[i]);
                Quaternion targetLocal = _target[i].localRotation; // relative to parent target
                j.SetTargetRotationLocal(targetLocal, _jointStartLocal[i]);
            }

            UpdateGrounded();

            // 3) Upright lock vs. free balance.
            if (UprightLock)
            {
                ApplyUprightLock();
                // Standing still on a non-biped body: bleed off any yaw ring. Gated on rest because
                // that is where it is visible and where it can cost nothing.
                if (_yawSettleRate > 0f && IsGrounded && MoveInput.sqrMagnitude < 0.01f)
                    SettleYawRing();
            }
            else if (DiveYawLock)
            {
                ApplyDiveYawLock();
            }
            else
            {
                ReleaseUprightLock();
                if (BalanceEnabled)
                    // _rootDriveMul is the whole fix for the quadruped standing sway: the torque here
                    // is sized against the PELVIS tensor but has to turn the welded body, so a
                    // barrel-and-four-legs assembly got a fraction of the commanded acceleration and
                    // with it a fraction of the damping. One for a biped, so a human is unchanged.
                    JointMath.DriveTowardRotation(Pelvis, FacingRotation,
                        SimConfig.BalanceFrequency, SimConfig.BalanceDamping, _rootDriveMul);
                else if (BodyOrientTarget.HasValue)
                    DrivePelvisOrientation(BodyOrientTarget.Value);
            }

            ApplyLocomotion();

            // WHOLE-BODY CARRY. See SimConfig.CarryHeightGain for why this exists: the hips were
            // never held at a height, so the body sagged and lurched once per stride. The gait's
            // limbs are cosmetic pose overrides, but they are still real colliders on real joints, so
            // a swung leg meeting the turf levers the whole assembly up and the next one drops it.
            // No leg pose can fix that. Holding the hips at their authored standing height does.
            //
            // It is applied as a SHARED VERTICAL DELTA, not as one assigned velocity. Assigning the
            // same v.y to every bone would freeze the limbs' vertical motion relative to the body and
            // fight the joint drives that pick the feet up; adding the same delta to all of them
            // translates the assembly while leaving every relative motion, including the stride,
            // exactly as the drives made it. The hips land on the target velocity either way.
            //
            // The gates are the containment. UprightLock is cleared by every jump, dive, trick,
            // keeper lay-out and knockdown, LocomotionEnabled by a committed launch, and an emote
            // owns the height itself - so nothing but a grounded, upright, self-driven body is
            // touched. The error clamp is the other half: a body further off its stand height than
            // CarryErrUp/Down is mid-event, and gravity keeps it.
            // _floorValid, NOT IsGrounded, is the gate that matters here. IsGrounded is set before the
            // floor filters in UpdateGrounded and can never go false once any collider is under the
            // probe, so gating on it let the servo run against the FABRICATED fallback _groundY - which
            // is exactly how a body got driven into the turf. _floorValid is false whenever the probe
            // did not actually identify a floor, and then the servo simply does not run and gravity has
            // the body. IsGrounded is kept in the condition because its other meaning (are we airborne)
            // is still required.
            //
            // Note this deliberately keeps the error clamp TWO-SIDED. A one-sided lift-only servo was
            // considered and rejected: CarryErrDown exists because a swung leg meeting the turf levers
            // the whole assembly up and the next stride drops it (see the SimConfig comment), so the
            // downward half is real work, not a hazard. The hazard was never the direction, it was
            // acting on a floor height that had been invented.
            if (UprightLock && IsGrounded && _floorValid && LocomotionEnabled && EmoteHeightOffset == 0f)
            {
                float err = Mathf.Clamp((_groundY + _standPelvisY) - Pelvis.position.y,
                                        -SimConfig.CarryErrDown, SimConfig.CarryErrUp);
                float vy = Mathf.Clamp(err * SimConfig.CarryHeightGain,
                                       -SimConfig.CarryHeightMaxSpeed, SimConfig.CarryHeightMaxSpeed);
                // Bounded, so the most this can ever add to any single bone in one tick is
                // CarryHeightMaxSpeed. A hard landing still needs a few ticks to be absorbed, and a
                // kicking foot's velocity is never disturbed by more than that.
                float dv = Mathf.Clamp(vy - Pelvis.linearVelocity.y,
                                       -SimConfig.CarryHeightMaxSpeed, SimConfig.CarryHeightMaxSpeed);
                for (int i = 0; i < (int)Bone.Count; i++)
                {
                    if (_rb[i] == null) continue;
                    var cv = _rb[i].linearVelocity;
                    cv.y += dv;
                    _rb[i].linearVelocity = cv;
                }
            }

            // Emote vertical bob (e.g. push-ups): while set, drive the pelvis toward its rest
            // height plus this offset with a strong PD so the whole body visibly rises/lowers.
            // Captured rest height is taken on the first frame the offset goes non-zero.
            if (EmoteHeightOffset != 0f)
            {
                if (!_emoteHeightBased) { _emoteHeightRestY = Pelvis.position.y; _emoteHeightBased = true; }
                float wantY = _emoteHeightRestY + EmoteHeightOffset;
                var v = Pelvis.linearVelocity;
                v.y = (wantY - Pelvis.position.y) * 36f;   // PD toward the target height
                Pelvis.linearVelocity = v;
            }
            else _emoteHeightBased = false;
        }

        // Vertical body offset (metres) an emote wants applied to the live dynamic body. 0 = none.
        public float EmoteHeightOffset;
        bool _emoteHeightBased;
        float _emoteHeightRestY;

        // Directly steer the pelvis toward a target orientation by setting its angular
        // velocity along the shortest-arc error. Strong and reliable (unlike a weak PD
        // torque that the heavy jointed body swamps), so the keeper actually reaches and
        // holds a flat lay-out, and his yaw stays put (nothing to snap on recovery).
        /// <summary>Hard-snap the pelvis to a yaw-only orientation and kill its spin, so
        /// recovery faces exactly where intended with no wrong-way slew from a tumble.</summary>
        public void SnapFacing(Quaternion facing)
        {
            if (Pelvis == null) return;
            Quaternion yawOnly = Quaternion.Euler(0f, facing.eulerAngles.y, 0f);
            Pelvis.rotation = yawOnly;
            Pelvis.transform.rotation = yawOnly;
            Pelvis.angularVelocity = Vector3.zero;
        }

        void DrivePelvisOrientation(Quaternion target)
        {
            Quaternion delta = target * Quaternion.Inverse(Pelvis.rotation);
            if (delta.w < 0f) { delta.x = -delta.x; delta.y = -delta.y; delta.z = -delta.z; delta.w = -delta.w; }
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsInfinity(axis.x) || float.IsNaN(axis.x)) return;
            // Move a large fraction of the remaining error each step -> snaps flat very
            // fast then holds. High gain so the lay-out reaches horizontal near-instantly.
            Vector3 w = axis.normalized * (angle * Mathf.Deg2Rad) * 32f;
            Pelvis.angularVelocity = w;
        }

        void ApplyUprightLock()
        {
            // Constrain the pelvis so it can yaw but never pitch/roll: it cannot tip.
            Pelvis.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _lockApplied = true;

            // Steer yaw to the desired facing via MoveRotation only, and kill ALL angular
            // velocity. Preserving yaw spin (the old behaviour) let a ball impact or an
            // asymmetric gait spin the body up and MoveRotation could not overpower it -
            // the runaway "random spinning". With yaw fully MoveRotation-driven he turns
            // cleanly toward the target and never keeps spinning on his own.
            float curYaw = Pelvis.rotation.eulerAngles.y;
            float wantYaw = FacingRotation.eulerAngles.y;
            float yaw = Mathf.MoveTowardsAngle(curYaw, wantYaw, 900f * Time.fixedDeltaTime);
            Pelvis.MoveRotation(Quaternion.Euler(0f, yaw, 0f));
            Pelvis.angularVelocity = Vector3.zero;
            // Publish the yaw rate this step actually commanded, for SettleYawRing to measure against.
            _lockYawRate = Mathf.DeltaAngle(curYaw, yaw) * Mathf.Deg2Rad / Time.fixedDeltaTime;
        }

        /// <summary>
        /// Bleed off the residual YAW RING of the body hanging off an upright-locked pelvis: the
        /// left-right pendulum a standing quadruped settled into. Non-biped only, and only at rest.
        ///
        /// This is the same inertia problem _rootDriveMul solves, one level down the hierarchy, and it
        /// needed a different answer. UprightLock pins the PELVIS outright (MoveRotation plus zeroed
        /// spin), so the balance PD - and with it _rootDriveMul - is not even running while a body
        /// stands. What is left free is everything above the pelvis, ringing in yaw against the barrel's
        /// slerp drive. BoneSpec.DriveMul compensates that drive for the barrel's OWN mass and size, not
        /// for the head and four legs welded to it, so the yaw damping ratio there falls short exactly
        /// as the root's did. Then the geometry finishes the job: the same small angular ring that is
        /// invisible on a human's shoulders sweeps a 1.2 m muzzle far enough to read as a pendulum.
        ///
        /// Why not the obvious alternatives:
        ///  - Raising SimConfig.BalanceDamping changes the human, and does not run under the lock anyway.
        ///  - Raising the barrel's DriveMul scales spring and damper together, which holds the damping
        ///    ratio fixed and so cannot damp anything; it only stiffens the pose.
        ///  - Lifting BodyLayout.RootDriveMul's clamp of 24 (the horse's yaw axis asks up to 45.75)
        ///    would help the free-root path, but the sway complained about is the LOCKED one.
        ///
        /// Measured against the pelvis's COMMANDED yaw rate rather than against zero, so this is a
        /// damper on the DIFFERENTIAL only: a body following a turn is in rigid yaw with its pelvis,
        /// reads zero excess, and pays nothing. It is symmetric, which is deliberate - a bone LAGGING
        /// the commanded yaw gets pulled up toward it, never past it, so turning in place stops
        /// winding the lag into the barrel's drive spring, which is where the ring came from. Nothing
        /// else is touched: the vertical component of spin only, so a header nod (pitch), a lean
        /// (roll), the gait and the whole airborne path all pass through untouched.
        /// </summary>
        void SettleYawRing()
        {
            // Fraction of the excess removed this step, framed as an exponential decay so the result
            // does not depend on the physics rate.
            float k = 1f - Mathf.Exp(-_yawSettleRate * Time.fixedDeltaTime);
            Vector3 pivot = Pelvis.position;
            Vector3 wRef = Vector3.up * _lockYawRate;

            for (int i = 0; i < (int)Bone.Count; i++)
            {
                var rb = _rb[i];
                if (rb == null || rb == Pelvis) continue;   // the pelvis is already pinned by the lock

                // Spin: only the world-up component, and only its excess over the pelvis's own yaw.
                Vector3 w = rb.angularVelocity;
                float excess = Vector3.Dot(w, Vector3.up) - _lockYawRate;
                rb.angularVelocity = w - Vector3.up * (excess * k);

                // Momentum: a yaw ring also orbits each bone about the pelvis axis, and that linear
                // part is what would otherwise re-wind the spin the next step. ApplyLocomotion cannot
                // absorb it - it steers the body's AVERAGE horizontal velocity and pushes every bone
                // equally, so a differential orbit sums to nothing there and passes straight through.
                Vector3 r = rb.worldCenterOfMass - pivot; r.y = 0f;
                if (r.sqrMagnitude < 1e-6f) continue;
                Vector3 tHat = Vector3.Cross(Vector3.up, r).normalized;
                Vector3 v = rb.linearVelocity;
                // Excess over the velocity that rigid yaw with the pelvis would give this bone,
                // again so that following a turn is free.
                float vt = Vector3.Dot(v - Vector3.Cross(wRef, r), tHat);
                rb.linearVelocity = v - tHat * (vt * k);
            }
        }

        void ReleaseUprightLock()
        {
            if (!_lockApplied) return;
            Pelvis.constraints = RigidbodyConstraints.None;
            _lockApplied = false;
        }

        // Diving header: pin yaw and roll so the chest stays square-forward, but ACTIVELY
        // drive the pitch forward toward a face-down lay-out so he reliably tips into the
        // header instead of the stiff spine holding him upright. Rigidbody constraints
        // only freeze WORLD axes and the pitch axis rotates with facing, so we rebuild
        // the rotation each step as (locked yaw) * (pitch only) and set the pitch spin.
        void ApplyDiveYawLock()
        {
            ReleaseUprightLock();   // no world-axis constraints; we correct in code
            Quaternion yaw = Quaternion.Euler(0f, DiveYawFacing.eulerAngles.y, 0f);
            // Tilt = deviation of the pelvis from the locked yaw. Extract pitch about the
            // facing's right axis, discard yaw drift and roll (keeps the chest square).
            Quaternion tilt = Quaternion.Inverse(yaw) * Pelvis.rotation;
            float pitch = Mathf.DeltaAngle(0f, tilt.eulerAngles.x);
            Pelvis.MoveRotation(yaw * Quaternion.Euler(pitch, 0f, 0f));
            // Drive pitch toward the face-down target; angular velocity only about the
            // right axis so there is no yaw/roll twist.
            Vector3 rightAxis = yaw * Vector3.right;
            float err = DiveLayoutPitch - pitch;                         // deg toward face-down
            Pelvis.angularVelocity = rightAxis * (err * Mathf.Deg2Rad * SimConfig.DivePitchGain);
        }

        void ApplyLocomotion()
        {
            // Only steer horizontal velocity while grounded; airborne stays ballistic.
            if (!IsGrounded || !LocomotionEnabled) return;
            // Steer by the whole body's average horizontal velocity, and push EVERY
            // bone equally (acceleration = mass independent), so the character
            // translates rigidly instead of the light pelvis being swallowed by the
            // heavier joint-linked torso/legs.
            Vector3 horiz = AverageHorizontalVelocity();
            Vector3 desired = new Vector3(MoveInput.x, 0f, MoveInput.z);
            Vector3 delta = desired - horiz;
            // Cap scales with the current desired speed so sprint isn't throttled by
            // the base-speed cap.
            float capSpeed = Mathf.Max(SimConfig.StrikerMoveSpeed, desired.magnitude);
            Vector3 accel = Vector3.ClampMagnitude(delta * SimConfig.StrikerAccel,
                                                   SimConfig.StrikerAccel * capSpeed);
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_rb[i] != null) _rb[i].AddForce(accel, ForceMode.Acceleration);
        }

        Vector3 AverageHorizontalVelocity()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                Vector3 v = _rb[i].linearVelocity; sum += new Vector3(v.x, 0f, v.z); n++;
            }
            return n > 0 ? sum / n : Vector3.zero;
        }

        void UpdateGrounded()
        {
            IsGrounded = false;
            Vector3 origin = Pelvis.position;
            // Fallback so _groundY is never stale garbage if the probe misses. It is a FABRICATION -
            // a height below the body with no evidence behind it - so the servo must never act on it,
            // which is what _floorValid enforces. This value exists only so readers that want a number
            // get a plausible one rather than whatever was left from the last frame.
            _groundY = origin.y - _layout.GroundProbeDist * _hScale;
            // Cast scales with the build: a taller player's pelvis sits higher, so the
            // ground is further below it; a wider player needs a slightly wider probe.
            // The distance is the layout's pelvis rest height plus a margin, so a quadruped
            // (whose hips sit lower than a person's) probes a shorter way down.
            float radius = _layout.GroundProbeRadius * _gScale;
            float maxDist = _layout.GroundProbeDist * _hScale;
            // NonAlloc: this runs per body per physics tick, so SphereCastAll was allocating an array
            // 22 times a tick at 11-a-side - roughly 1,100 arrays a second, for a query whose results
            // are consumed and discarded immediately.
            int n = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, _probeHits, maxDist,
                                               ~0, QueryTriggerInteraction.Ignore);
            // Grounding is unchanged: any accepted collider under the probe means grounded.
            // Alongside it, find the TURF HEIGHT for the carry servo, which needs more care than
            // grounding does. SphereCast order is not defined, so the first accepted hit can be a goal
            // post or the side of some static prop rather than the ground: take the HIGHEST hit point
            // instead (the surface you would actually stand on) and reject anything that is not
            // FACING UP.
            //
            // THIS TEST USED TO BE A HEIGHT CUT, AND THAT WAS THE BODIES-SINK BUG. It read
            //     floorCut = origin.y - _standPelvisY * 0.5f;   ... if (h.point.y > floorCut) continue;
            // i.e. it decided what counted as floor RELATIVE TO THE BODY'S OWN CURRENT HEIGHT. Drop the
            // pelvis low enough - a slide, a dive, a knockdown, a keeper lay-out, or simply one bad
            // frame - and floorCut falls BELOW the actual turf, so the real floor hit is rejected,
            // floorFound stays false, and _groundY keeps the fabricated fallback set at the top of this
            // method (origin.y - GroundProbeDist), which is metres BELOW the body. The carry servo then
            // reads that as "you are too high" and drives the body DOWN, which lowers floorCut again.
            // Positive feedback, and the body walks itself into the turf.
            //
            // A surface normal does not depend on where the body happens to be, so it cannot run away:
            // turf faces up, a post or a wall faces sideways. 0.7 is about 45 degrees, which keeps a
            // ramp or a stand step as standable and rejects anything vertical.
            _floorValid = false;
            for (int i = 0; i < n; i++)
            {
                var h = _probeHits[i];
                if (h.collider == null) continue;
                if (IsOwn(h.collider)) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // ignore other dynamic bodies (ball)
                IsGrounded = true;
                // A zero distance means the cast started already overlapping and h.point/h.normal are
                // undefined there, so such a hit tells us nothing about the floor height.
                if (h.distance <= 1e-4f) continue;
                if (Vector3.Dot(h.normal, Vector3.up) < 0.7f) continue;   // not a floor, a wall
                if (!_floorValid || h.point.y > _groundY) { _groundY = h.point.y; _floorValid = true; }
            }

            // Recovery for the overlapped case above. If the sphere started inside something, every hit
            // was skipped and the servo would have no floor to work from; a zero-radius ray from inside
            // the pelvis still reports the surface below it. Cheap because it only runs when the sphere
            // cast produced nothing usable, which is rare.
            if (!_floorValid)
            {
                RaycastHit r;
                if (Physics.Raycast(origin, Vector3.down, out r, maxDist * 1.5f, ~0,
                                    QueryTriggerInteraction.Ignore)
                    && !IsOwn(r.collider)
                    && (r.rigidbody == null || r.rigidbody.isKinematic)
                    && Vector3.Dot(r.normal, Vector3.up) >= 0.7f)
                {
                    _groundY = r.point.y;
                    _floorValid = true;
                    IsGrounded = true;
                }
            }

            // AND IF BOTH PROBES FAILED WHILE THE PELVIS IS BELOW THE WORLD FLOOR, assume the world
            // floor. This is the hole the earlier fix left, and it made the earlier fix WORSE than what
            // it replaced for this one case: both probes cast DOWNWARD, so a body already beneath the
            // turf sees nothing, _floorValid goes false, and gating the carry servo on _floorValid then
            // switched off the only thing that could have lifted it. Measured at a pelvis of -0.02:
            // zero downward hits, recovery ray fails, servo disabled, body abandoned. Before the gate
            // existed the servo at least ran - on a fabricated ground height that drove it further
            // down, so it was broken either way, just differently.
            //
            // Assuming y = 0 is safe because every play surface's top face IS y = 0 (see
            // SimConfig.BodyFloorClampY). Worst case on a venue that ever changes that, the servo lifts
            // toward a slightly wrong height for the few frames until a real probe succeeds.
            if (!_floorValid && Pelvis.position.y < 0f)
            {
                _groundY = 0f;
                _floorValid = true;
            }
        }

        // Reused probe buffer. 8 is generous: the query is a short cast straight down from one pelvis,
        // and the loop takes the HIGHEST valid hit rather than the first, so a truncated result set
        // degrades to a slightly lower floor estimate rather than to a wrong one.
        readonly RaycastHit[] _probeHits = new RaycastHit[8];

        /// <summary>
        /// LAST-RESORT INVARIANT: no bone may sit below the world floor. Runs first thing every
        /// physics tick, before anything else can read a broken position.
        ///
        /// This exists because "players get stuck in the ground" survived three softer fixes, and each
        /// one failed for its own reason:
        ///   - the grounding probe casts DOWNWARD, so it cannot see a floor it is already beneath;
        ///   - the carry servo is gated on that probe succeeding, so it switches off in exactly the
        ///     situation where lifting is needed;
        ///   - continuous collision protects against fast MOTION, but ActiveRagdoll has eight direct
        ///     rb.position writes (SnapBone, SnapLayout, the two display-puppet paths, the free-kick
        ///     restore) and a position write is a teleport, with no sweep to stop it.
        /// So the guarantee cannot come from sensing or from physics. It has to be an assertion.
        ///
        /// MOVES THE WHOLE BODY, NOT ONE BONE. Lifting a single sunk bone drags it against its joints
        /// and can tear the ragdoll into a shape the drives then fight; translating every bone by the
        /// same deficit preserves the pose exactly and leaves the joints untouched.
        ///
        /// Kinematic bodies are skipped. A networked display puppet is positioned wholesale by the
        /// host every frame, so clamping it here would just fight the host and hide a desync.
        ///
        /// Cost: one compare per bone (13) plus, only when it fires, one write per bone. Nothing
        /// allocates. At 22 bodies that is 286 float compares a tick, which is noise.
        /// </summary>
        void FloorRescue()
        {
            // EmoteHeightOffset != 0 means the carry servo has already been handed off to a
            // deliberate, bounded, scripted height change (a sit, a slide) - see the gate one
            // function below this ("EmoteHeightOffset == 0f") for the same signal used the same
            // way. This clamp existed to catch a body teleported pathologically underground
            // (measured at pelvis -2, -6), not to fight an intentional low pose it was never tuned
            // against: at SitDrop's own 0.55 m the hips alone clear BodyFloorClampY (-0.30) fine,
            // but other bones legitimately sink past it in a full sit, and this was silently
            // un-doing the pose every tick before it could ever settle into view. A real physics
            // fall (a knockdown/back-landing tumble) does NOT set EmoteHeightOffset, so it is
            // unaffected and still gets the safety net.
            if (Pelvis.isKinematic || EmoteHeightOffset != 0f) return;

            float floor = SimConfig.BodyFloorClampY;
            float lowest = float.MaxValue;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                float y = _rb[i].position.y;
                if (y < lowest) lowest = y;
            }
            if (lowest >= floor || lowest == float.MaxValue) return;

            // Lift so the lowest bone lands ON the tolerance line rather than at the surface: dumping a
            // body at stand height would look like a teleport, and the carry servo will take it the rest
            // of the way now that _groundY resolves again.
            float lift = floor - lowest;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                _rb[i].position += new Vector3(0f, lift, 0f);
                // Kill DOWNWARD velocity only. Preserving the horizontal keeps a running body running,
                // and preserving an upward component keeps a legitimate jump or dive intact.
                var v = _rb[i].linearVelocity;
                if (v.y < 0f) { v.y = 0f; _rb[i].linearVelocity = v; }
            }
        }

        bool IsOwn(Collider c)
        {
            for (int i = 0; i < _ownColliders.Count; i++)
                if (_ownColliders[i] == c) return true;
            return false;
        }

        // ------------------------------------------------------------ utilities
        public void AddImpulseToPelvis(Vector3 impulse) => Pelvis.AddForce(impulse, ForceMode.Impulse);
        public void AddTorqueToPelvis(Vector3 torque) => Pelvis.AddTorque(torque, ForceMode.Impulse);

        /// <summary>
        /// Spin the WHOLE body rigidly about its centre: give every bone the same
        /// angular velocity plus the matching tangential linear velocity. This makes
        /// the character actually flip/recline as one piece, instead of the pelvis
        /// alone twisting against the joints (which only produces a spinal arch).
        /// angularVelDeg is degrees/second about the given world axis.
        /// </summary>
        public void SpinWholeBody(Vector3 axisWorld, float angularVelDeg)
        {
            SetBodyAngularVelocity(axisWorld.normalized * (angularVelDeg * Mathf.Deg2Rad));
        }

        /// <summary>
        /// Stop a whole-body spin cleanly. Zeroing angular velocity alone is NOT enough:
        /// SpinWholeBody also gave each bone a tangential LINEAR velocity so the body
        /// orbits its centre, and that leftover momentum keeps flinging the bones in
        /// circles (the "keeps rotating all the way around" bug). So also replace every
        /// bone's linear velocity with the shared centre-of-mass velocity, removing the
        /// tangential component while keeping the fall.
        /// </summary>
        public void StopBodySpin()
        {
            Vector3 comVel = Vector3.zero; float m = 0f;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                comVel += _rb[i].linearVelocity * _rb[i].mass; m += _rb[i].mass;
            }
            if (m > 0f) comVel /= m;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                _rb[i].angularVelocity = Vector3.zero;
                _rb[i].linearVelocity = comVel;
            }
        }

        void SetBodyAngularVelocity(Vector3 w)
        {
            // Base every bone on the SHARED centre-of-mass velocity plus the fresh
            // tangential term from the spin. Rebuilding from the COM velocity each frame
            // (instead of adding cross(w,r) onto the bone's existing velocity, which still
            // held LAST frame's tangential term) stops the orbital velocity accumulating -
            // that accumulation was the back-and-forth wobble when holding a spin.
            Vector3 center = CenterOfMass();
            Vector3 comVel = Vector3.zero; float m = 0f;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                comVel += _rb[i].linearVelocity * _rb[i].mass; m += _rb[i].mass;
            }
            if (m > 0f) comVel /= m;

            for (int i = 0; i < (int)Bone.Count; i++)
            {
                var rb = _rb[i];
                if (rb == null) continue;
                rb.angularVelocity = w;
                Vector3 r = rb.worldCenterOfMass - center;
                rb.linearVelocity = comVel + Vector3.Cross(w, r);
            }
        }


        Vector3 CenterOfMass()
        {
            Vector3 sum = Vector3.zero; float m = 0f;
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                sum += _rb[i].worldCenterOfMass * _rb[i].mass; m += _rb[i].mass;
            }
            return m > 0f ? sum / m : Pelvis.position;
        }

        /// <summary>Add a velocity change to every bone so the whole body leaps as one.</summary>
        public void AddVelocityToAll(Vector3 deltaV)
        {
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_rb[i] != null) _rb[i].AddForce(deltaV, ForceMode.VelocityChange);
        }

        // Rigidly translate every bone by `delta`, preserving the pose + velocities. Client-side
        // server reconciliation nudges a mispredicted local body back toward the authoritative
        // position without disturbing its stance or physics (unlike ResetTo, which re-stands it).
        public void ShiftAll(Vector3 delta)
        {
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                var rb = _rb[i];
                if (rb == null) continue;
                rb.position += delta;
                rb.transform.position += delta;
            }
        }

        /// <summary>Scale the horizontal (x/z) velocity of every bone, leaving vertical
        /// intact. Used to bleed off carried run momentum at jump time.</summary>
        /// <summary>Scale only the VERTICAL velocity of every bone (0 = kill the fall/rise). The
        /// header-hold landing dive uses it: launching from touchdown with the drop still in the
        /// bones would net most of the pop away.</summary>
        public void ScaleVerticalVelocity(float factor)
        {
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                Vector3 v = _rb[i].linearVelocity;
                _rb[i].linearVelocity = new Vector3(v.x, v.y * factor, v.z);
            }
        }

        public void ScaleHorizontalVelocity(float factor)
        {
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_rb[i] == null) continue;
                Vector3 v = _rb[i].linearVelocity;
                _rb[i].linearVelocity = new Vector3(v.x * factor, v.y, v.z * factor);
            }
        }

        /// <summary>Launch straight up: cancel all horizontal velocity and set a clean
        /// vertical speed on every bone (a pure jump with no sideways/backward drift).</summary>
        public void LaunchVerticalAll(float upSpeed)
        {
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_rb[i] != null) _rb[i].linearVelocity = new Vector3(0f, upSpeed, 0f);
        }

        /// <summary>Set an additive pose offset (Euler deg) for a bone, layered on the base pose.</summary>
        public void SetPoseOverride(Bone b, Vector3 euler) => _poseOverride[(int)b] = euler;

        /// <summary>ADD to a bone's pose override instead of replacing it, so two systems can share
        /// one bone. The run gait and a player leg-raise both drive the same limb: the gait writes
        /// its (already raise-scaled) stride first, then the raise adds on top, and the limb crosses
        /// between them without the pop that overwriting caused on release.</summary>
        public void AddPoseOverride(Bone b, Vector3 euler) => _poseOverride[(int)b] += euler;

        /// <summary>Measured horizontal speed of the whole body (m/s). The run cadence reads this
        /// rather than input magnitude, so the legs cycle at the speed the body is actually
        /// travelling instead of snapping to full rate the frame a key goes down.</summary>
        public float GroundSpeed => AverageHorizontalVelocity().magnitude;

        /// <summary>Turf height under the pelvis, from the last grounding probe. Only meaningful
        /// while <see cref="IsGrounded"/>.</summary>
        public float GroundY => _groundY;

        public void ClearPoseOverrides()
        {
            for (int i = 0; i < _poseOverride.Length; i++) _poseOverride[i] = Vector3.zero;
        }

        public float PelvisHeight => Pelvis != null ? Pelvis.position.y : 0f;

        public Bounds ApproxBounds()
        {
            var b = new Bounds(Pelvis.position, Vector3.one * 0.5f);
            for (int i = 0; i < _ownColliders.Count; i++)
                if (_ownColliders[i] != null) b.Encapsulate(_ownColliders[i].bounds);
            return b;
        }

        /// <summary>Hard reset to a standing pose at a base position (used on round reset).</summary>
        public void ResetTo(Vector3 basePos, Quaternion facing)
        {
            FacingRotation = facing;
            MoveInput = Vector3.zero;
            DriveScale = 1f;
            BalanceEnabled = true;
            LocomotionEnabled = true;
            BodyOrientTarget = null;
            DiveYawLock = false;
            UprightLock = true;
            _lockApplied = false;
            if (Pelvis != null) Pelvis.constraints = RigidbodyConstraints.None;
            ClearPoseOverrides();
            _poseFrom = RagdollPose.Stand;
            _poseTo = RagdollPose.Stand;
            _poseT = 1f;

            // Reposition every bone to its layout rest offset and zero velocities.
            SnapLayout(basePos, facing);
        }

        // Client-side display puppet: pose the whole body at basePos+facing WITHOUT touching
        // control flags (unlike ResetTo). Call once to make it a kinematic display body
        // (BecomeDisplayBody), then DisplaySnap each frame toward the interpolated host pose.
        // Bones are kept kinematic so client physics never fights the networked pose.
        public void BecomeDisplayBody()
        {
            BalanceEnabled = false;
            LocomotionEnabled = false;
            UprightLock = false;
            BodyOrientTarget = null;
            if (Pelvis != null) Pelvis.constraints = RigidbodyConstraints.None;
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_rb[i] != null) _rb[i].isKinematic = true;
        }

        /// <summary>
        /// The inverse of BecomeDisplayBody: a puppet becomes a simulated, self-driven body again.
        /// Needed when a seat changes hands mid-match - a client whose crosser puppet is suddenly
        /// its OWN predicted body must get its physics back, or it stands there kinematic and
        /// unmovable. Safe on a body that was never a puppet.
        /// </summary>
        public void BecomeLiveBody()
        {
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_rb[i] != null)
                {
                    _rb[i].isKinematic = false;
                    _rb[i].linearVelocity = Vector3.zero;
                    _rb[i].angularVelocity = Vector3.zero;
                }
            BalanceEnabled = true;
            LocomotionEnabled = true;
            UprightLock = true;
            BodyOrientTarget = null;
        }

        public void DisplaySnap(Vector3 basePos, Quaternion facing)
        {
            FacingRotation = facing;
            SnapLayout(basePos, facing);
        }

        // Place every bone at its layout rest offset from basePos with its layout rest rotation,
        // and zero its velocities. Shared by ResetTo (dynamic body, round reset) and DisplaySnap
        // (kinematic display puppet, every frame), so the two can never drift apart.
        //
        // The offset is ROTATED by `facing` so every bone ORBITS the vertical axis as the body
        // turns and the whole thing moves rigidly. Without the rotation, off-axis bones (a human's
        // arms at x +/-0.26, legs at x +/-0.11, feet at z 0.06) stay pinned at their forward-facing
        // spot while only their rotation yaws, so on a drag-turn the spine and feet appear to spin
        // while the arms and legs stay locked (the customize-preview tearing bug).
        void SnapLayout(Vector3 basePos, Quaternion facing)
        {
            var bones = _layout.Bones;
            for (int i = 0; i < bones.Length; i++)
            {
                var s = bones[i];
                // TargetPos ?? Pos, same as MakeTarget's target-skeleton build above - only the feet
                // differ (BodyLayout.TargetPos's own doc), and Pos is deliberately the LOOSE spawn
                // position the ankle joint then pulls up to TargetPos within a physics step or two.
                // A live ragdoll never shows the gap because that correction is near-instant; a
                // KINEMATIC snap (DisplaySnap - the customize-screen preview, no physics ever runs)
                // held it at the loose Pos forever, showing a visibly detached, floating foot.
                Vector3 p = s.TargetPos ?? s.Pos;
                SnapBone(s.Bone, basePos + facing * Off(p.x, p.y, p.z),
                         facing * Quaternion.Euler(s.RestEuler));
            }
        }

        void SnapBone(Bone b, Vector3 worldPos, Quaternion rot)
        {
            var rb = _rb[(int)b];
            if (rb == null) return;
            rb.position = worldPos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.transform.position = worldPos;
            rb.transform.rotation = rot;
        }

        // Display a networked EMOTE on a kinematic puppet (client side). Poses bone TRANSFORMS
        // directly (a kinematic body ignores the joint-drive pose system), rotating each bone by
        // its emote pose euler about the facing so the dance's limb motion is visible remotely.
        // Bones the emote doesn't pose stay at the rest orientation. emoteId indexes
        // Celebration.Emote; phase is 0..1. Root/limbs use the same rest offsets as DisplaySnap,
        // so an in-place dance (e.g. Griddy) reads correctly from the streamed root pos + phase.
        public void DisplayEmote(Vector3 basePos, Quaternion facing, int emoteId, float phase)
        {
            FacingRotation = facing;
            // Collect the emote's per-bone euler overrides for this phase.
            var over = _emoteScratch;
            for (int i = 0; i < over.Length; i++) over[i] = Vector3.zero;
            var e = (Celebration.Emote)emoteId;
            float pc = Mathf.Clamp01(phase);
            EmotePose.Apply(e, pc, (bone, euler) => over[(int)bone] = euler);
            // Whole-body vertical bob (e.g. push-ups drop into a plank + pump up/down).
            basePos.y += EmotePose.RootLift(e, pc);
            // Place each bone at its rest position, rotated by facing * restEuler * poseEuler. The
            // rest offset is rotated by `facing` too so off-axis bones orbit with the body turn (see
            // SnapLayout). The layout's rest rotation has to sit BEFORE the pose euler, or a
            // quadruped's barrel would stand upright the moment a remote peer emoted.
            var bones = _layout.Bones;
            for (int k = 0; k < bones.Length; k++)
            {
                var s = bones[k];
                Vector3 worldPos = basePos + facing * Off(s.Pos.x, s.Pos.y, s.Pos.z);
                Quaternion rot = facing * Quaternion.Euler(s.RestEuler) * Quaternion.Euler(over[(int)s.Bone]);
                var rb = _rb[(int)s.Bone];
                if (rb == null) continue;
                rb.position = worldPos; rb.rotation = rot;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.transform.position = worldPos; rb.transform.rotation = rot;
            }
        }
        readonly Vector3[] _emoteScratch = new Vector3[(int)Bone.Count];

        // Display a networked ANIMATION STATE on a kinematic puppet (client side). Like DisplayEmote
        // but the per-bone pose comes from a canned local animation for the state (run gait, jump
        // tuck, dive layout, prone, kick swing) rather than a streamed emote. `phase` is a free-
        // running 0..1 clock the client advances locally (for cyclic anims like Run); `moveAmount`
        // 0..1 scales the run so a body barely moving doesn't flail. Rest offsets match DisplaySnap.
        public void DisplayAnim(Vector3 basePos, Quaternion facing, AnimState state, float phase, float moveAmount)
        {
            FacingRotation = facing;
            var over = _emoteScratch;
            for (int i = 0; i < over.Length; i++) over[i] = Vector3.zero;
            float rootPitch = 0f, rootRoll = 0f;   // whole-body lean for dive/prone
            float rootLift = 0f;                   // whole-body vertical drop (sit); pos.y is not on the wire

            switch (state)
            {
                case AnimState.Run:
                {
                    // The SHARED gait, so a remote player's puppet runs exactly like the local body -
                    // including the quadruped front knee, which folded forward here too. `phase`
                    // arrives as a free-running 0..1 clock, so scale it into radians. moveAmount is
                    // the only speed signal on the wire, so it doubles as the sprint blend once it
                    // is near the top.
                    float amt = Mathf.Clamp01(moveAmount);
                    Gait.Pose(over, Gait.For(Plan), phase * Mathf.PI * 2f, amt,
                              Mathf.Clamp01((amt - 0.75f) * 4f), 0f, 0f);
                    break;
                }
                case AnimState.Jump:
                    // Tuck: knees up a little, arms slightly out.
                    over[(int)Bone.ThighL] = new Vector3(-30f, 0f, 0f);
                    over[(int)Bone.ThighR] = new Vector3(-30f, 0f, 0f);
                    over[(int)Bone.CalfL]  = new Vector3(40f, 0f, 0f);
                    over[(int)Bone.CalfR]  = new Vector3(40f, 0f, 0f);
                    over[(int)Bone.UpperArmL] = new Vector3(0f, 0f, -40f);   // -Z on a LEFT limb is OUT
                    over[(int)Bone.UpperArmR] = new Vector3(0f, 0f, 40f);
                    break;
                case AnimState.Kick:
                    // Right-leg swing through + slight torso lean (a struck shot).
                    over[(int)Bone.ThighR] = new Vector3(-70f, 0f, 0f);
                    over[(int)Bone.CalfR]  = new Vector3(20f, 0f, 0f);
                    over[(int)Bone.Torso]  = new Vector3(12f, 0f, 0f);
                    over[(int)Bone.UpperArmL] = new Vector3(0f, 0f, -45f);    // left arm OUT to counter-balance
                    over[(int)Bone.UpperArmR] = new Vector3(0f, 0f, 25f);
                    break;
                case AnimState.KickL:
                    // The mirror: left-leg swing, right arm out. Z flips sign across the body's
                    // midline (a LEFT limb's OUT is -Z, a RIGHT limb's is +Z - see Jump above).
                    over[(int)Bone.ThighL] = new Vector3(-70f, 0f, 0f);
                    over[(int)Bone.CalfL]  = new Vector3(20f, 0f, 0f);
                    over[(int)Bone.Torso]  = new Vector3(12f, 0f, 0f);
                    over[(int)Bone.UpperArmR] = new Vector3(0f, 0f, 45f);     // right arm OUT to counter-balance
                    over[(int)Bone.UpperArmL] = new Vector3(0f, 0f, -25f);
                    break;
                case AnimState.Dive:
                    // Laid-out flat (keeper dive): whole body rolled ~horizontal, arms reaching.
                    rootRoll = 80f;
                    over[(int)Bone.UpperArmL] = new Vector3(0f, 0f, -150f);   // matches KeeperPose.Dive
                    over[(int)Bone.UpperArmR] = new Vector3(0f, 0f, 150f);
                    break;
                case AnimState.Down:
                    // Knocked over: face-down flat.
                    rootPitch = 85f;
                    break;
                case AnimState.Sit:
                    // Seated: the same shape as RagdollPose.Sit, plus a root drop, because the
                    // snapshot carries no height (pos.y is zeroed on the host) so the puppet would
                    // otherwise sit at standing hip height with its legs in mid-air.
                    rootLift = -SimConfig.SitDrop * _hScale;
                    over[(int)Bone.Torso]  = new Vector3(-12f, 0f, 0f);
                    over[(int)Bone.ThighL] = new Vector3(-88f, 0f, -6f);      // -Z on a LEFT limb is OUT
                    over[(int)Bone.ThighR] = new Vector3(-88f, 0f, 6f);
                    over[(int)Bone.CalfL]  = new Vector3(12f, 0f, 0f);
                    over[(int)Bone.CalfR]  = new Vector3(12f, 0f, 0f);
                    over[(int)Bone.UpperArmL] = new Vector3(32f, 0f, -22f);   // -Z on a LEFT limb is OUT
                    over[(int)Bone.UpperArmR] = new Vector3(32f, 0f, 22f);
                    break;
                // Idle: rest stance (all zero).
            }

            Quaternion root = facing * Quaternion.Euler(rootPitch, 0f, rootRoll);
            var bones = _layout.Bones;
            for (int k = 0; k < bones.Length; k++)
            {
                var s = bones[k];
                Vector3 worldPos = basePos + Vector3.up * rootLift + (root * Off(s.Pos.x, s.Pos.y, s.Pos.z));
                Quaternion rot = root * Quaternion.Euler(s.RestEuler) * Quaternion.Euler(over[(int)s.Bone]);
                var rb = _rb[(int)s.Bone];
                if (rb == null) continue;
                rb.position = worldPos; rb.rotation = rot;
                rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.transform.position = worldPos; rb.transform.rotation = rot;
            }
        }

        // FK scratch, reused each frame (DisplayPose runs every frame for the two menu puppets, so we
        // avoid allocating four arrays per call). _fkRest / _fkRestRot are the layout's rest offset
        // and rest rotation per bone; the parent map comes from _layout.ParentByBone.
        readonly Vector3[] _fkRest = new Vector3[(int)Bone.Count];
        readonly Quaternion[] _fkRestRot = new Quaternion[(int)Bone.Count];
        readonly Vector3[] _fkPos = new Vector3[(int)Bone.Count];
        readonly Quaternion[] _fkRot = new Quaternion[(int)Bone.Count];

        // Aesthetic display poser: like DisplayAnim, but the whole-body lean (rootPitch/rootRoll) and
        // the per-bone euler overrides come straight from the caller instead of a fixed AnimState.
        // Lets a fully scripted cinematic (e.g. the main-menu goal reel) drive the kinematic puppet
        // frame by frame without adding a networked animation state.
        //
        // Genuine forward kinematics: each bone is placed relative to its PARENT, so a rotated thigh
        // carries its calf and foot and a rotated upper arm carries its forearm, instead of every
        // bone teleporting to a fixed rest offset and only spinning in place (which read as a rigid,
        // static pose). `boneEuler` is indexed by Bone (pass Vector3.zero, or a short/absent entry,
        // to leave a bone at rest). At rest (all eulers zero) the chain telescopes back to exactly
        // basePos + root*restOffset[b], so the pelvis anchor (PelvisAnchor) and camera framing are
        // unchanged. Requires a kinematic display puppet (call BecomeDisplayBody first).
        public void DisplayPose(Vector3 basePos, Quaternion facing, float rootPitch, float rootRoll, Vector3[] boneEuler)
        {
            FacingRotation = facing;
            Quaternion root = facing * Quaternion.Euler(rootPitch, 0f, rootRoll);

            // Rest offsets and rest rotations (feet-relative, build-scaled) indexed by Bone.
            for (int i = 0; i < _fkRest.Length; i++)
            {
                _fkRest[i] = Vector3.zero;
                _fkRestRot[i] = Quaternion.identity;
            }
            var bones = _layout.Bones;
            for (int k = 0; k < bones.Length; k++)
            {
                var s = bones[k];
                _fkRest[(int)s.Bone] = Off(s.Pos.x, s.Pos.y, s.Pos.z);
                _fkRestRot[(int)s.Bone] = Quaternion.Euler(s.RestEuler);
            }
            int[] parent = _layout.ParentByBone;

            // Forward pass. A child's pivot is its parent's pivot plus the bind offset (parent->child
            // rest vector) rotated into the parent's accumulated frame; the child's own euler only
            // spins it about that pivot, so it swings as a connected limb.
            //
            // The rest rotations make this mirror the PHYSICAL joint hierarchy exactly. A bone's
            // build-time world rotation is root * restRot[b], so its rest rotation relative to its
            // parent is Inverse(restRot[p]) * restRot[b], which is precisely what the joint drive
            // relaxes to (see _targetRestLocal / _jointStartLocal in AddJoint). The bind offset is
            // likewise carried in the parent's rest frame. Every biped rest rotation is identity, so
            // this is algebraically the same code it replaced for a human.
            for (int b = 0; b < (int)Bone.Count; b++)
            {
                Vector3 e = (boneEuler != null && b < boneEuler.Length) ? boneEuler[b] : Vector3.zero;
                int p = parent[b];
                if (p < 0)
                {
                    _fkRot[b] = root * _fkRestRot[b] * Quaternion.Euler(e);
                    _fkPos[b] = basePos + root * _fkRest[b];
                }
                else
                {
                    Quaternion pInv = Quaternion.Inverse(_fkRestRot[p]);
                    _fkRot[b] = _fkRot[p] * (pInv * _fkRestRot[b]) * Quaternion.Euler(e);
                    _fkPos[b] = _fkPos[p] + _fkRot[p] * (pInv * (_fkRest[b] - _fkRest[p]));
                }

                var rb = _rb[b];
                if (rb == null) continue;
                // World-space writes, parent already placed. No velocity writes: the bodies are
                // kinematic (Unity rejects setting velocity on them, and it would be meaningless).
                rb.position = _fkPos[b]; rb.rotation = _fkRot[b];
                rb.transform.position = _fkPos[b]; rb.transform.rotation = _fkRot[b];
            }
        }
    }
}
