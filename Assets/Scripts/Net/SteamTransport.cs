using System;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Steam P2P transport STUB. It implements INetTransport so the game already targets it,
    /// but the actual Steamworks calls are gated behind the TRICKSHOT_STEAM scripting define
    /// and left as clearly-marked TODOs. This compiles and runs WITHOUT the Steamworks SDK
    /// (it behaves as a no-op transport); once you add a wrapper (Steamworks.NET or
    /// Facepunch.Steamworks) and define TRICKSHOT_STEAM, fill in the marked sections.
    ///
    /// See MULTIPLAYER.md for the full wiring guide (appID, SDK import, lobby + P2P mapping).
    ///
    /// Intended mapping (host-authoritative, per the framework):
    ///   StartHost  -> SteamMatchmaking.CreateLobby(FriendsOnly/Public, maxPlayers)
    ///   Join       -> SteamMatchmaking.JoinLobby(lobbyId); connect P2P to the lobby owner
    ///   Send       -> SteamNetworkingMessages.SendMessageToUser(identity, data, flags, ch)
    ///   Poll       -> SteamNetworkingMessages.ReceiveMessagesOnChannel(...) + RunCallbacks
    ///   Peer ids   -> the CSteamID.m_SteamID ulong maps straight onto PeerId.Value
    /// </summary>
    public class SteamTransport : INetTransport
    {
        public bool IsHost { get; private set; }
        public bool IsRunning { get; private set; }
        public PeerId LocalPeer { get; private set; }
        // TODO(steam): set to the lobby owner's CSteamID on Join/LobbyEnter; = LocalPeer on host.
        public PeerId HostPeer { get; private set; }

        // Events satisfy INetTransport; without TRICKSHOT_STEAM they never fire (the guarded
        // code raises them once wired). Pragma silences the "event never used" warning.
#pragma warning disable 0067
        public event Action<PeerId> PeerJoined;
        public event Action<PeerId> PeerLeft;
        public event Action Connected;
        public event Action Disconnected;
        public event Action<PeerId, byte[]> MessageReceived;
#pragma warning restore 0067

        public static bool Available =>
#if TRICKSHOT_STEAM
            true;
#else
            false;
#endif

        public void StartHost(int maxPlayers)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers)
            //   - on LobbyCreated_t: store lobby id, LocalPeer = new PeerId(SteamUser.GetSteamID().m_SteamID)
            //   - on LobbyChatUpdate_t (member joined): raise PeerJoined(new PeerId(theirSteamId))
            IsHost = true; IsRunning = true;
#else
            Debug.LogWarning("SteamTransport: built without TRICKSHOT_STEAM; StartHost is a no-op. See MULTIPLAYER.md.");
#endif
        }

        public void Join(ulong lobbyOrHost)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamMatchmaking.JoinLobby(new CSteamID(lobbyOrHost))
            //   - on LobbyEnter_t: LocalPeer = SteamUser.GetSteamID(); open a P2P channel to
            //     the lobby owner; raise Connected() once the session is accepted.
            IsRunning = true;
#else
            Debug.LogWarning("SteamTransport: built without TRICKSHOT_STEAM; Join is a no-op. See MULTIPLAYER.md.");
#endif
        }

        public void Shutdown()
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamMatchmaking.LeaveLobby(lobby); close SteamNetworkingMessages
            //   sessions; raise Disconnected().
#endif
            IsRunning = false;
        }

        public void Send(PeerId to, byte[] data, NetChannel channel)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamNetworkingMessages.SendMessageToUser(
            //   new SteamNetworkingIdentity(new CSteamID(to.Value)), data, len,
            //   channel == NetChannel.Reliable ? k_nSteamNetworkingSend_Reliable
            //                                   : k_nSteamNetworkingSend_Unreliable, (int)channel);
#endif
        }

        public void SendToAll(byte[] data, NetChannel channel)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): iterate lobby members (SteamMatchmaking.GetLobbyMemberByIndex) and
            //   Send() to each (skip self).
#endif
        }

        public void Poll()
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamAPI.RunCallbacks(); then for each channel
            //   SteamNetworkingMessages.ReceiveMessagesOnChannel(ch, buf, max) and raise
            //   MessageReceived(new PeerId(msg.m_identityPeer...), bytes) per message.
#endif
        }

        public void ListLobbies(System.Action<System.Collections.Generic.List<LobbyInfo>> onResults)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamMatchmaking.RequestLobbyList(); on LobbyMatchList_t, loop
            //   GetLobbyByIndex, read GetLobbyData(name/mode) + GetNumLobbyMembers, build a
            //   List<LobbyInfo> (handle = lobby CSteamID.m_SteamID) and invoke onResults.
            onResults?.Invoke(new System.Collections.Generic.List<LobbyInfo>());
#else
            onResults?.Invoke(new System.Collections.Generic.List<LobbyInfo>());
#endif
        }
    }
}
