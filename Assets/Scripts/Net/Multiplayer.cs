namespace Trickshot.Net
{
    /// <summary>
    /// Global access point for the current network session. Single-player leaves Session
    /// null and everything runs locally as today; a networked match sets it up via Host()
    /// or Join(). The mode drivers (ScrimmageGame / striker GameManager) check
    /// Multiplayer.Session: if null or host they run the authoritative sim; if a client they
    /// send input + apply snapshots. Both scrimmage and striker are networkable this way.
    /// </summary>
    public static class Multiplayer
    {
        public static NetSession Session { get; private set; }

        public static bool IsActive => Session != null && Session.Active;
        public static bool IsHost => Session != null && Session.IsHost;
        public static bool IsClient => IsActive && !IsHost;

        // Pick the best available transport: Steam if built with it, else local loopback.
        static INetTransport NewTransport()
            => SteamTransport.Available ? (INetTransport)new SteamTransport() : new LocalTransport();

        public static void Host(int maxPlayers)
        {
            Session = new NetSession(NewTransport());
            Session.Host(maxPlayers);
        }

        public static void Join(ulong lobbyOrHost)
        {
            Session = new NetSession(NewTransport());
            Session.JoinLobby(lobbyOrHost);
        }

        public static void End()
        {
            Session?.Leave();
            Session = null;
        }

        // Browse joinable lobbies without joining. Uses a transient transport instance to
        // query (loopback reads the shared bus; Steam issues RequestLobbyList).
        public static void Browse(System.Action<System.Collections.Generic.List<LobbyInfo>> onResults)
            => NewTransport().ListLobbies(onResults);

        public static bool SteamLinked => SteamTransport.Available;

        // Pump the transport once per frame. Call from the active mode driver's Update (or a
        // dedicated pump object) while a session is live.
        public static void Poll() => Session?.Poll();
    }
}
