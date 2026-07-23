using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A configurable defensive WALL for free-kick mode: a row of humanoid "blockers"
    /// standing shoulder-to-shoulder on the line between the dead ball and the goal
    /// centre, a set distance out, optionally shifted sideways so the player can
    /// position it. They face the shooter with their hands crossed over their groins
    /// (the classic wall pose).
    ///
    /// Each blocker is a CONTAINER GameObject (the one returned by Blockers) that owns
    /// the single body-approximating CapsuleCollider + a bouncy PhysicsMaterial (so a
    /// struck ball deflects off it) + a kinematic Rigidbody (so the hop can't knock it
    /// over). The mannequin itself (head/torso/shorts/legs/crossed-arms) is a set of
    /// collider-less child meshes parented to that container, so there is still exactly
    /// ONE collider per blocker - the driver's contact test and the ball deflection both
    /// key off it, unchanged. All blockers hop together when the wall jumps.
    ///
    /// Built entirely from code (Build()); pumped by Tick() from the driver each frame.
    /// A plain class, not a MonoBehaviour: the FreeKickGame driver owns it and pumps it.
    /// </summary>
    public class DefensiveWall
    {
        readonly List<GameObject> _blockers = new List<GameObject>();
        readonly List<Vector3> _groundPos = new List<Vector3>();   // rest (grounded) centre positions
        PhysicsMaterial _bounce;

        // Shared hop: the whole wall jumps as one. < 0 = grounded / idle.
        float _hopTime = -1f;
        const float HopDuration = 0.6f;   // seconds up-and-down
        const float HopHeight   = 0.7f;   // metres at the apex

        // Blocker capsule dimensions (metres) and spacing along the wall line.
        const float BlockerHeight   = 1.85f;
        const float BlockerRadius   = 0.30f;
        const float ShoulderSpacing = 0.62f;   // centre-to-centre along the wall

        /// <summary>Every blocker GameObject, so the driver can reset / inspect them.</summary>
        public IReadOnlyList<GameObject> Blockers => _blockers;

        /// <summary>Whether any blockers currently exist (a wall was built this attempt).</summary>
        public bool HasBlockers => _blockers.Count > 0;

        /// <summary>
        /// Build (or rebuild) the wall. Places <paramref name="count"/> blockers
        /// shoulder-to-shoulder, <paramref name="distance"/> metres from the ball along
        /// the ball-to-goal line, shifted by <paramref name="lateralOffset"/> along the
        /// axis parallel to the goal line. Idempotent: clears any prior blockers first.
        /// </summary>
        public void Build(Transform root, Vector3 ballPos, int count, float distance, float lateralOffset)
        {
            // Ball-relative placement: 'distance' out along ball->goal, shifted 'lateralOffset'
            // along the goal-parallel axis. Resolve to an explicit centre + lateral, then build.
            Vector3 toGoal = SimConfig.GoalCenter - ballPos; toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 0.0001f) toGoal = Vector3.forward;
            Vector3 dir = toGoal.normalized;
            Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 center = ballPos + dir * distance + lat * lateralOffset;
            BuildAt(root, center, lat, count);
        }

        /// <summary>Build the wall centred at an explicit world point (host-placed). Blockers
        /// fan along the axis perpendicular to the ball->goal line so the wall still faces the
        /// shot. Keeps the same physics/hop as the ball-relative Build.</summary>
        public void Build(Transform root, Vector3 ballPos, Vector3 wallCenter, int count)
        {
            Vector3 toGoal = SimConfig.GoalCenter - ballPos; toGoal.y = 0f;
            if (toGoal.sqrMagnitude < 0.0001f) toGoal = Vector3.forward;
            Vector3 lat = Vector3.Cross(Vector3.up, toGoal.normalized).normalized;
            BuildAt(root, wallCenter, lat, count);
        }

        // Shared core: fan `count` blockers along `lateral` around `wallCenter`.
        void BuildAt(Transform root, Vector3 wallCenter, Vector3 lateral, int count)
        {
            Clear();
            count = Mathf.Max(0, count);
            if (count == 0) return;

            if (_bounce == null)
                _bounce = Make.PhysMat("WallBlocker", 0.45f, 0.4f, 0.4f);

            wallCenter.y = 0f;

            // Facing: recover the ball->goal direction from the lateral axis (lateral = up x dir),
            // so dir = lateral x up. The wall faces the SHOOTER (opposite the goal), which is where
            // the crossed hands and chest point.
            Vector3 dir = Vector3.Cross(lateral, Vector3.up).normalized;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(-dir, Vector3.up);

            var shirt = Make.Mat(new Color(0.8f, 0.25f, 0.25f));
            var shorts = Make.Mat(new Color(0.14f, 0.15f, 0.2f));
            var skin = Make.Mat(new Color(0.82f, 0.64f, 0.5f));
            float half = (count - 1) * 0.5f;
            float centerY = BlockerHeight * 0.5f;   // container sits at body-centre; capsule base on y = 0

            for (int i = 0; i < count; i++)
            {
                float along = (i - half) * ShoulderSpacing;
                Vector3 groundPos = wallCenter + lateral * along + Vector3.up * centerY;

                // Container = the returned blocker. It owns the ONE body-approximating collider
                // (a full-height capsule, matching the old bare-capsule blocker exactly) + the
                // bouncy material + a kinematic body. The mannequin meshes hang off it collider-less.
                var go = Make.Empty("WallBlocker" + i, groundPos, root);
                go.transform.rotation = facing;

                var cap = go.AddComponent<CapsuleCollider>();
                cap.direction = 1;               // local Y
                cap.radius = BlockerRadius;
                cap.height = BlockerHeight;
                cap.center = Vector3.zero;        // container is at the body centre
                cap.material = _bounce;

                // Kinematic body: the collider moves with the animated hop and still deflects the
                // (dynamic) ball, but nothing can knock the blocker over.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                BuildMannequin(go.transform, centerY, shirt, shorts, skin);

                _blockers.Add(go);
                _groundPos.Add(groundPos);
            }
        }

        // Hang a collider-less humanoid on the container, posed with the hands crossed low over the
        // groin and facing the shooter (+localZ is the ball-facing front). Local Y is measured from
        // the body centre (the container origin), so the feet sit at local y = -centerY (world 0).
        void BuildMannequin(Transform body, float centerY, Material shirt, Material shorts, Material skin)
        {
            float feet = -centerY;   // local y of the ground

            // Head, torso, shorts.
            NoCollide(Make.Sphere("Head", 0.30f, body.position, skin, body), new Vector3(0f, feet + 1.78f, 0f), Quaternion.identity);
            NoCollide(Make.Capsule("Torso", 0.19f, 0.72f, body.position, shirt, body), new Vector3(0f, feet + 1.36f, 0f), Quaternion.identity);
            Make.Box("Shorts", new Vector3(0.44f, 0.32f, 0.32f), body.position, shorts, body, collider: false)
                .transform.localPosition = new Vector3(0f, feet + 0.94f, 0f);

            // Legs + simple feet.
            NoCollide(Make.Capsule("LegL", 0.12f, 0.92f, body.position, skin, body), new Vector3(-0.11f, feet + 0.46f, 0f), Quaternion.identity);
            NoCollide(Make.Capsule("LegR", 0.12f, 0.92f, body.position, skin, body), new Vector3(0.11f, feet + 0.46f, 0f), Quaternion.identity);
            Make.Box("FootL", new Vector3(0.16f, 0.08f, 0.30f), body.position, shorts, body, collider: false)
                .transform.localPosition = new Vector3(-0.11f, feet + 0.04f, 0.06f);
            Make.Box("FootR", new Vector3(0.16f, 0.08f, 0.30f), body.position, shorts, body, collider: false)
                .transform.localPosition = new Vector3(0.11f, feet + 0.04f, 0.06f);

            // Arms crossed low in FRONT (over the groin). Each arm runs from its shoulder down and
            // inward to a hand that crosses past the body centre, so the two forearms overlap in
            // front of the crotch - the standard wall pose. +Z is the ball-facing front.
            Vector3 shoulderL = new Vector3(-0.30f, feet + 1.60f, 0.02f);
            Vector3 handL     = new Vector3(0.05f,  feet + 0.90f, 0.20f);
            Vector3 shoulderR = new Vector3(0.30f,  feet + 1.60f, 0.02f);
            Vector3 handR     = new Vector3(-0.05f, feet + 0.90f, 0.20f);
            BuildArm(body, "ArmL", shoulderL, handL, skin);
            BuildArm(body, "ArmR", shoulderR, handR, skin);
        }

        // One straight arm capsule spanning shoulder->hand in the body's local space.
        void BuildArm(Transform body, string name, Vector3 shoulder, Vector3 hand, Material skin)
        {
            Vector3 seg = hand - shoulder;
            float len = seg.magnitude;
            if (len < 0.01f) return;
            var arm = Make.Capsule(name, 0.075f, len, body.position, skin, body);
            NoCollide(arm, (shoulder + hand) * 0.5f, Quaternion.FromToRotation(Vector3.up, seg / len));
        }

        // Place a primitive at a local pose under the body and strip its collider (visual only).
        static void NoCollide(GameObject go, Vector3 localPos, Quaternion localRot)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
        }

        /// <summary>Start a single wall hop (all blockers jump together, then land).</summary>
        public void TriggerJump()
        {
            if (_blockers.Count == 0) return;
            _hopTime = 0f;
        }

        /// <summary>Animate the hop. Call every frame from the driver's Update.</summary>
        public void Tick()
        {
            if (_hopTime < 0f) return;   // grounded, nothing to animate
            _hopTime += Time.deltaTime;
            float t = _hopTime / HopDuration;
            if (t >= 1f)
            {
                SetLift(0f);
                _hopTime = -1f;
                return;
            }
            SetLift(Mathf.Sin(t * Mathf.PI) * HopHeight);   // 0 -> apex -> 0
        }

        /// <summary>Snap the whole wall back to the ground and cancel any hop.</summary>
        public void Ground()
        {
            _hopTime = -1f;
            SetLift(0f);
        }

        void SetLift(float lift)
        {
            for (int i = 0; i < _blockers.Count; i++)
            {
                var go = _blockers[i];
                if (go == null) continue;
                Vector3 p = go.transform.position;
                p.y = _groundPos[i].y + lift;
                go.transform.position = p;
            }
        }

        /// <summary>Destroy all blockers and forget them.</summary>
        public void Clear()
        {
            for (int i = 0; i < _blockers.Count; i++)
                if (_blockers[i] != null) Object.Destroy(_blockers[i]);
            _blockers.Clear();
            _groundPos.Clear();
            _hopTime = -1f;
        }
    }
}
