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
            _prev = _has ? _cur : f;   // first feed: no phantom edges
            _cur = f;
            _has = true;
        }

        public Vector2 Move => _cur.move;
        public float Scroll => 0f;     // remote air-pitch not synced in this pass (rare, cosmetic)
        public bool SprintHeld => _cur.sprint;

        public bool JumpHeld => _cur.jump;
        public bool JumpPressed => _cur.jump && !_prev.jump;
        public bool JumpReleased => !_cur.jump && _prev.jump;

        public bool LeftLegHeld => _cur.legL;
        public bool RightLegHeld => _cur.legR;
    }
}
