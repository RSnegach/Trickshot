using System;
using System.IO;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Lifetime stat totals, one field per stat, grouped by mode. Plain data only - no logic,
    /// no formatting - so it serializes cleanly with JsonUtility and stays easy to extend.
    /// </summary>
    [Serializable]
    public class CareerStatsData
    {
        // ---- Striker ----
        public int StrikerGoals;
        public int StrikerTrickGoals;
        public int StrikerCrosses;
        public int StrikerShotsDenied;   // shot stopped by the AI keeper

        // ---- Goalkeeper ----
        public int KeeperSaves;
        public int KeeperGoalsConceded;
        public int KeeperShotsFaced;

        // ---- Accuracy ----
        public int AccuracyRoundsPlayed;
        public int AccuracyKicks;
        public int AccuracyTargetsHit;
        public int AccuracyBestScore;
        public long AccuracyTotalScore;

        // ---- Time Trial ----
        public int TimeTrialRunsPlayed;
        public int TimeTrialCrosses;
        public int TimeTrialGoals;
        public int TimeTrialBestRunGoals;

        // ---- Free Kick / Penalty ----
        public int FreeKickAttempts;
        public int FreeKickGoals;

        // ---- Freeplay ----
        public int FreeplayCrosses;
        public int FreeplayGoals;

        // ---- Scrimmage (single-player; networked scrimmage is not tracked yet) ----
        public int ScrimmageMatchesPlayed;
        public int ScrimmageWins;
        public int ScrimmageLosses;
        public int ScrimmageDraws;
        public int ScrimmageGoals;
        public int ScrimmageAssists;
        public int ScrimmageShots;
        public int ScrimmageTackles;
        public int ScrimmageSaves;
        public int ScrimmageConceded;
        public int ScrimmagePasses;
        public int ScrimmagePassesCompleted;
    }

    /// <summary>
    /// Lifetime player stats, persisted locally as JSON. This project's first file-based save -
    /// every other bit of state (PlayerProfile, SkillTree) is in-memory only and resets on
    /// relaunch; the only prior persistence anywhere is 4 raw-PlayerPrefs sites (Keybinds, Audio,
    /// Display, QuickChat), none of which fit a stats object this wide.
    ///
    /// Every caller goes through Data/Record*/ResetAll only, never PlayerPrefs or file APIs
    /// directly - so swapping Load/Save's insides for a future player-account backend is the
    /// only change that backend would ever need; nothing else in the game touches storage.
    /// </summary>
    public static class CareerStats
    {
        const string FileName = "career_stats.json";
        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static CareerStatsData _data;

        /// <summary>Lazily loaded, like Keybinds.Current - first access reads the save file.</summary>
        public static CareerStatsData Data
        {
            get { if (_data == null) Load(); return _data; }
        }

        static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonUtility.FromJson<CareerStatsData>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("CareerStats: failed to load save file, starting fresh. " + e.Message);
            }
            _data ??= new CareerStatsData();
        }

        /// <summary>
        /// Eager save, same convention Keybinds/QuickChat already use for their PlayerPrefs
        /// writes: every Record* call below saves immediately rather than batching. Nothing here
        /// fires faster than about once a second, and the file is small, so a rewrite per event
        /// costs nothing and can never lose a stat to a crash.
        /// </summary>
        public static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true)); }
            catch (Exception e) { Debug.LogWarning("CareerStats: failed to save. " + e.Message); }
        }

        /// <summary>Wipes every lifetime stat back to zero. Callers must confirm with the player
        /// first - this is the "Reset All Stats" button's target, gated behind an are-you-sure.</summary>
        public static void ResetAll()
        {
            _data = new CareerStatsData();
            Save();
        }

        // ---- Striker ----
        public static void RecordStrikerGoal(bool trick)
        {
            Data.StrikerGoals++;
            if (trick) Data.StrikerTrickGoals++;
            Save();
        }
        public static void RecordStrikerCross() { Data.StrikerCrosses++; Save(); }
        public static void RecordStrikerShotDenied() { Data.StrikerShotsDenied++; Save(); }

        // ---- Goalkeeper ----
        public static void RecordKeeperSave() { Data.KeeperSaves++; Save(); }
        public static void RecordKeeperGoalConceded() { Data.KeeperGoalsConceded++; Save(); }
        public static void RecordKeeperShotFaced() { Data.KeeperShotsFaced++; Save(); }

        // ---- Accuracy ----
        public static void RecordAccuracyKick() { Data.AccuracyKicks++; Save(); }
        public static void RecordAccuracyTargetHit() { Data.AccuracyTargetsHit++; Save(); }
        public static void RecordAccuracyRoundEnd(int score)
        {
            Data.AccuracyRoundsPlayed++;
            Data.AccuracyTotalScore += score;
            if (score > Data.AccuracyBestScore) Data.AccuracyBestScore = score;
            Save();
        }

        // ---- Time Trial ----
        public static void RecordTimeTrialCross() { Data.TimeTrialCrosses++; Save(); }
        public static void RecordTimeTrialGoal() { Data.TimeTrialGoals++; Save(); }
        public static void RecordTimeTrialRunEnd(int goals)
        {
            Data.TimeTrialRunsPlayed++;
            if (goals > Data.TimeTrialBestRunGoals) Data.TimeTrialBestRunGoals = goals;
            Save();
        }

        // ---- Free Kick / Penalty ----
        public static void RecordFreeKickAttempt() { Data.FreeKickAttempts++; Save(); }
        public static void RecordFreeKickGoal() { Data.FreeKickGoals++; Save(); }

        // ---- Freeplay ----
        public static void RecordFreeplayCross() { Data.FreeplayCrosses++; Save(); }
        public static void RecordFreeplayGoal() { Data.FreeplayGoals++; Save(); }

        // ---- Scrimmage ----
        // result: +1 win, 0 draw, -1 loss (from the local human's own side).
        public static void RecordScrimmageMatchEnd(int result, int goals, int assists, int shots,
                                                    int tackles, int saves, int conceded,
                                                    int passes, int passesCompleted)
        {
            Data.ScrimmageMatchesPlayed++;
            if (result > 0) Data.ScrimmageWins++;
            else if (result < 0) Data.ScrimmageLosses++;
            else Data.ScrimmageDraws++;
            Data.ScrimmageGoals += goals;
            Data.ScrimmageAssists += assists;
            Data.ScrimmageShots += shots;
            Data.ScrimmageTackles += tackles;
            Data.ScrimmageSaves += saves;
            Data.ScrimmageConceded += conceded;
            Data.ScrimmagePasses += passes;
            Data.ScrimmagePassesCompleted += passesCompleted;
            Save();
        }
    }
}
