using System.Collections.Generic;
using UnityEngine;

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
        readonly Quaternion[] _targetRestLocal = new Quaternion[(int)Bone.Count];
        readonly List<Collider> _ownColliders = new List<Collider>();

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
        public IReadOnlyList<Collider> OwnColliders => _ownColliders;

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
        public float HeightScale => _hScale;

        // ---------------------------------------------------------------- build
        // Build the player with a custom height/girth/mass from a PlayerProfile-style set
        // of scales, then delegate to the normal Build. Only the player striker uses this;
        // everyone else builds at 1.0 via Build().
        public void BuildScaled(Vector3 basePos, Quaternion facing, Material torsoMat, Material limbMat,
                                float heightScale, float girthScale, float massMul, bool withGloves = true)
        {
            _hScale = Mathf.Max(0.5f, heightScale);
            _gScale = Mathf.Max(0.5f, girthScale);
            _massMul = Mathf.Max(0.3f, massMul);
            Build(basePos, facing, torsoMat, limbMat, withGloves);
        }

        // withGloves: the keeper wears big white gloves (with hitboxes); the striker does not.
        public void Build(Vector3 basePos, Quaternion facing, Material torsoMat, Material limbMat,
                          bool withGloves = true)
        {
            FacingRotation = facing;

            _targetRoot = Make.Empty("TargetSkeleton", basePos, transform).transform;
            _targetRoot.rotation = facing;
            _physRoot = Make.Empty("PhysicsSkeleton", basePos, transform).transform;
            _physRoot.rotation = facing;

            // Bone dimensions (metres), authored upright along +Y, facing +Z.
            // y is the centre height of each part above the feet base.
            // Build the target skeleton (nested) and physics skeleton (nested) together.

            // --- target skeleton: nested empties at joint pivots ---
            // We place target transforms at the same world positions as the physics
            // parts' centres so their local rotations map 1:1 to the joints.
            var tPelvis = MakeTarget(Bone.Pelvis, _targetRoot, basePos + Off(0f, 1.02f, 0f), facing);
            var tTorso  = MakeTarget(Bone.Torso,  tPelvis,     basePos + Off(0f, 1.34f, 0f), facing);
            var tHead   = MakeTarget(Bone.Head,   tTorso,      basePos + Off(0f, 1.72f, 0f), facing);
            var tThighL = MakeTarget(Bone.ThighL, tPelvis,     basePos + Off(-0.11f, 0.73f, 0f), facing);
            var tThighR = MakeTarget(Bone.ThighR, tPelvis,     basePos + Off( 0.11f, 0.73f, 0f), facing);
            var tCalfL  = MakeTarget(Bone.CalfL,  tThighL,     basePos + Off(-0.11f, 0.33f, 0f), facing);
            var tCalfR  = MakeTarget(Bone.CalfR,  tThighR,     basePos + Off( 0.11f, 0.33f, 0f), facing);
            MakeTarget(Bone.FootL, tCalfL, basePos + Off(-0.11f, 0.09f, 0.06f), facing);
            MakeTarget(Bone.FootR, tCalfR, basePos + Off( 0.11f, 0.09f, 0.06f), facing);
            // Arms: shoulders on the torso, hanging down at rest.
            var tUpArmL = MakeTarget(Bone.UpperArmL, tTorso, basePos + Off(-0.26f, 1.40f, 0f), facing);
            var tUpArmR = MakeTarget(Bone.UpperArmR, tTorso, basePos + Off( 0.26f, 1.40f, 0f), facing);
            MakeTarget(Bone.ForearmL, tUpArmL, basePos + Off(-0.26f, 1.08f, 0f), facing);
            MakeTarget(Bone.ForearmR, tUpArmR, basePos + Off( 0.26f, 1.08f, 0f), facing);

            // capture rest local rotations of target bones (identity by construction)
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_target[i] != null) _targetRestLocal[i] = _target[i].localRotation;

            // --- physics skeleton ---
            // Only the TORSO wears the jersey (torsoMat, which may carry the painted
            // texture). The pelvis (shorts) and head use limbMat so the jersey art does
            // NOT bleed onto the shorts or the head.
            MakePart(Bone.Pelvis, _physRoot, basePos + Off(0f, 1.02f, 0f), facing,
                     ColliderKind.Box, new Vector3(0.32f, 0.20f, 0.20f), 12f, limbMat);

            MakePart(Bone.Torso, Phys(Bone.Pelvis), basePos + Off(0f, 1.34f, 0f), facing,
                     ColliderKind.Box, new Vector3(0.36f, 0.46f, 0.22f), 16f, torsoMat);
            // Head: 0.19 visible radius, 0.27 collider (dims.y override), and the hitbox
            // shifted forward (+Z) and a bit down (-Y) so headers reach in front of the face.
            MakePart(Bone.Head, Phys(Bone.Torso), basePos + Off(0f, 1.72f, 0f), facing,
                     ColliderKind.Sphere, new Vector3(0.19f, 0.27f, 0f), 4.5f, limbMat,
                     1f, new Vector3(0f, -0.05f, 0.12f));

            // Leg hitboxes are fattened (LegHitboxScale) beyond the visible leg so the ball
            // connects off the legs reliably instead of clipping through.
            float legHb = SimConfig.LegHitboxScale;
            MakePart(Bone.ThighL, Phys(Bone.Pelvis), basePos + Off(-0.11f, 0.73f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.09f, 0.44f, 0f), 7f, limbMat, legHb);
            MakePart(Bone.ThighR, Phys(Bone.Pelvis), basePos + Off(0.11f, 0.73f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.09f, 0.44f, 0f), 7f, limbMat, legHb);

            MakePart(Bone.CalfL, Phys(Bone.ThighL), basePos + Off(-0.11f, 0.33f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.075f, 0.42f, 0f), 4f, limbMat, legHb);
            MakePart(Bone.CalfR, Phys(Bone.ThighR), basePos + Off(0.11f, 0.33f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.075f, 0.42f, 0f), 4f, limbMat, legHb);

            // Small, low-profile feet visually, but a ~1.6x larger collider (last arg)
            // so the ball connects off the foot more easily.
            // Foot rest offset (0.06, 0.06) matches ResetTo so a round reset doesn't pop.
            MakePart(Bone.FootL, Phys(Bone.CalfL), basePos + Off(-0.11f, 0.06f, 0.06f), facing,
                     ColliderKind.Box, new Vector3(0.09f, 0.05f, 0.17f), 1.5f, limbMat, 1.6f);
            MakePart(Bone.FootR, Phys(Bone.CalfR), basePos + Off(0.11f, 0.06f, 0.06f), facing,
                     ColliderKind.Box, new Vector3(0.09f, 0.05f, 0.17f), 1.5f, limbMat, 1.6f);
            // Frictionless feet: grounding is a pelvis SphereCast (not foot contact), so
            // slick feet slide over the turf instead of catching and making the run janky.
            // Minimum friction-combine forces the contact to ~0 regardless of turf value.
            var slick = Make.PhysMat("Feet", 0f, 0f, 0f,
                                     PhysicsMaterialCombine.Minimum, PhysicsMaterialCombine.Minimum);
            _rb[(int)Bone.FootL].GetComponent<Collider>().material = slick;
            _rb[(int)Bone.FootR].GetComponent<Collider>().material = slick;

            // Arms (upper arm + forearm): thin, skinny capsules that weigh almost nothing
            // so they barely affect the body's momentum / spin.
            // X = 0.26 to match the target skeleton + ResetTo (so a round reset doesn't
            // pre-stress the shoulder/elbow joints, which girth scaling would amplify).
            // Arm HITBOXES are fattened (colliderScale ArmHitboxScale) beyond the thin
            // visible arm so the ball stops phasing through the keeper's arms.
            float armHb = SimConfig.ArmHitboxScale;
            MakePart(Bone.UpperArmL, Phys(Bone.Torso), basePos + Off(-0.26f, 1.40f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.05f, 0.30f, 0f), 0.3f, limbMat, armHb);
            MakePart(Bone.UpperArmR, Phys(Bone.Torso), basePos + Off(0.26f, 1.40f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.05f, 0.30f, 0f), 0.3f, limbMat, armHb);
            MakePart(Bone.ForearmL, Phys(Bone.UpperArmL), basePos + Off(-0.26f, 1.08f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.045f, 0.30f, 0f), 0.25f, limbMat, armHb);
            MakePart(Bone.ForearmR, Phys(Bone.UpperArmR), basePos + Off(0.26f, 1.08f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.045f, 0.30f, 0f), 0.25f, limbMat, armHb);

            // Joints: child -> parent (connectedBody). Pelvis has no joint (free root).
            AddJoint(Bone.Torso,  Bone.Pelvis, Off(0f, -0.23f, 0f));
            AddJoint(Bone.Head,   Bone.Torso,  Off(0f, -0.14f, 0f));
            AddJoint(Bone.ThighL, Bone.Pelvis, Off(0f, 0.22f, 0f));
            AddJoint(Bone.ThighR, Bone.Pelvis, Off(0f, 0.22f, 0f));
            AddJoint(Bone.CalfL,  Bone.ThighL, Off(0f, 0.21f, 0f));
            AddJoint(Bone.CalfR,  Bone.ThighR, Off(0f, 0.21f, 0f));
            AddJoint(Bone.FootL,  Bone.CalfL,  Off(0f, 0.16f, -0.06f));
            AddJoint(Bone.FootR,  Bone.CalfR,  Off(0f, 0.16f, -0.06f));
            AddJoint(Bone.UpperArmL, Bone.Torso,     Off(0f, 0.17f, 0f));
            AddJoint(Bone.UpperArmR, Bone.Torso,     Off(0f, 0.17f, 0f));
            AddJoint(Bone.ForearmL,  Bone.UpperArmL, Off(0f, 0.16f, 0f));
            AddJoint(Bone.ForearmR,  Bone.UpperArmR, Off(0f, 0.16f, 0f));

            IgnoreSelfCollisions();

            // Big white gloves at the hand end of each forearm (keeper only).
            if (withGloves)
            {
                AddGlove(Bone.ForearmL);
                AddGlove(Bone.ForearmR);
            }
        }

        void AddGlove(Bone forearm)
        {
            var rb = _rb[(int)forearm];
            if (rb == null) return;
            // Big white glove at the hand end of the forearm, WITH a hitbox: its sphere
            // collider is added to the forearm rigidbody (as a child object sharing the
            // body) and registered as an own-collider so it doesn't self-collide.
            var glove = Make.Sphere("Glove", 0.32f, rb.transform.position, Make.Mat(Color.white, 0.2f), rb.transform);
            glove.transform.localPosition = new Vector3(0f, -0.19f, 0f);
            glove.transform.localScale = Vector3.one * 0.32f;
            var sc = glove.GetComponent<SphereCollider>();  // keep + use as the hitbox
            if (sc != null)
            {
                sc.radius = 0.5f;   // local; *0.32 scale -> ~0.16 world radius, a chunky glove
                _ownColliders.Add(sc);
                // Ignore collisions with every existing own-collider (self-collision).
                for (int i = 0; i < _ownColliders.Count - 1; i++)
                    if (_ownColliders[i] != null) Physics.IgnoreCollision(sc, _ownColliders[i], true);
            }
        }

        // Layout offset. Heights (y) scale with the build height; lateral spacing (x/z)
        // scales with girth so a wider body's limbs sit further out. Default build = 1.
        Vector3 Off(float x, float y, float z) => new Vector3(x * _gScale, y * _hScale, z * _gScale);

        Transform MakeTarget(Bone b, Transform parent, Vector3 worldPos, Quaternion facing)
        {
            var go = Make.Empty("T_" + b, worldPos, parent);
            go.transform.rotation = facing; // all bones aligned to facing at rest
            _target[(int)b] = go.transform;
            return go.transform;
        }

        enum ColliderKind { Box, Sphere, CapsuleY }

        void MakePart(Bone b, Transform parent, Vector3 worldPos, Quaternion facing,
                      ColliderKind kind, Vector3 dims, float mass, Material mat,
                      float colliderScale = 1f, Vector3 colliderOffset = default)
        {
            // Apply the build scale to this part: girth widens X/Z (and capsule radius),
            // height lengthens the vertical extent, and mass scales with weight. For a
            // CapsuleY, dims.x is radius (girth) and dims.y is length (height). For a
            // Sphere, dims.x is visible radius and dims.y an optional collider-radius
            // override (both girth). For a Box, dims = full size (x/z girth, y height).
            if (kind == ColliderKind.CapsuleY)
                dims = new Vector3(dims.x * _gScale, dims.y * _hScale, dims.z);
            else if (kind == ColliderKind.Sphere)
                dims = new Vector3(dims.x * _gScale, dims.y * _gScale, dims.z);
            else // Box
                dims = new Vector3(dims.x * _gScale, dims.y * _hScale, dims.z * _gScale);
            colliderOffset = new Vector3(colliderOffset.x * _gScale, colliderOffset.y * _hScale, colliderOffset.z * _gScale);
            mass *= _massMul;

            var go = new GameObject("P_" + b);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.rotation = facing;
            go.transform.localScale = Vector3.one; // never scale a physics bone root

            Collider col;
            GameObject visual;
            switch (kind)
            {
                case ColliderKind.Sphere:
                {
                    var sc = go.AddComponent<SphereCollider>();
                    // dims.y (if > 0) is a collider-radius override so the hitbox can be
                    // bigger than the visible sphere (e.g. a generous header hitbox).
                    sc.radius = dims.y > 0f ? dims.y : dims.x;
                    sc.center = colliderOffset;   // shift the hitbox (e.g. forward + down)
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
                    visual = Make.Box("v", dims, worldPos, mat, go.transform, collider: false);
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

            ApplyDrive(j, 1f);

            _joint[(int)child] = j;
            // All bones are built aligned to 'facing', so each child's rotation
            // relative to its parent is identity at build time.
            _jointStartLocal[(int)child] = Quaternion.identity;
        }

        void ApplyDrive(ConfigurableJoint j, float scale)
        {
            var drive = new JointDrive
            {
                positionSpring = SimConfig.JointSpring * scale,
                positionDamper = SimConfig.JointDamper,
                maximumForce = SimConfig.JointMaxForce
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

            // 2) Drive each joint's target rotation to match its target bone.
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                var j = _joint[i];
                if (j == null) continue;
                ApplyDrive(j, DriveScale);
                Quaternion targetLocal = _target[i].localRotation; // relative to parent target
                j.SetTargetRotationLocal(targetLocal, _jointStartLocal[i]);
            }

            UpdateGrounded();

            // 3) Upright lock vs. free balance.
            if (UprightLock)
            {
                ApplyUprightLock();
            }
            else if (DiveYawLock)
            {
                ApplyDiveYawLock();
            }
            else
            {
                ReleaseUprightLock();
                if (BalanceEnabled)
                    JointMath.DriveTowardRotation(Pelvis, FacingRotation,
                        SimConfig.BalanceFrequency, SimConfig.BalanceDamping);
                else if (BodyOrientTarget.HasValue)
                    DrivePelvisOrientation(BodyOrientTarget.Value);
            }

            ApplyLocomotion();
        }

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
            // Cast scales with the build: a taller player's pelvis sits higher, so the
            // ground is further below it; a wider player needs a slightly wider probe.
            float radius = 0.18f * _gScale;
            float maxDist = 1.05f * _hScale;
            var hits = Physics.SphereCastAll(origin, radius, Vector3.down, maxDist,
                                             ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider == null) continue;
                if (IsOwn(h.collider)) continue;
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue; // ignore other dynamic bodies (ball)
                IsGrounded = true;
                break;
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

        /// <summary>Scale the horizontal (x/z) velocity of every bone, leaving vertical
        /// intact. Used to bleed off carried run momentum at jump time.</summary>
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

            // Reposition each bone to its build offset and zero velocities.
            SnapBone(Bone.Pelvis, basePos + Off(0f, 1.02f, 0f), facing);
            SnapBone(Bone.Torso,  basePos + Off(0f, 1.34f, 0f), facing);
            SnapBone(Bone.Head,   basePos + Off(0f, 1.72f, 0f), facing);
            SnapBone(Bone.ThighL, basePos + Off(-0.11f, 0.73f, 0f), facing);
            SnapBone(Bone.ThighR, basePos + Off(0.11f, 0.73f, 0f), facing);
            SnapBone(Bone.CalfL,  basePos + Off(-0.11f, 0.33f, 0f), facing);
            SnapBone(Bone.CalfR,  basePos + Off(0.11f, 0.33f, 0f), facing);
            SnapBone(Bone.FootL,  basePos + Off(-0.11f, 0.06f, 0.06f), facing);
            SnapBone(Bone.FootR,  basePos + Off(0.11f, 0.06f, 0.06f), facing);
            SnapBone(Bone.UpperArmL, basePos + Off(-0.26f, 1.40f, 0f), facing);
            SnapBone(Bone.UpperArmR, basePos + Off(0.26f, 1.40f, 0f), facing);
            SnapBone(Bone.ForearmL,  basePos + Off(-0.26f, 1.08f, 0f), facing);
            SnapBone(Bone.ForearmR,  basePos + Off(0.26f, 1.08f, 0f), facing);
        }

        void SnapBone(Bone b, Vector3 worldPos, Quaternion facing)
        {
            var rb = _rb[(int)b];
            if (rb == null) return;
            rb.position = worldPos;
            rb.rotation = facing;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.transform.position = worldPos;
            rb.transform.rotation = facing;
        }
    }
}
