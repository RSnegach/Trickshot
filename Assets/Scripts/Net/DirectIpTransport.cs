using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Direct-IP UDP transport: play with friends WITHOUT Steam or a paid server. One player
    /// hosts (their PC is authoritative, exactly like the Steam design); others Join by the
    /// host's IP:port. Works on a LAN for free, and over the internet if everyone joins a
    /// free virtual LAN (Tailscale / ZeroTier) - then it is just LAN again from here.
    ///
    /// It is a drop-in INetTransport sibling of LocalTransport / SteamTransport; the session,
    /// lobby, sim and snapshot loop are unchanged. Multiplayer.NewTransport() picks it when
    /// Steam isn't built in.
    ///
    /// Threading contract (mirrors LocalTransport): a background thread ONLY receives packets
    /// and enqueues them; EVERYTHING else - peer bookkeeping, event dispatch (PeerJoined /
    /// Connected / MessageReceived), resends, keepalive, timeouts - happens on the main thread
    /// in Poll(). The rest of the game stays single-threaded.
    ///
    /// Wire frame = a 1-byte kind + optional header, wrapping the app payload untouched:
    ///   [0] Unreliable : [kind][payload]                 (raw; newest-wins, drops fine)
    ///   [1] Reliable   : [kind][seq u32][payload]         (in-order + acked; lobby/score/replay)
    ///   [2] Ack        : [kind][cumAck u32]
    ///   [3] Ping       : [kind]                           (keepalive for disconnect detection)
    ///   [4] Probe      : discovery, see LobbyProbe        (from a stranger; no peer relationship)
    ///   [5] ProbeReply : discovery, see LobbyProbe        (sent only; dropped if received)
    /// The app payload delivered to MessageReceived is the SAME byte[] NetCodec produced, so
    /// NetReader / MsgType are untouched.
    ///
    /// Kinds 0-3 are peer traffic and go through the peer table. Kinds 4-5 deliberately do NOT:
    /// they are exchanged with machines that are not peers and must never become peers. See the
    /// interception at the top of HandlePacket.
    /// </summary>
    public class DirectIpTransport : INetTransport
    {
        // 4 and 5 are the discovery probe / reply (LobbyProbe) and are filtered before the switch
        // in HandlePacket - a reply is dropped on its first byte alone - so the bulk stream's kinds
        // skip them.
        const byte FrameUnreliable = 0, FrameReliable = 1, FrameAck = 2, FramePing = 3,
                   FrameReliableBulk = 6, FrameAckBulk = 7;
        // Send windows (ReliableChannel): packets in flight before the rest queue. Control messages
        // are small and rare; the bulk stream carries jerseys, ~400 x 1 KB, paced by its acks.
        const int ControlWindow = 64, BulkWindow = 32;
        const float KeepaliveInterval = 1.0f;   // ping cadence
        const float PeerTimeout = 5.0f;          // no packet this long -> peer is gone
        // How long the receive thread is allowed to sit blocked before it comes back around to
        // re-check _running. See OpenSocket: on macOS/Linux this is the ONLY thing that lets the
        // thread be stopped at all. Shutdown waits a small multiple of it.
        const int SocketWakeMs = 200;
        // SIO_UDP_CONNRESET. A Winsock ioctl, not a portable one (see OpenSocket).
        const int SioUdpConnReset = unchecked((int)0x9800000C);
        // Cap on distinct receive-thread faults reported to the main thread. A genuinely broken
        // socket would otherwise log a warning per loop iteration for the rest of the session.
        const int MaxRxErrorsLogged = 8;

        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public PeerId LocalPeer { get; private set; }
        public PeerId HostPeer { get; private set; }

        public event Action<PeerId> PeerJoined;
        public event Action<PeerId> PeerLeft;
        public event Action Connected;
        public event Action Disconnected;
        public event Action<PeerId, byte[]> MessageReceived;

        /// <summary>
        /// Set by a HOST session to describe itself to browsers (see INetTransport). Read on the
        /// main thread only, from inside Poll, so the provider may safely touch session state.
        /// </summary>
        public Func<LobbyAdvert> AdvertProvider { get; set; }

        UdpClient _udp;
        Thread _rxThread;
        volatile bool _running;

        // Background thread -> main thread. Only the rx thread enqueues; only Poll drains.
        readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> _inbox = new ConcurrentQueue<(IPEndPoint, byte[])>();
        readonly ConcurrentQueue<string> _rxErrors = new ConcurrentQueue<string>();   // logged on main thread
        int _rxErrCount;              // rx thread only: bounds the above (MaxRxErrorsLogged)

        // Peer registry (main thread only). Keyed by the encoded ulong of the endpoint, which
        // is a perfect key for an IPv4:port pair and sidesteps IPEndPoint equality quirks.
        readonly Dictionary<ulong, PeerId> _peerByEp = new Dictionary<ulong, PeerId>();     // ep-handle -> peer
        readonly Dictionary<ulong, IPEndPoint> _epByPeer = new Dictionary<ulong, IPEndPoint>(); // peer.Value -> ep
        readonly Dictionary<ulong, ReliableChannel> _relByPeer = new Dictionary<ulong, ReliableChannel>();
        readonly Dictionary<ulong, ReliableChannel> _bulkByPeer = new Dictionary<ulong, ReliableChannel>();
        // Reused scratch, all main-thread: framed unreliable packets (UdpClient.Send copies, so the
        // buffer never has to outlive the call), released/resent packet lists, SendToAll's targets.
        byte[] _txScratch = new byte[2048];
        readonly List<byte[]> _txList = new List<byte[]>();
        readonly List<ulong> _targets = new List<ulong>();
        readonly Dictionary<ulong, float> _lastRecv = new Dictionary<ulong, float>();       // peer.Value -> time
        readonly Dictionary<ulong, float> _lastPing = new Dictionary<ulong, float>();       // peer.Value -> time

        ulong _nextPeer = 1;             // host = 1; clients get 2,3,...
        bool _pendingConnected;
        float _now;                      // main-thread clock, advanced once per frame in Poll
        int _lastPollFrame = -1;         // guards _now against multiple Poll calls in one frame

        // ---- lifecycle ----
        public void StartHost(int maxPlayers)
        {
            IsHost = true;
            LocalPeer = new PeerId(_nextPeer++);   // host is peer 1
            HostPeer = LocalPeer;
            OpenSocket(NetEndpoint.DefaultPort);
            IsRunning = _running;
        }

        public void Join(ulong lobbyOrHost)
        {
            IsHost = false;
            var hostEp = NetEndpoint.Decode(lobbyOrHost);
            // Host is peer 1; give ourselves an arbitrary local id (never sent on the wire -
            // the host identifies us by our packets' source endpoint).
            HostPeer = new PeerId(_nextPeer++);      // 1
            LocalPeer = new PeerId(_nextPeer++);      // 2
            RegisterPeer(HostPeer, hostEp);
            OpenSocket(0);                            // ephemeral local port
            _pendingConnected = _running;
            IsRunning = _running;
        }

        void OpenSocket(int port)
        {
            try
            {
                _udp = new UdpClient(port);           // IPv4, binds the port
                // The receive thread has to be able to NOTICE that _running went false. On Windows,
                // Close()ing a socket while another thread is blocked in Receive() throws out of
                // that Receive at once; on macOS and Linux, closing a descriptor does NOT reliably
                // wake a thread parked in recvfrom(). Without a timeout that thread stays blocked
                // forever, Shutdown's Join expires, the handle leaks, and the port stays bound - so
                // hosting a second session in the same process fails to bind and looks like
                // "multiplayer stopped working" on exactly those two platforms. A finite block means
                // the loop always comes back to re-check the flag. Timing out is normal, not a fault.
                _udp.Client.ReceiveTimeout = SocketWakeMs;
                // Windows only: without this, a peer that has gone away makes the OS translate the
                // resulting ICMP port-unreachable into WSAECONNRESET on the NEXT Receive, i.e. an
                // error raised against an unrelated packet. The loop survives it either way, but
                // suppressing it keeps a normal departure from looking like a socket fault. The
                // ioctl is a Winsock concept and throws on Unix, hence the local guard.
                try { _udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null); }
                catch { }
                _running = true;
                _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "TrickshotUDP" };
                _rxThread.Start();
            }
            catch (Exception e)
            {
                _running = false;
                Debug.LogError("DirectIpTransport: failed to open socket on port " + port + ": " + e.Message);
            }
        }

        public void Shutdown()
        {
            if (!IsRunning && !_running) return;
            _running = false;
            IsRunning = false;
            try { _udp?.Close(); } catch { }         // unblocks the blocking Receive() on Windows
            // Long enough for the receive timeout to expire and the loop to see _running == false,
            // which is how the thread exits on macOS/Linux where Close() does not wake it. On
            // Windows the Close above has already thrown it out, so this returns immediately.
            try { _rxThread?.Join(SocketWakeMs * 3); } catch { }
            _udp = null; _rxThread = null;
            Disconnected?.Invoke();
        }

        // ---- background receive (enqueue only) ----
        void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref any);
                    // Copy the endpoint (Receive reuses/rewrites `any` in place).
                    _inbox.Enqueue((new IPEndPoint(any.Address, any.Port), data));
                }
                catch (SocketException se)
                {
                    // TimedOut is ReceiveTimeout firing on a quiet socket, which is what makes this
                    // loop interruptible on macOS/Linux, and ConnectionReset is a peer that left
                    // (see OpenSocket). Neither is an error and neither should be logged. Anything
                    // else is either the socket being closed under us - _running is already false,
                    // so we leave - or a real fault, reported a bounded number of times.
                    if (se.SocketErrorCode == SocketError.TimedOut) continue;
                    if (se.SocketErrorCode == SocketError.ConnectionReset) continue;
                    if (!_running) break;
                    if (_rxErrCount++ < MaxRxErrorsLogged) _rxErrors.Enqueue(se.Message);
                }
                catch (ObjectDisposedException) { break; }               // socket disposed
                catch (Exception e)
                {
                    if (_rxErrCount++ < MaxRxErrorsLogged) _rxErrors.Enqueue(e.Message);
                    if (!_running) break;
                }
            }
        }

        // ---- send ----
        public void Send(PeerId to, byte[] data, NetChannel channel) => SendTo(to, data, channel);

        void SendTo(PeerId to, byte[] data, NetChannel channel)
        {
            if (!_running || !_epByPeer.TryGetValue(to.Value, out var ep)) return;
            if (channel == NetChannel.Unreliable)
            {
                // Framed into the reused scratch buffer: no allocation per input frame or snapshot.
                int n = 1 + (data?.Length ?? 0);
                if (_txScratch.Length < n) _txScratch = new byte[Mathf.NextPowerOfTwo(n)];
                _txScratch[0] = FrameUnreliable;
                if (data != null) Buffer.BlockCopy(data, 0, _txScratch, 1, data.Length);
                RawSend(_txScratch, n, ep);
                return;
            }
            // Reliable / ReliableBulk: two independent ordered streams, each with its own sequence
            // space, window and ack kind (see NetChannel). A packet past the window is queued in the
            // channel and comes out of Ack() as the peer catches up.
            bool bulk = channel == NetChannel.ReliableBulk;
            var rel = bulk ? BulkFor(to.Value) : ChannelFor(to.Value);
            uint seq = rel.NextSeq();
            byte[] packet = FrameReliablePacket(bulk ? FrameReliableBulk : FrameReliable, seq, data);
            if (rel.Track(seq, packet, _now)) RawSend(packet, ep);
        }

        public void SendToAll(byte[] data, NetChannel channel)
        {
            // Snapshot the peer list (a reused list; handlers may mutate the map) and skip ourselves.
            _targets.Clear();
            _targets.AddRange(_epByPeer.Keys);
            for (int i = 0; i < _targets.Count; i++)
            {
                ulong pv = _targets[i];
                if (pv != LocalPeer.Value) SendTo(new PeerId(pv), data, channel);
            }
        }

        void RawSend(byte[] packet, IPEndPoint ep) => RawSend(packet, packet.Length, ep);

        void RawSend(byte[] buf, int len, IPEndPoint ep)
        {
            try { if (_running) _udp.Send(buf, len, ep); }
            catch (Exception e) { Debug.LogWarning("DirectIpTransport send failed: " + e.Message); }
        }

        // ---- main-thread pump ----
        public void Poll()
        {
            if (!_running) { DrainErrors(); return; }
            // Advance the transport clock ONCE PER FRAME, not once per call. Poll is intentionally
            // called from more than one place (the session-lifetime pump, the match NetPump, the
            // lobby UI), and a naive `_now += deltaTime` ran the clock at 2x when two of them
            // overlapped in a frame - which halves the effective PeerTimeout and causes spurious
            // disconnects. Keying off frameCount makes extra calls idempotent for timing purposes
            // while still draining the inbox every time.
            if (_lastPollFrame != Time.frameCount)
            {
                _lastPollFrame = Time.frameCount;
                _now += Time.unscaledDeltaTime;
            }

            if (_pendingConnected) { _pendingConnected = false; Connected?.Invoke(); }

            // 1) Deliver inbound packets (assign peers, fire events - all main thread).
            while (_inbox.TryDequeue(out var pkt))
                HandlePacket(pkt.from, pkt.data);

            // 2) Resend unacked reliable packets whose timer elapsed, on both streams.
            ResendDue(_relByPeer);
            ResendDue(_bulkByPeer);

            // 3) Keepalive pings (~1 Hz) to every known peer.
            SendKeepalives();

            // 4) Drop peers we haven't heard from in PeerTimeout.
            CheckTimeouts();

            DrainErrors();
        }

        void HandlePacket(IPEndPoint from, byte[] data)
        {
            if (data == null || data.Length < 1) return;
            ulong epKey = SafeEncode(from);
            if (epKey == 0) return;                 // non-IPv4 / unencodable source

            // ---- discovery: answered OUT OF BAND, before any peer bookkeeping ----
            // A probe arrives from a machine we have no relationship with: someone merely looking at
            // a list of games. Letting it fall through to the peer-resolution branch below would be
            // a real bug, not a cosmetic one: the host would mint a PeerId for every browser on the
            // network and fire PeerJoined, the session answers PeerJoined with a genuine slot
            // assignment, and so idle browsers would fill the lobby without anybody having joined it.
            // Answer and return. Note it deliberately does NOT touch _lastRecv - a probe is not
            // liveness for a peer, because there is no peer.
            if (LobbyProbe.IsProbe(data))
            {
                if (IsHost) AnswerProbe(from);
                return;
            }
            // A reply is ours to send, never to receive here: a browser listens for replies on its
            // own throwaway socket. Drop it explicitly so a stray or spoofed one cannot reach the
            // peer-minting branch either.
            if (data[0] == LobbyProbe.FrameProbeReply) return;

            // Resolve (or, host-side, create) the sending peer.
            if (!_peerByEp.TryGetValue(epKey, out var peer))
            {
                if (!IsHost) return;                // clients only ever talk to the known host
                peer = new PeerId(_nextPeer++);
                RegisterPeer(peer, from);
                PeerJoined?.Invoke(peer);           // BEFORE this packet's payload is delivered
            }
            _lastRecv[peer.Value] = _now;

            byte kind = data[0];
            switch (kind)
            {
                case FramePing:
                    break;                          // liveness only (lastRecv already bumped)

                case FrameUnreliable:
                    MessageReceived?.Invoke(peer, Slice(data, 1));
                    break;

                case FrameReliable:
                case FrameReliableBulk:
                {
                    if (data.Length < 5) break;
                    bool bulk = kind == FrameReliableBulk;
                    uint seq = ReadU32(data, 1);
                    var rel = bulk ? BulkFor(peer.Value) : ChannelFor(peer.Value);
                    byte[] payload = Slice(data, 5);
                    foreach (var ready in rel.Receive(seq, payload))
                        MessageReceived?.Invoke(peer, ready);
                    // Ack whatever we've now delivered in order (releases the sender's resends).
                    if (_epByPeer.TryGetValue(peer.Value, out var ep))
                        RawSend(FrameAckPacket(bulk ? FrameAckBulk : FrameAck, rel.CumAck), ep);
                    break;
                }

                case FrameAck:
                case FrameAckBulk:
                {
                    if (data.Length < 5) break;
                    uint cumAck = ReadU32(data, 1);
                    var rel = kind == FrameAckBulk ? BulkFor(peer.Value) : ChannelFor(peer.Value);
                    // The ack frees window: whatever was queued behind it goes out now.
                    _txList.Clear();
                    rel.Ack(cumAck, _txList, _now);
                    if (_txList.Count > 0 && _epByPeer.TryGetValue(peer.Value, out var ep))
                        for (int i = 0; i < _txList.Count; i++) RawSend(_txList[i], ep);
                    break;
                }
            }
        }

        void SendKeepalives()
        {
            byte[] ping = { FramePing };
            foreach (var kv in _epByPeer)
            {
                if (kv.Key == LocalPeer.Value) continue;
                float last = _lastPing.TryGetValue(kv.Key, out var t) ? t : -999f;
                if (_now - last >= KeepaliveInterval) { _lastPing[kv.Key] = _now; RawSend(ping, kv.Value); }
            }
        }

        void CheckTimeouts()
        {
            List<ulong> gone = null;
            foreach (var kv in _lastRecv)
                if (_now - kv.Value > PeerTimeout) (gone ??= new List<ulong>()).Add(kv.Key);
            if (gone == null) return;

            foreach (var pv in gone)
            {
                bool wasHost = !IsHost && pv == HostPeer.Value;
                DropPeer(pv);
                if (IsHost) { PeerLeft?.Invoke(new PeerId(pv)); continue; }
                // A CLIENT that lost the host is finished: DropPeer forgets the host endpoint, and
                // HandlePacket discards packets from unknown senders on a client, so no later host
                // packet could ever be processed anyway - the session could never recover but kept
                // reporting itself Active, which is what left drivers running a dead match. Mark the
                // transport not-running so Session.Active is honest, THEN raise Disconnected.
                if (wasHost) IsRunning = false;
                Disconnected?.Invoke();
            }
        }

        // ---- peer table ----
        void RegisterPeer(PeerId peer, IPEndPoint ep)
        {
            ulong epKey = SafeEncode(ep);
            if (epKey != 0) _peerByEp[epKey] = peer;
            _epByPeer[peer.Value] = ep;
            _lastRecv[peer.Value] = _now;
            // Start this peer's reliable channel FRESH. Channels are keyed by peer id, and a peer id
            // is minted per endpoint - so a client that left and rejoined quickly (same ephemeral
            // port, before the 5s timeout reaped the old entry) used to inherit the previous
            // session's sequence state. Its brand-new Hello at seq 1 then looked like a duplicate and
            // was silently dropped, leaving the joiner stuck at "Connecting..." while the host showed
            // a ghost occupant.
            _relByPeer.Remove(peer.Value);
            _bulkByPeer.Remove(peer.Value);
        }

        void DropPeer(ulong pv)
        {
            if (_epByPeer.TryGetValue(pv, out var ep))
            {
                ulong epKey = SafeEncode(ep);
                if (epKey != 0) _peerByEp.Remove(epKey);
            }
            _epByPeer.Remove(pv);
            _relByPeer.Remove(pv);
            _bulkByPeer.Remove(pv);
            _lastRecv.Remove(pv);
            _lastPing.Remove(pv);
        }

        ReliableChannel ChannelFor(ulong peerValue)
        {
            if (!_relByPeer.TryGetValue(peerValue, out var rel))
            {
                rel = new ReliableChannel(ControlWindow);
                _relByPeer[peerValue] = rel;
            }
            return rel;
        }

        ReliableChannel BulkFor(ulong peerValue)
        {
            if (!_bulkByPeer.TryGetValue(peerValue, out var rel))
            {
                rel = new ReliableChannel(BulkWindow);
                _bulkByPeer[peerValue] = rel;
            }
            return rel;
        }

        void ResendDue(Dictionary<ulong, ReliableChannel> map)
        {
            foreach (var kv in map)
            {
                if (!_epByPeer.TryGetValue(kv.Key, out var ep)) continue;
                _txList.Clear();
                kv.Value.DueResends(_now, _txList);
                for (int i = 0; i < _txList.Count; i++) RawSend(_txList[i], ep);
            }
        }

        // ---- discovery ----

        /// <summary>
        /// Answer one probe. Runs on the main thread (called from Poll via HandlePacket), so the
        /// provider is free to read live session state.
        /// </summary>
        void AnswerProbe(IPEndPoint to)
        {
            var provider = AdvertProvider;
            if (provider == null) return;           // not advertising (no session, or not a host)
            LobbyAdvert ad;
            try { ad = provider(); } catch { return; }
            // A private session says NOTHING. Silence is the honest implementation: shipping a
            // "private" flag inside the reply would still put the session on every browser's list
            // and then trust the browser to hide it.
            if (!ad.visible) return;
            RawSend(LobbyProbe.BuildReply(ad), to);
        }

        public void ListLobbies(Action<List<LobbyInfo>> onResults)
        {
            // Direct IP has no matchmaker to ask, so discovery is an active sweep this machine runs
            // itself: enumerate the tailnet from the local Tailscale client, probe each peer by
            // unicast, and broadcast on the LAN for good measure. Results are asynchronous and
            // delivered from TailnetDiscovery.Poll() on the main thread.
            TailnetDiscovery.Sweep(onResults);
        }

        // ---- framing helpers ----
        // `kind` is FrameReliable or FrameReliableBulk (and FrameAck / FrameAckBulk for the ack):
        // the same framing for both streams, told apart by the first byte.
        static byte[] FrameReliablePacket(byte kind, uint seq, byte[] payload)
        {
            var buf = new byte[5 + (payload?.Length ?? 0)];
            buf[0] = kind;
            WriteU32(buf, 1, seq);
            if (payload != null) Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);
            return buf;
        }
        static byte[] FrameAckPacket(byte kind, uint cumAck)
        {
            var buf = new byte[5];
            buf[0] = kind;
            WriteU32(buf, 1, cumAck);
            return buf;
        }

        static byte[] Slice(byte[] src, int start)
        {
            int len = src.Length - start;
            var dst = new byte[len < 0 ? 0 : len];
            if (len > 0) Buffer.BlockCopy(src, start, dst, 0, len);
            return dst;
        }

        // Explicit little-endian (both ends run identical code, but don't rely on host-endian).
        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF); b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF); b[o + 3] = (byte)((v >> 24) & 0xFF);
        }
        static uint ReadU32(byte[] b, int o)
            => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        static ulong SafeEncode(IPEndPoint ep)
        {
            if (ep == null || ep.AddressFamily != AddressFamily.InterNetwork) return 0;
            try { return NetEndpoint.Encode(ep); } catch { return 0; }
        }

        void DrainErrors()
        {
            while (_rxErrors.TryDequeue(out var msg)) Debug.LogWarning("DirectIpTransport rx: " + msg);
        }
    }
}
