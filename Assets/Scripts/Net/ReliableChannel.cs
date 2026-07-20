using System.Collections.Generic;

namespace Trickshot.Net
{
    /// <summary>
    /// Per-peer reliability bookkeeping for the ONE ordered/guaranteed channel the direct-IP
    /// transport exposes (NetChannel.Reliable). Raw UDP gives neither delivery nor ordering,
    /// but the session's lobby/roster/score/replay messages need both: RosterSync is
    /// last-applied-wins (a reordered stale roster would clobber the fresh one) and
    /// ReplayStart/ReplayEnd must not invert. This provides:
    ///   - outbound: a monotonic seq per packet + an unacked table, resent on a timer;
    ///   - inbound: in-order delivery with a small reorder buffer + duplicate rejection;
    ///   - a cumulative ACK (highest contiguous seq delivered) sent back to release the
    ///     sender's resends.
    ///
    /// This class is PURE bookkeeping - no sockets, no framing, no threads. The transport
    /// owns the socket, prepends the wire header, and calls these on the main thread (Poll).
    /// One instance per peer per direction pair (it tracks both our outbound + their inbound).
    /// Messages are small and infrequent, so a simple selective-repeat with a bounded buffer
    /// is ample.
    /// </summary>
    public class ReliableChannel
    {
        // Resend an unacked packet if it hasn't been acked within this long.
        public const float ResendInterval = 0.25f;
        // Don't buffer a received seq more than this far ahead of what we're waiting for
        // (bounds memory; the sender resends the gap anyway).
        const uint MaxAhead = 128;

        // ---- outbound ----
        uint _nextSeq = 1;                 // first packet is seq 1 (0 is never used)
        class Pending { public byte[] packet; public float lastSent; }
        readonly Dictionary<uint, Pending> _unacked = new Dictionary<uint, Pending>();

        // Assign the next outbound sequence number.
        public uint NextSeq() => _nextSeq++;

        // Remember a fully-framed packet so it can be resent until acked.
        public void Track(uint seq, byte[] packet, float now)
        {
            _unacked[seq] = new Pending { packet = packet, lastSent = now };
        }

        // Packets whose resend timer has elapsed; their timers are refreshed. Called each Poll.
        public List<byte[]> DueResends(float now)
        {
            var due = new List<byte[]>();
            foreach (var kv in _unacked)
            {
                if (now - kv.Value.lastSent >= ResendInterval)
                {
                    kv.Value.lastSent = now;
                    due.Add(kv.Value.packet);
                }
            }
            return due;
        }

        // Peer acked everything up to (and including) cumAck: stop resending those.
        public void Ack(uint cumAck)
        {
            if (_unacked.Count == 0) return;
            var drop = new List<uint>();
            foreach (var kv in _unacked) if (kv.Key <= cumAck) drop.Add(kv.Key);
            foreach (var s in drop) _unacked.Remove(s);
        }

        public bool HasUnacked => _unacked.Count > 0;

        // ---- inbound ----
        uint _expected = 1;                // next seq we want to deliver in order
        readonly Dictionary<uint, byte[]> _buffer = new Dictionary<uint, byte[]>();

        /// <summary>
        /// Accept an inbound reliable packet. Returns the app payloads now deliverable IN
        /// ORDER (empty if this arrived early and had to be buffered, or was a duplicate;
        /// possibly several if it filled a gap). Duplicates (seq &lt; expected) are dropped but
        /// still bump the ack so the sender stops resending them.
        /// </summary>
        public List<byte[]> Receive(uint seq, byte[] appPayload)
        {
            var ready = new List<byte[]>();
            if (seq < _expected) return ready;                 // duplicate / already delivered
            if (seq > _expected)
            {
                if (seq - _expected <= MaxAhead) _buffer[seq] = appPayload;   // hold for later
                return ready;                                  // gap: nothing to deliver yet
            }

            // seq == expected: deliver it, then drain any buffered consecutive seqs.
            ready.Add(appPayload);
            _expected++;
            while (_buffer.TryGetValue(_expected, out var next))
            {
                ready.Add(next);
                _buffer.Remove(_expected);
                _expected++;
            }
            return ready;
        }

        // Highest contiguous seq we've delivered (what to advertise as the cumulative ack).
        public uint CumAck => _expected - 1;
    }
}
