using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Trickshot.Net;

namespace Trickshot
{
    /// <summary>
    /// What an achievement is checked against. StatThreshold reads straight off CareerStats.Data
    /// and can be evaluated locally, right now. LeaderboardTop cannot be evaluated at all yet -
    /// "finish top N" is a comparison against every OTHER player's score, which needs Challenges
    /// mode's leaderboards to exist (they don't - still a DESIGN_NOTES.md idea) AND Steam
    /// Leaderboards to be wired to actually know a global rank. It's modelled here so the shape
    /// exists, but CheckAll() below never evaluates it - there is nothing to check yet.
    /// </summary>
    public enum AchievementKind { StatThreshold, LeaderboardTop }

    public class AchievementDef
    {
        // Stable id - also doubles as the Steam achievement "API Name" once Steamworks is wired
        // (SteamAchievementsAPI.Unlock(Id)), so this must never be renamed once shipped.
        public string Id;
        public string Title;
        public string Description;
        public AchievementKind Kind;
        public int Target;                          // StatThreshold: the count to reach
        public Func<CareerStatsData, int> CurrentValue;   // StatThreshold only
    }

    /// <summary>
    /// Local achievement definitions + unlock tracking. Deliberately small and hand-written - a
    /// handful of milestones now, more added the same way later (this is explicitly meant to be
    /// populated over time, not a closed list). Every StatThreshold achievement is checkable today
    /// off CareerStats alone, with or without Steam: SteamAchievementsAPI.Unlock is a side effect
    /// of a real local unlock, never the source of truth for one, so achievements work the same in
    /// a build with no Steamworks SDK at all.
    /// </summary>
    public static class Achievements
    {
        public static readonly AchievementDef[] All =
        {
            new AchievementDef
            {
                Id = "ACH_ONLINE_GOALS_100", Title = "Sharpshooter",
                Description = "Score 100 goals in Online matches.",
                Kind = AchievementKind.StatThreshold, Target = 100,
                CurrentValue = d => d.OnlineGoals,
            },
            new AchievementDef
            {
                Id = "ACH_ONLINE_WINS_10", Title = "Competitor",
                Description = "Win 10 Online matches.",
                Kind = AchievementKind.StatThreshold, Target = 10,
                CurrentValue = d => CareerStats.TotalOnlineWins(),
            },
            // Leaderboard-placement achievements (e.g. "finish top 10 in a Challenge") go here
            // once Challenges mode + its leaderboards exist - see the AchievementKind doc above.
        };

        const string FileName = "achievements.json";
        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        [Serializable] class UnlockedSet { public List<string> ids = new List<string>(); }
        static UnlockedSet _unlocked;

        static UnlockedSet Unlocked
        {
            get { if (_unlocked == null) Load(); return _unlocked; }
        }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    _unlocked = JsonUtility.FromJson<UnlockedSet>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Achievements: failed to load save file, starting fresh. " + e.Message);
            }
            _unlocked ??= new UnlockedSet();
            _unlocked.ids ??= new List<string>();
        }

        // Same atomic temp-file-then-swap save CareerStats.cs uses - a crash mid-write only
        // corrupts the temp file, never the real one.
        static void Save()
        {
            string tmp = FilePath + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonUtility.ToJson(Unlocked, true));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("Achievements: failed to save. " + e.Message); }
        }

        public static bool IsUnlocked(string id) => Unlocked.ids.Contains(id);

        /// <summary>
        /// Checks every StatThreshold achievement against CareerStats.Data and unlocks any newly
        /// crossed one - cheap (a handful of int comparisons), safe to call after every match.
        /// Call right after whatever CareerStats.Record* call could have moved the stat it reads
        /// (there's no event/callback on CareerStats to hook instead - this project's convention
        /// is an explicit call at each site that changes relevant state, same as every other
        /// CareerStats consumer).
        /// </summary>
        public static void CheckAll()
        {
            var data = CareerStats.Data;
            foreach (var a in All)
            {
                if (a.Kind != AchievementKind.StatThreshold) continue;
                if (IsUnlocked(a.Id)) continue;
                if (a.CurrentValue(data) < a.Target) continue;

                Unlocked.ids.Add(a.Id);
                Save();
                SteamAchievementsAPI.Unlock(a.Id);
                NotificationToastUI.Show(a.Title, a.Description);
            }
        }
    }
}
