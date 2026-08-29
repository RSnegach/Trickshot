using System.Collections.Generic;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Shared rolling-window gate for in-match calls (call-for-pass and anything added later that
    /// shouts on a player's behalf). At most Burst calls in any Window seconds, counted across ALL
    /// callers, so no single producer can flood on its own and no two producers can add up to a
    /// flood either.
    ///
    /// Overflow DROPS the call outright rather than queueing it. Held-and-released calls would fire
    /// against stale world state - a pass led onto a run that ended two seconds ago is worse than
    /// no pass at all.
    ///
    /// Uses unscaledTime so a pause or a slow-motion replay does not stretch the window, matching
    /// the quickchat limiters (QuickChatFeed.LocalAllow, NetSession.QcAllow).
    /// </summary>
    public static class CallLimiter
    {
        const int   Burst  = 3;      // calls allowed...
        const float Window = 3f;     // ...in any this-many seconds

        static readonly Queue<float> _times = new Queue<float>();

        /// <summary>True if a call may go out now, and records it. False = drop this call.</summary>
        public static bool Allow()
        {
            float now = Time.unscaledTime;
            while (_times.Count > 0 && now - _times.Peek() > Window) _times.Dequeue();
            if (_times.Count >= Burst) return false;
            _times.Enqueue(now);
            return true;
        }

        /// <summary>Clear the window (match start / kickoff), so a new match starts unthrottled.</summary>
        public static void Reset() => _times.Clear();
    }
}
