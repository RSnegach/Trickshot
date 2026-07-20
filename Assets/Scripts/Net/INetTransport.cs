using System;

namespace Trickshot.Net
{
    /// <summary>
    /// Transport-agnostic networking seam. The game talks only to this interface; the
    /// concrete transport (local loopback for testing now, Steam P2P later) is swapped in
    /// without the rest of the code changing. Host-authoritative: the host runs the sim,
    /// clients send inputs and receive snapshots.
    ///
    /// Channels: reliable (lobby/assignment/score events - must arrive, ordered) and
    /// unreliable (per-tick input + snapshots - newest wins, drops are fine).
    /// </summary>
    public enum NetChannel { Reliable = 0, Unreliable = 1 }

    // A peer is identified by an opaque ulong (a Steam ID once wired; a small int under the
    // loopback transport). 0 is reserved for "invalid/none".
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public readonly ulong Value;
        public PeerId(ulong v) { Value = v; }
        public bool IsValid => Value != 0;
        public bool Equals(PeerId o) => Value == o.Value;
        public override bool Equals(object o) => o is PeerId p && Equals(p);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString();
        public static readonly PeerId None = new PeerId(0);
    }

    public interface INetTransport
    {
        bool IsHost { get; }
        bool IsRunning { get; }
        PeerId LocalPeer { get; }
        // The host's peer id (from a client's perspective). PeerId.None until connected /
        // on the host itself. The session addresses its Hello + inputs to this.
        PeerId HostPeer { get; }

        // Fired on the HOST when a client connects / disconnects.
        event Action<PeerId> PeerJoined;
        event Action<PeerId> PeerLeft;
        // Fired on a CLIENT when it successfully connects to / loses the host.
        event Action Connected;
        event Action Disconnected;
        // Every inbound payload (already reassembled) with its sender.
        event Action<PeerId, byte[]> MessageReceived;

        // Host a session (create a lobby). maxPlayers includes the host.
        void StartHost(int maxPlayers);
        // Join a session by its lobby/host handle (a Steam lobby id once wired; the loopback
        // transport accepts a matching host token).
        void Join(ulong lobbyOrHost);
        void Shutdown();

        // Send to one peer, or to everyone (SendToAll). Host->clients and client->host.
        void Send(PeerId to, byte[] data, NetChannel channel);
        void SendToAll(byte[] data, NetChannel channel);

        // Pump the transport (poll incoming, service callbacks). Called once per frame.
        void Poll();
    }
}
