using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Marker for the invisible colliders behind/around the goal net. The ball reads
    /// this on contact to KILL its rebound in code - a PhysicMaterial can't, because
    /// the ball's own material uses Maximum bounce-combine (higher priority), so the
    /// ball's bounce always wins the material blend. Deadening in code makes the ball
    /// drop into the net and roll instead of pinging off.
    /// </summary>
    public class NetBackstop : MonoBehaviour { }
}
