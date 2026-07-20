using UnityEngine;

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

        // When true (default), a real cross-machine game uses the direct-IP UDP transport.
        // Set false to force the in-process loopback transport for single-machine testing.
        public static bool UseDirectIp = true;

        // Pick the transport: Steam if built with it (TRICKSHOT_STEAM); else direct-IP UDP for
        // real LAN/Tailscale play; else the in-process loopback (single-machine testing). All
        // three are INetTransport siblings, so nothing else changes.
        static INetTransport NewTransport()
        {
            if (SteamTransport.Available) return new SteamTransport();
            if (UseDirectIp) return new DirectIpTransport();
            return new LocalTransport();
        }

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

        // Safety net: guarantee the transport (its UDP socket + background receive thread) is
        // torn down when the app quits or the editor stops Play, even if some path forgot to
        // call End(). Without this the socket can stay bound and a zombie thread survives the
        // next Editor Play session (the DirectIpTransport pitfall). Registered once at startup.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InstallQuitGuard()
        {
            Application.quitting -= End;   // idempotent across domain reloads
            Application.quitting += End;
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
