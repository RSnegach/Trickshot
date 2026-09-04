using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// The cup's one door into the save file (design 9.7): a static facade over
    /// CareerStats.RecordCup* that also runs Achievements.CheckAll right after every write, which is
    /// this project's convention for unlocking (there is no event on CareerStats to hook). The
    /// director and the round driver call these; nothing in the cup touches CareerStats directly,
    /// so the bookkeeping rules - which bag, what counts as a giant kill, what "best stage" means -
    /// live in exactly one place.
    ///
    /// SP versus MP is decided by the cup's STYLE, never by Multiplayer.IsActive: Solo is the SP
    /// bag, Head to Head and Co-op the MP bag. Pass the director's Style (or its IsNetworked) so a
    /// solo cup started with a stale session can never land in the wrong column.
    ///
    /// What counts, per player:
    ///   - Entered: once per cup, when the draw is made (the nation is the player's own; in Co-op
    ///     it is the team's).
    ///   - A round: one bracket match the player was part of. Co-op: every teammate records it.
    ///   - A kick: only the ones THIS player took (Co-op shooters cycle, so a teammate's kick is
    ///     not yours). Saves / conceded: only the kicks THIS player kept against.
    ///   - Coin calls: every call the player made (the official caller's and the predictions
    ///     alike, design 6.11).
    ///   - Stage: the furthest stage reached, kept as a best; won: the Final, won.
    /// </summary>
    public static class CupCareer
    {
        /// <summary>Which stat bag a style writes to: Solo is single-player, the rest multiplayer.</summary>
        public static bool IsMp(CupStyle style) => style != CupStyle.Solo;

        /// <summary>A cup has started with this nation (call once, after the draw).</summary>
        public static void Entered(int nationIndex, CupStyle style)
        {
            CareerStats.RecordCupEntered(NationName(nationIndex), IsMp(style));
            Achievements.CheckAll();
        }

        /// <summary>
        /// A decided round the player was on `ownSide` of. Derives everything the achievements
        /// need from the round record itself: the winner, sudden death, the clean sheet (the other
        /// side scored nothing) and the giant kill (the opponent's table strength beat the player's
        /// own by CupTuning.GiantKillerMargin or more). `bracket` resolves the entrants' nations.
        /// </summary>
        public static void RoundDecided(CupRound round, CupSide ownSide, CupBracket bracket, CupStyle style)
        {
            if (round == null || !round.Done) return;
            var winner = round.WinnerSide;
            if (!winner.HasValue) return;
            bool won = winner.Value == ownSide;
            int own = round.ScoreOf(ownSide);
            int theirs = round.ScoreOf(CupSides.Other(ownSide));
            int ownStrength = StrengthOf(bracket, round.Entrant(ownSide));
            int theirStrength = StrengthOf(bracket, round.Entrant(CupSides.Other(ownSide)));
            RoundDecided(won, own, theirs, round.SuddenDeath, ownStrength, theirStrength, style);
        }

        /// <summary>The same, from bare numbers (a client that only has the wire tally).</summary>
        public static void RoundDecided(bool won, int ownScore, int theirScore, bool suddenDeath,
                                        int ownStrength, int theirStrength, CupStyle style)
        {
            bool cleanSheet = won && theirScore == 0;
            bool giantKill = won && ownStrength > 0 && theirStrength > 0
                          && theirStrength - ownStrength >= CupTuning.GiantKillerMargin;
            CareerStats.RecordCupRound(won, IsMp(style), suddenDeath: won && suddenDeath,
                                       cleanSheet: cleanSheet, giantKill: giantKill);
            Achievements.CheckAll();
        }

        /// <summary>One kick the player took: GOAL scores, SAVED and MISS do not.</summary>
        public static void KickTaken(KickOutcome outcome, CupStyle style)
        {
            CareerStats.RecordCupKick(outcome == KickOutcome.Goal, IsMp(style));
            Achievements.CheckAll();
        }

        /// <summary>One kick the player kept against: a GOAL is conceded, SAVED is a save, a MISS
        /// is neither (the shooter missed the goal, the keeper did nothing).</summary>
        public static void KickKept(KickOutcome outcome, CupStyle style)
        {
            if (outcome == KickOutcome.Goal) CareerStats.RecordCupConceded(IsMp(style));
            else if (outcome == KickOutcome.Saved) CareerStats.RecordCupSave(IsMp(style));
            else return;
            Achievements.CheckAll();
        }

        /// <summary>A HEADS/TAILS call, once the coin has landed.</summary>
        public static void CoinCalled(CoinFace call, CoinFace result, CupStyle style)
        {
            CareerStats.RecordCupCoinCall(call == result, IsMp(style));
            Achievements.CheckAll();
        }

        /// <summary>The player has a round in this stage (call as each stage opens; the best is kept).</summary>
        public static void StageReached(CupStage stage, CupStyle style)
        {
            CareerStats.RecordCupStage((int)stage, IsMp(style));
            Achievements.CheckAll();
        }

        /// <summary>Won the Final with this nation. Co-op counts toward Team Player as well.</summary>
        public static void Won(int nationIndex, CupStyle style)
        {
            CareerStats.RecordCupStage((int)CupStage.Final, IsMp(style));
            CareerStats.RecordCupWon(NationName(nationIndex), IsMp(style), coop: style == CupStyle.Coop);
            Achievements.CheckAll();
        }

        // ---- read side, for the stats page and the end cards ----

        /// <summary>"Round of 16" / "Final" / "-" from a ModeStats.CupBestStage (stage index + 1).</summary>
        public static string BestStageLabel(ModeStats d)
        {
            if (d == null || d.CupBestStage <= 0) return "-";
            var stage = CupStages.At(d.CupBestStage - 1);
            // A won cup outranks "reached the Final": the row should say so.
            if (stage == CupStage.Final && d.CupsWon > 0) return CupText.AchChampion;   // "Champion"
            return CupStages.Name(stage);
        }

        /// <summary>The stage a player has reached before, as a CupStage (null if never entered).</summary>
        public static CupStage? BestStage(ModeStats d)
        {
            if (d == null || d.CupBestStage <= 0) return null;
            return CupStages.At(d.CupBestStage - 1);
        }

        /// <summary>Is this stage further than any cup the player has reached before? (The KNOCKED
        /// OUT card's "career best" line, design 6.7.)</summary>
        public static bool BeatsBest(ModeStats d, CupStage stage)
            => d == null || d.CupBestStage <= 0 || (int)stage + 1 > d.CupBestStage;

        /// <summary>The nation entered most often ("Brazil (4)"), or "-" with no entries. Ties go to
        /// the first row recorded, which is the earliest pick.</summary>
        public static string MostEnteredNation(ModeStats d)
        {
            if (d == null || d.CupNations == null || d.CupNations.Count == 0) return "-";
            NationCups best = null;
            for (int i = 0; i < d.CupNations.Count; i++)
            {
                var row = d.CupNations[i];
                if (row == null || row.Entered <= 0) continue;
                if (best == null || row.Entered > best.Entered) best = row;
            }
            return best == null ? "-" : best.Nation + " (" + best.Entered + ")";
        }

        /// <summary>The player's wins with this nation (0 when never entered).</summary>
        public static int WinsWith(ModeStats d, int nationIndex)
        {
            var row = CareerStats.CupNationRow(d, NationName(nationIndex), create: false);
            return row == null ? 0 : row.Won;
        }

        // ---- helpers ----

        static string NationName(int nationIndex)
            => CupNationTable.IsValid(nationIndex) ? CupNationTable.NameOf(nationIndex) : "";

        static int StrengthOf(CupBracket bracket, int entrant)
        {
            if (bracket == null || !bracket.IsValidEntrant(entrant)) return 0;
            var nation = bracket.NationOf(entrant);
            return nation == null ? 0 : Mathf.Clamp(nation.Strength, CupTuning.StrengthMin, CupTuning.StrengthMax);
        }
    }
}
