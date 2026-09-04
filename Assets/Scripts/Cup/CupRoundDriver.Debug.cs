using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Verification seams for the round driver: a play-mode test has no keyboard to take the
    /// human's kick with, and no way to steer a verdict. Both only act on the machine simulating
    /// the round (Local / Host), and only in the phases where the real path would do the same
    /// thing, so they can never desynchronise a state a client mirrors. Kept on purpose: driving
    /// the choreography through every branch (a goal, a miss, a losing miss) from the editor is
    /// how it gets verified.
    /// </summary>
    public partial class CupRoundDriver
    {
        /// <summary>
        /// Take the current kick for the human taker with the AI bot (the real charge / run-up /
        /// strike through CupBotTaker), instead of the weak watchdog AutoLaunch. Armed phase only.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public bool DebugAutoKick()
        {
            if (!Configured || Authority == RoundAuthority.Client || Phase != RoundPhase.Armed) return false;
            var tb = _takerBody;
            if (tb == null || !tb.Alive) return false;
            var s = Setup;
            _taker.Reset();   // hands the ball back; Begin takes it again
            s.Ball.ResetTo(BallSpotPos);
            s.Ball.IgnoreBody(tb.Ragdoll, true);
            if (tb.Bot == null) tb.Bot = new CupBotTaker(_botRng);
            tb.Bot.Arm(_taker, s.Stage);
            _taker.Begin(tb.Bot, tb.Ragdoll, s.Ball, BallSpotPos, SimConfig.AttackGoalCenter,
                         displayOnly: false, combinedOverride: CupTuning.TakerCombined(s.Stage), aimPoint: null,
                         leftFootedOverride: tb.Bot.LeftFooted ? 1 : 0);
            _takerArmed = true;
            _autoLaunched = false;
            return true;
        }

        /// <summary>
        /// Resolve the current kick with a chosen outcome (Armed or Live), skipping the shot: for
        /// steering a round into a specific choreography branch. The taker is stood down and the
        /// ball is left where it is; the next placement cut resets both.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        public bool DebugForceVerdict(KickOutcome outcome)
        {
            if (!Configured || Authority == RoundAuthority.Client) return false;
            if (Phase != RoundPhase.Armed && Phase != RoundPhase.Live) return false;
            if (_takerBody == null) return false;
            _takerArmed = false;
            if (_takerBody.Bot != null) _takerBody.Bot.Disarm();
            _taker.Reset();
            Verdict(outcome);
            return true;
        }
    }
}
