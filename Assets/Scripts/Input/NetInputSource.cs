using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// Adapts a remote player's per-tick InputFrame (arriving over the wire) into the
    /// IStrikerInput surface the Striker reads, so the HOST can drive a networked player's
    /// body with the same controller code as a local one. Edges (Pressed/Released) are
    /// derived by comparing the current held state to the previous one.
    ///
    /// The host calls Feed(frame) each fixed step with that slot's latest input; the Striker
    /// then reads Move/held/edge exactly as it would from a device.
    /// </summary>
    public class NetInputSource : IStrikerInput
    {
        InputFrame _cur, _prev;
        bool _has;

        public void Feed(in InputFrame f)
        {
            // A REPEAT of the frame we already hold is not a new sample. The host feeds every tick
            // regardless of whether the client sent anything, so without this test a quiet client's
            // held buttons look like a button being held down forever. See Fresh.
            _fresh = !_has || f.tick != _cur.tick;
            _prev = _has ? _cur : f;   // first feed: no phantom edges
            _cur = f;
            _has = true;
        }

        public Vector2 Move => _cur.move;
        public float Scroll => 0f;     // remote air-pitch not synced in this pass (rare, cosmetic)
        public bool SprintHeld => _cur.sprint && !_cur.closeControl;
        public bool CloseControlHeld => _cur.closeControl;

        // The remote player's desired facing yaw (camera yaw), synced each frame. Used to turn
        // a remote keeper's body / aim a remote crosser to match their view.
        public float LookYaw => _cur.lookYaw;

        // The remote player's camera pitch (deg), synced each frame. The set-piece taker reads
        // this together with LookYaw to reconstruct the remote shooter's 3D aim direction.
        public float LookPitch => _cur.lookPitch;

        public bool JumpHeld => _cur.jump;
        public bool JumpPressed => _cur.jump && !_prev.jump;
        public bool JumpReleased => !_cur.jump && _prev.jump;

        public bool LeftLegHeld => _cur.legL;
        public bool RightLegHeld => _cur.legR;

        // Reset edge from the reset bit (same rising-edge scheme as jump).
        public bool ResetPressed => _cur.reset && !_prev.reset;

        // Click edges from the leg-held bits (LMB = left leg, RMB = right leg).
        public bool LeftClickPressed => _cur.legL && !_prev.legL;
        public bool RightClickPressed => _cur.legR && !_prev.legR;

        // Pass edges derived from the held pass bits on the wire (same scheme as jump).
        public bool PassGroundHeld => _cur.passGround;
        public bool PassLoftedHeld => _cur.passLofted;
        public bool PassGroundPressed => _cur.passGround && !_prev.passGround;
        public bool PassLoftedPressed => _cur.passLofted && !_prev.passLofted;
        public bool PassGroundReleased => !_cur.passGround && _prev.passGround;
        public bool PassLoftedReleased => !_cur.passLofted && _prev.passLofted;
        public bool PassChipHeld     => _cur.passChip;
        public bool PassChipPressed  => _cur.passChip && !_prev.passChip;
        public bool PassChipReleased => !_cur.passChip && _prev.passChip;

        /// <summary>
        /// Did a NEW frame arrive for this slot? The host calls Feed with NetSession.InputForSlot every
        /// tick whether or not the client sent anything, and the session keeps only the newest frame, so
        /// a client that goes quiet - paused, typing in quickchat, or simply dropping packets - leaves
        /// every held bit pinned true forever. Under fire-on-release that was harmless because Released
        /// never became true. The pass bar fires AT FULL, so it must not charge on a repeat: this compares
        /// the frame's own tick, which the client stamps and increments per send.
        /// </summary>
        public bool Fresh => _fresh;
        bool _fresh;

        // Emote pick from the wire: report it only on the tick it first appears (id != 255 and
        // changed from the previous frame's id) so the host starts the emote exactly once, even
        // though the same held frame may repeat across snapshot ticks.
        public int EmoteId => (_cur.emoteId != 255 && _cur.emoteId != _prev.emoteId) ? _cur.emoteId : 255;
    }
}
