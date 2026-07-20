using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The exact input surface the Striker controller reads. Two implementations:
    ///   - GameInput  : the local device (keyboard/mouse), the single-player + local path.
    ///   - NetInputSource : a remote player's per-tick InputFrame (host drives their body
    ///                      from the wire), deriving the press/release EDGES the striker
    ///                      needs from consecutive frames.
    /// This lets one Striker code path drive a local human OR a networked player unchanged.
    /// </summary>
    public interface IStrikerInput
    {
        Vector2 Move { get; }
        float Scroll { get; }
        bool SprintHeld { get; }
        bool JumpPressed { get; }
        bool JumpHeld { get; }
        bool JumpReleased { get; }
        bool LeftLegHeld { get; }
        bool RightLegHeld { get; }
    }
}
