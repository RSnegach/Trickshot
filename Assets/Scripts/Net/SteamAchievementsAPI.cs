using UnityEngine;

namespace Trickshot.Net
{
    /// <summary>
    /// Steamworks achievement-unlock STUB. Mirrors SteamTransport.cs's pattern: Achievements.cs
    /// (Assets/Scripts/Sim/Achievements.cs) already calls Unlock() the moment it detects a local
    /// threshold crossed, so the game's own achievement logic and persistence work with or
    /// without Steam - this is purely the "also tell Steam" side effect, gated behind
    /// TRICKSHOT_STEAM and left as a TODO until a wrapper is wired. See MULTIPLAYER.md.
    ///
    /// apiName must match the "API Name" configured for this achievement in the Steamworks
    /// partner site exactly (Achievements.cs's AchievementDef.Id is written to double as this).
    /// </summary>
    public static class SteamAchievementsAPI
    {
        public static bool Available => SteamTransport.Available;

        public static void Unlock(string apiName)
        {
#if TRICKSHOT_STEAM
            // TODO(steam): SteamUserStats.SetAchievement(apiName); SteamUserStats.StoreStats();
#else
            Debug.Log("SteamAchievementsAPI: built without TRICKSHOT_STEAM; Unlock('" + apiName + "') is a local-only no-op.");
#endif
        }
    }
}
