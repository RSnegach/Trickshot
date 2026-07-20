using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Makes a player ragdoll fall over when tackled or hit by a slide tackle, go limp for
    /// a moment, then get back up. Sits on every scrimmage footballer (and the human's
    /// controlled body). While Down, the owning controller (Striker AI/human, or Footballer
    /// AI) suspends its own steering so it doesn't fight the fall.
    /// </summary>
    public class Knockdown : MonoBehaviour
    {
        ActiveRagdoll _ragdoll;
        float _timer;
        bool _down;

        public bool Down => _down;

        public void Init(ActiveRagdoll ragdoll) => _ragdoll = ragdoll;

        // Fell over. dir is the horizontal push direction (whoever knocked them over).
        public void Fell(Vector3 dir)
        {
            if (_ragdoll == null || _ragdoll.Pelvis == null) return;
            _down = true;
            _timer = SimConfig.KnockdownTime;

            _ragdoll.UprightLock = false;
            _ragdoll.BalanceEnabled = false;
            _ragdoll.LocomotionEnabled = false;
            _ragdoll.BodyOrientTarget = null;
            _ragdoll.DriveScale = 0.1f;   // go limp so the body actually tumbles
            _ragdoll.ClearPoseOverrides();

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = _ragdoll.FacingRotation * Vector3.forward;
            dir.Normalize();
            _ragdoll.AddVelocityToAll(dir * SimConfig.KnockdownImpulse + Vector3.up * 1.2f);
            // Tumble about the axis perpendicular to the shove (topple forward over it).
            Vector3 axis = Vector3.Cross(Vector3.up, dir);
            _ragdoll.AddTorqueToPelvis(axis * SimConfig.KnockdownSpin);
        }

        void Update()
        {
            if (!_down) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) Recover();
        }

        void Recover()
        {
            _down = false;
            _ragdoll.DriveScale = 1f;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;   // pop back to his feet
            _ragdoll.SnapFacing(_ragdoll.FacingRotation);
        }

        // Force back up (match reset / kickoff).
        public void Cancel()
        {
            if (_down) Recover();
        }
    }
}
