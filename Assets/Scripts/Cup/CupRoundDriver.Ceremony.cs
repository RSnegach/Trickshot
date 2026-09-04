using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The round driver's seams for the coin toss and the choreography (agent F): the little the
    /// ceremony needs from the driver that the contract did not already expose. Everything here
    /// is a thin, read-mostly wrapper - the ceremony runs BETWEEN Configure and Begin, in the
    /// Idle phase, and the driver's Intro entry (ParkAllAtMarks + the camera release) is what
    /// makes Begin safe to call after it whatever the ceremony walked where.
    /// </summary>
    public partial class CupRoundDriver
    {
        /// <summary>
        /// Raise a HUD callout from outside the driver: the coin toss's HEADS / TAILS with its
        /// "GHANA KICK FIRST" sub-line (CupHud splits a '\n' into flash + sub). Goes through the
        /// same <see cref="Callout"/> the verdicts use, so a HUD bound to the driver needs nothing new.
        /// </summary>
        public void Announce(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Callout?.Invoke(text);
        }

        /// <summary>The nation a side wears (a CupNations index), -1 when the bracket cannot say.</summary>
        public int NationOf(CupSide side) => NationOfSide(side);

        /// <summary>Bodies are simulated on this machine (Local / Host); a Client's are puppets nothing here may move.</summary>
        public bool SimulatesBodies => Authority != RoundAuthority.Client;

        /// <summary>The ball the round plays with (Setup.Ball), null before Configure.</summary>
        public BallController Ball => Setup != null ? Setup.Ball : null;
    }
}
