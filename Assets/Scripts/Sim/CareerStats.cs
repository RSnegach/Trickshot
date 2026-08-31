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

        // ---- Free Kick / Penalty ----
        public int FreeKickAttempts;
        public int FreeKickGoals;

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

    /// <summary>One playlist's ranked record: an ELO-style MMR that moves on every result,
    /// starting every player at the same baseline. Never written by Friendlies or single-player.</summary>
    [Serializable]
    public class RankData
    {
        public int MatchesPlayed, Wins, Losses, Draws;
        public float Mmr = 1000f;
    }

    /// <summary>Online (ranked drop-in) only, one rank PER PLAYLIST - exactly like Rocket League
    /// tracks 1v1/2v2/3v3 separately, a strong 11v11 record says nothing about a player's 3v3
    /// record. Kept structurally separate from ModeStats/SP/MP entirely (not nested inside them),
    /// since rank must never be touched by anything but a ranked match, and a sibling field is
    /// the cheapest way to guarantee that (no per-call flag to get wrong).</summary>
    [Serializable]
    public class OnlineRanks
    {
        public RankData ThreeVThree = new RankData();
        public RankData FiveVFive = new RankData();
        public RankData ElevenVEleven = new RankData();
    }

    /// <summary>Lifetime stats split by origin: SP (single-player) and MP (networked). Every
    /// mode's stats live in both bags - an MP bag stays at zero for any mode that has no
    /// networked recording yet, which is expected, not a bug. Rank is separate - see OnlineRanks.</summary>
    [Serializable]
    public class CareerStatsData
    {
        public ModeStats SP = new ModeStats();
        public ModeStats MP = new ModeStats();
        public OnlineRanks Rank = new OnlineRanks();
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
            _data.Rank ??= new OnlineRanks();
            _data.Rank.ThreeVThree ??= new RankData();
            _data.Rank.FiveVFive ??= new RankData();
            _data.Rank.ElevenVEleven ??= new RankData();
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

        // ---- Free Kick / Penalty (single-player only) ----
        public static void RecordFreeKickAttempt() { Data.SP.FreeKickAttempts++; Save(); }
        public static void RecordFreeKickGoal() { Data.SP.FreeKickGoals++; Save(); }

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

        /// <summary>The playlist's rank bucket for a given team size (3/5/11 a side). Anything
        /// else falls back to 3v3 rather than throwing - a caller passing a bad perSide should
        /// not crash a match-end hook.</summary>
        public static RankData RankFor(int perSide) => perSide switch
        {
            5 => Data.Rank.FiveVFive,
            11 => Data.Rank.ElevenVEleven,
            _ => Data.Rank.ThreeVThree,
        };

        /// <summary>
        /// Online (ranked drop-in) only - call in ADDITION to RecordMatchEnd, never in place of
        /// it, so a ranked match's ordinary lifetime Match stats still count too. result: +1 win,
        /// 0 draw, -1 loss. An ELO-style update, like Rocket League's MMR: everyone starts at the
        /// same baseline (RankData.Mmr's 1000 default) and moves by how much the result beat or
        /// missed what was EXPECTED given the two sides' ratings, not by a fixed amount per win.
        ///
        /// opponentAvgMmr is optional because the wire doesn't carry a slot's MMR yet (nothing
        /// broadcasts it the way appearance/jersey already do) - with none given this falls back
        /// to an even-matchup assumption (expected = 0.5), which still moves the number up on a
        /// win and down on a loss, just without weighing how the actual opponents compared. Once
        /// MMR is synced the same way, pass the real opposing average here and this becomes a
        /// genuine Elo update with no other change.
        /// </summary>
        public static void RecordRankedMatch(int perSide, int result, float? opponentAvgMmr = null)
        {
            var r = RankFor(perSide);
            r.MatchesPlayed++;
            if (result > 0) r.Wins++;
            else if (result < 0) r.Losses++;
            else r.Draws++;

            float actual = result > 0 ? 1f : result < 0 ? 0f : 0.5f;
            float opp = opponentAvgMmr ?? r.Mmr;
            float expected = 1f / (1f + Mathf.Pow(10f, (opp - r.Mmr) / 400f));
            const float K = 32f;
            r.Mmr += K * (actual - expected);
            Save();
        }

        // Division names by MMR, tuned around the 1000 baseline. Simple, visible thresholds -
        // easy to retune.
        static readonly (float min, string name)[] RankTiers =
        {
            (1500f, "Champion"), (1300f, "Elite"), (1150f, "Gold"), (1000f, "Silver"), (0f, "Bronze"),
        };

        /// <summary>"Unranked" with no games played yet, else the tier name for the current MMR.</summary>
        public static string RankTierName(RankData r)
        {
            if (r.MatchesPlayed <= 0) return "Unranked";
            foreach (var (min, name) in RankTiers) if (r.Mmr >= min) return name;
            return "Bronze";
        }
    }
}
