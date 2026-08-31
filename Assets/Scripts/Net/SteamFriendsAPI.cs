using System;
using System.Collections.Generic;
using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// One Steam friend, as the Friends tab wants to show it.
    /// </summary>
    public struct SteamFriendInfo
    {
        public ulong steamId;
        public string name;
        public bool online;
        public bool playingTrickshot;   // playing this game right now (rich presence)
    }

    /// <summary>
    /// Steam friends-list + invite STUB. Mirrors SteamTransport.cs's pattern exactly: implements
    /// the real shape so FriendsPanelUI and LobbyUI already target it, but the actual Steamworks
    /// calls are gated behind TRICKSHOT_STEAM and left as clearly-marked TODOs. Compiles and runs
    /// without the Steamworks SDK (RequestFriendsList always returns empty; OpenInviteDialog is a
    /// no-op) - fill in the marked sections once a wrapper (Steamworks.NET or Facepunch.Steamworks)
    /// is added and TRICKSHOT_STEAM is defined. See MULTIPLAYER.md.
    ///
    /// Intended mapping:
    ///   RequestFriendsList -> SteamFriends.GetFriendCount(k_EFriendFlagImmediate) + GetFriendByIndex,
    ///     per friend: GetFriendPersonaName, GetFriendPersonaState (!= Offline -> online),
    ///     GetFriendGamePlayed (compare m_gameID.AppID() to our own AppId -> playingTrickshot).
    ///   OpenInviteDialog -> SteamFriends.ActivateGameOverlayInviteDialog(currentLobbySteamId) -
    ///     opens STEAM'S OWN native invite overlay for the active lobby. No custom friend-picker
    ///     UI is needed on our end at all; this call is the entire "invite to lobby" feature.
    /// </summary>
    public static class SteamFriendsAPI
    {
        public static bool Available => SteamTransport.Available;

        /// <summary>
        /// Async by shape (matches the real API, which requires a Steam callback round-trip) even
        /// though the stub answers synchronously. Always invoke onResult, even with an empty list,
        /// so a caller's "loading" state always resolves.
        /// </summary>
        public static void RequestFriendsList(Action<List<SteamFriendInfo>> onResult)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            //   for (int i = 0; i < n; i++) {
            //     var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
            //     var state = SteamFriends.GetFriendPersonaState(id);
            //     bool playing = SteamFriends.GetFriendGamePlayed(id, out var gameInfo)
            //                    && gameInfo.m_gameID.AppID() == (AppId_t)OUR_APP_ID;
            //     list.Add(new SteamFriendInfo { steamId = id.m_SteamID, name = SteamFriends.GetFriendPersonaName(id),
            //                                    online = state != EPersonaState.k_EPersonaStateOffline, playingTrickshot = playing });
            //   }
#else
            Debug.Log("SteamFriendsAPI: built without TRICKSHOT_STEAM; RequestFriendsList returns empty. See MULTIPLAYER.md.");
#endif
            onResult?.Invoke(new List<SteamFriendInfo>());
        }

        /// <summary>
        /// Opens Steam's own overlay invite dialog for the CURRENT lobby (no-op if there isn't
        /// one - only meaningful once Multiplayer.Session is an active host). Called from
        /// LobbyUI's "Invite Friends" button.
        /// </summary>
        public static void OpenInviteDialog()
        {
#if TRICKSHOT_STEAM
            // TODO(steam): if (Multiplayer.Session == null || !Multiplayer.Session.Active) return;
            //   SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(currentLobbySteamId));
#else
            Debug.LogWarning("SteamFriendsAPI: built without TRICKSHOT_STEAM; OpenInviteDialog is a no-op. See MULTIPLAYER.md.");
#endif
        }
    }
}
