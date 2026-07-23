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

        // Reset (R) edge. Single-player fully resets the round; multiplayer re-serves the ball,
        // and a human crosser uses it to refill a ball at their feet. Networked via the reset
        // frame bit (edge re-derived on the receiving side).
        bool ResetPressed { get; }

        // Click edges (LMB/RMB): the keeper's save lunges + replay-skip read these. Derived
        // from the leg-held bits on the network side.
        bool LeftClickPressed { get; }
        bool RightClickPressed { get; }

        // Pass buttons (Q ground / E lofted). The striker's call-for-pass and the human
        // crosser's driven/chipped delivery read these. Networked via the passGround/
        // passLofted frame bits (edges re-derived on the receiving side).
        bool PassGroundPressed { get; }
        bool PassLoftedPressed { get; }
        bool PassGroundHeld { get; }
        bool PassLoftedHeld { get; }
        bool PassGroundReleased { get; }
        bool PassLoftedReleased { get; }

        // Emote chosen THIS tick (from the emote wheel): a Celebration.Emote index, or 255 for
        // none. One-shot - the source returns a real id only on the frame a pick happens. The
        // host reads it to start that body's Celebration; networked via InputFrame.emoteId.
        int EmoteId { get; }
    }
}
