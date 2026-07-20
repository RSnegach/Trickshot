using System;
using System.Collections.Generic;

namespace Trickshot.Net
{
    /// <summary>
    /// In-process loopback transport so the multiplayer framework RUNS and is testable
    /// without any real networking. Transports register in a shared static bus keyed by
    /// peer id, so a HOST session and a CLIENT session created in the same process actually
    /// deliver messages to EACH OTHER (not just back to themselves). This lets the full
    /// host-authoritative handshake (Hello -> AssignSlot) and the input/snapshot loop run
    /// end to end for local testing. A real two-machine game swaps in SteamTransport; the
    /// game code is identical.
    ///
    /// Single-player simply hosts (one transport, peer 1); its self-addressed sends route
    /// back to itself through the same bus, which is correct.
    /// </summary>
    public class LocalTransport : INetTransport
    {
        // Shared bus: peer id -> the transport that owns it, so Send(to) can find the target.
        static readonly Dictionary<ulong, LocalTransport> Bus = new Dictionary<ulong, LocalTransport>();
        static ulong _nextPeer = 1;

        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public PeerId LocalPeer { get; private set; }
        public PeerId HostPeer { get; private set; }

        public event Action<PeerId> PeerJoined;
        public event Action<PeerId> PeerLeft;
        public event Action Connected;
        public event Action Disconnected;
        public event Action<PeerId, byte[]> MessageReceived;

        // Inbound (senderPeer, payload), delivered on Poll.
        readonly Queue<(PeerId from, byte[] data)> _inbox = new Queue<(PeerId, byte[])>();
        bool _pendingConnected;
        PeerId _pendingJoinNotify;   // host: a peer to announce as joined next Poll

        public void StartHost(int maxPlayers)
        {
            IsHost = true; IsRunning = true;
            LocalPeer = new PeerId(_nextPeer++);
            HostPeer = LocalPeer;
            Bus[LocalPeer.Value] = this;
        }

        public void Join(ulong lobbyOrHost)
        {
            IsHost = false; IsRunning = true;
            LocalPeer = new PeerId(_nextPeer++);
            Bus[LocalPeer.Value] = this;
            // Find the current host on the bus (single active host in a local session).
            HostPeer = PeerId.None;
            foreach (var kv in Bus) if (kv.Value.IsHost) { HostPeer = kv.Value.LocalPeer; break; }
            _pendingConnected = true;
            // Tell the host a peer joined (next host Poll).
            if (HostPeer.IsValid && Bus.TryGetValue(HostPeer.Value, out var host))
                host._pendingJoinNotify = LocalPeer;
        }

        public void Shutdown()
        {
            if (!IsRunning) return;
            IsRunning = false;
            Bus.Remove(LocalPeer.Value);
            _inbox.Clear();
            Disconnected?.Invoke();
        }

        public void Send(PeerId to, byte[] data, NetChannel channel)
        {
            if (Bus.TryGetValue(to.Value, out var t)) t._inbox.Enqueue((LocalPeer, data));
        }

        public void SendToAll(byte[] data, NetChannel channel)
        {
            foreach (var kv in Bus)
                if (kv.Value != this) kv.Value._inbox.Enqueue((LocalPeer, data));
            // (host talking to itself is unnecessary; snapshots it builds it already holds.)
        }

        public void Poll()
        {
            if (_pendingConnected) { _pendingConnected = false; Connected?.Invoke(); }
            if (_pendingJoinNotify.IsValid) { var p = _pendingJoinNotify; _pendingJoinNotify = PeerId.None; PeerJoined?.Invoke(p); }
            while (_inbox.Count > 0)
            {
                var (from, data) = _inbox.Dequeue();
                MessageReceived?.Invoke(from, data);
            }
        }
    }
}
