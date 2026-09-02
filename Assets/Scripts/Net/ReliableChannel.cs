using System.Collections.Generic;

namespace Trickshot.Net
{
    /// <summary>
    /// Per-peer reliability bookkeeping for ONE ordered/guaranteed stream over the direct-IP
    /// transport (NetChannel.Reliable, and a second instance for NetChannel.ReliableBulk). Raw UDP
    /// gives neither delivery nor ordering, but the session's lobby/roster/score/replay messages
    /// need both: RosterSync is last-applied-wins (a reordered stale roster would clobber the fresh
    /// one) and ReplayStart/ReplayEnd must not invert. This provides:
    ///   - outbound: a monotonic seq per packet, a SEND WINDOW, and an unacked table resent on a
    ///     timer;
    ///   - inbound: in-order delivery with a small reorder buffer + duplicate rejection;
    ///   - a cumulative ACK (highest contiguous seq delivered) sent back to release the
    ///     sender's resends.
    ///
    /// THE SEND WINDOW. At most `window` packets are in flight (sent, unacked); anything past that
    /// queues and goes out as acks come back. Without it a 400-chunk jersey went onto the wire in
    /// one frame, the socket dropped a slice of it, and every dropped chunk cost a ResendInterval
    /// stall - for the whole in-order stream behind it, gameplay messages included. With it the
    /// burst is paced to what the link is actually acking.
    ///
    /// This class is PURE bookkeeping - no sockets, no framing, no threads. The transport
    /// owns the socket, prepends the wire header, and calls these on the main thread (Poll).
    /// One instance per peer per stream (it tracks both our outbound + their inbound).
    /// </summary>
    public class ReliableChannel
    {
        // Resend an unacked packet if it hasn't been acked within this long.
        public const float ResendInterval = 0.25f;
        // Don't buffer a received seq more than this far ahead of what we're waiting for
        // (bounds memory; the sender resends the gap anyway).
        const uint MaxAhead = 128;

        readonly int _window;
        public ReliableChannel(int window = 32) { _window = window < 1 ? 1 : window; }

        // ---- outbound ----
        uint _nextSeq = 1;                 // first packet is seq 1 (0 is never used)
        class Pending { public uint seq; public byte[] packet; public float lastSent; }
        readonly Dictionary<uint, Pending> _unacked = new Dictionary<uint, Pending>();
        readonly Queue<Pending> _queue = new Queue<Pending>();   // past the window, in seq order
        readonly List<uint> _drop = new List<uint>();

        // Assign the next outbound sequence number.
        public uint NextSeq() => _nextSeq++;

        /// <summary>
        /// Remember a fully-framed packet. Returns true if it should go on the wire NOW (inside the
        /// window); false if it was queued - Ack() hands it back once the window opens. Seqs are
        /// assigned in Track order and the queue is FIFO, so the stream stays in order either way.
        /// </summary>
        public bool Track(uint seq, byte[] packet, float now)
        {
            var p = new Pending { seq = seq, packet = packet, lastSent = now };
            if (_unacked.Count < _window) { _unacked[seq] = p; return true; }
            _queue.Enqueue(p);
            return false;
        }

        // Packets whose resend timer has elapsed are appended to `into`; their timers are
        // refreshed. Called each Poll with a reused list (no allocation on a quiet frame).
        public void DueResends(float now, List<byte[]> into)
        {
            foreach (var kv in _unacked)
            {
                if (now - kv.Value.lastSent >= ResendInterval)
                {
                    kv.Value.lastSent = now;
                    into.Add(kv.Value.packet);
                }
            }
        }

        /// <summary>
        /// Peer acked everything up to (and including) cumAck: stop resending those, then move
        /// queued packets into the freed window. Those are appended to `send` (now tracked as in
        /// flight, timer started) for the caller to put on the wire.
        /// </summary>
        public void Ack(uint cumAck, List<byte[]> send, float now)
        {
            if (_unacked.Count > 0)
            {
                _drop.Clear();
                foreach (var kv in _unacked) if (kv.Key <= cumAck) _drop.Add(kv.Key);
                foreach (var s in _drop) _unacked.Remove(s);
            }
            while (_queue.Count > 0 && _unacked.Count < _window)
            {
                var p = _queue.Dequeue();
                p.lastSent = now;
                _unacked[p.seq] = p;
                send.Add(p.packet);
            }
        }

        public bool HasUnacked => _unacked.Count > 0 || _queue.Count > 0;

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
