using System;
using System.IO;
using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// One player's lifetime stat totals, one field per stat, grouped by mode. Plain data only -
    /// no logic, no formatting - so it serializes cleanly with JsonUtility and stays easy to
    /// extend. CareerStatsData holds two of these (SP and MP) rather than doubling every field,
    /// so an SP/MP total is one aggregation pass over one bag instead of a hand-maintained sum.
    /// </summary>
    [Serializable]
    public class ModeStats
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

        // ---- Match (was Scrimmage) ----
        public int MatchesPlayed;
        public int MatchWins;
        public int MatchLosses;
        public int MatchDraws;
        public int MatchGoals;
        public int MatchAssists;
        public int MatchShots;
        public int MatchTackles;
        public int MatchSaves;
        public int MatchConceded;
        public int MatchPasses;
        public int MatchPassesCompleted;
        public int MatchMOTM;   // times this player was Man of the Match
    }

    /// <summary>Lifetime stats split by origin: SP (single-player) and MP (networked). Every
    /// mode's stats live in both bags - an MP bag stays at zero for any mode that has no
    /// networked recording yet, which is expected, not a bug.</summary>
    [Serializable]
    public class CareerStatsData
    {
        public ModeStats SP = new ModeStats();
        public ModeStats MP = new ModeStats();
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
            // JsonUtility leaves a missing/renamed field at its type default rather than erroring,
            // so an old flat pre-SP/MP save (or one missing either bag for any other reason) just
            // loads both bags as fresh zeros - no explicit migration needed.
            _data.SP ??= new ModeStats();
            _data.MP ??= new ModeStats();
        }

        /// <summary>
        /// Eager save, same convention Keybinds/QuickChat already use for their PlayerPrefs
        /// writes: every Record* call below saves immediately rather than batching. Nothing here
        /// fires faster than about once a second, and the file is small, so a rewrite per event
        /// costs nothing. Written to a temp file first and swapped in - a crash mid-write only
        /// corrupts the temp file, never the real one, so lifetime stats can't be wiped by a kill
        /// at the wrong instant.
        /// </summary>
        public static void Save()
        {
            string tmp = FilePath + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e) { Debug.LogWarning("CareerStats: failed to save. " + e.Message); }
        }

        /// <summary>Wipes every lifetime stat back to zero (both SP and MP). Callers must confirm
        /// with the player first - this is the "Reset All Stats" button's target, gated behind an
        /// are-you-sure.</summary>
        public static void ResetAll()
        {
            _data = new CareerStatsData();
            Save();
        }

        // ---- Striker (single-player only) ----
        public static void RecordStrikerGoal(bool trick)
        {
            Data.SP.StrikerGoals++;
            if (trick) Data.SP.StrikerTrickGoals++;
            Save();
        }
        public static void RecordStrikerCross() { Data.SP.StrikerCrosses++; Save(); }
        public static void RecordStrikerShotDenied() { Data.SP.StrikerShotsDenied++; Save(); }

        // ---- Goalkeeper (single-player only) ----
        public static void RecordKeeperSave() { Data.SP.KeeperSaves++; Save(); }
        public static void RecordKeeperGoalConceded() { Data.SP.KeeperGoalsConceded++; Save(); }
        public static void RecordKeeperShotFaced() { Data.SP.KeeperShotsFaced++; Save(); }

        // ---- Accuracy (single-player only) ----
        public static void RecordAccuracyKick() { Data.SP.AccuracyKicks++; Save(); }
        public static void RecordAccuracyTargetHit() { Data.SP.AccuracyTargetsHit++; Save(); }
        public static void RecordAccuracyRoundEnd(int score)
        {
            Data.SP.AccuracyRoundsPlayed++;
            Data.SP.AccuracyTotalScore += score;
            if (score > Data.SP.AccuracyBestScore) Data.SP.AccuracyBestScore = score;
            Save();
        }

        // ---- Time Trial (single-player only) ----
        public static void RecordTimeTrialCross() { Data.SP.TimeTrialCrosses++; Save(); }
        public static void RecordTimeTrialGoal() { Data.SP.TimeTrialGoals++; Save(); }
        public static void RecordTimeTrialRunEnd(int goals)
        {
            Data.SP.TimeTrialRunsPlayed++;
            if (goals > Data.SP.TimeTrialBestRunGoals) Data.SP.TimeTrialBestRunGoals = goals;
            Save();
        }

        // ---- Free Kick / Penalty (single-player only) ----
        public static void RecordFreeKickAttempt() { Data.SP.FreeKickAttempts++; Save(); }
        public static void RecordFreeKickGoal() { Data.SP.FreeKickGoals++; Save(); }

        // ---- Freeplay (single-player only) ----
        public static void RecordFreeplayCross() { Data.SP.FreeplayCrosses++; Save(); }
        public static void RecordFreeplayGoal() { Data.SP.FreeplayGoals++; Save(); }

        // ---- Match (SP and, from the host's own side, MP) ----
        // result: +1 win, 0 draw, -1 loss (from the local human's own side).
        public static void RecordMatchEnd(bool networked, int result, int goals, int assists,
                                           int shots, int tackles, int saves, int conceded,
                                           int passes, int passesCompleted, bool motm)
        {
            var d = networked ? Data.MP : Data.SP;
            d.MatchesPlayed++;
            if (result > 0) d.MatchWins++;
            else if (result < 0) d.MatchLosses++;
            else d.MatchDraws++;
            d.MatchGoals += goals;
            d.MatchAssists += assists;
            d.MatchShots += shots;
            d.MatchTackles += tackles;
            d.MatchSaves += saves;
            d.MatchConceded += conceded;
            d.MatchPasses += passes;
            d.MatchPassesCompleted += passesCompleted;
            if (motm) d.MatchMOTM++;
            Save();
        }
    }
}
