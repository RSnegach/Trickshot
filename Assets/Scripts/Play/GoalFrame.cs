using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Marker for the goal frame colliders: both uprights, the crossbar, the back uprights
    /// and the depth rails. The ball reads this on contact to play the woodwork clang.
    ///
    /// A marker rather than a name match or a material check, same reasoning as NetBackstop:
    /// the frame is built in three separate places (Arena, PitchBuilder.BuildFarGoal,
    /// ScrimmageArena.BuildGoal), the net strings hanging off the same goalRoot must NOT
    /// count, and matching on "Post"/"Bar" would break the first time a part is renamed.
    /// Nothing here changes the bounce - the frame's own bouncy physics material still does
    /// the deflection.
    /// </summary>
    public class GoalFrame : MonoBehaviour { }
}
