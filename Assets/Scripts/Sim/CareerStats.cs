using System;
using System.Collections.Generic;
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
        // Two BESTS, because beating a keeper is a different game from an open goal and one number
        // covering both would only ever show the open-goal run. AccuracyBestScore is the WITH-keeper
        // best; it keeps its old name so an existing save file's high score stays attached to the
        // harder mode (JsonUtility leaves the new field at 0, which is the right starting point).
        public int AccuracyRoundsPlayed;
        public int AccuracyKicks;
        public int AccuracyTargetsHit;
        public int AccuracyBestScore;         // with a keeper
        public int AccuracyBestScoreNoKeeper; // open goal
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

        // ---- Trickshot Cup (design 9.7) ----
        // SP = Solo; MP = Head to Head and Co-op. A "round" is one bracket match between two
        // nations; "kicks" are the player's own (Co-op: only the ones this player took), "saves"
        // and "conceded" the kicks this player kept against.
        public int CupsEntered;
        public int CupsWon;
        public int CupBestStage;         // furthest stage index reached + 1 (1 = R32 .. 5 = Final); 0 = never entered
        public int CupRoundsWon;
        public int CupRoundsLost;
        public int CupKicksScored;
        public int CupKicksTaken;
        public int CupSaves;
        public int CupConceded;
        public int CupCoinCallsMade;
        public int CupCoinCallsRight;
        // Achievement counters (Achievements.All reads these; see CupCareer for the rules).
        public int CupGiantKills;        // rounds won against a nation CupTuning.GiantKillerMargin+ stronger
        public int CupCleanSheets;       // rounds won conceding nothing
        public int CupSuddenDeathWins;   // rounds won in sudden death
        public int CupCoopWins;          // Co-op cups won (MP bag only in practice)
        // Per-nation entries and wins. A list of rows because JsonUtility cannot serialise a
        // dictionary; it loads empty on an older save, which is the right starting point.
        public List<NationCups> CupNations = new List<NationCups>();
    }

    /// <summary>One nation's cup record for a player: how often they entered with it, how often
    /// they won with it. Keyed by the nation's design NAME (the CupNationTable key), never by
    /// index, so a re-ordered table cannot move a record onto another flag.</summary>
    [Serializable]
    public class NationCups
    {
        public string Nation;
        public int Entered;
        public int Won;
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
            // A save written before the cup existed has no list at all (JsonUtility leaves a
            // missing List field null rather than empty).
            _data.SP.CupNations ??= new List<NationCups>();
            _data.MP.CupNations ??= new List<NationCups>();
        }

        /// <summary>
        /// Eager save: every Record* call below saves at once rather than batching, so nothing is
        /// lost to a crash. The serialisation is main-thread and cheap (a few KB of JSON); the disk
        /// write is NOT - it lands mid-play (every cross, save, goal and kick) and a synchronous
        /// temp-file-and-swap was a visible hitch each time. AtomicFileWriter does the write on a
        /// worker thread, keeps the temp-then-swap so a crash mid-write never corrupts the real
        /// file, coalesces a burst into one write, and is flushed at quit.
        /// </summary>
        public static void Save() => AtomicFileWriter.Write(FilePath, JsonUtility.ToJson(Data, true), "CareerStats");

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
        /// <summary>End of a scored accuracy run. `noKeeper` picks which of the two bests it can
        /// beat - an open-goal run never touches the with-keeper record.</summary>
        public static void RecordAccuracyRoundEnd(int score, bool noKeeper)
        {
            Data.SP.AccuracyRoundsPlayed++;
            Data.SP.AccuracyTotalScore += score;
            if (noKeeper)
            {
                if (score > Data.SP.AccuracyBestScoreNoKeeper) Data.SP.AccuracyBestScoreNoKeeper = score;
            }
            else if (score > Data.SP.AccuracyBestScore) Data.SP.AccuracyBestScore = score;
            Save();
        }

        /// <summary>The best that applies to a run of this kind - what its HUD shows and what it
        /// is trying to beat.</summary>
        public static int AccuracyBest(bool noKeeper) =>
            noKeeper ? Data.SP.AccuracyBestScoreNoKeeper : Data.SP.AccuracyBestScore;

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

        // ---- Trickshot Cup (SP = Solo, MP = Head to Head / Co-op) ----
        // Every method takes `mp` rather than reading Multiplayer.IsActive, because the cup's
        // director knows its style and a solo cup launched with a stale session must not land in
        // the MP bag. CupCareer is the facade the director calls; it adds the achievement check.

        static ModeStats Bag(bool mp) => mp ? Data.MP : Data.SP;

        /// <summary>The per-nation row, created on first use when `create` is set; else null.</summary>
        public static NationCups CupNationRow(ModeStats d, string nation, bool create)
        {
            if (d == null || string.IsNullOrEmpty(nation)) return null;
            d.CupNations ??= new List<NationCups>();
            for (int i = 0; i < d.CupNations.Count; i++)
                if (string.Equals(d.CupNations[i].Nation, nation, StringComparison.OrdinalIgnoreCase))
                    return d.CupNations[i];
            if (!create) return null;
            var row = new NationCups { Nation = nation };
            d.CupNations.Add(row);
            return row;
        }

        /// <summary>A cup started with this nation (the draw has been made).</summary>
        public static void RecordCupEntered(string nation, bool mp)
        {
            var d = Bag(mp);
            d.CupsEntered++;
            var row = CupNationRow(d, nation, create: true);
            if (row != null) row.Entered++;
            Save();
        }

        /// <summary>
        /// A round this player was part of has been decided. The three flags feed achievements
        /// and only count on a WIN: `suddenDeath` (decided past five kicks each), `cleanSheet`
        /// (the other side scored nothing), `giantKill` (the opponent was 30+ strength above).
        /// </summary>
        public static void RecordCupRound(bool won, bool mp, bool suddenDeath = false,
                                          bool cleanSheet = false, bool giantKill = false)
        {
            var d = Bag(mp);
            if (won)
            {
                d.CupRoundsWon++;
                if (suddenDeath) d.CupSuddenDeathWins++;
                if (cleanSheet) d.CupCleanSheets++;
                if (giantKill) d.CupGiantKills++;
            }
            else d.CupRoundsLost++;
            Save();
        }

        /// <summary>One kick this player took.</summary>
        public static void RecordCupKick(bool scored, bool mp)
        {
            var d = Bag(mp);
            d.CupKicksTaken++;
            if (scored) d.CupKicksScored++;
            Save();
        }

        /// <summary>One kick this player kept against and stopped (a wall stop counts: it reads SAVED).</summary>
        public static void RecordCupSave(bool mp) { Bag(mp).CupSaves++; Save(); }

        /// <summary>One kick this player kept against that went in.</summary>
        public static void RecordCupConceded(bool mp) { Bag(mp).CupConceded++; Save(); }

        /// <summary>One HEADS/TAILS call before a flip (every human present calls; design 6.11).</summary>
        public static void RecordCupCoinCall(bool right, bool mp)
        {
            var d = Bag(mp);
            d.CupCoinCallsMade++;
            if (right) d.CupCoinCallsRight++;
            Save();
        }

        /// <summary>Won the Final. `coop` marks a Co-op cup for the Team Player achievement.</summary>
        public static void RecordCupWon(string nation, bool mp, bool coop = false)
        {
            var d = Bag(mp);
            d.CupsWon++;
            if (coop) d.CupCoopWins++;
            var row = CupNationRow(d, nation, create: true);
            if (row != null) row.Won++;
            Save();
        }

        /// <summary>
        /// The furthest stage reached in a cup, kept as a best. `stageIndex` is the CupStage value
        /// (0 = Round of 32 .. 4 = Final); stored +1 so an untouched save reads 0 = never entered.
        /// Winning the Final is RecordCupWon; this only says the Final was reached.
        /// </summary>
        public static void RecordCupStage(int stageIndex, bool mp)
        {
            var d = Bag(mp);
            int v = Mathf.Clamp(stageIndex, 0, 4) + 1;
            if (v > d.CupBestStage) { d.CupBestStage = v; Save(); }
        }
    }
}
