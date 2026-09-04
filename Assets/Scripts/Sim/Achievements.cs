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
            // ---- Trickshot Cup (design 9.7). Every one reads SP + MP together: a solo champion
            // and a multiplayer champion are both champions. The counters are moved by
            // CareerStats.RecordCup* through the CupCareer facade, which calls CheckAll after each.
            new AchievementDef
            {
                Id = "cup_champion", Title = CupText.AchChampion,
                Description = "Win a Trickshot Cup.",
                Kind = AchievementKind.StatThreshold, Target = 1,
                CurrentValue = d => d.SP.CupsWon + d.MP.CupsWon,
            },
            new AchievementDef
            {
                Id = "cup_giant_killer", Title = CupText.AchGiantKiller,
                Description = "Knock out a nation " + CupTuning.GiantKillerMargin + "+ strength above yours.",
                Kind = AchievementKind.StatThreshold, Target = 1,
                CurrentValue = d => d.SP.CupGiantKills + d.MP.CupGiantKills,
            },
            new AchievementDef
            {
                Id = "cup_clean_sheet", Title = CupText.AchCleanSheet,
                Description = "Win a cup round without conceding a kick.",
                Kind = AchievementKind.StatThreshold, Target = 1,
                CurrentValue = d => d.SP.CupCleanSheets + d.MP.CupCleanSheets,
            },
            new AchievementDef
            {
                Id = "cup_cold_blooded", Title = CupText.AchColdBlooded,
                Description = "Win a cup round in sudden death.",
                Kind = AchievementKind.StatThreshold, Target = 1,
                CurrentValue = d => d.SP.CupSuddenDeathWins + d.MP.CupSuddenDeathWins,
            },
            new AchievementDef
            {
                Id = "cup_team_player", Title = CupText.AchTeamPlayer,
                Description = "Win a Co-op Trickshot Cup.",
                Kind = AchievementKind.StatThreshold, Target = 1,
                CurrentValue = d => d.SP.CupCoopWins + d.MP.CupCoopWins,
            },
            new AchievementDef
            {
                Id = "cup_pundit", Title = CupText.AchPundit,
                Description = "Call " + CupTuning.PunditCalls + " coin tosses right.",
                Kind = AchievementKind.StatThreshold, Target = CupTuning.PunditCalls,
                CurrentValue = d => d.SP.CupCoinCallsRight + d.MP.CupCoinCallsRight,
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

        // Same atomic temp-file-then-swap save CareerStats.cs uses (a crash mid-write only
        // corrupts the temp file, never the real one), on the same worker thread - an unlock lands
        // mid-play, right after the stats save that triggered it.
        static void Save() => AtomicFileWriter.Write(FilePath, JsonUtility.ToJson(Unlocked, true), "Achievements");

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
