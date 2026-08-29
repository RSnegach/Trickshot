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

        Striker _strk;
        // Lazy, because Knockdown also sits on bodies with no Striker. Used only to tear down a
        // gesture the fall has to override (the seated hip drop, a trick in flight).
        Striker Strk => _strk != null ? _strk : (_strk = GetComponent<Striker>());

        public void Init(ActiveRagdoll ragdoll) => _ragdoll = ragdoll;

        // Fell over. dir is the horizontal push direction (whoever knocked them over).
        public void Fell(Vector3 dir)
        {
            if (_ragdoll == null || _ragdoll.Pelvis == null) return;

            // Tear down any gesture that owns the body FIRST, before the teardown below, or it
            // undoes it. Two things bite here:
            //   - EmoteHeightOffset. A seated (or mid-emote) body has it non-zero, which hands the
            //     whole-body carry servo off and PD-drives the pelvis to a fixed height every frame.
            //     Knockdown never cleared it, so a felled sitter was pinned at seat height while limp,
            //     fighting the tumble impulse below.
            //   - the Striker's own trick/sit latch. Tick is suspended while down, so the latch would
            //     survive the fall and re-assert the moment he gets up.
            var st = Strk;
            if (st != null && (st.IsSitting || st.IsBusy)) st.ForceRecover();
            _ragdoll.EmoteHeightOffset = 0f;

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
