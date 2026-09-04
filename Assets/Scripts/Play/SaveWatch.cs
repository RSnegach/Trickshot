using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Shared keeper-save detector. Every mode that calls SAVE / EPIC SAVE / MISS runs one of
    /// these, so the verdict is identical everywhere.
    ///
    /// It latches on REAL contacts from BallController's touch log (PhysX collisions, continuous
    /// detection, impact speed recorded at the contact). The per-mode checks it replaces polled
    /// bone-to-ball distance once a frame and fell back to the ball's resting position, so a save
    /// read MISS whenever the poll blinked past the contact or the keeper parried the ball clear.
    /// </summary>
    public class SaveWatch
    {
        const float RestSpeed = 0.8f;    // ball this slow counts as settling
        const float RestHold  = 0.4f;    // ...for this long = the shot is over
        const float TouchTimeout = 2.5f; // parried ball still rattling around: call it anyway

        float _armTime = -1f;
        float _rest;

        public bool Touched { get; private set; }   // the keeper got something to it this attempt
        public float TouchSpeed { get; private set; }
        public float TouchTime { get; private set; }

        // Start a fresh attempt. Contacts before this never count.
        public void Arm()
        {
            _armTime = Time.time; _rest = 0f;
            Touched = false; TouchSpeed = 0f; TouchTime = 0f;
        }

        public void Disarm() { _armTime = -1f; Touched = false; }

        public bool Armed => _armTime >= 0f;

        // Call every frame the ball is live. `highDive` is the keeper's own big-reach flag, read
        // it is accepted and IGNORED now that there is no EPIC tier, so the call sites that pass
        // a real dive flag need not all change. Cheap: one pass over an 8-entry array.
        public void Poll(BallController ball, ActiveRagdoll keeperBody, bool highDive)
        {
            if (Touched || _armTime < 0f || ball == null || keeperBody == null) return;
            if (!ball.BodyTouchedSince(keeperBody, _armTime, out float speed, out float when)) return;
            Touched = true; TouchSpeed = speed; TouchTime = when;
        }

        // True once a touched ball has clearly finished: settled, or a beat past the contact.
        // For modes that only resolve on out-of-play, this is what makes a CAUGHT shot call out at
        // all (the ball never leaves the field, so nothing else ever fires).
        public bool SettledAfterTouch(BallController ball)
        {
            if (!Touched) { _rest = 0f; return false; }
            if (ball != null && ball.Speed < RestSpeed) _rest += Time.deltaTime; else _rest = 0f;
            return _rest > RestHold || Time.time - TouchTime > TouchTimeout;
        }

        /// <summary>
        /// The save verdict. There is ONE save callout: the EPIC SAVE tier was removed outright
        /// (owner's call), so every stop reads the same however it was made. `allowEpic` is kept and
        /// IGNORED rather than deleted - accuracy passes false on purpose and several modes name it,
        /// so leaving it means no call site had to change to say what it already said.
        /// </summary>
        public string Callout(bool allowEpic = true) => "SAVE!";
    }
}
