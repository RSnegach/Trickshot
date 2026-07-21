using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// A configurable defensive WALL for free-kick mode: a row of cheap capsule
    /// "blockers" standing shoulder-to-shoulder on the line between the dead ball and
    /// the goal centre, a set distance out, optionally shifted sideways so the player
    /// can position it.
    ///
    /// The blockers are the simple option (not full active ragdolls): each is a
    /// Make.Capsule with its capsule collider kept and a bouncy PhysicsMaterial, so a
    /// struck ball deflects off them, plus a kinematic Rigidbody so we can animate a
    /// small jump HOP to challenge higher shots without them being knocked over. All
    /// blockers hop together when the wall jumps.
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

            var mat = Make.Mat(new Color(0.8f, 0.25f, 0.25f));
            float half = (count - 1) * 0.5f;
            float centerY = BlockerHeight * 0.5f;   // so the capsule base sits on y = 0

            for (int i = 0; i < count; i++)
            {
                float along = (i - half) * ShoulderSpacing;
                Vector3 groundPos = wallCenter + lateral * along + Vector3.up * centerY;

                var go = Make.Capsule("WallBlocker" + i, BlockerRadius, BlockerHeight, groundPos, mat, root);

                // Keep the primitive's capsule collider and give it the bouncy material so
                // the ball deflects off it.
                var col = go.GetComponent<Collider>();
                if (col != null) col.material = _bounce;

                // Kinematic body: the collider moves with the animated hop and still
                // deflects the (dynamic) ball, but nothing can knock the blocker over.
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                _blockers.Add(go);
                _groundPos.Add(groundPos);
            }
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
