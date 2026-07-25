using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Networked ACCURACY competition: shooters take turns hitting the pop-up targets in the goal
    /// with free kicks, and the highest target-point total wins. Keeper slot 0 can be a human, an
    /// AI, or left empty (no GK) - exactly as in the set-piece shootout.
    ///
    /// The whole thing IS the set-piece shootout with accuracy scoring, so rather than duplicate
    /// ~900 lines of identical netcode (body spawning, snapshot interpolation, client prediction,
    /// replay, quickchat, jersey sync, turn rotation, scoreboard) this is a thin marker component
    /// that drives NetSetPieceMatch in AccuracyMode. That driver then:
    ///   * scores the target board instead of goals,
    ///   * ends each turn on the host's chosen kick count or per-turn timer,
    ///   * re-arms the ball between kicks with no goal/save verdict or replay.
    ///
    /// Added by GameBootstrap.BuildNetAccuracy; it configures the underlying driver in Awake and
    /// then does nothing itself.
    /// </summary>
    public class NetAccuracyMatch : MonoBehaviour
    {
        NetSetPieceMatch _driver;

        public void Configure(GameInput input, Camera cam, GameCamera gameCam, BallController ball,
                              Material torso, Material limb, Material glove, Transform root)
        {
            // AccuracyMode must be set BEFORE Configure so the driver reads the accuracy config
            // and builds the target board during setup.
            _driver = gameObject.AddComponent<NetSetPieceMatch>();
            _driver.AccuracyMode = true;
            _driver.Configure(input, cam, gameCam, ball, torso, limb, glove, root);
        }
    }
}
