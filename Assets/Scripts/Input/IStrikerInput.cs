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

        // Close-control modifier (the dribble "shield/jockey" key). Held: shortest touches
        // at reduced pace with a much quicker turn, and sprint is ignored. Networked via the
        // closeControl frame bit so a remote carrier dribbles the same way a local one does.
        bool CloseControlHeld { get; }
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

        // Pass buttons (E ground / Q lofted). The striker's call-for-pass and the human
        // crosser's driven/chipped delivery read these. Networked via the passGround/
        // passLofted frame bits (edges re-derived on the receiving side).
        bool PassGroundPressed { get; }
        bool PassLoftedPressed { get; }
        bool PassGroundHeld { get; }
        bool PassLoftedHeld { get; }
        bool PassGroundReleased { get; }
        bool PassLoftedReleased { get; }
        // Chip: a short, very high lob to set up a header or a bicycle. Third pass button.
        bool PassChipPressed { get; }
        bool PassChipHeld { get; }
        bool PassChipReleased { get; }

        /// <summary>
        /// Is this frame's input NEW? True always on a local device. On the networked path the host
        /// re-feeds the last received frame every tick whether or not one arrived, and a client stops
        /// sending entirely while paused or typing, so a held button stays pinned true indefinitely.
        /// The pass power bar only accumulates on a fresh frame - without that, fire-at-full would turn
        /// a dropped connection into a maximum-range pass nobody asked for.
        /// </summary>
        bool Fresh { get; }

        // Emote chosen THIS tick (from the emote wheel): a Celebration.Emote index, or 255 for
        // none. One-shot - the source returns a real id only on the frame a pick happens. The
        // host reads it to start that body's Celebration; networked via InputFrame.emoteId.
        int EmoteId { get; }

        // Set up a cross (Enter) edge. A human CROSSER presses it to bring the ball to their feet
        // and step into the crossing stance (CrosserControl). Networked via the cross frame bit;
        // the edge is re-derived on the receiving side like every other button.
        bool CrossPressed { get; }

        // Adult mode: the appendage stands to attention while this is HELD (Striker writes it onto
        // the body's AnatomySim; a body without one ignores it). Networked via the thirdLeg frame
        // bit so the host stands a remote player's up, and streamed back out in BodyState.erect so
        // every client's puppet shows it.
        bool ThirdLegHeld { get; }
    }
}
