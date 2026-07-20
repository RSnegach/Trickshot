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
    /// The app payload delivered to MessageReceived is the SAME byte[] NetCodec produced, so
    /// NetReader / MsgType are untouched.
    /// </summary>
    public class DirectIpTransport : INetTransport
    {
        const byte FrameUnreliable = 0, FrameReliable = 1, FrameAck = 2, FramePing = 3;
        const float KeepaliveInterval = 1.0f;   // ping cadence
        const float PeerTimeout = 5.0f;          // no packet this long -> peer is gone

        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public PeerId LocalPeer { get; private set; }
        public PeerId HostPeer { get; private set; }

        public event Action<PeerId> PeerJoined;
        public event Action<PeerId> PeerLeft;
        public event Action Connected;
        public event Action Disconnected;
        public event Action<PeerId, byte[]> MessageReceived;

        UdpClient _udp;
        Thread _rxThread;
        volatile bool _running;

        // Background thread -> main thread. Only the rx thread enqueues; only Poll drains.
        readonly ConcurrentQueue<(IPEndPoint from, byte[] data)> _inbox = new ConcurrentQueue<(IPEndPoint, byte[])>();
        readonly ConcurrentQueue<string> _rxErrors = new ConcurrentQueue<string>();   // logged on main thread

        // Peer registry (main thread only). Keyed by the encoded ulong of the endpoint, which
        // is a perfect key for an IPv4:port pair and sidesteps IPEndPoint equality quirks.
        readonly Dictionary<ulong, PeerId> _peerByEp = new Dictionary<ulong, PeerId>();     // ep-handle -> peer
        readonly Dictionary<ulong, IPEndPoint> _epByPeer = new Dictionary<ulong, IPEndPoint>(); // peer.Value -> ep
        readonly Dictionary<ulong, ReliableChannel> _relByPeer = new Dictionary<ulong, ReliableChannel>();
        readonly Dictionary<ulong, float> _lastRecv = new Dictionary<ulong, float>();       // peer.Value -> time
        readonly Dictionary<ulong, float> _lastPing = new Dictionary<ulong, float>();       // peer.Value -> time

        ulong _nextPeer = 1;             // host = 1; clients get 2,3,...
        bool _pendingConnected;
        float _now;                      // main-thread clock, advanced in Poll

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
            try { _udp?.Close(); } catch { }         // unblocks the blocking Receive()
            try { _rxThread?.Join(200); } catch { }
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
                catch (SocketException) { if (!_running) break; }        // Close() during Receive
                catch (ObjectDisposedException) { break; }               // socket disposed
                catch (Exception e) { _rxErrors.Enqueue(e.Message); if (!_running) break; }
            }
        }

        // ---- send ----
        public void Send(PeerId to, byte[] data, NetChannel channel) => SendTo(to, data, channel);

        void SendTo(PeerId to, byte[] data, NetChannel channel)
        {
            if (!_running || !_epByPeer.TryGetValue(to.Value, out var ep)) return;
            if (channel == NetChannel.Reliable)
            {
                var rel = ChannelFor(to.Value);
                uint seq = rel.NextSeq();
                byte[] packet = FrameReliablePacket(seq, data);
                rel.Track(seq, packet, _now);
                RawSend(packet, ep);
            }
            else
            {
                RawSend(FrameUnreliablePacket(data), ep);
            }
        }

        public void SendToAll(byte[] data, NetChannel channel)
        {
            // Snapshot the peer list (handlers below may mutate it) and skip ourselves.
            var targets = new List<ulong>(_epByPeer.Keys);
            foreach (var pv in targets)
                if (pv != LocalPeer.Value) SendTo(new PeerId(pv), data, channel);
        }

        void RawSend(byte[] packet, IPEndPoint ep)
        {
            try { if (_running) _udp.Send(packet, packet.Length, ep); }
            catch (Exception e) { Debug.LogWarning("DirectIpTransport send failed: " + e.Message); }
        }

        // ---- main-thread pump ----
        public void Poll()
        {
            if (!_running) { DrainErrors(); return; }
            _now += Time.unscaledDeltaTime;

            if (_pendingConnected) { _pendingConnected = false; Connected?.Invoke(); }

            // 1) Deliver inbound packets (assign peers, fire events - all main thread).
            while (_inbox.TryDequeue(out var pkt))
                HandlePacket(pkt.from, pkt.data);

            // 2) Resend unacked reliable packets whose timer elapsed.
            foreach (var kv in _relByPeer)
            {
                if (!_epByPeer.TryGetValue(kv.Key, out var ep)) continue;
                foreach (var packet in kv.Value.DueResends(_now)) RawSend(packet, ep);
            }

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
                {
                    if (data.Length < 5) break;
                    uint seq = ReadU32(data, 1);
                    var rel = ChannelFor(peer.Value);
                    byte[] payload = Slice(data, 5);
                    foreach (var ready in rel.Receive(seq, payload))
                        MessageReceived?.Invoke(peer, ready);
                    // Ack whatever we've now delivered in order (releases the sender's resends).
                    if (_epByPeer.TryGetValue(peer.Value, out var ep)) RawSend(FrameAckPacket(rel.CumAck), ep);
                    break;
                }

                case FrameAck:
                {
                    if (data.Length < 5) break;
                    uint cumAck = ReadU32(data, 1);
                    ChannelFor(peer.Value).Ack(cumAck);
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
                DropPeer(pv);
                if (IsHost) PeerLeft?.Invoke(new PeerId(pv));
                else Disconnected?.Invoke();        // lost the host
            }
        }

        // ---- peer table ----
        void RegisterPeer(PeerId peer, IPEndPoint ep)
        {
            ulong epKey = SafeEncode(ep);
            if (epKey != 0) _peerByEp[epKey] = peer;
            _epByPeer[peer.Value] = ep;
            _lastRecv[peer.Value] = _now;
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
            _lastRecv.Remove(pv);
            _lastPing.Remove(pv);
        }

        ReliableChannel ChannelFor(ulong peerValue)
        {
            if (!_relByPeer.TryGetValue(peerValue, out var rel))
            {
                rel = new ReliableChannel();
                _relByPeer[peerValue] = rel;
            }
            return rel;
        }

        // ---- discovery (none for direct IP) ----
        public void ListLobbies(Action<List<LobbyInfo>> onResults)
        {
            // Direct IP has no discovery service: you join by typing the host's IP. Return an
            // empty list synchronously; SessionBrowserUI shows the join-by-IP field instead.
            onResults?.Invoke(new List<LobbyInfo>());
        }

        // ---- framing helpers ----
        static byte[] FrameUnreliablePacket(byte[] payload)
        {
            var buf = new byte[1 + (payload?.Length ?? 0)];
            buf[0] = FrameUnreliable;
            if (payload != null) Buffer.BlockCopy(payload, 0, buf, 1, payload.Length);
            return buf;
        }
        static byte[] FrameReliablePacket(uint seq, byte[] payload)
        {
            var buf = new byte[5 + (payload?.Length ?? 0)];
            buf[0] = FrameReliable;
            WriteU32(buf, 1, seq);
            if (payload != null) Buffer.BlockCopy(payload, 0, buf, 5, payload.Length);
            return buf;
        }
        static byte[] FrameAckPacket(uint cumAck)
        {
            var buf = new byte[5];
            buf[0] = FrameAck;
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
