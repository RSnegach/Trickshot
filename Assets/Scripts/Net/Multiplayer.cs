using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Global access point for the current network session. Single-player leaves Session
    /// null and everything runs locally as today; a networked match sets it up via Host()
    /// or Join(). The mode drivers (MatchGame / striker GameManager) check
    /// Multiplayer.Session: if null or host they run the authoritative sim; if a client they
    /// send input + apply snapshots. Both match and striker are networkable this way.
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
            End();   // never orphan a live session (its socket stays bound + rx thread runs)
            _hostLostFired = false; _hostGone = false;   // fresh session: allow the notice to fire again
            Session = new NetSession(NewTransport());
            Session.Host(maxPlayers);
            InstallPump();
        }

        public static void Join(ulong lobbyOrHost)
        {
            End();   // never orphan a live session (its socket stays bound + rx thread runs)
            _hostLostFired = false; _hostGone = false;   // fresh session: allow the notice to fire again
            Session = new NetSession(NewTransport());
            Session.JoinLobby(lobbyOrHost);
            InstallPump();
        }

        public static void End()
        {
            Session?.Leave();
            Session = null;
        }

        // A session-lifetime pump. The transport MUST be polled every frame for the whole time a
        // session is live, not just while a particular screen is up: DirectIpTransport advances its
        // own clock inside Poll(), so an unpolled window sends no keepalives and receives nothing,
        // and the peer on the other end drops us after PeerTimeout (5s). That used to happen for
        // real - opening Customize from the lobby destroyed the LobbyUI, which was the only thing
        // polling, so customizing for >5s killed the session (a client got dropped to the hub, a
        // host dropped every client). This object is created on Host/Join, marked
        // DontDestroyOnLoad, and polls until the session ends, so no screen transition can starve
        // the transport. The per-screen Poll calls that remain are harmless (Poll is idempotent).
        static NetPumpRunner _pump;

        // Set from the transport's Disconnected event (a CLIENT losing the host). Handled on the
        // main thread in the pump's Update, never inline, since the event can fire from inside Poll.
        static volatile bool _hostGone;

        static void InstallPump()
        {
            // A client watches for the host vanishing for the WHOLE session, not just in the lobby.
            if (Session != null && !Session.IsHost && Session.Transport != null)
                Session.Transport.Disconnected += OnTransportDisconnected;

            if (_pump != null) return;
            var go = new GameObject("NetSessionPump");
            Object.DontDestroyOnLoad(go);
            _pump = go.AddComponent<NetPumpRunner>();
        }

        static void OnTransportDisconnected() => _hostGone = true;

        /// <summary>Frame pump that lives for the whole session, independent of any UI screen.</summary>
        class NetPumpRunner : MonoBehaviour
        {
            void Update()
            {
                if (Session == null) { _pump = null; Destroy(gameObject); return; }
                Poll();
                // A CLIENT that lost the host has nothing left to do: the host owned the sim, there
                // is no host migration, and every remaining frame would render frozen puppets with
                // no snapshots. Only LobbyUI used to watch for this, so a host quitting MID-MATCH
                // left clients stuck in a dead match with no way out but the pause menu. The
                // Disconnected event (subscribed in InstallPump) sets _hostGone; act on it here, on
                // the main thread, one frame later. NOTE: a client host-TIMEOUT fires Disconnected
                // but deliberately leaves the transport running, so "!Session.Active" is NOT a
                // reliable signal - the event is.
                if (_hostGone) { _hostGone = false; HostLost(); }
            }
        }

        /// <summary>
        /// Raised on a CLIENT when the connection to the host is gone (host quit, or the transport
        /// timed the host out). GameBootstrap subscribes to tear the match down and return to the
        /// menu. Fired once per session; the session is ended immediately afterwards.
        /// </summary>
        public static event System.Action HostConnectionLost;

        static bool _hostLostFired;

        static void HostLost()
        {
            if (_hostLostFired) return;
            _hostLostFired = true;

            // A client that never got an AssignSlot was never IN a session, so this is a FAILED
            // CONNECT, not a lost host, and it must NOT raise the event. The handler is
            // GameBootstrap.OnHostConnectionLost, which calls DestroyNetworkedUI() and destroys the
            // SessionBrowserUI outright - and that browser is the screen currently mid-connect, the
            // only thing that knows WHY the join failed. An unreachable host (firewall dropping
            // inbound UDP 7777, Tailscale down, wrong 100.x, host left the lobby) sends nothing back,
            // so the transport drops it at PeerTimeout = 5s and lands here, which destroyed the
            // browser before its own 8s deadline could render the message naming those exact causes.
            // The player just got teleported to the main menu with no explanation at all. Instead:
            // drop the dead session quietly and let the browser observe Session == null and report it.
            bool wasInSession = Session == null || Session.SlotAnswered;

            var handler = HostConnectionLost;
            End();                 // drop our dead session (also clears the pump next frame)
            if (wasInSession) handler?.Invoke();   // let the game unwind the match/UI
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

        // Browse joinable lobbies without joining.
        //
        // Deliberately does NOT go through a live session's transport: browsing happens BEFORE there
        // is a session, and a client's transport is bound to one host anyway. Direct IP therefore runs
        // discovery as a standalone sweep (enumerate the tailnet, probe each peer, broadcast on the
        // LAN) on its own throwaway socket, which is also why it cannot collide with a host running on
        // this same machine on port 7777. Results arrive asynchronously, via BrowsePoll.
        //
        // Loopback still queries a transient transport, because there the "network" IS the static bus
        // and reading it is instant.
        public static void Browse(System.Action<System.Collections.Generic.List<LobbyInfo>> onResults)
        {
            if (SteamTransport.Available) { new SteamTransport().ListLobbies(onResults); return; }
            if (!UseDirectIp) { new LocalTransport().ListLobbies(onResults); return; }
            TailnetDiscovery.Sweep(onResults);
        }

        /// <summary>
        /// Pump discovery. The session browser calls this every frame while it is open, because a sweep
        /// finishes on a worker thread and its results have to be handed to the UI on the main thread.
        /// Harmless when nothing is sweeping.
        /// </summary>
        public static void BrowsePoll() => TailnetDiscovery.Poll();

        public static bool SteamLinked => SteamTransport.Available;

        // Pump the transport once per frame. Call from the active mode driver's Update (or a
        // dedicated pump object) while a session is live.
        public static void Poll() => Session?.Poll();
    }
}
