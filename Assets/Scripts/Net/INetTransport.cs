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
    // ReliableBulk is a SECOND ordered stream (jersey PNGs) with its own sequence space, so a
    // 400-packet jersey burst that loses a chunk stalls only other jersey chunks, never the
    // gameplay messages on Reliable (goal callouts, roster, cross-map edits) queued behind it.
    public enum NetChannel { Reliable = 0, Unreliable = 1, ReliableBulk = 2 }

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

        // Request the list of joinable lobbies. Results arrive via the callback (async on
        // Steam; near-immediate on loopback). Each entry is (lobby handle, display label).
        void ListLobbies(Action<System.Collections.Generic.List<LobbyInfo>> onResults);

        // How this session describes itself to anything looking for a game. A HOST assigns a
        // function here (NetSession does it in Host()); the transport calls it to answer a
        // discovery probe, so the answer is always built from the LIVE session and can never be a
        // stale copy of the occupancy. Returning visible == false means "do not answer at all",
        // which is what makes a private lobby actually private rather than merely flagged.
        //
        // It belongs on the transport rather than beside the discovery code because it is per
        // transport: direct IP answers a UDP probe with it, Steam would push the same fields into
        // SetLobbyData, and the loopback transport ignores it.
        Func<LobbyAdvert> AdvertProvider { get; set; }
    }

    /// <summary>
    /// What a host publishes about itself so a browser can list it. Built fresh per request from the
    /// live session (NetSession.BuildAdvert) so a browser sees real occupancy, not a stale copy.
    ///
    /// It lives HERE, next to the interface member that consumes it, and not beside the UDP discovery
    /// code where it started. Two reasons. It is part of the INetTransport contract - direct IP answers
    /// a UDP probe with it, Steam would push the same fields into SetLobbyData - so gating out or
    /// deleting UDP discovery for a Steam build must not take the interface's own type with it. And it
    /// was declared in a file that is not committed, which meant three TRACKED files depended on a type
    /// no clean clone had.
    /// </summary>
    public struct LobbyAdvert
    {
        public bool visible;      // false = do not advertise at all (private lobby / not ready)
        public string name;       // host player name
        public string mode;       // short mode line, e.g. "Match 3v3"
        public int players;
        public int maxPlayers;
        // The build this host is running, so an incompatible one can be filtered out BEFORE a connect.
        // Steam can do that server-side with AddRequestLobbyListStringFilter; today a version mismatch
        // is only discovered after a full handshake, when the host answers Hello with a refusal.
        public string build;
    }

    // A discoverable session in the browser.
    public struct LobbyInfo
    {
        public ulong handle;    // pass to Join()
        public string name;     // host player name / lobby title
        public string mode;     // e.g. "Match 5v5"
        public int players;     // current members
        public int maxPlayers;
    }
}
