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
        float _beaten;   // seconds left flat-footed after a MISSED tackle (see Stumble)

        public bool Down => _down;

        /// <summary>Off balance from a missed challenge. NOT Down: he is still on his feet.</summary>
        public bool Beaten => _beaten > 0f;

        // The cost of a missed tackle, which the contest in Dribble.ContestTackle applies. Measured
        // against TackleCooldown 0.9 s: 0.55 s eats about 60% of the tackler's next attempt window,
        // so spamming the button no longer converts a 34% roll into a certainty, and he still
        // recovers inside a second. A committed slide that misses costs double - and note the man he
        // fouled is down for KnockdownTime 1.4 s, so the fouler is up FIRST. That is deliberately
        // lenient: scrimmage has no free kick to award, so the punishment cannot be the whole cost.
        public const float BeatenTime       = 0.55f;
        public const float BeatenSlideTime  = 1.10f;
        // Partial limp, not a collapse. Enough that the lunge carries him past the man.
        public const float BeatenDriveScale = 0.45f;

        Striker _strk;
        // Lazy, because Knockdown also sits on bodies with no Striker. Used only to tear down a
        // gesture the fall has to override (the seated hip drop, a trick in flight).
        Striker Strk => _strk != null ? _strk : (_strk = GetComponent<Striker>());

        public void Init(ActiveRagdoll ragdoll) => _ragdoll = ragdoll;

        // Fell over. dir is the horizontal push direction (whoever knocked them over).
        public void Fell(Vector3 dir)
        {
            if (_ragdoll == null || _ragdoll.Pelvis == null) return;

            // RE-ENTRY GUARD. There was none, and several paths fell the same body in quick
            // succession: ScrimmageGame.WinBall fells NearestOpponentToBall on every ball-win, and
            // ResolveDiveHits can land on someone already going down. Each re-entry restacked
            // KnockdownImpulse 5.5 m/s onto a body already limp at DriveScale 0.1 - 11 m/s on a rag,
            // which at fdt 0.0200 is 0.22 m of travel per step and enough to push bones through thin
            // colliders (nets, walls). Refresh how long he is down; never restack the shove.
            if (_down) { _timer = SimConfig.KnockdownTime; return; }

            // A real fall outranks a stumble, and clearing it here is what stops the Stumble timer
            // in Update from restoring DriveScale in the middle of the tumble.
            _beaten = 0f;

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

        /// <summary>
        /// BEATEN: a missed tackle leaves you flat-footed, not on the floor. Drive is cut and the
        /// locomotion steering is suspended so the lunge carries you past the man you missed, but
        /// UprightLock and balance stay ON - he stumbles through it on his feet and can still turn.
        ///
        /// Deliberately NOT _down. Every controller suspends its whole brain on Down, which is 1.4 s
        /// of a statue, and that is far too much for a missed standing challenge - it would swap one
        /// unreadable outcome for another. The player needs to see "I lunged, I missed, I am off
        /// balance for half a second", and Beaten is exactly that much.
        /// </summary>
        public void Stumble(float seconds)
        {
            if (_ragdoll == null || _down) return;          // a real fall outranks a stumble
            // A slide or a dive already owns this body and runs its OWN limp phase with its own
            // DriveScale (SlideLimpDriveScale 0.15). Layering a stumble on top would have the
            // shorter timer restore drive to 1 mid-slide and snap him upright.
            var st = Strk;
            if (st != null && st.IsBusy) return;
            _beaten = Mathf.Max(_beaten, seconds);          // longest cost wins, never shortens
            _ragdoll.DriveScale = BeatenDriveScale;
            _ragdoll.LocomotionEnabled = false;
        }

        void Update()
        {
            // Stumble recovery restores ONLY the two things Stumble touched. Fell/Recover own
            // upright, balance and facing; stomping those here is what would fight a tumble.
            if (_beaten > 0f && !_down && (_beaten -= Time.deltaTime) <= 0f)
            {
                _beaten = 0f;
                if (_ragdoll != null)
                {
                    _ragdoll.DriveScale = 1f;
                    _ragdoll.LocomotionEnabled = true;
                }
            }

            if (!_down) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) Recover();
        }

        void Recover()
        {
            _down = false;
            _beaten = 0f;   // the fall's restore is the authoritative one; don't leave a second timer armed
            _ragdoll.DriveScale = 1f;
            _ragdoll.BalanceEnabled = true;
            _ragdoll.LocomotionEnabled = true;
            _ragdoll.UprightLock = true;   // pop back to his feet
            _ragdoll.SnapFacing(_ragdoll.FacingRotation);
        }

        // Force back up (match reset / kickoff).
        public void Cancel()
        {
            if (_down) { Recover(); return; }
            // A stumble must clear on a reset too, or a body caught mid-stumble by the whistle
            // starts the restart with locomotion off and reads as frozen.
            if (_beaten > 0f)
            {
                _beaten = 0f;
                if (_ragdoll != null) { _ragdoll.DriveScale = 1f; _ragdoll.LocomotionEnabled = true; }
            }
        }
    }
}
