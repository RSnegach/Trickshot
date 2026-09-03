using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// One Steam friend, as the Friends tab and the invite panel want to show them.
    /// </summary>
    public struct SteamFriendInfo
    {
        public ulong steamId;
        public string name;
        public bool online;
        public bool playingTrickshot;   // playing this game right now (rich presence)
    }

    /// <summary>
    /// Steam friends-list + invite. Mirrors SteamTransport.cs's pattern: the real shape is
    /// implemented so the UI already targets it, and the actual Steamworks calls are written out
    /// behind TRICKSHOT_STEAM. It compiles and runs WITHOUT the Steamworks SDK - the friends list
    /// comes back empty and Invite refuses - so nothing here can fake a friend or claim an invite
    /// that never left the machine.
    ///
    /// TO GO LIVE: import a wrapper (Steamworks.NET or Facepunch.Steamworks), put the real appid
    /// in steam_appid.txt, define TRICKSHOT_STEAM, set AppId below, and uncomment the guarded
    /// bodies. Nothing outside this file and SteamTransport.cs needs to change. See MULTIPLAYER.md.
    /// </summary>
    public static class SteamFriendsAPI
    {
        /// <summary>
        /// This game's Steam AppID. Used to tell a friend playing TRICKSHOT from one in some other
        /// game (GetFriendGamePlayed reports whatever they are in). Placeholder until the app is
        /// registered - 480 is Valve's public "Spacewar" test app, which is what a pre-release
        /// build can legitimately develop against.
        /// </summary>
        public const uint AppId = 480;

        public static bool Available => SteamTransport.Available;

        /// <summary>
        /// The lobby an invite would be addressed to, or 0 if there is none. Read live off the
        /// active transport rather than cached, so it cannot outlive the session it names.
        /// </summary>
        public static ulong CurrentLobbyId
        {
            get
            {
                var s = Multiplayer.Session;
                if (s == null || !s.Active) return 0;
                return s.Transport is SteamTransport st ? st.LobbyId : 0;
            }
        }

        /// <summary>
        /// True when an invite can actually be sent right now: Steam is linked AND a lobby exists.
        /// The two are separate failures with separate fixes - Steam not running, versus a lobby
        /// that has not finished being created - so the UI reports them separately and neither is
        /// ever shown as a generic "can't invite".
        /// </summary>
        public static bool CanInvite => Available && CurrentLobbyId != 0;

        /// <summary>
        /// Async by shape (the real API needs a Steam callback round-trip) even though the stub
        /// answers synchronously. ALWAYS invokes onResult, even with an empty list, so a caller's
        /// "Loading..." state always resolves rather than hanging forever.
        /// </summary>
        public static void RequestFriendsList(Action<List<SteamFriendInfo>> onResult)
        {
            var list = new List<SteamFriendInfo>();
#if TRICKSHOT_STEAM
            // TODO(steam): uncomment with the wrapper imported.
            // int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            // for (int i = 0; i < n; i++)
            // {
            //     var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            //     var state = SteamFriends.GetFriendPersonaState(id);
            //     bool playing = SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t g)
            //                    && g.m_gameID.AppID() == (AppId_t)AppId;
            //     list.Add(new SteamFriendInfo {
            //         steamId          = id.m_SteamID,
            //         name             = SteamFriends.GetFriendPersonaName(id),
            //         online           = state != EPersonaState.k_EPersonaStateOffline,
            //         playingTrickshot = playing,
            //     });
            // }
#else
            Debug.Log("SteamFriendsAPI: built without TRICKSHOT_STEAM; RequestFriendsList returns empty. See MULTIPLAYER.md.");
#endif
            onResult?.Invoke(list);
        }

        /// <summary>
        /// Send ONE friend a Steam invite to the current lobby - the real notification, the one
        /// they can click to join. This is the direct call, not the overlay: no Steam UI opens on
        /// the sender's screen, which is what lets the in-game friend list own the interaction.
        ///
        /// Returns false when the invite could not be sent, and the caller SHOWS that per row -
        /// Steam's own send can fail (the friend blocked invites, the lobby filled, the id was
        /// stale), and an invite silently failing looks identical to one that was ignored.
        /// </summary>
        public static bool InviteToLobby(ulong friendSteamId)
        {
            if (friendSteamId == 0) return false;
            ulong lobby = CurrentLobbyId;
            if (lobby == 0)
            {
                Debug.LogWarning("SteamFriendsAPI: no lobby to invite to (host a session first).");
                return false;
            }
#if TRICKSHOT_STEAM
            // TODO(steam): the whole feature is this one call.
            // return SteamMatchmaking.InviteUserToLobby(new CSteamID(lobby), new CSteamID(friendSteamId));
            return false;
#else
            Debug.LogWarning("SteamFriendsAPI: built without TRICKSHOT_STEAM; InviteToLobby is a no-op. See MULTIPLAYER.md.");
            return false;
#endif
        }

        /// <summary>
        /// Opens Steam's OWN overlay invite dialog for the current lobby. Kept as a fallback for
        /// players whose overlay they prefer, and because it is the only path that still works if
        /// the friends list itself cannot be read. No-op without a lobby.
        /// </summary>
        public static void OpenInviteDialog()
        {
            ulong lobby = CurrentLobbyId;
            if (lobby == 0) return;
#if TRICKSHOT_STEAM
            // TODO(steam): SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby));
#else
            Debug.LogWarning("SteamFriendsAPI: built without TRICKSHOT_STEAM; OpenInviteDialog is a no-op. See MULTIPLAYER.md.");
#endif
        }
    }
}
