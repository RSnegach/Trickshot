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

        // Set by the controller (Striker) each frame.
        public Vector3 MoveInput;            // desired world-space horizontal velocity
        public Quaternion FacingRotation = Quaternion.identity;
        public bool BalanceEnabled = true;
        public float DriveScale = 1f;        // 0..1 global motor strength multiplier
        public bool IsGrounded { get; private set; }

        public Rigidbody Pelvis => _rb[(int)Bone.Pelvis];
        public Rigidbody Rb(Bone b) => _rb[(int)b];
        public Transform Phys(Bone b) => _rb[(int)b] != null ? _rb[(int)b].transform : null;
        public IReadOnlyList<Collider> OwnColliders => _ownColliders;

        Transform _physRoot;
        Transform _targetRoot;

        // ---------------------------------------------------------------- build
        public void Build(Vector3 basePos, Quaternion facing, Material torsoMat, Material limbMat)
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
            var tHead   = MakeTarget(Bone.Head,   tTorso,      basePos + Off(0f, 1.70f, 0f), facing);
            var tThighL = MakeTarget(Bone.ThighL, tPelvis,     basePos + Off(-0.11f, 0.73f, 0f), facing);
            var tThighR = MakeTarget(Bone.ThighR, tPelvis,     basePos + Off( 0.11f, 0.73f, 0f), facing);
            var tCalfL  = MakeTarget(Bone.CalfL,  tThighL,     basePos + Off(-0.11f, 0.33f, 0f), facing);
            var tCalfR  = MakeTarget(Bone.CalfR,  tThighR,     basePos + Off( 0.11f, 0.33f, 0f), facing);
            MakeTarget(Bone.FootL, tCalfL, basePos + Off(-0.11f, 0.09f, 0.06f), facing);
            MakeTarget(Bone.FootR, tCalfR, basePos + Off( 0.11f, 0.09f, 0.06f), facing);

            // capture rest local rotations of target bones (identity by construction)
            for (int i = 0; i < (int)Bone.Count; i++)
                if (_target[i] != null) _targetRestLocal[i] = _target[i].localRotation;

            // --- physics skeleton ---
            // Pelvis (root rigidbody, box)
            MakePart(Bone.Pelvis, _physRoot, basePos + Off(0f, 1.02f, 0f), facing,
                     ColliderKind.Box, new Vector3(0.32f, 0.20f, 0.20f), 12f, torsoMat);

            MakePart(Bone.Torso, Phys(Bone.Pelvis), basePos + Off(0f, 1.34f, 0f), facing,
                     ColliderKind.Box, new Vector3(0.36f, 0.46f, 0.22f), 16f, torsoMat);
            MakePart(Bone.Head, Phys(Bone.Torso), basePos + Off(0f, 1.70f, 0f), facing,
                     ColliderKind.Sphere, new Vector3(0.14f, 0f, 0f), 4f, torsoMat);

            MakePart(Bone.ThighL, Phys(Bone.Pelvis), basePos + Off(-0.11f, 0.73f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.09f, 0.44f, 0f), 7f, limbMat);
            MakePart(Bone.ThighR, Phys(Bone.Pelvis), basePos + Off(0.11f, 0.73f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.09f, 0.44f, 0f), 7f, limbMat);

            MakePart(Bone.CalfL, Phys(Bone.ThighL), basePos + Off(-0.11f, 0.33f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.075f, 0.42f, 0f), 4f, limbMat);
            MakePart(Bone.CalfR, Phys(Bone.ThighR), basePos + Off(0.11f, 0.33f, 0f), facing,
                     ColliderKind.CapsuleY, new Vector3(0.075f, 0.42f, 0f), 4f, limbMat);

            MakePart(Bone.FootL, Phys(Bone.CalfL), basePos + Off(-0.11f, 0.06f, 0.06f), facing,
                     ColliderKind.Box, new Vector3(0.11f, 0.08f, 0.28f), 2f, limbMat);
            MakePart(Bone.FootR, Phys(Bone.CalfR), basePos + Off(0.11f, 0.06f, 0.06f), facing,
                     ColliderKind.Box, new Vector3(0.11f, 0.08f, 0.28f), 2f, limbMat);

            // Joints: child -> parent (connectedBody). Pelvis has no joint (free root).
            AddJoint(Bone.Torso,  Bone.Pelvis, Off(0f, -0.23f, 0f));
            AddJoint(Bone.Head,   Bone.Torso,  Off(0f, -0.14f, 0f));
            AddJoint(Bone.ThighL, Bone.Pelvis, Off(0f, 0.22f, 0f));
            AddJoint(Bone.ThighR, Bone.Pelvis, Off(0f, 0.22f, 0f));
            AddJoint(Bone.CalfL,  Bone.ThighL, Off(0f, 0.21f, 0f));
            AddJoint(Bone.CalfR,  Bone.ThighR, Off(0f, 0.21f, 0f));
            AddJoint(Bone.FootL,  Bone.CalfL,  Off(0f, 0.16f, -0.06f));
            AddJoint(Bone.FootR,  Bone.CalfR,  Off(0f, 0.16f, -0.06f));

            IgnoreSelfCollisions();
        }

        Vector3 Off(float x, float y, float z) => new Vector3(x, y, z);

        Transform MakeTarget(Bone b, Transform parent, Vector3 worldPos, Quaternion facing)
        {
            var go = Make.Empty("T_" + b, worldPos, parent);
            go.transform.rotation = facing; // all bones aligned to facing at rest
            _target[(int)b] = go.transform;
            return go.transform;
        }

        enum ColliderKind { Box, Sphere, CapsuleY }

        void MakePart(Bone b, Transform parent, Vector3 worldPos, Quaternion facing,
                      ColliderKind kind, Vector3 dims, float mass, Material mat)
        {
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
                    sc.radius = dims.x;
                    col = sc;
                    visual = Make.Sphere("v", dims.x * 2f, worldPos, mat, go.transform);
                    break;
                }
                case ColliderKind.CapsuleY:
                {
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.direction = 1; // Y
                    cc.radius = dims.x;
                    cc.height = dims.y;
                    col = cc;
                    visual = Make.Capsule("v", dims.x, dims.y, worldPos, mat, go.transform);
                    break;
                }
                default: // Box
                {
                    var bc = go.AddComponent<BoxCollider>();
                    bc.size = dims;
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

            // 1) Push the target skeleton toward the blended pose.
            for (int i = 0; i < (int)Bone.Count; i++)
            {
                if (_target[i] == null) continue;
                Vector3 e = Vector3.Lerp(_poseFrom[i], _poseTo[i], _poseT);
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

            // 3) Balance + locomotion on the free pelvis.
            if (BalanceEnabled)
            {
                JointMath.DriveTowardRotation(Pelvis, FacingRotation,
                    SimConfig.BalanceFrequency, SimConfig.BalanceDamping);
            }

            ApplyLocomotion();
        }

        void ApplyLocomotion()
        {
            // Only steer horizontal velocity while grounded; airborne stays ballistic.
            if (!IsGrounded) return;
            Vector3 v = Pelvis.linearVelocity;
            Vector3 horiz = new Vector3(v.x, 0f, v.z);
            Vector3 desired = new Vector3(MoveInput.x, 0f, MoveInput.z);
            Vector3 delta = desired - horiz;
            Vector3 force = delta * SimConfig.StrikerAccel;
            // cap so we don't launch
            force = Vector3.ClampMagnitude(force, SimConfig.StrikerAccel * SimConfig.StrikerMoveSpeed);
            Pelvis.AddForce(force, ForceMode.Acceleration);
        }

        void UpdateGrounded()
        {
            IsGrounded = false;
            Vector3 origin = Pelvis.position;
            float radius = 0.18f;
            float maxDist = 1.05f;
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
            _poseFrom = RagdollPose.Stand;
            _poseTo = RagdollPose.Stand;
            _poseT = 1f;

            // Reposition each bone to its build offset and zero velocities.
            SnapBone(Bone.Pelvis, basePos + Off(0f, 1.02f, 0f), facing);
            SnapBone(Bone.Torso,  basePos + Off(0f, 1.34f, 0f), facing);
            SnapBone(Bone.Head,   basePos + Off(0f, 1.70f, 0f), facing);
            SnapBone(Bone.ThighL, basePos + Off(-0.11f, 0.73f, 0f), facing);
            SnapBone(Bone.ThighR, basePos + Off(0.11f, 0.73f, 0f), facing);
            SnapBone(Bone.CalfL,  basePos + Off(-0.11f, 0.33f, 0f), facing);
            SnapBone(Bone.CalfR,  basePos + Off(0.11f, 0.33f, 0f), facing);
            SnapBone(Bone.FootL,  basePos + Off(-0.11f, 0.06f, 0.06f), facing);
            SnapBone(Bone.FootR,  basePos + Off(0.11f, 0.06f, 0.06f), facing);
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
