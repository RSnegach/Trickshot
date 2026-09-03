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
        public bool Epic { get; private set; }      // latched at the contact
        public float TouchSpeed { get; private set; }
        public float TouchTime { get; private set; }

        // Start a fresh attempt. Contacts before this never count.
        public void Arm()
        {
            _armTime = Time.time; _rest = 0f;
            Touched = false; Epic = false; TouchSpeed = 0f; TouchTime = 0f;
        }

        public void Disarm() { _armTime = -1f; Touched = false; Epic = false; }

        public bool Armed => _armTime >= 0f;

        // Call every frame the ball is live. `highDive` is the keeper's own big-reach flag, read
        // when the touch is found. Cheap: one pass over an 8-entry array until it latches.
        public void Poll(BallController ball, ActiveRagdoll keeperBody, bool highDive)
        {
            if (Touched || _armTime < 0f || ball == null || keeperBody == null) return;
            if (!ball.BodyTouchedSince(keeperBody, _armTime, out float speed, out float when)) return;
            Touched = true; TouchSpeed = speed; TouchTime = when;
            // Epic gates on the IMPACT speed, not the ball's speed on the frame we noticed (which
            // the touch has already slowed), so a hard shot no longer under-reports as a plain save.
            Epic = speed >= SimConfig.KeeperEpicSaveSpeed || highDive;
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
        /// The save verdict. <paramref name="allowEpic"/> false collapses it to a plain SAVE - for
        /// ACCURACY, where the keeper is scenery the shot has to beat rather than the point of the
        /// mode: a save there is a strike against the player, so dressing it up as a highlight
        /// celebrates the wrong side of the outcome.
        /// </summary>
        public string Callout(bool allowEpic = true) => (Epic && allowEpic) ? "EPIC SAVE!" : "SAVE!";
    }
}
